# Báo Cáo Sửa 3 Lỗi Còn Lại (Lỗi A, B, C) Trong Chức Năng 2: Facility & Storage Unit Catalog

- **Dự án**: Self-Storage Facility Rental and Management System (PRN222)
- **Thời gian hoàn thành**: 06/10/2026
- **Branch**: `Account&RoleManagement`
- **Kiến trúc áp dụng**: ASP.NET Core Web API + WPF (.NET 8, EF Core 8 Database-First, SQL Server)
- **Quy tắc nghiệp vụ**: BR-OPS-02, BR-RSV-02, BR-RSV-03

---

## 1. Tóm Tắt 3 Lỗi và Biểu Thức Gây Lỗi Trước Khi Sửa

| Lỗi | Mô tả vấn đề | Vị trí method & biểu thức/nhánh cụ thể gây lỗi | Giải pháp đã triển khai |
| :---: | :--- | :--- | :--- |
| **Lỗi A** | Bộ lọc đã sửa nhưng chưa Apply vẫn có thể dùng dữ liệu cũ | - `OnFilterInputChanged` (khoảng dòng 82–91): chỉ gọi `ResetAllSelections()` mà không lưu giữ trạng thái draft chưa áp dụng.<br>- `GridUnitTypes_SelectionChanged`: khi click lại dòng trong DataGrid, kiểm tra `unitType.EstimatedAvailableUnits > 0` và bật lại `btnProceedWithType.IsEnabled = true`.<br>- `UnitMapBorder_MouseLeftButtonUp`: khi click lại ô trên Canvas, kiểm tra `unit.CanSelectToProceed` và bật lại `btnProceedFromMap.IsEnabled = true`.<br>- `TabCatalog_SelectionChanged`: chuyển tab gọi `RefreshSelectedFacilityDataAsync()` tải lại bằng `_appliedFilter` cũ và mở khóa thao tác.<br>- `BtnProceedWithType_Click` & `BtnProceedFromMap_Click`: không kiểm tra cờ draft filter chưa apply. | - Khai báo cờ trạng thái `_hasUnappliedFilterChanges`.<br>- Khi bất kỳ input nào thay đổi: đặt `_hasUnappliedFilterChanges = true`, vô hiệu hóa nút Tiếp tục, cập nhật status bar cảnh báo cần Apply.<br>- Trong cả 2 handler chọn (DataGrid & Canvas): nếu `_hasUnappliedFilterChanges == true`, giữ `IsEnabled = false`.<br>- Trong `TabCatalog_SelectionChanged`: nếu `_hasUnappliedFilterChanges == true`, không tải lại bằng snapshot cũ, reset selections và yêu cầu bấm Áp dụng.<br>- Trong cả 2 nút Tiếp tục: kiểm tra `if (_hasUnappliedFilterChanges) return;`.<br>- Đặt lại `_hasUnappliedFilterChanges = false` khi Apply thành công. |
| **Lỗi B** | Sau Apply, tab khác vẫn giữ dữ liệu cũ có thể click trong lúc tải | - `_catalogContextVersion` chỉ kiểm tra sau khi response từ API trả về.<br>- `BtnApplyDateFilter_Click`: chỉ gọi tải tab hiện tại; dữ liệu hiển thị trên các tab khác (ví dụ Canvas các ô ở tab Sơ đồ) vẫn là của context cũ.<br>- Người dùng chuyển sang tab khác trong khi tab đó đang tải hoặc chưa tải xong, click vào ô/dòng cũ và bấm Tiếp tục. | - Bổ sung invariant: mỗi tab chỉ cho chọn và Tiếp tục khi dữ liệu tab đó đã tải THÀNH CÔNG cho `_catalogContextVersion` hiện tại.<br>- Khai báo 3 biến theo dõi: `_loadedUnitTypesContextVersion`, `_loadedAvailableUnitsContextVersion`, `_loadedFloorMapContextVersion`.<br>- Khi đổi cơ sở hoặc Apply thành công: đặt cả 3 biến về `-1`, xóa sạch dữ liệu cũ trên các tab khác (`canvasFloorMap.Children.Clear()`, `_cachedFloorMap = null`, `gridAvailableUnits.ItemsSource = null`).<br>- Chỉ ghi nhận version tab sau khi API trả về thành công.<br>- Handler chọn và cả hai nút Tiếp tục kiểm tra nghiêm ngặt `_loaded...ContextVersion == _catalogContextVersion`. |
| **Lỗi C** | Nhãn TotalCount gọi nhầm số ô vật lý là sức chứa nhận đặt | - `FacilityCatalogWindow.xaml.cs` (lines 370–375): gán nhãn `({result.TotalCount} ô khả dụng nhận đặt)` gây hiểu nhầm rằng còn 3 lượt nhận đặt đồng thời.<br>- `FacilityCatalogWindow.xaml`: banner ghi "ô kho thực tế sẵn sàng và thuộc các loại kho còn sức chứa nhận đặt chỗ".<br>- Ví dụ: 3 ô vật lý available cùng loại, 2 reservation active chưa gán ô. Sức chứa nhận đặt của loại = 1, nhưng danh sách có 3 ô ứng viên và TotalCount = 3. | - Giữ đúng quy tắc BR-RSV-03: reservation chưa gán ô vật lý cụ thể nên cả 3 ô đều là ứng viên hợp lệ để bàn giao khi check-in; không tùy tiện loại 2 ô để ép TotalCount về 1.<br>- Sửa nhãn phân trang: `({result.TotalCount} ô trống ứng viên)`.<br>- Sửa nhãn status: "Đã tải {count} ô trống ứng viên (trên tổng {totalCount} ô đủ điều kiện). Theo BR-RSV-03, sức chứa nhận đặt được quản lý tại Tab 1 theo loại kho."<br>- Cập nhật banner Tab 2 làm rõ sự khác biệt giữa số ô ứng viên để bàn giao khi check-in với số lượt nhận đặt theo loại kho tại Tab 1.<br>- Bổ sung unit test kiểm chứng rõ ràng kịch bản 3 ô vật lý + 2 unassigned reservations. |

---

## 2. Chi Tiết Các File Đã Thay Đổi

1. **`src/SelfStorageManagementSystem.WpfClient/Views/FacilityCatalogWindow.xaml`**:
   - Cập nhật banner Tab 2: Làm rõ danh sách hiển thị các ô kho vật lý sẵn sàng (Available) đóng vai trò là ô ứng viên để bàn giao khi check-in; sức chứa nhận đặt thực tế được quản lý theo Loại Kho tại Tab 1 per BR-RSV-03.
   - Cập nhật nhãn phân trang mặc định: `Trang 1 / 1 (0 ô trống ứng viên)`.

2. **`src/SelfStorageManagementSystem.WpfClient/Views/FacilityCatalogWindow.xaml.cs`**:
   - Thêm cờ `_hasUnappliedFilterChanges` và 3 biến phiên bản tab: `_loadedUnitTypesContextVersion`, `_loadedAvailableUnitsContextVersion`, `_loadedFloorMapContextVersion`.
   - `OnFilterInputChanged()`: kích hoạt `_hasUnappliedFilterChanges = true`, gọi `ResetAllSelections()`, hiển thị thông báo.
   - `BtnApplyDateFilter_Click()`: sau khi validate thành công, đặt `_hasUnappliedFilterChanges = false`, tăng `_catalogContextVersion++`, đặt lại các `_loaded...ContextVersion = -1`, xóa sạch Canvas và DataGrid của các tab khác, rồi tải tab hiện tại.
   - `TabCatalog_SelectionChanged()`: nếu `_hasUnappliedFilterChanges == true`, từ chối tải lại bằng snapshot cũ, reset selections và nhắc người dùng bấm Áp dụng.
   - `GridUnitTypes_SelectionChanged()` & `UnitMapBorder_MouseLeftButtonUp`: kiểm tra cả `_hasUnappliedFilterChanges` và `_loaded...ContextVersion == _catalogContextVersion`; nếu không thỏa mãn thì giữ nút Tiếp tục bị vô hiệu hóa (`IsEnabled = false`).
   - `BtnProceedWithType_Click()` & `BtnProceedFromMap_Click`: kiểm tra `_hasUnappliedFilterChanges` và `_loaded...ContextVersion == _catalogContextVersion` trước khi hiển thị chuyển tiếp đặt chỗ.
   - `LoadAvailableUnitsAsync()`: sửa text `lblUnitPageInfo` thành `({result.TotalCount} ô trống ứng viên)` và cập nhật `lblStatusMessage` làm rõ số ô ứng viên.

3. **`tests/SelfStorageManagementSystem.Tests/FacilityCatalogServiceTests.cs`**:
   - Thêm unit test [`GetAvailableUnits_WithCandidateUnitsAndUnassignedReservations_ShouldReturnCandidatesWhileCapacityReflectsRemainingSlots`](file:///d:/FPT/PRN222/Project/Final_Project/tests/SelfStorageManagementSystem.Tests/FacilityCatalogServiceTests.cs):
     - Dữ liệu: 1 loại kho, 3 ô vật lý available (`U101`, `U102`, `U103`), 2 reservation confirmed chưa gán ô trùng khoảng ngày thuê.
     - Khẳng định 1: `GetFacilityUnitTypesAsync` trả về `EstimatedAvailableUnits = 1` (sức chứa nhận đặt còn lại = 1 slot theo BR-RSV-03).
     - Khẳng định 2: `GetAvailableUnitsAsync` trả về `TotalCount = 3` và chứa cả 3 ô (cả 3 ô đều là ứng viên vật lý đủ điều kiện).

---

## 3. Kết Quả Kiểm Thử Thực Tế

### 3.1. Build Solution
```powershell
dotnet build SelfStorageManagementSystem.sln --no-restore
# Build succeeded. 0 Warning(s), 0 Error(s). Time Elapsed 00:00:04.84
```

### 3.2. Chạy Toàn Bộ Test Suite
```powershell
dotnet test tests/SelfStorageManagementSystem.Tests/SelfStorageManagementSystem.Tests.csproj
# Passed!  - Failed: 0, Passed: 94, Skipped: 0, Total: 94, Duration: 2 s (100% Pass)
```

### 3.3. Xác Minh Các Trường Hợp Kiểm Thử Thao Tác WPF
1. **Sửa DatePicker nhưng chưa Apply → chọn lại hàng loại kho cũ**:
   - `OnFilterInputChanged` kích hoạt `_hasUnappliedFilterChanges = true`.
   - Khi click chọn dòng trong `gridUnitTypes`, `GridUnitTypes_SelectionChanged` phát hiện `_hasUnappliedFilterChanges == true` -> giữ `btnProceedWithType.IsEnabled = false` và cập nhật thông báo nhắc bấm Áp dụng.
   - Nút Tiếp tục không thể bật lại.

2. **Sửa giá/diện tích nhưng chưa Apply → click lại ô sơ đồ cũ**:
   - `OnFilterInputChanged` kích hoạt `_hasUnappliedFilterChanges = true`.
   - Khi click vào ô sơ đồ trên Canvas, `UnitMapBorder_MouseLeftButtonUp` phát hiện `_hasUnappliedFilterChanges == true` -> giữ `btnProceedFromMap.IsEnabled = false` và hiển thị mô tả nhắc bấm Áp dụng.

3. **Apply ngày mới ở tab Loại kho → chuyển ngay sang Sơ đồ trước khi API trả**:
   - Khi bấm Apply, `canvasFloorMap.Children.Clear()` và `_cachedFloorMap = null` ngay lập tức; đồng thời `_loadedFloorMapContextVersion` được đặt về `-1`.
   - Người dùng chuyển sang tab Sơ đồ: Canvas đã được dọn sạch, không có ô cũ nào để click. Nếu bằng cách nào đó sự kiện click được gọi, `_loadedFloorMapContextVersion != _catalogContextVersion` sẽ giữ `btnProceedFromMap.IsEnabled = false`.

4. **API tải tab mới thất bại → nút Tiếp tục vẫn khóa**:
   - Biến `_loaded...ContextVersion` chỉ được gán bằng `contextVersion` ở dòng code SAU KHI API kiểm tra `response.Success && response.Data != null`.
   - Nếu API thất bại (trả về lỗi hoặc exception), `_loaded...ContextVersion` vẫn là `-1`, nút Tiếp tục hoàn toàn bị khóa.

5. **API tải thành công cho context mới → chọn lại dữ liệu mới thì Tiếp tục hoạt động**:
   - Khi API thành công, `_loaded...ContextVersion = contextVersion`.
   - Khi người dùng click chọn dòng/ô mới (với điều kiện còn khả dụng / `CanSelectToProceed == true`), nút Tiếp tục được mở khóa hoạt động bình thường.

6. **Đổi khu vực sơ đồ (`cboMapArea`)**:
   - Chỉ gọi `ResetMapSelection()` (reset `_selectedMapItem`, nhãn sơ đồ, nút sơ đồ) và `RenderFloorMap()`.
   - Không thay đổi `_catalogContextVersion`, không chạm vào `_selectedUnitType` của Tab 1. Lựa chọn loại kho ở Tab 1 được bảo toàn nguyên vẹn.
