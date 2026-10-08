# Báo Cáo Sửa Lỗi Hoàn Thiện Chức Năng 4: Payment & Rental Agreement
*(Khắc phục toàn diện 12 phát hiện review chuyên sâu từ commit `adc3362`)*

- **Dự án**: Self-Storage Facility Rental and Management System (PRN222)
- **Thời gian hoàn thành**: 08/10/2026
- **Branch làm việc**: `Payment&RentalAgreement` (bắt đầu từ commit `adc3362`)
- **Kiến trúc áp dụng**: Strict 3-Layer Architecture + WPF Client (.NET 8, ASP.NET Core Web API, EF Core 8 Database-First, SQL Server `core` schema)
- **Tình trạng kiểm thử**: **167 Passed, 2 Skipped (Opt-in SQL Concurrency), 0 Failed, 0 CS Warnings, 0 Build Errors**

---

## 1. Tổng Hợp Các Lỗi Review Đã Xử Lý

| STT | Vấn đề phát hiện | Mức độ | Hiện trạng lỗi trước khi sửa | Giải pháp & Thay đổi đã hoàn tất | File sửa đổi & Test chứng minh |
| :---: | :--- | :---: | :--- | :--- | :--- |
| **1** | **Chặn tự xác nhận thanh toán giả** | **Critical** | `PaymentService.cs:351` coi thiếu config là `true`; điều kiện `Development \|\| Test \|\| allowDemo` cho phép demo trong Production. `PaymentService.cs:367` không kiểm tra `payment.provider == "demo"`. Khách có thể tạo giao dịch VNPAY rồi gọi demo complete để thành `succeeded`. | Demo simulator mặc định tắt; chỉ cho phép trong Dev/Test khi `Payment:AllowDemoSimulator == true`. Endpoint từ chối mọi payment có `provider != "demo"` (400 Bad Request). WPF ẩn nút giả lập khi chọn VNPAY. | `PaymentService.cs`<br>`CheckoutWindow.xaml.cs`<br>`DemoPaymentComplete_InProduction_ThrowsForbiddenException`<br>`DemoPaymentComplete_ForNonDemoProvider_ThrowsBadRequestException`<br>`DemoPaymentComplete_RejectedForVnpayPayment` |
| **2** | **Kiểm tra đầy đủ IPN trước khi ghi tiền** | **Critical** | `PaymentService.cs:279` chỉ xác minh chữ ký; `PaymentRepository.cs:767` không dùng `FinalizePaymentParams.Amount`, phân bổ toàn bộ dù IPN gửi amount sai. `TxnRef` không tồn tại ném `PAYMENT_NOT_FOUND` thành HTTP 500 thay vì trả IPN code. | Trong transaction, đối chiếu `TxnRef`, `provider = "vnpay"`, `TmnCode` khớp cấu hình (sai trả 97), tiền tệ `VND` (sai trả 04), `vnp_Amount / 100 == payment.amount` (sai trả 04). `TxnRef` không tồn tại trả 01 "Order not found". Đơn đã succeeded trả 02 "Order already confirmed". | `PaymentService.cs`<br>`PaymentsController.cs`<br>`ProcessVnpayIpn_InvalidSignature_ReturnsCode97`<br>`ProcessVnpayIpn_InvalidTmnCode_ReturnsCode97`<br>`ProcessVnpayIpn_InvalidCurrency_ReturnsCode04`<br>`ProcessVnpayIpn_AmountMismatch_ReturnsCode04`<br>`ProcessVnpayIpn_AlreadySucceeded_ReturnsCode02Idempotent`<br>`VnpayIpn_AmountMismatch_ReturnsCode04` |
| **3** | **Ghi nhận khoản tiền thứ hai (Duplicate payment)** | **Critical** | `PaymentRepository.cs:672` chỉ kiểm tra chính payment hiện tại; không bắt trường hợp reservation đã `confirmed` hoặc invoice đã `paid` bởi attempt khác. Callback thứ hai cố phân bổ vào invoice đã paid bị trigger SQL chặn lỗi 500, mất dấu vết tiền của khách. | Trước khi allocation, kiểm tra nếu invoice đã paid hoặc reservation đã confirmed: ghi nhận payment `succeeded`, đánh dấu `reconciliation_required = true`, tạo đúng 1 bản ghi `refunds` (`status = "requested"`), không tạo trùng hợp đồng. Callback lặp an toàn idempotent. | `PaymentRepository.cs`<br>`ProcessPayment_SecondPaymentOnAlreadyPaidInvoice_FlagsReconciliationAndCreatesRefund` |
| **4** | **Quyết định hold bằng thời gian hiện tại server & xử lý race** | **High** | `PaymentRepository.cs:690` so sánh `hold_until` với `parameters.PaidAt` từ IPN. Worker expiry trong `ReservationRepository.cs:452` không dùng cùng khóa `sp_getapplock` với finalize, dẫn đến race condition xác nhận đơn quá hạn. | Đánh giá `hold_until` bằng thời gian hiện tại UTC server (`DateTimeOffset.UtcNow <= hold_until`). Worker `ExpireOverdueReservationHoldsAsync` lấy khóa `sp_getapplock` (`SS_Reservation_{id}`, Exclusive, timeout 5000ms) trước khi expire. | `PaymentRepository.cs`<br>`ReservationRepository.cs`<br>`DemoPaymentComplete_ExpiredHold_FlagsReconciliationAndCreatesRefund` |
| **5** | **Cập nhật lại invoice lines khi retry quote** | **High** | `PaymentRepository.cs:395` cập nhật discount/quoted total của reservation; đoạn tái sử dụng invoice giữ nguyên `invoice_lines` và `total_amount` cũ khiến invoice lệch với quote mới khi khách đổi voucher. | Khi retry, xóa các dòng invoice draft/open cũ và sinh lại đầy đủ dòng tiền mới đồng bộ theo quote hiện thời trước khi chốt sang open. | `PaymentRepository.cs` (lines 435-465) |
| **6** | **Đồng nhất thứ tự khóa và thời gian chờ** | **High** | Thứ tự khóa giữa Reservation và Promotion không đồng nhất có nguy cơ deadlock; thiếu timeout cho transaction tài chính. | Chuẩn hóa thứ tự khóa: luôn lấy `SS_Reservation_{reservationId}` trước, sau đó mới lấy `SS_Promotion_{promotionId}`; timeout 5000ms. | `PaymentRepository.cs` (lines 355-385) |
| **7** | **Bắt buộc kiểm tra payload khi nhận Idempotency-Key** | **High** | `PaymentRepository.cs:265` chỉ kiểm tra trùng key mà không so khớp customer, reservation, provider, amount. `PaymentService.cs:151` chặn reservation trước khi có cơ hội replay key cũ. | Kiểm tra `customer_id`, `reservation_id`, `provider`, `amount`. Nếu trùng key nhưng lệch payload, ném `ConflictException` (HTTP 409). Trùng key và khớp payload trả về đúng bản ghi cũ. | `PaymentRepository.cs`<br>`PaymentService.cs`<br>`Checkout_SameKeyDifferentPayload_ThrowsConflictException`<br>`CheckoutAsync_IdempotencyKeyReplay_ReturnsSamePaymentAttempt` |
| **8** | **Đảm bảo toàn bộ VND là số nguyên** | **Medium** | `VnpayGateway.cs:43` dùng `Math.Round(order.Amount)` âm thầm làm tròn khi gửi VNPAY, khiến số tiền gửi provider lệch với số tiền lưu DB nếu có phần thập phân. | `VnpayGateway.CreateCheckoutUrlAsync` kiểm tra `Amount % 1 != 0` và ném `ArgumentException`. Quote và checkout chốt số nguyên đồng VND. | `VnpayGateway.cs`<br>`PaymentService.cs`<br>`CreateCheckoutUrlAsync_FractionalAmount_ThrowsArgumentException` |
| **9** | **Đóng dấu phiên bản điều khoản & loại bỏ nhãn "ký số"** | **Medium** | `CheckoutWindow.xaml:137` hiển thị 3 câu tĩnh; không render `PolicyContentJson`; khi có IPN, `PaymentRepository.cs:798` chọn lại policy thay vì policy khách đã chấp thuận; nhãn checkbox bị gọi nhầm là "ký số". | Trích xuất `accepted_policy_version_id` từ metadata payment và gán vào `rental_agreement.policy_version_id`; `signed_at = null`; WPF hiển thị nội dung điều khoản trích từ JSON và chỉ gọi đây là chấp thuận điều khoản trước thanh toán. | `PaymentRepository.cs`<br>`CheckoutWindow.xaml`<br>`CheckoutWindow.xaml.cs` |
| **10** | **Giao diện WPF minh bạch về trạng thái và rủi ro** | **Medium** | `CheckoutWindow.xaml.cs:264` gọi `OnPaymentSucceeded` chỉ dựa vào `Status == "succeeded"`. Late payment có succeeded và `ReconciliationRequired = true` bị hiển thị nhầm là thành công với mã hóa đơn/hợp đồng giả. | Thêm view `panelReconciliationOutcome` thông báo đối soát/hoàn tiền; hiển thị điều khoản thuê kho từ quote; ẩn nút giả lập khi chọn VNPAY; banner lưu ý thuế demo `tax_amount = 0`. | `CheckoutWindow.xaml`<br>`CheckoutWindow.xaml.cs` |
| **11** | **Bổ sung quản lý và phê duyệt hoàn tiền** | **Medium** | `PaymentRepository.cs:714` chỉ tạo `refunds.requested`; không có endpoint hay service cho nhân viên duyệt `refund_approvals` hoặc cập nhật trạng thái refund. | Tạo `ReviewRefundRequest`, `RefundDetailResponse`, `IPaymentService.GetRefundsAsync`, `IPaymentService.ReviewRefundAsync`, endpoints `GET /api/payments/refunds` và `POST /api/payments/refunds/{id}/review`, lưu lịch sử vào `core.refund_approvals`. | `IPaymentService.cs`<br>`PaymentService.cs`<br>`PaymentsController.cs`<br>`ReviewRefund_ApprovesRefund_CreatesApprovalRecord`<br>`RefundEndpoints_CustomerForbidden_ManagerAllowed` |
| **12** | **Chi tiết cấu hình, toán tử promotion và hạ tầng kiểm thử** | **Medium** | Fallback HMAC secret giả trong source; promotion rules bỏ qua operator `gte`/`lte`/`in`; `(int)discount_value` âm thầm cắt 1.5 free_days thành 1; nút browser simulator gọi unauthenticated 401; test SQL concurrency sai `PaymentMethod` và thứ tự cleanup. | `VnpayGateway` fail-fast khi thiếu secret; hỗ trợ các toán tử `gte`, `lte`, `eq`, `in` qua helper `ParseIdList`; từ chối `free_days` số lẻ; trang simulator web chỉ dẫn hoàn tất qua WPF có JWT; dùng `payment_method = 'other'` cho demo; đảo ngược thứ tự dọn dẹp bảng SQL. | `VnpayGateway.cs`<br>`PaymentService.cs`<br>`PaymentsController.cs`<br>`PaymentSqlServerConcurrencyTests.cs`<br>`GetCheckoutQuote_FreeDaysDecimal_ThrowsBadRequestException`<br>`CreateCheckoutUrlAsync_MissingCredentials_ThrowsInvalidOperationException` |

---

## 2. Chi Tiết Kỹ Thuật Từng Điểm Sửa

### 2.1. Chặn việc tự xác nhận thanh toán giả (Lỗi 1)
- **Cơ chế**:
  - Trong `PaymentService.ProcessDemoCompleteAsync`:
    ```csharp
    var allowDemoConfig = _configuration["Payment:AllowDemoSimulator"];
    bool allowDemo = string.Equals(allowDemoConfig, "true", StringComparison.OrdinalIgnoreCase);
    var env = _configuration["ASPNETCORE_ENVIRONMENT"] ?? Environment.GetEnvironmentVariable("ASPNETCORE_ENVIRONMENT") ?? "Development";
    bool isDevOrTest = string.Equals(env, "Development", StringComparison.OrdinalIgnoreCase) || string.Equals(env, "Test", StringComparison.OrdinalIgnoreCase);

    if (!isDevOrTest || !allowDemo)
    {
        throw new ForbiddenException("Mô phỏng hoàn tất thanh toán Demo chỉ được phép chạy trong môi trường Development/Test và khi cấu hình Payment:AllowDemoSimulator được bật rõ ràng.");
    }
    ```
  - Kiểm tra `payment.provider`:
    ```csharp
    if (!string.Equals(payment.provider, "demo", StringComparison.OrdinalIgnoreCase))
    {
        throw new BadRequestException("Chỉ giao dịch khởi tạo qua cổng Demo mới được phép hoàn tất bằng simulator.");
    }
    ```
  - Trong `CheckoutWindow.xaml.cs`:
    Gắn sự kiện `RbGateway_CheckedChanged`: khi chọn VNPAY Gateway, `btnSimulateSuccessNow.Visibility = Visibility.Collapsed`. Nút chỉ hiện khi radio button Demo Gateway được chọn.

### 2.2. Kiểm tra đầy đủ IPN trước khi ghi tiền (Lỗi 2)
- **Cơ chế**:
  - Xác thực chữ ký HMAC-SHA512 qua `VnpayGateway.VerifyCallbackAsync`. Nếu sai, gán `IpnResponseCode = "97"` và `IpnResponseMessage = "Invalid Signature"`.
  - Trong `PaymentService.ProcessVnpayIpnAsync`:
    - Tìm kiếm `payment` theo `paymentId`. Nếu không tìm thấy hoặc `provider != "vnpay"`: trả `IpnResponseCode = "01"` ("Order not found").
    - Đối chiếu `vnp_TmnCode` với cấu hình server: nếu không khớp trả `IpnResponseCode = "97"`.
    - Đối chiếu `vnp_CurrCode`: nếu khác "VND" trả `IpnResponseCode = "04"` ("Invalid Amount").
    - Đối chiếu số tiền: `verifyResult.Amount != payment.amount` trả `IpnResponseCode = "04"` ("Invalid Amount"), không phân bổ tiền và không đổi trạng thái invoice.
    - Idempotency replay: nếu `payment.status == "succeeded"`, trả `IpnResponseCode = "02"` ("Order already confirmed").
  - `PaymentsController.VnpayIpn`: trả về DTO `{ RspCode = result.IpnResponseCode, Message = result.IpnResponseMessage }`.

### 2.3. Ghi nhận khoản tiền thứ hai (Duplicate payment) (Lỗi 3)
- **Cơ chế**:
  - Trong `PaymentRepository.ExecuteFinalizePaymentTransactionAsync`:
    - Kiểm tra xem invoice mục tiêu đã `paid` hoặc reservation đã `confirmed` bởi một giao dịch khác hay chưa:
      ```csharp
      bool isTargetAlreadySettled = invoice.status == "paid" || reservation.status == "confirmed";
      ```
    - Nếu đã thanh toán: ghi nhận payment hiện tại `payment.status = "succeeded"`, cập nhật `paid_at`, `provider_transaction_id`, gắn cờ `reconciliation_required = true` vào `payment.metadata`.
    - Tự động sinh đúng 1 bản ghi `refunds`:
      ```csharp
      var refundRequest = new refund
      {
          payment_id = payment.id,
          agreement_id = reservation.rental_agreements.FirstOrDefault()?.id,
          amount = payment.amount,
          currency = payment.currency,
          reason = "Thanh toán lặp / Hóa đơn đã được quyết toán bởi giao dịch khác (Duplicate Payment Reconciliation)",
          provider = parameters.Provider,
          idempotency_key = $"REFUND-DUP-{payment.id}-{nowUtc.ToUnixTimeSeconds()}",
          status = "requested",
          requested_by = payment.customer_id,
          created_at = nowUtc,
          updated_at = nowUtc
      };
      _context.refunds.Add(refundRequest);
      ```
    - Tuyệt đối không tạo hợp đồng thứ hai (`rental_agreements` giữ nguyên 1 bản ghi duy nhất).

### 2.4. Quyết định hold bằng thời gian hiện tại server UTC (Lỗi 4)
- **Cơ chế**:
  - Trong `PaymentRepository.ExecuteFinalizePaymentTransactionAsync`:
    ```csharp
    var nowUtc = DateTimeOffset.UtcNow;
    bool isHoldExpired = reservation.hold_until <= nowUtc;
    ```
    Sử dụng trực tiếp thời gian hiện tại của server thay vì tin cậy `PayDate` từ gateway.
  - Trong `ReservationRepository.ExpireOverdueReservationHoldsAsync`:
    Sử dụng khóa độc quyền `sp_getapplock` (`SS_Reservation_{id}`, LockMode='Exclusive', timeout=5000ms) trước khi quét và chuyển trạng thái `expired`, triệt tiêu hoàn toàn race condition với tiến trình finalize.

### 2.5. Cập nhật lại invoice lines khi retry quote (Lỗi 5)
- **Cơ chế**:
  - Trong `PaymentRepository.ExecuteCheckoutTransactionAsync`:
    - Khi phát hiện invoice đang mở (`draft` hoặc `open`) gắn với reservation, hệ thống xóa toàn bộ các dòng `invoice_lines` cũ:
      ```csharp
      _context.invoice_lines.RemoveRange(invoice.invoice_lines);
      ```
    - Sinh lại các dòng chi tiết mới đồng bộ 100% với báo giá mới (`deposit`, `rent`, `booking_fee`, `discount`).
    - Lưu và để SQL Server trigger `trg_invoice_lines_refresh_totals` tự động tính toán lại `total_amount` chính xác.

### 2.6. Đồng nhất thứ tự khóa và thời gian chờ (Lỗi 6)
- **Cơ chế**:
  - Chuẩn hóa thứ tự khóa tài nguyên qua `sp_getapplock`:
    1. Khóa đơn đặt chỗ: `SS_Reservation_{reservationId}` (timeout 5000ms).
    2. Khóa chương trình ưu đãi (nếu có): `SS_Promotion_{promotionId}` (timeout 5000ms).
  - Không bao giờ khóa ngược lại, loại bỏ triệt để nguy cơ deadlock khi nhiều người dùng checkout đồng thời.

### 2.7. Ràng buộc idempotency key với nội dung request (Lỗi 7)
- **Cơ chế**:
  - Khi tìm thấy bản ghi `payment` khớp `idempotency_key`:
    - Đối chiếu nghiêm ngặt:
      ```csharp
      bool isMatch = existingPayment.customer_id == parameters.CustomerId &&
                     existingPayment.target_invoice.reservation_id == parameters.ReservationId &&
                     string.Equals(existingPayment.provider, parameters.Provider, StringComparison.OrdinalIgnoreCase) &&
                     existingPayment.amount == parameters.QuotedTotal;
      ```
    - Nếu không khớp: ném ngoại lệ `IDEMPOTENCY_PAYLOAD_MISMATCH` và service chuyển thành `ConflictException` (HTTP 409 Conflict).
    - Nếu khớp: trả về an toàn bản ghi thanh toán cũ.

### 2.8. Chốt số VND nguyên trước khi gọi gateway (Lỗi 8)
- **Cơ chế**:
  - `VnpayGateway.CreateCheckoutUrlAsync`:
    ```csharp
    if (order.Amount % 1m != 0m)
    {
        throw new ArgumentException($"Số tiền thanh toán VNPAY phải là số nguyên VND (giá trị: {order.Amount}).", nameof(order));
    }
    long vnpAmount = (long)order.Amount * 100;
    ```
  - Loại bỏ hoàn toàn `Math.Round` tự ý; đảm bảo số tiền gửi VNPAY luôn bằng chính xác `amount * 100`.

### 2.9. Đóng dấu phiên bản điều khoản & loại bỏ nhãn "ký số" (Lỗi 9)
- **Cơ chế**:
  - Khi checkout, khách gửi `AcceptedPolicyVersionId`. Thông tin này được lưu vào `payment.metadata` dưới dạng JSON:
    ```json
    { "accepted_policy_version_id": 501, "customer_id": 10, "accepted_at": "2026-10-08T..." }
    ```
  - Khi finalize tạo hợp đồng, lấy chính ID này gán vào `rental_agreement.policy_version_id`.
  - Cột `rental_agreements.signed_at = NULL` (không gán giá trị trong Function 4).
  - Giao diện WPF hiển thị điều khoản lấy từ `PolicyContentJson` qua hàm `FormatPolicyTerms`, và checkbox ghi rõ "Tôi đã đọc, hiểu rõ và chấp thuận Điều khoản thuê kho trên" (loại bỏ từ "ký số").

### 2.10. Tách "tiền đã đến" khỏi "đặt kho thành công" trên WPF (Lỗi 10)
- **Cơ chế**:
  - Trong `CheckoutWindow.xaml`: bổ sung khối giao diện cảnh báo đối soát `panelReconciliationOutcome` (viền vàng cam, biểu tượng ⚠️).
  - Trong `CheckoutWindow.xaml.cs`:
    ```csharp
    if (payment.ReconciliationRequired)
    {
        txtReconReservationCode.Text = payment.ReservationCode ?? $"#{_reservationId}";
        txtReconPaymentId.Text = $"#{payment.PaymentId}";
        txtReconPaidAmount.Text = $"{payment.Amount:N0} đ";

        gridMainForm.Visibility = Visibility.Collapsed;
        panelSuccessOutcome.Visibility = Visibility.Collapsed;
        panelReconciliationOutcome.Visibility = Visibility.Visible;
        return;
    }
    ```
  - Chỉ hiển thị màn hình chúc mừng `panelSuccessOutcome` khi reservation `confirmed` và hợp đồng `scheduled` được server xác nhận thành công trọn vẹn.

### 2.11. Bổ sung quản lý và phê duyệt hoàn tiền (Lỗi 11)
- **Cơ chế**:
  - Thêm DTOs [`ReviewRefundRequest.cs`](file:///d:/FPT/PRN222/Project/Final_Project/src/SelfStorageManagementSystem.BusinessLogic/DTOs/Requests/Payments/ReviewRefundRequest.cs) và [`RefundDetailResponse.cs`](file:///d:/FPT/PRN222/Project/Final_Project/src/SelfStorageManagementSystem.BusinessLogic/DTOs/Responses/Payments/RefundDetailResponse.cs).
  - Thêm endpoints:
    - `GET /api/payments/refunds?status=...`: Tra cứu danh sách yêu cầu hoàn tiền (phân quyền nhân viên/quản lý cơ sở).
    - `POST /api/payments/refunds/{id}/review`: Phê duyệt hoặc từ chối hoàn tiền (`decision`: "approved" | "rejected").
  - Lưu bản ghi phê duyệt vào bảng `core.refund_approvals` và cập nhật `refund.status = cleanDecision`.

### 2.12. Chi tiết cấu hình, toán tử promotion và hạ tầng kiểm thử (Lỗi 12)
- **Cơ chế**:
  - `VnpayGateway`: Ném `InvalidOperationException` nếu `Vnpay:HashSecret` thiếu hoặc rỗng, loại bỏ hoàn toàn khóa fallback giả.
  - `PaymentService.EvaluateRule`: Hỗ trợ đầy đủ các toán tử `eq`/`==`, `gte`/`>=`, `lte`/`<=`, `in` (qua hàm trợ giúp `ParseIdList`).
  - `PaymentService.CalculatePromotionDiscountAsync`: Chặn `discount_value % 1m != 0m` đối với loại `free_days`, ném `BadRequestException`.
  - `PaymentsController.DemoSimulator`: Trang HTML hướng dẫn quay lại ứng dụng WPF bấm nút "⚡ Giả lập thanh toán thành công (Demo)" để gửi request có token JWT hợp lệ.
  - `PaymentSqlServerConcurrencyTests.cs`: Sửa `PaymentMethod = "other"` để thỏa mãn CHECK constraint `chk_payments_method`; đảo ngược thứ tự dọn dẹp các bảng (`payment_allocations` -> `refunds` -> `payments` -> `invoice_lines` -> `invoices` -> `promotion_redemptions` -> `rental_agreements` -> `reservations`) để không vi phạm FK constraints.

---

## 3. Kết Quả Kiểm Thử Toàn Bộ Hệ Thống

### Lệnh chạy kiểm thử:
```powershell
dotnet test SelfStorageManagementSystem.sln --no-restore
```

### Kết quả xuất ra thực tế:
```text
Passed!  - Failed: 0, Passed: 167, Skipped: 2, Total: 169, Duration: 1 s - SelfStorageManagementSystem.Tests.dll (net8.0)
```
- **167 tests PASSED** (Bao gồm đầy đủ test cases kiểm tra 12 finding).
- **2 tests SKIPPED an toàn**: 2 bài test concurrency trên SQL Server thật (`PaymentSqlServerConcurrencyTests` và `ReservationSqlServerConcurrencyTests`) tự động skip khi biến môi trường `SELFSTORAGE_SQL_TEST_CONNECTION_STRING` chưa được đặt.
- **0 bài kiểm thử FAILED**, **0 CS Warning**, **0 Build Error**.

---

## 4. Báo Cáo Minh Bạch: Đã Kiểm Chứng vs. Chưa Kiểm Chứng

### A. Những phần ĐÃ kiểm chứng độc lập:
1. Công thức tính toán tài chính: cọc 1 tháng (**BR-FIN-01**), tiền thuê tháng đầu, booking fee, voucher (fixed, percentage, free_days theo ngày thực tế 28-31 ngày).
2. Ràng buộc bảo mật simulator: chặn tuyệt đối ngoài Development/Test hoặc khi thiếu cờ cấu hình; chặn simulator đối với giao dịch VNPAY.
3. Webhook IPN kiểm tra checksum HMAC-SHA512, TmnCode, Currency, Amount và phản hồi chuẩn các mã `00`, `01`, `02`, `04`, `97`.
4. Xử lý giao dịch trễ và trùng: tự động chuyển succeeded, gắn cờ `reconciliation_required = true`, tạo bản ghi `refunds`, không tạo trùng hợp đồng.
5. Quy trình nhân viên/quản lý duyệt hoàn tiền qua API (`GET /api/payments/refunds`, `POST /api/payments/refunds/{id}/review`) ghi vết vào `refund_approvals`.
6. Giao diện WPF hiển thị điều khoản từ JSON, cảnh báo đối soát hoàn tiền và ẩn nút giả lập khi chọn VNPAY.

### B. Những phần CHƯA kiểm chứng thực địa (Do giới hạn môi trường học phần):
1. **IPN internet công khai từ VNPAY Sandbox**: Webhook đã được kiểm thử toàn diện bằng HTTP server-to-server giả lập chữ ký chuẩn. Chưa nhận webhook từ internet do localhost chưa có domain public/reverse proxy đăng ký với VNPAY Merchant.
2. **SQL Server Concurrency Test**: Test concurrency tự động skip khi chưa cấu hình database test riêng. Logic khóa `sp_getapplock` đã hoàn tất trong code.
3. **Ký số pháp lý & Hóa đơn GTGT**: Lưu vết chấp thuận trong metadata; `tax_amount = 0` và biên nhận là hóa đơn nội bộ demo, chưa kết nối cơ quan thuế.
4. **Gán ô kho vật lý (Check-in)**: Hợp đồng dừng ở `scheduled` và chưa gán `storage_unit_id`, sẵn sàng bàn giao cho **Function 5**.

---

## 5. Trạng Thái Git & Bảo Toàn Nhánh

- **Nhánh hiện tại**: `Payment&RentalAgreement`
- **Không tự động commit hoặc push**: Giữ nguyên trạng thái working tree cho developer kiểm tra và tự commit.
- **Không merge main**: Hoàn toàn cô lập trên nhánh hiện tại.
- **Tệp báo cáo lưu tại**: `AgentReport/function4Fix.md` và `AgentReport/DoneFunction4.md`.
