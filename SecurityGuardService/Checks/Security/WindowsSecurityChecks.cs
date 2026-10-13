using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics.Eventing.Reader;
using System.DirectoryServices;
using System.DirectoryServices.AccountManagement;
using System.Globalization;
using System.Linq;
using System.Management;
using System.ServiceProcess;
using System.Text;
using System.Text.RegularExpressions;
using Microsoft.Win32;
using SecurityGuard.Shared.Dto;
using SecurityGuard.Shared.Enums;

namespace SecurityGuardService.Checks.Security
{
    /// <summary>
    /// Windows / RDP güvenlik sağlık kontrolü. Sadece OKUR; düzeltme adımları ve komutları önerir, hiçbirini uygulamaz.
    /// </summary>
    internal sealed class WindowsSecurityChecks
    {
        private const string Area = "Windows";
        private readonly CheckContext _ctx;
        private int _rdpPort = 3389;

        private WindowsSecurityChecks(CheckContext ctx)
        {
            _ctx = ctx;
        }

        public static void Run(CheckContext ctx)
        {
            new WindowsSecurityChecks(ctx).RunAll();
        }

        private static string Id(string name)
        {
            return "sec.win." + name;
        }

        private void RunAll()
        {
            _ctx.Run(Id("firewall"), Area, "Windows Firewall", CheckFirewall);
            _ctx.Run(Id("auditpolicy"), Area, "Windows audit politikası", CheckAuditPolicy);
            _ctx.Run(Id("ransomware"), Area, "Fidye yazılımı erken uyarısı (komut izleme)", CheckRansomwareReadiness);
            _ctx.Run(Id("rdp"), Area, "RDP güvenliği (NLA, şifreleme, port)", CheckRdp);
            _ctx.Run(Id("openports"), Area, "Herkese açık portlar", CheckOpenPorts);
            _ctx.Run(Id("admin"), Area, "Administrator hesabı", CheckAdministrator);
            _ctx.Run(Id("guest"), Area, "Guest (Konuk) hesabı", CheckGuest);
            _ctx.Run(Id("admins"), Area, "Administrators grubu üyeleri", CheckAdminMembers);
            _ctx.Run(Id("passwordpolicy"), Area, "Şifre ve hesap kilitleme politikası", CheckPasswordPolicy);
            _ctx.Run(Id("staleaccounts"), Area, "Kullanılmayan ve şifresi süresiz hesaplar", CheckStaleAccounts);
            _ctx.Run(Id("autologon"), Area, "Otomatik oturum açma", CheckAutoLogon);
            _ctx.Run(Id("wdigest"), Area, "Bellekte açık şifre (WDigest) / eski NTLM", CheckCredentialProtection);
            _ctx.Run(Id("defender"), Area, "Windows Defender", CheckDefender);
            _ctx.Run(Id("defenderexclusions"), Area, "Defender dışlamaları", CheckDefenderExclusions);
            _ctx.Run(Id("lsa"), Area, "LSA koruması (şifre çalmaya karşı)", CheckLsaProtection);
            _ctx.Run(Id("updates"), Area, "Windows güncellemeleri", CheckUpdates);
            _ctx.Run(Id("osversion"), Area, "Windows sürümü desteği", CheckOsVersion);
            _ctx.Run(Id("smb1"), Area, "SMBv1", CheckSmb1);
            _ctx.Run(Id("eventlogsize"), Area, "Olay logu boyutları", CheckEventLogSize);
            _ctx.Run(Id("remoteregistry"), Area, "Uzak kayıt defteri (Remote Registry)", CheckRemoteRegistry);
            _ctx.Run(Id("remotetools"), Area, "Uzaktan erişim programları", CheckRemoteTools);
        }

        private static RegistryKey Hklm()
        {
            return RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, RegistryView.Registry64);
        }

        private static object Reg(string path, string name)
        {
            using (var hklm = Hklm())
            using (var key = hklm.OpenSubKey(path))
                return key == null ? null : key.GetValue(name);
        }

        private static int? RegInt(string path, string name)
        {
            var v = Reg(path, name);
            if (v is int) return (int)v;
            int parsed;
            return v is string && int.TryParse((string)v, out parsed) ? parsed : (int?)null;
        }

        // ================================================================ Firewall

        private SecurityNative.FirewallState _fw;

        private SecurityNative.FirewallState Fw
        {
            get { return _fw ?? (_fw = SecurityNative.Firewall()); }
        }

        private CheckItemDto CheckFirewall()
        {
            var names = new Dictionary<int, string> { { 1, "Etki alanı" }, { 2, "Özel" }, { 4, "Genel" } };
            var off = Fw.Enabled.Where(p => !p.Value).Select(p => p.Key).ToList();
            var activeOff = off.Where(p => (Fw.CurrentProfiles & p) != 0).ToList();
            string active = string.Join(", ", names.Where(n => (Fw.CurrentProfiles & n.Key) != 0).Select(n => n.Value));

            if (off.Count == 0)
                return new CheckItemDto { Status = CheckStatus.Gecti, Summary = "Tüm profillerde açık (etkin profil: " + active + ")." };

            return new CheckItemDto
            {
                Status = activeOff.Count > 0 ? CheckStatus.Kritik : CheckStatus.Uyari,
                Summary = "Kapalı profil: " + string.Join(", ", off.Select(p => names[p])) + (activeOff.Count > 0 ? " (ŞU AN ETKİN PROFİL)" : "") + ".",
                Detail = "Firewall kapalıyken sunucudaki tüm portlar herkese açıktır ve SecurityGuard'ın IP ban kuralları ÇALIŞMAZ (banlanan IP yine bağlanabilir).\r\n"
                         + "Etkin profil: " + active,
                HowToFix = "Windows Defender Güvenlik Duvarı → \"Windows Defender Güvenlik Duvarını aç veya kapat\" → tüm profillerde Aç. "
                           + "Açmadan önce RDP / SQL / uygulama portlarına izin kuralı olduğundan emin olun (yoksa bağlantınız kopabilir).",
                Script = "# Önce gerekli izin kurallarının olduğundan emin olun\r\nSet-NetFirewallProfile -Profile Domain,Private,Public -Enabled True",
                ScriptLanguage = "powershell"
            };
        }

        // ================================================================ Audit politikası

        private sealed class AuditNeed
        {
            public Guid Guid;
            public string Name;
            public uint Flags;
            public bool Critical;
            public string Why;
        }

        private static readonly AuditNeed[] AuditNeeds =
        {
            new AuditNeed { Guid = new Guid("0cce9215-69ae-11d9-bed3-505054503030"), Name = "Oturum açma (Logon)", Flags = SecurityNative.AuditFailure, Critical = true,
                            Why = "başarısız RDP / ağ girişleri (4625) görülmez: brute force ban ÇALIŞMAZ" },
            new AuditNeed { Guid = new Guid("0cce9235-69ae-11d9-bed3-505054503030"), Name = "Kullanıcı hesabı yönetimi", Flags = SecurityNative.AuditSuccess, Critical = true,
                            Why = "yeni kullanıcı / şifre sıfırlama (4720, 4724) görülmez" },
            new AuditNeed { Guid = new Guid("0cce9237-69ae-11d9-bed3-505054503030"), Name = "Güvenlik grubu yönetimi", Flags = SecurityNative.AuditSuccess, Critical = true,
                            Why = "Administrators grubuna eklenme (4732) görülmez" },
            new AuditNeed { Guid = new Guid("0cce922f-69ae-11d9-bed3-505054503030"), Name = "Audit politikası değişikliği", Flags = SecurityNative.AuditSuccess, Critical = false,
                            Why = "audit ayarlarını kapatan biri (4719) görülmez" },
            new AuditNeed { Guid = new Guid("0cce9227-69ae-11d9-bed3-505054503030"), Name = "Diğer nesne erişimi (zamanlanmış görevler)", Flags = SecurityNative.AuditSuccess, Critical = false,
                            Why = "yeni zamanlanmış görev (4698) görülmez" },
            new AuditNeed { Guid = new Guid("0cce9211-69ae-11d9-bed3-505054503030"), Name = "Güvenlik sistemi uzantısı", Flags = SecurityNative.AuditSuccess, Critical = false,
                            Why = "servis kurulumu (4697) Security logunda görülmez" },
            new AuditNeed { Guid = new Guid("0cce922b-69ae-11d9-bed3-505054503030"), Name = "İşlem oluşturma (Process Creation)", Flags = SecurityNative.AuditSuccess, Critical = false,
                            Why = "bir saldırıdan sonra hangi programın çalıştırıldığı izlenemez" },
            new AuditNeed { Guid = new Guid("0cce9232-69ae-11d9-bed3-505054503030"), Name = "Firewall kural değişikliği (MPSSVC)", Flags = SecurityNative.AuditSuccess, Critical = false,
                            Why = "firewall kural değişiklikleri Security logunda görülmez (Firewall logundan yine izlenir)" },
            new AuditNeed { Guid = Engine.Canary.CanaryFiles.FileSystemAuditGuid, Name = "Dosya sistemi (tuzak dosyalar)", Flags = SecurityNative.AuditSuccess, Critical = false,
                            Why = "tuzak dosyaya dokunulunca mailde kullanıcı / program yazmaz (sadece denetim işareti konan dosyalar loglanır: SecurityGuard'ın tuzak dosyaları)" },
        };

        private CheckItemDto CheckAuditPolicy()
        {
            var current = SecurityNative.AuditPolicy(AuditNeeds.Select(n => n.Guid));
            var missing = AuditNeeds.Where(n =>
            {
                uint flags;
                return !current.TryGetValue(n.Guid, out flags) || (flags & n.Flags) != n.Flags;
            }).ToList();

            if (missing.Count == 0)
                return new CheckItemDto { Status = CheckStatus.Gecti, Summary = "SecurityGuard'ın ihtiyaç duyduğu tüm audit ayarları açık." };

            var detail = new StringBuilder("Kapalı olanlar ve sonucu:\r\n");
            var script = new StringBuilder("# Dil bağımsız (GUID ile). Yönetici komut isteminde çalıştırın; hemen geçerli olur.\r\n");
            foreach (var n in missing)
            {
                detail.AppendLine("  " + n.Name + " (" + ((n.Flags & SecurityNative.AuditFailure) != 0 ? "başarısız" : "başarılı") + "): " + n.Why);
                script.AppendLine("auditpol /set /subcategory:\"{" + n.Guid.ToString().ToUpperInvariant() + "}\""
                                  + ((n.Flags & SecurityNative.AuditSuccess) != 0 ? " /success:enable" : "")
                                  + ((n.Flags & SecurityNative.AuditFailure) != 0 ? " /failure:enable" : ""));
            }
            bool critical = missing.Any(n => n.Critical);
            return new CheckItemDto
            {
                Status = critical ? CheckStatus.Kritik : CheckStatus.Uyari,
                Summary = missing.Count + " audit ayarı kapalı: " + string.Join(", ", missing.Take(3).Select(n => n.Name)) + (missing.Count > 3 ? "..." : "") + ".",
                Detail = "Windows bu olayları kaydetmezse SecurityGuard da göremez.\r\n\r\n" + detail,
                HowToFix = "Script'i yönetici olarak çalıştırın. Etki alanındaysa bu ayarlar grup ilkesinden gelir: "
                           + "Bilgisayar Yapılandırması → Windows Ayarları → Güvenlik Ayarları → Gelişmiş Denetim İlkesi Yapılandırması.",
                Script = script.ToString(),
                ScriptLanguage = "powershell"
            };
        }

        // ================================================================ RDP

        private CheckItemDto CheckRdp()
        {
            const string ts = @"SYSTEM\CurrentControlSet\Control\Terminal Server";
            const string tcp = ts + @"\WinStations\RDP-Tcp";
            const string policy = @"SOFTWARE\Policies\Microsoft\Windows NT\Terminal Services";

            if (RegInt(ts, "fDenyTSConnections") == 1)
                return new CheckItemDto { Status = CheckStatus.Gecti, Summary = "RDP kapalı (uzak masaüstü bağlantısına izin verilmiyor)." };

            _rdpPort = RegInt(tcp, "PortNumber") ?? 3389;
            int nla = RegInt(policy, "UserAuthentication") ?? RegInt(tcp, "UserAuthentication") ?? 1;
            int layer = RegInt(policy, "SecurityLayer") ?? RegInt(tcp, "SecurityLayer") ?? 1;
            int encryption = RegInt(policy, "MinEncryptionLevel") ?? RegInt(tcp, "MinEncryptionLevel") ?? 2;

            var problems = new List<string>();
            var script = new StringBuilder();
            var status = CheckStatus.Gecti;
            if (nla == 0)
            {
                problems.Add("NLA (ağ düzeyinde kimlik doğrulama) KAPALI");
                status = CheckStatus.Kritik;
                script.AppendLine("# NLA aç: oturum ekranı açılmadan önce kimlik doğrulanır (brute force ve BlueKeep türü açıklara karşı)");
                script.AppendLine("reg add \"HKLM\\" + tcp + "\" /v UserAuthentication /t REG_DWORD /d 1 /f");
            }
            if (layer == 0)
            {
                problems.Add("güvenlik katmanı \"RDP\" (TLS yok)");
                if (status != CheckStatus.Kritik) status = CheckStatus.Uyari;
                script.AppendLine("# TLS zorunlu");
                script.AppendLine("reg add \"HKLM\\" + tcp + "\" /v SecurityLayer /t REG_DWORD /d 2 /f");
            }
            if (encryption < 2)
            {
                problems.Add("şifreleme seviyesi düşük");
                if (status != CheckStatus.Kritik) status = CheckStatus.Uyari;
            }

            string detail = "RDP portu: " + _rdpPort + (_rdpPort == 3389 ? " (varsayılan)" : "") + "\r\nNLA: " + (nla == 0 ? "kapalı" : "açık")
                            + "\r\nGüvenlik katmanı: " + (layer == 0 ? "RDP" : layer == 1 ? "Negotiate" : "TLS") + "\r\n\r\n"
                            + "Varsayılan port (3389) internetteki otomatik taramaların ilk hedefidir. Port değiştirmek tek başına güvenlik sağlamaz "
                            + "ama gelen deneme sayısını büyük ölçüde azaltır. En güvenlisi RDP'yi internete hiç açmamak (VPN veya IP kısıtı).";

            if (problems.Count == 0)
                return new CheckItemDto
                {
                    Status = _rdpPort == 3389 ? CheckStatus.Bilgi : CheckStatus.Gecti,
                    Summary = "NLA açık" + (_rdpPort == 3389 ? "; port varsayılan (3389)." : ", port " + _rdpPort + "."),
                    Detail = detail,
                    HowToFix = _rdpPort == 3389 ? "Port değiştirmek isterseniz: önce firewall'da yeni porta izin verin, sonra kayıt defterinde PortNumber'ı değiştirip "
                                                 + "Uzak Masaüstü Hizmetleri'ni yeniden başlatın. Yanlış yapılırsa sunucuya erişim kopar; konsol erişiminiz olduğundan emin olun." : null
                };

            return new CheckItemDto
            {
                Status = status,
                Summary = string.Join(", ", problems) + ".",
                Detail = detail,
                HowToFix = "Sistem Özellikleri → Uzak → \"Yalnızca Ağ Düzeyi Kimlik Doğrulaması ile Uzak Masaüstü çalıştıran bilgisayarlardan bağlantıya izin ver\". "
                           + "Çok eski RDP istemcisi kullanan varsa bağlanamaz; önce istemcileri güncelleyin.",
                Script = script.Length == 0 ? null : script.ToString(),
                ScriptLanguage = "powershell"
            };
        }

        // ================================================================ Açık portlar

        private static readonly Dictionary<int, string> RiskyPorts = new Dictionary<int, string>
        {
            { 21, "FTP" }, { 23, "Telnet" }, { 135, "RPC" }, { 139, "NetBIOS" }, { 445, "SMB (dosya paylaşımı)" }, { 1433, "SQL Server" },
            { 3306, "MySQL" }, { 3389, "RDP" }, { 5432, "PostgreSQL" }, { 5900, "VNC" }, { 5985, "WinRM (HTTP)" }, { 5986, "WinRM (HTTPS)" }
        };

        private CheckItemDto CheckOpenPorts()
        {
            var listeners = SecurityNative.TcpListeners()
                .GroupBy(l => l.Port).Select(g => g.First()).OrderBy(l => l.Port).ToList();
            var rules = Fw.AllowIn.Where(r => (r.Profiles & Fw.CurrentProfiles) != 0 && (r.Protocol == 6 || r.Protocol == 256)
                                              && (string.IsNullOrEmpty(r.RemoteAddresses) || r.RemoteAddresses == "*")).ToList();
            bool firewallOff = Fw.Enabled.Any(p => !p.Value && (Fw.CurrentProfiles & p.Key) != 0);

            var open = new List<Tuple<SecurityNative.Listener, string>>();
            var restricted = new List<string>();
            foreach (var l in listeners)
            {
                // SecurityGuard port kısıtı (Koruma → Port Kısıtları) olan port herkese açık sayılmaz
                var guard = Fw.OurRestrictions.FirstOrDefault(r => r.Protocol == 6 && SecurityNative.PortMatches(r.LocalPorts, l.Port));
                if (guard != null && !firewallOff)
                {
                    restricted.Add(l.Port.ToString());
                    continue;
                }

                string reason = null;
                if (firewallOff) reason = "firewall kapalı";
                else
                {
                    var listener = l;
                    var rule = rules.FirstOrDefault(r =>
                    {
                        bool appMatch = !string.IsNullOrEmpty(r.Application) && listener.ProcessPath != null
                                        && string.Equals(Environment.ExpandEnvironmentVariables(r.Application), listener.ProcessPath, StringComparison.OrdinalIgnoreCase);
                        bool anyPort = string.IsNullOrEmpty(r.LocalPorts) || r.LocalPorts == "*";
                        // Tüm portlar: sadece programa bağlı kural sayılır (servise bağlı svchost kuralları hangi porta denk geldiği bilinmediği için sayılmaz)
                        if (anyPort) return appMatch && string.IsNullOrEmpty(r.Service);
                        return (string.IsNullOrEmpty(r.Application) || appMatch) && SecurityNative.PortMatches(r.LocalPorts, listener.Port);
                    });
                    if (rule != null) reason = "kural: " + rule.Name;
                }
                if (reason != null) open.Add(Tuple.Create(l, reason));
            }

            if (open.Count == 0)
                return new CheckItemDto
                {
                    Status = CheckStatus.Gecti,
                    Summary = "IP kısıtı olmadan herkese açık port yok.",
                    Detail = restricted.Count == 0 ? null : "SecurityGuard port kısıtı ile korunan: " + string.Join(", ", restricted)
                };

            var risky = open.Where(o => RiskyPorts.ContainsKey(o.Item1.Port) || o.Item1.Port == _rdpPort).ToList();
            var detail = new StringBuilder("Port | Program | Neden açık\r\n");
            foreach (var o in open)
            {
                string label;
                RiskyPorts.TryGetValue(o.Item1.Port, out label);
                if (o.Item1.Port == _rdpPort) label = "RDP";
                detail.AppendLine(o.Item1.Port + (label != null ? " (" + label + ")" : "") + " | " + (o.Item1.ProcessName ?? "?") + " | " + o.Item2);
            }

            return new CheckItemDto
            {
                Status = risky.Count > 0 ? CheckStatus.Uyari : CheckStatus.Bilgi,
                Summary = open.Count + " port uzak adres kısıtı olmadan açık" + (risky.Count > 0 ? ", riskli: " + string.Join(", ", risky.Select(r => r.Item1.Port + (RiskyPorts.ContainsKey(r.Item1.Port) ? " " + RiskyPorts[r.Item1.Port] : r.Item1.Port == _rdpPort ? " RDP" : ""))) : "") + ".",
                Detail = "Bu portlara firewall'da \"her IP\" için izin verilmiş. Sunucu modem / NAT arkasındaysa ve port yönlendirme yoksa sadece iç ağdan erişilebilir; "
                         + "modemde port yönlendirmesi varsa internete açıktır.\r\n\r\n" + detail
                         + (restricted.Count == 0 ? "" : "\r\nSecurityGuard port kısıtı ile korunan (listede değil): " + string.Join(", ", restricted)),
                HowToFix = "Gerekmeyen portları kapatın. Gerekenleri sadece belirli IP'lere açın: SecurityGuard → Koruma → Port Kısıtları (RDP / SQL hazır şablon) "
                           + "veya firewall kuralı → Özellikler → Kapsam → Uzak IP adresi → \"Bu IP adresleri\". "
                           + "RDP ve SQL için en güvenlisi internete açmamak (VPN). SecurityGuard RDP / SQL brute force'a karşı IP banlar ama kapalı port her zaman daha güvenlidir.",
                Script = "# Örnek: RDP'yi sadece ofis IP'sine açmak (IP'yi kendinize göre değiştirin)\r\n"
                         + "Set-NetFirewallRule -DisplayGroup \"Remote Desktop\" -RemoteAddress 85.105.0.10\r\n"
                         + "# veya grup adı Türkçe ise: Get-NetFirewallRule | Where-Object { $_.DisplayName -like '*Uzak Masa*' } | Set-NetFirewallRule -RemoteAddress 85.105.0.10",
                ScriptLanguage = "powershell"
            };
        }

        // ================================================================ Hesaplar

        private List<UserInfo> _users;

        private sealed class UserInfo
        {
            public string Name, Sid;
            public bool Enabled, PasswordNeverExpires;
            public DateTime? LastLogon, PasswordSet;
        }

        private List<UserInfo> Users()
        {
            if (_users != null) return _users;
            _users = new List<UserInfo>();
            using (var ctx = new PrincipalContext(ContextType.Machine))
            using (var searcher = new PrincipalSearcher(new UserPrincipal(ctx)))
            {
                foreach (var p in searcher.FindAll())
                {
                    using (p)
                    {
                        var u = p as UserPrincipal;
                        if (u == null) continue;
                        _users.Add(new UserInfo
                        {
                            Name = u.SamAccountName,
                            Sid = u.Sid == null ? null : u.Sid.Value,
                            Enabled = u.Enabled ?? true,
                            PasswordNeverExpires = u.PasswordNeverExpires,
                            LastLogon = u.LastLogon,
                            PasswordSet = u.LastPasswordSet
                        });
                    }
                }
            }
            return _users;
        }

        private CheckItemDto CheckAdministrator()
        {
            var admin = Users().FirstOrDefault(u => u.Sid != null && u.Sid.EndsWith("-500"));
            if (admin == null) return new CheckItemDto { Status = CheckStatus.Bilgi, Summary = "Yerleşik yönetici hesabı bulunamadı (etki alanı denetleyicisi olabilir)." };

            bool defaultName = admin.Name.Equals("Administrator", StringComparison.OrdinalIgnoreCase) || admin.Name.Equals("Yönetici", StringComparison.OrdinalIgnoreCase);
            if (!admin.Enabled)
                return new CheckItemDto { Status = CheckStatus.Gecti, Summary = "Yerleşik yönetici hesabı (" + admin.Name + ") devre dışı." };
            if (!defaultName)
                return new CheckItemDto { Status = CheckStatus.Gecti, Summary = "Yerleşik yönetici hesabının adı değiştirilmiş (" + admin.Name + ")." };

            return new CheckItemDto
            {
                Status = CheckStatus.Uyari,
                Summary = "\"" + admin.Name + "\" hesabı açık ve adı varsayılan.",
                Detail = "İnternetteki saldırı araçları ilk olarak \"Administrator\" kullanıcı adını dener: kullanıcı adı bilindiği için geriye sadece şifreyi tahmin etmek kalır. "
                         + "Bu hesap kilitlenmez (hesap kilitleme politikası yerleşik yöneticiye uygulanmaz).\r\n\r\n"
                         + "Son oturum: " + Fmt.Date(admin.LastLogon) + "\r\nŞifre değişikliği: " + Fmt.Date(admin.PasswordSet),
                HowToFix = "Önce kendinize ayrı bir yönetici hesabı açın ve onunla giriş yapabildiğinizi deneyin. Sonra yerleşik hesabı ya devre dışı bırakın "
                           + "ya da adını değiştirin (Bilgisayar Yönetimi → Yerel Kullanıcılar ve Gruplar). Hesabı kullanan servis / zamanlanmış görev varsa önce onları kontrol edin.",
                Script = "# 1) Yeni yönetici hesabı (adı ve şifreyi değiştirin), 2) onunla giriş yapıp deneyin, 3) sonra:\r\n"
                         + "Rename-LocalUser -Name \"" + admin.Name + "\" -NewName \"srv_yonetim\"\r\n# veya: Disable-LocalUser -Name \"" + admin.Name + "\"",
                ScriptLanguage = "powershell"
            };
        }

        private CheckItemDto CheckGuest()
        {
            var guest = Users().FirstOrDefault(u => u.Sid != null && u.Sid.EndsWith("-501"));
            if (guest == null || !guest.Enabled)
                return new CheckItemDto { Status = CheckStatus.Gecti, Summary = "Konuk hesabı devre dışı." };
            return new CheckItemDto
            {
                Status = CheckStatus.Kritik,
                Summary = "Konuk (" + guest.Name + ") hesabı AÇIK.",
                Detail = "Konuk hesabı şifresiz olabilir ve sunucuya kimliği belirsiz erişim sağlar.",
                HowToFix = "Bilgisayar Yönetimi → Yerel Kullanıcılar ve Gruplar → Kullanıcılar → " + guest.Name + " → \"Hesap devre dışı\".",
                Script = "Disable-LocalUser -Name \"" + guest.Name + "\"",
                ScriptLanguage = "powershell"
            };
        }

        private CheckItemDto CheckAdminMembers()
        {
            var members = new List<string>();
            using (var ctx = new PrincipalContext(ContextType.Machine))
            using (var group = GroupPrincipal.FindByIdentity(ctx, IdentityType.Sid, "S-1-5-32-544"))
            {
                if (group == null) return new CheckItemDto { Status = CheckStatus.Bilgi, Summary = "Administrators grubu bulunamadı." };
                // GetMembers yetim SID'lerde hata verdiği için alttaki WinNT nesnesinden okunur
                var entry = (DirectoryEntry)group.GetUnderlyingObject();
                foreach (object m in (IEnumerable)entry.Invoke("Members"))
                {
                    using (var me = new DirectoryEntry(m))
                    {
                        string path = me.Path ?? "";
                        // WinNT://ETKIALANI/ad veya WinNT://ETKIALANI/MAKINE/ad
                        var parts = path.Replace("WinNT://", "").Split('/');
                        members.Add(parts.Length >= 3 && parts[1].Equals(Environment.MachineName, StringComparison.OrdinalIgnoreCase)
                            ? Environment.MachineName + "\\" + parts[2]
                            : parts.Length >= 2 ? parts[0] + "\\" + parts[parts.Length - 1] : path);
                    }
                }
            }

            var builtin = Users().FirstOrDefault(u => u.Sid != null && u.Sid.EndsWith("-500"));
            var extra = members.Where(m => !m.EndsWith("\\Domain Admins", StringComparison.OrdinalIgnoreCase)
                                           && !m.EndsWith("\\Etki Alanı Yöneticileri", StringComparison.OrdinalIgnoreCase)
                                           && (builtin == null || !m.EndsWith("\\" + builtin.Name, StringComparison.OrdinalIgnoreCase))).ToList();

            return new CheckItemDto
            {
                Status = extra.Count > 3 ? CheckStatus.Uyari : CheckStatus.Bilgi,
                Summary = members.Count + " üye" + (extra.Count > 0 ? " (" + string.Join(", ", extra.Take(5)) + (extra.Count > 5 ? "..." : "") + ")" : "") + ".",
                Detail = "Bu gruptaki herkes sunucuda her şeyi yapabilir (SecurityGuard'ı durdurmak dahil). RDP ile çalışan son kullanıcılar bu grupta olmamalı; "
                         + "\"Remote Desktop Users\" grubu RDP için yeterlidir.\r\n\r\n  " + string.Join("\r\n  ", members),
                HowToFix = extra.Count == 0 ? null
                    : "Listeyi gözden geçirin; yönetici olması gerekmeyenleri çıkarın (Bilgisayar Yönetimi → Yerel Kullanıcılar ve Gruplar → Gruplar → Administrators). "
                      + "RDP ile bağlanması gerekenleri \"Remote Desktop Users\" grubuna ekleyin."
            };
        }

        private CheckItemDto CheckPasswordPolicy()
        {
            var p = SecurityNative.SystemAccessPolicy();
            Func<string, int> get = k =>
            {
                string v;
                int n;
                return p.TryGetValue(k, out v) && int.TryParse(v, out n) ? n : -1;
            };
            int minLength = get("MinimumPasswordLength"), complexity = get("PasswordComplexity"), lockout = get("LockoutBadCount"),
                lockMinutes = get("LockoutDuration"), maxAge = get("MaximumPasswordAge");

            var problems = new List<string>();
            var status = CheckStatus.Gecti;
            if (minLength >= 0 && minLength < 8) { problems.Add("en kısa şifre " + minLength + " karakter"); status = CheckStatus.Uyari; }
            if (complexity == 0) { problems.Add("karmaşıklık şartı kapalı"); status = CheckStatus.Uyari; }
            if (lockout == 0) { problems.Add("hesap kilitleme yok"); status = CheckStatus.Uyari; }

            string detail = "En kısa şifre: " + (minLength < 0 ? "?" : minLength.ToString()) + " karakter\r\nKarmaşıklık: " + (complexity == 1 ? "açık" : complexity == 0 ? "kapalı" : "?")
                            + "\r\nHesap kilitleme: " + (lockout == 0 ? "yok" : lockout < 0 ? "?" : lockout + " hatalı denemede, " + lockMinutes + " dakika")
                            + "\r\nŞifre geçerlilik süresi: " + (maxAge < 0 ? "?" : maxAge == -1 || maxAge > 9999 ? "süresiz" : maxAge + " gün");
            if (problems.Count == 0)
                return new CheckItemDto { Status = CheckStatus.Gecti, Summary = "Şifre ve kilitleme politikası uygun.", Detail = detail };

            return new CheckItemDto
            {
                Status = status,
                Summary = string.Join(", ", problems) + ".",
                Detail = "Kısa / basit şifreler brute force ile dakikalar içinde kırılabilir. Hesap kilitleme yoksa saldırgan sınırsız deneme yapabilir "
                         + "(SecurityGuard dış IP'leri banlar ama iç ağdan gelen denemeleri banlamaz).\r\n\r\n" + detail,
                HowToFix = "secpol.msc → Hesap İlkeleri → Parola İlkesi (en az 8-12 karakter, karmaşıklık açık) ve Hesap Kilitleme İlkesi (ör. 10 hatalı denemede 15 dakika). "
                           + "Etki alanındaysa bu ayarlar Default Domain Policy'den gelir.",
                Script = "net accounts /minpwlen:10 /lockoutthreshold:10 /lockoutduration:15 /lockoutwindow:15\r\n# Karmaşıklık için: secpol.msc → Parola karmaşıklık gereksinimlerini karşılamalı → Etkin",
                ScriptLanguage = "powershell"
            };
        }

        private CheckItemDto CheckRansomwareReadiness()
        {
            var audit = SecurityNative.AuditPolicy(new[] { ProcessCreationGuid });
            uint flags;
            bool processAudit = audit.TryGetValue(ProcessCreationGuid, out flags) && (flags & SecurityNative.AuditSuccess) != 0;
            bool commandLine = RegInt(@"SOFTWARE\Microsoft\Windows\CurrentVersion\Policies\System\Audit", "ProcessCreationIncludeCmdLine_Enabled") == 1;
            if (processAudit && commandLine)
                return new CheckItemDto
                {
                    Status = CheckStatus.Gecti,
                    Summary = "Açık: gölge kopya / yedek silme gibi fidye yazılımı komutları anında yakalanır.",
                    Detail = "SecurityGuard her yeni işlemin komut satırına bakar; vssadmin delete shadows, wbadmin delete catalog, bcdedit recoveryenabled no gibi komutlarda Kritik mail atar."
                };

            var missing = new List<string>();
            if (!processAudit) missing.Add("işlem oluşturma kaydı (audit) kapalı");
            if (!commandLine) missing.Add("komut satırı kaydı kapalı");
            return new CheckItemDto
            {
                Status = CheckStatus.Uyari,
                Summary = string.Join(", ", missing) + ": fidye yazılımının hazırlık komutları görülemez.",
                Detail = "Fidye yazılımları şifrelemeye başlamadan hemen önce geri dönüşü engeller: gölge kopyaları (vssadmin delete shadows), Windows yedek kataloğunu (wbadmin delete catalog) "
                         + "siler, otomatik onarımı kapatır (bcdedit), logları temizler. Bu komutlar şifrelemeden dakikalar önce çalışır ve normal işletimde neredeyse hiç görülmez.\r\n\r\n"
                         + "İki ayar açılınca SecurityGuard bu komutları anında yakalayıp Kritik mail atar: sunucuyu ağdan ayırmak için zaman kazandırır.\r\n"
                         + "Not: işlem oluşturma kaydı Security logunu daha hızlı doldurur; log boyutunu da büyütün (Olay logu boyutları maddesi).",
                HowToFix = "Script'i yönetici olarak çalıştırın; hemen geçerli olur. Etki alanındaysa grup ilkesinden: Gelişmiş Denetim İlkesi → Ayrıntılı İzleme → İşlem Oluşturmayı Denetle, "
                           + "ve Yönetim Şablonları → Sistem → İşlem Oluşturmayı Denetle → \"İşlem oluşturma olaylarına komut satırını ekle\".",
                Script = "auditpol /set /subcategory:\"{" + ProcessCreationGuid.ToString().ToUpperInvariant() + "}\" /success:enable\r\n"
                         + "reg add \"HKLM\\SOFTWARE\\Microsoft\\Windows\\CurrentVersion\\Policies\\System\\Audit\" /v ProcessCreationIncludeCmdLine_Enabled /t REG_DWORD /d 1 /f\r\n"
                         + "wevtutil sl Security /ms:" + (512L * 1024 * 1024),
                ScriptLanguage = "powershell"
            };
        }

        private static readonly Guid ProcessCreationGuid = new Guid("0cce922b-69ae-11d9-bed3-505054503030");

        private CheckItemDto CheckStaleAccounts()
        {
            var limit = DateTime.Now.AddDays(-90);
            // Yerleşik hesaplar (500 yönetici, 501 konuk, 503 DefaultAccount, 504 WDAGUtilityAccount) ayrı maddelerde
            var users = Users().Where(u => u.Enabled && u.Sid != null && !u.Sid.EndsWith("-500") && !u.Sid.EndsWith("-501") && !u.Sid.EndsWith("-503") && !u.Sid.EndsWith("-504")).ToList();
            // Yerel hesabın "son oturum" bilgisi (özellikle Microsoft hesabına bağlı kullanıcılarda) güncellenmeyebilir:
            // profilin son kullanımı ve şu an açık oturumlar da hesaba katılır
            var active = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            try
            {
                foreach (var session in SecurityGuardService.Checks.Performance.WindowsNative.Sessions())
                    active.Add(session.User.Contains("\\") ? session.User.Substring(session.User.IndexOf('\\') + 1) : session.User);
            }
            catch (Exception)
            {
                // oturum listesi alınamazsa sadece kayıtlı tarihlere bakılır
            }
            foreach (var u in users)
            {
                var profile = ProfileLastUse(u.Sid);
                if (profile != null && (u.LastLogon == null || profile > u.LastLogon)) u.LastLogon = profile;
            }
            var stale = users.Where(u => !active.Contains(u.Name) && (u.LastLogon ?? u.PasswordSet ?? DateTime.MinValue) < limit).ToList();
            var neverExpire = users.Where(u => u.PasswordNeverExpires && !stale.Contains(u)).ToList();
            if (stale.Count == 0 && neverExpire.Count == 0)
                return new CheckItemDto { Status = CheckStatus.Gecti, Summary = "90 günden uzun süredir kullanılmayan veya şifresi süresiz aktif hesap yok." };

            var detail = new StringBuilder();
            if (stale.Count > 0)
            {
                detail.AppendLine("90 günden uzun süredir giriş yapmayan aktif hesaplar:");
                foreach (var u in stale) detail.AppendLine("  " + u.Name + ": son giriş " + (u.LastLogon == null ? "hiç" : Fmt.Date(u.LastLogon)));
                detail.AppendLine();
            }
            if (neverExpire.Count > 0)
            {
                detail.AppendLine("Şifresi hiç dolmayan hesaplar:");
                foreach (var u in neverExpire) detail.AppendLine("  " + u.Name + ": şifre son değişiklik " + Fmt.Date(u.PasswordSet));
            }
            return new CheckItemDto
            {
                Status = stale.Count > 0 ? CheckStatus.Uyari : CheckStatus.Bilgi,
                Summary = (stale.Count > 0 ? stale.Count + " hesap 90 günden uzun süredir kullanılmıyor (" + string.Join(", ", stale.Take(4).Select(u => u.Name)) + (stale.Count > 4 ? "..." : "") + ")" : "")
                          + (stale.Count > 0 && neverExpire.Count > 0 ? "; " : "") + (neverExpire.Count > 0 ? neverExpire.Count + " hesabın şifresi süresiz" : "") + ".",
                Detail = "Ayrılmış çalışanların ve unutulmuş test hesaplarının açık kalması en sık kullanılan giriş yollarından biridir: şifresi bilinir, sahibi olmadığı için kimse fark etmez.\r\n\r\n" + detail,
                HowToFix = "Kullanılmayan hesapları devre dışı bırakın (silmek yerine önce kapatın, bir süre sonra silin). Servis hesapları dışında şifrelerin belirli aralıklarla değişmesini sağlayın.",
                Script = string.Join("\r\n", stale.Select(u => "# Disable-LocalUser -Name \"" + u.Name + "\"")),
                ScriptLanguage = "powershell"
            };
        }

        /// <summary>Profilin son yüklenme / kapanma zamanı veya NTUSER.DAT'ın son yazılması (profil yoksa null).</summary>
        private static DateTime? ProfileLastUse(string sid)
        {
            if (sid == null) return null;
            DateTime? last = null;
            using (var hklm = Hklm())
            using (var key = hklm.OpenSubKey(@"SOFTWARE\Microsoft\Windows NT\CurrentVersion\ProfileList\" + sid))
            {
                if (key == null) return null;
                foreach (var pair in new[] { Tuple.Create("LocalProfileLoadTimeHigh", "LocalProfileLoadTimeLow"), Tuple.Create("LocalProfileUnloadTimeHigh", "LocalProfileUnloadTimeLow") })
                {
                    var hi = key.GetValue(pair.Item1);
                    var lo = key.GetValue(pair.Item2);
                    if (!(hi is int) || !(lo is int)) continue;
                    try
                    {
                        var t = DateTime.FromFileTime(((long)(uint)(int)hi << 32) | (uint)(int)lo);
                        if (last == null || t > last) last = t;
                    }
                    catch (ArgumentOutOfRangeException) { }
                }
                string path = key.GetValue("ProfileImagePath") as string;
                try
                {
                    string ntuser = path == null ? null : System.IO.Path.Combine(Environment.ExpandEnvironmentVariables(path), "NTUSER.DAT");
                    if (ntuser != null && System.IO.File.Exists(ntuser))
                    {
                        var t = System.IO.File.GetLastWriteTime(ntuser);
                        if (last == null || t > last) last = t;
                    }
                }
                catch { }
            }
            return last;
        }

        private CheckItemDto CheckAutoLogon()
        {
            const string path = @"SOFTWARE\Microsoft\Windows NT\CurrentVersion\Winlogon";
            string auto = Reg(path, "AutoAdminLogon") as string;
            if (auto != "1") return new CheckItemDto { Status = CheckStatus.Gecti, Summary = "Otomatik oturum açma kapalı." };
            bool password = !string.IsNullOrEmpty(Reg(path, "DefaultPassword") as string);
            string user = Reg(path, "DefaultUserName") as string;
            return new CheckItemDto
            {
                Status = password ? CheckStatus.Kritik : CheckStatus.Uyari,
                Summary = "Sunucu açılışta \"" + user + "\" ile otomatik oturum açıyor" + (password ? "; şifre kayıt defterinde AÇIK METİN olarak duruyor." : "."),
                Detail = "Konsola erişen herkes bu kullanıcıyla içeride olur. Şifre kayıt defterinde açık metin tutuluyorsa yönetici yetkisi olan her program okuyabilir.",
                HowToFix = "netplwiz → \"Kullanıcılar bu bilgisayarı kullanmak için kullanıcı adı ve parola girmelidir\" işaretleyin. Bir program otomatik oturum gerektiriyorsa servis olarak çalıştırılmalı.",
                Script = "reg add \"HKLM\\" + path + "\" /v AutoAdminLogon /t REG_SZ /d 0 /f\r\nreg delete \"HKLM\\" + path + "\" /v DefaultPassword /f",
                ScriptLanguage = "powershell"
            };
        }

        private CheckItemDto CheckCredentialProtection()
        {
            int? wdigest = RegInt(@"SYSTEM\CurrentControlSet\Control\SecurityProviders\WDigest", "UseLogonCredential");
            int lm = RegInt(@"SYSTEM\CurrentControlSet\Control\Lsa", "LmCompatibilityLevel") ?? 3;
            var problems = new List<string>();
            var script = new StringBuilder();
            var status = CheckStatus.Gecti;
            if (wdigest == 1)
            {
                problems.Add("WDigest açık: oturum açanların şifreleri bellekte açık metin");
                status = CheckStatus.Kritik;
                script.AppendLine("reg add \"HKLM\\SYSTEM\\CurrentControlSet\\Control\\SecurityProviders\\WDigest\" /v UseLogonCredential /t REG_DWORD /d 0 /f");
            }
            if (lm < 3)
            {
                problems.Add("eski LM / NTLMv1 kimlik doğrulamaya izin veriliyor (seviye " + lm + ")");
                if (status != CheckStatus.Kritik) status = CheckStatus.Uyari;
                script.AppendLine("# Sadece NTLMv2 (çok eski istemciler bağlanamaz)\r\nreg add \"HKLM\\SYSTEM\\CurrentControlSet\\Control\\Lsa\" /v LmCompatibilityLevel /t REG_DWORD /d 5 /f");
            }
            if (problems.Count == 0) return new CheckItemDto { Status = CheckStatus.Gecti, Summary = "WDigest kapalı, sadece NTLMv2." };
            return new CheckItemDto
            {
                Status = status,
                Summary = string.Join("; ", problems) + ".",
                Detail = "WDigest açıksa sunucuya sızan biri (mimikatz vb.) RDP ile bağlanmış tüm kullanıcıların şifrelerini bellekten okuyabilir. "
                         + "LM / NTLMv1 şifre özetleri ağda dinlenip kolayca kırılabilir.",
                HowToFix = "Script'i yönetici olarak çalıştırın; WDigest değişikliği bir sonraki oturumlarda geçerli olur (sunucuyu yeniden başlatmak en temizi).",
                Script = script.ToString(),
                ScriptLanguage = "powershell"
            };
        }

        // ================================================================ Defender / güncelleme / sürüm

        private CheckItemDto CheckDefender()
        {
            ManagementObject status = null;
            try
            {
                using (var searcher = new ManagementObjectSearcher(@"root\Microsoft\Windows\Defender", "SELECT * FROM MSFT_MpComputerStatus"))
                    status = searcher.Get().Cast<ManagementObject>().FirstOrDefault();
            }
            catch (ManagementException)
            {
                status = null;
            }
            if (status == null)
                return new CheckItemDto
                {
                    Status = CheckStatus.Bilgi,
                    Summary = "Windows Defender bu sunucuda yok veya okunamadı; başka bir antivirüs kullanılıyorsa onun güncel ve açık olduğunu kontrol edin."
                };

            using (status)
            {
                bool av = (bool)(status["AntivirusEnabled"] ?? false);
                bool rt = (bool)(status["RealTimeProtectionEnabled"] ?? false);
                uint age = Convert.ToUInt32(status["AntivirusSignatureAge"] ?? 0u, CultureInfo.InvariantCulture);
                object tamperObj = null;
                try { tamperObj = status["IsTamperProtected"]; } catch (ManagementException) { }
                bool tamper = tamperObj is bool && (bool)tamperObj;

                var problems = new List<string>();
                var level = CheckStatus.Gecti;
                if (!av || !rt) { problems.Add(!av ? "antivirüs kapalı" : "gerçek zamanlı koruma kapalı"); level = CheckStatus.Kritik; }
                if (age > 7) { problems.Add("virüs imzaları " + age + " gün eski"); if (level != CheckStatus.Kritik) level = CheckStatus.Uyari; }
                string detail = "Antivirüs: " + (av ? "açık" : "kapalı") + "\r\nGerçek zamanlı koruma: " + (rt ? "açık" : "kapalı")
                                + "\r\nİmza yaşı: " + age + " gün\r\nKurcalama koruması (Tamper Protection): " + (tamper ? "açık" : "kapalı / bilinmiyor");
                if (problems.Count == 0)
                    return new CheckItemDto { Status = CheckStatus.Gecti, Summary = "Açık, imzalar güncel (" + age + " gün).", Detail = detail };

                return new CheckItemDto
                {
                    Status = level,
                    Summary = string.Join(", ", problems) + ".",
                    Detail = "Kapalı veya eski antivirüs fidye yazılımlarını tanımaz. Saldırganlar içeri girince ilk iş Defender'ı kapatır (SecurityGuard bunu olay olarak da bildirir).\r\n\r\n" + detail,
                    HowToFix = "Windows Güvenliği → Virüs ve tehdit koruması → Gerçek zamanlı koruma: Açık. İmzalar eskiyse güncellemeleri kontrol edin. "
                               + "Başka bir antivirüs bilerek kullanılıyorsa bu maddeyi notla görmezden gelin.",
                    Script = "Set-MpPreference -DisableRealtimeMonitoring $false\r\nUpdate-MpSignature",
                    ScriptLanguage = "powershell"
                };
            }
        }

        private CheckItemDto CheckDefenderExclusions()
        {
            var entries = new List<Tuple<string, string>>(); // (tür, değer)
            using (var hklm = Hklm())
            {
                foreach (var kind in new[] { "Paths", "Extensions", "Processes" })
                    foreach (var root in new[] { @"SOFTWARE\Microsoft\Windows Defender\Exclusions\", @"SOFTWARE\Policies\Microsoft\Windows Defender\Exclusions\" })
                        foreach (var name in ValueNames(hklm, root + kind))
                            entries.Add(Tuple.Create(kind, name));
            }
            if (entries.Count == 0) return new CheckItemDto { Status = CheckStatus.Gecti, Summary = "Defender'da dışlama yok (veya Defender kullanılmıyor)." };

            string windows = Environment.GetFolderPath(Environment.SpecialFolder.Windows).TrimEnd('\\');
            var risky = new List<string>();
            foreach (var e in entries)
            {
                string v = e.Item2.Trim().TrimEnd('\\');
                string lower = v.ToLowerInvariant();
                if (e.Item1 == "Paths")
                {
                    if (v.Length <= 3 || v == "*" || lower == windows.ToLowerInvariant() || lower.EndsWith("\\system32") || lower.Contains("\\temp") || lower.Contains("%temp%")
                        || lower.EndsWith("\\users") || lower.EndsWith("\\programdata") || lower.Contains("\\appdata") || lower.Contains("\\downloads") || lower.Contains("\\desktop"))
                        risky.Add("klasör: " + v);
                }
                else if (e.Item1 == "Extensions")
                {
                    if (new[] { "exe", "dll", "ps1", "bat", "cmd", "vbs", "js", "scr", "hta", "msi", "*" }.Contains(lower.TrimStart('.', '*')) || lower == "*") risky.Add("uzantı: " + v);
                }
                else if (new[] { "powershell.exe", "cmd.exe", "rundll32.exe", "regsvr32.exe", "mshta.exe", "wscript.exe", "cscript.exe", "explorer.exe", "svchost.exe" }
                         .Any(p => lower.EndsWith(p)))
                    risky.Add("işlem: " + v);
            }

            string all = string.Join("\r\n", entries.Select(e => "  " + (e.Item1 == "Paths" ? "Klasör" : e.Item1 == "Extensions" ? "Uzantı" : "İşlem") + ": " + e.Item2));
            if (risky.Count == 0)
                return new CheckItemDto { Status = CheckStatus.Gecti, Summary = entries.Count + " dışlama var, tehlikeli geniş dışlama yok.", Detail = all };
            return new CheckItemDto
            {
                Status = CheckStatus.Kritik,
                Summary = "Tehlikeli geniş dışlama: " + string.Join(", ", risky.Take(3)) + (risky.Count > 3 ? "..." : "") + ".",
                Detail = "Saldırganlar içeri girince zararlı dosyalarını koydukları yeri (C:\\, Temp, Kullanıcılar, ProgramData) veya .exe / .ps1 gibi uzantıları Defender'dan dışlar; "
                         + "böylece fidye yazılımı taranmadan çalışır. Bu tür geniş dışlamaları siz eklemediyseniz sunucu ele geçirilmiş olabilir.\r\n\r\nTüm dışlamalar:\r\n" + all,
                HowToFix = "Windows Güvenliği → Virüs ve tehdit koruması → Ayarları yönet → Dışlamalar: gereksiz geniş dışlamaları kaldırın. SQL için sadece .mdf / .ldf / .ndf ve SQL veri klasörleri yeterlidir. "
                           + "Kimin eklediğini bilmiyorsanız Olay Logu'nda \"Defender kapatıldı / ayarı değişti\" olaylarına bakın.",
                Script = "# Kontrol edip ilgili satırları çalıştırın\r\n" + string.Join("\r\n", risky.Select(r =>
                {
                    string value = r.Substring(r.IndexOf(':') + 2);
                    return r.StartsWith("klasör") ? "# Remove-MpPreference -ExclusionPath '" + value + "'"
                         : r.StartsWith("uzantı") ? "# Remove-MpPreference -ExclusionExtension '" + value + "'"
                         : "# Remove-MpPreference -ExclusionProcess '" + value + "'";
                })),
                ScriptLanguage = "powershell"
            };
        }

        private static IEnumerable<string> ValueNames(RegistryKey root, string path)
        {
            try
            {
                using (var key = root.OpenSubKey(path))
                    return key == null ? new string[0] : key.GetValueNames();
            }
            catch (System.Security.SecurityException)
            {
                return new string[0];
            }
            catch (UnauthorizedAccessException)
            {
                return new string[0];
            }
        }

        private CheckItemDto CheckLsaProtection()
        {
            int ppl = RegInt(@"SYSTEM\CurrentControlSet\Control\Lsa", "RunAsPPL") ?? 0;
            if (ppl == 1 || ppl == 2) return new CheckItemDto { Status = CheckStatus.Gecti, Summary = "LSA koruması açık." };
            return new CheckItemDto
            {
                Status = CheckStatus.Uyari,
                Summary = "LSA koruması kapalı.",
                Detail = "Sunucuya giren saldırgan mimikatz gibi araçlarla LSASS işleminin belleğinden, RDP ile bağlanmış kullanıcıların şifre özetlerini (hatta bazen şifrelerini) okur "
                         + "ve aynı şifreyi kullanan diğer sunuculara geçer. LSA koruması bu okumayı engeller.",
                HowToFix = "Script'i çalıştırıp sunucuyu yeniden başlatın. Nadiren eski bir güvenlik / akıllı kart yazılımı LSA'ya eklenti yüklüyorsa uyumsuz olabilir: "
                           + "önce test edin, yeniden başlatmadan sonra Olay Görüntüleyicisi → Uygulamalar ve Hizmetler → Microsoft → Windows → CodeIntegrity loguna bakın.",
                Script = "reg add \"HKLM\\SYSTEM\\CurrentControlSet\\Control\\Lsa\" /v RunAsPPL /t REG_DWORD /d 1 /f\r\n# Yeniden başlatma gerekir",
                ScriptLanguage = "powershell"
            };
        }

        private static readonly Regex DefinitionUpdate = new Regex(@"Defender|Security Intelligence|Güvenlik Zekası|KB2267602|Malicious Software|Kötü Amaçlı|Antimalware|Definition",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

        private static readonly Regex SecurityUpdate = new Regex(@"Cumulative|Toplu|Kümülatif|Security Update|Güvenlik Güncelleştirmesi|Monthly Rollup|Aylık",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

        private static readonly Regex WindowsUpdateTitle = new Regex(@"Windows|server operating system|sunucu işletim sistemi", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

        // Sürücü güncellemeleri "Üretici - Sınıf - Sürüm" biçiminde gelir
        private static readonly Regex DriverUpdate = new Regex(@"^[^-]+ - [^-]+ - ", RegexOptions.CultureInvariant);

        private CheckItemDto CheckUpdates()
        {
            DateTime? last = null;
            string lastTitle = null;
            bool lastIsSecurity = false;
            try
            {
                dynamic session = Activator.CreateInstance(Type.GetTypeFromProgID("Microsoft.Update.Session", true));
                dynamic searcher = session.CreateUpdateSearcher();
                int total = searcher.GetTotalHistoryCount();
                dynamic history = searcher.QueryHistory(0, Math.Min(total, 200));
                foreach (dynamic entry in history)
                {
                    // Operation 1 = kurulum, ResultCode 2 = başarılı
                    if ((int)entry.Operation != 1 || (int)entry.ResultCode != 2) continue;
                    string title = entry.Title;
                    if (title == null || DefinitionUpdate.IsMatch(title) || DriverUpdate.IsMatch(title)) continue;
                    DateTime date = entry.Date;
                    // Önce Windows güvenlik / toplu güncellemeleri sayılır; hiç yoksa diğer güncellemeler
                    bool security = SecurityUpdate.IsMatch(title) && WindowsUpdateTitle.IsMatch(title);
                    if (security && !lastIsSecurity) { last = null; lastIsSecurity = true; }
                    if (security != lastIsSecurity) continue;
                    if (last == null || date > last) { last = date; lastTitle = title; }
                }
            }
            catch (Exception)
            {
                // Windows Update servisi kapalı olabilir: aşağıda kurulu yamalardan (QFE) bakılır
                last = null;
            }

            // WU geçmişi yoksa (yeni kurulum, geçmiş temizlenmiş) kurulu yamalar (QFE) okunur
            if (last == null)
            {
                using (var s = new ManagementObjectSearcher("SELECT HotFixID, InstalledOn FROM Win32_QuickFixEngineering"))
                {
                    foreach (ManagementObject q in s.Get())
                    {
                        using (q)
                        {
                            DateTime d;
                            string text = Convert.ToString(q["InstalledOn"], CultureInfo.InvariantCulture);
                            if (DateTime.TryParse(text, CultureInfo.GetCultureInfo("en-US"), DateTimeStyles.None, out d) && (last == null || d > last))
                            {
                                last = d;
                                lastTitle = Convert.ToString(q["HotFixID"], CultureInfo.InvariantCulture);
                            }
                        }
                    }
                }
            }

            // Asıl ölçü Windows çekirdeğinin (ntoskrnl.exe) son güncellenme zamanı: her aylık toplu güncelleme onu yeniler.
            // Güncelleme geçmişindeki tarih yanıltabilir (ör. küçük bir platform güncellemesi yeni görünür ama işletim sistemi yıllarca eski kalmıştır).
            DateTime kernel = System.IO.File.GetLastWriteTime(System.IO.Path.Combine(Environment.SystemDirectory, "ntoskrnl.exe"));
            const string cv = @"SOFTWARE\Microsoft\Windows NT\CurrentVersion";
            string build = (Reg(cv, "CurrentBuildNumber") as string) + "." + RegInt(cv, "UBR");
            var patched = last == null || kernel < last.Value.ToLocalTime() ? kernel : last.Value.ToLocalTime();

            int days = (int)(DateTime.Now - patched).TotalDays;
            var status = days > 90 ? CheckStatus.Kritik : days > 45 ? CheckStatus.Uyari : CheckStatus.Gecti;
            return new CheckItemDto
            {
                Status = status,
                Summary = "Windows yama seviyesi " + days + " gün önce (" + patched.ToString("dd.MM.yyyy") + ", derleme " + build + ").",
                Detail = "Windows çekirdeğinin son güncellenmesi: " + Fmt.Date(kernel) + "\r\nGüncelleme geçmişindeki son kurulum: " + (last == null ? "yok" : Fmt.Date(last.Value.ToLocalTime()) + " (" + lastTitle + ")")
                         + "\r\n\r\nMicrosoft her ayın ikinci salısı güvenlik yaması yayınlar. 45 günden eski sunucuda bilinen ve aktif kullanılan açıklar kapanmamış olabilir.",
                HowToFix = status == CheckStatus.Gecti ? null
                    : "Ayarlar → Windows Update → Güncelleştirmeleri denetle → yükleyip mesai dışında yeniden başlatın. Güncelleme hata veriyorsa Windows Update önbelleğini temizlemek (Performans Önerileri) işe yarayabilir."
            };
        }

        private CheckItemDto CheckOsVersion()
        {
            const string path = @"SOFTWARE\Microsoft\Windows NT\CurrentVersion";
            string product = Reg(path, "ProductName") as string ?? "Windows";
            string type = Reg(path, "InstallationType") as string ?? "";
            int build;
            int.TryParse(Reg(path, "CurrentBuildNumber") as string, out build);
            bool server = type.IndexOf("Server", StringComparison.OrdinalIgnoreCase) >= 0;

            DateTime? end = null;
            string name = product;
            if (server)
            {
                if (build <= 7601) { end = new DateTime(2020, 1, 14); name = "Windows Server 2008 R2 veya eski"; }
                else if (build <= 9600) { end = new DateTime(2023, 10, 10); name = "Windows Server 2012 / 2012 R2"; }
                else if (build <= 14393) { end = new DateTime(2027, 1, 12); name = "Windows Server 2016"; }
                else if (build <= 17763) { end = new DateTime(2029, 1, 9); name = "Windows Server 2019"; }
                else if (build <= 20348) { end = new DateTime(2031, 10, 14); name = "Windows Server 2022"; }
                else if (build <= 26100) { end = new DateTime(2034, 11, 4); name = "Windows Server 2025"; }
            }
            else if (build <= 19045)
            {
                end = new DateTime(2025, 10, 14);
                name = "Windows 10";
            }

            string summary = name + " (derleme " + build + (product != name ? ", " + product : "") + ")";
            if (end == null)
                return new CheckItemDto { Status = server ? CheckStatus.Gecti : CheckStatus.Bilgi, Summary = summary + (server ? "." : ": masaüstü Windows sunucu olarak kullanılıyor.") };

            int daysLeft = (int)(end.Value - DateTime.Today).TotalDays;
            var status = daysLeft < 0 ? CheckStatus.Kritik : daysLeft < 365 ? CheckStatus.Uyari : CheckStatus.Gecti;
            return new CheckItemDto
            {
                Status = status,
                Summary = summary + (daysLeft < 0 ? ": desteği " + end.Value.ToString("dd.MM.yyyy") + " tarihinde BİTTİ, güvenlik yaması gelmiyor."
                                                  : ": destek bitişi " + end.Value.ToString("dd.MM.yyyy") + (daysLeft < 365 ? " (" + daysLeft + " gün kaldı)." : ".")),
                Detail = "Desteği biten Windows'a yeni bulunan açıklar için yama çıkmaz; fidye yazılımlarının en kolay hedefidir.",
                HowToFix = status == CheckStatus.Gecti ? null : "Daha yeni bir Windows Server sürümüne geçişi planlayın (yeni sunucuya taşıma veya yerinde yükseltme)."
            };
        }

        private CheckItemDto CheckSmb1()
        {
            bool? enabled = null;
            try
            {
                using (var s = new ManagementObjectSearcher(@"root\Microsoft\Windows\SMB", "SELECT EnableSMB1Protocol FROM MSFT_SmbServerConfiguration"))
                    foreach (ManagementObject o in s.Get())
                        using (o) enabled = (bool)o["EnableSMB1Protocol"];
            }
            catch (ManagementException)
            {
                enabled = null;
            }
            if (enabled == null)
            {
                int? v = RegInt(@"SYSTEM\CurrentControlSet\Services\LanmanServer\Parameters", "SMB1");
                if (v != null) enabled = v != 0;
                // SMB1 özelliği kurulu değilse sürücüsü (mrxsmb10 / srv) yoktur: 2016 ve sonrasında varsayılan
                else if (Reg(@"SYSTEM\CurrentControlSet\Services\mrxsmb10", "Start") == null && Reg(@"SYSTEM\CurrentControlSet\Services\srv", "Start") == null) enabled = false;
            }
            if (enabled == null) return new CheckItemDto { Status = CheckStatus.Bilgi, Summary = "SMBv1 durumu okunamadı." };
            if (enabled == false) return new CheckItemDto { Status = CheckStatus.Gecti, Summary = "SMBv1 kapalı." };
            return new CheckItemDto
            {
                Status = CheckStatus.Kritik,
                Summary = "SMBv1 açık.",
                Detail = "SMBv1, WannaCry / NotPetya fidye yazılımlarının yayıldığı eski dosya paylaşım protokolüdür. Windows 2000 / XP döneminden kalma cihazlar dışında gerekmez.",
                HowToFix = "Eski bir tarayıcı / NAS SMBv1 kullanıyorsa önce onu güncelleyin. Sonra script ile kapatın.",
                Script = "Set-SmbServerConfiguration -EnableSMB1Protocol $false -Force",
                ScriptLanguage = "powershell"
            };
        }

        private CheckItemDto CheckEventLogSize()
        {
            var limits = new[] { Tuple.Create("Security", 256L), Tuple.Create("Application", 64L), Tuple.Create("System", 32L) };
            var small = new List<string>();
            var detail = new StringBuilder();
            var script = new StringBuilder();
            foreach (var l in limits)
            {
                using (var cfg = new EventLogConfiguration(l.Item1))
                {
                    long mb = cfg.MaximumSizeInBytes / 1024 / 1024;
                    detail.AppendLine("  " + l.Item1 + ": " + mb + " MB (önerilen en az " + l.Item2 + " MB)");
                    if (mb < l.Item2)
                    {
                        small.Add(l.Item1 + " " + mb + " MB");
                        script.AppendLine("wevtutil sl " + l.Item1 + " /ms:" + (l.Item2 * 1024 * 1024));
                    }
                }
            }
            if (small.Count == 0) return new CheckItemDto { Status = CheckStatus.Gecti, Summary = "Olay logu boyutları yeterli.", Detail = detail.ToString() };
            return new CheckItemDto
            {
                Status = CheckStatus.Uyari,
                Summary = "Küçük loglar: " + string.Join(", ", small) + ".",
                Detail = "Log dolunca en eski kayıtlar silinir. RDP brute force yoğun sunucuda Security logu birkaç saatte dolabilir; bir saldırının izi kaybolur. "
                         + "SQL Server izleme olayları Application loguna yazılır.\r\n\r\n" + detail,
                HowToFix = "Olay Görüntüleyicisi → log → Özellikler → En büyük günlük boyutu. Veya script (hemen geçerli).",
                Script = script.ToString(),
                ScriptLanguage = "powershell"
            };
        }

        private CheckItemDto CheckRemoteRegistry()
        {
            try
            {
                using (var sc = new ServiceController("RemoteRegistry"))
                {
                    bool running = sc.Status == ServiceControllerStatus.Running;
                    bool auto = sc.StartType == ServiceStartMode.Automatic;
                    if (!running && !auto) return new CheckItemDto { Status = CheckStatus.Gecti, Summary = "Kapalı." };
                    return new CheckItemDto
                    {
                        Status = CheckStatus.Uyari,
                        Summary = running ? "Çalışıyor." : "Otomatik başlıyor.",
                        Detail = "Ağdaki başka bir bilgisayardan bu sunucunun kayıt defterini okumaya / değiştirmeye izin verir. Saldırganlar ağ içinde yayılırken kullanır.",
                        HowToFix = "services.msc → Uzak Kayıt Defteri → Başlangıç türü: Devre Dışı → Durdur. Bir yönetim yazılımı gerektiriyorsa notla görmezden gelin.",
                        Script = "Stop-Service RemoteRegistry\r\nSet-Service RemoteRegistry -StartupType Disabled",
                        ScriptLanguage = "powershell"
                    };
                }
            }
            catch (InvalidOperationException)
            {
                return new CheckItemDto { Status = CheckStatus.Gecti, Summary = "Servis yok." };
            }
        }

        private static readonly Regex RemoteTool = new Regex(
            @"anydesk|teamviewer|rustdesk|ultravnc|tightvnc|realvnc|vnc server|chrome remote desktop|splashtop|supremo|ammyy|radmin|logmein|screenconnect|connectwise|remote utilities|dameware|parsec|getscreen",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

        private CheckItemDto CheckRemoteTools()
        {
            var found = new SortedSet<string>(StringComparer.OrdinalIgnoreCase);
            using (var hklm = Hklm())
            {
                foreach (var path in new[] { @"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall", @"SOFTWARE\WOW6432Node\Microsoft\Windows\CurrentVersion\Uninstall" })
                    using (var root = hklm.OpenSubKey(path))
                    {
                        if (root == null) continue;
                        foreach (var sub in root.GetSubKeyNames())
                            using (var key = root.OpenSubKey(sub))
                            {
                                string name = key == null ? null : key.GetValue("DisplayName") as string;
                                if (!string.IsNullOrEmpty(name) && RemoteTool.IsMatch(name)) found.Add(name);
                            }
                    }
            }
            // Kullanıcıya kurulan (HKEY_USERS) ve kurulmadan çalışan (taşınabilir) programlar da sayılır
            using (var users = RegistryKey.OpenBaseKey(RegistryHive.Users, RegistryView.Registry64))
            {
                foreach (var sid in users.GetSubKeyNames().Where(n => n.StartsWith("S-1-5-21-") && !n.EndsWith("_Classes")))
                    using (var root = users.OpenSubKey(sid + @"\Software\Microsoft\Windows\CurrentVersion\Uninstall"))
                    {
                        if (root == null) continue;
                        foreach (var sub in root.GetSubKeyNames())
                            using (var key = root.OpenSubKey(sub))
                            {
                                string name = key == null ? null : key.GetValue("DisplayName") as string;
                                if (!string.IsNullOrEmpty(name) && RemoteTool.IsMatch(name)) found.Add(name + " (kullanıcıya kurulu)");
                            }
                    }
            }
            foreach (var sc in ServiceController.GetServices())
                using (sc)
                    if (RemoteTool.IsMatch(sc.DisplayName) && sc.Status == ServiceControllerStatus.Running) found.Add(sc.DisplayName + " (servis çalışıyor)");
            foreach (var proc in System.Diagnostics.Process.GetProcesses())
                using (proc)
                    if (RemoteTool.IsMatch(proc.ProcessName) && !found.Any(f => f.IndexOf(proc.ProcessName, StringComparison.OrdinalIgnoreCase) >= 0))
                        found.Add(proc.ProcessName + " (çalışıyor)");

            if (found.Count == 0) return new CheckItemDto { Status = CheckStatus.Gecti, Summary = "RDP dışında uzaktan erişim programı bulunamadı." };
            return new CheckItemDto
            {
                Status = CheckStatus.Uyari,
                Summary = found.Count + " uzaktan erişim programı: " + string.Join(", ", found.Take(3)) + (found.Count > 3 ? "..." : "") + ".",
                Detail = "Bu programlar RDP'den bağımsız ikinci bir kapıdır: SecurityGuard'ın RDP brute force korumasının dışında kalır, "
                         + "şifresi zayıfsa veya hesabı ele geçirilirse sunucuya doğrudan erişilir. Fidye yazılımı çeteleri sıkça kullanır.\r\n\r\n  " + string.Join("\r\n  ", found),
                HowToFix = "Gerçekten kullanılmıyorsa kaldırın. Kullanılıyorsa: güçlü şifre + iki adımlı doğrulama, \"katılımsız erişimi\" kapatın, erişimi sadece belirli hesaplara verin. "
                           + "Bilerek kullanılıyorsa notla görmezden gelin."
            };
        }
    }
}
