using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using DevExpress.XtraEditors.Controls;
using DevExpress.XtraGrid.Views.Base;
using DevExpress.XtraGrid.Views.Grid;
using SecurityGuard.Shared;
using SecurityGuard.Shared.Contracts;
using SecurityGuard.Shared.Dto;
using SecurityGuard.Shared.Enums;
using SecurityGuardUI.Helpers;

namespace SecurityGuardUI.Modules
{
    /// <summary>
    /// Olay Logu: servisin yakaladığı güvenlik olayları (kurallarda "Logla" açık olanlar).
    /// Önem / grup / metin filtresi, seçili olayın detayı altta. "Bütünlük Kontrolü" HMAC zincirini doğrular:
    /// kayıt değiştirilmiş veya silinmişse gösterir.
    /// </summary>
    public partial class EventLogModule : ModuleBase
    {
        private const int MaxRows = 5000;
        private const string AllCategories = "Tüm gruplar";

        private bool _categoriesLoaded;

        public EventLogModule()
        {
            InitializeComponent();
            GridHelper.SetupReadOnly(gridView);
            dateFrom.DateTime = DateTime.Today.AddDays(-7);
            dateTo.DateTime = DateTime.Today.AddDays(1).AddMinutes(-1);

            // (metin, object) kurucusu: (metin, int) kurucusu değeri metne çeviriyor
            comboSeverity.Properties.Items.Add(new ImageComboBoxItem("Tüm önemler", (object)(-1), -1));
            comboSeverity.Properties.Items.Add(new ImageComboBoxItem("Uyarı ve Kritik", (object)(int)Severity.Uyari, -1));
            comboSeverity.Properties.Items.Add(new ImageComboBoxItem("Sadece Kritik", (object)(int)Severity.Kritik, -1));
            comboSeverity.EditValue = -1;

            comboCategory.Properties.Items.Add(AllCategories);
            comboCategory.EditValue = AllCategories;
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

            string category = comboCategory.EditValue as string;
            var query = new EventLogQuery
            {
                FromUtc = UtcTime.ToText(dateFrom.DateTime),
                ToUtc = UtcTime.ToText(dateTo.DateTime),
                MinSeverity = comboSeverity.EditValue is int ? (int)comboSeverity.EditValue : -1,
                Category = category == AllCategories ? null : category,
                Search = textSearch.Text,
                MaxRows = MaxRows
            };

            var result = await CallAsync(c => c.SendAsync<EventLogResult>(Commands.EventLogQuery, query));
            if (result == null) return;

            if (!_categoriesLoaded && result.Categories != null)
            {
                foreach (var c in result.Categories) comboCategory.Properties.Items.Add(c);
                _categoriesLoaded = true;
            }

            gridControl.DataSource = result.Rows.Select(r => new Row
            {
                Time = UtcTime.ToLocal(r.TimeUtc),
                Severity = r.Severity,
                Category = r.Category,
                EventName = r.EventName,
                Summary = r.Summary,
                UserName = r.UserName,
                IpAddress = r.IpAddress,
                Mail = r.MailSent ? "Gönderildi" : "",
                Source = r.Source,
                Detail = r.Detail
            }).ToList();
            ShowDetail();

            labelStatus.Text = result.Truncated
                ? "En yeni " + MaxRows + " olay gösteriliyor. Daha eskileri için tarih aralığını daraltın."
                : result.Rows.Count == 0 ? "Bu aralıkta olay yok." : result.Rows.Count + " olay";
            labelStatus.Appearance.ForeColor = UiColors.Text;
        }

        // ================================================================ Tablo

        private void gridView_CustomColumnDisplayText(object sender, CustomColumnDisplayTextEventArgs e)
        {
            if (e.Column == colSeverity && e.Value is int)
                e.DisplayText = SeverityText((int)e.Value);
        }

        private void gridView_RowCellStyle(object sender, RowCellStyleEventArgs e)
        {
            if (e.Column != colSeverity) return;
            var row = gridView.GetRow(e.RowHandle) as Row;
            if (row == null) return;
            if (row.Severity == (int)Severity.Kritik) e.Appearance.ForeColor = UiColors.Error;
            else if (row.Severity == (int)Severity.Uyari) e.Appearance.ForeColor = UiColors.Warning;
        }

        private void gridView_FocusedRowChanged(object sender, FocusedRowChangedEventArgs e)
        {
            ShowDetail();
        }

        /// <summary>Seçili olayın tüm bilgisi alttaki kutuda (kopyalanabilir).</summary>
        private void ShowDetail()
        {
            var r = gridView.GetFocusedRow() as Row;
            if (r == null)
            {
                memoDetail.Text = "";
                return;
            }

            var sb = new StringBuilder();
            sb.AppendLine(r.EventName + " (" + SeverityText(r.Severity) + ")");
            sb.AppendLine(r.Summary);
            sb.AppendLine();
            sb.AppendLine("Zaman: " + (r.Time.HasValue ? r.Time.Value.ToString("dd.MM.yyyy HH:mm:ss") : "-"));
            sb.AppendLine("Kaynak: " + r.Source);
            if (!string.IsNullOrEmpty(r.UserName)) sb.AppendLine("Kullanıcı: " + r.UserName);
            if (!string.IsNullOrEmpty(r.IpAddress)) sb.AppendLine("IP: " + r.IpAddress);
            if (!string.IsNullOrEmpty(r.Detail)) sb.AppendLine(r.Detail);
            sb.Append("Mail: " + (r.Mail.Length > 0 ? "gönderildi" : "gönderilmedi (kuralda kapalı)"));
            memoDetail.Text = sb.ToString();
        }

        private static string SeverityText(int severity)
        {
            return severity == (int)Severity.Kritik ? "Kritik" : severity == (int)Severity.Uyari ? "Uyarı" : "Bilgi";
        }

        // ================================================================ Butonlar

        private async void buttonSearch_Click(object sender, EventArgs e)
        {
            await LoadAsync();
        }

        private async void textSearch_KeyDown(object sender, System.Windows.Forms.KeyEventArgs e)
        {
            if (e.KeyCode != System.Windows.Forms.Keys.Enter) return;
            e.SuppressKeyPress = true;
            await LoadAsync();
        }

        private void buttonExport_Click(object sender, EventArgs e)
        {
            GridHelper.ExportToExcel(FindForm(), gridControl, "OlayLogu");
        }

        private async void buttonVerify_Click(object sender, EventArgs e)
        {
            var v = await CallAsync(c => c.SendAsync<EventLogVerifyResult>(Commands.EventLogVerify, null, TimeSpan.FromMinutes(5)));
            if (v == null) return;

            if (!v.ChainEnabled)
            {
                Ui.Warning(FindForm(), "Bütünlük kontrolü yapılamadı: log imza anahtarı okunamıyor. Ayrıntı servis logunda.");
                return;
            }

            if (v.IsIntact)
            {
                string text = "Olay logu sağlam: " + v.CheckedRows + " kaydın hiçbiri dışarıdan değiştirilmemiş veya silinmemiş.";
                if (v.AdminDeletedRows > 0 || v.AdminModifiedRows > 0) text += Environment.NewLine + AdminText(v);
                if (v.AcceptedRows > 0) text += Environment.NewLine + AcceptedText(v);
                if (v.UnsignedRows > 0) text += Environment.NewLine + v.UnsignedRows + " kayıt imzasız yazılmış (imza anahtarı yokken).";
                labelStatus.Text = "Bütünlük kontrolü: sağlam (" + v.CheckedRows + " kayıt)";
                labelStatus.Appearance.ForeColor = UiColors.Success;
                Ui.Info(FindForm(), text);
                return;
            }

            var sb = new StringBuilder("Olay logunda oynama tespit edildi (" + v.CheckedRows + " kayıt kontrol edildi):");
            sb.AppendLine();
            if (v.ModifiedRows > 0) sb.AppendLine("- İçeriği sonradan değiştirilmiş kayıt: " + v.ModifiedRows);
            if (v.Gaps > 0) sb.AppendLine("- Arasından kayıt silinmiş nokta: " + v.Gaps);
            if (v.TailDeletedRows > 0) sb.AppendLine("- Sondan silinmiş kayıt: " + v.TailDeletedRows);
            if (v.ProblemIds.Count > 0) sb.AppendLine("İlk sorunlu kayıt Id'leri: " + string.Join(", ", v.ProblemIds));
            if (v.AdminDeletedRows > 0 || v.AdminModifiedRows > 0) sb.AppendLine(AdminText(v));
            sb.AppendLine();
            sb.Append("Bu değişiklikler SecurityGuard arayüzünden yapılmadı: veritabanı dosyasına dışarıdan müdahale edilmiş olabilir.");
            labelStatus.Text = "Bütünlük kontrolü: " + v.ModifiedRows + " değişmiş kayıt, " + v.Gaps + " kopukluk" + (v.TailDeletedRows > 0 ? ", sondan " + v.TailDeletedRows + " kayıt silinmiş" : "");
            labelStatus.Appearance.ForeColor = UiColors.Error;
            sb.AppendLine().AppendLine();
            sb.Append("Bu değişikliklerin sebebini biliyorsanız (ör. eski sürümde yapılan test silmesi) mevcut durumu kabul edebilirsiniz: "
                      + "bu sorunlar bir daha oynama olarak gösterilmez, sonradan oluşan yeni oynamalar yine yakalanır. Kabul edilsin mi?");
            if (!Ui.Confirm(FindForm(), sb.ToString())) return;

            string password;
            using (var form = new SecurityGuardUI.Forms.PasswordConfirmForm("Olay logundaki mevcut bütünlük sorunlarını kabul et",
                       "Bu işlem İşlem Kaydı'na yazılır. Sebebini bilmediğiniz bir değişikliği kabul etmeyin: veritabanına dışarıdan müdahale edilmiş olabilir."))
                password = form.ShowDialog(FindForm()) == System.Windows.Forms.DialogResult.OK ? form.Password : null;
            if (password == null) return;

            var after = await CallAsync(c => c.SendAsync<EventLogVerifyResult>(Commands.EventLogAcceptIntegrity, new PasswordConfirmRequest { Password = password }, TimeSpan.FromMinutes(5)));
            if (after == null) return;
            labelStatus.Text = after.IsIntact ? "Bütünlük kontrolü: sağlam (" + after.AcceptedRows + " kabul edilmiş sorun)" : "Bütünlük kontrolü: hâlâ sorun var";
            labelStatus.Appearance.ForeColor = after.IsIntact ? UiColors.Success : UiColors.Error;
            Ui.Info(FindForm(), after.IsIntact ? "Mevcut sorunlar kabul edildi. Olay logu artık sağlam görünüyor." : "Bazı sorunlar kabul edilemedi; tekrar kontrol edin.");
        }

        private static string AdminText(EventLogVerifyResult v)
        {
            return "Yönetici tarafından SQLite Sorgu ekranından " + (v.AdminDeletedRows > 0 ? v.AdminDeletedRows + " kayıt silinmiş" : "")
                   + (v.AdminDeletedRows > 0 && v.AdminModifiedRows > 0 ? ", " : "") + (v.AdminModifiedRows > 0 ? v.AdminModifiedRows + " kayıt değiştirilmiş" : "")
                   + " (oynama sayılmaz, ayrıntısı İşlem Kaydı'nda).";
        }

        private static string AcceptedText(EventLogVerifyResult v)
        {
            return v.AcceptedRows + " kayıttaki sorun daha önce \"mevcut durumu kabul et\" ile kabul edilmiş.";
        }

        /// <summary>Tablo satırı (zaman yerel saate çevrilmiş).</summary>
        public sealed class Row
        {
            public DateTime? Time { get; set; }
            public int Severity { get; set; }
            public string Category { get; set; }
            public string EventName { get; set; }
            public string Summary { get; set; }
            public string UserName { get; set; }
            public string IpAddress { get; set; }
            public string Mail { get; set; }
            internal string Source { get; set; }
            internal string Detail { get; set; }
        }
    }
}
