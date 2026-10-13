using SecurityGuardService.Core.Logging;

namespace SecurityGuardService.Core.Data
{
    /// <summary>UI'den yapılan her işlemin kaydı (AuditTrail tablosu). Yazma kuyruğa atılır, çağıranı bekletmez.</summary>
    internal static class AuditTrail
    {
        public static void Record(GuardDatabase db, string userName, string action, string detail = null)
        {
            bool queued = db.Writer.Enqueue(conn => conn.Execute(
                "INSERT INTO AuditTrail (TimeUtc, UserName, Action, Detail) VALUES (@t, @u, @a, @d)",
                ("@t", DbTime.Now()), ("@u", userName), ("@a", action), ("@d", detail)));

            if (!queued)
                AppLog.Error("İşlemKaydı", "İşlem kaydı kuyruğa alınamadı: " + action + " (" + userName + ")");
        }
    }
}
