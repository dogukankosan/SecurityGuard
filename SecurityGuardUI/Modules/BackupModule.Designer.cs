namespace SecurityGuardUI.Modules
{
    partial class BackupModule
    {
        private System.ComponentModel.IContainer components = null;

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

        private static DevExpress.XtraGrid.Columns.GridColumn DateCol(string field, string caption, int width, int index, string format)
        {
            var c = Col(field, caption, width, index);
            c.DisplayFormat.FormatType = DevExpress.Utils.FormatType.DateTime;
            c.DisplayFormat.FormatString = format;
            return c;
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

        private static DevExpress.XtraEditors.LabelControl WideLabel(int x, int y, int height)
        {
            var l = new DevExpress.XtraEditors.LabelControl
            {
                Anchor = System.Windows.Forms.AnchorStyles.Top | System.Windows.Forms.AnchorStyles.Left | System.Windows.Forms.AnchorStyles.Right,
                AutoSizeMode = DevExpress.XtraEditors.LabelAutoSizeMode.None,
                Location = new System.Drawing.Point(x, y),
                Size = new System.Drawing.Size(976, height)
            };
            l.Appearance.Options.UseForeColor = true;
            l.Appearance.Options.UseTextOptions = true;
            l.Appearance.TextOptions.WordWrap = DevExpress.Utils.WordWrap.Wrap;
            l.Appearance.TextOptions.VAlignment = DevExpress.Utils.VertAlignment.Top;
            return l;
        }

        private static DevExpress.XtraEditors.SimpleButton Button(string text, int x, int y, int width, bool right)
        {
            var b = new DevExpress.XtraEditors.SimpleButton { Text = text, Location = new System.Drawing.Point(x, y), Size = new System.Drawing.Size(width, 26) };
            if (right) b.Anchor = System.Windows.Forms.AnchorStyles.Top | System.Windows.Forms.AnchorStyles.Right;
            return b;
        }

        #region Component Designer generated code

        private void InitializeComponent()
        {
            this.panelTop = new DevExpress.XtraEditors.PanelControl();
            this.labelTitle = new DevExpress.XtraEditors.LabelControl();
            this.labelIntro = WideLabel(12, 40, 32);
            this.checkEnabled = Check("Yedekleme açık (seçilen gün ve saatte otomatik çalışır)", 12, 78, 420);
            this.checkMon = Check("Pzt", 70, 106, 54);
            this.checkTue = Check("Sal", 126, 106, 54);
            this.checkWed = Check("Çar", 182, 106, 54);
            this.checkThu = Check("Per", 238, 106, 54);
            this.checkFri = Check("Cum", 294, 106, 54);
            this.checkSat = Check("Cmt", 350, 106, 54);
            this.checkSun = Check("Paz", 406, 106, 54);
            this.timeStart = new DevExpress.XtraEditors.TimeEdit();
            this.textFolder = new DevExpress.XtraEditors.ButtonEdit();
            this.spinRetention = new DevExpress.XtraEditors.SpinEdit();
            this.comboCopyOnly = new DevExpress.XtraEditors.ImageComboBoxEdit();
            this.textRar = new DevExpress.XtraEditors.TextEdit();
            this.labelRar = WideLabel(12, 196, 16);
            this.labelPassword = Label("", 12, 222);
            this.buttonSetPassword = Button("Şifre Belirle...", 240, 216, 140, false);
            this.buttonShowPassword = Button("Şifreyi Göster...", 386, 216, 140, false);
            this.labelNotes = WideLabel(12, 250, 48);
            this.buttonRunNow = Button("Şimdi Yedekle...", 732, 10, 140, true);
            this.buttonSave = Button("Kaydet", 878, 10, 110, true);
            this.buttonRefresh = Button("Yenile", 878, 42, 110, true);
            this.splitMain = new DevExpress.XtraEditors.SplitContainerControl();
            this.labelDbs = new DevExpress.XtraEditors.LabelControl();
            this.gridDbs = new DevExpress.XtraGrid.GridControl();
            this.viewDbs = new DevExpress.XtraGrid.Views.Grid.GridView();
            this.colDbSelected = Col("Selected", "Seç", 34, 0);
            this.colDbLastAny = DateCol("LastAny", "Son tam yedek", 105, 5, "dd.MM.yyyy HH:mm");
            this.splitRuns = new DevExpress.XtraEditors.SplitContainerControl();
            this.labelRuns = new DevExpress.XtraEditors.LabelControl();
            this.gridRuns = new DevExpress.XtraGrid.GridControl();
            this.viewRuns = new DevExpress.XtraGrid.Views.Grid.GridView();
            this.colRunStatus = Col("Status", "Durum", 85, 2);
            this.gridFiles = new DevExpress.XtraGrid.GridControl();
            this.viewFiles = new DevExpress.XtraGrid.Views.Grid.GridView();
            this.colFileResult = Col("Result", "Sonuç", 70, 3);
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
            ((System.ComponentModel.ISupportInitialize)(this.gridFiles)).BeginInit();
            ((System.ComponentModel.ISupportInitialize)(this.viewFiles)).BeginInit();
            this.SuspendLayout();
            //
            // panelTop
            //
            this.panelTop.BorderStyle = DevExpress.XtraEditors.Controls.BorderStyles.NoBorder;
            this.panelTop.Dock = System.Windows.Forms.DockStyle.Top;
            this.panelTop.Size = new System.Drawing.Size(1000, 302);
            this.panelTop.Controls.AddRange(new System.Windows.Forms.Control[] {
                this.labelTitle, this.labelIntro, this.checkEnabled, Label("Günler", 12, 109),
                this.checkMon, this.checkTue, this.checkWed, this.checkThu, this.checkFri, this.checkSat, this.checkSun,
                Label("Saat", 476, 109), this.timeStart,
                Label("Klasör", 12, 141), this.textFolder, Label("Saklama", 512, 141), this.spinRetention, Label("gün (her DB'nin son 3 yedeği hep kalır)", 640, 141),
                Label("COPY_ONLY", 12, 171), this.comboCopyOnly, Label("Rar.exe", 400, 171), this.textRar,
                this.labelRar, this.labelPassword, this.buttonSetPassword, this.buttonShowPassword, this.labelNotes,
                this.buttonRunNow, this.buttonSave, this.buttonRefresh });
            this.labelTitle.Appearance.Font = new System.Drawing.Font("Segoe UI", 14F);
            this.labelTitle.Appearance.Options.UseFont = true;
            this.labelTitle.Location = new System.Drawing.Point(12, 8);
            this.labelTitle.Text = "SQL Yedekleme";
            this.labelIntro.Size = new System.Drawing.Size(700, 32);
            this.labelIntro.Text = "Seçilen veritabanları seçilen gün ve saatte tam yedeklenir, RESTORE VERIFYONLY ile doğrulanır ve WinRAR ile şifreli arşivlenir "
                + "(RAR5, AES-256). Şifresiz .bak dosyası hiç bırakılmaz. Başarısız yedekte Kritik mail gider. Varsayılan kapalı.";
            //
            // timeStart
            //
            this.timeStart.Location = new System.Drawing.Point(512, 106);
            this.timeStart.Properties.Mask.EditMask = "HH:mm";
            this.timeStart.Properties.Mask.UseMaskAsDisplayFormat = true;
            this.timeStart.Properties.DisplayFormat.FormatString = "HH:mm";
            this.timeStart.Properties.EditFormat.FormatString = "HH:mm";
            this.timeStart.Size = new System.Drawing.Size(74, 20);
            //
            // textFolder
            //
            this.textFolder.Location = new System.Drawing.Point(70, 138);
            this.textFolder.Properties.NullValuePrompt = "ör. D:\\SQLYedek (Windows'tan ayrı bir disk önerilir)";
            this.textFolder.Size = new System.Drawing.Size(430, 20);
            this.textFolder.ButtonClick += new DevExpress.XtraEditors.Controls.ButtonPressedEventHandler(this.textFolder_ButtonClick);
            //
            // spinRetention
            //
            this.spinRetention.Location = new System.Drawing.Point(568, 138);
            this.spinRetention.Properties.IsFloatValue = false;
            this.spinRetention.Properties.Mask.EditMask = "N0";
            this.spinRetention.Properties.MinValue = 1;
            this.spinRetention.Properties.MaxValue = 3650;
            this.spinRetention.Size = new System.Drawing.Size(64, 20);
            //
            // comboCopyOnly
            //
            this.comboCopyOnly.Location = new System.Drawing.Point(80, 168);
            this.comboCopyOnly.Properties.Buttons.AddRange(new DevExpress.XtraEditors.Controls.EditorButton[] {
                new DevExpress.XtraEditors.Controls.EditorButton(DevExpress.XtraEditors.Controls.ButtonPredefines.Combo)});
            this.comboCopyOnly.Size = new System.Drawing.Size(300, 20);
            this.comboCopyOnly.ToolTip = "COPY_ONLY yedek, sunucudaki başka yedekleme aracının yedek zincirini (fark / log yedekleri) bozmaz.";
            //
            // textRar
            //
            this.textRar.Location = new System.Drawing.Point(446, 168);
            this.textRar.Properties.NullValuePrompt = "boş: otomatik bulunur";
            this.textRar.Size = new System.Drawing.Size(300, 20);
            //
            // buttons
            //
            this.buttonRunNow.Click += new System.EventHandler(this.buttonRunNow_Click);
            this.buttonSave.Click += new System.EventHandler(this.buttonSave_Click);
            this.buttonRefresh.Click += new System.EventHandler(this.buttonRefresh_Click);
            this.buttonSetPassword.Click += new System.EventHandler(this.buttonSetPassword_Click);
            this.buttonShowPassword.Click += new System.EventHandler(this.buttonShowPassword_Click);
            this.buttonSetPassword.ToolTip = "Arşiv şifresini belirler veya güçlü bir şifre üretir (yönetici şifresi sorulur).";
            this.buttonShowPassword.ToolTip = "Arşiv şifresini gösterir (yönetici şifresi sorulur, İşlem Kaydı'na yazılır).";
            //
            // splitMain: solda DB'ler, sağda yedeklemeler
            //
            this.splitMain.Dock = System.Windows.Forms.DockStyle.Fill;
            this.splitMain.Panel1.Controls.Add(this.gridDbs);
            this.splitMain.Panel1.Controls.Add(this.labelDbs);
            this.splitMain.Panel2.Controls.Add(this.splitRuns);
            this.splitMain.SplitterPosition = 470;
            this.labelDbs.Dock = System.Windows.Forms.DockStyle.Top;
            this.labelDbs.AutoSizeMode = DevExpress.XtraEditors.LabelAutoSizeMode.None;
            this.labelDbs.Padding = new System.Windows.Forms.Padding(4, 6, 0, 0);
            this.labelDbs.Size = new System.Drawing.Size(470, 28);
            this.labelDbs.Text = "Yedeklenecek veritabanları (son tam yedek: herhangi bir araçla; kırmızı = 7 günden eski / hiç)";
            this.gridDbs.Dock = System.Windows.Forms.DockStyle.Fill;
            this.gridDbs.MainView = this.viewDbs;
            this.gridDbs.ViewCollection.AddRange(new DevExpress.XtraGrid.Views.Base.BaseView[] { this.viewDbs });
            this.viewDbs.GridControl = this.gridDbs;
            this.viewDbs.Columns.AddRange(new DevExpress.XtraGrid.Columns.GridColumn[] {
                this.colDbSelected, Col("Instance", "Instance", 85, 1), Col("Name", "Veritabanı", 120, 2), Col("Recovery", "Recovery", 60, 3), Col("Size", "Boyut", 60, 4),
                this.colDbLastAny, DateCol("LastGuard", "SecurityGuard yedeği", 105, 6, "dd.MM.yyyy HH:mm") });
            this.viewDbs.RowCellStyle += new DevExpress.XtraGrid.Views.Grid.RowCellStyleEventHandler(this.viewDbs_RowCellStyle);
            //
            // splitRuns
            //
            this.splitRuns.Dock = System.Windows.Forms.DockStyle.Fill;
            this.splitRuns.Horizontal = false;
            this.splitRuns.Panel1.Controls.Add(this.gridRuns);
            this.splitRuns.Panel1.Controls.Add(this.labelRuns);
            this.splitRuns.Panel2.Controls.Add(this.gridFiles);
            this.splitRuns.SplitterPosition = 150;
            this.labelRuns.Dock = System.Windows.Forms.DockStyle.Top;
            this.labelRuns.AutoSizeMode = DevExpress.XtraEditors.LabelAutoSizeMode.None;
            this.labelRuns.Padding = new System.Windows.Forms.Padding(4, 6, 0, 0);
            this.labelRuns.Size = new System.Drawing.Size(500, 28);
            this.labelRuns.Text = "Son yedeklemeler (seçilenin adımları altta)";
            this.gridRuns.Dock = System.Windows.Forms.DockStyle.Fill;
            this.gridRuns.MainView = this.viewRuns;
            this.gridRuns.ViewCollection.AddRange(new DevExpress.XtraGrid.Views.Base.BaseView[] { this.viewRuns });
            this.viewRuns.GridControl = this.gridRuns;
            this.viewRuns.Columns.AddRange(new DevExpress.XtraGrid.Columns.GridColumn[] {
                DateCol("Time", "Başlangıç", 105, 0, "dd.MM.yyyy HH:mm"), Col("Instance", "Instance", 80, 1), this.colRunStatus, Col("Duration", "Süre", 45, 3),
                Col("Summary", "Özet", 300, 4), Col("Trigger", "Tetikleyen", 120, 5) });
            this.viewRuns.FocusedRowChanged += new DevExpress.XtraGrid.Views.Base.FocusedRowChangedEventHandler(this.viewRuns_FocusedRowChanged);
            this.viewRuns.RowCellStyle += new DevExpress.XtraGrid.Views.Grid.RowCellStyleEventHandler(this.viewRuns_RowCellStyle);
            this.gridFiles.Dock = System.Windows.Forms.DockStyle.Fill;
            this.gridFiles.MainView = this.viewFiles;
            this.gridFiles.ViewCollection.AddRange(new DevExpress.XtraGrid.Views.Base.BaseView[] { this.viewFiles });
            this.viewFiles.GridControl = this.gridFiles;
            this.viewFiles.Columns.AddRange(new DevExpress.XtraGrid.Columns.GridColumn[] {
                DateCol("Time", "Saat", 60, 0, "HH:mm:ss"), Col("Database", "Veritabanı", 100, 1), Col("Step", "Adım", 80, 2), this.colFileResult,
                Col("Size", "Boyut", 60, 4), Col("Duration", "Süre", 55, 5), Col("File", "Dosya", 220, 6), Col("Note", "Not", 220, 7) });
            this.viewFiles.RowCellStyle += new DevExpress.XtraGrid.Views.Grid.RowCellStyleEventHandler(this.viewFiles_RowCellStyle);
            //
            // labelStatus
            //
            this.labelStatus.Appearance.Options.UseForeColor = true;
            this.labelStatus.AutoEllipsis = true;
            this.labelStatus.AutoSizeMode = DevExpress.XtraEditors.LabelAutoSizeMode.None;
            this.labelStatus.Dock = System.Windows.Forms.DockStyle.Bottom;
            this.labelStatus.Padding = new System.Windows.Forms.Padding(6, 6, 0, 0);
            this.labelStatus.Size = new System.Drawing.Size(1000, 26);
            //
            // BackupModule
            //
            this.AutoScaleDimensions = new System.Drawing.SizeF(6F, 13F);
            this.AutoScaleMode = System.Windows.Forms.AutoScaleMode.Font;
            this.Controls.Add(this.splitMain);
            this.Controls.Add(this.labelStatus);
            this.Controls.Add(this.panelTop);
            this.Name = "BackupModule";
            this.Size = new System.Drawing.Size(1000, 760);
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
            ((System.ComponentModel.ISupportInitialize)(this.gridFiles)).EndInit();
            ((System.ComponentModel.ISupportInitialize)(this.viewFiles)).EndInit();
            this.ResumeLayout(false);
        }

        #endregion

        private DevExpress.XtraEditors.PanelControl panelTop;
        private DevExpress.XtraEditors.LabelControl labelTitle;
        private DevExpress.XtraEditors.LabelControl labelIntro;
        private DevExpress.XtraEditors.CheckEdit checkEnabled;
        private DevExpress.XtraEditors.CheckEdit checkMon;
        private DevExpress.XtraEditors.CheckEdit checkTue;
        private DevExpress.XtraEditors.CheckEdit checkWed;
        private DevExpress.XtraEditors.CheckEdit checkThu;
        private DevExpress.XtraEditors.CheckEdit checkFri;
        private DevExpress.XtraEditors.CheckEdit checkSat;
        private DevExpress.XtraEditors.CheckEdit checkSun;
        private DevExpress.XtraEditors.TimeEdit timeStart;
        private DevExpress.XtraEditors.ButtonEdit textFolder;
        private DevExpress.XtraEditors.SpinEdit spinRetention;
        private DevExpress.XtraEditors.ImageComboBoxEdit comboCopyOnly;
        private DevExpress.XtraEditors.TextEdit textRar;
        private DevExpress.XtraEditors.LabelControl labelRar;
        private DevExpress.XtraEditors.LabelControl labelPassword;
        private DevExpress.XtraEditors.SimpleButton buttonSetPassword;
        private DevExpress.XtraEditors.SimpleButton buttonShowPassword;
        private DevExpress.XtraEditors.LabelControl labelNotes;
        private DevExpress.XtraEditors.SimpleButton buttonRunNow;
        private DevExpress.XtraEditors.SimpleButton buttonSave;
        private DevExpress.XtraEditors.SimpleButton buttonRefresh;
        private DevExpress.XtraEditors.SplitContainerControl splitMain;
        private DevExpress.XtraEditors.LabelControl labelDbs;
        private DevExpress.XtraGrid.GridControl gridDbs;
        private DevExpress.XtraGrid.Views.Grid.GridView viewDbs;
        private DevExpress.XtraGrid.Columns.GridColumn colDbSelected;
        private DevExpress.XtraGrid.Columns.GridColumn colDbLastAny;
        private DevExpress.XtraEditors.SplitContainerControl splitRuns;
        private DevExpress.XtraEditors.LabelControl labelRuns;
        private DevExpress.XtraGrid.GridControl gridRuns;
        private DevExpress.XtraGrid.Views.Grid.GridView viewRuns;
        private DevExpress.XtraGrid.Columns.GridColumn colRunStatus;
        private DevExpress.XtraGrid.GridControl gridFiles;
        private DevExpress.XtraGrid.Views.Grid.GridView viewFiles;
        private DevExpress.XtraGrid.Columns.GridColumn colFileResult;
        private DevExpress.XtraEditors.LabelControl labelStatus;
    }
}
