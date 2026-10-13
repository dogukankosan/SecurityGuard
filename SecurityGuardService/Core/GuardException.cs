using System;

namespace SecurityGuardService.Core
{
    /// <summary>
    /// Beklenen iş kuralı hatası (yanlış şifre, doğrulama hatası vb.).
    /// Mesajı kullanıcıya olduğu gibi gösterilir; bu yüzden Türkçe ve teknik detaysız yazılmalı.
    /// Beklenmeyen hatalar bu sınıfla fırlatılmaz: onlar loglanır, UI'ye genel mesaj döner.
    /// </summary>
    internal sealed class GuardException : Exception
    {
        public GuardException(string errorCode, string message) : base(message)
        {
            ErrorCode = errorCode;
        }

        /// <summary><see cref="SecurityGuard.Shared.Contracts.ErrorCodes"/> değeri.</summary>
        public string ErrorCode { get; private set; }
    }
}
