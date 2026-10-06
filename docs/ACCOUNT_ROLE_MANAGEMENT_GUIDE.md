# Hướng Dẫn Chức Năng 1: Account & Role Management

Tài liệu hướng dẫn kỹ thuật và vận hành cho Chức năng số 1: **"Account & Role Management"** trong hệ thống **Self-Storage Facility Rental and Management System** (PRN222).

---

## 1. Kiến Trúc & Công Nghệ

Hệ thống tuân thủ nghiêm ngặt mô hình **3-Layer Architecture** kết hợp ứng dụng client máy trạm WPF độc lập:

```text
WPF Client (Presentation)
  └── gọi REST API qua HTTPS (kèm JWT Bearer Token trong bộ nhớ)
        ↓
Presentation Layer (ASP.NET Core Web API 8.0)
  ├── AuthController & AdminAccountsController
  ├── JWT Bearer Authentication & Token Revalidation Middleware
  └── Global Exception Handler & Swagger / OpenAPI
        ↓
BusinessLogic Layer
  ├── Services: AuthService, AdminAccountService, FacilityScopeService, AuditService, PasswordHasher, JwtTokenGenerator
  └── DTOs, Exceptions, Role & Status Constants
        ↓
DataAccess Layer
  ├── SelfStorageDbContext (Database First, Scoped lifetime)
  └── Repositories: UserRepository (IUserRepository), GenericRepository<T>
        ↓
SQL Server Database (SelfStoragePRN222)
  └── Schema: core (57 bảng, 35 triggers, ràng buộc toàn vẹn)
```

### Tại sao chọn JWT Bearer cho WPF thay vì Cookie MVC (như code thầy)?
1. **Phù hợp với kiến trúc Client-Server hiện đại:** Client WPF là ứng dụng desktop độc lập, giao tiếp với backend thông qua RESTful API chuẩn. JWT Bearer token là tiêu chuẩn công nghiệp cho API thay vì Cookie (vốn phụ thuộc vào browser cookie jar, redirect URL và form auth của MVC).
2. **Không lưu token ra đĩa:** WPF client lưu token trong RAM tĩnh (`SessionStore`), không ghi xuống `appsettings.json`, Registry hay file đĩa, đảm bảo bảo mật tuyệt đối khi đóng ứng dụng hoặc logout.
3. **Đồng bộ phân quyền tức thời (Revalidation):** Mặc dù JWT có thời hạn (vd: 60 phút), hệ thống backend kích hoạt kiểm tra trong sự kiện `OnTokenValidated`. Khi một tài khoản bị Admin đổi trạng thái thành `locked`/`disabled` hoặc bị thu hồi role, yêu cầu kế tiếp mang token cũ sẽ bị từ chối ngay lập tức (401 hoặc 403) mà không cần chờ token hết hạn.

---

## 2. Danh Sách 5 Role Chuẩn Database

Hệ thống sử dụng trực tiếp các mã vai trò (role codes) trong bảng `[core].[roles]`:
1. `storage_customer`: Khách hàng thuê kho lưu trữ.
2. `facility_staff`: Nhân viên vận hành tại cơ sở (check-in, check-out, kiểm tra kho).
3. `facility_manager`: Quản lý kho phụ trách một hoặc nhiều cơ sở được phân công.
4. `business_operations_manager`: Quản lý kinh doanh và vận hành toàn hệ thống.
5. `system_administrator`: Quản trị viên hệ thống (quản lý tài khoản, phân quyền, phân công cơ sở, audit log).

---

## 3. Cơ Chế Bootstrap Tài Khoản Mẫu (Demo Data Bootstrap)

### Vấn đề:
SQL script mẫu khởi tạo 6 tài khoản với trường `password_hash = 'external-auth-demo-only'`, đây là giá trị tượng trưng không thể đăng nhập trực tiếp.

### Giải pháp:
Hệ thống tích hợp `IDemoAccountBootstrapService` chạy tự động khi API khởi động trong môi trường `Development`:
- Kiểm tra các tài khoản có hash `external-auth-demo-only`.
- Tạo chuỗi hash chuẩn PBKDF2 SHA-256 (100,000 iterations, salt 16-byte) và cập nhật vào SQL Server.
- Mật khẩu mặc định lấy từ biến môi trường `DEMO_DEFAULT_PASSWORD` hoặc cấu hình `DemoAccounts:DefaultPassword` (mặc định môi trường dev: `Storage@Demo2026!`).

### Danh sách 6 tài khoản mẫu đăng nhập:

| Email | Vai Trò (Roles) | Mật khẩu Demo | Ghi chú |
| :--- | :--- | :--- | :--- |
| `administrator@example.test` | `system_administrator` | `Storage@Demo2026!` | Admin toàn quyền hệ thống |
| `manager.hcm@example.test` | `facility_manager` | `Storage@Demo2026!` | Quản lý cơ sở Thủ Đức & Cầu Giấy |
| `staff.hcm@example.test` | `facility_staff` | `Storage@Demo2026!` | Nhân viên cơ sở Thủ Đức |
| `operations@example.test` | `business_operations_manager` | `Storage@Demo2026!` | Quản lý vận hành toàn hệ thống |
| `customer.one@example.test` | `storage_customer` | `Storage@Demo2026!` | Khách hàng Nguyễn Minh Anh |
| `customer.two@example.test` | `storage_customer` | `Storage@Demo2026!` | Khách hàng Trần Gia Bình |

---

## 4. Bảo Mật & Phạm Vi Cơ Sở (Facility Scope)

### Quy tắc phân quyền:
- **System Administrator** và **Business Operations Manager** có thẩm quyền trên toàn hệ thống và tất cả các cơ sở.
- **Facility Staff** và **Facility Manager** chỉ có quyền thao tác trên các cơ sở mà nhân viên đó được phân công hợp lệ trong bảng `[core].[staff_facility_assignments]`:
  - `employee_id == user.id`
  - `starts_at <= DateTimeOffset.UtcNow`
  - `ends_at == null || ends_at > DateTimeOffset.UtcNow`
  - `employment_status == 'active'`
- Trigger SQL Server `trg_staff_facility_assignment_no_overlap` ngăn chặn phân công chồng chéo thời gian cho cùng một nhân viên, cơ sở và vai trò.

### Quy ước cho các module sau (Reservations, Payments, Rentals...):
- **Tuyệt đối không lấy `customer_id` hoặc `employee_id` từ body/query request của client.**
- Backend trích xuất danh tính đã xác thực trực tiếp từ token:
  ```csharp
  var userId = long.Parse(User.FindFirst(ClaimTypes.NameIdentifier)!.Value);
  ```
- Backend sử dụng `IFacilityScopeService.HasAccessToFacilityAsync(userId, facilityId, role)` để xác minh quyền thao tác trên từng cơ sở trước khi thực hiện nghiệp vụ.

---

## 5. Hướng Dẫn Chạy & Thử Nghiệm

### Bước 1: Kiểm tra cấu hình kết nối SQL Server
Trong `src/SelfStorageManagementSystem.Presentation/appsettings.json`:
```json
"ConnectionStrings": {
  "DefaultConnection": "Server=localhost;Database=SelfStoragePRN222;Trusted_Connection=True;TrustServerCertificate=True;"
}
```

### Bước 2: Khởi động Web API
Mở terminal tại thư mục gốc của giải pháp (`D:\FPT\PRN222\Project\Final_Project`):
```bash
dotnet run --project src/SelfStorageManagementSystem.Presentation --urls "https://localhost:7031;http://localhost:5132"
```
Khi khởi động lần đầu trong Development, log console sẽ thông báo:
```text
info: [DemoBootstrap] Successfully bootstrapped 6 demo accounts with valid development password hash.
info: Now listening on: https://localhost:7031
```
Swagger UI sẵn sàng tại: `https://localhost:7031/swagger`

### Bước 3: Khởi động ứng dụng WPF Client
Mở một terminal khác (hoặc chạy từ Visual Studio):
```bash
dotnet run --project src/SelfStorageManagementSystem.WpfClient
```

### Bước 4: Thử nghiệm các kịch bản trên giao diện WPF
1. **Đăng nhập Admin:**
   - Trên màn hình `LoginWindow`, click nút **"Admin"** (nút Fast Fill) -> bấm **"Sign In"**.
   - Màn hình `MainWindow` mở ra hiển thị role `system_administrator` và nút **"Admin Management Console"**.
   - Bấm nút để mở màn hình Quản trị viên:
     - Xem DataGrid danh sách tài khoản, tìm kiếm theo tên/email, lọc status và role.
     - Thử chọn 1 tài khoản và đổi status sang `locked`/`disabled` -> Bấm "Update Status".
     - Thử bấm **"Assign / Revoke Roles"** để gán/thu hồi quyền.
     - Thử bấm **"+ Create Staff Account"** để tạo nhân viên mới kèm mã nhân viên và cơ sở ban đầu.
     - Thử bấm **"Assign Facility"** để phân công cơ sở cho nhân viên.
2. **Đăng nhập Staff / Manager:**
   - Đăng xuất (`Sign Out`), click nút **"Staff"** -> bấm **"Sign In"**.
   - `MainWindow` hiển thị bảng **"Your Assigned Facilities (Facility Scope)"** với cơ sở Thủ Đức.
   - Nút quản trị viên biến mất hoàn toàn.
3. **Đăng ký khách hàng mới:**
   - Trên màn hình `LoginWindow`, click link **"Create an account"**.
   - Điền form đăng ký khách hàng -> Bấm **"Register"**.
   - Đăng nhập bằng tài khoản mới vừa tạo.
   - Màn hình hiển thị giao diện cổng khách hàng (`Storage Customer Dashboard`).
4. **Kiểm tra bảo mật API:**
   - Thử gửi request từ tài khoản khách hàng tới API `/api/admin/accounts` -> API trả về `403 Forbidden`.
   - Thử sai mật khẩu -> API trả về `401 Unauthorized` và lưu lịch sử vào `login_history`.

---

## 6. Chạy Kiểm Thử Tự Động (Tests)

Giải pháp bao gồm 22 bài kiểm thử tự động bao quát đầy đủ logic hashing, xác thực, đăng ký, scope và revalidation:
```bash
dotnet test tests/SelfStorageManagementSystem.Tests/SelfStorageManagementSystem.Tests.csproj
```
Kết quả:
```text
Passed!  - Failed: 0, Passed: 22, Skipped: 0 - SelfStorageManagementSystem.Tests.dll (net8.0)
```
