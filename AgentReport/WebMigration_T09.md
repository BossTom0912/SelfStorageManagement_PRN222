# Báo Cáo Thực Hiện Task T09: Razor Pages CRUD Ô Kho Bằng Form

- **Task ID**: T09
- **Tên Task**: Razor Pages CRUD ô kho bằng form (Storage Units Admin Razor Pages CRUD)
- **Owner**: Gemini B (Admin & Inventory Lead)
- **Đợt (Wave)**: 4
- **Chờ (Dependencies)**: T08 (**VERIFIED** / **IMPLEMENTED**)
- **Trạng thái**: **VERIFIED**
- **Contract Version**: **v1 (Storage Unit Admin Form CRUD)**
- **Nghiệm thu lại**: 2026-10-16; bằng chứng Web solution sau cùng: `tommy@TOM 2026-10-16 15:11:28`
- **Môi trường**: Windows 11, .NET SDK 8.0.425 / 10.0.401, PowerShell 5.1
- **Solution Web**: `SelfStorageManagementSystem.Web.sln`

---

## 0. Sửa finding và nghiệm thu lại ngày 2026-10-16

- `Create.cshtml`: tạo từng `option` bằng `document.createElement`, gán `value` và `textContent` cho cả loại kho và khu vực. Nhãn JSON không được phân tích như HTML.
- Mỗi lần đổi cơ sở: abort request trước, xóa cả hai lookup và khóa submit. Kiểm tra cả controller hiện hành và facility đang chọn sau `fetch` và sau `json`; phản hồi/lỗi cũ không thể ghi đè hay điều hướng. Handler submit cũng từ chối trong lúc nạp hoặc khi chưa chọn cơ sở. Quyền ghi ban đầu của nút được giữ khi mở khóa.
- Giữ nguyên kiểm tra scope ở BLL.
- Browser smoke: **PASS**, Edge Chromium qua Playwright có sẵn; `Browser plugin not available`. URL `http://127.0.0.1:5189/StorageUnits/Create`; viewport 1440×1000 và 390×844. Kestrel proxy tạm gọi ứng dụng thật qua `WebApplicationFactory`, dữ liệu EF InMemory; fetch lookup được điều khiển để mô phỏng request cũ vẫn hoàn thành sau abort.
- Payload `</option><option value="999">Injected` ở cả tên loại kho và khu vực hiển thị nguyên văn; không xuất hiện option `999`. A → B, B trả trước và A trả sau: chỉ còn dữ liệu B. Lỗi A sau thành công B không đổi URL. Cả hai dropdown xóa khi chọn cơ sở rỗng; submit bị khóa lúc nạp.
- Page/title đúng, nội dung không rỗng, không có error overlay; không có console error/warning hoặc page error. Đã xem screenshot desktop/mobile.
- Bằng chứng browser: `C:/Users/tommy/.codex/visualizations/2026/10/16/01a1438b-ef07-7d80-9aa2-36f69ca0c837/t09-t11/T09-browser-result.json` và hai ảnh `T09-safe-lookups-desktop.png`, `T09-safe-lookups-mobile.png` cùng thư mục.
- Lần test sau sửa: T09 **18/18**, nhóm T09–T11 **55/55**, Web solution **244/244**, không failed/skipped. TRX tại `AgentReport/TestResults/T09-T11/`.

## 1. Tóm Tắt Kết Quả & Tiêu Chí Nghiệm Thu (Acceptance Criteria)

| Tiêu chí | Yêu cầu nghiệp vụ & kỹ thuật | Kết quả thực tế | Trạng thái |
|---|---|---|:---:|
| **Scaffold cô lập** | Sử dụng `dotnet aspnet-codegenerator razorpage` trong scratch project ngoài sản phẩm cuối theo chuẩn `TeacherCode/5.txt`. | Đã chạy scaffold thành công trong thư mục tạm `scratch/ScaffoldProject`; toàn bộ file DAL entity giữ nguyên vẹn; không để lọt code scaffold thô vào solution sản phẩm. | **ĐẠT** |
| **Kiến trúc 3 tầng & BLL Service** | Không inject `DbContext` hay Repository vào PageModel; toàn bộ thao tác gọi qua `IStorageUnitAdminService` và bind DTO request/response. | 100% PageModel inject `IStorageUnitAdminService`. Kiểm toán kiến trúc bằng Reflection `Architecture_PageModelsControllersAndHubs_DoNotInjectDbContextOrRepositories` đạt **3/3 PASS**. | **ĐẠT** |
| **Form CRUD POST & Bảo vệ CSRF** | Triển khai form POST kèm token Antiforgery `@Html.AntiForgeryToken()` và validation; GET Delete chỉ hiển thị xác nhận, không xóa dữ liệu tại GET. | Cả 3 form (Create/Edit/Delete) đều có CSRF token, validation summary và field validation. GET Delete chỉ nạp thông tin và đánh giá điều kiện; POST Delete mới thực thi xóa an toàn. | **ĐẠT** |
| **Scope cơ sở & Thuộc tính bất biến** | Tải Lookups đúng scope của người dùng; giữ `facilityId` từ Index sang Create; nạp động `UnitTypes` và `Areas` theo cơ sở; BLL thẩm định scope không tin client; giữ `FacilityId` và `UnitTypeId` bất biến khi Edit; truyền `ExpectedUpdatedAt` dạng round-trip. | Link Index forward `facilityId` sang Create; Create tự động xác định `effectiveFacilityId` và hỗ trợ AJAX handler `OnGetFacilityLookupsAsync` nạp lại `UnitTypes`/`Areas` khi đổi cơ sở, không để dropdown rỗng; BLL kiểm tra phân công chặt chẽ; Edit khóa chặt `FacilityId` và `UnitTypeId` nhãn bất biến; `ExpectedUpdatedAt` round-trip UTC ISO-8601 `Z`. | **ĐẠT** |
| **Xử lý lỗi & PRG** | Bắt và hiển thị rõ ràng Conflict (409), lỗi tham chiếu (occupied/allocations), lỗi quyền (403); POST Delete ngoài phạm vi trả về từ chối có kiểm soát không gây 500; tuân thủ Post-Redirect-Get không chứa secret. | Hiển thị thông báo thân thiện cho mọi ngoại lệ `ConflictException`, `ForbiddenException`, `BadRequestException`; POST Delete ngoài phạm vi trả lỗi kiểm soát an toàn (HTTP 200, CanDelete = false), loại bỏ hoàn toàn gọi lại `GetByIdAsync` gây 500; redirect kèm `TempData["SuccessMessage"]` sau khi submit thành công. | **ĐẠT** |
| **Chuẩn bị cho T13** | Chuẩn bị ID dòng ổn định (`unit-row-@item.Id`), data attribute (`data-unit-id="@item.Id"`) và vùng thông báo (`storageUnitsMessageArea`); chống submit hai lần; chưa kết nối Hub sớm. | Index đã gắn đầy đủ stable DOM IDs và message area; JavaScript chặn double-click và hiện spinner; không chứa kết nối SignalR sớm. | **ĐẠT** |
| **Kiểm thử tự động** | Đầy đủ 18 kịch bản kiểm thử tích hợp bao phủ Index, Create (kèm forward facilityId & dynamic areas AJAX), Edit, Details, Delete (kèm controlled forbidden), stale data, duplicate code, manager ngoài cơ sở, staff read-only. | Bộ test `StorageUnitsRazorPagesTests.cs` đạt **18/18 PASS** (100%); Toàn bộ test suite Web đạt **244/244 PASS** (100%). | **VERIFIED** |

---

## 2. Quá Trình Sinh Khung Scaffold (Theo Chuẩn TeacherCode/5.txt)

### 2.1. Lệnh scaffold đã thực thi
Tham chiếu theo mẫu tại `TeacherCode/5.txt`:
```powershell
dotnet add package Microsoft.VisualStudio.Web.CodeGeneration.Design
dotnet aspnet-codegenerator razorpage -m zPayment.Entities.VuLNS.Models.TransactionCashDepositSlip -dc zPayment.Repositories.VuLNS.Models.zPaymentContext -outDir Pages/TransactionCashDepositSlips --referenceScriptLibraries
```

Chúng tôi đã thiết lập một scratch project riêng biệt tại thư mục cách ly:
`C:\Users\tommy\.gemini\antigravity\brain\378f66df-80cc-40ac-99d1-3face17125d6\scratch\ScaffoldProject`

Lệnh thực tế đã chạy để sinh mã nguồn thô:
```powershell
# 1. Cài đặt công cụ codegenerator
dotnet tool install -g dotnet-aspnet-codegenerator --version 10.0.2

# 2. Thêm package hỗ trợ sinh mã vào scratch project
dotnet add package Microsoft.VisualStudio.Web.CodeGeneration.Design --version 8.0.7
dotnet add package Microsoft.EntityFrameworkCore.Design --version 8.0.7

# 3. Thực thi sinh mã 5 Razor Pages thô dựa trên DAL entity storage_unit và SelfStorageDbContext
dotnet aspnet-codegenerator razorpage `
  -m SelfStorageManagementSystem.DataAccess.Entities.storage_unit `
  -dc SelfStorageManagementSystem.DataAccess.Context.SelfStorageDbContext `
  -outDir Pages/StorageUnits `
  --referenceScriptLibraries
```

Năm cặp file sinh ra trong thư mục scratch gồm:
- `Index.cshtml`, `Index.cshtml.cs`
- `Create.cshtml`, `Create.cshtml.cs`
- `Edit.cshtml`, `Edit.cshtml.cs`
- `Details.cshtml`, `Details.cshtml.cs`
- `Delete.cshtml`, `Delete.cshtml.cs`

---

## 3. Bảng So Sánh & Diff Refactor (Scaffold Thô vs. Code Sản Phẩm)

Toàn bộ các file trên đã được đối chiếu, phân tích khuyết tật kiến trúc và viết lại hoàn toàn trong `src/SelfStorageManagementSystem.Web/Pages/StorageUnits/`:

| Tiêu chí | Mã Scaffold Thô (`dotnet-aspnet-codegenerator`) | Mã Refactor Sản Phẩm Chuẩn PRN222 (`T09`) |
|---|---|---|
| **Phụ thuộc tầng dữ liệu** | Inject trực tiếp `SelfStorageDbContext` vào từng `PageModel` $\longrightarrow$ **Vi phạm nghiêm trọng nguyên tắc tách tầng (Layering Violation)**. | Inject `IStorageUnitAdminService` (BLL). Web hoàn toàn không reference `DataAccess` hay `DbContext`. |
| **Model Binding** | Bind trực tiếp EF entity `[BindProperty] public storage_unit storage_unit { get; set; }` $\longrightarrow$ Nguy cơ Over-Posting, rò rỉ cấu trúc DB. | Bind các DTO chuyên biệt: `CreateStorageUnitRequest`, `UpdateStorageUnitRequest`, `StorageUnitAdminDto`, `StorageUnitAdminDetailDto`. |
| **Kiểm soát quyền & Scope** | Không có authorization; bất kỳ ai cũng có thể gọi GET/POST; không lọc theo cơ sở phân công của nhân sự. | Gắn `[Authorize(Policy = "StaffOnly")]`, phân biệt quyền Manager vs Staff; lookups và danh sách được lọc theo `_facilityScopeService`. |
| **Xử lý xóa (Delete)** | Scaffold thô xóa trực tiếp trong POST mà không có kiểm tra ràng buộc nghiệp vụ hay tham chiếu dữ liệu. | GET chỉ hiển thị xác nhận và phân tích điều kiện xóa (`CanDelete`); POST kiểm tra tham chiếu hợp đồng, gán chỗ và trạng thái occupied qua BLL. |
| **Kiểm soát xung đột (Concurrency)** | Không có optimistic concurrency. Cập nhật đè dữ liệu mới nhất nếu hai phiên làm việc mở cùng lúc (Lost Update). | Truyền `ExpectedUpdatedAt` dạng round-trip định dạng UTC ISO-8601 (`Z`). Phát hiện xung đột (409) và hiển thị cảnh báo stale data kèm nút nạp lại. |
| **Thuộc tính bất biến khi Edit** | Scaffold cho phép sửa tất cả các trường bao gồm `facility_id`, `unit_type_id`, `physical_status`. | Khóa cứng `facility_id` và `unit_type_id` dưới dạng nhãn hiển thị cố định. Chỉ cho phép sửa `unit_code`, `area_id`, `floor_label`, `zone_label`, `notes`. |
| **Phân trang & Tìm kiếm (Index)** | Tải toàn bộ bảng `_context.storage_units.ToListAsync()` vào bộ nhớ. | Phân trang SQL thông qua `PagedResult<StorageUnitAdminDto>`, lọc đa tiêu chí (Cơ sở, Loại kho, Trạng thái, Từ khóa tìm kiếm). |
| **Chuẩn bị Realtime T13** | Không có cấu trúc DOM định danh hàng hoặc vùng nhận tin. | Đặt ID dòng chuẩn `id="unit-row-@item.Id"`, attribute `data-unit-id="@item.Id"` và container động `id="storageUnitsMessageArea"`. |
| **Trải nghiệm UX & Chống lặp** | Submit thông thường không khóa nút; có thể click đúp gửi request trùng. | JavaScript `DOMContentLoaded` tự động disable nút submit và kích hoạt spinner khi người dùng gửi form. |

---

## 4. Chi Tiết Kiến Trúc & Cài Đặt 5 Razor Pages

### 4.1. Index (`Index.cshtml` & `Index.cshtml.cs`)
- **Tuyến đường**: `/StorageUnits` hoặc `/StorageUnits/Index`
- **Quyền truy cập**: `StaffOnly` (Facility Staff, Facility Manager, Operations Manager, System Admin).
- **Tính năng chính**:
  - Tải danh mục Lookups (Cơ sở, Loại kho, Khu vực) đúng phạm vi phân công của tài khoản hiện hành.
  - Bộ lọc tìm kiếm: Cơ sở lưu trữ, Loại kho, Trạng thái vật lý (`available`, `occupied`, `maintenance`, `reserved`), và Từ khóa mã ô kho.
  - Hiển thị badge màu sắc trực quan theo chuẩn trạng thái ô kho.
  - Phân trang đầy đủ bảo toàn toàn bộ tham số bộ lọc thông qua helper `GetPaginationUrl(page)`.
  - Chuẩn bị sẵn container `<div id="storageUnitsMessageArea" class="mb-3"></div>` và `<tr id="unit-row-@item.Id" data-unit-id="@item.Id">` sẵn sàng cho T13 tiếp nhận SignalR events.

#### 4.2. Create (`Create.cshtml` & `Create.cshtml.cs`)
- **Tuyến đường**: `/StorageUnits/Create`
- **Quyền truy cập**: `StaffOnly` (Tuy nhiên `FacilityStaff` ở chế độ Read-Only, chỉ `FacilityManager` và `Admin/Ops` được phép submit ghi dữ liệu).
- **Tính năng chính**:
  - Giao diện nhập liệu chuẩn Bootstrap 5 với validation summary và field validation.
  - **Bảo toàn cơ sở từ Index**: Nút "Tạo Ô Kho Mới" trên `Index.cshtml` mang query parameter `asp-route-facilityId="@Model.FacilityId"`. `Create.cshtml.cs` trong `OnGetAsync(facilityId)` tự động nhận diện `effectiveFacilityId` (từ query param hoặc tự chọn cơ sở duy nhất nếu user chỉ được phân công 1 cơ sở).
  - **Nạp động Khu vực và Loại kho (Dynamic Lookups)**:
    - Khi `effectiveFacilityId` được xác định, `GetLookupsAsync(facilityId)` nạp chính xác danh sách `Areas` và `UnitTypes` thuộc cơ sở đó, không để dropdown khu vực rỗng.
    - Cung cấp AJAX Page Handler `OnGetFacilityLookupsAsync(long facilityId)` trả về JSON `{ unitTypes, areas }`.
    - **Bảo vệ Scope ở tầng BLL**: AJAX endpoint kiểm tra phân công của tài khoản qua `_facilityScopeService.ValidateStaffFacilityScopeAsync(actorUserId, facilityId)` trước khi trả dữ liệu, tuyệt đối không tin cậy `facilityId` gửi từ client.
    - Phía client: JavaScript lắng nghe sự kiện `change` trên `#selectFacility`, gọi fetch handler `?handler=FacilityLookups&facilityId=...` và tự động cập nhật các tùy chọn của `#selectUnitType` và `#selectArea` (kèm fallback reload trang nếu có lỗi mạng).
  - Server gán mặc định `physical_status = "available"` và `is_listed = true`.
  - Bắt lỗi xung đột trùng mã ô kho trong cơ sở (`ConflictException` $\longrightarrow$ HTTP 409) và hiển thị thông báo lỗi rõ ràng trên form mà không làm mất dữ liệu người dùng đã nhập.
  - Chặn triệt để quản lý cố tình gửi tạo ô kho ngoài cơ sở được phân công (`ForbiddenException` $\longrightarrow$ HTTP 403).
  - Áp dụng mẫu Post-Redirect-Get (PRG): Điều hướng về `/StorageUnits?facilityId=...` kèm `TempData["SuccessMessage"]`.
  - Script client-side ngăn chặn double-submit (`btnSubmitCreate.disabled = true; spinner.classList.remove('d-none');`).

### 4.3. Edit (`Edit.cshtml` & `Edit.cshtml.cs`)
- **Tuyến đường**: `/StorageUnits/Edit?id={unitId}`
- **Quyền truy cập**: `StaffOnly` (Chỉ Manager/Admin có quyền submit cập nhật).
- **Tính năng chính**:
  - Khối thuộc tính bất biến (Immutable Attributes): Cơ sở lưu trữ (`FacilityName`, `FacilityCode`), Loại ô kho (`UnitTypeName`), Trạng thái vật lý (`PhysicalStatus`) hiển thị dạng Read-Only. Không tồn tại input hay select để sửa đổi các giá trị này.
  - Cho phép sửa đổi an toàn: Mã ô kho (`UnitCode`), Khu vực (`AreaId`), Nhãn tầng (`FloorLabel`), Nhãn phân khu (`ZoneLabel`), Ghi chú (`Notes`).
  - **Kiểm soát xung đột đồng thời (Optimistic Concurrency Control)**:
    - Truyền `ExpectedUpdatedAt` dạng hidden input với định dạng chuẩn UTC `yyyy-MM-ddTHH:mm:ss.fffffffZ`.
    - Khi phát hiện `updated_at` trong CSDL không khớp (stale data), BLL tung ngoại lệ `StorageUnitConcurrencyException` $\longrightarrow$ PageModel chuyển thành `ConflictException` (HTTP 409).
    - Trang hiển thị banner cảnh báo màu vàng nổi bật: *"Phát hiện xung đột đồng thời (Dữ liệu đã thay đổi)"*, hiển thị rõ mốc thời gian của người dùng so với mốc thời gian mới nhất trong CSDL, kèm nút *"Tải Lại Dữ Liệu Mới Nhất"* để người dùng đối chiếu.

### 4.4. Details (`Details.cshtml` & `Details.cshtml.cs`)
- **Tuyến đường**: `/StorageUnits/Details?id={unitId}`
- **Quyền truy cập**: `StaffOnly`.
- **Tính năng chính**:
  - Hiển thị toàn diện 3 nhóm thông tin có tổ chức:
    1. **Thông tin vị trí & Cơ sở**: Tên cơ sở, mã cơ sở, địa chỉ, khu vực/tầng, nhãn tầng, nhãn phân khu, ghi chú vận hành.
    2. **Loại ô kho & Quy cách kỹ thuật**: Kích thước Dài $\times$ Rộng $\times$ Cao, Diện tích ($m^2$), Thể tích ($m^3$), Tải trọng tối đa, Điều hòa nhiệt độ, Biểu giá niêm yết hiện hành và Tiền đặt cọc.
    3. **Thống kê tham chiếu & Lịch sử**: Số lượng hợp đồng thuê đã gán (`AllocationsCount`), Số biên bản bàn giao (`HandoverRecordsCount`), Số biên bản kiểm tra định kỳ (`InspectionsCount`), Số lệnh bảo trì (`MaintenanceOrdersCount`), Mốc thời gian tạo lập và cập nhật mới nhất.

### 4.5. Delete (`Delete.cshtml` & `Delete.cshtml.cs`)
- **Tuyến đường**: `/StorageUnits/Delete?id={unitId}`
- **Quyền truy cập**: `StaffOnly` (Chỉ Manager/Admin có quyền submit xóa).
- **Tính năng chính**:
  - **GET tuyệt đối an toàn**: Chỉ tải thông tin ô kho và đánh giá điều kiện tiên quyết (`EvaluatePreconditions`), không làm thay đổi hoặc xóa bất kỳ bản ghi nào trong DB.
  - **Chặn xóa theo chính sách bảo toàn dữ liệu**:
    - Nếu ô kho đang ở trạng thái `occupied`, `reserved` hoặc có liên kết tới `unit_allocations`: Trang hiển thị cảnh báo đỏ *"Không thể xóa ô kho này"* và **ẩn hoàn toàn nút xác nhận xóa** (`btnSubmitDelete`).
  - **POST xóa nguyên tử & Từ chối an toàn có kiểm soát (Safe Controlled Refusal)**:
    - Truyền `ExpectedUpdatedAt` dạng round-trip. Nếu ô kho đã bị thay đổi trước khi người dùng bấm xác nhận xóa, hệ thống từ chối xóa và yêu cầu nạp lại.
    - **Khắc phục lỗi 500 khi xóa ngoài phạm vi**: Trước đây trong `catch (ForbiddenException ex)` gọi lại `GetByIdAsync` để bind lại dữ liệu trang $\longrightarrow$ do không có quyền truy cập cơ sở của ô kho, `GetByIdAsync` lại ném tiếp `ForbiddenException` thứ hai dẫn đến ASP.NET Core trả lỗi HTTP 500. Sau khi sửa, khối catch không gọi lại `GetByIdAsync`, thiết lập `CanDelete = false`, gán `BlockReason = ex.Message`, thêm lỗi vào `ModelState` và trả về `Page()` (HTTP 200) với giao diện từ chối có kiểm soát. Dữ liệu trong CSDL được bảo toàn tuyệt đối.
    - Xóa thành công sẽ kích hoạt PRG quay về `/StorageUnits` kèm `TempData["SuccessMessage"]`.
    - Phòng chống double-submit bằng JavaScript spinner.

---

## 5. Bằng Chứng Kiểm Thử Tự Động (Automated Test Verification)

Bộ kiểm thử tích hợp chuyên biệt `StorageUnitsRazorPagesTests.cs` (bao gồm 18 kịch bản tích hợp hoàn chỉnh) đã được thực thi và xác nhận:

### 5.1. Kết quả kiểm thử bộ test T09 (`StorageUnitsRazorPagesTests`)

Lệnh thực tế chạy chung ba bộ chuyên biệt:
```powershell
dotnet test tests/SelfStorageManagementSystem.WebTests/SelfStorageManagementSystem.WebTests.csproj --filter "FullyQualifiedName~StorageUnitsRazorPagesTests|FullyQualifiedName~ReservationRazorPagesAndWorkerTests|FullyQualifiedName~StaffCheckInPageTests" --logger "trx;LogFileName=T09-T11-specialized.trx" --results-directory AgentReport/TestResults/T09-T11
```
Kết quả: **55 Passed, 0 Failed, 0 Skipped**, trong đó T09 **18 Passed** (đối chiếu tên class trong TRX).

Chi tiết 18 test cases đã vượt qua:
1. `Index_AnonymousUser_RedirectsToLogin`: Khách vãng lai truy cập `/StorageUnits` bị chuyển hướng về `/Account/Login`.
2. `Index_CustomerRole_IsForbidden`: Khách hàng vai trò `storage_customer` bị chuyển hướng về `/Account/Forbidden`.
3. `Index_StaffUser_RendersTable_WithStableRowId_AndMessageArea`: Nhân viên cơ sở xem được bảng dữ liệu với ID dòng ổn định (`unit-row-{id}`), `data-unit-id`, và vùng thông báo `storageUnitsMessageArea`.
4. `Index_FilterByStatus_ReturnsOnlyMatchingUnits`: Lọc danh sách ô kho theo trạng thái vật lý trả về chính xác tập dữ liệu tương ứng.
5. `Create_Get_Manager_RendersFormWithLookups`: Quản lý cơ sở mở form tạo mới thành công, nạp đủ Antiforgery token và Lookups theo cơ sở.
6. `Create_Post_Manager_ValidData_CreatesUnit_AndRedirectsWithSuccessMessage`: Tạo ô kho hợp lệ thành công, kiểm tra DB có `available`, `is_listed = true`, và PRG redirect.
7. `Create_Post_DuplicateUnitCode_ReturnsConflictError_AndPreservesInput`: Gửi trùng mã ô kho trong cùng cơ sở báo lỗi xung đột (409) và bảo toàn dữ liệu đã nhập trên form.
8. `Create_Post_ManagerOutsideFacilityScope_ReturnsForbiddenError`: Quản lý cố tình gửi tạo ô ngoài cơ sở được phân công bị từ chối quyền hạn rõ ràng (403 Forbidden).
9. `Create_Post_StaffUser_IsReadOnly_CannotCreateUnit`: Nhân viên thường (Staff) chỉ có quyền xem (Read-Only), bị chặn không được phép tạo ô kho.
10. `Edit_Get_Manager_RendersImmutableFields_AndRoundTripExpectedUpdatedAt`: Trang Edit hiển thị Cơ sở và Loại kho dưới dạng bất biến, truyền `ExpectedUpdatedAt` dạng round-trip.
11. `Edit_Post_ValidData_UpdatesUnit_PreservesFacilityAndType_AndRedirects`: Cập nhật thành công các trường được phép sửa, bảo toàn tuyệt đối Cơ sở và Loại kho trong CSDL, redirect Index.
12. `Edit_Post_StaleExpectedUpdatedAt_ReturnsConcurrencyConflictAlert_WithRefreshOption`: Gửi mốc thời gian stale phát hiện xung đột đồng thời, hiển thị cảnh báo và nút nạp lại.
13. `Details_Get_Staff_RendersComprehensiveUnitSpecsAndReferenceSummary`: Trang Details hiển thị đầy đủ thông số kỹ thuật, quy cách, kích thước, biểu giá và thống kê tham chiếu.
14. `Delete_Get_OnlyShowsConfirmation_DoesNotDeleteUnit`: GET Delete chỉ nạp thông tin xác nhận; kiểm chứng DB chứng minh không xóa dữ liệu tại GET.
15. `Delete_Post_AvailableUnitWithoutReferences_DeletesSuccessfully`: Xóa thành công ô kho khả dụng không có tham chiếu kèm kiểm soát concurrency; kiểm chứng DB xác nhận bản ghi đã bị xóa sạch.
16. `Delete_Post_UnitWithAllocationsOrOccupied_IsBlockedByPolicy`: Ô kho có gán thuê hoặc occupied bị chặn xóa ở cả GET (ẩn nút) và POST (báo lỗi vi phạm chính sách tham chiếu).
17. `Create_Get_FromIndex_WithFacilityId_PreservesFacility_RendersMatchingAreas_AndAjaxLookupsWork`: Mở Create từ Index bảo toàn facilityId đã chọn; nạp đúng danh sách Areas của cơ sở tương ứng; gọi endpoint AJAX handler `FacilityLookups` trả về đúng danh sách khu vực và loại kho theo scope.
18. `Delete_Post_ManagerOutsideFacilityScope_ReturnsForbiddenControlled_DoesNotReturn500_AndDoesNotDeleteData`: Quản lý POST xóa ô kho ngoài phân công nhận phản hồi từ chối có kiểm soát (HTTP 200 kèm CanDelete = false và lý do cấm, không gặp lỗi 500) và bản ghi trong CSDL không bị xóa.

---

### 5.2. Kiểm thử kiến trúc và toàn bộ Web solution

```powershell
dotnet test SelfStorageManagementSystem.Web.sln --no-restore --logger "trx;LogFileName=Web-solution.trx" --results-directory AgentReport/TestResults/T09-T11
```
Kết quả: **244 Passed, 0 Failed, 0 Skipped**. Trong lần chạy này có **3 test có tên chứa `Architecture` đạt**, xác nhận các thành phần Presentation không inject DbContext/Repository theo assertion của bộ smoke test.

---

## 6. Điểm Chuẩn Bị Bàn Giao Cho Task T13 (SignalR Realtime CRUD)

1. **ID dòng ổn định (Stable Row IDs)**:
   Mỗi dòng ô kho trong bảng `Index.cshtml` được gắn ID duy nhất và attribute định danh:
   ```html
   <tr id="unit-row-@item.Id" data-unit-id="@item.Id">
   ```
   T13 sẽ sử dụng `document.getElementById('unit-row-' + unitId)` để cập nhật màu sắc, hiệu ứng chớp sáng (flash highlight) hoặc xóa dòng DOM tương ứng khi nhận sự kiện từ SignalR Hub.

2. **Vùng thông báo động (Realtime Message Area)**:
   Container thông báo được bố trí ngay đầu bảng:
   ```html
   <div id="storageUnitsMessageArea" class="mb-3"></div>
   ```
   T13 sẽ append các toast/alert thông báo tự động (ví dụ: *"Ô kho HN1-A102 vừa được cập nhật bởi quản lý cơ sở"*) mà không làm gián đoạn thao tác của người dùng.

3. **Chưa kết nối Hub sớm**:
   Tuân thủ nghiêm ngặt điều kiện đạt của T09: Không khởi tạo WebSocket hay inject Hub client script vào các trang CRUD trong T09; việc kết nối và lắng nghe sự kiện realtime được dành riêng cho task T13.

---

## 7. Kết Luận
Task **T09** đã hoàn thành và đạt trạng thái **VERIFIED**. Tất cả các yêu cầu về scaffold cô lập, refactor sang BLL Service, form CRUD bảo mật với Anti-forgery và Concurrency round-trip, phân quyền theo scope cơ sở và chuẩn bị cấu trúc DOM cho T13 đã được kiểm chứng bằng bộ test HTTP và browser smoke nêu trên. Sẵn sàng bàn giao cho **T13** (SignalR Create Update Delete) tiếp nhận triển khai.
