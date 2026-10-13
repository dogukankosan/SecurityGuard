using System;
using System.Linq;
using System.Threading.Tasks;
using SecurityGuard.Shared;
using SecurityGuard.Shared.Contracts;
using SecurityGuard.Shared.Dto;
using SecurityGuardUI.Helpers;

namespace SecurityGuardUI.Modules
{
    /// <summary>İşlem Kaydı: UI'den yapılan her işlem (giriş, çıkış, ayar değişikliği, ban kaldırma...).</summary>
    public partial class AuditTrailModule : ModuleBase
    {
        private const int MaxRows = 5000;

        public AuditTrailModule()
        {
            InitializeComponent();
            GridHelper.SetupReadOnly(gridView);
            dateFrom.DateTime = DateTime.Today.AddDays(-7);
            dateTo.DateTime = DateTime.Today.AddDays(1).AddMinutes(-1);
        }

        protected override Task OnFirstShowAsync()
        {
            return LoadAsync();
        }

        private async Task LoadAsync()
        {
            if (dateFrom.DateTime > dateTo.DateTime)
            {
                Ui.Warning(FindForm(), "Başlangıç tarihi bitiş tarihinden sonra olamaz.");
                return;
            }

            var query = new AuditTrailQuery
            {
                FromUtc = UtcTime.ToText(dateFrom.DateTime),
                ToUtc = UtcTime.ToText(dateTo.DateTime),
                Search = textSearch.Text,
                MaxRows = MaxRows
            };

            var result = await CallAsync(c => c.SendAsync<AuditTrailResult>(Commands.AuditTrailQuery, query));
            if (result == null) return;

            gridControl.DataSource = result.Rows.Select(r => new Row
            {
                Time = UtcTime.ToLocal(r.TimeUtc),
                UserName = r.UserName,
                Action = r.Action,
                Detail = r.Detail
            }).ToList();

            labelStatus.Text = result.Truncated
                ? "En yeni " + MaxRows + " kayıt gösteriliyor. Daha eskileri için tarih aralığını daraltın."
                : result.Rows.Count + " kayıt";
        }

        private async void buttonSearch_Click(object sender, EventArgs e)
        {
            await LoadAsync();
        }

        private void buttonExport_Click(object sender, EventArgs e)
        {
            GridHelper.ExportToExcel(FindForm(), gridControl, "IslemKaydi");
        }

        private async void textSearch_KeyDown(object sender, System.Windows.Forms.KeyEventArgs e)
        {
            if (e.KeyCode != System.Windows.Forms.Keys.Enter) return;
            e.SuppressKeyPress = true;
            await LoadAsync();
        }

        /// <summary>Tablo satırı (zaman yerel saate çevrilmiş).</summary>
        public sealed class Row
        {
            public DateTime? Time { get; set; }
            public string UserName { get; set; }
            public string Action { get; set; }
            public string Detail { get; set; }
        }
    }
}
