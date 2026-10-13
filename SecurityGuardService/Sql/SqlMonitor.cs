using System;
using System.Threading;
using SecurityGuardService.Core.Logging;
using SecurityGuardService.Core.Settings;
using SecurityGuardService.Licensing;

namespace SecurityGuardService.Sql
{
    /// <summary>
    /// SQL Server periyodik kontrolleri (aralık: Genel Ayarlar → "Değişiklik kontrol aralığı"):
    /// audit nöbeti ve anlık görüntü karşılaştırması (SqlBaseline). Hata servisi düşürmez.
    /// </summary>
    internal sealed class SqlMonitor : IDisposable
    {
        private const string Module = "SQLİzleme";
        private static readonly TimeSpan FirstDelay = TimeSpan.FromSeconds(15);

        private readonly SettingsStore _settings;
        private readonly SqlAuditManager _audit;
        private readonly SqlBaseline _baseline;
        private readonly AutoResetEvent _wake = new AutoResetEvent(false);
        private readonly ManualResetEventSlim _stopping = new ManualResetEventSlim(false);
        private readonly CancellationTokenSource _cancel = new CancellationTokenSource();
        private Thread _thread;

        public SqlMonitor(SettingsStore settings, SqlAuditManager audit, SqlBaseline baseline)
        {
            _settings = settings;
            _audit = audit;
            _baseline = baseline;
        }

        public void Start()
        {
            _thread = new Thread(Loop) { IsBackground = true, Name = "SqlMonitor" };
            _thread.Start();
        }

        /// <summary>Beklemeden kontrol et (ör. audit nesnemize dokunulduğu görüldü).</summary>
        public void CheckSoon()
        {
            _wake.Set();
        }

        private void Loop()
        {
            if (WaitOrStop(FirstDelay)) return;
            while (!_stopping.IsSet)
            {
                try
                {
                    foreach (var instance in LicenseGate.Active ? _audit.MonitoredInstances() : new System.Collections.Generic.List<string>())
                    {
                        if (_stopping.IsSet) return;
                        _audit.CheckAsync(instance).Wait();
                        try
                        {
                            _baseline.Check(instance, _cancel.Token);
                        }
                        catch (Exception ex)
                        {
                            if (_stopping.IsSet) return;
                            AppLog.Warn(Module, instance + ": değişiklik karşılaştırması yapılamadı: " + ex.Message);
                        }
                    }
                }
                catch (Exception ex)
                {
                    AppLog.Error(Module, "Periyodik SQL kontrolü başarısız.", ex);
                }

                int minutes = GeneralSettings.GetInt(_settings, SettingKeys.SqlBaselineIntervalMinutes);
                if (WaitOrStop(TimeSpan.FromMinutes(minutes))) return;
            }
        }

        /// <returns>true = servis duruyor.</returns>
        private bool WaitOrStop(TimeSpan delay)
        {
            WaitHandle.WaitAny(new[] { _stopping.WaitHandle, _wake }, delay);
            return _stopping.IsSet;
        }

        public void Dispose()
        {
            _stopping.Set();
            _cancel.Cancel();
            if (_thread != null) _thread.Join(TimeSpan.FromSeconds(10));
        }
    }
}
