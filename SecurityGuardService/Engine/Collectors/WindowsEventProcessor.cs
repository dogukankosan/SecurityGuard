using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Net;
using System.Security.Principal;
using System.Text.RegularExpressions;
using SecurityGuard.Shared;
using SecurityGuardService.Core.Logging;
using SecurityGuardService.Core.Net;
using SecurityGuardService.Engine.Ban;
using SecurityGuardService.Engine.Events;
using SecurityGuardService.Engine.Rules;
using SecurityGuardService.Sql;
using SecurityGuardService.Licensing;

namespace SecurityGuardService.Engine.Collectors
{
    /// <summary>
    /// Windows olaylarını SecurityGuard olaylarına çevirir: loglar / mail (kurala göre) ve başarısız girişleri ban
    /// motoruna sayar. Sadece okur; Windows'ta hiçbir ayarı değiştirmez.
    /// </summary>
    internal sealed class WindowsEventProcessor : IDisposable
    {
        private const string Module = "Toplayıcı";

        public const string ChSecurity = "Security";
        public const string ChSystem = "System";
        public const string ChApplication = "Application";
        public const string ChRdpCore = "Microsoft-Windows-RemoteDesktopServices-RdpCoreTS/Operational";
        public const string ChFirewall = "Microsoft-Windows-Windows Firewall With Advanced Security/Firewall";
        public const string ChDefender = "Microsoft-Windows-Windows Defender/Operational";

        /// <summary>
        /// RDP oturumları: 21 = yeni oturum, 25 = kopuk oturuma yeniden bağlanma (kaynak IP ile). Audit ayarından bağımsız,
        /// her sunucuda varsayılan yazılır. Security 4624 kullanılmaz: yeniden bağlanmada LogonType 7 gelir (VM testinde kaçtı).
        /// </summary>
        public const string ChRdpSessions = "Microsoft-Windows-TerminalServices-LocalSessionManager/Operational";

        private const string AdministratorsSid = "S-1-5-32-544";

        /// <summary>NT SERVICE\MpsSvc (Windows Firewall servisinin kendisi). Servis SID'i addan türetilir, her makinede aynıdır.</summary>
        private const string FirewallServiceSid = "S-1-5-80-3088073201-1464728630-1879813800-1107566885-823218052";

        /// <summary>Yeni kullanıcı oluşturulurken Windows'un otomatik yazdığı olaylar (etkinleştirme, şifre, None grubu) bu süre içinde yok sayılır.</summary>
        private static readonly TimeSpan CreationNoiseWindow = TimeSpan.FromSeconds(60);

        // Defender 5007: sadece güvenliği zayıflatan ayarlar raporlanır (istisna, koruma kapatma, tamper protection)
        private static readonly Regex DefenderRisky = new Regex(@"\\Exclusions\\|\\Disable[A-Za-z]+\s*=\s*0x1\b|TamperProtection|DisableAntiSpyware|DisableRealtimeMonitoring",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

        /// <summary>Aynı kullanıcı adına bu kadar farklı dış IP'den deneme = dağıtık tahmin (spraying).</summary>
        public const int SprayingIpCount = 5;
        public static readonly TimeSpan SprayingWindow = TimeSpan.FromHours(1);

        /// <summary>Son başarılı girişten beri en az bu kadar hatadan sonra gelen başarılı giriş Kritik sayılır.</summary>
        public const int SuccessAfterFailuresMin = 3;

        private static readonly Regex SqlClient = new Regex(@"\[CLIENT:\s*([^\]]+)\]", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

        private readonly EventRecorder _events;
        private readonly BanManager _ban;
        private readonly RdpNetPairing _pairing = new RdpNetPairing();
        private readonly Timer _pairingFlush;
        private readonly SprayingDetector _spraying = new SprayingDetector(SprayingIpCount, SprayingWindow);
        private readonly Action _auditTouched;

        /// <summary>Oturum geçmişi + mesai dışı kontrolü (yoksa sadece eski davranış).</summary>
        public Health.RdpSessionTracker Sessions { get; set; }

        /// <summary>Port kısıtı kuralımıza dokunan firewall olayı (nöbetçi kontrol etsin).</summary>
        public Action PortRuleTouched { get; set; }

        // Yeni oluşturulan kullanıcılar (SID → ad, zaman): oluşturma gürültüsünü bastırmak ve silinmiş kullanıcının adını göstermek için
        private readonly Dictionary<string, KeyValuePair<string, DateTime>> _created =
            new Dictionary<string, KeyValuePair<string, DateTime>>(StringComparer.OrdinalIgnoreCase);

        /// <param name="auditTouched">SQL audit nesnemize dışarıdan dokunulunca (nöbetçi hemen kontrol etsin).</param>
        public WindowsEventProcessor(EventRecorder events, BanManager ban, Action auditTouched = null)
        {
            _events = events;
            _ban = ban;
            _auditTouched = auditTouched;
            _pairingFlush = new Timer(_ => FlushPairing(), null, TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(1));
        }

        public void Dispose()
        {
            _pairingFlush.Dispose();
            FlushPairing(true);
        }

        /// <summary>RDP eşi gelmeyen ağ girişi hataları SMB / WinRM olarak sayılır.</summary>
        internal void FlushPairing(bool all = false)
        {
            foreach (var a in _pairing.TakeDue(all))
            {
                try
                {
                    a();
                }
                catch (Exception ex)
                {
                    AppLog.Error("Olay", "Ağ girişi hatası işlenemedi.", ex);
                }
            }
        }

        /// <summary>
        /// Log dosyasından gelen başarısız giriş (IIS FTP / web / SMTP). Windows olaylarıyla aynı yoldan sayılır ve banlanır.
        /// </summary>
        public void ExternalFailure(string protocol, string ip, string user, DateTime timeUtc, string source, string summary, params string[] details)
        {
            if (!LicenseGate.Active) return;
            string type;
            switch (protocol)
            {
                case BanManager.ProtocolFtp: type = EventCatalog.FtpLoginFailed; break;
                case BanManager.ProtocolWeb: type = EventCatalog.WebLoginFailed; break;
                case BanManager.ProtocolSmtp: type = EventCatalog.SmtpLoginFailed; break;
                default: throw new ArgumentException("Bilinmeyen protokol: " + protocol);
            }
            ip = CleanIp(ip);
            if (ip == null) return;
            Failure(timeUtc, ip, string.IsNullOrEmpty(user) || user == "-" ? null : user, protocol, type, source, summary, details);
        }

        public void Process(RawEvent e)
        {
            if (!LicenseGate.Active) return; // lisans yok: olaylar okunur ama işlenmez
            switch (e.Channel)
            {
                case ChSecurity: Security(e); break;
                case ChSystem: SystemLog(e); break;
                case ChApplication: Application(e); break;
                case ChRdpCore: if (e.EventId == 140) RdpCoreFailed(e); break;
                case ChFirewall: Firewall(e); break;
                case ChDefender: Defender(e); break;
                case ChRdpSessions: RdpSession(e); break;
            }
        }

        // ================================================================ Security

        private void Security(RawEvent e)
        {
            switch (e.EventId)
            {
                case 4625: LogonFailed(e); break;
                case 4688: ProcessCreated(e); break;
                case 4720:
                    RememberCreated(e);
                    Record(e, "Win.UserCreated", "Yeni Windows kullanıcısı oluşturuldu: " + Account(e, "Target"), e.Get("TargetUserName"),
                        "Oluşturan", Account(e, "Subject"));
                    break;
                case 4722:
                case 4725:
                    if (e.EventId == 4722 && JustCreated(e.Get("TargetSid"), e.TimeUtc)) break; // oluşturmanın parçası
                    Record(e, "Win.UserEnabledDisabled",
                        "Windows kullanıcısı " + (e.EventId == 4722 ? "etkinleştirildi" : "devre dışı bırakıldı") + ": " + Account(e, "Target"),
                        e.Get("TargetUserName"), "Yapan", Account(e, "Subject"));
                    break;
                case 4723:
                case 4724:
                    if (e.EventId == 4724 && JustCreated(e.Get("TargetSid"), e.TimeUtc)) break; // ilk şifre atama
                    Record(e, "Win.PasswordReset",
                        (e.EventId == 4724 ? "Windows kullanıcı şifresi sıfırlandı: " : "Windows kullanıcısı şifresini değiştirdi: ") + Account(e, "Target"),
                        e.Get("TargetUserName"), "Yapan", Account(e, "Subject"));
                    break;
                case 4728:
                case 4732:
                case 4756:
                    GroupMemberAdded(e);
                    break;
                case 4698:
                    Record(e, "Win.ScheduledTaskCreated", "Zamanlanmış görev oluşturuldu: " + e.Get("TaskName"), e.Get("SubjectUserName"),
                        "Oluşturan", Account(e, "Subject"), "Görev", e.Get("TaskName"));
                    break;
                case 1102:
                    Record(e, "Win.EventLogCleared", "Windows Security logu temizlendi.", e.Get("SubjectUserName"),
                        "Temizleyen", Account(e, "Subject"), "Oturum (LogonId)", e.Get("SubjectLogonId"),
                        "Not", "Temizlemeden önceki olaylar SecurityGuard'da kayıtlı kalır.");
                    break;
                case 4719:
                {
                    string sub = AuditSubcategoryName(e.Get("SubcategoryGuid"));
                    string change = AuditChangeText(e.Get("AuditPolicyChanges"));
                    bool removed = change.Contains("kaldırıldı");
                    Record(e, "Win.AuditPolicyChanged", "Windows audit politikası değişti: " + sub + " → " + change + "." + (removed ? " Kayıt KAPATILDI: bu olaylar artık görülmeyecek." : ""),
                        e.Get("SubjectUserName"),
                        "Değiştiren", Account(e, "Subject"), "Alt kategori", sub, "Değişiklik", change,
                        "Not", removed ? "Saldırganlar izlerini gizlemek için audit kaydını kapatır. Bunu siz yapmadıysanız Güvenlik Önerileri → Windows audit politikası maddesindeki script ile geri açın."
                                       : "Kayıt açıldı / genişletildi.");
                    break;
                }
            }
        }

        /// <summary>İşlem oluşturma: sadece fidye yazılımı öncesi komutlar olay olur (diğer binlerce işlem yok sayılır).</summary>
        private void ProcessCreated(RawEvent e)
        {
            string process = e.Get("NewProcessName");
            string command = e.Get("CommandLine");
            string what = RansomwareIndicators.Match(process, command);
            if (what == null) return;
            Record(e, "Win.RansomwarePrecursor", what + ": " + (string.IsNullOrEmpty(command) ? process : command), e.Get("SubjectUserName"),
                "Komut", string.IsNullOrEmpty(command) ? process : command,
                "Çalıştıran", Account(e, "Subject"),
                "Üst işlem", e.Get("ParentProcessName"),
                "Ne yapmalı", "Bunu siz / bir yönetim aracı yapmadıysanız fidye yazılımı şifrelemeye başlamak üzere olabilir: sunucunun ağ bağlantısını hemen kesin "
                              + "(ağ kablosu / sanal ağ kartı), RDP oturumlarını ve yönetici hesaplarını kontrol edin.");
        }

        /// <summary>
        /// 4625: giriş tipi 10 → RDP. Giriş tipi 3 (ağ) hem NLA'lı RDP'de hem SMB / WinRM'de gelir: aynı IP'den 5 sn içinde
        /// RdpCoreTS 140 gelirse RDP denemesidir (140 sayılır), gelmezse SMB / WinRM olarak sayılır.
        /// IIS (w3wp.exe) kaynaklı hatalar IIS logundan sayıldığı için burada atlanır (iki kez sayılmasın).
        /// </summary>
        private void LogonFailed(RawEvent e)
        {
            string logonType = e.Get("LogonType");
            if (logonType != "3" && logonType != "10") return; // sadece ağ / RDP girişleri

            string ip = CleanIp(e.Get("IpAddress"));
            if (ip == null) return; // NLA: IP gelmez, RdpCoreTS 140 sayar
            string user = e.Get("TargetUserName");
            string shownUser = string.IsNullOrEmpty(user) ? "(boş kullanıcı)" : user;
            string reason = e.Get("SubStatus") ?? e.Get("Status");
            string process = e.Get("ProcessName") ?? "-";

            if (logonType == "10")
            {
                if (!_pairing.OnRdp4625(ip, e.TimeUtc)) return;
                Failure(e, ip, user, BanManager.ProtocolRdp, EventCatalog.RdpLoginFailed, "Security 4625",
                    "RDP girişi başarısız: " + shownUser + " (" + ip + ")",
                    "Giriş tipi", "RDP (10)", "Sebep kodu", reason, "İstemci adı", e.Get("WorkstationName"));
                return;
            }

            if (process.EndsWith(@"\w3wp.exe", StringComparison.OrdinalIgnoreCase)) return;

            var time = e.TimeUtc;
            string workstation = e.Get("WorkstationName");
            string package = e.Get("AuthenticationPackageName");
            _pairing.OnNet4625(ip, time, () => Failure(time, ip, user, BanManager.ProtocolNet, EventCatalog.NetLoginFailed, "Security 4625",
                "Ağ girişi başarısız (uzak RDP/NLA ya da SMB / WinRM): " + shownUser + " (" + ip + ")",
                "Giriş tipi", "Ağ (3)", "Sebep kodu", reason, "İstemci adı", workstation, "Kimlik doğrulama", package,
                "Program", process == "-" ? null : process,
                "Not", "Ağ üzerinden Windows kimlik doğrulaması: başka bir makineden NLA ile RDP denemesi (uzaktan RDP'de hata tip 3 olarak düşer, RdpCoreTS olayı gelmez), dosya paylaşımı (SMB) veya uzaktan yönetim (WinRM / PowerShell) olabilir."));
        }

        private void RdpCoreFailed(RawEvent e)
        {
            string ip = CleanIp(e.Get("ClientIP"));
            if (ip == null) return;
            if (!_pairing.On140(ip, e.TimeUtc)) return;
            Failure(e, ip, null, BanManager.ProtocolRdp, EventCatalog.RdpLoginFailed, "RdpCoreTS 140",
                "RDP girişi başarısız (kullanıcı adı / şifre hatalı) (" + ip + ")");
        }

        /// <summary>RDP oturumu açıldı (21) veya kopuk oturuma yeniden bağlanıldı (25). Konsol girişleri (LOCAL) sayılmaz.</summary>
        private void RdpSession(RawEvent e)
        {
            string account = e.Get("User") ?? "(bilinmiyor)";
            int sessionId;
            int.TryParse(e.Get("SessionID"), NumberStyles.Integer, CultureInfo.InvariantCulture, out sessionId);
            var tracker = Sessions;
            if (e.EventId == 23 || e.EventId == 24)
            {
                if (tracker == null) return;
                if (e.EventId == 23) tracker.OnLogoff(sessionId, account, e.TimeUtc);
                else tracker.OnDisconnect(sessionId, account, e.TimeUtc);
                return;
            }
            if (e.EventId != 21 && e.EventId != 25) return;

            string ip = CleanIp(e.Get("Address")); // "LOCAL" → null (konsol)
            bool reconnect = e.EventId == 25;
            string offHours = null;
            if (tracker != null)
            {
                try
                {
                    offHours = tracker.OnConnect(sessionId, account, ip, e.TimeUtc, reconnect);
                }
                catch (Exception ex)
                {
                    AppLog.Error("Oturum", "Oturum geçmişine yazılamadı.", ex);
                }
            }
            if (ip == null) return;
            int slash = account.LastIndexOf('\\');
            string user = slash >= 0 ? account.Substring(slash + 1) : account;
            if (offHours != null)
                Record(e, "Rdp.OffHoursLogin", "Mesai dışı RDP " + (reconnect ? "bağlantısı" : "girişi") + ": " + account + " (" + ip + ")", user, ip: ip,
                    details: new[] { "Zaman", offHours, "Ülke", tracker.CountryText(ip), "Oturum", e.Get("SessionID"),
                        "Not", "Bu girişi siz / ilgili kişi yapmadıysanız oturumu kapatın ve hesabın şifresini değiştirin. Kişiye özel mesai istisnası: İzleme → RDP Oturumları → Mesai istisnaları." });

            Record(e, "Rdp.SessionStarted", (reconnect ? "RDP oturumuna yeniden bağlanıldı: " : "RDP oturumu açıldı: ") + account + " (" + ip + ")", user,
                ip: ip, details: new[] { "Oturum", e.Get("SessionID") });

            var before = _ban.RegisterSuccessAsync(ip, BanManager.ProtocolRdp, user).Result;
            if (before.FailsSinceSuccess >= SuccessAfterFailuresMin)
                Record(e, "Login.SuccessAfterFailures",
                    ip + " adresinden " + before.FailsSinceSuccess + " başarısız denemeden sonra başarılı RDP girişi: " + account, user, ip: ip,
                    details: new[] { "Toplam başarısız deneme", before.FailCount.ToString(), "Öneri", "Bu girişi siz yapmadıysanız hesabın şifresini hemen değiştirin ve oturumu kapatın." });
            if (before.SuccessCount == 0 && !IsLoopback(ip))
                Record(e, "Rdp.LoginNewIp", "Daha önce görülmemiş IP'den RDP girişi: " + account + " (" + ip + ")", user, ip: ip);
        }

        private static readonly Regex OddServicePath = new Regex(@"\\(temp|tmp|appdata|downloads|users\\public|perflogs)\\|\\programdata\\(?!microsoft\\windows defender)|^[a-z]:\\[^\\]+\.exe",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
        private static readonly Regex ScriptHost = new Regex(@"cmd(\.exe)?\s+/c|powershell|pwsh|rundll32|mshta|regsvr32|wscript|cscript|certutil|bitsadmin|-enc\b|frombase64",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

        /// <summary>Yeni servis şüpheli mi (uzaktan erişim aracı, geçici / kullanıcı klasörü, komut satırı ile çalışan servis).</summary>
        internal static string ServiceSuspicion(string name, string imagePath)
        {
            var reasons = new List<string>();
            string tool = Health.RemoteTools.Match(name, imagePath);
            if (tool != null) reasons.Add("uzaktan erişim aracı (" + tool + ")");
            if (!string.IsNullOrEmpty(imagePath) && OddServicePath.IsMatch(imagePath)) reasons.Add("olağandışı klasörden çalışıyor (Temp / AppData / ProgramData / kök dizin)");
            if (!string.IsNullOrEmpty(imagePath) && ScriptHost.IsMatch(imagePath)) reasons.Add("komut satırı / betik ile çalışıyor");
            return reasons.Count == 0 ? null : string.Join("; ", reasons);
        }

        private void GroupMemberAdded(RawEvent e)
        {
            // Kullanıcının birincil grubu (None / Domain Users, RID 513): Windows her yeni kullanıcıda otomatik ekler
            string groupSid = e.Get("TargetSid") ?? "";
            if (groupSid.EndsWith("-513", StringComparison.Ordinal)) return;

            string group = Account(e, "Target");
            string member = MemberName(e);
            bool admins = string.Equals(e.Get("TargetSid"), AdministratorsSid, StringComparison.OrdinalIgnoreCase);
            Record(e, admins ? "Win.AdminGroupAdded" : "Win.GroupMemberAdded",
                (admins ? "Administrators grubuna üye eklendi: " : "Gruba üye eklendi: ") + member + " → " + group, e.Get("SubjectUserName"),
                "Grup", group, "Eklenen", member, "Ekleyen", Account(e, "Subject"));
        }

        // ================================================================ System / Application

        private void SystemLog(RawEvent e)
        {
            if (e.EventId == 7045)
            {
                string suspicion = ServiceSuspicion(e.Get("ServiceName"), e.Get("ImagePath"));
                Record(e, "Win.ServiceInstalled", (suspicion != null ? "ŞÜPHELİ servis kuruldu: " : "Yeni Windows servisi kuruldu: ") + e.Get("ServiceName"), e.Get("AccountName"),
                    "Servis", e.Get("ServiceName"), "Dosya", e.Get("ImagePath"), "Başlangıç", e.Get("StartType"), "Hesap", e.Get("AccountName"),
                    "Neden şüpheli", suspicion,
                    "Ne yapmalı", suspicion == null ? null : "Saldırganlar araçlarını servis olarak kurar. Bu servisi siz / bir kurulum programı eklemediyseniz servisi durdurup devre dışı bırakın ve dosyayı inceleyin.");
            }
            else if (e.EventId == 104 && string.Equals(e.Provider, "Microsoft-Windows-Eventlog", StringComparison.OrdinalIgnoreCase))
            {
                string channel = e.Get("Channel");
                Record(e, "Win.EventLogCleared", "Windows olay logu temizlendi: " + channel, e.Get("SubjectUserName"),
                    "Log", channel, "Temizleyen", Join(e.Get("SubjectDomainName"), e.Get("SubjectUserName")));
            }
        }

        private void Application(RawEvent e)
        {
            if (e.Provider == null || !(e.Provider.Equals("MSSQLSERVER", StringComparison.OrdinalIgnoreCase)
                                        || e.Provider.StartsWith("MSSQL$", StringComparison.OrdinalIgnoreCase))) return;
            if (e.EventId == 33205)
            {
                SqlAudit(e);
                return;
            }
            if (e.EventId != 18456) return;

            string user = e.Values.Count > 0 ? e.Values[0] : null;
            string reason = e.Values.Count > 1 ? e.Values[1] : null;
            string client = e.Values.Select(v => SqlClient.Match(v ?? "")).Where(m => m.Success).Select(m => m.Groups[1].Value.Trim()).FirstOrDefault();
            string ip = client == null || client.StartsWith("<", StringComparison.Ordinal) ? "127.0.0.1" : CleanIp(client); // <local machine>
            if (ip == null) return;

            bool guardAccount = string.Equals(user, GuardConstants.SqlLoginName, StringComparison.OrdinalIgnoreCase);
            if (guardAccount && _ban.IsProtected(ip)) return; // securityGuardWar yerel denemeleri sayılmaz

            if (guardAccount)
                Record(e, "Login.GuardAccountExternal", "Dış IP'den " + GuardConstants.SqlLoginName + " ile SQL girişi denendi (" + ip + ")", user, ip: ip,
                    details: new[] { "Instance", Instance(e.Provider), "Öneri", "Bu hesap sadece sunucu içinden kullanılır. Şifresini değiştirin." });

            Failure(e, ip, user, BanManager.ProtocolSql, EventCatalog.SqlLoginFailed, "SQL 18456 (" + Instance(e.Provider) + ")",
                "SQL girişi başarısız: " + (string.IsNullOrEmpty(user) ? "(boş)" : user) + " (" + ip + ")",
                "Instance", Instance(e.Provider), "Sebep", reason);
        }

        /// <summary>SQL Server Audit kaydı (33205): login / yetki / sertifika / veritabanı değişiklikleri.</summary>
        private void SqlAudit(RawEvent e)
        {
            string text = string.Join("\n", e.Values.Concat(e.Data.Values));
            var fields = SqlAuditEvents.Parse(text);
            var m = SqlAuditEvents.Map(fields, Instance(e.Provider));
            if (m == null) return;
            if (m.Config != null)
            {
                // Kim yaptı bilgisi saklanır ve ayar karşılaştırması hemen tetiklenir (hangi ayarın değiştiğini o bulur)
                m.Config.Instance = Instance(e.Provider);
                m.Config.TimeUtc = e.TimeUtc;
                SqlConfigTrail.Note(m.Config);
            }
            if (m.EventType == null) return;

            var ev = new GuardEvent(m.EventType, m.Summary)
            {
                TimeUtc = e.TimeUtc,
                Source = "SQL Audit (" + Instance(e.Provider) + ")",
                UserName = m.UserName,
                IpAddress = m.IpAddress
            };
            foreach (var d in m.Details) ev.With(d.Key, d.Value);
            _events.Record(ev);

            if (m.TouchesOurAudit && _auditTouched != null) _auditTouched();
        }

        // ================================================================ Firewall / Defender

        private void Firewall(RawEvent e)
        {
            string rule = e.Get("RuleName");
            if (rule != null && WindowsBanFirewall.IsOurs(rule))
            {
                // Kendi ban kuralımız: olay üretilmez; kural DB ile karşılaştırılır, silinmiş / değiştirilmişse onarılır
                // ("Ban firewall kuralı silindi / değiştirildi" olayını senkron üretir). Bizim yazmamız fark bulmaz, bir şey olmaz.
                _ban.RequestSync();
                return;
            }
            if (rule != null && Engine.Firewall.WindowsPortFirewall.IsOurs(rule))
            {
                // Kendi port kısıtı kuralımız: aynı şekilde DB ile karşılaştırılır
                var touched = PortRuleTouched;
                if (touched != null) touched();
                return;
            }
            // Firewall servisinin kendisi (ağ profili değişince yerleşik kuralları yeniden uygular): gürültü
            if (string.Equals(e.Get("ModifyingUser"), FirewallServiceSid, StringComparison.OrdinalIgnoreCase)) return;

            string action;
            switch (e.EventId)
            {
                case 2004: case 2097: action = "eklendi"; break;
                case 2005: case 2099: action = "değiştirildi"; break;
                case 2006: case 2052: action = "silindi"; break;
                case 2003: FirewallSetting(e); return;
                default: return;
            }

            Record(e, "Win.FirewallRuleChanged", "Firewall kuralı " + action + ": " + rule, null,
                "Kural", rule, "Yerel port", e.Get("LocalPorts"), "Uzak adres", e.Get("RemoteAddresses"),
                "Yapan uygulama", e.Get("ModifyingApplication"), "Yapan kullanıcı", SidName(e.Get("ModifyingUser")));
        }

        private void FirewallSetting(RawEvent e)
        {
            // SettingType 1 = firewall açık/kapalı. Değer 0 → kapatıldı.
            if (e.Get("SettingType") != "1") return;
            string value = (e.Get("SettingValue") ?? "").Trim().TrimStart('0', 'x');
            if (value.Length != 0) return;
            string profile = FirewallProfile(e.Get("Profiles") ?? e.Get("ProfileChanged"));
            Record(e, "Win.FirewallDisabled", "Windows Firewall kapatıldı (profil: " + profile + ")", null,
                "Profil", profile, "Yapan uygulama", e.Get("ModifyingApplication"), "Yapan kullanıcı", SidName(e.Get("ModifyingUser")),
                "Etki", "Firewall kapalıyken ban kuralları çalışmaz.");
        }

        /// <summary>Firewall 2003 olayındaki profil bit maskesi: 1 Etki alanı, 2 Özel, 4 Genel.</summary>
        internal static string FirewallProfile(string value)
        {
            int mask;
            if (string.IsNullOrWhiteSpace(value)) return "bilinmiyor";
            if (!int.TryParse(value.Trim(), System.Globalization.NumberStyles.Integer, System.Globalization.CultureInfo.InvariantCulture, out mask)) return value.Trim();
            var names = new List<string>();
            if ((mask & 1) != 0) names.Add("Etki alanı (Domain)");
            if ((mask & 2) != 0) names.Add("Özel (Private)");
            if ((mask & 4) != 0) names.Add("Genel (Public)");
            return names.Count == 0 ? value.Trim() : string.Join(", ", names);
        }

        private void Defender(RawEvent e)
        {
            string text;
            switch (e.EventId)
            {
                case 5001: text = "Defender gerçek zamanlı koruma kapatıldı."; break;
                case 5010: text = "Defender kötü amaçlı yazılım taraması kapatıldı."; break;
                case 5012: text = "Defender virüs taraması kapatıldı."; break;
                case 5007:
                    string changed = (e.Get("New Value") ?? "") + " " + (e.Get("Old Value") ?? "");
                    if (!DefenderRisky.IsMatch(changed)) return; // imza / platform güncellemesi gibi rutin değişiklikler
                    text = changed.IndexOf("\\Exclusions\\", StringComparison.OrdinalIgnoreCase) >= 0
                        ? "Defender'a istisna (exclusion) eklendi / değişti."
                        : "Defender koruma ayarı değişti (koruma kapatılmış olabilir).";
                    break;
                default: return;
            }
            Record(e, "Win.DefenderChanged", text, null, "Eski değer", e.Get("Old Value"), "Yeni değer", e.Get("New Value"));
        }

        // ================================================================ Ortak

        /// <summary>Başarısız giriş: olay kaydı + (eşleşme yoksa) ban sayacı + dağıtık deneme kontrolü.</summary>
        private void Failure(RawEvent e, string ip, string user, string protocol, string eventType, string source, string summary, params string[] details)
        {
            Failure(e.TimeUtc, ip, user, protocol, eventType, source, summary, details);
        }

        private void Failure(DateTime timeUtc, string ip, string user, string protocol, string eventType, string source, string summary, params string[] details)
        {
            var ev = new GuardEvent(eventType, summary) { TimeUtc = timeUtc, Source = source, UserName = user, IpAddress = ip };
            AddDetails(ev, details);
            _events.Record(ev);
            bool isProtected = _ban.IsProtected(ip);
            int n = _ban.RegisterFailureAsync(ip, protocol, user).Result;
            if (isProtected) return;

            if (!string.IsNullOrEmpty(user) && _spraying.Add(user, ip, timeUtc))
            {
                var spray = new GuardEvent("Login.Spraying",
                    "'" + user + "' kullanıcı adına son " + (int)SprayingWindow.TotalMinutes + " dakikada " + SprayingIpCount + " farklı dış IP'den giriş denendi.")
                { TimeUtc = timeUtc, Source = source, UserName = user, IpAddress = ip };
                spray.With("Protokol", protocol).With("IP'ler", string.Join(", ", _spraying.IpsFor(user)))
                     .With("Öneri", "Dağıtık şifre tahmini. Bu kullanıcı adını yeniden adlandırmayı / devre dışı bırakmayı değerlendirin.");
                _events.Record(spray);
            }
        }

        private void Record(RawEvent e, string type, string summary, string user, params string[] details)
        {
            Record(e, type, summary, user, null, details);
        }

        private void Record(RawEvent e, string type, string summary, string user, string ip = null, string[] details = null)
        {
            var ev = new GuardEvent(type, summary)
            {
                TimeUtc = e.TimeUtc,
                Source = SourceName(e),
                UserName = string.IsNullOrEmpty(user) || user == "-" ? null : user,
                IpAddress = ip
            };
            AddDetails(ev, details);
            _events.Record(ev);
        }

        private static void AddDetails(GuardEvent ev, string[] details)
        {
            if (details == null) return;
            for (int i = 0; i + 1 < details.Length; i += 2)
                if (!string.IsNullOrWhiteSpace(details[i + 1]) && details[i + 1] != "-") ev.With(details[i], details[i + 1]);
        }

        private static string SourceName(RawEvent e)
        {
            switch (e.Channel)
            {
                case ChRdpCore: return "RdpCoreTS " + e.EventId;
                case ChFirewall: return "Firewall " + e.EventId;
                case ChDefender: return "Defender " + e.EventId;
                case ChRdpSessions: return "RDP oturumu (" + (e.EventId == 25 ? "yeniden bağlanma" : "yeni oturum") + ", olay " + e.EventId + ")";
                default: return e.Channel + " " + e.EventId;
            }
        }

        private static readonly System.Collections.Generic.Dictionary<string, string> AuditSubcategories =
            new System.Collections.Generic.Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            { "0cce9215-69ae-11d9-bed3-505054503030", "Oturum açma (Logon)" },
            { "0cce9216-69ae-11d9-bed3-505054503030", "Oturum kapatma (Logoff)" },
            { "0cce9217-69ae-11d9-bed3-505054503030", "Hesap kilitleme" },
            { "0cce921b-69ae-11d9-bed3-505054503030", "Özel oturum açma (Special Logon)" },
            { "0cce921d-69ae-11d9-bed3-505054503030", "Dosya sistemi" },
            { "0cce9227-69ae-11d9-bed3-505054503030", "Diğer nesne erişimi (zamanlanmış görevler)" },
            { "0cce922b-69ae-11d9-bed3-505054503030", "İşlem oluşturma (Process Creation)" },
            { "0cce922f-69ae-11d9-bed3-505054503030", "Audit politikası değişikliği" },
            { "0cce9232-69ae-11d9-bed3-505054503030", "Firewall kural değişikliği (MPSSVC)" },
            { "0cce9235-69ae-11d9-bed3-505054503030", "Kullanıcı hesabı yönetimi" },
            { "0cce9237-69ae-11d9-bed3-505054503030", "Güvenlik grubu yönetimi" },
            { "0cce9211-69ae-11d9-bed3-505054503030", "Güvenlik sistemi uzantısı" },
            { "0cce9212-69ae-11d9-bed3-505054503030", "Sistem bütünlüğü" },
            { "0cce9242-69ae-11d9-bed3-505054503030", "Kerberos kimlik doğrulama" },
            { "0cce923f-69ae-11d9-bed3-505054503030", "Kimlik bilgisi doğrulama" },
        };

        private static string AuditSubcategoryName(string guid)
        {
            string key = (guid ?? "").Trim('{', '}', ' ');
            string name;
            return AuditSubcategories.TryGetValue(key, out name) ? name : (string.IsNullOrEmpty(guid) ? "(bilinmiyor)" : guid);
        }

        /// <summary>4719 "AuditPolicyChanges" kodları (%%8448...): birden fazla olabilir.</summary>
        private static string AuditChangeText(string raw)
        {
            if (string.IsNullOrEmpty(raw)) return "(bilinmiyor)";
            var parts = new System.Collections.Generic.List<string>();
            foreach (var code in raw.Split(new[] { ',', ' ' }, StringSplitOptions.RemoveEmptyEntries))
            {
                switch (code.Trim())
                {
                    case "%%8448": parts.Add("başarılı denetim kaldırıldı"); break;
                    case "%%8449": parts.Add("başarılı denetim eklendi"); break;
                    case "%%8450": parts.Add("başarısız denetim kaldırıldı"); break;
                    case "%%8451": parts.Add("başarısız denetim eklendi"); break;
                    default: parts.Add(code.Trim()); break;
                }
            }
            return string.Join(", ", parts);
        }

        private static string Account(RawEvent e, string prefix)
        {
            return Join(e.Get(prefix + "DomainName"), e.Get(prefix + "UserName"));
        }

        private static string Join(string domain, string user)
        {
            if (string.IsNullOrEmpty(user) || user == "-") return "(bilinmiyor)";
            return string.IsNullOrEmpty(domain) || domain == "-" ? user : domain + "\\" + user;
        }

        private string MemberName(RawEvent e)
        {
            string name = e.Get("MemberName");
            if (!string.IsNullOrEmpty(name) && name != "-") return name;
            string sid = e.Get("MemberSid");
            KeyValuePair<string, DateTime> created;
            if (sid != null && _created.TryGetValue(sid, out created)) return created.Key; // kullanıcı sonradan silinmiş olsa da adı bilinir
            return SidName(sid) ?? "(bilinmiyor)";
        }

        private void RememberCreated(RawEvent e)
        {
            string sid = e.Get("TargetSid");
            if (string.IsNullOrEmpty(sid)) return;
            if (_created.Count > 1000) _created.Clear();
            _created[sid] = new KeyValuePair<string, DateTime>(Account(e, "Target"), e.TimeUtc);
        }

        private bool JustCreated(string sid, DateTime timeUtc)
        {
            KeyValuePair<string, DateTime> created;
            return sid != null && _created.TryGetValue(sid, out created) && (timeUtc - created.Value).Duration() <= CreationNoiseWindow;
        }

        /// <summary>SID'i hesap adına çevirir; çevrilemezse SID'i döner.</summary>
        private static string SidName(string sid)
        {
            if (string.IsNullOrEmpty(sid) || sid == "-") return null;
            try
            {
                return new SecurityIdentifier(sid).Translate(typeof(NTAccount)).Value;
            }
            catch
            {
                return sid;
            }
        }

        private static string Instance(string provider)
        {
            return provider.StartsWith("MSSQL$", StringComparison.OrdinalIgnoreCase) ? provider.Substring(6) : provider;
        }

        /// <summary>"-", boş, "::ffff:1.2.3.4" gibi değerleri temizler; geçersizse null.</summary>
        internal static string CleanIp(string text)
        {
            if (string.IsNullOrWhiteSpace(text) || text.Trim() == "-") return null;
            IPAddress address;
            if (!IpRange.TryParseAddress(text.Trim(), out address)) return null;
            if (address.IsIPv4MappedToIPv6) address = address.MapToIPv4();
            return address.ToString();
        }

        private static bool IsLoopback(string ip)
        {
            IPAddress a;
            return IPAddress.TryParse(ip, out a) && IPAddress.IsLoopback(a);
        }
    }

    /// <summary>
    /// RDP / ağ girişi eşleştirme. NLA'lı RDP'de aynı deneme hem RdpCoreTS 140 hem Security 4625 (tip 3) üretir; SMB / WinRM sadece 4625.
    /// - 140: RDP kesin. Bekleyen 4625'i (aynı IP, 5 sn) iptal eder; önce sayılmış tip-10 4625 varsa kendisi sayılmaz.
    /// - 4625 tip 10: RDP kesin; önce gelen 140 varsa sayılmaz.
    /// - 4625 tip 3: önce gelen 140 varsa aynı denemedir (sayılmaz). Yoksa 5 sn bekletilir; 140 gelmezse SMB / WinRM olarak sayılır.
    /// Zamanlar olayın kendi zamanıdır; bekletme süresi duvar saatiyle.
    /// </summary>
    internal sealed class RdpNetPairing
    {
        public static readonly TimeSpan Window = TimeSpan.FromSeconds(5);
        private readonly object _sync = new object();
        private readonly Dictionary<string, List<DateTime>> _rdp140 = new Dictionary<string, List<DateTime>>(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, List<DateTime>> _rdp4625 = new Dictionary<string, List<DateTime>>(StringComparer.OrdinalIgnoreCase);
        private readonly List<Pending> _pending = new List<Pending>();

        private sealed class Pending
        {
            public string Ip;
            public DateTime TimeUtc;
            public DateTime DueUtc;
            public Action Count;
        }

        /// <returns>true = 140 sayılmalı.</returns>
        public bool On140(string ip, DateTime timeUtc)
        {
            lock (_sync)
            {
                int i = _pending.FindIndex(p => string.Equals(p.Ip, ip, StringComparison.OrdinalIgnoreCase) && (p.TimeUtc - timeUtc).Duration() <= Window);
                if (i >= 0) _pending.RemoveAt(i); // bekleyen ağ hatası aslında bu RDP denemesi
                if (Take(_rdp4625, ip, timeUtc)) return false;
                Put(_rdp140, ip, timeUtc);
                return true;
            }
        }

        /// <returns>true = tip-10 4625 sayılmalı.</returns>
        public bool OnRdp4625(string ip, DateTime timeUtc)
        {
            lock (_sync)
            {
                if (Take(_rdp140, ip, timeUtc)) return false;
                Put(_rdp4625, ip, timeUtc);
                return true;
            }
        }

        public void OnNet4625(string ip, DateTime timeUtc, Action countAsNetwork)
        {
            lock (_sync)
            {
                if (Take(_rdp140, ip, timeUtc)) return; // RDP denemesi, 140 olarak sayıldı
                if (_pending.Count > 50000) _pending.RemoveRange(0, 10000); // saldırı altında bellek şişmesin (en eskiler hemen sayılmaz, kayıp kabul)
                _pending.Add(new Pending { Ip = ip, TimeUtc = timeUtc, DueUtc = DateTime.UtcNow + Window, Count = countAsNetwork });
            }
        }

        /// <summary>Süresi dolan bekleyenler (SMB / WinRM olarak sayılacak).</summary>
        public List<Action> TakeDue(bool all = false)
        {
            lock (_sync)
            {
                var now = DateTime.UtcNow;
                var due = _pending.Where(p => all || p.DueUtc <= now).ToList();
                if (due.Count > 0) _pending.RemoveAll(p => all || p.DueUtc <= now);
                return due.Select(p => p.Count).ToList();
            }
        }

        private static bool Take(Dictionary<string, List<DateTime>> map, string ip, DateTime timeUtc)
        {
            List<DateTime> list;
            if (!map.TryGetValue(ip, out list)) return false;
            list.RemoveAll(t => (timeUtc - t).Duration() > TimeSpan.FromTicks(Window.Ticks * 4));
            int i = list.FindIndex(t => (timeUtc - t).Duration() <= Window);
            if (i < 0) return false;
            list.RemoveAt(i);
            return true;
        }

        private static void Put(Dictionary<string, List<DateTime>> map, string ip, DateTime timeUtc)
        {
            List<DateTime> list;
            if (!map.TryGetValue(ip, out list)) map[ip] = list = new List<DateTime>();
            list.RemoveAll(t => (timeUtc - t).Duration() > TimeSpan.FromTicks(Window.Ticks * 4));
            list.Add(timeUtc);
            if (map.Count > 10000) map.Clear(); // saldırı altında bellek şişmesin
        }
    }

    /// <summary>Aynı kullanıcı adına pencere içinde farklı dış IP sayısı eşiği geçince bir kez uyarır.</summary>
    internal sealed class SprayingDetector
    {
        private readonly int _threshold;
        private readonly TimeSpan _window;
        private readonly Dictionary<string, Dictionary<string, DateTime>> _byUser =
            new Dictionary<string, Dictionary<string, DateTime>>(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, DateTime> _alerted = new Dictionary<string, DateTime>(StringComparer.OrdinalIgnoreCase);
        private readonly object _sync = new object();

        public SprayingDetector(int threshold, TimeSpan window)
        {
            _threshold = threshold;
            _window = window;
        }

        /// <returns>true = eşik şimdi aşıldı (uyarı üret).</returns>
        public bool Add(string user, string ip, DateTime timeUtc)
        {
            lock (_sync)
            {
                Dictionary<string, DateTime> ips;
                if (!_byUser.TryGetValue(user, out ips)) _byUser[user] = ips = new Dictionary<string, DateTime>(StringComparer.OrdinalIgnoreCase);
                ips[ip] = timeUtc;
                foreach (var old in ips.Where(p => timeUtc - p.Value > _window).Select(p => p.Key).ToList()) ips.Remove(old);
                if (_byUser.Count > 10000) _byUser.Clear();

                if (ips.Count < _threshold) return false;
                DateTime last;
                if (_alerted.TryGetValue(user, out last) && timeUtc - last < _window) return false; // pencere başına bir uyarı
                _alerted[user] = timeUtc;
                return true;
            }
        }

        public List<string> IpsFor(string user)
        {
            lock (_sync)
            {
                Dictionary<string, DateTime> ips;
                return _byUser.TryGetValue(user, out ips) ? ips.Keys.ToList() : new List<string>();
            }
        }
    }
}
