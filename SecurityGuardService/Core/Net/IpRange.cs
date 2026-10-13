using System;
using System.Net;
using System.Net.Sockets;

namespace SecurityGuardService.Core.Net
{
    /// <summary>
    /// Tek IP veya CIDR aralığı (IPv4 / IPv6). Beyaz liste ve ban motoru kullanır.
    /// Metin her zaman normalize edilir: "10.1.2.3/8" → "10.0.0.0/8", "1.2.3.4/32" → "1.2.3.4".
    /// </summary>
    internal sealed class IpRange
    {
        /// <summary>Beyaz listeye eklenebilecek en geniş aralıklar (tüm interneti yanlışlıkla beyaz listeye almayı engeller).</summary>
        public const int MinPrefixV4 = 8;
        public const int MinPrefixV6 = 16;

        private readonly byte[] _network;

        private IpRange(byte[] network, int prefixLength, AddressFamily family)
        {
            _network = network;
            PrefixLength = prefixLength;
            Family = family;
        }

        public int PrefixLength { get; private set; }

        public AddressFamily Family { get; private set; }

        public int MaxPrefix { get { return _network.Length * 8; } }

        public bool IsSingleAddress { get { return PrefixLength == MaxPrefix; } }

        /// <param name="error">Türkçe hata mesajı (kullanıcıya gösterilir).</param>
        /// <param name="allowWide">Sistemin kendi eklediği geniş aralıklar için (fc00::/7 vb.); kullanıcı girişinde false.</param>
        public static bool TryParse(string text, out IpRange range, out string error, bool allowWide = false)
        {
            range = null;
            error = null;
            text = (text ?? "").Trim();
            if (text.Length == 0)
            {
                error = "IP adresi girin.";
                return false;
            }

            string addressPart = text;
            int prefix = -1;
            int slash = text.IndexOf('/');
            if (slash >= 0)
            {
                addressPart = text.Substring(0, slash).Trim();
                if (!int.TryParse(text.Substring(slash + 1).Trim(), out prefix))
                {
                    error = "Aralık (/ sonrası) sayı olmalı. Örnek: 192.168.1.0/24";
                    return false;
                }
            }

            IPAddress address;
            if (!TryParseAddress(addressPart, out address))
            {
                error = "Geçersiz IP adresi: " + addressPart;
                return false;
            }

            byte[] bytes = address.GetAddressBytes();
            int max = bytes.Length * 8;
            if (prefix < 0) prefix = max;
            if (prefix > max)
            {
                error = "Aralık en fazla /" + max + " olabilir.";
                return false;
            }

            int min = address.AddressFamily == AddressFamily.InterNetwork ? MinPrefixV4 : MinPrefixV6;
            if (prefix < min && !allowWide)
            {
                error = "Aralık çok geniş (/" + prefix + "). En geniş /" + min + " girilebilir.";
                return false;
            }

            range = new IpRange(Mask(bytes, prefix), prefix, address.AddressFamily);
            return true;
        }

        /// <summary>Tek IP adresi ayrıştırır. IPv4 için tam 4 bölüm şartı var ("10.1" gibi eksik yazımlar reddedilir).</summary>
        public static bool TryParseAddress(string text, out IPAddress address)
        {
            address = null;
            text = (text ?? "").Trim();
            if (!IPAddress.TryParse(text, out address)) return false;

            if (address.AddressFamily == AddressFamily.InterNetwork && text.Split('.').Length != 4)
            {
                address = null;
                return false;
            }
            if (address.IsIPv4MappedToIPv6) address = address.MapToIPv4();
            if (address.AddressFamily == AddressFamily.InterNetworkV6) address.ScopeId = 0; // "fe80::1%12" → "fe80::1"
            return true;
        }

        public bool Contains(IPAddress address)
        {
            if (address == null) return false;
            if (address.IsIPv4MappedToIPv6) address = address.MapToIPv4();
            if (address.AddressFamily != Family) return false;

            byte[] bytes = address.GetAddressBytes();
            int fullBytes = PrefixLength / 8;
            for (int i = 0; i < fullBytes; i++)
                if (bytes[i] != _network[i]) return false;

            int remBits = PrefixLength % 8;
            if (remBits == 0) return true;
            byte mask = (byte)(0xFF << (8 - remBits));
            return (bytes[fullBytes] & mask) == _network[fullBytes];
        }

        public override string ToString()
        {
            string ip = new IPAddress(_network).ToString();
            return IsSingleAddress ? ip : ip + "/" + PrefixLength;
        }

        private static byte[] Mask(byte[] bytes, int prefix)
        {
            var result = (byte[])bytes.Clone();
            for (int i = 0; i < result.Length; i++)
            {
                int bitsInByte = Math.Max(0, Math.Min(8, prefix - i * 8));
                result[i] &= (byte)(bitsInByte == 0 ? 0 : 0xFF << (8 - bitsInByte));
            }
            return result;
        }
    }
}
