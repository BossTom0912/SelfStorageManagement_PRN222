# PRN222_SE1905_VOTHAIHOANGKHOI_ASM03 — Hệ Thống Quản Lý Kho Tự Quản (Self-Storage Management)

Dự án bài tập Assignment 03 môn PRN222 (C# & .NET Core / ASP.NET Core MVC & Realtime) thực hiện theo kiến trúc 3 tầng chuẩn mực của Đại học FPT. Bài tập kế thừa phần lõi và tính năng SignalR từ ASM02, chuyển đổi hoàn toàn module quản trị nghiệp vụ cốt lõi **StorageUnits** sang mô hình **ASP.NET Core MVC (Controllers & Views)** kết hợp Dependency Injection, async/await, pagination, server-side authorization, anti-forgery token và Razor HTML encoding.

---

## 1. Công nghệ sử dụng (.NET Tech Stack)

- **Framework**: .NET 8 (LTS) — C# 12
- **Tầng giao diện (Presentation / UI - MVC)**: 
  - ASP.NET Core MVC Controllers: `StorageUnitsController`, `AccountController`, `HomeController`.
  - Razor Views tương ứng trong `Views/StorageUnits` (`Index.cshtml`, `Create.cshtml`, `Edit.cshtml`, `Details.cshtml`, `Delete.cshtml`).
  - SignalR Realtime Hub (`/storageUnitHub`) phát và nhận sự kiện CUD đồng bộ hóa danh sách tức thì giữa các client.
- **Tầng nghiệp vụ (Business Logic Layer - BLL)**: C# Class Library, Service-oriented, Dependency Injection (DI) ServiceCollection Extensions, Typed Domain Exceptions (`NotFoundException`, `ConflictException`, `ForbiddenException`, `BadRequestException`, `UnauthorizedException`).
- **Tầng truy cập dữ liệu (Data Access Layer - DAL)**: C# Class Library, Entity Framework Core 8 (`EF Core`), Generic Repository & Specialized Unit Repositories (`IStorageUnitAdminRepository`, `IReservationRepository`, `IUserRepository`, etc.).
- **Cơ sở dữ liệu (Database)**: SQL Server Database-First schema (`SelfStorageManagement`).
- **Xác thực & Phân quyền (Authentication & Authorization)**: Cookie Authentication (`CookieAuthenticationDefaults.AuthenticationScheme`), Claims-based Authorization Policies (`AdminOnly`, `ManagerOnly`, `StaffOnly`, `CustomerOnly`), Facility Scope Authorization.
- **Bảo mật Form & Chống tấn công**: DataAnnotations Validation, `[ValidateAntiForgeryToken]` trên tất cả các Action POST, Razor HTML encoding chống XSS, Anti-Open-Redirect (`Url.IsLocalUrl`).
- **Kiểm thử tự động (Unit & Integration Testing)**: xUnit, Moq, `Microsoft.AspNetCore.Mvc.Testing` (WebApplicationFactory), EF Core In-Memory Database.

---

## 2. Kiến trúc giải pháp (3-Tier Architecture)

Hệ thống tuân thủ nghiêm ngặt chuỗi phụ thuộc đơn hướng:
$$\text{Presentation (MVC / Web)} \longrightarrow \text{BusinessLogic (BLL)} \longrightarrow \text{DataAccess (DAL)} \longrightarrow \text{SQL Server (DbContext)}$$

1. **`SelfStorageManagementSystem.Web` (Presentation - MVC)**:
   - Module nghiệp vụ cốt lõi `StorageUnits` được triển khai hoàn chỉnh bằng **`StorageUnitsController`** kế thừa `Microsoft.AspNetCore.Mvc.Controller` cùng bộ View Razor tại `Views/StorageUnits/`.
   - **Tuyệt đối không inject `DbContext`** vào Controller; chỉ tương tác qua BLL Service duy nhất: **`IStorageUnitAdminService`**.
   - Các ViewModels riêng biệt (`StorageUnitsIndexViewModel`, `StorageUnitCreateViewModel`, `StorageUnitEditViewModel`, `StorageUnitDetailsViewModel`, `StorageUnitDeleteViewModel`) đóng gói dữ liệu truyền tải giữa Controller và View.
   - Thanh điều hướng chính (`_Layout.cshtml`) liên kết trực tiếp tới các Action MVC (`asp-controller="StorageUnits" asp-action="Index"`).
2. **`SelfStorageManagementSystem.BusinessLogic` (BLL)**:
   - Đóng gói toàn bộ quy tắc nghiệp vụ, kiểm tra thẩm quyền theo cơ sở (`actorUserId`), kiểm soát xung đột đồng thời (Optimistic Concurrency với `ExpectedUpdatedAt`), bảo toàn cam kết sức chứa và tham chiếu.
   - Cung cấp DTOs chuẩn mực (`StorageUnitAdminDto`, `StorageUnitAdminDetailDto`, `CreateStorageUnitRequest`, `UpdateStorageUnitRequest`, `StorageUnitFilterRequest`, `StorageUnitLookupsDto`).
3. **`SelfStorageManagementSystem.DataAccess` (DAL)**:
   - Ánh xạ database-first từ schema SQL Server hiện hữu.
   - Quản lý giao dịch và khóa logic tại repository (`StorageUnitAdminRepository`).

---

## 3. Các chức năng chính (Core MVC Features)

1. **MVC StorageUnits Index / Search / Pagination**:
   - URL: `/StorageUnits` (gọi Action `Index` của `StorageUnitsController`).
   - Lọc đa tiêu chí: Cơ sở (`facilityId`), Loại ô kho (`unitTypeId`), Trạng thái vật lý (`physicalStatus`), Từ khóa tìm kiếm (`searchTerm`).
   - Phân trang bất đồng bộ (`PagedResult<StorageUnitAdminDto>`) với các liên kết chuyển trang bảo toàn trạng thái lọc hiện hành.
   - Tích hợp SignalR JavaScript lắng nghe sự kiện CRUD (`Receiver_CreateStorageUnit`, `Receiver_UpdateStorageUnit`, `Receiver_DeleteStorageUnit`) để cập nhật bảng thời gian thực.
2. **MVC StorageUnits Create**:
   - URL: `/StorageUnits/Create` (GET hiển thị form, POST xử lý dữ liệu).
   - Kiểm tra thẩm quyền: Chỉ Quản lý cơ sở (`facility_manager`), Quản lý vận hành (`business_operations_manager`) và Quản trị viên (`system_administrator`) mới có quyền tạo ô kho.
   - Nạp danh mục cơ sở và khu vực theo phạm vi được phân công của người dùng hiện hành. Hỗ trợ AJAX action `FacilityLookups` nạp động loại kho và khu vực khi chọn cơ sở.
   - Bảo vệ token chống giả mạo (`[ValidateAntiForgeryToken]`).
3. **MVC StorageUnits Edit**:
   - URL: `/StorageUnits/Edit?id={id}` (GET hiển thị dữ liệu ô kho, POST cập nhật).
   - Bảo vệ tính bất biến: Cơ sở (`FacilityId`) và Loại ô kho (`UnitTypeId`) không được phép thay đổi sau khi tạo.
   - Kiểm soát xung đột phiên bản đồng thời (Optimistic Concurrency): Sử dụng `ExpectedUpdatedAt` dạng round-trip. Nếu dữ liệu trên server đã bị thay đổi trước đó, trả về cảnh báo `ConflictException` kèm nút tải lại dữ liệu mới nhất.
   - Tích hợp cảnh báo realtime khi có client khác vừa cập nhật ô kho đang xem.
4. **MVC StorageUnits Details**:
   - URL: `/StorageUnits/Details?id={id}`.
   - Hiển thị toàn diện thông số kỹ thuật (dài, rộng, cao, diện tích sàn, thể tích), biểu giá niêm yết hiện hành, hiện trạng thuê khách hàng và thống kê tham chiếu quan hệ (lượt gán, kiểm tra, bảo trì, hỗ trợ).
5. **MVC StorageUnits Delete**:
   - URL: `/StorageUnits/Delete?id={id}` (GET hiển thị xác nhận đối chiếu, POST thực hiện xóa).
   - **GET an toàn**: Tuyệt đối không xóa hay thay đổi dữ liệu tại GET.
   - **POST xóa an toàn**: Kiểm tra ràng buộc chính sách (chỉ xóa ô ở trạng thái `available`) và kiểm tra toàn vẹn tham chiếu (chặn xóa khi ô kho có dữ liệu lịch sử liên kết). Truyền `ExpectedUpdatedAt` ngăn ngừa xóa đè khi dữ liệu đã stale.

---

## 4. Cấu hình Connection String & Tài khoản Quản trị

Tuân thủ quy tắc bảo mật không hardcode credentials trong source code:

### 4.1. Cấu hình Chuỗi kết nối Database
Mặc định trong `src/SelfStorageManagementSystem.Web/appsettings.json` cấu hình LocalDB an toàn không mật khẩu:
```json
{
  "ConnectionStrings": {
    "DefaultConnection": "Server=(localdb)\\mssqllocaldb;Database=SelfStoragePRN222_WebDemo;Trusted_Connection=True;MultipleActiveResultSets=true;TrustServerCertificate=True"
  }
}
```
Khi kết nối với SQL Server production hoặc instance thật, thiết lập qua User Secrets:
```powershell
cd src/SelfStorageManagementSystem.Web
dotnet user-secrets set "ConnectionStrings:DefaultConnection" "Server=YOUR_SERVER;Database=SelfStorageManagement;User Id=YOUR_USER;Password=YOUR_SECRET;TrustServerCertificate=True"
```

### 4.2. Danh sách tài khoản thử nghiệm
Hệ thống hỗ trợ bootstrap dữ liệu demo trong môi trường Development khi bật cấu hình `Bootstrap:Enabled`:
- **Quản trị viên (Admin)**: `admin@selfstorage.local` (Quyền toàn hệ thống)
- **Quản lý cơ sở (Manager)**: `manager.d1@selfstorage.local` (CRUD ô kho thuộc cơ sở Quận 1)
- **Nhân viên cơ sở (Staff)**: `staff.d1@selfstorage.local` (Chỉ xem ô kho, tiếp nhận khách)
- **Khách hàng (Customer)**: `customer1@example.com` (Thuê kho, đặt chỗ cá nhân)

Mật khẩu demo mặc định có thể cấu hình an toàn qua User Secrets:
```powershell
dotnet user-secrets set "DemoAccounts:DefaultPassword" "Admin@123456"
```

---

## 5. Hướng dẫn Build, Test và Khởi chạy

### 5.1. Build Solution
Tại thư mục `D:\PRN222\PRN222_SE1905_VOTHAIHOANGKHOI_ASM03`:
```powershell
dotnet build PRN222_SE1905_VOTHAIHOANGKHOI_ASM03.sln
```

### 5.2. Chạy Kiểm thử Tự động (Automated Tests)
Chạy toàn bộ 190 bài kiểm thử đơn vị và tích hợp:
```powershell
dotnet test PRN222_SE1905_VOTHAIHOANGKHOI_ASM03.sln
```
*Kết quả:*
- `SelfStorageManagementSystem.BusinessLogicTests`: 37 passed, 0 failed.
- `SelfStorageManagementSystem.WebTests`: 153 passed, 0 failed (Bao gồm 5 bài test chuyên biệt `StorageUnitsMvcControllerTests`).
- **Tổng cộng: 190/190 bài test đạt 100%.**

### 5.3. Khởi chạy Ứng dụng Web
```powershell
cd src/SelfStorageManagementSystem.Web
dotnet run
```
Truy cập trình duyệt tại `https://localhost:7041` hoặc cổng hiển thị trên console. Đăng nhập bằng tài khoản Quản lý cơ sở hoặc Quản trị viên để trải nghiệm giao diện MVC tại menu **"Quản trị ô kho"** (`/StorageUnits`).

---

## 6. Điểm khác biệt so với ASM02 & Lợi ích kiến trúc

| Tiêu chí | ASM02 | ASM03 |
| :--- | :--- | :--- |
| **Kiến trúc giao diện nghiệp vụ StorageUnits** | Sử dụng Razor Pages (`Pages/StorageUnits/*.cshtml.cs`) | **Chuyển đổi hoàn toàn sang ASP.NET Core MVC Controller (`StorageUnitsController.cs`) & Views (`Views/StorageUnits/*.cshtml`)** |
| **Phụ thuộc dữ liệu trong Controller** | Razor Pages Model gọi `IStorageUnitAdminService` | **Controller chỉ inject `IStorageUnitAdminService` và `IStorageUnitRealtimePublisher`, hoàn toàn không có `DbContext`** |
| **Bảo mật Form POST** | ValidateAntiForgeryToken trên PageHandler | **Thuộc tính `[ValidateAntiForgeryToken]` trên tất cả các Action POST (`Create`, `Edit`, `Delete`)** |
| **Điều hướng hệ thống** | Menu dẫn tới Razor Pages `/StorageUnits` | **Thanh điều hướng Navbar `_Layout.cshtml` sử dụng TagHelper MVC `asp-controller="StorageUnits" asp-action="Index"`** |
| **Targeted Tests** | Kiểm thử Razor Pages handlers | **Bổ sung bộ kiểm thử chuyên biệt `StorageUnitsMvcControllerTests` xác thực cấu trúc MVC, DI và kiểm tra quyền** |
