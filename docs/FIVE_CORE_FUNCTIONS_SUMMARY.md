# Tổng kết dự án và 5 chức năng cốt lõi

**Cập nhật:** 07/10/2026

**Đề tài:** Self-Storage Facility Rental and Management System

Tài liệu này tóm tắt hiện trạng theo [roadmap 5 chức năng](DEMO_ROADMAP_AND_TECH_SPEC.md), [hướng dẫn chức năng 1](ACCOUNT_ROLE_MANAGEMENT_GUIDE.md) và mã nguồn hiện có. Giao diện chính của dự án là **WPF**; ưu tiên thao tác rõ ràng, đủ để trình diễn nghiệp vụ, không cần đầu tư giao diện cầu kỳ. Roadmap cũ có nhắc React/Tailwind; các màn hình bên dưới được hiểu là màn hình WPF.

## Hiện trạng chung

- Kiến trúc đang dùng: **WPF Client → ASP.NET Core Web API (.NET 8) → BusinessLogic → DataAccess (EF Core, Database First) → SQL Server**.
- Mã nguồn đã có chức năng 1 (xác thực, phân quyền, quản lý tài khoản và phân công cơ sở), chức năng 2 (API catalog, bộ lọc, danh sách ô ứng viên và sơ đồ WPF) và chức năng 3 (đặt chỗ theo loại kho, giữ chỗ 15 phút, xem/hủy đơn, giải phóng hold quá hạn và màn hình WPF đếm ngược). Chức năng 4–5 hiện mới có schema/entity liên quan, chưa có luồng API và WPF tương ứng.
- Kết quả kiểm tra gần nhất: **124/125 bài test đạt, 1 bài test SQL Concurrency được skip an toàn (yêu cầu opt-in qua biến môi trường)**.
- Trạng thái **Đã triển khai** trong bảng chỉ nói đến phạm vi chức năng đã có trong code; điểm nối sang chức năng kế tiếp và kiểm chứng trên môi trường chạy thật được ghi rõ bên dưới.

## Bảng trạng thái 5 chức năng

| STT | Chức năng | Kết quả nghiệp vụ cần có | Trạng thái |
| --- | --- | --- | --- |
| 1 | Xác thực, quản lý tài khoản và phân quyền (Authentication & RBAC) | Khách đăng ký/đăng nhập; admin quản lý tài khoản, role và phân công cơ sở; WPF điều hướng theo quyền | **Đã triển khai cốt lõi** |
| 2 | Tra cứu cơ sở và ô kho khả dụng (Facility & Storage Unit Catalog) | Khách lọc cơ sở, loại kho, giá và ô kho còn trống; xem sơ đồ vị trí đơn giản trên WPF; nối trực tiếp sang luồng giữ chỗ | **Đã triển khai catalog & đã nối giữ chỗ** |
| 3 | Đặt chỗ và giữ kho 15 phút (Reservation & Hold Unit) | Khách tạo/hủy đặt chỗ theo loại kho; giữ chỗ 15 phút; chống đặt vượt sức chứa; tự giải phóng hold quá hạn (worker & read-time); WPF đếm ngược và hủy đơn | **Đã triển khai cốt lõi** |
| 4 | Thanh toán cọc, tiền thuê và tạo hợp đồng (Payment & Rental Agreement) | Tính tiền, ghi nhận thanh toán, lập hóa đơn và hợp đồng trong giao dịch nhất quán | **Chưa triển khai** |
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
  - `POST /api/reservations`: Tạo reservation hold thời hạn tối đa 15 phút (`hold_until = now + 15m`), mã đặt chỗ duy nhất định dạng `RSV-yyyyMMdd-XXXXXX`, snapshot biểu giá (`monthly_rate`, `deposit_amount`, `booking_fee`), tính toán sức chứa còn lại trong database transaction để chống đặt vượt số lượng ô vật lý khả dụng. Ràng buộc thời hạn thuê 1–12 tháng (**BR-RSV-02**), không gán ô kho vật lý trước (**BR-RSV-03**), bắt buộc người gọi có vai trò `storage_customer`.
  - `GET /api/reservations/{id}`: Xem chi tiết đơn đặt chỗ; kiểm tra quyền truy cập trước khi thực thi; tự động phát hiện hết hạn khi đọc (`TryExpireSingleReservationIfOverdueAsync`) và void hóa đơn nháp liên quan trong cùng transaction; tải lại untracked entity để luôn phản ánh dữ liệu mới nhất khi có tranh chấp đồng thời.
  - `GET /api/reservations/mine`: Tra cứu danh sách đơn đặt chỗ cá nhân có phân trang.
  - `POST /api/reservations/{id}/cancel`: Cho phép khách hàng hủy đơn trong thời gian 15 phút giữ chỗ (`CanCancel = isPendingHold && hold_until > now`); cập nhật atomic kèm void hóa đơn nháp; xử lý idempotent; chặn hủy đơn đã thanh toán cọc (`confirmed`) với HTTP 409 Conflict.
- **Background Worker:** `ReservationExpirationWorker` chạy ngầm định kỳ 60 giây, thực hiện cập nhật nguyên tử có điều kiện (`conditional atomic update`), tuyệt đối không ghi đè đơn đã được xác nhận thanh toán (`confirmed`).
- **Giao diện WPF:** Màn hình `ConfirmReservationDialog` xác nhận thông tin cọc/thuê; màn hình `ReservationDetailWindow` hiển thị thông tin snapshot, trạng thái, đồng hồ đếm ngược 15 phút tự động cập nhật thời gian thực, và nút "Hủy giữ chỗ". Xử lý chặt chẽ chống stale state khi đổi filter hoặc bấm tiếp tục từ Floor Map.
- **Kiểm thử & Lỗi review đã sửa:**
  - Sửa lỗi EF Core Change Tracker trả cache cũ sau khi `ExecuteUpdateAsync` trong `CancelReservationAsync` và `GetReservationByIdAsync`.
  - Đồng bộ logic `CanCancel` nhất quán giữa `MapToDetailResponse` và `MapToListItemResponse`.
  - Cô lập an toàn bài test SQL Server Concurrency (`ReservationSqlServerConcurrencyTests`) qua `[SqlIntegrationFact]`, loại bỏ mã xóa dữ liệu thật, tự seed fixture độc lập và kiểm tra database name an toàn.

**Giới hạn kiểm chứng:** 124 unit & integration test chạy tự động trên môi trường in-memory; kiểm thử concurrency trên SQL Server thật được cấu hình opt-in an toàn và skip khi chưa có biến môi trường chỉ định database kiểm thử riêng biệt.

### 4. Thanh toán cọc, tiền thuê và tạo hợp đồng — Chưa triển khai

**Cần làm:** Tính cọc bằng một tháng tiền thuê theo **BR-FIN-01**, tiền thuê kỳ đầu và ưu đãi hợp lệ; xử lý kết quả thanh toán; ghi payment, invoice và rental agreement trong transaction. WPF hiển thị bảng tính tiền, trạng thái thanh toán và biên nhận. Chọn phương án sandbox hoặc giả lập phù hợp demo trước khi tích hợp cổng thật.

**Đầu ra demo:** Thanh toán thành công chuyển reservation sang `Confirmed`, có hóa đơn và hợp đồng chờ bàn giao; thất bại không để lại dữ liệu tài chính dở dang.

### 5. Check-in và bàn giao kho — Chưa triển khai

**Cần làm:** Staff tra cứu mã đặt chỗ, xác minh giấy tờ, gán ô kho cụ thể theo **BR-RSV-03**, lập biên bản bàn giao, tạo thông tin truy cập như PIN/QR và gửi thông báo bàn giao. Kiểm tra quyền staff/manager theo cơ sở trước mọi thao tác. WPF có màn hình quầy check-in và màn hình khách xem kho đã nhận.

**Đầu ra demo:** Hoàn tất check-in làm hợp đồng chuyển `Active`, ô kho chuyển sang trạng thái đang sử dụng và khách nhận được thông tin bàn giao.

## Phạm vi còn thiếu và giới hạn kiểm chứng

- Các lỗi review trước đây của chức năng 1 về khóa JWT mặc định, kiểm tra HTTPS, phân trang, quyền theo cơ sở và mật khẩu demo đã được sửa trong code hiện tại.
- Roadmap cũ yêu cầu refresh token nhưng chức năng 1 chưa có endpoint này. Code đã ghi audit log, chưa có API/WPF để admin xem hoặc xuất log.
- Chức năng 1–3 đã có 124 test tự động đạt chuẩn, nhưng kiểm thử luồng thực tế trên SQL Server và thao tác WPF vẫn cần môi trường chạy trực tiếp.
- Cần thống nhất chính sách kinh doanh cho `quoted_total` (tiền cọc + 1 tháng đầu tiên vs tiền cọc + toàn bộ kỳ hạn thuê) trước khi sang chức năng 4.

## Thứ tự thực hiện tiếp

1. Thống nhất quyết định chính sách giá cho `quoted_total` cho hợp đồng nhiều tháng.
2. Triển khai Chức năng 4: Thanh toán cọc, tiền thuê kỳ đầu, lập hóa đơn (`invoices`) và hợp đồng thuê (`rental_agreements`).
3. Triển khai Chức năng 5: Check-in, gán ô kho cụ thể theo **BR-RSV-03**, lập biên bản bàn giao và kích hoạt hợp đồng.
