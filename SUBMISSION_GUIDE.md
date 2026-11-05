# Bộ bài ASM01–04 — Võ Thái Hoàng Khôi

Cập nhật 05/11/2026. ASM01–04 đã được hợp nhất vào repository này. Mở `SelfStorageManagementSystem.sln` tại thư mục gốc; không cần các thư mục hoặc solution ASM riêng của bản nộp ngày 30/10.

| Bài | Thành phần | Đường dẫn demo |
| --- | --- | --- |
| ASM01 | MVC CRUD và SignalR | `/StorageUnits`, `/storageUnitHub` |
| ASM02 | Razor Pages: catalog, đặt chỗ, thanh toán, báo cáo | `/Catalog`, `/Reservations`, `/Reports/Reservations` |
| ASM03 | Blazor Server CRUD, tìm kiếm, phân trang | `/Blazor/StorageUnits` |
| ASM04 | Console / Windows Service, heartbeat và tác vụ hết hạn giữ chỗ tùy chọn | `src/SelfStorageManagementSystem.Worker` |

MVC đặt chỗ và báo cáo có route riêng `/Mvc/Reservations` và `/Mvc/Reports/Reservations`, để các route Razor Pages vẫn giữ đồng hồ giữ chỗ, phân trang và chuyển tiếp từ catalog. Blazor phát sự kiện CRUD qua cùng publisher SignalR với MVC và Razor Pages.

## Build và chạy tại thư mục gốc

Cần .NET 8 SDK/runtime và SQL Server/LocalDB có schema Database First. Không chạy lại script clean-install trên database có dữ liệu cần giữ.

```powershell
dotnet build .\SelfStorageManagementSystem.sln
.\run.ps1
# Hoặc
.\run.bat
```

Script dùng đường dẫn dựa trên vị trí script nên có thể gọi từ thư mục khác. Profile Web mặc định dùng HTTPS 7041 / HTTP 5041. Cấu hình database bằng `ConnectionStrings__DefaultConnection` hoặc User Secrets của Web project.

Bootstrap Web chỉ chạy trong Development khi `Bootstrap__Enabled=true`. Cần tài khoản admin đã tồn tại, có đúng role, và cấu hình `AdminAccount__Email`, `AdminAccount__Password`. Đặt `Bootstrap__Enabled=false` khi không muốn cập nhật tài khoản demo lúc startup. API/WPF là host riêng và dùng cờ `DemoAccounts__Enabled`; xem README để cấu hình JWT cho API.

## Kiểm thử

```powershell
# Mặc định các test SQL concurrency được bỏ qua khi chưa có cấu hình database riêng.
dotnet test .\SelfStorageManagementSystem.sln

# Chỉ dùng database kiểm thử tách biệt đã có schema, tuyệt đối không dùng database ứng dụng chính.
$env:SELFSTORAGE_SQL_TEST_CONNECTION_STRING = 'Server=localhost;Database=SelfStoragePRN222_Test;Integrated Security=True;TrustServerCertificate=True;'
dotnet test .\SelfStorageManagementSystem.sln
```

Bộ E2E hai trình duyệt cần Microsoft Edge và LocalDB `SelfStoragePRN222_WebE2eTest`. Bộ test tự tạo/dọn dữ liệu thử trong database này. Các ảnh T18 được bộ test hiện có ghi vào `AgentReport/Evidence/T18`; kiểm tra diff trước khi commit để tránh đưa ảnh chạy lại vào bản sửa code.

## ASM04 console và Windows Service

```powershell
# Log mỗi 5 giây, Ctrl+C để dừng
.\run-worker.ps1
# Log ba lần rồi tự dừng
.\run-worker.ps1 --Worker:MaxCycles=3
# Kiểm tra service
.\check-service.ps1
# Cài/chạy service từ PowerShell Administrator khi cần
.\install-service.ps1 -Start
```

Service name: `PRN222_VOTHAIHOANGKHOI_ASM04`. Mặc định worker chỉ heartbeat, không thay đổi database. Bật tác vụ hết hạn giữ chỗ bằng `ReservationWorker__Enabled=true` và cấp connection string cho tiến trình/service. Mỗi vòng bảo trì gọi BLL qua DI scope riêng. Worker đọc cấu hình từ thư mục executable.

Kiểm tra console không xác minh việc đăng ký/chạy dưới Service Control Manager. Xem trạng thái và giới hạn kiểm chứng hiện tại trong `ASM_REVIEW_STATUS.md`.
