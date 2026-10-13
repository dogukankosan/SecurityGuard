namespace SecurityGuardUI.Modules
{
    partial class BanListModule
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
            this.labelFirewall = new DevExpress.XtraEditors.LabelControl();
            this.buttonBan = new DevExpress.XtraEditors.SimpleButton();
            this.buttonUnban = new DevExpress.XtraEditors.SimpleButton();
            this.buttonResetAttempts = new DevExpress.XtraEditors.SimpleButton();
            this.buttonSync = new DevExpress.XtraEditors.SimpleButton();
            this.buttonRefresh = new DevExpress.XtraEditors.SimpleButton();
            this.buttonExport = new DevExpress.XtraEditors.SimpleButton();
            this.tabControl = new DevExpress.XtraTab.XtraTabControl();
            this.tabBanned = new DevExpress.XtraTab.XtraTabPage();
            this.gridBanned = new DevExpress.XtraGrid.GridControl();
            this.viewBanned = new DevExpress.XtraGrid.Views.Grid.GridView();
            this.tabAttempts = new DevExpress.XtraTab.XtraTabPage();
            this.gridAttempts = new DevExpress.XtraGrid.GridControl();
            this.viewAttempts = new DevExpress.XtraGrid.Views.Grid.GridView();
            this.colBanIp = new DevExpress.XtraGrid.Columns.GridColumn();
            this.colBanProtocol = new DevExpress.XtraGrid.Columns.GridColumn();
            this.colBanReason = new DevExpress.XtraGrid.Columns.GridColumn();
            this.colBanFail = new DevExpress.XtraGrid.Columns.GridColumn();
            this.colBanKind = new DevExpress.XtraGrid.Columns.GridColumn();
            this.colBanRule = new DevExpress.XtraGrid.Columns.GridColumn();
            this.colBanTime = new DevExpress.XtraGrid.Columns.GridColumn();
            this.colAttIp = new DevExpress.XtraGrid.Columns.GridColumn();
            this.colAttProtocol = new DevExpress.XtraGrid.Columns.GridColumn();
            this.colAttFail = new DevExpress.XtraGrid.Columns.GridColumn();
            this.colAttThreshold = new DevExpress.XtraGrid.Columns.GridColumn();
            this.colAttSuccess = new DevExpress.XtraGrid.Columns.GridColumn();
            this.colAttUser = new DevExpress.XtraGrid.Columns.GridColumn();
            this.colAttFirst = new DevExpress.XtraGrid.Columns.GridColumn();
            this.colAttLast = new DevExpress.XtraGrid.Columns.GridColumn();
            this.colAttState = new DevExpress.XtraGrid.Columns.GridColumn();
            ((System.ComponentModel.ISupportInitialize)(this.panelTop)).BeginInit();
            this.panelTop.SuspendLayout();
            ((System.ComponentModel.ISupportInitialize)(this.tabControl)).BeginInit();
            this.tabControl.SuspendLayout();
            this.tabBanned.SuspendLayout();
            ((System.ComponentModel.ISupportInitialize)(this.gridBanned)).BeginInit();
            ((System.ComponentModel.ISupportInitialize)(this.viewBanned)).BeginInit();
            this.tabAttempts.SuspendLayout();
            ((System.ComponentModel.ISupportInitialize)(this.gridAttempts)).BeginInit();
            ((System.ComponentModel.ISupportInitialize)(this.viewAttempts)).BeginInit();
            this.SuspendLayout();
            //
            // panelTop
            //
            this.panelTop.BorderStyle = DevExpress.XtraEditors.Controls.BorderStyles.NoBorder;
            this.panelTop.Controls.Add(this.labelTitle);
            this.panelTop.Controls.Add(this.labelFirewall);
            this.panelTop.Controls.Add(this.buttonBan);
            this.panelTop.Controls.Add(this.buttonUnban);
            this.panelTop.Controls.Add(this.buttonResetAttempts);
            this.panelTop.Controls.Add(this.buttonSync);
            this.panelTop.Controls.Add(this.buttonRefresh);
            this.panelTop.Controls.Add(this.buttonExport);
            this.panelTop.Dock = System.Windows.Forms.DockStyle.Top;
            this.panelTop.Location = new System.Drawing.Point(0, 0);
            this.panelTop.Name = "panelTop";
            this.panelTop.Size = new System.Drawing.Size(1000, 88);
            this.panelTop.TabIndex = 0;
            //
            // labelTitle
            //
            this.labelTitle.Appearance.Font = new System.Drawing.Font("Segoe UI", 14F);
            this.labelTitle.Appearance.Options.UseFont = true;
            this.labelTitle.Location = new System.Drawing.Point(12, 8);
            this.labelTitle.Name = "labelTitle";
            this.labelTitle.Size = new System.Drawing.Size(94, 25);
            this.labelTitle.TabIndex = 0;
            this.labelTitle.Text = "Ban Listesi";
            //
            // labelFirewall
            //
            this.labelFirewall.Appearance.Options.UseForeColor = true;
            this.labelFirewall.Location = new System.Drawing.Point(12, 38);
            this.labelFirewall.Name = "labelFirewall";
            this.labelFirewall.Size = new System.Drawing.Size(0, 13);
            this.labelFirewall.TabIndex = 1;
            //
            // buttonBan
            //
            this.buttonBan.Location = new System.Drawing.Point(12, 56);
            this.buttonBan.Name = "buttonBan";
            this.buttonBan.Size = new System.Drawing.Size(110, 26);
            this.buttonBan.TabIndex = 2;
            this.buttonBan.Text = "IP Banla...";
            this.buttonBan.Click += new System.EventHandler(this.buttonBan_Click);
            //
            // buttonUnban
            //
            this.buttonUnban.Location = new System.Drawing.Point(128, 56);
            this.buttonUnban.Name = "buttonUnban";
            this.buttonUnban.Size = new System.Drawing.Size(110, 26);
            this.buttonUnban.TabIndex = 3;
            this.buttonUnban.Text = "Banı Kaldır";
            this.buttonUnban.Click += new System.EventHandler(this.buttonUnban_Click);
            //
            // buttonResetAttempts
            //
            this.buttonResetAttempts.Location = new System.Drawing.Point(244, 56);
            this.buttonResetAttempts.Name = "buttonResetAttempts";
            this.buttonResetAttempts.Size = new System.Drawing.Size(110, 26);
            this.buttonResetAttempts.TabIndex = 4;
            this.buttonResetAttempts.Text = "Sayacı Sil";
            this.buttonResetAttempts.Click += new System.EventHandler(this.buttonResetAttempts_Click);
            //
            // buttonSync
            //
            this.buttonSync.Anchor = ((System.Windows.Forms.AnchorStyles)((System.Windows.Forms.AnchorStyles.Top | System.Windows.Forms.AnchorStyles.Right)));
            this.buttonSync.Location = new System.Drawing.Point(632, 10);
            this.buttonSync.Name = "buttonSync";
            this.buttonSync.Size = new System.Drawing.Size(130, 26);
            this.buttonSync.TabIndex = 5;
            this.buttonSync.Text = "Firewall Kontrol";
            this.buttonSync.Click += new System.EventHandler(this.buttonSync_Click);
            //
            // buttonRefresh
            //
            this.buttonRefresh.Anchor = ((System.Windows.Forms.AnchorStyles)((System.Windows.Forms.AnchorStyles.Top | System.Windows.Forms.AnchorStyles.Right)));
            this.buttonRefresh.Location = new System.Drawing.Point(768, 10);
            this.buttonRefresh.Name = "buttonRefresh";
            this.buttonRefresh.Size = new System.Drawing.Size(100, 26);
            this.buttonRefresh.TabIndex = 6;
            this.buttonRefresh.Text = "Yenile";
            this.buttonRefresh.Click += new System.EventHandler(this.buttonRefresh_Click);
            //
            // buttonExport
            //
            this.buttonExport.Anchor = ((System.Windows.Forms.AnchorStyles)((System.Windows.Forms.AnchorStyles.Top | System.Windows.Forms.AnchorStyles.Right)));
            this.buttonExport.Location = new System.Drawing.Point(874, 10);
            this.buttonExport.Name = "buttonExport";
            this.buttonExport.Size = new System.Drawing.Size(114, 26);
            this.buttonExport.TabIndex = 7;
            this.buttonExport.Text = "Excel\'e Aktar";
            this.buttonExport.Click += new System.EventHandler(this.buttonExport_Click);
            //
            // tabControl
            //
            this.tabControl.Dock = System.Windows.Forms.DockStyle.Fill;
            this.tabControl.Location = new System.Drawing.Point(0, 88);
            this.tabControl.Name = "tabControl";
            this.tabControl.SelectedTabPage = this.tabBanned;
            this.tabControl.Size = new System.Drawing.Size(1000, 612);
            this.tabControl.TabIndex = 1;
            this.tabControl.TabPages.AddRange(new DevExpress.XtraTab.XtraTabPage[] {
            this.tabBanned,
            this.tabAttempts});
            this.tabControl.SelectedPageChanged += new DevExpress.XtraTab.TabPageChangedEventHandler(this.tabControl_SelectedPageChanged);
            //
            // tabBanned
            //
            this.tabBanned.Controls.Add(this.gridBanned);
            this.tabBanned.Name = "tabBanned";
            this.tabBanned.Size = new System.Drawing.Size(998, 587);
            this.tabBanned.Text = "Banlı IP\'ler";
            //
            // gridBanned
            //
            this.gridBanned.Dock = System.Windows.Forms.DockStyle.Fill;
            this.gridBanned.MainView = this.viewBanned;
            this.gridBanned.Name = "gridBanned";
            this.gridBanned.TabIndex = 0;
            this.gridBanned.ViewCollection.AddRange(new DevExpress.XtraGrid.Views.Base.BaseView[] {
            this.viewBanned});
            //
            // viewBanned
            //
            this.viewBanned.Columns.AddRange(new DevExpress.XtraGrid.Columns.GridColumn[] {
            this.colBanIp,
            this.colBanProtocol,
            this.colBanReason,
            this.colBanFail,
            this.colBanKind,
            this.colBanRule,
            this.colBanTime});
            this.viewBanned.GridControl = this.gridBanned;
            this.viewBanned.Name = "viewBanned";
            //
            // colBanIp
            //
            this.colBanIp.Caption = "IP";
            this.colBanIp.FieldName = "IpAddress";
            this.colBanIp.Name = "colBanIp";
            this.colBanIp.Visible = true;
            this.colBanIp.VisibleIndex = 0;
            this.colBanIp.Width = 120;
            //
            // colBanProtocol
            //
            this.colBanProtocol.Caption = "Protokol";
            this.colBanProtocol.FieldName = "Protocol";
            this.colBanProtocol.Name = "colBanProtocol";
            this.colBanProtocol.Visible = true;
            this.colBanProtocol.VisibleIndex = 1;
            this.colBanProtocol.Width = 70;
            //
            // colBanReason
            //
            this.colBanReason.Caption = "Sebep";
            this.colBanReason.FieldName = "Reason";
            this.colBanReason.Name = "colBanReason";
            this.colBanReason.Visible = true;
            this.colBanReason.VisibleIndex = 2;
            this.colBanReason.Width = 320;
            //
            // colBanFail
            //
            this.colBanFail.Caption = "Deneme";
            this.colBanFail.FieldName = "FailCount";
            this.colBanFail.Name = "colBanFail";
            this.colBanFail.Visible = true;
            this.colBanFail.VisibleIndex = 3;
            this.colBanFail.Width = 60;
            //
            // colBanKind
            //
            this.colBanKind.Caption = "Tür";
            this.colBanKind.FieldName = "Kind";
            this.colBanKind.Name = "colBanKind";
            this.colBanKind.Visible = true;
            this.colBanKind.VisibleIndex = 4;
            this.colBanKind.Width = 70;
            //
            // colBanRule
            //
            this.colBanRule.Caption = "Firewall kuralı";
            this.colBanRule.FieldName = "RuleName";
            this.colBanRule.Name = "colBanRule";
            this.colBanRule.Visible = true;
            this.colBanRule.VisibleIndex = 5;
            this.colBanRule.Width = 150;
            //
            // colBanTime
            //
            this.colBanTime.Caption = "Banlanma zamanı";
            this.colBanTime.DisplayFormat.FormatString = "dd.MM.yyyy HH:mm:ss";
            this.colBanTime.DisplayFormat.FormatType = DevExpress.Utils.FormatType.DateTime;
            this.colBanTime.FieldName = "Time";
            this.colBanTime.Name = "colBanTime";
            this.colBanTime.Visible = true;
            this.colBanTime.VisibleIndex = 6;
            this.colBanTime.Width = 130;
            //
            // tabAttempts
            //
            this.tabAttempts.Controls.Add(this.gridAttempts);
            this.tabAttempts.Name = "tabAttempts";
            this.tabAttempts.Size = new System.Drawing.Size(998, 587);
            this.tabAttempts.Text = "Giriş Denemeleri";
            //
            // gridAttempts
            //
            this.gridAttempts.Dock = System.Windows.Forms.DockStyle.Fill;
            this.gridAttempts.MainView = this.viewAttempts;
            this.gridAttempts.Name = "gridAttempts";
            this.gridAttempts.TabIndex = 0;
            this.gridAttempts.ViewCollection.AddRange(new DevExpress.XtraGrid.Views.Base.BaseView[] {
            this.viewAttempts});
            //
            // viewAttempts
            //
            this.viewAttempts.Columns.AddRange(new DevExpress.XtraGrid.Columns.GridColumn[] {
            this.colAttIp,
            this.colAttProtocol,
            this.colAttFail,
            this.colAttThreshold,
            this.colAttSuccess,
            this.colAttUser,
            this.colAttFirst,
            this.colAttLast,
            this.colAttState});
            this.viewAttempts.GridControl = this.gridAttempts;
            this.viewAttempts.Name = "viewAttempts";
            this.viewAttempts.RowCellStyle += new DevExpress.XtraGrid.Views.Grid.RowCellStyleEventHandler(this.viewAttempts_RowCellStyle);
            //
            // colAttIp
            //
            this.colAttIp.Caption = "IP";
            this.colAttIp.FieldName = "IpAddress";
            this.colAttIp.Name = "colAttIp";
            this.colAttIp.Visible = true;
            this.colAttIp.VisibleIndex = 0;
            this.colAttIp.Width = 120;
            //
            // colAttProtocol
            //
            this.colAttProtocol.Caption = "Protokol";
            this.colAttProtocol.FieldName = "Protocol";
            this.colAttProtocol.Name = "colAttProtocol";
            this.colAttProtocol.Visible = true;
            this.colAttProtocol.VisibleIndex = 1;
            this.colAttProtocol.Width = 70;
            //
            // colAttFail
            //
            this.colAttFail.Caption = "Başarısız";
            this.colAttFail.FieldName = "FailCount";
            this.colAttFail.Name = "colAttFail";
            this.colAttFail.Visible = true;
            this.colAttFail.VisibleIndex = 2;
            this.colAttFail.Width = 70;
            //
            // colAttThreshold
            //
            this.colAttThreshold.Caption = "Eşik";
            this.colAttThreshold.FieldName = "Threshold";
            this.colAttThreshold.Name = "colAttThreshold";
            this.colAttThreshold.Visible = true;
            this.colAttThreshold.VisibleIndex = 3;
            this.colAttThreshold.Width = 50;
            //
            // colAttSuccess
            //
            this.colAttSuccess.Caption = "Başarılı";
            this.colAttSuccess.FieldName = "SuccessCount";
            this.colAttSuccess.Name = "colAttSuccess";
            this.colAttSuccess.Visible = true;
            this.colAttSuccess.VisibleIndex = 4;
            this.colAttSuccess.Width = 70;
            //
            // colAttUser
            //
            this.colAttUser.Caption = "Son kullanıcı";
            this.colAttUser.FieldName = "LastUserName";
            this.colAttUser.Name = "colAttUser";
            this.colAttUser.Visible = true;
            this.colAttUser.VisibleIndex = 5;
            this.colAttUser.Width = 130;
            //
            // colAttFirst
            //
            this.colAttFirst.Caption = "İlk deneme";
            this.colAttFirst.DisplayFormat.FormatString = "dd.MM.yyyy HH:mm:ss";
            this.colAttFirst.DisplayFormat.FormatType = DevExpress.Utils.FormatType.DateTime;
            this.colAttFirst.FieldName = "First";
            this.colAttFirst.Name = "colAttFirst";
            this.colAttFirst.Visible = true;
            this.colAttFirst.VisibleIndex = 6;
            this.colAttFirst.Width = 130;
            //
            // colAttLast
            //
            this.colAttLast.Caption = "Son deneme";
            this.colAttLast.DisplayFormat.FormatString = "dd.MM.yyyy HH:mm:ss";
            this.colAttLast.DisplayFormat.FormatType = DevExpress.Utils.FormatType.DateTime;
            this.colAttLast.FieldName = "Last";
            this.colAttLast.Name = "colAttLast";
            this.colAttLast.Visible = true;
            this.colAttLast.VisibleIndex = 7;
            this.colAttLast.Width = 130;
            //
            // colAttState
            //
            this.colAttState.Caption = "Durum";
            this.colAttState.FieldName = "State";
            this.colAttState.Name = "colAttState";
            this.colAttState.Visible = true;
            this.colAttState.VisibleIndex = 8;
            this.colAttState.Width = 140;
            //
            // BanListModule
            //
            this.AutoScaleDimensions = new System.Drawing.SizeF(6F, 13F);
            this.AutoScaleMode = System.Windows.Forms.AutoScaleMode.Font;
            this.Controls.Add(this.tabControl);
            this.Controls.Add(this.panelTop);
            this.Name = "BanListModule";
            this.Size = new System.Drawing.Size(1000, 700);
            ((System.ComponentModel.ISupportInitialize)(this.panelTop)).EndInit();
            this.panelTop.ResumeLayout(false);
            this.panelTop.PerformLayout();
            ((System.ComponentModel.ISupportInitialize)(this.tabControl)).EndInit();
            this.tabControl.ResumeLayout(false);
            this.tabBanned.ResumeLayout(false);
            ((System.ComponentModel.ISupportInitialize)(this.gridBanned)).EndInit();
            ((System.ComponentModel.ISupportInitialize)(this.viewBanned)).EndInit();
            this.tabAttempts.ResumeLayout(false);
            ((System.ComponentModel.ISupportInitialize)(this.gridAttempts)).EndInit();
            ((System.ComponentModel.ISupportInitialize)(this.viewAttempts)).EndInit();
            this.ResumeLayout(false);

        }

        #endregion

        private DevExpress.XtraEditors.PanelControl panelTop;
        private DevExpress.XtraEditors.LabelControl labelTitle;
        private DevExpress.XtraEditors.LabelControl labelFirewall;
        private DevExpress.XtraEditors.SimpleButton buttonBan;
        private DevExpress.XtraEditors.SimpleButton buttonUnban;
        private DevExpress.XtraEditors.SimpleButton buttonResetAttempts;
        private DevExpress.XtraEditors.SimpleButton buttonSync;
        private DevExpress.XtraEditors.SimpleButton buttonRefresh;
        private DevExpress.XtraEditors.SimpleButton buttonExport;
        private DevExpress.XtraTab.XtraTabControl tabControl;
        private DevExpress.XtraTab.XtraTabPage tabBanned;
        private DevExpress.XtraGrid.GridControl gridBanned;
        private DevExpress.XtraGrid.Views.Grid.GridView viewBanned;
        private DevExpress.XtraTab.XtraTabPage tabAttempts;
        private DevExpress.XtraGrid.GridControl gridAttempts;
        private DevExpress.XtraGrid.Views.Grid.GridView viewAttempts;
        private DevExpress.XtraGrid.Columns.GridColumn colBanIp;
        private DevExpress.XtraGrid.Columns.GridColumn colBanProtocol;
        private DevExpress.XtraGrid.Columns.GridColumn colBanReason;
        private DevExpress.XtraGrid.Columns.GridColumn colBanFail;
        private DevExpress.XtraGrid.Columns.GridColumn colBanKind;
        private DevExpress.XtraGrid.Columns.GridColumn colBanRule;
        private DevExpress.XtraGrid.Columns.GridColumn colBanTime;
        private DevExpress.XtraGrid.Columns.GridColumn colAttIp;
        private DevExpress.XtraGrid.Columns.GridColumn colAttProtocol;
        private DevExpress.XtraGrid.Columns.GridColumn colAttFail;
        private DevExpress.XtraGrid.Columns.GridColumn colAttThreshold;
        private DevExpress.XtraGrid.Columns.GridColumn colAttSuccess;
        private DevExpress.XtraGrid.Columns.GridColumn colAttUser;
        private DevExpress.XtraGrid.Columns.GridColumn colAttFirst;
        private DevExpress.XtraGrid.Columns.GridColumn colAttLast;
        private DevExpress.XtraGrid.Columns.GridColumn colAttState;
    }
}
