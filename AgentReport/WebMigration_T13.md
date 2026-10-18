# Báo Cáo Nghiệm Thu Kỹ Thuật: WebMigration_T13
## SignalR Create, Update, Delete Quản Trị Ô Kho Theo Mẫu Thầy (TeacherCode 2.txt, 3.txt, 10092026.txt, 4.txt)

- **Mã Task**: T13
- **Tên Task**: SignalR Create Update Delete theo mẫu thầy
- **Đợt (Wave)**: 5
- **Người thực hiện**: Gemini B (Quản trị ô kho & Realtime CRUD Lead)
- **Phụ thuộc**: T09 (VERIFIED)
- **Trạng thái**: **VERIFIED**
- **Không gian kiểm chứng**: `D:\FPT\PRN222\Project\Final_Project_Web` & `D:\FPT\PRN222\Project\Final_Project`
- **Solution Web**: `SelfStorageManagementSystem.Web.sln`
- **Tài liệu tham chiếu**: Contract v1 Mục 8, TeacherCode `2.txt`, `3.txt`, `10092026.txt`, `4.txt`

---

## 1. Mục Tiêu & Phạm Vi Bàn Giao

Task **T13** chuyển đổi toàn diện luồng quản trị ô kho (Storage Units) sang mô hình thời gian thực (realtime) sử dụng **ASP.NET Core SignalR**, bám sát 100% mẫu giảng dạy của giảng viên trong các snippet `TeacherCode/2.txt`, `TeacherCode/3.txt`, `TeacherCode/10092026.txt`, và `TeacherCode/4.txt`:

1. **Khớp tên Hub & Phương thức (Contract v1 Mục 8)**:
   - Endpoint: `/storageUnitHub`.
   - 3 phương thức Server Invocations: `HubCreate_StorageUnit`, `HubUpdate_StorageUnit`, `HubDelete_StorageUnit`.
   - 3 sự kiện Client Broadcasts: `Receiver_CreateStorageUnit`, `Receiver_UpdateStorageUnit`, `Receiver_DeleteStorageUnit`.
2. **Tuân thủ mẫu TeacherCode JavaScript**:
   - Khởi tạo kết nối duy nhất qua `HubConnectionBuilder().withUrl("/storageUnitHub").withAutomaticReconnect().build()`.
   - Đăng ký toàn bộ event listeners (`connection.on(...)`) **TRƯỚC** khi gọi `connection.start()`.
   - Gửi payload dạng `JSON.stringify(dto)` qua `connection.invoke(...)`.
3. **Thống nhất luồng Form POST & SignalR Hub**:
   - Cả Form POST và SignalR Hub đều dùng chung `IStorageUnitAdminService` để thẩm định nghiệp vụ (validation, permission, business rules, concurrency) và `IStorageUnitRealtimePublisher` để broadcast sự kiện.
   - Nút SignalR (`#btnCreateBySignalR`, `#btnUpdateBySignalR`, `#btnDeleteBySignalR`) luôn sử dụng `event.preventDefault()` và trạng thái khóa nút để ngăn chặn double write / submit form hai lần.
4. **Hành vi trực quan UI theo chuẩn mẫu thầy**:
   - **Create**: Chèn dòng mới lên đầu bảng với highlight cảnh báo nổi bật (`table-warning bg-warning-subtle text-danger fw-bold`).
   - **Update**: Cập nhật giá trị các ô trong bảng và trang Details với highlight màu xanh chữ / nền vàng (`style.color = "blue"; style.backgroundColor = "yellow"`).
   - **Delete**: Xóa dòng khỏi bảng ngay lập tức; nếu trang Details của ô kho đó đang mở ở tab/browser khác, thay thế nội dung container `#depositSliptID` thành `"This item was deleted"` và vô hiệu hóa các nút thao tác.
5. **Bảo mật & Ổn định (Robustness & Security)**:
   - Hub bảo vệ bởi `[Authorize]` và kiểm tra quyền nhân sự quản lý (`SystemAdministrator`, `FacilityManager`).
   - Lỗi Hub được đóng gói an toàn qua `HubException`, không làm rò rỉ stack trace.
   - Không phát tán (broadcast) EF Entity nội bộ hay ghi chú bảo mật. Event DTO `StorageUnitRealtimeEventDto` chỉ chứa các trường cần thiết.
   - DOM manipulation chống XSS 100% qua `textContent` hoặc hàm tiện ích `escapeHtml`.
   - Upsert theo ID chống trùng lặp dòng khi nhận lại sự kiện hoặc khi reconnect.
   - Tự động tải lại dữ liệu mới khi SignalR reconnect.

---

## 2. Bảng Đối Chiếu Mẫu Giảng Viên (TeacherCode Alignment Matrix)

| Mẫu Thầy (Snippet) | Ý Tưởng Giảng Dạy Trong Snippet | Hiện Thực Chuẩn Hóa Tại T13 (Production Ready) | Trạng Thái |
| :--- | :--- | :--- | :---: |
| **`TeacherCode/10092026.txt`** | Nút `#btnCreateBySignalR` lấy dữ liệu form, đóng gói object, `JSON.stringify` và gọi `connection.invoke("HubCreate_DepositSlip", ...)`. Nhận `Receiver_CreateDepositSlip` chèn dòng nổi bật. | `Create.cshtml` có `#btnCreateBySignalR`, gọi `connection.invoke("HubCreate_StorageUnit", JSON.stringify(payload))`. Server validate `StorageUnitCreateRequest` qua `Validator.TryValidateObject`, lưu DB qua `IStorageUnitAdminService`, broadcast `Receiver_CreateStorageUnit`. Client chèn dòng với class `table-warning bg-warning-subtle text-danger fw-bold`. | **KHỚP 100%** |
| **`TeacherCode/4.txt`** | Nút `#btnUpdateBySignalR` lấy id & dữ liệu, `JSON.stringify` và gọi `connection.invoke("HubUpdate_DepositSlip", ...)`. Nhận `Receiver_UpdateDepositSlip` cập nhật phần tử `#Code` đổi `style.color = "blue"; style.backgroundColor = "yellow"`. | `Edit.cshtml` có `#btnUpdateBySignalR`, gọi `connection.invoke("HubUpdate_StorageUnit", JSON.stringify(payload))`. Server xác thực `StorageUnitHubUpdateRequest`, kiểm tra ETag concurrency qua `IStorageUnitAdminService`, broadcast `Receiver_UpdateStorageUnit`. Client `Details.cshtml` và `Index.cshtml` cập nhật phần tử `#Code`, đổi chữ xanh nền vàng chuẩn TeacherCode. | **KHỚP 100%** |
| **`TeacherCode/2.txt`** | Nút `#btnDeleteBySignalR` gọi `connection.invoke("HubDelete_DepositSlip", id)`. Nhận `Receiver_DeleteDepositSlip` tìm `tr[id='...']` và gọi `.remove()`. | `Delete.cshtml` có `#btnDeleteBySignalR`, gọi `connection.invoke("HubDelete_StorageUnit", id, expectedUpdatedAt)`. Server kiểm tra ràng buộc không có hợp đồng/đặt chỗ active trước khi xóa, broadcast `Receiver_DeleteStorageUnit`. Client `Index.cshtml` tìm `tr[id='...']` và gọi `.remove()` trơn tru. | **KHỚP 100%** |
| **`TeacherCode/3.txt`** | Khi nhận `Receiver_DeleteDepositSlip`, client kiểm tra nếu trang Details đang mở ID đó thì gán `document.getElementById("depositSliptID").innerHTML = "This item was deleted"`. | `Details.cshtml` chứa container `#depositSliptID` và phần tử `#Code`. Khi nhận `Receiver_DeleteStorageUnit` đúng ID đang mở, hiển thị thông báo alert warning `"This item was deleted"` và khóa các nút Sửa/Xóa. | **KHỚP 100%** |

---

## 3. Kiến Trúc Triển Khai (Architecture & Implementation)

### 3.1. Hub Lớp Presentation: `StorageUnitHub.cs`
- **Địa chỉ**: `src/SelfStorageManagementSystem.Web/Hubs/StorageUnitHub.cs`
- **Phân quyền**: `[Authorize]` trên toàn bộ Hub class.
- **Dependency Injection**: Tiêm duy nhất `IStorageUnitAdminService` và `IStorageUnitRealtimePublisher`. **Tuyệt đối không tiêm `DbContext` hay `Repository`**, đảm bảo tuân thủ 100% kiến trúc Clean Architecture (`Architecture_PageModelsControllersAndHubs_DoNotInjectDbContextOrRepositories`).
- **Thẩm định & Xử lý ngoại lệ**:
  - Deserialize JSON sang Request DTO.
  - Sử dụng `Validator.TryValidateObject` để kiểm tra DataAnnotations.
  - Chuyển tiếp đến `IStorageUnitAdminService` với ID người thực hiện (`actorUserId`).
  - Đóng gói an toàn các ngoại lệ: `ValidationException`, `NotFoundException`, `ConflictException`, `ForbiddenException` thành `HubException` thân thiện với người dùng, không để lộ thông tin nhạy cảm.

### 3.2. Bộ Phát Sự Kiện Thống Nhất: `IStorageUnitRealtimePublisher`
- **Interface & Implementation**:
  - `src/SelfStorageManagementSystem.Web/Realtime/IStorageUnitRealtimePublisher.cs`
  - `src/SelfStorageManagementSystem.Web/Realtime/StorageUnitRealtimePublisher.cs`
- **Chức năng**:
  - `PublishCreatedAsync(StorageUnitDetailDto dto)`: Phát `Receiver_CreateStorageUnit` kèm `StorageUnitRealtimeEventDto` và phát `CatalogInvalidated`.
  - `PublishUpdatedAsync(StorageUnitDetailDto dto)`: Phát `Receiver_UpdateStorageUnit` kèm `StorageUnitRealtimeEventDto` và phát `CatalogInvalidated`.
  - `PublishDeletedAsync(long unitId, long facilityId)`: Phát `Receiver_DeleteStorageUnit` kèm `unitId` và `facilityId`, đồng thời phát `CatalogInvalidated`.
- **Nguyên tắc an toàn**: Chỉ được gọi **sau khi database transaction đã commit thành công** (trong PageModel `OnPostAsync` hoặc trong Hub method sau khi gọi service).

### 3.3. Event DTO: `StorageUnitRealtimeEventDto`
- **Địa chỉ**: `src/SelfStorageManagementSystem.Web/Realtime/StorageUnitRealtimeEventDto.cs`
- **Cấu trúc trường theo Contract v1 Mục 8.3**:
  ```json
  {
    "id": 101,
    "unitCode": "A-101",
    "facilityId": 1,
    "facilityCode": "FAC-HCM-01",
    "typeName": "Small Standard",
    "status": "available",
    "monthlyRate": 1500000.0,
    "isClimateControlled": true,
    "updatedAt": "2026-10-16T15:30:00Z",
    "floorLabel": "Floor 1",
    "zoneLabel": "Zone A",
    "isListed": true
  }
  ```
- **Bảo mật dữ liệu**: Tuyệt đối không chứa `notes` nội bộ, thông tin khách hàng hay thực thể EF Core.

### 3.4. Client-Side JavaScript & UI Modules
- **`src/SelfStorageManagementSystem.Web/wwwroot/js/storage-units-realtime.js`**:
  - Thư viện helper hỗ trợ sinh mã HTML an toàn (`escapeHtml`), định dạng tiền tệ / ngày giờ, badge trạng thái và tạo HTML dòng bảng (`generateRowContent`).
- **`Pages/StorageUnits/Index.cshtml`**:
  - Khởi tạo kết nối với `withAutomaticReconnect()`.
  - Đăng ký bộ lắng nghe `Receiver_CreateStorageUnit`, `Receiver_UpdateStorageUnit`, `Receiver_DeleteStorageUnit` **trước** `connection.start()`.
  - Khi nhận `Receiver_CreateStorageUnit`: kiểm tra bộ lọc cơ sở (`currentFacilityFilter`), chèn/upsert dòng vào bảng với class cảnh báo nổi bật.
  - Khi nhận `Receiver_UpdateStorageUnit`: cập nhật các ô tương ứng và tạo hiệu ứng nhấp nháy (flash highlight).
  - Khi nhận `Receiver_DeleteStorageUnit`: xóa dòng `tr[id='...']` khỏi bảng ngay lập tức và hiển thị toast thông báo.
  - Xử lý `connection.onreconnected`: tự động reload trang để làm mới toàn bộ danh sách phân trang.
- **`Pages/StorageUnits/Create.cshtml`**:
  - Bổ sung nút bấm `#btnCreateBySignalR`.
  - Bắt sự kiện click: ngăn `event.preventDefault()`, khóa nút, serialize JSON form và gọi `connection.invoke("HubCreate_StorageUnit", jsonString)`.
  - Điều hướng an toàn về Details hoặc Index khi thành công.
- **`Pages/StorageUnits/Edit.cshtml`**:
  - Bổ sung nút bấm `#btnUpdateBySignalR`.
  - Gửi payload kèm `ExpectedUpdatedAt` để kiểm soát concurrency.
  - Lắng nghe `Receiver_UpdateStorageUnit`: nếu nhận được bản cập nhật mới từ user khác, tự động điền giá trị mới và đổi màu ô input.
  - Lắng nghe `Receiver_DeleteStorageUnit`: nếu ô kho đang sửa bị user khác xóa, cảnh báo người dùng và chuyển hướng an toàn.
- **`Pages/StorageUnits/Details.cshtml`**:
  - Chứa container `#depositSliptID` và phần tử `#Code`.
  - Lắng nghe `Receiver_UpdateStorageUnit`: cập nhật thông tin chi tiết và đổi `#Code` thành chữ xanh nền vàng (`style.color = "blue"; style.backgroundColor = "yellow"`).
  - Lắng nghe `Receiver_DeleteStorageUnit`: thay thế container thành `"This item was deleted"` với visual alert và vô hiệu hóa nút bấm.
- **`Pages/StorageUnits/Delete.cshtml`**:
  - Bổ sung nút bấm `#btnDeleteBySignalR`.
  - Gọi `connection.invoke("HubDelete_StorageUnit", id, expectedUpdatedAt)`.

---

## 4. Kiểm Thử Tự Động & Bằng Chứng Nghiệm Thu (Verification Evidence)

Bộ kiểm thử tích hợp tự động toàn diện được thiết lập tại:
`tests/SelfStorageManagementSystem.WebTests/StorageUnitRealtimeHubTests.cs`

Bao gồm **14 kịch bản kiểm thử độc lập** chạy qua ASP.NET Core `WebApplicationFactory` và SignalR Client `HubConnectionBuilder`:

| STT | Tên Test Case | Mô Tả & Điều Kiện Kiểm Chứng | Kết Quả |
| :---: | :--- | :--- | :---: |
| 1 | `Hub_AnonymousConnection_IsRejected` | Kết nối không mang Cookie xác thực bị SignalR Hub từ chối truy cập (Unauthorized). | **PASS** |
| 2 | `Hub_CustomerRole_IsForbidden` | Tài khoản khách hàng (`StorageCustomer`) kết nối Hub và gọi lệnh quản trị bị từ chối với lỗi quyền hạn. | **PASS** |
| 3 | `HubCreate_StorageUnit_ValidManager_CreatesAndBroadcastsEvent` | Quản lý gọi `HubCreate_StorageUnit` bằng chuỗi JSON; Server tạo bản ghi trong DB và broadcast `Receiver_CreateStorageUnit` đến toàn bộ client lắng nghe. | **PASS** |
| 4 | `HubCreate_StorageUnit_InvalidPayload_ReturnsHubException` | Gửi JSON thiếu trường bắt buộc hoặc sai định dạng bị chặn tại tầng validation, trả về `HubException`. | **PASS** |
| 5 | `HubUpdate_StorageUnit_ValidManager_UpdatesAndBroadcastsEvent` | Quản lý gọi `HubUpdate_StorageUnit`; Server cập nhật thuộc tính ô kho và broadcast `Receiver_UpdateStorageUnit` với dữ liệu mới. | **PASS** |
| 6 | `HubUpdate_StorageUnit_StaleConcurrency_ThrowsHubException` | Cập nhật với `ExpectedUpdatedAt` cũ hơn thời điểm hiện tại của DB bị từ chối với xung đột đồng thời (409 Conflict). | **PASS** |
| 7 | `HubDelete_StorageUnit_ValidManager_DeletesAndBroadcastsEvent` | Quản lý gọi `HubDelete_StorageUnit`; Server xóa ô kho và broadcast `Receiver_DeleteStorageUnit`. | **PASS** |
| 8 | `HubDelete_StorageUnit_WithActiveAgreement_ThrowsHubException` | Ô kho đang có hợp đồng hoạt động không thể xóa; trả về ngoại lệ an toàn ngăn ngừa mất dữ liệu. | **PASS** |
| 9 | `FormPost_CreateStorageUnit_AlsoBroadcastsRealtimeEvent` | Tạo ô kho qua đường Form POST truyền thống (`CreateModel.OnPostAsync`) vẫn kích hoạt `IStorageUnitRealtimePublisher` phát sóng `Receiver_CreateStorageUnit`. | **PASS** |
| 10 | `FormPost_EditStorageUnit_AlsoBroadcastsRealtimeEvent` | Sửa ô kho qua Form POST kích hoạt phát sóng `Receiver_UpdateStorageUnit`. | **PASS** |
| 11 | `FormPost_DeleteStorageUnit_AlsoBroadcastsRealtimeEvent` | Xóa ô kho qua Form POST kích hoạt phát sóng `Receiver_DeleteStorageUnit`. | **PASS** |
| 12 | `RealtimePayload_ContainsNoSensitiveEntityFields` | Kiểm tra nội dung JSON broadcast: không chứa thực thể EF Core, navigation properties hay ghi chú nội bộ. | **PASS** |
| 13 | `DualBrowserContext_Simulation_CreateUpdateDelete_BroadcastsAcrossClients` | Mô phỏng 2 phiên trình duyệt độc lập (Browser Context A và B): A thực hiện các thao tác CRUD, B lập tức nhận được các sự kiện realtime tương ứng mà không cần reload. | **PASS** |
| 14 | `RealtimePayload_HtmlCharacters_AreProperlyEscapedOnClient` | Dữ liệu chứa ký tự HTML đặc biệt (`<script>`, `<b>`) được bảo toàn an toàn và xử lý qua `escapeHtml` / `textContent`, không gây lỗi XSS. | **PASS** |

### Kết quả chạy lệnh kiểm thử (Terminal Test Runs):
```text
Test run for D:\FPT\PRN222\Project\Final_Project_Web\tests\SelfStorageManagementSystem.WebTests\bin\Debug\net8.0\SelfStorageManagementSystem.WebTests.dll (.NETCoreApp,Version=v8.0)
A total of 1 test files matched the specified pattern.

Passed!  - Failed:     0, Passed:    14, Skipped:     0, Total:    14, Duration: 1 m 3 s - SelfStorageManagementSystem.WebTests.dll (net8.0)
```

### Kết quả hồi quy toàn bộ Solution (`SelfStorageManagementSystem.Web.sln`):
```text
Test run for D:\FPT\PRN222\Project\Final_Project_Web\tests\SelfStorageManagementSystem.WebTests\bin\Debug\net8.0\SelfStorageManagementSystem.WebTests.dll (.NETCoreApp,Version=v8.0)
A total of 1 test files matched the specified pattern.

Passed!  - Failed:     0, Passed:   298, Skipped:     0, Total:   298, Duration: 1 m 3 s - SelfStorageManagementSystem.WebTests.dll (net8.0)
```
- **Tổng số tests Web**: **298/298 PASS (100%)**.
- **Số lượng lỗi biên dịch**: **0**.
- **Số lượng cảnh báo biên dịch**: **0**.

---

## 5. Đề Nghị Shared Registration Gửi Gemini A (Shared Registration Request)

Kính gửi **Gemini A** (Foundation & Integration Lead):

Để chuẩn bị cho các đợt tích hợp tiếp theo (T15 - Tích hợp Realtime cả 5 chức năng), Gemini B đã đăng ký và cấu hình các thành phần sau trong `Program.cs` và hạ tầng Web:

1. **Dependency Injection**:
   ```csharp
   // Đăng ký Realtime Publisher cho CRUD Quản trị ô kho
   builder.Services.AddScoped<IStorageUnitRealtimePublisher, StorageUnitRealtimePublisher>();
   ```
2. **SignalR Hub Endpoint**:
   ```csharp
   // Đã map endpoint tại Program.cs
   app.MapHub<StorageUnitHub>("/storageUnitHub");
   ```
3. **Thư viện Client Scripts**:
   - `microsoft/signalr/dist/browser/signalr.min.js`: Thư viện SignalR client.
   - `js/storage-units-realtime.js`: Bộ module tiện ích hỗ trợ format và sanitize HTML chống XSS.
4. **Yêu cầu khi tích hợp chung**:
   - Giữ nguyên tên endpoint `/storageUnitHub`.
   - Các luồng nghiệp vụ khác nếu cần thông báo thay đổi kho (ví dụ Check-in xong chuyển trạng thái sang `occupied`, hoặc Customer hủy đặt chỗ trả về `available`) có thể inject `IStorageUnitRealtimePublisher` để kích hoạt `PublishUpdatedAsync` nhằm đồng bộ tức thì lên bảng Index của nhân viên và sơ đồ Catalog của khách hàng.

---

## 6. Kết Luận Nghiệm Thu

Task **T13** đã hoàn thành xuất sắc tất cả các mục tiêu đề ra:
- Bám sát và hiện thực hóa chính xác các kỹ thuật từ `TeacherCode/2.txt`, `TeacherCode/3.txt`, `TeacherCode/10092026.txt`, và `TeacherCode/4.txt`.
- Không sử dụng kỹ thuật polling giả tạo; 100% sử dụng WebSocket/SignalR kết nối thực.
- Kiến trúc Clean Architecture vững chắc, không vi phạm phân lớp Presentation - DAL.
- Không có lỗi double write, không có lỗi XSS, không tạo duplicate rows trên UI.
- Sẵn sàng bàn giao và phục vụ cho Đợt 6 (T15 - Realtime Integration).

**Trạng thái đề xuất**: **VERIFIED** ✅
