# Báo Cáo Cập Nhật & Sửa Lỗi Chức Năng 2: Facility & Storage Unit Catalog

- **Dự án**: Self-Storage Facility Rental and Management System (PRN222)
- **Thời gian hoàn thành**: 06/10/2026
- **Branch**: `Account&RoleManagement`
- **Kiến trúc áp dụng**: 3-Layer Architecture + WPF Client (.NET 8, ASP.NET Core Web API, EF Core 8 Database-First, SQL Server `core` schema)
- **Quy tắc nghiệp vụ cốt lõi**: BR-OPS-02, BR-RSV-02, BR-RSV-03

---

## 1. Tổng Hợp Các Lỗi Review Đã Xác Nhận và Phương Án Khắc Phục (Findings A – F)

| Mã lỗi | Vấn đề review phát hiện | Trạng thái trước khi sửa | Giải pháp & Thay đổi đã hoàn tất |
| :---: | :--- | :--- | :--- |
| **Finding A** | **Sơ đồ bị trống sau lần tải đầu hoặc khi đổi cơ sở** | WPF tạo mục "Tất cả khu vực" với `Id=0`. `LoadFloorMapAsync` gửi `areaId=0` lên API; repository query `u.area_id == 0` trả về rỗng vì ID SQL bắt đầu từ 1. ComboBox giữ nguyên danh sách khu vực của cơ sở cũ khi đổi cơ sở. | WPF `LoadFloorMapAsync` luôn gọi API với `areaId: null` để tải toàn bộ sơ đồ của cơ sở vào bộ nhớ đệm `_cachedFloorMap`. Lọc khu vực được xử lý trong bộ nhớ tại `RenderFloorMap`. Khi đổi cơ sở, xóa cache và nạp lại ComboBox `cboMapArea` tương ứng với cơ sở mới. |
| **Finding B** | **`/units/available` trả ô thuộc loại kho ngừng hoạt động hoặc không có giá hợp lệ** | `GetPagedAvailableUnitsAsync` chỉ kiểm tra bảng giá khi có tham số `maxPrice`, chưa ràng buộc bắt buộc loại kho phải `is_active` và phải tồn tại `facility_rate` hợp lệ tại `rentalStartDate`. | Cập nhật cả `GetPagedAvailableUnitsAsync` và `CountCandidateAvailableUnitsAsync` để **LUÔN LUÔN** ràng buộc: (1) `u.unit_type.is_active = true`, (2) Tồn tại `facility_rate` hợp lệ tại `rentalStartDate` (`valid_from <= startDate` và `valid_to IS NULL OR startDate < valid_to`). Cả query đếm và danh sách dùng chung biểu thức. |
| **Finding C** | **Ba endpoint diễn giải "khả dụng" không nhất quán theo BR-RSV-03** | `/unit-types` khấu trừ đơn đặt chỗ chưa gán ô, trong khi `/floor-map` gán `CanSelectToProceed = true` cho mọi ô vật lý trống kể cả khi loại kho đó đã hết sức chứa đặt trước hoặc chưa có biểu phí hợp lệ. | Tính toán bản đồ sức chứa đặt trước theo loại kho: `unitTypeCapacity = Math.Max(0, candidateUnits - unassignedReservations)`. Một ô trên sơ đồ chỉ có `CanSelectToProceed = true` khi: vật lý available, loại kho active, có rate hợp lệ và sức chứa loại kho > 0. Nếu loại kho hết chỗ, ô trống chỉ hiển thị xem trực quan và nút đặt chỗ bị vô hiệu hóa. |
| **Finding D** | **Lựa chọn cũ tồn tại khi đổi ngữ cảnh** | Khi đổi cơ sở, đổi ngày hoặc áp dụng bộ lọc mới, các biến `_selectedUnitType`, `_selectedMapItem`, nút bấm và nhãn tóm tắt không được reset, gây nguy cơ gửi nhầm `UnitTypeId` của cơ sở cũ sang bước đặt chỗ. | Bổ sung `ResetAllSelections()` trong `FacilityCatalogWindow.xaml.cs` và gọi mỗi khi đổi cơ sở, chọn lại ngày, áp dụng bộ lọc. Trong `BtnProceedWithType_Click`, kiểm tra `_selectedUnitType.FacilityId == _selectedFacility.Id`. Trong `BtnProceedFromMap_Click`, kiểm tra `_selectedMapItem.CanSelectToProceed`. |
| **Finding E** | **Thiếu lọc diện tích và định dạng CultureInfo số thập phân** | Request DTO và giao diện thiếu tham số diện tích `MinAreaM2` và `MaxAreaM2`. Query params số thập phân ghép chuỗi không dùng `CultureInfo.InvariantCulture`. | Thêm `MinAreaM2` và `MaxAreaM2` vào `GetFacilityUnitTypesRequest` và `GetAvailableUnitsRequest`. Kiểm tra số âm và `MinAreaM2 <= MaxAreaM2` trong `FacilityCatalogService`. Format số thập phân bằng `CultureInfo.InvariantCulture` trong `ApiClient.cs`. Bổ sung ô nhập diện tích trên thanh công cụ WPF. |
| **Finding F** | **Race condition / Response cũ đè response mới khi người dùng thao tác nhanh** | Thao tác chuyển đổi cơ sở hoặc phân trang nhanh có thể khiến response gửi đi trước (trả về trễ) ghi đè lên giao diện của cơ sở mới chọn. | Triển khai Sequence Tokens / Request Version (`_facilityRequestVersion`, `_unitTypesRequestVersion`, `_availableUnitsRequestVersion`, `_floorMapRequestVersion`). Sau mỗi câu lệnh `await ApiClient`, kiểm tra phiên bản request và ID cơ sở hiện tại; hủy bỏ kết quả cũ nếu không còn phù hợp. |

---

## 2. Danh Sách Các File Đã Thay Đổi và Phạm Vi Can Thiệp

| STT | File thay đổi | Layer / Dự án | Phạm vi & Lý do can thiệp |
| :---: | :--- | :--- | :--- |
| 1 | `src/SelfStorageManagementSystem.BusinessLogic/DTOs/Requests/Catalog/GetFacilityUnitTypesRequest.cs` | BusinessLogic | Bổ sung `MinAreaM2` và `MaxAreaM2` cho request lọc loại kho (Finding E). |
| 2 | `src/SelfStorageManagementSystem.BusinessLogic/DTOs/Requests/Catalog/GetAvailableUnitsRequest.cs` | BusinessLogic | Bổ sung `MinAreaM2` và `MaxAreaM2` cho request lọc ô khả dụng (Finding E). |
| 3 | `src/SelfStorageManagementSystem.DataAccess/Repositories/Interfaces/IFacilityCatalogRepository.cs` | DataAccess | Bổ sung tham số `minAreaM2`, `maxAreaM2` vào `GetActiveFacilityRatesAsync` và `GetPagedAvailableUnitsAsync`. |
| 4 | `src/SelfStorageManagementSystem.DataAccess/Repositories/Implementations/FacilityCatalogRepository.cs` | DataAccess | Ràng buộc bắt buộc `u.unit_type.is_active` và `facility_rates` hợp lệ tại `startDate` trong `GetPagedAvailableUnitsAsync`; đồng bộ điều kiện `is_active` cho `CountCandidateAvailableUnitsAsync`; áp dụng lọc diện tích (Finding B, E). |
| 5 | `src/SelfStorageManagementSystem.BusinessLogic/Services/Implementations/FacilityCatalogService.cs` | BusinessLogic | Kiểm tra tham số bộ lọc (`ValidateFilterParameters`); tính toán bản đồ sức chứa loại kho `unitTypeCapacityMap` và kiểm soát cờ `CanSelectToProceed` trên sơ đồ mặt bằng (Finding C, E). |
| 6 | `src/SelfStorageManagementSystem.WpfClient/Services/ApiClient.cs` | WpfClient | Bổ sung tham số `minAreaM2`, `maxAreaM2`; định dạng số thập phân qua `CultureInfo.InvariantCulture` (Finding E). |
| 7 | `src/SelfStorageManagementSystem.WpfClient/Views/FacilityCatalogWindow.xaml` | WpfClient | Bổ sung 2 ô nhập diện tích từ/đến (`txtFilterMinArea`, `txtFilterMaxArea`) trên thanh lọc tham số dùng chung (Finding E). |
| 8 | `src/SelfStorageManagementSystem.WpfClient/Views/FacilityCatalogWindow.xaml.cs` | WpfClient | Triển khai request sequence version tokens (Finding F); `ResetAllSelections()` khi đổi ngữ cảnh và chốt chặn `FacilityId` khi tiếp tục (Finding D); tải trọn vẹn sơ đồ và lọc cục bộ kèm nạp lại ComboBox khu vực (Finding A); mô tả trạng thái chi tiết theo BR-RSV-03 (Finding C). |
| 9 | `tests/SelfStorageManagementSystem.Tests/FacilityCatalogServiceTests.cs` | Tests | Cập nhật seed dữ liệu và bổ sung 3 unit tests mới: (1) loại trừ ô có type inactive hoặc không có giá, (2) loại kho hết sức chứa thì ô sơ đồ có `CanSelectToProceed = false`, (3) validate và lọc diện tích. |
| 10 | `tests/SelfStorageManagementSystem.Tests/CatalogIntegrationTests.cs` | Tests | Bổ sung integration test kiểm thử trực tiếp endpoint HTTP với bộ lọc diện tích và validation số âm. |
| 11 | `AgentReport/DoneFunction2.md` | Tài liệu báo cáo | Cập nhật số liệu kiểm thử và bổ sung Mục 9 ghi nhận chi tiết các cải tiến. |

---

## 3. Chi Tiết Khắc Phục Từng Finding & Bằng Chứng Kiểm Chứng

### 3.1. Finding A: Sửa lỗi sơ đồ bị trống và mất đồng bộ khu vực
- **Trước khi sửa**:
  ```csharp
  // Cũ: ComboBox chọn "Tất cả khu vực" có Id = 0, truyền trực tiếp lên API
  long? areaId = cboMapArea.SelectedValue is long aid ? aid : null;
  var response = await ApiClient.Instance.GetFacilityFloorMapAsync(_selectedFacility.Id, areaId, ...);
  ```
- **Sau khi sửa**:
  ```csharp
  // Mới: Luôn tải toàn bộ sơ đồ của cơ sở về cache
  var response = await ApiClient.Instance.GetFacilityFloorMapAsync(_selectedFacility.Id, areaId: null, startDate, endDate);
  _cachedFloorMap = response.Data;

  // Nạp lại ComboBox khu vực đúng cho cơ sở hiện tại
  var areaList = new List<FacilityAreaModel> { new() { Id = 0, Name = "Tất cả các khu vực" } };
  areaList.AddRange(_cachedFloorMap.Areas);
  cboMapArea.ItemsSource = areaList;
  cboMapArea.SelectedIndex = 0;

  // Lọc khu vực trực tiếp trên cache trong RenderFloorMap:
  var unitsToRender = selectedAreaId > 0
      ? _cachedFloorMap.Units.Where(u => u.AreaId == selectedAreaId).ToList()
      : _cachedFloorMap.Units;
  ```

### 3.2. Finding B: Sửa truy vấn `/units/available` loại trừ ô không có giá hoặc type ngừng hoạt động
- **Trước khi sửa**:
  Query chỉ lọc `maxMonthlyRate` nếu `maxMonthlyRate.HasValue`. Nếu không lọc giá, ô kho thuộc loại kho không có `facility_rate` hoặc loại kho `is_active = false` vẫn bị trả về.
- **Sau khi sửa**:
  Trong `FacilityCatalogRepository.cs`:
  ```csharp
  Where(u =>
      u.facility_id == facilityId &&
      u.unit_type.is_active &&
      u.is_listed &&
      u.physical_status == "available" &&
      !u.maintenance_work_orders.Any(m => m.blocks_booking && m.status != "completed" && m.status != "cancelled") &&
      !u.unit_allocations.Any(a => a.status == "active" && a.allocation_start_date < endDate && a.allocation_end_date > startDate) &&
      _context.facility_rates.Any(fr =>
          fr.facility_id == facilityId &&
          fr.unit_type_id == u.unit_type_id &&
          fr.valid_from <= startDate &&
          (fr.valid_to == null || startDate < fr.valid_to) &&
          (!maxMonthlyRate.HasValue || fr.monthly_rate <= maxMonthlyRate.Value)))
  ```
  *Bằng chứng kiểm chứng*: Unit test `GetAvailableUnits_ShouldExcludeUnits_WhenUnitTypeIsInactive_OrNoValidRate` đã kiểm tra và xác nhận loại trừ cả 2 trường hợp này.

### 3.3. Finding C: Đồng bộ tính khả dụng giữa Catalog, Danh Sách và Sơ Đồ theo BR-RSV-03
- **Trước khi sửa**:
  `/floor-map` gán `canSelectToProceed = true` cho mọi ô có `physical_status == "available"`. Nếu 1 ô vật lý trống nhưng có 1 reservation chưa gán ô (sức chứa loại kho = 0), sơ đồ vẫn cho tiếp tục đặt.
- **Sau khi sửa**:
  Trong `FacilityCatalogService.GetFacilityFloorMapAsync`:
  ```csharp
  var activeRates = await _catalogRepository.GetActiveFacilityRatesAsync(facilityId, startDate, ...);
  var unitTypeCapacityMap = new Dictionary<long, int>();
  foreach (var rate in activeRates)
  {
      var candidateUnits = await _catalogRepository.CountCandidateAvailableUnitsAsync(facilityId, rate.unit_type_id, startDate, endDate, cancellationToken);
      var unassignedReservations = await _catalogRepository.CountUnassignedActiveReservationsAsync(facilityId, rate.unit_type_id, startDate, endDate, nowUtc, cancellationToken);
      unitTypeCapacityMap[rate.unit_type_id] = Math.Max(0, candidateUnits - unassignedReservations);
  }

  // Đánh giá CanSelectToProceed:
  bool isTypeActive = u.unit_type?.is_active ?? false;
  bool hasValidRate = ratesMap.ContainsKey(u.unit_type_id);
  bool hasBookingCapacity = unitTypeCapacityMap.TryGetValue(u.unit_type_id, out var cap) && cap > 0;

  bool canSelectToProceed = (displayStatus == "available") && isTypeActive && hasValidRate && hasBookingCapacity;
  ```
  *Bằng chứng kiểm chứng*: Unit test `GetFacilityFloorMap_ShouldSetCanSelectToProceedFalse_WhenUnitTypeBookingCapacityIsZero` đã kiểm tra và xác nhận `CanSelectToProceed == false`.

### 3.4. Finding D: Làm sạch lựa chọn cũ khi đổi ngữ cảnh
- **Trước khi sửa**:
  Chọn cơ sở A -> chọn loại kho B -> bấm sang cơ sở C -> nút "Tiếp tục" vẫn giữ loại kho B của cơ sở A.
- **Sau khi sửa**:
  Hàm `ResetAllSelections()` được gọi tự động khi:
  1. Người dùng chọn cơ sở khác trong ListBox (`LstFacilities_SelectionChanged`).
  2. Người dùng đổi ngày thuê hoặc bấm nút áp dụng bộ lọc (`BtnApplyDateFilter_Click`).
  3. Chốt chặn trong `BtnProceedWithType_Click`:
     ```csharp
     if (_selectedUnitType.FacilityId != _selectedFacility.Id)
     {
         MessageBox.Show("Loại kho đã chọn không thuộc về cơ sở hiện tại. Vui lòng chọn lại.", "Cảnh báo", MessageBoxButton.OK, MessageBoxImage.Warning);
         ResetAllSelections();
         return;
     }
     ```

### 3.5. Finding E: Lọc diện tích và chuẩn hóa định dạng số thập phân
- **Trước khi sửa**:
  Thiếu `MinAreaM2` / `MaxAreaM2`. Chuỗi query `$"{maxPrice.Value}"` có thể sinh ra dấu phẩy `,` tùy theo locale máy tính.
- **Sau khi sửa**:
  - API Client:
    ```csharp
    if (minAreaM2.HasValue)
        queryParams.Add($"minAreaM2={minAreaM2.Value.ToString(System.Globalization.CultureInfo.InvariantCulture)}");
    if (maxAreaM2.HasValue)
        queryParams.Add($"maxAreaM2={maxAreaM2.Value.ToString(System.Globalization.CultureInfo.InvariantCulture)}");
    ```
  - Service: `ValidateFilterParameters` chặn số âm và chặn `minArea > maxArea`.
  - WPF Client: `txtFilterMinArea` và `txtFilterMaxArea` hỗ trợ người dùng nhập cả dấu chấm và dấu phẩy (`.` / `,`).

### 3.6. Finding F: Sequence Version Tokens ngăn ngừa Race Condition
- **Trước khi sửa**:
  Nhấp nhanh cơ sở 1 rồi sang cơ sở 2, kết quả mạng của cơ sở 1 về sau sẽ đè lên UI của cơ sở 2.
- **Sau khi sửa**:
  ```csharp
  var currentVersion = ++_unitTypesRequestVersion;
  var currentFacilityId = _selectedFacility.Id;

  var response = await ApiClient.Instance.GetFacilityUnitTypesAsync(...);

  // Hủy cập nhật nếu phiên bản không còn mới nhất hoặc cơ sở đã bị thay đổi
  if (currentVersion != _unitTypesRequestVersion || _selectedFacility?.Id != currentFacilityId) return;
  ```

---

## 4. Kết Quả Build và Test Thực Tế

### 4.1. Build Solution
```powershell
dotnet build SelfStorageManagementSystem.sln
```
- **Kết quả**: `Build succeeded` (0 Error, 0 Warning).

### 4.2. Test Suite
```powershell
dotnet test
```
- **Kết quả**: **Passed: 70, Failed: 0, Skipped: 0, Total: 70**.
  - **Chức năng 1**: 50/50 tests passed (Auth, RBAC, JWT, PasswordHasher, Facility Scope).
  - **Chức năng 2**: 20/20 tests passed (14 tests catalog unit & integration + 6 tests liên quan).

### 4.3. Git Status & Bảo Vệ Working Tree
```powershell
git status
```
- Nhánh: `Account&RoleManagement`.
- Toàn bộ thay đổi của Chức năng 1 và Chức năng 2 được bảo lưu đầy đủ; không có thao tác reset, checkout, commit hay push.

---

## 5. Sẵn Sàng Chuyển Tiếp Sang Chức Năng 3 (Reservation & Hold Unit)

- Bộ dữ liệu chuyển giao đã được đóng gói chuẩn mực từ Chức năng 2: `FacilityId`, `UnitTypeId`, `RentalStartDate`, `RentalEndDate`, giá thuê tháng niêm yết và tiền cọc.
- Hộp thoại tiếp tục từ cả Tab Loại Kho và Tab Sơ Đồ đều hiển thị rõ ràng quy tắc BR-RSV-03 để người dùng hiểu rằng họ đang đặt giữ chỗ theo **Loại kho & Diện tích**, việc gán ô kho cụ thể sẽ được thực hiện khi Check-in.
- Chức năng 3 có thể tự tin tiếp nhận bộ dữ liệu này để thực hiện logic giữ chỗ 15 phút, tính cọc và thanh toán.
