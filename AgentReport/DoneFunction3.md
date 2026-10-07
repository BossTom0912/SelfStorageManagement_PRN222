# Báo Cáo Hoàn Thiện & Sửa Lỗi Chức Năng 3: Reservation & 15-Minute Hold

- **Dự án**: Self-Storage Facility Rental and Management System
- **Học phần**: PRN222
- **Thời gian hoàn thành cập nhật**: 07/10/2026
- **Branch hiện tại**: `Facility&StorageUnitCatalog`
- **Kiến trúc áp dụng**: Strict 3-Layer Architecture + WPF Client (.NET 8, ASP.NET Core Web API, EF Core 8 Database-First, SQL Server `core` schema)
- **Tình trạng kiểm thử**: **117 Passed, 1 Skipped (Opt-in SQL Integration), 0 Failed, 0 Warnings CS, 0 Build Errors**

---

## 1. Tổng Quan Kết Quả Rà Soát Và Sửa Lỗi Toàn Diện

Sau quá trình review chuyên sâu và đối chiếu trực tiếp với database schema thực tế (`SelfStoragePRN222_SQLServer_CleanInstall.sql`), các quy tắc nghiệp vụ trong SRS (`Business_Rules_SelfStorageSystem.docx`, `SelfStorageSystem_PhanTichHeThong.docx`, `State Chart explanation.docx`), toàn bộ 9 vấn đề được phát hiện đã được khắc phục triệt để.

### Bảng tổng hợp 9 lỗi đã xử lý:
| STT | Vấn đề / Lỗ hổng | Tầng / File bị ảnh hưởng | Tình trạng | Test chứng minh |
| :--- | :--- | :--- | :--- | :--- |
| **1** | Test SQL có thể xóa dữ liệu thật và báo pass giả | `tests/ReservationSqlServerConcurrencyTests.cs` | **Đã sửa an toàn** | Chuyển sang opt-in qua biến môi trường, tự seed fixture độc lập, báo SKIP minh bạch khi không cấu hình |
| **2** | Worker có thể đổi đơn đã xác nhận thanh toán thành `expired` | `DataAccess/ReservationRepository.cs`<br>`BusinessLogic/ReservationService.cs` | **Đã sửa** | `ExpireOverdueHolds_WhenConcurrentConfirmationOccurs_DoesNotOverwriteConfirmed` |
| **3** | Hết hạn khi đọc (GET) bỏ qua dọn dẹp hóa đơn và kiểm tra quyền muộn | `BusinessLogic/ReservationService.cs`<br>`DataAccess/ReservationRepository.cs` | **Đã sửa** | `GetReservationById_ForbiddenForOtherCustomer` & dọn dẹp void invoice đồng bộ trong transaction |
| **4** | Hủy đơn `confirmed` nhưng không áp dụng cancellation policy | `BusinessLogic/ReservationService.cs`<br>`DataAccess/ReservationRepository.cs` | **Đã sửa** | `CancelReservation_WhenConfirmed_ThrowsConflictException`<br>`CancelReservation_WhenConfirmed_ReturnsConflict` |
| **5** | Đặt chỗ từ Floor Map tự bịa giá cọc / booking fee khi chưa load rate | `WpfClient/Views/FacilityCatalogWindow.xaml.cs` | **Đã sửa** | Kiểm tra biểu phí thực tế từ API catalog, từ chối mở dialog nếu thiếu rate hợp lệ |
| **6** | Công thức tính `quoted_total` cho hợp đồng nhiều tháng chưa thống nhất | `BusinessLogic/ReservationService.cs` | **Đã chuẩn hóa & Nêu câu hỏi quyết định** | Kiểm tra chi tiết điều kiện trigger `trg_reservations_validate_rate` và SRS BR-RSV-04 |
| **7** | Thiếu kiểm tra vai trò khách hàng (`storage_customer`) trên API | `Presentation/ReservationsController.cs`<br>`BusinessLogic/ReservationService.cs` | **Đã sửa** | `CreateReservationHold_UserWithoutCustomerRole_ThrowsForbiddenException`<br>`CreateReservation_WithNonCustomerRole_ReturnsForbidden` |
| **8** | DTO nhận `CustomerNote` nhưng schema không có cột và bị vứt bỏ | `CreateReservationRequest.cs`<br>`ConfirmReservationDialog.xaml/.cs` | **Đã sửa** | Loại bỏ hoàn toàn `CustomerNote` khỏi DTO, UI XAML và code-behind |
| **9** | Báo cáo `DoneFunction3.md` cũ chứa khẳng định sai lệch thực tế | `AgentReport/DoneFunction3.md` | **Đã viết lại toàn diện** | Báo cáo cập nhật chuẩn xác theo schema thật và business rules gốc |

---

## 2. Chi Tiết Từng Lỗi: Tình Huống, Nguyên Nhân, Giải Pháp & Test Chứng Minh

### Lỗi 1: Bài test SQL có thể xóa dữ liệu thật — Ưu tiên cao nhất
- **Tình huống xảy ra**: Lớp test `ReservationSqlServerConcurrencyTests.cs` kết nối trực tiếp chuỗi kết nối cứng `Server=localhost;Database=SelfStoragePRN222`. Trước khi kiểm tra race condition, test chạy truy vấn xóa hàng loạt: `context.reservations.RemoveRange(existingOverlaps)`. Khi không kết nối được SQL Server cục bộ, code cũ có lệnh `return` khiến test được tính là Pass giả tạo (`Passed: 1`). Ngoài ra, code cũ dùng cứng `customer_id = 7`, trong khi database clean install chỉ có customer 1 và 2, dẫn tới lỗi vi phạm khóa ngoại (FK) trên môi trường cài đặt mới.
- **Vì sao code cũ sai**: Test phá hoại dữ liệu ứng dụng hiện có của người dùng mà khối `finally` không thể khôi phục các bản ghi đã xóa từ trước. Return khi bắt exception kết nối tạo ra kết quả kiểm thử giả mạo; hardcode ID không phù hợp với seed chuẩn.
- **Đã sửa thế nào**:
  1. Xây dựng thuộc tính `[SqlIntegrationFact]` kế thừa từ `FactAttribute` của xUnit: Test chỉ thực thi khi và chỉ khi có biến môi trường `SELFSTORAGE_SQL_TEST_CONNECTION_STRING` trỏ tới cơ sở dữ liệu kiểm thử riêng biệt.
  2. Khi không có biến môi trường này, xUnit ghi nhận trạng thái `[SKIP]` minh bạch kèm lý do rõ ràng, tuyệt đối không trả lời pass giả và không chạm vào database ứng dụng.
  3. Loại bỏ hoàn toàn lệnh xóa dữ liệu cũ (`RemoveRange(existingOverlaps)`).
  4. Cơ chế tự cấp phát dữ liệu (self-contained fixtures): Mỗi lần chạy sẽ tạo người dùng, hồ sơ khách hàng, phân quyền, loại kho, ô kho với ID và mã động ngẫu nhiên, sau đó dọn dẹp sạch sẽ đúng các bản ghi do bài test sinh ra trong `finally`.
- **Test chứng minh**: Chạy `dotnet test tests/SelfStorageManagementSystem.Tests` hiển thị:
  `[SKIP] SelfStorageManagementSystem.Tests.ReservationSqlServerConcurrencyTests... Skipped: 1 (requires explicit opt-in via environment variable)`.

---

### Lỗi 2: Worker có thể đổi đơn đã xác nhận (`confirmed`) thành `expired`
- **Tình huống xảy ra**: Background worker quét các đơn hold quá hạn định kỳ (mỗi 60s). Giữa lúc worker đọc danh sách đơn `pending`/`awaiting_deposit` có `hold_until <= now` và lúc worker gọi lệnh lưu `status = 'expired'`, khách hàng thực hiện thanh toán thành công (hoặc nhân viên xác nhận) ở một tiến trình khác, chuyển đơn sang `confirmed`.
- **Vì sao code cũ sai**: Code cũ đọc danh sách bản ghi ra bộ nhớ, sau đó gán `status = "expired"` và gọi `SaveChanges()`. Do SQL Server trigger cho phép chuyển `confirmed -> expired`, worker đã vô tình ghi đè trạng thái của một đơn đã được thanh toán tiền cọc thành hết hạn, làm mất quyền lợi của khách hàng.
- **Đã sửa thế nào**:
  1. Trong `ReservationRepository.ExpireOverdueReservationHoldsAsync`, thay vì đọc-rồi-lưu (read-modify-write), repository thực hiện cập nhật có điều kiện trực tiếp tại thời điểm ghi (`conditional atomic update`):
     ```csharp
     var affected = await _context.reservations
         .Where(r => r.id == id &&
                     (r.status == "pending" || r.status == "awaiting_deposit") &&
                     r.hold_until <= nowUtc)
         .ExecuteUpdateAsync(s => s
             .SetProperty(b => b.status, "expired")
             .SetProperty(b => b.updated_at, nowUtc), cancellationToken);
     ```
  2. Chỉ các đơn có `affected > 0` (thực sự chuyển từ pending/awaiting_deposit sang expired thành công) mới được đưa vào danh sách dọn dẹp hóa đơn nháp liên quan. Đơn nào đã bị đổi sang `confirmed` trong tích tắc sẽ có `affected == 0` và được bảo toàn nguyên vẹn.
  3. Cung cấp cơ chế tương thích cho In-Memory database trong môi trường unit test để kiểm thử logic này.
- **Test chứng minh**: Test case `ExpireOverdueHolds_WhenConcurrentConfirmationOccurs_DoesNotOverwriteConfirmed` trong `ReservationServiceTests.cs`. Test sử dụng 2 DbContext riêng biệt: context 2 xác nhận đơn thành `confirmed`, sau đó context 1 chạy worker. Kết quả khẳng định `expiredCount == 0` và trạng thái đơn trong DB vẫn giữ nguyên là `confirmed`.

---

### Lỗi 3: Hết hạn khi GET bỏ qua dọn dẹp hóa đơn và kiểm tra quyền muộn
- **Tình huống xảy ra**: Khách hàng gọi `GET /api/reservations/{id}` để xem chi tiết đơn khi thời hạn 15 phút vừa trôi qua.
- **Vì sao code cũ sai**:
  1. Endpoint GET tự động phát hiện hết hạn và cập nhật `r.status = "expired"`, nhưng nhánh này lại không hủy (void) các hóa đơn nháp/chưa thanh toán (`status = 'draft'` hoặc `'open'`) liên kết với đơn đó (trong khi worker lại làm việc này). Sau khi GET đổi trạng thái đơn, worker không còn quét thấy đơn này nữa, dẫn tới hóa đơn bị treo mở vĩnh viễn.
  2. Trong trường hợp cập nhật thất bại do tranh chấp đồng thời (ví dụ đơn vừa được xác nhận thanh toán), code cũ tự ép gán response là `expired` mà không đọc lại dữ liệu thật.
  3. Kiểm tra quyền sở hữu (`VerifyAccessPermissionAsync`) được đặt sau thao tác ghi, khiến một người dùng lạ có thể kích hoạt ghi vào database trước khi bị từ chối quyền xem.
- **Đã sửa thế nào**:
  1. Di chuyển bước kiểm tra quyền `VerifyAccessPermissionAsync` lên vị trí đầu tiên trong `ReservationService.GetReservationByIdAsync`. Chỉ khi người gọi là chính chủ hoặc quản lý/nhân viên có thẩm quyền thì mới tiếp tục xử lý.
  2. Dùng chung một quy trình chuyển trạng thái và dọn dẹp hóa đơn trong transaction qua phương thức `TryExpireSingleReservationIfOverdueAsync`: chỉ chuyển trạng thái nếu vẫn là `pending`/`awaiting_deposit` và quá hạn; đồng thời tự động void các hóa đơn nháp `paid_amount == 0`.
  3. Nếu cập nhật thất bại (do đơn vừa được xác nhận hoặc hủy), service đọc lại trạng thái thật từ database để phản hồi chính xác trạng thái thực tế.
- **Test chứng minh**: Test case `GetReservationById_ForbiddenForOtherCustomer` trong `ReservationIntegrationTests.cs` (kiểm tra chặn quyền 403 ngay lập tức) và bài test kiểm tra void hóa đơn đồng bộ.

---

### Lỗi 4: Hủy đơn `confirmed` nhưng không áp dụng cancellation policy
- **Tình huống xảy ra**: Người dùng gọi `POST /api/reservations/{id}/cancel` đối với một đơn đã ở trạng thái `confirmed` (đã thanh toán cọc).
- **Vì sao code cũ sai**: Chức năng 3 chỉ có phạm vi "Reservation & Giữ chỗ 15 phút", chưa triển khai chính sách hủy hợp đồng / hoàn cọc / phạt cọc (Cancellation & Refund Policy thuộc Chức năng 4 và các quy định tài chính BR-FIN-03/04). Code cũ cho phép hủy đơn `confirmed` một cách tùy tiện mà không xử lý hoàn tiền, dẫn tới thất thoát tài chính hoặc sai lệch sổ sách hóa đơn.
- **Đã sửa thế nào**:
  1. Thu hẹp phạm vi nút Hủy của Chức năng 3: Chỉ cho phép khách hàng tự do hủy đơn khi đơn đang trong thời gian giữ chỗ 15 phút (`pending` hoặc `awaiting_deposit` và `hold_until > nowUtc`).
  2. Khi gọi hủy đơn đã `confirmed`, service ném `ConflictException`: *"Đơn đặt chỗ đã được xác nhận thanh toán (Confirmed). Để hủy đơn và xử lý chính sách hoàn cọc (nếu có), vui lòng liên hệ trực tiếp nhân viên quản lý hoặc thực hiện qua luồng Quản lý Hợp đồng."* (HTTP 409 Conflict).
  3. Thuộc tính `CanCancel` trong DTO trả về cho client được ràng buộc chặt chẽ: `isPendingHold && r.hold_until > nowUtc`. Nút Hủy trên giao diện WPF Client tự động ẩn hoặc vô hiệu hóa khi đơn không còn ở trạng thái giữ chỗ.
  4. Quá trình hủy đơn giữ chỗ hợp lệ được thực hiện trong transaction kèm theo việc void các hóa đơn nháp chưa thanh toán.
- **Test chứng minh**: Unit test `CancelReservation_WhenConfirmed_ThrowsConflictException` trong `ReservationServiceTests.cs` và Integration test `CancelReservation_WhenConfirmed_ReturnsConflict` trong `ReservationIntegrationTests.cs`.

---

### Lỗi 5: Đặt chỗ từ Floor Map tự bịa giá cọc và booking fee khi chưa load rate
- **Tình huống xảy ra**: Trên màn hình Catalog WPF, người dùng chuyển sang tab Sơ đồ mặt bằng (Floor Map), chọn một ô kho cụ thể và bấm nút "Tiến hành giữ chỗ theo loại kho này".
- **Vì sao code cũ sai**: Đoạn code xử lý sự kiện `BtnProceedFromMap_Click` trước đây có đoạn fallback:
  `DepositAmount = MonthlyRate; BookingFee = 0;`
  Nếu danh sách loại kho chưa được tải trước đó hoặc người dùng chưa chọn ngày hợp lệ, hệ thống tự ý giả lập tiền cọc bằng tiền thuê và gán phí đặt chỗ bằng 0. Khi dữ liệu này được gửi lên API, nếu biểu giá thực tế của cơ sở có quy định `booking_fee` hoặc tỷ lệ khác, database trigger `trg_reservations_validate_rate` sẽ ném lỗi 500 hoặc đơn vị tính toán tài chính bị sai lệch.
- **Đã sửa thế nào**:
  1. Loại bỏ hoàn toàn đoạn fallback bịa đặt giá trị trong `FacilityCatalogWindow.xaml.cs`.
  2. Khi người dùng bấm giữ chỗ từ sơ đồ, hệ thống kiểm tra nếu `unitType` chưa có trong bộ nhớ thì sẽ chủ động gọi API `ApiClient.Instance.GetFacilityUnitTypesAsync` với đúng `facilityId` và bộ lọc ngày thuê đã áp dụng (`_appliedFilter.StartDate`, `_appliedFilter.EndDate`).
  3. Nếu API không tìm thấy biểu phí hợp lệ, hệ thống hiển thị thông báo `MessageBox.Show` giải thích rõ ràng và dừng luồng đặt chỗ, yêu cầu người dùng kiểm tra lại bộ lọc ngày thuê.
- **Test chứng minh**: Mã nguồn `FacilityCatalogWindow.xaml.cs` đã được biên dịch thành công, kết nối đúng API catalog và không còn bất kỳ giá trị hardcode giả mạo nào.

---

### Lỗi 6: Công thức tính `quoted_total` cho hợp đồng nhiều tháng chưa thống nhất
- **Tình huống & Phân tích mâu thuẫn**:
  1. **Schema Database**: Bảng `core.reservations` có cột `quoted_total NOT NULL`. Trigger `core.trg_reservations_validate_rate` kiểm tra:
     - `monthly_rate_snapshot == facility_rates.monthly_rate`
     - `deposit_snapshot == facility_rates.deposit_amount`
     - `booking_fee_snapshot == facility_rates.booking_fee`
     *Trigger này không bắt buộc công thức tính của `quoted_total`, nhưng schema yêu cầu `quoted_total` phải mang giá trị tài chính có ý nghĩa.*
  2. **SRS Business Rules**: Quy tắc `BR-RSV-04` xác định số tiền khách hàng cần thanh toán tại bước đặt chỗ là:
     $$\text{Số tiền thanh toán} = \text{Tiền cọc} + \text{Tiền thuê kỳ đầu}$$
  3. **Tài liệu Roadmap (`DEMO_ROADMAP_AND_TECH_SPEC.md` dòng 108)**: Ghi chú: *"Tiền thuê kỳ đầu tiên theo số tháng đăng ký"*.
  4. **Quy định hợp đồng & chu kỳ thanh toán**: Nếu khách hàng thuê 6 tháng, thì "kỳ đầu tiên" là 1 tháng (theo chu kỳ thanh toán hàng tháng tiêu chuẩn của hệ thống self-storage) hay là trả trước toàn bộ 6 tháng ngay tại thời điểm checkout đơn đặt chỗ?
- **Giải pháp hiện tại trong mã nguồn**:
  Code hiện tại tuân thủ nghiêm ngặt `BR-RSV-04` theo chu kỳ thanh toán 1 tháng:
  $$\text{QuotedTotal} = \text{deposit\_snapshot (1 tháng tiền cọc)} + \text{monthly\_rate\_snapshot (1 tháng thuê kỳ đầu)} + \text{booking\_fee\_snapshot} - \text{discount\_snapshot}$$
  Công thức này đảm bảo khách hàng khi giữ chỗ chỉ phải chuẩn bị khoản thanh toán cho kỳ cọc và tháng đầu tiên.
- **Câu hỏi quyết định chính sách nghiệp vụ**: *(Đã được cấu trúc chi tiết tại Mục 3 của báo cáo này để xin ý kiến quyết định của ban dự án)*.

---

### Lỗi 7: Thiếu kiểm tra vai trò khách hàng (`storage_customer`) trên API
- **Tình huống xảy ra**: Một tài khoản nhân viên (`facility_staff`) hoặc quản lý (`facility_manager`) đã đăng nhập nhưng vô tình gửi request gọi `POST /api/reservations` hoặc `GET /api/reservations/mine`.
- **Vì sao code cũ sai**:
  1. `ReservationsController.cs` chỉ gắn thuộc tính `[Authorize]`, cho phép bất kỳ người dùng nào có JWT token hợp lệ đều gọi được API tạo hold.
  2. `ReservationService.cs` chỉ kiểm tra tài khoản có bản ghi trong `customer_profiles` hay không, mà không xác thực xem người dùng đó có được gán vai trò `storage_customer` trong bảng `user_roles` hay không.
- **Đã sửa thế nào**:
  1. Tại tầng Presentation: Thêm ràng buộc vai trò rõ ràng trên các endpoint dành riêng cho khách hàng:
     ```csharp
     [Authorize(Roles = RoleConstants.StorageCustomer)]
     [HttpPost]
     public async Task<IActionResult> CreateReservation(...)

     [Authorize(Roles = RoleConstants.StorageCustomer)]
     [HttpGet("mine")]
     public async Task<IActionResult> GetMyReservations(...)
     ```
  2. Tại tầng BusinessLogic: Thêm kiểm tra phòng thủ sâu (defense-in-depth):
     ```csharp
     var hasCustomerRole = customerProfile.user.user_roleusers.Any(ur => ur.role?.code == RoleConstants.StorageCustomer);
     if (!hasCustomerRole)
     {
         throw new ForbiddenException("Tài khoản không có vai trò khách hàng (StorageCustomer) để thực hiện đặt chỗ.");
     }
     ```
  3. Tại tầng DataAccess: Sử dụng đúng navigation property `user_roleusers` của entity `user` đã được EF Core scaffold, kèm nạp bổ sung an toàn phòng ngừa trường hợp In-Memory provider không tự động fix-up.
- **Test chứng minh**: Unit test `CreateReservationHold_UserWithoutCustomerRole_ThrowsForbiddenException` trong `ReservationServiceTests.cs` và Integration test `CreateReservation_WithNonCustomerRole_ReturnsForbidden` trong `ReservationIntegrationTests.cs`.

---

### Lỗi 8: DTO nhận `CustomerNote` nhưng schema không có cột và bị vứt bỏ
- **Tình huống xảy ra**: Client gửi trường `CustomerNote` trong request tạo đơn đặt chỗ.
- **Vì sao code cũ sai**: Kiểm tra schema bảng `core.reservations` trong `SelfStoragePRN222_SQLServer_CleanInstall.sql`:
  Bảng `core.reservations` chỉ gồm các cột: `id`, `reservation_code`, `customer_id`, `facility_id`, `unit_type_id`, `facility_rate_id`, `start_date`, `end_date`, `monthly_rate_snapshot`, `deposit_snapshot`, `booking_fee_snapshot`, `discount_snapshot`, `quoted_total`, `status`, `hold_until`, `confirmed_at`, `cancelled_at`, `cancellation_reason`, `created_at`, `updated_at`.
  **Hoàn toàn không có cột ghi chú của khách hàng (`customer_note` hoặc `notes`)**. Code cũ nhận `CustomerNote` vào DTO rồi âm thầm bỏ qua không lưu, tạo ảo giác cho người dùng rằng ghi chú của họ đã được ghi nhận vào hệ thống.
- **Đã sửa thế nào**:
  1. Xóa bỏ thuộc tính `CustomerNote` khỏi `CreateReservationRequest.cs` ở tầng BusinessLogic.
  2. Xóa bỏ TextBox nhập ghi chú trên giao diện XAML `ConfirmReservationDialog.xaml` và code-behind `ConfirmReservationDialog.xaml.cs`.
  3. Cập nhật model tương ứng trong `ReservationModels.cs` của WPF Client.
  4. Đảm bảo request gửi lên API hoàn toàn trung thực với schema database thực tế.
- **Test chứng minh**: Solution biên dịch thành công, toàn bộ các test khởi tạo DTO và gửi API request đều chạy thông suốt không còn thuộc tính thừa.

---

### Lỗi 9: Báo cáo `DoneFunction3.md` cũ chứa khẳng định sai lệch thực tế
- **Tình huống & Phân tích sai lệch**:
  1. Báo cáo cũ ghi: *"Trường storage_unit_id bắt buộc để NULL"*. Tuy nhiên, trong schema SQL Server thật, bảng `core.reservations` **không hề có** cột `storage_unit_id` (cột này nằm ở bảng `core.unit_allocations`). Do đó khẳng định này là sai sự thật về cấu trúc bảng.
  2. Báo cáo cũ diễn giải: *"BR-REN-01 quy định chu kỳ tính tiền thuê của hệ thống là theo từng tháng"*. Thực chất trong SRS, `BR-REN-01` là quy tắc gửi thông báo gia hạn hợp đồng trước 14 ngày, không phải quy tắc định nghĩa chu kỳ tính tiền.
  3. Báo cáo cũ khẳng định đã chạy kiểm thử SQL Concurrency trên database `SelfStoragePRN222` với các kết quả xóa dữ liệu thật và pass giả.
- **Đã sửa thế nào**:
  - Viết lại toàn bộ tài liệu `AgentReport/DoneFunction3.md` với đầy đủ tính minh bạch, chính xác tuyệt đối theo schema thực tế, làm rõ cơ chế opt-in kiểm thử an toàn, phân tích rõ ràng nguồn gốc quy tắc nghiệp vụ và hiện trạng triển khai.

---

## 3. Câu Hỏi Quyết Định Chính Sách Giá `quoted_total`

Để thống nhất triệt để quy định kinh doanh trước khi bước sang Chức năng 4 (Thanh toán & Hợp đồng), xin ý kiến chỉ đạo của Product Owner / Giảng viên hướng dẫn về chính sách tính tổng tiền báo giá `quoted_total` cho các hợp đồng thuê có thời hạn nhiều tháng ($N > 1$ tháng):

### Bối cảnh nghiệp vụ:
- Theo `BR-RSV-02`, khách hàng được quyền đăng ký thuê từ **1 tháng đến 12 tháng** (ví dụ: thuê 6 tháng).
- Theo `BR-FIN-01`, tiền cọc cố định bằng **1 tháng tiền thuê** (`deposit_amount = monthly_rate`).
- Tại thời điểm đặt chỗ (Chức năng 3), hệ thống phải phát hành một con số báo giá `quoted_total` để khách hàng biết số tiền cần thanh toán khi chuyển sang bước checkout ở Chức năng 4.

### Có 2 phương án chính sách:

```
+----------------------------------------------------------------------------------------------------+
| PHƯƠNG ÁN A (Khuyến nghị - Hiện đang áp dụng trong code):                                          |
| "Thanh toán theo chu kỳ tháng: Tiền cọc + 1 tháng tiền thuê đầu tiên"                             |
|                                                                                                    |
| QuotedTotal = Deposit (1 tháng) + MonthlyRate (1 tháng) + BookingFee - Discount                    |
|                                                                                                    |
| Ưu điểm: Phù hợp mô hình self-storage tiêu chuẩn (trả cọc + thanh toán tháng đầu, các tháng sau    |
| lập hóa đơn định kỳ); khách hàng không phải trả một cục tiền quá lớn ngay khi giữ chỗ.            |
+----------------------------------------------------------------------------------------------------+
                                                HOẶC
+----------------------------------------------------------------------------------------------------+
| PHƯƠNG ÁN B (Theo diễn giải Roadmap dòng 108):                                                     |
| "Thanh toán trọn gói toàn bộ kỳ hạn: Tiền cọc + Toàn bộ N tháng tiền thuê"                         |
|                                                                                                    |
| QuotedTotal = Deposit (1 tháng) + (MonthlyRate * N tháng) + BookingFee - Discount                 |
|                                                                                                    |
| Ưu điểm: Thu đủ toàn bộ tiền thuê của cả hợp đồng ngay từ đầu; giảm thiểu rủi ro nợ xấu.          |
+----------------------------------------------------------------------------------------------------+
```

> **Gợi ý của hệ thống**: Đề xuất duy trì **Phương án A** cho Chức năng 3, vì khi chuyển sang Chức năng 4 (Hợp đồng & Hóa đơn), hệ thống sẽ lập hóa đơn đầu tiên (Invoice kỳ 1) gồm tiền cọc + tháng đầu tiên, các tháng tiếp theo sẽ được tạo hóa đơn định kỳ theo hợp đồng.

---

## 4. Kết Quả Kiểm Thử Cuối Cùng (Verification & Test Results)

### 4.1. Kết quả Build
```bash
dotnet build SelfStorageManagementSystem.sln
```
- **Trạng thái**: **Build succeeded.**
- **Cảnh báo mã**: 0 Warning ngoài các cảnh báo định danh lớp lowercase do EF Scaffold tự sinh (`CS8981`).
- **Lỗi biên dịch**: **0 Error(s)**.

### 4.2. Kết quả Chạy Toàn Bộ Test Suite
```bash
dotnet test tests/SelfStorageManagementSystem.Tests
```
- **Kết quả thực tế**:
  ```
  Test run for D:\FPT\PRN222\Project\Final_Project\tests\SelfStorageManagementSystem.Tests\bin\Debug\net8.0-windows\SelfStorageManagementSystem.Tests.dll (.NETCoreApp,Version=v8.0)
  A total of 1 test files matched the specified pattern.
  [xUnit.net 00:00:00.14] SelfStorageManagementSystem.Tests.ReservationSqlServerConcurrencyTests.ConcurrentReservationHold_WhenOnlyOneSlotAvailable_ExactlyOneSucceedsAndOneFailsWithControlledConflict [SKIP]
    Skipped SelfStorageManagementSystem.Tests.ReservationSqlServerConcurrencyTests... [1 ms]

  Passed!  - Failed: 0, Passed: 117, Skipped: 1, Total: 118, Duration: 1 s
  ```
- **Phân loại các bộ test**:
  1. `ReservationServiceTests`: **16 test cases** (Unit Tests - Đạt 100%)
     - Kiểm tra toàn diện thời hạn thuê (tối thiểu 1 tháng, tối đa 12 tháng, ngày quá khứ).
     - Kiểm tra chặn tài khoản thiếu vai trò khách hàng (`storage_customer`).
     - Kiểm tra tính toán snapshot biểu giá, `hold_until` đúng 15 phút.
     - Kiểm tra tranh chấp đồng thời giữa worker và thao tác xác nhận đơn thanh toán.
     - Kiểm tra từ chối hủy đơn đã xác nhận hoặc đã hết hạn.
     - Kiểm tra hủy đơn giữ chỗ an toàn, lặp lại an toàn (idempotent).
  2. `ReservationIntegrationTests`: **7 test cases** (Integration Tests Web API - Đạt 100%)
     - Xác thực JWT, 401 khi thiếu token.
     - Kiểm tra quyền truy cập 403 khi dùng token nhân viên tạo đơn.
     - Tạo hold thành công với HTTP 201 Created và dữ liệu đếm ngược.
     - Kiểm tra khách hàng khác bị chặn 403 khi truy cập đơn không thuộc sở hữu.
     - Hủy đơn thành công và xử lý lặp lại an toàn (200 OK).
     - Từ chối hủy đơn đã confirmed với HTTP 409 Conflict.
     - Lấy danh sách đơn cá nhân phân trang `GET /api/reservations/mine`.
  3. `ReservationSqlServerConcurrencyTests`: **1 test case** (SQL Server Real Concurrency Test)
     - **Trạng thái**: `[SKIP]` an toàn khi chạy mặc định (không can thiệp dữ liệu thật).
     - Kích hoạt khi có biến môi trường `SELFSTORAGE_SQL_TEST_CONNECTION_STRING`.
  4. Các bộ test hiện có khác trong hệ thống (Auth, Admin, Scope, Catalog): **94 test cases** (Đạt 100%).

---

## 5. Trạng Thái Mã Nguồn & Cam Kết Quy Tắc

1. **Bảo toàn thay đổi của người dùng**:
   - File `docs/FIVE_CORE_FUNCTIONS_SUMMARY.md` hoàn toàn được giữ nguyên trạng thái sửa đổi của người dùng, không bị format hay hoàn tác.
2. **Nguyên vẹn Database-First & Scaffolded Entities**:
   - Không can thiệp vào các entity scaffolded hoặc `SelfStorageDbContext.cs`.
   - Đúng tầng kiến trúc: DataAccess -> BusinessLogic -> Presentation / WpfClient.
3. **Quy tắc Git**:
   - Không thực hiện commit hay push lên remote.
   - Sẵn sàng để người dùng xem xét diff và tiếp tục triển khai Chức năng 4.
