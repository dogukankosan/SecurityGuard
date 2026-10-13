namespace SecurityGuardUI.Modules
{
    partial class EventLogModule
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
            this.labelFrom = new DevExpress.XtraEditors.LabelControl();
            this.dateFrom = new DevExpress.XtraEditors.DateEdit();
            this.labelTo = new DevExpress.XtraEditors.LabelControl();
            this.dateTo = new DevExpress.XtraEditors.DateEdit();
            this.comboSeverity = new DevExpress.XtraEditors.ImageComboBoxEdit();
            this.comboCategory = new DevExpress.XtraEditors.ComboBoxEdit();
            this.textSearch = new DevExpress.XtraEditors.TextEdit();
            this.buttonSearch = new DevExpress.XtraEditors.SimpleButton();
            this.buttonVerify = new DevExpress.XtraEditors.SimpleButton();
            this.buttonExport = new DevExpress.XtraEditors.SimpleButton();
            this.gridControl = new DevExpress.XtraGrid.GridControl();
            this.gridView = new DevExpress.XtraGrid.Views.Grid.GridView();
            this.colTime = new DevExpress.XtraGrid.Columns.GridColumn();
            this.colSeverity = new DevExpress.XtraGrid.Columns.GridColumn();
            this.colCategory = new DevExpress.XtraGrid.Columns.GridColumn();
            this.colEventName = new DevExpress.XtraGrid.Columns.GridColumn();
            this.colSummary = new DevExpress.XtraGrid.Columns.GridColumn();
            this.colUserName = new DevExpress.XtraGrid.Columns.GridColumn();
            this.colIpAddress = new DevExpress.XtraGrid.Columns.GridColumn();
            this.colMail = new DevExpress.XtraGrid.Columns.GridColumn();
            this.splitterDetail = new DevExpress.XtraEditors.SplitterControl();
            this.memoDetail = new DevExpress.XtraEditors.MemoEdit();
            this.labelStatus = new DevExpress.XtraEditors.LabelControl();
            ((System.ComponentModel.ISupportInitialize)(this.panelTop)).BeginInit();
            this.panelTop.SuspendLayout();
            ((System.ComponentModel.ISupportInitialize)(this.dateFrom.Properties.CalendarTimeProperties)).BeginInit();
            ((System.ComponentModel.ISupportInitialize)(this.dateFrom.Properties)).BeginInit();
            ((System.ComponentModel.ISupportInitialize)(this.dateTo.Properties.CalendarTimeProperties)).BeginInit();
            ((System.ComponentModel.ISupportInitialize)(this.dateTo.Properties)).BeginInit();
            ((System.ComponentModel.ISupportInitialize)(this.comboSeverity.Properties)).BeginInit();
            ((System.ComponentModel.ISupportInitialize)(this.comboCategory.Properties)).BeginInit();
            ((System.ComponentModel.ISupportInitialize)(this.textSearch.Properties)).BeginInit();
            ((System.ComponentModel.ISupportInitialize)(this.gridControl)).BeginInit();
            ((System.ComponentModel.ISupportInitialize)(this.gridView)).BeginInit();
            ((System.ComponentModel.ISupportInitialize)(this.memoDetail.Properties)).BeginInit();
            this.SuspendLayout();
            //
            // panelTop
            //
            this.panelTop.BorderStyle = DevExpress.XtraEditors.Controls.BorderStyles.NoBorder;
            this.panelTop.Controls.Add(this.labelTitle);
            this.panelTop.Controls.Add(this.labelFrom);
            this.panelTop.Controls.Add(this.dateFrom);
            this.panelTop.Controls.Add(this.labelTo);
            this.panelTop.Controls.Add(this.dateTo);
            this.panelTop.Controls.Add(this.comboSeverity);
            this.panelTop.Controls.Add(this.comboCategory);
            this.panelTop.Controls.Add(this.textSearch);
            this.panelTop.Controls.Add(this.buttonSearch);
            this.panelTop.Controls.Add(this.buttonVerify);
            this.panelTop.Controls.Add(this.buttonExport);
            this.panelTop.Dock = System.Windows.Forms.DockStyle.Top;
            this.panelTop.Location = new System.Drawing.Point(0, 0);
            this.panelTop.Name = "panelTop";
            this.panelTop.Size = new System.Drawing.Size(1000, 84);
            this.panelTop.TabIndex = 0;
            //
            // labelTitle
            //
            this.labelTitle.Appearance.Font = new System.Drawing.Font("Segoe UI", 14F);
            this.labelTitle.Appearance.Options.UseFont = true;
            this.labelTitle.Location = new System.Drawing.Point(12, 8);
            this.labelTitle.Name = "labelTitle";
            this.labelTitle.Size = new System.Drawing.Size(84, 25);
            this.labelTitle.TabIndex = 0;
            this.labelTitle.Text = "Olay Logu";
            //
            // labelFrom
            //
            this.labelFrom.Location = new System.Drawing.Point(12, 51);
            this.labelFrom.Name = "labelFrom";
            this.labelFrom.Size = new System.Drawing.Size(47, 13);
            this.labelFrom.TabIndex = 1;
            this.labelFrom.Text = "Başlangıç";
            //
            // dateFrom
            //
            this.dateFrom.EditValue = null;
            this.dateFrom.Location = new System.Drawing.Point(66, 48);
            this.dateFrom.Name = "dateFrom";
            this.dateFrom.Properties.Buttons.AddRange(new DevExpress.XtraEditors.Controls.EditorButton[] {
            new DevExpress.XtraEditors.Controls.EditorButton(DevExpress.XtraEditors.Controls.ButtonPredefines.Combo)});
            this.dateFrom.Properties.CalendarTimeEditing = DevExpress.Utils.DefaultBoolean.True;
            this.dateFrom.Properties.CalendarTimeProperties.Buttons.AddRange(new DevExpress.XtraEditors.Controls.EditorButton[] {
            new DevExpress.XtraEditors.Controls.EditorButton(DevExpress.XtraEditors.Controls.ButtonPredefines.Combo)});
            this.dateFrom.Properties.Mask.EditMask = "dd.MM.yyyy HH:mm";
            this.dateFrom.Properties.Mask.UseMaskAsDisplayFormat = true;
            this.dateFrom.Size = new System.Drawing.Size(130, 20);
            this.dateFrom.TabIndex = 2;
            //
            // labelTo
            //
            this.labelTo.Location = new System.Drawing.Point(206, 51);
            this.labelTo.Name = "labelTo";
            this.labelTo.Size = new System.Drawing.Size(20, 13);
            this.labelTo.TabIndex = 3;
            this.labelTo.Text = "Bitiş";
            //
            // dateTo
            //
            this.dateTo.EditValue = null;
            this.dateTo.Location = new System.Drawing.Point(232, 48);
            this.dateTo.Name = "dateTo";
            this.dateTo.Properties.Buttons.AddRange(new DevExpress.XtraEditors.Controls.EditorButton[] {
            new DevExpress.XtraEditors.Controls.EditorButton(DevExpress.XtraEditors.Controls.ButtonPredefines.Combo)});
            this.dateTo.Properties.CalendarTimeEditing = DevExpress.Utils.DefaultBoolean.True;
            this.dateTo.Properties.CalendarTimeProperties.Buttons.AddRange(new DevExpress.XtraEditors.Controls.EditorButton[] {
            new DevExpress.XtraEditors.Controls.EditorButton(DevExpress.XtraEditors.Controls.ButtonPredefines.Combo)});
            this.dateTo.Properties.Mask.EditMask = "dd.MM.yyyy HH:mm";
            this.dateTo.Properties.Mask.UseMaskAsDisplayFormat = true;
            this.dateTo.Size = new System.Drawing.Size(130, 20);
            this.dateTo.TabIndex = 4;
            //
            // comboSeverity
            //
            this.comboSeverity.Location = new System.Drawing.Point(372, 48);
            this.comboSeverity.Name = "comboSeverity";
            this.comboSeverity.Properties.Buttons.AddRange(new DevExpress.XtraEditors.Controls.EditorButton[] {
            new DevExpress.XtraEditors.Controls.EditorButton(DevExpress.XtraEditors.Controls.ButtonPredefines.Combo)});
            this.comboSeverity.Size = new System.Drawing.Size(120, 20);
            this.comboSeverity.TabIndex = 5;
            //
            // comboCategory
            //
            this.comboCategory.Location = new System.Drawing.Point(500, 48);
            this.comboCategory.Name = "comboCategory";
            this.comboCategory.Properties.Buttons.AddRange(new DevExpress.XtraEditors.Controls.EditorButton[] {
            new DevExpress.XtraEditors.Controls.EditorButton(DevExpress.XtraEditors.Controls.ButtonPredefines.Combo)});
            this.comboCategory.Properties.TextEditStyle = DevExpress.XtraEditors.Controls.TextEditStyles.DisableTextEditor;
            this.comboCategory.Size = new System.Drawing.Size(120, 20);
            this.comboCategory.TabIndex = 6;
            //
            // textSearch
            //
            this.textSearch.Location = new System.Drawing.Point(628, 48);
            this.textSearch.Name = "textSearch";
            this.textSearch.Properties.NullValuePrompt = "Özet, kullanıcı, IP veya detayda ara...";
            this.textSearch.Size = new System.Drawing.Size(200, 20);
            this.textSearch.TabIndex = 7;
            this.textSearch.KeyDown += new System.Windows.Forms.KeyEventHandler(this.textSearch_KeyDown);
            //
            // buttonSearch
            //
            this.buttonSearch.Location = new System.Drawing.Point(836, 45);
            this.buttonSearch.Name = "buttonSearch";
            this.buttonSearch.Size = new System.Drawing.Size(80, 26);
            this.buttonSearch.TabIndex = 8;
            this.buttonSearch.Text = "Listele";
            this.buttonSearch.Click += new System.EventHandler(this.buttonSearch_Click);
            //
            // buttonVerify
            //
            this.buttonVerify.Anchor = ((System.Windows.Forms.AnchorStyles)((System.Windows.Forms.AnchorStyles.Top | System.Windows.Forms.AnchorStyles.Right)));
            this.buttonVerify.Location = new System.Drawing.Point(732, 10);
            this.buttonVerify.Name = "buttonVerify";
            this.buttonVerify.Size = new System.Drawing.Size(140, 26);
            this.buttonVerify.TabIndex = 9;
            this.buttonVerify.Text = "Bütünlük Kontrolü";
            this.buttonVerify.ToolTip = "Kayıtların sonradan değiştirilip silinmediğini imzalarından doğrular.";
            this.buttonVerify.Click += new System.EventHandler(this.buttonVerify_Click);
            //
            // buttonExport
            //
            this.buttonExport.Anchor = ((System.Windows.Forms.AnchorStyles)((System.Windows.Forms.AnchorStyles.Top | System.Windows.Forms.AnchorStyles.Right)));
            this.buttonExport.Location = new System.Drawing.Point(878, 10);
            this.buttonExport.Name = "buttonExport";
            this.buttonExport.Size = new System.Drawing.Size(110, 26);
            this.buttonExport.TabIndex = 10;
            this.buttonExport.Text = "Excel\'e Aktar";
            this.buttonExport.Click += new System.EventHandler(this.buttonExport_Click);
            //
            // gridControl
            //
            this.gridControl.Dock = System.Windows.Forms.DockStyle.Fill;
            this.gridControl.Location = new System.Drawing.Point(0, 84);
            this.gridControl.MainView = this.gridView;
            this.gridControl.Name = "gridControl";
            this.gridControl.Size = new System.Drawing.Size(1000, 460);
            this.gridControl.TabIndex = 1;
            this.gridControl.ViewCollection.AddRange(new DevExpress.XtraGrid.Views.Base.BaseView[] {
            this.gridView});
            //
            // gridView
            //
            this.gridView.Columns.AddRange(new DevExpress.XtraGrid.Columns.GridColumn[] {
            this.colTime,
            this.colSeverity,
            this.colCategory,
            this.colEventName,
            this.colSummary,
            this.colUserName,
            this.colIpAddress,
            this.colMail});
            this.gridView.GridControl = this.gridControl;
            this.gridView.Name = "gridView";
            this.gridView.CustomColumnDisplayText += new DevExpress.XtraGrid.Views.Base.CustomColumnDisplayTextEventHandler(this.gridView_CustomColumnDisplayText);
            this.gridView.RowCellStyle += new DevExpress.XtraGrid.Views.Grid.RowCellStyleEventHandler(this.gridView_RowCellStyle);
            this.gridView.FocusedRowChanged += new DevExpress.XtraGrid.Views.Base.FocusedRowChangedEventHandler(this.gridView_FocusedRowChanged);
            //
            // colTime
            //
            this.colTime.Caption = "Zaman";
            this.colTime.DisplayFormat.FormatString = "dd.MM.yyyy HH:mm:ss";
            this.colTime.DisplayFormat.FormatType = DevExpress.Utils.FormatType.DateTime;
            this.colTime.FieldName = "Time";
            this.colTime.Name = "colTime";
            this.colTime.Visible = true;
            this.colTime.VisibleIndex = 0;
            this.colTime.Width = 125;
            //
            // colSeverity
            //
            this.colSeverity.AppearanceCell.Options.UseTextOptions = true;
            this.colSeverity.AppearanceCell.TextOptions.HAlignment = DevExpress.Utils.HorzAlignment.Near;
            this.colSeverity.Caption = "Önem";
            this.colSeverity.FieldName = "Severity";
            this.colSeverity.FilterMode = DevExpress.XtraGrid.ColumnFilterMode.DisplayText;
            this.colSeverity.Name = "colSeverity";
            this.colSeverity.Visible = true;
            this.colSeverity.VisibleIndex = 1;
            this.colSeverity.Width = 60;
            //
            // colCategory
            //
            this.colCategory.Caption = "Grup";
            this.colCategory.FieldName = "Category";
            this.colCategory.Name = "colCategory";
            this.colCategory.Visible = true;
            this.colCategory.VisibleIndex = 2;
            this.colCategory.Width = 80;
            //
            // colEventName
            //
            this.colEventName.Caption = "Olay";
            this.colEventName.FieldName = "EventName";
            this.colEventName.Name = "colEventName";
            this.colEventName.Visible = true;
            this.colEventName.VisibleIndex = 3;
            this.colEventName.Width = 200;
            //
            // colSummary
            //
            this.colSummary.Caption = "Özet";
            this.colSummary.FieldName = "Summary";
            this.colSummary.Name = "colSummary";
            this.colSummary.Visible = true;
            this.colSummary.VisibleIndex = 4;
            this.colSummary.Width = 300;
            //
            // colUserName
            //
            this.colUserName.Caption = "Kullanıcı";
            this.colUserName.FieldName = "UserName";
            this.colUserName.Name = "colUserName";
            this.colUserName.Visible = true;
            this.colUserName.VisibleIndex = 5;
            this.colUserName.Width = 100;
            //
            // colIpAddress
            //
            this.colIpAddress.Caption = "IP";
            this.colIpAddress.FieldName = "IpAddress";
            this.colIpAddress.Name = "colIpAddress";
            this.colIpAddress.Visible = true;
            this.colIpAddress.VisibleIndex = 6;
            this.colIpAddress.Width = 100;
            //
            // colMail
            //
            this.colMail.Caption = "Mail";
            this.colMail.FieldName = "Mail";
            this.colMail.Name = "colMail";
            this.colMail.Visible = true;
            this.colMail.VisibleIndex = 7;
            this.colMail.Width = 70;
            //
            // splitterDetail
            //
            this.splitterDetail.Dock = System.Windows.Forms.DockStyle.Bottom;
            this.splitterDetail.Location = new System.Drawing.Point(0, 544);
            this.splitterDetail.Name = "splitterDetail";
            this.splitterDetail.Size = new System.Drawing.Size(1000, 6);
            this.splitterDetail.TabIndex = 2;
            this.splitterDetail.TabStop = false;
            //
            // memoDetail
            //
            this.memoDetail.Dock = System.Windows.Forms.DockStyle.Bottom;
            this.memoDetail.Location = new System.Drawing.Point(0, 550);
            this.memoDetail.Name = "memoDetail";
            this.memoDetail.Properties.ReadOnly = true;
            this.memoDetail.Size = new System.Drawing.Size(1000, 124);
            this.memoDetail.TabIndex = 3;
            //
            // labelStatus
            //
            this.labelStatus.Appearance.Options.UseForeColor = true;
            this.labelStatus.AutoSizeMode = DevExpress.XtraEditors.LabelAutoSizeMode.None;
            this.labelStatus.Dock = System.Windows.Forms.DockStyle.Bottom;
            this.labelStatus.Location = new System.Drawing.Point(0, 674);
            this.labelStatus.Name = "labelStatus";
            this.labelStatus.Padding = new System.Windows.Forms.Padding(6, 6, 0, 0);
            this.labelStatus.Size = new System.Drawing.Size(1000, 26);
            this.labelStatus.TabIndex = 4;
            //
            // EventLogModule
            //
            this.AutoScaleDimensions = new System.Drawing.SizeF(6F, 13F);
            this.AutoScaleMode = System.Windows.Forms.AutoScaleMode.Font;
            this.Controls.Add(this.gridControl);
            this.Controls.Add(this.splitterDetail);
            this.Controls.Add(this.memoDetail);
            this.Controls.Add(this.labelStatus);
            this.Controls.Add(this.panelTop);
            this.Name = "EventLogModule";
            this.Size = new System.Drawing.Size(1000, 700);
            ((System.ComponentModel.ISupportInitialize)(this.panelTop)).EndInit();
            this.panelTop.ResumeLayout(false);
            this.panelTop.PerformLayout();
            ((System.ComponentModel.ISupportInitialize)(this.dateFrom.Properties.CalendarTimeProperties)).EndInit();
            ((System.ComponentModel.ISupportInitialize)(this.dateFrom.Properties)).EndInit();
            ((System.ComponentModel.ISupportInitialize)(this.dateTo.Properties.CalendarTimeProperties)).EndInit();
            ((System.ComponentModel.ISupportInitialize)(this.dateTo.Properties)).EndInit();
            ((System.ComponentModel.ISupportInitialize)(this.comboSeverity.Properties)).EndInit();
            ((System.ComponentModel.ISupportInitialize)(this.comboCategory.Properties)).EndInit();
            ((System.ComponentModel.ISupportInitialize)(this.textSearch.Properties)).EndInit();
            ((System.ComponentModel.ISupportInitialize)(this.gridControl)).EndInit();
            ((System.ComponentModel.ISupportInitialize)(this.gridView)).EndInit();
            ((System.ComponentModel.ISupportInitialize)(this.memoDetail.Properties)).EndInit();
            this.ResumeLayout(false);

        }

        #endregion

        private DevExpress.XtraEditors.PanelControl panelTop;
        private DevExpress.XtraEditors.LabelControl labelTitle;
        private DevExpress.XtraEditors.LabelControl labelFrom;
        private DevExpress.XtraEditors.DateEdit dateFrom;
        private DevExpress.XtraEditors.LabelControl labelTo;
        private DevExpress.XtraEditors.DateEdit dateTo;
        private DevExpress.XtraEditors.ImageComboBoxEdit comboSeverity;
        private DevExpress.XtraEditors.ComboBoxEdit comboCategory;
        private DevExpress.XtraEditors.TextEdit textSearch;
        private DevExpress.XtraEditors.SimpleButton buttonSearch;
        private DevExpress.XtraEditors.SimpleButton buttonVerify;
        private DevExpress.XtraEditors.SimpleButton buttonExport;
        private DevExpress.XtraGrid.GridControl gridControl;
        private DevExpress.XtraGrid.Views.Grid.GridView gridView;
        private DevExpress.XtraGrid.Columns.GridColumn colTime;
        private DevExpress.XtraGrid.Columns.GridColumn colSeverity;
        private DevExpress.XtraGrid.Columns.GridColumn colCategory;
        private DevExpress.XtraGrid.Columns.GridColumn colEventName;
        private DevExpress.XtraGrid.Columns.GridColumn colSummary;
        private DevExpress.XtraGrid.Columns.GridColumn colUserName;
        private DevExpress.XtraGrid.Columns.GridColumn colIpAddress;
        private DevExpress.XtraGrid.Columns.GridColumn colMail;
        private DevExpress.XtraEditors.SplitterControl splitterDetail;
        private DevExpress.XtraEditors.MemoEdit memoDetail;
        private DevExpress.XtraEditors.LabelControl labelStatus;
    }
}
