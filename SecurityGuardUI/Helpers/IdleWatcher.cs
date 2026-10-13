using System;
using System.Windows.Forms;

namespace SecurityGuardUI.Helpers
{
    /// <summary>
    /// Kullanıcının klavye / fare hareketlerini izler.
    /// - Kullanıcı aktifken servis oturumu periyodik olarak canlı tutulur (<see cref="KeepAlive"/>).
    /// - Belirlenen süre hiç işlem yapılmazsa <see cref="IdleTimeout"/> tetiklenir (otomatik çıkış).
    /// </summary>
    internal sealed class IdleWatcher : IMessageFilter, IDisposable
    {
        private const int WM_KEYDOWN = 0x0100;
        private const int WM_SYSKEYDOWN = 0x0104;
        private const int WM_MOUSEMOVE = 0x0200;
        private const int WM_LBUTTONDOWN = 0x0201;
        private const int WM_RBUTTONDOWN = 0x0204;
        private const int WM_MOUSEWHEEL = 0x020A;

        private readonly TimeSpan _idleLimit;

        // En fazla 5 dk; kısa oturum süresinde (ör. 5 dk) servis oturumu dolmadan uzatılsın diye sürenin üçte biri
        private readonly TimeSpan _keepAliveInterval;
        private readonly Timer _timer = new Timer { Interval = 15000 };
        private DateTime _lastInput = DateTime.UtcNow;
        private DateTime _lastKeepAlive = DateTime.UtcNow;
        private bool _fired;

        public IdleWatcher(TimeSpan idleLimit)
        {
            _idleLimit = idleLimit;
            _keepAliveInterval = TimeSpan.FromTicks(Math.Min(TimeSpan.FromMinutes(5).Ticks, idleLimit.Ticks / 3));
            _timer.Tick += OnTick;
            Application.AddMessageFilter(this);
            _timer.Start();
        }

        /// <summary>Kullanıcı aktifse düzenli aralıkla tetiklenir (servis oturumu uzatılsın).</summary>
        public event EventHandler KeepAlive;

        /// <summary>Süre boyunca işlem yapılmadı (bir kez tetiklenir).</summary>
        public event EventHandler IdleTimeout;

        public bool PreFilterMessage(ref Message m)
        {
            switch (m.Msg)
            {
                case WM_KEYDOWN:
                case WM_SYSKEYDOWN:
                case WM_MOUSEMOVE:
                case WM_LBUTTONDOWN:
                case WM_RBUTTONDOWN:
                case WM_MOUSEWHEEL:
                    _lastInput = DateTime.UtcNow;
                    break;
            }
            return false; // mesajı engelleme, sadece izle
        }

        private void OnTick(object sender, EventArgs e)
        {
            DateTime now = DateTime.UtcNow;

            if (!_fired && now - _lastInput >= _idleLimit)
            {
                _fired = true;
                _timer.Stop();
                var idle = IdleTimeout;
                if (idle != null) idle(this, EventArgs.Empty);
                return;
            }

            if (now - _lastKeepAlive >= _keepAliveInterval && now - _lastInput < _keepAliveInterval)
            {
                _lastKeepAlive = now;
                var keep = KeepAlive;
                if (keep != null) keep(this, EventArgs.Empty);
            }
        }

        public void Dispose()
        {
            _timer.Stop();
            _timer.Dispose();
            Application.RemoveMessageFilter(this);
        }
    }
}
