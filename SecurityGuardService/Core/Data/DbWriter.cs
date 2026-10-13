using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Data.SQLite;
using System.Threading;
using System.Threading.Tasks;
using SecurityGuardService.Core.Logging;

namespace SecurityGuardService.Core.Data
{
    /// <summary>
    /// Tüm DB yazmaları bu tek thread'den yapılır (SQLite'ta yazıcı kilidi çakışması olmaz).
    /// İşler kuyruğa atılır, toplu halde tek transaction'da yazılır.
    /// Her iş kendi SAVEPOINT'inde çalışır: bir işin hatası diğerlerini geri almaz.
    /// Bir iş "tamamlandı" sayılması için transaction'ın commit edilmiş olması gerekir.
    /// </summary>
    internal sealed class DbWriter : IDisposable
    {
        private const string Module = "DbWriter";
        private const int Capacity = 50000;
        private const int MaxBatch = 500;
        private static readonly TimeSpan[] ReconnectDelays =
        {
            TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(5), TimeSpan.FromSeconds(30), TimeSpan.FromMinutes(2)
        };

        private readonly Func<SQLiteConnection> _connect;
        private readonly BlockingCollection<WorkItem> _queue =
            new BlockingCollection<WorkItem>(new ConcurrentQueue<WorkItem>(), Capacity);
        private readonly ManualResetEventSlim _stopping = new ManualResetEventSlim(false);
        private Thread _thread;
        private SQLiteConnection _conn;
        private int _connectFailures;

        public DbWriter(Func<SQLiteConnection> connect)
        {
            _connect = connect;
        }

        /// <summary>Kuyrukta bekleyen iş sayısı (izleme için).</summary>
        public int Pending { get { return _queue.Count; } }

        public void Start()
        {
            _thread = new Thread(Loop) { IsBackground = true, Name = "DbWriter" };
            _thread.Start();
        }

        /// <summary>Sonucu beklenmeyen yazma (log, sayaç vb.). Kuyruk doluysa false döner.</summary>
        public bool Enqueue(Action<SQLiteConnection> work)
        {
            return TryAdd(new WorkItem(work, null, null));
        }

        /// <summary>Commit edilince tamamlanan yazma.</summary>
        public Task EnqueueAsync(Action<SQLiteConnection> work)
        {
            return EnqueueAsync<object>(c => { work(c); return null; });
        }

        /// <summary>Commit edilince sonucu dönen yazma (ör. eklenen kaydın Id'si).</summary>
        public Task<T> EnqueueAsync<T>(Func<SQLiteConnection, T> work)
        {
            var tcs = new TaskCompletionSource<T>(TaskCreationOptions.RunContinuationsAsynchronously);
            T result = default(T);
            var item = new WorkItem(
                c => { result = work(c); },
                () => tcs.TrySetResult(result),
                ex => tcs.TrySetException(ex));

            if (!TryAdd(item))
                tcs.TrySetException(new InvalidOperationException("Veritabanı yazma kuyruğu dolu veya kapalı."));
            return tcs.Task;
        }

        private bool TryAdd(WorkItem item)
        {
            try
            {
                return _queue.TryAdd(item);
            }
            catch (InvalidOperationException)
            {
                return false; // kuyruk kapandı (servis duruyor)
            }
        }

        private void Loop()
        {
            var batch = new List<WorkItem>(MaxBatch);

            while (true)
            {
                WorkItem first;
                try
                {
                    if (!_queue.TryTake(out first, 1000))
                    {
                        if (_queue.IsCompleted) break;
                        continue;
                    }
                }
                catch
                {
                    break;
                }

                batch.Add(first);
                WorkItem more;
                while (batch.Count < MaxBatch && _queue.TryTake(out more))
                    batch.Add(more);

                try
                {
                    WriteBatch(batch);
                }
                catch (Exception ex)
                {
                    // WriteBatch kendi hatalarını yakalar; buraya düşmesi beklenmez. Yine de thread asla ölmez.
                    AppLog.Error(Module, "Beklenmeyen yazma hatası.", ex);
                    foreach (var item in batch) item.Fail(ex);
                }
                batch.Clear();
            }

            CloseConnection();
        }

        private void WriteBatch(List<WorkItem> batch)
        {
            var conn = EnsureConnection();
            if (conn == null)
            {
                var ex = new InvalidOperationException("Veritabanına bağlanılamadı.");
                foreach (var item in batch) item.Fail(ex);
                return;
            }

            var done = new List<WorkItem>(batch.Count);
            try
            {
                using (var tx = conn.BeginTransaction())
                {
                    foreach (var item in batch)
                    {
                        conn.Execute("SAVEPOINT w");
                        try
                        {
                            item.Execute(conn);
                            conn.Execute("RELEASE w");
                            done.Add(item);
                        }
                        catch (Exception ex)
                        {
                            conn.Execute("ROLLBACK TO w");
                            conn.Execute("RELEASE w");
                            AppLog.Error(Module, "Yazma işi başarısız, sadece bu iş geri alındı.", ex);
                            item.Fail(ex);
                        }
                    }
                    tx.Commit();
                }
            }
            catch (Exception ex)
            {
                // Commit / bağlantı hatası: bu partide yazılan hiçbir şey kalıcı olmadı.
                // Partideki tüm işler başarısız sayılır (zaten sonuçlananlar etkilenmez).
                AppLog.Error(Module, "Toplu yazma commit edilemedi, bağlantı yenilenecek.", ex);
                foreach (var item in batch) item.Fail(ex);
                CloseConnection();
                return;
            }

            foreach (var item in done) item.Complete();
        }

        private SQLiteConnection EnsureConnection()
        {
            if (_conn != null) return _conn;

            while (!_stopping.IsSet)
            {
                try
                {
                    _conn = _connect();
                    if (_connectFailures > 0)
                        AppLog.Info(Module, "Veritabanı bağlantısı yeniden kuruldu.");
                    _connectFailures = 0;
                    return _conn;
                }
                catch (Exception ex)
                {
                    var delay = ReconnectDelays[Math.Min(_connectFailures, ReconnectDelays.Length - 1)];
                    _connectFailures++;
                    AppLog.Error(Module, "Veritabanına bağlanılamadı, " + delay.TotalSeconds + " sn sonra tekrar denenecek.", ex);
                    _stopping.Wait(delay);
                }
            }
            return null;
        }

        private void CloseConnection()
        {
            try
            {
                if (_conn != null) _conn.Dispose();
            }
            catch
            {
                // kapanış hatası önemsiz
            }
            _conn = null;
        }

        /// <summary>Kuyruktaki işleri yazar ve durur.</summary>
        public void Dispose()
        {
            try
            {
                _queue.CompleteAdding();
                if (_thread != null && !_thread.Join(TimeSpan.FromSeconds(10)))
                {
                    // Bağlantı bekleniyorsa bekleme döngüsünü kır
                    _stopping.Set();
                    _thread.Join(TimeSpan.FromSeconds(5));
                }
            }
            catch (Exception ex)
            {
                AppLog.Error(Module, "Yazıcı kapatılırken hata.", ex);
            }
        }

        private sealed class WorkItem
        {
            private readonly Action<SQLiteConnection> _execute;
            private readonly Action _complete;
            private readonly Action<Exception> _fail;
            private int _finished; // Complete / Fail sadece bir kez çalışır

            public WorkItem(Action<SQLiteConnection> execute, Action complete, Action<Exception> fail)
            {
                _execute = execute;
                _complete = complete;
                _fail = fail;
            }

            public void Execute(SQLiteConnection conn) { _execute(conn); }

            public void Complete()
            {
                if (Interlocked.Exchange(ref _finished, 1) != 0) return;
                try { if (_complete != null) _complete(); } catch { }
            }

            public void Fail(Exception ex)
            {
                if (Interlocked.Exchange(ref _finished, 1) != 0) return;
                try { if (_fail != null) _fail(ex); } catch { }
            }
        }
    }
}
