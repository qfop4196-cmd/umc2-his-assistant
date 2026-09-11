# Hướng dẫn triển khai UMC2 HIS Suite

Dành cho phòng CNTT / người phụ trách pilot. Thời gian: khoảng 20 phút cho máy chủ, 3 phút cho mỗi máy bác sĩ.

## 1. Chuẩn bị

| Hạng mục | Yêu cầu |
| --- | --- |
| Máy chủ tờ khai | Windows 10/11 hoặc Windows Server, bật liên tục, cùng mạng LAN với máy bác sĩ và máy điều dưỡng. Có quyền Administrator khi cài. |
| Máy bác sĩ | Máy đang chạy UMC2HIS. Không cần quyền Administrator. |
| Phần mềm | .NET Framework 4.x (có sẵn trên Windows 10/11). Không cần SQL, IIS, Node hay Python. |
| Mạng | Cổng TCP 8080 (điều dưỡng + máy bác sĩ), 8081 (tờ khai người bệnh), UDP 47810 (tự dò tìm). Script tự mở tường lửa chỉ cho IP nội bộ. |

Tải file `.zip` từ GitHub/nội bộ: **chuột phải → Properties → Unblock** trước khi giải nén, để Windows không chặn file `.exe`/`.ps1`.

## 2. Cài máy chủ tờ khai

1. Chép thư mục `1-may-chu-to-khai` lên máy chủ.
2. Chạy `CAI-DAT-MAY-CHU.cmd` (tự xin quyền Admin). Tùy chọn khi chạy `install-server.ps1`:
   - `-StaffPort 8080 -PublicPort 8081` đổi cổng nếu trùng.
   - `-WithTunnel` tải `cloudflared.exe` để bật đường hầm tạm từ trang quản trị.
   - `-TunnelToken "<token>"` cài Cloudflare Tunnel có tên miền (xem mục 6).
3. Script sẽ: chép `IntakeServer.exe` vào `C:\Program Files\UMC2\IntakeServer`, tạo `C:\ProgramData\UMC2\IntakeServer` (chỉ Administrators/SYSTEM/LOCAL SERVICE truy cập), đăng ký URL http.sys, mở tường lửa cho dải `10.x`, `172.16–31.x`, `192.168.x`, tạo dịch vụ **UMC2 Intake Server** (tài khoản LOCAL SERVICE, tự khởi động lại khi lỗi) và in ra các địa chỉ LAN.
4. **Trên chính máy chủ**, mở `http://localhost:8080` → **Thiết lập lần đầu**: tên bệnh viện, tài khoản quản trị. (Vì an toàn, bước này không làm được từ máy khác.)
5. **Quản trị → Tài khoản**: tạo tài khoản cho điều dưỡng (mật khẩu tạm, bắt đổi ở lần đăng nhập đầu).
6. **Mã QR & kết nối**: in áp phích A4 cho phòng chờ, lấy địa chỉ kiosk cho máy tính bảng.

Chạy thử không cài dịch vụ: mở PowerShell tại thư mục có `IntakeServer.exe` và chạy `.\IntakeServer.exe` (Ctrl+C để dừng). Nếu chưa chạy script cài đặt, máy chủ chỉ phục vụ trên `localhost`.

Quên mật khẩu quản trị: `"C:\Program Files\UMC2\IntakeServer\IntakeServer.exe" --reset-password admin --data "C:\ProgramData\UMC2\IntakeServer"` (chạy bằng Admin, dừng dịch vụ trước rồi khởi động lại).

## 3. Máy tính bảng / kiosk tại quầy

- Mở địa chỉ `http://<IP máy chủ>:8081/?kiosk=1` (có trong **Mã QR & kết nối**), ghim vào màn hình chính, bật chế độ toàn màn hình của trình duyệt.
- Chế độ kiosk không lưu nháp trên máy, tự xóa màn hình 60 giây sau khi gửi và hỏi lại khi bỏ dở quá 2,5 phút.

## 4. Cài máy bác sĩ

1. Chép thư mục `2-may-bac-si` sang máy bác sĩ (hoặc thư mục chung), chạy `CAI-DAT-MAY-BAC-SI.cmd`: cài vào `%LOCALAPPDATA%\Programs\UMC2\HisAssistant`, tạo shortcut và tự mở cùng Windows.
2. Nếu UMC2HIS chạy bằng **Run as administrator** thì trợ lý cũng phải chạy bằng Administrator (Windows chặn chương trình quyền thấp điều khiển cửa sổ quyền cao).
3. Trên trang quản trị: **Quản trị → Máy bác sĩ → Tạo mã ghép nối** (6 số, dùng 1 lần, hạn 10 phút).
4. Trong trợ lý: tab **Tờ khai BN → Ghép nối máy chủ… → Tự tìm** (hoặc nhập `http://<IP máy chủ>:8080`), đặt tên máy (ví dụ `PK-NGOAI-01`), nhập mã → **Ghép nối**. Khóa được lưu mã hóa cho tài khoản Windows đó; thu hồi được bất cứ lúc nào.

## 5. Hiệu chỉnh trên máy HIS thật (làm 1 lần cho mỗi màn hình)

1. Mở hồ sơ một bệnh nhân **thử** trên màn hình Phiếu khám vào viện / Khám bệnh.
2. Trong trợ lý, tab **Hiệu chỉnh UIA → Quét control**; kiểm tra các `AutomationId` như `mabn`, `lydo`, `benhly`… có đúng không. Ô nào sai: chọn trường ở tab **Nhập liệu**, bấm **Bắt control sau 3 giây** và rê chuột lên ô đó trên HIS.
3. Bấm **Kiểm tra selector**: profile đã có sẵn **Họ tên BN** (`hoten`) và **Năm sinh BN** (`namsinh`, chỉ màn hình Khám bệnh); mã BN màn hình Khám bệnh đọc từ 2 ô `mabn1+mabn3`. Trạng thái chỉ báo số chữ số (ví dụ "mabn1: 2 chữ số · mabn3: 6 chữ số"), không hiện giá trị. Nếu `mabn1` đã đủ mã thì sửa `PatientId` thành `mabn1`.
4. **Lưu profile**. Kiểm tra: bảng gọn phải hiện đúng *Mã BN · Họ tên (năm sinh)* khi mở hồ sơ.
5. Thử trọn luồng với bệnh nhân thử theo `PILOT-CHECKLIST.md` trước khi dùng thật.

## 6. Mở tờ khai ra Internet (người bệnh khai từ nhà)

Chỉ cổng người bệnh (8081) được mở ra ngoài; trang điều dưỡng và API máy bác sĩ luôn từ chối yêu cầu đến qua đường hầm/proxy hoặc từ IP công cộng.

**Thử nghiệm (không cần tài khoản):** cài với `-WithTunnel`, vào **Mã QR & kết nối → Bật đường hầm tạm**. Địa chỉ `https://….trycloudflare.com` đổi mỗi lần khởi động lại; Cloudflare chỉ khuyến nghị dùng thử.

**Chính thức:**
1. Tạo tài khoản Cloudflare, thêm tên miền của bệnh viện.
2. Zero Trust → Networks → Tunnels → **Create tunnel** (Cloudflared) → sao chép token.
3. Thêm **Public Hostname**, ví dụ `khai.benhvien.vn` → Service `http://localhost:8081`. Không tạo hostname nào trỏ tới cổng 8080.
4. Trên máy chủ: `.\install-server.ps1 -TunnelToken "<token>"`.
5. Trang quản trị → **Cài đặt → Địa chỉ công khai**: `https://khai.benhvien.vn` → in lại áp phích QR.

Có thể thay Cloudflare bằng reverse proxy/DMZ sẵn có của bệnh viện, miễn là chỉ chuyển tiếp tới cổng 8081 và dùng HTTPS.

## 7. Dữ liệu, lưu trữ và tuân thủ

- Máy chủ tờ khai là **hàng chờ tạm**, không phải hồ sơ bệnh án. Hồ sơ chính thức vẫn nằm trong HIS. Mặc định tờ khai chưa duyệt tự xóa sau 3 ngày, đã hoàn tất sau 7 ngày (chỉnh ở **Cài đặt**).
- Dữ liệu mã hóa bằng DPAPI của Windows (khóa gắn với máy chủ): chép file sang máy khác không đọc được, và cài lại Windows sẽ mất dữ liệu cũ — chấp nhận được với hàng chờ tạm.
- Người bệnh phải tích **đồng ý xử lý dữ liệu** trước khi gửi; nhật ký kiểm toán (`logs\audit-*.log`) ghi ai xem/duyệt/điền tờ khai nào, không ghi nội dung.
- Dữ liệu sức khỏe là dữ liệu cá nhân nhạy cảm theo Luật Bảo vệ dữ liệu cá nhân số 91/2025/QH15 (hiệu lực 01/01/2026) và Nghị định 356/2025/NĐ-CP hướng dẫn. Trước khi dùng thật, nhất là khi mở ra Internet qua dịch vụ nước ngoài như Cloudflare, cần bộ phận pháp chế và CNTT của bệnh viện đánh giá và phê duyệt.

## 8. Cập nhật và gỡ bỏ

- Cập nhật: build bản mới, chạy lại `CAI-DAT-MAY-CHU.cmd` (giữ nguyên dữ liệu, tài khoản, máy đã ghép) và `CAI-DAT-MAY-BAC-SI.cmd` trên từng máy bác sĩ.
- Gỡ: `uninstall-server.ps1` (thêm `-RemoveData` để xóa dữ liệu), `uninstall-assistant.ps1` (thêm `-RemoveSettings` để xóa ghép nối và profile đã hiệu chỉnh).

## 9. Xử lý sự cố

| Hiện tượng | Cách xử lý |
| --- | --- |
| Máy khác không mở được trang | Kiểm tra dịch vụ **UMC2 Intake Server** đang chạy; ping IP máy chủ; tường lửa/antivirus có chặn cổng 8080/8081; hai máy cùng dải IP nội bộ. |
| Trang quản trị báo "chỉ phục vụ localhost" | Chưa chạy script cài bằng quyền Admin (thiếu đăng ký URL http.sys). |
| "Tự tìm" không thấy máy chủ | Mạng chặn broadcast hoặc khác VLAN: nhập tay `http://<IP máy chủ>:8080`. |
| Trợ lý không thấy bệnh nhân trên HIS | Bấm **Nhận diện lại** khi đang mở hồ sơ; kiểm tra selector `PatientId` (mục 5); chạy trợ lý cùng mức quyền với HIS. |
| Bảng gọn báo "Không có tờ khai" | Điều dưỡng đã duyệt chưa? Mã BN điều dưỡng nhập có đúng mã đang mở trên HIS? Tích "Hiện tất cả tờ khai đã duyệt" để chọn tay. |
| Điền báo "Không tìm thấy control" | Màn hình HIS khác profile hoặc control không expose UI Automation: hiệu chỉnh lại (mục 5). |
| "Khóa của máy này đã bị thu hồi" | Tạo mã ghép nối mới và ghép lại. |
| Windows SmartScreen chặn file .exe | File chưa ký số: bấm **More info → Run anyway**, hoặc để CNTT ký số nội bộ. |
