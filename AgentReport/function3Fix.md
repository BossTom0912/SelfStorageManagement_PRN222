# Báo Cáo Sửa Lỗi Hoàn Thiện Chức Năng 3: Reservation & 15-Minute Hold

- **Dự án**: Self-Storage Facility Rental and Management System (PRN222)
- **Thời gian hoàn thành**: 07/10/2026
- **Branch làm việc**: `Reservation-and-Hold-Unit` (bắt đầu từ commit `7b1f411`)
- **Kiến trúc áp dụng**: Strict 3-Layer Architecture + WPF Client (.NET 8, ASP.NET Core Web API, EF Core 8 Database-First, SQL Server `core` schema)
- **Tình trạng kiểm thử**: **124 Passed, 1 Skipped (Opt-in SQL Integration), 0 Failed, 0 Warnings CS, 0 Build Errors**

---

## 1. Tổng Hợp Các Lỗi Review Đã Xử Lý

| STT | Vấn đề phát hiện | Mức độ | Hiện trạng lỗi trước khi sửa | Giải pháp & Thay đổi đã hoàn tất | Test chứng minh |
| :---: | :--- | :---: | :--- | :--- | :--- |
| **1** | **API hủy đặt chỗ trả trạng thái cũ do EF Core tracking sau `ExecuteUpdateAsync`** (`ReservationService.cs`, `ReservationRepository.cs`) | **Critical** | Entity reservation được nạp có tracking. Repo chạy `ExecuteUpdateAsync` đổi `status = 'cancelled'` trực tiếp xuống DB. Khi nạp lại entity trả về, EF Core Identity Map trả lại instance `pending` trong bộ nhớ, dẫn tới HTTP 200 trả sai `status`, `canCancel` và `hold_until`. | Thêm `.AsNoTracking()` vào `GetByIdWithDetailsAsync`. Trong `CancelReservationAsync`, tách rời (`Detach`) entity đang theo dõi khỏi Change Tracker trước khi nạp lại bằng untracked query với đầy đủ Include navigation. | `CancelReservation_ReturnsFreshCancelledState_EvenWhenEntityWasTrackedAsPending` |
| **2** | **GET chi tiết trả trạng thái `pending` khi đơn được xác nhận đồng thời** (`ReservationService.cs`, `ReservationRepository.cs`) | **High** | Đơn quá hạn, `TryExpireSingleReservationIfOverdueAsync` trả `false` do tiến trình khác vừa chuyển đơn sang `confirmed`. Service nạp lại entity bằng context đang theo dõi `pending`, trả sai trạng thái cũ cho client. | Đọc lại bản ghi bằng truy vấn không theo dõi `GetByIdWithDetailsAsync(id)` (đã có `.AsNoTracking()`), đảm bảo phản ánh trạng thái `confirmed` mới nhất đã commit từ DB. | `GetReservationById_WhenConcurrentConfirmationOccurs_ReturnsFreshConfirmedStatus` |
| **3** | **Nút Tiếp tục từ Floor Map dùng giá cũ hoặc bị race condition khi context đổi trong lúc `await`** (`FacilityCatalogWindow.xaml.cs`) | **High** | Bấm Tiếp tục từ Floor Map gọi `await GetFacilityUnitTypesAsync`. Trong lúc chờ, người dùng có thể đổi cơ sở, filter hoặc chọn ô khác. Khi resume, code cũ gán kết quả cho context hiện tại mà không kiểm tra xem ngữ cảnh có bị thay đổi không. Không kiểm tra filter chưa áp dụng; không chặn khi `MonthlyRate <= 0`. | Thêm kiểm tra `_hasUnappliedFilterChanges`; dùng `_catalogContextVersion` để kiểm tra tính hợp lệ của cache; chụp snapshot context trước `await` và đối chiếu sau `await` để loại bỏ response sai ngữ cảnh; kiểm tra `MonthlyRate > 0`. | Mã nguồn WPF biên dịch chuẩn xác, xử lý nhất quán với tab Catalog |
| **4** | **Test SQL Server Concurrency có thể lỗi trên DB cài sạch và chưa an toàn tuyệt đối** (`ReservationSqlServerConcurrencyTests.cs`) | **Critical** | Trigger `trg_facility_rates_validate_price_range` lỗi 51022 do thiếu seed `price_range`. Fixture giả định cơ sở 1 luôn tồn tại. Khối `finally` có thể bỏ sót invoice dọn dẹp hoặc nuốt lỗi. Chưa kiểm tra tên DB để ngăn chặn chạy nhầm trên DB thật `SelfStoragePRN222`. | Kiểm tra tên DB phải chứa `test` và từ chối chạy trên `SelfStoragePRN222`. Tự seed `price_range` hợp lệ trước khi tạo `facility_rate`. Tự động tạo cơ sở nếu chưa có. Thu thập ID reservation và dọn dẹp sạch cả invoice/reservation trong `finally`, báo lỗi cleanup minh bạch. | `ReservationSqlServerConcurrencyTests` (SKIP an toàn khi không opt-in, sẵn sàng chạy an toàn trên DB test) |
| **5** | **`CanCancel` không nhất quán giữa màn hình danh sách và chi tiết** (`ReservationService.cs`) | **Medium** | `MapToDetailResponse` quy định `CanCancel = isPendingHold && hold_until > nowUtc`, nhưng `MapToListItemResponse` lại dùng `(isPendingHold && hold_until > nowUtc) || r.status == "confirmed"`. Khách thấy nút hủy trên danh sách nhưng vào chi tiết lại không hủy được hoặc gọi API bị 409. | Sửa `MapToListItemResponse` đồng bộ 100% với `MapToDetailResponse`: `canCancel = isPendingHold && r.hold_until > nowUtc`. Chỉ cho phép hủy khi đang trong thời hạn giữ chỗ 15 phút. | `CanCancelPolicy_DetailResponseAndListItemResponse_AreStrictlyConsistent` |
| **6** | **Tài liệu và báo cáo chứa thông tin chưa chuẩn xác** (`DoneFunction3.md`, `FIVE_CORE_FUNCTIONS_SUMMARY.md`) | **Medium** | Báo cáo cũ nhầm lẫn mốc nhắc gia hạn hợp đồng `BR-REN-01` thành 14 ngày (SRS quy định 07, 03, 01 ngày); chưa cập nhật đúng tên branch và số lượng test thực tế; `FIVE_CORE_FUNCTIONS_SUMMARY.md` chưa ghi nhận phạm vi hoàn thành của Chức năng 3. | Đính chính `BR-REN-01` là 07, 03, 01 ngày theo SRS. Cập nhật branch `Reservation-and-Hold-Unit`. Cập nhật `FIVE_CORE_FUNCTIONS_SUMMARY.md` phản ánh trung thực hiện trạng 124 test pass và giới hạn kiểm thử. Nêu rõ câu hỏi quyết định cho `quoted_total`. | Kiểm tra tài liệu và git status |

---

## 2. Chi Tiết Kỹ Thuật Từng Điểm Sửa

### 2.1. Khắc phục lỗi EF Core Tracking trong Hủy đặt chỗ (Lỗi 1)
- **Vấn đề**: `ExecuteUpdateAsync` là thao tác bulk update trực tiếp mức cơ sở dữ liệu, không thông qua Change Tracker của EF Core. Khi entity `reservation` đã được DbContext nạp vào bộ nhớ trước đó ở trạng thái `pending`, lệnh `ExecuteUpdateAsync` sửa `status` thành `cancelled` ở DB nhưng không làm thay đổi giá trị thuộc tính của entity trong Change Tracker. Truy vấn nạp lại tiếp theo nếu dùng tracking query sẽ trả về chính instance cũ trong Identity Map, khiến response trả về `status = "pending"` và `canCancel = true`.
- **Giải pháp**:
  1. Trong `ReservationRepository.GetByIdWithDetailsAsync`, chuyển sang truy vấn không theo dõi: `.AsNoTracking()`.
  2. Trong `ReservationRepository.CancelReservationAsync`, tại mọi nhánh trả về:
     - Nhánh Idempotent (`current.status == "cancelled"`): Gọi `_context.Entry(current).State = EntityState.Detached;` và nạp lại bằng untracked query có đầy đủ `Include` navigation.
     - Nhánh Race condition (`affected == 0`): Gọi `_context.Entry(current).State = EntityState.Detached;` và nạp lại entity mới nhất từ DB.
     - Nhánh Cập nhật thành công sau commit: Gọi `_context.Entry(current).State = EntityState.Detached;` và tải lại `updated` bằng truy vấn `AsNoTracking().Include(...)`.
- **Kiểm thử**: Viết bài test `CancelReservation_ReturnsFreshCancelledState_EvenWhenEntityWasTrackedAsPending` trong `ReservationServiceTests.cs`. Test nạp entity `pending` có tracking vào cùng một DbContext, thực thi hủy, và khẳng định kết quả trả về mang đúng `status = "cancelled"` và `canCancel = false`.

### 2.2. Khắc phục stale state khi đọc chi tiết có tranh chấp đồng thời (Lỗi 2)
- **Vấn đề**: Khi khách hàng đọc chi tiết đơn quá hạn, nếu đơn vừa được xác nhận thanh toán bởi một tiến trình khác, `TryExpireSingleReservationIfOverdueAsync` trả về `false`. Code sau đó gọi `GetByIdWithDetailsAsync(id)`. Nếu phương thức này dùng tracking trên cùng DbContext, entity trả về vẫn là `pending`.
- **Giải pháp**: Nhờ cập nhật `GetByIdWithDetailsAsync` sử dụng `.AsNoTracking()`, truy vấn đọc lại sau tranh chấp luôn bỏ qua Change Tracker và nạp dữ liệu committed mới nhất (`confirmed`) từ cơ sở dữ liệu.
- **Kiểm thử**: Viết bài test `GetReservationById_WhenConcurrentConfirmationOccurs_ReturnsFreshConfirmedStatus` trong `ReservationServiceTests.cs`. Test mô phỏng transaction song song cập nhật trạng thái sang `confirmed`, và xác minh GET chi tiết trả về đúng `status = "confirmed"`.

### 2.3. Chống Stale Context và Race Condition trên WPF Floor Map (Lỗi 3)
- **Vấn đề**: Trên màn hình WPF Catalog:
  - Khi người dùng thay đổi bộ lọc ngày/giá nhưng chưa bấm "Áp dụng", việc bấm tiếp tục từ Floor Map có thể sử dụng dữ liệu không khớp với mong muốn người dùng.
  - Khi bấm "Tiến hành giữ chỗ theo loại kho này" từ Floor Map, hàm thực hiện `await ApiClient.Instance.GetFacilityUnitTypesAsync`. Trong lúc chờ phản hồi mạng, người dùng có thể chuyển cơ sở hoặc thay đổi bộ lọc. Khi hoàn thành `await`, hàm tiếp tục xử lý với kết quả cũ và gán cho cơ sở mới, gây sai lệch dữ liệu nghiêm trọng.
  - Không kiểm tra `MonthlyRate > 0`.
- **Giải pháp**:
  1. Trong `BtnApplyFilter_Click`, dọn dẹp `gridUnitTypes.ItemsSource = null` khi đặt lại ngữ cảnh.
  2. Trong `BtnProceedFromMap_Click`:
     - Kiểm tra cờ `_hasUnappliedFilterChanges`, nếu có thay đổi chưa áp dụng thì cảnh báo người dùng bấm "Áp dụng bộ lọc" trước.
     - Kiểm tra phiên bản ngữ cảnh `_loadedUnitTypesContextVersion == _catalogContextVersion` trước khi đọc từ cache `gridUnitTypes.ItemsSource`.
     - Chụp snapshot ngữ cảnh trước khi gọi `await`:
       ```csharp
       var targetFacilityId = _selectedFacilityId;
       var targetUnitTypeId = unit.UnitTypeId;
       var targetFilter = _appliedFilter;
       var targetContextVersion = _catalogContextVersion;
       ```
     - Sau lệnh `await`, kiểm tra lại nghiêm ngặt xem ngữ cảnh có bị thay đổi trong lúc chờ không:
       ```csharp
       if (targetFacilityId != _selectedFacilityId ||
           targetUnitTypeId != _selectedUnitTypeId ||
           targetContextVersion != _catalogContextVersion ||
           _appliedFilter.StartDate != targetFilter.StartDate ||
           _appliedFilter.EndDate != targetFilter.EndDate)
       {
           return; // Hủy bỏ kết quả bất đồng bộ do ngữ cảnh người dùng đã thay đổi
       }
       ```
     - Ràng buộc kiểm tra `unitType.MonthlyRate > 0` trước khi mở dialog xác nhận.

### 2.4. Tối ưu độ an toàn và tương thích Clean Install cho Test SQL Concurrency (Lỗi 4)
- **Vấn đề**:
  - Bài test `ReservationSqlServerConcurrencyTests.cs` khi chạy trên database mới cài đặt (clean install) bị lỗi trigger SQL Server `core.trg_facility_rates_validate_price_range` (error 51022) do chưa có bản ghi `price_range` cho loại kho test.
  - Test giả định cơ sở 1 luôn tồn tại trong DB.
  - Thuộc tính kết nối chưa bảo vệ tuyệt đối: nếu người dùng vô tình đặt biến môi trường trỏ vào database chính `SelfStoragePRN222`, test có thể chạy trên DB ứng dụng.
- **Giải pháp**:
  1. Trong `SqlIntegrationFactAttribute` và `GetTestConnectionString`: Thêm kiểm tra nghiêm ngặt tên database phải chứa từ khóa `test` (không phân biệt hoa thường), và lập tức từ chối chạy nếu trỏ tới `SelfStoragePRN222`.
  2. Tự động kiểm tra và khởi tạo cơ sở thử nghiệm nếu cơ sở 1 chưa tồn tại.
  3. Tự động seed bản ghi `price_range` hợp lệ tương ứng với `unit_type_id` trước khi chèn `facility_rate`, thỏa mãn hoàn toàn trigger `core.trg_facility_rates_validate_price_range`.
  4. Thu thập các ID reservation được tạo ngay trong lúc chạy; trong khối `finally`, truy vấn và dọn dẹp triệt để các hóa đơn và reservation được sinh ra trong đợt test, báo cáo lỗi cleanup minh bạch bằng tiền tố `[CLEANUP_FAILURE]` thay vì nuốt lỗi.

### 2.5. Đồng bộ chính sách `CanCancel` (Lỗi 5)
- **Vấn đề**: `MapToDetailResponse` gán `CanCancel = isPendingHold && r.hold_until > nowUtc`, trong khi `MapToListItemResponse` lại gán `CanCancel = (isPendingHold && r.hold_until > nowUtc) || r.status == "confirmed"`. Sự không nhất quán này dẫn tới việc hiển thị nút Hủy trên danh sách cho các đơn đã thanh toán cọc (`confirmed`), nhưng khi gọi hủy thì bị từ chối với HTTP 409 Conflict.
- **Giải pháp**: Đồng bộ `canCancel = isPendingHold && r.hold_until > nowUtc` trong `MapToListItemResponse`, đảm bảo 100% nhất quán với `MapToDetailResponse` và đúng phạm vi Chức năng 3 (chỉ cho phép hủy trong thời hạn giữ chỗ 15 phút).
- **Kiểm thử**: Viết bài test `CanCancelPolicy_DetailResponseAndListItemResponse_AreStrictlyConsistent` trong `ReservationServiceTests.cs` kiểm tra 5 kịch bản trạng thái:
  - Đơn pending còn hạn: Cả hai đều `CanCancel == true`.
  - Đơn pending quá hạn: Cả hai đều `CanCancel == false`.
  - Đơn confirmed: Cả hai đều `CanCancel == false`.
  - Đơn cancelled: Cả hai đều `CanCancel == false`.
  - Đơn expired: Cả hai đều `CanCancel == false`.

---

## 3. Danh Sách Các File Đã Can Thiệp

| STT | Đường dẫn file | Tầng kiến trúc | Nội dung sửa đổi |
| :---: | :--- | :--- | :--- |
| 1 | `src/SelfStorageManagementSystem.DataAccess/Repositories/Implementations/ReservationRepository.cs` | DataAccess | Thêm `.AsNoTracking()` trong `GetByIdWithDetailsAsync`; detach entity trước khi nạp lại trong `CancelReservationAsync` để tránh stale Change Tracker; nạp lại fresh graph sau commit. |
| 2 | `src/SelfStorageManagementSystem.BusinessLogic/Services/Implementations/ReservationService.cs` | BusinessLogic | Sử dụng untracked query khi đọc lại sau tranh chấp trong `GetReservationByIdAsync`; sửa `canCancel` trong `MapToListItemResponse` đồng bộ với `MapToDetailResponse`. |
| 3 | `src/SelfStorageManagementSystem.WpfClient/Views/FacilityCatalogWindow.xaml.cs` | WPF Client | Kiểm tra `_hasUnappliedFilterChanges`; thêm versioning `_catalogContextVersion`; snapshot và xác thực context sau `await` trong `BtnProceedFromMap_Click`; kiểm tra `MonthlyRate > 0`. |
| 4 | `tests/SelfStorageManagementSystem.Tests/ReservationSqlServerConcurrencyTests.cs` | Tests | Bổ sung kiểm tra tên DB an toàn (bắt buộc chứa `test`); seed `price_range` hợp lệ thỏa mãn trigger 51022; tạo dynamic facility; dọn dẹp an toàn invoice và reservation trong `finally`. |
| 5 | `tests/SelfStorageManagementSystem.Tests/ReservationServiceTests.cs` | Tests | Thêm 3 test cases mới: `CancelReservation_ReturnsFreshCancelledState_EvenWhenEntityWasTrackedAsPending`, `GetReservationById_WhenConcurrentConfirmationOccurs_ReturnsFreshConfirmedStatus`, `CanCancelPolicy_DetailResponseAndListItemResponse_AreStrictlyConsistent`. |
| 6 | `docs/FIVE_CORE_FUNCTIONS_SUMMARY.md` | Tài liệu dự án | Cập nhật hiện trạng Chức năng 3 thành "Đã triển khai cốt lõi", cập nhật kết quả 124 test passed, ghi nhận phạm vi đã hoàn thành và giới hạn kiểm thử. |
| 7 | `AgentReport/DoneFunction3.md` | Tài liệu báo cáo | Đính chính mốc nhắc gia hạn `BR-REN-01` thành 07, 03, 01 ngày; cập nhật branch `Reservation-and-Hold-Unit`; cập nhật kết quả kiểm thử toàn diện. |
| 8 | `AgentReport/function3Fix.md` | Tài liệu báo cáo | Lập báo cáo kỹ thuật chi tiết các lỗi review đã sửa, phân tích nguyên nhân và test chứng minh. |

---

## 4. Câu Hỏi Quyết Định Chính Sách Giá `quoted_total`

Khi khách hàng đặt thuê kho có thời hạn nhiều tháng ($N > 1$, ví dụ 6 tháng) với tiền cọc bằng 1 tháng tiền thuê (`BR-FIN-01`):

**Hệ thống nên áp dụng công thức nào cho `quoted_total` tại bước giữ chỗ?**

* **Phương án A (Khuyến nghị - Hiện đang áp dụng trong code)**:
  $$\text{QuotedTotal} = \text{Tiền cọc (1 tháng)} + \text{Tiền thuê tháng đầu tiên (1 tháng)} + \text{Phí đặt chỗ} - \text{Giảm giá}$$
  *Lý do*: Tuân thủ quy tắc `BR-RSV-04` ("Tiền cọc + Tiền thuê kỳ đầu"); phù hợp mô hình self-storage thanh toán định kỳ theo từng tháng; giảm áp lực tài chính ban đầu cho khách hàng khi giữ chỗ. Hóa đơn các tháng tiếp theo sẽ được phát hành định kỳ trong Chức năng 4–5.

* **Phương án B (Theo diễn giải Roadmap dòng 108)**:
  $$\text{QuotedTotal} = \text{Tiền cọc (1 tháng)} + (\text{Tiền thuê mỗi tháng} \times N) + \text{Phí đặt chỗ} - \text{Giảm giá}$$
  *Lý do*: Yêu cầu thanh toán trọn gói toàn bộ tiền thuê của cả hợp đồng ngay tại thời điểm checkout đặt chỗ.

---

## 5. Kết Quả Kiểm Thử Cuối Cùng

- **Build Solution**:
  ```bash
  dotnet build SelfStorageManagementSystem.sln
  ```
  $\rightarrow$ **Build succeeded: 0 Error, 0 Warning CS**.
- **Chạy toàn bộ Test Suite**:
  ```bash
  dotnet test tests/SelfStorageManagementSystem.Tests
  ```
  ```text
  Test run for D:\FPT\PRN222\Project\Final_Project\tests\SelfStorageManagementSystem.Tests\bin\Debug\net8.0-windows\SelfStorageManagementSystem.Tests.dll (.NETCoreApp,Version=v8.0)
  A total of 1 test files matched the specified pattern.
  [xUnit.net 00:00:00.19] SelfStorageManagementSystem.Tests.ReservationSqlServerConcurrencyTests.ConcurrentReservationHold_WhenOnlyOneSlotAvailable_ExactlyOneSucceedsAndOneFailsWithControlledConflict [SKIP]
    Skipped SelfStorageManagementSystem.Tests.ReservationSqlServerConcurrencyTests... [1 ms]

  Passed!  - Failed: 0, Passed: 124, Skipped: 1, Total: 125, Duration: 1 s
  ```
- **Kiểm tra git diff**:
  - Không commit, không push.
  - Giữ nguyên các thay đổi chưa commit của người dùng.
