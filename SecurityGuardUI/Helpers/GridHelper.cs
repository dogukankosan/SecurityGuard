using System;
using System.Diagnostics;
using System.IO;
using System.Windows.Forms;
using DevExpress.XtraGrid;
using DevExpress.XtraGrid.Views.Grid;

namespace SecurityGuardUI.Helpers
{
    /// <summary>Tüm liste ekranlarındaki tabloların ortak ayarları ve Excel'e aktarma.</summary>
    internal static class GridHelper
    {
        /// <summary>Salt okunur liste: sütun filtre satırı, sıralama, çoklu seçim (isteğe bağlı).</summary>
        public static void SetupReadOnly(GridView view, bool multiSelect = false)
        {
            view.OptionsBehavior.Editable = false;
            view.OptionsBehavior.ReadOnly = true;
            view.OptionsView.ShowGroupPanel = false;
            view.OptionsView.ShowAutoFilterRow = true;
            view.OptionsView.ShowIndicator = false;
            view.OptionsView.ColumnAutoWidth = true;
            view.OptionsView.EnableAppearanceEvenRow = true;
            view.OptionsSelection.EnableAppearanceFocusedCell = false;
            view.FocusRectStyle = DrawFocusRectStyle.RowFocus;
            view.OptionsSelection.MultiSelect = multiSelect;
            if (multiSelect)
                view.OptionsSelection.MultiSelectMode = GridMultiSelectMode.RowSelect;
        }

        /// <summary>Tabloyu (görünen sütunlar, uygulanan filtre ve sıralamayla) .xlsx dosyasına aktarır.</summary>
        public static void ExportToExcel(IWin32Window owner, GridControl grid, string fileBaseName)
        {
            using (var dialog = new SaveFileDialog
            {
                Filter = "Excel dosyası (*.xlsx)|*.xlsx",
                FileName = fileBaseName + "_" + DateTime.Now.ToString("yyyyMMdd_HHmm") + ".xlsx",
                OverwritePrompt = true
            })
            {
                if (dialog.ShowDialog(owner) != DialogResult.OK) return;
                try
                {
                    grid.ExportToXlsx(dialog.FileName);
                }
                catch (IOException)
                {
                    Ui.Error(owner, "Dosya yazılamadı. Dosya Excel'de açıksa kapatıp tekrar deneyin.");
                    return;
                }
                catch (UnauthorizedAccessException)
                {
                    Ui.Error(owner, "Bu klasöre yazma izniniz yok. Başka bir klasör seçin.");
                    return;
                }

                if (!Ui.Confirm(owner, "Dışa aktarıldı:" + Environment.NewLine + dialog.FileName + Environment.NewLine + Environment.NewLine + "Dosya açılsın mı?"))
                    return;
                try
                {
                    Process.Start(new ProcessStartInfo(dialog.FileName) { UseShellExecute = true });
                }
                catch (Exception ex)
                {
                    Ui.Error(owner, "Dosya açılamadı (Excel kurulu olmayabilir):" + Environment.NewLine + ex.Message);
                }
            }
        }
    }
}
