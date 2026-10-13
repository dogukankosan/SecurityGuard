using System.Windows.Forms;
using DevExpress.XtraEditors;

namespace SecurityGuardUI.Helpers
{
    /// <summary>Tüm ekranlarda aynı görünen mesaj kutuları (butonlarda el imleci dahil).</summary>
    internal static class Ui
    {
        public const string Title = "SecurityGuard";

        public static void Error(IWin32Window owner, string message)
        {
            Show(owner, message, MessageBoxButtons.OK, MessageBoxIcon.Error);
        }

        public static void Warning(IWin32Window owner, string message)
        {
            Show(owner, message, MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }

        public static void Info(IWin32Window owner, string message)
        {
            Show(owner, message, MessageBoxButtons.OK, MessageBoxIcon.Information);
        }

        public static bool Confirm(IWin32Window owner, string message)
        {
            return Show(owner, message, MessageBoxButtons.YesNo, MessageBoxIcon.Question) == DialogResult.Yes;
        }

        /// <summary>Evet / Hayır / İptal (Yes, No, Cancel döner).</summary>
        public static DialogResult YesNoCancel(IWin32Window owner, string message)
        {
            return Show(owner, message, MessageBoxButtons.YesNoCancel, MessageBoxIcon.Question);
        }

        public static bool RetryOrCancel(IWin32Window owner, string message)
        {
            return Show(owner, message, MessageBoxButtons.RetryCancel, MessageBoxIcon.Warning) == DialogResult.Retry;
        }

        private static DialogResult Show(IWin32Window owner, string message, MessageBoxButtons buttons, MessageBoxIcon icon)
        {
            var args = new XtraMessageBoxArgs
            {
                Owner = owner,
                Text = message,
                Caption = Title,
                Buttons = ToButtons(buttons),
                Icon = ToIcon(icon)
            };
            args.Showing += (s, e) =>
            {
                e.Form.Icon = AppImages.Icon;
                HandCursor.Apply(e.Form);
            };
            return XtraMessageBox.Show(args);
        }

        private static DialogResult[] ToButtons(MessageBoxButtons buttons)
        {
            switch (buttons)
            {
                case MessageBoxButtons.YesNo: return new[] { DialogResult.Yes, DialogResult.No };
                case MessageBoxButtons.RetryCancel: return new[] { DialogResult.Retry, DialogResult.Cancel };
                case MessageBoxButtons.OKCancel: return new[] { DialogResult.OK, DialogResult.Cancel };
                case MessageBoxButtons.YesNoCancel: return new[] { DialogResult.Yes, DialogResult.No, DialogResult.Cancel };
                default: return new[] { DialogResult.OK };
            }
        }

        private static System.Drawing.Icon ToIcon(MessageBoxIcon icon)
        {
            switch (icon)
            {
                case MessageBoxIcon.Error: return System.Drawing.SystemIcons.Error;
                case MessageBoxIcon.Warning: return System.Drawing.SystemIcons.Warning;
                case MessageBoxIcon.Question: return System.Drawing.SystemIcons.Question;
                case MessageBoxIcon.Information: return System.Drawing.SystemIcons.Information;
                default: return null;
            }
        }
    }
}
