using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Forms;
using DevExpress.XtraEditors;
using DevExpress.XtraGrid;
using DevExpress.XtraGrid.Views.Grid;
using SecurityGuard.Shared;
using SecurityGuard.Shared.Contracts;
using SecurityGuard.Shared.Dto;
using SecurityGuardUI.Helpers;

namespace SecurityGuardUI.Modules
{
    /// <summary>
    /// Tuzak Dosyalar: paylaşılan klasörlere / yedek klasörüne konan gizli dosyalar. Dokunulursa (yazma, silme, ad değiştirme)
    /// anında kritik mail. Paylaşımlar ve yedek klasörü servis tarafından kendiliğinden bulunur.
    /// </summary>
    public sealed class CanaryModule : ModuleBase
    {
        private readonly GridControl _folders = new GridControl { Dock = DockStyle.Fill };
        private readonly GridView _folderView;
        private readonly GridControl _files = new GridControl { Dock = DockStyle.Fill };
        private readonly GridView _fileView;
        private readonly LabelControl _audit = new LabelControl();
        private readonly SimpleButton _copyCommand = new SimpleButton { Text = "Komutu kopyala", Size = new Size(120, 24) };
        private readonly LabelControl _incident = new LabelControl();
        private readonly LabelControl _status = new LabelControl();
        private readonly SimpleButton _toggle = new SimpleButton { Text = "Kapat", Size = new Size(80, 26) };
        private readonly SimpleButton _remove = new SimpleButton { Text = "Kaldır", Size = new Size(80, 26) };
        private CanaryStateDto _state;

        public CanaryModule()
        {
            _folderView = new GridView(_folders);
            _folders.MainView = _folderView;
            _folders.ViewCollection.Add(_folderView);
            GridHelper.SetupReadOnly(_folderView);
            _fileView = new GridView(_files);
            _files.MainView = _fileView;
            _files.ViewCollection.Add(_fileView);
            GridHelper.SetupReadOnly(_fileView);
            Build();
        }

        private void Build()
        {
            var top = new PanelControl { Dock = DockStyle.Top, Height = 168, BorderStyle = DevExpress.XtraEditors.Controls.BorderStyles.NoBorder };
            var title = new LabelControl { Text = "Tuzak Dosyalar", Location = new Point(12, 8) };
            title.Appearance.Font = new Font("Segoe UI", 13F);
            title.Appearance.Options.UseFont = true;
            var info = new LabelControl
            {
                Location = new Point(12, 36),
                AutoSizeMode = LabelAutoSizeMode.None,
                Size = new Size(1000, 36),
                Text = "Paylaşılan klasörlere ve SQL yedek klasörüne gizli tuzak dosyalar konur (müşteri dosyalarına dokunulmaz). "
                       + "Bu dosyalara yazılır, silinir veya adı değişirse fidye yazılımı belirtisi olarak anında kritik mail gider: dosya, kullanıcı, program, "
                       + "paylaşıma bağlı bilgisayarlar ve sunucudaki oturumlar. Sadece bildirilir, müdahale edilmez."
            };
            info.Appearance.TextOptions.WordWrap = DevExpress.Utils.WordWrap.Wrap;
            info.Appearance.Options.UseTextOptions = true;
            top.Resize += (s, e) => info.Width = Math.Max(300, top.Width - 24);

            _audit.Location = new Point(12, 80);
            _audit.Appearance.Options.UseForeColor = true;
            _copyCommand.Click += (s, e) =>
            {
                if (_state == null) return;
                Clipboard.SetText(_state.AuditCommand);
                Ui.Info(FindForm(), "Komut panoya kopyalandı. Yönetici komut isteminde (cmd) çalıştırın; hemen geçerli olur:\r\n\r\n" + _state.AuditCommand
                                    + "\r\n\r\nSadece denetim işareti konan dosyalar loglanır (SecurityGuard'ın tuzak dosyaları); diğer dosyalar için log oluşmaz.");
            };
            _incident.Location = new Point(12, 106);
            _incident.Appearance.Options.UseForeColor = true;

            int x = 12, y = 134;
            Func<string, int, Func<Task>, SimpleButton> button = (text, width, click) =>
            {
                var b = new SimpleButton { Text = text, Location = new Point(x, y), Size = new Size(width, 26) };
                b.Click += async (s, e) => await click();
                top.Controls.Add(b);
                x += width + 6;
                return b;
            };
            button("Klasör ekle...", 110, AddAsync);
            _remove.Location = new Point(x, y);
            _remove.Click += async (s, e) => await RemoveAsync();
            top.Controls.Add(_remove);
            x += 86;
            _toggle.Location = new Point(x, y);
            _toggle.Click += async (s, e) => await ToggleAsync();
            top.Controls.Add(_toggle);
            x += 86 + 16;
            button("Şimdi kontrol et", 120, () => RunAsync(Commands.CanaryCheck, null));
            button("Test et", 80, TestAsync);
            button("Yenile", 70, LoadAsync);

            top.Controls.Add(title);
            top.Controls.Add(info);
            top.Controls.Add(_audit);
            top.Controls.Add(_copyCommand);
            top.Controls.Add(_incident);

            var filesPanel = new PanelControl { Dock = DockStyle.Bottom, Height = 170, BorderStyle = DevExpress.XtraEditors.Controls.BorderStyles.NoBorder };
            var filesTitle = new LabelControl { Text = "Seçili klasördeki tuzak dosyalar", Dock = DockStyle.Top, Padding = new Padding(12, 6, 0, 4) };
            filesPanel.Controls.Add(_files);
            filesPanel.Controls.Add(filesTitle);

            var bottom = new PanelControl { Dock = DockStyle.Bottom, Height = 28, BorderStyle = DevExpress.XtraEditors.Controls.BorderStyles.NoBorder };
            _status.Location = new Point(12, 6);
            bottom.Controls.Add(_status);

            Controls.Add(_folders);
            Controls.Add(top);
            Controls.Add(filesPanel);
            Controls.Add(bottom);

            _folderView.FocusedRowChanged += (s, e) => ShowFiles();
            _folderView.RowStyle += (s, e) =>
            {
                var row = _folderView.GetRow(e.RowHandle) as FolderRow;
                if (row == null) return;
                if (!row.Item.Enabled) e.Appearance.ForeColor = DevExpress.LookAndFeel.DXSkinColors.ForeColors.DisabledText;
                else if (!row.State.StartsWith("İzleniyor", StringComparison.Ordinal)) e.Appearance.ForeColor = UiColors.Warning;
            };
            _fileView.RowStyle += (s, e) =>
            {
                var row = _fileView.GetRow(e.RowHandle) as FileRow;
                if (row != null && row.State != "Yerinde") e.Appearance.ForeColor = UiColors.Error;
            };
        }

        protected override Task OnFirstShowAsync()
        {
            return LoadAsync();
        }

        private async Task LoadAsync()
        {
            var state = await CallAsync(c => c.SendAsync<CanaryStateDto>(Commands.CanaryGet));
            if (state != null) Bind(state);
        }

        private void Bind(CanaryStateDto state)
        {
            _state = state;
            long focused = FocusedFolder() != null ? FocusedFolder().Item.Id : 0;
            _folders.DataSource = state.Folders.Select(f => new FolderRow(f)).ToList();
            _folderView.BestFitColumns();
            for (int h = 0; h < _folderView.RowCount; h++)
                if (((FolderRow)_folderView.GetRow(h)).Item.Id == focused) _folderView.FocusedRowHandle = h;
            ShowFiles();

            _audit.Text = state.FileAuditEnabled
                ? "Windows \"Dosya sistemi\" denetimi açık: mailde dokunan kullanıcı ve program yazar."
                : "Windows \"Dosya sistemi\" denetimi KAPALI: alarm yine gider ama mailde kullanıcı / program yazmaz. Açmak için komutu yönetici olarak çalıştırın →";
            _audit.Appearance.ForeColor = state.FileAuditEnabled ? UiColors.Success : UiColors.Warning;
            _copyCommand.Visible = !state.FileAuditEnabled;
            _copyCommand.Location = new Point(_audit.Right + 8, 76);

            var last = UtcTime.ToLocal(state.LastIncidentUtc);
            _incident.Text = last == null ? "Şimdiye kadar tuzak dosyaya dokunulmadı." : "Son tetiklenme: " + last.Value.ToString("dd.MM.yyyy HH:mm") + " - " + state.LastIncident;
            _incident.Appearance.ForeColor = last == null ? UiColors.Text : UiColors.Error;

            var checkedAt = UtcTime.ToLocal(state.LastCheckUtc);
            _status.Text = state.Message + (checkedAt == null ? "" : " Son kontrol: " + checkedAt.Value.ToString("HH:mm:ss") + ".");
        }

        private FolderRow FocusedFolder()
        {
            return _folderView.GetRow(_folderView.FocusedRowHandle) as FolderRow;
        }

        private void ShowFiles()
        {
            var row = FocusedFolder();
            _files.DataSource = row == null ? new List<FileRow>() : row.Item.Files.Select(f => new FileRow(f)).ToList();
            _fileView.BestFitColumns();
            _toggle.Enabled = _remove.Enabled = row != null;
            if (row == null) return;
            _toggle.Text = row.Item.Enabled ? "Kapat" : "Aç";
            _remove.Enabled = row.Item.CanRemove;
        }

        private async Task RunAsync(string command, object payload)
        {
            var state = await CallAsync(c => c.SendAsync<CanaryStateDto>(command, payload, TimeSpan.FromMinutes(2)));
            if (state != null) Bind(state);
        }

        private async Task AddAsync()
        {
            string path;
            using (var dialog = new FolderBrowserDialog { Description = "Tuzak dosya konacak klasörü seçin (paylaşılan klasörler kendiliğinden eklenir)." })
            {
                if (dialog.ShowDialog(FindForm()) != DialogResult.OK) return;
                path = dialog.SelectedPath;
            }
            await RunAsync(Commands.CanaryAddFolder, new CanaryFolderRequest { Path = path });
        }

        private async Task RemoveAsync()
        {
            var row = FocusedFolder();
            if (row == null || !row.Item.CanRemove) return;
            if (!Ui.Confirm(FindForm(), row.Item.Path + " listeden kaldırılsın mı? Bu klasördeki tuzak dosyalar silinir.")) return;
            await RunAsync(Commands.CanaryRemoveFolder, new CanaryFolderRequest { Id = row.Item.Id });
        }

        private async Task ToggleAsync()
        {
            var row = FocusedFolder();
            if (row == null) return;
            bool enable = !row.Item.Enabled;
            if (!enable && !Ui.Confirm(FindForm(), row.Item.Path + " için tuzak dosyalar kapatılsın mı? Dosyalar silinir; bu klasörde fidye yazılımı erken uyarısı olmaz.")) return;
            await RunAsync(Commands.CanarySetEnabled, new CanaryFolderRequest { Id = row.Item.Id, Enabled = enable });
        }

        private async Task TestAsync()
        {
            if (!Ui.Confirm(FindForm(), "Bir tuzak dosyaya SecurityGuard kendisi yazacak: Olay Logu'na \"TEST: Tuzak dosyaya dokunuldu\" düşer ve kritik mail gider. Devam edilsin mi?")) return;
            var result = await CallAsync(c => c.SendAsync<string>(Commands.CanaryTest));
            if (result == null) return;
            Ui.Info(FindForm(), result);
            await Task.Delay(6000);
            if (!IsDisposed) await LoadAsync();
        }

        public sealed class FolderRow
        {
            public FolderRow(CanaryFolderDto item)
            {
                Item = item;
            }

            internal CanaryFolderDto Item { get; private set; }

            [System.ComponentModel.DisplayName("Klasör")]
            public string Path { get { return Item.Path; } }

            [System.ComponentModel.DisplayName("Kaynak")]
            public string Source { get { return Item.Source; } }

            [System.ComponentModel.DisplayName("Durum")]
            public string State { get { return Item.State; } }
        }

        public sealed class FileRow
        {
            public FileRow(CanaryFileDto item)
            {
                Item = item;
            }

            internal CanaryFileDto Item { get; private set; }

            [System.ComponentModel.DisplayName("Dosya")]
            public string Name { get { return Item.Name; } }

            [System.ComponentModel.DisplayName("Durum")]
            public string State { get { return Item.State; } }

            [System.ComponentModel.DisplayName("Konma zamanı")]
            public DateTime? Created { get { return UtcTime.ToLocal(Item.CreatedUtc); } }
        }
    }
}
