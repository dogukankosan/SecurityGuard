using System.Collections.Generic;

namespace SecurityGuard.Shared.Dto
{
    /// <summary>İşlem kaydı sorgusu. Tarihler UTC metin (<see cref="UtcTime"/>).</summary>
    public sealed class AuditTrailQuery
    {
        public string FromUtc { get; set; }
        public string ToUtc { get; set; }

        /// <summary>Kullanıcı, işlem veya detayda geçen metin (boşsa filtre yok).</summary>
        public string Search { get; set; }

        public int MaxRows { get; set; }
    }

    public sealed class AuditTrailRow
    {
        public long Id { get; set; }
        public string TimeUtc { get; set; }
        public string UserName { get; set; }
        public string Action { get; set; }
        public string Detail { get; set; }
    }

    public sealed class AuditTrailResult
    {
        public List<AuditTrailRow> Rows { get; set; }

        /// <summary>Sonuç MaxRows'a ulaştı; daha fazla kayıt var (filtre daraltılmalı).</summary>
        public bool Truncated { get; set; }
    }
}
