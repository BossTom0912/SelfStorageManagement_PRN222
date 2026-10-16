# Báo Cáo Thực Hiện Task T02: Tách Xác Thực Tài Khoản Khỏi JWT (Đã Fix Codex P1 Review)

- **Task ID**: T02
- **Tên Task**: Tách xác thực tài khoản khỏi JWT (Account Identity & Decoupled Authentication)
- **Owner**: Gemini A (Foundation, Auth & Integration Lead)
- **Đợt**: 1
- **Chờ (Dependencies)**: T01 (**VERIFIED** tại [`AgentReport/WebMigration_T01.md`](file:///D:/FPT/PRN222/Project/Final_Project_Web/AgentReport/WebMigration_T01.md))
- **Status**: **VERIFIED** (Đã giải quyết triệt để lỗi Codex review P1 về phạm vi phân công cơ sở)
- **Contract Version**: v0 (Baseline & Architecture Freeze tại [`docs/WebMigration_Contracts.md`](file:///D:/FPT/PRN222/Project/Final_Project_Web/docs/WebMigration_Contracts.md))
- **Thời điểm hoàn thành**: 2026-10-15
- **Môi trường**: Windows 11, .NET SDK 8.0.425 / 10.0.401, PowerShell 5.1

---

## 1. Mục Tiêu và Phạm Vi Hoàn Thành

1. **Thêm Giao Diện và Dịch Vụ `IAccountIdentityService`**:
   - Khởi tạo giao diện chuẩn `IAccountIdentityService` tại BLL với đầy đủ 3 nghiệp vụ định danh cốt lõi:
     - `AuthenticateAsync(email, password, ipAddress, userAgent, cancellationToken)` (kèm overload tiện ích nhận `LoginRequest`).
     - `RegisterCustomerAsync(request, cancellationToken)`.
     - `GetCurrentUserAsync(userId, cancellationToken)`.
   - Kết quả xác thực trả về `AuthenticatedAccountResponse` mang đầy đủ thông tin định danh (`UserId`, `DisplayName`, `Email`, `PhoneNumber`, `Status`, `UserType`, `EmployeeCode`, `LastLoginAt`) và danh sách quyền (`Roles`).
   - **Tuyệt đối không chứa JWT access token, refresh token hay password hash** trong DTO kết quả.

2. **Tách Biệt Logic Xác Thực Dùng Chung & Tránh Ghi Trùng Lịch Sử Đăng Nhập (Single Login History Audit)**:
   - Toàn bộ logic kiểm tra hợp lệ định danh được tập trung vào `AccountIdentityService`:
     - Chuẩn hóa email (`Trim().ToLowerInvariant()`).
     - Kiểm tra tồn tại và trạng thái tài khoản (`active` vs `locked` / `disabled`).
     - Xác minh mật khẩu thông qua `IPasswordHasher.VerifyPassword`.
     - Cập nhật thời điểm đăng nhập cuối `last_login_at = DateTimeOffset.UtcNow`.
     - Ghi nhận nhật ký đăng nhập vào `login_histories` theo đúng kết quả: `succeeded`, `failed`, `locked`, `blocked`.
   - Cơ chế ủy nhiệm: `AuthService.LoginAsync` gọi `AccountIdentityService.AuthenticateAsync`, sau đó chỉ thực hiện cấp phát JWT qua `IJwtTokenGenerator`. **Không ghi lịch sử lần thứ hai cho cùng một lượt đăng nhập**.

3. **Bảo Toàn Hợp Đồng Công Khai `IAuthService` Cho Web API / JWT Clients**:
   - Giữ nguyên vẹn 100% public contract `IAuthService` (`LoginAsync`, `RegisterCustomerAsync`, `GetCurrentUserAsync`).
   - `AuthService` chuyển hóa thành mô hình **Adapter Pattern**:
     - Tiêm phụ thuộc `IAccountIdentityService` và `IJwtTokenGenerator`.
     - Khi `LoginAsync` thành công, lấy thông tin `AuthenticatedAccountResponse`, chuyển giao cho `IJwtTokenGenerator.GenerateToken(userId, email, roles)` và trả về `LoginResponse` đầy đủ `AccessToken` và `ExpiresAt` như cũ.
     - `RegisterCustomerAsync` và `GetCurrentUserAsync` ủy thác trực tiếp sang `IAccountIdentityService`. Nếu `GetCurrentUserAsync` trả về `null` (người dùng không tồn tại hoặc bị vô hiệu hóa), `AuthService` ném `UnauthorizedException` chuẩn xác tương thích API cũ.

4. **Bản Web Không Resolve `JwtTokenGenerator` & Không Cần Khóa Ký JWT (Zero JWT Secret on Web)**:
   - Trong `ServiceCollectionExtensions.cs` của BLL:
     - `AddBusinessLogicCore()` đăng ký `IAccountIdentityService -> AccountIdentityService`.
     - `AddBusinessLogicJwt()` chỉ đăng ký `IJwtTokenGenerator -> JwtTokenGenerator` và `IAuthService -> AuthService`.
     - `AddSelfStorageWebComposition(configuration)` hợp thành DAL và `AddBusinessLogicCore()`, hoàn toàn **loại trừ** `AddBusinessLogicJwt()`.
   - Host Web khởi động và xác thực tài khoản hoàn toàn độc lập mà không cần bất kỳ cấu hình `Jwt:Key` hay biến môi trường `JWT_SECRET_KEY` nào.

5. **Truy Vấn Snapshot Identity Mới Trong DAL Bằng Projection và AsNoTracking (Đã Fix Codex P1)**:
   - Bổ sung vào `IUserRepository` và `UserRepository`:
     - `GetIdentitySnapshotByIdAsync(id, asOfUtc, cancellationToken)`
     - `GetIdentitySnapshotByEmailAsync(normalizedEmail, asOfUtc, cancellationToken)`
     - `GetByIdWithRolesAndProfilesAsNoTrackingAsync(id, cancellationToken)`
     - `GetByEmailWithRolesAndProfilesAsNoTrackingAsync(normalizedEmail, cancellationToken)`
     - `UpdateLastLoginAtAsync(userId, lastLoginAt, cancellationToken)`
   - Cung cấp mô hình snapshot chuyên biệt `UserIdentitySnapshot` và `StaffAssignmentSnapshot` trong DAL.
   - Toàn bộ truy vấn sử dụng `.AsNoTracking()` và `.Select(...)` chiếu trực tiếp từ SQL Server/DbSet, **không bị phụ thuộc hay bị che khuất bởi các thực thể đã được track trong EF Core ChangeTracker từ các lượt kiểm tra trước**. Mọi thay đổi quyền hạn hoặc phân công cơ sở trong DB đều được phản ánh ngay lập tức.

---

## 2. Chi Tiết Khắc Phục Lỗi Codex Review P1 (Phạm Vi Phân Công Cơ Sở)

### 2.1. Nguyên Nhân Gốc (Root Cause)
1. Trước khi sửa, `UserRepository.GetIdentitySnapshotByIdAsync` và `GetIdentitySnapshotByEmailAsync` chỉ lọc phân công theo thời gian (`starts_at <= now && (ends_at == null || ends_at > now)`), nhưng không kiểm tra:
   - `employee_profile.employment_status == "active"`.
   - Vai trò hiện tại của nhân viên (`facility_manager` vs `facility_staff`).
2. `AccountIdentityService.GetCurrentUserAsync` dùng trực tiếp kết quả `ActiveAssignments` từ snapshot mà không đối chiếu lại các quy tắc nghiệp vụ phạm vi cơ sở (vốn đã được thiết lập chặt chẽ trong `FacilityScopeService`).
3. Dẫn đến rủi ro: Một nhân viên đang nghỉ việc (`leave`) hoặc đã thôi việc (`terminated`), hoặc đã bị thu hồi vai trò Quản lý/Nhân viên, hoặc chỉ là nhân viên thông thường nhưng có phân công vai trò Manager trong DB, vẫn có thể nhận phạm vi cơ sở không hợp lệ. Điều này ảnh hưởng trực tiếp đến Task T06 (CRUD ô kho theo cơ sở).

### 2.2. Giải Pháp và Cách Sửa (Fix Implementation)
1. **Tại DAL (`UserRepository.cs`)**:
   - Áp dụng điều kiện lọc trực tiếp trong LINQ projection `.Select(...)`:
     - `u.employee_profile != null && u.employee_profile.employment_status == "active"`
     - Người dùng phải sở hữu ít nhất một trong hai vai trò: `facility_manager` hoặc `facility_staff`.
     - Phân công phải trong khoảng thời gian hiệu lực (`starts_at <= now && (ends_at == null || ends_at > now)`).
     - **Nếu có `facility_manager`**: Nhận các phân công có `assignment_role == "facility_manager"` hoặc `"facility_staff"`.
     - **Nếu chỉ có `facility_staff`**: Chỉ nhận phân công có `assignment_role == "facility_staff"` (loại bỏ hoàn toàn phân công Manager).
     - **Nếu không có cả 2 vai trò trên, hoặc `employment_status != "active"`**: Trả về danh sách rỗng (`new List<StaffAssignmentSnapshot>()`).
2. **Tại BLL (`AccountIdentityService.cs`)**:
   - Trong `GetCurrentUserAsync`, bổ sung cơ chế kiểm tra phòng vệ tầng sâu (Defense-in-depth):
     - Xác thực `EmploymentStatus == "active"` và kiểm tra tập vai trò của `snapshot`.
     - Lọc `ActiveAssignments` đồng nhất với `FacilityScopeService`, đảm bảo an toàn tuyệt đối ngay cả khi snapshot được mock hoặc nạp từ nguồn ngoài.

---

## 3. Danh Sách Tập Tin Đã Tạo và Chỉnh Sửa

### 3.1. Tập tin tạo mới (New Files)
| Tập tin | Mô tả chức năng | Quyền sở hữu (Ownership) |
| :--- | :--- | :---: |
| `src/SelfStorageManagementSystem.DataAccess/Models/UserIdentitySnapshot.cs` | Mô hình snapshot độc lập mang dữ liệu user, profile, roles và danh sách phân công cơ sở đang hiệu lực (`ActiveAssignments`) | Gemini A |
| `src/SelfStorageManagementSystem.BusinessLogic/DTOs/Responses/Auth/AuthenticatedAccountResponse.cs` | DTO kết quả định danh không chứa token/hash | Gemini A |
| `src/SelfStorageManagementSystem.BusinessLogic/Services/Interfaces/IAccountIdentityService.cs` | Hợp đồng giao diện xác thực tài khoản và truy vấn identity độc lập với JWT | Gemini A |
| `src/SelfStorageManagementSystem.BusinessLogic/Services/Implementations/AccountIdentityService.cs` | Cài đặt nghiệp vụ xác thực chung, kiểm tra status, cập nhật last login, ghi nhận lịch sử và đọc snapshot DB | Gemini A |
| `tests/SelfStorageManagementSystem.Tests/AccountIdentityServiceTests.cs` | 15 kịch bản kiểm thử đơn vị & tích hợp kiểm chứng xác thực đúng/sai, tài khoản khóa/chặn, snapshot tươi mới, không trùng lịch sử và lọc phạm vi phân công theo vai trò/trạng thái | Gemini A |
| `tests/SelfStorageManagementSystem.WebTests/AccountIdentityAndZeroJwtTests.cs` | 6 bài kiểm thử web host fixture chứng minh DI Web resolve thành công identity mà không resolve JWT generator, chạy không cần JWT secret và lọc phân công trên web host | Gemini A |
| `AgentReport/WebMigration_T02.md` | Báo cáo hoàn thành và bàn giao task T02 | Gemini A |

### 3.2. Tập tin chỉnh sửa (Modified Files)
| Tập tin | Nội dung chỉnh sửa | Ranh giới sở hữu (Ownership) |
| :--- | :--- | :---: |
| `src/SelfStorageManagementSystem.DataAccess/Repositories/Interfaces/IUserRepository.cs` | Bổ sung hợp đồng `GetIdentitySnapshotByIdAsync`, `GetIdentitySnapshotByEmailAsync`, `GetByIdWithRolesAndProfilesAsNoTrackingAsync`, `GetByEmailWithRolesAndProfilesAsNoTrackingAsync`, và `UpdateLastLoginAtAsync` | Gemini A |
| `src/SelfStorageManagementSystem.DataAccess/Repositories/Implementations/UserRepository.cs` | Cài đặt 5 phương thức với `.AsNoTracking()` và EF Core projection; lọc `ActiveAssignments` theo `employment_status == "active"` và vai trò `facility_manager` / `facility_staff` | Gemini A |
| `src/SelfStorageManagementSystem.DataAccess/DependencyInjection/ServiceCollectionExtensions.cs` | Đăng ký `IStorageUnitAdminRepository` theo yêu cầu từ Gemini B (T03) | Gemini A (Shared File) |
| `src/SelfStorageManagementSystem.BusinessLogic/Services/Interfaces/IJwtTokenGenerator.cs` | Bổ sung overload `GenerateToken(long userId, string email, IEnumerable<string> roleCodes)` | Gemini A |
| `src/SelfStorageManagementSystem.BusinessLogic/Services/Implementations/JwtTokenGenerator.cs` | Cài đặt overload `GenerateToken` không phụ thuộc thực thể EF | Gemini A |
| `src/SelfStorageManagementSystem.BusinessLogic/Services/Implementations/AuthService.cs` | Refactor thành Adapter tiêm `IAccountIdentityService` và `IJwtTokenGenerator`, bảo toàn contract API | Gemini A |
| `src/SelfStorageManagementSystem.BusinessLogic/DependencyInjection/ServiceCollectionExtensions.cs` | Đăng ký `IAccountIdentityService` trong `AddBusinessLogicCore` | Gemini A (Shared File) |
| `tests/SelfStorageManagementSystem.Tests/AuthServiceTests.cs` | Cập nhật khởi tạo fixture phù hợp kiến trúc adapter mới | Gemini A |
| `AgentReport/WebMigration_TaskBoard.md` | Cập nhật tiến độ T02 thành VERIFIED | Gemini A (Shared File) |

---

## 4. Bằng Chứng Kiểm Tra và Kết Quả Thực Tế (Verification Evidence)

### 4.1. Kiểm Thử Đơn Vị & Snapshot Tươi Mới (`AccountIdentityServiceTests`)
- **Lệnh thực thi**: `dotnet test tests/SelfStorageManagementSystem.Tests/SelfStorageManagementSystem.Tests.csproj --filter "FullyQualifiedName~AccountIdentityServiceTests"`
- **Kết quả**: **15 / 15 test cases PASS 100% (0 Failed, 0 Skipped)**
- **Chi tiết các ca kiểm thử đạt**:
  1. `AuthenticateAsync_WithValidCredentials_ShouldSucceed_UpdateLastLogin_RecordHistory_AndReturnIdentityWithoutToken`: Đăng nhập thành công trả về đầy đủ định danh và vai trò, không có token/hash; cập nhật `last_login_at`; ghi lịch sử `succeeded`.
  2. `AuthenticateAsync_WithNonExistentEmail_ShouldThrowUnauthorizedAndRecordFailedHistory`: Email không tồn tại ném `UnauthorizedException` an toàn, ghi nhận lịch sử `failed` với `user_id = null`.
  3. `AuthenticateAsync_WithWrongPassword_ShouldThrowUnauthorizedAndRecordFailedHistory`: Mật khẩu sai ném lỗi đồng nhất, ghi nhận lịch sử `failed`.
  4. `AuthenticateAsync_WithLockedAccount_ShouldThrowUnauthorizedAndRecordLockedHistory`: Tài khoản ở trạng thái `locked` ném lỗi đồng nhất và ghi nhận lịch sử `locked`.
  5. `AuthenticateAsync_WithDisabledAccount_ShouldThrowUnauthorizedAndRecordBlockedHistory`: Tài khoản ở trạng thái `disabled` ném lỗi đồng nhất và ghi nhận lịch sử `blocked`.
  6. `AuthServiceAdapter_LoginAsync_CallsAccountIdentity_AndDoesNotRecordHistoryTwice`: Khi gọi qua `AuthService.LoginAsync`, `AccountIdentityService` xác thực và ghi nhận lịch sử, `AuthService` cấp JWT và **tuyệt đối không ghi lịch sử lần thứ hai**.
  7. `GetCurrentUserAsync_WithInactiveOrMissingUser_ShouldReturnNull`: Người dùng bị xóa hoặc bị vô hiệu hóa trả về `null` phục vụ Cookie `ValidatePrincipal`.
  8. `GetCurrentUserAsync_WithActiveUser_ShouldReturnCurrentUserResponseWithActiveAssignments`: Người dùng active trả về đầy đủ hồ sơ, mã nhân viên và phân công cơ sở.
  9. `SnapshotQuery_ReflectsFreshDbChanges_WithoutRelyingOnTrackedEntity`: Trực tiếp sửa DB (vai trò Admin thành Manager, thêm phân công cơ sở), lệnh gọi snapshot đọc trực tiếp từ DB và phản ánh chính xác dữ liệu mới nhất mà không bị che khuất bởi ChangeTracker.
  10. `GetCurrentUserAsync_ActiveManager_ReceivesBothManagerAndStaffAssignments`: **(Mới - Codex Fix)** Nhân viên active có role Manager nhận đúng cả phân công Manager và Staff.
  11. `GetCurrentUserAsync_ActiveStaff_DoesNotReceiveManagerAssignments`: **(Mới - Codex Fix)** Nhân viên active chỉ có role Staff không nhận phân công Manager.
  12. `GetCurrentUserAsync_EmploymentStatusLeaveOrTerminated_ReturnsEmptyAssignments("leave")`: **(Mới - Codex Fix)** Nhân viên có trạng thái `leave` nhận `ActiveAssignments` rỗng.
  13. `GetCurrentUserAsync_EmploymentStatusLeaveOrTerminated_ReturnsEmptyAssignments("terminated")`: **(Mới - Codex Fix)** Nhân viên có trạng thái `terminated` nhận `ActiveAssignments` rỗng.
  14. `GetCurrentUserAsync_RevokedStaffOrManagerRole_ReturnsEmptyAssignments`: **(Mới - Codex Fix)** Vai trò Staff/Manager bị thu hồi nhưng phân công còn trong DB vẫn nhận danh sách rỗng.
  15. `SnapshotQuery_EnforcesRoleAndEmploymentStatusRules_DirectlyFromDb`: **(Mới - Codex Fix)** Khẳng định năng lực lọc DB thực tế: Nhân viên A (Manager) nhận 2 cơ sở; Nhân viên B (Staff) nhận 1 cơ sở Staff; Nhân viên C (Leave) nhận 0 cơ sở; Nhân viên D (Terminated) nhận 0 cơ sở; Nhân viên A khi bị thu hồi quyền sang Customer nhận ngay lập tức 0 cơ sở.

### 4.2. Kiểm Thử Web Host & Không Phụ Thuộc Khóa JWT (`AccountIdentityAndZeroJwtTests`)
- **Lệnh thực thi**: `dotnet test tests/SelfStorageManagementSystem.WebTests/SelfStorageManagementSystem.WebTests.csproj --filter "FullyQualifiedName~AccountIdentityAndZeroJwtTests"`
- **Kết quả**: **6 / 6 test cases PASS 100% (0 Failed, 0 Skipped)**
- **Chi tiết các ca kiểm thử đạt**:
  1. `WebComposition_RegistersAccountIdentityService_AndCanBeResolved`: Web host container resolve thành công `IAccountIdentityService`.
  2. `WebComposition_DoesNotRegisterJwtTokenGenerator_ResolvingFails`: Khẳng định `IJwtTokenGenerator` **không** tồn tại trong DI của Web (`GetService` trả về `null`, `GetRequiredService` ném `InvalidOperationException`).
  3. `WebComposition_DoesNotRegisterIAuthService_ResolvingFails`: Khẳng định `IAuthService` **không** tồn tại trong DI của Web.
  4. `WebHost_OperatesAndAuthenticatesWithoutJwtSecret`: Khởi tạo người dùng mẫu, gọi `IAccountIdentityService.AuthenticateAsync` trên Web host đang chạy hoàn toàn không có `Jwt:Key`; xác thực thành công, trả về đúng định danh và ghi nhận lịch sử DB.
  5. `IdentitySnapshot_ReflectsFreshDbRoleChanges_Immediately`: Cập nhật trực tiếp vai trò trong DbContext của Web host; lệnh gọi `GetCurrentUserAsync` đọc snapshot tươi mới và phản ánh ngay lập tức vai trò mới.
  6. `IdentitySnapshot_FiltersFacilityAssignments_ByEmploymentStatusAndRoles_OnWebHost`: **(Mới - Codex Fix)** Chạy toàn diện trên Web host fixture: kiểm chứng qua 4 giai đoạn trạng thái tài khoản (Manager -> Staff -> Leave -> Revoked Roles), kết quả phân công phản ánh chính xác tức thì.

### 4.3. Kiểm Thử Toàn Diện Solution Web (`SelfStorageManagementSystem.Web.sln`)
- **Lệnh thực thi**: `dotnet test SelfStorageManagementSystem.Web.sln`
- **Kết quả**: **54 / 54 test cases PASS 100% (0 Failed, 0 Skipped)**
  - `LandingPageAndBootstrapTests` (T01): 14 / 14 PASS
  - `AccountIdentityAndZeroJwtTests` (T02): 6 / 6 PASS
  - `LayoutAndSharedComponentsTests` (T04): 15 / 15 PASS
  - `StorageUnitAdminRepositoryTests` (T03): 8 / 8 PASS
  - Các bài kiểm thử Web UI & Components mở rộng: 11 / 11 PASS

### 4.4. Kiểm Thử Hồi Quy Solution Cũ (`SelfStorageManagementSystem.sln`)
- **Lệnh thực thi**: `dotnet test SelfStorageManagementSystem.sln --filter "FullyQualifiedName!~SqlServerConcurrency"`
- **Kết quả**: **223 / 223 test cases PASS 100% (0 Failed, 0 Skipped)**
  - Toàn bộ 208 test baseline ban đầu (bao gồm toàn bộ API endpoints của Presentation: login, register, me, catalog, reservation, payment, check-in) + 15 bài test của `AccountIdentityServiceTests` đều đạt 100%.
  - Hoàn toàn không phát sinh hồi quy hay lỗi tương thích ngược.

---

## 5. Bàn Giao Đầu Vào Cho Các Task Tiếp Theo

1. **Bàn giao cho T05 (Cookie Login, Register, Logout, Forbidden - Gemini A)**:
   - Sẵn sàng `IAccountIdentityService` trong DI Web để `AccountController` MVC tiêm và sử dụng.
   - Sẵn sàng `GetCurrentUserAsync` trả về `null` khi tài khoản inactive để `CookieAuthenticationEvents.ValidatePrincipal` từ chối phiên đăng nhập.
   - Khẳng định 100% web host không phụ thuộc vào `IJwtTokenGenerator`.
2. **Bàn giao cho T06 (Service nghiệp vụ quản trị ô kho - Gemini B)**:
   - Đã sửa triệt để snapshot: `CurrentUserResponse.ActiveAssignments` bảo đảm chỉ chứa các phân công hợp lệ theo `employment_status == "active"` và vai trò thực tế (`facility_manager` / `facility_staff`).
   - Sẵn sàng cung cấp dữ liệu chính xác tuyệt đối để T06 giới hạn phạm vi cơ sở khi CRUD ô kho.

---

## 6. Kết Luận Nghiệm Thu (Acceptance Criteria Check)

| Tiêu chí nghiệm thu | Trạng thái | Bằng chứng thực tế |
| :--- | :---: | :--- |
| `IAccountIdentityService` độc lập JWT | **ĐẠT** | Cung cấp `AuthenticateAsync`, `RegisterCustomerAsync`, `GetCurrentUserAsync`. Kết quả không chứa token/hash. |
| Không ghi lịch sử 2 lần khi đăng nhập | **ĐẠT** | Test `AuthServiceAdapter_LoginAsync_CallsAccountIdentity_AndDoesNotRecordHistoryTwice` xác nhận `login_history` chỉ ghi đúng 1 lần. |
| Public contract `IAuthService` giữ nguyên | **ĐẠT** | 223/223 kiểm thử API và controller cũ vượt qua mà không cần sửa contract. |
| Web host không resolve `JwtTokenGenerator` | **ĐẠT** | Test `WebComposition_DoesNotRegisterJwtTokenGenerator_ResolvingFails` xác minh `GetRequiredService<IJwtTokenGenerator>` ném lỗi. |
| Không cần JWT secret trên bản web | **ĐẠT** | Host Web khởi động và chạy kiểm thử không có `Jwt:Key` trong `appsettings.json` hay environment. |
| Lọc phân công cơ sở theo trạng thái lao động | **ĐẠT** | `employment_status` là `leave` hoặc `terminated` trả về `ActiveAssignments` rỗng (đạt trên cả Unit test, DB query và Web host). |
| Lọc phân công cơ sở theo vai trò Quản lý / Nhân viên | **ĐẠT** | Manager nhận cả 2 loại phân công; Staff chỉ nhận phân công Staff; tài khoản mất quyền nhận danh sách rỗng. |
| Snapshot quyền mới phản ánh trực tiếp từ DB | **ĐẠT** | Các bài kiểm thử snapshot với EF Core và Web host đều PASS 100% không bị lưu đệm bởi ChangeTracker. |
| Biên dịch và kiểm thử tự động 100% | **ĐẠT** | 54/54 WebTests PASS, 223/223 Legacy Tests PASS. |
