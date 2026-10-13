using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Threading.Tasks;
using SecurityGuard.Shared.Contracts;
using SecurityGuard.Shared.Dto;
using SecurityGuardService.Core;
using SecurityGuardService.Core.Data;
using SecurityGuardService.Core.Logging;
using SecurityGuardService.Core.Net;

namespace SecurityGuardService.Engine.Ban
{
    /// <summary>
    /// Beyaz liste: buradaki adreslerden gelen denemeler loglanır ve kurala göre mail atılır ama ASLA banlanmaz.
    ///
    /// Sistem kayıtları (silinemez, her açılışta senkronize edilir):
    ///   localhost, iç ağ aralıkları, link-local ve sunucunun kendi (iç ağ dışı) IP'leri.
    /// Kullanıcı kayıtları: UI'den eklenir / silinir.
    ///
    /// Kontrol (<see cref="IsWhitelisted"/>) bellekteki listeden yapılır, DB'ye gitmez (ban motoru her denemede çağırır).
    /// </summary>
    internal sealed class Whitelist
    {
        private const string Module = "BeyazListe";
        private const int MaxNoteLength = 200;

        private static readonly string[][] StaticSystemEntries =
        {
            new[] { "127.0.0.0/8", "Localhost" },
            new[] { "::1", "Localhost (IPv6)" },
            new[] { "10.0.0.0/8", "İç ağ" },
            new[] { "172.16.0.0/12", "İç ağ" },
            new[] { "192.168.0.0/16", "İç ağ" },
            new[] { "169.254.0.0/16", "Link-local" },
            new[] { "fe80::/10", "Link-local (IPv6)" },
            new[] { "fc00::/7", "İç ağ (IPv6)" },
        };

        private readonly GuardDatabase _db;
        private volatile IpRange[] _ranges = new IpRange[0];

        public Whitelist(GuardDatabase db)
        {
            _db = db;
        }

        public bool IsWhitelisted(IPAddress address)
        {
            foreach (var r in _ranges)
                if (r.Contains(address)) return true;
            return false;
        }

        private static readonly IpRange[] FixedSystemRanges = StaticSystemEntries.Select(e =>
        {
            IpRange r; string err;
            IpRange.TryParse(e[0], out r, out err, allowWide: true);
            return r;
        }).ToArray();

        private static IPAddress[] _ownAddresses = new IPAddress[0];
        private static DateTime _ownAddressesUtc = DateTime.MinValue;

        /// <summary>
        /// Asla banlanmayacak adres mi: localhost, iç ağ, link-local, sunucunun kendi IP'leri (koddan, DB'den bağımsız:
        /// Whitelist tablosu SQLite ekranından silinse bile koruma kalkmaz) veya beyaz liste.
        /// </summary>
        public bool IsProtected(IPAddress address)
        {
            if (IPAddress.IsLoopback(address)) return true;
            foreach (var r in FixedSystemRanges)
                if (r.Contains(address)) return true;

            if (DateTime.UtcNow - _ownAddressesUtc > TimeSpan.FromMinutes(5))
            {
                _ownAddresses = GetOwnAddresses().ToArray(); // IP değişebilir (DHCP): 5 dk'da bir tazelenir
                _ownAddressesUtc = DateTime.UtcNow;
            }
            foreach (var own in _ownAddresses)
                if (own.Equals(address)) return true;

            return IsWhitelisted(address);
        }

        /// <summary>Sistem kayıtlarını olması gereken hale getirir (eksikleri ekler, eskimiş sunucu IP'lerini kaldırır).</summary>
        public void SyncSystemEntries()
        {
            var desired = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            var staticRanges = new List<IpRange>();
            foreach (var e in StaticSystemEntries)
            {
                IpRange r; string err;
                IpRange.TryParse(e[0], out r, out err, allowWide: true);
                desired[r.ToString()] = e[1];
                staticRanges.Add(r);
            }
            foreach (var ip in GetOwnAddresses())
                if (!staticRanges.Any(r => r.Contains(ip)))
                    desired[ip.ToString()] = "Sunucu IP'si";

            var existing = ReadAll();
            var toDelete = existing.Where(e => e.IsSystem && !desired.ContainsKey(e.Cidr)).ToList();
            var toAdd = desired.Where(d => !existing.Any(e => string.Equals(e.Cidr, d.Key, StringComparison.OrdinalIgnoreCase))).ToList();
            if (toDelete.Count == 0 && toAdd.Count == 0) return;

            string now = DbTime.Now();
            _db.Writer.EnqueueAsync(c =>
            {
                foreach (var e in toDelete)
                    c.Execute("DELETE FROM Whitelist WHERE Id = @id AND IsSystem = 1", ("@id", e.Id));
                foreach (var d in toAdd)
                    c.Execute("INSERT INTO Whitelist (Cidr, Note, IsSystem, CreatedUtc) VALUES (@c, @n, 1, @t)",
                        ("@c", d.Key), ("@n", d.Value + " (otomatik)"), ("@t", now));
            }).Wait(TimeSpan.FromSeconds(30));

            AppLog.Info(Module, "Sistem kayıtları güncellendi: " + toAdd.Count + " eklendi, " + toDelete.Count + " kaldırıldı.");
        }

        /// <summary>Bellekteki kontrol listesini DB'den yeniler.</summary>
        public void Reload()
        {
            var ranges = new List<IpRange>();
            foreach (var e in ReadAll())
            {
                IpRange r; string err;
                if (IpRange.TryParse(e.Cidr, out r, out err, allowWide: true))
                    ranges.Add(r);
                else
                    AppLog.Warn(Module, "Geçersiz kayıt atlandı: " + e.Cidr + " (" + err + ")");
            }
            _ranges = ranges.ToArray();
        }

        public List<WhitelistEntry> ReadAll()
        {
            return _db.Read(c =>
            {
                var list = new List<WhitelistEntry>();
                using (var cmd = c.Create("SELECT Id, Cidr, Note, IsSystem, CreatedUtc FROM Whitelist ORDER BY IsSystem DESC, Id"))
                using (var r = cmd.ExecuteReader())
                {
                    while (r.Read())
                        list.Add(new WhitelistEntry
                        {
                            Id = r.GetInt64(0),
                            Cidr = r.GetString(1),
                            Note = r.IsDBNull(2) ? null : r.GetString(2),
                            IsSystem = r.GetInt64(3) != 0,
                            CreatedUtc = r.GetString(4)
                        });
                }
                return list;
            });
        }

        public async Task<WhitelistEntry> AddAsync(string cidr, string note, string userName)
        {
            IpRange range; string error;
            if (!IpRange.TryParse(cidr, out range, out error))
                throw new GuardException(ErrorCodes.Validation, error);

            string normalized = range.ToString();
            note = (note ?? "").Trim();
            if (note.Length > MaxNoteLength) note = note.Substring(0, MaxNoteLength);

            if (ReadAll().Any(e => string.Equals(e.Cidr, normalized, StringComparison.OrdinalIgnoreCase)))
                throw new GuardException(ErrorCodes.Validation, normalized + " zaten beyaz listede.");

            string now = DbTime.Now();
            long id = await _db.Writer.EnqueueAsync(c =>
            {
                c.Execute("INSERT INTO Whitelist (Cidr, Note, IsSystem, CreatedUtc) VALUES (@c, @n, 0, @t)",
                    ("@c", normalized), ("@n", note.Length == 0 ? null : note), ("@t", now));
                return c.LastInsertRowId;
            }).ConfigureAwait(false);

            Reload();
            AppLog.Info(Module, "Eklendi: " + normalized + " (" + userName + ")");
            AuditTrail.Record(_db, userName, "Beyaz listeye eklendi", normalized + (note.Length > 0 ? " - " + note : ""));

            return new WhitelistEntry { Id = id, Cidr = normalized, Note = note, IsSystem = false, CreatedUtc = now };
        }

        public async Task DeleteAsync(ICollection<long> ids, string userName)
        {
            if (ids == null || ids.Count == 0)
                throw new GuardException(ErrorCodes.Validation, "Silinecek kayıt seçin.");

            var rows = ReadAll().Where(e => ids.Contains(e.Id)).ToList();
            if (rows.Any(e => e.IsSystem))
                throw new GuardException(ErrorCodes.Validation, "Sistem kayıtları (iç ağ, localhost, sunucu IP'leri) silinemez.");
            if (rows.Count == 0) return;

            await _db.Writer.EnqueueAsync(c =>
            {
                foreach (var e in rows)
                    c.Execute("DELETE FROM Whitelist WHERE Id = @id AND IsSystem = 0", ("@id", e.Id));
            }).ConfigureAwait(false);

            Reload();
            foreach (var e in rows)
            {
                AppLog.Info(Module, "Silindi: " + e.Cidr + " (" + userName + ")");
                AuditTrail.Record(_db, userName, "Beyaz listeden silindi", e.Cidr);
            }
        }

        private static IEnumerable<IPAddress> GetOwnAddresses()
        {
            var result = new List<IPAddress>();
            try
            {
                foreach (var nic in NetworkInterface.GetAllNetworkInterfaces())
                {
                    if (nic.NetworkInterfaceType == NetworkInterfaceType.Loopback) continue;
                    foreach (var ua in nic.GetIPProperties().UnicastAddresses)
                    {
                        var ip = ua.Address;
                        if (ip.AddressFamily != AddressFamily.InterNetwork && ip.AddressFamily != AddressFamily.InterNetworkV6) continue;
                        IPAddress clean;
                        if (IpRange.TryParseAddress(ip.ToString(), out clean)) result.Add(clean);
                    }
                }
            }
            catch (Exception ex)
            {
                AppLog.Warn(Module, "Sunucu IP adresleri okunamadı.", ex);
            }
            return result;
        }
    }
}
