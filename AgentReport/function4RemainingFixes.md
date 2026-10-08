# Báo Cáo Sửa Các Lỗi Còn Lại Của Chức Năng 4: Payment & Rental Agreement

- **Dự án**: Self-Storage Facility Rental and Management System (PRN222)
- **Thời gian thực hiện**: 08/10/2026
- **Branch làm việc**: `Payment&RentalAgreement`
- **Kiến trúc áp dụng**: Strict 3-Layer Architecture (Presentation, BusinessLogic, DataAccess) + WPF Client (.NET 8, ASP.NET Core Web API, EF Core 8 Database-First, SQL Server `core` schema)
- **Tình trạng kiểm thử**: **176 Passed, 2 Skipped (Opt-in SQL Server Concurrency), 0 Failed, 0 Build Errors**

---

## 1. Tóm Tắt 6 Vấn Đề Được Rà Soát & Khắc Phục (Ưu Tiên P1 & P2)

| STT | Mức độ | Vấn đề | Tầng / File bị ảnh hưởng | Hiện trạng trước khi sửa | Giải pháp đã hoàn tất | Test kiểm chứng |
| :---: | :---: | :--- | :--- | :--- | :--- | :--- |
| **1** | **[P1]** | **API hoàn tiền thiếu giới hạn theo cơ sở** (Lộ dữ liệu & duyệt sai cơ sở) | `PaymentService.cs`<br>`PaymentRepository.cs`<br>`PaymentsController.cs` | `GetRefundsAsync` và `ReviewRefundAsync` chỉ kiểm tra Role. Nhân viên `facility_staff`/`facility_manager` cơ sở A có thể đọc toàn bộ danh sách refund của cơ sở B và POST review refund cơ sở B. | Sử dụng `_facilityScopeService.GetAccessibleFacilityIdsAsync` để lọc refunds ở cấp độ DB query (LINQ where). Khi review, phân giải refund -> payment -> target_invoice -> reservation -> facility_id (hoặc agreement -> facility_id); nếu nhân viên không có quyền trên cơ sở tương ứng thì trả `403 Forbidden`. Quản trị viên (`system_administrator`, `business_operations_manager`) có quyền toàn hệ thống. | `GetRefunds_FacilityStaff_ReturnsOnlyAssignedFacilityRefunds`<br>`ReviewRefund_FacilityStaff_ReviewingOtherFacility_ThrowsForbiddenException`<br>`ReviewRefund_SystemAdministrator_CanReviewAnyFacility` |
| **2** | **[P1]** | **Checkout lại sửa hóa đơn đang được payment attempt cũ tham chiếu & thiếu kiểm tra paid_amount trước khi tạo hợp đồng** | `PaymentRepository.cs` | Khi retry checkout với báo giá/voucher mới, code cũ xóa/tạo lại `invoice_lines` và sửa `total_amount` trên chính hóa đơn cũ; payment attempt 1 vẫn trỏ vào hóa đơn đó. Nếu payment attempt 1 hoàn tất muộn sẽ làm hóa đơn bị trả sai số tiền hoặc sai thỏa thuận. Ngoài ra, code cũ cấp hợp đồng mà không đối chiếu chặt chẽ `invoice.status == 'paid'` và `invoice.paid_amount == invoice.total_amount`. | **Bảo đảm tính bất biến của hóa đơn (Invoice Immutability)**: Khi checkout lại có thay đổi số tiền hoặc hóa đơn cũ đã có payment attempts tham chiếu, hủy hóa đơn cũ (`status = 'voided'`, `voided_at = nowUtc`) và phát hành hóa đơn mới (`INV-...`). Nếu payment callback của attempt cũ đến muộn trỏ vào hóa đơn `voided` hoặc lệch số tiền: tự động chuyển sang luồng tra soát đối soát (`reconciliation_required = true`), tạo bản ghi `refund` ở trạng thái `requested`, **không tạo hợp đồng**, **không confirm reservation**. Trong finalization, phân bổ chính xác `parameters.Amount` và bắt buộc `invoice.status == "paid" && invoice.paid_amount == invoice.total_amount` trước khi cấp agreement. | `Checkout_RetryWithDifferentQuote_VoidsOldInvoice_AndLatePaymentRoutesToReconciliation`<br>`FinalizePayment_WhenInvoicePartiallyPaid_DoesNotCreateAgreementOrConfirmReservation` |
| **3** | **[P2]** | **Kiểm tra Idempotency Replay chạy sau bước kiểm tra giới hạn voucher** | `PaymentService.cs` | Code cũ gọi `CalculatePromotionDiscountAsync` và kiểm tra `usage_limit`/`per_customer_limit` trước khi kiểm tra Idempotency Key. Khi client gọi lại với cùng Idempotency Key mà voucher vừa chạm trần lượt dùng, request bị ném 400 thay vì trả kết quả replay. Ngoài ra không so khớp payload khi replay. | Di chuyển kiểm tra `GetPaymentByIdempotencyKeyAsync` lên đầu phương thức `CheckoutAsync`. So khớp toàn bộ payload (`CustomerId`, `ReservationId`, `PaymentMethod`, `PromotionCode`, `AcceptedPolicyVersionId`). Nếu payload lệch -> ném `409 Conflict`. Nếu payload trùng khớp hoàn toàn -> trả lại ngay `CheckoutResponse` đã lưu mà không thẩm định lại voucher. | `Checkout_IdempotencyReplay_WhenVoucherLimitReached_SucceedsWithoutRevalidatingVoucher`<br>`Checkout_IdempotencyReplay_WhenPayloadMismatched_ThrowsConflictException` |
| **4** | **[P2]** | **Retry bỏ voucher giữ nguyên `discount_snapshot` và redemption `reserved`** | `PaymentRepository.cs`<br>`PaymentService.cs` | Khi retry checkout mà không nhập voucher (`AppliedPromotion == null`), code cũ chỉ không thêm redemption mới, giữ nguyên redemption `reserved` cũ và `reservation.discount_snapshot`. Khi finalize, code luôn chuyển redemption sang `applied` dù attempt không dùng voucher. | Trong `ExecuteCheckoutTransactionAsync`, nếu `AppliedPromotion == null`: giải phóng toàn bộ redemption `reserved` cũ của reservation (`status = 'released'`) và reset `reservation.discount_snapshot = 0`. Nếu đổi sang mã voucher khác: giải phóng các redemption của voucher cũ. Ghi nhận `promotion_id` vào metadata của payment attempt. Khi finalization, chỉ chuyển redemption sang `applied` nếu metadata của payment đó thực sự có `promotion_id`. | `Checkout_RetryWithoutVoucher_ReleasesExistingReservedRedemption` |
| **5** | **[P2]** | **Admin review refund xung đột khóa ngoại SQL Server** | `SelfStoragePRN222_SQLServer_CleanInstall.sql`<br>`refund_approval.cs`<br>`user.cs`<br>`employee_profile.cs`<br>`SelfStorageDbContext.cs` | Khóa ngoại `refund_approvals.decided_by` trỏ tới `[core].employee_profiles(user_id)`. Tài khoản `system_administrator` chỉ có bản ghi `users`, không có `employee_profiles`. Khi Admin duyệt refund, SQL Server ném lỗi FK constraint `547`. | Đổi FK của `refund_approvals.decided_by` trỏ tới `[core].users(id)`. Cập nhật entity scaffold EF Core: `refund_approval.decided_byNavigation` trỏ tới `user`, cập nhật DbContext Fluent API mapping. Admin và nhân viên đều duyệt hoàn tiền hợp lệ mà không vi phạm toàn vẹn khóa ngoại. | `ReviewRefund_SystemAdministrator_CanReviewAnyFacility`<br>Biên dịch DataAccess, BusinessLogic, Presentation, WPF, Tests thành công 100%. |
| **6** | **[P2]** | **Refund không tồn tại hoặc đã duyệt bị trả HTTP 500 thay vì 404/409** | `PaymentService.cs`<br>`PaymentRepository.cs`<br>`PaymentsController.cs` | `ReviewRefundAsync` ném `InvalidOperationException("REFUND_NOT_FOUND")` hoặc `InvalidOperationException("REFUND_ALREADY_DECIDED:...")`, khiến middleware ASP.NET Core bắt thành HTTP 500 Unhandled Exception. | Chuẩn hóa exception trong BusinessLogic: Refund không tồn tại -> ném `NotFoundException` (HTTP 404). Refund đã duyệt hoặc tranh chấp đồng thời -> ném `ConflictException` (HTTP 409). Cập nhật `[ProducesResponseType(StatusCodes.Status409Conflict)]` trên `PaymentsController`. | `ReviewRefund_NonExistentRefund_ThrowsNotFoundException`<br>`ReviewRefund_AlreadyDecidedRefund_ThrowsConflictException` |

---

## 2. Chi Tiết Kỹ Thuật Từng Điểm Sửa Đổi

### 2.1. [P1] Giới hạn phạm vi cơ sở (Facility Scope) trên các API hoàn tiền
- **Nguyên nhân**:
  - `PaymentService.GetRefundsAsync` và `PaymentService.ReviewRefundAsync` trước đây chỉ xác thực danh sách role người dùng thông qua `[Authorize(Roles = ...)]` mà không kiểm tra phạm vi cơ sở được phân công (`facility_assignments`).
  - Dẫn đến lỗ hổng: Một nhân viên hoặc quản lý cơ sở A có thể xem danh sách hoàn tiền của toàn bộ hệ thống (kể cả cơ sở B) và có thể POST duyệt yêu cầu hoàn tiền của cơ sở B nếu biết `refundId`.
- **Giải pháp**:
  - Tích hợp `IFacilityScopeService` vào `PaymentService`.
  - Trong `GetRefundsAsync`: Gọi `_facilityScopeService.GetAccessibleFacilityIdsAsync(currentUserId, roles)`. Nếu là nhân viên (`facility_staff` / `facility_manager`), kết quả trả về danh sách ID các cơ sở được phân công hoạt động. Danh sách này được truyền xuống `PaymentRepository.GetRefundsAsync(accessibleFacilityIds, ...)` để áp dụng bộ lọc trực tiếp trong mệnh đề `WHERE` của SQL / LINQ:
    ```csharp
    if (accessibleFacilityIds != null)
    {
        query = query.Where(r =>
            (r.payment.target_invoice != null &&
             r.payment.target_invoice.reservation != null &&
             accessibleFacilityIds.Contains(r.payment.target_invoice.reservation.facility_id))
            ||
            (r.agreement != null &&
             accessibleFacilityIds.Contains(r.agreement.facility_id)));
    }
    ```
  - Trong `ReviewRefundAsync`: Tải bản ghi refund kèm theo navigation graph (`payment -> target_invoice -> reservation -> facility` hoặc `agreement -> facility`).
    - Nếu người dùng có quyền toàn hệ thống (`system_administrator`, `business_operations_manager`), cho phép duyệt.
    - Nếu là nhân viên cơ sở: xác định `refundFacilityId`. Nếu không tìm thấy thông tin cơ sở hoặc `refundFacilityId` không nằm trong `accessibleFacilityIds`, hệ thống ném ngay `ForbiddenException` ("Bạn không có quyền xử lý yêu cầu hoàn tiền thuộc cơ sở này (ID: {refundFacilityId}).").
- **Kiểm chứng**:
  - Unit test `GetRefunds_FacilityStaff_ReturnsOnlyAssignedFacilityRefunds`: Nhân viên cơ sở 1 chỉ nhận được các yêu cầu hoàn tiền thuộc cơ sở 1, không thấy cơ sở 2.
  - Unit test `ReviewRefund_FacilityStaff_ReviewingOtherFacility_ThrowsForbiddenException`: Nhân viên cơ sở 1 gửi request duyệt refund của cơ sở 2 bị từ chối với `ForbiddenException` (HTTP 403), dữ liệu DB giữ nguyên trạng thái `requested`.
  - Unit test `ReviewRefund_SystemAdministrator_CanReviewAnyFacility`: Quản trị viên hệ thống có thể duyệt refund của bất kỳ cơ sở nào thành công.

---

### 2.2. [P1] Tính bất biến của hóa đơn (Invoice Immutability) & Kiểm tra thanh toán đủ trước khi lập hợp đồng
- **Nguyên nhân**:
  - Trong kịch bản khách hàng thanh toán lần 1 thất bại hoặc muốn đổi voucher / đổi cổng thanh toán: Code cũ tìm hóa đơn `draft`/`open` hiện có, xóa sạch `invoice_lines` và ghi đè `total_amount`. Trong khi đó, `payment attempt 1` vẫn đang lưu `target_invoice_id` trỏ vào hóa đơn đó. Nếu callback từ ngân hàng/cổng thanh toán của attempt 1 đến muộn, số tiền đã trả sẽ bị gán vào một hóa đơn có tổng tiền bị lệch.
  - Trong `ExecuteFinalizePaymentTransactionAsync`, code cũ không kiểm tra chặt chẽ `invoice.status == "paid"` và `invoice.paid_amount == invoice.total_amount`, dẫn đến nguy cơ cấp hợp đồng thuê (`rental_agreement`) ngay cả khi chỉ mới thanh toán một phần số tiền.
- **Giải pháp**:
  - **Invoice Immutability**: Trong `PaymentRepository.ExecuteCheckoutTransactionAsync`:
    ```csharp
    if (existingInvoice != null)
    {
        var hasExistingPayments = await _context.payments
            .AnyAsync(p => p.target_invoice_id == existingInvoice.id, cancellationToken);

        if (hasExistingPayments || existingInvoice.total_amount != parameters.ComputedTotal)
        {
            existingInvoice.status = "voided";
            existingInvoice.voided_at = parameters.NowUtc;
            existingInvoice.updated_at = parameters.NowUtc;
            existingInvoice = null; // Hủy hóa đơn cũ và tạo hóa đơn mới bất biến!
        }
    }
    ```
  - **Reconciliation & Refund Flow cho Callback trỏ vào Hóa đơn đã hủy / Lệch tiền**: Trong `ExecuteFinalizePaymentTransactionAsync`:
    - Nếu `invoice.status == "voided"` hoặc `invoice.total_amount != payment.amount`:
      - Đánh dấu `payment.status = "succeeded"`.
      - Ghi nhận `failure_reason = "INVOICE_VOIDED"` hoặc `"INVOICE_AMOUNT_MISMATCH"`.
      - Đánh dấu `reconciliation_required = true` trong metadata.
      - **Tự động tạo bản ghi `refund` ở trạng thái `requested`** với số tiền bằng `payment.amount` để nhân sự cơ sở rà soát hoàn tiền.
      - **Không khôi phục reservation**, **không tạo hợp đồng**, **không allocation tiền vào hóa đơn đã void**.
  - **Strict Invoice Full Payment Verification**:
    - Phân bổ chính xác số tiền thực nhận `parameters.Amount` (thay vì cố định `payment.amount` ban đầu).
    - Kiểm tra nghiêm ngặt trước khi tạo agreement:
      ```csharp
      if (invoice.status != "paid" || invoice.paid_amount != invoice.total_amount)
      {
          return new FinalizePaymentResult
          {
              IsAlreadyProcessed = false,
              ReconciliationRequired = true,
              ReconciliationReason = $"Hóa đơn chỉ mới được thanh toán một phần ({invoice.paid_amount}/{invoice.total_amount}). Chưa đủ điều kiện kích hoạt hợp đồng.",
              Payment = payment,
              Invoice = invoice,
              Reservation = reservation,
              Agreement = null
          };
      }
      ```
- **Kiểm chứng**:
  - Unit test `Checkout_RetryWithDifferentQuote_VoidsOldInvoice_AndLatePaymentRoutesToReconciliation`: Attempt 1 tạo Invoice 1. Attempt 1 thất bại/hủy. Khách retry checkout có voucher tạo Invoice 2 (Invoice 1 bị chuyển sang `voided`). Khi callback muộn của Attempt 1 đến, hệ thống nhận diện `INVOICE_VOIDED`, không tạo agreement, và sinh bản ghi refund `requested`.
  - Unit test `FinalizePayment_WhenInvoicePartiallyPaid_DoesNotCreateAgreementOrConfirmReservation`: Thanh toán thiếu (1.0M thay vì 2.05M) được chuyển sang `ReconciliationRequired = true`, reservation không được `confirmed`, và không có agreement nào được sinh ra.

---

### 2.3. [P2] Ưu tiên xử lý Idempotency Replay & Xác thực toàn vẹn Payload
- **Nguyên nhân**:
  - Trong luồng cũ, phương thức `CheckoutAsync` thực hiện tính toán voucher và kiểm tra `usage_limit` trước khi tra cứu Idempotency Key. Nếu giao dịch đầu tiên đã tiêu hao lượt dùng cuối cùng của voucher, request gửi lại (replay do timeout hoặc retry mạng) sẽ bị từ chối với lỗi `BadRequestException: Mã giảm giá đã hết lượt sử dụng`.
  - Chưa so khớp đầy đủ payload, dẫn đến rủi ro gửi cùng 1 Idempotency Key với nội dung thanh toán khác nhau nhưng vẫn nhận kết quả cũ.
- **Giải pháp**:
  - Di chuyển việc kiểm tra Idempotency Key lên vị trí đầu tiên của `CheckoutAsync`:
    - Truy vấn `_paymentRepository.GetPaymentByIdempotencyKeyAsync(idempotencyKeyHeader)`.
    - So sánh toàn bộ các trường cốt lõi của payload: `CustomerId`, `ReservationId`, `PaymentMethod`, `PromotionCode`, và `AcceptedPolicyVersionId`.
    - Nếu không khớp bất kỳ trường nào -> ném `ConflictException("Idempotency-Key đã được sử dụng cho một giao dịch khác với nội dung thanh toán không trùng khớp.")`.
    - Nếu trùng khớp -> trả lại ngay `CheckoutResponse` đã lưu kèm URL thanh toán, không tính lại discount và không kiểm tra lại voucher limit.
- **Kiểm chứng**:
  - Unit test `Checkout_IdempotencyReplay_WhenVoucherLimitReached_SucceedsWithoutRevalidatingVoucher`: Voucher có `usage_limit = 1`. Lần 1 thành công (sử dụng 1/1 lượt). Lần 2 gửi lại cùng key và payload: trả về thành công kết quả cũ mà không bị chặn bởi `usage_limit`.
  - Unit test `Checkout_IdempotencyReplay_WhenPayloadMismatched_ThrowsConflictException`: Gửi cùng key nhưng đổi `PromotionCode` bị ném `ConflictException`.

---

### 2.4. [P2] Giải phóng Voucher khi Retry bỏ mã & Liên kết khuyến mãi chính xác khi Finalization
- **Nguyên nhân**:
  - Khi khách hàng checkout lần 1 có voucher -> bản ghi `promotion_redemptions` được tạo với `status = "reserved"`, `reservation.discount_snapshot` được cập nhật.
  - Khi khách hàng retry checkout bỏ voucher -> code cũ không dọn dẹp, dẫn đến `discount_snapshot` và redemption `reserved` vẫn tồn tại. Khi thanh toán thành công, redemption cũ bị chuyển thành `applied` dù khách trả nguyên giá.
- **Giải pháp**:
  - Trong `PaymentRepository.ExecuteCheckoutTransactionAsync`:
    - Nếu `parameters.AppliedPromotion == null`: Tìm tất cả bản ghi `promotion_redemptions` đang ở trạng thái `reserved` của reservation đó và đổi `status = "released"`. Đặt `reservation.discount_snapshot = 0`.
    - Nếu khách đổi sang mã voucher khác: giải phóng các redemption của voucher cũ.
  - Lưu trữ `promotion_id` trong trường `metadata` của bản ghi `payment`.
  - Trong `ExecuteFinalizePaymentTransactionAsync`: Chỉ chuyển redemption sang `applied` nếu `payment.metadata` thực sự chứa `promotion_id` trùng khớp.
- **Kiểm chứng**:
  - Unit test `Checkout_RetryWithoutVoucher_ReleasesExistingReservedRedemption`: Lần 1 có voucher -> `status = "reserved"`. Lần 1 hủy, Lần 2 không có voucher -> redemption được đổi sang `status = "released"`, và `reservation.discount_snapshot = 0`.

---

### 2.5. [P2] Sửa ràng buộc khóa ngoại `refund_approvals.decided_by`
- **Nguyên nhân**:
  - File SQL script cũ định nghĩa: `decided_by bigint NOT NULL REFERENCES [core].employee_profiles(user_id)`.
  - Tuy nhiên, trong hệ thống phân quyền, tài khoản quản trị viên (`system_administrator`) chỉ tồn tại trong bảng `[core].users` mà không nhất thiết có bản ghi trong `[core].employee_profiles`.
  - Khi quản trị viên thực hiện phê duyệt hoàn tiền, SQL Server ném lỗi vi phạm khóa ngoại `FK__refund_app__decid__...` (Error 547).
- **Giải pháp**:
  - Cập nhật schema SQL: Sửa khóa ngoại `decided_by` trỏ tới `[core].users(id)`:
    ```sql
    CREATE TABLE [core].refund_approvals (
        refund_id bigint PRIMARY KEY REFERENCES [core].refunds(id) ON DELETE NO ACTION,
        decision nvarchar(255) NOT NULL CHECK (decision IN ('approved', 'rejected')),
        decided_by bigint NOT NULL REFERENCES [core].users(id) ON DELETE NO ACTION,
        reason nvarchar(255),
        decided_at datetimeoffset(7) NOT NULL DEFAULT SYSUTCDATETIME()
    );
    ```
  - Cập nhật EF Core Entities:
    - `refund_approval.cs`: Sửa navigation `public virtual user decided_byNavigation { get; set; } = null!;`
    - `user.cs`: Thêm quan hệ `public virtual ICollection<refund_approval> refund_approvals { get; set; } = new List<refund_approval>();`
    - `employee_profile.cs`: Loại bỏ collection navigation cũ.
    - `SelfStorageDbContext.cs`: Cập nhật Fluent API mapping tương ứng.
- **Kiểm chứng**:
  - Unit test `ReviewRefund_SystemAdministrator_CanReviewAnyFacility`: Quản trị viên duyệt refund thành công, tạo bản ghi `refund_approval` gắn đúng `decided_by = adminUserId`.
  - Toàn bộ solution biên dịch sạch 100%.

---

### 2.6. [P2] Chuẩn hóa mã lỗi HTTP cho thao tác phê duyệt hoàn tiền (404/409)
- **Nguyên nhân**:
  - Khi gọi `POST /api/payments/refunds/{id}/review` với `id` không tồn tại, hoặc khi refund đã được duyệt trước đó, repository ném `InvalidOperationException("REFUND_NOT_FOUND")` hoặc `InvalidOperationException("REFUND_ALREADY_DECIDED:...")`. Do thiếu bộ lọc mapping, lỗi bị biến thành HTTP 500 Internal Server Error.
- **Giải pháp**:
  - Bổ sung exception handling chuẩn RESTful trong `PaymentService.ReviewRefundAsync`:
    - `ex.Message == "REFUND_NOT_FOUND"` -> ném `NotFoundException("Không tìm thấy yêu cầu hoàn tiền với mã số {refundId}.")` (HTTP 404).
    - `ex.Message.StartsWith("REFUND_ALREADY_DECIDED:")` -> ném `ConflictException("Yêu cầu hoàn tiền này đã được xử lý trước đó với kết quả '{currentStatus}'.")` (HTTP 409).
    - Lỗi tranh chấp đồng thời cập nhật DB (`DbUpdateConcurrencyException`) -> ném `ConflictException("Yêu cầu hoàn tiền đang được cập nhật bởi một phiên làm việc khác. Vui lòng tải lại.")` (HTTP 409).
  - Cập nhật Controller: Thêm `[ProducesResponseType(StatusCodes.Status409Conflict)]` trên endpoint `ReviewRefund`.
- **Kiểm chứng**:
  - Unit test `ReviewRefund_NonExistentRefund_ThrowsNotFoundException`: Trả về `NotFoundException` (404).
  - Unit test `ReviewRefund_AlreadyDecidedRefund_ThrowsConflictException`: Gọi duyệt lần 2 trên refund đã quyết định trả về `ConflictException` (409).

---

## 3. Tổng Hợp Kết Quả Kiểm Thử (Test Suite Status)

Chạy lệnh `dotnet test` trên toàn bộ solution:
```text
Passed!  - Failed: 0, Passed: 176, Skipped: 2, Total: 178, Duration: 2 s - SelfStorageManagementSystem.Tests.dll (net8.0)
```

### Chi tiết 2 test được đánh dấu `[SKIP]`:
1. `SelfStorageManagementSystem.Tests.PaymentSqlServerConcurrencyTests.ConcurrentCheckout_SameReservation_DifferentIdempotencyKeys_ExactlyOneSucceeds`
2. `SelfStorageManagementSystem.Tests.ReservationSqlServerConcurrencyTests.ConcurrentReservationHold_WhenOnlyOneSlotAvailable_ExactlyOneSucceedsAndOneFailsWithControlledConflict`

> **Lưu ý minh bạch**: Các bài kiểm thử trên được thiết kế riêng với thuộc tính `[SqlIntegrationFact]`. Các test này yêu cầu biến môi trường `SELFSTORAGE_SQL_TEST_CONNECTION_STRING` trỏ tới cơ sở dữ liệu SQL Server vật lý độc lập (có tiền tố/hậu tố `test` để bảo vệ an toàn dữ liệu). Khi biến môi trường chưa được thiết lập, test tự động `SKIP` một cách có chủ đích, tuyệt đối không giả mạo kết quả đạt.

---

## 4. Bảo Toàn Tính Toàn Vẹn Của Repository & Branch
- **Không thực hiện commit, push hay merge vào nhánh `main`**.
- Tất cả các thay đổi không liên quan được giữ nguyên vẹn trong working tree (`git status` giữ nguyên tài liệu và mã nguồn liên quan khác).
- Toàn bộ 6 vấn đề yêu cầu đều được giải quyết triệt để, bám sát thiết kế Database-First và quy tắc nghiệp vụ hệ thống.
