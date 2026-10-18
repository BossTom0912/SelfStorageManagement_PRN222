# Báo Cáo Sửa Finding Review Vòng 8 — Task T13: SignalR Realtime CRUD Ô Kho

- **Người thực hiện**: Gemini B (Owner T13)
- **Đợt thực hiện**: Đợt 5 (Song song 4)
- **Ngày hoàn thành**: 2026-10-17
- **Workspace triển khai**: `D:\FPT\PRN222\Project\Final_Project_Web`
- **Workspace đồng bộ**: `D:\FPT\PRN222\Project\Final_Project`
- **Đầu vào review**: `AgentReport/WebMigration_T13_R7_FindingFixes.md` và 3 finding review Vòng 8 (`WEBMIGRATION_T13_R8_FIX_PROMPT.md`).
- **Trạng thái bàn giao**: **`IMPLEMENTED — chờ Codex review`** (TaskBoard giữ T13 `IMPLEMENTED — chờ Codex review`, T15 `WAITING`; Gemini không tự đánh dấu `VERIFIED`).

---

## 1. Tóm Tắt Kết Quả Xử Lý 3 Finding Vòng 8

| Mã Finding | Mức Độ | Tên Lỗi | Tình Trạng | Tệp Tin Sửa Đổi / Bổ Sung Chính |
| :--- | :---: | :--- | :---: | :--- |
| **T13-R8-F01** | **P1** | Prune thất bại nhưng broadcast guard vẫn coi là thành công | **ĐÃ KHẮC PHỤC TRIỆT ĐỂ** | [`StorageUnitHubConnectionTracker.cs`](file:///D:/FPT/PRN222/Project/Final_Project_Web/src/SelfStorageManagementSystem.Web/Realtime/StorageUnitHubConnectionTracker.cs)<br>[`RealtimeScopeRevocationException.cs`](file:///D:/FPT/PRN222/Project/Final_Project_Web/src/SelfStorageManagementSystem.Web/Realtime/RealtimeScopeRevocationException.cs)<br>[`StorageUnitHubScopeRevocationService.cs`](file:///D:/FPT/PRN222/Project/Final_Project_Web/src/SelfStorageManagementSystem.Web/Realtime/StorageUnitHubScopeRevocationService.cs)<br>[`StorageUnitRealtimeHubTests.cs`](file:///D:/FPT/PRN222/Project/Final_Project_Web/tests/SelfStorageManagementSystem.WebTests/StorageUnitRealtimeHubTests.cs)<br>[`StorageUnitsBrowserRealtimeTests.cs`](file:///D:/FPT/PRN222/Project/Final_Project_Web/tests/SelfStorageManagementSystem.WebTests/StorageUnitsBrowserRealtimeTests.cs) |
| **T13-R8-F02** | **P2** | Đo baseline CSDL thật trước và sau pre-commit DB failure | **ĐÃ KHẮC PHỤC TRIỆT ĐỂ** | [`StorageUnitRealtimeHubTests.cs`](file:///D:/FPT/PRN222/Project/Final_Project_Web/tests/SelfStorageManagementSystem.WebTests/StorageUnitRealtimeHubTests.cs) |
| **T13-R8-F03** | **P2** | Phân định Direct Integration vs Real Transport & Kiểm thử truyền thông thực tế khi publisher suy giảm | **ĐÃ KHẮC PHỤC TRIỆT ĐỂ** | [`StorageUnitRealtimeHubTests.cs`](file:///D:/FPT/PRN222/Project/Final_Project_Web/tests/SelfStorageManagementSystem.WebTests/StorageUnitRealtimeHubTests.cs) |

---

## 2. Chi Tiết Khắc Phục Từng Finding

### 2.1. T13-R8-F01 [P1] — Prune Thất Bại Nhưng Broadcast Guard Vẫn Coi Là Thành Công

#### A. Phân tích nguyên nhân & Kịch bản lỗi
1. **Nuốt ngoại lệ trong Connection Tracker**:
   Trong `StorageUnitHubConnectionTracker.cs` (hàm `AbortConnection(string connectionId)`), khối `catch (Exception ex)` ghi warning nhưng **bỏ qua ngoại lệ**, sau đó tiếp tục xóa bản ghi connection khỏi `_connectionGroups` và `_userConnections`.
   Nếu abort callback delegate gặp sự cố (ví dụ lỗi socket ngắt giữa chừng, race condition hủy context), connection bị xóa khỏi tracker trong khi kết nối WebSocket thực tế vẫn tồn tại trong Group Manager nội bộ của SignalR.
2. **Revocation Service không báo lỗi cho caller**:
   Trong `StorageUnitHubScopeRevocationService.cs`, khi `IGroupManager.RemoveFromGroupAsync` thất bại sau 3 lần retry, phương thức chỉ gọi `_tracker.AbortConnection(connId)` và tiếp tục hoàn tất phương thức `ReconcileUserConnectionsAsync` mà **không hề ném exception hoặc báo trạng thái thất bại**. Tương tự với nhánh tài khoản bị khóa/hạ vai trò.
3. **Broadcast Guard ngộ nhận đã sạch (False Verified State)**:
   Khi `ReconcileUserConnectionsAsync` kết thúc êm thấm, `PruneConnectionsForBroadcastAsync` và `PruneUnverifiedAdminConnectionsAsync` xem như việc dọn dẹp hoàn tất. Kết quả là `StorageUnitRealtimePublisher` tiếp tục phát DTO CRUD nhạy cảm tới group `facility_{id}` và `admin_global`.
   Hơn nữa, vì tracker đã xóa bản ghi connection, những lần phát tin sau `_tracker.GetConnectionsInGroup` trả về rỗng, khiến broadcast guard hoàn toàn bỏ qua connection này — tạo ra trạng thái "sạch giả" trong khi client không còn quyền vẫn nhận DTO đều đặn.
4. **Bản chất của `Context.Abort()`**:
   Phương thức `Context.Abort()` là hành động hủy socket bất đồng bộ ở tầng transport, không phải bằng chứng tức thời chứng minh client đã rời khỏi nhóm SignalR. Khi `RemoveFromGroupAsync` thất bại, việc gọi abort chỉ là biện pháp phòng thủ phụ trợ, không thể coi là đã prune thành công để tiếp tục broadcast.

#### B. Giải pháp kỹ thuật đã triển khai
1. **Bảo toàn tính toàn vẹn trạng thái trong [`StorageUnitHubConnectionTracker.cs`](file:///D:/FPT/PRN222/Project/Final_Project_Web/src/SelfStorageManagementSystem.Web/Realtime/StorageUnitHubConnectionTracker.cs)**:
   - Trong `AbortConnection(string connectionId)`: Tuyệt đối không nuốt exception của abort delegate.
   - Nếu delegate ném exception, phương thức rethrow ngay lập tức và **không xóa connection hoặc groups** khỏi `_connectionGroups` và `_userConnections`. Điều này ngăn chặn việc tạo ra trạng thái "sạch giả" trong tracker.
2. **Khởi tạo biệt lệ thu hồi phạm vi [`RealtimeScopeRevocationException.cs`](file:///D:/FPT/PRN222/Project/Final_Project_Web/src/SelfStorageManagementSystem.Web/Realtime/RealtimeScopeRevocationException.cs)**:
   - Tạo mới lớp exception kế thừa `InvalidOperationException` để đại diện cho mọi trường hợp thu hồi quyền kết nối realtime không thể xác minh thành công.
3. **Fail-Closed dứt khoát trong [`StorageUnitHubScopeRevocationService.cs`](file:///D:/FPT/PRN222/Project/Final_Project_Web/src/SelfStorageManagementSystem.Web/Realtime/StorageUnitHubScopeRevocationService.cs)**:
   - Trong cả hai nhánh: Nhánh 1 (tài khoản không hoạt động/bị khóa) và Nhánh 2 (tài khoản hoạt động nhưng bị thu hồi quyền cơ sở hoặc quyền admin):
     - Khi `TryRemoveFromGroupWithRetryAsync` trả về `false` (thất bại sau 3 lần retry):
     - Vẫn cố gắng kích hoạt `_tracker.AbortConnection(connId)` như biện pháp phòng thủ phụ trợ.
     - **Bắt buộc ném `RealtimeScopeRevocationException`** thông báo rõ không thể xác minh việc thu hồi group hoặc ngắt kết nối an toàn cho connection.
   - Nhờ vậy, `PruneConnectionsForBroadcastAsync` và `PruneUnverifiedAdminConnectionsAsync` sẽ nhận được ngoại lệ, không nuốt lỗi, và trả exception cho caller.
4. **Chặn đứng phát tin DTO trong [`StorageUnitRealtimePublisher.cs`](file:///D:/FPT/PRN222/Project/Final_Project_Web/src/SelfStorageManagementSystem.Web/Realtime/StorageUnitRealtimePublisher.cs)**:
   - Các guard phát tin trong publisher bắt được ngoại lệ suy giảm từ revocation service, ghi log cảnh báo và ném `RealtimePublishDegradedException`.
   - **Tuyệt đối 0 DTO nào được gửi tới `Clients.Groups(...)` hay `Clients.Group("admin_global")`** (fail-closed toàn diện).
5. **Cập nhật lại test cũ trong [`StorageUnitsBrowserRealtimeTests.cs`](file:///D:/FPT/PRN222/Project/Final_Project_Web/tests/SelfStorageManagementSystem.WebTests/StorageUnitsBrowserRealtimeTests.cs)**:
   - Cập nhật test `T13_R3_F02_RevocationService_WhenRemoveFromGroupFailsPermanently_Retries3Times_AndEnforcesFailClosedAbort`: khẳng định `ReconcileUserConnectionsAsync` ném `RealtimeScopeRevocationException` đúng quy chuẩn fail-closed mới thay vì hoàn tất âm thầm.

#### C. Bằng chứng kiểm thử tự động
Triển khai 5 ca kiểm thử chuyên biệt trong `StorageUnitRealtimeHubTests.cs`:
1. `T13_R8_F01_DemotedAdmin_WhenRemoveFromGroupAndAbortFail_FailsClosed_TrackerRetainsGroup_ZeroEvents`: Admin bị hạ quyền, cả SignalR group removal và abort callback đều thất bại -> Publisher ném `RealtimePublishDegradedException`, 0 DTO broadcast, tracker bảo toàn group `admin_global` không bị xóa ảo.
2. `T13_R8_F01_StaffExpired_WhenRemoveFromGroupAndAbortFail_FailsClosed_TrackerRetainsGroup_ZeroEvents`: Nhân viên hết hạn phân công, remove group và abort thất bại -> Publisher fail-closed ném `RealtimePublishDegradedException`, 0 DTO broadcast, tracker giữ nguyên facility group.
3. `T13_R8_F01_LockedAccount_WhenRemoveFromGroupAndAbortFail_FailsClosed_TrackerRetainsGroup_ZeroEvents`: Tài khoản bị khóa, remove group và abort thất bại -> Publisher fail-closed, 0 DTO broadcast.
4. `T13_R8_F01_NormalRemoval_Succeeds_AndRemainingAuthorizedUsersReceiveEvent`: Trường hợp remove group thành công bình thường -> Connection vi phạm bị loại khỏi tracker, connection hợp lệ còn lại trong `admin_global` vẫn nhận DTO broadcast thành công.
5. `T13_R8_F01_FallbackDelete_WhenRemoveFromGroupFails_FailsClosed_ZeroEvents`: Nhánh fallback `PublishDeletedAsync(unitId)` khi remove group thất bại -> Fail-closed, 0 sự kiện gửi tới `admin_global`.

---

### 2.2. T13-R8-F02 [P2] — Đo Baseline CSDL Thật Trước và Sau Pre-Commit DB Failure

#### A. Phân tích nguyên nhân
Trong Vòng 7, test `T13_R7_F03_Integration_WhenPreCommitDbFails_ThrowsException_NoDatabaseChange_NoPublisher` chỉ kiểm tra gián tiếp mã ô kho trùng, chưa chụp snapshot baseline CSDL trước khi thao tác để chứng minh 100% không có bất kỳ thay đổi, đột biến hoặc bản ghi mồ côi (orphan record) nào bị ghi nhận dở dang vào CSDL. Báo cáo R7 cũng ghi nhận nhận định này khái quát mà chưa có phép đo baseline cụ thể.

#### B. Giải pháp kỹ thuật đã triển khai
1. **Đo đạc và xác nhận Baseline CSDL trong [`StorageUnitRealtimeHubTests.cs`](file:///D:/FPT/PRN222/Project/Final_Project_Web/tests/SelfStorageManagementSystem.WebTests/StorageUnitRealtimeHubTests.cs)**:
   - Trong `T13_R7_F03_Integration_WhenPreCommitDbFails_ThrowsException_NoDatabaseChange_NoPublisher`:
     - **Chụp snapshot baseline trước thao tác** từ một DbContext độc lập:
       - Đếm tổng số lượng ô kho trong cơ sở: `initialUnitCount`.
       - Tập hợp toàn bộ danh sách ID ô kho hiện có: `initialUnitIds`.
       - Ghi nhận chính xác các trường dữ liệu của ô kho gốc: `initialUpdatedAt`, `initialFloor`, `initialZone`, `initialPhysicalStatus`.
     - **Sau mỗi thao tác Razor Pages thất bại (Create, Edit, Delete)**:
       - Mở một `DbContext` scope hoàn toàn mới.
       - Khẳng định số lượng ô kho `storage_units.Count()` bằng đúng `initialUnitCount`.
       - Khẳng định tập hợp ID ô kho không thay đổi (`SetEquals(initialUnitIds)`).
       - Khẳng định các trường của ô kho gốc giữ nguyên giá trị baseline, không bị đột biến cục bộ.
       - Khẳng định `mockPublisher.Verify(..., Times.Never)`.
     - **Sau mỗi thao tác SignalR Hub thất bại (HubCreate, HubUpdate, HubDelete)**:
       - Mở tiếp một `DbContext` scope hoàn toàn mới và thực hiện bộ assertion baseline tương tự.
2. **Chứng minh năng lực phát hiện đột biến dữ liệu dở dang (Rogue Mutation Test)**:
   - Bổ sung test `T13_R8_F02_PreCommitDbFailure_RoguePartialMutation_IsDetectedByDatabaseStateAssertions`:
     - Tạo baseline, sau đó cố tình tiêm một bản ghi ô kho rác/dở dang vào CSDL (`rogue unit`) mà không cập nhật count/ids hợp lệ.
     - Khẳng định bộ assertion baseline lập tức phát hiện ra sự thay đổi và kích hoạt lỗi assertion (`Assert.Equal` failure), chứng minh rằng các assertion trạng thái CSDL có độ nhạy thực sự, không phải assertion hình thức.
3. **Hiệu đính báo cáo**:
   - Khẳng định tính trung thực: các assertion CSDL trong R8 kiểm tra toàn diện cả số lượng bản ghi, tập hợp ID, các trường thuộc tính chi tiết, và hoàn toàn độc lập với context của service/page.

---

### 2.3. T13-R8-F03 [P2] — Phân Định Rõ Ràng Direct Integration vs Real Transport & Kiểm Thử Truyền Thông Thực Tế

#### A. Phân tích nguyên nhân
Trong Vòng 7, các test `T13_R7_F03` khởi tạo trực tiếp instance của `CreateModel`, `EditModel`, `DeleteModel` và `StorageUnitHub` trên bộ nhớ (Direct PageModel/Hub Tests). Mặc dù các test này sử dụng BLL và DbContext thật, chúng không đi qua:
- Pipeline HTTP của Kestrel/TestServer: Cookie Authentication handler, Antiforgery Token validation, Model Binding, HTTP 302 Redirect handling, TempData cookie lifecycle.
- Pipeline WebSocket của SignalR: Hub Connection handshake, WebSocket frame dispatch, JSON invocation serialization/deserialization.
Báo cáo R7 đã gọi chung các test này là "kiểm thử tích hợp đầu-cuối", gây hiểu nhầm về mức độ bao phủ đường truyền thực tế.

#### B. Giải pháp kỹ thuật đã triển khai
1. **Phân định minh bạch hai cấp độ kiểm thử**:
   - **Direct PageModel / Hub Integration Tests**: Gọi trực tiếp method của PageModel/Hub trên bộ nhớ với BLL và EF DbContext thật để xác minh logic xử lý lỗi, điều phối giao dịch và ánh xạ ngoại lệ nghiệp vụ.
   - **End-to-End Transport Tests**: Gửi HTTP request thật qua `HttpClient` tới Kestrel/TestServer và gửi SignalR invocation thật qua WebSocket `HubConnection` với cookie xác thực.
2. **Cấu hình WebApplicationFactory với Degraded Publisher Decorator**:
   - Xây dựng lớp `DegradedRealtimePublisherDecorator` thay thế `IStorageUnitRealtimePublisher` trong DI container thông qua `_factory.WithWebHostBuilder`:
     - Mô phỏng chính xác trường hợp CSDL đã commit thành công nhưng realtime broadcast bị suy giảm (ném `RealtimePublishDegradedException`).
3. **Triển khai 4 ca kiểm thử truyền thông thực tế (Real Transport Tests)**:
   - **`T13_R8_F03_E2E_RazorPages_HttpEndpoint_WhenPublisherDegraded_DbCommits_AndClientReceivesWarningRedirect`**:
     - Client đăng nhập qua Cookie Auth thật (`/Account/Login`).
     - Gửi `GET /StorageUnits/Create` để trích xuất Antiforgery Token thật.
     - Gửi `POST /StorageUnits/Create` qua HTTP pipeline thật.
     - Khẳng định server phản hồi HTTP `302 Redirect` tới `/StorageUnits/Index`.
     - Theo vết redirect, phân tích nội dung HTML: khẳng định có hiển thị cảnh báo `WarningMessage` (báo hiệu realtime suy giảm), **không chứa lời mời gọi thử lại** ("vui lòng thử lại").
     - Mở `DbContext` scope mới: khẳng định ô kho mới đã được commit thực sự trong CSDL.
   - **`T13_R8_F03_E2E_SignalR_WebSocketTransport_WhenPublisherDegraded_DbCommits_AndHubCallReturnsConfirmedDto`**:
     - Khởi tạo kết nối WebSocket `HubConnection` thật tới `/storageUnitHub` kèm Cookie Auth.
     - Gọi `await hubConnection.InvokeAsync<StorageUnitRealtimeEventDto>("HubCreate_StorageUnit", jsonPayload)`.
     - Khẳng định phương thức trả về DTO xác nhận thành công, **hoàn toàn không ném `HubException`**.
     - Mở `DbContext` scope mới: khẳng định ô kho mới đã tồn tại vững chắc trong CSDL.
   - **`T13_R8_F03_E2E_RazorPages_HttpEditAndDelete_WhenPublisherDegraded_DbUpdatesAndDeletes_WithWarningRedirect`**:
     - Thực hiện tuần tự `GET` và `POST` cho `/StorageUnits/Edit` và `/StorageUnits/Delete` qua HTTP transport thật.
     - Cả hai thao tác đều nhận `302 Redirect`.
     - Mở `DbContext` scope mới: xác minh bản ghi ô kho đã được cập nhật dữ liệu mới sau Edit và bị xóa hoàn toàn khỏi CSDL sau Delete.
   - **`T13_R8_F03_E2E_SignalR_WebSocketUpdateAndDelete_WhenPublisherDegraded_DbUpdatesAndDeletes_WithoutHubException`**:
     - Thực hiện `HubUpdate_StorageUnit` và `HubDelete_StorageUnit` qua WebSocket transport thật với `HubConnection`.
     - Cả hai phương thức hoàn tất thành công, trả về DTO cập nhật và ID xóa mà không có bất kỳ `HubException` nào.
     - Mở `DbContext` scope mới: xác minh dữ liệu CSDL được cập nhật và xóa chính xác.

---

### 2.4. Hiệu Đính Kiểm Thử Trình Duyệt R7-F05 (Browser RED/GREEN & Tên Hook)

1. **Làm rõ điều kiện kiểm tra generation trong `storage-units-realtime.js`**:
   - Tại dòng 266:
     ```javascript
     if (generation < latestScheduledGeneration || generation < latestAppliedGeneration) {
         // Snapshot cũ bị loại bỏ (superseded)
         return;
     }
     ```
   - Điều kiện kết hợp cả hai yếu tố: thế hệ đã lên lịch (`latestScheduledGeneration`) và thế hệ đã được áp dụng (`latestAppliedGeneration`).
2. **Khẳng định kết quả RED / GREEN thực tế**:
   - Khi vô hiệu hóa cả hai điều kiện kiểm tra (thay bằng `if (false && ...)`): Snapshot A cũ (11 ô kho) được áp dụng sau Snapshot B mới (12 ô kho), khiến DOM bị rollback ngược về 11 ô kho. Test F05 phát hiện rollback và lập tức **FAIL (RED)**.
   - Khi giữ nguyên logic: Snapshot A bị loại bỏ, DOM duy trì 12 ô kho chuẩn xác. Test F05 **PASS (GREEN)** trong 9 giây.
3. **Hiệu đính tên hook**:
   - Biến callback toàn cục được phơi bày trên `window` phục vụ việc giải phóng fetch 1 có tên chính xác trong mã nguồn là **`window.__resolveFirst`** (trong lời văn báo cáo R7 từng ghi nhầm thành `__releaseFetch1`). Báo cáo R8 hiệu đính chuẩn xác theo mã nguồn thực tế.

---

## 3. Báo Cáo Kết Quả Kiểm Thử Thực Tế Vòng 8

### 3.1. Toàn bộ Suite Kiểm thử Hub SignalR (`StorageUnitRealtimeHubTests.cs`)
```text
Test run for D:\FPT\PRN222\Project\Final_Project_Web\tests\SelfStorageManagementSystem.WebTests\bin\Debug\net8.0\SelfStorageManagementSystem.WebTests.dll (.NETCoreApp,Version=v8.0)
A total of 1 test files matched the specified pattern.

Passed!  - Failed:     0, Passed:    50, Skipped:     0, Total:    50, Duration: 4 m 44 s - SelfStorageManagementSystem.WebTests.dll (net8.0)
```
- **50/50 test PASS 100%** (bao gồm 5 test F01, 2 test F02, 4 test F03 và toàn bộ các test R5-R7 trước đó).

### 3.2. Toàn bộ Suite Kiểm thử Trình duyệt Headless Edge (`StorageUnitsBrowserRealtimeTests.cs`)
```text
Test run for D:\FPT\PRN222\Project\Final_Project_Web\tests\SelfStorageManagementSystem.WebTests\bin\Debug\net8.0\SelfStorageManagementSystem.WebTests.dll (.NETCoreApp,Version=v8.0)
A total of 1 test files matched the specified pattern.

Passed!  - Failed:     0, Passed:    10, Skipped:     0, Total:    10, Duration: 19 s - SelfStorageManagementSystem.WebTests.dll (net8.0)
```
- **10/10 test PASS 100%**.

### 3.3. Toàn bộ Kiểm thử Web Tests Dự án (`SelfStorageManagementSystem.WebTests.csproj`)
```text
Test run for D:\FPT\PRN222\Project\Final_Project_Web\tests\SelfStorageManagementSystem.WebTests\bin\Debug\net8.0\SelfStorageManagementSystem.WebTests.dll (.NETCoreApp,Version=v8.0)
A total of 1 test files matched the specified pattern.

Passed!  - Failed:     0, Passed:   374, Skipped:     0, Total:   374, Duration: 4 m 44 s - SelfStorageManagementSystem.WebTests.dll (net8.0)
```
- **374/374 test PASS 100% (0 failed, 0 skipped)**.

### 3.4. Kiểm thử Hồi quy BLL & DAL (`SelfStorageManagementSystem.Tests.csproj`)
```text
Test run for D:\FPT\PRN222\Project\Final_Project_Web\tests\SelfStorageManagementSystem.Tests\bin\Debug\net8.0-windows\SelfStorageManagementSystem.Tests.dll (.NETCoreApp,Version=v8.0)
A total of 1 test files matched the specified pattern.

Passed!  - Failed:     0, Passed:   225, Skipped:     8, Total:   233, Duration: 2 s - SelfStorageManagementSystem.Tests.dll (net8.0)
```
- **225 test PASS, 8 test SQL Server skip** theo kiến trúc chờ T17. Không có bất kỳ lỗi hồi quy nào.

---

## 4. Bảng Đối Soát Mã Hash SHA-256 Các Tệp Đã Sửa Đổi

Bảng đối soát mã băm SHA-256 của toàn bộ các tệp mã nguồn và kiểm thử đã tạo mới hoặc chỉnh sửa trong Vòng 8 giữa hai workspace:

| Tên Tệp Tin | SHA-256 (`Final_Project_Web`) | SHA-256 (`Final_Project`) | Kết Quả Khớp |
| :--- | :---: | :---: | :---: |
| `src/SelfStorageManagementSystem.Web/Realtime/RealtimeScopeRevocationException.cs` | `F404619A40BCB0FDBC4BA763AE95E528ECAA6241842386353D29658016871D34` | `F404619A40BCB0FDBC4BA763AE95E528ECAA6241842386353D29658016871D34` | **100% Khớp** |
| `src/SelfStorageManagementSystem.Web/Realtime/StorageUnitHubConnectionTracker.cs` | `30AFA298D6D32B6F2F9B9A9D3BCB1FC56ECEF36590004E3A0CAE091E27E1E05E` | `30AFA298D6D32B6F2F9B9A9D3BCB1FC56ECEF36590004E3A0CAE091E27E1E05E` | **100% Khớp** |
| `src/SelfStorageManagementSystem.Web/Realtime/StorageUnitHubScopeRevocationService.cs` | `AD02BF842D8CCD4DA6978D81188F528118E444AADAC665303DA28D515B467F5F` | `AD02BF842D8CCD4DA6978D81188F528118E444AADAC665303DA28D515B467F5F` | **100% Khớp** |
| `tests/SelfStorageManagementSystem.WebTests/StorageUnitsBrowserRealtimeTests.cs` | `BA2B1E49C46BE570FD2D06244C718CB17D9B99649687C45DBAB4293A3A674F5D` | `BA2B1E49C46BE570FD2D06244C718CB17D9B99649687C45DBAB4293A3A674F5D` | **100% Khớp** |
| `tests/SelfStorageManagementSystem.WebTests/StorageUnitRealtimeHubTests.cs` | `11D204F589E13DDAADF12F3A84AF92CBB78D1493E1D3E02EFB8A574CCEA64122` | `11D204F589E13DDAADF12F3A84AF92CBB78D1493E1D3E02EFB8A574CCEA64122` | **100% Khớp** |

*(Ghi chú: Theo quy chuẩn an toàn, tệp báo cáo không chứa mã băm tự thân của chính nó).*

---

## 5. Kết Luận & Trạng Thái Bàn Giao

1. **Tuân thủ hợp đồng và kiến trúc**:
   - Giữ nguyên vẹn 100% route `/storageUnitHub`, 3 Hub methods (`HubCreate_StorageUnit`, `HubUpdate_StorageUnit`, `HubDelete_StorageUnit`) và 3 sự kiện CRUD (`Receiver_CreateStorageUnit`, `Receiver_UpdateStorageUnit`, `Receiver_DeleteStorageUnit`).
   - Tuyệt đối không can thiệp logic nghiệp vụ T12 hoặc T14; không tự ý bắt đầu T15.
2. **Trạng thái nhiệm vụ**:
   - Task T13: **`IMPLEMENTED — chờ Codex review`**
   - Task T15: **`WAITING`** (chờ T12 và T13 được Codex nghiệm thu độc lập PASS).
   - Mã nguồn, bộ test, và báo cáo đã được đồng bộ 100% sang cả hai workspace `Final_Project_Web` và `Final_Project`.
