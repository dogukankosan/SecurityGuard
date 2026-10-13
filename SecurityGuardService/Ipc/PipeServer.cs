using System;
using System.IO;
using System.IO.Pipes;
using System.Security.AccessControl;
using System.Security.Principal;
using System.Threading;
using System.Threading.Tasks;
using SecurityGuard.Shared;
using SecurityGuard.Shared.Contracts;
using SecurityGuardService.Core.Logging;

namespace SecurityGuardService.Ipc
{
    /// <summary>
    /// UI ile konuşan named pipe sunucusu (\\.\pipe\SecurityGuard.Pipe).
    ///
    /// Erişim: sadece yerel Administrators ve SYSTEM. Ağdan (NETWORK) erişim açıkça yasak;
    /// başka bir bilgisayardan bağlanılamaz. Birkaç dinleyici paralel çalışır, bir bağlantının
    /// hatası diğerlerini ve servisi etkilemez. Uzun süre boşta kalan bağlantı kapatılır.
    /// </summary>
    internal sealed class PipeServer : IDisposable
    {
        private const string Module = "Pipe";
        private const int ListenerCount = 4;
        private const int BufferSize = 64 * 1024;

        /// <summary>Bağlı ama istek göndermeyen istemcinin kapatılma süresi (oturum süresinden biraz uzun).</summary>
        private static readonly TimeSpan ConnectionIdleTimeout = SessionManager.MaxIdleTimeout + TimeSpan.FromMinutes(5);

        private readonly CommandRouter _router;
        private readonly CancellationTokenSource _cts = new CancellationTokenSource();
        private Task[] _listeners;

        public PipeServer(CommandRouter router)
        {
            _router = router;
        }

        public void Start()
        {
            _listeners = new Task[ListenerCount];
            for (int i = 0; i < ListenerCount; i++)
                _listeners[i] = Task.Run(() => ListenLoopAsync(_cts.Token));
            AppLog.Info(Module, "Yönetim kanalı dinleniyor: " + GuardConstants.PipeName);
        }

        private async Task ListenLoopAsync(CancellationToken token)
        {
            int failures = 0;
            while (!token.IsCancellationRequested)
            {
                NamedPipeServerStream pipe = null;
                try
                {
                    pipe = CreatePipe();
                    var current = pipe;
                    // .NET Framework'te pipe beklemeleri iptal sinyalini her zaman dinlemez: durdurulunca pipe kapatılır
                    using (token.Register(() => current.Dispose()))
                    {
                        await pipe.WaitForConnectionAsync(token).ConfigureAwait(false);
                        failures = 0;
                        await HandleClientAsync(pipe, token).ConfigureAwait(false);
                    }
                }
                catch (Exception) when (token.IsCancellationRequested)
                {
                    break; // servis duruyor
                }
                catch (Exception ex)
                {
                    failures++;
                    AppLog.Error(Module, "Dinleyici hatası (art arda " + failures + ").", ex);
                    try
                    {
                        await Task.Delay(TimeSpan.FromSeconds(Math.Min(30, failures * 2)), token).ConfigureAwait(false);
                    }
                    catch (OperationCanceledException)
                    {
                        break;
                    }
                }
                finally
                {
                    if (pipe != null) pipe.Dispose();
                }
            }
        }

        private async Task HandleClientAsync(NamedPipeServerStream pipe, CancellationToken token)
        {
            try
            {
                while (pipe.IsConnected && !token.IsCancellationRequested)
                {
                    PipeRequest request;
                    bool idleExpired = false;
                    try
                    {
                        using (var idle = new CancellationTokenSource(ConnectionIdleTimeout))
                        using (idle.Token.Register(() => { idleExpired = true; pipe.Dispose(); }))
                        {
                            request = await PipeProtocol.ReadAsync<PipeRequest>(pipe, token).ConfigureAwait(false);
                        }
                    }
                    catch (Exception) when (idleExpired && !token.IsCancellationRequested)
                    {
                        AppLog.Debug(Module, "Boşta kalan bağlantı kapatıldı.");
                        return;
                    }
                    if (request == null) return; // istemci bağlantıyı kapattı

                    PipeResponse response = await _router.DispatchAsync(request).ConfigureAwait(false);
                    await PipeProtocol.WriteAsync(pipe, response, token).ConfigureAwait(false);
                }
            }
            catch (IOException ex)
            {
                // UI kapandı / bağlantı koptu: normal durum
                AppLog.Debug(Module, "Bağlantı kapandı: " + ex.Message);
            }
            catch (InvalidDataException ex)
            {
                AppLog.Warn(Module, "Geçersiz mesaj alındı, bağlantı kapatıldı.", ex);
            }
        }

        private static NamedPipeServerStream CreatePipe()
        {
            var security = new PipeSecurity();
            security.AddAccessRule(new PipeAccessRule(new SecurityIdentifier(WellKnownSidType.BuiltinAdministratorsSid, null),
                PipeAccessRights.ReadWrite, AccessControlType.Allow));
            security.AddAccessRule(new PipeAccessRule(new SecurityIdentifier(WellKnownSidType.LocalSystemSid, null),
                PipeAccessRights.FullControl, AccessControlType.Allow));
            // Servisi çalıştıran hesap (serviste SYSTEM, F5 konsol modunda geliştirici) yeni dinleyici açabilmeli
            security.AddAccessRule(new PipeAccessRule(WindowsIdentity.GetCurrent().User,
                PipeAccessRights.FullControl, AccessControlType.Allow));
            // Ağdan erişim yasak
            security.AddAccessRule(new PipeAccessRule(new SecurityIdentifier(WellKnownSidType.NetworkSid, null),
                PipeAccessRights.FullControl, AccessControlType.Deny));

            return new NamedPipeServerStream(GuardConstants.PipeName, PipeDirection.InOut, ListenerCount,
                PipeTransmissionMode.Byte, PipeOptions.Asynchronous, BufferSize, BufferSize, security);
        }

        public void Dispose()
        {
            try
            {
                _cts.Cancel();
                if (_listeners != null) Task.WaitAll(_listeners, TimeSpan.FromSeconds(5));
            }
            catch (Exception ex)
            {
                AppLog.Warn(Module, "Yönetim kanalı kapatılırken hata.", ex);
            }
        }
    }
}
