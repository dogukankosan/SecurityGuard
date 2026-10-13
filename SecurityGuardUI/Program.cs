using System;
using System.Globalization;
using System.Threading;
using System.Windows.Forms;
using DevExpress.XtraEditors.Controls;
using SecurityGuardUI.Helpers;
using SecurityGuardUI.Ipc;
using SecurityGuardUI.Setup;

namespace SecurityGuardUI
{
    static class Program
    {
        [STAThread]
        static int Main(string[] args)
        {
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);

            var turkish = new CultureInfo("tr-TR");
            Thread.CurrentThread.CurrentCulture = turkish;
            Thread.CurrentThread.CurrentUICulture = turkish;
            CultureInfo.DefaultThreadCurrentCulture = turkish;
            CultureInfo.DefaultThreadCurrentUICulture = turkish;
            Localizer.Active = new TurkishLocalizer();
            DevExpress.XtraGrid.Localization.GridLocalizer.Active = new TurkishGridLocalizer();

            // Kurulum paketinin (Inno Setup) çağırdığı komut satırı modu (ör. Denetim Masası'ndan kaldırma).
            if (CliSetup.IsCliCommand(args))
            {
                ThemeManager.Initialize();
                try
                {
                    return CliSetup.Run(args);
                }
                catch (Exception ex)
                {
                    MessageBox.Show("Kaldırma sırasında hata: " + ex.Message, Ui.Title, MessageBoxButtons.OK, MessageBoxIcon.Error);
                    return CliSetup.Cancelled;
                }
            }

            // Aynı anda tek arayüz
            bool createdNew;
            using (new Mutex(true, @"Global\SecurityGuardUI", out createdNew))
            {
                if (!createdNew)
                {
                    MessageBox.Show("SecurityGuard arayüzü zaten açık.", Ui.Title, MessageBoxButtons.OK, MessageBoxIcon.Information);
                    return 0;
                }

                // Beklenmeyen hata programı kapatmaz; kullanıcıya gösterilir
                Application.SetUnhandledExceptionMode(UnhandledExceptionMode.CatchException);
                Application.ThreadException += (s, e) =>
                    Ui.Error(null, "Beklenmeyen bir hata oluştu:" + Environment.NewLine + e.Exception.Message);
                AppDomain.CurrentDomain.UnhandledException += (s, e) =>
                    MessageBox.Show("Beklenmeyen bir hata oluştu:" + Environment.NewLine + e.ExceptionObject,
                        Ui.Title, MessageBoxButtons.OK, MessageBoxIcon.Error);

                using (var client = new GuardClient())
                    new AppFlow(client).Run();
            }

            return 0;
        }
    }
}
