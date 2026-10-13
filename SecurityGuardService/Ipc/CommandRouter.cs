using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using SecurityGuard.Shared.Contracts;
using SecurityGuardService.Core;
using SecurityGuardService.Core.Logging;
using SecurityGuardService.Licensing;

namespace SecurityGuardService.Ipc
{
    /// <summary>
    /// Pipe isteklerini komut adına göre ilgili handler'a yönlendirir.
    /// Ortak kontroller burada: servis hazır mı, oturum geçerli mi, hata yönetimi.
    /// Handler'ların fırlattığı <see cref="GuardException"/> kullanıcıya mesajıyla döner;
    /// diğer hatalar loglanır ve UI'ye genel mesaj gider (teknik detay dışarı sızmaz).
    /// İstek içeriği (Payload) asla loglanmaz: şifre içerebilir.
    /// </summary>
    internal sealed class CommandRouter
    {
        private const string Module = "Komut";

        private readonly Dictionary<string, Route> _routes = new Dictionary<string, Route>(StringComparer.Ordinal);

        /// <summary>Lisans geçerli değilken de çalışan komutlar: giriş, lisans sayfası, servis sayfası (durdur / kaldır).</summary>
        private static readonly HashSet<string> Unlicensed = new HashSet<string>(StringComparer.Ordinal)
        {
            Commands.GetStatus, Commands.CreateAdmin, Commands.Login, Commands.Logout, Commands.Ping, Commands.SetTheme,
            Commands.LicenseGet, Commands.LicenseCheck, Commands.LicenseSetApiUrl,
            Commands.ServiceInfo, Commands.ServiceStop, Commands.ServiceUninstall
        };
        private readonly Func<GuardContext> _getContext;
        private readonly SessionManager _sessions;

        public CommandRouter(Func<GuardContext> getContext, SessionManager sessions)
        {
            _getContext = getContext;
            _sessions = sessions;
        }

        /// <param name="requiresAuth">Geçerli oturum token'ı gerekir.</param>
        /// <param name="requiresReady">Servisin açılışı (DB) tamamlanmış olmalı.</param>
        public void Register(string command, bool requiresAuth, bool requiresReady, Func<RequestContext, Task<PipeResponse>> handler)
        {
            _routes.Add(command, new Route(requiresAuth, requiresReady, handler));
        }

        public async Task<PipeResponse> DispatchAsync(PipeRequest request)
        {
            if (request == null || string.IsNullOrEmpty(request.Command))
                return PipeResponse.Fail(ErrorCodes.InvalidRequest, "Geçersiz istek.");

            Route route;
            if (!_routes.TryGetValue(request.Command, out route))
                return PipeResponse.Fail(ErrorCodes.UnknownCommand, "Bilinmeyen komut: " + request.Command);

            GuardContext context = _getContext();
            if (route.RequiresReady && context == null)
                return PipeResponse.Fail(ErrorCodes.NotReady, "Servis açılıyor, lütfen birkaç saniye sonra tekrar deneyin.");

            Session session = null;
            if (route.RequiresAuth)
            {
                session = _sessions.Validate(request.Token);
                if (session == null)
                    return PipeResponse.Fail(ErrorCodes.Unauthorized, "Oturum süresi doldu, lütfen tekrar giriş yapın.");
            }

            if (context != null && !LicenseGate.Active && !Unlicensed.Contains(request.Command))
                return PipeResponse.Fail(ErrorCodes.Unlicensed, "Lisans geçerli değil: sunucu izleme ve koruma durdu. Sistem → Lisans sayfasına bakın.");

            AppLog.Debug(Module, request.Command + (session != null ? " (" + session.UserName + ")" : ""));
            try
            {
                return await route.Handler(new RequestContext(request, context, session, _sessions)).ConfigureAwait(false) ?? PipeResponse.Ok();
            }
            catch (GuardException ex)
            {
                return PipeResponse.Fail(ex.ErrorCode, ex.Message);
            }
            catch (Exception ex)
            {
                AppLog.Error(Module, "Komut işlenirken hata: " + request.Command, ex);
                return PipeResponse.Fail(ErrorCodes.Internal, "İşlem sırasında beklenmeyen bir hata oluştu. Detay servis logunda.");
            }
        }

        private sealed class Route
        {
            public readonly bool RequiresAuth;
            public readonly bool RequiresReady;
            public readonly Func<RequestContext, Task<PipeResponse>> Handler;

            public Route(bool requiresAuth, bool requiresReady, Func<RequestContext, Task<PipeResponse>> handler)
            {
                RequiresAuth = requiresAuth;
                RequiresReady = requiresReady;
                Handler = handler;
            }
        }
    }

    /// <summary>Handler'a verilen istek bilgisi.</summary>
    internal sealed class RequestContext
    {
        public RequestContext(PipeRequest request, GuardContext guard, Session session, SessionManager sessions)
        {
            Request = request;
            Guard = guard;
            Session = session;
            Sessions = sessions;
        }

        public PipeRequest Request { get; private set; }

        /// <summary>Çekirdek servisler. RequiresReady=false komutlarda null olabilir.</summary>
        public GuardContext Guard { get; private set; }

        /// <summary>Oturum. RequiresAuth=false komutlarda null.</summary>
        public Session Session { get; private set; }

        public SessionManager Sessions { get; private set; }

        /// <summary>İstek verisini okur; eksik veya bozuksa kullanıcıya "geçersiz istek" döner.</summary>
        public T Payload<T>() where T : class
        {
            T value;
            try
            {
                value = PipeProtocol.FromJson<T>(Request.Payload);
            }
            catch (Exception)
            {
                throw new GuardException(ErrorCodes.InvalidRequest, "İstek verisi okunamadı.");
            }
            if (value == null)
                throw new GuardException(ErrorCodes.InvalidRequest, "İstek verisi eksik.");
            return value;
        }
    }
}
