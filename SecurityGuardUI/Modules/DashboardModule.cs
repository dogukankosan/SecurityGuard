using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Forms;
using DevExpress.XtraEditors;
using DevExpress.XtraGrid.Views.Grid;
using SecurityGuard.Shared;
using SecurityGuard.Shared.Contracts;
using SecurityGuard.Shared.Dto;
using SecurityGuardUI.Helpers;

namespace SecurityGuardUI.Modules
{
    /// <summary>
    /// Panel (ana sayfa): durum kutucukları (tıklanınca ilgili sayfa açılır), son önemli olaylar ve yapılması önerilenler.
    /// Sayfa görünürken dakikada bir kendiliğinden yenilenir.
    /// </summary>
    public partial class DashboardModule : ModuleBase
    {
        private readonly Timer _refresh = new Timer { Interval = 60000 };
        private readonly List<TileView> _tiles = new List<TileView>();
        private bool _loading;

        public DashboardModule()
        {
            InitializeComponent();
            for (int i = 0; i < 10; i++)
            {
                var tile = new TileView();
                tile.Click += (s, e) => { if (tile.Page != null && Host != null) Host.Navigate(tile.Page); };
                _tiles.Add(tile);
                tableTiles.Controls.Add(tile.Panel, i % 5, i / 5);
            }
            GridHelper.SetupReadOnly(viewEvents);
            viewEvents.OptionsView.ShowAutoFilterRow = false;
            GridHelper.SetupReadOnly(viewTodos);
            viewTodos.OptionsView.ShowAutoFilterRow = false;
            viewTodos.OptionsView.RowAutoHeight = true;
            _refresh.Tick += async (s, e) => { if (Visible) await LoadAsync(false); };
        }

        protected override async Task OnFirstShowAsync()
        {
            await LoadAsync(true);
            _refresh.Start();
        }

        private async Task LoadAsync(bool overlay)
        {
            if (_loading) return;
            _loading = true;
            try
            {
                DashboardDto dto;
                if (overlay)
                {
                    dto = await CallAsync(c => c.SendAsync<DashboardDto>(Commands.DashboardGet, null, TimeSpan.FromMinutes(1)));
                }
                else
                {
                    var result = await Client.SendAsync<DashboardDto>(Commands.DashboardGet, null, TimeSpan.FromMinutes(1));
                    if (result.IsUnauthorized)
                    {
                        _refresh.Stop();
                        Host.HandleSessionExpired();
                        return;
                    }
                    dto = result.Success ? result.Value : null;
                }
                if (dto == null || IsDisposed) return;
                Bind(dto);
            }
            finally
            {
                _loading = false;
            }
        }

        private void Bind(DashboardDto dto)
        {
            var started = UtcTime.ToLocal(dto.ServiceStartedUtc);
            labelInfo.Text = dto.MachineName + " · SecurityGuard " + dto.Version
                             + (started.HasValue ? " · servis " + Since(started.Value) + " çalışıyor" : "") + " · güncellendi " + DateTime.Now.ToString("HH:mm");
            for (int i = 0; i < _tiles.Count; i++)
                _tiles[i].Bind(i < dto.Tiles.Count ? dto.Tiles[i] : null);

            gridEvents.DataSource = dto.RecentEvents.Select(e => new EventRow
            {
                Time = UtcTime.ToLocal(e.TimeUtc),
                Severity = e.Severity,
                Name = e.Name,
                Summary = e.Summary
            }).ToList();
            gridTodos.DataSource = dto.Todos.Select(t => new TodoRow { Level = t.Level, Text = t.Text, Page = t.Page }).ToList();
            labelTodos.Text = dto.Todos.Count == 0 ? "Yapılması önerilen iş yok." : "Yapılması önerilenler (" + dto.Todos.Count + ") · çift tıklayınca ilgili sayfa açılır";
        }

        private static string Since(DateTime started)
        {
            var span = DateTime.Now - started;
            return span.TotalDays >= 1 ? (int)span.TotalDays + " gündür" : span.TotalHours >= 1 ? (int)span.TotalHours + " saattir" : Math.Max(1, (int)span.TotalMinutes) + " dakikadır";
        }

        private async void buttonRefresh_Click(object sender, EventArgs e)
        {
            await LoadAsync(true);
        }

        private void viewEvents_RowCellStyle(object sender, RowCellStyleEventArgs e)
        {
            var row = viewEvents.GetRow(e.RowHandle) as EventRow;
            if (row == null || e.Column != colEventSeverity) return;
            e.Appearance.ForeColor = row.Severity == 2 ? UiColors.Error : UiColors.Warning;
        }

        private void viewEvents_CustomColumnDisplayText(object sender, DevExpress.XtraGrid.Views.Base.CustomColumnDisplayTextEventArgs e)
        {
            if (e.Column == colEventSeverity && e.Value is int) e.DisplayText = (int)e.Value == 2 ? "Kritik" : "Uyarı";
        }

        private void viewTodos_RowCellStyle(object sender, RowCellStyleEventArgs e)
        {
            var row = viewTodos.GetRow(e.RowHandle) as TodoRow;
            if (row == null || e.Column != colTodoLevel) return;
            e.Appearance.ForeColor = row.Level >= 3 ? UiColors.Error : UiColors.Warning;
        }

        private void viewTodos_CustomColumnDisplayText(object sender, DevExpress.XtraGrid.Views.Base.CustomColumnDisplayTextEventArgs e)
        {
            if (e.Column == colTodoLevel && e.Value is int) e.DisplayText = (int)e.Value >= 3 ? "Kritik" : "Uyarı";
        }

        private void viewTodos_DoubleClick(object sender, EventArgs e)
        {
            var row = viewTodos.GetFocusedRow() as TodoRow;
            if (row != null && row.Page != null) Host.Navigate(row.Page);
        }

        private void viewEvents_DoubleClick(object sender, EventArgs e)
        {
            Host.Navigate(DashboardPages.EventLog);
        }

        protected override void OnHandleDestroyed(EventArgs e)
        {
            _refresh.Stop();
            base.OnHandleDestroyed(e);
        }

        /// <summary>Tek durum kutucuğu: solda renkli şerit, başlık, büyük değer, açıklama. Tıklanınca ilgili sayfa.</summary>
        private sealed class TileView
        {
            public readonly PanelControl Panel = new PanelControl { Dock = DockStyle.Fill, Margin = new Padding(6), Cursor = Cursors.Hand };
            private readonly Panel _strip = new Panel { Dock = DockStyle.Left, Width = 5 };
            private readonly LabelControl _title = new LabelControl { Location = new Point(16, 10), Cursor = Cursors.Hand };
            private readonly LabelControl _value = new LabelControl { Location = new Point(14, 30), Cursor = Cursors.Hand };
            private readonly LabelControl _detail = new LabelControl
            {
                Location = new Point(16, 66), AutoSizeMode = LabelAutoSizeMode.None, Size = new Size(200, 34), Cursor = Cursors.Hand,
                Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right
            };

            // Son 24 saatin eğilimi (sadece işlemci / bellek kutucuklarında)
            private readonly SparklineEdit _trend = new SparklineEdit
            {
                Location = new Point(12, 64), Size = new Size(200, 22), Visible = false, Cursor = Cursors.Hand,
                Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right
            };

            public TileView()
            {
                _value.Appearance.Font = new Font("Segoe UI", 15F, FontStyle.Bold);
                _value.Appearance.Options.UseFont = true;
                _value.Appearance.Options.UseForeColor = true;
                _detail.Appearance.Options.UseTextOptions = true;
                _detail.Appearance.TextOptions.WordWrap = DevExpress.Utils.WordWrap.Wrap;
                _detail.Appearance.TextOptions.VAlignment = DevExpress.Utils.VertAlignment.Top;
                _detail.AutoEllipsis = true;
                _trend.Properties.View = new DevExpress.Sparkline.LineSparklineView { HighlightMaxPoint = true, HighlightEndPoint = true };
                _trend.Properties.BorderStyle = DevExpress.XtraEditors.Controls.BorderStyles.NoBorder;
                _trend.Properties.ReadOnly = true;
                Panel.Controls.Add(_trend);
                Panel.Controls.Add(_detail);
                Panel.Controls.Add(_value);
                Panel.Controls.Add(_title);
                Panel.Controls.Add(_strip);
                Panel.Resize += (s, e) => { _detail.Width = Math.Max(50, Panel.Width - 24); _trend.Width = Math.Max(50, Panel.Width - 20); };
                foreach (Control c in new Control[] { Panel, _title, _value, _detail, _strip, _trend })
                    c.Click += (s, e) => { var h = Click; if (h != null) h(this, EventArgs.Empty); };
            }

            public event EventHandler Click;
            public string Page { get; private set; }

            public void Bind(DashboardTileDto tile)
            {
                Panel.Visible = tile != null;
                if (tile == null) return;
                Page = tile.Page;
                _title.Text = tile.Title;
                _value.Text = tile.Value;
                _detail.Text = tile.Detail;
                Color color = tile.Level >= 3 ? UiColors.Error : tile.Level == 2 ? UiColors.Warning : tile.Level == 0 ? UiColors.Success : UiColors.Text;
                bool trend = tile.Trend != null && tile.Trend.Count > 1;
                _trend.Visible = trend;
                _detail.Top = trend ? 88 : 66;
                if (trend) _trend.EditValue = tile.Trend.ToArray();
                ((DevExpress.Sparkline.LineSparklineView)_trend.Properties.View).Color = tile.Level >= 2 ? color : Color.FromArgb(90, 140, 200);
                _value.Appearance.ForeColor = color;
                _strip.BackColor = tile.Level == 1 ? Color.FromArgb(90, 140, 200) : color;
            }
        }

        public sealed class EventRow
        {
            public DateTime? Time { get; set; }
            public int Severity { get; set; }
            public string Name { get; set; }
            public string Summary { get; set; }
        }

        public sealed class TodoRow
        {
            public int Level { get; set; }
            public string Text { get; set; }
            internal string Page { get; set; }
        }
    }
}
