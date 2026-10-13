using System;
using System.Collections.Generic;
using System.Drawing;
using System.Globalization;
using System.Linq;
using System.Threading.Tasks;
using DevExpress.XtraEditors.Controls;
using DevExpress.XtraEditors.Repository;
using DevExpress.XtraGrid.Views.Base;
using DevExpress.XtraGrid.Views.Grid;
using SecurityGuard.Shared.Contracts;
using SecurityGuard.Shared.Dto;
using SecurityGuardUI.Helpers;

namespace SecurityGuardUI.Modules
{
    /// <summary>
    /// Genel Ayarlar: servisin genel parametreleri (giriş güvenliği, rapor saati, SQL izleme, mail toplama, log, disk).
    /// Ayar listesi, açıklamalar, varsayılanlar ve sınırlar servisten gelir; bu ekran sadece düzenler.
    /// Değişen satırlar kalın yazılır; Kaydet sadece değişenleri gönderir.
    /// </summary>
    public partial class GeneralSettingsModule : ModuleBase
    {
        private List<SettingRow> _rows = new List<SettingRow>();
        private readonly Dictionary<string, RepositoryItem> _editors = new Dictionary<string, RepositoryItem>();
        private readonly RepositoryItemCheckEdit _checkEdit = new RepositoryItemCheckEdit();
        private readonly RepositoryItemTimeEdit _timeEdit = new RepositoryItemTimeEdit();
        private readonly RepositoryItemMemoEdit _wrapText = new RepositoryItemMemoEdit();
        private Font _boldFont;

        public GeneralSettingsModule()
        {
            InitializeComponent();

            gridView.OptionsView.ShowGroupPanel = false;
            gridView.OptionsView.ShowIndicator = false;
            gridView.OptionsView.RowAutoHeight = true;
            gridView.OptionsView.ShowHorizontalLines = DevExpress.Utils.DefaultBoolean.True;
            gridView.OptionsBehavior.AutoExpandAllGroups = true;
            gridView.OptionsBehavior.EditorShowMode = DevExpress.Utils.EditorShowMode.MouseDown;
            gridView.OptionsCustomization.AllowSort = false;
            gridView.OptionsCustomization.AllowGroup = false;
            gridView.OptionsSelection.EnableAppearanceFocusedCell = false;
            gridView.OptionsMenu.EnableGroupPanelMenu = false;
            gridView.GroupFormat = "{1}"; // sadece grup adı

            foreach (DevExpress.XtraGrid.Columns.GridColumn c in gridView.Columns)
                c.OptionsColumn.AllowEdit = c == colValue;

            _checkEdit.NullStyle = StyleIndeterminate.Unchecked;
            _timeEdit.Mask.EditMask = "HH:mm";
            _timeEdit.Mask.UseMaskAsDisplayFormat = true;
            _wrapText.WordWrap = true;
            _wrapText.ReadOnly = true;
            _wrapText.AutoHeight = true;
            gridControl.RepositoryItems.AddRange(new RepositoryItem[] { _checkEdit, _timeEdit, _wrapText });
            colDescription.ColumnEdit = _wrapText;

            UpdateButtons();
        }

        protected override Task OnFirstShowAsync()
        {
            return LoadAsync();
        }

        private async Task LoadAsync()
        {
            var list = await CallAsync(c => c.SendAsync<List<GeneralSettingDto>>(Commands.GeneralSettingsList));
            if (list != null) Bind(list);
        }

        private void Bind(List<GeneralSettingDto> list)
        {
            _rows = list.Select(d => new SettingRow(d)).ToList();
            _editors.Clear();
            gridControl.DataSource = _rows;
            gridView.ExpandAllGroups();
            if (_rows.Count > 0) gridView.FocusedRowHandle = 0; // grup satırı değil, ilk ayar
            UpdateButtons();
        }

        /// <summary>Gruplar alfabetik değil, servisin verdiği sırada (giriş güvenliği en üstte).</summary>
        private void gridView_CustomColumnSort(object sender, CustomColumnSortEventArgs e)
        {
            if (e.Column != colGroup) return;
            e.Result = GroupOrder(e.Value1 as string).CompareTo(GroupOrder(e.Value2 as string));
            e.Handled = true;
        }

        private int GroupOrder(string group)
        {
            return _rows.FindIndex(r => r.Group == group);
        }

        // ================================================================ Düzenleyiciler

        private void gridView_CustomRowCellEdit(object sender, CustomRowCellEditEventArgs e)
        {
            if (e.Column != colValue) return;
            var row = gridView.GetRow(e.RowHandle) as SettingRow;
            if (row != null) e.RepositoryItem = EditorFor(row.Dto);
        }

        private RepositoryItem EditorFor(GeneralSettingDto d)
        {
            switch (d.Kind)
            {
                case "bool": return _checkEdit;
                case "time": return _timeEdit;
            }

            RepositoryItem item;
            if (_editors.TryGetValue(d.Key, out item)) return item;

            if (d.Kind == "int")
            {
                item = new RepositoryItemSpinEdit
                {
                    IsFloatValue = false,
                    MinValue = d.Min,
                    MaxValue = d.Max,
                    EditMask = "N0"
                };
            }
            else
            {
                var combo = new RepositoryItemImageComboBox();
                combo.TextEditStyle = TextEditStyles.DisableTextEditor;
                foreach (var o in d.Options ?? new List<SettingOptionDto>())
                    combo.Items.Add(new ImageComboBoxItem(o.Text, (object)o.Value, -1)); // (text, int) kurucusu değeri metne çeviriyor
                item = combo;
            }
            gridControl.RepositoryItems.Add(item);
            _editors[d.Key] = item;
            return item;
        }

        /// <summary>Sayılarda birimi göster ("30 dakika"); düzenlerken sadece sayı.</summary>
        private void gridView_CustomColumnDisplayText(object sender, CustomColumnDisplayTextEventArgs e)
        {
            if (e.Column != colValue || e.ListSourceRowIndex < 0 || e.ListSourceRowIndex >= _rows.Count) return;
            var row = _rows[e.ListSourceRowIndex];
            if (row.Dto.Kind == "int")
                e.DisplayText = WithUnit(Convert.ToString(e.Value, CultureInfo.CurrentCulture), row.Dto.Unit);
        }

        private void gridView_RowCellStyle(object sender, RowCellStyleEventArgs e)
        {
            if (e.Column != colValue && e.Column != colLabel) return;
            var row = gridView.GetRow(e.RowHandle) as SettingRow;
            if (row == null || !row.IsModified) return;
            if (_boldFont == null) _boldFont = new Font(e.Appearance.Font, FontStyle.Bold);
            e.Appearance.Font = _boldFont;
        }

        private void gridView_CellValueChanged(object sender, CellValueChangedEventArgs e)
        {
            gridView.RefreshRow(e.RowHandle);
            UpdateButtons();
        }

        /// <summary>Onay kutusu tıklanır tıklanmaz değer yazılsın (hücreden çıkmayı beklemesin).</summary>
        private void gridView_CellValueChanging(object sender, CellValueChangedEventArgs e)
        {
            var row = gridView.GetRow(e.RowHandle) as SettingRow;
            if (row != null && row.Dto.Kind == "bool")
                gridView.PostEditor();
        }

        private void gridView_FocusedRowChanged(object sender, FocusedRowChangedEventArgs e)
        {
            UpdateButtons();
        }

        private void UpdateButtons()
        {
            int changed = _rows.Count(r => r.IsModified);
            buttonSave.Enabled = changed > 0;
            buttonDiscard.Enabled = changed > 0;
            labelChanges.Text = changed == 0 ? "" : changed + " ayar değişti, kaydedilmedi.";
            labelChanges.Appearance.ForeColor = UiColors.Warning;

            var focused = gridView.GetFocusedRow() as SettingRow;
            buttonDefault.Enabled = focused != null && !focused.IsDefault;
        }

        // ================================================================ Butonlar

        private async void buttonSave_Click(object sender, EventArgs e)
        {
            gridView.CloseEditor();
            gridView.UpdateCurrentRow();

            var changed = _rows.Where(r => r.IsModified).ToList();
            if (changed.Count == 0) return;

            var request = new GeneralSettingsSaveRequest
            {
                Values = changed.Select(r => new SettingValueDto { Key = r.Dto.Key, Value = r.CurrentText }).ToList()
            };
            var list = await CallAsync(c => c.SendAsync<List<GeneralSettingDto>>(Commands.GeneralSettingsSave, request));
            if (list == null) return;

            Bind(list);
            Ui.Info(FindForm(), changed.Count + " ayar kaydedildi.");
        }

        private void buttonDiscard_Click(object sender, EventArgs e)
        {
            gridView.CancelUpdateCurrentRow();
            foreach (var r in _rows) r.Reset();
            gridControl.RefreshDataSource();
            UpdateButtons();
        }

        private void buttonDefault_Click(object sender, EventArgs e)
        {
            var row = gridView.GetFocusedRow() as SettingRow;
            if (row == null) return;
            gridView.CloseEditor();
            row.SetDefault();
            gridView.RefreshRow(gridView.FocusedRowHandle);
            UpdateButtons();
        }

        /// <summary>"30 dakika", yüzde Türkçe yazımla "%90".</summary>
        internal static string WithUnit(string number, string unit)
        {
            if (string.IsNullOrEmpty(unit)) return number;
            return unit == "%" ? "%" + number : number + " " + unit;
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                if (_boldFont != null) _boldFont.Dispose();
                if (components != null) components.Dispose();
            }
            base.Dispose(disposing);
        }

        // ================================================================ Satır

        public sealed class SettingRow
        {
            public SettingRow(GeneralSettingDto dto)
            {
                Dto = dto;
                Reset();
            }

            internal GeneralSettingDto Dto { get; private set; }

            public string Group { get { return Dto.Group; } }
            public string Label { get { return Dto.Label; } }
            public string Description { get { return Dto.Description; } }

            /// <summary>Düzenleyicinin değeri: int, bool, DateTime (saat) veya seçenek değeri (string).</summary>
            public object Value { get; set; }

            public string DefaultText { get { return Display(Dto, Dto.DefaultValue); } }

            internal string CurrentText { get { return ToText(Dto, Value); } }
            internal bool IsModified { get { return CurrentText != Dto.Value; } }
            internal bool IsDefault { get { return CurrentText == Dto.DefaultValue; } }

            internal void Reset() { Value = ToEditValue(Dto, Dto.Value); }
            internal void SetDefault() { Value = ToEditValue(Dto, Dto.DefaultValue); }

            private static object ToEditValue(GeneralSettingDto d, string text)
            {
                switch (d.Kind)
                {
                    case "int":
                        int n;
                        return int.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out n) ? n : 0;
                    case "bool":
                        return text == "true";
                    case "time":
                        DateTime t;
                        return DateTime.TryParseExact(text, "HH:mm", CultureInfo.InvariantCulture, DateTimeStyles.None, out t) ? t : DateTime.Today;
                    default:
                        return text;
                }
            }

            private static string ToText(GeneralSettingDto d, object value)
            {
                switch (d.Kind)
                {
                    case "int":
                        return Convert.ToInt32(value ?? 0, CultureInfo.InvariantCulture).ToString(CultureInfo.InvariantCulture);
                    case "bool":
                        return value is bool && (bool)value ? "true" : "false";
                    case "time":
                        return value is DateTime ? ((DateTime)value).ToString("HH:mm", CultureInfo.InvariantCulture) : "";
                    default:
                        return value as string ?? "";
                }
            }

            private static string Display(GeneralSettingDto d, string text)
            {
                switch (d.Kind)
                {
                    case "int": return WithUnit(text, d.Unit);
                    case "bool": return text == "true" ? "Açık" : "Kapalı";
                    case "choice":
                        var o = d.Options == null ? null : d.Options.FirstOrDefault(x => x.Value == text);
                        return o == null ? text : o.Text;
                    default: return text;
                }
            }
        }
    }
}
