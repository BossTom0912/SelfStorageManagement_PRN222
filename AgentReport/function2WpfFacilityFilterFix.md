# Báo Cáo Sửa Lỗi Draft Filter Khi Đổi Cơ Sở (WPF Function 2)

- **Dự án**: Self-Storage Facility Rental and Management System (PRN222)
- **Thời gian hoàn thành**: 06/10/2026
- **Branch**: `function-2-dang-lam`
- **File sửa**: `src/SelfStorageManagementSystem.WpfClient/Views/FacilityCatalogWindow.xaml.cs`
- **Kiến trúc áp dụng**: ASP.NET Core Web API + WPF (.NET 8, EF Core Database-First, SQL Server)
- **Quy tắc nghiệp vụ liên quan**: BR-OPS-02, BR-RSV-03

---

## 1. Tóm Tắt Vấn Đề & Biểu Thức Gây Lỗi Trước Khi Sửa

### 1.1. Hiện tượng & Đường đi phát sinh lỗi
1. Người dùng mở WPF Client, chọn Cơ sở A.
2. Người dùng nhập "Giá tối đa" = `500.000` (hoặc sửa ngày, diện tích) nhưng **KHÔNG** bấm "Áp dụng lọc".
   - `OnFilterInputChanged()` được kích hoạt và gán `_hasUnappliedFilterChanges = true`.
   - Snapshot bộ lọc đang áp dụng (`_appliedFilter.MaxPrice`) vẫn là giá trị cũ (`null`).
3. Người dùng click chọn Cơ sở B trên danh sách `lstFacilities`:
   - Sự kiện `LstFacilities_SelectionChanged()` kích hoạt.
   - **Biểu thức gây lỗi (trước đây tại dòng 269)**:
     ```csharp
     _hasUnappliedFilterChanges = false;
     ```
     Dòng lệnh này đã xóa mất cờ báo draft filter chưa áp dụng.
4. Handler tiếp tục gọi `RefreshSelectedFacilityDataAsync()`:
   - Do cờ `_hasUnappliedFilterChanges` đã bị xóa về `false` và `RefreshSelectedFacilityDataAsync()` không chặn kiểm tra, hệ thống lập tức gọi `LoadUnitTypesAsync()`.
5. `LoadUnitTypesAsync()` lấy tiêu chí lọc từ `_appliedFilter` (vốn vẫn là `MaxPrice = null`), bỏ qua hoàn toàn giá trị `500.000` đang hiển thị trên ô nhập giao diện:
   - Request API trả về danh sách loại kho của Cơ sở B có giá > 500.000đ.
6. Khi người dùng click chọn một loại kho có giá > 500.000đ:
   - `GridUnitTypes_SelectionChanged()` thấy `_hasUnappliedFilterChanges == false` và context version hợp lệ nên bật sáng nút **"Tiếp tục"** (`btnProceedWithType.IsEnabled = true`).
   - Bấm "Tiếp tục" mở dialog đặt chỗ với loại kho có giá > 500.000đ, mâu thuẫn trực tiếp với bộ lọc 500.000đ đang hiển thị trên màn hình.

---

## 2. Bảng Theo Dõi Giá Trị Biến Tại Các Điểm Dừng (Breakpoints)

| Điểm kiểm tra (Breakpoint) | Biến / Thuộc tính | Giá trị TRƯỚC khi sửa | Giá trị SAU khi sửa |
| :--- | :--- | :--- | :--- |
| **1. Nhập `500.000` tại Cơ sở A** (`OnFilterInputChanged`) | `txtFilterMaxPrice.Text`<br>`_hasUnappliedFilterChanges`<br>`_appliedFilter.MaxPrice`<br>`_selectedFacility.Id`<br>`_catalogContextVersion` | `"500.000"`<br>`true`<br>`null`<br>`1` (Cơ sở A)<br>`V` | `"500.000"`<br>`true`<br>`null`<br>`1` (Cơ sở A)<br>`V` |
| **2. Chọn Cơ sở B** (`LstFacilities_SelectionChanged`) | `_selectedFacility.Id`<br>`_catalogContextVersion`<br>`_hasUnappliedFilterChanges` | `2` (Cơ sở B)<br>`V + 1`<br>**`false` (bị xóa sai)** | `2` (Cơ sở B)<br>`V + 1`<br>**`true` (bảo toàn draft filter)** |
| **3. Điều phối tải dữ liệu** (`RefreshSelectedFacilityDataAsync`) | Hành vi điều hướng | Chạy tiếp xuống gọi `LoadUnitTypesAsync()` | Phát hiện `_hasUnappliedFilterChanges == true`:<br>- Cập nhật thông báo nhắc bấm "Áp dụng lọc".<br>- Gọi `ResetAllSelections()`.<br>- **Return ngay, không gọi loader.** |
| **4. Loader con** (`LoadUnitTypesAsync`) | Request API gửi đi | Gửi request với `maxPrice = null` | **Không được gọi** (chặn từ `RefreshSelectedFacilityDataAsync` và guard phòng thủ loader). |
| **5. Danh sách & Nút Tiếp tục** (`GridUnitTypes_SelectionChanged`) | `gridUnitTypes.ItemsSource`<br>`btnProceedWithType.IsEnabled` | Có danh sách loại kho > 500k<br>**`true` (Bật sáng sai quy tắc)** | Bị xóa sạch (`null`)<br>**`false` (Khóa chặt)** |
| **6. Bấm "Áp dụng lọc" tại Cơ sở B** (`BtnApplyDateFilter_Click`) | `_appliedFilter.MaxPrice`<br>`_hasUnappliedFilterChanges`<br>`_catalogContextVersion` | (Lỗi sai lệch đã xảy ra trước đó) | `500000m`<br>`false`<br>`V + 2`<br>-> Gọi `RefreshSelectedFacilityDataAsync()`, tải đúng loại kho <= 500k cho Cơ sở B. |

---

## 3. Các Thay Đổi Cụ Thể Trong Mã Nguồn

File: `src/SelfStorageManagementSystem.WpfClient/Views/FacilityCatalogWindow.xaml.cs`

1. **Loại bỏ việc xóa cờ draft filter khi đổi cơ sở trong `LstFacilities_SelectionChanged()`**:
   - Xóa bỏ dòng `_hasUnappliedFilterChanges = false;`.
   - Giữ nguyên việc cập nhật `_selectedFacility = facility`, tăng `_catalogContextVersion++`, dọn dẹp các cache/UI cũ (`gridUnitTypes.ItemsSource = null`, `gridAvailableUnits.ItemsSource = null`, `canvasFloorMap.Children.Clear()`).
   - Giữ nguyên nội dung người dùng đã nhập trên các TextBox (`txtFilterMaxPrice`, `txtFilterMinArea`, `txtFilterMaxArea`), không tự tiện xóa text.
2. **Chặn tập trung tại `RefreshSelectedFacilityDataAsync()`**:
   ```csharp
   if (_hasUnappliedFilterChanges)
   {
       lblStatusMessage.Text = "Bộ lọc đang có thay đổi chưa áp dụng. Vui lòng bấm 'Áp dụng lọc' để tải dữ liệu cho cơ sở này.";
       ResetAllSelections();
       return;
   }
   ```
3. **Phòng thủ đa lớp (Defense-in-depth) trong các hàm nạp dữ liệu**:
   - Thêm guard `if (_selectedFacility == null || _hasUnappliedFilterChanges) return;` vào cả 3 phương thức:
     - `LoadUnitTypesAsync()`
     - `LoadAvailableUnitsAsync()`
     - `LoadFloorMapAsync()`
4. **Bảo toàn cơ chế chuyển tab & apply**:
   - `TabCatalog_SelectionChanged`: Đã có chốt chặn nếu `_hasUnappliedFilterChanges == true` thì từ chối tải ngầm bằng snapshot cũ.
   - `BtnApplyDateFilter_Click`: Khi bấm Apply, nếu validation thất bại thì cờ `_hasUnappliedFilterChanges` vẫn giữ `true`, nút Tiếp tục tiếp tục bị khóa. Chỉ khi validation thành công, `_appliedFilter` mới được cập nhật từ UI và `_hasUnappliedFilterChanges = false`.

---

## 4. Kết Quả Kiểm Chứng 5 Kịch Bản Nghiệp Vụ

1. **Nhập `500.000` ở Cơ sở A, chưa Apply, chuyển Cơ sở B:**
   - Sau khi đổi sang Cơ sở B, `_hasUnappliedFilterChanges` vẫn là `true`.
   - `RefreshSelectedFacilityDataAsync` dừng lại, không gọi API với filter cũ.
   - DataGrid loại kho trống, `lblStatusMessage` nhắc người dùng: *"Bộ lọc đang có thay đổi chưa áp dụng. Vui lòng bấm 'Áp dụng lọc' để tải dữ liệu cho cơ sở này."*
   - Nút "Tiếp tục" bị vô hiệu hóa hoàn toàn (`IsEnabled = false`).
2. **Bấm "Áp dụng lọc" tại Cơ sở B:**
   - `ParseVndPrice("500.000")` trả về `500000m`.
   - `_appliedFilter` được cập nhật với `MaxPrice = 500000m`, `_hasUnappliedFilterChanges = false`, `_catalogContextVersion` tăng lên.
   - Dữ liệu Cơ sở B được tải về qua API với bộ lọc `maxPrice = 500000`.
   - Chọn loại kho hợp lệ thì nút "Tiếp tục" được bật sáng; bấm "Tiếp tục" hiển thị đúng dữ liệu đã áp dụng.
3. **Nhập giá sai định dạng/âm (ví dụ: `abc` hoặc `-1000`) rồi chuyển Cơ sở B, bấm Apply:**
   - Khi bấm Apply, `ParseVndPrice` trả về lỗi validation.
   - Hiển thị MessageBox cảnh báo lỗi định dạng giá.
   - `_hasUnappliedFilterChanges` tiếp tục là `true`.
   - Dữ liệu không được tải về, nút "Tiếp tục" vẫn khóa.
4. **Đổi cơ sở khi KHÔNG có draft filter chưa Apply (`_hasUnappliedFilterChanges == false`):**
   - Khi không có draft chưa apply, chuyển sang cơ sở khác thì `RefreshSelectedFacilityDataAsync` chạy bình thường và tải dữ liệu cho cơ sở mới bằng bộ lọc đã áp dụng trước đó.
5. **Đổi tab khi có draft filter chưa Apply:**
   - Tại `TabCatalog_SelectionChanged`, nhánh kiểm tra `_hasUnappliedFilterChanges` chặn tải dữ liệu cho Tab 1 / Tab 2 bằng snapshot cũ, đồng thời làm mới selection để tránh sai lệch dữ liệu.

---

## 5. Kết Quả Build và Test

- **Lệnh build**:
  ```powershell
  dotnet build SelfStorageManagementSystem.sln --no-restore
  ```
  **Kết quả**: `Build succeeded. 11 Warning(s) (CS8981 từ scaffold entity), 0 Error(s).`
- **Lệnh test**:
  ```powershell
  dotnet test SelfStorageManagementSystem.sln --no-restore
  ```
  **Kết quả**: `Passed! - Failed: 0, Passed: 94, Skipped: 0, Total: 94 (100% Passed).`

---

## 6. Git Diff Chi Tiết

```diff
diff --git a/src/SelfStorageManagementSystem.WpfClient/Views/FacilityCatalogWindow.xaml.cs b/src/SelfStorageManagementSystem.WpfClient/Views/FacilityCatalogWindow.xaml.cs
index c1f50e9..c25f6b6 100644
--- a/src/SelfStorageManagementSystem.WpfClient/Views/FacilityCatalogWindow.xaml.cs
+++ b/src/SelfStorageManagementSystem.WpfClient/Views/FacilityCatalogWindow.xaml.cs
@@ -266,7 +266,6 @@ public partial class FacilityCatalogWindow : Window
         _loadedUnitTypesContextVersion = -1;
         _loadedAvailableUnitsContextVersion = -1;
         _loadedFloorMapContextVersion = -1;
-        _hasUnappliedFilterChanges = false;
         panelNoFacility.Visibility = Visibility.Collapsed;
 
         txtSelectedFacilityTitle.Text = $"{facility.Name} ({facility.Code})";
@@ -289,6 +288,13 @@ public partial class FacilityCatalogWindow : Window
     {
         if (_selectedFacility == null) return;
 
+        if (_hasUnappliedFilterChanges)
+        {
+            lblStatusMessage.Text = "Bộ lọc đang có thay đổi chưa áp dụng. Vui lòng bấm 'Áp dụng lọc' để tải dữ liệu cho cơ sở này.";
+            ResetAllSelections();
+            return;
+        }
+
         var selectedTab = tabCatalog.SelectedIndex;
         if (selectedTab == 0)
         {
@@ -306,7 +312,7 @@ public partial class FacilityCatalogWindow : Window
 
     private async Task LoadUnitTypesAsync()
     {
-        if (_selectedFacility == null) return;
+        if (_selectedFacility == null || _hasUnappliedFilterChanges) return;
 
         var contextVersion = _catalogContextVersion;
         var currentVersion = ++_unitTypesRequestVersion;
@@ -345,7 +351,7 @@ public partial class FacilityCatalogWindow : Window
 
     private async Task LoadAvailableUnitsAsync()
     {
-        if (_selectedFacility == null) return;
+        if (_selectedFacility == null || _hasUnappliedFilterChanges) return;
 
         var contextVersion = _catalogContextVersion;
         var currentVersion = ++_availableUnitsRequestVersion;
@@ -392,7 +398,7 @@ public partial class FacilityCatalogWindow : Window
 
     private async Task LoadFloorMapAsync()
     {
-        if (_selectedFacility == null) return;
+        if (_selectedFacility == null || _hasUnappliedFilterChanges) return;
 
         var contextVersion = _catalogContextVersion;
         var currentVersion = ++_floorMapRequestVersion;
```
