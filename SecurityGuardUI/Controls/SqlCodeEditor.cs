using System;
using System.Drawing;
using System.Text.RegularExpressions;
using System.Windows.Forms;

namespace SecurityGuardUI.Controls
{
    /// <summary>
    /// SQL kod kutusu (SQLite sorgu ekranı): anahtar kelime, metin, sayı ve yorumlar ayrı renkte.
    /// F5 veya Ctrl+Enter <see cref="ExecuteRequested"/> olayını tetikler.
    /// </summary>
    public class SqlCodeEditor : CodeEditor
    {
        // Sıra önemli: yorum ve metin önce yakalanır (içlerindeki kelimeler boyanmasın)
        private static readonly Regex Tokens = new Regex(
            @"(?<c>--[^\n]*|/\*.*?(\*/|$))|(?<s>'([^']|'')*'?)|(?<q>""[^""]*""?|\[[^\]]*\]?)|(?<n>\b\d+(\.\d+)?\b)|(?<w>\b[A-Za-z_]+\b)",
            RegexOptions.Singleline | RegexOptions.Compiled);

        private static readonly string[] KeywordList =
        {
            "SELECT", "FROM", "WHERE", "AND", "OR", "NOT", "IN", "IS", "NULL", "LIKE", "GLOB", "BETWEEN", "EXISTS",
            "INSERT", "INTO", "VALUES", "UPDATE", "SET", "DELETE", "REPLACE", "UPSERT", "CONFLICT", "DO", "NOTHING",
            "CREATE", "TABLE", "INDEX", "VIEW", "TRIGGER", "DROP", "ALTER", "ADD", "COLUMN", "RENAME", "TO", "IF",
            "PRIMARY", "KEY", "FOREIGN", "REFERENCES", "UNIQUE", "CHECK", "DEFAULT", "AUTOINCREMENT", "COLLATE",
            "JOIN", "INNER", "LEFT", "RIGHT", "FULL", "OUTER", "CROSS", "ON", "USING", "NATURAL",
            "GROUP", "BY", "ORDER", "ASC", "DESC", "HAVING", "LIMIT", "OFFSET", "DISTINCT", "ALL", "AS",
            "UNION", "EXCEPT", "INTERSECT", "WITH", "RECURSIVE", "CASE", "WHEN", "THEN", "ELSE", "END",
            "BEGIN", "COMMIT", "ROLLBACK", "TRANSACTION", "SAVEPOINT", "RELEASE",
            "PRAGMA", "VACUUM", "ANALYZE", "EXPLAIN", "QUERY", "PLAN", "ATTACH", "DETACH", "DATABASE", "REINDEX",
            "INTEGER", "TEXT", "REAL", "BLOB", "NUMERIC"
        };

        private static readonly System.Collections.Generic.HashSet<string> Keywords =
            new System.Collections.Generic.HashSet<string>(KeywordList, StringComparer.OrdinalIgnoreCase);

        private Color _keywordColor, _stringColor, _numberColor, _commentColor, _identColor;

        public SqlCodeEditor()
        {
            WordWrap = false;
            ScrollBars = RichTextBoxScrollBars.Both;
        }

        /// <summary>F5 veya Ctrl+Enter basıldı.</summary>
        public event EventHandler ExecuteRequested;

        /// <summary>Seçim varsa seçili metin, yoksa tüm metin (SSMS gibi).</summary>
        public string SqlToRun
        {
            get { return SelectionLength > 0 ? SelectedText : Text; }
        }

        protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
        {
            if (keyData == Keys.F5 || keyData == (Keys.Control | Keys.Enter))
            {
                var handler = ExecuteRequested;
                if (handler != null) handler(this, EventArgs.Empty);
                return true;
            }
            return base.ProcessCmdKey(ref msg, keyData);
        }

        protected override void SetPalette(bool dark)
        {
            if (dark)
            {
                _keywordColor = Color.FromArgb(86, 156, 214);
                _stringColor = Color.FromArgb(206, 145, 120);
                _numberColor = Color.FromArgb(181, 206, 168);
                _commentColor = Color.FromArgb(106, 153, 85);
                _identColor = Color.FromArgb(156, 220, 254);
            }
            else
            {
                _keywordColor = Color.FromArgb(0, 0, 255);
                _stringColor = Color.FromArgb(163, 21, 21);
                _numberColor = Color.FromArgb(9, 134, 88);
                _commentColor = Color.FromArgb(0, 128, 0);
                _identColor = Color.FromArgb(0, 16, 128);
            }
        }

        protected override void HighlightText(string text)
        {
            foreach (Match m in Tokens.Matches(text))
            {
                if (m.Groups["c"].Success) Colorize(m.Index, m.Length, _commentColor);
                else if (m.Groups["s"].Success) Colorize(m.Index, m.Length, _stringColor);
                else if (m.Groups["q"].Success) Colorize(m.Index, m.Length, _identColor);
                else if (m.Groups["n"].Success) Colorize(m.Index, m.Length, _numberColor);
                else if (Keywords.Contains(m.Value)) Colorize(m.Index, m.Length, _keywordColor);
            }
        }
    }
}
