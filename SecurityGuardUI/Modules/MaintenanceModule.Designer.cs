namespace SecurityGuardUI.Modules
{
    partial class MaintenanceModule
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
            if (disposing)
            {
                _poll.Dispose();
                if (components != null) components.Dispose();
            }
            base.Dispose(disposing);
        }

        private static DevExpress.XtraGrid.Columns.GridColumn Col(string field, string caption, int width, int index)
        {
            return new DevExpress.XtraGrid.Columns.GridColumn { FieldName = field, Caption = caption, Width = width, Visible = true, VisibleIndex = index, Name = "col" + field };
        }

        private static DevExpress.XtraEditors.CheckEdit Check(string text, int x, int y, int width)
        {
            var c = new DevExpress.XtraEditors.CheckEdit { Location = new System.Drawing.Point(x, y), Size = new System.Drawing.Size(width, 20) };
            c.Properties.Caption = text;
            return c;
        }

        private static DevExpress.XtraEditors.LabelControl Label(string text, int x, int y)
        {
            return new DevExpress.XtraEditors.LabelControl { Text = text, Location = new System.Drawing.Point(x, y) };
        }

        #region Component Designer generated code

        private void InitializeComponent()
        {
            this.panelTop = new DevExpress.XtraEditors.PanelControl();
            this.labelTitle = new DevExpress.XtraEditors.LabelControl();
            this.labelIntro = new DevExpress.XtraEditors.LabelControl();
            this.checkEnabled = Check("Bakım açık (seçilen gün ve saatte otomatik çalışır)", 12, 78, 420);
            this.checkDryRun = Check("Deneme modu: hiçbir şey uygulama, sadece ne yapılacağını kaydet", 12, 102, 520);
            this.checkMon = Check("Pzt", 70, 132, 54);
            this.checkTue = Check("Sal", 126, 132, 54);
            this.checkWed = Check("Çar", 182, 132, 54);
            this.checkThu = Check("Per", 238, 132, 54);
            this.checkFri = Check("Cum", 294, 132, 54);
            this.checkSat = Check("Cmt", 350, 132, 54);
            this.checkSun = Check("Paz", 406, 132, 54);
            this.timeStart = new DevExpress.XtraEditors.TimeEdit();
            this.spinMax = new DevExpress.XtraEditors.SpinEdit();
            this.checkIndexes = Check("Index bakımı (REBUILD / REORGANIZE)", 12, 160, 260);
            this.checkStats = Check("İstatistik güncelleme", 280, 160, 180);
            this.checkShrink = Check("Şişmiş log dosyasını küçült (veri dosyasına dokunulmaz)", 470, 160, 340);
            this.labelThresholds = new DevExpress.XtraEditors.LabelControl();
            this.labelNotes = new DevExpress.XtraEditors.LabelControl();
            this.buttonSave = new DevExpress.XtraEditors.SimpleButton();
            this.buttonRefresh = new DevExpress.XtraEditors.SimpleButton();
            this.buttonDryRun = new DevExpress.XtraEditors.SimpleButton();
            this.buttonRunNow = new DevExpress.XtraEditors.SimpleButton();
            this.splitMain = new DevExpress.XtraEditors.SplitContainerControl();
            this.labelDbs = new DevExpress.XtraEditors.LabelControl();
            this.gridDbs = new DevExpress.XtraGrid.GridControl();
            this.viewDbs = new DevExpress.XtraGrid.Views.Grid.GridView();
            this.colDbSelected = Col("Selected", "Seç", 36, 0);
            this.splitRuns = new DevExpress.XtraEditors.SplitContainerControl();
            this.panelRunsBar = new DevExpress.XtraEditors.PanelControl();
            this.labelRuns = new DevExpress.XtraEditors.LabelControl();
            this.buttonExportOps = new DevExpress.XtraEditors.SimpleButton();
            this.gridRuns = new DevExpress.XtraGrid.GridControl();
            this.viewRuns = new DevExpress.XtraGrid.Views.Grid.GridView();
            this.colRunStatus = Col("Status", "Durum", 90, 3);
            this.gridOps = new DevExpress.XtraGrid.GridControl();
            this.viewOps = new DevExpress.XtraGrid.Views.Grid.GridView();
            this.colOpResult = Col("Result", "Sonuç", 100, 7);
            this.labelStatus = new DevExpress.XtraEditors.LabelControl();
            ((System.ComponentModel.ISupportInitialize)(this.panelTop)).BeginInit();
            this.panelTop.SuspendLayout();
            ((System.ComponentModel.ISupportInitialize)(this.splitMain)).BeginInit();
            this.splitMain.SuspendLayout();
            ((System.ComponentModel.ISupportInitialize)(this.gridDbs)).BeginInit();
            ((System.ComponentModel.ISupportInitialize)(this.viewDbs)).BeginInit();
            ((System.ComponentModel.ISupportInitialize)(this.splitRuns)).BeginInit();
            this.splitRuns.SuspendLayout();
            ((System.ComponentModel.ISupportInitialize)(this.gridRuns)).BeginInit();
            ((System.ComponentModel.ISupportInitialize)(this.viewRuns)).BeginInit();
            ((System.ComponentModel.ISupportInitialize)(this.gridOps)).BeginInit();
            ((System.ComponentModel.ISupportInitialize)(this.viewOps)).BeginInit();
            this.SuspendLayout();
            //
            // panelTop
            //
            this.panelTop.BorderStyle = DevExpress.XtraEditors.Controls.BorderStyles.NoBorder;
            this.panelTop.Dock = System.Windows.Forms.DockStyle.Top;
            this.panelTop.Size = new System.Drawing.Size(1000, 250);
            this.panelTop.Controls.AddRange(new System.Windows.Forms.Control[] {
                this.labelTitle, this.labelIntro, this.checkEnabled, this.checkDryRun, Label("Günler", 12, 135),
                this.checkMon, this.checkTue, this.checkWed, this.checkThu, this.checkFri, this.checkSat, this.checkSun,
                Label("Başlangıç", 476, 135), this.timeStart, Label("En fazla", 616, 135), this.spinMax, Label("dakika", 728, 135),
                this.checkIndexes, this.checkStats, this.checkShrink, this.labelThresholds, this.labelNotes,
                this.buttonSave, this.buttonRefresh, this.buttonDryRun, this.buttonRunNow });
            //
            // labelTitle
            //
            this.labelTitle.Appearance.Font = new System.Drawing.Font("Segoe UI", 14F);
            this.labelTitle.Appearance.Options.UseFont = true;
            this.labelTitle.Location = new System.Drawing.Point(12, 8);
            this.labelTitle.Text = "SQL Bakımı";
            //
            // labelIntro
            //
            this.labelIntro.Anchor = ((System.Windows.Forms.AnchorStyles)(((System.Windows.Forms.AnchorStyles.Top | System.Windows.Forms.AnchorStyles.Left) | System.Windows.Forms.AnchorStyles.Right)));
            this.labelIntro.Appearance.Options.UseTextOptions = true;
            this.labelIntro.Appearance.TextOptions.WordWrap = DevExpress.Utils.WordWrap.Wrap;
            this.labelIntro.AutoSizeMode = DevExpress.XtraEditors.LabelAutoSizeMode.None;
            this.labelIntro.Location = new System.Drawing.Point(12, 40);
            this.labelIntro.Size = new System.Drawing.Size(700, 32);
            this.labelIntro.Text = "Seçilen veritabanlarında bakım penceresinde index bakımı ve istatistik güncellemesi yapar. Varsayılan kapalı. "
                + "SecurityGuard'ın SQL Server'da değişiklik yaptığı tek özelliktir: açmadan önce \"Deneme Yap\" ile ne yapılacağına bakın.";
            //
            // timeStart
            //
            this.timeStart.Location = new System.Drawing.Point(532, 132);
            this.timeStart.Properties.Mask.EditMask = "HH:mm";
            this.timeStart.Properties.Mask.UseMaskAsDisplayFormat = true;
            this.timeStart.Properties.DisplayFormat.FormatString = "HH:mm";
            this.timeStart.Properties.EditFormat.FormatString = "HH:mm";
            this.timeStart.Size = new System.Drawing.Size(74, 20);
            //
            // spinMax
            //
            this.spinMax.Location = new System.Drawing.Point(664, 132);
            this.spinMax.Properties.IsFloatValue = false;
            this.spinMax.Properties.Mask.EditMask = "N0";
            this.spinMax.Properties.MinValue = 15;
            this.spinMax.Properties.MaxValue = 720;
            this.spinMax.Properties.Increment = 15;
            this.spinMax.Size = new System.Drawing.Size(60, 20);
            this.spinMax.ToolTip = "Süre dolunca yeni işleme başlanmaz; o an süren işlem iptal edilir (SQL Server geri alır).";
            //
            // labelThresholds
            //
            this.labelThresholds.Anchor = ((System.Windows.Forms.AnchorStyles)(((System.Windows.Forms.AnchorStyles.Top | System.Windows.Forms.AnchorStyles.Left) | System.Windows.Forms.AnchorStyles.Right)));
            this.labelThresholds.AutoEllipsis = true;
            this.labelThresholds.AutoSizeMode = DevExpress.XtraEditors.LabelAutoSizeMode.None;
            this.labelThresholds.Location = new System.Drawing.Point(12, 190);
            this.labelThresholds.Size = new System.Drawing.Size(976, 16);
            //
            // labelNotes
            //
            this.labelNotes.Anchor = ((System.Windows.Forms.AnchorStyles)(((System.Windows.Forms.AnchorStyles.Top | System.Windows.Forms.AnchorStyles.Left) | System.Windows.Forms.AnchorStyles.Right)));
            this.labelNotes.Appearance.Options.UseForeColor = true;
            this.labelNotes.Appearance.Options.UseTextOptions = true;
            this.labelNotes.Appearance.TextOptions.WordWrap = DevExpress.Utils.WordWrap.Wrap;
            this.labelNotes.Appearance.TextOptions.VAlignment = DevExpress.Utils.VertAlignment.Top;
            this.labelNotes.AutoSizeMode = DevExpress.XtraEditors.LabelAutoSizeMode.None;
            this.labelNotes.Location = new System.Drawing.Point(12, 210);
            this.labelNotes.Size = new System.Drawing.Size(976, 36);
            //
            // buttons
            //
            this.buttonDryRun.Anchor = ((System.Windows.Forms.AnchorStyles)((System.Windows.Forms.AnchorStyles.Top | System.Windows.Forms.AnchorStyles.Right)));
            this.buttonDryRun.Location = new System.Drawing.Point(732, 10);
            this.buttonDryRun.Size = new System.Drawing.Size(140, 26);
            this.buttonDryRun.Text = "Deneme Yap";
            this.buttonDryRun.ToolTip = "Şimdi ne yapılacağını listeler; SQL Server'da hiçbir şey değiştirmez (sadece okur).";
            this.buttonDryRun.Click += new System.EventHandler(this.buttonDryRun_Click);
            this.buttonRunNow.Anchor = ((System.Windows.Forms.AnchorStyles)((System.Windows.Forms.AnchorStyles.Top | System.Windows.Forms.AnchorStyles.Right)));
            this.buttonRunNow.Location = new System.Drawing.Point(732, 42);
            this.buttonRunNow.Size = new System.Drawing.Size(140, 26);
            this.buttonRunNow.Text = "Şimdi Uygula...";
            this.buttonRunNow.ToolTip = "Bakım penceresini beklemeden bakımı uygular (onay ister).";
            this.buttonRunNow.Click += new System.EventHandler(this.buttonRunNow_Click);
            this.buttonSave.Anchor = ((System.Windows.Forms.AnchorStyles)((System.Windows.Forms.AnchorStyles.Top | System.Windows.Forms.AnchorStyles.Right)));
            this.buttonSave.Location = new System.Drawing.Point(878, 10);
            this.buttonSave.Size = new System.Drawing.Size(110, 26);
            this.buttonSave.Text = "Kaydet";
            this.buttonSave.Click += new System.EventHandler(this.buttonSave_Click);
            this.buttonRefresh.Anchor = ((System.Windows.Forms.AnchorStyles)((System.Windows.Forms.AnchorStyles.Top | System.Windows.Forms.AnchorStyles.Right)));
            this.buttonRefresh.Location = new System.Drawing.Point(878, 42);
            this.buttonRefresh.Size = new System.Drawing.Size(110, 26);
            this.buttonRefresh.Text = "Yenile";
            this.buttonRefresh.Click += new System.EventHandler(this.buttonRefresh_Click);
            //
            // splitMain: solda DB'ler, sağda çalıştırmalar
            //
            this.splitMain.Dock = System.Windows.Forms.DockStyle.Fill;
            this.splitMain.Panel1.Controls.Add(this.gridDbs);
            this.splitMain.Panel1.Controls.Add(this.labelDbs);
            this.splitMain.Panel2.Controls.Add(this.splitRuns);
            this.splitMain.SplitterPosition = 400;
            this.labelDbs.Dock = System.Windows.Forms.DockStyle.Top;
            this.labelDbs.AutoSizeMode = DevExpress.XtraEditors.LabelAutoSizeMode.None;
            this.labelDbs.Padding = new System.Windows.Forms.Padding(4, 6, 0, 0);
            this.labelDbs.Size = new System.Drawing.Size(400, 28);
            this.labelDbs.Text = "Bakım yapılacak veritabanları (sistem DB'leri hariç)";
            this.gridDbs.Dock = System.Windows.Forms.DockStyle.Fill;
            this.gridDbs.MainView = this.viewDbs;
            this.gridDbs.ViewCollection.AddRange(new DevExpress.XtraGrid.Views.Base.BaseView[] { this.viewDbs });
            this.viewDbs.GridControl = this.gridDbs;
            this.viewDbs.Columns.AddRange(new DevExpress.XtraGrid.Columns.GridColumn[] {
                this.colDbSelected, Col("Instance", "Instance", 90, 1), Col("Name", "Veritabanı", 140, 2), Col("Recovery", "Recovery", 70, 3), Col("Size", "Boyut", 120, 4) });
            //
            // splitRuns: üstte çalıştırmalar, altta seçilenin işlemleri
            //
            this.splitRuns.Dock = System.Windows.Forms.DockStyle.Fill;
            this.splitRuns.Horizontal = false;
            this.splitRuns.Panel1.Controls.Add(this.gridRuns);
            this.splitRuns.Panel1.Controls.Add(this.panelRunsBar);
            this.splitRuns.Panel2.Controls.Add(this.gridOps);
            this.splitRuns.SplitterPosition = 170;
            this.panelRunsBar.BorderStyle = DevExpress.XtraEditors.Controls.BorderStyles.NoBorder;
            this.panelRunsBar.Dock = System.Windows.Forms.DockStyle.Top;
            this.panelRunsBar.Size = new System.Drawing.Size(600, 30);
            this.panelRunsBar.Controls.Add(this.labelRuns);
            this.panelRunsBar.Controls.Add(this.buttonExportOps);
            this.labelRuns.Location = new System.Drawing.Point(4, 8);
            this.labelRuns.Text = "Son çalıştırmalar (seçilenin işlemleri altta)";
            this.buttonExportOps.Anchor = ((System.Windows.Forms.AnchorStyles)((System.Windows.Forms.AnchorStyles.Top | System.Windows.Forms.AnchorStyles.Right)));
            this.buttonExportOps.Location = new System.Drawing.Point(470, 2);
            this.buttonExportOps.Size = new System.Drawing.Size(126, 26);
            this.buttonExportOps.Text = "İşlemleri Excel'e Aktar";
            this.buttonExportOps.Click += new System.EventHandler(this.buttonExportOps_Click);
            this.gridRuns.Dock = System.Windows.Forms.DockStyle.Fill;
            this.gridRuns.MainView = this.viewRuns;
            this.gridRuns.ViewCollection.AddRange(new DevExpress.XtraGrid.Views.Base.BaseView[] { this.viewRuns });
            this.viewRuns.GridControl = this.gridRuns;
            var colRunTime = Col("Time", "Başlangıç", 105, 0);
            colRunTime.DisplayFormat.FormatType = DevExpress.Utils.FormatType.DateTime;
            colRunTime.DisplayFormat.FormatString = "dd.MM.yyyy HH:mm";
            this.viewRuns.Columns.AddRange(new DevExpress.XtraGrid.Columns.GridColumn[] {
                colRunTime, Col("Instance", "Instance", 80, 1), Col("Mode", "Tür", 65, 2), this.colRunStatus, Col("Duration", "Süre", 45, 4),
                Col("Summary", "Özet", 380, 5), Col("Trigger", "Tetikleyen", 140, 6) });
            this.viewRuns.FocusedRowChanged += new DevExpress.XtraGrid.Views.Base.FocusedRowChangedEventHandler(this.viewRuns_FocusedRowChanged);
            this.viewRuns.RowCellStyle += new DevExpress.XtraGrid.Views.Grid.RowCellStyleEventHandler(this.viewRuns_RowCellStyle);
            this.gridOps.Dock = System.Windows.Forms.DockStyle.Fill;
            this.gridOps.MainView = this.viewOps;
            this.gridOps.ViewCollection.AddRange(new DevExpress.XtraGrid.Views.Base.BaseView[] { this.viewOps });
            this.viewOps.GridControl = this.gridOps;
            var colOpTime = Col("Time", "Saat", 60, 0);
            colOpTime.DisplayFormat.FormatType = DevExpress.Utils.FormatType.DateTime;
            colOpTime.DisplayFormat.FormatString = "HH:mm:ss";
            this.viewOps.Columns.AddRange(new DevExpress.XtraGrid.Columns.GridColumn[] {
                colOpTime, Col("Database", "Veritabanı", 100, 1), Col("Object", "Nesne", 220, 2), Col("Operation", "İşlem", 110, 3),
                Col("Before", "Önce", 110, 4), Col("After", "Sonra", 110, 5), Col("Duration", "Süre", 55, 6), this.colOpResult, Col("Note", "Not", 260, 8) });
            this.viewOps.RowCellStyle += new DevExpress.XtraGrid.Views.Grid.RowCellStyleEventHandler(this.viewOps_RowCellStyle);
            //
            // labelStatus
            //
            this.labelStatus.Appearance.Options.UseForeColor = true;
            this.labelStatus.AutoSizeMode = DevExpress.XtraEditors.LabelAutoSizeMode.None;
            this.labelStatus.Dock = System.Windows.Forms.DockStyle.Bottom;
            this.labelStatus.Padding = new System.Windows.Forms.Padding(6, 6, 0, 0);
            this.labelStatus.Size = new System.Drawing.Size(1000, 26);
            //
            // MaintenanceModule
            //
            this.AutoScaleDimensions = new System.Drawing.SizeF(6F, 13F);
            this.AutoScaleMode = System.Windows.Forms.AutoScaleMode.Font;
            this.Controls.Add(this.splitMain);
            this.Controls.Add(this.labelStatus);
            this.Controls.Add(this.panelTop);
            this.Name = "MaintenanceModule";
            this.Size = new System.Drawing.Size(1000, 700);
            ((System.ComponentModel.ISupportInitialize)(this.panelTop)).EndInit();
            this.panelTop.ResumeLayout(false);
            this.panelTop.PerformLayout();
            ((System.ComponentModel.ISupportInitialize)(this.splitMain)).EndInit();
            this.splitMain.ResumeLayout(false);
            ((System.ComponentModel.ISupportInitialize)(this.gridDbs)).EndInit();
            ((System.ComponentModel.ISupportInitialize)(this.viewDbs)).EndInit();
            ((System.ComponentModel.ISupportInitialize)(this.splitRuns)).EndInit();
            this.splitRuns.ResumeLayout(false);
            ((System.ComponentModel.ISupportInitialize)(this.gridRuns)).EndInit();
            ((System.ComponentModel.ISupportInitialize)(this.viewRuns)).EndInit();
            ((System.ComponentModel.ISupportInitialize)(this.gridOps)).EndInit();
            ((System.ComponentModel.ISupportInitialize)(this.viewOps)).EndInit();
            this.ResumeLayout(false);
        }

        #endregion

        private DevExpress.XtraEditors.PanelControl panelTop;
        private DevExpress.XtraEditors.LabelControl labelTitle;
        private DevExpress.XtraEditors.LabelControl labelIntro;
        private DevExpress.XtraEditors.CheckEdit checkEnabled;
        private DevExpress.XtraEditors.CheckEdit checkDryRun;
        private DevExpress.XtraEditors.CheckEdit checkMon;
        private DevExpress.XtraEditors.CheckEdit checkTue;
        private DevExpress.XtraEditors.CheckEdit checkWed;
        private DevExpress.XtraEditors.CheckEdit checkThu;
        private DevExpress.XtraEditors.CheckEdit checkFri;
        private DevExpress.XtraEditors.CheckEdit checkSat;
        private DevExpress.XtraEditors.CheckEdit checkSun;
        private DevExpress.XtraEditors.TimeEdit timeStart;
        private DevExpress.XtraEditors.SpinEdit spinMax;
        private DevExpress.XtraEditors.CheckEdit checkIndexes;
        private DevExpress.XtraEditors.CheckEdit checkStats;
        private DevExpress.XtraEditors.CheckEdit checkShrink;
        private DevExpress.XtraEditors.LabelControl labelThresholds;
        private DevExpress.XtraEditors.LabelControl labelNotes;
        private DevExpress.XtraEditors.SimpleButton buttonSave;
        private DevExpress.XtraEditors.SimpleButton buttonRefresh;
        private DevExpress.XtraEditors.SimpleButton buttonDryRun;
        private DevExpress.XtraEditors.SimpleButton buttonRunNow;
        private DevExpress.XtraEditors.SplitContainerControl splitMain;
        private DevExpress.XtraEditors.LabelControl labelDbs;
        private DevExpress.XtraGrid.GridControl gridDbs;
        private DevExpress.XtraGrid.Views.Grid.GridView viewDbs;
        private DevExpress.XtraGrid.Columns.GridColumn colDbSelected;
        private DevExpress.XtraEditors.SplitContainerControl splitRuns;
        private DevExpress.XtraEditors.PanelControl panelRunsBar;
        private DevExpress.XtraEditors.LabelControl labelRuns;
        private DevExpress.XtraEditors.SimpleButton buttonExportOps;
        private DevExpress.XtraGrid.GridControl gridRuns;
        private DevExpress.XtraGrid.Views.Grid.GridView viewRuns;
        private DevExpress.XtraGrid.Columns.GridColumn colRunStatus;
        private DevExpress.XtraGrid.GridControl gridOps;
        private DevExpress.XtraGrid.Views.Grid.GridView viewOps;
        private DevExpress.XtraGrid.Columns.GridColumn colOpResult;
        private DevExpress.XtraEditors.LabelControl labelStatus;
    }
}
