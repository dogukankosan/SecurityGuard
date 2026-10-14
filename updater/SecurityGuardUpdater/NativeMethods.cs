using System;
using System.IO.Pipes;
using System.Runtime.InteropServices;
using System.Text;
using Microsoft.Win32.SafeHandles;

namespace SecurityGuardUpdater
{
    /// <summary>Pipe'ın karşı ucundaki sürecin exe yolunu bulur (sahte pipe'a şifre göndermemek için kimlik doğrulama).</summary>
    internal static class NativeMethods
    {
        private const uint ProcessQueryLimitedInformation = 0x1000;

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool GetNamedPipeServerProcessId(SafePipeHandle pipe, out uint serverProcessId);

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern SafeProcessHandle OpenProcess(uint access, bool inheritHandle, uint processId);

        [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
        private static extern bool QueryFullProcessImageName(SafeProcessHandle process, int flags, StringBuilder exeName, ref int size);

        public static string GetServerProcessPath(PipeStream pipe)
        {
            uint pid;
            if (!GetNamedPipeServerProcessId(pipe.SafePipeHandle, out pid)) return null;

            using (var process = OpenProcess(ProcessQueryLimitedInformation, false, pid))
            {
                if (process.IsInvalid) return null;
                var sb = new StringBuilder(1024);
                int size = sb.Capacity;
                return QueryFullProcessImageName(process, 0, sb, ref size) ? sb.ToString(0, size) : null;
            }
        }
    }
}
