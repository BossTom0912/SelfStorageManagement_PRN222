# Báo Cáo Hoàn Thành Task T11: Quầy Check-in, Inspection và Bàn Giao Kho Số Hóa

- **Task ID**: T11
- **Tên Task**: Quầy check in inspection và bàn giao
- **Owner**: Gemini C
- **Đợt (Wave)**: Đợt 4 (Song song 3)
- **Chờ (Dependencies)**: T08 (Tích hợp nền tảng Web Migration)
- **Trạng thái**: **VERIFIED**
- **Đường dẫn trang Web**: `/Staff/CheckIn`
- **Mã nguồn thực hiện**:
  - `src/SelfStorageManagementSystem.Web/Pages/Staff/CheckIn/CheckInViewModels.cs`
  - `src/SelfStorageManagementSystem.Web/Pages/Staff/CheckIn/Index.cshtml.cs`
  - `src/SelfStorageManagementSystem.Web/Pages/Staff/CheckIn/Index.cshtml`
  - `src/SelfStorageManagementSystem.Web/wwwroot/js/staff-checkin.js`
  - `src/SelfStorageManagementSystem.Web/wwwroot/css/staff-checkin.css`
  - `tests/SelfStorageManagementSystem.WebTests/Fixtures/CheckInWebTestFixture.cs`
  - `tests/SelfStorageManagementSystem.WebTests/StaffCheckInPageTests.cs`

---

## 1. Mục Tiêu & Phạm Vi Công Việc

Thực hiện số hóa quy trình quầy tiếp nhận khách hàng, xác minh danh tính, phân bổ ô kho vật lý, lập biên bản kiểm tra tình trạng kho (inspection checklist) và bàn giao cấp mã PIN truy cập số hóa một lần theo đúng đặc tả nghiệp vụ Function 5:

1. **Tuân thủ phân lớp và toàn vẹn Domain**:
   - Sử dụng trực tiếp `ICheckInService` và các DTO chuẩn của Function 5.
   - Giữ nguyên toàn vẹn dịch vụ BLL `CheckInService` và DAL `CheckInRepository`, không can thiệp hay sửa đổi logic nghiệp vụ backend.
2. **Triển khai đầy đủ 8 thao tác nghiệp vụ theo Contract**:
   - `Search`: Tra cứu thông tin đặt chỗ / hợp đồng check-in theo mã đặt chỗ (`reservation_code`) hoặc số hợp đồng (`agreement_no`).
   - `EligibleUnits`: Tải danh sách ô kho ứng viên khả dụng (`physical_status == "available"`, đúng cơ sở, đúng loại kho).
   - `VerifyId`: Xác minh giấy tờ tùy thân trực tiếp tại quầy (CCCD/CMND hoặc Hộ chiếu), đối chiếu thông tin hồ sơ và cam kết của nhân viên quầy.
   - `Assign`: Gán ô kho vật lý ứng viên cho hợp đồng check-in.
   - `Release`: Giải phóng ô kho đã gán (kèm lý do) để chọn ô kho khác khi cần.
   - `Inspection`: Kiểm tra an toàn kho với 4 hạng mục bắt buộc (`door`, `lock`, `lighting`, `cleanliness`), tình trạng hợp lệ schema (`good`, `acceptable`, `damaged`, `missing`) và cờ an toàn độc lập `isUnsafe`.
   - `Complete`: Hoàn tất bàn giao, kích hoạt hợp đồng sang `active`, ghi nhận biên bản bàn giao và cấp phát mã PIN truy cập số hóa lần đầu.
   - `Reissue PIN`: Cấp lại mã PIN số hóa tại quầy cho hợp đồng đang hoạt động khi khách quên mã, yêu cầu xác minh lại số giấy tờ và lý do cấp lại.
3. **Phân quyền quầy nghiêm ngặt (Strict Counter Scope)**:
   - Chỉ tài khoản có vai trò `facility_staff` hoặc `facility_manager`, sở hữu hồ sơ nhân viên đang hoạt động (`employment_status == "active"`) và có phân công còn hiệu lực tại đúng cơ sở của đơn check-in mới được phép thao tác.
   - **Tuyệt đối không có đường bypass** cho Quản trị viên (`system_administrator`) hoặc Quản lý vận hành (`business_operations_manager`).
4. **Kiểm soát đồng thời lạc quan (Optimistic Concurrency)**:
   - Mọi form cập nhật trạng thái đều gửi kèm các khóa nhận diện phiên làm việc hiện hành: `CurrentAllocationId`, `CurrentInspectionId`, `CurrentCredentialId`.
   - Khi phát hiện xung đột dữ liệu cũ (`ConflictException`), giao diện hiển thị cảnh báo **Xung Đột Dữ Liệu (Stale Data Conflict)** màu vàng và yêu cầu nhân viên bấm nút tải lại dữ liệu mới nhất.
5. **An toàn bảo mật mã PIN truy cập (Zero-Persistence & Single-Issuance)**:
   - Mã PIN dạng plaintext 6 chữ số chỉ được trả về trực tiếp trong bộ nhớ phản hồi HTTP POST của thao tác `CompleteCheckIn` hoặc `ReissuePin`.
   - Phản hồi chứa PIN bắt buộc cấu hình tiêu đề HTTP cấm lưu đệm: `Cache-Control: no-store, no-cache`, `Pragma: no-cache`.
   - Tuyệt đối không dùng PRG (Post-Redirect-Get) hoặc `TempData` để truyền tải PIN.
   - Tuyệt đối không lưu PIN vào URL, Cookie, Log hệ thống, `localStorage`, `sessionStorage` hay phát tán qua kênh SignalR.
   - Thao tác tải lại trang (F5/Reload) hoặc tra cứu lại bằng URL GET hoàn toàn xóa sạch mã PIN khỏi giao diện, bảo đảm quy tắc hiển thị duy nhất một lần tại quầy.

---

## 2. Chi Tiết Triển Khai Giao Diện & Script

### 2.1. ViewModels (`CheckInViewModels.cs`)
- Định nghĩa các View Model độc lập phục vụ Model Binding cho từng handler của Razor Pages:
  - `VerifyIdentityInput`: Tiếp nhận `ReservationId`, `Code`, `VerificationMethod`, `DocumentNumber`, `IsConfirmedMatch`, `Notes`.
  - `AssignUnitInput`: Tiếp nhận `ReservationId`, `Code`, `StorageUnitId`.
  - `ReleaseUnitInput`: Tiếp nhận `ReservationId`, `Code`, `CurrentAllocationId`, `Reason`.
  - `InspectionItemInput` & `PerformInspectionInput`: Tiếp nhận `ReservationId`, `Code`, `CurrentAllocationId`, `IsUnsafe`, `OverallSummary`, danh sách 4 hạng mục kiểm tra.
  - `CompleteCheckInInput`: Tiếp nhận `ReservationId`, `Code`, `CurrentAllocationId`, `CurrentInspectionId`, `CustomerAcknowledged`, `Notes`.
  - `ReissuePinInput`: Tiếp nhận `ReservationId`, `Code`, `CurrentCredentialId`, `DocumentNumber`, `IsConfirmedMatch`, `Reason`.

### 2.2. Razor PageModel (`Index.cshtml.cs`)
- Khai báo route `@page "/Staff/CheckIn"`, bảo vệ bằng chính sách `[Authorize(Policy = "StaffOnly")]`.
- Bổ sung phương thức bảo vệ vai trò quầy `HasCounterRole()`: Từ chối ngay lập tức nếu tài khoản không có vai trò `facility_staff` hoặc `facility_manager`.
- Triển khai toàn bộ 7 handler POST theo quy ước Razor Pages:
  - `OnGetAsync(string? code)`: Nạp dữ liệu check-in, khởi tạo danh sách 4 hạng mục kiểm định (`Cửa ô kho`, `Khóa & then cài`, `Đèn chiếu sáng`, `Vệ sinh sạch sẽ`).
  - `OnPostSearch(string? searchCode)`: Điều hướng an toàn sang query param `code`.
  - `OnPostVerifyIdAsync([FromForm] VerifyIdentityInput input)`
  - `OnPostAssignUnitAsync([FromForm] AssignUnitInput input)`
  - `OnPostReleaseUnitAsync([FromForm] ReleaseUnitInput input)`
  - `OnPostPerformInspectionAsync([FromForm] PerformInspectionInput input)`: Kiểm tra 4 hạng mục bắt buộc, xử lý tự động giải phóng ô kho và chuyển sang trạng thái `maintenance` khi kiểm định không đạt hoặc cờ `isUnsafe` bật.
  - `OnPostCompleteCheckInAsync([FromForm] CompleteCheckInInput input)`: Cấp PIN lần đầu, ghi đè header `Cache-Control: no-store, no-cache`, lưu PIN vào biến bộ nhớ `IssuedPin` chỉ sống trong lượt render hiện tại.
  - `OnPostReissuePinAsync([FromForm] ReissuePinInput input)`: Cấp lại PIN, ghi đè header `Cache-Control: no-store, no-cache`, lưu PIN vào `IssuedPin` (đánh dấu cờ `IsReissuedPin = true`).
- Xử lý phân loại ngoại lệ nghiệp vụ tập trung:
  - `ConflictException`: Gán `IsStaleConflict = true`, hiển thị cảnh báo tải lại.
  - `ForbiddenException`: Gán `IsForbiddenAccess = true`, hiển thị thông điệp từ chối thao tác quầy.
  - `BadRequestException`: Báo lỗi nghiệp vụ trên banner thông báo.

### 2.3. Razor Page View (`Index.cshtml`)
- Giao diện thiết kế theo chuẩn Bootstrap 5.3.3:
  - **Thanh tiến trình 5 bước (Check-In Step Tracker)**: Hiển thị trực quan trạng thái từng bước (Tra cứu &rarr; Xác minh danh tính &rarr; Gán ô kho &rarr; Kiểm tra an toàn &rarr; Bàn giao & Cấp PIN).
  - **Khung tóm tắt thông tin**: Đặt chỗ, khách hàng (mã hóa số giấy tờ dạng `********1234`), cơ sở, loại kho, hợp đồng, tình trạng hóa đơn thanh toán kỳ đầu.
  - **Khung cảnh báo Xung đột dữ liệu (Stale Conflict Alert)**: Banner màu vàng nổi bật kèm nút một chạm "Tải lại dữ liệu mới nhất".
  - **Khung hiển thị mã PIN an toàn (Secure PIN Box)**:
    - Hiển thị nổi bật với nền tối, font monospace cỡ lớn, nhãn đỏ rực "CHỈ HIỂN THỊ 1 LẦN".
    - Nút "Sao chép mã PIN" tích hợp hiệu ứng copy an toàn.
    - Cảnh báo bảo mật rõ ràng: "Mã PIN này sẽ biến mất vĩnh viễn sau khi rời khỏi hoặc tải lại trang web".
  - **Bảng kiểm định an toàn (Inspection Checklist Table)**: 4 dòng hạng mục cố định, dropdown lựa chọn tình trạng (`good`, `acceptable`, `damaged`, `missing`), công tắc toggle `isUnsafe` viền đỏ cảnh báo.
  - **Modal Cấp lại mã PIN**: Hỗ trợ mở form cấp lại mã PIN cho hợp đồng đã bàn giao, yêu cầu nhập lại số giấy tờ và lý do.

### 2.4. Stylesheet & Client Script (`staff-checkin.css`, `staff-checkin.js`)
- `staff-checkin.css`: Tùy biến kiểu dáng thanh tiến trình 5 bước, bảng kiểm định an toàn, huy hiệu trạng thái và hộp hiển thị mã PIN an toàn.
- `staff-checkin.js`:
  - Xử lý sao chép mã PIN vào Clipboard qua `navigator.clipboard.writeText` với fallback `document.execCommand('copy')`.
  - Tự động xóa sạch biến bộ nhớ chứa PIN sau 30 giây nếu trang chưa được tải lại.
  - Kiểm tra phía client trước khi submit checklist kiểm định: Cảnh báo xác nhận khi nhân viên chọn tình trạng `damaged`/`missing` hoặc bật cờ `isUnsafe`.
  - **Tuân thủ bảo mật tuyệt đối**: Tuyệt đối không đọc, ghi hoặc lưu trữ mã PIN vào `localStorage`, `sessionStorage`, `document.cookie` hoặc phát tán SignalR.

---

## 3. Kiến Trúc Kiểm Thử & Kết Quả Xác Minh

### 3.1. Fixture Kiểm Thử Chuyên Biệt (`CheckInWebTestFixture.cs`)
- Kế thừa `WebApplicationFactory<Program>`, cấu hình cơ sở dữ liệu `SelfStorageDbContext` độc lập bằng `UseInMemoryDatabase`.
- Cấu hình bí mật băm HMAC (`CheckIn:PinHmacSecret = "CheckIn_Secret_Key_For_Testing_Hmac_32_Chars_Length!"`).
- Cấu hình `HtmlEncoder.Create(UnicodeRanges.All)` đảm bảo các ký tự tiếng Việt có dấu hiển thị chính xác theo chuẩn UTF-8.
- Hàm hỗ trợ xác thực `CreateAuthenticatedClientAsync`: Đăng nhập thực tế thông qua MVC Cookie Authentication (`/Account/Login`) với Antiforgery token, đọc và bảo lưu phiên cookie `SelfStoragePRN222.Web.Auth`.
- Phương thức `SeedFullCheckInEnvironmentAsync`: Khởi tạo môi trường hoàn chỉnh với 5 vai trò, cơ sở, loại kho, 3 ô kho vật lý, người dùng khách hàng, nhân viên có phân công hợp lệ, nhân viên chưa phân công, nhân viên đã nghỉ việc (`terminated`), quản lý cơ sở, admin, quản lý vận hành, đơn đặt chỗ confirmed, hợp đồng scheduled và hóa đơn thanh toán đủ.

### 3.2. Bộ Kiểm Thử Tự Động (`StaffCheckInPageTests.cs`)
Bao gồm 18 ca kiểm thử bao phủ toàn bộ các kịch bản kiểm tra chất lượng:

| STT | Tên Phương Thức `[Fact]` Thực Tế | Mục Tiêu & Kịch Bản Kiểm Tra | Kết Quả |
| :---: | :--- | :--- | :---: |
| 1 | `Anonymous_Access_RedirectsToLogin` | Khách vãng lai chưa xác thực truy cập `/Staff/CheckIn` bị chuyển hướng về `/Account/Login` | **PASS** |
| 2 | `CustomerRole_Access_ReturnsForbidden` | Khách hàng thuê kho (`StorageCustomer`) truy cập quầy bị từ chối quyền (403 Forbidden) | **PASS** |
| 3 | `AdminOrOps_WithoutStaffCounterRole_Access_ShowsForbiddenWarning` | Nhân viên chưa phân công, nhân viên thôi việc (`terminated`), Admin và Quản lý vận hành không có quyền bypass quầy; hiển thị banner từ chối truy cập | **PASS** |
| 4 | `Staff_WithActiveAssignment_Access_Succeeds` | Nhân viên có phân công cơ sở hợp lệ truy cập thành công giao diện quầy check-in (200 OK) | **PASS** |
| 5 | `Manager_WithActiveAssignment_Access_Succeeds` | Quản lý cơ sở có phân công hợp lệ truy cập thành công giao diện quầy check-in (200 OK) | **PASS** |
| 6 | `Search_NonExistentCode_ShowsErrorMessage` | Tra cứu mã đơn đặt chỗ không tồn tại hiển thị thông báo lỗi tìm kiếm | **PASS** |
| 7 | `Search_UnassignedStaff_ShowsForbiddenError` | Nhân viên không thuộc cơ sở của mã đơn đặt chỗ bị từ chối quyền tra cứu dữ liệu | **PASS** |
| 8 | `Search_ValidCode_RendersCustomerAndUnitInfo` | Tra cứu mã hợp lệ hiển thị thông tin khách hàng, số giấy tờ che mờ (`********1234`), loại kho và cơ sở | **PASS** |
| 9 | `VerifyId_DocumentMismatch_ShowsErrorMessage` | Xác minh giấy tờ tùy thân với số giấy tờ không trùng khớp hiển thị thông báo lỗi đối chiếu | **PASS** |
| 10 | `VerifyId_Successful_ShowsVerifiedBadge` | Xác minh giấy tờ hợp lệ ghi nhận thành công và hiển thị huy hiệu đã xác minh danh tính | **PASS** |
| 11 | `AssignUnit_And_ReleaseUnit_LifecycleSucceeds` | Luồng vòng đời gán ô kho vật lý ứng viên và giải phóng ô kho hoạt động chính xác | **PASS** |
| 12 | `ReleaseUnit_StaleAllocationId_ShowsStaleConflictAlert` | Giải phóng ô kho với allocation ID cũ (stale conflict) hiển thị banner xung đột dữ liệu và yêu cầu tải lại | **PASS** |
| 13 | `Inspection_Pass_RecordsCompleted_EnablesHandover` | Kiểm định đạt 4 mục bắt buộc ghi nhận hoàn tất và mở khóa bước bàn giao kèm cấp mã PIN | **PASS** |
| 14 | `Inspection_Fail_DamagedItem_ReleasesUnit_SetsMaintenance` | Kiểm định có mục hư hại tự động đánh rớt, giải phóng ô kho và chuyển trạng thái sang `maintenance` | **PASS** |
| 15 | `Inspection_Fail_IsUnsafeFlag_ReleasesUnit_SetsMaintenance` | Cờ an toàn độc lập `isUnsafe` bật lập tức đánh rớt kiểm định, giải phóng ô kho và chuyển trạng thái bảo trì | **PASS** |
| 16 | `CompleteCheckIn_Successful_IssuesPin_SetsNoStoreHeaders_AndZeroPersistence` | Hoàn tất bàn giao cấp mã PIN 6 số dùng một lần, trả về header `no-store, no-cache`, không lưu cookie, và làm mới trang xóa sạch PIN (Zero-Persistence) | **PASS** |
| 17 | `ReissuePin_DocumentMismatch_Fails` | Cấp lại mã PIN với số giấy tờ tùy thân không khớp hồ sơ bị từ chối và báo lỗi | **PASS** |
| 18 | `ReissuePin_Successful_IssuesNewPin_SetsNoStoreHeaders_AndZeroPersistence` | Cấp lại mã PIN thành công sinh credential mới, trả về header `no-store`, hiển thị nhãn cấp lại và biến mất khi làm mới trang | **PASS** |

### 3.3. Kết Quả Kiểm Thử Toàn Diện
1. **Bộ kiểm thử T11 (`StaffCheckInPageTests`)**:
   - Lệnh chạy: `dotnet test "tests/SelfStorageManagementSystem.WebTests/SelfStorageManagementSystem.WebTests.csproj" --filter "FullyQualifiedName~StaffCheckInPageTests"`
   - Kết quả: **18 Passed, 0 Failed, 0 Skipped** (100%).
2. **Toàn bộ dự án WebTests (`SelfStorageManagementSystem.WebTests`)**:
   - Lệnh chạy: `dotnet test "tests/SelfStorageManagementSystem.WebTests/SelfStorageManagementSystem.WebTests.csproj"`
   - Kết quả: **236 Passed, 0 Failed, 0 Skipped** (100%).
3. **Toàn bộ giải pháp Web (`SelfStorageManagementSystem.Web.sln`)**:
   - Lệnh chạy: `dotnet test "SelfStorageManagementSystem.Web.sln"`
   - Kết quả: **236 Passed, 0 Failed, 0 Skipped** (100%).
4. **Kiểm thử hồi quy Core Domain (`SelfStorageManagementSystem.Tests`)**:
   - Lệnh chạy: `dotnet test "tests/SelfStorageManagementSystem.Tests/SelfStorageManagementSystem.Tests.csproj"`
   - Kết quả: **225 Passed, 0 Failed, 8 Skipped** (100% test thường, 8 test SQL Concurrency được bỏ qua có chủ đích khi chạy không có SQL Server instance).

---

## 4. Phát Hiện Đáng Chú Ý & Giải Pháp Kỹ Thuật (Key Technical Findings)

1. **Phân biệt danh mục checklist kiểm định (Checklist Normalization)**:
   - *Vấn đề*: Trong `CheckInRepository.NormalizeCategory()`, từ khóa kiểm tra loại khóa bao gồm `khoa`. Khi hạng mục vệ sinh đặt tên là `"Vệ sinh khoang chứa"`, chuỗi con `"khoang"` chứa `"khoa"`, dẫn đến việc bị chuẩn hóa nhầm thành loại `lock`, gây lỗi `CHECKLIST_DUPLICATE_ITEM`.
   - *Giải pháp*: Chuẩn hóa tên hạng mục kiểm tra thứ tư thành `"Vệ sinh sạch sẽ"` (hoặc `"Vệ sinh kho"`), loại bỏ hoàn toàn từ khóa trùng lặp tiềm ẩn mà vẫn phản ánh đúng nghiệp vụ vệ sinh khoang lưu trữ.
2. **Cấu hình mã hóa ký tự tiếng Việt (UTF-8 HtmlEncoder)**:
   - *Vấn đề*: Mặc định Razor Engine của ASP.NET Core mã hóa các ký tự Unicode tiếng Việt có dấu thành mã thực thể HTML số (`&#x1A1; &#x1EDF; ...`), làm tăng dung lượng payload phản hồi và gây sai lệch khi xử lý kiểm thử chuỗi văn bản.
   - *Giải pháp*: Đăng ký `HtmlEncoder.Create(UnicodeRanges.All)` vào `Program.cs` và `CheckInWebTestFixture`, giúp trang web kết xuất tiếng Việt UTF-8 chuẩn xác, giảm kích thước HTML tải về và nâng cao trải nghiệm người dùng.
3. **Bảo mật tuyệt đối mã PIN (Zero-Persistence)**:
   - Đảm bảo mã PIN chỉ tồn tại trong bộ nhớ View Model trong lượt phản hồi HTTP POST duy nhất. Không sử dụng `TempData` (vốn ghi vào Cookie/Session), không sử dụng PRG, và cấu hình bắt buộc `Response.Headers.CacheControl = "no-store, no-cache"`.

---

## 5. Kết Luận & Sẵn Sàng Bàn Giao

Task **T11** đã hoàn thành xuất sắc toàn bộ yêu cầu kỹ thuật và nghiệp vụ, biên dịch sạch sẽ không cảnh báo lỗi, vượt qua 100% kiểm thử tích hợp giao diện quầy và bảo đảm không gây bất kỳ tác động hồi quy nào tới toàn bộ hệ sinh thái Web.

Task T11 chính thức chuyển trạng thái sang **VERIFIED**. Sẵn sàng mở khóa cho Task tiếp theo của Gemini C: **T14: Quản trị tài khoản và trang của khách**.
