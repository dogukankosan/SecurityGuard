namespace SecurityGuardUI.Forms
{
    partial class WhitelistAddForm
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
            this.labelCidr = new DevExpress.XtraEditors.LabelControl();
            this.textCidr = new DevExpress.XtraEditors.TextEdit();
            this.labelHint = new DevExpress.XtraEditors.LabelControl();
            this.labelRdp = new DevExpress.XtraEditors.LabelControl();
            this.buttonUseRdp = new DevExpress.XtraEditors.SimpleButton();
            this.labelNote = new DevExpress.XtraEditors.LabelControl();
            this.textNote = new DevExpress.XtraEditors.TextEdit();
            this.labelMessage = new DevExpress.XtraEditors.LabelControl();
            this.buttonSave = new DevExpress.XtraEditors.SimpleButton();
            this.buttonCancel = new DevExpress.XtraEditors.SimpleButton();
            ((System.ComponentModel.ISupportInitialize)(this.textCidr.Properties)).BeginInit();
            ((System.ComponentModel.ISupportInitialize)(this.textNote.Properties)).BeginInit();
            this.SuspendLayout();
            //
            // labelCidr
            //
            this.labelCidr.Location = new System.Drawing.Point(24, 22);
            this.labelCidr.Name = "labelCidr";
            this.labelCidr.Size = new System.Drawing.Size(99, 13);
            this.labelCidr.TabIndex = 0;
            this.labelCidr.Text = "IP adresi veya aralık";
            //
            // textCidr
            //
            this.textCidr.Location = new System.Drawing.Point(24, 41);
            this.textCidr.Name = "textCidr";
            this.textCidr.Properties.MaxLength = 64;
            this.textCidr.Size = new System.Drawing.Size(386, 20);
            this.textCidr.TabIndex = 1;
            //
            // labelHint
            //
            this.labelHint.Appearance.ForeColor = DevExpress.LookAndFeel.DXSkinColors.ForeColors.ControlText;
            this.labelHint.Appearance.Options.UseForeColor = true;
            this.labelHint.Location = new System.Drawing.Point(24, 66);
            this.labelHint.Name = "labelHint";
            this.labelHint.Size = new System.Drawing.Size(290, 13);
            this.labelHint.TabIndex = 2;
            this.labelHint.Text = "Örnek: 88.12.34.56 (tek IP) veya 88.12.34.0/24 (aralık)";
            //
            // labelRdp
            //
            this.labelRdp.Location = new System.Drawing.Point(24, 98);
            this.labelRdp.Name = "labelRdp";
            this.labelRdp.Size = new System.Drawing.Size(180, 13);
            this.labelRdp.TabIndex = 3;
            this.labelRdp.Text = "Bu RDP oturumunun bağlandığı IP:";
            //
            // buttonUseRdp
            //
            this.buttonUseRdp.Location = new System.Drawing.Point(290, 92);
            this.buttonUseRdp.Name = "buttonUseRdp";
            this.buttonUseRdp.Size = new System.Drawing.Size(120, 24);
            this.buttonUseRdp.TabIndex = 4;
            this.buttonUseRdp.Text = "Bu IP\'yi kullan";
            this.buttonUseRdp.Click += new System.EventHandler(this.buttonUseRdp_Click);
            //
            // labelNote
            //
            this.labelNote.Location = new System.Drawing.Point(24, 132);
            this.labelNote.Name = "labelNote";
            this.labelNote.Size = new System.Drawing.Size(68, 13);
            this.labelNote.TabIndex = 5;
            this.labelNote.Text = "Not (isteğe bağlı)";
            //
            // textNote
            //
            this.textNote.Location = new System.Drawing.Point(24, 151);
            this.textNote.Name = "textNote";
            this.textNote.Properties.MaxLength = 200;
            this.textNote.Properties.NullValuePrompt = "Örn: Merkez ofis sabit IP";
            this.textNote.Size = new System.Drawing.Size(386, 20);
            this.textNote.TabIndex = 6;
            //
            // labelMessage
            //
            this.labelMessage.Appearance.ForeColor = DevExpress.LookAndFeel.DXSkinColors.ForeColors.Critical;
            this.labelMessage.Appearance.Options.UseForeColor = true;
            this.labelMessage.Appearance.Options.UseTextOptions = true;
            this.labelMessage.Appearance.TextOptions.WordWrap = DevExpress.Utils.WordWrap.Wrap;
            this.labelMessage.AutoSizeMode = DevExpress.XtraEditors.LabelAutoSizeMode.None;
            this.labelMessage.Location = new System.Drawing.Point(24, 182);
            this.labelMessage.Name = "labelMessage";
            this.labelMessage.Size = new System.Drawing.Size(386, 30);
            this.labelMessage.TabIndex = 7;
            this.labelMessage.Visible = false;
            //
            // buttonSave
            //
            this.buttonSave.Location = new System.Drawing.Point(234, 222);
            this.buttonSave.Name = "buttonSave";
            this.buttonSave.Size = new System.Drawing.Size(86, 30);
            this.buttonSave.TabIndex = 8;
            this.buttonSave.Text = "Ekle";
            this.buttonSave.Click += new System.EventHandler(this.buttonSave_Click);
            //
            // buttonCancel
            //
            this.buttonCancel.DialogResult = System.Windows.Forms.DialogResult.Cancel;
            this.buttonCancel.Location = new System.Drawing.Point(326, 222);
            this.buttonCancel.Name = "buttonCancel";
            this.buttonCancel.Size = new System.Drawing.Size(84, 30);
            this.buttonCancel.TabIndex = 9;
            this.buttonCancel.Text = "Vazgeç";
            //
            // WhitelistAddForm
            //
            this.AcceptButton = this.buttonSave;
            this.AutoScaleDimensions = new System.Drawing.SizeF(6F, 13F);
            this.AutoScaleMode = System.Windows.Forms.AutoScaleMode.Font;
            this.CancelButton = this.buttonCancel;
            this.ClientSize = new System.Drawing.Size(434, 270);
            this.Controls.Add(this.buttonCancel);
            this.Controls.Add(this.buttonSave);
            this.Controls.Add(this.labelMessage);
            this.Controls.Add(this.textNote);
            this.Controls.Add(this.labelNote);
            this.Controls.Add(this.buttonUseRdp);
            this.Controls.Add(this.labelRdp);
            this.Controls.Add(this.labelHint);
            this.Controls.Add(this.textCidr);
            this.Controls.Add(this.labelCidr);
            this.FormBorderStyle = System.Windows.Forms.FormBorderStyle.FixedDialog;
            this.MaximizeBox = false;
            this.MinimizeBox = false;
            this.Name = "WhitelistAddForm";
            this.ShowInTaskbar = false;
            this.StartPosition = System.Windows.Forms.FormStartPosition.CenterParent;
            this.Text = "Beyaz Listeye Ekle";
            ((System.ComponentModel.ISupportInitialize)(this.textCidr.Properties)).EndInit();
            ((System.ComponentModel.ISupportInitialize)(this.textNote.Properties)).EndInit();
            this.ResumeLayout(false);
            this.PerformLayout();

        }

        #endregion

        private DevExpress.XtraEditors.LabelControl labelCidr;
        private DevExpress.XtraEditors.TextEdit textCidr;
        private DevExpress.XtraEditors.LabelControl labelHint;
        private DevExpress.XtraEditors.LabelControl labelRdp;
        private DevExpress.XtraEditors.SimpleButton buttonUseRdp;
        private DevExpress.XtraEditors.LabelControl labelNote;
        private DevExpress.XtraEditors.TextEdit textNote;
        private DevExpress.XtraEditors.LabelControl labelMessage;
        private DevExpress.XtraEditors.SimpleButton buttonSave;
        private DevExpress.XtraEditors.SimpleButton buttonCancel;
    }
}
