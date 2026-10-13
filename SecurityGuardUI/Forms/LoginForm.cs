using System;
using System.Windows.Forms;
using DevExpress.XtraEditors;
using SecurityGuardUI.Helpers;
using SecurityGuardUI.Ipc;

namespace SecurityGuardUI.Forms
{
    /// <summary>Yönetici girişi. Başarılı girişte DialogResult.OK döner, oturum token'ı GuardClient'ta kalır.</summary>
    public partial class LoginForm : XtraForm
    {
        private readonly GuardClient _client;
        private bool _busy;

        public LoginForm(GuardClient client, string machineName, string infoMessage = null)
        {
            InitializeComponent();
            Icon = AppImages.Icon;
            HandCursor.Apply(this);
            pictureLogo.Image = AppImages.Logo;
            _client = client;
            labelServer.Text = "Sunucu: " + machineName;
            if (!string.IsNullOrEmpty(infoMessage))
                ShowMessage(infoMessage);
        }

        protected override void OnShown(EventArgs e)
        {
            base.OnShown(e);
            textUser.Focus();
        }

        private async void buttonLogin_Click(object sender, EventArgs e)
        {
            if (_busy) return;

            string user = textUser.Text.Trim();
            string password = textPassword.Text;
            if (user.Length == 0 || password.Length == 0)
            {
                ShowMessage("Kullanıcı adı ve şifre girin.");
                return;
            }

            SetBusy(true);
            var result = await _client.LoginAsync(user, password);
            SetBusy(false);

            if (result.Success)
            {
                DialogResult = DialogResult.OK;
                Close();
                return;
            }

            ShowMessage(result.ErrorMessage);
            textPassword.Text = "";
            textPassword.Focus();
        }

        private void SetBusy(bool busy)
        {
            _busy = busy;
            textUser.Enabled = !busy;
            textPassword.Enabled = !busy;
            buttonLogin.Enabled = !busy;
            progress.Visible = busy;
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
            // Giriş isteği sürerken kapatılmasın (cevap yarıda kalmasın)
            if (_busy) e.Cancel = true;
            base.OnFormClosing(e);
        }
    }
}
