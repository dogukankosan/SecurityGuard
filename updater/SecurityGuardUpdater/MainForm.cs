using System;
using System.Drawing;
using System.IO;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Forms;
using DevExpress.XtraEditors;

namespace SecurityGuardUpdater
{
    /// <summary>SecurityGuard güncelleme masaüstü arayüzü. Adres yazmaya gerek yok — servisten otomatik alınır.</summary>
    public sealed class MainForm : XtraForm
    {
        private readonly UpdateEngine _engine = new UpdateEngine();

        private readonly LabelControl _info = new LabelControl();
        private readonly TextEdit _user = new TextEdit();
        private readonly TextEdit _pass = new TextEdit();
        private readonly SimpleButton _check = new SimpleButton { Text = "Giriş & Güncelleme Kontrol" };
        private readonly LabelControl _result = new LabelControl();
        private readonly SimpleButton _apply = new SimpleButton { Text = "Güncelle", Enabled = false };
        private readonly ProgressBarControl _bar = new ProgressBarControl { Visible = false };
        private readonly MarqueeProgressBarControl _marquee = new MarqueeProgressBarControl { Visible = false };
        private readonly LabelControl _status = new LabelControl();
        private readonly MemoEdit _log = new MemoEdit();
        private readonly string _logFile = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "update.log");

        public MainForm()
        {
            Text = "SecurityGuard — Merkezi Güncelleme";
            Icon = SafeIcon();
            StartPosition = FormStartPosition.CenterScreen;
            FormBorderStyle = FormBorderStyle.FixedSingle;  // sabit boyut
            MaximizeBox = false;                            // büyütme yok
            ClientSize = new Size(560, 480);
            Build();
            _engine.Log = s => BeginInvoke((Action)(() => AppendLog(s)));
            _engine.Percent = p => BeginInvoke((Action)(() => SetPercent(p)));
            Load += (s, e) => InitEngine();
        }

        private void Build()
        {
            int x = 18, w = ClientSize.Width - 36;

            var title = new LabelControl { Text = "SecurityGuard Güncelleme", Location = new Point(x, 16), AutoSizeMode = LabelAutoSizeMode.None, Size = new Size(w, 26) };
            title.Appearance.Font = new Font("Segoe UI", 15F, FontStyle.Bold);
            title.Appearance.Options.UseFont = true;
            Controls.Add(title);

            _info.Location = new Point(x, 48);
            _info.AutoSizeMode = LabelAutoSizeMode.None;
            _info.Size = new Size(w, 20);
            _info.Appearance.ForeColor = Color.Gray;
            _info.Appearance.Options.UseForeColor = true;
            Controls.Add(_info);

            var group = new GroupControl { Text = "Yönetici girişi", Location = new Point(x, 78), Size = new Size(w, 92) };
            var lu = new LabelControl { Text = "Kullanıcı adı", Location = new Point(14, 30) };
            _user.Location = new Point(110, 27); _user.Width = 200;
            var lp = new LabelControl { Text = "Şifre", Location = new Point(14, 58) };
            _pass.Location = new Point(110, 55); _pass.Width = 200; _pass.Properties.UseSystemPasswordChar = true;
            _check.Location = new Point(325, 40); _check.Size = new Size(190, 30);
            _check.Cursor = Cursors.Hand;
            _check.Click += async (s, e) => await CheckAsync();
            _pass.KeyDown += async (s, e) => { if (e.KeyCode == Keys.Enter) await CheckAsync(); };
            group.Controls.AddRange(new Control[] { lu, _user, lp, _pass, _check });
            Controls.Add(group);

            _result.Location = new Point(x, 182);
            _result.AutoSizeMode = LabelAutoSizeMode.None;
            _result.Size = new Size(w - 130, 44);
            _result.Appearance.Font = new Font("Segoe UI", 10F);
            _result.Appearance.Options.UseFont = true;
            _result.Appearance.TextOptions.WordWrap = DevExpress.Utils.WordWrap.Wrap;
            _result.Appearance.Options.UseTextOptions = true;
            Controls.Add(_result);

            _apply.Location = new Point(ClientSize.Width - 18 - 120, 182);
            _apply.Size = new Size(120, 40);
            _apply.Appearance.Font = new Font("Segoe UI", 10F, FontStyle.Bold);
            _apply.Appearance.Options.UseFont = true;
            _apply.Cursor = Cursors.Hand;
            _apply.Click += async (s, e) => await ApplyAsync();
            Controls.Add(_apply);

            _bar.Location = new Point(x, 236); _bar.Size = new Size(w, 22);
            _bar.Properties.ShowTitle = true;
            _marquee.Location = _bar.Location; _marquee.Size = _bar.Size;
            Controls.Add(_bar); Controls.Add(_marquee);

            _status.Location = new Point(x, 264);
            _status.AutoSizeMode = LabelAutoSizeMode.None; _status.Size = new Size(w, 18);
            _status.Appearance.ForeColor = Color.Gray; _status.Appearance.Options.UseForeColor = true;
            Controls.Add(_status);

            _log.Location = new Point(x, 288); _log.Size = new Size(w, ClientSize.Height - 288 - 16);
            _log.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right | AnchorStyles.Bottom;
            _log.Properties.ReadOnly = true;
            _log.Properties.ScrollBars = ScrollBars.Vertical;
            _log.Properties.Appearance.Font = new Font("Consolas", 8.5F);
            _log.Properties.Appearance.Options.UseFont = true;
            Controls.Add(_log);

            _apply.Anchor = AnchorStyles.Top | AnchorStyles.Right;
        }

        private void InitEngine()
        {
            try
            {
                _engine.Init();
                _info.Text = "Mevcut sürüm: " + (_engine.CurrentVersion ?? "?") + "   ·   Makine: " + _engine.MachineName;
                _user.Focus();
            }
            catch (Exception ex)
            {
                _info.Text = "";
                LogException(ex);
                EnableLogin(false);
            }
        }

        private async Task CheckAsync()
        {
            string u = _user.Text.Trim(), p = _pass.Text;
            if (u.Length == 0 || p.Length == 0) { Result("Kullanıcı adı ve şifre girin.", Color.FromArgb(0xE0, 0x8A, 0x00)); return; }
            Busy(true, "Sunucuya bağlanılıyor...");
            try
            {
                await Task.Run(() => _engine.LoginAndCheck(u, p));
                if (_engine.Pending == null)
                {
                    Result("Zaten güncelsiniz (" + (_engine.CurrentVersion ?? "?") + ").", Color.FromArgb(0x1A, 0xA4, 0x5A));
                    _apply.Enabled = false;
                }
                else
                {
                    var m = _engine.Pending;
                    Result("Yeni sürüm: " + _engine.CurrentVersion + "  →  " + m.Version
                           + (m.Mandatory ? "  (ZORUNLU)" : "")
                           + (string.IsNullOrWhiteSpace(m.Notes) ? "" : "\r\n" + m.Notes), Color.FromArgb(0x25, 0x63, 0xEB));
                    _apply.Enabled = true;
                    AppendLog("Yeni sürüm bulundu: " + m.Version + " (" + Math.Max(1, m.SizeBytes / 1024 / 1024) + " MB). İndirme ve doğrulama 'Güncelle' ile başlar.");
                }
            }
            catch (Exception ex) { LogException(ex); }
            finally { Busy(false, ""); }
        }

        private async Task ApplyAsync()
        {
            if (_engine.Pending == null) return;
            if (XtraMessageBox.Show(this, "Güncelleme uygulanacak. Servis kısa süre duracak. Devam edilsin mi?",
                    "SecurityGuard Güncelleme", MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes) return;

            Busy(true, "Güncelleme uygulanıyor...");
            _apply.Enabled = false; _check.Enabled = false;
            try
            {
                await Task.Run(() => _engine.Apply());
                SetPercent(100);
                Result("Güncelleme tamamlandı. Yeni sürüm: " + _engine.CurrentVersion + ". Servis çalışıyor.", Color.FromArgb(0x1A, 0xA4, 0x5A));
                XtraMessageBox.Show(this, "Güncelleme başarıyla tamamlandı.\r\nYeni sürüm: " + _engine.CurrentVersion, "SecurityGuard", MessageBoxButtons.OK, MessageBoxIcon.Information);
            }
            catch (Exception ex)
            {
                LogException(ex);
                XtraMessageBox.Show(this, "Güncelleme başarısız:\r\n" + ex.Message, "SecurityGuard", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
            finally { Busy(false, ""); _check.Enabled = true; }
        }

        // ---- UI yardımcıları ----

        private void Busy(bool busy, string status)
        {
            _check.Enabled = !busy;
            _status.Text = status;
            Cursor = busy ? Cursors.WaitCursor : Cursors.Default;
            if (busy) SetPercent(-1);   // işlem başladı → belirsiz çubuk döner
            else StopProgress();        // işlem bitti → çubuk durur
        }

        /// <summary>İşlem bitince ilerleme göstergesini durdurur (marquee gizlenir; başarı için %100 çubuğu kalır).</summary>
        private void StopProgress()
        {
            _marquee.Visible = false;
            bool completed = false;
            try { completed = _bar.Visible && Convert.ToInt32(_bar.EditValue ?? 0) >= 100; } catch { }
            if (!completed) _bar.Visible = false;
        }

        private void SetPercent(int p)
        {
            if (p < 0) { _bar.Visible = false; _marquee.Visible = true; }
            else
            {
                _marquee.Visible = false; _bar.Visible = true;
                _bar.Properties.Minimum = 0; _bar.Properties.Maximum = 100;
                _bar.EditValue = Math.Min(100, Math.Max(0, p));
            }
        }

        private void AppendLog(string s)
        {
            string line = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss") + "  " + s;
            _log.Text += (_log.Text.Length > 0 ? "\r\n" : "") + line;
            _log.SelectionStart = _log.Text.Length;
            _log.ScrollToCaret();
            try { File.AppendAllText(_logFile, line + "\r\n", Encoding.UTF8); } catch { }
        }

        private void LogException(Exception ex)
        {
            Fail(ex.Message);
            try { File.AppendAllText(_logFile, "  >> " + ex + "\r\n", Encoding.UTF8); } catch { }
        }

        private void Result(string text, Color color)
        {
            _result.Text = text;
            _result.Appearance.ForeColor = color;
            _result.Appearance.Options.UseForeColor = true;
        }

        private void Fail(string msg)
        {
            Result("HATA: " + msg, Color.FromArgb(0xDC, 0x35, 0x45));
            AppendLog("HATA: " + msg);
        }

        private void EnableLogin(bool enabled)
        {
            _user.Enabled = _pass.Enabled = _check.Enabled = enabled;
        }

        private static System.Drawing.Icon SafeIcon()
        {
            try { return System.Drawing.Icon.ExtractAssociatedIcon(System.Reflection.Assembly.GetExecutingAssembly().Location); }
            catch { return null; }
        }
    }
}
