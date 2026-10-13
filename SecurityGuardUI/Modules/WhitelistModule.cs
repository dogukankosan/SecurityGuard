using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Forms;
using DevExpress.LookAndFeel;
using DevExpress.XtraGrid.Views.Grid;
using SecurityGuard.Shared;
using SecurityGuard.Shared.Contracts;
using SecurityGuard.Shared.Dto;
using SecurityGuardUI.Forms;
using SecurityGuardUI.Helpers;

namespace SecurityGuardUI.Modules
{
    /// <summary>
    /// Beyaz liste: buradaki adresler asla banlanmaz (loglanır, kurala göre mail atılır).
    /// Sistem kayıtları (iç ağ, localhost, sunucu IP'leri) servis tarafından eklenir ve silinemez.
    /// </summary>
    public partial class WhitelistModule : ModuleBase
    {
        public WhitelistModule()
        {
            InitializeComponent();
            GridHelper.SetupReadOnly(gridView, multiSelect: true);
        }

        protected override Task OnFirstShowAsync()
        {
            return LoadAsync();
        }

        private async Task LoadAsync()
        {
            var entries = await CallAsync(c => c.SendAsync<List<WhitelistEntry>>(Commands.WhitelistList));
            if (entries == null) return;

            gridControl.DataSource = entries.Select(e => new Row
            {
                Id = e.Id,
                Cidr = e.Cidr,
                Note = e.Note,
                IsSystem = e.IsSystem,
                Type = e.IsSystem ? "Sistem" : "Kullanıcı",
                Created = UtcTime.ToLocal(e.CreatedUtc)
            }).ToList();

            int user = entries.Count(e => !e.IsSystem);
            labelStatus.Text = entries.Count + " kayıt (" + user + " kullanıcı, " + (entries.Count - user) + " sistem)";
        }

        private async void buttonAdd_Click(object sender, EventArgs e)
        {
            using (var form = new WhitelistAddForm(Client))
            {
                var result = form.ShowDialog(FindForm());
                if (result == DialogResult.Abort)
                {
                    Host.HandleSessionExpired();
                    return;
                }
                if (result == DialogResult.OK)
                    await LoadAsync();
            }
        }

        private async void buttonDelete_Click(object sender, EventArgs e)
        {
            var selected = gridView.GetSelectedRows()
                .Where(h => h >= 0)
                .Select(h => (Row)gridView.GetRow(h))
                .Where(r => r != null)
                .ToList();

            if (selected.Count == 0)
            {
                Ui.Warning(FindForm(), "Silinecek kayıtları seçin.");
                return;
            }
            if (selected.Any(r => r.IsSystem))
            {
                Ui.Warning(FindForm(), "Sistem kayıtları (iç ağ, localhost, sunucu IP'leri) silinemez. Seçimden çıkarıp tekrar deneyin.");
                return;
            }

            string list = string.Join(Environment.NewLine, selected.Take(10).Select(r => "  " + r.Cidr));
            if (selected.Count > 10) list += Environment.NewLine + "  ... ve " + (selected.Count - 10) + " kayıt daha";
            if (!Ui.Confirm(FindForm(), selected.Count + " kayıt beyaz listeden silinsin mi?" + Environment.NewLine + Environment.NewLine + list
                + Environment.NewLine + Environment.NewLine + "Silinen adresler bundan sonra kurala göre banlanabilir."))
                return;

            var request = new IdListRequest { Ids = selected.Select(r => r.Id).ToList() };
            if (await CallAsync(c => c.SendAsync(Commands.WhitelistDelete, request)))
                await LoadAsync();
        }

        private async void buttonRefresh_Click(object sender, EventArgs e)
        {
            await LoadAsync();
        }

        private void gridView_RowStyle(object sender, RowStyleEventArgs e)
        {
            var row = gridView.GetRow(e.RowHandle) as Row;
            if (row != null && row.IsSystem)
                e.Appearance.ForeColor = DXSkinColors.ForeColors.DisabledText;
        }

        public sealed class Row
        {
            public long Id { get; set; }
            public string Cidr { get; set; }
            public string Note { get; set; }
            public bool IsSystem { get; set; }
            public string Type { get; set; }
            public DateTime? Created { get; set; }
        }
    }
}
