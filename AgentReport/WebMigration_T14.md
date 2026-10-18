# Báo Cáo Triển Khai Task T14: Quản Trị Tài Khoản và Trang Của Khách

- **Mã Task**: T14
- **Tên Task**: Quản trị tài khoản và trang của khách (Admin Accounts & Customer Pages)
- **Đợt (Wave)**: 5
- **Owner**: Gemini C
- **Phụ thuộc (Dependencies)**: T11 (VERIFIED)
- **Trạng thái**: **VERIFIED**
- **Không gian làm việc**: `D:\FPT\PRN222\Project\Final_Project_Web` & `D:\FPT\PRN222\Project\Final_Project`
- **Solution Web**: `SelfStorageManagementSystem.Web.sln`

---

## 1. Mục Tiêu và Phạm Vi Triển Khai

Thực hiện toàn bộ giao diện quản trị tài khoản cho System Administrator và giao diện cá nhân cho Storage Customer / Người dùng xác thực:
1. **Quản trị tài khoản (`Pages/Admin/Accounts/`)**:
   - `Index.cshtml` & `Index.cshtml.cs`: Danh sách tài khoản có phân trang, bộ lọc đa tiêu chí (từ khóa email/họ tên/mã nhân viên, trạng thái tài khoản, vai trò).
   - `Create.cshtml` & `Create.cshtml.cs`: Tạo tài khoản nhân viên mới (`employee_profile`), gán vai trò nhân sự hợp lệ và cơ sở phân công ban đầu tùy chọn.
   - `Details.cshtml` & `Details.cshtml.cs`: Xem chi tiết thông tin định danh, hồ sơ nhân viên/khách hàng, lịch sử phân công cơ sở; thực hiện 4 nghiệp vụ cập nhật (đổi trạng thái tài khoản, cập nhật danh sách vai trò, phân công cơ sở mới, chấm dứt phân công đang hoạt động).
   - Bảo vệ nghiêm ngặt: Chặn khóa hoặc thu hồi vai trò của Quản trị viên hệ thống cuối cùng (`sole active admin protection`); kiểm tra ràng buộc vai trò nhân viên yêu cầu hồ sơ nhân viên.
2. **Trang ô kho của khách (`Pages/MyUnits/`)**:
   - `Index.cshtml` & `Index.cshtml.cs`: Danh sách các ô kho đang thuê của khách hàng hiện tại kèm phân trang và liên kết xem chi tiết bàn giao.
   - `HandoverDetails.cshtml` & `HandoverDetails.cshtml.cs`: Chi tiết thỏa thuận bàn giao gồm thông tin hợp đồng, cơ sở, ô kho, checklist kiểm tra bàn giao, và metadata thông tin truy cập (credential type, valid window, status).
   - **Zero-Persistence PIN Security**: Tuyệt đối không lưu trữ hoặc hiển thị nút bấm/API truy xuất mã PIN dạng văn bản rõ (plaintext). Hiển thị cảnh báo bảo mật một chiều rõ ràng cho khách hàng.
   - **Bảo vệ quyền sở hữu (Ownership Enforcement)**: Khách hàng A tuyệt đối không xem được dữ liệu ô kho hay biên bản bàn giao của khách hàng B (trả về thông báo từ chối an toàn / 404).
3. **Trang thông báo người dùng (`Pages/Notifications/`)**:
   - `Index.cshtml` & `Index.cshtml.cs`: Danh sách thông báo in-app gửi riêng cho người dùng đang đăng nhập với phân trang, badge kênh gửi, thời gian và trạng thái.
4. **Scripts hỗ trợ & chuẩn bị SignalR (T15 readiness)**:
   - `admin-accounts.js`: Hộp thoại xác nhận thao tác quản trị, xử lý hook realtime `AccountChanged`.
   - `my-units.js`: Hook realtime `CheckInChanged` để tự động làm mới trạng thái ô kho của khách.
   - `notifications.js`: Hook realtime `NotificationsChanged` để cập nhật danh sách thông báo tức thời.
5. **Chính sách phân quyền & an toàn hệ thống**:
   - Trang Admin chỉ cho phép `system_administrator` (chính sách `AdminOnly`).
   - Trang MyUnits chỉ cho phép `storage_customer` (chính sách `CustomerOnly`).
   - Trang Notifications áp dụng `[Authorize]`.
   - Toàn bộ các thao tác POST form đều được bảo vệ bằng Antiforgery token (`ValidateAntiForgeryToken`).
   - Giữ nguyên bố cục chung `_Layout.cshtml`, các liên kết menu đã được cấu hình chuẩn từ T04.

---

## 2. Chi Tiết Các Tệp Mã Nguồn Đã Tạo & Cập Nhật

| Đường dẫn tệp | Mô tả chi tiết |
| :--- | :--- |
| `Pages/Admin/Accounts/Index.cshtml(.cs)` | Danh sách tài khoản phân trang, lọc theo keyword, status, role code. |
| `Pages/Admin/Accounts/Create.cshtml(.cs)` | Biểu mẫu tạo tài khoản nhân sự với validation và danh mục cơ sở. |
| `Pages/Admin/Accounts/Details.cshtml(.cs)` | Chi tiết tài khoản, đổi status, cập nhật role, phân công/chấm dứt cơ sở. |
| `Pages/MyUnits/Index.cshtml(.cs)` | Trang My Units của khách hàng đang đăng nhập kèm phân trang. |
| `Pages/MyUnits/HandoverDetails.cshtml(.cs)` | Chi tiết bàn giao nhận kho an toàn, tuân thủ zero-persistence PIN. |
| `Pages/Notifications/Index.cshtml(.cs)` | Trang danh sách thông báo in-app cá nhân hóa. |
| `wwwroot/js/admin-accounts.js` | Client script hỗ trợ xác nhận hành động và hook realtime T15. |
| `wwwroot/js/my-units.js` | Client script hỗ trợ hook realtime T15 cho My Units. |
| `wwwroot/js/notifications.js` | Client script hỗ trợ hook realtime T15 cho Notifications. |
| `BusinessLogic/.../AdminAccountService.cs` | Sửa tracking entity detached `roleEntity` trong EF Core khi tạo tài khoản nhân viên; bảo vệ mapper an toàn `role != null`. |
| `WebTests/AdminAccountsAndCustomerPagesTests.cs` | Bộ kiểm thử tích hợp 32 test cases bao quát mọi kịch bản của T14. |

---

## 3. Bằng Chứng Kiểm Thử & Nghiệm Thu (Verification Evidence)

### 3.1. Kiểm thử tích hợp T14 (`AdminAccountsAndCustomerPagesTests`)
Chạy kiểm thử với lệnh:
```bash
dotnet test "tests/SelfStorageManagementSystem.WebTests/SelfStorageManagementSystem.WebTests.csproj" --filter "FullyQualifiedName~AdminAccountsAndCustomerPagesTests"
```
**Kết quả**:
```text
Passed!  - Failed: 0, Passed: 32, Skipped: 0, Total: 32, Duration: 8 s
```

Danh sách các nhóm test cases đã kiểm chứng thành công:
1. **Phân quyền truy cập chéo (Authorization & Cross-Access Guards)**:
   - Khách vãng lai chưa đăng nhập bị chuyển hướng đến `/Account/Login`.
   - Customer truy cập `/Admin/Accounts/*` bị từ chối chuyển hướng đến `/Account/AccessDenied` (Forbidden).
   - Staff/Manager truy cập `/Admin/Accounts/*` bị từ chối chuyển hướng đến `/Account/AccessDenied`.
   - Admin truy cập `/MyUnits/*` bị từ chối chuyển hướng đến `/Account/AccessDenied`.
2. **Quản trị tài khoản Admin (Admin Account Operations)**:
   - Admin xem danh sách tài khoản thành công kèm phân trang và bộ lọc.
   - Admin lọc danh sách theo vai trò và trạng thái chính xác.
   - Admin tạo tài khoản nhân viên mới hợp lệ (tạo đồng thời `user`, `employee_profile`, `user_role` và `staff_facility_assignment`).
   - Admin gán cơ sở mới và chấm dứt phân công cơ sở hiện tại thành công.
   - Admin cập nhật trạng thái tài khoản người dùng khác thành công.
   - Admin cập nhật vai trò người dùng khác thành công.
   - **Guard Quản trị viên cuối cùng**: Từ chối khóa trạng thái hoặc thu hồi vai trò Admin đối với quản trị viên duy nhất còn lại, hiển thị thông báo lỗi rõ ràng.
3. **Trang của khách hàng (Customer My Units & Handover)**:
   - Khách hàng xem danh sách ô kho đang thuê của chính mình thành công.
   - Khách hàng xem chi tiết biên bản bàn giao của ô kho mình sở hữu.
   - **Bảo mật mã PIN (Zero-Persistence)**: Trang chi tiết bàn giao TUYỆT ĐỐI KHÔNG chứa mã PIN dạng plaintext, không có bất kỳ nút hay endpoint nào cho phép lộ mã PIN đã cấp.
   - **Kiểm tra quyền sở hữu (Ownership Verification)**: Khách hàng A không thể xem chi tiết bàn giao hợp đồng của Khách hàng B (báo 404 Not Found an toàn, không rò rỉ dữ liệu).
4. **Trang thông báo (Notifications)**:
   - Người dùng đăng nhập xem danh sách thông báo in-app của chính mình.
   - Thông báo của người dùng khác không bao giờ bị lộ ra ngoài.
5. **Bảo mật CSRF / Antiforgery**:
   - Mọi yêu cầu POST thiếu antiforgery token đều bị từ chối ngay lập tức (HTTP 400 Bad Request).

### 3.2. Kiểm thử hồi quy toàn bộ giải pháp Web (Full Solution Regression)
- Toàn bộ 284 bài kiểm thử WebTests thông thường: **284/284 PASS (0 FAILED)**.
- Toàn bộ 14 bài kiểm thử SignalR RealtimeHubTests: **14/14 PASS (0 FAILED)**.
- Tổng cộng: **298/298 PASS**, không có bất kỳ hồi quy nào trên hệ thống Web.

---

## 4. Kết Luận & Sẵn Sàng Bàn Giao

- Task T14 đã hoàn thành 100% các yêu cầu kỹ thuật và nghiệp vụ được giao.
- Toàn bộ mã nguồn đã được đồng bộ chuẩn xác giữa `Final_Project_Web` và `Final_Project`.
- Sẵn sàng chuyển giao cho Task **T15: Tích hợp realtime cả năm chức năng**.
- Đánh dấu trạng thái Task T14 trên TaskBoard: **VERIFIED**.
