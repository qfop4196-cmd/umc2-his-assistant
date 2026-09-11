<#
  Lượt 1 của PILOT-CHECKLIST — phần tự động: build + ServerFlowTest (máy chủ tờ khai) + SmokeTest (UIA với Mock HIS).
  Kết quả ghi vào dist\pilot\kiem-thu.log (UTF-8). Chỉ dùng dữ liệu giả; cần màn hình Windows đang mở (SmokeTest mở cửa sổ).
#>
$ErrorActionPreference = 'Stop'
$root = (Resolve-Path (Join-Path $PSScriptRoot '..\..')).Path
$logDir = Join-Path $root 'dist\pilot'
New-Item -ItemType Directory -Force -Path $logDir | Out-Null
$log = Join-Path $logDir 'kiem-thu.log'
$lines = New-Object 'System.Collections.Generic.List[string]'
function Say([string]$text) { Write-Host $text; $lines.Add($text) }

$release = (Get-ItemProperty 'HKLM:\SOFTWARE\Microsoft\NET Framework Setup\NDP\v4\Full' -ErrorAction SilentlyContinue).Release
Say ('UMC2 HIS Suite - kiểm thử tự động ' + (Get-Date -Format 'yyyy-MM-dd HH:mm:ss'))
Say ('Windows ' + [Environment]::OSVersion.Version + ' | PowerShell ' + $PSVersionTable.PSVersion + ' | .NET Framework release ' + $release)
$code = 0
try {
    & (Join-Path $root 'build.ps1') -Test -Smoke -NoZip *>&1 | ForEach-Object { Say ([string]$_) }
    Say 'KẾT QUẢ: PASS'
} catch {
    $code = 1
    Say ('KẾT QUẢ: FAIL - ' + $_.Exception.Message)
} finally {
    [IO.File]::WriteAllLines($log, $lines.ToArray(), (New-Object System.Text.UTF8Encoding($false)))
}
Write-Host ''
Write-Host "Nhật ký: $log"
exit $code
