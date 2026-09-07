# Checklist pilot HIS — 07/09/2026

## Chuẩn bị

- Chỉ dùng người bệnh thử hoặc hồ sơ đã khử nhận diện trong lượt đầu.
- Sao lưu profile hiện có tại `%LOCALAPPDATA%\UMC2\HisAdmissionAssistant\Profiles`.
- Mở HIS và HIS Admission Assistant cùng mức quyền; không chạy một app bằng Administrator và app còn lại bằng quyền thường.
- Để chế độ **Tự điền + Lưu + Đóng** tắt trong lượt đầu.

## Lượt thử tờ vào viện

1. Trên webapp, tạo lượt nhập viện thử và mở QR bằng trình duyệt đã đăng nhập.
2. Gửi bản khai, nhập đúng một ICD chính, kiểm tra đề xuất CLS.
3. Chọn **Hồ sơ & HIS → Tải gói pilot ngoại tuyến**.
4. Mở đúng hồ sơ và đúng màn hình **Phiếu khám vào viện** trên HIS.
5. Trong app Windows chọn **Đồng bộ webapp → Nhập gói pilot JSON**.
6. Kiểm tra mã bệnh nhân trong lưới phải trùng mã đang hiển thị trên HIS.
7. Bấm **Kiểm tra selector**. Nếu bất kỳ trường bắt buộc nào không tìm thấy, dừng và dùng **Hiệu chỉnh UIA** để bắt lại control.
8. Bấm **ĐIỀN VÀO HIS**, kiểm tra trực tiếp từng trường và tự bấm Lưu.
9. Xác nhận HIS đã lưu đúng hồ sơ; đóng và mở lại biểu mẫu để kiểm tra dữ liệu thực sự tồn tại.

## Chỉ sau khi lượt thủ công đạt

1. Trong **Hiệu chỉnh UIA**, quét và gán chính xác nút **Lưu** và **Đóng**.
2. Giới hạn `WindowTitleRegex` để chỉ khớp đúng màn hình cần điền.
3. Tích **Cho phép Lưu/Đóng tự động**, lưu profile rồi thử lại bằng hồ sơ giả thứ hai.
4. Chỉ bật tự động khi cửa sổ HIS tìm thấy đúng một kết quả.

## Điều kiện dừng ngay

- Mã bệnh nhân không khớp.
- Có hơn một cửa sổ HIS phù hợp.
- Selector trỏ nhầm control, trường bị khóa hoặc form đổi bố cục.
- HIS xuất hiện hộp thoại cảnh báo/xác nhận ngoài dự kiến.
- Dữ liệu OCR chưa được nhân viên y tế xác minh.
- Biểu mẫu yêu cầu chữ ký điện tử.

Sau pilot, xóa gói JSON nếu có dữ liệu thật và gửi lại nhật ký chỉ gồm thông báo kỹ thuật; không gửi tên, mã bệnh nhân hay nội dung lâm sàng.
