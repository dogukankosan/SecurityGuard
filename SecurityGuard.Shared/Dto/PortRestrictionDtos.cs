using System;
using System.Collections.Generic;

namespace SecurityGuard.Shared.Dto
{
    /// <summary>
    /// Port IP kısıtı: bu porta sadece listedeki IP'ler (ve istenirse iç ağ) bağlanabilir, diğerleri firewall'da engellenir.
    /// </summary>
    public sealed class PortRestrictionDto
    {
        public long Id { get; set; }

        public string Name { get; set; }

        /// <summary>"TCP", "UDP" veya "BOTH".</summary>
        public string Protocol { get; set; } = "TCP";

        /// <summary>"3389", "1433,1434", "5000-5100".</summary>
        public string Ports { get; set; }

        /// <summary>İzinli adresler, satır satır: IP, CIDR (1.2.3.0/24) veya aralık (1.2.3.10-1.2.3.20). "#" sonrası açıklama.</summary>
        public string Addresses { get; set; }

        /// <summary>İzinli ülkeler (ISO kodu, virgülle: "TR" veya "TR,DE"); boşsa ülke filtresi yok.</summary>
        public string Countries { get; set; }

        /// <summary>İç ağ (10/8, 172.16/12, 192.168/16, link-local, IPv6 ULA) her zaman izinli.</summary>
        public bool AllowLan { get; set; } = true;

        public bool Enabled { get; set; }

        public string Note { get; set; }

        // --- Sadece servisten gelir
        public DateTime? UpdatedUtc { get; set; }
        public string UpdatedBy { get; set; }

        /// <summary>Doluysa uygulandı ama onaylanmadı: bu zamana kadar onaylanmazsa servis geri alır.</summary>
        public DateTime? PendingUntilUtc { get; set; }

        /// <summary>Durum metni ("Uygulandı", "Onay bekliyor", "Kapalı", "Firewall'da yok"...).</summary>
        public string State { get; set; }

        /// <summary>Firewall kural adları.</summary>
        public string RuleNames { get; set; }
    }

    /// <summary>Hazır şablon (RDP / SQL): portlar sunucudan okunur.</summary>
    public sealed class PortTemplateDto
    {
        public string Name { get; set; }
        public string Protocol { get; set; }
        public string Ports { get; set; }
        public string Note { get; set; }
    }

    public sealed class PortRestrictionStateDto
    {
        public List<PortRestrictionDto> Items { get; set; } = new List<PortRestrictionDto>();
        public List<PortTemplateDto> Templates { get; set; } = new List<PortTemplateDto>();

        /// <summary>Onay süresi (saniye).</summary>
        public int ConfirmSeconds { get; set; }

        public bool FirewallOk { get; set; }
        public string FirewallMessage { get; set; }

        /// <summary>Windows Firewall bir profilde kapalıysa açıklama (kısıt o profilde çalışmaz).</summary>
        public string FirewallOffWarning { get; set; }

        /// <summary>Ülke seçimi için liste (veritabanı yoksa boş).</summary>
        public List<CountryDto> Countries { get; set; } = new List<CountryDto>();

        /// <summary>Ülke veritabanı ayı + kaynak (db-ip.com, CC BY 4.0).</summary>
        public string GeoInfo { get; set; }

        /// <summary>Veritabanı yok / eski ise uyarı.</summary>
        public string GeoWarning { get; set; }
    }

    public sealed class CountryDto
    {
        public string Code { get; set; }
        public string Name { get; set; }
    }

    public sealed class PortRestrictionSaveRequest
    {
        public PortRestrictionDto Item { get; set; }

        /// <summary>Arayüzü açan RDP oturumunun istemci IP'si (konsoldaysa null). Engellenecekse kayıt reddedilir.</summary>
        public string ClientAddress { get; set; }

        /// <summary>Başka aktif RDP oturumları engellenecek olsa da devam (kullanıcı uyarıyı onayladı).</summary>
        public bool AcceptOtherSessions { get; set; }
    }

    /// <summary>Kaydet öncesi kontrol sonucu.</summary>
    public sealed class PortRestrictionCheckDto
    {
        /// <summary>Normalize edilmiş, firewall'a yazılacak engel aralıkları (özet).</summary>
        public string BlockedSummary { get; set; }

        /// <summary>Engellenecek aktif RDP oturumları ("kullanici (1.2.3.4)").</summary>
        public List<string> BlockedSessions { get; set; } = new List<string>();

        /// <summary>Kaydı engelleyen hata (kendi IP'niz engellenecek vb.); null ise kaydedilebilir.</summary>
        public string Error { get; set; }
    }
}
