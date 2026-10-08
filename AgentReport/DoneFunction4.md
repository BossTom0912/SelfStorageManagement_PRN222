# Báo Cáo Hoàn Thiện & Nghiệm Thu Chức Năng 4: Payment & Rental Agreement
*(Bao gồm khắc phục triệt để 12 phát hiện review chuyên sâu và 6 vấn đề ưu tiên P1/P2)*

- **Dự án**: Self-Storage Facility Rental and Management System
- **Học phần**: PRN222
- **Thời gian hoàn thành**: 08/10/2026
- **Branch**: `Payment&RentalAgreement` (bắt đầu từ commit `adc3362`)
- **Kiến trúc áp dụng**: Strict 3-Layer Architecture + WPF Client (.NET 8, ASP.NET Core Web API, EF Core 8 Database-First, SQL Server `core` schema)
- **Tình trạng kiểm thử**: **176 Passed, 2 Skipped (Opt-in SQL Concurrency), 0 Failed, 0 CS Warnings, 0 Build Errors**

---

## 1. Tổng Quan Triển Khai & Khắc Phục Lỗi Review

Chức năng 4 (Payment & Rental Agreement) đã được hoàn thiện đầy đủ trên toàn bộ hệ thống bao gồm DataAccess, BusinessLogic, Presentation (Web API), WPF Client, Unit Tests, Integration Tests và tài liệu kỹ thuật.

### A. 12 phát hiện review chuyên sâu ban đầu:
1. **Chặn việc tự xác nhận thanh toán giả** (Demo simulator chỉ cho phép khi có cờ cấu hình và đúng provider).
2. **Kiểm tra đầy đủ IPN trước khi ghi tiền** (Đối chiếu chữ ký, TmnCode, tiền tệ VND, số tiền khớp, TxnRef tồn tại, xử lý idempotent).
3. **Ghi nhận khoản tiền thứ hai thay vì để callback lỗi** (Duplicate payment được ghi nhận succeeded + `reconciliation_required = true` + tạo đúng 1 bản ghi `refunds`, không tạo trùng agreement).
4. **Quyết định hold bằng thời gian hiện tại server UTC và xử lý race** (Dùng thời gian server UTC và khóa `sp_getapplock` trong worker quét quá hạn).
5. **Cập nhật lại invoice lines khi retry quote thay đổi** (Tiền thân của chính sách Invoice Immutability).
6. **Đồng nhất thứ tự khóa và thời gian chờ** (Khóa `SS_Reservation` trước `SS_Promotion`, timeout 5000ms).
7. **Bắt buộc kiểm tra payload trùng lặp khi nhận Idempotency-Key** (Kiểm tra customer, reservation, provider, amount; lệch trả 409 Conflict; khớp trả lại attempt cũ).
8. **Đảm bảo toàn bộ VND là số nguyên** (`VnpayGateway` ném ngoại lệ nếu số lẻ, quote/checkout làm tròn nguyên đồng VND).
9. **Đóng dấu phiên bản điều khoản và loại bỏ nhãn "ký số"** (Lưu `accepted_policy_version_id` vào `rental_agreement.policy_version_id`, `signed_at = null`, bỏ nhãn ký số gây hiểu nhầm).
10. **Giao diện WPF minh bạch về trạng thái và rủi ro** (Thêm view cảnh báo `panelReconciliationOutcome`, hiển thị điều khoản từ quote, ẩn nút giả lập khi chọn VNPAY).
11. **Bổ sung quản lý và phê duyệt hoàn tiền** (Endpoint `GET /api/payments/refunds` và `POST /api/payments/refunds/{id}/review`, DTOs, bảng `refund_approvals`).
12. **Chi tiết kiểm thử và hạ tầng** (Fail-fast thiếu credentials, toán tử rule promotion `gte`/`lte`/`eq`/`in`, chặn `free_days` lẻ, skip an toàn test SQL, `payment_method = other` cho demo, đảo ngược thứ tự dọn dẹp test database).

### B. 6 vấn đề sửa chữa ưu tiên P1/P2 bổ sung:
1. **[P1] Giới hạn phạm vi cơ sở (Facility Scope) trên API hoàn tiền**: `GetRefundsAsync` và `ReviewRefundAsync` được tích hợp `IFacilityScopeService`. Lọc danh sách `refunds` trực tiếp ở SQL query theo các cơ sở nhân viên được phân công; từ chối trả về `403 Forbidden` nếu nhân viên duyệt refund cơ sở khác; quản trị viên có quyền toàn hệ thống.
2. **[P1] Tính bất biến của hóa đơn (Invoice Immutability) & Kiểm tra thanh toán đủ trước khi cấp hợp đồng**: Không sửa lại hóa đơn đã phát hành khi đã có payment attempt tham chiếu; thay vào đó hủy hóa đơn cũ (`status = 'voided'`) và phát hành hóa đơn mới. Late callback trỏ vào hóa đơn `voided` được chuyển sang đối soát và tạo refund; bắt buộc `invoice.status == 'paid' && invoice.paid_amount == invoice.total_amount` trước khi cấp hợp đồng.
3. **[P2] Ưu tiên Idempotency Replay trước khi kiểm tra hạn mức voucher**: Đưa kiểm tra Idempotency Key lên đầu `CheckoutAsync`. So khớp toàn bộ payload; trùng khớp thì trả ngay kết quả cũ mà không kiểm tra lại `usage_limit`.
4. **[P2] Giải phóng voucher khi Retry bỏ mã & Gắn voucher vào Payment Metadata**: Khi retry không dùng voucher, giải phóng các redemption `reserved` cũ (`status = 'released'`) và reset `reservation.discount_snapshot = 0`. Finalization chỉ áp dụng redemption nếu metadata payment có khuyến mãi đó.
5. **[P2] Sửa ràng buộc khóa ngoại `refund_approvals.decided_by`**: Chuyển FK trỏ tới `[core].users(id)` để quản trị viên hệ thống có thể duyệt hoàn tiền mà không bị lỗi FK 547.
6. **[P2] Chuẩn hóa mã lỗi HTTP cho duyệt hoàn tiền**: Trả về `404 Not Found` khi refund không tồn tại và `409 Conflict` khi refund đã được xử lý hoặc có xung đột đồng thời.

---

## 2. Bảng Đối Chiếu Các Vấn Đề Kỹ Thuật

| STT | Vấn đề | Hiện trạng trước khi sửa | Giải pháp kỹ thuật đã triển khai | File sửa đổi & Bằng chứng kiểm thử |
| :--- | :--- | :--- | :--- | :--- |
| **1** | **Chặn tự xác nhận thanh toán giả** | Coi thiếu config là true, cho phép chạy ngoài Dev/Test, không check provider demo. | Mặc định tắt; chỉ cho phép trong Dev/Test khi `Payment:AllowDemoSimulator == true`. Từ chối mọi provider khác demo. WPF ẩn nút giả lập khi chọn VNPAY. | `PaymentService.cs`<br>`CheckoutWindow.xaml.cs`<br>`DemoPaymentComplete_InProduction_ThrowsForbiddenException`<br>`DemoPaymentComplete_ForNonDemoProvider_ThrowsBadRequestException` |
| **2** | **Kiểm tra đầy đủ IPN trước khi ghi tiền** | IPN chỉ check signature mà không đối chiếu TmnCode, Currency, Amount; `TxnRef` sai ném 500. | Đối chiếu TxnRef, provider=vnpay, TmnCode cấu hình (sai trả 97), Currency=VND (sai trả 04), amount khớp (sai trả 04). Đơn không tồn tại trả 01. Đơn đã succeeded trả 02. | `PaymentService.cs`<br>`PaymentsController.cs`<br>`ProcessVnpayIpn_InvalidSignature_ReturnsCode97`<br>`ProcessVnpayIpn_AmountMismatch_ReturnsCode04` |
| **3** | **Ghi nhận khoản tiền thứ hai (Duplicate payment)** | Attempt thứ hai cố allocation vào invoice đã paid bị trigger chặn ném 500. | Ghi nhận payment `succeeded`, gắn cờ `reconciliation_required = true`, tạo đúng 1 bản ghi `refunds` (`requested`), không tạo agreement trùng. | `PaymentRepository.cs`<br>`ProcessPayment_SecondPaymentOnAlreadyPaidInvoice_FlagsReconciliationAndCreatesRefund` |
| **4** | **Quyết định hold bằng thời gian hiện tại server UTC** | So sánh hold với PayDate từ gateway; worker expiry không dùng applock chung. | Đánh giá hold bằng server UTC time (`UtcNow <= hold_until`). Worker lấy khóa `sp_getapplock` trước khi expire. | `ReservationRepository.cs`<br>`PaymentRepository.cs`<br>`DemoPaymentComplete_ExpiredHold_FlagsReconciliationAndCreatesRefund` |
| **5** | **Tính bất biến của hóa đơn (Invoice Immutability) khi Retry** | Sửa/xóa dòng tiền trên hóa đơn cũ mà payment attempt 1 vẫn tham chiếu. | Void hóa đơn cũ (`status = 'voided'`, `voided_at = nowUtc`) và phát hành hóa đơn mới. Callback trỏ vào hóa đơn voided được chuyển sang reconciliation và tạo refund. Bắt buộc kiểm tra thanh toán đủ 100% trước khi lập agreement. | `PaymentRepository.cs`<br>`Checkout_RetryWithDifferentQuote_VoidsOldInvoice_AndLatePaymentRoutesToReconciliation`<br>`FinalizePayment_WhenInvoicePartiallyPaid_DoesNotCreateAgreementOrConfirmReservation` |
| **6** | **Đồng nhất thứ tự khóa và thời gian chờ** | Nguy cơ deadlock giữa Reservation và Promotion; thiếu timeout. | Luôn lấy khóa `SS_Reservation_{id}` trước, sau đó lấy `SS_Promotion_{id}`; timeout 5000ms. | `PaymentRepository.cs` |
| **7** | **Ưu tiên Idempotency Replay trước khi kiểm tra voucher** | Replay bị chặn bởi `usage_limit` voucher; không so sánh đầy đủ payload. | Di chuyển kiểm tra Idempotency lên đầu `CheckoutAsync`. So sánh customer, reservation, provider, method, policy, promo code. Lệch trả 409; khớp trả lại kết quả replay ngay. | `PaymentService.cs`<br>`Checkout_IdempotencyReplay_WhenVoucherLimitReached_SucceedsWithoutRevalidatingVoucher`<br>`Checkout_IdempotencyReplay_WhenPayloadMismatched_ThrowsConflictException` |
| **8** | **Giải phóng voucher khi Retry bỏ mã** | Retry bỏ voucher giữ nguyên `discount_snapshot` và redemption `reserved`. | Release toàn bộ redemption `reserved` cũ, reset `discount_snapshot = 0`. Chỉ apply redemption khi payment metadata có `promotion_id`. | `PaymentRepository.cs`<br>`PaymentService.cs`<br>`Checkout_RetryWithoutVoucher_ReleasesExistingReservedRedemption` |
| **9** | **Phân quyền cơ sở (Facility Scope) cho API hoàn tiền** | Nhân viên cơ sở A xem và duyệt được refund cơ sở B do chỉ kiểm tra role. | Áp dụng `_facilityScopeService.GetAccessibleFacilityIdsAsync` lọc ở SQL query. `ReviewRefundAsync` ném `403 Forbidden` nếu thao tác ngoài cơ sở. | `PaymentService.cs`<br>`PaymentRepository.cs`<br>`GetRefunds_FacilityStaff_ReturnsOnlyAssignedFacilityRefunds`<br>`ReviewRefund_FacilityStaff_ReviewingOtherFacility_ThrowsForbiddenException` |
| **10** | **Sửa khóa ngoại `refund_approvals.decided_by` trỏ tới `users(id)`** | FK trỏ tới `employee_profiles(user_id)` khiến Admin duyệt refund bị lỗi FK 547. | Sửa FK trỏ tới `[core].users(id)`, cập nhật EF navigation properties và mapping DbContext. | `SelfStoragePRN222_SQLServer_CleanInstall.sql`<br>`refund_approval.cs`<br>`user.cs`<br>`ReviewRefund_SystemAdministrator_CanReviewAnyFacility` |
| **11** | **Chuẩn hóa mã lỗi HTTP cho duyệt hoàn tiền (404/409)** | Refund không tồn tại hoặc đã duyệt bị trả lỗi HTTP 500. | Map lỗi sang `NotFoundException` (404) và `ConflictException` (409) rõ ràng. | `PaymentService.cs`<br>`PaymentsController.cs`<br>`ReviewRefund_NonExistentRefund_ThrowsNotFoundException`<br>`ReviewRefund_AlreadyDecidedRefund_ThrowsConflictException` |
| **12** | **Đảm bảo toàn bộ VND là số nguyên & Đóng dấu điều khoản** | `Amount % 1 != 0` gây sai lệch VNPAY; agreement thiếu lưu `policy_version_id`. | Ném `ArgumentException` nếu VND lẻ. Lưu `accepted_policy_version_id` vào `rental_agreement.policy_version_id`, `signed_at = null`. | `VnpayGateway.cs`<br>`PaymentRepository.cs`<br>`CreateCheckoutUrlAsync_FractionalAmount_ThrowsArgumentException` |

---

## 3. Danh Sách Tệp Tin Triển Khai & Cập Nhật

### A. DataAccess Layer
- `DB/SelfStoragePRN222_SQLServer_CleanInstall.sql` *(Modified)*: Sửa FK `refund_approvals.decided_by` trỏ tới `[core].users(id)`.
- `src/SelfStorageManagementSystem.DataAccess/Entities/refund_approval.cs` *(Modified)*: Chuyển `decided_byNavigation` sang kiểu `user`.
- `src/SelfStorageManagementSystem.DataAccess/Entities/user.cs` *(Modified)*: Thêm navigation collection `refund_approvals`.
- `src/SelfStorageManagementSystem.DataAccess/Entities/employee_profile.cs` *(Modified)*: Loại bỏ navigation collection `refund_approvals`.
- `src/SelfStorageManagementSystem.DataAccess/Context/SelfStorageDbContext.cs` *(Modified)*: Cập nhật Fluent API mapping cho quan hệ `refund_approval -> user`.
- `src/SelfStorageManagementSystem.DataAccess/Repositories/Interfaces/IPaymentRepository.cs` *(Modified)*: Bổ sung tham số `accessibleFacilityIds` cho `GetRefundsAsync`.
- `src/SelfStorageManagementSystem.DataAccess/Repositories/Implementations/PaymentRepository.cs` *(Modified)*:
  - Lọc `GetRefundsAsync` theo `accessibleFacilityIds` tại câu lệnh truy vấn LINQ/SQL.
  - Tải graph `payment.target_invoice.reservation.facility` trong `GetRefundByIdAsync`.
  - Triển khai **Invoice Immutability**: Hủy hóa đơn cũ (`status = 'voided'`) và tạo hóa đơn mới khi quote thay đổi hoặc đã có payment attempt.
  - Giải phóng redemption `reserved` khi checkout lại bỏ mã (`status = 'released'`).
  - Ghi nhận `reconciliation_required = true` và sinh `refund` khi payment callback trỏ vào hóa đơn `voided`.
  - Phân bổ đúng `effectiveAmount = parameters.Amount > 0 ? parameters.Amount : payment.amount`.
  - Bắt buộc kiểm tra `invoice.status == "paid" && invoice.paid_amount == invoice.total_amount` trước khi cấp `rental_agreement`.

### B. BusinessLogic Layer
- `src/SelfStorageManagementSystem.BusinessLogic/Services/Interfaces/IPaymentService.cs` *(Modified)*: Giữ contract sạch cho checkout, IPN, demo complete, refund management.
- `src/SelfStorageManagementSystem.BusinessLogic/Services/Implementations/PaymentService.cs` *(Modified)*:
  - Tích hợp `IFacilityScopeService` vào kiểm tra quyền cơ sở cho `GetRefundsAsync` và `ReviewRefundAsync`.
  - Nâng ưu tiên Idempotency Replay lên trước bước tính discount và voucher limit; kiểm tra toàn vẹn payload.
  - Lưu `promotion_id` vào payment metadata và chỉ apply redemption khi payment thực sự dùng khuyến mãi đó.
  - Bắt lỗi chuẩn hóa HTTP 404 (`NotFoundException`) và 409 (`ConflictException`) cho duyệt refund.

### C. Presentation Layer (ASP.NET Core Web API)
- `src/SelfStorageManagementSystem.Presentation/Controllers/PaymentsController.cs` *(Modified)*:
  - Thêm `[ProducesResponseType(StatusCodes.Status409Conflict)]` trên `ReviewRefund`.
  - Chuẩn hóa mã phản hồi IPN VNPAY (00, 01, 02, 04, 97).
  - Cập nhật trang HTML demo simulator: hướng dẫn người dùng hoàn tất qua client có xác thực JWT thay vì mở endpoint unauthenticated.

### D. WPF Client
- `src/SelfStorageManagementSystem.WpfClient/Views/CheckoutWindow.xaml` *(Modified)*:
  - ScrollViewer hiển thị điều khoản thuê kho.
  - View cảnh báo đối soát `panelReconciliationOutcome`.
  - Ẩn nút giả lập demo khi chọn VNPAY.
- `src/SelfStorageManagementSystem.WpfClient/Views/CheckoutWindow.xaml.cs` *(Modified)*:
  - Xử lý phân nhánh kết quả thanh toán theo cờ `ReconciliationRequired`.

### E. Kiểm thử tự động (Tests)
- `tests/SelfStorageManagementSystem.Tests/PaymentServiceTests.cs` *(Modified)*:
  - Bổ sung 8 bài test chuyên sâu cho 6 vấn đề ưu tiên:
    - `GetRefunds_FacilityStaff_ReturnsOnlyAssignedFacilityRefunds`
    - `ReviewRefund_FacilityStaff_ReviewingOtherFacility_ThrowsForbiddenException`
    - `ReviewRefund_SystemAdministrator_CanReviewAnyFacility`
    - `ReviewRefund_NonExistentRefund_ThrowsNotFoundException`
    - `ReviewRefund_AlreadyDecidedRefund_ThrowsConflictException`
    - `Checkout_IdempotencyReplay_WhenVoucherLimitReached_SucceedsWithoutRevalidatingVoucher`
    - `Checkout_IdempotencyReplay_WhenPayloadMismatched_ThrowsConflictException`
    - `Checkout_RetryWithoutVoucher_ReleasesExistingReservedRedemption`
    - `Checkout_RetryWithDifferentQuote_VoidsOldInvoice_AndLatePaymentRoutesToReconciliation`
    - `FinalizePayment_WhenInvoicePartiallyPaid_DoesNotCreateAgreementOrConfirmReservation`

---

## 4. Bằng Chứng Thực Thi Kiểm Thử Tự Động

### Lệnh chạy kiểm thử:
```powershell
dotnet test SelfStorageManagementSystem.sln --verbosity normal
```

### Kết quả xuất ra thực tế:
```text
Test run for D:\FPT\PRN222\Project\Final_Project\tests\SelfStorageManagementSystem.Tests\bin\Debug\net8.0-windows\SelfStorageManagementSystem.Tests.dll (.NETCoreApp,Version=v8.0)
A total of 1 test files matched the specified pattern.
[xUnit.net 00:00:00.17]     SelfStorageManagementSystem.Tests.PaymentSqlServerConcurrencyTests.ConcurrentCheckout_SameReservation_DifferentIdempotencyKeys_ExactlyOneSucceeds [SKIP]
[xUnit.net 00:00:00.17]     SelfStorageManagementSystem.Tests.ReservationSqlServerConcurrencyTests.ConcurrentReservationHold_WhenOnlyOneSlotAvailable_ExactlyOneSucceedsAndOneFailsWithControlledConflict [SKIP]
  Skipped SelfStorageManagementSystem.Tests.PaymentSqlServerConcurrencyTests.ConcurrentCheckout_SameReservation_DifferentIdempotencyKeys_ExactlyOneSucceeds [1 ms]
  Skipped SelfStorageManagementSystem.Tests.ReservationSqlServerConcurrencyTests.ConcurrentReservationHold_WhenOnlyOneSlotAvailable_ExactlyOneSucceedsAndOneFailsWithControlledConflict [1 ms]

Passed!  - Failed:     0, Passed:   176, Skipped:     2, Total:   178, Duration: 2 s - SelfStorageManagementSystem.Tests.dll (net8.0)
```

---

## 5. Báo Cáo Tính Minh Bạch: Phần Đã Kiểm Chứng vs. Phần Chưa Kiểm Chứng

### A. Những phần ĐÃ kiểm chứng độc lập & chắc chắn:
1. **Tính toán tài chính & Voucher**:
   - Cọc 1 tháng (**BR-FIN-01**) không bị voucher giảm trừ.
   - Tiền thuê kỳ đầu bóc tách riêng; voucher `fixed`, `percentage`, `free_days` (tính trên số ngày thực tế 28, 29, 30, 31 ngày) đã kiểm chứng qua bộ test đầy đủ.
   - Chặn ngày lẻ, chặn cọc lệch snapshot.
2. **Bảo mật & Tính toàn vẹn cổng thanh toán**:
   - Chặn giả lập thanh toán ngoài Dev/Test hoặc khi chưa bật cấu hình.
   - Chặn gọi simulator cho các giao dịch không phải provider `demo`.
   - HMAC-SHA512 của VNPAY kiểm tra toàn vẹn chuỗi tham số, phát hiện can thiệp số tiền hoặc chữ ký giả mạo.
   - IPN phản hồi chính xác các mã chuẩn: `00`, `01`, `02`, `04`, `97`.
3. **Quản lý Hóa đơn & Giao dịch tài chính bất biến**:
   - Khách hàng retry checkout hủy hóa đơn cũ (`status = 'voided'`), phát hành hóa đơn mới.
   - Late callback trỏ vào hóa đơn `voided` tự động chuyển vào tra soát (`reconciliation_required = true`), tạo yêu cầu hoàn tiền `refunds`, không tạo hợp đồng.
   - Bắt buộc hóa đơn thanh toán đủ 100% mới được cấp hợp đồng.
   - Idempotency key kiểm tra toàn bộ payload, replay ưu tiên trước hạn mức voucher.
   - Voucher được giải phóng sạch (`status = 'released'`) khi retry không kèm mã.
4. **Phân quyền cơ sở & Phê duyệt hoàn tiền**:
   - Nhân viên cơ sở chỉ xem và duyệt refund thuộc cơ sở được phân công; duyệt trái cơ sở trả `403 Forbidden`.
   - Quản trị viên hệ thống có quyền toàn hệ thống và duyệt refund thành công mà không vi phạm FK `core.refund_approvals`.
   - Chuẩn hóa HTTP 404 cho refund không tồn tại và HTTP 409 cho refund đã duyệt.
5. **Giao diện WPF Client**:
   - Biên dịch thành công 0 warning, 0 error.
   - Binding dữ liệu báo giá, timer 15 phút, radio button chọn cổng thanh toán, checkbox xác nhận điều khoản, view cảnh báo đối soát.

### B. Những phần CHƯA kiểm chứng thực địa (Do giới hạn môi trường học phần):
1. **Webhook IPN thực tế từ server VNPAY bên ngoài**:
   - Đã kiểm chứng toàn diện qua test tích hợp HTTP server-to-server giả lập chữ ký và tham số thực tế.
   - Chưa kiểm chứng nhận request trực tiếp từ internet của VNPAY Sandbox do môi trường dev chạy localhost và chưa cấu hình công cụ reverse proxy/tunnel (như ngrok) có domain public được đăng ký với VNPAY Merchant Portal.
2. **Test Concurrency trên SQL Server phân tán thật**:
   - Các bài test concurrency `PaymentSqlServerConcurrencyTests` và `ReservationSqlServerConcurrencyTests` được cấu hình an toàn skip khi chưa có biến môi trường `SELFSTORAGE_SQL_TEST_CONNECTION_STRING` trỏ tới DB test chuyên biệt nhằm bảo vệ dữ liệu cục bộ không bị ảnh hưởng.
   - Logic khóa tầng dữ liệu (`sp_getapplock` với transaction owner và timeout 5000ms) đã được cài đặt đầy đủ trong code `PaymentRepository.cs` và `ReservationRepository.cs`.
3. **Ký số pháp lý và Hóa đơn điện tử GTGT**:
   - Khách hàng xác nhận phiên bản điều khoản hợp đồng được lưu vết trong metadata (`accepted_policy_version_id`), không tuyên bố đây là chữ ký số PKI/token USB hợp pháp.
   - Thuế `tax_amount = 0` và hóa đơn/biên nhận là tài liệu nội bộ demo, chưa kết nối cơ quan thuế.
4. **Bàn giao ô kho vật lý (Check-in)**:
   - Function 4 dừng ở trạng thái hợp đồng `scheduled` và chưa gán ô kho cụ thể (`storage_unit_id`). Việc gán ô thực tế và chuyển sang `active` thuộc phạm vi bàn giao cho **Function 5**.

---

## 6. Hướng Dẫn Chạy Demo Thực Tế

### Bước 1: Khởi động API Server
```powershell
dotnet run --project src/SelfStorageManagementSystem.Presentation --launch-profile https
```
- Swagger UI sẵn sàng tại: `https://localhost:7031/swagger`

### Bước 2: Khởi động WPF Client
```powershell
dotnet run --project src/SelfStorageManagementSystem.WpfClient
```

### Bước 3: Kịch bản Demo Trực Tiếp
1. **Đăng nhập**: Sử dụng tài khoản khách hàng demo (hoặc đăng ký tài khoản mới).
2. **Tạo Đơn Giữ Chỗ (Function 3)**:
   - Chọn kho tại Tab Catalog, bấm **"Tiến hành giữ chỗ"**.
   - Cửa sổ chi tiết đơn đặt chỗ mở ra với đồng hồ đếm ngược 15 phút.
3. **Thực hiện Checkout & Thanh toán (Function 4)**:
   - Bấm nút xanh lá **"💳 Thanh toán ngay (Function 4)"**.
   - Cửa sổ Checkout hiển thị chi tiết các khoản: Cọc (1 tháng) + Tiền thuê tháng đầu + Phí đặt chỗ.
   - *(Tùy chọn)*: Nhập mã voucher `WELCOME10` và bấm **"Áp dụng"**. Bảng giá cập nhật giảm trừ tiền thuê, tiền cọc giữ nguyên 100%.
   - Đánh dấu tick chọn *"Tôi đã đọc, hiểu rõ và chấp thuận Điều khoản thuê kho trên"*.
   - Chọn phương thức:
     - **Demo Payment Gateway**: Nút "⚡ Giả lập thanh toán thành công (Demo)" xuất hiện. Bấm **"🚀 Xác nhận & Tiến hành Thanh toán"**, sau đó bấm nút giả lập trên ứng dụng WPF để hoàn tất.
     - **VNPAY Sandbox**: Nút giả lập tự động ẩn đi. Bấm **"🚀 Xác nhận & Tiến hành Thanh toán"**, trình duyệt mở cổng thanh toán sandbox VNPAY chính thức.
   - Giao diện WPF tự động bắt tín hiệu thành công:
     - Đơn đặt chỗ chuyển thành **Confirmed**.
     - Hóa đơn chuyển thành **Paid** (mã `INV-...`).
     - Hợp đồng chuyển thành **Scheduled** (mã `AGR-...`), bảo toàn nguyên vẹn số dư tiền cọc và sẵn sàng cho thủ tục nhận kho tại quầy ở **Function 5**.
4. **Kịch bản Đối soát & Hoàn tiền (Reconciliation Flow)**:
   - Nếu đơn bị quá hạn 15 phút mà tiền vẫn về, thanh toán trùng lặp, hoặc thanh toán trỏ vào hóa đơn đã void: WPF tự động chuyển sang màn hình cảnh báo màu vàng cam `panelReconciliationOutcome`.
   - Thông báo rõ ràng: "ĐÃ THU TIỀN — CẦN ĐỐI SOÁT & HOÀN TIỀN", ô kho chưa cấp và yêu cầu hoàn tiền `refunds` đã được tạo tự động để nhân viên xử lý.
   - Nhân viên cơ sở có thể truy cập `GET /api/payments/refunds` và gọi `POST /api/payments/refunds/{id}/review` để duyệt hoàn tiền trong phạm vi cơ sở của mình.

---

## 7. Trạng Thái Git & Bảo Toàn Nhánh

- **Nhánh hiện tại**: `Payment&RentalAgreement`
- **Không tự động commit hoặc push**: Giữ nguyên trạng thái working tree cho developer kiểm tra và tự commit.
- **Không merge main**: Hoàn toàn cô lập trên nhánh hiện tại.
