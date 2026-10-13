using System;
using System.Collections.Concurrent;
using System.Security.Cryptography;
using System.Text;
using System.Threading;

namespace SecurityGuardService.Ipc
{
    /// <summary>
    /// UI oturumları (bellekte). Girişte rastgele token üretilir; her istekte süre uzar,
    /// işlem yapılmazsa oturumun boşta kalma süresi (girişteki ayar) sonunda düşer. Servis yeniden başlarsa tüm oturumlar düşer
    /// (UI tekrar giriş ister).
    /// </summary>
    internal sealed class SessionManager
    {
        /// <summary>Ayarlanabilecek en uzun boşta kalma süresi (pipe bağlantı zaman aşımı buna göre).</summary>
        public static readonly TimeSpan MaxIdleTimeout = TimeSpan.FromMinutes(240);

        private readonly ConcurrentDictionary<string, Session> _sessions =
            new ConcurrentDictionary<string, Session>(StringComparer.Ordinal);

        public Session Create(string userName, TimeSpan idleTimeout)
        {
            RemoveExpired();
            if (idleTimeout > MaxIdleTimeout) idleTimeout = MaxIdleTimeout;
            var session = new Session(NewToken(), userName, idleTimeout);
            _sessions[session.Token] = session;
            return session;
        }

        /// <returns>Geçerli oturum (süresi uzatılır) veya null.</returns>
        public Session Validate(string token)
        {
            if (string.IsNullOrEmpty(token)) return null;

            Session s;
            if (!_sessions.TryGetValue(token, out s)) return null;

            if (s.IdleFor > s.IdleTimeout)
            {
                _sessions.TryRemove(token, out s);
                return null;
            }

            s.Touch();
            return s;
        }

        public void Remove(string token)
        {
            Session s;
            if (!string.IsNullOrEmpty(token)) _sessions.TryRemove(token, out s);
        }

        private void RemoveExpired()
        {
            foreach (var pair in _sessions)
            {
                Session s;
                if (pair.Value.IdleFor > pair.Value.IdleTimeout) _sessions.TryRemove(pair.Key, out s);
            }
        }

        private static string NewToken()
        {
            byte[] bytes = new byte[32];
            using (var rng = RandomNumberGenerator.Create())
                rng.GetBytes(bytes);

            var sb = new StringBuilder(64);
            foreach (byte b in bytes) sb.Append(b.ToString("x2"));
            return sb.ToString();
        }
    }

    internal sealed class Session
    {
        private long _lastUsedTicks;

        public Session(string token, string userName, TimeSpan idleTimeout)
        {
            Token = token;
            UserName = userName;
            IdleTimeout = idleTimeout;
            Touch();
        }

        public string Token { get; private set; }

        public string UserName { get; private set; }

        /// <summary>İşlem yapılmazsa oturumun düşeceği süre (giriş anındaki ayar).</summary>
        public TimeSpan IdleTimeout { get; private set; }

        public TimeSpan IdleFor
        {
            get { return TimeSpan.FromTicks(DateTime.UtcNow.Ticks - Interlocked.Read(ref _lastUsedTicks)); }
        }

        public void Touch()
        {
            Interlocked.Exchange(ref _lastUsedTicks, DateTime.UtcNow.Ticks);
        }
    }
}
