using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Forms;
using DevExpress.XtraEditors;
using DevExpress.XtraEditors.Controls;
using DevExpress.XtraGrid.Views.Base;
using DevExpress.XtraGrid.Views.Grid;
using SecurityGuard.Shared;
using SecurityGuard.Shared.Contracts;
using SecurityGuard.Shared.Dto;
using SecurityGuard.Shared.Enums;
using SecurityGuardUI.Helpers;

namespace SecurityGuardUI.Modules
{
    /// <summary>
    /// Tarama sayfası (Performans Önerileri; ileride Güvenlik Önerileri de aynı sayfayı kullanır).
    /// Son taramanın maddeleri: durum, neden, nasıl düzeltilir ve hazır script. SecurityGuard hiçbir öneriyi
    /// kendisi uygulamaz: script'i kullanıcı kopyalayıp kendisi çalıştırır. Madde notla görmezden gelinebilir.
    /// </summary>
    public partial class ChecksModule : ModuleBase
    {
        private const int ViewProblemsAndInfo = 0;
        private const int ViewProblems = 1;
        private const int ViewAll = 2;

        private readonly string _scope;
        private readonly Timer _poll = new Timer { Interval = 2000 };
        private CheckRunDto _run;
        private bool _polling;

        public ChecksModule()
            : this(CheckScopes.Performance, "Performans Önerileri")
        {
        }

        public ChecksModule(string scope, string title)
        {
            InitializeComponent();
            _scope = scope;
            labelTitle.Text = title;
            GridHelper.SetupReadOnly(gridView);
            gridView.OptionsView.ShowAutoFilterRow = false;
            gridView.OptionsView.RowAutoHeight = false;

            comboView.Properties.Items.Add(new ImageComboBoxItem("Sorunlar ve bilgiler", (object)ViewProblemsAndInfo, -1));
            comboView.Properties.Items.Add(new ImageComboBoxItem("Sadece sorunlar (kritik / uyarı)", (object)ViewProblems, -1));
            comboView.Properties.Items.Add(new ImageComboBoxItem("Tümü (geçenler dahil)", (object)ViewAll, -1));
            comboView.EditValue = ViewProblemsAndInfo;

            _poll.Tick += async (s, e) => await PollAsync();
            ShowDetail();
        }

        protected override Task OnFirstShowAsync()
        {
            return LoadAsync();
        }

        private async Task LoadAsync()
        {
            var run = await CallAsync(c => c.SendAsync<CheckRunDto>(Commands.ChecksLatest, new CheckScopeRequest { Scope = _scope }));
            if (run != null) Bind(run);
        }

        // ================================================================ Tarama

        private async void buttonRun_Click(object sender, EventArgs e)
        {
            var run = await CallAsync(c => c.SendAsync<CheckRunDto>(Commands.ChecksRun, new CheckScopeRequest { Scope = _scope }));
            if (run != null) Bind(run);
        }

        /// <summary>Tarama sürerken durumu sessizce (yükleniyor göstergesi olmadan) yeniler.</summary>
        private async Task PollAsync()
        {
            if (_polling || IsDisposed) return;
            _polling = true;
            try
            {
                var result = await Client.SendAsync<CheckRunDto>(Commands.ChecksLatest, new CheckScopeRequest { Scope = _scope });
                if (IsDisposed) return;
                if (result.IsUnauthorized)
                {
                    _poll.Stop();
                    Host.HandleSessionExpired();
                    return;
                }
                if (!result.Success) return; // geçici hata: bir sonraki turda tekrar denenir

                bool finished = _run != null && _run.IsRunning && !result.Value.IsRunning;
                Bind(result.Value);
                if (finished)
                {
                    labelRun.Text = "Tarama bitti. " + labelRun.Text;
                    labelRun.Appearance.ForeColor = UiColors.Success;
                }
            }
            finally
            {
                _polling = false;
            }
        }

        private void Bind(CheckRunDto run)
        {
            string focusedId = FocusedRow() == null ? null : FocusedRow().Item.CheckId;
            _run = run;

            if (run.IsRunning) _poll.Start();
            else _poll.Stop();
            buttonRun.Enabled = !run.IsRunning;

            string next = run.NextRunUtc == null ? "" : "Sonraki otomatik tarama: " + Local(run.NextRunUtc);
            if (run.IsRunning)
                labelRun.Text = "Tarama sürüyor: " + (run.Progress ?? "...") + (run.RunId != null ? "   (listede bir önceki tarama görünüyor)" : "");
            else if (run.RunId == null)
                labelRun.Text = "Henüz tarama yapılmadı. \"Şimdi Tara\" ile başlatın.   " + next;
            else
                labelRun.Text = "Son tarama: " + Local(run.StartedUtc) + " (" + Duration(run) + ", " + run.Trigger + ")   " + next;
            labelRun.Appearance.ForeColor = UiColors.Text;

            BindGrid(focusedId);
        }

        private void BindGrid(string focusedId = null)
        {
            var items = _run == null || _run.Items == null ? new List<CheckItemDto>() : _run.Items;
            int view = comboView.EditValue is int ? (int)comboView.EditValue : ViewProblemsAndInfo;

            var rows = items.Select((x, i) => new Row(x, i))
                .Where(r => checkIgnored.Checked || !r.Item.IsIgnored)
                .Where(r => view == ViewAll || r.Item.Status != CheckStatus.Gecti)
                .Where(r => view != ViewProblems || r.Item.Status == CheckStatus.Kritik || r.Item.Status == CheckStatus.Uyari)
                .OrderByDescending(r => r.Rank).ThenBy(r => r.Order)
                .ToList();
            gridControl.DataSource = rows;

            if (focusedId != null)
            {
                int index = rows.FindIndex(r => r.Item.CheckId == focusedId);
                if (index >= 0) gridView.FocusedRowHandle = gridView.GetRowHandle(index);
            }
            ShowDetail();

            var active = items.Where(x => !x.IsIgnored).ToList();
            int ignored = items.Count - active.Count;
            labelStatus.Text = items.Count == 0 ? ""
                : Count(active, CheckStatus.Kritik, "kritik") + ", " + Count(active, CheckStatus.Uyari, "uyarı") + ", "
                  + Count(active, CheckStatus.Bilgi, "bilgi") + ", " + Count(active, CheckStatus.Gecti, "geçti")
                  + (active.Any(x => x.Status == CheckStatus.Hata) ? ", " + Count(active, CheckStatus.Hata, "kontrol edilemedi") : "")
                  + (ignored > 0 ? "   ·   " + ignored + " madde görmezden geliniyor" : "")
                  + (active.Any(x => x.IsNew) ? "   ·   " + active.Count(x => x.IsNew) + " yeni bulgu (bir önceki taramada yoktu)" : "");
            labelStatus.Appearance.ForeColor = active.Any(x => x.Status == CheckStatus.Kritik) ? UiColors.Error
                : active.Any(x => x.Status == CheckStatus.Uyari) ? UiColors.Warning : UiColors.Text;
        }

        private static string Count(List<CheckItemDto> items, CheckStatus status, string text)
        {
            return items.Count(x => x.Status == status) + " " + text;
        }

        private static string Local(string utc)
        {
            var t = UtcTime.ToLocal(utc);
            return t.HasValue ? t.Value.ToString("dd.MM.yyyy HH:mm") : "-";
        }

        private static string Duration(CheckRunDto run)
        {
            var a = UtcTime.ToLocal(run.StartedUtc);
            var b = UtcTime.ToLocal(run.FinishedUtc);
            if (!a.HasValue || !b.HasValue) return "-";
            var span = b.Value - a.Value;
            return span.TotalMinutes >= 1 ? (int)span.TotalMinutes + " dk " + span.Seconds + " sn" : Math.Max(1, (int)span.TotalSeconds) + " sn";
        }

        // ================================================================ Tablo

        private Row FocusedRow()
        {
            return gridView.GetFocusedRow() as Row;
        }

        internal static string StatusText(CheckStatus s)
        {
            switch (s)
            {
                case CheckStatus.Kritik: return "Kritik";
                case CheckStatus.Uyari: return "Uyarı";
                case CheckStatus.Bilgi: return "Bilgi";
                case CheckStatus.Hata: return "Kontrol edilemedi";
                default: return "Geçti";
            }
        }

        private void gridView_RowCellStyle(object sender, RowCellStyleEventArgs e)
        {
            var row = gridView.GetRow(e.RowHandle) as Row;
            if (row == null) return;
            if (row.Item.IsIgnored)
            {
                e.Appearance.FontStyleDelta = System.Drawing.FontStyle.Strikeout;
                return;
            }
            if (e.Column != colStatus && e.Column != colNew) return;
            if (row.Item.Status == CheckStatus.Kritik) e.Appearance.ForeColor = UiColors.Error;
            else if (row.Item.Status == CheckStatus.Uyari || row.Item.Status == CheckStatus.Hata) e.Appearance.ForeColor = UiColors.Warning;
            else if (row.Item.Status == CheckStatus.Gecti) e.Appearance.ForeColor = UiColors.Success;
        }

        private void gridView_FocusedRowChanged(object sender, FocusedRowChangedEventArgs e)
        {
            ShowDetail();
        }

        private void ShowDetail()
        {
            var r = FocusedRow();
            buttonIgnore.Enabled = r != null && r.Item.Status != CheckStatus.Gecti;
            buttonIgnore.Text = r != null && r.Item.IsIgnored ? "Görmezden Gelme" : "Görmezden Gel...";
            if (r == null)
            {
                memoDetail.Text = "";
                ShowScript(null, null);
                return;
            }

            var x = r.Item;
            var sb = new StringBuilder();
            sb.AppendLine(x.Title + " - " + StatusText(x.Status) + (x.IsNew ? " (yeni)" : ""));
            sb.AppendLine(x.Area);
            sb.AppendLine();
            sb.AppendLine(x.Summary);
            if (!string.IsNullOrEmpty(x.Detail))
            {
                sb.AppendLine();
                sb.AppendLine(x.Detail.TrimEnd());
            }
            if (!string.IsNullOrEmpty(x.HowToFix))
            {
                sb.AppendLine();
                sb.AppendLine("NASIL DÜZELTİLİR");
                sb.AppendLine(x.HowToFix.TrimEnd());
            }
            if (x.IsIgnored)
            {
                sb.AppendLine();
                sb.AppendLine("GÖRMEZDEN GELİNİYOR: " + x.IgnoreNote);
            }
            memoDetail.Text = sb.ToString().TrimEnd();
            ShowScript(x.Script, x.ScriptLanguage);
        }

        private void ShowScript(string script, string language)
        {
            bool has = !string.IsNullOrEmpty(script);
            bool sql = language == "sql";
            sqlScript.Visible = has && sql;
            memoScript.Visible = has && !sql;
            labelNoScript.Visible = !has;
            buttonCopyScript.Enabled = has;
            if (has && sql) sqlScript.Text = script;
            else sqlScript.Text = "";
            memoScript.Text = has && !sql ? script : "";
            labelScriptTitle.Text = !has ? "Script" : sql ? "SQL script (SSMS'te kendiniz çalıştırın)" : "Komut (yönetici PowerShell'de kendiniz çalıştırın)";
        }

        // ================================================================ Butonlar

        private void comboView_SelectedIndexChanged(object sender, EventArgs e)
        {
            BindGrid(FocusedRow() == null ? null : FocusedRow().Item.CheckId);
        }

        private void checkIgnored_CheckedChanged(object sender, EventArgs e)
        {
            BindGrid(FocusedRow() == null ? null : FocusedRow().Item.CheckId);
        }

        private void buttonCopyScript_Click(object sender, EventArgs e)
        {
            var r = FocusedRow();
            if (r == null || string.IsNullOrEmpty(r.Item.Script)) return;
            try
            {
                Clipboard.SetText(r.Item.Script);
                labelStatus.Text = "Script panoya kopyalandı.";
                labelStatus.Appearance.ForeColor = UiColors.Success;
            }
            catch (System.Runtime.InteropServices.ExternalException)
            {
                Ui.Warning(FindForm(), "Pano başka bir program tarafından kullanılıyor, tekrar deneyin.");
            }
        }

        private async void buttonIgnore_Click(object sender, EventArgs e)
        {
            var r = FocusedRow();
            if (r == null) return;

            var req = new CheckIgnoreRequest { Scope = _scope, CheckId = r.Item.CheckId, Ignore = !r.Item.IsIgnored };
            if (req.Ignore)
            {
                string note = XtraInputBox.Show(FindForm(),
                    "\"" + r.Item.Title + "\" maddesi görmezden gelinsin.\r\nNeden? (ör. \"Uygulama üreticisi bu ayarı istiyor\")",
                    "Görmezden gel", r.Item.IgnoreNote ?? "");
                if (note == null) return;
                if (note.Trim().Length == 0)
                {
                    Ui.Warning(FindForm(), "Kısa bir not yazın: sonradan bakan kişi neden görmezden gelindiğini bilsin.");
                    return;
                }
                req.Note = note.Trim();
            }
            else if (!Ui.Confirm(FindForm(), "\"" + r.Item.Title + "\" tekrar uyarı olarak gösterilsin mi?"))
            {
                return;
            }

            var run = await CallAsync(c => c.SendAsync<CheckRunDto>(Commands.ChecksIgnore, req));
            if (run != null) Bind(run);
        }

        private void buttonExport_Click(object sender, EventArgs e)
        {
            GridHelper.ExportToExcel(FindForm(), gridControl, labelTitle.Text.Replace(" ", ""));
        }

        protected override void OnHandleDestroyed(EventArgs e)
        {
            _poll.Stop();
            base.OnHandleDestroyed(e);
        }

        /// <summary>Tablo satırı.</summary>
        public sealed class Row
        {
            internal Row(CheckItemDto item, int order)
            {
                Item = item;
                Order = order;
            }

            internal CheckItemDto Item { get; private set; }
            internal int Order { get; private set; }

            internal int Rank
            {
                get
                {
                    switch (Item.Status)
                    {
                        case CheckStatus.Kritik: return 4;
                        case CheckStatus.Uyari: return 3;
                        case CheckStatus.Hata: return 2;
                        case CheckStatus.Bilgi: return 1;
                        default: return 0;
                    }
                }
            }

            public string Status { get { return StatusText(Item.Status) + (Item.IsIgnored ? " (görmezden)" : ""); } }
            public string New { get { return Item.IsNew ? "Yeni" : ""; } }
            public string Area { get { return Item.Area; } }
            public string Title { get { return Item.Title; } }
            public string Summary { get { return Item.Summary; } }
            public string Script { get { return string.IsNullOrEmpty(Item.Script) ? "" : "Var"; } }
        }
    }
}
