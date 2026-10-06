# Báo Cáo Triển Khai Hoàn Chỉnh Chức Năng 2: Tra Cứu Cơ Sở, Loại Kho, Bảng Giá & Ô Kho Khả Dụng (Catalog & Floor Map)

- **Dự án**: Self-Storage Facility Rental and Management System
- **Học phần**: PRN222
- **Thời gian hoàn thành**: 06/10/2026
- **Branch**: `Account&RoleManagement`
- **Kiến trúc áp dụng**: 3-Layer Architecture + WPF Client (.NET 8, ASP.NET Core Web API, EF Core 8 Database-First, SQL Server `core` schema)

---

## 1. Tổng Quan Kết Quả Đạt Được

Chức năng 2 đã được triển khai hoàn chỉnh từ tầng cơ sở dữ liệu, truy vấn tối ưu, dịch vụ nghiệp vụ, controller Web API đến giao diện tương tác WPF Client.

1. **Tuân thủ kiến trúc 3 lớp & Database-First**:
   - Không can thiệp hoặc sửa đổi các Entity scaffolded hay `SelfStorageDbContext.cs`.
   - Toàn bộ truy vấn đọc sử dụng `AsNoTracking()`, phân trang thực thi trực tiếp tại SQL Server qua `Skip`/`Take`.
   - Controller giữ mỏng, không gọi trực tiếp DbContext hay Repository; trả về chuẩn `ApiResponse<T>` thống nhất.
2. **Tuân thủ quy tắc nghiệp vụ cốt lõi**:
   - **BR-OPS-02**: Danh sách ô kho trống (`/api/facilities/{id}/units/available`) tuyệt đối loại trừ các ô đang sử dụng (`occupied`), bảo trì (`maintenance`), đang giữ chỗ (`reserved`), chờ kiểm tra, ngừng hoạt động, ô có lệnh bảo trì chặn đặt chỗ (`blocks_booking = 1`), hoặc có phân bổ active giao cắt khoảng ngày thuê.
   - **BR-RSV-02**: Kiểm tra thời hạn thuê tối thiểu 1 tháng và tối đa 12 tháng.
   - **BR-RSV-03**: Khách hàng chọn đặt chỗ theo **Loại kho (Unit Type)** và diện tích. Sơ đồ mặt bằng phục vụ trực quan hóa không gian; nút tiếp tục đặt chỗ chỉ lưu giữ `FacilityId`, `UnitTypeId` và khoảng ngày thuê, **không cố định gán mã ô kho (`storageUnitId`)** tại bước này.
   - **Timezone Awareness**: Tự động xác định ngày hiện tại theo múi giờ khai báo của từng cơ sở (`facility.timezone`) để kiểm tra tính hợp lệ của ngày thuê.
   - **Honest Labeling**: Gắn nhãn trung thực "Số ô trống tham khảo tại thời điểm tra cứu" (`EstimatedAvailableUnits`) sau khi đã khấu trừ các đơn đặt chỗ đang có hiệu lực nhưng chưa được gán ô cụ thể.
3. **Giao diện WPF Client**:
   - Thêm lối vào cho **khách vãng lai** trực tiếp từ màn hình đăng nhập (`LoginWindow`) mà không làm ảnh hưởng luồng Login/Register.
   - Thêm lối vào cho **người dùng đã đăng nhập** từ `MainWindow`.
   - Màn hình `FacilityCatalogWindow`: hỗ trợ lọc cơ sở, lọc loại kho theo ngân sách/ngày thuê/kho mát, xem danh sách ô khả dụng, và vẽ sơ đồ mặt bằng trực quan trên WPF Canvas dựa trên dữ liệu tọa độ `unit_map_positions`.

---

## 2. Danh Sách Endpoint Web API (Presentation Layer)

Bộ endpoint catalog được cấu hình công khai (`[AllowAnonymous]`) phục vụ khách vãng lai và khách hàng tra cứu trước khi đăng nhập/đặt chỗ:

| Phương thức | Đường dẫn API | Tham số | Mô tả & Quy tắc nghiệp vụ |
|:---:|---|---|---|
| **GET** | `/api/facilities` | `city`, `district`, `searchTerm`, `pageNumber`, `pageSize` | Lọc danh sách cơ sở đang hoạt động (`status = 'active'`), sắp xếp ổn định, phân trang `PagedResult<FacilityCatalogDto>`. |
| **GET** | `/api/facilities/{id}` | `id` (mã cơ sở) | Lấy chi tiết cơ sở (địa chỉ, giờ mở/đóng cửa, múi giờ vận hành). Trả 404 nếu cơ sở không tồn tại hoặc đã đóng cửa. |
| **GET** | `/api/facilities/{id}/unit-types` | `rentalStartDate`, `rentalEndDate`, `maxPrice`, `climateControlled` | Danh mục loại kho hoạt động; giá tháng, tiền cọc (100%), phí đặt lấy từ `facility_rates` theo ngày bắt đầu; số ô trống tham khảo (`EstimatedAvailableUnits`) đã khấu trừ đơn đặt chưa gán ô. |
| **GET** | `/api/facilities/{id}/units/available` | `unitTypeId`, `areaId`, `climateControlled`, `maxPrice`, `rentalStartDate`, `rentalEndDate`, `pageNumber`, `pageSize` | Danh sách ô kho ứng viên sẵn sàng cho thuê theo BR-OPS-02; trả đầy đủ thông tin kích thước, giá, tình trạng định vị sơ đồ; phân trang `PagedResult<AvailableStorageUnitDto>`. |
| **GET** | `/api/facilities/{id}/floor-map` | `areaId`, `rentalStartDate`, `rentalEndDate` | Dữ liệu sơ đồ mặt bằng: khu vực (`facility_areas`) và tọa độ ô kho (`unit_map_positions`). Trạng thái đã giản lược (`available`, `occupied`, `reserved`, `maintenance`, `unavailable`), không chứa thông tin nhạy cảm của khách thuê. |

---

## 3. Giao Diện & Trải Nghiệm Trên WPF Client

Giao diện tra cứu được thiết kế trên công nghệ WPF XAML thuần, không phụ thuộc trình duyệt hay WebView ngoài:

1. **Lối vào màn hình Catalog**:
   - **Tại `LoginWindow`**: Bổ sung nút `🏢 Tra cứu kho & bảng giá (Public Catalog)` ở chân trang. Khách vãng lai bấm vào có thể duyệt catalog ngay lập tức mà không cần tạo tài khoản trước.
   - **Tại `MainWindow`**: Người dùng đã đăng nhập (Customer, Staff, Manager, Admin) có nút `🏢 Tra cứu Cơ sở & Ô kho` trên thanh công cụ và nút `🔍 Tìm kiếm Kho & Xem Sơ đồ Mặt bằng` trong thẻ Storage Customer.
2. **Cửa sổ `FacilityCatalogWindow`**:
   - **Bảng điều khiển cơ sở (Cột trái)**: Tìm kiếm chi nhánh theo thành phố/tỉnh, quận/huyện hoặc từ khóa; danh sách cơ sở hiển thị trực quan địa chỉ và giờ mở cửa; phân trang cơ sở độc lập.
   - **Thanh tham số thuê dùng chung**: DatePicker chọn ngày bắt đầu thuê và ngày kết thúc thuê (mặc định từ ngày tiếp theo, kỳ hạn 1 tháng); bộ lọc giá trần và tùy chọn kho mát có điều hòa nhiệt độ.
   - **Tab 1: Loại kho & Biểu phí**: DataGrid trình bày mã loại, tên loại, kích thước chi tiết (Rộng x Dài x Cao), diện tích, giá thuê tháng, tiền cọc, số ô khả dụng tham khảo. Khi chọn một loại kho, người dùng có thể bấm nút `Tiếp tục chọn loại kho này (Chức năng 3) ➜`.
   - **Tab 2: Danh sách Ô kho Khả dụng**: DataGrid liệt kê từng ô kho ứng viên còn trống kèm tầng/khu vực và tình trạng định vị trên sơ đồ, hỗ trợ phân trang `btnUnitPrev` / `btnUnitNext`.
   - **Tab 3: Sơ đồ Mặt bằng Tương tác (Canvas Floor Map)**:
     - ComboBox chọn tầng/khu vực (`facility_areas`).
     - Vẽ các ô kho trực tiếp lên WPF `Canvas` theo tọa độ `X, Y, Width, Height` từ `unit_map_positions`.
     - Phân định trạng thái màu sắc: **Xanh lá** (Trống/Khả dụng), **Đỏ** (Đang thuê), **Vàng cam** (Đang giữ chỗ), **Xám** (Bảo trì/Khóa).
     - Báo cáo số lượng ô chưa được gắn tọa độ trên sơ đồ (`lblMapUnplacedCount`).
     - Khi click vào một ô trên sơ đồ, hệ thống hiển thị chi tiết mã ô, loại kho, diện tích, giá niêm yết và trạng thái. Nếu ô khả dụng, kích hoạt nút tiếp tục đặt chỗ theo đúng Loại Kho của ô đó.
3. **Cơ chế chuyển tiếp sang Chức năng 3**:
   - Cả hai nút tiếp tục (từ Tab Loại Kho và từ Tab Sơ Đồ) đều tuân thủ nghiêm ngặt **BR-RSV-03**: Hệ thống hiển thị hộp thoại xác nhận tổng hợp thông tin gồm `FacilityId`, `UnitTypeId`, `StartDate`, `EndDate`, đơn giá thuê và tiền cọc, giải thích rõ quy định đặt chỗ theo Loại kho (ô cụ thể được nhân viên bàn giao tại thời điểm Check-in) và chuẩn bị sẵn sàng dữ liệu cho Chức năng 3 mà không tạo dữ liệu giả mạo.

---

## 4. Bảng và Cột Database (`core` Schema) Sử Dụng

| Bảng SQL | Các cột sử dụng | Mục đích nghiệp vụ |
|---|---|---|
| `core.facilities` | `id`, `code`, `name`, `address_line`, `ward`, `district`, `city`, `latitude`, `longitude`, `timezone`, `opening_time`, `closing_time`, `status` | Quản lý danh mục cơ sở kho; chỉ lấy cơ sở `status = 'active'`. |
| `core.unit_types` | `id`, `code`, `name`, `width_m`, `length_m`, `height_m`, `area_m2`, `volume_m3`, `climate_controlled`, `max_weight_kg`, `description`, `is_active` | Danh mục loại kho và thông số kỹ thuật; chỉ lấy loại kho `is_active = 1`. |
| `core.facility_rates` | `id`, `facility_id`, `unit_type_id`, `monthly_rate`, `deposit_amount`, `booking_fee`, `valid_from`, `valid_to` | Nguồn biểu phí niêm yết thực tế; kiểm tra hiệu lực `valid_from <= startDate` và `(valid_to IS NULL OR startDate < valid_to)`. |
| `core.storage_units` | `id`, `facility_id`, `unit_type_id`, `area_id`, `unit_code`, `floor_label`, `zone_label`, `physical_status`, `is_listed` | Thông tin ô kho vật lý; lọc ứng viên khả dụng với `is_listed = 1` và `physical_status = 'available'`. |
| `core.facility_areas` | `id`, `facility_id`, `parent_area_id`, `code`, `name`, `area_type`, `display_order`, `is_active`, `map_metadata` | Phân khu vực/tầng của cơ sở phục vụ hiển thị sơ đồ. |
| `core.unit_map_positions` | `unit_id`, `area_id`, `x`, `y`, `width`, `height`, `rotation_degrees`, `metadata` | Tọa độ và kích thước ô kho trên sơ đồ Canvas. |
| `core.maintenance_work_orders` | `id`, `storage_unit_id`, `blocks_booking`, `status` | Kiểm tra lệnh bảo trì đang hiệu lực (`status NOT IN ('completed', 'cancelled')`) có chặn đặt chỗ (`blocks_booking = 1`). |
| `core.unit_allocations` | `storage_unit_id`, `reservation_id`, `allocation_kind`, `allocation_start_date`, `allocation_end_date`, `status` | Kiểm tra phân bổ active trùng khoảng ngày thuê (`allocation_start_date < endDate AND allocation_end_date > startDate`). |
| `core.reservations` | `facility_id`, `unit_type_id`, `start_date`, `end_date`, `hold_until`, `status` | Kiểm tra các đơn đặt chỗ còn hiệu lực chưa gán ô để khấu trừ vào số lượng khả dụng tham khảo. |

---

## 5. Danh Sách File Đã Tạo Mới và Thay Đổi

### File Tạo Mới (14 files)
1. `src/SelfStorageManagementSystem.DataAccess/Repositories/Interfaces/IFacilityCatalogRepository.cs`
2. `src/SelfStorageManagementSystem.DataAccess/Repositories/Implementations/FacilityCatalogRepository.cs`
3. `src/SelfStorageManagementSystem.BusinessLogic/DTOs/Requests/Catalog/GetFacilitiesCatalogRequest.cs`
4. `src/SelfStorageManagementSystem.BusinessLogic/DTOs/Requests/Catalog/GetFacilityUnitTypesRequest.cs`
5. `src/SelfStorageManagementSystem.BusinessLogic/DTOs/Requests/Catalog/GetAvailableUnitsRequest.cs`
6. `src/SelfStorageManagementSystem.BusinessLogic/DTOs/Requests/Catalog/GetFacilityFloorMapRequest.cs`
7. `src/SelfStorageManagementSystem.BusinessLogic/DTOs/Responses/Catalog/FacilityCatalogDto.cs`
8. `src/SelfStorageManagementSystem.BusinessLogic/DTOs/Responses/Catalog/FacilityUnitTypeCatalogDto.cs`
9. `src/SelfStorageManagementSystem.BusinessLogic/DTOs/Responses/Catalog/AvailableStorageUnitDto.cs`
10. `src/SelfStorageManagementSystem.BusinessLogic/DTOs/Responses/Catalog/FacilityFloorMapDto.cs`
11. `src/SelfStorageManagementSystem.BusinessLogic/Services/Interfaces/IFacilityCatalogService.cs`
12. `src/SelfStorageManagementSystem.BusinessLogic/Services/Implementations/FacilityCatalogService.cs`
13. `src/SelfStorageManagementSystem.Presentation/Controllers/CatalogController.cs`
14. `src/SelfStorageManagementSystem.WpfClient/Models/CatalogModels.cs`
15. `src/SelfStorageManagementSystem.WpfClient/Views/FacilityCatalogWindow.xaml`
16. `src/SelfStorageManagementSystem.WpfClient/Views/FacilityCatalogWindow.xaml.cs`
17. `tests/SelfStorageManagementSystem.Tests/FacilityCatalogServiceTests.cs`
18. `tests/SelfStorageManagementSystem.Tests/CatalogIntegrationTests.cs`

### File Chỉnh Sửa (5 files)
1. `src/SelfStorageManagementSystem.DataAccess/DependencyInjection/ServiceCollectionExtensions.cs`: Đăng ký `IFacilityCatalogRepository`.
2. `src/SelfStorageManagementSystem.BusinessLogic/DependencyInjection/ServiceCollectionExtensions.cs`: Đăng ký `IFacilityCatalogService`.
3. `src/SelfStorageManagementSystem.WpfClient/Services/ApiClient.cs`: Bổ sung 4 method HTTP gọi catalog bảo mật qua HTTPS.
4. `src/SelfStorageManagementSystem.WpfClient/Views/LoginWindow.xaml` & `.xaml.cs`: Thêm nút mở catalog cho khách vãng lai.
5. `src/SelfStorageManagementSystem.WpfClient/Views/MainWindow.xaml` & `.xaml.cs`: Thêm nút mở catalog cho người dùng đã đăng nhập.

---

## 6. Kết Quả Build và Test Thực Tế

### 6.1. Build Solution
- Lệnh: `dotnet build SelfStorageManagementSystem.sln`
- Kết quả: **Build Succeeded**.
- Lỗi: **0 Error(s)**.
- Cảnh báo: **0 Warning(s)** từ mã nguồn viết tay (11 cảnh báo CS8981 từ tên entity EF scaffolded được bảo lưu theo quy tắc repo).

### 6.2. Test Suite
- Lệnh: `dotnet test`
- Kết quả: **Passed: 70, Failed: 0, Skipped: 0, Total: 70**.
  - **50 bài test Chức năng 1**: Tiếp tục pass 100% (Auth, Roles, JWT, PasswordHasher, Facility Scope).
  - **14 bài unit/integration test Chức năng 2** (`FacilityCatalogServiceTests`, `CatalogIntegrationTests`, `ApiClientAuthorizationHeaderTests`): Kiểm thử phân trang cơ sở active, loại bỏ bảng giá hết hạn, kiểm tra điều kiện BR-RSV-02 (ngày quá khứ, < 1 tháng, > 12 tháng), khấu trừ reservation chưa gán ô khỏi số khả dụng tham khảo, kiểm tra BR-OPS-02 lọc triệt để các ô không khả dụng, kiểm tra che giấu thông tin người thuê trên sơ đồ mặt bằng, kiểm tra loại trừ ô thuộc unit type inactive hoặc không có giá hợp lệ, kiểm tra nhất quán sức chứa loại kho trên sơ đồ, kiểm tra lọc diện tích (min/max area).

### 6.3. Kiểm Chứng Trực Tiếp Trên SQL Server Thật
Đã thực hiện truy vấn read-only trên database `SelfStoragePRN222` của instance `localhost (MSSQLSERVER)`:
- Cơ sở: Đang có 2 chi nhánh active: `HCM-TD` (Thu Duc Self Storage) và `HN-CG` (Cau Giay Self Storage).
- Biểu phí: Bảng `[core].facility_rates` đang có 5 bản ghi hợp lệ hiệu lực từ `2026-01-01` (`Small 3 m2`, `Medium 6 m2`, `Medium 6 m2 Climate Controlled`).
- Ô kho & Sơ đồ: Bảng `[core].storage_units` đang có 6 ô kho khả dụng và 6 bản ghi tương ứng trong `[core].unit_map_positions`.
- Toàn bộ câu lệnh LINQ thiết kế trong `FacilityCatalogRepository` hoàn toàn tương thích và khớp nối 100% với schema thật này.

---

## 7. Các Điểm Mâu Thuẫn Phát Hiện Giữa Tài Liệu và Schema SQL

1. **Bảng giữ chỗ tạm thời (`reservation_hold`)**:
   - *Tài liệu (Roadmap)*: Nhắc đến entity/table `reservation_hold`.
   - *Schema SQL thật*: Không có bảng `reservation_holds` độc lập. Thời hạn giữ chỗ 15 phút là cột `core.reservations.hold_until` (`datetimeoffset(7)`), và `'reservation_hold'` là giá trị của check constraint `core.unit_allocations.allocation_kind IN ('reservation_hold', 'rental', 'transfer')`.
2. **Nguồn biểu phí bán hàng (`price_ranges` vs `facility_rates`)**:
   - *Tài liệu*: Có đoạn ghi lấy giá từ bảng `price_ranges`.
   - *Schema SQL thật*: Bảng `core.price_ranges` chỉ lưu khung giá min/max trần sàn toàn hệ thống; bảng giá bán và tiền cọc thực tế của từng cơ sở là `core.facility_rates` (`monthly_rate`, `deposit_amount`, `booking_fee`).
3. **Quy tắc gán ô kho (`BR-RSV-03` vs Giao diện chọn ô)**:
   - *Tài liệu roadmap*: Ghi "Khách click chọn ô kho trên sơ đồ để chuyển sang bước đặt chỗ".
   - *Quy tắc nghiệp vụ gốc BR-RSV-03*: Khách hàng chỉ đặt chỗ theo Loại kho (Unit Type) và Diện tích. Mã ô cụ thể do Staff/Manager gán lúc Check-in. Vì vậy, hệ thống catalog cho xem sơ đồ tham khảo, nhưng nút tiếp tục chỉ chuyển giao `facilityId` và `unitTypeId`.
4. **Tên trạng thái vật lý của ô kho (`physical_status`)**:
   - *Tài liệu*: Viết "In-Use" và "Under Maintenance".
   - *Schema SQL thật*: Check constraint trên `core.storage_units.physical_status` quy định các giá trị: `'available', 'reserved', 'occupied', 'pending_inspection', 'maintenance', 'out_of_service'`.

---

## 8. Rủi Ro Còn Lại và Điểm Nối Sang Chức Năng 3

- **Rủi ro Concurrency tại thời điểm đặt chỗ**:
  - API `GET` chỉ cung cấp số liệu tham khảo tại thời điểm tra cứu (`EstimatedAvailableUnits`). Khi có nhiều khách hàng cùng xem một loại kho còn 1 ô duy nhất, cả hai đều có thể bấm tiếp tục đặt chỗ.
  - **Giải pháp**: Ở Chức năng 3 (Reservation & Hold Unit), thao tác tạo đơn `POST /api/reservations` phải được thực hiện trong một Transaction có kiểm tra đồng thời (RowVersion / Optimistic Concurrency hoặc khóa hàng) để đảm bảo không bị vượt quá số lượng ô kho còn trống.
- **Điểm nối trực tiếp sang Chức năng 3**:
  - Bộ tham số đã được chuẩn bị đầy đủ từ Chức năng 2: `FacilityId`, `UnitTypeId`, `RentalStartDate`, `RentalEndDate`, đơn giá thuê và tiền cọc.
  - Chức năng 3 sẽ tiếp nhận bộ tham số này, mở màn hình xác nhận đặt kho, tạo bản ghi `reservations` với `status = 'pending'`, `hold_until = UtcNow + 15m`, kích hoạt đồng hồ đếm ngược 15:00 trên WPF, và thiết lập `BackgroundService` chạy ngầm để quét hủy các đơn quá hạn.

---

## 9. Chi Tiết Sửa Lỗi và Tối Ưu Hóa Sau Review (Finding A – F)

### Finding A: Sơ đồ bị trống sau lần tải đầu hoặc khi đổi cơ sở
- **Nguyên nhân**: WPF gửi `areaId=0` ("Tất cả các khu vực") lên API khiến repository query `u.area_id == 0` trả về rỗng; đổi cơ sở không xóa cache và không nạp lại ComboBox khu vực của cơ sở mới.
- **Khắc phục**:
  - Trong `FacilityCatalogWindow.xaml.cs`: `LoadFloorMapAsync` luôn gọi API với `areaId=null` để nạp toàn bộ sơ đồ của cơ sở vào `_cachedFloorMap`. Việc lọc khu vực được thực hiện trực tiếp trong bộ nhớ tại `RenderFloorMap`.
  - Khi đổi cơ sở hoặc tải xong sơ đồ mới: ComboBox khu vực `cboMapArea` được xóa và tạo mới với mục "Tất cả các khu vực" (Id=0) cùng danh sách các khu vực của cơ sở hiện tại.

### Finding B: `/units/available` trả ô thuộc loại kho ngừng hoạt động hoặc không có giá hợp lệ
- **Nguyên nhân**: Truy vấn `GetPagedAvailableUnitsAsync` ban đầu chỉ kiểm tra bảng giá khi có tham số `maxPrice`, chưa ràng buộc bắt buộc loại kho phải `is_active` và phải tồn tại `facility_rate` hợp lệ tại `rentalStartDate`.
- **Khắc phục**:
  - Bổ sung điều kiện bắt buộc trong `FacilityCatalogRepository.GetPagedAvailableUnitsAsync`:
    1. `u.unit_type.is_active = true`
    2. Tồn tại `facility_rate` của đúng cơ sở và loại kho với `valid_from <= startDate` và `(valid_to IS NULL OR startDate < valid_to)` (kèm điều kiện `monthly_rate <= maxPrice` nếu có lọc giá).
  - Áp dụng cùng điều kiện `u.unit_type.is_active` cho `CountCandidateAvailableUnitsAsync`.

### Finding C: Ba endpoint diễn giải "khả dụng" không nhất quán theo BR-RSV-03
- **Nguyên nhân**: `/unit-types` khấu trừ đơn đặt chỗ chưa gán ô, trong khi `/floor-map` gán `CanSelectToProceed = true` cho mọi ô vật lý trống kể cả khi loại kho đó đã hết sức chứa đặt trước hoặc chưa có giá niêm yết.
- **Khắc phục**:
  - Trong `FacilityCatalogService.GetFacilityFloorMapAsync`: Tính toán bản đồ sức chứa đặt trước của từng loại kho: `unitTypeCapacity = Math.Max(0, candidateUnits - unassignedReservations)`.
  - Một ô trên sơ đồ chỉ có `CanSelectToProceed = true` khi:
    1. Trạng thái hiển thị vật lý là `available`
    2. Loại kho đang hoạt động (`is_active = true`)
    3. Có bảng giá hợp lệ tại ngày thuê (`hasValidRate`)
    4. Sức chứa đặt trước của loại kho còn lớn hơn 0 (`unitTypeCapacity > 0`).
  - Khi click vào ô có `DisplayStatus = "available"` nhưng loại kho hết chỗ, UI hiển thị thông báo rõ ràng rằng ô kho chỉ mang tính chất tham khảo trực quan và nút tiếp tục đặt chỗ bị vô hiệu hóa.

### Finding D: Lựa chọn cũ tồn tại khi đổi ngữ cảnh
- **Nguyên nhân**: Khi đổi cơ sở, đổi ngày hoặc áp dụng bộ lọc mới, các biến trạng thái `_selectedUnitType`, `_selectedMapItem`, nút bấm và nhãn tóm tắt không được reset, dẫn đến nguy cơ gửi nhầm `UnitTypeId` của cơ sở cũ sang bước đặt chỗ.
- **Khắc phục**:
  - Bổ sung phương thức `ResetAllSelections()` trong `FacilityCatalogWindow.xaml.cs` và gọi mỗi khi đổi cơ sở, thay đổi bộ lọc ngày/giá/diện tích.
  - Thêm chốt chặn bảo vệ trong `BtnProceedWithType_Click`: Kiểm tra `_selectedUnitType.FacilityId == _selectedFacility.Id`, nếu lệch sẽ cảnh báo và reset toàn bộ lựa chọn.
  - Kiểm tra `_selectedMapItem.CanSelectToProceed` trong `BtnProceedFromMap_Click`.

### Finding E: Thiếu lọc diện tích và định dạng CultureInfo số thập phân
- **Nguyên nhân**: DTO và giao diện thiếu lọc diện tích tối thiểu/tối đa; query params số thập phân có nguy cơ bị lỗi định dạng theo locale máy tính.
- **Khắc phục**:
  - Thêm `MinAreaM2` và `MaxAreaM2` vào `GetFacilityUnitTypesRequest` và `GetAvailableUnitsRequest`.
  - Thêm kiểm tra tham số: không nhận số âm, `MinAreaM2 <= MaxAreaM2`.
  - Cập nhật `ApiClient.cs`: Format toàn bộ số thập phân (`maxPrice`, `minAreaM2`, `maxAreaM2`) bằng `CultureInfo.InvariantCulture`.
  - Bổ sung ô nhập `txtFilterMinArea` và `txtFilterMaxArea` trên thanh công cụ của `FacilityCatalogWindow.xaml`.

### Finding F: Race condition / Response cũ đè response mới khi người dùng thao tác nhanh
- **Nguyên nhân**: Khi người dùng chuyển đổi liên tục giữa các cơ sở hoặc các trang, các request bất đồng bộ trước đó trả về sau có thể ghi đè giao diện cơ sở mới.
- **Khắc phục**:
  - Áp dụng cơ chế Sequence Token / Request Version cho tất cả các luồng tải bất đồng bộ: `_facilityRequestVersion`, `_unitTypesRequestVersion`, `_availableUnitsRequestVersion`, `_floorMapRequestVersion`.
  - Sau mỗi câu lệnh `await ApiClient`, kiểm tra phiên bản request và ID cơ sở hiện tại; nếu đã có thao tác mới phát sinh thì hủy bỏ kết quả cũ và không cập nhật lên UI.

---

## 10. Hoàn Thiện 5 Lỗi Còn Lại (Remaining Fixes)
1. **Đồng bộ contract khả dụng `/units/available` với sức chứa loại kho theo BR-RSV-03**:
   - Tái sử dụng `ComputeUnitTypeCapacitiesAsync` giữa cả 3 endpoint (`/unit-types`, `/floor-map`, `/units/available`).
   - Lọc `eligibleUnitTypeIds` có sức chứa nhận đặt > 0 trước `CountAsync` và `Skip/Take` trong `GetPagedAvailableUnitsAsync`.
   - Cập nhật UI làm rõ danh sách ô khả dụng thực tế thuộc các loại kho còn sức chứa nhận đặt chỗ.
2. **Khắc phục lựa chọn sơ đồ tồn tại sau khi đổi khu vực**:
   - Thêm `ResetMapSelection()` khi đổi khu vực (`cboMapArea`), giữ nguyên lựa chọn loại kho ở Tab 1.
   - Kiểm tra chặt chẽ trong `BtnProceedFromMap_Click`: ô phải thuộc cache hiện tại, đúng Area đang lọc và `CanSelectToProceed == true`.
3. **Ngăn chặn Cross-tab race condition bằng `_catalogContextVersion`**:
   - Bổ sung `_catalogContextVersion` đại diện cho ngữ cảnh cơ sở và bộ lọc đã áp dụng.
   - Tăng version khi đổi cơ sở và khi áp dụng bộ lọc. Cả 3 tab kiểm tra song song context version và tab version sau `await`.
4. **Đồng bộ ngày hiển thị với ngày tính toán khả dụng qua `AppliedFilterSnapshot`**:
   - Lưu trữ `AppliedFilterSnapshot` khi người dùng bấm "Áp dụng lọc".
   - Bắt sự kiện thay đổi điều khiển nhập (`OnFilterInputChanged` đồng bộ `void`), tự động reset selections và cảnh báo người dùng.
   - Cả 2 nút tiếp tục đọc ngày trực tiếp từ snapshot đã áp dụng.
5. **Xử lý đầu vào giá và diện tích với 3 trạng thái rõ ràng**:
   - `ParseVndPrice`: hỗ trợ số nguyên và định dạng phân tách nghìn tiếng Việt (`1.500.000` hoặc `1,500,000`).
   - `ParseArea`: hỗ trợ số thập phân không âm (chấp nhận cả `.` và `,`), loại bỏ hoàn toàn `NumberStyles.Any`.
   - Hiển thị `MessageBox` cảnh báo và dừng thực thi ngay khi đầu vào không hợp lệ, tuyệt đối không âm thầm gọi API.

---

## 11. Hoàn Thiện Tiếp Tục 3 Vấn Đề Chuyên Sâu (Lỗi A, B, C)
1. **Khắc phục triệt để Lỗi A (Draft input chưa áp dụng)**:
   - Thêm cờ `_hasUnappliedFilterChanges`. Khi sửa DatePicker hoặc các ô nhập giá/diện tích, cờ được bật `true`.
   - Khóa nút Tiếp tục ở mọi handler (chọn lại dòng trong DataGrid, click ô trên sơ đồ Canvas, chuyển tab).
   - Kiểm tra `_hasUnappliedFilterChanges` ở cả 2 nút Tiếp tục. Không cho chuyển tab tải lại bằng snapshot cũ rồi mở khóa thao tác.
2. **Khắc phục triệt để Lỗi B (Tab khác giữ dữ liệu cũ có thể click trong lúc tải)**:
   - Áp dụng invariant: Mỗi tab chỉ cho chọn và Tiếp tục khi dữ liệu tab đó đã tải THÀNH CÔNG cho `_catalogContextVersion` hiện tại.
   - Theo dõi bằng 3 biến: `_loadedUnitTypesContextVersion`, `_loadedAvailableUnitsContextVersion`, `_loadedFloorMapContextVersion`.
   - Khi Apply hoặc đổi cơ sở: xóa sạch Canvas và DataGrid của các tab khác, đặt các loaded context version về `-1`.
   - Handler chọn dòng/ô và 2 nút Tiếp tục kiểm tra đối chiếu version đã tải với `_catalogContextVersion`.
3. **Khắc phục triệt để Lỗi C (Diễn giải đúng nghĩa TotalCount là số ô ứng viên)**:
   - Sửa nhãn phân trang và status bar: phân biệt rõ ràng giữa "số ô trống ứng viên vật lý" (TotalCount = 3) và "sức chứa nhận đặt theo loại kho" (Remaining Booking Capacity = 1) theo đúng BR-RSV-03.
   - Banner Tab 2 được diễn giải chính xác.
   - Thêm unit test kiểm chứng kịch bản 3 ô ứng viên vật lý + 2 unassigned reservations.



