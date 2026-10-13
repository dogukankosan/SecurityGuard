using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text;

namespace SecurityGuardService.Checks.Security
{
    /// <summary>Güvenlik taraması için Windows okuma yardımcıları (audit politikası, yerel güvenlik politikası, firewall, dinleyen portlar). Hiçbir şey değiştirmez.</summary>
    internal static class SecurityNative
    {
        // ================================================================ Audit politikası

        [StructLayout(LayoutKind.Sequential)]
        private struct AUDIT_POLICY_INFORMATION
        {
            public Guid AuditSubCategoryGuid;
            public uint AuditingInformation;
            public Guid AuditCategoryGuid;
        }

        [DllImport("advapi32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.U1)]
        private static extern bool AuditQuerySystemPolicy(Guid[] subCategoryGuids, uint policyCount, out IntPtr auditPolicy);

        [DllImport("advapi32.dll")]
        private static extern void AuditFree(IntPtr buffer);

        public const uint AuditSuccess = 1;
        public const uint AuditFailure = 2;

        /// <summary>Alt kategori başına (başarı / başarısızlık bayrakları).</summary>
        public static Dictionary<Guid, uint> AuditPolicy(IEnumerable<Guid> subCategories)
        {
            var guids = subCategories.ToArray();
            IntPtr buffer;
            if (!AuditQuerySystemPolicy(guids, (uint)guids.Length, out buffer)) throw new System.ComponentModel.Win32Exception();
            try
            {
                var result = new Dictionary<Guid, uint>();
                int size = Marshal.SizeOf(typeof(AUDIT_POLICY_INFORMATION));
                for (int i = 0; i < guids.Length; i++)
                {
                    var info = (AUDIT_POLICY_INFORMATION)Marshal.PtrToStructure(new IntPtr(buffer.ToInt64() + i * size), typeof(AUDIT_POLICY_INFORMATION));
                    result[info.AuditSubCategoryGuid] = info.AuditingInformation;
                }
                return result;
            }
            finally
            {
                AuditFree(buffer);
            }
        }

        // ================================================================ Yerel güvenlik politikası (secedit, sadece dışa aktarma)

        /// <summary>secedit /export ile [System Access] bölümü (ör. MinimumPasswordLength, PasswordComplexity, LockoutBadCount).</summary>
        public static Dictionary<string, string> SystemAccessPolicy()
        {
            string file = Core.AppPaths.TempFile("sg_secpol_", ".inf");
            try
            {
                var psi = new ProcessStartInfo(Path.Combine(Environment.SystemDirectory, "secedit.exe"), "/export /cfg \"" + file + "\" /areas SECURITYPOLICY /quiet")
                {
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true
                };
                using (var p = Process.Start(psi))
                {
                    p.StandardOutput.ReadToEnd();
                    if (!p.WaitForExit(30000)) throw new TimeoutException("secedit zamanında bitmedi.");
                }
                if (!File.Exists(file)) throw new InvalidOperationException("Güvenlik politikası okunamadı (secedit çıktı üretmedi).");

                var map = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                bool inSection = false;
                foreach (var raw in File.ReadAllLines(file, Encoding.Unicode))
                {
                    string line = raw.Trim();
                    if (line.StartsWith("[")) { inSection = line.Equals("[System Access]", StringComparison.OrdinalIgnoreCase); continue; }
                    if (!inSection) continue;
                    int eq = line.IndexOf('=');
                    if (eq > 0) map[line.Substring(0, eq).Trim()] = line.Substring(eq + 1).Trim().Trim('"');
                }
                return map;
            }
            finally
            {
                try { File.Delete(file); } catch { }
            }
        }

        // ================================================================ Firewall (sadece okuma)

        public sealed class FirewallRule
        {
            public string Name;
            public string LocalPorts;
            public string RemoteAddresses;
            public string Application;
            public string Service;
            public int Protocol;
            public int Profiles;
        }

        public sealed class FirewallState
        {
            /// <summary>Profil (1 etki alanı, 2 özel, 4 genel) → açık mı.</summary>
            public Dictionary<int, bool> Enabled = new Dictionary<int, bool>();
            public int CurrentProfiles;

            /// <summary>Etkin, gelen, izin veren kurallar.</summary>
            public List<FirewallRule> AllowIn = new List<FirewallRule>();

            /// <summary>Etkin SecurityGuard port kısıtı kuralları (gelen, engelle: izinli IP'ler dışındakiler).</summary>
            public List<FirewallRule> OurRestrictions = new List<FirewallRule>();

            /// <summary>Diğer etkin gelen ENGELLE kuralları (SecurityGuard ban / kısıt hariç).</summary>
            public List<FirewallRule> BlockIn = new List<FirewallRule>();
        }

        public static FirewallState Firewall()
        {
            var state = new FirewallState();
            var type = Type.GetTypeFromProgID("HNetCfg.FwPolicy2", true);
            object policy = Activator.CreateInstance(type);
            try
            {
                foreach (int profile in new[] { 1, 2, 4 })
                    state.Enabled[profile] = (bool)type.InvokeMember("FirewallEnabled", BindingFlags.GetProperty, null, policy, new object[] { profile });
                state.CurrentProfiles = (int)type.InvokeMember("CurrentProfileTypes", BindingFlags.GetProperty, null, policy, null);

                // Kural koleksiyonu, numaralandırıcı ve her kural FirewallCom içinde bırakılır (bırakılmazsa çağrı başına ~1 MB kalır)
                Engine.Firewall.FirewallCom.ForEachRule(policy, rule =>
                {
                    try
                    {
                        Func<string, string> str = name => Engine.Firewall.FirewallCom.Str(rule, name);
                        Func<string, int> num = name => Engine.Firewall.FirewallCom.Int(rule, name);
                        if (!Engine.Firewall.FirewallCom.Bool(rule, "Enabled") || num("Direction") != 1) return;
                        int action = num("Action");
                        if (action == 0)
                        {
                            string blockName = str("Name");
                            if (blockName != null && blockName.StartsWith(SecurityGuard.Shared.GuardConstants.PortRestrictionRulePrefix + "-", StringComparison.OrdinalIgnoreCase))
                                state.OurRestrictions.Add(new FirewallRule { Name = blockName, LocalPorts = str("LocalPorts"), Protocol = num("Protocol"), Profiles = num("Profiles") });
                            else
                                state.BlockIn.Add(new FirewallRule { Name = blockName, LocalPorts = str("LocalPorts"), RemoteAddresses = str("RemoteAddresses"), Application = str("ApplicationName"), Protocol = num("Protocol"), Profiles = num("Profiles") });
                            return;
                        }
                        if (action != 1) return;
                        state.AllowIn.Add(new FirewallRule
                        {
                            Name = str("Name"),
                            LocalPorts = str("LocalPorts"),
                            RemoteAddresses = str("RemoteAddresses"),
                            Application = str("ApplicationName"),
                            Service = str("serviceName"),
                            Protocol = num("Protocol"),
                            Profiles = num("Profiles")
                        });
                    }
                    catch (Exception)
                    {
                        // tek bir kural okunamazsa diğerleri yine okunur
                    }
                });
            }
            finally
            {
                Marshal.ReleaseComObject(policy);
            }
            return state;
        }

        /// <summary>Kuralın portu kapsıyor mu ("*", "3389", "135,445", "5000-5100").</summary>
        public static bool PortMatches(string localPorts, int port)
        {
            if (string.IsNullOrEmpty(localPorts) || localPorts == "*") return true;
            foreach (var part in localPorts.Split(','))
            {
                string p = part.Trim();
                int dash = p.IndexOf('-');
                int a, b;
                if (dash > 0 && int.TryParse(p.Substring(0, dash), out a) && int.TryParse(p.Substring(dash + 1), out b))
                {
                    if (port >= a && port <= b) return true;
                }
                else if (int.TryParse(p, out a) && a == port) return true;
            }
            return false;
        }

        // ================================================================ Dinleyen TCP portları (+ işlem)

        [DllImport("iphlpapi.dll", SetLastError = true)]
        private static extern uint GetExtendedTcpTable(IntPtr table, ref int size, bool order, int af, int tableClass, uint reserved);

        private const int TcpTableOwnerPidListener = 3;

        [DllImport("iphlpapi.dll", SetLastError = true)]
        private static extern uint GetExtendedUdpTable(IntPtr table, ref int size, bool order, int af, int tableClass, uint reserved);

        private const int UdpTableOwnerPid = 1;

        public sealed class Listener
        {
            /// <summary>"TCP" veya "UDP".</summary>
            public string Protocol = "TCP";
            public IPAddress Address;
            public int Port;
            public int Pid;
            public string ProcessName;
            public string ProcessPath;
        }

        /// <summary>Dinleyen TCP portları (IPv4 + IPv6), sahibi işlemle. Sadece loopback'te dinleyenler dahil değil.</summary>
        public static List<Listener> TcpListeners()
        {
            return Listeners(false, false);
        }

        /// <summary>Dinleyen TCP (ve istenirse UDP) portları, sahibi işlemle.</summary>
        public static List<Listener> Listeners(bool udp, bool includeLoopback)
        {
            var list = new List<Listener>();
            list.AddRange(ReadTable(2));   // AF_INET
            list.AddRange(ReadTable(23));  // AF_INET6
            if (udp)
            {
                list.AddRange(ReadUdpTable(2));
                list.AddRange(ReadUdpTable(23));
            }
            var names = new Dictionary<int, Tuple<string, string>>();
            foreach (var l in list)
            {
                Tuple<string, string> info;
                if (!names.TryGetValue(l.Pid, out info))
                {
                    string name = null, path = null;
                    try
                    {
                        using (var p = Process.GetProcessById(l.Pid))
                        {
                            name = p.ProcessName;
                            try { path = p.MainModule.FileName; } catch { }
                        }
                    }
                    catch { }
                    names[l.Pid] = info = Tuple.Create(name, path);
                }
                l.ProcessName = info.Item1;
                l.ProcessPath = info.Item2;
            }
            return list.Where(l => includeLoopback || !IPAddress.IsLoopback(l.Address)).ToList();
        }

        private static List<Listener> ReadUdpTable(int family)
        {
            var list = new List<Listener>();
            int size = 0;
            GetExtendedUdpTable(IntPtr.Zero, ref size, false, family, UdpTableOwnerPid, 0);
            IntPtr buffer = Marshal.AllocHGlobal(size);
            try
            {
                if (GetExtendedUdpTable(buffer, ref size, false, family, UdpTableOwnerPid, 0) != 0) return list;
                int count = Marshal.ReadInt32(buffer);
                IntPtr row = new IntPtr(buffer.ToInt64() + 4);
                for (int i = 0; i < count; i++)
                {
                    if (family == 2)
                    {
                        // MIB_UDPROW_OWNER_PID: localAddr, localPort, pid
                        uint addr = (uint)Marshal.ReadInt32(row, 0);
                        list.Add(new Listener { Protocol = "UDP", Address = new IPAddress(addr), Port = PortFrom(Marshal.ReadInt32(row, 4)), Pid = Marshal.ReadInt32(row, 8) });
                        row = new IntPtr(row.ToInt64() + 12);
                    }
                    else
                    {
                        // MIB_UDP6ROW_OWNER_PID: localAddr[16], localScope, localPort, pid
                        var bytes = new byte[16];
                        Marshal.Copy(row, bytes, 0, 16);
                        list.Add(new Listener { Protocol = "UDP", Address = new IPAddress(bytes), Port = PortFrom(Marshal.ReadInt32(row, 20)), Pid = Marshal.ReadInt32(row, 24) });
                        row = new IntPtr(row.ToInt64() + 28);
                    }
                }
            }
            finally
            {
                Marshal.FreeHGlobal(buffer);
            }
            return list;
        }

        private static List<Listener> ReadTable(int family)
        {
            var list = new List<Listener>();
            int size = 0;
            GetExtendedTcpTable(IntPtr.Zero, ref size, false, family, TcpTableOwnerPidListener, 0);
            IntPtr buffer = Marshal.AllocHGlobal(size);
            try
            {
                if (GetExtendedTcpTable(buffer, ref size, false, family, TcpTableOwnerPidListener, 0) != 0) return list;
                int count = Marshal.ReadInt32(buffer);
                IntPtr row = new IntPtr(buffer.ToInt64() + 4);
                for (int i = 0; i < count; i++)
                {
                    if (family == 2)
                    {
                        // MIB_TCPROW_OWNER_PID: state, localAddr, localPort, remoteAddr, remotePort, pid (6 x 4 bayt)
                        uint addr = (uint)Marshal.ReadInt32(row, 4);
                        int port = PortFrom(Marshal.ReadInt32(row, 8));
                        int pid = Marshal.ReadInt32(row, 20);
                        list.Add(new Listener { Address = new IPAddress(addr), Port = port, Pid = pid });
                        row = new IntPtr(row.ToInt64() + 24);
                    }
                    else
                    {
                        // MIB_TCP6ROW_OWNER_PID: localAddr[16], localScope, localPort, remoteAddr[16], remoteScope, remotePort, state, pid
                        var bytes = new byte[16];
                        Marshal.Copy(row, bytes, 0, 16);
                        int port = PortFrom(Marshal.ReadInt32(row, 20));
                        int pid = Marshal.ReadInt32(row, 52);
                        list.Add(new Listener { Address = new IPAddress(bytes), Port = port, Pid = pid });
                        row = new IntPtr(row.ToInt64() + 56);
                    }
                }
            }
            finally
            {
                Marshal.FreeHGlobal(buffer);
            }
            return list;
        }

        private static int PortFrom(int raw)
        {
            return ((raw & 0xFF) << 8) | ((raw >> 8) & 0xFF);
        }
    }
}
