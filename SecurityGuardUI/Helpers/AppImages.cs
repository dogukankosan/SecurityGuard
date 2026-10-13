using System.Collections.Generic;
using System.Drawing;
using System.Reflection;
using DevExpress.Utils.Svg;

namespace SecurityGuardUI.Helpers
{
    /// <summary>Program ikonu, logo ve menü ikonları (exe'ye gömülü kaynaklardan bir kez yüklenir).</summary>
    internal static class AppImages
    {
        private static Icon _icon;
        private static Image _logo;
        private static readonly Dictionary<string, SvgImage> _menu = new Dictionary<string, SvgImage>();

        /// <summary>Tüm formların başlık çubuğu ikonu.</summary>
        public static Icon Icon
        {
            get
            {
                if (_icon == null)
                    using (var s = Assembly.GetExecutingAssembly().GetManifestResourceStream("SecurityGuardUI.AppIcon.ico"))
                        _icon = new Icon(s);
                return _icon;
            }
        }

        /// <summary>Giriş / kurulum ekranlarındaki logo (96x96).</summary>
        public static Image Logo
        {
            get
            {
                if (_logo == null)
                    using (var s = Assembly.GetExecutingAssembly().GetManifestResourceStream("SecurityGuardUI.AppLogo.png"))
                        _logo = new Bitmap(Image.FromStream(s)); // akış kapanınca da kullanılabilsin diye kopyalanır
                return _logo;
            }
        }

        /// <summary>
        /// Menü ikonu (Resources\Menu\&lt;ad&gt;.svg; DevExpress Icon Builder setinden, sadece kullanılanlar gömülü).
        /// SVG olduğu için seçili temanın renklerine uyar. Bulunamazsa null (menü ikonsuz açılır).
        /// </summary>
        public static SvgImage Menu(string name)
        {
            SvgImage img;
            if (_menu.TryGetValue(name, out img)) return img;
            using (var s = Assembly.GetExecutingAssembly().GetManifestResourceStream("SecurityGuardUI.Menu." + name + ".svg"))
                img = s == null ? null : SvgImage.FromStream(s);
            _menu[name] = img;
            return img;
        }
    }
}
