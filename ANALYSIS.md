# Phân tích UMC2HIS phục vụ automation nhập viện

## Phạm vi dữ liệu nhận được

Workspace và ba archive (`bin_umc.rar`, `bin.rar`, `UMC2HIS41.rar`) chỉ chứa binary/runtime; không có `.sln`, `.csproj`, `.cs` hoặc `.vb`. Vì vậy kết luận dưới đây dựa trên metadata .NET/PDB và tên thành phần, chưa phải code review đầy đủ ở mức câu lệnh.

Không sử dụng các PDF/XML/HL7 có khả năng chứa dữ liệu bệnh nhân trong quá trình phân tích.

## Kiến trúc nhận diện được

- `UMC2HIS.exe`: .NET Framework CLR v4, WinForms, Product `MQHIS`, version `10.2.2.2016`, hãng `MQ Solutions`.
- UI dùng kết hợp WinForms chuẩn, DevComponents DotNetBar, DevExpress, `MaskedBox`, `MaskedTextBox`, `LibList` và `ListLookupICD`.
- Các assembly nghiệp vụ đáng chú ý: `MQEMR.dll`, `LibHIS.dll`, `LibHISExtension.dll`, `MQLIB.dll`.
- Có module dữ liệu/Oracle nội bộ, nhưng app companion không gọi database/service nội bộ để tránh phụ thuộc và vượt ranh giới tích hợp được yêu cầu.

## Luồng và form liên quan

1. Tiếp đón: `MQHIS.frmTiepdonDHYD` có các control hành chính và logic tiếp đón.
2. Khám bệnh/chỉ định nhập viện: `MQHIS.frmKhambenh1` có `mabn1`, `lydo`, `trieuchung`, `icd_chinh`, `khoa`, `mabs`, `tenbs`, `sovaovien`, `butchonkhoa`, `butLuu`.
3. Chọn phòng/giường: `MQHIS.frmChonkhoagiuong` có `phong`, `listBox1`, `butOk`, `butKetthuc`.
4. Phiếu khám vào viện EMR: `MQEMR.frmKhambenhvv` có `mabn`, `lydo`, `benhly`, `banthan`, `giadinh`, `diung`, `toanthan`, `bophan`, `tomtat`, `chandoan`, `sobo`, `xuli`, `chuy`, dấu sinh tồn và `butLuu`.
5. Model `MQEMR.Models.ThongTinVaoVien` xác nhận các khái niệm: lý do vào viện, quá trình bệnh lý, chẩn đoán sơ bộ/vào viện, ICD, khoa điều trị, bác sĩ khám và dấu sinh tồn.

## Kết luận kỹ thuật

HIS là ứng viên tốt cho UI Automation vì phần lớn trường là WinForms `TextBox`, `ComboBox`, `ListBox`. Nhóm custom control có thể expose UIA không đầy đủ; vì vậy app được thiết kế theo mô hình selector cấu hình được và có công cụ capture/scan tại máy đích.

MVP không tự điều hướng toàn bộ menu và không tự bấm Lưu. Nó tự điền trên màn hình người dùng đã mở, đối chiếu mã bệnh nhân trước khi ghi, rồi trả quyền quyết định cho nhân viên y tế.

## Việc cần xác nhận trên máy HIS thật

1. UIA `AutomationId` thực tế của khoảng 15–20 control trong ba form trên.
2. Tiêu đề cửa sổ ổn định để thu hẹp `WindowTitleRegex` (profile ban đầu cố ý dùng regex rộng và lọc theo process).
3. Pattern của `LibList` và DotNetBar trên máy thật (`MaskedBox`/`MaskedTextBox` là TextBox; `ListLookupICD`/`ListLookup` là UserControl — xem mục dò control bên dưới).
4. Xác nhận `mabn1`+`mabn3` đúng là mã BN đầy đủ trên màn hình Khám bệnh (Kiểm tra selector phải báo kiểu "mabn1: 2 chữ số · mabn3: 6 chữ số").
5. Luồng mở lần lượt form khám, phiếu vào viện và chọn phòng/giường tại cấu hình triển khai của bệnh viện.

## Cập nhật 09/2026 — luồng tờ khai trước khám

- Thêm máy chủ LAN `UMC2 Intake Server` (tờ khai người bệnh, điều dưỡng duyệt, hàng chờ máy bác sĩ); không kết nối database HIS.
- Máy bác sĩ tự nhận bệnh nhân đang mở bằng cách **đọc** control mã BN (`mabn` trên `frmKhambenhvv`, `mabn1`+`mabn3` trên `frmKhambenh1`/`frmKhambenhDHYD`) qua UI Automation, rồi ghép với tờ khai điều dưỡng đã gắn đúng mã BN.
- Họ tên và năm sinh BN đã xác định được từ metadata (xem mục dưới), profile đã điền sẵn selector.

## Control họ tên / năm sinh / mã BN (dò từ metadata, 11/09/2026)

Nguồn: tên và kiểu field của các form trong `UMC2HIS.exe`, `MQEMR.exe`, `MQEMR.dll` và kiểu cơ sở của `MaskedTextBox.dll`, `MaskedBox.dll`, `ListLookup.dll`, `ListLookupICD.dll` (chỉ đọc metadata; không mở dữ liệu, cấu hình hay IL). WinForms dùng tên control làm UIA `AutomationId`.

| Màn hình | Mã BN | Họ tên | Năm sinh | Ghi chú |
|---|---|---|---|---|
| `MQEMR.frmKhambenhvv` — Phiếu khám vào viện (có trong cả `MQEMR.exe` và `MQEMR.dll`) | `mabn` (TextBox) | `hoten` (TextBox) | **không có** | Biến thể `frmKhambenhvvmat` (mắt), `frmKhambenhvvtmh` (TMH) cùng tên ô chữ nhưng không có ô sinh hiệu và `diung` |
| `MQHIS.frmKhambenhDHYD` và `MQHIS.frmKhambenh1` — Khám bệnh | `mabn1` + `mabn3` (MaskedTextBox) | `hoten` (TextBox) | `namsinh` (MaskedTextBox) | `ngaysinh` (MaskedBox) = ngày sinh đầy đủ; `qh_hoten` là họ tên **thân nhân** — không dùng |
| `MQHIS.frmTiepdonDHYD` — Tiếp đón | `mabn1` (MaskedTextBox) + `mabn3` (TextBox) | `hoten` | `namsinh` | Không nằm trong luồng bác sĩ |

- **Mã BN trên màn hình Khám bệnh nằm ở hai ô**: form có sự kiện `mabn3_Validated` (không có `mabn1_Validated`) và tùy chọn `chkyymabn` → `mabn1` nhiều khả năng là 2 số năm, `mabn3` là số thứ tự. Profile 02 đối chiếu bằng selector ghép chỉ đọc `mabn1+mabn3`; nếu thực tế `mabn1` đã chứa đủ mã thì đổi lại `mabn1` (tab Hiệu chỉnh → Kiểm tra selector cho biết mỗi ô đang có bao nhiêu chữ số, không hiện giá trị).
- `MaskedTextBox.MaskedTextBox` và `MaskedBox.MaskedBox` kế thừa `System.Windows.Forms.TextBox` → UIA thấy là `Edit`, ghi được bằng ValuePattern/WM_SETTEXT (sinh hiệu, năm sinh).
- `ListLookup.ListLookUp` (khoa) và `ListLookupICD.ListLookUp` (ICD) kế thừa `UserControl` chứa DevExpress `TextEdit`/`SearchLookUpEdit` → **không nhận chữ qua UIA**; đã bỏ `icd_chinh`, `khoa` khỏi profile 02, bác sĩ chọn trực tiếp trên HIS. Trợ lý giờ kiểm tra trước mọi ô sẽ ghi: gặp ô không nhận chữ hoặc đang chỉ đọc thì dừng toàn bộ, không điền dở dang.

