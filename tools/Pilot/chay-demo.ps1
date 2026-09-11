<#
  CHẾ ĐỘ DEMO / CHẤM THI: chạy máy chủ tờ khai trên máy này với DỮ LIỆU GIẢ và mở cả hai trang ra Internet
  bằng Cloudflare Quick Tunnel (không cần tài khoản, không cần quyền Administrator):
    - Tờ khai người bệnh  : https://<ngẫu-nhiên>.trycloudflare.com  (cổng 8081)
    - Trang điều dưỡng    : https://<ngẫu-nhiên>.trycloudflare.com  (cổng 8080, chỉ bật ở chế độ demo)
  Địa chỉ đổi mỗi lần khởi động lại → giữ cửa sổ này mở trong suốt thời gian chấm thi.
  Dữ liệu demo nằm ở %LOCALAPPDATA%\UMC2\DemoIntakeData (14 tờ khai mẫu tự tạo lần đầu). KHÔNG dùng với dữ liệu thật.
#>
param(
    [int]$StaffPort = 8080,
    [int]$PublicPort = 8081,
    [switch]$Reset
)
$ErrorActionPreference = 'Stop'
$root = (Resolve-Path (Join-Path $PSScriptRoot '..\..')).Path
$serverDir = Join-Path $root 'src\IntakeServer\bin\Release'
$server = Join-Path $serverDir 'IntakeServer.exe'
if (-not (Test-Path -LiteralPath $server)) { throw "Chưa build $server - hãy chạy KIEM-THU-TU-DONG.cmd trước." }

$cloudflared = Join-Path $serverDir 'cloudflared.exe'
if (-not (Test-Path -LiteralPath $cloudflared)) {
    Write-Host 'Đang tải cloudflared.exe (Cloudflare Tunnel, bản chính thức từ GitHub, ~60 MB)...'
    [Net.ServicePointManager]::SecurityProtocol = [Net.SecurityProtocolType]::Tls12
    Invoke-WebRequest -Uri 'https://github.com/cloudflare/cloudflared/releases/latest/download/cloudflared-windows-amd64.exe' -OutFile $cloudflared -UseBasicParsing
}

$data = Join-Path $env:LOCALAPPDATA 'UMC2\DemoIntakeData'
if ($Reset -and (Test-Path -LiteralPath $data)) {
    Get-Process -Name 'IntakeServer' -ErrorAction SilentlyContinue | Where-Object { $_.Path -eq $server } | Stop-Process -Force -ErrorAction SilentlyContinue
    Start-Sleep -Seconds 1
    Remove-Item -LiteralPath $data -Recurse -Force
    Write-Host 'Đã xóa dữ liệu demo cũ.'
}
New-Item -ItemType Directory -Force -Path $data | Out-Null

Write-Host ''
Write-Host 'Máy chủ demo đang khởi động. Địa chỉ Internet sẽ hiện bên dưới sau khoảng 10 giây.' -ForegroundColor Green
Write-Host "Lần đầu: mở http://localhost:$StaffPort/ để tạo tài khoản quản trị, rồi Quản trị > Tài khoản > thêm tài khoản 'giamkhao' (tích 'Tài khoản demo')."
Write-Host 'Giữ cửa sổ này mở. Đóng cửa sổ = dừng máy chủ demo.'
Write-Host ''
& $server --data $data --staff-port $StaffPort --public-port $PublicPort --bind localhost --demo
