using System;
using System.Windows.Forms;
using DevExpress.XtraEditors;
using SecurityGuardUI.Helpers;
using SecurityGuardUI.Ipc;

namespace SecurityGuardUI.Forms
{
    /// <summary>
    /// İlk kurulum 1. adım: tek yönetici hesabını oluşturur ve aynı bilgilerle giriş yapar.
    /// Şifre kuralları servis tarafında da kontrol edilir (UI atlatılsa bile zayıf şifre kabul edilmez).
    /// </summary>
    public partial class SetupAdminForm : XtraForm
    {
        private readonly GuardClient _client;
        private bool _busy;

        public SetupAdminForm(GuardClient client)
        {
            InitializeComponent();
            Icon = AppImages.Icon;
            HandCursor.Apply(this);
            pictureLogo.Image = AppImages.Logo;
            _client = client;
        }

        protected override void OnShown(EventArgs e)
        {
            base.OnShown(e);
            textUser.Focus();
        }

        private async void buttonCreate_Click(object sender, EventArgs e)
        {
            if (_busy) return;

            string user = textUser.Text.Trim();
            string password = textPassword.Text;

            if (user.Length == 0 || password.Length == 0)
            {
                ShowMessage("Kullanıcı adı ve şifre girin.");
                return;
            }
            if (password != textPasswordRepeat.Text)
            {
                ShowMessage("Şifreler aynı değil.");
                textPasswordRepeat.Text = "";
                textPasswordRepeat.Focus();
                return;
            }

            SetBusy(true);
            var created = await _client.CreateAdminAsync(user, password);
            if (!created.Success)
            {
                SetBusy(false);
                ShowMessage(created.ErrorMessage);
                return;
            }

            // Giriş başarısız olursa (bağlantı koptu vb.) hesap yine de oluşmuştur: akış giriş ekranını açar
            await _client.LoginAsync(user, password);
            SetBusy(false);

            DialogResult = DialogResult.OK;
            Close();
        }

        private void SetBusy(bool busy)
        {
            _busy = busy;
            textUser.Enabled = !busy;
            textPassword.Enabled = !busy;
            textPasswordRepeat.Enabled = !busy;
            buttonCreate.Enabled = !busy;
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
            if (_busy) e.Cancel = true;
            base.OnFormClosing(e);
        }
    }
}
