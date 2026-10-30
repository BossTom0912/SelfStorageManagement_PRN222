# PRN222_SE1905_TRANDOANTHANHHA_ASM01 — MVC + SignalR

Sinh viên: Trần Đoàn Thanh Hà. Cập nhật 30/10/2026.

Module quản trị ô kho tại `/StorageUnits` sử dụng ASP.NET Core MVC: `Controllers/StorageUnitsController.cs`, `Models/StorageUnitViewModels.cs`, `Views/StorageUnits/`. Có CRUD, tìm kiếm, phân trang, DataAnnotations, kiểm tra quyền/phạm vi cơ sở qua service và chống CSRF trên POST. Nhân viên chỉ đọc; quản lý/quản trị viên ghi dữ liệu theo quyền hiện tại trong DB.

SignalR `/storageUnitHub` phát thông báo tạo/sửa/xóa; `wwwroot/js/storage-units-realtime.js` cập nhật bảng ở trình duyệt khác. Đã kiểm tra CRUD trên hai phiên Edge độc lập.

Bản Razor Pages cũ của module được chuyển vào `docs/LegacyRazorStorageUnits` để tham khảo, không còn được biên dịch/đăng ký route. Các module phụ vẫn có Razor Pages. UI chỉ gọi BLL; DAL dùng EF Core Database First. Không thay đổi schema hoặc entity sinh tự động.

```powershell
.\run.ps1
# Hoặc .\run.bat
dotnet build .\PRN222_SE1905_TRANDOANTHANHHA_ASM01.sln
dotnet test .\PRN222_SE1905_TRANDOANTHANHHA_ASM01.sln
```

Kết quả: 190/190 test (37 nghiệp vụ + 153 web), không skip. Các test từ chối MVC kiểm tra chuyển hướng `/Account/Forbidden` và xác nhận không tạo/xóa dữ liệu trái phép. Xem `../SUBMISSION_GUIDE.md` để cấu hình kết nối/tài khoản và xem vị trí log.