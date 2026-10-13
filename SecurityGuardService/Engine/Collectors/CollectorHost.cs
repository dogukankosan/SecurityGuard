using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Security;
using SecurityGuardService.Core.Data;
using SecurityGuardService.Core.Logging;

namespace SecurityGuardService.Engine.Collectors
{
    /// <summary>
    /// Windows olay kanallarını izleyen toplayıcıların sahibi. Tüm kanalların olayları tek sırayla işlenir
    /// (sayaçlar ve eşleştirme tutarlı kalsın). Kanal açılamazsa diğerleri çalışmaya devam eder.
    /// </summary>
    internal sealed class CollectorHost : IDisposable
    {
        private const string Module = "Toplayıcı";

        private readonly List<ChannelWatcher> _watchers = new List<ChannelWatcher>();
        private readonly WindowsEventProcessor _processor;
        private readonly object _processLock = new object();

        public CollectorHost(GuardDatabase db, WindowsEventProcessor processor)
        {
            _processor = processor;

            _watchers.Add(new ChannelWatcher(WindowsEventProcessor.ChSecurity, last => QueryList(WindowsEventProcessor.ChSecurity,
                Select(last, 4625, 4720, 4722, 4723, 4724, 4725, 4728, 4732, 4756, 4698, 1102, 4719, 4688)), db, Sink));
            _watchers.Add(Simple(db, WindowsEventProcessor.ChSystem, 7045, 104));
            _watchers.Add(Simple(db, WindowsEventProcessor.ChApplication, 18456, 33205));
            _watchers.Add(Simple(db, WindowsEventProcessor.ChRdpCore, 140));
            _watchers.Add(Simple(db, WindowsEventProcessor.ChRdpSessions, 21, 23, 24, 25)); // RDP / konsol oturumu: açıldı, kapandı, koptu, yeniden bağlanıldı
            _watchers.Add(Simple(db, WindowsEventProcessor.ChFirewall, 2003, 2004, 2005, 2006, 2052, 2097, 2099));
            _watchers.Add(Simple(db, WindowsEventProcessor.ChDefender, 5001, 5007, 5010, 5012));
        }

        public IReadOnlyList<ChannelWatcher> Watchers { get { return _watchers; } }

        public void Start()
        {
            foreach (var w in _watchers) w.Start();
            AppLog.Info(Module, _watchers.Count + " olay kanalı izlemeye alındı.");
        }

        private void Sink(RawEvent e)
        {
            lock (_processLock)
                _processor.Process(e);
        }

        private ChannelWatcher Simple(GuardDatabase db, string channel, params int[] ids)
        {
            return new ChannelWatcher(channel, last => QueryList(channel, Select(last, ids)), db, Sink);
        }

        internal static string Select(long last, params int[] ids)
        {
            return "*[System[(" + string.Join(" or ", ids.Select(i => "EventID=" + i.ToString(CultureInfo.InvariantCulture))) +
                   ")" + RecordFilter(last) + "]]";
        }

        /// <summary>
        /// Okuma sorgusu için kayıt numarası filtresi. Canlı abonelikte (-1) YOK: abonelikte bu filtre olursa Windows olay teslim etmiyor.
        /// </summary>
        internal static string RecordFilter(long last)
        {
            return last < 0 ? "" : " and EventRecordID > " + N(last);
        }

        /// <summary>Birden fazla Select'i tek sorguda birleştirir (yapısal XML sorgu).</summary>
        internal static string QueryList(string channel, params string[] selects)
        {
            string path = SecurityElement.Escape(channel);
            return "<QueryList><Query Id=\"0\" Path=\"" + path + "\">" +
                   string.Concat(selects.Select(s => "<Select Path=\"" + path + "\">" + SecurityElement.Escape(s) + "</Select>")) +
                   "</Query></QueryList>";
        }

        private static string N(long v)
        {
            return v.ToString(CultureInfo.InvariantCulture);
        }

        public void Dispose()
        {
            foreach (var w in _watchers)
            {
                try
                {
                    w.Dispose();
                }
                catch (Exception ex)
                {
                    AppLog.Warn(Module, w.Channel + " kapatılamadı.", ex);
                }
            }
            try
            {
                _processor.Dispose(); // eşleşme bekleyen ağ girişi hataları sayılır
            }
            catch (Exception ex)
            {
                AppLog.Warn(Module, "Bekleyen giriş hataları işlenemedi.", ex);
            }
        }
    }
}
