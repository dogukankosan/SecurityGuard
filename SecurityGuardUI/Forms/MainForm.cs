using System;
using System.Collections.Generic;
using System.Windows.Forms;
using DevExpress.XtraBars.ToolbarForm;
using DevExpress.XtraBars.Navigation;
using SecurityGuard.Shared.Dto;
using SecurityGuardUI.Helpers;
using SecurityGuardUI.Ipc;
using SecurityGuardUI.Modules;

namespace SecurityGuardUI.Forms
{
    public enum MainExitReason
    {
        /// <summary>Pencere kapatıldı: program biter.</summary>
        Closed,

        /// <summary>Kullanıcı çıkış yaptı: giriş ekranına dönülür.</summary>
        Logout,

        /// <summary>Oturum düştü (işlem yapılmadı veya servis yeniden başladı): giriş ekranına dönülür.</summary>
        SessionExpired,

        /// <summary>Servis arayüzden durduruldu / yeniden başlatıldı: en baştan servise bağlanılır.</summary>
        ServiceStopped,

        /// <summary>Lisans durumu değişti: oturum korunur, kurulum sihirbazı / ana form baştan açılır.</summary>
        Reload
    }

    /// <summary>
    /// Ana pencere: solda menü, sağda seçilen modül. Başlık çubuğunda tema / palet seçici ve çıkış.
    /// 30 dakika işlem yapılmazsa otomatik çıkış yapılır; kullanıcı aktifken servis oturumu canlı tutulur.
    ///
    /// Not: Pencere tipi bilerek ToolbarForm. DevExpress 20.1 FluentDesignForm metin kutularını çizmek için
    /// pencereye saydam renk (TransparencyKey) atıyor: açık temalarda tıklama alttaki uygulamaya geçiyor,
    /// renk silinince de metin kutuları bozuluyor. ToolbarForm bu hileyi kullanmıyor.
    /// </summary>
    public partial class MainForm : ToolbarForm, IModuleHost
    {
        private readonly GuardClient _client;
        private readonly Dictionary<AccordionControlElement, Control> _modules = new Dictionary<AccordionControlElement, Control>();
        private IdleWatcher _idle;
        private bool _exiting;
        private readonly bool _startUnlicensed;
        private DateTime _unlicensedShown;

        public MainForm(GuardClient client, StatusDto status, bool unlicensed = false)
        {
            _startUnlicensed = unlicensed;
            InitializeComponent();
            Icon = AppImages.Icon;
            SetMenuIcons();
            HandCursor.Apply(this); // modüller sonradan eklenince onların butonları da kapsanır
            _client = client;
            Text = "SecurityGuard - " + status.MachineName;
            barStaticUser.Caption = "Kullanıcı: " + client.UserName;
            ExitReason = MainExitReason.Closed;
        }

        /// <summary>Menü ikonları (DevExpress Icon Builder SVG'leri; temaya göre renklenir).</summary>
        private void SetMenuIcons()
        {
            var icons = new Dictionary<AccordionControlElement, string>
            {
                { groupMonitor, "security_visibility" },
                { elementDashboard, "actions_home" },
                { elementEventLog, "actions_bell" },
                { elementReports, "business_report" },
                { elementRdpSessions, "actions_clock" },
                { elementServiceWatch, "actions_settings" },
                { elementAuditTrail, "actions_book" },
                { groupProtection, "security_lock" },
                { elementRules, "actions_filter" },
                { elementBanList, "actions_forbid" },
                { elementWhitelist, "actions_checkcircled" },
                { elementPortRestrictions, "security_security" },
                { elementCanary, "security_bug" },
                { elementOpenPorts, "security_unlock" },
                { groupAdvice, "business_idea" },
                { elementSecurityChecks, "security_warning" },
                { elementPerformance, "business_linearchart" },
                { groupSql, "actions_database" },
                { elementSqlConnection, "actions_hyperlink" },
                { elementBackup, "business_safe" },
                { elementMaintenance, "actions_refresh" },
                { groupSystem, "actions_settings" },
                { elementMail, "actions_envelopeclose" },
                { elementService, "electronics_desktopwindows" },
                { elementLicense, "security_key" },
                { elementSqlite, "actions_table" },
                { elementSettings, "actions_options" },
            };
            foreach (var pair in icons)
            {
                pair.Key.ImageOptions.SvgImage = AppImages.Menu(pair.Value);
                pair.Key.ImageOptions.SvgImageSize = new System.Drawing.Size(18, 18);
            }
        }

        public MainExitReason ExitReason { get; private set; }

        /// <summary>Oturum düştüğünde çıkış sebebi (giriş ekranında gösterilir).</summary>
        public string ExitMessage { get; private set; }

        protected override void OnLoad(EventArgs e)
        {
            base.OnLoad(e);
            var first = _startUnlicensed ? elementLicense : elementDashboard;
            accordionControl.SelectedElement = first;
            ShowModule(first);

            // Servis tarafındaki süreyle aynı (girişte gelir): biri önce dolup diğerini şaşırtmasın
            var idleLimit = TimeSpan.FromMinutes(_client.IdleTimeoutMinutes);
            _idle = new IdleWatcher(idleLimit);
            _idle.KeepAlive += async (s, a) =>
            {
                var result = await _client.PingAsync();
                if (result.IsUnauthorized)
                    EndSession(MainExitReason.SessionExpired, "Oturum süresi doldu, lütfen tekrar giriş yapın.");
            };
            _idle.IdleTimeout += async (s, a) =>
            {
                await _client.LogoutAsync();
                EndSession(MainExitReason.SessionExpired, (int)idleLimit.TotalMinutes + " dakika işlem yapılmadığı için oturum kapatıldı.");
            };
        }

        /// <summary>Modüller servis cevabında Unauthorized alınca bunu çağırır.</summary>
        public void HandleSessionExpired()
        {
            EndSession(MainExitReason.SessionExpired, "Oturum süresi doldu, lütfen tekrar giriş yapın.");
        }

        public void ReturnToStart(string message)
        {
            EndSession(MainExitReason.ServiceStopped, message);
        }

        public void ExitApplication()
        {
            EndSession(MainExitReason.Closed, null);
        }

        public void HandleUnlicensed(string message)
        {
            if (accordionControl.SelectedElement != elementLicense)
            {
                accordionControl.SelectedElement = elementLicense;
                ShowModule(elementLicense);
            }
            // Bir sayfa birden çok çağrı yapabilir: uyarı bir kez
            if (DateTime.UtcNow - _unlicensedShown < TimeSpan.FromSeconds(10)) return;
            _unlicensedShown = DateTime.UtcNow;
            Ui.Warning(this, message);
        }

        public void LicenseActivated()
        {
            // Lisanssız açıldıysa ilk kurulum yapılmamış olabilir: akış baştan (oturum korunur)
            if (_startUnlicensed) EndSession(MainExitReason.Reload, null);
        }

        public void Navigate(string page)
        {
            AccordionControlElement element;
            switch (page)
            {
                case DashboardPages.EventLog: element = elementEventLog; break;
                case DashboardPages.BanList: element = elementBanList; break;
                case DashboardPages.Security: element = elementSecurityChecks; break;
                case DashboardPages.Performance: element = elementPerformance; break;
                case DashboardPages.Backup: element = elementBackup; break;
                case DashboardPages.Maintenance: element = elementMaintenance; break;
                case DashboardPages.Mail: element = elementMail; break;
                case DashboardPages.SqlConnection: element = elementSqlConnection; break;
                case DashboardPages.Reports: element = elementReports; break;
                case DashboardPages.PortRestrictions: element = elementPortRestrictions; break;
                case DashboardPages.License: element = elementLicense; break;
                default: return;
            }
            accordionControl.SelectedElement = element;
            ShowModule(element);
        }

        private void EndSession(MainExitReason reason, string message)
        {
            if (_exiting) return;
            _exiting = true;
            ExitReason = reason;
            ExitMessage = message;
            Close();
        }

        private void accordionControl_ElementClick(object sender, ElementClickEventArgs e)
        {
            if (e.Element.Style == ElementStyle.Item)
                ShowModule(e.Element);
        }

        /// <summary>
        /// Menüde seçilen modülü gösterir. Modüller ilk açılışta oluşturulur ve saklanır:
        /// sayfalar arasında gezinirken filtreler / seçimler kaybolmaz.
        /// </summary>
        private void ShowModule(AccordionControlElement element)
        {
            Control module;
            if (!_modules.TryGetValue(element, out module))
            {
                module = CreateModule(element);
                module.Dock = DockStyle.Fill;
                var real = module as ModuleBase;
                if (real != null) real.Attach(this, _client);
                _modules[element] = module;
            }

            container.SuspendLayout();
            container.Controls.Clear(); // modül yok edilmez, sadece görünümden çıkar
            container.Controls.Add(module);
            container.ResumeLayout();

            var activated = module as ModuleBase;
            if (activated != null) activated.Activate();
        }

        private Control CreateModule(AccordionControlElement element)
        {
            if (element == elementDashboard) return new DashboardModule();
            if (element == elementEventLog) return new EventLogModule();
            if (element == elementAuditTrail) return new AuditTrailModule();
            if (element == elementWhitelist) return new WhitelistModule();
            if (element == elementPortRestrictions) return new PortRestrictionsModule();
            if (element == elementCanary) return new CanaryModule();
            if (element == elementOpenPorts) return new OpenPortsModule();
            if (element == elementBanList) return new BanListModule();
            if (element == elementRules) return new RulesModule();
            if (element == elementMail) return new MailModule();
            if (element == elementService) return new ServiceModule();
            if (element == elementSqlConnection) return new SqlModule();
            if (element == elementSqlite) return new SqliteModule();
            if (element == elementSettings) return new GeneralSettingsModule();
            if (element == elementMaintenance) return new SqlRequiredModule(() => new MaintenanceModule(), "SQL Bakımı");
            if (element == elementBackup) return new SqlRequiredModule(() => new BackupModule(), "SQL Yedekleme");
            if (element == elementReports) return new ReportsModule();
            if (element == elementRdpSessions) return new RdpSessionsModule();
            if (element == elementServiceWatch) return new ServiceWatchModule();
            if (element == elementLicense) return new LicenseModule();
            if (element == elementPerformance) return new ChecksModule(CheckScopes.Performance, "Performans Önerileri");
            if (element == elementSecurityChecks) return new ChecksModule(CheckScopes.Security, "Güvenlik Önerileri");
            return new PlaceholderModule(element.Text);
        }

        private async void barButtonLogout_ItemClick(object sender, DevExpress.XtraBars.ItemClickEventArgs e)
        {
            if (!Ui.Confirm(this, "Çıkış yapılsın mı?")) return;
            await _client.LogoutAsync();
            EndSession(MainExitReason.Logout, null);
        }

        protected override void OnFormClosing(FormClosingEventArgs e)
        {
            ThemeManager.Flush();
            base.OnFormClosing(e);
        }

        protected override void OnFormClosed(FormClosedEventArgs e)
        {
            if (_idle != null) _idle.Dispose();
            foreach (var module in _modules.Values) module.Dispose();
            _modules.Clear();
            base.OnFormClosed(e);
        }
    }
}
