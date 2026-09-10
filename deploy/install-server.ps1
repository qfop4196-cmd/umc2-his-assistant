<#
  Cài UMC2 Intake Server thành Windows Service (chạy nền, tự khởi động cùng Windows).
  Chạy MỘT LẦN bằng quyền Administrator trên máy làm máy chủ tờ khai (máy quầy tiếp nhận/điều dưỡng hoặc máy chủ nội bộ).

    .\install-server.ps1
    .\install-server.ps1 -WithTunnel                      # tải thêm cloudflared.exe để bật "đường hầm tạm" từ trang quản trị
    .\install-server.ps1 -TunnelToken "<token>"           # Cloudflare Tunnel có tên miền (chính thức) chạy như dịch vụ riêng
    .\install-server.ps1 -StaffPort 8080 -PublicPort 8081

  Việc script làm: chép IntakeServer.exe vào Program Files, tạo thư mục dữ liệu mã hóa trong ProgramData (chỉ Administrators,
  SYSTEM và LOCAL SERVICE được truy cập), đăng ký URL cho http.sys, mở tường lửa CHỈ cho dải IP nội bộ, tạo dịch vụ
  UMC2IntakeServer chạy bằng tài khoản LOCAL SERVICE (quyền thấp) và tự khởi động lại khi lỗi.
#>
param(
    [int]$StaffPort = 8080,
    [int]$PublicPort = 8081,
    [string]$InstallDir = (Join-Path $env:ProgramFiles 'UMC2\IntakeServer'),
    [string]$DataDir = (Join-Path $env:ProgramData 'UMC2\IntakeServer'),
    [switch]$WithTunnel,
    [string]$TunnelToken = '',
    [switch]$NoFirewall
)

$ErrorActionPreference = 'Stop'
$serviceName = 'UMC2IntakeServer'
$discoveryPort = 47810

function Step([string]$text) { Write-Host "==> $text" -ForegroundColor Cyan }

# Chạy lệnh ngoài mà lỗi là chấp nhận được (xóa luật/URL chưa tồn tại...) mà không làm dừng script.
function Quiet([scriptblock]$command) {
    $previous = $ErrorActionPreference
    $ErrorActionPreference = 'Continue'
    try { & $command 2>&1 | Out-Null } catch { }
    finally { $ErrorActionPreference = $previous }
}

$principal = New-Object Security.Principal.WindowsPrincipal([Security.Principal.WindowsIdentity]::GetCurrent())
if (-not $principal.IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)) {
    throw 'Hãy chạy script bằng quyền Administrator (chuột phải PowerShell > Run as administrator), hoặc dùng CAI-DAT-MAY-CHU.cmd.'
}

$release = (Get-ItemProperty 'HKLM:\SOFTWARE\Microsoft\NET Framework Setup\NDP\v4\Full' -ErrorAction SilentlyContinue).Release
if (-not $release) { throw 'Máy chưa có .NET Framework 4.x. Cài .NET Framework 4.8 rồi chạy lại.' }

$source = Join-Path $PSScriptRoot 'IntakeServer.exe'
if (-not (Test-Path -LiteralPath $source)) { throw "Không thấy IntakeServer.exe cạnh script ($source)." }

Step 'Dừng dịch vụ cũ (nếu có)'
$existing = Get-Service -Name $serviceName -ErrorAction SilentlyContinue
if ($existing) {
    if ($existing.Status -ne 'Stopped') { Stop-Service -Name $serviceName -Force; Start-Sleep -Seconds 2 }
}

Step "Chép chương trình vào $InstallDir"
New-Item -ItemType Directory -Force -Path $InstallDir | Out-Null
Copy-Item -LiteralPath $source -Destination $InstallDir -Force
$wwwOverride = Join-Path $PSScriptRoot 'wwwroot'
if (Test-Path -LiteralPath $wwwOverride) { Copy-Item -LiteralPath $wwwOverride -Destination $InstallDir -Recurse -Force }
$exe = Join-Path $InstallDir 'IntakeServer.exe'

Step "Chuẩn bị thư mục dữ liệu $DataDir (mã hóa DPAPI, giới hạn quyền)"
New-Item -ItemType Directory -Force -Path $DataDir | Out-Null
# *S-1-5-32-544 = Administrators, *S-1-5-18 = SYSTEM, *S-1-5-19 = LOCAL SERVICE (dùng SID để chạy được trên Windows tiếng Việt)
& icacls $DataDir /inheritance:r /grant:r '*S-1-5-32-544:(OI)(CI)F' '*S-1-5-18:(OI)(CI)F' '*S-1-5-19:(OI)(CI)M' | Out-Null
if ($LASTEXITCODE -ne 0) { throw 'Không đặt được quyền thư mục dữ liệu.' }

Step 'Đăng ký địa chỉ http.sys cho cổng nhân viên và cổng người bệnh'
foreach ($port in @($StaffPort, $PublicPort)) {
    Quiet { netsh http delete urlacl url="http://+:$port/" }
    # LS = LOCAL SERVICE (dịch vụ), BA = Administrators, IU = người dùng đăng nhập (chạy thử bằng tay)
    & netsh http add urlacl url="http://+:$port/" sddl="D:(A;;GX;;;LS)(A;;GX;;;BA)(A;;GX;;;IU)" | Out-Null
    if ($LASTEXITCODE -ne 0) { throw "Không đăng ký được URL cho cổng $port (cổng có thể đang bị chương trình khác dùng)." }
}

if (-not $NoFirewall) {
    Step 'Mở tường lửa CHỈ cho mạng nội bộ (10.x, 172.16-31.x, 192.168.x)'
    $lan = '10.0.0.0/8,172.16.0.0/12,192.168.0.0/16,169.254.0.0/16'
    $rules = @(
        @{ Name = 'UMC2 Intake - Staff (LAN)'; Protocol = 'TCP'; Port = $StaffPort },
        @{ Name = 'UMC2 Intake - Patient form (LAN)'; Protocol = 'TCP'; Port = $PublicPort },
        @{ Name = 'UMC2 Intake - Discovery (LAN)'; Protocol = 'UDP'; Port = $discoveryPort })
    foreach ($rule in $rules) {
        Quiet { netsh advfirewall firewall delete rule "name=$($rule.Name)" }
        & netsh advfirewall firewall add rule "name=$($rule.Name)" dir=in action=allow "protocol=$($rule.Protocol)" "localport=$($rule.Port)" "remoteip=$lan" profile=any | Out-Null
        if ($LASTEXITCODE -ne 0) { Write-Warning "Không tạo được luật tường lửa $($rule.Name)" }
    }
}

if ($WithTunnel -or $TunnelToken) {
    $cloudflared = Join-Path $InstallDir 'cloudflared.exe'
    if (-not (Test-Path -LiteralPath $cloudflared)) {
        Step 'Tải cloudflared.exe (Cloudflare Tunnel, bản chính thức từ GitHub)'
        [Net.ServicePointManager]::SecurityProtocol = [Net.SecurityProtocolType]::Tls12
        $url = 'https://github.com/cloudflare/cloudflared/releases/latest/download/cloudflared-windows-amd64.exe'
        Invoke-WebRequest -Uri $url -OutFile $cloudflared -UseBasicParsing
    }
    if ($TunnelToken) {
        Step 'Cài Cloudflare Tunnel có tên miền thành dịch vụ riêng (cloudflared)'
        Quiet { & $cloudflared service uninstall }
        & $cloudflared service install $TunnelToken
        Write-Host "   Trên Cloudflare Zero Trust, trỏ Public Hostname của tunnel tới http://localhost:$PublicPort (CHỈ cổng người bệnh)." -ForegroundColor Yellow
    }
}

Step 'Tạo/cập nhật dịch vụ Windows'
$binPath = "`"$exe`" --service --data `"$DataDir`" --staff-port $StaffPort --public-port $PublicPort"
if ($existing) {
    # Ghi ImagePath trực tiếp để tránh lỗi trích dẫn tham số của sc.exe trên PowerShell 5.1.
    Set-ItemProperty -Path "HKLM:\SYSTEM\CurrentControlSet\Services\$serviceName" -Name ImagePath -Value $binPath
} else {
    New-Service -Name $serviceName -BinaryPathName $binPath -DisplayName 'UMC2 Intake Server' -StartupType Automatic `
        -Description 'May chu to khai truoc kham UMC2 (LAN, du lieu ma hoa, tu xoa theo han luu tru).' | Out-Null
}
# Tài khoản LOCAL SERVICE (quyền thấp, không mật khẩu), khởi động trễ, tự khởi động lại khi lỗi.
& sc.exe config $serviceName obj= 'NT AUTHORITY\LocalService' start= delayed-auto | Out-Null
if ($LASTEXITCODE -ne 0) { throw 'Không cấu hình được tài khoản dịch vụ LOCAL SERVICE.' }
& sc.exe failure $serviceName reset= 86400 actions= restart/5000/restart/10000/restart/60000 | Out-Null

Step 'Khởi động dịch vụ'
Start-Service -Name $serviceName
$ok = $false
for ($i = 0; $i -lt 30 -and -not $ok; $i++) {
    Start-Sleep -Milliseconds 500
    try {
        $r = Invoke-WebRequest -Uri "http://localhost:$PublicPort/api/public/health" -UseBasicParsing -TimeoutSec 3
        $ok = $r.StatusCode -eq 200
    } catch { }
}
if (-not $ok) { throw "Dịch vụ không phản hồi. Xem nhật ký tại $DataDir\logs" }

$ips = @([System.Net.NetworkInformation.NetworkInterface]::GetAllNetworkInterfaces() |
    Where-Object { $_.OperationalStatus -eq 'Up' -and $_.NetworkInterfaceType -ne 'Loopback' } |
    ForEach-Object { $_.GetIPProperties().UnicastAddresses } |
    Where-Object { $_.Address.AddressFamily -eq 'InterNetwork' -and -not $_.Address.ToString().StartsWith('169.254.') } |
    ForEach-Object { $_.Address.ToString() })
Write-Host ''
Write-Host 'CÀI ĐẶT XONG.' -ForegroundColor Green
Write-Host "  1) Mở trình duyệt TRÊN MÁY NÀY: http://localhost:$StaffPort/  -> tạo tài khoản quản trị, tên bệnh viện."
foreach ($ip in $ips) {
    Write-Host "  Trang điều dưỡng (LAN)  : http://${ip}:$StaffPort/"
    Write-Host "  Tờ khai người bệnh (LAN): http://${ip}:$PublicPort/   (máy tính bảng: thêm ?kiosk=1)"
}
Write-Host "  Dữ liệu (mã hóa)        : $DataDir"
Write-Host '  2) Trang quản trị > Máy bác sĩ > Tạo mã ghép nối, rồi cài HIS Assistant trên từng máy bác sĩ.'
