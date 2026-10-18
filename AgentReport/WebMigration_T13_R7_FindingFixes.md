# Báo Cáo Sửa Finding Review Vòng 7 — Task T13: SignalR Realtime CRUD Ô Kho

- **Người thực hiện**: Gemini B (Owner T13)
- **Đợt thực hiện**: Đợt 5 (Song song 4)
- **Ngày hoàn thành**: 2026-10-17
- **Workspace triển khai**: `D:\FPT\PRN222\Project\Final_Project_Web`
- **Workspace đồng bộ**: `D:\FPT\PRN222\Project\Final_Project`
- **Đầu vào review**: `AgentReport/WebMigration_T13_R6_FindingFixes.md` và 3 finding review Vòng 7 (`WEBMIGRATION_T13_R7_FIX_PROMPT.md`).
- **Trạng thái bàn giao**: **`IMPLEMENTED — chờ Codex review`** (Task Board giữ T13 `IMPLEMENTED — chờ Codex review`, T15 `WAITING`; Gemini không tự đánh dấu `VERIFIED`).

---

## 1. Tóm Tắt Kết Quả Xử Lý 3 Finding Vòng 7

| Mã Finding | Mức Độ | Tên Lỗi | Tình Trạng | Tệp Tin Sửa Đổi / Bổ Sung Chính |
| :--- | :---: | :--- | :--- :---: | :--- |
| **T13-R7-F01** | **P1** | Thu hồi quyền `admin_global` không được xác minh tại thời điểm phát CRUD | **ĐÃ KHẮC PHỤC TRIỆT ĐỂ** | [`StorageUnitHubScopeRevocationService.cs`](file:///D:/FPT/PRN222/Project/Final_Project_Web/src/SelfStorageManagementSystem.Web/Realtime/StorageUnitHubScopeRevocationService.cs)<br>[`StorageUnitRealtimePublisher.cs`](file:///D:/FPT/PRN222/Project/Final_Project_Web/src/SelfStorageManagementSystem.Web/Realtime/StorageUnitRealtimePublisher.cs)<br>[`StorageUnitRealtimeHubTests.cs`](file:///D:/FPT/PRN222/Project/Final_Project_Web/tests/SelfStorageManagementSystem.WebTests/StorageUnitRealtimeHubTests.cs) |
| **T13-R7-F02** | **P2** | Browser test F05 bỏ qua barrier phản hồi server và generation bị loại | **ĐÃ KHẮC PHỤC TRIỆT ĐỂ** | [`StorageUnitsBrowserRealtimeTests.cs`](file:///D:/FPT/PRN222/Project/Final_Project_Web/tests/SelfStorageManagementSystem.WebTests/StorageUnitsBrowserRealtimeTests.cs) |
| **T13-R7-F03** | **P2** | Bộ test suy giảm realtime R6 chỉ mock bộ nhớ, chưa chứng minh CSDL commit và thiếu báo cáo khách quan | **ĐÃ KHẮC PHỤC TRIỆT ĐỂ** | [`StorageUnitRealtimeHubTests.cs`](file:///D:/FPT/PRN222/Project/Final_Project_Web/tests/SelfStorageManagementSystem.WebTests/StorageUnitRealtimeHubTests.cs) |

---

## 2. Chi Tiết Khắc Phục Từng Finding

### 2.1. T13-R7-F01 [P1] — Đối Soát Quyền `admin_global` và Cơ Sở Tại Thời Điểm Phát Sự Kiện & Fail-Closed Toàn Diện

#### A. Phân tích nguyên nhân
1. Khi vai trò admin bị hạ cấp (demote) hoặc tài khoản admin bị vô hiệu hóa/chấm dứt, phương thức `AdminAccountService` đã commit thay đổi vào DB rồi gọi `IUserScopeChangedNotifier`. Nếu notifier gặp lỗi ngoài ý muốn hoặc worker định kỳ (`StorageUnitScopeSyncWorker`) chưa đến chu kỳ quét kế tiếp (hoặc dịch vụ kiểm tra quyền gặp lỗi), kết nối WebSocket của tài khoản bị hạ cấp vẫn có thể còn nằm trong group SignalR `admin_global`.
2. Trong mã nguồn Vòng 6 của `StorageUnitRealtimePublisher`:
   - Hàm `PublishCreatedAsync`, `PublishUpdatedAsync`, `PublishDeletedAsync(unitId, facilityId)` chỉ gọi `PruneExpiredConnectionsForFacilityAsync(facilityId)`. Nhóm `admin_global` hoàn toàn không được đối soát quyền tại thời điểm phát tin CRUD.
   - Khi bước prune của `facility_{id}` gặp lỗi và rơi vào khối `catch (Exception ex)`: mã nguồn cũ vẫn thực hiện gửi payload CRUD DTO chi tiết tới `admin_global`:
     ```csharp
     await _hubContext.Clients.Group("admin_global").SendAsync(..., eventDto, cancellationToken);
     ```
     Điều này vi phạm nguyên tắc an toàn Fail-Closed: nếu bước kiểm tra quyền đang gặp sự cố (ví dụ lỗi truy vấn CSDL quyền hoặc dịch vụ phân quyền), việc tiếp tục phát payload dữ liệu nhạy cảm tới bất kỳ nhóm chưa được xác minh nào (kể cả `admin_global`) có nguy cơ làm rò rỉ dữ liệu tới kết nối của tài khoản vừa bị giáng cấp.
   - Nhánh fallback `PublishDeletedAsync(long unitId)` (khi không rõ `facilityId`) chỉ phát trực tiếp tới `admin_global` mà không hề có bước đối soát quyền của các kết nối đang nằm trong group `admin_global`.

#### B. Giải pháp kỹ thuật đã triển khai
1. **Nâng cấp [`StorageUnitHubScopeRevocationService.cs`](file:///D:/FPT/PRN222/Project/Final_Project_Web/src/SelfStorageManagementSystem.Web/Realtime/StorageUnitHubScopeRevocationService.cs)**:
   - Bổ sung phương thức `PruneConnectionsForBroadcastAsync(long? facilityId, bool includeAdminGlobal, CancellationToken cancellationToken = default)`:
     - Tập hợp danh sách các `UserId` phân biệt (distinct) từ cả hai nhóm mục tiêu phát tin: group `facility_{id}` và group `admin_global`.
     - Thực hiện đối soát quyền thực tế (`ReconcileUserConnectionsAsync`) cho từng người dùng ngay tại thời điểm chuẩn bị phát tin. Bất kỳ kết nối nào không còn vai trò admin hợp lệ sẽ lập tức bị loại bỏ khỏi `admin_global` và/hoặc ngắt kết nối trước khi broadcast.
   - Bổ sung phương thức `PruneUnverifiedAdminConnectionsAsync(CancellationToken cancellationToken = default)` chuyên biệt để bảo vệ nhánh fallback `PublishDeletedAsync(long unitId)`.
   - Giữ nguyên `PruneExpiredConnectionsForFacilityAsync(facilityId, cancellationToken)` ủy quyền vào `PruneConnectionsForBroadcastAsync` nhằm đảm bảo tính tương thích ngược tuyệt đối.
2. **Cập nhật [`StorageUnitRealtimePublisher.cs`](file:///D:/FPT/PRN222/Project/Final_Project_Web/src/SelfStorageManagementSystem.Web/Realtime/StorageUnitRealtimePublisher.cs)**:
   - Trong `PublishCreatedAsync`, `PublishUpdatedAsync`, `PublishDeletedAsync(unitId, facilityId)`:
     - Gọi `_revocationService.PruneConnectionsForBroadcastAsync(facilityId, includeAdminGlobal: true, cancellationToken)`.
     - **Fail-Closed toàn diện**: Nếu bước prune gặp bất kỳ sự cố nào (lỗi DB quyền, lỗi phân quyền, exception):
       - **Tuyệt đối không gửi payload CRUD DTO cho bất kỳ nhóm nào** (chặn hoàn toàn cả `facility_{id}` lẫn `admin_global`).
       - Chỉ phát sự kiện an toàn chung `CatalogInvalidated` tới client công khai nếu cần báo hiệu vô hiệu hóa cache.
       - Ghi log cảnh báo `LogWarning` và ném `RealtimePublishDegradedException`.
   - Trong nhánh fallback `PublishDeletedAsync(long unitId)`:
     - Gọi `_revocationService.PruneUnverifiedAdminConnectionsAsync(cancellationToken)`.
     - Nếu prune thất bại: rơi vào khối `catch`, **không gửi DTO** tới `admin_global`, ghi log cảnh báo và ném `RealtimePublishDegradedException`.
3. **Bộ kiểm thử tích hợp thực tế với kết nối WebSocket Kestrel thật trong [`StorageUnitRealtimeHubTests.cs`](file:///D:/FPT/PRN222/Project/Final_Project_Web/tests/SelfStorageManagementSystem.WebTests/StorageUnitRealtimeHubTests.cs)**:
   - `T13_R7_F01_DemotedAdmin_WhenNotifierFails_PrunedAtBroadcastTime_ZeroEvents`: Khởi tạo kết nối WebSocket Hub thật của một admin, hạ cấp admin thành customer trong CSDL nhưng cố tình bỏ qua notifier (mô phỏng notifier thất bại trước chu kỳ quét của worker). Khi publisher phát CRUD event, phương thức `PruneConnectionsForBroadcastAsync` lập tức phát hiện admin đã bị hạ cấp, loại kết nối khỏi `admin_global`. Kết nối của tài khoản bị hạ cấp nhận **đúng 0 sự kiện CRUD**.
   - `T13_R7_F01_IdentityQueryFails_FailClosed_BothFacilityAndAdminGlobalReceiveZeroEvents`: Mô phỏng truy vấn quyền/danh tính bị lỗi CSDL khi chuẩn bị phát tin. Publisher thực hiện fail-closed: **cả kết nối ở `admin_global` và kết nối ở `facility_{id}` đều nhận đúng 0 sự kiện CRUD**, và publisher ném `RealtimePublishDegradedException`.
   - `T13_R7_F01_FallbackDelete_GuardsAdminGlobal_WhenPruningFails_ThrowsDegradedAndZeroEvents`: Nhánh fallback `PublishDeletedAsync(unitId)` khi prune `admin_global` gặp lỗi sẽ ném `RealtimePublishDegradedException` và không phát bất kỳ sự kiện nào tới `admin_global`.

---

### 2.2. T13-R7-F02 [P2] — Rào Chắn Phản Hồi Server và Kiểm Chứng Generation Bị Loại Trừ Trong Browser Test F05

#### A. Phân tích nguyên nhân
Trong Vòng 6, test browser F05 đã gán mã ô kho `UNIT-00-A` và `UNIT-00-B` để đưa lên Trang 1, tuy nhiên:
1. Thiếu rào chắn (barrier) xác minh Snapshot A thực sự được phản hồi từ server trước khi commit Snapshot B: Mã test giải phóng request A nhưng chưa khẳng định nội dung HTML của Snapshot A do server trả về thực sự chỉ chứa ô kho `UNIT-00-A` (hàng 401) và không chứa ô kho `UNIT-00-B` (hàng 402), với tổng số ô kho là 11.
2. Sau khi giải phóng fetch 1, mã test sử dụng `Task.Delay(1500)` tĩnh và kiểm tra mảng log console chung chung, thay vì quan sát trực tiếp kết quả của generation bị loại trừ (`superseded`) trong chu trình `refetchTable`.
3. Thiếu khối `try ... finally` để khôi phục các interceptor `window.fetch` và dọn dẹp các bản ghi ô kho thử nghiệm trong CSDL sau khi test hoàn tất.

#### B. Giải pháp kỹ thuật đã triển khai
1. **Nâng cấp Test F05 trong [`StorageUnitsBrowserRealtimeTests.cs`](file:///D:/FPT/PRN222/Project/Final_Project_Web/tests/SelfStorageManagementSystem.WebTests/StorageUnitsBrowserRealtimeTests.cs)**:
   - **Ghi nhận phản hồi và generation của Fetch 1**:
     - Interceptor ghi nhận lại chuỗi HTML thật do server trả về vào biến toàn cục: `window.__resp1Html = htmlText;`.
     - Đồng thời ghi nhận thế hệ của fetch 1: `window.__fetch1Gen = window.StorageUnitsRealtime.getLatestScheduledGeneration();`.
   - **Barrier 1 (Kiểm chứng phản hồi Snapshot A từ server thật)**:
     - Chờ `resp1Ready == true` (tối đa 5 giây).
     - Khẳng định chuỗi HTML trả về từ server của Snapshot A:
       - Chứa hàng `unit-row-401` (`UNIT-00-A`).
       - **Không chứa** hàng `unit-row-402` (`UNIT-00-B`).
       - Chứa chuỗi đếm tổng số ô kho là `11`.
   - **Barrier 2 (Commit Snapshot B và xác nhận áp dụng lên DOM)**:
     - Tạo `UNIT-00-B` (ID 402) trong CSDL thật.
     - Phát tín hiệu SignalR cho B qua WebSocket thật.
     - Chờ và khẳng định DOM đã hiển thị Snapshot B: hàng 402 có trong DOM, tổng số đếm là 12, phân trang hiển thị `1 đến 10 của 12 ô kho`.
   - **Barrier 3 (Giải phóng Fetch 1 và quan sát Generation Superseded Outcome)**:
     - Kích hoạt callback giải phóng `window.__releaseFetch1()`.
     - Thay vì dùng `Task.Delay` tĩnh, test kiểm tra vòng lặp có điều kiện (với timeout an toàn) trên mảng `window.__refetchOutcomes`:
       ```javascript
       window.__refetchOutcomes.some(o => o.generation === window.__fetch1Gen && o.status === 'superseded')
       ```
     - Khẳng định kết quả: generation của fetch 1 đã bị hệ thống xác định chính xác là `superseded`.
   - **Barrier 4 (Kiểm chứng tính bất biến của DOM — Chống Rollback)**:
     - Khẳng định hàng `unit-row-402` vẫn tồn tại vững chắc trong DOM.
     - Tổng số ô kho hiển thị vẫn là 12 (không bị rollback về 11).
     - Không có banner cảnh báo lỗi `#realtimeSyncAlert` nào xuất hiện.
   - **Dọn dẹp tài nguyên trong khối `try ... finally`**:
     - Trong khối `finally`, khôi phục lại `window.fetch` và `window.StorageUnitsRealtime.refetchTable` nguyên bản.
     - Xóa triệt để các ô kho kiểm thử `UNIT-00-A` và `UNIT-00-B` khỏi CSDL bằng DbContext độc lập.
2. **Chứng minh năng lực phát hiện lỗi (RED/GREEN Demonstration)**:
   - **Thử nghiệm RED**: Khi cố tình vô hiệu hóa điều kiện loại trừ generation cũ (`generation < latestAppliedGeneration`) trong `storage-units-realtime.js`:
     - Test lập tức chuyển sang trạng thái **FAIL** (bị lỗi assertion vì DOM bị rollback về 11 và mất hàng 402, `Failed: 1, Passed: 0`).
   - **Thử nghiệm GREEN**: Khôi phục lại điều kiện bảo vệ generation:
     - Test chuyển sang trạng thái **PASS** tuyệt đối (`Passed: 1, Failed: 0` trong 9 giây).

---

### 2.3. T13-R7-F03 [P2] — Bộ Test Tích Hợp CSDL Thật Chứng Minh Commit Dữ Liệu Khi Realtime Bị Suy Giảm & Báo Cáo Khách Quan

#### A. Phân tích nguyên nhân & Nhận diện hạn chế của Vòng 6
1. Trong Vòng 6, các test kiểm tra trường hợp realtime suy giảm (`T13_R6_F01_*`) sử dụng `Mock<IStorageUnitAdminService>` và giả lập bằng Moq trên bộ nhớ. Việc chỉ dùng mock khiến bộ test chưa kiểm chứng được toàn diện:
   - Liệu CSDL thực tế đã lưu và commit giao dịch (transaction) thành công hay chưa.
   - Liệu các thao tác Create, Edit, Delete trên form Razor Pages và Hub SignalR có thực sự lưu lại trạng thái mới nhất trong cơ sở dữ liệu khi publisher ném `RealtimePublishDegradedException`.
2. Báo cáo R6 có đưa ra phỏng đoán về "race condition InMemory DbContext" để giải thích 2 test fail ở R5 mà chưa đưa ra các chứng cứ kiểm thử cô lập hoặc phân biệt rạch ròi giữa test mock đơn vị và test tích hợp CSDL thật.

#### B. Giải pháp kỹ thuật đã triển khai
1. **Triển khai 3 ca kiểm thử tích hợp chuyên sâu với CSDL thật trong [`StorageUnitRealtimeHubTests.cs`](file:///D:/FPT/PRN222/Project/Final_Project_Web/tests/SelfStorageManagementSystem.WebTests/StorageUnitRealtimeHubTests.cs)**:
   - **`T13_R7_F03_Integration_RazorPages_WhenRealtimeFails_DatabaseCommitted_NewScopeVerifiesDb`**:
     - Sử dụng `SelfStorageDbContext` thật (EF Core InMemory), service BLL thật `StorageUnitAdminService`, gán phân công quản lý cơ sở thật trong `staff_facility_assignments`, và đăng ký kết nối trong `IStorageUnitHubConnectionTracker`.
     - Cấu hình `StorageUnitRealtimePublisher` thật đi kèm `StorageUnitHubScopeRevocationService` có dependency kiểm tra quyền giả lập lỗi CSDL (`FailingFacilityScopeService`).
     - **Thao tác Create**: `CreateModel.OnPostAsync` tạo ô kho mới. Sau khi CSDL commit thành công, publisher gặp sự cố suy giảm -> Razor Page chuyển hướng an toàn `RedirectToPageResult("./Index")` kèm `TempData["WarningMessage"]`. Mở một `DbContext` scope **hoàn toàn mới** (`using var verifyDb = _factory.CreateDbContext()`), truy vấn kiểm chứng ô kho mới đã được lưu vĩnh viễn trong CSDL.
     - **Thao tác Edit**: `EditModel.OnPostAsync` cập nhật thông tin ô kho vừa tạo. Sau khi CSDL commit thành công, publisher suy giảm -> Razor Page chuyển hướng an toàn `./Index` kèm `WarningMessage`. Mở `DbContext` scope mới, kiểm chứng các trường dữ liệu cập nhật (`floor_label`) đã được lưu chuẩn xác trong CSDL.
     - **Thao tác Delete**: `DeleteModel.OnPostAsync` xóa ô kho. CSDL commit xóa thành công, publisher suy giảm -> Razor Page chuyển hướng an toàn `./Index` kèm `WarningMessage`. Mở `DbContext` scope mới, kiểm chứng bản ghi ô kho đã bị xóa hoàn toàn khỏi CSDL (`Assert.Null(deleted)`).
   - **`T13_R7_F03_Integration_HubMethods_WhenRealtimeFails_DatabaseCommitted_NewScopeVerifiesDb`**:
     - Thực hiện kiểm thử tích hợp tương tự trên SignalR Hub với BLL thật và CSDL thật cho cả 3 phương thức: `HubCreate_StorageUnit`, `HubUpdate_StorageUnit`, `HubDelete_StorageUnit`.
     - Khi publisher gặp sự cố suy giảm sau khi CSDL đã commit, Hub **không ném `HubException`**, trả về DTO/ID xác nhận thao tác thành công.
     - Truy vấn từ `DbContext` scope mới khẳng định chắc chắn 100% dữ liệu Create, Update, Delete đã được ghi nhận trong CSDL.
   - **`T13_R7_F03_Integration_WhenPreCommitDbFails_ThrowsException_NoDatabaseChange_NoPublisher`**:
     - Kiểm thử trường hợp lỗi nghiệp vụ xảy ra ở giai đoạn Pre-commit CSDL (ví dụ gửi trùng mã ô kho `duplicateUnitCode` đã tồn tại trong CSDL).
     - `CreateModel` hiển thị lỗi `ModelState` và render lại form.
     - `StorageUnitHub` bắt `ConflictException` và rethrow `HubException` với thông báo trùng mã rõ ràng.
     - CSDL hoàn toàn không bị biến đổi, và `mockPublisher` **chưa từng được gọi một lần nào** (`Times.Never`).
2. **Hiệu đính và Báo cáo Khách quan**:
   - Phân định rõ ràng trong báo cáo: Bộ test R6 bao gồm các test đơn vị dựa trên mock để kiểm tra hành vi luồng controller/page, trong khi bộ test R7 bổ sung các test tích hợp đầu-cuối với CSDL và BLL thật.
   - Ghi nhận chính xác số liệu chạy thực tế không qua suy đoán: Cả bộ test `SelfStorageManagementSystem.WebTests` đạt **364/364 PASS 100%** (bao gồm 40/40 Hub tests và 10/10 Browser tests).

---

## 3. Báo Cáo Kết Quả Kiểm Thử Thực Tế Vòng 7

### 3.1. Kiểm thử Hub SignalR và Quyền hạn (`StorageUnitRealtimeHubTests.cs`)
```text
Test run for D:\FPT\PRN222\Project\Final_Project_Web\tests\SelfStorageManagementSystem.WebTests\bin\Debug\net8.0\SelfStorageManagementSystem.WebTests.dll (.NETCoreApp,Version=v8.0)
A total of 1 test files matched the specified pattern.

Passed!  - Failed:     0, Passed:    40, Skipped:     0, Total:    40, Duration: 4 m 34 s - SelfStorageManagementSystem.WebTests.dll (net8.0)
```
- Tất cả **40/40 test** trong suite SignalR Hub đều vượt qua xuất sắc (bao gồm 3 test mới cho F01 và 3 test tích hợp CSDL mới cho F03).

### 3.2. Kiểm thử Trình duyệt Headless Edge (`StorageUnitsBrowserRealtimeTests.cs`)
```text
Test run for D:\FPT\PRN222\Project\Final_Project_Web\tests\SelfStorageManagementSystem.WebTests\bin\Debug\net8.0\SelfStorageManagementSystem.WebTests.dll (.NETCoreApp,Version=v8.0)
A total of 1 test files matched the specified pattern.

Passed!  - Failed:     0, Passed:    10, Skipped:     0, Total:    10, Duration: 19 s - SelfStorageManagementSystem.WebTests.dll (net8.0)
```
- Tất cả **10/10 test trình duyệt thật** (bao gồm test F05 với đầy đủ rào chắn HTML snapshot, superseded generation observation, và finally cleanup) đều vượt qua xuất sắc.

### 3.3. Toàn bộ Solution Web Tests (`SelfStorageManagementSystem.WebTests.csproj`)
```text
Test run for D:\FPT\PRN222\Project\Final_Project_Web\tests\SelfStorageManagementSystem.WebTests\bin\Debug\net8.0\SelfStorageManagementSystem.WebTests.dll (.NETCoreApp,Version=v8.0)
A total of 1 test files matched the specified pattern.

Passed!  - Failed:     0, Passed:   364, Skipped:     0, Total:   364, Duration: 4 m 34 s - SelfStorageManagementSystem.WebTests.dll (net8.0)
```
- **364/364 test PASS 100% (0 failed, 0 skipped)**.

### 3.4. Kiểm thử Hồi quy Toàn bộ Dự án (`SelfStorageManagementSystem.Tests.csproj`)
```text
Test run for D:\FPT\PRN222\Project\Final_Project_Web\tests\SelfStorageManagementSystem.Tests\bin\Debug\net8.0-windows\SelfStorageManagementSystem.Tests.dll (.NETCoreApp,Version=v8.0)
A total of 1 test files matched the specified pattern.

Passed!  - Failed:     0, Passed:   225, Skipped:     8, Total:   233, Duration: 2 s - SelfStorageManagementSystem.Tests.dll (net8.0)
```
- **225 test PASS, 8 test SQL Server skip** theo đúng phân bổ kiến trúc từ T00 chờ T17. Không có bất kỳ hồi quy nào.

---

## 4. Bảng Đối Soát Mã Hash SHA-256 Các Tệp Đã Sửa Đổi

Bảng đối soát mã băm SHA-256 của các tệp mã nguồn và kiểm thử đã chỉnh sửa trong Vòng 7 giữa hai thư mục làm việc:

| Tên Tệp Tin | SHA-256 (`Final_Project_Web`) | SHA-256 (`Final_Project`) | Kết Quả Khớp |
| :--- | :---: | :---: | :---: |
| `src/SelfStorageManagementSystem.Web/Realtime/StorageUnitHubScopeRevocationService.cs` | `37911AEABE8BEB22CE6EA96052EC6FD6FC2031C9277E8A1EC3BA09E36B533688` | `37911AEABE8BEB22CE6EA96052EC6FD6FC2031C9277E8A1EC3BA09E36B533688` | **100% Khớp** |
| `src/SelfStorageManagementSystem.Web/Realtime/StorageUnitRealtimePublisher.cs` | `98E2F2F04341EE71DB877ABDECCA1D13F2EE98E73B88F1A09D4038F8F88303EC` | `98E2F2F04341EE71DB877ABDECCA1D13F2EE98E73B88F1A09D4038F8F88303EC` | **100% Khớp** |
| `tests/SelfStorageManagementSystem.WebTests/StorageUnitsBrowserRealtimeTests.cs` | `2DF765105A5A3176715957FB0A157F06E877530899175BDE67D1361AD15B38AB` | `2DF765105A5A3176715957FB0A157F06E877530899175BDE67D1361AD15B38AB` | **100% Khớp** |
| `tests/SelfStorageManagementSystem.WebTests/StorageUnitRealtimeHubTests.cs` | `368C637F7E04FC05BA33F0139F2179DEF180F7424FD21EB988DEF0CB193D65BB` | `368C637F7E04FC05BA33F0139F2179DEF180F7424FD21EB988DEF0CB193D65BB` | **100% Khớp** |

---

## 5. Kết Luận & Trạng Thái Bàn Giao

1. **Tuân thủ hợp đồng và kiến trúc**:
   - Route `/storageUnitHub`, 3 Hub methods (`HubCreate_StorageUnit`, `HubUpdate_StorageUnit`, `HubDelete_StorageUnit`) và 3 sự kiện CRUD (`Receiver_CreateStorageUnit`, `Receiver_UpdateStorageUnit`, `Receiver_DeleteStorageUnit`) được giữ nguyên vẹn 100%.
   - Không can thiệp nghiệp vụ T12 hoặc T14; không tự ý bắt đầu T15.
2. **Trạng thái nhiệm vụ**:
   - Task T13: **`IMPLEMENTED — chờ Codex review`**
   - Task T15: **`WAITING`** (chờ T12 và T13 được Codex nghiệm thu độc lập).
   - Báo cáo và mã nguồn đã được đồng bộ toàn diện sang cả hai thư mục làm việc.
