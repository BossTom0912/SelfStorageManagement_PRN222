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

## Cập nhật ngày 03/11/2026

- Xóa ô khả dụng được phép khi số ô còn lại vẫn đủ cho các đặt chỗ chưa phân kho. Giữ kiểm tra quyền, phạm vi cơ sở, timestamp, capacity lock và transaction.
- Ô có lịch sử hoặc tham chiếu được giữ lại và ngừng niêm yết; giao diện và sự kiện SignalR phân biệt đúng với xóa vĩnh viễn.
- Danh sách nhận snapshot DTO qua Hub thay vì `fetch` toàn bộ HTML. Đồng bộ lại sau reconnect và retry vẫn giữ bộ lọc, phân trang và thứ tự phản hồi.
- Dọn log debug và đặt SignalR browser logging về `None` trên các trang ô kho. Traffic WebSocket vẫn xem được trong Network; POST form và tải trang theo thao tác người dùng vẫn bình thường.

Kiểm tra lại từ source: **203 test pass** (37 nghiệp vụ, 166 web), gồm một test hai phiên Edge với Kestrel/WebSocket thật và database InMemory riêng: tạo–sửa–xóa khi có booking confirmed nhưng đủ capacity, đồng bộ trình duyệt thứ hai, không fetch HTML và không log debug realtime. Test POST Razor Pages xác nhận ô có lịch sử được ngừng niêm yết và giữ lịch sử.

```powershell
dotnet test .\PRN222_SE1905_TRANDOANTHANHHA_ASM02.sln -c Release --filter "FullyQualifiedName!~T18TwoBrowserRealtimeE2ETests"
```

Sáu test T18 riêng không nằm trong kết quả 203 trên; chúng yêu cầu database SQL Server `SelfStoragePRN222_WebE2eTest`. Các test InMemory không xác minh cơ chế khóa đồng thời của SQL Server. Không thay đổi schema hoặc dữ liệu đặt chỗ đang dùng để sửa lỗi này.
