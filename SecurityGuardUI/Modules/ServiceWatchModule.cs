using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Forms;
using DevExpress.XtraEditors;
using DevExpress.XtraGrid;
using DevExpress.XtraGrid.Views.Grid;
using SecurityGuard.Shared.Contracts;
using SecurityGuard.Shared.Dto;
using SecurityGuardUI.Helpers;

namespace SecurityGuardUI.Modules
{
    /// <summary>
    /// Servis Takibi: izlenecek Windows servislerini seçme. İzlenen servis durursa / kaldırılırsa mail; tekrar çalışınca mail.
    /// "Otomatik yeniden başlat" servis bazında, varsayılan kapalı.
    /// </summary>
    public sealed class ServiceWatchModule : ModuleBase
    {
        private readonly GridControl _grid = new GridControl { Dock = DockStyle.Fill };
        private readonly GridView _view;
        private readonly CheckEdit _onlyWatched = new CheckEdit { Width = 200 };
        private readonly SimpleButton _save = new SimpleButton { Text = "Kaydet", Size = new Size(90, 26) };
        private readonly LabelControl _status = new LabelControl();
        private List<Row> _rows = new List<Row>();
        private bool _dirty;

        public ServiceWatchModule()
        {
            _view = new GridView(_grid);
            _grid.MainView = _view;
            _grid.ViewCollection.Add(_view);
            GridHelper.SetupReadOnly(_view);
            _view.OptionsBehavior.Editable = true;
            _view.OptionsBehavior.EditorShowMode = DevExpress.Utils.EditorShowMode.MouseDown;
            Build();
        }

        private void Build()
        {
            var top = new PanelControl { Dock = DockStyle.Top, Height = 118, BorderStyle = DevExpress.XtraEditors.Controls.BorderStyles.NoBorder };
            var title = new LabelControl { Text = "Servis Takibi", Location = new Point(12, 8) };
            title.Appearance.Font = new Font("Segoe UI", 13F);
            title.Appearance.Options.UseFont = true;
            var info = new LabelControl
            {
                Location = new Point(12, 36),
                AutoSizeMode = LabelAutoSizeMode.None,
                Size = new Size(1000, 36),
                Text = "İşaretlenen servisler 15 saniyede bir kontrol edilir: durursa (çökme, biri durdurdu, devre dışı bırakıldı, kaldırıldı) kritik mail, tekrar çalışınca bilgi maili. "
                       + "\"Otomatik yeniden başlat\" açıksa SecurityGuard 30 sn, 2 dk ve 5 dk sonra servisi başlatmayı dener (devre dışı bırakılmış servise dokunmaz)."
            };
            info.Appearance.TextOptions.WordWrap = DevExpress.Utils.WordWrap.Wrap;
            info.Appearance.Options.UseTextOptions = true;
            top.Resize += (s, e) => info.Width = Math.Max(300, top.Width - 24);

            _save.Location = new Point(12, 82);
            _save.Click += async (s, e) => await SaveAsync();
            var refresh = new SimpleButton { Text = "Yenile", Location = new Point(108, 82), Size = new Size(80, 26) };
            refresh.Click += async (s, e) => await LoadAsync();
            var recommend = new SimpleButton { Text = "Önerilenleri işaretle", Location = new Point(194, 82), Size = new Size(150, 26) };
            recommend.Click += (s, e) =>
            {
                foreach (var r in _rows.Where(r => r.Item.Recommended && r.StartType == "Otomatik")) r.Watched = true;
                _dirty = true;
                Bind();
            };
            _onlyWatched.Location = new Point(360, 85);
            _onlyWatched.Properties.Caption = "Sadece izlenenleri göster";
            _onlyWatched.Checked = true;
            _onlyWatched.CheckedChanged += (s, e) => Bind();
            top.Controls.AddRange(new Control[] { title, info, _save, refresh, recommend, _onlyWatched });

            var bottom = new PanelControl { Dock = DockStyle.Bottom, Height = 28, BorderStyle = DevExpress.XtraEditors.Controls.BorderStyles.NoBorder };
            _status.Location = new Point(12, 6);
            bottom.Controls.Add(_status);

            Controls.Add(_grid);
            Controls.Add(top);
            Controls.Add(bottom);

            _view.OptionsFind.AlwaysVisible = true;
            _view.OptionsFind.FindNullPrompt = "Servis ara...";
            _view.OptionsFind.ShowFindButton = false;
            _view.OptionsFind.ShowClearButton = false;
            _view.CellValueChanged += (s, e) => { _dirty = true; UpdateStatus(); };
            _view.RowStyle += (s, e) =>
            {
                var row = _view.GetRow(e.RowHandle) as Row;
                if (row == null || !row.Watched) return;
                if (row.Status != "Çalışıyor") e.Appearance.ForeColor = UiColors.Error;
            };
        }

        protected override Task OnFirstShowAsync()
        {
            return LoadAsync();
        }

        private async Task LoadAsync()
        {
            if (_dirty && !Ui.Confirm(FindForm(), "Kaydedilmemiş değişiklikler var. Yine de yenilensin mi?")) return;
            var state = await CallAsync(c => c.SendAsync<ServiceWatchStateDto>(Commands.ServiceWatchGet));
            if (state != null) ApplyState(state);
        }

        private void ApplyState(ServiceWatchStateDto state)
        {
            _rows = state.Services.Select(s => new Row(s)).ToList();
            _dirty = false;
            if (_rows.All(r => !r.Watched)) _onlyWatched.Checked = false;
            Bind();
        }

        private void Bind()
        {
            _grid.DataSource = _rows.Where(r => !_onlyWatched.Checked || r.Watched).OrderByDescending(r => r.Watched).ThenBy(r => r.DisplayName).ToList();
            _view.BestFitColumns();
            foreach (DevExpress.XtraGrid.Columns.GridColumn c in _view.Columns)
                c.OptionsColumn.AllowEdit = c.FieldName == "Watched" || c.FieldName == "AutoRestart";
            UpdateStatus();
        }

        private void UpdateStatus()
        {
            int watched = _rows.Count(r => r.Watched), down = _rows.Count(r => r.Watched && r.Status != "Çalışıyor");
            _status.Text = _rows.Count + " servis, " + watched + " izleniyor" + (down > 0 ? ", " + down + " izlenen servis ÇALIŞMIYOR" : "") + (_dirty ? ". Kaydedilmemiş değişiklik var." : ".");
            _save.Enabled = _dirty;
        }

        private async Task SaveAsync()
        {
            _view.CloseEditor();
            _view.UpdateCurrentRow();
            var req = new ServiceWatchSaveRequest
            {
                Items = _rows.Where(r => r.Watched).Select(r => new WatchedServiceDto { Name = r.Item.Name, DisplayName = r.DisplayName, Watched = true, AutoRestart = r.AutoRestart }).ToList()
            };
            var restart = req.Items.Where(i => i.AutoRestart).ToList();
            if (restart.Any(i => !_rows.First(r => r.Item.Name == i.Name).Item.AutoRestart)
                && !Ui.Confirm(FindForm(), "Otomatik yeniden başlatma açılacak:\r\n\r\n  " + string.Join("\r\n  ", restart.Select(i => i.DisplayName))
                                           + "\r\n\r\nBu servisleri biri bakım için bilerek durdurursa SecurityGuard tekrar başlatır. Bakım öncesi bu seçeneği kapatın. Devam edilsin mi?"))
                return;
            var state = await CallAsync(c => c.SendAsync<ServiceWatchStateDto>(Commands.ServiceWatchSave, req));
            if (state == null) return;
            ApplyState(state);
            Ui.Info(FindForm(), "Kaydedildi: " + state.Services.Count(s => s.Watched) + " servis izleniyor.");
        }

        public sealed class Row
        {
            public Row(WatchedServiceDto item)
            {
                Item = item;
                Watched = item.Watched;
                AutoRestart = item.AutoRestart;
            }

            internal WatchedServiceDto Item { get; private set; }

            [System.ComponentModel.DisplayName("İzle")]
            public bool Watched { get; set; }

            [System.ComponentModel.DisplayName("Otomatik yeniden başlat")]
            public bool AutoRestart { get; set; }

            [System.ComponentModel.DisplayName("Servis")]
            public string DisplayName { get { return Item.DisplayName; } }

            [System.ComponentModel.DisplayName("Ad")]
            public string Name { get { return Item.Name; } }

            [System.ComponentModel.DisplayName("Durum")]
            public string Status { get { return Item.Status; } }

            [System.ComponentModel.DisplayName("Başlangıç")]
            public string StartType { get { return Item.StartType; } }

            [System.ComponentModel.DisplayName("Önerilen")]
            public string Recommended { get { return Item.Recommended ? "Evet" : ""; } }

            [System.ComponentModel.DisplayName("Son olay")]
            public string LastEvent { get { return Item.LastEvent; } }
        }
    }
}
