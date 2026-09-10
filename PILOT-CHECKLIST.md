# Checklist pilot HIS

## Chuẩn bị

- Chỉ dùng người bệnh thử hoặc hồ sơ đã khử nhận diện trong lượt đầu.
- Máy chủ tờ khai đã cài (`CAI-DAT-MAY-CHU.cmd`), đã tạo tài khoản quản trị và ít nhất 1 tài khoản điều dưỡng.
- Máy bác sĩ đã cài trợ lý, đã ghép nối (tab **Tờ khai BN** hiện chấm xanh "Đã kết nối").
- Sao lưu profile hiện có tại `%LOCALAPPDATA%\UMC2\HisAdmissionAssistant\Profiles`.
- Mở HIS và trợ lý cùng mức quyền; không chạy một app bằng Administrator và app còn lại bằng quyền thường.

## Lượt 1 — Mock HIS (không cần HIS thật)

1. Chạy `.\build.ps1 -Test -Smoke` trên máy lập trình: cả hai bước phải báo PASS.
2. Mở `tools\MockHis\bin\Release\MockHis.exe`, chọn **BN-TEST-001**.
3. Trên điện thoại/máy tính bảng: khai một tờ khai thử, ghi lại mã tờ khai.
4. Trang điều dưỡng: tìm mã tờ khai → nhập mã BN `BN-TEST-001` → nhập sinh hiệu → **Duyệt & chuyển bác sĩ**.
5. Đưa cửa sổ Mock HIS lên trước → trợ lý tự nhận profile **Kiểm thử — Mock HIS**, bảng gọn phải hiện **✔ Có tờ khai**.
6. Bấm **ĐIỀN VÀO HIS** → kiểm tra các ô trên Mock HIS; bộ đếm **Số lần bấm Lưu** vẫn là 0.
7. Đổi Mock HIS sang **BN-TEST-002** → bảng gọn phải đổi bệnh nhân và báo "Không có tờ khai".
8. Quay lại BN-TEST-001, bấm **✓ ĐÃ LƯU** → trang điều dưỡng chuyển tờ khai sang **Đã nhập HIS**.

## Lượt 2 — HIS thật, bệnh nhân thử

1. Mở đúng hồ sơ bệnh nhân thử trên màn hình **Phiếu khám vào viện** (hoặc **Khám bệnh**).
2. Tab **Hiệu chỉnh UIA → Quét control**: xác nhận `AutomationId` của mã BN và các ô cần điền; bắt thêm **Họ tên BN** và **Năm sinh BN**; **Lưu profile**.
3. Bảng gọn phải hiện đúng *Mã BN · Họ tên (năm sinh)*. Nếu không: **Nhận diện lại**, kiểm tra selector `PatientId`.
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
