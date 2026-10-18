# Báo Cáo Triển Khai Task T16 Vòng R1 — Kiểm Thử Cookie, Quyền và HTTP Thực

- **Ngày thực hiện**: 2026-10-18
- **Tác giả**: Gemini A (Foundation & Integration Lead)
- **Không gian làm việc chính**: `D:\FPT\PRN222\Project\Final_Project_Web`
- **Không gian làm việc đồng bộ**: `D:\FPT\PRN222\Project\Final_Project`
- **Git Baseline**: `82496d648624ddf9af82d481017cfdd12e612787` (T12–T15 VERIFIED, smoke WebTests 403/403 PASS)
- **Trạng thái task T16**: **IMPLEMENTED — chờ Codex review** (Tuân thủ quy ước: Không tự đánh dấu VERIFIED)
- **Bảo toàn các tệp ngoài commit**: Giữ nguyên vẹn `docs/KeHoach_Web_Gemini_Va_Codex_Review.docx`, `AgentReport/WebMigration_T15_CodexLoopState.json`, `scratch/generate_r3_report.js`.

---

## 1. Mục Tiêu và Phạm Vi Sở Hữu Task T16

Task T16 tập trung thiết lập bộ kiểm thử tích hợp an ninh và HTTP thực tế độc lập, kiểm chứng toàn diện cơ chế xác thực Cookie, phân quyền theo vai trò (Roles) và cơ sở (Facility Scope), chống tấn công IDOR, xác thực động `OnValidatePrincipal`, bảo vệ CSRF / Antiforgery, và hợp đồng Realtime SignalR trên máy chủ ASP.NET Core Kestrel thật lắng nghe trên cổng TCP loopback (`http://127.0.0.1:{port}`).

### 1.1. Phạm Vi Sở Hữu Tệp (File Ownership)
Tuân thủ nghiêm ngặt ma trận sở hữu:
- **Tệp harness/test mới**:
  1. `tests/SelfStorageManagementSystem.WebTests/SecurityHttpServerFixture.cs`
  2. `tests/SelfStorageManagementSystem.WebTests/SecurityHttpLiveServerTests.cs`
- **Báo cáo**: `AgentReport/WebMigration_T16_R1_FindingFixes.md` (ở cả hai workspace).
- **Quy tắc phối hợp đa agent**: Không chỉnh sửa `Program.cs`, các tệp `*.csproj`, `TaskBoard.md`, hay bất kỳ tệp chung/fixture dùng chung nào của Gemini B (T17) và Gemini C (T18). Không tự ý reset/restore mã nguồn của các chat khác.

### 1.2. Tiêu Chuẩn Bằng Chứng Thực Nghiệm
- Toàn bộ 28 kiểm thử an ninh được chạy trên Kestrel HTTP/TCP loopback thật với Cookie thật và tài khoản CSDL thật (mã hóa mật khẩu bằng `IPasswordHasher` thật, phiên cookie được mã hóa và giải mã bằng `IDataProtectionProvider` thật).
- Tuyệt đối không lấy Service Mocks hay `TestAuthHandler` làm bằng chứng duy nhất. Mọi truy vấn HTTP đều qua `HttpClient` thật gửi request qua mạng TCP nội bộ.

---

## 2. Danh Sách Tệp Thay Đổi và Lý Do

| STT | Đường dẫn tệp | Loại thao tác | Mục đích và lý do kỹ thuật |
| :---: | :--- | :---: | :--- |
| 1 | `tests/SelfStorageManagementSystem.WebTests/SecurityHttpServerFixture.cs` | Tạo mới | Fixture khởi chạy độc lập một máy chủ ASP.NET Core Kestrel trên cổng TCP loopback ngẫu nhiên (`IPAddress.Loopback, 0`), nạp đầy đủ cấu hình production từ `Program.cs` (Cookie Auth, 4 Policy phân quyền, Antiforgery, SignalR `/storageUnitHub`, ExceptionHandler `/Home/Error`, StatusCode re-execute `/Home/StatusCode/{0}`). Cung cấp bộ seed CSDL cho 5 vai trò, cơ sở, ô kho, đơn đặt chỗ, và các helper `GetAntiforgeryTokensAsync`, `LoginViaHttpAsync`. |
| 2 | `tests/SelfStorageManagementSystem.WebTests/SecurityHttpLiveServerTests.cs` | Tạo mới | Bộ 28 ca kiểm thử tích hợp chuyên sâu bao phủ 10 nhóm yêu cầu an ninh: ẩn danh, phân quyền 5 vai trò, scope cơ sở, IDOR, thu hồi phiên tức thì khi khóa tài khoản/nghỉ việc, tự động làm mới claims, hết hạn sliding expiration, logout & chống replay, chống CSRF/Open Redirect, chống SQLi/XSS/rò rỉ secret & PIN, và bảo toàn contract SignalR trên loopback TCP. |
| 3 | `AgentReport/WebMigration_T16_R1_FindingFixes.md` | Tạo mới | Báo cáo chi tiết kết quả thực nghiệm, nhật ký lệnh, bằng chứng HTTP và đối chiếu mã băm SHA-256 giữa hai workspace. |

---

## 3. Chi Tiết Thực Nghiệm & Bằng Chứng HTTP Thực Tế

### 3.1. Nhóm 1: Kiểm thử Ẩn Danh (Anonymous Access over HTTP & WebSocket)
- **MVC Endpoints** (`/Account/Profile`, `/Account/SecuredData`): Gửi HTTP GET không kèm Cookie nhận HTTP `302 Found`, chuyển hướng chính xác đến `/Account/Login?ReturnUrl=%2FAccount%2FProfile`.
- **Razor Pages** (`/StorageUnits`, `/Reservations`, `/Staff/CheckIn`, `/Admin/Accounts`): Nhận HTTP `302 Found`, chuyển hướng an toàn về `/Account/Login` kèm `ReturnUrl` tương ứng.
- **Yêu cầu AJAX** (`X-Requested-With: XMLHttpRequest`): Gửi GET tới `/Account/SecuredData` nhận mã `401 Unauthorized`, `Content-Type: application/json; charset=utf-8`, payload `{"statusCode":401,"message":"Yêu cầu xác thực phiên đăng nhập (Unauthorized)."}` mà không bị rơi vào vòng lặp chuyển hướng HTML.
- **SignalR Negotiate** (`POST /storageUnitHub/negotiate?negotiateVersion=1`): Không có Cookie xác thực bị từ chối ngay với `401 Unauthorized`.
- **WebSocket Direct Connection**: Thử nghiệm kết nối WebSocket trực tiếp không có cookie bị ngắt kết nối/từ chối bắt tay ngay từ tầng transport.

### 3.2. Nhóm 2: Phân Quyền Theo Vai Trò (Role Authorization Matrix)
- **`storage_customer`**: Đăng nhập bằng tài khoản khách hàng thật:
  - Truy cập thành công: `/Account/Profile` (200 OK), `/Account/SecuredData` (200 OK), `/Reservations` (200 OK).
  - Bị từ chối truy cập: `/StorageUnits` (302 sang `/Account/Forbidden`), `/Staff/CheckIn` (302 sang `/Account/Forbidden`), `/Admin/Accounts` (302 sang `/Account/Forbidden`), `/Account/AdminCheck` (302 sang `/Account/Forbidden`).
  - Gửi AJAX request tới `/Account/AdminCheck`: Nhận mã `403 Forbidden` dạng JSON `{"statusCode":403,"message":"Từ chối quyền truy cập tài nguyên (Forbidden)."}`.
- **`facility_staff`**: Đăng nhập nhân viên:
  - Truy cập thành công `/StorageUnits` (200 OK) và `/Staff/CheckIn` (200 OK).
  - Bị chặn khi vào `/Admin/Accounts` và `/Account/AdminCheck` (302 sang `/Account/Forbidden`).
- **`system_administrator`**: Đăng nhập tài khoản quản trị hệ thống:
  - Truy cập thành công tất cả các trang: `/StorageUnits`, `/Staff/CheckIn`, `/Admin/Accounts`, `/Account/AdminCheck` (200 OK, trả JSON `{"status":"authorized",...}`).

### 3.3. Nhóm 3: Phân Vùng Quyền Hạn Theo Cơ Sở (Facility Scope Enforcement)
- Nhân viên cơ sở A (được phân công duy nhất tại Cơ sở A) kết nối tới `/storageUnitHub` bằng phiên Cookie hợp lệ.
- Thao tác xóa ô kho thuộc Cơ sở B (`HubDelete_StorageUnit`):
- Bị từ chối quyền hạn ở tầng BLL (`IStorageUnitAdminService.DeleteAsync` ném `ForbiddenException` / Hub ném `HubException`), ô kho tại Cơ sở B hoàn toàn không bị xóa trong CSDL.

### 3.4. Nhóm 4: Chống Lỗ Hổng IDOR (Insecure Direct Object Reference)
- Khách hàng A tạo đơn giữ chỗ đặt kho A (`reservation`).
- Khách hàng B đăng nhập phiên hợp lệ, lấy Cookie B.
- Khách hàng B cố ý gửi GET `/Reservations/Details?id={id_của_A}`:
  `ReservationService.GetReservationByIdAsync` kiểm tra quyền sở hữu (`customer_id == currentUserId`). Nhận diện vi phạm IDOR -> ném `ForbiddenException` -> Razor PageModel thực thi `Forbid()` -> Kestrel trả mã `302 Found` chuyển hướng về `/Account/Forbidden`.
- Khách hàng B gửi AJAX request `/Reservations/Details?id={id_của_A}&handler=Status`: Bị từ chối quyền với mã `403 Forbidden` / chuyển hướng Forbidden, dữ liệu đơn của Khách hàng A hoàn toàn được bảo vệ.

### 3.5. Nhóm 5: Tự Động Thu Hồi Phiên & Cập Nhật Claims Động (`OnValidatePrincipal`)
- **Tài khoản bị khóa (`user.status = "disabled"`)**:
  Người dùng đang đăng nhập hợp lệ. Trực tiếp cập nhật `status = "disabled"` trong CSDL. Ở HTTP request kế tiếp với cùng Auth Cookie cũ: `OnValidatePrincipal` kiểm tra snapshot identity tươi từ CSDL, lập tức gọi `context.RejectPrincipal()` và `SignOutAsync()`. Kestrel trả mã `302 Found` chuyển hướng về `/Account/Login` và hủy phiên, không chờ hết hạn 5 phút cookie.
- **Nhân sự nghỉ việc (`employment_status = "terminated"`)**:
  Nhân viên đang đăng nhập, CSDL cập nhật trạng thái nghỉ việc. HTTP request tiếp theo ngay lập tức bị từ chối và chuyển hướng về `/Account/Login`.
- **Nâng quyền tài khoản (`user_roles` thêm vai trò Admin)**:
  Khách hàng ban đầu chỉ có role `storage_customer`. CSDL được cấp thêm role `system_administrator`. Ở HTTP request kế tiếp, `OnValidatePrincipal` phát hiện chênh lệch danh sách roles, tự động làm mới ClaimsIdentity và kích hoạt `context.ShouldRenew = true`. Response trả về header `Set-Cookie` chứa vé đăng nhập mới; client ngay lập tức có thể truy cập `/Account/AdminCheck` với mã `200 OK`.

### 3.6. Nhóm 6: Hết Hạn và Sliding Expiration
- Xác thực trực tiếp cấu hình DI của `CookieAuthenticationOptions`:
  - `Cookie.Name = "SelfStoragePRN222.Web.Auth"`
  - `HttpOnly = true`, `SameSite = Lax`
  - `ExpireTimeSpan = TimeSpan.FromMinutes(5)`
  - `SlidingExpiration = true`
- Gửi vé đăng nhập đã hết hạn (được mã hóa hợp lệ nhưng mốc `ExpiresUtc` ở quá khứ): Kestrel cookie authentication handler phát hiện vé hết hạn, từ chối xác thực và trả mã `302 Found` chuyển hướng về `/Account/Login`.

### 3.7. Nhóm 7: Đăng Xuất và Chống Tấn Công Replay (Logout & Replay Resistance)
- **GET `/Account/Logout`**: Hiển thị trang xác nhận với mã `200 OK`, tuyệt đối không thay đổi trạng thái đăng nhập nhằm chống tấn công CSRF Logout.
- **POST `/Account/Logout`**: Bắt buộc có Antiforgery token hợp lệ. Khi gửi thành công, server gọi `SignOutAsync()`, trả mã `302 Found` về `/Account/Login`, và gửi header `Set-Cookie: SelfStoragePRN222.Web.Auth=; expires=Thu, 01 Jan 1970 00:00:00 GMT` để xóa cookie tại trình duyệt.
- Cookie cũ sau khi tài khoản bị khóa hoặc hủy phiên bị từ chối truy cập hoàn toàn nếu kẻ tấn công cố tình gửi lại (replay attack).

### 3.8. Nhóm 8: CSRF / Antiforgery & Chống Lỗ Hổng Open Redirect
- POST `/Account/Login` không có token `__RequestVerificationToken`: Kestrel antiforgery middleware trả về mã `400 Bad Request`.
- POST `/Account/Login` với token giả mạo/sai lệch: Trả về mã `400 Bad Request`.
- POST `/Account/Login` kèm token hợp lệ: Xác thực thành công và thiết lập phiên đăng nhập.
- **Chống Open Redirect**: POST `/Account/Login` với tham số `ReturnUrl = "https://attacker.evil.com/phishing"`. Controller kiểm tra `Url.IsLocalUrl`, phát hiện URL ngoại bộ, lập tức từ chối và chỉ chuyển hướng an toàn về `/`.

### 3.9. Nhóm 9: Negative Cases & Chống Rò Rỉ Thông Tin (Zero Leakage)
- Gửi payload SQL Injection vào form đăng nhập (`UserName = "admin' OR '1'='1'--"` và `Password = "' UNION SELECT 1,2,3,4,5--"`): Hệ thống xử lý an toàn, hiển thị thông báo lỗi nghiệp vụ thân thiện ("Tên đăng nhập hoặc mật khẩu không chính xác..."). Phản hồi HTML tuyệt đối không chứa chuỗi lỗi SQL, `Microsoft.EntityFrameworkCore`, `SqlException`, cú pháp `SELECT`, hoặc stack trace.
- Truy cập tuyến đường không tồn tại (`/NonExistent_Random_Path_12345`): Kestrel kích hoạt `UseStatusCodePagesWithReExecute`, trả về mã `404 Not Found` với view thân thiện `StatusCode.cshtml`. Hoàn toàn không để lộ exception dump hay `at System.`.
- Kiểm tra toàn bộ phản hồi hệ thống: Tuyệt đối không chứa chuỗi băm mật khẩu (`$argon2`, `AQAAAA`), không chứa khóa bí mật JWT (`JWT_SECRET_KEY`), và không để lộ mã PIN quầy.

### 3.10. Nhóm 10: Bảo Toàn Hợp Đồng SignalR Realtime Trên Kestrel Loopback
- Kết nối tới endpoint `/storageUnitHub` qua cổng TCP loopback thật với Cookie xác thực của Manager:
- Gọi `HubCreate_StorageUnit`: Thành công tạo ô kho trong CSDL, trả về DTO và phát sự kiện broadcast `Receiver_CreateStorageUnit` tới client.
- Gọi `HubUpdate_StorageUnit`: Cập nhật ô kho và phát sự kiện broadcast `Receiver_UpdateStorageUnit`.
- Gọi `HubDelete_StorageUnit`: Xóa ô kho nguyên tử và phát sự kiện broadcast `Receiver_DeleteStorageUnit`.
- Toàn bộ tên hàm và route `/storageUnitHub` được bảo toàn nguyên vẹn 100%.

---

## 4. Kết Quả Kiểm Thử Thực Tế

### 4.1. Kiểm Thử Mục Tiêu T16 (`SecurityHttpLiveServerTests`)
Lệnh thực thi:
```powershell
dotnet test tests/SelfStorageManagementSystem.WebTests/SelfStorageManagementSystem.WebTests.csproj --filter "FullyQualifiedName~SecurityHttpLiveServerTests" --no-restore
```
Kết quả:
```text
Test run for D:\FPT\PRN222\Project\Final_Project_Web\tests\SelfStorageManagementSystem.WebTests\bin\Debug\net8.0\SelfStorageManagementSystem.WebTests.dll (.NETCoreApp,Version=v8.0)
A total of 1 test files matched the specified pattern.

Passed!  - Failed:     0, Passed:    28, Skipped:     0, Total:    28, Duration: 2 s - SelfStorageManagementSystem.WebTests.dll (net8.0)
```
- **Tổng số**: 28 bài test
- **Đạt (Passed)**: 28 (100%)
- **Thất bại (Failed)**: 0
- **Bỏ qua (Skipped)**: 0

### 4.2. Kiểm Thử Hồi Quy WebTests
Lệnh thực thi (loại trừ tệp `T18TwoBrowserRealtimeE2ETests` đang được Gemini C phát triển song song trong cùng workspace):
```powershell
dotnet test tests/SelfStorageManagementSystem.WebTests/SelfStorageManagementSystem.WebTests.csproj --filter "ClassName!=SelfStorageManagementSystem.WebTests.T18TwoBrowserRealtimeE2ETests" --no-restore --verbosity quiet
```
Kết quả:
```text
Test run for D:\FPT\PRN222\Project\Final_Project_Web\tests\SelfStorageManagementSystem.WebTests\bin\Debug\net8.0\SelfStorageManagementSystem.WebTests.dll (.NETCoreApp,Version=v8.0)
A total of 1 test files matched the specified pattern.

Passed!  - Failed:     0, Passed:   437, Skipped:     0, Total:   437, Duration: 4 m 39 s - SelfStorageManagementSystem.WebTests.dll (net8.0)
```
- **Tổng số**: 437 bài test (Bao gồm 403 test baseline + 28 test T16 + các test T17)
- **Đạt (Passed)**: 437 (100%)
- **Thất bại (Failed)**: 0
- **Bỏ qua (Skipped)**: 0

### 4.3. Kiểm Thử Tại Workspace Đồng Bộ `Final_Project`
Lệnh thực thi:
```powershell
dotnet test tests/SelfStorageManagementSystem.WebTests/SelfStorageManagementSystem.WebTests.csproj --filter "FullyQualifiedName~SecurityHttpLiveServerTests"
```
Kết quả:
```text
Test run for D:\FPT\PRN222\Project\Final_Project\tests\SelfStorageManagementSystem.WebTests\bin\Debug\net8.0\SelfStorageManagementSystem.WebTests.dll (.NETCoreApp,Version=v8.0)
A total of 1 test files matched the specified pattern.

Passed!  - Failed:     0, Passed:    28, Skipped:     0, Total:    28, Duration: 2 s - SelfStorageManagementSystem.WebTests.dll (net8.0)
```

---

## 5. Bảng Đối Chiếu Mã Băm SHA-256 Giữa Hai Workspace

Toàn bộ các tệp thuộc quyền sở hữu của Task T16 đã được đồng bộ độc lập sang `D:\FPT\PRN222\Project\Final_Project` (không sao chép toàn bộ repository) và đối chiếu mã băm SHA-256 đạt trùng khớp 100%:

| Tệp tin liên quan T16 | SHA-256 tại `Final_Project_Web` | SHA-256 tại `Final_Project` | Trạng thái khớp |
| :--- | :--- | :--- | :---: |
| `tests/SelfStorageManagementSystem.WebTests/SecurityHttpServerFixture.cs` | `EF878D63812FA0770A188EEE15F23778DF224718DA0E27632F7D0D6053E2E784` | `EF878D63812FA0770A188EEE15F23778DF224718DA0E27632F7D0D6053E2E784` | **KHỚP 100%** |
| `tests/SelfStorageManagementSystem.WebTests/SecurityHttpLiveServerTests.cs` | `F87D577C0882E77507855F632FD43794FE11AD7915970711F172150CBB5CA1D6` | `F87D577C0882E77507855F632FD43794FE11AD7915970711F172150CBB5CA1D6` | **KHỚP 100%** |
| `AgentReport/WebMigration_T16_R1_FindingFixes.md` | *(Tính toán sau khi ghi)* | *(Tính toán sau khi ghi)* | **ĐỒNG BỘ** |

---

## 6. Lỗi và Giới Hạn Của Đợt Kiểm Thử

1. **Phạm vi CSDL**: Bộ kiểm thử T16 được chạy trên máy chủ Kestrel thật kết nối CSDL EF Core In-Memory để kiểm tra trọn vẹn tầng Presentation, Middleware, Cookie Authentication, Antiforgery và Routing. Việc kiểm thử tranh chấp khóa vật lý (pessimistic lock) và rollback trên SQL Server vật lý thuộc quyền sở hữu của **Task T17** (Gemini B).
2. **Phạm vi Trình Duyệt Thực**: Kiểm thử tự động đa trình duyệt E2E đồng thời trên Microsoft Edge qua giao thức CDP thuộc quyền sở hữu của **Task T18** (Gemini C).
3. **Tính Độc Lập Kiến Trúc**: Không có bất kỳ thay đổi nào tác động vào tầng DataAccess hay Presentation API cũ; kiến trúc 3 tầng Web $\rightarrow$ BLL $\rightarrow$ DAL được giữ gìn nguyên vẹn.

---

## 7. Kết Luận Nghiệm Thu

Task T16 đã hoàn thành đầy đủ mọi yêu cầu kỹ thuật và phạm vi an ninh:
- Đã kiểm thử trên HTTP/Kestrel thật với Cookie thật và tài khoản/quyền thật.
- 28/28 bài kiểm thử mục tiêu T16 đạt **PASS 100%**.
- 437 bài kiểm thử hồi quy WebTests đạt **PASS 100%**.
- Đồng bộ hoàn tất và khớp mã băm SHA-256 sang `Final_Project`.
- Đánh dấu trạng thái: **IMPLEMENTED — chờ Codex review**.
