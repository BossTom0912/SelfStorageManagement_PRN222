# Bộ bài ASM01–04 — Võ Thái Hoàng Khôi

Cập nhật: 30/10/2026. Bộ nộp bài nằm trong bốn thư mục `PRN222_SE1905_VOTHAIHOANGKHOI_ASM01` đến `ASM04` tại repository `Final_Project_Web`.

| Bài | Công nghệ / màn hình demo | Kết quả chạy lại |
| --- | --- | --- |
| ASM01 | MVC CRUD `/StorageUnits` + SignalR `/storageUnitHub` | 190/190 (37 nghiệp vụ + 153 web) |
| ASM02 | Razor Pages `/StorageUnits`, báo cáo đặt chỗ, SignalR | 192/192 (37 nghiệp vụ + 155 web, gồm E2E hai trình duyệt) |
| ASM03 | Blazor Server `/Blazor/StorageUnits`, Razor host, DI, async và phân trang | 194/194 (37 nghiệp vụ + 157 web) |
| ASM04 | Console / Windows Service mới; giữ Web + API + WPF | 700/700 (250 nghiệp vụ/API/WPF + 450 web) |

**Tổng: 1.276 test đạt, 0 thất bại, 0 bỏ qua.** Build cả bốn solution ASM và solution API/WPF của ASM04 đạt. Bộ ASM04 có 19 test SQL concurrency đã chạy trên database test riêng.

## Chạy ASM01–03 và phần Web của ASM04

Mở solution mang tên cùng thư mục hoặc mở PowerShell tại từng thư mục:

```powershell
.\run.ps1
# Hoặc
.\run.bat
# Chạy toàn bộ test của solution trong thư mục hiện tại
dotnet test .\PRN222_SE1905_VOTHAIHOANGKHOI_ASM01.sln
```

Đổi `ASM01` trong lệnh test thành số bài tương ứng. Script chạy dùng đường dẫn dựa trên vị trí script nên có thể gọi từ thư mục khác.

Cần .NET 8 runtime/SDK và SQL Server/LocalDB có schema Database First của dự án. Cấu hình kết nối qua `ConnectionStrings__DefaultConnection`; cấu hình tài khoản bootstrap bằng `AdminAccount__Email`, `AdminAccount__Password`, `Bootstrap__Enabled` khi chủ động khởi tạo tài khoản demo trên database riêng. Khi không muốn thay đổi mật khẩu demo lúc startup, đặt `Bootstrap__Enabled=false`. Không dùng dữ liệu ứng dụng thật để chạy bộ test.

Các profile Web cũ mặc định dùng cổng 7041/5041; khi cần chạy nhiều bài cùng lúc, truyền `--urls` với cổng khác nhau và `--no-launch-profile`. Kiểm thử trình duyệt lần này dùng HTTPS 7141–7144, desktop 1440×1000 và Blazor mobile 390×844.

## Chạy ASM04 console và kiểm tra Windows Service

Tại `PRN222_SE1905_VOTHAIHOANGKHOI_ASM04`:

```powershell
# Chạy liên tục, log mỗi 5 giây, Ctrl+C để dừng
.\run-worker.ps1
# Log ba lần tại giây 0, 5, 10 rồi tự dừng
.\run-worker.ps1 --Worker:MaxCycles=3
# Kiểm tra đăng ký service và mở Services trên máy
.\check-service.ps1 -OpenServices
# Cài và chạy nếu cần: phải mở PowerShell bằng Run as Administrator
.\install-service.ps1 -Start
```

Service name: `PRN222_VOTHAIHOANGKHOI_ASM04`. Lần kiểm tra thực tế: **chưa cài service trên máy**; tài khoản thực thi không có quyền Administrator. Đã mở Services, truy vấn Service Control Manager, build và publish executable, chạy console trực tiếp lẫn bản publish thành công. Chưa chạy dưới Service Control Manager vì service chưa được đăng ký.

Console ghi bằng `ILogger`; khi chạy Windows Service, log Information trở lên ghi vào Event Viewer → Windows Logs → Application, source cùng tên service. File cấu hình của worker được đọc từ thư mục executable. Mặc định chỉ heartbeat, không thay đổi database. Có thể bật tác vụ giải phóng giữ chỗ qua `ReservationWorker__Enabled=true` và cung cấp connection string cho tiến trình/service; worker gọi `IReservationService` qua DI scope riêng.

## Bằng chứng và giới hạn

- Kiểm thử trình duyệt bằng Playwright + Microsoft Edge: ASM01 tạo/sửa/xóa bằng MVC và nhận cập nhật ở phiên độc lập; ASM02 tìm kiếm bằng Razor Pages; ASM03 thao tác CRUD/tìm kiếm qua Blazor, chặn khách hàng; ASM04 Web và SignalR hoạt động. Không có lỗi console trong các luồng đã kiểm tra.
- Dữ liệu thử trình duyệt nằm trong LocalDB `SelfStoragePRN222_WebDemo`, được tạo mới bằng script bootstrap có kiểm tra database tồn tại; đã dọn ô kho tạm. Test SQL concurrency dùng `localhost/SelfStoragePRN222_Test`; E2E có sẵn dùng LocalDB `SelfStoragePRN222_WebE2eTest`.
- Log, TRX, ảnh và script kiểm thử bổ sung nằm ngoài repository tại `C:\Users\tommy\.codex\visualizations\2026\10\30\01a18af3-cc2c-77d0-abd3-d043b517fe46\asm-verification`.
- Có cảnh báo có sẵn về tên entity EF viết thường, nullable và raw SQL trong test; không có lỗi build. Không sửa schema/entity sinh tự động, không commit hoặc push.
- Thư mục ASM01 tên cũ còn được giữ làm bản gốc vì tiến trình khác đang khóa thư mục. **Nộp bản `PRN222_SE1905_VOTHAIHOANGKHOI_ASM01` mới**, không dùng thư mục tên cũ.