using System.Drawing;
using DevExpress.LookAndFeel;
using DevExpress.Utils.Frames;

namespace SecurityGuardUI.Helpers
{
    /// <summary>
    /// Temaya uyan yazı renkleri. Ekranlarda sabit renk (Color.Gray, kırmızı...) kullanılmaz;
    /// DXSkinColors renkleri seçili temadan okunur ve tema değişince kendiliğinden güncellenir.
    /// Designer dosyalarında da doğrudan DXSkinColors.ForeColors.* kullanılır.
    ///
    /// Not: Açıklama yazıları bilerek soluk (DisabledText) yapılmaz; bazı açık temalarda zemine
    /// çok yaklaşıp okunmaz hale geliyor. Açıklamalar temanın normal yazı rengiyle yazılır.
    /// </summary>
    internal static class UiColors
    {
        /// <summary>Normal / açıklama yazısı (temada en okunaklı renk).</summary>
        public static Color Text { get { return DXSkinColors.ForeColors.ControlText; } }

        /// <summary>Hata mesajı.</summary>
        public static Color Error { get { return DXSkinColors.ForeColors.Critical; } }

        /// <summary>Uyarı (ör. kaydedilmemiş değişiklik).</summary>
        public static Color Warning { get { return DXSkinColors.ForeColors.Warning; } }

        /// <summary>Başarı mesajı. Temada hazır yeşil olmadığı için koyu / açık temaya göre seçilir.</summary>
        public static Color Success
        {
            get { return FrameHelper.IsDarkSkin(UserLookAndFeel.Default) ? Color.FromArgb(110, 200, 130) : Color.FromArgb(20, 120, 50); }
        }
    }
}
