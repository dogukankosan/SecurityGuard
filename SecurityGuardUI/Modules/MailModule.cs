using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Windows.Forms;
using SecurityGuard.Shared;
using SecurityGuard.Shared.Contracts;
using SecurityGuard.Shared.Dto;
using SecurityGuardUI.Helpers;

namespace SecurityGuardUI.Modules
{
    /// <summary>
    /// Mail Ayarları: SMTP (+ test maili), alıcılar ve HTML imza (canlı önizleme, gömülü resimler).
    /// SMTP şifresi ekrana hiç gelmez; kutu boş bırakılırsa kayıtlı şifre korunur.
    /// </summary>
    public partial class MailModule : ModuleBase
    {
        private const int MaxImageBytes = 512 * 1024;

        private readonly List<SignatureImageDto> _images = new List<SignatureImageDto>();
        private readonly Timer _previewTimer = new Timer { Interval = 400 };
        private string _savedSignature = "";
        private bool _hasPassword;

        /// <summary>Başarılı test mailinin gönderildiği ekran bilgileri. Kaydet sadece bunlar değişmediyse aktif.</summary>
        private string _testedKey;

        public MailModule()
        {
            InitializeComponent();
            GridHelper.SetupReadOnly(gridRecipientsView, multiSelect: true);
            gridRecipientsView.OptionsBehavior.Editable = true;
            gridRecipientsView.OptionsBehavior.ReadOnly = false;
            gridRecipientsView.OptionsBehavior.EditorShowMode = DevExpress.Utils.EditorShowMode.MouseDown;
            colEmail.OptionsColumn.AllowEdit = false;
            colDisplayName.OptionsColumn.AllowEdit = false;
            colCreated.OptionsColumn.AllowEdit = false;

            comboSecurity.Properties.Items.AddRange(new object[]
            {
                "Otomatik (porta göre)", "SSL (genelde 465)", "STARTTLS (genelde 587)", "Şifresiz (önerilmez)"
            });

            _previewTimer.Tick += (s, e) => { _previewTimer.Stop(); RenderPreview(); };
            webPreview.Navigating += (s, e) =>
            {
                // Önizlemedeki linklere tıklanınca sayfa açılmasın
                if (e.Url != null && e.Url.ToString() != "about:blank") e.Cancel = true;
            };
            webPreview.DocumentCompleted += webPreview_DocumentCompleted;

            // Bağlantı bilgisi değişince önceki test geçersiz: Kaydet tekrar pasif
            EventHandler changed = (s, e) => UpdateSaveState();
            textHost.EditValueChanged += changed;
            spinPort.EditValueChanged += changed;
            comboSecurity.SelectedIndexChanged += changed;
            textUser.EditValueChanged += changed;
            textPassword.EditValueChanged += changed;
            textFrom.EditValueChanged += changed;
            UpdateSaveState();
        }

        /// <summary>Bağlantıyı etkileyen alanlar (gönderen adı hariç; değişmesi test gerektirmez).</summary>
        private string CurrentKey()
        {
            var s = ReadSettings();
            return string.Join("\u0001", s.Host.ToLowerInvariant(), s.Port, s.Security, s.UserName, s.Password, s.FromAddress.ToLowerInvariant());
        }

        private void UpdateSaveState()
        {
            bool tested = _testedKey != null && _testedKey == CurrentKey();
            buttonSaveSmtp.Enabled = tested;
            labelSaveHint.Text = tested
                ? "Test başarılı; ayarları kaydedebilirsiniz."
                : "Kaydetmek için önce aşağıdan bu ayarlarla test maili gönderin.";
            labelSaveHint.Appearance.ForeColor = tested ? UiColors.Success : UiColors.Text;
        }

        protected override async Task OnFirstShowAsync()
        {
            await LoadSettingsAsync();
            await LoadRecipientsAsync();
            await LoadSignatureAsync();
        }

        // ================================================================ SMTP

        private async Task LoadSettingsAsync()
        {
            var s = await CallAsync(c => c.SendAsync<MailSettingsDto>(Commands.MailGetSettings));
            if (s != null) BindSettings(s);
        }

        private void BindSettings(MailSettingsDto s)
        {
            textHost.Text = s.Host;
            spinPort.Value = s.Port <= 0 ? 587 : s.Port;
            comboSecurity.SelectedIndex = Math.Max(0, Math.Min(3, s.Security));
            textUser.Text = s.UserName;
            textFrom.Text = s.FromAddress;
            textFromName.Text = s.FromName;
            textPassword.Text = "";
            _hasPassword = s.HasPassword;
            textPassword.Properties.NullValuePrompt = _hasPassword
                ? "Kayıtlı şifre korunur; değiştirmek için yeni şifreyi yazın"
                : "Şifre (kullanıcı adı varsa)";
            _testedKey = null;
            UpdateSaveState();
        }

        private MailSettingsDto ReadSettings()
        {
            return new MailSettingsDto
            {
                Host = textHost.Text.Trim(),
                Port = (int)spinPort.Value,
                Security = comboSecurity.SelectedIndex < 0 ? 0 : comboSecurity.SelectedIndex,
                UserName = textUser.Text.Trim(),
                Password = textPassword.Text,
                FromAddress = textFrom.Text.Trim(),
                FromName = textFromName.Text.Trim()
            };
        }

        private async void buttonSaveSmtp_Click(object sender, EventArgs e)
        {
            var saved = await CallAsync(c => c.SendAsync<MailSettingsDto>(Commands.MailSaveSettings, ReadSettings()));
            if (saved == null) return;
            BindSettings(saved);
            Ui.Info(FindForm(), "Mail ayarları kaydedildi. Bekleyen mailler varsa hemen gönderilmeye başlanır.");
        }

        private async void buttonTest_Click(object sender, EventArgs e)
        {
            string to = textTestTo.Text.Trim();
            if (to.Length == 0)
            {
                ShowTestResult(false, "Test mailinin gideceği adresi yazın.");
                textTestTo.Focus();
                return;
            }

            ShowTestResult(null, "Gönderiliyor...");
            string key = CurrentKey();
            var request = new MailTestRequest { Settings = ReadSettings(), To = to };
            var result = await CallQuietAsync(c => c.SendAsync(Commands.MailTest, request, TimeSpan.FromSeconds(90)));
            if (result.IsUnauthorized) return;

            _testedKey = result.Success ? key : null;
            UpdateSaveState();
            ShowTestResult(result.Success, result.Success
                ? "Test maili gönderildi → " + to + ". Gelen kutusunu (ve gereksiz klasörünü) kontrol edin. Artık ayarları kaydedebilirsiniz."
                : result.ErrorMessage);
        }

        /// <param name="success">null = devam ediyor</param>
        private void ShowTestResult(bool? success, string message)
        {
            labelTestResult.Text = message;
            labelTestResult.Appearance.ForeColor = success == null ? UiColors.Text
                : success.Value ? UiColors.Success : UiColors.Error;
        }

        // ================================================================ Alıcılar

        private async Task LoadRecipientsAsync()
        {
            var list = await CallAsync(c => c.SendAsync<List<RecipientDto>>(Commands.MailRecipientsList));
            if (list != null) BindRecipients(list);
        }

        private void BindRecipients(List<RecipientDto> list)
        {
            gridRecipients.DataSource = list.Select(r => new RecipientRow
            {
                Id = r.Id,
                Email = r.Email,
                DisplayName = r.DisplayName,
                IsEnabled = r.IsEnabled,
                Created = UtcTime.ToLocal(r.CreatedUtc)
            }).ToList();

            int active = list.Count(r => r.IsEnabled);
            labelRecipientStatus.Text = list.Count + " alıcı (" + active + " aktif)";
            if (string.IsNullOrEmpty(textTestTo.Text))
            {
                var first = list.FirstOrDefault(r => r.IsEnabled);
                if (first != null) textTestTo.Text = first.Email;
            }
        }

        private async void buttonAddRecipient_Click(object sender, EventArgs e)
        {
            var req = new RecipientAddRequest { Email = textNewEmail.Text.Trim(), DisplayName = textNewName.Text.Trim() };
            if (req.Email.Length == 0)
            {
                Ui.Warning(FindForm(), "Mail adresini yazın.");
                textNewEmail.Focus();
                return;
            }

            var list = await CallAsync(c => c.SendAsync<List<RecipientDto>>(Commands.MailRecipientAdd, req));
            if (list == null) return;
            BindRecipients(list);
            textNewEmail.Text = "";
            textNewName.Text = "";
            textNewEmail.Focus();
        }

        private async void buttonDeleteRecipient_Click(object sender, EventArgs e)
        {
            var selected = gridRecipientsView.GetSelectedRows().Where(h => h >= 0)
                .Select(h => (RecipientRow)gridRecipientsView.GetRow(h)).Where(r => r != null).ToList();
            if (selected.Count == 0)
            {
                Ui.Warning(FindForm(), "Silinecek alıcıları seçin.");
                return;
            }
            if (!Ui.Confirm(FindForm(), selected.Count + " alıcı silinsin mi?" + Environment.NewLine + Environment.NewLine +
                string.Join(Environment.NewLine, selected.Select(r => "  " + r.Email)))) return;

            var list = await CallAsync(c => c.SendAsync<List<RecipientDto>>(Commands.MailRecipientDelete,
                new IdListRequest { Ids = selected.Select(r => r.Id).ToList() }));
            if (list != null) BindRecipients(list);
        }

        /// <summary>Aktif kutucuğu değişince hemen kaydedilir.</summary>
        private async void repoEnabled_EditValueChanged(object sender, EventArgs e)
        {
            gridRecipientsView.PostEditor();
            var row = gridRecipientsView.GetFocusedRow() as RecipientRow;
            if (row == null) return;

            await CallAsync(c => c.SendAsync(Commands.MailRecipientSetEnabled,
                new RecipientSetEnabledRequest { Id = row.Id, IsEnabled = row.IsEnabled }));
            await LoadRecipientsAsync(); // başarısızsa eski haline döner
        }

        private void textNewEmail_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.KeyCode != Keys.Enter) return;
            e.SuppressKeyPress = true;
            buttonAddRecipient.PerformClick();
        }

        // ================================================================ İmza

        private async Task LoadSignatureAsync()
        {
            var sig = await CallAsync(c => c.SendAsync<SignatureDto>(Commands.MailSignatureGet));
            if (sig == null) return;

            _images.Clear();
            _images.AddRange(sig.Images ?? new List<SignatureImageDto>());
            RefreshImageList();
            // Kod kutusu satır sonlarını \n olarak tutar; karşılaştırma aynı biçimde yapılsın
            _savedSignature = (sig.Html ?? "").Replace("\r\n", "\n");
            textHtml.Text = _savedSignature;
            textHtml.ResetHistory(); // yüklenen hal geri alınamasın; geçmiş buradan başlar
            UpdateSignatureState();
            RenderPreview();
        }

        private void textHtml_TextChanged(object sender, EventArgs e)
        {
            UpdateSignatureState();
            _previewTimer.Stop();
            _previewTimer.Start();
        }

        private void UpdateSignatureState()
        {
            bool dirty = textHtml.Text != _savedSignature;
            buttonSaveSignature.Enabled = dirty;
            labelSignatureState.Text = dirty ? "İmzada kaydedilmemiş değişiklik var" : "İmza her mailin sonuna eklenir.";
        }

        private async void buttonSaveSignature_Click(object sender, EventArgs e)
        {
            string html = textHtml.Text;
            var missing = Regex.Matches(html, "cid:([A-Za-z0-9]+)").Cast<Match>().Select(m => m.Groups[1].Value)
                .Where(cid => _images.All(i => i.ContentId != cid)).Distinct().ToList();
            if (missing.Count > 0 && !Ui.Confirm(FindForm(), "İmzada olmayan resimlere referans var: " + string.Join(", ", missing) +
                Environment.NewLine + "Bu resimler mailde görünmez. Yine de kaydedilsin mi?")) return;

            if (!await CallAsync(c => c.SendAsync(Commands.MailSignatureSave, new SignatureSaveRequest { Html = html }))) return;
            _savedSignature = html;
            UpdateSignatureState();
        }

        private async void buttonAddImage_Click(object sender, EventArgs e)
        {
            using (var dialog = new OpenFileDialog { Filter = "Resim (*.png;*.jpg;*.jpeg;*.gif)|*.png;*.jpg;*.jpeg;*.gif", Title = "İmzaya resim ekle" })
            {
                if (dialog.ShowDialog(FindForm()) != DialogResult.OK) return;

                var info = new FileInfo(dialog.FileName);
                if (info.Length > MaxImageBytes)
                {
                    Ui.Warning(FindForm(), "Resim en fazla " + (MaxImageBytes / 1024) + " KB olabilir (bu dosya " + (info.Length / 1024) + " KB). Küçültüp tekrar deneyin.");
                    return;
                }

                var req = new SignatureImageDto { FileName = info.Name, Base64 = Convert.ToBase64String(File.ReadAllBytes(info.FullName)) };
                var added = await CallAsync(c => c.SendAsync<SignatureImageDto>(Commands.MailSignatureAddImage, req));
                if (added == null) return;

                _images.Add(added);
                RefreshImageList();
                InsertImageTag(added);
            }
        }

        /// <summary>
        /// Sol tık: resim imlecin olduğu yere hemen eklenir (seçim listede kalmaz).
        /// Sağ tık: sil menüsü.
        /// </summary>
        private void listImages_MouseClick(object sender, MouseEventArgs e)
        {
            int index = listImages.IndexFromPoint(e.Location);
            if (index < 0 || index >= _images.Count) return;
            var img = _images[index];

            if (e.Button == MouseButtons.Left)
            {
                InsertImageTag(img);
            }
            else if (e.Button == MouseButtons.Right)
            {
                var menu = new ContextMenuStrip();
                menu.Items.Add("İmlecin olduğu yere ekle", null, (s, a) => InsertImageTag(img));
                menu.Items.Add("Resmi sil", null, async (s, a) => await DeleteImageAsync(img));
                menu.Closed += (s, a) => BeginInvoke(new Action(menu.Dispose));
                menu.Show(listImages, e.Location);
            }
            listImages.SelectedIndex = -1;
        }

        private async Task DeleteImageAsync(SignatureImageDto img)
        {
            bool used = textHtml.Text.Contains("cid:" + img.ContentId);
            if (!Ui.Confirm(FindForm(), img.FileName + " silinsin mi?" +
                (used ? Environment.NewLine + "Bu resim imzada kullanılıyor; HTML'deki etiketini de kaldırmanız gerekir." : ""))) return;

            if (!await CallAsync(c => c.SendAsync(Commands.MailSignatureDeleteImage, new ContentIdRequest { ContentId = img.ContentId }))) return;
            _images.Remove(img);
            RefreshImageList();
            RenderPreview();
        }

        private void RefreshImageList()
        {
            listImages.Items.Clear();
            foreach (var img in _images)
                listImages.Items.Add(img.FileName + "   (cid:" + img.ContentId + ", " + (img.Base64.Length * 3 / 4 / 1024) + " KB)");
            listImages.SelectedIndex = -1;
        }

        private void InsertImageTag(SignatureImageDto img)
        {
            string tag = "<img src=\"cid:" + img.ContentId + "\" alt=\"" + System.Net.WebUtility.HtmlEncode(Path.GetFileNameWithoutExtension(img.FileName)) + "\">";
            textHtml.InsertAtCaret(tag);
        }

        /// <summary>
        /// Önizlemedeki resme tıklanınca HTML'deki aynı sıradaki &lt;img&gt; etiketi seçilir.
        /// (Önizlemede imzadan önce resim olmadığı için sıra birebir eşleşir.)
        /// </summary>
        private void webPreview_DocumentCompleted(object sender, WebBrowserDocumentCompletedEventArgs e)
        {
            var doc = webPreview.Document;
            if (doc == null) return;

            int i = 0;
            foreach (HtmlElement img in doc.GetElementsByTagName("IMG"))
            {
                int index = i++;
                img.Style = (img.Style ?? "") + ";cursor:pointer";
                img.SetAttribute("title", "HTML'de göstermek için tıklayın");
                img.Click += (s, a) => textHtml.SelectImageTag(index);
            }
        }

        /// <summary>Önizleme: cid: referansları resim verisiyle değiştirilir (mailde gömülü görünecek hali).</summary>
        private void RenderPreview()
        {
            string html = textHtml.Text;
            foreach (var img in _images)
                html = html.Replace("cid:" + img.ContentId, "data:" + img.MimeType + ";base64," + img.Base64);

            webPreview.DocumentText =
                "<html><head><meta http-equiv=\"X-UA-Compatible\" content=\"IE=edge\"><meta charset=\"utf-8\"></head>" +
                "<body style=\"font-family:'Segoe UI',Arial,sans-serif;font-size:14px;color:#222222;background:#ffffff;margin:16px\">" +
                "<p style=\"color:#999999\">Örnek mail içeriği... SecurityGuard kritik olay bildirimi burada yer alır.</p><br>" +
                html + "</body></html>";
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                _previewTimer.Dispose();
                if (components != null) components.Dispose();
            }
            base.Dispose(disposing);
        }

        public sealed class RecipientRow
        {
            public long Id { get; set; }
            public string Email { get; set; }
            public string DisplayName { get; set; }
            public bool IsEnabled { get; set; }
            public DateTime? Created { get; set; }
        }
    }
}
