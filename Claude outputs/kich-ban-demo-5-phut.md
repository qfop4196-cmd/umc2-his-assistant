# Kịch bản demo 5 phút — UMC2 HIS Suite (có Mock HIS)

## Chuẩn bị trước (làm 1 lần, ~5 phút, không tính vào demo)

Tất cả trên máy chủ demo, thư mục `Máy tính\Github-Thi\umc2-his-assistant`.

1. Cửa sổ **BUILD-VA-CHAY-DEMO.cmd** đang chạy (máy chủ + 2 tunnel). Không đóng.
2. Mở `src\HisAdmissionAssistant\bin\Release\HisAdmissionAssistant.exe` (trợ lý máy bác sĩ).
3. Mở `tools\MockHis\bin\Release\MockHis.exe` (HIS giả).
4. Ghép nối trợ lý với máy chủ:
   - Trình duyệt: `http://localhost:8080` → đăng nhập **giamkhao / giamkhao123** → **Quản trị → Máy bác sĩ → Tạo mã ghép nối máy bác sĩ** → được mã 6 số.
   - Trợ lý: tab **Tờ khai BN → Ghép nối máy chủ… → Tự tìm** (hoặc gõ `http://localhost:8080`) → nhập mã 6 số → **Ghép nối**.
   - Trợ lý: chọn profile **Kiểm thử — Mock HIS**.
5. Chuẩn bị sẵn 1 tờ khai để duyệt trong demo: mở tab **Chờ duyệt**, chọn 1 tờ khai (ví dụ "Nguyễn Văn Mẫu"), nhớ tên + năm sinh. Chưa duyệt.
6. Trên Mock HIS: chọn bệnh nhân **BN-TEST-001 — NGUYỄN VĂN TEST — 1970**. Đây là mã BN sẽ dùng khi duyệt.
7. Thử chạy trơn 1 lần trước buổi chấm. Nếu tờ khai đã nhập HIS rồi thì vào tờ khai đó → **Mở lại** để dùng lại.

Sắp cửa sổ: trình duyệt bên trái (trang điều dưỡng), Mock HIS + trợ lý bên phải.

---

## Demo (5 phút)

### 0:00 – 0:45 · Bài toán
> "Hiện nay bệnh nhân đến khám phải khai lại thông tin nhiều lần, bác sĩ mất thời gian gõ lại bệnh sử vào HIS. Hệ thống này cho bệnh nhân tự khai trước, điều dưỡng duyệt, và trợ lý trên máy bác sĩ điền thẳng vào HIS — không đụng vào cơ sở dữ liệu HIS."

Chỉ vào sơ đồ 3 bước: **Người bệnh → Điều dưỡng → Máy bác sĩ**.

### 0:45 – 1:45 · Người bệnh khai trên điện thoại
- Đưa QR (đã in: `qr-to-khai-benh-nhan.png`) hoặc mở link `?to=patient` trên điện thoại.
- Điền nhanh: họ tên, năm sinh, lý do khám, chọn vài triệu chứng, dị ứng → **Gửi**.
- Nhấn mạnh: không cần tải app, không tài khoản, dữ liệu mã hóa, tự xóa theo hạn lưu trữ.

### 1:45 – 3:00 · Điều dưỡng duyệt
- Trang điều dưỡng (đăng nhập **dieuduong / Dieuduong2026demo**): tờ khai vừa gửi hiện ngay ở **Chờ duyệt** (có chuông báo).
- Mở tờ khai, nhập sinh hiệu (mạch, HA, nhiệt), nhập **Mã BN HIS = `BN-TEST-001`**.
- Bấm **Duyệt & chuyển bác sĩ** → tờ khai sang tab **Chờ bác sĩ**.
- Nói: "Điều dưỡng chỉ đối chiếu và gắn mã bệnh nhân. Từ đây không ai phải gõ lại."

### 3:00 – 4:15 · Bác sĩ: một cú bấm vào HIS
- Chuyển sang Mock HIS (đang mở BN-TEST-001). Trợ lý tự nhận ra mã BN và hiện tờ khai đã duyệt (bảng nổi).
- Bấm **Điền vào HIS** → các ô Lý do vào viện, bệnh sử, tiền sử, dị ứng, sinh hiệu được điền.
- Chỉ vào bộ đếm **"Số lần bấm Lưu: 0"** trên Mock HIS: "Trợ lý không bao giờ tự lưu, bác sĩ kiểm tra rồi tự bấm Lưu."
- Quay lại trang điều dưỡng: tờ khai đã nhảy sang **Đã nhập HIS**.

### 4:15 – 5:00 · An toàn & mở rộng
- **Quản trị → Tổng quan**: thống kê, xuất Excel, nhật ký thao tác.
- Điểm an toàn (nói nhanh): đối chiếu mã BN 3 lớp (sai bệnh nhân là hủy điền), máy bác sĩ ghép bằng mã 1 lần và thu hồi được, dữ liệu chỉ nằm trong RAM ở máy bác sĩ, AI gợi ý đã bỏ định danh.
- Kết: chạy trên UMC2HIS thật chỉ cần đổi profile (đã có sẵn 3 màn hình UMC2), không cần HIS cấp API.

---

## Phòng hờ

| Sự cố | Xử lý |
|---|---|
| Trợ lý không hiện tờ khai | Kiểm tra Mock HIS đang mở đúng BN-TEST-001 và tờ khai được duyệt với đúng mã đó. Trong trợ lý bấm **Đọc lại**. |
| Tunnel đổi địa chỉ (link ?to= chết) | Lấy 2 địa chỉ mới trong cửa sổ đen, sửa `docs/redirect/targets.json` trên github.com. Trong lúc chờ, dùng `http://192.168.1.2:8081` trên điện thoại cùng Wi‑Fi. |
| Điện thoại không có mạng | Nhập tờ khai ngay trên trình duyệt máy chủ ở tab thứ hai (`localhost:8081`). |
| Trợ lý bị mất ghép nối | Tạo mã mới ở Quản trị → Máy bác sĩ, ghép lại (30 giây). |
