using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Xml.Linq;

namespace SecurityGuardService.Engine.Collectors
{
    /// <summary>
    /// Windows olay kaydının işlenmeye hazır hali (XML'den çözülür). İşleyiciler EventRecord yerine bunu kullanır:
    /// test edilebilir, kanal / sürüm farklarından bağımsız.
    /// </summary>
    internal sealed class RawEvent
    {
        private static readonly XNamespace Ns = "http://schemas.microsoft.com/win/2004/08/events/event";

        public string Channel { get; private set; }
        public long RecordId { get; private set; }
        public int EventId { get; private set; }
        public string Provider { get; private set; }
        public DateTime TimeUtc { get; private set; }

        /// <summary>İsimli veri alanları (EventData/Data[@Name] veya UserData altındaki elemanlar).</summary>
        public Dictionary<string, string> Data { get; private set; }

        /// <summary>İsimsiz veri alanları sırayla (ör. SQL 18456).</summary>
        public List<string> Values { get; private set; }

        /// <summary>Olayın metin mesajı (varsa; kaynağın mesaj dosyasından).</summary>
        public string Message { get; set; }

        public string Get(string name)
        {
            string v;
            return Data.TryGetValue(name, out v) ? v : null;
        }

        public static RawEvent FromXml(string channel, string xml)
        {
            var doc = XDocument.Parse(xml);
            var root = doc.Root;
            var system = root.Element(Ns + "System");

            var e = new RawEvent
            {
                Channel = channel,
                Data = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase),
                Values = new List<string>()
            };

            if (system != null)
            {
                var provider = system.Element(Ns + "Provider");
                e.Provider = provider == null ? null : (string)provider.Attribute("Name");
                e.EventId = ParseInt(system.Element(Ns + "EventID"));
                long rid;
                var ridEl = system.Element(Ns + "EventRecordID");
                e.RecordId = ridEl != null && long.TryParse(ridEl.Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out rid) ? rid : 0;
                var time = system.Element(Ns + "TimeCreated");
                DateTime t;
                string st = time == null ? null : (string)time.Attribute("SystemTime");
                e.TimeUtc = st != null && DateTime.TryParse(st, CultureInfo.InvariantCulture, DateTimeStyles.AdjustToUniversal | DateTimeStyles.AssumeUniversal, out t)
                    ? t : DateTime.UtcNow;
                var ch = system.Element(Ns + "Channel");
                if (string.IsNullOrEmpty(e.Channel) && ch != null) e.Channel = ch.Value;
            }

            var eventData = root.Element(Ns + "EventData");
            if (eventData != null)
            {
                foreach (var d in eventData.Elements(Ns + "Data"))
                {
                    string name = (string)d.Attribute("Name");
                    if (string.IsNullOrEmpty(name)) e.Values.Add(d.Value);
                    else e.Data[name] = d.Value;
                }
            }

            // Bazı kanallar (RdpCoreTS, log temizleme 1102 / 104) veriyi UserData altında tutar
            var userData = root.Element(Ns + "UserData");
            if (userData != null)
            {
                foreach (var el in userData.Descendants().Where(x => !x.HasElements))
                    if (!e.Data.ContainsKey(el.Name.LocalName)) e.Data[el.Name.LocalName] = el.Value;
            }
            return e;
        }

        private static int ParseInt(XElement el)
        {
            int v;
            return el != null && int.TryParse(el.Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out v) ? v : 0;
        }
    }
}
