using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;
using DevExpress.XtraEditors;
using SecurityGuard.Shared.Dto;
using SecurityGuardUI.Helpers;

namespace SecurityGuardUI.Forms
{
    /// <summary>Mesai dışı uyarısı için kullanıcı istisnaları: "her zaman serbest" veya kişiye özel gün / saat.</summary>
    public sealed class WorkHoursForm : XtraForm
    {
        private static readonly string[] DayNames = { "Pazar", "Pazartesi", "Salı", "Çarşamba", "Perşembe", "Cuma", "Cumartesi" };
        private static readonly int[] DayOrder = { 1, 2, 3, 4, 5, 6, 0 };
        private static readonly string[] DayShort = { "Paz", "Pzt", "Sal", "Çar", "Per", "Cum", "Cmt" };

        private readonly List<WorkHoursExceptionDto> _items;
        private readonly ListBoxControl _list = new ListBoxControl();
        private readonly ComboBoxEdit _user = new ComboBoxEdit();
        private readonly RadioGroup _mode = new RadioGroup();
        private readonly CheckEdit[] _days = new CheckEdit[7];
        private readonly TextEdit _start = new TextEdit();
        private readonly TextEdit _end = new TextEdit();
        private readonly TextEdit _note = new TextEdit();

        public WorkHoursForm(WorkHoursDto dto)
        {
            _items = dto.Exceptions.Select(Copy).ToList();
            Build(dto);
            RefreshList();
            ShowItem(null);
        }

        public List<WorkHoursExceptionDto> Exceptions { get { return _items; } }

        private void Build(WorkHoursDto dto)
        {
            Text = "Mesai istisnaları";
            Icon = AppImages.Icon;
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = MinimizeBox = false;
            StartPosition = FormStartPosition.CenterParent;
            ClientSize = new Size(760, 470);

            var info = new LabelControl
            {
                Location = new Point(16, 12),
                AutoSizeMode = LabelAutoSizeMode.None,
                Size = new Size(728, 48),
                Text = "Genel mesai: " + dto.General + (dto.AlarmEnabled ? "" : " (mesai dışı uyarısı KAPALI: Genel Ayarlar → Mesai saatleri)")
                       + ". Buradaki kişiler için genel mesai yerine kendi ayarı geçerli: IT / nöbetçi gibi her saatte bağlananlar için \"her zaman serbest\", vardiyalı çalışanlar için kendi gün ve saatleri."
            };
            info.Appearance.TextOptions.WordWrap = DevExpress.Utils.WordWrap.Wrap;
            info.Appearance.Options.UseTextOptions = true;
            Controls.Add(info);

            _list.Location = new Point(16, 70);
            _list.Size = new Size(300, 330);
            _list.SelectedIndexChanged += (s, e) => ShowItem(_list.SelectedIndex >= 0 ? _items[_list.SelectedIndex] : null);
            Controls.Add(_list);

            int x = 336, y = 70;
            Controls.Add(new LabelControl { Text = "Kullanıcı", Location = new Point(x, y + 3) });
            _user.Location = new Point(x + 90, y);
            _user.Width = 318;
            _user.Properties.Items.AddRange(dto.KnownUsers.ToArray());
            _user.Properties.NullText = "kullanıcı veya ETKİALANI\\kullanıcı";
            Controls.Add(_user);
            y += 34;
            _mode.Location = new Point(x, y);
            _mode.Size = new Size(408, 56);
            _mode.Properties.Columns = 1;
            _mode.Properties.Items.Add(new DevExpress.XtraEditors.Controls.RadioGroupItem("always", "Her zaman serbest (mesai dışı uyarısı yok)"));
            _mode.Properties.Items.Add(new DevExpress.XtraEditors.Controls.RadioGroupItem("custom", "Kişiye özel gün ve saat"));
            _mode.SelectedIndexChanged += (s, e) => EnableCustom();
            Controls.Add(_mode);
            y += 62;
            for (int i = 0; i < 7; i++)
            {
                int day = DayOrder[i];
                _days[day] = new CheckEdit { Location = new Point(x + (i % 4) * 102, y + (i / 4) * 24), Width = 100 };
                _days[day].Properties.Caption = DayNames[day];
                Controls.Add(_days[day]);
            }
            y += 56;
            Controls.Add(new LabelControl { Text = "Saat", Location = new Point(x, y + 3) });
            _start.Location = new Point(x + 90, y);
            _start.Width = 60;
            _end.Location = new Point(x + 170, y);
            _end.Width = 60;
            Controls.Add(_start);
            Controls.Add(new LabelControl { Text = "-", Location = new Point(x + 157, y + 3) });
            Controls.Add(_end);
            Controls.Add(new LabelControl { Text = "(SS:dd; bitiş < başlangıç = gece)", Location = new Point(x + 240, y + 3) });
            y += 34;
            Controls.Add(new LabelControl { Text = "Not", Location = new Point(x, y + 3) });
            _note.Location = new Point(x + 90, y);
            _note.Width = 318;
            Controls.Add(_note);
            y += 40;

            var save = new SimpleButton { Text = "Listeye ekle / güncelle", Location = new Point(x, y), Size = new Size(170, 28) };
            save.Click += (s, e) => AddOrUpdate();
            var remove = new SimpleButton { Text = "Listeden çıkar", Location = new Point(x + 178, y), Size = new Size(120, 28) };
            remove.Click += (s, e) => Remove();
            var clear = new SimpleButton { Text = "Yeni", Location = new Point(x + 306, y), Size = new Size(70, 28) };
            clear.Click += (s, e) => { _list.SelectedIndex = -1; ShowItem(null); };
            Controls.AddRange(new Control[] { save, remove, clear });

            var ok = new SimpleButton { Text = "Kaydet", Size = new Size(110, 30), Location = new Point(ClientSize.Width - 16 - 110 - 8 - 90, ClientSize.Height - 46), DialogResult = DialogResult.OK };
            var cancel = new SimpleButton { Text = "İptal", Size = new Size(90, 30), Location = new Point(ClientSize.Width - 16 - 90, ClientSize.Height - 46), DialogResult = DialogResult.Cancel };
            Controls.Add(ok);
            Controls.Add(cancel);
            AcceptButton = ok;
            CancelButton = cancel;
        }

        private void ShowItem(WorkHoursExceptionDto e)
        {
            _user.Text = e == null ? "" : e.User;
            _mode.EditValue = e == null ? "always" : e.Mode;
            var days = e == null || e.Days == null || e.Days.Count == 0 ? new List<int> { 1, 2, 3, 4, 5 } : e.Days;
            for (int d = 0; d < 7; d++) _days[d].Checked = days.Contains(d);
            _start.Text = e == null || e.Start == null ? "08:00" : e.Start;
            _end.Text = e == null || e.End == null ? "19:00" : e.End;
            _note.Text = e == null ? "" : e.Note;
            EnableCustom();
        }

        private void EnableCustom()
        {
            bool custom = (string)_mode.EditValue == "custom";
            foreach (var c in _days) c.Enabled = custom;
            _start.Enabled = _end.Enabled = custom;
        }

        private void AddOrUpdate()
        {
            string user = (_user.Text ?? "").Trim();
            if (user.Length == 0)
            {
                Ui.Warning(this, "Kullanıcı adını yazın veya listeden seçin.");
                return;
            }
            var item = new WorkHoursExceptionDto
            {
                User = user,
                Mode = (string)_mode.EditValue == "custom" ? "custom" : "always",
                Days = Enumerable.Range(0, 7).Where(d => _days[d].Checked).ToList(),
                Start = _start.Text.Trim(),
                End = _end.Text.Trim(),
                Note = _note.Text
            };
            int existing = _items.FindIndex(i => string.Equals(i.User, user, StringComparison.OrdinalIgnoreCase));
            if (existing >= 0) _items[existing] = item;
            else _items.Add(item);
            RefreshList();
            _list.SelectedIndex = _items.IndexOf(item);
        }

        private void Remove()
        {
            if (_list.SelectedIndex < 0) return;
            _items.RemoveAt(_list.SelectedIndex);
            RefreshList();
            ShowItem(null);
        }

        private void RefreshList()
        {
            _list.Items.Clear();
            foreach (var i in _items)
                _list.Items.Add(i.User + " — " + (i.Mode == "always" ? "her zaman serbest"
                    : string.Join(",", DayOrder.Where(d => i.Days.Contains(d)).Select(d => DayShort[d])) + " " + i.Start + "-" + i.End)
                    + (string.IsNullOrEmpty(i.Note) ? "" : " (" + i.Note + ")"));
        }

        private static WorkHoursExceptionDto Copy(WorkHoursExceptionDto e)
        {
            return new WorkHoursExceptionDto { User = e.User, Mode = e.Mode, Days = (e.Days ?? new List<int>()).ToList(), Start = e.Start, End = e.End, Note = e.Note };
        }
    }
}
