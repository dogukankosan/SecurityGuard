using System;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Management;
using System.Security.Cryptography;
using System.Text;
using Microsoft.Win32;
using SecurityGuardService.Core.Logging;

namespace SecurityGuardService.Licensing
{
    /// <summary>
    /// Makinenin donanım kimliği: SG1-AAAAAAAA-BBBBBBBB-CCCCCCCC-DDDDDDDD.
    /// Her parça bir bileşenin SHA-256 özetinin ilk 4 baytı (ham değerler dışarı çıkmaz):
    ///   A Windows MachineGuid, B anakart seri no (yoksa SMBIOS UUID), C işlemci kimliği, D sistem diskinin seri no (yoksa birim seri no).
    /// Lisans sunucusu 4 parçanın 3'ü tutarsa geçerli sayar: tek bileşen değişince (disk / anakart değişimi) lisans düşmez.
    /// Okunamayan parça sabit "yok" değerinin özeti olur; hesap açılışta bir kez yapılır.
    /// </summary>
    internal static class HardwareId
    {
        private const string Module = "Lisans";
        private static readonly Lazy<string> Current = new Lazy<string>(Compute);

        public static string Value
        {
            get { return Current.Value; }
        }

        private static string Compute()
        {
            string machineGuid = Safe("MachineGuid", MachineGuid);
            string board = Safe("anakart", () => Usable(Wmi("Win32_BaseBoard", "SerialNumber")) ?? Usable(Wmi("Win32_ComputerSystemProduct", "UUID")));
            string cpu = Safe("işlemci", () => Usable(Wmi("Win32_Processor", "ProcessorId")) ?? Usable(Wmi("Win32_Processor", "Name")));
            string disk = Safe("disk", SystemDiskSerial);
            string id = "SG1-" + Part("guid", machineGuid) + "-" + Part("board", board) + "-" + Part("cpu", cpu) + "-" + Part("disk", disk);
            AppLog.Info(Module, "Donanım kimliği: " + id);
            return id;
        }

        private static string Part(string kind, string value)
        {
            using (var sha = SHA256.Create())
            {
                byte[] hash = sha.ComputeHash(Encoding.UTF8.GetBytes("SG1|" + kind + "|" + (value ?? "yok").Trim().ToUpperInvariant()));
                return string.Concat(hash.Take(4).Select(b => b.ToString("X2", CultureInfo.InvariantCulture)));
            }
        }

        private static string Safe(string what, Func<string> read)
        {
            try
            {
                return read();
            }
            catch (Exception ex)
            {
                AppLog.Warn(Module, "Donanım bilgisi okunamadı (" + what + "): " + ex.Message);
                return null;
            }
        }

        private static string MachineGuid()
        {
            using (var key = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, RegistryView.Registry64).OpenSubKey(@"SOFTWARE\Microsoft\Cryptography"))
                return Usable(key == null ? null : key.GetValue("MachineGuid") as string);
        }

        private static string Wmi(string cls, string property)
        {
            using (var searcher = new ManagementObjectSearcher("SELECT " + property + " FROM " + cls))
            using (var results = searcher.Get())
                foreach (ManagementObject o in results)
                    using (o)
                    {
                        string value = Usable(Convert.ToString(o[property], CultureInfo.InvariantCulture));
                        if (value != null) return value;
                    }
            return null;
        }

        /// <summary>Windows'un kurulu olduğu fiziksel diskin seri numarası; okunamazsa sistem biriminin seri numarası.</summary>
        private static string SystemDiskSerial()
        {
            string drive = Path.GetPathRoot(Environment.SystemDirectory).TrimEnd('\\'); // "C:"
            try
            {
                using (var parts = new ManagementObjectSearcher("ASSOCIATORS OF {Win32_LogicalDisk.DeviceID='" + drive + "'} WHERE AssocClass = Win32_LogicalDiskToPartition"))
                using (var partResults = parts.Get())
                    foreach (ManagementObject part in partResults)
                        using (part)
                        using (var disks = new ManagementObjectSearcher("ASSOCIATORS OF {Win32_DiskPartition.DeviceID='" + part["DeviceID"] + "'} WHERE AssocClass = Win32_DiskDriveToDiskPartition"))
                        using (var diskResults = disks.Get())
                            foreach (ManagementObject disk in diskResults)
                                using (disk)
                                {
                                    string serial = Usable(Convert.ToString(disk["SerialNumber"], CultureInfo.InvariantCulture));
                                    if (serial != null) return serial;
                                }
            }
            catch (ManagementException)
            {
                // birim seri numarasına düşülür
            }
            using (var volumes = new ManagementObjectSearcher("SELECT VolumeSerialNumber FROM Win32_LogicalDisk WHERE DeviceID = '" + drive + "'"))
            using (var results = volumes.Get())
                foreach (ManagementObject v in results)
                    using (v)
                        return Usable(Convert.ToString(v["VolumeSerialNumber"], CultureInfo.InvariantCulture));
            return null;
        }

        /// <summary>Üreticinin doldurmadığı / anlamsız değerler kimliğe katılmaz.</summary>
        private static string Usable(string value)
        {
            if (value == null) return null;
            value = value.Trim();
            if (value.Length < 3) return null;
            string upper = value.ToUpperInvariant();
            if (upper.All(c => c == '0' || c == 'F' || c == '-' || c == ' ')) return null;
            string[] junk = { "TO BE FILLED BY O.E.M.", "DEFAULT STRING", "NONE", "NOT APPLICABLE", "NOT SPECIFIED", "SYSTEM SERIAL NUMBER", "BASE BOARD SERIAL NUMBER", "123456789", "OEM" };
            return junk.Contains(upper) ? null : value;
        }
    }
}
