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
using SecurityGuardUI.Forms;
using SecurityGuardUI.Helpers;

namespace SecurityGuardUI.Modules
{
    /// <summary>
    /// RDP Oturumları: kim, hangi IP / ülkeden, ne zaman bağlandı, ne kadar kaldı (denetim için Excel'e aktarılabilir).
    /// Mesai dışı girişler işaretlenir; kişiye özel mesai istisnaları buradan yönetilir (genel mesai: Genel Ayarlar).
    /// </summary>
    public sealed class RdpSessionsModule : ModuleBase
    {
        private readonly GridControl _grid = new GridControl { Dock = DockStyle.Fill };
        private readonly GridView _view;
        private readonly DateEdit _from = new DateEdit { Width = 110 };
        private readonly DateEdit _to = new DateEdit { Width = 110 };
        private readonly TextEdit _user = new TextEdit { Width = 150 };
        private readonly CheckEdit _offOnly = new CheckEdit { Width = 130 };
        private readonly CheckEdit _activeOnly = new CheckEdit { Width = 150 };
        private readonly CheckEdit _console = new CheckEdit { Width = 170, Checked = true };
        private RdpSessionResult _last;
        private readonly LabelControl _work = new LabelControl();
        private readonly LabelControl _status = new LabelControl();

        public RdpSessionsModule()
        {
            _view = new GridView(_grid);
            _grid.MainView = _view;
            _grid.ViewCollection.Add(_view);
            GridHelper.SetupReadOnly(_view);
            Build();
        }

        private void Build()
        {
            var top = new PanelControl { Dock = DockStyle.Top, Height = 110, BorderStyle = DevExpress.XtraEditors.Controls.BorderStyles.NoBorder };
            var title = new LabelControl { Text = "RDP Oturumları", Location = new Point(12, 8) };
            title.Appearance.Font = new Font("Segoe UI", 13F);
            title.Appearance.Options.UseFont = true;
            _work.Location = new Point(12, 38);

            int x = 12, y = 66;
            Action<string, Control, int> add = (label, control, width) =>
            {
                if (label != null)
                {
                    var l = new LabelControl { Text = label, Location = new Point(x, y + 4) };
                    top.Controls.Add(l);
                    x += l.PreferredSize.Width + 6;
                }
                control.Location = new Point(x, y + 1);
                if (width > 0) control.Width = width;
                top.Controls.Add(control);
                x += control.Width + 8;
            };
            _from.DateTime = DateTime.Today.AddDays(-7);
            _to.DateTime = DateTime.Today;
            add("Başlangıç", _from, 110);
            add("Bitiş", _to, 110);
            add("Kullanıcı", _user, 150);
            _offOnly.Properties.Caption = "Sadece mesai dışı";
            add(null, _offOnly, 130);
            _activeOnly.Properties.Caption = "Sadece şu an bağlı";
            _activeOnly.CheckedChanged += (s, e) => { if (_last != null) Bind(_last); };
            add(null, _activeOnly, 150);
            _console.Properties.Caption = "Konsol girişlerini göster";
            add(null, _console, 170);
            var list = new SimpleButton { Text = "Listele", Size = new Size(80, 26) };
            list.Click += async (s, e) => await LoadAsync();
            add(null, list, 80);
            var excel = new SimpleButton { Text = "Excel'e aktar", Size = new Size(100, 26) };
            excel.Click += (s, e) => GridHelper.ExportToExcel(FindForm(), _grid, "RdpOturumlari");
            add(null, excel, 100);
            var hours = new SimpleButton { Text = "Mesai istisnaları...", Size = new Size(130, 26) };
            hours.Click += async (s, e) => await EditWorkHoursAsync();
            add(null, hours, 130);
            _user.KeyDown += async (s, e) => { if (e.KeyCode == Keys.Enter) await LoadAsync(); };

            top.Controls.Add(title);
            top.Controls.Add(_work);

            var bottom = new PanelControl { Dock = DockStyle.Bottom, Height = 46, BorderStyle = DevExpress.XtraEditors.Controls.BorderStyles.NoBorder };
            _status.Location = new Point(12, 6);
            _status.AutoSizeMode = LabelAutoSizeMode.None;
            _status.Size = new Size(1100, 36);
            _status.Appearance.TextOptions.WordWrap = DevExpress.Utils.WordWrap.Wrap;
            _status.Appearance.Options.UseTextOptions = true;
            bottom.Resize += (s, e) => _status.Width = Math.Max(300, bottom.Width - 24);
            bottom.Controls.Add(_status);

            Controls.Add(_grid);
            Controls.Add(top);
            Controls.Add(bottom);

            _view.RowStyle += (s, e) =>
            {
                var row = _view.GetRow(e.RowHandle) as Row;
                if (row == null) return;
                if (row.Item.OffHours) e.Appearance.ForeColor = UiColors.Warning;
                else if (row.State == "Açık") e.Appearance.ForeColor = UiColors.Success;
            };
        }

        protected override Task OnFirstShowAsync()
        {
            return LoadAsync();
        }

        private async Task LoadAsync()
        {
            var q = new RdpSessionQuery
            {
                FromUtc = UtcTime.ToText(_from.DateTime.Date),
                ToUtc = UtcTime.ToText(_to.DateTime.Date.AddDays(1)),
                User = _user.Text,
                OnlyOffHours = _offOnly.Checked,
                IncludeConsole = _console.Checked
            };
            var result = await CallAsync(c => c.SendAsync<RdpSessionResult>(Commands.RdpSessionsList, q));
            if (result != null) Bind(result);
        }

        private static bool IsActive(RdpSessionDto r)
        {
            return r.State == "Açık" || r.State == "Bağlantı kopuk";
        }

        private void Bind(RdpSessionResult result)
        {
            _last = result;
            var rows = _activeOnly.Checked ? result.Rows.Where(IsActive).ToList() : result.Rows;
            _grid.DataSource = rows.Select(r => new Row(r)).ToList();
            _view.BestFitColumns();
            _work.Text = "Genel mesai: " + result.WorkHours + " (değiştirmek için Genel Ayarlar → Mesai saatleri). Turuncu satırlar mesai dışı, yeşil = şu an bağlı.";

            var perUser = rows.GroupBy(r => r.User).Select(g => new { User = g.Key, Seconds = g.Sum(r => r.DurationSeconds), Count = g.Count() })
                .OrderByDescending(u => u.Seconds).ToList();
            _status.Text = rows.Count + " oturum" + (result.Truncated ? " (ilk 5000; aralığı daraltın)" : "")
                           + (_activeOnly.Checked ? " (sadece şu an bağlı)" : "")
                           + ", mesai dışı: " + rows.Count(r => r.OffHours) + ", şu an bağlı: " + rows.Count(IsActive)
                           + (perUser.Count == 0 ? "" : ".  Kullanıcı başına süre: " + string.Join(", ", perUser.Take(8).Select(u => u.User + " " + Duration(u.Seconds) + " (" + u.Count + ")")));
        }

        private async Task EditWorkHoursAsync()
        {
            var dto = await CallAsync(c => c.SendAsync<WorkHoursDto>(Commands.WorkHoursGet));
            if (dto == null) return;
            using (var form = new WorkHoursForm(dto))
            {
                if (form.ShowDialog(FindForm()) != DialogResult.OK) return;
                var saved = await CallAsync(c => c.SendAsync<WorkHoursDto>(Commands.WorkHoursSave, new WorkHoursDto { Exceptions = form.Exceptions }));
                if (saved != null) Ui.Info(FindForm(), "Mesai istisnaları kaydedildi (" + saved.Exceptions.Count + " kişi).");
            }
        }

        internal static string Duration(long seconds)
        {
            var t = TimeSpan.FromSeconds(seconds);
            if (t.TotalDays >= 1) return (int)t.TotalDays + "g " + t.Hours + "sa";
            if (t.TotalHours >= 1) return (int)t.TotalHours + "sa " + t.Minutes + "dk";
            return Math.Max(0, t.Minutes) + "dk";
        }

        public sealed class Row
        {
            public Row(RdpSessionDto item)
            {
                Item = item;
            }

            internal RdpSessionDto Item { get; private set; }

            [System.ComponentModel.DisplayName("Kullanıcı")]
            public string User { get { return Item.User; } }

            [System.ComponentModel.DisplayName("IP")]
            public string Ips { get { return (Item.Ips ?? Item.ClientIp ?? "").Replace(",", ", "); } }

            [System.ComponentModel.DisplayName("Ülke")]
            public string Country { get { return Item.Country; } }

            [System.ComponentModel.DisplayName("Başlangıç")]
            public string Start { get { var t = UtcTime.ToLocal(Item.StartUtc); return t == null ? "" : t.Value.ToString("dd.MM.yyyy HH:mm") + (Item.StartUnknown ? " (önce açılmış)" : ""); } }

            [System.ComponentModel.DisplayName("Bitiş")]
            public string End { get { var t = UtcTime.ToLocal(Item.EndUtc); return t == null ? "" : t.Value.ToString("dd.MM.yyyy HH:mm"); } }

            [System.ComponentModel.DisplayName("Süre")]
            public string Duration { get { return RdpSessionsModule.Duration(Item.DurationSeconds) + (Item.State == "Açık" || Item.State == "Bağlantı kopuk" ? " (sürüyor)" : ""); } }

            [System.ComponentModel.DisplayName("Durum")]
            public string State { get { return Item.State; } }

            [System.ComponentModel.DisplayName("Kopma")]
            public int Disconnects { get { return Item.Disconnects; } }

            [System.ComponentModel.DisplayName("Yeniden bağlanma")]
            public int Reconnects { get { return Item.Reconnects; } }

            [System.ComponentModel.DisplayName("Mesai dışı")]
            public string OffHours { get { return Item.OffHours ? "Evet" : ""; } }
        }
    }
}
