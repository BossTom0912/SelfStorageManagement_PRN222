# Tổng kết dự án và 5 chức năng cốt lõi

**Cập nhật:** 06/10/2026

**Đề tài:** Self-Storage Facility Rental and Management System

Tài liệu này tóm tắt hiện trạng theo [roadmap 5 chức năng](DEMO_ROADMAP_AND_TECH_SPEC.md), [hướng dẫn chức năng 1](ACCOUNT_ROLE_MANAGEMENT_GUIDE.md) và mã nguồn hiện có. Giao diện chính của dự án là **WPF**; ưu tiên thao tác rõ ràng, đủ để trình diễn nghiệp vụ, không cần đầu tư giao diện cầu kỳ. Roadmap cũ có nhắc React/Tailwind; các màn hình bên dưới được hiểu là màn hình WPF.

## Hiện trạng chung

- Kiến trúc đang dùng: **WPF Client → ASP.NET Core Web API (.NET 8) → BusinessLogic → DataAccess (EF Core, Database First) → SQL Server**.
- Mã nguồn đã có chức năng 1 (xác thực, phân quyền, quản lý tài khoản và phân công cơ sở) và chức năng 2 (API catalog, bộ lọc, danh sách ô ứng viên và sơ đồ WPF). Chức năng 3–5 hiện mới có schema/entity liên quan, chưa có luồng API và WPF tương ứng.
- Kết quả kiểm tra gần nhất: **94/94 bài test đạt**. Các test catalog dùng EF InMemory; chưa xác nhận toàn bộ luồng với SQL Server thật hoặc thao tác trực tiếp trên WPF.
- Trạng thái **Đã triển khai** trong bảng chỉ nói đến phạm vi chức năng đã có trong code; điểm nối sang chức năng kế tiếp và kiểm chứng trên môi trường chạy thật được ghi rõ bên dưới.

## Bảng trạng thái 5 chức năng

| STT | Chức năng | Kết quả nghiệp vụ cần có | Trạng thái |
| --- | --- | --- | --- |
| 1 | Xác thực, quản lý tài khoản và phân quyền (Authentication & RBAC) | Khách đăng ký/đăng nhập; admin quản lý tài khoản, role và phân công cơ sở; WPF điều hướng theo quyền | **Đã triển khai cốt lõi** |
| 2 | Tra cứu cơ sở và ô kho khả dụng (Facility & Storage Unit Catalog) | Khách lọc cơ sở, loại kho, giá và ô kho còn trống; xem sơ đồ vị trí đơn giản trên WPF | **Đã triển khai catalog; điểm nối đặt chỗ đang mô phỏng** |
| 3 | Đặt chỗ và giữ kho 15 phút (Reservation & Hold Unit) | Khách tạo/hủy đặt chỗ; hệ thống chống đặt trùng và tự giải phóng lượt giữ quá hạn | **Chưa triển khai** |
| 4 | Thanh toán cọc, tiền thuê và tạo hợp đồng (Payment & Rental Agreement) | Tính tiền, ghi nhận thanh toán, lập hóa đơn và hợp đồng trong giao dịch nhất quán | **Chưa triển khai** |
| 5 | Check-in và bàn giao kho (Check-in & Digital Handover) | Staff xác minh khách, gán ô kho, hoàn tất bàn giao, cấp thông tin truy cập và kích hoạt hợp đồng | **Chưa triển khai** |

## Phạm vi từng chức năng

### 1. Xác thực, quản lý tài khoản và phân quyền — Đã triển khai cốt lõi

**Đã có:** API đăng nhập, đăng ký khách hàng, lấy thông tin người dùng; JWT với kiểm tra lại trạng thái/role trong database; quản lý trạng thái tài khoản, role và phân công cơ sở; ghi lịch sử đăng nhập/audit; màn hình WPF Login, Register, Main và Admin Accounts. Quyền hệ thống dựa trên 5 role: `storage_customer`, `facility_staff`, `facility_manager`, `business_operations_manager`, `system_administrator`.

**Đầu ra demo:** Đăng nhập các vai trò, khách đăng ký, admin tạo tài khoản nhân viên/đổi trạng thái/gán role và cơ sở; WPF hiển thị chức năng theo quyền. Xem chi tiết cách chạy tại [ACCOUNT_ROLE_MANAGEMENT_GUIDE.md](ACCOUNT_ROLE_MANAGEMENT_GUIDE.md).

### 2. Tra cứu cơ sở và ô kho khả dụng — Đã triển khai catalog

**Đã có:** API tra cứu cơ sở, loại kho và giá thuê, danh sách ô trống ứng viên, sơ đồ vị trí; bộ lọc theo địa điểm, diện tích, giá và điều hòa. Repository dùng `AsNoTracking` và phân trang. Danh sách loại trừ ô đang sử dụng hoặc bảo trì theo **BR-OPS-02**; sức chứa nhận đặt được tính theo loại kho sau khi trừ reservation còn hiệu lực chưa gán ô theo **BR-RSV-03**. WPF có form lọc, DataGrid và sơ đồ Canvas; các nút tiếp tục kiểm tra bộ lọc đã áp dụng và dữ liệu thuộc cơ sở/ngữ cảnh hiện tại.

**Đầu ra demo hiện tại:** Khách lọc cơ sở, xem giá, số lượt còn nhận đặt theo loại kho và các ô trống ứng viên; chọn loại kho hoặc ô tham khảo trên sơ đồ để xem thông tin chuyển tiếp. Nút Tiếp tục hiện chỉ hiển thị `MessageBox`; chưa tạo reservation hay mở màn hình chức năng 3. Việc giữ chỗ thực tế phải được kiểm tra lại khi triển khai chức năng 3.

### 3. Đặt chỗ và giữ kho 15 phút — Chưa triển khai

**Cần làm:** Tạo và hủy reservation; lưu hạn giữ 15 phút bằng `reservations.hold_until` (schema không có bảng `reservation_hold`); xử lý đồng thời để không nhận quá sức chứa theo loại kho; tích hợp xử lý hết hạn và giải phóng lượt giữ. WPF hiển thị chi tiết đơn và đồng hồ đếm ngược. Theo **BR-RSV-03**, ô kho cụ thể được gán khi check-in.

**Đầu ra demo cần đạt:** Sau khi một khách giữ chỗ, khách khác không thể đặt vượt sức chứa của loại kho; khi hết hạn mà chưa thanh toán, lượt giữ được giải phóng để nhận đặt tiếp.

### 4. Thanh toán cọc, tiền thuê và tạo hợp đồng — Chưa triển khai

**Cần làm:** Tính cọc bằng một tháng tiền thuê theo **BR-FIN-01**, tiền thuê kỳ đầu và ưu đãi hợp lệ; xử lý kết quả thanh toán; ghi payment, invoice và rental agreement trong transaction. WPF hiển thị bảng tính tiền, trạng thái thanh toán và biên nhận. Chọn phương án sandbox hoặc giả lập phù hợp demo trước khi tích hợp cổng thật.

**Đầu ra demo:** Thanh toán thành công chuyển reservation sang `Confirmed`, có hóa đơn và hợp đồng chờ bàn giao; thất bại không để lại dữ liệu tài chính dở dang.

### 5. Check-in và bàn giao kho — Chưa triển khai

**Cần làm:** Staff tra cứu mã đặt chỗ, xác minh giấy tờ, gán ô kho cụ thể theo **BR-RSV-03**, lập biên bản bàn giao, tạo thông tin truy cập như PIN/QR và gửi thông báo bàn giao. Kiểm tra quyền staff/manager theo cơ sở trước mọi thao tác. WPF có màn hình quầy check-in và màn hình khách xem kho đã nhận.

**Đầu ra demo:** Hoàn tất check-in làm hợp đồng chuyển `Active`, ô kho chuyển sang trạng thái đang sử dụng và khách nhận được thông tin bàn giao.

## Phạm vi còn thiếu và giới hạn kiểm chứng

- Các lỗi review trước đây của chức năng 1 về khóa JWT mặc định, kiểm tra HTTPS, phân trang, quyền theo cơ sở và mật khẩu demo đã được sửa trong code hiện tại.
- Roadmap cũ yêu cầu refresh token nhưng chức năng 1 chưa có endpoint này. Code đã ghi audit log, chưa có API/WPF để admin xem hoặc xuất log.
- Chức năng 1–2 đã có test tự động, nhưng chưa có bằng chứng kiểm thử đầy đủ trên SQL Server thật và thao tác WPF trực tiếp.

## Thứ tự thực hiện tiếp

1. Kiểm chứng chức năng 1–2 với SQL Server thật và thao tác WPF trực tiếp.
2. Triển khai chức năng 3, nối dữ liệu đã chọn từ catalog vào luồng tạo reservation và giữ chỗ 15 phút.
3. Làm lần lượt chức năng 4 → 5 để hoàn thành luồng demo từ chọn kho đến bàn giao.
