using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Forms;
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
    /// Ban Listesi: banlı IP'ler (firewall'da tüm portlardan engelli) ve IP başına giriş deneme sayaçları.
    /// Ban kaldırılınca o IP'nin başarısız deneme sayacı da sıfırlanır (yoksa bir sonraki hatada tekrar banlanırdı).
    /// </summary>
    public partial class BanListModule : ModuleBase
    {
        public BanListModule()
        {
            InitializeComponent();
            GridHelper.SetupReadOnly(viewBanned, true);
            GridHelper.SetupReadOnly(viewAttempts, true);
            viewBanned.SelectionChanged += (s, e) => UpdateButtons();
            viewAttempts.SelectionChanged += (s, e) => UpdateButtons();
            UpdateButtons();
        }

        protected override Task OnFirstShowAsync()
        {
            return LoadAsync();
        }

        private async Task LoadAsync()
        {
            var result = await CallAsync(c => c.SendAsync<BanListResult>(Commands.BanList));
            if (result != null) Bind(result);
        }

        private void Bind(BanListResult r)
        {
            gridBanned.DataSource = r.Banned.Select(b => new BannedRow
            {
                IpAddress = b.IpAddress,
                Protocol = string.IsNullOrEmpty(b.Protocol) ? "Tümü" : b.Protocol,
                Reason = b.Reason,
                FailCount = b.FailCount > 0 ? (int?)b.FailCount : null,
                Kind = b.IsManual ? "Elle" : "Otomatik",
                RuleName = b.RuleName,
                Time = UtcTime.ToLocal(b.BannedUtc)
            }).ToList();

            gridAttempts.DataSource = r.Attempts.Select(a => new AttemptRow
            {
                IpAddress = a.IpAddress,
                Protocol = a.Protocol,
                FailCount = a.FailCount,
                Threshold = a.Threshold,
                SuccessCount = a.SuccessCount,
                LastUserName = a.LastUserName,
                First = UtcTime.ToLocal(a.FirstUtc),
                Last = UtcTime.ToLocal(a.LastUtc),
                State = a.IsBanned ? "Banlı" : a.IsProtected ? "Korunan (banlanmaz)" : "",
                IsBanned = a.IsBanned
            }).ToList();

            tabBanned.Text = "Banlı IP'ler (" + r.Banned.Count + ")";
            tabAttempts.Text = "Giriş Denemeleri (" + r.Attempts.Count + ")";

            labelFirewall.Text = r.FirewallMessage;
            labelFirewall.Appearance.ForeColor = r.FirewallOk ? UiColors.Text : UiColors.Error;
            UpdateButtons();
        }

        // ================================================================ Seçim

        private bool OnBannedTab { get { return tabControl.SelectedTabPage == tabBanned; } }

        private List<string> SelectedBannedIps()
        {
            if (OnBannedTab)
                return viewBanned.GetSelectedRows().Select(h => viewBanned.GetRow(h)).OfType<BannedRow>().Select(r => r.IpAddress).ToList();
            return SelectedAttempts().Where(a => a.IsBanned).Select(a => a.IpAddress).Distinct().ToList();
        }

        private List<AttemptRow> SelectedAttempts()
        {
            return OnBannedTab
                ? new List<AttemptRow>()
                : viewAttempts.GetSelectedRows().Select(h => viewAttempts.GetRow(h)).OfType<AttemptRow>().ToList();
        }

        private void UpdateButtons()
        {
            buttonUnban.Enabled = SelectedBannedIps().Count > 0;
            buttonResetAttempts.Enabled = !OnBannedTab && SelectedAttempts().Count > 0;
        }

        private void tabControl_SelectedPageChanged(object sender, DevExpress.XtraTab.TabPageChangedEventArgs e)
        {
            UpdateButtons();
        }

        private void viewAttempts_RowCellStyle(object sender, RowCellStyleEventArgs e)
        {
            if (e.Column != colAttState && e.Column != colAttFail) return;
            var row = viewAttempts.GetRow(e.RowHandle) as AttemptRow;
            if (row == null) return;
            if (row.IsBanned) e.Appearance.ForeColor = UiColors.Error;
            else if (e.Column == colAttFail && row.Threshold > 0 && row.FailCount >= row.Threshold * 0.7 && row.State.Length == 0)
                e.Appearance.ForeColor = UiColors.Warning; // eşiğe yaklaşıyor
        }

        // ================================================================ Butonlar

        private void buttonBan_Click(object sender, EventArgs e)
        {
            string preset = null;
            if (!OnBannedTab)
            {
                var focused = viewAttempts.GetFocusedRow() as AttemptRow;
                if (focused != null && !focused.IsBanned) preset = focused.IpAddress;
            }

            using (var form = new BanAddForm(Client, preset))
            {
                var result = form.ShowDialog(FindForm());
                if (result == DialogResult.Abort)
                {
                    Host.HandleSessionExpired();
                    return;
                }
                if (result == DialogResult.OK && form.Result != null)
                {
                    Bind(form.Result);
                    tabControl.SelectedTabPage = tabBanned;
                }
            }
        }

        private async void buttonUnban_Click(object sender, EventArgs e)
        {
            var ips = SelectedBannedIps();
            if (ips.Count == 0) return;

            string text = ips.Count == 1 ? ips[0] + " adresinin banı kaldırılsın mı?" : ips.Count + " IP'nin banı kaldırılsın mı?";
            text += Environment.NewLine + Environment.NewLine + "Başarısız deneme sayacı da sıfırlanır (yoksa bir sonraki hatalı denemede tekrar banlanır).";
            if (!Ui.Confirm(FindForm(), text)) return;

            var result = await CallAsync(c => c.SendAsync<BanListResult>(Commands.BanRemove, new IpListRequest { IpAddresses = ips, ResetCounter = true }));
            if (result != null) Bind(result);
        }

        private async void buttonResetAttempts_Click(object sender, EventArgs e)
        {
            var ips = SelectedAttempts().Select(a => a.IpAddress).Distinct().ToList();
            if (ips.Count == 0) return;
            if (!Ui.Confirm(FindForm(), ips.Count + " IP'nin deneme sayaçları (RDP ve SQL) silinsin mi?" + Environment.NewLine +
                                        "Ban durumu değişmez; olaylar Olay Logu'nda kalır.")) return;

            var result = await CallAsync(c => c.SendAsync<BanListResult>(Commands.BanResetAttempts, new IpListRequest { IpAddresses = ips }));
            if (result != null) Bind(result);
        }

        private async void buttonSync_Click(object sender, EventArgs e)
        {
            var result = await CallAsync(c => c.SendAsync<BanListResult>(Commands.BanSyncFirewall, null, TimeSpan.FromMinutes(2)));
            if (result == null) return;
            Bind(result);
            if (result.FirewallOk) Ui.Info(FindForm(), result.FirewallMessage);
            else Ui.Error(FindForm(), result.FirewallMessage);
        }

        private async void buttonRefresh_Click(object sender, EventArgs e)
        {
            await LoadAsync();
        }

        private void buttonExport_Click(object sender, EventArgs e)
        {
            if (OnBannedTab) GridHelper.ExportToExcel(FindForm(), gridBanned, "BanListesi");
            else GridHelper.ExportToExcel(FindForm(), gridAttempts, "GirisDenemeleri");
        }

        // ================================================================ Satırlar

        public sealed class BannedRow
        {
            public string IpAddress { get; set; }
            public string Protocol { get; set; }
            public string Reason { get; set; }
            public int? FailCount { get; set; }
            public string Kind { get; set; }
            public string RuleName { get; set; }
            public DateTime? Time { get; set; }
        }

        public sealed class AttemptRow
        {
            public string IpAddress { get; set; }
            public string Protocol { get; set; }
            public int FailCount { get; set; }
            public int Threshold { get; set; }
            public int SuccessCount { get; set; }
            public string LastUserName { get; set; }
            public DateTime? First { get; set; }
            public DateTime? Last { get; set; }
            public string State { get; set; }
            internal bool IsBanned { get; set; }
        }
    }
}
