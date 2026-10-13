using SecurityGuard.Shared.Contracts;

namespace SecurityGuardUI.Ipc
{
    /// <summary>Servis çağrısının sonucu. Hata durumunda ErrorMessage kullanıcıya gösterilebilir (Türkçe).</summary>
    public class GuardResult
    {
        public bool Success { get; protected set; }
        public string ErrorCode { get; protected set; }
        public string ErrorMessage { get; protected set; }

        /// <summary>Oturum düştü: UI giriş ekranına dönmeli.</summary>
        public bool IsUnauthorized { get { return ErrorCode == ErrorCodes.Unauthorized; } }

        /// <summary>Lisans geçerli değil: UI lisans sayfasını açmalı.</summary>
        public bool IsUnlicensed { get { return ErrorCode == ErrorCodes.Unlicensed; } }

        internal static GuardResult From(PipeResponse response)
        {
            return new GuardResult { Success = response.Success, ErrorCode = response.ErrorCode, ErrorMessage = Explain(response) };
        }

        /// <summary>
        /// Servis bu komutu tanımıyorsa arayüz servisten yeni demektir (güncelleme sonrası servis yeniden başlatılmamış).
        /// Ham "Bilinmeyen komut" yerine ne yapılacağını söyler.
        /// </summary>
        internal static string Explain(PipeResponse response)
        {
            if (response.ErrorCode == ErrorCodes.UnknownCommand)
                return "Çalışan SecurityGuard servisi bu arayüzden eski bir sürüm, bu işlemi tanımıyor." +
                       System.Environment.NewLine + "Servisi yeniden başlatın (Servis Yönetimi → Durdur, sonra tekrar başlatın).";
            return response.ErrorMessage;
        }

        internal static GuardResult Fail(string errorCode, string errorMessage)
        {
            return new GuardResult { Success = false, ErrorCode = errorCode, ErrorMessage = errorMessage };
        }
    }

    public sealed class GuardResult<T> : GuardResult where T : class
    {
        public T Value { get; private set; }

        internal static GuardResult<T> From(PipeResponse response, T value)
        {
            return new GuardResult<T>
            {
                Success = response.Success,
                ErrorCode = response.ErrorCode,
                ErrorMessage = Explain(response),
                Value = value
            };
        }

        internal static new GuardResult<T> Fail(string errorCode, string errorMessage)
        {
            return new GuardResult<T> { Success = false, ErrorCode = errorCode, ErrorMessage = errorMessage };
        }
    }
}
