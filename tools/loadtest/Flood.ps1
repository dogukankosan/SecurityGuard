# SecurityGuard yük testi - GERÇEK olay seli (VM'de, YÖNETİCİ PowerShell ile).
# Sunucunun kendisinden (localhost) yanlış şifreyle ağ girişi (Security 4625) ve SQL girişi (18456) dener.
# Localhost korunan adres olduğu için BAN OLUŞMAZ; amaç servisin olayları kaçırmadan, makul CPU/RAM ile işlemesini görmek.
# Denenen kullanıcı adları var olmayan "sgload_*" hesaplarıdır (gerçek hesap kilitlenmez).
#
#   .\Flood.ps1 -Seconds 300 -Parallel 8
#
param([int]$Seconds = 300, [int]$Parallel = 8, [string]$SqlServer = "localhost")

$ErrorActionPreference = "Continue"
$end = (Get-Date).AddSeconds($Seconds)
$svc = Get-Process -Name SecurityGuardService -ErrorAction SilentlyContinue
if (-not $svc) { Write-Host "SecurityGuardService çalışmıyor." -ForegroundColor Red; exit 1 }

$jobs = @()
for ($p = 0; $p -lt $Parallel; $p++) {
    # Ağ girişi (SMB → Security 4625, giriş tipi 3)
    $jobs += Start-Job -ArgumentList $end, $p -ScriptBlock {
        param($end, $p)
        $n = 0
        while ((Get-Date) -lt $end) {
            $null = cmd /c "net use \\127.0.0.1\IPC$ /user:sgload_$p`_$($n % 50) YanlisSifre123 2>&1"
            $n++
        }
        "win:$n"
    }
    # SQL girişi (18456)
    $jobs += Start-Job -ArgumentList $end, $p, $SqlServer -ScriptBlock {
        param($end, $p, $server)
        $n = 0
        while ((Get-Date) -lt $end) {
            try {
                $c = New-Object System.Data.SqlClient.SqlConnection("Server=$server;User ID=sgload_$p;Password=Yanlis;Connect Timeout=3;Pooling=false")
                $c.Open(); $c.Close()
            } catch { }
            $n++
        }
        "sql:$n"
    }
}

# Servisin CPU / RAM'i (5 sn'de bir)
$samples = @()
$prevCpu = $svc.TotalProcessorTime.TotalSeconds; $prevT = Get-Date
while ((Get-Date) -lt $end) {
    Start-Sleep -Seconds 5
    $svc.Refresh()
    $now = Get-Date
    $cpu = ($svc.TotalProcessorTime.TotalSeconds - $prevCpu) / ($now - $prevT).TotalSeconds / [Environment]::ProcessorCount * 100
    $prevCpu = $svc.TotalProcessorTime.TotalSeconds; $prevT = $now
    $samples += [pscustomobject]@{ Cpu = $cpu; RamMb = $svc.PrivateMemorySize64 / 1MB }
    Write-Host ("{0:HH:mm:ss}  servis CPU %{1,5:N1}   RAM {2,6:N1} MB" -f $now, $cpu, ($svc.PrivateMemorySize64 / 1MB))
}

$counts = $jobs | Wait-Job | Receive-Job
$jobs | Remove-Job
$win = ($counts | Where-Object { $_ -like "win:*" } | ForEach-Object { [int]$_.Substring(4) } | Measure-Object -Sum).Sum
$sql = ($counts | Where-Object { $_ -like "sql:*" } | ForEach-Object { [int]$_.Substring(4) } | Measure-Object -Sum).Sum
$summary = @(
    ("Süre: $Seconds sn, paralel: $Parallel"),
    ("Üretilen ağ girişi denemesi: {0}  ({1:N1}/sn)" -f $win, ($win / $Seconds)),
    ("Üretilen SQL girişi denemesi: {0}  ({1:N1}/sn)" -f $sql, ($sql / $Seconds)),
    ("Servis CPU ort. %{0:N1}, en yüksek %{1:N1} (çekirdek sayısı {2})" -f ($samples | Measure-Object Cpu -Average).Average, ($samples | Measure-Object Cpu -Maximum).Maximum, [Environment]::ProcessorCount),
    ("Servis RAM ilk {0:N1} MB, son {1:N1} MB, en yüksek {2:N1} MB" -f $samples[0].RamMb, $samples[-1].RamMb, ($samples | Measure-Object RamMb -Maximum).Maximum),
    ("Başlangıç (UTC): {0:yyyy-MM-ddTHH:mm:ss}" -f $end.AddSeconds(-$Seconds).ToUniversalTime())
)
$summary | ForEach-Object { Write-Host $_ -ForegroundColor Cyan }
$out = Join-Path $PSScriptRoot ("Flood_{0:yyyyMMdd_HHmm}.txt" -f (Get-Date))
$summary | Set-Content -Path $out -Encoding UTF8
Write-Host "Sonuç: $out"
