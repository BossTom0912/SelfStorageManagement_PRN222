# Báo Cáo Khắc Phục Finding Vòng R3 — Task T15: Tích Hợp Realtime Toàn Web

- **Đơn vị thực hiện**: Gemini A (Foundation & Integration Lead)
- **Mã công việc**: Task T15 — Tích hợp realtime toàn Web (Realtime Integration Across Entire Web App)
- **Vòng xử lý**: R3 (Khắc phục triệt để các finding R2-F01 đến R2-F04 từ đợt review R2 của Codex)
- **Workspace triển khai**: `D:\FPT\PRN222\Project\Final_Project_Web`
- **Workspace đồng bộ**: `D:\FPT\PRN222\Project\Final_Project`
- **Solution Web**: `SelfStorageManagementSystem.Web.sln`
- **Trạng thái TaskBoard**: `T15 IMPLEMENTED — chờ Codex review` (T12/T13/T14 đã VERIFIED, T16–T18 giữ WAITING)
- **Ngày hoàn tất**: 2026-10-18

---

## 1. Chi Tiết Khắc Phục Toàn Diện 4 Findings Vòng R2 (R2-F01 – R2-F04)

### R2-F01 (P1): Khôi phục ranh giới kiến trúc Web → BLL → DAL
- **Vị trí**:
  - `src/SelfStorageManagementSystem.Web/Workers/IReservationExpirationHook.cs`
  - `src/SelfStorageManagementSystem.Web/Workers/ReservationExpirationRealtimeHook.cs`
  - `src/SelfStorageManagementSystem.BusinessLogic/Services/Interfaces/IReservationService.cs`
  - `src/SelfStorageManagementSystem.BusinessLogic/Services/Implementations/ReservationService.cs`
  - `src/SelfStorageManagementSystem.BusinessLogic/DTOs/Responses/Reservations/ExpiredReservationHoldInfo.cs`
- **Hiện tượng**:
  - Khi chạy 2 bài test kiến trúc `FoundationIntegrationSmokeTests.Architecture_MvcWebApp_DoesNotReferenceDataAccessOrApiOrWpf` và `LandingPageAndBootstrapTests.MvcWebApp_DoesNotReferenceDataAccessOrApiOrWpf`, cả hai đều bị thất bại do `typeof(Program).Assembly.GetReferencedAssemblies()` có chứa `SelfStorageManagementSystem.DataAccess`.
- **Nguyên nhân**:
  - Record `ExpiredReservationHoldInfo` ở vòng R2 được đặt trong namespace DAL (`SelfStorageManagementSystem.DataAccess.Repositories.Interfaces`), khiến interface `IReservationExpirationHook` và class `ReservationExpirationRealtimeHook` ở tầng Web phụ thuộc trực tiếp vào DAL, vi phạm quy tắc phân ranh giới kiến trúc Clean Architecture.
- **Giải pháp xử lý**:
  - Tạo mới record `ExpiredReservationHoldInfo` tại tầng BLL: `SelfStorageManagementSystem.BusinessLogic.DTOs.Responses.Reservations.ExpiredReservationHoldInfo`.
  - Cập nhật `IReservationService` và `ReservationService` để trả về danh sách `ExpiredReservationHoldInfo` thuộc namespace BLL (thực hiện mapping sạch từ DAL record sang BLL DTO bên trong BLL).
  - Cập nhật `IReservationExpirationHook` và `ReservationExpirationRealtimeHook` tại tầng Web: chỉ `using` namespace BLL, loại bỏ 100% namespace `SelfStorageManagementSystem.DataAccess`.
  - Kiểm tra độc lập: Cả 2 bài test kiến trúc `Architecture_MvcWebApp_DoesNotReferenceDataAccessOrApiOrWpf` và `MvcWebApp_DoesNotReferenceDataAccessOrApiOrWpf` đạt **PASS 100%**. Assembly Web hoàn toàn sạch tham chiếu trực tiếp đến DataAccess.

---

### R2-F02 (P1): Chỉ phát IPN event khi repository thực sự chuyển trạng thái DB
- **Vị trí**:
  - `src/SelfStorageManagementSystem.DataAccess/Repositories/Implementations/PaymentRepository.cs`
  - `src/SelfStorageManagementSystem.BusinessLogic/Services/Implementations/PaymentService.cs`
  - `src/SelfStorageManagementSystem.Web/Controllers/PaymentsApiController.cs`
- **Hiện tượng & Nguyên nhân**:
  - *Tái hiện 1 (Concurrent duplicate success IPN)*: Hai callback IPN success cùng tới một giao dịch. Cả hai đọc trạng thái `pending` ban đầu; giao dịch thứ nhất commit, giao dịch thứ hai vào `ExecuteFinalizePaymentTransactionAsync` nhận được `IsAlreadyProcessed = true`. Tuy nhiên, service trước đây bỏ qua cờ này và vẫn đánh dấu `IsStatusTransitionCommitted = true`, khiến controller phát sự kiện trùng lặp. Đặc biệt, nếu giao dịch ban đầu là thanh toán muộn (cần đối soát), nhánh idempotent lại trả `ReconciliationRequired = false` mặc định, dẫn đến phát sai `PaymentChanged(completed)` và `ReservationChanged(confirmed)` cho một reservation đã bị hết hạn.
  - *Tái hiện 2 (Repeated decline IPN / Race with success)*: Payment đã bị từ chối (`failed`), sau đó cổng thanh toán gửi lại callback decline. Hoặc decline IPN đua với success IPN: repository trả `false` cho `ExecuteFinalizeFailedPaymentTransactionAsync` vì payment đã sang trạng thái cuối (`succeeded` hoặc `failed`). Tuy nhiên, service trước đây luôn gán `IsStatusTransitionCommitted = true`, gây phát sự kiện giả hoặc ghi đè trạng thái.
  - *Tái hiện 3 (Late payment IPN reconciliation)*: Thanh toán thành công sau khi hold đã hết hạn (`LATE_PAYMENT_HOLD_EXPIRED`), trạng thái payment chuyển thành `reconciliation_required`. Controller không được phép phát `ReservationChanged(confirmed)`.
- **Giải pháp xử lý**:
  - Trong `PaymentRepository.ExecuteFinalizePaymentTransactionAsync`:
    - Khi gặp giao dịch đã được xử lý (`IsAlreadyProcessed = true`), kiểm tra trạng thái thực tế đã lưu trong DB (hoặc `failure_reason == "LATE_PAYMENT_HOLD_EXPIRED"`). Nếu là đối soát, gán chính xác `ReconciliationRequired = true` và `ReconciliationReason` vào kết quả idempotent.
  - Trong `PaymentRepository.ExecuteFinalizeFailedPaymentTransactionAsync`:
    - Thêm kiểm tra trạng thái `payment.status == "failed"` bên cạnh `payment.status == "succeeded"`. Nếu payment đã ở trạng thái kết thúc, trả về `false` ngay lập tức và không thực hiện chuyển trạng thái lại.
  - Trong `PaymentService.ProcessVnpayIpnAsync`:
    - Đối với callback thành công: Nếu `finalResult.IsAlreadyProcessed == true`, đặt `IsStatusTransitionCommitted = false` và trả mã phản hồi giao thức `02` (Order already confirmed). Đồng thời bảo toàn chính xác cờ `ReconciliationRequired`.
    - Đối với callback thất bại: Kiểm tra xem repository có thực sự commit chuyển trạng thái hay không (`transitionCommitted = await ...`). Gán `IsStatusTransitionCommitted = transitionCommitted`.
  - Trong `PaymentsApiController`:
    - Chỉ phát sự kiện realtime khi `result.IsStatusTransitionCommitted == true`.
    - Khi nhận duplicate callback thành công (`IsAlreadyProcessed = true`): `IsStatusTransitionCommitted` là `false` -> phát **0 sự kiện**.
    - Khi nhận callback thất bại trùng lặp hoặc đến sau khi đã succeeded: `IsStatusTransitionCommitted` là `false` -> phát **0 sự kiện**.
    - Khi nhận late payment IPN cần đối soát: `ReconciliationRequired == true` -> chỉ phát `PaymentChanged("reconciliation_required")`, tuyệt đối **không phát** `ReservationChanged("confirmed")`.
    - Bảo toàn toàn bộ mã ACK giao thức của VNPAY (`00`, `02`, `04`, `97`).

---

### R2-F03 (P2): Triển khai realtime receiver chức năng trên trang Payments
- **Vị trí**:
  - `src/SelfStorageManagementSystem.Web/Pages/Payments/Index.cshtml`
  - `src/SelfStorageManagementSystem.Web/Pages/Payments/Details.cshtml`
  - `src/SelfStorageManagementSystem.Web/wwwroot/js/payments.js`
- **Hiện tượng & Nguyên nhân**:
  - Các trang `/Payments` (Index) và `/Payments/Details` chưa có bộ lắng nghe SignalR chuyên trách để tự động cập nhật badge trạng thái thanh toán khi có sự kiện `app:payment-changed` hoặc khi kết nối SignalR phục hồi (`app:realtime-reconnected`).
- **Giải pháp xử lý**:
  - Xây dựng module JavaScript chuyên biệt `src/SelfStorageManagementSystem.Web/wwwroot/js/payments.js`:
    - Lắng nghe sự kiện `app:payment-changed`: Đối chiếu `paymentId` hoặc `reservationId` với thuộc tính `data-payment-id` / `data-reservation-id` trên trang Details hoặc các dòng bảng trong trang Index.
    - Cập nhật dynamic badge (thành công -> badge xanh `badge-success`, thất bại -> badge đỏ `badge-danger`, đối soát -> badge vàng `badge-warning`).
    - Hiển thị thông báo toast/alert tại `#paymentDetailsMessageArea` (trang Details) hoặc `#paymentsMessageArea` (trang Index) thông báo trạng thái cập nhật thời gian thực kèm nút "Tải lại trang".
    - Lắng nghe sự kiện `app:realtime-reconnected`: Khi kết nối SignalR khôi phục sau rớt mạng, tự động hiển thị alert thông báo và khuyến nghị người dùng làm mới dữ liệu để đồng bộ canonical state từ server.
  - Cập nhật Razor Pages `Payments/Index.cshtml` và `Payments/Details.cshtml`:
    - Bổ sung các vùng thông báo chuẩn `#paymentsMessageArea` và `#paymentDetailsMessageArea`.
    - Gắn các metadata attributes `data-payment-id` và `data-reservation-id` lên container và từng dòng bảng thanh toán.
    - Nhúng script `~/js/payments.js`.

---

### R2-F04 (P2): Đọc lại trạng thái chuẩn ngay lập tức khi SignalR reconnect trên trang Reservation Details & danh sách
- **Vị trí**:
  - `src/SelfStorageManagementSystem.Web/wwwroot/js/reservation-countdown.js`
- **Hiện tượng & Nguyên nhân**:
  - Khi client mất mạng và kết nối lại SignalR (`app:realtime-reconnected`), trang Reservation Details và các trang danh sách không chủ động gọi API `/Reservations/Details?id=...&handler=Status` để đồng bộ canonical status ngay lập tức mà phải chờ hết hạn bộ đếm 15 phút hoặc yêu cầu người dùng F5 thủ công.
- **Giải pháp xử lý**:
  - Cập nhật hàm `applyStatusUpdateOnUi` trong `reservation-countdown.js`: Hỗ trợ 2 kiểu chữ ký linh hoạt (nhận container countdown hoặc trực tiếp `reservationId`, `status`, `displayName`), cho phép cập nhật badge UI ngay cả khi container không có timer countdown (như khi reservation đã hết hạn hoặc đã xác nhận).
  - Đăng ký bộ lắng nghe sự kiện `app:realtime-reconnected`:
    - Tự động quét toàn bộ reservation active trên trang (qua các container `[data-reservation-id]` hoặc `[data-reservation-countdown]`).
    - Gửi request bất đồng bộ đến endpoint chuẩn: `/Reservations/Details?id=${resId}&handler=Status`.
    - Nhận dữ liệu trạng thái chuẩn từ server (`status`, `badgeCssClass`, `displayName`) và cập nhật tức thì lên giao diện người dùng.
    - Hiển thị thông báo trạng thái đã đồng bộ thành công trên giao diện.

---

## 2. Đường Gọi Nghiệp Vụ Toàn Diện (Commit → Publish → Client)

```text
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
```

---

## 3. Báo Cáo Kết Quả Kiểm Thử (Test Execution Evidence)

### 3.1. Bộ Kiểm Thử Tích Hợp Realtime Chuyên Biệt (`WebRealtimeIntegrationTests`)
- **Lệnh thực thi**:
  ```powershell
  dotnet test tests/SelfStorageManagementSystem.WebTests/SelfStorageManagementSystem.WebTests.csproj --filter "FullyQualifiedName~WebRealtimeIntegrationTests"
  ```
- **Kết quả**: **26 Passed, 0 Failed, 0 Skipped** (Tổng cộng 26/26 tests, 100% PASS).
- **Danh mục 26 bài test tích hợp**:
  1. `StorageUnit_CRUD_Events_RemainFunctional_AndBroadcasted`
  2. `Public_CatalogInvalidated_BroadcastsToAll_AndFiltersByFacilityOnClient`
  3. `ReservationExpiration_Worker_PublishesPerHold_WithExactIdentities_AndNoGlobalLeak`
  4. `ReservationExpiration_MultiHold_DispatchesDistinctCatalogEvents_PerFacility`
  5. `Ipn_Realtime_Success_EmitsPaymentChangedCompleted_AndReservationChangedConfirmed_WithExactMetadata`
  6. `Ipn_Realtime_Declined_EmitsPaymentChangedFailed_NeverEmitsReservationChangedConfirmed`
  7. `Ipn_Realtime_InvalidAmountOrDuplicate_EmitsZeroRealtimeEvents`
  8. `Ipn_Realtime_ReconciliationRequired_EmitsPaymentChangedReconciliation_NeverReservationConfirmed`
  9. `Refund_ReviewRefund_EmitsPaymentChanged_ScopedToCustomerAndStaff_NotBystander`
  10. `StaffCheckIn_CompleteCheckIn_EmitsNotificationsChanged_WithAuthenticNotificationId`
  11. `DegradedPublisher_PageHandler_PreservesDatabaseCommit_AndExposesDegradedWarning`
  12. `DegradedPublisher_OperationCanceledException_RethrownAndNotSwallowed`
  13. `DegradedPublisher_WhenRealtimeThrows_ThrowsRealtimePublishDegradedException_WithoutImpactingDatabase`
  14. `Preservation_StorageUnits_CRUD_Events_RemainFunctional`
  15. `Kestrel_RealWebSocket_TwoClients_EndToEndScoping_AndZeroPinLeakage`
  16. `R3_Architecture_MvcWebApp_DoesNotReferenceDataAccess_DirectlyOrIndirectly` (R2-F01)
  17. `R3_Ipn_DuplicateConcurrentSuccess_ReturnsCode02_AndEmitsZeroRealtimeEvents` (R2-F02)
  18. `R3_Ipn_DuplicateDecline_And_RaceWithSuccess_EmitsZeroRealtimeEvents` (R2-F02)
  19. `R3_Ipn_LatePaymentReconciliation_DuplicateIdempotent_PreservesReconciliation_AndEmitsZeroEvents` (R2-F02)
  20. `R3_PaymentsPages_RenderMessageAreas_AndIncludePaymentsJsScript` (R2-F03)
  21. `R3_ReservationDetails_StatusApi_ReturnsCanonicalStatus_ForImmediateResync` (R2-F04)
  22. `CheckIn_Handover_ZeroPinLeakage_InAllRealtimePayloads`
  23. `ScopeRevocation_TerminatedStaff_ConnectionTerminated_CannotReceiveEvents`
  24. `AnonymousUser_CannotConnectToRealtimeHub`
  25. `BystanderCustomer_DoesNotReceiveOtherCustomerPaymentEvents`
  26. `CatalogDetails_CapacityRefetch_UpdatesAvailableUnitsCount`

### 3.2. Toàn Bộ Giải Pháp Web Tests (`SelfStorageManagementSystem.WebTests`)
- **Lệnh thực thi**:
  ```powershell
  dotnet test tests/SelfStorageManagementSystem.WebTests/SelfStorageManagementSystem.WebTests.csproj
  ```
- **Kết quả**: **400 Passed, 0 Failed, 0 Skipped** (Tổng cộng 400 tests, 100% PASS).
- **Điểm nổi bật**:
  - `Architecture_MvcWebApp_DoesNotReferenceDataAccessOrApiOrWpf`: **PASSED**.
  - `MvcWebApp_DoesNotReferenceDataAccessOrApiOrWpf`: **PASSED**.
  - `ReservationCountdown_ClientScript_ExecutesHeadlessBrowserTests_AllScenariosPass`: **PASSED** (thực thi trên Microsoft Edge headless browser).

### 3.3. BLL/DAL Regression Tests (`SelfStorageManagementSystem.Tests`)
- **Lệnh thực thi**:
  ```powershell
  dotnet test tests/SelfStorageManagementSystem.Tests/SelfStorageManagementSystem.Tests.csproj
  ```
- **Kết quả**: **225 Passed, 0 Failed, 8 Skipped** (Tổng cộng 233 tests).
- **Ghi chú 8 test Skipped**: 8 kịch bản kiểm thử tranh chấp mức cao trên SQL Server vật lý thật, được đánh dấu skip khi chạy trên môi trường InMemory và sẽ được thẩm định chuyên sâu tại Task T17.

### 3.4. Kiểm Tra Cú Pháp Toàn Bộ JavaScript (`node --check`)
- **Lệnh thực thi**:
  ```powershell
  node --check src/SelfStorageManagementSystem.Web/wwwroot/js/app-realtime.js
  node --check src/SelfStorageManagementSystem.Web/wwwroot/js/catalog.js
  node --check src/SelfStorageManagementSystem.Web/wwwroot/js/staff-checkin.js
  node --check src/SelfStorageManagementSystem.Web/wwwroot/js/my-units.js
  node --check src/SelfStorageManagementSystem.Web/wwwroot/js/notifications.js
  node --check src/SelfStorageManagementSystem.Web/wwwroot/js/payments.js
  node --check src/SelfStorageManagementSystem.Web/wwwroot/js/reservation-countdown.js
  ```
- **Kết quả**: Exit code: **0** cho toàn bộ 7 tệp script.

---

## 4. Bảng Đối Chiếu Mã Băm SHA-256 Giữa Hai Workspace

Đối chiếu giữa `Final_Project_Web` và `Final_Project` cho toàn bộ **43 tệp** thuộc Task T15:

| Đường dẫn tệp | Mã băm SHA-256 | Trạng thái đồng bộ |
| :--- | :--- | :---: |
| `src/SelfStorageManagementSystem.BusinessLogic/DTOs/Responses/Reservations/ExpiredReservationHoldInfo.cs` | `2C4F425B098FE10450C7F071747F315D3486CE50AC74E4A1B269463253829DEE` | Khớp 100% |
| `src/SelfStorageManagementSystem.BusinessLogic/Services/Interfaces/IPaymentGateway.cs` | `7BF72D4BB936046170791C07A81B934FA7BCF427FCEDA1E536234803064238F0` | Khớp 100% |
| `src/SelfStorageManagementSystem.BusinessLogic/DTOs/Responses/Payments/RefundDetailResponse.cs` | `255A675B2F11E29EDF8FEC491EEFE420DD209608962A4D495B4A685D19323E43` | Khớp 100% |
| `src/SelfStorageManagementSystem.BusinessLogic/DTOs/Responses/CheckIn/HandoverCompleteResponse.cs` | `0530F68E8D3F30FCD6CCC4EAEE63C20565E797DEEA9647B24C7E323D0E68F9D2` | Khớp 100% |
| `src/SelfStorageManagementSystem.DataAccess/Repositories/Interfaces/ICheckInRepository.cs` | `9C9335895AF17AAAA74FB369031F821E98B64ACE8CD695DCF8B040AEE4110F82` | Khớp 100% |
| `src/SelfStorageManagementSystem.DataAccess/Repositories/Implementations/CheckInRepository.cs` | `C35EE35DAB82B5D1C92954D8041BD2AE74A2C10DA90B041C8777090295BE6903` | Khớp 100% |
| `src/SelfStorageManagementSystem.BusinessLogic/Services/Implementations/CheckInService.cs` | `9353ACD75E86F26596D02E35F0E0B2282373B1DC292AA030AC2650E99D2107C3` | Khớp 100% |
| `src/SelfStorageManagementSystem.BusinessLogic/Services/Implementations/PaymentService.cs` | `63C5CB86D3838EA4471A44903D7BF6B19EF10BFDA8AD5E7E17609069E1A75ACD` | Khớp 100% |
| `src/SelfStorageManagementSystem.DataAccess/Repositories/Interfaces/IReservationRepository.cs` | `1CEF2C6C025FBCA807E14780E7F5BC546D6F6F5E4CF7251E56520C5685066E5D` | Khớp 100% |
| `src/SelfStorageManagementSystem.DataAccess/Repositories/Implementations/ReservationRepository.cs` | `1D69596A598C361C235320CA5FCDF56F33EA627C8F1CE6F898350DE0FF05A664` | Khớp 100% |
| `src/SelfStorageManagementSystem.DataAccess/Repositories/Implementations/PaymentRepository.cs` | `968A774CDC8F7B95ED821A3620F680D0F3B00292A59B15997EE0C8035D953FA9` | Khớp 100% |
| `src/SelfStorageManagementSystem.BusinessLogic/Services/Interfaces/IReservationService.cs` | `1A4A60A035D46C40FDDEE0F541920A6449BA14538EFFF3F8FDE3D3FC6342096F` | Khớp 100% |
| `src/SelfStorageManagementSystem.BusinessLogic/Services/Implementations/ReservationService.cs` | `79166FD6D1B3B47FFE7F94AE21074F613742BDAED11D4B077479638C3060122D` | Khớp 100% |
| `src/SelfStorageManagementSystem.Web/Workers/IReservationExpirationHook.cs` | `FB6A5B7B4CE8317E72225E141905B57D0A621D60B912C51DBD25636EFB807565` | Khớp 100% |
| `src/SelfStorageManagementSystem.Web/Workers/ReservationExpirationRealtimeHook.cs` | `6158AD7CDA2BD1934A13292A34B29E5A3CCDBA91B1F03F37866F06242E440C62` | Khớp 100% |
| `src/SelfStorageManagementSystem.Web/Workers/ReservationHoldExpirationWorker.cs` | `396A871A31A56EDD65D25760487B2F41CA19FC3F0287EDCD35599EA99D51703C` | Khớp 100% |
| `src/SelfStorageManagementSystem.Web/Realtime/IWebRealtimePublisher.cs` | `9EDDE031316E7F83041F572AAB0404E5E6E27ED77305A2BCC02EF27E3358A18B` | Khớp 100% |
| `src/SelfStorageManagementSystem.Web/Realtime/StorageUnitRealtimePublisher.cs` | `7113E3F3A56922A8D2798AA63BAD4B37B5989BD9DF7CED7641F2B39DBFA854DF` | Khớp 100% |
| `src/SelfStorageManagementSystem.Web/Realtime/RealtimePublishDegradedException.cs` | `8BF4FFA82A03177FD5032A7E07AE51BB53793D37CB09323370BCDAE3F03059A0` | Khớp 100% |
| `src/SelfStorageManagementSystem.Web/Realtime/StorageUnitHubScopeRevocationService.cs` | `3E634DC0F933ACF1DC67807055E273E05509EE6A32C657D8830A43307D4EDDC0` | Khớp 100% |
| `src/SelfStorageManagementSystem.Web/Realtime/StorageUnitHubConnectionTracker.cs` | `30AFA298D6D32B6F2F9B9A9D3BCB1FC56ECEF36590004E3A0CAE091E27E1E05E` | Khớp 100% |
| `src/SelfStorageManagementSystem.Web/Realtime/StorageUnitScopeSyncWorker.cs` | `6F61E3FE086A22D30F689B389FD584A55813DE367E08E0C5BE001C597D77A6BE` | Khớp 100% |
| `src/SelfStorageManagementSystem.Web/Controllers/PaymentsApiController.cs` | `CEB2FBC7C55CD047F72E078AC9DDD3F293034C1611F40FC49F9EE03DEDDEF5FB` | Khớp 100% |
| `src/SelfStorageManagementSystem.Web/Pages/Payments/Index.cshtml.cs` | `71A32BC871FB5333CF34F440CBCEB029B6D9E4A66AAB63A6068A1C92C66BA1EF` | Khớp 100% |
| `src/SelfStorageManagementSystem.Web/Pages/Payments/Index.cshtml` | `4598C16033E912A6396A029EFD2356F6100F85A0FD75881899B4E810880752A3` | Khớp 100% |
| `src/SelfStorageManagementSystem.Web/Pages/Payments/Details.cshtml` | `4E0FFA0DFD13FD21E6223DF661C5ECA3369B0BF8BF6DA5F442180A96139D1E40` | Khớp 100% |
| `src/SelfStorageManagementSystem.Web/Pages/Staff/CheckIn/Index.cshtml.cs` | `425C8D586041B0C218372B35C0221FF9898E7A5FD3EEDB0E72F9E3BEC1E5AFAF` | Khớp 100% |
| `src/SelfStorageManagementSystem.Web/Pages/Staff/CheckIn/Index.cshtml` | `3E355FDFD2610FC88AB6A98A4B9005032FDEABA7756A163E0A8F7990DB31C92A` | Khớp 100% |
| `src/SelfStorageManagementSystem.Web/Pages/Reservations/Create.cshtml.cs` | `DBF626313D17B1B4C76C15A23767FE2209822758DB26657AFF36C81F85A01B89` | Khớp 100% |
| `src/SelfStorageManagementSystem.Web/Pages/Reservations/Cancel.cshtml.cs` | `40F00F77D4FE25DC53F28EB11C3C1E69A5E33F1D9CC63B207AAA1F558C9D24EE` | Khớp 100% |
| `src/SelfStorageManagementSystem.Web/Pages/Reservations/Details.cshtml.cs` | `DFB9075FBE823240C88E62D41DABACCBE65591DAB23ED2782DAEFDA120506575` | Khớp 100% |
| `src/SelfStorageManagementSystem.Web/Pages/Catalog/Details.cshtml` | `D18F38E655ED2258952C49CD146A5E7B63878983496F0AE095D108A46DAF232A` | Khớp 100% |
| `src/SelfStorageManagementSystem.Web/Program.cs` | `B528549940C8F30DA2F499E2F73F425CA096826753628D253C9A32E0639A4B34` | Khớp 100% |
| `src/SelfStorageManagementSystem.Web/Views/Shared/_Layout.cshtml` | `E31DDDE5200FD08301893687CBC9B0B357E990F1FB8A365805573CC8B3000D2E` | Khớp 100% |
| `src/SelfStorageManagementSystem.Web/wwwroot/js/app-realtime.js` | `7646CFA568D42475D19B27BFA636B080042D58626C10A0DCB6EEEFFC4074DC9C` | Khớp 100% |
| `src/SelfStorageManagementSystem.Web/wwwroot/js/catalog.js` | `9EC711E77585655C4E6FE0671D8C6A59437FB27BC76014764912F5B8B1369C0C` | Khớp 100% |
| `src/SelfStorageManagementSystem.Web/wwwroot/js/staff-checkin.js` | `7C93C051F5BA04AA8C5BFEBA58A713BD2B0C9D0403689CFD37B997519B651129` | Khớp 100% |
| `src/SelfStorageManagementSystem.Web/wwwroot/js/my-units.js` | `080380DB0649E29CF9CFFE604B2EB7B4740B62E28E10A1851A063361E5678088` | Khớp 100% |
| `src/SelfStorageManagementSystem.Web/wwwroot/js/notifications.js` | `A030E5ADD043F07B2D3815B37BB1586B57ECC3A80674D49F27C1F671B5B60375` | Khớp 100% |
| `src/SelfStorageManagementSystem.Web/wwwroot/js/payments.js` | `546E021076F5D06E9DC20955CAFA34487328B722C58222380DE744C70C743E02` | Khớp 100% |
| `src/SelfStorageManagementSystem.Web/wwwroot/js/reservation-countdown.js` | `198CCAE30B1FFBEC0380AF79845F33801B123819455B1180874549F3167298E0` | Khớp 100% |
| `tests/SelfStorageManagementSystem.WebTests/WebRealtimeIntegrationTests.cs` | `65E1D7BECE29A98A9439B6FFD85A99384ED84A1F0DA02EA0A9B443D3AEF50373` | Khớp 100% |
| `tests/SelfStorageManagementSystem.WebTests/Infrastructure/KestrelRealtimeServer.cs` | `8174C781A4AD7BD697534DE3D1CC68EA7AC23A186A3EB66C3B3CB932A6FA00FB` | Khớp 100% |

---

## 5. Kết Luận Bàn Giao Vòng R3

Toàn bộ 4 findings (R2-F01 đến R2-F04) từ Codex review vòng R2 đã được khắc phục triệt để và kiểm chứng toàn diện bằng 26 bài test tích hợp realtime chuyên biệt cùng 400 bài test của toàn bộ giải pháp Web (đạt tỷ lệ thành công 100%). Ranh giới kiến trúc Clean Architecture được khôi phục nguyên bản (Web hoàn toàn không tham chiếu DataAccess). Cơ chế idempotent và race condition của VNPAY IPN được hoàn thiện vững chắc, không phát sinh bất kỳ sự kiện lặp hoặc sự kiện giả nào. Hai trang thanh toán và cơ chế đồng bộ canonical khi kết nối lại của chi tiết đặt chỗ đã được trang bị đầy đủ.

Toàn bộ 43 tệp mã nguồn, kiểm thử, script JavaScript và báo cáo đã được đồng bộ tuyệt đối giữa hai workspace `Final_Project_Web` và `Final_Project` (khớp mã băm SHA-256 100%). Trạng thái trên TaskBoard được duy trì ở `T15 IMPLEMENTED — chờ Codex review`.
