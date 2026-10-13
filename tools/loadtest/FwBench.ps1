# SecurityGuard yük testi - Windows Firewall yazma hızı (VM'de, YÖNETİCİ PowerShell ile çalıştırın).
# Sadece adı "SGLoadTest-" ile başlayan GEÇİCİ kurallar oluşturur ve sonunda siler.
# Adresler RFC 2544 test aralığı 198.18.0.0/15 (gerçek internette kullanılmaz); kural hiçbir gerçek trafiği etkilemez.
# SecurityGuard servisinin kurallarına (SecurityGuard-Block-*, SecurityGuard-Restrict-*) dokunulmaz.

$ErrorActionPreference = "Stop"
$policy = New-Object -ComObject HNetCfg.FwPolicy2
function Addr([int]$n) { "198.18.{0}.{1}" -f ([math]::Floor($n / 254) % 256), ($n % 254 + 1) }

function Remove-Test {
    foreach ($r in @($policy.Rules | Where-Object { $_.Name -like "SGLoadTest-*" })) { $policy.Rules.Remove($r.Name) }
}
Remove-Test

$results = @()
try {
    # 1) Bir kuralın her ban'da yeniden yazılması: 1, 100, 250, 500, 750, 1000 adresle yazma süresi
    $rule = New-Object -ComObject HNetCfg.FWRule
    $rule.Name = "SGLoadTest-001"; $rule.Grouping = "SGLoadTest"; $rule.Protocol = 256; $rule.Direction = 1; $rule.Action = 0
    $rule.Profiles = 0x7FFFFFFF; $rule.RemoteAddresses = (Addr 0); $rule.Enabled = $true
    $policy.Rules.Add($rule)
    $existing = $policy.Rules.Item("SGLoadTest-001")
    foreach ($size in 1, 100, 250, 500, 750, 1000) {
        $list = (0..($size - 1) | ForEach-Object { Addr $_ }) -join ","
        $sw = [Diagnostics.Stopwatch]::StartNew()
        for ($i = 0; $i -lt 10; $i++) { $existing.RemoteAddresses = $list }
        $ms = $sw.Elapsed.TotalMilliseconds / 10
        $results += "Kural yazma ($size adres): {0:N1} ms" -f $ms
        Write-Host $results[-1]
    }

    # 2) Dağıtık saldırı benzetimi: 1000 IP art arda banlanıyor (her ban'da kural tümüyle yeniden yazılır)
    $sw = [Diagnostics.Stopwatch]::StartNew()
    $acc = New-Object System.Collections.Generic.List[string]
    for ($i = 0; $i -lt 1000; $i++) { $acc.Add((Addr (2000 + $i))); $existing.RemoteAddresses = ($acc -join ",") }
    $results += "1000 ban art arda (her biri kuralı yeniden yazar): {0:N1} sn" -f $sw.Elapsed.TotalSeconds
    Write-Host $results[-1]

    # 3) 25 kural × 1000 adres (25.000 banlı IP) varken kural listesini okuma (servisin 5 dk'lık kontrolü)
    for ($k = 2; $k -le 25; $k++) {
        $r = New-Object -ComObject HNetCfg.FWRule
        $r.Name = "SGLoadTest-{0:000}" -f $k; $r.Grouping = "SGLoadTest"; $r.Protocol = 256; $r.Direction = 1; $r.Action = 0; $r.Profiles = 0x7FFFFFFF
        $r.RemoteAddresses = ((0..999 | ForEach-Object { Addr ($k * 1000 + $_) }) -join ","); $r.Enabled = $true
        $policy.Rules.Add($r)
    }
    $sw = [Diagnostics.Stopwatch]::StartNew()
    $count = 0
    foreach ($r in $policy.Rules) { if ($r.Name -like "SGLoadTest-*") { $null = $r.RemoteAddresses; $count++ } }
    $results += "Tüm kuralları okuma ($count test kuralı, toplam $($policy.Rules.Count) kural): {0:N0} ms" -f $sw.Elapsed.TotalMilliseconds
    Write-Host $results[-1]
}
finally {
    Remove-Test
    Write-Host "Test kuralları silindi." -ForegroundColor Green
}
$out = Join-Path $PSScriptRoot ("FwBench_{0:yyyyMMdd_HHmm}.txt" -f (Get-Date))
$results | Set-Content -Path $out -Encoding UTF8
Write-Host "Sonuç: $out"
