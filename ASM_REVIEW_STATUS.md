# Rà soát ASM 01–04 (20/10/2026)

## Kết luận tạm thời

Repository không phải chỉ có ASM 01. Mã nguồn đã kết hợp Razor Pages, MVC, SignalR, WPF, Web API, EF Core, DI và các nghiệp vụ đặt chỗ/thanh toán. Theo mức công nghệ, dự án đã đi tới **ASM 04 đang hoàn thiện**, nhưng chưa thể nghiệm thu cả bốn ASM vì còn thiếu bằng chứng chạy thật và một số yêu cầu cụ thể.

| ASM | Bằng chứng hiện có | Chưa xác nhận / còn thiếu |
| --- | --- | --- |
| 01 | `src/SelfStorageManagementSystem.Web/Program.cs` cấu hình Razor Pages, cookie auth và role policies; `src/SelfStorageManagementSystem.Web/Pages/StorageUnits/` có CRUD; `src/SelfStorageManagementSystem.BusinessLogic/` và `src/SelfStorageManagementSystem.DataAccess/` phân tầng; `appsettings.json` chứa connection string không kèm mật khẩu. | Yêu cầu "tài khoản Admin từ appsettings.json" không đúng hoàn toàn với thiết kế hiện tại: tài khoản nằm trong DB và demo bootstrap dùng cấu hình/secret. Cần làm rõ đề bài chính thức trước khi thay đổi cơ chế này. |
| 02 | `src/SelfStorageManagementSystem.Web/Hubs/StorageUnitHub.cs` và `wwwroot/js/storage-units-realtime.js` xử lý cập nhật SignalR; `Pages/Reservations/` có đặt chỗ; `Pages/Reports/Reservations` đã có báo cáo theo ngày bắt đầu thuê. | Kịch bản hai trình duyệt chưa chạy được vì thiếu DB E2E riêng. |
| 03 | `AddControllersWithViews` và `Views/Account`, `Views/Home` là MVC; service async, phân trang trong `PagedRequest`, DI trong composition. | Phần lớn màn hình nghiệp vụ vẫn là Razor Pages, nên mức "chuyển đổi tầng giao diện sang MVC" cần so với đề bài từng học kỳ. |
| 04 | Có WPF gọi Web API, transaction trong dịch vụ, cơ chế concurrency ở repository/service và bảo vệ dữ liệu đầu vào. | Chưa xác nhận toàn bộ race trên SQL Server thật; chưa thấy chức năng nhập/xuất file (đề cho phép **hoặc** backend/frontend tách mạng, vốn đã có). Tài liệu `docs/FIVE_CORE_FUNCTIONS_SUMMARY.md` liệt kê rủi ro thanh toán chưa nghiệm thu và đã cũ so với code hiện tại. |

## Kết quả thực thi hiện tại

- Gemini CLI cũ bị từ chối xác thực, nhưng Antigravity CLI (`agy.exe`) chạy được Gemini Flash. Gemini đã thêm báo cáo đặt chỗ theo khoảng ngày; các vòng review tiếp theo đã sửa lỗi phạm vi cơ sở, tên chỉ số và test. Xem `GEMINI_ASM_REPORT.md` để biết thay đổi cụ thể.
- Build Web đạt với 5 cảnh báo, 0 lỗi. Sáu test mục tiêu đạt. Bộ test nghiệp vụ đạt 231 pass, 19 skip, 0 fail. Bộ WebTests không hoàn tất trong lượt kiểm tra này và đã được dừng sau khi chạy lâu không trả kết quả.
- Một test E2E SignalR hai trình duyệt thất bại khi khởi tạo do không kết nối được database SQL Server riêng `SelfStoragePRN222_WebE2eTest` trên `(localdb)\\mssqllocaldb`. Đây là giới hạn môi trường, chưa chứng minh lỗi chức năng SignalR.
- Bản rà soát này chỉ là kết luận sơ bộ từ cấu trúc code và một test. Không tuyên bố code không lỗi.

## Việc cần làm để nghiệm thu

1. Tiếp tục giao các khoảng trống còn lại cho Gemini qua Antigravity CLI, với phạm vi nhỏ đủ để kiểm tra từng vòng.
2. Cấp database SQL Server test riêng và chạy các E2E/concurrency tests; không chạy script clean install trên DB chứa dữ liệu thật.
3. Đối chiếu đề ASM chính thức của lớp, đặc biệt yêu cầu Admin trong appsettings, báo cáo thống kê và mức bắt buộc chuyển giao diện sang MVC.
4. Review diff, báo cáo Gemini và test sau mỗi vòng; chỉ đánh dấu hoàn thiện khi mọi mục bắt buộc được chứng minh.
