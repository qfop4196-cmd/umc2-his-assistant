# HIS Admission Assistant

Ứng dụng Windows chạy song song với UMC2HIS và điền dữ liệu qua Microsoft UI Automation. Ứng dụng không sửa source/database HIS. Chế độ thủ công vẫn dừng trước **Lưu/Xác nhận**; chế độ webapp chỉ được phép bấm **Lưu/Đóng** cho biểu mẫu không yêu cầu ký và profile đã được hiệu chỉnh rõ ràng.

## Chạy nhanh

1. Chạy `build.ps1` bằng PowerShell, hoặc mở `HisAdmissionAssistant.sln` trong Visual Studio và build `Release`.
2. Mở màn hình cần nhập trên HIS.
3. Chạy `src\HisAdmissionAssistant\bin\Release\HisAdmissionAssistant.exe` cùng mức quyền với HIS.
4. Chọn profile, bấm **Tìm cửa sổ**, chọn đúng cửa sổ.
5. Nhập mã bệnh nhân để đối chiếu và các nội dung cần điền.
6. Bấm **Kiểm tra selector** trước. Chỉ khi kết quả đúng mới bấm **ĐIỀN VÀO HIS**.
7. Ở chế độ thủ công, kiểm tra lại toàn bộ trên HIS và tự bấm Lưu nếu phù hợp.

## Đồng bộ với webapp

1. Đặt `HIS_AGENT_API_BASE` thành URL của webapp và `HIS_AGENT_SECRET` thành khóa tác nhân do máy chủ cấp; hoặc nhập hai giá trị ở tab **Đồng bộ webapp**. Khóa chỉ nằm trong RAM.
2. Bấm **Nhận tác vụ kế tiếp**. App chọn profile theo thuộc tính XML `CloudFormCode` và nạp các trường vào RAM.
3. Mặc định người dùng kiểm tra, điền và tự Lưu. Sau đó bấm **Đánh dấu hoàn tất** để trả trạng thái về webapp.
4. Sau lượt thử thủ công thành công, sang **Hiệu chỉnh UIA**, bấm **Quét control**, chọn đúng dòng nút rồi dùng **Gán dòng scan → Lưu/Đóng**. Tích **Cho phép Lưu/Đóng tự động** và lưu profile. Chế độ này không bao giờ xử lý tác vụ yêu cầu chữ ký.

### Chạy pilot ngoại tuyến

Nếu mạng bệnh viện hoặc lớp đăng nhập của webapp chặn kết nối nền, tại dashboard chọn **Hồ sơ & HIS → Tải gói pilot ngoại tuyến**. Trong ứng dụng Windows mở **Đồng bộ webapp → Nhập gói pilot JSON**. Gói này chỉ chứa dữ liệu của tờ vào viện đã chọn, được nạp vào RAM và vẫn bắt buộc đối chiếu mã bệnh nhân. Xóa file JSON sau buổi thử nếu file có dữ liệu thật.

Ví dụ cấu hình hành động sau khi đã bắt đúng control trên máy HIS thật:

```xml
<AutomationProfile CloudFormCode="admission_sheet" AllowSaveAfterFill="true" StopBeforeSave="false">
  <SaveAction AutomationId="butLuu" Name="" ClassName="" MatchIndex="0" />
  <CloseAction AutomationId="butKetthuc" Name="" ClassName="" MatchIndex="0" />
</AutomationProfile>
```

Không chạy app quyền Administrator trừ khi HIS cũng đang chạy quyền Administrator. Windows không cho tiến trình quyền thấp điều khiển UI của tiến trình quyền cao.

## Hiệu chỉnh selector tại máy HIS

- Tab **Hiệu chỉnh UIA** → **Quét control** liệt kê `AutomationId`, `Name`, `ControlType`, `ClassName` nhưng không đọc `Value`.
- Hoặc chọn một field ở tab **Nhập liệu**, sang tab hiệu chỉnh và bấm **Bắt control sau 3 giây**; đưa chuột lên đúng control HIS.
- Bấm **Lưu profile**. Chỉ selector được lưu tại `%LOCALAPPDATA%\UMC2\HisAdmissionAssistant\Profiles`; dữ liệu bệnh nhân không được lưu.
- Có thể double-click một control trong bảng quét để gán nó cho field đang chọn.

## Cơ chế an toàn

- Preflight toàn bộ selector trước khi ghi; thiếu field bắt buộc thì hủy cả lượt.
- Field `PatientId` dùng chế độ `Verify`: chỉ đọc để đối chiếu, không ghi đè.
- Engine chặn selector trường dữ liệu có dấu hiệu là `Lưu`, `Save`, `Xác nhận`, `Đồng ý` hoặc `OK`. Nút Lưu/Đóng chỉ có đường gọi riêng từ tác vụ webapp không yêu cầu ký.
- Log chỉ ghi tên thao tác, PID và số lượng; không ghi nội dung lâm sàng hay tiêu đề cửa sổ.
- Dữ liệu nhập không được lưu vào profile và được xóa khỏi bộ nhớ giao diện khi đóng app.

## Giới hạn hiện tại

- Tên form/control lấy từ metadata assembly, chưa được xác nhận trên UIA tree của phiên HIS đang chạy. Với control DotNetBar, `ListLookup`, `MaskedBox` hoặc DevExpress, cần hiệu chỉnh bằng chức năng capture nếu `AutomationId` không được expose.
- Textbox hỗ trợ `ValuePattern` và fallback `WM_SETTEXT` có timeout; dropdown/list chọn item bằng `SelectionItemPattern`. Custom control không expose các cơ chế này sẽ cần bổ sung MSAA hoặc keyboard fallback riêng sau khi đo thực tế.
- Profile `Chọn phòng và giường` chỉ chọn dữ liệu; không gọi nút OK.
- Tự động hoàn toàn từng biểu mẫu chỉ bật được sau khi bắt selector Lưu/Đóng trên phiên HIS thật. Nếu thiếu profile hoặc có nhiều cửa sổ phù hợp, tác vụ dừng ở trạng thái cần người kiểm tra.

## Kiểm thử không dùng dữ liệu thật

Chạy `tools\MockHis\bin\Release\MockHis.exe`, chọn profile **Kiểm thử — Mock HIS**, nhập mã `BN-TEST-001`, thêm lý do/chẩn đoán rồi kiểm tra và điền. Bộ đếm **Số lần bấm Lưu** phải luôn là `0`.

Kiểm thử tự động: `tests\SmokeTest\bin\Release\SmokeTest.exe tools\MockHis\bin\Release\MockHis.exe`.
