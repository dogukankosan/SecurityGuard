using System;
using System.Windows.Forms;
using DevExpress.LookAndFeel;
using DevExpress.Skins;
using DevExpress.UserSkins;
using SecurityGuard.Shared.Contracts;
using SecurityGuard.Shared.Dto;
using SecurityGuardUI.Ipc;

namespace SecurityGuardUI.Helpers
{
    /// <summary>
    /// UI teması. Varsayılan dark tema; seçim servis üzerinden DB'ye kaydedilir ve bir sonraki açılışta
    /// (giriş ekranından önce) aynen uygulanır. Hangi ekrandan değiştirilirse değiştirilsin
    /// (başlık çubuğundaki tema / palet seçici dahil) değişiklik yakalanıp kaydedilir.
    /// </summary>
    internal static class ThemeManager
    {
        public const string DefaultSkin = "Office 2019 Black";

        private static GuardClient _client;
        private static Timer _saveTimer;
        private static bool _applying;

        /// <summary>Program başında bir kez: ek temaları yükler, varsayılan temayı uygular.</summary>
        public static void Initialize()
        {
            BonusSkins.Register();
            SkinManager.EnableFormSkins();
            Apply(null);
        }

        /// <summary>Kayıtlı temayı uygular. Tema bulunamazsa (sürüm farkı vb.) varsayılan tema.</summary>
        public static void Apply(ThemeDto theme)
        {
            _applying = true;
            try
            {
                string skin = theme != null && SkinManager.Default.Skins[theme.SkinName] != null ? theme.SkinName : DefaultSkin;
                string palette = theme != null && skin == theme.SkinName ? theme.PaletteName : null;

                if (string.IsNullOrEmpty(palette))
                    UserLookAndFeel.Default.SetSkinStyle(skin);
                else
                    UserLookAndFeel.Default.SetSkinStyle(skin, palette);
            }
            catch
            {
                UserLookAndFeel.Default.SetSkinStyle(DefaultSkin);
            }
            finally
            {
                _applying = false;
            }
        }

        /// <summary>Tema değişikliklerini izlemeye başlar; değişiklik kısa bir gecikmeyle servise kaydedilir.</summary>
        public static void TrackChanges(GuardClient client)
        {
            if (_client != null) return;
            _client = client;
            _saveTimer = new Timer { Interval = 800 };
            _saveTimer.Tick += (s, e) => Save();
            UserLookAndFeel.Default.StyleChanged += (s, e) =>
            {
                if (_applying) return;
                // Tema + palet art arda değişebilir: sadece son hali kaydedilir
                _saveTimer.Stop();
                _saveTimer.Start();
            };
        }

        /// <summary>Kaydedilmeyi bekleyen değişiklik varsa hemen kaydeder (program kapanırken).</summary>
        public static void Flush()
        {
            if (_saveTimer != null && _saveTimer.Enabled) Save();
        }

        private static void Save()
        {
            _saveTimer.Stop();
            if (_client == null || !_client.IsLoggedIn) return;

            var theme = new ThemeDto
            {
                SkinName = UserLookAndFeel.Default.ActiveSkinName,
                PaletteName = UserLookAndFeel.Default.ActiveSvgPaletteName ?? ""
            };
            // Kaydedilemezse (servis kapalı vb.) tema bu oturumda yine de geçerli; sessizce geçilir
            _client.SendAsync(Commands.SetTheme, theme).Wait(TimeSpan.FromSeconds(3));
        }
    }
}
