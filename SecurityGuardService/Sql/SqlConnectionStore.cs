using System;
using System.Collections.Generic;
using System.Data.SqlClient;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Tasks;
using SecurityGuard.Shared;
using SecurityGuard.Shared.Contracts;
using SecurityGuard.Shared.Dto;
using SecurityGuardService.Core;
using SecurityGuardService.Core.Data;
using SecurityGuardService.Core.Logging;
using SecurityGuardService.Core.Security;

namespace SecurityGuardService.Sql
{
    /// <summary>
    /// SQL Server bağlantı bilgileri (instance başına). Sunucu, port, login ve şifrenin tamamı
    /// DPAPI ile şifreli saklanır (SqlInstances.ConnectionEnc). Şifre UI'ye hiç dönmez.
    ///
    /// Kural (mail ile aynı): aynı bilgilerle son 30 dakikada başarılı bağlantı testi yoksa kaydedilemez.
    /// Test sadece okur: sürüm, edition ve login'in sysadmin olup olmadığına bakar.
    /// </summary>
    internal sealed class SqlConnectionStore
    {
        private const string Module = "SQL";
        private const string Purpose = "sql.connection:";
        private const int MinSupportedMajor = 11; // SQL Server 2012
        private static readonly TimeSpan TestValidity = TimeSpan.FromMinutes(30);

        private readonly GuardDatabase _db;
        private readonly object _testLock = new object();
        private string _lastTestFingerprint;
        private DateTime _lastTestUtc;
        private SqlTestResultDto _lastTestResult;

        public SqlConnectionStore(GuardDatabase db)
        {
            _db = db;
        }

        /// <summary>Kayıtlı bağlantı değiştiğinde (SQL izleme modülleri bağlantıyı yenilesin diye).</summary>
        public event Action Changed;

        // ================================================================ Listeleme

        /// <summary>Sunucuda bulunan ve kayıtlı instance'ların birleşimi.</summary>
        public List<SqlInstanceDto> List()
        {
            var detected = SqlDiscovery.Detect();
            var saved = ReadSaved();
            var result = new List<SqlInstanceDto>();

            foreach (var d in detected)
            {
                SavedInstance s;
                saved.TryGetValue(d.Name, out s);
                result.Add(ToDto(d.Name, d, s));
            }
            foreach (var s in saved.Values.Where(s => !detected.Any(d => string.Equals(d.Name, s.InstanceName, StringComparison.OrdinalIgnoreCase))))
                result.Add(ToDto(s.InstanceName, null, s));

            return result;
        }

        private static SqlInstanceDto ToDto(string name, DetectedInstance d, SavedInstance s)
        {
            return new SqlInstanceDto
            {
                InstanceName = name,
                Detected = d != null,
                ServiceState = d != null ? d.ServiceState : "Sunucuda bulunamadı",
                ProductVersion = d != null && d.Version != null ? d.Version : (s != null ? s.ProductVersion : null),
                Edition = d != null && d.Edition != null ? d.Edition : (s != null ? s.Edition : null),
                TcpPort = d != null ? d.TcpPort : 0,
                IsConfigured = s != null && s.Connection != null,
                IsEnabled = s != null && s.IsEnabled,
                Server = s != null && s.Connection != null ? s.Connection.Server : null,
                Port = s != null && s.Connection != null ? s.Connection.Port : 0,
                Login = s != null && s.Connection != null ? s.Connection.Login : null,
                HasPassword = s != null && s.Connection != null && !string.IsNullOrEmpty(s.Connection.Password),
                SuggestedServer = SqlDiscovery.SuggestServer(name)
            };
        }

        // ================================================================ Test

        public SqlTestResultDto Test(SqlConnectionRequest request, string userName)
        {
            var cfg = Normalize(request);
            SqlTestResultDto result;
            try
            {
                result = Probe(cfg);
            }
            catch (SqlException ex)
            {
                AppLog.Warn(Module, "Bağlantı testi başarısız (" + cfg.InstanceName + ", " + cfg.Server + "): SQL " + ex.Number + " " + ex.Message);
                throw new GuardException(ErrorCodes.SqlFailed, Explain(ex));
            }
            catch (Exception ex)
            {
                AppLog.Warn(Module, "Bağlantı testi başarısız (" + cfg.InstanceName + ", " + cfg.Server + ").", ex);
                throw new GuardException(ErrorCodes.SqlFailed, "SQL Server'a bağlanılamadı: " + ex.Message);
            }

            if (result.MajorVersion < MinSupportedMajor)
                throw new GuardException(ErrorCodes.SqlFailed, "Bağlantı kuruldu ama " + result.VersionName + " desteklenmiyor. SecurityGuard SQL Server 2012 ve üstünü destekler.");
            if (!result.IsSysadmin)
            {
                AppLog.Warn(Module, "Bağlantı testi reddedildi (" + cfg.InstanceName + "): " + cfg.Login + " sysadmin değil.");
                throw new GuardException(ErrorCodes.SqlFailed, "Bağlantı kuruldu ama " + cfg.Login + " tam yetkili (sysadmin) değil. Login'e sysadmin rolü verin (scriptteki ALTER SERVER ROLE satırı), sonra tekrar test edin.");
            }

            lock (_testLock)
            {
                _lastTestFingerprint = Fingerprint(cfg);
                _lastTestUtc = DateTime.UtcNow;
                _lastTestResult = result;
            }

            AppLog.Info(Module, "Bağlantı testi başarılı: " + cfg.InstanceName + " → " + result.VersionName + " " + result.Edition);
            AuditTrail.Record(_db, userName, "SQL bağlantısı test edildi", cfg.InstanceName + " (" + cfg.Server + ", " + cfg.Login + ")");
            return result;
        }

        private static SqlTestResultDto Probe(SqlConnectionRequest cfg)
        {
            using (var conn = new SqlConnection(BuildConnectionString(cfg, pooling: false)))
            {
                conn.Open();
                using (var cmd = conn.CreateCommand())
                {
                    cmd.CommandTimeout = 10;
                    cmd.CommandText =
                        @"SELECT CAST(SERVERPROPERTY('ServerName') AS nvarchar(256)),
                                 CAST(SERVERPROPERTY('ProductVersion') AS nvarchar(64)),
                                 CAST(SERVERPROPERTY('ProductLevel') AS nvarchar(64)),
                                 CAST(SERVERPROPERTY('Edition') AS nvarchar(256)),
                                 IS_SRVROLEMEMBER('sysadmin')";
                    using (var r = cmd.ExecuteReader())
                    {
                        r.Read();
                        string version = r.IsDBNull(1) ? "0" : r.GetString(1);
                        int major;
                        int.TryParse(version.Split('.')[0], out major);

                        var result = new SqlTestResultDto
                        {
                            ServerName = r.IsDBNull(0) ? null : r.GetString(0),
                            ProductVersion = version,
                            ProductLevel = r.IsDBNull(2) ? null : r.GetString(2),
                            Edition = r.IsDBNull(3) ? null : r.GetString(3),
                            MajorVersion = major,
                            VersionName = VersionName(major),
                            IsSysadmin = !r.IsDBNull(4) && Convert.ToInt32(r.GetValue(4)) == 1,
                            Warnings = new List<string>()
                        };
                        return result;
                    }
                }
            }
        }

        // ================================================================ Kayıt

        public async Task SaveAsync(SqlConnectionRequest request, string userName)
        {
            var cfg = Normalize(request);
            SqlTestResultDto tested;
            lock (_testLock)
            {
                if (_lastTestFingerprint == null || _lastTestFingerprint != Fingerprint(cfg) || DateTime.UtcNow - _lastTestUtc > TestValidity)
                    throw new GuardException(ErrorCodes.Validation, "Önce bu bilgilerle bağlantı testi yapın. Test başarılı olmadan SQL bağlantısı kaydedilemez.");
                tested = _lastTestResult;
                _lastTestFingerprint = null; // bir test = bir kayıt
            }

            string enc = SecretProtector.Protect(PipeProtocol.ToJson(cfg), Purpose + cfg.InstanceName.ToUpperInvariant());
            string now = DbTime.Now();
            await _db.Writer.EnqueueAsync(c => c.Execute(
                @"INSERT INTO SqlInstances (InstanceName, ProductVersion, Edition, Port, ConnectionEnc, IsEnabled, DetectedUtc, LastSeenUtc)
                  VALUES (@n, @v, @e, @p, @c, @en, @t, @t)
                  ON CONFLICT(InstanceName) DO UPDATE SET ProductVersion = excluded.ProductVersion, Edition = excluded.Edition,
                      Port = excluded.Port, ConnectionEnc = excluded.ConnectionEnc, IsEnabled = excluded.IsEnabled, LastSeenUtc = excluded.LastSeenUtc",
                ("@n", cfg.InstanceName), ("@v", tested.ProductVersion), ("@e", tested.Edition), ("@p", cfg.Port),
                ("@c", enc), ("@en", cfg.IsEnabled ? 1 : 0), ("@t", now))).ConfigureAwait(false);

            AppLog.Info(Module, "Bağlantı kaydedildi: " + cfg.InstanceName + " (" + cfg.Server + ", " + cfg.Login + ", izleme " + (cfg.IsEnabled ? "açık" : "kapalı") + ")");
            AuditTrail.Record(_db, userName, "SQL bağlantısı kaydedildi",
                cfg.InstanceName + " (" + cfg.Server + (cfg.Port > 0 ? ":" + cfg.Port : "") + ", " + cfg.Login + ", izleme " + (cfg.IsEnabled ? "açık" : "kapalı") + ")");

            var handler = Changed;
            if (handler != null)
            {
                try { handler(); }
                catch (Exception ex) { AppLog.Error(Module, "Bağlantı değişikliği bildirimi işlenemedi.", ex); }
            }
        }

        /// <summary>İzleme modülleri için bağlantı cümlesi (kayıtlı ve izleme açıksa); yoksa null.</summary>
        public string GetConnectionString(string instanceName)
        {
            SavedInstance s;
            if (!ReadSaved().TryGetValue(instanceName, out s) || s.Connection == null || !s.IsEnabled) return null;
            return BuildConnectionString(s.Connection, pooling: true);
        }

        // ================================================================ Yardımcılar

        /// <summary>Doğrular, boşlukları temizler; şifre boşsa kayıtlı şifreyi kullanır.</summary>
        private SqlConnectionRequest Normalize(SqlConnectionRequest r)
        {
            if (r == null) throw new GuardException(ErrorCodes.InvalidRequest, "Bağlantı bilgisi eksik.");
            var cfg = new SqlConnectionRequest
            {
                InstanceName = (r.InstanceName ?? "").Trim(),
                Server = (r.Server ?? "").Trim(),
                Port = r.Port,
                Login = GuardConstants.SqlLoginName, // sabit: istemci ne gönderirse göndersin bu login kullanılır
                Password = r.Password,
                IsEnabled = r.IsEnabled
            };

            if (cfg.InstanceName.Length == 0 || cfg.InstanceName.Length > 128)
                throw new GuardException(ErrorCodes.Validation, "Instance seçin.");
            if (cfg.Server.Length == 0 || cfg.Server.Length > 200)
                throw new GuardException(ErrorCodes.Validation, "Sunucu adını girin (ör. localhost veya localhost\\INSTANCE).");
            if (cfg.Port < 0 || cfg.Port > 65535)
                throw new GuardException(ErrorCodes.Validation, "Port 0-65535 arasında olmalı (0 = otomatik).");
            if (string.IsNullOrEmpty(cfg.Password))
            {
                SavedInstance s;
                if (ReadSaved().TryGetValue(cfg.InstanceName, out s) && s.Connection != null)
                    cfg.Password = s.Connection.Password;
                if (string.IsNullOrEmpty(cfg.Password))
                    throw new GuardException(ErrorCodes.Validation, "SQL login şifresini girin.");
            }
            return cfg;
        }

        private static string BuildConnectionString(SqlConnectionRequest cfg, bool pooling)
        {
            // Port verilmişse instance adı yerine doğrudan port kullanılır (SQL Browser gerekmez)
            string host = cfg.Server.Split('\\')[0];
            var b = new SqlConnectionStringBuilder
            {
                DataSource = cfg.Port > 0 ? host + "," + cfg.Port : cfg.Server,
                InitialCatalog = "master",
                UserID = cfg.Login,
                Password = cfg.Password,
                ConnectTimeout = 10,
                ApplicationName = "SecurityGuard",
                Pooling = pooling,
                TrustServerCertificate = true
            };
            return b.ConnectionString;
        }

        private Dictionary<string, SavedInstance> ReadSaved()
        {
            var rows = _db.Read(c =>
            {
                var list = new List<SavedInstance>();
                using (var cmd = c.Create("SELECT InstanceName, ProductVersion, Edition, ConnectionEnc, IsEnabled FROM SqlInstances"))
                using (var r = cmd.ExecuteReader())
                {
                    while (r.Read())
                        list.Add(new SavedInstance
                        {
                            InstanceName = r.GetString(0),
                            ProductVersion = r.IsDBNull(1) ? null : r.GetString(1),
                            Edition = r.IsDBNull(2) ? null : r.GetString(2),
                            ConnectionEnc = r.IsDBNull(3) ? null : r.GetString(3),
                            IsEnabled = r.GetInt64(4) != 0
                        });
                }
                return list;
            });

            var dict = new Dictionary<string, SavedInstance>(StringComparer.OrdinalIgnoreCase);
            foreach (var s in rows)
            {
                if (s.ConnectionEnc != null)
                {
                    try
                    {
                        s.Connection = PipeProtocol.FromJson<SqlConnectionRequest>(
                            SecretProtector.Unprotect(s.ConnectionEnc, Purpose + s.InstanceName.ToUpperInvariant()));
                    }
                    catch (Exception ex)
                    {
                        AppLog.Error(Module, "Kayıtlı bağlantı bilgisi çözülemedi: " + s.InstanceName, ex);
                    }
                }
                dict[s.InstanceName] = s;
            }
            return dict;
        }

        /// <summary>Bağlantıyı etkileyen alanların özeti (izleme açık/kapalı hariç). Şifre açık halde tutulmaz.</summary>
        private static string Fingerprint(SqlConnectionRequest cfg)
        {
            string raw = string.Join("\u0001", cfg.InstanceName.ToUpperInvariant(), cfg.Server.ToLowerInvariant(), cfg.Port.ToString(), cfg.Login, cfg.Password);
            using (var sha = SHA256.Create())
                return Convert.ToBase64String(sha.ComputeHash(Encoding.UTF8.GetBytes(raw)));
        }

        private static string VersionName(int major)
        {
            switch (major)
            {
                case 11: return "SQL Server 2012";
                case 12: return "SQL Server 2014";
                case 13: return "SQL Server 2016";
                case 14: return "SQL Server 2017";
                case 15: return "SQL Server 2019";
                case 16: return "SQL Server 2022";
                case 17: return "SQL Server 2025";
                default: return major < 11 ? "SQL Server (2012 öncesi, sürüm " + major + ")" : "SQL Server (sürüm " + major + ")";
            }
        }

        /// <summary>SQL hatasını kullanıcının anlayacağı dile çevirir.</summary>
        private static string Explain(SqlException ex)
        {
            switch (ex.Number)
            {
                case 18456:
                    return "Login başarısız: kullanıcı adı / şifre hatalı, login devre dışı ya da SQL Server sadece Windows kimlik doğrulamasına izin veriyor (karma mod kapalı).";
                case 18452:
                    return "SQL Server sadece Windows kimlik doğrulamasına izin veriyor. Sunucu özelliklerinde \"SQL Server and Windows Authentication mode\" seçilip SQL servisi yeniden başlatılmalı.";
                case 18470:
                    return "Login devre dışı (disabled). SSMS'ten login'i etkinleştirin.";
                case 18487:
                case 18488:
                    return "Login şifresinin süresi dolmuş veya değiştirilmesi gerekiyor. SSMS'ten şifreyi yenileyin.";
                case -2:
                    return "SQL Server zamanında cevap vermedi (zaman aşımı).";
                case -1:
                case 2:
                case 26:
                case 40:
                case 53:
                    return "SQL Server'a bağlanılamadı. Sunucu / instance adı veya port yanlış, SQL Server servisi durmuş ya da TCP/IP protokolü kapalı olabilir.";
                default:
                    return "SQL Server hatası (" + ex.Number + "): " + ex.Message;
            }
        }

        private sealed class SavedInstance
        {
            public string InstanceName;
            public string ProductVersion;
            public string Edition;
            public string ConnectionEnc;
            public bool IsEnabled;
            public SqlConnectionRequest Connection;
        }
    }
}
