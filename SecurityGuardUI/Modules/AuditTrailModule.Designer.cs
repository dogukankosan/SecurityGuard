namespace SecurityGuardUI.Modules
{
    partial class AuditTrailModule
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
            this.textSearch = new DevExpress.XtraEditors.TextEdit();
            this.buttonSearch = new DevExpress.XtraEditors.SimpleButton();
            this.buttonExport = new DevExpress.XtraEditors.SimpleButton();
            this.gridControl = new DevExpress.XtraGrid.GridControl();
            this.gridView = new DevExpress.XtraGrid.Views.Grid.GridView();
            this.colTime = new DevExpress.XtraGrid.Columns.GridColumn();
            this.colUserName = new DevExpress.XtraGrid.Columns.GridColumn();
            this.colAction = new DevExpress.XtraGrid.Columns.GridColumn();
            this.colDetail = new DevExpress.XtraGrid.Columns.GridColumn();
            this.labelStatus = new DevExpress.XtraEditors.LabelControl();
            ((System.ComponentModel.ISupportInitialize)(this.panelTop)).BeginInit();
            this.panelTop.SuspendLayout();
            ((System.ComponentModel.ISupportInitialize)(this.dateFrom.Properties.CalendarTimeProperties)).BeginInit();
            ((System.ComponentModel.ISupportInitialize)(this.dateFrom.Properties)).BeginInit();
            ((System.ComponentModel.ISupportInitialize)(this.dateTo.Properties.CalendarTimeProperties)).BeginInit();
            ((System.ComponentModel.ISupportInitialize)(this.dateTo.Properties)).BeginInit();
            ((System.ComponentModel.ISupportInitialize)(this.textSearch.Properties)).BeginInit();
            ((System.ComponentModel.ISupportInitialize)(this.gridControl)).BeginInit();
            ((System.ComponentModel.ISupportInitialize)(this.gridView)).BeginInit();
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
            this.panelTop.Controls.Add(this.textSearch);
            this.panelTop.Controls.Add(this.buttonSearch);
            this.panelTop.Controls.Add(this.buttonExport);
            this.panelTop.Dock = System.Windows.Forms.DockStyle.Top;
            this.panelTop.Location = new System.Drawing.Point(0, 0);
            this.panelTop.Name = "panelTop";
            this.panelTop.Size = new System.Drawing.Size(900, 84);
            this.panelTop.TabIndex = 0;
            //
            // labelTitle
            //
            this.labelTitle.Appearance.Font = new System.Drawing.Font("Segoe UI", 14F);
            this.labelTitle.Appearance.Options.UseFont = true;
            this.labelTitle.Location = new System.Drawing.Point(12, 8);
            this.labelTitle.Name = "labelTitle";
            this.labelTitle.Size = new System.Drawing.Size(99, 25);
            this.labelTitle.TabIndex = 0;
            this.labelTitle.Text = "İşlem Kaydı";
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
            this.dateFrom.Properties.DisplayFormat.FormatString = "dd.MM.yyyy HH:mm";
            this.dateFrom.Properties.DisplayFormat.FormatType = DevExpress.Utils.FormatType.DateTime;
            this.dateFrom.Properties.EditFormat.FormatString = "dd.MM.yyyy HH:mm";
            this.dateFrom.Properties.EditFormat.FormatType = DevExpress.Utils.FormatType.DateTime;
            this.dateFrom.Properties.Mask.EditMask = "dd.MM.yyyy HH:mm";
            this.dateFrom.Properties.Mask.UseMaskAsDisplayFormat = true;
            this.dateFrom.Size = new System.Drawing.Size(130, 20);
            this.dateFrom.TabIndex = 2;
            //
            // labelTo
            //
            this.labelTo.Location = new System.Drawing.Point(210, 51);
            this.labelTo.Name = "labelTo";
            this.labelTo.Size = new System.Drawing.Size(20, 13);
            this.labelTo.TabIndex = 3;
            this.labelTo.Text = "Bitiş";
            //
            // dateTo
            //
            this.dateTo.EditValue = null;
            this.dateTo.Location = new System.Drawing.Point(238, 48);
            this.dateTo.Name = "dateTo";
            this.dateTo.Properties.Buttons.AddRange(new DevExpress.XtraEditors.Controls.EditorButton[] {
            new DevExpress.XtraEditors.Controls.EditorButton(DevExpress.XtraEditors.Controls.ButtonPredefines.Combo)});
            this.dateTo.Properties.CalendarTimeEditing = DevExpress.Utils.DefaultBoolean.True;
            this.dateTo.Properties.CalendarTimeProperties.Buttons.AddRange(new DevExpress.XtraEditors.Controls.EditorButton[] {
            new DevExpress.XtraEditors.Controls.EditorButton(DevExpress.XtraEditors.Controls.ButtonPredefines.Combo)});
            this.dateTo.Properties.DisplayFormat.FormatString = "dd.MM.yyyy HH:mm";
            this.dateTo.Properties.DisplayFormat.FormatType = DevExpress.Utils.FormatType.DateTime;
            this.dateTo.Properties.EditFormat.FormatString = "dd.MM.yyyy HH:mm";
            this.dateTo.Properties.EditFormat.FormatType = DevExpress.Utils.FormatType.DateTime;
            this.dateTo.Properties.Mask.EditMask = "dd.MM.yyyy HH:mm";
            this.dateTo.Properties.Mask.UseMaskAsDisplayFormat = true;
            this.dateTo.Size = new System.Drawing.Size(130, 20);
            this.dateTo.TabIndex = 4;
            //
            // textSearch
            //
            this.textSearch.Location = new System.Drawing.Point(384, 48);
            this.textSearch.Name = "textSearch";
            this.textSearch.Properties.NullValuePrompt = "Kullanıcı, işlem veya detayda ara...";
            this.textSearch.Size = new System.Drawing.Size(220, 20);
            this.textSearch.TabIndex = 5;
            this.textSearch.KeyDown += new System.Windows.Forms.KeyEventHandler(this.textSearch_KeyDown);
            //
            // buttonSearch
            //
            this.buttonSearch.Location = new System.Drawing.Point(614, 45);
            this.buttonSearch.Name = "buttonSearch";
            this.buttonSearch.Size = new System.Drawing.Size(80, 26);
            this.buttonSearch.TabIndex = 6;
            this.buttonSearch.Text = "Listele";
            this.buttonSearch.Click += new System.EventHandler(this.buttonSearch_Click);
            //
            // buttonExport
            //
            this.buttonExport.Anchor = ((System.Windows.Forms.AnchorStyles)((System.Windows.Forms.AnchorStyles.Top | System.Windows.Forms.AnchorStyles.Right)));
            this.buttonExport.Location = new System.Drawing.Point(778, 45);
            this.buttonExport.Name = "buttonExport";
            this.buttonExport.Size = new System.Drawing.Size(110, 26);
            this.buttonExport.TabIndex = 7;
            this.buttonExport.Text = "Excel\'e Aktar";
            this.buttonExport.Click += new System.EventHandler(this.buttonExport_Click);
            //
            // gridControl
            //
            this.gridControl.Dock = System.Windows.Forms.DockStyle.Fill;
            this.gridControl.Location = new System.Drawing.Point(0, 84);
            this.gridControl.MainView = this.gridView;
            this.gridControl.Name = "gridControl";
            this.gridControl.Size = new System.Drawing.Size(900, 392);
            this.gridControl.TabIndex = 1;
            this.gridControl.ViewCollection.AddRange(new DevExpress.XtraGrid.Views.Base.BaseView[] {
            this.gridView});
            //
            // gridView
            //
            this.gridView.Columns.AddRange(new DevExpress.XtraGrid.Columns.GridColumn[] {
            this.colTime,
            this.colUserName,
            this.colAction,
            this.colDetail});
            this.gridView.GridControl = this.gridControl;
            this.gridView.Name = "gridView";
            //
            // colTime
            //
            this.colTime.Caption = "Zaman";
            this.colTime.DisplayFormat.FormatString = "dd.MM.yyyy HH:mm:ss";
            this.colTime.DisplayFormat.FormatType = DevExpress.Utils.FormatType.DateTime;
            this.colTime.FieldName = "Time";
            this.colTime.MaxWidth = 160;
            this.colTime.MinWidth = 130;
            this.colTime.Name = "colTime";
            this.colTime.Visible = true;
            this.colTime.VisibleIndex = 0;
            this.colTime.Width = 140;
            //
            // colUserName
            //
            this.colUserName.Caption = "Kullanıcı";
            this.colUserName.FieldName = "UserName";
            this.colUserName.MaxWidth = 180;
            this.colUserName.Name = "colUserName";
            this.colUserName.Visible = true;
            this.colUserName.VisibleIndex = 1;
            this.colUserName.Width = 120;
            //
            // colAction
            //
            this.colAction.Caption = "İşlem";
            this.colAction.FieldName = "Action";
            this.colAction.Name = "colAction";
            this.colAction.Visible = true;
            this.colAction.VisibleIndex = 2;
            this.colAction.Width = 220;
            //
            // colDetail
            //
            this.colDetail.Caption = "Detay";
            this.colDetail.FieldName = "Detail";
            this.colDetail.Name = "colDetail";
            this.colDetail.Visible = true;
            this.colDetail.VisibleIndex = 3;
            this.colDetail.Width = 400;
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
            // AuditTrailModule
            //
            this.AutoScaleDimensions = new System.Drawing.SizeF(6F, 13F);
            this.AutoScaleMode = System.Windows.Forms.AutoScaleMode.Font;
            this.Controls.Add(this.gridControl);
            this.Controls.Add(this.labelStatus);
            this.Controls.Add(this.panelTop);
            this.Name = "AuditTrailModule";
            this.Size = new System.Drawing.Size(900, 500);
            ((System.ComponentModel.ISupportInitialize)(this.panelTop)).EndInit();
            this.panelTop.ResumeLayout(false);
            this.panelTop.PerformLayout();
            ((System.ComponentModel.ISupportInitialize)(this.dateFrom.Properties.CalendarTimeProperties)).EndInit();
            ((System.ComponentModel.ISupportInitialize)(this.dateFrom.Properties)).EndInit();
            ((System.ComponentModel.ISupportInitialize)(this.dateTo.Properties.CalendarTimeProperties)).EndInit();
            ((System.ComponentModel.ISupportInitialize)(this.dateTo.Properties)).EndInit();
            ((System.ComponentModel.ISupportInitialize)(this.textSearch.Properties)).EndInit();
            ((System.ComponentModel.ISupportInitialize)(this.gridControl)).EndInit();
            ((System.ComponentModel.ISupportInitialize)(this.gridView)).EndInit();
            this.ResumeLayout(false);

        }

        #endregion

        private DevExpress.XtraEditors.PanelControl panelTop;
        private DevExpress.XtraEditors.LabelControl labelTitle;
        private DevExpress.XtraEditors.LabelControl labelFrom;
        private DevExpress.XtraEditors.DateEdit dateFrom;
        private DevExpress.XtraEditors.LabelControl labelTo;
        private DevExpress.XtraEditors.DateEdit dateTo;
        private DevExpress.XtraEditors.TextEdit textSearch;
        private DevExpress.XtraEditors.SimpleButton buttonSearch;
        private DevExpress.XtraEditors.SimpleButton buttonExport;
        private DevExpress.XtraGrid.GridControl gridControl;
        private DevExpress.XtraGrid.Views.Grid.GridView gridView;
        private DevExpress.XtraGrid.Columns.GridColumn colTime;
        private DevExpress.XtraGrid.Columns.GridColumn colUserName;
        private DevExpress.XtraGrid.Columns.GridColumn colAction;
        private DevExpress.XtraGrid.Columns.GridColumn colDetail;
        private DevExpress.XtraEditors.LabelControl labelStatus;
    }
}
