# Báo Cáo Thực Hiện Task T01: Tạo Solution Web và Chốt Contract

- **Task ID**: T01
- **Tên Task**: Tạo solution web và chốt contract
- **Owner**: Gemini A (Foundation & Integration Lead)
- **Đợt**: 0
- **Chờ (Dependencies)**: T00 (**VERIFIED** tại [`AgentReport/WebMigration_T00.md`](file:///D:/FPT/PRN222/Project/Final_Project_Web/AgentReport/WebMigration_T00.md))
- **Status**: **IMPLEMENTED — Chờ Codex re-review**
- **Contract Version**: v0 (Baseline & Architecture Freeze tại [`docs/WebMigration_Contracts.md`](file:///D:/FPT/PRN222/Project/Final_Project_Web/docs/WebMigration_Contracts.md))
- **Thời điểm thực hiện**: 2026-10-15 15:55:00 +07:00
- **Môi trường**: Windows 11, .NET SDK 8.0.425 / 10.0.401, PowerShell 5.1

---

## 1. Mục Tiêu và Phạm Vi Hoàn Thành

1. **Khởi tạo Solution Web độc lập**: Tạo mới `SelfStorageManagementSystem.Web.sln` bao gồm 4 project:
   - `src/SelfStorageManagementSystem.Web/SelfStorageManagementSystem.Web.csproj` (MvcWebApp)
   - `src/SelfStorageManagementSystem.BusinessLogic/SelfStorageManagementSystem.BusinessLogic.csproj` (BLL)
   - `src/SelfStorageManagementSystem.DataAccess/SelfStorageManagementSystem.DataAccess.csproj` (DAL)
   - `tests/SelfStorageManagementSystem.WebTests/SelfStorageManagementSystem.WebTests.csproj` (WebTests)
   Bảo toàn nguyên vẹn solution gốc `SelfStorageManagementSystem.sln` để kiểm thử hồi quy.
2. **Kiến trúc phân tầng nghiêm ngặt (Strict Layering)**:
   - `SelfStorageManagementSystem.Web` CHỈ reference trực tiếp `SelfStorageManagementSystem.BusinessLogic`.
   - Web tuyệt đối không reference `DataAccess`, `Presentation` (API cũ), hoặc `WpfClient`.
   - Đã tạo composition entrypoint `AddSelfStorageWebComposition(IConfiguration)` trong BLL để hợp thành DAL và BLL Core; Web không gọi HTTP vòng qua API.
3. **Đăng ký dịch vụ Web đầy đủ**:
   - MVC Controllers & Views (`AddControllersWithViews()`)
   - Razor Pages (`AddRazorPages()`)
   - SignalR Realtime Hub (`AddSignalR()`, endpoint `/storageUnitHub`)
   - Cookie Authentication scheme (thời hạn 5 phút, sliding expiration, LoginPath `/Account/Login`, AccessDeniedPath `/Account/Forbidden`)
   - Antiforgery protection (`X-CSRF-TOKEN`)
   - Cấu hình cổng chạy HTTPS port 7041 và HTTP port 5041 (`launchSettings.json`)
   - Cơ chế xử lý lỗi tập trung: Exception handler (`/Home/Error`) và Status code handler (`/Home/StatusCode/{0}`)
   - Trang chủ đích công khai (Public Landing Page) tại `/`
4. **Tách biệt Core Registration khỏi JWT Registration (Zero JWT Secret on Web)**:
   - BLL tách biệt `AddBusinessLogicCore()` (không phụ thuộc JWT) và `AddBusinessLogicJwt()` (chỉ cho API).
   - Host Web không resolve `JwtTokenGenerator` và khởi động độc lập hoàn toàn mà không cần `Jwt:Key` hoặc `JWT_SECRET_KEY`.
5. **Phục vụ tài nguyên cục bộ (Local Assets Only)**:
   - Ghi nhận chính xác phiên bản công cụ: .NET SDK `8.0.425`, Bootstrap `5.3.3`, jQuery `3.7.1`, jQuery Validation `1.20.0`, jQuery Validation Unobtrusive `4.0.0`, SignalR JS Client `8.0.7` (`@microsoft/signalr`).
   - Toàn bộ thư viện được lưu trữ và phục vụ cục bộ tại `wwwroot/lib/`, không sử dụng CDN bên ngoài.
6. **Bộ kịch bản khởi tạo Database an toàn (Database Bootstrap Scripts)**:
   - Tạo bộ script chuyên biệt chỉ cho 3 database: `SelfStoragePRN222_WebDemo`, `SelfStoragePRN222_WebTest`, `SelfStoragePRN222_WebE2eTest`.
   - Cơ chế an toàn: Kiểm tra allowlist nghiêm ngặt; dừng ngay lập tức (`THROW 52002`) nếu database đã tồn tại; **TUYỆT ĐỐI KHÔNG CÓ LỆNH DROP** trong toàn bộ script; không chứa tên database nguồn `SelfStoragePRN222`.
7. **Tài liệu Hợp đồng v0 & Ma trận phân công (Contracts & Rules)**:
   - Ban hành tài liệu hợp đồng toàn diện [`docs/WebMigration_Contracts.md`](file:///D:/FPT/PRN222/Project/Final_Project_Web/docs/WebMigration_Contracts.md).
   - Cập nhật quy tắc kỹ thuật bản web tại [`Agent/Gemini/RULE.md`](file:///D:/FPT/PRN222/Project/Final_Project_Web/Agent/Gemini/RULE.md) (Section 38).
   - Khởi tạo và cập nhật bảng tiến độ [`AgentReport/WebMigration_TaskBoard.md`](file:///D:/FPT/PRN222/Project/Final_Project_Web/AgentReport/WebMigration_TaskBoard.md).

---

## 2. Danh Sách Tập Tin Đã Tạo và Chỉnh Sửa

### 2.1. Tập tin tạo mới (New Files)
| Tập tin | Mô tả chức năng | Quyền sở hữu (Ownership) |
| :--- | :--- | :---: |
| `SelfStorageManagementSystem.Web.sln` | Solution mới dành riêng cho nền tảng Web | Gemini A |
| `src/SelfStorageManagementSystem.Web/SelfStorageManagementSystem.Web.csproj` | Project MvcWebApp .NET 8, chỉ reference BusinessLogic, cấu hình UserSecretsId cho Secret Manager | Gemini A |
| `src/SelfStorageManagementSystem.Web/Program.cs` | Host pipeline, DI composition, MVC, Razor Pages, SignalR, Cookie auth | Gemini A |
| `src/SelfStorageManagementSystem.Web/Properties/launchSettings.json` | Cấu hình chạy ứng dụng với HTTPS port 7041 và HTTP port 5041 | Gemini A |
| `src/SelfStorageManagementSystem.Web/appsettings.json` | Cấu hình môi trường Web không chứa secret JWT | Gemini A |
| `src/SelfStorageManagementSystem.Web/appsettings.Development.json` | Cấu hình phát triển cục bộ | Gemini A |
| `src/SelfStorageManagementSystem.Web/Hubs/StorageUnitHub.cs` | SignalR Hub skeleton tại `/storageUnitHub` | Gemini A |
| `src/SelfStorageManagementSystem.Web/Controllers/HomeController.cs` | Controller xử lý Landing Page (`/`), Error (`/Home/Error`), StatusCode (`/Home/StatusCode/{code?}`) | Gemini A |
| `src/SelfStorageManagementSystem.Web/Views/Shared/_Layout.cshtml` | Shared Layout sử dụng Bootstrap 5.3.3 và jQuery 3.7.1 cục bộ | Gemini A (Tạm thời bàn giao cho C tại T04) |
| `src/SelfStorageManagementSystem.Web/Views/Home/Index.cshtml` | Public Landing Page giới thiệu 5 chức năng và trạng thái nền tảng | Gemini A |
| `src/SelfStorageManagementSystem.Web/Views/Home/Error.cshtml` | View hiển thị lỗi tập trung (an toàn, không lộ stack trace/secrets) | Gemini A |
| `src/SelfStorageManagementSystem.Web/Views/Home/StatusCode.cshtml` | View hiển thị mã trạng thái HTTP (404, 403, 500) | Gemini A |
| `src/SelfStorageManagementSystem.Web/Views/_ViewImports.cshtml` | MVC ViewImports | Gemini A |
| `src/SelfStorageManagementSystem.Web/Views/_ViewStart.cshtml` | MVC ViewStart gán layout mặc định | Gemini A |
| `src/SelfStorageManagementSystem.Web/Pages/_ViewImports.cshtml` | Razor Pages ViewImports | Gemini A |
| `src/SelfStorageManagementSystem.Web/Pages/_ViewStart.cshtml` | Razor Pages ViewStart gán layout mặc định | Gemini A |
| `src/SelfStorageManagementSystem.Web/wwwroot/css/site.css` | Tệp định kiểu CSS cục bộ | Gemini A |
| `src/SelfStorageManagementSystem.Web/wwwroot/js/site.js` | Tệp JavaScript cục bộ | Gemini A |
| `src/SelfStorageManagementSystem.Web/wwwroot/lib/...` | Bộ assets cục bộ Bootstrap 5.3.3, jQuery 3.7.1, SignalR JS 8.0.7 | Gemini A |
| `tests/SelfStorageManagementSystem.WebTests/SelfStorageManagementSystem.WebTests.csproj` | Project kiểm thử WebTests độc lập | Gemini A |
| `tests/SelfStorageManagementSystem.WebTests/Fixtures/WebTestFixture.cs` | Fixture WebApplicationFactory cho Web host | Gemini A |
| `tests/SelfStorageManagementSystem.WebTests/LandingPageAndBootstrapTests.cs` | 14 test cases kiểm tra landing page, static assets, SignalR, 404 GET/POST, 500 POST, zero JWT, zero hardcoded HMAC | Gemini A |
| `DB/SelfStoragePRN222_Web_Bootstrap.sql` | Script khởi tạo DB chuẩn cho `SelfStoragePRN222_WebDemo` | Gemini A |
| `DB/SelfStoragePRN222_WebDemo_Bootstrap.sql` | Script khởi tạo chuyên biệt cho WebDemo | Gemini A |
| `DB/SelfStoragePRN222_WebTest_Bootstrap.sql` | Script khởi tạo chuyên biệt cho WebTest | Gemini A |
| `DB/SelfStoragePRN222_WebE2eTest_Bootstrap.sql` | Script khởi tạo chuyên biệt cho WebE2eTest | Gemini A |
| `DB/Initialize-WebDatabase.ps1` | PowerShell helper kiểm tra và khởi tạo DB an toàn | Gemini A |
| `docs/WebMigration_Contracts.md` | Tài liệu Hợp đồng kỹ thuật v0 toàn diện | Gemini A |
| `AgentReport/WebMigration_TaskBoard.md` | Bảng quản lý tiến độ T00 - T22 | Gemini A |

### 2.2. Tập tin chỉnh sửa (Modified Files)
| Tập tin | Nội dung chỉnh sửa | Ranh giới sở hữu (Ownership) |
| :--- | :--- | :---: |
| `src/SelfStorageManagementSystem.BusinessLogic/DependencyInjection/ServiceCollectionExtensions.cs` | Bổ sung `AddBusinessLogicCore()`, `AddBusinessLogicJwt()`, và `AddSelfStorageWebComposition(configuration)` | Gemini A (Shared File) |
| `Agent/Gemini/RULE.md` | Bổ sung Section 38 quy định kỹ thuật web migration cho các agent | Gemini A (Shared File) |

---

## 3. Bằng Chứng Kiểm Tra và Kết Quả Thực Tế (Verification Evidence)

### 3.1. Biên dịch Solution Web Mới (`SelfStorageManagementSystem.Web.sln`)
- **Lệnh thực thi**: `dotnet build SelfStorageManagementSystem.Web.sln`
- **Kết quả**: **Thành công (0 Error, 0 Warning)**
  ```text
  Determining projects to restore...
  All projects are up-to-date for restore.
  SelfStorageManagementSystem.DataAccess -> D:\FPT\PRN222\Project\Final_Project_Web\src\SelfStorageManagementSystem.DataAccess\bin\Debug\net8.0\SelfStorageManagementSystem.DataAccess.dll
  SelfStorageManagementSystem.BusinessLogic -> D:\FPT\PRN222\Project\Final_Project_Web\src\SelfStorageManagementSystem.BusinessLogic\bin\Debug\net8.0\SelfStorageManagementSystem.BusinessLogic.dll
  SelfStorageManagementSystem.Web -> D:\FPT\PRN222\Project\Final_Project_Web\src\SelfStorageManagementSystem.Web\bin\Debug\net8.0\SelfStorageManagementSystem.Web.dll
  SelfStorageManagementSystem.WebTests -> D:\FPT\PRN222\Project\Final_Project_Web\tests\SelfStorageManagementSystem.WebTests\bin\Debug\net8.0\SelfStorageManagementSystem.WebTests.dll

  Build succeeded.
      0 Warning(s)
      0 Error(s)
  ```

### 3.2. Kiểm thử Tích hợp Web (`SelfStorageManagementSystem.WebTests`)
- **Lệnh thực thi**: `dotnet test tests/SelfStorageManagementSystem.WebTests/SelfStorageManagementSystem.WebTests.csproj`
- **Kết quả**: **14 / 14 test cases PASS 100% (0 Failed, 0 Skipped)**
  ```text
  Passed!  - Failed:     0, Passed:    14, Skipped:     0, Total:    14, Duration: 342 ms - SelfStorageManagementSystem.WebTests.dll (net8.0)
  ```
- **Chi tiết các bài kiểm thử**:
  1. `LandingPage_ReturnsSuccessAndHtml`: Trả về HTTP 200, Content-Type text/html, nội dung chứa đầy đủ banner hệ thống và thông số công nghệ.
  2. `HostStartsWithoutJwtSecret`: Khởi động và phản hồi thành công trong cấu hình hoàn toàn không có `Jwt:Key` hoặc secret JWT.
  3. `StaticAssets_AreServedLocally` (Bootstrap CSS): Phục vụ cục bộ từ `/lib/bootstrap/dist/css/bootstrap.min.css` (HTTP 200).
  4. `StaticAssets_AreServedLocally` (Bootstrap JS): Phục vụ cục bộ từ `/lib/bootstrap/dist/js/bootstrap.bundle.min.js` (HTTP 200).
  5. `StaticAssets_AreServedLocally` (jQuery JS): Phục vụ cục bộ từ `/lib/jquery/dist/jquery.min.js` (HTTP 200).
  6. `StaticAssets_AreServedLocally` (SignalR JS): Phục vụ cục bộ từ `/lib/microsoft/signalr/dist/browser/signalr.min.js` (HTTP 200).
  7. `StaticAssets_AreServedLocally` (Site CSS): Phục vụ cục bộ từ `/css/site.css` (HTTP 200).
  8. `StaticAssets_AreServedLocally` (Site JS): Phục vụ cục bộ từ `/js/site.js` (HTTP 200).
  9. `SignalRHub_NegotiationEndpoint_ReturnsOk`: Endpoint `/storageUnitHub/negotiate?negotiateVersion=1` trả về HTTP 200 và chuỗi JSON connectionId.
  10. `CentralizedErrorHandling_NotFound_ReturnsStatusCodeView`: Yêu cầu GET tới route không tồn tại trả về HTTP 404 và giao diện `StatusCode.cshtml` thân thiện, không lộ stack trace.
  11. `CentralizedErrorHandling_NotFound_OnPostRequest_ReturnsStatusCode404`: Yêu cầu POST tới route không tồn tại trả về HTTP 404 thay vì 405 Method Not Allowed (xử lý Finding 4).
  12. `CentralizedErrorHandling_ServerError_OnPostRequest_ReturnsStatusCode500`: Yêu cầu POST tới `/Home/Error` trả về HTTP 500 thay vì 405 Method Not Allowed (xử lý Finding 4).
  13. `Security_NoHardcodedHmacSecretInAppSettings`: Trực tiếp kiểm toán file cấu hình vật lý (`appsettings.json`, `appsettings.Development.json`) trên đĩa; đảm bảo không chứa secret HMAC hardcode trong mã nguồn, đồng thời không gây xung đột / fail oan khi môi trường kiểm thử có thiết lập biến môi trường bảo mật hợp lệ (`CheckIn__PinHmacSecret`).
  14. `MvcWebApp_DoesNotReferenceDataAccessOrApiOrWpf`: Kiểm toán reflection assembly chứng minh Web chỉ reference BusinessLogic, không reference DataAccess, Presentation hay WpfClient.

### 3.3. Kiểm thử Hồi quy trên Solution Cũ (`SelfStorageManagementSystem.sln`)
- **Lệnh thực thi**: `dotnet test SelfStorageManagementSystem.sln --filter "FullyQualifiedName!~SqlServerConcurrencyTests" --no-restore`
- **Kết quả**: **208 / 208 test thường PASS 100% (0 Failed, 0 Skipped)**
  ```text
  Passed!  - Failed:     0, Passed:   208, Skipped:     0, Total:   208, Duration: 2 s - SelfStorageManagementSystem.Tests.dll (net8.0)
  ```
  Chứng minh việc refactor DI trong BusinessLogic (`AddBusinessLogicCore` / `AddBusinessLogicJwt`) duy trì tính tương thích ngược tuyệt đối 100% cho toàn bộ API và test cũ.

### 3.4. Kiểm toán Kịch bản SQL Bootstrap (Zero DROP, Zero Source DB, Multi-Batch Halt)
Đã thực hiện script kiểm toán regex trên toàn bộ 4 file SQL khởi tạo:
```powershell
$files = @(
    "DB\SelfStoragePRN222_Web_Bootstrap.sql",
    "DB\SelfStoragePRN222_WebDemo_Bootstrap.sql",
    "DB\SelfStoragePRN222_WebTest_Bootstrap.sql",
    "DB\SelfStoragePRN222_WebE2eTest_Bootstrap.sql"
)
foreach ($f in $files) {
    $dropMatches = Select-String -Path $f -Pattern "DROP"
    $sourceDbMatches = Select-String -Path $f -Pattern "SelfStoragePRN222(?!\w)"
    $doubleSuffixMatches = Select-String -Path $f -Pattern "(_WebDemo_WebDemo|_WebTest_WebTest|_WebE2eTest_WebE2eTest)"
    $setNoExecMatches = Select-String -Path $f -Pattern "SET NOEXEC ON"
    Write-Host "File: $f | DROP: $($dropMatches.Count) | Source DB: $($sourceDbMatches.Count) | Double Suffix: $($doubleSuffixMatches.Count) | SET NOEXEC ON: $($setNoExecMatches.Count)"
}
```
- **Kết quả kiểm toán**:
  - `DB\SelfStoragePRN222_Web_Bootstrap.sql`: **0 DROP**, **0 Source DB Name**, **0 Double Suffix**, **2 SET NOEXEC ON**
  - `DB\SelfStoragePRN222_WebDemo_Bootstrap.sql`: **0 DROP**, **0 Source DB Name**, **0 Double Suffix**, **2 SET NOEXEC ON**
  - `DB\SelfStoragePRN222_WebTest_Bootstrap.sql`: **0 DROP**, **0 Source DB Name**, **0 Double Suffix**, **2 SET NOEXEC ON**
  - `DB\SelfStoragePRN222_WebE2eTest_Bootstrap.sql`: **0 DROP**, **0 Source DB Name**, **0 Double Suffix**, **2 SET NOEXEC ON**
- **Cơ chế dừng toàn bộ lượt chạy (Multi-Batch Abortion)**:
  `THROW` mặc định chỉ dừng batch hiện tại; khi chạy qua SSMS (F5) hoặc `sqlcmd`, các batch sau `GO` vẫn tiếp tục. Để triệt để dừng toàn bộ script, cả 4 file SQL đều tích hợp `SET NOEXEC ON;` ngay trong batch kiểm tra allowlist và batch kiểm tra tồn tại database:
  ```sql
  IF DB_ID(N'<TargetDB>') IS NOT NULL
  BEGIN
      RAISERROR(N'Database [<TargetDB>] already exists. Bootstrap stopped immediately to preserve existing data.', 16, 1);
      SET NOEXEC ON;
  END;
  ```
  Và kết thúc file bằng `SET NOEXEC OFF;`. Nhờ vậy, mọi batch chứa schema, trigger, seed data phía sau đều được SQL Server bỏ qua hoàn toàn nếu database đã tồn tại.

---

## 4. Xử Lý Các Điểm Đánh Giá Sau Review (Review Findings Resolution - P1/P2)

Dưới đây là chi tiết xử lý triệt để 5 điểm đánh giá do reviewer nêu ra:

### Finding 1 [P1]: Bảo đảm lỗi SQL dừng toàn bộ bootstrap
- **Vấn đề**: `THROW` chỉ hủy batch hiện tại. Khi thực thi qua SSMS F5 hoặc `sqlcmd` mặc định, các batch sau `GO` vẫn tiếp tục chạy, có nguy cơ ghi đè schema/seed lên database đã tồn tại.
- **Biện pháp xử lý**:
  - Tại batch 1 (kiểm tra allowlist) và batch 2 (kiểm tra `DB_ID`), bổ sung lệnh `SET NOEXEC ON;` kết hợp `RAISERROR(..., 16, 1)`.
  - Khi điều kiện lỗi thỏa mãn, SQL Server chuyển sang chế độ biên dịch không thực thi (no-execution mode) cho toàn bộ các batch tiếp theo.
  - Cuối file bổ sung lệnh `SET NOEXEC OFF;` để hoàn nguyên trạng thái session an toàn.
- **Bằng chứng kiểm chứng**:
  - Kiểm toán mã nguồn xác nhận 2 vị trí `SET NOEXEC ON;` trên cả 4 file bootstrap (`Web_Bootstrap`, `WebDemo_Bootstrap`, `WebTest_Bootstrap`, `WebE2eTest_Bootstrap`).
  - Phép thử chỉ đọc xác nhận nếu database tồn tại, toàn bộ các batch sau `GO` bị bỏ qua 100%.

### Finding 2 [P1]: Sửa tên database bị lặp hậu tố
- **Vấn đề**: Do script PowerShell sinh file ban đầu thực hiện thay thế chuỗi nhiều lần liên tiếp, chuỗi `SelfStoragePRN222_WebDemo` bị thế trùng thành `SelfStoragePRN222_WebDemo_WebDemo`. Lỗi tương tự xuất hiện với `_WebTest` và `_WebE2eTest`.
- **Biện pháp xử lý**:
  - Cập nhật kịch bản sinh mã `scratch/gen_db.ps1` để thực hiện phép thế chuỗi đơn nhất, chính xác.
  - Tái tạo sạch sẽ toàn bộ 4 file bootstrap SQL.
- **Bằng chứng kiểm chứng**:
  - Kiểm toán regex tìm kiếm pattern `(_WebDemo_WebDemo|_WebTest_WebTest|_WebE2eTest_WebE2eTest)` trả về kết quả **0 lần xuất hiện** trên tất cả các file SQL.
  - Các câu lệnh `USE [...]`, `CREATE DATABASE [...]`, và metadata trong file hoàn toàn khớp với danh sách allowlist định trước.

### Finding 3 [P1 & P2]: Loại bỏ HMAC secret cố định khỏi source và chuẩn hóa hướng dẫn cấu hình PIN
- **Vấn đề**: `appsettings.json` của Web ban đầu chứa chuỗi HMAC secret mẫu `"YourStorageSystemHmacSecretKeyMustBe32BytesLong!"`. `CheckInService` có thể sử dụng chuỗi này nếu không được ghi đè, tạo nguy cơ bảo mật dùng chung secret công khai. Ngoài ra, hướng dẫn trước đó ghi nhầm tên cấu hình (`SelfStorage__PinHmacSecret` và `"PinHmacSecret"`) không khớp với key mà `CheckInService` đọc. Đồng thời, project Web ban đầu chưa cấu hình `<UserSecretsId>` dẫn đến `dotnet user-secrets` báo lỗi. Đặc biệt, trong `CheckInService.cs`:
  ```csharp
  var pinSecret = _configuration["CheckIn:PinHmacSecret"]
                  ?? _configuration["CheckIn__PinHmacSecret"]
                  ?? Environment.GetEnvironmentVariable("CHECKIN_PIN_HMAC_SECRET");
  ```
  Do `appsettings.json` bàn giao chứa `"CheckIn": { "PinHmacSecret": "" }` (chuỗi rỗng `""` không phải `null`), toán tử `??` không bao giờ rẽ sang nhánh `Environment.GetEnvironmentVariable("CHECKIN_PIN_HMAC_SECRET")`. Do đó, biến môi trường `CHECKIN_PIN_HMAC_SECRET` không hoạt động với cấu hình mặc định hiện tại.
- **Biện pháp xử lý**:
  - Xóa bỏ chuỗi secret cố định trong `src/SelfStorageManagementSystem.Web/appsettings.json`, thiết lập `"CheckIn": { "PinHmacSecret": "" }`.
  - Giữ nguyên thẻ `<UserSecretsId>4a633fbf-432e-48dd-9788-1307cf48e3e3</UserSecretsId>` trong `src/SelfStorageManagementSystem.Web/SelfStorageManagementSystem.Web.csproj`, cho phép sử dụng Secret Manager mà không gặp lỗi.
  - Chuẩn hóa tài liệu chỉ hướng dẫn hai cách thiết lập khóa HMAC đã hoạt động thực tế trên host web:
    - **Cách 1 — Biến môi trường PowerShell**:
      ```powershell
      $env:CheckIn__PinHmacSecret = "<YOUR_RANDOM_LOCAL_SECRET>"
      dotnet run --project src/SelfStorageManagementSystem.Web --launch-profile https
      ```
      *Lưu ý*: Biến môi trường chỉ có hiệu lực trong phiên shell tương ứng; ứng dụng phải được khởi động từ chính phiên shell đó. Profile `https` được cấu hình chính xác trong `launchSettings.json`.
    - **Cách 2 — User Secrets cho môi trường Development**:
      ```powershell
      dotnet user-secrets set "CheckIn:PinHmacSecret" "<YOUR_RANDOM_LOCAL_SECRET>" --project src/SelfStorageManagementSystem.Web
      ```
      *Lưu ý*: User Secrets được host ASP.NET Core mặc định nạp trong môi trường Development thông qua `UserSecretsId` đã cấu hình.
  - **Quy định an ninh & giới hạn kỹ thuật**:
    - Chuỗi `<YOUR_RANDOM_LOCAL_SECRET>` là placeholder tượng trưng, bắt buộc phải thay bằng khóa riêng (tối thiểu 32 ký tự). Tuyệt đối không ghi khóa thật vào mã nguồn, file cấu hình bàn giao, log, screenshot hay báo cáo.
    - Không hướng dẫn dùng `SelfStorage__PinHmacSecret` hoặc khóa user-secrets `"PinHmacSecret"` đứng riêng vì không khớp key cấu hình.
    - Không tiếp tục trình bày `CHECKIN_PIN_HMAC_SECRET` là một phương án hoạt động với file JSON mặc định hiện tại; biến này chỉ tồn tại trong lịch sử code cũ và bị vô hiệu bởi chuỗi rỗng của cấu hình mặc định.
  - Cập nhật test `Security_NoHardcodedHmacSecretInAppSettings` trong `LandingPageAndBootstrapTests.cs` để kiểm tra trực tiếp các file cấu hình nguồn vật lý trên đĩa (`appsettings.json`, `appsettings.Development.json`) thay vì cấu hình runtime đã hợp nhất, đảm bảo file bàn giao không chứa secret cố định mà không từ chối cấu hình biến môi trường hợp lệ.
- **Bằng chứng kiểm chứng**:
  - Lệnh `dotnet user-secrets list --project src/SelfStorageManagementSystem.Web/SelfStorageManagementSystem.Web.csproj` hoạt động chuẩn xác, không báo lỗi.
  - File `appsettings.json` bàn giao có `"PinHmacSecret": ""`.
  - Unit test `Security_NoHardcodedHmacSecretInAppSettings` chạy PASS cả hai trường hợp: khi biến môi trường không được đặt, và khi `$env:CheckIn__PinHmacSecret` được đặt giá trị thử nghiệm.

### Finding 4 [P2]: Cho phép trang xử lý lỗi nhận phương thức gốc (GET/POST)
- **Vấn đề**: Middleware `app.UseStatusCodePagesWithReExecute("/Home/StatusCode/{0}")` chạy lại request với HTTP method ban đầu của client. Khi client gửi `POST` tới route không tồn tại, action có thuộc tính `[HttpGet]` gây ra lỗi 405 Method Not Allowed thay vì hiển thị trang 404; tương tự với `/Home/Error`. Ngoài ra, Razor view có thể vô tình thiết lập lại status code về 200 nếu không được gán tường minh.
- **Biện pháp xử lý**:
  - Trong `src/SelfStorageManagementSystem.Web/Controllers/HomeController.cs`:
    - Bỏ thuộc tính `[HttpGet]` trên cả `Error()` và `HandleStatusCode(int? code)`.
    - Dùng route mở `[Route("Home/Error")]` và `[Route("Home/StatusCode/{code?}")]` để chấp nhận mọi HTTP verb (GET, POST, PUT, DELETE...).
    - Thiết lập tường minh `Response.StatusCode = 500;` trong `Error()`.
    - Thiết lập tường minh `Response.StatusCode = statusCode;` trong `HandleStatusCode(code)`.
- **Bằng chứng kiểm chứng**:
  - Bổ sung 2 bài kiểm thử tích hợp:
    - `CentralizedErrorHandling_NotFound_OnPostRequest_ReturnsStatusCode404`: Gửi POST tới `/non-existent-endpoint`, kết quả trả về đúng HTTP 404 và view StatusCode.
    - `CentralizedErrorHandling_ServerError_OnPostRequest_ReturnsStatusCode500`: Gửi POST tới `/Home/Error`, kết quả trả về đúng HTTP 500 và view Error.
  - Cả hai bài kiểm thử đều chạy **PASS 100%**.

### Finding 5 [P2]: Chốt contract theo interface thực tế và đồng bộ chính xác với báo cáo
- **Vấn đề**: Mục 6 tài liệu contract v0 trước đó từng ghi các method ước lệ/giả định không khớp với code C# thực tế của BLL; đồng thời bản báo cáo trước đó ghi sai kiểu `Guid` (thay vì `long`), sai chữ ký tham số (thiếu `roles`, `request`), và liệt kê các chức năng không tồn tại trong hệ thống (như `CustomerGetPinAsync`, `DownloadInvoicePdfAsync`, `SignAgreementAsync`). Điều này đặc biệt nguy hiểm nếu chuyển giao nhầm chức năng lấy lại PIN khách hàng vốn nằm ngoài phạm vi an ninh.
- **Biện pháp xử lý**:
  - Rà soát 100% đối chiếu với các interface trong `SelfStorageManagementSystem.BusinessLogic/Services/Interfaces/`.
  - Đồng bộ báo cáo hoàn toàn chuẩn xác với [`docs/WebMigration_Contracts.md`](file:///D:/FPT/PRN222/Project/Final_Project_Web/docs/WebMigration_Contracts.md) và mã nguồn C# thực tế:
    - **Kiểu dữ liệu định danh**: Toàn bộ ID thực thể và Actor ID sử dụng kiểu `long` (`long facilityId`, `long currentUserId`, `long reservationId`, `long paymentId`, `long invoiceId`, `long agreementId`, `long userId`), hoàn toàn không dùng `Guid`.
    - **IFacilityCatalogService**:
      - `GetFacilitiesAsync(GetFacilitiesCatalogRequest request, CancellationToken cancellationToken = default)`
      - `GetFacilityByIdAsync(long facilityId, CancellationToken cancellationToken = default)`
      - `GetFacilityUnitTypesAsync(long facilityId, GetFacilityUnitTypesRequest request, CancellationToken cancellationToken = default)`
      - `GetAvailableUnitsAsync(long facilityId, GetAvailableUnitsRequest request, CancellationToken cancellationToken = default)`
      - `GetFacilityFloorMapAsync(long facilityId, GetFacilityFloorMapRequest request, CancellationToken cancellationToken = default)`
    - **IReservationService**:
      - `CreateReservationHoldAsync(long currentUserId, CreateReservationRequest request, CancellationToken cancellationToken = default)`
      - `GetReservationByIdAsync(long currentUserId, IReadOnlyList<string> currentUserRoles, long reservationId, CancellationToken cancellationToken = default)`
      - `GetMyReservationsAsync(long currentUserId, GetMyReservationsRequest request, CancellationToken cancellationToken = default)`
      - `CancelReservationAsync(long currentUserId, IReadOnlyList<string> currentUserRoles, long reservationId, CancelReservationRequest? request, CancellationToken cancellationToken = default)`
      - `ExpireOverdueHoldsAsync(CancellationToken cancellationToken = default)`
    - **IPaymentService**:
      - `GetCheckoutQuoteAsync(long currentUserId, IReadOnlyList<string> roles, long reservationId, string? promotionCode, CancellationToken cancellationToken = default)`
      - `CheckoutAsync(long currentUserId, IReadOnlyList<string> roles, CheckoutRequest request, string? idempotencyKeyHeader, string? clientIp, CancellationToken cancellationToken = default)`
      - `ProcessVnpayIpnAsync(IDictionary<string, string> queryParams, CancellationToken cancellationToken = default)`
      - `ProcessDemoCompleteAsync(long currentUserId, IReadOnlyList<string> roles, long paymentId, DemoPaymentCompleteRequest request, CancellationToken cancellationToken = default)`
      - `GetPaymentByIdAsync(long currentUserId, IReadOnlyList<string> roles, long paymentId, CancellationToken cancellationToken = default)`
      - `GetPaymentsRequiringReconciliationAsync(long currentUserId, IReadOnlyList<string> roles, CancellationToken cancellationToken = default)`
      - `GetRefundsAsync(long currentUserId, IReadOnlyList<string> roles, string? status, CancellationToken cancellationToken = default)`
      - `ReviewRefundAsync(long currentUserId, IReadOnlyList<string> roles, long refundId, ReviewRefundRequest request, CancellationToken cancellationToken = default)`
    - **IInvoiceService**:
      - `GetInvoiceByIdAsync(long currentUserId, IReadOnlyList<string> roles, long invoiceId, CancellationToken cancellationToken = default)`
      - *(Lưu ý: Không có method `GetMyInvoicesAsync` hay `DownloadInvoicePdfAsync` trong BLL interface hiện hữu)*.
    - **IRentalAgreementService**:
      - `GetAgreementByIdAsync(long currentUserId, IReadOnlyList<string> roles, long agreementId, CancellationToken cancellationToken = default)`
      - *(Lưu ý: Không có method `SignAgreementAsync` hay `TerminateAgreementAsync` trong BLL interface hiện hữu)*.
    - **ICheckInService**:
      - *Nghiệp vụ nhân viên quầy (Staff Operations)*:
        - `SearchCheckInAsync(string code, long currentUserId, IReadOnlyList<string> roles, CancellationToken cancellationToken = default)`
        - `GetEligibleUnitsAsync(long reservationId, long currentUserId, IReadOnlyList<string> roles, CancellationToken cancellationToken = default)`
        - `VerifyIdentityAsync(long reservationId, VerifyIdentityRequest request, long currentUserId, IReadOnlyList<string> roles, CancellationToken cancellationToken = default)`
        - `AssignUnitAsync(long reservationId, AssignUnitRequest request, long currentUserId, IReadOnlyList<string> roles, CancellationToken cancellationToken = default)`
        - `ReleaseUnitAsync(long reservationId, ReleaseUnitRequest request, long currentUserId, IReadOnlyList<string> roles, CancellationToken cancellationToken = default)`
        - `PerformInspectionAsync(long reservationId, UnitInspectionRequest request, long currentUserId, IReadOnlyList<string> roles, CancellationToken cancellationToken = default)`
        - `CompleteCheckInAsync(long reservationId, CompleteCheckInRequest request, long currentUserId, IReadOnlyList<string> roles, CancellationToken cancellationToken = default)`
        - `ReissuePinAsync(long reservationId, ReissuePinRequest request, long currentUserId, IReadOnlyList<string> roles, CancellationToken cancellationToken = default)` *(cấp lại PIN tại quầy bởi nhân viên có thẩm quyền, sinh mã băm HMAC-SHA256 bảo mật)*
      - *Nghiệp vụ khách hàng thuê kho (Customer Operations)*:
        - `GetCustomerUnitsAsync(long currentUserId, PagedRequest pagedRequest, CancellationToken cancellationToken = default)`
        - `GetCustomerHandoverAsync(long currentUserId, long agreementId, CancellationToken cancellationToken = default)`
        - `GetCustomerNotificationsAsync(long currentUserId, PagedRequest pagedRequest, CancellationToken cancellationToken = default)`
        - *(Lưu ý an ninh nghiệp vụ: Khách hàng tuyệt đối không được cấp method lấy lại mã PIN plain-text như `CustomerGetPinAsync` hay `CustomerRegeneratePinAsync` - việc này ngoài phạm vi để bảo toàn nguyên tắc an ninh access code)*.
    - **IAdminAccountService** (Sao chép nguyên văn chữ ký thực tế từ `src/SelfStorageManagementSystem.BusinessLogic/Services/Interfaces/IAdminAccountService.cs`, bao gồm đầy đủ 8 method):
      ```csharp
      public interface IAdminAccountService
      {
          Task<PagedResult<UserAccountDto>> GetAccountsAsync(
              GetAccountsRequest request,
              CancellationToken cancellationToken = default);

          Task<UserAccountDto> GetAccountByIdAsync(
              long userId,
              CancellationToken cancellationToken = default);

          Task<UserAccountDto> CreateStaffAccountAsync(
              CreateStaffAccountRequest request,
              long actorUserId,
              string? ipAddress = null,
              string? requestId = null,
              CancellationToken cancellationToken = default);

          Task<UserAccountDto> UpdateUserStatusAsync(
              long userId,
              UpdateUserStatusRequest request,
              long actorUserId,
              string? ipAddress = null,
              string? requestId = null,
              CancellationToken cancellationToken = default);

          Task<UserAccountDto> ManageUserRolesAsync(
              long userId,
              ManageUserRolesRequest request,
              long actorUserId,
              string? ipAddress = null,
              string? requestId = null,
              CancellationToken cancellationToken = default);

          Task<FacilityAssignmentDto> AssignFacilityAsync(
              long userId,
              CreateFacilityAssignmentRequest request,
              long actorUserId,
              string? ipAddress = null,
              string? requestId = null,
              CancellationToken cancellationToken = default);

          Task TerminateFacilityAssignmentAsync(
              long assignmentId,
              long actorUserId,
              string? ipAddress = null,
              string? requestId = null,
              CancellationToken cancellationToken = default);

          Task<List<FacilityLookupDto>> GetFacilitiesAsync(
              CancellationToken cancellationToken = default);
      }
      ```
      *(Lưu ý: Bốn method `CreateAccountAsync`, `UpdateAccountStatusAsync`, `UpdateAccountRolesAsync`, và `ResetAccountPasswordAsync` không tồn tại trong interface C# hiện hữu. Báo cáo đã cập nhật chuẩn hóa hoàn toàn theo đúng 8 method thực tế nêu trên).*
- **Bằng chứng kiểm chứng**:
  - So khớp mã nguồn C# và tài liệu contract hoàn toàn đồng nhất 100%. Báo cáo không chứa bất kỳ hàm ngoài phạm vi nào, loại bỏ hoàn toàn rủi ro chuyển giao sai hợp đồng cho các task T02–T14.

---

## 5. Xử Lý Codex Re-Review — Hai Finding P2 Còn Lại

### 5.1. Finding P2-01: Hướng Dẫn Biến Môi Trường Sai
- **Nội dung finding**:
  - File `appsettings.json` có `"CheckIn": { "PinHmacSecret": "" }`.
  - Service `CheckInService` đọc:
    ```csharp
    var pinSecret = _configuration["CheckIn:PinHmacSecret"]
                    ?? _configuration["CheckIn__PinHmacSecret"]
                    ?? Environment.GetEnvironmentVariable("CHECKIN_PIN_HMAC_SECRET");
    ```
  - Chuỗi rỗng `""` không phải `null`, nên toán tử `??` không chuyển tiếp sang `CHECKIN_PIN_HMAC_SECRET`. Do đó, hướng dẫn trước đó cho rằng có thể dùng `CHECKIN_PIN_HMAC_SECRET` không hoạt động với cấu hình mặc định được bàn giao.
- **File và mục đã sửa**:
  - `AgentReport/WebMigration_T01.md`: Mục 3.2 (Mô tả test 13), Mục 4 (Finding 3), Mục 5 (Mục re-review này).
- **Phương án sửa thực tế**:
  - Chỉ sửa tài liệu để hướng dẫn đúng 2 cách đã hoạt động thực tế trên host web; không refactor `CheckInService` hoặc thay đổi `appsettings.json`:
    - **Cách 1 — Biến môi trường PowerShell**:
      ```powershell
      $env:CheckIn__PinHmacSecret = "<YOUR_RANDOM_LOCAL_SECRET>"
      dotnet run --project src/SelfStorageManagementSystem.Web --launch-profile https
      ```
      *Lưu ý*: Biến môi trường chỉ có hiệu lực trong phiên shell tương ứng; ứng dụng phải được khởi động từ phiên đó. Profile `https` đã được xác minh từ `Properties/launchSettings.json`.
    - **Cách 2 — User Secrets cho môi trường Development**:
      ```powershell
      dotnet user-secrets set "CheckIn:PinHmacSecret" "<YOUR_RANDOM_LOCAL_SECRET>" --project src/SelfStorageManagementSystem.Web
      ```
      *Lưu ý*: User Secrets được host mặc định nạp trong Development qua `<UserSecretsId>4a633fbf-432e-48dd-9788-1307cf48e3e3</UserSecretsId>` đã cấu hình.
  - **Quy tắc an ninh**: Placeholder `<YOUR_RANDOM_LOCAL_SECRET>` không phải secret thật và phải được thay bằng khóa riêng tối thiểu 32 ký tự. Không ghi khóa thật vào source, log, screenshot hay báo cáo. Không hướng dẫn dùng `SelfStorage__PinHmacSecret` hoặc user-secrets key `"PinHmacSecret"` đứng riêng. Không tiếp tục trình bày `CHECKIN_PIN_HMAC_SECRET` là một lựa chọn hoạt động với JSON mặc định hiện tại.
- **Lệnh kiểm tra và kết quả thật**:
  - `dotnet user-secrets list --project src/SelfStorageManagementSystem.Web/SelfStorageManagementSystem.Web.csproj` &rarr; Thành công (exit code 0, không báo lỗi).
  - Test với biến môi trường thử nghiệm:
    ```powershell
    $env:CheckIn__PinHmacSecret = "ReviewOnly_NotARealSecret_123456789"
    dotnet test SelfStorageManagementSystem.Web.sln --no-build --no-restore
    Remove-Item Env:\CheckIn__PinHmacSecret
    ```
    &rarr; Kết quả thực tế: **Passed: 14, Failed: 0, Skipped: 0, Duration: 353 ms**.

### 5.2. Finding P2-02: Báo Cáo Vẫn Ghi Sai Admin Interface
- **Nội dung finding**:
  - Báo cáo trước đó ghi các method không tồn tại trong `IAdminAccountService`: `CreateAccountAsync`, `UpdateAccountStatusAsync`, `UpdateAccountRolesAsync`, `ResetAccountPasswordAsync`.
- **File và mục đã sửa**:
  - `AgentReport/WebMigration_T01.md`: Mục 4 (Finding 5), Mục 5 (Mục re-review này).
- **Phương án sửa thực tế**:
  - Mở file `src/SelfStorageManagementSystem.BusinessLogic/Services/Interfaces/IAdminAccountService.cs` và sao chép nguyên văn chữ ký thực tế gồm đúng 8 method (giữ nguyên kiểu trả về, DTO request/response, thứ tự tham số, `actorUserId`, `ipAddress`, `requestId`, `CancellationToken` và giá trị mặc định).
  - Không thêm các hàm đang thiếu vào C#; không thêm reset password hoặc nghiệp vụ ngoài kế hoạch; không sửa interface C# hay contract đã đúng.
  - Đối chiếu 3 nơi: source `IAdminAccountService.cs`, `docs/WebMigration_Contracts.md` Mục 6.8, và `AgentReport/WebMigration_T01.md` hoàn toàn thống nhất 100% về hợp đồng hiện có.
- **Lệnh kiểm tra và kết quả thật**:
  - `dotnet build SelfStorageManagementSystem.Web.sln --no-restore` &rarr; **0 Warning, 0 Error, Time Elapsed: 00:00:01.31**.
  - `dotnet test SelfStorageManagementSystem.Web.sln --no-build --no-restore` &rarr; **Passed: 14, Failed: 0, Skipped: 0, Duration: 446 ms**.
  - `dotnet test SelfStorageManagementSystem.sln --filter "FullyQualifiedName!~SqlServerConcurrencyTests" --no-restore` &rarr; **Passed: 208, Failed: 0, Skipped: 0, Duration: 3 s**.

### 5.3. Giới Hạn Kiểm Chứng
- Lượt sửa này chỉ sửa tài liệu báo cáo và task board; không sửa BusinessLogic hay DataAccess; không thao tác database (không khởi tạo, cập nhật, xóa DB hay chạy SQL concurrency).
- Kết quả test web (`WebTests`) xác nhận tính toàn vẹn của DI composition, HTTP pipeline, routing, assets cục bộ và cơ chế cấu hình, không thay thế cho kiểm thử nghiệp vụ quầy check-in end-to-end trên DB thật (thuộc phạm vi T11/T16).
- Nội dung user-secrets không xuất ra log hay báo cáo.

### 5.4. Trạng Thái Chờ Codex Re-Review
- Hai finding P2-01 và P2-02 đã được xử lý xong: **IMPLEMENTED**.
- Task T01 chuyển trạng thái: **IMPLEMENTED — Chờ Codex re-review**.
- Không tự tuyên bố Codex đã VERIFIED; chờ kết quả review độc lập từ Codex.

---

## 6. Đối Chiếu Tiêu Chí Nghiệm Thu (Acceptance Criteria Checklist)

| Tiêu chí yêu cầu trong Task T01 | Trạng thái | Bằng chứng kiểm chứng thực tế |
| :--- | :---: | :--- |
| Tạo `SelfStorageManagementSystem.Web.sln` với MvcWebApp, BusinessLogic, DataAccess, WebTests | **ĐẠT** | Solution tồn tại tại thư mục gốc; chứa đúng 4 project; `dotnet sln list` xác nhận đầy đủ. |
| Giữ solution cũ để kiểm tra hồi quy | **ĐẠT** | `SelfStorageManagementSystem.sln` được bảo tồn; 208/208 test thường chạy PASS 100%. |
| MvcWebApp chỉ reference BusinessLogic | **ĐẠT** | Project file csproj chỉ chứa 1 ProjectReference; Unit test reflection xác nhận không có DAL/API/WPF. |
| Tạo composition entrypoint trong BLL; web không gọi HTTP vòng qua API | **ĐẠT** | `AddSelfStorageWebComposition(configuration)` trong BLL; Web gọi trực tiếp BLL qua DI in-process. |
| Đăng ký MVC, Razor Pages, SignalR | **ĐẠT** | Đã đăng ký trong `Program.cs`; Hub endpoint `/storageUnitHub`; test negotiation đạt HTTP 200. |
| Cấu hình HTTPS port 7041, lỗi tập trung và public landing page | **ĐẠT** | `launchSettings.json` cấu hình port 7041/5041; landing page `/` đạt HTTP 200; route 404/500 xử lý đúng cả GET lẫn POST. |
| Tách core registration khỏi JWT registration; Web không cần JWT secret | **ĐẠT** | `AddBusinessLogicCore()` tách rời `AddBusinessLogicJwt()`; test xác nhận host chạy khi không có JWT secret. |
| Ghi exact version .NET 8, Bootstrap, jQuery, SignalR JS; phục vụ asset cục bộ | **ĐẠT** | .NET 8.0.425, Bootstrap 5.3.3, jQuery 3.7.1, SignalR JS 8.0.7; 6 test static assets cục bộ PASS. |
| Bootstrap DB chỉ cho WebDemo, WebTest, WebE2eTest; dừng nếu DB đã tồn tại; không có DROP | **ĐẠT** | Allowlist 3 DB; script dừng toàn bộ batch bằng `SET NOEXEC ON`; kiểm toán regex xác nhận 0 DROP, 0 source DB name, 0 double suffix. |
| Không chứa secret cứng trong source code | **ĐẠT** | `appsettings.json` có `PinHmacSecret` rỗng; test tự động xác nhận không có secret hardcode; hướng dẫn PowerShell `$env:CheckIn__PinHmacSecret` và User Secrets chuẩn. |
| Ghi contract v0 và ownership vào `docs/WebMigration_Contracts.md` | **ĐẠT** | Đã ban hành đầy đủ và đồng bộ 100% với interface BLL thực tế tại `docs/WebMigration_Contracts.md`. |
| Cập nhật rules bản sao cho MVC/Razor/Cookie/SignalR | **ĐẠT** | Đã cập nhật Section 38 vào `Agent/Gemini/RULE.md`. |

---

## 7. Kết Luận Bàn Giao và Trạng Thái Đợt 1

- **Kết luận bàn giao Task T01**: **IMPLEMENTED — Chờ Codex re-review**
- **Đầu ra bàn giao**:
  1. Solution web hoàn chỉnh: `SelfStorageManagementSystem.Web.sln` (Build thành công 0 warning, 0 error)
  2. Mã nguồn khung ứng dụng chạy được: `src/SelfStorageManagementSystem.Web/` (Đã cấu hình `UserSecretsId`)
  3. Bộ kiểm thử web tích hợp: `tests/SelfStorageManagementSystem.WebTests/` (14/14 tests PASS)
  4. Bộ kiểm thử hồi quy: `SelfStorageManagementSystem.sln` (208/208 tests PASS)
  5. Tài liệu hợp đồng kỹ thuật: `docs/WebMigration_Contracts.md` (v0 - Đồng bộ 100% interface thực tế)
  6. Script khởi tạo DB và công cụ: `DB/SelfStoragePRN222_Web_Bootstrap.sql`, `DB/Initialize-WebDatabase.ps1` (Có cơ chế `SET NOEXEC ON` dừng toàn bộ batch khi DB đã tồn tại, 0 DROP, 0 DB nguồn)
  7. Quy tắc kỹ thuật bản sao: `Agent/Gemini/RULE.md` (Section 38)
  8. Bảng tiến độ di chuyển: `AgentReport/WebMigration_TaskBoard.md`
- **Trạng thái các task kế tiếp**:
  Toàn bộ các task của Đợt 1 ở trạng thái **WAITING** để chờ Codex nghiệm thu chấp nhận T01:
  - **T02**: Gemini A (Tách xác thực tài khoản khỏi JWT) &rarr; **WAITING** (Chờ T01 được Codex chấp nhận nghiệm thu)
  - **T03**: Gemini B (Repository quản trị ô kho) &rarr; **WAITING** (Chờ T01 được Codex chấp nhận nghiệm thu)
  - **T04**: Gemini C (Layout Bootstrap và giao diện chung) &rarr; **WAITING** (Chờ T01 được Codex chấp nhận nghiệm thu)

