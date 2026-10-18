# T15 — prompt sửa finding R1, vòng R2 (chỉ Task T15)

Gemini A tiếp tục **đúng cuộc trò chuyện T15** trong workspace `D:\FPT\PRN222\Project\Final_Project_Web`. Codex đã review độc lập báo cáo `AgentReport\WebMigration_T15_R1_FindingFixes.md` (SHA-256 `3A380DEDA93DB3AAEC763A25DB46551DC5096F8B6FF1A02FF344CE69752187A1`). Kết luận: **chưa VERIFIED**. Sửa các finding dưới đây, giữ nguyên contract VERIFIED của T12/T13/T14, đặc biệt `/storageUnitHub`, `HubCreate_StorageUnit`, `HubUpdate_StorageUnit`, `HubDelete_StorageUnit` và ba `Receiver_*StorageUnit` CRUD events. Không khởi động T16–T18.

## F01 — P1: IPN phát trạng thái hoàn tất sai khi giao dịch bị từ chối

- **Vị trí:** `src/SelfStorageManagementSystem.Web/Controllers/PaymentsApiController.cs:60-69`; đường gây lỗi trong `src/SelfStorageManagementSystem.BusinessLogic/Services/Implementations/PaymentService.cs:479-496`.
- **Tái hiện:** VNPAY IPN có chữ ký hợp lệ nhưng `IsSuccess=false`. PaymentService commit trạng thái thất bại rồi trả `IpnResponseCode="00"` vì đây là mã ACK của IPN. Controller suy ra `paymentStatus="completed"` từ ACK, phát `PaymentChanged(completed)` và `ReservationChanged(confirmed)`. Callback trùng cho payment đã thành công trả ACK `02` lại phát `PaymentChanged(failed)` dù DB không thay đổi. IPN sai số tiền vẫn có thể phát sự kiện dù không commit.
- **Nguyên nhân:** Dùng mã phản hồi giao thức IPN để suy trạng thái nghiệp vụ và không phân biệt có chuyển trạng thái DB hay không.
- **Yêu cầu sửa:** Dựa vào kết quả commit/trạng thái thực của payment + reservation; chỉ phát sau transition đã commit; callback lỗi validation và idempotent không phát sự kiện sai. Trường hợp `reconciliation_required` phải phản ánh trạng thái này. Giữ nguyên response/ACK cần thiết cho gateway.
- **Nghiệm thu:** Test HTTP/IPN với success, decline, duplicate, invalid amount và reconciliation: trạng thái DB và payload cùng giá trị; không có `completed`/`confirmed` khi giao dịch thất bại; không có sự kiện giả cho callback không commit.

## F02 — P1: IPN và worker mất định danh reservation, worker phát cho mọi client

- **Vị trí:** `PaymentsApiController.cs:65-69`, `src/SelfStorageManagementSystem.Web/Workers/ReservationExpirationRealtimeHook.cs:26-42`, `src/SelfStorageManagementSystem.Web/Realtime/StorageUnitRealtimePublisher.cs:295-303`, `src/SelfStorageManagementSystem.Web/wwwroot/js/app-realtime.js:120-126`.
- **Tái hiện:** Worker hết hạn hai holds gọi hook chỉ với `expiredCount`; hook phát `ReservationChanged(reservationId=0,facilityId=0,customer=null,expired)`. Publisher dùng `Clients.All`, nên khách hàng không liên quan cũng nhận sự kiện; trang Details có `data-reservation-id` thật không áp dụng update ID 0. IPN success cũng phát `confirmed` với ID 0 cho tất cả kết nối.
- **Nguyên nhân:** Chỉ truyền số lượng/ACK qua ranh giới service, bỏ identity của từng reservation; fallback broadcast toàn cục cho stream có dữ liệu riêng tư.
- **Yêu cầu sửa:** Sau commit, truyền ID reservation thật, facility thật, owner thật cho từng thay đổi; không dùng ID 0 để thay một reservation; không phát `ReservationChanged` qua `Clients.All`. Nếu cần sửa tối thiểu interface BLL/repository của expiration để trả danh sách định danh đã commit, chỉ sửa phần phục vụ T15. Bảo toàn logic hết hạn 15 phút và không làm thay đổi transaction.
- **Nghiệm thu:** Test worker với ít nhất hai hold ở hai khách/cơ sở và một khách ngoài cuộc: mỗi chủ/nhân viên đúng phạm vi nhận ID/status đúng; khách ngoài cuộc không nhận; giao diện reservation cập nhật đúng bản ghi.

## F03 — P1: PaymentChanged từ IPN/refund không đến khách hàng sở hữu

- **Vị trí:** `PaymentsApiController.cs:65`, `src/SelfStorageManagementSystem.Web/Pages/Payments/Index.cshtml.cs:115-116`, `StorageUnitRealtimePublisher.cs:350-371`, `app-realtime.js:135-141`.
- **Tái hiện:** IPN hoặc review refund gọi publisher với `reservationId=0, facilityId=0, customerUserId=null`. Target group chỉ còn `admin_global`; khách hàng đang mở trang của reservation không nhận event. Với IPN, client cũng không thể match `data-reservation-id` vì payload là 0.
- **Nguyên nhân:** Call site không lấy thông tin sở hữu/cơ sở/reservation từ payment/refund đã commit.
- **Yêu cầu sửa:** Lấy metadata tin cậy từ DB/BLL sau commit, gửi đúng `paymentId`, `reservationId`, `facilityId`, `customerUserId`, trạng thái thực. Không suy owner từ query IPN hoặc tin đầu vào của browser.
- **Nghiệm thu:** Test callback/refund thật với customer, staff đúng facility, admin, khách không liên quan: người có quyền nhận event và client match reservation; khách khác không nhận.

## F04 — P2: CatalogInvalidated không đến khách đang xem Catalog

- **Vị trí:** `StorageUnitRealtimePublisher.cs:221-229`; `src/SelfStorageManagementSystem.Web/Realtime/StorageUnitHubScopeRevocationService.cs:193-206`; `src/SelfStorageManagementSystem.Web/wwwroot/js/catalog.js:218-258`.
- **Tái hiện:** Khách đã đăng nhập ở `/Catalog/Details` chỉ thuộc `user_{id}`. Tạo/hủy hold phát `CatalogInvalidated(facilityId>0)`, nhưng publisher chỉ gửi `facility_{id}` và `admin_global` (nhóm của staff/admin), nên khách không thấy cảnh báo/sync. Code hiện tại cũng không prune quyền staff trước khi phát vào nhóm facility.
- **Yêu cầu sửa:** Chọn phạm vi nhận phù hợp cho catalog public/khách hàng và kiểm tra quyền staff theo nguyên tắc fail-closed đã VERIFIED. Nếu broadcast rộng, payload chỉ được chứa metadata catalog public; không mở rộng phạm vi cho `ReservationChanged`/payment. Client cần lọc facility và lấy lại trạng thái chuẩn từ server.
- **Nghiệm thu:** Khách đang xem đúng facility nhận invalidation sau tạo/hủy hold; khách xem facility khác không bị cập nhật sai; staff bị thu hồi quyền không nhận metadata riêng theo scope; test trên kết nối SignalR thật.

## F05 — P2: NotificationsChanged dùng agreement ID thay notification ID

- **Vị trí:** `src/SelfStorageManagementSystem.Web/Pages/Staff/CheckIn/Index.cshtml.cs:419-424`. Bản ghi notification thực được tạo trong `src/SelfStorageManagementSystem.DataAccess/Repositories/Implementations/CheckInRepository.cs:1055-1085`.
- **Tái hiện:** CompleteCheckIn commit một `core.notifications` row, nhưng call site gọi `PublishNotificationsChangedAsync(customerId, result.AgreementId)`. Payload `notificationId` không phải `notification.id`, sai contract `docs/WebMigration_Contracts.md:662-665`.
- **Yêu cầu sửa:** Truyền ID thông báo đã commit thật qua BLL response hoặc lookup tin cậy theo khóa duy nhất; chỉ phát khi notification thực sự tồn tại. Giữ bảo mật PIN và quyền truy cập.
- **Nghiệm thu:** Test complete check-in qua PageModel/HTTP rồi đọc DB: `notificationId` từ SignalR bằng chính khóa của notification vừa commit, đúng owner; người khác không nhận.

## F06 — P2: Lỗi realtime sau commit bị nuốt, UI vẫn báo thành công đơn thuần

- **Vị trí đại diện:** `src/SelfStorageManagementSystem.Web/Pages/Reservations/Cancel.cshtml.cs:120-134`, `Reservations/Create.cshtml.cs:165-179`, `Staff/CheckIn/Index.cshtml.cs:413-432`, `Payments/Index.cshtml.cs:108-122`.
- **Tái hiện:** Inject publisher ném `RealtimePublishDegradedException` sau khi DB đã commit. Handler chỉ ghi log và hiển thị `SuccessMessage`, không có cảnh báo realtime suy giảm như điều kiện bàn giao R1. Ngoài ra catch chung có thể nuốt hủy request.
- **Yêu cầu sửa:** Giữ kết quả nghiệp vụ đã commit; UI hiển thị cảnh báo riêng rằng đồng bộ thời gian thực chậm/không khả dụng và gợi ý tải lại dữ liệu chuẩn, không gợi ý gửi lại POST. Phân biệt cancellation với lỗi transport hợp lý; áp dụng đồng nhất ở các call site T15 có response cho người dùng. IPN server-to-server chỉ cần log/đo lường, không đổi ACK sai.
- **Nghiệm thu:** Inject lỗi publisher vào thao tác đã commit, assert DB đúng, response/TempData vừa báo thành công nghiệp vụ vừa có cảnh báo suy giảm; không yêu cầu thao tác lại.

## F07 — P2: Reconnect không làm mới dữ liệu đã lỡ

- **Vị trí:** `src/SelfStorageManagementSystem.Web/wwwroot/js/app-realtime.js:171-180`; tìm toàn Web chỉ thấy nơi dispatch `app:realtime-reconnected`, không có listener tiêu thụ sự kiện này. `btnRetrySync` chỉ tồn tại ở trang Storage Units.
- **Tái hiện:** Mở reservation/payment/catalog/notifications, ngắt WebSocket, trạng thái DB thay đổi trong lúc mất kết nối rồi reconnect. App chỉ dispatch DOM event không có consumer và không refetch trang tương ứng; trạng thái cũ tiếp tục hiện. `catalog.js` chỉ đưa nút tải lại khi nhận live event, không refetch khi reconnect.
- **Yêu cầu sửa:** Sau reconnect, từng trang/luồng đang dùng realtime phải lấy lại trạng thái chuẩn từ server hoặc reload có kiểm soát phù hợp; không dựa vào event đã lỡ. Bảo toàn POST/PRG, tránh reload khi đang nhập form hoặc đang hiển thị PIN một lần.
- **Nghiệm thu:** Browser test làm mất kết nối, thay đổi DB, reconnect, xác nhận trạng thái mới hiển thị/được nhắc tải lại an toàn trên trang liên quan; không cần nhận live event cũ.

## Kiểm thử và bàn giao R2

1. Bổ sung test qua **đường gọi thực** (HTTP/PageModel/worker → commit → publisher → client), không chỉ gọi `IWebRealtimePublisher` trực tiếp. Giữ các test scoping/zero-PIN và ba CRUD Hub methods/events T13.
2. Chạy focused T15, toàn bộ WebTests, BLL/DAL regression thích hợp, `node --check` các JS thay đổi. Codex đã tự chạy R1: focused **12/12 pass**; toàn WebTests **385 pass/1 fail** ở `ReservationCountdown_BrowserEndToEnd_RealReloadFlowAndTwoFacilityTimezones_PreservesHoldDeadlineAndNoDuplicate`, nhưng chạy riêng bài này ngay sau đó **pass**. Theo dõi và báo rõ nếu lỗi full suite tái diễn; đừng tuyên bố toàn bộ pass nếu thực tế không pass.
3. Đồng bộ source/test/prompt/report/TaskBoard giữa `Final_Project_Web` và `Final_Project`; cung cấp SHA-256 mỗi tệp sửa và đối chiếu ở cả hai workspace.
4. Viết `AgentReport\WebMigration_T15_R2_FindingFixes.md` ở cả hai workspace: từng finding → mã sửa → test thực tế (lệnh, pass/fail/skip) → hạn chế còn lại → bảng SHA-256. TaskBoard để `T15 IMPLEMENTED — chờ Codex review`; **không tự đánh dấu VERIFIED**. Chỉ gửi `READY_FOR_CODEX_REVIEW` sau khi code, test, báo cáo và đồng bộ đều hoàn tất.
