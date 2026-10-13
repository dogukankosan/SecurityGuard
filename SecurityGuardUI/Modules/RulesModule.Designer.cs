namespace SecurityGuardUI.Modules
{
    partial class RulesModule
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
            this.buttonSave = new DevExpress.XtraEditors.SimpleButton();
            this.buttonUndo = new DevExpress.XtraEditors.SimpleButton();
            this.buttonReset = new DevExpress.XtraEditors.SimpleButton();
            this.labelDirty = new DevExpress.XtraEditors.LabelControl();
            this.gridControl = new DevExpress.XtraGrid.GridControl();
            this.gridView = new DevExpress.XtraGrid.Views.Grid.GridView();
            this.colCategory = new DevExpress.XtraGrid.Columns.GridColumn();
            this.colName = new DevExpress.XtraGrid.Columns.GridColumn();
            this.colSource = new DevExpress.XtraGrid.Columns.GridColumn();
            this.colMonitor = new DevExpress.XtraGrid.Columns.GridColumn();
            this.repoCheck = new DevExpress.XtraEditors.Repository.RepositoryItemCheckEdit();
            this.colWriteLog = new DevExpress.XtraGrid.Columns.GridColumn();
            this.colSendMail = new DevExpress.XtraGrid.Columns.GridColumn();
            this.colRecipients = new DevExpress.XtraGrid.Columns.GridColumn();
            this.repoRecipients = new DevExpress.XtraEditors.Repository.RepositoryItemCheckedComboBoxEdit();
            this.buttonAssignRecipients = new DevExpress.XtraEditors.SimpleButton();
            this.colSeverity = new DevExpress.XtraGrid.Columns.GridColumn();
            this.repoSeverity = new DevExpress.XtraEditors.Repository.RepositoryItemImageComboBox();
            this.colBan = new DevExpress.XtraGrid.Columns.GridColumn();
            this.colThreshold = new DevExpress.XtraGrid.Columns.GridColumn();
            this.repoThreshold = new DevExpress.XtraEditors.Repository.RepositoryItemSpinEdit();
            this.repoEmpty = new DevExpress.XtraEditors.Repository.RepositoryItemTextEdit();
            ((System.ComponentModel.ISupportInitialize)(this.panelTop)).BeginInit();
            this.panelTop.SuspendLayout();
            ((System.ComponentModel.ISupportInitialize)(this.gridControl)).BeginInit();
            ((System.ComponentModel.ISupportInitialize)(this.gridView)).BeginInit();
            ((System.ComponentModel.ISupportInitialize)(this.repoCheck)).BeginInit();
            ((System.ComponentModel.ISupportInitialize)(this.repoSeverity)).BeginInit();
            ((System.ComponentModel.ISupportInitialize)(this.repoRecipients)).BeginInit();
            ((System.ComponentModel.ISupportInitialize)(this.repoThreshold)).BeginInit();
            ((System.ComponentModel.ISupportInitialize)(this.repoEmpty)).BeginInit();
            this.SuspendLayout();
            //
            // panelTop
            //
            this.panelTop.BorderStyle = DevExpress.XtraEditors.Controls.BorderStyles.NoBorder;
            this.panelTop.Controls.Add(this.labelTitle);
            this.panelTop.Controls.Add(this.labelInfo);
            this.panelTop.Controls.Add(this.buttonSave);
            this.panelTop.Controls.Add(this.buttonUndo);
            this.panelTop.Controls.Add(this.buttonReset);
            this.panelTop.Controls.Add(this.buttonAssignRecipients);
            this.panelTop.Controls.Add(this.labelDirty);
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
            this.labelTitle.Size = new System.Drawing.Size(117, 25);
            this.labelTitle.TabIndex = 0;
            this.labelTitle.Text = "Olay Kuralları";
            //
            // labelInfo
            //
            this.labelInfo.Appearance.ForeColor = DevExpress.LookAndFeel.DXSkinColors.ForeColors.ControlText;
            this.labelInfo.Appearance.Options.UseForeColor = true;
            this.labelInfo.Location = new System.Drawing.Point(12, 38);
            this.labelInfo.Name = "labelInfo";
            this.labelInfo.Size = new System.Drawing.Size(600, 13);
            this.labelInfo.TabIndex = 1;
            this.labelInfo.Text = "Her olay için izleme, log, mail, alıcı ve önem ayarı. Alıcı seçilmezse mail tüm aktif alıcılara gider. Ban sadece dış IP\'den RDP / SQL başarısız girişlerde uygulanır.";
            //
            // buttonSave
            //
            this.buttonSave.Location = new System.Drawing.Point(12, 62);
            this.buttonSave.Name = "buttonSave";
            this.buttonSave.Size = new System.Drawing.Size(90, 26);
            this.buttonSave.TabIndex = 2;
            this.buttonSave.Text = "Kaydet";
            this.buttonSave.Click += new System.EventHandler(this.buttonSave_Click);
            //
            // buttonUndo
            //
            this.buttonUndo.Location = new System.Drawing.Point(108, 62);
            this.buttonUndo.Name = "buttonUndo";
            this.buttonUndo.Size = new System.Drawing.Size(90, 26);
            this.buttonUndo.TabIndex = 3;
            this.buttonUndo.Text = "Geri Al";
            this.buttonUndo.Click += new System.EventHandler(this.buttonUndo_Click);
            //
            // buttonReset
            //
            this.buttonReset.Location = new System.Drawing.Point(204, 62);
            this.buttonReset.Name = "buttonReset";
            this.buttonReset.Size = new System.Drawing.Size(140, 26);
            this.buttonReset.TabIndex = 4;
            this.buttonReset.Text = "Varsayılanlara Dön";
            this.buttonReset.Click += new System.EventHandler(this.buttonReset_Click);
            //
            // buttonAssignRecipients
            //
            this.buttonAssignRecipients.Location = new System.Drawing.Point(350, 62);
            this.buttonAssignRecipients.Name = "buttonAssignRecipients";
            this.buttonAssignRecipients.Size = new System.Drawing.Size(200, 26);
            this.buttonAssignRecipients.TabIndex = 6;
            this.buttonAssignRecipients.Text = "Seçili Kurallara Alıcı Ata...";
            this.buttonAssignRecipients.Click += new System.EventHandler(this.buttonAssignRecipients_Click);
            //
            // labelDirty
            //
            this.labelDirty.Appearance.ForeColor = DevExpress.LookAndFeel.DXSkinColors.ForeColors.Warning;
            this.labelDirty.Appearance.Options.UseForeColor = true;
            this.labelDirty.Location = new System.Drawing.Point(562, 68);
            this.labelDirty.Name = "labelDirty";
            this.labelDirty.Size = new System.Drawing.Size(0, 13);
            this.labelDirty.TabIndex = 5;
            //
            // gridControl
            //
            this.gridControl.Dock = System.Windows.Forms.DockStyle.Fill;
            this.gridControl.Location = new System.Drawing.Point(0, 100);
            this.gridControl.MainView = this.gridView;
            this.gridControl.Name = "gridControl";
            this.gridControl.RepositoryItems.AddRange(new DevExpress.XtraEditors.Repository.RepositoryItem[] {
            this.repoCheck,
            this.repoSeverity,
            this.repoRecipients,
            this.repoThreshold,
            this.repoEmpty});
            this.gridControl.Size = new System.Drawing.Size(900, 400);
            this.gridControl.TabIndex = 1;
            this.gridControl.ViewCollection.AddRange(new DevExpress.XtraGrid.Views.Base.BaseView[] {
            this.gridView});
            //
            // gridView
            //
            this.gridView.Columns.AddRange(new DevExpress.XtraGrid.Columns.GridColumn[] {
            this.colCategory,
            this.colName,
            this.colSource,
            this.colMonitor,
            this.colWriteLog,
            this.colSendMail,
            this.colRecipients,
            this.colSeverity,
            this.colBan,
            this.colThreshold});
            this.gridView.GridControl = this.gridControl;
            this.gridView.GroupCount = 1;
            this.gridView.Name = "gridView";
            this.gridView.SortInfo.AddRange(new DevExpress.XtraGrid.Columns.GridColumnSortInfo[] {
            new DevExpress.XtraGrid.Columns.GridColumnSortInfo(this.colCategory, DevExpress.Data.ColumnSortOrder.Ascending)});
            this.gridView.CustomRowCellEdit += new DevExpress.XtraGrid.Views.Grid.CustomRowCellEditEventHandler(this.gridView_CustomRowCellEdit);
            this.gridView.RowStyle += new DevExpress.XtraGrid.Views.Grid.RowStyleEventHandler(this.gridView_RowStyle);
            this.gridView.ShowingEditor += new System.ComponentModel.CancelEventHandler(this.gridView_ShowingEditor);
            this.gridView.CellValueChanged += new DevExpress.XtraGrid.Views.Base.CellValueChangedEventHandler(this.gridView_CellValueChanged);
            this.gridView.CustomColumnSort += new DevExpress.XtraGrid.Views.Base.CustomColumnSortEventHandler(this.gridView_CustomColumnSort);
            this.gridView.CustomColumnDisplayText += new DevExpress.XtraGrid.Views.Base.CustomColumnDisplayTextEventHandler(this.gridView_CustomColumnDisplayText);
            //
            // colCategory
            //
            this.colCategory.Caption = "Grup";
            this.colCategory.FieldName = "Category";
            this.colCategory.Name = "colCategory";
            this.colCategory.OptionsColumn.AllowEdit = false;
            this.colCategory.SortMode = DevExpress.XtraGrid.ColumnSortMode.Custom;
            this.colCategory.Visible = true;
            this.colCategory.VisibleIndex = 0;
            //
            // colName
            //
            this.colName.Caption = "Olay";
            this.colName.FieldName = "Name";
            this.colName.Name = "colName";
            this.colName.OptionsColumn.AllowEdit = false;
            this.colName.OptionsColumn.AllowFocus = false;
            this.colName.Visible = true;
            this.colName.VisibleIndex = 0;
            this.colName.Width = 320;
            //
            // colSource
            //
            this.colSource.Caption = "Kaynak";
            this.colSource.FieldName = "Source";
            this.colSource.Name = "colSource";
            this.colSource.OptionsColumn.AllowEdit = false;
            this.colSource.OptionsColumn.AllowFocus = false;
            this.colSource.Visible = true;
            this.colSource.VisibleIndex = 1;
            this.colSource.Width = 220;
            //
            // colMonitor
            //
            this.colMonitor.Caption = "İzle";
            this.colMonitor.ColumnEdit = this.repoCheck;
            this.colMonitor.FieldName = "Monitor";
            this.colMonitor.MaxWidth = 60;
            this.colMonitor.Name = "colMonitor";
            this.colMonitor.Visible = true;
            this.colMonitor.VisibleIndex = 2;
            this.colMonitor.Width = 50;
            //
            // repoCheck
            //
            this.repoCheck.AutoHeight = false;
            this.repoCheck.Name = "repoCheck";
            //
            // colWriteLog
            //
            this.colWriteLog.Caption = "Logla";
            this.colWriteLog.ColumnEdit = this.repoCheck;
            this.colWriteLog.FieldName = "WriteLog";
            this.colWriteLog.MaxWidth = 60;
            this.colWriteLog.Name = "colWriteLog";
            this.colWriteLog.Visible = true;
            this.colWriteLog.VisibleIndex = 3;
            this.colWriteLog.Width = 50;
            //
            // colSendMail
            //
            this.colSendMail.Caption = "Mail";
            this.colSendMail.ColumnEdit = this.repoCheck;
            this.colSendMail.FieldName = "SendMail";
            this.colSendMail.MaxWidth = 60;
            this.colSendMail.Name = "colSendMail";
            this.colSendMail.Visible = true;
            this.colSendMail.VisibleIndex = 4;
            this.colSendMail.Width = 50;
            //
            // colRecipients
            //
            this.colRecipients.Caption = "Alıcılar";
            this.colRecipients.ColumnEdit = this.repoRecipients;
            this.colRecipients.FieldName = "RecipientCsv";
            this.colRecipients.Name = "colRecipients";
            this.colRecipients.Visible = true;
            this.colRecipients.VisibleIndex = 5;
            this.colRecipients.Width = 220;
            //
            // repoRecipients
            //
            this.repoRecipients.AutoHeight = false;
            this.repoRecipients.Buttons.AddRange(new DevExpress.XtraEditors.Controls.EditorButton[] {
            new DevExpress.XtraEditors.Controls.EditorButton(DevExpress.XtraEditors.Controls.ButtonPredefines.Combo)});
            this.repoRecipients.Name = "repoRecipients";
            this.repoRecipients.NullText = "Tüm aktif alıcılar";
            this.repoRecipients.SelectAllItemVisible = false;
            //
            // colSeverity
            //
            this.colSeverity.Caption = "Önem";
            this.colSeverity.ColumnEdit = this.repoSeverity;
            this.colSeverity.FieldName = "Severity";
            this.colSeverity.MaxWidth = 110;
            this.colSeverity.Name = "colSeverity";
            this.colSeverity.Visible = true;
            this.colSeverity.VisibleIndex = 6;
            this.colSeverity.Width = 90;
            //
            // repoSeverity
            //
            this.repoSeverity.AutoHeight = false;
            this.repoSeverity.Buttons.AddRange(new DevExpress.XtraEditors.Controls.EditorButton[] {
            new DevExpress.XtraEditors.Controls.EditorButton(DevExpress.XtraEditors.Controls.ButtonPredefines.Combo)});
            this.repoSeverity.Name = "repoSeverity";
            //
            // colBan
            //
            this.colBan.Caption = "Ban";
            this.colBan.ColumnEdit = this.repoCheck;
            this.colBan.FieldName = "BanEnabled";
            this.colBan.MaxWidth = 60;
            this.colBan.Name = "colBan";
            this.colBan.Visible = true;
            this.colBan.VisibleIndex = 7;
            this.colBan.Width = 50;
            //
            // colThreshold
            //
            this.colThreshold.Caption = "Eşik";
            this.colThreshold.ColumnEdit = this.repoThreshold;
            this.colThreshold.FieldName = "Threshold";
            this.colThreshold.MaxWidth = 90;
            this.colThreshold.Name = "colThreshold";
            this.colThreshold.Visible = true;
            this.colThreshold.VisibleIndex = 8;
            this.colThreshold.Width = 70;
            //
            // repoThreshold
            //
            this.repoThreshold.AutoHeight = false;
            this.repoThreshold.Buttons.AddRange(new DevExpress.XtraEditors.Controls.EditorButton[] {
            new DevExpress.XtraEditors.Controls.EditorButton(DevExpress.XtraEditors.Controls.ButtonPredefines.Combo)});
            this.repoThreshold.IsFloatValue = false;
            this.repoThreshold.Mask.EditMask = "N0";
            this.repoThreshold.MaxValue = new decimal(new int[] {
            1000,
            0,
            0,
            0});
            this.repoThreshold.MinValue = new decimal(new int[] {
            3,
            0,
            0,
            0});
            this.repoThreshold.Name = "repoThreshold";
            //
            // repoEmpty
            //
            this.repoEmpty.AutoHeight = false;
            this.repoEmpty.Name = "repoEmpty";
            this.repoEmpty.ReadOnly = true;
            //
            // RulesModule
            //
            this.AutoScaleDimensions = new System.Drawing.SizeF(6F, 13F);
            this.AutoScaleMode = System.Windows.Forms.AutoScaleMode.Font;
            this.Controls.Add(this.gridControl);
            this.Controls.Add(this.panelTop);
            this.Name = "RulesModule";
            this.Size = new System.Drawing.Size(900, 500);
            ((System.ComponentModel.ISupportInitialize)(this.panelTop)).EndInit();
            this.panelTop.ResumeLayout(false);
            this.panelTop.PerformLayout();
            ((System.ComponentModel.ISupportInitialize)(this.gridControl)).EndInit();
            ((System.ComponentModel.ISupportInitialize)(this.gridView)).EndInit();
            ((System.ComponentModel.ISupportInitialize)(this.repoCheck)).EndInit();
            ((System.ComponentModel.ISupportInitialize)(this.repoSeverity)).EndInit();
            ((System.ComponentModel.ISupportInitialize)(this.repoRecipients)).EndInit();
            ((System.ComponentModel.ISupportInitialize)(this.repoThreshold)).EndInit();
            ((System.ComponentModel.ISupportInitialize)(this.repoEmpty)).EndInit();
            this.ResumeLayout(false);

        }

        #endregion

        private DevExpress.XtraEditors.PanelControl panelTop;
        private DevExpress.XtraEditors.LabelControl labelTitle;
        private DevExpress.XtraEditors.LabelControl labelInfo;
        private DevExpress.XtraEditors.SimpleButton buttonSave;
        private DevExpress.XtraEditors.SimpleButton buttonUndo;
        private DevExpress.XtraEditors.SimpleButton buttonReset;
        private DevExpress.XtraEditors.LabelControl labelDirty;
        private DevExpress.XtraGrid.GridControl gridControl;
        private DevExpress.XtraGrid.Views.Grid.GridView gridView;
        private DevExpress.XtraGrid.Columns.GridColumn colCategory;
        private DevExpress.XtraGrid.Columns.GridColumn colName;
        private DevExpress.XtraGrid.Columns.GridColumn colSource;
        private DevExpress.XtraGrid.Columns.GridColumn colMonitor;
        private DevExpress.XtraEditors.Repository.RepositoryItemCheckEdit repoCheck;
        private DevExpress.XtraGrid.Columns.GridColumn colWriteLog;
        private DevExpress.XtraGrid.Columns.GridColumn colSendMail;
        private DevExpress.XtraGrid.Columns.GridColumn colRecipients;
        private DevExpress.XtraEditors.Repository.RepositoryItemCheckedComboBoxEdit repoRecipients;
        private DevExpress.XtraEditors.SimpleButton buttonAssignRecipients;
        private DevExpress.XtraGrid.Columns.GridColumn colSeverity;
        private DevExpress.XtraEditors.Repository.RepositoryItemImageComboBox repoSeverity;
        private DevExpress.XtraGrid.Columns.GridColumn colBan;
        private DevExpress.XtraGrid.Columns.GridColumn colThreshold;
        private DevExpress.XtraEditors.Repository.RepositoryItemSpinEdit repoThreshold;
        private DevExpress.XtraEditors.Repository.RepositoryItemTextEdit repoEmpty;
    }
}
