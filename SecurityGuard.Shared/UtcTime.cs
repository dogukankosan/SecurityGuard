using System;
using System.Globalization;

namespace SecurityGuard.Shared
{
    /// <summary>
    /// DTO'lardaki zamanlar UTC ve ISO 8601 metin olarak taşınır (2026-09-27T11:03:22.123Z).
    /// Servis DB'de de aynı biçimi kullanır; UI gösterirken yerel saate çevirir.
    /// </summary>
    public static class UtcTime
    {
        public const string Format = "yyyy-MM-dd'T'HH:mm:ss.fff'Z'";

        public static string ToText(DateTime time)
        {
            return time.ToUniversalTime().ToString(Format, CultureInfo.InvariantCulture);
        }

        public static DateTime Parse(string text)
        {
            return DateTime.ParseExact(text, Format, CultureInfo.InvariantCulture,
                DateTimeStyles.AdjustToUniversal | DateTimeStyles.AssumeUniversal);
        }

        /// <summary>UTC metni yerel saate çevirir; boş veya hatalıysa null.</summary>
        public static DateTime? ToLocal(string text)
        {
            DateTime utc;
            if (string.IsNullOrEmpty(text) ||
                !DateTime.TryParseExact(text, Format, CultureInfo.InvariantCulture,
                    DateTimeStyles.AdjustToUniversal | DateTimeStyles.AssumeUniversal, out utc))
                return null;
            return utc.ToLocalTime();
        }
    }
}
