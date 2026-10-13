namespace SecurityGuardUI.Forms
{
    partial class SetupAdminForm
    {
        /// <summary>
        /// Required designer variable.
        /// </summary>
        private System.ComponentModel.IContainer components = null;

        /// <summary>
        /// Clean up any resources being used.
        /// </summary>
        /// <param name="disposing">true if managed resources should be disposed; otherwise, false.</param>
        protected override void Dispose(bool disposing)
        {
            if (disposing && (components != null))
            {
                components.Dispose();
            }
            base.Dispose(disposing);
        }

        #region Windows Form Designer generated code

        /// <summary>
        /// Required method for Designer support - do not modify
        /// the contents of this method with the code editor.
        /// </summary>
        private void InitializeComponent()
        {
            this.pictureLogo = new System.Windows.Forms.PictureBox();
            this.labelStep = new DevExpress.XtraEditors.LabelControl();
            this.labelTitle = new DevExpress.XtraEditors.LabelControl();
            this.labelInfo = new DevExpress.XtraEditors.LabelControl();
            this.labelUser = new DevExpress.XtraEditors.LabelControl();
            this.textUser = new DevExpress.XtraEditors.TextEdit();
            this.labelPassword = new DevExpress.XtraEditors.LabelControl();
            this.textPassword = new DevExpress.XtraEditors.TextEdit();
            this.labelPasswordRepeat = new DevExpress.XtraEditors.LabelControl();
            this.textPasswordRepeat = new DevExpress.XtraEditors.TextEdit();
            this.labelRules = new DevExpress.XtraEditors.LabelControl();
            this.labelMessage = new DevExpress.XtraEditors.LabelControl();
            this.progress = new DevExpress.XtraEditors.MarqueeProgressBarControl();
            this.buttonCreate = new DevExpress.XtraEditors.SimpleButton();
            this.buttonCancel = new DevExpress.XtraEditors.SimpleButton();
            ((System.ComponentModel.ISupportInitialize)(this.pictureLogo)).BeginInit();
            ((System.ComponentModel.ISupportInitialize)(this.textUser.Properties)).BeginInit();
            ((System.ComponentModel.ISupportInitialize)(this.textPassword.Properties)).BeginInit();
            ((System.ComponentModel.ISupportInitialize)(this.textPasswordRepeat.Properties)).BeginInit();
            ((System.ComponentModel.ISupportInitialize)(this.progress.Properties)).BeginInit();
            this.SuspendLayout();
            //
            // pictureLogo
            //
            this.pictureLogo.BackColor = System.Drawing.Color.Transparent;
            this.pictureLogo.Location = new System.Drawing.Point(368, 20);
            this.pictureLogo.Name = "pictureLogo";
            this.pictureLogo.Size = new System.Drawing.Size(52, 52);
            this.pictureLogo.SizeMode = System.Windows.Forms.PictureBoxSizeMode.Zoom;
            this.pictureLogo.TabIndex = 14;
            this.pictureLogo.TabStop = false;
            //
            // labelStep
            //
            this.labelStep.Appearance.ForeColor = DevExpress.LookAndFeel.DXSkinColors.ForeColors.ControlText;
            this.labelStep.Appearance.Options.UseForeColor = true;
            this.labelStep.Location = new System.Drawing.Point(34, 20);
            this.labelStep.Name = "labelStep";
            this.labelStep.Size = new System.Drawing.Size(100, 13);
            this.labelStep.TabIndex = 0;
            this.labelStep.Text = "İlk kurulum · Adım 1 / 5";
            //
            // labelTitle
            //
            this.labelTitle.Appearance.Font = new System.Drawing.Font("Segoe UI", 16F);
            this.labelTitle.Appearance.Options.UseFont = true;
            this.labelTitle.Location = new System.Drawing.Point(32, 38);
            this.labelTitle.Name = "labelTitle";
            this.labelTitle.Size = new System.Drawing.Size(180, 30);
            this.labelTitle.TabIndex = 1;
            this.labelTitle.Text = "Yönetici hesabı";
            //
            // labelInfo
            //
            this.labelInfo.Appearance.Options.UseTextOptions = true;
            this.labelInfo.Appearance.TextOptions.WordWrap = DevExpress.Utils.WordWrap.Wrap;
            this.labelInfo.AutoSizeMode = DevExpress.XtraEditors.LabelAutoSizeMode.None;
            this.labelInfo.Location = new System.Drawing.Point(34, 78);
            this.labelInfo.Name = "labelInfo";
            this.labelInfo.Size = new System.Drawing.Size(386, 44);
            this.labelInfo.TabIndex = 2;
            this.labelInfo.Text = "SecurityGuard\'ı yönetmek için tek bir yönetici hesabı oluşturulur. Şifre kurtarma yoktur; şifrenizi güvenli bir yere not edin.";
            //
            // labelUser
            //
            this.labelUser.Location = new System.Drawing.Point(34, 134);
            this.labelUser.Name = "labelUser";
            this.labelUser.Size = new System.Drawing.Size(62, 13);
            this.labelUser.TabIndex = 3;
            this.labelUser.Text = "Kullanıcı adı";
            //
            // textUser
            //
            this.textUser.Location = new System.Drawing.Point(34, 153);
            this.textUser.Name = "textUser";
            this.textUser.Properties.MaxLength = 50;
            this.textUser.Size = new System.Drawing.Size(386, 20);
            this.textUser.TabIndex = 4;
            //
            // labelPassword
            //
            this.labelPassword.Location = new System.Drawing.Point(34, 186);
            this.labelPassword.Name = "labelPassword";
            this.labelPassword.Size = new System.Drawing.Size(24, 13);
            this.labelPassword.TabIndex = 5;
            this.labelPassword.Text = "Şifre";
            //
            // textPassword
            //
            this.textPassword.Location = new System.Drawing.Point(34, 205);
            this.textPassword.Name = "textPassword";
            this.textPassword.Properties.MaxLength = 128;
            this.textPassword.Properties.UseSystemPasswordChar = true;
            this.textPassword.Size = new System.Drawing.Size(386, 20);
            this.textPassword.TabIndex = 6;
            //
            // labelPasswordRepeat
            //
            this.labelPasswordRepeat.Location = new System.Drawing.Point(34, 238);
            this.labelPasswordRepeat.Name = "labelPasswordRepeat";
            this.labelPasswordRepeat.Size = new System.Drawing.Size(66, 13);
            this.labelPasswordRepeat.TabIndex = 7;
            this.labelPasswordRepeat.Text = "Şifre (tekrar)";
            //
            // textPasswordRepeat
            //
            this.textPasswordRepeat.Location = new System.Drawing.Point(34, 257);
            this.textPasswordRepeat.Name = "textPasswordRepeat";
            this.textPasswordRepeat.Properties.MaxLength = 128;
            this.textPasswordRepeat.Properties.UseSystemPasswordChar = true;
            this.textPasswordRepeat.Size = new System.Drawing.Size(386, 20);
            this.textPasswordRepeat.TabIndex = 8;
            //
            // labelRules
            //
            this.labelRules.Appearance.ForeColor = DevExpress.LookAndFeel.DXSkinColors.ForeColors.ControlText;
            this.labelRules.Appearance.Options.UseForeColor = true;
            this.labelRules.Appearance.Options.UseTextOptions = true;
            this.labelRules.Appearance.TextOptions.WordWrap = DevExpress.Utils.WordWrap.Wrap;
            this.labelRules.AutoSizeMode = DevExpress.XtraEditors.LabelAutoSizeMode.None;
            this.labelRules.Location = new System.Drawing.Point(34, 284);
            this.labelRules.Name = "labelRules";
            this.labelRules.Size = new System.Drawing.Size(386, 30);
            this.labelRules.TabIndex = 9;
            this.labelRules.Text = "Şifre en az 8 karakter olmalı, harf ve rakam içermeli, kullanıcı adını içermemeli.";
            //
            // labelMessage
            //
            this.labelMessage.Appearance.ForeColor = DevExpress.LookAndFeel.DXSkinColors.ForeColors.Critical;
            this.labelMessage.Appearance.Options.UseForeColor = true;
            this.labelMessage.Appearance.Options.UseTextOptions = true;
            this.labelMessage.Appearance.TextOptions.WordWrap = DevExpress.Utils.WordWrap.Wrap;
            this.labelMessage.AutoSizeMode = DevExpress.XtraEditors.LabelAutoSizeMode.None;
            this.labelMessage.Location = new System.Drawing.Point(34, 318);
            this.labelMessage.Name = "labelMessage";
            this.labelMessage.Size = new System.Drawing.Size(386, 34);
            this.labelMessage.TabIndex = 10;
            this.labelMessage.Visible = false;
            //
            // progress
            //
            this.progress.EditValue = 0;
            this.progress.Location = new System.Drawing.Point(34, 372);
            this.progress.Name = "progress";
            this.progress.Size = new System.Drawing.Size(150, 16);
            this.progress.TabIndex = 11;
            this.progress.Visible = false;
            //
            // buttonCreate
            //
            this.buttonCreate.Location = new System.Drawing.Point(200, 364);
            this.buttonCreate.Name = "buttonCreate";
            this.buttonCreate.Size = new System.Drawing.Size(130, 30);
            this.buttonCreate.TabIndex = 12;
            this.buttonCreate.Text = "Oluştur ve giriş yap";
            this.buttonCreate.Click += new System.EventHandler(this.buttonCreate_Click);
            //
            // buttonCancel
            //
            this.buttonCancel.DialogResult = System.Windows.Forms.DialogResult.Cancel;
            this.buttonCancel.Location = new System.Drawing.Point(336, 364);
            this.buttonCancel.Name = "buttonCancel";
            this.buttonCancel.Size = new System.Drawing.Size(84, 30);
            this.buttonCancel.TabIndex = 13;
            this.buttonCancel.Text = "Kapat";
            //
            // SetupAdminForm
            //
            this.AcceptButton = this.buttonCreate;
            this.AutoScaleDimensions = new System.Drawing.SizeF(6F, 13F);
            this.AutoScaleMode = System.Windows.Forms.AutoScaleMode.Font;
            this.CancelButton = this.buttonCancel;
            this.ClientSize = new System.Drawing.Size(454, 418);
            this.Controls.Add(this.buttonCancel);
            this.Controls.Add(this.buttonCreate);
            this.Controls.Add(this.progress);
            this.Controls.Add(this.labelMessage);
            this.Controls.Add(this.labelRules);
            this.Controls.Add(this.textPasswordRepeat);
            this.Controls.Add(this.labelPasswordRepeat);
            this.Controls.Add(this.textPassword);
            this.Controls.Add(this.labelPassword);
            this.Controls.Add(this.textUser);
            this.Controls.Add(this.labelUser);
            this.Controls.Add(this.labelInfo);
            this.Controls.Add(this.labelTitle);
            this.Controls.Add(this.labelStep);
            this.Controls.Add(this.pictureLogo);
            this.FormBorderStyle = System.Windows.Forms.FormBorderStyle.FixedDialog;
            this.MaximizeBox = false;
            this.MinimizeBox = false;
            this.Name = "SetupAdminForm";
            this.StartPosition = System.Windows.Forms.FormStartPosition.CenterScreen;
            this.Text = "SecurityGuard - İlk Kurulum";
            ((System.ComponentModel.ISupportInitialize)(this.pictureLogo)).EndInit();
            ((System.ComponentModel.ISupportInitialize)(this.textUser.Properties)).EndInit();
            ((System.ComponentModel.ISupportInitialize)(this.textPassword.Properties)).EndInit();
            ((System.ComponentModel.ISupportInitialize)(this.textPasswordRepeat.Properties)).EndInit();
            ((System.ComponentModel.ISupportInitialize)(this.progress.Properties)).EndInit();
            this.ResumeLayout(false);
            this.PerformLayout();

        }

        #endregion

        private System.Windows.Forms.PictureBox pictureLogo;
        private DevExpress.XtraEditors.LabelControl labelStep;
        private DevExpress.XtraEditors.LabelControl labelTitle;
        private DevExpress.XtraEditors.LabelControl labelInfo;
        private DevExpress.XtraEditors.LabelControl labelUser;
        private DevExpress.XtraEditors.TextEdit textUser;
        private DevExpress.XtraEditors.LabelControl labelPassword;
        private DevExpress.XtraEditors.TextEdit textPassword;
        private DevExpress.XtraEditors.LabelControl labelPasswordRepeat;
        private DevExpress.XtraEditors.TextEdit textPasswordRepeat;
        private DevExpress.XtraEditors.LabelControl labelRules;
        private DevExpress.XtraEditors.LabelControl labelMessage;
        private DevExpress.XtraEditors.MarqueeProgressBarControl progress;
        private DevExpress.XtraEditors.SimpleButton buttonCreate;
        private DevExpress.XtraEditors.SimpleButton buttonCancel;
    }
}
