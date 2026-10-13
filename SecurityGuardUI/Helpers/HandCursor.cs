using System.Windows.Forms;
using DevExpress.XtraEditors;

namespace SecurityGuardUI.Helpers
{
    /// <summary>
    /// Butonlarda el imleci. Pencereye bir kez uygulanır; içindeki tüm butonlar ve sonradan eklenen
    /// kontroller (ör. ana formda açılan modüller) otomatik kapsanır.
    /// </summary>
    internal static class HandCursor
    {
        public static void Apply(Control root)
        {
            if (root == null) return;

            if (root is SimpleButton || root is CheckButton)
                root.Cursor = Cursors.Hand;

            foreach (Control child in root.Controls)
                Apply(child);

            // Sonradan eklenenler (modüller, dinamik kontroller); aynı kontrole iki kez bağlanmasın
            root.ControlAdded -= OnControlAdded;
            root.ControlAdded += OnControlAdded;
        }

        private static void OnControlAdded(object sender, ControlEventArgs e)
        {
            Apply(e.Control);
        }
    }
}
