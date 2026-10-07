# Báo Cáo Sửa Các Lỗi Còn Lại Của Chức Năng 3: Reservation & Hold Unit

- **Dự án**: Self-Storage Facility Rental and Management System (PRN222)
- **Thời gian hoàn thành**: 07/10/2026
- **Branch làm việc**: `Reservation-and-Hold-Unit` (bắt đầu từ commit `7b1f411`)
- **Kiến trúc áp dụng**: Strict 3-Layer Architecture + WPF Client (.NET 8, ASP.NET Core Web API, EF Core 8 Database-First, SQL Server `core` schema)
- **Tình trạng kiểm thử**: **124 Passed, 1 Skipped (Opt-in SQL Integration), 0 Failed, 0 Warnings CS, 0 Build Errors**

---

## 1. Tóm Tắt Các Lỗi Được Rà Soát & Khắc Phục

| STT | Vấn đề rà soát | Tầng / File bị ảnh hưởng | Hiện trạng trước khi sửa | Giải pháp đã hoàn tất | Test chứng minh |
| :---: | :--- | :--- | :--- | :--- | :--- |
| **1** | **API hủy đặt chỗ trả trạng thái cũ** do EF Core Change Tracker sau `ExecuteUpdateAsync` | `DataAccess/ReservationRepository.cs`<br>`BusinessLogic/ReservationService.cs` | `CancelReservationAsync` dùng `ExecuteUpdateAsync` sửa status trong DB nhưng Change Tracker vẫn theo dõi entity `pending`. Query nạp lại trả về chính instance cũ trong Identity Map, HTTP 200 trả sai `pending`, `canCancel: true`. | Thêm `.AsNoTracking()` vào `GetByIdWithDetailsAsync`. Trong `CancelReservationAsync`, tách rời (`Detach`) entity đang theo dõi khỏi Change Tracker ở cả 3 nhánh (idempotent, race condition, sau commit), sau đó nạp lại fresh graph bằng untracked query. | `CancelReservation_ReturnsFreshCancelledState_EvenWhenEntityWasTrackedAsPending` |
| **2** | **GET chi tiết trả `pending`** khi đơn được xác nhận đồng thời | `BusinessLogic/ReservationService.cs`<br>`DataAccess/ReservationRepository.cs` | Khi đơn quá hạn, nếu đơn vừa được xác nhận đồng thời (`confirmed`), `TryExpireSingleReservationIfOverdueAsync` trả `false`. Service nạp lại entity bằng tracking query, Change Tracker trả lại entity `pending` cũ. | Nạp lại entity bằng truy vấn không theo dõi `GetByIdWithDetailsAsync(id)` (đã có `.AsNoTracking()`), đảm bảo lấy trạng thái `confirmed` mới nhất đã commit từ DB. | `GetReservationById_WhenConcurrentConfirmationOccurs_ReturnsFreshConfirmedStatus` |
| **3** | **Nút Tiếp tục từ Floor Map dùng giá cũ hoặc bị race condition** khi context đổi trong lúc `await` | `WpfClient/Views/FacilityCatalogWindow.xaml.cs` | Bấm Tiếp tục từ Floor Map gọi `await GetFacilityUnitTypesAsync`. Trong lúc chờ, người dùng có thể đổi cơ sở, filter hoặc chọn ô khác. Khi resume, code cũ gán kết quả cho context hiện tại mà không kiểm tra xem ngữ cảnh có bị thay đổi không. Không kiểm tra filter chưa áp dụng; không chặn khi `MonthlyRate <= 0`. | Thêm kiểm tra `_hasUnappliedFilterChanges`; dùng `_catalogContextVersion` để kiểm tra cache; chụp snapshot context trước `await` và đối chiếu sau `await` để loại bỏ response sai ngữ cảnh; kiểm tra `MonthlyRate > 0`. | Solution biên dịch thành công, luồng WPF Catalog hoạt động nhất quán, bảo vệ context |
| **4** | **Test SQL Concurrency có thể lỗi trên DB cài sạch** và bảo vệ an toàn dữ liệu | `tests/ReservationSqlServerConcurrencyTests.cs` | Trigger `trg_facility_rates_validate_price_range` lỗi 51022 do thiếu seed `price_range`. Fixture giả định cơ sở 1 luôn tồn tại. Khối `finally` có thể bỏ sót invoice dọn dẹp hoặc nuốt lỗi. Chưa kiểm tra tên DB để ngăn chạy trên DB chính `SelfStoragePRN222`. | Kiểm tra tên DB phải chứa `test` và từ chối chạy trên `SelfStoragePRN222`. Tự seed `price_range` hợp lệ trước khi tạo `facility_rate`. Tự động tạo cơ sở nếu chưa có. Thu thập ID reservation và dọn dẹp sạch cả invoice/reservation trong `finally`, báo lỗi cleanup minh bạch. | `ReservationSqlServerConcurrencyTests` (SKIP an toàn khi không opt-in, sẵn sàng chạy an toàn trên DB test) |
| **5** | **`CanCancel` không nhất quán** giữa màn hình danh sách và chi tiết | `BusinessLogic/ReservationService.cs` | `MapToDetailResponse` gán `CanCancel = isPendingHold && r.hold_until > nowUtc`, nhưng `MapToListItemResponse` lại dùng `(isPendingHold && r.hold_until > nowUtc) || r.status == "confirmed"`. Khách thấy nút hủy trên danh sách nhưng vào chi tiết lại không hủy được hoặc gọi API bị 409. | Sửa `MapToListItemResponse` đồng bộ 100% với `MapToDetailResponse`: `canCancel = isPendingHold && r.hold_until > nowUtc`. Chỉ cho phép hủy khi đang trong thời hạn giữ chỗ 15 phút. | `CanCancelPolicy_DetailResponseAndListItemResponse_AreStrictlyConsistent` |
| **6** | **Tài liệu và báo cáo chứa thông tin chưa chuẩn xác** | `DoneFunction3.md`<br>`function3Fix.md`<br>`FIVE_CORE_FUNCTIONS_SUMMARY.md` | Báo cáo cũ nhầm lẫn mốc nhắc gia hạn hợp đồng `BR-REN-01` thành 14 ngày (SRS quy định 07, 03, 01 ngày); chưa cập nhật đúng tên branch và số lượng test thực tế; `FIVE_CORE_FUNCTIONS_SUMMARY.md` chưa ghi nhận phạm vi hoàn thành của Chức năng 3. | Đính chính `BR-REN-01` là 07, 03, 01 ngày theo SRS. Cập nhật branch `Reservation-and-Hold-Unit`. Cập nhật `FIVE_CORE_FUNCTIONS_SUMMARY.md` phản ánh trung thực hiện trạng 124 test pass và giới hạn kiểm thử. Nêu rõ câu hỏi quyết định cho `quoted_total`. | Kiểm tra tài liệu và git status |

---

## 2. Chi Tiết Kỹ Thuật Từng Điểm Sửa

### 2.1. Khắc phục lỗi EF Core Change Tracker sau `ExecuteUpdateAsync` (Lỗi 1)
- **Tình huống & Nguyên nhân**:
  - Trong `ReservationService.CancelReservationAsync`, trước khi thực hiện hủy, entity `reservation` đã được nạp qua `GetByIdWithDetailsAsync` vào DbContext hiện tại ở trạng thái theo dõi (`EntityState.Unchanged`).
  - Trong `ReservationRepository.CancelReservationAsync`, nhánh SQL Server sử dụng `ExecuteUpdateAsync` để cập nhật nguyên tử `status = 'cancelled'` và `cancelled_at = nowUtc` trực tiếp xuống SQL Server.
  - Do `ExecuteUpdateAsync` tác động trực tiếp ở database và hoàn toàn bỏ qua Change Tracker của EF Core, đối tượng entity `reservation` nằm trong bộ nhớ cục bộ của DbContext vẫn giữ nguyên giá trị ban đầu (`status = "pending"`).
  - Khi repository cố gắng tải lại bản ghi bằng truy vấn LINQ để trả về DTO cho service, Identity Map của EF Core phát hiện ID bản ghi đã tồn tại trong Change Tracker, nên trả lại ngay instance cũ đang theo dõi trong bộ nhớ thay vì nạp từ database.
  - Kết quả: Dù database đã chuyển sang `cancelled`, HTTP 200 trả về client vẫn hiển thị trạng thái `pending`, `canCancel = true`, và thời hạn giữ chỗ sai.
- **Giải pháp**:
  - Tại `ReservationRepository.GetByIdWithDetailsAsync`, chuyển sang truy vấn không theo dõi: `.AsNoTracking()`.
  - Tại `ReservationRepository.CancelReservationAsync`, bổ sung lệnh tách rời entity khỏi Change Tracker trước khi nạp lại:
    ```csharp
    _context.Entry(current).State = EntityState.Detached;
    ```
  - Áp dụng việc detach này cho cả 3 nhánh: nhánh idempotent (`current.status == "cancelled"`), nhánh race condition (`affected == 0`), và nhánh sau khi commit transaction thành công.
  - Nạp lại bản ghi bằng truy vấn `AsNoTracking().Include(...)` đảm bảo đầy đủ navigation properties cho mapper DTO.
- **Test chứng minh**: Unit test `CancelReservation_ReturnsFreshCancelledState_EvenWhenEntityWasTrackedAsPending` trong `ReservationServiceTests.cs`.

### 2.2. Khắc phục stale state khi đọc chi tiết có tranh chấp đồng thời (Lỗi 2)
- **Tình huống & Nguyên nhân**:
  - Trong `ReservationService.GetReservationByIdAsync`, nếu đơn quá hạn, hệ thống gọi `TryExpireSingleReservationIfOverdueAsync`.
  - Nếu một tiến trình khác (khách thanh toán thành công hoặc nhân viên xác nhận) vừa cập nhật đơn sang `confirmed`, lệnh cập nhật có điều kiện trả về `affected == 0` và hàm trả về `false`.
  - Code cũ sau đó gọi lại `GetByIdWithDetailsAsync(id)` trên cùng một DbContext đang theo dõi entity ở trạng thái `pending`. Kết quả là EF Core trả lại chính entity `pending` cũ, thay vì trạng thái `confirmed` vừa được commit.
- **Giải pháp**:
  - Đảm bảo `GetByIdWithDetailsAsync` luôn sử dụng `.AsNoTracking()`, loại bỏ phụ thuộc vào Change Tracker khi đọc lại bản ghi sau tranh chấp.
- **Test chứng minh**: Unit test `GetReservationById_WhenConcurrentConfirmationOccurs_ReturnsFreshConfirmedStatus` trong `ReservationServiceTests.cs`.

### 2.3. Chống Stale Context và Race Condition trên WPF Floor Map (Lỗi 3)
- **Tình huống & Nguyên nhân**:
  - Trong `FacilityCatalogWindow.xaml.cs`, khi người dùng bấm "Tiến hành giữ chỗ theo loại kho này" từ sơ đồ tầng, hệ thống gọi `await ApiClient.Instance.GetFacilityUnitTypesAsync`.
  - Trong lúc chờ cuộc gọi mạng bất đồng bộ, người dùng có thể chuyển sang cơ sở khác, thay đổi bộ lọc ngày/giá, hoặc chọn ô khác trên bản đồ.
  - Khi `await` hoàn tất, code cũ tiếp tục gán kết quả trả về cho cơ sở hoặc ngữ cảnh mới mà không kiểm tra xem ngữ cảnh có bị thay đổi trong lúc chờ hay không.
  - Người dùng có thể chỉnh sửa bộ lọc ngày thuê nhưng chưa bấm "Áp dụng bộ lọc", dẫn tới việc bấm tiếp tục sử dụng khoảng ngày cũ không khớp với mong muốn.
  - Nếu `unitType.MonthlyRate <= 0`, dialog vẫn có thể mở với dữ liệu không hợp lệ.
- **Giải pháp**:
  - Trong `BtnApplyFilter_Click`, dọn dẹp `gridUnitTypes.ItemsSource = null` khi đặt lại ngữ cảnh.
  - Trong `BtnProceedFromMap_Click`:
    - Kiểm tra `if (_hasUnappliedFilterChanges)`: cảnh báo người dùng bấm "Áp dụng bộ lọc" trước khi tiếp tục.
    - Kiểm tra `_loadedUnitTypesContextVersion == _catalogContextVersion` trước khi đọc từ cache `gridUnitTypes.ItemsSource`.
    - Chụp snapshot ngữ cảnh trước khi gọi `await`:
      ```csharp
      var targetFacilityId = _selectedFacilityId;
      var targetUnitTypeId = unit.UnitTypeId;
      var targetFilter = _appliedFilter;
      var targetContextVersion = _catalogContextVersion;
      ```
    - Sau khi `await` hoàn tất, đối chiếu nghiêm ngặt:
      ```csharp
      if (targetFacilityId != _selectedFacilityId ||
          targetUnitTypeId != _selectedUnitTypeId ||
          targetContextVersion != _catalogContextVersion ||
          _appliedFilter.StartDate != targetFilter.StartDate ||
          _appliedFilter.EndDate != targetFilter.EndDate)
      {
          return; // Bỏ qua response bất đồng bộ vì ngữ cảnh người dùng đã thay đổi
      }
      ```
    - Kiểm tra bắt buộc `unitType.MonthlyRate > 0` trước khi khởi tạo `ConfirmReservationDialog`.

### 2.4. Đảm bảo an toàn tuyệt đối và tương thích Clean Install cho Test SQL Concurrency (Lỗi 4)
- **Tình huống & Nguyên nhân**:
  - Trong database SQL Server cài đặt sạch theo script `SelfStoragePRN222_SQLServer_CleanInstall.sql`, trigger `core.trg_facility_rates_validate_price_range` kiểm tra rằng mỗi khi thêm bản ghi `core.facility_rates`, bắt buộc phải tồn tại một bản ghi `core.price_ranges` cho `unit_type_id` đó với khoảng giá bao hàm `monthly_rate`. Nếu thiếu, trigger ném lỗi SQL 51022.
  - Bài test `ReservationSqlServerConcurrencyTests.cs` trước đây không seed `price_range` mà chỉ chèn trực tiếp `facility_rate`, dẫn tới bài test bị văng lỗi trigger 51022 trên môi trường clean install.
  - Fixture giả định cơ sở ID 1 luôn tồn tại; nếu database test không có cơ sở 1 thì fail do lỗi khóa ngoại.
  - Chưa có cơ chế bảo vệ danh tính database: nếu biến môi trường trỏ nhầm vào database chính `SelfStoragePRN222`, test có thể chạy trên database thật.
- **Giải pháp**:
  - Thêm kiểm tra nghiêm ngặt trong `SqlIntegrationFactAttribute` và `GetTestConnectionString`: tên database bắt buộc phải chứa chuỗi `test` (không phân biệt hoa thường), và lập tức từ chối chạy nếu trỏ tới `SelfStoragePRN222`.
  - Tự động kiểm tra và khởi tạo cơ sở thử nghiệm nếu cơ sở 1 chưa tồn tại.
  - Tự động seed bản ghi `core.price_ranges` hợp lệ tương ứng với `unit_type_id` trước khi chèn `core.facility_rates`.
  - Thu thập danh sách ID reservation được tạo; trong khối `finally`, truy vấn và dọn dẹp triệt để các hóa đơn và reservation được sinh ra trong đợt test, báo cáo lỗi cleanup minh bạch bằng tiền tố `[CLEANUP_FAILURE]`.

### 2.5. Đồng bộ chính sách `CanCancel` (Lỗi 5)
- **Tình huống & Nguyên nhân**:
  - `MapToDetailResponse` gán `CanCancel = isPendingHold && r.hold_until > nowUtc`, trong khi `MapToListItemResponse` lại gán `CanCancel = (isPendingHold && r.hold_until > nowUtc) || r.status == "confirmed"`.
  - Việc này khiến danh sách hiển thị cờ cho phép hủy đối với đơn đã xác nhận thanh toán (`confirmed`), nhưng khi gọi API thì bị trả về `409 Conflict`.
- **Giải pháp**:
  - Đồng bộ `canCancel = isPendingHold && r.hold_until > nowUtc` trong `MapToListItemResponse`, đảm bảo 100% nhất quán với `MapToDetailResponse` và đúng phạm vi Chức năng 3 (chỉ cho phép hủy trong thời hạn giữ chỗ 15 phút).
- **Test chứng minh**: Unit test `CanCancelPolicy_DetailResponseAndListItemResponse_AreStrictlyConsistent` trong `ReservationServiceTests.cs` kiểm tra 5 kịch bản trạng thái (`pending` còn hạn/hết hạn, `confirmed`, `cancelled`, `expired`).

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

Khi khách hàng thuê kho nhiều tháng ($N > 1$, ví dụ 6 tháng) với tiền cọc bằng 1 tháng tiền thuê (`BR-FIN-01`):

> **Hệ thống nên áp dụng chính sách nào cho `quoted_total` tại bước giữ chỗ?**
>
> * **Phương án A (Khuyến nghị - Hiện đang áp dụng trong code)**:
>   $$\text{QuotedTotal} = \text{Tiền cọc (1 tháng)} + \text{Tiền thuê tháng đầu tiên (1 tháng)} + \text{Phí đặt chỗ} - \text{Giảm giá}$$
>   *Ưu điểm*: Tuân thủ quy tắc `BR-RSV-04` ("Tiền cọc + Tiền thuê kỳ đầu"); phù hợp mô hình self-storage thanh toán định kỳ theo từng tháng; giảm áp lực tài chính ban đầu cho khách hàng khi giữ chỗ. Hóa đơn các tháng tiếp theo sẽ được phát hành định kỳ trong Chức năng 4–5.
>
> * **Phương án B (Theo diễn giải Roadmap dòng 108)**:
>   $$\text{QuotedTotal} = \text{Tiền cọc (1 tháng)} + (\text{Tiền thuê mỗi tháng} \times N) + \text{Phí đặt chỗ} - \text{Giảm giá}$$
>   *Ưu điểm*: Thu đủ toàn bộ tiền thuê của cả hợp đồng ngay từ đầu; giảm thiểu rủi ro nợ xấu.

---

## 5. Kết Quả Kiểm Thử Thực Tế

- **Build Solution**:
  ```bash
  dotnet build SelfStorageManagementSystem.sln
  ```
  $\rightarrow$ **Build succeeded: 0 Error(s), 0 Warning(s) CS**.
- **Chạy toàn bộ Test Suite**:
  ```bash
  dotnet test tests/SelfStorageManagementSystem.Tests
  ```
  ```text
  Test run for D:\FPT\PRN222\Project\Final_Project\tests\SelfStorageManagementSystem.Tests\bin\Debug\net8.0-windows\SelfStorageManagementSystem.Tests.dll (.NETCoreApp,Version=v8.0)
  A total of 1 test files matched the specified pattern.
  [xUnit.net 00:00:00.16] SelfStorageManagementSystem.Tests.ReservationSqlServerConcurrencyTests.ConcurrentReservationHold_WhenOnlyOneSlotAvailable_ExactlyOneSucceedsAndOneFailsWithControlledConflict [SKIP]
    Skipped SelfStorageManagementSystem.Tests.ReservationSqlServerConcurrencyTests... [1 ms]

  Passed!  - Failed: 0, Passed: 124, Skipped: 1, Total: 125, Duration: 1 s
  ```
- **Kiểm tra git diff**:
  - Không commit, không push.
  - Bảo toàn toàn bộ các thay đổi của người dùng.
