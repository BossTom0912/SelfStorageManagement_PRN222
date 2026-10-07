# Báo Cáo Cập Nhật & Sửa Lỗi Chức Năng 3: Reservation & 15-Minute Hold

- **Dự án**: Self-Storage Facility Rental and Management System (PRN222)
- **Thời gian hoàn thành cập nhật**: 07/10/2026
- **Branch hiện tại**: `Facility&StorageUnitCatalog`
- **Kiến trúc áp dụng**: Strict 3-Layer Architecture + WPF Client (.NET 8, ASP.NET Core Web API, EF Core 8 Database-First, SQL Server `core` schema)
- **Tình trạng kiểm thử**: **117 Passed, 1 Skipped (Opt-in SQL Integration), 0 Failed, 0 Warnings CS, 0 Build Errors**

---

## 1. Tổng Hợp Các Lỗi Review Đã Xác Nhận & Phương Án Khắc Phục

| STT | Vấn đề review phát hiện | Mức độ | Hiện trạng trước khi sửa | Giải pháp & Thay đổi đã hoàn tất |
| :---: | :--- | :---: | :--- | :--- |
| **1** | **Bài test SQL có thể xóa dữ liệu thật & báo pass giả** (`ReservationSqlServerConcurrencyTests.cs`) | **Critical** | Kết nối trực tiếp DB `SelfStoragePRN222`, gọi `RemoveRange` xóa mọi reservation trùng ngày, hardcode `customer_id = 7`, `return` khi lỗi kết nối tạo kết quả Pass giả. | Áp dụng `[SqlIntegrationFact]` yêu cầu opt-in qua biến môi trường `SELFSTORAGE_SQL_TEST_CONNECTION_STRING`. Khi không có, xUnit báo `[SKIP]` minh bạch. Xóa bỏ `RemoveRange`. Tự tạo fixture độc lập và dọn dẹp sạch sẽ đúng fixture đó trong `finally`. |
| **2** | **Worker có thể đổi đơn đã xác nhận thành `expired`** (`ReservationRepository.cs`) | **Critical** | Worker đọc danh sách `pending`/`awaiting_deposit` ra bộ nhớ rồi mới SaveChanges. Nếu khách thanh toán đồng thời chuyển sang `confirmed`, worker ghi đè thành `expired`. | Cập nhật có điều kiện trực tiếp tại thời điểm ghi (`conditional atomic update`). Chỉ dọn dẹp invoice khi `affected > 0`. Đơn đã `confirmed` không bị ảnh hưởng. |
| **3** | **Hết hạn khi GET bỏ qua dọn dẹp hóa đơn & check quyền muộn** (`ReservationService.cs`, `ReservationRepository.cs`) | **High** | Nhánh GET expire chỉ đổi status đơn, không void invoice nháp; worker không quét thấy đơn đó nữa khiến invoice mở vĩnh viễn. Kiểm tra quyền sở hữu diễn ra sau thao tác ghi. | Đưa `VerifyAccessPermissionAsync` lên đầu phương thức GET. Dùng chung hàm `TryExpireSingleReservationIfOverdueAsync` trong transaction để void invoice nháp. Đọc lại DB nếu tranh chấp cập nhật. |
| **4** | **Hủy đơn `confirmed` nhưng không áp dụng cancellation policy** (`ReservationService.cs`, `ReservationRepository.cs`) | **High** | Cho phép hủy đơn `confirmed` mà không có quy trình hoàn tiền, phạt cọc hay hợp đồng. | Thu hẹp phạm vi Chức năng 3: Chỉ cho phép hủy đơn khi đang giữ chỗ (`pending`/`awaiting_deposit` và còn hạn 15 phút). Đơn `confirmed` bị từ chối với `ConflictException` (HTTP 409). Ràng buộc `CanCancel` trong DTO và UI. |
| **5** | **Đặt chỗ từ Floor Map tự bịa giá cọc / booking fee khi chưa load rate** (`FacilityCatalogWindow.xaml.cs`) | **Medium** | Tab Floor Map có fallback `DepositAmount = MonthlyRate; BookingFee = 0;` khi chưa nạp `unitType`, dẫn tới gửi giá sai lên API hoặc lỗi trigger. | Xóa bỏ fallback giả lập. Gọi API catalog lấy biểu phí thực tế theo ngày thuê. Nếu không tìm thấy rate hợp lệ, cảnh báo người dùng và dừng đặt chỗ. |
| **6** | **Công thức tính `quoted_total` cho hợp đồng nhiều tháng chưa thống nhất** (`ReservationService.cs`) | **Medium** | Mâu thuẫn giữa chu kỳ thanh toán tháng (`BR-RSV-04`) và Roadmap dòng 108 (thu toàn bộ tiền thuê các tháng đăng ký ngay khi checkout đặt chỗ). | Chuẩn hóa theo chu kỳ thanh toán tháng: `Deposit (1 tháng) + MonthlyRate (1 tháng kỳ đầu) + BookingFee - Discount`. Lập bảng đối chiếu và đề xuất phương án xin ý kiến. |
| **7** | **Thiếu kiểm tra vai trò khách hàng (`storage_customer`) trên API** (`ReservationsController.cs`, `ReservationService.cs`) | **High** | API và Service chỉ kiểm tra có đăng nhập và có `customer_profile`, nhân viên/quản lý có thể gọi API tạo đặt chỗ của khách. | Gắn `[Authorize(Roles = RoleConstants.StorageCustomer)]` cho `POST /api/reservations` và `GET /api/reservations/mine`. Bổ sung kiểm tra vai trò trong Service. Dùng đúng navigation `user_roleusers`. |
| **8** | **DTO nhận `CustomerNote` nhưng schema không có cột và bị vứt bỏ** (`CreateReservationRequest.cs`, `ConfirmReservationDialog.xaml/.cs`) | **Low** | Request nhận `CustomerNote` nhưng bảng `core.reservations` không có cột lưu ghi chú, gây hiểu lầm cho người dùng. | Xóa bỏ hoàn toàn trường `CustomerNote` khỏi DTO, UI XAML Dialog và code-behind. Request phản ánh trung thực schema database. |
| **9** | **Báo cáo `DoneFunction3.md` cũ chứa khẳng định sai lệch thực tế** (`AgentReport/DoneFunction3.md`) | **Medium** | Khẳng định sai `storage_unit_id = NULL` (schema không có cột này), diễn giải sai `BR-REN-01`, mô tả test SQL nguy hiểm. | Viết lại toàn bộ báo cáo phản ánh trung thực schema, đối chiếu đúng SRS Business Rules và cập nhật trạng thái test. |

---

## 2. Danh Sách Các File Đã Thay Đổi và Phạm Vi Can Thiệp

| STT | File thay đổi | Tầng kiến trúc | Phạm vi can thiệp |
| :---: | :--- | :--- | :--- |
| 1 | `tests/SelfStorageManagementSystem.Tests/ReservationSqlServerConcurrencyTests.cs` | Tests | Thêm `[SqlIntegrationFact]`, xóa `RemoveRange`, cấp phát fixture độc lập và dọn dẹp an toàn. |
| 2 | `src/SelfStorageManagementSystem.DataAccess/Repositories/Implementations/ReservationRepository.cs` | DataAccess | Cập nhật nguyên tử có điều kiện cho worker, expire-on-read và cancel; dọn dẹp invoice nháp trong transaction; hỗ trợ chạy đa môi trường (SQL Server / In-Memory). |
| 3 | `src/SelfStorageManagementSystem.BusinessLogic/Services/Implementations/ReservationService.cs` | BusinessLogic | Kiểm tra quyền trước khi ghi trong GET; kiểm tra role `storage_customer`; chặn hủy đơn `confirmed`; bảo toàn dữ liệu thật khi tranh chấp. |
| 4 | `src/SelfStorageManagementSystem.Presentation/Controllers/ReservationsController.cs` | Presentation | Gắn `[Authorize(Roles = RoleConstants.StorageCustomer)]` bảo vệ endpoint khách hàng. |
| 5 | `src/SelfStorageManagementSystem.WpfClient/Views/FacilityCatalogWindow.xaml.cs` | WPF Client | Xóa fallback gán giá giả lập trên Floor Map; gọi API catalog lấy biểu phí thật hoặc cảnh báo dừng luồng. |
| 6 | `src/SelfStorageManagementSystem.BusinessLogic/DTOs/Requests/Reservations/CreateReservationRequest.cs` | BusinessLogic | Xóa bỏ trường `CustomerNote`. |
| 7 | `src/SelfStorageManagementSystem.WpfClient/Views/ConfirmReservationDialog.xaml` & `.xaml.cs` | WPF Client | Xóa ô nhập Ghi chú khách hàng không có trong schema DB. |
| 8 | `src/SelfStorageManagementSystem.WpfClient/Models/ReservationModels.cs` | WPF Client | Đồng bộ bỏ `CustomerNote` trên client model. |
| 9 | `tests/SelfStorageManagementSystem.Tests/ReservationServiceTests.cs` | Tests | Bổ sung unit tests cho role check, concurrent confirmation không bị worker ghi đè, và chặn hủy đơn confirmed. |
| 10 | `tests/SelfStorageManagementSystem.Tests/ReservationIntegrationTests.cs` | Tests | Bổ sung integration test phân quyền role, kiểm tra conflict khi hủy đơn confirmed, mở rộng seed capacity lên 10 units. |
| 11 | `AgentReport/DoneFunction3.md` | Tài liệu | Đính chính toàn bộ báo cáo nghiệm thu Chức năng 3. |

---

## 3. Chi Tiết Khắc Phục Từng Lỗi & Test Chứng Minh

### 1. Cách ly và bảo vệ an toàn cho test đồng thời SQL Server (Lỗi 1)
- **Giải pháp**: Xây dựng `SqlIntegrationFactAttribute` kiểm tra biến môi trường `SELFSTORAGE_SQL_TEST_CONNECTION_STRING`. Nếu không được thiết lập, test lập tức ghi nhận trạng thái `[SKIP]` minh bạch. Khi được kích hoạt, test chỉ thao tác trên database kiểm thử được cấu hình, tự tạo các bản ghi test với mã và ID động, và dọn dẹp chính xác các bản ghi đó trong khối `finally`.
- **Test chứng minh**: Chạy `dotnet test tests/SelfStorageManagementSystem.Tests` hiển thị:
  `[SKIP] ...ReservationSqlServerConcurrencyTests... Skipped: 1 (requires explicit opt-in via environment variable)`.

### 2. Ngăn chặn worker ghi đè đơn đã xác nhận thanh toán (Lỗi 2)
- **Giải pháp**: Thay thế quy trình đọc-rồi-lưu bằng cập nhật nguyên tử có điều kiện:
  `UPDATE core.reservations SET status = 'expired', updated_at = @now WHERE id = @id AND status IN ('pending', 'awaiting_deposit') AND hold_until <= @now`.
  Nếu một giao dịch khác đã thanh toán thành công và chuyển đơn thành `confirmed`, câu lệnh UPDATE tác động 0 dòng (`affected == 0`). Trạng thái đơn `confirmed` được bảo toàn nguyên vẹn.
- **Test chứng minh**: Test case `ExpireOverdueHolds_WhenConcurrentConfirmationOccurs_DoesNotOverwriteConfirmed` trong `ReservationServiceTests.cs`.

### 3. Đồng bộ dọn dẹp hóa đơn và kiểm tra quyền trước khi đọc/ghi (Lỗi 3)
- **Giải pháp**:
  - Di chuyển lệnh `VerifyAccessPermissionAsync` lên đầu phương thức `GetReservationByIdAsync`. Nếu người dùng không phải chủ đơn hoặc quản trị viên, request bị từ chối `403 Forbidden` ngay lập tức trước khi thực hiện bất kỳ lệnh ghi nào.
  - Sử dụng chung phương thức `TryExpireSingleReservationIfOverdueAsync` trong transaction để đổi trạng thái đơn và void các hóa đơn nháp liên quan (`paid_amount == 0`).
  - Nếu cập nhật không thành công do đơn vừa được xác nhận thanh toán, service nạp lại dữ liệu thật từ DB thay vì tự ý gán response là `expired`.
- **Test chứng minh**: Test case `GetReservationById_ForbiddenForOtherCustomer` trong `ReservationIntegrationTests.cs`.

### 4. Chặn hủy đơn đã thanh toán khi chưa có chính sách hoàn cọc (Lỗi 4)
- **Giải pháp**: Thu hẹp phạm vi Chức năng 3 theo đúng nghiệp vụ đặt chỗ & giữ chỗ 15 phút. Ném ngoại lệ `ConflictException` (HTTP 409) khi nhận yêu cầu hủy đơn đã ở trạng thái `confirmed`. Ràng buộc `CanCancel = isPendingHold && hold_until > nowUtc` để giao diện WPF Client tự động ẩn hoặc vô hiệu hóa nút hủy khi đơn đã được xác nhận thanh toán.
- **Test chứng minh**: Unit test `CancelReservation_WhenConfirmed_ThrowsConflictException` và Integration test `CancelReservation_WhenConfirmed_ReturnsConflict`.

### 5. Loại bỏ giá trị giả lập trên sơ đồ mặt bằng (Lỗi 5)
- **Giải pháp**: Trong sự kiện `BtnProceedFromMap_Click` của `FacilityCatalogWindow.xaml.cs`, xóa bỏ hoàn toàn đoạn fallback gán cứng `DepositAmount = MonthlyRate; BookingFee = 0;`. Nếu loại kho chưa có sẵn trong bộ nhớ, hệ thống gọi API `ApiClient.Instance.GetFacilityUnitTypesAsync` với ngày thuê thực tế để nạp biểu phí chuẩn xác từ database. Nếu không có biểu phí hợp lệ, hệ thống dừng luồng đặt chỗ và cảnh báo người dùng kiểm tra lại bộ lọc.

### 6. Chuẩn hóa công thức `quoted_total` & Nêu câu hỏi chính sách (Lỗi 6)
- **Giải pháp**: Áp dụng chuẩn công thức theo chu kỳ thanh toán tháng:
  $$\text{QuotedTotal} = \text{DepositSnapshot (1 tháng cọc)} + \text{MonthlyRateSnapshot (1 tháng thuê kỳ đầu)} + \text{BookingFee} - \text{Discount}$$
  Đảm bảo vượt qua trigger `trg_reservations_validate_rate` và chuẩn bị đúng số tiền cần thanh toán cho kỳ cọc và tháng đầu tiên khi chuyển sang Chức năng 4.

### 7. Thắt chặt kiểm tra vai trò `storage_customer` trên API và Service (Lỗi 7)
- **Giải pháp**: Gắn thuộc tính `[Authorize(Roles = RoleConstants.StorageCustomer)]` cho endpoint `POST /api/reservations` và `GET /api/reservations/mine`. Tại `ReservationService.cs`, bổ sung kiểm tra bắt buộc tài khoản phải sở hữu role `storage_customer` trong bảng `user_roles` thông qua navigation property `user_roleusers`.
- **Test chứng minh**: Unit test `CreateReservationHold_UserWithoutCustomerRole_ThrowsForbiddenException` và Integration test `CreateReservation_WithNonCustomerRole_ReturnsForbidden`.

### 8. Loại bỏ thuộc tính thừa `CustomerNote` (Lỗi 8)
- **Giải pháp**: Đối chiếu schema `core.reservations` xác nhận không có cột ghi chú. Loại bỏ hoàn toàn trường `CustomerNote` khỏi DTO `CreateReservationRequest`, XAML `ConfirmReservationDialog.xaml`, code-behind và các model client. Request gửi lên API đảm bảo trung thực 100% với schema database.

### 9. Đính chính và cập nhật tài liệu báo cáo (Lỗi 9)
- **Giải pháp**: Viết lại hoàn chỉnh `AgentReport/DoneFunction3.md` đính chính việc bảng `core.reservations` không có cột `storage_unit_id` (không tạo allocation theo `BR-RSV-03`), làm rõ quy tắc `BR-REN-01` là thông báo gia hạn hợp đồng, và ghi nhận minh bạch cơ chế opt-in cho test tích hợp SQL Server.

---

## 4. Câu Hỏi Quyết Định Chính Sách Giá `quoted_total`

Khi khách hàng thuê kho có kỳ hạn nhiều tháng ($N > 1$, ví dụ 6 tháng) với tiền cọc bằng 1 tháng tiền thuê (`BR-FIN-01`):

**Hệ thống nên áp dụng chính sách nào cho `quoted_total` tại bước giữ chỗ?**

* **Phương án A (Khuyến nghị - Hiện đang áp dụng trong code)**:
  $$\text{QuotedTotal} = \text{Tiền cọc (1 tháng)} + \text{Tiền thuê tháng đầu tiên (1 tháng)} + \text{Phí đặt chỗ} - \text{Giảm giá}$$
  *Lý do*: Tuân thủ quy tắc `BR-RSV-04` ("Tiền cọc + Tiền thuê kỳ đầu"); phù hợp mô hình self-storage thanh toán định kỳ theo từng tháng; giảm áp lực tài chính ban đầu cho khách hàng. Hóa đơn các tháng tiếp theo sẽ được phát hành định kỳ trong Chức năng 4–5.

* **Phương án B (Theo diễn giải Roadmap dòng 108)**:
  $$\text{QuotedTotal} = \text{Tiền cọc (1 tháng)} + (\text{Tiền thuê mỗi tháng} \times N) + \text{Phí đặt chỗ} - \text{Giảm giá}$$
  *Lý do*: Yêu cầu thanh toán trọn gói toàn bộ tiền thuê của cả hợp đồng ngay tại thời điểm checkout đặt chỗ.

---

## 5. Kết Quả Kiểm Thử Cuối Cùng

- **Build solution**: `dotnet build SelfStorageManagementSystem.sln` $\rightarrow$ **Build succeeded (0 Error, 0 Warning CS)**.
- **Test suite**: `dotnet test tests/SelfStorageManagementSystem.Tests`
  ```text
  Test run for D:\FPT\PRN222\Project\Final_Project\tests\SelfStorageManagementSystem.Tests\bin\Debug\net8.0-windows\SelfStorageManagementSystem.Tests.dll (.NETCoreApp,Version=v8.0)
  A total of 1 test files matched the specified pattern.
  [xUnit.net 00:00:00.18] SelfStorageManagementSystem.Tests.ReservationSqlServerConcurrencyTests.ConcurrentReservationHold_WhenOnlyOneSlotAvailable_ExactlyOneSucceedsAndOneFailsWithControlledConflict [SKIP]
    Skipped SelfStorageManagementSystem.Tests.ReservationSqlServerConcurrencyTests... [1 ms]

  Passed!  - Failed: 0, Passed: 117, Skipped: 1, Total: 118, Duration: 1 s
  ```
- **Kiểm tra git format**: `git diff --check` $\rightarrow$ **Pass (0 lỗi format)**.
- **Trạng thái git**: Giữ nguyên vẹn thay đổi của người dùng tại `docs/FIVE_CORE_FUNCTIONS_SUMMARY.md`; không commit, không push.
