using System;
using System.Collections.Generic;
using System.Linq;
using DevExpress.XtraEditors;
using DevExpress.XtraEditors.Controls;
using SecurityGuard.Shared.Dto;
using SecurityGuardUI.Helpers;
using SecurityGuardUI.Modules;

namespace SecurityGuardUI.Forms
{
    /// <summary>
    /// Seçili olay kurallarına toplu alıcı atama. Hiçbiri işaretlenmezse "tüm aktif alıcılar" demektir.
    /// Sonuç <see cref="SelectedIds"/>; değişiklik Olay Kuralları ekranında "Kaydet" ile gider.
    /// </summary>
    public partial class RecipientPickerForm : XtraForm
    {
        public RecipientPickerForm(IList<RecipientDto> recipients, IList<long> preset, int ruleCount)
        {
            InitializeComponent();
            Icon = AppImages.Icon;
            HandCursor.Apply(this);

            labelInfo.Text = ruleCount + " kurala uygulanacak. Hiçbiri işaretlenmezse mail tüm aktif alıcılara gider.";
            foreach (var r in recipients)
                listRecipients.Items.Add(new CheckedListBoxItem(r.Id, RulesModule.RecipientLabel(r), preset.Contains(r.Id) ? System.Windows.Forms.CheckState.Checked : System.Windows.Forms.CheckState.Unchecked));
            UpdateSummary();
        }

        /// <summary>İşaretlenen alıcılar (boş = tüm aktif alıcılar).</summary>
        public List<long> SelectedIds
        {
            get
            {
                return listRecipients.Items.Cast<CheckedListBoxItem>()
                    .Where(i => i.CheckState == System.Windows.Forms.CheckState.Checked)
                    .Select(i => (long)i.Value).ToList();
            }
        }

        private void listRecipients_ItemCheck(object sender, DevExpress.XtraEditors.Controls.ItemCheckEventArgs e)
        {
            UpdateSummary();
        }

        private void buttonClear_Click(object sender, EventArgs e)
        {
            listRecipients.UnCheckAll();
            UpdateSummary();
        }

        private void UpdateSummary()
        {
            int n = SelectedIds.Count;
            labelSummary.Text = n == 0 ? "Seçim: tüm aktif alıcılar" : "Seçim: " + n + " alıcı";
        }
    }
}
