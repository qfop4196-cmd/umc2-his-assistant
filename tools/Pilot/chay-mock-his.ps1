<#
  Lượt 1 của PILOT-CHECKLIST — phần thử tay: mở máy chủ tờ khai TẠM (chỉ localhost, dữ liệu thử), Mock HIS và trợ lý.
  Đóng cửa sổ máy chủ để dừng. Dữ liệu thử (tài khoản, tờ khai, ghép nối) nằm ở %LOCALAPPDATA%\UMC2\PilotIntakeData
  và ĐƯỢC GIỮ giữa các lần chạy; muốn làm lại từ đầu thì chạy với -Reset (hoặc bấm LAM-LAI-TU-DAU.cmd).
  Không dùng cổng 8080/8081 để không đụng máy chủ thật nếu có.
#>
param(
    [int]$StaffPort = 18080,
    [int]$PublicPort = 18081,
    [switch]$Reset
)
$ErrorActionPreference = 'Stop'
$root = (Resolve-Path (Join-Path $PSScriptRoot '..\..')).Path
$server = Join-Path $root 'src\IntakeServer\bin\Release\IntakeServer.exe'
$mock = Join-Path $root 'tools\MockHis\bin\Release\MockHis.exe'
$assistant = Join-Path $root 'src\HisAdmissionAssistant\bin\Release\HisAdmissionAssistant.exe'
foreach ($file in @($server, $mock, $assistant)) {
    if (-not (Test-Path -LiteralPath $file)) { throw "Chưa build $file - hãy chạy KIEM-THU-TU-DONG.cmd trước." }
}
$data = Join-Path $env:LOCALAPPDATA 'UMC2\PilotIntakeData'
if ($Reset -and (Test-Path -LiteralPath $data)) {
    Get-Process -Name 'IntakeServer' -ErrorAction SilentlyContinue | Where-Object { $_.Path -eq $server } | Stop-Process -Force -ErrorAction SilentlyContinue
    Start-Sleep -Seconds 1
    Remove-Item -LiteralPath $data -Recurse -Force
    Write-Host 'Đã xóa dữ liệu thử cũ — sẽ phải tạo lại tài khoản quản trị và ghép nối lại trợ lý.'
}
New-Item -ItemType Directory -Force -Path $data | Out-Null

Start-Process -FilePath $server -WorkingDirectory (Split-Path $server) -ArgumentList @(
    '--data', ('"' + $data + '"'), '--staff-port', $StaffPort, '--public-port', $PublicPort, '--bind', 'localhost')
Start-Sleep -Seconds 2
Start-Process -FilePath $mock -WorkingDirectory (Split-Path $mock)
Start-Process -FilePath $assistant -WorkingDirectory (Split-Path $assistant)

Write-Host ''
Write-Host "Trang người bệnh : http://localhost:$PublicPort/"
Write-Host "Trang điều dưỡng : http://localhost:$StaffPort/   (lần đầu: tạo tài khoản quản trị)"
Write-Host "Trợ lý           : tab Tờ khai BN > Ghép nối máy chủ... > http://localhost:$StaffPort + mã 6 số (Quản trị > Máy bác sĩ)"
Write-Host "Dữ liệu thử      : $data (giữ lại giữa các lần chạy; LAM-LAI-TU-DAU.cmd để xóa)"
Write-Host 'Làm tiếp bước 2-8 của Lượt 1 trong PILOT-CHECKLIST.md. Xong thì đóng cửa sổ máy chủ, Mock HIS và trợ lý.'
