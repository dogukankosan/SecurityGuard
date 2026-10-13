# SecurityGuard - Proje Dokümanı

Sunucuya kurulan, RDP ve SQL Server saldırılarını izleyen, kritik olaylarda mail atan, dış saldırganları banlayan güvenlik tespit yazılımı.

**Teknoloji:** C# / .NET Framework 4.8, Windows Service, WinForms + DevExpress 20.1, SQLite (System.Data.SQLite)
**Kurulum:** Tek sunucuya, yerel makine izlenir. Setup: Inno Setup.
**Hedef ortam:** Windows Server 2012 ve üstü, SQL Server 2012 ve üstü. Genelde tek SQL instance. Tek müşteri, tek lisans.

---

## 1. Temel Kararlar

1. **Değiştirme yok.** Yazılım sunucuda hiçbir ayarı kendi kafasına göre değiştirmez (RDP portu, SQL şifresi, `sa` durumu, audit politikası, firewall kuralları vb.). Sadece kontrol eder, bulgu ve "nasıl düzeltilir" bilgisi verir. Düzeltme her zaman kullanıcının iradesiyle yapılır.
   İstisnalar (sadece bunlar): bizim ban firewall kuralları, bizim SQL Server Audit nesnesinin durdurulursa yeniden başlatılması (ayarlardan kapatılabilir), kullanıcının açtığı SQL bakımı (index / log shrink, bölüm 9.1) ve SQL yedekleme (bölüm 9.2), ikisi de varsayılan kapalı.
2. **Tek dil: Türkçe.** UI, mailler, loglar, hata mesajları.
3. **Tüm zamanlar UTC saklanır**, ekranda ve mailde yerel saat (+ UTC) gösterilir.
4. **Log ömür boyu.** DB ve loglar hiçbir zaman silinmez; kaldırmada da kalır. Yıllık arşiv dosyası (`guard_2026.db`). Disk dolmak üzereyse alarm.
5. **Servis asla çökmez** (bölüm 11).
6. **Servis yalnızca bizim UI'den durdurulabilir.** Hizmetler ekranı, `sc stop`, `net stop` çalışmaz (bölüm 7).
7. **Hassas her veri şifreli:** SMTP bilgileri, SQL bağlantı bilgileri, lisans belgesi. Kullanıcı şifresi hash'li.
8. **Şifre kurtarma yok.**

---

## 2. Neyi İzleriz

### 2.1 Giriş denemeleri
| Tehdit | Kaynak | Aksiyon |
|---|---|---|
| RDP brute force / spraying | Security 4625 (LogonType 3/10) + `RemoteDesktopServices-RdpCoreTS/Operational` 140 (NLA'da 4625'te IP gelmeyebilir) | Dış IP eşiği aşarsa: ban + mail |
| SQL brute force | Application log 18456 (`[CLIENT: x.x.x.x]`) | Dış IP eşiği aşarsa: ban + mail |
| Başarısız denemelerden sonra başarılı giriş | 4624 öncesi 4625 geçmişi | Kritik mail |
| Yeni / alışılmadık IP'den giriş | 4624, önceki IP geçmişi | Parametrik mail |
| RDP yeni oturum | TerminalServices 1149, 4624 LogonType 10 | Parametrik mail |
| Aynı kullanıcı adına çok IP'den deneme (dağıtık spraying) | 4625 / 18456 kullanıcı adı bazlı sayaç | Sadece mail (ban yok) |

### 2.2 Windows: giriş sonrası (kalıcılık, iz silme, hasar)
| Olay | Kaynak |
|---|---|
| Yeni Windows kullanıcısı | 4720 |
| Şifre sıfırlandı/değiştirildi | 4723 / 4724 |
| Gruba eklendi (özellikle Administrators) | 4732 / 4728 / 4756 |
| Kullanıcı etkinleştirildi/devre dışı | 4722 / 4725 |
| Zamanlanmış görev oluşturuldu | 4698, TaskScheduler/Operational 106 |
| Yeni servis kuruldu | System 7045, 4697 |
| **Event log temizlendi** | Security 1102, System 104 |
| Audit politikası değişti | 4719 |
| **Firewall kural/profil değişimi, yeni port açılması** | `Windows Firewall With Advanced Security/Firewall` 2004/2005/2006/2003, 4946/4947/4950 |
| Defender kapatıldı / ayar değişti | `Windows Defender/Operational` 5001 / 5007 |
| RDP portu / NLA / RDP aç-kapa | Registry izleme (değişiklik bildirimi + baseline diff) |
| Bizim servise müdahale (durdurma denemesi, başlangıç tipi, DACL) | 7036, 7040, 4670 |
| Bizim klasör/DB'ye silme, yeniden adlandırma, izin değiştirme denemesi | 4663 / 4670 (SACL) |
| Bizim ban firewall kuralı silindi/değişti | Firewall baseline diff |

**Log silme mailinde:** hangi log, kim sildi, hangi logon oturumundan (LogonId ile 4624 eşleştirilip IP ve zaman), silinmeden önceki son olaylar (zaten gerçek zamanlı yakalandığı için bizde durur).

**Audit kapalıysa:** Bazı olaylar Windows'ta varsayılan olarak loglanmaz (Other Object Access, MPSSVC Rule-Level Policy Change, Security System Extension, File System). Bu durumda UI'de o kuralın yanında **"Çalışmıyor: X audit ayarı kapalı"** uyarısı ve açma adımları gösterilir. Ayarı yazılım değiştirmez.

**Kaçırılan olay yok:** Her event log kanalı için son işlenen `EventRecordID` SQLite'ta (`EventBookmarks`) tutulur. Servis kapalıyken oluşan olaylar açılışta kaldığı yerden okunur. İlk kurulumda geçmiş işlenmez (eski denemeler ban üretmez). Kanal açılamazsa (yetki yok / kanal yok) diğerleri çalışır, dakikada bir yeniden denenir.

**Sayım kuralları (toplayıcı):**
- 4625 sadece LogonType 3 / 10 ve IP varsa sayılır. NLA'da aynı deneme hem 4625 hem RdpCoreTS 140 olarak gelir: aynı IP'den farklı kanaldan 5 sn içinde gelen iki kayıt **tek deneme** sayılır.
- Başarılı RDP girişi `TerminalServices-LocalSessionManager/Operational` 21 (yeni oturum) ve 25 (kopuk oturuma yeniden bağlanma) olaylarından okunur; kaynak `LOCAL` (konsol) sayılmaz. Security 4624 kullanılmaz: yeniden bağlanmada LogonType 7 yazılır, VM testinde bu yüzden kaçmıştı. "Başarısız denemelerden sonra başarılı giriş" = son başarılı girişten beri en az **3** hata (`LoginAttempts.FailsSinceSuccess`); toplam sayaç kümülatif kalır. "Yeni IP" = bu IP'den ilk başarılı RDP girişi.
- Dağıtık deneme (spraying): aynı kullanıcı adına **1 saat içinde 5 farklı dış IP** → pencere başına bir uyarı (ban yok).
- SQL 18456: kullanıcı ve `[CLIENT: ip]` EventData'dan; `<local machine>` = localhost. `securityGuardWar` yerel denemeleri hiç sayılmaz.
- Firewall olaylarında kendi `SecurityGuard-Block-` kurallarımız yok sayılır.

### 2.3 SQL Server olayları
- Login oluşturma / silme / değiştirme
- **`sa` ve herhangi bir SQL login şifre değişimi**, `sa` enable/disable
- Sysadmin ve diğer sabit sunucu rolleri üyelik değişimi, yetkili hesaplara yetki verilmesi
- Database create / drop / alter, TDE açma
- **Certificate, asymmetric/symmetric key, master key, credential** create / alter / drop / backup
- Server trigger create / drop / alter
- `sp_configure` değişimi (`xp_cmdshell`, Ole Automation, Ad Hoc Distributed Queries, CLR)
- Linked server create / drop
- Audit veya izleme durduruldu / değiştirildi, error log döndürüldü

### 2.4 Fidye senaryosu (sertifika şifreleyip silme)
1. **Olay bazlı:** certificate / key / credential / TDE olayları anında **Kritik**.
2. **Baseline diff (1-5 dk, parametrik):** `sys.certificates`, `sys.asymmetric_keys`, `sys.symmetric_keys`, `sys.credentials`, `sys.dm_database_encryption_keys`, `sys.server_principals`, `sys.server_role_members`, `sys.databases`, `sys.server_triggers`, `sys.configurations`, `sys.servers` anlık görüntüsü bir öncekiyle karşılaştırılır. Olay yakalama kaçırsa bile fark bulunur.
   **Uygulama (Sql/SqlBaseline, SqlMonitor her aralıkta audit kontrolünden sonra çağırır):** kategoriler: login'ler (oluşturma / silme / ad değişikliği (aynı SID) / etkinleştirme / devre dışı / şifre değişikliği (PasswordLastSetTime) / CHECK_POLICY kapatma; sa için `Sql.SaStateChanged`), sunucu rolü üyelikleri, sunucu yetkileri (CONNECT SQL, public, NT SERVICE / NT AUTHORITY hariç), credential'lar, master'daki sertifika / anahtarlar ve database master key, veritabanları (oluşturma / silme / TDE açılması / durum / recovery / sahip / TRUSTWORTHY / üzerine geri yükleme / salt okunur), sunucu trigger'ları, tehlikeli ayarlar (xp_cmdshell, Ole Automation, CLR, clr strict security, Ad Hoc, cross db ownership, remote admin connections, Database Mail XPs, external scripts), linked server'lar ve login eşlemeleri. Görüntü `Baselines` tablosunda (Scope `sql-baseline`, Key `INSTANCE|kategori`, JSON); ilk görüntü sadece kaydedilir. Aynı tip + aynı ad için son görüntüden bu yana EventLog'da SQL izlemesi olayı varsa tekrar bildirilmez (audit çalışıyorsa önce 10 sn beklenir, yazma kuyruğu boşaltılır). Audit çalıştığı halde bildirilmeyen değişiklikte olaya "SQL izlemesi bildirmedi: kapatılmış / atlatılmış olabilir" notu eklenir. Kategori başına en fazla 15 olay, fazlası tek toplu olay.

### 2.5 Tuzak dosyalar (Koruma → Tuzak Dosyalar, Engine/Canary)
Fidye yazılımı erken uyarısı. Sadece bildirir, müdahale etmez (oturum kapatma vb. yok).
- **Klasörler:** disk paylaşımları (registry `LanmanServer\Shares`; `C$`, `ADMIN$` gibi `$` ile bitenler ve yazıcılar hariç) ve SQL Yedekleme hedef klasörü kendiliğinden eklenir; elle klasör eklenebilir (ağ yolu ve Windows klasörü olmaz). Paylaşım kaldırılırsa / yedek klasörü değişirse listeden çıkar ve tuzakları temizlenir. Paylaşım ve yedek klasörü silinemez, kapatılabilir.
- **Dosyalar:** klasörün en üstüne 3 gizli dosya (alfabede başa / ortaya / sona düşen adlar: `!!_Muhasebe_Kapanis_YYYY.xlsx`, `Fatura_Arsiv_Listesi.docx`, `~~_Personel_Bordro_YYYY.pdf`; yedek klasöründe `.bak` / `.rar`), gerçekçi başlık + rastgele içerik, SHA-256 DB'de. Aynı adlı gerçek dosya varsa o ad atlanır; müşteri dosyalarına hiç dokunulmaz. Öznitelik: Gizli + dizinlenmez (Sistem değil: bazı fidye yazılımları sistem dosyalarını atlar).
- **Algılama:** klasör izleyici (anında; yazma / silme / ad değiştirme) + 5 dakikalık kontrol (kaçanlar, servis kapalıyken olanlar). Bildirim hazırlanırken içerik özeti aynıysa yok sayılır (okuma, antivirüs, öznitelik değişikliği alarm değildir). Kendi yazmalarımız kilit altında yapılıp özet güncellendiği için alarm üretmez; zamanla susturma yok. Art arda dokunuşlar 4 sn içinde tek olayda, sonraki olay en erken 60 sn sonra (arada gelenler toplanır).
- **Olay `Win.CanaryTouched` (Kritik, anında mail):** dosyalar ve ne olduğu (yeni ad dahil), kim / hangi program (dosyaya denetim işareti (SACL, Everyone: yazma / ekleme / silme / izin / sahiplik) konur; Windows "Dosya sistemi" denetimi açıksa Security 4663'ten; ağdan geldiyse "ağ paylaşımından"), paylaşıma bağlı bilgisayarlar (NetSessionEnum, son işleme göre sıralı; son 2 dk'da tek bilgisayar varsa IP olayda), sunucudaki oturumlar, ne yapılmalı. Dokunulan dosya yeniden oluşturulur.
- **Denetim ayarı** (sunucu ayarı, biz açmayız): sayfada durum + "Komutu kopyala" (`auditpol /set /subcategory:"{0CCE921D-...}" /success:enable /failure:enable`); Güvenlik Önerileri → Windows audit politikası maddesinde de önerilir. Sadece SACL konan dosyalar loglandığı için ek log yükü yok.
- **Test et:** servis bir tuzak dosyaya kendisi yazar → "TEST: Tuzak dosyaya dokunuldu" olayı + mail. Günlük rapor servis sağlığında "Tuzak dosyalar" satırı.
- Komutlar `Canary.Get / AddFolder / RemoveFolder / SetEnabled / Check / Test`; tablolar `CanaryFolders`, `CanaryFiles` (şema v12).

### 2.6 Program kurulumu, şüpheli servis, USB disk (Engine/Health/SoftwareWatch; sadece bildirir)
- **Program listesi** ("Programlar ve Özellikler": HKLM 64 / 32 bit + yüklü kullanıcı hive'ları; sistem bileşenleri, KB güncellemeleri hariç) sistem nöbetinde 2 dakikada bir önceki listeyle (Baselines `watch/software`) karşılaştırılır; MSI olmayan exe kurulumlar da yakalanır. İlk çalıştırmada sadece kaydedilir; servis kapalıyken olan değişiklik açılışta bildirilir. Aynı (sürümsüz) ad hem kalkıp hem eklenirse sürüm yükseltmesidir, bildirilmez.
  - Uzaktan erişim programı (AnyDesk, TeamViewer, RustDesk, Splashtop, ScreenConnect, UltraViewer, VNC türleri, Chrome Remote Desktop, Atera, NinjaOne, MeshAgent, NetSupport, Radmin, AnyViewer benzerleri: ad / yayıncı / yol eşleşmesi) → `Win.RemoteToolInstalled` (Kritik).
  - Diğerleri → `Win.SoftwareInstalled` / `Win.SoftwareRemoved` (Uyarı; 5'ten fazlası tek olayda toplanır). Kuran / kaldıran: son MSI kaydından (Application, MsiInstaller 11707 / 1033 / 11724 / 1034, kullanıcı SID'i), yoksa "bilinmiyor" + o an bağlı oturumlar. Kullanıcıya özel kurulumda kapsam "Sadece kullanıcı: X".
- **Şüpheli servis:** System 7045 (yeni servis, zaten Kritik) değerlendirilir: uzaktan erişim aracı, olağandışı klasör (Temp, AppData, Downloads, Users\Public, PerfLogs, ProgramData — Defender hariç — veya kök dizinde exe) ya da komut satırı / betik (cmd /c, PowerShell, -enc, rundll32, mshta, regsvr32, certutil, bitsadmin...) ise özet "ŞÜPHELİ servis kuruldu" + neden + ne yapmalı.
- **USB disk** (WMI `Win32_DiskDrive`, arayüz USB; 30 sn'de bir): yeni takılan bellek / harici disk → `Win.UsbStorage` (Uyarı + mail): model, seri no, boyut, sürücü harfi, aygıt kimliği, oturumlar. Takılı kaldıkça tekrar bildirilmez; çıkarılıp takılınca yine bildirilir; servis kapalıyken takılan açılışta. Fare / klavye / telefon (MTP) sayılmaz.
- Test: `SgAutoTest.exe -yazilim` (sahte AnyDesk kurulum kaydı ekler / siler; Users\Public'i gösteren, hiç çalıştırılmayan test servisi oluşturur / siler).


### 2.7 RDP oturum geçmişi ve mesai dışı giriş (İzleme → RDP Oturumları, Engine/Health/RdpSessionTracker)
- **Kaynak:** TerminalServices-LocalSessionManager 21 (açıldı), 23 (kapandı), 24 (koptu), 25 (yeniden bağlanıldı). Her oturum tek satır (`RdpSessions`, şema v14): kullanıcı, ilk IP + oturum boyunca bağlanılan tüm IP'ler, ülke (IPv4, ülke veritabanından; "iç ağ", "localhost"), başlangıç / bitiş, süre, kopma / yeniden bağlanma sayısı, durum (Açık / Bağlantı kopuk / Kapandı / Kapandı (bitiş bilinmiyor)), mesai dışı işareti. Konsol girişleri "konsol" olarak yazılır (uyarı üretmez).
- Servisten önce açılmış oturuma yeniden bağlanılırsa satır açılır, "başlangıç bilinmiyor" işaretlenir. Kapanışı kaçan oturumlar (sunucu kapandı vb.): açılıştan 2 dk sonra ve 10 dk'da bir, artık var olmayan oturum "bitiş bilinmiyor" ile kapatılır.
- **Sayfa:** tarih aralığı, kullanıcı, "sadece mesai dışı", "konsol girişlerini göster" filtreleri; kullanıcı başına toplam süre; Excel'e aktar (denetim için). En fazla 5000 satır.
- **Mesai dışı uyarısı (`Rdp.OffHoursLogin`, Uyarı + mail):** Genel Ayarlar → Mesai saatleri: uyarı açık / kapalı, çalışma günleri (hafta içi / Pzt-Cmt / her gün), başlangıç / bitiş (bitiş < başlangıç = gece vardiyası; gece yarısından sonraki kısım önceki günün vardiyası). Kullanıcı istisnaları (`workhours.exceptions`, RDP Oturumları → Mesai istisnaları): "her zaman serbest" veya kişiye özel gün / saat; "kullanıcı" veya "ETKİALANI\kullanıcı" ile eşleşir. Başarılı RDP girişi ve yeniden bağlanmada kontrol edilir; mailde kullanıcı, IP, ülke, gün / saat ve geçerli mesai.


### 2.8 Servis takibi (İzleme → Servis Takibi, Engine/Health/ServiceWatch)
- Seçilen Windows servisleri 15 sn'de bir kontrol edilir (`WatchedServices`, şema v15). İlk açılışta önerilenler izlemeye alınır (sadece başlangıcı "Otomatik" olanlar): SQL Server / SQL Agent (tüm instance'lar), IIS (W3SVC), Windows Firewall (MpsSvc), Defender (WinDefend), Olay Günlüğü (EventLog), Uzak Masaüstü (TermService), görünen adında LOGO / Netsis / Mikro / Nebim geçenler.
- Çalışırken durursa / kaldırılırsa `Svc.Stopped` (Kritik + mail): neden (System logu, Service Control Manager 7034 çöktü / 7031 / 7024 / 7023 hata / 7036 normal durdurma; Windows durduranı kaydetmez), başlangıç türü (devre dışı bırakılmışsa belirtilir), servise göre "neden önemli" (Firewall durursa ban kuralları çalışmaz, Olay Günlüğü durursa SecurityGuard kör kalır...). Tekrar çalışınca `Svc.Recovered` (Bilgi + mail). Durduğu sürece tekrar olay yok; son durum Baselines `watch/services`'ta: servis kapalıyken duran açılışta bildirilir.
- **Otomatik yeniden başlat** (servis bazında, varsayılan kapalı; açarken bakım uyarısı): 30 sn, 2 dk, 5 dk sonra dener; başarıda "SecurityGuard tarafından yeniden başlatıldı", 3 denemede olmazsa ayrıca olay. Devre dışı bırakılmış servise dokunmaz. Kullanıcı açıkça seçtiği için "sadece öner" kuralının istisnasıdır.
- Test: `SgAutoTest.exe -servis` (Windows Saati / W32Time'ı izlemeye alır, durdurur / başlatır, otomatik yeniden başlatmayı dener, listeyi eski haline getirir).


### 2.9 Aylık yönetici raporu (Engine/Reports/MonthlyReport)
- Her ayın 1'inde (Genel Ayarlar → Günlük rapor → Aylık rapor saati, varsayılan 09:00) önceki ayın sade dille özeti; servis o gün kapalıysa açılınca gönderilir (`report.lastMonthly`). Alıcılar ve açma / kapama: Olay Kuralları → "Aylık yönetici raporu" (`Report.Monthly`); yöneticiye sadece bu rapor gönderilebilir.
- Başta **genel durum** (İyi / Dikkat / Kötü) + tek cümle özet + madde madde nedenler. Kötü: tuzak dosya, fidye öncesi komut, uzaktan erişim kurulumu, log / kural kurcalama, admin grubuna ekleme, deneme sonrası başarılı giriş, hiç yedek alınmaması. Dikkat: kritik olay, mesai dışı / yurt dışı RDP, hatalı yedek, servis kesintisi, uygulanmamış kritik öneri, disk %90+, işlemci ort. %70+, 1000+ saldırı.
- Bölümler: 1) engellenen saldırılar (toplam, hizmet bazında, ülke bazında — db-ip, en çok saldıran 10 IP ve durumu), 2) önemli olaylar (kritikler + bilgi amaçlı sayılar), 3) RDP kullanımı (kişi başına oturum, ay içine düşen süre, mesai dışı, nereden), 4) yedek / bakım, 5) sunucu sağlığı (işlemci / bellek, diskler, servis kesintileri), 6) çözülmemiş güvenlik önerileri (son tarama), 7) önceki ayla karşılaştırma.
- Raporlar sayfasında "Aylık: önizle" (önceki ay veya bu ay şimdiye kadar) ve "Aylık: şimdi gönder". `Reports.Kind` (şema v16): günlükler son 90, aylıklar son 24 saklanır.
- Günlük rapordaki "Giriş denemeleri" artık tüm protokolleri (RDP, SQL, SMB / WinRM, FTP, Web, SMTP) sayar.


---

### 2.10 Zamanlanmış görev takibi (Engine/Health/TaskWatch)

- Görev tanımları (`Windows\System32\Tasks` altındaki XML dosyaları, sadece okunur) dakikada bir önceki listeyle karşılaştırılır. Windows denetim ayarı ("Diğer nesne erişimi", Security 4698) kapalı olsa da yeni görev yakalanır.
- Yeni görev veya çalıştırdığı komut / hesabı değişen görev → `Win.ScheduledTaskCreated` (komut, hangi hesapla, tetikleyici, gizli mi, görevi yazan, o an bağlı oturumlar). Tetikleyici / saat değişikliği ve silme bildirilmez.
- Cmd / PowerShell / rundll32 ile çalışan, Temp / AppData / Users\Public / ProgramData'dan çalışan veya uzaktan erişim aracı çalıştıran görev "ŞÜPHELİ" işaretlenir (servis kurulumundaki aynı kurallar).
- `\Microsoft\` altındaki görevleri Windows kendisi sürekli oluşturup siler: orada sadece ŞÜPHELİ olanlar bildirilir.
- Aynı görev için denetim olayı (4698, kimin oluşturduğuyla) son 10 dakikada yazıldıysa tekrar bildirilmez.
- Önceki liste Baselines tablosunda (`watch/tasks`); ilk çalıştırmada sadece kaydedilir. O an okunamayan dosya sahte "yeni görev" üretmez.
- Test: `TaskTest` (yerel), `SgAutoTest.exe -tam` → Windows olayları bölümü.

---

## 3. SQL Server (2012 ve üstü)

Kurulumda ve servis açılışında sürüm, edition, instance listesi, port **dinamik** okunur ve kaydedilir. Desteklenmeyen sürüm (2012 altı) bulunursa UI'de uyarı verilir.

**Yakalama yöntemi:**
- **SQL Server Audit** (2012+ tüm edition'larda sunucu seviyesi audit var). Hedef: Windows **Application log**. Böylece SQL olayları da Windows olaylarıyla aynı `EventLogWatcher` hattından akar.
  Gruplar: `SERVER_PRINCIPAL_CHANGE_GROUP`, `LOGIN_CHANGE_PASSWORD_GROUP`, `SERVER_ROLE_MEMBER_CHANGE_GROUP`, `SERVER_PERMISSION_CHANGE_GROUP`, `DATABASE_CHANGE_GROUP`, `SERVER_OBJECT_CHANGE_GROUP`, `DATABASE_OBJECT_CHANGE_GROUP`, `SERVER_STATE_CHANGE_GROUP`, `AUDIT_CHANGE_GROUP`, `FAILED_LOGIN_GROUP`.
- **Baseline diff** (bölüm 2.4), yedek katman.
  `FAILED_LOGIN_GROUP` eklenmez: başarısız SQL girişleri zaten 18456 ile gelir, ikisi birden her denemeyi iki kez saydırırdı. `SERVER_OPERATION_GROUP` eklenir (`sp_configure` değişiklikleri için).
- **Kurulum kullanıcının iradesiyle:** SQL Bağlantısı sayfasında "İzlemeyi Kur" (onay penceresi) veya "Audit Scriptini Kopyala" ile SSMS'ten. Servis kendiliğinden kurmaz. Nesneler: `SecurityGuard_Audit` (TO APPLICATION_LOG, ON_FAILURE = CONTINUE) ve `SecurityGuard_AuditSpec`. Kaldırma da aynı sayfadan.
- Audit kayıtları Application log 33205 olarak aynı toplayıcıdan akar. `securityGuardWar` + uygulama adı "SecurityGuard" ile yapılan (bizim) işlemler alarm üretmez; `securityGuardWar` başka uygulamadan kullanılırsa olay üretilir. Başarısız komutlar (`succeeded:false`) yok sayılır.
- Audit durdurulur/silinirse (`AUDIT_CHANGE_GROUP` + periyodik kontrol) **kritik alarm** ve audit yeniden başlatılır (ayarlardan kapatılabilir).
- **SQL'e bağlanılamazsa izleme kör kalır → alarm** (`Sql.Unreachable`, Kritik): `securityGuardWar` silinir / devre dışı bırakılır / şifresi değişirse (SQL hatası 18456/18470/18487/18488/18452) **anında**; SQL servisi durur / ağ kopar / port değişirse **2 ard arda başarısız kontrol** sonra (reboot/patch yanlış alarmı olmasın). Süren kesintide tek kez üretilir; bağlantı dönünce `Sql.Reconnected` (Bilgi). Servis çökmez, bağlantı dönünce kendiliğinden devam eder. Test: `-tam -sadece sql` sonundaki blok (hesabı DISABLE eder, alarmı bekler, ENABLE eder; `finally` hesabı tekrar açar).
- Birden fazla instance varsa her biri ayrı izlenir (genelde tek instance).
- Her kural/kontrol `MinSqlVersion` ve `RequiredEdition` taşır; desteklenmeyen madde UI'de gri görünür, nedeni yazar.

**SQL erişimi:** Tek SQL login **`securityGuardWar`**, **tam yetkili (sysadmin)**, kullanıcı kendisi açar. Login adı sabittir, arayüzden değiştirilemez (servis de sadece bu adı kabul eder). Login sysadmin değilse bağlantı testi başarısız sayılır ve kayıt yapılamaz. İzleme, audit, sağlık kontrolü, performans taraması ve bakım işlemlerinin tamamı bu hesapla yapılır. Bağlantı bilgileri SQLite'ta şifreli. Bağlantı sunucu içinden yapılır (localhost / shared memory). UI sunucu alanında `localhost` önerir; makine adı veya IP yazılırsa uyarır (ad / IP değişince bağlantı kopar).
Hesap tam yetkili olduğu için sağlık kontrolünde ayrıca önerilir: güçlü şifre, `CHECK_POLICY` açık, hesabın dışarıdan kullanılmaması (dış IP'den bu login ile deneme zaten Kritik mail + ban).

**`securityGuardWar` muafiyeti:** Sadece localhost / sunucunun kendi IP'lerinden gelen bağlantıları brute-force sayacından muaftır. Dış IP'den `securityGuardWar` ile yapılan denemeler normal sayılır ve banlanır.

---

**Sunucu ayarı değişikliğinde "kim yaptı" (Sql/SqlConfigTrail):** `sp_configure` ile yapılan değişiklikte SQL Audit kaydı (ALTER SETTINGS / `ALST`) çoğu zaman sadece `RECONFIGURE` der, hangi ayarın değiştiğini yazmaz. Hangi ayarın değiştiğini periyodik karşılaştırma bulur ama kimin yaptığını bilmez. İkisi birleştirilir: audit kaydı gelince yapan kişi (login, IP, uygulama, bilgisayar) not edilir ve karşılaştırma 5 dakikayı beklemeden çalıştırılır; `Sql.DangerousConfigChanged` olayı son 15 dakikadaki bu kayıttan "yapan" bilgisini alır.

---

## 4. Ban Motoru

**Ban sadece iki kuralda:** dış IP'den RDP başarısız giriş, dış IP'den SQL başarısız giriş.

- **Eşik:** IP başına ayrı sayaç, protokol bazlı (RDP ve SQL ayrı). Varsayılan **10 başarısız deneme**, zaman penceresi yok (kümülatif). Eşik parametrik.
- **Başarılı girişte sayaç sıfırlanmaz.**
- Sayaç RAM'de, her değişiklik kuyrukla SQLite'a yazılır; servis yeniden başlasa da sıfırlanmaz.
- **Banlanmayanlar (sakın!):**
  - İç ağ IP'leri (10.0.0.0/8, 172.16.0.0/12, 192.168.0.0/16, 169.254.0.0/16, fe80::/10, fc00::/7)
  - Localhost (127.0.0.1, ::1) ve sunucunun kendi IP'leri (otomatik, silinemez)
  - **Beyaz liste** (IP ve aralık/CIDR)
  - Bunlar için sayaç tutulur, log yazılır, kurala göre mail atılır, **ban atılmaz**.
- **Ban kalıcı**, **tüm portlar ve tüm protokoller** engellenir.
- **Firewall:** `SecurityGuard-Block-001`, `-002`... (Inbound, Block, Any protokol). Her kuralda en fazla ~1000 IP (tek kuralda binlerce IP yavaşlar/bozulur). Binlerce ayrı kural açılmaz.
- Ban listesinin asıl kaydı SQLite `BannedIps`. Firewall kuralı silinir/bozulursa servis SQLite'tan yeniden kurar ve alarm verir.
- **UI:** ban listesi, arama, tek tek / toplu ban kaldırma, manuel ban ekleme, sebep ve tarih; beyaz liste yönetimi.
- **Ban kaldırılınca** o IP'nin başarısız deneme sayacı da sıfırlanır (yoksa bir sonraki hatada tekrar banlanır). "Sayacı Sil" banı etkilemeden sayacı temizler. Her ban / kaldırma İşlem Kaydı'na yazılır.
- Korunan adres kontrolü koddadır (Whitelist tablosu SQLite ekranından silinse bile iç ağ / localhost / sunucu IP'leri korunur). Elle ban penceresi, yöneticinin bağlı olduğu RDP IP'sini banlatmaz.
- Firewall önce yazılır, sonra DB: kural yazılamazsa IP "banlı" görünmez. Senkron: servis açılışında, her 5 dakikada bir, bizim kurala dokunan firewall olayı geldiğinde (2 sn içinde, butona gerek yok) ve "Firewall Kontrol" butonunda: eksik / değiştirilmiş kural onarılır, DB'de karşılığı olmayan bizim kuralımız silinir, fark varsa "Ban firewall kuralı silindi / değiştirildi" olayı. Yalnızca `SecurityGuard-Block-` ile başlayan kurallara dokunulur.
- IPv6 desteklenir.
- **Toplu ban (yük testi sonrası):** eşik aşan IP olay işleyiciyi bekletmeden kuyruğa girer; 300 ms içinde gelenler tek seferde banlanır (kural başına TEK firewall yazması, tek DB işlemi). Banlı IP listesi bellekte tutulur (DB'den bir kez yüklenir, 5 dakikalık senkronda DB'den tazelenir). Gerekçe: VM ölçümü (FwBench) kural yazma 18 ms (1 adres) – 49 ms (1000 adres); eski yöntemde her ban kuralı baştan yazıyordu ve her banda tüm ban tablosu okunuyordu → 1000 IP'lik dağıtık saldırıda ~40 sn olay işleme durması, IP sayısı arttıkça karesel yavaşlama. Elle ban yine anında ve hatayı arayüze döner.
- **NAT / donanım firewall uyarısı:** Başarısız denemelerin çoğu tek bir iç IP'den geliyorsa (gerçek IP'yi gizleyen cihaz), sağlık kontrolü bunu uyarı olarak gösterir.

### 4.0 Diğer protokollerde şifre denemeleri (SMB / WinRM / FTP / Web / SMTP)
- **RDP / SMB ayrımı (WindowsEventProcessor, RdpNetPairing):** 4625 giriş tipi 10 → RDP. Giriş tipi 3 hem NLA'lı RDP'de hem SMB / WinRM'de gelir: aynı IP'den 5 sn içinde RdpCoreTS 140 varsa RDP (bir kez sayılır), yoksa 5 sn sonra `Net.LoginFailed` (SMB / WinRM) olarak sayılır. Önceden bu denemeler "RDP" sayılıyordu. `w3wp.exe` kaynaklı 4625 sayılmaz (IIS logundan sayılır, iki kez olmasın).
- **IIS log okuyucu (Engine/Collectors/IisLogCollector):** applicationHost.config'ten site log klasörleri bulunur (W3SVCn, FTPSVCn) + `System32\LogFiles\SMTPSVC*`. 15 sn'de bir yeni satırlar okunur: FTP `PASS` 530 → `Ftp.LoginFailed`, web 401.1 (Basic / OWA / WebDAV kimlik doğrulama hatası; NTLM el sıkışmasının 401.2'si sayılmaz) → `Web.LoginFailed`, SMTP `AUTH` 535 → `Smtp.LoginFailed`. Kaldığı yer ayarlarda (`iislog.positions`); ilk görülen eski dosya sondan okunur (geçmiş ban üretmez), çalışırken oluşan yeni dosya baştan; yarım satır sonraki taramaya kalır. IIS logu ~1 dk tamponlar: tespit gecikmesi bu kadar. Kurulu değilse hiçbir şey yapmaz.
- Her protokolün Olay Kuralları'nda kendi satırı (eşik varsayılan 10, ban açık): ban kaydında protokol `SMB` / `FTP` / `WEB` / `SMTP`.

### 4.0.1 Açık Portlar (Koruma → Açık Portlar, Checks/Security/OpenPorts, `OpenPorts.Get`)
Sunucuda dinleyen TCP / UDP portları (IPv4 + IPv6): program, servis (WMI), dinlenen adresler; firewall açıklığı: "Sadece bu sunucu" (localhost), "Herkese açık" (firewall kapalı veya uzak adresi `*` olan izin kuralı; kural adı), "Kısıtlı" (belirli adres / LocalSubnet), "SecurityGuard kısıtı", "Kapalı" (izin kuralı yok / engel kuralı). Kural eşleşmesi: port, program yolu veya servis adı. Bilinen portlar (RDP portu registry'den, SQL portları instance'lardan, SMB, RPC, WinRM, FTP, Telnet, VNC, MySQL, AnyDesk, TeamViewer...) etiketlenir; herkese açık + riskli = "Yüksek", herkese açık = "Orta", kısıtlı + riskli = "Düşük". Son 24 saatte o protokolde başarısız giriş sayısı ve öneri. Varsayılan liste sadece dışarıya açık olanlar ("kapalı ve yerel olanları da göster" seçeneği). "Port kısıtı ekle..." Port Kısıtları sayfasını o port için doldurulmuş formla açar (RDP'de TCP + UDP). Hiçbir port kapatılmaz.

### 4.1 Port Kısıtları (Koruma → Port Kısıtları)

"Bu porta sadece şu IP'ler bağlanabilsin, diğerleri engellensin" listesi (IT isteği). Ban'den farklı: saldırı beklemeden, izinli liste dışındaki herkes baştan engellenir.

- **Kapsam:** her port (TCP / UDP / TCP+UDP; tek port, liste veya aralık: `3389`, `1433,1434`, `5000-5100`; `*` yok). Hazır şablonlar sunucudan okunur: RDP (registry `PortNumber`, TCP+UDP), her SQL instance'ının TCP portu, adlı instance varsa SQL Browser (UDP 1434).
- **İzinli adresler:** satır satır IP, CIDR veya aralık (`85.105.0.10-85.105.0.20`), `#` sonrası açıklama. Sabit IP / aralık (değişen IP / DDNS yok). En fazla 500 adres. "İç ağ her zaman izinli" (varsayılan açık: 10/8, 172.16/12, 192.168/16, link-local, IPv6 ULA); localhost her zaman izinli.
- **Firewall:** Windows'un kendi kurallarına dokunulmaz. Her kısıt için bizim ENGELLE kuralımız: `SecurityGuard-Restrict-001-TCP` / `-UDP` (gelen, tüm profiller, port = kısıt portları, uzak adres = izinli listenin **tümleyeni**, IPv4 ve IPv6 aralıkları olarak hesaplanır). Engelle kuralı izin kurallarından önce geldiği için Windows'un "Uzak Masaüstü" izin kuralı açık kalsa da kısıt geçerlidir. Bir porta tek kısıt (iki kısıt kesişim olurdu, kayıt reddedilir).
- **Kilitlenme koruması:** (1) Arayüzü açan RDP oturumunun IP'si kısıtlanan RDP portunda engellenecekse kayıt yapılmaz. (2) Başka aktif RDP oturumları engellenecekse listelenir, kullanıcı onaylamadan kaydedilmez. (3) Kaydedilen değişiklik hemen uygulanır ve **5 dakika içinde onaylanmazsa servis geri alır** (düzenlemede ilk onaylı hale, yeni kısıtta kapatır; süre servis kapalıyken dolarsa açılışta). Firewall mevcut bağlantıları kesmediği için onay öncesi izinli bir IP'den **yeni** bağlantıyla test istenir. Onay penceresinde geri sayım, "Onayla", "Hemen geri al", "Sonra karar ver".
- **Nöbet:** servis açılışında, 5 dakikada bir ve bizim kısıt kuralına dokunan firewall olayında (2 sn içinde): eksik / değiştirilmiş / devre dışı bırakılmış kural DB'ye göre onarılır, DB'de karşılığı olmayan kısıt kuralımız silinir → `Guard.PortRuleTampered` (Kritik). Ban senkronu bu kurallara dokunmaz (`SecurityGuard-Block-` ön eki ayrı).
- **Olaylar:** `Guard.PortRestrictionChanged` (onaylandı / kapatıldı / silindi, Uyarı + mail; yetkisiz bir kişinin kendi IP'sini eklemesi görülsün diye), `Guard.PortRestrictionReverted` (onaylanmadığı için geri alındı), `Guard.PortRuleTampered`. Her işlem İşlem Kaydı'na yazılır.
- **Firewall kapalıysa** sayfada uyarı (kısıtlar da ban kuralları da çalışmaz). Güvenlik Önerileri → "Herkese açık portlar" bizim kısıtımızla korunan portları açık saymaz.
- Sadece kullanıcı "Kaydet"e basınca firewall değişir; servis kendiliğinden kısıt eklemez (öneri değil, yöneticinin iradesi).
- Komutlar: `PortRestrict.Get / Check / Save / Confirm / Revert / Disable / Delete`. Tablo `PortRestrictions` (şema v11, v13'te `Countries`; `PendingUntilUtc` + `PreviousJson` geri alma noktası).
- **Ülke kısıtı (Engine/Firewall/GeoIpDatabase):** kısıtta "İzinli ülkeler" seçilebilir (örn. sadece Türkiye); seçilen ülkelerin tüm IP aralıkları izinli listeye eklenir (değişen IP'li kullanıcılar için; DDNS yerine). Kaynak: db-ip.com "IP to Country Lite" (aylık, CC BY 4.0, kaynak gösterimi arayüzde ve raporda: "IP Geolocation by DB-IP"). Bir kopya programla gelir (`Service\GeoIP\dbip-country-lite-YYYY-MM.csv.gz`, ~4,5 MB); servis 12 saatte bir bakar, bu ayın dosyası yoksa indirir (`Kök\GeoIP`), dosyayı tamamen okuyup doğrulamadan (≥300 bin satır, TR kaydı) kullanmaz, eskisini siler. Yeni dosya devreye girince tüm kısıtlar sessizce yeniden yazılır (kurcalama olayı üretmez). 3 aydan eski veritabanı arayüzde ve günlük raporda uyarı. Sadece seçilen ülkelerin aralıkları bellekte tutulur (TR: 4.517 birleşik aralık, okuma ~0,35 sn).
- **Büyük engel listeleri parçalanır:** kural başına en fazla 1000 girdi (`...-001-TCP`, `-TCP-2`, ...); sadece Türkiye ≈ 4.500 girdi = protokol başına 5 kural. Toplam sınır 40.000 girdi (TR+DE ≈ 36 kural; ABD / Almanya / Hollanda gibi çok parçalı ülkelerin birlikte seçimi reddedilir). Kilitlenme koruması ülke için de geçerli (arayüzü açan RDP IP'si seçilen ülkede / listede değilse kayıt reddedilir).

---

## 5. Kural Motoru (parametrik)

Her olay tipi UI'de bir satırdır:

| Alan | Açıklama |
|---|---|
| İzle | Olay yakalansın mı |
| Logla | Detaylı log yazılsın mı (varsayılan açık) |
| Mail | Mail atılsın mı |
| Önem | Bilgi / Uyarı / Kritik |
| Alıcılar | Bu olayın maili hangi kişilere gidecek (boş = tüm aktif alıcılar; seçilenlerin hepsi pasifse uyarı kaybolmasın diye tüm aktif alıcılara). Günlük rapor da "Raporlar" grubunda ayrı bir kural olarak aynı şekilde seçilir. Toplu atama var. |
| Ban | Sadece dış RDP ve dış SQL giriş kurallarında |
| Eşik | Ban kurallarında deneme sayısı |
| Durum | Çalışıyor / Çalışmıyor (audit kapalı, yetki yok, sürüm desteklemiyor) |

- **Mail tamamen parametrik.** Zararlı (Kritik) olaylarda mail **varsayılan açık (checkli)** gelir, kullanıcı kapatabilir. Kritik bir olayın maili kapatılırsa sağlık kontrolünde **Uyarı** olarak önerilir ("X olayı için mail kapalı").
- Ayar değişince servis **yeniden başlatmadan** canlı okur.
- "Test maili gönder" butonu.
- Kritik olaylar anında mail atar. Aynı türden yoğun olaylar (başarısız giriş yağmuru) 1-2 dakikada tek maile toplanır.

---

### 5.0 Anlık Kritik Mail Listesi (varsayılan açık, toplanmadan hemen gider)

| Alan | Olay |
|---|---|
| SQL hesapları | Login açma / silme / değiştirme; `sa` veya herhangi bir login şifre değişimi; `sa` enable/disable; sysadmin (ve diğer sabit rollere) üye ekleme / çıkarma; **full yetkili hesapta her türlü değişiklik** (şifre, yetki, rol, enable/disable, isim) |
| SQL güvenlik nesneleri | **Credential** create / alter / drop; certificate, asymmetric/symmetric key, master key create / alter / drop / backup; TDE açma |
| SQL log / iz | **SQL error log silinmesi / döndürülmesi** (`sp_cycle_errorlog`, ERRORLOG dosya silme - klasör izleme ile), audit / izleme durdurma veya silme |
| SQL diğer | Database drop; server trigger değişimi; `xp_cmdshell` / CLR / Ole Automation açılması; linked server ekleme |
| Windows log | **Event log temizlenmesi** (Security 1102, System ve diğerleri 104); audit politikası değişimi |
| Windows hesapları | Yeni kullanıcı; Administrators grubuna ekleme; yönetici şifre sıfırlama |
| Firewall | **Yeni kural / port açılması**, kural değişimi / silinmesi, firewall profili kapatma; bizim ban kuralına dokunulması |
| Kalıcılık | Yeni servis kurulumu, zamanlanmış görev oluşturma, Defender kapatma |
| Giriş | Ban (dış IP eşik aştı); başarısız denemeler sonrası başarılı giriş; dış IP'den `securityGuardWar` denemesi |
| Bizim sistem | Servis durdurma denemesi, servis DACL / başlangıç tipi değişimi, DB veya yedek klasörüne silme / izin değiştirme denemesi, log bütünlüğü bozulması, yedek başarısız, lisans bitti |

Her satırın maili UI'den kapatılabilir; kapatılırsa sağlık kontrolünde uyarı olarak önerilir.

### 5.1 Parametrik (UI'den değiştirilebilen) ayarlar
Kodda sabit değer yok; mantıklı olan her şey UI'den yönetilir, varsayılanlarla gelir:

| Alan | Parametreler (varsayılan) |
|---|---|
| Olay kuralları | İzle / Logla / Mail / Önem / Alıcılar, her olay tipi için ayrı (Kritik: mail açık) |
| Ban | RDP eşiği (10), SQL eşiği (10), ban açık/kapalı, beyaz liste |
| Mail | SMTP sunucu, port, SSL/TLS, kullanıcı, şifre, gönderen, alıcılar (birden fazla), toplama süresi (2 dk), HTML imza |
| SQL | Instance, bağlantı bilgisi, baseline diff aralığı (5 dk), audit yeniden başlatma (açık) |
| Sağlık kontrolü | Tarama saati (20:00), rapor maili, kontrol bazında görmezden gelme |
| Performans / bakım | Tarama saati, bakım açık/kapalı (kapalı), bakım penceresi, DB seçimi, parçalanma eşikleri, log shrink açık/kapalı |
| Yedekleme | Açık/kapalı (kapalı), DB seçimi, günler + saat, yerel klasör, WinRAR yolu, arşiv şifresi, saklama günü, COPY_ONLY |
| Log | Seviye (Info), disk doluluk alarm eşiği |
| UI | Tema (dark) |

**Genel Ayarlar sayfası:** Arayüz girişi (hatalı giriş hakkı 5, kilit süresi 15 dk, oturum zaman aşımı 30 dk / en fazla 240), günlük rapor saati (20:00), SQL değişiklik kontrol aralığı (5 dk), audit yeniden başlatma (açık), mail toplama süresi (2 dk), log seviyesi (Normal), disk doluluk uyarısı (%90). Tanım, varsayılan ve sınırlar tek yerde (servis `GeneralSettings`); DB'ye elle sınır dışı değer yazılsa bile servis sınırda kalır. Her değişiklik İşlem Kaydı'na eski → yeni değerle yazılır. Kendi sayfası olan ayarlar (kurallar / ban eşikleri, mail, SQL bağlantısı, yedekleme, bakım) orada kalır.

Sabit kalanlar (güvenlik gereği değiştirilemez): lisans doğrulama aralığı (5 dk), iç ağ / localhost / sunucu IP'lerinin ban muafiyeti, log ve DB'nin silinmemesi.

## 6. Mail

- Tek gönderen SMTP hesabı (SSL/TLS, port parametrik), **birden fazla alıcı**.
- SMTP bilgileri SQLite'ta şifreli.
- Asenkron: `MailQueue` tablosu, gönderilemezse yeniden dener, izlemeyi asla bloklamaz.
- **Eski mailler:** 24 saatten uzun gönderilemeyen (SMTP ayarı yok / sunucuya ulaşılamıyor) mailler, gönderim mümkün olduğu anda tek tek gönderilmez, "süresi geçti" olur; yerine tek özet mail gider ("Gönderilemeyen N eski bildirim", konu bazında adet, ilk / son zaman). SMTP ayarı hiç yokken hiçbir mail iptal edilmez. Alıcı yoksa mail tekrar denenir (en fazla 50). İzleme / log / ban mail ayarından bağımsızdır.
- **Toplama (olay tipi bazında, süre Genel Ayarlar, varsayılan 2 dk):** Kritik olayın ilk maili hemen gider; aynı tip süre içinde tekrarlanırsa sonrakiler toplanıp süre dolunca tek özet mail ("X (N olay)", en fazla 50 satır, tamamı Olay Logu'nda). Kritik olmayanlar hep toplanır (tek olaysa normal mail). Her olay için bir kez mail; durum olayları (audit durdu vb.) sadece durum değişince. 0 = her olay hemen. Servis dururken bekleyenler kuyruğa alınır.
- İçerik (HTML, Türkçe): olay, önem, sunucu adı, zaman (yerel + UTC), kim, hangi IP, detay, varsa öncesi/sonrası değer, önerilen aksiyon.
- **HTML imza:** SQLite'ta (`Settings`) saklanır, **her mailin sonuna** eklenir (olay, günlük rapor, test, lisans maili dahil). UI'de imza sayfası: HTML düzenleme + canlı önizleme + "test maili gönder". İmzadaki resimler (logo vb.) SQLite'ta saklanır ve maile gömülü (CID) eklenir; dış linke / base64'e bağlı kalmaz (Outlook / Gmail engellemesin). Mail ayrıca düz metin alternatifiyle gönderilir.

---

## 7. Servis ve Veri Koruması

**Amaç:** Kimse kolayca durduramasın/silemesin, denerse alarm gelsin, kanıt sunucu dışına çıksın.

1. **Servis DACL:** Administrators dahil kimseye dışarıdan Stop / Pause / Delete / Change Config izni verilmez. Hizmetler ekranı, `sc stop`, `net stop` çalışmaz.
2. **Yönetim kanalı:** UI servisi **named pipe** üzerinden yönetir (sadece yerel, ağa port açılmaz). Pipe ACL: sadece yerel Administrators. UI login sonrası oturum token'ı alır; her komut token ile. Durdurma / yeniden başlatma / kaldırma sadece login olmuş yönetici ile, servis bunu kendi yapar.
3. **SQLite koruması:** Servis DB'yi sürekli açık tutar (silme/yeniden adlandırma kilitlenir). `Database\` ve `Logs\` klasörlerine özel ACL: SYSTEM tam yetki, diğer herkese silme/yeniden adlandırma/yazma yok. `-wal` / `-shm` dosyaları dahil. UI DB'ye doğrudan bağlanmaz.
4. **Denetim:** Klasör ve DB için SACL (sadece DELETE, WRITE_DAC, WRITE_OWNER; normal yazmalar log şişirmesin). 4663 / 4670 izlenir, denemeler Kritik mail.
5. **Windows kurtarma ayarı:** Servis hata ile kapanırsa otomatik yeniden başlar (1., 2., sonraki hatalar).
6. **Şifreleme:** DPAPI (SYSTEM kapsamı + ek entropy). SMTP, SQL bağlantı bilgileri, lisans belgesi, HMAC anahtarı.
7. **Yönetici şifresi:** PBKDF2 (salt + yüksek iterasyon), düz metin yok. 5 hatalı girişte geçici kilit.
8. **Log bütünlüğü:** `EventLog` satırlarında **HMAC zinciri** (anahtar DPAPI ile). Satır silinir/değişirse fark edilir. Olay Logu sayfasında "Bütünlük Kontrolü": değiştirilmiş satır ve kopukluk (aradan / baştan silme) sayısı ile ilk sorunlu Id'ler. Olaylar `EventRecorder` üzerinden yazılır: kural İzle kapalıysa yok sayılır, Logla açıksa zincirli yazılır, Mail açıksa kuraldaki alıcılara kuyruğa alınır (Kritik öncelikli). Zincir başı (son hash) periyodik olarak dışarı çıkar (günlük rapor maili, ileride lisans API'si).
9. **Yedek:** DB'nin periyodik şifreli yedeği (SQLite online backup) ayrı klasörde. DB silinirse/bozulursa servis yedekten kurar ve Kritik mail atar.
10. **Sunucu dışı kopya:** Kritik olaylar mail ile anında dışarı çıkar.
11. **Kapanış maili:** Servis kapanırken "servis durduruldu" maili atmayı dener.
12. **Heartbeat (lisans API'si ile, sonra):** 5 dakikalık lisans isteği aynı zamanda heartbeat. Gelmezse API tarafı "sunucu sessiz" maili atar. Servisi öldüren saldırgan da böyle yakalanır.

**Uygulama (Engine/Health/SelfProtection, Genel Ayarlar → Servis koruması; üçü de varsayılan açık, sadece Windows servisi olarak çalışırken — konsol / F5 modunda uygulanmaz):**
- **Servis izinleri** (dakikada bir kontrol): SYSTEM tam; Administrators sorgula / başlat / izin oku / izin yaz (WRITE_DAC kurtarma yolu olarak bilerek açık) — durdur, duraklat, ayar değiştir, sil YOK. `sc stop / config / delete`, Hizmetler ekranı "Durdur" erişim engellendi (5) verir. Durdurma / kaldırma: Servis sayfası (yönetici şifresi; servis kendi kendini durdurur). İzinler değiştirilirse (`sc sdset`) 1 dk içinde geri yazılır → `Guard.ServiceTamper` (Kritik). Kapatılınca Windows varsayılan izinleri. Windows'un geri okuduğu biçim de "bizim" sayılır (biçim farkı sahte alarm üretmez).
- **Klasör izinleri:** Database (yedekleri dahil) ve Logs + SQL Yedekleme hedef klasörü: sahibi SYSTEM, devralma kapalı, SYSTEM tam, Administrators sadece okuma (yönetici / fidye yazılımı silemez, şifreleyemez; servis SYSTEM olarak normal yazar). Sürücü kökü, ağ yolu ve sistem klasörleri korunmaz; yedek klasörü değişirse eskisinin koruması kalkar. `takeown` + `icacls` ile açılırsa 1 dk içinde geri kilitlenir → `Guard.DataTamper` (Kritik). Uygulanamazsa bir kez loglanır, sahte alarm yok. Kapatılınca klasör üst klasörün izinlerine döner.
- **Kapanış:** arayüzden durdurma → `Guard.ServiceStopped` (Uyarı + mail, kim durdurdu), sunucu kapanışı → aynı olay; başka yoldan durdurma → `Guard.ServiceTamper` (Kritik). Mail kuyruğu kapanışta 10 sn boşaltılır.
- **Beklenmedik kapanma:** dakikalık yaşam sinyali + temiz kapanış işareti (`guard.heartbeat`, `guard.cleanStop`). Açılışta önceki çalışma temiz kapanmadıysa `Guard.UnexpectedStop` (Kritik): son yaşam sinyali, kapalı kalınan süre, sunucu yeniden başlamış mı (elektrik / mavi ekran) yoksa servis işlemi mi öldürülmüş. Servis öldürülürse Windows kurtarma ayarı (kurulumda: 5 / 10 / 30 sn) yeniden başlatır.
- **Güncelleme:** servis artık `net stop` ile durmaz; önce Servis sayfasından durdurun, dosyaları kopyalayın, Hizmetler'den başlatın (başlatma izni var). Kurulum programı bu akışı otomatik yapacak.
- Test: `SgAutoTest.exe -koruma`.

**Dürüst sınır:** SYSTEM yetkisine yükselmiş bir saldırgan (sahiplik alma, Safe Mode) her dosyayı silebilir, Windows'ta %100 engel yok. Garanti: **fark edilmeden silinemez, silinirse yedekten geri gelir, mail gider.**

---

## 8. UI (DevExpress 20.1)

- **İlk kurulum sihirbazı (sıra zorunlu, adım atlanamaz):** 1) Yönetici oluştur → 2) Lisans → 3) SQL bağlantısı (`securityGuardWar`, bağlantı testi başarılı olmadan ilerlemez) → 4) SMTP (test maili gitmeden ilerlemez) → 5) Beyaz liste (ofis IP'leri) → 6) İzleme başlar.
- **Uygulama (Forms/SetupWizardForm, Setup.Status / Setup.Complete):** yönetici oluşturulup giriş yapıldıktan sonra, kurulum bitirilene kadar (`setup.completed`) her girişte sihirbaz açılır; kaldığı yerden (ilk eksik zorunlu adım) devam eder. Adımlar: Hoş geldiniz → SQL Server (SqlModule; zorunlu: en az bir bağlantı + bağlanan instance'larda SQL izlemesi kurulu; sunucuda SQL yoksa geçilir) → Mail (MailModule; zorunlu: test edilmiş SMTP ayarı + en az bir aktif alıcı) → Beyaz liste (WhitelistModule; isteğe bağlı, otomatik korunanlar anlatılır) → Bitti (durum özeti + önerilenler). Lisans adımı lisans geliştirmesinde eklenecek. Zorunlu şart sağlanmadan "İleri" geçmez, alt satırda eksik yazar (4 sn'de bir yenilenir); servis de bitirirken aynı şartları denetler. Bitirince İşlem Kaydı'na yazılır ve ilk Güvenlik Önerileri taraması başlar. Sihirbaz kapatılırsa program kapanır.
- **Sayfa kilitleri (uygulandı):** SQL Bakımı ve SQL Yedekleme `SqlRequiredModule` ile sarılı: SQL bağlantısı yoksa sayfa yerine sebep ve "SQL Bağlantısı sayfasına git" düğmesi; sayfa her gösterilişte tekrar kontrol eder. Mail zaten zorunlu olduğundan mail kilidi gerekmedi.
- **Bağımlılık kuralı:** Bir sayfa ihtiyaç duyduğu bilgi girilmeden kullanılamaz (SQL bilgisi yoksa SQL sayfaları kilitli: "Önce SQL bağlantısını tanımlayın"; mail bilgisi yoksa mail seçenekleri pasif). Servis tarafında da aynı kontrol: eksik ayarda ilgili modül çalışmaz, loga yazar, hiçbir şey patlamaz.
- **SQLite sorgu ekranı:** Tamamen serbest, yetki kısıtı yok (SELECT, INSERT, UPDATE, DELETE, CREATE, ALTER...). Sonuç grid'de, Excel'e aktarma. Sorgular servis üzerinden çalışır (UI DB'ye doğrudan bağlanmaz). Çalıştırılan her sorgu AuditTrail'e yazılır. `EventLog`'dan bu ekrandan yapılan silme, bütünlük kontrolünde "yönetici tarafından silindi" olarak tanınır, sahte alarm üretmez. DB dosyasının kendisi silinemez.
  - Çalıştırma: F5 / Ctrl+Enter, seçili metin varsa sadece o. Sadece SELECT ise ayrı okuma bağlantısında; değişiklik yapan SQL tek yazıcı kuyruğunda tek parça çalışır (bir ifade hata verirse o çalıştırmadaki tüm değişiklikler geri alınır). VACUUM / BEGIN-COMMIT / ATTACH / PRAGMA ayrı bağlantıda; COMMIT edilmeden bırakılan transaction geri alınır ve bildirilir.
  - Sonuç kümesi başına en fazla 5000 satır gösterilir (toplam satır sayısı yine bildirilir). NULL italik gösterilir, BLOB kısa hex önizleme.
  - Elle yapılan değişiklikten sonra ayarlar, beyaz liste ve kurallar bellekte yeniden okunur (servis yeniden başlatılmadan geçerli olur).
- **Giriş ekranı:** tek yönetici hesabı (kullanıcı adı / şifre). İlk açılışta "Yönetici oluştur" ekranı. Başka rol/kullanıcı yok. 5 hatalı girişte geçici kilit.
- **Tema:** Birden fazla DevExpress teması seçilebilir, **varsayılan dark tema**. Seçim SQLite'ta saklanır, giriş ekranı dahil uygulanır.
- **Menü ikonları:** DevExpress Icon Builder SVG'leri (`SecurityGuardUI/Resources/Menu`, exe'ye gömülü). 8 MB'lık DevExpress.Images DLL'i dağıtılmaz, sadece kullanılan ~20 ikon (~18 KB) alınır. SVG oldukları için seçili temanın renklerine uyarlar.
- **Panel (uygulandı, Engine/Reports/Dashboard + Modules/DashboardModule):** 8 durum kutucuğu (olaylar 24 saat, banlar, güvenlik önerileri, performans önerileri, SQL yedekleri, olay logu bütünlüğü (son kontrolün sonucu, yeniden doğrulanmaz), SQL Server izleme, mail); renk: yeşil iyi / mavi bilgi / turuncu uyarı / kırmızı kritik; tıklanınca ilgili sayfa açılır. Son 7 günün kritik + uyarı olayları (20) ve "yapılması önerilenler" (mail ayarı / alıcı yok, SQL izlenmiyor, yedek kapalı / başarısız / eski, bütünlük bozuk, taramalardaki kritik bulgular, fidye yazılımı erken uyarısı kapalı); çift tıklayınca ilgili sayfa. Sayfa görünürken dakikada bir yenilenir.
- **Sistem nöbeti (Engine/Health/SystemWatch):** RDP ayarları (aç / kapa, port, NLA, güvenlik katmanı; ilke değerleri öncelikli) 30 sn'de bir okunur, son durum `watch.rdpSnapshot` ayarında: değişince `Win.RdpSettingsChanged` (servis kapalıyken değişen de açılışta yakalanır). Disk doluluğu 15 dk'da bir: eşik (Genel Ayarlar) aşılırsa `Guard.DiskLow`, aynı disk için 24 saatte bir (eşik altına inince sıfırlanır). Aynı modül 1 saatte 5+ hata (AppLog.ErrorWritten) verirse `Guard.ModuleError`, 6 saatte bir.
- **İşlemci / bellek takibi (Engine/Health/ResourceMonitor, sistem nöbeti 30 sn'de bir):** toplam işlemci (GetSystemTimes farkı) ve bellek (GlobalMemoryStatusEx) ölçülür; dakikalık ortalama `ResourceSamples` (şema v10, 30 gün). Genel Ayarlar → İşlemci ve bellek izleme: işlemci eşiği (%90), bellek eşiği (%90), süre (10 dk). Eşik süre boyunca aralıksız aşılırsa `Win.HighCpu` / `Win.HighMemory` (Uyarı + mail): en çok kullanan 8 program (aynı adlılar toplanır, kopya sayısı, hangi kullanıcıların oturumunda) ve oturum başına bellek. Eşik altına inince sıfırlanır, sürerse 6 saatte bir tekrar. Panel'de "İşlemci" ve "Bellek" kutucukları (anlık değer, 24 saat ort. / en yüksek / eşik üstü dakika, 15 dk'lık eğilim grafiği); günlük raporun servis sağlığı bölümünde aynı özet.
- **Panel (ilk tasarım notu):** özet, son olaylar, aktif ban sayısı, servis durumu, lisans durumu, son sağlık skoru.
- **Kurallar:** olay kural tablosu (bölüm 5), eşik ayarları.
- **Mail:** SMTP ayarı, alıcılar, HTML imza (düzenleme + önizleme), test maili.
- **Ban listesi ve beyaz liste:** ekleme, silme, **ban kaldırma** (tek / toplu), manuel ban.
- **Loglar:** olay logu ve uygulama logu; filtreleme (tarih, önem, kaynak, IP, kullanıcı); Excel'e aktarma.
- **Servis yönetimi:** başlat / durdur / yeniden başlat / kaldır / yeniden kur.
- **Güncelleme:** UI'den servis durdurulur → yeni setup çalıştırılır (servis çalışıyorsa setup "önce UI'den durdurun" der) → servis yeniden başlar. DB ve ayarlar aynen kalır.
- **Güvenlik sağlık kontrolü** (bölüm 9).
- **Lisans ekranı:** anahtar girişi, durum, bitiş tarihi.
- **İşlem kaydı (AuditTrail):** UI'de yapılan her işlem (kural değişimi, ban kaldırma, servis durdurma vb.) kaydedilir.
- UI sunucuda RDP oturumunda çalışır; uzaktan yönetim yok.

---

## 9. Güvenlik Sağlık Kontrolü (sadece okur, değiştirmez)

Her madde: **Geçti / Uyarı / Kritik**, neden riskli, **nasıl düzeltilir** (adım adım, komut/script örneği). Otomatik "Düzelt" butonu **yok**. "Görmezden gel" seçeneği not ile eklenebilir.

**Zamanlama:** Günlük otomatik tarama, **varsayılan 20:00** (parametrik), rapor maili parametrik. Yeni kritik bulgu çıkarsa mail. Elle "Şimdi tara" butonu.

**Windows / RDP:** RDP varsayılan port, NLA kapalı, RDP internete açık (firewall kapsamı), **IP kısıtı olmadan herkese açık portlar**, Administrator adı varsayılan/aktif, Guest aktif, hesap kilitleme politikası yok, şifre politikası zayıf, **audit politikası kapalı (Logon failure, Process creation, Object access, Firewall rule change) - Kritik**, Defender kapalı / imzalar eski, Firewall profili kapalı (ban çalışmaz - Kritik), Windows güncelleme eski, event log boyutu küçük, SMBv1 açık, gereksiz yönetici üyeleri, desteklenmeyen Windows sürümü.

**SQL Server:** `sa` aktif / zayıf şifre / şifresi eski, varsayılan port 1433, SQL Browser açık, `xp_cmdshell` / Ole Automation / Ad Hoc Distributed Queries / CLR açık, karma mod, login'lerde `CHECK_POLICY` kapalı, kullanıcı adıyla aynı şifre, gereksiz sysadmin, **login auditing kapalı (failed logins yok) - Kritik**, TRUSTWORTHY DB, linked server, TLS şifreleme kapalı, sürüm / CU eski veya desteği bitmiş, yedek yok / eski, yedekler aynı diskte, TDE açık DB'nin sertifika yedeği yok, servis hesabı fazla yetkili, audit / izleme durumu.

Şifre testi (`PWDCOMPARE`) sonuçlarında şifreler asla loglanmaz, sadece "zayıf şifreli login var" denir.

**Fidye yazılımı erken uyarısı (izleme):** Security 4688 (işlem oluşturma) izlenir; sadece fidye yazılımının şifrelemeden önce çalıştırdığı komutlar olay olur (`Win.RansomwarePrecursor`, Kritik + anında mail): vssadmin / wmic / diskshadow / PowerShell ile gölge kopya silme, wbadmin ile yedek / katalog silme, bcdedit ile kurtarmayı kapatma, wevtutil cl, cipher /w, fsutil usn deletejournal, PowerShell ile Defender kapatma / dışlama ekleme (Engine/Collectors/RansomwareIndicators). Diğer binlerce işlem yok sayılır. Çalışması için "İşlem oluşturma" audit'i ve komut satırı kaydı açık olmalı (Güvenlik Önerileri'nde ayrı madde + script).

**Ek güvenlik maddeleri:** Defender'da geniş dışlamalar (C:\, Temp, Users, ProgramData, .exe / .ps1, powershell.exe... → Kritik), LSA koruması (RunAsPPL), 90 günden uzun kullanılmayan / şifresi süresiz yerel hesaplar (son oturum + profil kullanımı + açık oturumlar), kullanıcı DB'lerinde açık guest.
**Ek performans maddeleri:** SQL bellek baskısı (PLE, hedef max(300, GB/4 x 300) sn; bekleyen bellek talebi), kilitlenme / uzun süredir açık işlem / deadlock sayısı (anlık + SQL açıldığından beri), Instant File Initialization, eski uyumluluk seviyesi (bilgi; ERP onayı gerekir), veri / log / tempdb yerleşimi (bilgi). Parçalanma maddesine "SSD'de etkisi düşük, istatistik daha önemli" notu.
**Windows güncellemeleri:** yama seviyesi ntoskrnl.exe'nin son güncellenmesinden ölçülür (her aylık toplu güncelleme yeniler); güncelleme geçmişindeki küçük platform güncellemeleri yanıltmaz.

**Uygulama (Checks/Security, UI: Öneriler → Güvenlik Önerileri):** Performans Önerileri ile aynı altyapı (tarama, görmezden gelme, YENİ, yeni kritik bulgu olayı, günlük raporda ayrı bölüm; rapor iki taramanın da bitmesini bekler).
- Windows: firewall profilleri (kapalıysa ban çalışmaz → Kritik), audit politikası (AuditQuerySystemPolicy, GUID ile; Logon başarısız / kullanıcı / grup yönetimi kapalıysa Kritik; düzeltme `auditpol` GUID ile, dil bağımsız), RDP (NLA, güvenlik katmanı, şifreleme, port), herkese açık portlar (dinleyen TCP portları + işlem, firewall'da uzak adres "*" izin kuralı; servise bağlı svchost kuralları sayılmaz), yerleşik Administrator (adı / açık), Guest, Administrators üyeleri, şifre + kilitleme politikası (`secedit /export`, sadece okuma), otomatik oturum açma (açık metin şifre → Kritik), WDigest / LmCompatibilityLevel, Defender (WMI MSFT_MpComputerStatus), son Windows toplu güncellemesi (WU geçmişi; Defender imzası ve sürücüler hariç; 45 gün Uyarı, 90 gün Kritik), Windows sürüm desteği, SMBv1, olay logu boyutları (Security 256 / Application 64 / System 32 MB), Remote Registry, uzaktan erişim programları (AnyDesk, TeamViewer... makineye / kullanıcıya kurulu veya çalışan).
- SQL: sürüm desteği, SecurityGuard SQL izlemesi, login auditing (başarısız girişler kapalıysa SQL ban çalışmaz → Kritik), sa (açık / adı / şifre yaşı), zayıf şifreler (boş, kullanıcı adıyla aynı, yaygın liste; parametreyle PWDCOMPARE), CHECK_POLICY kapalı login'ler, ek sysadmin'ler (BUILTIN\Administrators, SQL login'leri), tehlikeli ayarlar (xp_cmdshell, Ole Automation → Kritik; Ad Hoc, CLR, clr strict security, cross db ownership), kimlik doğrulama modu (bilgi), TRUSTWORTHY, linked server'lar, port + ForceEncryption, yedekler (kullanıcı DB 7 gün, sistem DB 30 gün; hiç yoksa Kritik; yedek veriyle aynı diskte → Uyarı), TDE sertifika yedeği, servis hesabı LocalSystem, SQL Browser.

---

## 9.1 Performans Önerileri ve SQL Bakımı

Ayrı UI sayfası; sağlık kontrolüyle aynı altyapı (her madde: Geçti / Uyarı / Kritik, neden, nasıl yapılır, gerekirse hazır script). UI'den elle "Şimdi tara" butonu. Tarama saati parametrik (akşam). Taramanın kendisi sunucuyu yormaz: index ölçümü `LIMITED` modda, DB'ler tek tek, düşük öncelikle.

**Tarama altyapısı (uygulandı, Performans ve ileride Güvenlik Önerileri ortak):**
- `Checks/CheckRunner`: tür başına tek tarama, düşük öncelikli ayrı iş parçacığı. Her gün rapor saatinde (Genel Ayarlar → Rapor saati); servis o saatte kapalıysa aynı gün açılınca kaçırılan tarama yapılır (`checks.lastScheduledDate:<tür>` ayarı). Elle "Şimdi Tara" (İşlem Kaydı'na yazılır); sürerken UI 2 sn'de bir ilerlemeyi gösterir.
- Durumlar: Geçti / Bilgi / Uyarı / Kritik / Kontrol edilemedi. Bir kontrol hata verirse sadece o madde "Kontrol edilemedi" olur. SQL bağlantısı `SET LOCK_TIMEOUT 10000; DEADLOCK_PRIORITY LOW; READ UNCOMMITTED` ile açılır (kimseyi bekletmez); servis kapanırsa SQL komutu iptal edilir.
- Sonuçlar `CheckRuns` + `CheckResults` (şema v5), tür başına son 30 tarama saklanır. "Yeni": bir önceki taramada olmayan veya kötüleşen Uyarı / Kritik.
- **Görmezden gel:** madde notla (not zorunlu) görmezden gelinir (`CheckIgnores`), sayılmaz, mail atılmaz, üstü çizili gösterilebilir, geri alınabilir. İşlem Kaydı'na yazılır.
- **Yeni kritik bulgu** (görmezden gelinmeyen, bir önceki taramada kritik olmayan) tek olay olarak kaydedilir: `Check.NewCritical` (Raporlar grubu, varsayılan Kritik + mail).
- Script'ler sadece gösterilir / kopyalanır; SecurityGuard çalıştırmaz. Silme önerileri (DROP INDEX vb.) bilerek yorum satırı olarak verilir.
- Genel Ayarlar → Performans ve bakım: parçalanma düzenleme eşiği (%5), yeniden oluşturma eşiği (%30, düzenlemeden büyük olmalı), en küçük index boyutu (1000 sayfa).
- Uygulanan SQL maddeleri: sunucu bilgisi, max server memory (RDP kullanıcıları için geniş pay: ≤64 GB'da max(4 GB, %25)), MAXDOP, cost threshold, optimize for ad hoc, tempdb dosyaları, autogrowth, auto shrink / close, VLF (2016 SP2+), log dosyaları (log yedeği yok / şişme), en büyük 30 tablo, parçalanma, kullanılmayan / tekrar eden index, eski istatistik, veri dosyası boş alanı, eksik index, wait'ler, en çok süre harcayan sorgular, diğer bakım / yedek işleri + SQL Agent durumu.
- Uygulanan Windows maddeleri: disk doluluğu ve gecikmesi (5 sn ölçüm), bellek + sanal bellek (commit), işlemci + en çok kullanan işlemler (aynı adlılar toplanır), kopmuş RDP oturumları + süre sınırı ilkesi, son 7 günün kritik hataları (kaynak adı tam eşleşir: Kernel-Power 41, 6008, BugCheck, WHEA, disk, depolama denetleyicisi 129, NTFS, sanal bellek 2004, SQL 823/824/825/833), güç planı, sayfa dosyası, Defender'da SQL hariç tutmaları, temp klasörleri, Windows Update önbelleği, WinSxS (sistem diski %80+ ise), 90 günden eski profiller, tüm kullanıcılarda açılan başlangıç uygulamaları (Windows bileşenleri hariç), gereksiz servisler, sunucuya uygun olmayan uygulamalar, internet gecikmesi (ping + DNS).

**SQL Server önerileri:**
- **En büyük 30 tablo** (boyut, satır sayısı, son okunma/yazılma). Log / History / Temp / Backup / `_old` / tarih ekli kopya gibi tablolar "güvenliyse silinebilir / arşivlenebilir" diye işaretlenir. Silmez; sadece önerir. Kullanım istatistiği SQL yeniden başlayınca sıfırlanır, raporda belirtilir. Tablo içeriği okunmaz.
- **Index:** parçalanma (%5-30 REORGANIZE, %30+ REBUILD), **eksik index önerileri (hazır CREATE INDEX scripti, uygulamak kullanıcıda)**, kullanılmayan index, tekrar eden index, eski istatistikler.
- **Shrink ihtiyacı:** log dosyası şişmiş mi (FULL recovery + log yedeği yok mu), data dosyasında aşırı boş alan var mı.
- **Sunucu ayarları:** max server memory ayarsız, MAXDOP, cost threshold for parallelism, tempdb dosya sayısı, autogrowth yüzde ile, auto shrink / auto close açık, VLF sayısı, en çok beklenen wait tipleri, en yavaş sorgular (özet).
- Sunucuda başka bakım işi (SQL Agent maintenance plan vb.) varsa "aynı iş iki kez yapılıyor" uyarısı.

**Windows / RDP önerileri:**
- Disk doluluğu ve gecikmesi, temp klasörleri (kullanıcı profilleri dahil), eski / kullanılmayan kullanıcı profilleri, Windows Update önbelleği, WinSxS temizliği (DISM).
- Güç planı (Yüksek performans), page file, en çok CPU / RAM kullanan işlemler.
- Kopmuş ama açık kalan RDP oturumları (oturum zaman aşımı önerisi).
- Gereksiz servisler ve başlangıç uygulamaları, şüpheli kurulu uygulamalar (oyun, araç çubuğu vb.). **Print Spooler / yazıcı ile ilgili hiçbir öneri verilmez** (müşteriler yazıcı kullanıyor).
- Antivirüste SQL dosyaları için hariç tutma, son günlerdeki kritik Windows hataları (disk, bellek, beklenmedik kapanma).
- İnternet: otomatik olarak sadece gecikme (ping / DNS); gerçek hız testi UI'de elle buton (bant genişliği tüketir).

**SQL bakımı (servis uygular, parametrik, varsayılan KAPALI):**
Performans modülü "değiştirme yok" ilkesinin tek bilinçli istisnasıdır; kullanıcı UI'den açana kadar sadece öneri verir.
- İşlemler: index REBUILD / REORGANIZE + istatistik güncelleme, **log dosyası shrink (ihtiyaç varsa)**. **Data dosyası shrink otomatik yapılmaz** (index'leri parçalar, rebuild ile sonsuz döngü); sadece önerilir, gerekirse UI'de uyarılı elle buton.
- **Bakım penceresi:** günler + başlangıç saati + en fazla süre (ör. Pazar 01:00, 3 saat). Süre dolunca yeni işleme başlanmaz.
- DB seçimi (sistem DB'leri varsayılan hariç), eşikler (parçalanma %, minimum sayfa sayısı) parametrik.
- Başlamadan önce: disk boş alanı yeterli mi, o an yedek alınıyor mu (varsa bekle).
- Edition'a göre: Enterprise'da `ONLINE = ON`, Standard / Express'te offline (bu yüzden bakım penceresi zorunlu).
- **Deneme modu:** "Bu gece şunlar yapılacak" listesi, hiçbir şey uygulamaz.
- Her işlem (önce / sonra parçalanma, süre, sonuç) loglanır, AuditTrail'e yazılır, günlük maile girer.
- Ayrı modül, kendi uzun timeout'u; servis kapanırsa yarıdaki işlem güvenli iptal edilir; hata güvenlik izlemeyi asla etkilemez.

**Uygulama (Sql/SqlMaintenance, UI: SQL Server → Bakım):**
- Ayarlar `maint.config` (JSON): açık/kapalı (kapalı), deneme modu (açık), günler, başlangıç saati, en fazla süre (15-720 dk, varsayılan 180), işlemler (index, istatistik, log küçültme: kapalı), seçili DB'ler (`INSTANCE|DB`; yeni DB kendiliğinden eklenmez). Açarken gün / DB / işlem zorunlu. Deneme modu kapatılarak açılırken UI ayrıca onay ister. Her değişiklik İşlem Kaydı'na.
- Zamanlama: pencere içindeyken (gece yarısını aşan pencere başladığı güne aittir) her pencerede bir kez (`maint.lastWindow`). Servis pencere ortasında açılırsa kalan sürede çalışır; pencere geçtiyse çalışmaz.
- Elle: "Deneme Yap" (her zaman, sadece okur) ve "Şimdi Uygula" (onaylı, pencere beklenmez, süre sınırı yine geçerli). Kaydedilmemiş ayar varsa önce kaydetme sorulur.
- Akış (instance başına): kendi bağlantısı (havuz dışı, DEADLOCK_PRIORITY LOW, LOCK_TIMEOUT 60 sn) → yedek / geri yükleme / DBCC sürüyorsa bekle (en fazla 30 dk, sonra atla) → her DB: index (en çok kazanç sağlayan önce; bölümlü index'te PARTITION = n), REBUILD için diskte index boyutunun 1,5 katı boş yer yoksa REORGANIZE; Enterprise'da ONLINE, çevrimiçi yapılamayan index çevrimdışı; sonra eski istatistikler (REBUILD edilenler hariç); sonra (açıksa) şişmiş log küçültme (FULL + log yedeği bekliyorsa atlanır).
- Komut zaman aşımı = pencerenin kalanı: süre dolunca süren işlem iptal edilir (SQL geri alır), kalanlar "Atlandı" olarak bir sonraki bakıma kalır.
- Kayıt: `MaintenanceRuns` + `MaintenanceOps` (şema v6, son 200 çalıştırma): önce / sonra parçalanma, süre, sonuç (Yapıldı / Yapılacak (deneme) / Atlandı / Hata / İptal / Gerek yok), not. Olay: `Sql.MaintenanceDone` (Bilgi, mail kapalı) / `Sql.MaintenanceFailed` (Uyarı, mail açık: hatalı / yarıda kesildi).
- Sayfada sunucudaki diğer bakım işleri (SQL Agent) uyarı olarak, edition notu (online REBUILD var / yok) ve eşikler gösterilir.

## 9.2 SQL Yedekleme (parametrik, varsayılan kapalı)

- **Seçilen DB'ler**, belirlenen **gün + saatte** **full yedek** (`BACKUP DATABASE ... WITH CHECKSUM`, edition destekliyorsa `COMPRESSION`).
- Akış: disk boş alanı kontrolü → yedek → `RESTORE VERIFYONLY` → **WinRAR ile şifreli arşiv** → açık `.bak` silinir → eski yedek temizliği → log + günlük mail. **Başarısız yedek = Kritik mail.**
- **WinRAR:** Sunucuda lisanslı WinRAR kurulu olmalı (`Rar.exe` otomatik bulunur, yol elle de verilebilir). Bulunamazsa yedekleme sayfası kilitli ("WinRAR bulunamadı"), sağlık kontrolünde uyarı. Arşiv RAR5 + AES-256, `-hp` ile dosya adları da şifreli. Şifre SQLite'ta şifreli; yönetici UI'de görebilir, "şifreyi güvenli yere not edin" uyarısı.
- **Hedef:** Sadece yerel klasör (parametrik yol). Klasör bizim DB gibi ACL ile korunur, silme / değiştirme denemesi Kritik mail.
- **Saklama:** X günden eski yedekler otomatik silinir (gün sayısı parametrik). Güvenlik şartı: her DB için **son 3 başarılı yedek süre dolsa da silinmez** (yedekler günlerce hata verirse elde yedek kalsın). Silme sadece bizim dosyalarımıza dokunur (`SG_<db>_<yyyyMMdd_HHmm>.rar`), klasördeki başka dosyalara asla.
- Sunucuda başka yedekleme (SQL Agent job, 3. parti) tespit edilirse `COPY_ONLY` önerilir (parametrik), başkasının yedek zinciri bozulmaz.
- Index bakımı ile aynı anda çalışmaz; çakışırsa biri diğerini bekler.
- **Lisans biterse yedekleme de durur** (bölüm 10 kuralı).

**Uygulama (Sql/SqlBackup, UI: SQL Server → Yedekleme):**
- Ayarlar `backup.config` (JSON): açık/kapalı (kapalı), günler (varsayılan her gün), saat (22:00), seçili DB'ler (`INSTANCE|DB`, sistem DB'leri de seçilebilir), hedef klasör (yerel, Windows klasörü olamaz), saklama günü (14), COPY_ONLY (otomatik / her zaman / hiç), Rar.exe yolu (boşsa kayıt defteri / Program Files'tan bulunur). Açarken gün, DB, klasör, WinRAR ve arşiv şifresi zorunlu.
- Arşiv şifresi `backup.password` (DPAPI şifreli). Belirleme (boşsa 24 karakterlik güçlü şifre üretilir, en az 12 karakter) ve gösterme yönetici şifresini tekrar ister, İşlem Kaydı'na yazılır. Şifre değişirse eski arşivler eski şifreyle açılır (UI uyarır).
- Akış (instance başına, DB DB): boş alan (veri dosyası boyutu × 0,6 sıkıştırmalı / × 1, geçici ve hedef diskte) → `BACKUP DATABASE … WITH CHECKSUM, INIT, FORMAT, NAME = N'SecurityGuard', DESCRIPTION = <arşiv yolu>[, COMPRESSION][, COPY_ONLY]` SQL Server'ın kendi yedek klasörüne (InstanceDefaultBackupPath: SQL servis hesabı yazabilir) → `RESTORE VERIFYONLY … WITH CHECKSUM` → `Rar.exe a -ep -ma5 -m1 -hp<şifre>` hedef\<instance>\SG_<db>_<yyyyMMdd_HHmm>.rar → `Rar.exe t` (arşiv testi) → .bak her durumda silinir, hatada yarım arşiv de silinir. Şifre loglanmaz (Rar hata mesajında maskelenir).
- COPY_ONLY "otomatik": son 30 günde SecurityGuard dışında COPY_ONLY olmayan tam yedek varsa (başka araç zinciri yönetiyor) COPY_ONLY alınır.
- Temizlik: hedef\<instance> klasöründe sadece `SG_<db>_<damga>.rar` dosyaları; DB başına en yeni 3 arşiv her durumda kalır, diğerleri saklama gününden eskiyse silinir.
- Zamanlama: seçili günde saat gelince bir kez (`backup.lastSlot`); servis o saatte kapalıysa aynı gün açılınca. Başlatılamazsa (WinRAR / şifre yok) Kritik olay.
- Bakım ile aynı anda çalışmaz (`SqlJobGate`): yedek en fazla 3 saat, bakım pencere sonuna kadar bekler; deneme modundaki bakım beklemez.
- Kayıt `BackupRuns` + `BackupFiles` (şema v9, son 200): adım (YEDEK / DOĞRULAMA / ARŞİV / TEMİZLİK), boyut, süre, sonuç, not. Olay: `Sql.BackupDone` (Bilgi, mail kapalı), `Guard.BackupFailed` (Kritik, mail).
- Günlük raporda "SQL yedekleri" bölümü; seçili DB'lerden 2 günden uzun süredir başarılı yedeği olmayanlar başlığa girer. Güvenlik Önerileri "yedek aynı diskte" kontrolü SecurityGuard yedeklerinde arşiv yoluna bakar.
- Hedef klasörün ACL ile korunması servis koruma adımında yapılacak.
- **Bildirimler (Yedekleme sayfasında):** "Başarılı yedek maili" (`Sql.BackupDone`) ve "Başarısız yedek maili" (`Guard.BackupFailed`) Olay Kuralları'ndaki kuralların kendisidir; sayfadan mail açılıp kapatılır ve alıcılar seçilir (değişiklik anında kurala kaydedilir). Örn. yöneticiye sadece yedek maili gider, güvenlik olayları IT alıcılarına. Alıcı seçilmezse tüm aktif alıcılara, seçilenlerin hepsi pasifse yine tüm aktif alıcılara gider. Başarısız yedek mailini kapatmak onay ister.

**Günlük rapor maili:** Güvenlik ve performans özeti **her gün, tek mailde iki bölüm** (varsayılan 20:00, parametrik).

**Uygulama (Engine/Reports/DailyReport, UI: İzleme → Günlük Rapor):**
- Bölümler: güvenlik olayları (önem sayıları, olay tipine göre özet, son 30 kritik olay), giriş denemeleri ve banlar (en çok deneyen 10 IP, yeni banlar, toplam banlı), performans taraması (görmezden gelinenler hariç kritik / uyarılar, YENİ işaretli), SQL bakımı (dönemdeki çalıştırmalar), servis sağlığı (sürüm / çalışma süresi, olay logu bütünlüğü, mail kuyruğu, firewall ban kuralları, SQL izleme durumu). Bir bölüm hata verirse rapor yine çıkar, o bölümde hata yazar.
- Konu: `[SecurityGuard] Günlük rapor - SUNUCU - tarih: <özet>` (ör. "3 kritik olay, 1 kritik bulgu" veya "sorun yok").
- Zamanlama: rapor saatinde aynı saatte başlayan performans taramasının bitmesi beklenir (en fazla 45 dk). Servis o saatte kapalıysa aynı gün açılınca gönderilir. Dönem: bir önceki zamanlanmış rapordan bu yana (`report.lastPeriodEnd`, en fazla 7 gün).
- Alıcılar / açma-kapama: Olay Kuralları → "Günlük güvenlik ve performans raporu" (izleme kapalıysa / mail kapalıysa / mail ayarı yoksa rapor kaydedilir, gönderilmez; nedeni raporda görünür).
- Her rapor `Reports` tablosunda (şema v8, son 90). UI: geçmiş raporlar, mailde göründüğü gibi önizleme, "Önizle" (kaydetmez / göndermez), "Şimdi Gönder" (zamanlanmış raporun dönemini etkilemez).
- **Otomatik bütünlük kontrolü:** her zamanlanmış raporda HMAC zinciri doğrulanır. Dışarıdan oynama bulunursa `Guard.LogIntegrity` Kritik olayı (aynı durum her gün tekrar olay üretmez, `report.lastIntegrity`).
- **SQLite Sorgu ekranı ile ayrım:** ekrandan EventLog'a dokunan her değişiklik (Writer yolu aynı transaction'da, BEGIN/COMMIT yolu hemen ardından) önce / sonra karşılaştırılır; silinen / değişen kayıtların Id'leri `EventLogAdminChanges` tablosuna (şema v7) HMAC ile imzalı yazılır. Doğrulama bu kayıtları "yönetici tarafından" sayar (oynama değil); imzası olmayan / tutmayan kayıt kabul edilmez. Sondan silinen kayıtlar `sqlite_sequence` ile yakalanır.
- **Mevcut durumu kabul et:** Olay Logu → Bütünlük Kontrolü sorun bulursa yönetici (şifre tekrar sorulur) mevcut sorunları kabul edebilir: eksik Id'ler tek tek "deleted", değişmiş / bağı kopuk satırlar "accepted" olarak imzalanır, İşlem Kaydı'na yazılır. Kabul edilmiş satırın önünden sonradan silinen kayıt, kabulden sonraki değişiklikler ve sondan silmeler yine yakalanır (eski sürümden kalan test silmeleri gibi durumlar için).

---

## 10. Lisanslama

**Model:** Lisansı biz (satıcı) lisans sunucusunda oluştururuz; arayüzde lisans girme / başlatma yoktur. Arayüz sadece durumu gösterir (Sistem → Lisans). Lisans makinenin donanım kimliğine bağlıdır.

**Parçalar:**
- `SecurityGuard.LicenseApi` (ASP.NET Core, .NET 10, aynı solution): minimal API, Dapper + saklı prosedürler, Windows servisi olarak da çalışır (`SecurityGuardLicenseApi`).
- Veritabanı `SecurityGuardLicense` (SQL Server, Windows kimlik doğrulama): `tools/license-db/SecurityGuardLicense.sql` (tekrar çalıştırılabilir). RCSI, Query Store, SIMPLE kurtarma, `LicenseChecks` PAGE sıkıştırmalı; HWID parçaları kalıcı hesaplanan kolon + index (3/4 eşleşme aramasında tarama yok). Aynı makineye tek aktif lisans (filtreli unique index).
- Ortak protokol `SecurityGuard.Shared/Licensing/LicenseProtocol.cs`: istek / cevap / imzalanan metin; hem .NET 4.8 serviste hem .NET 10 API'de aynı dosya derlenir (API'de bağlantılı dosya).

**Donanım kimliği** (`Licensing/HardwareId.cs`): `SG1-AAAAAAAA-BBBBBBBB-CCCCCCCC-DDDDDDDD`. Parçalar: Windows MachineGuid, anakart seri no (yoksa SMBIOS UUID), işlemci kimliği, sistem diski seri no (yoksa birim seri no); her biri SHA-256 özetinin ilk 4 baytı (ham değer dışarı çıkmaz). Sunucu 4 parçanın en az 3'ü tutarsa geçerli sayar (tek donanım değişimi lisansı düşürmez; arayüzde "donanım değişikliği algılandı" yazar).

**Doğrulama** (`Licensing/LicenseManager.cs`):
1. Servis açılışında ve **5 dakikada bir** `POST api/v1/license/validate` (HwId, 16 baytlık rastgele nonce, sürüm, makine adı).
2. Sunucu cevabı **RSA-3072 / SHA-256 / PKCS#1** ile imzalar; imza `Canonical` metin üzerindedir (sürüm, durum, lisans no, müşteri, bitiş, sunucu saati, nonce, HwId, eşleşen parça). Açık anahtar `LicenseProtocol.PublicModulus` içinde gömülü; özel anahtar sadece API sunucusunda DPAPI (makine) ile şifreli (`%ProgramData%\SecurityGuardLicenseApi\signing.key`). İmza / nonce / HwId tutmayan cevap "sunucuya ulaşılamadı" sayılır.
3. Son imzalı cevap SQLite `License` tablosunda DPAPI ile şifreli saklanır; açılışta imzası ve HwId yeniden doğrulanır (DB'de elle değiştirilemez, başka makineye kopyalanamaz).
4. **Çevrimdışı tolerans 7 gün** (son geçerli cevaptan itibaren); 3. günden itibaren günde bir `Guard.LicenseOffline` maili. Saat geri alınarak süre uzatılamaz: görülen en ileri zaman saklanır, sunucuyla konuşunca sıfırlanır.
5. Bitişe 7 / 3 / 1 gün kala `Guard.LicenseExpiring` (her eşik bir kez).
6. **Geçersiz** (lisans yok / süresi doldu / iptal / 7 günden uzun çevrimdışı): `LicenseGate.Active = false` → olay kaydı (EventRecorder), olay işleme, yeni ban, günlük / aylık rapor, yedekleme, bakım, taramalar, SQL izleme, sistem / servis takibi durur. **Mevcut ban ve port kısıtı kuralları firewall'da kalır.** Sadece `Guard.License*` olayları (lisans bitti maili) geçer. Lisans tekrar geçerli olunca yeniden başlatmadan devam eder (`Guard.LicenseRestored`).
7. **Hiç lisans yoksa hiç çalışmaz**: kurulumdan sonra arayüze giriş yapılır, doğrudan Lisans sayfası açılır (donanım kimliği kopyalanır, sunucu adresi ayarlanır). Lisanssızken sadece giriş, Lisans ve Servis (durdur / kaldır) komutları çalışır, diğerleri `Unlicensed` hatası döner. Lisans geçerli olunca ilk kurulum sihirbazı / ana ekran açılır.
8. Lisans sunucusu adresi ayardır (`license.apiUrl`, boşsa `GuardConstants.LicenseDefaultApiUrl`), Lisans sayfasından değiştirilir.

**API uçları** (Swagger: `/swagger`):
- `POST /api/v1/license/validate` — istemci; IP başına dakikada 30 istek.
- Yönetici (`X-Admin-Key` başlığı; ayarlarda anahtarın sadece SHA-256 özeti durur, sabit sürede karşılaştırma; IP başına dakikada 120):
  `GET /api/v1/admin/licenses?search=`, `GET /licenses/{id}`, `POST /licenses` (HwId, Customer, ExpiresUtc, Note; aynı makinede aktif lisans varsa 409), `PUT /licenses/{id}` (süre uzatma / ad / not), `POST /licenses/{id}/revoke`, `POST /licenses/{id}/restore`, `GET /licenses/{id}/checks`, `GET /unknown-machines?days=7` (lisanssız doğrulama yapan makineler: yeni kurulumların HWID'si buradan da alınır), `POST /purge?keepDays=`.
- Doğrulama kayıtları günde bir temizlenir (`Purge:KeepDays`, varsayılan 180 gün). Yönetici işlemleri `AdminAudit` tablosunda.

**Sunucu kurulumu (bir kez):**
1. `sqlcmd -S . -E -I -i tools\license-db\SecurityGuardLicense.sql -f 65001`
2. `SecurityGuard.LicenseApi.exe --generate-key` → çıkan modulus `LicenseProtocol.PublicModulus`'a yazılır ve SecurityGuard yeniden derlenir (anahtar değişirse eski sürümler yeni sunucuyu tanımaz; anahtar dosyası yedeklenmeli).
3. `SecurityGuard.LicenseApi.exe --generate-admin-key` → anahtar saklanır, özeti `appsettings.json → Admin:KeyHashes`.
4. Servis hesabının SQL'de `LicenseApiRole` üyesi bir login'i olmalı (Windows kimlik doğrulama). İnternete açılacaksa HTTPS (ters vekil / Kestrel sertifikası) kullanılmalı: yönetici anahtarı düz HTTP'de gönderilmemeli. Cevaplar imzalı olduğu için doğrulama ucu HTTP'de de sahte cevap kabul etmez.

---

## 11. Hata Yönetimi, Loglama, Performans

**Servis asla çökmez:**
- Her modül kendi izole döngüsünde; biri çökse diğerleri çalışır.
- Her döngü turu, her olay işleme ve her dış çağrı (SQL, SMTP, firewall, API, dosya, registry) try/catch içinde.
- `AppDomain.UnhandledException`, `TaskScheduler.UnobservedTaskException`, `OnStart` / `OnStop` koruması.
- Modül üst üste hata verirse artan bekleme (5 sn, 30 sn, 2 dk, 10 dk), tek alarm maili.
- Bağlantılar `using`, kuyruklar sınırlı boyutlu, kısa timeout'lar.

**İki ayrı log:**
| Log | İçerik | Yer |
|---|---|---|
| Güvenlik olay logu | Yakalanan olaylar, ban, mail (HMAC zincirli) | SQLite `EventLog` |
| Uygulama logu | Her işlem, her hata, stack trace, lisans kontrolü | Günlük düz metin dosya (`Logs\app_yyyyMMdd.log`) + SQLite özet |

Seviyeler Debug / Info / Warn / Error / Fatal (UI'den ayarlanır). Aynı hata tekrarları tek satıra sıkıştırılır. Log yazma hatası servisi asla etkilemez.

**Performans:** `EventLogWatcher` ile anlık abonelik (polling yok), SQLite tek yazıcı + kuyruk + toplu yazma + WAL, sayaçlar bellekte, mail asenkron, baseline diff aralıklı.

---

## 12. SQLite Şeması (taslak)

| Tablo | İçerik |
|---|---|
| `Settings` | Genel ayarlar, eşikler, tema, SMTP (şifreli) |
| `AdminUser` | Tek yönetici (PBKDF2 hash, salt, kilit durumu) |
| `SqlInstances` | Instance, sürüm, edition, port, bağlantı bilgisi (şifreli) |
| `EventRules` | Olay tipi, İzle / Logla / Mail / Önem / Alıcılar / Ban / Eşik |
| `Recipients` | Mail alıcıları |
| `EventLog` | Olaylar (UTC zaman, kaynak, kim, IP, detay, önceki HMAC) |
| `EventBookmarks` | Kanal başına son okunan olay |
| `LoginAttempts` | RDP / SQL giriş sayaçları (IP, kullanıcı, protokol) |
| `BannedIps` | Kalıcı ban (IP, sebep, tarih, kural adı, firewall kural no) |
| `Whitelist` | Banlanmayacak IP / aralıklar (sistem kayıtları silinemez) |
| `CanaryFolders`, `CanaryFiles` | Tuzak dosya klasörleri (paylaşım / yedek / elle) ve konan dosyalar (yol, SHA-256) |
| `PortRestrictions` | Port IP kısıtları (port, protokol, izinli adresler, onay bekleme / geri alma noktası) |
| `Baselines` | Diff için anlık görüntüler |
| `CheckResults`, `CheckIgnores` | Sağlık kontrol sonuçları, görmezden gelme notları |
| `License` | Lisans belgesi (şifreli), son doğrulama |
| `MailQueue` | Gönderim kuyruğu ve yeniden deneme |
| `AuditTrail` | UI işlem kaydı |

**Şema versiyonu:** `SchemaVersion` tablosu. Tablolar kodla otomatik oluşur; yeni sürüm kurulunca servis eksik tablo / kolonları kendisi ekler (migration). Elle tablo açmak için SQLite sorgu ekranı da kullanılabilir.

**Konum: her şey programın kendi klasöründe** (ProgramData / Temp gibi dış yerlere hiçbir şey yazılmaz):
```
C:\Program Files\SecurityGuard\
├─ Service\
├─ UI\
├─ Database\guard.db      (WAL; yıllık arşiv: guard_YYYY.db)
├─ Database\Backup\       (DB'nin kendi yedekleri)
└─ Logs\                  (uygulama logu)
```
Setup kaldırma / güncellemede `Database\` ve `Logs\` asla silinmez.

---

## 13. Solution Yapısı

```
SecurityGuard.sln
├─ SecurityGuard.Shared      ortak veri tanımları (iş mantığı, SQLite, DevExpress yok)
│  ├─ Contracts\             pipe istek / cevap mesajları
│  ├─ Dto\                   ban kaydı, kural satırı, log satırı, lisans durumu...
│  ├─ Enums\                 Önem, Protokol...
│  └─ GuardConstants.cs
├─ SecurityGuardService      tüm iş mantığı
│  ├─ Core\                  Data (SQLite), Security (DPAPI / PBKDF2 / HMAC), Logging, Models
│  ├─ Collectors\            Windows\, Sql\
│  ├─ Engine\                Rules\, Ban\, Firewall\
│  ├─ Mail\
│  ├─ Licensing\
│  ├─ Checks\                Windows\, Sql\
│  └─ Ipc\                   pipe sunucusu
└─ SecurityGuardUI           sadece ekranlar, DB'ye dokunmaz
   ├─ Forms\, Modules\, Ipc\ (pipe istemcisi), Helpers\

Çıktı:  bin\<Debug|Release>\SecurityGuard\Service\  ve  ...\UI\
Kurulum: C:\Program Files\SecurityGuard\Service\ ve ...\UI\

SecurityGuard.LicenseApi     (ayrı proje, en son: ASP.NET Core aktivasyon API + heartbeat)
```

Akış: **UI ↔ named pipe ↔ Service ↔ SQLite / Firewall / SQL Server / Event Log / SMTP**

---

## 14. Geliştirme Ortamı

- Test: Windows Server + SQL Server kurulu sanal makine (snapshot ile), kullanıcı hazırlar.
- Servis Visual Studio'dan F5 ile **konsol modunda** çalıştırılabilir (kurmadan).
- Servis koruması (DACL, durdurulamama, klasör ACL) **Debug'da kapalı, Release'te açık**.

## 15. Yazma Sırası

1. **Core:** SQLite (şema, tek yazıcı kuyruk, WAL), şifreleme (DPAPI, PBKDF2, HMAC zinciri), uygulama logu, modeller
2. **Servis iskeleti:** modül altyapısı, hata izolasyonu, pipe sunucusu + Shared kontratları
3. **UI iskeleti:** giriş / ilk yönetici, ana form, tema, pipe istemcisi
4. **Collectors:** Windows olayları (bookmark ile), SQL (Audit + baseline diff)
5. **Engine:** kural motoru, ban motoru, firewall; **Mail** kuyruğu
6. **UI modülleri:** kurallar, ban / beyaz liste, loglar, mail, servis yönetimi
7. **Checks:** sağlık kontrolü, performans önerileri, SQL bakımı, SQL yedekleme + günlük rapor
8. **Servis koruma:** DACL, klasör ACL / SACL, yedek
9. **Licensing** (sahte doğrulayıcı ile), Setup (Inno Setup), en son Lisans API'si
