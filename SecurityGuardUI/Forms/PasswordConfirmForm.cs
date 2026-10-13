using System;
using System.Windows.Forms;
using DevExpress.XtraEditors;
using SecurityGuardUI.Helpers;

namespace SecurityGuardUI.Forms
{
    /// <summary>Kritik işlemden (servisi durdurma, kaldırma) önce yönetici şifresinin tekrar girilmesi.</summary>
    public partial class PasswordConfirmForm : XtraForm
    {
        public PasswordConfirmForm(string action, string warning)
        {
            InitializeComponent();
            Icon = AppImages.Icon;
            HandCursor.Apply(this);
            labelAction.Text = action;
            labelWarning.Text = warning;
            labelWarning.Appearance.ForeColor = UiColors.Warning;
        }

        public string Password { get { return textPassword.Text; } }

        protected override void OnShown(EventArgs e)
        {
            base.OnShown(e);
            textPassword.Focus();
        }

        private void buttonOk_Click(object sender, EventArgs e)
        {
            if (textPassword.Text.Length == 0)
            {
                textPassword.Focus();
                return;
            }
            DialogResult = DialogResult.OK;
            Close();
        }
    }
}
