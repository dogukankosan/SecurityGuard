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
    /// Beyaz listeye adres ekleme. Adres kuralları servis tarafında kontrol edilir; hata olursa pencere açık kalır.
    /// Sonuç: OK = eklendi, Abort = oturum düştü, Cancel = vazgeçildi.
    /// </summary>
    public partial class WhitelistAddForm : XtraForm
    {
        private readonly GuardClient _client;
        private readonly string _rdpAddress;
        private bool _busy;

        public WhitelistAddForm(GuardClient client)
        {
            InitializeComponent();
            Icon = AppImages.Icon;
            HandCursor.Apply(this);
            _client = client;

            _rdpAddress = RdpSession.GetClientAddress();
            if (_rdpAddress != null)
            {
                labelRdp.Text = "Bu RDP oturumunun bağlandığı IP: " + _rdpAddress;
            }
            else
            {
                labelRdp.Text = "Bu oturum RDP ile açılmamış (konsol oturumu).";
                buttonUseRdp.Enabled = false;
            }
        }

        private void buttonUseRdp_Click(object sender, EventArgs e)
        {
            textCidr.Text = _rdpAddress;
            if (string.IsNullOrWhiteSpace(textNote.Text))
                textNote.Text = "Yöneticinin RDP bağlantı IP'si";
            textNote.Focus();
        }

        private async void buttonSave_Click(object sender, EventArgs e)
        {
            if (_busy) return;
            if (string.IsNullOrWhiteSpace(textCidr.Text))
            {
                ShowMessage("IP adresi veya aralık girin.");
                textCidr.Focus();
                return;
            }

            SetBusy(true);
            var result = await _client.SendAsync<WhitelistEntry>(Commands.WhitelistAdd,
                new WhitelistAddRequest { Cidr = textCidr.Text.Trim(), Note = textNote.Text.Trim() });
            SetBusy(false);

            if (result.Success)
            {
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
            textCidr.Focus();
        }

        private void SetBusy(bool busy)
        {
            _busy = busy;
            textCidr.Enabled = !busy;
            textNote.Enabled = !busy;
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
