# Báo Cáo Thực Hiện Task T08: Tích Hợp Nền Và Kiểm Tra Điểm Bàn Giao

- **Task ID**: T08 (kèm các subtasks T08-F01, T08-F02, T08-F03, T08-F04)
- **Tên Task**: Tích hợp nền và kiểm tra điểm bàn giao (Foundation Integration & Handover Verification)
- **Owner**: Gemini A (Foundation & Integration Lead)
- **Đợt (Wave)**: 3
- **Chờ (Dependencies)**: T05 (**VERIFIED**), T06 (**VERIFIED**), T07 (**VERIFIED**)
- **Trạng thái**: **IMPLEMENTED** (Đã hoàn tất toàn bộ T08-F01, T08-F02, T08-F03, T08-F04; chờ reviewer nghiệm thu)
- **Contract Version**: **v1 (Foundation Integration & Feature Handover Freeze)**
- **Thời điểm thực hiện**: 2026-10-16 13:20:00 +07:00
- **Môi trường**: Windows 11, .NET SDK 8.0.425 / 10.0.401, PowerShell 5.1
- **Solution Web**: `SelfStorageManagementSystem.Web.sln`
- **Solution Hồi quy**: `SelfStorageManagementSystem.sln` (tại `Final_Project_Web` và `Final_Project`)

---

## 1. Mục Tiêu và Phạm Vi Hoàn Thành

1. **Tích hợp ba nhánh công việc Đợt 2 (Wave 2)**:
   - **T05 (Gemini A)**: Xác thực Cookie 5 phút độc lập, không phụ thuộc JWT secret, cơ chế `ValidatePrincipal` kiểm tra trạng thái tài khoản và tự động thu hồi phiên của nhân sự không còn hoạt động (`employment_status != "active"`).
   - **T06 (Gemini B)**: Dịch vụ nghiệp vụ quản trị ô kho `IStorageUnitAdminService`, khoảng thời gian hiệu lực nửa mở `[valid_from, valid_to)`, tính ngày địa phương qua `TimeProvider` + múi giờ cơ sở, fallback biểu giá an toàn `0m` / `null`.
   - **T07 (Gemini C)**: Danh mục cơ sở kho và sơ đồ mặt bằng SVG (`/Catalog`, `/Catalog/Details`), phân biệt sức chứa giữ chỗ (`capacity hold` theo BR-RSV-03) với trạng thái ô vật lý (`physical_status`), bảo vệ chống Stored XSS trong SVG và DOM script.

2. **Kiểm tra và xác thực các ràng buộc kiến trúc 3 tầng (Layering & Decoupling Rules)**:
   - `SelfStorageManagementSystem.Web` CHỈ reference trực tiếp `SelfStorageManagementSystem.BusinessLogic`. Tuyệt đối không reference `DataAccess`, `Presentation` (API) hay `WpfClient`.
   - Toàn bộ PageModel, Controller và SignalR Hub không inject `DbContext` hay `IRepository<T>`, chỉ tiêu thụ các Service Interfaces do BLL cung cấp.
   - BLL đóng vai trò Composition Root cho Web thông qua phương thức mở rộng `AddSelfStorageWebComposition(configuration)`.

3. **Khởi động an toàn không cần khóa bí mật JWT (Zero JWT Secret on Web Host)**:
   - Ứng dụng Web khởi động hoàn toàn độc lập khi không có khóa cấu hình `Jwt:Key` và không có biến môi trường `JWT_SECRET_KEY`.
   - Service cấp phát token `IJwtTokenGenerator` không bị rò rỉ hay đăng ký vào container DI của Web host.

4. **Kiểm tra vòng đời dịch vụ và chống đăng ký trùng lặp (DI Lifetime Safety)**:
   - Toàn bộ 13 dịch vụ nghiệp vụ cốt lõi được cấu hình đúng vòng đời: `Scoped` cho các service nghiệp vụ phụ thuộc DbContext/Repositories, và `Singleton` cho các tiện ích phi trạng thái (`TimeProvider.System`, `IPasswordHasher`).
   - Tuyệt đối không có hiện tượng Captive Dependency (Singleton giữ tham chiếu Scoped) hay đăng ký lặp.

5. **Xác nhận chu trình đầy đủ (Pipeline Verification trên EF Core InMemory)**:
   - Chạy thông suốt chuỗi dữ liệu trong test host: `Cookie` $\longrightarrow$ `OnValidatePrincipal` $\longrightarrow$ `IAccountIdentityService` $\longrightarrow$ `IUserRepository` $\longrightarrow$ `SelfStorageDbContext` (EF Core InMemory).
   - Kiểm chứng việc nạp và cập nhật động các claim vai trò (`ClaimTypes.Role`) và phạm vi cơ sở phân công (`facility_scope`) khi cơ sở dữ liệu thay đổi.
   - Phân biệt rành mạch: Pipeline được kiểm chứng trên EF Core InMemory; kiểm thử tích hợp trên cơ sở dữ liệu vật lý SQL Server và kiểm thử đồng thời (concurrency) thuộc trách nhiệm của task **T17**.

6. **Tách biệt hoàn toàn Test Host giữa Web và API cũ**:
   - `SelfStorageManagementSystem.WebTests` sử dụng `SelfStorageManagementSystem.Web.Program` thông qua `WebApplicationFactory<Program>`, hoàn toàn không phụ thuộc và không bị xung đột kiểu dữ liệu với `SelfStorageManagementSystem.Presentation.Program`.

7. **Nâng cấp và chốt Hợp đồng Kỹ thuật v1 (`docs/WebMigration_Contracts.md`)**:
   - Cập nhật đầy đủ chữ ký phương thức (signatures), quy hoạch tuyến đường (routes), 4 chính sách phân quyền (`CustomerOnly`, `StaffOnly`, `ManagerOnly`, `AdminOnly`), và chi tiết các trường sự kiện realtime SignalR (`/storageUnitHub`).

---

## 2. Chi Tiết Thực Hiện Các Task Con (Subtasks T08-F01 đến T08-F04)

### 2.1. Task T08-F01: Bảo Vệ SignalR Endpoint `/storageUnitHub` Bằng Cookie Authentication *(Đã ACCEPTED)*
- **Vấn đề**: `StorageUnitHub` kế thừa `Hub` nhưng thiếu thuộc tính `[Authorize]`. Anonymous client có thể gửi `POST /storageUnitHub/negotiate?negotiateVersion=1` và nhận về HTTP 200 kèm `connectionToken`.
- **Khắc phục**:
  - Thêm `using Microsoft.AspNetCore.Authorization;` và gắn `[Authorize]` trên lớp `StorageUnitHub` tại `src/SelfStorageManagementSystem.Web/Hubs/StorageUnitHub.cs`.
  - Giữ Hub ở mức skeleton, không triển khai CRUD nghiệp vụ sớm của T13.
  - Bổ sung 3 unit/integration tests trong `FoundationIntegrationSmokeTests.cs`:
    - `StorageUnitHub_HasAuthorizeAttribute`: Dùng reflection xác nhận `[Authorize]` tồn tại trên Hub.
    - `StorageUnitHub_AnonymousNegotiate_IsRejected`: Yêu cầu negotiate ẩn danh bị từ chối (HTTP 401 hoặc Redirect Login).
    - `StorageUnitHub_AuthenticatedCookie_NegotiateSucceeds`: Gửi kèm Cookie xác thực thật, negotiate thành công trả về HTTP 200 kèm `connectionToken`.
- **Trạng thái**: Đã được Reviewer kiểm tra và **ACCEPTED**.

---

### 2.2. Task T08-F02: Sửa Bằng Chứng Pipeline InMemory *(IMPLEMENTED)*
- **Vấn đề**:
  - Test `FullPipeline_CookieToServiceToRepositoryToDatabase_WithFacilityScopesAndRenewal` sử dụng fixture `CookieAuthWebTestFixture`. Fixture này ghi đè cấu hình DbContext bằng `options.UseInMemoryDatabase(...)`.
  - Trong tài liệu trước đây (Report, Contract v1, TaskBoard), luồng này bị mô tả nhầm là đã kiểm thử tới "SQL Server". Thực tế luồng chỉ đi tới EF Core InMemory provider, chưa chứng minh SQL translation hay transaction trên SQL Server thật.
- **Khắc phục**:
  1. Đổi tên phương thức kiểm thử tại `tests/SelfStorageManagementSystem.WebTests/FoundationIntegrationSmokeTests.cs`:
     - Tên cũ: `FullPipeline_CookieToServiceToRepositoryToDatabase_WithFacilityScopesAndRenewal`
     - Tên mới: `FullPipeline_CookieToServiceToRepositoryToEfInMemory_WithFacilityScopesAndRenewal`
  2. Cập nhật XML comment và region tương ứng thành: `#region 4. Full Pipeline: Cookie -> Service -> Repository -> EF Core InMemory With Facility Scopes`.
  3. Cập nhật tài liệu (`WebMigration_T08.md`, `WebMigration_Contracts.md`, `WebMigration_TaskBoard.md`):
     - Ghi nhận chính xác chu trình: `Cookie -> ValidatePrincipal -> Service -> Repository -> DbContext (EF Core InMemory)`.
     - Phân định rõ ràng: Kiểm thử SQL Server thật và Concurrency được giao cho task **T17** và chưa được chứng minh bằng test này.
     - Báo cáo số lượng test SQL Server Concurrency bị SKIP (8 tests) một cách trung thực; tuyệt đối không tính SKIP thành PASS.

---

### 2.3. Task T08-F03: Đưa SignalR Hub Vào Architecture Test *(IMPLEMENTED)*
- **Vấn đề**:
  - Phương thức `Architecture_PageModelsAndControllers_DoNotInjectDbContextOrRepositories` trước đây chỉ lọc:
    ```csharp
    var uiTypes = webAssembly.GetTypes()
        .Where(t => typeof(PageModel).IsAssignableFrom(t) || typeof(ControllerBase).IsAssignableFrom(t))
        .ToList();
    ```
  - `StorageUnitHub` kế thừa `Hub` nên bị bỏ lọt khỏi danh sách `uiTypes`. Nếu ai đó inject `DbContext` hoặc `Repository` vào Hub, test kiểm toán kiến trúc vẫn PASS sai thực tế.
- **Khắc phục**:
  1. Bổ sung các namespace cần thiết vào `FoundationIntegrationSmokeTests.cs`:
     ```csharp
     using Microsoft.AspNetCore.SignalR;
     using SelfStorageManagementSystem.Web.Hubs;
     ```
  2. Đổi tên phương thức test:
     - Tên cũ: `Architecture_PageModelsAndControllers_DoNotInjectDbContextOrRepositories`
     - Tên mới: `Architecture_PageModelsControllersAndHubs_DoNotInjectDbContextOrRepositories`
  3. Mở rộng bộ lọc `uiTypes` bao gồm cả các lớp kế thừa `Hub`:
     ```csharp
     var uiTypes = webAssembly.GetTypes()
         .Where(t =>
             typeof(PageModel).IsAssignableFrom(t) ||
             typeof(ControllerBase).IsAssignableFrom(t) ||
             typeof(Hub).IsAssignableFrom(t))
         .ToList();
     ```
  4. Bổ sung assertion bắt buộc Hub thực tế phải nằm trong danh sách kiểm tra:
     ```csharp
     Assert.Contains(uiTypes, type => type == typeof(StorageUnitHub));
     ```
  5. Toàn bộ các kiểm tra constructors, fields, và properties đối với `SelfStorageManagementSystem.DataAccess`, `DbContext`, và `Repository` nay được áp dụng tự động lên cả `StorageUnitHub`.

---

### 2.4. Task T08-F04: Chốt Tài Liệu Và Bằng Chứng Đồng Bộ *(IMPLEMENTED)*
- **Vấn đề**:
  - Trạng thái giữa `WebMigration_TaskBoard.md` (ghi IMPLEMENTED, T09-T11 WAITING) và `docs/WebMigration_Contracts.md` (ghi VERIFIED, T09-T11 READY) trước đây không đồng nhất.
  - Các tài liệu cần được cập nhật ngày `2026-10-16`, đồng bộ sang thư mục `Final_Project`, và kiểm tra mã băm SHA-256.
- **Khắc phục**:
  1. Đồng bộ trạng thái:
     - `WebMigration_Contracts.md`: Đặt trạng thái tích hợp nền thành **IMPLEMENTED (Chờ Reviewer nghiệm thu)**; Bảng 10.2 đặt T09, T10, T11 ở trạng thái **WAITING** (Chờ nghiệm thu T08).
     - `WebMigration_TaskBoard.md`: Cập nhật ngày `2026-10-16`, ghi nhận đầy đủ hoàn tất F01-F04, T08 là **IMPLEMENTED**, T09-T11 là **WAITING**.
  2. Đồng bộ 3 tập tin tài liệu sang `D:\FPT\PRN222\Project\Final_Project`:
     - `docs/WebMigration_Contracts.md`
     - `AgentReport/WebMigration_TaskBoard.md`
     - `AgentReport/WebMigration_T08.md`
  3. Tính toán và xác nhận mã băm SHA-256 đối xứng giữa hai thư mục khớp 100%.

---

## 3. Các Tập Tin Sửa Đổi & Bổ Sung (Shared Integration Diff)

1. [`src/SelfStorageManagementSystem.Web/Hubs/StorageUnitHub.cs`](file:///D:/FPT/PRN222/Project/Final_Project_Web/src/SelfStorageManagementSystem.Web/Hubs/StorageUnitHub.cs):
   - Thêm `using Microsoft.AspNetCore.Authorization;` và gắn `[Authorize]` trên lớp `StorageUnitHub`.
2. [`src/SelfStorageManagementSystem.Web/Views/Shared/_Layout.cshtml`](file:///D:/FPT/PRN222/Project/Final_Project_Web/src/SelfStorageManagementSystem.Web/Views/Shared/_Layout.cshtml):
   - Bổ sung liên kết điều hướng trực tiếp đến trang thông tin tài khoản cá nhân: `<li><a class="dropdown-item" href="/Account/Profile">Hồ sơ cá nhân</a></li>` trong menu dropdown tài khoản người dùng đã đăng nhập.
3. [`tests/SelfStorageManagementSystem.WebTests/LandingPageAndBootstrapTests.cs`](file:///D:/FPT/PRN222/Project/Final_Project_Web/tests/SelfStorageManagementSystem.WebTests/LandingPageAndBootstrapTests.cs):
   - Cập nhật test `SignalRHub_NegotiationEndpoint_ReturnsOk` truyền header xác thực kiểm thử `X-Test-Role: storage_customer`.
4. [`tests/SelfStorageManagementSystem.WebTests/FoundationIntegrationSmokeTests.cs`](file:///D:/FPT/PRN222/Project/Final_Project_Web/tests/SelfStorageManagementSystem.WebTests/FoundationIntegrationSmokeTests.cs):
   - Bổ sung usings `Microsoft.AspNetCore.SignalR` và `SelfStorageManagementSystem.Web.Hubs`.
   - Đổi tên test `FullPipeline_CookieToServiceToRepositoryToDatabase_WithFacilityScopesAndRenewal` $\longrightarrow$ `FullPipeline_CookieToServiceToRepositoryToEfInMemory_WithFacilityScopesAndRenewal`.
   - Đổi tên test `Architecture_PageModelsAndControllers_DoNotInjectDbContextOrRepositories` $\longrightarrow$ `Architecture_PageModelsControllersAndHubs_DoNotInjectDbContextOrRepositories`.
   - Thêm `typeof(Hub).IsAssignableFrom(t)` vào bộ lọc UI types và assert `StorageUnitHub` được kiểm tra.
   - Giữ nguyên 3 tests bảo mật SignalR Hub (`StorageUnitHub_HasAuthorizeAttribute`, `StorageUnitHub_AnonymousNegotiate_IsRejected`, `StorageUnitHub_AuthenticatedCookie_NegotiateSucceeds`).
5. [`docs/WebMigration_Contracts.md`](file:///D:/FPT/PRN222/Project/Final_Project_Web/docs/WebMigration_Contracts.md):
   - Chốt phiên bản **v1**.
   - Cập nhật trạng thái hợp đồng thành **IMPLEMENTED (Chờ Reviewer nghiệm thu)**.
   - Mục 10.1 làm rõ pipeline được kiểm chứng trên EF Core InMemory, SQL Server thuộc phạm vi T17; cập nhật số lượng test 189/189 PASS.
   - Mục 10.2 chuyển trạng thái bàn giao T09, T10, T11 về **WAITING**.
6. [`AgentReport/WebMigration_TaskBoard.md`](file:///D:/FPT/PRN222/Project/Final_Project_Web/AgentReport/WebMigration_TaskBoard.md):
   - Cập nhật thời điểm `2026-10-16`, cập nhật ghi chú T08 và giữ T09-T11 ở trạng thái **WAITING**.

---

## 4. Bằng Chứng Kiểm Thử Tự Động (Automated Test Evidence)

### 4.1. Kiểm thử Solution Web (`SelfStorageManagementSystem.Web.sln`)
- **Thư mục thực thi**: `D:\FPT\PRN222\Project\Final_Project_Web`
- **Lệnh thực thi**: `dotnet test .\SelfStorageManagementSystem.Web.sln --nologo`
- **Thời điểm**: 2026-10-16 13:17:10 +07:00
- **Kết quả**:
  ```text
  Test run for D:\FPT\PRN222\Project\Final_Project_Web\tests\SelfStorageManagementSystem.WebTests\bin\Debug\net8.0\SelfStorageManagementSystem.WebTests.dll (.NETCoreApp,Version=v8.0)
  A total of 1 test files matched the specified pattern.

  Passed!  - Failed:     0, Passed:   189, Skipped:     0, Total:   189, Duration: 2 s - SelfStorageManagementSystem.WebTests.dll (net8.0)
  ```
- **Phân bổ chi tiết 189 bài kiểm thử đã PASS**:
  | Bộ Kiểm Thử (Test Suite) | Số Lượng Tests | Trạng Thái | Chức Năng Xác Minh |
  | :--- | :---: | :---: | :--- |
  | `FoundationIntegrationSmokeTests` | **22** | **PASS** | HTTP smoke, Zero JWT startup, DI lifetimes, Cookie->EF Core InMemory pipeline, Policy matrix, Layering rules (gồm Hubs), SignalR [Authorize] security |
  | `AccountCookieAuthTests` | **34** | **PASS** | Cookie 5m Lax, Antiforgery, ValidatePrincipal, Inactive revocation, Local redirect |
  | `AccountIdentityAndZeroJwtTests` | **6** | **PASS** | Zero JWT startup, Identity snapshot AsNoTracking, Fresh role changes |
  | `CatalogRazorPagesTests` | **16** | **PASS** | /Catalog & /Catalog/Details, SVG bounding box, XSS protection, Capacity hold vs Physical status |
  | `LandingPageAndBootstrapTests` | **14** | **PASS** | Landing page, local assets Bootstrap/jQuery/SignalR, centralized error handling |
  | `LayoutAndSharedComponentsTests` | **21** | **PASS** | Shared layout, role menus 4 nhóm, formatters, alerts |
  | `StorageUnitAdminRepositoryTests` | **18** | **PASS** | CRUD repository, semaphore release, staged mutations, distinct DbContexts |
  | `StorageUnitAdminServiceTests` | **58** | **PASS** | BLL admin service, half-open interval, facility timezone via TimeProvider, zero fallback |
  | **Tổng cộng** | **189** | **PASS 100%** | Sạch sẽ, không có lỗi biên dịch hay cảnh báo phụ thuộc |

### 4.2. Kiểm thử Hồi quy trên Solution Cũ (`SelfStorageManagementSystem.sln` tại `Final_Project_Web`)
- **Thư mục thực thi**: `D:\FPT\PRN222\Project\Final_Project_Web`
- **Lệnh thực thi**: `dotnet test .\SelfStorageManagementSystem.sln --nologo`
- **Thời điểm**: 2026-10-16 13:17:23 +07:00
- **Kết quả**:
  ```text
  Test run for D:\FPT\PRN222\Project\Final_Project_Web\tests\SelfStorageManagementSystem.Tests\bin\Debug\net8.0-windows\SelfStorageManagementSystem.Tests.dll (.NETCoreApp,Version=v8.0)
  A total of 1 test files matched the specified pattern.

  Passed!  - Failed:     0, Passed:   225, Skipped:     8, Total:   233, Duration: 3 s - SelfStorageManagementSystem.Tests.dll (net8.0)
  ```
  *(8 test SQL Server Concurrency được SKIP theo đúng thiết kế vì không chạy trên môi trường DB SQL Server vật lý khi test tự động. Không có bất kỳ test fail nào)*.

### 4.3. Kiểm thử trên Thư mục Gốc Đối chứng (`Final_Project`)
- **Thư mục thực thi**: `D:\FPT\PRN222\Project\Final_Project`
- **Lệnh thực thi**: `dotnet test .\SelfStorageManagementSystem.sln --nologo`
- **Thời điểm**: 2026-10-16 13:17:32 +07:00
- **Kết quả**:
  ```text
  Test run for D:\FPT\PRN222\Project\Final_Project\tests\SelfStorageManagementSystem.Tests\bin\Debug\net8.0-windows\SelfStorageManagementSystem.Tests.dll (.NETCoreApp,Version=v8.0)
  A total of 1 test files matched the specified pattern.

  Passed!  - Failed:     0, Passed:   208, Skipped:     8, Total:   216, Duration: 2 s - SelfStorageManagementSystem.Tests.dll (net8.0)
  ```
  *(Bảo tồn nguyên vẹn 100% tính tương thích của solution gốc)*.

---

## 5. Bảng Đối Chiếu Ma Trận Nghiệm Thu T08

| Tiêu Chí Nghiệm Thu | Kết Quả Thực Tế | Trạng Thái |
| :--- | :--- | :---: |
| **Bảo vệ SignalR Endpoint (T08-F01)** | `StorageUnitHub` có `[Authorize]`. Anonymous negotiate bị từ chối (401/Redirect). Authenticated negotiate với Cookie thành công trả về 200 OK + `connectionToken`. Reviewer đã ACCEPTED. | **ĐẠT** |
| **Đính chính Pipeline InMemory (T08-F02)** | Đổi tên test thành `FullPipeline_CookieToServiceToRepositoryToEfInMemory_WithFacilityScopesAndRenewal`. Tài liệu Contract v1, Report và TaskBoard phân định rõ kiểm thử trên EF Core InMemory, tích hợp SQL Server vật lý và concurrency thuộc task T17. Báo cáo trung thực 8 test SQL SKIP. | **ĐẠT** |
| **Kiểm toán Kiến trúc gồm Hub (T08-F03)** | Đổi tên test thành `Architecture_PageModelsControllersAndHubs_DoNotInjectDbContextOrRepositories`. Đã thêm `typeof(Hub).IsAssignableFrom(t)` vào bộ lọc UI types và assert `StorageUnitHub` được kiểm tra. Toàn bộ constructors, fields, properties của PageModel, Controller và Hub không inject `DbContext` hay `Repository`. | **ĐẠT** |
| **Không lỗi DI / Lifetime** | Đã kiểm tra qua bài test `DependencyInjection_ServiceLifetimes_AreCorrect_AndNoDuplicates`. Không có đăng ký trùng lặp, 13 core domain services được Scoped đúng cách, Singletons không capture Scoped. | **ĐẠT** |
| **Zero JWT Secret on Web** | Host Web khởi động và giải quyết toàn bộ dịch vụ nghiệp vụ khi hoàn toàn không có `Jwt:Key` hay `JWT_SECRET_KEY`. `IJwtTokenGenerator` không được nạp vào DI. | **ĐẠT** |
| **Tách biệt Test Host** | `WebTests` tham chiếu trực tiếp đến `SelfStorageManagementSystem.Web.Program`. Assembly `Presentation` không bị kéo vào `WebTests`. Không có xung đột kiểu `Program` trong test host. | **ĐẠT** |
| **Ràng buộc phân tầng** | `MvcWebApp` chỉ reference `BusinessLogic`. Kiểm thử tự động duyệt qua toàn bộ constructors, fields, properties của tất cả PageModels, Controllers và Hubs khẳng định không có bất kỳ phụ thuộc nào vào `DataAccess` hay `DbContext`. | **ĐẠT** |
| **Local Assets 100%** | Bootstrap 5.3.3, jQuery 3.7.1, jQuery Validation 1.20.0, SignalR 8.0.7 được phục vụ cục bộ từ `wwwroot/lib/`, không sử dụng CDN công cộng bên ngoài. | **ĐẠT** |
| **Chốt Contract v1** | `docs/WebMigration_Contracts.md` đã cập nhật v1 đầy đủ signatures, routes, policies, event fields và handover sign-off. | **ĐẠT** |
| **Đồng bộ tài liệu & SHA-256 (T08-F04)** | Trạng thái đồng nhất: T08 = IMPLEMENTED, T09-T11 = WAITING trên cả TaskBoard và Contract v1. Ba file tài liệu được đồng bộ 100% sang `Final_Project` với SHA-256 đối xứng trùng khớp. | **ĐẠT** |

---

## 6. Trạng Thái Bàn Giao (Handover Status)

Theo đúng quy định và kỷ luật nghiệm thu:
- **Trạng thái Task T08**: **IMPLEMENTED** (Không tự ý đánh dấu VERIFIED, chờ Reviewer nghiệm thu độc lập).
- **Trạng thái Đợt 4 (T09, T10, T11)**: **WAITING** (Giữ nguyên trạng thái chờ, không mở sớm khi T08 chưa được Reviewer phê duyệt).
- Toàn bộ bằng chứng, mã nguồn và tài liệu đồng bộ đã sẵn sàng để Reviewer tiến hành nghiệm thu.
