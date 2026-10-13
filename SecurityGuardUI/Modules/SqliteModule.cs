using System;
using System.Collections.Generic;
using System.Data;
using System.Drawing;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using DevExpress.LookAndFeel;
using DevExpress.XtraGrid;
using DevExpress.XtraGrid.Views.Base;
using DevExpress.XtraGrid.Views.Grid;
using DevExpress.XtraTab;
using SecurityGuard.Shared.Contracts;
using SecurityGuard.Shared.Dto;
using SecurityGuardUI.Helpers;

namespace SecurityGuardUI.Modules
{
    /// <summary>
    /// SQLite Sorgu: guard.db üzerinde serbest SQL (yetki kısıtı yok, bilinçli karar).
    /// Solda tablolar ve kolonlar, sağda SQL kutusu ve sonuçlar. F5 / Ctrl+Enter çalıştırır;
    /// seçili metin varsa sadece o çalışır. DB dosyası servis çalıştıkça kilitli olduğu için silinemez.
    /// </summary>
    public partial class SqliteModule : ModuleBase
    {
        private const string DefaultSql =
            "-- F5 veya Ctrl+Enter: çalıştır. Metin seçiliyse sadece seçili kısım çalışır.\r\n" +
            "-- Soldaki tabloya çift tıklayınca son 200 kaydı getirir.\r\n" +
            "SELECT * FROM EventLog ORDER BY Id DESC LIMIT 100;";

        private SqliteSchemaDto _schema;
        private bool _running;

        public SqliteModule()
        {
            InitializeComponent();
            GridHelper.SetupReadOnly(viewTables);
            GridHelper.SetupReadOnly(viewColumns);
            viewTables.OptionsView.ShowAutoFilterRow = false;
            viewColumns.OptionsView.ShowAutoFilterRow = false;
            viewTables.OptionsDetail.EnableMasterViewMode = false; // Columns listesi alt tablo olarak açılmasın
            viewColumns.OptionsDetail.EnableMasterViewMode = false;

            editor.Text = DefaultSql;
            editor.ResetHistory();
            editor.ExecuteRequested += async (s, e) => await RunAsync();
            UpdateExportState();
        }

        protected override Task OnFirstShowAsync()
        {
            return LoadSchemaAsync(false);
        }

        // ================================================================ Tablolar

        private async Task LoadSchemaAsync(bool quiet)
        {
            string focused = FocusedTable() == null ? null : FocusedTable().Name;
            SqliteSchemaDto schema;
            if (quiet)
            {
                var result = await CallQuietAsync(c => c.SendAsync<SqliteSchemaDto>(Commands.SqliteSchema));
                schema = result.Success ? result.Value : null;
            }
            else
            {
                schema = await CallAsync(c => c.SendAsync<SqliteSchemaDto>(Commands.SqliteSchema));
            }
            if (schema == null) return;

            _schema = schema;
            gridTables.DataSource = schema.Tables;
            labelDbInfo.Text = "guard.db · " + FormatSize(schema.FileSizeBytes) + " · şema v" + schema.SchemaVersion + " · " + schema.Tables.Count + " tablo";

            int index = focused == null ? -1 : schema.Tables.FindIndex(t => t.Name == focused);
            if (index >= 0) viewTables.FocusedRowHandle = viewTables.GetRowHandle(index);
            ShowColumns(FocusedTable());
        }

        private SqliteTableDto FocusedTable()
        {
            return viewTables.GetFocusedRow() as SqliteTableDto;
        }

        private void viewTables_FocusedRowChanged(object sender, FocusedRowChangedEventArgs e)
        {
            ShowColumns(FocusedTable());
        }

        private void ShowColumns(SqliteTableDto table)
        {
            gridColumns.DataSource = table == null
                ? null
                : table.Columns.Select(c => new
                {
                    Name = c.Name,
                    Type = c.Type,
                    Info = (c.IsPrimaryKey ? "PK " : "") + (c.NotNull ? "NOT NULL" : "")
                }).ToList();
        }

        private void viewTables_CustomColumnDisplayText(object sender, CustomColumnDisplayTextEventArgs e)
        {
            if (e.Column == colTableRows && e.Value is long && (long)e.Value < 0)
                e.DisplayText = "view";
        }

        /// <summary>Tabloya çift tık: son 200 kaydı getirir (SQL kutusundaki metin Ctrl+Z ile geri gelir).</summary>
        private async void viewTables_DoubleClick(object sender, EventArgs e)
        {
            var table = FocusedTable();
            if (table == null) return;
            string name = "\"" + table.Name.Replace("\"", "\"\"") + "\"";
            string sql = table.Type == "table"
                ? "SELECT * FROM " + name + " ORDER BY rowid DESC LIMIT 200;"
                : "SELECT * FROM " + name + " LIMIT 200;";

            editor.SelectAll();
            editor.InsertAtCaret(sql);
            editor.SelectionStart = editor.TextLength;
            await RunAsync();
        }

        private async void buttonRefresh_Click(object sender, EventArgs e)
        {
            await LoadSchemaAsync(false);
        }

        // ================================================================ Çalıştırma

        private async void buttonRun_Click(object sender, EventArgs e)
        {
            await RunAsync();
        }

        private async Task RunAsync()
        {
            if (_running) return;
            string sql = editor.SqlToRun;
            if (string.IsNullOrWhiteSpace(sql)) return;

            _running = true;
            buttonRun.Enabled = false;
            try
            {
                var result = await CallQuietAsync(c => c.SendAsync<SqliteExecuteResultDto>(
                    Commands.SqliteExecute, new SqliteExecuteRequest { Sql = sql }, TimeSpan.FromSeconds(180)));
                if (result.IsUnauthorized) return;

                if (!result.Success)
                {
                    ShowError(result.ErrorMessage);
                    await LoadSchemaAsync(true); // ayrı bağlantıda kısmen uygulanmış olabilir
                    return;
                }

                ShowResults(result.Value);
                if (result.Value.RecordsAffected > 0 || result.Value.ResultSets.Count == 0)
                    await LoadSchemaAsync(true); // satır sayıları / yeni tablolar
            }
            finally
            {
                _running = false;
                buttonRun.Enabled = true;
                editor.Focus();
            }
        }

        private void ShowError(string message)
        {
            ClearResultTabs();
            memoMessages.Text = message;
            memoMessages.Properties.Appearance.ForeColor = UiColors.Error;
            tabResults.SelectedTabPage = tabMessages;
            labelStatus.Text = "Hata";
            labelStatus.Appearance.ForeColor = UiColors.Error;
            UpdateExportState();
        }

        private void ShowResults(SqliteExecuteResultDto r)
        {
            ClearResultTabs();

            var log = new StringBuilder();
            int index = 0;
            foreach (var set in r.ResultSets)
            {
                index++;
                var page = new XtraTabPage { Text = "Sonuç " + index + " (" + set.TotalRows + ")" };
                var grid = CreateResultGrid(set);
                page.Controls.Add(grid);
                tabResults.TabPages.Insert(tabResults.TabPages.Count - 1, page); // Mesajlar hep en sonda
                BestFit(grid);

                log.AppendLine("Sonuç " + index + ": " + set.TotalRows + " satır, " + set.Columns.Count + " kolon" +
                               (set.Truncated ? " (ilk " + set.Rows.Count + " satır gösteriliyor)" : ""));
            }

            if (r.RecordsAffected > 0 || r.ResultSets.Count == 0)
                log.AppendLine(r.RecordsAffected + " satır etkilendi.");
            if (!string.IsNullOrEmpty(r.Message))
                log.AppendLine(r.Message);
            log.AppendLine("Süre: " + r.ElapsedMs + " ms");

            memoMessages.Text = log.ToString().TrimEnd();
            memoMessages.Properties.Appearance.ForeColor = string.IsNullOrEmpty(r.Message) ? UiColors.Text : UiColors.Warning;
            tabResults.SelectedTabPageIndex = 0; // ilk sonuç (yoksa Mesajlar)

            string summary = r.ResultSets.Count > 0
                ? string.Join(" · ", r.ResultSets.Select(s => s.TotalRows + " satır" + (s.Truncated ? " (" + s.Rows.Count + " gösteriliyor)" : "")))
                : r.RecordsAffected + " satır etkilendi";
            if (r.ResultSets.Count > 0 && r.RecordsAffected > 0) summary += " · " + r.RecordsAffected + " satır etkilendi";
            labelStatus.Text = summary + " · " + r.ElapsedMs + " ms";
            labelStatus.Appearance.ForeColor = UiColors.Success;
            UpdateExportState();
        }

        private GridControl CreateResultGrid(SqliteResultSetDto set)
        {
            // Kolon adları tekrar edebilir (SELECT 1, 1): tabloda benzersiz ad, başlıkta gerçek ad
            var table = new DataTable();
            for (int i = 0; i < set.Columns.Count; i++)
                table.Columns.Add("c" + i, typeof(string));
            table.BeginLoadData();
            foreach (var row in set.Rows)
                table.Rows.Add(row.Select(v => v == null ? (object)DBNull.Value : v).ToArray());
            table.EndLoadData();

            var grid = new GridControl { Dock = System.Windows.Forms.DockStyle.Fill };
            var view = new GridView(grid);
            grid.MainView = view;
            grid.DataSource = table;
            GridHelper.SetupReadOnly(view, true);
            view.OptionsView.ColumnAutoWidth = false;
            view.OptionsView.ShowIndicator = true;
            view.IndicatorWidth = 50;
            view.OptionsSelection.MultiSelectMode = GridMultiSelectMode.CellSelect;
            view.OptionsClipboard.CopyColumnHeaders = DevExpress.Utils.DefaultBoolean.False;

            // Kolonlar elle: PopulateColumns grid ekrana gelmeden kolon üretmiyor
            for (int i = 0; i < set.Columns.Count; i++)
                view.Columns.AddVisible("c" + i, set.Columns[i]);

            view.CustomDrawRowIndicator += (s, e) =>
            {
                if (e.RowHandle >= 0) e.Info.DisplayText = (e.RowHandle + 1).ToString();
            };
            view.CustomColumnDisplayText += (s, e) =>
            {
                if (e.ListSourceRowIndex >= 0 && (e.Value == null || e.Value is DBNull)) e.DisplayText = "NULL"; // filtre satırı hariç
            };
            Font italic = null;
            view.RowCellStyle += (s, e) =>
            {
                if (e.RowHandle >= 0 && (e.CellValue == null || e.CellValue is DBNull))
                {
                    if (italic == null) italic = new Font(e.Appearance.Font, FontStyle.Italic);
                    e.Appearance.ForeColor = DXSkinColors.ForeColors.DisabledText;
                    e.Appearance.Font = italic;
                }
            };
            grid.Disposed += (s, e) => { if (italic != null) italic.Dispose(); };

            HandCursor.Apply(grid);
            return grid;
        }

        /// <summary>Kolon genişliklerini içeriğe göre ayarlar (grid sayfaya eklendikten sonra çağrılır).</summary>
        private static void BestFit(GridControl grid)
        {
            var view = (GridView)grid.MainView;
            view.BestFitMaxRowCount = 200;
            view.BestFitColumns();
            foreach (DevExpress.XtraGrid.Columns.GridColumn c in view.Columns)
                if (c.Width > 400) c.Width = 400;
        }

        private void ClearResultTabs()
        {
            foreach (var page in tabResults.TabPages.Cast<XtraTabPage>().Where(p => p != tabMessages).ToList())
            {
                tabResults.TabPages.Remove(page);
                page.Dispose();
            }
        }

        private void tabResults_SelectedPageChanged(object sender, TabPageChangedEventArgs e)
        {
            UpdateExportState();
        }

        private GridControl CurrentResultGrid()
        {
            var page = tabResults.SelectedTabPage;
            return page == null || page == tabMessages ? null : page.Controls.OfType<GridControl>().FirstOrDefault();
        }

        private void UpdateExportState()
        {
            buttonExport.Enabled = CurrentResultGrid() != null;
        }

        private void buttonExport_Click(object sender, EventArgs e)
        {
            var grid = CurrentResultGrid();
            if (grid != null) GridHelper.ExportToExcel(FindForm(), grid, "SQLite_Sorgu");
        }

        private static string FormatSize(long bytes)
        {
            if (bytes >= 1024L * 1024 * 1024) return (bytes / 1024d / 1024 / 1024).ToString("0.0") + " GB";
            if (bytes >= 1024L * 1024) return (bytes / 1024d / 1024).ToString("0.0") + " MB";
            return (bytes / 1024d).ToString("0") + " KB";
        }
    }
}
