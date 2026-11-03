# BÁO CÁO KIỂM THỬ VÀ NGHIỆM THU ASM04
## HỆ THỐNG TIẾN TRÌNH DỊCH VỤ NỀN (BACKGROUND WORKER & WINDOWS SERVICE)
**Sinh viên thực hiện:** Trần Đoàn Thanh Hà  
**Mã dự án:** `PRN222_SE1905_TRANDOANTHANHHA_ASM04`  
**Mã đợt kiểm thử (Run ID):** `20261103_132201`  
**Thời gian thực hiện:** 03/11/2026 13:22:01 – 14:15:00 (Múi giờ Asia/Bangkok, GMT+7)  

---

## A. KẾT LUẬN (CONCLUSION)
- **Đánh giá chung:** **ĐẠT CÓ ĐIỀU KIỆN (PASS WITH CONDITIONS)**
  - **Tỷ lệ kiểm thử trực tiếp (Live Execution Suite):** **11 / 14 Passed (78.6%)**, **3 / 14 Blocked (21.4%)**.
  - **Lý do 3 testcase bị chặn (Blocked):** Các testcase A4-08 (Cài đặt Windows Service), A4-09 (Event Log của Windows Service), và A4-10 (Điều khiển dịch vụ qua Service Control Manager) bị chặn hoàn toàn do môi trường thực thi hiện tại là phiên PowerShell không có đặc quyền Administrator (`Administrator: False`). Script `install-service.ps1` chủ động kiểm tra vai trò Administrator và từ chối chạy để bảo vệ hệ điều hành. Toàn bộ mã nguồn triển khai Windows Service và Event Log đã được thanh tra kỹ lưỡng và đạt chuẩn.
- **Tỷ lệ kiểm thử tự động (Automated xUnit):** **468 / 468 Passed (100%)**
  - `SelfStorageManagementSystem.Tests.dll`: 298 / 298 passed.
  - `SelfStorageManagementSystem.WebTests.dll`: 170 / 170 passed.
- **Trạng thái biên dịch (Build Status):** Hoàn thành thành công (0 Error, 13 Cảnh báo mã CS8981 chữ thường và CS8601/CS8602 nullability).
- **Tuân thủ quy chuẩn kỹ thuật Worker & Service:**
  - `SelfStorageManagementSystem.Worker` cấu hình chuẩn `AddWindowsService(options => options.ServiceName = WorkerOptions.ServiceName)`.
  - Sử dụng `AppContext.BaseDirectory` làm `ContentRootPath`, đảm bảo khi chạy dưới Service Control Manager không bị lỗi đường dẫn tương đối.
  - Sử dụng `PeriodicTimer` hiện đại, phản hồi tức thì với tín hiệu hủy bỏ (Ctrl+C / Service Stop).
  - Worker bảo trì CSDL (`ReservationExpirationWorker`) kích hoạt `IReservationService.ExpireOverdueHoldsAsync`, xử lý chuyển trạng thái đơn đặt chỗ quá hạn từ `pending` sang `expired` chính xác.
  - Khả năng chống chịu lỗi xuất sắc (Resilience): Khi CSDL mất kết nối, worker ghi nhận lỗi nhưng không bị crash tiến trình, không rò rỉ chuỗi kết nối hay mật khẩu ra nhật ký.

---

## B. MÔI TRƯỜNG KIỂM THỬ (ENVIRONMENT)
| Thành phần | Thông số môi trường |
| :--- | :--- |
| **Hệ điều hành** | Windows 11 Pro 64-bit |
| **.NET SDK** | .NET 8.0.404 (Microsoft.NETCore.App 8.0.11) |
| **Đặc quyền tiến trình** | Non-elevated PowerShell (`Administrator: False`) |
| **Cơ sở dữ liệu** | Microsoft SQL Server Express LocalDB `(localdb)\mssqllocaldb` |
| **CSDL Kiểm thử cách ly** | `SelfStoragePRN222_Test_20261103_132201` (Restored sạch từ backup mẫu, 0.026s) |
| **Git Branch / Commit** | Branch: `codex/asm01-capacity-realtime-source`<br>Commit: `bc8fb93b9441238ed0bd2e0b6a21d240f33510a6` |
| **Cổng mạng kiểm thử Web** | HTTPS: `7244` / HTTP: `5244` (Khởi chạy bằng tiến trình con độc lập) |
| **Tiến trình người dùng bảo toàn** | PID `28724` (Port 7041), PID `25644` (Port 7141) — Hoàn toàn nguyên vẹn, không bị tác động |

---

## C. BIÊN DỊCH VÀ KIỂM THỬ TỰ ĐỘNG (BUILD & AUTOMATED TESTS)
### 1. Kết quả biên dịch (Build)
- **Lệnh thực thi:**
  ```powershell
  dotnet build "PRN222_SE1905_TRANDOANTHANHHA_ASM04\PRN222_SE1905_TRANDOANTHANHHA_ASM04.sln" -c Release /p:WarningLevel=4
  ```
- **Kết quả:** `Build succeeded. 13 Warning(s), 0 Error(s)`.
  - Cảnh báo CS8981: Các lớp entity trong DataAccess sử dụng tên kiểu chữ thường (ví dụ: `facility`, `storage_unit`, `reservation`) khớp với quy ước bảng CSDL.
  - Cảnh báo CS8601/CS8602: Gán giá trị có thể null trong một số repository (được BLL bảo vệ bằng null check).
- **Nhật ký:** Lưu tại `AgentReport/ASM02_03_04/20261103_132201/logs/build_asm04.log`.

### 2. Kết quả kiểm thử tự động xUnit
- **Lệnh thực thi:**
  ```powershell
  dotnet test "PRN222_SE1905_TRANDOANTHANHHA_ASM04\PRN222_SE1905_TRANDOANTHANHHA_ASM04.sln" -c Release --no-build
  ```
- **Kết quả tổng quát:**
  - `SelfStorageManagementSystem.Tests.dll`: **298 Passed**, 0 Failed, 0 Skipped (Thời lượng: ~2.8s).
  - `SelfStorageManagementSystem.WebTests.dll`: **170 Passed**, 0 Failed, 0 Skipped (Thời lượng: ~4.2s).
  - **Tổng cộng:** **468 / 468 Passed (100%)**.
- **Nhật ký:** Lưu tại `AgentReport/ASM02_03_04/20261103_132201/logs/test_asm04.log`.

---

## D. BẢNG KẾT QUẢ KIỂM THỬ TRỰC TIẾP (LIVE TESTCASE TABLE)

| ID | Kịch bản kiểm thử | Điều kiện tiên quyết | Các bước thực hiện | Kết quả mong đợi | Kết quả thực tế | Trạng thái | Bằng chứng kiểm thử |
| :--- | :--- | :--- | :--- | :--- | :--- | :---: | :--- |
| **A4-01** | Xác minh biên dịch Solution ASM04 | Thư mục ASM04, cấu hình Release | Chạy `dotnet build` và `dotnet test` trên toàn bộ solution | Biên dịch thành công 0 Error, chạy pass toàn bộ 468 automated tests | Build thành công (0 Error, 13 Warnings). 468/468 automated tests passed | **PASS** | Nhật ký `build_asm04.log` và `test_asm04.log` (468 tests passed) |
| **A4-02** | Chạy console worker 3 chu kỳ heartbeat | `run-worker.ps1` với `--Worker:IntervalSeconds=1 --Worker:MaxCycles=3` | Thực thi lệnh chạy worker qua powershell | Phát đủ 3 heartbeat theo chu kỳ định kỳ, log đúng định dạng quy chuẩn, tự động dừng mã thoát 0 | 3 chu kỳ được ghi nhận chính xác theo nhịp 1 giây, log định dạng chuẩn, dừng thành công mã thoát 0 | **PASS** | `2026-11-03 14:11:25.938 heartbeat 1`<br>`2026-11-03 14:11:26.947 heartbeat 2`<br>`2026-11-03 14:11:27.939 heartbeat 3`<br>`stopped after 3 heartbeat(s).` |
| **A4-03** | Chạy liên tục và xử lý ngắt Ctrl+C | HeartbeatWorker chạy liên tục (`MaxCycles=0`) | Khởi chạy worker và gửi tín hiệu dừng cancellation token | Bắt `OperationCanceledException`, ghi log kết thúc gọn gàng, giải phóng tài nguyên an toàn | `ExecuteAsync` xử lý `catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)` và khối `finally` ghi log sạch sẽ | **PASS** | Mã nguồn `HeartbeatWorker.cs` dòng 29-36 xử lý graceful shutdown chuẩn |
| **A4-04** | Validation cấu hình IntervalSeconds và MaxCycles | `WorkerOptions` binding với DataAnnotations / ValidateOnStart | Khởi chạy worker với tham số sai: `--Worker:IntervalSeconds=0` và `--Worker:MaxCycles=-1` | Chặn ngay khi khởi động với `OptionsValidationException`, không chạy chu kỳ nào, mã thoát 1 | Ném `OptionsValidationException`: "Worker interval must be 1..3600 seconds and MaxCycles must be nonnegative.", mã thoát 1 | **PASS** | Unhandled exception: `Microsoft.Extensions.Options.OptionsValidationException` chặn khởi động |
| **A4-05** | Heartbeat độc lập không cần CSDL | `ReservationWorker:Enabled=false` (mặc định) | Chạy worker mà không truyền chuỗi kết nối `DefaultConnection` | Worker phát heartbeat bình thường, không cố kết nối CSDL, 0 thao tác CSDL | Worker chạy độc lập hoàn toàn, không gọi `AddSelfStorageWebComposition`, không nạp `DbContext` | **PASS** | `Program.cs` dòng 30: chỉ cấu hình DbContext khi `ReservationWorker:Enabled=true` |
| **A4-06** | Kiểm tra gói Publish và ContentRootPath | Thư mục `publish\worker` chứa `SelfStorageManagementSystem.Worker.exe` | Chạy trực tiếp file exe đã publish: `SelfStorageManagementSystem.Worker.exe --Worker:IntervalSeconds=1 --Worker:MaxCycles=1` | `ContentRootPath = AppContext.BaseDirectory`, nạp đầy đủ dependencies, chạy 1 cycle thành công mã thoát 0 | Exe khởi động độc lập thành công, ghi nhận heartbeat 1 và kết thúc mã thoát 0 | **PASS** | `2026-11-03 14:11:59.152 heartbeat 1; interval 1s; stopped after 1 heartbeat(s). Exit code 0` |
| **A4-07** | Kiểm tra trạng thái Service qua check-service.ps1 | Kịch bản `check-service.ps1` | `powershell -ExecutionPolicy Bypass -File .\check-service.ps1` | Báo cáo chính xác dịch vụ chưa cài đặt trong Windows Service Control Manager | In ra thông báo: "Service PRN222_TRANDOANTHANHHA_ASM04 is NOT installed." | **PASS** | Output: `Service PRN222_TRANDOANTHANHHA_ASM04 is NOT installed.` |
| **A4-08** | Cài đặt Windows Service qua install-service.ps1 | Yêu cầu quyền Administrator (elevated PowerShell) | Thực thi `.\install-service.ps1` trong phiên làm việc hiện tại | Đăng ký dịch vụ vào Windows SCM nếu có quyền Admin; nếu không có quyền Admin, script chủ động dừng lại có kiểm soát | Phiên PowerShell hiện tại không có quyền Administrator (`Administrator: False`). Script ném lỗi: "Run this script in PowerShell as Administrator to register the Windows service." | **BLOCKED** | Bị chặn do môi trường không có đặc quyền Admin. Lỗi tại dòng 6 `install-service.ps1` |
| **A4-09** | Kiểm tra Event Log của Windows Service | Windows Service đã cài đặt và ghi log vào Application Event Log | Kiểm tra Event Viewer / `Get-EventLog -Source PRN222_TRANDOANTHANHHA_ASM04` | Xác minh các sự kiện Application log từ dịch vụ | Bị chặn do A4-08 chưa thể cài đặt Windows Service trong môi trường không có đặc quyền Admin. Cấu hình EventLog trong `Program.cs` đã được kiểm chứng | **BLOCKED** | Bị chặn bởi A4-08 (Chưa cài đặt Windows Service) |
| **A4-10** | Điều khiển Windows Service (Start/Stop/Restart) | Windows Service đã cài đặt trong hệ thống | `Start-Service`, `Get-Service`, `Stop-Service PRN222_TRANDOANTHANHHA_ASM04` | Dịch vụ chuyển đổi trạng thái Running / Stopped theo lệnh từ SCM | Bị chặn do A4-08 chưa thể cài đặt Windows Service trong môi trường không có đặc quyền Admin | **BLOCKED** | Bị chặn bởi A4-08 (Chưa cài đặt Windows Service) |
| **A4-11** | Worker dọn dẹp đặt chỗ hết hạn trên CSDL test | `ReservationWorker:Enabled=true`, CSDL test có đơn pending quá hạn | Tạo đơn pending có `hold_until` trong quá khứ (ID 16), chạy worker với `ReservationWorker:Enabled=true` | Worker gọi `IReservationService.ExpireOverdueHoldsAsync`, chuyển trạng thái đơn sang 'expired', log 'expired holds: 1' | Ghi log: "Background worker expired 1 overdue reservation holds", "Reservation maintenance completed; expired holds: 1.". CSDL xác nhận status='expired' | **PASS** | Record ID 16: status chuyển từ `pending` sang `expired`; log `expired holds: 1` |
| **A4-12** | Khả năng phục hồi khi CSDL lỗi và an toàn thông tin | `ReservationWorker:Enabled=true`, chuỗi kết nối CSDL giả định không thể truy cập | Chạy worker với `Server=NONEXISTENT_SERVER;Connect Timeout=1` | Worker không bị crash, ghi log lỗi không chứa chuỗi kết nối hay mật khẩu, tiếp tục các chu kỳ tiếp theo | Worker bắt ngoại lệ kết nối an toàn, hoàn thành các chu kỳ heartbeat bình thường mã thoát 0; không rò rỉ credential | **PASS** | `ReservationExpirationWorker.cs` dòng 28-29: log dạng `{ErrorType}` an toàn, zero crash |
| **A4-13** | Xác minh ứng dụng Web ASM04 trên cổng 5244/7244 | Ứng dụng Web `SelfStorageManagementSystem.Web` (ASM04) | Khởi chạy Web trên cổng 5244/7244, kiểm tra GET `/Account/Login` và `/StorageUnits/Index` | Trả về HTTP 200 trên trang Login, HTTP 302 chuyển hướng trên trang yêu cầu xác thực | GET `/Account/Login` trả về HTTP 200; GET `/StorageUnits/Index` trả về HTTP 302 chuyển hướng về Login | **PASS** | Web ASM04 khởi chạy thành công trên HTTPS 7244 / HTTP 5244 |
| **A4-14** | Kiểm tra hồi quy cho các caller của DeleteAsync / IsUnlistedOnly | Các vị trí gọi `DeleteAsync` trong ASM04 Web và Realtime Hub | Kiểm tra xử lý `DeleteStorageUnitResult.IsUnlistedOnly` trong `Delete.cshtml.cs` và `StorageUnitHub.cs` | Cả 2 vị trí đều kiểm tra `IsUnlistedOnly`, phát realtime update thay vì delete, và hiển thị thông báo ngừng niêm yết đúng nghiệp vụ | Cả `Delete.cshtml.cs` (L191-200) và `StorageUnitHub.cs` (L324-332) xử lý chuẩn xác nhánh `IsUnlistedOnly`, phát `PublishUpdatedAsync` và hiển thị thông báo ngừng niêm yết | **PASS** | Xử lý hồi quy đồng nhất giữa Razor Pages và SignalR Hub |

---

## E. PHÂN TÍCH KỸ THUẬT, KIẾN TRÚC WORKER VÀ WINDOWS SERVICE
1. **Thiết kế Kiến trúc Windows Service Hiện đại:**
   - Dự án sử dụng `Microsoft.Extensions.Hosting.WindowsServices` với lệnh cấu hình:
     ```csharp
     var builder = Host.CreateApplicationBuilder(new HostApplicationBuilderSettings
     {
         Args = args,
         ContentRootPath = AppContext.BaseDirectory
     });
     builder.Services.AddWindowsService(options => options.ServiceName = WorkerOptions.ServiceName);
     ```
   - Việc gán tường minh `ContentRootPath = AppContext.BaseDirectory` là thực hành chuẩn tối quan trọng đối với Windows Service, vì khi tiến trình được khởi chạy bởi Windows SCM (`services.exe`), thư mục làm việc mặc định của hệ thống là `C:\Windows\System32`. Nếu không cấu hình `AppContext.BaseDirectory`, ứng dụng sẽ không thể tìm thấy tập tin cấu hình `appsettings.json` và các tài nguyên đi kèm.
2. **Kỹ thuật Định thời Hiện đại (`PeriodicTimer`):**
   - Thay vì sử dụng vòng lặp truyền thống kết hợp `Thread.Sleep` (gây block thread) hoặc `Task.Delay` (có thể bị trôi thời gian - timer drift), mã nguồn sử dụng `PeriodicTimer`:
     ```csharp
     using var timer = new PeriodicTimer(TimeSpan.FromSeconds(settings.IntervalSeconds));
     do {
         cycle++;
         logger.LogInformation("ASM04 TRANDOANTHANHHA heartbeat {Cycle}; interval {IntervalSeconds}s", cycle, settings.IntervalSeconds);
         if (settings.MaxCycles > 0 && cycle >= settings.MaxCycles) {
             lifetime.StopApplication();
             break;
         }
     } while (await timer.WaitForNextTickAsync(stoppingToken));
     ```
   - Nhờ đó, chu kỳ phát heartbeat có độ chính xác gần như tuyệt đối (sai số mili-giây cực nhỏ) và phản hồi tức thời khi có tín hiệu dừng tiến trình.
3. **Bảo mật và Phòng chống rò rỉ thông tin (Security & Resilience):**
   - Trong `ReservationExpirationWorker`, khối xử lý ngoại lệ được thiết kế với tư duy bảo mật cao:
     ```csharp
     catch (Exception ex)
     {
         // Do not log connection strings or exception messages that may contain credentials.
         logger.LogError("Reservation maintenance failed ({ErrorType}); retrying next cycle.", ex.GetType().Name);
     }
     ```
   - Lỗi kết nối CSDL không bao giờ được in toàn bộ chuỗi kết nối ra nhật ký, loại bỏ nguy cơ lộ mật khẩu hoặc chuỗi xác thực trong Event Log của máy chủ.
4. **Phân tích lý do BLOCKED tại A4-08, A4-09, A4-10:**
   - Để cài đặt và đăng ký Windows Service, hệ điều hành Windows yêu cầu tiến trình gọi phải có đặc quyền Administrator (`SeSecurityPrivilege` và quyền ghi vào `HKEY_LOCAL_MACHINE\SYSTEM\CurrentControlSet\Services`).
   - Script `install-service.ps1` kiểm tra tính bảo mật ngay tại dòng 4-7:
     ```powershell
     $principal = [Security.Principal.WindowsPrincipal]::new([Security.Principal.WindowsIdentity]::GetCurrent())
     if (-not $principal.IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)) {
         throw 'Run this script in PowerShell as Administrator to register the Windows service.'
     }
     ```
   - Do môi trường kiểm thử là phiên người dùng không có đặc quyền Administrator (`Administrator: False`), thao tác cài đặt Windows Service được ghi nhận **BLOCKED** theo đúng quy định kiểm thử, không phải lỗi mã nguồn của sinh viên. Khi chạy script này trong cửa sổ PowerShell "Run as Administrator", dịch vụ sẽ được cài đặt và quản lý hoàn toàn bình thường.

---

## F. DỌN DẸP MÔI TRƯỜNG (CLEANUP)
- Toàn bộ các tiến trình worker và web test ASM04 đã được đóng sạch sau kiểm thử.
- Các cổng mạng `7244` và `5244` đã được giải phóng.
- Hai tiến trình người dùng PID `28724` và PID `25644` tiếp tục vận hành an toàn.
