# Báo Cáo Cập Nhật & Sửa Lỗi Chức Năng 1: Authentication & RBAC

- **Dự án**: Self-Storage Facility Rental and Management System (PRN222)
- **Thời gian cập nhật**: 06/10/2026
- **Branch**: `Account&RoleManagement`
- **Kiến trúc tuân thủ**: WPF Client → ASP.NET Core Web API → BusinessLogic → DataAccess (EF Core Database First) → SQL Server

---

## 1. Tổng Hợp Các Lỗi Review Đã Xác Nhận và Phương Án Khắc Phục

| Mã lỗi | Vấn đề review phát hiện | Trạng thái trước đợt sửa này | Giải pháp & Thay đổi đã hoàn tất |
| :---: | :--- | :--- | :--- |
| **P1** | **Kiểm tra HTTPS chưa bao phủ mọi request** (`ApiClient.cs`) | Chỉ chặn HTTP trong `LoginAsync`. `RegisterCustomerAsync`, `CreateStaffAccountAsync` và các method mang token vẫn gửi tới `BaseUrl` mà không kiểm tra HTTPS. | Tập trung hóa kiểm tra URL tại một điểm dùng chung (`ValidateBaseUrl` & `SendRequestAsync`). Chặn toàn bộ 10 method của `ApiClient` nếu URL không phải tuyệt đối sử dụng HTTPS trước khi phát sinh bất kỳ request mạng nào. |
| **P1** | **Mật khẩu demo vẫn hardcode trong XAML** | `LoginWindow.xaml:38` và `CreateStaffDialog.xaml:15` vẫn còn thuộc tính `Password="Storage@Demo2026!"`. `ACCOUNT_ROLE_MANAGEMENT_GUIDE.md` vẫn mô tả đây là mật khẩu mặc định. | Xóa bỏ hoàn toàn thuộc tính `Password` trong cả hai file XAML (ô PasswordBox hoàn toàn trống khi mở cửa sổ). Cập nhật `docs/ACCOUNT_ROLE_MANAGEMENT_GUIDE.md` bỏ mật khẩu mặc định, bắt buộc cấu hình mật khẩu khi bật demo. |
| **P2** | **README ghi sai roadmap 5 chức năng cốt lõi** | `README.md` đổi chức năng 3–5 thành các nhóm khác; chức năng 5 bị ghi sai thành Maintenance/Tickets. | Đối chiếu và sửa bảng trạng thái trong `README.md` khớp 100% với `docs/FIVE_CORE_FUNCTIONS_SUMMARY.md`. |

---

## 2. Danh Sách Các File Đã Thay Đổi và Lý Do

| STT | File thay đổi | Lý do và Phạm vi can thiệp |
| :---: | :--- | :--- |
| 1 | `src/SelfStorageManagementSystem.WpfClient/Services/ApiClient.cs` | **Tập trung hóa kiểm tra HTTPS**: Bổ sung `ValidateBaseUrl<T>` và `SendRequestAsync<T>` làm dispatch tập trung cho toàn bộ 10 phương thức của client. Chặn ngay lập tức nếu URL không phải HTTPS tuyệt đối mà không thực hiện request mạng. |
| 2 | `src/SelfStorageManagementSystem.WpfClient/Views/LoginWindow.xaml` | **Xóa mật khẩu hardcode**: Xóa thuộc tính `Password="Storage@Demo2026!"` tại PasswordBox `txtPassword`. |
| 3 | `src/SelfStorageManagementSystem.WpfClient/Views/CreateStaffDialog.xaml` | **Xóa mật khẩu hardcode**: Xóa thuộc tính `Password="Storage@Demo2026!"` tại PasswordBox `txtPassword`. |
| 4 | `docs/ACCOUNT_ROLE_MANAGEMENT_GUIDE.md` | **Đính chính tài liệu hướng dẫn**: Loại bỏ mô tả mật khẩu demo mặc định; hướng dẫn cấu hình mật khẩu demo thông qua User Secrets (`DemoAccounts:DefaultPassword`) hoặc `DEMO_DEFAULT_PASSWORD` khi bật cờ `DemoAccounts:Enabled=true`. |
| 5 | `README.md` | **Đính chính roadmap 5 chức năng**: Cập nhật chuẩn xác tên và thứ tự 5 chức năng cốt lõi theo `docs/FIVE_CORE_FUNCTIONS_SUMMARY.md`. Giữ chức năng 1 là "bản triển khai cốt lõi", chức năng 2–5 chưa triển khai. |
| 6 | `src/SelfStorageManagementSystem.BusinessLogic/Common/JwtOptions.cs` | Lớp cấu hình JWT tập trung qua DI (`IOptions<JwtOptions>`), quy định khóa tối thiểu 32 bytes (256 bits). |
| 7 | `src/SelfStorageManagementSystem.BusinessLogic/Services/Implementations/JwtTokenGenerator.cs` | Xóa khóa ký mặc định hardcode; thẩm định khóa bắt buộc và đủ độ dài qua `IOptions<JwtOptions>`. |
| 8 | `src/SelfStorageManagementSystem.Presentation/Program.cs` | Dừng khởi động nếu thiếu hoặc ngắn khóa JWT; đăng ký `JwtOptions` thống nhất; chỉ chạy demo bootstrap khi `Development` và `DemoAccounts:Enabled=true`. |
| 9 | `src/SelfStorageManagementSystem.WpfClient/Views/LoginWindow.xaml.cs` | Xóa hằng số mật khẩu hardcode; các nút demo chỉ điền email; xử lý hiển thị lỗi SSL/TLS kèm lệnh trust certificate. |
| 10 | `src/SelfStorageManagementSystem.BusinessLogic/Services/Implementations/FacilityScopeService.cs` | Quyền cơ sở đòi hỏi cả role hiện hành của tài khoản và assignment còn hiệu lực tương ứng; lọc đồng bộ `GetAccessibleFacilityIdsAsync` và `GetActiveAssignmentsForUserAsync`. |
| 11 | `src/SelfStorageManagementSystem.BusinessLogic/Common/PagedResult.cs` | Bổ sung `HasPreviousPage`, `HasNextPage` và tính đúng `TotalPages`. |
| 12 | `src/SelfStorageManagementSystem.BusinessLogic/Services/Implementations/AdminAccountService.cs` | Gọi constructor `PagedResult` có tham số; giữ nguyên phân trang DB bằng `Skip`/`Take`. |
| 13 | `src/SelfStorageManagementSystem.BusinessLogic/Services/Implementations/DemoAccountBootstrapService.cs` | Xóa mật khẩu demo hardcode; báo lỗi cấu hình nếu thiếu mật khẩu khi bootstrap; không log mật khẩu. |
| 14 | `src/SelfStorageManagementSystem.BusinessLogic/Services/Implementations/AuthService.cs` | Trả cùng thông báo 401 `"Invalid email or password."` cho email không tồn tại, sai mật khẩu, tài khoản locked/disabled; ghi nhận đúng lý do nội bộ vào `login_history`. |
| 15 | `tests/...` (Các file test) | Bao gồm 50 unit và integration tests kiểm chứng JWT, phân quyền cơ sở, phân trang, demo bootstrap và thông báo đăng nhập 401. |

---

## 3. Kết Quả Build và Kiểm Thử

### Build Solution
- **Lệnh**: `dotnet build SelfStorageManagementSystem.sln`
- **Kết quả**: `Build succeeded. 0 Warning(s), 0 Error(s)`.

### Test Suite
- **Lệnh**: `dotnet test SelfStorageManagementSystem.sln`
- **Kết quả**: **Passed: 50, Failed: 0, Skipped: 0, Total: 50**.

---

## 4. Chi Tiết Khắc Phục Các Điểm Review và Bằng Chứng Kiểm Chứng

### 1. Kiểm tra HTTPS bao phủ toàn bộ request trong `ApiClient`
- **Hiện tượng trước khi sửa**: `LoginAsync` có lệnh kiểm tra HTTPS riêng, nhưng `RegisterCustomerAsync`, `CreateStaffAccountAsync`, `GetAccountsAsync` và các method khác đều dùng trực tiếp `BaseUrl`. Khi người dùng nhập URL `http://localhost:5132` ở màn hình đăng nhập rồi chuyển sang đăng ký tài khoản, dữ liệu vẫn bị gửi qua HTTP.
- **Cách khắc phục**:
  - Xây dựng method `ValidateBaseUrl<T>(out Uri? validUri)`:
    - Kiểm tra `BaseUrl` phải là URL tuyệt đối có scheme là `https`.
    - Nếu không thỏa mãn, trả ngay `ApiResponse<T>` với `Success = false` và `Message = InsecureOrInvalidUrlErrorMessage`.
  - Toàn bộ 10 phương thức công khai của `ApiClient` (`LoginAsync`, `RegisterCustomerAsync`, `GetMeAsync`, `GetAccountsAsync`, `CreateStaffAccountAsync`, `UpdateUserStatusAsync`, `ManageUserRolesAsync`, `AssignFacilityAsync`, `TerminateFacilityAssignmentAsync`, `GetFacilitiesAsync`) đều gọi thông qua method điều phối tập trung `SendRequestAsync<T>`.
  - Request hoàn toàn bị chặn trước khi chạm vào network; không phát sinh bất kỳ kết nối HTTP nào.
- **Bằng chứng kiểm chứng**:
  - Đã chạy chương trình kiểm chứng (Console runner nạp trực tiếp `SelfStorageManagementSystem.WpfClient.dll`) thử nghiệm 5 kịch bản:
    1. `LoginAsync` với `http://localhost:5132`: Bị chặn (Success=False, Message="Insecure or invalid API URL...").
    2. `RegisterCustomerAsync` với `http://localhost:5132` (tái hiện đường đi review): Bị chặn (Success=False, Message="Insecure or invalid API URL...").
    3. `CreateStaffAccountAsync` với `http://localhost:5132`: Bị chặn (Success=False, Message="Insecure or invalid API URL...").
    4. `GetAccountsAsync` với `http://localhost:5132`: Bị chặn (Success=False, Message="Insecure or invalid API URL...").
    5. URL tương đối `localhost:7031`: Bị chặn (Success=False, Message="Insecure or invalid API URL...").
    *Kết quả: Toàn bộ 5/5 kịch bản đều bị chặn thành công mà không gọi mạng.*

### 2. Xóa mật khẩu demo hardcode trong XAML và tài liệu
- **Hiện tượng trước khi sửa**:
  - `LoginWindow.xaml:38`: `PasswordBox` chứa `Password="Storage@Demo2026!"`.
  - `CreateStaffDialog.xaml:15`: `PasswordBox` chứa `Password="Storage@Demo2026!"`.
  - `docs/ACCOUNT_ROLE_MANAGEMENT_GUIDE.md`: bảng tài khoản demo vẫn ghi `Storage@Demo2026!` là mật khẩu mặc định.
- **Cách khắc phục**:
  - Xóa bỏ thuộc tính `Password` trong cả hai file XAML. Khi khởi tạo cửa sổ, hai ô `PasswordBox` hoàn toàn rỗng.
  - Các nút Fast Fill demo trong `LoginWindow.xaml.cs` chỉ điền email mẫu, không điền mật khẩu.
  - Cập nhật `docs/ACCOUNT_ROLE_MANAGEMENT_GUIDE.md`: làm rõ cơ chế bootstrap chỉ chạy khi `Development` và `DemoAccounts:Enabled=true`; mật khẩu bắt buộc phải cấu hình riêng qua User Secrets hoặc biến môi trường `DEMO_DEFAULT_PASSWORD`.
  - Quét lại toàn bộ thư mục `src/` và `docs/`, xác nhận không còn bất kỳ chuỗi `Storage@Demo2026!` nào trong mã nguồn và tài liệu.

### 3. Đính chính bảng trạng thái 5 chức năng trong `README.md`
- **Hiện tượng trước khi sửa**: `README.md` ghi sai tên và thứ tự của các chức năng 3, 4, 5 (chức năng 5 bị ghi thành Maintenance/Tickets).
- **Cách khắc phục**:
  - Đồng bộ bảng trạng thái trong `README.md` theo đúng chuẩn `docs/FIVE_CORE_FUNCTIONS_SUMMARY.md`:
    1. **Feature 1: Authentication & RBAC**: `Implemented (Core)`
    2. **Feature 2: Facility & Storage Unit Catalog**: `Not Implemented`
    3. **Feature 3: Reservation & Hold Unit**: `Not Implemented`
    4. **Feature 4: Payment & Rental Agreement**: `Not Implemented`
    5. **Feature 5: Check-in & Digital Handover**: `Not Implemented`
  - Giữ nguyên trạng thái chức năng 1 là "bản triển khai cốt lõi" (không tuyên bố Done hoàn toàn do còn pending scope Refresh Token & Audit Log UI); không tự triển khai chức năng 2–5.

---

## 5. Những Việc Chưa Làm Hoặc Chưa Kiểm Chứng

1. **Thao tác giao diện người dùng trực tiếp trên WPF**:
   - Hai file XAML đã được kiểm tra trực tiếp và xác nhận thuộc tính `Password` đã bị xóa hoàn toàn.
   - Logic chặn URL HTTPS của `ApiClient` đã được xác minh tự động thông qua code runner.
   - Tuy nhiên, thao tác click chuột/gõ phím trực tiếp của người dùng trên cửa sổ WPF đang chạy chưa được thực hiện do hạn chế môi trường dòng lệnh/headless.
2. **Cơ sở dữ liệu SQL Server thật**:
   - Toàn bộ 50 test tự động chạy trên `EF Core InMemory` và Mock Repositories.
   - Chưa chạy thao tác ghi trên database SQL Server thật để bảo vệ an toàn cho dữ liệu.
3. **Phạm vi chức năng 2–5**:
   - Các file unstaged liên quan đến Feature 2 (ví dụ `IFacilityCatalogRepository.cs`, `FacilityCatalogService.cs`...) được giữ nguyên trạng thái của người khác, không bị sửa đổi hay ghi đè.

---

## 6. Thống Kê Git Diff và Trạng Thái

### Git Status
```text
On branch Account&RoleManagement
Your branch is up to date with 'origin/Account&RoleManagement'.

Changes not staged for commit:
	modified:   README.md
	modified:   docs/ACCOUNT_ROLE_MANAGEMENT_GUIDE.md
	modified:   src/SelfStorageManagementSystem.BusinessLogic/Common/PagedResult.cs
	modified:   src/SelfStorageManagementSystem.BusinessLogic/DependencyInjection/ServiceCollectionExtensions.cs
	modified:   src/SelfStorageManagementSystem.BusinessLogic/Services/Implementations/AdminAccountService.cs
	modified:   src/SelfStorageManagementSystem.BusinessLogic/Services/Implementations/AuthService.cs
	modified:   src/SelfStorageManagementSystem.BusinessLogic/Services/Implementations/DemoAccountBootstrapService.cs
	modified:   src/SelfStorageManagementSystem.BusinessLogic/Services/Implementations/FacilityScopeService.cs
	modified:   src/SelfStorageManagementSystem.BusinessLogic/Services/Implementations/JwtTokenGenerator.cs
	modified:   src/SelfStorageManagementSystem.DataAccess/DependencyInjection/ServiceCollectionExtensions.cs
	modified:   src/SelfStorageManagementSystem.Presentation/Program.cs
	modified:   src/SelfStorageManagementSystem.WpfClient/Services/ApiClient.cs
	modified:   src/SelfStorageManagementSystem.WpfClient/Views/CreateStaffDialog.xaml
	modified:   src/SelfStorageManagementSystem.WpfClient/Views/LoginWindow.xaml
	modified:   src/SelfStorageManagementSystem.WpfClient/Views/LoginWindow.xaml.cs
	tests/SelfStorageManagementSystem.Tests/... (các file test)
```

- Lệnh `git diff --check`: Không có lỗi định dạng hay khoảng trắng thừa.
- Cam kết tuân thủ: **Không commit, không push, không tạo migration, không sửa DB schema scaffolded, không lưu trữ secret**.
