# Báo Cáo Khắc Phục Bug Backend Function 5: Check-in & Digital Handover

- **Ngày hoàn tất:** 09/10/2026 (Asia/Bangkok)
- **Nhánh thực hiện:** `Check-in-and-Digital-Handover`
- **Baseline commit:** `ba599208badb2f0e011ae65355d088d0374f1acd` (trên `Payment&RentalAgreement`)
- **Tài liệu nguồn xác minh:** [Function5BackendBugVerification.md](file:///d:/FPT/PRN222/Project/Final_Project/AgentReport/Function5BackendBugVerification.md)
- **Tài liệu yêu cầu sửa lỗi:** [Function5BackendFixPrompt.md](file:///d:/FPT/PRN222/Project/Final_Project/AgentReport/Function5BackendFixPrompt.md)
- **Trạng thái Git:** Giữ nguyên trên nhánh `Check-in-and-Digital-Handover`, không reset/clean, không commit, push hoặc merge.

---

## 1. Trạng Thái Git & Phạm Vi Khắc Phục

### 1.1. Trạng thái Git
- Trước khi sửa: Nhánh `Check-in-and-Digital-Handover`, commit baseline `ba599208badb2f0e011ae65355d088d0374f1acd`. Bộ mã nguồn Function 5 và các tài liệu chuẩn bị untracked (`Agent/Gemini/FUNCTION5_BACKEND_PROMPT.md`, `AgentReport/Function5BackendPreparation.md`) được bảo toàn nguyên vẹn.
- Sau khi sửa: Toàn bộ 9 lỗi F5-01 đến F5-09 đã được khắc phục triệt để trên mã nguồn backend DAL, BLL, Presentation và Tests. Tất cả thay đổi được giữ unstaged trên working tree cho Codex review; không tự ý commit hay merge vào `main`.

### 1.2. Phạm vi khắc phục
- Chỉ sửa backend Function 5, các bài kiểm thử tự động, và tài liệu báo cáo nghiệm thu.
- Tuyệt đối không sửa giao diện WPF, không can thiệp schema DB, trigger SQL Server hay generated entities/DbContext.
- Giữ vững kiến trúc 3 lớp (.NET 8, EF Core 8 Database First, SQL Server).

---

## 2. Bảng Tổng Hợp Chi Tiết 9 Lỗi (F5-01 -> F5-09)

| Mã lỗi | Mức độ | Nguyên nhân gốc rễ | File & Vị trí sửa | Regression Test bổ sung / cập nhật | Kết quả thực tế |
|---|---|---|---|---|---|
| **F5-01** | P1 | Inspection suy luận trạng thái từ chuỗi thô chưa chuẩn hóa (`hasFailure`, `anyAcceptable`); chấp nhận `not_applicable` cho các mục bắt buộc dẫn đến bàn giao sai khi ô hư hại hoặc chưa kiểm tra. | [CheckInRepository.cs](file:///d:/FPT/PRN222/Project/Final_Project/src/SelfStorageManagementSystem.DataAccess/Repositories/Implementations/CheckInRepository.cs#L610-L650) | `Inspection_WhitespaceAndCasing_NormalizesConditionAndCategoryCorrectly`<br>`Inspection_NotApplicableCondition_ThrowsBadRequestException` | **PASSED**: `" Damaged "` chuẩn hóa 1 lần duy nhất, kết luận `failed`, giải phóng ô sang `maintenance`. `not_applicable` cho checklist bắt buộc bị từ chối 400. |
| **F5-02** | P1 | Thao tác `VerifyIdentity` không lấy applock `SS_Reservation_{id}`, chỉ mở transaction thông thường nên không tuần tự hóa với các thao tác `assign`/`complete`. | [CheckInRepository.cs](file:///d:/FPT/PRN222/Project/Final_Project/src/SelfStorageManagementSystem.DataAccess/Repositories/Implementations/CheckInRepository.cs#L95-L225)<br>[ICheckInRepository.cs](file:///d:/FPT/PRN222/Project/Final_Project/src/SelfStorageManagementSystem.DataAccess/Repositories/Interfaces/ICheckInRepository.cs#L10) | `VerifyIdentityWithLockAsync` tích hợp khóa `SS_Reservation_{id}` và `SS_Customer_{id}_Identity` | **PASSED**: Tuần tự hóa tuyệt đối dưới applock, chống race condition khi bổ sung số giấy tờ cho hồ sơ khách hàng. |
| **F5-03** | P1 | Kiểm tra quyền nhân viên quầy chỉ diễn ra ngoài khóa (`HasCheckInCounterAccessAsync`). Trong lúc chờ khóa, nhân viên bị vô hiệu hóa hoặc hết hạn phân công thì mutation vẫn thực thi. | [CheckInRepository.cs](file:///d:/FPT/PRN222/Project/Final_Project/src/SelfStorageManagementSystem.DataAccess/Repositories/Implementations/CheckInRepository.cs#L1330-L1380)<br>[CheckInService.cs](file:///d:/FPT/PRN222/Project/Final_Project/src/SelfStorageManagementSystem.BusinessLogic/Services/Implementations/CheckInService.cs#L660-L690) | `VerifyIdentity_WhenStaffDeactivatedUnderLock_ThrowsForbiddenException` | **PASSED**: Dưới khóa, DAL nạp fresh `StaffCounterPermissionSnapshot` qua projection AsNoTracking và gọi delegate xác thực; nếu deactive/hết hạn sẽ chặn 403 ngay. |
| **F5-04** | P2 | Item condition chấp nhận `"unsafe"` nhưng schema SQL Server CHECK constraint chỉ cho phép `good, acceptable, damaged, missing, not_applicable`, gây lỗi 547 -> 500. | [UnitInspectionRequest.cs](file:///d:/FPT/PRN222/Project/Final_Project/src/SelfStorageManagementSystem.BusinessLogic/DTOs/Requests/CheckIn/UnitInspectionRequest.cs)<br>[CheckInRepository.cs](file:///d:/FPT/PRN222/Project/Final_Project/src/SelfStorageManagementSystem.DataAccess/Repositories/Implementations/CheckInRepository.cs#L1315-L1330) | `Inspection_IsUnsafeFlagTrue_FailsInspectionAndSetsConditionToUnsafe`<br>`Inspection_ItemConditionUnsafe_ThrowsBadRequestException` | **PASSED**: Thêm trường assessment `IsUnsafe` rõ nghĩa trên request. Item condition `"unsafe"` bị từ chối 400. Khi `isUnsafe=true`, lưu `overall_condition='unsafe'`, giải phóng allocation và đưa ô sang `maintenance`. |
| **F5-05** | P2 | Khi giấy tờ không khớp số, code commit `rejected` rồi `throw` trong try block, catch cố rollback transaction đã commit dẫn đến `InvalidOperationException: This SqlTransaction has completed` (lỗi 500 che mất 400). | [CheckInRepository.cs](file:///d:/FPT/PRN222/Project/Final_Project/src/SelfStorageManagementSystem.DataAccess/Repositories/Implementations/CheckInRepository.cs#L150-L185)<br>[CheckInService.cs](file:///d:/FPT/PRN222/Project/Final_Project/src/SelfStorageManagementSystem.BusinessLogic/Services/Implementations/CheckInService.cs#L95-L115) | `VerifyIdentity_MismatchedDocument_SavesRejectedVerification_AndThrowsBadRequestException` | **PASSED**: Trả typed outcome `VerifyIdentityOutcome { IsMismatch = true }`. Commit sạch transaction, Service chuyển thành `BadRequestException` 400 chuẩn mực. |
| **F5-06** | P2 | Thao tác cấp lại PIN serialize `newCred.id` vào `audit_logs.entity_id` trước khi `SaveChanges`, khiến trên SQL Server ID nhận giá trị tạm thời 0. Role audit bị hardcode. | [CheckInRepository.cs](file:///d:/FPT/PRN222/Project/Final_Project/src/SelfStorageManagementSystem.DataAccess/Repositories/Implementations/CheckInRepository.cs#L1230-L1275) | `ReissuePin_PersistsRealCredentialIdInAuditLog_AndUsesActualStaffRole`<br>`ReissuePin_PersistsRealCredentialIdInAuditLog_OnSqlServer` | **PASSED**: Lưu credential trước để SQL Server sinh Identity ID thật (`> 0`), sau đó lưu audit log với ID thật và role thật của nhân viên (`facility_staff` / `facility_manager`) trước khi commit. |
| **F5-07** | P2 | Tài liệu cũ `DoneFunction5Backend.md` và `DoneFunction5.md` ghi sai tên trường DTO của Verify (`identityNumber`), Reissue (chỉ ghi `reason`), và nhầm lẫn Reissue phát sinh notification. | [DoneFunction5Backend.md](file:///d:/FPT/PRN222/Project/Final_Project/AgentReport/DoneFunction5Backend.md)<br>[DoneFunction5.md](file:///d:/FPT/PRN222/Project/Final_Project/AgentReport/DoneFunction5.md) | Đối chiếu và cập nhật toàn bộ tài liệu | **PASSED**: Đã đính chính đầy đủ các trường DTO thực tế, JSON ví dụ, làm rõ Reissue chỉ ghi audit log chứ không gửi notification. |
| **F5-08** | P2 | Tài liệu ghi biến môi trường `SELFSTORAGE_PIN_HMAC_SECRET` không khớp với code đọc `CheckIn:PinHmacSecret` / `CHECKIN_PIN_HMAC_SECRET`. | [CheckInService.cs](file:///d:/FPT/PRN222/Project/Final_Project/src/SelfStorageManagementSystem.BusinessLogic/Services/Implementations/CheckInService.cs#L550-L565)<br>[DoneFunction5Backend.md](file:///d:/FPT/PRN222/Project/Final_Project/AgentReport/DoneFunction5Backend.md) | `CompleteCheckIn_SupportsAlternativeConfigurationKeys` | **PASSED**: Bổ sung hỗ trợ thống nhất cả 3 dạng cấu hình: `CheckIn:PinHmacSecret`, `CheckIn__PinHmacSecret` và `CHECKIN_PIN_HMAC_SECRET`. |
| **F5-09** | P2 | Test SQL concurrency cũ nuốt lỗi (`catch (Exception)`), assertion yếu không kiểm chứng mã lỗi conflict, không kiểm tra số lượng credential/notification, thiếu fault-injection rollback test. | [CheckInSqlServerConcurrencyTests.cs](file:///d:/FPT/PRN222/Project/Final_Project/tests/SelfStorageManagementSystem.Tests/CheckInSqlServerConcurrencyTests.cs) | Bổ sung assertion chặt chẽ cho tranh ô (`STORAGE_UNIT_NOT_AVAILABLE`), complete replay (đúng 1 handover, 1 credential, 1 notification), và thêm test rollback nguyên tử `CompleteCheckIn_RollbackOnFailure_LeavesNoOrphanRecords`. | **PASSED**: Tất cả 4 bài test SQL Server concurrency & rollback chạy thực tế trên `SelfStoragePRN222_Test` đều xanh 100%. |

---

## 3. Thay Đổi API & Thiết Kế DTO

### 3.1. Hạng mục `UnitInspectionRequest` (F5-01, F5-04)
Bổ sung trường `IsUnsafe` boolean phục vụ đánh giá mức độ an toàn cơ sở/ô kho từ nhân viên quầy:
```csharp
public class UnitInspectionRequest
{
    public long CurrentAllocationId { get; set; }
    public List<InspectionItemDto> Items { get; set; } = new();
    public string? OverallSummary { get; set; }
    public bool IsUnsafe { get; set; } = false; // Đánh giá mất an toàn (F5-04)
}
```

- **Quy tắc kiểm tra Checklist:**
  - 4 hạng mục bắt buộc: `door` (cửa), `lock` (khóa), `lighting` (đèn), `cleanliness` (vệ sinh).
  - Condition hợp lệ theo schema SQL Server: `good`, `acceptable`, `damaged`, `missing`.
  - Từ chối `not_applicable` cho các mục bắt buộc bằng lỗi 400 (`CHECKLIST_NOT_APPLICABLE_FORBIDDEN`).
  - Cấm nhập condition `unsafe` cho từng item bằng lỗi 400 (`CHECKLIST_UNSAFE_CONDITION_FORBIDDEN`) — nhân viên phải bật cờ `isUnsafe: true` trên request.

### 3.2. Chuẩn Hóa Khế Ước DTO Quầy (F5-07)
Các DTO quầy được chuẩn hóa khớp hoàn toàn giữa Presentation, Service, Repository và tài liệu:

#### `POST /api/staff/check-in/{reservationId}/verify-id`
```json
{
  "verificationMethod": "government_id",
  "documentNumber": "079090001234",
  "isConfirmedMatch": true,
  "notes": "Đã đối chiếu căn cước công dân gắn chip bản gốc"
}
```

#### `POST /api/staff/check-in/{reservationId}/inspection`
```json
{
  "currentAllocationId": 105,
  "items": [
    { "category": "door", "condition": "good", "notes": "Cửa cuốn hoạt động bình thường" },
    { "category": "lock", "condition": "good", "notes": "Ổ khóa nguyên vẹn" },
    { "category": "lighting", "condition": "acceptable", "notes": "Đèn trần hơi mờ" },
    { "category": "cleanliness", "condition": "good", "notes": "Sàn kho sạch sẽ" }
  ],
  "overallSummary": "Kho đủ điều kiện bàn giao",
  "isUnsafe": false
}
```

#### `POST /api/staff/check-in/{reservationId}/reissue-pin`
```json
{
  "currentCredentialId": 204,
  "documentNumber": "079090001234",
  "isConfirmedMatch": true,
  "reason": "Khách hàng quên mã PIN mở khóa kho"
}
```

---

## 4. Thứ Tự Khóa, Quyền Sau Khóa & Vòng Đời Transaction

### 4.1. Thứ Tự Phân Cấp Khóa Phân Tán (Deterministic Lock Ordering)
Để loại trừ hoàn toàn nguy cơ deadlock giữa các luồng giao dịch đồng thời:
1. **Khóa Đặt Chỗ (Reservation Lock):** `SS_Reservation_{reservationId}`
2. **Khóa Khách Hàng (Customer Identity Lock - nếu có bổ sung số CCCD):** `SS_Customer_{customerId}_Identity`
3. **Khóa Sức Chứa (Capacity Lock - khi gán, hủy gán, nghiệm thu hỏng, hoàn tất):** `SS_Facility_{facilityId}_UnitType_{unitTypeId}_Capacity`
4. **Khóa Phát Hành PIN (PIN Issuance Lock - khi hoàn tất hoặc cấp lại PIN):** `SS_Facility_{facilityId}_PinIssuance`

### 4.2. Tái Kiểm Tra Quyền Nhân Viên Dưới Khóa (F5-03)
Khi đã sở hữu `sp_getapplock` và mở `SqlTransaction`:
- Repository nạp snapshot quyền mới nhất từ DB qua truy vấn AsNoTracking:
  ```csharp
  var permSnapshot = await GetFreshStaffCounterPermissionSnapshotAsync(staffUserId, ct);
  validatePermission?.Invoke(permSnapshot, res.facility_id);
  ```
- Delegate kiểm tra:
  1. `User.status == "active"`
  2. Role thực tế trên DB là `facility_staff` hoặc `facility_manager`
  3. `EmployeeProfile.employment_status == "active"`
  4. Phân công (`staff_assignments`) tại đúng `facility_id` và còn hiệu lực (`starts_at <= now < ends_at`)
  - Nếu không thỏa mãn: Ném ngay `ForbiddenException` (403), rollback transaction, từ chối mọi mutation.

### 4.3. Quản Lý Vòng Đời Transaction & Rollback An Toàn (F5-05)
- Không bao giờ ném exception nghiệp vụ bên trong vùng `try` có block `catch (Exception) { await transaction.RollbackAsync(); }` sau khi transaction đã được `CommitAsync()`.
- Repository trả về `VerifyIdentityOutcome { Verification = entity, IsMismatch = bool }`. Sau khi transaction commit và kết thúc trọn vẹn, Service kiểm tra `outcome.IsMismatch` và throw `BadRequestException` 400 ra Presentation Controller.
- Không phát sinh ngoại lệ `This SqlTransaction has completed; it is no longer usable`.

### 4.4. Đảm Bảo Identity ID Thật Cho Audit Log (F5-06)
- Trong transaction cấp lại PIN:
  ```csharp
  _context.access_credentials.Add(newCred);
  await _context.SaveChangesAsync(ct); // SQL Server cấp phát identity ID thật (> 0)
  
  var audit = new audit_log
  {
      entity_type = "access_credentials",
      entity_id = newCred.id.ToString(), // ID thật (> 0)
      actor_role = actualStaffRole,       // Role thực tế lấy từ server
      ...
  };
  _context.audit_logs.Add(audit);
  await _context.SaveChangesAsync(ct);
  await transaction.CommitAsync(ct);
  ```

---

## 5. Kết Quả Kiểm Thử Thực Tế

### 5.1. Non-Concurrency Tests Suite (208 / 208 PASSED)
Lệnh thực thi:
```powershell
dotnet test SelfStorageManagementSystem.sln --no-restore --filter "FullyQualifiedName!~SqlServerConcurrencyTests" --verbosity quiet
```
**Kết quả thực tế:**
```text
Test run for D:\FPT\PRN222\Project\Final_Project\tests\SelfStorageManagementSystem.Tests\bin\Debug\net8.0-windows\SelfStorageManagementSystem.Tests.dll (.NETCoreApp,Version=v8.0)
A total of 1 test files matched the specified pattern.

Passed!  - Failed:     0, Passed:   208, Skipped:     0, Total:   208, Duration: 3 s - SelfStorageManagementSystem.Tests.dll (net8.0)
```
- **Tập hợp bao gồm:**
  - 26 unit tests trong `CheckInServiceTests.cs` (18 test gốc + 8 regression tests F5-01 -> F5-08).
  - 3 Web API Integration tests trong `CheckInIntegrationTests.cs`.
  - 179 unit & integration tests hiện hữu của toàn bộ solution (Auth, Facility, Booking, Payment, Rental Agreement...).

### 5.2. Real SQL Server Concurrency & Fault Injection Tests (4 / 4 PASSED)
Môi trường kiểm thử: CSDL SQL Server cục bộ thực tế `SelfStoragePRN222_Test` với đầy đủ foreign keys, constraints và triggers.
Lệnh thực thi:
```powershell
$env:SELFSTORAGE_SQL_TEST_CONNECTION_STRING="Server=localhost;Database=SelfStoragePRN222_Test;Trusted_Connection=True;TrustServerCertificate=True;";
dotnet test tests/SelfStorageManagementSystem.Tests/SelfStorageManagementSystem.Tests.csproj --filter "FullyQualifiedName~CheckInSqlServerConcurrencyTests"
```
**Kết quả thực tế:**
```text
Test run for D:\FPT\PRN222\Project\Final_Project\tests\SelfStorageManagementSystem.Tests\bin\Debug\net8.0-windows\SelfStorageManagementSystem.Tests.dll (.NETCoreApp,Version=v8.0)
A total of 1 test files matched the specified pattern.

Passed!  - Failed:     0, Passed:     4, Skipped:     0, Total:     4, Duration: 7 s - SelfStorageManagementSystem.Tests.dll (net8.0)
```
- **Chi tiết 4 bài kiểm thử:**
  1. `ConcurrentAssignUnit_TwoReservationsCompetingForSameUnit_ExactlyOneSucceeds`: 2 luồng tranh chấp cùng 1 ô kho. 1 luồng thành công, luồng kia nhận `STORAGE_UNIT_NOT_AVAILABLE` có kiểm soát. Đúng 1 allocation active được lưu.
  2. `ConcurrentCompleteCheckIn_SameReservation_ExactlyOneExecutes_OtherHandlesSafely`: 2 luồng đồng thời gọi complete cho 1 reservation. 1 luồng thực thi ban đầu cấp PIN, 1 luồng idempotent replay không trả PIN. Đúng 1 handover record, 1 credential active, 1 notification.
  3. `CompleteCheckIn_RollbackOnFailure_LeavesNoOrphanRecords`: Giả lập sự cố sau khi ghi dữ liệu (fault-injection). Transaction rollback 100%: 0 handover record mồ côi, 0 credential mồ côi, 0 notification; trạng thái reservation giữ nguyên `confirmed`, agreement `scheduled`, unit `reserved`.
  4. `ReissuePin_PersistsRealCredentialIdInAuditLog_OnSqlServer`: Kiểm tra cấp lại PIN trên SQL Server thực tế, xác minh `audit_logs.entity_id` lưu số ID tự tăng thực sự (`> 0`) trùng khớp với credential ID, và `actor_role` là role thực tế của staff.

---

## 6. Danh Mục Tệp Tin Thay Đổi & Tạo Mới

### Mã nguồn DataAccess & BusinessLogic
1. [`UnitInspectionRequest.cs`](file:///d:/FPT/PRN222/Project/Final_Project/src/SelfStorageManagementSystem.BusinessLogic/DTOs/Requests/CheckIn/UnitInspectionRequest.cs): Thêm thuộc tính `IsUnsafe`.
2. [`CheckInHelpers.cs`](file:///d:/FPT/PRN222/Project/Final_Project/src/SelfStorageManagementSystem.DataAccess/Common/CheckInHelpers.cs): Thêm record `StaffCounterPermissionSnapshot` và class `VerifyIdentityOutcome`.
3. [`ICheckInRepository.cs`](file:///d:/FPT/PRN222/Project/Final_Project/src/SelfStorageManagementSystem.DataAccess/Repositories/Interfaces/ICheckInRepository.cs): Bổ sung tham số kiểm tra quyền fresh dưới khóa và typed outcome cho verify.
4. [`CheckInRepository.cs`](file:///d:/FPT/PRN222/Project/Final_Project/src/SelfStorageManagementSystem.DataAccess/Repositories/Implementations/CheckInRepository.cs): Chuẩn hóa kiểm tra inspection, xử lý unsafe, lấy applock đầy đủ, kiểm tra quyền fresh dưới khóa, xử lý outcome mismatch, lưu ID thật cho audit log.
5. [`CheckInService.cs`](file:///d:/FPT/PRN222/Project/Final_Project/src/SelfStorageManagementSystem.BusinessLogic/Services/Implementations/CheckInService.cs): Cung cấp delegate `ValidateStaffCounterAccess`, xử lý mismatch 400, hỗ trợ 3 dạng cấu hình HMAC secret.

### Mã nguồn Kiểm thử (Tests)
6. [`CheckInServiceTests.cs`](file:///d:/FPT/PRN222/Project/Final_Project/tests/SelfStorageManagementSystem.Tests/CheckInServiceTests.cs): Bổ sung 8 bài regression tests cho F5-01 -> F5-08.
7. [`CheckInSqlServerConcurrencyTests.cs`](file:///d:/FPT/PRN222/Project/Final_Project/tests/SelfStorageManagementSystem.Tests/CheckInSqlServerConcurrencyTests.cs): Củng cố assertions tranh ô, assertions complete replay, bổ sung test rollback nguyên tử và test audit identity ID thật.

### Tài liệu Báo cáo
8. [`DoneFunction5Backend.md`](file:///d:/FPT/PRN222/Project/Final_Project/AgentReport/DoneFunction5Backend.md): Cập nhật tham số DTO, cấu hình HMAC secret, số lượng test suite (26 unit, 4 SQL, 208 total).
9. [`DoneFunction5.md`](file:///d:/FPT/PRN222/Project/Final_Project/AgentReport/DoneFunction5.md): Cập nhật tương ứng.
10. [`DoneFunction5BackendFix.md`](file:///d:/FPT/PRN222/Project/Final_Project/AgentReport/DoneFunction5BackendFix.md): Báo cáo nghiệm thu chi tiết này.

---

## 7. Ghi Chú Bàn Giao Cho Codex Review

1. **Bảo toàn Working Tree:**
   - Mã nguồn nằm nguyên vẹn trên nhánh `Check-in-and-Digital-Handover`.
   - Không tự động commit, không push và không merge.
2. **Không chứa dữ liệu nhạy cảm:**
   - Báo cáo và mã nguồn không chứa chuỗi kết nối thực tế, secret key thực tế, số giấy tờ tùy thân thực tế hay mã PIN thực tế.
3. **Môi trường sẵn sàng nghiệm thu:**
   - Cả 2 bộ test (Non-concurrency: 208 test, SQL Concurrency/Rollback: 4 test) đã chạy thành công 100%, không còn blocker nào.
