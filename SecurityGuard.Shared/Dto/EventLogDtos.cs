using System.Collections.Generic;

namespace SecurityGuard.Shared.Dto
{
    /// <summary>Olay Logu sorgusu. Tarihler UTC metin (<see cref="UtcTime"/>).</summary>
    public sealed class EventLogQuery
    {
        public string FromUtc { get; set; }
        public string ToUtc { get; set; }

        /// <summary>En düşük önem (Severity sayısı); -1 = hepsi.</summary>
        public int MinSeverity { get; set; }

        /// <summary>Olay grubu (Giriş, Windows, SQL Server...); boşsa hepsi.</summary>
        public string Category { get; set; }

        /// <summary>Özet, detay, kullanıcı, IP veya olay tipinde geçen metin.</summary>
        public string Search { get; set; }

        public int MaxRows { get; set; }
    }

    public sealed class EventLogRow
    {
        public long Id { get; set; }
        public string TimeUtc { get; set; }
        public string EventType { get; set; }

        /// <summary>Olayın Türkçe adı ve grubu (katalogdan; bilinmeyen tipte EventType).</summary>
        public string EventName { get; set; }
        public string Category { get; set; }

        public int Severity { get; set; }
        public string Source { get; set; }
        public string UserName { get; set; }
        public string IpAddress { get; set; }
        public string Summary { get; set; }
        public string Detail { get; set; }

        /// <summary>Olay için mail kuyruğa alındı.</summary>
        public bool MailSent { get; set; }
    }

    public sealed class EventLogResult
    {
        public List<EventLogRow> Rows { get; set; }
        public bool Truncated { get; set; }

        /// <summary>Filtre kutusu için olay grupları.</summary>
        public List<string> Categories { get; set; }
    }

    /// <summary>Olay logu bütünlük (HMAC zinciri) kontrolü sonucu.</summary>
    public sealed class EventLogVerifyResult
    {
        /// <summary>Zincir anahtarı yok / çözülemiyor: kontrol yapılamadı.</summary>
        public bool ChainEnabled { get; set; }

        public int CheckedRows { get; set; }

        /// <summary>İçeriği sonradan değiştirilmiş satırlar.</summary>
        public int ModifiedRows { get; set; }

        /// <summary>Arasından kayıt silinmiş noktalar.</summary>
        public int Gaps { get; set; }

        /// <summary>İmzasız yazılmış satırlar (zincir kapalıyken).</summary>
        public int UnsignedRows { get; set; }

        /// <summary>İlk sorunlu satırların Id'leri (en fazla 20).</summary>
        public List<long> ProblemIds { get; set; }

        /// <summary>SQLite Sorgu ekranından (yönetici) silinen kayıtlar: imzalı kaydı var, oynama sayılmaz.</summary>
        public int AdminDeletedRows { get; set; }

        /// <summary>SQLite Sorgu ekranından (yönetici) değiştirilen kayıtlar: imzalı kaydı var, oynama sayılmaz.</summary>
        public int AdminModifiedRows { get; set; }

        /// <summary>Daha önce "mevcut durumu kabul et" ile onaylanmış sorunlu satırlar.</summary>
        public int AcceptedRows { get; set; }

        /// <summary>Sondan silinmiş, açıklanamayan kayıt sayısı (en son kayıtlar silinmiş).</summary>
        public int TailDeletedRows { get; set; }

        public bool IsIntact { get { return ChainEnabled && ModifiedRows == 0 && Gaps == 0 && TailDeletedRows == 0; } }
    }
}
