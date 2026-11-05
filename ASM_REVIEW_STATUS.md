# Trạng thái kiểm tra ASM01–04

Cập nhật 05/11/2026. Phạm vi: solution hợp nhất `SelfStorageManagementSystem.sln` trên `main`, dựa trên `origin/main` tại `00117fe`, cộng bản sửa checkout có sẵn trong workspace. Các số liệu bốn solution riêng ngày 30/10 là kết quả lịch sử, không phải cấu trúc hiện tại.

## Các lỗi đã sửa

| Mức | Lỗi xác nhận được | Cách xử lý và bằng chứng |
| --- | --- | --- |
| P1 | MVC đặt chỗ chiếm route Razor `/Reservations`: liên kết catalog không chuyển sang tạo đơn, danh sách mất countdown; chi tiết dạng `/Details/{id}` cũng bị chuyển sang view MVC rút gọn. | Chuyển các màn hình MVC đặt chỗ/báo cáo sang `/Mvc/Reservations` và `/Mvc/Reports/Reservations`, cập nhật link/form bằng route helpers. Ba test đặt chỗ từng fail nay qua; thêm hai test bảo đảm MVC/Razor và các link hoạt động độc lập. |
| P2 | CRUD Blazor chỉ lưu database, không phát sự kiện tới các phiên SignalR khác. | Phát sự kiện bằng publisher hiện có sau khi lưu. Ngừng niêm yết phát update, xóa thật phát delete kèm facility; lỗi broadcast giữ thông báo lưu thành công và hướng dẫn tải lại. Hai test Edge thao tác CRUD và một kết nối SignalR độc lập xác nhận sự kiện cùng trạng thái database. Thông báo xóa/ngừng niêm yết dùng tiếng Việt thay cho mã nội bộ. |
| P2 | Form checkout sau khi ApplyPromo có thể POST lại handler ApplyPromo, không khởi tạo thanh toán. | Giữ và kiểm chứng bản sửa checkout có sẵn: form trỏ rõ về handler checkout mặc định. Hai test gửi theo action/input thực tế của form, có/không có mã giảm giá, xác nhận payment pending và số tiền. |
| P2 | Fixture API khởi động với bootstrap demo từ cấu hình local trước khi seed database InMemory, làm 36 test tích hợp fail. | Fixture tắt `DemoAccounts:Enabled` và tiếp tục seed dữ liệu của riêng nó; toàn bộ test API qua. Không thay đổi cờ bootstrap của ứng dụng. |
| P2 | Danh sách đặt chỗ MVC lấy dữ liệu theo trang nhưng không có nút sang trang tiếp theo, khiến các đơn ngoài trang đầu không truy cập được từ danh sách. | Thêm nút trang trước/sau giữ nguyên bộ lọc và kích thước trang; test đi theo href đã render để xem đơn ở trang hai. |
| P3 | Hướng dẫn vẫn yêu cầu các thư mục/solution ASM đã bị gỡ và cấu hình bootstrap cũ. | Cập nhật `SUBMISSION_GUIDE.md` và README theo solution, routes và cấu hình hiện tại. |

Không còn finding đã xác nhận nhưng chưa xử lý trong phạm vi rà soát này.

## Kiểm chứng

| Hạng mục | Kết quả |
| --- | --- |
| Build `SelfStorageManagementSystem.sln` | Qua, 0 lỗi |
| BusinessLogicTests | 38/38 |
| Tests — nghiệp vụ, API, WPF | 253/253, gồm 19 test SQL concurrency thật |
| WebTests — Razor/MVC, auth, SignalR, browser, E2E | 479/479 |
| Tổng bộ test solution | **770 qua, 0 fail, 0 skip** |
| Worker console | Ba heartbeat ở giây 0, 5, 10; tự dừng với `Worker:MaxCycles=3` |
| Worker publish | Publish Release qua; executable chạy một heartbeat và tự dừng |

Bộ solution chạy với `SELFSTORAGE_SQL_TEST_CONNECTION_STRING` trỏ tới `localhost/SelfStoragePRN222_Test`. E2E hai trình duyệt dùng LocalDB `SelfStoragePRN222_WebE2eTest`. Không chạy test ghi dữ liệu vào database ứng dụng chính, không sửa schema hoặc entity EF sinh tự động.

Sau lần chạy đầy đủ, sửa câu thông báo xóa/ngừng niêm yết trên Blazor, thêm nút phân trang MVC và build/chạy lại bốn test liên quan tới Blazor, MVC đặt chỗ và báo cáo. Không thay đổi logic nghiệp vụ sau lần chạy đầy đủ.

Browser plugin không có trong phiên làm việc. Kiểm chứng dùng workflow Edge headless/CDP sẵn có của repository, Kestrel thật và kết nối SignalR độc lập. Test Blazor kiểm tra route/title, nội dung sau create/edit/delete, thông báo kết quả và không có lỗi JavaScript/console.error. Ảnh chụp desktop 1280×800 ở thư mục tạm; chưa kiểm tra lại mobile trong lần rà soát này.

Log, TRX và ảnh lần chạy nằm ngoài source tại `%TEMP%/asm-review-*.log`, `%TEMP%/asm-review-results`, `%TEMP%/asm-review-blazor-*.png` và `%TEMP%/asm-review-evidence`. Ảnh T18 bị bộ test ghi lại được khôi phục về bản tracked trước khi commit.

## Giới hạn

- Các cảnh báo có sẵn về tên entity EF viết thường, nullable và raw SQL trong test vẫn còn; build không có lỗi.
- Chưa cài/chạy Worker dưới Windows Service Control Manager trong lần rà soát này. Console và bản publish đã chạy; đăng ký service cần PowerShell Administrator.
- Thanh toán được kiểm chứng bằng Demo/Sandbox và các callback test; không thực hiện giao dịch tiền thật.
