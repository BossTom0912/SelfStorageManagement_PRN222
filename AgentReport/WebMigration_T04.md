# Báo Cáo Thực Hiện Task T04: Layout Bootstrap và Giao Diện Chung

- **Task ID**: T04
- **Tên Task**: Layout Bootstrap và giao diện chung
- **Owner**: Gemini C (Layout, Catalog, Check-in UI & E2E Lead)
- **Đợt**: 1
- **Chờ (Dependencies)**: T01 (**VERIFIED** tại [`AgentReport/WebMigration_T01.md`](file:///D:/FPT/PRN222/Project/Final_Project_Web/AgentReport/WebMigration_T01.md))
- **Status**: **VERIFIED** (Đã giải quyết trọn vẹn 2 vấn đề từ Codex review: chuẩn hóa múi giờ Việt Nam và thứ tự ưu tiên đa vai trò)
- **Contract Version**: v0 (Baseline & Architecture Freeze tại [`docs/WebMigration_Contracts.md`](file:///D:/FPT/PRN222/Project/Final_Project_Web/docs/WebMigration_Contracts.md))
- **Thời điểm hoàn thành**: 2026-10-15 17:35:00 +07:00
- **Môi trường**: Windows 11, .NET SDK 8.0.425 / 10.0.401, PowerShell 5.1

---

## 1. Mục Tiêu và Phạm Vi Hoàn Thành

1. **Xây dựng Layout Bootstrap 5.3.3 dùng chung cho cả MVC View và Razor Page**:
   - Thiết kế tệp `_Layout.cshtml` kế thừa phong cách học phần chuẩn của giảng viên (`TeacherCode/Account/Account/Forbidden.cshtml` và `TeacherCode/1.txt`), đồng thời hiện đại hóa theo Bootstrap 5.3.3.
   - Nhận diện thương hiệu chuẩn mực: `SelfStorage - <span class="text-danger fw-bold">PRN222</span> <span class="badge bg-secondary brand-badge">Web</span>`.
   - Cấu trúc trang co giãn với Flexbox (`min-height: 100vh`), chân trang cố định sạch sẽ (`app-footer`), không đè nội dung và thích ứng hoàn hảo giữa màn hình Desktop và Mobile (Navbar Toggler Collapse).
   - Chứng minh tính tương thích: Cả MVC View (`Views/Home/Index.cshtml`) và Razor Page (`Pages/LayoutTest.cshtml`) đều hiển thị đồng nhất qua cùng một layout `_Layout.cshtml`.

2. **Thanh điều hướng phân quyền theo Role Code thật (Navigation Menu) & Xử Lý Đa Vai Trò (Multi-Role)**:
   - Triển khai cây menu động đáp ứng 4 nhóm người dùng theo đúng hằng số vai trò tại `RoleConstants`:
     - **Khách công khai (Guest / Unauthenticated)**: Trang chủ (`/`), Danh mục kho (`/Catalog`), nút Đăng nhập (`/Account/Login`), nút Đăng ký (`/Account/Register`).
     - **Khách hàng (`storage_customer`)**: Đặt chỗ của tôi (`/Reservations`), Ô kho của tôi (`/MyUnits`), Menu tài chính & hợp đồng (Hóa đơn `/Invoices`, Lịch sử thanh toán `/Payments`, Hợp đồng thuê `/Agreements`), Trung tâm thông báo (`/Notifications`), Phù hiệu vai trò và Menu cá nhân.
     - **Nhân viên cơ sở (`facility_staff`)**: Quầy tiếp nhận (`/Staff/CheckIn`), Quản trị ô kho (`/StorageUnits`), Tra cứu hóa đơn (`/Invoices`), Tra cứu hợp đồng (`/Agreements`), Thông báo (`/Notifications`), Phù hiệu "Nhân viên cơ sở".
     - **Quản lý cơ sở (`facility_manager`)**: Toàn bộ nghiệp vụ nhân viên + Menu "Đối soát thanh toán & Hoàn tiền" (`/Payments`), Phù hiệu "Quản lý cơ sở".
     - **Quản trị viên & Vận hành (`system_administrator`, `business_operations_manager`)**: Quản trị ô kho (`/StorageUnits`), Quản lý tài khoản & phân quyền (`/Admin/Accounts` cho Admin), Toàn bộ menu quản trị hệ thống (`/Invoices`, `/Payments`, `/Agreements`, `/Staff/CheckIn`).
   - **Xử lý phù hiệu đa vai trò (Codex Review Fix)**: `_Layout.cshtml` không dùng `FindFirst(ClaimTypes.Role)` để tránh phụ thuộc vào thứ tự claims ngẫu nhiên. Thay vào đó, gọi `AppFormatters.GetPrimaryRole(User)` áp dụng thứ tự ưu tiên nghiêm ngặt:
     $$\text{system\_administrator} \to \text{business\_operations\_manager} \to \text{facility\_manager} \to \text{facility\_staff} \to \text{storage\_customer}$$
   - **Giữ nguyên quyền truy cập menu đa vai trò**: Sử dụng `User.IsInRole(...)` để quyết định hiển thị các mục menu, cho phép tài khoản có nhiều vai trò (ví dụ: vừa là Khách hàng vừa là Quản trị viên) truy cập trọn vẹn cả hai phân hệ.
   - Form Đăng xuất bảo mật CSRF: Thực hiện qua HTTP POST tới `/Account/Logout` có gắn kèm `@Html.AntiForgeryToken()`, tuyệt đối không dùng GET Logout làm thay đổi trạng thái xác thực.
   - **Nguyên tắc an toàn**: Toàn bộ việc ẩn/hiện menu chỉ phục vụ tối ưu trải nghiệm người dùng (UX), ranh giới bảo mật thực sự thuộc về server-side authorization attributes/policies.

3. **Bộ thành phần dùng chung (Shared Partials)**:
   - `_AlertMessages.cshtml`: Hiển thị thông báo nhanh dạng flash message từ `TempData` (hỗ trợ các khóa `SuccessMessage`, `Success`, `ErrorMessage`, `Error`, `WarningMessage`, `Warning`, `InfoMessage`, `Info`) với nút đóng dismissible và biểu tượng trực quan.
   - `_ValidationSummary.cshtml`: Hiển thị khối cảnh báo tổng hợp lỗi nhập liệu khi `ModelState.IsValid == false`.
   - `_ValidationScriptsPartial.cshtml`: Nạp thư viện kiểm tra hợp lệ phía client (`jquery.validate.min.js`, `jquery.validate.unobtrusive.min.js`) phục vụ cục bộ, đồng thời thiết lập bản địa hóa thông báo lỗi sang Tiếng Việt chuẩn.
   - `_LoadingState.cshtml`: Thành phần hiển thị trạng thái đang xử lý dạng inline cho từng bảng/khối dữ liệu và khung phủ toàn cục `#globalLoadingOverlay`.
   - `_EmptyState.cshtml`: Thành phần tái sử dụng hiển thị trạng thái danh sách rỗng kèm biểu tượng, thông điệp hướng dẫn và nút hành động chuyển hướng.

4. **Chuẩn hóa dữ liệu Tiếng Việt, Tiền tệ VND, Ngày tháng & Giờ hoạt động (Codex Review Fix)**:
   - Xây dựng lớp trợ năng C# `AppFormatters`:
     - `ResolveFacilityTimeZone(string?)`: Tự động tìm kiếm múi giờ đa nền tảng (chuyển đổi qua lại giữa IANA ID `Asia/Ho_Chi_Minh` và Windows ID `SE Asia Standard Time`, kiểm tra các candidate fallback, và cơ chế dự phòng tạo `TimeZoneInfo.CreateCustomTimeZone` UTC+7 không phụ thuộc OS).
     - `FormatVnd(decimal?)`: Định dạng tiền tệ VND chuẩn `1.500.000 ₫` (theo CultureInfo `vi-VN`).
     - `FormatDate(DateTimeOffset?)` / `FormatDate(DateTime?)`: Chuyển đổi mọi mốc thời gian về múi giờ Việt Nam trước khi xuất chuỗi `dd/MM/yyyy` (vd: `15/10/2026`). Null trả về `"—”`.
     - `FormatDateTime(DateTimeOffset?)` / `FormatDateTime(DateTime?)`: Chuyển đổi mốc thời gian UTC (hoặc mốc có offset khác) về múi giờ Việt Nam trước khi xuất chuỗi `dd/MM/yyyy HH:mm` (vd: UTC `2026-10-15T07:30:00Z` chuyển đổi chính xác thành `15/10/2026 14:30`). Null trả về `"—”`.
     - `FormatTime(TimeSpan?)`: Định dạng giờ mở/đóng cửa `HH:mm`. Null trả về `"—”`.
     - `GetPrimaryRole(...)`: Xác định vai trò hiển thị ưu tiên cao nhất theo chuẩn quy định.
     - `GetRoleDisplayName(roleCode)`: Tên hiển thị vai trò tiếng Việt thân thiện ("Khách hàng", "Nhân viên cơ sở", "Quản lý cơ sở", "Điều hành kinh doanh", "Quản trị hệ thống").
     - `GetRoleBadgeClass(roleCode)`: Lớp màu Bootstrap badge tương ứng từng vai trò.
   - Xây dựng thư viện JavaScript `window.SelfStorage` trong `site.js`:
     - `SelfStorage.formatVnd(amount)`: Định dạng tiền tệ VND phía client qua `Intl.NumberFormat`.
     - `SelfStorage.formatDate(dateInput)`: Định dạng ngày `dd/MM/yyyy` theo múi giờ `Asia/Ho_Chi_Minh` qua `Intl.DateTimeFormat`.
     - `SelfStorage.formatDateTime(dateInput)`: Định dạng ngày giờ `dd/MM/yyyy HH:mm` theo múi giờ `Asia/Ho_Chi_Minh` qua `Intl.DateTimeFormat`.
     - `SelfStorage.showLoading(message)` & `SelfStorage.hideLoading()`: Điều khiển loading overlay.
     - `SelfStorage.setButtonLoading(btn, text)` & `SelfStorage.resetButtonLoading(btn)`: Chống bấm gửi trùng lặp (double-submit protection).

5. **Đặt vị trí Script độc lập & Cách ly kết nối SignalR**:
   - `_Layout.cshtml` đặt thẻ `@await RenderSectionAsync("Scripts", required: false)` ở cuối trang.
   - Layout **tuyệt đối không khởi tạo kết nối SignalR toàn cục**. Thư viện `signalr.min.js` được nạp sẵn cục bộ nhưng việc kết nối Hub (`HubConnectionBuilder`) chỉ được thực hiện trong `@section Scripts` của những trang thực sự có nhu cầu realtime (StorageUnits ở T13, Reservations ở T10).

6. **Trang báo lỗi an toàn (Safe Error & NotFound Pages)**:
   - `Views/Home/Error.cshtml`: Xử lý ngoại lệ chưa bắt (500), chỉ hiển thị Mã sự cố (Request ID: `Activity.Current?.Id ?? HttpContext.TraceIdentifier`) và thông điệp an toàn. Tuyệt đối không để lộ stack trace, SQL exception, connection string hay thông tin cấu hình nội bộ.
   - `Views/Home/StatusCode.cshtml`: Xử lý các mã trạng thái HTTP 404 (Không Tìm Thấy Trang), 403 (Từ Chối Truy Cập), 401 (Yêu Cầu Đăng Nhập).
   - `Views/Account/Forbidden.cshtml`: Trang 403 chuyên biệt theo chuẩn TeacherCode, sửa triệt để lỗi typo `asp-asp-controller="Account"` của code mẫu giảng viên.

7. **Bàn giao quyền sở hữu Layout**:
   - Hoàn tất toàn bộ yêu cầu thiết kế ban đầu và xử lý các phản hồi từ Codex review.
   - Chính thức bàn giao quyền sửa đổi `_Layout.cshtml` và thanh navigation lại cho **Gemini A** (Foundation & Integration Lead).
   - Các feature sau (T07 Catalog, T09 StorageUnits CRUD, T10 Reservations, T11 Check-in, T12 Payments, T14 Accounts) thêm trang nghiệp vụ riêng biệt mà không cần sửa layout dùng chung.

---

## 2. Danh Sách Tập Tin Đã Tạo và Chỉnh Sửa

### 2.1. Tập tin tạo mới (New Files)
| STT | Đường dẫn tập tin | Mục đích & Trách nhiệm | Quyền sở hữu (Ownership) |
| :---: | :--- | :--- | :---: |
| 1 | `src/SelfStorageManagementSystem.Web/Helpers/AppFormatters.cs` | Trợ năng chuẩn hóa VND, ngày tháng (múi giờ VN đa nền tảng), và nhãn vai trò ưu tiên | Gemini C |
| 2 | `src/SelfStorageManagementSystem.Web/Models/EmptyStateViewModel.cs` | DTO mô hình dữ liệu cho thành phần Empty State | Gemini C |
| 3 | `src/SelfStorageManagementSystem.Web/Views/Shared/_AlertMessages.cshtml` | Partial hiển thị flash messages từ TempData | Gemini C |
| 4 | `src/SelfStorageManagementSystem.Web/Views/Shared/_ValidationSummary.cshtml` | Partial hiển thị tóm tắt lỗi dữ liệu đầu vào | Gemini C |
| 5 | `src/SelfStorageManagementSystem.Web/Views/Shared/_ValidationScriptsPartial.cshtml` | Partial nạp script validate client-side và bản địa hóa Tiếng Việt | Gemini C |
| 6 | `src/SelfStorageManagementSystem.Web/Views/Shared/_LoadingState.cshtml` | Partial hiển thị spinner và trạng thái đang xử lý | Gemini C |
| 7 | `src/SelfStorageManagementSystem.Web/Views/Shared/_EmptyState.cshtml` | Partial hiển thị giao diện rỗng khi chưa có dữ liệu | Gemini C |
| 8 | `src/SelfStorageManagementSystem.Web/Views/Account/Forbidden.cshtml` | View từ chối truy cập 403 theo mẫu TeacherCode (đã sửa bug typo) | Gemini C |
| 9 | `src/SelfStorageManagementSystem.Web/Pages/LayoutTest.cshtml.cs` | PageModel của Razor Page kiểm tra tương thích layout | Gemini C |
| 10 | `src/SelfStorageManagementSystem.Web/Pages/LayoutTest.cshtml` | Razor Page kiểm tra tương thích layout, formatters, validation, empty state | Gemini C |
| 11 | `tests/SelfStorageManagementSystem.WebTests/Fixtures/TestAuthHandler.cs` | Handler giả lập xác thực qua header `X-Test-Role` (hỗ trợ phân tách dấu phẩy cho đa vai trò) | Gemini C |
| 12 | `tests/SelfStorageManagementSystem.WebTests/LayoutAndSharedComponentsTests.cs` | Bộ 21 bài kiểm thử tự động xác minh toàn diện layout, shared components, múi giờ VN và đa vai trò | Gemini C |
| 13 | `AgentReport/WebMigration_T04.md` | Báo cáo hoàn thành và tài liệu bàn giao Task T04 | Gemini C |

### 2.2. Tập tin chỉnh sửa (Modified Files)
| STT | Đường dẫn tập tin | Nội dung chỉnh sửa | Ranh giới sở hữu |
| :---: | :--- | :--- | :---: |
| 1 | `src/SelfStorageManagementSystem.Web/Views/Shared/_Layout.cshtml` | Thiết kế lại layout chung, menu role code, gọi `AppFormatters.GetPrimaryRole(User)`, alert partial, footer TeacherCode | Gemini C (Ngoại lệ T04, bàn giao lại cho A) |
| 2 | `src/SelfStorageManagementSystem.Web/Views/Home/StatusCode.cshtml` | Bổ sung giao diện an toàn cho 404, 403, 401, giữ thông điệp trạng thái thân thiện | Gemini C |
| 3 | `src/SelfStorageManagementSystem.Web/Views/_ViewImports.cshtml` | Khai báo `Helpers` và `RoleConstants` cho MVC Views | Gemini C |
| 4 | `src/SelfStorageManagementSystem.Web/Pages/_ViewImports.cshtml` | Khai báo `Helpers` và `RoleConstants` cho Razor Pages | Gemini C |
| 5 | `src/SelfStorageManagementSystem.Web/wwwroot/css/site.css` | Nâng cấp CSS giao diện, sticky footer, loading overlay, validation styles | Gemini C |
| 6 | `src/SelfStorageManagementSystem.Web/wwwroot/js/site.js` | Nâng cấp `SelfStorage` format VND, formatDate/formatDateTime (múi giờ VN), loading, double-submit guard | Gemini C |
| 7 | `tests/SelfStorageManagementSystem.WebTests/Fixtures/WebTestFixture.cs` | Đăng ký `TestAuthHandler` trong `ConfigureTestServices` để hỗ trợ test role menu | Gemini C |
| 8 | `AgentReport/WebMigration_TaskBoard.md` | Cập nhật tiến độ T04 sang VERIFIED và ghi nhận bàn giao quyền sở hữu | Gemini C |

---

## 3. Chi Tiết Xử Lý Findings Từ Codex Review

### 3.1. Vấn Đề 1: Chuẩn Hóa Múi Giờ Cơ Sở (UTC sang Asia/Ho_Chi_Minh)
- **Vấn đề**: Các trường dữ liệu `created_at`, `updated_at` trong hệ thống sử dụng `DateTimeOffset.UtcNow`. Nếu format trực tiếp mà không chuyển đổi múi giờ, giờ hiển thị trên Web sẽ bị chậm 7 tiếng so với thực tế hoạt động tại Việt Nam.
- **Giải pháp triển khai trong `AppFormatters.cs`**:
  1. Xây dựng phương thức `ResolveFacilityTimeZone(string?)`:
     - Tự động chuyển đổi giữa định dạng IANA (chuẩn Linux/macOS/Docker, vd: `"Asia/Ho_Chi_Minh"`) và định dạng Windows (vd: `"SE Asia Standard Time"`) thông qua `TimeZoneInfo.TryConvertIanaIdToWindowsId` và `TimeZoneInfo.TryConvertWindowsIdToIanaId`.
     - Kiểm tra danh sách ứng viên phổ biến: `"SE Asia Standard Time"`, `"Asia/Ho_Chi_Minh"`, `"Asia/Saigon"`, `"Asia/Bangkok"`.
     - Cơ chế dự phòng tuyệt đối: Khởi tạo custom `TimeZoneInfo.CreateCustomTimeZone("Asia/Ho_Chi_Minh", TimeSpan.FromHours(7), ...)` đảm bảo không bao giờ phát sinh `TimeZoneNotFoundException` trên bất kỳ hệ điều hành nào.
  2. Cập nhật `FormatDateTime(DateTimeOffset?)` và `FormatDate(DateTimeOffset?)`:
     - Sử dụng `TimeZoneInfo.ConvertTime(dateTime.Value, tz)` để quy đổi về múi giờ cơ sở Việt Nam trước khi xuất chuỗi định dạng `dd/MM/yyyy HH:mm`.
     - Cung cấp overload cho `DateTime?`: Kiểm tra thuộc tính `.Kind == DateTimeKind.Utc` để chuyển đổi qua `ConvertTimeFromUtc`.
     - Dữ liệu `null` hiển thị `"—”` sạch sẽ.
  3. Cập nhật `site.js` phía client: Sử dụng `Intl.DateTimeFormat('en-GB', { timeZone: 'Asia/Ho_Chi_Minh', ... })` và `formatToParts` để format đồng nhất `dd/MM/yyyy HH:mm`.

### 3.2. Vấn Đề 2: Thứ Tự Ưu Tiên Phù Hiệu Đa Vai Trò (Multi-Role Priority)
- **Vấn đề**: `_Layout.cshtml` trước đây sử dụng `User.FindFirst(ClaimTypes.Role)`, dẫn đến việc người dùng có nhiều vai trò (ví dụ: vừa có role `storage_customer`, vừa có role `system_administrator`) sẽ bị hiển thị sai vai trò tùy thuộc vào thứ tự thêm claim.
- **Giải pháp triển khai**:
  1. Trong `AppFormatters.cs`, xây dựng `GetPrimaryRole(ClaimsPrincipal)` và `GetPrimaryRole(IEnumerable<string>)` áp dụng danh sách ưu tiên cố định:
     ```csharp
     public static readonly IReadOnlyList<string> RoleDisplayPriority = new[]
     {
         RoleConstants.SystemAdministrator,
         RoleConstants.BusinessOperationsManager,
         RoleConstants.FacilityManager,
         RoleConstants.FacilityStaff,
         RoleConstants.StorageCustomer
     };
     ```
  2. Bất kể thứ tự các claim trong danh tính người dùng (`[Customer, Admin]` hay `[Admin, Customer]`), phương thức luôn trả về `system_administrator`.
  3. Trong `_Layout.cshtml`, thay thế hoàn toàn biến `userRole` cũ bằng:
     ```razor
     var primaryRole = AppFormatters.GetPrimaryRole(User);
     ```
     Đồng thời hiển thị badge bằng `@AppFormatters.GetRoleBadgeClass(primaryRole)` và `@AppFormatters.GetRoleDisplayName(primaryRole)`.
  4. Quyền hiển thị menu: Giữ nguyên `User.IsInRole(role)` cho từng mục điều hướng, giúp tài khoản đa vai trò thấy đầy đủ các menu nghiệp vụ tương ứng mà không bị mất quyền.

---

## 4. Kết Quả Kiểm Thử (Test Verification)

### 4.1. Bộ Kiểm Thử Giao Diện và Thành Phần Dùng Chung (`LayoutAndSharedComponentsTests.cs`)
Tổng cộng: **21/21 bài kiểm thử đạt PASS 100%**:
1. `MvcView_RendersWithSharedLayout_AndTeacherCodeStyle`: MVC View hiển thị layout, thẻ meta, Bootstrap cục bộ, footer PRN222.
2. `RazorPage_RendersWithSharedLayout_AndComponents`: Razor Page dùng chung `_Layout`, kiểm tra VND format, validation script, empty state.
3. `GuestNavigation_RendersOnlyPublicLinks`: Khách vãng lai chỉ thấy Trang chủ, Danh mục kho, Đăng nhập, Đăng ký.
4. `CustomerNavigation_RendersCustomerSpecificLinks`: Khách hàng thấy Đặt chỗ, Ô kho, Hóa đơn, Thanh toán, Hợp đồng, Thông báo.
5. `StaffNavigation_RendersStaffSpecificLinks`: Nhân viên cơ sở thấy Quầy tiếp nhận, Quản trị ô kho, Nghiệp vụ cơ sở.
6. `ManagerNavigation_RendersManagerSpecificLinks`: Quản lý cơ sở thấy Quầy tiếp nhận, Quản trị ô kho, Đối soát thanh toán & Hoàn tiền.
7. `AdminNavigation_RendersAdministratorLinks`: Quản trị viên thấy Quản trị ô kho, Quản lý tài khoản (`/Admin/Accounts`), Phù hiệu Đỏ.
8. `BusinessOperationsManagerNavigation_RendersOperationsLinks`: Điều hành kinh doanh thấy menu quản trị hệ thống tương ứng.
9. `AppFormatters_StandardizesVietnameseFormats`: Kiểm tra định dạng tiền tệ VND (`1.500.000 ₫`), tên hiển thị vai trò và lớp màu badge.
10. `SafeErrorPages_NotFound_DoesNotLeakStackTraceOrSql`: Trang 404 hiển thị an toàn, không rò rỉ mã lỗi nội bộ hay chuỗi truy vấn.
11. `SafeErrorPages_InternalError_DoesNotLeakStackTraceOrSql`: Trang 500 chỉ hiển thị Request ID, không chứa stack trace hay connection string.
12. `SafeErrorPages_ForbiddenStatusCode_RendersSafely`: Trang 403 Forbidden hiển thị thông báo an toàn và dẫn về trang Đăng nhập.
13. `ForbiddenView_FileExistsAndIsConfigured`: Kiểm tra tệp `Views/Account/Forbidden.cshtml` tồn tại và đã sửa lỗi typo `asp-asp-controller`.
14. `Layout_DoesNotEstablishGlobalSignalRConnection`: Layout nạp file thư viện nhưng tuyệt đối không khởi tạo kết nối SignalR toàn cục.
15. `Navbar_AllLinksAreWellFormedAndMatchContractRoutes`: Tất cả thẻ liên kết trên thanh điều hướng đều hợp lệ và khớp contract route.
16. `AppFormatters_ConvertsUtcToVietnamTimezone_Correctly` (**Mới - Codex Review Fix**): Xác minh mốc UTC `2026-10-15T07:30:00Z` chuyển đổi chính xác thành `15/10/2026 14:30`, kiểm tra DateTime Kind Utc, mốc giờ có offset khác (+02:00), giá trị null và `FormatTime`.
17. `AppFormatters_GetPrimaryRole_FollowsPriorityOrder_RegardlessOfInputOrder` (**Mới - Codex Review Fix**): Xác minh logic ưu tiên đa vai trò hoạt động bất kể thứ tự mảng và trên `ClaimsPrincipal`.
18. `MultiRole_CustomerAndAdmin_ShowsAdminBadge_AndRetainsCustomerAndAdminMenus` (**Mới - Codex Review Fix**): Tài khoản có role Customer trước rồi đến Admin hiển thị badge Admin và giữ trọn vẹn cả hai cụm menu.
19. `MultiRole_AdminAndCustomer_OrderInverted_ShowsAdminBadge_AndRetainsCustomerAndAdminMenus` (**Mới - Codex Review Fix**): Đảo ngược thứ tự claim (Admin trước rồi đến Customer) vẫn đảm bảo hiển thị badge Admin.
20. `MultiRole_StaffAndManager_ShowsManagerBadge` (**Mới - Codex Review Fix**): Tài khoản có role Staff và Manager hiển thị badge Quản lý cơ sở và link Đối soát thanh toán.
21. `MultiRole_ManagerAndStaff_OrderInverted_ShowsManagerBadge` (**Mới - Codex Review Fix**): Đảo ngược thứ tự claim (Manager trước rồi đến Staff) vẫn đảm bảo hiển thị badge Quản lý cơ sở.

### 4.2. Toàn Bộ Kiểm Thử Dự Án Web (`SelfStorageManagementSystem.WebTests`)
- Lệnh thực thi: `dotnet test tests/SelfStorageManagementSystem.WebTests/SelfStorageManagementSystem.WebTests.csproj`
- Kết quả: **Passed! - Failed: 0, Passed: 48, Skipped: 0, Total: 48, Duration: 3 s**
  - `LandingPageAndBootstrapTests`: 14/14 PASS
  - `AccountIdentityAndZeroJwtTests`: 5/5 PASS
  - `LayoutAndSharedComponentsTests`: 21/21 PASS
  - Các bài kiểm thử Web host cơ bản khác: 8/8 PASS

### 4.3. Kiểm Thử Hồi Quy Toàn Hệ Thống (`SelfStorageManagementSystem.sln`)
- Lệnh thực thi: `dotnet test SelfStorageManagementSystem.sln --filter "FullyQualifiedName!~SqlServerConcurrencyTests"`
- Kết quả: **Passed! - Failed: 0, Passed: 217, Skipped: 0, Total: 217, Duration: 4 s**
- Bảo đảm 100% không gây ảnh hưởng hay xung đột với các thành phần nghiệp vụ và kiểm thử đã có từ trước.

---

## 5. Hướng Dẫn Tích Hợp Cho Các Feature Tiếp Theo

1. **Sử Dụng Thành Phần Partial Dùng Chung**:
   - **Thông báo nhanh (Flash Messages)**:
     ```csharp
     TempData["SuccessMessage"] = "Thao tác cập nhật ô kho thành công.";
     // hoặc TempData["ErrorMessage"] = "Có lỗi xảy ra khi lưu thông tin.";
     ```
   - **Tóm tắt lỗi nhập liệu (Validation Summary)**:
     ```html
     <partial name="_ValidationSummary" />
     ```
   - **Kiểm tra hợp lệ phía client (Client-side validation)**:
     ```html
     @section Scripts {
         <partial name="_ValidationScriptsPartial" />
     }
     ```
   - **Trạng thái danh sách rỗng (Empty State)**:
     ```html
     <partial name="_EmptyState" model='new EmptyStateViewModel {
         Title = "Chưa có hợp đồng nào",
         Description = "Quý khách chưa đăng ký hợp đồng thuê kho nào tại hệ thống.",
         ActionText = "Khám phá danh mục kho",
         ActionUrl = "/Catalog"
     }' />
     ```

2. **Cách Ly Kết Nối SignalR Theo Từng Trang (Không Kết Nối Toàn Cục)**:
   Tại các trang cần realtime (như T09/T13 Quản trị ô kho, T10 Đặt chỗ), chỉ khởi tạo kết nối trong `@section Scripts`:
   ```javascript
   const connection = new signalR.HubConnectionBuilder()
       .withUrl("/storageUnitHub")
       .withAutomaticReconnect()
       .build();

   connection.on("StorageUnitStatusChanged", function (unitId, status) {
       // Cập nhật giao diện cục bộ của trang
   });

   connection.start().catch(err => console.error(err.toString()));
   ```

3. **Chống Gửi Đúp Form (Double-Submit Protection)**:
   Thêm thuộc tính `data-prevent-double-submit="true"` vào thẻ `<form>`:
   ```html
   <form method="post" data-prevent-double-submit="true">
       <button type="submit" class="btn btn-primary">Lưu thông tin</button>
   </form>
   ```

---

## 6. Bàn Giao Quyền Sở Hữu Layout Cho Gemini A

Theo đúng quy định tại Kế hoạch Chuyển Đổi Web (Mục 3 và Mục 4.T04):
> *"T04 là ngoại lệ có kiểm soát để Gemini C tạo layout ban đầu, rồi chuyển ownership về Gemini A. Các feature sau gửi yêu cầu thêm menu qua báo cáo."*

- **Bàn giao chính thức**: Quyền sửa đổi `_Layout.cshtml`, các tệp shared view imports, và thanh điều hướng chính thức được chuyển giao lại cho **Gemini A** (Foundation & Integration Lead).
- **Yêu cầu bổ sung menu trong tương lai**: Nếu bất kỳ tính năng nào sau T04 phát sinh nhu cầu thay đổi cấu trúc thanh điều hướng chính hoặc bổ sung liên kết menu mới, agent phụ trách tính năng đó sẽ gửi đề nghị bằng văn bản trong báo cáo task của mình để Gemini A tích hợp tại điểm kiểm soát đợt (T08 / T15).

---

## 7. Kết Luận Nghiệm Thu (Acceptance Conclusion)

Task **T04: Layout Bootstrap và giao diện chung** đã giải quyết triệt để 100% các tiêu chí và phản hồi kiểm thử của Codex review:
1. Giao diện Bootstrap 5.3.3 dùng chung tương thích hoàn hảo cho cả MVC View và Razor Page, bảo tồn trọn vẹn phong cách học phần môn PRN222 của giảng viên.
2. Thanh navigation phân quyền đầy đủ cho 4 nhóm đối tượng theo đúng role code thật.
3. Hỗ trợ hiển thị phù hiệu đa vai trò chuẩn xác theo thứ tự ưu tiên nghiêm ngặt, độc lập với thứ tự claim đầu vào, đồng thời giữ nguyên quyền truy cập menu.
4. Múi giờ Việt Nam (`Asia/Ho_Chi_Minh` UTC+7) được chuyển đổi tự động, an toàn và đa nền tảng cho cả `DateTimeOffset` và `DateTime`, loại bỏ hiện tượng lệch 7 giờ.
5. Bộ partial components và chuẩn hóa định dạng Tiếng Việt / VND / Ngày giờ hoạt động ổn định.
6. Cơ chế xử lý lỗi an toàn tuyệt đối, không rò rỉ stack trace hay thông tin nhạy cảm.
7. 21/21 bài kiểm thử giao diện đạt 100%, 48/48 bài kiểm thử Web host đạt 100%, 217/217 bài kiểm thử hồi quy hệ thống cũ đạt 100%.

**Trạng thái chính thức: VERIFIED**. Sẵn sàng làm đầu vào cho các task tiếp theo trong Đợt 2 (T05, T07).
