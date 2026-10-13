namespace SecurityGuardUI.Forms
{
    partial class LoginForm
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
            this.labelTitle = new DevExpress.XtraEditors.LabelControl();
            this.labelServer = new DevExpress.XtraEditors.LabelControl();
            this.labelUser = new DevExpress.XtraEditors.LabelControl();
            this.textUser = new DevExpress.XtraEditors.TextEdit();
            this.labelPassword = new DevExpress.XtraEditors.LabelControl();
            this.textPassword = new DevExpress.XtraEditors.TextEdit();
            this.labelMessage = new DevExpress.XtraEditors.LabelControl();
            this.progress = new DevExpress.XtraEditors.MarqueeProgressBarControl();
            this.buttonLogin = new DevExpress.XtraEditors.SimpleButton();
            this.buttonCancel = new DevExpress.XtraEditors.SimpleButton();
            ((System.ComponentModel.ISupportInitialize)(this.pictureLogo)).BeginInit();
            ((System.ComponentModel.ISupportInitialize)(this.textUser.Properties)).BeginInit();
            ((System.ComponentModel.ISupportInitialize)(this.textPassword.Properties)).BeginInit();
            ((System.ComponentModel.ISupportInitialize)(this.progress.Properties)).BeginInit();
            this.SuspendLayout();
            //
            // pictureLogo
            //
            this.pictureLogo.BackColor = System.Drawing.Color.Transparent;
            this.pictureLogo.Location = new System.Drawing.Point(34, 22);
            this.pictureLogo.Name = "pictureLogo";
            this.pictureLogo.Size = new System.Drawing.Size(52, 52);
            this.pictureLogo.SizeMode = System.Windows.Forms.PictureBoxSizeMode.Zoom;
            this.pictureLogo.TabIndex = 10;
            this.pictureLogo.TabStop = false;
            //
            // labelTitle
            //
            this.labelTitle.Appearance.Font = new System.Drawing.Font("Segoe UI", 18F);
            this.labelTitle.Appearance.Options.UseFont = true;
            this.labelTitle.Location = new System.Drawing.Point(96, 20);
            this.labelTitle.Name = "labelTitle";
            this.labelTitle.Size = new System.Drawing.Size(150, 32);
            this.labelTitle.TabIndex = 0;
            this.labelTitle.Text = "SecurityGuard";
            //
            // labelServer
            //
            this.labelServer.Location = new System.Drawing.Point(98, 58);
            this.labelServer.Name = "labelServer";
            this.labelServer.Size = new System.Drawing.Size(40, 13);
            this.labelServer.TabIndex = 1;
            this.labelServer.Text = "Sunucu:";
            //
            // labelUser
            //
            this.labelUser.Location = new System.Drawing.Point(34, 100);
            this.labelUser.Name = "labelUser";
            this.labelUser.Size = new System.Drawing.Size(62, 13);
            this.labelUser.TabIndex = 2;
            this.labelUser.Text = "Kullanıcı adı";
            //
            // textUser
            //
            this.textUser.Location = new System.Drawing.Point(34, 119);
            this.textUser.Name = "textUser";
            this.textUser.Properties.MaxLength = 50;
            this.textUser.Size = new System.Drawing.Size(366, 20);
            this.textUser.TabIndex = 3;
            //
            // labelPassword
            //
            this.labelPassword.Location = new System.Drawing.Point(34, 154);
            this.labelPassword.Name = "labelPassword";
            this.labelPassword.Size = new System.Drawing.Size(24, 13);
            this.labelPassword.TabIndex = 4;
            this.labelPassword.Text = "Şifre";
            //
            // textPassword
            //
            this.textPassword.Location = new System.Drawing.Point(34, 173);
            this.textPassword.Name = "textPassword";
            this.textPassword.Properties.MaxLength = 128;
            this.textPassword.Properties.UseSystemPasswordChar = true;
            this.textPassword.Size = new System.Drawing.Size(366, 20);
            this.textPassword.TabIndex = 5;
            //
            // labelMessage
            //
            this.labelMessage.Appearance.ForeColor = DevExpress.LookAndFeel.DXSkinColors.ForeColors.Critical;
            this.labelMessage.Appearance.Options.UseForeColor = true;
            this.labelMessage.Appearance.Options.UseTextOptions = true;
            this.labelMessage.Appearance.TextOptions.WordWrap = DevExpress.Utils.WordWrap.Wrap;
            this.labelMessage.AutoSizeMode = DevExpress.XtraEditors.LabelAutoSizeMode.None;
            this.labelMessage.Location = new System.Drawing.Point(34, 204);
            this.labelMessage.Name = "labelMessage";
            this.labelMessage.Size = new System.Drawing.Size(366, 34);
            this.labelMessage.TabIndex = 6;
            this.labelMessage.Visible = false;
            //
            // progress
            //
            this.progress.EditValue = 0;
            this.progress.Location = new System.Drawing.Point(34, 258);
            this.progress.Name = "progress";
            this.progress.Size = new System.Drawing.Size(170, 16);
            this.progress.TabIndex = 7;
            this.progress.Visible = false;
            //
            // buttonLogin
            //
            this.buttonLogin.Location = new System.Drawing.Point(220, 250);
            this.buttonLogin.Name = "buttonLogin";
            this.buttonLogin.Size = new System.Drawing.Size(90, 30);
            this.buttonLogin.TabIndex = 8;
            this.buttonLogin.Text = "Giriş";
            this.buttonLogin.Click += new System.EventHandler(this.buttonLogin_Click);
            //
            // buttonCancel
            //
            this.buttonCancel.DialogResult = System.Windows.Forms.DialogResult.Cancel;
            this.buttonCancel.Location = new System.Drawing.Point(316, 250);
            this.buttonCancel.Name = "buttonCancel";
            this.buttonCancel.Size = new System.Drawing.Size(84, 30);
            this.buttonCancel.TabIndex = 9;
            this.buttonCancel.Text = "Kapat";
            //
            // LoginForm
            //
            this.AcceptButton = this.buttonLogin;
            this.AutoScaleDimensions = new System.Drawing.SizeF(6F, 13F);
            this.AutoScaleMode = System.Windows.Forms.AutoScaleMode.Font;
            this.CancelButton = this.buttonCancel;
            this.ClientSize = new System.Drawing.Size(434, 304);
            this.Controls.Add(this.buttonCancel);
            this.Controls.Add(this.buttonLogin);
            this.Controls.Add(this.progress);
            this.Controls.Add(this.labelMessage);
            this.Controls.Add(this.textPassword);
            this.Controls.Add(this.labelPassword);
            this.Controls.Add(this.textUser);
            this.Controls.Add(this.labelUser);
            this.Controls.Add(this.labelServer);
            this.Controls.Add(this.labelTitle);
            this.Controls.Add(this.pictureLogo);
            this.FormBorderStyle = System.Windows.Forms.FormBorderStyle.FixedDialog;
            this.MaximizeBox = false;
            this.MinimizeBox = false;
            this.Name = "LoginForm";
            this.StartPosition = System.Windows.Forms.FormStartPosition.CenterScreen;
            this.Text = "SecurityGuard - Giriş";
            ((System.ComponentModel.ISupportInitialize)(this.pictureLogo)).EndInit();
            ((System.ComponentModel.ISupportInitialize)(this.textUser.Properties)).EndInit();
            ((System.ComponentModel.ISupportInitialize)(this.textPassword.Properties)).EndInit();
            ((System.ComponentModel.ISupportInitialize)(this.progress.Properties)).EndInit();
            this.ResumeLayout(false);
            this.PerformLayout();

        }

        #endregion

        private System.Windows.Forms.PictureBox pictureLogo;
        private DevExpress.XtraEditors.LabelControl labelTitle;
        private DevExpress.XtraEditors.LabelControl labelServer;
        private DevExpress.XtraEditors.LabelControl labelUser;
        private DevExpress.XtraEditors.TextEdit textUser;
        private DevExpress.XtraEditors.LabelControl labelPassword;
        private DevExpress.XtraEditors.TextEdit textPassword;
        private DevExpress.XtraEditors.LabelControl labelMessage;
        private DevExpress.XtraEditors.MarqueeProgressBarControl progress;
        private DevExpress.XtraEditors.SimpleButton buttonLogin;
        private DevExpress.XtraEditors.SimpleButton buttonCancel;
    }
}
