# PRN222_SE1905_TRANDOANTHANHHA_ASM02 — Razor Pages

Sinh viên: Trần Đoàn Thanh Hà. Cập nhật 30/10/2026.

Module quản trị ô kho `/StorageUnits` dùng Razor Pages tại `src/SelfStorageManagementSystem.Web/Pages/StorageUnits/`: CRUD, tìm kiếm, validation, phân quyền và phạm vi cơ sở. Có SignalR `/storageUnitHub` đồng bộ thay đổi trên trình duyệt khác và báo cáo đặt chỗ theo khoảng ngày. MVC vẫn phục vụ các trang đăng nhập/trang chủ dùng chung.

UI gọi BLL/service; repository/DAO ở DAL; DbContext và entity ánh xạ SQL Server theo Database First. Không thay đổi schema.

```powershell
.\run.ps1
# Hoặc .\run.bat
dotnet build .\PRN222_SE1905_TRANDOANTHANHHA_ASM02.sln
dotnet test .\PRN222_SE1905_TRANDOANTHANHHA_ASM02.sln
```

Kết quả: 192/192 test (37 nghiệp vụ + 155 web), không skip; bộ web có E2E hai trình duyệt. Đã kiểm tra form tìm kiếm Razor Pages bằng Edge, không có lỗi console. Xem `../SUBMISSION_GUIDE.md` để cấu hình kết nối/tài khoản và xem vị trí log.