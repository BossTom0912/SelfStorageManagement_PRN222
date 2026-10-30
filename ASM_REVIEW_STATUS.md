# Trạng thái kiểm tra ASM01–04

Cập nhật 30/10/2026. Phạm vi đã được người dùng xác nhận: sửa trực tiếp bộ bài trong `Final_Project_Web`, đổi định danh nộp bài sang `TRANDOANTHANHHA`, ASM01 MVC + SignalR, ASM02 Razor Pages, ASM03 Blazor, ASM04 Console / Windows Service.

| Bài | Thay đổi chính | Kiểm chứng |
| --- | --- | --- |
| ASM01 | Đưa CRUD MVC và view từ module MVC đã có vào bản ASM01, chuyển Razor StorageUnits cũ sang tài liệu tham khảo để không trùng route, cập nhật test phản hồi từ chối MVC và kiểm tra không ghi dữ liệu trái phép. | 190 test đạt; trình duyệt độc lập nhận thay đổi tạo/sửa/xóa qua SignalR. |
| ASM02 | Đổi tên thư mục/solution/định danh tài liệu, giữ Razor Pages và SignalR, sửa script chạy theo vị trí script. | 192 test đạt, gồm E2E; tìm kiếm thực tế bằng Edge đạt. |
| ASM03 | Thêm component Blazor CRUD/tìm kiếm/phân trang, Razor host được bảo vệ, hub policy và thời hạn cookie, mỗi sự kiện dùng DI scope mới, base URL và namespace Web cho event binding, khóa nút trước khi circuit sẵn sàng. | 194 test đạt; browser CRUD/tìm kiếm, khách hàng bị chặn, desktop/mobile, không lỗi console. |
| ASM04 | Thêm Worker project, heartbeat định kỳ 5 giây, chế độ console/service, script kiểm tra/cài service, tác vụ hết hạn giữ chỗ tùy chọn dùng BLL. | 700 test đạt; 19 SQL concurrency chạy thật; build/publish worker; console log ở giây 0, 5, 10. |

Tổng 1.276 test đạt, 0 fail, 0 skip. Cả bốn solution bài nộp và solution API/WPF ASM04 build đạt. Windows Service `PRN222_TRANDOANTHANHHA_ASM04` chưa cài trên máy; Services đã được mở và danh sách service được kiểm tra. Cần PowerShell Administrator để đăng ký bằng `install-service.ps1 -Start`; chưa xác minh chạy thực tế dưới Service Control Manager.

Không thay đổi schema/EF entity, không ghi test vào database ứng dụng chính, không commit/push. Các cảnh báo cũ (CS8981, nullable, EF1002 trong test SQL) vẫn được ghi trong log. Bản ASM01 tên cũ được giữ vì thư mục bị khóa bởi tiến trình khác; bản nộp là thư mục mang tên mới.

Xem `SUBMISSION_GUIDE.md` để có lệnh chạy, kết quả chi tiết và vị trí bằng chứng. Kết quả này thay thế các nhận định sơ bộ ngày 20/10 trong tài liệu cũ.