using System;
using System.Collections.Generic;
using System.Data.SqlClient;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using SecurityGuard.Shared.Dto;
using SecurityGuard.Shared.Enums;
using SecurityGuardService.Core.Settings;
using static SecurityGuardService.Checks.SqlQuery;

namespace SecurityGuardService.Checks.Performance
{
    /// <summary>
    /// SQL Server performans önerileri (bir instance). Sadece OKUR: DMV'ler ve katalog görünümleri.
    /// Index parçalanması LIMITED modda ölçülür, DB'ler tek tek; tablo içerikleri okunmaz.
    /// Her öneri hazır script içerebilir ama script'i kullanıcı kendisi çalıştırır.
    /// </summary>
    internal sealed class SqlPerformanceChecks
    {
        private const int MaxScriptLines = 300;

        private readonly CheckContext _ctx;
        private readonly string _instance;
        private readonly string _area;
        private readonly SqlConnection _conn;
        private ServerInfo _info;

        private SqlPerformanceChecks(CheckContext ctx, string instance, SqlConnection conn)
        {
            _ctx = ctx;
            _instance = instance;
            _area = "SQL Server (" + instance + ")";
            _conn = conn;
        }

        public static void Run(CheckContext ctx, string instance, string connectionString)
        {
            string area = "SQL Server (" + instance + ")";
            SqlConnection conn = null;
            try
            {
                ctx.Progress(area + ": bağlanılıyor");
                conn = new SqlConnection(connectionString);
                conn.Open();
                using (var cmd = conn.CreateCommand())
                {
                    // Tarama kimseyi bekletmesin: kilit beklemez, kilitlenmede kurban olan biz oluruz
                    cmd.CommandText = "SET LOCK_TIMEOUT 10000; SET DEADLOCK_PRIORITY LOW; SET TRANSACTION ISOLATION LEVEL READ UNCOMMITTED;";
                    cmd.ExecuteNonQuery();
                }
            }
            catch (Exception ex)
            {
                if (conn != null) conn.Dispose();
                ctx.Add(CheckContext.Item("perf.sql.connect@" + instance, area, "SQL Server bağlantısı", CheckStatus.Hata,
                    "SQL Server'a bağlanılamadı, SQL önerileri yapılamadı: " + ex.Message));
                return;
            }

            using (conn)
                new SqlPerformanceChecks(ctx, instance, conn).RunAll();
        }

        private string Id(string name)
        {
            return "perf.sql." + name + "@" + _instance;
        }

        private void RunAll()
        {
            _ctx.Run(Id("info"), _area, "Sunucu bilgisi", ReadServerInfo);
            if (_info == null) return;

            _ctx.Run(Id("maxmemory"), _area, "Bellek sınırı (max server memory)", CheckMaxMemory);
            _ctx.Run(Id("memorypressure"), _area, "SQL bellek baskısı (Page Life Expectancy)", CheckMemoryPressure);
            _ctx.Run(Id("blocking"), _area, "Kilitlenme (blocking) ve açık kalan işlemler", CheckBlocking);
            _ctx.Run(Id("ifi"), _area, "Hızlı dosya büyütme (Instant File Initialization)", CheckInstantFileInit);
            _ctx.Run(Id("maxdop"), _area, "Paralellik (MAXDOP)", CheckMaxDop);
            _ctx.Run(Id("costthreshold"), _area, "Paralellik eşiği (cost threshold)", CheckCostThreshold);
            _ctx.Run(Id("adhoc"), _area, "Tek kullanımlık sorgu planları (optimize for ad hoc)", CheckAdHoc);
            _ctx.Run(Id("tempdb"), _area, "tempdb dosyaları", CheckTempDb);
            _ctx.Run(Id("autogrowth"), _area, "Dosya büyüme ayarları (autogrowth)", CheckAutogrowth);
            _ctx.Run(Id("autoshrink"), _area, "Auto shrink / auto close", CheckAutoShrinkClose);
            _ctx.Run(Id("vlf"), _area, "Log dosyası parça sayısı (VLF)", CheckVlf);
            _ctx.Run(Id("logs"), _area, "Log dosyaları (şişme / log yedeği)", CheckLogFiles);
            _ctx.Run(Id("compat"), _area, "Veritabanı uyumluluk seviyesi", CheckCompatibility);
            _ctx.Run(Id("placement"), _area, "Veri / log / tempdb dosyalarının yeri", CheckFilePlacement);

            var dbs = UserDatabases();
            PerDatabaseChecks(dbs);

            _ctx.Run(Id("missingindex"), _area, "Eksik index önerileri", CheckMissingIndexes);
            _ctx.Run(Id("waits"), _area, "En çok beklenen kaynaklar (wait)", CheckWaits);
            _ctx.Run(Id("slowqueries"), _area, "En çok süre harcayan sorgular", CheckSlowQueries);
            _ctx.Run(Id("jobs"), _area, "Sunucudaki diğer bakım / yedek işleri", CheckOtherJobs);
        }

        // ================================================================ Sunucu bilgisi

        private CheckItemDto ReadServerInfo()
        {
            var row = Rows(_conn, @"
SELECT CAST(SERVERPROPERTY('ProductVersion') AS nvarchar(64)), CAST(SERVERPROPERTY('Edition') AS nvarchar(256)),
       CAST(SERVERPROPERTY('EngineEdition') AS int), i.sqlserver_start_time, i.cpu_count, i.physical_memory_kb / 1024,
       (SELECT COUNT(*) FROM sys.dm_os_nodes WHERE node_state_desc NOT LIKE '%DAC%'),
       CAST(SERVERPROPERTY('InstanceDefaultDataPath') AS nvarchar(512)), CAST(SERVERPROPERTY('InstanceDefaultLogPath') AS nvarchar(512))
FROM sys.dm_os_sys_info i", _ctx.Token)[0];

            string version = Str(row[0]) ?? "0";
            int major;
            int.TryParse(version.Split('.')[0], out major);
            _info = new ServerInfo
            {
                Version = version,
                Major = major,
                Edition = Str(row[1]),
                EngineEdition = (int)Long(row[2]),
                StartTime = Date(row[3]) ?? DateTime.Now,
                Cpu = (int)Long(row[4]),
                MemoryMb = Long(row[5]),
                NumaNodes = Math.Max(1, (int)Long(row[6]))
            };

            // Antivirüs hariç tutma kontrolü için SQL dosyalarının klasörleri
            var paths = _ctx.Shared.ContainsKey("sql.paths") ? (HashSet<string>)_ctx.Shared["sql.paths"] : new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var r in Rows(_conn, "SELECT DISTINCT physical_name FROM sys.master_files", _ctx.Token))
            {
                string file = Str(r[0]);
                int slash = file == null ? -1 : file.LastIndexOf('\\');
                if (slash > 0) paths.Add(file.Substring(0, slash));
            }
            _ctx.Shared["sql.paths"] = paths;

            double uptimeDays = (DateTime.Now - _info.StartTime).TotalDays;
            var item = CheckContext.Item(null, null, null, CheckStatus.Bilgi,
                _info.Edition + ", sürüm " + _info.Version + ", " + _info.Cpu + " işlemci, " + Fmt.Mb(_info.MemoryMb) + " bellek");
            item.Detail = "Sürüm: " + _info.Version + "\r\nEdition: " + _info.Edition + "\r\nİşlemci (mantıksal): " + _info.Cpu
                + "\r\nNUMA düğümü: " + _info.NumaNodes + "\r\nSunucu belleği: " + Fmt.Mb(_info.MemoryMb)
                + "\r\nSQL Server başlama zamanı: " + Fmt.Date(_info.StartTime) + " (" + (int)uptimeDays + " gün)"
                + "\r\n\r\nNot: Kullanım istatistikleri (kullanılmayan index, wait, yavaş sorgu, son okunma) SQL Server yeniden başlayınca sıfırlanır."
                + (uptimeDays < 7 ? " SQL Server " + (int)uptimeDays + " gündür açık: bu öneriler henüz yeterli veriye dayanmıyor." : "");
            return item;
        }

        private double UptimeDays { get { return (DateTime.Now - _info.StartTime).TotalDays; } }

        private Dictionary<string, long> Configs()
        {
            var map = new Dictionary<string, long>(StringComparer.OrdinalIgnoreCase);
            foreach (var r in Rows(_conn, "SELECT name, CAST(value_in_use AS bigint) FROM sys.configurations", _ctx.Token))
                map[Str(r[0])] = Long(r[1]);
            return map;
        }

        private static string ConfigScript(string name, long value)
        {
            return "EXEC sys.sp_configure N'show advanced options', 1;\r\nRECONFIGURE;\r\nEXEC sys.sp_configure N'" + name + "', " + value + ";\r\nRECONFIGURE;";
        }

        // ================================================================ Sunucu ayarları

        private CheckItemDto CheckMaxMemory()
        {
            long configured = Configs()["max server memory (MB)"];
            long total = _info.MemoryMb;

            // RDP kullanıcıları da aynı sunucuda çalıştığı için işletim sistemi + kullanıcılara geniş pay bırakılır
            long reserve = total <= 65536 ? Math.Max(4096, total / 4) : Math.Max(16384, total * 15 / 100);
            long recommended = Math.Max(2048, (total - reserve) / 1024 * 1024);

            var item = new CheckItemDto { ScriptLanguage = "sql" };
            string why = "SQL Server sınırsız bırakılırsa zamanla sunucunun neredeyse tüm belleğini alır ve geri vermez; Windows ve RDP kullanıcılarının "
                         + "programları belleksiz kalır, sunucu yavaşlar.";

            if (_info.EngineEdition == 4)
            {
                item.Status = CheckStatus.Gecti;
                item.Summary = "Express edition: bellek kullanımı zaten sınırlı (yaklaşık 1,4 GB).";
                return item;
            }

            if (configured >= 2147483647 || configured > total)
            {
                item.Status = CheckStatus.Uyari;
                item.Summary = "Sınır yok (sunucu belleği " + Fmt.Mb(total) + "). Önerilen: " + Fmt.Mb(recommended) + ".";
                item.Detail = why + "\r\n\r\nSunucu belleği: " + Fmt.Mb(total) + "\r\nŞu anki ayar: sınırsız\r\nÖnerilen: " + Fmt.Num(recommended) + " MB ("
                              + Fmt.Mb(reserve) + " Windows ve kullanıcılar için ayrıldı).";
            }
            else if (configured < total / 4 && total >= 8192)
            {
                item.Status = CheckStatus.Bilgi;
                item.Summary = "Sınır düşük olabilir: " + Fmt.Mb(configured) + " / " + Fmt.Mb(total) + ". Önerilen: " + Fmt.Mb(recommended) + ".";
                item.Detail = "SQL Server'a sunucu belleğinin dörtte birinden azı verilmiş. Sunucuda bellek boşta duruyorsa SQL Server'a daha fazlası verilebilir; "
                              + "diskten okuma azalır.\r\n\r\nSunucu belleği: " + Fmt.Mb(total) + "\r\nŞu anki ayar: " + Fmt.Num(configured) + " MB\r\nÖnerilen: " + Fmt.Num(recommended) + " MB";
            }
            else
            {
                item.Status = CheckStatus.Gecti;
                item.Summary = "Ayarlı: " + Fmt.Mb(configured) + " / " + Fmt.Mb(total) + ".";
                return item;
            }

            item.HowToFix = "SSMS → sunucuya sağ tık → Properties → Memory → \"Maximum server memory\" = " + recommended + ".\r\n"
                            + "Veya aşağıdaki script. Ayar hemen geçerli olur, SQL Server yeniden başlatılmaz.\r\n"
                            + "Sunucuda başka ağır programlar da çalışıyorsa değeri biraz daha düşük tutun.";
            item.Script = ConfigScript("max server memory (MB)", recommended);
            return item;
        }

        private CheckItemDto CheckMaxDop()
        {
            long maxdop = Configs()["max degree of parallelism"];
            int perNode = Math.Max(1, _info.Cpu / _info.NumaNodes);
            int recommended = Math.Min(8, perNode);
            var item = new CheckItemDto { ScriptLanguage = "sql" };

            if (maxdop == 0 && _info.Cpu > 8)
            {
                item.Status = CheckStatus.Uyari;
                item.Summary = "MAXDOP = 0 (sınırsız), " + _info.Cpu + " işlemci. Önerilen: " + recommended + ".";
                item.Detail = "Tek bir sorgu tüm işlemcileri kullanabiliyor; büyük sorgular diğer kullanıcıları bekletir (CXPACKET beklemeleri).\r\n"
                              + "İşlemci: " + _info.Cpu + ", NUMA düğümü: " + _info.NumaNodes + " (düğüm başına " + perNode + ").";
            }
            else if (maxdop == 1 && _info.Cpu > 2)
            {
                item.Status = CheckStatus.Bilgi;
                item.Summary = "MAXDOP = 1: paralellik tamamen kapalı.";
                item.Detail = "Bazı uygulamalar (ERP / muhasebe programları) MAXDOP = 1 ister. Uygulama üreticisi istemediyse " + recommended
                              + " önerilir: büyük raporlar daha hızlı çalışır.";
            }
            else if (maxdop > recommended && maxdop > 1)
            {
                item.Status = CheckStatus.Bilgi;
                item.Summary = "MAXDOP = " + maxdop + ", önerilen: " + recommended + ".";
                item.Detail = "Microsoft önerisi: NUMA düğümü başına işlemci sayısı, en fazla 8. Düğüm başına işlemci: " + perNode + ".";
            }
            else
            {
                item.Status = CheckStatus.Gecti;
                item.Summary = "MAXDOP = " + maxdop + (maxdop == 0 ? " (" + _info.Cpu + " işlemci için uygun)" : "") + ".";
                return item;
            }

            item.HowToFix = "SSMS → sunucu Properties → Advanced → \"Max Degree of Parallelism\" = " + recommended + ".\r\n"
                            + "Uygulama üreticiniz belirli bir değer istiyorsa onu kullanın ve bu maddeyi notla görmezden gelin.";
            item.Script = ConfigScript("max degree of parallelism", recommended);
            return item;
        }

        private CheckItemDto CheckMemoryPressure()
        {
            var counters = Rows(_conn, @"
SELECT RTRIM(counter_name), MAX(cntr_value) FROM sys.dm_os_performance_counters
WHERE (object_name LIKE '%Buffer Manager%' AND counter_name IN ('Page life expectancy', 'Database pages'))
   OR (object_name LIKE '%Memory Manager%' AND counter_name IN ('Memory Grants Pending', 'Target Server Memory (KB)', 'Total Server Memory (KB)'))
GROUP BY counter_name", _ctx.Token).ToDictionary(r => Str(r[0]), r => Long(r[1]), StringComparer.OrdinalIgnoreCase);
            Func<string, long> get = n => counters.ContainsKey(n) ? counters[n] : 0;
            long ple = get("Page life expectancy"), pending = get("Memory Grants Pending");
            double bufferGb = get("Database pages") * 8 / 1024.0 / 1024.0;
            // Klasik 300 sn eşiği küçük bellekler içindir; büyük bellekte (GB / 4) x 300 sn
            long target = (long)Math.Max(300, bufferGb / 4 * 300);
            string detail = "Page Life Expectancy: " + Fmt.Num(ple) + " sn (bir sayfanın bellekte kalma süresi; hedef en az " + Fmt.Num(target) + " sn)\r\n"
                            + "Bellekteki veri: " + Fmt.Mb(bufferGb * 1024) + "\r\nSQL Server belleği: " + Fmt.Mb(get("Total Server Memory (KB)") / 1024.0) + " / hedef " + Fmt.Mb(get("Target Server Memory (KB)") / 1024.0)
                            + "\r\nBellek bekleyen sorgu (Memory Grants Pending): " + pending
                            + "\r\n\r\nAnlık ölçümdür (tarama saatindeki durum). SQL Server yeni başladıysa PLE düşük görünür, birkaç saat sonra yükselir.";

            if (UptimeDays < 1.0 / 24)
                return new CheckItemDto { Status = CheckStatus.Bilgi, Summary = "SQL Server 1 saatten kısa süredir açık; bellek ölçümü henüz anlamlı değil.", Detail = detail };
            if (pending > 0 || ple < target / 3)
                return new CheckItemDto
                {
                    Status = CheckStatus.Uyari,
                    Summary = pending > 0 ? pending + " sorgu çalışmak için bellek bekliyor." : "Veri bellekte çok kısa kalıyor (PLE " + Fmt.Num(ple) + " sn, hedef " + Fmt.Num(target) + ").",
                    Detail = "SQL Server'a ayrılan bellek veriye yetmiyor: aynı veriyi sürekli diskten tekrar okuyor, sorgular yavaşlıyor.\r\n\r\n" + detail,
                    HowToFix = "Önce eksik index ve en yavaş sorgular maddelerine bakın (gereksiz büyük taramalar belleği boşaltır). Sonra max server memory'yi (sunucuda boş bellek varsa) artırın "
                               + "veya sunucuya bellek ekleyin."
                };
            return new CheckItemDto { Status = CheckStatus.Gecti, Summary = "Bellek yeterli görünüyor (PLE " + Fmt.Num(ple) + " sn, bekleyen sorgu yok).", Detail = detail };
        }

        private CheckItemDto CheckBlocking()
        {
            // Açık transaction'ı olan ve 5 dakikadır işlem yapmayan (veya başkasını bekleten) oturumlar
            var open = Rows(_conn, @"
SELECT s.session_id, s.login_name, s.host_name, s.program_name, s.status, s.last_request_end_time,
       MIN(at.transaction_begin_time), DB_NAME(MAX(dt.database_id)),
       (SELECT COUNT(*) FROM sys.dm_exec_requests r WHERE r.blocking_session_id = s.session_id)
FROM sys.dm_exec_sessions s
JOIN sys.dm_tran_session_transactions st ON st.session_id = s.session_id
JOIN sys.dm_tran_active_transactions at ON at.transaction_id = st.transaction_id
LEFT JOIN sys.dm_tran_database_transactions dt ON dt.transaction_id = st.transaction_id AND dt.database_id > 4
WHERE s.is_user_process = 1 AND s.session_id <> @@SPID
GROUP BY s.session_id, s.login_name, s.host_name, s.program_name, s.status, s.last_request_end_time
HAVING MIN(at.transaction_begin_time) < DATEADD(MINUTE, -5, GETDATE())
    OR (SELECT COUNT(*) FROM sys.dm_exec_requests r WHERE r.blocking_session_id = s.session_id) > 0
ORDER BY MIN(at.transaction_begin_time)", _ctx.Token);
            var blockedNow = Rows(_conn, "SELECT COUNT(*), MAX(wait_time) / 1000 FROM sys.dm_exec_requests WHERE blocking_session_id <> 0", _ctx.Token)[0];
            long deadlocks = Long(Scalar(_conn, "SELECT cntr_value FROM sys.dm_os_performance_counters WHERE counter_name LIKE 'Number of Deadlocks/sec%' AND instance_name = '_Total'", _ctx.Token));

            var detail = new StringBuilder();
            detail.AppendLine("Şu an bekleyen (kilitlenmiş) sorgu: " + Long(blockedNow[0]) + (Long(blockedNow[0]) > 0 ? ", en uzun " + Long(blockedNow[1]) + " sn" : ""));
            detail.AppendLine("SQL Server açıldığından beri deadlock: " + Fmt.Num(deadlocks) + " (" + (int)UptimeDays + " gün)");
            if (open.Count > 0)
            {
                detail.AppendLine().AppendLine("Uzun süredir açık işlemi (transaction) olan oturumlar:");
                foreach (var r in open.Take(20))
                    detail.AppendLine("  Oturum " + Long(r[0]) + ": " + Str(r[1]) + " @ " + Str(r[2]) + " (" + Str(r[3]) + "), işlem " + Fmt.Ago(Date(r[6])) + " başladı, "
                                      + (Str(r[4]) == "sleeping" ? "şu an BOŞTA bekliyor" : "çalışıyor") + (Str(r[7]) != null ? ", DB " + Str(r[7]) : "")
                                      + (Long(r[8]) > 0 ? ", " + Long(r[8]) + " sorguyu BEKLETİYOR" : ""));
            }
            detail.AppendLine().Append("Anlık durumdur (tarama saatinde). \"Program dondu\" şikâyeti olduğu anda tekrar tarayın.");

            bool blocking = open.Any(r => Long(r[8]) > 0) || Long(blockedNow[0]) > 0;
            bool idleOpen = open.Any(r => Str(r[4]) == "sleeping" && Date(r[6]) < DateTime.Now.AddMinutes(-30));
            double perDay = UptimeDays >= 1 ? deadlocks / UptimeDays : deadlocks;
            if (!blocking && !idleOpen && perDay < 5)
                return new CheckItemDto { Status = CheckStatus.Gecti, Summary = "Kilitlenme yok, uzun süredir açık işlem yok; SQL açıldığından beri " + Fmt.Num(deadlocks) + " deadlock" + (UptimeDays >= 1 ? " (" + (int)UptimeDays + " gün)" : "") + ".", Detail = detail.ToString() };

            var parts = new List<string>();
            if (blocking) parts.Add("sorgular birbirini bekliyor");
            if (idleOpen) parts.Add("işlemi açık bırakılmış boşta oturum var");
            if (perDay >= 5) parts.Add("günde ortalama " + (int)perDay + " deadlock");
            return new CheckItemDto
            {
                Status = CheckStatus.Uyari,
                Summary = string.Join(", ", parts) + ".",
                Detail = "ERP / muhasebe programlarında \"program dondu, kayıt kaydetmiyor\" şikâyetinin en sık sebebi: bir kullanıcının ekranı (ör. açık bir fiş) arkada bir işlemi açık tutar, "
                         + "aynı kayıtlara ulaşmak isteyen herkes onu bekler. Deadlock'ta ise iki işlem birbirini bekler ve SQL Server birini iptal eder (kullanıcı hata görür).\r\n\r\n" + detail,
                HowToFix = "Listede \"BEKLETİYOR\" veya uzun süredir \"BOŞTA\" görünen oturumun kullanıcısına ekranındaki işlemi kaydetmesini / kapatmasını söyleyin. "
                           + "Ulaşılamıyorsa son çare SSMS'te KILL <oturum no> (o kullanıcının kaydedilmemiş işlemi geri alınır). Sürekli tekrarlıyorsa uygulama üreticisine bildirin; "
                           + "deadlock'lar çoğunlukla eksik index'ten kaynaklanır.",
                Script = open.Count == 0 ? null : "-- SON ÇARE: oturumu kapatır, o kullanıcının kaydedilmemiş işlemi geri alınır. Önce kullanıcıya ulaşmayı deneyin.\r\n"
                         + string.Join("\r\n", open.Where(r => Long(r[8]) > 0 || Str(r[4]) == "sleeping").Take(10).Select(r => "-- KILL " + Long(r[0]) + ";   -- " + Str(r[1]) + " @ " + Str(r[2]))),
                ScriptLanguage = "sql"
            };
        }

        private CheckItemDto CheckInstantFileInit()
        {
            object value;
            try
            {
                value = Scalar(_conn, "SELECT TOP 1 instant_file_initialization_enabled FROM sys.dm_server_services WHERE servicename LIKE 'SQL Server (%'", _ctx.Token);
            }
            catch (SqlException)
            {
                return new CheckItemDto { Status = CheckStatus.Bilgi, Summary = "Bu SQL Server sürümünde okunamıyor (2016 SP1 ve üstü gerekir)." };
            }
            if (Str(value) == "Y") return new CheckItemDto { Status = CheckStatus.Gecti, Summary = "Açık: veri dosyaları beklemeden büyüyor." };
            string account = Str(Scalar(_conn, "SELECT TOP 1 service_account FROM sys.dm_server_services WHERE servicename LIKE 'SQL Server (%'", _ctx.Token));
            return new CheckItemDto
            {
                Status = CheckStatus.Uyari,
                Summary = "Kapalı: veri dosyası her büyüdüğünde ve geri yüklemede disk sıfırlarla doldurulup bekleniyor.",
                Detail = "Kapalıyken veri dosyası 1 GB büyürken SQL Server 1 GB'ı sıfırla yazana kadar o DB'deki herkes bekler (kullanıcıya \"program dondu\" gibi görünür); "
                         + "yedekten geri yükleme de çok uzar. Açmak güvenli ve Microsoft'un önerisi (log dosyaları her zaman sıfırlanır, bu ayar onları etkilemez).\r\n\r\nSQL servis hesabı: " + account,
                HowToFix = "secpol.msc → Yerel İlkeler → Kullanıcı Hakları Ataması → \"Birim bakım görevlerini gerçekleştir\" (Perform volume maintenance tasks) → " + account
                           + " hesabını ekleyin → SQL Server servisini yeniden başlatın (mesai dışında)."
            };
        }

        private CheckItemDto CheckCompatibility()
        {
            int serverLevel = _info.Major * 10;
            var rows = Rows(_conn, "SELECT name, compatibility_level FROM sys.databases WHERE database_id > 4 AND state = 0 AND compatibility_level < " + serverLevel + " ORDER BY name", _ctx.Token);
            if (rows.Count == 0) return new CheckItemDto { Status = CheckStatus.Gecti, Summary = "Tüm DB'ler sunucunun sürümüyle aynı uyumluluk seviyesinde (" + serverLevel + ")." };
            return new CheckItemDto
            {
                Status = CheckStatus.Bilgi,
                Summary = rows.Count + " DB eski uyumluluk seviyesinde: " + string.Join(", ", rows.Take(4).Select(r => Str(r[0]) + " (" + Long(r[1]) + ")")) + (rows.Count > 4 ? "..." : "") + ".",
                Detail = "SQL Server yükseltilse de DB eski seviyede kalırsa yeni sürümün sorgu iyileştirmelerinin çoğundan yararlanamaz. Sunucu seviyesi: " + serverLevel + ".\r\n\r\n  "
                         + string.Join("\r\n  ", rows.Select(r => Str(r[0]) + ": " + Long(r[1]))),
                HowToFix = "DİKKAT: Bazı ERP programları belirli bir uyumluluk seviyesi ister; değiştirmeden önce uygulama üreticisine sorun. Değiştirdikten sonra yavaşlayan sorgu olursa "
                           + "eski seviyeye dönmek anında mümkündür. Uygulama belirli seviye istiyorsa bu maddeyi notla görmezden gelin.",
                Script = "-- Uygulama üreticisinin onayıyla\r\n" + string.Join("\r\n", rows.Select(r => "-- ALTER DATABASE " + Q(Str(r[0])) + " SET COMPATIBILITY_LEVEL = " + serverLevel + ";")),
                ScriptLanguage = "sql"
            };
        }

        private CheckItemDto CheckFilePlacement()
        {
            var rows = Rows(_conn, "SELECT DB_NAME(database_id), type, physical_name FROM sys.master_files WHERE database_id <> 3", _ctx.Token);
            string system = (Environment.GetFolderPath(Environment.SpecialFolder.Windows).Substring(0, 1) + ":").ToUpperInvariant();
            Func<string, string> drive = p => p != null && p.Length > 1 && p[1] == ':' ? p.Substring(0, 2).ToUpperInvariant() : "?";

            var sameDisk = rows.Where(r => Str(r[0]) != "tempdb" && Long(r[1]) == 0).GroupBy(r => Str(r[0]))
                .Where(g => rows.Any(l => Str(l[0]) == g.Key && Long(l[1]) == 1 && g.Any(d => drive(Str(d[2])) == drive(Str(l[2]))))).Select(g => g.Key).ToList();
            var onSystem = rows.Where(r => drive(Str(r[2])) == system && Str(r[0]) != "master" && Str(r[0]) != "model" && Str(r[0]) != "msdb")
                .Select(r => Str(r[0])).Distinct().ToList();
            var drives = rows.Select(r => drive(Str(r[2]))).Distinct().OrderBy(d => d).ToList();

            var detail = new StringBuilder();
            foreach (var g in rows.GroupBy(r => Str(r[0])).OrderBy(g => g.Key))
                detail.AppendLine("  " + g.Key + ": veri " + string.Join(",", g.Where(r => Long(r[1]) == 0).Select(r => drive(Str(r[2]))).Distinct())
                                  + ", log " + string.Join(",", g.Where(r => Long(r[1]) == 1).Select(r => drive(Str(r[2]))).Distinct()));

            if (sameDisk.Count == 0 && onSystem.Count == 0)
                return new CheckItemDto { Status = CheckStatus.Gecti, Summary = "Veri ve log dosyaları ayrı disklerde, sistem diskinde kullanıcı DB'si yok.", Detail = detail.ToString() };

            var parts = new List<string>();
            if (onSystem.Count > 0) parts.Add(onSystem.Count + " DB (veya tempdb) Windows'un diskinde (" + system + ")");
            if (sameDisk.Count > 0) parts.Add(sameDisk.Count + " DB'nin veri ve log dosyası aynı diskte");
            return new CheckItemDto
            {
                Status = CheckStatus.Bilgi,
                Summary = string.Join(", ", parts) + (drives.Count == 1 ? " (sunucuda tek disk var)" : "") + ".",
                Detail = "En iyi düzen: Windows, SQL veri dosyaları, log dosyaları ve tempdb ayrı disklerde (sanal sunucuda ayrı sanal diskler). Aynı diskte olunca okuma ve yazmalar birbirini bekler; "
                         + "C: dolarsa Windows da SQL Server da durur. Tek diskli sunucularda bu normaldir, disk hızlıysa (SSD) etkisi azdır.\r\n\r\n" + detail,
                HowToFix = "Yeni disk eklendiğinde DB dosyaları taşınabilir (DB'yi çevrimdışı alıp dosyayı taşıma, ALTER DATABASE … MODIFY FILE). Planlı bir bakım işidir; destek alarak yapın."
            };
        }

        private CheckItemDto CheckCostThreshold()
        {
            long value = Configs()["cost threshold for parallelism"];
            if (value > 5)
                return new CheckItemDto { Status = CheckStatus.Gecti, Summary = "Ayarlı: " + value + "." };

            return new CheckItemDto
            {
                Status = CheckStatus.Uyari,
                Summary = "Varsayılan değerde (" + value + "). Önerilen: 50.",
                Detail = "5 değeri 1990'ların donanımına göre belirlenmiş: küçük sorgular bile paralel çalışıp gereksiz işlemci harcıyor ve birbirini bekletiyor.",
                HowToFix = "SSMS → sunucu Properties → Advanced → \"Cost Threshold for Parallelism\" = 50. Hemen geçerli olur.",
                Script = ConfigScript("cost threshold for parallelism", 50),
                ScriptLanguage = "sql"
            };
        }

        private CheckItemDto CheckAdHoc()
        {
            long value = Configs()["optimize for ad hoc workloads"];
            if (value == 1)
                return new CheckItemDto { Status = CheckStatus.Gecti, Summary = "Açık." };

            var row = Rows(_conn, @"SELECT COUNT_BIG(*), SUM(CAST(size_in_bytes AS bigint)) / 1048576
                                    FROM sys.dm_exec_cached_plans WHERE usecounts = 1 AND objtype = 'Adhoc'", _ctx.Token)[0];
            long count = Long(row[0]), mb = Long(row[1]);
            return new CheckItemDto
            {
                Status = CheckStatus.Bilgi,
                Summary = "Kapalı. Bir kez kullanılmış " + Fmt.Num(count) + " sorgu planı belleğin " + Fmt.Mb(mb) + " kadarını kaplıyor.",
                Detail = "Açılırsa bir kez çalışan sorguların planı tam saklanmaz, bellek veriye kalır. Riski yok denecek kadar az.",
                HowToFix = "Aşağıdaki script ile açılabilir. Hemen geçerli olur.",
                Script = ConfigScript("optimize for ad hoc workloads", 1),
                ScriptLanguage = "sql"
            };
        }

        private CheckItemDto CheckTempDb()
        {
            var files = Rows(_conn, "SELECT name, physical_name, size / 128, growth, is_percent_growth FROM tempdb.sys.database_files WHERE type = 0", _ctx.Token);
            int count = files.Count;
            int recommended = Math.Min(8, _info.Cpu);
            long minMb = files.Min(f => Long(f[2])), maxMb = files.Max(f => Long(f[2]));

            var detail = new StringBuilder("tempdb veri dosyaları (" + count + "):\r\n");
            foreach (var f in files) detail.AppendLine("  " + Str(f[0]) + ": " + Fmt.Mb(Long(f[2])) + " (" + Str(f[1]) + ")");

            CheckStatus status;
            string summary;
            if (count < recommended && count < 4)
            {
                status = count == 1 && _info.Cpu > 1 ? CheckStatus.Uyari : CheckStatus.Bilgi;
                summary = count + " veri dosyası var, " + _info.Cpu + " işlemci için önerilen: " + recommended + ".";
                detail.AppendLine().Append("Tek / az dosyada geçici tablolar ve sıralamalar aynı sayfalar için sıraya girer (PAGELATCH beklemesi). "
                                           + "Dosya sayısı işlemci sayısı kadar (en fazla 8) ve hepsi aynı boyutta olmalı.");
            }
            else if (maxMb > minMb * 1.2 + 64)
            {
                status = CheckStatus.Bilgi;
                summary = "Dosya boyutları farklı (" + Fmt.Mb(minMb) + " - " + Fmt.Mb(maxMb) + "); SQL Server büyük dosyayı daha çok kullanır.";
            }
            else
            {
                return new CheckItemDto { Status = CheckStatus.Gecti, Summary = count + " veri dosyası, boyutları eşit.", Detail = detail.ToString() };
            }

            var item = new CheckItemDto { Status = status, Summary = summary, Detail = detail.ToString(), ScriptLanguage = "sql" };
            if (count < recommended)
            {
                string first = Str(files[0][1]);
                string folder = first.Substring(0, first.LastIndexOf('\\') + 1);
                var script = new StringBuilder("-- Yeni dosyalar ilk dosyayla aynı klasöre ve aynı boyutta eklenir. Klasörde yeterli yer olduğundan emin olun.\r\n");
                for (int i = count + 1; i <= recommended; i++)
                    script.AppendLine("ALTER DATABASE tempdb ADD FILE (NAME = " + N("tempdev" + i) + ", FILENAME = " + N(folder + "tempdb_sg" + i + ".ndf")
                                      + ", SIZE = " + Math.Max(64, maxMb) + "MB, FILEGROWTH = 256MB);");
                item.Script = script.ToString();
                item.HowToFix = "Script yeni tempdb dosyalarını ekler; hemen kullanılmaya başlanır. SQL Server yeniden başlatıldığında dosyalar yine yerinde olur.";
            }
            else
            {
                item.HowToFix = "SSMS → tempdb → Properties → Files: tüm veri dosyalarına aynı Initial Size ve aynı Autogrowth değerini verin.";
            }
            return item;
        }

        private CheckItemDto CheckAutogrowth()
        {
            var rows = Rows(_conn, @"
SELECT DB_NAME(mf.database_id), mf.name, mf.type, mf.size / 128, mf.growth, mf.is_percent_growth
FROM sys.master_files mf JOIN sys.databases d ON d.database_id = mf.database_id
WHERE d.state = 0 AND d.is_read_only = 0 AND mf.growth > 0 AND mf.type IN (0, 1)
  AND (mf.is_percent_growth = 1 OR mf.growth / 128 < 64)
ORDER BY 1, 2", _ctx.Token);

            if (rows.Count == 0) return new CheckItemDto { Status = CheckStatus.Gecti, Summary = "Tüm dosyalar sabit MB ile büyüyor." };

            var percent = rows.Where(r => Long(r[5]) == 1).ToList();
            var detail = new StringBuilder();
            var script = new StringBuilder("-- Veri dosyaları 256 MB, log dosyaları 128 MB adımlarla büyüsün\r\n");
            foreach (var r in rows)
            {
                bool isPercent = Long(r[5]) == 1;
                bool isLog = Long(r[2]) == 1;
                detail.AppendLine("  " + Str(r[0]) + " / " + Str(r[1]) + (isLog ? " (log)" : "") + ": " + Fmt.Mb(Long(r[3])) + ", büyüme "
                                  + (isPercent ? "%" + Long(r[4]) : Fmt.Mb(Long(r[4]) / 128.0)));
                script.AppendLine("ALTER DATABASE " + Q(Str(r[0])) + " MODIFY FILE (NAME = " + N(Str(r[1])) + ", FILEGROWTH = " + (isLog ? "128MB" : "256MB") + ");");
            }

            return new CheckItemDto
            {
                Status = percent.Count > 0 ? CheckStatus.Uyari : CheckStatus.Bilgi,
                Summary = percent.Count > 0
                    ? percent.Count + " dosya yüzde ile büyüyor" + (rows.Count > percent.Count ? ", " + (rows.Count - percent.Count) + " dosya çok küçük adımlarla" : "") + "."
                    : rows.Count + " dosya çok küçük adımlarla (64 MB altı) büyüyor.",
                Detail = "Yüzde ile büyüme: dosya büyüdükçe her büyüme daha uzun sürer (o sırada DB bekler); log dosyasında parça (VLF) sayısını da bozar.\r\n"
                         + "Çok küçük adım: dosya sürekli büyür, disk parçalanır.\r\n\r\n" + detail,
                HowToFix = "SSMS → DB → Properties → Files → Autogrowth: \"In Megabytes\" seçin (veri 256 MB, log 128 MB önerilir). Hemen geçerli olur.",
                Script = Limit(script),
                ScriptLanguage = "sql"
            };
        }

        private CheckItemDto CheckAutoShrinkClose()
        {
            var rows = Rows(_conn, "SELECT name, is_auto_shrink_on, is_auto_close_on FROM sys.databases WHERE database_id > 4 AND (is_auto_shrink_on = 1 OR is_auto_close_on = 1) ORDER BY name", _ctx.Token);
            if (rows.Count == 0) return new CheckItemDto { Status = CheckStatus.Gecti, Summary = "Hiçbir DB'de açık değil." };

            var detail = new StringBuilder();
            var script = new StringBuilder();
            foreach (var r in rows)
            {
                var list = new List<string>();
                if (Long(r[1]) == 1) { list.Add("AUTO_SHRINK"); script.AppendLine("ALTER DATABASE " + Q(Str(r[0])) + " SET AUTO_SHRINK OFF;"); }
                if (Long(r[2]) == 1) { list.Add("AUTO_CLOSE"); script.AppendLine("ALTER DATABASE " + Q(Str(r[0])) + " SET AUTO_CLOSE OFF;"); }
                detail.AppendLine("  " + Str(r[0]) + ": " + string.Join(", ", list));
            }
            return new CheckItemDto
            {
                Status = CheckStatus.Uyari,
                Summary = rows.Count + " DB'de açık: " + string.Join(", ", rows.Take(5).Select(r => Str(r[0]))) + (rows.Count > 5 ? "..." : ""),
                Detail = "AUTO_SHRINK: dosya sürekli küçülüp tekrar büyür, index'ler parçalanır, işlemci ve disk boşa harcanır.\r\n"
                         + "AUTO_CLOSE: son kullanıcı çıkınca DB kapanır, her bağlantıda yeniden açılır (yavaş ilk bağlantı).\r\n\r\n" + detail,
                HowToFix = "SSMS → DB → Properties → Options: Auto Shrink = False, Auto Close = False. Hemen geçerli olur.",
                Script = script.ToString(),
                ScriptLanguage = "sql"
            };
        }

        private CheckItemDto CheckVlf()
        {
            // sys.dm_db_log_info: SQL Server 2016 SP2 ve sonrası
            List<object[]> rows;
            try
            {
                rows = Rows(_conn, @"SELECT d.name, COUNT(*) FROM sys.databases d CROSS APPLY sys.dm_db_log_info(d.database_id) li
                                     WHERE d.state = 0 GROUP BY d.name HAVING COUNT(*) > 300 ORDER BY 2 DESC", _ctx.Token);
            }
            catch (SqlException ex) when (ex.Number == 208 || ex.Number == 195)
            {
                return new CheckItemDto { Status = CheckStatus.Bilgi, Summary = "Bu SQL Server sürümünde ölçülemiyor (SQL Server 2016 SP2 ve üstü gerekir)." };
            }

            var high = rows.Where(r => Long(r[1]) > 1000).ToList();
            if (high.Count == 0)
                return new CheckItemDto { Status = CheckStatus.Gecti, Summary = rows.Count == 0 ? "Tüm log dosyalarında parça sayısı normal." : rows.Count + " DB'de biraz yüksek ama sorun değil (1000 altı)." };

            var detail = new StringBuilder("Log dosyası çok küçük adımlarla büyüdüğü için binlerce parçaya (VLF) bölünmüş. DB açılışı, geri yükleme ve log yedeği yavaşlar.\r\n\r\n");
            foreach (var r in rows) detail.AppendLine("  " + Str(r[0]) + ": " + Fmt.Num(Long(r[1])) + " VLF");
            return new CheckItemDto
            {
                Status = CheckStatus.Uyari,
                Summary = high.Count + " DB'nin log dosyası 1000'den fazla parçalı: " + string.Join(", ", high.Take(3).Select(r => Str(r[0]) + " (" + Long(r[1]) + ")")),
                Detail = detail.ToString(),
                HowToFix = "Mesai dışında: 1) FULL recovery ise önce log yedeği alın. 2) Log dosyasını küçültün (DBCC SHRINKFILE). "
                           + "3) Log dosyasını tek seferde ihtiyaç duyulan boyuta büyütün ve büyüme adımını MB ile verin (ör. 512 MB).\r\n"
                           + "\"Log dosyaları\" maddesindeki shrink script'i de bu işe yarar.",
            };
        }

        private CheckItemDto CheckLogFiles()
        {
            var logSpace = new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase);
            foreach (var r in Rows(_conn, "DBCC SQLPERF(LOGSPACE) WITH NO_INFOMSGS", _ctx.Token))
                logSpace[Str(r[0])] = Dbl(r[2]);

            var rows = Rows(_conn, @"
SELECT d.name, d.recovery_model_desc, d.log_reuse_wait_desc,
       (SELECT SUM(CAST(size AS bigint)) FROM sys.master_files mf WHERE mf.database_id = d.database_id AND mf.type = 1) / 128,
       (SELECT SUM(CAST(size AS bigint)) FROM sys.master_files mf WHERE mf.database_id = d.database_id AND mf.type = 0) / 128,
       (SELECT TOP 1 name FROM sys.master_files mf WHERE mf.database_id = d.database_id AND mf.type = 1 ORDER BY file_id),
       (SELECT MAX(backup_finish_date) FROM msdb.dbo.backupset b WHERE b.database_name = d.name AND b.type = 'L'),
       (SELECT MAX(backup_finish_date) FROM msdb.dbo.backupset b WHERE b.database_name = d.name AND b.type = 'D')
FROM sys.databases d WHERE d.database_id > 4 AND d.state = 0 AND d.is_read_only = 0 ORDER BY d.name", _ctx.Token);

            var noLogBackup = new List<string>();
            var bloated = new List<string>();
            var script = new StringBuilder();
            foreach (var r in rows)
            {
                string db = Str(r[0]), model = Str(r[1]), wait = Str(r[2]), logName = Str(r[5]);
                long logMb = Long(r[3]), dataMb = Long(r[4]);
                var lastLog = Date(r[6]);
                double used;
                logSpace.TryGetValue(db, out used);

                if (model != "SIMPLE" && (lastLog == null || lastLog < DateTime.Now.AddDays(-1)))
                {
                    noLogBackup.Add("  " + db + ": " + model + " recovery, son log yedeği: " + (lastLog == null ? "hiç alınmamış" : Fmt.Date(lastLog))
                                    + ", log " + Fmt.Mb(logMb) + (wait == "LOG_BACKUP" ? " (log yedeği bekliyor)" : ""));
                    script.AppendLine("-- " + db + ": nokta zamana geri dönüş gerekmiyorsa SIMPLE yapılabilir (gerekiyorsa düzenli log yedeği alın)");
                    script.AppendLine("-- ALTER DATABASE " + Q(db) + " SET RECOVERY SIMPLE;");
                }

                if (logMb >= 1024 && logMb > dataMb / 2 && used < 25)
                {
                    long target = Math.Max(1024, Math.Min(logMb / 4, Math.Max(1024, dataMb / 4)));
                    bloated.Add("  " + db + ": log " + Fmt.Mb(logMb) + " (dolu " + Fmt.Pct(used) + "), veri " + Fmt.Mb(dataMb));
                    script.AppendLine("USE " + Q(db) + ";");
                    if (model != "SIMPLE") script.AppendLine("-- FULL recovery: önce log yedeği alın, yoksa log küçülmez");
                    script.AppendLine("DBCC SHRINKFILE (" + N(logName) + ", " + target + ");");
                }
            }

            if (noLogBackup.Count == 0 && bloated.Count == 0)
                return new CheckItemDto { Status = CheckStatus.Gecti, Summary = "Log dosyalarında şişme ve eksik log yedeği yok." };

            var detail = new StringBuilder();
            if (noLogBackup.Count > 0)
                detail.AppendLine("FULL / BULK_LOGGED recovery'de log yedeği alınmadıkça log dosyası hiç boşalmaz ve diski doldurana kadar büyür:")
                      .AppendLine(string.Join("\r\n", noLogBackup)).AppendLine();
            if (bloated.Count > 0)
                detail.AppendLine("Log dosyası veri dosyasının yarısından büyük ve çoğu boş (bir kerelik büyük işlem sonrası şişmiş):")
                      .AppendLine(string.Join("\r\n", bloated));

            return new CheckItemDto
            {
                Status = CheckStatus.Uyari,
                Summary = (noLogBackup.Count > 0 ? noLogBackup.Count + " DB'de log yedeği alınmıyor" : "")
                          + (noLogBackup.Count > 0 && bloated.Count > 0 ? ", " : "")
                          + (bloated.Count > 0 ? bloated.Count + " DB'nin log dosyası şişmiş" : "") + ".",
                Detail = detail.ToString(),
                HowToFix = "Log yedeği alınmıyorsa: ya düzenli log yedeği planlayın ya da (nokta zamana geri dönüş gerekmiyorsa) DB'yi SIMPLE recovery'ye alın.\r\n"
                           + "Şişmiş log: mesai dışında DBCC SHRINKFILE ile küçültün. Sadece LOG dosyası küçültülür; veri dosyası küçültmek önerilmez.\r\n"
                           + "SQL Bakımı açılırsa log küçültme bakım penceresinde otomatik yapılabilir.",
                Script = script.ToString(),
                ScriptLanguage = "sql"
            };
        }

        // ================================================================ DB bazında kontroller

        private List<string> UserDatabases()
        {
            return Rows(_conn, "SELECT name FROM sys.databases WHERE database_id > 4 AND state = 0 AND HAS_DBACCESS(name) = 1 ORDER BY name", _ctx.Token)
                .Select(r => Str(r[0])).Where(n => !string.Equals(n, "distribution", StringComparison.OrdinalIgnoreCase)).ToList();
        }

        private void PerDatabaseChecks(List<string> dbs)
        {
            int reorg = GeneralSettings.GetInt(_ctx.Settings, SettingKeys.PerfFragReorganizePercent);
            int rebuild = GeneralSettings.GetInt(_ctx.Settings, SettingKeys.PerfFragRebuildPercent);
            int minPages = GeneralSettings.GetInt(_ctx.Settings, SettingKeys.PerfMinPageCount);

            var tables = new List<TableInfo>();
            var frag = new List<object[]>();
            var unused = new List<object[]>();
            var dupes = new List<string>();
            var dupeScript = new StringBuilder();
            var stats = new List<object[]>();
            var freeSpace = new List<object[]>();
            var errors = new Dictionary<string, List<string>>();
            Action<string, string, Exception> fail = (check, db, ex) =>
            {
                if (ex is OperationCanceledException || _ctx.Token.IsCancellationRequested) throw new OperationCanceledException(_ctx.Token);
                List<string> list;
                if (!errors.TryGetValue(check, out list)) errors[check] = list = new List<string>();
                list.Add(db + ": " + ex.Message);
            };

            for (int i = 0; i < dbs.Count; i++)
            {
                string db = dbs[i];
                _ctx.Token.ThrowIfCancellationRequested();
                _ctx.Progress(_area + ": " + db + " inceleniyor (" + (i + 1) + "/" + dbs.Count + ")");
                try
                {
                    _conn.ChangeDatabase(db);
                }
                catch (Exception ex)
                {
                    fail("db", db, ex);
                    continue;
                }

                try { tables.AddRange(ReadTables(db)); } catch (Exception ex) { fail("tables", db, ex); }
                try { frag.AddRange(ReadFragmentation(db, reorg, minPages)); } catch (Exception ex) { fail("frag", db, ex); }
                try { unused.AddRange(ReadUnusedIndexes(db)); } catch (Exception ex) { fail("unused", db, ex); }
                try { ReadDuplicateIndexes(db, dupes, dupeScript); } catch (Exception ex) { fail("dupes", db, ex); }
                try { stats.AddRange(ReadStaleStats(db)); } catch (Exception ex) { fail("stats", db, ex); }
                try { freeSpace.AddRange(ReadFreeSpace(db)); } catch (Exception ex) { fail("free", db, ex); }
            }
            try { _conn.ChangeDatabase("master"); } catch { }

            Func<string, string> errorText = key =>
            {
                var list = new List<string>();
                List<string> e;
                if (errors.TryGetValue("db", out e)) list.AddRange(e);
                if (errors.TryGetValue(key, out e)) list.AddRange(e);
                return list.Count == 0 ? "" : "\r\n\r\nİncelenemeyen DB'ler:\r\n  " + string.Join("\r\n  ", list.Take(20));
            };

            _ctx.Run(Id("toptables"), _area, "En büyük tablolar", () => TopTablesItem(tables, errorText("tables")));
            _ctx.Run(Id("fragmentation"), _area, "Index parçalanması", () => FragmentationItem(frag, reorg, rebuild, minPages, errorText("frag")));
            _ctx.Run(Id("unusedindex"), _area, "Kullanılmayan index'ler", () => UnusedIndexItem(unused, errorText("unused")));
            _ctx.Run(Id("duplicateindex"), _area, "Tekrar eden index'ler", () => DuplicateIndexItem(dupes, dupeScript, errorText("dupes")));
            _ctx.Run(Id("stats"), _area, "Eski istatistikler", () => StatsItem(stats, errorText("stats")));
            _ctx.Run(Id("datafree"), _area, "Veri dosyalarında boş alan", () => FreeSpaceItem(freeSpace, errorText("free")));
        }

        // --- En büyük tablolar

        private IEnumerable<TableInfo> ReadTables(string db)
        {
            return Rows(_conn, @"
WITH sz AS (
    SELECT object_id, SUM(reserved_page_count) AS pages, SUM(CASE WHEN index_id IN (0, 1) THEN row_count ELSE 0 END) AS rows
    FROM sys.dm_db_partition_stats GROUP BY object_id),
us AS (
    SELECT object_id,
           MAX(CASE WHEN last_user_seek >= ISNULL(last_user_scan, 0) AND last_user_seek >= ISNULL(last_user_lookup, 0) THEN last_user_seek
                    WHEN last_user_scan >= ISNULL(last_user_lookup, 0) THEN last_user_scan ELSE last_user_lookup END) AS last_read,
           MAX(last_user_update) AS last_write
    FROM sys.dm_db_index_usage_stats WHERE database_id = DB_ID() GROUP BY object_id)
SELECT TOP 30 s.name, t.name, sz.pages / 128.0, sz.rows, us.last_read, us.last_write
FROM sys.tables t
JOIN sys.schemas s ON s.schema_id = t.schema_id
JOIN sz ON sz.object_id = t.object_id
LEFT JOIN us ON us.object_id = t.object_id
WHERE t.is_ms_shipped = 0
ORDER BY sz.pages DESC", _ctx.Token, 300)
                .Select(r => new TableInfo
                {
                    Db = db,
                    Schema = Str(r[0]),
                    Name = Str(r[1]),
                    Mb = Dbl(r[2]),
                    Rows = Long(r[3]),
                    LastRead = Date(r[4]),
                    LastWrite = Date(r[5])
                }).ToList();
        }

        private static readonly Regex ArchiveName = new Regex(
            @"(log|history|hist|tarihce|arsiv|archive|temp|tmp|backup|bak|yedek|_old|old_|eski|copy|kopya|_\d{6,8}|\d{8}$)",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

        private CheckItemDto TopTablesItem(List<TableInfo> all, string errors)
        {
            var top = all.OrderByDescending(t => t.Mb).Take(30).ToList();
            if (top.Count == 0) return new CheckItemDto { Status = CheckStatus.Bilgi, Summary = "Kullanıcı tablosu bulunamadı." + errors.Trim() };

            bool enoughUptime = UptimeDays >= 7;
            var candidates = new List<TableInfo>();
            var detail = new StringBuilder("Boyut | Satır | Son okunma | Son yazılma | Tablo\r\n");
            foreach (var t in top)
            {
                bool nameHint = ArchiveName.IsMatch(t.Name);
                bool untouched = enoughUptime && t.LastRead == null && t.LastWrite == null;
                if ((nameHint || untouched) && t.Mb >= 100) candidates.Add(t);
                detail.AppendLine(Fmt.Mb(t.Mb) + " | " + Fmt.Num(t.Rows) + " | " + Fmt.Ago(t.LastRead) + " | " + Fmt.Ago(t.LastWrite) + " | "
                                  + t.Db + "." + t.Schema + "." + t.Name
                                  + (nameHint ? "  ← adı log / geçmiş / yedek tablosuna benziyor" : untouched ? "  ← SQL açıldığından beri hiç kullanılmamış" : ""));
            }
            detail.AppendLine().Append("Son okunma / yazılma SQL Server " + Fmt.Date(_info.StartTime) + " tarihinde başladığından beri ölçülür (yeniden başlayınca sıfırlanır). Tablo içerikleri okunmadı.");

            var biggest = top[0];
            return new CheckItemDto
            {
                Status = CheckStatus.Bilgi,
                Summary = "En büyük: " + biggest.Db + "." + biggest.Schema + "." + biggest.Name + " (" + Fmt.Mb(biggest.Mb) + ")"
                          + (candidates.Count > 0 ? "; " + candidates.Count + " tablo arşivlenebilir / temizlenebilir olabilir (toplam " + Fmt.Mb(candidates.Sum(c => c.Mb)) + ")." : "."),
                Detail = detail + errors,
                HowToFix = candidates.Count == 0 ? null
                    : "İşaretli tablolar log / geçmiş / yedek kopyası gibi görünüyor veya hiç kullanılmıyor. Uygulamanın bunlara ihtiyacı olup olmadığını "
                      + "uygulama üreticisine sorun. Gerekmiyorsa önce yedek alıp eski kayıtları arşivleyin / silin. SecurityGuard hiçbir tabloyu silmez."
            };
        }

        // --- Parçalanma

        private IEnumerable<object[]> ReadFragmentation(string db, int reorg, int minPages)
        {
            var rows = Rows(_conn, @"
SELECT s.name, o.name, i.name, ps.avg_fragmentation_in_percent, ps.page_count, i.type_desc
FROM sys.dm_db_index_physical_stats(DB_ID(), NULL, NULL, NULL, 'LIMITED') ps
JOIN sys.indexes i ON i.object_id = ps.object_id AND i.index_id = ps.index_id
JOIN sys.objects o ON o.object_id = ps.object_id
JOIN sys.schemas s ON s.schema_id = o.schema_id
WHERE ps.index_id > 0 AND ps.alloc_unit_type_desc = 'IN_ROW_DATA' AND o.is_ms_shipped = 0
  AND ps.page_count >= " + minPages + " AND ps.avg_fragmentation_in_percent >= " + reorg + @"
ORDER BY ps.avg_fragmentation_in_percent * ps.page_count DESC", _ctx.Token, 900);
            return rows.Select(r => new object[] { db, r[0], r[1], r[2], r[3], r[4], r[5] });
        }

        private CheckItemDto FragmentationItem(List<object[]> rows, int reorg, int rebuild, int minPages, string errors)
        {
            if (rows.Count == 0)
                return new CheckItemDto { Status = errors.Length > 0 ? CheckStatus.Hata : CheckStatus.Gecti, Summary = "Parçalanmış index yok (%" + reorg + " üstü, " + Fmt.Num(minPages) + " sayfadan büyük)." + (errors.Length > 0 ? " Bazı DB'ler incelenemedi." : ""), Detail = errors.Trim() };

            var toRebuild = rows.Where(r => Dbl(r[4]) >= rebuild).ToList();
            var toReorg = rows.Where(r => Dbl(r[4]) < rebuild).ToList();
            bool online = _info.EngineEdition == 3;

            var detail = new StringBuilder("Parçalanma | Boyut | Index\r\n");
            var script = new StringBuilder("-- Mesai dışında çalıştırın. " + (online
                ? "Enterprise edition: REBUILD ONLINE yapılır, tablolar kullanılabilir kalır."
                : "Standard / Express: REBUILD sırasında tablo kilitlenir (bu yüzden bakım penceresi şart).") + "\r\n");
            foreach (var r in rows.Take(MaxScriptLines))
            {
                double pct = Dbl(r[4]);
                string target = Q(Str(r[0])) + "." + Q(Str(r[1])) + "." + Q(Str(r[2]));
                detail.AppendLine(Fmt.Pct(pct) + " | " + Fmt.Mb(Long(r[5]) / 128.0) + " | " + Str(r[0]) + "." + Str(r[1]) + "." + Str(r[2]) + " → " + Str(r[3]));
                script.AppendLine("ALTER INDEX " + Q(Str(r[3])) + " ON " + target
                                  + (pct >= rebuild ? " REBUILD" + (online ? " WITH (ONLINE = ON)" : "") : " REORGANIZE") + ";");
            }
            if (rows.Count > MaxScriptLines) script.AppendLine("-- ... ve " + (rows.Count - MaxScriptLines) + " index daha (SQL Bakımı hepsini yapar)");

            return new CheckItemDto
            {
                Status = toRebuild.Count > 0 ? CheckStatus.Uyari : CheckStatus.Bilgi,
                Summary = toRebuild.Count + " index yeniden oluşturulmalı (%" + rebuild + " üstü), " + toReorg.Count + " index düzenlenmeli (%" + reorg + "-" + rebuild + ").",
                Detail = "Parçalanmış index'lerde SQL Server aynı veri için daha çok sayfa okur; sorgular yavaşlar, bellek boşa harcanır.\r\n"
                         + "Not: SSD disklerde parçalanmanın etkisi eskisine göre azdır; çoğu zaman istatistiklerin güncel olması daha çok fark yaratır. Bakımda ikisi birlikte yapılır.\r\n"
                         + "Ölçüm LIMITED modda yapıldı (sunucuyu yormaz). Eşikler Genel Ayarlar → Performans ve bakım'dan değiştirilebilir.\r\n\r\n" + detail + errors,
                HowToFix = "Script'i mesai dışında SSMS'te çalıştırın. REORGANIZE her zaman çevrimiçidir; REBUILD istatistikleri de günceller.\r\n"
                           + "Düzenli yapılması için SQL Bakımı (bakım penceresinde otomatik) açılabilir; sunucuda zaten bir bakım işi varsa ikisini birden açmayın.",
                Script = script.ToString(),
                ScriptLanguage = "sql"
            };
        }

        // --- Kullanılmayan index'ler

        private IEnumerable<object[]> ReadUnusedIndexes(string db)
        {
            var rows = Rows(_conn, @"
SELECT s.name, o.name, i.name, ISNULL(us.user_updates, 0),
       (SELECT SUM(ps.reserved_page_count) FROM sys.dm_db_partition_stats ps WHERE ps.object_id = i.object_id AND ps.index_id = i.index_id) / 128.0
FROM sys.indexes i
JOIN sys.objects o ON o.object_id = i.object_id
JOIN sys.schemas s ON s.schema_id = o.schema_id
LEFT JOIN sys.dm_db_index_usage_stats us ON us.database_id = DB_ID() AND us.object_id = i.object_id AND us.index_id = i.index_id
WHERE o.type = 'U' AND o.is_ms_shipped = 0 AND i.type = 2 AND i.is_primary_key = 0 AND i.is_unique = 0
  AND i.is_unique_constraint = 0 AND i.is_hypothetical = 0 AND i.is_disabled = 0
  AND ISNULL(us.user_seeks, 0) + ISNULL(us.user_scans, 0) + ISNULL(us.user_lookups, 0) = 0
ORDER BY 5 DESC", _ctx.Token, 300);
            return rows.Where(r => Dbl(r[4]) >= 1).Select(r => new object[] { db, r[0], r[1], r[2], r[3], r[4] });
        }

        private CheckItemDto UnusedIndexItem(List<object[]> rows, string errors)
        {
            if (UptimeDays < 7)
                return new CheckItemDto { Status = CheckStatus.Bilgi, Summary = "SQL Server " + (int)UptimeDays + " gündür açık; güvenilir sonuç için en az 7 gün gerekir." };
            if (rows.Count == 0)
                return new CheckItemDto { Status = CheckStatus.Gecti, Summary = "SQL açıldığından beri (" + (int)UptimeDays + " gün) hiç kullanılmayan index yok.", Detail = errors.Trim() };

            var sorted = rows.OrderByDescending(r => Dbl(r[5])).ToList();
            var detail = new StringBuilder("Boyut | Güncellenme | Index\r\n");
            var script = new StringBuilder("-- DİKKAT: Ay sonu / yıl sonu raporları gibi seyrek çalışan işler bu index'leri kullanıyor olabilir.\r\n"
                                           + "-- Silmeden önce uygulama üreticisine danışın. Satırlar bilerek yorum (--) olarak bırakıldı.\r\n");
            foreach (var r in sorted.Take(100))
            {
                detail.AppendLine(Fmt.Mb(Dbl(r[5])) + " | " + Fmt.Num(Long(r[4])) + " | " + Str(r[0]) + "." + Str(r[1]) + "." + Str(r[2]) + " → " + Str(r[3]));
                script.AppendLine("-- DROP INDEX " + Q(Str(r[3])) + " ON " + Q(Str(r[0])) + "." + Q(Str(r[1])) + "." + Q(Str(r[2])) + ";");
            }

            return new CheckItemDto
            {
                Status = CheckStatus.Bilgi,
                Summary = rows.Count + " index SQL açıldığından beri (" + (int)UptimeDays + " gün) hiç okunmadı (toplam " + Fmt.Mb(rows.Sum(r => Dbl(r[5]))) + ").",
                Detail = "Hiç okunmayan index'ler her INSERT / UPDATE / DELETE'te güncellenir: yazma işlemlerini yavaşlatır, disk ve yedek boyutunu büyütür.\r\n"
                         + "Primary key ve unique index'ler listede yok.\r\n\r\n" + detail + errors,
                HowToFix = "Uygulama üreticisine sorun; gereksizse silinebilir. Seyrek çalışan raporlar (ay sonu vb.) için gerekebilir, bu yüzden aceleci olmayın.",
                Script = script.ToString(),
                ScriptLanguage = "sql"
            };
        }

        // --- Tekrar eden index'ler

        private void ReadDuplicateIndexes(string db, List<string> dupes, StringBuilder script)
        {
            var rows = Rows(_conn, @"
SELECT s.name, o.name, i.name, i.is_primary_key, i.is_unique, i.type,
       STUFF((SELECT ',' + c.name + CASE WHEN ic.is_descending_key = 1 THEN ' DESC' ELSE '' END
              FROM sys.index_columns ic JOIN sys.columns c ON c.object_id = ic.object_id AND c.column_id = ic.column_id
              WHERE ic.object_id = i.object_id AND ic.index_id = i.index_id AND ic.is_included_column = 0
              ORDER BY ic.key_ordinal FOR XML PATH('')), 1, 1, ''),
       STUFF((SELECT ',' + c.name
              FROM sys.index_columns ic JOIN sys.columns c ON c.object_id = ic.object_id AND c.column_id = ic.column_id
              WHERE ic.object_id = i.object_id AND ic.index_id = i.index_id AND ic.is_included_column = 1
              ORDER BY c.name FOR XML PATH('')), 1, 1, '')
FROM sys.indexes i
JOIN sys.objects o ON o.object_id = i.object_id
JOIN sys.schemas s ON s.schema_id = o.schema_id
WHERE o.type = 'U' AND o.is_ms_shipped = 0 AND i.type IN (1, 2) AND i.is_hypothetical = 0 AND i.has_filter = 0", _ctx.Token, 300);

            foreach (var group in rows.GroupBy(r => Str(r[0]) + "." + Str(r[1]) + "|" + Str(r[6]), StringComparer.OrdinalIgnoreCase).Where(g => g.Count() > 1))
            {
                var list = group.ToList();
                // Tutulacak: PK / clustered / unique olan, yoksa include'u en geniş olan
                var keep = list.OrderByDescending(r => Long(r[3]) == 1 || Long(r[5]) == 1)
                               .ThenByDescending(r => Long(r[4]) == 1)
                               .ThenByDescending(r => (Str(r[7]) ?? "").Length).First();
                dupes.Add(db + "." + Str(keep[0]) + "." + Str(keep[1]) + " (" + Str(keep[6]) + "): " + string.Join(", ", list.Select(r => Str(r[2]))));
                foreach (var r in list.Where(r => r != keep && Long(r[3]) == 0 && Long(r[5]) == 2 && Long(r[4]) == 0))
                {
                    string keepIncl = Str(keep[7]) ?? "";
                    var mine = (Str(r[7]) ?? "").Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries);
                    bool covered = mine.All(c => keepIncl.Split(',').Contains(c, StringComparer.OrdinalIgnoreCase));
                    script.AppendLine("-- " + db + ": " + Str(r[2]) + " ile " + Str(keep[2]) + " aynı anahtar kolonlara sahip"
                                      + (covered ? "" : " (INCLUDE kolonları farklı: silmeden önce " + Str(keep[2]) + " index'ine eklenmeli)"));
                    script.AppendLine("-- DROP INDEX " + Q(Str(r[2])) + " ON " + Q(db) + "." + Q(Str(r[0])) + "." + Q(Str(r[1])) + ";");
                }
            }
        }

        private CheckItemDto DuplicateIndexItem(List<string> dupes, StringBuilder script, string errors)
        {
            if (dupes.Count == 0)
                return new CheckItemDto { Status = CheckStatus.Gecti, Summary = "Aynı kolonlarda tekrar eden index yok.", Detail = errors.Trim() };

            return new CheckItemDto
            {
                Status = CheckStatus.Uyari,
                Summary = dupes.Count + " tabloda aynı anahtar kolonlarla birden fazla index var.",
                Detail = "Aynı kolonlardaki ikinci index okumaya katkı sağlamaz ama her yazmada güncellenir ve yer kaplar.\r\n\r\n"
                         + string.Join("\r\n", dupes.Take(100)) + errors,
                HowToFix = "Fazla olanı silin (script'te hangisinin silinebileceği yazıyor; satırlar bilerek yorum olarak bırakıldı). "
                           + "Uygulama index'i adıyla kullanıyorsa (index hint) silmeden önce üreticiye danışın.",
                Script = script.Length == 0 ? null : "-- Satırlar bilerek yorum (--) olarak bırakıldı: kontrol edip başındaki -- işaretini kaldırın.\r\n" + script,
                ScriptLanguage = "sql"
            };
        }

        // --- Eski istatistikler

        private IEnumerable<object[]> ReadStaleStats(string db)
        {
            var rows = Rows(_conn, @"
SELECT s.name, o.name, st.name, sp.last_updated, sp.rows, sp.modification_counter
FROM sys.stats st
JOIN sys.objects o ON o.object_id = st.object_id
JOIN sys.schemas s ON s.schema_id = o.schema_id
CROSS APPLY sys.dm_db_stats_properties(st.object_id, st.stats_id) sp
WHERE o.type = 'U' AND o.is_ms_shipped = 0 AND sp.rows >= 1000 AND sp.modification_counter > 0
  AND (sp.modification_counter >= sp.rows * 0.2
       OR (sp.last_updated < DATEADD(DAY, -30, GETDATE()) AND sp.modification_counter >= sp.rows * 0.05))
ORDER BY sp.modification_counter DESC", _ctx.Token, 300);
            return rows.Select(r => new object[] { db, r[0], r[1], r[2], r[3], r[4], r[5] });
        }

        private CheckItemDto StatsItem(List<object[]> rows, string errors)
        {
            if (rows.Count == 0)
                return new CheckItemDto { Status = CheckStatus.Gecti, Summary = "İstatistikler güncel.", Detail = errors.Trim() };

            var tables = rows.GroupBy(r => Q(Str(r[0])) + "." + Q(Str(r[1])) + "." + Q(Str(r[2]))).ToList();
            var detail = new StringBuilder("Son güncelleme | Satır | Değişen | İstatistik\r\n");
            foreach (var r in rows.Take(100))
                detail.AppendLine(Fmt.Date(Date(r[4])) + " | " + Fmt.Num(Long(r[5])) + " | " + Fmt.Num(Long(r[6])) + " | "
                                  + Str(r[0]) + "." + Str(r[1]) + "." + Str(r[2]) + " → " + Str(r[3]));
            var script = new StringBuilder("-- Mesai dışında çalıştırın (büyük tablolarda birkaç dakika sürebilir)\r\n");
            foreach (var t in tables.Take(MaxScriptLines)) script.AppendLine("UPDATE STATISTICS " + t.Key + ";");

            return new CheckItemDto
            {
                Status = CheckStatus.Uyari,
                Summary = tables.Count + " tabloda istatistik eski (satırların %20'sinden fazlası değişmiş veya 30 günden eski).",
                Detail = "SQL Server sorgu planını istatistiklere göre seçer. İstatistik eskiyse yanlış plan seçilir: dün hızlı çalışan sorgu bugün yavaşlar.\r\n\r\n" + detail + errors,
                HowToFix = "Script tabloların istatistiklerini günceller. Düzenli yapılması için SQL Bakımı açılabilir (index bakımıyla birlikte).",
                Script = script.ToString(),
                ScriptLanguage = "sql"
            };
        }

        // --- Veri dosyalarında boş alan

        private IEnumerable<object[]> ReadFreeSpace(string db)
        {
            var rows = Rows(_conn, "SELECT name, size / 128.0, CAST(FILEPROPERTY(name, 'SpaceUsed') AS bigint) / 128.0 FROM sys.database_files WHERE type = 0", _ctx.Token);
            return rows.Select(r => new object[] { db, r[0], r[1], r[2] });
        }

        private CheckItemDto FreeSpaceItem(List<object[]> rows, string errors)
        {
            var big = rows.Where(r => Dbl(r[2]) - Dbl(r[3]) >= 5120 && Dbl(r[3]) < Dbl(r[2]) * 0.5).ToList();
            if (big.Count == 0)
                return new CheckItemDto { Status = CheckStatus.Gecti, Summary = "Veri dosyalarında aşırı boş alan yok.", Detail = errors.Trim() };

            var detail = new StringBuilder();
            var script = new StringBuilder("-- Sadece disk gerçekten lazımsa ve mesai dışında. Sonrasında index bakımı (REBUILD) şart.\r\n");
            foreach (var r in big)
            {
                double size = Dbl(r[2]), used = Dbl(r[3]);
                detail.AppendLine("  " + Str(r[0]) + " / " + Str(r[1]) + ": " + Fmt.Mb(size) + ", kullanılan " + Fmt.Mb(used) + ", boş " + Fmt.Mb(size - used));
                script.AppendLine("-- USE " + Q(Str(r[0])) + "; DBCC SHRINKFILE (" + N(Str(r[1])) + ", " + (long)Math.Ceiling(used * 1.2) + ");");
            }
            return new CheckItemDto
            {
                Status = CheckStatus.Bilgi,
                Summary = big.Count + " veri dosyasında toplam " + Fmt.Mb(big.Sum(r => Dbl(r[2]) - Dbl(r[3]))) + " boş alan var.",
                Detail = "Boş alan SQL Server tarafından tekrar kullanılır; disk dolmuyorsa dokunmayın.\r\n\r\n" + detail + errors,
                HowToFix = "Veri dosyası küçültmek (shrink) ÖNERİLMEZ: index'leri tamamen parçalar, sonra REBUILD edince dosya yine büyür. "
                           + "Sadece disk gerçekten lazımsa, mesai dışında, ardından index bakımı yaparak uygulayın. SecurityGuard bunu otomatik yapmaz.",
                Script = script.ToString(),
                ScriptLanguage = "sql"
            };
        }

        // ================================================================ Sunucu geneli (DMV)

        private CheckItemDto CheckMissingIndexes()
        {
            var rows = Rows(_conn, @"
SELECT TOP 25 DB_NAME(d.database_id), d.statement, d.equality_columns, d.inequality_columns, d.included_columns,
       gs.user_seeks, gs.user_scans, gs.avg_user_impact, gs.last_user_seek,
       gs.avg_total_user_cost * (gs.avg_user_impact / 100.0) * (gs.user_seeks + gs.user_scans) AS score
FROM sys.dm_db_missing_index_groups g
JOIN sys.dm_db_missing_index_group_stats gs ON gs.group_handle = g.index_group_handle
JOIN sys.dm_db_missing_index_details d ON d.index_handle = g.index_handle
WHERE d.database_id > 4
  AND gs.avg_total_user_cost * (gs.avg_user_impact / 100.0) * (gs.user_seeks + gs.user_scans) > 10
ORDER BY score DESC", _ctx.Token);

            if (rows.Count == 0)
                return new CheckItemDto { Status = CheckStatus.Gecti, Summary = "SQL Server önemli bir eksik index bildirmiyor." };

            var detail = new StringBuilder("Etki | Kullanım | Tablo | Kolonlar\r\n");
            var script = new StringBuilder("-- SQL Server'ın önerileri: aynı tabloya benzer öneriler gelebilir, birleştirerek az sayıda index oluşturun.\r\n"
                                           + "-- Çok index yazmayı yavaşlatır. Önce test ortamında deneyin. Mesai dışında çalıştırın.\r\n");
            int n = 0;
            foreach (var r in rows)
            {
                n++;
                string statement = Str(r[1]);
                string eq = Str(r[2]), ineq = Str(r[3]), incl = Str(r[4]);
                string keys = string.Join(", ", new[] { eq, ineq }.Where(x => !string.IsNullOrEmpty(x)));
                string table = statement.Split('.').Last().Trim('[', ']');
                detail.AppendLine(Fmt.Pct(Dbl(r[7])) + " | " + Fmt.Num(Long(r[5]) + Long(r[6])) + " | " + statement + " | " + keys + (incl != null ? " INCLUDE " + incl : ""));
                script.AppendLine("-- Tahmini iyileşme " + Fmt.Pct(Dbl(r[7])) + ", " + Fmt.Num(Long(r[5]) + Long(r[6])) + " kez istendi, son: " + Fmt.Date(Date(r[8])));
                script.AppendLine("CREATE NONCLUSTERED INDEX " + Q("IX_SG_" + Regex.Replace(table, @"[^\w]", "_") + "_" + n) + " ON " + statement
                                  + " (" + keys + ")" + (incl != null ? " INCLUDE (" + incl + ")" : "") + ";");
            }

            bool strong = rows.Any(r => Dbl(r[7]) >= 50 && Long(r[5]) + Long(r[6]) >= 100);
            return new CheckItemDto
            {
                Status = strong ? CheckStatus.Uyari : CheckStatus.Bilgi,
                Summary = rows.Count + " eksik index önerisi" + (strong ? " (bazıları sorguları belirgin hızlandırır)" : "") + ".",
                Detail = "SQL Server sorguları çalıştırırken \"şu index olsaydı daha hızlı olurdu\" dediği yerleri kaydeder. Liste SQL yeniden başlayınca sıfırlanır.\r\n\r\n" + detail,
                HowToFix = "Script'teki CREATE INDEX komutlarını inceleyin; aynı tablo için benzer olanları birleştirin. Uygulamak sizin kararınız: "
                           + "SecurityGuard index oluşturmaz.",
                Script = script.ToString(),
                ScriptLanguage = "sql"
            };
        }

        private static readonly HashSet<string> BenignWaits = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "CHECKPOINT_QUEUE", "CHKPT", "CLR_AUTO_EVENT", "CLR_MANUAL_EVENT", "CLR_SEMAPHORE", "DBMIRROR_DBM_EVENT", "DBMIRROR_EVENTS_QUEUE",
            "DBMIRROR_WORKER_QUEUE", "DBMIRRORING_CMD", "DIRTY_PAGE_POLL", "DISPATCHER_QUEUE_SEMAPHORE", "EXECSYNC", "FSAGENT", "KSOURCE_WAKEUP",
            "LAZYWRITER_SLEEP", "LOGMGR_QUEUE", "MEMORY_ALLOCATION_EXT", "ONDEMAND_TASK_QUEUE", "PREEMPTIVE_OS_FLUSHFILEBUFFERS",
            "PREEMPTIVE_XE_GETTARGETSTATE", "PVS_PREALLOCATE", "REDO_THREAD_PENDING_WORK", "REQUEST_FOR_DEADLOCK_SEARCH", "RESOURCE_QUEUE",
            "SERVER_IDLE_CHECK", "SNI_HTTP_ACCEPT", "SOS_WORK_DISPATCHER", "SP_SERVER_DIAGNOSTICS_SLEEP", "UCS_SESSION_REGISTRATION",
            "VDI_CLIENT_OTHER", "WAIT_FOR_RESULTS", "WAITFOR", "WAITFOR_TASKSHUTDOWN", "CXCONSUMER", "SOS_WORKER_MIGRATION",
            "PREEMPTIVE_OS_QUERYREGISTRY", "PREEMPTIVE_OS_CRYPTOPS", "PREEMPTIVE_OS_CRYPTACQUIRECONTEXT", "STARTUP_DEPENDENCY_MANAGER", "XTP_PREEMPTIVE_TASK", "PREEMPTIVE_OS_DEVICEOPS", "PREEMPTIVE_OS_AUTHENTICATIONOPS", "PREEMPTIVE_OS_AUTHORIZATIONOPS"
        };

        private static readonly string[] BenignPrefixes = { "SLEEP_", "BROKER_", "XE_", "QDS_", "HADR_", "PWAIT_", "PARALLEL_REDO", "WAIT_XTP", "FT_IFTS", "SQLTRACE_", "AZURE_" };

        private static string ExplainWait(string type)
        {
            if (type.StartsWith("PAGEIOLATCH_")) return "veri diskten okunuyor: disk yavaş, bellek yetersiz veya index eksik";
            if (type == "WRITELOG") return "log diskine yazma bekleniyor: log dosyasının diski yavaş";
            if (type == "CXPACKET" || type.StartsWith("CXSYNC")) return "paralel sorgular: MAXDOP / cost threshold ayarları ve eksik index'ler";
            if (type.StartsWith("LCK_M_")) return "kilit bekleme (blocking): uzun süren transaction'lar veya eksik index";
            if (type == "SOS_SCHEDULER_YIELD") return "işlemci baskısı: çok tarama yapan sorgular veya yetersiz CPU";
            if (type == "RESOURCE_SEMAPHORE") return "sorgular çalışmak için bellek bekliyor: bellek yetersiz veya çok büyük sıralamalar";
            if (type == "ASYNC_NETWORK_IO") return "uygulama sonuçları yavaş alıyor (ağ veya istemci programı)";
            if (type.StartsWith("PAGELATCH_")) return "bellekteki sayfalara erişim çekişmesi (çoğunlukla tempdb dosya sayısı)";
            if (type == "THREADPOOL") return "SQL Server'ın iş parçacığı tükendi: aşırı yük veya ciddi blocking";
            if (type.StartsWith("BACKUP") || type == "ASYNC_IO_COMPLETION" || type == "IO_COMPLETION") return "disk G/Ç (yedekleme, sıralama dökümü vb.)";
            if (type == "OLEDB") return "linked server / dış veri kaynağı çağrıları";
            if (type.StartsWith("PREEMPTIVE_")) return "işletim sistemi çağrıları";
            return null;
        }

        private CheckItemDto CheckWaits()
        {
            var rows = Rows(_conn, "SELECT TOP 80 wait_type, wait_time_ms, waiting_tasks_count FROM sys.dm_os_wait_stats WHERE wait_time_ms > 0 ORDER BY wait_time_ms DESC", _ctx.Token)
                .Where(r => !BenignWaits.Contains(Str(r[0])) && !BenignPrefixes.Any(p => Str(r[0]).StartsWith(p, StringComparison.OrdinalIgnoreCase))).ToList();
            if (rows.Count == 0) return new CheckItemDto { Status = CheckStatus.Gecti, Summary = "Kayda değer bekleme yok." };

            double total = rows.Sum(r => Dbl(r[1]));
            var top = rows.Take(10).ToList();
            var detail = new StringBuilder("Pay | Toplam süre | Bekleme tipi: anlamı\r\n");
            foreach (var r in top)
            {
                string type = Str(r[0]);
                string explain = ExplainWait(type);
                detail.AppendLine(Fmt.Pct(Dbl(r[1]) * 100 / total) + " | " + Fmt.Num((long)(Dbl(r[1]) / 1000 / 60)) + " dk | " + type + (explain != null ? ": " + explain : ""));
            }
            detail.AppendLine().Append("SQL Server " + Fmt.Date(_info.StartTime) + " tarihinden beri toplanan değerler. Zararsız arka plan beklemeleri listeye alınmadı.");

            bool threadpool = top.Take(5).Any(r => Str(r[0]) == "THREADPOOL");
            string first = Str(top[0][0]);
            string firstExplain = ExplainWait(first);
            return new CheckItemDto
            {
                Status = threadpool ? CheckStatus.Uyari : CheckStatus.Bilgi,
                Summary = "En çok: " + first + " (" + Fmt.Pct(Dbl(top[0][1]) * 100 / total) + ")" + (firstExplain != null ? ": " + firstExplain : "") + ".",
                Detail = detail.ToString(),
                HowToFix = "Bekleme tipleri SQL Server'ın neyi beklerken zaman kaybettiğini gösterir. En üstteki tipin anlamına göre diğer maddelere bakın "
                           + "(disk, bellek, MAXDOP, eksik index...)."
            };
        }

        private CheckItemDto CheckSlowQueries()
        {
            var rows = Rows(_conn, @"
SELECT TOP 10 qs.execution_count, qs.total_elapsed_time / 1000, qs.total_elapsed_time / qs.execution_count / 1000,
       qs.total_worker_time / qs.execution_count / 1000, qs.total_logical_reads / qs.execution_count, qs.last_execution_time,
       DB_NAME(st.dbid),
       SUBSTRING(st.text, (qs.statement_start_offset / 2) + 1,
                 ((CASE qs.statement_end_offset WHEN -1 THEN DATALENGTH(st.text) ELSE qs.statement_end_offset END - qs.statement_start_offset) / 2) + 1)
FROM sys.dm_exec_query_stats qs
CROSS APPLY sys.dm_exec_sql_text(qs.sql_handle) st
WHERE qs.execution_count >= 2 AND (st.dbid IS NULL OR st.dbid > 4)
ORDER BY qs.total_elapsed_time DESC", _ctx.Token);
            if (rows.Count == 0) return new CheckItemDto { Status = CheckStatus.Gecti, Summary = "Kayıtlı sorgu istatistiği yok." };

            var detail = new StringBuilder("Toplam | Ortalama | Çalışma | Ort. okuma | DB | Sorgu\r\n");
            var script = new StringBuilder("-- En çok toplam süre harcayan sorgular (sadece inceleme için; çalıştırmayın)\r\n");
            int n = 0;
            foreach (var r in rows)
            {
                n++;
                string text = Regex.Replace(Str(r[7]) ?? "", @"\s+", " ").Trim();
                detail.AppendLine(Fmt.Num(Long(r[1]) / 1000) + " sn | " + Fmt.Num(Long(r[2])) + " ms | " + Fmt.Num(Long(r[0])) + " | "
                                  + Fmt.Num(Long(r[4])) + " sayfa | " + (Str(r[6]) ?? "-") + " | " + (text.Length > 200 ? text.Substring(0, 200) + "..." : text));
                string full = Str(r[7]) ?? "";
                script.AppendLine().AppendLine("-- " + n + ") " + (Str(r[6]) ?? "-") + ": ortalama " + Fmt.Num(Long(r[2])) + " ms, " + Fmt.Num(Long(r[0])) + " kez")
                      .AppendLine(full.Length > 4000 ? full.Substring(0, 4000) + "\r\n-- (kısaltıldı)" : full);
            }

            return new CheckItemDto
            {
                Status = CheckStatus.Bilgi,
                Summary = "En çok toplam süre harcayan sorgu: " + Fmt.Num(Long(rows[0][1]) / 1000) + " sn (" + Fmt.Num(Long(rows[0][0])) + " kez çalışmış, ortalama "
                          + (Long(rows[0][2]) == 0 ? "1 ms'den az" : Fmt.Num(Long(rows[0][2])) + " ms") + ").",
                Detail = "Bu sorgular sunucunun zamanının en büyük kısmını harcıyor. Ortalama okuma çok yüksekse genellikle index eksiktir "
                         + "(Eksik index önerileri maddesine bakın).\r\n\r\n" + detail,
                HowToFix = "Sorgu metinleri script alanında. Uygulama üreticisine iletilebilir; çoğu zaman eksik index veya eski istatistik sebebidir.",
                Script = script.ToString(),
                ScriptLanguage = "sql"
            };
        }

        private CheckItemDto CheckOtherJobs()
        {
            if (_info.EngineEdition == 4)
                return new CheckItemDto { Status = CheckStatus.Bilgi, Summary = "Express edition: SQL Agent yok, zamanlanmış SQL işi çalışmaz (bakım / yedek başka araçla yapılıyor olabilir)." };

            var rows = Rows(_conn, @"
SELECT j.name, j.enabled, s.step_name, s.subsystem, CAST(s.command AS nvarchar(4000))
FROM msdb.dbo.sysjobs j JOIN msdb.dbo.sysjobsteps s ON s.job_id = j.job_id
ORDER BY j.name, s.step_id", _ctx.Token);

            string agentState = null;
            try
            {
                agentState = Str(Scalar(_conn, "SELECT TOP 1 status_desc FROM sys.dm_server_services WHERE servicename LIKE N'SQL Server Agent%'", _ctx.Token));
            }
            catch (SqlException)
            {
                // eski sürüm / yetki: Agent durumu bilinmiyor
            }

            var index = new List<string>();
            var backup = new List<string>();
            var userJobs = new List<string>();
            foreach (var job in rows.GroupBy(r => Str(r[0])))
            {
                // SQL Server'ın kendi kurduğu temizlik işi sayılmaz
                if (job.Key.StartsWith("syspolicy_purge_history", StringComparison.OrdinalIgnoreCase)) continue;
                if (Long(job.First()[1]) == 1) userJobs.Add("  " + job.Key);
                string all = string.Join("\n", job.Select(r => Str(r[2]) + " " + Str(r[3]) + " " + Str(r[4])));
                bool enabled = Long(job.First()[1]) == 1;
                string label = "  " + job.Key + (enabled ? "" : " (devre dışı)");
                if (Regex.IsMatch(all, @"REBUILD|REORGANIZE|IndexOptimize|UPDATE\s+STATISTICS|sp_updatestats|Rebuild Index|Reorganize Index|Maintenance ?Plan|SSIS", RegexOptions.IgnoreCase))
                    index.Add(label);
                if (Regex.IsMatch(all, @"BACKUP\s+(DATABASE|LOG)|DatabaseBackup|Back Up Database", RegexOptions.IgnoreCase))
                    backup.Add(label);
            }

            bool agentStopped = agentState != null && !agentState.Equals("Running", StringComparison.OrdinalIgnoreCase);
            bool jobsNotRunning = agentStopped && userJobs.Count > 0;
            if (index.Count == 0 && backup.Count == 0 && !jobsNotRunning)
                return new CheckItemDto { Status = CheckStatus.Bilgi, Summary = "Sunucuda index bakımı veya yedek alan SQL Agent işi yok." + (agentState != null ? " (SQL Agent: " + agentState + ")" : "") };

            var detail = new StringBuilder();
            if (index.Count > 0) detail.AppendLine("Index / istatistik bakımı yapan işler:").AppendLine(string.Join("\r\n", index)).AppendLine();
            if (backup.Count > 0) detail.AppendLine("Yedek alan işler:").AppendLine(string.Join("\r\n", backup)).AppendLine();
            if (jobsNotRunning) detail.AppendLine("SQL Agent çalışmadığı için çalışmayan etkin işler:").AppendLine(string.Join("\r\n", userJobs)).AppendLine();
            if (agentState != null) detail.AppendLine("SQL Server Agent servisi: " + agentState);

            return new CheckItemDto
            {
                Status = jobsNotRunning ? CheckStatus.Uyari : CheckStatus.Bilgi,
                Summary = (jobsNotRunning ? "SQL Agent çalışmıyor: " + userJobs.Count + " zamanlanmış iş çalışmaz." + (index.Count + backup.Count > 0 ? " " : "") : "")
                          + (index.Count > 0 ? index.Count + " bakım işi" : "") + (index.Count > 0 && backup.Count > 0 ? ", " : "")
                          + (backup.Count > 0 ? backup.Count + " yedek işi" : "") + (index.Count + backup.Count > 0 ? " var." : ""),
                Detail = detail.ToString(),
                HowToFix = "Sunucuda zaten bakım / yedek işi varsa SecurityGuard'ın SQL Bakımı ve Yedekleme özelliklerini aynı DB'ler için açmayın: "
                           + "aynı iş iki kez yapılır."
                           + (agentStopped ? "\r\nSQL Agent durmuşsa: services.msc → SQL Server Agent → Başlat ve başlangıç türünü Otomatik yapın." : "")
            };
        }

        private static string Limit(StringBuilder script)
        {
            var lines = script.ToString().Split(new[] { "\r\n" }, StringSplitOptions.None);
            if (lines.Length <= MaxScriptLines) return script.ToString();
            return string.Join("\r\n", lines.Take(MaxScriptLines)) + "\r\n-- ... ve " + (lines.Length - MaxScriptLines) + " satır daha";
        }

        private sealed class ServerInfo
        {
            public string Version, Edition;
            public int Major, EngineEdition, Cpu, NumaNodes;
            public long MemoryMb;
            public DateTime StartTime;
        }

        private sealed class TableInfo
        {
            public string Db, Schema, Name;
            public double Mb;
            public long Rows;
            public DateTime? LastRead, LastWrite;
        }
    }
}
