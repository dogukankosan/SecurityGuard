using System;
using System.Drawing;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Forms;
using DevExpress.XtraEditors;
using SecurityGuard.Shared.Contracts;
using SecurityGuard.Shared.Dto;
using SecurityGuardUI.Helpers;
using SecurityGuardUI.Ipc;
using SecurityGuardUI.Modules;

namespace SecurityGuardUI.Forms
{
    /// <summary>
    /// İlk kurulum sihirbazı (yönetici oluşturulup giriş yapıldıktan sonra, kurulum bitirilene kadar her girişte açılır):
    /// Hoş geldiniz → SQL Server (zorunlu: bağlantı + SQL izlemesi; sunucuda SQL yoksa geçilir) → Mail (zorunlu: test edilmiş
    /// SMTP + en az bir alıcı) → Beyaz liste (isteğe bağlı) → Bitti. Adımlar programın kendi sayfalarını kullanır.
    /// Zorunlu adım tamamlanmadan ileri gidilemez; servis de bitirirken aynı şartları denetler.
    /// </summary>
    public sealed class SetupWizardForm : XtraForm, IModuleHost
    {
        private static readonly string[] StepNames = { "Hoş geldiniz", "SQL Server", "Mail", "Beyaz liste", "Bitti" };

        private readonly GuardClient _client;
        private readonly LabelControl[] _stepLabels = new LabelControl[StepNames.Length];
        private readonly ModuleBase[] _modules = new ModuleBase[StepNames.Length];
        private readonly Timer _refresh = new Timer { Interval = 4000 };
        private readonly PanelControl _content = new PanelControl { Dock = DockStyle.Fill, BorderStyle = DevExpress.XtraEditors.Controls.BorderStyles.NoBorder };
        private readonly LabelControl _title = new LabelControl();
        private readonly LabelControl _text = new LabelControl();
        private readonly LabelControl _requirement = new LabelControl();
        private readonly SimpleButton _back = new SimpleButton { Text = "< Geri", Size = new Size(110, 30) };
        private readonly SimpleButton _next = new SimpleButton { Text = "İleri >", Size = new Size(140, 30) };
        private SetupStatusDto _status;
        private int _step;
        private bool _closing;

        public SetupWizardForm(GuardClient client, SetupStatusDto status)
        {
            _client = client;
            _status = status;
            ExitReason = MainExitReason.Closed;
            Build();
            // Kaldığı yerden: eksik ilk zorunlu adım (hiçbiri yapılmadıysa hoş geldiniz)
            _step = !(status.SqlConnected || status.MailConfigured) ? 0 : !status.SqlStepDone ? 1 : !status.MailStepDone ? 2 : 3;
            _refresh.Tick += async (s, e) => await RefreshStatusAsync();
        }

        public MainExitReason ExitReason { get; private set; }
        public string ExitMessage { get; private set; }

        private void Build()
        {
            Text = "SecurityGuard - İlk kurulum";
            Icon = AppImages.Icon;
            Size = new Size(1300, 860);
            MinimumSize = new Size(1100, 700);
            StartPosition = FormStartPosition.CenterScreen;

            var left = new PanelControl { Dock = DockStyle.Left, Width = 220 };
            var logo = new LabelControl { Text = "Kurulum adımları", Location = new Point(16, 16) };
            logo.Appearance.Font = new Font("Segoe UI", 11F, FontStyle.Bold);
            logo.Appearance.Options.UseFont = true;
            left.Controls.Add(logo);
            for (int i = 0; i < StepNames.Length; i++)
            {
                _stepLabels[i] = new LabelControl { Location = new Point(20, 56 + i * 34), Text = (i + 1) + ". " + StepNames[i] };
                _stepLabels[i].Appearance.Font = new Font("Segoe UI", 10F);
                _stepLabels[i].Appearance.Options.UseFont = true;
                _stepLabels[i].Appearance.Options.UseForeColor = true;
                left.Controls.Add(_stepLabels[i]);
            }

            var header = new PanelControl { Dock = DockStyle.Top, Height = 92, BorderStyle = DevExpress.XtraEditors.Controls.BorderStyles.NoBorder };
            _title.Location = new Point(16, 10);
            _title.Appearance.Font = new Font("Segoe UI", 15F);
            _title.Appearance.Options.UseFont = true;
            _text.Location = new Point(16, 44);
            _text.AutoSizeMode = LabelAutoSizeMode.None;
            _text.Size = new Size(1000, 44);
            _text.Appearance.Options.UseTextOptions = true;
            _text.Appearance.TextOptions.WordWrap = DevExpress.Utils.WordWrap.Wrap;
            _text.Appearance.TextOptions.VAlignment = DevExpress.Utils.VertAlignment.Top;
            header.Controls.Add(_title);
            header.Controls.Add(_text);
            header.Resize += (s, e) => _text.Width = Math.Max(200, header.Width - 32);

            var bottom = new PanelControl { Dock = DockStyle.Bottom, Height = 56 };
            _requirement.Location = new Point(16, 12);
            _requirement.AutoSizeMode = LabelAutoSizeMode.None;
            _requirement.Size = new Size(780, 34);
            _requirement.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right;
            _requirement.Appearance.Options.UseForeColor = true;
            _requirement.Appearance.Options.UseTextOptions = true;
            _requirement.Appearance.TextOptions.WordWrap = DevExpress.Utils.WordWrap.Wrap;
            _back.Anchor = AnchorStyles.Top | AnchorStyles.Right;
            _next.Anchor = AnchorStyles.Top | AnchorStyles.Right;
            bottom.Controls.Add(_requirement);
            bottom.Controls.Add(_back);
            bottom.Controls.Add(_next);
            bottom.Resize += (s, e) =>
            {
                _next.Location = new Point(bottom.Width - _next.Width - 16, 12);
                _back.Location = new Point(_next.Left - _back.Width - 8, 12);
                _requirement.Width = Math.Max(200, _back.Left - 32);
            };
            _back.Click += (s, e) => ShowStep(_step - 1);
            _next.Click += async (s, e) => await NextAsync();

            Controls.Add(_content);
            Controls.Add(header);
            Controls.Add(bottom);
            Controls.Add(left);
        }

        protected override void OnShown(EventArgs e)
        {
            base.OnShown(e);
            ShowStep(_step);
            _refresh.Start();
        }

        // ================================================================ Adımlar

        private void ShowStep(int step)
        {
            step = Math.Max(0, Math.Min(StepNames.Length - 1, step));
            _step = step;
            for (int i = 0; i < _stepLabels.Length; i++)
            {
                bool done = i < step || (i == 1 && _status.SqlStepDone && step > 1) || (i == 2 && _status.MailStepDone && step > 2);
                _stepLabels[i].Text = (i == step ? "► " : done ? "✓ " : "   ") + (i + 1) + ". " + StepNames[i];
                _stepLabels[i].Appearance.Font = new Font("Segoe UI", 10F, i == step ? FontStyle.Bold : FontStyle.Regular);
                _stepLabels[i].Appearance.ForeColor = done && i != step ? UiColors.Success : UiColors.Text;
            }

            _content.SuspendLayout();
            _content.Controls.Clear();
            switch (step)
            {
                case 0:
                    _title.Text = "SecurityGuard'a hoş geldiniz";
                    _text.Text = "Birkaç adımda kurulumu tamamlayalım. SQL Server bağlantısı ve mail ayarı zorunludur: bunlar olmadan SQL saldırıları izlenemez ve "
                                 + "kritik olaylarda kimseye haber gidemez. Kurulumu yarıda bırakırsanız bir dahaki girişte kaldığınız yerden devam edersiniz.";
                    _content.Controls.Add(InfoPage(
                        "Kurulum boyunca SecurityGuard sunucunuzda hiçbir ayarı kendiliğinden değiştirmez.\r\n\r\n"
                        + "1. SQL Server: SecurityGuard'ın kendi SQL login'i (" + SecurityGuard.Shared.GuardConstants.SqlLoginName + ") ile bağlantı ve SQL izlemesinin kurulması\r\n"
                        + "   (login'i oluşturma script'i SQL Server adımında hazır verilir; SSMS'te sizin çalıştırmanız gerekir).\r\n"
                        + "2. Mail: SMTP ayarı (test maili başarılı olmalı) ve uyarıların gideceği alıcılar.\r\n"
                        + "3. Beyaz liste: ofisinizin dış IP'leri (yanlışlıkla banlanmasın). İsteğe bağlı.\r\n\r\n"
                        + "Bitirince ilk güvenlik taraması başlar ve Panel açılır."));
                    break;
                case 1:
                    _title.Text = "SQL Server bağlantısı ve izleme";
                    _text.Text = "Listeden instance'ı seçin, bağlantıyı test edip kaydedin, ardından \"SQL İzlemesini Kur\" deyin. "
                                 + "Login henüz yoksa sayfadaki script'i SSMS'te çalıştırın.";
                    if (!_status.SqlDetected) _text.Text = "Bu sunucuda SQL Server bulunamadı: bu adımı geçebilirsiniz. (SQL sonradan kurulursa SQL Bağlantısı sayfasından tanımlayın.)";
                    ShowModule(step, () => new SqlModule());
                    break;
                case 2:
                    _title.Text = "Mail ayarları ve alıcılar";
                    _text.Text = "SMTP sunucusunu girin, test maili gönderin (test başarılı olmadan kaydedilmez) ve uyarıların gideceği en az bir alıcı ekleyin.";
                    ShowModule(step, () => new MailModule());
                    break;
                case 3:
                    _title.Text = "Beyaz liste (isteğe bağlı)";
                    _text.Text = "Ofisinizin dış (internet) IP adreslerini ekleyin: buradan gelen hatalı girişler asla banlanmaz. "
                                 + "Sunucunun kendi IP'leri, localhost ve iç ağ (10.x, 172.16-31.x, 192.168.x) zaten otomatik korunur.";
                    ShowModule(step, () => new WhitelistModule());
                    break;
                default:
                    _title.Text = "Kurulum tamamlanıyor";
                    _text.Text = "\"Bitir\" deyince izleme tam olarak devreye girer, ilk güvenlik taraması başlar ve Panel açılır.";
                    _content.Controls.Add(InfoPage(Summary()));
                    break;
            }
            _content.ResumeLayout();

            _back.Enabled = step > 0;
            _next.Text = step == StepNames.Length - 1 ? "Bitir" : "İleri >";
            UpdateRequirement();
        }

        private void ShowModule(int step, Func<ModuleBase> create)
        {
            if (_modules[step] == null)
            {
                _modules[step] = create();
                _modules[step].Dock = DockStyle.Fill;
                _modules[step].Attach(this, _client);
            }
            _content.Controls.Add(_modules[step]);
            _modules[step].Activate();
        }

        private static Control InfoPage(string text)
        {
            var memo = new MemoEdit { Dock = DockStyle.Fill, Text = text };
            memo.Properties.ReadOnly = true;
            memo.Properties.Appearance.Font = new Font("Segoe UI", 11F);
            memo.Properties.Appearance.Options.UseFont = true;
            memo.Properties.BorderStyle = DevExpress.XtraEditors.Controls.BorderStyles.NoBorder;
            var panel = new PanelControl { Dock = DockStyle.Fill, Padding = new Padding(24), BorderStyle = DevExpress.XtraEditors.Controls.BorderStyles.NoBorder };
            panel.Controls.Add(memo);
            return panel;
        }

        private string Summary()
        {
            var s = _status;
            return "Durum:\r\n"
                   + (s.SqlDetected ? (s.SqlStepDone ? "  ✓ SQL Server bağlı, SQL izlemesi kurulu" : "  ✗ SQL Server adımı eksik") : "  – Sunucuda SQL Server yok")
                   + (s.SqlMissingInstances != null && s.SqlMissingInstances.Count > 0 && s.SqlConnected ? "\r\n    (bağlanmayan diğer instance'lar: " + string.Join(", ", s.SqlMissingInstances) + ")" : "")
                   + "\r\n" + (s.MailStepDone ? "  ✓ Mail ayarlı, " + s.ActiveRecipients + " aktif alıcı" : "  ✗ Mail adımı eksik")
                   + "\r\n  " + (s.WhitelistEntries > 0 ? "✓ Beyaz listede " + s.WhitelistEntries + " kayıt" : "– Beyaz listeye kayıt eklenmedi (iç ağ ve sunucunun kendi IP'leri yine korunur)")
                   + "\r\n\r\nBitirdikten sonra yapmanızı önerdiklerimiz:\r\n"
                   + "  1. Öneriler → Güvenlik Önerileri: ilk tarama kendiliğinden başlar; kritik maddeleri inceleyin.\r\n"
                   + "  2. \"Fidye yazılımı erken uyarısı\" maddesindeki script'i çalıştırın: gölge kopya / yedek silme komutları anında yakalanır.\r\n"
                   + "  3. SQL Server → Yedekleme: sunucuda başka yedek yoksa yedeklemeyi açın (WinRAR gerekir).\r\n"
                   + "  4. Genel Ayarlar: rapor saati, disk / işlemci / bellek uyarı eşikleri.\r\n\r\n"
                   + "Her gün rapor saatinde (varsayılan 20:00) güvenlik ve performans özeti mail ile gelir.";
        }

        // ================================================================ İleri / bitir

        private async Task NextAsync()
        {
            _next.Enabled = false;
            try
            {
                if (_step == 1 || _step == 2)
                {
                    if (!await RefreshStatusAsync()) return;
                    if (_step == 1 && !_status.SqlStepDone)
                    {
                        Ui.Warning(this, SqlMissing() + Environment.NewLine + Environment.NewLine + "SQL Server adımı tamamlanmadan devam edilemez.");
                        return;
                    }
                    if (_step == 2 && !_status.MailStepDone)
                    {
                        Ui.Warning(this, MailMissing() + Environment.NewLine + Environment.NewLine + "Mail adımı tamamlanmadan devam edilemez.");
                        return;
                    }
                }
                if (_step < StepNames.Length - 1)
                {
                    if (_step == 3) await RefreshStatusAsync();
                    ShowStep(_step + 1);
                    return;
                }

                var result = await _client.SendAsync<SetupStatusDto>(Commands.SetupComplete, null, TimeSpan.FromMinutes(1));
                if (result.IsUnauthorized)
                {
                    HandleSessionExpired();
                    return;
                }
                if (!result.Success)
                {
                    Ui.Warning(this, result.ErrorMessage);
                    await RefreshStatusAsync();
                    ShowStep(!_status.SqlStepDone ? 1 : !_status.MailStepDone ? 2 : _step);
                    return;
                }
                _closing = true;
                ExitReason = MainExitReason.Logout; // = devam: ana form açılır
                DialogResult = DialogResult.OK;
                Close();
            }
            finally
            {
                if (!IsDisposed) _next.Enabled = true;
            }
        }

        private async Task<bool> RefreshStatusAsync()
        {
            var result = await _client.SendAsync<SetupStatusDto>(Commands.SetupStatus);
            if (IsDisposed) return false;
            if (result.IsUnauthorized)
            {
                HandleSessionExpired();
                return false;
            }
            if (!result.Success) return false;
            _status = result.Value;
            UpdateRequirement();
            return true;
        }

        private void UpdateRequirement()
        {
            string text;
            bool ok;
            switch (_step)
            {
                case 1:
                    ok = _status.SqlStepDone;
                    text = ok ? (_status.SqlDetected ? "✓ SQL bağlantısı ve izleme tamam." : "✓ Sunucuda SQL Server yok, bu adım geçilebilir.") : SqlMissing();
                    break;
                case 2:
                    ok = _status.MailStepDone;
                    text = ok ? "✓ Mail ayarı ve " + _status.ActiveRecipients + " aktif alıcı tamam." : MailMissing();
                    break;
                case 3:
                    ok = true;
                    text = _status.WhitelistEntries > 0 ? "✓ Beyaz listede " + _status.WhitelistEntries + " kayıt." : "İsteğe bağlı: ofis IP'si eklemeden de devam edebilirsiniz.";
                    break;
                default:
                    ok = true;
                    text = "";
                    break;
            }
            _requirement.Text = text;
            _requirement.Appearance.ForeColor = ok ? UiColors.Success : UiColors.Warning;
        }

        private string SqlMissing()
        {
            if (!_status.SqlConnected) return "Eksik: SQL Server bağlantısı kaydedilmedi (bağlantıyı test edip kaydedin).";
            return "Eksik: SQL izlemesi kurulmadı (instance'ı seçip \"SQL İzlemesini Kur\" deyin).";
        }

        private string MailMissing()
        {
            if (!_status.MailConfigured) return "Eksik: mail ayarı kaydedilmedi (test maili başarılı olunca kaydedilebilir).";
            return "Eksik: en az bir aktif mail alıcısı ekleyin.";
        }

        // ================================================================ Kapatma / IModuleHost

        protected override void OnFormClosing(FormClosingEventArgs e)
        {
            if (!_closing && e.CloseReason == CloseReason.UserClosing)
            {
                if (!Ui.Confirm(this, "Kurulum tamamlanmadan program kapanacak. Bir dahaki girişte kaldığınız yerden devam edersiniz." + Environment.NewLine + Environment.NewLine + "Kapatılsın mı?"))
                {
                    e.Cancel = true;
                    return;
                }
                ExitReason = MainExitReason.Closed;
            }
            _refresh.Stop();
            base.OnFormClosing(e);
        }

        protected override void OnFormClosed(FormClosedEventArgs e)
        {
            _refresh.Dispose();
            foreach (var m in _modules.Where(m => m != null)) m.Dispose();
            base.OnFormClosed(e);
        }

        public void HandleSessionExpired()
        {
            End(MainExitReason.SessionExpired, "Oturum süresi doldu, lütfen tekrar giriş yapın.");
        }

        public void ReturnToStart(string message)
        {
            End(MainExitReason.ServiceStopped, message);
        }

        public void ExitApplication()
        {
            End(MainExitReason.Closed, null);
        }

        public void Navigate(string page)
        {
            // Sihirbazda sayfa değiştirilmez
        }

        public void HandleUnlicensed(string message)
        {
            // Kurulum sırasında lisans geçersiz oldu: akış lisans sayfasıyla baştan açılır
            End(MainExitReason.Reload, message);
        }

        public void LicenseActivated()
        {
        }

        private void End(MainExitReason reason, string message)
        {
            if (_closing) return;
            _closing = true;
            ExitReason = reason;
            ExitMessage = message;
            Close();
        }
    }
}
