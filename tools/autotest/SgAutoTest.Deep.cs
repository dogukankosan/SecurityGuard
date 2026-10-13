using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Threading;
using SecurityGuard.Shared.Contracts;
using SecurityGuard.Shared.Dto;

// Derin testler ("-tam" içinde; tek başına: -tam -sadece geriyukle,gercekbakim,zamanlama):
//  - geriyukle  : yedeği alınan test veritabanını arşivden açıp FARKLI ADLA geri yükler, veriyi birebir karşılaştırır;
//                 veritabanı çevrimdışıyken / hedef sürücü yokken "yedekleme başarısız" alarmını doğrular.
//  - gercekbakim: bilerek parçalanmış test veritabanında bakımı GERÇEK modda çalıştırır, parçalanmanın düştüğünü ölçer.
//  - zamanlama  : yedek ve günlük rapor saatini 2 dakika sonrasına alır, kendiliğinden çalıştıklarını doğrular.
// Sadece kendi oluşturduğu SgAutoTest* veritabanlarına dokunur; yedek / bakım / rapor ayarlarını test sonunda eski haline getirir.
partial class SgAutoTest
{
    const string BakDb = "SgAutoTestBak";
    const string BakDbRestored = "SgAutoTestBak_R";
    const string FragDb = "SgAutoTestFrag";

    // ================================================================ Ortak

    static bool SqlReady(out string server, out string instance)
    {
        server = instance = null;
        var inst = Send<List<SqlInstanceDto>>(Commands.SqlInstances, null).FirstOrDefault(i => i.IsConfigured && i.IsEnabled);
        if (inst == null) { Warn("Yapılandırılmış SQL instance yok: bölüm atlandı."); return false; }
        server = inst.Server + (inst.Port > 0 ? "," + inst.Port : "");
        instance = inst.InstanceName;
        try
        {
            RunOut("sqlcmd.exe", "-?");
        }
        catch (System.ComponentModel.Win32Exception)
        {
            Warn("sqlcmd.exe bu makinede yok: bölüm atlandı.");
            return false;
        }
        string admin = SqlScalar(server, "SELECT IS_SRVROLEMEMBER('sysadmin')");
        if (admin != "1") { Warn("sqlcmd ile Windows kimliğiyle sysadmin bağlanılamadı (" + Short(admin) + "): bölüm atlandı."); return false; }
        return true;
    }

    /// <summary>Tek değer dönen sorgu (hata olursa "HATA: ..." döner).</summary>
    static string SqlScalar(string server, string sql)
    {
        var r = RunOut("sqlcmd.exe", "-S " + server + " -C -E -b -h -1 -W -Q \"SET NOCOUNT ON; " + sql.Replace("\"", "\\\"") + "\"");
        return r.Item1 == 0 ? r.Item2.Trim() : "HATA: " + r.Item2.Trim();
    }

    static BackupConfigDto Clone(BackupConfigDto c)
    {
        return new BackupConfigDto
        {
            Enabled = c.Enabled, Days = new List<int>(c.Days ?? new List<int>()), StartTime = c.StartTime, Databases = new List<string>(c.Databases ?? new List<string>()),
            TargetFolder = c.TargetFolder, RetentionDays = c.RetentionDays, CopyOnlyMode = c.CopyOnlyMode, RarPath = c.RarPath
        };
    }

    static MaintenanceConfigDto Clone(MaintenanceConfigDto c)
    {
        return new MaintenanceConfigDto
        {
            Enabled = c.Enabled, DryRun = c.DryRun, Days = new List<int>(c.Days ?? new List<int>()), StartTime = c.StartTime, MaxMinutes = c.MaxMinutes,
            Indexes = c.Indexes, Statistics = c.Statistics, ShrinkLog = c.ShrinkLog, Databases = new List<string>(c.Databases ?? new List<string>())
        };
    }

    /// <summary>Yedeği elle başlatır ve bitmesini bekler. Yeni çalıştırma oluşmadıysa null.</summary>
    static BackupRunDto RunBackup(out List<BackupFileDto> files)
    {
        files = new List<BackupFileDto>();
        var bs = Send<BackupStateDto>(Commands.BackupGet, null);
        string last = bs.Runs != null && bs.Runs.Count > 0 ? bs.Runs[0].RunId : null;
        var started = client.SendAsync<BackupStateDto>(Commands.BackupRun, null, TimeSpan.FromMinutes(2)).Result;
        if (!started.Success) { Fail("Yedek başlatılamadı: " + started.ErrorMessage); return null; }
        Thread.Sleep(1500);
        WaitUntil(() => !(bs = Send<BackupStateDto>(Commands.BackupGet, null)).IsRunning, 900, true);
        var run = bs.Runs != null && bs.Runs.Count > 0 ? bs.Runs[0] : null;
        if (run == null || run.RunId == last) return null;
        files = Send<List<BackupFileDto>>(Commands.BackupFiles, new RunIdRequest { RunId = run.RunId });
        return run;
    }

    static MaintenanceRunDto RunMaintenance(bool dryRun, out List<MaintenanceOpDto> ops)
    {
        ops = new List<MaintenanceOpDto>();
        var ms = Send<MaintenanceStateDto>(Commands.MaintenanceGet, null);
        string last = ms.Runs != null && ms.Runs.Count > 0 ? ms.Runs[0].RunId : null;
        var started = client.SendAsync<MaintenanceStateDto>(Commands.MaintenanceRun, new MaintenanceRunRequest { DryRun = dryRun }, TimeSpan.FromMinutes(2)).Result;
        if (!started.Success) { Fail("Bakım başlatılamadı: " + started.ErrorMessage); return null; }
        Thread.Sleep(1500);
        WaitUntil(() => !(ms = Send<MaintenanceStateDto>(Commands.MaintenanceGet, null)).IsRunning, 900, true);
        var run = ms.Runs != null && ms.Runs.Count > 0 ? ms.Runs[0] : null;
        if (run == null || run.RunId == last) return null;
        ops = Send<List<MaintenanceOpDto>>(Commands.MaintenanceOps, new RunIdRequest { RunId = run.RunId });
        return run;
    }

    // ================================================================ Yedekten geri yükleme + hata durumları

    static void RestoreTests()
    {
        Title("YEDEKTEN GERİ YÜKLEME VE YEDEK HATA DURUMLARI (test veritabanı " + BakDb + ")");
        string server, instance;
        if (!SqlReady(out server, out instance)) return;
        var state = Send<BackupStateDto>(Commands.BackupGet, null);
        if (string.IsNullOrEmpty(state.Config.TargetFolder) || string.IsNullOrEmpty(state.RarFound) || !state.HasPassword)
        {
            Warn("Yedekleme hazır değil (hedef klasör / WinRAR / arşiv şifresi eksik): bölüm atlandı. SQL Yedekleme sayfasından ayarlayın.");
            return;
        }
        var original = Clone(state.Config);
        string key = instance + "|" + BakDb;
        string marker = Guid.NewGuid().ToString("N");
        string extracted = null;
        try
        {
            // 1) Test veritabanı: 5000 satır + bu çalıştırmaya özel işaret
            Sql(server, "IF DB_ID('" + BakDbRestored + "') IS NOT NULL DROP DATABASE [" + BakDbRestored + "]; IF DB_ID('" + BakDb + "') IS NOT NULL BEGIN ALTER DATABASE [" + BakDb + "] SET ONLINE; DROP DATABASE [" + BakDb + "]; END");
            Check(Sql(server, "CREATE DATABASE [" + BakDb + "];"), "Test veritabanı oluşturuldu");
            Sql(server, "CREATE TABLE [" + BakDb + "].dbo.Veri (Id int IDENTITY PRIMARY KEY, Metin nvarchar(100) NOT NULL, Isaret char(32) NOT NULL); "
                        + "INSERT INTO [" + BakDb + "].dbo.Veri (Metin, Isaret) SELECT TOP (5000) N'satir ' + CAST(ROW_NUMBER() OVER (ORDER BY (SELECT 1)) AS nvarchar(10)), '" + marker + "' FROM sys.all_objects a CROSS JOIN sys.all_objects b;");
            string sumSql = "SELECT CAST(COUNT(*) AS varchar(20)) + '/' + CAST(CHECKSUM_AGG(CHECKSUM(Id, Metin, Isaret)) AS varchar(20)) FROM [{0}].dbo.Veri";
            string sum0 = SqlScalar(server, string.Format(sumSql, BakDb));
            Check(sum0.StartsWith("5000/"), "Test verisi yazıldı (satır / sağlama: " + sum0 + ")");

            // 2) Yedek listesine ekle, yedek al
            var cfg = Clone(original);
            if (!cfg.Databases.Contains(key, StringComparer.OrdinalIgnoreCase)) cfg.Databases.Add(key);
            Send<BackupStateDto>(Commands.BackupSave, cfg);
            List<BackupFileDto> files;
            var run = RunBackup(out files);
            var mine = files.Where(f => string.Equals(f.Database, BakDb, StringComparison.OrdinalIgnoreCase)).ToList();
            Check(run != null && run.Status == "Tamamlandı" && mine.Count > 0 && mine.All(f => f.Result == "Tamam"), "Yedek alındı: " + (run == null ? "-" : run.Status + " | " + Short(run.Summary)));
            var rarStep = mine.FirstOrDefault(f => f.Step == "ARŞİV" && !string.IsNullOrEmpty(f.ArchivePath));
            var bakStep = mine.FirstOrDefault(f => f.Step == "YEDEK" && !string.IsNullOrEmpty(f.ArchivePath));
            if (rarStep == null || bakStep == null || !File.Exists(rarStep.ArchivePath))
            {
                Fail("Arşiv dosyası bulunamadı: geri yükleme denenemedi.");
            }
            else
            {
                string rar = rarStep.ArchivePath, rarExe = state.RarFound, bakDir = Path.GetDirectoryName(bakStep.ArchivePath);
                Info("Arşiv: " + rar + " (" + new FileInfo(rar).Length / 1024 + " KB)");

                // 3) Şifre koruması
                var noPw = RunOut(rarExe, "lb -p- -y \"" + rar + "\"");
                Check(noPw.Item1 != 0 || noPw.Item2.IndexOf(".bak", StringComparison.OrdinalIgnoreCase) < 0, "Şifre olmadan arşivin içindeki dosya adı bile görünmüyor");
                var wrong = RunOut(rarExe, "t -pYanlisSifre123 -y \"" + rar + "\"");
                Check(wrong.Item1 != 0, "Yanlış şifreyle arşiv açılamıyor (Rar çıkış kodu " + wrong.Item1 + ")");

                // 4) Doğru şifreyle aç (SQL Server'ın okuyabildiği yedek klasörüne) ve FARKLI ADLA geri yükle
                string pw = Send<string>(Commands.BackupGetPassword, new PasswordConfirmRequest { Password = loginPassword });
                Check(!string.IsNullOrEmpty(pw), "Arşiv şifresi yönetici şifresiyle alınabildi");
                var ex = RunOut(rarExe, "x \"-p" + pw + "\" -o+ -y \"" + rar + "\" \"" + bakDir + "\\\\\"");
                extracted = Path.Combine(bakDir, Path.GetFileName(bakStep.ArchivePath));
                Check(ex.Item1 == 0 && File.Exists(extracted), "Arşiv doğru şifreyle açıldı (" + (File.Exists(extracted) ? new FileInfo(extracted).Length / 1024 + " KB .bak" : "çıkış kodu " + ex.Item1 + ": " + Short(ex.Item2.Trim())) + ")");
                if (File.Exists(extracted))
                {
                    string dataDir = SqlScalar(server, "SELECT CAST(SERVERPROPERTY('InstanceDefaultDataPath') AS nvarchar(400))");
                    if (!dataDir.EndsWith("\\")) dataDir += "\\";
                    bool restored = Sql(server, "RESTORE DATABASE [" + BakDbRestored + "] FROM DISK = N'" + extracted + "' WITH MOVE N'" + BakDb + "' TO N'" + dataDir + BakDbRestored + ".mdf', MOVE N'" + BakDb
                                                + "_log' TO N'" + dataDir + BakDbRestored + "_log.ldf', REPLACE;");
                    Check(restored, "Yedek farklı adla geri yüklendi (" + BakDbRestored + ")");
                    if (restored)
                    {
                        string sum1 = SqlScalar(server, string.Format(sumSql, BakDbRestored));
                        Check(sum1 == sum0, "Geri yüklenen veri birebir aynı (satır / sağlama: " + sum1 + ")");
                        Check(SqlScalar(server, "SELECT MAX(Isaret) FROM [" + BakDbRestored + "].dbo.Veri") == marker, "Bu çalıştırmanın işareti geri yüklenen veride var (eski bir yedek değil)");
                        Check(SqlScalar(server, "SELECT state_desc FROM sys.databases WHERE name = '" + BakDbRestored + "'") == "ONLINE", "Geri yüklenen veritabanı çevrimiçi");
                    }
                }
            }

            // 5) Hata durumu: veritabanı çevrimdışı → yedek "Hatalı", kritik olay
            long before = LastId();
            Sql(server, "ALTER DATABASE [" + BakDb + "] SET OFFLINE WITH ROLLBACK IMMEDIATE;");
            run = RunBackup(out files);
            var failedStep = files.FirstOrDefault(f => string.Equals(f.Database, BakDb, StringComparison.OrdinalIgnoreCase) && f.Result == "Hata");
            Check(run != null && run.Status == "Hatalı" && failedStep != null, "Çevrimdışı veritabanı: yedek 'Hatalı' bitti" + (failedStep == null ? " (" + (run == null ? "çalışmadı" : run.Status) + ")" : " (" + Short(failedStep.Note) + ")"));
            var others = files.Where(f => !string.Equals(f.Database, BakDb, StringComparison.OrdinalIgnoreCase) && f.Step == "ARŞİV").ToList();
            if (others.Count > 0) Check(others.All(f => f.Result == "Tamam"), "Bir veritabanı hata verse de diğerleri yedeklendi (" + string.Join(", ", others.Select(f => f.Database)) + ")");
            var failEv = WaitEvent("Guard.BackupFailed", before, 30);
            Check(failEv != null, "'SQL yedekleme başarısız' kritik olayı" + MailText(failEv) + (failEv == null ? "" : ": " + Short(failEv.Summary)));
            Sql(server, "ALTER DATABASE [" + BakDb + "] SET ONLINE;");

            // 6) Hata durumu: hedef sürücü yok
            string missing = new[] { "Q", "R", "Y", "W", "V" }.FirstOrDefault(l => !Directory.Exists(l + ":\\"));
            if (missing == null) Warn("Boş sürücü harfi bulunamadı: 'hedef yok' testi atlandı.");
            else
            {
                var bad = Clone(cfg);
                bad.TargetFolder = missing + ":\\SgYedekYok";
                var saved = client.SendAsync<BackupStateDto>(Commands.BackupSave, bad, TimeSpan.FromMinutes(1)).Result;
                if (!saved.Success) Check(true, "Olmayan sürücüdeki hedef klasör kaydedilirken reddedildi (" + Short(saved.ErrorMessage) + ")");
                else
                {
                    if (saved.Value != null && !string.IsNullOrEmpty(saved.Value.TargetInfo)) Info("Sayfadaki hedef bilgisi: " + Short(saved.Value.TargetInfo, 160));
                    before = LastId();
                    run = RunBackup(out files);
                    Check(run != null && run.Status == "Hatalı", "Hedef sürücü yok (" + bad.TargetFolder + "): yedek 'Hatalı' bitti" + (run == null ? " (çalışmadı)" : " (" + Short(run.Summary, 200) + ")"));
                    if (run != null) Check((run.Summary ?? "").Contains("Yedek klasörüne ulaşılamıyor"), "Hata nedeni anlaşılır Türkçe yazıyor (İngilizce sistem mesajı değil)");
                    failEv = WaitEvent("Guard.BackupFailed", before, 30);
                    Check(failEv != null, "Hedef yokken 'SQL yedekleme başarısız' kritik olayı" + MailText(failEv) + (failEv == null ? "" : ": " + Short(failEv.Summary)));
                    var left = RunOut("sqlcmd.exe", "-S " + server + " -C -E -b -h -1 -W -Q \"SET NOCOUNT ON; SELECT 1\"");
                    Check(left.Item1 == 0, "Başarısız yedekten sonra SQL Server erişilebilir");
                }
            }
        }
        finally
        {
            var back = client.SendAsync<BackupStateDto>(Commands.BackupSave, original, TimeSpan.FromMinutes(1)).Result;
            if (!back.Success) Fail("!!! Yedekleme ayarları eski haline getirilemedi: " + back.ErrorMessage);
            Sql(server, "IF DB_ID('" + BakDbRestored + "') IS NOT NULL DROP DATABASE [" + BakDbRestored + "]; IF DB_ID('" + BakDb + "') IS NOT NULL BEGIN ALTER DATABASE [" + BakDb + "] SET ONLINE; DROP DATABASE [" + BakDb + "]; END");
            try { if (extracted != null && File.Exists(extracted)) File.Delete(extracted); } catch (Exception ex) { Info("Açılan .bak silinemedi: " + ex.Message); }
            Info("Temizlik: test veritabanları silindi, açılan .bak silindi, yedekleme ayarları eski haline getirildi. (Test arşivleri yedek klasöründe kalır; saklama süresi dolunca silinir.)");
        }
        var after = Send<BackupStateDto>(Commands.BackupGet, null).Config;
        Check(after.TargetFolder == original.TargetFolder && after.Enabled == original.Enabled && after.Databases.Count == original.Databases.Count, "Yedekleme ayarları eski halinde (" + after.TargetFolder + ", " + (after.Enabled ? "açık" : "kapalı") + ")");
    }

    // ================================================================ Gerçek SQL bakımı

    static void RealMaintenanceTests()
    {
        Title("GERÇEK SQL BAKIMI (bilerek parçalanmış test veritabanı " + FragDb + ")");
        string server, instance;
        if (!SqlReady(out server, out instance)) return;
        var original = Clone(Send<MaintenanceStateDto>(Commands.MaintenanceGet, null).Config);
        string key = instance + "|" + FragDb;
        string fragSql = "SELECT CAST(CAST(MAX(avg_fragmentation_in_percent) AS int) AS varchar(10)) + '/' + CAST(MAX(page_count) AS varchar(20)) FROM sys.dm_db_index_physical_stats(DB_ID('" + FragDb
                         + "'), OBJECT_ID('" + FragDb + ".dbo.Parca'), NULL, NULL, 'LIMITED')";
        Func<int[]> frag = () =>
        {
            string v = SqlScalar(server, fragSql);
            var p = v.Split('/');
            int f, pages;
            return p.Length == 2 && int.TryParse(p[0], out f) && int.TryParse(p[1], out pages) ? new[] { f, pages } : new[] { -1, -1 };
        };
        try
        {
            Sql(server, "IF DB_ID('" + FragDb + "') IS NOT NULL DROP DATABASE [" + FragDb + "];");
            Check(Sql(server, "CREATE DATABASE [" + FragDb + "]; ALTER DATABASE [" + FragDb + "] SET RECOVERY SIMPLE;"), "Test veritabanı oluşturuldu");
            Sql(server, "CREATE TABLE [" + FragDb + "].dbo.Parca (Id uniqueidentifier NOT NULL DEFAULT NEWID() PRIMARY KEY CLUSTERED, Dolgu char(300) NOT NULL DEFAULT 'x');");
            // Rastgele anahtarla 40 parti: her parti dolu sayfaların arasına girer → sayfa bölünmesi → yüksek parçalanma
            Sql(server, "SET NOCOUNT ON; DECLARE @i int = 0; WHILE @i < 40 BEGIN INSERT INTO [" + FragDb + "].dbo.Parca (Id, Dolgu) SELECT TOP (4000) NEWID(), 'x' FROM sys.all_objects a CROSS JOIN sys.all_objects b; SET @i += 1; END");
            var f0 = frag();
            Check(f0[0] >= 30 && f0[1] >= 1000, "Test tablosu parçalandı: %" + f0[0] + ", " + f0[1] + " sayfa (eşik: %30 üstü yeniden oluşturulur, 1000 sayfadan küçüğe dokunulmaz)");
            if (f0[0] < 30 || f0[1] < 1000) return;

            var cfg = Clone(original);
            if (!cfg.Databases.Contains(key, StringComparer.OrdinalIgnoreCase)) cfg.Databases.Add(key);
            cfg.Indexes = true;
            cfg.Statistics = true;
            Send<MaintenanceStateDto>(Commands.MaintenanceSave, cfg);

            // Deneme modu: ne yapılacağını listeler, hiçbir şeyi değiştirmez
            List<MaintenanceOpDto> ops;
            var dry = RunMaintenance(true, out ops);
            var mineDry = ops.Where(o => string.Equals(o.Database, FragDb, StringComparison.OrdinalIgnoreCase)).ToList();
            foreach (var o in mineDry.Take(6)) Info("deneme: " + o.ObjectName + " | " + o.Operation + " | " + o.Result + " | " + Short(o.Before) + (string.IsNullOrEmpty(o.Error) ? "" : " | " + Short(o.Error)));
            Check(dry != null && dry.DryRun && mineDry.Any(o => (o.ObjectName ?? "").IndexOf("Parca", StringComparison.OrdinalIgnoreCase) >= 0 || (o.Operation ?? "").IndexOf("REBUILD", StringComparison.OrdinalIgnoreCase) >= 0),
                "Deneme modu parçalanmış index'i 'yapılacak' olarak listeledi" + (dry == null ? "" : " (" + Short(dry.Summary) + ")"));
            var fDry = frag();
            Check(fDry[0] == f0[0], "Deneme modu hiçbir şeyi değiştirmedi (parçalanma hâlâ %" + fDry[0] + ")");

            // Gerçek çalıştırma
            long before = LastId();
            var sw = System.Diagnostics.Stopwatch.StartNew();
            var real = RunMaintenance(false, out ops);
            var mine = ops.Where(o => string.Equals(o.Database, FragDb, StringComparison.OrdinalIgnoreCase)).ToList();
            foreach (var o in mine.Take(8)) Info("uygulama: " + o.ObjectName + " | " + o.Operation + " | " + o.Result + " | " + Short(o.Before) + " → " + Short(o.After) + " | " + o.DurationMs + " ms" + (string.IsNullOrEmpty(o.Error) ? "" : " | " + Short(o.Error)));
            Check(real != null && !real.DryRun && real.Status == "Tamamlandı", "Bakım gerçek modda bitti (" + (int)sw.Elapsed.TotalSeconds + " sn): " + (real == null ? "çalışmadı" : real.Status + " | " + Short(real.Summary)));
            Check(mine.Count > 0 && mine.All(o => o.Result != "Hata"), "Bakım işlemlerinde hata yok (" + mine.Count + " satır)");
            var f1 = frag();
            Check(f1[0] >= 0 && f1[0] < 10, "Parçalanma düştü: %" + f0[0] + " → %" + f1[0] + " (" + f0[1] + " → " + f1[1] + " sayfa)");
            Check(SqlScalar(server, "SELECT COUNT(*) FROM [" + FragDb + "].dbo.Parca") == "160000", "Bakımdan sonra veri eksiksiz (160000 satır)");
            Check(SqlScalar(server, "SELECT state_desc FROM sys.databases WHERE name = '" + FragDb + "'") == "ONLINE", "Veritabanı çevrimiçi");
            var done = WaitEvent("Sql.MaintenanceDone", before, 20);
            Check(done != null, "'SQL bakımı yapıldı' olayı yazıldı" + (done == null ? "" : ": " + Short(done.Summary)));

            // İkinci kez: yapılacak iş kalmamalı
            var again = RunMaintenance(false, out ops);
            var mine2 = ops.Where(o => string.Equals(o.Database, FragDb, StringComparison.OrdinalIgnoreCase) && (o.Operation ?? "").IndexOf("INDEX", StringComparison.OrdinalIgnoreCase) >= 0).ToList();
            Check(again != null && mine2.All(o => o.Result == "Gerek yok"), "Hemen ardından ikinci bakım index'e tekrar dokunmadı" + (mine2.Count == 0 ? "" : " (" + mine2[0].Result + ": " + Short(mine2[0].Error) + ")"));
        }
        finally
        {
            var back = client.SendAsync<MaintenanceStateDto>(Commands.MaintenanceSave, original, TimeSpan.FromMinutes(1)).Result;
            if (!back.Success) Fail("!!! Bakım ayarları eski haline getirilemedi: " + back.ErrorMessage);
            Sql(server, "IF DB_ID('" + FragDb + "') IS NOT NULL DROP DATABASE [" + FragDb + "];");
            Info("Temizlik: test veritabanı silindi, bakım ayarları eski haline getirildi.");
        }
    }

    // ================================================================ Zamanlama

    static void ScheduleTests()
    {
        Title("ZAMANLAMA (yedek ve günlük rapor saati 2-3 dakika sonrasına alınır)");
        var now = DateTime.Now;
        var at = now.AddMinutes(now.Second > 35 ? 3 : 2);
        if (at.Date != now.Date) { Warn("Gece yarısına çok yakın: zamanlama testi atlandı."); return; }
        string time = at.ToString("HH:mm", CultureInfo.InvariantCulture);

        var settings = Send<List<GeneralSettingDto>>(Commands.GeneralSettingsList, null);
        var reportTime = settings.FirstOrDefault(s => s.Key == "report.dailyTime");
        var backup = Send<BackupStateDto>(Commands.BackupGet, null);
        var originalBackup = Clone(backup.Config);
        bool backupReady = !string.IsNullOrEmpty(originalBackup.TargetFolder) && originalBackup.Databases.Count > 0 && backup.HasPassword && !string.IsNullOrEmpty(backup.RarFound);
        string lastBackupRun = backup.Runs != null && backup.Runs.Count > 0 ? backup.Runs[0].RunId : null;
        long lastReportId = Send<List<ReportDto>>(Commands.ReportList, null).Select(r => r.Id).DefaultIfEmpty(0).Max();
        try
        {
            // Bugün zamanlanmış rapor gönderildiyse tekrar gönderilmez: işaret temizlenir (servis ayarları yeniden okur)
            try
            {
                Query("UPDATE Settings SET Value = NULL WHERE Key = 'report.lastDate'");
            }
            catch (Exception ex)
            {
                Warn("Günlük rapor 'bugün gönderildi' işareti temizlenemedi (" + Short(ex.Message) + "): rapor bugün zaten gönderildiyse tetiklenmeyebilir.");
            }

            if (reportTime != null) Check(SaveSetting("report.dailyTime", time).Success, "Günlük rapor saati " + time + " yapıldı (eski: " + reportTime.Value + ")");
            if (backupReady)
            {
                var cfg = Clone(originalBackup);
                cfg.Enabled = true;
                cfg.Days = new List<int> { 0, 1, 2, 3, 4, 5, 6 };
                cfg.StartTime = time;
                var saved = client.SendAsync<BackupStateDto>(Commands.BackupSave, cfg, TimeSpan.FromMinutes(1)).Result;
                Check(saved.Success, "Yedekleme açıldı, saati " + time + " yapıldı" + (saved.Success ? " (sonraki: " + saved.Value.NextRunUtc + ")" : ": " + saved.ErrorMessage));
            }
            else Warn("Yedekleme yapılandırılmamış: zamanlanmış yedek testi atlandı.");

            // Zamanlanmış yedek (zamanlayıcı dakikada bir bakar)
            if (backupReady)
            {
                Info("Zamanlanmış yedeğin kendiliğinden başlaması bekleniyor (saat " + time + ", en fazla ~5 dk)...");
                BackupRunDto run = null;
                WaitUntil(() =>
                {
                    var bs = Send<BackupStateDto>(Commands.BackupGet, null);
                    run = bs.Runs != null && bs.Runs.Count > 0 && bs.Runs[0].RunId != lastBackupRun ? bs.Runs[0] : null;
                    return run != null && !bs.IsRunning;
                }, 330, true);
                Check(run != null && (run.Trigger ?? "").StartsWith("zamanlanmış", StringComparison.OrdinalIgnoreCase), "Yedek saati gelince kendiliğinden başladı" + (run == null ? " (başlamadı)" : " (tetikleyen: " + run.Trigger + ", başlangıç " + run.StartedUtc + ")"));
                if (run != null) Check(run.Status == "Tamamlandı", "Zamanlanmış yedek tamamlandı: " + run.Status + " | " + Short(run.Summary));
                // Aynı gün ikinci kez başlamamalı
                Thread.Sleep(70000);
                var after = Send<BackupStateDto>(Commands.BackupGet, null);
                Check(run != null && after.Runs[0].RunId == run.RunId, "Aynı saat için ikinci kez başlamadı");
            }

            // Zamanlanmış günlük rapor (aynı saatte taramalar da başlar; rapor taramaların bitmesini bekler)
            if (reportTime != null)
            {
                Info("Zamanlanmış günlük raporun kendiliğinden gönderilmesi bekleniyor (en fazla ~8 dk; önce taramalar biter)...");
                ReportDto rep = null;
                WaitUntil(() => (rep = Send<List<ReportDto>>(Commands.ReportList, null).Where(r => r.Id > lastReportId && r.Kind == "daily" && (r.Trigger ?? "").StartsWith("zamanlanmış", StringComparison.OrdinalIgnoreCase))
                    .OrderByDescending(r => r.Id).FirstOrDefault()) != null, 480, true);
                Check(rep != null, "Günlük rapor saati gelince kendiliğinden hazırlandı" + (rep == null ? " (hazırlanmadı)" : ": " + Short(rep.Subject) + " | tetikleyen: " + rep.Trigger));
                if (rep != null) Check(rep.Mailed, "Zamanlanmış rapor maile verildi" + (rep.Mailed ? "" : " (" + rep.MailNote + ")"));
                var scans = new[] { CheckScopes.Security, CheckScopes.Performance }.Select(s => Send<CheckRunDto>(Commands.ChecksLatest, new CheckScopeRequest { Scope = s })).ToList();
                Info("Rapor saatindeki taramalar: " + string.Join(" | ", scans.Select(s => s.Scope + " " + s.Trigger + " " + s.StartedUtc)));
            }
        }
        finally
        {
            if (reportTime != null) SaveSetting("report.dailyTime", reportTime.Value);
            var back = client.SendAsync<BackupStateDto>(Commands.BackupSave, originalBackup, TimeSpan.FromMinutes(1)).Result;
            if (!back.Success) Fail("!!! Yedekleme ayarları eski haline getirilemedi: " + back.ErrorMessage);
            // Bu akşamki gerçek rapor yine gitsin
            try { Query("UPDATE Settings SET Value = NULL WHERE Key = 'report.lastDate'"); } catch { }
            Info("Temizlik: rapor saati (" + (reportTime == null ? "-" : reportTime.Value) + ") ve yedekleme ayarları (" + (originalBackup.Enabled ? "açık" : "kapalı") + ", " + originalBackup.StartTime + ") eski haline getirildi.");
        }
        var end = Send<BackupStateDto>(Commands.BackupGet, null).Config;
        Check(end.Enabled == originalBackup.Enabled && end.StartTime == originalBackup.StartTime, "Yedekleme zamanlaması eski halinde");
    }
}
