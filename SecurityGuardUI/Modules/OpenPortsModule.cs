using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Forms;
using DevExpress.XtraEditors;
using DevExpress.XtraGrid;
using DevExpress.XtraGrid.Views.Grid;
using SecurityGuard.Shared;
using SecurityGuard.Shared.Contracts;
using SecurityGuard.Shared.Dto;
using SecurityGuardUI.Helpers;

namespace SecurityGuardUI.Modules
{
    /// <summary>
    /// Açık Portlar: sunucuda dinleyen portlar, firewall'da kime açık oldukları, riskleri ve son 24 saatteki başarısız girişler.
    /// Sadece gösterir ve önerir; "Port kısıtı ekle" Port Kısıtları sayfasını doldurulmuş olarak açar.
    /// </summary>
    public sealed class OpenPortsModule : ModuleBase
    {
        private readonly GridControl _grid = new GridControl { Dock = DockStyle.Fill };
        private readonly GridView _view;
        private readonly CheckEdit _showAll = new CheckEdit { Width = 260 };
        private readonly LabelControl _warning = new LabelControl();
        private readonly LabelControl _status = new LabelControl();
        private readonly MemoEdit _advice = new MemoEdit { Dock = DockStyle.Fill };
        private readonly SimpleButton _restrict = new SimpleButton { Text = "Port kısıtı ekle...", Size = new Size(140, 26) };
        private OpenPortsDto _state;

        public OpenPortsModule()
        {
            _view = new GridView(_grid);
            _grid.MainView = _view;
            _grid.ViewCollection.Add(_view);
            GridHelper.SetupReadOnly(_view);
            Build();
        }

        private void Build()
        {
            var top = new PanelControl { Dock = DockStyle.Top, Height = 132, BorderStyle = DevExpress.XtraEditors.Controls.BorderStyles.NoBorder };
            var title = new LabelControl { Text = "Açık Portlar", Location = new Point(12, 8) };
            title.Appearance.Font = new Font("Segoe UI", 13F);
            title.Appearance.Options.UseFont = true;
            var info = new LabelControl
            {
                Location = new Point(12, 36),
                AutoSizeMode = LabelAutoSizeMode.None,
                Size = new Size(1000, 36),
                Text = "Sunucuda dinleyen portlar ve Windows Firewall'da kime açık oldukları. Sadece gösterilir, hiçbir port kapatılmaz. "
                       + "\"Herkese açık\" bir port modemde yönlendirilmişse internetten erişilebilir; kullanılmayanları kapatın, gerekenleri Port Kısıtları ile sınırlayın."
            };
            info.Appearance.TextOptions.WordWrap = DevExpress.Utils.WordWrap.Wrap;
            info.Appearance.Options.UseTextOptions = true;
            top.Resize += (s, e) => info.Width = Math.Max(300, top.Width - 24);

            _warning.Location = new Point(12, 76);
            _warning.Appearance.ForeColor = UiColors.Error;
            _warning.Appearance.Options.UseForeColor = true;

            var refresh = new SimpleButton { Text = "Yenile", Location = new Point(12, 98), Size = new Size(80, 26) };
            refresh.Click += async (s, e) => await LoadAsync();
            _restrict.Location = new Point(98, 98);
            _restrict.Click += (s, e) => Restrict();
            _showAll.Location = new Point(250, 101);
            _showAll.Properties.Caption = "Kapalı ve sadece yerel portları da göster";
            _showAll.CheckedChanged += (s, e) => Bind();
            top.Controls.AddRange(new Control[] { title, info, _warning, refresh, _restrict, _showAll });

            var advicePanel = new PanelControl { Dock = DockStyle.Bottom, Height = 110, BorderStyle = DevExpress.XtraEditors.Controls.BorderStyles.NoBorder };
            _advice.Properties.ReadOnly = true;
            var adviceTitle = new LabelControl { Text = "Seçili port", Dock = DockStyle.Top, Padding = new Padding(12, 6, 0, 4) };
            advicePanel.Controls.Add(_advice);
            advicePanel.Controls.Add(adviceTitle);

            var bottom = new PanelControl { Dock = DockStyle.Bottom, Height = 28, BorderStyle = DevExpress.XtraEditors.Controls.BorderStyles.NoBorder };
            _status.Location = new Point(12, 6);
            bottom.Controls.Add(_status);

            Controls.Add(_grid);
            Controls.Add(top);
            Controls.Add(advicePanel);
            Controls.Add(bottom);

            _view.FocusedRowChanged += (s, e) => ShowSelected();
            _view.RowStyle += (s, e) =>
            {
                var row = _view.GetRow(e.RowHandle) as Row;
                if (row == null) return;
                if (row.Risk == "Yüksek") e.Appearance.ForeColor = UiColors.Error;
                else if (row.Risk == "Orta") e.Appearance.ForeColor = UiColors.Warning;
                else if (row.Exposure == "Kapalı" || row.Exposure == "Sadece bu sunucu") e.Appearance.ForeColor = DevExpress.LookAndFeel.DXSkinColors.ForeColors.DisabledText;
            };
        }

        protected override Task OnFirstShowAsync()
        {
            return LoadAsync();
        }

        private async Task LoadAsync()
        {
            var state = await CallAsync(c => c.SendAsync<OpenPortsDto>(Commands.OpenPortsGet, null, TimeSpan.FromMinutes(1)));
            if (state == null) return;
            _state = state;
            Bind();
        }

        private void Bind()
        {
            if (_state == null) return;
            var rows = _state.Ports.Select(p => new Row(p))
                .Where(r => _showAll.Checked || (r.Exposure != "Kapalı" && r.Exposure != "Sadece bu sunucu"))
                .OrderBy(r => r.Risk == "Yüksek" ? 0 : r.Risk == "Orta" ? 1 : r.Risk == "Düşük" ? 2 : 3)
                .ThenByDescending(r => r.Item.Failures24h).ThenBy(r => r.Item.Protocol).ThenBy(r => r.Item.Port).ToList();
            _grid.DataSource = rows;
            _view.BestFitColumns();
            if (_view.Columns["Detail"] != null) _view.Columns["Detail"].Width = Math.Min(_view.Columns["Detail"].Width, 380);

            _warning.Text = _state.FirewallWarning ?? "";
            _warning.Visible = _state.FirewallWarning != null;
            int open = _state.Ports.Count(p => p.Exposure == "Herkese açık"), high = _state.Ports.Count(p => p.Risk == "Yüksek");
            var at = UtcTime.ToLocal(_state.GeneratedUtc);
            _status.Text = _state.Ports.Count + " dinleyen port, " + open + " herkese açık" + (high > 0 ? ", " + high + " yüksek riskli" : "") + "."
                           + (_state.WatchedLogs.Count > 0 ? " İzlenen IIS logları: " + string.Join(", ", _state.WatchedLogs) + "." : "")
                           + (at == null ? "" : " (" + at.Value.ToString("HH:mm:ss") + ")");
            ShowSelected();
        }

        private Row Selected()
        {
            return _view.GetRow(_view.FocusedRowHandle) as Row;
        }

        private void ShowSelected()
        {
            var r = Selected();
            _restrict.Enabled = r != null && r.Item.CanRestrict;
            if (r == null)
            {
                _advice.Text = "";
                return;
            }
            var p = r.Item;
            var lines = new List<string>
            {
                p.Protocol + " " + p.Port + (string.IsNullOrEmpty(p.Label) ? "" : " - " + p.Label) + " | dinleyen: " + p.Addresses,
                "Program: " + p.Process + (string.IsNullOrEmpty(p.ProcessPath) ? "" : " (" + p.ProcessPath + ")") + (string.IsNullOrEmpty(p.Services) ? "" : " | Servis: " + p.Services),
                "Firewall: " + p.Exposure + " - " + p.ExposureDetail
            };
            if (p.Failures24h > 0) lines.Add("Son 24 saatte bu protokolde " + p.Failures24h + " başarısız giriş.");
            if (!string.IsNullOrEmpty(p.Advice)) lines.Add("Öneri: " + p.Advice);
            _advice.Text = string.Join(Environment.NewLine, lines);
        }

        private void Restrict()
        {
            var r = Selected();
            if (r == null || !r.Item.CanRestrict) return;
            var p = r.Item;
            string protocol = p.Protocol;
            // RDP: TCP + UDP birlikte
            if (_state.Ports.Any(x => x.Port == p.Port && x.Protocol != p.Protocol && x.Label != null && x.Label.StartsWith("RDP"))) protocol = "BOTH";
            Host.Navigate(DashboardPages.PortRestrictions); // önce sayfa açılır, form onun üstünde açılsın
            PortRestrictionsModule.RequestNew(new PortRestrictionDto
            {
                Name = (string.IsNullOrEmpty(p.Label) ? p.Process : p.Label) + " - " + p.Port,
                Protocol = protocol,
                Ports = p.Port.ToString(),
                AllowLan = true,
                Enabled = true,
                Note = "Açık Portlar sayfasından eklendi."
            });
        }

        public sealed class Row
        {
            public Row(OpenPortDto item)
            {
                Item = item;
            }

            internal OpenPortDto Item { get; private set; }

            [System.ComponentModel.DisplayName("Port")]
            public string Port { get { return Item.Protocol + " " + Item.Port; } }

            [System.ComponentModel.DisplayName("Kullanım")]
            public string Label { get { return Item.Label; } }

            [System.ComponentModel.DisplayName("Program")]
            public string Process { get { return Item.Process; } }

            [System.ComponentModel.DisplayName("Servis")]
            public string Services { get { return Item.Services; } }

            [System.ComponentModel.DisplayName("Firewall")]
            public string Exposure { get { return Item.Exposure; } }

            [System.ComponentModel.DisplayName("Risk")]
            public string Risk { get { return Item.Risk; } }

            [System.ComponentModel.DisplayName("24 saatte hatalı giriş")]
            public string Failures { get { return Item.Failures24h < 0 ? "" : Item.Failures24h.ToString(); } }

            [System.ComponentModel.DisplayName("Ayrıntı")]
            public string Detail { get { return Item.ExposureDetail; } }
        }
    }
}
