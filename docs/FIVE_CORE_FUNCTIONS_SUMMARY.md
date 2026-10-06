# Tổng kết dự án và 5 chức năng cốt lõi

**Cập nhật:** 06/10/2026

**Đề tài:** Self-Storage Facility Rental and Management System

Tài liệu này tóm tắt hiện trạng theo [roadmap 5 chức năng](DEMO_ROADMAP_AND_TECH_SPEC.md), [hướng dẫn chức năng 1](ACCOUNT_ROLE_MANAGEMENT_GUIDE.md) và mã nguồn hiện có. Giao diện chính của dự án là **WPF**; ưu tiên thao tác rõ ràng, đủ để trình diễn nghiệp vụ, không cần đầu tư giao diện cầu kỳ. Roadmap cũ có nhắc React/Tailwind; các màn hình bên dưới được hiểu là màn hình WPF.

## Hiện trạng chung

- Kiến trúc đang dùng: **WPF Client → ASP.NET Core Web API (.NET 8) → BusinessLogic → DataAccess (EF Core, Database First) → SQL Server**.
- Mã nguồn đã có chức năng xác thực, phân quyền, quản lý tài khoản và phân công cơ sở. Các bảng/entity phục vụ chức năng 2–5 đã có từ schema, nhưng chưa có controller, service và màn hình WPF cho các luồng nghiệp vụ đó.
- Kết quả kiểm tra gần nhất: solution build thành công; **22/22 bài test đạt**. Các test hiện dùng EF InMemory, chưa xác nhận luồng chạy với SQL Server thật và thao tác WPF.
- Trong bảng dưới đây, **Done** là trạng thái hoàn thành bản triển khai chức năng 1 theo yêu cầu của nhóm. Các vấn đề phát hiện khi review được theo dõi riêng ở cuối tài liệu.

## Bảng trạng thái 5 chức năng

| STT | Chức năng | Kết quả nghiệp vụ cần có | Trạng thái |
| --- | --- | --- | --- |
| 1 | Xác thực, quản lý tài khoản và phân quyền (Authentication & RBAC) | Khách đăng ký/đăng nhập; admin quản lý tài khoản, role và phân công cơ sở; WPF điều hướng theo quyền | **Done** |
| 2 | Tra cứu cơ sở và ô kho khả dụng (Facility & Storage Unit Catalog) | Khách lọc cơ sở, loại kho, giá và ô kho còn trống; xem sơ đồ vị trí đơn giản trên WPF | **Chưa triển khai** |
| 3 | Đặt chỗ và giữ kho 15 phút (Reservation & Hold Unit) | Khách tạo/hủy đặt chỗ; hệ thống chống đặt trùng và tự giải phóng lượt giữ quá hạn | **Chưa triển khai** |
| 4 | Thanh toán cọc, tiền thuê và tạo hợp đồng (Payment & Rental Agreement) | Tính tiền, ghi nhận thanh toán, lập hóa đơn và hợp đồng trong giao dịch nhất quán | **Chưa triển khai** |
| 5 | Check-in và bàn giao kho (Check-in & Digital Handover) | Staff xác minh khách, gán ô kho, hoàn tất bàn giao, cấp thông tin truy cập và kích hoạt hợp đồng | **Chưa triển khai** |

## Phạm vi từng chức năng

### 1. Xác thực, quản lý tài khoản và phân quyền — Done

**Đã có:** API đăng nhập, đăng ký khách hàng, lấy thông tin người dùng; JWT với kiểm tra lại trạng thái/role trong database; quản lý trạng thái tài khoản, role và phân công cơ sở; ghi lịch sử đăng nhập/audit; màn hình WPF Login, Register, Main và Admin Accounts. Quyền hệ thống dựa trên 5 role: `storage_customer`, `facility_staff`, `facility_manager`, `business_operations_manager`, `system_administrator`.

**Đầu ra demo:** Đăng nhập các vai trò, khách đăng ký, admin tạo tài khoản nhân viên/đổi trạng thái/gán role và cơ sở; WPF hiển thị chức năng theo quyền. Xem chi tiết cách chạy tại [ACCOUNT_ROLE_MANAGEMENT_GUIDE.md](ACCOUNT_ROLE_MANAGEMENT_GUIDE.md).

### 2. Tra cứu cơ sở và ô kho khả dụng — Chưa triển khai

**Cần làm:** API đọc danh sách cơ sở, loại kho, giá thuê, ô kho còn trống và vị trí trên sơ đồ; bộ lọc theo vị trí, kích thước, mức giá. Dùng truy vấn `AsNoTracking` và phân trang cho danh sách lớn. Chỉ trả ô kho thực sự có thể đặt theo quy tắc **BR-OPS-02**; loại ô đang sử dụng hoặc bảo trì. Trên WPF, dùng form lọc, danh sách/DataGrid và sơ đồ mặt bằng ở mức đủ để chọn ô kho.

**Đầu ra demo:** Khách lọc một cơ sở, thấy giá và các ô kho khả dụng, chọn được ô để chuyển sang bước đặt chỗ.

### 3. Đặt chỗ và giữ kho 15 phút — Chưa triển khai

**Cần làm:** Tạo và hủy reservation; tạo lượt giữ `reservation_hold` có thời điểm hết hạn sau 15 phút; xử lý đồng thời để hai khách không giữ cùng một ô; background worker tự hết hạn và trả ô kho về trạng thái có thể đặt. WPF hiển thị chi tiết đơn và đồng hồ đếm ngược.

**Đầu ra demo:** Sau khi một khách giữ ô, khách khác không đặt trùng; khi hết hạn mà chưa thanh toán, ô trở lại danh sách khả dụng.

### 4. Thanh toán cọc, tiền thuê và tạo hợp đồng — Chưa triển khai

**Cần làm:** Tính cọc bằng một tháng tiền thuê theo **BR-FIN-01**, tiền thuê kỳ đầu và ưu đãi hợp lệ; xử lý kết quả thanh toán; ghi payment, invoice và rental agreement trong transaction. WPF hiển thị bảng tính tiền, trạng thái thanh toán và biên nhận. Chọn phương án sandbox hoặc giả lập phù hợp demo trước khi tích hợp cổng thật.

**Đầu ra demo:** Thanh toán thành công chuyển reservation sang `Confirmed`, có hóa đơn và hợp đồng chờ bàn giao; thất bại không để lại dữ liệu tài chính dở dang.

### 5. Check-in và bàn giao kho — Chưa triển khai

**Cần làm:** Staff tra cứu mã đặt chỗ, xác minh giấy tờ, gán ô kho cụ thể theo **BR-RSV-03**, lập biên bản bàn giao, tạo thông tin truy cập như PIN/QR và gửi thông báo bàn giao. Kiểm tra quyền staff/manager theo cơ sở trước mọi thao tác. WPF có màn hình quầy check-in và màn hình khách xem kho đã nhận.

**Đầu ra demo:** Hoàn tất check-in làm hợp đồng chuyển `Active`, ô kho chuyển sang trạng thái đang sử dụng và khách nhận được thông tin bàn giao.

## Việc còn lại từ review chức năng 1

Bảng tiến độ ghi **Done** cho bản triển khai đầu tiên. Trước khi dùng hệ thống ngoài môi trường demo, cần xử lý:

1. Bỏ khóa ký JWT mặc định ở môi trường triển khai; bắt buộc cung cấp secret riêng.
2. Không chấp nhận mọi chứng chỉ HTTPS trong WPF.
3. Sửa metadata phân trang để admin xem được tài khoản từ trang 2 trở đi.
4. Kiểm tra role hiện hành cùng assignment khi xác định quyền trên cơ sở.
5. Giới hạn cơ chế mật khẩu tài khoản mẫu vào môi trường demo được bật rõ ràng; thống nhất thông báo đăng nhập thất bại.

**Chênh lệch tài liệu cần chốt:** Roadmap cũ yêu cầu refresh token nhưng chức năng 1 hiện chưa có endpoint này. Tài liệu phân tích hệ thống còn yêu cầu admin xem/trích xuất audit log; code hiện ghi log nhưng chưa có API/WPF để xem hoặc xuất. Cập nhật lại roadmap và README sau khi chốt phạm vi.

## Thứ tự thực hiện tiếp

1. Sửa các lỗi review ảnh hưởng đến bảo mật và phân trang của chức năng 1.
2. Triển khai chức năng 2 trên kiến trúc và WPF hiện tại; dữ liệu catalog là đầu vào của đặt chỗ.
3. Làm lần lượt chức năng 3 → 4 → 5 để hoàn thành luồng demo từ chọn kho đến bàn giao.
