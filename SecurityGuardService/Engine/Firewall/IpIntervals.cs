using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using SecurityGuardService.Core.Net;

namespace SecurityGuardService.Engine.Firewall
{
    /// <summary>Kapalı IP aralığı [Start, End] (aynı aileden, big-endian bayt dizisi: IPv4 4, IPv6 16 bayt).</summary>
    internal sealed class IpInterval
    {
        public IpInterval(byte[] start, byte[] end)
        {
            Start = start;
            End = end;
        }

        public byte[] Start { get; private set; }
        public byte[] End { get; private set; }

        public AddressFamily Family { get { return Start.Length == 4 ? AddressFamily.InterNetwork : AddressFamily.InterNetworkV6; } }

        public bool IsSingle { get { return IpIntervals.Compare(Start, End) == 0; } }

        /// <summary>Firewall biçimi: "1.2.3.4" veya "1.2.3.0-1.2.3.255".</summary>
        public override string ToString()
        {
            string a = IpIntervals.ToAddress(Start), b = IpIntervals.ToAddress(End);
            return IsSingle ? a : a + "-" + b;
        }
    }

    /// <summary>
    /// IP aralığı hesapları: kullanıcı girişi ayrıştırma, birleştirme ve tümleyen (izinli olmayan her şey).
    /// Port kısıtı firewall'da "izinli IP'ler DIŞINDAKİLERİ engelle" kuralı olarak yazılır; engel listesi bu tümleyendir.
    /// </summary>
    internal static class IpIntervals
    {
        /// <summary>
        /// Tek giriş: "1.2.3.4", "1.2.3.0/24", "1.2.3.0/255.255.255.0", "1.2.3.10-1.2.3.20" (IPv6 da).
        /// </summary>
        public static bool TryParse(string text, out IpInterval interval, out string error)
        {
            interval = null;
            error = null;
            text = (text ?? "").Trim();
            if (text.Length == 0)
            {
                error = "Boş adres.";
                return false;
            }

            int dash = text.IndexOf('-');
            if (dash > 0)
            {
                IPAddress a, b;
                if (!IpRange.TryParseAddress(text.Substring(0, dash), out a) || !IpRange.TryParseAddress(text.Substring(dash + 1), out b))
                {
                    error = "Geçersiz aralık: " + text + " (örnek: 85.105.0.10-85.105.0.20)";
                    return false;
                }
                if (a.AddressFamily != b.AddressFamily)
                {
                    error = "Aralığın iki ucu aynı türde (IPv4 / IPv6) olmalı: " + text;
                    return false;
                }
                byte[] s = a.GetAddressBytes(), e = b.GetAddressBytes();
                if (Compare(s, e) > 0)
                {
                    error = "Aralığın başı sonundan büyük: " + text;
                    return false;
                }
                interval = new IpInterval(s, e);
                return true;
            }

            int slash = text.IndexOf('/');
            if (slash > 0)
            {
                // Firewall'ın kendi biçimi: 1.2.3.0/255.255.255.0
                string maskText = text.Substring(slash + 1).Trim();
                IPAddress mask;
                if (maskText.Contains(".") && IpRange.TryParseAddress(maskText, out mask))
                {
                    int bits = MaskBits(mask.GetAddressBytes());
                    if (bits < 0)
                    {
                        error = "Geçersiz ağ maskesi: " + maskText;
                        return false;
                    }
                    text = text.Substring(0, slash).Trim() + "/" + bits;
                }
            }

            IpRange range;
            if (!IpRange.TryParse(text, out range, out error, allowWide: true)) return false;
            if (range.PrefixLength == 0)
            {
                error = "/0 tüm adresleri kapsar; kısıt anlamsız olur: " + text;
                return false;
            }
            interval = FromRange(range);
            return true;
        }

        public static IpInterval FromRange(IpRange range)
        {
            byte[] start = IpRangeBytes(range);
            byte[] end = (byte[])start.Clone();
            for (int bit = range.PrefixLength; bit < end.Length * 8; bit++)
                end[bit / 8] |= (byte)(0x80 >> (bit % 8));
            return new IpInterval(start, end);
        }

        private static byte[] IpRangeBytes(IpRange range)
        {
            // IpRange metni normalize (ağ adresi) olduğundan baştan ayrıştırmak yeterli
            string s = range.ToString();
            int slash = s.IndexOf('/');
            return IPAddress.Parse(slash < 0 ? s : s.Substring(0, slash)).GetAddressBytes();
        }

        /// <summary>Sıralar, örtüşen / bitişik aralıkları birleştirir (aile başına).</summary>
        public static List<IpInterval> Merge(IEnumerable<IpInterval> items)
        {
            var result = new List<IpInterval>();
            foreach (var family in items.GroupBy(i => i.Start.Length).OrderBy(g => g.Key))
            {
                IpInterval cur = null;
                foreach (var i in family.OrderBy(i => i.Start, Comparer))
                {
                    if (cur == null)
                    {
                        cur = i;
                        continue;
                    }
                    bool touches = IsMax(cur.End) || Compare(i.Start, Increment(cur.End)) <= 0;
                    if (touches)
                    {
                        if (Compare(i.End, cur.End) > 0) cur = new IpInterval(cur.Start, i.End);
                    }
                    else
                    {
                        result.Add(cur);
                        cur = i;
                    }
                }
                if (cur != null) result.Add(cur);
            }
            return result;
        }

        /// <summary>Verilen ailede, listede OLMAYAN her şey (liste birleştirilmiş olmalı).</summary>
        public static List<IpInterval> Complement(IList<IpInterval> merged, AddressFamily family)
        {
            int length = family == AddressFamily.InterNetwork ? 4 : 16;
            var result = new List<IpInterval>();
            byte[] next = new byte[length]; // 0.0.0.0 / ::
            foreach (var i in merged.Where(m => m.Start.Length == length))
            {
                if (next == null) break;
                if (Compare(i.Start, next) > 0) result.Add(new IpInterval(next, Decrement(i.Start)));
                next = IsMax(i.End) ? null : Increment(i.End);
            }
            if (next != null)
            {
                byte[] max = new byte[length];
                for (int k = 0; k < length; k++) max[k] = 0xFF;
                result.Add(new IpInterval(next, max));
            }
            return result;
        }

        public static bool Contains(IEnumerable<IpInterval> items, IPAddress address)
        {
            if (address == null) return false;
            if (address.IsIPv4MappedToIPv6) address = address.MapToIPv4();
            byte[] b = address.GetAddressBytes();
            return items.Any(i => i.Start.Length == b.Length && Compare(i.Start, b) <= 0 && Compare(b, i.End) <= 0);
        }

        /// <summary>İki listenin kapsadığı adresler aynı mı (yazım biçiminden bağımsız).</summary>
        public static bool SameCoverage(IEnumerable<IpInterval> a, IEnumerable<IpInterval> b)
        {
            var x = Merge(a);
            var y = Merge(b);
            if (x.Count != y.Count) return false;
            for (int i = 0; i < x.Count; i++)
                if (Compare(x[i].Start, y[i].Start) != 0 || Compare(x[i].End, y[i].End) != 0) return false;
            return true;
        }

        /// <summary>Firewall'dan okunan RemoteAddresses metni. Tanınmayan öğe (LocalSubnet vb.) varsa null.</summary>
        public static List<IpInterval> ParseFirewall(string remote)
        {
            var list = new List<IpInterval>();
            if (string.IsNullOrWhiteSpace(remote) || remote.Trim() == "*") return null;
            foreach (var part in remote.Split(','))
            {
                string p = part.Trim();
                if (p.Length == 0) continue;
                IpInterval i;
                string err;
                if (!TryParse(p, out i, out err)) return null;
                list.Add(i);
            }
            return list;
        }

        public static int Compare(byte[] a, byte[] b)
        {
            if (a.Length != b.Length) return a.Length.CompareTo(b.Length);
            for (int i = 0; i < a.Length; i++)
                if (a[i] != b[i]) return a[i].CompareTo(b[i]);
            return 0;
        }

        public static string ToAddress(byte[] bytes)
        {
            var ip = new IPAddress(bytes);
            if (bytes.Length == 16)
            {
                // .NET "::ffff:1.2.3.4" / "::1.2.3.4" yazabilir; firewall'a karışıklık olmasın diye ilk 80 bit sıfırsa açık IPv6 biçimi
                if (LeadingZero(bytes)) return Hex(bytes);
            }
            return ip.ToString();
        }

        private static bool LeadingZero(byte[] b)
        {
            for (int i = 0; i < 10; i++) if (b[i] != 0) return false;
            return true;
        }

        private static string Hex(byte[] b)
        {
            var groups = new string[8];
            for (int i = 0; i < 8; i++) groups[i] = ((b[i * 2] << 8) | b[i * 2 + 1]).ToString("x");
            return string.Join(":", groups);
        }

        private static readonly IComparer<byte[]> Comparer = Comparer<byte[]>.Create(Compare);

        private static bool IsMax(byte[] b)
        {
            foreach (var x in b) if (x != 0xFF) return false;
            return true;
        }

        private static byte[] Increment(byte[] b)
        {
            var r = (byte[])b.Clone();
            for (int i = r.Length - 1; i >= 0; i--)
            {
                if (r[i] != 0xFF)
                {
                    r[i]++;
                    return r;
                }
                r[i] = 0;
            }
            throw new OverflowException();
        }

        private static byte[] Decrement(byte[] b)
        {
            var r = (byte[])b.Clone();
            for (int i = r.Length - 1; i >= 0; i--)
            {
                if (r[i] != 0)
                {
                    r[i]--;
                    return r;
                }
                r[i] = 0xFF;
            }
            throw new OverflowException();
        }

        /// <summary>255.255.255.0 → 24; ardışık olmayan maske → -1.</summary>
        private static int MaskBits(byte[] mask)
        {
            int bits = 0;
            bool zero = false;
            foreach (var m in mask)
                for (int k = 7; k >= 0; k--)
                {
                    bool one = (m & (1 << k)) != 0;
                    if (one && zero) return -1;
                    if (one) bits++;
                    else zero = true;
                }
            return bits;
        }
    }
}
