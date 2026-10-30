# PRN222_SE1905_VOTHAIHOANGKHOI_ASM03 — Blazor Server

Sinh viên: Võ Thái Hoàng Khôi. Cập nhật 30/10/2026.

Màn hình chính: `/Blazor/StorageUnits`, được liên kết từ thanh điều hướng quản trị.

- `Components/StorageUnitManager.razor`: CRUD có xác nhận xóa, tìm kiếm và phân trang phía server. Form dùng DataAnnotations; DTO cập nhật/xóa mang `ExpectedUpdatedAt` để kiểm soát xung đột. Không đưa entity EF ra UI.
- `Pages/Blazor/StorageUnits.cshtml`: Razor host của component, có policy `StaffOnly`.
- `Program.cs`: đăng ký Blazor Server, hub `/_blazor` yêu cầu `StaffOnly` và đóng kết nối khi cookie hết hạn.
- Mỗi thao tác dùng DI scope riêng rồi gọi `IStorageUnitAdminService`, kiểm tra lại quyền/phạm vi cơ sở ở BLL, tránh giữ DbContext cho toàn circuit. Nhân viên chỉ có nút xem; quản lý/quản trị viên có thao tác ghi.
- `Views/Shared/_Layout.cshtml`: base URL cho đường dẫn Blazor. `Components/_Imports.razor` nhập namespace Web để đăng ký event binding. Nút bị khóa cho đến khi circuit tương tác sẵn sàng.
- Các màn hình MVC cũ `/StorageUnits` được giữ để tương thích/xem chi tiết; các module phụ vẫn có Razor Pages.

```powershell
.\run.ps1
# Hoặc .\run.bat
dotnet build .\PRN222_SE1905_VOTHAIHOANGKHOI_ASM03.sln
dotnet test .\PRN222_SE1905_VOTHAIHOANGKHOI_ASM03.sln
```

Kết quả: 194/194 test (37 nghiệp vụ + 157 web), không skip. Test mới `BlazorStorageUnitsTests.cs` xác nhận truy cập host/hub của người chưa đăng nhập, khách hàng, nhân viên và quản trị viên; kiểm tra base URL và event binding. Edge đã thực hiện CRUD/tìm kiếm qua circuit thật, chặn khách hàng, chụp desktop/mobile; không có lỗi console trong luồng kiểm tra. Xem `../SUBMISSION_GUIDE.md` để cấu hình kết nối và vị trí log/ảnh.