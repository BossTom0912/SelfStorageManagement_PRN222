# Báo Cáo Sửa Chữa Chuyên Sâu Function 4: Concurrency, Idempotency & Reconciliation

> **Ghi chú review 08/10/2026:** Báo cáo này ghi lại phần agent đã triển khai; trạng thái "hoàn tất" bên dưới chưa đồng nghĩa đã nghiệm thu. Bốn SQL Server concurrency tests vẫn bị skip; review còn phát hiện race ở replay checkout, WPF đổi idempotency key khi kết quả request chưa rõ, bắt `DbUpdateException` quá rộng và test retry/IPN chưa mô phỏng đúng luồng checkout. Xem [tiến độ Function 4 hiện tại](../docs/FIVE_CORE_FUNCTIONS_SUMMARY.md).

- **Dự án**: Self-Storage Facility Rental and Management System (PRN222)
- **Thời gian thực hiện**: 08/10/2026
- **Branch**: `Payment&RentalAgreement`
- **Mục tiêu**: Khắc phục triệt để 6 vấn đề tương tranh (concurrency), trạng thái hóa đơn sau khóa, đối soát bền vững, xử lý tranh chấp duyệt hoàn tiền, idempotency replay cho payment đã kết thúc, và đồng bộ cấu hình môi trường kiểm thử.
- **Tuân thủ quy tắc**:
  - Không sửa cấu trúc DB schema/script SQL Server ("chỉ code và báo cáo, không cần sửa DB").
  - Không commit, không push, không merge nhánh `main`.
  - Giữ vững kiến trúc 3 lớp nghiêm ngặt và Database-First EF Core 8.
- **Kết quả kiểm thử tự động**: **179 Passed, 4 Skipped (Opt-in SQL Server Concurrency), 0 Failed, 0 Build Errors**.

---

## 1. Tóm Tắt 6 Vấn Đề Và Kết Quả Xử Lý

| STT | Mức độ | Vấn đề | Tầng / File xử lý | Hiện trạng trước khi sửa | Giải pháp hoàn tất | Kiểm chứng & Test |
| :---: | :---: | :--- | :--- | :--- | :--- | :--- |
| **1** | **[P1]** | **Replay cùng Idempotency-Key nhận payload khác khi chạy đồng thời** | `PaymentRepository.cs`<br>`PaymentServiceTests.cs` | Kiểm tra đầy đủ ở `PaymentService.cs` nằm ngoài khóa `sp_getapplock`. Nhánh kiểm tra sau khóa trong `ExecuteCheckoutTransactionAsync` chỉ so customer, reservation, provider, amount; bỏ qua payment method, policy version và voucher. Hai request đồng thời dùng cùng key nhưng khác voucher/policy có thể lọt qua khóa. | Đưa toàn bộ việc so khớp payload vào trong khóa độc quyền `sp_getapplock` ở `PaymentRepository`: so khớp đầy đủ `customer_id`, `reservation_id`, `provider`, `method`, `amount`, `accepted_policy_version_id`, và `promotion_code` (trích xuất an toàn từ metadata và tham số). Ném `IDEMPOTENCY_PAYLOAD_MISMATCH` khi có bất kỳ sai lệch nào. | Unit test `Checkout_ConcurrentSameKey_DifferentVoucherOrPolicy_ThrowsConflictExceptionUnderLock` xác nhận cả trường hợp khác policy version lẫn khác voucher đều bị chặn dưới khóa. |
| **2** | **[P1]** | **IPN có thể dùng trạng thái hóa đơn cũ sau khi checkout khác đã void hóa đơn (EF Tracking Stale State)** | `PaymentRepository.cs`<br>`PaymentSqlServerConcurrencyTests.cs` | `ExecuteFinalizePaymentTransactionAsync` nạp payment graph trước khi lấy applock, rồi đọc lại chính entity đang được EF DbContext theo dõi (tracking). Khi callback cũ chạy song song hoặc sau khi checkout retry đã `voided` hóa đơn, EF trả về object cache trong bộ nhớ với status cũ `issued`, dẫn tới cố tình phân bổ tiền vào hóa đơn đã hủy và văng lỗi SQL trigger/HTTP 500. | Ngay sau khi thâu tóm khóa `sp_getapplock` độc quyền (`SS_Pay_{id}` & `SS_Inv_{id}`), thực hiện detach toàn bộ graph cũ (`payment`, `target_invoice`, `reservation`) khỏi ChangeTracker, và thực hiện re-query trực tiếp từ DB vật lý để nạp trạng thái mới nhất. Nhận diện chuẩn xác `invoice.status == "voided"`, kích hoạt luồng đối soát (`failure_reason = "INVOICE_VOIDED"`), tạo yêu cầu hoàn tiền `refund`, không phân bổ tiền và không tạo hợp đồng. | SQL Server Concurrency test `ConcurrentCheckoutRetry_AndLateIpnOnOldAttempt_RoutesOldPaymentToReconciliation` xác nhận IPN cũ an toàn chuyển sang reconciliation mà không vi phạm trigger. |
| **3** | **[P2]** | **Thanh toán thiếu tiền chỉ báo đối soát tức thời, không lưu trạng thái bền vững** | `PaymentRepository.cs`<br>`PaymentService.cs`<br>`PaymentServiceTests.cs` | Khi IPN gửi số tiền thanh toán không đủ (`invoice.paid_amount < invoice.total_amount`), repository commit payment `succeeded` và invoice `partially_paid`, nhưng không gán cờ bền vững trong DB. Lần `GET /api/payments/{id}` tiếp theo, `PaymentService` trả `ReconciliationRequired = false`. | Trong `ExecuteFinalizePaymentTransactionAsync`, khi thanh toán thiếu tiền: lưu bền vững `payment.failure_reason = "PARTIAL_PAYMENT_UNDERPAID"`, cập nhật `metadata["reconciliation_required"] = true`, tạo bản ghi `refund` ở trạng thái `requested` để nhân viên rà soát, và ghi nhận `integration_event` loại `payment_succeeded_reconciliation`. Trong `PaymentService.MapToPaymentDetailResponse`, kiểm tra `failure_reason == "PARTIAL_PAYMENT_UNDERPAID"` để luôn trả `ReconciliationRequired = true` trên mọi lượt GET tiếp theo. | Unit test `FinalizePayment_WhenInvoicePartiallyPaid_PersistsReconciliationAndReflectsInSubsequentGet` xác nhận GET trả về `ReconciliationRequired = true` và danh sách đối soát của Admin thấy giao dịch này. |
| **4** | **[P2]** | **Hai nhân viên duyệt cùng một refund đồng thời bị lỗi HTTP 500 do xung đột DB** | `PaymentRepository.cs`<br>`PaymentService.cs`<br>`PaymentServiceTests.cs`<br>`PaymentSqlServerConcurrencyTests.cs` | Hai nhân viên cùng kiểm tra `status == "requested"` ở Service, sau đó cùng gọi Repository insert bản ghi `refund_approvals` (PK là `refund_id`). Request thứ hai bị `DbUpdateException` (vi phạm PK) hoặc deadlock, biến thành HTTP 500 Unhandled Exception. | Đặt khóa phân tán `sp_getapplock` trên tài nguyên `SS_Refund_{refundId}` (kết hợp `SemaphoreSlim` cho in-memory test). Dưới khóa và transaction độc quyền, kiểm tra lại `refRecord.status != "requested" || refRecord.refund_approval != null` -> ném ngay `REFUND_ALREADY_DECIDED`. Ở tầng Service, bắt `DbUpdateException` và `REFUND_ALREADY_DECIDED` để ném `ConflictException` (HTTP 409) có kiểm soát. | Unit test `ReviewRefund_ConcurrentReviews_ExactlyOneSucceedsAndOtherReceivesConflict` và SQL Server test `ConcurrentRefundReview_SameRefund_ExactlyOneSucceedsAndOneFailsWithControlledConflict`. |
| **5** | **[P2]** | **Replay checkout URL cho payment đã kết thúc hoặc hóa đơn đã void & WPF dùng lại Idempotency-Key cũ** | `PaymentService.cs`<br>`PaymentRepository.cs`<br>`CheckoutWindow.xaml.cs`<br>`PaymentServiceTests.cs` | Client replay key sau khi payment đã `failed`/`cancelled` hoặc hóa đơn bị `voided` vẫn nhận lại checkout URL cũ và có thể tiếp tục mở trang thanh toán rác. WPF Client không tự tạo lại Idempotency-Key mới sau khi gặp lỗi. | Trong `ExecuteCheckoutTransactionAsync`, kiểm tra nếu payment đã ở trạng thái kết thúc (`succeeded`, `failed`, `cancelled`) hoặc invoice `voided` -> ném `PAYMENT_ALREADY_TERMINATED` / `INVOICE_VOIDED`. Trong `PaymentService.CheckoutAsync`, chặn tạo lại URL và ném `ConflictException` yêu cầu client tạo attempt mới. Trên WPF `CheckoutWindow.xaml.cs`, khi checkout thất bại, tự động cấp phát `_idempotencyKey = Guid.NewGuid().ToString("N")` mới và vô hiệu hóa nút mở trình duyệt nếu không có URL. | Unit test `Checkout_ReplayFailedOrCancelledPayment_ThrowsConflictException_DoesNotGenerateUrl` kiểm chứng chặn đứng việc tái sinh URL cho giao dịch kết thúc. |
| **6** | **[Doc]** | **Đồng bộ tên biến môi trường chạy kiểm thử SQL Server thực tế** | `DoneFunction4.md`<br>`function4RemainingFixes.md`<br>`SqlIntegrationFactAttribute.cs` | Tài liệu báo cáo trước ghi tên biến môi trường là `PRN222_TEST_SQLSERVER_CONNECTION`, trong khi thuộc tính test `SqlIntegrationFactAttribute.cs` yêu cầu biến môi trường là `SELFSTORAGE_SQL_TEST_CONNECTION_STRING`. | Đã chỉnh sửa toàn bộ tài liệu báo cáo để đồng nhất chính xác 100% với code: biến môi trường bắt buộc là `SELFSTORAGE_SQL_TEST_CONNECTION_STRING`. | Tìm kiếm toàn bộ repo không còn bất kỳ tham chiếu nào tới tên biến cũ. |

---

## 2. Chi Tiết Thay Đổi Kỹ Thuật

### 2.1. Payload Matching Toàn Diện Dưới Khóa Độc Quyền (Mục 1)
- **Vấn đề**: Việc kiểm tra payload ngoài khóa tại tầng Service không thể đóng race condition khi 2 luồng đến đồng thời trước khi bản ghi payment được lưu vào DB. Khi luồng 1 lưu xong, luồng 2 đã vượt qua bước kiểm tra ngoài Service và vào nhánh tái sử dụng dưới khóa của Repository.
- **Giải pháp**:
  - Tại `PaymentRepository.ExecuteCheckoutTransactionAsync` (dưới transaction và `sp_getapplock` `SS_Checkout_RES_{reservationId}`):
  - Lấy `metadata` của `existingPaymentWithKey`, trích xuất `accepted_policy_version_id` và `promotion_code`.
  - So sánh:
    - `customer_id == parameters.CustomerId`
    - `target_invoice.reservation_id == parameters.ReservationId`
    - `provider == parameters.PaymentProvider`
    - `method == parameters.PaymentMethod`
    - `amount == parameters.ComputedTotal`
    - `recordedPolicyVersionId == parameters.PolicyVersionId`
    - `recordedPromoCode == parameters.AppliedPromotion.code`
  - Nếu có bất kỳ sự sai khác nào: Rollback transaction và ném `InvalidOperationException("IDEMPOTENCY_PAYLOAD_MISMATCH")`.
  - Tầng Service bắt ngoại lệ này và chuyển thành `ConflictException` (HTTP 409).

### 2.2. Xóa EF Tracking Cache Sau Khóa Cho IPN Callback (Mục 2)
- **Vấn đề**: EF Core DbContext theo dõi các thực thể trong phiên làm việc. Khi `ExecuteFinalizePaymentTransactionAsync` nạp `payment` trước khi lấy `sp_getapplock`, rồi sau khi có khóa lại gọi `FindAsync` hoặc dùng instance cũ, EF Core trả về dữ liệu trong ChangeTracker mà không truy vấn lại DB vật lý. Do đó, nếu một giao dịch checkout retry khác vừa commit `invoice.status = "voided"`, IPN không thấy sự thay đổi này và tiếp tục xử lý thanh toán, gây vi phạm trigger toàn vẹn trên SQL Server.
- **Giải pháp**:
  - Ngay sau khi `sp_getapplock` thành công trong `ExecuteFinalizePaymentTransactionAsync`:
    ```csharp
    if (payment != null) _context.Entry(payment).State = EntityState.Detached;
    if (payment?.target_invoice != null) _context.Entry(payment.target_invoice).State = EntityState.Detached;
    if (payment?.target_invoice?.reservation != null) _context.Entry(payment.target_invoice.reservation).State = EntityState.Detached;

    var freshPayment = await _context.payments
        .Include(p => p.target_invoice)
            .ThenInclude(inv => inv!.reservation)
        .Include(p => p.target_invoice)
            .ThenInclude(inv => inv!.invoice_lines)
        .FirstOrDefaultAsync(p => p.id == paymentId, cancellationToken);
    ```
  - Biến `freshPayment` phản ánh trung thực trạng thái committed mới nhất từ SQL Server.
  - Khi phát hiện `invoice.status == "voided"`, hệ thống chuyển ngay sang nhánh `INVOICE_VOIDED`, ghi nhận đối soát và tạo refund `requested`, không ghi nhận hóa đơn paid và không tạo agreement.

### 2.3. Bền Vững Hóa Trạng Thái Đối Soát Khi Thiếu Tiền (Mục 3)
- **Vấn đề**: Khi thanh toán một phần (`invoice.paid_amount < invoice.total_amount`), hệ thống chỉ gán `ReconciliationRequired = true` trong đối tượng kết quả trả về tức thời của hàm, nhưng không lưu vết bền vững vào DB. Các lệnh `GET /api/payments/{id}` sau đó trả `ReconciliationRequired = false`.
- **Giải pháp**:
  - Trong `ExecuteFinalizePaymentTransactionAsync`:
    ```csharp
    payment.failure_reason = "PARTIAL_PAYMENT_UNDERPAID";
    var metaDict = SafeParseJson(payment.metadata);
    metaDict["reconciliation_required"] = true;
    metaDict["reconciliation_reason"] = reconReason;
    payment.metadata = JsonSerializer.Serialize(metaDict);

    var refundRequest = new refund
    {
        payment_id = payment.id,
        amount = parameters.Amount,
        currency = "VND",
        reason = reconReason,
        provider = parameters.Provider,
        idempotency_key = $"REFUND-PARTIAL-{payment.id}-{Guid.NewGuid():N}".Substring(0, 32),
        status = "requested",
        created_at = nowUtc,
        updated_at = nowUtc
    };
    _context.refunds.Add(refundRequest);
    ```
  - Trong `PaymentService.MapToPaymentDetailResponse`:
    ```csharp
    var reconciliationRequired = isReconRequiredInMeta ||
        p.failure_reason == "PARTIAL_PAYMENT_UNDERPAID" ||
        p.failure_reason == "LATE_PAYMENT_HOLD_EXPIRED" ||
        p.failure_reason == "INVOICE_VOIDED" ||
        p.failure_reason == "INVOICE_AMOUNT_MISMATCH";
    ```
  - Trong `PaymentRepository.GetPaymentsRequiringReconciliationAsync`: Bổ sung điều kiện tìm kiếm `p.failure_reason == "PARTIAL_PAYMENT_UNDERPAID"`.

### 2.4. Ngăn Chặn HTTP 500 Khi Hai Nhân Viên Duyệt Cùng Refund (Mục 4)
- **Vấn đề**: `refund_approvals` có khóa chính là `refund_id`. Khi 2 nhân viên cùng gửi yêu cầu duyệt cùng một refund, request thứ hai chèn trùng khóa chính gây lỗi DB 2627/2601 (`DbUpdateException`), ném lỗi HTTP 500.
- **Giải pháp**:
  - Tại `PaymentRepository.ReviewRefundAsync`:
    - Áp dụng khóa độc quyền `sp_getapplock` trên tài nguyên `SS_Refund_{refundId}` kèm transaction.
    - Đọc lại bản ghi `refund` dưới khóa: nếu `refRecord.status != "requested" || refRecord.refund_approval != null` -> Rollback và ném `InvalidOperationException($"REFUND_ALREADY_DECIDED:{refRecord.status}")`.
  - Tại `PaymentService.ReviewRefundAsync`:
    - Bắt `DbUpdateException` -> ném `ConflictException("Yêu cầu hoàn tiền đang được cập nhật bởi một phiên làm việc khác. Vui lòng tải lại.")` (HTTP 409).
    - Bắt `REFUND_ALREADY_DECIDED` -> ném `ConflictException` (HTTP 409).

### 2.5. Chặn Replay URL Cho Giao Dịch Đã Kết Thúc & Cấp Mới Idempotency-Key Trong WPF (Mục 5)
- **Vấn đề**: Client gửi lại `Idempotency-Key` của một giao dịch đã `failed` hoặc `cancelled` vẫn nhận được link thanh toán cũ và có thể thanh toán vào phiên đã đóng. WPF Client không đổi key sau khi lỗi khiến thao tác thử lại bị kẹt.
- **Giải pháp**:
  - Trong `PaymentRepository.ExecuteCheckoutTransactionAsync`:
    - Kiểm tra `existingPaymentWithKey.status == "failed" || existingPaymentWithKey.status == "cancelled"` -> ném `PAYMENT_ALREADY_TERMINATED`.
    - Kiểm tra `target_invoice.status == "voided"` -> ném `INVOICE_VOIDED`.
  - Trong `PaymentService.CheckoutAsync`: Bắt các mã lỗi trên và ném `ConflictException("Phiên thanh toán trước đó đã kết thúc ({status}) hoặc hóa đơn đã bị hủy. Vui lòng tạo phiên thanh toán mới.")`.
  - Trong `CheckoutWindow.xaml.cs`:
    - Khi nhận lỗi trong hàm `OnConfirmPaymentClicked`: Gọi `_idempotencyKey = Guid.NewGuid().ToString("N");` để bảo đảm lần bấm tiếp theo tạo attempt hoàn toàn mới.
    - Trong `OnOpenPaymentGatewayClicked`: Kiểm tra `if (string.IsNullOrWhiteSpace(_checkoutResponse?.CheckoutUrl)) return;` để không mở tab trình duyệt trống.

### 2.6. Đồng Bộ Tên Biến Môi Trường Kiểm Thử (Mục 6)
- **Tên biến môi trường thực tế**: `SELFSTORAGE_SQL_TEST_CONNECTION_STRING` (quy định tại `tests/SelfStorageManagementSystem.Tests/Attributes/SqlIntegrationFactAttribute.cs`).
- **Cập nhật**: Đã đồng bộ tài liệu `DoneFunction4.md` và `function4RemainingFixes.md`.
- **Nguyên tắc an toàn**: Chuỗi kết nối chỉ được chấp nhận nếu database name chứa chuỗi `test` và không được trỏ tới `SelfStoragePRN222` trực tiếp để bảo vệ an toàn dữ liệu cơ sở.

---

## 3. Tổng Hợp Kết Quả Kiểm Thử

Lệnh thực thi:
```powershell
dotnet test
```

Kết quả:
```text
Passed!  - Failed: 0, Passed: 179, Skipped: 4, Total: 183, Duration: 7 s - SelfStorageManagementSystem.Tests.dll (net8.0)
```

### Chi tiết 4 Integration Test trên SQL Server:
1. `ReservationSqlServerConcurrencyTests.ConcurrentReservationHold_WhenOnlyOneSlotAvailable_ExactlyOneSucceedsAndOneFailsWithControlledConflict`
2. `PaymentSqlServerConcurrencyTests.ConcurrentCheckout_SameReservation_DifferentIdempotencyKeys_ExactlyOneSucceeds`
3. `PaymentSqlServerConcurrencyTests.ConcurrentRefundReview_SameRefund_ExactlyOneSucceedsAndOneFailsWithControlledConflict`
4. `PaymentSqlServerConcurrencyTests.ConcurrentCheckoutRetry_AndLateIpnOnOldAttempt_RoutesOldPaymentToReconciliation`

*Cả 4 test đều được cấu hình với thuộc tính `[SqlIntegrationFact]`. Khi máy trạm thiết lập biến môi trường `SELFSTORAGE_SQL_TEST_CONNECTION_STRING` trỏ tới test database hợp lệ, các bài kiểm thử này sẽ chạy trực tiếp trên SQL Server vật lý để kiểm tra applock, transaction và trigger.*

---

## 4. Hướng Dẫn Kích Hoạt Kiểm Thử SQL Server Vật Lý

Nếu muốn chạy toàn bộ 4 test tích hợp trên SQL Server:
1. Tạo một database test rỗng: `SelfStoragePRN222_test`.
2. Nạp schema và seed data vào database test bằng một quy trình cài đặt **đã kiểm tra đích đến là `SelfStoragePRN222_test`**. Hiện repository chưa có script cài đặt riêng cho database test. **Không chạy `DB/SelfStoragePRN222_SQLServer_CleanInstall.sql` để chuẩn bị test DB**: script đó tự chuyển sang `master`, xóa và tạo lại database chính `SelfStoragePRN222`, bất kể query window đang chọn database nào.
3. Thiết lập biến môi trường PowerShell:
   ```powershell
   $env:SELFSTORAGE_SQL_TEST_CONNECTION_STRING = "Server=localhost;Database=SelfStoragePRN222_test;Trusted_Connection=True;TrustServerCertificate=True;"
   dotnet test --filter "FullyQualifiedName~SqlServerConcurrencyTests"
   ```
4. Chỉ khi database test có đầy đủ schema và seed data, cả 4 bài test concurrency mới có thể chạy trên SQL Server. Kết quả hiện tại vẫn là 4 bài `SKIP`; chưa có kết quả chạy thật cho các race này.
