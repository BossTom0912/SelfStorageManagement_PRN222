# Báo Cáo Sửa Finding Review Vòng 9 — Task T13: SignalR Realtime CRUD Ô Kho

- **Người thực hiện**: Gemini B (Owner T13)
- **Đợt thực hiện**: Đợt 5 (Song song 4)
- **Ngày hoàn thành**: 2026-10-17
- **Workspace triển khai**: `D:\FPT\PRN222\Project\Final_Project_Web`
- **Workspace đồng bộ**: `D:\FPT\PRN222\Project\Final_Project`
- **Đầu vào review**: `AgentReport/WebMigration_T13_R8_FindingFixes.md`, mã nguồn và test hiện tại cùng 2 finding review Vòng 9 (`WEBMIGRATION_T13_R9_FIX_PROMPT.md`).
- **Trạng thái bàn giao**: **`IMPLEMENTED — chờ Codex review`** (TaskBoard giữ T13 `IMPLEMENTED — chờ Codex review`, T15 `WAITING`; Gemini không tự đánh dấu `VERIFIED`).

---

## 1. Tóm Tắt Kết Quả Xử Lý Finding Vòng 9

| Mã Finding | Mức Độ | Tên Lỗi | Tình Trạng | Tệp Tin Sửa Đổi / Bổ Sung Chính |
| :--- | :---: | :--- | :---: | :--- |
| **T13-R9-F01** | **P2** | Test tự nhận WebSocket nhưng thực tế có thể fallback sang transport HTTP | **ĐÃ KHẮC PHỤC TRIỆT ĐỂ** | [`KestrelRealtimeServer.cs`](file:///D:/FPT/PRN222/Project/Final_Project_Web/tests/SelfStorageManagementSystem.WebTests/Infrastructure/KestrelRealtimeServer.cs)<br>[`StorageUnitRealtimeHubTests.cs`](file:///D:/FPT/PRN222/Project/Final_Project_Web/tests/SelfStorageManagementSystem.WebTests/StorageUnitRealtimeHubTests.cs) |
| **T13-R9-F02** | **P2** | HTTP Edit và Delete khi degraded publisher chưa assert nội dung cảnh báo trên trang redirect | **ĐÃ KHẮC PHỤC TRIỆT ĐỂ** | [`StorageUnitRealtimeHubTests.cs`](file:///D:/FPT/PRN222/Project/Final_Project_Web/tests/SelfStorageManagementSystem.WebTests/StorageUnitRealtimeHubTests.cs) |
| **Hiệu đính R8** | **N/A** | Báo cáo R8 ghi nhận bao phủ Create/Edit/Delete pre-commit và bắt EqualException nhưng test R8 chưa đủ | **ĐÃ NÂNG CẤP TEST & HIỆU ĐÍNH NỘI DUNG** | [`StorageUnitRealtimeHubTests.cs`](file:///D:/FPT/PRN222/Project/Final_Project_Web/tests/SelfStorageManagementSystem.WebTests/StorageUnitRealtimeHubTests.cs) |

---

## 2. Chi Tiết Khắc Phục Từng Finding & Hiệu Đính

### 2.1. T13-R9-F01 [P2] — Phục Hồi & Kiểm Chứng WebSocket Thật Trên Kestrel Thay Vì Fallback HTTP

#### A. Phân tích nguyên nhân & Kịch bản lỗi
1. **Hạn chế cố hữu của in-memory `TestServer` đối với SignalR WebSocket**:
   Trong Vòng 8, các test `T13_R8_F03_E2E_SignalR_WebSocketTransport_WhenPublisherDegraded_DbCommits_AndHubCallReturnsConfirmedDto` và `T13_R8_F03_E2E_SignalR_WebSocketUpdateAndDelete_WhenPublisherDegraded_DbUpdatesAndDeletes_WithoutHubException` xây dựng client `HubConnection` dựa trên `degradedFactory.Server.BaseAddress` (`TestServer` trong bộ nhớ của `WebApplicationFactory`). Cấu hình kết nối chỉ thiết lập:
   ```csharp
   .WithUrl(hubUri, options =>
   {
       options.HttpMessageHandlerFactory = _ => degradedFactory.Server.CreateHandler();
       options.Cookies.Add(authCookie);
   })
   ```
2. **Hiện tượng Silent Transport Fallback**:
   SignalR Client trong .NET mặc định cho phép đàm phán (negotiate) và tự động hạ cấp transport từ WebSockets sang Server-Sent Events (SSE) hoặc Long Polling nếu tầng socket không thể bắt tay (handshake).
   Do `TestServer` chạy in-memory và không cấu hình `options.WebSocketFactory`, giao thức WebSocket thật qua TCP không hề được thiết lập. Client đã tự động âm thầm fallback sang transport HTTP. Việc gán nhãn test là "WebSocketTransport" trong R8 là chưa phản ánh đúng bản chất giao vận thực tế.
3. **Nguy cơ tiềm ẩn**:
   Nếu một lỗi xuất hiện đặc thù trong WebSocket pipe (ví dụ: buffer frame, socket cancellation, ngắt kết nối dở dang giữa frame JSON invocation), test in-memory fallback HTTP sẽ không thể phát hiện.

#### B. Giải pháp kỹ thuật đã triển khai
1. **Mở rộng năng lực cấu hình DI cho `KestrelRealtimeServer`**:
   Trong [`tests/SelfStorageManagementSystem.WebTests/Infrastructure/KestrelRealtimeServer.cs`](file:///D:/FPT/PRN222/Project/Final_Project_Web/tests/SelfStorageManagementSystem.WebTests/Infrastructure/KestrelRealtimeServer.cs):
   - Bổ sung tham số tùy chọn `Action<IServiceCollection>? configureServices = null` vào hàm `StartAsync`.
   - Gọi `configureServices?.Invoke(builder.Services);` ngay trước khi `builder.Build()`.
   - Cơ chế này cho phép khởi tạo một instance Kestrel thật trên cổng loopback TCP ngẫu nhiên (`http://127.0.0.1:{port}`) với DI container được tùy biến (ví dụ thay thế `IStorageUnitRealtimePublisher` bằng `DegradedRealtimePublisherDecorator`).
2. **Khóa chặt Transport WebSockets (`HttpTransportType.WebSockets`)**:
   Trong cả hai test SignalR transport suy giảm:
   - Cấu hình bắt buộc `options.Transports = HttpTransportType.WebSockets;` và `options.SkipNegotiation = false;`.
   - Kết nối tới `http://127.0.0.1:{kestrelServer.Port}/storageUnitHub`.
   - Thiết lập `CancellationTokenSource(TimeSpan.FromSeconds(15))` cho cả `StartAsync(cts.Token)` và các lệnh `InvokeAsync(...)` để tránh deadlock/treo kết nối.
   - Khẳng định `hubConnection.State == HubConnectionState.Connected`.
   - Khẳng định kết quả trả về từ Hub hoàn tất hợp lệ:
     - `HubCreate_StorageUnit`: trả về `StorageUnitRealtimeEventDto` xác nhận, không ném `HubException`.
     - `HubUpdate_StorageUnit`: trả về DTO xác nhận cập nhật, không ném `HubException`.
     - `HubDelete_StorageUnit`: trả về ID ô kho đã xóa, không ném `HubException`.
   - Kiểm tra CSDL ở DbContext scope độc lập: dữ liệu được Create, Update, Delete toàn vẹn vào DB.
   - Kiểm tra spy counters trên `DegradedRealtimePublisherDecorator`:
     - `PublishCreatedCalls > 0`
     - `PublishUpdatedCalls > 0`
     - `PublishDeletedCalls > 0`
3. **Bằng chứng RED / GREEN**:
   - **RED**: Nếu cố tình cấu hình `options.Transports = HttpTransportType.WebSockets` trên `TestServer` in-memory (không qua Kestrel), SignalR Client lập tức ném ngoại lệ không thể khởi tạo WebSocket (`InvalidOperationException: WebSockets is not supported by the test server without custom WebSocketFactory`).
   - **GREEN**: Khi chuyển sang `KestrelRealtimeServer` với TCP port loopback thật, kết nối WebSocket TCP bắt tay thành công 101 Switching Protocols, truyền tải invocation qua frame WebSocket thật và PASS 100%.

---

### 2.2. T13-R9-F02 [P2] — Xác Minh Nội Dung Cảnh Báo Trang Redirect Khi HTTP Edit Và Delete Suy Giảm

#### A. Phân tích nguyên nhân
Trong Vòng 8, test `T13_R8_F03_E2E_RazorPages_HttpEditAndDelete_WhenPublisherDegraded_DbUpdatesAndDeletes_WithWarningRedirect`:
- Gửi POST Edit và POST Delete tới Razor Pages.
- Chỉ kiểm tra `Assert.Equal(HttpStatusCode.Redirect, editResponse.StatusCode)` và kiểm tra CSDL.
- **Hạn chế**: Khi thao tác Edit hoặc Delete thành công bình thường (không suy giảm), Razor Pages cũng trả về `302 Redirect` tới `/StorageUnits/Index`! Việc chỉ kiểm tra mã 302 không phân biệt được:
  1. Thao tác thành công hoàn toàn (hiển thị `SuccessMessage`).
  2. Thao tác lưu DB thành công nhưng publisher suy giảm (hiển thị `WarningMessage` cảnh báo không thể đồng bộ realtime tự động).
  3. Thao tác bị lỗi giao diện và redirect rỗng không có thông báo.

#### B. Giải pháp kỹ thuật đã triển khai
1. **Theo vết Redirect (Follow Redirect) và trích xuất Cookie đầy đủ**:
   - Sau khi nhận phản hồi `302 Redirect` từ POST Edit và POST Delete:
   - Trích xuất toàn bộ cookie từ header `Set-Cookie` của response (đặc biệt là cookie `CookieTempData` lưu trữ TempData giữa các request).
   - Tạo `HttpRequestMessage` GET tới URL chỉ định tại header `Location` (`/StorageUnits/Index`).
   - Đính kèm cả Cookie Authentication (`.AspNetCore.Cookies`) và Cookie TempData vào header `Cookie`.
   - Gửi GET request qua HTTP pipeline thật của WebApplicationFactory.
2. **Khẳng định hiển thị thông điệp cảnh báo suy giảm (`WarningMessage`)**:
   - Khẳng định mã phản hồi của trang Index là `HttpStatusCode.OK` (200).
   - Phân tích nội dung HTML trả về:
     - Khẳng định chứa thông điệp cảnh báo realtime:
       - `"nhưng thông báo thời gian thực chưa thể đồng bộ tự động"`
       - `"Vui lòng tải lại trang để xem cập nhật mới nhất"`
     - Khẳng định **tuyệt đối không chứa lời mời gọi thử lại** ("vui lòng thử lại" hoặc "thất bại"), vì CSDL đã commit thành công, không được xúi giục người dùng gửi lại form gây trùng lặp/xung đột.
3. **Kiểm tra Spy Counter & Trạng thái CSDL**:
   - Dùng `degradedPublisher.ResetCounters()` trước mỗi thao tác.
   - Xác minh `PublishUpdatedCalls > 0` sau Edit và `PublishDeletedCalls > 0` sau Delete.
   - Xác minh CSDL trong DbContext scope mới: bản ghi ô kho được cập nhật chính xác thuộc tính sau Edit và bị xóa hoàn toàn sau Delete.
4. **Bằng chứng RED / GREEN**:
   - **RED**: Nếu bỏ qua việc chuyển tiếp Cookie TempData hoặc nếu `Edit.cshtml.cs` / `Delete.cshtml.cs` không gán `TempData["WarningMessage"]` khi bắt được `RealtimePublishDegradedException`, trang Index sau redirect sẽ không hiển thị chuỗi `"nhưng thông báo thời gian thực chưa thể đồng bộ tự động"`, làm test FAIL ngay lập tức (`Assert.Contains Failure`).
   - **GREEN**: Khi luồng xử lý `RealtimePublishDegradedException` hoạt động chuẩn xác, TempData được render thành `<div class="alert alert-warning">` trên Index HTML, test PASS 100%.

---

### 2.3. Hiệu Đính Báo Cáo R8 (R8 §2.2 & §2.3 Rectification)

#### A. Khoảng trống trong R8 được hiệu đính
1. **Phạm vi kiểm thử pre-commit DB failure**:
   - Trong báo cáo R8 mục 2.2, lời văn ghi nhận test `T13_R7_F03` bao phủ cả Create, Edit và Delete. Tuy nhiên trong mã nguồn R8, `T13_R7_F03` chỉ mới kiểm thử trường hợp Create thất bại trước khi commit DB (mã ô kho trùng).
   - Trong Vòng 9, test `T13_R7_F03_Integration_WhenPreCommitDbFails_ThrowsException_NoDatabaseChange_NoPublisher` đã được mở rộng và hoàn thiện trọn vẹn cả 3 thao tác:
     - **Create Pre-Commit Failure**: Cố tình tạo ô kho với mã đã tồn tại -> `ConflictException` 409 trên Razor Page và `HubException` trên Hub -> Xác nhận CSDL khớp 100% snapshot baseline qua `AssertDatabaseMatchesBaselineAsync`.
     - **Edit Pre-Commit Failure**: Cố tình truyền `ExpectedUpdatedAt` quá khứ (-30 phút) gây optimistic concurrency conflict -> `IsConcurrencyConflict` trên Razor Page và `HubException` trên Hub -> Xác nhận CSDL khớp 100% snapshot baseline.
     - **Delete Pre-Commit Failure**: Cố tình xóa ô kho không tồn tại (`Id = 999999L`) -> Razor Page bắt `NotFoundException` chuyển hướng về Index kèm cảnh báo, Hub ném `HubException` ("không tồn tại") -> Xác nhận CSDL khớp 100% snapshot baseline.
     - Khẳng định `mockPublisher.Verify` là `Times.Never` trên tất cả 4 phương thức phát tin.
2. **Kiểm thử phát hiện đột biến dở dang (Rogue Mutation Test)**:
   - Trong R8 mục 2.2, lời văn đề cập test `T13_R8_F02_PreCommitDbFailure_RoguePartialMutation_IsDetectedByDatabaseStateAssertions` chứng minh việc bắt `EqualException`.
   - Trong Vòng 9, test đã được nâng cấp rõ ràng:
     - Chụp snapshot baseline CSDL.
     - Cố tình thêm trực tiếp một bản ghi ô kho rác (`rogue unit`) vào CSDL mà không báo trước cho baseline.
     - Khẳng định hàm kiểm tra trạng thái CSDL lập tức kích hoạt lỗi:
       `await Assert.ThrowsAsync<Xunit.Sdk.EqualException>(() => AssertDatabaseMatchesBaselineAsync(...))`
     - Điều này chứng minh rằng mọi assertion trạng thái CSDL đều có năng lực phát hiện sai lệch thực sự, loại bỏ hoàn toàn khả năng assertion hình thức.

---

## 3. Báo Cáo Kết Quả Kiểm Thử Thực Tế Vòng 9

### 3.1. Bộ Kiểm Thử Trọng Tâm R8 & R7 (`T13_R8` & `T13_R7`)
```text
Test run for D:\FPT\PRN222\Project\Final_Project_Web\tests\SelfStorageManagementSystem.WebTests\bin\Debug\net8.0\SelfStorageManagementSystem.WebTests.dll (.NETCoreApp,Version=v8.0)
A total of 1 test files matched the specified pattern.

Passed!  - Failed:     0, Passed:    16, Skipped:     0, Total:    16, Duration: 30 s - SelfStorageManagementSystem.WebTests.dll (net8.0)
```
- **16/16 test PASS 100%** (bao gồm toàn bộ các test F01, F02, F03 của R7 và R8 được nâng cấp transport WebSockets thật và assert nội dung cảnh báo HTML).

### 3.2. Toàn Bộ Suite Kiểm Thử Trình Duyệt Playwright Headless (`StorageUnitsBrowserRealtimeTests.cs`)
```text
Test run for D:\FPT\PRN222\Project\Final_Project_Web\tests\SelfStorageManagementSystem.WebTests\bin\Debug\net8.0\SelfStorageManagementSystem.WebTests.dll (.NETCoreApp,Version=v8.0)
A total of 1 test files matched the specified pattern.

Passed!  - Failed:     0, Passed:    10, Skipped:     0, Total:    10, Duration: 19 s - SelfStorageManagementSystem.WebTests.dll (net8.0)
```
- **10/10 browser test PASS 100%**.

### 3.3. Toàn Bộ Kiểm Thử Web Tests Dự Án (`SelfStorageManagementSystem.WebTests.csproj`)
```text
Test run for D:\FPT\PRN222\Project\Final_Project_Web\tests\SelfStorageManagementSystem.WebTests\bin\Debug\net8.0\SelfStorageManagementSystem.WebTests.dll (.NETCoreApp,Version=v8.0)
A total of 1 test files matched the specified pattern.

Passed!  - Failed:     0, Passed:   374, Skipped:     0, Total:   374, Duration: 4 m 35 s - SelfStorageManagementSystem.WebTests.dll (net8.0)
```
- **374/374 test PASS 100% (0 failed, 0 skipped)**.

### 3.4. Kiểm Thử Hồi Quy Toàn Bộ Nghiệp Vụ BLL & DAL (`SelfStorageManagementSystem.Tests.csproj`)
```text
Test run for D:\FPT\PRN222\Project\Final_Project_Web\tests\SelfStorageManagementSystem.Tests\bin\Debug\net8.0-windows\SelfStorageManagementSystem.Tests.dll (.NETCoreApp,Version=v8.0)
A total of 1 test files matched the specified pattern.

Passed!  - Failed:     0, Passed:   225, Skipped:     8, Total:   233, Duration: 2 s - SelfStorageManagementSystem.Tests.dll (net8.0)
```
- **225 test PASS, 8 test SQL Server skip** theo kiến trúc chờ T17. Không có hồi quy.

---

## 4. Bảng Đối Soát Mã Hash SHA-256 Các Tệp Đã Sửa Đổi

Bảng đối soát mã băm SHA-256 của toàn bộ các tệp mã nguồn và kiểm thử đã tạo mới hoặc chỉnh sửa trong Vòng 9 giữa hai workspace:

| Tên Tệp Tin | SHA-256 (`Final_Project_Web`) | SHA-256 (`Final_Project`) | Kết Quả Khớp |
| :--- | :---: | :---: | :---: |
| `tests/SelfStorageManagementSystem.WebTests/Infrastructure/KestrelRealtimeServer.cs` | `E9650A56809F3E96D0ABB4DF72C7F793D57B975C104FBC1809F29026FE5EF14A` | `E9650A56809F3E96D0ABB4DF72C7F793D57B975C104FBC1809F29026FE5EF14A` | **100% Khớp** |
| `tests/SelfStorageManagementSystem.WebTests/StorageUnitRealtimeHubTests.cs` | `F1D2A3EC993A8731E76D346EBF3E53C87E5354242AFF5DD60972D81F0DDE5F9F` | `F1D2A3EC993A8731E76D346EBF3E53C87E5354242AFF5DD60972D81F0DDE5F9F` | **100% Khớp** |

*(Ghi chú: Theo quy chuẩn an toàn, tệp báo cáo không chứa mã băm tự thân của chính nó).*

---

## 5. Kết Luận & Trạng Thái Bàn Giao

1. **Tuân thủ hợp đồng và kiến trúc**:
   - Bảo toàn 100% route `/storageUnitHub`, 3 Hub methods (`HubCreate_StorageUnit`, `HubUpdate_StorageUnit`, `HubDelete_StorageUnit`) và 3 sự kiện CRUD (`Receiver_CreateStorageUnit`, `Receiver_UpdateStorageUnit`, `Receiver_DeleteStorageUnit`).
   - Tuyệt đối không can thiệp logic nghiệp vụ T12 hoặc T14; không tự ý bắt đầu T15.
2. **Trạng thái nhiệm vụ**:
   - Task T13: **`IMPLEMENTED — chờ Codex review`**
   - Task T15: **`WAITING`** (chờ T12 và T13 được Codex nghiệm thu độc lập PASS).
   - Mã nguồn, bộ test, và báo cáo được đồng bộ 100% sang cả hai workspace `Final_Project_Web` và `Final_Project`.
