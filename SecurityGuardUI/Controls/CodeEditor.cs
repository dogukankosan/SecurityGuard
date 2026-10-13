using System;
using System.Collections.Generic;
using System.Drawing;
using System.Runtime.InteropServices;
using System.Windows.Forms;
using DevExpress.LookAndFeel;
using DevExpress.Skins;
using DevExpress.Utils.Frames;
using DevExpress.XtraEditors;

namespace SecurityGuardUI.Controls
{
    /// <summary>
    /// Renklendirmeli kod kutusu tabanı (HTML imza ve SQLite sorgu ekranı bunu kullanır).
    /// Renklendirme kuralları alt sınıftadır (<see cref="SetPalette"/> ve <see cref="HighlightText"/>).
    /// - Zemin ve yazı rengi seçili temadan alınır, tema değişince güncellenir.
    /// - Yapıştırma her zaman düz metin (web'den kopyalanan biçim taşınmaz).
    /// - Renklendirme yazma durduktan kısa süre sonra yapılır (yazarken takılma olmaz).
    /// - Kısayollar: Ctrl+Z / Ctrl+Y (kendi geçmişi; renklendirme adım sayılmaz), Ctrl+F / F3 / Shift+F3 (bul),
    ///   Ctrl+X / C / V, Ctrl+A.
    /// </summary>
    public abstract class CodeEditor : RichTextBox
    {
        private const int WM_SETREDRAW = 0x000B;
        private const int EM_GETSCROLLPOS = 0x04DD;
        private const int EM_SETSCROLLPOS = 0x04DE;

        /// <summary>Bu süre içindeki ardışık yazma tek adımda geri alınır.</summary>
        private static readonly TimeSpan UndoGroupWindow = TimeSpan.FromMilliseconds(800);
        private const int MaxHistory = 300;

        private readonly Timer _timer = new Timer { Interval = 250 };
        private bool _coloring;

        // Kendi geri al / yinele geçmişi: RichTextBox'ın kendi geçmişi renklendirmeyi de adım saydığı için kullanılmaz
        private readonly List<EditState> _undo = new List<EditState>();
        private readonly List<EditState> _redo = new List<EditState>();
        private EditState _lastState = new EditState("", 0);
        private DateTime _lastEditUtc = DateTime.MinValue;
        private bool _restoring;
        private bool _forceUndoPoint;

        private string _lastSearch = "";

        protected CodeEditor()
        {
            Font = new Font("Consolas", 10F);
            WordWrap = true;
            DetectUrls = false;
            AcceptsTab = true;
            HideSelection = false; // "Bul" sonucu ve önizlemeden seçilen etiket odak başka yerdeyken de görünsün
            BorderStyle = BorderStyle.None;
            ScrollBars = RichTextBoxScrollBars.Vertical;

            _timer.Tick += (s, e) => { _timer.Stop(); Highlight(); };
            UserLookAndFeel.Default.StyleChanged += OnSkinChanged;
            ContextMenuStrip = BuildMenu();
            ApplyTheme();
        }

        // ================================================================ Düzenleme

        /// <summary>Seçili metnin yerine (seçim yoksa imleç konumuna) ekler. Tek adımda geri alınabilir.</summary>
        public void InsertAtCaret(string text)
        {
            _forceUndoPoint = true;
            SelectedText = text;
            Focus();
        }

        public void PastePlainText()
        {
            if (!Clipboard.ContainsText()) return;
            _forceUndoPoint = true;
            SelectedText = Clipboard.GetText();
        }

        private void CutText()
        {
            if (SelectionLength == 0) return;
            _forceUndoPoint = true;
            Cut();
        }

        /// <summary>Metin programdan yüklendiğinde çağrılır: geri alma geçmişi bu noktadan başlar.</summary>
        public void ResetHistory()
        {
            _undo.Clear();
            _redo.Clear();
            _lastState = new EditState(Text, SelectionStart);
            _lastEditUtc = DateTime.MinValue;
        }

        public void UndoEdit()
        {
            if (_undo.Count == 0) return;
            Push(_redo, new EditState(Text, SelectionStart));
            Restore(Pop(_undo));
        }

        public void RedoEdit()
        {
            if (_redo.Count == 0) return;
            Push(_undo, new EditState(Text, SelectionStart));
            Restore(Pop(_redo));
        }

        private void Restore(EditState state)
        {
            _restoring = true;
            try
            {
                Text = state.Text;
                SelectionStart = Math.Min(state.Caret, TextLength);
                SelectionLength = 0;
                ScrollToCaret();
            }
            finally
            {
                _restoring = false;
            }
            _lastState = state;
            _lastEditUtc = DateTime.MinValue; // sonraki yazma yeni bir adım başlatır
        }

        /// <summary>Kullanıcı düzenlemesini geçmişe işler (ardışık yazma gruplanır).</summary>
        private void RecordEdit()
        {
            DateTime now = DateTime.UtcNow;
            if (_forceUndoPoint || now - _lastEditUtc > UndoGroupWindow)
            {
                Push(_undo, _lastState);
                _redo.Clear();
            }
            _forceUndoPoint = false;
            _lastState = new EditState(Text, SelectionStart);
            _lastEditUtc = now;
        }

        private static void Push(List<EditState> list, EditState state)
        {
            list.Add(state);
            if (list.Count > MaxHistory) list.RemoveAt(0);
        }

        private static EditState Pop(List<EditState> list)
        {
            var s = list[list.Count - 1];
            list.RemoveAt(list.Count - 1);
            return s;
        }

        // ================================================================ Bul

        public void ShowFind()
        {
            string initial = SelectionLength > 0 && SelectionLength < 100 && !SelectedText.Contains("\n") ? SelectedText : _lastSearch;
            string text = XtraInputBox.Show(FindForm(), "Aranacak metin (F3: sonraki, Shift+F3: önceki):", "Bul", initial);
            if (string.IsNullOrEmpty(text)) return;
            _lastSearch = text;
            SelectionLength = 0; // aramaya imleçten başla
            FindNext(true);
        }

        /// <summary>Büyük / küçük harf fark etmeden arar; sona gelince baştan devam eder.</summary>
        public void FindNext(bool forward)
        {
            if (_lastSearch.Length == 0)
            {
                ShowFind();
                return;
            }

            string text = Text;
            int index;
            if (forward)
            {
                int from = Math.Min(SelectionStart + SelectionLength, text.Length);
                index = IndexOf(text, from, true);
                if (index < 0) index = IndexOf(text, 0, true);
            }
            else
            {
                int from = SelectionStart - 1;
                index = from >= 0 ? IndexOf(text, from, false) : -1;
                if (index < 0 && text.Length > 0) index = IndexOf(text, text.Length - 1, false);
            }

            if (index < 0)
            {
                XtraMessageBox.Show(FindForm(), "\"" + _lastSearch + "\" bulunamadı.", "Bul", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }
            Focus();
            Select(index, _lastSearch.Length);
            ScrollToCaret();
        }

        /// <summary>
        /// Hem Türkçe hem kültürden bağımsız büyük/küçük harf duyarsız arar, en yakın sonucu döner:
        /// "istanbul" → "İstanbul" (Türkçe kural) ve "img" → "IMG" (HTML etiketi) ikisi de bulunur.
        /// </summary>
        private int IndexOf(string text, int from, bool forward)
        {
            int a, b;
            if (forward)
            {
                a = text.IndexOf(_lastSearch, from, StringComparison.CurrentCultureIgnoreCase);
                b = text.IndexOf(_lastSearch, from, StringComparison.InvariantCultureIgnoreCase);
                if (a < 0) return b;
                if (b < 0) return a;
                return Math.Min(a, b);
            }
            a = text.LastIndexOf(_lastSearch, from, StringComparison.CurrentCultureIgnoreCase);
            b = text.LastIndexOf(_lastSearch, from, StringComparison.InvariantCultureIgnoreCase);
            return Math.Max(a, b);
        }

        // ================================================================ Klavye

        protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
        {
            switch (keyData)
            {
                case Keys.Control | Keys.Z:
                    UndoEdit();
                    return true;
                case Keys.Control | Keys.Y:
                case Keys.Control | Keys.Shift | Keys.Z:
                    RedoEdit();
                    return true;
                case Keys.Control | Keys.F:
                    ShowFind();
                    return true;
                case Keys.F3:
                    FindNext(true);
                    return true;
                case Keys.Shift | Keys.F3:
                    FindNext(false);
                    return true;
                case Keys.Control | Keys.V:
                case Keys.Shift | Keys.Insert:
                    PastePlainText();
                    return true;
                case Keys.Control | Keys.X:
                case Keys.Shift | Keys.Delete:
                    CutText();
                    return true;
            }
            return base.ProcessCmdKey(ref msg, keyData);
        }

        protected override void OnTextChanged(EventArgs e)
        {
            base.OnTextChanged(e);
            if (_coloring) return;
            if (!_restoring) RecordEdit();
            _timer.Stop();
            _timer.Start();
        }

        protected override void OnHandleCreated(EventArgs e)
        {
            base.OnHandleCreated(e);
            Highlight();
        }

        private void OnSkinChanged(object sender, EventArgs e)
        {
            if (!IsDisposed) ApplyTheme();
        }

        private void ApplyTheme()
        {
            var skin = CommonSkins.GetSkin(UserLookAndFeel.Default);
            BackColor = skin.Colors.GetColor(CommonColors.Window);
            ForeColor = skin.Colors.GetColor(CommonColors.WindowText);

            SetPalette(FrameHelper.IsDarkSkin(UserLookAndFeel.Default));
            Highlight();
        }

        /// <summary>Tüm metni yeniden renklendirir; imleç, seçim ve kaydırma konumu korunur.</summary>
        protected void Highlight()
        {
            if (!IsHandleCreated || _coloring) return;
            _coloring = true;

            int selStart = SelectionStart, selLength = SelectionLength;
            var scroll = Point.Empty;
            SendMessage(Handle, EM_GETSCROLLPOS, IntPtr.Zero, ref scroll);
            SendMessage(Handle, WM_SETREDRAW, IntPtr.Zero, IntPtr.Zero);
            try
            {
                SelectAll();
                SelectionColor = ForeColor;

                HighlightText(Text);
            }
            finally
            {
                Select(selStart, selLength);
                SelectionColor = ForeColor; // yeni yazılan karakter varsayılan renkte başlasın
                SendMessage(Handle, EM_SETSCROLLPOS, IntPtr.Zero, ref scroll);
                SendMessage(Handle, WM_SETREDRAW, new IntPtr(1), IntPtr.Zero);
                Invalidate();
                _coloring = false;
            }
        }

        /// <summary>Renk setini seçer (tema değişince çağrılır).</summary>
        protected abstract void SetPalette(bool dark);

        /// <summary>Metni boyar: <see cref="Colorize"/> ile parçaları renklendirir. Başlangıçta tüm metin ForeColor'dadır.</summary>
        protected abstract void HighlightText(string text);

        protected void Colorize(int start, int length, Color color)
        {
            if (length <= 0) return;
            Select(start, length);
            SelectionColor = color;
        }

        private ContextMenuStrip BuildMenu()
        {
            var menu = new ContextMenuStrip();
            var undo = Item("Geri Al", "Ctrl+Z", UndoEdit);
            var redo = Item("Yinele", "Ctrl+Y", RedoEdit);
            var cut = Item("Kes", "Ctrl+X", CutText);
            var copy = Item("Kopyala", "Ctrl+C", Copy);
            var paste = Item("Yapıştır", "Ctrl+V", PastePlainText);
            menu.Items.AddRange(new ToolStripItem[]
            {
                undo, redo, new ToolStripSeparator(),
                cut, copy, paste, new ToolStripSeparator(),
                Item("Tümünü Seç", "Ctrl+A", SelectAll),
                Item("Bul...", "Ctrl+F", ShowFind)
            });
            menu.Opening += (s, e) =>
            {
                undo.Enabled = _undo.Count > 0;
                redo.Enabled = _redo.Count > 0;
                cut.Enabled = SelectionLength > 0;
                copy.Enabled = SelectionLength > 0;
                paste.Enabled = Clipboard.ContainsText();
            };
            return menu;
        }

        private static ToolStripMenuItem Item(string text, string shortcut, Action action)
        {
            return new ToolStripMenuItem(text, null, (s, e) => action()) { ShortcutKeyDisplayString = shortcut };
        }

        private struct EditState
        {
            public readonly string Text;
            public readonly int Caret;

            public EditState(string text, int caret)
            {
                Text = text;
                Caret = caret;
            }
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                UserLookAndFeel.Default.StyleChanged -= OnSkinChanged;
                _timer.Dispose();
                if (ContextMenuStrip != null) ContextMenuStrip.Dispose();
            }
            base.Dispose(disposing);
        }

        [DllImport("user32.dll")]
        private static extern IntPtr SendMessage(IntPtr hWnd, int msg, IntPtr wParam, IntPtr lParam);

        [DllImport("user32.dll")]
        private static extern IntPtr SendMessage(IntPtr hWnd, int msg, IntPtr wParam, ref Point lParam);
    }
}
