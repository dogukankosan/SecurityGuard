namespace SecurityGuardUI.Forms
{
    partial class BanAddForm
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
            this.labelIp = new DevExpress.XtraEditors.LabelControl();
            this.textIp = new DevExpress.XtraEditors.TextEdit();
            this.labelHint = new DevExpress.XtraEditors.LabelControl();
            this.labelReason = new DevExpress.XtraEditors.LabelControl();
            this.textReason = new DevExpress.XtraEditors.TextEdit();
            this.labelMessage = new DevExpress.XtraEditors.LabelControl();
            this.buttonSave = new DevExpress.XtraEditors.SimpleButton();
            this.buttonCancel = new DevExpress.XtraEditors.SimpleButton();
            ((System.ComponentModel.ISupportInitialize)(this.textIp.Properties)).BeginInit();
            ((System.ComponentModel.ISupportInitialize)(this.textReason.Properties)).BeginInit();
            this.SuspendLayout();
            //
            // labelIp
            //
            this.labelIp.Location = new System.Drawing.Point(24, 22);
            this.labelIp.Name = "labelIp";
            this.labelIp.Size = new System.Drawing.Size(47, 13);
            this.labelIp.TabIndex = 0;
            this.labelIp.Text = "IP adresi";
            //
            // textIp
            //
            this.textIp.Location = new System.Drawing.Point(24, 41);
            this.textIp.Name = "textIp";
            this.textIp.Properties.MaxLength = 64;
            this.textIp.Size = new System.Drawing.Size(386, 20);
            this.textIp.TabIndex = 1;
            //
            // labelHint
            //
            this.labelHint.Appearance.Options.UseTextOptions = true;
            this.labelHint.Appearance.TextOptions.WordWrap = DevExpress.Utils.WordWrap.Wrap;
            this.labelHint.AutoSizeMode = DevExpress.XtraEditors.LabelAutoSizeMode.None;
            this.labelHint.Location = new System.Drawing.Point(24, 66);
            this.labelHint.Name = "labelHint";
            this.labelHint.Size = new System.Drawing.Size(386, 28);
            this.labelHint.TabIndex = 2;
            this.labelHint.Text = "Tek IP (örn: 185.220.101.5). Firewall\'da bu IP\'den gelen tüm trafik engellenir. İç ağ, localhost, sunucu IP\'leri ve beyaz liste banlanamaz.";
            //
            // labelReason
            //
            this.labelReason.Location = new System.Drawing.Point(24, 104);
            this.labelReason.Name = "labelReason";
            this.labelReason.Size = new System.Drawing.Size(92, 13);
            this.labelReason.TabIndex = 3;
            this.labelReason.Text = "Sebep (isteğe bağlı)";
            //
            // textReason
            //
            this.textReason.Location = new System.Drawing.Point(24, 123);
            this.textReason.Name = "textReason";
            this.textReason.Properties.MaxLength = 200;
            this.textReason.Properties.NullValuePrompt = "Örn: Port taraması yapıyor";
            this.textReason.Size = new System.Drawing.Size(386, 20);
            this.textReason.TabIndex = 4;
            //
            // labelMessage
            //
            this.labelMessage.Appearance.ForeColor = DevExpress.LookAndFeel.DXSkinColors.ForeColors.Critical;
            this.labelMessage.Appearance.Options.UseForeColor = true;
            this.labelMessage.Appearance.Options.UseTextOptions = true;
            this.labelMessage.Appearance.TextOptions.WordWrap = DevExpress.Utils.WordWrap.Wrap;
            this.labelMessage.AutoSizeMode = DevExpress.XtraEditors.LabelAutoSizeMode.None;
            this.labelMessage.Location = new System.Drawing.Point(24, 154);
            this.labelMessage.Name = "labelMessage";
            this.labelMessage.Size = new System.Drawing.Size(386, 30);
            this.labelMessage.TabIndex = 5;
            this.labelMessage.Visible = false;
            //
            // buttonSave
            //
            this.buttonSave.Location = new System.Drawing.Point(234, 194);
            this.buttonSave.Name = "buttonSave";
            this.buttonSave.Size = new System.Drawing.Size(86, 30);
            this.buttonSave.TabIndex = 6;
            this.buttonSave.Text = "Banla";
            this.buttonSave.Click += new System.EventHandler(this.buttonSave_Click);
            //
            // buttonCancel
            //
            this.buttonCancel.DialogResult = System.Windows.Forms.DialogResult.Cancel;
            this.buttonCancel.Location = new System.Drawing.Point(326, 194);
            this.buttonCancel.Name = "buttonCancel";
            this.buttonCancel.Size = new System.Drawing.Size(84, 30);
            this.buttonCancel.TabIndex = 7;
            this.buttonCancel.Text = "Vazgeç";
            //
            // BanAddForm
            //
            this.AcceptButton = this.buttonSave;
            this.AutoScaleDimensions = new System.Drawing.SizeF(6F, 13F);
            this.AutoScaleMode = System.Windows.Forms.AutoScaleMode.Font;
            this.CancelButton = this.buttonCancel;
            this.ClientSize = new System.Drawing.Size(434, 242);
            this.Controls.Add(this.buttonCancel);
            this.Controls.Add(this.buttonSave);
            this.Controls.Add(this.labelMessage);
            this.Controls.Add(this.textReason);
            this.Controls.Add(this.labelReason);
            this.Controls.Add(this.labelHint);
            this.Controls.Add(this.textIp);
            this.Controls.Add(this.labelIp);
            this.FormBorderStyle = System.Windows.Forms.FormBorderStyle.FixedDialog;
            this.MaximizeBox = false;
            this.MinimizeBox = false;
            this.Name = "BanAddForm";
            this.ShowInTaskbar = false;
            this.StartPosition = System.Windows.Forms.FormStartPosition.CenterParent;
            this.Text = "IP Banla";
            ((System.ComponentModel.ISupportInitialize)(this.textIp.Properties)).EndInit();
            ((System.ComponentModel.ISupportInitialize)(this.textReason.Properties)).EndInit();
            this.ResumeLayout(false);
            this.PerformLayout();

        }

        #endregion

        private DevExpress.XtraEditors.LabelControl labelIp;
        private DevExpress.XtraEditors.TextEdit textIp;
        private DevExpress.XtraEditors.LabelControl labelHint;
        private DevExpress.XtraEditors.LabelControl labelReason;
        private DevExpress.XtraEditors.TextEdit textReason;
        private DevExpress.XtraEditors.LabelControl labelMessage;
        private DevExpress.XtraEditors.SimpleButton buttonSave;
        private DevExpress.XtraEditors.SimpleButton buttonCancel;
    }
}
