using System;
using System.Globalization;

namespace SecurityGuardService.Core.Data
{
    /// <summary>
    /// DB'deki tüm zamanlar UTC ve ISO 8601 metin olarak saklanır (2026-09-27T11:03:22.123Z).
    /// Metin olarak sıralanabilir, SQLite sorgu ekranında da okunabilir.
    /// </summary>
    internal static class DbTime
    {
        private const string Format = "yyyy-MM-dd'T'HH:mm:ss.fff'Z'";

        public static string Now()
        {
            return ToDb(DateTime.UtcNow);
        }

        public static string ToDb(DateTime time)
        {
            return time.ToUniversalTime().ToString(Format, CultureInfo.InvariantCulture);
        }

        public static DateTime FromDb(string value)
        {
            return DateTime.ParseExact(value, Format, CultureInfo.InvariantCulture,
                DateTimeStyles.AdjustToUniversal | DateTimeStyles.AssumeUniversal);
        }
    }
}
