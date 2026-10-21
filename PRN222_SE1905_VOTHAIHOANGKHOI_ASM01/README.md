# PRN222_SE1905_VOTHAIHOANGKHOI_ASM01 — Hệ Thống Quản Lý Kho Tự Quản (Self-Storage Management)

Dự án bài tập Assignment 01 môn PRN222 (C# & .NET Core / ASP.NET Core) thực hiện theo kiến trúc 3 tầng chuẩn mực của Đại học FPT, tuân thủ nghiêm ngặt mô hình Enterprise, tách biệt các tầng chức năng và đảm bảo an toàn bảo mật.

---

## 1. Công nghệ sử dụng (.NET Tech Stack)

- **Framework**: .NET 8 (LTS) — C# 12
- **Tầng giao diện (Presentation / UI)**: ASP.NET Core Razor Pages kết hợp MVC Controllers cho luồng xác thực Cookie.
- **Tầng nghiệp vụ (Business Logic Layer - BLL)**: C# Class Library, Service-oriented, Dependency Injection (DI) ServiceCollection Extensions, Typed Domain Exceptions (`NotFoundException`, `ConflictException`, `ForbiddenException`, `BadRequestException`, `UnauthorizedException`).
- **Tầng truy cập dữ liệu (Data Access Layer - DAL)**: C# Class Library, Entity Framework Core 8 (`EF Core`), Generic Repository & Specialized Unit Repositories (`IStorageUnitAdminRepository`, `IUserRepository`, etc.).
- **Cơ sở dữ liệu (Database)**: SQL Server Database-First schema (`SelfStorageManagement`).
- **Xác thực & Phân quyền (Authentication & Authorization)**: Cookie Authentication (`CookieAuthenticationDefaults.AuthenticationScheme`), Claims-based Authorization Policies (`AdminOnly`, `ManagerOnly`, `StaffOnly`, `CustomerOnly`).
- **Bảo mật Form & Chống tấn công**: DataAnnotations Validation, AutoValidateAntiforgeryToken, CSP / HSTS, Anti-Open-Redirect (`Url.IsLocalUrl`).
- **Kiểm thử tự động (Unit & Integration Testing)**: xUnit, Moq, `Microsoft.AspNetCore.Mvc.Testing` (WebApplicationFactory), EF Core In-Memory Database.

---

## 2. Kiến trúc giải pháp (3-Tier Architecture)

Hệ thống được thiết kế hoàn toàn theo chuỗi phụ thuộc đơn hướng:
$$\text{Presentation (Web)} \longrightarrow \text{BusinessLogic (BLL)} \longrightarrow \text{DataAccess (DAL)} \longrightarrow \text{SQL Server (DbContext)}$$

1. **`SelfStorageManagementSystem.Web` (Presentation)**:
   - Các trang Razor Pages phụ trách hiển thị và tiếp nhận thao tác CRUD danh mục ô kho (`/StorageUnits`), tài khoản quản trị (`/Admin/Accounts`), đăng nhập/đăng ký (`/Account/Login`, `/Account/Register`).
   - Tuyệt đối **không** tham chiếu đến tầng DAL, không inject `DbContext` hay các entity Db trực tiếp vào PageModel/Controller.
   - Giao tiếp với BLL thông qua các Service Interfaces: `IStorageUnitAdminService`, `IAccountIdentityService`, `IAdminAccountService`, `IFacilityScopeService`.
2. **`SelfStorageManagementSystem.BusinessLogic` (BLL)**:
   - Đóng gói toàn bộ quy tắc nghiệp vụ, kiểm tra ràng buộc logic, kiểm tra quyền hạn theo phạm vi cơ sở (Facility Scope Authorization), tính toán biểu giá và xử lý các ngoại lệ nghiệp vụ.
   - Chuyển đổi dữ liệu giữa Entity và các DTO / Request / Response.
3. **`SelfStorageManagementSystem.DataAccess` (DAL)**:
   - Nắm giữ `SelfStorageDbContext` được ánh xạ database-first từ schema SQL Server.
   - Triển khai Repository pattern (Generic Repository và Specialized Repositories), quản lý các câu truy vấn LINQ tối ưu và giao dịch dữ liệu.

---

## 3. Các chức năng chính (Core Features)

1. **Xác thực & Phiên làm việc Cookie an toàn**:
   - Đăng nhập, đăng xuất bảo vệ bằng Cookie Authentication thời hạn 5 phút, hỗ trợ sliding expiration.
   - Khóa tài khoản sau chuỗi đăng nhập thất bại, lưu vết lịch sử đăng nhập vào `login_history`.
   - Chống Open Redirect: Chỉ chấp nhận chuyển hướng nội bộ (`Url.IsLocalUrl`).
   - Đăng xuất bằng HTTP POST có xác thực token CSRF; vô hiệu hóa việc chuyển đổi trạng thái phiên làm việc qua HTTP GET.
2. **Bảo vệ URL & Phân quyền theo vai trò (Role-based Authorization)**:
   - Kiểm tra vai trò trên server cả khi người dùng nhập URL trực tiếp vào thanh địa chỉ.
   - Phân định rõ 4 chính sách bảo mật: `AdminOnly` (`SYSTEM_ADMIN`), `ManagerOnly` (`FACILITY_MANAGER`), `StaffOnly` (`FACILITY_STAFF`), `CustomerOnly` (`CUSTOMER`).
   - Kiểm tra quyền truy cập dữ liệu theo phạm vi cơ sở được phân công (`FacilityScopeService`).
3. **CRUD Quản trị Danh mục Ô kho (Storage Units)**:
   - **Danh sách & Tìm kiếm (Index / Search)**: Tìm kiếm theo mã ô kho, lọc đa tiêu chí (cơ sở, loại ô kho, khu vực, trạng thái vật lý, trạng thái niêm yết), phân trang và sắp xếp linh hoạt.
   - **Thêm mới (Create)**: Form tạo mới với DataAnnotations validation, kiểm tra trùng mã ô kho trên phạm vi cơ sở, giới hạn danh sách cơ sở theo quyền hạn nhân sự.
   - **Chỉnh sửa (Edit)**: Cập nhật thông số ô kho, bảo vệ các thuộc tính bất biến (`FacilityId`, `UnitTypeId`), cơ chế kiểm soát xung đột phiên cập nhật đồng thời (Optimistic Concurrency qua `ExpectedUpdatedAt`).
   - **Xem chi tiết (Details)**: Hiển thị đầy đủ thông số kỹ thuật, thể tích, diện tích sàn, biểu giá hiện hành và thống kê tham chiếu quan hệ.
   - **Xóa an toàn & Chuyển đổi trạng thái (Delete / Inactive Policy)**:
     - GET chỉ hiển thị giao diện đối chiếu, tuyệt đối không xóa dữ liệu tại GET.
     - Kiểm tra toàn diện các quan hệ phụ thuộc (hợp đồng thuê `rental_agreement`, đặt chỗ `reservation`, bàn giao `handover_record`, phân bổ `unit_allocation`, sự cố bảo trì `maintenance_work_order`).
     - **Chặn xóa cứng** khi có dữ liệu phụ thuộc hoặc khi ô kho đang được sử dụng/đặt trước; hướng dẫn chuyển sang ngừng niêm yết hoặc Inactive để bảo toàn tính toàn vẹn tham chiếu của cơ sở dữ liệu.

---

## 4. Cấu hình Connection String & Tài khoản Quản trị (Admin Configuration)

Tuân thủ nguyên tắc không hardcode thông tin nhạy cảm (secrets) vào mã nguồn hay git repository.

### 4.1. Cấu hình Chuỗi kết nối Database

Mặc định file `appsettings.json` tại `src/SelfStorageManagementSystem.Web/appsettings.json` sử dụng chuỗi kết nối LocalDB an toàn không mật khẩu:
```json
{
  "ConnectionStrings": {
    "DefaultConnection": "Server=(localdb)\\mssqllocaldb;Database=SelfStoragePRN222_WebDemo;Trusted_Connection=True;MultipleActiveResultSets=true;TrustServerCertificate=True"
  }
}
```
Để kết nối tới SQL Server thực tế của bạn, cấu hình thông qua **.NET User Secrets** hoặc biến môi trường:
```powershell
cd src/SelfStorageManagementSystem.Web
dotnet user-secrets set "ConnectionStrings:DefaultConnection" "Server=YOUR_SERVER;Database=SelfStorageManagement;User Id=YOUR_USER;Password=YOUR_SECRET;TrustServerCertificate=True"
```

### 4.2. Cấu hình Tài khoản Admin & Demo Mật khẩu an toàn

Tài khoản người dùng được lưu trữ trong cơ sở dữ liệu SQL Server với mật khẩu mã hóa PBKDF2 (`IPasswordHasher`). Để khởi tạo mật khẩu phát triển/demo mà không lưu mật khẩu thô vào file cấu hình, thiết lập mật khẩu mẫu qua User Secrets hoặc biến môi trường:
```powershell
# Cấu hình qua User Secrets (Khuyến nghị khi phát triển cục bộ)
dotnet user-secrets set "DemoAccounts:DefaultPassword" "Admin@123456"

# Hoặc thiết lập qua biến môi trường của hệ điều hành
$env:DEMO_DEFAULT_PASSWORD="Admin@123456"
```

### 4.3. Danh sách tài khoản thử nghiệm chuẩn

| Tên đăng nhập (Email) | Vai trò hệ thống | Phạm vi quyền hạn | Mật khẩu mẫu demo |
| :--- | :--- | :--- | :--- |
| `admin@selfstorage.local` | System Administrator | Toàn quyền cấu hình & quản trị hệ thống | Đặt theo cấu hình mục 4.2 |
| `manager.d1@selfstorage.local` | Facility Manager | Quản lý ô kho cơ sở Quận 1 (CRUD) | Đặt theo cấu hình mục 4.2 |
| `staff.d1@selfstorage.local` | Facility Staff | Xem ô kho, thực hiện thủ tục kho (Chỉ đọc ô kho) | Đặt theo cấu hình mục 4.2 |
| `customer1@example.com` | Storage Customer | Khách hàng thuê kho | Đặt theo cấu hình mục 4.2 |

---

## 5. Hướng dẫn Build, Test và Khởi chạy (Commands)

### 5.1. Build toàn bộ Solution
Tại thư mục gốc của bài `PRN222_SE1905_VOTHAIHOANGKHOI_ASM01`:
```powershell
dotnet build PRN222_SE1905_VOTHAIHOANGKHOI_ASM01.sln
```

### 5.2. Chạy bộ Kiểm thử tự động (Automated Tests)
Toàn bộ 183 bài kiểm thử unit & integration độc lập với SQL Server bên ngoài (sử dụng EF In-Memory và Mocking) đều pass 100%:
```powershell
dotnet test PRN222_SE1905_VOTHAIHOANGKHOI_ASM01.sln
```
*Kết quả:*
- `SelfStorageManagementSystem.BusinessLogicTests`: 35 passed, 0 failed.
- `SelfStorageManagementSystem.WebTests`: 148 passed, 0 failed.
- **Tổng cộng: 183 bài test pass 100%.**

### 5.3. Khởi chạy Ứng dụng Web
```powershell
cd src/SelfStorageManagementSystem.Web
dotnet run
```
Truy cập trình duyệt tại địa chỉ hiển thị trên terminal (ví dụ: `https://localhost:7041`).

---

## 6. Điểm khác biệt và Đặc tính của ASM01 (So với các giai đoạn tiếp theo)

- **Độc lập và Tập trung**: ASM01 thiết lập nền móng kiến trúc vững chắc với 3 tầng phân định ranh giới rõ ràng (`Presentation -> BLL -> DAL -> DbContext`).
- **Giao diện Razor Pages**: Tập trung hoàn thiện giao diện quản trị CRUD danh mục ô kho vật lý bằng Razor Pages chuẩn, xác thực dữ liệu qua DataAnnotations và bảo vệ bằng Cookie Authentication.
- **Xử lý ràng buộc quan hệ**: Đặt trọng tâm vào việc bảo vệ an toàn dữ liệu: không cho phép xóa ô kho khi có các bản ghi phụ thuộc (hợp đồng, hóa đơn, bàn giao, đặt cọc), chuyển hướng xử lý sang Inactive/ngừng niêm yết.
- **Tính độc lập của Solution**: Dự án không dùng project reference trỏ ra bên ngoài, có thể build và chạy kiểm thử độc lập ở bất kỳ môi trường nào có .NET 8.
