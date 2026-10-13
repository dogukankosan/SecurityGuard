using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Forms;
using DevExpress.XtraEditors;
using DevExpress.XtraGrid;
using DevExpress.XtraGrid.Views.Grid;
using SecurityGuard.Shared.Contracts;
using SecurityGuard.Shared.Dto;
using SecurityGuardUI.Forms;
using SecurityGuardUI.Helpers;

namespace SecurityGuardUI.Modules
{
    /// <summary>
    /// Port Kısıtları: "bu porta sadece şu IP'ler bağlanabilsin". Servis her kısıt için firewall'a kendi ENGELLE kuralını yazar
    /// (izinli IP'ler dışındaki herkes); Windows'un kendi kurallarına dokunulmaz.
    /// Kilitlenme koruması: kaydedilen değişiklik uygulanır, süre içinde onaylanmazsa servis geri alır.
    /// </summary>
    public sealed class PortRestrictionsModule : ModuleBase
    {
        private readonly GridControl _grid = new GridControl { Dock = DockStyle.Fill };
        private readonly GridView _view;
        private readonly LabelControl _status = new LabelControl();
        private readonly LabelControl _warning = new LabelControl();
        private readonly ComboBoxEdit _template = new ComboBoxEdit { Width = 220 };
        private readonly SimpleButton _confirm = new SimpleButton { Text = "Onayla" };
        private readonly SimpleButton _revert = new SimpleButton { Text = "Geri al" };
        private readonly Timer _poll = new Timer { Interval = 1000 };
        private PortRestrictionStateDto _state;
        private DateTime _lastLoadUtc;

        public PortRestrictionsModule()
        {
            _view = new GridView(_grid);
            _grid.MainView = _view;
            _grid.ViewCollection.Add(_view);
            GridHelper.SetupReadOnly(_view);
            Build();
            _poll.Tick += async (s, e) => await PollAsync();
            Disposed += (s, e) => _poll.Dispose();
        }

        private void Build()
        {
            var top = new PanelControl { Dock = DockStyle.Top, Height = 132, BorderStyle = DevExpress.XtraEditors.Controls.BorderStyles.NoBorder };
            var title = new LabelControl { Text = "Port Kısıtları", Location = new Point(12, 8) };
            title.Appearance.Font = new Font("Segoe UI", 13F);
            title.Appearance.Options.UseFont = true;
            var info = new LabelControl
            {
                Location = new Point(12, 36),
                AutoSizeMode = LabelAutoSizeMode.None,
                Size = new Size(1000, 36),
                Text = "Seçilen porta sadece listedeki IP'ler (ve isterseniz iç ağ) bağlanabilir; diğer herkes Windows Firewall'da engellenir. "
                       + "Windows'un kendi kuralları değiştirilmez, SecurityGuard ayrı bir engel kuralı yazar. "
                       + "Kaydettiğiniz değişiklik hemen uygulanır ve onaylamazsanız 5 dakika sonra kendiliğinden geri alınır (kilitlenme koruması)."
            };
            info.Appearance.TextOptions.WordWrap = DevExpress.Utils.WordWrap.Wrap;
            info.Appearance.Options.UseTextOptions = true;
            top.Resize += (s, e) => info.Width = Math.Max(300, top.Width - 24);

            _warning.Location = new Point(12, 76);
            _warning.Appearance.ForeColor = UiColors.Error;
            _warning.Appearance.Options.UseForeColor = true;
            _warning.Visible = false;

            int x = 12, y = 98;
            Func<string, int, EventHandler, SimpleButton> button = (text, width, click) =>
            {
                var b = new SimpleButton { Text = text, Location = new Point(x, y), Size = new Size(width, 26) };
                b.Click += click;
                top.Controls.Add(b);
                x += width + 6;
                return b;
            };
            button("Yeni kısıt...", 100, async (s, e) => await EditAsync(new PortRestrictionDto { Protocol = "TCP", AllowLan = true, Enabled = true }));
            _template.Location = new Point(x + 6, y + 2);
            _template.Properties.TextEditStyle = DevExpress.XtraEditors.Controls.TextEditStyles.DisableTextEditor;
            _template.Properties.NullText = "Hazır şablon seçin";
            top.Controls.Add(_template);
            x += 232;
            button("Şablondan ekle...", 120, async (s, e) => await FromTemplateAsync());
            x += 16;
            button("Düzenle...", 90, async (s, e) => await EditSelectedAsync());
            _confirm.Location = new Point(x, y);
            _confirm.Size = new Size(80, 26);
            _confirm.Click += async (s, e) => await ConfirmSelectedAsync();
            top.Controls.Add(_confirm);
            x += 86;
            _revert.Location = new Point(x, y);
            _revert.Size = new Size(80, 26);
            _revert.Click += async (s, e) => await RevertSelectedAsync();
            top.Controls.Add(_revert);
            x += 86;
            button("Kapat", 70, async (s, e) => await DisableSelectedAsync());
            button("Sil", 60, async (s, e) => await DeleteSelectedAsync());
            button("Yenile", 70, async (s, e) => await LoadAsync());

            top.Controls.Add(title);
            top.Controls.Add(info);
            top.Controls.Add(_warning);

            var bottom = new PanelControl { Dock = DockStyle.Bottom, Height = 28, BorderStyle = DevExpress.XtraEditors.Controls.BorderStyles.NoBorder };
            _status.Location = new Point(12, 6);
            bottom.Controls.Add(_status);

            Controls.Add(_grid);
            Controls.Add(top);
            Controls.Add(bottom);

            _view.FocusedRowChanged += (s, e) => UpdateButtons();
            _view.DoubleClick += async (s, e) => await EditSelectedAsync();
            _view.RowStyle += (s, e) =>
            {
                var row = _view.GetRow(e.RowHandle) as Row;
                if (row == null) return;
                if (row.Item.PendingUntilUtc != null) e.Appearance.ForeColor = UiColors.Warning;
                else if (!row.Item.Enabled) e.Appearance.ForeColor = DevExpress.LookAndFeel.DXSkinColors.ForeColors.DisabledText;
                else if (row.State.StartsWith("Firewall", StringComparison.Ordinal)) e.Appearance.ForeColor = UiColors.Error;
            };
        }

        // Başka sayfadan (Açık Portlar) gelen "yeni kısıt" isteği: sayfa açılınca form doldurulmuş olarak açılır
        private static PortRestrictionDto _pendingNew;
        private static event Action PendingNewRequested;

        /// <summary>Port Kısıtları sayfası açıldığında bu kısıt için düzenleme formu açılsın.</summary>
        public static void RequestNew(PortRestrictionDto item)
        {
            _pendingNew = item;
            var h = PendingNewRequested;
            if (h != null) h();
        }

        private bool _loadedOnce;

        protected override async Task OnFirstShowAsync()
        {
            PendingNewRequested += OnPendingNew;
            Disposed += (s, e) => PendingNewRequested -= OnPendingNew;
            await LoadAsync();
            _loadedOnce = true;
            await OpenPendingAsync();
        }

        private async void OnPendingNew()
        {
            if (!_loadedOnce || IsDisposed) return;
            if (InvokeRequired)
            {
                BeginInvoke(new Action(OnPendingNew));
                return;
            }
            await OpenPendingAsync();
        }

        private async Task OpenPendingAsync()
        {
            var item = _pendingNew;
            _pendingNew = null;
            if (item == null) return;
            var existing = _state == null ? null : _state.Items.FirstOrDefault(i => i.Ports == item.Ports && (i.Protocol == item.Protocol || i.Protocol == "BOTH" || item.Protocol == "BOTH"));
            if (existing != null)
            {
                Ui.Info(FindForm(), "Bu port için zaten bir kısıt var: \"" + existing.Name + "\". O kayıt açılıyor.");
                item = existing;
            }
            await EditAsync(item);
        }

        private async Task LoadAsync()
        {
            var state = await CallAsync(c => c.SendAsync<PortRestrictionStateDto>(Commands.PortRestrictGet));
            if (state != null) Bind(state);
        }

        private void Bind(PortRestrictionStateDto state)
        {
            _state = state;
            _lastLoadUtc = DateTime.UtcNow;
            long focused = FocusedItem() != null ? FocusedItem().Item.Id : 0;

            _grid.DataSource = state.Items.Select(i => new Row(i)).ToList();
            _view.BestFitColumns();
            if (_view.Columns["Addresses"] != null) _view.Columns["Addresses"].Width = Math.Min(_view.Columns["Addresses"].Width, 420);
            for (int h = 0; h < _view.RowCount; h++)
                if (((Row)_view.GetRow(h)).Item.Id == focused) _view.FocusedRowHandle = h;

            string selected = _template.SelectedItem as string;
            _template.Properties.Items.Clear();
            foreach (var t in state.Templates) _template.Properties.Items.Add(TemplateText(t));
            if (selected != null && _template.Properties.Items.Contains(selected)) _template.SelectedItem = selected;
            else if (_template.Properties.Items.Count > 0) _template.SelectedIndex = 0;

            string warning = state.FirewallOffWarning ?? (state.FirewallOk ? null : state.FirewallMessage) ?? state.GeoWarning;
            _warning.Text = warning ?? "";
            _warning.Visible = warning != null;

            int on = state.Items.Count(i => i.Enabled);
            int pending = state.Items.Count(i => i.PendingUntilUtc != null);
            _status.Text = state.Items.Count + " kısıt, " + on + " açık" + (pending > 0 ? ", " + pending + " onay bekliyor" : "") + ". " + (state.FirewallOk ? state.FirewallMessage : "");
            _poll.Enabled = pending > 0;
            UpdateButtons();
        }

        private static string TemplateText(PortTemplateDto t)
        {
            return t.Name + " - " + (t.Protocol == "BOTH" ? "TCP+UDP" : t.Protocol) + " " + t.Ports;
        }

        private Row FocusedItem()
        {
            return _view.GetRow(_view.FocusedRowHandle) as Row;
        }

        private void UpdateButtons()
        {
            var row = FocusedItem();
            bool pending = row != null && row.Item.PendingUntilUtc != null;
            _confirm.Enabled = pending;
            _revert.Enabled = pending;
        }

        /// <summary>Onay bekleyen varken: kalan süre her saniye, durum 5 saniyede bir yenilenir.</summary>
        private async Task PollAsync()
        {
            if (!Visible || _state == null) return;
            _view.RefreshData();
            if (DateTime.UtcNow - _lastLoadUtc < TimeSpan.FromSeconds(5)) return;
            _lastLoadUtc = DateTime.UtcNow;
            var result = await Client.SendAsync<PortRestrictionStateDto>(Commands.PortRestrictGet);
            if (!IsDisposed && result.Success) Bind(result.Value);
        }

        // ================================================================ Ekle / düzenle

        private async Task FromTemplateAsync()
        {
            if (_state == null || _template.SelectedIndex < 0) return;
            var t = _state.Templates[_template.SelectedIndex];
            var existing = _state.Items.FirstOrDefault(i => i.Ports == t.Ports && (i.Protocol == t.Protocol || i.Protocol == "BOTH" || t.Protocol == "BOTH"));
            if (existing != null)
            {
                Ui.Info(FindForm(), "Bu port için zaten bir kısıt var: \"" + existing.Name + "\". O kaydı düzenleyin.");
                await EditAsync(existing);
                return;
            }
            await EditAsync(new PortRestrictionDto
            {
                Name = t.Name,
                Protocol = t.Protocol,
                Ports = t.Ports,
                AllowLan = true,
                Enabled = true,
                Note = t.Note
            });
        }

        private async Task EditSelectedAsync()
        {
            var row = FocusedItem();
            if (row == null)
            {
                Ui.Warning(FindForm(), "Düzenlenecek kısıtı seçin.");
                return;
            }
            await EditAsync(row.Item);
        }

        private async Task EditAsync(PortRestrictionDto item)
        {
            string client = RdpSession.GetClientAddress();
            using (var form = new PortRestrictionEditForm(item, client, CheckAsync, _state == null ? null : _state.Countries, _state == null ? null : _state.GeoInfo))
            {
                if (form.ShowDialog(FindForm()) != DialogResult.OK) return;
                var saved = await CallAsync(c => c.SendAsync<IdRequest>(Commands.PortRestrictSave, form.Request));
                await LoadAsync();
                if (saved == null || !form.Request.Item.Enabled) return;
                await AskConfirmAsync(saved.Id, form.Request.Item.Name);
            }
        }

        /// <summary>Form "Kaydet"te çağırır: servis kontrolü + kullanıcı onayları. false → form açık kalır.</summary>
        private async Task<bool> CheckAsync(PortRestrictionEditForm form, PortRestrictionSaveRequest request)
        {
            var result = await Client.SendAsync<PortRestrictionCheckDto>(Commands.PortRestrictCheck, request);
            if (result.IsUnauthorized)
            {
                Host.HandleSessionExpired();
                return false;
            }
            if (!result.Success)
            {
                Ui.Error(form, result.ErrorMessage);
                return false;
            }
            var check = result.Value;
            if (check.Error != null)
            {
                Ui.Warning(form, check.Error);
                return false;
            }
            if (!request.Item.Enabled) return true;

            if (check.BlockedSessions.Count > 0)
            {
                if (!Ui.Confirm(form, "Şu an bağlı olan şu RDP oturumlarının IP'si izinli listede yok:\r\n\r\n  " + string.Join("\r\n  ", check.BlockedSessions)
                                      + "\r\n\r\nMevcut bağlantıları hemen kopmaz ama bağlantı koptuğunda / yeniden bağlanmak istediklerinde bağlanamazlar. Devam edilsin mi?"))
                    return false;
                request.AcceptOtherSessions = true;
            }

            return Ui.Confirm(form, check.BlockedSummary + "\r\n\r\n"
                                    + "Kısıt şimdi firewall'a uygulanacak. " + (_state != null ? _state.ConfirmSeconds / 60 : 5) + " dakika içinde ONAYLAMAZSANIZ otomatik geri alınır.\r\n\r\n"
                                    + "ÖNEMLİ: Firewall mevcut bağlantıları kesmez; bu oturumun açık kalması erişimin çalıştığını göstermez. "
                                    + "Onaylamadan önce izinli bir IP'den YENİ bir bağlantı açıp (ör. yeni RDP penceresi) bağlanabildiğinizi deneyin.\r\n\r\nUygulansın mı?");
        }

        private async Task AskConfirmAsync(long id, string name)
        {
            var item = _state == null ? null : _state.Items.FirstOrDefault(i => i.Id == id);
            if (item == null || item.PendingUntilUtc == null) return;
            DialogResult answer;
            using (var form = new PendingConfirmForm(name, item.PendingUntilUtc.Value))
                answer = form.ShowDialog(FindForm());
            if (answer == DialogResult.OK) await ConfirmAsync(id);
            else if (answer == DialogResult.No) await RevertAsync(id);
        }

        private async Task ConfirmSelectedAsync()
        {
            var row = FocusedItem();
            if (row == null || row.Item.PendingUntilUtc == null) return;
            if (!Ui.Confirm(FindForm(), "\"" + row.Item.Name + "\" kısıtı onaylansın mı?\r\n\r\nİzinli bir IP'den YENİ bir bağlantıyla erişimi test ettiyseniz onaylayın. "
                                        + "Onaydan sonra otomatik geri alma yapılmaz.")) return;
            await ConfirmAsync(row.Item.Id);
        }

        private async Task ConfirmAsync(long id)
        {
            if (await CallAsync(c => c.SendAsync(Commands.PortRestrictConfirm, new IdRequest { Id = id })))
                Ui.Info(FindForm(), "Kısıt onaylandı ve kalıcı olarak uygulandı.");
            await LoadAsync();
        }

        private async Task RevertSelectedAsync()
        {
            var row = FocusedItem();
            if (row == null || row.Item.PendingUntilUtc == null) return;
            if (!Ui.Confirm(FindForm(), "\"" + row.Item.Name + "\" için yapılan son değişiklik geri alınsın mı? (Yeni kısıtsa kapatılır.)")) return;
            await RevertAsync(row.Item.Id);
        }

        private async Task RevertAsync(long id)
        {
            await CallAsync(c => c.SendAsync(Commands.PortRestrictRevert, new IdRequest { Id = id }));
            await LoadAsync();
        }

        private async Task DisableSelectedAsync()
        {
            var row = FocusedItem();
            if (row == null || !row.Item.Enabled) return;
            if (!Ui.Confirm(FindForm(), "\"" + row.Item.Name + "\" kısıtı kapatılsın mı?\r\n\r\n" + row.Ports + " portu yeniden herkese açık olur "
                                        + "(Windows'un kendi firewall kuralları ne diyorsa o geçerli olur). Kayıt silinmez, sonra tekrar açabilirsiniz.")) return;
            await CallAsync(c => c.SendAsync(Commands.PortRestrictDisable, new IdRequest { Id = row.Item.Id }));
            await LoadAsync();
        }

        private async Task DeleteSelectedAsync()
        {
            var row = FocusedItem();
            if (row == null) return;
            if (!Ui.Confirm(FindForm(), "\"" + row.Item.Name + "\" kısıtı silinsin mi?" + (row.Item.Enabled ? "\r\n\r\n" + row.Ports + " portu yeniden herkese açık olur." : ""))) return;
            await CallAsync(c => c.SendAsync(Commands.PortRestrictDelete, new IdRequest { Id = row.Item.Id }));
            await LoadAsync();
        }

        public sealed class Row
        {
            public Row(PortRestrictionDto item)
            {
                Item = item;
            }

            internal PortRestrictionDto Item { get; private set; }

            [System.ComponentModel.DisplayName("Ad")]
            public string Name { get { return Item.Name; } }

            [System.ComponentModel.DisplayName("Port")]
            public string Ports { get { return (Item.Protocol == "BOTH" ? "TCP+UDP" : Item.Protocol) + " " + Item.Ports; } }

            [System.ComponentModel.DisplayName("İzinli IP'ler")]
            public string Addresses
            {
                get
                {
                    var lines = (Item.Addresses ?? "").Split(new[] { "\r\n", "\n" }, StringSplitOptions.RemoveEmptyEntries).Select(l => l.Trim()).Where(l => l.Length > 0).ToList();
                    string text = lines.Count == 0 ? "(yok)" : string.Join(", ", lines);
                    if (!string.IsNullOrEmpty(Item.Countries)) text = (lines.Count == 0 ? "" : text + "  + ") + "ülke: " + Item.Countries.Replace(",", ", ");
                    return text + (Item.AllowLan ? "  + iç ağ" : "");
                }
            }

            [System.ComponentModel.DisplayName("Durum")]
            public string State
            {
                get
                {
                    if (Item.PendingUntilUtc == null) return Item.State;
                    var left = Item.PendingUntilUtc.Value.ToUniversalTime() - DateTime.UtcNow;
                    return left <= TimeSpan.Zero ? "Onay süresi doldu (geri alınıyor)" : "Onay bekliyor: " + left.ToString(@"m\:ss") + " kaldı";
                }
            }

            [System.ComponentModel.DisplayName("Firewall kuralı")]
            public string RuleNames { get { return Item.RuleNames; } }

            [System.ComponentModel.DisplayName("Not")]
            public string Note { get { return Item.Note; } }

            [System.ComponentModel.DisplayName("Değiştiren")]
            public string UpdatedBy { get { return Item.UpdatedBy; } }

            [System.ComponentModel.DisplayName("Değişiklik")]
            public DateTime? Updated { get { return Item.UpdatedUtc.HasValue ? Item.UpdatedUtc.Value.ToLocalTime() : (DateTime?)null; } }
        }
    }
}
