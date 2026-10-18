# Báo Cáo Sửa Finding T13 — SignalR CRUD Ô Kho

- **Mã Task**: T13
- **Tên Task**: SignalR Create Update Delete theo mẫu thầy (TeacherCode 2.txt, 3.txt, 10092026.txt, 4.txt)
- **Đợt (Wave)**: 5 (Song song 4)
- **Người thực hiện**: Gemini B (Quản trị ô kho & Realtime CRUD Lead)
- **Không gian triển khai**: `D:\FPT\PRN222\Project\Final_Project_Web`
- **Solution Web**: `SelfStorageManagementSystem.Web.sln`
- **Trạng thái bàn giao**: **`READY_FOR_CODEX_REVIEW`**

---

## 1. Tổng Quan Xử Lý Các Finding

Báo cáo này ghi nhận việc khắc phục toàn diện 3 finding P2 thuộc Task T13 theo yêu cầu từ Codex Review:
- **T13-F01 (P2)**: Broadcast lộ dữ liệu tới mọi tài khoản đăng nhập (Customer, nhân viên khác cơ sở).
- **T13-F02 (P2)**: Mất cập nhật đồng thời trên trang Edit do tự động nạp ETag mới cho dữ liệu form cũ.
- **T13-F03 (P2)**: Dòng realtime Create/Update/Delete phá bộ lọc và phân trang trên trang Index.

---

## 2. Chi Tiết Từng Finding & Giải Pháp Triển Khai

### 2.1. T13-F01 — Broadcast lộ dữ liệu tới mọi tài khoản đăng nhập (P2)

- **Nguyên nhân gốc (Root Cause)**:
  - `StorageUnitHub.cs` chỉ áp dụng thuộc tính `[Authorize]` ở cấp độ class mà không phân nhóm quyền hạn kết nối/nhận sự kiện.
  - `StorageUnitRealtimePublisher.cs` sử dụng `_hubContext.Clients.All.SendAsync(...)` để phát các sự kiện CRUD (`Receiver_CreateStorageUnit`, `Receiver_UpdateStorageUnit`, `Receiver_DeleteStorageUnit`).
  - Hậu quả: Mọi tài khoản đã đăng nhập (bao gồm khách hàng `StorageCustomer` hoặc nhân viên cơ sở A) đều nhận trọn vẹn dữ liệu CRUD quản trị của các cơ sở khác.
- **Giải pháp triển khai (Solution Applied)**:
  1. **Tách quyền kết nối/nhận khỏi quyền gọi CRUD**:
     - `StorageUnitHub` tiêm `IFacilityScopeService` (tuân thủ Clean Architecture, không tiêm DbContext/Repo).
     - Ghi đè `OnConnectedAsync()`:
       - `SystemAdministrator` & `BusinessOperationsManager`: Tham gia nhóm toàn hệ thống `"admin_global"`.
       - `FacilityManager` & `FacilityStaff`: Truy vấn danh sách cơ sở được phân công qua `_facilityScopeService.GetAccessibleFacilityIdsAsync(actorUserId)` và tham gia các nhóm tương ứng `$"facility_{facilityId}"`.
       - `StorageCustomer`: Không tham gia bất kỳ nhóm CRUD quản trị nào.
     - Khi kết nối lại (`onreconnected`), SignalR tự động kích hoạt `OnConnectedAsync` với connection ID mới và nạp lại phạm vi phân công hiện hành, không làm mở rộng quyền hạn.
  2. **Định tuyến sự kiện CRUD theo nhóm**:
     - `PublishCreatedAsync`: Phát tới `Clients.Groups(new[] { $"facility_{unit.FacilityId}", "admin_global" })`.
     - `PublishUpdatedAsync`: Phát tới `Clients.Groups(new[] { $"facility_{unit.FacilityId}", "admin_global" })`.
     - `PublishDeletedAsync(unitId, facilityId)`: Phát tới `Clients.Groups(new[] { $"facility_{facilityId}", "admin_global" })`.
     - Sự kiện công khai `CatalogInvalidated` vẫn được gửi an toàn tới `Clients.All` vì chỉ chứa metadata sức chứa (`facilityId`, `unitTypeId`, `timestampUtc`), không lộ thuộc tính ô kho nội bộ.
  3. **Đảm bảo Delete event mang đủ facility ID**:
     - Cập nhật interface `IStorageUnitRealtimePublisher.PublishDeletedAsync(long unitId, long facilityId, ...)` kèm overload fallback.
     - Trong `StorageUnitHub.HubDelete_StorageUnit` và `Delete.cshtml.cs`: Truy vấn ô kho trước khi xóa để lấy `facilityId` định tuyến chính xác đến nhóm cơ sở, trong khi payload client gửi đi vẫn giữ nguyên chuỗi `unitId.ToString()` theo Contract v1 Section 8.3 & TeacherCode `2.txt`.
- **Tệp và dòng sửa đổi**:
  - `src/SelfStorageManagementSystem.Web/Realtime/IStorageUnitRealtimePublisher.cs`: Dòng 14–16.
  - `src/SelfStorageManagementSystem.Web/Realtime/StorageUnitRealtimePublisher.cs`: Dòng 37–78.
  - `src/SelfStorageManagementSystem.Web/Hubs/StorageUnitHub.cs`: Dòng 33, 42, 53–87 (OnConnectedAsync), dòng 306–324 (HubDelete_StorageUnit).
  - `src/SelfStorageManagementSystem.Web/Pages/StorageUnits/Delete.cshtml.cs`: Dòng 115–128.

---

### 2.2. T13-F02 — Mất cập nhật đồng thời trên trang Edit (P2)

- **Nguyên nhân gốc (Root Cause)**:
  - Trong `Pages/StorageUnits/Edit.cshtml` dòng 264–268, listener `Receiver_UpdateStorageUnit` khi nhận sự kiện từ phiên khác đã tự động gán `expUpdatedInput.value = item.updatedAt;`.
  - Hậu quả: Dữ liệu trên form (`AreaId`, `FloorLabel`, `ZoneLabel`, `Notes`) vẫn là dữ liệu cũ do người dùng đang mở, nhưng `ExpectedUpdatedAt` lại bị đổi thành mốc mới nhất. Khi người dùng bấm Lưu, form vượt qua kiểm tra concurrency trên máy chủ và ghi đè làm mất thay đổi của phiên kia (Lost Update).
- **Giải pháp triển khai (Solution Applied)**:
  1. **Không tự động nhận ETag mới cho dữ liệu form cũ**:
     - Bỏ đoạn gán `expUpdatedInput.value = item.updatedAt;`. `ExpectedUpdatedAt` ẩn vẫn giữ nguyên mốc thời gian lúc form được nạp ban đầu.
     - Khi form được gửi lên, máy chủ so sánh `ExpectedUpdatedAt` cũ với `UpdatedAt` mới trong CSDL và từ chối an toàn với ngoại lệ `ConflictException` (HTTP 409).
  2. **Hiển thị cảnh báo trực quan & Khóa thao tác lưu**:
     - Thêm banner cảnh báo `#realtimeStaleWarning`: Thông báo ô kho vừa được cập nhật bởi phiên khác, nêu rõ mốc thời gian máy chủ và cung cấp nút *"Tải Lại Dữ Liệu Mới Nhất"* (`window.location.reload()`).
     - Đặt cờ `isFormStale = true;`.
     - Vô hiệu hóa cả nút lưu truyền thống (`#btnSubmitEdit.disabled = true;`) và nút lưu SignalR (`#btnUpdateBySignalR.disabled = true;`).
     - Bắt sự kiện submit form và click nút SignalR: Nếu `isFormStale`, chặn thực thi ngay lập tức qua `event.preventDefault()` và hiển thị thông báo alert.
     - Vẫn duy trì hiệu ứng đổi màu `#Code` thành chữ xanh nền vàng theo mẫu TeacherCode `4.txt` để người dùng nhận biết ô kho vừa được cập nhật ở máy chủ.
- **Tệp và dòng sửa đổi**:
  - `src/SelfStorageManagementSystem.Web/Pages/StorageUnits/Edit.cshtml`: Dòng 74–92 (Banner `#realtimeStaleWarning`), dòng 236–328 (Logic chặn lưu và khóa form).

---

### 2.3. T13-F03 — Dòng realtime phá bộ lọc và phân trang (P2)

- **Nguyên nhân gốc (Root Cause)**:
  - `Pages/StorageUnits/Index.cshtml` chỉ so sánh `item.facilityId === activeFacilityId`, bỏ qua toàn bộ các tham số lọc khác (`unitTypeId`, `areaId`, `physicalStatus`, `isListed`, `searchTerm`) và vị trí phân trang (`pageNumber`, `pageSize`).
  - Hậu quả: Khi tạo mới ô kho, một dòng không khớp bộ lọc (hoặc khi người dùng đang xem trang 2) vẫn bị prepend vào đầu bảng; số dòng hiển thị vượt quá `pageSize` làm sai lệch bố cục và phân trang. Khi cập nhật ô kho làm bản ghi rời khỏi bộ lọc (ví dụ chuyển trạng thái sang `maintenance`), dòng vẫn tồn tại trong bảng.
- **Giải pháp triển khai (Solution Applied)**:
  1. **Hàm thẩm định bộ lọc toàn diện `matchesFilter(item, filter)`**:
     - Bổ sung helper `StorageUnitsRealtime.matchesFilter(item, filter)` trong `wwwroot/js/storage-units-realtime.js`.
     - Kiểm tra đầy đủ: `facilityId`, `unitTypeId`, `areaId`, `physicalStatus`, `isListed`, `searchTerm` (so sánh mã ô, loại kho, tên cơ sở).
  2. **Truyền đầy đủ ngữ cảnh bộ lọc từ Razor PageModel xuống JavaScript**:
     - Khởi tạo object `currentFilter` chứa: `facilityId`, `unitTypeId`, `areaId`, `physicalStatus`, `isListed`, `searchTerm`, `pageNumber`, `pageSize`, `totalCount`.
     - Gắn ID `#storageUnitsTotalCount` vào thẻ tổng số lượng ô kho để đồng bộ động.
  3. **Xử lý sự kiện Create**:
     - Nếu `!matchesFilter`: Không chèn vào bảng; hiển thị toast thông báo nhẹ nhàng *"Ô kho mới vừa được tạo (không thuộc bộ lọc hiện tại)"*.
     - Nếu `matchesFilter` nhưng `pageNumber > 1`: Không chèn vào trang 2; hiển thị toast kèm liên kết tải lại trang / về trang 1.
     - Nếu `matchesFilter` và `pageNumber === 1`:
       - Nếu dòng đã có trong DOM: Cập nhật tại chỗ (upsert).
       - Nếu dòng chưa có: Kiểm tra số dòng hiện tại; nếu đã đạt `pageSize`, tỉa bỏ dòng cuối cùng (`tableBody.lastElementChild.remove()`) để không vượt quá `pageSize`.
       - Prepend dòng mới nổi bật theo chuẩn TeacherCode (`table-warning text-danger fw-bold`).
       - Tăng biến đếm `#storageUnitsTotalCount` lên 1.
  4. **Xử lý sự kiện Update**:
     - Nếu dòng có trong DOM:
       - Nếu vẫn khớp `matchesFilter`: Cập nhật nội dung ô và nháy flash highlight vàng.
       - Nếu không còn khớp (bản ghi rời bộ lọc): Thực hiện animation fadeOut và gỡ bỏ khỏi DOM (`$(row).remove()`), giảm biến đếm `#storageUnitsTotalCount` xuống 1. Nếu bảng không còn dòng nào, hiển thị thông báo danh sách trống.
     - Nếu dòng chưa có trong DOM nhưng `matchesFilter`: Hiển thị toast thông báo có ô kho vừa được cập nhật khớp bộ lọc kèm liên kết tải lại.
  5. **Xử lý sự kiện Delete**:
     - Tìm dòng trong DOM, fadeOut và xóa khỏi bảng.
     - Giảm biến đếm `#storageUnitsTotalCount` xuống 1. Nếu bảng rỗng, hiển thị placeholder trống.
- **Tệp và dòng sửa đổi**:
  - `src/SelfStorageManagementSystem.Web/wwwroot/js/storage-units-realtime.js`: Dòng 158–202.
  - `src/SelfStorageManagementSystem.Web/Pages/StorageUnits/Index.cshtml`: Dòng 215, dòng 245–375.

---

## 3. Bằng Chứng Hai Browser & Kiểm Thử Client (Two-Browser Verification)

### 3.1. Kịch bản kiểm thử tích hợp SignalR 2 trình duyệt độc lập (C# xUnit Integration Tests)
Bộ test `StorageUnitRealtimeHubTests.cs` bổ sung 6 test case chuyên biệt kiểm tra đa ngữ cảnh trình duyệt và luồng sự kiện thực tế:

| STT | Tên Test Case | Kịch Bản & Mục Tiêu Xác Minh | Kết Quả |
| :---: | :--- | :--- | :---: |
| 1 | `T13_F01_Customer_SubscribedToHub_ReceivesZeroCrudEvents` | Khách hàng kết nối Hub và lắng nghe 3 sự kiện CRUD. Quản lý thực hiện Create/Update/Delete. Khách hàng nhận **0 sự kiện CRUD**, chỉ nhận sự kiện công khai `CatalogInvalidated`. | **PASS** |
| 2 | `T13_F01_StaffOfFacilityA_DoesNotReceiveEventsOfFacilityB` | Nhân viên cơ sở A kết nối Hub. Quản lý cơ sở B thực hiện CRUD tại cơ sở B. Nhân viên cơ sở A nhận **0 sự kiện** từ cơ sở B. | **PASS** |
| 3 | `T13_F01_Admin_ReceivesEventsAcrossAllFacilities` | System Administrator kết nối Hub. CRUD diễn ra tại cả cơ sở A và cơ sở B. Admin nhận đầy đủ sự kiện từ cả hai cơ sở. | **PASS** |
| 4 | `T13_F01_DeleteStorageUnit_RoutesToFacilityGroup_AndPreservesContractPayload` | Quản lý xóa ô kho cơ sở A. Staff A nhận chuỗi ID ô kho (Contract v1 Section 8.3); Staff B không nhận sự kiện. | **PASS** |
| 5 | `T13_F02_EditPage_ConcurrentUpdate_FormRejectsStaleSave_PreventingLostUpdate` | Phiên A và B cùng mở Edit. Phiên A sửa và lưu thành công. Phiên B giữ form cũ và ETag cũ gửi lên: BLL chặn lại với lỗi 409 Conflict ("Phát hiện xung đột đồng thời"). Dữ liệu của A được bảo toàn 100%. | **PASS** |
| 6 | `T13_F03_MatchesFilter_EvaluatesAllCriteriaCorrectly` | Kiểm tra tính chính xác của bộ quy tắc lọc: cơ sở, loại kho, khu vực, trạng thái vật lý, niêm yết, từ khóa tìm kiếm. | **PASS** |

### 3.2. Bộ kiểm thử DOM trình duyệt thực tế (`storage-units-realtime-tests.html`)
Tệp test trình duyệt độc lập được thiết lập tại:
`tests/SelfStorageManagementSystem.WebTests/BrowserTests/storage-units-realtime-tests.html`

Bao gồm 7 kịch bản chạy trong DOM:
1. `T13-F03-01`: `matchesFilter` thẩm định toàn diện tất cả 6 trường lọc (`PASS`).
2. `T13-F03-02`: Create không khớp bộ lọc không được chèn vào bảng (`PASS`).
3. `T13-F03-03`: Create khi đang ở trang 2 không prepend vào bảng, tránh phá cấu trúc phân trang (`PASS`).
4. `T13-F03-04`: Create trên trang 1 tỉa dòng cuối cùng để tổng số dòng không vượt quá `pageSize` (`PASS`).
5. `T13-F03-05`: Update làm bản ghi rời bộ lọc tự động gỡ dòng khỏi DOM và giảm biến đếm tổng (`PASS`).
6. `T13-F02-06`: Edit form từ chối nhận ETag mới, hiển thị cảnh báo stale và khóa các nút Lưu (`PASS`).
7. `T13-XSS-07`: `escapeHtml` ngăn chặn 100% tấn công XSS qua thuộc tính ô kho (`PASS`).

---

## 4. Kết Quả Chạy Kiểm Thử Toàn Bộ Hệ Thống (Test Execution Results)

### 4.1. Chạy riêng bộ test Hub Realtime T13:
```powershell
dotnet test .\tests\SelfStorageManagementSystem.WebTests\SelfStorageManagementSystem.WebTests.csproj --filter "FullyQualifiedName~StorageUnitRealtimeHubTests" --nologo
```
- **Kết quả**: `Passed! - Failed: 0, Passed: 20, Skipped: 0, Total: 20, Duration: 1 m 48 s`.

### 4.2. Chạy hồi quy toàn bộ Web Solution (`SelfStorageManagementSystem.Web.sln`):
```powershell
dotnet test .\SelfStorageManagementSystem.Web.sln --nologo
```
- **Kết quả**: `Passed! - Failed: 0, Passed: 334, Skipped: 0, Total: 334, Duration: 1 m 47 s`.
- **Lỗi biên dịch**: **0**.
- **Cảnh báo biên dịch**: **0**.

---

## 5. Bảng Đối Chiếu Đồng Bộ Mã Nguồn & Bảng Băm SHA-256

Tất cả các tệp sửa đổi của T13 đã được đồng bộ 100% giữa hai kho lưu trữ `Final_Project_Web` và `Final_Project`:

| STT | Đường Dẫn Tệp (File Path) | Bảng Băm SHA-256 (Khớp 100%) | Trạng Thái Kiểm Tra Xung Đột |
| :---: | :--- | :--- | :---: |
| 1 | `src/SelfStorageManagementSystem.Web/Hubs/StorageUnitHub.cs` | `D994A4D76F4C12439A00D5EC1817799D6F368B0D42E033C88C8B0057093DECB6` | Không xung đột, Clean Architecture |
| 2 | `src/SelfStorageManagementSystem.Web/Realtime/IStorageUnitRealtimePublisher.cs` | `C208CC0D129ACBD70CCCE3569CACF230049CE13F06704DFF7163D364F1F4051A` | Khớp Contract v1, hỗ trợ facilityId routing |
| 3 | `src/SelfStorageManagementSystem.Web/Realtime/StorageUnitRealtimePublisher.cs` | `46C45D31FF09BCC6809280E9874E745B953BEF29EF5030CAD7966AD0ADCB3A0F` | Phát sóng theo nhóm cơ sở & admin_global |
| 4 | `src/SelfStorageManagementSystem.Web/Realtime/StorageUnitRealtimeEventDto.cs` | `CD106876C19411B9F7706688D9DB6C33D508C9EF9ED915E6C244538C39BBBC93` | Đầy đủ trường cho filter matching |
| 5 | `src/SelfStorageManagementSystem.Web/Realtime/StorageUnitHubUpdateRequest.cs` | `9DD0594C6BD6F9AB6CA4C05CFFD6B25B8D5DFD6B598116691E5334D3B4E41844` | Hỗ trợ JSON DTO validation |
| 6 | `src/SelfStorageManagementSystem.Web/Pages/StorageUnits/Delete.cshtml.cs` | `6717A04362AB7040E1F96004161AC9A92151907F2A334165C497C67C330DCEB3` | Định tuyến Delete event kèm facilityId |
| 7 | `src/SelfStorageManagementSystem.Web/Pages/StorageUnits/Edit.cshtml` | `C3CF2F97CC8ED45EE67009797CDF44A7AE4DB5C91D57C0FD1BB168FF61DD1D5B` | Chặn lost update, khóa nút lưu khi stale |
| 8 | `src/SelfStorageManagementSystem.Web/Pages/StorageUnits/Index.cshtml` | `D6782623FEB6EFA0DC17B5B163DC1982CC1F17958743983B728F2342616B8D91` | Tôn trọng toàn diện filter và pagination |
| 9 | `src/SelfStorageManagementSystem.Web/wwwroot/js/storage-units-realtime.js` | `172F1555736405A0F1B3E874A16EB74CF264FFD82DE0858AF14335C36FDFAE6F` | Bổ sung matchesFilter và escapeHtml |
| 10 | `tests/SelfStorageManagementSystem.WebTests/StorageUnitRealtimeHubTests.cs` | `AC149312C418DA49FD833DF0066B1AA37E5DB19BEF7DB4548A8EB560AC1AF895` | 20/20 test pass, kiểm thử đa browser |
| 11 | `tests/SelfStorageManagementSystem.WebTests/BrowserTests/storage-units-realtime-tests.html` | `635795DE7422628A2BAD22555244CDE364099C33D0165AD475222D0E40ADADC2` | 7/7 DOM test browser pass |

---

## 6. Rủi Ro Còn Lại & Khuyến Nghị (Remaining Risks & Recommendations)

1. **Kết nối mạng kém trên thiết bị di động**:
   - Khi mạng bị gián đoạn, SignalR tự động kích hoạt `withAutomaticReconnect`. Cơ chế `onreconnected` đã được cấu hình tự động reload danh sách sau 1 giây để bảo đảm tính nhất quán dữ liệu phân trang.
2. **Số lượng phân công cơ sở lớn đối với một nhân viên**:
   - `OnConnectedAsync` duyệt danh sách các cơ sở phân công để thêm connection vào các group `facility_{facilityId}`. Trong thực tế một nhân viên thường phụ trách từ 1–3 cơ sở nên chi phí gia nhập nhóm là không đáng kể.
3. **Phạm vi task**:
   - Đúng yêu cầu, task chỉ sửa đổi trong phạm vi T13; tuyệt đối không can thiệp vào mã nguồn của T12 hoặc T14.

---

## 7. Kết Luận Bàn Giao

Ba finding T13-F01, T13-F02 và T13-F03 đã được khắc phục hoàn chỉnh, bảo đảm an toàn dữ liệu, chống rò rỉ quyền hạn và tuân thủ chặt chẽ các mẫu của giảng viên. Toàn bộ 334 test trong Web Solution đạt 100% PASS.

**Trạng thái sẵn sàng nghiệm thu**:
`READY_FOR_CODEX_REVIEW`
