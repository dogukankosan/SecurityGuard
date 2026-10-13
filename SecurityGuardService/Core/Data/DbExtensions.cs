using System;
using System.Data.SQLite;

namespace SecurityGuardService.Core.Data
{
    /// <summary>
    /// Kısa SQLite yardımcıları. Parametreler her zaman isimli verilir (SQL injection yok):
    /// conn.Execute("UPDATE X SET A = @a WHERE Id = @id", ("@a", 5), ("@id", 1));
    /// </summary>
    internal static class DbExtensions
    {
        public static int Execute(this SQLiteConnection conn, string sql, params (string Name, object Value)[] args)
        {
            using (var cmd = Create(conn, sql, args))
                return cmd.ExecuteNonQuery();
        }

        public static T Scalar<T>(this SQLiteConnection conn, string sql, params (string Name, object Value)[] args)
        {
            using (var cmd = Create(conn, sql, args))
            {
                object value = cmd.ExecuteScalar();
                if (value == null || value is DBNull) return default(T);
                return (T)Convert.ChangeType(value, Nullable.GetUnderlyingType(typeof(T)) ?? typeof(T));
            }
        }

        public static SQLiteCommand Create(this SQLiteConnection conn, string sql, params (string Name, object Value)[] args)
        {
            var cmd = conn.CreateCommand();
            cmd.CommandText = sql;
            if (args != null)
                foreach (var a in args)
                    cmd.Parameters.AddWithValue(a.Name, a.Value ?? DBNull.Value);
            return cmd;
        }
    }
}
