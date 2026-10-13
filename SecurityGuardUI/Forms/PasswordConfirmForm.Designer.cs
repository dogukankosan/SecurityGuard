namespace SecurityGuardUI.Forms
{
    partial class PasswordConfirmForm
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
            this.labelAction = new DevExpress.XtraEditors.LabelControl();
            this.labelWarning = new DevExpress.XtraEditors.LabelControl();
            this.labelPassword = new DevExpress.XtraEditors.LabelControl();
            this.textPassword = new DevExpress.XtraEditors.TextEdit();
            this.buttonOk = new DevExpress.XtraEditors.SimpleButton();
            this.buttonCancel = new DevExpress.XtraEditors.SimpleButton();
            ((System.ComponentModel.ISupportInitialize)(this.textPassword.Properties)).BeginInit();
            this.SuspendLayout();
            //
            // labelAction
            //
            this.labelAction.Appearance.Font = new System.Drawing.Font("Segoe UI", 11F);
            this.labelAction.Appearance.Options.UseFont = true;
            this.labelAction.Appearance.Options.UseTextOptions = true;
            this.labelAction.Appearance.TextOptions.WordWrap = DevExpress.Utils.WordWrap.Wrap;
            this.labelAction.AutoSizeMode = DevExpress.XtraEditors.LabelAutoSizeMode.None;
            this.labelAction.Location = new System.Drawing.Point(20, 18);
            this.labelAction.Name = "labelAction";
            this.labelAction.Size = new System.Drawing.Size(376, 24);
            this.labelAction.TabIndex = 0;
            //
            // labelWarning
            //
            this.labelWarning.Appearance.Options.UseForeColor = true;
            this.labelWarning.Appearance.Options.UseTextOptions = true;
            this.labelWarning.Appearance.TextOptions.WordWrap = DevExpress.Utils.WordWrap.Wrap;
            this.labelWarning.AutoSizeMode = DevExpress.XtraEditors.LabelAutoSizeMode.None;
            this.labelWarning.Location = new System.Drawing.Point(20, 48);
            this.labelWarning.Name = "labelWarning";
            this.labelWarning.Size = new System.Drawing.Size(376, 44);
            this.labelWarning.TabIndex = 1;
            //
            // labelPassword
            //
            this.labelPassword.Location = new System.Drawing.Point(20, 102);
            this.labelPassword.Name = "labelPassword";
            this.labelPassword.Size = new System.Drawing.Size(104, 13);
            this.labelPassword.TabIndex = 2;
            this.labelPassword.Text = "Yönetici şifresi (onay)";
            //
            // textPassword
            //
            this.textPassword.Location = new System.Drawing.Point(20, 121);
            this.textPassword.Name = "textPassword";
            this.textPassword.Properties.MaxLength = 128;
            this.textPassword.Properties.UseSystemPasswordChar = true;
            this.textPassword.Size = new System.Drawing.Size(376, 20);
            this.textPassword.TabIndex = 3;
            //
            // buttonOk
            //
            this.buttonOk.Location = new System.Drawing.Point(220, 160);
            this.buttonOk.Name = "buttonOk";
            this.buttonOk.Size = new System.Drawing.Size(86, 30);
            this.buttonOk.TabIndex = 4;
            this.buttonOk.Text = "Onayla";
            this.buttonOk.Click += new System.EventHandler(this.buttonOk_Click);
            //
            // buttonCancel
            //
            this.buttonCancel.DialogResult = System.Windows.Forms.DialogResult.Cancel;
            this.buttonCancel.Location = new System.Drawing.Point(312, 160);
            this.buttonCancel.Name = "buttonCancel";
            this.buttonCancel.Size = new System.Drawing.Size(84, 30);
            this.buttonCancel.TabIndex = 5;
            this.buttonCancel.Text = "Vazgeç";
            //
            // PasswordConfirmForm
            //
            this.AcceptButton = this.buttonOk;
            this.AutoScaleDimensions = new System.Drawing.SizeF(6F, 13F);
            this.AutoScaleMode = System.Windows.Forms.AutoScaleMode.Font;
            this.CancelButton = this.buttonCancel;
            this.ClientSize = new System.Drawing.Size(416, 208);
            this.Controls.Add(this.buttonCancel);
            this.Controls.Add(this.buttonOk);
            this.Controls.Add(this.textPassword);
            this.Controls.Add(this.labelPassword);
            this.Controls.Add(this.labelWarning);
            this.Controls.Add(this.labelAction);
            this.FormBorderStyle = System.Windows.Forms.FormBorderStyle.FixedDialog;
            this.MaximizeBox = false;
            this.MinimizeBox = false;
            this.Name = "PasswordConfirmForm";
            this.ShowInTaskbar = false;
            this.StartPosition = System.Windows.Forms.FormStartPosition.CenterParent;
            this.Text = "Onay";
            ((System.ComponentModel.ISupportInitialize)(this.textPassword.Properties)).EndInit();
            this.ResumeLayout(false);

        }

        #endregion

        private DevExpress.XtraEditors.LabelControl labelAction;
        private DevExpress.XtraEditors.LabelControl labelWarning;
        private DevExpress.XtraEditors.LabelControl labelPassword;
        private DevExpress.XtraEditors.TextEdit textPassword;
        private DevExpress.XtraEditors.SimpleButton buttonOk;
        private DevExpress.XtraEditors.SimpleButton buttonCancel;
    }
}
