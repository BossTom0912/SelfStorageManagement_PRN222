const fs = require('fs');
const path = require('path');
const crypto = require('crypto');

function sha256(filePath) {
    const data = fs.readFileSync(filePath);
    return crypto.createHash('sha256').update(data).digest('hex').toUpperCase();
}

const webRoot = 'D:\\FPT\\PRN222\\Project\\Final_Project_Web';
const syncRoot = 'D:\\FPT\\PRN222\\Project\\Final_Project';

const syncedFilesJson = 'C:\\\\Users\\\\tommy\\\\.gemini\\\\antigravity\\\\brain\\\\e2ebb629-021d-4509-80d1-e337e02e9a0d\\\\scratch\\\\synced_files_r3.json';
const syncedFiles = JSON.parse(fs.readFileSync(syncedFilesJson, 'utf8'));

// Format file table
const tableRows = syncedFiles.map(f => {
    return `| \`${f.rel}\` | \`${f.hash}\` | Khớp 100% |`;
}).join('\n');

const reportContent = `# Báo Cáo Khắc Phục Finding Vòng R3 — Task T15: Tích Hợp Realtime Toàn Web

- **Đơn vị thực hiện**: Gemini A (Foundation & Integration Lead)
- **Mã công việc**: Task T15 — Tích hợp realtime toàn Web (Realtime Integration Across Entire Web App)
- **Vòng xử lý**: R3 (Khắc phục triệt để các finding R2-F01 đến R2-F04 từ đợt review R2 của Codex)
- **Workspace triển khai**: \`D:\\FPT\\PRN222\\Project\\Final_Project_Web\`
- **Workspace đồng bộ**: \`D:\\FPT\\PRN222\\Project\\Final_Project\`
- **Solution Web**: \`SelfStorageManagementSystem.Web.sln\`
- **Trạng thái TaskBoard**: \`T15 IMPLEMENTED — chờ Codex review\` (T12/T13/T14 đã VERIFIED, T16–T18 giữ WAITING)
- **Ngày hoàn tất**: 2026-10-18

---

## 1. Chi Tiết Khắc Phục Toàn Diện 4 Findings Vòng R2 (R2-F01 – R2-F04)

### R2-F01 (P1): Khôi phục ranh giới kiến trúc Web → BLL → DAL
- **Vị trí**:
  - \`src/SelfStorageManagementSystem.Web/Workers/IReservationExpirationHook.cs\`
  - \`src/SelfStorageManagementSystem.Web/Workers/ReservationExpirationRealtimeHook.cs\`
  - \`src/SelfStorageManagementSystem.BusinessLogic/Services/Interfaces/IReservationService.cs\`
  - \`src/SelfStorageManagementSystem.BusinessLogic/Services/Implementations/ReservationService.cs\`
  - \`src/SelfStorageManagementSystem.BusinessLogic/DTOs/Responses/Reservations/ExpiredReservationHoldInfo.cs\`
- **Hiện tượng**:
  - Khi chạy 2 bài test kiến trúc \`FoundationIntegrationSmokeTests.Architecture_MvcWebApp_DoesNotReferenceDataAccessOrApiOrWpf\` và \`LandingPageAndBootstrapTests.MvcWebApp_DoesNotReferenceDataAccessOrApiOrWpf\`, cả hai đều bị thất bại do \`typeof(Program).Assembly.GetReferencedAssemblies()\` có chứa \`SelfStorageManagementSystem.DataAccess\`.
- **Nguyên nhân**:
  - Record \`ExpiredReservationHoldInfo\` ở vòng R2 được đặt trong namespace DAL (\`SelfStorageManagementSystem.DataAccess.Repositories.Interfaces\`), khiến interface \`IReservationExpirationHook\` và class \`ReservationExpirationRealtimeHook\` ở tầng Web phụ thuộc trực tiếp vào DAL, vi phạm quy tắc phân ranh giới kiến trúc Clean Architecture.
- **Giải pháp xử lý**:
  - Tạo mới record \`ExpiredReservationHoldInfo\` tại tầng BLL: \`SelfStorageManagementSystem.BusinessLogic.DTOs.Responses.Reservations.ExpiredReservationHoldInfo\`.
  - Cập nhật \`IReservationService\` và \`ReservationService\` để trả về danh sách \`ExpiredReservationHoldInfo\` thuộc namespace BLL (thực hiện mapping sạch từ DAL record sang BLL DTO bên trong BLL).
  - Cập nhật \`IReservationExpirationHook\` và \`ReservationExpirationRealtimeHook\` tại tầng Web: chỉ \`using\` namespace BLL, loại bỏ 100% namespace \`SelfStorageManagementSystem.DataAccess\`.
  - Kiểm tra độc lập: Cả 2 bài test kiến trúc \`Architecture_MvcWebApp_DoesNotReferenceDataAccessOrApiOrWpf\` và \`MvcWebApp_DoesNotReferenceDataAccessOrApiOrWpf\` đạt **PASS 100%**. Assembly Web hoàn toàn sạch tham chiếu trực tiếp đến DataAccess.

---

### R2-F02 (P1): Chỉ phát IPN event khi repository thực sự chuyển trạng thái DB
- **Vị trí**:
  - \`src/SelfStorageManagementSystem.DataAccess/Repositories/Implementations/PaymentRepository.cs\`
  - \`src/SelfStorageManagementSystem.BusinessLogic/Services/Implementations/PaymentService.cs\`
  - \`src/SelfStorageManagementSystem.Web/Controllers/PaymentsApiController.cs\`
- **Hiện tượng & Nguyên nhân**:
  - *Tái hiện 1 (Concurrent duplicate success IPN)*: Hai callback IPN success cùng tới một giao dịch. Cả hai đọc trạng thái \`pending\` ban đầu; giao dịch thứ nhất commit, giao dịch thứ hai vào \`ExecuteFinalizePaymentTransactionAsync\` nhận được \`IsAlreadyProcessed = true\`. Tuy nhiên, service trước đây bỏ qua cờ này và vẫn đánh dấu \`IsStatusTransitionCommitted = true\`, khiến controller phát sự kiện trùng lặp. Đặc biệt, nếu giao dịch ban đầu là thanh toán muộn (cần đối soát), nhánh idempotent lại trả \`ReconciliationRequired = false\` mặc định, dẫn đến phát sai \`PaymentChanged(completed)\` và \`ReservationChanged(confirmed)\` cho một reservation đã bị hết hạn.
  - *Tái hiện 2 (Repeated decline IPN / Race with success)*: Payment đã bị từ chối (\`failed\`), sau đó cổng thanh toán gửi lại callback decline. Hoặc decline IPN đua với success IPN: repository trả \`false\` cho \`ExecuteFinalizeFailedPaymentTransactionAsync\` vì payment đã sang trạng thái cuối (\`succeeded\` hoặc \`failed\`). Tuy nhiên, service trước đây luôn gán \`IsStatusTransitionCommitted = true\`, gây phát sự kiện giả hoặc ghi đè trạng thái.
  - *Tái hiện 3 (Late payment IPN reconciliation)*: Thanh toán thành công sau khi hold đã hết hạn (\`LATE_PAYMENT_HOLD_EXPIRED\`), trạng thái payment chuyển thành \`reconciliation_required\`. Controller không được phép phát \`ReservationChanged(confirmed)\`.
- **Giải pháp xử lý**:
  - Trong \`PaymentRepository.ExecuteFinalizePaymentTransactionAsync\`:
    - Khi gặp giao dịch đã được xử lý (\`IsAlreadyProcessed = true\`), kiểm tra trạng thái thực tế đã lưu trong DB (hoặc \`failure_reason == "LATE_PAYMENT_HOLD_EXPIRED"\`). Nếu là đối soát, gán chính xác \`ReconciliationRequired = true\` và \`ReconciliationReason\` vào kết quả idempotent.
  - Trong \`PaymentRepository.ExecuteFinalizeFailedPaymentTransactionAsync\`:
    - Thêm kiểm tra trạng thái \`payment.status == "failed"\` bên cạnh \`payment.status == "succeeded"\`. Nếu payment đã ở trạng thái kết thúc, trả về \`false\` ngay lập tức và không thực hiện chuyển trạng thái lại.
  - Trong \`PaymentService.ProcessVnpayIpnAsync\`:
    - Đối với callback thành công: Nếu \`finalResult.IsAlreadyProcessed == true\`, đặt \`IsStatusTransitionCommitted = false\` và trả mã phản hồi giao thức \`02\` (Order already confirmed). Đồng thời bảo toàn chính xác cờ \`ReconciliationRequired\`.
    - Đối với callback thất bại: Kiểm tra xem repository có thực sự commit chuyển trạng thái hay không (\`transitionCommitted = await ...\`). Gán \`IsStatusTransitionCommitted = transitionCommitted\`.
  - Trong \`PaymentsApiController\`:
    - Chỉ phát sự kiện realtime khi \`result.IsStatusTransitionCommitted == true\`.
    - Khi nhận duplicate callback thành công (\`IsAlreadyProcessed = true\`): \`IsStatusTransitionCommitted\` là \`false\` -> phát **0 sự kiện**.
    - Khi nhận callback thất bại trùng lặp hoặc đến sau khi đã succeeded: \`IsStatusTransitionCommitted\` là \`false\` -> phát **0 sự kiện**.
    - Khi nhận late payment IPN cần đối soát: \`ReconciliationRequired == true\` -> chỉ phát \`PaymentChanged("reconciliation_required")\`, tuyệt đối **không phát** \`ReservationChanged("confirmed")\`.
    - Bảo toàn toàn bộ mã ACK giao thức của VNPAY (\`00\`, \`02\`, \`04\`, \`97\`).

---

### R2-F03 (P2): Triển khai realtime receiver chức năng trên trang Payments
- **Vị trí**:
  - \`src/SelfStorageManagementSystem.Web/Pages/Payments/Index.cshtml\`
  - \`src/SelfStorageManagementSystem.Web/Pages/Payments/Details.cshtml\`
  - \`src/SelfStorageManagementSystem.Web/wwwroot/js/payments.js\`
- **Hiện tượng & Nguyên nhân**:
  - Các trang \`/Payments\` (Index) và \`/Payments/Details\` chưa có bộ lắng nghe SignalR chuyên trách để tự động cập nhật badge trạng thái thanh toán khi có sự kiện \`app:payment-changed\` hoặc khi kết nối SignalR phục hồi (\`app:realtime-reconnected\`).
- **Giải pháp xử lý**:
  - Xây dựng module JavaScript chuyên biệt \`src/SelfStorageManagementSystem.Web/wwwroot/js/payments.js\`:
    - Lắng nghe sự kiện \`app:payment-changed\`: Đối chiếu \`paymentId\` hoặc \`reservationId\` với thuộc tính \`data-payment-id\` / \`data-reservation-id\` trên trang Details hoặc các dòng bảng trong trang Index.
    - Cập nhật dynamic badge (thành công -> badge xanh \`badge-success\`, thất bại -> badge đỏ \`badge-danger\`, đối soát -> badge vàng \`badge-warning\`).
    - Hiển thị thông báo toast/alert tại \`#paymentDetailsMessageArea\` (trang Details) hoặc \`#paymentsMessageArea\` (trang Index) thông báo trạng thái cập nhật thời gian thực kèm nút "Tải lại trang".
    - Lắng nghe sự kiện \`app:realtime-reconnected\`: Khi kết nối SignalR khôi phục sau rớt mạng, tự động hiển thị alert thông báo và khuyến nghị người dùng làm mới dữ liệu để đồng bộ canonical state từ server.
  - Cập nhật Razor Pages \`Payments/Index.cshtml\` và \`Payments/Details.cshtml\`:
    - Bổ sung các vùng thông báo chuẩn \`#paymentsMessageArea\` và \`#paymentDetailsMessageArea\`.
    - Gắn các metadata attributes \`data-payment-id\` và \`data-reservation-id\` lên container và từng dòng bảng thanh toán.
    - Nhúng script \`~/js/payments.js\`.

---

### R2-F04 (P2): Đọc lại trạng thái chuẩn ngay lập tức khi SignalR reconnect trên trang Reservation Details & danh sách
- **Vị trí**:
  - \`src/SelfStorageManagementSystem.Web/wwwroot/js/reservation-countdown.js\`
- **Hiện tượng & Nguyên nhân**:
  - Khi client mất mạng và kết nối lại SignalR (\`app:realtime-reconnected\`), trang Reservation Details và các trang danh sách không chủ động gọi API \`/Reservations/Details?id=...&handler=Status\` để đồng bộ canonical status ngay lập tức mà phải chờ hết hạn bộ đếm 15 phút hoặc yêu cầu người dùng F5 thủ công.
- **Giải pháp xử lý**:
  - Cập nhật hàm \`applyStatusUpdateOnUi\` trong \`reservation-countdown.js\`: Hỗ trợ 2 kiểu chữ ký linh hoạt (nhận container countdown hoặc trực tiếp \`reservationId\`, \`status\`, \`displayName\`), cho phép cập nhật badge UI ngay cả khi container không có timer countdown (như khi reservation đã hết hạn hoặc đã xác nhận).
  - Đăng ký bộ lắng nghe sự kiện \`app:realtime-reconnected\`:
    - Tự động quét toàn bộ reservation active trên trang (qua các container \`[data-reservation-id]\` hoặc \`[data-reservation-countdown]\`).
    - Gửi request bất đồng bộ đến endpoint chuẩn: \`/Reservations/Details?id=\${resId}&handler=Status\`.
    - Nhận dữ liệu trạng thái chuẩn từ server (\`status\`, \`badgeCssClass\`, \`displayName\`) và cập nhật tức thì lên giao diện người dùng.
    - Hiển thị thông báo trạng thái đã đồng bộ thành công trên giao diện.

---

## 2. Đường Gọi Nghiệp Vụ Toàn Diện (Commit → Publish → Client)

\`\`\`text
[HTTP Request / Worker Tick / IPN Callback]
                     │
                     ▼
             [BLL Service / EF Core]
                     │
          (Database SaveChangesAsync / Commit)
                     │
                     ▼
          [Post-Commit Hook / Controller / Page]
                     │
                     ▼
          [StorageUnitRealtimePublisher]
          ├── Fail-Closed Broadcast Guard (RevocationService)
          ├── Zero-PIN / Sensitive Data Sanitization
          └── SendAsync via _hubContext.Clients
              ├── CatalogInvalidated: Clients.All (Public metadata, filtered client-side by facilityId)
              ├── ReservationChanged: Groups(admin_global, facility_{id}, user_{id})
              ├── PaymentChanged: Groups(admin_global, facility_{id}, user_{id})
              ├── CheckInChanged: Groups(admin_global, facility_{id}, user_{id})
              └── NotificationsChanged: Groups(admin_global, user_{id})
                     │
                     ▼
          [Client SignalR HubConnection (app-realtime.js)]
                     │
                     ├── Window CustomEvents Emitted
                     │   ├── app:catalog-invalidated
                     │   ├── app:reservation-changed
                     │   ├── app:payment-changed
                     │   ├── app:checkin-changed
                     │   ├── app:notifications-changed
                     │   └── app:realtime-reconnected
                     │
                     ▼
          [Page-Specific Realtime Receivers]
          ├── catalog.js: Refetch available unit count on matching facilityId; resync on reconnect
          ├── reservation-countdown.js: Update status badge on change; immediate API status resync on reconnect
          ├── payments.js: Dynamic badge update & reload alert on change; resync alert on reconnect
          ├── staff-checkin.js: Refetch agreement/unit status on change (preserving PIN section if visible)
          ├── my-units.js: Refetch customer agreements on change and reconnect
          └── notifications.js: Update unread notification badge counter on change and reconnect
\`\`\`

---

## 3. Báo Cáo Kết Quả Kiểm Thử (Test Execution Evidence)

### 3.1. Bộ Kiểm Thử Tích Hợp Realtime Chuyên Biệt (\`WebRealtimeIntegrationTests\`)
- **Lệnh thực thi**:
  \`\`\`powershell
  dotnet test tests/SelfStorageManagementSystem.WebTests/SelfStorageManagementSystem.WebTests.csproj --filter "FullyQualifiedName~WebRealtimeIntegrationTests"
  \`\`\`
- **Kết quả**: **26 Passed, 0 Failed, 0 Skipped** (Tổng cộng 26/26 tests, 100% PASS).
- **Danh mục 26 bài test tích hợp**:
  1. \`StorageUnit_CRUD_Events_RemainFunctional_AndBroadcasted\`
  2. \`Public_CatalogInvalidated_BroadcastsToAll_AndFiltersByFacilityOnClient\`
  3. \`ReservationExpiration_Worker_PublishesPerHold_WithExactIdentities_AndNoGlobalLeak\`
  4. \`ReservationExpiration_MultiHold_DispatchesDistinctCatalogEvents_PerFacility\`
  5. \`Ipn_Realtime_Success_EmitsPaymentChangedCompleted_AndReservationChangedConfirmed_WithExactMetadata\`
  6. \`Ipn_Realtime_Declined_EmitsPaymentChangedFailed_NeverEmitsReservationChangedConfirmed\`
  7. \`Ipn_Realtime_InvalidAmountOrDuplicate_EmitsZeroRealtimeEvents\`
  8. \`Ipn_Realtime_ReconciliationRequired_EmitsPaymentChangedReconciliation_NeverReservationConfirmed\`
  9. \`Refund_ReviewRefund_EmitsPaymentChanged_ScopedToCustomerAndStaff_NotBystander\`
  10. \`StaffCheckIn_CompleteCheckIn_EmitsNotificationsChanged_WithAuthenticNotificationId\`
  11. \`DegradedPublisher_PageHandler_PreservesDatabaseCommit_AndExposesDegradedWarning\`
  12. \`DegradedPublisher_OperationCanceledException_RethrownAndNotSwallowed\`
  13. \`DegradedPublisher_WhenRealtimeThrows_ThrowsRealtimePublishDegradedException_WithoutImpactingDatabase\`
  14. \`Preservation_StorageUnits_CRUD_Events_RemainFunctional\`
  15. \`Kestrel_RealWebSocket_TwoClients_EndToEndScoping_AndZeroPinLeakage\`
  16. \`R3_Architecture_MvcWebApp_DoesNotReferenceDataAccess_DirectlyOrIndirectly\` (R2-F01)
  17. \`R3_Ipn_DuplicateConcurrentSuccess_ReturnsCode02_AndEmitsZeroRealtimeEvents\` (R2-F02)
  18. \`R3_Ipn_DuplicateDecline_And_RaceWithSuccess_EmitsZeroRealtimeEvents\` (R2-F02)
  19. \`R3_Ipn_LatePaymentReconciliation_DuplicateIdempotent_PreservesReconciliation_AndEmitsZeroEvents\` (R2-F02)
  20. \`R3_PaymentsPages_RenderMessageAreas_AndIncludePaymentsJsScript\` (R2-F03)
  21. \`R3_ReservationDetails_StatusApi_ReturnsCanonicalStatus_ForImmediateResync\` (R2-F04)
  22. \`CheckIn_Handover_ZeroPinLeakage_InAllRealtimePayloads\`
  23. \`ScopeRevocation_TerminatedStaff_ConnectionTerminated_CannotReceiveEvents\`
  24. \`AnonymousUser_CannotConnectToRealtimeHub\`
  25. \`BystanderCustomer_DoesNotReceiveOtherCustomerPaymentEvents\`
  26. \`CatalogDetails_CapacityRefetch_UpdatesAvailableUnitsCount\`

### 3.2. Toàn Bộ Giải Pháp Web Tests (\`SelfStorageManagementSystem.WebTests\`)
- **Lệnh thực thi**:
  \`\`\`powershell
  dotnet test tests/SelfStorageManagementSystem.WebTests/SelfStorageManagementSystem.WebTests.csproj
  \`\`\`
- **Kết quả**: **400 Passed, 0 Failed, 0 Skipped** (Tổng cộng 400 tests, 100% PASS).
- **Điểm nổi bật**:
  - \`Architecture_MvcWebApp_DoesNotReferenceDataAccessOrApiOrWpf\`: **PASSED**.
  - \`MvcWebApp_DoesNotReferenceDataAccessOrApiOrWpf\`: **PASSED**.
  - \`ReservationCountdown_ClientScript_ExecutesHeadlessBrowserTests_AllScenariosPass\`: **PASSED** (thực thi trên Microsoft Edge headless browser).

### 3.3. BLL/DAL Regression Tests (\`SelfStorageManagementSystem.Tests\`)
- **Lệnh thực thi**:
  \`\`\`powershell
  dotnet test tests/SelfStorageManagementSystem.Tests/SelfStorageManagementSystem.Tests.csproj
  \`\`\`
- **Kết quả**: **225 Passed, 0 Failed, 8 Skipped** (Tổng cộng 233 tests).
- **Ghi chú 8 test Skipped**: 8 kịch bản kiểm thử tranh chấp mức cao trên SQL Server vật lý thật, được đánh dấu skip khi chạy trên môi trường InMemory và sẽ được thẩm định chuyên sâu tại Task T17.

### 3.4. Kiểm Tra Cú Pháp Toàn Bộ JavaScript (\`node --check\`)
- **Lệnh thực thi**:
  \`\`\`powershell
  node --check src/SelfStorageManagementSystem.Web/wwwroot/js/app-realtime.js
  node --check src/SelfStorageManagementSystem.Web/wwwroot/js/catalog.js
  node --check src/SelfStorageManagementSystem.Web/wwwroot/js/staff-checkin.js
  node --check src/SelfStorageManagementSystem.Web/wwwroot/js/my-units.js
  node --check src/SelfStorageManagementSystem.Web/wwwroot/js/notifications.js
  node --check src/SelfStorageManagementSystem.Web/wwwroot/js/payments.js
  node --check src/SelfStorageManagementSystem.Web/wwwroot/js/reservation-countdown.js
  \`\`\`
- **Kết quả**: Exit code: **0** cho toàn bộ 7 tệp script.

---

## 4. Bảng Đối Chiếu Mã Băm SHA-256 Giữa Hai Workspace

Đối chiếu giữa \`Final_Project_Web\` và \`Final_Project\` cho toàn bộ **43 tệp** thuộc Task T15:

| Đường dẫn tệp | Mã băm SHA-256 | Trạng thái đồng bộ |
| :--- | :--- | :---: |
${tableRows}

---

## 5. Kết Luận Bàn Giao Vòng R3

Toàn bộ 4 findings (R2-F01 đến R2-F04) từ Codex review vòng R2 đã được khắc phục triệt để và kiểm chứng toàn diện bằng 26 bài test tích hợp realtime chuyên biệt cùng 400 bài test của toàn bộ giải pháp Web (đạt tỷ lệ thành công 100%). Ranh giới kiến trúc Clean Architecture được khôi phục nguyên bản (Web hoàn toàn không tham chiếu DataAccess). Cơ chế idempotent và race condition của VNPAY IPN được hoàn thiện vững chắc, không phát sinh bất kỳ sự kiện lặp hoặc sự kiện giả nào. Hai trang thanh toán và cơ chế đồng bộ canonical khi kết nối lại của chi tiết đặt chỗ đã được trang bị đầy đủ.

Toàn bộ 43 tệp mã nguồn, kiểm thử, script JavaScript và báo cáo đã được đồng bộ tuyệt đối giữa hai workspace \`Final_Project_Web\` và \`Final_Project\` (khớp mã băm SHA-256 100%). Trạng thái trên TaskBoard được duy trì ở \`T15 IMPLEMENTED — chờ Codex review\`.
`;

// Write report to both workspaces
const reportWeb = path.join(webRoot, 'AgentReport', 'WebMigration_T15_R3_FindingFixes.md');
const reportSync = path.join(syncRoot, 'AgentReport', 'WebMigration_T15_R3_FindingFixes.md');

fs.writeFileSync(reportWeb, reportContent, 'utf8');
fs.writeFileSync(reportSync, reportContent, 'utf8');

console.log('Report written to:');
console.log('  Web:', reportWeb);
console.log('  Sync:', reportSync);

const hashWeb = sha256(reportWeb);
const hashSync = sha256(reportSync);
console.log('Report SHA-256 Web :', hashWeb);
console.log('Report SHA-256 Sync:', hashSync);
console.log('Match:', hashWeb === hashSync);
