# Báo Cáo Thực Hiện Task T06: Service Nghiệp Vụ Quản Trị Ô Kho (StorageUnitAdminService)

- **Task ID**: T06 (kèm Task con T06-F02)
- **Tên Task**: Service nghiệp vụ quản trị ô kho (BLL Storage Unit Admin Service & Policy Enforcement)
- **Owner**: Gemini B (Quản trị Storage Units, Razor CRUD, SignalR Hub, SQL Tests)
- **Đợt**: 2
- **Chờ (Dependencies)**: T02 (**VERIFIED** tại [`AgentReport/WebMigration_T02.md`](file:///D:/FPT/PRN222/Project/Final_Project_Web/AgentReport/WebMigration_T02.md)), T03 (**VERIFIED** tại [`AgentReport/WebMigration_T03.md`](file:///D:/FPT/PRN222/Project/Final_Project_Web/AgentReport/WebMigration_T03.md))
- **Status**: **IMPLEMENTED** (Đã hoàn tất khắc phục Task T06-F02 và toàn bộ các finding trước đó; sẵn sàng chờ reviewer nghiệm thu)
- **Contract Version**: v0 / v1 (Baseline & Architecture Freeze tại [`docs/WebMigration_Contracts.md`](file:///D:/FPT/PRN222/Project/Final_Project_Web/docs/WebMigration_Contracts.md))
- **Thời điểm thực hiện**: 2026-10-15 (Hoàn tất ban đầu) | 2026-10-16 (Khắc phục Codex P1/P2, Re-Review P2, và Task T06-F02)
- **Môi trường**: Windows 11, .NET SDK 8.0, PowerShell 5.1

---

## 1. Khắc Phục Task T06-F02: MapToDetailDto Không Fallback Sang Giá Hết Hạn / Tương Lai

### 1.1. Vấn Đề (Bug Description)
Tại `StorageUnitAdminService.cs` (hàm `MapToDetailDto`), khi không tìm thấy biểu giá hiệu lực thỏa điều kiện thời gian, mã nguồn cũ sử dụng biểu thức fallback:
```csharp
monthlyRate = rate?.monthly_rate ?? unit.facility.facility_rates.FirstOrDefault(r => r.unit_type_id == unit.unit_type_id)?.monthly_rate ?? 0;
```
Biểu thức `FirstOrDefault(r => r.unit_type_id == unit.unit_type_id)` hoàn toàn bỏ qua kiểm tra `valid_from` và `valid_to`. Điều này làm endpoint chi tiết (`GetByIdAsync`) có thể trả về biểu giá đã hết hạn hoặc biểu giá tương lai chưa đến ngày hiệu lực, vi phạm quy tắc khoảng hiệu lực nửa mở `[valid_from, valid_to)`.

### 1.2. Biện Pháp Xử Lý
1. **Xóa hoàn toàn fallback không kiểm tra thời gian**: Thay thế bằng `monthlyRate = rate?.monthly_rate ?? 0m;`.
2. **Lọc chặt chẽ biểu giá thỏa mãn 4 điều kiện**:
   - `r.facility_id == unit.facility_id` (đúng cơ sở của ô kho).
   - `r.unit_type_id == unit.unit_type_id` (đúng loại ô kho).
   - `r.valid_from <= effectiveDate` (ngày bắt đầu hiệu lực nhỏ hơn hoặc bằng ngày địa phương của cơ sở).
   - `r.valid_to == null || effectiveDate < r.valid_to.Value` (ngày kết thúc là vô thời hạn hoặc lớn hơn ngày địa phương của cơ sở).
3. **Giữ nguyên contract**: `StorageUnitAdminDetailDto.MonthlyRate` là kiểu `decimal` (không null).
4. **Xử lý khi không có giá hiệu lực**: Trả về `0m`, tuyệt đối không lấy giá hết hạn hoặc giá tương lai.
5. **Thứ tự ưu tiên**: `OrderByDescending(r => r.valid_from).ThenByDescending(r => r.id)`.
6. **Thời gian & múi giờ**: Sử dụng `_timeProvider.GetUtcNow()` đã inject kết hợp với `ResolveTimeZone(unit.facility.timezone)`.
7. **Bảo đảm nạp dữ liệu ở Repository**: Bổ sung `.ThenInclude(f => f.facility_rates)` trong `StorageUnitAdminRepository.GetStorageUnitByIdAsync(includeDetails: true)` để collection `facility_rates` được nạp đầy đủ từ database.

---

## 2. Tổng Hợp Các Lần Xử Lý Trước Đó

### 2.1. Re-Review Đợt 2 (Timezone & Half-Open Interval trong GetLookupsAsync)
- **Finding 1 (P2)**: `GetLookupsAsync` dùng trực tiếp `DateTime.UtcNow`. Đã chuyển sang tính ngày địa phương theo `targetFacility.timezone` thông qua `TimeProvider` (đăng ký `TimeProvider.System` trong DI). Giữ `MonthlyRate = null` cho lookup toàn cục (`facilityId = null`).
- **Finding 2 (P2)**: Biên `valid_to` trước đó là `>= today` (inclusive). Đã chuyển sang khoảng nửa mở `effectiveDate < valid_to`. Khi `valid_to == today` thì biểu giá được tính là hết hạn.

### 2.2. Codex Round 1 (P1 & P2)
- **Finding 1 (P1)**: Terminated employee vẫn quản trị ô kho. Đã bổ sung kiểm tra `EmploymentStatus == Active` ở mọi entry point, không ngoại lệ cho System Admin hay Ops Manager.
- **Finding 2 (P2)**: `MonthlyRate` trong lookup luôn trả 0. Đã sửa eager loading và chuyển `StorageUnitTypeLookupDto.MonthlyRate` sang `decimal?`.

---

## 3. Danh Sách Tập Tin Sửa Đổi

1. [`src/SelfStorageManagementSystem.BusinessLogic/Services/Implementations/StorageUnitAdminService.cs`](file:///D:/FPT/PRN222/Project/Final_Project_Web/src/SelfStorageManagementSystem.BusinessLogic/Services/Implementations/StorageUnitAdminService.cs):
   - Chuẩn hóa `MapToDetailDto`: xóa fallback unvalidated rate, lọc chính xác theo facility/unit_type/local_date, gán `0m` khi không có rate hiệu lực.
2. [`src/SelfStorageManagementSystem.DataAccess/Repositories/Implementations/StorageUnitAdminRepository.cs`](file:///D:/FPT/PRN222/Project/Final_Project_Web/src/SelfStorageManagementSystem.DataAccess/Repositories/Implementations/StorageUnitAdminRepository.cs):
   - Bổ sung `.ThenInclude(f => f.facility_rates)` cho `GetStorageUnitByIdAsync(includeDetails: true)`.
3. [`tests/SelfStorageManagementSystem.WebTests/StorageUnitAdminServiceTests.cs`](file:///D:/FPT/PRN222/Project/Final_Project_Web/tests/SelfStorageManagementSystem.WebTests/StorageUnitAdminServiceTests.cs):
   - Bổ sung 4 unit test cases bắt buộc cho Task T06-F02.
4. **Đồng bộ mã nguồn**:
   - Toàn bộ các thay đổi trên được đồng bộ 100% sang repository gốc [`D:\FPT\PRN222\Project\Final_Project`](file:///D:/FPT/PRN222/Project/Final_Project).

---

## 4. Chi Tiết Các Unit Test Cho Task T06-F02

| STT | Tên Test Case | Mục Tiêu Kiểm Tra | Kết Quả |
| :---: | :--- | :--- | :---: |
| 1 | `GetByIdAsync_RateWithValidToEqualLocalToday_ReturnsZero` | Biểu giá có `valid_to` bằng ngày địa phương của facility bị coi là hết hạn theo khoảng nửa mở `[valid_from, valid_to)`, trả về `0m` | **PASS** |
| 2 | `GetByIdAsync_FutureRateOnly_ReturnsZero` | Khi cơ sở chỉ có biểu giá bắt đầu trong tương lai (`valid_from > today`), không fallback lấy giá này mà trả về `0m` | **PASS** |
| 3 | `GetByIdAsync_MultipleEffectiveRates_SelectsLatestValidFrom` | Khi có nhiều biểu giá đang hiệu lực, chọn biểu giá có `valid_from` mới nhất | **PASS** |
| 4 | `GetByIdAsync_FacilityTimezoneAheadOfUtc_UsesLocalDate` | Khi UTC còn ở ngày hôm trước nhưng múi giờ địa phương (`Asia/Ho_Chi_Minh` UTC+7) đã sang ngày mới, áp dụng đúng ngày địa phương để chọn biểu giá mới và loại biểu giá cũ đã hết hạn | **PASS** |

---

## 5. Kết Quả Kiểm Thử Toàn Diện & Hồi Quy

### 5.1. Solution Web (`SelfStorageManagementSystem.Web.sln`)
Lệnh thực thi: `dotnet test .\SelfStorageManagementSystem.Web.sln`
- **Failed**: 0
- **Passed**: 167 (tăng từ 163 lên 167 sau khi bổ sung 4 test cases của T06-F02)
- **Skipped**: 0
- **Total**: 167
- **Thời gian chạy**: ~2 giây

### 5.2. Kiểm thử Hồi quy trên Baseline (`SelfStorageManagementSystem.sln`)
Lệnh thực thi: `dotnet test D:\FPT\PRN222\Project\Final_Project\SelfStorageManagementSystem.sln`
- **Failed**: 0
- **Passed**: 208
- **Skipped**: 8 (Các test concurrency SQL Server yêu cầu database SQL Server vật lý)
- **Total**: 216
- **Thời gian chạy**: ~2 giây

---

## 6. Trạng Thái Bàn Giao

Theo đúng quy định của Task T06-F02:
- **Trạng thái Task T06**: **IMPLEMENTED** (Không tự ý đánh dấu VERIFIED, chờ reviewer nghiệm thu độc lập).
- Sẵn sàng bàn giao cho reviewer kiểm tra.
