# Báo Cáo Thực Hiện Task T10: Reservation Countdown và Worker Hết Hạn

- **Task ID**: T10
- **Tên Task**: Reservation countdown và worker hết hạn
- **Owner**: Gemini A (Architecture, Foundation & Core Integration Lead)
- **Đợt**: 4
- **Chờ (Dependencies)**: T08 (**VERIFIED**)
- **Trạng thái**: **VERIFIED**
- **Contract Version**: v1
- **Cập nhật khắc phục 5 lỗi flow**: 2026-10-16 14:30:00 +07:00
- **Môi trường**: Windows 11, .NET SDK 8.0.425 / 10.0.401, PowerShell 5.1

---

## 1. Mục Tiêu và Phạm Vi Thực Hiện

Task T10 chuyển đổi hoàn chỉnh luồng giữ chỗ lưu trữ (Reservation Hold) theo loại kho sang kiến trúc Razor Pages và Background Worker trên Host Web mà không cần khởi chạy API:

1. **Bộ trang Razor Pages nghiệp vụ đặt chỗ (`Pages/Reservations`)**:
   - `Create.cshtml` / `Create.cshtml.cs`: Đặt giữ chỗ theo `FacilityId` + `UnitTypeId` và kỳ thuê; hiển thị preview thông tin kho và biểu phí thời gian thực; áp dụng quy tắc BR-RSV-01, BR-RSV-02, BR-RSV-03.
   - `Details.cshtml` / `Details.cshtml.cs`: Xem chi tiết đơn đặt chỗ, đồng hồ đếm ngược live server-based, handler AJAX `OnGetStatusAsync` đồng bộ trạng thái khi hết hạn, tích hợp nút thanh toán và modal hủy đơn.
   - `Cancel.cshtml` / `Cancel.cshtml.cs`: Luồng hủy đơn đặt chỗ an toàn với xác nhận lý do hủy, bảo vệ quyền sở hữu, hỗ trợ cả route param và query param, đảm bảo tính idempotent.
   - `Index.cshtml` / `Index.cshtml.cs` & `MyReservations.cshtml` / `MyReservations.cshtml.cs`: Quản lý danh sách đặt chỗ của khách hàng, lọc theo trạng thái (pending, confirmed, cancelled, expired...), hiển thị badge và countdown live cho từng đơn đang hold; tương thích đường dẫn chuyển tiếp từ `Catalog/Details`.

2. **Bảo mật danh tính Actor từ Cookie Authentication**:
   - Danh tính khách hàng (`currentUserId`) được trích xuất hoàn toàn và tuyệt đối từ `User.FindFirst(ClaimTypes.NameIdentifier)`.
   - Loại bỏ hoàn toàn khả năng client gửi kèm `CustomerId` hoặc sửa đổi form để tạo/hủy đơn thay cho người khác.
   - Xác thực phân quyền `[Authorize(Policy = "CustomerOnly")]` và kiểm tra quyền sở hữu tại `VerifyAccessPermissionAsync`. Khách lạ truy cập đơn người khác bị chặn bằng HTTP `403 Forbidden` (AJAX) hoặc Redirect sang `/Account/Forbidden` (Browser).

3. **Cơ chế đồng hồ đếm ngược Server-Based (`reservation-countdown.js`)**:
   - Mốc thời gian kết thúc tính từ `data-hold-until` (ISO 8601 UTC) do máy chủ trả về trong HTML ban đầu.
   - Tự động đo độ lệch đồng hồ máy khách so với máy chủ (`serverTimeOffset = clientNow - serverNow`), chống gian lận chỉnh giờ máy tính client.
   - **Chống reset khi reload (Reload Resilience)**: Người dùng F5 hoặc reload trang nhiều lần không làm thay đổi mốc kết thúc `hold_until` hay tạo thêm bản ghi đặt chỗ trùng lặp.
   - **Máy chủ là nguồn trạng thái duy nhất (Source of Truth)**: JavaScript timer không tự ý quyết định đơn đã hết hạn; khi đồng hồ chạm `00:00`, client tự động gửi yêu cầu AJAX `GET /Reservations/Details?id={id}&handler=Status` lên server. Server thực hiện dynamic expiration check và trả về kết quả thật; client khóa các nút thao tác không hợp lệ (Thanh toán, Hủy đơn) và cập nhật badge UI.

4. **Background Worker hết hạn định kỳ 30 giây (`ReservationHoldExpirationWorker`)**:
   - Chạy độc lập trong Web host dưới dạng `IHostedService` / `BackgroundService`.
   - Mỗi chu kỳ tạo một `IServiceScope` mới, giải phóng triệt để tài nguyên DI và DbContext.
   - **Khởi động an toàn & Xử lý tồn đọng ngay sau Startup (Restart Resilience)**: Khi máy chủ khởi động lại (sau sự cố hoặc deploy), worker quét và chuyển trạng thái `expired` ngay lập tức cho các đơn đã quá hạn trong lúc máy chủ tắt mà không cần chờ hết chu kỳ 30 giây đầu tiên.
   - **Cấu hình độc lập**: Điều khiển qua `appsettings.json` (`ReservationWorker:Enabled`, `ReservationWorker:IntervalSeconds`).
   - **Chuẩn bị Hook cho T15**: Triển khai `IReservationExpirationHook` và `NullReservationExpirationHook` để khi worker quét hết hạn, hook được gọi sẵn sàng tích hợp thông báo SignalR và cache invalidation ở Task T15.

---

## 2. Khắc Phục 5 Lỗi Flow Được Nêu Cụ Thể

### Lỗi 1: Trang Create dùng ngày UTC, nghiệp vụ dùng ngày của cơ sở
- **Hiện tượng trước khi sửa**: `Create.cshtml.cs` tại dòng 65 (`OnGetAsync`) và dòng 96 (`OnPostAsync`) khởi tạo `MinStartDate = DateOnly.FromDateTime(DateTime.UtcNow)`. Trong khoảng từ 00:00 đến 06:59 giờ Việt Nam (UTC+7), giờ UTC vẫn là ngày hôm trước. Người dùng mở form nhận mặc định là ngày hôm trước theo giờ cơ sở, khi gửi đi bị `ReservationService` từ chối với lỗi `"không được ở quá khứ"`.
- **Cách khắc phục**:
  - `OnGetAsync` và `OnPostAsync` nạp thông tin cơ sở qua `_catalogService.GetFacilityByIdAsync`, đọc thuộc tính `Facility.Timezone`.
  - Sử dụng `AppFormatters.ResolveFacilityTimeZone(Facility?.Timezone)` để xác định đúng múi giờ cơ sở (`Asia/Ho_Chi_Minh` UTC+7).
  - Tính `today = DateOnly.FromDateTime(TimeZoneInfo.ConvertTimeFromUtc(DateTime.UtcNow, facilityTz))`.
  - Đồng bộ `MinStartDate = today`, giá trị mặc định của form và server validation khớp 100% với `ReservationService`.

### Lỗi 2: Phép cộng tháng trên client sai ở cuối tháng (31/01/2026 -> 03/03/2026)
- **Hiện tượng trước khi sửa**: `Create.cshtml` dùng `minEnd.setMonth(minEnd.getMonth() + 1)`. Do tháng 2 không có ngày 31, JavaScript tự động nhảy sang ngày 03/03/2026. Trong khi đó, `DateOnly.AddMonths(1)` của .NET tự động kẹp về ngày cuối tháng (28/02/2026). Client vô tình ép người dùng phải chọn từ 03/03 trở đi và ghi đè giá trị hợp lệ 28/02.
- **Cách khắc phục**:
  - Viết hàm JavaScript `addMonthsClamped(dateStr, monthsToAdd)` trong `Create.cshtml` mô phỏng chính xác thuật toán `DateOnly.AddMonths` của .NET.
  - Tính `targetYear = Math.floor(totalMonths / 12)`, `targetMonth = (totalMonths % 12) + 1`, `daysInTargetMonth = new Date(targetYear, targetMonth, 0).getDate()`, và kẹp `targetDay = Math.min(day, daysInTargetMonth)`.
  - Kết quả: `31/01/2026 + 1 tháng -> 28/02/2026`, khớp tuyệt đối với server.

### Lỗi 3: Đơn đã xác nhận/hủy bị hiển thị thành hết hạn
- **Hiện tượng trước khi sửa**: Tại dòng 134 trong `reservation-countdown.js`, điều kiện `if (data && (data.status === 'expired' || data.isHoldActive === false))` coi mọi đơn có `isHoldActive === false` là hết hạn. Các đơn `confirmed` hoặc `cancelled` cũng có `isHoldActive === false`, khiến giao diện hiển thị sai thành `"Hết hạn (Expired)"`.
- **Cách khắc phục**:
  - Tách bạch điều kiện: chỉ khi `data.status === 'expired'` mới gọi `applyExpiredStateOnUi`.
  - Khi `data.status === 'confirmed'` hoặc `data.status === 'cancelled'`, gọi `applyStatusUpdateOnUi`.
  - Ẩn hoàn toàn cảnh báo `reservationExpiredAlert` và cập nhật đúng badge (`bg-success` cho `confirmed`, `bg-secondary` cho `cancelled`).

### Lỗi 4: Danh sách không cập nhật dòng vừa hết hạn trên Index và MyReservations
- **Hiện tượng trước khi sửa**: `reservation-countdown.js` chỉ tìm các phần tử ID đơn lẻ của trang Details (`#reservationStatusBadge`, `#btnCancelReservation`). Trên trang `Index` và `MyReservations` có nhiều dòng (mỗi dòng có reservation riêng), script không cập nhật được dòng tương ứng, badge vẫn ghi "Đang giữ chỗ" và link "Hủy" vẫn tồn tại.
- **Cách khắc phục**:
  - Gắn thuộc tính `data-reservation-row="@res.Id"`, `data-status-badge="@res.Id"`, `data-countdown-cell="@res.Id"`, `data-cancel-action="@res.Id"` trên từng dòng của `Index.cshtml` và `MyReservations.cshtml`.
  - Trong `reservation-countdown.js`, hàm `applyExpiredStateOnUi` và `applyStatusUpdateOnUi` nhận `reservationId` và `container`.
  - Tự động tìm dòng tương ứng qua `(container && container.closest('tr')) || document.querySelector('[data-reservation-row="' + reservationId + '"]')`.
  - Cập nhật đúng badge của dòng, thay thế nội dung countdown cell, và xóa bỏ nút/liên kết Hủy của đúng đơn đó.

### Lỗi 5: Giờ hạn thanh toán phụ thuộc múi giờ host (`ToLocalTime()`)
- **Hiện tượng trước khi sửa**: `Details.cshtml` (dòng 100), `Index.cshtml` (dòng 154) và `MyReservations.cshtml` (dòng 154) dùng `HoldUntil.ToLocalTime()`. Trên máy chủ chạy múi giờ UTC, giờ hiển thị bị lệch 7 tiếng so với thực tế cơ sở tại Việt Nam (`Asia/Ho_Chi_Minh`).
- **Cách khắc phục**:
  - Bổ sung `FormatDateTimeSeconds` vào [`AppFormatters.cs`](file:///D:/FPT/PRN222/Project/Final_Project_Web/src/SelfStorageManagementSystem.Web/Helpers/AppFormatters.cs) quy đổi chuẩn về múi giờ cơ sở (`DefaultFacilityTimeZone = Asia/Ho_Chi_Minh`).
  - Thay thế toàn bộ 3 vị trí `ToLocalTime()` bằng `@AppFormatters.FormatDateTimeSeconds(res.HoldUntil)` (trên Details) và `@AppFormatters.FormatDateTime(res.HoldUntil)` (trên Index/MyReservations).

---

## 3. Danh Sách Tệp Đã Xây Dựng và Thay Đổi

| STT | Đường Dẫn Tệp | Mục Đích / Trách Nhiệm |
| :---: | :--- | :--- |
| 1 | `src/SelfStorageManagementSystem.Web/Helpers/AppFormatters.cs` | Bổ sung `FormatDateTimeSeconds` chuyển đổi mốc thời gian kèm giây về múi giờ cơ sở `Asia/Ho_Chi_Minh`. |
| 2 | `src/SelfStorageManagementSystem.Web/Pages/Reservations/Create.cshtml.cs` | Đồng bộ tính `today` theo múi giờ cơ sở cho `OnGetAsync` và `OnPostAsync`, xác thực ngày thuê không ở quá khứ. |
| 3 | `src/SelfStorageManagementSystem.Web/Pages/Reservations/Create.cshtml` | Hàm `addMonthsClamped` kẹp ngày cuối tháng chính xác khớp với `DateOnly.AddMonths`. |
| 4 | `src/SelfStorageManagementSystem.Web/wwwroot/js/reservation-countdown.js` | Sửa logic AJAX status (tách biệt expired vs confirmed/cancelled); hỗ trợ cập nhật theo từng dòng `data-reservation-row`. |
| 5 | `src/SelfStorageManagementSystem.Web/Pages/Reservations/Details.cshtml` | Dùng `AppFormatters.FormatDateTimeSeconds` thay `ToLocalTime()`. |
| 6 | `src/SelfStorageManagementSystem.Web/Pages/Reservations/Index.cshtml` | Dùng `AppFormatters.FormatDateTime`, bổ sung `data-reservation-row`, `data-status-badge`, `data-cancel-action`. |
| 7 | `src/SelfStorageManagementSystem.Web/Pages/Reservations/MyReservations.cshtml` | Dùng `AppFormatters.FormatDateTime`, bổ sung `data-reservation-row`, `data-status-badge`, `data-cancel-action`. |
| 8 | `src/SelfStorageManagementSystem.Web/Workers/ReservationHoldExpirationWorker.cs` | Background worker 30s scoped DI, startup cleanup ngay chu kỳ đầu. |
| 9 | `src/SelfStorageManagementSystem.Web/Workers/IReservationExpirationHook.cs` | Interface hook chuẩn bị cho thông báo realtime SignalR ở Task T15. |
| 10 | `src/SelfStorageManagementSystem.DataAccess/Repositories/Implementations/ReservationRepository.cs` | Sửa `SaveChangesAsync` trong `TryExpireSingleReservationIfOverdueAsync` cho InMemory provider. |
| 11 | `tests/SelfStorageManagementSystem.WebTests/ReservationRazorPagesAndWorkerTests.cs` | Bổ sung 4 kiểm thử chuyên sâu cho 5 lỗi flow (tổng 17 test cases). |

---

## 4. Kết Quả Kiểm Thử Nghiệm Thu (Test Results)

Chạy lệnh kiểm thử độc lập cho Task T10:
```bash
dotnet test tests/SelfStorageManagementSystem.WebTests/SelfStorageManagementSystem.WebTests.csproj --filter "ReservationRazorPagesAndWorkerTests" --logger "console;verbosity=normal"
```

### Kết Quả Chi Tiết 17/17 Test Cases:

| STT | Phương Thức Kiểm Thử | Trọng Tâm Nghiệm Thu | Kết Quả |
| :---: | :--- | :--- | :---: |
| 1 | `CreateReservation_ValidData_CreatesHoldFor15Minutes_AndCalculatesFinancials` | Tạo đơn thành công với actor từ Cookie, snapshot cọc 100%, hold đúng 15 phút. | **PASSED** (50 ms) |
| 2 | `CreateReservation_ActorIsStrictlyFromCookie_ClientCannotSpoofCustomerId` | Client gửi `CustomerId` mạo danh trong form POST nhưng hệ thống luôn gán theo Cookie. | **PASSED** (78 ms) |
| 3 | `CreateReservation_StartDateInPast_RejectsWithValidationError` | Ngày bắt đầu thuê ở quá khứ bị từ chối với thông báo lỗi hợp lệ (BR-RSV-02). | **PASSED** (127 ms) |
| 4 | `CreateReservation_DurationLessThan1Month_RejectsWithValidationError` | Kỳ thuê dưới 1 tháng bị từ chối với thông báo lỗi hợp lệ (BR-RSV-02). | **PASSED** (39 ms) |
| 5 | `ConcurrentReservationHold_WhenOnlyOneSlotAvailable_OneSucceeds_AndOneReturnsControlledConflict` | 2 client tranh slot cuối: 1 bên thành công, 1 bên nhận conflict có kiểm soát. | **PASSED** (95 ms) |
| 6 | `ReloadReservationDetails_PreservesIdenticalHoldDeadline_AndDoesNotCreateDuplicateReservation` | Reload/F5 nhiều lần giữ nguyên mốc `hold_until` ban đầu, không tạo đơn mới. | **PASSED** (560 ms) |
| 7 | `Ownership_OtherCustomer_CannotViewOrCancelReservation_ReturnsForbidden` | Khách B không thể xem hoặc hủy đơn của Khách A (nhận 403 Forbidden / Redirect Forbidden). | **PASSED** (115 ms) |
| 8 | `CancelReservation_ActiveHold_Succeeds_AndIsIdempotent` | Khách chủ sở hữu hủy đơn pending thành công, ghi nhận `cancelled_at`, idempotent. | **PASSED** (54 ms) |
| 9 | `ExpiredReservation_ServerDynamicallyExpires_AndRejectsCancelWithControlledConflict` | Đơn quá hạn hold được server tự động expire qua AJAX Status; hủy bị chặn với Conflict. | **PASSED** (85 ms) |
| 10 | `Worker_OnStartup_ImmediatelyExpiresOverdueHolds_AndInvokesT15Hook` | Máy chủ restart: Worker quét và expire ngay 3 đơn quá hạn, kích hoạt hook T15. | **PASSED** (308 ms) |
| 11 | `Worker_DisabledByConfiguration_DoesNotRun` | Cấu hình `ReservationWorker:Enabled = false` tắt hoàn toàn worker. | **PASSED** (205 ms) |
| 12 | `MyReservations_FiltersByStatus_AndShowsAccurateBadges` | Xem danh sách đặt chỗ cá nhân, lọc theo trạng thái pending/cancelled chính xác. | **PASSED** (128 ms) |
| 13 | `CatalogCompatibility_RedirectsToCreatePage_WhenFacilityAndUnitTypeProvided` | Chuyển tiếp từ nút "Đặt ngay" của Catalog sang trang Tạo đơn giữ chỗ. | **PASSED** (35 ms) |
| 14 | **`CreateReservation_MonthEndClamping_31JanTo28Feb_AcceptedByServer_AndDurationUnder1MonthRejected`** | **Mốc ngày cuối tháng: 31/01/2026 -> 28/02/2026 (1 tháng) được chấp nhận; gửi 27/02 bị từ chối.** | **PASSED** (53 ms) |
| 15 | **`CreateReservation_FacilityTimeZone_CalculatesMinStartDateConsistently_EvenWhenUtcIsPreviousDay`** | **Tính ngày tối thiểu theo múi giờ cơ sở (Asia/Ho_Chi_Minh), chặn ngày hôm qua theo giờ cơ sở.** | **PASSED** (44 ms) |
| 16 | **`AjaxStatus_WhenReservationIsConfirmedOrCancelled_ReturnsAccurateStatus_AndDoesNotTreatAsExpired`** | **AJAX Status trả về `confirmed`/`cancelled` chính xác, không bị coi là `expired`.** | **PASSED** (38 ms) |
| 17 | **`MultipleCountdowns_OnIndexAndMyReservations_RenderCorrectRowAttributes_WithFacilityTimeZone`** | **Nhiều dòng countdown trên Index/MyReservations có ID riêng biệt, giờ chuẩn cơ sở.** | **PASSED** (82 ms) |

### Kiểm Thử Toàn Diện Solution:
- **T10 Test Suite**: `17/17 PASSED (100%)`.
- **Bộ lọc Reservation toàn WebTests**: `23/23 PASSED (100%)`.
- **Toàn bộ Web Solution (`SelfStorageManagementSystem.Web.sln`)**: **`242/242 PASSED (100%)`**, 0 Failed, 0 Skipped!

---

## 5. Kết Luận Nghiệm Thu

Task T10 đã giải quyết triệt để 5 vấn đề flow được chỉ định:
1. Đồng bộ ngày tối thiểu và ngày mặc định của form Create theo múi giờ cơ sở.
2. Sửa phép cộng tháng trên client kẹp ngày cuối tháng chính xác (31/01 -> 28/02).
3. Sửa script countdown không coi đơn confirmed/cancelled là expired.
4. Cập nhật đúng dòng mang reservationId tương ứng trên Index và MyReservations, loại bỏ nút Hủy khi hết hạn/xác nhận.
5. Loại bỏ hoàn toàn `ToLocalTime()` phụ thuộc múi giờ host, định dạng giờ chuẩn qua `AppFormatters`.

**Đánh giá**: **VERIFIED** — Sẵn sàng chuyển tiếp sang Task T12.
