using System;
using System.Drawing;
using System.Windows.Forms;
using DevExpress.Utils;
using DevExpress.XtraEditors;

namespace SecurityGuardUI.Modules
{
    /// <summary>Henüz yazılmamış modüller için yer tutucu.</summary>
    internal sealed class PlaceholderModule : XtraUserControl
    {
        public PlaceholderModule(string title)
        {
            var label = new LabelControl
            {
                Text = title + Environment.NewLine + Environment.NewLine + "Bu bölüm sonraki adımlarda eklenecek.",
                AutoSizeMode = LabelAutoSizeMode.None,
                Dock = DockStyle.Fill
            };
            label.Appearance.Font = new Font("Segoe UI", 12F);
            label.Appearance.TextOptions.HAlignment = HorzAlignment.Center;
            label.Appearance.TextOptions.VAlignment = VertAlignment.Center;
            label.Appearance.TextOptions.WordWrap = WordWrap.Wrap;
            Controls.Add(label);
        }
    }
}
