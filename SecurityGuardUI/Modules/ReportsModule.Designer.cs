namespace SecurityGuardUI.Modules
{
    partial class ReportsModule
    {
        private System.ComponentModel.IContainer components = null;

        protected override void Dispose(bool disposing)
        {
            if (disposing && (components != null))
            {
                components.Dispose();
            }
            base.Dispose(disposing);
        }

        private static DevExpress.XtraGrid.Columns.GridColumn Col(string field, string caption, int width, int index)
        {
            return new DevExpress.XtraGrid.Columns.GridColumn { FieldName = field, Caption = caption, Width = width, Visible = true, VisibleIndex = index, Name = "col" + field };
        }

        #region Component Designer generated code

        private void InitializeComponent()
        {
            this.panelTop = new DevExpress.XtraEditors.PanelControl();
            this.labelTitle = new DevExpress.XtraEditors.LabelControl();
            this.labelInfo = new DevExpress.XtraEditors.LabelControl();
            this.buttonPreview = new DevExpress.XtraEditors.SimpleButton();
            this.buttonSend = new DevExpress.XtraEditors.SimpleButton();
            this.buttonRefresh = new DevExpress.XtraEditors.SimpleButton();
            this.split = new DevExpress.XtraEditors.SplitContainerControl();
            this.gridReports = new DevExpress.XtraGrid.GridControl();
            this.viewReports = new DevExpress.XtraGrid.Views.Grid.GridView();
            this.labelReport = new DevExpress.XtraEditors.LabelControl();
            this.web = new System.Windows.Forms.WebBrowser();
            this.labelStatus = new DevExpress.XtraEditors.LabelControl();
            ((System.ComponentModel.ISupportInitialize)(this.panelTop)).BeginInit();
            this.panelTop.SuspendLayout();
            ((System.ComponentModel.ISupportInitialize)(this.split)).BeginInit();
            this.split.SuspendLayout();
            ((System.ComponentModel.ISupportInitialize)(this.gridReports)).BeginInit();
            ((System.ComponentModel.ISupportInitialize)(this.viewReports)).BeginInit();
            this.SuspendLayout();
            //
            // panelTop
            //
            this.panelTop.BorderStyle = DevExpress.XtraEditors.Controls.BorderStyles.NoBorder;
            this.panelTop.Controls.Add(this.labelTitle);
            this.panelTop.Controls.Add(this.labelInfo);
            this.panelTop.Controls.Add(this.buttonPreview);
            this.panelTop.Controls.Add(this.buttonSend);
            this.panelTop.Controls.Add(this.buttonRefresh);
            this.panelTop.Dock = System.Windows.Forms.DockStyle.Top;
            this.panelTop.Size = new System.Drawing.Size(1000, 84);
            this.labelTitle.Appearance.Font = new System.Drawing.Font("Segoe UI", 14F);
            this.labelTitle.Appearance.Options.UseFont = true;
            this.labelTitle.Location = new System.Drawing.Point(12, 8);
            this.labelTitle.Text = "Günlük Rapor";
            this.labelInfo.Anchor = ((System.Windows.Forms.AnchorStyles)(((System.Windows.Forms.AnchorStyles.Top | System.Windows.Forms.AnchorStyles.Left) | System.Windows.Forms.AnchorStyles.Right)));
            this.labelInfo.Appearance.Options.UseTextOptions = true;
            this.labelInfo.Appearance.TextOptions.WordWrap = DevExpress.Utils.WordWrap.Wrap;
            this.labelInfo.AutoSizeMode = DevExpress.XtraEditors.LabelAutoSizeMode.None;
            this.labelInfo.Location = new System.Drawing.Point(12, 42);
            this.labelInfo.Size = new System.Drawing.Size(560, 34);
            this.labelInfo.Text = "Her gün rapor saatinde (Genel Ayarlar) güvenlik olayları, banlar, performans taraması, SQL bakımı ve servis sağlığı tek mailde gönderilir. "
                + "Alıcılar ve açma / kapama: Olay Kuralları → \"Günlük güvenlik ve performans raporu\".";
            this.buttonPreview.Anchor = ((System.Windows.Forms.AnchorStyles)((System.Windows.Forms.AnchorStyles.Top | System.Windows.Forms.AnchorStyles.Right)));
            this.buttonPreview.Location = new System.Drawing.Point(586, 10);
            this.buttonPreview.Size = new System.Drawing.Size(140, 26);
            this.buttonPreview.Text = "Önizle";
            this.buttonPreview.ToolTip = "Raporu şu anki durumla hazırlar; kaydetmez, göndermez.";
            this.buttonPreview.Click += new System.EventHandler(this.buttonPreview_Click);
            this.buttonSend.Anchor = ((System.Windows.Forms.AnchorStyles)((System.Windows.Forms.AnchorStyles.Top | System.Windows.Forms.AnchorStyles.Right)));
            this.buttonSend.Location = new System.Drawing.Point(732, 10);
            this.buttonSend.Size = new System.Drawing.Size(140, 26);
            this.buttonSend.Text = "Şimdi Gönder...";
            this.buttonSend.Click += new System.EventHandler(this.buttonSend_Click);
            this.buttonRefresh.Anchor = ((System.Windows.Forms.AnchorStyles)((System.Windows.Forms.AnchorStyles.Top | System.Windows.Forms.AnchorStyles.Right)));
            this.buttonRefresh.Location = new System.Drawing.Point(878, 10);
            this.buttonRefresh.Size = new System.Drawing.Size(110, 26);
            this.buttonRefresh.Text = "Yenile";
            this.buttonRefresh.Click += new System.EventHandler(this.buttonRefresh_Click);
            //
            // split
            //
            this.split.Dock = System.Windows.Forms.DockStyle.Fill;
            this.split.Panel1.Controls.Add(this.gridReports);
            this.split.Panel2.Controls.Add(this.web);
            this.split.Panel2.Controls.Add(this.labelReport);
            this.split.SplitterPosition = 380;
            this.gridReports.Dock = System.Windows.Forms.DockStyle.Fill;
            this.gridReports.MainView = this.viewReports;
            this.gridReports.ViewCollection.AddRange(new DevExpress.XtraGrid.Views.Base.BaseView[] { this.viewReports });
            this.viewReports.GridControl = this.gridReports;
            var colTime = Col("Time", "Tarih", 105, 0);
            colTime.DisplayFormat.FormatType = DevExpress.Utils.FormatType.DateTime;
            colTime.DisplayFormat.FormatString = "dd.MM.yyyy HH:mm";
            this.viewReports.Columns.AddRange(new DevExpress.XtraGrid.Columns.GridColumn[] {
                colTime, Col("Subject", "Özet", 200, 1), Col("Mail", "Mail", 90, 2), Col("Trigger", "Tetikleyen", 100, 3) });
            this.viewReports.FocusedRowChanged += new DevExpress.XtraGrid.Views.Base.FocusedRowChangedEventHandler(this.viewReports_FocusedRowChanged);
            this.labelReport.AutoEllipsis = true;
            this.labelReport.AutoSizeMode = DevExpress.XtraEditors.LabelAutoSizeMode.None;
            this.labelReport.Dock = System.Windows.Forms.DockStyle.Top;
            this.labelReport.Padding = new System.Windows.Forms.Padding(6, 6, 0, 0);
            this.labelReport.Size = new System.Drawing.Size(600, 26);
            this.web.Dock = System.Windows.Forms.DockStyle.Fill;
            this.web.AllowWebBrowserDrop = false;
            this.web.IsWebBrowserContextMenuEnabled = false;
            this.web.ScriptErrorsSuppressed = true;
            this.web.WebBrowserShortcutsEnabled = true;
            //
            // labelStatus
            //
            this.labelStatus.Appearance.Options.UseForeColor = true;
            this.labelStatus.AutoSizeMode = DevExpress.XtraEditors.LabelAutoSizeMode.None;
            this.labelStatus.Dock = System.Windows.Forms.DockStyle.Bottom;
            this.labelStatus.Padding = new System.Windows.Forms.Padding(6, 6, 0, 0);
            this.labelStatus.Size = new System.Drawing.Size(1000, 26);
            //
            // ReportsModule
            //
            this.AutoScaleDimensions = new System.Drawing.SizeF(6F, 13F);
            this.AutoScaleMode = System.Windows.Forms.AutoScaleMode.Font;
            this.Controls.Add(this.split);
            this.Controls.Add(this.labelStatus);
            this.Controls.Add(this.panelTop);
            this.Name = "ReportsModule";
            this.Size = new System.Drawing.Size(1000, 700);
            ((System.ComponentModel.ISupportInitialize)(this.panelTop)).EndInit();
            this.panelTop.ResumeLayout(false);
            ((System.ComponentModel.ISupportInitialize)(this.split)).EndInit();
            this.split.ResumeLayout(false);
            ((System.ComponentModel.ISupportInitialize)(this.gridReports)).EndInit();
            ((System.ComponentModel.ISupportInitialize)(this.viewReports)).EndInit();
            this.ResumeLayout(false);
        }

        #endregion

        private DevExpress.XtraEditors.PanelControl panelTop;
        private DevExpress.XtraEditors.LabelControl labelTitle;
        private DevExpress.XtraEditors.LabelControl labelInfo;
        private DevExpress.XtraEditors.SimpleButton buttonPreview;
        private DevExpress.XtraEditors.SimpleButton buttonSend;
        private DevExpress.XtraEditors.SimpleButton buttonRefresh;
        private DevExpress.XtraEditors.SplitContainerControl split;
        private DevExpress.XtraGrid.GridControl gridReports;
        private DevExpress.XtraGrid.Views.Grid.GridView viewReports;
        private DevExpress.XtraEditors.LabelControl labelReport;
        private System.Windows.Forms.WebBrowser web;
        private DevExpress.XtraEditors.LabelControl labelStatus;
    }
}
