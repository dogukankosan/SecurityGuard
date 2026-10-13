using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Threading.Tasks;
using DevExpress.XtraGrid.Views.Base;
using DevExpress.XtraGrid.Views.Grid;
using SecurityGuard.Shared.Contracts;
using SecurityGuard.Shared.Dto;
using SecurityGuard.Shared.Enums;
using SecurityGuardUI.Helpers;

namespace SecurityGuardUI.Modules
{
    /// <summary>
    /// Olay Kuralları: her olay tipi için İzle / Logla / Mail / Önem; RDP ve SQL başarısız giriş için Ban / Eşik.
    /// Değişiklikler "Kaydet" ile servise gider ve servis yeniden başlatılmadan geçerli olur.
    /// </summary>
    public partial class RulesModule : ModuleBase
    {
        private static readonly string[] CategoryOrder = { "Giriş", "Windows", "SQL Server", "SecurityGuard", "Raporlar" };

        private List<RuleRow> _rows = new List<RuleRow>();
        private List<RecipientDto> _recipients = new List<RecipientDto>();

        public RulesModule()
        {
            InitializeComponent();

            gridView.OptionsView.ShowGroupPanel = false;
            gridView.OptionsView.ShowIndicator = false;
            gridView.OptionsView.ColumnAutoWidth = true;
            gridView.OptionsBehavior.EditorShowMode = DevExpress.Utils.EditorShowMode.MouseDown;
            gridView.OptionsCustomization.AllowGroup = false;
            gridView.OptionsSelection.EnableAppearanceFocusedCell = false;
            gridView.OptionsView.GroupDrawMode = GroupDrawMode.Office;
            // Toplu alıcı atama için birden fazla satır seçilebilir (Ctrl / Shift)
            gridView.OptionsSelection.MultiSelect = true;
            gridView.OptionsSelection.MultiSelectMode = GridMultiSelectMode.RowSelect;

            // Dikkat: ImageComboBoxItem("metin", int) değeri METİN yapar (int'i resim numarası sayar).
            // Değerin sayı olması için (açıklama, object değer, resim) kullanılmalı.
            repoSeverity.Items.Add(new DevExpress.XtraEditors.Controls.ImageComboBoxItem("Bilgi", (object)(int)Severity.Bilgi, -1));
            repoSeverity.Items.Add(new DevExpress.XtraEditors.Controls.ImageComboBoxItem("Uyarı", (object)(int)Severity.Uyari, -1));
            repoSeverity.Items.Add(new DevExpress.XtraEditors.Controls.ImageComboBoxItem("Kritik", (object)(int)Severity.Kritik, -1));

            // Checkbox tıklanınca değer hemen satıra yazılsın (hücreden çıkmayı beklemesin)
            repoCheck.EditValueChanged += (s, e) => gridView.PostEditor();
            repoSeverity.EditValueChanged += (s, e) => gridView.PostEditor();
            repoRecipients.EditValueChanged += (s, e) => gridView.PostEditor();
            repoRecipients.CustomDisplayText += repoRecipients_CustomDisplayText;
            UpdateDirtyState();
        }

        protected override Task OnFirstShowAsync()
        {
            return LoadAsync();
        }

        private async Task LoadAsync()
        {
            var recipients = await CallAsync(c => c.SendAsync<List<RecipientDto>>(Commands.MailRecipientsList));
            if (recipients == null) return;
            var rules = await CallAsync(c => c.SendAsync<List<EventRuleDto>>(Commands.RulesList));
            if (rules == null) return;

            BindRecipients(recipients);
            Bind(rules);
        }

        /// <summary>"Alıcılar" açılır listesindeki kişiler.</summary>
        private void BindRecipients(List<RecipientDto> recipients)
        {
            _recipients = recipients;
            repoRecipients.Items.Clear();
            foreach (var r in recipients)
                repoRecipients.Items.Add(r.Id, RecipientLabel(r));
        }

        /// <summary>Tabloda kısa gösterim: sadece isimler ("DBA, Bilgi İşlem"); açılır listede mail adresleri de yazar.</summary>
        private void repoRecipients_CustomDisplayText(object sender, DevExpress.XtraEditors.Controls.CustomDisplayTextEventArgs e)
        {
            var ids = new RuleRow.IdParser(e.Value as string).Ids;
            if (ids.Count == 0)
            {
                e.DisplayText = "Tüm aktif alıcılar";
                return;
            }
            e.DisplayText = string.Join(", ", ids.Select(id =>
            {
                var r = _recipients.FirstOrDefault(x => x.Id == id);
                if (r == null) return "(silinmiş)";
                string name = string.IsNullOrWhiteSpace(r.DisplayName) ? r.Email : r.DisplayName;
                return r.IsEnabled ? name : name + " (pasif)";
            }));
        }

        internal static string RecipientLabel(RecipientDto r)
        {
            string text = string.IsNullOrWhiteSpace(r.DisplayName) ? r.Email : r.DisplayName + " <" + r.Email + ">";
            return r.IsEnabled ? text : text + " (pasif)";
        }

        /// <summary>Seçili kurallara (toplu) alıcı atar; değişiklik "Kaydet" ile gider.</summary>
        private void buttonAssignRecipients_Click(object sender, EventArgs e)
        {
            gridView.CloseEditor();
            var selected = gridView.GetSelectedRows().Where(h => h >= 0)
                .Select(h => gridView.GetRow(h) as RuleRow).Where(r => r != null).ToList();
            if (selected.Count == 0)
            {
                Ui.Warning(FindForm(), "Önce tablodan kuralları seçin (Ctrl veya Shift ile birden fazla seçebilirsiniz).");
                return;
            }
            if (_recipients.Count == 0)
            {
                Ui.Warning(FindForm(), "Henüz mail alıcısı yok. Önce Mail Ayarları → Alıcılar sekmesinden alıcı ekleyin.");
                return;
            }

            // Seçili kuralların hepsinde aynı alıcılar varsa pencere onlarla açılır
            var first = selected[0].RecipientIds;
            var preset = selected.All(r => r.RecipientIds.OrderBy(x => x).SequenceEqual(first.OrderBy(x => x))) ? first : new List<long>();

            using (var form = new Forms.RecipientPickerForm(_recipients, preset, selected.Count))
            {
                if (form.ShowDialog(FindForm()) != System.Windows.Forms.DialogResult.OK) return;
                foreach (var row in selected) row.RecipientIds = new List<long>(form.SelectedIds);
            }
            gridView.RefreshData();
            UpdateDirtyState();
        }

        private void Bind(List<EventRuleDto> rules)
        {
            _rows = rules.Select(r => new RuleRow(r)).ToList();
            gridControl.DataSource = _rows;
            gridView.ExpandAllGroups();
            UpdateDirtyState();
        }

        private async void buttonSave_Click(object sender, EventArgs e)
        {
            gridView.CloseEditor();
            gridView.UpdateCurrentRow();

            var changed = _rows.Where(r => r.IsDirty).Select(r => r.ToDto()).ToList();
            if (changed.Count == 0) return;

            var turnedOffCritical = _rows.Where(r => r.IsDirty && r.Severity == (int)Severity.Kritik && !r.SendMail && r.Original.SendMail).ToList();
            if (turnedOffCritical.Count > 0 && !Ui.Confirm(FindForm(),
                "Şu kritik olayların maili kapatılıyor:" + Environment.NewLine + Environment.NewLine +
                string.Join(Environment.NewLine, turnedOffCritical.Select(r => "  • " + r.Name)) +
                Environment.NewLine + Environment.NewLine + "Bu olaylar olduğunda haberiniz olmayabilir. Devam edilsin mi?"))
                return;

            var saved = await CallAsync(c => c.SendAsync<List<EventRuleDto>>(Commands.RulesSave, new RuleSaveRequest { Rules = changed }));
            if (saved == null) return;
            Bind(saved);
            Ui.Info(FindForm(), changed.Count + " kural kaydedildi.");
        }

        private async void buttonUndo_Click(object sender, EventArgs e)
        {
            if (_rows.Any(r => r.IsDirty) && !Ui.Confirm(FindForm(), "Kaydedilmemiş değişiklikler geri alınsın mı?")) return;
            await LoadAsync();
        }

        private async void buttonReset_Click(object sender, EventArgs e)
        {
            if (!Ui.Confirm(FindForm(), "Tüm olay kuralları varsayılan değerlere döndürülsün mü?" + Environment.NewLine +
                "(Kritik olaylarda mail açık, RDP ve SQL başarısız girişte 10 denemede ban)")) return;

            var rules = await CallAsync(c => c.SendAsync<List<EventRuleDto>>(Commands.RulesReset));
            if (rules != null) Bind(rules);
        }

        private void UpdateDirtyState()
        {
            int dirty = _rows.Count(r => r.IsDirty);
            buttonSave.Enabled = dirty > 0;
            labelDirty.Text = dirty > 0 ? dirty + " kuralda kaydedilmemiş değişiklik var" : "";
        }

        // --------------------------------------------------------- tablo olayları

        private void gridView_CellValueChanged(object sender, CellValueChangedEventArgs e)
        {
            UpdateDirtyState();
            gridView.RefreshRow(e.RowHandle);
        }

        /// <summary>Ban / Eşik sadece ban destekleyen kurallarda düzenlenebilir.</summary>
        private void gridView_ShowingEditor(object sender, System.ComponentModel.CancelEventArgs e)
        {
            var row = gridView.GetFocusedRow() as RuleRow;
            if (row != null && !row.CanBan && (gridView.FocusedColumn == colBan || gridView.FocusedColumn == colThreshold))
                e.Cancel = true;
        }

        private void gridView_CustomRowCellEdit(object sender, CustomRowCellEditEventArgs e)
        {
            if (e.Column != colBan && e.Column != colThreshold) return;
            var row = gridView.GetRow(e.RowHandle) as RuleRow;
            if (row != null && !row.CanBan) e.RepositoryItem = repoEmpty;
        }

        private void gridView_CustomColumnDisplayText(object sender, CustomColumnDisplayTextEventArgs e)
        {
            if (e.Column != colBan && e.Column != colThreshold) return;
            if (e.ListSourceRowIndex < 0 || e.ListSourceRowIndex >= _rows.Count) return;
            if (!_rows[e.ListSourceRowIndex].CanBan) e.DisplayText = "—";
        }

        /// <summary>Değişen satır kalın; izlenmeyen kural soluk.</summary>
        private void gridView_RowStyle(object sender, RowStyleEventArgs e)
        {
            var row = gridView.GetRow(e.RowHandle) as RuleRow;
            if (row == null) return;
            if (row.IsDirty) e.Appearance.FontStyleDelta = FontStyle.Bold;
            if (!row.Monitor) e.Appearance.ForeColor = DevExpress.LookAndFeel.DXSkinColors.ForeColors.DisabledText;
        }

        /// <summary>Gruplar katalog sırasıyla: Giriş, Windows, SQL Server, SecurityGuard, Raporlar.</summary>
        private void gridView_CustomColumnSort(object sender, CustomColumnSortEventArgs e)
        {
            if (e.Column != colCategory) return;
            e.Result = Order(e.Value1 as string).CompareTo(Order(e.Value2 as string));
            e.Handled = true;
        }

        private static int Order(string category)
        {
            int i = Array.IndexOf(CategoryOrder, category);
            return i < 0 ? CategoryOrder.Length : i;
        }

        // --------------------------------------------------------- satır

        public sealed class RuleRow
        {
            public RuleRow(EventRuleDto dto)
            {
                Original = dto;
                EventType = dto.EventType;
                Category = dto.Category;
                Name = dto.Name;
                Source = dto.Source;
                CanBan = dto.CanBan;
                Monitor = dto.Monitor;
                WriteLog = dto.WriteLog;
                SendMail = dto.SendMail;
                Severity = dto.Severity;
                BanEnabled = dto.BanEnabled;
                Threshold = dto.Threshold;
                RecipientIds = new List<long>(dto.RecipientIds ?? new List<long>());
            }

            internal EventRuleDto Original { get; private set; }

            public string EventType { get; private set; }
            public string Category { get; private set; }
            public string Name { get; private set; }
            public string Source { get; private set; }
            public bool CanBan { get; private set; }

            public bool Monitor { get; set; }
            public bool WriteLog { get; set; }
            public bool SendMail { get; set; }
            public int Severity { get; set; }
            public bool BanEnabled { get; set; }
            public int Threshold { get; set; }

            /// <summary>Seçili alıcılar; boş = tüm aktif alıcılar.</summary>
            internal List<long> RecipientIds { get; set; }

            /// <summary>
            /// Tablodaki "Alıcılar" açılır listesinin değeri (Id'ler virgülle: "1, 3").
            /// Boş değer → "Tüm aktif alıcılar" yazar.
            /// </summary>
            public string RecipientCsv
            {
                // Boşken null: açılır listenin "Tüm aktif alıcılar" yazısı (NullText) görünsün
                get { return RecipientIds.Count == 0 ? null : string.Join(", ", RecipientIds); }
                set { RecipientIds = new IdParser(value).Ids; }
            }

            /// <summary>"1, 3" biçimindeki Id listesini çözer (açılır listenin değer biçimi).</summary>
            internal sealed class IdParser
            {
                public readonly List<long> Ids;

                public IdParser(string csv)
                {
                    Ids = (csv ?? "").Split(',')
                        .Select(s => { long id; return long.TryParse(s.Trim(), out id) ? id : 0; })
                        .Where(id => id > 0).Distinct().ToList();
                }
            }

            public bool IsDirty
            {
                get
                {
                    var original = Original.RecipientIds ?? new List<long>();
                    return Monitor != Original.Monitor || WriteLog != Original.WriteLog || SendMail != Original.SendMail
                        || Severity != Original.Severity || BanEnabled != Original.BanEnabled || Threshold != Original.Threshold
                        || !RecipientIds.OrderBy(x => x).SequenceEqual(original.OrderBy(x => x));
                }
            }

            public EventRuleDto ToDto()
            {
                return new EventRuleDto
                {
                    EventType = EventType,
                    Monitor = Monitor,
                    WriteLog = WriteLog,
                    SendMail = SendMail,
                    Severity = Severity,
                    BanEnabled = BanEnabled,
                    Threshold = Threshold,
                    RecipientIds = new List<long>(RecipientIds)
                };
            }
        }
    }
}
