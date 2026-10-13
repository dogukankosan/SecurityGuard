using System;
using System.Collections.Generic;
using System.Data.SqlClient;
using System.Globalization;
using System.Threading;

namespace SecurityGuardService.Checks
{
    /// <summary>Taramalar için kısa SQL Server okuma yardımcıları (servis kapanırsa komut iptal edilir).</summary>
    internal static class SqlQuery
    {
        public static List<object[]> Rows(SqlConnection conn, string sql, CancellationToken token, int timeoutSeconds = 120)
        {
            var list = new List<object[]>();
            using (var cmd = conn.CreateCommand())
            {
                cmd.CommandText = sql;
                cmd.CommandTimeout = timeoutSeconds;
                using (token.Register(() => { try { cmd.Cancel(); } catch { } }))
                using (var r = cmd.ExecuteReader())
                {
                    while (r.Read())
                    {
                        var row = new object[r.FieldCount];
                        r.GetValues(row);
                        for (int i = 0; i < row.Length; i++)
                            if (row[i] is DBNull) row[i] = null;
                        list.Add(row);
                    }
                }
            }
            token.ThrowIfCancellationRequested();
            return list;
        }

        public static object Scalar(SqlConnection conn, string sql, CancellationToken token, int timeoutSeconds = 60)
        {
            var rows = Rows(conn, sql, token, timeoutSeconds);
            return rows.Count == 0 ? null : rows[0][0];
        }

        /// <summary>[ad] (köşeli parantez kaçışlı).</summary>
        public static string Q(string name)
        {
            return "[" + (name ?? "").Replace("]", "]]") + "]";
        }

        /// <summary>N'metin' (tırnak kaçışlı).</summary>
        public static string N(string text)
        {
            return "N'" + (text ?? "").Replace("'", "''") + "'";
        }

        public static long Long(object v)
        {
            return v == null ? 0 : Convert.ToInt64(v, CultureInfo.InvariantCulture);
        }

        public static double Dbl(object v)
        {
            return v == null ? 0 : Convert.ToDouble(v, CultureInfo.InvariantCulture);
        }

        public static string Str(object v)
        {
            return v == null ? null : Convert.ToString(v, CultureInfo.InvariantCulture);
        }

        public static DateTime? Date(object v)
        {
            return v == null ? (DateTime?)null : Convert.ToDateTime(v, CultureInfo.InvariantCulture);
        }
    }

    /// <summary>Kullanıcıya gösterilen sayı / boyut biçimleri (Türkçe).</summary>
    internal static class Fmt
    {
        public static readonly CultureInfo Tr = CultureInfo.GetCultureInfo("tr-TR");

        public static string Mb(double mb)
        {
            if (mb >= 1024 * 1024) return (mb / 1024 / 1024).ToString("0.##", Tr) + " TB";
            if (mb >= 1024) return (mb / 1024).ToString("0.#", Tr) + " GB";
            return mb.ToString("0", Tr) + " MB";
        }

        public static string Bytes(long bytes)
        {
            return Mb(bytes / 1024.0 / 1024.0);
        }

        public static string Num(long n)
        {
            return n.ToString("N0", Tr);
        }

        public static string Pct(double p)
        {
            return "%" + p.ToString("0", Tr);
        }

        public static string Date(DateTime? d)
        {
            return d == null ? "-" : d.Value.ToString("dd.MM.yyyy HH:mm", Tr);
        }

        public static string Ago(DateTime? d)
        {
            if (d == null) return "hiç";
            var span = DateTime.Now - d.Value;
            if (span.TotalDays >= 1) return (int)span.TotalDays + " gün önce";
            if (span.TotalHours >= 1) return (int)span.TotalHours + " saat önce";
            return Math.Max(0, (int)span.TotalMinutes) + " dk önce";
        }
    }
}
