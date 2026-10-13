using System;
using System.IO;
using System.IO.Pipes;
using System.Threading;
using System.Threading.Tasks;
using SecurityGuard.Shared;
using SecurityGuard.Shared.Contracts;
using SecurityGuard.Shared.Dto;

namespace SecurityGuardUI.Ipc
{
    /// <summary>
    /// Servisle konuşan pipe istemcisi. UI'nin servisle tek iletişim yolu budur (UI DB'ye dokunmaz).
    ///
    /// - Bağlantı açık tutulur, koparsa bir sonraki istekte yeniden kurulur.
    /// - İstekler sırayla gönderilir (aynı anda tek istek).
    /// - Güvenlik: bağlanılan pipe'ın karşısındaki sürecin bizim servis exe'miz olduğu doğrulanır.
    ///   Aynı isimde sahte bir pipe açan başka bir program yönetici şifresini ele geçiremez.
    /// - Hiçbir durumda exception fırlatmaz; sonuç <see cref="GuardResult"/> ile döner.
    /// </summary>
    public sealed class GuardClient : IDisposable
    {
        private static readonly TimeSpan ConnectTimeout = TimeSpan.FromSeconds(3);
        private static readonly TimeSpan DefaultRequestTimeout = TimeSpan.FromSeconds(60);

        private readonly SemaphoreSlim _gate = new SemaphoreSlim(1, 1);
        private readonly string _expectedServerPath;
        private NamedPipeClientStream _pipe;

        /// <summary>Servis exe'si, UI ile aynı kurulumun Service\ klasöründe beklenir.</summary>
        public GuardClient()
            : this(Path.GetFullPath(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "..", "Service", "SecurityGuardService.exe")))
        {
        }

        public GuardClient(string expectedServerPath)
        {
            _expectedServerPath = expectedServerPath;
        }

        /// <summary>Giriş yapıldıysa oturum token'ı.</summary>
        public string Token { get; private set; }

        public string UserName { get; private set; }

        /// <summary>İşlem yapılmazsa oturumun kapanacağı süre (girişte servisten gelir, Genel Ayarlar'dan değişir).</summary>
        public int IdleTimeoutMinutes { get; private set; }

        public bool IsLoggedIn { get { return Token != null; } }

        // ---------------------------------------------------------------- Komutlar

        public Task<GuardResult<StatusDto>> GetStatusAsync()
        {
            return SendAsync<StatusDto>(Commands.GetStatus);
        }

        public Task<GuardResult> CreateAdminAsync(string userName, string password)
        {
            return SendAsync(Commands.CreateAdmin, new CreateAdminRequest { UserName = userName, Password = password });
        }

        public async Task<GuardResult<LoginResult>> LoginAsync(string userName, string password)
        {
            var result = await SendAsync<LoginResult>(Commands.Login, new LoginRequest { UserName = userName, Password = password }).ConfigureAwait(false);
            if (result.Success && result.Value != null)
            {
                Token = result.Value.Token;
                UserName = result.Value.UserName;
                IdleTimeoutMinutes = result.Value.IdleTimeoutMinutes > 0 ? result.Value.IdleTimeoutMinutes : 30;
            }
            return result;
        }

        public async Task<GuardResult> LogoutAsync()
        {
            var result = await SendAsync(Commands.Logout).ConfigureAwait(false);
            Token = null;
            UserName = null;
            return result;
        }

        public Task<GuardResult> PingAsync()
        {
            return SendAsync(Commands.Ping);
        }

        /// <summary>Servis durduğunda / yeniden başladığında: oturum servis tarafında zaten geçersiz, yerelde de unutulur.</summary>
        public void ForgetSession()
        {
            Token = null;
            UserName = null;
            Disconnect();
        }

        // ---------------------------------------------------------------- Genel gönderim

        public async Task<GuardResult> SendAsync(string command, object payload = null, TimeSpan? timeout = null)
        {
            PipeResponse response = await SendCoreAsync(command, payload, timeout ?? DefaultRequestTimeout).ConfigureAwait(false);
            return GuardResult.From(response);
        }

        public async Task<GuardResult<T>> SendAsync<T>(string command, object payload = null, TimeSpan? timeout = null) where T : class
        {
            PipeResponse response = await SendCoreAsync(command, payload, timeout ?? DefaultRequestTimeout).ConfigureAwait(false);
            if (!response.Success) return GuardResult<T>.Fail(response.ErrorCode, response.ErrorMessage);

            try
            {
                return GuardResult<T>.From(response, PipeProtocol.FromJson<T>(response.Payload));
            }
            catch (Exception)
            {
                return GuardResult<T>.Fail(ErrorCodes.InvalidRequest, "Servisten gelen cevap okunamadı. UI ve servis sürümleri uyumsuz olabilir.");
            }
        }

        private async Task<PipeResponse> SendCoreAsync(string command, object payload, TimeSpan timeout)
        {
            await _gate.WaitAsync().ConfigureAwait(false);
            try
            {
                bool reused = _pipe != null && _pipe.IsConnected;
                string trustError = await EnsureConnectedAsync().ConfigureAwait(false);
                if (trustError != null)
                    return PipeResponse.Fail(ErrorCodes.ServiceNotTrusted, trustError);

                var request = new PipeRequest
                {
                    Command = command,
                    Token = Token,
                    Payload = payload == null ? null : PipeProtocol.ToJson(payload)
                };

                var pipe = _pipe;
                using (var cts = new CancellationTokenSource(timeout))
                using (cts.Token.Register(() => pipe.Dispose())) // .NET Framework'te pipe okuması iptali dinlemez
                {
                    try
                    {
                        await PipeProtocol.WriteAsync(pipe, request, cts.Token).ConfigureAwait(false);
                    }
                    catch (IOException) when (reused)
                    {
                        // Eski bağlantı ölmüş (servis yeniden başlamış olabilir). İstek gönderilemedi, yani
                        // servis tarafında işlenmedi: yeni bağlantıyla bir kez tekrar denemek güvenli.
                        Disconnect();
                        trustError = await EnsureConnectedAsync().ConfigureAwait(false);
                        if (trustError != null)
                            return PipeResponse.Fail(ErrorCodes.ServiceNotTrusted, trustError);
                        pipe = _pipe;
                        await PipeProtocol.WriteAsync(pipe, request, cts.Token).ConfigureAwait(false);
                    }
                    PipeResponse response = await PipeProtocol.ReadAsync<PipeResponse>(pipe, cts.Token).ConfigureAwait(false);
                    if (response == null)
                    {
                        Disconnect();
                        return PipeResponse.Fail(ErrorCodes.ServiceUnavailable, "Servis bağlantıyı kapattı.");
                    }

                    if (response.ErrorCode == ErrorCodes.Unauthorized)
                    {
                        Token = null;
                        UserName = null;
                    }
                    return response;
                }
            }
            catch (TimeoutException)
            {
                Disconnect();
                return PipeResponse.Fail(ErrorCodes.ServiceUnavailable, "Servise bağlanılamadı. SecurityGuard servisi çalışıyor mu?");
            }
            catch (UnauthorizedAccessException)
            {
                Disconnect();
                return PipeResponse.Fail(ErrorCodes.ServiceUnavailable, "Servise bağlanma yetkiniz yok. Programı yönetici olarak çalıştırın.");
            }
            catch (Exception)
            {
                // Bağlantı koptu, zaman aşımı (pipe kapatıldı) veya bozuk cevap
                Disconnect();
                return PipeResponse.Fail(ErrorCodes.ServiceUnavailable, "Servisle bağlantı kurulamadı veya koptu.");
            }
            finally
            {
                _gate.Release();
            }
        }

        /// <returns>Güven hatası mesajı; sorun yoksa null.</returns>
        private async Task<string> EnsureConnectedAsync()
        {
            if (_pipe != null && _pipe.IsConnected) return null;
            Disconnect();

            var pipe = new NamedPipeClientStream(".", GuardConstants.PipeName, PipeDirection.InOut, PipeOptions.Asynchronous);
            try
            {
                await Task.Run(() => pipe.Connect((int)ConnectTimeout.TotalMilliseconds)).ConfigureAwait(false);

                string serverPath = NativeMethods.GetServerProcessPath(pipe);
                if (serverPath == null || !string.Equals(Path.GetFullPath(serverPath), _expectedServerPath, StringComparison.OrdinalIgnoreCase))
                {
                    pipe.Dispose();
                    return "Güvenlik uyarısı: yönetim kanalına bağlanan program SecurityGuard servisi değil ("
                        + (serverPath ?? "bilinmiyor") + "). Bağlantı kesildi.";
                }

                _pipe = pipe;
                return null;
            }
            catch
            {
                pipe.Dispose();
                throw;
            }
        }

        private void Disconnect()
        {
            try
            {
                if (_pipe != null) _pipe.Dispose();
            }
            catch
            {
                // kapanış hatası önemsiz
            }
            _pipe = null;
        }

        public void Dispose()
        {
            Disconnect();
        }
    }
}
