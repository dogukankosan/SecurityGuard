using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Data.SqlClient;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using SecurityGuard.Shared;
using SecurityGuard.Shared.Contracts;
using SecurityGuardService.Core;
using SecurityGuardService.Core.Data;
using SecurityGuardService.Core.Logging;
using SecurityGuardService.Core.Settings;
using SecurityGuardService.Engine.Events;

namespace SecurityGuardService.Sql
{
    /// <summary>SQL'e bağlanabildik mi; bağlanamadıysak sebep hesap mı sunucu mu?</summary>
    internal enum SqlReachability
    {
        Ok,                 // bağlanıldı
        AccountRejected,    // login reddedildi (silinmiş / devre dışı / şifre değişmiş)
        Unreachable         // sunucuya ulaşılamadı (servis kapalı / ağ / port)
    }

    /// <summary>SQL izleme (Audit) durumu.</summary>
    internal sealed class SqlAuditState
    {
        public bool Installed;      // kullanıcı kurdu (bizim kaydımız)
        public bool Exists;         // SQL'de audit nesnesi var
        public bool Running;        // audit + spec açık ve çalışıyor
        public string Message;      // UI'de gösterilecek durum metni
        public DateTime CheckedUtc;
        public SqlReachability Reach;  // bağlanılabildi mi (bağlantı alarmı için)
    }

    /// <summary>
    /// SecurityGuard'ın SQL Server Audit nesnesi (<see cref="SqlAuditEvents.AuditName"/>).
    /// - Kurma / kaldırma SADECE kullanıcı arayüzden isteyince yapılır (sunucuda kendiliğinden değişiklik yok).
    /// - Kurulduktan sonra durdurulur / silinirse Kritik olay; "Audit durdurulursa yeniden başlat" ayarı açıksa
    ///   kendi nesnemiz yeniden başlatılır (dokümandaki tek istisna). Başka hiçbir audit / ayar değiştirilmez.
    /// </summary>
    internal sealed class SqlAuditManager
    {
        private const string Module = "SQLAudit";
        private const string InstalledKeyPrefix = "sql.audit.installed:";

        internal static readonly string[] Groups =
        {
            "SERVER_PRINCIPAL_CHANGE_GROUP", "LOGIN_CHANGE_PASSWORD_GROUP", "SERVER_ROLE_MEMBER_CHANGE_GROUP",
            "SERVER_PERMISSION_CHANGE_GROUP", "DATABASE_CHANGE_GROUP", "SERVER_OBJECT_CHANGE_GROUP", "DATABASE_OBJECT_CHANGE_GROUP",
            "SERVER_STATE_CHANGE_GROUP", "AUDIT_CHANGE_GROUP", "SERVER_OPERATION_GROUP"
        };

        private readonly GuardDatabase _db;
        private readonly SettingsStore _settings;
        private readonly SqlConnectionStore _connections;
        private readonly EventRecorder _events;
        private readonly ConcurrentDictionary<string, SqlAuditState> _states = new ConcurrentDictionary<string, SqlAuditState>(StringComparer.OrdinalIgnoreCase);
        private readonly ConcurrentDictionary<string, ConnFail> _connFails = new ConcurrentDictionary<string, ConnFail>(StringComparer.OrdinalIgnoreCase);
        private readonly SemaphoreSlim _gate = new SemaphoreSlim(1, 1);
        private long _auditEventTicks;

        /// <summary>Sunucu erişilemiyor (servis kapalı / ağ): yeniden başlatma yanlış alarmı olmasın diye bu kadar ard arda başarısızlıktan sonra alarm. Hesap reddinde beklenmez.</summary>
        private const int ServerDownGraceChecks = 2;

        private sealed class ConnFail
        {
            public int Consecutive;
            public bool Alarmed;
        }

        /// <summary>Bu süre içinde audit kaydından "audit'e dokunuldu" olayı geldiyse nöbetçi ayrıca olay üretmez.</summary>
        private static readonly TimeSpan DuplicateWindow = TimeSpan.FromMinutes(2);

        public SqlAuditManager(GuardDatabase db, SettingsStore settings, SqlConnectionStore connections, EventRecorder events)
        {
            _db = db;
            _settings = settings;
            _connections = connections;
            _events = events;
        }

        /// <summary>Kurulum scripti (UI'de gösterilir / kopyalanır; servis de aynısını çalıştırır). Tekrar çalıştırılabilir.</summary>
        public static string InstallScript()
        {
            string a = SqlAuditEvents.AuditName, s = SqlAuditEvents.SpecName;
            return
"-- SecurityGuard SQL izlemesi (SQL Server Audit → Windows Application log). Tekrar çalıştırılabilir.\r\n" +
"USE [master];\r\n" +
"IF EXISTS (SELECT 1 FROM sys.server_audit_specifications WHERE name = N'" + s + "')\r\n" +
"BEGIN\r\n" +
"    ALTER SERVER AUDIT SPECIFICATION [" + s + "] WITH (STATE = OFF);\r\n" +
"    DROP SERVER AUDIT SPECIFICATION [" + s + "];\r\n" +
"END\r\n" +
"IF NOT EXISTS (SELECT 1 FROM sys.server_audits WHERE name = N'" + a + "')\r\n" +
"    CREATE SERVER AUDIT [" + a + "] TO APPLICATION_LOG WITH (QUEUE_DELAY = 1000, ON_FAILURE = CONTINUE);\r\n" +
"CREATE SERVER AUDIT SPECIFICATION [" + s + "] FOR SERVER AUDIT [" + a + "]\r\n    " +
string.Join(",\r\n    ", Groups.Select(g => "ADD (" + g + ")")) + "\r\n" +
"    WITH (STATE = ON);\r\n" +
"ALTER SERVER AUDIT [" + a + "] WITH (STATE = ON);\r\n";
        }

        public static string UninstallScript()
        {
            string a = SqlAuditEvents.AuditName, s = SqlAuditEvents.SpecName;
            return
"USE [master];\r\n" +
"IF EXISTS (SELECT 1 FROM sys.server_audit_specifications WHERE name = N'" + s + "')\r\n" +
"BEGIN\r\n" +
"    ALTER SERVER AUDIT SPECIFICATION [" + s + "] WITH (STATE = OFF);\r\n" +
"    DROP SERVER AUDIT SPECIFICATION [" + s + "];\r\n" +
"END\r\n" +
"IF EXISTS (SELECT 1 FROM sys.server_audits WHERE name = N'" + a + "')\r\n" +
"BEGIN\r\n" +
"    ALTER SERVER AUDIT [" + a + "] WITH (STATE = OFF);\r\n" +
"    DROP SERVER AUDIT [" + a + "];\r\n" +
"END\r\n";
        }

        public bool IsInstalled(string instance)
        {
            return _settings.GetBool(InstalledKeyPrefix + instance.ToUpperInvariant(), false);
        }

        /// <summary>Son bilinen durum (UI listesi için; SQL'e gitmez).</summary>
        public SqlAuditState GetState(string instance)
        {
            SqlAuditState s;
            if (_states.TryGetValue(instance, out s)) return s;
            return new SqlAuditState { Installed = IsInstalled(instance), Message = IsInstalled(instance) ? "Kontrol bekleniyor" : "Kurulu değil" };
        }

        // ================================================================ Kullanıcı işlemleri

        public async Task InstallAsync(string instance, string userName)
        {
            string cs = RequireConnection(instance);
            await _gate.WaitAsync().ConfigureAwait(false);
            try
            {
                Execute(cs, InstallScript());
                await _settings.SetAsync(InstalledKeyPrefix + instance.ToUpperInvariant(), true).ConfigureAwait(false);
                AuditTrail.Record(_db, userName, "SQL izleme kuruldu", instance + " (" + SqlAuditEvents.AuditName + ")");
                AppLog.Info(Module, instance + ": SQL izleme kuruldu (" + userName + ").");
                Refresh(instance, cs);
            }
            finally
            {
                _gate.Release();
            }
        }

        public async Task UninstallAsync(string instance, string userName)
        {
            string cs = RequireConnection(instance);
            await _gate.WaitAsync().ConfigureAwait(false);
            try
            {
                // Önce "kurulu değil" işaretlenir: nöbetçi kaldırmayı saldırı sanıp yeniden kurmasın
                await _settings.SetAsync(InstalledKeyPrefix + instance.ToUpperInvariant(), false).ConfigureAwait(false);
                Execute(cs, UninstallScript());
                AuditTrail.Record(_db, userName, "SQL izleme kaldırıldı", instance);
                AppLog.Warn(Module, instance + ": SQL izleme kaldırıldı (" + userName + ").");
                Refresh(instance, cs);
            }
            finally
            {
                _gate.Release();
            }
        }

        // ================================================================ Nöbet

        /// <summary>Audit kaydından "audit nesnemize dokunuldu" olayı üretildi (kimin yaptığı bilgisiyle).</summary>
        public void AuditEventSeen()
        {
            Interlocked.Exchange(ref _auditEventTicks, DateTime.UtcNow.Ticks);
        }

        /// <summary>
        /// Kurulu instance'larda audit çalışıyor mu bakar. Durmuşsa / silinmişse (bir kez) Kritik olay; ayar açıksa yeniden başlatır.
        /// </summary>
        public async Task CheckAsync(string instance)
        {
            string cs = _connections.GetConnectionString(instance);
            if (cs == null) return;
            await _gate.WaitAsync().ConfigureAwait(false);
            try
            {
                SqlAuditState before;
                _states.TryGetValue(instance, out before);
                var now = Refresh(instance, cs);

                // Bağlantı durumu alarmı: hesap reddi (silme/devre dışı/şifre) hemen, sunucu erişilemiyorsa (servis kapalı / reboot) birkaç tur sonra.
                HandleReachability(instance, now);

                // Bağlanamadıysak audit durumu zaten okunamadı (UI'de "Bağlanılamadı" görünür); audit-durdu alarmı üretme.
                if (!now.Installed || now.Reach != SqlReachability.Ok) return;
                if (now.Running) return;

                bool wasRunning = before == null || before.Running;
                bool alreadyReported = DateTime.UtcNow.Ticks - Interlocked.Read(ref _auditEventTicks) < DuplicateWindow.Ticks;
                if (wasRunning && !alreadyReported)
                {
                    _events.Record(new GuardEvent("Sql.AuditStopped", instance + ": SecurityGuard SQL izlemesi çalışmıyor (" + now.Message + ").")
                    {
                        Source = "SQL izleme kontrolü"
                    }
                    .With("Instance", instance)
                    .With("Durum", now.Message)
                    .With("Etki", "Bu sürede SQL login, yetki, sertifika ve veritabanı değişiklikleri yakalanamaz.")
                    .With("Otomatik düzeltme", GeneralSettings.GetBool(_settings, SettingKeys.SqlAuditAutoRestart) ? "Açık: yeniden başlatılıyor" : "Kapalı (Genel Ayarlar)"));
                }

                if (!GeneralSettings.GetBool(_settings, SettingKeys.SqlAuditAutoRestart)) return;
                try
                {
                    Execute(cs, InstallScript());
                    var after = Refresh(instance, cs);
                    AppLog.Warn(Module, instance + ": SQL izleme yeniden başlatıldı → " + after.Message);
                    AuditTrail.Record(_db, "SecurityGuard", "SQL izleme otomatik yeniden başlatıldı", instance + " → " + after.Message);
                }
                catch (Exception ex)
                {
                    AppLog.Error(Module, instance + ": SQL izleme yeniden başlatılamadı.", ex);
                }
            }
            catch (Exception ex)
            {
                AppLog.Warn(Module, instance + ": izleme kontrolü yapılamadı.", ex);
            }
            finally
            {
                _gate.Release();
            }
        }

        /// <summary>
        /// SQL'e bağlanamama durumunu alarma çevirir: hesap reddi (silme / devre dışı / şifre) hemen kritik,
        /// sunucu erişilemiyorsa (servis kapalı / reboot / ağ) <see cref="ServerDownGraceChecks"/> tur sonra kritik.
        /// Bağlantı geri gelince bir kez "tekrar bağlanıldı" bilgisi. Alarm süren kesinti boyunca bir kez üretilir.
        /// </summary>
        private void HandleReachability(string instance, SqlAuditState now)
        {
            ConnFail removed;
            if (!now.Installed)
            {
                _connFails.TryRemove(instance, out removed); // izleme kurulu değil: bağlantı alarmı üretme
                return;
            }

            if (now.Reach == SqlReachability.Ok)
            {
                if (_connFails.TryRemove(instance, out removed) && removed.Alarmed)
                {
                    _events.Record(new GuardEvent("Sql.Reconnected", instance + ": SQL Server'a tekrar bağlanıldı, izleme devam ediyor.")
                    {
                        Source = "SQL izleme kontrolü"
                    }
                    .With("Instance", instance));
                    AppLog.Warn(Module, instance + ": SQL bağlantısı geri geldi.");
                }
                return;
            }

            var f = _connFails.GetOrAdd(instance, _ => new ConnFail());
            f.Consecutive++;
            bool account = now.Reach == SqlReachability.AccountRejected;
            if (f.Alarmed || !(account || f.Consecutive >= ServerDownGraceChecks)) return;
            f.Alarmed = true;

            string head = account
                ? "SecurityGuard'ın SQL hesabı (" + GuardConstants.SqlLoginName + ") reddedildi: silinmiş, devre dışı bırakılmış veya şifresi değiştirilmiş olabilir."
                : "SQL Server'a bağlanılamıyor: SQL servisi durmuş, ağ kapalı veya sunucu/port değişmiş olabilir.";
            _events.Record(new GuardEvent("Sql.Unreachable", instance + ": " + head)
            {
                Source = "SQL izleme kontrolü"
            }
            .With("Instance", instance)
            .With("Durum", now.Message)
            .With("Etki", "Bu sürede SQL login, yetki, sertifika, veritabanı ve tehlikeli ayar (xp_cmdshell...) değişiklikleri YAKALANAMAZ: izleme kör kalır.")
            .With("Olası sebep", account
                ? "Hesabın silinmesi / devre dışı bırakılması / şifre değişikliği — izlemeyi köreltmeye yönelik bir saldırı belirtisi olabilir."
                : "SQL servisi kapalı, yeniden başlıyor veya ağ sorunu.")
            .With("Ne yapmalı", account
                ? "securityGuardWar hesabını kontrol edin; kimin değiştirdiğini araştırın. SQL sayfasından bağlantıyı yeniden test edip kaydedin."
                : "SQL Server servisinin çalıştığını doğrulayın. Planlı bir yeniden başlatma değilse araştırın."));
            AppLog.Warn(Module, instance + ": SQL erişilemiyor alarmı (" + (account ? "hesap reddi" : "sunucu erişilemiyor") + "): " + now.Message);
        }

        private SqlAuditState Refresh(string instance, string cs)
        {
            var state = new SqlAuditState { Installed = IsInstalled(instance), CheckedUtc = DateTime.UtcNow };
            try
            {
                using (var conn = new SqlConnection(cs))
                using (var cmd = conn.CreateCommand())
                {
                    conn.Open();
                    cmd.CommandTimeout = 15;
                    cmd.CommandText =
                        @"SELECT a.is_state_enabled, s.is_state_enabled, st.status_desc
                          FROM sys.server_audits a
                          LEFT JOIN sys.server_audit_specifications s ON s.audit_guid = a.audit_guid AND s.name = @spec
                          LEFT JOIN sys.dm_server_audit_status st ON st.audit_id = a.audit_id
                          WHERE a.name = @audit";
                    cmd.Parameters.AddWithValue("@audit", SqlAuditEvents.AuditName);
                    cmd.Parameters.AddWithValue("@spec", SqlAuditEvents.SpecName);
                    using (var r = cmd.ExecuteReader())
                    {
                        if (!r.Read())
                        {
                            state.Message = state.Installed ? "Audit nesnesi silinmiş" : "Kurulu değil";
                        }
                        else
                        {
                            state.Exists = true;
                            bool audit = !r.IsDBNull(0) && r.GetBoolean(0);
                            bool spec = !r.IsDBNull(1) && r.GetBoolean(1);
                            string status = r.IsDBNull(2) ? null : r.GetString(2);
                            state.Running = audit && spec && (status == null || status == "STARTED");
                            state.Message = state.Running ? "Çalışıyor"
                                : r.IsDBNull(1) ? "Audit tanımı (specification) silinmiş"
                                : !audit ? "Audit durdurulmuş"
                                : !spec ? "Audit tanımı durdurulmuş"
                                : "Audit hata durumunda: " + status;
                            if (!state.Installed) state.Message += " (SecurityGuard dışında kurulmuş)";
                        }
                        state.Reach = SqlReachability.Ok;
                    }
                }
            }
            catch (SqlException ex)
            {
                state.Message = "Bağlanılamadı: " + ex.Message;
                // 18456 login başarısız, 18470 devre dışı, 18487/18488 şifre süresi doldu, 18452 Windows-only: hesap sorunu
                state.Reach = (ex.Number == 18456 || ex.Number == 18470 || ex.Number == 18487 || ex.Number == 18488 || ex.Number == 18452)
                    ? SqlReachability.AccountRejected : SqlReachability.Unreachable;
            }
            catch (Exception ex)
            {
                state.Message = "Bağlanılamadı: " + ex.Message;
                state.Reach = SqlReachability.Unreachable;
            }
            _states[instance] = state;
            return state;
        }

        private string RequireConnection(string instance)
        {
            string cs = _connections.GetConnectionString(instance);
            if (cs == null)
                throw new GuardException(ErrorCodes.Validation, instance + " için kayıtlı ve izlemesi açık bir SQL bağlantısı yok. Önce bağlantıyı test edip kaydedin.");
            return cs;
        }

        private static void Execute(string cs, string script)
        {
            try
            {
                using (var conn = new SqlConnection(cs))
                using (var cmd = conn.CreateCommand())
                {
                    conn.Open();
                    cmd.CommandTimeout = 60;
                    cmd.CommandText = script;
                    cmd.ExecuteNonQuery();
                }
            }
            catch (SqlException ex)
            {
                throw new GuardException(ErrorCodes.SqlFailed, "SQL izleme scripti çalıştırılamadı: " + ex.Message);
            }
        }

        /// <summary>İzlenen instance'lar (kayıtlı ve izleme açık).</summary>
        public List<string> MonitoredInstances()
        {
            return _connections.List().Where(i => i.IsConfigured && i.IsEnabled).Select(i => i.InstanceName).ToList();
        }
    }
}
