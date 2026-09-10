<#
  Build toàn bộ UMC2 HIS Suite bằng C# compiler có sẵn trong .NET Framework 4 (không cần Visual Studio, không cần NuGet).

    .\build.ps1            Build + tạo gói triển khai dist\UMC2-HIS-Suite-<version>\ và file .zip
    .\build.ps1 -Test      Build rồi chạy kiểm thử máy chủ tờ khai (dữ liệu giả, cổng ngẫu nhiên)
    .\build.ps1 -Smoke     Chạy thêm SmokeTest UIA với Mock HIS (mở cửa sổ, cần màn hình)
#>
param(
    [switch]$Test,
    [switch]$Smoke,
    [switch]$NoZip
)

$ErrorActionPreference = 'Stop'
$framework = 'C:\Windows\Microsoft.NET\Framework\v4.0.30319'
$csc = Join-Path $framework 'csc.exe'
if (-not (Test-Path -LiteralPath $csc)) { throw 'Không tìm thấy C# compiler của .NET Framework 4.' }

$root = $PSScriptRoot
$appSource = Join-Path $root 'src\HisAdmissionAssistant'
$appOutput = Join-Path $appSource 'bin\Release'
$serverSource = Join-Path $root 'src\IntakeServer'
$serverOutput = Join-Path $serverSource 'bin\Release'
$mockSource = Join-Path $root 'tools\MockHis'
$mockOutput = Join-Path $mockSource 'bin\Release'
$testSource = Join-Path $root 'tests\SmokeTest'
$testOutput = Join-Path $testSource 'bin\Release'
$flowSource = Join-Path $root 'tests\ServerFlowTest'
$flowOutput = Join-Path $flowSource 'bin\Release'
$renderSource = Join-Path $root 'tests\RenderUi'
$renderOutput = Join-Path $renderSource 'bin\Release'
New-Item -ItemType Directory -Force -Path $appOutput, $serverOutput, $mockOutput, $testOutput, $flowOutput, $renderOutput | Out-Null

function Invoke-Csc([string]$name, [string[]]$arguments) {
    & $csc /nologo /optimize+ $arguments
    if ($LASTEXITCODE -ne 0) { throw "Build $name thất bại với mã $LASTEXITCODE" }
    Write-Host "  OK  $name"
}

$gac = 'C:\Windows\Microsoft.NET\assembly\GAC_MSIL'
function Ref([string]$file) { '/reference:' + (Join-Path $framework $file) }
$baseRefs = @((Ref 'System.dll'), (Ref 'System.Core.dll'))
$uiaRefs = @(
    ('/reference:' + (Join-Path $gac 'UIAutomationClient\v4.0_4.0.0.0__31bf3856ad364e35\UIAutomationClient.dll')),
    ('/reference:' + (Join-Path $gac 'UIAutomationTypes\v4.0_4.0.0.0__31bf3856ad364e35\UIAutomationTypes.dll')),
    ('/reference:' + (Join-Path $gac 'WindowsBase\v4.0_4.0.0.0__31bf3856ad364e35\WindowsBase.dll'))
)
$appRefs = $baseRefs + @((Ref 'System.Drawing.dll'), (Ref 'System.Windows.Forms.dll'), (Ref 'System.Web.Extensions.dll'),
    (Ref 'System.Xml.dll'), (Ref 'System.Security.dll')) + $uiaRefs

Write-Host 'Đang build…'

# 1. HIS Admission Assistant (máy bác sĩ)
$appFiles = Get-ChildItem -LiteralPath $appSource -Filter '*.cs' -File | ForEach-Object { $_.FullName }
Invoke-Csc 'HisAdmissionAssistant' (@('/target:winexe', '/debug:pdbonly', "/win32manifest:$appSource\app.manifest",
    "/out:$appOutput\HisAdmissionAssistant.exe") + $appRefs + $appFiles)
$profileOutput = Join-Path $appOutput 'profiles'
New-Item -ItemType Directory -Force -Path $profileOutput | Out-Null
Copy-Item -Path (Join-Path $appSource 'profiles\*.xml') -Destination $profileOutput -Force

# 2. Intake Server (máy chủ tờ khai) — web UI nhúng trong exe
$serverFiles = Get-ChildItem -LiteralPath $serverSource -Filter '*.cs' -File | ForEach-Object { $_.FullName }
$wwwroot = Join-Path $serverSource 'wwwroot'
$resources = Get-ChildItem -LiteralPath $wwwroot -Recurse -File | ForEach-Object {
    $relative = $_.FullName.Substring($wwwroot.Length + 1).Replace('\', '/')
    '/resource:' + $_.FullName + ',wwwroot/' + $relative
}
Invoke-Csc 'IntakeServer' (@('/target:exe', '/debug:pdbonly', "/out:$serverOutput\IntakeServer.exe") + $baseRefs +
    @((Ref 'System.Web.Extensions.dll'), (Ref 'System.Security.dll'), (Ref 'System.ServiceProcess.dll')) + $serverFiles + $resources)

# 3. Công cụ & kiểm thử
Invoke-Csc 'MockHis' (@('/target:winexe', "/out:$mockOutput\MockHis.exe") + $baseRefs +
    @((Ref 'System.Drawing.dll'), (Ref 'System.Windows.Forms.dll'), (Join-Path $mockSource 'Program.cs')))
Invoke-Csc 'SmokeTest' (@('/target:exe', "/out:$testOutput\SmokeTest.exe") + $appRefs + @(
    (Join-Path $testSource 'Program.cs'), (Join-Path $appSource 'Models.cs'),
    (Join-Path $appSource 'UiaAutomationService.cs'), (Join-Path $appSource 'PatientContextWatcher.cs')))
Invoke-Csc 'ServerFlowTest' (@('/target:exe', "/out:$flowOutput\ServerFlowTest.exe") + $baseRefs +
    @((Ref 'System.Web.Extensions.dll'), (Ref 'System.Security.dll'), (Join-Path $flowSource 'Program.cs'), (Join-Path $appSource 'IntakeClient.cs')))
$renderFiles = @(Join-Path $renderSource 'Program.cs') + ($appFiles | Where-Object { -not $_.EndsWith('\Program.cs') })
Invoke-Csc 'RenderUi' (@('/target:winexe', "/out:$renderOutput\RenderUi.exe") + $appRefs + $renderFiles)
$renderProfiles = Join-Path $renderOutput 'profiles'
New-Item -ItemType Directory -Force -Path $renderProfiles | Out-Null
Copy-Item -Path (Join-Path $appSource 'profiles\*.xml') -Destination $renderProfiles -Force

# 4. Kiểm thử (tùy chọn)
if ($Test) {
    Write-Host 'Chạy kiểm thử máy chủ tờ khai…'
    & "$flowOutput\ServerFlowTest.exe" "$serverOutput\IntakeServer.exe"
    if ($LASTEXITCODE -ne 0) { throw 'ServerFlowTest thất bại.' }
}
if ($Smoke) {
    Write-Host 'Chạy SmokeTest UIA với Mock HIS…'
    & "$testOutput\SmokeTest.exe" "$mockOutput\MockHis.exe"
    if ($LASTEXITCODE -ne 0) { throw 'SmokeTest thất bại.' }
}

# 5. Gói triển khai
$version = (Get-Item "$serverOutput\IntakeServer.exe").VersionInfo.FileVersion
if (-not $version) { $version = '1.0.0' }
$distName = "UMC2-HIS-Suite-$version"
$dist = Join-Path $root "dist\$distName"
if (Test-Path -LiteralPath $dist) { Remove-Item -LiteralPath $dist -Recurse -Force }
$distServer = Join-Path $dist '1-may-chu-to-khai'
$distDoctor = Join-Path $dist '2-may-bac-si'
New-Item -ItemType Directory -Force -Path $distServer, (Join-Path $distDoctor 'profiles') | Out-Null
Copy-Item -LiteralPath "$serverOutput\IntakeServer.exe" -Destination $distServer
Copy-Item -Path (Join-Path $root 'deploy\install-server.ps1'), (Join-Path $root 'deploy\uninstall-server.ps1'), (Join-Path $root 'deploy\CAI-DAT-MAY-CHU.cmd') -Destination $distServer
Copy-Item -LiteralPath "$appOutput\HisAdmissionAssistant.exe" -Destination $distDoctor
Copy-Item -Path (Join-Path $appSource 'profiles\*.xml') -Destination (Join-Path $distDoctor 'profiles')
Copy-Item -Path (Join-Path $root 'deploy\install-assistant.ps1'), (Join-Path $root 'deploy\uninstall-assistant.ps1'), (Join-Path $root 'deploy\CAI-DAT-MAY-BAC-SI.cmd') -Destination $distDoctor
Copy-Item -Path (Join-Path $root 'docs\TRIEN-KHAI.md') -Destination (Join-Path $dist 'HUONG-DAN-TRIEN-KHAI.md')
Copy-Item -Path (Join-Path $root 'THIRD-PARTY-NOTICES.md') -Destination $dist

if (-not $NoZip) {
    $zip = Join-Path $root "dist\$distName.zip"
    if (Test-Path -LiteralPath $zip) { Remove-Item -LiteralPath $zip -Force }
    if (Get-Command Compress-Archive -ErrorAction SilentlyContinue) {
        Compress-Archive -Path "$dist\*" -DestinationPath $zip
        Write-Host "Gói nén: $zip"
    } else {
        Write-Warning 'PowerShell < 5: bỏ qua tạo .zip, dùng thư mục dist.'
    }
}

Write-Host ''
Write-Host "Build hoàn tất." -ForegroundColor Green
Write-Host "  Máy bác sĩ : $appOutput\HisAdmissionAssistant.exe"
Write-Host "  Máy chủ    : $serverOutput\IntakeServer.exe"
Write-Host "  Gói cài    : $dist"
