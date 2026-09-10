<#
  Gỡ HIS Admission Assistant của người dùng hiện tại (thêm -RemoveSettings để xóa cả ghép nối và profile đã hiệu chỉnh).
#>
param(
    [string]$InstallDir = (Join-Path $env:LOCALAPPDATA 'Programs\UMC2\HisAssistant'),
    [switch]$RemoveSettings
)
Get-Process -Name 'HisAdmissionAssistant' -ErrorAction SilentlyContinue | Stop-Process -Force -ErrorAction SilentlyContinue
foreach ($link in @(
    (Join-Path ([Environment]::GetFolderPath('Programs')) 'UMC2 HIS Assistant.lnk'),
    (Join-Path ([Environment]::GetFolderPath('Desktop')) 'UMC2 HIS Assistant.lnk'),
    (Join-Path ([Environment]::GetFolderPath('Startup')) 'UMC2 HIS Assistant.lnk'))) {
    if (Test-Path -LiteralPath $link) { Remove-Item -LiteralPath $link -Force }
}
if (Test-Path -LiteralPath $InstallDir) { Remove-Item -LiteralPath $InstallDir -Recurse -Force }
if ($RemoveSettings) {
    $settings = Join-Path $env:LOCALAPPDATA 'UMC2\HisAdmissionAssistant'
    if (Test-Path -LiteralPath $settings) { Remove-Item -LiteralPath $settings -Recurse -Force }
}
Write-Host 'Đã gỡ UMC2 HIS Assistant.' -ForegroundColor Green
