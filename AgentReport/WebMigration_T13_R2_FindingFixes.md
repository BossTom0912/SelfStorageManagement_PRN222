# Báo Cáo Sửa Finding Vòng 2 — Task T13 (SignalR Realtime CRUD Ô Kho)

- **Người thực hiện:** Gemini B (Owner Task T13)
- **Đợt triển khai:** Đợt 5
- **Workspace chính:** `D:\FPT\PRN222\Project\Final_Project_Web`
- **Workspace đồng bộ:** `D:\FPT\PRN222\Project\Final_Project`
- **Tài liệu tham chiếu:** `AgentReport/WebMigration_T13_FindingFixes.md`, Contract v1 mục 8.3
- **Trạng thái bàn giao:** `READY_FOR_CODEX_REVIEW` (Không tự ý nâng thành VERIFIED)

---

## 1. Tóm Tắt Khắc Phục 5 Finding Round 2 (T13-R2-F01 đến F05)

| Mã Finding | Mức độ | Trạng thái | Giải pháp tóm tắt | File chính đã sửa |
| :--- | :---: | :---: | :--- | :--- |
| **T13-R2-F01** | P1 | Đã sửa | Đóng dấu ngoặc `};` cho hàm `generateRowContent`, chạy `node --check` exit code 0, kiểm thử tự động trên Microsoft Edge Headless qua giao thức CDP xác nhận `window.StorageUnitsRealtime` và các helper nạp đầy đủ vào DOM. | `storage-units-realtime.js`, `HeadlessEdgeSession.cs`, `StorageUnitsBrowserRealtimeTests.cs` |
| **T13-R2-F02** | P1 | Đã sửa | Xây dựng cơ chế thu hồi group SignalR cho connection đang sống không cần reconnect: định nghĩa `IUserScopeChangedNotifier` trong BLL, hook vào `AdminAccountService` (đổi status, đổi roles, gán/chấm dứt cơ sở), theo dõi connection qua `IStorageUnitHubConnectionTracker` và thu hồi group/quyền tức thì qua `StorageUnitHubScopeRevocationService`. | `IUserScopeChangedNotifier.cs`, `AdminAccountService.cs`, `StorageUnitHubScopeRevocationService.cs`, `StorageUnitHubConnectionTracker.cs`, `StorageUnitHub.cs` |
| **T13-R2-F03** | P2 | Đã sửa | Chuẩn hóa `matchesFilter` client chỉ tìm kiếm theo `unitCode`, `floorLabel`, `zoneLabel` (khớp hoàn toàn server search), loại bỏ tìm kiếm theo `typeName`/`facilityName`. Tuyệt đối không leak trường `notes` bảo mật vào event DTO. Khi không chắc chắn, kích hoạt `refetchTable` từ server làm nguồn sự thật. | `storage-units-realtime.js`, `StorageUnitsBrowserRealtimeTests.cs` |
| **T13-R2-F04** | P2 | Đã sửa | Thay thế logic tự prepend/xóa DOM cục bộ bằng `RealtimeUtils.refetchTable`: fetch trang hiện tại ngầm bằng `fetch(window.location.href)` và `DOMParser` để giữ nguyên thứ tự sắp xếp (theo code hoặc ngày tạo asc/desc), kéo bản ghi tiếp theo từ trang sau khi xóa bản ghi, cập nhật phân trang & tổng số, highlight dòng thay đổi. | `storage-units-realtime.js`, `Index.cshtml` |
| **T13-R2-F05** | P2 | Đã sửa | Thay thế việc nhúng chuỗi trực tiếp vào thẻ `<script>` (gây lỗi HTML-encode `&amp;`, `&quot;`) bằng phần tử DOM `<div id="storageUnitsPageConfig" data-*="...">`. Trình duyệt DOM parser tự giải mã các ký tự đặc biệt (`A&B`, dấu ngoặc kép, `<`, `>`), JavaScript đọc sạch qua `configEl.dataset`. | `Index.cshtml`, `storage-units-realtime.js`, `StorageUnitsBrowserRealtimeTests.cs` |

---

## 2. Chi Tiết Kỹ Thuật Từng Finding

### T13-R2-F01 (P1) — Cú pháp JavaScript & Kiểm thử Trình duyệt thật (Headless Edge CDP)
- **Nguyên nhân gốc:** Tại `storage-units-realtime.js`, sau khối lệnh `return` trong `RealtimeUtils.generateRowContent` thiếu dấu đóng `};` trước khai báo `RealtimeUtils.matchesFilter`. Lệnh `node --check` báo lỗi `SyntaxError: Unexpected token ')'` khiến script dừng thực thi, `window.StorageUnitsRealtime` không được khởi tạo trên trình duyệt.
- **Biện pháp xử lý:**
  1. Đã bổ sung dấu đóng hàm `};` chính xác.
  2. Kiểm tra cú pháp bằng Node.js v24.12.0: `node --check src/SelfStorageManagementSystem.Web/wwwroot/js/storage-units-realtime.js` trả về Exit Code 0.
  3. Xây dựng bộ điều khiển CDP trình duyệt Microsoft Edge không đầu (`HeadlessEdgeSession.cs`) qua WebSocket Remote Debugging:
     - Lọc chính xác CDP target `type == "page"` (tránh background page của extension trình duyệt).
     - Kết nối tới trang `/StorageUnits?facilityId=...`.
     - Kiểm tra trực tiếp trên engine V8 của Edge: `typeof window.StorageUnitsRealtime === 'object'`, các hàm `generateRowContent`, `matchesFilter`, `refetchTable` đều là `function`, và các dòng bảng `tr[id^="unit-row-"]` hiển thị chính xác.

### T13-R2-F02 (P1) — Thu hồi WebSocket Connection đang sống khi thay đổi quyền/phân công cơ sở
- **Nguyên nhân gốc:** `StorageUnitHub.cs` chỉ gán connection vào group `facility_{id}` một lần duy nhất lúc `OnConnectedAsync`. Khi Admin chấm dứt phân công cơ sở hoặc khóa tài khoản của nhân viên đang mở WebSocket, `OnValidatePrincipal` của Cookie không tự trigger trên từng frame của WebSocket đang mở, dẫn tới nhân viên vẫn nhận event CRUD của cơ sở cũ.
- **Biện pháp xử lý:**
  1. **Thiết kế Clean Architecture:**
     - Tạo interface `IUserScopeChangedNotifier` trong BLL (`Services/Interfaces/IUserScopeChangedNotifier.cs`) và implementation mặc định `NullUserScopeChangedNotifier.cs`.
     - Hook vào `AdminAccountService.cs` tại 4 vị trí mutation:
       - `UpdateUserStatusAsync` (khi tài khoản bị khóa/hủy kích hoạt).
       - `ManageUserRolesAsync` (khi vai trò bị thu hồi, ví dụ mất role Admin/Manager).
       - `AssignFacilityAsync` (khi được phân công thêm cơ sở mới).
       - `TerminateFacilityAssignmentAsync` (khi chấm dứt phân công cơ sở).
  2. **Connection Tracker & Revocation Service phía Web:**
     - `IStorageUnitHubConnectionTracker` & `StorageUnitHubConnectionTracker`: Quản lý thread-safe danh sách active connection IDs và các groups đang tham gia theo từng `userId`.
     - `StorageUnitHubScopeRevocationService`: Thực thi `IUserScopeChangedNotifier`. Khi nhận tín hiệu thay đổi scope:
       - Kiểm tra trạng thái tài khoản từ `IAccountIdentityService`. Nếu user không còn active/bị khóa -> Gỡ toàn bộ active connections khỏi tất cả các groups bằng `_hubContext.Groups.RemoveFromGroupAsync(connId, group)`.
       - Nếu user vẫn active -> Đọc lại quyền từ `IFacilityScopeService` và tính toán lại danh sách groups hợp lệ (`admin_global` và `facility_{id}`). Gỡ các connection khỏi các groups không còn được phép và thêm vào groups mới nếu có.
       - Thao tác gỡ/thêm group qua `IHubContext` có hiệu lực tức thì trên socket đang mở mà không cần client phải reconnect thủ công.
  3. **Kiểm thử tự động:**
     - Viết 3 integration tests hoàn chỉnh trong `StorageUnitRealtimeHubTests.cs`:
       - `T13_R2_F02_Staff_AssignmentTerminated_OpenWebSocketStopsReceivingEvents_WithoutManualReconnect`: Staff đang mở WebSocket nhận Event 1; Admin terminate assignment cơ sở; WebSocket giữ nguyên không reconnect; Event 2 phát sinh; Staff nhận chính xác 0 event.
       - `T13_R2_F02_UserLocked_OpenWebSocketStopsReceivingAllEvents`: Staff bị khóa tài khoản; WebSocket lập tức bị gỡ khỏi mọi group; không nhận thêm bất kỳ event nào.
       - `T13_R2_F02_AdminRoleRevoked_OpenWebSocketStopsReceivingGlobalEvents`: Admin bị thu hồi vai trò; lập tức bị gỡ khỏi group `admin_global`.

### T13-R2-F03 (P2) — Đồng nhất bộ lọc tìm kiếm Client với Server & Không rò rỉ Notes
- **Nguyên nhân gốc:** Client trước đây tìm kiếm trên `unitCode`, `typeName`, `facilityName`, trong khi server `Index.cshtml.cs` chỉ lọc theo `unit_code`, `notes`, `floor_label`, `zone_label`. Event DTO tuyệt đối không phát tán `notes` nhạy cảm.
- **Biện pháp xử lý:**
  - Sửa `matchesFilter` trong `storage-units-realtime.js`: So khớp từ khóa tìm kiếm trên `unitCode`, `floorLabel`, `zoneLabel`.
  - Loại bỏ việc so khớp `typeName` và `facilityName` ở client.
  - Viết unit test `T13_R2_F03_MatchesFilter_EvaluatesUnitCodeFloorZone_AndOmitsTypeNameFacilityNameFromSearch` xác nhận bản ghi chỉ khớp tên facility sẽ bị từ chối khỏi client filter.

### T13-R2-F04 (P2) — Giữ trọn vẹn Sort và Pagination khi nhận sự kiện Realtime
- **Nguyên nhân gốc:** Khi nhận `Receiver_CreateStorageUnit`, việc tự chèn dòng lên đầu bảng phá vỡ trật tự sắp xếp hiện hành (ví dụ khi người dùng đang xem sort theo `unit_code ASC` hoặc `created_at ASC`). Khi nhận `Receiver_DeleteStorageUnit`, dòng bị xóa để lại khoảng trống ở trang 1 mà không kéo được phần tử thứ 11 từ trang 2 lên.
- **Biện pháp xử lý:**
  - Triển khai hàm `RealtimeUtils.refetchTable(tableBodyId, totalCountId, affectedItemId, eventType)`.
  - Hàm sử dụng `fetch(window.location.href)` để lấy chính xác HTML của trang hiện tại từ server, sau đó dùng `DOMParser` để cập nhật `#storageUnitsTableBody`, thanh thông tin `#storageUnitsDisplayRange` và phân trang `#storageUnitsPaginationContainer`.
  - Giữ vững 100% thứ tự sắp xếp và logic phân trang do server quyết định.
  - Vẫn giữ nguyên hiệu ứng UX trực quan: dòng vừa tạo được highlight màu vàng/chữ đỏ (`table-warning bg-warning-subtle text-danger fw-bold`), dòng vừa cập nhật được highlight nhạt và mờ dần trong 2.5s.

### T13-R2-F05 (P2) — Đọc ngữ cảnh bộ lọc an toàn qua DOM Dataset (Chống lỗi mã hóa Razor)
- **Nguyên nhân gốc:** Việc nhúng chuỗi trực tiếp `@Model.SearchTerm` vào khối `<script>` khiến Razor tự động encode HTML (`A&B` thành `A&amp;B`, `"` thành `&quot;`), làm sai lệch chuỗi tìm kiếm trong JavaScript.
- **Biện pháp xử lý:**
  - Định nghĩa thẻ cấu hình DOM:
    ```html
    <div id="storageUnitsPageConfig"
         data-facility-id="@(Model.FacilityId.HasValue ? Model.FacilityId.Value.ToString() : "")"
         data-search-term="@(string.IsNullOrWhiteSpace(Model.SearchTerm) ? "" : Model.SearchTerm.Trim())"
         data-sort-by="@(string.IsNullOrWhiteSpace(Model.SortBy) ? "" : Model.SortBy.Trim())"
         data-sort-descending="@(Model.SortDescending ? "true" : "false")"
         data-page-number="@Model.StorageUnits.PageNumber"
         data-page-size="@Model.StorageUnits.PageSize"
         data-total-count="@Model.StorageUnits.TotalCount"
         data-can-manage="@(Model.CanManageUnits ? "true" : "false")"
         style="display:none;"></div>
    ```
  - Trình duyệt phân tích thuộc tính DOM dataset sẽ tự động giải mã các ký tự HTML entity về ký tự gốc nguyên bản, không chứa `&amp;` hay `&quot;`.
  - Kiểm thử tự động trên Microsoft Edge Headless xác nhận: từ khóa `"Kho A&B \"VIP\" <1>"` được đọc ra trong JS chính xác 100%, không chứa thực thể mã hóa nào.

---

## 3. Bằng Chứng Kiểm Thử Tự Động (Test Execution Results)

### 3.1. Kiểm thử Node.js cú pháp
```powershell
node --check src/SelfStorageManagementSystem.Web/wwwroot/js/storage-units-realtime.js
# Exit Code: 0 (Cú pháp JavaScript hoàn toàn hợp lệ)
```

### 3.2. Bộ kiểm thử trình duyệt thực tế & DOM (`StorageUnitsBrowserRealtimeTests`)
```text
Test run for SelfStorageManagementSystem.WebTests.dll (.NETCoreApp,Version=v8.0)
Passed! - Failed: 0, Passed: 5, Skipped: 0, Total: 5, Duration: 6 s
  [PASS] DirectTest_StorageUnits_Returns200OK
  [PASS] T13_R2_F01_NodeCheck_StorageUnitsRealtimeScript_ExitsWithZero
  [PASS] T13_R2_F01_EdgeBrowser_LoadsIndexPage_DefinesStorageUnitsRealtimeWithoutErrors
  [PASS] T13_R2_F05_EdgeBrowser_SpecialCharsInSearchTerm_DecodedCorrectlyInJavaScript
  [PASS] T13_R2_F03_MatchesFilter_EvaluatesUnitCodeFloorZone_AndOmitsTypeNameFacilityNameFromSearch
```

### 3.3. Bộ kiểm thử tích hợp SignalR Hub & Thu hồi quyền sống (`StorageUnitRealtimeHubTests`)
```text
Test run for SelfStorageManagementSystem.WebTests.dll (.NETCoreApp,Version=v8.0)
Passed! - Failed: 0, Passed: 23, Skipped: 0, Total: 23
  [PASS] T13_R2_F02_Staff_AssignmentTerminated_OpenWebSocketStopsReceivingEvents_WithoutManualReconnect
  [PASS] T13_R2_F02_UserLocked_OpenWebSocketStopsReceivingAllEvents
  [PASS] T13_R2_F02_AdminRoleRevoked_OpenWebSocketStopsReceivingGlobalEvents
  [PASS] T13_F01_Customer_CannotConnectToManagementHubWithoutAuthorization
  [PASS] T13_F01_StaffOfFacilityA_DoesNotReceiveEventsOfFacilityB
  [PASS] T13_F02_EditPage_ConcurrentUpdate_FormRejectsStaleSave_PreventingLostUpdate
  ... (và toàn bộ 17 kịch bản Hub invocations/broadcasts ban đầu)
```

### 3.4. Toàn bộ Solution Web (`SelfStorageManagementSystem.Web.sln`)
```text
Test run for SelfStorageManagementSystem.WebTests.dll (.NETCoreApp,Version=v8.0)
Passed! - Failed: 0, Passed: 342, Skipped: 0, Total: 342, Duration: 2 m 21 s
```

---

## 4. Bảng Băm SHA-256 Đồng Bộ Hai Workspace

Toàn bộ các file triển khai và kiểm thử đã được đồng bộ chính xác 100% giữa `Final_Project_Web` và `Final_Project`:

| Đường dẫn tệp | Khớp SHA-256 | Mã băm SHA-256 |
| :--- | :---: | :--- |
| `src/.../Services/Interfaces/IUserScopeChangedNotifier.cs` | **Match=True** | `61285F09EE7D72F0F6711055E8D5F557C5048052431099A15DA1461EC0CD528C` |
| `src/.../Services/Implementations/NullUserScopeChangedNotifier.cs` | **Match=True** | `B68270841A66DD2F5FF6810308A2B3E1E338ADBC7551EDD6C9A126D0B1C85A5C` |
| `src/.../Services/Implementations/AdminAccountService.cs` | **Match=True** | `0AC586119072EB5B3614659E5ED87650020C738B558BF03EFD1EAF3FBFF3A2DB` |
| `src/.../DependencyInjection/ServiceCollectionExtensions.cs` | **Match=True** | `0C0AD29CDCA1E86A07BEC8B072972146DAAF65E60BE81B170A4C344974A040A3` |
| `src/.../Web/Realtime/IStorageUnitHubConnectionTracker.cs` | **Match=True** | `F92191239B186D90E09A6AA671A35EFA461D75D0924EBABC80C896C00D94B251` |
| `src/.../Web/Realtime/StorageUnitHubConnectionTracker.cs` | **Match=True** | `B2205A92348DFEBAE1623E16008579AC11AC520D6E8153F72341A421A79CE760` |
| `src/.../Web/Realtime/StorageUnitHubScopeRevocationService.cs` | **Match=True** | `E6AB975256012A812A4381225F9F69893764A5D895EB0994272B3D3AF3A3D583` |
| `src/.../Web/Realtime/StorageUnitRealtimeEventDto.cs` | **Match=True** | `CD106876C19411B9F7706688D9DB6C33D508C9EF9ED915E6C244538C39BBBC93` |
| `src/.../Web/Realtime/StorageUnitRealtimePublisher.cs` | **Match=True** | `46C45D31FF09BCC6809280E9874E745B953BEF29EF5030CAD7966AD0ADCB3A0F` |
| `src/.../Web/Realtime/IStorageUnitRealtimePublisher.cs` | **Match=True** | `C208CC0D129ACBD70CCCE3569CACF230049CE13F06704DFF7163D364F1F4051A` |
| `src/.../Web/Hubs/StorageUnitHub.cs` | **Match=True** | `2475C1F0BAD2F095420CFC90B7C0D023387E54FC152315A23E0781A9969A1C2A` |
| `src/.../Web/Pages/StorageUnits/Index.cshtml` | **Match=True** | `E0648525367BCD45BF87D1D2F1063256F1C195C7E9B043D4B37D6CACEC58DAB7` |
| `src/.../Web/wwwroot/js/storage-units-realtime.js` | **Match=True** | `B9BA5A11ACAE9595049DA6963CD74C4EBF8BBBAA3963354E729006DD64550E2F` |
| `src/.../Web/Program.cs` | **Match=True** | `1367B1B969A26732AFE73093456982533B2C9822CB3B0832C3DCB6E6283EA4EB` |
| `tests/.../WebTests/Infrastructure/HeadlessEdgeSession.cs` | **Match=True** | `8ADE0243A5DDC767CE2A474179A176FE7BBFDB03DE0D4C40084CF7B0022EE511` |
| `tests/.../WebTests/StorageUnitsBrowserRealtimeTests.cs` | **Match=True** | `5B1B7FBDBEFF7ED081EB2626ADCCCD387025CF03EA05C5D5B5FB0521A55B1A80` |
| `tests/.../WebTests/StorageUnitRealtimeHubTests.cs` | **Match=True** | `0569F62A1187B1E77DA495ED200C00D5B53DAF29505E6C4D05AD665F6EC515D4` |
| `tests/.../WebTests/BrowserTests/storage-units-realtime-tests.html` | **Match=True** | `635795DE7422628A2BAD22555244CDE364099C33D0165AD475222D0E40ADADC2` |

---

## 5. Kết Luận và Bàn Giao

Các finding Round 2 của Task T13 đã được giải quyết triệt để, có bằng chứng chạy trên trình duyệt Microsoft Edge Headless thực tế và kiểm thử tự động SignalR end-to-end. Không xâm phạm phạm vi của T12 hoặc T14. Điểm tích hợp mở rộng duy nhất tại `AdminAccountService` được trừu tượng hóa bằng interface `IUserScopeChangedNotifier` và chuyển giao an toàn cho `StorageUnitHubScopeRevocationService` ở tầng Web.

Trạng thái bàn giao Task T13: **IMPLEMENTED — chờ Codex review**  
READY_FOR_CODEX_REVIEW
