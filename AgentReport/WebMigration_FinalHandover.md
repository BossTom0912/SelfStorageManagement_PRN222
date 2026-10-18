# WebMigration — Báo Cáo Bàn Giao Cuối Cùng (Final Gemini Handover Report)

- **Mã Task**: T19 (Đóng gói bàn giao cho Codex)
- **Đầu mối thực hiện**: Gemini A (Foundation & Integration Lead)
- **Đơn vị phối hợp**: Gemini B (Business Logic & Concurrency Lead), Gemini C (UI/UX & Client Integration Lead)
- **Reviewer độc lập**: Codex (Lead Independent Reviewer)
- **Không gian làm việc chính**: `D:\FPT\PRN222\Project\Final_Project_Web`
- **Không gian làm việc đồng bộ**: `D:\FPT\PRN222\Project\Final_Project`
- **Solution Web**: `SelfStorageManagementSystem.Web.sln`
- **Git Commit hiện hành trên `main`**: `821449030eebe1e38dc3bfda996f8bcb38474d10`
- **Trạng thái TaskBoard**: T19 **IMPLEMENTED** (Chờ Codex review độc lập); T20–T22 **WAITING**

---

## 1. Tổng Thể Tính Năng Web Đã Hoàn Thành và Bàn Giao (Delivered Web Functionality)

Toàn bộ giải pháp Web được di chuyển và tái cấu trúc hoàn toàn từ giải pháp Desktop (WPF/API) sang giải pháp Web thuần túy ASP.NET Core (.NET 8) sử dụng kiến trúc Clean Architecture kết hợp Razor Pages và MVC. Hệ thống bàn giao đáp ứng đầy đủ 5 phân hệ nghiệp vụ chính cùng các cơ chế bảo mật và thời gian thực:

### 1.1. Phân hệ Xác thực & Quản trị Phiên làm việc (Authentication & Session Security - T02, T05, T16)
- **Xác thực thuần Cookie (Zero JWT in Web)**: Sử dụng `CookieAuthenticationDefaults.AuthenticationScheme` cho toàn bộ giao diện Web, loại bỏ hoàn toàn JWT và secret token ra khỏi Web presentation.
- **Bảo mật Cookie tiêu chuẩn cao**: Cấu hình `HttpOnly = true`, `SameSite = SameSiteMode.Lax`, `SecurePolicy = CookieSecurePolicy.Always` khi chạy HTTPS, và thời gian sống trượt (sliding expiration) 5 phút.
- **Kiểm soát Principal theo thời gian thực (Live Revocation)**: Triển khai sự kiện `OnValidatePrincipal` qua `CookieAuthenticationEvents`. Hệ thống kiểm tra trực tiếp trạng thái tài khoản và phân quyền trên database ở mỗi request. Tài khoản bị vô hiệu hóa (`IsActive = false`) hoặc chấm dứt hợp đồng (`is_terminated = true`) lập tức bị thu hồi phiên và chuyển hướng về trang đăng nhập mà không có ngoại lệ (kể cả quản trị viên).
- **Hỗ trợ 5 vai trò phân quyền chuẩn**: Hệ thống triển khai 5 vai trò theo đúng định nghĩa trong `RoleConstants.cs`: `storage_customer`, `facility_staff`, `facility_manager`, `business_operations_manager`, và `system_administrator`; không sử dụng vai trò Guest. Phân quyền phân tầng nghiêm ngặt trên Controller, Razor Pages và View layout.
- **Hardening nguồn gốc WebSocket/HTTP (RFC 6454)**: Triển khai `HubOriginSecurityExtensions.IsAllowedHubOrigin` bảo vệ SignalR Hub. So khớp chính xác bộ ba (Scheme, Host, Effective Port) với Kestrel host, loại bỏ lỗ hổng bypass từ loopback tự do, chặn đứng Cross-Site WebSocket Hijacking (CSWSH), đồng thời hỗ trợ client không mang header Origin (native clients/tools).

### 1.2. Phân hệ Quản trị Ô Kho & Sơ Đồ Mặt Bằng (Storage Units & Catalog - T03, T06, T07, T09)
- **Tách bạch tầng dữ liệu và nghiệp vụ**: Xây dựng `IStorageUnitAdminRepository` và `IStorageUnitAdminService` bảo đảm tuân thủ Clean Architecture; Web Presentation hoàn toàn không phụ thuộc trực tiếp vào `DbContext` hay `DataAccess`.
- **Razor Pages CRUD hoàn chỉnh (`/StorageUnits`)**: Các trang `Index`, `Create`, `Edit`, `Details`, `Delete` được trang bị Anti-Forgery Token, xác thực mô hình dữ liệu (Model Validation) và cơ chế chống submit lặp (double-submit protection).
- **Kiểm soát tương tranh lạc quan (Optimistic Concurrency)**: Sử dụng trường `ExpectedUpdatedAt` với kiểu dữ liệu `DateTimeOffset` (trên cả request DTOs và page models) để phát hiện và ngăn chặn xung đột ghi đè dữ liệu khi nhiều quản trị viên cùng thao tác trên một ô kho.
- **Ràng buộc nghiệp vụ bất biến**: Khóa cứng cơ sở (`facility_id`) và loại kho (`unit_type_id`) khi sửa chữa; kiểm tra trùng mã ô kho (HTTP 409 Conflict); xử lý từ chối truy cập ngoài phạm vi (HTTP 403 Forbidden).
- **Catalog công khai & Sơ đồ SVG động (`/Catalog`)**: Cung cấp 4 thẻ nội dung (Loại kho & Bảng giá, Sơ đồ mặt bằng SVG, Danh sách ô kho ứng viên phân trang, Thông tin cơ sở). Sơ đồ mặt bằng SVG được tính toán viewBox và tọa độ động trực tiếp từ cơ sở dữ liệu thật, khử khuẩn XSS an toàn cho text/shapes, phân biệt rõ ràng giữa trạng thái giữ chỗ tạm thời (Capacity Hold) và trạng thái vật lý thực tế của ô kho theo chuẩn BR-RSV-03.

### 1.3. Phân hệ Đặt Chỗ, Đếm Ngược & Hết Hạn Tự Động (Reservations & Expiration - T10, T15)
- **Quy trình giữ chỗ 15 phút an toàn (`/Reservations`)**: Khách hàng tạo yêu cầu đặt chỗ với thời hạn giữ chỗ chính xác 15 phút (`hold_expires_at`).
- **Đồng hồ đếm ngược thời gian thực (`reservation-countdown.js`)**: Hiển thị đồng hồ đếm ngược trực quan, tự động đồng bộ theo múi giờ cơ sở (`Asia/Ho_Chi_Minh`), hỗ trợ tính toán chính xác ngày cuối tháng (kẹp ngày 31/01 -> 28/02 không lỗi).
- **Worker dọn dẹp nền tự phục hồi (`ReservationHoldExpirationWorker`)**: Chạy ngầm định kỳ 30 giây trong phạm vi DI scoped; tự động chuyển đổi các đơn giữ chỗ quá hạn sang trạng thái `expired`, giải phóng sức chứa ô kho, và kích hoạt `ReservationExpirationRealtimeHook` để phát thông báo thời gian thực đến người dùng và nhân viên.
- **Tự động phục hồi khi mất kết nối**: Lắng nghe sự kiện `app:realtime-reconnected` để lập tức tải lại dữ liệu chuẩn từ API `/Reservations/Details?handler=Status`, tránh sai lệch trạng thái sau khi mạng phục hồi.

### 1.4. Phân hệ Quầy Check-in, Bàn Giao & Quản Trị Tài Khoản (Check-in Counter & Accounts - T11, T14)
- **Quầy tiếp tân và bàn giao ô kho (`/Staff/CheckIn`)**: Hỗ trợ nhân viên thực hiện đầy đủ 8 nghiệp vụ tại quầy: tra cứu mã giữ chỗ, kiểm tra danh tính khách hàng, xác thực danh mục kiểm tra 4 bước (Checklist Inspection), ký biên bản bàn giao điện tử, phát sinh mã PIN mở cửa, và kích hoạt hợp đồng thuê.
- **Bảo mật mã PIN quầy (Zero-persistence PIN)**: Mã PIN được mã hóa/bảo mật theo chuẩn một chiều, tuyệt đối không lưu trữ bản rõ (plain text) trong cơ sở dữ liệu.
- **Quản trị người dùng & Ngăn chặn cô lập hệ thống (`/Admin/Accounts`)**: Cung cấp giao diện quản lý tài khoản cho Admin (danh sách, tạo mới, chi tiết, khóa/mở khóa, đổi vai trò). Tích hợp logic bảo vệ cốt lõi: ngăn chặn khóa hoặc tước quyền của Quản trị viên kích hoạt cuối cùng trong hệ thống (`Cannot lock or demote the last active Administrator`).
- **Trung tâm khách hàng (`/MyUnits` & `/Notifications`)**: Khách hàng theo dõi danh sách ô kho đang thuê, thông tin thẻ ra vào, chi tiết biên bản bàn giao và nhận thông báo cá nhân hóa theo thời gian thực.

### 1.5. Phân hệ Thanh Toán, Hóa Đơn, Hợp Đồng & Hoàn Tiền (Payments, Invoices & Refunds - T12, T15, T17)
- **Tích hợp cổng thanh toán VNPAY an toàn**:
  - Mã hóa và ký số HMAC-SHA512 cho toàn bộ giao dịch thanh toán.
  - VNPAY IPN (`/api/payments/vnpay/ipn`) là **nguồn sự thật tài chính duy nhất (Single Source of Financial Truth)**: endpoint xử lý callback máy chủ - máy chủ từ cổng thanh toán, kiểm tra chữ ký số HMAC-SHA512, cập nhật hóa đơn, kích hoạt hợp đồng và ghi nhận trạng thái thanh toán.
  - Điểm tiếp nhận phản hồi trình duyệt (`/api/payments/vnpay/return`) và trang kết quả (`/Payments/Result`) hoạt động ở chế độ **hiển thị thuần túy (Display-Only)**: tiếp nhận redirect từ VNPAY, kiểm tra mã phản hồi để hiển thị giao diện kết quả cho người dùng, tuyệt đối không thay đổi trạng thái tài chính hay cơ sở dữ liệu.
- **Môi trường giả lập Web Demo thuần túy (`/Payments/DemoSimulator`)**: Cho phép kiểm thử quy trình thanh toán thành công/thất bại hoàn toàn trên nền Web mà không cần phần mềm Desktop/WPF bên ngoài. Cấu hình bảo vệ chặt chẽ: trả về mã lỗi HTTP 403 Forbidden nếu được gọi trong môi trường Production.
- **Quy trình xử lý hoàn tiền (Refund Workflow)**: Cho phép nhân viên và quản lý rà soát yêu cầu hoàn tiền, phê duyệt/từ chối theo phạm vi phân quyền, và phát thông báo trạng thái tức thì.

### 1.6. Hạ Tầng Thời Gian Thực & Đồng Bộ Dữ Liệu Hai Chiều (SignalR Subsystem - T13, T15, T18)
- **SignalR Hub trung tâm (`/storageUnitHub`)**: Thiết lập kênh giao tiếp hai chiều giữa máy chủ Kestrel và trình duyệt client với định tuyến xác thực `[Authorize]`.
- **Bảo toàn hợp đồng Hub và Receiver chuẩn (T12–T15 Contract)**:
  - Phương thức Hub (Client gọi Server): `HubCreate_StorageUnit(string dataObj)`, `HubUpdate_StorageUnit(string dataObj)`, `HubDelete_StorageUnit(string id, string? expectedUpdatedAt = null)`.
  - Phương thức Receiver (Server đẩy xuống Client): `Receiver_CreateStorageUnit(StorageUnitRealtimeEventDto data)`, `Receiver_UpdateStorageUnit(StorageUnitRealtimeEventDto data)`, `Receiver_DeleteStorageUnit(string unitId)`.
- **Đồng bộ sự kiện nghiệp vụ mở rộng**: Phát sóng tức thì các biến động dữ liệu: `Receive_ReservationChanged`, `Receive_PaymentChanged`, `Receive_CheckInStatusChanged`, `Receive_AccountStatusChanged`.
- **Theo dõi và thu hồi kết nối động**: Sử dụng `StorageUnitHubConnectionTracker` ghi nhận danh sách kết nối theo người dùng và cơ sở, hỗ trợ thu hồi tức thì kết nối khi quyền hạn bị thay đổi.

### 1.7. Kiểm Soát Tương Tranh, Giao Dịch Ngân Hàng & Khóa Hai Cấp (Concurrency & Distributed Locking - T15, T17)
- **Cơ chế khóa phân tán hai cấp đối xứng**: Kết hợp `sp_getapplock` trên SQL Server thật và `SemaphoreSlim` trên bộ nhớ ứng dụng (In-Memory). Khóa đối xứng theo thứ tự chuẩn: khóa giữ chỗ `SS_Reservation_{reservationId}` trước, tiếp theo khóa thanh toán `SS_Payment_{paymentId}`.
- **Xử lý cuộc đua ngược (Reverse Race Resolution)**: Khi thông báo IPN thành công đến sau khi giao dịch đã bị ghi nhận thất bại (`payment_failed`), hệ thống chuyển payment sang `succeeded` kèm cờ `reconciliation_required = true` (`PAYMENT_ALREADY_FAILED`), tự động tạo bản ghi hoàn tiền `refund` cho nhân viên đối soát, tuyệt đối không tạo hợp đồng thuê và không xác nhận giữ chỗ sai lệch.
- **Tính lũy thừa tuyệt đối (Idempotency)**: Các thông báo IPN trùng lặp gửi đồng thời hoặc gửi lại nhiều lần được phát hiện chính xác, trả mã phản hồi chuẩn (`02` cho giao dịch đã xử lý, `00` cho giao dịch từ chối lặp lại), phát đúng 0 sự kiện thừa và tạo 0 bản ghi trùng lặp.
- **Bảo đảm giải phóng tài nguyên**: Cơ chế `try/finally` chặt chẽ quản lý biến cờ trạng thái khóa, giải phóng an toàn semaphore giữ chỗ nếu việc chờ khóa thanh toán bị hủy ngang qua `CancellationToken`, ngăn ngừa rò rỉ semaphore vĩnh viễn và triệt tiêu lỗi `SemaphoreFullException`.

---

## 2. Bảng Đối Chiếu Báo Cáo, Review Độc Lập và Evidence T16–T18 (Reports, Reviews & Hashes)

Tất cả các tài liệu báo cáo nghiệm thu vòng cuối, review độc lập từ Codex và bộ bằng chứng (Evidence) của các task T16, T17, T18 đã được kiểm tra, xác thực mã băm SHA-256 và đồng bộ tuyệt đối giữa hai thư mục dự án `Final_Project_Web` và `Final_Project`.

| Task | Vai trò | Báo cáo hoàn tất nghiệm thu | Mã băm SHA-256 Báo cáo | Báo cáo Review độc lập từ Codex | Mã băm SHA-256 Review | Ghi nhớ Task (Memory) | Mã băm SHA-256 Memory | Kết quả nghiệm thu độc lập |
| :---: | :---: | :--- | :--- | :--- | :--- | :--- | :--- | :--- |
| **T16** | Gemini A | `AgentReport/WebMigration_T16_R4_FindingFixes.md` | `0403BF1D5F8F09EF87C3F602B4C6390C3A15A40E7CD7407F58B0E2169A3FF841` | `AgentReport/WebMigration_T16_R4_CodexReview.md` | `9289F762F1E71BC16E3F7D5D032BF2D311CD944E862D5D4D03383BD6A290FE34` | `AgentReport/WebMigration_T16_Memory.md` | `2A7908DB5D1080DFAEEAAF5A1E65740FC40CC06FBBD5BFCFFB8261A92D06CF16` | **VERIFIED** (No findings; 39/39 live HTTP security tests PASS) |
| **T17** | Gemini B | `AgentReport/WebMigration_T17_R4_FindingFixes.md` | `F1FA57FEBF2011EBC35525642ACC4713F23358A993F618D1020A3F3B23A124C2` | `AgentReport/WebMigration_T17_R4_CodexReview.md` | `1B8DA4191D1C712FEB7E2EDBE951EE6D50661EEB37764D5DB220135EDE891DBC` | `AgentReport/WebMigration_T17_Memory.md` | `1272E8CFCC1BC81077CF41B72F3BD3E7A8D16712BDA1271C0C3BB17BF5716E7A` | **VERIFIED** (No findings; 19/19 SQL concurrency tests PASS; 6 triggers `is_disabled=0`) |
| **T18** | Gemini C | `AgentReport/WebMigration_T18_R6_FindingFixes.md` | `18A2AA0D0462483FB3CE923D2A194E76279645EA701959BB2FE4077AE7BE7C29` | `AgentReport/WebMigration_T18_R6_CodexReview.md` | `631E6D7B0EE9EB60A49A20DB8A0E191F0EC46D4F8EC453D3AACA81A6D5297B75` | `AgentReport/WebMigration_T18_Memory.md` | `EC67A1FEBA4E87E303A15457D4D4BC00B1E1E38CFDE7472F18A5B003410FC3C8` | **VERIFIED** (No findings; 7/7 Edge CDP tests PASS; 449/449 Web solution tests PASS) |

### 2.1. Tài liệu điều phối và quy tắc ghi nhớ (Batch Handoff & Rules)
- **Biên bản bàn giao đợt T16–T18**: `AgentReport/WebMigration_T16_T18_HandoffMemory.md`  
  - SHA-256: `BCF2B78B25B9E96169179DF6E9F16EDAAC3DDEE0479097986589B8C5EF1B119F`
- **Quy chuẩn ghi nhớ task (Memory Guide)**: `AgentReport/WebMigration_TaskMemoryGuide.md`  
  - SHA-256: `565F7E221ABF3EABCAAAA21830F1A8B422B95F6E3DE83AB4E97E26D02A66D42A`

### 2.2. Danh mục 24 ảnh bằng chứng T18 (Evidence PNGs in `AgentReport/Evidence/T18/`)
Toàn bộ 24 ảnh chụp màn hình tự động từ bộ kiểm thử E2E hai trình duyệt Edge (`T18TwoBrowserRealtimeE2ETests`) đã được khử khuẩn (sanitized), lưu trữ tại thư mục `AgentReport/Evidence/T18/` và khớp 100% mã băm SHA-256 giữa `Final_Project_Web`, `Final_Project` cũng như bảng kê khai trong báo cáo R6:

| STT | Tên tệp ảnh Evidence | Kích thước | Mã băm SHA-256 |
| :---: | :--- | :---: | :--- |
| 1 | `T18_01_Customer_Catalog_Initial_Context2_Customer.png` | 99,093 B | `042C43194480C1361A8A5EAAF17D457E450EE294F889D610F4EC78C5CB7849FF` |
| 2 | `T18_01_Staff_StorageUnits_Initial_Context1_Staff.png` | 92,766 B | `6D0E2FDD1AE1ED32DD1A1A13CCF05145ABA076215C8ACA6582936C8146D4EA29` |
| 3 | `T18_01_Staff_Unit_Created_Context1_Staff.png` | 92,903 B | `40DF545CEFC0C3209F5E23645D3B213DE4FD7D202F63C281541ED95A797792DF` |
| 4 | `T18_01_Staff_Unit_Deleted_Context1_Staff.png` | 82,376 B | `7BF33F789CFDF0D0918719D033FEFE4BCFED3F3C528270AADF5F24A41D1FE14A` |
| 5 | `T18_01_Staff_Unit_Updated_Context1_Staff.png` | 88,098 B | `26DC50A399DAD51897D8FD435B77F5299FBB4C0A6C9F40A889ECE474676500A3` |
| 6 | `T18_02_Customer_Reservation_Cancelled_Context2_Customer.png` | 63,556 B | `119310A1DE821FD5F92CA2EC08258949C2F552A9A82B78F30251D8DC2E489128` |
| 7 | `T18_02_Customer_Reservation_Ticking_Context2_Customer.png` | 79,051 B | `94AD9E461F08DE6C1060928C827C32FC1D05EC91E69D2C3CFFA2551333ACDC89` |
| 8 | `T18_02_Staff_Reservation_Cancelled_View_Context1_Staff.png` | 77,525 B | `B2F5268E825FA9F8A40453C8D46C1D0E3508C765CCE50ED341B7E756B47DF1D1` |
| 9 | `T18_02_Staff_Reservation_Hold_View_Context1_Staff.png` | 78,198 B | `5C192B8E6EF4102938FC8E0DD3E21F51F9DDF7257332FFFD11B4527CFBB24BBB` |
| 10 | `T18_03_Customer_Checkout_Page_Context2_Customer.png` | 110,351 B | `C3061BB3DB99D8314886EED00D05A28F2E1DD6816EFC25EC14909214357C0264` |
| 11 | `T18_03_Customer_Demo_Simulator_Context2_Customer.png` | 62,797 B | `F505AB2DC731254341625B612D29B7AD844F3FB860C2D766C83B6AD00A01E533` |
| 12 | `T18_03_Customer_Invoices_Paid_Context2_Customer.png` | 88,980 B | `C4DD68F51CBE682574E59AC2AC37B314E53E32A5E3E069BD9395F6BE663CA1C0` |
| 13 | `T18_03_Customer_Payment_Result_Context2_Customer.png` | 66,464 B | `74A6188AB802463747671B8295AE1930F6CC406764AC227F53E88F024B211934` |
| 14 | `T18_03_Staff_Payments_Overview_Context1_Staff.png` | 70,481 B | `DCC9AA85679283FE2036CF024D0E2D20D711937DB77FB97BD0FE609ABEFF8237` |
| 15 | `T18_03_Staff_Refund_Approved_View_Context1_Staff.png` | 72,302 B | `BBF18B63AAA8004EF266A783C4792B5E71E92DDEAD41D1C4D88D79856FC21CB4` |
| 16 | `T18_03_Staff_Refund_Requested_View_Context1_Staff.png` | 72,208 B | `A992F254EFE4E9983D1FDE64D926F5367449318FF426D457F268FA89038C66EF` |
| 17 | `T18_04_Customer_MyUnits_Active_Context2_Customer.png` | 106,399 B | `1EA3B32E4C18356F8A5EF56EE5C53F0522079250A464FBAA0ABFC9CFE3F8DD1B` |
| 18 | `T18_04_Customer_Notifications_Received_Context2_Customer.png` | 69,275 B | `FBAD940BB0ABEB773561F66E0EA36F1D223DA19FBD5F43480A1C351462FF6DDF` |
| 19 | `T18_04_Staff_CheckIn_Completed_Sanitized_Context1_Staff.png` | 75,834 B | `820AD0D3DA22B0E44ACE1917229BDA4A96F71B1D09AF2860EAA84FD0B3FC69F7` |
| 20 | `T18_04_Staff_CheckIn_Dashboard_Context1_Staff.png` | 86,491 B | `F6375818C3E13E46028934800C75D6262D944F9821685AF9BE6C4D1BD476B0AD` |
| 21 | `T18_05_Admin_Accounts_List_Context1_Staff.png` | 98,643 B | `A8F8A2053274EBC5F593BFD6231FC130E824D9E9DB5E7555EB4384B83948E148` |
| 22 | `T18_05_Admin_Revocation_Rejected_Context1_Staff.png` | 44,640 B | `12B5EF7EF252E3C9130297217079B484B3BC17E0890646854DC8F3116C76A049` |
| 23 | `T18_05_Customer_Notifications_Context2_Customer.png` | 69,275 B | `FBAD940BB0ABEB773561F66E0EA36F1D223DA19FBD5F43480A1C351462FF6DDF` |
| 24 | `T18_06_Staff_Reconnected_Reconciled_Context1_Staff.png` | 97,600 B | `840A81957AE5C1ED3DE39D6AE5E27BEA8397F75C9A1164F3508496F1EF564730` |

---

## 3. Trạng Thái Git Hiện Hành và Các Tệp Loại Trừ (Git SHA & Exclusions)

### 3.1. Thông tin commit Git
- **Nhánh hiện hành**: `main`
- **Commit SHA hiện hành**: `821449030eebe1e38dc3bfda996f8bcb38474d10`
- **Thông điệp commit**: `Merge WebMigration T16-T18 verified batch`
- **Commit tính năng đã merge**: `156204ec5f4d74b8b61006e94c67541bd4d4f05b` (trên nhánh `codex/webmigration-t16-t18`)
- **Trạng thái remote**: Nhánh `main` đã được đẩy lên `origin/main` đầy đủ, sạch sẽ.
- **Quy tắc Git của đợt này**: Gemini A **không commit, không push và không merge**. Codex sẽ chịu trách nhiệm toàn bộ chu trình Git sau khi hoàn tất kiểm tra độc lập T20.

### 3.2. Ba tệp loại trừ được bảo tồn nguyên vẹn (Preexisting Excluded Files)
Theo quy định nghiêm ngặt từ handoff memory, 3 tệp sau đây phải tuyệt đối giữ nguyên trạng thái gốc và không được đưa vào bất kỳ commit nào:

| STT | Đường dẫn tệp | Trạng thái Git | Mã băm SHA-256 đối chiếu |
| :---: | :--- | :---: | :--- |
| 1 | `docs/KeHoach_Web_Gemini_Va_Codex_Review.docx` | Modified (Working tree) | `8809241D1C6A87611E930576F8D669105C7D5EE2EE009E22C330ECE5E68418A3` |
| 2 | `AgentReport/WebMigration_T15_CodexLoopState.json` | Untracked | `170B2B6AFA6F355245F000F0B768E01FF820B163205858A9CBD7CF69165B6E1C` |
| 3 | `scratch/generate_r3_report.js` | Untracked | `6EFE26FF4900690D2FB415463EB83259632C62B422D3AE872DA81A0CC92AB6E9` |

---

## 4. Kết Quả Kiểm Thử Toàn Diện và Giới Hạn Nghiệp Vụ (Test Results & Limits)

### 4.1. Tổng hợp kết quả kiểm thử (Test Matrix)
- **Toàn bộ Solution Web (`SelfStorageManagementSystem.Web.sln`)**: **449 Passed, 0 Failed, 0 Skipped**  
  *(Đã kiểm chứng độc lập bởi Codex; bao gồm bài test controlled double-failure; con số 449 là chuẩn chính thức).*
- **Kiểm thử E2E hai trình duyệt Edge (`T18TwoBrowserRealtimeE2ETests`)**: **7 Passed, 0 Failed, 0 Skipped**  
  Lệnh thực thi: `dotnet test tests/SelfStorageManagementSystem.WebTests/SelfStorageManagementSystem.WebTests.csproj --no-restore --filter "FullyQualifiedName~T18"`
- **Kiểm thử Bảo mật Live HTTP Kestrel Server (`SecurityHttpLiveServerTests` - T16)**: **39 Passed, 0 Failed, 0 Skipped**
- **Kiểm thử Hồi quy Web không cần trình duyệt (`WebTests Non-Browser`)**: **426 Passed, 0 Failed, 0 Skipped**
- **Kiểm thử Hồi quy Legacy Solution (`SelfStorageManagementSystem.sln` - 2 lần chạy tách biệt)**:
  - *Lần chạy mặc định (không cấu hình chuỗi kết nối SQL Server)*: `dotnet test SelfStorageManagementSystem.sln --no-restore`: **225 Passed, 0 Failed, 19 Skipped** (tổng 244 test).
  - *Lần chạy kiểm thử tương tranh SQL Server (với biến môi trường `SELFSTORAGE_SQL_TEST_CONNECTION_STRING` trỏ tới DB `SelfStoragePRN222_WebTest`)*: **19 Passed, 0 Failed, 0 Skipped** (chính là 19 bài test được kích hoạt kiểm tra tương tranh thực tế trên SQL Server).
- **Kiểm thử Cú pháp JavaScript Client**: **10/10 Passed** (`node --check` trên toàn bộ tệp `.js` trong `wwwroot/js`)
- **Kiểm thử Kiến trúc Clean Architecture**: **2/2 Passed** (Bảo đảm Web assembly hoàn toàn sạch, không tham chiếu DataAccess, Api hay WPF)

### 4.2. Trạng thái cơ sở dữ liệu và 6 Trigger cốt lõi sau kiểm thử
- **6 Trigger nghiệp vụ cốt lõi được kiểm chứng trên SQL Server**: Đều có `is_disabled = 0` (được truy vấn trực tiếp qua `sys.triggers` trong bài test `AssertAllCriticalTriggersEnabledAsync` trên cơ sở dữ liệu `SelfStoragePRN222_WebTest`):
  1. `trg_agreement_status_transition` (bảo vệ chuyển đổi trạng thái hợp đồng thuê)
  2. `trg_invoice_lines_guard` (bảo vệ tính toàn vẹn các dòng hóa đơn)
  3. `trg_invoice_lines_refresh_totals` (tự động cập nhật tổng tiền hóa đơn)
  4. `trg_invoice_status_transition` (kiểm soát chuyển đổi trạng thái hóa đơn)
  5. `trg_payment_allocation_guard` (bảo vệ phân bổ thanh toán, chặn đứng vi phạm với lỗi nghiệp vụ 51055)
  6. `trg_reservation_status_transition` (kiểm soát chuyển đổi trạng thái đặt chỗ)
- **Khử dọn Fixture rác**: 100% dữ liệu thử nghiệm trong các bài test T16, T17, T18 được dọn dẹp sạch sẽ qua các giao dịch transactional và khối teardown an toàn, không để lại ô kho, reservation hay payment mồ côi.

### 4.3. Giới hạn vận hành và Phạm vi kiểm thử (Limits & Constraints)
- **Môi trường cơ sở dữ liệu**: Các bài kiểm thử tương tranh SQL Server (T17, T18) yêu cầu instance SQL Server cục bộ hoạt động với tài khoản `sa` hoặc tương đương; các bài kiểm thử unit/service thông thường sử dụng EF Core InMemory provider.
- **Môi trường trình duyệt**: Kiểm thử E2E hai trình duyệt (T18) phụ thuộc vào trình duyệt Microsoft Edge cài đặt cục bộ và giao thức Chrome DevTools Protocol (CDP); môi trường CI headless thuần không có Edge sẽ bỏ qua bài test này.
- **Cổng thanh toán bên thứ ba**: Cổng VNPAY trong môi trường kiểm thử được vận hành qua giao diện web giả lập `DemoSimulator` và endpoint sandbox; các lệnh gọi thực tế đến hệ thống ngân hàng đối tác chỉ được kích hoạt trong môi trường Production chính thức.

---

## 5. Hợp Đồng Realtime Hub/Receiver T12–T15 (Authoritative Realtime Contract)

Hợp đồng kết nối và phương thức thời gian thực SignalR thiết lập từ đợt T12–T15 được giữ nguyên vẹn 100%, phản ánh chính xác mã nguồn triển khai thực tế:

### 5.1. Định tuyến và Bảo mật Hub
- **Đường dẫn Hub (Route)**: `/storageUnitHub`
- **Xác thực**: Bắt buộc gắn attribute `[Authorize]` trên lớp `StorageUnitHub`.
- **Bảo vệ Origin**: `HubOriginSecurityExtensions.IsAllowedHubOrigin` kiểm tra chính xác bộ ba (Scheme, Host, Port) theo chuẩn RFC 6454.

### 5.2. Phương thức Hub (Client gửi lên Server)
Theo đúng cài đặt trong `StorageUnitHub.cs`:
1. `HubCreate_StorageUnit(string dataObj)`: Nhận chuỗi JSON chứa request tạo ô kho (`CreateStorageUnitRequest`), deserialize và kiểm tra validation, thực thi qua BLL service và phát sự kiện `Receiver_CreateStorageUnit`.
2. `HubUpdate_StorageUnit(string dataObj)`: Nhận chuỗi JSON chứa request cập nhật (`StorageUnitHubUpdateRequest`) kèm mốc thời gian tương tranh `DateTimeOffset? ExpectedUpdatedAt`, thực thi BLL update và phát sự kiện `Receiver_UpdateStorageUnit`.
3. `HubDelete_StorageUnit(string id, string? expectedUpdatedAt = null)`: Nhận mã ô kho dạng chuỗi `id` và chuỗi mốc thời gian tùy chọn `expectedUpdatedAt` (`DateTimeOffset`), thực thi xóa an toàn và phát sự kiện `Receiver_DeleteStorageUnit(unitId.ToString())`.

### 5.3. Phương thức Receiver (Server đẩy xuống Client)
Theo đúng cài đặt trong `StorageUnitRealtimePublisher.cs`:
1. `Receiver_CreateStorageUnit(StorageUnitRealtimeEventDto data)`: Đẩy DTO sự kiện ô kho mới tạo tới nhóm cơ sở `facility_{facilityId}` và `admin_global`.
2. `Receiver_UpdateStorageUnit(StorageUnitRealtimeEventDto data)`: Đẩy DTO sự kiện ô kho cập nhật tới nhóm cơ sở `facility_{facilityId}` và `admin_global`.
3. `Receiver_DeleteStorageUnit(string unitId)`: Đẩy chuỗi mã ID ô kho (`unitId.ToString()`) tới nhóm cơ sở `facility_{facilityId}` và `admin_global` (hoặc fallback `admin_global`).

*(Ghi chú: Client JavaScript tiếp nhận các sự kiện trên và kích hoạt CustomEvent nội bộ tương ứng `app:storage-unit-created`, `app:storage-unit-updated`, `app:storage-unit-deleted` để cập nhật giao diện mà không phụ thuộc cứng vào cấu trúc DOM).*

---

## 6. Điều Kiện Tiên Quyết Mở Task T20 Cho Codex (T20 Independent Review Entry Conditions)

Codex có thể lập tức tiếp nhận không gian làm việc và khởi động task T20 (Review độc lập sau bàn giao Gemini) khi các điều kiện tiên quyết sau đây được thỏa mãn:

1. **Trạng thái TaskBoard**: Task T19 được chuyển sang trạng thái **IMPLEMENTED** (Chờ Codex review độc lập); các task tiếp theo T20, T21, T22 duy trì trạng thái **WAITING**.
2. **Sự sẵn sàng của tài liệu bàn giao**:
   - Báo cáo bàn giao tổng thể `AgentReport/WebMigration_FinalHandover.md` đã hoàn tất cập nhật các điểm sửa đổi R1.
   - Báo cáo sửa finding vòng R1 `AgentReport/WebMigration_T19_R1_FindingFixes.md` đã được khởi tạo.
   - Báo cáo ghi nhớ task `AgentReport/WebMigration_T19_Memory.md` đã được cập nhật theo đúng quy chuẩn `AgentReport/WebMigration_TaskMemoryGuide.md`.
3. **Đồng bộ mã băm SHA-256 tuyệt đối giữa hai Workspace**:
   - Toàn bộ các báo cáo T16, T17, T18, T19, TaskBoard, TaskMemoryGuide và HandoffMemory phải có mã băm SHA-256 hoàn toàn đồng nhất giữa `D:\FPT\PRN222\Project\Final_Project_Web` và `D:\FPT\PRN222\Project\Final_Project`.
   - Toàn bộ 24 ảnh Evidence PNG trong `AgentReport/Evidence/T18/` phải có mã băm khớp 100% giữa hai workspace.
4. **Trạng thái Git và Điều kiện cây làm việc cho T20**:
   - Nhánh hiện hành: `main` tại commit HEAD `821449030eebe1e38dc3bfda996f8bcb38474d10` (Merge WebMigration T16-T18 verified batch).
   - T19 chỉ bao gồm các tệp bàn giao có phạm vi (scoped uncommitted files): `AgentReport/WebMigration_FinalHandover.md`, `AgentReport/WebMigration_T19_Memory.md`, `AgentReport/WebMigration_T19_R1_FindingFixes.md` và tệp theo dõi được sửa đổi `AgentReport/WebMigration_TaskBoard.md`.
   - Cây làm việc phản ánh các cảnh báo chuẩn hóa kết thúc dòng (Line-ending CRLF/LF normalization noise) trên các tệp T16–T18 trước đó do cơ chế checkout của Git trên Windows; các cảnh báo này không chứa khác biệt nội dung logic (`git diff --quiet` trả về mã 0).
   - Ba tệp loại trừ tiếp tục được bảo toàn nguyên vẹn ngoài commit (`docs/KeHoach_Web_Gemini_Va_Codex_Review.docx`, `AgentReport/WebMigration_T15_CodexLoopState.json`, `scratch/generate_r3_report.js`).
   - T20 được phép bắt đầu ngay với các tệp T19 uncommitted trong không gian làm việc; chu trình commit/merge sau review do Codex thực hiện sẽ chỉ bao gồm các tệp T19 có phạm vi và loại trừ tuyệt đối cả 3 tệp bảo vệ lẫn các thay đổi kết thúc dòng không liên quan.
5. **Quy tắc phối hợp tệp dùng chung (Shared-File Coordination Rule)**:
   - Gemini đã hoàn thành nhiệm vụ T19, không tự ý chỉnh sửa mã nguồn hoặc tệp dùng chung sau khi bàn giao.
   - Nếu có phát sinh chỉnh sửa tệp dùng chung (như TaskBoard), phải thông báo rõ ràng cho Codex trước khi thực hiện.
6. **Không tự ý khởi động T20–T22**:
   - Gemini dừng lại sau khi hoàn tất T19 và phát sinh biên bản ghi nhớ; không tự ý triển khai T20, T21 hay T22.
   - Chu trình Git (tạo nhánh, commit, merge, push) thuộc quyền hạn độc quyền của Codex sau khi hoàn thành review T20.
7. **Phân nhánh kết quả T20**:
   - Nếu Codex review T20 kết luận **No findings**: Chuyển thẳng sang Task T22 (Kết luận nghiệm thu tổng thể).
   - Nếu Codex review T20 phát hiện lỗi/yêu cầu thay đổi (**Findings exist**): Mở Task T21 (Sửa các finding Codex đã giao) trong một chat Gemini mới với phạm vi cụ thể được giao.
