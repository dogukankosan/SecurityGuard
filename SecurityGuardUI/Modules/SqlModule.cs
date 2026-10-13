using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Forms;
using DevExpress.XtraGrid.Views.Base;
using SecurityGuard.Shared;
using SecurityGuard.Shared.Contracts;
using SecurityGuard.Shared.Dto;
using SecurityGuardUI.Helpers;

namespace SecurityGuardUI.Modules
{
    /// <summary>
    /// SQL Server Bağlantısı: sunucudaki instance'lar otomatik bulunur; her biri için login bilgisi girilir.
    /// Kural: aynı bilgilerle başarılı bağlantı testi yapılmadan kaydedilemez (servis de kontrol eder).
    /// </summary>
    public partial class SqlModule : ModuleBase
    {
        private List<InstanceRow> _rows = new List<InstanceRow>();
        private InstanceRow _current;
        private string _testedKey;
        private bool _binding;

        public SqlModule()
        {
            InitializeComponent();
            GridHelper.SetupReadOnly(gridView);
            gridView.OptionsView.ShowAutoFilterRow = false;
            memoScript.Text = LoginScript();
            textLogin.Text = GuardConstants.SqlLoginName; // sabit, değiştirilemez (servis de sadece bu login'i kabul eder)

            EventHandler changed = (s, e) => { if (!_binding) UpdateSaveState(); };
            textServer.EditValueChanged += (s, e) => UpdateServerHint();
            textServer.EditValueChanged += changed;
            spinPort.EditValueChanged += changed;
            textPassword.EditValueChanged += changed;
            UpdateSaveState();
        }

        protected override Task OnFirstShowAsync()
        {
            return LoadAsync(null);
        }

        private async Task LoadAsync(string selectInstance)
        {
            var list = await CallAsync(c => c.SendAsync<List<SqlInstanceDto>>(Commands.SqlInstances));
            if (list == null) return;
            Bind(list, selectInstance);
        }

        private void Bind(List<SqlInstanceDto> list, string selectInstance)
        {
            _rows = list.Select(i => new InstanceRow(i)).ToList();
            gridControl.DataSource = _rows;
            labelEmpty.Visible = _rows.Count == 0;
            panelForm.Enabled = _rows.Count > 0;

            var target = _rows.FirstOrDefault(r => string.Equals(r.InstanceName, selectInstance, StringComparison.OrdinalIgnoreCase))
                         ?? _rows.FirstOrDefault(r => r.Dto.IsConfigured) ?? _rows.FirstOrDefault();
            if (target != null)
            {
                int handle = gridView.GetRowHandle(_rows.IndexOf(target));
                gridView.FocusedRowHandle = handle;
                ShowInstance(target);
            }
        }

        private void gridView_FocusedRowChanged(object sender, FocusedRowChangedEventArgs e)
        {
            var row = gridView.GetRow(e.FocusedRowHandle) as InstanceRow;
            if (row != null && row != _current) ShowInstance(row);
        }

        private void ShowInstance(InstanceRow row)
        {
            _current = row;
            var i = row.Dto;
            _binding = true;
            try
            {
                labelFormTitle.Text = "Bağlantı: " + row.DisplayName;
                textServer.Text = i.IsConfigured ? i.Server : i.SuggestedServer;
                spinPort.Value = i.IsConfigured ? i.Port : 0;
                textPassword.Text = "";
                textPassword.Properties.NullValuePrompt = i.HasPassword ? "Kayıtlı şifre korunur; değiştirmek için yazın" : "SQL login şifresi";
                checkEnabled.Checked = !i.IsConfigured || i.IsEnabled;
            }
            finally
            {
                _binding = false;
            }

            _testedKey = null;
            UpdateSaveState();

            if (!i.Detected)
                ShowResult(false, "Bu instance kayıtlı ama sunucuda artık bulunamadı (kaldırılmış olabilir).");
            else if (i.ServiceState == "Durmuş")
                ShowResult(false, "Bu instance'ın SQL Server servisi durmuş. Bağlantı testi için önce SQL Server servisini başlatın.");
            else
                ShowResult(null, i.IsConfigured ? "Bağlantı kayıtlı. Bilgileri değiştirmek için önce test edin." : "Bağlantı henüz tanımlanmamış.");

            ShowAudit(i);
        }

        // ================================================================ SQL izleme (Audit)

        private void ShowAudit(SqlInstanceDto i)
        {
            bool usable = i.IsConfigured && i.IsEnabled;
            buttonAuditInstall.Enabled = usable;
            buttonAuditInstall.Text = i.AuditInstalled ? "İzlemeyi Onar" : "İzlemeyi Kur";
            buttonAuditUninstall.Enabled = usable && i.AuditInstalled;

            if (!usable)
            {
                labelAuditStatus.Text = "Kurmak için önce bu instance'ın bağlantısını kaydedin ve izlemeyi açın.";
                labelAuditStatus.Appearance.ForeColor = UiColors.Text;
            }
            else if (!i.AuditInstalled)
            {
                labelAuditStatus.Text = "Kurulu değil: SQL login, yetki, sertifika, TDE ve veritabanı değişiklikleri yakalanmaz. " +
                                        "Kurulum SQL Server'da sadece SecurityGuard_Audit nesnesini oluşturur (Application log'a yazar).";
                labelAuditStatus.Appearance.ForeColor = UiColors.Warning;
            }
            else
            {
                labelAuditStatus.Text = "Durum: " + (i.AuditStatus ?? "bilinmiyor");
                labelAuditStatus.Appearance.ForeColor = i.AuditRunning ? UiColors.Success : UiColors.Error;
            }
        }

        private async void buttonAuditInstall_Click(object sender, EventArgs e)
        {
            if (_current == null) return;
            string instance = _current.InstanceName;
            if (!Ui.Confirm(FindForm(),
                instance + " üzerinde SecurityGuard SQL izlemesi kurulsun mu?" + Environment.NewLine + Environment.NewLine +
                "Oluşturulacak: SecurityGuard_Audit (SQL Server Audit → Windows Application log) ve SecurityGuard_AuditSpec " +
                "(login, şifre, rol, yetki, veritabanı, sertifika / anahtar, sunucu nesnesi ve ayar değişiklikleri)." + Environment.NewLine +
                "Başka hiçbir SQL ayarı değişmez. İsterseniz \"Audit Scriptini Kopyala\" ile SSMS'te kendiniz de çalıştırabilirsiniz.")) return;

            var list = await CallAsync(c => c.SendAsync<List<SqlInstanceDto>>(Commands.SqlAuditInstall, new SqlInstanceRequest { InstanceName = instance }, TimeSpan.FromMinutes(2)));
            if (list == null) return;
            Bind(list, instance);
            Ui.Info(FindForm(), instance + ": SQL izleme kuruldu.");
        }

        private async void buttonAuditUninstall_Click(object sender, EventArgs e)
        {
            if (_current == null) return;
            string instance = _current.InstanceName;
            if (!Ui.Confirm(FindForm(),
                instance + " üzerindeki SecurityGuard SQL izlemesi kaldırılsın mı?" + Environment.NewLine + Environment.NewLine +
                "Kaldırılırsa SQL login, yetki, sertifika ve veritabanı değişiklikleri artık yakalanmaz.")) return;

            var list = await CallAsync(c => c.SendAsync<List<SqlInstanceDto>>(Commands.SqlAuditUninstall, new SqlInstanceRequest { InstanceName = instance }, TimeSpan.FromMinutes(2)));
            if (list == null) return;
            Bind(list, instance);
        }

        private async void buttonAuditScript_Click(object sender, EventArgs e)
        {
            var script = await CallAsync(c => c.SendAsync<string>(Commands.SqlAuditScript));
            if (script == null) return;
            try
            {
                Clipboard.SetText(script);
                Ui.Info(FindForm(), "Audit kurulum scripti panoya kopyalandı. SSMS'te çalıştırdıktan sonra \"İzlemeyi Onar\" ile SecurityGuard'a kurulu olarak tanıtın.");
            }
            catch (Exception ex)
            {
                Ui.Error(FindForm(), "Panoya kopyalanamadı: " + ex.Message);
            }
        }

        // ================================================================ Test / kayıt

        private SqlConnectionRequest ReadForm()
        {
            return new SqlConnectionRequest
            {
                InstanceName = _current == null ? null : _current.InstanceName,
                Server = textServer.Text.Trim(),
                Port = (int)spinPort.Value,
                Login = GuardConstants.SqlLoginName,
                Password = textPassword.Text,
                IsEnabled = checkEnabled.Checked
            };
        }

        /// <summary>Bağlantıyı etkileyen alanlar (izleme açık / kapalı hariç).</summary>
        private string CurrentKey()
        {
            var r = ReadForm();
            return string.Join("\u0001", r.InstanceName, r.Server.ToLowerInvariant(), r.Port, r.Login, r.Password);
        }

        /// <summary>
        /// Sunucu alanına makine adı / IP yazılırsa uyarır: makine adı veya IP değişince bağlantı kopar.
        /// SecurityGuard SQL'e hep aynı sunucudan bağlandığı için "localhost" her zaman doğrudur.
        /// </summary>
        private void UpdateServerHint()
        {
            string host = (textServer.Text ?? "").Trim().Split('\\')[0].Split(',')[0].Trim();
            bool local = host.Length == 0 || host == "." || host.Equals("localhost", StringComparison.OrdinalIgnoreCase)
                         || host.Equals("(local)", StringComparison.OrdinalIgnoreCase) || host == "127.0.0.1" || host == "::1";
            if (local)
            {
                labelServerHint.Text = "";
                return;
            }

            bool machineName = host.Equals(Environment.MachineName, StringComparison.OrdinalIgnoreCase);
            System.Net.IPAddress ip;
            bool isIp = System.Net.IPAddress.TryParse(host, out ip);
            labelServerHint.Text = machineName
                ? "Makine adı yerine \"localhost\" yazın: makine adı değişirse bağlantı kopar."
                : isIp
                    ? "IP yerine \"localhost\" yazın: sunucunun IP'si değişirse bağlantı kopar."
                    : "SecurityGuard SQL'e aynı sunucudan bağlanır; \"localhost\" önerilir.";
            labelServerHint.Appearance.ForeColor = UiColors.Warning;
        }

        private void UpdateSaveState()
        {
            bool tested = _testedKey != null && _testedKey == CurrentKey();
            buttonSave.Enabled = tested;
            labelSaveHint.Text = tested ? "Test başarılı; kaydedebilirsiniz." : "Kaydetmek için önce bağlantıyı test edin.";
            labelSaveHint.Appearance.ForeColor = tested ? UiColors.Success : UiColors.Text;
        }

        private async void buttonTest_Click(object sender, EventArgs e)
        {
            if (_current == null) return;
            string key = CurrentKey();
            ShowResult(null, "Bağlanılıyor...");

            var result = await CallQuietAsync(c => c.SendAsync<SqlTestResultDto>(Commands.SqlTest, ReadForm(), TimeSpan.FromSeconds(60)));
            if (result.IsUnauthorized) return;

            if (!result.Success)
            {
                _testedKey = null;
                UpdateSaveState();
                ShowResult(false, result.ErrorMessage);
                return;
            }

            var t = result.Value;
            _testedKey = key;
            UpdateSaveState();

            string text = "Bağlantı başarılı: " + t.VersionName + " " + t.Edition + " (" + t.ProductVersion + ", " + t.ProductLevel + ")"
                          + Environment.NewLine + "Sunucu: " + t.ServerName + "   ·   Yetki: tam yetkili (sysadmin)";
            if (t.Warnings != null && t.Warnings.Count > 0)
            {
                ShowResult(null, text + Environment.NewLine + "Uyarı: " + string.Join(" ", t.Warnings));
                labelResult.Appearance.ForeColor = UiColors.Warning;
            }
            else
            {
                ShowResult(true, text);
            }
        }

        private async void buttonSave_Click(object sender, EventArgs e)
        {
            if (_current == null) return;
            string instance = _current.InstanceName;
            var list = await CallAsync(c => c.SendAsync<List<SqlInstanceDto>>(Commands.SqlSave, ReadForm()));
            if (list == null) return;

            Bind(list, instance);
            Ui.Info(FindForm(), instance + " bağlantısı kaydedildi.");
        }

        private async void buttonRescan_Click(object sender, EventArgs e)
        {
            await LoadAsync(_current == null ? null : _current.InstanceName);
        }

        /// <param name="success">null = bilgi / devam ediyor</param>
        private void ShowResult(bool? success, string message)
        {
            labelResult.Text = message;
            labelResult.Appearance.ForeColor = success == null ? UiColors.Text : success.Value ? UiColors.Success : UiColors.Error;
        }

        // ================================================================ Login scripti

        private void buttonCopyScript_Click(object sender, EventArgs e)
        {
            try
            {
                Clipboard.SetText(memoScript.Text);
                Ui.Info(FindForm(), "Script panoya kopyalandı. SSMS'te yapıştırıp şifreyi değiştirdikten sonra çalıştırın.");
            }
            catch (Exception ex)
            {
                Ui.Error(FindForm(), "Panoya kopyalanamadı: " + ex.Message);
            }
        }

        private static string LoginScript()
        {
            string login = GuardConstants.SqlLoginName;
            return
                "-- SecurityGuard için tam yetkili SQL login. SSMS'te çalıştırın; şifreyi mutlaka değiştirin." + Environment.NewLine +
                "-- SQL Server karma kimlik doğrulama modunda olmalı (SQL Server and Windows Authentication mode)." + Environment.NewLine +
                "USE [master];" + Environment.NewLine +
                "CREATE LOGIN [" + login + "] WITH PASSWORD = N'BURAYA_GUCLU_BIR_SIFRE', CHECK_POLICY = ON, CHECK_EXPIRATION = OFF;" + Environment.NewLine +
                "ALTER SERVER ROLE [sysadmin] ADD MEMBER [" + login + "];";
        }

        // ================================================================ Satır

        public sealed class InstanceRow
        {
            public InstanceRow(SqlInstanceDto dto)
            {
                Dto = dto;
            }

            internal SqlInstanceDto Dto { get; private set; }

            public string InstanceName { get { return Dto.InstanceName; } }

            public string DisplayName
            {
                get { return string.Equals(Dto.InstanceName, "MSSQLSERVER", StringComparison.OrdinalIgnoreCase) ? "MSSQLSERVER (varsayılan)" : Dto.InstanceName; }
            }

            public string Version { get { return Dto.ProductVersion; } }
            public string Edition { get { return Dto.Edition; } }
            public string ServiceState { get { return Dto.ServiceState; } }
            public string PortText { get { return Dto.TcpPort > 0 ? Dto.TcpPort.ToString() : "-"; } }

            public string AuditText
            {
                get
                {
                    if (!Dto.AuditInstalled) return Dto.AuditStatus != null && Dto.AuditStatus.Contains("dışında") ? Dto.AuditStatus : "Kurulu değil";
                    return Dto.AuditStatus ?? "Kontrol bekleniyor";
                }
            }

            public string ConnectionText
            {
                get
                {
                    if (!Dto.IsConfigured) return "Tanımlı değil";
                    return "Tanımlı · " + Dto.Login + " · izleme " + (Dto.IsEnabled ? "açık" : "kapalı");
                }
            }
        }
    }
}
