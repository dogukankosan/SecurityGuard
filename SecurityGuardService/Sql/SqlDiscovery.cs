using System;
using System.Collections.Generic;
using System.Linq;
using System.ServiceProcess;
using Microsoft.Win32;
using SecurityGuardService.Core.Logging;

namespace SecurityGuardService.Sql
{
    /// <summary>Sunucuda kurulu bulunan SQL Server instance'ı.</summary>
    internal sealed class DetectedInstance
    {
        public string Name;
        public string Version;
        public string Edition;
        public int TcpPort;
        public string ServiceState;
    }

    /// <summary>
    /// Sunucudaki SQL Server instance'larını registry'den bulur (64 ve 32 bit kurulumlar).
    /// Sadece okur; hiçbir ayara dokunmaz.
    /// </summary>
    internal static class SqlDiscovery
    {
        private const string Module = "SQL";
        private const string DefaultInstance = "MSSQLSERVER";

        public static List<DetectedInstance> Detect()
        {
            var result = new List<DetectedInstance>();
            foreach (var view in new[] { RegistryView.Registry64, RegistryView.Registry32 })
            {
                try
                {
                    using (var baseKey = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, view))
                    using (var names = baseKey.OpenSubKey(@"SOFTWARE\Microsoft\Microsoft SQL Server\Instance Names\SQL"))
                    {
                        if (names == null) continue;
                        foreach (string name in names.GetValueNames())
                        {
                            if (result.Any(r => string.Equals(r.Name, name, StringComparison.OrdinalIgnoreCase))) continue;
                            string id = names.GetValue(name) as string;
                            result.Add(Read(baseKey, name, id));
                        }
                    }
                }
                catch (Exception ex)
                {
                    AppLog.Warn(Module, "SQL instance'ları okunamadı (" + view + ").", ex);
                }
            }
            return result;
        }

        public static bool IsDefault(string instanceName)
        {
            return string.Equals(instanceName, DefaultInstance, StringComparison.OrdinalIgnoreCase);
        }

        /// <summary>Varsayılan instance için "localhost", adlı instance için "localhost\AD".</summary>
        public static string SuggestServer(string instanceName)
        {
            return IsDefault(instanceName) ? "localhost" : @"localhost\" + instanceName;
        }

        private static DetectedInstance Read(RegistryKey baseKey, string name, string id)
        {
            var item = new DetectedInstance { Name = name, ServiceState = ServiceState(name) };
            if (string.IsNullOrEmpty(id)) return item;

            using (var setup = baseKey.OpenSubKey(@"SOFTWARE\Microsoft\Microsoft SQL Server\" + id + @"\Setup"))
            {
                if (setup != null)
                {
                    item.Version = setup.GetValue("Version") as string;
                    item.Edition = setup.GetValue("Edition") as string;
                }
            }
            using (var tcp = baseKey.OpenSubKey(@"SOFTWARE\Microsoft\Microsoft SQL Server\" + id + @"\MSSQLServer\SuperSocketNetLib\Tcp\IPAll"))
            {
                if (tcp != null)
                {
                    int port;
                    string raw = (tcp.GetValue("TcpPort") as string ?? "").Split(',')[0].Trim();
                    if (int.TryParse(raw, out port)) item.TcpPort = port;
                }
            }
            return item;
        }

        private static string ServiceState(string instanceName)
        {
            string service = IsDefault(instanceName) ? DefaultInstance : "MSSQL$" + instanceName;
            try
            {
                using (var sc = new ServiceController(service))
                {
                    switch (sc.Status)
                    {
                        case ServiceControllerStatus.Running: return "Çalışıyor";
                        case ServiceControllerStatus.Stopped: return "Durmuş";
                        case ServiceControllerStatus.StartPending: return "Başlıyor";
                        case ServiceControllerStatus.StopPending: return "Duruyor";
                        default: return "Bilinmiyor";
                    }
                }
            }
            catch
            {
                return "Bilinmiyor";
            }
        }
    }
}
