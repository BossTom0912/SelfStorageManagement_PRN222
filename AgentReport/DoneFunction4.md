# Báo Cáo Hoàn Thiện & Nghiệm Thu Chức Năng 4: Payment & Rental Agreement

- **Dự án**: Self-Storage Facility Rental and Management System
- **Học phần**: PRN222
- **Thời gian hoàn thành**: 07/10/2026
- **Branch**: `Payment&RentalAgreement`
- **Kiến trúc áp dụng**: Strict 3-Layer Architecture + WPF Client (.NET 8, ASP.NET Core Web API, EF Core 8 Database-First, SQL Server `core` schema)
- **Tình trạng kiểm thử**: **149 Passed, 2 Skipped (Opt-in SQL Concurrency), 0 Failed, 0 Warnings CS, 0 Build Errors**

---

## 1. Tổng Quan Triển Khai Chức Năng 4

Chức năng 4 (Payment & Rental Agreement) đã được triển khai đầy đủ trên toàn bộ hệ thống bao gồm DataAccess, BusinessLogic, Presentation (Web API), WPF Client, Unit Tests, Integration Tests và tài liệu kỹ thuật.

Triển khai bám sát tuyệt đối:
- **CODEX_RULE.md** và các chỉ dẫn repository hiện hành.
- **docs/FUNCTION4_PAYMENT_RENTAL_AGREEMENT_PLAN.md** (Thiết kế chi tiết).
- **docs/FIVE_CORE_FUNCTIONS_SUMMARY.md** và **docs/DEMO_ROADMAP_AND_TECH_SPEC.md**.
- Các tài liệu SRS: `Business_Rules_SelfStorageSystem.docx` (BR-RSV-01/03/04, BR-FIN-01/04), `SelfStorageSystem_PhanTichHeThong.docx`, `State Chart explanation.docx`.
- Cơ sở dữ liệu sạch: `DB/SelfStoragePRN222_SQLServer_CleanInstall.sql` (schema `core`, các CHECK constraints, Foreign Keys, Unique Indexes, và 35 triggers).

---

## 2. Bảng Tổng Hợp Nghiệp Vụ & Quyết Định Kỹ Thuật

| STT | Nội dung / Quyết định | Cách thức triển khai | Tình trạng & Bằng chứng |
| :--- | :--- | :--- | :--- |
| **1** | **Khoản phải trả lần đầu & Quy định tiền cọc** | Thu cọc 1 tháng (**BR-FIN-01**) + tiền thuê tháng đầu + booking fee − voucher hợp lệ. Cọc là khoản riêng biệt, không bị voucher giảm trừ. Nếu `deposit_snapshot != monthly_rate_snapshot`, chặn checkout với HTTP 400 Bad Request; không tự ý sửa giá chốt trong reservation. | **Đã hoàn thành**<br>`GetCheckoutQuoteAsync_DepositMismatch_ThrowsBadRequestException`<br>`GetCheckoutQuoteAsync_ValidReservation_ReturnsAccuratePriceBreakdown` |
| **2** | **Chính sách Voucher (Fixed, Percentage, Free Days)** | Tối đa 1 mã/đơn. `fixed` và `percentage` chỉ giảm trên tiền thuê tháng đầu + booking fee. `free_days` chỉ giảm tiền thuê tháng đầu theo công thức: $\text{monthly\_rate} \times \frac{\text{free\_days}}{\text{số ngày thực tế kỳ đầu}}$. Làm tròn đến đồng VND. Chặn lỗi nếu `free_days > số ngày kỳ đầu`. Kiểm tra `minimum_months`, `minimum_amount`, `facility`, `unit_type`, `new_customer`, `usage_limit`, `per_customer_limit` trong transaction. | **Đã hoàn thành**<br>Bao phủ tháng 28, 29, 30, 31 ngày trong `PaymentServiceTests` |
| **3** | **Lưu ý Thuế Demo** | Thiết lập `tax_amount = 0` theo quy ước đồ án học phần. Hiển thị rõ ràng trên UI WPF, API và biên nhận là hóa đơn nội bộ / biên nhận demo, không tuyên bố là hóa đơn điện tử GTGT hợp pháp. Không hardcode thành chính sách sản xuất. | **Đã hoàn thành**<br>Banner trên `CheckoutWindow.xaml`<br>Ghi rõ trong `README.md` & docs |
| **4** | **Trạng thái sau thanh toán thành công** | Chuyển payment `succeeded`, invoice `paid`, reservation `confirmed`, rental agreement `scheduled`. `signed_at = NULL` và chưa gán `storage_unit_id` (Function 5 mới kích hoạt sang `active` và gán ô vật lý khi check-in). Lưu bằng chứng chấp thuận điều khoản của khách vào `metadata`. | **Đã hoàn thành**<br>`DemoPaymentComplete_Success_TransitionsToConfirmedAndScheduled`<br>`Checkout_ValidRequest_CreatesPaymentAndAllowsCompletion` |
| **5** | **Xử lý thanh toán trễ (Late Callback / Expired Hold)** | Nếu tiền đã thu tại cổng nhưng hold đã hết hạn hoặc bị hủy: **Không** phục hồi reservation, **Không** tạo agreement, **Không** phân bổ tiền vào invoice đã void. Ghi nhận payment `succeeded`, gắn cờ `reconciliation_required = true` và tự động sinh bản ghi yêu cầu hoàn tiền `refunds` trạng thái `requested`. | **Đã hoàn thành**<br>`DemoPaymentComplete_ExpiredHold_FlagsReconciliationAndCreatesRefund` |
| **6** | **Chống tranh chấp & Idempotency** | Sử dụng header `Idempotency-Key`. Khóa tài nguyên SQL Server qua `sp_getapplock` (`SS_Reservation_{id}`). Nhận diện attempt trùng lặp trả về cùng kết quả; nhận diện attempt đồng thời trả 409 Conflict. Khởi tạo invoice `draft` → dòng tiền → đối chiếu trigger `total_amount` → `open`. | **Đã hoàn thành**<br>`CheckoutAsync_IdempotencyKeyReplay_ReturnsSamePaymentAttempt`<br>`CheckoutAsync_ConcurrentAttemptWithDifferentKey_ThrowsConflictException` |
| **7** | **Cổng thanh toán Demo & VNPAY Sandbox** | `DemoGateway` cho môi trường Development/Test. `VnpayGateway` chuẩn HMAC-SHA512, múi giờ GMT+7, số tiền VND nguyên × 100, `ExpireDate` không vượt hold. Webhook IPN kiểm tra checksum, amount, tmnCode, txnRef. | **Đã hoàn thành**<br>`VnpayGatewayTests` (3 bài)<br>`PaymentIntegrationTests` (IPN codes `00`, `97`, `01`) |
| **8** | **Giao diện WPF Client** | Nối trực tiếp từ `ReservationDetailWindow` sang `CheckoutWindow`. Hiển thị chi tiết bảng giá, áp dụng voucher, đếm ngược hold, chọn cổng thanh toán, đồng ý điều khoản, mở browser, polling trạng thái thời gian thực và màn hình kết quả chúc mừng. | **Đã hoàn thành**<br>`CheckoutWindow.xaml`<br>`CheckoutWindow.xaml.cs` |
| **9** | **Bugfix Function 3 Worker & Expiry** | Sửa worker quét quá hạn, thao tác hủy đơn và GET hết hạn để tự động giải phóng `promotion_redemptions.status = 'released'` cùng transaction void invoice chưa trả. | **Đã hoàn thành**<br>`ReservationRepository.cs` |

---

## 3. Danh Sách Tệp Tin Đã Tạo Và Sửa Đổi

### A. DataAccess Layer
- `src/SelfStorageManagementSystem.DataAccess/Repositories/Implementations/ReservationRepository.cs` *(Modified)*: Bổ sung giải phóng voucher `released` trong `CancelReservationAsync`, `ExpireOverdueReservationHoldsAsync`, `TryExpireSingleReservationIfOverdueAsync`.
- `src/SelfStorageManagementSystem.DataAccess/Repositories/Interfaces/IPaymentRepository.cs` *(Created)*: Định nghĩa interface quản lý transaction checkout, hoàn tất thanh toán, đối soát và tra cứu.
- `src/SelfStorageManagementSystem.DataAccess/Repositories/Implementations/PaymentRepository.cs` *(Created)*: Triển khai transaction khóa `sp_getapplock`, xử lý trigger SQL, đối chiếu invoice lines, sinh mã tự động, ghi nhận sự kiện tích hợp và hoàn tiền.
- `src/SelfStorageManagementSystem.DataAccess/DependencyInjection/ServiceCollectionExtensions.cs` *(Modified)*: Đăng ký `IPaymentRepository` vào DI container.

### B. BusinessLogic Layer
- `src/SelfStorageManagementSystem.BusinessLogic/DTOs/Requests/Payments/CheckoutRequest.cs` *(Created)*
- `src/SelfStorageManagementSystem.BusinessLogic/DTOs/Requests/Payments/DemoPaymentCompleteRequest.cs` *(Created)*
- `src/SelfStorageManagementSystem.BusinessLogic/DTOs/Responses/Payments/CheckoutQuoteResponse.cs` *(Created)*
- `src/SelfStorageManagementSystem.BusinessLogic/DTOs/Responses/Payments/CheckoutResponse.cs` *(Created)*
- `src/SelfStorageManagementSystem.BusinessLogic/DTOs/Responses/Payments/PaymentDetailResponse.cs` *(Created)*
- `src/SelfStorageManagementSystem.BusinessLogic/DTOs/Responses/Invoices/InvoiceDetailResponse.cs` *(Created)*
- `src/SelfStorageManagementSystem.BusinessLogic/DTOs/Responses/Agreements/RentalAgreementDetailResponse.cs` *(Created)*
- `src/SelfStorageManagementSystem.BusinessLogic/Services/Interfaces/IPaymentGateway.cs` *(Created)*
- `src/SelfStorageManagementSystem.BusinessLogic/Services/Implementations/DemoGateway.cs` *(Created)*
- `src/SelfStorageManagementSystem.BusinessLogic/Services/Implementations/VnpayGateway.cs` *(Created)*
- `src/SelfStorageManagementSystem.BusinessLogic/Services/Interfaces/IPaymentService.cs` *(Created)*
- `src/SelfStorageManagementSystem.BusinessLogic/Services/Implementations/PaymentService.cs` *(Created)*
- `src/SelfStorageManagementSystem.BusinessLogic/Services/Interfaces/IInvoiceService.cs` *(Created)*
- `src/SelfStorageManagementSystem.BusinessLogic/Services/Implementations/InvoiceService.cs` *(Created)*
- `src/SelfStorageManagementSystem.BusinessLogic/Services/Interfaces/IRentalAgreementService.cs` *(Created)*
- `src/SelfStorageManagementSystem.BusinessLogic/Services/Implementations/RentalAgreementService.cs` *(Created)*
- `src/SelfStorageManagementSystem.BusinessLogic/DependencyInjection/ServiceCollectionExtensions.cs` *(Modified)*: Đăng ký các service và gateway Function 4.

### C. Presentation Layer (ASP.NET Core Web API)
- `src/SelfStorageManagementSystem.Presentation/Controllers/PaymentsController.cs` *(Created)*: Các endpoint thanh toán, checkout, webhook VNPAY IPN, simulator demo và tra cứu đối soát.
- `src/SelfStorageManagementSystem.Presentation/Controllers/ReservationsController.cs` *(Modified)*: Bổ sung endpoint `GET /api/reservations/{id}/checkout-quote`.
- `src/SelfStorageManagementSystem.Presentation/Controllers/InvoicesController.cs` *(Created)*: Endpoint `GET /api/invoices/{id}`.
- `src/SelfStorageManagementSystem.Presentation/Controllers/RentalAgreementsController.cs` *(Created)*: Endpoint `GET /api/agreements/{id}`.

### D. WPF Client
- `src/SelfStorageManagementSystem.WpfClient/Models/PaymentModels.cs` *(Created)*: Models giao diện client.
- `src/SelfStorageManagementSystem.WpfClient/Services/ApiClient.cs` *(Modified)*: Thêm hỗ trợ custom headers (`Idempotency-Key`) và các hàm gọi API Function 4.
- `src/SelfStorageManagementSystem.WpfClient/Views/ReservationDetailWindow.xaml` *(Modified)*: Thêm nút "💳 Thanh toán ngay (Function 4)".
- `src/SelfStorageManagementSystem.WpfClient/Views/ReservationDetailWindow.xaml.cs` *(Modified)*: Điều khiển hiển thị nút thanh toán và mở modal `CheckoutWindow`.
- `src/SelfStorageManagementSystem.WpfClient/Views/CheckoutWindow.xaml` *(Created)*: Giao diện XAML thanh toán, bóc tách dòng tiền, voucher, điều khoản, chọn cổng thanh toán, simulator trigger và kết quả thành công.
- `src/SelfStorageManagementSystem.WpfClient/Views/CheckoutWindow.xaml.cs` *(Created)*: Code-behind xử lý countdown, gọi API checkout, mở browser, polling trạng thái ngầm và hiển thị mã hóa đơn/hợp đồng.

### E. Kiểm thử tự động (Tests)
- `tests/SelfStorageManagementSystem.Tests/PaymentServiceTests.cs` *(Created)*: 16 bài unit tests.
- `tests/SelfStorageManagementSystem.Tests/VnpayGatewayTests.cs` *(Created)*: 3 bài unit tests cho VNPAY hashing & callback.
- `tests/SelfStorageManagementSystem.Tests/PaymentIntegrationTests.cs` *(Created)*: 6 bài integration tests cho API endpoints và webhook IPN.
- `tests/SelfStorageManagementSystem.Tests/PaymentSqlServerConcurrencyTests.cs` *(Created)*: 1 bài SQL Server Concurrency test với `[SqlIntegrationFact]`.
- `tests/SelfStorageManagementSystem.Tests/AuthAndAuthorizationIntegrationTests.cs` *(Modified)*: Cấu hình VNPAY test settings trong `CustomWebApplicationFactory`.

### F. Tài liệu
- `docs/FUNCTION4_PAYMENT_RENTAL_AGREEMENT_PLAN.md` *(Modified)*: Cập nhật Mục 8 Báo cáo triển khai thực tế.
- `docs/FIVE_CORE_FUNCTIONS_SUMMARY.md` *(Modified)*: Cập nhật trạng thái Function 4 đã hoàn thành cốt lõi.
- `README.md` *(Modified)*: Cập nhật bảng roadmap và lưu ý thuế demo.

---

## 4. Chi Tiết Thực Thi & Bằng Chứng Kiểm Thử

### Lệnh thực thi:
```powershell
dotnet test SelfStorageManagementSystem.sln
```

### Kết quả xuất ra:
```text
Test run for D:\FPT\PRN222\Project\Final_Project\tests\SelfStorageManagementSystem.Tests\bin\Debug\net8.0-windows\SelfStorageManagementSystem.Tests.dll (.NETCoreApp,Version=v8.0)
A total of 1 test files matched the specified pattern.
[xUnit.net 00:00:00.15]     SelfStorageManagementSystem.Tests.ReservationSqlServerConcurrencyTests.ConcurrentReservationHold_WhenOnlyOneSlotAvailable_ExactlyOneSucceedsAndOneFailsWithControlledConflict [SKIP]
[xUnit.net 00:00:00.15]     SelfStorageManagementSystem.Tests.PaymentSqlServerConcurrencyTests.ConcurrentCheckout_SameReservation_DifferentIdempotencyKeys_ExactlyOneSucceeds [SKIP]
  Skipped SelfStorageManagementSystem.Tests.ReservationSqlServerConcurrencyTests.ConcurrentReservationHold_WhenOnlyOneSlotAvailable_ExactlyOneSucceedsAndOneFailsWithControlledConflict [1 ms]
  Skipped SelfStorageManagementSystem.Tests.PaymentSqlServerConcurrencyTests.ConcurrentCheckout_SameReservation_DifferentIdempotencyKeys_ExactlyOneSucceeds [1 ms]

Passed!  - Failed:     0, Passed:   149, Skipped:     2, Total:   151, Duration: 1 s - SelfStorageManagementSystem.Tests.dll (net8.0)
```

### Phân loại các bài kiểm thử Function 4:
1. **Unit Tests (`PaymentServiceTests` - 16 bài)**:
   - `GetCheckoutQuoteAsync_ValidReservation_ReturnsAccuratePriceBreakdown`: Kiểm tra bóc tách tiền cọc 1 tháng, tiền thuê tháng đầu, booking fee, tax = 0.
   - `GetCheckoutQuoteAsync_DepositMismatch_ThrowsBadRequestException`: Chặn checkout nếu cọc không bằng 1 tháng thuê.
   - `GetCheckoutQuoteAsync_FixedVoucher_DiscountsRentAndFee_NotDeposit`: Voucher fixed chỉ trừ tiền thuê và phí, cọc giữ nguyên.
   - `GetCheckoutQuoteAsync_FixedVoucherExceedsRentAndFee_CappedAtRentAndFee`: Voucher vượt quá tiền thuê + phí được chặn trần, không giảm vào cọc.
   - `GetCheckoutQuoteAsync_PercentageVoucher_CalculatesAccurately`: Tính phần trăm chuẩn xác trên tiền thuê + phí.
   - `GetCheckoutQuoteAsync_FreeDaysVoucher_CalculatesAccuratelyForVariousMonthLengths`: Kiểm tra 4 trường hợp độ dài tháng thực tế:
     - Tháng 2 năm nhuận (29 ngày): 10 ngày miễn phí trên giá 2.900.000đ → giảm đúng 1.000.000đ.
     - Tháng 2 năm thường (28 ngày): 7 ngày miễn phí trên giá 2.800.000đ → giảm đúng 700.000đ.
     - Tháng 4 (30 ngày): 15 ngày miễn phí trên giá 3.000.000đ → giảm đúng 1.500.000đ.
     - Tháng 1 (31 ngày): 10 ngày miễn phí trên giá 3.100.000đ → giảm đúng 1.000.000đ.
   - `GetCheckoutQuoteAsync_FreeDaysExceedsDaysInFirstMonth_ThrowsBadRequestException`: Báo lỗi rõ ràng khi số ngày miễn phí lớn hơn số ngày của tháng đầu.
   - `GetCheckoutQuoteAsync_VoucherMinimumMonthsViolation_ThrowsBadRequestException`: Chặn voucher yêu cầu tối thiểu 3 tháng khi đơn chỉ thuê 1 tháng.
   - `GetCheckoutQuoteAsync_VoucherFacilityMismatch_ThrowsBadRequestException`: Chặn voucher áp dụng sai cơ sở kho.
   - `CheckoutAsync_IdempotencyKeyReplay_ReturnsSamePaymentAttempt`: Tái hiện cùng kết quả khi gọi lặp Idempotency-Key.
   - `CheckoutAsync_ConcurrentAttemptWithDifferentKey_ThrowsConflictException`: Trả về lỗi 409 Conflict khi có giao dịch đang chờ xử lý khác.
   - `DemoPaymentComplete_Success_TransitionsToConfirmedAndScheduled`: Chuyển reservation `confirmed`, invoice `paid`, tạo agreement `scheduled` (`signed_at = null`, `deposit_balance = deposit_snapshot`).
   - `DemoPaymentComplete_ExpiredHold_FlagsReconciliationAndCreatesRefund`: Xử lý thanh toán trễ quá hạn: không confirm reservation, không tạo agreement, đánh dấu `reconciliation_required` và tạo `refunds`.
2. **Gateway Tests (`VnpayGatewayTests` - 3 bài)**:
   - `CreateCheckoutUrlAsync_GeneratesValidVnpayUrlWithIntegerAmountTimes100`: Tạo URL VNPAY chuẩn, số tiền VND nhân 100 dạng số nguyên.
   - `VerifyCallbackAsync_ValidHmacChecksum_ReturnsSuccess`: Kiểm tra tính toàn vẹn chữ ký HMAC-SHA512.
   - `VerifyCallbackAsync_TamperedAmount_FailsSignature`: Phát hiện thay đổi số tiền hoặc dữ liệu giả mạo.
3. **Integration Tests (`PaymentIntegrationTests` - 6 bài)**:
   - `GetCheckoutQuote_OwnerCustomer_ReturnsOkWithAccuratePricing`: Khách hàng chính chủ xem được báo giá.
   - `GetCheckoutQuote_OtherCustomer_ReturnsForbidden`: Khách hàng khác bị chặn HTTP 403 Forbidden.
   - `Checkout_MissingIdempotencyHeader_ReturnsBadRequest`: Chặn HTTP 400 nếu thiếu header `Idempotency-Key`.
   - `Checkout_ValidRequest_CreatesPaymentAndAllowsCompletion`: Thực hiện đầy đủ luồng checkout, hoàn tất thanh toán demo, xác nhận invoice `paid` và agreement `scheduled`.
   - `VnpayIpn_InvalidSignature_ReturnsCode97`: Webhook IPN trả mã `97` khi sai chữ ký.
   - `VnpayIpn_ValidSignatureUnknownTxnRef_ReturnsCode01`: Webhook IPN trả mã `01` khi không tìm thấy đơn.
4. **SQL Concurrency Tests (`PaymentSqlServerConcurrencyTests` - 1 bài)**:
   - `ConcurrentCheckout_SameReservation_DifferentIdempotencyKeys_ExactlyOneSucceeds`: Chạy opt-in bảo vệ database qua `[SqlIntegrationFact]`.

---

## 5. Hướng Dẫn Chạy Thử và Demo Thực Tế

### Bước 1: Khởi động API Server
```powershell
dotnet run --project src/SelfStorageManagementSystem.Presentation --launch-profile https
```
- Swagger UI sẵn sàng tại: `https://localhost:7031/swagger`

### Bước 2: Khởi động WPF Client
```powershell
dotnet run --project src/SelfStorageManagementSystem.WpfClient
```

### Bước 3: Thực hiện Kịch bản Demo
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
     - **Demo Payment Gateway**: Bấm **"🚀 Xác nhận & Tiến hành Thanh toán"**. Trình duyệt mở ra trang giả lập localhost, hoặc có thể bấm ngay nút **"⚡ Giả lập thanh toán thành công (Demo)"** ngay trên giao diện WPF.
     - **VNPAY Sandbox**: Mở trang thanh toán thử nghiệm VNPAY chính thức qua chữ ký bảo mật HMAC-SHA512.
   - Giao diện WPF tự động bắt tín hiệu thành công:
     - Đơn đặt chỗ chuyển thành **Confirmed**.
     - Hóa đơn chuyển thành **Paid** (mã `INV-...`).
     - Hợp đồng chuyển thành **Scheduled** (mã `AGR-...`), bảo toàn nguyên vẹn số dư tiền cọc và sẵn sàng cho thủ tục nhận kho tại quầy ở **Function 5**.

---

## 6. Trạng Thái Git Hiện Tại

```text
On branch Payment&RentalAgreement
Changes not staged for commit:
	modified:   README.md
	modified:   docs/FIVE_CORE_FUNCTIONS_SUMMARY.md
	modified:   src/SelfStorageManagementSystem.BusinessLogic/DependencyInjection/ServiceCollectionExtensions.cs
	modified:   src/SelfStorageManagementSystem.DataAccess/DependencyInjection/ServiceCollectionExtensions.cs
	modified:   src/SelfStorageManagementSystem.DataAccess/Repositories/Implementations/ReservationRepository.cs
	modified:   src/SelfStorageManagementSystem.Presentation/Controllers/ReservationsController.cs
	modified:   src/SelfStorageManagementSystem.WpfClient/Services/ApiClient.cs
	modified:   src/SelfStorageManagementSystem.WpfClient/Views/ReservationDetailWindow.xaml
	modified:   src/SelfStorageManagementSystem.WpfClient/Views/ReservationDetailWindow.xaml.cs
	tests/SelfStorageManagementSystem.Tests/AuthAndAuthorizationIntegrationTests.cs

Untracked files:
	AgentReport/DoneFunction4.md
	docs/FUNCTION4_PAYMENT_RENTAL_AGREEMENT_PLAN.md
	src/SelfStorageManagementSystem.BusinessLogic/DTOs/Requests/Payments/
	src/SelfStorageManagementSystem.BusinessLogic/DTOs/Responses/Agreements/
	src/SelfStorageManagementSystem.BusinessLogic/DTOs/Responses/Invoices/
	src/SelfStorageManagementSystem.BusinessLogic/DTOs/Responses/Payments/
	src/SelfStorageManagementSystem.BusinessLogic/Services/Implementations/DemoGateway.cs
	src/SelfStorageManagementSystem.BusinessLogic/Services/Implementations/InvoiceService.cs
	src/SelfStorageManagementSystem.BusinessLogic/Services/Implementations/PaymentService.cs
	src/SelfStorageManagementSystem.BusinessLogic/Services/Implementations/RentalAgreementService.cs
	src/SelfStorageManagementSystem.BusinessLogic/Services/Implementations/VnpayGateway.cs
	src/SelfStorageManagementSystem.BusinessLogic/Services/Interfaces/IInvoiceService.cs
	src/SelfStorageManagementSystem.BusinessLogic/Services/Interfaces/IPaymentGateway.cs
	src/SelfStorageManagementSystem.BusinessLogic/Services/Interfaces/IPaymentService.cs
	src/SelfStorageManagementSystem.BusinessLogic/Services/Interfaces/IRentalAgreementService.cs
	src/SelfStorageManagementSystem.DataAccess/Repositories/Implementations/PaymentRepository.cs
	src/SelfStorageManagementSystem.DataAccess/Repositories/Interfaces/IPaymentRepository.cs
	src/SelfStorageManagementSystem.Presentation/Controllers/InvoicesController.cs
	src/SelfStorageManagementSystem.Presentation/Controllers/PaymentsController.cs
	src/SelfStorageManagementSystem.Presentation/Controllers/RentalAgreementsController.cs
	src/SelfStorageManagementSystem.WpfClient/Models/PaymentModels.cs
	src/SelfStorageManagementSystem.WpfClient/Views/CheckoutWindow.xaml
	src/SelfStorageManagementSystem.WpfClient/Views/CheckoutWindow.xaml.cs
	tests/SelfStorageManagementSystem.Tests/PaymentIntegrationTests.cs
	tests/SelfStorageManagementSystem.Tests/PaymentServiceTests.cs
	tests/SelfStorageManagementSystem.Tests/PaymentSqlServerConcurrencyTests.cs
	tests/SelfStorageManagementSystem.Tests/VnpayGatewayTests.cs
```

*(Tuân thủ chỉ dẫn: Giữ nguyên toàn bộ file đang thay đổi, không tự động commit, push, reset hay clean).*
