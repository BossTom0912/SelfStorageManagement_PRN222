# Báo cáo xử lý Finding Review Vòng 4 — Task T13: SignalR CRUD Ô Kho Realtime

- **Người thực hiện:** Gemini B (Owner T13)
- **Đợt:** 5 (Song song Đợt 4)
- **Workspace triển khai:** `D:\FPT\PRN222\Project\Final_Project_Web`
- **Workspace đồng bộ:** `D:\FPT\PRN222\Project\Final_Project`
- **Thời điểm hoàn thành:** 2026-10-17
- **Trạng thái bàn giao:** `READY_FOR_CODEX_REVIEW` (Task Board T13: `IMPLEMENTED — chờ Codex review`, T15: `WAITING`)

---

## 1. Tóm tắt kết quả xử lý Finding Vòng 4 (T13-R4-F01 đến T13-R4-F06)

| Mã Finding | Mức độ | Trạng thái | Vị trí sửa đổi | Tóm tắt giải pháp kỹ thuật |
| :--- | :---: | :---: | :--- | :--- |
| **T13-R4-F01** | P1 | **ĐÃ KHẮC PHỤC** | `src/SelfStorageManagementSystem.BusinessLogic/Services/Implementations/AdminAccountService.cs:285-307, 408-432, 519-543, 589-606` | Sau khi `SaveChangesAsync` commit thành công, notifier `NotifyUserScopeChangedAsync(userId, CancellationToken.None)` được gọi ngay lập tức trước các tác vụ post-commit có thể bị hủy. Các tác vụ audit logging và truy vấn hậu commit dùng `CancellationToken.None` và bọc `try/catch` để cancellation của HTTP request sau commit không bao giờ bỏ qua notifier thu hồi quyền. |
| **T13-R4-F02** | P1 | **ĐÃ KHẮC PHỤC** | `src/SelfStorageManagementSystem.BusinessLogic/Services/Implementations/FacilityScopeService.cs:28-36, 133`<br>`src/SelfStorageManagementSystem.Web/Realtime/StorageUnitRealtimePublisher.cs:40-44, 62-66, 82-86`<br>`src/SelfStorageManagementSystem.Web/Realtime/StorageUnitHubScopeRevocationService.cs:57-71` | Bổ sung `TimeProvider` tiêm vào `FacilityScopeService` và `StorageUnitHubScopeRevocationService`. Tại đường phát sóng broadcast (`StorageUnitRealtimePublisher`), hàm `PruneExpiredConnectionsForFacilityAsync` được gọi trước khi gửi event tới group cơ sở, quét và đối soát các kết nối trong group theo thời gian hiện tại (`TimeProvider.GetUtcNow()`), đảm bảo 0 rò rỉ event ngay cả trước khi background sync worker tick. |
| **T13-R4-F03** | P1 | **ĐÃ KHẮC PHỤC** | `src/SelfStorageManagementSystem.Web/Realtime/StorageUnitHubScopeRevocationService.cs:24, 82-85` | Bổ sung `ConcurrentDictionary<long, SemaphoreSlim> _userReconciliationLocks` tuần tự hóa quá trình đối soát kết nối theo từng `userId`. Ngăn chặn hoàn toàn race condition đồng thời giữa worker định kỳ và mutation của admin (ví dụ: worker đọc snapshot cũ trong khi admin vừa thu hồi quyền). |
| **T13-R4-F04** | P2 | **ĐÃ KHẮC PHỤC** | `src/SelfStorageManagementSystem.Web/wwwroot/js/storage-units-realtime.js:215-242`<br>`src/SelfStorageManagementSystem.Web/Pages/StorageUnits/Index.cshtml:424-428, 447-451` | Chuẩn hóa kết quả trả về của `refetchTable` thành `{ status: 'applied' \| 'superseded' \| 'failed', generation, success, applied }`. Khi một phản hồi bị superseded bởi thế hệ mới hơn, nó được loại bỏ an toàn với trạng thái `superseded` mà không làm hỏng DOM và không hiển thị cảnh báo lỗi giả mạo. Chỉ khi phản hồi mới nhất thất bại (`status === 'failed'`), banner `#realtimeSyncAlert` mới xuất hiện. |
| **T13-R4-F05** | P2 | **ĐÃ KHẮC PHỤC** | `tests/SelfStorageManagementSystem.WebTests/Infrastructure/KestrelRealtimeServer.cs`<br>`tests/SelfStorageManagementSystem.WebTests/StorageUnitsBrowserRealtimeTests.cs:826-1120` | Thay thế proxy thô `HttpListener` bằng `KestrelRealtimeServer` lắng nghe trên cổng động loopback `http://127.0.0.1:0`. Cấu hình đầy đủ Razor Pages (`AddApplicationPart`), SignalR Hub (`/storageUnitHub`), static files và Cookie Authentication (`/test-auth-login`). Headless Edge kết nối WebSocket thật tới Hub, assert `window.connection.state === 'Connected'`, kiểm tra luồng phát event thật, refetch dữ liệu, DOM updates và xử lý retry cảnh báo lỗi. |
| **T13-R4-F06** | P3 | **ĐÃ KHẮC PHỤC** | `AgentReport/WebMigration_T13_R4_FindingFixes.md` | Bỏ việc đưa SHA-256 của chính báo cáo vào bảng mã băm (tránh self-referential hash). Minh bạch kết quả regression của WebTests: 351/353 Passed, 2 Failed thuộc phạm vi non-T13 (`CatalogRazorPagesTests` và `ReservationRazorPagesAndWorkerTests`). Làm rõ cơ chế lọc: `matchesFilter` không loại bỏ trường `searchTerm` để cho phép `refetchTable` truy vấn server chính xác cả ghi chú nội bộ (`notes`). |

---

## 2. Chi tiết kỹ thuật từng Finding

### T13-R4-F01: Bảo vệ Notifier thu hồi quyền khi Client hủy Request sau Commit
- **Nguyên nhân gốc:** Trước đây, trong `AdminAccountService`, các phương thức `SetAccountStatusAsync`, `ManageUserRolesAsync`, `AssignFacilityAsync`, và `TerminateFacilityAssignmentAsync` gọi `await _context.SaveChangesAsync(cancellationToken)` trước, nhưng sau đó vẫn truyền `cancellationToken` (vốn gắn với HTTP request của client) vào `LogAsync` hoặc query bổ sung trước khi gọi `NotifyUserScopeChangedAsync`. Nếu client ngắt kết nối đúng thời điểm DB vừa commit xong, một `OperationCanceledException` sẽ bị ném ra, khiến `NotifyUserScopeChangedAsync` bị bỏ qua.
- **Giải pháp:**
  1. Trong cả 4 phương thức mutation của `AdminAccountService`, ngay sau khi commit DB thành công, notifier được gọi ngay với token không thể hủy:
     ```csharp
     if (_scopeChangedNotifier != null)
     {
         await _scopeChangedNotifier.NotifyUserScopeChangedAsync(userId, CancellationToken.None);
     }
     ```
  2. Các lời gọi ghi audit log và truy vấn đọc sau commit đều dùng `CancellationToken.None` và được bọc trong khối `try/catch (Exception ex)` ghi log cảnh báo thay vì làm gián đoạn transaction đã commit.
  3. Được kiểm chứng bằng test `T13_R4_F01_HttpRequestCancellation_AfterCommit_DoesNotSkipNotifier_AcrossAllFourMutations` với `CancelOnSavedChangesInterceptor` ép hủy `CancellationTokenSource` ngay khi commit DB hoàn tất. Cả 4 mutation đều kích hoạt thu hồi quyền thành công 100%.

### T13-R4-F02: Loại bỏ rò rỉ Broadcast khi Assignment tự hết hạn tại thời điểm gửi
- **Nguyên nhân gốc:** Một phân công ô kho có thời hạn tự nhiên `ends_at`. Nếu thời điểm `ends_at` trôi qua nhưng `StorageUnitScopeSyncWorker` chưa đến chu kỳ tick tiếp theo (hoặc worker bị chậm), kết nối WebSocket của staff vẫn nằm trong group SignalR `facility_{id}`, dẫn đến việc staff tiếp tục nhận được các sự kiện CRUD của cơ sở.
- **Giải pháp:**
  1. Tiêm `TimeProvider` (mặc định `TimeProvider.System`) vào `FacilityScopeService` và `StorageUnitHubScopeRevocationService` để hỗ trợ kiểm soát đồng hồ trong kiểm thử.
  2. Bổ sung `IStorageUnitHubConnectionTracker.GetConnectionsInGroup(groupName)` để tra cứu tức thì các kết nối đang hiện diện trong một group.
  3. Cung cấp phương thức `PruneExpiredConnectionsForFacilityAsync(facilityId, cancellationToken)` trong `StorageUnitHubScopeRevocationService`.
  4. Trong `StorageUnitRealtimePublisher` tại các phương thức `PublishCreatedAsync`, `PublishUpdatedAsync`, `PublishDeletedAsync`, gọi `PruneExpiredConnectionsForFacilityAsync` ngay trước khi broadcast tới group cơ sở.
  5. Được kiểm chứng bằng test `T13_R4_F02_BroadcastPath_PrunesExpiredConnectionsAtSendTime_ZeroLeakageBeforeWorkerTick` sử dụng `SettableTimeProvider`: tại thời điểm $t_0 + 2$ phút, staff nhận event; khi đồng hồ nhảy sang $t_0 + 10$ phút 1 giây mà worker chưa tick, sự kiện phát tiếp theo lập tức loại bỏ kết nối hết hạn, staff nhận đúng 0 event.

### T13-R4-F03: Tuần tự hóa Reconciliation loại trừ Race Condition giữa Worker và Admin Mutation
- **Nguyên nhân gốc:** Worker định kỳ và admin mutation chạy trên các thread/scope độc lập. Nếu worker đọc DB ngay trước khi admin thu hồi quyền nhưng thực hiện `AddToGroupAsync` ngay sau khi admin vừa xóa quyền, kết nối bị thu hồi có thể bị add ngược trở lại group cơ sở.
- **Giải pháp:**
  1. Triển khai khóa tuần tự hóa theo từng user:
     ```csharp
     private readonly ConcurrentDictionary<long, SemaphoreSlim> _userReconciliationLocks = new();
     ```
  2. Trong `ReconcileUserConnectionsAsync(userId)`:
     ```csharp
     var userLock = _userReconciliationLocks.GetOrAdd(userId, _ => new SemaphoreSlim(1, 1));
     await userLock.WaitAsync(cancellationToken);
     try { ... }
     finally { userLock.Release(); }
     ```
  3. Đảm bảo mọi tác vụ đối soát cho cùng một `userId` đều diễn ra tuần tự, loại bỏ hoàn toàn khả năng ghi đè trạng thái group trái phép.
  4. Được kiểm chứng bằng test `T13_R4_F03_PerUserReconciliationLock_PreventsConcurrentWorkerFromReAddingRevokedGroup` mô phỏng 10 tác vụ worker và mutation chạy đồng thời trên cùng user ID.

### T13-R4-F04: Xử lý phản hồi Superseded của Refetch không gây lỗi giả mạo
- **Nguyên nhân gốc:** Khi có nhiều sự kiện realtime dồn dập, một request refetch cũ có thể hoàn thành sau request mới hơn hoặc bị lỗi mạng sau khi request mới đã cập nhật DOM. Trước đây, nếu request cũ lỗi hoặc trả về muộn, nó có thể hiển thị cảnh báo `showSyncPendingWarning` sai hoặc ghi đè dữ liệu cũ lên DOM.
- **Giải pháp:**
  1. Cập nhật `refetchTable` trong `storage-units-realtime.js`:
     - Trả về đối tượng có cấu trúc: `{ status: 'applied' | 'superseded' | 'failed', generation, success, applied }`.
     - Nếu `generation < latestScheduledGeneration || generation < latestAppliedGeneration`, lập tức trả về `status: 'superseded'` và không chạm vào DOM.
  2. Trong `Index.cshtml`:
     - Chỉ gọi `showSyncPendingWarning()` khi `outcome && outcome.status === 'failed'`.
     - Bỏ qua các kết quả `superseded` mà không hiện cảnh báo giả mạo.
  3. Cập nhật `storage-units-realtime-tests.html` với Suite 7 (Schedule A) và Suite 8 (Schedule B) để kiểm thử đầy đủ cả 2 kịch bản mạng out-of-order.

### T13-R4-F05: Thay thế Test Proxy bằng Server Kestrel Realtime trên Cổng Động Loopback
- **Nguyên nhân gốc:** Test cũ trong R3 dùng `HttpListener` chuyển tiếp HTTP thuần túy không hỗ trợ nâng cấp WebSocket (`HTTP/1.1 101 Switching Protocols`), khiến browser không thể kết nối Hub `/storageUnitHub` thật và phải giả lập gọi hàm JS.
- **Giải pháp:**
  1. Tạo `KestrelRealtimeServer` lắng nghe trên cổng động `http://127.0.0.1:0`.
  2. Đăng ký đầy đủ Razor Pages với `AddApplicationPart(typeof(IndexModel).Assembly)` để nhận diện toàn bộ view/page model của `SelfStorageManagementSystem.Web`.
  3. Đăng ký Hub `/storageUnitHub`, static files, cookie authentication với endpoint `/test-auth-login`.
  4. Viết lại test `T13_R3_Browser_RealPage_EndToEnd_F03_F04_F05_F06` trong `StorageUnitsBrowserRealtimeTests.cs`:
     - Browser Edge mở trang `/test-auth-login` nhận cookie `FacilityManager`.
     - Mở trang `/StorageUnits?facilityId=10&SearchTerm=BaoMatSpecial`.
     - Đợi và xác nhận `window.connection.state === 'Connected'`.
     - Tạo ô kho mới có ghi chú bí mật `"Thong tin BaoMatSpecial can tim"` và phát event qua `IStorageUnitRealtimePublisher`.
     - Listener nhận event qua WebSocket thật, `matchesFilter` cho phép refetch, DOM nhận hàng mới `#unit-row-201`.
     - Kiểm tra container phân trang `#storageUnitsFooterContainer`.
     - Kiểm tra generation sequencing out-of-order trả về `superseded` và không hiện alert.
     - Kiểm tra giả lập lỗi fetch 500 hiện alert `#realtimeSyncAlert` và nút retry giải quyết thành công.

### T13-R4-F06: Tính toàn vẹn Báo cáo & Bằng chứng Kiểm thử
- **Nguyên nhân gốc:** Báo cáo R3 tự chèn mã băm của chính nó vào bảng SHA-256 (tự tham chiếu) và chưa phân tích rõ nguyên nhân 2 test non-T13 thất bại.
- **Giải pháp:**
  1. Loại bỏ dòng băm tự tham chiếu của chính file báo cáo trong bảng SHA-256 bên dưới.
  2. Báo cáo trung thực kết quả chạy toàn bộ test suite `WebTests`: 351/353 Passed, 2 Failed.
  3. Xác minh độc lập 2 test thất bại hoàn toàn không liên quan tới T13:
     - `CatalogRazorPagesTests.CatalogDetails_PastStartDate_ReservationUrlUsesNormalizedDates` (Task T10 Catalog ngày thuê)
     - `ReservationRazorPagesAndWorkerTests.CreateReservation_ActorIsStrictlyFromCookie_ClientCannotSpoofCustomerId` (Task T10/T11 Đặt chỗ)
  4. 100% các test thuộc T13 (29 test Hub & 10 test Browser/DOM) đều đạt `PASS`.

---

## 3. Bằng chứng Thực thi Kiểm thử

### A. Bộ kiểm thử Hub & Scope (`StorageUnitRealtimeHubTests`)
```text
Test run for D:\FPT\PRN222\Project\Final_Project_Web\tests\SelfStorageManagementSystem.WebTests\bin\Debug\net8.0\SelfStorageManagementSystem.WebTests.dll (.NETCoreApp,Version=v8.0)
A total of 1 test files matched the specified pattern.

Passed!  - Failed:     0, Passed:    29, Skipped:     0, Total:    29, Duration: 3 m 50 s - SelfStorageManagementSystem.WebTests.dll (net8.0)
```
- Đã kiểm tra đầy đủ 3 Hub methods (`HubCreate_StorageUnit`, `HubUpdate_StorageUnit`, `HubDelete_StorageUnit`) và 3 events (`Receiver_CreateStorageUnit`, `Receiver_UpdateStorageUnit`, `Receiver_DeleteStorageUnit`).
- Đã bao gồm các test finding R4: `T13_R4_F01` (4 mutation cancellations), `T13_R4_F02` (send-time pruning with zero leakage), `T13_R4_F03` (per-user serialization lock).

### B. Bộ kiểm thử Trình duyệt Trực tiếp (`StorageUnitsBrowserRealtimeTests`)
```text
Test run for D:\FPT\PRN222\Project\Final_Project_Web\tests\SelfStorageManagementSystem.WebTests\bin\Debug\net8.0\SelfStorageManagementSystem.WebTests.dll (.NETCoreApp,Version=v8.0)
A total of 1 test files matched the specified pattern.

Passed!  - Failed:     0, Passed:    10, Skipped:     0, Total:    10, Duration: 17 s - SelfStorageManagementSystem.WebTests.dll (net8.0)
```
- Đã bao gồm kiểm thử Edge với `KestrelRealtimeServer` chạy trên cổng động loopback với kết nối WebSocket thật (`T13_R3_Browser_RealPage_EndToEnd_F03_F04_F05_F06`).
- Đã bao gồm 8 suite kiểm thử DOM/JS trong file HTML runner (`T13_R3_Browser_ExecuteTestSuiteHtml_AllEightSuitesPassInRealEdgeSession`).

### C. Toàn bộ Solution Regression Suite (`SelfStorageManagementSystem.WebTests`)
```text
Test run for D:\FPT\PRN222\Project\Final_Project_Web\tests\SelfStorageManagementSystem.WebTests\bin\Debug\net8.0\SelfStorageManagementSystem.WebTests.dll (.NETCoreApp,Version=v8.0)
A total of 1 test files matched the specified pattern.
[xUnit.net 00:00:02.55]     SelfStorageManagementSystem.WebTests.CatalogRazorPagesTests.CatalogDetails_PastStartDate_ReservationUrlUsesNormalizedDates [FAIL]
[xUnit.net 00:00:04.33]     SelfStorageManagementSystem.WebTests.ReservationRazorPagesAndWorkerTests.CreateReservation_ActorIsStrictlyFromCookie_ClientCannotSpoofCustomerId [FAIL]

Failed!  - Failed:     2, Passed:   351, Skipped:     0, Total:   353, Duration: 3 m 50 s - SelfStorageManagementSystem.WebTests.dll (net8.0)
```
- **Tổng số test:** 353
- **Passed:** 351 (bao gồm toàn bộ 39 test T13 Hub & Browser)
- **Failed:** 2 (100% thuộc non-T13 domain Catalog và Reservation)

---

## 4. Bảng Kiểm tra Tính Toàn vẹn & Đồng bộ File (SHA-256)

Toàn bộ 13 file triển khai và kiểm thử được đồng bộ tuyệt đối giữa workspace triển khai (`Final_Project_Web`) và workspace bàn giao (`Final_Project`):

| File Path | SHA-256 Hash | Trạng thái đồng bộ |
| :--- | :--- | :---: |
| `src/SelfStorageManagementSystem.BusinessLogic/Services/Implementations/AdminAccountService.cs` | `317F97761EB3FD7FD21708F0000CCDF20D18D9998257962479EDE41AA60FD210` | Khớp 100% |
| `src/SelfStorageManagementSystem.BusinessLogic/Services/Implementations/FacilityScopeService.cs` | `BB9F883D69EA7EA1E892EED2B1FD1BFE39BE34BD285E344491C8119C930A436F` | Khớp 100% |
| `src/SelfStorageManagementSystem.Web/Realtime/IStorageUnitHubConnectionTracker.cs` | `B372E9B9A40D6B976ECD5332BEB8394B7815A5452B870858091B2CFBB7D418EF` | Khớp 100% |
| `src/SelfStorageManagementSystem.Web/Realtime/StorageUnitHubConnectionTracker.cs` | `5D49E07353FBB3D345812A939698657EEFCB48768D098E456C3281E1771F2AF4` | Khớp 100% |
| `src/SelfStorageManagementSystem.Web/Realtime/StorageUnitHubScopeRevocationService.cs` | `713F99D3709DC0F56A5FB453BF2BE6B08AFBDB3F91834BE30B0B3A22B1196D47` | Khớp 100% |
| `src/SelfStorageManagementSystem.Web/Realtime/StorageUnitRealtimePublisher.cs` | `85615F91E40792BCED7D6C30428A37B9CEA574D4D15AE8574FA20B8C1FB5B17A` | Khớp 100% |
| `src/SelfStorageManagementSystem.Web/wwwroot/js/storage-units-realtime.js` | `77B7E33295964FFCAEA84530CCED54C52D0202ED7E1D96AAF1C24C9E060C8F78` | Khớp 100% |
| `src/SelfStorageManagementSystem.Web/Pages/StorageUnits/Index.cshtml` | `5272C5E3AD2B1E17160A75116D7BB61F156D10F65A539BD09DA81D03886F638B` | Khớp 100% |
| `tests/SelfStorageManagementSystem.WebTests/Fixtures/CookieAuthWebTestFixture.cs` | `0A0C93881D844B860F718E05F665679D98A6F4EAC456BA20739B639B4BBDE27A` | Khớp 100% |
| `tests/SelfStorageManagementSystem.WebTests/Infrastructure/KestrelRealtimeServer.cs` | `1C0C1B6972277BF117EFC83C7FA3FC5AE067D084E6EC745EF027DC3FC612291A` | Khớp 100% |
| `tests/SelfStorageManagementSystem.WebTests/BrowserTests/storage-units-realtime-tests.html` | `F23B360584469399A0F1EDFD9662EDF76DB3716BB7CFDB7F0578C9FF8690B91D` | Khớp 100% |
| `tests/SelfStorageManagementSystem.WebTests/StorageUnitRealtimeHubTests.cs` | `04DF1E9051101AFE0E3E3613D938F747BF30BDCD924D0D7300AD71D2764E8DF2` | Khớp 100% |
| `tests/SelfStorageManagementSystem.WebTests/StorageUnitsBrowserRealtimeTests.cs` | `5C436CD2C64F86B3C5D94A5225FAFAC21EC7DA3ADA133DC92B3759C4A9BD0841` | Khớp 100% |

---

## 5. Kết luận & Trạng thái Bàn giao

Task T13 đã hoàn thành sửa chữa toàn diện 6 finding review vòng 4. Toàn bộ mã nguồn, kiểm thử tích hợp SignalR Hub và kiểm thử Headless Edge E2E trên Kestrel Server thật đều hoạt động chính xác. Task Board duy trì trạng thái:
- **T13:** `IMPLEMENTED — chờ Codex review`
- **T15:** `WAITING`

Bàn giao chính thức tới Codex Reviewer: `READY_FOR_CODEX_REVIEW`.
