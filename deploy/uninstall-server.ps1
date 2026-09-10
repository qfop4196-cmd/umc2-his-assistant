<#
  Gỡ UMC2 Intake Server. Mặc định GIỮ thư mục dữ liệu; thêm -RemoveData để xóa hẳn (không khôi phục được).
#>
param(
    [int]$StaffPort = 8080,
    [int]$PublicPort = 8081,
    [string]$InstallDir = (Join-Path $env:ProgramFiles 'UMC2\IntakeServer'),
    [string]$DataDir = (Join-Path $env:ProgramData 'UMC2\IntakeServer'),
    [switch]$RemoveData
)

$ErrorActionPreference = 'Continue'
$principal = New-Object Security.Principal.WindowsPrincipal([Security.Principal.WindowsIdentity]::GetCurrent())
if (-not $principal.IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)) { throw 'Hãy chạy bằng quyền Administrator.' }

$serviceName = 'UMC2IntakeServer'
if (Get-Service -Name $serviceName -ErrorAction SilentlyContinue) {
    Stop-Service -Name $serviceName -Force -ErrorAction SilentlyContinue
    & sc.exe delete $serviceName | Out-Null
}
$cloudflared = Join-Path $InstallDir 'cloudflared.exe'
if (Test-Path -LiteralPath $cloudflared) { & $cloudflared service uninstall 2>&1 | Out-Null }
foreach ($port in @($StaffPort, $PublicPort)) { & netsh http delete urlacl url="http://+:$port/" 2>&1 | Out-Null }
foreach ($name in @('UMC2 Intake - Staff (LAN)', 'UMC2 Intake - Patient form (LAN)', 'UMC2 Intake - Discovery (LAN)')) {
    & netsh advfirewall firewall delete rule "name=$name" 2>&1 | Out-Null
}
Start-Sleep -Seconds 1
if (Test-Path -LiteralPath $InstallDir) { Remove-Item -LiteralPath $InstallDir -Recurse -Force }
if ($RemoveData -and (Test-Path -LiteralPath $DataDir)) {
    Remove-Item -LiteralPath $DataDir -Recurse -Force
    Write-Host "Đã xóa dữ liệu $DataDir"
} else {
    Write-Host "Giữ lại dữ liệu tại $DataDir (thêm -RemoveData để xóa)."
}
Write-Host 'Đã gỡ UMC2 Intake Server.' -ForegroundColor Green
