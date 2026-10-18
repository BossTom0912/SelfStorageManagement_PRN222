# Báo Cáo Xử Lý Finding Codex Vòng R4 — Task T15: Tích Hợp Realtime Toàn Web

> **Dự án:** SelfStorageManagementSystem — Chuyển đổi Web ASP.NET Core Razor Pages / SignalR  
> **Workspace làm việc:** `D:\FPT\PRN222\Project\Final_Project_Web`  
> **Workspace đồng bộ:** `D:\FPT\PRN222\Project\Final_Project`  
> **Trạng thái Task T15:** `T15 IMPLEMENTED — chờ Codex review` (KHÔNG tự ý đánh dấu `VERIFIED`)  
> **Ngày thực hiện:** 18/10/2026  
> **Người thực hiện:** Gemini A  

---

## 1. Tóm Tắt Kết Quả Review Vòng R3 & Mục Tiêu Vòng R4

Codex đã thực hiện review độc lập báo cáo `AgentReport/WebMigration_T15_R3_FindingFixes.md` (SHA-256 `443C524D01C8FBE76E6E91858ECDAF39FE2FBF1DD133C856A2B42ED300923868`) và ghi nhận 01 finding duy nhất:

### R3-P1 [P1] Serialize failed IPN transitions with success and duplicate failures
- **Vị trí phát hiện:** `src/SelfStorageManagementSystem.DataAccess/Repositories/Implementations/PaymentRepository.cs:1247–1289`, `src/SelfStorageManagementSystem.BusinessLogic/Services/Implementations/PaymentService.cs:502–522`.
- **Hiện tượng & Nguyên nhân gốc:**
  1. Hai callback decline đồng thời từ gateway dùng hai `DbContext` độc lập cùng đọc `payment.status == pending` trước lock, sau đó cả hai ghi `status = failed` và trả `true`. `PaymentService.ProcessVnpayIpnAsync` đặt `IsStatusTransitionCommitted = true` cho cả hai, làm `PaymentsApiController.VnpayIpn` phát hai sự kiện `PaymentChanged(failed)` trùng lặp.
  2. Nhánh success sử dụng cơ chế serialize khóa qua `sp_getapplock`/`SemaphoreSlim`, nhưng nhánh decline trước đó không có khóa tương đương. Nếu decline đua với success: decline có thể đọc `pending`, trong khi success đã commit `succeeded` và hợp đồng `scheduled/confirmed`, sau đó decline lại ghi đè trạng thái `failed`.
  3. Ở chiều ngược lại (reverse race): nếu callback success tới sau khi một callback decline đã commit `failed`, hệ thống cần có cơ chế bảo vệ nhất quán, ghi nhận tiền theo đối soát (`reconciliation_required` / `PAYMENT_ALREADY_FAILED`), tạo bản ghi hoàn tiền cho nhân viên kiểm tra, và tuyệt đối không tạo hợp đồng thuê hoặc xác nhận reservation trái phép.
  4. Các kiểm thử ở vòng R3 trước đây sử dụng mock `IPaymentService`/`IPaymentRepository`, chưa chứng minh được hành vi đồng thời thực tế qua nhiều `DbContext` và transaction database.

---

## 2. Giải Pháp Triển Khai Chi Tiết

### 2.1. Thứ Tự Khóa Nhất Quán & Đồng Bộ (Dual Lock Hierarchy)
Để loại trừ deadlock và bảo đảm serialization tuyệt đối giữa các giao dịch thanh toán (cả success lẫn decline):
- Cả hai phương thức `ExecuteFinalizePaymentTransactionAsync` và `ExecuteFinalizeFailedPaymentTransactionAsync` đều tuân thủ thứ tự khóa nghiêm ngặt:
  1. Khóa theo Reservation: `SS_Reservation_{reservationId}` (nếu có reservation liên kết)
  2. Khóa theo Payment: `SS_Payment_{parameters.PaymentId}`
- Trên hệ quản trị CSDL quan hệ (SQL Server): sử dụng `sp_getapplock` ở chế độ `Exclusive`, phạm vi `Transaction`, thời gian chờ 10,000ms.
- Trên môi trường In-Memory (EF Core test fixture): sử dụng `ConcurrentDictionary<string, SemaphoreSlim>` với thứ tự acquire và giải phóng an toàn trong khối `try...finally`.

### 2.2. Nạp Lại Thực Thể Từ Database Trong Vùng Khóa Độc Quyền (Atomic Reload Under Lock)
- Không tin cậy dữ liệu đọc trước khi nhận khóa.
- Ngay sau khi vào transaction và sở hữu khóa độc quyền, thực thể `payment` ban đầu được tách khỏi context (`EntityState.Detached`) và nạp lại bản ghi tươi mới nhất từ database (`_context.payments.FirstOrDefaultAsync(...)`).
- Kiểm tra trạng thái cuối (`terminal state check`): Nếu `payment.status` đã ở trạng thái kết thúc (`succeeded`, `failed`, hoặc `cancelled`), phương thức từ chối chuyển trạng thái thêm lần nữa:
  - Trả về `IsTransitionCommitted = false` kèm `CurrentStatus` thực tế.
  - Callback retry hoặc callback thua race không được phép ghi đè trạng thái.

### 2.3. Xử Lý Nhánh Decline Đồng Thời và Callback Retry Sau Terminal State
Trong `PaymentRepository.ExecuteFinalizeFailedPaymentTransactionAsync`:
```csharp
// Terminal status check: If payment already reached a terminal status, do NOT transition!
if (payment.status == "succeeded" || payment.status == "failed" || payment.status == "cancelled")
{
    if (transaction != null) await transaction.CommitAsync(cancellationToken);
    return new FinalizeFailedPaymentResult
    {
        IsTransitionCommitted = false,
        CurrentStatus = payment.status
    };
}

payment.status = "failed";
payment.failure_reason = parameters.FailureReason;
payment.updated_at = parameters.FailedAt;
...
```
Trong `PaymentService.ProcessVnpayIpnAsync`:
- Nếu `failedResult.CurrentStatus == "succeeded"`: Trả về mã ACK `02` (*Order already confirmed*), `IsStatusTransitionCommitted = false`, không phát sự kiện.
- Nếu `failedResult.CurrentStatus == "failed"` và `IsTransitionCommitted == false`: Trả về mã ACK `00` (*Confirm Success*), `IsStatusTransitionCommitted = false`, không phát sự kiện lặp.
- Chỉ khi chính callback đó chuyển trạng thái thành công (`failedResult.IsTransitionCommitted == true`), `IsStatusTransitionCommitted` mới là `true` để `PaymentsApiController` phát đúng 1 sự kiện `PaymentChanged(failed)`.

### 2.4. Xử Lý Ngược Chiều (Reverse Race: Success tới sau khi Decline đã Commit)
Khi ngân hàng trừ tiền của khách nhưng gateway gửi callback success muộn sau khi giao dịch đã bị đánh dấu thất bại (`payment.status == "failed"`):
- Trong `ExecuteFinalizePaymentTransactionAsync`:
  ```csharp
  var isPaymentAlreadyFailedOrCancelled = payment.status == "failed" || payment.status == "cancelled";

  if (isHoldExpiredOrCancelled || isInvoiceVoidedOrMismatch || isPaymentAlreadyFailedOrCancelled)
  {
      var failureReason = isHoldExpiredOrCancelled
          ? "LATE_PAYMENT_HOLD_EXPIRED"
          : (isInvoiceVoidedOrMismatch
              ? (invoice.status == "voided" ? "INVOICE_VOIDED" : "INVOICE_AMOUNT_MISMATCH")
              : "PAYMENT_ALREADY_FAILED");

      // Cổng thanh toán báo tiền vào, nhưng giao dịch đã bị đánh dấu thất bại trước đó!
      // KHÔNG khôi phục reservation!
      // KHÔNG tạo agreement!
      // KHÔNG phân bổ payment vào invoice!
      payment.status = "succeeded";
      payment.paid_at = parameters.PaidAt;
      payment.failure_reason = failureReason;
      
      // Đánh dấu đối soát & tạo yêu cầu hoàn tiền tự động cho nhân viên xử lý
      metaDict["reconciliation_required"] = true;
      ...
  ```
- Kết quả:
  - Database được bảo toàn tính toàn vẹn 100%: không tạo hợp đồng trùng, không đổi trạng thái reservation thành confirmed.
  - Một bản ghi `refund` ở trạng thái `requested` được khởi tạo để kế toán/nhân sự kiểm tra và hoàn tiền.
  - Không phát sự kiện `ReservationChanged(confirmed)`.

---

## 3. Kiểm Thử Nghiệm Thu Thực Tế (Không Dùng Mock Cuối)

Để chứng minh đầy đủ 4 tiêu chí nghiệm thu của Codex review R3, bộ kiểm thử `tests/SelfStorageManagementSystem.WebTests/WebRealtimeIntegrationTests.cs` đã được xây dựng lại với 4 test case gọi trực tiếp `PaymentService` và `PaymentRepository` qua nhiều `DbContext` độc lập:

1. **`R4_Concurrency_TwoSimultaneousDeclines_CommitOnce_AndEmitExactlyOneRealtimeEvent`**:
   - Khởi tạo 1 payment ở trạng thái `pending`.
   - Kết nối SignalR client của khách hàng lắng nghe sự kiện `PaymentChanged`.
   - Chạy 2 tác vụ IPN decline đồng thời trên 2 DI scope và 2 `DbContext` riêng biệt.
   - **Kết quả:** Cả 2 đều nhận ACK `00` (*Confirm Success*); trạng thái DB chỉ chuyển một lần sang `failed`; Client nhận đúng **duy nhất 1 sự kiện** `PaymentChanged(failed)`.
   - **Trạng thái:** **PASSED**.

2. **`R4_Concurrency_SuccessRacingDecline_MaintainsConsistentDatabaseState_AndZeroFalseEvents`**:
   - Chạy 1 tác vụ Success IPN và 1 tác vụ Decline IPN đồng thời trên cùng một payment `pending`.
   - **Kết quả:** Trạng thái cơ sở dữ liệu cuối cùng nhất quán hoàn toàn: Reservation `confirmed`, Invoice `paid`, Rental Agreement `scheduled`, Payment `succeeded`. Nhánh decline bị chặn bởi lock và kiểm tra terminal state, không ghi đè `failed`. Client không nhận bất kỳ sự kiện `PaymentChanged(failed)` giả mạo nào.
   - **Trạng thái:** **PASSED**.

3. **`R4_Concurrency_CallbacksAfterTerminalState_EmitZeroEvents_AndReturnValidAck`**:
   - Payment đã hoàn tất `succeeded`.
   - Gửi lại callback Success: Nhận ACK `02` (*Order already confirmed*), `IsStatusTransitionCommitted = false`.
   - Gửi tiếp callback Decline: Nhận ACK `02` (*Order already confirmed*), `IsStatusTransitionCommitted = false`.
   - **Kết quả:** Phát ra đúng **0 sự kiện realtime**.
   - **Trạng thái:** **PASSED**.

4. **`R4_Concurrency_ReverseRace_SuccessAfterDeclineCommitted_RequiresReconciliation_NeverConfirmsReservation`**:
   - Bước 1: Decline callback đến trước và commit `failed` trong DB; phát 1 sự kiện `PaymentChanged(failed)`.
   - Bước 2: Success callback đến sau cho cùng giao dịch này.
   - **Kết quả:** Payment chuyển sang `succeeded` kèm cờ `reconciliation_required=true` và lý do `PAYMENT_ALREADY_FAILED`; 1 bản ghi `refund` được tạo ở trạng thái `requested`; Reservation giữ nguyên trạng thái không confirmed; Hợp đồng thuê (`rental_agreement`) tuyệt đối là `null`; Realtime không phát `ReservationChanged(confirmed)`.
   - **Trạng thái:** **PASSED**.

---

## 4. Tổng Hợp Bằng Chứng Chạy Test & Kiểm Tra Cú Pháp

| Bộ Kiểm Thử | Lệnh Thực Thi | Kết Quả | Ghi Chú |
| :--- | :--- | :---: | :--- |
| **4 bài test R4 Concurrency** | `dotnet test tests/SelfStorageManagementSystem.WebTests/SelfStorageManagementSystem.WebTests.csproj --filter "FullyQualifiedName~R4_Concurrency"` | **4 / 4 PASSED** (100%) | Thời gian thực thi 21 giây, kiểm thử đồng thời đa DbContext |
| **Kiểm thử kiến trúc Clean Architecture** | `dotnet test tests/SelfStorageManagementSystem.WebTests/SelfStorageManagementSystem.WebTests.csproj --filter "FullyQualifiedName~Architecture_MvcWebApp_DoesNotReferenceDataAccessOrApiOrWpf\|FullyQualifiedName~MvcWebApp_DoesNotReferenceDataAccessOrApiOrWpf"` | **2 / 2 PASSED** (100%) | Ranh giới Web → BLL → DAL hoàn toàn bảo toàn |
| **Toàn bộ WebTests Suite** | `dotnet test tests/SelfStorageManagementSystem.WebTests/SelfStorageManagementSystem.WebTests.csproj` | **401 / 401 PASSED** (100%) | Không có bất kỳ hồi quy nào trên toàn bộ 401 tests |
| **BLL / DAL Regression Tests** | `dotnet test tests/SelfStorageManagementSystem.Tests/SelfStorageManagementSystem.Tests.csproj` | **225 PASSED, 8 SKIP, 0 FAIL** | 225/225 tests khả dụng pass 100% |
| **JavaScript Syntax Check** | `node --check` trên cả 10 tệp wwwroot/js | **10 / 10 PASSED** (100%) | Exit code 0, không có lỗi cú pháp |
| **Kiểm tra đồng bộ trên `Final_Project`** | Chạy 4 test R4 + 2 test Architecture + 208 BLL/DAL tests | **TẤT CẢ PASSED** (100%) | Đồng bộ trọn vẹn, chạy sạch |

---

## 5. Bảng Mã Băm SHA-256 Các Tệp Đã Chỉnh Sửa Trong Vòng R4

Tất cả các tệp sửa đổi đều được kiểm tra mã băm SHA-256 và đồng bộ sang thư mục `D:\FPT\PRN222\Project\Final_Project`:

| STT | Đường Dẫn Tệp | SHA-256 Hash |
| :---: | :--- | :--- |
| 1 | `src/SelfStorageManagementSystem.DataAccess/Repositories/Interfaces/IPaymentRepository.cs` | `E277823C01678444F3E03CD38A84ADB9D458359FFEA3038654B3E6B6FA76323A` |
| 2 | `src/SelfStorageManagementSystem.DataAccess/Repositories/Implementations/PaymentRepository.cs` | `20E0EEEEEF5A70DCB88A9E835159F2494A1476C8CDDDC17D2EB51A5A374D2BB5` |
| 3 | `src/SelfStorageManagementSystem.BusinessLogic/Services/Implementations/PaymentService.cs` | `D09080317B784816CAFCD189B269D2C22A9C0CEACAE51319975BAD0C6115AB3F` |
| 4 | `tests/SelfStorageManagementSystem.WebTests/Fixtures/PaymentWebTestFixture.cs` | `B6D157C184D3A327A68BC7886BF38B9B3BFCA814DB78B72B4846FB70D24664AF` |
| 5 | `tests/SelfStorageManagementSystem.WebTests/WebRealtimeIntegrationTests.cs` | `844F9C1380A2A015FD6D9CADD0826EE6DB662500979CFA7AF50DFB764FFE5D91` |

---

## 6. Cam Kết Hợp Đồng & Kế Hoạch Tiếp Theo

1. **Bảo toàn các Task T12, T13, T14:**
   - Route `/storageUnitHub`, 3 Hub methods (`HubCreate_StorageUnit`, `HubUpdate_StorageUnit`, `HubDelete_StorageUnit`) và 3 Receiver CRUD events (`Receiver_CreateStorageUnit`, `Receiver_UpdateStorageUnit`, `Receiver_DeleteStorageUnit`) giữ nguyên vẹn 100%.
   - Không đưa PIN, entity EF Core, hay dữ liệu thanh toán nhạy cảm vào payload sự kiện.
2. **Tuân thủ quy trình kiểm thử và chuyển giao:**
   - Tuyệt đối KHÔNG tự ý khởi động các task T16, T17, T18.
   - Giữ trạng thái TaskBoard là `T15 IMPLEMENTED — chờ Codex review`.
   - Bàn giao báo cáo và mã nguồn đã đồng bộ để Codex tiến hành re-review độc lập.
