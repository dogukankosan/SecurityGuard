using DevExpress.XtraEditors.Controls;
using DevExpress.XtraGrid.Localization;

namespace SecurityGuardUI.Helpers
{
    /// <summary>DevExpress'in kendi metinlerinden ekranda görünenlerin Türkçesi (mesaj kutusu, tarih seçici).</summary>
    internal sealed class TurkishLocalizer : Localizer
    {
        public override string GetLocalizedString(StringId id)
        {
            switch (id)
            {
                case StringId.XtraMessageBoxOkButtonText: return "Tamam";
                case StringId.XtraMessageBoxCancelButtonText: return "İptal";
                case StringId.XtraMessageBoxYesButtonText: return "Evet";
                case StringId.XtraMessageBoxNoButtonText: return "Hayır";
                case StringId.XtraMessageBoxRetryButtonText: return "Tekrar Dene";
                case StringId.XtraMessageBoxAbortButtonText: return "Durdur";
                case StringId.XtraMessageBoxIgnoreButtonText: return "Yoksay";
                case StringId.DateEditToday: return "Bugün";
                case StringId.DateEditClear: return "Temizle";
                default: return base.GetLocalizedString(id);
            }
        }
    }

    /// <summary>Tablo (grid) menü ve arama metinlerinin Türkçesi.</summary>
    internal sealed class TurkishGridLocalizer : GridLocalizer
    {
        public override string GetLocalizedString(GridStringId id)
        {
            switch (id)
            {
                case GridStringId.FindControlFindButton: return "Bul";
                case GridStringId.FindControlClearButton: return "Temizle";
                case GridStringId.FindNullPrompt: return "Aramak için yazın...";
                case GridStringId.MenuColumnSortAscending: return "Artan sırala";
                case GridStringId.MenuColumnSortDescending: return "Azalan sırala";
                case GridStringId.MenuColumnClearSorting: return "Sıralamayı kaldır";
                case GridStringId.MenuColumnBestFit: return "En uygun genişlik";
                case GridStringId.MenuColumnBestFitAllColumns: return "Tüm sütunlar en uygun genişlik";
                case GridStringId.MenuColumnColumnCustomization: return "Sütun seçici";
                case GridStringId.MenuColumnFilterEditor: return "Filtre düzenleyici...";
                case GridStringId.MenuColumnAutoFilterRowShow: return "Filtre satırını göster";
                case GridStringId.MenuColumnAutoFilterRowHide: return "Filtre satırını gizle";
                case GridStringId.MenuColumnFindFilterShow: return "Arama kutusunu göster";
                case GridStringId.MenuColumnFindFilterHide: return "Arama kutusunu gizle";
                case GridStringId.MenuColumnClearFilter: return "Filtreyi temizle";
                default: return base.GetLocalizedString(id);
            }
        }
    }
}
