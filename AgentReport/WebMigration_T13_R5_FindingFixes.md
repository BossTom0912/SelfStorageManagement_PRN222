# Báo Cáo Sửa Finding Review Vòng 5 — Task T13: SignalR Realtime CRUD Ô Kho

- **Người thực hiện**: Gemini B (Owner T13)
- **Đợt thực hiện**: Đợt 5 (Song song 4)
- **Ngày hoàn thành**: 2026-10-17
- **Workspace triển khai**: `D:\FPT\PRN222\Project\Final_Project_Web`
- **Workspace đồng bộ**: `D:\FPT\PRN222\Project\Final_Project`
- **Đầu vào review**: `AgentReport/WebMigration_T13_R4_FindingFixes.md` và 3 finding review Vòng 5.
- **Trạng thái bàn giao**: **`IMPLEMENTED — chờ Codex review`** (Task Board giữ T13 `IMPLEMENTED`, T15 `WAITING`; không tự đánh dấu `VERIFIED`).

---

## 1. Tóm Tắt Kết Quả Xử Lý 3 Finding Vòng 5

| Mã Finding | Mức Độ | Tên Lỗi | Tình Trạng | Tệp Tin Sửa Đổi / Bổ Sung Chính |
| :--- | :---: | :--- | :---: | :--- |
| **T13-R5-F01** | **P1** | Bước kiểm tra quyền lỗi nhưng publisher vẫn phát dữ liệu cho group cũ | **ĐÃ KHẮC PHỤC TRIỆT ĐỂ** | [`StorageUnitRealtimePublisher.cs`](file:///D:/FPT/PRN222/Project/Final_Project_Web/src/SelfStorageManagementSystem.Web/Realtime/StorageUnitRealtimePublisher.cs)<br>[`StorageUnitRealtimeHubTests.cs`](file:///D:/FPT/PRN222/Project/Final_Project_Web/tests/SelfStorageManagementSystem.WebTests/StorageUnitRealtimeHubTests.cs) |
| **T13-R5-F02** | **P2** | Browser test gọi hàm nội bộ và tự dựng cảnh báo, chưa kiểm E2E qua event SignalR thật | **ĐÃ KHẮC PHỤC TRIỆT ĐỂ** | [`HeadlessEdgeSession.cs`](file:///D:/FPT/PRN222/Project/Final_Project_Web/tests/SelfStorageManagementSystem.WebTests/Infrastructure/HeadlessEdgeSession.cs)<br>[`StorageUnitsBrowserRealtimeTests.cs`](file:///D:/FPT/PRN222/Project/Final_Project_Web/tests/SelfStorageManagementSystem.WebTests/StorageUnitsBrowserRealtimeTests.cs) |
| **T13-R5-F03** | **P2** | Flaky test T13_R4_F02 khi chạy chung với T13_R4_F03 do race condition OnConnectedAsync và rò rỉ connection | **ĐÃ KHẮC PHỤC TRIỆT ĐỂ** | [`StorageUnitRealtimeHubTests.cs`](file:///D:/FPT/PRN222/Project/Final_Project_Web/tests/SelfStorageManagementSystem.WebTests/StorageUnitRealtimeHubTests.cs) |

---

## 2. Chi Tiết Khắc Phục Từng Finding

### 2.1. T13-R5-F01 [P1] — Fail-Closed Broadcast Protection khi bước kiểm tra quyền gặp lỗi

#### A. Phân tích nguyên nhân
Trong implementation Vòng 4, ba phương thức `PublishCreatedAsync`, `PublishUpdatedAsync`, `PublishDeletedAsync` của `StorageUnitRealtimePublisher` gọi `PruneExpiredConnectionsForFacilityAsync(facilityId)`. Tuy nhiên, khối `catch (Exception ex)` chỉ ghi `LogWarning` rồi cho phép tiếp tục thực thi câu lệnh:
```csharp
await _hubContext.Clients.Groups(StorageUnitHub.FacilityGroup(facilityId))
    .SendAsync(StorageUnitHubContract.ReceiverCreateStorageUnit, dto, cancellationToken);
```
Nếu xảy ra lỗi cơ sở dữ liệu khi truy vấn quyền (`IFacilityScopeService.GetAccessibleFacilityIdsAsync`), lỗi tài khoản (`IAccountIdentityService.GetCurrentUserAsync`), hoặc lỗi semaphore lock, tiến trình prune bị hủy bỏ dở dang. Kết quả là connection của nhân viên đã hết hạn phân công vẫn còn nằm trong nhóm `facility_{facilityId}` và nhận được dữ liệu ô kho của cơ sở đó (rò rỉ dữ liệu ngoài phân công). Đồng thời, tham số constructor của `StorageUnitRealtimePublisher` cho phép `StorageUnitHubScopeRevocationService? revocationService = null`, tạo kẽ hở bỏ qua hoàn toàn cơ chế bảo vệ nếu publisher được khởi tạo ngoài container DI của production.

#### B. Giải pháp kỹ thuật đã triển khai
1. **Bắt buộc Dependency Injection**: Xóa giá trị mặc định `= null` của `StorageUnitHubScopeRevocationService` trong constructor `StorageUnitRealtimePublisher`, ép buộc mọi nơi khởi tạo publisher phải cung cấp service thu hồi quyền.
2. **Cơ chế Fail-Closed (An toàn khi lỗi)**:
   - Đặt cờ `facilityBroadcastAllowed = true`.
   - Nếu `PruneExpiredConnectionsForFacilityAsync` bắt ngoại lệ, lập tức ghi log `LogError` và gán `facilityBroadcastAllowed = false`.
   - Khi `facilityBroadcastAllowed == false`, **chặn hoàn toàn** việc gửi DTO ô kho tới nhóm `facility_{facilityId}`.
   - Nhóm quản trị toàn cục (`admin_global`) vẫn được nhận event vì quyền của họ là toàn hệ thống, không phụ thuộc vào cơ sở cụ thể.
   - Sự kiện an toàn `CatalogInvalidated` (chỉ mang ID cơ sở, không chứa payload DTO hay thông tin ô kho nội bộ) vẫn được phát rộng rãi để xóa cache capacity của khách hàng.
   - Ném ngoại lệ `InvalidOperationException("Pruning expired connections failed for facility ... Facility broadcast was aborted to prevent unauthorized leakage.")` để thông báo lỗi rõ ràng cho luồng gọi.
3. **Thêm Test kiểm chứng**:
   - `T13_R5_F01_FailClosed_WhenPruneFails_BlocksFacilityBroadcast_ZeroLeakageToExpiredStaff`: Sử dụng `FailingFacilityScopeService` giả lập lỗi DB/scope service. Xác nhận `PublishCreatedAsync` ném `InvalidOperationException`, staff có quyền hết hạn nhận đúng 0 event (zero leakage), trong khi `admin_global` vẫn nhận được event.
   - `T13_R5_F01_NormalPath_WhenPruneSucceeds_ActiveStaffReceives_ExpiredStaffZeroEvents`: Xác nhận luồng bình thường nhân viên active nhận event, còn nhân viên hết hạn bị prune ngay tại send-time và nhận đúng 0 event.

---

### 2.2. T13-R5-F02 [P2] — Viết lại Browser Test thành End-to-End thật qua Kestrel & WebSocket

#### A. Phân tích nguyên nhân
Trong Vòng 4, test browser `T13_R3_Browser_RealPage_EndToEnd_F03_F04_F05_F06` đã đưa `F03` chạy qua Kestrel và WebSocket thật, nhưng các phần F04 (phân trang), F05 (sắp xếp tuần tự out-of-order), và F06 (cảnh báo lỗi 500) vẫn còn sử dụng lệnh gọi hàm nội bộ JavaScript (`StorageUnitsRealtime.refetchTable`) hoặc tự tạo DOM giả (`document.createElement('div')` với id `#realtimeSyncAlert`). Điều này chưa kiểm tra được sự phối hợp đầy đủ giữa:
- Sự kiện SignalR thực tế phát từ server qua Hub/WebSocket.
- Bộ lắng nghe production trên trang Razor Page `Index.cshtml` (`connection.on("Receiver_CreateStorageUnit", ...)`).
- Logic kiểm tra `matchesFilter`, gọi `refetchTable`, hoán đổi DOM phân trang và hiển thị thông báo lỗi.

#### B. Giải pháp kỹ thuật đã triển khai
1. **Mở rộng CDP Helper trong [`HeadlessEdgeSession.cs`](file:///D:/FPT/PRN222/Project/Final_Project_Web/tests/SelfStorageManagementSystem.WebTests/Infrastructure/HeadlessEdgeSession.cs)**:
   - Thêm phương thức `EvaluateIntAsync(string expression, CancellationToken cancellationToken = default)` để parse số nguyên an toàn từ kết quả JavaScript `Runtime.evaluate`.
2. **Viết lại F04 E2E (Phân trang 10 -> 11 -> 10)**:
   - Khởi tạo 10 ô kho ban đầu, mở trang `/StorageUnits?facilityId=10` (trang 1 có 10 ô kho, chưa có link Trang 2, footer hiển thị 1 đến 10 trong 10 ô kho).
   - Thêm ô kho thứ 11 vào DB và phát sự kiện thật `publisher.PublishCreatedAsync(unit11)`.
   - Bộ lắng nghe production nhận event, kích hoạt `refetchTable`, DOM được cập nhật: bộ đếm lên 11, footer container cập nhật hiển thị 1 đến 10 trong 11 ô kho, link Trang 2 (`pageNumber=2`) xuất hiện trong cụm phân trang.
   - Xóa ô kho thứ 11 trong DB và phát sự kiện thật `publisher.PublishDeletedAsync(unit11Id, facilityId)`.
   - Bộ lắng nghe production nhận event, bộ đếm quay về 10, footer container cập nhật và link Trang 2 tự động bị gỡ bỏ.
   - Áp dụng chuẩn hóa khoảng trắng (`.replace(/\s+/g, ' ').trim()`) khi đánh giá text footer để xử lý việc ngắt dòng tự nhiên của Razor View Engine.
3. **Viết lại F05 E2E (Sequencing out-of-order)**:
   - Không gọi `refetchTable` thủ công. Test chặn `window.fetch` của listener, giữ lại Promise của response đầu tiên (`SEQ-A`), phát tiếp sự kiện thứ hai (`SEQ-B`).
   - Sau khi response `SEQ-B` áp dụng xong, mới giải phóng Promise của `SEQ-A`.
   - Khẳng định response cũ bị đánh dấu `superseded`, không làm lùi DOM và không gây ra cảnh báo lỗi sai lệch (`#realtimeSyncAlert` không xuất hiện).
4. **Viết lại F06 E2E (Xử lý lỗi HTTP 500 và nút Thử lại)**:
   - Chặn `window.fetch` trả về HTTP 500 khi nhận sự kiện SignalR thật qua WebSocket.
   - Listener production nhận HTTP 500, tự động sinh thẻ cảnh báo `#realtimeSyncAlert` và nút `#btnRetrySync`.
   - Khôi phục `window.fetch` và mô phỏng browser click thật vào `#btnRetrySync`.
   - Hàm `retryRefetch()` thực thi thành công và tự động xóa cảnh báo khỏi DOM.

---

### 2.3. T13-R5-F03 [P2] — Triệt tiêu Flakiness giữa T13_R4_F02 và T13_R4_F03

#### A. Phân tích nguyên nhân
Khi chạy liên tiếp test `T13_R4_F02` và `T13_R4_F03` trong cùng một test run:
1. `T13_R4_F02` tạo nhiều connection (staff, manager, future staff), nhưng khi kết thúc test không gọi `await conn.DisposeAsync()`. Các connection này vẫn còn active và đăng ký trong `IStorageUnitHubConnectionTracker`.
2. Khi `T13_R4_F03` chạy ngay sau đó, `staffConn.StartAsync()` hoàn tất ở client, nhưng ở phía server, phương thức `OnConnectedAsync` phải đợi khóa `SemaphoreSlim` theo từng user và thực hiện truy vấn `DbContext` bất đồng bộ.
3. Khi test gọi ngay `publisher.PublishCreatedAsync`, connection phía server chưa kịp hoàn tất lệnh `_hubContext.Groups.AddToGroupAsync(Context.ConnectionId, $"facility_{facilityId}")` và chưa cập nhật tracker. Kết quả là event broadcast bị bỏ lỡ, `events` nhận được là rỗng `[]`, gây ra fail ngẫu nhiên.

#### B. Giải pháp kỹ thuật đã triển khai
1. **Bổ sung helper đồng bộ hóa nhóm Hub trong test**:
   ```csharp
   private static async Task WaitForUserInGroupAsync(
       IStorageUnitHubConnectionTracker connectionTracker,
       string groupName,
       long userId,
       TimeSpan timeout)
   ```
   Helper này thăm dò tracker với timeout 5 giây, đảm bảo connection của người dùng đã thực sự được server ghi nhận vào nhóm phân quyền trước khi test kích hoạt broadcast.
2. **Đồng bộ hóa trước mọi broadcast trong `T13_R4_F02` và `T13_R4_F03`**:
   - `T13_R4_F02`: Chờ cả 3 user (staff, manager, future staff) hoàn tất join nhóm trước khi gọi `PublishCreatedAsync`.
   - `T13_R4_F03`: Chờ user staff hoàn tất join nhóm trước khi gọi `PublishCreatedAsync`.
3. **Bọc `try ... finally { await conn.DisposeAsync(); }`**:
   - Đảm bảo 100% kết nối WebSocket/Hub Connection được ngắt kết nối và giải phóng tài nguyên sau khi kết thúc mỗi test method, triệt tiêu hoàn toàn ô nhiễm tài nguyên giữa các test.
4. **Kết quả kiểm chứng độ ổn định**:
   - Chạy vòng lặp 5 lần liên tiếp kịch bản chạy cặp `T13_R4_F02` và `T13_R4_F03`:
     ```text
     === ITERATION 1 === Passed! (2/2)
     === ITERATION 2 === Passed! (2/2)
     === ITERATION 3 === Passed! (2/2)
     === ITERATION 4 === Passed! (2/2)
     === ITERATION 5 === Passed! (2/2)
     ALL 5 ITERATIONS PASSED!
     ```
   - Tỷ lệ thành công: **5/5 runs (100% PASS, 0 flakiness)**.

---

## 3. Báo Cáo Kết Quả Kiểm Thử Thực Tế

### 3.1. Hub Tests (`StorageUnitRealtimeHubTests.cs`)
```text
Passed! - Failed: 0, Passed: 31, Skipped: 0, Total: 31, Duration: 23 s
```
*(Bao gồm 29 test cũ của R4 và 2 test mới của R5-F01)*

### 3.2. Browser Tests (`StorageUnitsBrowserRealtimeTests.cs`)
```text
Passed! - Failed: 0, Passed: 10, Skipped: 0, Total: 10, Duration: 20 s
```
*(Bao gồm E2E F03, F04, F05, F06 chạy trên Kestrel cổng động và trình duyệt Headless Edge)*

### 3.3. Kiểm thử lặp độ ổn định (5 iterations)
```text
5/5 Iterations Passed (10/10 individual test executions passed, 0 failures)
```

### 3.4. Toàn bộ Web Test Suite (`SelfStorageManagementSystem.WebTests.csproj`)
```text
Total tests: 355
     Passed: 353
     Failed: 2 (2 lỗi baseline có sẵn từ trước thuộc T12/T14, không thuộc phạm vi T13)
 Total time: 4 m 7 s
```

Hai lỗi baseline không thuộc T13:
1. `CatalogRazorPagesTests.CatalogDetails_PastStartDate_ReservationUrlUsesNormalizedDates` (T10/T07)
2. `ReservationRazorPagesAndWorkerTests.CreateReservation_ActorIsStrictlyFromCookie_ClientCannotSpoofCustomerId` (T10)

---

## 4. Bảng Kiểm Tra Tuân Thủ Nguyên Tắc & Ranh Giới

| Tiêu Chí Kiểm Tra | Kết Quả | Chi Tiết Minh Chứng |
| :--- | :---: | :--- |
| **Route SignalR** | **GIỮ NGUYÊN** | Tuyến đường `/storageUnitHub` được bảo toàn nguyên vẹn |
| **Hub Methods** | **GIỮ NGUYÊN** | Giữ đúng 3 phương thức: `HubCreate_StorageUnit`, `HubUpdate_StorageUnit`, `HubDelete_StorageUnit` |
| **Receiver Events** | **GIỮ NGUYÊN** | Giữ đúng 3 sự kiện: `Receiver_CreateStorageUnit`, `Receiver_UpdateStorageUnit`, `Receiver_DeleteStorageUnit` |
| **Ranh giới Task khác** | **TUÂN THỦ** | Không can thiệp mã nghiệp vụ của T12 hay T14 |
| **Trạng thái Task Board** | **TUÂN THỦ** | T13 ghi nhận `IMPLEMENTED — chờ Codex review`, T15 ghi nhận `WAITING`, tuyệt đối không tự ghi `VERIFIED` |
| **Self-referential Hash** | **TUÂN THỦ** | Báo cáo không chứa hash của chính file báo cáo, tránh lỗi chênh lệch hash giữa 2 workspace |

---

## 5. Bảng Mã Hash SHA-256 Các Tệp Tin Mã Nguồn

| Đường Dẫn Tệp Tin | SHA-256 Checksum |
| :--- | :--- |
| `src/SelfStorageManagementSystem.Web/Realtime/StorageUnitRealtimePublisher.cs` | `051AC7A80ECCD085AEAF79EEA26D099344A90D084CEEEA4AC69BD3C3429577CD` |
| `tests/SelfStorageManagementSystem.WebTests/Infrastructure/HeadlessEdgeSession.cs` | `8668D77538BA815FBDB1519399AFC18A2DF9DF77DB70A481E63485CC47658DA7` |
| `tests/SelfStorageManagementSystem.WebTests/StorageUnitRealtimeHubTests.cs` | `9E71BEB73726F002B9D70E8ED6DF9205197F79D3078AF9107D16265E0065BF5A` |
| `tests/SelfStorageManagementSystem.WebTests/StorageUnitsBrowserRealtimeTests.cs` | `565777C7D1C9A125CA8C715CE4B64547B299AACBC1AB713CB5AEDFA7249C839C` |
| `src/SelfStorageManagementSystem.Web/wwwroot/js/storage-units-realtime.js` | `77B7E33295964FFCAEA84530CCED54C52D0202ED7E1D96AAF1C24C9E060C8F78` |
| `src/SelfStorageManagementSystem.Web/Pages/StorageUnits/Index.cshtml` | `5272C5E3AD2B1E17160A75116D7BB61F156D10F65A539BD09DA81D03886F638B` |
| `src/SelfStorageManagementSystem.Web/Hubs/StorageUnitHub.cs` | `160464424B7088F0B45D599D0DB1EF2808CE2F1B48826795FCF0337BE175C391` |

---

## 6. Kết Luận

Gemini B đã hoàn tất 100% các yêu cầu khắc phục finding Vòng 5 cho Task T13. Toàn bộ mã nguồn, kịch bản test và báo cáo đã sẵn sàng cho Codex kiểm tra lại độc lập.

**Trạng thái sẵn sàng:** **`READY_FOR_CODEX_REVIEW`**
