using System.Drawing;
using System.Text.RegularExpressions;

namespace SecurityGuardUI.Controls
{
    /// <summary>
    /// HTML kod kutusu (imza düzenleme): etiket, özellik adı, değer ve yorumlar ayrı renkte.
    /// Düzenleme, geri alma, bul ve tema davranışı <see cref="CodeEditor"/>'dan gelir.
    /// </summary>
    public class HtmlCodeEditor : CodeEditor
    {
        private static readonly Regex TagOrComment = new Regex(@"<!--.*?-->|<[^>]*>?", RegexOptions.Singleline | RegexOptions.Compiled);
        private static readonly Regex TagName = new Regex(@"^</?\s*([A-Za-z][\w:-]*)", RegexOptions.Compiled);
        private static readonly Regex Attribute = new Regex(@"([A-Za-z_:][\w:.-]*)(\s*=\s*(""[^""]*""?|'[^']*'?|[^\s>]+))?", RegexOptions.Compiled);

        // CultureInvariant şart: Türkçe kültürde "IMG" ile "img" eşleşmez (I → ı)
        private static readonly Regex ImgTag = new Regex(@"<img\b[^>]*>?", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Compiled);

        private Color _tagColor, _attrColor, _valueColor, _punctColor, _commentColor;

        /// <summary>Metindeki kaçıncı &lt;img&gt; etiketiyse onu seçer ve oraya kaydırır (önizlemede resme tıklanınca).</summary>
        public void SelectImageTag(int index)
        {
            var matches = ImgTag.Matches(Text);
            if (index < 0 || index >= matches.Count) return;
            Focus();
            Select(matches[index].Index, matches[index].Length);
            ScrollToCaret();
        }

        protected override void SetPalette(bool dark)
        {
            if (dark)
            {
                // VS Code koyu tema renkleri
                _tagColor = Color.FromArgb(86, 156, 214);
                _attrColor = Color.FromArgb(156, 220, 254);
                _valueColor = Color.FromArgb(206, 145, 120);
                _punctColor = Color.FromArgb(128, 128, 128);
                _commentColor = Color.FromArgb(106, 153, 85);
            }
            else
            {
                // Visual Studio açık tema renkleri
                _tagColor = Color.FromArgb(128, 0, 0);
                _attrColor = Color.FromArgb(220, 0, 0);
                _valueColor = Color.FromArgb(0, 0, 230);
                _punctColor = Color.FromArgb(0, 0, 230);
                _commentColor = Color.FromArgb(0, 128, 0);
            }
        }

        protected override void HighlightText(string text)
        {
            foreach (Match tag in TagOrComment.Matches(text))
            {
                if (tag.Value.StartsWith("<!--", System.StringComparison.Ordinal))
                {
                    Colorize(tag.Index, tag.Length, _commentColor);
                    continue;
                }

                Colorize(tag.Index, tag.Length, _punctColor); // < > / =
                var name = TagName.Match(tag.Value);
                int attrStart = 1;
                if (name.Success)
                {
                    Colorize(tag.Index + name.Groups[1].Index, name.Groups[1].Length, _tagColor);
                    attrStart = name.Index + name.Length;
                }

                int attrEnd = tag.Value.EndsWith(">", System.StringComparison.Ordinal) ? tag.Length - 1 : tag.Length;
                if (attrEnd <= attrStart) continue;
                string inner = tag.Value.Substring(attrStart, attrEnd - attrStart);
                foreach (Match a in Attribute.Matches(inner))
                {
                    Colorize(tag.Index + attrStart + a.Groups[1].Index, a.Groups[1].Length, _attrColor);
                    if (a.Groups[3].Success)
                        Colorize(tag.Index + attrStart + a.Groups[3].Index, a.Groups[3].Length, _valueColor);
                }
            }
        }
    }
}
