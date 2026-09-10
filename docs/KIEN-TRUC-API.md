# Kiến trúc & API

## Nguyên tắc thiết kế

1. **Không đụng HIS**: không đọc/ghi database hay service nội bộ của UMC2HIS; chỉ UI Automation trên màn hình người dùng đã mở.
2. **Con người quyết định**: người bệnh khai → điều dưỡng duyệt → bác sĩ bấm điền và tự Lưu. Không có bước nào tự lưu hay tự ký hồ sơ.
3. **Dễ cài**: .NET Framework 4 có sẵn trên Windows, build bằng `csc.exe` của Windows (C# 5), không NuGet. Máy chủ là một file exe có giao diện web nhúng bên trong.
4. **Tối thiểu dữ liệu**: máy chủ là hàng chờ tạm, mã hóa, tự xóa; máy bác sĩ chỉ giữ dữ liệu trong RAM.

## Thành phần máy chủ (`src/IntakeServer`)

| File | Vai trò |
| --- | --- |
| `ServerHost.cs` | Khởi tạo cấu hình, kho dữ liệu, 2 cổng HTTP, dò tìm UDP, đường hầm, bảo trì định kỳ (xóa hết hạn, trả tờ khai bị giữ quá 4 giờ). |
| `HttpHost.cs` | HttpListener (http.sys), header bảo mật (CSP, nosniff, frame-deny), gzip, giới hạn kích thước body. |
| `PublicEndpoints.cs` | **Cổng người bệnh (8081)**: chỉ trang tờ khai + `POST /api/public/intakes`. Được phép đưa ra Internet. |
| `StaffEndpoints.cs` | **Cổng nhân viên (8080)**: trang điều dưỡng, thiết lập, tài khoản, cài đặt, nhật ký. Từ chối mọi yêu cầu từ IP công cộng hoặc có header proxy/đường hầm. |
| `AgentEndpoints.cs` | API cho HIS Assistant (cùng cổng 8080, xác thực bằng khóa thiết bị). |
| `IntakeStore.cs` | Mỗi tờ khai một file `.dat` mã hóa DPAPI (LocalMachine + entropy riêng), ghi nguyên tử; ghép theo mã BN/họ tên + năm sinh. |
| `Security.cs` | PBKDF2 (120.000 vòng), phiên đăng nhập, giới hạn tần suất, mã ghép nối, kiểm tra IP nội bộ. |
| `TunnelManager.cs` | Chạy `cloudflared` cho đường hầm tạm (chỉ trỏ tới cổng người bệnh). |
| `Discovery.cs` | Trả lời gói UDP `UMC2-INTAKE-DISCOVER v1` (cổng 47810) để máy bác sĩ tự tìm máy chủ. |
| `wwwroot/public` | Tờ khai 5 bước cho người bệnh (điện thoại/kiosk). |
| `wwwroot/staff` | Trang duyệt, soạn nội dung HIS (`compose.js`), quản trị, in áp phích QR. |
| `wwwroot/shared` | CSS dùng chung, biểu tượng. |

Trạng thái tờ khai: `pending` (chờ duyệt) → `approved` (chờ bác sĩ) → `claimed` (máy bác sĩ đang điền) → `completed` (đã nhập HIS). Điều dưỡng có thể `rejected` hoặc mở lại.

## Thành phần máy bác sĩ (`src/HisAdmissionAssistant`)

| File | Vai trò |
| --- | --- |
| `PatientContextWatcher.cs` | Luồng nền đọc mã BN (và họ tên/năm sinh nếu đã hiệu chỉnh) trên cửa sổ HIS đang ở phía trước. Lưu cache control nên mỗi lần kiểm tra chỉ tốn 1 lần đọc giá trị; giữ nguyên bệnh nhân khi mở hộp thoại phụ của HIS. |
| `IntakeClient.cs` | Gọi API máy chủ (ghép nối, ghép tờ khai, nhận, hoàn tất) và dò tìm UDP. |
| `ConnectionSettings.cs` | Địa chỉ máy chủ + khóa thiết bị, mã hóa DPAPI theo người dùng Windows. |
| `MainForm.Intake.cs` | Tab **Tờ khai BN**: đọc lại mã BN trước khi điền, xin xác nhận khi chỉ ghép theo họ tên, hỏi trước khi ghi đè, điền bằng engine an toàn sẵn có, đưa cửa sổ HIS lên trước để bác sĩ kiểm tra. |
| `QuickPanel.cs` | Bảng gọn luôn nổi: BN đang mở, trạng thái tờ khai, nút **ĐIỀN VÀO HIS** / **✓ ĐÃ LƯU**. |
| `UiaAutomationService.cs` | Engine UI Automation: preflight toàn bộ, đối chiếu `Verify`, trường `Read` không bao giờ ghi, CRLF cho ô nhiều dòng, không đoán control khi thiếu selector, chặn nút Lưu. |

Profile XML: `Operation="Set"` (điền), `"Verify"` (đọc để đối chiếu, bắt buộc khớp), `"Read"` (chỉ đọc để ghép tờ khai). Khóa (`Key`) trùng với tên trường máy chủ gửi về.

| Key | Nguồn |
| --- | --- |
| `PatientId` | Mã BN điều dưỡng xác nhận (chỉ dùng để đối chiếu, không ghi) |
| `ReasonForAdmission`, `History`, `PastHistory`, `FamilyHistory`, `Allergy`, `Symptoms` | Soạn từ câu trả lời của người bệnh, điều dưỡng sửa rồi duyệt |
| `Pulse`, `Temperature`, `BloodPressure`, `RespiratoryRate`, `SpO2`, `Weight`, `Height` | Điều dưỡng đo (cân nặng/chiều cao lấy sẵn từ người bệnh) |
| `PatientName`, `BirthYear` | Đọc từ màn hình HIS để ghép (không gửi về, không ghi) |

## API

Mọi phản hồi là JSON; lỗi có dạng `{"error": "<mã>", "message": "<tiếng Việt>"}`.

### Cổng người bệnh (8081)

| Phương thức | Đường dẫn | Ghi chú |
| --- | --- | --- |
| GET | `/api/public/config` | Tên bệnh viện, phiên bản biểu mẫu |
| POST | `/api/public/intakes` | Body `{formVersion, consent: true, patient: {fullName, birthDate "yyyy" hoặc "yyyy-mm-dd", gender, phone, nationalId?, hisPatientId?, filledBy, relation?}, answers: {...}}` → `201 {code}`. Giới hạn 64 KB, 6 lần / IP / 10 phút. |
| GET | `/api/public/health` | Kiểm tra sống |

### Cổng nhân viên (8080, chỉ LAN)

Yêu cầu thay đổi dữ liệu phải có header `X-UMC2: 1` (chống CSRF) và cookie phiên `umc2_staff`.

| Phương thức | Đường dẫn | Quyền |
| --- | --- | --- |
| GET | `/api/staff/bootstrap` | Công khai trong LAN |
| POST | `/api/setup` | Chỉ khi chưa có tài khoản, chỉ từ `localhost` |
| POST | `/api/staff/login`, `/api/staff/logout`, `/api/staff/password` | — |
| GET | `/api/staff/intakes?status=pending\|approved\|completed\|rejected\|all&q=` | Điều dưỡng |
| GET | `/api/staff/intakes/{id}` | Điều dưỡng (ghi nhật ký xem) |
| POST | `/api/staff/intakes/{id}/review` `{action: save\|approve, hisPatientId, fields, note}` | Điều dưỡng |
| POST | `/api/staff/intakes/{id}/reject` `{reason}`, `/reopen` | Điều dưỡng |
| DELETE | `/api/staff/intakes/{id}` | Quản trị |
| GET | `/api/staff/access` | Địa chỉ LAN/Internet để in QR |
| GET/POST | `/api/staff/users`, `/api/staff/settings`, `/api/staff/devices`, `/api/staff/devices/pair-code`, `/api/staff/devices/{id}/revoke`, `/api/staff/tunnel`, `/api/staff/audit` | Quản trị |

### API máy bác sĩ (8080, header `Authorization: Bearer <khóa thiết bị>`)

| Phương thức | Đường dẫn | Ghi chú |
| --- | --- | --- |
| POST | `/api/agent/pair` `{code, deviceName, machineName}` | Không cần khóa; mã 6 số dùng 1 lần → `{token}` |
| GET | `/api/agent/ping` | Kiểm tra khóa |
| POST | `/api/agent/intakes/match` `{patientId, patientName, birthYear}` | Trả tờ khai đã duyệt kèm `score` (100 = trùng mã BN điều dưỡng xác nhận; 90 = họ tên + năm sinh) và `fields` |
| GET | `/api/agent/intakes` | Tờ khai đã duyệt 48 giờ gần nhất (không kèm nội dung) |
| GET | `/api/agent/intakes/{id}` | Nội dung một tờ khai đã duyệt |
| POST | `/api/agent/intakes/{id}/claim` `{hisPatientId, force}` | Từ chối (`409 patient_mismatch`) nếu mã BN trên màn hình khác mã đã duyệt |
| POST | `/api/agent/intakes/{id}/release` | Trả về hàng chờ |
| POST | `/api/agent/intakes/{id}/complete` `{result: completed\|needs_human, summary, hisPatientId, filledCount}` | Hoàn tất |
| POST | `/api/agent/jobs/claim`, `/api/agent/jobs/complete` | Tương thích `CloudQueueClient` cũ (`signaturePolicy` luôn là `manual`) |

## Dữ liệu trên đĩa (máy chủ)

```
C:\ProgramData\UMC2\IntakeServer\
  config\server.json   cấu hình, tài khoản (hash PBKDF2), máy bác sĩ (hash SHA-256 của khóa)
  config\data.key      entropy DPAPI
  intakes\*.dat        tờ khai mã hóa (xóa đè trước khi xóa file)
  logs\server-*.log    nhật ký kỹ thuật
  logs\audit-*.log     nhật ký kiểm toán (không nội dung lâm sàng)
```
