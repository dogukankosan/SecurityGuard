namespace SecurityGuardUI.Forms
{
    partial class MainForm
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

        #region Windows Form Designer generated code

        /// <summary>
        /// Required method for Designer support - do not modify
        /// the contents of this method with the code editor.
        /// </summary>
        private void InitializeComponent()
        {
            this.components = new System.ComponentModel.Container();
            this.container = new DevExpress.XtraEditors.PanelControl();
            this.accordionControl = new DevExpress.XtraBars.Navigation.AccordionControl();
            this.groupMonitor = new DevExpress.XtraBars.Navigation.AccordionControlElement();
            this.elementDashboard = new DevExpress.XtraBars.Navigation.AccordionControlElement();
            this.elementEventLog = new DevExpress.XtraBars.Navigation.AccordionControlElement();
            this.elementAuditTrail = new DevExpress.XtraBars.Navigation.AccordionControlElement();
            this.elementReports = new DevExpress.XtraBars.Navigation.AccordionControlElement();
            this.elementRdpSessions = new DevExpress.XtraBars.Navigation.AccordionControlElement();
            this.elementServiceWatch = new DevExpress.XtraBars.Navigation.AccordionControlElement();
            this.groupProtection = new DevExpress.XtraBars.Navigation.AccordionControlElement();
            this.elementRules = new DevExpress.XtraBars.Navigation.AccordionControlElement();
            this.elementBanList = new DevExpress.XtraBars.Navigation.AccordionControlElement();
            this.elementWhitelist = new DevExpress.XtraBars.Navigation.AccordionControlElement();
            this.elementPortRestrictions = new DevExpress.XtraBars.Navigation.AccordionControlElement();
            this.elementCanary = new DevExpress.XtraBars.Navigation.AccordionControlElement();
            this.elementOpenPorts = new DevExpress.XtraBars.Navigation.AccordionControlElement();
            this.groupAdvice = new DevExpress.XtraBars.Navigation.AccordionControlElement();
            this.elementSecurityChecks = new DevExpress.XtraBars.Navigation.AccordionControlElement();
            this.elementPerformance = new DevExpress.XtraBars.Navigation.AccordionControlElement();
            this.groupSql = new DevExpress.XtraBars.Navigation.AccordionControlElement();
            this.elementSqlConnection = new DevExpress.XtraBars.Navigation.AccordionControlElement();
            this.elementBackup = new DevExpress.XtraBars.Navigation.AccordionControlElement();
            this.elementMaintenance = new DevExpress.XtraBars.Navigation.AccordionControlElement();
            this.groupSystem = new DevExpress.XtraBars.Navigation.AccordionControlElement();
            this.elementMail = new DevExpress.XtraBars.Navigation.AccordionControlElement();
            this.elementService = new DevExpress.XtraBars.Navigation.AccordionControlElement();
            this.elementLicense = new DevExpress.XtraBars.Navigation.AccordionControlElement();
            this.elementSqlite = new DevExpress.XtraBars.Navigation.AccordionControlElement();
            this.elementSettings = new DevExpress.XtraBars.Navigation.AccordionControlElement();
            this.toolbarFormControl = new DevExpress.XtraBars.ToolbarForm.ToolbarFormControl();
            this.toolbarFormManager = new DevExpress.XtraBars.ToolbarForm.ToolbarFormManager(this.components);
            this.skinDropDownButtonItem = new DevExpress.XtraBars.SkinDropDownButtonItem();
            this.skinPaletteDropDownButtonItem = new DevExpress.XtraBars.SkinPaletteDropDownButtonItem();
            this.barStaticUser = new DevExpress.XtraBars.BarStaticItem();
            this.barButtonLogout = new DevExpress.XtraBars.BarButtonItem();
            ((System.ComponentModel.ISupportInitialize)(this.accordionControl)).BeginInit();
            ((System.ComponentModel.ISupportInitialize)(this.toolbarFormControl)).BeginInit();
            ((System.ComponentModel.ISupportInitialize)(this.toolbarFormManager)).BeginInit();
            this.SuspendLayout();
            //
            // container
            //
            this.container.BorderStyle = DevExpress.XtraEditors.Controls.BorderStyles.NoBorder;
            this.container.Dock = System.Windows.Forms.DockStyle.Fill;
            this.container.Location = new System.Drawing.Point(250, 31);
            this.container.Name = "container";
            this.container.Size = new System.Drawing.Size(774, 617);
            this.container.TabIndex = 0;
            //
            // accordionControl
            //
            this.accordionControl.Dock = System.Windows.Forms.DockStyle.Left;
            this.accordionControl.Elements.AddRange(new DevExpress.XtraBars.Navigation.AccordionControlElement[] {
            this.groupMonitor,
            this.groupProtection,
            this.groupAdvice,
            this.groupSql,
            this.groupSystem});
            this.accordionControl.Location = new System.Drawing.Point(0, 31);
            this.accordionControl.Name = "accordionControl";
            this.accordionControl.ScrollBarMode = DevExpress.XtraBars.Navigation.ScrollBarMode.Touch;
            this.accordionControl.Size = new System.Drawing.Size(250, 617);
            this.accordionControl.TabIndex = 1;
            this.accordionControl.ViewType = DevExpress.XtraBars.Navigation.AccordionControlViewType.HamburgerMenu;
            this.accordionControl.ElementClick += new DevExpress.XtraBars.Navigation.ElementClickEventHandler(this.accordionControl_ElementClick);
            //
            // groupMonitor
            //
            this.groupMonitor.Elements.AddRange(new DevExpress.XtraBars.Navigation.AccordionControlElement[] {
            this.elementDashboard,
            this.elementEventLog,
            this.elementRdpSessions,
            this.elementServiceWatch,
            this.elementReports,
            this.elementAuditTrail});
            this.groupMonitor.Expanded = true;
            this.groupMonitor.Name = "groupMonitor";
            this.groupMonitor.Text = "İzleme";
            //
            // elementDashboard
            //
            this.elementDashboard.Name = "elementDashboard";
            this.elementDashboard.Style = DevExpress.XtraBars.Navigation.ElementStyle.Item;
            this.elementDashboard.Text = "Panel";
            //
            // elementEventLog
            //
            this.elementEventLog.Name = "elementEventLog";
            this.elementEventLog.Style = DevExpress.XtraBars.Navigation.ElementStyle.Item;
            this.elementEventLog.Text = "Olay Logu";
            //
            // elementAuditTrail
            //
            this.elementAuditTrail.Name = "elementAuditTrail";
            this.elementAuditTrail.Style = DevExpress.XtraBars.Navigation.ElementStyle.Item;
            this.elementAuditTrail.Text = "İşlem Kaydı";
            //
            // elementReports
            //
            this.elementReports.Name = "elementReports";
            this.elementReports.Style = DevExpress.XtraBars.Navigation.ElementStyle.Item;
            this.elementReports.Text = "Günlük Rapor";
            //
            // elementRdpSessions
            //
            this.elementRdpSessions.Name = "elementRdpSessions";
            this.elementRdpSessions.Style = DevExpress.XtraBars.Navigation.ElementStyle.Item;
            this.elementRdpSessions.Text = "RDP Oturumları";
            //
            // elementServiceWatch
            //
            this.elementServiceWatch.Name = "elementServiceWatch";
            this.elementServiceWatch.Style = DevExpress.XtraBars.Navigation.ElementStyle.Item;
            this.elementServiceWatch.Text = "Servis Takibi";
            //
            // groupProtection
            //
            this.groupProtection.Elements.AddRange(new DevExpress.XtraBars.Navigation.AccordionControlElement[] {
            this.elementRules,
            this.elementBanList,
            this.elementWhitelist,
            this.elementOpenPorts,
            this.elementPortRestrictions,
            this.elementCanary});
            this.groupProtection.Expanded = true;
            this.groupProtection.Name = "groupProtection";
            this.groupProtection.Text = "Koruma";
            //
            // elementRules
            //
            this.elementRules.Name = "elementRules";
            this.elementRules.Style = DevExpress.XtraBars.Navigation.ElementStyle.Item;
            this.elementRules.Text = "Olay Kuralları";
            //
            // elementBanList
            //
            this.elementBanList.Name = "elementBanList";
            this.elementBanList.Style = DevExpress.XtraBars.Navigation.ElementStyle.Item;
            this.elementBanList.Text = "Ban Listesi";
            //
            // elementWhitelist
            //
            this.elementWhitelist.Name = "elementWhitelist";
            this.elementWhitelist.Style = DevExpress.XtraBars.Navigation.ElementStyle.Item;
            this.elementWhitelist.Text = "Beyaz Liste";
            //
            // elementPortRestrictions
            //
            this.elementPortRestrictions.Name = "elementPortRestrictions";
            this.elementPortRestrictions.Style = DevExpress.XtraBars.Navigation.ElementStyle.Item;
            this.elementPortRestrictions.Text = "Port Kısıtları";
            //
            // elementCanary
            //
            this.elementCanary.Name = "elementCanary";
            this.elementCanary.Style = DevExpress.XtraBars.Navigation.ElementStyle.Item;
            this.elementCanary.Text = "Tuzak Dosyalar";
            //
            // elementOpenPorts
            //
            this.elementOpenPorts.Name = "elementOpenPorts";
            this.elementOpenPorts.Style = DevExpress.XtraBars.Navigation.ElementStyle.Item;
            this.elementOpenPorts.Text = "Açık Portlar";
            //
            // groupAdvice
            //
            this.groupAdvice.Elements.AddRange(new DevExpress.XtraBars.Navigation.AccordionControlElement[] {
            this.elementSecurityChecks,
            this.elementPerformance});
            this.groupAdvice.Expanded = true;
            this.groupAdvice.Name = "groupAdvice";
            this.groupAdvice.Text = "Öneriler";
            //
            // elementSecurityChecks
            //
            this.elementSecurityChecks.Name = "elementSecurityChecks";
            this.elementSecurityChecks.Style = DevExpress.XtraBars.Navigation.ElementStyle.Item;
            this.elementSecurityChecks.Text = "Güvenlik Önerileri";
            //
            // elementPerformance
            //
            this.elementPerformance.Name = "elementPerformance";
            this.elementPerformance.Style = DevExpress.XtraBars.Navigation.ElementStyle.Item;
            this.elementPerformance.Text = "Performans Önerileri";
            //
            // groupSql
            //
            this.groupSql.Elements.AddRange(new DevExpress.XtraBars.Navigation.AccordionControlElement[] {
            this.elementSqlConnection,
            this.elementBackup,
            this.elementMaintenance});
            this.groupSql.Expanded = true;
            this.groupSql.Name = "groupSql";
            this.groupSql.Text = "SQL Server";
            //
            // elementSqlConnection
            //
            this.elementSqlConnection.Name = "elementSqlConnection";
            this.elementSqlConnection.Style = DevExpress.XtraBars.Navigation.ElementStyle.Item;
            this.elementSqlConnection.Text = "SQL Bağlantısı";
            //
            // elementBackup
            //
            this.elementBackup.Name = "elementBackup";
            this.elementBackup.Style = DevExpress.XtraBars.Navigation.ElementStyle.Item;
            this.elementBackup.Text = "Yedekleme";
            //
            // elementMaintenance
            //
            this.elementMaintenance.Name = "elementMaintenance";
            this.elementMaintenance.Style = DevExpress.XtraBars.Navigation.ElementStyle.Item;
            this.elementMaintenance.Text = "Bakım";
            //
            // groupSystem
            //
            this.groupSystem.Elements.AddRange(new DevExpress.XtraBars.Navigation.AccordionControlElement[] {
            this.elementMail,
            this.elementService,
            this.elementLicense,
            this.elementSqlite,
            this.elementSettings});
            this.groupSystem.Expanded = true;
            this.groupSystem.Name = "groupSystem";
            this.groupSystem.Text = "Sistem";
            //
            // elementMail
            //
            this.elementMail.Name = "elementMail";
            this.elementMail.Style = DevExpress.XtraBars.Navigation.ElementStyle.Item;
            this.elementMail.Text = "Mail Ayarları";
            //
            // elementService
            //
            this.elementService.Name = "elementService";
            this.elementService.Style = DevExpress.XtraBars.Navigation.ElementStyle.Item;
            this.elementService.Text = "Servis Yönetimi";
            //
            // elementLicense
            //
            this.elementLicense.Name = "elementLicense";
            this.elementLicense.Style = DevExpress.XtraBars.Navigation.ElementStyle.Item;
            this.elementLicense.Text = "Lisans";
            //
            // elementSqlite
            //
            this.elementSqlite.Name = "elementSqlite";
            this.elementSqlite.Style = DevExpress.XtraBars.Navigation.ElementStyle.Item;
            this.elementSqlite.Text = "SQLite Sorgu";
            //
            // elementSettings
            //
            this.elementSettings.Name = "elementSettings";
            this.elementSettings.Style = DevExpress.XtraBars.Navigation.ElementStyle.Item;
            this.elementSettings.Text = "Genel Ayarlar";
            //
            // toolbarFormControl
            //
            this.toolbarFormControl.ToolbarForm = this;
            this.toolbarFormControl.Items.AddRange(new DevExpress.XtraBars.BarItem[] {
            this.skinDropDownButtonItem,
            this.skinPaletteDropDownButtonItem,
            this.barStaticUser,
            this.barButtonLogout});
            this.toolbarFormControl.Location = new System.Drawing.Point(0, 0);
            this.toolbarFormControl.Manager = this.toolbarFormManager;
            this.toolbarFormControl.Name = "toolbarFormControl";
            this.toolbarFormControl.Size = new System.Drawing.Size(1024, 31);
            this.toolbarFormControl.TabIndex = 2;
            this.toolbarFormControl.TabStop = false;
            this.toolbarFormControl.TitleItemLinks.Add(this.skinDropDownButtonItem);
            this.toolbarFormControl.TitleItemLinks.Add(this.skinPaletteDropDownButtonItem);
            this.toolbarFormControl.TitleItemLinks.Add(this.barStaticUser);
            this.toolbarFormControl.TitleItemLinks.Add(this.barButtonLogout);
            //
            // toolbarFormManager
            //
            this.toolbarFormManager.Form = this;
            this.toolbarFormManager.Items.AddRange(new DevExpress.XtraBars.BarItem[] {
            this.skinDropDownButtonItem,
            this.skinPaletteDropDownButtonItem,
            this.barStaticUser,
            this.barButtonLogout});
            this.toolbarFormManager.MaxItemId = 4;
            //
            // skinDropDownButtonItem
            //
            this.skinDropDownButtonItem.Hint = "Tema";
            this.skinDropDownButtonItem.Id = 0;
            this.skinDropDownButtonItem.Name = "skinDropDownButtonItem";
            //
            // skinPaletteDropDownButtonItem
            //
            this.skinPaletteDropDownButtonItem.Hint = "Renk paleti";
            this.skinPaletteDropDownButtonItem.Id = 1;
            this.skinPaletteDropDownButtonItem.Name = "skinPaletteDropDownButtonItem";
            //
            // barStaticUser
            //
            this.barStaticUser.Caption = "Kullanıcı";
            this.barStaticUser.Id = 2;
            this.barStaticUser.Name = "barStaticUser";
            //
            // barButtonLogout
            //
            this.barButtonLogout.Caption = "Çıkış Yap";
            this.barButtonLogout.Id = 3;
            this.barButtonLogout.Name = "barButtonLogout";
            this.barButtonLogout.ItemClick += new DevExpress.XtraBars.ItemClickEventHandler(this.barButtonLogout_ItemClick);
            //
            // MainForm
            //
            this.AutoScaleDimensions = new System.Drawing.SizeF(6F, 13F);
            this.AutoScaleMode = System.Windows.Forms.AutoScaleMode.Font;
            this.ClientSize = new System.Drawing.Size(1024, 648);
            this.Controls.Add(this.container);
            this.Controls.Add(this.accordionControl);
            this.Controls.Add(this.toolbarFormControl);
            this.ToolbarFormControl = this.toolbarFormControl;
            this.MinimumSize = new System.Drawing.Size(900, 560);
            this.Name = "MainForm";
            this.StartPosition = System.Windows.Forms.FormStartPosition.CenterScreen;
            this.Text = "SecurityGuard";
            ((System.ComponentModel.ISupportInitialize)(this.accordionControl)).EndInit();
            ((System.ComponentModel.ISupportInitialize)(this.toolbarFormControl)).EndInit();
            ((System.ComponentModel.ISupportInitialize)(this.toolbarFormManager)).EndInit();
            this.ResumeLayout(false);
            this.PerformLayout();

        }

        #endregion

        private DevExpress.XtraEditors.PanelControl container;
        private DevExpress.XtraBars.Navigation.AccordionControl accordionControl;
        private DevExpress.XtraBars.Navigation.AccordionControlElement groupMonitor;
        private DevExpress.XtraBars.Navigation.AccordionControlElement elementDashboard;
        private DevExpress.XtraBars.Navigation.AccordionControlElement elementEventLog;
        private DevExpress.XtraBars.Navigation.AccordionControlElement elementAuditTrail;
        private DevExpress.XtraBars.Navigation.AccordionControlElement elementReports;
        private DevExpress.XtraBars.Navigation.AccordionControlElement elementRdpSessions;
        private DevExpress.XtraBars.Navigation.AccordionControlElement elementServiceWatch;
        private DevExpress.XtraBars.Navigation.AccordionControlElement groupProtection;
        private DevExpress.XtraBars.Navigation.AccordionControlElement elementRules;
        private DevExpress.XtraBars.Navigation.AccordionControlElement elementBanList;
        private DevExpress.XtraBars.Navigation.AccordionControlElement elementWhitelist;
        private DevExpress.XtraBars.Navigation.AccordionControlElement elementPortRestrictions;
        private DevExpress.XtraBars.Navigation.AccordionControlElement elementCanary;
        private DevExpress.XtraBars.Navigation.AccordionControlElement elementOpenPorts;
        private DevExpress.XtraBars.Navigation.AccordionControlElement groupAdvice;
        private DevExpress.XtraBars.Navigation.AccordionControlElement elementSecurityChecks;
        private DevExpress.XtraBars.Navigation.AccordionControlElement elementPerformance;
        private DevExpress.XtraBars.Navigation.AccordionControlElement groupSql;
        private DevExpress.XtraBars.Navigation.AccordionControlElement elementSqlConnection;
        private DevExpress.XtraBars.Navigation.AccordionControlElement elementBackup;
        private DevExpress.XtraBars.Navigation.AccordionControlElement elementMaintenance;
        private DevExpress.XtraBars.Navigation.AccordionControlElement groupSystem;
        private DevExpress.XtraBars.Navigation.AccordionControlElement elementMail;
        private DevExpress.XtraBars.Navigation.AccordionControlElement elementService;
        private DevExpress.XtraBars.Navigation.AccordionControlElement elementLicense;
        private DevExpress.XtraBars.Navigation.AccordionControlElement elementSqlite;
        private DevExpress.XtraBars.Navigation.AccordionControlElement elementSettings;
        private DevExpress.XtraBars.ToolbarForm.ToolbarFormControl toolbarFormControl;
        private DevExpress.XtraBars.ToolbarForm.ToolbarFormManager toolbarFormManager;
        private DevExpress.XtraBars.SkinDropDownButtonItem skinDropDownButtonItem;
        private DevExpress.XtraBars.SkinPaletteDropDownButtonItem skinPaletteDropDownButtonItem;
        private DevExpress.XtraBars.BarStaticItem barStaticUser;
        private DevExpress.XtraBars.BarButtonItem barButtonLogout;
    }
}
