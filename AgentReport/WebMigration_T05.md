# Báo Cáo Thực Hiện Task T05: Cookie Login, Register, Logout và Forbidden

- **Task ID**: T05
- **Tên Task**: Cookie Login, Register, Logout và Forbidden
- **Owner**: Gemini A (Architecture, Account Identity & Core Auth Lead)
- **Đợt**: 2
- **Chờ (Dependencies)**: T02, T04 (Cả hai đã **VERIFIED**)
- **Trạng thái**: **VERIFIED** (Đã giải quyết trọn vẹn Finding Codex P1 về kiểm soát `employment_status`)
- **Contract Version**: v1
- **Thời điểm hoàn thành**: 2026-10-16 10:00:00 +07:00
- **Môi trường**: Windows 11, .NET SDK 8.0.425 / 10.0.401, PowerShell 5.1

---

## 1. Mục Tiêu và Phạm Vi Hoàn Thành

1. **Bám sát cấu trúc MVC Account của Giảng viên (`TeacherCode/1.txt`, `TeacherCode/AccountController.cs`)**:
   - Sử dụng kiến trúc MVC Controller `AccountController` với đầy đủ các Action: `Login` (GET/POST), `Register` (GET/POST), `Logout` (GET/POST), `Forbidden` (GET), `Profile` (GET), `SecuredData` (GET), `AdminCheck` (GET).
   - Giữ nguyên thuộc tính `UserName` và `Password` trong `Models/LoginRequest.cs` theo chuẩn bài giảng.
   - Tại tầng Controller, map `UserName` sang Email để gọi nghiệp vụ `IAccountIdentityService.AuthenticateAsync(email, password)`.

2. **Cơ chế xác thực Cookie độc lập (Zero JWT trên Web Host)**:
   - Sử dụng chuẩn `ClaimsIdentity(CookieAuthenticationDefaults.AuthenticationScheme)` và `HttpContext.SignInAsync(...)`.
   - Nạp đầy đủ claims tiêu chuẩn:
     - `ClaimTypes.NameIdentifier`: ID người dùng (`user.id`).
     - `ClaimTypes.Name`: Họ và tên hiển thị (`DisplayName` từ `customer_profile` hoặc `employee_profile`).
     - `ClaimTypes.Email`: Địa chỉ email người dùng.
     - `ClaimTypes.Role`: Danh sách mã vai trò thực tế (`storage_customer`, `facility_staff`, `facility_manager`, `business_operations_manager`, `system_administrator`).
     - Claims tùy biến hỗ trợ phân quyền chi tiết: `user_type`, `employee_code`, `facility_scopes` (chuỗi danh sách các `facility_id` được phân công cho nhân viên/quản lý).
   - Tuyệt đối không sinh, lưu trữ hay yêu cầu token JWT trên ứng dụng Web ASP.NET Core MVC/Razor Pages.

3. **Cấu hình Cookie an toàn & Thời hạn 5 phút**:
   - `Cookie.Name = "SelfStoragePRN222.Web.Auth"`: Tách biệt tên cookie ứng dụng web, không xung đột hệ thống khác.
   - `Cookie.HttpOnly = true`: Chống tấn công đánh cắp phiên qua XSS phía client.
   - `Cookie.SameSite = SameSiteMode.Lax`: Đảm bảo an toàn chống CSRF trong điều kiện điều hướng thông thường.
   - `Cookie.SecurePolicy = CookieSecurePolicy.SameAsRequest`: Bảo mật HTTPS tự động khi chạy môi trường production/staging, đồng thời linh hoạt khi chạy kiểm thử HTTP nội bộ.
   - `ExpireTimeSpan = TimeSpan.FromMinutes(5)`: Đúng hạn mức 5 phút theo quy định bài giảng.
   - `SlidingExpiration = true`: Tự động gia hạn khi người dùng thao tác liên tục.
   - `LoginPath = "/Account/Login"`, `AccessDeniedPath = "/Account/Forbidden"`.

4. **Kiểm tra tính hợp lệ phiên động qua `ValidatePrincipal` (Live Identity Refresh & Auto-Revocation)**:
   - Đăng ký sự kiện `OnValidatePrincipal = CookieAuthEventsHandler.ValidatePrincipalAsync`.
   - Tại mỗi request, đọc snapshot định danh mới nhất từ `IAccountIdentityService.GetIdentitySnapshotByIdAsync(...)`.
   - **Tự động từ chối (RejectPrincipal) và SignOutAsync** nếu:
     - Tài khoản người dùng không còn tồn tại trong DB.
     - Trạng thái tài khoản người dùng bị khóa hoặc ngừng hoạt động (`users.status != "active"`).
     - Đối với người dùng có hồ sơ nhân sự (`employee_profile`), trạng thái nhân sự không hoạt động (`employment_status != "active"`, ví dụ: `leave`, `terminated`, `suspended`).
   - **Tự động làm mới Claims (Dynamic Refresh)**:
     - Nếu quyền hạn (`Roles`) hoặc danh sách cơ sở phân công (`ActiveAssignments`) thay đổi trong DB, sự kiện tự động cập nhật lại danh sách Claims trong Principal và gọi `context.ShouldRenew = true` để ghi đè Cookie mới mà người dùng không bị gián đoạn hay phải đăng nhập lại.

5. **Khắc phục toàn diện 5 lỗ hổng bảo mật đã cảnh báo trong kế hoạch di trú**:
   - **Lỗ hổng 1 - Thiếu `await` tại `SignOutAsync`**: Toàn bộ các vị trí gọi `SignOutAsync` đều sử dụng `await HttpContext.SignOutAsync(...)`.
   - **Lỗ hổng 2 - Khối `catch (Exception) { }` rỗng**: Đã loại bỏ hoàn toàn. Controller xử lý biệt lập từng ngoại lệ nghiệp vụ: `UnauthorizedException`, `BadRequestException`, `ConflictException`, đồng thời ghi log chi tiết và bổ sung lỗi thân thiện vào `ModelState`.
   - **Lỗ hổng 3 - Dùng Cookie `Role` phụ bằng Plaintext**: Loại bỏ hoàn toàn hành vi `Response.Cookies.Append("Role", ...)`. Vai trò và quyền truy cập duy nhất chỉ được giải mã và kiểm chứng thông qua Cookie xác thực bảo mật có chữ ký HMAC.
   - **Lỗ hổng 4 - Lỗ hổng CSRF Đăng xuất qua GET**: GET `/Account/Logout` tuyệt đối **không** thay đổi trạng thái đăng nhập; chỉ có POST `/Account/Logout` được bảo vệ bởi Antiforgery Token mới thực thi `SignOutAsync`.
   - **Lỗ hổng 5 - Trả về mã chuyển hướng 302 thay vì 401/403 cho AJAX / SignalR**:
     - Nếu request là AJAX (`X-Requested-With: XMLHttpRequest`), hoặc yêu cầu API JSON (`Accept: application/json`), hoặc yêu cầu SignalR negotiate (`/storageUnitHub`), hệ thống lập tức trả về mã HTTP `401 Unauthorized` hoặc `403 Forbidden` nguyên bản.
     - Nếu là trình duyệt Web thông thường, hệ thống giữ nguyên hành vi chuyển hướng HTTP 302 tới `/Account/Login` hoặc `/Account/Forbidden`.

6. **Chống tấn công Chuyển hướng hở (Open Redirect Protection)**:
   - Kiểm tra chặt chẽ `Url.IsLocalUrl(returnUrl)` trước khi chuyển hướng sau khi đăng nhập.
   - Nếu `returnUrl` là URL độc hại bên ngoài, hệ thống lập tức bỏ qua và đưa người dùng về trang chủ an toàn `/`.

7. **Đăng ký khách hàng mới (Customer Registration)**:
   - Cung cấp giao diện `Account/Register` và Action POST xử lý đầy đủ thông tin: Họ tên, Email, Mật khẩu, Số điện thoại, CCCD, Ngày sinh, Địa chỉ, Người liên hệ khẩn cấp.
   - Kiểm tra ràng buộc duy nhất (Email, Phone, CCCD) qua BLL, tự động gắn Role `storage_customer` và tạo hồ sơ `customer_profile`.

---

## 2. Chi Tiết Sửa Lỗi Finding Codex P1: Kiểm Soát Trạng Thái Nhân Sự (`employment_status`)

### 2.1. Nguyên Nhân Gốc Rễ (Root Cause)
1. **Lỗ hổng đăng nhập**: Trong `AccountIdentityService.AuthenticateAsync`, hệ thống chỉ kiểm tra `users.status == "active"`. Nhân viên có `users.status == "active"` nhưng `employee_profile.employment_status` là `"leave"` hoặc `"terminated"` vẫn xác thực thành công và được cấp Cookie phiên.
2. **Lỗ hổng thu hồi quyền hạn**: Trong `AccountIdentityService.GetCurrentUserAsync`, khi `employment_status != "active"`, mã nguồn trước đây chỉ làm rỗng `ActiveAssignments = new List<FacilityAssignmentDto>()`, nhưng vẫn trả về `CurrentUserResponse` kèm theo đầy đủ danh sách vai trò (`Roles`) như `system_administrator`, `business_operations_manager`, `facility_staff`, `facility_manager`.
3. **Lỗ hổng kiểm tra phiên**: Trong `CookieAuthExtensions.cs`, sự kiện `OnValidatePrincipal` chỉ kiểm tra `currentUser.Status != "active"`, dẫn đến việc nhân viên có `employment_status` là `"terminated"` vẫn tiếp tục giữ phiên Cookie hợp lệ. Các vai trò cấp cao như Operations Manager hoặc System Administrator (vốn không phụ thuộc vào phân công cơ sở cụ thể) tiếp tục truy cập trái phép vào các tài nguyên quản trị hệ thống.

### 2.2. Các Tệp và Dòng Đã Sửa Đổi
1. **`src/SelfStorageManagementSystem.BusinessLogic/DTOs/Responses/Auth/CurrentUserResponse.cs`**:
   - Thêm thuộc tính `public string? EmploymentStatus { get; set; }` để thể hiện trạng thái nhân sự của người dùng hiện hành.
2. **`src/SelfStorageManagementSystem.DataAccess/Models/UserIdentitySnapshot.cs`**:
   - Thêm thuộc tính `public bool HasEmployeeProfile => EmploymentStatus != null || !string.IsNullOrWhiteSpace(EmployeeCode) || !string.IsNullOrWhiteSpace(EmployeeFullName);` để phân biệt chính xác người dùng có hồ sơ nhân sự với khách hàng thông thường.
3. **`src/SelfStorageManagementSystem.BusinessLogic/Services/Implementations/AccountIdentityService.cs`**:
   - Dòng 85 & dòng 132-137 (`AuthenticateAsync`): Khi phát hiện `hasEmployeeProfile && !string.Equals(employmentStatus, "active", StringComparison.OrdinalIgnoreCase)`, lập tức ghi lịch sử `LoginHistoryResultConstants.Blocked` và ném `UnauthorizedException("Invalid email or password.")`. Chặn đăng nhập hoàn toàn đối với nhân viên có trạng thái khác `active`.
   - Dòng 293-298 & dòng 348-353 (`GetCurrentUserAsync`): Kiểm tra nếu `HasEmployeeProfile` và `employment_status != "active"`, trả về ngay `null` (tương đương với tài khoản không hợp lệ), tước bỏ toàn bộ vai trò và quyền hạn; gán trường `EmploymentStatus` vào `CurrentUserResponse`.
4. **`src/SelfStorageManagementSystem.Web/Extensions/CookieAuthExtensions.cs`**:
   - Dòng 59-67 (`OnValidatePrincipal`): Bổ sung kiểm tra `isEmploymentInactive = currentUser?.EmploymentStatus != null && !string.Equals(currentUser.EmploymentStatus, EmploymentStatusConstants.Active, StringComparison.OrdinalIgnoreCase)`. Nếu `currentUser == null || currentUser.Status != "active" || isEmploymentInactive`, lập tức gọi `context.RejectPrincipal()` và `await context.HttpContext.SignOutAsync(CookieScheme)`. Không có bất kỳ ngoại lệ nào cho System Administrator hay Operations Manager nếu hồ sơ nhân sự đã bị terminated.
   - Khách hàng không có hồ sơ nhân sự (`currentUser.EmploymentStatus == null`) tiếp tục được phép duy trì phiên bình thường.
5. **`tests/SelfStorageManagementSystem.WebTests/Fixtures/CookieAuthWebTestFixture.cs`**:
   - Cập nhật `SeedUserAsync` cho phép truyền tham số `string employmentStatus = "active"`.
6. **`tests/SelfStorageManagementSystem.WebTests/AccountIdentityAndZeroJwtTests.cs`**:
   - Cập nhật bước 3 trong `IdentitySnapshot_ReflectsFreshDbRoleChanges_Immediately`: Khi `employment_status = "leave"`, xác nhận `GetCurrentUserAsync` trả về `null`.
7. **`tests/SelfStorageManagementSystem.Tests/AccountIdentityServiceTests.cs`**:
   - Cập nhật `GetCurrentUserAsync_EmploymentStatusLeaveOrTerminated_ReturnsNull`: Kiểm tra trả về `null`.
   - Thêm test `AuthenticateAsync_WithInactiveEmploymentStatus_ShouldThrowUnauthorizedAndRecordBlockedHistory`.
8. **`tests/SelfStorageManagementSystem.WebTests/AccountCookieAuthTests.cs`**:
   - Bổ sung 5 bài test tích hợp bắt buộc.

---

## 3. Bằng Chứng Kiểm Thử Tự Động (Test Verification)

### 3.1. Các Bài Kiểm Thử Bắt Buộc Về `employment_status` & Kết Quả `AccountCookieAuthTests`
Bộ kiểm thử tích hợp [`AccountCookieAuthTests.cs`](file:///D:/FPT/PRN222/Project/Final_Project_Web/tests/SelfStorageManagementSystem.WebTests/AccountCookieAuthTests.cs) đã bổ sung và kiểm chứng thành công 5 trường hợp bắt buộc (tổng cộng 34 bài test PASS 100%):

```text
Command: dotnet test .\tests\SelfStorageManagementSystem.WebTests\SelfStorageManagementSystem.WebTests.csproj --filter "FullyQualifiedName~AccountCookieAuthTests"
Test run for D:\FPT\PRN222\Project\Final_Project_Web\tests\SelfStorageManagementSystem.WebTests\bin\Debug\net8.0\SelfStorageManagementSystem.WebTests.dll (.NETCoreApp,Version=v8.0)
A total of 1 test files matched the specified pattern.

Passed!  - Failed:     0, Passed:    34, Skipped:     0, Total:    34, Duration: 2 s - SelfStorageManagementSystem.WebTests.dll (net8.0)
```

**Chi tiết 5 trường hợp kiểm tra bắt buộc về kiểm soát `employment_status`:**
1. **`Login_EmployeeEmploymentInactive_IsRejected`** (2 test cases: `leave`, `terminated`):
   - Nhân viên có tài khoản `users.status == active` nhưng `employee_profile.employment_status` là `leave` hoặc `terminated` gửi POST `/Account/Login`.
   - **Kết quả**: Bị từ chối đăng nhập (HTTP 200 trở lại View Login), không phát hành cookie xác thực hợp lệ, hiển thị thông báo an toàn `"Tên đăng nhập hoặc mật khẩu không chính xác"`.
2. **`ValidatePrincipal_EmploymentBecomesTerminated_RejectsPrincipalAndSignsOut`**:
   - Nhân viên cơ sở đăng nhập thành công và sở hữu Cookie hợp lệ. Sau đó trong DB, quản trị viên cập nhật `employee_profile.employment_status = "terminated"`.
   - Gửi request kế tiếp tới `/Account/Profile` với Cookie cũ.
   - **Kết quả**: `OnValidatePrincipal` phát hiện trạng thái terminated, lập tức gọi `RejectPrincipal()` và `SignOutAsync()`, chuyển hướng 302 về `/Account/Login`, phát hành Set-Cookie xóa phiên đăng nhập.
3. **`ValidatePrincipal_ActiveCustomerWithoutEmployeeProfile_RemainsAuthenticated`**:
   - Khách hàng thông thường (`storage_customer`) không có `employee_profile`.
   - Đăng nhập và gửi request tới `/Account/Profile`.
   - **Kết quả**: Xác thực thành công (HTTP 200 OK), không bị chặn nhầm bởi logic kiểm tra nhân sự.
4. **`ValidatePrincipal_TerminatedOperationsManager_LosesRoleAccess`**:
   - Business Operations Manager đăng nhập thành công và truy cập được `/Account/SecuredData`.
   - Cập nhật `employee_profile.employment_status = "terminated"` trong DB.
   - Gửi request kế tiếp tới `/Account/SecuredData` với Cookie cũ.
   - **Kết quả**: Principal bị từ chối, mất toàn bộ quyền truy cập vai trò, bị chuyển hướng 302 về `/Account/Login`.
5. **`ValidatePrincipal_TerminatedSystemAdministrator_LosesRoleAccess`**:
   - System Administrator đăng nhập thành công và truy cập được `/Account/AdminCheck` (`[Authorize(Roles = "system_administrator")]`).
   - Cập nhật `employee_profile.employment_status = "terminated"` trong DB.
   - Gửi request kế tiếp tới `/Account/AdminCheck` với Cookie cũ.
   - **Kết quả**: Không có ngoại lệ cho System Administrator. Principal bị thu hồi ngay lập tức, bị chuyển hướng 302 về `/Account/Login`, Cookie bị xóa.

---

### 3.2. Kết Quả Kiểm Thử Solution Web Tích Hợp (`SelfStorageManagementSystem.Web.sln`)
```text
Command: dotnet test .\SelfStorageManagementSystem.Web.sln
Test run for D:\FPT\PRN222\Project\Final_Project_Web\tests\SelfStorageManagementSystem.WebTests\bin\Debug\net8.0\SelfStorageManagementSystem.WebTests.dll (.NETCoreApp,Version=v8.0)
A total of 1 test files matched the specified pattern.

Passed!  - Failed:     0, Passed:   159, Skipped:     0, Total:   159, Duration: 2 s - SelfStorageManagementSystem.WebTests.dll (net8.0)
```
- **Kết quả hiện tại**: Toàn bộ **159/159** bài test của solution Web (`SelfStorageManagementSystem.WebTests`) đạt **PASS 100% (0 failed, 0 skipped)**.
- *Ghi chú snapshot lịch sử*: Tại thời điểm ban đầu khi hoàn thành sửa riêng lẻ Task T05 (trước khi tích hợp các bài test bổ sung của Catalog và Storage Unit Admin), số lượng test Web ghi nhận là **149/149 passed**. Sau khi tích hợp toàn bộ các task song song cùng đợt, solution Web hiện tại đạt chuẩn đầy đủ **159/159 passed, 0 failed, 0 skipped**.

---

### 3.3. Kết Quả Kiểm Thử Hồi Quy Toàn Hệ Thống (`SelfStorageManagementSystem.sln`)
```text
Command: dotnet test D:\FPT\PRN222\Project\Final_Project\SelfStorageManagementSystem.sln
Test run for D:\FPT\PRN222\Project\Final_Project\tests\SelfStorageManagementSystem.Tests\bin\Debug\net8.0-windows\SelfStorageManagementSystem.Tests.dll (.NETCoreApp,Version=v8.0)
A total of 1 test files matched the specified pattern.

Passed!  - Failed:     0, Passed:   208, Skipped:     8, Total:   216, Duration: 2 s - SelfStorageManagementSystem.Tests.dll (net8.0)
```
- **Passed**: 208
- **Failed**: 0
- **Skipped**: 8 (Gồm 8 bài test SQL Server concurrency đặc thù được cấu hình tự động skip khi chạy môi trường in-memory / không kết nối SQL Server vật lý).
- **Total**: 216 bài test hồi quy hệ thống đã xác minh sạch sẽ.
- **Xác nhận**: Đã loại bỏ toàn bộ các số liệu không có output kiểm thử thực tế tương ứng.

---

## 4. Đối Chiếu Tiêu Chí Nghiệm Thu Sau Khi Sửa Finding Codex P1

| Tiêu Chí Đặt Ra | Trạng Thái | Bằng Chứng / Ghi Chú |
| :--- | :---: | :--- |
| Giữ tên `UserName` & `Password` trong `LoginRequest` | **ĐẠT** | Bám sát TeacherCode, map sang Email BLL ở Controller. |
| Dùng `ClaimsIdentity`, `SignInAsync`, `SignOutAsync` | **ĐẠT** | Thực hiện chuẩn mực ASP.NET Core Cookie Authentication. |
| Claim gồm NameIdentifier, Name, Email, real role codes | **ĐẠT** | Nạp đủ 5 roles thật và facility scope. |
| Cookie HttpOnly, SameSite=Lax, 5 phút, sliding expiration | **ĐẠT** | `ExpireTimeSpan = TimeSpan.FromMinutes(5)`, `SlidingExpiration = true`. |
| `ValidatePrincipal` từ chối user disabled, cập nhật role/scope | **ĐẠT** | Test tự động xác nhận hủy cookie khi disabled và cập nhật claim khi role thay đổi. |
| **Kiểm soát `employment_status` khi đăng nhập** | **ĐẠT** | Nhân sự có `employment_status != "active"` bị chặn ở `AuthenticateAsync`, ghi nhận `blocked`. |
| **Kiểm soát `employment_status` qua `ValidatePrincipal`** | **ĐẠT** | `OnValidatePrincipal` gọi `RejectPrincipal()` và `await SignOutAsync()` ngay ở request kế tiếp. |
| **Không chặn nhầm customer không có employee profile** | **ĐẠT** | Khách hàng active không có employee profile tiếp tục duy trì phiên bình thường. |
| **Không miễn trừ cho System Admin / Operations Manager** | **ĐẠT** | System Admin hoặc Ops Manager bị terminated đều lập tức bị tước quyền hạn và đăng xuất. |
| Chống Open Redirect qua `Url.IsLocalUrl` | **ĐẠT** | Test xác nhận chặn URL ngoài và chuyển hướng target hợp lệ. |
| Antiforgery token trên POST Login, Register, Logout | **ĐẠT** | Test xác nhận 400 Bad Request nếu thiếu/sai token. |
| GET Logout không đổi trạng thái xác thực | **ĐẠT** | Test xác nhận người dùng vẫn giữ nguyên phiên khi truy cập GET `/Account/Logout`. |
| Không dùng cookie `Role` phụ bằng plaintext | **ĐẠT** | Quyền hạn hoàn toàn lấy từ ClaimsPrincipal của Cookie mã hóa. |
| AJAX / SignalR nhận 401/403 thay vì 302 HTML | **ĐẠT** | Test xác nhận 401/403 cho XMLHttpRequest, JSON API và `/storageUnitHub`. |
| Zero JWT trên Web Host | **ĐẠT** | Kiểm thử chứng minh không chứa thư viện hoặc token JWT trên Host Web. |
| Không có `catch` rỗng và `SignOutAsync` thiếu `await` | **ĐẠT** | Toàn bộ ngoại lệ được log và bắt theo type; 100% `SignOutAsync` được `await`. |

---

## 5. Kết Luận

Finding Codex P1 đối với Task **T05** đã được sửa chữa triệt để, có bài test xác minh cho từng trường hợp, biên dịch sạch sẽ và vượt qua 100% các bộ kiểm thử tự động. Task **T05** được xác nhận đạt trạng thái **VERIFIED**.
