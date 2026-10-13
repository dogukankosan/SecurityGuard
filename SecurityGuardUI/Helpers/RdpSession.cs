using System;
using System.Net;
using System.Runtime.InteropServices;

namespace SecurityGuardUI.Helpers
{
    /// <summary>
    /// Bu RDP oturumunun bağlandığı istemci IP'si (sunucunun gördüğü adres; ban motorunun da göreceği adres budur).
    /// Beyaz listeye "kendi IP'mi ekle" için kullanılır. Konsol oturumunda veya alınamazsa null.
    /// </summary>
    internal static class RdpSession
    {
        private const int CurrentSession = -1;
        private const int WTSClientAddress = 14;
        private const int AF_INET = 2;
        private const int AF_INET6 = 23;

        [DllImport("wtsapi32.dll", SetLastError = true)]
        private static extern bool WTSQuerySessionInformation(IntPtr server, int sessionId, int infoClass, out IntPtr buffer, out int bytes);

        [DllImport("wtsapi32.dll")]
        private static extern void WTSFreeMemory(IntPtr memory);

        public static string GetClientAddress()
        {
            IntPtr buffer;
            int bytes;
            try
            {
                if (!WTSQuerySessionInformation(IntPtr.Zero, CurrentSession, WTSClientAddress, out buffer, out bytes))
                    return null;
            }
            catch (DllNotFoundException)
            {
                return null;
            }

            try
            {
                // WTS_CLIENT_ADDRESS: DWORD AddressFamily; BYTE Address[20]
                int family = Marshal.ReadInt32(buffer);
                byte[] raw = new byte[20];
                Marshal.Copy(IntPtr.Add(buffer, 4), raw, 0, 20);

                if (family == AF_INET)
                    return raw[2] + "." + raw[3] + "." + raw[4] + "." + raw[5];
                if (family == AF_INET6)
                {
                    byte[] v6 = new byte[16];
                    Array.Copy(raw, 2, v6, 0, 16);
                    return new IPAddress(v6).ToString();
                }
                return null;
            }
            finally
            {
                WTSFreeMemory(buffer);
            }
        }
    }
}
