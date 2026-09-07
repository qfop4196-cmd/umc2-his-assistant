$ErrorActionPreference = 'Stop'
$framework = 'C:\Windows\Microsoft.NET\Framework\v4.0.30319'
$csc = Join-Path $framework 'csc.exe'
if (-not (Test-Path -LiteralPath $csc)) { throw 'Không tìm thấy C# compiler của .NET Framework 4.' }

$appSource = Join-Path $PSScriptRoot 'src\HisAdmissionAssistant'
$appOutput = Join-Path $appSource 'bin\Release'
$mockSource = Join-Path $PSScriptRoot 'tools\MockHis'
$mockOutput = Join-Path $mockSource 'bin\Release'
$testSource = Join-Path $PSScriptRoot 'tests\SmokeTest'
$testOutput = Join-Path $testSource 'bin\Release'
$renderSource = Join-Path $PSScriptRoot 'tests\RenderUi'
$renderOutput = Join-Path $renderSource 'bin\Release'
New-Item -ItemType Directory -Force -Path $appOutput, $mockOutput, $testOutput, $renderOutput | Out-Null

$gac = 'C:\Windows\Microsoft.NET\assembly\GAC_MSIL'
$references = @(
    (Join-Path $framework 'System.dll'),
    (Join-Path $framework 'System.Core.dll'),
    (Join-Path $framework 'System.Drawing.dll'),
    (Join-Path $framework 'System.Windows.Forms.dll'),
    (Join-Path $framework 'System.Web.Extensions.dll'),
    (Join-Path $framework 'System.Xml.dll'),
    (Join-Path $gac 'UIAutomationClient\v4.0_4.0.0.0__31bf3856ad364e35\UIAutomationClient.dll'),
    (Join-Path $gac 'UIAutomationTypes\v4.0_4.0.0.0__31bf3856ad364e35\UIAutomationTypes.dll'),
    (Join-Path $gac 'WindowsBase\v4.0_4.0.0.0__31bf3856ad364e35\WindowsBase.dll')
)
$referenceArgs = $references | ForEach-Object { '/reference:' + $_ }
$appFiles = Get-ChildItem -LiteralPath $appSource -Filter '*.cs' -File | ForEach-Object { $_.FullName }
& $csc /nologo /target:winexe /optimize+ /debug:pdbonly "/win32manifest:$appSource\app.manifest" "/out:$appOutput\HisAdmissionAssistant.exe" $referenceArgs $appFiles
if ($LASTEXITCODE -ne 0) { throw "Build app thất bại với mã $LASTEXITCODE" }

$profileOutput = Join-Path $appOutput 'profiles'
New-Item -ItemType Directory -Force -Path $profileOutput | Out-Null
Copy-Item -LiteralPath (Get-ChildItem -LiteralPath (Join-Path $appSource 'profiles') -Filter '*.xml').FullName -Destination $profileOutput -Force

$mockReferences = @(
    ('/reference:' + (Join-Path $framework 'System.dll')),
    ('/reference:' + (Join-Path $framework 'System.Core.dll')),
    ('/reference:' + (Join-Path $framework 'System.Drawing.dll')),
    ('/reference:' + (Join-Path $framework 'System.Windows.Forms.dll'))
)
& $csc /nologo /target:winexe /optimize+ /debug:pdbonly "/out:$mockOutput\MockHis.exe" $mockReferences (Join-Path $mockSource 'Program.cs')
if ($LASTEXITCODE -ne 0) { throw "Build MockHis thất bại với mã $LASTEXITCODE" }

$testFiles = @(
    (Join-Path $testSource 'Program.cs'),
    (Join-Path $appSource 'Models.cs'),
    (Join-Path $appSource 'UiaAutomationService.cs')
)
& $csc /nologo /target:exe /optimize+ "/out:$testOutput\SmokeTest.exe" $referenceArgs $testFiles
if ($LASTEXITCODE -ne 0) { throw "Build smoke test thất bại với mã $LASTEXITCODE" }

$renderFiles = @(
    (Join-Path $renderSource 'Program.cs'),
    (Join-Path $appSource 'Models.cs'),
    (Join-Path $appSource 'ProfileStore.cs'),
    (Join-Path $appSource 'UiaAutomationService.cs'),
    (Join-Path $appSource 'CloudQueueClient.cs'),
    (Join-Path $appSource 'MainForm.cs')
)
& $csc /nologo /target:winexe /optimize+ "/out:$renderOutput\RenderUi.exe" $referenceArgs $renderFiles
if ($LASTEXITCODE -ne 0) { throw "Build UI renderer thất bại với mã $LASTEXITCODE" }
$renderProfiles = Join-Path $renderOutput 'profiles'
New-Item -ItemType Directory -Force -Path $renderProfiles | Out-Null
Copy-Item -LiteralPath (Get-ChildItem -LiteralPath (Join-Path $appSource 'profiles') -Filter '*.xml').FullName -Destination $renderProfiles -Force

Write-Host "Build hoàn tất: $appOutput\HisAdmissionAssistant.exe"
