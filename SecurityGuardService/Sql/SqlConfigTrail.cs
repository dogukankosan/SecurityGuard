using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;

namespace SecurityGuardService.Sql
{
    /// <summary>SQL izlemesinin (Audit) gördüğü bir sunucu ayarı değişikliği: kim, nereden, hangi komutla.</summary>
    internal sealed class SqlConfigActor
    {
        public string Instance, Who, Ip, Application, Host, Statement;
        public DateTime TimeUtc;
    }

    /// <summary>
    /// Sunucu ayarı değişikliklerinde "kim yaptı" bilgisi.
    /// sp_configure ile yapılan değişiklikte Audit kaydı çoğu zaman sadece "RECONFIGURE" der: hangi ayarın değiştiği yazmaz.
    /// Hangi ayarın değiştiğini periyodik karşılaştırma (SqlBaseline) bulur ama kimin yaptığını bilmez. İkisi burada birleşir:
    /// Audit kaydı gelince yapan kişi not edilir ve karşılaştırma hemen tetiklenir; karşılaştırma olayı yazarken yapanı buradan alır.
    /// </summary>
    internal static class SqlConfigTrail
    {
        private static readonly object Gate = new object();
        private static readonly List<SqlConfigActor> Recent = new List<SqlConfigActor>();
        internal static TimeSpan TriggerDelay = TimeSpan.FromSeconds(3);
        private static readonly Timer Trigger = new Timer(_ =>
        {
            var changed = Changed;
            if (changed != null) changed();
        }, null, Timeout.InfiniteTimeSpan, Timeout.InfiniteTimeSpan);

        /// <summary>Ayar değişikliği görüldü: karşılaştırmayı beklemeden çalıştır (SqlMonitor.CheckSoon).</summary>
        public static Action Changed;

        public static void Note(SqlConfigActor actor)
        {
            lock (Gate)
            {
                Recent.Add(actor);
                if (Recent.Count > 50) Recent.RemoveRange(0, Recent.Count - 50);
            }
            // Art arda gelen kayıtlar (sp_configure genelde iki RECONFIGURE'dur: "show advanced options" + asıl ayar) tek karşılaştırmada
            // toplanır: son kayıttan 3 sn sonra bir kez tetiklenir. Hiçbir kayıt düşürülmez (ilk kayıtta hemen bakılırsa asıl ayar henüz değişmemiş olur).
            Trigger.Change(TriggerDelay, Timeout.InfiniteTimeSpan);
        }

        /// <summary>Bu instance'ta son <paramref name="window"/> içinde ayar değiştiren en son kişi; yoksa null.</summary>
        public static SqlConfigActor Latest(string instance, TimeSpan window)
        {
            DateTime since = DateTime.UtcNow - window;
            lock (Gate)
                return Recent.Where(a => a.TimeUtc >= since && string.Equals(a.Instance, instance, StringComparison.OrdinalIgnoreCase))
                             .OrderByDescending(a => a.TimeUtc).FirstOrDefault();
        }
    }
}
