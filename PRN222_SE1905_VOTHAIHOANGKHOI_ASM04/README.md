# PRN222_SE1905_VOTHAIHOANGKHOI_ASM04 — Console / Windows Service

Sinh viên: Võ Thái Hoàng Khôi. Cập nhật 30/10/2026.

Project mới `src/SelfStorageManagementSystem.Worker` (.NET 8) có trong solution ASM04 và solution API/WPF. Dùng Generic Host, BackgroundService, ILogger và Microsoft.Extensions.Hosting.WindowsServices. Các phần Web/Razor/MVC/SignalR, API JWT, WPF, nghiệp vụ đặt chỗ/thanh toán/bàn giao và concurrency được giữ nguyên.

```powershell
# Console liên tục; Ctrl+C để dừng
.\run-worker.ps1
# Log tại giây 0, 5, 10 và tự dừng
.\run-worker.ps1 --Worker:MaxCycles=3
# Kiểm tra service và mở Services
.\check-service.ps1 -OpenServices
# Cài service trong PowerShell Run as Administrator khi cần
.\install-service.ps1 -Start
# Web
.\run.ps1
# Build/test
dotnet build .\PRN222_SE1905_VOTHAIHOANGKHOI_ASM04.sln
dotnet build .\SelfStorageManagementSystem.sln
dotnet test tests\SelfStorageManagementSystem.Tests
dotnet test tests\SelfStorageManagementSystem.WebTests
```

Service name: `PRN222_VOTHAIHOANGKHOI_ASM04`, display name `PRN222 Vo Thai Hoang Khoi ASM04 Worker`, startup Manual. Console dùng timestamp với mili giây; Windows Service ghi Information trở lên vào Application Event Log, source cùng tên service. Cấu hình đọc từ thư mục executable nên không phụ thuộc working directory của Service Control Manager.

Mặc định worker chỉ heartbeat, không ghi database. Muốn chạy thêm tác vụ hết hạn giữ chỗ thì cấu hình `ReservationWorker__Enabled=true` và `ConnectionStrings__DefaultConnection` cho tiến trình/service; tác vụ gọi `IReservationService` qua scope riêng, heartbeat chạy độc lập. Không sửa schema.

Kết quả: 700/700 test (250 nghiệp vụ/API/WPF + 450 web), không skip. Trong đó 19 test SQL concurrency chạy thật trên `localhost/SelfStoragePRN222_Test` bằng biến môi trường `SELFSTORAGE_SQL_TEST_CONNECTION_STRING`. E2E web dùng database test riêng. Worker đã build/publish và chạy console/bản publish có log cách nhau 5 giây.

Trạng thái máy khi kiểm tra: service chưa được cài, tài khoản kiểm tra không có quyền Administrator; đã mở Services và truy vấn danh sách service. Chưa xác minh chạy dưới Service Control Manager. Xem `../SUBMISSION_GUIDE.md` để cấu hình và xem vị trí bằng chứng.