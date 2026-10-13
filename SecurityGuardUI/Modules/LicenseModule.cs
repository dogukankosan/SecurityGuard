using System;
using System.Drawing;
using System.Threading.Tasks;
using System.Windows.Forms;
using DevExpress.XtraEditors;
using SecurityGuard.Shared.Contracts;
using SecurityGuard.Shared.Dto;
using SecurityGuardUI.Helpers;

namespace SecurityGuardUI.Modules
{
    /// <summary>
    /// Sistem → Lisans: durum, bitiş, donanım kimliği (kopyalanır) ve lisans sunucusu adresi.
    /// Lisans burada girilmez / başlatılmaz: lisans sağlayıcısı bu makinenin donanım kimliğiyle lisans sunucusunda tanımlar,
    /// servis 5 dakikada bir sunucuya sorar ("Şimdi kontrol et" beklemeden sorar).
    /// </summary>
    public sealed class LicenseModule : ModuleBase
    {
        private readonly LabelControl _state = new LabelControl();
        private readonly LabelControl _detail = new LabelControl();
        private readonly LabelControl _customer = new LabelControl();
        private readonly LabelControl _expires = new LabelControl();
        private readonly LabelControl _lastCheck = new LabelControl();
        private readonly LabelControl _lastError = new LabelControl();
        private readonly TextEdit _hwId = new TextEdit();
        private readonly TextEdit _apiUrl = new TextEdit();
        private readonly LabelControl _checkResult = new LabelControl();
        private readonly Timer _refresh = new Timer { Interval = 30000 };
        private bool? _wasActive;

        public LicenseModule()
        {
            Build();
            _refresh.Tick += async (s, e) => await LoadAsync(false);
            VisibleChanged += async (s, e) =>
            {
                if (Visible && Client != null) await LoadAsync(false);
                _refresh.Enabled = Visible;
            };
        }

        private void Build()
        {
            var panel = new XtraScrollableControl { Dock = DockStyle.Fill };
            var title = new LabelControl { Text = "Lisans", Location = new Point(12, 8) };
            title.Appearance.Font = new Font("Segoe UI", 13F);
            title.Appearance.Options.UseFont = true;
            var info = Wrap(new LabelControl
            {
                Location = new Point(12, 36),
                Size = new Size(900, 36),
                Text = "SecurityGuard lisansı bu sunucunun donanım kimliğine bağlıdır. Lisans, lisans sağlayıcınız tarafından lisans sunucusunda tanımlanır; "
                       + "buradan sadece durumu görülür. Durum 5 dakikada bir kontrol edilir. Lisans geçerli değilse sunucu izleme ve koruma durur, mevcut ban kuralları yerinde kalır."
            });

            int y = 86;
            _state.Appearance.Font = new Font("Segoe UI", 12F, FontStyle.Bold);
            _state.Appearance.Options.UseFont = true;
            _state.Appearance.Options.UseForeColor = true;
            AddRow(panel, "Durum", _state, ref y, 30);
            Wrap(_detail).Size = new Size(760, 36);
            AddRow(panel, "", _detail, ref y, 44);
            AddRow(panel, "Müşteri", _customer, ref y);
            AddRow(panel, "Bitiş tarihi", _expires, ref y);
            AddRow(panel, "Son kontrol", _lastCheck, ref y);
            _lastError.Appearance.Options.UseForeColor = true;
            Wrap(_lastError).Size = new Size(760, 36);
            AddRow(panel, "Son hata", _lastError, ref y, 44);

            y += 10;
            _hwId.Properties.ReadOnly = true;
            _hwId.Size = new Size(380, 24);
            _hwId.Properties.Appearance.Font = new Font("Consolas", 10F);
            _hwId.Properties.Appearance.Options.UseFont = true;
            AddRow(panel, "Donanım kimliği", _hwId, ref y, 34);
            var copy = new SimpleButton { Text = "Kopyala", Location = new Point(_hwId.Right + 8, _hwId.Top - 1), Size = new Size(90, 26) };
            copy.Click += (s, e) =>
            {
                if (string.IsNullOrEmpty(_hwId.Text)) return;
                Clipboard.SetText(_hwId.Text);
                Ui.Info(FindForm(), "Donanım kimliği panoya kopyalandı. Lisans için bu değeri lisans sağlayıcınıza iletin.");
            };
            panel.Controls.Add(copy);
            var hwNote = Wrap(new LabelControl
            {
                Location = new Point(170, y - 4),
                Size = new Size(760, 36),
                Text = "Lisans bu kimlikle oluşturulur. Disk / anakart gibi tek bir donanım değişirse lisans geçerli kalır; Windows yeniden kurulursa veya birden fazla parça değişirse yeni kimlik için lisans güncellenmelidir."
            });
            panel.Controls.Add(hwNote);
            y += 44;

            _apiUrl.Size = new Size(380, 24);
            _apiUrl.Properties.NullValuePrompt = "Boş bırakılırsa varsayılan adres";
            AddRow(panel, "Lisans sunucusu", _apiUrl, ref y, 40);
            var saveUrl = new SimpleButton { Text = "Kaydet", Location = new Point(_apiUrl.Right + 8, _apiUrl.Top - 1), Size = new Size(90, 26) };
            saveUrl.Click += async (s, e) => await SaveUrlAsync();
            panel.Controls.Add(saveUrl);

            var check = new SimpleButton { Text = "Şimdi kontrol et", Location = new Point(170, y), Size = new Size(150, 30) };
            check.Click += async (s, e) =>
            {
                check.Enabled = false;
                _checkResult.Appearance.ForeColor = UiColors.Text;
                _checkResult.Text = "Lisans sunucusuna soruluyor...";
                try
                {
                    var result = await CallAsync(c => c.SendAsync<LicenseInfoDto>(Commands.LicenseCheck, null, TimeSpan.FromSeconds(60)));
                    if (result == null)
                    {
                        _checkResult.Text = "";
                        return;
                    }
                    ShowInfo(result);
                    // Durum değişmese de basıldığı belli olsun: sonuç ve saat butonun yanında
                    bool reached = string.IsNullOrEmpty(result.LastError);
                    _checkResult.Appearance.ForeColor = !reached ? UiColors.Error : result.Active ? UiColors.Success : UiColors.Warning;
                    _checkResult.Text = DateTime.Now.ToString("HH:mm:ss") + " - " + (reached ? "Sunucuya soruldu: " + result.StatusText : "Sunucuya ulaşılamadı (ayrıntı: Son hata)");
                }
                finally
                {
                    check.Enabled = true;
                }
            };
            panel.Controls.Add(check);
            _checkResult.Appearance.Options.UseForeColor = true;
            _checkResult.Location = new Point(check.Right + 12, check.Top + 8);
            panel.Controls.Add(_checkResult);

            panel.Controls.Add(title);
            panel.Controls.Add(info);
            panel.Resize += (s, e) => info.Width = Math.Max(300, panel.Width - 24);
            Controls.Add(panel);
        }

        private static LabelControl Wrap(LabelControl label)
        {
            label.AutoSizeMode = LabelAutoSizeMode.None;
            label.Appearance.TextOptions.WordWrap = DevExpress.Utils.WordWrap.Wrap;
            label.Appearance.Options.UseTextOptions = true;
            return label;
        }

        private static void AddRow(Control panel, string caption, Control value, ref int y, int height = 26)
        {
            if (caption.Length > 0)
                panel.Controls.Add(new LabelControl { Text = caption + ":", Location = new Point(16, y + 3) });
            value.Location = new Point(170, y);
            panel.Controls.Add(value);
            y += height;
        }

        protected override Task OnFirstShowAsync()
        {
            return LoadAsync(true);
        }

        private async Task LoadAsync(bool overlay)
        {
            LicenseInfoDto info;
            if (overlay)
                info = await CallAsync(c => c.SendAsync<LicenseInfoDto>(Commands.LicenseGet));
            else
            {
                var result = await Client.SendAsync<LicenseInfoDto>(Commands.LicenseGet);
                if (result.IsUnauthorized) Host.HandleSessionExpired();
                info = result.Success ? result.Value : null;
            }
            if (info != null) ShowInfo(info);
        }

        private async Task SaveUrlAsync()
        {
            var info = await CallAsync(c => c.SendAsync<LicenseInfoDto>(Commands.LicenseSetApiUrl, new LicenseApiUrlRequest { Url = _apiUrl.Text }, TimeSpan.FromSeconds(60)));
            if (info == null) return;
            ShowInfo(info);
            Ui.Info(FindForm(), "Lisans sunucusu adresi kaydedildi." + Environment.NewLine + "Durum: " + info.StatusText);
        }

        private void ShowInfo(LicenseInfoDto info)
        {
            _state.Text = info.StatusText + (info.HardwareChanged ? " (donanım değişikliği algılandı)" : "");
            _state.Appearance.ForeColor = !info.Active ? UiColors.Error : info.OfflineUntil != null || (info.DaysLeft > 0 && info.DaysLeft <= 7) ? UiColors.Warning : UiColors.Success;
            _detail.Text = info.Detail ?? (info.Active ? "Sunucu izleme ve koruma çalışıyor." : "");
            _customer.Text = string.IsNullOrEmpty(info.Customer) ? "-" : info.Customer;
            _expires.Text = string.IsNullOrEmpty(info.Expires) ? "-" : info.Expires + (info.Active && info.DaysLeft > 0 ? " (" + info.DaysLeft + " gün kaldı)" : "");
            _lastCheck.Text = string.IsNullOrEmpty(info.LastCheck) ? "Henüz sunucuyla görüşülmedi" : info.LastCheck;
            _lastError.Text = string.IsNullOrEmpty(info.LastError) ? "-" : info.LastError;
            _lastError.Appearance.ForeColor = string.IsNullOrEmpty(info.LastError) ? UiColors.Text : UiColors.Error;
            _hwId.Text = info.HwId;
            if (!_apiUrl.Focused) _apiUrl.Text = info.ApiUrl;

            bool activated = _wasActive == false && info.Active;
            _wasActive = info.Active;
            if (activated) Host.LicenseActivated();
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing) _refresh.Dispose();
            base.Dispose(disposing);
        }
    }
}
