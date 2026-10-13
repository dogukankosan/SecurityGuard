using System.Collections.Generic;

namespace SecurityGuard.Shared.Dto
{
    /// <summary>SQLite sorgu ekranından çalıştırılacak SQL (birden fazla ifade ; ile ayrılabilir).</summary>
    public sealed class SqliteExecuteRequest
    {
        public string Sql { get; set; }
    }

    /// <summary>Sorgunun sonucu: sonuç kümeleri + etkilenen satır sayısı.</summary>
    public sealed class SqliteExecuteResultDto
    {
        public List<SqliteResultSetDto> ResultSets { get; set; }

        /// <summary>INSERT / UPDATE / DELETE ile etkilenen toplam satır (tetikleyiciler dahil).</summary>
        public int RecordsAffected { get; set; }

        public long ElapsedMs { get; set; }

        /// <summary>Bilgi notu (ör. açık kalan transaction geri alındı).</summary>
        public string Message { get; set; }
    }

    public sealed class SqliteResultSetDto
    {
        public List<string> Columns { get; set; }

        /// <summary>Hücreler metin olarak; NULL değer null döner.</summary>
        public List<List<string>> Rows { get; set; }

        /// <summary>Kümedeki toplam satır (Rows sınırda kesildiyse bundan azdır).</summary>
        public int TotalRows { get; set; }

        public bool Truncated { get; set; }
    }

    /// <summary>Veritabanı yapısı (sol paneldeki tablo listesi).</summary>
    public sealed class SqliteSchemaDto
    {
        public string FilePath { get; set; }
        public long FileSizeBytes { get; set; }
        public int SchemaVersion { get; set; }
        public List<SqliteTableDto> Tables { get; set; }
    }

    public sealed class SqliteTableDto
    {
        public string Name { get; set; }

        /// <summary>"table" veya "view".</summary>
        public string Type { get; set; }

        /// <summary>Tablolar için satır sayısı (view için -1).</summary>
        public long RowCount { get; set; }

        public List<SqliteColumnDto> Columns { get; set; }
    }

    public sealed class SqliteColumnDto
    {
        public string Name { get; set; }
        public string Type { get; set; }
        public bool IsPrimaryKey { get; set; }
        public bool NotNull { get; set; }
    }
}
