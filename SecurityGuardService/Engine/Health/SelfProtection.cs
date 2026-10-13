using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Security.AccessControl;
using System.Security.Principal;
using System.Threading;
using SecurityGuard.Shared;
using SecurityGuardService.Core;
using SecurityGuardService.Core.Data;
using SecurityGuardService.Core.Logging;
using SecurityGuardService.Core.Settings;
using SecurityGuardService.Engine.Events;

namespace SecurityGuardService.Engine.Health
{
    /// <summary>
    /// SecurityGuard'ın kendini koruması (Genel Ayarlar → Servis koruması, hepsi varsayılan açık):
    /// - Servis izinleri: Administrators servisi başlatabilir / sorgulayabilir ama Hizmetler ekranı, sc stop / config / delete ile
    ///   DURDURAMAZ, AYARINI DEĞİŞTİREMEZ, SİLEMEZ (durdurma / kaldırma arayüzden, yönetici şifresiyle). İzinleri geri alma yolu
    ///   (WRITE_DAC) bilerek açık bırakılır; değiştirilirse 1 dk içinde geri yazılır ve "Guard.ServiceTamper" maili gider.
    /// - Veri klasörleri (Database, Logs) ve SQL yedek klasörü: sahibi SYSTEM, devralma kapalı; SYSTEM tam yetki, Administrators
    ///   sadece okuma. Yöneticinin / fidye yazılımının silmesi, şifrelemesi engellenir; servis (SYSTEM) normal yazar. Sahiplik
    ///   alınıp izin değiştirilirse geri yazılır ve "Guard.DataTamper" maili gider.
    /// - Kapanış: arayüzden / sunucu kapanırken durursa bilgi maili, başka yoldan durdurulursa kritik mail (kuyruk 10 sn boşaltılır).
    ///   İşlem öldürülürse (taskkill / çökme) Windows kurtarma ayarı yeniden başlatır; açılışta "beklenmedik kapanma" kritik maili
    ///   (son yaşam sinyali ile kapalı kalınan süre).
    /// Dürüst sınır: SYSTEM yetkisine yükselmiş saldırgan her şeyi değiştirebilir; amaç fark edilmeden yapılamaması.
    /// </summary>
    internal sealed class SelfProtection : IDisposable
    {
        private const string Module = "Koruma";
        private const string HeartbeatKey = "guard.heartbeat";
        private const string CleanStopKey = "guard.cleanStop";
        public static readonly TimeSpan Interval = TimeSpan.FromMinutes(1);

        // Servis hakları (SDDL): CC sorgula-ayar, DC ayar değiştir, LC durum, SW bağımlılar, RP başlat, WP durdur, DT duraklat,
        // LO sorgula, CR kullanıcı kontrolü, SD sil, RC izin oku, WD izin yaz, WO sahiplik
        internal const string ProtectedDacl = "D:(A;;CCDCLCSWRPWPDTLOCRSDRCWDWO;;;SY)(A;;CCLCSWRPLOCRRCWD;;;BA)(A;;CCLCSWLOCRRC;;;IU)(A;;CCLCSWLOCRRC;;;SU)(A;;CCLCSWLOCRRC;;;AU)";
        internal const string DefaultDacl = "D:(A;;CCLCSWRPWPDTLOCRRC;;;SY)(A;;CCDCLCSWRPWPDTLOCRSDRCWDWO;;;BA)(A;;CCLCSWLOCRRC;;;IU)(A;;CCLCSWLOCRRC;;;SU)";

        private static readonly SecurityIdentifier SystemSid = new SecurityIdentifier(WellKnownSidType.LocalSystemSid, null);
        private static readonly SecurityIdentifier Admins = new SecurityIdentifier(WellKnownSidType.BuiltinAdministratorsSid, null);

        private readonly GuardDatabase _db;
        private readonly SettingsStore _settings;
        private readonly EventRecorder _events;
        private readonly Func<string> _backupFolder;
        private readonly bool _isService;
        private readonly object _lock = new object();
        private readonly Dictionary<string, bool> _protected = new Dictionary<string, bool>(StringComparer.OrdinalIgnoreCase);
        private bool? _serviceProtected;
        private string _appliedDacl; // Windows'un geri okuduğu hali (biçim farkı yanlış alarm üretmesin)
        private string _lastBackupFolder;
        private readonly HashSet<string> _failedFolders = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        private Timer _timer;

        /// <summary>Korunan veri klasörleri (test için değiştirilebilir).</summary>
        internal Func<List<string>> DataFolders = () => new List<string> { AppPaths.DatabaseFolder, AppPaths.LogFolder };

        public SelfProtection(GuardDatabase db, SettingsStore settings, EventRecorder events, Func<string> backupFolder, bool isService)
        {
            _db = db;
            _settings = settings;
            _events = events;
            _backupFolder = backupFolder;
            _isService = isService;
        }

        /// <summary>Son durum (Servis sayfası).</summary>
        public string Status { get; private set; } = "Kontrol edilmedi.";

        public void Start()
        {
            CheckUnexpectedStop();
            _settings.Changed += key =>
            {
                if (key == SettingKeys.ProtectService || key == SettingKeys.ProtectData || key == SettingKeys.ProtectBackup)
                    ThreadPool.QueueUserWorkItem(_ => Tick());
            };
            _timer = new Timer(_ => Tick(), null, TimeSpan.FromSeconds(5), Interval);
        }

        public void Dispose()
        {
            var t = _timer;
            _timer = null;
            if (t != null) t.Dispose();
        }

        internal void Tick()
        {
            lock (_lock)
            {
                var status = new List<string>();
                try
                {
                    _settings.SetAsync(HeartbeatKey, DbTime.Now()).Wait(TimeSpan.FromSeconds(10));
                }
                catch (Exception ex)
                {
                    AppLog.Debug(Module, "Yaşam sinyali yazılamadı: " + ex.Message);
                }
                if (!_isService)
                {
                    // Geliştirme / konsol modunda servis kullanıcının hesabıyla çalışır: klasörü SYSTEM'e verirsek kendi DB'sine yazamaz
                    Status = "konsol modu: servis ve klasör koruması uygulanmaz.";
                    return;
                }
                Safe(status, "Servis izinleri", ServiceDacl);
                Safe(status, "Veri klasörleri", () => DataFolders().Select(f => FolderAcl(f, GeneralSettings.GetBool(_settings, SettingKeys.ProtectData), "veri klasörü")).ToList().All(x => x) ? "korunuyor" : null);
                Safe(status, "Yedek klasörü", BackupAcl);
                Status = string.Join("; ", status) + ".";
            }
        }

        private static void Safe(List<string> status, string what, Func<string> action)
        {
            try
            {
                string s = action();
                if (s != null) status.Add(what + ": " + s);
            }
            catch (Exception ex)
            {
                status.Add(what + ": HATA (" + ex.Message + ")");
                AppLog.Warn(Module, what + " uygulanamadı: " + ex.Message);
            }
        }

        // ================================================================ Servis izinleri

        private string ServiceDacl()
        {
            if (!_isService) return "konsol modu (uygulanmaz)";
            bool want = GeneralSettings.GetBool(_settings, SettingKeys.ProtectService);
            string current = ServiceSecurity.GetDacl(GuardConstants.ServiceName);
            bool isProtected = SameDacl(current, ProtectedDacl) || (_appliedDacl != null && SameDacl(current, _appliedDacl));
            if (want)
            {
                if (!isProtected)
                {
                    ServiceSecurity.SetDacl(GuardConstants.ServiceName, ProtectedDacl);
                    _appliedDacl = ServiceSecurity.GetDacl(GuardConstants.ServiceName);
                    if (_serviceProtected == true)
                        _events.Record(new GuardEvent("Guard.ServiceTamper", "SecurityGuard servisinin izinleri dışarıdan değiştirildi; koruma geri yazıldı.") { Source = "Servis koruması" }
                            .With("Bulunan", current).With("Geri yazılan", ProtectedDacl)
                            .With("Not", "Biri servisi durdurabilmek / silebilmek için izinleri açmış (sc sdset). Bunu siz yapmadıysanız sunucuda yetkisiz yönetici olabilir."));
                    else AppLog.Info(Module, "Servis koruması uygulandı (Windows'tan durdurma / ayar değiştirme / silme kapalı).");
                }
                _serviceProtected = true;
                return "korunuyor (sadece arayüzden durdurulur)";
            }
            if (isProtected)
            {
                ServiceSecurity.SetDacl(GuardConstants.ServiceName, DefaultDacl);
                AppLog.Info(Module, "Servis koruması kapatıldı (Windows varsayılan izinleri).");
            }
            _serviceProtected = false;
            return "KAPALI (Genel Ayarlar)";
        }

        internal static bool SameDacl(string a, string b)
        {
            Func<string, HashSet<string>> aces = s =>
            {
                int d = (s ?? "").IndexOf("D:", StringComparison.Ordinal);
                if (d < 0) return new HashSet<string>();
                string dacl = s.Substring(d + 2);
                int sacl = dacl.IndexOf("S:", StringComparison.Ordinal);
                if (sacl >= 0) dacl = dacl.Substring(0, sacl);
                return new HashSet<string>(dacl.Split(new[] { '(', ')' }, StringSplitOptions.RemoveEmptyEntries).Select(x => x.Trim()).Where(x => x.Length > 0), StringComparer.OrdinalIgnoreCase);
            };
            return aces(a).SetEquals(aces(b));
        }

        // ================================================================ Klasör izinleri

        private string BackupAcl()
        {
            string folder = null;
            try
            {
                folder = _backupFolder == null ? null : _backupFolder();
            }
            catch (Exception)
            {
                // yedek ayarı okunamadı
            }
            bool want = GeneralSettings.GetBool(_settings, SettingKeys.ProtectBackup);
            // Yedek klasörü değiştiyse eskisinin koruması kaldırılır
            if (_lastBackupFolder != null && !string.Equals(_lastBackupFolder, folder, StringComparison.OrdinalIgnoreCase) && Directory.Exists(_lastBackupFolder))
                FolderAcl(_lastBackupFolder, false, "eski yedek klasörü");
            _lastBackupFolder = folder;
            if (string.IsNullOrWhiteSpace(folder)) return "yedek klasörü tanımlı değil";
            string reason = UnsafeFolder(folder);
            if (reason != null) return "korunmuyor (" + reason + ")";
            if (!Directory.Exists(folder)) return "klasör yok";
            return FolderAcl(folder, want, "yedek klasörü") ? "korunuyor" : "KAPALI (Genel Ayarlar)";
        }

        /// <summary>Koruma uygulanmaması gereken klasörler (sürücü kökü, ağ yolu, Windows / Program Files).</summary>
        internal static string UnsafeFolder(string folder)
        {
            if (folder.StartsWith(@"\\", StringComparison.Ordinal)) return "ağ yolu";
            string full = Path.GetFullPath(folder).TrimEnd('\\');
            if (full.Length <= 2) return "sürücü kökü";
            foreach (var sys in new[] { Environment.SpecialFolder.Windows, Environment.SpecialFolder.ProgramFiles, Environment.SpecialFolder.ProgramFilesX86, Environment.SpecialFolder.UserProfile })
            {
                string s = Environment.GetFolderPath(sys).TrimEnd('\\');
                if (s.Length > 0 && (string.Equals(full, s, StringComparison.OrdinalIgnoreCase))) return "sistem klasörü";
            }
            if (string.Equals(full, Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData).TrimEnd('\\'), StringComparison.OrdinalIgnoreCase)) return "sistem klasörü";
            return null;
        }

        /// <summary>
        /// Klasörü korur (true) veya normale döndürür (false). Korunuyorken dışarıdan değiştirildiyse olay üretip geri yazar.
        /// </summary>
        /// <returns>Şu an korumalı mı.</returns>
        internal bool FolderAcl(string folder, bool protect, string what)
        {
            if (!Directory.Exists(folder)) return false;
            var current = Directory.GetAccessControl(folder, AccessControlSections.Access | AccessControlSections.Owner);
            bool isProtected = IsProtected(current);
            bool wasProtected;
            _protected.TryGetValue(folder, out wasProtected);

            if (protect)
            {
                if (!isProtected)
                {
                    var ds = new DirectorySecurity();
                    ds.SetAccessRuleProtection(true, false);
                    ds.AddAccessRule(new FileSystemAccessRule(SystemSid, FileSystemRights.FullControl, InheritanceFlags.ContainerInherit | InheritanceFlags.ObjectInherit, PropagationFlags.None, AccessControlType.Allow));
                    ds.AddAccessRule(new FileSystemAccessRule(Admins, FileSystemRights.ReadAndExecute, InheritanceFlags.ContainerInherit | InheritanceFlags.ObjectInherit, PropagationFlags.None, AccessControlType.Allow));
                    TrySetOwner(ds, SystemSid);
                    Directory.SetAccessControl(folder, ds);
                    if (!IsProtected(Directory.GetAccessControl(folder, AccessControlSections.Access | AccessControlSections.Owner)))
                    {
                        // Uygulanamadı (yetki / dosya sistemi): her dakika sahte "kurcalama" maili atılmasın
                        if (!_failedFolders.Contains(folder)) AppLog.Warn(Module, what + " korunamadı (izinler tam uygulanamadı): " + folder);
                        _failedFolders.Add(folder);
                        _protected[folder] = false;
                        return false;
                    }
                    _failedFolders.Remove(folder);
                    if (wasProtected)
                        _events.Record(new GuardEvent("Guard.DataTamper", "SecurityGuard " + what + " izinleri dışarıdan değiştirildi; koruma geri yazıldı: " + folder) { Source = "Klasör koruması" }
                            .With("Klasör", folder)
                            .With("Bulunan", Describe(current))
                            .With("Not", "Biri klasörün sahipliğini alıp izinleri açmış (takeown / icacls). Bunu siz yapmadıysanız sunucuda yetkisiz yönetici veya fidye yazılımı olabilir."));
                    else AppLog.Info(Module, what + " korundu (sahibi SYSTEM, yöneticiler sadece okur): " + folder);
                }
                _protected[folder] = true;
                return true;
            }
            if (isProtected)
            {
                var ds = new DirectorySecurity();
                ds.SetAccessRuleProtection(false, false); // üst klasörden devral, özel kurallar kalkar
                TrySetOwner(ds, Admins);
                Directory.SetAccessControl(folder, ds);
                AppLog.Info(Module, what + " koruması kaldırıldı (üst klasör izinleri): " + folder);
            }
            _protected[folder] = false;
            return false;
        }

        internal static bool IsProtected(DirectorySecurity ds)
        {
            if (!ds.AreAccessRulesProtected) return false;
            var owner = ds.GetOwner(typeof(SecurityIdentifier)) as SecurityIdentifier;
            if (owner != null && !owner.Equals(SystemSid)) return false;
            var rules = ds.GetAccessRules(true, true, typeof(SecurityIdentifier)).Cast<FileSystemAccessRule>().ToList();
            if (rules.Any(r => r.AccessControlType == AccessControlType.Allow && !r.IdentityReference.Equals(SystemSid) && !r.IdentityReference.Equals(Admins))) return false;
            var admin = rules.Where(r => r.IdentityReference.Equals(Admins) && r.AccessControlType == AccessControlType.Allow).Select(r => r.FileSystemRights).Aggregate((FileSystemRights)0, (x, y) => x | y);
            var forbidden = FileSystemRights.Write | FileSystemRights.Delete | FileSystemRights.DeleteSubdirectoriesAndFiles | FileSystemRights.ChangePermissions | FileSystemRights.TakeOwnership;
            return (admin & forbidden) == 0 && rules.Any(r => r.IdentityReference.Equals(SystemSid) && (r.FileSystemRights & FileSystemRights.FullControl) == FileSystemRights.FullControl);
        }

        private static void TrySetOwner(DirectorySecurity ds, SecurityIdentifier owner)
        {
            try
            {
                ds.SetOwner(owner);
            }
            catch (Exception)
            {
                // sahiplik verilemezse (yetki) sadece izinler uygulanır
            }
        }

        private static string Describe(DirectorySecurity ds)
        {
            var owner = ds.GetOwner(typeof(NTAccount));
            var rules = ds.GetAccessRules(true, true, typeof(NTAccount)).Cast<FileSystemAccessRule>()
                .Select(r => r.IdentityReference.Value + ": " + r.AccessControlType + " " + r.FileSystemRights).Distinct().Take(6);
            return "sahip " + (owner == null ? "?" : owner.Value) + "; " + string.Join("; ", rules);
        }

        // ================================================================ Kapanış / beklenmedik kapanma

        /// <summary>Açılışta: önceki çalışma temiz kapanmadıysa (öldürüldü / çöktü / elektrik) kritik olay.</summary>
        private void CheckUnexpectedStop()
        {
            string heartbeat = _settings.Get(HeartbeatKey);
            string clean = _settings.Get(CleanStopKey);
            if (heartbeat != null && clean == "0")
            {
                DateTime last;
                string lastText = heartbeat;
                try
                {
                    last = DbTime.FromDb(heartbeat);
                    lastText = last.ToLocalTime().ToString("dd.MM.yyyy HH:mm", CultureInfo.GetCultureInfo("tr-TR"));
                    var down = DateTime.UtcNow - last;
                    lastText += " (yaklaşık " + (down.TotalMinutes < 2 ? "1 dakika" : down.TotalHours >= 1 ? (int)down.TotalHours + " saat " + down.Minutes + " dakika" : (int)down.TotalMinutes + " dakika") + " kapalı kaldı)";
                }
                catch (FormatException)
                {
                }
                var boot = BootTime();
                bool rebooted = boot != null && heartbeat != null && boot.Value > DbTime.FromDb(heartbeat);
                _events.Record(new GuardEvent("Guard.UnexpectedStop", rebooted ? "Sunucu beklenmedik şekilde yeniden başladı; SecurityGuard düzgün kapanamadı."
                                                                                : "SecurityGuard servisi beklenmedik şekilde kapandı (sonlandırılmış veya çökmüş olabilir).")
                    { Source = "Servis koruması" }
                    .With("Son yaşam sinyali", lastText)
                    .With("Sunucu açılışı", boot == null ? null : boot.Value.ToLocalTime().ToString("dd.MM.yyyy HH:mm"))
                    .With("Not", rebooted ? "Elektrik kesintisi, mavi ekran veya zorla kapatma olabilir. Bu sürede olan olaylar Windows loglarından tekrar okundu."
                                          : "Servis işlemi öldürülmüş (taskkill) veya çökmüş; Windows kurtarma ayarı yeniden başlattı. Bunu siz yapmadıysanız sunucudaki oturumları kontrol edin."));
            }
            _settings.SetAsync(CleanStopKey, "0").Wait(TimeSpan.FromSeconds(10));
        }

        [DllImport("kernel32.dll")]
        private static extern ulong GetTickCount64();

        private static DateTime? BootTime()
        {
            try
            {
                return DateTime.UtcNow - TimeSpan.FromMilliseconds(GetTickCount64()); // UTC: yaşam sinyaliyle aynı saat diliminde karşılaştırılır
            }
            catch (Exception)
            {
                return null;
            }
        }

        /// <summary>Durdurulurken çağrılır (DB kapanmadan önce): olay + temiz kapanış işareti.</summary>
        public void OnStopping(string reason, bool byUi, bool shutdown)
        {
            try
            {
                if (byUi)
                    _events.Record(new GuardEvent("Guard.ServiceStopped", "SecurityGuard servisi durduruldu: " + reason) { Source = "Servis" }
                        .With("Not", "Servis durduğu sürece saldırılar engellenmez ve olaylar bildirilmez. Tekrar başlatmak: Hizmetler → SecurityGuard → Başlat."));
                else if (shutdown)
                    _events.Record(new GuardEvent("Guard.ServiceStopped", "Sunucu kapanıyor / yeniden başlıyor; SecurityGuard durduruluyor.") { Source = "Servis" });
                else
                    _events.Record(new GuardEvent("Guard.ServiceTamper", "SecurityGuard servisi arayüz dışından durduruldu!") { Source = "Servis koruması" }
                        .With("Not", "Servis Hizmetler ekranı / sc / net stop ile durduruldu (koruma kapalıysa mümkün). Bunu siz yapmadıysanız sunucuya yetkisiz erişim olabilir.")
                        .With("Koruma", GeneralSettings.GetBool(_settings, SettingKeys.ProtectService) ? "açık" : "KAPALI"));
                _settings.SetAsync(CleanStopKey, "1").Wait(TimeSpan.FromSeconds(5));
            }
            catch (Exception ex)
            {
                AppLog.Error(Module, "Kapanış kaydı yazılamadı.", ex);
            }
        }
    }

    /// <summary>Windows servis nesnesinin izinleri (advapi32).</summary>
    internal static class ServiceSecurity
    {
        private const int DaclInformation = 4;
        private const uint SC_MANAGER_CONNECT = 0x0001;
        private const uint READ_CONTROL = 0x00020000, WRITE_DAC = 0x00040000;

        [DllImport("advapi32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
        private static extern IntPtr OpenSCManager(string machine, string database, uint access);

        [DllImport("advapi32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
        private static extern IntPtr OpenService(IntPtr manager, string name, uint access);

        [DllImport("advapi32.dll", SetLastError = true)]
        private static extern bool CloseServiceHandle(IntPtr handle);

        [DllImport("advapi32.dll", SetLastError = true)]
        private static extern bool QueryServiceObjectSecurity(IntPtr service, int info, byte[] sd, int size, out int needed);

        [DllImport("advapi32.dll", SetLastError = true)]
        private static extern bool SetServiceObjectSecurity(IntPtr service, int info, byte[] sd);

        public static string GetDacl(string name)
        {
            return WithService(name, READ_CONTROL, h =>
            {
                int needed;
                QueryServiceObjectSecurity(h, DaclInformation, new byte[0], 0, out needed);
                var buffer = new byte[needed];
                if (!QueryServiceObjectSecurity(h, DaclInformation, buffer, buffer.Length, out needed)) throw new Win32Exception();
                return new RawSecurityDescriptor(buffer, 0).GetSddlForm(AccessControlSections.Access);
            });
        }

        public static void SetDacl(string name, string sddl)
        {
            WithService(name, READ_CONTROL | WRITE_DAC, h =>
            {
                var sd = new RawSecurityDescriptor(sddl);
                var bytes = new byte[sd.BinaryLength];
                sd.GetBinaryForm(bytes, 0);
                if (!SetServiceObjectSecurity(h, DaclInformation, bytes)) throw new Win32Exception();
                return (string)null;
            });
        }

        private static string WithService(string name, uint access, Func<IntPtr, string> action)
        {
            IntPtr scm = OpenSCManager(null, null, SC_MANAGER_CONNECT);
            if (scm == IntPtr.Zero) throw new Win32Exception();
            try
            {
                IntPtr svc = OpenService(scm, name, access);
                if (svc == IntPtr.Zero) throw new Win32Exception();
                try
                {
                    return action(svc);
                }
                finally
                {
                    CloseServiceHandle(svc);
                }
            }
            finally
            {
                CloseServiceHandle(scm);
            }
        }
    }
}
