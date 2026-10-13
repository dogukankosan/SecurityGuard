<#
    SecurityGuard VM test scripti
    - SADECE TEST VM'İNDE çalıştırın (gerçek sunucuda değil).
    - Yönetici PowerShell ile:  powershell -ExecutionPolicy Bypass -File C:\SecurityGuard\VmTest.ps1
    - Her bölümden önce sorar (E/H). Her adımdan sonra arayüzde neye bakacağınızı yazar.
    - Oluşturduğu her şeyi (test kullanıcısı, görev, firewall kuralı, SQL login / DB) kendisi siler.
#>

$ErrorActionPreference = 'Stop'
$script:Results = New-Object System.Collections.Generic.List[string]

function Title($t) { Write-Host ""; Write-Host ("=" * 70) -ForegroundColor Cyan; Write-Host "  $t" -ForegroundColor Cyan; Write-Host ("=" * 70) -ForegroundColor Cyan }
function Ok($t)    { Write-Host "  [OK]   $t" -ForegroundColor Green;  $script:Results.Add("[OK]   $t") }
function Bad($t)   { Write-Host "  [HATA] $t" -ForegroundColor Red;    $script:Results.Add("[HATA] $t") }
function Info($t)  { Write-Host "  $t" -ForegroundColor Gray }
function Look($t)  { Write-Host "  >> Arayüzde bakın: $t" -ForegroundColor Yellow; $script:Results.Add("  bakılacak: $t") }
function Ask($q)   { $a = Read-Host "  $q (E/H)"; return ($a -match '^[eEyY]') }
function Step($name, [scriptblock]$body) { try { & $body } catch { Bad "$name -> $($_.Exception.Message)" } }

# ---------------------------------------------------------------- 0. Ön kontrol
Title "0. Ön kontrol"
if (-not ([Security.Principal.WindowsPrincipal][Security.Principal.WindowsIdentity]::GetCurrent()).IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)) {
    Write-Host "Bu script yönetici PowerShell ile çalıştırılmalı." -ForegroundColor Red; exit 1
}

Step "Servis" {
    $svc = Get-CimInstance Win32_Service -Filter "Name='SecurityGuard'"
    if (-not $svc) { Bad "SecurityGuard servisi kurulu değil"; return }
    if ($svc.State -eq 'Running') { Ok "SecurityGuard servisi çalışıyor ($($svc.StartName), $($svc.PathName))" } else { Bad "SecurityGuard servisi durumu: $($svc.State)" }
    if ($svc.StartName -ne 'LocalSystem') { Bad "Servis LocalSystem ile çalışmıyor: $($svc.StartName) (Security log okunamaz, firewall yazılamaz)" }
    $exe = if ($svc.PathName -match '^"([^"]+)"') { $Matches[1] } else { ($svc.PathName -split '\.exe', 2)[0] + '.exe' }
    $script:Root = Split-Path (Split-Path $exe)
    Info "Kurulum klasörü: $script:Root"
}

Step "Firewall" {
    $off = Get-NetFirewallProfile | Where-Object { -not $_.Enabled }
    if ($off) { Bad "Kapalı firewall profili var: $($off.Name -join ', ') (ban çalışmaz)" } else { Ok "Firewall profilleri açık" }
}

Step "Audit politikası (Windows olayları için)" {
    foreach ($sub in 'Logon', 'User Account Management', 'Security Group Management', 'Other Object Access Events', 'Audit Policy Change') {
        $line = (auditpol /get /subcategory:"$sub" 2>$null | Select-String $sub | Select-Object -First 1)
        if ($line) { Info ("auditpol: " + ($line.ToString().Trim() -replace '\s{2,}', ' -> ')) }
    }
    Info "('No Auditing' görünen alt kategorinin olayları Windows'ta hiç yazılmaz, SecurityGuard da göremez.)"
}

Step "Servis logu" {
    if ($script:Root) {
        $log = Get-ChildItem (Join-Path $script:Root 'Logs') -File -ErrorAction SilentlyContinue | Sort-Object LastWriteTime -Descending | Select-Object -First 1
        if ($log) {
            $errs = Select-String -Path $log.FullName -Pattern 'HATA|ERROR|izlenemiyor|yetki' | Select-Object -Last 8
            if ($errs) { Info "Son logda dikkat çeken satırlar ($($log.Name)):"; $errs | ForEach-Object { Info ("   " + $_.Line) } }
            else { Ok "Son servis logunda hata yok ($($log.Name))" }
        }
    }
}

# ---------------------------------------------------------------- 1. Windows kullanıcı / grup / görev
Title "1. Windows olayları (test kullanıcısı, Administrators, görev, servis)"
if (Ask "Test kullanıcısı 'sg_test_user' oluşturulup Administrators'a eklensin, görev ve servis oluşturulup silinsin mi?") {
    $pw = ConvertTo-SecureString ("Sg!" + [Guid]::NewGuid().ToString('N').Substring(0, 12) + "a1") -AsPlainText -Force
    Step "Kullanıcı" {
        New-LocalUser -Name sg_test_user -Password $pw -Description "SecurityGuard test" | Out-Null
        Add-LocalGroupMember -Group (Get-LocalGroup -SID 'S-1-5-32-544').Name -Member sg_test_user
        Disable-LocalUser -Name sg_test_user
        Ok "sg_test_user oluşturuldu, Administrators'a eklendi, devre dışı bırakıldı"
        Look "Olay Logu → 'Yeni Windows kullanıcısı' (Kritik), 'Administrators grubuna üye eklendi' (Kritik), 'Kullanıcı etkinleştirildi / devre dışı'. Kritikler için mail gelmeli."
    }
    Step "Zamanlanmış görev" {
        $action = New-ScheduledTaskAction -Execute 'cmd.exe' -Argument '/c echo sg'
        Register-ScheduledTask -TaskName 'sg_test_task' -Action $action -Description 'SecurityGuard test' | Out-Null
        Unregister-ScheduledTask -TaskName 'sg_test_task' -Confirm:$false
        Ok "sg_test_task oluşturuldu ve silindi"
        Look "Olay Logu → 'Zamanlanmış görev oluşturuldu' (sadece 'Other Object Access Events' audit'i açıksa gelir)"
    }
    Step "Servis kurulumu" {
        sc.exe create sg_test_svc binPath= "C:\Windows\System32\cmd.exe /c exit" start= demand | Out-Null
        sc.exe delete sg_test_svc | Out-Null
        Ok "sg_test_svc servisi kuruldu ve silindi"
        Look "Olay Logu → 'Yeni Windows servisi kuruldu: sg_test_svc' (Kritik)"
    }
    Step "Temizlik" {
        Remove-LocalUser -Name sg_test_user
        Ok "sg_test_user silindi"
    }
}

# ---------------------------------------------------------------- 2. Firewall kural değişikliği
Title "2. Firewall kural değişikliği"
if (Ask "Test firewall kuralı 'SG_TEST_RULE' (3390 portu) eklenip silinsin mi?") {
    Step "Firewall kuralı" {
        New-NetFirewallRule -DisplayName 'SG_TEST_RULE' -Direction Inbound -Protocol TCP -LocalPort 3390 -Action Allow | Out-Null
        Start-Sleep 1
        Remove-NetFirewallRule -DisplayName 'SG_TEST_RULE'
        Ok "SG_TEST_RULE eklendi ve silindi"
        Look "Olay Logu → 'Firewall kuralı eklendi: SG_TEST_RULE' ve '... silindi' (Kritik)"
    }
}

# ---------------------------------------------------------------- 3. Başarısız giriş sayımı (yerel)
Title "3. Başarısız giriş sayımı"
Info "Not: iç ağ / localhost IP'leri sayılır ama ASLA banlanmaz (koddaki sabit koruma)."
Info "Gerçek ban testi için dış (public) IP'den RDP denemesi gerekir."
if (Ask "Yerelden 5 hatalı ağ girişi (net use \\localhost\IPC$, kullanıcı sg_fake) denensin mi?") {
    Step "Hatalı giriş" {
        for ($i = 1; $i -le 5; $i++) { cmd /c "net use \\localhost\IPC$ /user:sg_fake YanlisSifre$i >nul 2>&1" | Out-Null; cmd /c "net use \\localhost\IPC$ /delete >nul 2>&1" | Out-Null }
        $n = (Get-WinEvent -FilterHashtable @{ LogName = 'Security'; Id = 4625; StartTime = (Get-Date).AddMinutes(-2) } -ErrorAction SilentlyContinue | Measure-Object).Count
        if ($n -gt 0) { Ok "Windows'ta $n adet 4625 yazıldı" } else { Bad "Windows 4625 yazmadı (Logon audit'i 'Failure' kapalı olabilir)" }
        Look "Ban Listesi → Giriş Denemeleri: 127.0.0.1 veya ::1, kullanıcı sg_fake, 'Korunan (banlanmaz)'. Olay Logu → 'RDP başarısız giriş (dış IP)' satırları."
    }
}

# ---------------------------------------------------------------- 4. Ban + firewall onarımı
Title "4. Ban ve firewall kuralı onarımı"
Info "Önce arayüzde: Ban Listesi → 'IP Banla...' → 203.0.113.50 (test IP'si, gerçek değil) → Banla."
if (Ask "203.0.113.50'yi arayüzden banladınız mı? (E ile devam: firewall kuralı kontrol edilip silinecek, onarım test edilecek)") {
    Step "Ban kuralı" {
        $rule = Get-NetFirewallRule -DisplayName 'SecurityGuard-Block-*' -ErrorAction SilentlyContinue
        if (-not $rule) { Bad "SecurityGuard-Block kuralı yok (ban firewall'a yazılamamış)"; return }
        $addr = ($rule | Get-NetFirewallAddressFilter).RemoteAddress
        if ($addr -contains '203.0.113.50') { Ok "Firewall'da $($rule[0].DisplayName) → 203.0.113.50 var" } else { Bad "Kuralda 203.0.113.50 yok: $addr" }
        Look "Olay Logu → 'IP banlandı' (Kritik) + mail"

        Remove-NetFirewallRule -DisplayName 'SecurityGuard-Block-*'
        Ok "Ban kuralı elle silindi (saldırgan simülasyonu)"
        Read-Host "  Şimdi arayüzde Ban Listesi → 'Firewall Kontrol' butonuna basın, sonra Enter"
        if (Get-NetFirewallRule -DisplayName 'SecurityGuard-Block-*' -ErrorAction SilentlyContinue) { Ok "Kural geri oluşturuldu" } else { Bad "Kural geri gelmedi" }
        Look "Olay Logu → 'Ban firewall kuralı silindi / değiştirildi' (Kritik) + mail. Bitince 203.0.113.50'nin banını arayüzden kaldırın."
    }
}

# ---------------------------------------------------------------- 5. SQL Server
Title "5. SQL Server"
$sqlInstance = Read-Host "  SQL instance (varsayılan için Enter = localhost, isimli için localhost\ADI; atlamak için 'h')"
if ($sqlInstance -notmatch '^[hH]$') {
    if ([string]::IsNullOrWhiteSpace($sqlInstance)) { $sqlInstance = 'localhost' }
    Add-Type -AssemblyName System.Data
    function Sql($sql, [switch]$Scalar) {
        $c = New-Object System.Data.SqlClient.SqlConnection("Server=$sqlInstance;Database=master;Integrated Security=true;Connect Timeout=10;Application Name=SG_VmTest")
        $c.Open(); try { $cmd = $c.CreateCommand(); $cmd.CommandText = $sql; $cmd.CommandTimeout = 60; if ($Scalar) { return $cmd.ExecuteScalar() } else { [void]$cmd.ExecuteNonQuery() } } finally { $c.Dispose() }
    }
    function FailLogin($user) {
        $c = New-Object System.Data.SqlClient.SqlConnection("Server=$sqlInstance;Database=master;User ID=$user;Password=YanlisSifre_123;Connect Timeout=5")
        try { $c.Open() } catch { } finally { $c.Dispose() }
    }

    Step "Bağlantı" { $v = Sql "SELECT @@VERSION" -Scalar; Ok ("SQL'e Windows hesabıyla bağlanıldı: " + ($v -split "`n")[0]) }

    Step "Audit durumu" {
        $st = Sql "SELECT TOP 1 status_desc FROM sys.dm_server_audit_status WHERE name = 'SecurityGuard_Audit'" -Scalar
        if ($st -eq 'STARTED') { Ok "SecurityGuard_Audit çalışıyor" } else { Bad "SecurityGuard_Audit çalışmıyor/kurulu değil ($st). Arayüzde SQL Bağlantısı → 'İzlemeyi Kur'" }
        $la = Sql "DECLARE @v int; EXEC master.dbo.xp_instance_regread N'HKEY_LOCAL_MACHINE', N'Software\Microsoft\MSSQLServer\MSSQLServer', N'AuditLevel', @v OUTPUT; SELECT @v" -Scalar
        if ($la -eq 2 -or $la -eq 3) { Ok "SQL 'login auditing' başarısız girişleri logluyor (AuditLevel=$la)" } else { Bad "SQL login auditing başarısız girişleri loglamıyor (AuditLevel=$la) → 18456 gelmez" }
    }

    if (Ask "Hatalı SQL girişleri denensin mi (sg_fake x3, securityGuardWar x2 - yerel)?") {
        Step "SQL hatalı giriş" {
            1..3 | ForEach-Object { FailLogin 'sg_fake' }
            1..2 | ForEach-Object { FailLogin 'securityGuardWar' }
            Ok "5 hatalı SQL girişi denendi"
            Look "Ban Listesi → Giriş Denemeleri: SQL, sg_fake, 3 başarısız, 'Korunan'. securityGuardWar'ın yerel denemeleri HİÇ sayılmamalı."
        }
    }

    if (Ask "SQL Audit olayları test edilsin mi (sg_test_login oluştur / şifre / sysadmin / sil, sg_test_db oluştur / sil)?") {
        Step "SQL audit olayları" {
            $p = "Sg!" + [Guid]::NewGuid().ToString('N').Substring(0, 12)
            Sql "CREATE LOGIN [sg_test_login] WITH PASSWORD = N'$p', CHECK_POLICY = OFF"
            Sql "ALTER LOGIN [sg_test_login] WITH PASSWORD = N'${p}X'"
            Sql "ALTER SERVER ROLE [sysadmin] ADD MEMBER [sg_test_login]"
            Sql "ALTER SERVER ROLE [sysadmin] DROP MEMBER [sg_test_login]"
            Sql "DROP LOGIN [sg_test_login]"
            Sql "CREATE DATABASE [sg_test_db]"
            Sql "DROP DATABASE [sg_test_db]"
            Ok "Login ve veritabanı işlemleri yapıldı (hepsi silindi)"
            Start-Sleep 3
            $n = (Get-WinEvent -FilterHashtable @{ LogName = 'Application'; Id = 33205; StartTime = (Get-Date).AddMinutes(-2) } -ErrorAction SilentlyContinue | Measure-Object).Count
            if ($n -gt 0) { Ok "Application log'a $n adet SQL audit (33205) kaydı düştü" } else { Bad "33205 kaydı yok (audit kurulu/çalışır değil)" }
            Look "Olay Logu (grup: SQL Server) → login oluşturuldu, şifre değişti, sysadmin'e eklendi/çıkarıldı, login silindi, veritabanı oluşturuldu, VERİTABANI SİLİNDİ (Kritik)."
            Look "Eksik / yanlış eşlenen olay varsa ekran görüntüsü atın + aşağıdaki dosyayı gönderin."
            $dump = Join-Path $env:TEMP 'sg_sql_audit_ornek.txt'
            Get-WinEvent -FilterHashtable @{ LogName = 'Application'; Id = 33205; StartTime = (Get-Date).AddMinutes(-5) } -ErrorAction SilentlyContinue |
                ForEach-Object { "----- " + $_.TimeCreated; $_.Message } | Out-File $dump -Encoding UTF8
            Info "Ham audit kayıtları: $dump"
        }
    }

    if (Ask "Audit durdurma testi yapılsın mı (SecurityGuard_Audit STATE=OFF, servis geri açmalı)?") {
        Step "Audit nöbeti" {
            Sql "ALTER SERVER AUDIT [SecurityGuard_Audit] WITH (STATE = OFF)"
            Ok "SecurityGuard_Audit durduruldu (saldırgan simülasyonu). 3 dakikaya kadar bekleniyor..."
            $back = $false
            for ($i = 0; $i -lt 36 -and -not $back; $i++) {
                Start-Sleep 5
                $back = (Sql "SELECT TOP 1 status_desc FROM sys.dm_server_audit_status WHERE name = 'SecurityGuard_Audit'" -Scalar) -eq 'STARTED'
            }
            if ($back) { Ok "Servis audit'i kendiliğinden yeniden başlattı ($(($i) * 5) sn)" } else { Bad "Audit 3 dakikada geri açılmadı (Genel Ayarlar → 'Audit durdurulursa yeniden başlat' açık mı? Periyot 5 dk olabilir)" }
            Look "Olay Logu → 'SQL izleme (Audit) durduruldu / silindi' (Kritik) + mail. İşlem Kaydı → 'SQL izleme otomatik yeniden başlatıldı'."
        }
    }
}

# ---------------------------------------------------------------- 6. Log temizleme
Title "6. Olay logu temizleme"
if (Ask "'Windows PowerShell' olay logu temizlensin mi (System 104 olayı üretir; Security log'a dokunulmaz)?") {
    Step "Log temizleme" {
        wevtutil cl "Windows PowerShell"
        Ok "'Windows PowerShell' logu temizlendi"
        Look "Olay Logu → 'Windows olay logu temizlendi: Windows PowerShell' (Kritik) + mail"
    }
}

# ---------------------------------------------------------------- Özet
Title "Özet"
$script:Results | ForEach-Object { if ($_ -like '`[HATA*') { Write-Host $_ -ForegroundColor Red } elseif ($_ -like '`[OK*') { Write-Host $_ -ForegroundColor Green } else { Write-Host $_ -ForegroundColor Yellow } }
$out = Join-Path $env:TEMP ("sg_vmtest_" + (Get-Date -Format 'yyyyMMdd_HHmm') + ".txt")
$script:Results | Out-File $out -Encoding UTF8
Write-Host ""
Write-Host "Sonuç dosyası: $out" -ForegroundColor Cyan
Write-Host "Bunu + servis logunu (Logs klasörü) + varsa sg_sql_audit_ornek.txt dosyasını gönderin." -ForegroundColor Cyan
