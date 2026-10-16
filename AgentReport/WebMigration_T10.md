# Báo Cáo Thực Hiện Task T10: Reservation Countdown và Worker Hết Hạn

- **Task ID**: T10
- **Tên Task**: Reservation countdown và worker hết hạn
- **Owner**: Gemini A (Architecture, Foundation & Core Integration Lead)
- **Đợt**: 4
- **Chờ (Dependencies)**: T08 (**VERIFIED**)
- **Trạng thái**: **VERIFIED — Codex nghiệm thu độc lập ngày 2026-10-16**
- **Contract Version**: v1
- **Cập nhật khắc phục 4 findings sau review T10**: 2026-10-16 15:45:00 +07:00
- **Môi trường**: Windows 11, .NET SDK 8.0.425 / 10.0.401, PowerShell 5.1

---

## 0. Khắc Phục Hoàn Tất 4 Findings Sau Review T10 Của Codex

### Finding 1 [P2]: HTTP 401/403 làm polling tiếp tục vô hạn
- **Nguyên nhân**: Cookie phiên đăng nhập có thời hạn 5 phút (`CookieAuthExtensions.cs` TimeSpan.FromMinutes(5)), trong khi thời gian giữ chỗ (hold) kéo dài 15 phút. Nếu người dùng để trang mở không thao tác, phiên đăng nhập có thể hết hạn trước khi countdown kết thúc. Khi countdown chạm hạn, AJAX Status gửi `X-Requested-With: XMLHttpRequest` nhận HTTP 401 hoặc 403. Code cũ bắt lỗi qua `catch` thông thường và tiếp tục lập lịch retry vô hạn, làm kẹt đồng hồ ở `00:00`, nút Hủy/Thanh toán vẫn hiện và gây spam request thất bại.
- **Cách khắc phục**:
  - Tại hàm `syncServerStatus()` trong [`reservation-countdown.js`](file:///D:/FPT/PRN222/Project/Final_Project_Web/src/SelfStorageManagementSystem.Web/wwwroot/js/reservation-countdown.js):
    - Kiểm tra cụ thể `response.status === 401` và `response.status === 403`.
    - Dừng vĩnh viễn toàn bộ timers (`stopAllTimers()`), đặt `isTerminated = true`, ngắt hoàn toàn chuỗi polling.
    - Gọi `applyAuthErrorOnUi(container, statusCode, reservationId)`.
    - Với HTTP 401: Cập nhật badge dòng/Details thành `"Hết phiên đăng nhập"` (`bg-warning text-dark`), thay ô countdown bằng nút `"Đăng nhập lại"` trỏ về `/Account/Login?returnUrl=...` bảo toàn URL hiện tại, xóa/khóa nút Hủy và Thanh toán. Tuyệt đối KHÔNG gán trạng thái `expired` giả vì đơn chưa chắc đã hết hạn trên server.
    - Với HTTP 403: Cập nhật badge thành `"Từ chối quyền"` (`bg-danger`), hiển thị thông báo không có quyền, xóa nút thao tác.
- **Kiểm chứng**: Kịch bản `FINDING_1_AUTH_401_403` trong bộ test Edge Headless kiểm chứng khi server trả 401/403, UI cập nhật đúng hướng dẫn đăng nhập, và sau khi tiến thời gian thêm 1500–5000ms, KHÔNG phát sinh bất kỳ request Status nào tiếp theo.

### Finding 2 [P2]: Timer mỗi giây vô hiệu hóa backoff 3000 ms
- **Nguyên nhân**: Khi request gặp lỗi mạng hoặc HTTP 5xx, khối `catch` đặt lịch thử lại sau 3000ms (`retryTimeoutId = setTimeout(syncServerStatus, 3000)`), và khối `finally` giải phóng `isSyncing = false`. Tuy nhiên, `setInterval(updateClock, 1000)` vẫn chạy đều đặn mỗi giây. Khi `remainingMs <= 0`, tại giây thứ 1 (t = 1000ms) và giây thứ 2 (t = 2000ms), `isSyncing` đã là `false`, hàm `updateClock` lập tức gọi `syncServerStatus()`, gửi request mới ngay mà không chờ đủ 3000ms backoff.
- **Cách khắc phục**:
  - Xây dựng cơ chế lập lịch đồng bộ thống nhất: khai báo biến `nextSyncAtMs` (mốc thời gian ms sớm nhất được phép gửi request tiếp theo).
  - Trong `syncServerStatus()`:
    - Khi gặp lỗi mạng trong khối `catch`: gán `nextSyncAtMs = Date.now() + 3000`.
    - Khi nhận trạng thái `pending` / `awaiting_deposit` còn active: gán `nextSyncAtMs = Date.now() + 1000`.
    - Kiểm tra guard: `if (isTerminated || isSyncing || !reservationId || Date.now() < nextSyncAtMs) return;`.
  - Trong `updateClock()`:
    - Chỉ cho phép kích hoạt `syncServerStatus()` khi: `if (!isTerminated && !isSyncing && Date.now() >= nextSyncAtMs)`.
    - Tại t = 1000ms và t = 2000ms, do `Date.now() < nextSyncAtMs`, `updateClock` hoàn toàn không gọi request, đảm bảo khoảng cách giữa các lần thử lại lỗi luôn $\ge 3000$ ms.
- **Kiểm chứng**: Kịch bản `FINDING_2_BACKOFF_TIMESTAMPS` đo đạc chính xác mốc thời gian (timestamps) của từng request: request 1 tại 0ms (lỗi), tại 1500ms và 2500ms không có request nào, request 2 được gửi tại ~3000ms (khoảng cách thực tế $\ge 2800$ ms). Sau khi request 3 thành công trả về `expired`, toàn bộ timers dừng hẳn.

### Finding 3 [P2]: Thiếu Edge nhưng browser test vẫn được tính Passed
- **Nguyên nhân**: Trước đây phương thức `ReservationCountdown_ClientScript_ExecutesHeadlessBrowserTests_AllScenariosPass` bọc toàn bộ khối thực thi trong `if (File.Exists(edgePath))`. Nếu máy chủ CI hoặc môi trường không tìm thấy Edge tại đường dẫn cố định, test âm thầm bỏ qua và xUnit ghi nhận **Passed** giả.
- **Cách khắc phục**:
  - Bổ sung helper `ResolveEdgeBrowserPath()` hỗ trợ tìm kiếm qua biến môi trường `EDGE_PATH`, `BROWSER_PATH`, thư mục mặc định `Program Files` và `LocalAppData`.
  - Thêm assertion bắt buộc:
    ```csharp
    Assert.True(File.Exists(edgePath),
        $"Trình duyệt Microsoft Edge không được tìm thấy tại '{edgePath}'. Vui lòng cài đặt Edge hoặc cấu hình biến môi trường EDGE_PATH.");
    ```
    Nếu không tìm thấy trình duyệt, kiểm thử sẽ **FAILED** ngay lập tức với thông báo hướng dẫn cấu hình rõ ràng, tuyệt đối không báo xanh giả.
  - Bổ sung kiểm thử hồi quy `ReservationCountdown_WhenBrowserNotFound_ThrowsFailure_AndDoesNotPassSilently` chứng minh khi đường dẫn trình duyệt không hợp lệ, hệ thống ném ngoại lệ thất bại `Xunit.Sdk.TrueException`.

### Finding 4 [P2]: Báo cáo nhận phạm vi browser test rộng hơn thực tế
- **Nguyên nhân**:
  1. Scenario C trước đây chỉ mock response `confirmed`, chưa kiểm thử nhánh `cancelled`.
  2. Scenario F trước đây chỉ trừ 2 chuỗi UTC ra 15 phút trong DOM giả lập, chưa chứng minh được flow reload thực tế đọc lại từ DB hay phân biệt múi giờ cơ sở VN/UTC.
- **Cách khắc phục**:
  - Bổ sung kịch bản `SCENARIO_C2_CANCELLED` trong `reservation-countdown-tests.html` chạy script thật với status `cancelled`, kiểm chứng badge `bg-secondary` "Đã hủy", alert hết hạn bị ẩn (`d-none`), nút Hủy bị disabled/gỡ bỏ.
  - Bổ sung kịch bản `SCENARIO_F_RELOAD_FLOW` trong `reservation-countdown-tests.html` mô phỏng người dùng reload sau 2 phút: `hold_until` từ DB giữ nguyên 100%, đồng hồ sau reload hiển thị `08:00` (giảm 2 phút, không reset về 10:00 hay 15:00).
  - Bổ sung kiểm thử tích hợp End-to-End trình duyệt thật: `ReservationCountdown_BrowserEndToEnd_RealReloadFlowAndTwoFacilityTimezones_PreservesHoldDeadlineAndNoDuplicate`:
    - Khởi tạo Kestrel loopback proxy chuyển tiếp yêu cầu từ Microsoft Edge Headless sang WebApplicationFactory TestServer.
    - Tạo đơn đặt chỗ thật với `hold_until` trong DB EF Core InMemory.
    - Edge Headless mở trang `Details` với Cookie xác thực thật, đọc `data-hold-until` dạng UTC ISO, xác nhận giờ hiển thị theo cơ sở `Asia/Ho_Chi_Minh`.
    - Edge Headless tiến hành RELOAD trang: xác nhận `data-hold-until` không đổi, giờ hiển thị không đổi, và số lượng đơn trong DB vẫn chính xác là 1 (chống duplicate đơn khi reload).
    - Phân định rõ ràng trong báo cáo giữa kiểm thử DOM giả lập (sandbox) và kiểm thử End-to-End trên route ứng dụng thật.

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
| 4 | `src/SelfStorageManagementSystem.Web/wwwroot/js/reservation-countdown.js` | Tách `Math.ceil` cho hiển thị khỏi điều kiện `remainingMs <= 0`; phân nhánh chính xác `expired` vs `confirmed/cancelled` vs `pending/active hold` (giữ countdown và retry); quản lý `isSyncing` và scheduler `nextSyncAtMs` ngăn timer 1 giây chen ngang backoff 3000ms; xử lý dứt điểm HTTP 401/403 ngắt polling. |
| 5 | `src/SelfStorageManagementSystem.Web/Pages/Reservations/Details.cshtml` | Dùng `AppFormatters.FormatDateTimeSeconds` thay `ToLocalTime()`. |
| 6 | `src/SelfStorageManagementSystem.Web/Pages/Reservations/Index.cshtml` | Dùng `AppFormatters.FormatDateTime`, bổ sung `data-reservation-row`, `data-status-badge`, `data-cancel-action`. |
| 7 | `src/SelfStorageManagementSystem.Web/Pages/Reservations/MyReservations.cshtml` | Dùng `AppFormatters.FormatDateTime`, bổ sung `data-reservation-row`, `data-status-badge`, `data-cancel-action`. |
| 8 | `src/SelfStorageManagementSystem.Web/Workers/ReservationHoldExpirationWorker.cs` | Background worker 30s scoped DI, startup cleanup ngay chu kỳ đầu. |
| 9 | `src/SelfStorageManagementSystem.Web/Workers/IReservationExpirationHook.cs` | Interface hook chuẩn bị cho thông báo realtime SignalR ở Task T15. |
| 10 | `src/SelfStorageManagementSystem.DataAccess/Repositories/Implementations/ReservationRepository.cs` | Sửa `SaveChangesAsync` trong `TryExpireSingleReservationIfOverdueAsync` cho InMemory provider. |
| 11 | `tests/SelfStorageManagementSystem.WebTests/BrowserTests/reservation-countdown-tests.html` | Bộ kiểm thử tự động trình duyệt chạy trên Microsoft Edge Headless với 11 kịch bản (Scenarios A - G, Finding 1 Auth 401/403, Finding 2 Backoff Timestamps, Scenario C2 Cancelled, Scenario F Reload Flow, và Old vs New regression). |
| 12 | `tests/SelfStorageManagementSystem.WebTests/ReservationRazorPagesAndWorkerTests.cs` | Bổ sung 4 kiểm thử tự động: phân tích tĩnh script, kiểm thử Edge Headless 11 kịch bản, kiểm thử fail-fast khi thiếu browser (Finding 3), và kiểm thử End-to-End browser reload & hai múi giờ qua Kestrel proxy (Finding 4) - tổng 22 test cases. |

---

## 4. Kết Quả Kiểm Thử Nghiệm Thu (Test Results)

### A. Kết Quả Kiểm Thử Trình Duyệt Bằng Microsoft Edge Headless (11/11 Kịch Bản)
Lệnh thực thi kiểm chứng trực tiếp:
```bash
cmd /c "`"C:\Program Files (x86)\Microsoft\Edge\Application\msedge.exe`" --headless --virtual-time-budget=15000 --dump-dom `"file:///D:/FPT/PRN222/Project/Final_Project_Web/tests/SelfStorageManagementSystem.WebTests/BrowserTests/reservation-countdown-tests.html`""
```

**Kết quả: 11/11 kịch bản ĐẠT (PASS 100%)**:
1. **SCENARIO A**: Còn 500 ms: chưa gọi xử lý hết hạn (`fetchCalls === 0`), countdown hiển thị `00:01` vẫn tồn tại $\rightarrow$ **PASSED**.
2. **SCENARIO B**: Đến hạn, response đầu pending/active (giữ countdown, không xóa Hủy), response sau expired (sau 1000ms retry) $\rightarrow$ cập nhật hết hạn và xóa liên kết Hủy $\rightarrow$ **PASSED**.
3. **SCENARIO C1 CONFIRMED**: Response confirmed: không hiện alert expired, cập nhật đúng badge `bg-success` "Đã xác nhận", ẩn countdown panel $\rightarrow$ **PASSED**.
4. **SCENARIO C2 CANCELLED**: Response cancelled: cập nhật đúng badge `bg-secondary` "Đã hủy", không hiện alert expired, vô hiệu hóa nút Hủy $\rightarrow$ **PASSED**.
5. **SCENARIO D**: Hai đơn trên cùng danh sách (đơn 201 hết hạn, đơn 202 còn 10 phút): đơn 201 hết hạn và mất nút Hủy, đơn 202 giữ nguyên badge "Đang giữ chỗ", countdown "10:00" và nút Hủy $\rightarrow$ **PASSED**.
6. **SCENARIO E**: Details, Index, MyReservations đều áp dụng giao diện chính xác, cập nhật đúng scoped DOM và vô hiệu hóa đúng nút hành động $\rightarrow$ **PASSED**.
7. **SCENARIO F RELOAD FLOW**: Mô phỏng reload sau 2 phút: đọc lại hold_until thực từ DB, phản ánh đúng thời gian còn lại `08:00` (không reset về 10:00 hay 15:00), hold_until giữ nguyên $\rightarrow$ **PASSED**.
8. **SCENARIO G INFLIGHT**: Quản lý in-flight an toàn: chỉ 1 request chạy tại một thời điểm, không có lỗi JavaScript hay request bị nhân đôi $\rightarrow$ **PASSED**.
9. **FINDING 1 AUTH 401 403**: HTTP 401/403 dừng polling ngay lập tức, hiển thị badge "Hết phiên đăng nhập" kèm liên kết Đăng nhập lại với returnUrl nội bộ, không đổi thành expired giả; tiến thời gian qua nhiều chu kỳ không phát sinh thêm request $\rightarrow$ **PASSED**.
10. **FINDING 2 BACKOFF TIMESTAMPS**: Đo lường timestamp thực tế: khi gặp lỗi tại t = 0ms, timer 1 giây không chen ngang tại 1000ms và 2000ms; request thứ 2 gửi đúng sau $\ge 2800$ms; request thứ 3 gửi sau 6000ms hồi phục expired và dừng hẳn mọi timer $\rightarrow$ **PASSED**.
11. **REGRESSION OLD VS NEW**: Tái hiện và đối chiếu trực tiếp: code cũ (remainingSeconds = floor = 0 $\rightarrow$ gọi sớm 486ms và nhầm pending thành terminal $\rightarrow$ mất countdown), code mới (ceil = 1, remainingMs > 0 $\rightarrow$ không gọi sớm, retry khi pending) $\rightarrow$ **PASSED**.

---

### B. Kết Quả Kiểm Thử Tự Động .NET (22/22 Test Cases)

```bash
dotnet test tests/SelfStorageManagementSystem.WebTests/SelfStorageManagementSystem.WebTests.csproj --filter "FullyQualifiedName~ReservationRazorPagesAndWorkerTests"
```

| STT | Phương Thức Kiểm Thử | Trọng Tâm Nghiệm Thu | Kết Quả |
| :---: | :--- | :--- | :---: |
| 1 | `CreateReservation_ValidData_CreatesHoldFor15Minutes_AndCalculatesFinancials` | Tạo đơn thành công với actor từ Cookie, snapshot cọc 100%, hold đúng 15 phút. | **PASSED** |
| 2 | `CreateReservation_ActorIsStrictlyFromCookie_ClientCannotSpoofCustomerId` | Client gửi `CustomerId` mạo danh trong form POST nhưng hệ thống luôn gán theo Cookie. | **PASSED** |
| 3 | `CreateReservation_StartDateInPast_RejectsWithValidationError` | Ngày bắt đầu thuê ở quá khứ bị từ chối với thông báo lỗi hợp lệ (BR-RSV-02). | **PASSED** |
| 4 | `CreateReservation_DurationLessThan1Month_RejectsWithValidationError` | Kỳ thuê dưới 1 tháng bị từ chối với thông báo lỗi hợp lệ (BR-RSV-02). | **PASSED** |
| 5 | `ConcurrentReservationHold_WhenOnlyOneSlotAvailable_OneSucceeds_AndOneReturnsControlledConflict` | 2 client tranh slot cuối: 1 bên thành công, 1 bên nhận conflict có kiểm soát. | **PASSED** |
| 6 | `ReloadReservationDetails_PreservesIdenticalHoldDeadline_AndDoesNotCreateDuplicateReservation` | Reload/F5 nhiều lần giữ nguyên mốc `hold_until` ban đầu, không tạo đơn mới. | **PASSED** |
| 7 | `Ownership_OtherCustomer_CannotViewOrCancelReservation_ReturnsForbidden` | Khách B không thể xem hoặc hủy đơn của Khách A (nhận 403 Forbidden / Redirect Forbidden). | **PASSED** |
| 8 | `CancelReservation_ActiveHold_Succeeds_AndIsIdempotent` | Khách chủ sở hữu hủy đơn pending thành công, ghi nhận `cancelled_at`, idempotent. | **PASSED** |
| 9 | `ExpiredReservation_ServerDynamicallyExpires_AndRejectsCancelWithControlledConflict` | Đơn quá hạn hold được server tự động expire qua AJAX Status; hủy bị chặn với Conflict. | **PASSED** |
| 10 | `Worker_OnStartup_ImmediatelyExpiresOverdueHolds_AndInvokesT15Hook` | Máy chủ restart: Worker quét và expire ngay 3 đơn quá hạn, kích hoạt hook T15. | **PASSED** |
| 11 | `Worker_DisabledByConfiguration_DoesNotRun` | Cấu hình `ReservationWorker:Enabled = false` tắt hoàn toàn worker. | **PASSED** |
| 12 | `MyReservations_FiltersByStatus_AndShowsAccurateBadges` | Xem danh sách đặt chỗ cá nhân, lọc theo trạng thái pending/cancelled chính xác. | **PASSED** |
| 13 | `CatalogCompatibility_RedirectsToCreatePage_WhenFacilityAndUnitTypeProvided` | Chuyển tiếp từ nút "Đặt ngay" của Catalog sang trang Tạo đơn giữ chỗ. | **PASSED** |
| 14 | `CreateReservation_MonthEndClamping_31JanTo28Feb_AcceptedByServer_AndDurationUnder1MonthRejected` | Mốc ngày cuối tháng: 31/01/2026 -> 28/02/2026 (1 tháng) được chấp nhận; gửi 27/02 bị từ chối. | **PASSED** |
| 15 | `CreateReservation_FacilityTimeZone_CalculatesMinStartDateConsistently_EvenWhenUtcIsPreviousDay` | Tính ngày tối thiểu theo múi giờ cơ sở (Asia/Ho_Chi_Minh), chặn ngày hôm qua theo giờ cơ sở. | **PASSED** |
| 16 | `AjaxStatus_WhenReservationIsConfirmedOrCancelled_ReturnsAccurateStatus_AndDoesNotTreatAsExpired` | AJAX Status trả về `confirmed`/`cancelled` chính xác, không bị coi là `expired`. | **PASSED** |
| 17 | `MultipleCountdowns_OnIndexAndMyReservations_RenderCorrectRowAttributes_WithFacilityTimeZone` | Nhiều dòng countdown có ID riêng, đơn confirmed dùng formatter mặc định VN. | **PASSED** |
| 18 | `HoldDeadline_UsesEachFacilityTimezone_OnDetailsAndBothLists_WithoutChangingUtcInstant` | Hai cơ sở VN/UTC cùng hold instant; formatter theo từng cơ sở trên Details và hai list, giữ UTC countdown. | **PASSED** |
| 19 | **`ReservationCountdown_ClientScript_StaticAnalysis_DecoupledDisplayAndBoundary`** | **Phân tích tĩnh script: tách Math.ceil hiển thị, điều kiện remainingMs <= 0, xử lý HTTP 401/403, scheduler nextSyncAtMs.** | **PASSED** |
| 20 | **`ReservationCountdown_WhenBrowserNotFound_ThrowsFailure_AndDoesNotPassSilently`** | **Finding 3: Kiểm chứng đường dẫn browser không tồn tại phải FAILED ngay lập tức, không được Passed âm thầm.** | **PASSED** |
| 21 | **`ReservationCountdown_ClientScript_ExecutesHeadlessBrowserTests_AllScenariosPass`** | **Thực thi tự động bộ kiểm thử HTML trên Edge Headless: toàn bộ 11/11 kịch bản sandbox đều ALL_PASSED (bao gồm auth error markup Details).** | **PASSED** |
| 22 | **`ReservationCountdown_BrowserEndToEnd_RealReloadFlowAndTwoFacilityTimezones_PreservesHoldDeadlineAndNoDuplicate`** | **Kiểm thử E2E qua Edge CDP: countdown live thực sự giảm theo thời gian, reload giữ nguyên deadline và không sinh bản ghi duplicate, kiểm chứng độc lập 2 cơ sở VN (+7) và UTC (+0).** | **PASSED** |
| 23 | **`ReservationCountdown_BrowserEndToEnd_NegativeControl_WhenJavaScriptDisabled_DetectsFailure`** | **Negative Control: Vô hiệu hóa JS qua CDP Emulation chứng minh cả hai mẫu countdown hợp lệ đứng yên khi không có script, khẳng định bước kiểm tra countdown hoạt động là đáng tin cậy.** | **PASSED** |
| 24 | **`ReservationCountdown_DetailsPage_WhenStatusReturns401Or403OrExpired_AppliesCorrectUiAndDoesNotShowFakeExpiredAlert`** | **Kiểm chứng Details markup: 401/403 hiển thị `#reservationAuthAlert` riêng, ẩn `#reservationExpiredAlert`, không tuyên bố giải phóng sức chứa; expired hiển thị `#reservationExpiredAlert`.** | **PASSED** |
| 25 | **`ReservationCountdown_CdpHarness_WhenSelectorNotFoundOrJsThrows_ThrowsFailureAndDetectsError`** | **Kiểm chứng harness: CDP Runtime.evaluate bắt `exceptionDetails` và ném lỗi rõ ràng khi selector không tồn tại/JS throws, không âm thầm trả null pass giả.** | **PASSED** |
| 26 | **`ReservationCountdown_CdpHarness_ParseCountdownSecondsStrict_RejectsInvalidFormat`** | **Kiểm chứng parser countdown: từ chối nghiêm ngặt null, rỗng, sai format `mm:ss`, giây >= 60; parse chính xác mẫu hợp lệ.** | **PASSED** |

### C. Kiểm Thử Toàn Diện Solution:
- **T10 Test Suite**: **`26/26 PASSED (100%)`**, 0 Failed, 0 Skipped.
- **Reservation Domain (`ReservationServiceTests` + `ReservationIntegrationTests`)**: **`30/30 PASSED (100%)`**, 0 Failed, 0 Skipped.
- **Toàn bộ Web Solution (`SelfStorageManagementSystem.Web.sln`)**: **`252/252 PASSED (100%)`**, 0 Failed, 0 Skipped!

---

## 5. Kết Luận Nghiệm Thu

Task T10 đã giải quyết triệt để 5 vấn đề flow được chỉ định, 4 findings ban đầu, 2 findings re-review của Codex, và finding CDP test harness:
1. Đồng bộ ngày tối thiểu và ngày mặc định của form Create theo múi giờ cơ sở.
2. Sửa phép cộng tháng trên client kẹp ngày cuối tháng chính xác (31/01 -> 28/02).
3. Sửa script countdown không coi đơn confirmed/cancelled là expired.
4. Cập nhật đúng dòng mang reservationId tương ứng trên Index và MyReservations, loại bỏ nút Hủy khi hết hạn/xác nhận.
5. Loại bỏ hoàn toàn `ToLocalTime()` phụ thuộc múi giờ host, định dạng giờ chuẩn qua `AppFormatters`.
6. Khắc phục triệt để các findings review và re-review của Codex:
   - **Finding 1 (Auth error & Details markup sync)**: Tách riêng khối thông báo xác thực `#reservationAuthAlert` hiển thị khi nhận HTTP 401/403, chấm dứt polling ngay lập tức, cung cấp nút đăng nhập lại kèm returnUrl nội bộ; khối `#reservationExpiredAlert` được bảo toàn nghiêm ngặt chỉ hiển thị khi server khẳng định `expired`, không tuyên bố giải phóng sức chứa giả mạo.
   - **Finding 2 (Browser E2E Live Countdown & Two Timezones)**: Triển khai kiểm thử tự động trình duyệt thật (Edge Headless) kết nối qua giao thức Chrome DevTools Protocol (CDP) trên nền loopback proxy Kestrel:
     + Chứng minh countdown live thực sự giảm sau khoảng thời gian trôi qua có kiểm soát.
     + Chứng minh reload giữ nguyên deadline từ DB, không reset về 15:00, và tiếp tục đếm ngược sau reload.
     + Kiểm chứng độc lập 2 cơ sở (Asia/Ho_Chi_Minh và UTC) cùng instant UTC render đúng múi giờ địa phương khác nhau (+7 vs +0).
     + Xác nhận database không sinh thêm bản ghi đặt chỗ duplicate (tính idempotent).
     + Bổ sung Negative Control khi vô hiệu hóa JavaScript qua CDP Emulation (`Emulation.setScriptExecutionDisabled`), khẳng định test có khả năng phát hiện lỗi script chính xác.
   - **Finding 3 (CDP Test Harness Robustness)**:
     + Kiểm tra `exceptionDetails` và `subtype error` trong `Runtime.evaluate` của CDP: ném `InvalidOperationException` kèm biểu thức và chi tiết lỗi JavaScript nếu script gặp lỗi, loại bỏ hoàn toàn nguy cơ âm thầm trả null pass giả.
     + Phân biệt `ExecuteScriptAsync` (thao tác void/không trả về) với `EvaluateStringRequiredAsync` (đọc DOM bắt buộc có chuỗi hợp lệ non-empty).
     + Mỗi mẫu countdown trong E2E và Negative Control đều được kiểm tra nghiêm ngặt qua `ParseCountdownSecondsStrict` (đúng format `mm:ss`, giây < 60, tổng giây >= 0, không dùng -1 để so sánh).
     + Bổ sung 2 bài test harness chuyên biệt kiểm chứng cơ chế bắt lỗi của CDP helper và parser.

**Đánh giá**: **VERIFIED**. Codex đã review lại và không ghi nhận finding còn mở. T12 chuyển **READY** sau khi dependency T10 được nghiệm thu.

## 6. Xác Nhận Nghiệm Thu Độc Lập Của Codex (2026-10-16)

- Kết luận review mã nguồn và bằng chứng kiểm thử: **No findings**.
- Codex chạy lại T10 WebTests: **26 Passed, 0 Failed, 0 Skipped**.
- Codex chạy lại ReservationServiceTests và ReservationIntegrationTests: **30 Passed, 0 Failed, 0 Skipped**.
- Codex chạy lại toàn bộ Web Solution: **252 Passed, 0 Failed, 0 Skipped**.
- Phạm vi browser: Edge/CDP và sandbox; dữ liệu kiểm thử dùng EF Core InMemory. Kết quả này không thay thế nghiệm thu SQL concurrency tại T17.
- Trạng thái bàn giao: **T10 VERIFIED**, **T12 READY**; T13 và T14 tiếp tục **READY** theo dependency tương ứng.
