using System;
using System.Collections.Generic;
using System.Linq;
using System.Drawing;
using System.Threading.Tasks;
using System.Windows.Forms;
using DevExpress.XtraEditors;
using SecurityGuard.Shared.Dto;
using SecurityGuardUI.Helpers;

namespace SecurityGuardUI.Forms
{
    /// <summary>Port kısıtı ekle / düzenle. "Kaydet"te servis kontrolü (kendi IP, çakışma, adres hatası) form açıkken yapılır.</summary>
    public sealed class PortRestrictionEditForm : XtraForm
    {
        private static readonly string[] ProtocolCodes = { "TCP", "UDP", "BOTH" };
        private static readonly string[] ProtocolTexts = { "TCP", "UDP", "TCP + UDP" };

        private readonly PortRestrictionDto _item;
        private readonly string _clientAddress;
        private readonly Func<PortRestrictionEditForm, PortRestrictionSaveRequest, Task<bool>> _check;

        private readonly TextEdit _name = new TextEdit();
        private readonly ComboBoxEdit _protocol = new ComboBoxEdit();
        private readonly TextEdit _ports = new TextEdit();
        private readonly MemoEdit _addresses = new MemoEdit();
        private readonly CheckEdit _lan = new CheckEdit();
        private readonly CheckedComboBoxEdit _countries = new CheckedComboBoxEdit();
        private readonly List<CountryDto> _countryList;
        private readonly string _geoInfo;
        private readonly CheckEdit _enabled = new CheckEdit();
        private readonly TextEdit _note = new TextEdit();
        private readonly SimpleButton _save = new SimpleButton { Text = "Kaydet", Size = new Size(110, 30) };
        private readonly SimpleButton _cancel = new SimpleButton { Text = "İptal", Size = new Size(90, 30), DialogResult = DialogResult.Cancel };
        private bool _busy;

        public PortRestrictionEditForm(PortRestrictionDto item, string clientAddress, Func<PortRestrictionEditForm, PortRestrictionSaveRequest, Task<bool>> check,
            List<CountryDto> countries = null, string geoInfo = null)
        {
            _countryList = countries ?? new List<CountryDto>();
            _geoInfo = geoInfo;
            _item = item;
            _clientAddress = clientAddress;
            _check = check;
            Build();
            Bind();
        }

        /// <summary>Kaydet onaylandıysa servise gönderilecek istek.</summary>
        public PortRestrictionSaveRequest Request { get; private set; }

        private void Build()
        {
            Text = _item.Id > 0 ? "Port kısıtını düzenle" : "Yeni port kısıtı";
            Icon = AppImages.Icon;
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = MinimizeBox = false;
            StartPosition = FormStartPosition.CenterParent;
            ClientSize = new Size(640, 618);
            CancelButton = _cancel;

            int y = 16;
            Action<string, Control, int> row = (label, control, height) =>
            {
                Controls.Add(new LabelControl { Text = label, Location = new Point(16, y + 3) });
                control.Location = new Point(150, y);
                control.Size = new Size(470, height);
                Controls.Add(control);
                y += height + 10;
            };

            row("Ad", _name, 22);
            _protocol.Properties.Items.AddRange(ProtocolTexts);
            _protocol.Properties.TextEditStyle = DevExpress.XtraEditors.Controls.TextEditStyles.DisableTextEditor;
            row("Protokol", _protocol, 22);
            row("Port(lar)", _ports, 22);
            var portHint = Hint("Örnek: 3389  ·  1433,1434  ·  5000-5100", y - 8);
            y += 14;

            row("İzinli IP'ler", _addresses, 170);
            var ownIp = new SimpleButton { Text = "Kendi IP'mi ekle", Location = new Point(16, y - 150), Size = new Size(124, 26), Enabled = _clientAddress != null };
            ownIp.Click += (s, e) => AddOwnAddress();
            Controls.Add(ownIp);
            if (_clientAddress != null) Controls.Add(new LabelControl { Text = "(" + _clientAddress + ")", Location = new Point(18, y - 120) });
            Hint("Satır başına: 85.105.0.10  ·  85.105.0.0/24  ·  85.105.0.10-85.105.0.20   (açıklama: # merkez)", y - 8);
            y += 14;

            // Ülkeler: seçilen ülkelerin tüm IP'leri izinli (sabit IP'si olmayan kullanıcılar için)
            _countries.Properties.SeparatorChar = ',';
            _countries.Properties.DropDownRows = 18;
            _countries.Properties.NullText = _countryList.Count == 0 ? "(ülke veritabanı yok)" : "Ülke seçilmedi (sadece yukarıdaki IP'ler)";
            foreach (var c in _countryList) _countries.Properties.Items.Add(c.Code, c.Name + " (" + c.Code + ")");
            _countries.Enabled = _countryList.Count > 0;
            row("İzinli ülkeler", _countries, 22);
            Hint("Seçilen ülkelerden gelen her IP bağlanabilir (değişen IP'li kullanıcılar için).", y - 8);
            if (_geoInfo != null) Hint(_geoInfo, y + 8);
            y += 32;

            _lan.Properties.Caption = "İç ağ her zaman izinli (10.x, 172.16-31.x, 192.168.x, link-local)";
            row("", _lan, 22);
            _enabled.Properties.Caption = "Açık (kaydedince firewall'a uygula)";
            row("", _enabled, 22);
            row("Not", _note, 22);

            var warn = new LabelControl
            {
                Location = new Point(16, y + 2),
                AutoSizeMode = LabelAutoSizeMode.None,
                Size = new Size(604, 48),
                Text = "Kaydettiğinizde kısıt hemen uygulanır; onaylamazsanız 5 dakika sonra geri alınır. "
                       + "IP'si değişen kullanıcılar için IP yerine ülke seçin; sabit IP'ler için ülke gerekmez."
            };
            warn.Appearance.TextOptions.WordWrap = DevExpress.Utils.WordWrap.Wrap;
            warn.Appearance.Options.UseTextOptions = true;
            Controls.Add(warn);

            _save.Location = new Point(ClientSize.Width - 16 - _cancel.Width - 8 - _save.Width, ClientSize.Height - 46);
            _cancel.Location = new Point(ClientSize.Width - 16 - _cancel.Width, ClientSize.Height - 46);
            _save.Click += async (s, e) => await SaveAsync();
            Controls.Add(_save);
            Controls.Add(_cancel);
            portHint.BringToFront();
        }

        private LabelControl Hint(string text, int y)
        {
            var label = new LabelControl { Text = text, Location = new Point(152, y) };
            label.Appearance.ForeColor = DevExpress.LookAndFeel.DXSkinColors.ForeColors.DisabledText;
            label.Appearance.Options.UseForeColor = true;
            Controls.Add(label);
            return label;
        }

        private void Bind()
        {
            _name.Text = _item.Name;
            int p = Array.IndexOf(ProtocolCodes, _item.Protocol ?? "TCP");
            _protocol.SelectedIndex = p < 0 ? 0 : p;
            _ports.Text = _item.Ports;
            _addresses.Text = _item.Addresses;
            _lan.Checked = _item.AllowLan;
            _enabled.Checked = _item.Enabled;
            _note.Text = _item.Note;
            var selected = new HashSet<string>((_item.Countries ?? "").Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries).Select(c => c.Trim()), StringComparer.OrdinalIgnoreCase);
            foreach (DevExpress.XtraEditors.Controls.CheckedListBoxItem i in _countries.Properties.Items)
                i.CheckState = selected.Contains((string)i.Value) ? CheckState.Checked : CheckState.Unchecked;
        }

        private void AddOwnAddress()
        {
            if (_clientAddress == null) return;
            if ((_addresses.Text ?? "").Contains(_clientAddress)) return;
            string text = (_addresses.Text ?? "").TrimEnd();
            _addresses.Text = (text.Length == 0 ? "" : text + Environment.NewLine) + _clientAddress + "  # " + Environment.UserName + " (bu bağlantı)";
        }

        private async Task SaveAsync()
        {
            if (_busy) return;
            var request = new PortRestrictionSaveRequest
            {
                ClientAddress = _clientAddress,
                Item = new PortRestrictionDto
                {
                    Id = _item.Id,
                    Name = _name.Text,
                    Protocol = ProtocolCodes[Math.Max(0, _protocol.SelectedIndex)],
                    Ports = _ports.Text,
                    Addresses = _addresses.Text,
                    AllowLan = _lan.Checked,
                    Enabled = _enabled.Checked,
                    Note = _note.Text,
                    Countries = string.Join(",", _countries.Properties.Items.Cast<DevExpress.XtraEditors.Controls.CheckedListBoxItem>()
                        .Where(i => i.CheckState == CheckState.Checked).Select(i => (string)i.Value))
                }
            };
            _busy = true;
            _save.Enabled = false;
            try
            {
                if (!await _check(this, request)) return;
            }
            finally
            {
                _busy = false;
                if (!IsDisposed) _save.Enabled = true;
            }
            Request = request;
            DialogResult = DialogResult.OK;
        }
    }

    /// <summary>Uygulanan kısıt için geri sayım: Onayla (OK) / Geri al (No) / Sonra (Cancel; süre dolunca servis geri alır).</summary>
    public sealed class PendingConfirmForm : XtraForm
    {
        private readonly DateTime _untilUtc;
        private readonly LabelControl _countdown = new LabelControl();
        private readonly Timer _timer = new Timer { Interval = 500 };

        public PendingConfirmForm(string name, DateTime untilUtc)
        {
            _untilUtc = untilUtc.ToUniversalTime();
            Text = "Kısıtı onaylayın";
            Icon = AppImages.Icon;
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = MinimizeBox = false;
            StartPosition = FormStartPosition.CenterParent;
            ClientSize = new Size(560, 250);

            var text = new LabelControl
            {
                Location = new Point(16, 16),
                AutoSizeMode = LabelAutoSizeMode.None,
                Size = new Size(528, 120),
                Text = "\"" + name + "\" firewall'a uygulandı.\r\n\r\n"
                       + "Şimdi izinli bir IP'den YENİ bir bağlantı açıp (ör. başka bir RDP penceresi) erişimin çalıştığını deneyin. "
                       + "Mevcut bağlantılar firewall tarafından kesilmez; bu pencerenin açık kalması erişimin çalıştığını göstermez.\r\n\r\n"
                       + "Onaylamazsanız süre sonunda kısıt otomatik geri alınır."
            };
            text.Appearance.TextOptions.WordWrap = DevExpress.Utils.WordWrap.Wrap;
            text.Appearance.Options.UseTextOptions = true;
            Controls.Add(text);

            _countdown.Location = new Point(16, 146);
            _countdown.Appearance.Font = new Font("Segoe UI", 14F, FontStyle.Bold);
            _countdown.Appearance.ForeColor = UiColors.Warning;
            _countdown.Appearance.Options.UseFont = true;
            _countdown.Appearance.Options.UseForeColor = true;
            Controls.Add(_countdown);

            var ok = new SimpleButton { Text = "Onayla (erişim çalışıyor)", Size = new Size(180, 30), Location = new Point(16, 200), DialogResult = DialogResult.OK };
            var revert = new SimpleButton { Text = "Hemen geri al", Size = new Size(130, 30), Location = new Point(204, 200), DialogResult = DialogResult.No };
            var later = new SimpleButton { Text = "Sonra karar ver", Size = new Size(130, 30), Location = new Point(414, 200), DialogResult = DialogResult.Cancel };
            Controls.Add(ok);
            Controls.Add(revert);
            Controls.Add(later);
            CancelButton = later;

            _timer.Tick += (s, e) => Tick();
            Tick();
            _timer.Start();
            FormClosed += (s, e) => _timer.Dispose();
        }

        private void Tick()
        {
            var left = _untilUtc - DateTime.UtcNow;
            if (left <= TimeSpan.Zero)
            {
                _timer.Stop();
                Ui.Warning(this, "Süre doldu: servis değişikliği geri alıyor.");
                DialogResult = DialogResult.Cancel;
                return;
            }
            _countdown.Text = "Otomatik geri almaya: " + left.ToString(@"m\:ss");
        }
    }
}
