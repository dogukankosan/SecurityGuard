using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Management;
using System.Net;
using SecurityGuard.Shared;
using SecurityGuard.Shared.Dto;
using SecurityGuardService.Core;
using SecurityGuardService.Core.Data;
using SecurityGuardService.Core.Logging;
using SecurityGuardService.Engine.Rules;
using SecurityGuardService.Sql;

namespace SecurityGuardService.Checks.Security
{
    /// <summary>
    /// Açık Portlar: sunucuda dinleyen her port için hangi program / servis, firewall'da kime açık, riskli mi,
    /// son 24 saatte o protokolde kaç başarısız giriş var. Sadece okur ve öneri verir; hiçbir port kapatılmaz.
    /// </summary>
    internal static class OpenPorts
    {
        private const string Module = "AçıkPortlar";

        private sealed class Known
        {
            public string Label;
            public bool HighRisk;
            public string EventType;
            public string Advice;
        }

        private static readonly Dictionary<string, Known> KnownPorts = new Dictionary<string, Known>
        {
            { "TCP/21", new Known { Label = "FTP", HighRisk = true, EventType = EventCatalog.FtpLoginFailed, Advice = "FTP şifreleri açık metin gider. Kullanılmıyorsa kapatın; gerekiyorsa sadece bilinen IP'lere açın (SFTP / FTPS tercih edin)." } },
            { "TCP/22", new Known { Label = "SSH", HighRisk = true, Advice = "Sadece yönetim IP'lerine açın." } },
            { "TCP/23", new Known { Label = "Telnet", HighRisk = true, Advice = "Telnet şifresizdir; kapatın." } },
            { "TCP/25", new Known { Label = "SMTP", EventType = EventCatalog.SmtpLoginFailed, Advice = "Mail sunucusu değilse kapatın. Açık röle olmadığından emin olun." } },
            { "TCP/53", new Known { Label = "DNS" } },
            { "UDP/53", new Known { Label = "DNS" } },
            { "TCP/80", new Known { Label = "Web (HTTP)", EventType = EventCatalog.WebLoginFailed } },
            { "TCP/443", new Known { Label = "Web (HTTPS)", EventType = EventCatalog.WebLoginFailed } },
            { "TCP/110", new Known { Label = "POP3", Advice = "Şifreli sürümü (995) tercih edin." } },
            { "TCP/143", new Known { Label = "IMAP", Advice = "Şifreli sürümü (993) tercih edin." } },
            { "TCP/135", new Known { Label = "RPC", HighRisk = true, Advice = "İnternete asla açılmamalı; sadece iç ağ." } },
            { "TCP/139", new Known { Label = "NetBIOS", HighRisk = true, EventType = EventCatalog.NetLoginFailed, Advice = "İnternete asla açılmamalı; sadece iç ağ." } },
            { "UDP/137", new Known { Label = "NetBIOS ad hizmeti", Advice = "Sadece iç ağ." } },
            { "UDP/138", new Known { Label = "NetBIOS datagram", Advice = "Sadece iç ağ." } },
            { "TCP/445", new Known { Label = "SMB (dosya paylaşımı)", HighRisk = true, EventType = EventCatalog.NetLoginFailed, Advice = "İnternete asla açılmamalı (WannaCry gibi saldırıların hedefi). Sadece iç ağa / belirli IP'lere açın." } },
            { "TCP/587", new Known { Label = "SMTP (gönderim)", EventType = EventCatalog.SmtpLoginFailed } },
            { "TCP/993", new Known { Label = "IMAPS" } },
            { "TCP/995", new Known { Label = "POP3S" } },
            { "TCP/1433", new Known { Label = "SQL Server", HighRisk = true, EventType = EventCatalog.SqlLoginFailed, Advice = "SQL Server internete açılmamalı; uzaktan erişim gerekiyorsa sadece belirli IP'lere / ülkeye açın." } },
            { "UDP/1434", new Known { Label = "SQL Browser", Advice = "Adlı instance yoksa kapatılabilir; internete açılmamalı." } },
            { "TCP/3306", new Known { Label = "MySQL", HighRisk = true, Advice = "Sadece belirli IP'lere açın." } },
            { "TCP/5432", new Known { Label = "PostgreSQL", HighRisk = true, Advice = "Sadece belirli IP'lere açın." } },
            { "TCP/5900", new Known { Label = "VNC", HighRisk = true, Advice = "VNC zayıf şifrelidir; kapatın veya sadece belirli IP'lere açın." } },
            { "TCP/5938", new Known { Label = "TeamViewer", Advice = "Uzaktan erişim programı; kullanılmıyorsa kaldırın." } },
            { "TCP/7070", new Known { Label = "AnyDesk", Advice = "Uzaktan erişim programı; kullanılmıyorsa kaldırın." } },
            { "TCP/5985", new Known { Label = "WinRM (HTTP)", HighRisk = true, EventType = EventCatalog.NetLoginFailed, Advice = "Uzaktan PowerShell yönetimi; sadece yönetim IP'lerine açın." } },
            { "TCP/5986", new Known { Label = "WinRM (HTTPS)", HighRisk = true, EventType = EventCatalog.NetLoginFailed, Advice = "Uzaktan PowerShell yönetimi; sadece yönetim IP'lerine açın." } },
            { "TCP/8080", new Known { Label = "Web (alternatif)", EventType = EventCatalog.WebLoginFailed } },
        };

        public static OpenPortsDto Build(GuardContext g)
        {
            return Build(g.Database, g.PortRestrictions, g.IisLogs);
        }

        public static OpenPortsDto Build(GuardDatabase db, Engine.Firewall.PortRestrictions portRestrictions, Engine.Collectors.IisLogCollector iisLogs)
        {
            var result = new OpenPortsDto { GeneratedUtc = DbTime.Now() };
            var known = new Dictionary<string, Known>(KnownPorts);
            int rdp = RdpPort();
            known["TCP/" + rdp] = new Known { Label = "RDP (Uzak Masaüstü)", HighRisk = true, EventType = EventCatalog.RdpLoginFailed,
                Advice = "En çok saldırılan port. Sadece belirli IP'lere / Türkiye'ye açın (Port Kısıtları) veya VPN arkasına alın." };
            known["UDP/" + rdp] = new Known { Label = "RDP (UDP)", HighRisk = true, Advice = "RDP'nin UDP kanalı; TCP ile birlikte kısıtlayın (Port Kısıtları → TCP + UDP)." };
            try
            {
                foreach (var i in SqlDiscovery.Detect().Where(i => i.TcpPort > 0))
                    known["TCP/" + i.TcpPort] = new Known { Label = "SQL Server (" + i.Name + ")", HighRisk = true, EventType = EventCatalog.SqlLoginFailed, Advice = KnownPorts["TCP/1433"].Advice };
            }
            catch (Exception ex)
            {
                AppLog.Debug(Module, "SQL portları okunamadı: " + ex.Message);
            }

            var fw = SecurityNative.Firewall();
            bool firewallOff = fw.Enabled.Any(p => !p.Value && (fw.CurrentProfiles & p.Key) != 0);
            if (firewallOff) result.FirewallWarning = "Windows Firewall etkin profilde KAPALI: dinleyen tüm portlar herkese açık.";

            var restrictions = portRestrictions.GetState().Items.Where(i => i.Enabled).ToList();
            var services = ServicesByPid();
            var failures = Failures24h(db);
            result.WatchedLogs = iisLogs.Watched.Select(d => d.Name).ToList();

            var listeners = SecurityNative.Listeners(true, true);
            foreach (var grp in listeners.GroupBy(l => l.Protocol + "/" + l.Port).OrderBy(x => x.First().Protocol == "TCP" ? 0 : 1).ThenBy(x => x.First().Port))
            {
                var first = grp.First();
                bool localOnly = grp.All(l => IPAddress.IsLoopback(l.Address));
                Known k;
                known.TryGetValue(grp.Key, out k);
                var pids = grp.Select(l => l.Pid).Distinct().ToList();
                var svc = pids.SelectMany(p => services.ContainsKey(p) ? services[p] : new List<string>()).Distinct().ToList();

                var dto = new OpenPortDto
                {
                    Protocol = first.Protocol,
                    Port = first.Port,
                    Label = k == null ? null : k.Label,
                    Addresses = AddressText(grp.Select(l => l.Address)),
                    Process = string.Join(", ", grp.Select(l => l.ProcessName ?? (l.Pid == 4 ? "System" : "?")).Distinct()),
                    ProcessPath = grp.Select(l => l.ProcessPath).FirstOrDefault(p => p != null),
                    Services = string.Join(", ", svc),
                    Failures24h = k != null && k.EventType != null && failures.ContainsKey(k.EventType) ? failures[k.EventType] : (k != null && k.EventType != null ? 0 : -1)
                };

                int proto = first.Protocol == "TCP" ? 6 : 17;
                var ours = restrictions.FirstOrDefault(r => (r.Protocol == "BOTH" || r.Protocol == first.Protocol) && SecurityNative.PortMatches(r.Ports, first.Port));
                var block = fw.BlockIn.FirstOrDefault(r => (r.Profiles & fw.CurrentProfiles) != 0 && (r.Protocol == proto || r.Protocol == 256) && IsAny(r.RemoteAddresses)
                                                           && !string.IsNullOrEmpty(r.LocalPorts) && r.LocalPorts != "*" && SecurityNative.PortMatches(r.LocalPorts, first.Port));
                var allows = fw.AllowIn.Where(r => (r.Profiles & fw.CurrentProfiles) != 0 && (r.Protocol == proto || r.Protocol == 256) && Matches(r, first.Port, dto.ProcessPath, svc, pids.Contains(4))).ToList();

                if (localOnly)
                {
                    dto.Exposure = "Sadece bu sunucu";
                    dto.ExposureDetail = "Sadece localhost'ta dinliyor; ağdan erişilemez.";
                }
                else if (firewallOff)
                {
                    dto.Exposure = "Herkese açık";
                    dto.ExposureDetail = "Firewall kapalı.";
                }
                else if (ours != null)
                {
                    dto.Exposure = "SecurityGuard kısıtı";
                    dto.ExposureDetail = "Port kısıtı: " + ours.Name + " (" + ours.State + ")";
                }
                else if (block != null)
                {
                    dto.Exposure = "Kapalı";
                    dto.ExposureDetail = "Engelleme kuralı: " + block.Name;
                }
                else if (allows.Any(r => IsAny(r.RemoteAddresses)))
                {
                    dto.Exposure = "Herkese açık";
                    dto.ExposureDetail = "Kural: " + string.Join(", ", allows.Where(r => IsAny(r.RemoteAddresses)).Select(r => r.Name).Distinct().Take(3));
                }
                else if (allows.Count > 0)
                {
                    dto.Exposure = "Kısıtlı";
                    dto.ExposureDetail = string.Join("; ", allows.Select(r => r.Name + ": " + RemoteText(r.RemoteAddresses)).Distinct().Take(3));
                }
                else
                {
                    dto.Exposure = "Kapalı";
                    dto.ExposureDetail = "İzin veren firewall kuralı yok.";
                }

                bool open = dto.Exposure == "Herkese açık";
                bool high = k != null && k.HighRisk;
                dto.Risk = open ? (high ? "Yüksek" : "Orta") : dto.Exposure == "Kısıtlı" && high ? "Düşük" : "";
                dto.CanRestrict = !localOnly && ours == null && dto.Exposure != "Kapalı";
                dto.Advice = Advice(dto, k, open);
                result.Ports.Add(dto);
            }
            return result;
        }

        private static string Advice(OpenPortDto dto, Known k, bool open)
        {
            if (dto.Exposure == "Sadece bu sunucu" || dto.Exposure == "Kapalı") return null;
            if (dto.Exposure == "SecurityGuard kısıtı") return "Kısıtlı (SecurityGuard).";
            string baseAdvice = k != null && k.Advice != null ? k.Advice
                : open ? "Bu portu kullanan bir hizmet yoksa programı / servisi kapatın; gerekiyorsa sadece belirli IP'lere açın." : null;
            if (open && dto.Failures24h > 0) baseAdvice = "Son 24 saatte " + dto.Failures24h + " başarısız giriş! " + baseAdvice;
            return baseAdvice;
        }

        /// <summary>Kural bu porta / programa / servise uyuyor mu (eski açık port kontrolüyle aynı mantık + servis adı).</summary>
        private static bool Matches(SecurityNative.FirewallRule r, int port, string processPath, List<string> services, bool kernel)
        {
            // "System" = Windows çekirdeği (PID 4): SMB 445, NetBIOS 139, HTTP.sys (WinRM 5985, IIS 80/443); dosya yolu yok
            bool appMatch = !string.IsNullOrEmpty(r.Application)
                            && (kernel && string.Equals(r.Application, "System", StringComparison.OrdinalIgnoreCase)
                                || processPath != null && string.Equals(Environment.ExpandEnvironmentVariables(r.Application), processPath, StringComparison.OrdinalIgnoreCase));
            bool serviceMatch = !string.IsNullOrEmpty(r.Service) && r.Service != "*" && services.Any(s => string.Equals(s, r.Service, StringComparison.OrdinalIgnoreCase));
            bool anyPort = string.IsNullOrEmpty(r.LocalPorts) || r.LocalPorts == "*";
            if (!string.IsNullOrEmpty(r.Service) && r.Service != "*" && !serviceMatch) return false;
            if (!string.IsNullOrEmpty(r.Application) && !appMatch) return false;
            if (anyPort)
            {
                // Çekirdeğe (System) bağlı "tüm portlar" kuralları özel amaçlıdır (Wi-Fi Direct, Teredo...: belirli arayüzlerle sınırlı); sayılmaz
                bool kernelRule = string.Equals(r.Application, "System", StringComparison.OrdinalIgnoreCase);
                return (appMatch && !kernelRule) || serviceMatch;
            }
            return SecurityNative.PortMatches(r.LocalPorts, port);
        }

        private static bool IsAny(string remote)
        {
            return string.IsNullOrEmpty(remote) || remote == "*";
        }

        private static string RemoteText(string remote)
        {
            if (string.IsNullOrEmpty(remote)) return "herkes";
            if (remote.Equals("LocalSubnet", StringComparison.OrdinalIgnoreCase)) return "sadece yerel alt ağ";
            var parts = remote.Split(',');
            return parts.Length <= 3 ? remote : string.Join(",", parts.Take(3)) + " (+" + (parts.Length - 3) + ")";
        }

        private static string AddressText(IEnumerable<IPAddress> addresses)
        {
            var list = addresses.Select(a => a.Equals(IPAddress.Any) || a.Equals(IPAddress.IPv6Any) ? "tüm adresler" : a.ToString()).Distinct().ToList();
            return list.Contains("tüm adresler") ? "tüm adresler" : string.Join(", ", list.Take(4)) + (list.Count > 4 ? " ..." : "");
        }

        private static Dictionary<int, List<string>> ServicesByPid()
        {
            var map = new Dictionary<int, List<string>>();
            try
            {
                using (var searcher = new ManagementObjectSearcher("SELECT Name, ProcessId FROM Win32_Service WHERE ProcessId <> 0"))
                    foreach (ManagementObject s in searcher.Get())
                        using (s)
                        {
                            int pid = Convert.ToInt32(s["ProcessId"], CultureInfo.InvariantCulture);
                            List<string> list;
                            if (!map.TryGetValue(pid, out list)) map[pid] = list = new List<string>();
                            list.Add((string)s["Name"]);
                        }
            }
            catch (Exception ex)
            {
                AppLog.Debug(Module, "Servisler okunamadı: " + ex.Message);
            }
            return map;
        }

        private static Dictionary<string, int> Failures24h(GuardDatabase db)
        {
            string from = DbTime.ToDb(DateTime.UtcNow.AddDays(-1));
            return db.Read(c =>
            {
                var d = new Dictionary<string, int>();
                using (var cmd = c.Create("SELECT EventType, count(*) FROM EventLog WHERE TimeUtc >= @f AND EventType LIKE '%.LoginFailed' GROUP BY EventType", ("@f", from)))
                using (var r = cmd.ExecuteReader())
                    while (r.Read()) d[r.GetString(0)] = r.GetInt32(1);
                return d;
            });
        }

        private static int RdpPort()
        {
            try
            {
                using (var key = Microsoft.Win32.Registry.LocalMachine.OpenSubKey(@"SYSTEM\CurrentControlSet\Control\Terminal Server\WinStations\RDP-Tcp"))
                {
                    object v = key == null ? null : key.GetValue("PortNumber");
                    if (v is int && (int)v > 0) return (int)v;
                }
            }
            catch (Exception)
            {
            }
            return 3389;
        }
    }
}
