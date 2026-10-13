namespace SecurityGuardUI.Modules
{
    partial class ServiceModule
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
            this.labelTitle = new DevExpress.XtraEditors.LabelControl();
            this.labelInfo = new DevExpress.XtraEditors.LabelControl();
            this.captionState = new DevExpress.XtraEditors.LabelControl();
            this.valueState = new DevExpress.XtraEditors.LabelControl();
            this.captionVersion = new DevExpress.XtraEditors.LabelControl();
            this.valueVersion = new DevExpress.XtraEditors.LabelControl();
            this.captionStarted = new DevExpress.XtraEditors.LabelControl();
            this.valueStarted = new DevExpress.XtraEditors.LabelControl();
            this.captionPid = new DevExpress.XtraEditors.LabelControl();
            this.valuePid = new DevExpress.XtraEditors.LabelControl();
            this.captionDatabase = new DevExpress.XtraEditors.LabelControl();
            this.valueDatabase = new DevExpress.XtraEditors.LabelControl();
            this.captionDatabaseSize = new DevExpress.XtraEditors.LabelControl();
            this.valueDatabaseSize = new DevExpress.XtraEditors.LabelControl();
            this.captionLogs = new DevExpress.XtraEditors.LabelControl();
            this.valueLogs = new DevExpress.XtraEditors.LabelControl();
            this.captionMail = new DevExpress.XtraEditors.LabelControl();
            this.valueMail = new DevExpress.XtraEditors.LabelControl();
            this.buttonRefresh = new DevExpress.XtraEditors.SimpleButton();
            this.buttonRestart = new DevExpress.XtraEditors.SimpleButton();
            this.buttonStop = new DevExpress.XtraEditors.SimpleButton();
            this.buttonUninstall = new DevExpress.XtraEditors.SimpleButton();
            this.buttonOpenLogs = new DevExpress.XtraEditors.SimpleButton();
            this.labelWarning = new DevExpress.XtraEditors.LabelControl();
            this.SuspendLayout();
            //
            // labelTitle
            //
            this.labelTitle.Appearance.Font = new System.Drawing.Font("Segoe UI", 14F);
            this.labelTitle.Appearance.Options.UseFont = true;
            this.labelTitle.Location = new System.Drawing.Point(12, 8);
            this.labelTitle.Name = "labelTitle";
            this.labelTitle.Size = new System.Drawing.Size(135, 25);
            this.labelTitle.TabIndex = 0;
            this.labelTitle.Text = "Servis Yönetimi";
            //
            // labelInfo
            //
            this.labelInfo.Location = new System.Drawing.Point(12, 38);
            this.labelInfo.Name = "labelInfo";
            this.labelInfo.Size = new System.Drawing.Size(600, 13);
            this.labelInfo.TabIndex = 1;
            this.labelInfo.Text = "SecurityGuard servisi arka planda sürekli çalışır; bu program kapansa da koruma devam eder.";
            //
            // captionState
            //
            this.captionState.Location = new System.Drawing.Point(16, 78);
            this.captionState.Name = "captionState";
            this.captionState.Size = new System.Drawing.Size(30, 13);
            this.captionState.TabIndex = 2;
            this.captionState.Text = "Durum";
            //
            // valueState
            //
            this.valueState.Appearance.Font = new System.Drawing.Font("Segoe UI", 9F, System.Drawing.FontStyle.Bold);
            this.valueState.Appearance.Options.UseFont = true;
            this.valueState.AutoSizeMode = DevExpress.XtraEditors.LabelAutoSizeMode.None;
            this.valueState.Location = new System.Drawing.Point(180, 76);
            this.valueState.Name = "valueState";
            this.valueState.Size = new System.Drawing.Size(600, 17);
            this.valueState.TabIndex = 3;
            this.valueState.Text = "-";
            //
            // captionVersion
            //
            this.captionVersion.Location = new System.Drawing.Point(16, 104);
            this.captionVersion.Name = "captionVersion";
            this.captionVersion.Size = new System.Drawing.Size(30, 13);
            this.captionVersion.TabIndex = 4;
            this.captionVersion.Text = "Sürüm";
            //
            // valueVersion
            //
            this.valueVersion.AutoSizeMode = DevExpress.XtraEditors.LabelAutoSizeMode.None;
            this.valueVersion.Location = new System.Drawing.Point(180, 104);
            this.valueVersion.Name = "valueVersion";
            this.valueVersion.Size = new System.Drawing.Size(600, 13);
            this.valueVersion.TabIndex = 5;
            this.valueVersion.Text = "-";
            //
            // captionStarted
            //
            this.captionStarted.Location = new System.Drawing.Point(16, 130);
            this.captionStarted.Name = "captionStarted";
            this.captionStarted.Size = new System.Drawing.Size(51, 13);
            this.captionStarted.TabIndex = 6;
            this.captionStarted.Text = "Başlangıç";
            //
            // valueStarted
            //
            this.valueStarted.AutoSizeMode = DevExpress.XtraEditors.LabelAutoSizeMode.None;
            this.valueStarted.Location = new System.Drawing.Point(180, 130);
            this.valueStarted.Name = "valueStarted";
            this.valueStarted.Size = new System.Drawing.Size(600, 13);
            this.valueStarted.TabIndex = 7;
            this.valueStarted.Text = "-";
            //
            // captionPid
            //
            this.captionPid.Location = new System.Drawing.Point(16, 156);
            this.captionPid.Name = "captionPid";
            this.captionPid.Size = new System.Drawing.Size(76, 13);
            this.captionPid.TabIndex = 8;
            this.captionPid.Text = "İşlem no (PID)";
            //
            // valuePid
            //
            this.valuePid.AutoSizeMode = DevExpress.XtraEditors.LabelAutoSizeMode.None;
            this.valuePid.Location = new System.Drawing.Point(180, 156);
            this.valuePid.Name = "valuePid";
            this.valuePid.Size = new System.Drawing.Size(600, 13);
            this.valuePid.TabIndex = 9;
            this.valuePid.Text = "-";
            //
            // captionDatabase
            //
            this.captionDatabase.Location = new System.Drawing.Point(16, 182);
            this.captionDatabase.Name = "captionDatabase";
            this.captionDatabase.Size = new System.Drawing.Size(55, 13);
            this.captionDatabase.TabIndex = 10;
            this.captionDatabase.Text = "Veritabanı";
            //
            // valueDatabase
            //
            this.valueDatabase.AutoSizeMode = DevExpress.XtraEditors.LabelAutoSizeMode.None;
            this.valueDatabase.Location = new System.Drawing.Point(180, 182);
            this.valueDatabase.Name = "valueDatabase";
            this.valueDatabase.Size = new System.Drawing.Size(600, 13);
            this.valueDatabase.TabIndex = 11;
            this.valueDatabase.Text = "-";
            //
            // captionDatabaseSize
            //
            this.captionDatabaseSize.Location = new System.Drawing.Point(16, 208);
            this.captionDatabaseSize.Name = "captionDatabaseSize";
            this.captionDatabaseSize.Size = new System.Drawing.Size(90, 13);
            this.captionDatabaseSize.TabIndex = 12;
            this.captionDatabaseSize.Text = "Veritabanı boyutu";
            //
            // valueDatabaseSize
            //
            this.valueDatabaseSize.AutoSizeMode = DevExpress.XtraEditors.LabelAutoSizeMode.None;
            this.valueDatabaseSize.Location = new System.Drawing.Point(180, 208);
            this.valueDatabaseSize.Name = "valueDatabaseSize";
            this.valueDatabaseSize.Size = new System.Drawing.Size(600, 13);
            this.valueDatabaseSize.TabIndex = 13;
            this.valueDatabaseSize.Text = "-";
            //
            // captionLogs
            //
            this.captionLogs.Location = new System.Drawing.Point(16, 234);
            this.captionLogs.Name = "captionLogs";
            this.captionLogs.Size = new System.Drawing.Size(60, 13);
            this.captionLogs.TabIndex = 14;
            this.captionLogs.Text = "Log klasörü";
            //
            // valueLogs
            //
            this.valueLogs.AutoSizeMode = DevExpress.XtraEditors.LabelAutoSizeMode.None;
            this.valueLogs.Location = new System.Drawing.Point(180, 234);
            this.valueLogs.Name = "valueLogs";
            this.valueLogs.Size = new System.Drawing.Size(600, 13);
            this.valueLogs.TabIndex = 15;
            this.valueLogs.Text = "-";
            //
            // captionMail
            //
            this.captionMail.Location = new System.Drawing.Point(16, 260);
            this.captionMail.Name = "captionMail";
            this.captionMail.Size = new System.Drawing.Size(68, 13);
            this.captionMail.TabIndex = 16;
            this.captionMail.Text = "Bekleyen mail";
            //
            // valueMail
            //
            this.valueMail.AutoSizeMode = DevExpress.XtraEditors.LabelAutoSizeMode.None;
            this.valueMail.Location = new System.Drawing.Point(180, 260);
            this.valueMail.Name = "valueMail";
            this.valueMail.Size = new System.Drawing.Size(600, 13);
            this.valueMail.TabIndex = 17;
            this.valueMail.Text = "-";
            //
            // buttonRefresh
            //
            this.buttonRefresh.Location = new System.Drawing.Point(16, 300);
            this.buttonRefresh.Name = "buttonRefresh";
            this.buttonRefresh.Size = new System.Drawing.Size(90, 28);
            this.buttonRefresh.TabIndex = 18;
            this.buttonRefresh.Text = "Yenile";
            this.buttonRefresh.Click += new System.EventHandler(this.buttonRefresh_Click);
            //
            // buttonRestart
            //
            this.buttonRestart.Location = new System.Drawing.Point(112, 300);
            this.buttonRestart.Name = "buttonRestart";
            this.buttonRestart.Size = new System.Drawing.Size(120, 28);
            this.buttonRestart.TabIndex = 19;
            this.buttonRestart.Text = "Yeniden Başlat";
            this.buttonRestart.Click += new System.EventHandler(this.buttonRestart_Click);
            //
            // buttonStop
            //
            this.buttonStop.Location = new System.Drawing.Point(238, 300);
            this.buttonStop.Name = "buttonStop";
            this.buttonStop.Size = new System.Drawing.Size(90, 28);
            this.buttonStop.TabIndex = 20;
            this.buttonStop.Text = "Durdur";
            this.buttonStop.Click += new System.EventHandler(this.buttonStop_Click);
            //
            // buttonUninstall
            //
            this.buttonUninstall.Location = new System.Drawing.Point(334, 300);
            this.buttonUninstall.Name = "buttonUninstall";
            this.buttonUninstall.Size = new System.Drawing.Size(120, 28);
            this.buttonUninstall.TabIndex = 21;
            this.buttonUninstall.Text = "Servisi Kaldır";
            this.buttonUninstall.Click += new System.EventHandler(this.buttonUninstall_Click);
            //
            // buttonOpenLogs
            //
            this.buttonOpenLogs.Location = new System.Drawing.Point(474, 300);
            this.buttonOpenLogs.Name = "buttonOpenLogs";
            this.buttonOpenLogs.Size = new System.Drawing.Size(150, 28);
            this.buttonOpenLogs.TabIndex = 22;
            this.buttonOpenLogs.Text = "Log Klasörünü Aç";
            this.buttonOpenLogs.Click += new System.EventHandler(this.buttonOpenLogs_Click);
            //
            // labelWarning
            //
            this.labelWarning.Appearance.Options.UseForeColor = true;
            this.labelWarning.Appearance.Options.UseTextOptions = true;
            this.labelWarning.Appearance.TextOptions.WordWrap = DevExpress.Utils.WordWrap.Wrap;
            this.labelWarning.AutoSizeMode = DevExpress.XtraEditors.LabelAutoSizeMode.None;
            this.labelWarning.Location = new System.Drawing.Point(16, 344);
            this.labelWarning.Name = "labelWarning";
            this.labelWarning.Size = new System.Drawing.Size(700, 34);
            this.labelWarning.TabIndex = 23;
            this.labelWarning.Text = "Servis durduğunda sunucu korunmaz: izleme, ban ve mailler durur. Durdurma, yeniden başlatma ve kaldırma yönetici şifresini tekrar ister ve İşlem Kaydı\'na yazılır.";
            //
            // ServiceModule
            //
            this.AutoScaleDimensions = new System.Drawing.SizeF(6F, 13F);
            this.AutoScaleMode = System.Windows.Forms.AutoScaleMode.Font;
            this.Controls.Add(this.labelWarning);
            this.Controls.Add(this.buttonOpenLogs);
            this.Controls.Add(this.buttonUninstall);
            this.Controls.Add(this.buttonStop);
            this.Controls.Add(this.buttonRestart);
            this.Controls.Add(this.buttonRefresh);
            this.Controls.Add(this.valueMail);
            this.Controls.Add(this.captionMail);
            this.Controls.Add(this.valueLogs);
            this.Controls.Add(this.captionLogs);
            this.Controls.Add(this.valueDatabaseSize);
            this.Controls.Add(this.captionDatabaseSize);
            this.Controls.Add(this.valueDatabase);
            this.Controls.Add(this.captionDatabase);
            this.Controls.Add(this.valuePid);
            this.Controls.Add(this.captionPid);
            this.Controls.Add(this.valueStarted);
            this.Controls.Add(this.captionStarted);
            this.Controls.Add(this.valueVersion);
            this.Controls.Add(this.captionVersion);
            this.Controls.Add(this.valueState);
            this.Controls.Add(this.captionState);
            this.Controls.Add(this.labelInfo);
            this.Controls.Add(this.labelTitle);
            this.Name = "ServiceModule";
            this.Size = new System.Drawing.Size(900, 500);
            this.ResumeLayout(false);
            this.PerformLayout();

        }

        #endregion

        private DevExpress.XtraEditors.LabelControl labelTitle;
        private DevExpress.XtraEditors.LabelControl labelInfo;
        private DevExpress.XtraEditors.LabelControl captionState;
        private DevExpress.XtraEditors.LabelControl valueState;
        private DevExpress.XtraEditors.LabelControl captionVersion;
        private DevExpress.XtraEditors.LabelControl valueVersion;
        private DevExpress.XtraEditors.LabelControl captionStarted;
        private DevExpress.XtraEditors.LabelControl valueStarted;
        private DevExpress.XtraEditors.LabelControl captionPid;
        private DevExpress.XtraEditors.LabelControl valuePid;
        private DevExpress.XtraEditors.LabelControl captionDatabase;
        private DevExpress.XtraEditors.LabelControl valueDatabase;
        private DevExpress.XtraEditors.LabelControl captionDatabaseSize;
        private DevExpress.XtraEditors.LabelControl valueDatabaseSize;
        private DevExpress.XtraEditors.LabelControl captionLogs;
        private DevExpress.XtraEditors.LabelControl valueLogs;
        private DevExpress.XtraEditors.LabelControl captionMail;
        private DevExpress.XtraEditors.LabelControl valueMail;
        private DevExpress.XtraEditors.SimpleButton buttonRefresh;
        private DevExpress.XtraEditors.SimpleButton buttonRestart;
        private DevExpress.XtraEditors.SimpleButton buttonStop;
        private DevExpress.XtraEditors.SimpleButton buttonUninstall;
        private DevExpress.XtraEditors.SimpleButton buttonOpenLogs;
        private DevExpress.XtraEditors.LabelControl labelWarning;
    }
}
