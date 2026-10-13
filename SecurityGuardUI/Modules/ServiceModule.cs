using System;
using System.Diagnostics;
using System.Threading.Tasks;
using System.Windows.Forms;
using DevExpress.XtraEditors;
using DevExpress.XtraSplashScreen;
using SecurityGuard.Shared;
using SecurityGuard.Shared.Contracts;
using SecurityGuard.Shared.Dto;
using SecurityGuardUI.Forms;
using SecurityGuardUI.Helpers;

namespace SecurityGuardUI.Modules
{
    /// <summary>
    /// Servis Yönetimi: durum bilgisi, yeniden başlatma, durdurma ve kaldırma.
    /// Durdurma / kaldırma yönetici şifresini tekrar ister; işlemi servisin kendisi yapar
    /// (ileride Windows'tan dışarıdan durdurma kapatılacak, tek yol bu ekran olacak).
    /// </summary>
    public partial class ServiceModule : ModuleBase
    {
        private ServiceInfoDto _info;

        public ServiceModule()
        {
            InitializeComponent();
            labelWarning.Appearance.ForeColor = UiColors.Warning;

            // "Koruma" satırı bilgi satırlarının devamında (Bekleyen mail'in altında) durur; butonlar ve uyarı onun altına iner.
            // (Önceden sonradan eklendiği için butonların arkasında kalıyordu.)
            const int rowHeight = 26, protectionHeight = 32;
            int top = valueMail.Top + rowHeight;
            var caption = new LabelControl { Text = "Koruma", Location = new System.Drawing.Point(captionMail.Left, top) };
            _protection = new LabelControl { Text = "-", Location = new System.Drawing.Point(valueMail.Left, top), AutoSizeMode = LabelAutoSizeMode.None, Size = new System.Drawing.Size(760, protectionHeight) };
            _protection.Appearance.TextOptions.WordWrap = DevExpress.Utils.WordWrap.Wrap;
            _protection.Appearance.TextOptions.VAlignment = DevExpress.Utils.VertAlignment.Top;
            _protection.Appearance.Options.UseTextOptions = true;
            Controls.Add(caption);
            Controls.Add(_protection);

            int shift = top + protectionHeight + 14 - buttonRefresh.Top;
            foreach (Control c in new Control[] { buttonRefresh, buttonRestart, buttonStop, buttonUninstall, buttonOpenLogs, labelWarning })
                c.Top += shift;
        }

        protected override Task OnFirstShowAsync()
        {
            return LoadAsync();
        }

        private async Task LoadAsync()
        {
            var info = await CallAsync(c => c.SendAsync<ServiceInfoDto>(Commands.ServiceInfo));
            if (info == null) return;
            _info = info;

            valueState.Text = info.IsWindowsService ? "Çalışıyor (Windows servisi)" : "Çalışıyor (geliştirme / konsol modu)";
            valueVersion.Text = info.Version;
            DateTime? started = UtcTime.ToLocal(info.StartedUtc);
            valueStarted.Text = started.HasValue
                ? started.Value.ToString("dd.MM.yyyy HH:mm:ss") + "   (" + Uptime(DateTime.Now - started.Value) + " önce)"
                : "-";
            valuePid.Text = info.ProcessId.ToString();
            valueDatabase.Text = info.DatabaseFile;
            valueDatabaseSize.Text = SizeText(info.DatabaseSizeBytes) + "   ·   şema v" + info.SchemaVersion;
            valueLogs.Text = info.LogFolder;
            valueMail.Text = info.MailPending == 0 ? "Yok" : info.MailPending + " mail gönderilmeyi bekliyor";
            _protection.Text = (info.Protection ?? "-") + "  (Genel Ayarlar → Servis koruması)";

            // Geliştirme modunda servis Windows'a kurulu değil: kaldırılacak bir şey yok
            buttonUninstall.Enabled = info.IsWindowsService;
        }

        private readonly LabelControl _protection;

        private async void buttonRefresh_Click(object sender, EventArgs e)
        {
            await LoadAsync();
        }

        private void buttonOpenLogs_Click(object sender, EventArgs e)
        {
            if (_info == null) return;
            try
            {
                Process.Start("explorer.exe", "\"" + _info.LogFolder + "\"");
            }
            catch (Exception ex)
            {
                Ui.Error(FindForm(), "Klasör açılamadı: " + ex.Message);
            }
        }

        private async void buttonRestart_Click(object sender, EventArgs e)
        {
            await StopAsync(restart: true);
        }

        private async void buttonStop_Click(object sender, EventArgs e)
        {
            await StopAsync(restart: false);
        }

        private async Task StopAsync(bool restart)
        {
            if (_info == null) return;

            string password = AskPassword(
                restart ? "Servis yeniden başlatılacak." : "Servis durdurulacak.",
                restart
                    ? "Yeniden başlarken birkaç saniye izleme yapılmaz. Sonrasında tekrar giriş yapmanız gerekir."
                    : "Servis durduğunda sunucu korunmaz: izleme, ban ve mailler durur.");
            if (password == null) return;

            if (!await CallAsync(c => c.SendAsync(Commands.ServiceStop, new PasswordConfirmRequest { Password = password }))) return;

            if (!_info.IsWindowsService)
            {
                Host.ReturnToStart("Servis durduruldu (geliştirme modu). SecurityGuardService'i F5 ile tekrar başlatın.");
                return;
            }
            if (!restart)
            {
                Host.ReturnToStart("Servis durduruldu; sunucu şu an korunmuyor.");
                return;
            }

            string error = await WithWaitForm("Servis yeniden başlatılıyor...", () =>
                ServiceControl.WaitForStopped(TimeSpan.FromSeconds(60)) ? ServiceControl.Start() : "Servis zamanında durmadı.");
            if (error != null) Ui.Error(FindForm(), error);
            Host.ReturnToStart(error == null ? "Servis yeniden başlatıldı. Tekrar giriş yapın." : null);
        }

        private async void buttonUninstall_Click(object sender, EventArgs e)
        {
            if (_info == null || !_info.IsWindowsService) return;

            string password = AskPassword("SecurityGuard servisi kaldırılacak.",
                "Servis Windows'tan silinir ve sunucu korunmaz. Veritabanı ve loglar silinmez; programı tekrar açarak veya kurulumu çalıştırarak yeniden kurabilirsiniz.");
            if (password == null) return;

            if (!await CallAsync(c => c.SendAsync(Commands.ServiceUninstall, new PasswordConfirmRequest { Password = password }))) return;

            await WithWaitForm("Servis kaldırılıyor...", () => ServiceControl.WaitForStopped(TimeSpan.FromSeconds(60)) ? null : "");
            Ui.Info(FindForm(), "SecurityGuard servisi kaldırıldı. Program kapanacak.");
            Host.ExitApplication();
        }

        private string AskPassword(string action, string warning)
        {
            using (var form = new PasswordConfirmForm(action, warning))
                return form.ShowDialog(FindForm()) == DialogResult.OK ? form.Password : null;
        }

        private static async Task<string> WithWaitForm(string caption, Func<string> work)
        {
            SplashScreenManager.ShowDefaultWaitForm("SecurityGuard", caption);
            try
            {
                return await Task.Run(work);
            }
            finally
            {
                SplashScreenManager.CloseForm(false);
            }
        }

        private static string Uptime(TimeSpan t)
        {
            if (t.TotalMinutes < 1) return "az";
            if (t.TotalHours < 1) return (int)t.TotalMinutes + " dk";
            if (t.TotalDays < 1) return (int)t.TotalHours + " saat " + t.Minutes + " dk";
            return (int)t.TotalDays + " gün " + t.Hours + " saat";
        }

        private static string SizeText(long bytes)
        {
            if (bytes < 1024 * 1024) return (bytes / 1024.0).ToString("0.0") + " KB";
            if (bytes < 1024L * 1024 * 1024) return (bytes / 1024.0 / 1024).ToString("0.0") + " MB";
            return (bytes / 1024.0 / 1024 / 1024).ToString("0.00") + " GB";
        }
    }
}
