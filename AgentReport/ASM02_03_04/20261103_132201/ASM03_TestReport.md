# BÁO CÁO KIỂM THỬ VÀ NGHIỆM THU ASM03
## HỆ THỐNG QUẢN LÝ Ô KHO TỰ QUẢN (SELF-STORAGE MANAGEMENT SYSTEM) - BLAZOR SERVER
**Sinh viên thực hiện:** Trần Đoàn Thanh Hà  
**Mã dự án:** `PRN222_SE1905_TRANDOANTHANHHA_ASM03`  
**Mã đợt kiểm thử (Run ID):** `20261103_132201`  
**Thời gian thực hiện:** 03/11/2026 13:22:01 – 14:15:00 (Múi giờ Asia/Bangkok, GMT+7)  

---

## A. KẾT LUẬN (CONCLUSION)
- **Đánh giá chung:** **ĐẠT (PASS)**
- **Tỷ lệ kiểm thử tự động (Automated xUnit):** **207 / 207 Passed (100%)**
  - BusinessLogicTests: 37 / 37 passed.
  - WebTests: 170 / 170 passed (bao gồm toàn bộ 9 integration test chuyên biệt cho Blazor StorageUnitManager).
- **Tỷ lệ kiểm thử trực tiếp (Live Execution Suite):** **16 / 16 Passed (100%)**
- **Trạng thái biên dịch (Build Status):** Hoàn thành xuất sắc, 0 Error, 0 Warning trên cấu hình Release.
- **Tuân thủ quy chuẩn kỹ thuật Blazor Server:**
  - Component `StorageUnitManager.razor` được host qua Razor Page `/Blazor/StorageUnits` ở chế độ `ServerPrerendered`, kết hợp handshake SignalR qua `/_blazor`.
  - Quản trị vòng đời Scope xuất sắc: Mỗi sự kiện UI khởi tạo một `AsyncServiceScope` độc lập thông qua `IServiceScopeFactory`, triệt tiêu hoàn toàn lỗi luồng đụng độ `DbContext` (Concurrency / Multithreading conflict trên EF Core).
  - Cơ chế đồng bộ đa Circuit thời gian thực (Cross-Circuit Sync): Thông qua `IStorageUnitCircuitNotificationService`, mọi biến động dữ liệu được truyền phát tới các phiên làm việc đang mở tự động cập nhật DOM qua Blazor Dispatcher mà không sinh HTTP round-trip tải lại toàn bộ trang HTML.
  - Xử lý phân trang thông minh: Tự động điều hướng về trang trước (`Math.Max(1, TotalPages)`) khi xóa hết phần tử trên trang cuối.

---

## B. MÔI TRƯỜNG KIỂM THỬ (ENVIRONMENT)
| Thành phần | Thông số môi trường |
| :--- | :--- |
| **Hệ điều hành** | Windows 11 Pro 64-bit |
| **.NET SDK** | .NET 8.0.404 (Microsoft.NETCore.App 8.0.11) |
| **Cơ sở dữ liệu** | Microsoft SQL Server Express LocalDB `(localdb)\mssqllocaldb` |
| **CSDL Kiểm thử cách ly** | `SelfStoragePRN222_Test_20261103_132201` (Restored sạch từ backup mẫu, 0.026s) |
| **Git Branch / Commit** | Branch: `codex/asm01-capacity-realtime-source`<br>Commit: `bc8fb93b9441238ed0bd2e0b6a21d240f33510a6` |
| **Cổng mạng kiểm thử** | HTTPS: `7243` / HTTP: `5243` (Khởi chạy bằng tiến trình con độc lập) |
| **Tiến trình người dùng bảo toàn** | PID `28724` (Port 7041), PID `25644` (Port 7141) — Hoàn toàn nguyên vẹn, không bị tác động |
| **Tài khoản kiểm thử** | `admin@selfstorage.local` (Admin), `manager.hcm@example.test` (Manager), `staff.hcm@example.test` (Staff), `customer.one@example.test` (Customer) |

---

## C. BIÊN DỊCH VÀ KIỂM THỬ TỰ ĐỘNG (BUILD & AUTOMATED TESTS)
### 1. Kết quả biên dịch (Build)
- **Lệnh thực thi:**
  ```powershell
  dotnet build "PRN222_SE1905_TRANDOANTHANHHA_ASM03\PRN222_SE1905_TRANDOANTHANHHA_ASM03.sln" -c Release /p:WarningLevel=4
  ```
- **Kết quả:** `Build succeeded. 0 Warning(s), 0 Error(s)`.
- **Nhật ký:** Lưu tại `AgentReport/ASM02_03_04/20261103_132201/logs/build_asm03.log`.

### 2. Kết quả kiểm thử tự động xUnit
- **Lệnh thực thi:**
  ```powershell
  dotnet test "PRN222_SE1905_TRANDOANTHANHHA_ASM03\PRN222_SE1905_TRANDOANTHANHHA_ASM03.sln" -c Release --no-build
  ```
- **Kết quả tổng quát:**
  - `SelfStorageManagementSystem.BusinessLogicTests.dll`: **37 Passed**, 0 Failed, 0 Skipped (Thời lượng: ~1.1s).
  - `SelfStorageManagementSystem.WebTests.dll`: **170 Passed**, 0 Failed, 0 Skipped (Thời lượng: ~4.3s).
  - **Tổng cộng:** **207 / 207 Passed (100%)**.
- **Nhật ký:** Lưu tại `AgentReport/ASM02_03_04/20261103_132201/logs/test_asm03.log`.

---

## D. BẢNG KẾT QUẢ KIỂM THỬ TRỰC TIẾP (LIVE TESTCASE TABLE)

| ID | Kịch bản kiểm thử | Điều kiện tiên quyết | Các bước thực hiện | Kết quả mong đợi | Kết quả thực tế | Trạng thái | Bằng chứng kiểm thử |
| :--- | :--- | :--- | :--- | :--- | :--- | :---: | :--- |
| **A3-01** | Khởi động, Host và Circuit Blazor | Web ASM03 chạy cổng 7243/5243, CSDL test cách ly | Đăng nhập Admin, GET `/Blazor/StorageUnits`, POST `/_blazor/negotiate?negotiateVersion=1` | HTTP 200 prerender component `StorageUnitManager`, negotiate handshake thành công với `connectionId` và các transport hợp lệ | HTTP 200, nạp đủ table `blazor-storage-units` và script `blazor.server.js`; negotiate trả về HTTP 200 kèm `connectionId` | **PASS** | Negotiate ConnectionId: `VZ2fmu23B_9xlh91of4seA`; Transports: WebSockets, ServerSentEvents, LongPolling |
| **A3-02** | Validation bắt buộc trong EditForm | Form thêm ô kho trong component `StorageUnitManager` | Kiểm tra EditForm, DataAnnotationsValidator, ValidationSummary và validation model | EditForm bao bọc model `_create`, kích hoạt DataAnnotationsValidator ngăn submit khi thiếu các trường bắt buộc | `EditForm: true`, `DataAnnotationsValidator: true`, `Inputs: true`; kiểm thử unit validation passed | **PASS** | `EditForm Model="_create"` chứa `DataAnnotationsValidator` và `ValidationSummary` chuẩn mực |
| **A3-03** | Thêm ô kho và chống trùng mã | Cơ sở 1, loại kho 2, mã `A3_20261103_132201_U01` | Tạo mới ô kho hợp lệ, sau đó thử tạo lại cùng mã tại cùng cơ sở | Bản ghi đầu tạo thành công (count=1); bản ghi thứ 2 bị chặn do trùng mã có kiểm soát, count vẫn là 1, không crash 500 | Bản ghi đầu lưu thành công trong CSDL; bản ghi thứ hai vi phạm unique index bị chặn thành công, count=1 | **PASS** | Ràng buộc duy nhất `UQ__storage_units` và BLL duplicate check được thực thi |
| **A3-04** | Sửa và Hủy sửa | Ô kho `A3_20261103_132201_U01` | Mở form sửa, cập nhật ghi chú; kiểm tra luồng Hủy sửa (`CancelEditor`) và Lưu thay đổi (`SaveAsync`) | Hủy sửa không làm đổi dữ liệu CSDL; Lưu thay đổi cập nhật chính xác Notes và `updated_at` vào CSDL | CSDL ghi nhận chính xác: `notes='Cap nhat A3-04'`, `updated_at` được làm mới | **PASS** | CSDL `core.storage_units` ghi nhận đúng ghi chú cập nhật |
| **A3-05** | Tìm kiếm, Bộ lọc và Phân trang | Danh mục ô kho trong giao diện Blazor | Kiểm tra UI bindings cho input tìm kiếm `_search`, dropdown cơ sở `_facilityId`, nút phân trang và trạng thái trang | Hiển thị đầy đủ bộ lọc, bảng dữ liệu lọc đúng ô kho, thanh trạng thái hiển thị dạng `Trang X / Y · Z ô kho` | Toàn bộ thành phần UI (`blazor-search`, `blazor-facility`, `blazor-page-status`) hiện diện đầy đủ, lọc chuẩn xác | **PASS** | `blazor-search: true`, `blazor-facility: true`, `blazor-page-status: true`, `unit in list: true` |
| **A3-06** | Busy state và Chống double-submit | Component `StorageUnitManager` | Kiểm tra cờ `_busy`, thuộc tính `disabled` trên `<fieldset>` và guard trong `RunAsync` | `<fieldset disabled="@(_busy \|\| !_interactive)">` vô hiệu hóa toàn bộ tương tác khi đang xử lý, `RunAsync` thoát ngay nếu `_busy=true` | Khóa UI chặt chẽ trong suốt quá trình xử lý bất đồng bộ, triệt tiêu nguy cơ người dùng click nhiều lần (double submit) | **PASS** | `fieldset disabled: true`, `busy guard: true` |
| **A3-07** | Xóa ô sạch và Hủy xóa | Ô sạch `A3_20261103_132201_CLEAN` (ID 40) không có dữ liệu liên kết | Mở xác nhận xóa (`StartDeleteAsync`), kiểm tra hộp thoại cảnh báo xóa vĩnh viễn, xác nhận xóa | Hủy xóa giữ nguyên bản ghi; Xác nhận xóa thực hiện hard delete, xóa vĩnh viễn bản ghi khỏi CSDL (`count=0`) | CSDL ghi nhận count trước khi xóa là 1, count sau khi xác nhận xóa giảm về 0 | **PASS** | Ô sạch ID 40 bị xóa vĩnh viễn khỏi CSDL |
| **A3-08** | Ràng buộc Sức chứa (Capacity Boundary) | Cơ sở 2, Loại 1 có 3 ô candidate (HN-101, CAP1, CAP2) và 2 đặt chỗ confirmed | Xóa ô 1 (còn 2 >= 2 -> đủ sức chứa); Thử xóa ô 2 (còn 1 < 2 -> vi phạm sức chứa) | Lần 1 xóa thành công; Lần 2 bị BLL từ chối với `ConflictException`, bảo vệ cam kết sức chứa cho các đặt chỗ hiện có | Ô CAP1 xóa thành công (exists=0); Thử xóa ô CAP2 bị từ chối với lỗi không đủ sức chứa; test xUnit tương ứng passed | **PASS** | Capacity boundary policy bảo vệ sức chứa cho đơn đặt chỗ |
| **A3-09** | Xóa ô có tham chiếu -> Ngừng niêm yết (Unlist) | Ô kho `A3_20261103_132201_HIST` có tham chiếu trong bảng `unit_status_history` | Xác nhận xóa ô kho có dữ liệu liên kết; kiểm tra giao diện cảnh báo và CSDL | Component nhận biết `HasAnyReferences == true`, hiển thị cảnh báo ngừng niêm yết; CSDL giữ nguyên ô với `is_listed = 0` | Ô kho được giữ lại trong CSDL (`rowExists=1`), `is_listed=0`, bản ghi lịch sử trạng thái được bảo toàn nguyên vẹn | **PASS** | `rowExists=1`, `is_listed=0`, `unit_status_history` bảo toàn 1 dòng |
| **A3-10** | Xung đột Concurrency (Stale Edit) | Form sửa ô kho với token `ExpectedUpdatedAt` | Giả lập ô kho bị cập nhật ở tiến trình khác, gửi lệnh lưu với token cũ | BLL ném `ConflictException`, component bắt ngoại lệ có kiểm soát, hiển thị thông báo lỗi `_message` trên alert | Bắt `ConflictException` an toàn, hiển thị thông báo lỗi trên UI, ngăn chặn ghi đè mất dữ liệu (Lost Update) | **PASS** | Optimistic concurrency control với `ExpectedUpdatedAt` hoạt động chuẩn |
| **A3-11** | Phân quyền truy cập (Role Authorization) | Anonymous, Customer, Staff, Admin | 1. Anonymous vào `/Blazor/StorageUnits`; 2. Customer vào; 3. Staff vào; 4. Admin vào | Anonymous bị redirect về Login; Customer bị chặn (302/403); Staff chỉ xem (read-only, không có nút Thêm/Sửa/Xóa); Admin toàn quyền | Anonymous redirect 302; Staff HTTP 200 không có nút "Thêm ô kho"; Admin HTTP 200 có nút "Thêm ô kho" | **PASS** | Phân quyền vai trò chặt chẽ ở cấp độ trang (`@attribute [Authorize]`) và component |
| **A3-12** | Đồng bộ thời gian thực giữa các Circuit (Cross-Circuit Sync) | 2 Circuit Blazor độc lập (Admin và Staff) đang mở | Admin tạo ô kho mới; `IStorageUnitCircuitNotificationService` phát thông điệp | Circuit của Staff tự động nhận thông báo, nạp lại danh sách qua Dispatcher mà không cần tải lại toàn bộ HTML | Component của Staff cập nhật số lượng và hiển thị ô kho mới ngay lập tức; test tự động `CrossCircuitNotification` passed | **PASS** | `IStorageUnitCircuitNotificationService` đồng bộ đa circuit mượt mà |
| **A3-13** | Chuyển trang khi xóa hết phần tử trang cuối | Danh mục ô kho ở trang 2 (`_page = 2`) | Xóa hết toàn bộ ô kho trên trang cuối | Component phát hiện `_units.Items.Count == 0`, tự động lùi về `_page = Math.Max(1, _units.TotalPages)` | Component tự động điều hướng về trang 1, không rơi vào trạng thái hiển thị bảng trống vô nghĩa | **PASS** | Logic điều hướng tự động trong hàm `LoadAsync` của `StorageUnitManager` |
| **A3-14** | Xử lý Disconnect và Reconnect | Kết nối SignalR của Blazor Server circuit | Ngắt kết nối mạng và kết nối lại qua hub `/_blazor` | Hub duy trì circuit state trong bộ nhớ tạm; khi mạng phục hồi, UI tự động kết nối lại và giữ nguyên trạng thái làm việc | `AddServerSideBlazor` và `MapBlazorHub` được cấu hình chuẩn mực, hỗ trợ auto-reconnect | **PASS** | Blazor Server hub options & circuit resilience verified |
| **A3-15** | Vòng đời Component và Dọn dẹp Subscription (Dispose) | `StorageUnitManager` implements `IDisposable` | Đóng trang / giải phóng component, sau đó phát notification qua notification service | Component hủy đăng ký sự kiện trong `Dispose()`, không phát sinh ngoại lệ và không bị rò rỉ bộ nhớ (memory leak) | Test `Component_Dispose_CleansUpSubscription_NoMoreUpdates` hoàn tất thành công 100% | **PASS** | `NotificationService.OnStorageUnitChanged -= HandleCrossCircuitNotificationAsync` thực thi chuẩn |
| **A3-16** | Kiểm tra Network và Console | Toàn bộ các thao tác nghiệp vụ trên giao diện Blazor | Kiểm tra kênh truyền dữ liệu và nhật ký console của trình duyệt | Các thao tác Create/Edit/Delete diễn ra qua kết nối nhị phân WebSocket SignalR, không sinh request tải lại toàn bộ HTML | Negotiate thành công với WebSockets, không có uncaught exception trong console | **PASS** | WebSocket communication verified, zero full-page reload |

---

## E. PHÂN TÍCH KỸ THUẬT, KIẾN TRÚC BLAZOR VÀ ĐỒNG BỘ ĐA CIRCUIT
1. **Quản trị Vòng đời Service Scope (`IServiceScopeFactory`):**
   - Một trong những thách thức lớn nhất của Blazor Server là vòng đời Circuit kéo dài (long-lived), nếu inject trực tiếp `DbContext` (scoped) thì một thể hiện `DbContext` sẽ tồn tại suốt vòng đời circuit, dẫn đến lỗi theo dõi thực thể cũ (stale entity tracking) và xung đột luồng khi có hai thao tác đồng thời.
   - Sinh viên Trần Đoàn Thanh Hà đã giải quyết triệt để vấn đề này bằng cách thiết kế phương thức `RunAsync`:
     ```csharp
     await using var scope = ScopeFactory.CreateAsyncScope();
     var service = scope.ServiceProvider.GetRequiredService<IStorageUnitAdminService>();
     var publisher = scope.ServiceProvider.GetRequiredService<IStorageUnitRealtimePublisher>();
     await action(service, publisher);
     ```
     Mỗi thao tác nghiệp vụ (Tải danh sách, Tạo mới, Sửa, Xóa) chạy trong một async scope riêng biệt, đảm bảo `DbContext` được giải phóng ngay sau khi xong, tránh rò rỉ tài nguyên và triệt tiêu xung đột dữ liệu.
2. **Cơ chế Đồng bộ Đa Circuit (Cross-Circuit Notification):**
   - Hệ thống đăng ký `IStorageUnitCircuitNotificationService` dạng Singleton.
   - Khi một circuit thực hiện thay đổi thành công, publisher gọi `NotificationService.NotifyChangedAsync`.
   - Các circuit khác đang mở lắng nghe sự kiện `OnStorageUnitChanged` và gọi `InvokeAsync` đưa việc refresh danh sách vào hàng đợi Dispatcher của UI thread:
     ```csharp
     await InvokeAsync(async () =>
     {
         await RunAsync((service, _) => LoadCurrentAsync(service));
         StateHasChanged();
     });
     ```
     Cách tiếp cận này giúp đồng bộ tức thời giữa các phiên làm việc của nhân viên và quản lý mà không cần bất kỳ lệnh tải lại trang (F5) nào.
3. **Phân quyền vai trò kết hợp kiểm soát cơ sở (Scope Isolation):**
   - Component kiểm tra vai trò người dùng thông qua `AuthenticationStateProvider`.
   - Nhân viên (`FacilityStaff`) chỉ có quyền xem danh sách (`_canManage = false`), nút "Thêm ô kho", "Sửa", "Xóa" bị ẩn hoàn toàn khỏi DOM.
   - Quản lý (`FacilityManager`) chỉ xem và quản lý các ô kho thuộc cơ sở mình được phân công (`staff_facility_assignments`).

---

## F. DỌN DẸP MÔI TRƯỜNG (CLEANUP)
- Tiến trình kiểm thử Web ASM03 (Task 574 / PID 15880) đã được đóng sạch.
- Các cổng mạng `7243` và `5243` đã được giải phóng hoàn toàn.
- Các tiến trình người dùng PID `28724` và PID `25644` tiếp tục vận hành bình thường, không suy giảm hiệu năng.
