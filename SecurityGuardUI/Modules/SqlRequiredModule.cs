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
    /// SQL Server bağlantısı gerektiren sayfaların (Bakım, Yedekleme) kilidi: bağlantı yoksa sayfa yerine sebebi ve
    /// SQL Bağlantısı sayfasına giden düğme gösterilir. Bağlantı tanımlanınca sayfa her gösterilişte tekrar kontrol edilir.
    /// </summary>
    public sealed class SqlRequiredModule : ModuleBase
    {
        private readonly Func<ModuleBase> _create;
        private readonly string _title;
        private readonly LabelControl _message = new LabelControl();
        private readonly SimpleButton _go = new SimpleButton { Text = "SQL Bağlantısı sayfasına git", Size = new Size(220, 32) };
        private ModuleBase _inner;
        private bool _checking;

        public SqlRequiredModule(Func<ModuleBase> create, string title)
        {
            _create = create;
            _title = title;
            var header = new LabelControl { Text = title, Location = new Point(12, 8) };
            header.Appearance.Font = new Font("Segoe UI", 14F);
            header.Appearance.Options.UseFont = true;
            _message.Location = new Point(14, 56);
            _message.AutoSizeMode = LabelAutoSizeMode.None;
            _message.Size = new Size(700, 60);
            _message.Appearance.Options.UseTextOptions = true;
            _message.Appearance.TextOptions.WordWrap = DevExpress.Utils.WordWrap.Wrap;
            _message.Appearance.Options.UseForeColor = true;
            _go.Location = new Point(14, 124);
            _go.Click += (s, e) => Host.Navigate(DashboardPages.SqlConnection);
            Controls.Add(header);
            Controls.Add(_message);
            Controls.Add(_go);
            _message.Text = "Kontrol ediliyor...";
            _go.Visible = false;
            VisibleChanged += async (s, e) => { if (Visible && _inner == null && Client != null) await CheckAsync(); };
        }

        protected override Task OnFirstShowAsync()
        {
            return CheckAsync();
        }

        private async Task CheckAsync()
        {
            if (_checking || _inner != null) return;
            _checking = true;
            try
            {
                var result = await Client.SendAsync<SetupStatusDto>(Commands.SetupStatus);
                if (IsDisposed) return;
                if (result.IsUnauthorized)
                {
                    Host.HandleSessionExpired();
                    return;
                }
                // Servis eski (komutu bilmiyor) veya okunamadı: kilitlemeden sayfayı aç
                if (!result.Success || result.Value.SqlConnected)
                {
                    _inner = _create();
                    _inner.Dock = DockStyle.Fill;
                    _inner.Attach(Host, Client);
                    Controls.Clear();
                    Controls.Add(_inner);
                    _inner.Activate();
                    return;
                }
                _message.Text = result.Value.SqlDetected
                    ? "Önce SQL Server bağlantısını tanımlayın: " + _title + " SQL Server'a bağlanmadan çalışamaz."
                    : "Bu sunucuda SQL Server bulunamadı: " + _title + " kullanılamaz.";
                _message.Appearance.ForeColor = UiColors.Warning;
                _go.Visible = result.Value.SqlDetected;
            }
            finally
            {
                _checking = false;
            }
        }
    }
}
