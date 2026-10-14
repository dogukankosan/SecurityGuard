using System;
using System.IO;
using System.IO.Pipes;
using System.Threading;
using SecurityGuard.Shared;
using SecurityGuard.Shared.Contracts;
using SecurityGuard.Shared.Dto;

namespace SecurityGuardUpdater
{
    /// <summary>
    /// Servisle konuşan küçük pipe istemcisi (GuardClient'ın updater için sadeleştirilmişi).
    /// Bağlandığı pipe'ın karşısındaki sürecin GERÇEK servis exe'si olduğunu doğrular: sahte pipe açan
    /// bir program SG yönetici şifresini ele geçiremez. Senkron (updater tek iş yapar).
    /// </summary>
    internal sealed class PipeClient : IDisposable
    {
        private readonly string _expectedServer;
        private NamedPipeClientStream _pipe;

        public string Token { get; private set; }

        public PipeClient(string expectedServerExe)
        {
            _expectedServer = Path.GetFullPath(expectedServerExe);
        }

        private void Connect()
        {
            if (_pipe != null && _pipe.IsConnected) return;
            Disconnect();
            var p = new NamedPipeClientStream(".", GuardConstants.PipeName, PipeDirection.InOut, PipeOptions.Asynchronous);
            try
            {
                p.Connect(3000);
                string serverPath = NativeMethods.GetServerProcessPath(p);
                if (serverPath == null || !string.Equals(Path.GetFullPath(serverPath), _expectedServer, StringComparison.OrdinalIgnoreCase))
                {
                    p.Dispose();
                    throw new UpdateException("Güvenlik: yönetim kanalına bağlanan program SecurityGuard servisi değil (" + (serverPath ?? "bilinmiyor") + "). Bağlantı kesildi.");
                }
                _pipe = p;
            }
            catch (UpdateException) { throw; }
            catch (Exception ex)
            {
                p.Dispose();
                throw new UpdateException("Servise bağlanılamadı (çalışıyor mu?): " + ex.Message);
            }
        }

        public PipeResponse Send(string command, object payload = null, int timeoutSec = 60)
        {
            Connect();
            var req = new PipeRequest { Command = command, Token = Token, Payload = payload == null ? null : PipeProtocol.ToJson(payload) };
            using (var cts = new CancellationTokenSource(TimeSpan.FromSeconds(timeoutSec)))
            using (cts.Token.Register(() => { try { _pipe.Dispose(); } catch { } }))
            {
                try
                {
                    PipeProtocol.WriteAsync(_pipe, req, cts.Token).GetAwaiter().GetResult();
                    var resp = PipeProtocol.ReadAsync<PipeResponse>(_pipe, cts.Token).GetAwaiter().GetResult();
                    if (resp == null) { Disconnect(); throw new UpdateException("Servis bağlantıyı kapattı."); }
                    return resp;
                }
                catch (UpdateException) { throw; }
                catch (Exception ex)
                {
                    Disconnect();
                    throw new UpdateException("Servisle iletişim koptu: " + ex.Message);
                }
            }
        }

        public T Send<T>(string command, object payload = null) where T : class
        {
            var r = Send(command, payload);
            if (!r.Success) throw new UpdateException(r.ErrorMessage ?? r.ErrorCode ?? "Bilinmeyen hata");
            return string.IsNullOrEmpty(r.Payload) ? null : PipeProtocol.FromJson<T>(r.Payload);
        }

        public StatusDto Status()
        {
            return Send<StatusDto>(Commands.GetStatus);
        }

        public void Login(string user, string password)
        {
            var lr = Send<LoginResult>(Commands.Login, new LoginRequest { UserName = user, Password = password });
            Token = lr == null ? null : lr.Token;
            if (string.IsNullOrEmpty(Token)) throw new UpdateException("Giriş başarısız (kullanıcı adı / şifre).");
        }

        public LicenseInfoDto License()
        {
            return Send<LicenseInfoDto>(Commands.LicenseGet);
        }

        /// <summary>Kimlikli durdurma: servis korumadan çıkıp kendini kapatır.</summary>
        public void StopService(string password)
        {
            var r = Send(Commands.ServiceStop, new PasswordConfirmRequest { Password = password });
            if (!r.Success) throw new UpdateException("Servis durdurulamadı: " + (r.ErrorMessage ?? r.ErrorCode));
        }

        private void Disconnect()
        {
            try { if (_pipe != null) _pipe.Dispose(); } catch { }
            _pipe = null;
        }

        public void Dispose()
        {
            Disconnect();
        }
    }

    internal sealed class UpdateException : Exception
    {
        public UpdateException(string message) : base(message) { }
    }
}
