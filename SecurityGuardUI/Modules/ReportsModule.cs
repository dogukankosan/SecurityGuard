using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using DevExpress.XtraGrid.Views.Base;
using SecurityGuard.Shared;
using SecurityGuard.Shared.Contracts;
using SecurityGuard.Shared.Dto;
using SecurityGuardUI.Helpers;

namespace SecurityGuardUI.Modules
{
    /// <summary>
    /// Günlük rapor: solda gönderilen raporlar, sağda seçilenin içeriği (mailde görüneceği gibi).
    /// "Önizle" şu anki durumla raporu hazırlar ama kaydetmez / göndermez; "Şimdi Gönder" hazırlayıp kuyruğa alır.
    /// Saat: Genel Ayarlar → Rapor saati. Alıcılar ve açık / kapalı: Olay Kuralları → Günlük güvenlik ve performans raporu.
    /// </summary>
    public partial class ReportsModule : ModuleBase
    {
        public ReportsModule()
        {
            InitializeComponent();
            GridHelper.SetupReadOnly(viewReports);
            viewReports.OptionsView.ShowAutoFilterRow = false;

            buttonPreview.Text = "Günlük: önizle";
            buttonSend.Text = "Günlük: şimdi gönder...";
            var monthlyPreview = new DevExpress.XtraEditors.SimpleButton { Text = "Aylık: önizle", Size = buttonPreview.Size, Location = new System.Drawing.Point(buttonPreview.Left, 46),
                Anchor = buttonPreview.Anchor, ToolTip = "Önceki ayın yönetici raporunu hazırlar; kaydetmez, göndermez." };
            var monthlySend = new DevExpress.XtraEditors.SimpleButton { Text = "Aylık: şimdi gönder...", Size = buttonSend.Size, Location = new System.Drawing.Point(buttonSend.Left, 46), Anchor = buttonSend.Anchor };
            monthlyPreview.Click += async (s, e) =>
            {
                var answer = Ui.YesNoCancel(FindForm(), "Hangi ay önizlensin?" + Environment.NewLine + Environment.NewLine
                                                        + "Evet: önceki ay (1'inde gönderilecek rapor)" + Environment.NewLine + "Hayır: bu ay (şimdiye kadar)");
                if (answer == System.Windows.Forms.DialogResult.Cancel) return;
                bool current = answer == System.Windows.Forms.DialogResult.No;
                var report = await CallAsync(c => c.SendAsync<ReportDto>(Commands.ReportMonthlyPreview, new IdRequest { Id = current ? 1 : 0 }, TimeSpan.FromMinutes(2)));
                if (report == null) return;
                Show(report, "ÖNİZLEME (kaydedilmedi, gönderilmedi): ");
                labelStatus.Text = "Aylık rapor önizlemesi: " + (current ? "bu ay (şimdiye kadar)." : "önceki ay.");
                labelStatus.Appearance.ForeColor = UiColors.Text;
            };
            monthlySend.Click += async (s, e) =>
            {
                if (!Ui.Confirm(FindForm(), "Önceki ayın yönetici raporu şimdi hazırlanıp \"Aylık yönetici raporu\" kuralının alıcılarına gönderilsin mi?")) return;
                var report = await CallAsync(c => c.SendAsync<ReportDto>(Commands.ReportMonthlySendNow, null, TimeSpan.FromMinutes(2)));
                if (report == null) return;
                await LoadAsync(report.Id);
                labelStatus.Text = report.Mailed ? "Aylık rapor mail kuyruğuna alındı." : "Aylık rapor kaydedildi ama gönderilmedi: " + report.MailNote + ".";
                labelStatus.Appearance.ForeColor = report.Mailed ? UiColors.Success : UiColors.Warning;
            };
            panelTop.Controls.Add(monthlyPreview);
            panelTop.Controls.Add(monthlySend);
            labelInfo.Text = "Günlük: her gün rapor saatinde teknik rapor. Aylık: her ayın 1'inde yöneticiye sade özet (Genel Ayarlar → Rapor saatleri). "
                             + "Alıcılar: Olay Kuralları → \"Günlük güvenlik ve performans raporu\" / \"Aylık yönetici raporu\".";
        }

        protected override Task OnFirstShowAsync()
        {
            return LoadAsync(0);
        }

        private async Task LoadAsync(long focusId)
        {
            var list = await CallAsync(c => c.SendAsync<List<ReportDto>>(Commands.ReportList));
            if (list == null) return;
            var rows = list.Select(r => new Row(r)).ToList();
            gridReports.DataSource = rows;
            int i = focusId > 0 ? rows.FindIndex(r => r.Id == focusId) : 0;
            if (rows.Count > 0) viewReports.FocusedRowHandle = viewReports.GetRowHandle(Math.Max(0, i));
            labelStatus.Text = rows.Count == 0 ? "Henüz rapor yok. İlk rapor rapor saatinde hazırlanır; \"Önizle\" ile şimdiden görebilirsiniz." : rows.Count + " rapor";
            labelStatus.Appearance.ForeColor = UiColors.Text;
            await ShowSelectedAsync();
        }

        private async void viewReports_FocusedRowChanged(object sender, FocusedRowChangedEventArgs e)
        {
            await ShowSelectedAsync();
        }

        private async Task ShowSelectedAsync()
        {
            var row = viewReports.GetFocusedRow() as Row;
            if (row == null)
            {
                Show(null, null);
                return;
            }
            var result = await Client.SendAsync<ReportDto>(Commands.ReportGet, new IdRequest { Id = row.Id });
            if (IsDisposed || viewReports.GetFocusedRow() != row) return;
            if (result.IsUnauthorized)
            {
                Host.HandleSessionExpired();
                return;
            }
            if (result.Success) Show(result.Value, null);
        }

        private void Show(ReportDto report, string note)
        {
            labelReport.Text = report == null ? "" : (note ?? "") + report.Subject
                + (report.Mailed ? "   (mail gönderildi)" : report.MailNote != null ? "   (gönderilmedi: " + report.MailNote + ")" : "");
            web.DocumentText = report == null ? "<html><body></body></html>"
                : "<html><head><meta charset=\"utf-8\"><meta http-equiv=\"X-UA-Compatible\" content=\"IE=edge\"></head><body style=\"margin:12px;background:#fff\">"
                  + report.Html + "</body></html>";
        }

        private async void buttonPreview_Click(object sender, EventArgs e)
        {
            var report = await CallAsync(c => c.SendAsync<ReportDto>(Commands.ReportPreview, null, TimeSpan.FromMinutes(2)));
            if (report == null) return;
            Show(report, "ÖNİZLEME (kaydedilmedi, gönderilmedi): ");
            labelStatus.Text = "Önizleme hazırlandı: şu anki durumla, son rapordan bu yana olan dönem.";
            labelStatus.Appearance.ForeColor = UiColors.Text;
        }

        private async void buttonSend_Click(object sender, EventArgs e)
        {
            if (!Ui.Confirm(FindForm(), "Günlük rapor şimdi hazırlanıp alıcılara gönderilsin mi?" + Environment.NewLine
                                        + "(Akşamki zamanlanmış rapor yine gönderilir.)"))
                return;
            var report = await CallAsync(c => c.SendAsync<ReportDto>(Commands.ReportSendNow, null, TimeSpan.FromMinutes(2)));
            if (report == null) return;
            await LoadAsync(report.Id);
            if (report.Mailed)
            {
                labelStatus.Text = "Rapor mail kuyruğuna alındı.";
                labelStatus.Appearance.ForeColor = UiColors.Success;
            }
            else
            {
                labelStatus.Text = "Rapor kaydedildi ama gönderilmedi: " + report.MailNote + ".";
                labelStatus.Appearance.ForeColor = UiColors.Warning;
            }
        }

        private async void buttonRefresh_Click(object sender, EventArgs e)
        {
            var row = viewReports.GetFocusedRow() as Row;
            await LoadAsync(row == null ? 0 : row.Id);
        }

        public sealed class Row
        {
            internal Row(ReportDto r)
            {
                Id = r.Id;
                Time = UtcTime.ToLocal(r.CreatedUtc);
                Trigger = r.Trigger;
                Mail = r.Mailed ? "Gönderildi" : r.MailNote;
                Subject = r.Subject.Replace("[SecurityGuard] Günlük rapor - ", "").Replace("[SecurityGuard] Aylık yönetici raporu - ", "");
                Kind = r.Kind == "monthly" ? "Aylık" : "Günlük";
            }

            internal long Id { get; private set; }
            public DateTime? Time { get; private set; }

            [System.ComponentModel.DisplayName("Tür")]
            public string Kind { get; private set; }
            public string Subject { get; private set; }
            public string Mail { get; private set; }
            public string Trigger { get; private set; }
        }
    }
}
