namespace SecurityGuardUI.Modules
{
    partial class WhitelistModule
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

        #region Component Designer generated code

        /// <summary>
        /// Required method for Designer support - do not modify
        /// the contents of this method with the code editor.
        /// </summary>
        private void InitializeComponent()
        {
            this.panelTop = new DevExpress.XtraEditors.PanelControl();
            this.labelTitle = new DevExpress.XtraEditors.LabelControl();
            this.labelInfo = new DevExpress.XtraEditors.LabelControl();
            this.buttonAdd = new DevExpress.XtraEditors.SimpleButton();
            this.buttonDelete = new DevExpress.XtraEditors.SimpleButton();
            this.buttonRefresh = new DevExpress.XtraEditors.SimpleButton();
            this.gridControl = new DevExpress.XtraGrid.GridControl();
            this.gridView = new DevExpress.XtraGrid.Views.Grid.GridView();
            this.colCidr = new DevExpress.XtraGrid.Columns.GridColumn();
            this.colNote = new DevExpress.XtraGrid.Columns.GridColumn();
            this.colType = new DevExpress.XtraGrid.Columns.GridColumn();
            this.colCreated = new DevExpress.XtraGrid.Columns.GridColumn();
            this.labelStatus = new DevExpress.XtraEditors.LabelControl();
            ((System.ComponentModel.ISupportInitialize)(this.panelTop)).BeginInit();
            this.panelTop.SuspendLayout();
            ((System.ComponentModel.ISupportInitialize)(this.gridControl)).BeginInit();
            ((System.ComponentModel.ISupportInitialize)(this.gridView)).BeginInit();
            this.SuspendLayout();
            //
            // panelTop
            //
            this.panelTop.BorderStyle = DevExpress.XtraEditors.Controls.BorderStyles.NoBorder;
            this.panelTop.Controls.Add(this.labelTitle);
            this.panelTop.Controls.Add(this.labelInfo);
            this.panelTop.Controls.Add(this.buttonAdd);
            this.panelTop.Controls.Add(this.buttonDelete);
            this.panelTop.Controls.Add(this.buttonRefresh);
            this.panelTop.Dock = System.Windows.Forms.DockStyle.Top;
            this.panelTop.Location = new System.Drawing.Point(0, 0);
            this.panelTop.Name = "panelTop";
            this.panelTop.Size = new System.Drawing.Size(900, 100);
            this.panelTop.TabIndex = 0;
            //
            // labelTitle
            //
            this.labelTitle.Appearance.Font = new System.Drawing.Font("Segoe UI", 14F);
            this.labelTitle.Appearance.Options.UseFont = true;
            this.labelTitle.Location = new System.Drawing.Point(12, 8);
            this.labelTitle.Name = "labelTitle";
            this.labelTitle.Size = new System.Drawing.Size(97, 25);
            this.labelTitle.TabIndex = 0;
            this.labelTitle.Text = "Beyaz Liste";
            //
            // labelInfo
            //
            this.labelInfo.Appearance.ForeColor = DevExpress.LookAndFeel.DXSkinColors.ForeColors.ControlText;
            this.labelInfo.Appearance.Options.UseForeColor = true;
            this.labelInfo.Location = new System.Drawing.Point(12, 38);
            this.labelInfo.Name = "labelInfo";
            this.labelInfo.Size = new System.Drawing.Size(560, 13);
            this.labelInfo.TabIndex = 1;
            this.labelInfo.Text = "Beyaz listedeki adresler asla banlanmaz; olayları yine loglanır ve kurala göre mail atılır. Sistem kayıtları otomatik eklenir ve silinemez.";
            //
            // buttonAdd
            //
            this.buttonAdd.Location = new System.Drawing.Point(12, 62);
            this.buttonAdd.Name = "buttonAdd";
            this.buttonAdd.Size = new System.Drawing.Size(90, 26);
            this.buttonAdd.TabIndex = 2;
            this.buttonAdd.Text = "Ekle";
            this.buttonAdd.Click += new System.EventHandler(this.buttonAdd_Click);
            //
            // buttonDelete
            //
            this.buttonDelete.Location = new System.Drawing.Point(108, 62);
            this.buttonDelete.Name = "buttonDelete";
            this.buttonDelete.Size = new System.Drawing.Size(120, 26);
            this.buttonDelete.TabIndex = 3;
            this.buttonDelete.Text = "Seçilenleri Sil";
            this.buttonDelete.Click += new System.EventHandler(this.buttonDelete_Click);
            //
            // buttonRefresh
            //
            this.buttonRefresh.Location = new System.Drawing.Point(234, 62);
            this.buttonRefresh.Name = "buttonRefresh";
            this.buttonRefresh.Size = new System.Drawing.Size(80, 26);
            this.buttonRefresh.TabIndex = 4;
            this.buttonRefresh.Text = "Yenile";
            this.buttonRefresh.Click += new System.EventHandler(this.buttonRefresh_Click);
            //
            // gridControl
            //
            this.gridControl.Dock = System.Windows.Forms.DockStyle.Fill;
            this.gridControl.Location = new System.Drawing.Point(0, 100);
            this.gridControl.MainView = this.gridView;
            this.gridControl.Name = "gridControl";
            this.gridControl.Size = new System.Drawing.Size(900, 376);
            this.gridControl.TabIndex = 1;
            this.gridControl.ViewCollection.AddRange(new DevExpress.XtraGrid.Views.Base.BaseView[] {
            this.gridView});
            //
            // gridView
            //
            this.gridView.Columns.AddRange(new DevExpress.XtraGrid.Columns.GridColumn[] {
            this.colCidr,
            this.colNote,
            this.colType,
            this.colCreated});
            this.gridView.GridControl = this.gridControl;
            this.gridView.Name = "gridView";
            this.gridView.RowStyle += new DevExpress.XtraGrid.Views.Grid.RowStyleEventHandler(this.gridView_RowStyle);
            //
            // colCidr
            //
            this.colCidr.Caption = "Adres / Aralık";
            this.colCidr.FieldName = "Cidr";
            this.colCidr.Name = "colCidr";
            this.colCidr.Visible = true;
            this.colCidr.VisibleIndex = 0;
            this.colCidr.Width = 200;
            //
            // colNote
            //
            this.colNote.Caption = "Not";
            this.colNote.FieldName = "Note";
            this.colNote.Name = "colNote";
            this.colNote.Visible = true;
            this.colNote.VisibleIndex = 1;
            this.colNote.Width = 380;
            //
            // colType
            //
            this.colType.Caption = "Tür";
            this.colType.FieldName = "Type";
            this.colType.MaxWidth = 110;
            this.colType.Name = "colType";
            this.colType.Visible = true;
            this.colType.VisibleIndex = 2;
            this.colType.Width = 90;
            //
            // colCreated
            //
            this.colCreated.Caption = "Eklenme";
            this.colCreated.DisplayFormat.FormatString = "dd.MM.yyyy HH:mm";
            this.colCreated.DisplayFormat.FormatType = DevExpress.Utils.FormatType.DateTime;
            this.colCreated.FieldName = "Created";
            this.colCreated.MaxWidth = 150;
            this.colCreated.Name = "colCreated";
            this.colCreated.Visible = true;
            this.colCreated.VisibleIndex = 3;
            this.colCreated.Width = 130;
            //
            // labelStatus
            //
            this.labelStatus.AutoSizeMode = DevExpress.XtraEditors.LabelAutoSizeMode.None;
            this.labelStatus.Dock = System.Windows.Forms.DockStyle.Bottom;
            this.labelStatus.Location = new System.Drawing.Point(0, 476);
            this.labelStatus.Name = "labelStatus";
            this.labelStatus.Padding = new System.Windows.Forms.Padding(12, 4, 0, 4);
            this.labelStatus.Size = new System.Drawing.Size(900, 24);
            this.labelStatus.TabIndex = 2;
            //
            // WhitelistModule
            //
            this.AutoScaleDimensions = new System.Drawing.SizeF(6F, 13F);
            this.AutoScaleMode = System.Windows.Forms.AutoScaleMode.Font;
            this.Controls.Add(this.gridControl);
            this.Controls.Add(this.labelStatus);
            this.Controls.Add(this.panelTop);
            this.Name = "WhitelistModule";
            this.Size = new System.Drawing.Size(900, 500);
            ((System.ComponentModel.ISupportInitialize)(this.panelTop)).EndInit();
            this.panelTop.ResumeLayout(false);
            this.panelTop.PerformLayout();
            ((System.ComponentModel.ISupportInitialize)(this.gridControl)).EndInit();
            ((System.ComponentModel.ISupportInitialize)(this.gridView)).EndInit();
            this.ResumeLayout(false);

        }

        #endregion

        private DevExpress.XtraEditors.PanelControl panelTop;
        private DevExpress.XtraEditors.LabelControl labelTitle;
        private DevExpress.XtraEditors.LabelControl labelInfo;
        private DevExpress.XtraEditors.SimpleButton buttonAdd;
        private DevExpress.XtraEditors.SimpleButton buttonDelete;
        private DevExpress.XtraEditors.SimpleButton buttonRefresh;
        private DevExpress.XtraGrid.GridControl gridControl;
        private DevExpress.XtraGrid.Views.Grid.GridView gridView;
        private DevExpress.XtraGrid.Columns.GridColumn colCidr;
        private DevExpress.XtraGrid.Columns.GridColumn colNote;
        private DevExpress.XtraGrid.Columns.GridColumn colType;
        private DevExpress.XtraGrid.Columns.GridColumn colCreated;
        private DevExpress.XtraEditors.LabelControl labelStatus;
    }
}
