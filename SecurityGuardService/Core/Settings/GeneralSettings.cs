using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading.Tasks;
using SecurityGuard.Shared.Contracts;
using SecurityGuard.Shared.Dto;
using SecurityGuardService.Core.Data;
using SecurityGuardService.Core.Logging;

namespace SecurityGuardService.Core.Settings
{
    /// <summary>
    /// Genel Ayarlar sayfasındaki ayarların tek tanım yeri: grup, açıklama, varsayılan ve sınırlar.
    /// Servisin geri kalanı değerleri buradan okur (<see cref="GetInt"/> vb.), böylece varsayılan ve sınır
    /// tek yerde durur; DB'ye elle yanlış değer yazılsa bile sınır dışına çıkılmaz.
    /// </summary>
    internal static class GeneralSettings
    {
        private const string Module = "Ayarlar";

        private const string GroupAuth = "Arayüz girişi";
        private const string GroupReport = "Günlük rapor";
        private const string GroupSql = "SQL Server izleme";
        private const string GroupPerf = "Performans ve bakım";
        private const string GroupMail = "Mail";
        private const string GroupSystem = "Sistem";
        private const string GroupResource = "İşlemci ve bellek izleme";
        private const string GroupWork = "Mesai saatleri";
        private const string GroupProtect = "Servis koruması";

        private static readonly Definition[] All =
        {
            Int(SettingKeys.AuthMaxFailedLogins, GroupAuth, "Hatalı giriş hakkı", 5, 3, 20, "deneme",
                "Bu kadar art arda yanlış şifre girilince hesap geçici olarak kilitlenir."),
            Int(SettingKeys.AuthLockMinutes, GroupAuth, "Kilit süresi", 15, 1, 1440, "dakika",
                "Hesap kilitlenince bu süre boyunca doğru şifreyle de girilemez. Süre dolunca kilit kendiliğinden kalkar."),
            Int(SettingKeys.AuthSessionIdleMinutes, GroupAuth, "Oturum zaman aşımı", 30, 5, 240, "dakika",
                "Bu süre boyunca işlem yapılmazsa oturum kapanır ve tekrar giriş istenir. Sonraki girişte geçerli olur."),

            Time(SettingKeys.ReportDailyTime, GroupReport, "Rapor saati", "20:00",
                "Günlük güvenlik + performans raporu maili ve sağlık / performans taraması bu saatte yapılır."),
            Time(SettingKeys.ReportMonthlyTime, GroupReport, "Aylık rapor saati", "09:00",
                "Aylık yönetici raporu her ayın 1'inde bu saatte önceki ayı özetler. Alıcılar: Olay Kuralları → \"Aylık yönetici raporu\"."),

            Int(SettingKeys.SqlBaselineIntervalMinutes, GroupSql, "Değişiklik kontrol aralığı", 5, 1, 60, "dakika",
                "Login, yetki, sertifika, DB ve yapılandırma bilgileri bu aralıkla bir öncekiyle karşılaştırılır (olay kaçsa bile fark bulunur)."),
            Bool(SettingKeys.SqlAuditAutoRestart, GroupSql, "Audit durdurulursa yeniden başlat", true,
                "SecurityGuard'ın SQL Server Audit nesnesi biri tarafından durdurulursa servis onu tekrar başlatır (olay yine mail ile bildirilir)."),

            Int(SettingKeys.PerfFragReorganizePercent, GroupPerf, "Parçalanma: düzenleme eşiği", 5, 1, 50, "%",
                "Index parçalanması bu orandan itibaren raporlanır ve REORGANIZE önerilir."),
            Int(SettingKeys.PerfFragRebuildPercent, GroupPerf, "Parçalanma: yeniden oluşturma eşiği", 30, 10, 90, "%",
                "Bu oranın üstündeki index'ler için REBUILD önerilir (düzenleme eşiğinden büyük olmalı)."),
            Int(SettingKeys.PerfMinPageCount, GroupPerf, "En küçük index boyutu", 1000, 100, 1000000, "sayfa",
                "Bundan küçük index'lerin parçalanması performansı etkilemez, dikkate alınmaz (1000 sayfa ≈ 8 MB)."),

            Int(SettingKeys.MailBatchMinutes, GroupMail, "Toplama süresi", 2, 0, 60, "dakika",
                "Kritik olayın ilk maili hemen gider; aynı olay bu süre içinde tekrarlanırsa sonrakiler tek özet mailde gelir. Kritik olmayanlar hep toplanır. 0 = her olay ayrı ve hemen."),

            Int(SettingKeys.CpuAlarmPercent, GroupResource, "İşlemci uyarı eşiği", 90, 50, 100, "%",
                "Sunucunun toplam işlemci kullanımı bu oranın üstünde aşağıdaki süre boyunca kalırsa mail ile bildirilir (en çok kullanan programlar ve kullanıcılar ile)."),
            Int(SettingKeys.RamAlarmPercent, GroupResource, "Bellek uyarı eşiği", 90, 50, 99, "%",
                "Bellek kullanımı bu oranın üstünde aşağıdaki süre boyunca kalırsa mail ile bildirilir. Not: SQL Server belleği bilerek doldurur; bellek sınırı ayarlı değilse bu uyarı sık gelir (Performans Önerileri → Bellek sınırı)."),
            Int(SettingKeys.ResourceAlarmMinutes, GroupResource, "Uyarı için süre", 10, 1, 120, "dakika",
                "Kısa süreli tepeler uyarı üretmez: eşik bu kadar dakika aralıksız aşılmalı. Aynı durum sürerse 6 saatte bir tekrar bildirilir."),

            Bool(SettingKeys.WorkHoursAlarm, GroupWork, "Mesai dışı RDP girişi uyarısı", true,
                "Mesai dışında başarılı RDP girişi (veya kopuk oturuma yeniden bağlanma) olursa kişi, IP ve ülke ile mail atılır. Kullanıcıya özel istisnalar: İzleme → RDP Oturumları → Mesai istisnaları."),
            Choice(SettingKeys.WorkDays, GroupWork, "Çalışma günleri", "weekdays",
                "Bu günler dışındaki tüm girişler mesai dışı sayılır.",
                Option("weekdays", "Hafta içi (Pazartesi - Cuma)"), Option("saturday", "Pazartesi - Cumartesi"), Option("all", "Her gün (sadece saat)")),
            Time(SettingKeys.WorkStart, GroupWork, "Mesai başlangıcı", "08:00", "Bu saatten önceki girişler mesai dışı sayılır."),
            Time(SettingKeys.WorkEnd, GroupWork, "Mesai bitişi", "19:00",
                "Bu saatten sonraki girişler mesai dışı sayılır. Başlangıçtan küçükse gece vardiyası kabul edilir (örn. 22:00 - 06:00)."),

            Bool(SettingKeys.ProtectService, GroupProtect, "Windows'tan durdurmayı engelle", true,
                "Açıkken SecurityGuard servisi Hizmetler ekranı, sc / net stop ile durdurulamaz, ayarı değiştirilemez, silinemez (yönetici dahil). "
                + "Durdurma / kaldırma: SecurityGuard → Servis sayfası (yönetici şifresiyle). Güncelleme öncesi servisi oradan durdurun."),
            Bool(SettingKeys.ProtectData, GroupProtect, "Veritabanı ve log klasörünü koru", true,
                "Database ve Logs klasörlerinin sahibi SYSTEM olur; yöneticiler sadece okuyabilir (silme / değiştirme yok). Değiştirilirse geri alınır ve mail gelir."),
            Bool(SettingKeys.ProtectBackup, GroupProtect, "SQL yedek klasörünü koru", true,
                "SQL Yedekleme hedef klasörünü fidye yazılımına karşı korur: yöneticiler yedekleri okuyup kopyalayabilir ama silemez / değiştiremez. "
                + "Sürücü kökü ve ağ yolu korunmaz. Kapatınca klasör üst klasörün izinlerine döner."),

            Choice(SettingKeys.LogLevel, GroupSystem, "Log seviyesi", "Info",
                "Logs klasörüne yazılan uygulama logunun ayrıntı düzeyi. Sorun araştırılmıyorsa Normal önerilir.",
                Option("Debug", "Ayrıntılı (Debug)"), Option("Info", "Normal"), Option("Warn", "Sadece uyarı ve hatalar"), Option("Error", "Sadece hatalar")),
            Int(SettingKeys.DiskAlarmPercent, GroupSystem, "Disk doluluk uyarısı", 90, 50, 99, "%",
                "Herhangi bir disk bu oranın üstüne çıkarsa uyarı verilir.")
        };

        // ================================================================ Okuma (servis içi)

        public static int GetInt(SettingsStore settings, string key)
        {
            var d = Find(key);
            int value;
            if (!int.TryParse(settings.Get(key), NumberStyles.Integer, CultureInfo.InvariantCulture, out value))
                value = int.Parse(d.Default, CultureInfo.InvariantCulture);
            return Math.Min(d.Max, Math.Max(d.Min, value));
        }

        public static bool GetBool(SettingsStore settings, string key)
        {
            var d = Find(key);
            bool value;
            return bool.TryParse(settings.Get(key), out value) ? value : bool.Parse(d.Default);
        }

        /// <summary>Seçim ayarı; kayıtlı değer seçeneklerde yoksa varsayılan.</summary>
        public static string GetChoice(SettingsStore settings, string key)
        {
            var d = Find(key);
            string value = settings.Get(key);
            return value != null && d.Options != null && d.Options.Any(o => o.Value == value) ? value : d.Default;
        }

        public static TimeSpan GetTime(SettingsStore settings, string key)
        {
            var d = Find(key);
            TimeSpan value;
            return TryParseTime(settings.Get(key), out value) ? value : ParseTimeOrThrow(d.Default);
        }

        // ================================================================ UI

        public static List<GeneralSettingDto> List(SettingsStore settings)
        {
            return All.Select(d => new GeneralSettingDto
            {
                Key = d.Key,
                Group = d.Group,
                Label = d.Label,
                Description = d.Description,
                Kind = d.Kind,
                Value = Current(settings, d),
                DefaultValue = d.Default,
                Min = d.Min,
                Max = d.Max,
                Unit = d.Unit,
                Options = d.Options == null ? null : d.Options.ToList()
            }).ToList();
        }

        /// <summary>Tüm değerleri önce doğrular (biri hatalıysa hiçbiri kaydedilmez), sonra sadece değişenleri yazar.</summary>
        public static async Task SaveAsync(SettingsStore settings, GuardDatabase db, IList<SettingValueDto> values, string userName)
        {
            if (values == null || values.Count == 0) return;

            var changes = new List<Tuple<Definition, string, string>>();
            foreach (var v in values)
            {
                var d = All.FirstOrDefault(x => x.Key == (v == null ? null : v.Key));
                if (d == null) throw new GuardException(ErrorCodes.Validation, "Bilinmeyen ayar: " + (v == null ? "(boş)" : v.Key));

                string normalized = Normalize(d, v.Value);
                string old = Current(settings, d);
                if (normalized != old) changes.Add(Tuple.Create(d, old, normalized));
            }

            // Birbirine bağlı ayarlar: kaydedilecek son hâllerine göre
            Func<string, int> final = key =>
            {
                var c = changes.FirstOrDefault(x => x.Item1.Key == key);
                return c != null ? int.Parse(c.Item3, CultureInfo.InvariantCulture) : GetInt(settings, key);
            };
            if (final(SettingKeys.PerfFragRebuildPercent) <= final(SettingKeys.PerfFragReorganizePercent))
                throw new GuardException(ErrorCodes.Validation, "Parçalanma: yeniden oluşturma eşiği, düzenleme eşiğinden büyük olmalı.");

            foreach (var c in changes)
            {
                await settings.SetAsync(c.Item1.Key, c.Item3).ConfigureAwait(false);
                AuditTrail.Record(db, userName, "Genel ayar değişti", c.Item1.Label + ": " + Display(c.Item1, c.Item2) + " → " + Display(c.Item1, c.Item3));
            }
            if (changes.Count > 0)
                AppLog.Info(Module, changes.Count + " genel ayar değişti (" + userName + ").");
        }

        // ================================================================ Yardımcılar

        /// <summary>DB'deki değer geçersizse varsayılan gösterilir (servis de varsayılanı kullanır).</summary>
        private static string Current(SettingsStore settings, Definition d)
        {
            try
            {
                return Normalize(d, settings.Get(d.Key) ?? d.Default);
            }
            catch (GuardException)
            {
                return d.Default;
            }
        }

        private static string Normalize(Definition d, string raw)
        {
            raw = (raw ?? "").Trim();
            switch (d.Kind)
            {
                case "int":
                    int n;
                    if (!int.TryParse(raw, NumberStyles.Integer, CultureInfo.InvariantCulture, out n))
                        throw new GuardException(ErrorCodes.Validation, d.Label + ": sayı girin.");
                    if (n < d.Min || n > d.Max)
                        throw new GuardException(ErrorCodes.Validation, d.Label + ": " + d.Min + " ile " + d.Max + " arasında olmalı.");
                    return n.ToString(CultureInfo.InvariantCulture);

                case "bool":
                    bool b;
                    if (!bool.TryParse(raw, out b)) throw new GuardException(ErrorCodes.Validation, d.Label + ": geçersiz değer.");
                    return b ? "true" : "false";

                case "time":
                    TimeSpan t;
                    if (!TryParseTime(raw, out t)) throw new GuardException(ErrorCodes.Validation, d.Label + ": saat SS:dd biçiminde olmalı (ör. 20:00).");
                    return t.ToString(@"hh\:mm", CultureInfo.InvariantCulture);

                default: // choice
                    var option = d.Options.FirstOrDefault(o => string.Equals(o.Value, raw, StringComparison.OrdinalIgnoreCase));
                    if (option == null) throw new GuardException(ErrorCodes.Validation, d.Label + ": geçersiz seçim.");
                    return option.Value;
            }
        }

        private static string Display(Definition d, string value)
        {
            switch (d.Kind)
            {
                case "bool": return value == "true" ? "açık" : "kapalı";
                case "choice":
                    var o = d.Options.FirstOrDefault(x => x.Value == value);
                    return o == null ? value : o.Text;
                case "int": return string.IsNullOrEmpty(d.Unit) ? value : d.Unit == "%" ? "%" + value : value + " " + d.Unit;
                default: return value;
            }
        }

        private static bool TryParseTime(string raw, out TimeSpan value)
        {
            value = TimeSpan.Zero;
            if (string.IsNullOrWhiteSpace(raw)) return false;
            DateTime dt;
            if (!DateTime.TryParseExact(raw.Trim(), new[] { "H:mm", "HH:mm" }, CultureInfo.InvariantCulture, DateTimeStyles.None, out dt)) return false;
            value = dt.TimeOfDay;
            return true;
        }

        private static TimeSpan ParseTimeOrThrow(string raw)
        {
            TimeSpan t;
            if (!TryParseTime(raw, out t)) throw new InvalidOperationException("Geçersiz varsayılan saat: " + raw);
            return t;
        }

        private static Definition Find(string key)
        {
            var d = All.FirstOrDefault(x => x.Key == key);
            if (d == null) throw new InvalidOperationException("Genel ayar tanımı yok: " + key);
            return d;
        }

        private static Definition Int(string key, string group, string label, int def, int min, int max, string unit, string description)
        {
            return new Definition { Key = key, Group = group, Label = label, Kind = "int", Default = def.ToString(CultureInfo.InvariantCulture), Min = min, Max = max, Unit = unit, Description = description };
        }

        private static Definition Bool(string key, string group, string label, bool def, string description)
        {
            return new Definition { Key = key, Group = group, Label = label, Kind = "bool", Default = def ? "true" : "false", Description = description };
        }

        private static Definition Time(string key, string group, string label, string def, string description)
        {
            return new Definition { Key = key, Group = group, Label = label, Kind = "time", Default = def, Description = description };
        }

        private static Definition Choice(string key, string group, string label, string def, string description, params SettingOptionDto[] options)
        {
            return new Definition { Key = key, Group = group, Label = label, Kind = "choice", Default = def, Description = description, Options = options };
        }

        private static SettingOptionDto Option(string value, string text)
        {
            return new SettingOptionDto { Value = value, Text = text };
        }

        private sealed class Definition
        {
            public string Key, Group, Label, Description, Kind, Default, Unit;
            public int Min, Max;
            public SettingOptionDto[] Options;
        }
    }
}
