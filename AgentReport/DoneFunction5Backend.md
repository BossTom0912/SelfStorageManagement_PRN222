# Báo Cáo Triển Khai Backend Function 5: Check-in & Digital Handover

## 1. Thông Tin Nhánh, Commit Baseline Và Trạng Thái Git

- **Mã nguồn dự án:** `D:\FPT\PRN222\Project\Final_Project`
- **Nhánh thực hiện:** `Check-in-and-Digital-Handover`
- **Baseline commit:** `ba599208badb2f0e011ae65355d088d0374f1acd` (trên nhánh `Payment&RentalAgreement`)
- **Trạng thái Git:**
  - Không xóa/di chuyển file vật lý, không `reset --hard`, không clean, không tự động commit, push hay merge.
  - Các file tài liệu chuẩn bị từ trước được bảo toàn nguyên vẹn:
    - [FUNCTION5_BACKEND_PROMPT.md](file:///d:/FPT/PRN222/Project/Final_Project/Agent/Gemini/FUNCTION5_BACKEND_PROMPT.md)
    - [Function5BackendPreparation.md](file:///d:/FPT/PRN222/Project/Final_Project/AgentReport/Function5BackendPreparation.md)
  - Tuân thủ nghiêm ngặt mô hình 3 lớp (.NET 8, ASP.NET Core Web API, EF Core 8 Database First, SQL Server). Không sửa WPF, không thêm package dư thừa.

---

## 2. Danh Sách Endpoint Triển Khai

Tất cả các endpoint tuân thủ quy ước `ApiResponse<T>`, status codes `200`, `400`, `401`, `403`, `404`, `409`, `500`. Toàn bộ thông tin định danh nhân viên (`staffUserId`, `roles`, `facilityScope`) và khách hàng (`customerUserId`) được trích xuất an toàn từ ClaimsPrincipal trên server (`User.FindFirstValue(ClaimTypes.NameIdentifier)`), không bao giờ nhận qua request body hay query.

### 2.1. Nhóm API Quầy (Staff Operations) - `CheckInController`
Được bảo vệ bằng `[Authorize(Roles = "facility_staff,facility_manager")]` kết hợp kiểm tra phân công quầy nghiêm ngặt qua `IFacilityScopeService.HasCheckInCounterAccessAsync` (yêu cầu tài khoản active, `employee_profile` active, phân công cơ sở đang có hiệu lực; không dùng nhánh bypass Admin/Operations Manager).

| Phương thức | Route | Request DTO / Query | Response DTO | Mô tả & Status Codes |
|---|---|---|---|---|
| `GET` | `/api/staff/check-in/search?code={code}` | Query: `code` (mã đặt chỗ hoặc số hợp đồng) | `CheckInSearchResponse` | Tra cứu hồ sơ check-in: thông tin khách (che số giấy tờ), cơ sở, loại kho, kỳ thuê, tiến độ (verified, allocated, inspected, completed) và `canProceedToCheckIn` kèm lý do. `200`, `400`, `401`, `403`, `404`. |
| `GET` | `/api/staff/check-in/{reservationId}/eligible-units` | Route: `reservationId` | `List<EligibleUnitResponse>` | Liệt kê các ô kho ứng viên cùng cơ sở, cùng loại kho, `is_listed=true`, `physical_status='available'`, không có maintenance blocks_booking, không có allocation active trùng kỳ thuê. `200`, `400`, `401`, `403`, `404`. |
| `POST` | `/api/staff/check-in/{reservationId}/verify-id` | `VerifyIdentityRequest` (`verificationMethod`, `documentNumber`, `isConfirmedMatch`, `notes`) | `IdentityVerificationResponse` | Xác minh giấy tờ tuỳ thân (hỗ trợ `government_id`, `passport`). Nếu hồ sơ có sẵn số thì đối chiếu khớp; nếu chưa có thì cập nhật số vào hồ sơ khi `result='verified'`. Ghi vào `core.identity_verifications`. `200`, `400`, `401`, `403`, `404`, `409`. |
| `POST` | `/api/staff/check-in/{reservationId}/assign-unit` | `AssignUnitRequest` (`storageUnitId`) | `UnitAllocationResponse` | Gán ô lưu trữ vật lý cho hợp đồng dưới khóa phân tán. Chuyển ô `available` → `reserved`, tạo `unit_allocation` loại `rental`, trạng thái `active`. Idempotent nếu chọn lại cùng ô; xung đột `409` nếu chọn ô khác khi chưa release. `200`, `400`, `401`, `403`, `404`, `409`. |
| `POST` | `/api/staff/check-in/{reservationId}/release-unit` | `ReleaseUnitRequest` (`currentAllocationId`, `reason`) | `UnitAllocationResponse` | Giải phóng ô kho trước bàn giao dưới khóa phân tán. Kết thúc allocation hiện tại, hoàn trả trạng thái ô `reserved` → `available`. `200`, `400`, `401`, `403`, `404`, `409`. |
| `POST` | `/api/staff/check-in/{reservationId}/inspection` | `UnitInspectionRequest` (`currentAllocationId`, `items`, `overallSummary`, `isUnsafe`) | `InspectionResponse` | Thực hiện kiểm tra bàn giao ô kho (checklist bắt buộc gồm 4 hạng mục: `cửa`, `khóa`, `đèn`, `vệ sinh`). Đánh giá server: hoàn thành hoặc thất bại (nếu có mục hư hại hoặc `isUnsafe=true` thì tự giải phóng allocation và đưa ô sang `maintenance`). Item condition chỉ nhận `good`, `acceptable`, `damaged`, `missing` (từ chối `not_applicable` cho mục bắt buộc và cấm item condition `unsafe`). `200`, `400`, `401`, `403`, `404`, `409`. |
| `POST` | `/api/staff/check-in/{reservationId}/complete` | `CompleteCheckInRequest` (`currentAllocationId`, `currentInspectionId`, `customerAcknowledged`, `notes`) | `HandoverCompleteResponse` | Hoàn tất check-in & bàn giao điện tử trong một transaction: ghi `handover_records`, chuyển hợp đồng `scheduled` → `active`, reservation `confirmed` → `checked_in`, ô `reserved` → `occupied`, phát sinh mã PIN số 6 chữ số và tạo thông báo `in_app`. Response trả PIN một lần duy nhất kèm header `Cache-Control: no-store`. Idempotent replay an toàn nếu gọi lại cùng dữ liệu. `200`, `400`, `401`, `403`, `404`, `409`. |
| `POST` | `/api/staff/check-in/{reservationId}/reissue-pin` | `ReissuePinRequest` (`currentCredentialId`, `documentNumber`, `isConfirmedMatch`, `reason`) | `PinReissueResponse` | Cấp lại mã PIN khi khách quên/thay đổi: thu hồi credential cũ (`revoked`), phát hành credential active mới, lưu audit log an toàn (không sinh notification in_app theo scope). Response trả mã PIN mới kèm header `Cache-Control: no-store`. `200`, `400`, `401`, `403`, `404`, `409`. |

### 2.2. Nhóm API Khách Hàng (Customer Operations) - `MeController`
Được bảo vệ bằng `[Authorize(Roles = "customer")]`. Toàn bộ dữ liệu được cô lập chặt chẽ theo `customerUserId` từ token.

| Phương thức | Route | Request DTO / Query | Response DTO | Mô tả & Status Codes |
|---|---|---|---|---|
| `GET` | `/api/me/units` | Query: `pageNumber`, `pageSize` | `PagedResult<CustomerUnitListItemResponse>` | Danh sách các ô kho của khách hàng đăng nhập kèm trạng thái hợp đồng, ngày bắt đầu/kết thúc, kỳ hạn credential và thông tin cơ sở. `200`, `401`, `403`. |
| `GET` | `/api/me/units/{agreementId}/handover` | Route: `agreementId` | `CustomerHandoverResponse` | Chi tiết biên bản bàn giao điện tử của hợp đồng (ngày bàn giao, loại handover, điều kiện kiểm tra, thông tin ô kho, credential ID và thời hạn, tuyệt đối không lộ PIN/secret_digest). `200`, `401`, `403`, `404`. |
| `GET` | `/api/me/notifications` | Query: `pageNumber`, `pageSize` | `PagedResult<CustomerNotificationResponse>` | Danh sách thông báo nội bộ kênh `in_app` gửi tới khách hàng, sắp xếp theo thời gian mới nhất. `200`, `401`, `403`. |

---

## 3. Thiết Kế Khóa Phân Tán, Transaction Và Giải Quyết Race Condition

### 3.1. Cơ chế Khóa Phân Tán với `sp_getapplock`
Hệ thống sử dụng thủ tục lưu trữ tiêu chuẩn của SQL Server `sys.sp_getapplock` với `LockOwner = 'Transaction'`, mức cô lập `ReadCommitted`, cùng cơ chế dự phòng an toàn bằng `ConcurrentDictionary<string, SemaphoreSlim>` trong môi trường In-Memory test.

### 3.2. Thứ tự Lấy Khóa Nhất Quán (Deterministic Lock Ordering) Tránh Deadlock
Mọi thao tác sửa đổi dữ liệu (mutation) đều tuân thủ nghiêm ngặt thứ tự phân cấp khóa:
1. **Khóa Đặt Chỗ (Reservation Lock):**  
   Key: `SS_Reservation_{reservationId}`  
   Mục đích: Tuần tự hóa mọi thao tác trên cùng một đơn đặt chỗ / hợp đồng thuê (tránh xác minh đồng thời, gán đồng thời hoặc complete đồng thời).
2. **Khóa Sức Chứa (Capacity Lock):**  
   Key: `SS_Facility_{facilityId}_UnitType_{unitTypeId}_Capacity`  
   Mục đích: Tuần tự hóa việc gán/hủy gán/hoàn tất và tính toán tồn kho của từng loại ô kho tại mỗi cơ sở, tránh overselling hoặc xung đột allocation.
3. **Khóa Phát Hành PIN (PIN Issuance Lock):**  
   Key: `SS_Facility_{facilityId}_PinIssuance`  
   Mục đích: Tuần tự hóa việc sinh mã PIN và kiểm tra trùng lặp digest trên toàn bộ cơ sở tại thời điểm hoàn tất bàn giao hoặc cấp lại PIN.

### 3.3. Tái Thẩm Định Trạng Thái Dưới Khóa (Re-read Under Lock)
Ngay sau khi lấy được khóa bên trong transaction, service/repository đọc lại toàn bộ trạng thái thực tế từ cơ sở dữ liệu (`reservations`, `rental_agreements`, `unit_allocations`, `storage_units`, `invoices`, `identity_verifications`, `inspections`), không bao giờ tin tưởng mù quáng vào pre-check ngoài khóa:
- Đảm bảo `today` tại múi giờ cơ sở thỏa `start_date <= today < end_date`.
- Đảm bảo hóa đơn chu kỳ đầu tiên `paid` và `paid_amount == total_amount`.
- Đảm bảo kết quả xác minh mới nhất là `verified`.
- Đảm bảo ô kho đang ở trạng thái phù hợp (`available` khi gán; `reserved` khi complete).
- Đảm bảo inspection thuộc đúng allocation hiện tại, đạt chuẩn `good` hoặc `acceptable`, và được thực hiện sau thời điểm tạo allocation.

---

## 4. An Toàn Dữ Liệu, HMAC-SHA256, Che Giấy Tờ Và Bảo Vệ PIN

- **Sinh mã PIN ngẫu nhiên bảo mật:**  
  Sử dụng `RandomNumberGenerator.GetInt32(0, 1000000).ToString("D6")`, đảm bảo luôn là chuỗi 6 chữ số (giữ nguyên các số 0 ở đầu nếu có).
- **Mã hóa một chiều Secret Digest:**  
  PIN được băm một chiều bằng **HMAC-SHA256** với khóa bí mật cấu hình từ `CheckIn:PinHmacSecret` (hoặc biến môi trường `CheckIn__PinHmacSecret` / `CHECKIN_PIN_HMAC_SECRET`). Hệ thống thực hiện kiểm tra fail-fast: nếu cấu hình thiếu khóa bí mật, service sẽ từ chối hoạt động ngay lập tức.
- **Nguyên tắc bảo vệ PIN:**  
  - Tuyệt đối không lưu plaintext PIN trong database, không lưu trong audit log, không ghi vào logger console.
  - Plaintext PIN chỉ được trả về **duy nhất một lần** cho client tại response của endpoint `complete` và `reissue-pin`.
  - Header HTTP `Cache-Control: no-store` và `Pragma: no-cache` được áp dụng cho mọi phản hồi trả mã PIN nhằm ngăn chặn browser/proxy caching.
  - Khi gọi lặp lại (idempotent replay) của complete, biên bản được trả về với `isAlreadyCompleted = true` và `pin = null`.
- **Bảo mật giấy tờ tùy thân:**  
  - Số giấy tờ khách hàng được che (masking) theo chuẩn: chỉ hiển thị 3 ký tự đầu và 3 ký tự cuối (ví dụ `079*****1234`).
  - Tuyệt đối không ghi số giấy tờ hoặc dữ liệu nhạy cảm vào `notes`, audit payload hay log ứng dụng.

---

## 5. Tương Tác Trigger, Database Schema Và Tính Toán Sức Chứa

### 5.1. Tương tác Trigger & Database First
Hệ thống tôn trọng 100% schema và trigger của SQL Server sạch (`SelfStoragePRN222_SQLServer_CleanInstall.sql`):
- **Trạng thái ô kho:** Chuyển đổi trạng thái ô kho vật lý qua các bước:  
  `available` → `reserved` (khi gán ô) → `occupied` (khi hoàn tất bàn giao) hoặc `maintenance` (khi kiểm tra bàn giao thất bại).
- **Trigger `trg_storage_units_status_history`:** Trigger tự động ghi nhận lịch sử thay đổi trạng thái vào `core.unit_status_history`. Repository không tự insert thủ công vào bảng này để tránh trùng lặp bản ghi.
- **Trigger `trg_unit_allocations_validate_bookable`:** Trigger cấm tạo allocation active trên các ô đã ở trạng thái `occupied`. Do đó, allocation rental active được tạo và giữ nguyên từ bước `assign-unit` (khi ô đang `available` / `reserved`) và tiếp tục duy trì trạng thái `active` sau khi complete để bảo vệ toàn bộ thời hạn thuê mà không vi phạm trigger.
- **Trigger `trg_handover_records_validate_business_rules`:** Kiểm tra tính toàn vẹn của biên bản bàn giao: hợp đồng, allocation, inspection và storage unit phải tương thích tuyệt đối.

### 5.2. Sửa Lỗi Tính Toán Sức Chứa (Capacity Query Fix)
Trước đây, các truy vấn kiểm tra capacity (`FacilityCatalogRepository` và `ReservationRepository`) đếm tất cả các reservation `confirmed` mà chưa tính đến việc reservation đó đã có agreement được gán ô vật lý (`unit_allocations.status = 'active'`).  
Đã sửa logic truy vấn tại:
- [FacilityCatalogRepository.cs](file:///d:/FPT/PRN222/Project/Final_Project/src/SelfStorageManagementSystem.DataAccess/Repositories/Implementations/FacilityCatalogRepository.cs#L85-L105)
- [ReservationRepository.cs](file:///d:/FPT/PRN222/Project/Final_Project/src/SelfStorageManagementSystem.DataAccess/Repositories/Implementations/ReservationRepository.cs#L225-L245) và [ReservationRepository.cs](file:///d:/FPT/PRN222/Project/Final_Project/src/SelfStorageManagementSystem.DataAccess/Repositories/Implementations/ReservationRepository.cs#L745-L770)
Nhờ đó, khi một reservation đã được gán ô cụ thể qua agreement, nó không còn bị tính đúp là một đơn đặt chỗ chưa gán chiếm chỗ ảo trong quỹ sức chứa.

---

## 6. Kết Quả Kiểm Thử Toàn Diện (Test Results)

Hệ thống đã được kiểm thử toàn diện qua 3 tầng kiểm thử độc lập (bao gồm toàn bộ regression tests sau khi fix các findings F5-01 -> F5-09):

### 6.1. Unit Tests (`CheckInServiceTests.cs`)
- **Số lượng test:** 26 bài test (18 test ban đầu + 8 regression/fix tests).
- **Kết quả:** **26 / 26 PASSED** (100%).
- **Phạm vi kiểm thử:**
  1. `Search_ValidReservation_ReturnsExpectedCheckInDetails`
  2. `Search_NonExistent_ThrowsNotFound`
  3. `GetEligibleUnits_NoScope_ThrowsForbidden`
  4. `GetEligibleUnits_ValidScope_ReturnsMatchingUnits`
  5. `VerifyIdentity_CustomerAlreadyHasId_MismatchThrowsConflict`
  6. `VerifyIdentity_CustomerNoId_PopulatesProfileOnVerified`
  7. `AssignUnit_PreCheckFails_ThrowsInvalidOperation`
  8. `AssignUnit_ValidUnit_SetsUnitReservedAndCreatesActiveAllocation`
  9. `ReleaseUnit_ActiveAllocation_ReleasesAndSetsUnitAvailable`
  10. `Inspection_AllGood_CompletesInspection`
  11. `Inspection_Damaged_FailsInspectionAndReleasesUnitToMaintenance`
  12. `CompleteCheckIn_CustomerNotAcknowledged_ThrowsBadRequest`
  13. `CompleteCheckIn_InspectionFailed_ThrowsInvalidOperation`
  14. `CompleteCheckIn_ValidData_GeneratesPinAndCompletesHandover`
  15. `CompleteCheckIn_IdempotentReplay_ReturnsExistingHandoverWithoutPin`
  16. `ReissuePin_ActiveAgreement_IssuesNewPinAndRevokesOld`
  17. `CustomerUnits_ReturnsOnlyCustomerUnits`
  18. `CustomerNotifications_ReturnsOnlyInAppNotifications`
  19. `Inspection_WhitespaceAndCasing_NormalizesConditionAndCategoryCorrectly` (F5-01)
  20. `Inspection_NotApplicableCondition_ThrowsBadRequestException` (F5-01)
  21. `Inspection_IsUnsafeFlagTrue_FailsInspectionAndSetsConditionToUnsafe` (F5-04)
  22. `Inspection_ItemConditionUnsafe_ThrowsBadRequestException` (F5-04)
  23. `VerifyIdentity_MismatchedDocument_SavesRejectedVerification_AndThrowsBadRequestException` (F5-02, F5-05)
  24. `VerifyIdentity_WhenStaffDeactivatedUnderLock_ThrowsForbiddenException` (F5-03)
  25. `ReissuePin_PersistsRealCredentialIdInAuditLog_AndUsesActualStaffRole` (F5-06)
  26. `CompleteCheckIn_SupportsAlternativeConfigurationKeys` (F5-08)

### 6.2. Web API Integration Tests (`CheckInIntegrationTests.cs`)
- **Số lượng test:** 3 kịch bản tích hợp Web API end-to-end (sử dụng `CustomWebApplicationFactory` với role claims và authentication JWT).
- **Kết quả:** **3 / 3 PASSED** (100%).
- **Phạm vi kiểm thử:**
  1. `StaffCheckIn_FullLifecycle_FromSearchToComplete_SucceedsWithNoStoreHeader`: Kiểm thử toàn bộ vòng đời check-in qua HTTP: Search -> Verify ID -> Eligible Units -> Assign Unit -> Perform Inspection -> Complete Check-in. Xác minh mã PIN trả về, header `Cache-Control: no-store`, và trạng thái hợp đồng chuyển thành `active`.
  2. `CustomerEndpoints_ReturnsOnlyOwnedResources`: Xác minh customer chỉ xem được ô kho, biên bản handover và thông báo của chính mình; từ chối truy cập tài nguyên của khách hàng khác.
  3. `ReissuePin_GeneratesNewPinAndRevokesOld`: Xác minh endpoint cấp lại PIN, kiểm tra credential cũ bị chuyển sang `revoked` và credential mới `active`.

### 6.3. Real SQL Server Concurrency & Rollback Tests (`CheckInSqlServerConcurrencyTests.cs`)
- **Môi trường:** Cơ sở dữ liệu SQL Server cục bộ thực tế `SelfStoragePRN222_Test` với đầy đủ ràng buộc khóa ngoại, unique constraints và triggers.
- **Số lượng test:** 4 bài test concurrency & fault injection rollback.
- **Kết quả:** **4 / 4 PASSED** (100%).
- **Phạm vi kiểm thử:**
  1. `ConcurrentAssignUnit_TwoReservationsCompetingForSameUnit_ExactlyOneSucceeds`: Hai nhân viên đồng thời cố gắng gán cùng một ô lưu trữ cho hai mã đặt chỗ khác nhau qua rào cản luồng `Barrier(2)`. Dưới khóa `sp_getapplock` phân tán, **chính xác 1 luồng thành công** gán ô và chuyển ô thành `reserved`, còn luồng kia thất bại an toàn với lỗi xung đột kiểm soát được (`STORAGE_UNIT_NOT_AVAILABLE`). Cơ sở dữ liệu duy trì tính toàn vẹn với đúng 1 allocation active.
  2. `ConcurrentCompleteCheckIn_SameReservation_ExactlyOneExecutes_OtherHandlesSafely`: Hai nhân viên đồng thời gửi yêu cầu hoàn tất bàn giao cho cùng một đơn đặt chỗ qua `Barrier(2)`. Dưới khóa `sp_getapplock`, **chính xác 1 luồng thực thi tạo biên bản bàn giao và cấp PIN**, luồng còn lại được xử lý idempotent an toàn (`IsAlreadyCompleted = true`), không nhân đôi biên bản bàn giao, đúng 1 active credential, đúng 1 notification.
  3. `CompleteCheckIn_RollbackOnFailure_LeavesNoOrphanRecords`: Kiểm thử rollback nguyên tử trên SQL Server thực tế (fault-injection). Nếu quá trình hoàn tất bàn giao gặp sự cố sau khi đã cập nhật một số bảng nhưng trước khi commit transaction, toàn bộ thay đổi bị rollback 100%: 0 handover record, 0 credential mới, 0 notification; trạng thái reservation giữ nguyên `confirmed`, agreement giữ nguyên `scheduled`, unit giữ nguyên `reserved`.
  4. `ReissuePin_PersistsRealCredentialIdInAuditLog_OnSqlServer`: Kiểm thử cấp lại PIN trên SQL Server thực tế xác minh `audit_logs.entity_id` nhận giá trị ID tự tăng thật (`> 0`) của `access_credentials` sau `SaveChangesAsync`, `actor_role` ghi nhận role thực tế của staff (`facility_staff` / `facility_manager`), không serialize plaintext PIN hay HMAC key.

### 6.4. Hồi Quy Toàn Hệ Thống (Solution Regression Test Run)
- Chạy toàn bộ test suite non-concurrency của giải pháp: **208 / 208 unit và integration tests PASSED 100%**.

---

## 7. Danh Mục File Đã Triển Khai & Chỉnh Sửa

### DataAccess Layer
- [`ICheckInRepository.cs`](file:///d:/FPT/PRN222/Project/Final_Project/src/SelfStorageManagementSystem.DataAccess/Repositories/Interfaces/ICheckInRepository.cs)
- [`CheckInRepository.cs`](file:///d:/FPT/PRN222/Project/Final_Project/src/SelfStorageManagementSystem.DataAccess/Repositories/Implementations/CheckInRepository.cs)
- [`CheckInHelpers.cs`](file:///d:/FPT/PRN222/Project/Final_Project/src/SelfStorageManagementSystem.DataAccess/Common/CheckInHelpers.cs)
- [`ServiceCollectionExtensions.cs`](file:///d:/FPT/PRN222/Project/Final_Project/src/SelfStorageManagementSystem.DataAccess/DependencyInjection/ServiceCollectionExtensions.cs) (đăng ký `ICheckInRepository`)
- [`FacilityCatalogRepository.cs`](file:///d:/FPT/PRN222/Project/Final_Project/src/SelfStorageManagementSystem.DataAccess/Repositories/Implementations/FacilityCatalogRepository.cs) (sửa capacity query)
- [`ReservationRepository.cs`](file:///d:/FPT/PRN222/Project/Final_Project/src/SelfStorageManagementSystem.DataAccess/Repositories/Implementations/ReservationRepository.cs) (sửa capacity query)
- [`PaymentRepository.cs`](file:///d:/FPT/PRN222/Project/Final_Project/src/SelfStorageManagementSystem.DataAccess/Repositories/Implementations/PaymentRepository.cs) (đảm bảo include agreement invoices)

### BusinessLogic Layer
- DTO Requests: [`VerifyIdentityRequest.cs`](file:///d:/FPT/PRN222/Project/Final_Project/src/SelfStorageManagementSystem.BusinessLogic/DTOs/Requests/CheckIn/VerifyIdentityRequest.cs), [`AssignUnitRequest.cs`](file:///d:/FPT/PRN222/Project/Final_Project/src/SelfStorageManagementSystem.BusinessLogic/DTOs/Requests/CheckIn/AssignUnitRequest.cs), [`ReleaseUnitRequest.cs`](file:///d:/FPT/PRN222/Project/Final_Project/src/SelfStorageManagementSystem.BusinessLogic/DTOs/Requests/CheckIn/ReleaseUnitRequest.cs), [`UnitInspectionRequest.cs`](file:///d:/FPT/PRN222/Project/Final_Project/src/SelfStorageManagementSystem.BusinessLogic/DTOs/Requests/CheckIn/UnitInspectionRequest.cs), [`CompleteCheckInRequest.cs`](file:///d:/FPT/PRN222/Project/Final_Project/src/SelfStorageManagementSystem.BusinessLogic/DTOs/Requests/CheckIn/CompleteCheckInRequest.cs), [`ReissuePinRequest.cs`](file:///d:/FPT/PRN222/Project/Final_Project/src/SelfStorageManagementSystem.BusinessLogic/DTOs/Requests/CheckIn/ReissuePinRequest.cs)
- DTO Responses: [`CheckInSearchResponse.cs`](file:///d:/FPT/PRN222/Project/Final_Project/src/SelfStorageManagementSystem.BusinessLogic/DTOs/Responses/CheckIn/CheckInSearchResponse.cs), [`EligibleUnitResponse.cs`](file:///d:/FPT/PRN222/Project/Final_Project/src/SelfStorageManagementSystem.BusinessLogic/DTOs/Responses/CheckIn/EligibleUnitResponse.cs), [`IdentityVerificationResponse.cs`](file:///d:/FPT/PRN222/Project/Final_Project/src/SelfStorageManagementSystem.BusinessLogic/DTOs/Responses/CheckIn/IdentityVerificationResponse.cs), [`UnitAllocationResponse.cs`](file:///d:/FPT/PRN222/Project/Final_Project/src/SelfStorageManagementSystem.BusinessLogic/DTOs/Responses/CheckIn/UnitAllocationResponse.cs), [`InspectionResponse.cs`](file:///d:/FPT/PRN222/Project/Final_Project/src/SelfStorageManagementSystem.BusinessLogic/DTOs/Responses/CheckIn/InspectionResponse.cs), [`HandoverCompleteResponse.cs`](file:///d:/FPT/PRN222/Project/Final_Project/src/SelfStorageManagementSystem.BusinessLogic/DTOs/Responses/CheckIn/HandoverCompleteResponse.cs), [`PinReissueResponse.cs`](file:///d:/FPT/PRN222/Project/Final_Project/src/SelfStorageManagementSystem.BusinessLogic/DTOs/Responses/CheckIn/PinReissueResponse.cs), [`CustomerUnitListItemResponse.cs`](file:///d:/FPT/PRN222/Project/Final_Project/src/SelfStorageManagementSystem.BusinessLogic/DTOs/Responses/CheckIn/CustomerUnitListItemResponse.cs), [`CustomerHandoverResponse.cs`](file:///d:/FPT/PRN222/Project/Final_Project/src/SelfStorageManagementSystem.BusinessLogic/DTOs/Responses/CheckIn/CustomerHandoverResponse.cs), [`CustomerNotificationResponse.cs`](file:///d:/FPT/PRN222/Project/Final_Project/src/SelfStorageManagementSystem.BusinessLogic/DTOs/Responses/CheckIn/CustomerNotificationResponse.cs)
- [`ICheckInService.cs`](file:///d:/FPT/PRN222/Project/Final_Project/src/SelfStorageManagementSystem.BusinessLogic/Services/Interfaces/ICheckInService.cs)
- [`CheckInService.cs`](file:///d:/FPT/PRN222/Project/Final_Project/src/SelfStorageManagementSystem.BusinessLogic/Services/Implementations/CheckInService.cs)
- [`IFacilityScopeService.cs`](file:///d:/FPT/PRN222/Project/Final_Project/src/SelfStorageManagementSystem.BusinessLogic/Services/Interfaces/IFacilityScopeService.cs) & [`FacilityScopeService.cs`](file:///d:/FPT/PRN222/Project/Final_Project/src/SelfStorageManagementSystem.BusinessLogic/Services/Implementations/FacilityScopeService.cs) (bổ sung `HasCheckInCounterAccessAsync`)
- [`ServiceCollectionExtensions.cs`](file:///d:/FPT/PRN222/Project/Final_Project/src/SelfStorageManagementSystem.BusinessLogic/DependencyInjection/ServiceCollectionExtensions.cs) (đăng ký `ICheckInService`)

### Presentation Layer
- [`CheckInController.cs`](file:///d:/FPT/PRN222/Project/Final_Project/src/SelfStorageManagementSystem.Presentation/Controllers/CheckInController.cs)
- [`MeController.cs`](file:///d:/FPT/PRN222/Project/Final_Project/src/SelfStorageManagementSystem.Presentation/Controllers/MeController.cs)

### Tests Layer
- [`CheckInServiceTests.cs`](file:///d:/FPT/PRN222/Project/Final_Project/tests/SelfStorageManagementSystem.Tests/CheckInServiceTests.cs) (18 unit tests)
- [`CheckInIntegrationTests.cs`](file:///d:/FPT/PRN222/Project/Final_Project/tests/SelfStorageManagementSystem.Tests/CheckInIntegrationTests.cs) (3 integration tests)
- [`CheckInSqlServerConcurrencyTests.cs`](file:///d:/FPT/PRN222/Project/Final_Project/tests/SelfStorageManagementSystem.Tests/CheckInSqlServerConcurrencyTests.cs) (2 real SQL Server concurrency tests)
- [`AuthAndAuthorizationIntegrationTests.cs`](file:///d:/FPT/PRN222/Project/Final_Project/tests/SelfStorageManagementSystem.Tests/AuthAndAuthorizationIntegrationTests.cs) (bổ sung config `CheckIn:PinHmacSecret` cho test factory)

---

## 8. Lưu Ý Bàn Giao Cho Codex Review

1. **Bảo toàn trạng thái nhánh và commit:** Toàn bộ mã nguồn Function 5 hiện đang nằm trên nhánh `Check-in-and-Digital-Handover`, sẵn sàng để Codex review. Không có commit tự động, không có thao tác push/merge ngoài ý muốn.
2. **Khóa phân tán và môi trường kiểm thử:**
   - Trong môi trường Web API / SQL Server production, hệ thống tự động kích hoạt transaction kết hợp `sp_getapplock` theo 3 cấp độ: `SS_Reservation_{id}` → `SS_Facility_{id}_UnitType_{id}_Capacity` → `SS_Facility_{id}_PinIssuance`.
   - Trong môi trường unit test (In-Memory EF Core), cơ chế tự động chuyển đổi sang semaphore phân tán in-memory giúp toàn bộ 18 unit tests chạy độc lập và không phụ thuộc SQL Server.
   - Khi chạy kiểm thử SQL Server thực tế, chỉ cần truyền chuỗi kết nối qua biến môi trường `SELFSTORAGE_SQL_TEST_CONNECTION_STRING`.
3. **Môi trường Database:** Cơ sở dữ liệu chính `SelfStoragePRN222` hoàn toàn được giữ nguyên không thay đổi; các kiểm thử concurrency được thực thi trên database riêng `SelfStoragePRN222_Test`.
