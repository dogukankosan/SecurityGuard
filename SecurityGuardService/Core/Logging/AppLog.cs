using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Threading;
using SecurityGuard.Shared.Enums;

namespace SecurityGuardService.Core.Logging
{
    /// <summary>
    /// Uygulama logu: günlük düz metin dosya (logs\app_yyyyMMdd.log).
    /// Tek bir arka plan thread'i yazar; çağıran taraf asla beklemez ve log hatası asla dışarı sızmaz.
    /// Aynı mesaj art arda tekrarlanırsa tek satıra sıkıştırılır.
    /// </summary>
    internal static class AppLog
    {
        private const int QueueCapacity = 20000;
        private const int MaxBatch = 500;
        private static readonly TimeSpan RepeatWindow = TimeSpan.FromSeconds(60);
        private static readonly Encoding Utf8NoBom = new UTF8Encoding(false);

        // Kuyruk baştan oluşturulur: Start'tan önce yazılan loglar da kaybolmaz.
        private static readonly BlockingCollection<LogEntry> Queue =
            new BlockingCollection<LogEntry>(new ConcurrentQueue<LogEntry>(), QueueCapacity);

        private static Thread _worker;
        private static string _folder;
        private static volatile int _minLevel = (int)LogLevel.Info;
        private static long _dropped;

        // Tekrar sıkıştırma durumu (sadece worker thread kullanır)
        private static string _lastKey;
        private static DateTimeOffset _lastFirstTime;
        private static int _repeatCount;

        public static LogLevel MinLevel
        {
            get { return (LogLevel)_minLevel; }
            set { _minLevel = (int)value; }
        }

        /// <summary>
        /// Error / Fatal yazıldığında (modül, mesaj). Dinleyen hızlı olmalı ve hata fırlatmamalı (yazan iş parçacığında çalışır).
        /// Sistem nöbeti "art arda hata veren modül" tespiti için kullanır.
        /// </summary>
        public static event Action<string, string> ErrorWritten;

        /// <summary>Konsol modunda (F5) logları ekrana da yazar.</summary>
        public static bool EchoToConsole { get; set; }

        public static void Start(string folder)
        {
            if (_worker != null) return;
            _folder = folder;
            _worker = new Thread(WorkerLoop) { IsBackground = true, Name = "AppLog" };
            _worker.Start();
        }

        /// <summary>Kuyruktaki logları yazıp durur. Servis kapanırken en son çağrılır.</summary>
        public static void Stop(TimeSpan timeout)
        {
            try
            {
                Queue.CompleteAdding();
                if (_worker != null) _worker.Join(timeout);
            }
            catch
            {
                // Log kapatma hatası servisi etkilemez
            }
        }

        public static void Debug(string module, string message) { Write(LogLevel.Debug, module, message, null); }
        public static void Info(string module, string message) { Write(LogLevel.Info, module, message, null); }
        public static void Warn(string module, string message, Exception ex = null) { Write(LogLevel.Warn, module, message, ex); }
        public static void Error(string module, string message, Exception ex = null) { Write(LogLevel.Error, module, message, ex); }
        public static void Fatal(string module, string message, Exception ex = null) { Write(LogLevel.Fatal, module, message, ex); }

        private static void Write(LogLevel level, string module, string message, Exception ex)
        {
            try
            {
                if (level >= LogLevel.Error)
                {
                    var handler = ErrorWritten;
                    if (handler != null)
                    {
                        try { handler(module, message + (ex != null ? " (" + ex.Message + ")" : "")); }
                        catch { }
                    }
                }
                if ((int)level < _minLevel) return;
                if (!Queue.TryAdd(new LogEntry(DateTimeOffset.Now, level, module, message, ex)))
                    Interlocked.Increment(ref _dropped);
            }
            catch
            {
                // Kuyruk kapandıysa (servis durdu) veya başka bir hata: log yüzünden hiçbir şey patlamaz
                Interlocked.Increment(ref _dropped);
            }
        }

        private static void WorkerLoop()
        {
            var lines = new List<KeyValuePair<DateTimeOffset, string>>();

            while (true)
            {
                LogEntry entry;
                bool got;
                try
                {
                    got = Queue.TryTake(out entry, 1000);
                }
                catch
                {
                    break;
                }

                if (got)
                {
                    Format(entry, lines);
                    LogEntry more;
                    for (int i = 0; i < MaxBatch && Queue.TryTake(out more); i++)
                        Format(more, lines);
                }
                else
                {
                    FlushRepeatIfExpired(lines, DateTimeOffset.Now);
                    if (Queue.IsCompleted) break;
                }

                long dropped = Interlocked.Exchange(ref _dropped, 0);
                if (dropped > 0)
                    lines.Add(Line(DateTimeOffset.Now, LogLevel.Warn, "AppLog", dropped + " log mesajı kuyruk dolu olduğu için atlandı."));

                WriteLines(lines);
            }

            // Kapanış: bekleyen tekrar sayısını da yaz
            FlushRepeat(lines, DateTimeOffset.Now);
            WriteLines(lines);
        }

        private static void Format(LogEntry e, List<KeyValuePair<DateTimeOffset, string>> lines)
        {
            string key = ((int)e.Level) + "|" + e.Module + "|" + e.Message + "|" + (e.Exception == null ? "" : e.Exception.GetType().FullName);

            if (key == _lastKey && e.Time - _lastFirstTime < RepeatWindow)
            {
                _repeatCount++;
                return;
            }

            FlushRepeat(lines, e.Time);
            _lastKey = key;
            _lastFirstTime = e.Time;

            string text = e.Message;
            if (e.Exception != null)
                text += Environment.NewLine + "    " + e.Exception.ToString().Replace(Environment.NewLine, Environment.NewLine + "    ");

            lines.Add(Line(e.Time, e.Level, e.Module, text));
        }

        private static void FlushRepeatIfExpired(List<KeyValuePair<DateTimeOffset, string>> lines, DateTimeOffset now)
        {
            if (_repeatCount > 0 && now - _lastFirstTime >= RepeatWindow)
            {
                FlushRepeat(lines, now);
                _lastKey = null;
            }
        }

        private static void FlushRepeat(List<KeyValuePair<DateTimeOffset, string>> lines, DateTimeOffset now)
        {
            if (_repeatCount <= 0) return;
            lines.Add(Line(now, LogLevel.Info, "AppLog", "Önceki mesaj " + _repeatCount + " kez daha tekrarlandı."));
            _repeatCount = 0;
        }

        private static KeyValuePair<DateTimeOffset, string> Line(DateTimeOffset time, LogLevel level, string module, string text)
        {
            string line = time.ToString("yyyy-MM-dd HH:mm:ss.fff zzz") + " [" + level.ToString().ToUpperInvariant().PadRight(5) + "] [" + module + "] " + text;
            return new KeyValuePair<DateTimeOffset, string>(time, line);
        }

        private static void WriteLines(List<KeyValuePair<DateTimeOffset, string>> lines)
        {
            if (lines.Count == 0) return;
            try
            {
                if (EchoToConsole)
                    foreach (var l in lines) Console.WriteLine(l.Value);

                if (_folder != null)
                {
                    // Satırlar gün değişiminde doğru dosyaya gitsin diye tarihe göre gruplanır
                    StreamWriter writer = null;
                    string currentPath = null;
                    try
                    {
                        foreach (var l in lines)
                        {
                            string path = Path.Combine(_folder, "app_" + l.Key.ToString("yyyyMMdd") + ".log");
                            if (path != currentPath)
                            {
                                if (writer != null) writer.Dispose();
                                writer = new StreamWriter(path, true, Utf8NoBom);
                                currentPath = path;
                            }
                            writer.WriteLine(l.Value);
                        }
                    }
                    finally
                    {
                        if (writer != null) writer.Dispose();
                    }
                }
            }
            catch
            {
                // Disk dolu, izin yok vb.: log yazılamazsa servis yine de çalışmaya devam eder
            }
            finally
            {
                lines.Clear();
            }
        }

        private sealed class LogEntry
        {
            public readonly DateTimeOffset Time;
            public readonly LogLevel Level;
            public readonly string Module;
            public readonly string Message;
            public readonly Exception Exception;

            public LogEntry(DateTimeOffset time, LogLevel level, string module, string message, Exception exception)
            {
                Time = time;
                Level = level;
                Module = module ?? "";
                Message = message ?? "";
                Exception = exception;
            }
        }
    }
}
