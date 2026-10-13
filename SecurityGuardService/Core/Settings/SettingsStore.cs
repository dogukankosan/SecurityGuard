using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading.Tasks;
using SecurityGuardService.Core.Data;
using SecurityGuardService.Core.Logging;
using SecurityGuardService.Core.Security;

namespace SecurityGuardService.Core.Settings
{
    /// <summary>
    /// Settings tablosunun bellekteki kopyası. Okumalar DB'ye gitmez (hızlı);
    /// yazmalar DB'ye commit edildikten sonra belleğe yansır ve <see cref="Changed"/> ile modüllere bildirilir,
    /// böylece ayar değişince servis yeniden başlatılmadan canlı uygulanır.
    ///
    /// Şifreli ayarlar (SetSecretAsync) DPAPI ile, anahtar adı "amaç" olarak şifrelenir.
    /// Get() şifreli bir ayarı asla döndürmez (yanlışlıkla UI'ye / loga sızmasın); sadece GetSecret() çözer.
    /// </summary>
    internal sealed class SettingsStore
    {
        private const string Module = "Ayarlar";

        private readonly GuardDatabase _db;
        private readonly ConcurrentDictionary<string, Entry> _cache = new ConcurrentDictionary<string, Entry>(StringComparer.Ordinal);

        public SettingsStore(GuardDatabase db)
        {
            _db = db;
        }

        /// <summary>Bir ayar değiştiğinde (commit sonrası) anahtar adıyla tetiklenir.</summary>
        public event Action<string> Changed;

        public void Load()
        {
            _db.Read(conn =>
            {
                using (var cmd = conn.Create("SELECT Key, Value, IsEncrypted FROM Settings"))
                using (var r = cmd.ExecuteReader())
                {
                    while (r.Read())
                        _cache[r.GetString(0)] = new Entry(r.IsDBNull(1) ? null : r.GetString(1), r.GetInt64(2) != 0);
                }
                return 0;
            });
            AppLog.Debug(Module, _cache.Count + " ayar yüklendi.");
        }

        public bool Contains(string key)
        {
            return _cache.ContainsKey(key);
        }

        public string Get(string key, string defaultValue = null)
        {
            Entry e;
            if (!_cache.TryGetValue(key, out e) || e.IsEncrypted || e.Value == null) return defaultValue;
            return e.Value;
        }

        public int GetInt(string key, int defaultValue)
        {
            int v;
            return int.TryParse(Get(key), NumberStyles.Integer, CultureInfo.InvariantCulture, out v) ? v : defaultValue;
        }

        public bool GetBool(string key, bool defaultValue)
        {
            string v = Get(key);
            if (v == "1") return true;
            if (v == "0") return false;
            return defaultValue;
        }

        public TEnum GetEnum<TEnum>(string key, TEnum defaultValue) where TEnum : struct
        {
            TEnum v;
            return Enum.TryParse(Get(key), true, out v) ? v : defaultValue;
        }

        /// <summary>Şifreli ayarı çözer. Ayar yoksa veya çözülemezse null (hata loglanır).</summary>
        public string GetSecret(string key)
        {
            Entry e;
            if (!_cache.TryGetValue(key, out e) || !e.IsEncrypted || e.Value == null) return null;
            try
            {
                return SecretProtector.Unprotect(e.Value, key);
            }
            catch (Exception ex)
            {
                AppLog.Error(Module, "Şifreli ayar çözülemedi: " + key, ex);
                return null;
            }
        }

        public Task SetAsync(string key, string value)
        {
            return SaveAsync(key, value, false);
        }

        public Task SetAsync(string key, int value)
        {
            return SaveAsync(key, value.ToString(CultureInfo.InvariantCulture), false);
        }

        public Task SetAsync(string key, bool value)
        {
            return SaveAsync(key, value ? "1" : "0", false);
        }

        public Task SetSecretAsync(string key, string plainText)
        {
            return SaveAsync(key, SecretProtector.Protect(plainText, key), true);
        }

        private async Task SaveAsync(string key, string storedValue, bool encrypted)
        {
            await _db.Writer.EnqueueAsync(conn => conn.Execute(
                @"INSERT INTO Settings (Key, Value, IsEncrypted, UpdatedUtc) VALUES (@k, @v, @e, @t)
                  ON CONFLICT(Key) DO UPDATE SET Value = excluded.Value, IsEncrypted = excluded.IsEncrypted, UpdatedUtc = excluded.UpdatedUtc",
                ("@k", key), ("@v", storedValue), ("@e", encrypted ? 1 : 0), ("@t", DbTime.Now()))).ConfigureAwait(false);

            _cache[key] = new Entry(storedValue, encrypted);
            AppLog.Info(Module, "Ayar değişti: " + key + (encrypted ? " (şifreli)" : " = " + Shorten(storedValue)));
            Notify(key);
        }

        /// <summary>
        /// Belleği DB'den baştan okur (SQLite sorgu ekranından elle yapılan değişiklikler için).
        /// Değişen / eklenen / silinen her anahtar için <see cref="Changed"/> tetiklenir.
        /// </summary>
        public void Reload()
        {
            var fresh = new Dictionary<string, Entry>(StringComparer.Ordinal);
            _db.Read(conn =>
            {
                using (var cmd = conn.Create("SELECT Key, Value, IsEncrypted FROM Settings"))
                using (var r = cmd.ExecuteReader())
                {
                    while (r.Read())
                        fresh[r.GetString(0)] = new Entry(r.IsDBNull(1) ? null : r.GetString(1), r.GetInt64(2) != 0);
                }
                return 0;
            });

            var changed = new List<string>();
            foreach (var pair in fresh)
            {
                Entry old;
                if (!_cache.TryGetValue(pair.Key, out old) || old.Value != pair.Value.Value || old.IsEncrypted != pair.Value.IsEncrypted)
                {
                    _cache[pair.Key] = pair.Value;
                    changed.Add(pair.Key);
                }
            }
            foreach (var key in _cache.Keys.Where(k => !fresh.ContainsKey(k)).ToList())
            {
                Entry removed;
                _cache.TryRemove(key, out removed);
                changed.Add(key);
            }

            if (changed.Count == 0) return;
            AppLog.Info(Module, "Ayarlar DB'den yeniden okundu, değişen: " + string.Join(", ", changed));
            foreach (var key in changed) Notify(key);
        }

        private void Notify(string key)
        {
            var handler = Changed;
            if (handler == null) return;
            foreach (Action<string> h in handler.GetInvocationList())
            {
                try
                {
                    h(key);
                }
                catch (Exception ex)
                {
                    // Bir modülün hatası diğerlerinin bildirimi almasını engellemez
                    AppLog.Error(Module, "Ayar değişikliği bildirimi işlenemedi: " + key, ex);
                }
            }
        }

        // Uzun değerler (ör. HTML imza) logu şişirmesin
        private static string Shorten(string value)
        {
            if (value == null) return "(boş)";
            return value.Length <= 200 ? value : value.Substring(0, 200) + "... (" + value.Length + " karakter)";
        }

        private sealed class Entry
        {
            public readonly string Value;
            public readonly bool IsEncrypted;

            public Entry(string value, bool isEncrypted)
            {
                Value = value;
                IsEncrypted = isEncrypted;
            }
        }
    }
}
