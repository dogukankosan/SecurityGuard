namespace SecurityGuardUI.Modules
{
    partial class SqlModule
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
            this.buttonRescan = new DevExpress.XtraEditors.SimpleButton();
            this.gridControl = new DevExpress.XtraGrid.GridControl();
            this.gridView = new DevExpress.XtraGrid.Views.Grid.GridView();
            this.colInstance = new DevExpress.XtraGrid.Columns.GridColumn();
            this.colVersion = new DevExpress.XtraGrid.Columns.GridColumn();
            this.colEdition = new DevExpress.XtraGrid.Columns.GridColumn();
            this.colServiceState = new DevExpress.XtraGrid.Columns.GridColumn();
            this.colPort = new DevExpress.XtraGrid.Columns.GridColumn();
            this.colConnection = new DevExpress.XtraGrid.Columns.GridColumn();
            this.labelEmpty = new DevExpress.XtraEditors.LabelControl();
            this.panelForm = new DevExpress.XtraEditors.PanelControl();
            this.labelFormTitle = new DevExpress.XtraEditors.LabelControl();
            this.labelServer = new DevExpress.XtraEditors.LabelControl();
            this.textServer = new DevExpress.XtraEditors.TextEdit();
            this.labelServerHint = new DevExpress.XtraEditors.LabelControl();
            this.labelPort = new DevExpress.XtraEditors.LabelControl();
            this.spinPort = new DevExpress.XtraEditors.SpinEdit();
            this.labelPortHint = new DevExpress.XtraEditors.LabelControl();
            this.labelLogin = new DevExpress.XtraEditors.LabelControl();
            this.textLogin = new DevExpress.XtraEditors.TextEdit();
            this.labelPassword = new DevExpress.XtraEditors.LabelControl();
            this.textPassword = new DevExpress.XtraEditors.TextEdit();
            this.checkEnabled = new DevExpress.XtraEditors.CheckEdit();
            this.buttonTest = new DevExpress.XtraEditors.SimpleButton();
            this.buttonSave = new DevExpress.XtraEditors.SimpleButton();
            this.labelSaveHint = new DevExpress.XtraEditors.LabelControl();
            this.labelResult = new DevExpress.XtraEditors.LabelControl();
            this.labelScriptTitle = new DevExpress.XtraEditors.LabelControl();
            this.labelScriptInfo = new DevExpress.XtraEditors.LabelControl();
            this.memoScript = new DevExpress.XtraEditors.MemoEdit();
            this.buttonCopyScript = new DevExpress.XtraEditors.SimpleButton();
            this.colAudit = new DevExpress.XtraGrid.Columns.GridColumn();
            this.labelAuditTitle = new DevExpress.XtraEditors.LabelControl();
            this.labelAuditStatus = new DevExpress.XtraEditors.LabelControl();
            this.buttonAuditInstall = new DevExpress.XtraEditors.SimpleButton();
            this.buttonAuditUninstall = new DevExpress.XtraEditors.SimpleButton();
            this.buttonAuditScript = new DevExpress.XtraEditors.SimpleButton();
            ((System.ComponentModel.ISupportInitialize)(this.panelTop)).BeginInit();
            this.panelTop.SuspendLayout();
            ((System.ComponentModel.ISupportInitialize)(this.gridControl)).BeginInit();
            ((System.ComponentModel.ISupportInitialize)(this.gridView)).BeginInit();
            ((System.ComponentModel.ISupportInitialize)(this.panelForm)).BeginInit();
            this.panelForm.SuspendLayout();
            ((System.ComponentModel.ISupportInitialize)(this.textServer.Properties)).BeginInit();
            ((System.ComponentModel.ISupportInitialize)(this.spinPort.Properties)).BeginInit();
            ((System.ComponentModel.ISupportInitialize)(this.textLogin.Properties)).BeginInit();
            ((System.ComponentModel.ISupportInitialize)(this.textPassword.Properties)).BeginInit();
            ((System.ComponentModel.ISupportInitialize)(this.checkEnabled.Properties)).BeginInit();
            ((System.ComponentModel.ISupportInitialize)(this.memoScript.Properties)).BeginInit();
            this.SuspendLayout();
            //
            // panelTop
            //
            this.panelTop.BorderStyle = DevExpress.XtraEditors.Controls.BorderStyles.NoBorder;
            this.panelTop.Controls.Add(this.labelTitle);
            this.panelTop.Controls.Add(this.labelInfo);
            this.panelTop.Controls.Add(this.buttonRescan);
            this.panelTop.Dock = System.Windows.Forms.DockStyle.Top;
            this.panelTop.Location = new System.Drawing.Point(0, 0);
            this.panelTop.Name = "panelTop";
            this.panelTop.Size = new System.Drawing.Size(900, 64);
            this.panelTop.TabIndex = 0;
            //
            // labelTitle
            //
            this.labelTitle.Appearance.Font = new System.Drawing.Font("Segoe UI", 14F);
            this.labelTitle.Appearance.Options.UseFont = true;
            this.labelTitle.Location = new System.Drawing.Point(12, 8);
            this.labelTitle.Name = "labelTitle";
            this.labelTitle.Size = new System.Drawing.Size(190, 25);
            this.labelTitle.TabIndex = 0;
            this.labelTitle.Text = "SQL Server Bağlantısı";
            //
            // labelInfo
            //
            this.labelInfo.Location = new System.Drawing.Point(12, 38);
            this.labelInfo.Name = "labelInfo";
            this.labelInfo.Size = new System.Drawing.Size(620, 13);
            this.labelInfo.TabIndex = 1;
            this.labelInfo.Text = "Sunucudaki SQL Server instance\'ları otomatik bulunur. İzleme için tam yetkili bir SQL login kullanılır; bilgiler şifreli saklanır.";
            //
            // buttonRescan
            //
            this.buttonRescan.Anchor = ((System.Windows.Forms.AnchorStyles)((System.Windows.Forms.AnchorStyles.Top | System.Windows.Forms.AnchorStyles.Right)));
            this.buttonRescan.Location = new System.Drawing.Point(778, 12);
            this.buttonRescan.Name = "buttonRescan";
            this.buttonRescan.Size = new System.Drawing.Size(110, 26);
            this.buttonRescan.TabIndex = 2;
            this.buttonRescan.Text = "Yeniden Tara";
            this.buttonRescan.Click += new System.EventHandler(this.buttonRescan_Click);
            //
            // gridControl
            //
            this.gridControl.Dock = System.Windows.Forms.DockStyle.Top;
            this.gridControl.Location = new System.Drawing.Point(0, 64);
            this.gridControl.MainView = this.gridView;
            this.gridControl.Name = "gridControl";
            this.gridControl.Size = new System.Drawing.Size(900, 130);
            this.gridControl.TabIndex = 1;
            this.gridControl.ViewCollection.AddRange(new DevExpress.XtraGrid.Views.Base.BaseView[] {
            this.gridView});
            //
            // gridView
            //
            this.gridView.Columns.AddRange(new DevExpress.XtraGrid.Columns.GridColumn[] {
            this.colInstance,
            this.colVersion,
            this.colEdition,
            this.colServiceState,
            this.colPort,
            this.colConnection,
            this.colAudit});
            this.gridView.GridControl = this.gridControl;
            this.gridView.Name = "gridView";
            this.gridView.FocusedRowChanged += new DevExpress.XtraGrid.Views.Base.FocusedRowChangedEventHandler(this.gridView_FocusedRowChanged);
            //
            // colInstance
            //
            this.colInstance.Caption = "Instance";
            this.colInstance.FieldName = "DisplayName";
            this.colInstance.Name = "colInstance";
            this.colInstance.Visible = true;
            this.colInstance.VisibleIndex = 0;
            this.colInstance.Width = 170;
            //
            // colVersion
            //
            this.colVersion.Caption = "Sürüm";
            this.colVersion.FieldName = "Version";
            this.colVersion.Name = "colVersion";
            this.colVersion.Visible = true;
            this.colVersion.VisibleIndex = 1;
            this.colVersion.Width = 100;
            //
            // colEdition
            //
            this.colEdition.Caption = "Edition";
            this.colEdition.FieldName = "Edition";
            this.colEdition.Name = "colEdition";
            this.colEdition.Visible = true;
            this.colEdition.VisibleIndex = 2;
            this.colEdition.Width = 150;
            //
            // colServiceState
            //
            this.colServiceState.Caption = "SQL servisi";
            this.colServiceState.FieldName = "ServiceState";
            this.colServiceState.Name = "colServiceState";
            this.colServiceState.Visible = true;
            this.colServiceState.VisibleIndex = 3;
            this.colServiceState.Width = 100;
            //
            // colPort
            //
            this.colPort.Caption = "TCP port";
            this.colPort.FieldName = "PortText";
            this.colPort.MaxWidth = 90;
            this.colPort.Name = "colPort";
            this.colPort.Visible = true;
            this.colPort.VisibleIndex = 4;
            this.colPort.Width = 70;
            //
            // colConnection
            //
            this.colConnection.Caption = "Bağlantı";
            this.colConnection.FieldName = "ConnectionText";
            this.colConnection.Name = "colConnection";
            this.colConnection.Visible = true;
            this.colConnection.VisibleIndex = 5;
            this.colConnection.Width = 220;
            //
            // colAudit
            //
            this.colAudit.Caption = "SQL izleme";
            this.colAudit.FieldName = "AuditText";
            this.colAudit.Name = "colAudit";
            this.colAudit.Visible = true;
            this.colAudit.VisibleIndex = 6;
            this.colAudit.Width = 170;
            //
            // labelEmpty
            //
            this.labelEmpty.AutoSizeMode = DevExpress.XtraEditors.LabelAutoSizeMode.None;
            this.labelEmpty.Dock = System.Windows.Forms.DockStyle.Top;
            this.labelEmpty.Location = new System.Drawing.Point(0, 194);
            this.labelEmpty.Name = "labelEmpty";
            this.labelEmpty.Padding = new System.Windows.Forms.Padding(12, 6, 0, 6);
            this.labelEmpty.Size = new System.Drawing.Size(900, 26);
            this.labelEmpty.TabIndex = 2;
            this.labelEmpty.Text = "Bu sunucuda SQL Server bulunamadı.";
            this.labelEmpty.Visible = false;
            //
            // panelForm
            //
            this.panelForm.BorderStyle = DevExpress.XtraEditors.Controls.BorderStyles.NoBorder;
            this.panelForm.Controls.Add(this.labelFormTitle);
            this.panelForm.Controls.Add(this.labelServer);
            this.panelForm.Controls.Add(this.textServer);
            this.panelForm.Controls.Add(this.labelServerHint);
            this.panelForm.Controls.Add(this.labelPort);
            this.panelForm.Controls.Add(this.spinPort);
            this.panelForm.Controls.Add(this.labelPortHint);
            this.panelForm.Controls.Add(this.labelLogin);
            this.panelForm.Controls.Add(this.textLogin);
            this.panelForm.Controls.Add(this.labelPassword);
            this.panelForm.Controls.Add(this.textPassword);
            this.panelForm.Controls.Add(this.checkEnabled);
            this.panelForm.Controls.Add(this.buttonTest);
            this.panelForm.Controls.Add(this.buttonSave);
            this.panelForm.Controls.Add(this.labelSaveHint);
            this.panelForm.Controls.Add(this.labelResult);
            this.panelForm.Controls.Add(this.labelScriptTitle);
            this.panelForm.Controls.Add(this.labelScriptInfo);
            this.panelForm.Controls.Add(this.memoScript);
            this.panelForm.Controls.Add(this.buttonCopyScript);
            this.panelForm.Controls.Add(this.labelAuditTitle);
            this.panelForm.Controls.Add(this.labelAuditStatus);
            this.panelForm.Controls.Add(this.buttonAuditInstall);
            this.panelForm.Controls.Add(this.buttonAuditUninstall);
            this.panelForm.Controls.Add(this.buttonAuditScript);
            this.panelForm.Dock = System.Windows.Forms.DockStyle.Fill;
            this.panelForm.Location = new System.Drawing.Point(0, 220);
            this.panelForm.Name = "panelForm";
            this.panelForm.Size = new System.Drawing.Size(900, 480);
            this.panelForm.TabIndex = 3;
            //
            // labelFormTitle
            //
            this.labelFormTitle.Appearance.Font = new System.Drawing.Font("Segoe UI", 11F);
            this.labelFormTitle.Appearance.Options.UseFont = true;
            this.labelFormTitle.Location = new System.Drawing.Point(16, 14);
            this.labelFormTitle.Name = "labelFormTitle";
            this.labelFormTitle.Size = new System.Drawing.Size(66, 20);
            this.labelFormTitle.TabIndex = 0;
            this.labelFormTitle.Text = "Bağlantı";
            //
            // labelServer
            //
            this.labelServer.Location = new System.Drawing.Point(16, 46);
            this.labelServer.Name = "labelServer";
            this.labelServer.Size = new System.Drawing.Size(172, 13);
            this.labelServer.TabIndex = 1;
            this.labelServer.Text = "Sunucu (önerilen: localhost veya localhost\\AD)";
            //
            // textServer
            //
            this.textServer.Location = new System.Drawing.Point(16, 65);
            this.textServer.Name = "textServer";
            this.textServer.Properties.MaxLength = 200;
            this.textServer.Size = new System.Drawing.Size(300, 20);
            this.textServer.TabIndex = 2;
            //
            // labelServerHint
            //
            this.labelServerHint.Appearance.Options.UseForeColor = true;
            this.labelServerHint.Location = new System.Drawing.Point(16, 89);
            this.labelServerHint.Name = "labelServerHint";
            this.labelServerHint.Size = new System.Drawing.Size(0, 13);
            this.labelServerHint.TabIndex = 21;
            //
            // labelPort
            //
            this.labelPort.Location = new System.Drawing.Point(330, 46);
            this.labelPort.Name = "labelPort";
            this.labelPort.Size = new System.Drawing.Size(20, 13);
            this.labelPort.TabIndex = 3;
            this.labelPort.Text = "Port";
            //
            // spinPort
            //
            this.spinPort.EditValue = new decimal(new int[] {
            0,
            0,
            0,
            0});
            this.spinPort.Location = new System.Drawing.Point(330, 65);
            this.spinPort.Name = "spinPort";
            this.spinPort.Properties.Buttons.AddRange(new DevExpress.XtraEditors.Controls.EditorButton[] {
            new DevExpress.XtraEditors.Controls.EditorButton(DevExpress.XtraEditors.Controls.ButtonPredefines.Combo)});
            this.spinPort.Properties.IsFloatValue = false;
            this.spinPort.Properties.Mask.EditMask = "N0";
            this.spinPort.Properties.MaxValue = new decimal(new int[] {
            65535,
            0,
            0,
            0});
            this.spinPort.Size = new System.Drawing.Size(90, 20);
            this.spinPort.TabIndex = 4;
            //
            // labelPortHint
            //
            this.labelPortHint.Location = new System.Drawing.Point(428, 68);
            this.labelPortHint.Name = "labelPortHint";
            this.labelPortHint.Size = new System.Drawing.Size(200, 13);
            this.labelPortHint.TabIndex = 5;
            this.labelPortHint.Text = "0 = otomatik (instance adıyla bağlanır)";
            //
            // labelLogin
            //
            this.labelLogin.Location = new System.Drawing.Point(16, 116);
            this.labelLogin.Name = "labelLogin";
            this.labelLogin.Size = new System.Drawing.Size(84, 13);
            this.labelLogin.TabIndex = 6;
            this.labelLogin.Text = "SQL login (sabit)";
            //
            // textLogin
            //
            this.textLogin.Location = new System.Drawing.Point(16, 135);
            this.textLogin.Name = "textLogin";
            this.textLogin.Properties.ReadOnly = true;
            this.textLogin.TabStop = false;
            this.textLogin.Size = new System.Drawing.Size(300, 20);
            this.textLogin.TabIndex = 7;
            //
            // labelPassword
            //
            this.labelPassword.Location = new System.Drawing.Point(330, 116);
            this.labelPassword.Name = "labelPassword";
            this.labelPassword.Size = new System.Drawing.Size(24, 13);
            this.labelPassword.TabIndex = 8;
            this.labelPassword.Text = "Şifre";
            //
            // textPassword
            //
            this.textPassword.Location = new System.Drawing.Point(330, 135);
            this.textPassword.Name = "textPassword";
            this.textPassword.Properties.MaxLength = 128;
            this.textPassword.Properties.UseSystemPasswordChar = true;
            this.textPassword.Size = new System.Drawing.Size(300, 20);
            this.textPassword.TabIndex = 9;
            //
            // checkEnabled
            //
            this.checkEnabled.Location = new System.Drawing.Point(14, 166);
            this.checkEnabled.Name = "checkEnabled";
            this.checkEnabled.Properties.Caption = "Bu instance\'ı izle";
            this.checkEnabled.Size = new System.Drawing.Size(200, 20);
            this.checkEnabled.TabIndex = 10;
            //
            // buttonTest
            //
            this.buttonTest.Location = new System.Drawing.Point(16, 198);
            this.buttonTest.Name = "buttonTest";
            this.buttonTest.Size = new System.Drawing.Size(150, 28);
            this.buttonTest.TabIndex = 11;
            this.buttonTest.Text = "Bağlantıyı Test Et";
            this.buttonTest.Click += new System.EventHandler(this.buttonTest_Click);
            //
            // buttonSave
            //
            this.buttonSave.Location = new System.Drawing.Point(172, 198);
            this.buttonSave.Name = "buttonSave";
            this.buttonSave.Size = new System.Drawing.Size(100, 28);
            this.buttonSave.TabIndex = 12;
            this.buttonSave.Text = "Kaydet";
            this.buttonSave.Click += new System.EventHandler(this.buttonSave_Click);
            //
            // labelSaveHint
            //
            this.labelSaveHint.Appearance.Options.UseForeColor = true;
            this.labelSaveHint.Location = new System.Drawing.Point(282, 206);
            this.labelSaveHint.Name = "labelSaveHint";
            this.labelSaveHint.Size = new System.Drawing.Size(200, 13);
            this.labelSaveHint.TabIndex = 13;
            //
            // labelResult
            //
            this.labelResult.Appearance.Options.UseForeColor = true;
            this.labelResult.Appearance.Options.UseTextOptions = true;
            this.labelResult.Appearance.TextOptions.WordWrap = DevExpress.Utils.WordWrap.Wrap;
            this.labelResult.AutoSizeMode = DevExpress.XtraEditors.LabelAutoSizeMode.None;
            this.labelResult.Location = new System.Drawing.Point(16, 238);
            this.labelResult.Name = "labelResult";
            this.labelResult.Size = new System.Drawing.Size(720, 52);
            this.labelResult.TabIndex = 14;
            //
            // labelAuditTitle
            //
            this.labelAuditTitle.Appearance.Font = new System.Drawing.Font("Segoe UI", 11F);
            this.labelAuditTitle.Appearance.Options.UseFont = true;
            this.labelAuditTitle.Location = new System.Drawing.Point(16, 302);
            this.labelAuditTitle.Name = "labelAuditTitle";
            this.labelAuditTitle.Size = new System.Drawing.Size(140, 20);
            this.labelAuditTitle.TabIndex = 19;
            this.labelAuditTitle.Text = "SQL izleme (Audit)";
            //
            // labelAuditStatus
            //
            this.labelAuditStatus.Appearance.Options.UseForeColor = true;
            this.labelAuditStatus.Appearance.Options.UseTextOptions = true;
            this.labelAuditStatus.Appearance.TextOptions.WordWrap = DevExpress.Utils.WordWrap.Wrap;
            this.labelAuditStatus.AutoSizeMode = DevExpress.XtraEditors.LabelAutoSizeMode.None;
            this.labelAuditStatus.Location = new System.Drawing.Point(16, 326);
            this.labelAuditStatus.Name = "labelAuditStatus";
            this.labelAuditStatus.Size = new System.Drawing.Size(720, 26);
            this.labelAuditStatus.TabIndex = 20;
            //
            // buttonAuditInstall
            //
            this.buttonAuditInstall.Location = new System.Drawing.Point(16, 354);
            this.buttonAuditInstall.Name = "buttonAuditInstall";
            this.buttonAuditInstall.Size = new System.Drawing.Size(150, 28);
            this.buttonAuditInstall.TabIndex = 21;
            this.buttonAuditInstall.Text = "İzlemeyi Kur";
            this.buttonAuditInstall.Click += new System.EventHandler(this.buttonAuditInstall_Click);
            //
            // buttonAuditUninstall
            //
            this.buttonAuditUninstall.Location = new System.Drawing.Point(172, 354);
            this.buttonAuditUninstall.Name = "buttonAuditUninstall";
            this.buttonAuditUninstall.Size = new System.Drawing.Size(150, 28);
            this.buttonAuditUninstall.TabIndex = 22;
            this.buttonAuditUninstall.Text = "İzlemeyi Kaldır";
            this.buttonAuditUninstall.Click += new System.EventHandler(this.buttonAuditUninstall_Click);
            //
            // buttonAuditScript
            //
            this.buttonAuditScript.Location = new System.Drawing.Point(328, 354);
            this.buttonAuditScript.Name = "buttonAuditScript";
            this.buttonAuditScript.Size = new System.Drawing.Size(150, 28);
            this.buttonAuditScript.TabIndex = 23;
            this.buttonAuditScript.Text = "Audit Scriptini Kopyala";
            this.buttonAuditScript.Click += new System.EventHandler(this.buttonAuditScript_Click);
            //
            // labelScriptTitle
            //
            this.labelScriptTitle.Appearance.Font = new System.Drawing.Font("Segoe UI", 11F);
            this.labelScriptTitle.Appearance.Options.UseFont = true;
            this.labelScriptTitle.Location = new System.Drawing.Point(16, 398);
            this.labelScriptTitle.Name = "labelScriptTitle";
            this.labelScriptTitle.Size = new System.Drawing.Size(166, 20);
            this.labelScriptTitle.TabIndex = 15;
            this.labelScriptTitle.Text = "Login oluşturma scripti";
            //
            // labelScriptInfo
            //
            this.labelScriptInfo.Location = new System.Drawing.Point(16, 422);
            this.labelScriptInfo.Name = "labelScriptInfo";
            this.labelScriptInfo.Size = new System.Drawing.Size(720, 13);
            this.labelScriptInfo.TabIndex = 16;
            this.labelScriptInfo.Text = "SecurityGuard sadece bu login ile ve tam yetkili (sysadmin) çalışır. Login yoksa scripti SSMS\'te çalıştırın (şifreyi değiştirin), sonra test edip kaydedin.";
            //
            // memoScript
            //
            this.memoScript.Location = new System.Drawing.Point(16, 442);
            this.memoScript.Name = "memoScript";
            this.memoScript.Properties.Appearance.Font = new System.Drawing.Font("Consolas", 9F);
            this.memoScript.Properties.Appearance.Options.UseFont = true;
            this.memoScript.Properties.ReadOnly = true;
            this.memoScript.Size = new System.Drawing.Size(720, 88);
            this.memoScript.TabIndex = 17;
            //
            // buttonCopyScript
            //
            this.buttonCopyScript.Location = new System.Drawing.Point(16, 538);
            this.buttonCopyScript.Name = "buttonCopyScript";
            this.buttonCopyScript.Size = new System.Drawing.Size(140, 26);
            this.buttonCopyScript.TabIndex = 18;
            this.buttonCopyScript.Text = "Scripti Kopyala";
            this.buttonCopyScript.Click += new System.EventHandler(this.buttonCopyScript_Click);
            //
            // SqlModule
            //
            this.AutoScaleDimensions = new System.Drawing.SizeF(6F, 13F);
            this.AutoScaleMode = System.Windows.Forms.AutoScaleMode.Font;
            this.Controls.Add(this.panelForm);
            this.Controls.Add(this.labelEmpty);
            this.Controls.Add(this.gridControl);
            this.Controls.Add(this.panelTop);
            this.Name = "SqlModule";
            this.Size = new System.Drawing.Size(900, 700);
            ((System.ComponentModel.ISupportInitialize)(this.panelTop)).EndInit();
            this.panelTop.ResumeLayout(false);
            this.panelTop.PerformLayout();
            ((System.ComponentModel.ISupportInitialize)(this.gridControl)).EndInit();
            ((System.ComponentModel.ISupportInitialize)(this.gridView)).EndInit();
            ((System.ComponentModel.ISupportInitialize)(this.textServer.Properties)).EndInit();
            ((System.ComponentModel.ISupportInitialize)(this.spinPort.Properties)).EndInit();
            ((System.ComponentModel.ISupportInitialize)(this.textLogin.Properties)).EndInit();
            ((System.ComponentModel.ISupportInitialize)(this.textPassword.Properties)).EndInit();
            ((System.ComponentModel.ISupportInitialize)(this.checkEnabled.Properties)).EndInit();
            ((System.ComponentModel.ISupportInitialize)(this.memoScript.Properties)).EndInit();
            ((System.ComponentModel.ISupportInitialize)(this.panelForm)).EndInit();
            this.panelForm.ResumeLayout(false);
            this.panelForm.PerformLayout();
            this.ResumeLayout(false);

        }

        #endregion

        private DevExpress.XtraEditors.PanelControl panelTop;
        private DevExpress.XtraEditors.LabelControl labelTitle;
        private DevExpress.XtraEditors.LabelControl labelInfo;
        private DevExpress.XtraEditors.SimpleButton buttonRescan;
        private DevExpress.XtraGrid.GridControl gridControl;
        private DevExpress.XtraGrid.Views.Grid.GridView gridView;
        private DevExpress.XtraGrid.Columns.GridColumn colInstance;
        private DevExpress.XtraGrid.Columns.GridColumn colVersion;
        private DevExpress.XtraGrid.Columns.GridColumn colEdition;
        private DevExpress.XtraGrid.Columns.GridColumn colServiceState;
        private DevExpress.XtraGrid.Columns.GridColumn colPort;
        private DevExpress.XtraGrid.Columns.GridColumn colConnection;
        private DevExpress.XtraEditors.LabelControl labelEmpty;
        private DevExpress.XtraEditors.PanelControl panelForm;
        private DevExpress.XtraEditors.LabelControl labelFormTitle;
        private DevExpress.XtraEditors.LabelControl labelServer;
        private DevExpress.XtraEditors.TextEdit textServer;
        private DevExpress.XtraEditors.LabelControl labelServerHint;
        private DevExpress.XtraEditors.LabelControl labelPort;
        private DevExpress.XtraEditors.SpinEdit spinPort;
        private DevExpress.XtraEditors.LabelControl labelPortHint;
        private DevExpress.XtraEditors.LabelControl labelLogin;
        private DevExpress.XtraEditors.TextEdit textLogin;
        private DevExpress.XtraEditors.LabelControl labelPassword;
        private DevExpress.XtraEditors.TextEdit textPassword;
        private DevExpress.XtraEditors.CheckEdit checkEnabled;
        private DevExpress.XtraEditors.SimpleButton buttonTest;
        private DevExpress.XtraEditors.SimpleButton buttonSave;
        private DevExpress.XtraEditors.LabelControl labelSaveHint;
        private DevExpress.XtraEditors.LabelControl labelResult;
        private DevExpress.XtraEditors.LabelControl labelScriptTitle;
        private DevExpress.XtraEditors.LabelControl labelScriptInfo;
        private DevExpress.XtraEditors.MemoEdit memoScript;
        private DevExpress.XtraEditors.SimpleButton buttonCopyScript;
        private DevExpress.XtraGrid.Columns.GridColumn colAudit;
        private DevExpress.XtraEditors.LabelControl labelAuditTitle;
        private DevExpress.XtraEditors.LabelControl labelAuditStatus;
        private DevExpress.XtraEditors.SimpleButton buttonAuditInstall;
        private DevExpress.XtraEditors.SimpleButton buttonAuditUninstall;
        private DevExpress.XtraEditors.SimpleButton buttonAuditScript;
    }
}
