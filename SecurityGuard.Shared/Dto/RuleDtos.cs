using System.Collections.Generic;

namespace SecurityGuard.Shared.Dto
{
    /// <summary>Olay kuralı (bir olay tipinin izleme / log / mail / önem / ban ayarı).</summary>
    public sealed class EventRuleDto
    {
        /// <summary>Olay tipi anahtarı (ör. "Rdp.LoginFailed"). Değişmez.</summary>
        public string EventType { get; set; }

        /// <summary>Grup: Giriş, Windows, SQL Server, SecurityGuard.</summary>
        public string Category { get; set; }

        public string Name { get; set; }

        /// <summary>Olayın kaynağı / nasıl yakalandığı (ör. "Security log 4625").</summary>
        public string Source { get; set; }

        public bool Monitor { get; set; }
        public bool WriteLog { get; set; }
        public bool SendMail { get; set; }

        /// <summary><see cref="Enums.Severity"/> değeri.</summary>
        public int Severity { get; set; }

        /// <summary>Bu kural ban destekliyor mu (sadece dış RDP / SQL başarısız giriş).</summary>
        public bool CanBan { get; set; }

        public bool BanEnabled { get; set; }

        /// <summary>Ban eşiği (başarısız deneme sayısı). Sadece CanBan kurallarında.</summary>
        public int Threshold { get; set; }

        /// <summary>
        /// Bu olayın mailinin gideceği alıcılar (Recipients.Id). Boş = tüm aktif alıcılar.
        /// Seçilenlerin hepsi pasif / silinmişse mail kaybolmasın diye tüm aktif alıcılara gider.
        /// </summary>
        public List<long> RecipientIds { get; set; }
    }

    public sealed class RuleSaveRequest
    {
        public List<EventRuleDto> Rules { get; set; }
    }
}
