# Nhiệm vụ Gemini: hoàn thiện chuỗi ASM 01–04

Làm việc trực tiếp trong repository này. Trước khi sửa, kiểm tra toàn bộ code, tài liệu, test và xác định mức ASM hiện tại theo tiêu chí bên dưới. Không giả định rằng thiếu đúng ba ASM: phân biệt tính năng đã có, mới một phần và chưa có. Ưu tiên hoàn thiện luồng ứng dụng hiện có, không tạo bốn project giả hoặc đổi kiến trúc database-first.

## Tiêu chí nghiệm thu

- ASM 01: giao diện Razor Pages hoặc WPF; các lớp UI -> BusinessLogic/service -> repository/DAO -> EF Core DbContext; cấu hình connection string và Admin lấy từ IConfiguration/appsettings hoặc nguồn cấu hình an toàn; login/logout, bảo vệ URL theo role, CRUD + search dữ liệu chính, validation, chính sách xóa có dữ liệu liên quan.
- ASM 02: SignalR tự cập nhật danh sách trên client khác sau thêm/sửa/xóa; booking hoặc đặt hàng và báo cáo theo StartDate–EndDate; xử lý input an toàn.
- ASM 03: giao diện MVC hoặc Blazor tái sử dụng tầng nghiệp vụ; async/await, pagination, DI; chống CSRF/XSS.
- ASM 04: hệ thống tích hợp hoàn chỉnh với concurrency control, transaction, nhập/xuất Excel/JSON/XML hoặc frontend/backend giao tiếp mạng; giới hạn quyền và xử lý an toàn các thao tác cạnh tranh.

## Cách thực hiện

1. Khảo sát kỹ, xác định chính xác trạng thái từng ASM và chọn các khoảng trống quan trọng có thể sửa trong code hiện tại.
2. Thực hiện thay đổi thực chất để hoàn thiện các tiêu chí còn thiếu, giữ nguyên schema SQL Server database-first và không hardcode secret. Không tự tuyên bố hoàn thiện nếu thiếu điều kiện kiểm chứng.
3. Chạy build và test phù hợp. Nếu cần SQL Server hay môi trường ngoài chưa sẵn có, ghi rõ giới hạn; không báo test pass khi chưa chạy.
4. **Bắt buộc viết lại file `GEMINI_ASM_REPORT.md` sau khi hoàn tất** bằng tiếng Việt. Báo cáo phải gồm: ma trận ASM 01–04 với đường dẫn code làm bằng chứng, thay đổi cụ thể, lệnh kiểm thử và kết quả thật, các lỗi/gap còn lại, điều kiện để demo. Ghi ngày giờ cập nhật. Nếu vòng sau được giao sửa tiếp, thay thế nội dung báo cáo bằng trạng thái mới.

## Quy tắc chất lượng

- Không gọi DbContext trong UI. Kiểm tra phân quyền server-side và validation ở mọi luồng ghi.
- Không tạo tài khoản/mật khẩu Admin mặc định có thể khai thác. Dùng cấu hình/secret và giải thích cách demo an toàn.
- Không che giấu lỗi, không sửa test thành dễ qua, không xoá tính năng cũ.
- Giữ thay đổi tập trung, code rõ ràng; thêm test cho logic quan trọng nếu thực sự cần.

Sau khi ghi báo cáo, trả lời ngắn gọn đường dẫn file và tóm tắt trạng thái. Người review sẽ đọc file, kiểm tra diff và giao vòng sửa tiếp nếu cần.
