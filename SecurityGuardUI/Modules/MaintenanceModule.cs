using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Forms;
using DevExpress.XtraEditors;
using DevExpress.XtraGrid.Views.Base;
using DevExpress.XtraGrid.Views.Grid;
using SecurityGuard.Shared;
using SecurityGuard.Shared.Contracts;
using SecurityGuard.Shared.Dto;
using SecurityGuardUI.Helpers;

namespace SecurityGuardUI.Modules
{
    /// <summary>
    /// SQL Bakımı: seçilen DB'lerde bakım penceresinde index / istatistik bakımı ve (isteğe bağlı) log küçültme.
    /// Varsayılan kapalı; açılınca da deneme modunda başlar (hiçbir şey uygulanmaz, ne yapılacağı listelenir).
    /// Altta son çalıştırmalar ve seçilen çalıştırmanın her işlemi (önce / sonra parçalanma, süre, sonuç).
    /// </summary>
    public partial class MaintenanceModule : ModuleBase
    {
        private readonly Timer _poll = new Timer { Interval = 3000 };
        private readonly CheckEdit[] _days;
        private MaintenanceStateDto _state;
        private bool _polling;
        private bool _wasRunning;

        public MaintenanceModule()
        {
            InitializeComponent();
            // Pazartesiden başlayan sıra; Tag = DayOfWeek
            _days = new[] { checkMon, checkTue, checkWed, checkThu, checkFri, checkSat, checkSun };

            GridHelper.SetupReadOnly(viewDbs);
            viewDbs.OptionsView.ShowAutoFilterRow = false;
            viewDbs.OptionsBehavior.Editable = true;
            viewDbs.OptionsBehavior.ReadOnly = false;
            foreach (DevExpress.XtraGrid.Columns.GridColumn c in viewDbs.Columns) c.OptionsColumn.AllowEdit = c == colDbSelected;

            GridHelper.SetupReadOnly(viewRuns);
            viewRuns.OptionsView.ShowAutoFilterRow = false;
            GridHelper.SetupReadOnly(viewOps);
            viewOps.OptionsView.ShowAutoFilterRow = false;

            _poll.Tick += async (s, e) => await PollAsync();
        }

        protected override Task OnFirstShowAsync()
        {
            return LoadAsync();
        }

        private async Task LoadAsync()
        {
            var state = await CallAsync(c => c.SendAsync<MaintenanceStateDto>(Commands.MaintenanceGet, null, TimeSpan.FromMinutes(1)));
            if (state != null) Bind(state, true);
        }

        private void Bind(MaintenanceStateDto state, bool config)
        {
            string focusedRun = FocusedRun() == null ? null : FocusedRun().RunId;
            // Yeni bir çalıştırma başladıysa (listenin başına eklendi) o seçilir: işlemleri canlı görünsün
            bool newRun = _state != null && state.Runs.Count > 0 && !_state.Runs.Any(r => r.RunId == state.Runs[0].RunId);
            _state = state;
            if (config) BindConfig(state.Config);

            gridDbs.DataSource = state.Databases.Select(d => new DbRow
            {
                Selected = d.Selected,
                Instance = d.Instance,
                Name = d.Name,
                Recovery = d.RecoveryModel,
                Size = d.RecoveryModel == "bulunamadı" ? "" : MbText(d.DataMb) + " + log " + MbText(d.LogMb)
            }).ToList();

            var notes = new List<string>(state.Notes);
            if (state.OtherJobs.Count > 0)
                notes.Insert(0, "DİKKAT: Sunucuda zaten bakım yapan iş var (" + string.Join(", ", state.OtherJobs)
                                + "). Aynı DB'ler için SecurityGuard bakımını da açarsanız aynı iş iki kez yapılır.");
            labelNotes.Text = string.Join(Environment.NewLine, notes);
            labelNotes.Appearance.ForeColor = state.OtherJobs.Count > 0 ? UiColors.Warning : UiColors.Text;
            labelThresholds.Text = state.Thresholds;

            gridRuns.DataSource = state.Runs.Select(r => new RunRow(r)).ToList();
            int index = newRun || focusedRun == null ? 0 : Math.Max(0, state.Runs.FindIndex(r => r.RunId == focusedRun));
            if (state.Runs.Count > 0) viewRuns.FocusedRowHandle = viewRuns.GetRowHandle(index);

            if (state.IsRunning) _poll.Start();
            else _poll.Stop();
            buttonDryRun.Enabled = buttonRunNow.Enabled = !state.IsRunning;

            if (state.IsRunning)
            {
                labelStatus.Text = "Bakım sürüyor: " + (state.Progress ?? "...");
                labelStatus.Appearance.ForeColor = UiColors.Warning;
            }
            else
            {
                labelStatus.Text = !state.Config.Enabled ? "Bakım kapalı: SecurityGuard SQL Server'da hiçbir bakım işlemi yapmaz."
                    : "Bakım açık" + (state.Config.DryRun ? " (DENEME MODU: hiçbir şey uygulanmaz)" : "")
                      + (state.NextRunUtc != null ? ". Sonraki bakım: " + Local(state.NextRunUtc) : "");
                labelStatus.Appearance.ForeColor = state.Config.Enabled && !state.Config.DryRun ? UiColors.Warning : UiColors.Text;
            }

            // Seçili çalıştırma sürüyorsa (veya az önce bittiyse) işlemleri yenilenir
            var focused = FocusedRun();
            if (focused != null && (focused.Status == "Sürüyor" || _wasRunning || newRun)) ReloadOps();
            _wasRunning = state.IsRunning;
        }

        private void BindConfig(MaintenanceConfigDto c)
        {
            checkEnabled.Checked = c.Enabled;
            checkDryRun.Checked = c.DryRun;
            for (int i = 0; i < _days.Length; i++) _days[i].Checked = c.Days.Contains((i + 1) % 7);
            TimeSpan start;
            timeStart.EditValue = TimeSpan.TryParse(c.StartTime, out start) ? DateTime.Today + start : DateTime.Today.AddHours(1);
            spinMax.Value = c.MaxMinutes;
            checkIndexes.Checked = c.Indexes;
            checkStats.Checked = c.Statistics;
            checkShrink.Checked = c.ShrinkLog;
        }

        private MaintenanceConfigDto ReadConfig()
        {
            viewDbs.CloseEditor();
            viewDbs.UpdateCurrentRow();
            var rows = gridDbs.DataSource as List<DbRow> ?? new List<DbRow>();
            var time = timeStart.EditValue is DateTime ? (DateTime)timeStart.EditValue : DateTime.Today.AddHours(1);
            return new MaintenanceConfigDto
            {
                Enabled = checkEnabled.Checked,
                DryRun = checkDryRun.Checked,
                Days = _days.Select((d, i) => new { d, day = (i + 1) % 7 }).Where(x => x.d.Checked).Select(x => x.day).ToList(),
                StartTime = time.ToString("HH:mm"),
                MaxMinutes = (int)spinMax.Value,
                Indexes = checkIndexes.Checked,
                Statistics = checkStats.Checked,
                ShrinkLog = checkShrink.Checked,
                Databases = rows.Where(r => r.Selected).Select(r => r.Instance + "|" + r.Name).ToList()
            };
        }

        // ================================================================ Butonlar

        private async void buttonSave_Click(object sender, EventArgs e)
        {
            var cfg = ReadConfig();
            if (cfg.Enabled && !cfg.DryRun && (_state == null || !_state.Config.Enabled || _state.Config.DryRun))
            {
                if (!Ui.Confirm(FindForm(), "Bakım deneme modu KAPALI olarak açılıyor: seçilen günlerde SQL Server'da index / istatistik"
                                            + (cfg.ShrinkLog ? " / log küçültme" : "") + " işlemleri gerçekten uygulanacak."
                                            + Environment.NewLine + Environment.NewLine
                                            + "Standard / Express edition'da REBUILD sırasında tablo kilitlenir: bakım saatinde kimse çalışmamalı."
                                            + Environment.NewLine + "Önce \"Deneme Yap\" ile ne yapılacağına baktınız mı? Devam edilsin mi?"))
                    return;
            }
            var state = await CallAsync(c => c.SendAsync<MaintenanceStateDto>(Commands.MaintenanceSave, cfg, TimeSpan.FromMinutes(1)));
            if (state == null) return;
            Bind(state, true);
            Ui.Info(FindForm(), "Bakım ayarları kaydedildi.");
        }

        private async void buttonDryRun_Click(object sender, EventArgs e)
        {
            if (!await SaveIfChangedAsync()) return;
            var state = await CallAsync(c => c.SendAsync<MaintenanceStateDto>(Commands.MaintenanceRun, new MaintenanceRunRequest { DryRun = true }, TimeSpan.FromMinutes(1)));
            if (state != null) Bind(state, false);
        }

        private async void buttonRunNow_Click(object sender, EventArgs e)
        {
            if (!await SaveIfChangedAsync()) return;
            if (!Ui.Confirm(FindForm(), "Bakım ŞİMDİ uygulanacak (bakım penceresi beklenmeden, en fazla " + (int)spinMax.Value + " dakika)."
                                        + Environment.NewLine + Environment.NewLine
                                        + "Standard / Express edition'da REBUILD sırasında tablolar kilitlenir ve kullanıcılar bekler. "
                                        + "Mesai içindeyseniz \"Deneme Yap\" kullanın." + Environment.NewLine + Environment.NewLine + "Devam edilsin mi?"))
                return;
            var state = await CallAsync(c => c.SendAsync<MaintenanceStateDto>(Commands.MaintenanceRun, new MaintenanceRunRequest { DryRun = false }, TimeSpan.FromMinutes(1)));
            if (state != null) Bind(state, false);
        }

        /// <summary>Elle çalıştırma kayıtlı ayarla çalışır: ekrandaki değişiklik kaydedilmemişse önce sorulur.</summary>
        private async Task<bool> SaveIfChangedAsync()
        {
            if (_state == null) return false;
            var cfg = ReadConfig();
            if (Same(cfg, _state.Config)) return true;
            if (!Ui.Confirm(FindForm(), "Ekrandaki değişiklikler kaydedilmemiş. Kaydedip öyle çalıştırılsın mı?")) return false;
            var state = await CallAsync(c => c.SendAsync<MaintenanceStateDto>(Commands.MaintenanceSave, cfg, TimeSpan.FromMinutes(1)));
            if (state == null) return false;
            Bind(state, true);
            return true;
        }

        private static bool Same(MaintenanceConfigDto a, MaintenanceConfigDto b)
        {
            return a.Enabled == b.Enabled && a.DryRun == b.DryRun && a.StartTime == b.StartTime && a.MaxMinutes == b.MaxMinutes
                   && a.Indexes == b.Indexes && a.Statistics == b.Statistics && a.ShrinkLog == b.ShrinkLog
                   && new HashSet<int>(a.Days).SetEquals(b.Days)
                   && new HashSet<string>(a.Databases, StringComparer.OrdinalIgnoreCase).SetEquals(b.Databases);
        }

        private async void buttonRefresh_Click(object sender, EventArgs e)
        {
            await LoadAsync();
        }

        private async Task PollAsync()
        {
            if (_polling || IsDisposed) return;
            _polling = true;
            try
            {
                var result = await Client.SendAsync<MaintenanceStateDto>(Commands.MaintenanceGet, null, TimeSpan.FromMinutes(1));
                if (IsDisposed) return;
                if (result.IsUnauthorized)
                {
                    _poll.Stop();
                    Host.HandleSessionExpired();
                    return;
                }
                if (result.Success) Bind(result.Value, false);
            }
            finally
            {
                _polling = false;
            }
        }

        // ================================================================ Çalıştırmalar

        private RunRow FocusedRun()
        {
            return viewRuns.GetFocusedRow() as RunRow;
        }

        private async void viewRuns_FocusedRowChanged(object sender, FocusedRowChangedEventArgs e)
        {
            await LoadOpsAsync();
        }

        private async void ReloadOps()
        {
            await LoadOpsAsync();
        }

        private async Task LoadOpsAsync()
        {
            var run = FocusedRun();
            if (run == null)
            {
                gridOps.DataSource = null;
                return;
            }
            var result = await Client.SendAsync<List<MaintenanceOpDto>>(Commands.MaintenanceOps, new RunIdRequest { RunId = run.RunId });
            if (IsDisposed || FocusedRun() != run) return;
            if (result.IsUnauthorized)
            {
                Host.HandleSessionExpired();
                return;
            }
            if (!result.Success) return;
            gridOps.DataSource = result.Value.Select(o => new OpRow
            {
                Time = UtcTime.ToLocal(o.TimeUtc),
                Database = o.Database,
                Object = o.ObjectName,
                Operation = o.Operation,
                Before = o.Before,
                After = o.After,
                Duration = o.DurationMs <= 0 ? "" : o.DurationMs < 1000 ? o.DurationMs + " ms" : (o.DurationMs / 1000.0).ToString("0.#") + " sn",
                Result = o.Result,
                Note = o.Error
            }).ToList();
        }

        private void viewRuns_RowCellStyle(object sender, RowCellStyleEventArgs e)
        {
            var r = viewRuns.GetRow(e.RowHandle) as RunRow;
            if (r == null || e.Column != colRunStatus) return;
            if (r.Status == "Hatalı" || r.Status == "Yarıda kesildi") e.Appearance.ForeColor = UiColors.Error;
            else if (r.Status == "Süre doldu" || r.Status == "Sürüyor") e.Appearance.ForeColor = UiColors.Warning;
            else e.Appearance.ForeColor = UiColors.Success;
        }

        private void viewOps_RowCellStyle(object sender, RowCellStyleEventArgs e)
        {
            var r = viewOps.GetRow(e.RowHandle) as OpRow;
            if (r == null || e.Column != colOpResult) return;
            if (r.Result == "Hata" || r.Result == "İptal") e.Appearance.ForeColor = UiColors.Error;
            else if (r.Result == "Atlandı" || r.Result == "Bekleniyor") e.Appearance.ForeColor = UiColors.Warning;
            else if (r.Result == "Yapıldı") e.Appearance.ForeColor = UiColors.Success;
        }

        private void buttonExportOps_Click(object sender, EventArgs e)
        {
            GridHelper.ExportToExcel(FindForm(), gridOps, "SqlBakim");
        }

        private static string Local(string utc)
        {
            var t = UtcTime.ToLocal(utc);
            return t.HasValue ? t.Value.ToString("dd.MM.yyyy HH:mm") : "-";
        }

        private static string MbText(double mb)
        {
            return mb >= 1024 ? (mb / 1024).ToString("0.#") + " GB" : mb.ToString("0") + " MB";
        }

        protected override void OnHandleDestroyed(EventArgs e)
        {
            _poll.Stop();
            base.OnHandleDestroyed(e);
        }

        // ================================================================ Satırlar

        public sealed class DbRow
        {
            public bool Selected { get; set; }
            public string Instance { get; set; }
            public string Name { get; set; }
            public string Recovery { get; set; }
            public string Size { get; set; }
        }

        public sealed class RunRow
        {
            internal RunRow(MaintenanceRunDto r)
            {
                RunId = r.RunId;
                Time = UtcTime.ToLocal(r.StartedUtc);
                Instance = r.Instance;
                Mode = r.DryRun ? "Deneme" : "Uygulama";
                Status = r.Status;
                Summary = r.Summary;
                Trigger = r.Trigger;
                var a = UtcTime.ToLocal(r.StartedUtc);
                var b = UtcTime.ToLocal(r.FinishedUtc);
                Duration = a.HasValue && b.HasValue ? Math.Max(0, (int)(b.Value - a.Value).TotalMinutes) + " dk" : "";
            }

            internal string RunId { get; private set; }
            public DateTime? Time { get; private set; }
            public string Instance { get; private set; }
            public string Mode { get; private set; }
            public string Status { get; private set; }
            public string Duration { get; private set; }
            public string Summary { get; private set; }
            public string Trigger { get; private set; }
        }

        public sealed class OpRow
        {
            public DateTime? Time { get; set; }
            public string Database { get; set; }
            public string Object { get; set; }
            public string Operation { get; set; }
            public string Before { get; set; }
            public string After { get; set; }
            public string Duration { get; set; }
            public string Result { get; set; }
            public string Note { get; set; }
        }
    }
}
