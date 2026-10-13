namespace SecurityGuardUI.Forms
{
    partial class RecipientPickerForm
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
            this.labelInfo = new DevExpress.XtraEditors.LabelControl();
            this.listRecipients = new DevExpress.XtraEditors.CheckedListBoxControl();
            this.labelSummary = new DevExpress.XtraEditors.LabelControl();
            this.buttonClear = new DevExpress.XtraEditors.SimpleButton();
            this.buttonOk = new DevExpress.XtraEditors.SimpleButton();
            this.buttonCancel = new DevExpress.XtraEditors.SimpleButton();
            ((System.ComponentModel.ISupportInitialize)(this.listRecipients)).BeginInit();
            this.SuspendLayout();
            //
            // labelInfo
            //
            this.labelInfo.Appearance.Options.UseTextOptions = true;
            this.labelInfo.Appearance.TextOptions.WordWrap = DevExpress.Utils.WordWrap.Wrap;
            this.labelInfo.AutoSizeMode = DevExpress.XtraEditors.LabelAutoSizeMode.None;
            this.labelInfo.Location = new System.Drawing.Point(16, 14);
            this.labelInfo.Name = "labelInfo";
            this.labelInfo.Size = new System.Drawing.Size(392, 32);
            this.labelInfo.TabIndex = 0;
            //
            // listRecipients
            //
            this.listRecipients.CheckOnClick = true;
            this.listRecipients.Location = new System.Drawing.Point(16, 52);
            this.listRecipients.Name = "listRecipients";
            this.listRecipients.Size = new System.Drawing.Size(392, 220);
            this.listRecipients.TabIndex = 1;
            this.listRecipients.ItemCheck += new DevExpress.XtraEditors.Controls.ItemCheckEventHandler(this.listRecipients_ItemCheck);
            //
            // labelSummary
            //
            this.labelSummary.Location = new System.Drawing.Point(16, 282);
            this.labelSummary.Name = "labelSummary";
            this.labelSummary.Size = new System.Drawing.Size(120, 13);
            this.labelSummary.TabIndex = 2;
            //
            // buttonClear
            //
            this.buttonClear.Location = new System.Drawing.Point(248, 276);
            this.buttonClear.Name = "buttonClear";
            this.buttonClear.Size = new System.Drawing.Size(160, 24);
            this.buttonClear.TabIndex = 3;
            this.buttonClear.Text = "Seçimi Temizle (tümü)";
            this.buttonClear.Click += new System.EventHandler(this.buttonClear_Click);
            //
            // buttonOk
            //
            this.buttonOk.DialogResult = System.Windows.Forms.DialogResult.OK;
            this.buttonOk.Location = new System.Drawing.Point(232, 316);
            this.buttonOk.Name = "buttonOk";
            this.buttonOk.Size = new System.Drawing.Size(86, 30);
            this.buttonOk.TabIndex = 4;
            this.buttonOk.Text = "Uygula";
            //
            // buttonCancel
            //
            this.buttonCancel.DialogResult = System.Windows.Forms.DialogResult.Cancel;
            this.buttonCancel.Location = new System.Drawing.Point(324, 316);
            this.buttonCancel.Name = "buttonCancel";
            this.buttonCancel.Size = new System.Drawing.Size(84, 30);
            this.buttonCancel.TabIndex = 5;
            this.buttonCancel.Text = "Vazgeç";
            //
            // RecipientPickerForm
            //
            this.AcceptButton = this.buttonOk;
            this.AutoScaleDimensions = new System.Drawing.SizeF(6F, 13F);
            this.AutoScaleMode = System.Windows.Forms.AutoScaleMode.Font;
            this.CancelButton = this.buttonCancel;
            this.ClientSize = new System.Drawing.Size(424, 362);
            this.Controls.Add(this.buttonCancel);
            this.Controls.Add(this.buttonOk);
            this.Controls.Add(this.buttonClear);
            this.Controls.Add(this.labelSummary);
            this.Controls.Add(this.listRecipients);
            this.Controls.Add(this.labelInfo);
            this.FormBorderStyle = System.Windows.Forms.FormBorderStyle.FixedDialog;
            this.MaximizeBox = false;
            this.MinimizeBox = false;
            this.Name = "RecipientPickerForm";
            this.ShowInTaskbar = false;
            this.StartPosition = System.Windows.Forms.FormStartPosition.CenterParent;
            this.Text = "Mail Alıcıları";
            ((System.ComponentModel.ISupportInitialize)(this.listRecipients)).EndInit();
            this.ResumeLayout(false);

        }

        #endregion

        private DevExpress.XtraEditors.LabelControl labelInfo;
        private DevExpress.XtraEditors.CheckedListBoxControl listRecipients;
        private DevExpress.XtraEditors.LabelControl labelSummary;
        private DevExpress.XtraEditors.SimpleButton buttonClear;
        private DevExpress.XtraEditors.SimpleButton buttonOk;
        private DevExpress.XtraEditors.SimpleButton buttonCancel;
    }
}
