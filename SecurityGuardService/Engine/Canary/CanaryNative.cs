using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics.Eventing.Reader;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Security.AccessControl;
using System.Security.Principal;
using Microsoft.Win32;
using SecurityGuardService.Engine.Collectors;

namespace SecurityGuardService.Engine.Canary
{
    /// <summary>Tuzak dosyalar için Windows yardımcıları (sadece okuma + kendi dosyalarımızın denetim işareti).</summary>
    internal static class CanaryNative
    {
        // ================================================================ Paylaşımlar

        internal sealed class Share
        {
            public string Name;
            public string Path;
        }

        /// <summary>Disk paylaşımları (C$, ADMIN$, IPC$ gibi yönetim paylaşımları ve yazıcılar hariç).</summary>
        public static List<Share> DiskShares()
        {
            var list = new List<Share>();
            using (var key = Registry.LocalMachine.OpenSubKey(@"SYSTEM\CurrentControlSet\Services\LanmanServer\Shares"))
            {
                if (key == null) return list;
                foreach (string name in key.GetValueNames())
                {
                    if (name.EndsWith("$", StringComparison.Ordinal)) continue;
                    var values = key.GetValue(name) as string[];
                    if (values == null) continue;
                    string path = Value(values, "Path");
                    string type = Value(values, "Type") ?? "0";
                    if (string.IsNullOrEmpty(path) || type != "0") continue; // 0 = disk
                    list.Add(new Share { Name = name, Path = path });
                }
            }
            return list;
        }

        private static string Value(string[] values, string name)
        {
            string prefix = name + "=";
            var v = values.FirstOrDefault(x => x.StartsWith(prefix, StringComparison.OrdinalIgnoreCase));
            return v == null ? null : v.Substring(prefix.Length);
        }

        // ================================================================ SMB oturumları (paylaşıma bağlı bilgisayarlar)

        [DllImport("netapi32.dll", CharSet = CharSet.Unicode)]
        private static extern int NetSessionEnum(string server, string client, string user, int level, out IntPtr buffer, int prefMaxLen,
            out int entriesRead, out int totalEntries, ref int resumeHandle);

        [DllImport("netapi32.dll")]
        private static extern int NetApiBufferFree(IntPtr buffer);

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        private struct SESSION_INFO_10
        {
            public string sesi10_cname;
            public string sesi10_username;
            public int sesi10_time;
            public int sesi10_idle_time;
        }

        internal sealed class SmbSession
        {
            public string Client;
            public string User;
            public int ActiveSeconds;
            public int IdleSeconds;
        }

        /// <summary>Sunucunun paylaşımlarına bağlı oturumlar (bağlanan bilgisayar, kullanıcı, boşta kalma süresi).</summary>
        public static List<SmbSession> SmbSessions()
        {
            var list = new List<SmbSession>();
            IntPtr buffer;
            int read, total, resume = 0;
            int rc = NetSessionEnum(null, null, null, 10, out buffer, -1, out read, out total, ref resume);
            if (rc != 0) throw new Win32Exception(rc);
            try
            {
                int size = Marshal.SizeOf(typeof(SESSION_INFO_10));
                for (int i = 0; i < read; i++)
                {
                    var s = (SESSION_INFO_10)Marshal.PtrToStructure(new IntPtr(buffer.ToInt64() + i * size), typeof(SESSION_INFO_10));
                    list.Add(new SmbSession
                    {
                        Client = (s.sesi10_cname ?? "").TrimStart('\\'),
                        User = s.sesi10_username,
                        ActiveSeconds = s.sesi10_time,
                        IdleSeconds = s.sesi10_idle_time
                    });
                }
            }
            finally
            {
                NetApiBufferFree(buffer);
            }
            return list;
        }

        // ================================================================ Denetim işareti (SACL)

        [DllImport("advapi32.dll", SetLastError = true)]
        private static extern bool OpenProcessToken(IntPtr process, int access, out IntPtr token);

        [DllImport("advapi32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
        private static extern bool LookupPrivilegeValue(string system, string name, out long luid);

        [DllImport("advapi32.dll", SetLastError = true)]
        private static extern bool AdjustTokenPrivileges(IntPtr token, bool disableAll, ref TOKEN_PRIVILEGES state, int length, IntPtr previous, IntPtr returnLength);

        [DllImport("kernel32.dll")]
        private static extern IntPtr GetCurrentProcess();

        [DllImport("kernel32.dll")]
        private static extern bool CloseHandle(IntPtr handle);

        [StructLayout(LayoutKind.Sequential, Pack = 4)]
        private struct TOKEN_PRIVILEGES
        {
            public int Count;
            public long Luid;
            public int Attributes;
        }

        private static bool _securityPrivilege;

        /// <summary>SACL yazmak için SeSecurityPrivilege (servis SYSTEM'de var ama kapalı gelir).</summary>
        private static void EnableSecurityPrivilege()
        {
            if (_securityPrivilege) return;
            IntPtr token;
            if (!OpenProcessToken(GetCurrentProcess(), 0x0020 | 0x0008, out token)) throw new Win32Exception();
            try
            {
                long luid;
                if (!LookupPrivilegeValue(null, "SeSecurityPrivilege", out luid)) throw new Win32Exception();
                var tp = new TOKEN_PRIVILEGES { Count = 1, Luid = luid, Attributes = 2 };
                if (!AdjustTokenPrivileges(token, false, ref tp, 0, IntPtr.Zero, IntPtr.Zero)) throw new Win32Exception();
                int err = Marshal.GetLastWin32Error();
                if (err == 1300) throw new UnauthorizedAccessException("SeSecurityPrivilege yok (servis yönetici yetkisiyle çalışmıyor).");
            }
            finally
            {
                CloseHandle(token);
            }
            _securityPrivilege = true;
        }

        /// <summary>
        /// Tuzak dosyaya "herkes için yazma / silme / izin değiştirme denetimi" koyar. Windows "Dosya sistemi" denetimi açıksa
        /// dokunan kullanıcı + program Security loguna 4663 olarak düşer. Sadece bizim dosyamıza uygulanır.
        /// </summary>
        public static void SetAudit(string path)
        {
            EnableSecurityPrivilege();
            var security = new FileSecurity();
            security.AddAuditRule(new FileSystemAuditRule(new SecurityIdentifier(WellKnownSidType.WorldSid, null),
                FileSystemRights.WriteData | FileSystemRights.AppendData | FileSystemRights.Delete | FileSystemRights.ChangePermissions | FileSystemRights.TakeOwnership,
                AuditFlags.Success | AuditFlags.Failure));
            File.SetAccessControl(path, security); // sadece denetim bölümü yazılır, erişim izinlerine dokunulmaz
        }

        // ================================================================ 4663 (dosyaya kim dokundu)

        internal sealed class Access
        {
            public DateTime TimeUtc;
            public string Path;
            public string User;
            public string Process;
            public string Accesses;
        }

        /// <summary>Son dakikalardaki 4663 olaylarından verilen dosyalara ait olanlar.</summary>
        public static List<Access> RecentAccesses(ICollection<string> paths, TimeSpan window)
        {
            var result = new List<Access>();
            var set = new HashSet<string>(paths, StringComparer.OrdinalIgnoreCase);
            string xpath = "*[System[(EventID=4663) and TimeCreated[timediff(@SystemTime) <= " + (long)window.TotalMilliseconds + "]]]";
            using (var reader = new EventLogReader(new System.Diagnostics.Eventing.Reader.EventLogQuery("Security", PathType.LogName, xpath) { ReverseDirection = true }))
            {
                for (int n = 0; n < 5000; n++)
                {
                    using (var record = reader.ReadEvent())
                    {
                        if (record == null) break;
                        var e = RawEvent.FromXml("Security", record.ToXml());
                        string obj = e.Get("ObjectName");
                        if (obj == null || !set.Contains(obj)) continue;
                        string user = e.Get("SubjectUserName");
                        string domain = e.Get("SubjectDomainName");
                        result.Add(new Access
                        {
                            TimeUtc = e.TimeUtc,
                            Path = obj,
                            User = string.IsNullOrEmpty(domain) || domain == "-" ? user : domain + "\\" + user,
                            Process = e.Get("ProcessName"),
                            Accesses = AccessText(e.Get("AccessMask"))
                        });
                    }
                }
            }
            return result;
        }

        private static string AccessText(string mask)
        {
            long m;
            if (string.IsNullOrEmpty(mask) || !long.TryParse(mask.Replace("0x", ""), System.Globalization.NumberStyles.HexNumber, null, out m)) return mask;
            var parts = new List<string>();
            if ((m & 0x2) != 0) parts.Add("yazma");
            if ((m & 0x4) != 0) parts.Add("ekleme");
            if ((m & 0x10000) != 0) parts.Add("silme / ad değiştirme");
            if ((m & 0x40000) != 0) parts.Add("izin değiştirme");
            if ((m & 0x80000) != 0) parts.Add("sahiplik alma");
            return parts.Count == 0 ? mask : string.Join(", ", parts);
        }
    }
}
