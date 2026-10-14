using System;
using System.Windows.Forms;
using DevExpress.LookAndFeel;
using DevExpress.Skins;

namespace SecurityGuardUpdater
{
    internal static class Program
    {
        [STAThread]
        private static void Main()
        {
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            try
            {
                SkinManager.EnableFormSkins();
                UserLookAndFeel.Default.SetSkinStyle("Office 2019 Colorful");
            }
            catch { }
            Application.Run(new MainForm());
        }
    }
}
