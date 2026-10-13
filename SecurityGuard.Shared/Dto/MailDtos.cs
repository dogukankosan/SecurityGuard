using System.Collections.Generic;

namespace SecurityGuard.Shared.Dto
{
    /// <summary>SMTP ayarları. Şifre UI'ye asla geri gönderilmez; sadece kayıtlı olup olmadığı bildirilir.</summary>
    public sealed class MailSettingsDto
    {
        public string Host { get; set; }
        public int Port { get; set; }

        /// <summary><see cref="Enums.MailSecurity"/> değeri.</summary>
        public int Security { get; set; }

        /// <summary>Boşsa kimlik doğrulaması yapılmaz.</summary>
        public string UserName { get; set; }

        /// <summary>Kaydederken: null/boş = kayıtlı şifre değişmez. Okurken her zaman null.</summary>
        public string Password { get; set; }

        /// <summary>Okurken: kayıtlı şifre var mı.</summary>
        public bool HasPassword { get; set; }

        public string FromAddress { get; set; }
        public string FromName { get; set; }
    }

    /// <summary>Test maili: ekrandaki (henüz kaydedilmemiş olabilir) ayarlarla hemen gönderilir.</summary>
    public sealed class MailTestRequest
    {
        public MailSettingsDto Settings { get; set; }
        public string To { get; set; }
    }

    public sealed class RecipientDto
    {
        public long Id { get; set; }
        public string Email { get; set; }
        public string DisplayName { get; set; }
        public bool IsEnabled { get; set; }
        public string CreatedUtc { get; set; }
    }

    public sealed class RecipientAddRequest
    {
        public string Email { get; set; }
        public string DisplayName { get; set; }
    }

    public sealed class RecipientSetEnabledRequest
    {
        public long Id { get; set; }
        public bool IsEnabled { get; set; }
    }

    /// <summary>HTML imza ve içindeki gömülü resimler (HTML'de "cid:ContentId" ile kullanılır).</summary>
    public sealed class SignatureDto
    {
        public string Html { get; set; }
        public List<SignatureImageDto> Images { get; set; }
    }

    public sealed class SignatureImageDto
    {
        public string ContentId { get; set; }
        public string FileName { get; set; }
        public string MimeType { get; set; }

        /// <summary>Resim verisi (önizleme ve yükleme için).</summary>
        public string Base64 { get; set; }
    }

    public sealed class SignatureSaveRequest
    {
        public string Html { get; set; }
    }

    public sealed class ContentIdRequest
    {
        public string ContentId { get; set; }
    }
}
