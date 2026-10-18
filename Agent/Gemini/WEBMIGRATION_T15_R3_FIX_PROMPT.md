# T15 — prompt sửa finding R2, vòng R3 (chỉ Task T15)

Gemini A tiếp tục trong **đúng chat Antigravity T15** của `Final_Project_Web`. Codex đã review độc lập báo cáo `AgentReport/WebMigration_T15_R2_FindingFixes.md`, SHA-256 `443C524D01C8FBE76E6E91858ECDAF39FE2FBF1DD133C856A2B42ED300923868`. Kết luận **chưa VERIFIED**. T12/T13/T14 vẫn VERIFIED; giữ `/storageUnitHub`, ba Hub methods `HubCreate_StorageUnit`/`HubUpdate_StorageUnit`/`HubDelete_StorageUnit` và ba Receiver CRUD events. Không làm T16–T18.

## R2-F01 [P1] Khôi phục ranh giới kiến trúc Web → BLL → DAL

- **File/dòng:** `src/SelfStorageManagementSystem.Web/Workers/IReservationExpirationHook.cs:1,22-23`; `src/SelfStorageManagementSystem.BusinessLogic/Services/Interfaces/IReservationService.cs:35`; `src/SelfStorageManagementSystem.DataAccess/Repositories/Interfaces/IReservationRepository.cs:5`.
- **Tái hiện:** Build R2 và chạy hai test `FoundationIntegrationSmokeTests.Architecture_MvcWebApp_DoesNotReferenceDataAccessOrApiOrWpf` và `LandingPageAndBootstrapTests.MvcWebApp_DoesNotReferenceDataAccessOrApiOrWpf`: **cả hai fail**. R1 full WebTests có 386/386 pass, nên đây là hồi quy mới R2. Web worker/hook dùng trực tiếp `ExpiredReservationHoldInfo` khai báo trong namespace DAL, khiến Web assembly tham chiếu DAL và phá contract kiến trúc của `docs/WebMigration_Contracts.md:693-700`.
- **Nguyên nhân:** DTO truyền qua BLL và Web đặt trong tầng repository.
- **Hướng sửa:** Đặt contract DTO/record ở BLL (hoặc abstraction chung hợp lệ), map từ DAL ở BLL; Web chỉ phụ thuộc BLL. Không bỏ hoặc sửa yếu hai bài test kiến trúc.
- **Nghiệm thu:** Hai test kiến trúc và toàn WebTests pass; `typeof(Program).Assembly.GetReferencedAssemblies()` không có `SelfStorageManagementSystem.DataAccess`, PageModel/worker/hub không dùng namespace DAL. T10 worker expiration và event T15 vẫn hoạt động.

## R2-F02 [P1] Chỉ phát IPN event khi repository thực sự chuyển trạng thái

- **File/dòng:** `src/SelfStorageManagementSystem.BusinessLogic/Services/Implementations/PaymentService.cs:502-510,525-543`; `src/SelfStorageManagementSystem.DataAccess/Repositories/Implementations/PaymentRepository.cs:783-797,833-849,1243-1247`; `src/SelfStorageManagementSystem.Web/Controllers/PaymentsApiController.cs:60-85`.
- **Tái hiện 1:** Hai success IPN đồng thời cùng payment: cả hai đọc `pending` trước lock; lần đầu commit, lần hai vào `ExecuteFinalizePaymentTransactionAsync` và nhận `FinalizePaymentResult.IsAlreadyProcessed=true`. Service R2 bỏ qua cờ này và vẫn đặt `IsStatusTransitionCommitted=true`; controller phát event trùng. Nếu lần đầu là late payment cần đối soát, lần hai nhận `ReconciliationRequired=false` mặc định từ nhánh idempotent rồi phát sai `PaymentChanged(completed)` + `ReservationChanged(confirmed)` cho reservation đã expired.
- **Tái hiện 2:** Payment bị decline rồi callback decline lặp lại: service chỉ short circuit `status==succeeded`; repository trả `bool` cho `ExecuteFinalizeFailedPaymentTransactionAsync` nhưng service bỏ qua, luôn đặt `IsStatusTransitionCommitted=true`. Trong cuộc đua với success IPN, repository có thể trả `false` vì payment đã succeeded nhưng service vẫn phát `failed`.
- **Nguyên nhân:** BLL gán cờ committed dựa vào việc phương thức repository trả về bình thường, không dùng kết quả `IsAlreadyProcessed`/`bool` hoặc trạng thái sau lock.
- **Hướng sửa:** Propagate kết quả transition thực từ repository vào `PaymentGatewayCallbackResult`, phân biệt ACK với state và idempotent/race. Không phát false event khi không commit; trạng thái reconciliation từ DB phải được giữ đúng. Với failed IPN, kiểm soát lặp lại `failed` và success/failed race; không thay đổi hành vi tài chính đã VERIFIED T12 ngoài phần metadata/notification cần thiết.
- **Nghiệm thu:** Test BLL + HTTP callback cho duplicate success đồng thời, duplicate decline và success/decline race; assert DB, ACK, số lượng event, payload và các scope. Riêng reconciliation, không bao giờ phát `confirmed` nếu reservation đã expired.

## R2-F03 [P2] PaymentChanged không có receiver chức năng trên các trang Payment

- **File/dòng:** `src/SelfStorageManagementSystem.Web/wwwroot/js/app-realtime.js:130-143`; `src/SelfStorageManagementSystem.Web/Pages/Payments/Index.cshtml` (không có listener/script nhận `app:payment-changed`); `src/SelfStorageManagementSystem.Web/Pages/Payments/Details.cshtml`.
- **Tái hiện:** Nhân viên đang mở danh sách Payment hoặc khách xem Payment Details, IPN/refund cập nhật DB và `PaymentChanged` tới connection. App chỉ dispatch `app:payment-changed`; tìm toàn Web không có listener tiêu thụ DOM event trên các trang Payment. Bảng/trạng thái vẫn cũ, không có nút/cảnh báo đồng bộ. Bài test R2 chỉ xác nhận publisher/SignalR nhận payload, chưa kiểm DOM ở trang Payment.
- **Hướng sửa:** Trên các trang Payment liên quan, xử lý event đúng payment/reservation bằng cách refetch server hoặc thông báo tải lại an toàn. Sau reconnect cũng phải đồng bộ trạng thái chuẩn; không làm lộ dữ liệu thanh toán nhạy cảm trong payload/DOM.
- **Nghiệm thu:** Browser test mở Payment page trước, thay đổi payment bằng luồng thật ở client khác/IPN, trang nhận update hoặc hiện cảnh báo reload rõ ràng; sự kiện payment khác không cập nhật nhầm; reconnect sau missed event cũng đồng bộ.

## R2-F04 [P2] Reservation Details không đồng bộ ngay sau reconnect khi bỏ lỡ event

- **File/dòng:** `src/SelfStorageManagementSystem.Web/wwwroot/js/app-realtime.js:171-180`; `src/SelfStorageManagementSystem.Web/wwwroot/js/reservation-countdown.js:88-100,255-267`; `src/SelfStorageManagementSystem.Web/Pages/Reservations/Details.cshtml:85`.
- **Tái hiện:** Mở Details của hold còn 10 phút, ngắt SignalR, IPN xác nhận payment trong lúc ngắt rồi reconnect. `app-realtime.js` chỉ dispatch `app:realtime-reconnected` và bấm `btnRetrySync` (chỉ có ở Storage Units). `reservation-countdown.js` không lắng nghe reconnect và chỉ gọi status API khi đến deadline, nên trang vẫn hiển thị hold/nút thanh toán cho tới nhiều phút sau.
- **Hướng sửa:** Khi reconnect, refetch trạng thái reservation hiện hành bằng Status API đã có, áp dụng trạng thái chuẩn ngay; hỗ trợ cả Details và danh sách có nhiều `data-reservation-id`, bảo toàn logic T10 về 15 phút, timezone và 401/403.
- **Nghiệm thu:** Browser test disconnect → DB confirmed/cancelled/expired → reconnect trước deadline: trang cập nhật đúng trong thời gian hữu hạn mà không cần chờ deadline hoặc F5; không đổi nhầm reservation khác.

## Kiểm thử và bàn giao R3

Codex chạy độc lập R2: **20/20** test `WebRealtimeIntegrationTests` pass, **2/2** test kiến trúc fail, **89/89** Payment/Reservation/CheckIn BLL tests pass; bài `ReservationCountdown_ClientScript_ExecutesHeadlessBrowserTests_AllScenariosPass` chạy riêng **pass** trên máy này. Báo cáo R2 ghi browser test fail do thiếu Chromium nhưng test dùng Microsoft Edge (`ReservationRazorPagesAndWorkerTests.cs:1308-1311`) và Edge có mặt; ghi nguyên nhân chính xác theo log nếu full suite còn fail, không coi thiếu Chromium là bằng chứng.

Bổ sung test qua service/repository thật cho IPN transition; test browser cho Payment/Reservation reconnect và DOM, không chỉ mock `IPaymentService` hoặc gọi publisher trực tiếp. Chạy focused T15, hai test kiến trúc, toàn WebTests, BLL/DAL phù hợp và `node --check` JS sửa. Ghi pass/fail/skip thật. Đồng bộ tệp T15 và báo cáo giữa `Final_Project_Web` và `Final_Project`, đối chiếu SHA-256 từng tệp. Viết `AgentReport/WebMigration_T15_R3_FindingFixes.md` ở cả hai workspace, cập nhật TaskBoard `T15 IMPLEMENTED — chờ Codex review`, giữ T16–T18 WAITING, **không tự đánh dấu VERIFIED**. Chỉ gửi `READY_FOR_CODEX_REVIEW` sau khi code, test, báo cáo và đồng bộ hoàn tất.
