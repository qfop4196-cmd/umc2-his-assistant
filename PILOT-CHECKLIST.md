# Checklist pilot HIS

## Chuẩn bị

- Chỉ dùng người bệnh thử hoặc hồ sơ đã khử nhận diện trong lượt đầu.
- Máy chủ tờ khai đã cài (`CAI-DAT-MAY-CHU.cmd`), đã tạo tài khoản quản trị và ít nhất 1 tài khoản điều dưỡng.
- Máy bác sĩ đã cài trợ lý, đã ghép nối (tab **Tờ khai BN** hiện chấm xanh "Đã kết nối").
- Sao lưu profile hiện có tại `%LOCALAPPDATA%\UMC2\HisAdmissionAssistant\Profiles`.
- Mở HIS và trợ lý cùng mức quyền; không chạy một app bằng Administrator và app còn lại bằng quyền thường.

## Lượt 1 — Mock HIS (không cần HIS thật)

1. Bấm đúp `tools\Pilot\KIEM-THU-TU-DONG.cmd` (tương đương `.\build.ps1 -Test -Smoke`): phải báo **KẾT QUẢ: PASS**; nhật ký ở `dist\pilot\kiem-thu.log`.
2. Bấm đúp `tools\Pilot\CHAY-THU-MOCK-HIS.cmd`: mở máy chủ tờ khai **tạm** (chỉ localhost, cổng 18080/18081, dữ liệu thử), Mock HIS và trợ lý. Trên Mock HIS chọn **BN-TEST-001**.
   Trợ lý: tab **Tờ khai BN → Ghép nối máy chủ…** → `http://localhost:18080` + mã 6 số tạo ở **Quản trị → Máy bác sĩ** (lần đầu mở `http://localhost:18080/` để tạo tài khoản quản trị: tên đăng nhập chữ thường không dấu, mật khẩu ≥ 8 ký tự có cả chữ và số).
   Dữ liệu thử nằm ở `%LOCALAPPDATA%\UMC2\PilotIntakeData` và được giữ giữa các lần chạy (tài khoản, ghép nối vẫn còn); muốn làm lại từ đầu bấm `LAM-LAI-TU-DAU.cmd`.
3. Mở `http://localhost:18081/` (hoặc điện thoại cùng mạng khi dùng máy chủ thật): khai một tờ khai thử, ghi lại mã tờ khai.
4. Trang điều dưỡng: tìm mã tờ khai → nhập mã BN `BN-TEST-001` → nhập sinh hiệu → **Duyệt & chuyển bác sĩ**.
5. Đưa cửa sổ Mock HIS lên trước → trợ lý tự nhận profile **Kiểm thử — Mock HIS**, bảng gọn phải hiện **✔ Có tờ khai**.
6. Bấm **ĐIỀN VÀO HIS** → kiểm tra các ô trên Mock HIS; bộ đếm **Số lần bấm Lưu** vẫn là 0.
7. Đổi Mock HIS sang **BN-TEST-002** → bảng gọn phải đổi bệnh nhân và báo "Không có tờ khai".
8. Quay lại BN-TEST-001, bấm **✓ ĐÃ LƯU** → trang điều dưỡng chuyển tờ khai sang **Đã nhập HIS**.

## Lượt 2 — HIS thật, bệnh nhân thử

1. Mở đúng hồ sơ bệnh nhân thử trên màn hình **Phiếu khám vào viện** (hoặc **Khám bệnh**).
2. Tab **Hiệu chỉnh UIA → Kiểm tra selector** (profile đã có sẵn `hoten`, `namsinh`, mã BN). Cột trạng thái chỉ báo *số chữ số/ký tự*, không hiện giá trị:
   - Phiếu khám vào viện: `PatientId` → "đọc được N chữ số" (ô `mabn`), `PatientName` → "đọc được … ký tự". Phiếu này **không có ô năm sinh**.
   - Khám bệnh: `PatientId` → kiểu "mabn1: 2 chữ số · mabn3: 6 chữ số". Nếu `mabn1` đã đủ mã (ví dụ 8 chữ số) thì sửa `AutomationId` của `PatientId` thành `mabn1`; `BirthYear` → "đọc được 4 chữ số".
   - Ô nào "Không tìm thấy": chọn trường đó, **Bắt control sau 3 giây**, rê chuột lên ô trên HIS → **Lưu profile**.
3. Bảng gọn phải hiện đúng *Mã BN · Họ tên (năm sinh)* và so khớp với mã BN in trên HIS. Nếu không: **Nhận diện lại**, kiểm tra selector `PatientId`.
   ICD-10 chính và Khoa là ô tra cứu — trợ lý không điền, bác sĩ chọn trên HIS.
4. Khai tờ khai thử → điều dưỡng duyệt với đúng mã BN thử.
5. Trợ lý: **ĐIỀN VÀO HIS** → kiểm tra trực tiếp từng trường, dấu xuống dòng, sinh hiệu → tự bấm **Lưu** trên HIS.
6. Đóng và mở lại hồ sơ trên HIS để xác nhận dữ liệu thực sự đã lưu; bấm **✓ ĐÃ LƯU** trên trợ lý.
7. Thử cố ý sai: duyệt tờ khai với mã BN khác → trợ lý phải KHÔNG đề xuất tờ khai đó cho bệnh nhân đang mở.
8. Thử ô đã có chữ: gõ sẵn nội dung vào "Lý do vào viện" trên HIS → trợ lý phải hỏi và mặc định chỉ điền ô trống.

## Lượt 3 — Người bệnh khai từ nhà (tùy chọn, cần phê duyệt của bệnh viện)

1. Bật **Đường hầm tạm** (thử) hoặc cấu hình Cloudflare Tunnel có tên miền.
2. Dùng 4G (không dùng Wi-Fi bệnh viện) mở địa chỉ Internet → khai tờ khai thử.
3. Xác nhận tờ khai hiện nhãn **Internet** trên trang điều dưỡng.
4. Từ 4G, thử mở `https://<địa chỉ Internet>/api/staff/bootstrap` → phải báo không tìm thấy (trang điều dưỡng không ra Internet).

## Điều kiện dừng ngay

- Mã bệnh nhân không khớp hoặc trợ lý nhận sai bệnh nhân.
- Selector trỏ nhầm control, trường bị khóa hoặc form đổi bố cục.
- HIS xuất hiện hộp thoại cảnh báo/xác nhận ngoài dự kiến.
- Nội dung soạn tự động sai nghĩa lâm sàng mà điều dưỡng không phát hiện.
- Biểu mẫu yêu cầu chữ ký điện tử.

Sau pilot: gửi lại nhật ký kỹ thuật (tab **Nhật ký phiên**, `logs\server-*.log`) — không gửi tên, mã bệnh nhân hay nội dung lâm sàng.
