using System;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Net;
using System.Security.Cryptography;
using System.ServiceProcess;
using System.Text;
using System.Threading;
using SecurityGuard.Shared.Contracts;
using SecurityGuard.Shared.Dto;
using SecurityGuard.Shared.Licensing;

namespace SecurityGuardUpdater
{
    /// <summary>
    /// SecurityGuard merkezi güncelleme aracı. Kurulum kökünde (Service ve UI klasörlerinin yanında) durur,
    /// YÖNETİCİ olarak çalıştırılır. Hub'tan en son sürümü sorar; yeni varsa imzalı paketi indirir + doğrular,
    /// SG yönetici şifresiyle servisi durdurur, dosyaları değiştirir, servisi başlatır (yeni şema migration'ı
    /// açılışta otomatik). Yeni sürüm düzgün açılmazsa eski sürümü geri yükler (rollback).
    ///
    /// Güvenlik: paket RSA imzası + SHA-256 doğrulanmadan HİÇBİR dosya değişmez. Pipe'ın karşısındaki sürecin
    /// gerçek servis olduğu doğrulanır (sahte pipe şifreyi ele geçiremez).
    /// </summary>
    internal static class Program
    {
        private const string ServiceName = "SecurityGuard";

        private static int Main(string[] args)
        {
            try { Console.OutputEncoding = Encoding.UTF8; } catch { }
            try { Console.Title = "SecurityGuard Güncelleme"; } catch { }
            Header();
            try
            {
                Run(args);
                return 0;
            }
            catch (UpdateException ex)
            {
                Line("");
                Line("HATA: " + ex.Message, ConsoleColor.Red);
                Pause();
                return 1;
            }
            catch (Exception ex)
            {
                Line("");
                Line("Beklenmeyen hata: " + ex.Message, ConsoleColor.Red);
                Pause();
                return 2;
            }
        }

        private static void Run(string[] args)
        {
            bool auto = args.Any(a => string.Equals(a, "--yes", StringComparison.OrdinalIgnoreCase) || a == "-y");

            string root = AppDomain.CurrentDomain.BaseDirectory.TrimEnd('\\');
            string serviceDir = Path.Combine(root, "Service");
            string uiDir = Path.Combine(root, "UI");
            string serviceExe = Path.Combine(serviceDir, "SecurityGuardService.exe");
            if (!File.Exists(serviceExe))
                throw new UpdateException("SecurityGuard kurulumu bulunamadı. Bu program kurulum kökünde (Service ve UI klasörlerinin yanında) olmalı.\r\n  Aranan: " + serviceExe);

            using (var pipe = new PipeClient(serviceExe))
            {
                var status = pipe.Status();
                string current = status != null ? status.ServiceVersion : null;
                Line("Mevcut sürüm : " + (current ?? "?"), ConsoleColor.Gray);
                Line("Makine       : " + (status != null && status.MachineName != null ? status.MachineName : Environment.MachineName), ConsoleColor.Gray);
                Line("");

                // Giriş: servisi durdurmak ve lisans adresini almak için gerekli
                Console.Write("SecurityGuard yönetici kullanıcı adı: ");
                string user = Console.ReadLine();
                string pass = ReadPassword("Şifre: ");
                pipe.Login(user, pass);
                Line("Giriş tamam.", ConsoleColor.Green);

                var lic = pipe.License();
                string apiUrl = lic != null ? lic.ApiUrl : null;
                if (string.IsNullOrWhiteSpace(apiUrl)) throw new UpdateException("Lisans sunucusu adresi alınamadı.");

                Line("Sunucuya soruluyor: " + apiUrl, ConsoleColor.Gray);
                var m = FetchManifest(apiUrl, current);
                if (m == null) { Line("Sunucuda tanımlı güncelleme yok.", ConsoleColor.Yellow); Pause(); return; }
                if (!LicenseProtocol.Verify(m)) throw new UpdateException("Güncelleme manifesti imzası DOĞRULANAMADI (sahte sunucu / bozuk cevap). İşlem durduruldu.");
                if (!LicenseProtocol.IsNewerVersion(current, m.Version)) { Line("Zaten güncelsiniz (" + current + ").", ConsoleColor.Green); Pause(); return; }
                if (!LicenseProtocol.MeetsMinVersion(current, m.MinVersion)) throw new UpdateException("Bu sürüme (" + m.Version + ") çıkmak için önce " + m.MinVersion + " sürümüne geçmelisiniz.");

                Line("");
                Line("YENİ SÜRÜM: " + current + "  →  " + m.Version + (m.Mandatory ? "   (ZORUNLU güvenlik güncellemesi)" : ""), ConsoleColor.Cyan);
                if (!string.IsNullOrWhiteSpace(m.Notes)) Line("Not: " + m.Notes, ConsoleColor.Gray);
                Line("Paket boyutu: " + Math.Max(1, m.SizeBytes / 1024 / 1024) + " MB", ConsoleColor.Gray);
                Line("");
                if (!auto && !Confirm("Güncellemeyi şimdi uygulamak istiyor musunuz? (servis kısa süre durur) [E/h]: "))
                { Line("İptal edildi.", ConsoleColor.Yellow); return; }

                string work = Path.Combine(root, "Temp", "Update", Safe(m.Version));
                string zip = Path.Combine(work, "package.zip");
                string staged = Path.Combine(work, "staged");
                string backup = Path.Combine(work, "backup");
                Directory.CreateDirectory(work);

                Line("İndiriliyor...", ConsoleColor.Gray);
                Download(m.Url, zip);
                string sha = Sha256(zip);
                if (!string.Equals(sha, m.Sha256, StringComparison.OrdinalIgnoreCase))
                    throw new UpdateException("İndirilen paketin SHA-256'sı manifestle UYUŞMUYOR: paket reddedildi.\r\n  Beklenen " + m.Sha256 + "\r\n  Gelen    " + sha);
                Line("İmza ve bütünlük doğrulandı. ✓", ConsoleColor.Green);

                if (Directory.Exists(staged)) Directory.Delete(staged, true);
                Directory.CreateDirectory(staged);
                ZipFile.ExtractToDirectory(zip, staged);
                string stagedService = Path.Combine(staged, "Service");
                string stagedUi = Path.Combine(staged, "UI");
                if (!File.Exists(Path.Combine(stagedService, "SecurityGuardService.exe")))
                    throw new UpdateException("Paket beklenen yapıda değil (içinde Service\\SecurityGuardService.exe yok).");

                Line("Servis durduruluyor...", ConsoleColor.Gray);
                pipe.StopService(pass);
                WaitServiceState(ServiceControllerStatus.Stopped, TimeSpan.FromSeconds(90));
                KillUi(uiDir);
                Line("Servis durdu.", ConsoleColor.Green);

                if (Directory.Exists(backup)) Directory.Delete(backup, true);
                Line("Mevcut sürüm yedekleniyor...", ConsoleColor.Gray);
                CopyDir(serviceDir, Path.Combine(backup, "Service"));
                if (Directory.Exists(uiDir)) CopyDir(uiDir, Path.Combine(backup, "UI"));

                try
                {
                    Line("Yeni dosyalar kopyalanıyor...", ConsoleColor.Gray);
                    CopyDir(stagedService, serviceDir);
                    if (Directory.Exists(stagedUi)) CopyDir(stagedUi, uiDir);
                }
                catch (Exception ex)
                {
                    Line("Kopyalama başarısız (" + ex.Message + "), eski sürüm geri yükleniyor...", ConsoleColor.Red);
                    Rollback(backup, serviceDir, uiDir);
                    TryStart();
                    throw new UpdateException("Güncelleme uygulanamadı, eski sürüm geri yüklendi.");
                }

                Line("Servis başlatılıyor...", ConsoleColor.Gray);
                TryStart();
                string newVer = VerifyRunning(serviceExe, TimeSpan.FromSeconds(90));
                if (newVer == null)
                {
                    Line("Yeni sürüm açılışta doğrulanamadı → GERİ ALINIYOR...", ConsoleColor.Red);
                    StopForRollback();
                    Rollback(backup, serviceDir, uiDir);
                    TryStart();
                    throw new UpdateException("Yeni sürüm düzgün başlamadı; eski sürüm geri yüklendi. Logları inceleyin (kök\\Logs).");
                }

                Line("");
                Line("GÜNCELLEME TAMAM: " + current + "  →  " + newVer + ".  Servis çalışıyor. ✓", ConsoleColor.Green);
                try { Directory.Delete(work, true); } catch { }
                Pause();
            }
        }

        // ================================================================ Hub

        private static UpdateManifest FetchManifest(string apiUrl, string version)
        {
            ServicePointManager.SecurityProtocol |= SecurityProtocolType.Tls12;
            string url = (apiUrl.EndsWith("/") ? apiUrl : apiUrl + "/") + LicenseProtocol.UpdatePath;
            var http = (HttpWebRequest)WebRequest.Create(url);
            http.Method = "GET";
            http.Timeout = 30000;
            http.UserAgent = "SecurityGuardUpdater/" + (version ?? "");
            using (var resp = (HttpWebResponse)http.GetResponse())
            {
                if (resp.StatusCode == HttpStatusCode.NoContent) return null;
                using (var sr = new StreamReader(resp.GetResponseStream(), Encoding.UTF8))
                    return PipeProtocol.FromJson<UpdateManifest>(sr.ReadToEnd());
            }
        }

        private static void Download(string url, string file)
        {
            ServicePointManager.SecurityProtocol |= SecurityProtocolType.Tls12;
            var http = (HttpWebRequest)WebRequest.Create(url);
            http.Method = "GET";
            http.Timeout = 30000;
            http.ReadWriteTimeout = 600000;
            using (var resp = (HttpWebResponse)http.GetResponse())
            using (var rs = resp.GetResponseStream())
            using (var fs = File.Create(file))
                rs.CopyTo(fs);
        }

        private static string Sha256(string file)
        {
            using (var s = File.OpenRead(file))
            using (var h = SHA256.Create())
                return BitConverter.ToString(h.ComputeHash(s)).Replace("-", "");
        }

        // ================================================================ Servis

        private static void WaitServiceState(ServiceControllerStatus target, TimeSpan timeout)
        {
            using (var sc = new ServiceController(ServiceName))
            {
                var deadline = DateTime.UtcNow + timeout;
                while (DateTime.UtcNow < deadline)
                {
                    try { sc.Refresh(); if (sc.Status == target) return; } catch { }
                    Thread.Sleep(1000);
                }
                throw new UpdateException("Servis " + (int)timeout.TotalSeconds + " sn içinde '" + target + "' durumuna gelmedi.");
            }
        }

        private static void TryStart()
        {
            using (var sc = new ServiceController(ServiceName))
            {
                try { sc.Refresh(); if (sc.Status == ServiceControllerStatus.Running) return; } catch { }
                try { sc.Start(); } catch (Exception ex) { throw new UpdateException("Servis başlatılamadı: " + ex.Message); }
            }
            WaitServiceState(ServiceControllerStatus.Running, TimeSpan.FromSeconds(60));
        }

        /// <summary>Servisin açılıp istek işler hale geldiğini pipe'tan doğrular; sürümü döner (olmazsa null).</summary>
        private static string VerifyRunning(string serviceExe, TimeSpan timeout)
        {
            var deadline = DateTime.UtcNow + timeout;
            while (DateTime.UtcNow < deadline)
            {
                try
                {
                    using (var pipe = new PipeClient(serviceExe))
                    {
                        var st = pipe.Status();
                        if (st != null && st.Ready) return st.ServiceVersion ?? "?";
                    }
                }
                catch { }
                Thread.Sleep(2000);
            }
            return null;
        }

        private static void StopForRollback()
        {
            try
            {
                using (var sc = new ServiceController(ServiceName))
                {
                    sc.Refresh();
                    if (sc.CanStop) { sc.Stop(); sc.WaitForStatus(ServiceControllerStatus.Stopped, TimeSpan.FromSeconds(20)); }
                }
            }
            catch { }
            foreach (var p in SafeProcesses("SecurityGuardService"))
                try { p.Kill(); p.WaitForExit(5000); } catch { }
            Thread.Sleep(2000);
        }

        private static void KillUi(string uiDir)
        {
            foreach (var p in SafeProcesses("SecurityGuardUI"))
            {
                try
                {
                    if (p.MainModule != null && p.MainModule.FileName.StartsWith(uiDir, StringComparison.OrdinalIgnoreCase))
                    { p.Kill(); p.WaitForExit(5000); }
                }
                catch { }
            }
        }

        private static Process[] SafeProcesses(string name)
        {
            try { return Process.GetProcessesByName(name); } catch { return new Process[0]; }
        }

        // ================================================================ Dosya

        private static void CopyDir(string src, string dst)
        {
            src = Path.GetFullPath(src);
            dst = Path.GetFullPath(dst);
            Directory.CreateDirectory(dst);
            foreach (var f in Directory.GetFiles(src, "*", SearchOption.AllDirectories))
            {
                string rel = f.Substring(src.Length).TrimStart('\\');
                string target = Path.Combine(dst, rel);
                Directory.CreateDirectory(Path.GetDirectoryName(target));
                CopyWithRetry(f, target);
            }
        }

        private static void CopyWithRetry(string from, string to)
        {
            for (int i = 0; ; i++)
            {
                try { try { if (File.Exists(to)) File.SetAttributes(to, FileAttributes.Normal); } catch { } File.Copy(from, to, true); return; }
                catch (IOException) when (i < 10) { Thread.Sleep(500); }
                catch (UnauthorizedAccessException) when (i < 10) { Thread.Sleep(500); }
            }
        }

        private static void Rollback(string backup, string serviceDir, string uiDir)
        {
            string bs = Path.Combine(backup, "Service"), bu = Path.Combine(backup, "UI");
            if (Directory.Exists(bs)) CopyDir(bs, serviceDir);
            if (Directory.Exists(bu)) CopyDir(bu, uiDir);
        }

        private static string Safe(string v)
        {
            return new string((v ?? "").Select(c => char.IsLetterOrDigit(c) || c == '.' ? c : '_').ToArray());
        }

        // ================================================================ Konsol

        private static void Header()
        {
            Line("");
            Line("  SecurityGuard — Merkezi Güncelleme", ConsoleColor.Cyan);
            Line("  ----------------------------------", ConsoleColor.DarkGray);
            Line("");
        }

        private static bool Confirm(string prompt)
        {
            Console.Write(prompt);
            string a = (Console.ReadLine() ?? "").Trim().ToLowerInvariant();
            return a == "e" || a == "evet" || a == "y" || a == "yes";
        }

        private static string ReadPassword(string prompt)
        {
            Console.Write(prompt);
            var sb = new StringBuilder();
            ConsoleKeyInfo k;
            while ((k = Console.ReadKey(true)).Key != ConsoleKey.Enter)
            {
                if (k.Key == ConsoleKey.Backspace) { if (sb.Length > 0) sb.Length--; }
                else if (!char.IsControl(k.KeyChar)) sb.Append(k.KeyChar);
            }
            Console.WriteLine();
            return sb.ToString();
        }

        private static void Line(string text, ConsoleColor color = ConsoleColor.Gray)
        {
            var old = Console.ForegroundColor;
            Console.ForegroundColor = color;
            Console.WriteLine(text);
            Console.ForegroundColor = old;
        }

        private static void Pause()
        {
            Line("");
            Line("Kapatmak için bir tuşa basın...", ConsoleColor.DarkGray);
            try { Console.ReadKey(true); } catch { }
        }
    }
}
