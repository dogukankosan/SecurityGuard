# SecurityGuard

Windows sunucular için **güvenlik izleme ve saldırı engelleme** yazılımı. İnternete açık **RDP** ve **SQL Server** çalıştıran sunucuları dış saldırılara karşı korur, şüpheli her hareketi anında mail ile bildirir.

Küçük–orta ölçekli firmaların Logo/SQL sunucuları düşünülerek tasarlandı: hafif, sunucuya yük bindirmez ve **hiçbir ayarı izinsiz değiştirmez** — sadece önerir, uygulama kararı yöneticinindir.

---

## Ne yapar?

- **Saldırı engelleme:** RDP, SQL, SMB/WinRM, FTP, IIS (Web), SMTP brute-force tespiti → eşiği aşan dış IP'yi firewall'da **otomatik banlar**. Ülke kısıtı (GeoIP) ve port kısıtı.
- **İzleme & anlık alarm:** yeni / mesai dışı RDP girişi, başarısız denemelerden sonra başarılı giriş (ele geçirme şüphesi), SQL login/rol/şifre/`xp_cmdshell` değişiklikleri, Windows kullanıcı/grup/görev/firewall/audit değişiklikleri, yeni uzaktan erişim programı veya servis kurulumu.
- **Fidye yazılımı koruması:** tuzak (canary) dosyalar, gölge kopya / yedek silme ve olay logu temizleme girişimlerini yakalama.
- **Yedekleme & bakım:** zamanlanmış SQL yedeği (AES-256 şifreli RAR arşivi), yedekten geri yükleme ve doğrulama, index bakımı.
- **Raporlar & öneriler:** günlük teknik + aylık yönetici raporu (mail); güvenlik ve performans taraması (hazır düzeltme script'leriyle).
- **Kendini koruma:** servis Windows'tan durdurulamaz / silinemez (sadece arayüzden, yönetici şifresiyle); veri klasörleri kilitli; olay logu HMAC zinciriyle imzalı (dışarıdan oynama fark edilir); servis registry kaydı (Start / ImagePath) kurcalama tespiti.

## Proje yapısı

| Proje | Açıklama |
|---|---|
| `SecurityGuardService` | Windows servisi (.NET Framework 4.8). Olay toplama, ban/firewall, SQL audit/yedek/bakım, mail, lisans, kendini koruma. SQLite veritabanı (`guard.db`). |
| `SecurityGuardUI` | WinForms + DevExpress arayüzü. Servise **named pipe** ile bağlanır (yerel; ağa port açmaz). |
| `SecurityGuard.Shared` | Ortak DTO'lar, sabitler ve lisans protokolü. |
| `SecurityGuard.LicenseApi` | Lisans sunucusu (ASP.NET Core / .NET 10, SQL Server, **RSA-3072 imzalı** cevaplar). |
| `installer/` | Inno Setup kurulum paketi (`SecurityGuard.iss`). |
| `tools/` | SgAutoTest (otomatik test aracı), yük testleri, lisans DB scripti, Postman koleksiyonu. |
| `docs/` | Teknik doküman, tanıtım sayfası, mail imzası. |

## Teknoloji

.NET Framework 4.8 (servis + UI) · .NET 10 (Lisans API) · DevExpress 20.1 WinForms · SQLite · RSA-3072 imzalı lisanslama · DPAPI şifreli secret'lar · Windows Firewall (COM) · SQL Server Audit · canlı olay dinleme (EventLogWatcher).

## Mimari (özet)

- **Yerel yönetim:** UI ↔ servis arası named pipe (sadece Administrators/SYSTEM); sunucuya **inbound port açılmaz**.
- **Dışa doğru:** servis yalnızca lisans sunucusuna (ve mail SMTP'ye) **outbound** bağlanır.
- **Canlı izleme:** Windows olayları push ile (EventLogWatcher) saniyeler içinde işlenir, aralıkla taranmaz.

## Derleme

**Gereksinimler:** Visual Studio 2022+ (.NET Framework 4.8 geliştirme araçları + .NET 10 SDK), DevExpress 20.1.

Klonladıktan sonra, güvenlik gereği git'e **girmeyen** iki dosyayı elle oluştur:

**1. `SecurityGuard.Shared/LicenseServer.Local.cs`** — gerçek lisans sunucusu adresi:
```csharp
namespace SecurityGuard.Shared
{
    internal static partial class LicenseServer
    {
        public const string DefaultApiUrl = "http://<lisans-sunucu-adresin>:5080/";
    }
}
```
> Yoksa `LicenseServer.cs` placeholder (`localhost:5080`) ile derlenir; adres zaten arayüzden de ayarlanabilir.

**2. `SecurityGuard.LicenseApi/appsettings.Production.json`** — lisans sunucusu ayarları (SQL bağlantısı, admin anahtar hash'leri, Kestrel/HTTPS). Şablon için `appsettings.json`'a bakın.

Sonra `SecurityGuard.sln`'i **Release** derleyin.

## Kurulum (müşteri sunucusu)

`installer/SecurityGuard.iss` ile üretilen **`setup.exe`** ile kurulur: Windows servisini kurup başlatır, ilk açılışta kurulum sihirbazından **SQL bağlantısı, mail ve lisans** ayarlanır. Güncelleme ve kaldırma, korumalı servis olduğundan **SG yönetici şifresi** ister.

> **Önemli:** Yeni bir makineye kurulum **sıfırdan** yapılır — `guard.db` kopyalanmaz. (DPAPI ile şifreli secret'lar makineye bağlıdır ve olay logu yer imleri yeni makinede geçersiz olur; kopyalanırsa tespit/ban çalışmaz.)

## Güvenlik notları

- **Secret'lar git'e girmez:** lisans sunucusu adresi, admin anahtar hash'leri, imza/PFX anahtarları, `*.db`, kurulum çıktıları.
- **Lisanslama:** cevaplar RSA imzalı + nonce'lu + donanım kimliğine bağlı → sahte üretilemez, replay ve sahte sunucu engellenir.
- **Lisans API admin uçları:** `X-Admin-Key` (SHA-256 özet), IP izin listesi, HTTPS zorunluluğu ve rate limit ile korunur; Swagger üretimde kapalıdır.

## Test

Otomatik test aracı (`tools/autotest` → `SgAutoTest.exe`) ürünün çoğu senaryosunu uçtan uca çalıştırır:
```
SgAutoTest.exe -tam            # tüm bölümler
SgAutoTest.exe -tam -sadece <bölüm,...>   # seçili bölüm(ler)
```
