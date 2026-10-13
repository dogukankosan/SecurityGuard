using System;
using System.Windows.Forms;
using DevExpress.XtraEditors;
using SecurityGuard.Shared.Contracts;
using SecurityGuard.Shared.Dto;
using SecurityGuardUI.Helpers;
using SecurityGuardUI.Ipc;

namespace SecurityGuardUI.Forms
{
    /// <summary>
    /// Elle ban. Korunan adresler (iç ağ, beyaz liste...) servis tarafında reddedilir; hata olursa pencere açık kalır.
    /// Bu RDP oturumunun kendi IP'si burada da engellenir (yönetici kendini dışarıda bırakmasın).
    /// Sonuç: OK = banlandı (<see cref="Result"/>), Abort = oturum düştü, Cancel = vazgeçildi.
    /// </summary>
    public partial class BanAddForm : XtraForm
    {
        private readonly GuardClient _client;
        private readonly string _rdpAddress;
        private bool _busy;

        public BanAddForm(GuardClient client, string presetIp)
        {
            InitializeComponent();
            Icon = AppImages.Icon;
            HandCursor.Apply(this);
            _client = client;
            _rdpAddress = RdpSession.GetClientAddress();
            textIp.Text = presetIp ?? "";
        }

        /// <summary>Banlama sonrası güncel liste.</summary>
        public BanListResult Result { get; private set; }

        private async void buttonSave_Click(object sender, EventArgs e)
        {
            if (_busy) return;
            string ip = textIp.Text.Trim();
            if (ip.Length == 0)
            {
                ShowMessage("IP adresi girin.");
                textIp.Focus();
                return;
            }
            if (_rdpAddress != null && string.Equals(ip, _rdpAddress, StringComparison.OrdinalIgnoreCase))
            {
                ShowMessage("Bu IP, şu an bağlı olduğunuz RDP oturumunun adresi. Banlarsanız bağlantınız kopar ve sunucuya giremezsiniz.");
                return;
            }

            SetBusy(true);
            var result = await _client.SendAsync<BanListResult>(Commands.BanAdd, new BanAddRequest { IpAddress = ip, Reason = textReason.Text.Trim() });
            SetBusy(false);

            if (result.Success)
            {
                Result = result.Value;
                DialogResult = DialogResult.OK;
                Close();
                return;
            }
            if (result.IsUnauthorized)
            {
                DialogResult = DialogResult.Abort;
                Close();
                return;
            }
            ShowMessage(result.ErrorMessage);
            textIp.Focus();
        }

        private void SetBusy(bool busy)
        {
            _busy = busy;
            textIp.Enabled = !busy;
            textReason.Enabled = !busy;
            buttonSave.Enabled = !busy;
            UseWaitCursor = busy;
            if (busy) labelMessage.Visible = false;
        }

        private void ShowMessage(string message)
        {
            labelMessage.Text = message;
            labelMessage.Visible = true;
        }

        protected override void OnFormClosing(FormClosingEventArgs e)
        {
            if (_busy) e.Cancel = true;
            base.OnFormClosing(e);
        }
    }
}
