using System;
using System.Diagnostics.Eventing.Reader;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading;
using SecurityGuardService.Core.Data;
using SecurityGuardService.Core.Logging;

namespace SecurityGuardService.Engine.Collectors
{
    /// <summary>
    /// Bir Windows olay kanalını izler.
    /// - Canlı: EventLogWatcher, sorguda SADECE olay ID'leri. (Abonelik sorgusunda EventRecordID filtresi olursa Windows
    ///   canlı olay teslim etmiyor; VM testinde Security / Firewall hiç gelmedi.)
    /// - Kaçırılanlar: açılışta kaldığı yerden sonrası EventLogReader ile okunur (okuma sorgusunda RecordID filtresi çalışır).
    ///   Bu sırada gelen canlı olaylar bekletilir, okuma bitince sırayla işlenir. Aynı kayıt iki kez işlenmez (RecordID kontrolü).
    /// - Kaldığı yer: kanal başına son işlenen EventRecordID (EventBookmarks). İlk açılışta geçmiş işlenmez.
    /// - Kanal açılamazsa (yetki yok, kanal yok, log servisi kapalı) servisi düşürmez: loglar ve periyodik tekrar dener.
    /// - Olaylar sırayla <see cref="EventSink"/>'e verilir; kaldığı yer işlendikten sonra kaydedilir.
    /// </summary>
    internal sealed class ChannelWatcher : IDisposable
    {
        private const string Module = "Toplayıcı";
        private static readonly TimeSpan RetryDelay = TimeSpan.FromMinutes(1);

        private readonly string _channel;
        private readonly Func<long, string> _query;
        private readonly GuardDatabase _db;
        private readonly Action<RawEvent> _sink;
        private readonly object _sync = new object();
        private readonly ManualResetEventSlim _stopping = new ManualResetEventSlim(false);

        private readonly object _process = new object();
        private readonly List<KeyValuePair<long, string>> _pending = new List<KeyValuePair<long, string>>();
        private bool _catchingUp;
        private EventLogWatcher _watcher;
        private Thread _thread;
        private long _lastRecordId;
        private string _lastError;

        /// <param name="query">Kayıt numarası → XPath sorgusu. -1 = canlı abonelik (numara filtresi YOK); ≥0 = bu numaradan büyükleri oku.</param>
        public ChannelWatcher(string channel, Func<long, string> query, GuardDatabase db, Action<RawEvent> sink)
        {
            _channel = channel;
            _query = query;
            _db = db;
            _sink = sink;
        }

        public string Channel { get { return _channel; } }

        /// <summary>İzleme şu an çalışıyor mu (sağlık ekranı için).</summary>
        public bool IsRunning { get; private set; }

        /// <summary>Son bağlanma hatası (çalışıyorsa null).</summary>
        public string LastError { get { return _lastError; } }

        public void Start()
        {
            _thread = new Thread(Loop) { IsBackground = true, Name = "Watch " + _channel };
            _thread.Start();
        }

        private void Loop()
        {
            while (!_stopping.IsSet)
            {
                if (!IsRunning)
                {
                    try
                    {
                        Subscribe();
                        if (_lastError != null) AppLog.Info(Module, _channel + " izleniyor (tekrar bağlandı).");
                        _lastError = null;
                    }
                    catch (Exception ex)
                    {
                        string msg = Describe(ex);
                        if (msg != _lastError) AppLog.Warn(Module, _channel + " izlenemiyor: " + msg + " Dakikada bir tekrar denenecek.");
                        _lastError = msg;
                    }
                }
                _stopping.Wait(RetryDelay);
            }
        }

        private void Subscribe()
        {
            long last = LoadBookmark();
            if (last < 0)
            {
                last = LatestRecordId(); // ilk kez: şimdiden itibaren
                SaveBookmark(last);
            }
            _lastRecordId = last;

            lock (_sync)
            {
                _catchingUp = true;
                _pending.Clear();
            }

            // 1) Canlı abonelik (numara filtresi yok): okuma sürerken gelenler bekletilir
            var watcher = new EventLogWatcher(Query(_query(-1)), null, false);
            watcher.EventRecordWritten += OnEvent;
            try
            {
                watcher.Enabled = true;

                // 2) Kaldığı yerden sonrası (servis kapalıyken oluşanlar), 3) bekleyen canlılar sırayla
                int caught = 0;
                lock (_process)
                {
                    using (var reader = new EventLogReader(Query(_query(last))))
                    {
                        EventRecord record;
                        while (!_stopping.IsSet && (record = reader.ReadEvent()) != null)
                        {
                            using (record)
                            {
                                long id = record.RecordId ?? 0;
                                if (id > _lastRecordId && Process(id, record.ToXml())) caught++;
                            }
                        }
                    }

                    List<KeyValuePair<long, string>> pending;
                    lock (_sync)
                    {
                        pending = new List<KeyValuePair<long, string>>(_pending);
                        _pending.Clear();
                        _catchingUp = false;
                    }
                    foreach (var p in pending.OrderBy(p => p.Key))
                        Process(p.Key, p.Value);
                }

                lock (_sync)
                {
                    if (_stopping.IsSet)
                    {
                        watcher.Dispose();
                        return;
                    }
                    _watcher = watcher;
                    IsRunning = true;
                }
                AppLog.Info(Module, _channel + " izleniyor (kayıt " + last + " sonrasından" +
                    (caught > 0 ? ", kapalıyken oluşan " + caught + " olay işlendi" : "") + ").");
            }
            catch
            {
                watcher.Dispose();
                throw;
            }
        }

        /// <summary>Yapısal XML sorguda (QueryList) kanal yolu sorgunun içindedir; ayrıca verilirse Windows reddeder.</summary>
        private EventLogQuery Query(string text)
        {
            return text.StartsWith("<QueryList", StringComparison.Ordinal)
                ? new EventLogQuery(null, PathType.LogName, text)
                : new EventLogQuery(_channel, PathType.LogName, text);
        }

        private void OnEvent(object sender, EventRecordWrittenEventArgs e)
        {
            if (e.EventException != null)
            {
                if (_stopping.IsSet) return; // servis duruyor: abonelik bilerek kapatıldı, hata değil

                // Windows kapanırken / olay log servisi yeniden başlarken abonelik iptal edilir: bilgi, uyarı değil
                if (e.EventException is OperationCanceledException)
                    AppLog.Info(Module, _channel + " izlemesi Windows tarafından kapatıldı (olay log servisi duruyor olabilir), yeniden bağlanılacak.");
                else
                    AppLog.Warn(Module, _channel + " izleme hatası: " + Describe(e.EventException));

                // Aboneliği bırak, döngü yeniden bağlanır (kaldığı yerden; arada kaçan olay olmaz)
                DropWatcher();
                return;
            }

            using (var record = e.EventRecord)
            {
                if (record == null) return;
                long id = record.RecordId ?? 0;
                string xml;
                try
                {
                    xml = record.ToXml();
                }
                catch (Exception ex)
                {
                    AppLog.Error(Module, _channel + " olayı okunamadı (kayıt " + id + ").", ex);
                    return;
                }

                lock (_sync)
                {
                    if (_catchingUp)
                    {
                        _pending.Add(new KeyValuePair<long, string>(id, xml));
                        return;
                    }
                }
                lock (_process)
                    Process(id, xml);
            }
        }

        /// <returns>true = işlendi (yeni kayıt).</returns>
        private bool Process(long id, string xml)
        {
            if (id != 0 && id <= _lastRecordId) return false; // zaten işlenmiş
            try
            {
                _sink(RawEvent.FromXml(_channel, xml));
            }
            catch (Exception ex)
            {
                // Tek bir bozuk kayıt izlemeyi durdurmaz
                AppLog.Error(Module, _channel + " olayı işlenemedi (kayıt " + id + ").", ex);
            }
            if (id > _lastRecordId)
            {
                _lastRecordId = id;
                SaveBookmark(id);
            }
            return true;
        }

        private void DropWatcher()
        {
            EventLogWatcher w;
            lock (_sync)
            {
                w = _watcher;
                _watcher = null;
                IsRunning = false;
            }
            if (w != null)
            {
                ThreadPool.QueueUserWorkItem(_ =>
                {
                    try { w.Enabled = false; w.Dispose(); } catch { /* kapanış hatası önemsiz */ }
                });
            }
        }

        private long LatestRecordId()
        {
            var q = new EventLogQuery(_channel, PathType.LogName, "*") { ReverseDirection = true };
            using (var reader = new EventLogReader(q))
            using (var last = reader.ReadEvent())
                return last == null ? 0 : last.RecordId ?? 0;
        }

        private long LoadBookmark()
        {
            string value = _db.Read(c => c.Scalar<string>("SELECT Bookmark FROM EventBookmarks WHERE Channel = @c", ("@c", _channel)));
            long id;
            return value != null && long.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out id) ? id : -1;
        }

        private void SaveBookmark(long recordId)
        {
            string text = recordId.ToString(CultureInfo.InvariantCulture);
            _db.Writer.Enqueue(c => c.Execute(
                @"INSERT INTO EventBookmarks (Channel, Bookmark, UpdatedUtc) VALUES (@c, @b, @t)
                  ON CONFLICT(Channel) DO UPDATE SET Bookmark = excluded.Bookmark, UpdatedUtc = excluded.UpdatedUtc",
                ("@c", _channel), ("@b", text), ("@t", DbTime.Now())));
        }

        private static string Describe(Exception ex)
        {
            if (ex is EventLogNotFoundException) return "kanal bu sunucuda yok.";
            if (ex is UnauthorizedAccessException) return "okuma yetkisi yok (servis SYSTEM / yönetici olarak çalışmalı).";
            return ex.Message;
        }

        public void Dispose()
        {
            _stopping.Set();
            EventLogWatcher w;
            lock (_sync)
            {
                w = _watcher;
                _watcher = null;
                IsRunning = false;
            }
            try
            {
                if (w != null)
                {
                    w.Enabled = false;
                    w.Dispose();
                }
            }
            catch (Exception ex)
            {
                AppLog.Warn(Module, _channel + " izleme kapatılamadı.", ex);
            }
            if (_thread != null) _thread.Join(TimeSpan.FromSeconds(5));
        }
    }
}
