# ASM04 Vu632 — phân tích và sửa lỗi ghi hoạt động

Ngày: 06/11/2026. Báo cáo được tạo trước khi sửa source.

## Phạm vi và bằng chứng ban đầu

Service `Vu632` trỏ đến `src/SelfStorageManagementSystem.Worker/bin/Debug/net8.0-windows/SelfStorageManagementSystem.Worker.exe` trong thư mục ASM04 này. Workspace `Final_Project` là repository khác và không phải executable mà service sử dụng. Tại lần kiểm tra này service Stopped, chạy bằng LocalSystem, startup Manual. Tiến trình Codex không có quyền Administrator.

Application Event Log source Vu632 ghi lỗi: `Could not write ... Vu632.txt ... because it is being used by another process`. Lần gần nhất 04/11/2026 03:07:04. File Vu632.txt có 3 dòng, dòng cuối 04/11/2026 03:07:09. Đây là bằng chứng lịch sử cho sharing violation; chưa xác định chắc chắn tiến trình giữ khóa ở thời điểm đó. File Office `~$Vu632.txt` là dấu hiệu, không đủ để kết luận Word chính là thủ phạm.

## Đoạn code và nguyên nhân

1. `HeartbeatWorker.cs:25` dùng `File.AppendAllTextAsync`; exception bị catch rồi bỏ qua ở dòng 27–30. Khi file bị khóa, heartbeat hiện tại mất khỏi file; service vẫn chạy vì exception đã bị bắt. Không có đích ghi dự phòng.
2. `WorkerOptions.cs:5` đã đổi tên thành Vu632 nhưng install-service.ps1 và check-service.ps1 vẫn dùng tên cũ. Đổi tên chưa đồng bộ làm script kiểm tra sai service.
3. `Program.cs:17–21` chỉ đặt EventLogSettings.Filter. EventLog provider còn có filter trong hệ thống ILogger, mặc định Warning; Information bị loại trước khi tới filter của EventLogSettings. Cần đặt filter cho provider để heartbeat Information được chuyển đến Event Log. Đây là lỗi cấu hình có trước lần đổi tên, được xử lý trong phạm vi sửa luồng ghi log hiện tại.
4. ReservationWorker.Enabled=false là cấu hình chủ ý: mặc định chỉ ghi heartbeat, không dọn database, không phải lỗi ghi file.

## Hướng sửa

- Giữ Vu632.txt cạnh executable để tương thích service đang cài. Khi file chính bị khóa/không ghi được, ghi heartbeat sang Vu632.fallback.log và báo rõ đường dẫn qua ILogger; vòng sau thử lại file chính. File dự phòng giữ riêng, không tự nhập lại hay xóa, tránh nhân đôi và mất bằng chứng.
- Mở file với FileShare.ReadWrite để các trình xem log có thể đọc đồng thời. Không thể vượt qua khóa độc quyền của một ứng dụng khác; cần đóng ứng dụng giữ khóa nếu muốn ghi trực tiếp file chính.
- Đồng bộ script cài/kiểm tra theo WorkerOptions.ServiceName; thêm lựa chọn cập nhật đường dẫn service đã có một cách tường minh.
- Cấu hình Information cho EventLogLoggerProvider, sửa nhãn heartbeat theo ServiceName.
- Thêm regression tests cho ghi bình thường, file bị khóa, khôi phục sau mở khóa, hủy tác vụ, fallback thất bại và filter Event Log. Chạy build, unit tests và console smoke.

## Kết quả thực hiện

Đã hoàn tất sửa source và chạy test ngày 06/11/2026, khoảng 15:17–15:18 (Asia/Bangkok).

### Thay đổi đã thực hiện

- Thêm `HeartbeatFileWriter.cs`: ghi UTF-8, fallback khi IOException/UnauthorizedAccessException; cancellation không bị chuyển thành lỗi ghi file. Nếu cả hai đích đều lỗi, exception được HeartbeatWorker ghi ở mức Error qua ILogger, vòng lặp vẫn tiếp tục.
- `HeartbeatWorker.cs` dùng writer qua DI, nhãn log lấy ServiceName. `WorkerLogging.cs` cấu hình filter Information cho EventLogLoggerProvider; Program đăng ký writer và gọi cấu hình này.
- `service-name.ps1` đọc hằng ServiceName trong source làm nguồn tên duy nhất cho install/check. Install hỗ trợ `-UpdateExisting` khi service đã dừng, publish Release rồi cập nhật BinaryPath qua CIM, kiểm tra mã trả về. Nhánh cập nhật SCM chưa chạy vì phiên này không có quyền Administrator.
- Thêm project WorkerTests vào cả solution ASM04 và SelfStorageManagementSystem.sln, cập nhật README.

### Kết quả kiểm chứng

| Kiểm chứng | Kết quả |
| --- | --- |
| `dotnet test tests/SelfStorageManagementSystem.WorkerTests` | 7 passed, 0 failed, 0 skipped |
| `dotnet test tests/SelfStorageManagementSystem.Tests` | 231 passed, 0 failed, 19 skipped / 250 tổng |
| File bị giữ handle không cho ghi | Test xác nhận ghi sang fallback; sau mở khóa vòng sau ghi lại primary; dữ liệu fallback vẫn còn |
| Reader mở với FileShare.ReadWrite | Ghi file chính bình thường |
| Cancellation và cả hai file cùng bị khóa | Cancellation không tạo file; cả hai đích lỗi trả IOException để worker ghi Error |
| Host với MaxCycles=3 | Tự dừng, đủ 3 dòng được lưu |
| Script PowerShell | Parse thành công; `check-service.ps1` tìm đúng Vu632 và in đúng BinaryPath hiện tại |
| Event Log thật | Đã đọc lại source Vu632, mức Information: heartbeat lúc 15:17:25, 15:17:30, 15:17:35; thêm bản ghi xác minh lúc 15:18:11 |
| EXE thực tế ở đường dẫn service | Chạy console, 3 heartbeat cách nhau 5 giây, exit code 0; file chính vẫn bị ứng dụng ngoài giữ khóa nên cả 3 dòng được lưu vào Vu632.fallback.log |

Trong lần smoke đầu, việc cố mở thêm handle khóa thất bại vì file chính đã bị ứng dụng khác khóa sẵn. Vì vậy không gọi đây là mô phỏng khóa thành công: đó là kiểm chứng trực tiếp bản sửa xử lý tình trạng khóa đang tồn tại. Test tự động trong thư mục tạm mới là kiểm thử chủ động khóa/mở khóa, không phụ thuộc ứng dụng ngoài.

Các test SQL bỏ qua vì chưa cấu hình SELFSTORAGE_SQL_TEST_CONNECTION_STRING; không coi là đã qua kiểm thử SQL thật. Không cần sửa database cho lỗi ghi log này. Chưa chạy bộ Web/E2E vì thay đổi nằm trong worker và script cài service.

### Trạng thái bàn giao và cách chạy

Source sửa nằm trong thư mục ASM04 thuộc repository Final_Project_Web, không phải repository Final_Project đang mở trong chat. Bản Debug ở chính BinaryPath của service đã được build lại cùng DLL phụ thuộc; giữ lại các binary này để lần start tiếp theo dùng bản sửa. Các artifact build có tracked trong git ở project khác được khôi phục, giữ thay đổi source và thư mục binary của worker. Thay đổi .suo có sẵn ở ASM01 được giữ nguyên.

Service Vu632 vẫn Stopped. Phiên thực thi không có quyền Administrator nên chưa kiểm chứng start/stop qua SCM hay chạy dưới LocalSystem. Để chạy bản Debug đã sửa: mở PowerShell Administrator và chạy `Start-Service Vu632`. Để chuyển sang bản publish Release: tại thư mục ASM04 này chạy `./install-service.ps1 -UpdateExisting -Start` khi service đã dừng.

Xem Application Event Log → source Vu632, hoặc đọc `src/SelfStorageManagementSystem.Worker/bin/Debug/net8.0-windows/Vu632.fallback.log` nếu file chính còn khóa. Sau khi đóng ứng dụng giữ khóa Vu632.txt, vòng kế tiếp sẽ thử ghi lại primary. Không đóng ứng dụng người dùng hoặc xóa file log trong lần sửa này. Không có dữ liệu log cũ bị xóa.
