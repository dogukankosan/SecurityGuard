using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Forms;
using DevExpress.XtraEditors;
using DevExpress.XtraEditors.Controls;
using DevExpress.XtraGrid.Views.Base;
using DevExpress.XtraGrid.Views.Grid;
using SecurityGuard.Shared;
using SecurityGuard.Shared.Contracts;
using SecurityGuard.Shared.Dto;
using SecurityGuardUI.Forms;
using SecurityGuardUI.Helpers;

namespace SecurityGuardUI.Modules
{
    /// <summary>
    /// SQL Yedekleme: seçilen DB'ler seçilen gün / saatte tam yedeklenir, doğrulanır, WinRAR ile şifreli arşivlenir.
    /// Varsayılan kapalı. Arşiv şifresi yönetici şifresi sorularak belirlenir / gösterilir. Altta son yedeklemeler ve adımları.
    /// </summary>
    public partial class BackupModule : ModuleBase
    {
        private readonly Timer _poll = new Timer { Interval = 3000 };
        private readonly CheckEdit[] _days;
        private BackupStateDto _state;
        private bool _polling;
        private bool _wasRunning;

        // Yedek bildirimleri: Olay Kuralları'ndaki iki kural (başarılı / başarısız yedek) bu sayfadan da ayarlanır
        private const string RuleDone = "Sql.BackupDone";
        private const string RuleFailed = "Guard.BackupFailed";
        private readonly NotifyRow _notifyDone;
        private readonly NotifyRow _notifyFailed;
        private List<EventRuleDto> _rules;
        private List<RecipientDto> _recipients;

        public BackupModule()
        {
            InitializeComponent();
            _days = new[] { checkMon, checkTue, checkWed, checkThu, checkFri, checkSat, checkSun };

            comboCopyOnly.Properties.Items.Add(new ImageComboBoxItem("Otomatik (başka yedek aracı varsa COPY_ONLY)", (object)"auto", -1));
            comboCopyOnly.Properties.Items.Add(new ImageComboBoxItem("Her zaman COPY_ONLY", (object)"always", -1));
            comboCopyOnly.Properties.Items.Add(new ImageComboBoxItem("Hiçbir zaman", (object)"never", -1));

            GridHelper.SetupReadOnly(viewDbs);
            viewDbs.OptionsView.ShowAutoFilterRow = false;
            viewDbs.OptionsBehavior.Editable = true;
            viewDbs.OptionsBehavior.ReadOnly = false;
            foreach (DevExpress.XtraGrid.Columns.GridColumn c in viewDbs.Columns) c.OptionsColumn.AllowEdit = c == colDbSelected;
            GridHelper.SetupReadOnly(viewRuns);
            viewRuns.OptionsView.ShowAutoFilterRow = false;
            GridHelper.SetupReadOnly(viewFiles);
            viewFiles.OptionsView.ShowAutoFilterRow = false;

            _poll.Tick += async (s, e) => await PollAsync();

            panelTop.Height += 62;
            var title = new LabelControl { Text = "Bildirimler", Location = new System.Drawing.Point(12, 308) };
            title.Appearance.Font = new System.Drawing.Font(title.Appearance.Font, System.Drawing.FontStyle.Bold);
            title.Appearance.Options.UseFont = true;
            panelTop.Controls.Add(title);
            _notifyDone = new NotifyRow(panelTop, 330, "Başarılı yedek maili");
            _notifyFailed = new NotifyRow(panelTop, 330 + 28, "Başarısız yedek maili");
            panelTop.Height += 26;
            _notifyDone.Changed += async (s, e) => await SaveRuleAsync(RuleDone, _notifyDone);
            _notifyFailed.Changed += async (s, e) => await SaveRuleAsync(RuleFailed, _notifyFailed);
            _notifyDone.Pick += (s, e) => PickRecipients(RuleDone, _notifyDone);
            _notifyFailed.Pick += (s, e) => PickRecipients(RuleFailed, _notifyFailed);
        }

        protected override Task OnFirstShowAsync()
        {
            return LoadAsync();
        }

        private async Task LoadAsync()
        {
            var state = await CallAsync(c => c.SendAsync<BackupStateDto>(Commands.BackupGet, null, TimeSpan.FromMinutes(1)));
            if (state != null) Bind(state, true);
            await LoadNotificationsAsync();
        }

        // ================================================================ Bildirimler

        private async Task LoadNotificationsAsync()
        {
            var recipients = await Client.SendAsync<List<RecipientDto>>(Commands.MailRecipientsList);
            var rules = await Client.SendAsync<List<EventRuleDto>>(Commands.RulesList);
            if (IsDisposed || !recipients.Success || !rules.Success) return;
            _recipients = recipients.Value;
            _rules = rules.Value;
            BindNotifications();
        }

        private void BindNotifications()
        {
            _notifyDone.Bind(_rules.FirstOrDefault(r => r.EventType == RuleDone), _recipients);
            _notifyFailed.Bind(_rules.FirstOrDefault(r => r.EventType == RuleFailed), _recipients);
        }

        private void PickRecipients(string type, NotifyRow row)
        {
            var rule = _rules == null ? null : _rules.FirstOrDefault(r => r.EventType == type);
            if (rule == null) return;
            if (_recipients.Count == 0)
            {
                Ui.Warning(FindForm(), "Önce Mail sayfasından alıcı ekleyin.");
                return;
            }
            using (var form = new RecipientPickerForm(_recipients, rule.RecipientIds ?? new List<long>(), 1))
            {
                if (form.ShowDialog(FindForm()) != DialogResult.OK) return;
                rule.RecipientIds = new List<long>(form.SelectedIds);
            }
            row.SetMail(true); // alıcı seçen mail ister
            SaveRuleAsync(type, row).ContinueWith(t => { }, TaskScheduler.FromCurrentSynchronizationContext());
        }

        private async Task SaveRuleAsync(string type, NotifyRow row)
        {
            var rule = _rules == null ? null : _rules.FirstOrDefault(r => r.EventType == type);
            if (rule == null) return;
            if (rule.SendMail && !row.MailChecked && type == RuleFailed
                && !Ui.Confirm(FindForm(), "Başarısız yedekte mail KAPATILIRSA yedek alınamadığında kimsenin haberi olmaz. Devam edilsin mi?"))
            {
                BindNotifications();
                return;
            }
            rule.SendMail = row.MailChecked;
            var saved = await CallAsync(c => c.SendAsync<List<EventRuleDto>>(Commands.RulesSave, new RuleSaveRequest { Rules = new List<EventRuleDto> { rule } }));
            if (saved != null) _rules = saved;
            await LoadNotificationsAsync();
        }

        /// <summary>Bir bildirim satırı: [ ] mail gönder · alıcılar · [Alıcıları seç...]</summary>
        private sealed class NotifyRow
        {
            private readonly CheckEdit _mail;
            private readonly LabelControl _who = new LabelControl { AutoSizeMode = LabelAutoSizeMode.None, AutoEllipsis = true };
            private readonly SimpleButton _pick = new SimpleButton { Text = "Alıcıları seç...", Size = new System.Drawing.Size(120, 24) };
            private bool _binding;

            public NotifyRow(Control parent, int y, string text)
            {
                _mail = new CheckEdit { Location = new System.Drawing.Point(12, y), Size = new System.Drawing.Size(190, 20) };
                _mail.Properties.Caption = text;
                _who.Location = new System.Drawing.Point(210, y + 3);
                _who.Size = new System.Drawing.Size(520, 16);
                _who.Appearance.Options.UseForeColor = true;
                _pick.Location = new System.Drawing.Point(740, y - 2);
                _mail.CheckedChanged += (s, e) => { if (!_binding) { var h = Changed; if (h != null) h(this, EventArgs.Empty); } };
                _pick.Click += (s, e) => { var h = Pick; if (h != null) h(this, EventArgs.Empty); };
                parent.Controls.Add(_mail);
                parent.Controls.Add(_who);
                parent.Controls.Add(_pick);
            }

            public event EventHandler Changed;
            public event EventHandler Pick;

            public bool MailChecked { get { return _mail.Checked; } }

            public void SetMail(bool on)
            {
                _binding = true;
                _mail.Checked = on;
                _binding = false;
            }

            public void Bind(EventRuleDto rule, List<RecipientDto> recipients)
            {
                _binding = true;
                _mail.Enabled = _pick.Enabled = rule != null;
                _mail.Checked = rule != null && rule.SendMail;
                _binding = false;
                if (rule == null)
                {
                    _who.Text = "(kural bulunamadı: servis eski sürüm olabilir)";
                    return;
                }
                var ids = rule.RecipientIds ?? new List<long>();
                var chosen = recipients.Where(r => ids.Contains(r.Id)).ToList();
                string names = chosen.Count == 0 ? "tüm aktif alıcılara"
                    : string.Join(", ", chosen.Select(r => (string.IsNullOrEmpty(r.DisplayName) ? r.Email : r.DisplayName) + (r.IsEnabled ? "" : " (pasif)")));
                if (chosen.Count > 0 && chosen.All(r => !r.IsEnabled)) names += " → hepsi pasif olduğu için tüm aktif alıcılara gider";
                _who.Text = !rule.SendMail ? "Mail gönderilmiyor (sadece Olay Logu'na yazılır)" : "Gönderilir: " + names;
                _who.Appearance.ForeColor = !rule.SendMail && rule.EventType == RuleFailed ? UiColors.Warning : UiColors.Text;
            }
        }

        private void Bind(BackupStateDto state, bool config)
        {
            string focusedRun = FocusedRun() == null ? null : FocusedRun().RunId;
            bool newRun = _state != null && state.Runs.Count > 0 && !_state.Runs.Any(r => r.RunId == state.Runs[0].RunId);
            _state = state;
            if (config) BindConfig(state.Config);

            gridDbs.DataSource = state.Databases.Select(d => new DbRow
            {
                Selected = d.Selected,
                Instance = d.Instance,
                Name = d.Name,
                Recovery = d.RecoveryModel,
                Size = d.RecoveryModel == "bulunamadı" ? "" : MbText(d.DataMb),
                LastAny = UtcTime.ToLocal(d.LastFullBackupUtc),
                LastGuard = UtcTime.ToLocal(d.LastGuardBackupUtc)
            }).ToList();

            labelRar.Text = state.RarFound == null ? state.RarNote : "WinRAR: " + state.RarFound + (state.RarNote != null ? "  (" + state.RarNote + ")" : "");
            labelRar.Appearance.ForeColor = state.RarFound == null ? UiColors.Error : state.RarNote != null ? UiColors.Warning : UiColors.Text;
            labelPassword.Text = state.HasPassword ? "Arşiv şifresi belirlenmiş." : "Arşiv şifresi henüz belirlenmedi.";
            labelPassword.Appearance.ForeColor = state.HasPassword ? UiColors.Text : UiColors.Warning;
            buttonShowPassword.Enabled = state.HasPassword;
            labelNotes.Text = state.TargetInfo + Environment.NewLine + string.Join(Environment.NewLine, state.Notes);
            labelNotes.Appearance.ForeColor = state.Notes.Any(n => n.Contains("DİKKAT")) || (state.TargetInfo ?? "").Contains("UYARI") ? UiColors.Warning : UiColors.Text;

            gridRuns.DataSource = state.Runs.Select(r => new RunRow(r)).ToList();
            int index = newRun || focusedRun == null ? 0 : Math.Max(0, state.Runs.FindIndex(r => r.RunId == focusedRun));
            if (state.Runs.Count > 0) viewRuns.FocusedRowHandle = viewRuns.GetRowHandle(index);

            if (state.IsRunning) _poll.Start();
            else _poll.Stop();
            buttonRunNow.Enabled = !state.IsRunning;

            if (state.IsRunning)
            {
                labelStatus.Text = "Yedekleme sürüyor: " + (state.Progress ?? "...");
                labelStatus.Appearance.ForeColor = UiColors.Warning;
            }
            else
            {
                labelStatus.Text = (state.Config.Enabled ? "Yedekleme açık" + (state.NextRunUtc != null ? ", sonraki: " + Local(state.NextRunUtc) : "") : "Yedekleme kapalı")
                                   + ".   Geri yükleme: arşivi WinRAR ile açın (arşiv şifresi gerekir), çıkan .bak dosyasını SSMS → Restore Database ile geri yükleyin.";
                labelStatus.Appearance.ForeColor = UiColors.Text;
            }

            var focused = FocusedRun();
            if (focused != null && (focused.Status == "Sürüyor" || _wasRunning || newRun)) ReloadFiles();
            _wasRunning = state.IsRunning;
        }

        private void BindConfig(BackupConfigDto c)
        {
            checkEnabled.Checked = c.Enabled;
            for (int i = 0; i < _days.Length; i++) _days[i].Checked = c.Days.Contains((i + 1) % 7);
            TimeSpan start;
            timeStart.EditValue = TimeSpan.TryParse(c.StartTime, out start) ? DateTime.Today + start : DateTime.Today.AddHours(22);
            textFolder.Text = c.TargetFolder;
            spinRetention.Value = c.RetentionDays;
            comboCopyOnly.EditValue = c.CopyOnlyMode ?? "auto";
            textRar.Text = c.RarPath;
        }

        private BackupConfigDto ReadConfig()
        {
            viewDbs.CloseEditor();
            viewDbs.UpdateCurrentRow();
            var rows = gridDbs.DataSource as List<DbRow> ?? new List<DbRow>();
            var time = timeStart.EditValue is DateTime ? (DateTime)timeStart.EditValue : DateTime.Today.AddHours(22);
            return new BackupConfigDto
            {
                Enabled = checkEnabled.Checked,
                Days = _days.Select((d, i) => new { d, day = (i + 1) % 7 }).Where(x => x.d.Checked).Select(x => x.day).OrderBy(d => d).ToList(),
                StartTime = time.ToString("HH:mm"),
                TargetFolder = textFolder.Text.Trim(),
                RetentionDays = (int)spinRetention.Value,
                CopyOnlyMode = comboCopyOnly.EditValue as string ?? "auto",
                RarPath = string.IsNullOrWhiteSpace(textRar.Text) ? null : textRar.Text.Trim(),
                Databases = rows.Where(r => r.Selected).Select(r => r.Instance + "|" + r.Name).ToList()
            };
        }

        private static bool Same(BackupConfigDto a, BackupConfigDto b)
        {
            return a.Enabled == b.Enabled && a.StartTime == b.StartTime && a.RetentionDays == b.RetentionDays && a.CopyOnlyMode == b.CopyOnlyMode
                   && (a.TargetFolder ?? "") == (b.TargetFolder ?? "") && (a.RarPath ?? "") == (b.RarPath ?? "")
                   && new HashSet<int>(a.Days).SetEquals(b.Days)
                   && new HashSet<string>(a.Databases, StringComparer.OrdinalIgnoreCase).SetEquals(b.Databases);
        }

        // ================================================================ Butonlar

        private async void buttonSave_Click(object sender, EventArgs e)
        {
            var state = await CallAsync(c => c.SendAsync<BackupStateDto>(Commands.BackupSave, ReadConfig(), TimeSpan.FromMinutes(1)));
            if (state == null) return;
            Bind(state, true);
            Ui.Info(FindForm(), "Yedekleme ayarları kaydedildi.");
        }

        private async void buttonRunNow_Click(object sender, EventArgs e)
        {
            if (_state == null) return;
            var cfg = ReadConfig();
            if (!Same(cfg, _state.Config))
            {
                if (!Ui.Confirm(FindForm(), "Ekrandaki değişiklikler kaydedilmemiş. Kaydedip öyle yedeklensin mi?")) return;
                var saved = await CallAsync(c => c.SendAsync<BackupStateDto>(Commands.BackupSave, cfg, TimeSpan.FromMinutes(1)));
                if (saved == null) return;
                Bind(saved, true);
            }
            if (!Ui.Confirm(FindForm(), "Seçili " + _state.Config.Databases.Count + " veritabanı şimdi yedeklensin mi?" + Environment.NewLine
                                        + "Yedek sırasında disk ve SQL Server yoğun çalışır; büyük DB'lerde kullanıcılar yavaşlık hissedebilir."))
                return;
            var state = await CallAsync(c => c.SendAsync<BackupStateDto>(Commands.BackupRun, null, TimeSpan.FromMinutes(1)));
            if (state != null) Bind(state, false);
        }

        private async void buttonRefresh_Click(object sender, EventArgs e)
        {
            await LoadAsync();
        }

        private void textFolder_ButtonClick(object sender, ButtonPressedEventArgs e)
        {
            using (var dialog = new FolderBrowserDialog { Description = "SQL yedek arşivlerinin yazılacağı klasör (başka bir disk önerilir)", ShowNewFolderButton = true })
            {
                if (!string.IsNullOrEmpty(textFolder.Text)) dialog.SelectedPath = textFolder.Text;
                if (dialog.ShowDialog(FindForm()) == DialogResult.OK) textFolder.Text = dialog.SelectedPath;
            }
        }

        private async void buttonSetPassword_Click(object sender, EventArgs e)
        {
            string text = (_state != null && _state.HasPassword
                    ? "DİKKAT: Şifre değişirse ESKİ arşivler yine ESKİ şifreyle açılır; eski şifreyi kaybetmeyin.\r\n\r\n" : "")
                + "Yeni arşiv şifresi (en az 12 karakter). Boş bırakırsanız güçlü bir şifre üretilir:";
            string password = XtraInputBox.Show(FindForm(), text, "Arşiv şifresi", "");
            if (password == null) return;
            string admin = AskAdmin("Yedek arşiv şifresini belirle");
            if (admin == null) return;
            var result = await CallAsync(c => c.SendAsync<string>(Commands.BackupSetPassword, new BackupPasswordRequest { NewPassword = password, AdminPassword = admin }));
            if (result == null) return;
            ShowPassword(result, "Arşiv şifresi kaydedildi.");
            await LoadAsync();
        }

        private async void buttonShowPassword_Click(object sender, EventArgs e)
        {
            string admin = AskAdmin("Yedek arşiv şifresini göster");
            if (admin == null) return;
            var result = await CallAsync(c => c.SendAsync<string>(Commands.BackupGetPassword, new PasswordConfirmRequest { Password = admin }));
            if (result != null) ShowPassword(result, "Arşiv şifresi:");
        }

        private string AskAdmin(string action)
        {
            using (var form = new PasswordConfirmForm(action, "Bu işlem İşlem Kaydı'na yazılır."))
                return form.ShowDialog(FindForm()) == DialogResult.OK ? form.Password : null;
        }

        private void ShowPassword(string password, string title)
        {
            if (Ui.Confirm(FindForm(), title + Environment.NewLine + Environment.NewLine + password + Environment.NewLine + Environment.NewLine
                                       + "Bu şifre olmadan yedekler AÇILAMAZ. Sunucu dışında güvenli bir yere (ör. şifre yöneticisi, kasadaki zarf) not edin." + Environment.NewLine
                                       + Environment.NewLine + "Şifre panoya kopyalansın mı?"))
            {
                try
                {
                    Clipboard.SetText(password);
                }
                catch (System.Runtime.InteropServices.ExternalException)
                {
                    Ui.Warning(FindForm(), "Pano kullanılıyor, kopyalanamadı.");
                }
            }
        }

        private async Task PollAsync()
        {
            if (_polling || IsDisposed) return;
            _polling = true;
            try
            {
                var result = await Client.SendAsync<BackupStateDto>(Commands.BackupGet, null, TimeSpan.FromMinutes(1));
                if (IsDisposed) return;
                if (result.IsUnauthorized)
                {
                    _poll.Stop();
                    Host.HandleSessionExpired();
                    return;
                }
                if (result.Success) Bind(result.Value, false);
            }
            finally
            {
                _polling = false;
            }
        }

        // ================================================================ Çalıştırmalar

        private RunRow FocusedRun()
        {
            return viewRuns.GetFocusedRow() as RunRow;
        }

        private async void viewRuns_FocusedRowChanged(object sender, FocusedRowChangedEventArgs e)
        {
            await LoadFilesAsync();
        }

        private async void ReloadFiles()
        {
            await LoadFilesAsync();
        }

        private async Task LoadFilesAsync()
        {
            var run = FocusedRun();
            if (run == null)
            {
                gridFiles.DataSource = null;
                return;
            }
            var result = await Client.SendAsync<List<BackupFileDto>>(Commands.BackupFiles, new RunIdRequest { RunId = run.RunId });
            if (IsDisposed || FocusedRun() != run) return;
            if (result.IsUnauthorized)
            {
                Host.HandleSessionExpired();
                return;
            }
            if (!result.Success) return;
            gridFiles.DataSource = result.Value.Select(f => new FileRow
            {
                Time = UtcTime.ToLocal(f.TimeUtc),
                Database = f.Database,
                Step = f.Step,
                Result = f.Result,
                Size = f.SizeMb > 0 ? MbText(f.SizeMb) : "",
                Duration = f.DurationMs <= 0 ? "" : f.DurationMs < 1000 ? f.DurationMs + " ms" : (f.DurationMs / 1000.0).ToString("0.#") + " sn",
                File = f.ArchivePath,
                Note = f.Note
            }).ToList();
        }

        private void viewRuns_RowCellStyle(object sender, RowCellStyleEventArgs e)
        {
            var r = viewRuns.GetRow(e.RowHandle) as RunRow;
            if (r == null || e.Column != colRunStatus) return;
            e.Appearance.ForeColor = r.Status == "Tamamlandı" ? UiColors.Success : r.Status == "Sürüyor" ? UiColors.Warning : UiColors.Error;
        }

        private void viewFiles_RowCellStyle(object sender, RowCellStyleEventArgs e)
        {
            var r = viewFiles.GetRow(e.RowHandle) as FileRow;
            if (r == null || e.Column != colFileResult) return;
            e.Appearance.ForeColor = r.Result == "Tamam" ? UiColors.Success : r.Result == "Hata" ? UiColors.Error : UiColors.Warning;
        }

        private void viewDbs_RowCellStyle(object sender, RowCellStyleEventArgs e)
        {
            var r = viewDbs.GetRow(e.RowHandle) as DbRow;
            if (r == null || e.Column != colDbLastAny) return;
            if (r.LastAny == null || r.LastAny < DateTime.Now.AddDays(-7)) e.Appearance.ForeColor = UiColors.Error;
        }

        private static string Local(string utc)
        {
            var t = UtcTime.ToLocal(utc);
            return t.HasValue ? t.Value.ToString("dd.MM.yyyy HH:mm") : "-";
        }

        private static string MbText(double mb)
        {
            return mb >= 1024 ? (mb / 1024).ToString("0.#") + " GB" : mb.ToString("0") + " MB";
        }

        protected override void OnHandleDestroyed(EventArgs e)
        {
            _poll.Stop();
            base.OnHandleDestroyed(e);
        }

        // ================================================================ Satırlar

        public sealed class DbRow
        {
            public bool Selected { get; set; }
            public string Instance { get; set; }
            public string Name { get; set; }
            public string Recovery { get; set; }
            public string Size { get; set; }
            public DateTime? LastAny { get; set; }
            public DateTime? LastGuard { get; set; }
        }

        public sealed class RunRow
        {
            internal RunRow(BackupRunDto r)
            {
                RunId = r.RunId;
                Time = UtcTime.ToLocal(r.StartedUtc);
                Instance = r.Instance;
                Status = r.Status;
                Summary = r.Summary;
                Trigger = r.Trigger;
                var a = UtcTime.ToLocal(r.StartedUtc);
                var b = UtcTime.ToLocal(r.FinishedUtc);
                Duration = a.HasValue && b.HasValue ? Math.Max(0, (int)(b.Value - a.Value).TotalMinutes) + " dk" : "";
            }

            internal string RunId { get; private set; }
            public DateTime? Time { get; private set; }
            public string Instance { get; private set; }
            public string Status { get; private set; }
            public string Duration { get; private set; }
            public string Summary { get; private set; }
            public string Trigger { get; private set; }
        }

        public sealed class FileRow
        {
            public DateTime? Time { get; set; }
            public string Database { get; set; }
            public string Step { get; set; }
            public string Result { get; set; }
            public string Size { get; set; }
            public string Duration { get; set; }
            public string File { get; set; }
            public string Note { get; set; }
        }
    }
}
