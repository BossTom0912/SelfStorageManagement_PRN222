# Tổng kết dự án và 5 chức năng cốt lõi

**Cập nhật:** 08/10/2026

**Đề tài:** Self-Storage Facility Rental and Management System

Tài liệu này tóm tắt hiện trạng theo [roadmap 5 chức năng](DEMO_ROADMAP_AND_TECH_SPEC.md), [hướng dẫn chức năng 1](ACCOUNT_ROLE_MANAGEMENT_GUIDE.md) và mã nguồn hiện có. Giao diện chính của dự án là **WPF**; ưu tiên thao tác rõ ràng, đủ để trình diễn nghiệp vụ, không cần đầu tư giao diện cầu kỳ. Roadmap cũ có nhắc React/Tailwind; các màn hình bên dưới được hiểu là màn hình WPF.

## Hiện trạng chung

- Kiến trúc đang dùng: **WPF Client → ASP.NET Core Web API (.NET 8) → BusinessLogic → DataAccess (EF Core, Database First) → SQL Server**.
- Mã nguồn đã có chức năng 1 (xác thực, phân quyền, quản lý tài khoản và phân công cơ sở), chức năng 2 (API catalog, bộ lọc, danh sách ô ứng viên và sơ đồ WPF), chức năng 3 (đặt chỗ theo loại kho, giữ chỗ 15 phút, xem/hủy đơn, giải phóng hold quá hạn và màn hình WPF đếm ngược), và chức năng 4 (tính tiền cọc/kỳ đầu/voucher, checkout idempotency, cổng thanh toán Demo/VNPAY Sandbox, hóa đơn paid, hợp đồng scheduled, màn hình checkout WPF, webhook IPN, xử lý tiền đến muộn/thanh toán lặp tạo hoàn tiền và quản lý phê duyệt hoàn tiền). Chức năng 5 hiện có schema/entity liên quan, chưa có luồng API và WPF tương ứng.
- Kết quả kiểm tra (`dotnet test SelfStorageManagementSystem.sln --no-restore --verbosity quiet`): **179 bài đạt, 0 bài lỗi, 4 bài SQL Server concurrency được skip** vì chưa cấu hình database test riêng qua biến môi trường `SELFSTORAGE_SQL_TEST_CONNECTION_STRING`.
- Trạng thái **Đã triển khai** trong bảng chỉ nói đến phạm vi chức năng đã có trong code; điểm nối sang chức năng kế tiếp và kiểm chứng trên môi trường chạy thật được ghi rõ bên dưới.

## Bảng trạng thái 5 chức năng

| STT | Chức năng | Kết quả nghiệp vụ cần có | Trạng thái |
| --- | --- | --- | --- |
| 1 | Xác thực, quản lý tài khoản và phân quyền (Authentication & RBAC) | Khách đăng ký/đăng nhập; admin quản lý tài khoản, role và phân công cơ sở; WPF điều hướng theo quyền | **Đã triển khai cốt lõi** |
| 2 | Tra cứu cơ sở và ô kho khả dụng (Facility & Storage Unit Catalog) | Khách lọc cơ sở, loại kho, giá và ô kho còn trống; xem sơ đồ vị trí đơn giản trên WPF; nối trực tiếp sang luồng giữ chỗ | **Đã triển khai catalog & đã nối giữ chỗ** |
| 3 | Đặt chỗ và giữ kho 15 phút (Reservation & Hold Unit) | Khách tạo/hủy đặt chỗ theo loại kho; giữ chỗ 15 phút; chống đặt vượt sức chứa; tự giải phóng hold quá hạn (worker & read-time); WPF đếm ngược và hủy đơn | **Đã triển khai cốt lõi; chờ kiểm chứng SQL Server/WPF thực tế** |
| 4 | Thanh toán cọc, tiền thuê và tạo hợp đồng (Payment & Rental Agreement) | Tính cọc 1 tháng + thuê tháng đầu + booking fee - voucher, checkout idempotency có xác minh payload, Demo/VNPAY Gateway, hóa đơn paid, hợp đồng scheduled, xử lý trễ/khoản tiền thứ hai tạo refund, quản lý duyệt hoàn tiền, màn hình WPF minh bạch rủi ro | **Đã hoàn thiện & kiểm thử chuyên sâu; chờ kiểm chứng SQL Server/WPF thực tế** |
| 5 | Check-in và bàn giao kho (Check-in & Digital Handover) | Staff xác minh khách, gán ô kho, hoàn tất bàn giao, cấp thông tin truy cập và kích hoạt hợp đồng | **Chưa triển khai** |

## Phạm vi từng chức năng

### 1. Xác thực, quản lý tài khoản và phân quyền — Đã triển khai cốt lõi

**Đã có:** API đăng nhập, đăng ký khách hàng, lấy thông tin người dùng; JWT với kiểm tra lại trạng thái/role trong database; quản lý trạng thái tài khoản, role và phân công cơ sở; ghi lịch sử đăng nhập/audit; màn hình WPF Login, Register, Main và Admin Accounts. Quyền hệ thống dựa trên 5 role: `storage_customer`, `facility_staff`, `facility_manager`, `business_operations_manager`, `system_administrator`.

**Đầu ra demo:** Đăng nhập các vai trò, khách đăng ký, admin tạo tài khoản nhân viên/đổi trạng thái/gán role và cơ sở; WPF hiển thị chức năng theo quyền. Xem chi tiết cách chạy tại [ACCOUNT_ROLE_MANAGEMENT_GUIDE.md](ACCOUNT_ROLE_MANAGEMENT_GUIDE.md).

### 2. Tra cứu cơ sở và ô kho khả dụng — Đã triển khai catalog & đã nối giữ chỗ

**Đã có:** API tra cứu cơ sở, loại kho và giá thuê, danh sách ô trống ứng viên, sơ đồ vị trí; bộ lọc theo địa điểm, diện tích, giá và điều hòa. Repository dùng `AsNoTracking` và phân trang. Danh sách loại trừ ô đang sử dụng hoặc bảo trì theo **BR-OPS-02**; sức chứa nhận đặt được tính theo loại kho sau khi trừ reservation còn hiệu lực chưa gán ô theo **BR-RSV-03**. WPF có form lọc, DataGrid và sơ đồ Canvas; các nút tiếp tục kiểm tra bộ lọc đã áp dụng và dữ liệu thuộc cơ sở/ngữ cảnh hiện tại.

**Đầu ra demo hiện tại:** Khách lọc cơ sở, xem giá, số lượt còn nhận đặt theo loại kho và các ô trống ứng viên; chọn loại kho từ danh sách hoặc chọn ô tham khảo trên sơ đồ mặt bằng để bấm "Tiến hành giữ chỗ", mở dialog xác nhận và chuyển tiếp sang màn hình chi tiết đơn đặt chỗ với đồng hồ đếm ngược.

### 3. Đặt chỗ và giữ kho 15 phút — Đã triển khai cốt lõi

**Đã có:**
- **API & Nghiệp vụ:**
  - `POST /api/reservations`: Tạo reservation hold thời hạn tối đa 15 phút (`hold_until = now + 15m`), sinh mã dạng `RES-{facilityCode}-yyyyMMdd-XXXXXX`, snapshot biểu giá (`monthly_rate`, `deposit_amount`, `booking_fee`), tính toán sức chứa còn lại trong database transaction để chống đặt vượt số lượng ô vật lý khả dụng. Ràng buộc thời hạn thuê 1–12 tháng (**BR-RSV-02**), không gán ô kho vật lý trước (**BR-RSV-03**), bắt buộc người gọi có vai trò `storage_customer`. `quoted_total` hiện bằng tiền cọc + tiền thuê tháng đầu + phí đặt chỗ, chưa áp dụng ưu đãi.
  - `GET /api/reservations/{id}`: Xem chi tiết đơn đặt chỗ; kiểm tra quyền truy cập trước khi thực thi; tự động phát hiện hết hạn khi đọc (`TryExpireSingleReservationIfOverdueAsync`) và void hóa đơn nháp liên quan trong cùng transaction; tải lại untracked entity để luôn phản ánh dữ liệu mới nhất khi có tranh chấp đồng thời.
  - `GET /api/reservations/mine`: Tra cứu danh sách đơn đặt chỗ cá nhân có phân trang.
  - `POST /api/reservations/{id}/cancel`: Cho phép khách hàng hủy đơn trong thời gian 15 phút giữ chỗ (`CanCancel = isPendingHold && hold_until > now`); cập nhật atomic kèm void hóa đơn nháp; xử lý idempotent; chặn hủy đơn đã thanh toán cọc (`confirmed`) với HTTP 409 Conflict.
- **Background Worker:** `ReservationHoldExpirationWorker` chạy ngay khi khởi động rồi kiểm tra định kỳ 30 giây theo cấu hình mặc định; cập nhật nguyên tử có điều kiện (`conditional atomic update`) để không ghi đè đơn đã được xác nhận thanh toán (`confirmed`). Hold hiện giữ **sức chứa theo loại kho**, chưa tạo `unit_allocations` hay gán ô kho vật lý; sức chứa được tính lại khi hold hết hạn hoặc bị hủy.
- **Giao diện WPF:** Màn hình `ConfirmReservationDialog` xác nhận thông tin cọc/thuê; màn hình `ReservationDetailWindow` hiển thị thông tin snapshot, trạng thái, đồng hồ đếm ngược 15 phút tự động cập nhật thời gian thực, và nút "Hủy giữ chỗ". Xử lý chặt chẽ chống stale state khi đổi filter hoặc bấm tiếp tục từ Floor Map.
- **Kiểm thử & Lỗi review đã sửa:**
  - Sửa lỗi EF Core Change Tracker trả cache cũ sau khi `ExecuteUpdateAsync` trong `CancelReservationAsync` và `GetReservationByIdAsync`.
  - Đồng bộ logic `CanCancel` nhất quán giữa `MapToDetailResponse` và `MapToListItemResponse`.
  - Cô lập an toàn bài test SQL Server Concurrency (`ReservationSqlServerConcurrencyTests`) qua `[SqlIntegrationFact]`, loại bỏ mã xóa dữ liệu thật, tự seed fixture độc lập và kiểm tra database name an toàn.

**Tiến độ function 3:** Hoàn tất luồng API tạo/xem danh sách/xem chi tiết/hủy đơn, giữ sức chứa 15 phút, worker hết hạn và màn hình WPF xác nhận, xem đơn, đếm ngược, hủy đơn. Bộ test tự động hiện đạt 124/124 bài được chạy; bài SQL Server concurrency chưa chạy do chưa có database test riêng. Chưa kiểm chứng end-to-end với SQL Server thật và thao tác WPF trực tiếp. Phần thanh toán, hóa đơn, hợp đồng và chuyển `confirmed` thuộc function 4.

### 4. Thanh toán cọc, tiền thuê và tạo hợp đồng — Đã hoàn thiện & kiểm thử chuyên sâu

**Đã có:**
- **API & Nghiệp vụ:**
  - `GET /api/reservations/{id}/checkout-quote`: Báo giá chính xác kỳ đầu: Cọc bằng 1 tháng thuê (**BR-FIN-01**) + Tiền thuê tháng đầu + Phí đặt chỗ - Giảm giá voucher. Kiểm tra hold còn hạn, snapshot cọc khớp giá tháng, voucher (`fixed`, `percentage`, `free_days` tính chuẩn theo số ngày thực tế của tháng 28/29/30/31 ngày, không giảm tiền cọc; chặn ngày lẻ). Thuế demo `tax_amount = 0`.
  - `POST /api/payments/checkout`: Tạo payment attempt và invoice trong database transaction với thứ tự khóa chuẩn (`SS_Reservation` trước `SS_Promotion`); kiểm tra idempotency key bắt buộc kèm đối chiếu payload (`customer_id`, `reservation_id`, `provider`, `amount`) chống xung đột payload (409 Conflict); đối chiếu tổng tiền trigger SQL Server tính toán; hỗ trợ retry làm mới invoice lines khi quote thay đổi.
  - Cổng thanh toán: `DemoGateway` (chỉ cho phép trong Development/Test khi cấu hình `Payment:AllowDemoSimulator=true`, từ chối payment khác provider demo) và `VnpayGateway` (fail-fast khi thiếu secret, kiểm tra số nguyên VND, HMAC-SHA512, GMT+7, VND x 100).
  - Webhook IPN & Callback: `GET /api/payments/vnpay/ipn` kiểm tra toàn diện chữ ký (97), TmnCode (97), currency VND (04), amount khớp payment (04), txnRef tồn tại (01), và xử lý idempotent (02).
  - Xử lý khoản tiền thứ hai & hold hết hạn: Phân xử hold theo thời gian server UTC; worker quét quá hạn dùng chung khóa applock; nếu tiền về sau hạn hoặc đơn đã được thanh toán bởi attempt khác, ghi nhận payment `succeeded`, đánh dấu `reconciliation_required = true`, tạo đúng 1 yêu cầu hoàn tiền `refunds`, không tạo trùng hợp đồng.
  - Quản lý hoàn tiền: `GET /api/payments/refunds` và `POST /api/payments/refunds/{id}/review` cho nhân viên/quản lý phê duyệt hoặc từ chối, ghi vết vào bảng `refund_approvals`.
  - Finalization thành công: Chuyển payment `succeeded`, invoice `paid`, reservation `confirmed`, tạo hợp đồng `rental_agreements` ở trạng thái `scheduled`, liên kết `policy_version_id` đã chấp thuận, `signed_at = null`, `deposit_balance = deposit_snapshot`, chuyển voucher sang `applied`.
  - Tra cứu: `GET /api/payments/{id}`, `GET /api/invoices/{id}`, `GET /api/agreements/{id}`, `GET /api/payments/reconciliation`.
- **WPF Client:**
  - Thêm nút chuyển thanh toán từ `ReservationDetailWindow`.
  - Màn hình `CheckoutWindow`: Chi tiết các dòng tiền, ô nhập voucher, hiển thị điều khoản thuê kho từ quote, checkbox xác nhận điều khoản, chọn cổng thanh toán (Demo / VNPAY Sandbox), ẩn nút giả lập khi chọn VNPAY, mở browser, đồng hồ đếm ngược hold, polling trạng thái thời gian thực, view kết quả thành công và view cảnh báo đối soát / hoàn tiền (`panelReconciliationOutcome`).
- **Kiểm thử tự động:**
  - Unit tests cho `PaymentService` (công thức tính tiền, 3 loại voucher, ràng buộc tháng 28-31 ngày, validation rules, idempotency replay/conflict, expired hold, duplicate payment, demo simulator protection, IPN validation, refund review).
  - 5 unit tests cho `VnpayGateway` (URL creation, HMAC-SHA512 checksum, tampered detection, fractional VND rejection, fail-fast credentials).
  - 10 integration tests cho API endpoints (quyền truy cập, checkout, demo completion, invoice/agreement retrieval, IPN responses 00/01/04/97, refund endpoints RBAC).
  - 4 SQL Server concurrency tests (`PaymentSqlServerConcurrencyTests` và `ReservationSqlServerConcurrencyTests`) với `[SqlIntegrationFact]`; cả 4 chưa chạy vì chưa có database test riêng.

**Tiến độ function 4:** Đã có backend, WPF và bộ kiểm thử tự động (179 passed / 4 skipped). Các lỗi review về replay thanh toán, xử lý lỗi và kiểm chứng race trên SQL Server vẫn cần xử lý trước khi nghiệm thu và bàn giao cho Chức năng 5.

### 5. Check-in và bàn giao kho — Chưa triển khai

**Cần làm:** Staff tra cứu mã đặt chỗ, xác minh giấy tờ, gán ô kho cụ thể theo **BR-RSV-03**, lập biên bản bàn giao, tạo thông tin truy cập như PIN/QR và gửi thông báo bàn giao. Kiểm tra quyền staff/manager theo cơ sở trước mọi thao tác. WPF có màn hình quầy check-in và màn hình khách xem kho đã nhận.

**Đầu ra demo:** Hoàn tất check-in làm hợp đồng chuyển `Active`, ô kho chuyển sang trạng thái đang sử dụng và khách nhận được thông tin bàn giao.

## Phạm vi còn thiếu và giới hạn kiểm chứng

- Các lỗi review trước đây của chức năng 1 về khóa JWT mặc định, kiểm tra HTTPS, phân trang, quyền theo cơ sở và mật khẩu demo đã được sửa trong code hiện tại.
- Roadmap cũ yêu cầu refresh token nhưng chức năng 1 chưa có endpoint này. Code đã ghi audit log, chưa có API/WPF để admin xem hoặc xuất log.
- Chức năng 1–4 hiện có 179 test tự động đạt, 4 bài test concurrency trên SQL Server được cấu hình skip khi chưa có database test riêng qua `SELFSTORAGE_SQL_TEST_CONNECTION_STRING`.
- `tax_amount = 0` được thiết lập như quy ước nội bộ demo, hiển thị rõ ràng trên UI/hóa đơn là biên nhận nội bộ, chưa phải phát hành hóa đơn điện tử GTGT hợp pháp.

## Thứ tự thực hiện tiếp

1. Triển khai Chức năng 5: Check-in, gán ô kho cụ thể theo **BR-RSV-03**, lập biên bản bàn giao và kích hoạt hợp đồng (`scheduled → active`).
2. Kiểm chứng liên hoàn luồng nghiệp vụ từ Chức năng 1 đến Chức năng 5 trên môi trường demo thực tế.
