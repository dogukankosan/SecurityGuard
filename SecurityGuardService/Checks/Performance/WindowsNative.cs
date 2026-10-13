using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;

namespace SecurityGuardService.Checks.Performance
{
    /// <summary>Windows API çağrıları (bellek, işlemci süresi, RDP oturumları). Sadece okur.</summary>
    internal static class WindowsNative
    {
        // ================================================================ Bellek / işlemci

        [StructLayout(LayoutKind.Sequential)]
        private struct MEMORYSTATUSEX
        {
            public uint dwLength;
            public uint dwMemoryLoad;
            public ulong ullTotalPhys;
            public ulong ullAvailPhys;
            public ulong ullTotalPageFile;
            public ulong ullAvailPageFile;
            public ulong ullTotalVirtual;
            public ulong ullAvailVirtual;
            public ulong ullAvailExtendedVirtual;
        }

        [DllImport("kernel32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool GlobalMemoryStatusEx(ref MEMORYSTATUSEX buffer);

        /// <summary>(toplam bayt, boş bayt, kullanım %, commit limiti, boş commit).</summary>
        public static (ulong Total, ulong Available, int LoadPercent, ulong CommitLimit, ulong CommitAvailable) Memory()
        {
            var m = new MEMORYSTATUSEX { dwLength = (uint)Marshal.SizeOf(typeof(MEMORYSTATUSEX)) };
            if (!GlobalMemoryStatusEx(ref m)) throw new System.ComponentModel.Win32Exception();
            return (m.ullTotalPhys, m.ullAvailPhys, (int)m.dwMemoryLoad, m.ullTotalPageFile, m.ullAvailPageFile);
        }

        [DllImport("kernel32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool GetSystemTimes(out long idle, out long kernel, out long user);

        /// <summary>(boşta, toplam) süre sayaçları; iki ölçüm farkından CPU kullanımı hesaplanır.</summary>
        public static (long Idle, long Total) CpuTimes()
        {
            long idle, kernel, user;
            if (!GetSystemTimes(out idle, out kernel, out user)) throw new System.ComponentModel.Win32Exception();
            return (idle, kernel + user); // kernel süresi boşta süreyi de içerir
        }

        // ================================================================ RDP oturumları

        private enum WTS_INFO_CLASS
        {
            WTSUserName = 5,
            WTSDomainName = 7,
            WTSClientAddress = 14,
            WTSSessionInfo = 24
        }

        private const int WTSDisconnected = 4;

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        private struct WTS_SESSION_INFO
        {
            public int SessionId;
            public IntPtr pWinStationName;
            public int State;
        }

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        private struct WTSINFO
        {
            public int State;
            public int SessionId;
            public int IncomingBytes;
            public int OutgoingBytes;
            public int IncomingFrames;
            public int OutgoingFrames;
            public int IncomingCompressedBytes;
            public int OutgoingCompressedBytes;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)] public string WinStationName;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 17)] public string Domain;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 21)] public string UserName;
            public long ConnectTime;
            public long DisconnectTime;
            public long LastInputTime;
            public long LogonTime;
            public long CurrentTime;
        }

        [DllImport("wtsapi32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool WTSEnumerateSessions(IntPtr hServer, int reserved, int version, out IntPtr ppSessionInfo, out int count);

        [DllImport("wtsapi32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool WTSQuerySessionInformation(IntPtr hServer, int sessionId, WTS_INFO_CLASS infoClass, out IntPtr buffer, out int bytes);

        [DllImport("wtsapi32.dll")]
        private static extern void WTSFreeMemory(IntPtr memory);

        public sealed class Session
        {
            public int Id;
            public string User;
            public bool Disconnected;
            public DateTime? DisconnectTime;
            public DateTime? LogonTime;

            /// <summary>RDP istemcisinin IP'si (konsol oturumunda null).</summary>
            public string ClientAddress;
        }

        /// <summary>Kullanıcısı olan oturumlar (konsol + RDP).</summary>
        public static List<Session> Sessions()
        {
            var list = new List<Session>();
            IntPtr buffer;
            int count;
            if (!WTSEnumerateSessions(IntPtr.Zero, 0, 1, out buffer, out count)) throw new System.ComponentModel.Win32Exception();
            try
            {
                int size = Marshal.SizeOf(typeof(WTS_SESSION_INFO));
                for (int i = 0; i < count; i++)
                {
                    var s = (WTS_SESSION_INFO)Marshal.PtrToStructure(new IntPtr(buffer.ToInt64() + i * size), typeof(WTS_SESSION_INFO));
                    string user = QueryString(s.SessionId, WTS_INFO_CLASS.WTSUserName);
                    if (string.IsNullOrEmpty(user)) continue;
                    string domain = QueryString(s.SessionId, WTS_INFO_CLASS.WTSDomainName);
                    var session = new Session
                    {
                        Id = s.SessionId,
                        User = string.IsNullOrEmpty(domain) ? user : domain + "\\" + user,
                        Disconnected = s.State == WTSDisconnected
                    };
                    ReadTimes(session);
                    session.ClientAddress = ReadClientAddress(s.SessionId);
                    list.Add(session);
                }
            }
            finally
            {
                WTSFreeMemory(buffer);
            }
            return list;
        }

        private static string QueryString(int sessionId, WTS_INFO_CLASS info)
        {
            IntPtr buffer;
            int bytes;
            if (!WTSQuerySessionInformation(IntPtr.Zero, sessionId, info, out buffer, out bytes)) return null;
            try
            {
                return Marshal.PtrToStringUni(buffer);
            }
            finally
            {
                WTSFreeMemory(buffer);
            }
        }

        private static string ReadClientAddress(int sessionId)
        {
            IntPtr buffer;
            int bytes;
            if (!WTSQuerySessionInformation(IntPtr.Zero, sessionId, WTS_INFO_CLASS.WTSClientAddress, out buffer, out bytes)) return null;
            try
            {
                // WTS_CLIENT_ADDRESS: DWORD AddressFamily; BYTE Address[20]
                int family = Marshal.ReadInt32(buffer);
                byte[] raw = new byte[20];
                Marshal.Copy(IntPtr.Add(buffer, 4), raw, 0, 20);
                if (family == 2) return raw[2] + "." + raw[3] + "." + raw[4] + "." + raw[5];
                if (family == 23)
                {
                    byte[] v6 = new byte[16];
                    Array.Copy(raw, 2, v6, 0, 16);
                    return new System.Net.IPAddress(v6).ToString();
                }
                return null;
            }
            finally
            {
                WTSFreeMemory(buffer);
            }
        }

        private static void ReadTimes(Session session)
        {
            IntPtr buffer;
            int bytes;
            if (!WTSQuerySessionInformation(IntPtr.Zero, session.Id, WTS_INFO_CLASS.WTSSessionInfo, out buffer, out bytes)) return;
            try
            {
                var info = (WTSINFO)Marshal.PtrToStructure(buffer, typeof(WTSINFO));
                session.DisconnectTime = FromFileTime(info.DisconnectTime);
                session.LogonTime = FromFileTime(info.LogonTime);
            }
            finally
            {
                WTSFreeMemory(buffer);
            }
        }

        private static DateTime? FromFileTime(long value)
        {
            if (value <= 0) return null;
            try
            {
                return DateTime.FromFileTime(value);
            }
            catch (ArgumentOutOfRangeException)
            {
                return null;
            }
        }
    }
}
