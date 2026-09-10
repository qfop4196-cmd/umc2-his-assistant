<#
  Cài HIS Admission Assistant cho người dùng hiện tại trên máy bác sĩ. KHÔNG cần quyền Administrator.

    .\install-assistant.ps1              # cài + tạo shortcut Desktop/Start Menu
    .\install-assistant.ps1 -AutoStart   # thêm tự mở khi đăng nhập Windows (bảng gọn luôn nổi)

  Lưu ý: nếu UMC2HIS chạy bằng "Run as administrator" thì trợ lý cũng phải chạy cùng mức quyền (Windows chặn
  chương trình quyền thấp điều khiển cửa sổ quyền cao).
#>
param(
    [string]$InstallDir = (Join-Path $env:LOCALAPPDATA 'Programs\UMC2\HisAssistant'),
    [switch]$AutoStart,
    [switch]$NoDesktopShortcut
)

$ErrorActionPreference = 'Stop'
$source = Join-Path $PSScriptRoot 'HisAdmissionAssistant.exe'
if (-not (Test-Path -LiteralPath $source)) { throw "Không thấy HisAdmissionAssistant.exe cạnh script ($source)." }
$release = (Get-ItemProperty 'HKLM:\SOFTWARE\Microsoft\NET Framework Setup\NDP\v4\Full' -ErrorAction SilentlyContinue).Release
if (-not $release) { throw 'Máy chưa có .NET Framework 4.x.' }

Get-Process -Name 'HisAdmissionAssistant' -ErrorAction SilentlyContinue | Stop-Process -Force -ErrorAction SilentlyContinue
New-Item -ItemType Directory -Force -Path (Join-Path $InstallDir 'profiles') | Out-Null
Copy-Item -LiteralPath $source -Destination $InstallDir -Force
Copy-Item -Path (Join-Path $PSScriptRoot 'profiles\*.xml') -Destination (Join-Path $InstallDir 'profiles') -Force
$exe = Join-Path $InstallDir 'HisAdmissionAssistant.exe'

$shell = New-Object -ComObject WScript.Shell
function New-Shortcut([string]$path, [string]$arguments = '') {
    $link = $shell.CreateShortcut($path)
    $link.TargetPath = $exe
    $link.Arguments = $arguments
    $link.WorkingDirectory = $InstallDir
    $link.Description = 'UMC2 HIS Assistant - điền tờ khai đã duyệt vào HIS'
    $link.Save()
}
$programs = [Environment]::GetFolderPath('Programs')
New-Shortcut (Join-Path $programs 'UMC2 HIS Assistant.lnk')
if (-not $NoDesktopShortcut) { New-Shortcut (Join-Path ([Environment]::GetFolderPath('Desktop')) 'UMC2 HIS Assistant.lnk') }
$startup = Join-Path ([Environment]::GetFolderPath('Startup')) 'UMC2 HIS Assistant.lnk'
if ($AutoStart) { New-Shortcut $startup '--minimized' } elseif (Test-Path -LiteralPath $startup) { Remove-Item -LiteralPath $startup -Force }

Write-Host "Đã cài vào $InstallDir" -ForegroundColor Green
Write-Host 'Mở "UMC2 HIS Assistant" > tab "Tờ khai BN" > Ghép nối máy chủ… > nhập mã 6 số do quản trị viên cấp.'
Start-Process -FilePath $exe -WorkingDirectory $InstallDir
