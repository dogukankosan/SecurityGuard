using System;
using System.Collections.Generic;
using System.Data.SqlClient;
using System.IO;
using System.Linq;
using System.Text;
using SecurityGuard.Shared;
using SecurityGuard.Shared.Dto;
using SecurityGuard.Shared.Enums;
using static SecurityGuardService.Checks.SqlQuery;

namespace SecurityGuardService.Checks.Security
{
    /// <summary>
    /// SQL Server güvenlik sağlık kontrolü (bir instance). Sadece OKUR. Şifre testi PWDCOMPARE ile SQL Server içinde yapılır;
    /// denenen şifreler ve sonuçlar ASLA loglanmaz / gösterilmez, sadece "zayıf şifreli login var" denir.
    /// </summary>
    internal sealed class SqlSecurityChecks
    {
        private readonly CheckContext _ctx;
        private readonly string _instance;
        private readonly string _area;
        private readonly SqlConnection _conn;
        private readonly Func<string, string> _auditState;
        private int _major;

        // Yaygın şifreler (Türkiye'deki ERP / muhasebe kurulumlarında sık görülenler dahil). Kod içinde kalır, hiçbir yere yazılmaz.
        private static readonly string[] CommonPasswords =
        {
            "123456", "12345678", "123456789", "1234567890", "1234", "12345", "123", "111111", "000000", "123123", "1q2w3e4r", "1q2w3e", "qwerty", "abc123",
            "password", "Password1", "P@ssw0rd", "P@ssword", "Passw0rd", "Aa123456", "Abc123", "Admin123", "admin123", "admin", "administrator",
            "sa", "sa123", "Sa123456", "sql", "sqlserver", "sql2019", "sql2022", "root", "test", "12345678a", "logo", "Logo1234", "LOGO", "netsis", "mikro", "eta", "zirve"
        };

        private SqlSecurityChecks(CheckContext ctx, string instance, SqlConnection conn, Func<string, string> auditState)
        {
            _ctx = ctx;
            _instance = instance;
            _area = "SQL Server (" + instance + ")";
            _conn = conn;
            _auditState = auditState;
        }

        /// <param name="auditState">SecurityGuard SQL izlemesinin durumu (null = çalışıyor, değilse açıklama).</param>
        public static void Run(CheckContext ctx, string instance, string connectionString, Func<string, string> auditState)
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
                    cmd.CommandText = "SET LOCK_TIMEOUT 10000; SET DEADLOCK_PRIORITY LOW;";
                    cmd.ExecuteNonQuery();
                }
            }
            catch (Exception ex)
            {
                if (conn != null) conn.Dispose();
                ctx.Add(CheckContext.Item("sec.sql.connect@" + instance, area, "SQL Server bağlantısı", CheckStatus.Hata,
                    "SQL Server'a bağlanılamadı, SQL güvenlik kontrolleri yapılamadı: " + ex.Message));
                return;
            }
            using (conn)
                new SqlSecurityChecks(ctx, instance, conn, auditState).RunAll();
        }

        private string Id(string name)
        {
            return "sec.sql." + name + "@" + _instance;
        }

        private void RunAll()
        {
            _major = (int)Long(Scalar(_conn, "SELECT CAST(PARSENAME(CAST(SERVERPROPERTY('ProductVersion') AS nvarchar(64)), 4) AS int)", _ctx.Token));
            _ctx.Run(Id("version"), _area, "SQL Server sürümü desteği", CheckVersion);
            _ctx.Run(Id("audit"), _area, "SecurityGuard SQL izlemesi", CheckGuardAudit);
            _ctx.Run(Id("loginaudit"), _area, "Başarısız giriş kaydı (login auditing)", CheckLoginAuditing);
            _ctx.Run(Id("sa"), _area, "sa hesabı", CheckSa);
            _ctx.Run(Id("weakpasswords"), _area, "Zayıf şifreli SQL login'leri", CheckWeakPasswords);
            _ctx.Run(Id("checkpolicy"), _area, "Şifre politikası uygulanmayan login'ler", CheckPolicy);
            _ctx.Run(Id("sysadmin"), _area, "sysadmin üyeleri", CheckSysadmins);
            _ctx.Run(Id("dangerousconfig"), _area, "Tehlikeli sunucu ayarları (xp_cmdshell vb.)", CheckDangerousConfig);
            _ctx.Run(Id("authmode"), _area, "Kimlik doğrulama modu", CheckAuthMode);
            _ctx.Run(Id("trustworthy"), _area, "TRUSTWORTHY veritabanları", CheckTrustworthy);
            _ctx.Run(Id("guestuser"), _area, "Veritabanlarında guest kullanıcısı", CheckGuestUser);
            _ctx.Run(Id("linkedservers"), _area, "Linked server'lar", CheckLinkedServers);
            _ctx.Run(Id("network"), _area, "Port ve bağlantı şifrelemesi", CheckNetwork);
            _ctx.Run(Id("backups"), _area, "Yedekler", CheckBackups);
            _ctx.Run(Id("tde"), _area, "TDE sertifika yedeği", CheckTde);
            _ctx.Run(Id("serviceaccount"), _area, "SQL Server servis hesabı", CheckServiceAccount);
        }

        // ================================================================ Sürüm / izleme

        private CheckItemDto CheckVersion()
        {
            var row = Rows(_conn, "SELECT CAST(SERVERPROPERTY('ProductVersion') AS nvarchar(64)), CAST(SERVERPROPERTY('ProductLevel') AS nvarchar(64)), CAST(SERVERPROPERTY('ProductUpdateLevel') AS nvarchar(64))", _ctx.Token)[0];
            var ends = new Dictionary<int, Tuple<string, DateTime>>
            {
                { 10, Tuple.Create("SQL Server 2008 / 2008 R2", new DateTime(2019, 7, 9)) },
                { 11, Tuple.Create("SQL Server 2012", new DateTime(2022, 7, 12)) },
                { 12, Tuple.Create("SQL Server 2014", new DateTime(2024, 7, 9)) },
                { 13, Tuple.Create("SQL Server 2016", new DateTime(2026, 7, 14)) },
                { 14, Tuple.Create("SQL Server 2017", new DateTime(2027, 10, 12)) },
                { 15, Tuple.Create("SQL Server 2019", new DateTime(2030, 1, 8)) },
                { 16, Tuple.Create("SQL Server 2022", new DateTime(2033, 1, 11)) },
            };
            string level = Str(row[1]) + (Str(row[2]) != null ? " " + Str(row[2]) : "");
            Tuple<string, DateTime> end;
            if (!ends.TryGetValue(_major, out end))
                return new CheckItemDto { Status = _major > 16 ? CheckStatus.Gecti : CheckStatus.Bilgi, Summary = "Sürüm " + Str(row[0]) + " (" + level + ")." };

            int days = (int)(end.Item2 - DateTime.Today).TotalDays;
            var status = days < 0 ? CheckStatus.Kritik : days < 365 ? CheckStatus.Uyari : CheckStatus.Gecti;
            return new CheckItemDto
            {
                Status = status,
                Summary = end.Item1 + " " + level + (days < 0 ? ": desteği " + end.Item2.ToString("dd.MM.yyyy") + " tarihinde BİTTİ, güvenlik yaması gelmiyor."
                                                            : ", destek bitişi " + end.Item2.ToString("dd.MM.yyyy") + (days < 365 ? " (" + days + " gün kaldı)." : ".")),
                Detail = "Sürüm: " + Str(row[0]) + "\r\nGüncelleme seviyesi: " + level + "\r\n\r\nGüncel kümülatif güncellemeyi (CU) kurmak bilinen açıkları kapatır.",
                HowToFix = status == CheckStatus.Gecti ? "Yine de en son kümülatif güncellemenin (CU) kurulu olduğunu kontrol edin."
                    : "Desteklenen bir sürüme yükseltmeyi planlayın. Uygulama üreticinizin hangi sürümleri desteklediğini sorun."
            };
        }

        private CheckItemDto CheckGuardAudit()
        {
            string problem = _auditState(_instance);
            if (problem == null) return new CheckItemDto { Status = CheckStatus.Gecti, Summary = "Kurulu ve çalışıyor." };
            return new CheckItemDto
            {
                Status = CheckStatus.Uyari,
                Summary = problem,
                Detail = "SecurityGuard'ın SQL izlemesi (Audit) olmadan login / yetki / DB değişiklikleri anında yakalanamaz.",
                HowToFix = "SecurityGuard → SQL Server → SQL Bağlantısı → \"SQL İzlemesini Kur\"."
            };
        }

        private CheckItemDto CheckLoginAuditing()
        {
            int level = (int)Long(Scalar(_conn, @"DECLARE @v int;
EXEC master.dbo.xp_instance_regread N'HKEY_LOCAL_MACHINE', N'Software\Microsoft\MSSQLServer\MSSQLServer', N'AuditLevel', @v OUTPUT;
SELECT ISNULL(@v, 0);", _ctx.Token));
            if (level == 2 || level == 3)
                return new CheckItemDto { Status = CheckStatus.Gecti, Summary = level == 3 ? "Başarısız ve başarılı girişler kaydediliyor." : "Başarısız girişler kaydediliyor." };
            return new CheckItemDto
            {
                Status = CheckStatus.Kritik,
                Summary = "Başarısız SQL girişleri kaydedilmiyor (" + (level == 0 ? "kapalı" : "sadece başarılılar") + ").",
                Detail = "Başarısız girişler (18456) Windows loguna yazılmazsa SecurityGuard SQL brute force saldırılarını göremez ve IP banlayamaz.",
                HowToFix = "SSMS → sunucu Properties → Security → Login auditing: \"Failed logins only\" (veya \"Both\"). SQL Server servisi yeniden başlatılınca geçerli olur.",
                Script = "EXEC master.dbo.xp_instance_regwrite N'HKEY_LOCAL_MACHINE', N'Software\\Microsoft\\MSSQLServer\\MSSQLServer', N'AuditLevel', REG_DWORD, 2;\r\n-- SQL Server servisini yeniden başlatın",
                ScriptLanguage = "sql"
            };
        }

        // ================================================================ Login'ler

        private CheckItemDto CheckSa()
        {
            var row = Rows(_conn, @"SELECT name, is_disabled, is_policy_checked, CAST(LOGINPROPERTY(name, 'PasswordLastSetTime') AS datetime)
                                    FROM sys.sql_logins WHERE sid = 0x01", _ctx.Token).FirstOrDefault();
            if (row == null) return new CheckItemDto { Status = CheckStatus.Gecti, Summary = "sa hesabı yok (yalnızca Windows kimlik doğrulaması)." };
            string name = Str(row[0]);
            bool disabled = Long(row[1]) == 1;
            var lastSet = Date(row[3]);
            if (disabled)
                return new CheckItemDto { Status = CheckStatus.Gecti, Summary = "sa" + (name != "sa" ? " (" + name + ")" : "") + " devre dışı." };

            bool old = lastSet != null && lastSet < DateTime.Now.AddYears(-1);
            return new CheckItemDto
            {
                Status = CheckStatus.Uyari,
                Summary = "sa hesabı AÇIK" + (name != "sa" ? " (adı " + name + ")" : "") + (old ? ", şifresi " + (int)((DateTime.Now - lastSet.Value).TotalDays / 30) + " aydır değişmemiş" : "") + ".",
                Detail = "İnternetteki SQL saldırıları neredeyse her zaman \"sa\" kullanıcısını dener. sa sınırsız yetkilidir; şifresi bir kez tahmin edilirse sunucunun tamamı ele geçer "
                         + "(xp_cmdshell ile Windows komutu çalıştırma dahil).\r\n\r\nŞifre son değişiklik: " + Fmt.Date(lastSet),
                HowToFix = "Uygulamalar sa ile bağlanıyorsa önce onlara ayrı, sadece kendi DB'sinde yetkili login açın (uygulama üreticisine danışın). Sonra sa'yı devre dışı bırakın. "
                           + "Kapatamıyorsanız en azından çok güçlü (16+ karakter) bir şifre verin ve adını değiştirin.",
                Script = "-- Önce sa kullanan uygulama kalmadığından emin olun!\r\nALTER LOGIN " + Q(name) + " DISABLE;\r\n-- veya adını değiştirme: ALTER LOGIN " + Q(name) + " WITH NAME = [sql_yonetim_x];",
                ScriptLanguage = "sql"
            };
        }

        private CheckItemDto CheckWeakPasswords()
        {
            // Şifreler parametre olarak gider, sorgu metnine / loga yazılmaz; sonuçta sadece login adı ve zayıflık türü döner
            var weak = new List<string>();
            using (var cmd = _conn.CreateCommand())
            {
                var values = new StringBuilder();
                for (int i = 0; i < CommonPasswords.Length; i++)
                {
                    if (i > 0) values.Append(",");
                    values.Append("(@p").Append(i).Append(")");
                    cmd.Parameters.AddWithValue("@p" + i, CommonPasswords[i]);
                }
                cmd.CommandTimeout = 120;
                cmd.CommandText = @"
SELECT l.name, l.is_disabled,
       CASE WHEN PWDCOMPARE(N'', l.password_hash) = 1 THEN 1
            WHEN PWDCOMPARE(l.name, l.password_hash) = 1 THEN 2
            WHEN EXISTS (SELECT 1 FROM (VALUES " + values + @") v(p) WHERE PWDCOMPARE(v.p, l.password_hash) = 1) THEN 3
            ELSE 0 END
FROM sys.sql_logins l WHERE l.name NOT LIKE '##%'";
                using (_ctx.Token.Register(() => { try { cmd.Cancel(); } catch { } }))
                using (var r = cmd.ExecuteReader())
                {
                    while (r.Read())
                    {
                        int kind = Convert.ToInt32(r.GetValue(2));
                        if (kind == 0) continue;
                        weak.Add(r.GetString(0) + ": " + (kind == 1 ? "BOŞ şifre" : kind == 2 ? "şifre kullanıcı adıyla aynı" : "çok yaygın bir şifre")
                                 + (r.GetBoolean(1) ? " (devre dışı)" : ""));
                    }
                }
            }
            if (weak.Count == 0) return new CheckItemDto { Status = CheckStatus.Gecti, Summary = "Boş, kullanıcı adıyla aynı veya yaygın şifreli SQL login yok." };
            bool active = weak.Any(w => !w.EndsWith("(devre dışı)"));
            return new CheckItemDto
            {
                Status = active ? CheckStatus.Kritik : CheckStatus.Uyari,
                Summary = weak.Count + " login'in şifresi zayıf: " + string.Join(", ", weak.Select(w => w.Split(':')[0]).Take(5)) + ".",
                Detail = "Bu şifreler saldırı araçlarının ilk denediği listelerde var; dakikalar içinde bulunur. (Şifreler SQL Server içinde karşılaştırıldı, hiçbir yere kaydedilmedi.)\r\n\r\n  "
                         + string.Join("\r\n  ", weak),
                HowToFix = "Bu login'lere güçlü (en az 12 karakter, harf + rakam + işaret) şifre verin ve şifre politikasını açın. Login'i kullanan uygulamaların bağlantı ayarını da güncelleyin.",
                Script = "-- Örnek (şifreyi kendiniz belirleyin, uygulama bağlantılarını da güncelleyin):\r\n"
                         + string.Join("\r\n", weak.Select(w => "-- ALTER LOGIN " + Q(w.Split(':')[0]) + " WITH PASSWORD = N'<güçlü şifre>', CHECK_POLICY = ON;")),
                ScriptLanguage = "sql"
            };
        }

        private CheckItemDto CheckPolicy()
        {
            var rows = Rows(_conn, "SELECT name FROM sys.sql_logins WHERE is_policy_checked = 0 AND is_disabled = 0 AND name NOT LIKE '##%' ORDER BY name", _ctx.Token)
                .Select(r => Str(r[0])).ToList();
            if (rows.Count == 0) return new CheckItemDto { Status = CheckStatus.Gecti, Summary = "Tüm aktif SQL login'lerinde Windows şifre politikası uygulanıyor." };
            return new CheckItemDto
            {
                Status = CheckStatus.Uyari,
                Summary = rows.Count + " aktif login'de şifre politikası kapalı: " + string.Join(", ", rows.Take(5)) + (rows.Count > 5 ? "..." : "") + ".",
                Detail = "CHECK_POLICY kapalı login'lere \"1\" gibi şifre verilebilir ve hatalı denemede kilitlenmez (sınırsız tahmin).\r\n\r\n  " + string.Join("\r\n  ", rows),
                HowToFix = "SSMS → Security → Logins → login → \"Enforce password policy\". Açmadan önce şifrenin politikaya uyduğundan emin olun.",
                Script = string.Join("\r\n", rows.Select(n => "ALTER LOGIN " + Q(n) + " WITH CHECK_POLICY = ON;")),
                ScriptLanguage = "sql"
            };
        }

        private CheckItemDto CheckSysadmins()
        {
            var rows = Rows(_conn, @"
SELECT p.name, p.type_desc, p.is_disabled, p.sid
FROM sys.server_role_members rm
JOIN sys.server_principals r ON r.principal_id = rm.role_principal_id
JOIN sys.server_principals p ON p.principal_id = rm.member_principal_id
WHERE r.name = 'sysadmin' ORDER BY p.name", _ctx.Token);
            var others = rows.Where(r =>
            {
                string n = Str(r[0]);
                var sid = r[3] as byte[];
                return !(sid != null && sid.Length == 1 && sid[0] == 1)
                       && !n.StartsWith("NT SERVICE\\", StringComparison.OrdinalIgnoreCase)
                       && !n.Equals("NT AUTHORITY\\SYSTEM", StringComparison.OrdinalIgnoreCase)
                       && !n.Equals(GuardConstants.SqlLoginName, StringComparison.OrdinalIgnoreCase)
                       && Long(r[2]) == 0;
            }).ToList();
            var all = rows.Select(r => "  " + Str(r[0]) + " (" + Str(r[1]) + (Long(r[2]) == 1 ? ", devre dışı" : "") + ")");
            if (others.Count == 0)
                return new CheckItemDto { Status = CheckStatus.Gecti, Summary = "sa, servis hesapları ve SecurityGuard dışında sysadmin yok.", Detail = string.Join("\r\n", all) };

            bool builtinAdmins = others.Any(r => Str(r[0]).Equals("BUILTIN\\Administrators", StringComparison.OrdinalIgnoreCase));
            bool sqlLogins = others.Any(r => Str(r[1]) == "SQL_LOGIN");
            return new CheckItemDto
            {
                Status = builtinAdmins || sqlLogins ? CheckStatus.Uyari : CheckStatus.Bilgi,
                Summary = others.Count + " ek sysadmin: " + string.Join(", ", others.Take(5).Select(r => Str(r[0]))) + (others.Count > 5 ? "..." : "") + ".",
                Detail = "sysadmin her şeyi yapabilir: tüm DB'leri okur / siler, Windows komutu çalıştırabilir. Uygulamaların bağlandığı login'ler sysadmin olmamalı; "
                         + "sadece kendi DB'sinde db_owner yeterlidir."
                         + (builtinAdmins ? "\r\nBUILTIN\\Administrators sysadmin: sunucudaki her Windows yöneticisi SQL'de de tam yetkili." : "")
                         + "\r\n\r\nTüm sysadmin üyeleri:\r\n" + string.Join("\r\n", all),
                HowToFix = "Uygulama üreticisine login'in sysadmin olması gerekip gerekmediğini sorun. Gerekmiyorsa sysadmin'den çıkarıp sadece kendi DB'sinde yetki verin. "
                           + "SecurityGuard'ın kendi login'i (" + GuardConstants.SqlLoginName + ") izleme için sysadmin olmak zorundadır.",
                Script = "-- Kontrol edip başındaki -- işaretini kaldırın\r\n" + string.Join("\r\n", others.Select(r => "-- ALTER SERVER ROLE [sysadmin] DROP MEMBER " + Q(Str(r[0])) + ";")),
                ScriptLanguage = "sql"
            };
        }

        // ================================================================ Ayarlar

        private CheckItemDto CheckDangerousConfig()
        {
            var cfg = Rows(_conn, "SELECT name, CAST(value_in_use AS int) FROM sys.configurations", _ctx.Token)
                .ToDictionary(r => Str(r[0]), r => (int)Long(r[1]), StringComparer.OrdinalIgnoreCase);
            Func<string, int> get = n => cfg.ContainsKey(n) ? cfg[n] : -1;

            var items = new List<Tuple<string, CheckStatus, string, int>>();
            if (get("xp_cmdshell") == 1) items.Add(Tuple.Create("xp_cmdshell", CheckStatus.Kritik, "SQL'den Windows komutu çalıştırma (fidye yazılımlarının en sevdiği yol)", 0));
            if (get("Ole Automation Procedures") == 1) items.Add(Tuple.Create("Ole Automation Procedures", CheckStatus.Kritik, "SQL'den COM nesnesi / dosya işlemi", 0));
            if (get("Ad Hoc Distributed Queries") == 1) items.Add(Tuple.Create("Ad Hoc Distributed Queries", CheckStatus.Uyari, "OPENROWSET ile dış kaynaklara bağlanma", 0));
            if (get("clr enabled") == 1) items.Add(Tuple.Create("clr enabled", CheckStatus.Uyari, ".NET kodu çalıştırma (bazı uygulamalar ister)", 0));
            if (get("clr strict security") == 0) items.Add(Tuple.Create("clr strict security", CheckStatus.Uyari, "imzasız CLR derlemelerine izin", 1));
            if (get("cross db ownership chaining") == 1) items.Add(Tuple.Create("cross db ownership chaining", CheckStatus.Uyari, "DB'ler arası yetki zinciri", 0));

            if (items.Count == 0) return new CheckItemDto { Status = CheckStatus.Gecti, Summary = "xp_cmdshell, Ole Automation ve benzeri tehlikeli ayarlar kapalı." };
            var script = new StringBuilder("-- Uygulamanız bu özelliklerden birini kullanıyorsa kapatmadan önce üreticiye danışın\r\nEXEC sys.sp_configure N'show advanced options', 1; RECONFIGURE;\r\n");
            foreach (var i in items) script.AppendLine("EXEC sys.sp_configure N'" + i.Item1 + "', " + i.Item4 + "; RECONFIGURE;");
            return new CheckItemDto
            {
                Status = items.Max(i => i.Item2),
                Summary = "Açık: " + string.Join(", ", items.Select(i => i.Item1)) + ".",
                Detail = string.Join("\r\n", items.Select(i => "  " + i.Item1 + ": " + i.Item3)) + "\r\n\r\nSecurityGuard bu ayarlar açılınca anında olay olarak da bildirir.",
                HowToFix = "Kullanılmayanları kapatın (script). Hemen geçerli olur.",
                Script = script.ToString(),
                ScriptLanguage = "sql"
            };
        }

        private CheckItemDto CheckAuthMode()
        {
            bool windowsOnly = Long(Scalar(_conn, "SELECT CAST(SERVERPROPERTY('IsIntegratedSecurityOnly') AS int)", _ctx.Token)) == 1;
            return windowsOnly
                ? new CheckItemDto { Status = CheckStatus.Gecti, Summary = "Sadece Windows kimlik doğrulaması." }
                : new CheckItemDto
                {
                    Status = CheckStatus.Bilgi,
                    Summary = "Karma mod: SQL login'leri ile de bağlanılabiliyor.",
                    Detail = "Çoğu ERP / muhasebe programı SQL login ister; bu yüzden sorun değil. Ama SQL login'leri ağdan şifre tahminine açıktır: "
                             + "güçlü şifre ve şifre politikası (bu sayfadaki diğer maddeler) önemlidir."
                };
        }

        private CheckItemDto CheckTrustworthy()
        {
            var rows = Rows(_conn, "SELECT name FROM sys.databases WHERE is_trustworthy_on = 1 AND name <> 'msdb' ORDER BY name", _ctx.Token).Select(r => Str(r[0])).ToList();
            if (rows.Count == 0) return new CheckItemDto { Status = CheckStatus.Gecti, Summary = "TRUSTWORTHY açık kullanıcı DB'si yok." };
            return new CheckItemDto
            {
                Status = CheckStatus.Uyari,
                Summary = "TRUSTWORTHY açık: " + string.Join(", ", rows) + ".",
                Detail = "TRUSTWORTHY açık bir DB'de db_owner yetkisi olan biri kendini sysadmin yapabilir (bilinen yetki yükseltme yolu).",
                HowToFix = "Uygulama gerektirmiyorsa kapatın (CLR kullanan bazı uygulamalar ister; o durumda derlemeyi sertifikayla imzalamak doğru yoldur).",
                Script = string.Join("\r\n", rows.Select(n => "ALTER DATABASE " + Q(n) + " SET TRUSTWORTHY OFF;")),
                ScriptLanguage = "sql"
            };
        }

        private CheckItemDto CheckGuestUser()
        {
            var dbs = Rows(_conn, "SELECT name FROM sys.databases WHERE database_id > 4 AND state = 0 AND HAS_DBACCESS(name) = 1 ORDER BY name", _ctx.Token).Select(r => Str(r[0])).ToList();
            var open = new List<string>();
            try
            {
                foreach (var db in dbs)
                {
                    _ctx.Token.ThrowIfCancellationRequested();
                    _conn.ChangeDatabase(db);
                    if (Long(Scalar(_conn, "SELECT COUNT(*) FROM sys.database_permissions WHERE grantee_principal_id = DATABASE_PRINCIPAL_ID('guest') AND permission_name = 'CONNECT' AND state IN ('G', 'W')", _ctx.Token)) > 0)
                        open.Add(db);
                }
            }
            finally
            {
                try { _conn.ChangeDatabase("master"); } catch { }
            }
            if (open.Count == 0) return new CheckItemDto { Status = CheckStatus.Gecti, Summary = "Hiçbir kullanıcı DB'sinde guest açık değil." };
            return new CheckItemDto
            {
                Status = CheckStatus.Uyari,
                Summary = "guest açık: " + string.Join(", ", open.Take(5)) + (open.Count > 5 ? "..." : "") + ".",
                Detail = "guest açık bir DB'ye, o DB'de kullanıcısı olmayan HER login bağlanabilir ve public rolünün yetkileriyle veri okuyabilir.\r\n\r\n  " + string.Join("\r\n  ", open),
                HowToFix = "Uygulama gerektirmiyorsa guest'in bağlanma yetkisini kaldırın (master, msdb ve tempdb'de guest açık kalmalı, onlara dokunmayın).",
                Script = string.Join("\r\n", open.Select(d => "USE " + Q(d) + "; REVOKE CONNECT FROM guest;")),
                ScriptLanguage = "sql"
            };
        }

        private CheckItemDto CheckLinkedServers()
        {
            var rows = Rows(_conn, @"
SELECT s.name, s.product, s.data_source, l.uses_self_credential, l.remote_name, l.local_principal_id
FROM sys.servers s LEFT JOIN sys.linked_logins l ON l.server_id = s.server_id
WHERE s.is_linked = 1 ORDER BY s.name", _ctx.Token);
            if (rows.Count == 0) return new CheckItemDto { Status = CheckStatus.Gecti, Summary = "Linked server yok." };
            var risky = rows.Where(r => Str(r[4]) != null && (Str(r[4]).Equals("sa", StringComparison.OrdinalIgnoreCase) || Long(r[5]) == 0)).ToList();
            var detail = string.Join("\r\n", rows.GroupBy(r => Str(r[0])).Select(g => "  " + g.Key + " → " + Str(g.First()[2]) + " (" + Str(g.First()[1]) + ")"
                + string.Concat(g.Where(r => Str(r[4]) != null).Select(r => ", " + (Long(r[5]) == 0 ? "herkes" : "eşlenen login") + " → " + Str(r[4])))));
            return new CheckItemDto
            {
                Status = risky.Count > 0 ? CheckStatus.Uyari : CheckStatus.Bilgi,
                Summary = rows.Select(r => Str(r[0])).Distinct().Count() + " linked server" + (risky.Count > 0 ? "; bazıları herkes için sabit (veya sa) hesapla bağlanıyor." : "."),
                Detail = "Linked server bu sunucudan başka sunucuya bağlanır. \"Herkes\" için sabit bir hesapla eşlenmişse bu sunucudaki her kullanıcı karşı sunucuda o hesabın yetkisiyle iş yapabilir.\r\n\r\n" + detail,
                HowToFix = risky.Count == 0 ? "Kullanılmayan linked server'ları silin." : "SSMS → Server Objects → Linked Servers → Properties → Security: "
                           + "\"Be made using this security context\" yerine login eşlemesi kullanın; sa ile bağlanmayın."
            };
        }

        private CheckItemDto CheckNetwork()
        {
            List<object[]> reg;
            try
            {
                reg = Rows(_conn, "SELECT registry_key, value_name, CAST(value_data AS nvarchar(256)) FROM sys.dm_server_registry WHERE value_name IN ('TcpPort', 'TcpDynamicPorts', 'ForceEncryption')", _ctx.Token);
            }
            catch (SqlException)
            {
                return new CheckItemDto { Status = CheckStatus.Bilgi, Summary = "Port / şifreleme ayarı bu sürümde okunamadı." };
            }
            foreach (var r in reg) r[2] = Str(r[2]) == null ? null : Str(r[2]).Trim('\0', ' ');
            string port = reg.Where(r => Str(r[0]).EndsWith("IPAll", StringComparison.OrdinalIgnoreCase) && Str(r[1]) == "TcpPort").Select(r => Str(r[2])).FirstOrDefault(v => !string.IsNullOrEmpty(v));
            string dynamicPort = reg.Where(r => Str(r[0]).EndsWith("IPAll", StringComparison.OrdinalIgnoreCase) && Str(r[1]) == "TcpDynamicPorts").Select(r => Str(r[2])).FirstOrDefault(v => !string.IsNullOrEmpty(v));
            bool force = reg.Any(r => Str(r[1]) == "ForceEncryption" && Str(r[2]) == "1");
            string portText = port ?? (dynamicPort != null ? "dinamik (" + dynamicPort + ")" : "?");

            bool defaultPort = port == "1433";
            return new CheckItemDto
            {
                Status = defaultPort || !force ? CheckStatus.Bilgi : CheckStatus.Gecti,
                Summary = "Port " + portText + (defaultPort ? " (varsayılan)" : "") + ", şifreleme " + (force ? "zorunlu" : "zorunlu değil") + ".",
                Detail = "Varsayılan 1433 portu internet taramalarının ilk hedefidir; SQL portu internete açıksa (Windows → Herkese açık portlar) port değiştirmek deneme sayısını azaltır, "
                         + "en doğrusu SQL'i internete hiç açmamaktır.\r\nŞifreleme zorunlu değilse ağı dinleyen biri sorguları ve verileri görebilir (iç ağda genelde kabul edilebilir).",
                HowToFix = "SQL Server Configuration Manager → SQL Server Network Configuration → Protocols → TCP/IP: IPAll → TCP Port (değiştirirseniz uygulama bağlantılarını ve firewall'u güncelleyin). "
                           + "Şifreleme için: aynı yerde Flags → Force Encryption = Yes (sertifika gerekebilir). İkisi de SQL servisi yeniden başlatılınca geçerli olur."
            };
        }

        // ================================================================ Yedek / TDE / servis

        private CheckItemDto CheckBackups()
        {
            var rows = Rows(_conn, @"
SELECT d.name,
       (SELECT MAX(backup_finish_date) FROM msdb.dbo.backupset b WHERE b.database_name = d.name AND b.type = 'D'),
       (SELECT TOP 1 CASE WHEN b.name = N'SecurityGuard' AND b.description IS NOT NULL THEN b.description ELSE mf.physical_device_name END
        FROM msdb.dbo.backupset b JOIN msdb.dbo.backupmediafamily mf ON mf.media_set_id = b.media_set_id
        WHERE b.database_name = d.name AND b.type = 'D' ORDER BY b.backup_finish_date DESC),
       (SELECT TOP 1 physical_name FROM sys.master_files f WHERE f.database_id = d.database_id AND f.type = 0),
       (SELECT TOP 1 mf.device_type FROM msdb.dbo.backupset b JOIN msdb.dbo.backupmediafamily mf ON mf.media_set_id = b.media_set_id
        WHERE b.database_name = d.name AND b.type = 'D' ORDER BY b.backup_finish_date DESC)
FROM sys.databases d WHERE d.name <> 'tempdb' AND d.state = 0 AND d.source_database_id IS NULL ORDER BY d.name", _ctx.Token);

            var never = new List<string>();
            var old = new List<string>();
            var sameDisk = new List<string>();
            var detail = new StringBuilder("DB | Son tam yedek | Yer\r\n");
            foreach (var r in rows)
            {
                string db = Str(r[0]), device = Str(r[2]), data = Str(r[3]);
                var last = Date(r[1]);
                bool system = db == "master" || db == "msdb" || db == "model";
                int limit = system ? 30 : 7;
                detail.AppendLine(db + " | " + (last == null ? "HİÇ" : Fmt.Date(last) + " (" + Fmt.Ago(last) + ")") + " | " + (device ?? "-"));
                if (last == null) { if (!system || db != "model") never.Add(db); }
                else if (last < DateTime.Now.AddDays(-limit)) old.Add(db + " (" + Fmt.Ago(last) + ")");
                if (device != null && data != null && Long(r[4]) == 2 && device.Length > 2 && data.Length > 2 && device[1] == ':' && char.ToUpperInvariant(device[0]) == char.ToUpperInvariant(data[0]))
                    sameDisk.Add(db + " (" + device.Substring(0, 2) + ")");
            }

            var userNever = never.Where(n => n != "master" && n != "msdb").ToList();
            var status = userNever.Count > 0 || old.Any(o => !o.StartsWith("master") && !o.StartsWith("msdb")) ? CheckStatus.Kritik
                       : never.Count > 0 || old.Count > 0 || sameDisk.Count > 0 ? CheckStatus.Uyari : CheckStatus.Gecti;
            if (status == CheckStatus.Gecti)
                return new CheckItemDto { Status = status, Summary = "Tüm DB'lerin son 7 gün içinde (sistem DB'leri 30 gün) tam yedeği var.", Detail = detail.ToString() };

            var parts = new List<string>();
            if (never.Count > 0) parts.Add("hiç yedeği olmayan: " + string.Join(", ", never.Take(5)));
            if (old.Count > 0) parts.Add("yedeği eski: " + string.Join(", ", old.Take(5)));
            if (sameDisk.Count > 0) parts.Add("yedek veriyle aynı diskte: " + string.Join(", ", sameDisk.Take(5)));
            return new CheckItemDto
            {
                Status = status,
                Summary = string.Join("; ", parts) + ".",
                Detail = "Fidye yazılımı, disk arızası veya yanlışlıkla silmede tek kurtuluş güncel ve AYRI bir yerde duran yedektir. Veriyle aynı diskteki yedek, disk bozulunca veriyle birlikte gider; "
                         + "sunucudaki yedek, fidye yazılımı sunucuyu şifrelerse onunla birlikte şifrelenir.\r\n"
                         + "Not: SQL Server'ın kaydı (msdb) baz alındı; 3. parti yazılımın SQL dışında aldığı yedekler burada görünmeyebilir.\r\n\r\n" + detail,
                HowToFix = "Düzenli tam yedek planlayın (SecurityGuard → SQL Server → Yedekleme veya SQL Agent bakım planı). Yedeklerin bir kopyası başka bir diske / NAS'a / buluta gitmeli. "
                           + "Ara sıra yedekten geri yüklemeyi deneyin."
            };
        }

        private CheckItemDto CheckTde()
        {
            var rows = Rows(_conn, @"
SELECT DB_NAME(k.database_id), c.name, c.pvt_key_last_backup_date
FROM sys.dm_database_encryption_keys k JOIN master.sys.certificates c ON c.thumbprint = k.encryptor_thumbprint
WHERE k.database_id > 4", _ctx.Token);
            if (rows.Count == 0) return new CheckItemDto { Status = CheckStatus.Gecti, Summary = "TDE (veritabanı şifrelemesi) kullanan DB yok." };
            var missing = rows.Where(r => r[2] == null).ToList();
            if (missing.Count == 0) return new CheckItemDto { Status = CheckStatus.Gecti, Summary = rows.Count + " TDE'li DB, sertifika yedekleri alınmış." };
            return new CheckItemDto
            {
                Status = CheckStatus.Kritik,
                Summary = "Sertifika yedeği olmayan TDE'li DB: " + string.Join(", ", missing.Select(r => Str(r[0]) + " (" + Str(r[1]) + ")")) + ".",
                Detail = "TDE ile şifrelenmiş DB'nin yedeği, şifreleme sertifikası olmadan BAŞKA BİR SUNUCUDA AÇILAMAZ. Sunucu kaybolursa yedekler işe yaramaz. "
                         + "Saldırganların bir yöntemi de DB'yi kendi sertifikalarıyla TDE ile şifreleyip fidye istemektir (SecurityGuard TDE açılmasını olay olarak bildirir).",
                HowToFix = "Sertifikayı özel anahtarıyla birlikte yedekleyin ve sunucu dışında güvenli bir yerde saklayın (şifresini de not edin).",
                Script = string.Join("\r\n", missing.Select(r => "BACKUP CERTIFICATE " + Q(Str(r[1])) + " TO FILE = N'C:\\Yedek\\" + Str(r[1]) + ".cer'\r\n"
                                                                + "    WITH PRIVATE KEY (FILE = N'C:\\Yedek\\" + Str(r[1]) + ".pvk', ENCRYPTION BY PASSWORD = N'<güçlü şifre>');")),
                ScriptLanguage = "sql"
            };
        }

        private CheckItemDto CheckServiceAccount()
        {
            List<object[]> rows;
            try
            {
                rows = Rows(_conn, "SELECT servicename, service_account FROM sys.dm_server_services", _ctx.Token);
            }
            catch (SqlException)
            {
                return new CheckItemDto { Status = CheckStatus.Bilgi, Summary = "Servis hesabı bu sürümde okunamadı." };
            }
            var local = rows.Where(r => (Str(r[1]) ?? "").Equals("LocalSystem", StringComparison.OrdinalIgnoreCase)).ToList();
            string detail = string.Join("\r\n", rows.Select(r => "  " + Str(r[0]) + ": " + Str(r[1])));
            if (local.Count == 0) return new CheckItemDto { Status = CheckStatus.Gecti, Summary = "SQL servisleri sanal / ayrı hesapla çalışıyor.", Detail = detail };
            return new CheckItemDto
            {
                Status = CheckStatus.Uyari,
                Summary = string.Join(", ", local.Select(r => Str(r[0]))) + " LocalSystem ile çalışıyor.",
                Detail = "LocalSystem Windows'ta en yüksek yetkidir. SQL Server ele geçirilirse (ör. xp_cmdshell) saldırgan Windows'ta da her şeyi yapar. "
                         + "Varsayılan sanal hesap (NT SERVICE\\MSSQLSERVER) çok daha kısıtlıdır.\r\n\r\n" + detail,
                HowToFix = "SQL Server Configuration Manager → SQL Server Services → servis → Properties → Log On → \"NT Service\\MSSQLSERVER\" (Configuration Manager dosya izinlerini kendisi ayarlar; "
                           + "services.msc'den değiştirmeyin). Servis yeniden başlar; mesai dışında yapın."
            };
        }
    }
}
