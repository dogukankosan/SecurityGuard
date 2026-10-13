namespace SecurityGuardUI.Modules
{
    partial class SqliteModule
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
            this.buttonRun = new DevExpress.XtraEditors.SimpleButton();
            this.buttonExport = new DevExpress.XtraEditors.SimpleButton();
            this.buttonRefresh = new DevExpress.XtraEditors.SimpleButton();
            this.panelLeft = new DevExpress.XtraEditors.PanelControl();
            this.gridTables = new DevExpress.XtraGrid.GridControl();
            this.viewTables = new DevExpress.XtraGrid.Views.Grid.GridView();
            this.colTableName = new DevExpress.XtraGrid.Columns.GridColumn();
            this.colTableRows = new DevExpress.XtraGrid.Columns.GridColumn();
            this.splitterColumns = new DevExpress.XtraEditors.SplitterControl();
            this.gridColumns = new DevExpress.XtraGrid.GridControl();
            this.viewColumns = new DevExpress.XtraGrid.Views.Grid.GridView();
            this.colColumnName = new DevExpress.XtraGrid.Columns.GridColumn();
            this.colColumnType = new DevExpress.XtraGrid.Columns.GridColumn();
            this.colColumnInfo = new DevExpress.XtraGrid.Columns.GridColumn();
            this.labelDbInfo = new DevExpress.XtraEditors.LabelControl();
            this.splitterLeft = new DevExpress.XtraEditors.SplitterControl();
            this.panelRight = new DevExpress.XtraEditors.PanelControl();
            this.tabResults = new DevExpress.XtraTab.XtraTabControl();
            this.tabMessages = new DevExpress.XtraTab.XtraTabPage();
            this.memoMessages = new DevExpress.XtraEditors.MemoEdit();
            this.splitterEditor = new DevExpress.XtraEditors.SplitterControl();
            this.panelEditor = new DevExpress.XtraEditors.PanelControl();
            this.editor = new SecurityGuardUI.Controls.SqlCodeEditor();
            this.labelStatus = new DevExpress.XtraEditors.LabelControl();
            ((System.ComponentModel.ISupportInitialize)(this.panelTop)).BeginInit();
            this.panelTop.SuspendLayout();
            ((System.ComponentModel.ISupportInitialize)(this.panelLeft)).BeginInit();
            this.panelLeft.SuspendLayout();
            ((System.ComponentModel.ISupportInitialize)(this.gridTables)).BeginInit();
            ((System.ComponentModel.ISupportInitialize)(this.viewTables)).BeginInit();
            ((System.ComponentModel.ISupportInitialize)(this.gridColumns)).BeginInit();
            ((System.ComponentModel.ISupportInitialize)(this.viewColumns)).BeginInit();
            ((System.ComponentModel.ISupportInitialize)(this.panelRight)).BeginInit();
            this.panelRight.SuspendLayout();
            ((System.ComponentModel.ISupportInitialize)(this.tabResults)).BeginInit();
            this.tabResults.SuspendLayout();
            this.tabMessages.SuspendLayout();
            ((System.ComponentModel.ISupportInitialize)(this.memoMessages.Properties)).BeginInit();
            ((System.ComponentModel.ISupportInitialize)(this.panelEditor)).BeginInit();
            this.panelEditor.SuspendLayout();
            this.SuspendLayout();
            //
            // panelTop
            //
            this.panelTop.BorderStyle = DevExpress.XtraEditors.Controls.BorderStyles.NoBorder;
            this.panelTop.Controls.Add(this.labelTitle);
            this.panelTop.Controls.Add(this.labelInfo);
            this.panelTop.Controls.Add(this.buttonRun);
            this.panelTop.Controls.Add(this.buttonExport);
            this.panelTop.Controls.Add(this.buttonRefresh);
            this.panelTop.Dock = System.Windows.Forms.DockStyle.Top;
            this.panelTop.Location = new System.Drawing.Point(0, 0);
            this.panelTop.Name = "panelTop";
            this.panelTop.Size = new System.Drawing.Size(1000, 64);
            this.panelTop.TabIndex = 0;
            //
            // labelTitle
            //
            this.labelTitle.Appearance.Font = new System.Drawing.Font("Segoe UI", 14F);
            this.labelTitle.Appearance.Options.UseFont = true;
            this.labelTitle.Location = new System.Drawing.Point(12, 8);
            this.labelTitle.Name = "labelTitle";
            this.labelTitle.Size = new System.Drawing.Size(110, 25);
            this.labelTitle.TabIndex = 0;
            this.labelTitle.Text = "SQLite Sorgu";
            //
            // labelInfo
            //
            this.labelInfo.Location = new System.Drawing.Point(12, 38);
            this.labelInfo.Name = "labelInfo";
            this.labelInfo.Size = new System.Drawing.Size(560, 13);
            this.labelInfo.TabIndex = 1;
            this.labelInfo.Text = "SecurityGuard veritabanında (guard.db) istediğiniz SQL\'i çalıştırabilirsiniz. Yapılan değişiklikler geri alınamaz.";
            //
            // buttonRun
            //
            this.buttonRun.Anchor = ((System.Windows.Forms.AnchorStyles)((System.Windows.Forms.AnchorStyles.Top | System.Windows.Forms.AnchorStyles.Right)));
            this.buttonRun.Location = new System.Drawing.Point(622, 12);
            this.buttonRun.Name = "buttonRun";
            this.buttonRun.Size = new System.Drawing.Size(120, 26);
            this.buttonRun.TabIndex = 2;
            this.buttonRun.Text = "Çalıştır (F5)";
            this.buttonRun.Click += new System.EventHandler(this.buttonRun_Click);
            //
            // buttonExport
            //
            this.buttonExport.Anchor = ((System.Windows.Forms.AnchorStyles)((System.Windows.Forms.AnchorStyles.Top | System.Windows.Forms.AnchorStyles.Right)));
            this.buttonExport.Location = new System.Drawing.Point(748, 12);
            this.buttonExport.Name = "buttonExport";
            this.buttonExport.Size = new System.Drawing.Size(120, 26);
            this.buttonExport.TabIndex = 3;
            this.buttonExport.Text = "Excel\'e Aktar";
            this.buttonExport.Click += new System.EventHandler(this.buttonExport_Click);
            //
            // buttonRefresh
            //
            this.buttonRefresh.Anchor = ((System.Windows.Forms.AnchorStyles)((System.Windows.Forms.AnchorStyles.Top | System.Windows.Forms.AnchorStyles.Right)));
            this.buttonRefresh.Location = new System.Drawing.Point(874, 12);
            this.buttonRefresh.Name = "buttonRefresh";
            this.buttonRefresh.Size = new System.Drawing.Size(114, 26);
            this.buttonRefresh.TabIndex = 4;
            this.buttonRefresh.Text = "Tabloları Yenile";
            this.buttonRefresh.Click += new System.EventHandler(this.buttonRefresh_Click);
            //
            // panelLeft
            //
            this.panelLeft.BorderStyle = DevExpress.XtraEditors.Controls.BorderStyles.NoBorder;
            this.panelLeft.Controls.Add(this.gridTables);
            this.panelLeft.Controls.Add(this.splitterColumns);
            this.panelLeft.Controls.Add(this.gridColumns);
            this.panelLeft.Controls.Add(this.labelDbInfo);
            this.panelLeft.Dock = System.Windows.Forms.DockStyle.Left;
            this.panelLeft.Location = new System.Drawing.Point(0, 64);
            this.panelLeft.Name = "panelLeft";
            this.panelLeft.Size = new System.Drawing.Size(260, 636);
            this.panelLeft.TabIndex = 1;
            //
            // gridTables
            //
            this.gridTables.Dock = System.Windows.Forms.DockStyle.Fill;
            this.gridTables.Location = new System.Drawing.Point(0, 0);
            this.gridTables.MainView = this.viewTables;
            this.gridTables.Name = "gridTables";
            this.gridTables.Size = new System.Drawing.Size(260, 390);
            this.gridTables.TabIndex = 0;
            this.gridTables.ViewCollection.AddRange(new DevExpress.XtraGrid.Views.Base.BaseView[] {
            this.viewTables});
            //
            // viewTables
            //
            this.viewTables.Columns.AddRange(new DevExpress.XtraGrid.Columns.GridColumn[] {
            this.colTableName,
            this.colTableRows});
            this.viewTables.GridControl = this.gridTables;
            this.viewTables.Name = "viewTables";
            this.viewTables.FocusedRowChanged += new DevExpress.XtraGrid.Views.Base.FocusedRowChangedEventHandler(this.viewTables_FocusedRowChanged);
            this.viewTables.CustomColumnDisplayText += new DevExpress.XtraGrid.Views.Base.CustomColumnDisplayTextEventHandler(this.viewTables_CustomColumnDisplayText);
            this.viewTables.DoubleClick += new System.EventHandler(this.viewTables_DoubleClick);
            //
            // colTableName
            //
            this.colTableName.Caption = "Tablo";
            this.colTableName.FieldName = "Name";
            this.colTableName.Name = "colTableName";
            this.colTableName.Visible = true;
            this.colTableName.VisibleIndex = 0;
            this.colTableName.Width = 160;
            //
            // colTableRows
            //
            this.colTableRows.Caption = "Satır";
            this.colTableRows.DisplayFormat.FormatString = "N0";
            this.colTableRows.DisplayFormat.FormatType = DevExpress.Utils.FormatType.Numeric;
            this.colTableRows.FieldName = "RowCount";
            this.colTableRows.Name = "colTableRows";
            this.colTableRows.Visible = true;
            this.colTableRows.VisibleIndex = 1;
            this.colTableRows.Width = 80;
            //
            // splitterColumns
            //
            this.splitterColumns.Dock = System.Windows.Forms.DockStyle.Bottom;
            this.splitterColumns.Location = new System.Drawing.Point(0, 390);
            this.splitterColumns.Name = "splitterColumns";
            this.splitterColumns.Size = new System.Drawing.Size(260, 6);
            this.splitterColumns.TabIndex = 1;
            this.splitterColumns.TabStop = false;
            //
            // gridColumns
            //
            this.gridColumns.Dock = System.Windows.Forms.DockStyle.Bottom;
            this.gridColumns.Location = new System.Drawing.Point(0, 396);
            this.gridColumns.MainView = this.viewColumns;
            this.gridColumns.Name = "gridColumns";
            this.gridColumns.Size = new System.Drawing.Size(260, 214);
            this.gridColumns.TabIndex = 2;
            this.gridColumns.ViewCollection.AddRange(new DevExpress.XtraGrid.Views.Base.BaseView[] {
            this.viewColumns});
            //
            // viewColumns
            //
            this.viewColumns.Columns.AddRange(new DevExpress.XtraGrid.Columns.GridColumn[] {
            this.colColumnName,
            this.colColumnType,
            this.colColumnInfo});
            this.viewColumns.GridControl = this.gridColumns;
            this.viewColumns.Name = "viewColumns";
            //
            // colColumnName
            //
            this.colColumnName.Caption = "Kolon";
            this.colColumnName.FieldName = "Name";
            this.colColumnName.Name = "colColumnName";
            this.colColumnName.Visible = true;
            this.colColumnName.VisibleIndex = 0;
            this.colColumnName.Width = 100;
            //
            // colColumnType
            //
            this.colColumnType.Caption = "Tip";
            this.colColumnType.FieldName = "Type";
            this.colColumnType.Name = "colColumnType";
            this.colColumnType.Visible = true;
            this.colColumnType.VisibleIndex = 1;
            this.colColumnType.Width = 70;
            //
            // colColumnInfo
            //
            this.colColumnInfo.Caption = "Özellik";
            this.colColumnInfo.FieldName = "Info";
            this.colColumnInfo.Name = "colColumnInfo";
            this.colColumnInfo.Visible = true;
            this.colColumnInfo.VisibleIndex = 2;
            this.colColumnInfo.Width = 80;
            //
            // labelDbInfo
            //
            this.labelDbInfo.AutoSizeMode = DevExpress.XtraEditors.LabelAutoSizeMode.None;
            this.labelDbInfo.Dock = System.Windows.Forms.DockStyle.Bottom;
            this.labelDbInfo.Location = new System.Drawing.Point(0, 610);
            this.labelDbInfo.Name = "labelDbInfo";
            this.labelDbInfo.Padding = new System.Windows.Forms.Padding(6, 6, 0, 0);
            this.labelDbInfo.Size = new System.Drawing.Size(260, 26);
            this.labelDbInfo.TabIndex = 3;
            //
            // splitterLeft
            //
            this.splitterLeft.Location = new System.Drawing.Point(260, 64);
            this.splitterLeft.Name = "splitterLeft";
            this.splitterLeft.Size = new System.Drawing.Size(6, 636);
            this.splitterLeft.TabIndex = 2;
            this.splitterLeft.TabStop = false;
            //
            // panelRight
            //
            this.panelRight.BorderStyle = DevExpress.XtraEditors.Controls.BorderStyles.NoBorder;
            this.panelRight.Controls.Add(this.tabResults);
            this.panelRight.Controls.Add(this.splitterEditor);
            this.panelRight.Controls.Add(this.panelEditor);
            this.panelRight.Controls.Add(this.labelStatus);
            this.panelRight.Dock = System.Windows.Forms.DockStyle.Fill;
            this.panelRight.Location = new System.Drawing.Point(266, 64);
            this.panelRight.Name = "panelRight";
            this.panelRight.Size = new System.Drawing.Size(734, 636);
            this.panelRight.TabIndex = 3;
            //
            // tabResults
            //
            this.tabResults.Dock = System.Windows.Forms.DockStyle.Fill;
            this.tabResults.Location = new System.Drawing.Point(0, 226);
            this.tabResults.Name = "tabResults";
            this.tabResults.SelectedTabPage = this.tabMessages;
            this.tabResults.Size = new System.Drawing.Size(734, 384);
            this.tabResults.TabIndex = 2;
            this.tabResults.TabPages.AddRange(new DevExpress.XtraTab.XtraTabPage[] {
            this.tabMessages});
            this.tabResults.SelectedPageChanged += new DevExpress.XtraTab.TabPageChangedEventHandler(this.tabResults_SelectedPageChanged);
            //
            // tabMessages
            //
            this.tabMessages.Controls.Add(this.memoMessages);
            this.tabMessages.Name = "tabMessages";
            this.tabMessages.Size = new System.Drawing.Size(732, 359);
            this.tabMessages.Text = "Mesajlar";
            //
            // memoMessages
            //
            this.memoMessages.Dock = System.Windows.Forms.DockStyle.Fill;
            this.memoMessages.Location = new System.Drawing.Point(0, 0);
            this.memoMessages.Name = "memoMessages";
            this.memoMessages.Properties.Appearance.Font = new System.Drawing.Font("Consolas", 9.75F);
            this.memoMessages.Properties.Appearance.Options.UseFont = true;
            this.memoMessages.Properties.Appearance.Options.UseForeColor = true;
            this.memoMessages.Properties.ReadOnly = true;
            this.memoMessages.Size = new System.Drawing.Size(732, 359);
            this.memoMessages.TabIndex = 0;
            //
            // splitterEditor
            //
            this.splitterEditor.Dock = System.Windows.Forms.DockStyle.Top;
            this.splitterEditor.Location = new System.Drawing.Point(0, 220);
            this.splitterEditor.Name = "splitterEditor";
            this.splitterEditor.Size = new System.Drawing.Size(734, 6);
            this.splitterEditor.TabIndex = 1;
            this.splitterEditor.TabStop = false;
            //
            // panelEditor
            //
            this.panelEditor.Controls.Add(this.editor);
            this.panelEditor.Dock = System.Windows.Forms.DockStyle.Top;
            this.panelEditor.Location = new System.Drawing.Point(0, 0);
            this.panelEditor.Name = "panelEditor";
            this.panelEditor.Padding = new System.Windows.Forms.Padding(4);
            this.panelEditor.Size = new System.Drawing.Size(734, 220);
            this.panelEditor.TabIndex = 0;
            //
            // editor
            //
            this.editor.Dock = System.Windows.Forms.DockStyle.Fill;
            this.editor.Location = new System.Drawing.Point(6, 6);
            this.editor.Name = "editor";
            this.editor.Size = new System.Drawing.Size(722, 208);
            this.editor.TabIndex = 0;
            this.editor.Text = "";
            //
            // labelStatus
            //
            this.labelStatus.Appearance.Options.UseForeColor = true;
            this.labelStatus.AutoSizeMode = DevExpress.XtraEditors.LabelAutoSizeMode.None;
            this.labelStatus.Dock = System.Windows.Forms.DockStyle.Bottom;
            this.labelStatus.Location = new System.Drawing.Point(0, 610);
            this.labelStatus.Name = "labelStatus";
            this.labelStatus.Padding = new System.Windows.Forms.Padding(6, 6, 0, 0);
            this.labelStatus.Size = new System.Drawing.Size(734, 26);
            this.labelStatus.TabIndex = 3;
            this.labelStatus.Text = "Hazır";
            //
            // SqliteModule
            //
            this.AutoScaleDimensions = new System.Drawing.SizeF(6F, 13F);
            this.AutoScaleMode = System.Windows.Forms.AutoScaleMode.Font;
            this.Controls.Add(this.panelRight);
            this.Controls.Add(this.splitterLeft);
            this.Controls.Add(this.panelLeft);
            this.Controls.Add(this.panelTop);
            this.Name = "SqliteModule";
            this.Size = new System.Drawing.Size(1000, 700);
            ((System.ComponentModel.ISupportInitialize)(this.panelTop)).EndInit();
            this.panelTop.ResumeLayout(false);
            this.panelTop.PerformLayout();
            ((System.ComponentModel.ISupportInitialize)(this.panelLeft)).EndInit();
            this.panelLeft.ResumeLayout(false);
            ((System.ComponentModel.ISupportInitialize)(this.gridTables)).EndInit();
            ((System.ComponentModel.ISupportInitialize)(this.viewTables)).EndInit();
            ((System.ComponentModel.ISupportInitialize)(this.gridColumns)).EndInit();
            ((System.ComponentModel.ISupportInitialize)(this.viewColumns)).EndInit();
            ((System.ComponentModel.ISupportInitialize)(this.panelRight)).EndInit();
            this.panelRight.ResumeLayout(false);
            ((System.ComponentModel.ISupportInitialize)(this.tabResults)).EndInit();
            this.tabResults.ResumeLayout(false);
            this.tabMessages.ResumeLayout(false);
            ((System.ComponentModel.ISupportInitialize)(this.memoMessages.Properties)).EndInit();
            ((System.ComponentModel.ISupportInitialize)(this.panelEditor)).EndInit();
            this.panelEditor.ResumeLayout(false);
            this.ResumeLayout(false);

        }

        #endregion

        private DevExpress.XtraEditors.PanelControl panelTop;
        private DevExpress.XtraEditors.LabelControl labelTitle;
        private DevExpress.XtraEditors.LabelControl labelInfo;
        private DevExpress.XtraEditors.SimpleButton buttonRun;
        private DevExpress.XtraEditors.SimpleButton buttonExport;
        private DevExpress.XtraEditors.SimpleButton buttonRefresh;
        private DevExpress.XtraEditors.PanelControl panelLeft;
        private DevExpress.XtraGrid.GridControl gridTables;
        private DevExpress.XtraGrid.Views.Grid.GridView viewTables;
        private DevExpress.XtraGrid.Columns.GridColumn colTableName;
        private DevExpress.XtraGrid.Columns.GridColumn colTableRows;
        private DevExpress.XtraEditors.SplitterControl splitterColumns;
        private DevExpress.XtraGrid.GridControl gridColumns;
        private DevExpress.XtraGrid.Views.Grid.GridView viewColumns;
        private DevExpress.XtraGrid.Columns.GridColumn colColumnName;
        private DevExpress.XtraGrid.Columns.GridColumn colColumnType;
        private DevExpress.XtraGrid.Columns.GridColumn colColumnInfo;
        private DevExpress.XtraEditors.LabelControl labelDbInfo;
        private DevExpress.XtraEditors.SplitterControl splitterLeft;
        private DevExpress.XtraEditors.PanelControl panelRight;
        private DevExpress.XtraTab.XtraTabControl tabResults;
        private DevExpress.XtraTab.XtraTabPage tabMessages;
        private DevExpress.XtraEditors.MemoEdit memoMessages;
        private DevExpress.XtraEditors.SplitterControl splitterEditor;
        private DevExpress.XtraEditors.PanelControl panelEditor;
        private SecurityGuardUI.Controls.SqlCodeEditor editor;
        private DevExpress.XtraEditors.LabelControl labelStatus;
    }
}
