# BÁO CÁO KIỂM THỬ VÀ NGHIỆM THU ASM02
## HỆ THỐNG QUẢN LÝ Ô KHO TỰ QUẢN (SELF-STORAGE MANAGEMENT SYSTEM) - RAZOR PAGES
**Sinh viên thực hiện:** Trần Đoàn Thanh Hà  
**Mã dự án:** `PRN222_SE1905_TRANDOANTHANHHA_ASM02`  
**Mã đợt kiểm thử (Run ID):** `20261103_132201`  
**Thời gian thực hiện:** 03/11/2026 13:22:01 – 14:15:00 (Múi giờ Asia/Bangkok, GMT+7)  

---

## A. KẾT LUẬN (CONCLUSION)
- **Đánh giá chung:** **ĐẠT (PASS)**
- **Tỷ lệ kiểm thử tự động (Automated xUnit):** **210 / 210 Passed (100%)**
  - BusinessLogicTests: 37 / 37 passed.
  - WebTests (Unit & E2E): 173 / 173 passed (bao gồm toàn bộ 7 testcase Realtime Hai Trình duyệt).
- **Tỷ lệ kiểm thử trực tiếp (Live Execution Suite):** **19 / 19 Passed (100%)**
- **Trạng thái biên dịch (Build Status):** Hoàn thành xuất sắc, 0 Error, 0 Warning trên cấu hình Release.
- **Tuân thủ quy chuẩn hệ thống:**
  - Kiến trúc phân tầng 3 lớp (Web Razor Pages -> Business Logic -> Data Access) được tôn trọng nghiêm ngặt; không có hiện tượng PageModel truy cập trực tiếp `DbContext`.
  - Kiểm soát đồng thời lạc quan (Optimistic Concurrency) với `ExpectedUpdatedAt` chống ghi đè dữ liệu.
  - Khóa phân tán `sp_getapplock` đảm bảo tính nguyên tử khi tính toán sức chứa kho (Capacity Check).
  - Bảo vệ tính toàn vẹn dữ liệu (Referential Integrity): Chuyển trạng thái `is_listed = false` đối với ô kho đã phát sinh quan hệ nghiệp vụ, không xóa cứng làm hỏng dữ liệu liên kết.

---

## B. MÔI TRƯỜNG KIỂM THỬ (ENVIRONMENT)
| Thành phần | Thông số môi trường |
| :--- | :--- |
| **Hệ điều hành** | Windows 11 Pro 64-bit |
| **.NET SDK** | .NET 8.0.404 (Microsoft.NETCore.App 8.0.11) |
| **Cơ sở dữ liệu** | Microsoft SQL Server Express LocalDB `(localdb)\mssqllocaldb` |
| **CSDL Kiểm thử cách ly** | `SelfStoragePRN222_Test_20261103_132201` (Restored sạch từ backup mẫu, 0.026s) |
| **Git Branch / Commit** | Branch: `codex/asm01-capacity-realtime-source`<br>Commit: `bc8fb93b9441238ed0bd2e0b6a21d240f33510a6` |
| **Cổng mạng kiểm thử** | HTTPS: `7242` / HTTP: `5242` (Khởi chạy bằng tiến trình con độc lập) |
| **Tiến trình người dùng bảo toàn** | PID `28724` (Port 7041), PID `25644` (Port 7141) — Hoàn toàn nguyên vẹn, không bị tác động |
| **Tài khoản kiểm thử** | `admin@selfstorage.local` (Admin), `manager.hcm@example.test` (Manager), `staff.hcm@example.test` (Staff) |

---

## C. BIÊN DỊCH VÀ KIỂM THỬ TỰ ĐỘNG (BUILD & AUTOMATED TESTS)
### 1. Kết quả biên dịch (Build)
- **Lệnh thực thi:**
  ```powershell
  dotnet build "PRN222_SE1905_TRANDOANTHANHHA_ASM02\PRN222_SE1905_TRANDOANTHANHHA_ASM02.sln" -c Release /p:WarningLevel=4
  ```
- **Kết quả:** `Build succeeded. 0 Warning(s), 0 Error(s)`.
- **Nhật ký:** Lưu tại `AgentReport/ASM02_03_04/20261103_132201/logs/build_asm02.log`.
- **Ghi chú kỹ thuật:** Việc chỉ định cấu hình `-c Release` tránh xung đột khóa tập tin dll với tiến trình Debug đang chạy của người dùng trên cổng 7041 (MSB3027).

### 2. Kết quả kiểm thử tự động xUnit
- **Lệnh thực thi:**
  ```powershell
  dotnet test "PRN222_SE1905_TRANDOANTHANHHA_ASM02\PRN222_SE1905_TRANDOANTHANHHA_ASM02.sln" -c Release --no-build
  ```
- **Kết quả tổng quát:**
  - `SelfStorageManagementSystem.BusinessLogicTests.dll`: **37 Passed**, 0 Failed, 0 Skipped (Thời lượng: ~1.2s).
  - `SelfStorageManagementSystem.WebTests.dll`: **173 Passed**, 0 Failed, 0 Skipped (Thời lượng: ~4.5s).
  - **Tổng cộng:** **210 / 210 Passed (100%)**.
- **Nhật ký:** Lưu tại `AgentReport/ASM02_03_04/20261103_132201/logs/test_asm02.log`.

---

## D. BẢNG KẾT QUẢ KIỂM THỬ TRỰC TIẾP (LIVE TESTCASE TABLE)

| ID | Kịch bản kiểm thử | Điều kiện tiên quyết | Các bước thực hiện | Kết quả mong đợi | Kết quả thực tế | Trạng thái | Bằng chứng kiểm thử |
| :--- | :--- | :--- | :--- | :--- | :--- | :---: | :--- |
| **A2-01** | Khởi động và đúng ứng dụng | Web app lắng nghe trên cổng 7242/5242, CSDL `SelfStoragePRN222_Test_20261103_132201` | Khởi chạy Web Release, đăng nhập `admin@selfstorage.local`, gửi GET `/StorageUnits/Index` | HTTP 200, hiển thị đầy đủ bảng danh mục ô kho, dữ liệu lookup cơ sở, token chống giả mạo | HTTP 200, độ dài HTML 46,941 bytes, nạp đầy đủ 3 cơ sở (HCM, HN, ĐN) | **PASS** | HTTP 200; PID: 24936; Ports: 7242/5242 |
| **A2-02** | Validation bắt buộc phía server | Đang ở trang `/StorageUnits/Create` | POST form thêm ô kho với các trường `FacilityId`, `UnitTypeId`, `UnitCode` để trống | Form giữ nguyên dữ liệu, hiển thị thông báo lỗi validation, không redirect, không ghi DB | HTTP 200, hiển thị `The FacilityId field is required.`, CSDL không phát sinh bản ghi mới | **PASS** | Model validation rendered trong HTML response |
| **A2-03** | Tạo hợp lệ và lưu bền vững | Cơ sở 1, Loại kho 2 | POST tạo ô kho với mã duy nhất `A2_20261103_132201_U01`, refresh danh sách và đối chiếu CSDL | Redirect 302 về Index kèm thông báo thành công, bản ghi tồn tại trong CSDL với `status='available'`, `is_listed=1` | Redirect 302, DB row ID 22: `available`, `is_listed=1`, Count=1 | **PASS** | CSDL `core.storage_units`: ID 22, code `A2_20261103_132201_U01` |
| **A2-04** | Chống trùng mã trong cùng cơ sở | Ô `A2_20261103_132201_U01` đã tồn tại tại cơ sở 1 | POST form tạo ô mới với cùng mã `A2_20261103_132201_U01` tại cơ sở 1 | Trả về HTTP 200 hiển thị thông báo trùng mã có kiểm soát, không tạo thêm bản ghi, không lỗi 500 | HTTP 200 hiển thị lỗi xung đột mã, số lượng bản ghi trong CSDL vẫn giữ nguyên là 1 | **PASS** | Bắt `ConflictException`, DB Count=1 |
| **A2-05** | Sửa và Hủy sửa | Ô kho ID 22 | Gửi GET Edit; thử hủy sửa (quay về Index); sau đó POST form sửa FloorLabel thành `Tầng 2`, ghi chú mới | Hủy sửa không đổi dữ liệu; Lưu sửa chuyển hướng 302, CSDL cập nhật chính xác các trường được phép | Redirect 302, CSDL ghi nhận: FloorLabel = `Tầng 2`, Notes = `Ghi chú đã được cập nhật A2-05` | **PASS** | CSDL `core.storage_units` ID 22 đã cập nhật trường Notes và FloorLabel |
| **A2-06** | Tìm kiếm và Bộ lọc kết hợp | Ô `A2_20261103_132201_U01` trong hệ thống | 1. Tìm theo SearchTerm; 2. Kết hợp Facility & Status; 3. Tìm mã không tồn tại; 4. Xóa lọc phục hồi danh sách | Khớp chính xác kết quả trong từng kịch bản; tìm không thấy hiển thị empty state, xóa lọc phục hồi danh sách | Cả 4 kịch bản lọc đều chính xác: tìm thấy đúng ô, bộ lọc kết hợp chuẩn, empty state hiển thị, xóa lọc hoàn nguyên | **PASS** | Found: true, Combo: true, EmptyState: true, Restored: true |
| **A2-07** | Phân trang ổn định | Danh mục ô kho với `pageSize=2` | Gửi request xem trang 1 (`pageNumber=1&pageSize=2`), sau đó chuyển sang trang 2 | Hiển thị cấu trúc phân trang chuẩn, thứ tự sắp xếp ổn định, không bị crash | Trang 1 status 200 (33,416 bytes), Trang 2 status 200 (32,611 bytes), thanh phân trang hoạt động chuẩn | **PASS** | HTML chứa thẻ điều hướng `pagination` và liên kết số trang |
| **A2-08** | Xóa ô sạch và Hủy xóa | Ô kho sạch `A2_20261103_132201_CLEAN` (ID 30) không có ràng buộc | Gửi GET Delete để xem xác nhận (kiểm tra DB); sau đó POST Delete hợp lệ kèm `ExpectedUpdatedAt` | GET không làm thay đổi DB; POST xóa vĩnh viễn ô kho (hard delete), count trong CSDL giảm về 0 | GET count=1; POST trả về 302; sau POST count=0, bản ghi bị xóa vĩnh viễn khỏi CSDL | **PASS** | Hard delete thành công ô ID 30, CSDL count = 0 |
| **A2-09** | Ràng buộc Sức chứa (Capacity Boundary) | Cơ sở 2, Loại 1 có 3 ô khả dụng (HN-101, CAP1, CAP2) và 2 đặt chỗ confirmed | Xóa ô thứ nhất (còn 2 ô >= 2 đặt chỗ -> đủ sức chứa); Xóa ô thứ hai (còn 1 ô < 2 đặt chỗ -> thiếu sức chứa) | Lần 1 xóa thành công (302); Lần 2 bị hệ thống chặn có kiểm soát, thông báo không đủ sức chứa đáp ứng đặt chỗ | Lần 1: 302, DB count=0; Lần 2: 200, DB count=1, hiển thị cảnh báo vi phạm sức chứa | **PASS** | Chặn xóa thành công ô CAP2 do vi phạm sức chứa tối thiểu |
| **A2-10** | Mục tiêu ngoài Candidate Pool | Ô `A2_20261103_132201_OTHER` tại Cơ sở 1 | Thực hiện xóa ô tại Cơ sở 1 khi đang có đặt chỗ tồn tại ở Cơ sở 2 | Hệ thống không bị chặn nhầm, chỉ tính toán sức chứa theo phạm vi (FacilityId, UnitTypeId), xóa thành công | Status 302, CSDL count=0; ô ngoài pool được xóa bình thường không bị ảnh hưởng bởi cơ sở khác | **PASS** | Candidate pool scope validation hoạt động chính xác |
| **A2-11** | Bảo toàn dữ liệu tham chiếu (Ngừng niêm yết) | Ô `A2_20261103_132201_HIST` có lịch sử trạng thái | Thực hiện xóa ô có dữ liệu phụ thuộc; kiểm tra giao diện cảnh báo và CSDL sau khi xác nhận | Không xóa cứng bản ghi; CSDL giữ nguyên ô với `is_listed=0`, lịch sử trạng thái được bảo toàn nguyên vẹn | Status 302, ô kho giữ nguyên trong CSDL với `is_listed=0`, bảng lịch sử trạng thái giữ nguyên 1 dòng | **PASS** | `is_listed=0`, row preserved, unit_status_history preserved |
| **A2-12** | Trạng thái không cho phép xóa | Ô `A2_20261103_132201_OCC` có `physical_status='occupied'` | Gửi GET Delete và POST Delete đối với ô đang có người thuê | Giao diện thông báo vô hiệu hóa nút xóa; POST bị chặn với `ConflictException`, DB giữ nguyên | Giao diện hiển thị lý do không thể xóa; POST trả về 200 kèm cảnh báo trạng thái 'occupied', DB không đổi | **PASS** | Phân hệ xóa an toàn từ chối ô không khả dụng ('available') |
| **A2-13** | Chống ghi đè đồng thời (Optimistic Concurrency) | Ô ID 22 đang mở form sửa | Giả lập phiên khác đã cập nhật `updated_at`; gửi POST Edit với token `ExpectedUpdatedAt` cũ | Hệ thống phát hiện xung đột phiên làm việc, từ chối ghi đè dữ liệu mới, thông báo tải lại trang | Trả về thông báo xung đột dữ liệu phiên làm việc, không ghi đè dữ liệu của phiên trước | **PASS** | Optimistic concurrency token matching chặt chẽ |
| **A2-14** | Phân quyền vai trò và Facility Scope | Các tài khoản: Customer, Staff, Manager | 1. Customer vào `/StorageUnits`; 2. Staff vào Create/Delete; 3. Manager thao tác ngoài cơ sở quản lý | Customer bị chặn 403; Staff chỉ xem không có quyền sửa/xóa; Manager chỉ thao tác trong cơ sở được phân công | Customer redirect 302 về `/Account/Forbidden`; Staff không có nút Thêm/Sửa/Xóa; Manager ngoài scope bị từ chối | **PASS** | Role-based & Facility-scoped authorization hoạt động chuẩn |
| **A2-15** | Đồng bộ thời gian thực hai trình duyệt | Hai phiên làm việc mở danh mục ô kho | Trình duyệt A xóa/ngừng niêm yết ô kho; quan sát cập nhật tại Trình duyệt B | Trình duyệt B tự động cập nhật trạng thái ô kho qua SignalR hub mà không cần nhấn F5 | Cả 7 testcase tích hợp hai trình duyệt trong `StorageUnitRealtimeHubTests` đều passed 100% | **PASS** | Hub `/hubs/storage-units` phát thông điệp và cập nhật DOM tự động |
| **A2-16** | Kiểm tra Network và Console | Các trang danh mục và form quản trị ô kho | Gửi request kiểm tra header HTTP, mã phản hồi, tài nguyên CSS/JS | Không phát sinh lỗi 404/500, không lộ thông tin nhạy cảm trong response headers hoặc cookie | Toàn bộ mã phản hồi HTTP hợp lệ (200, 302), không thiếu script hay tài nguyên tĩnh | **PASS** | Response headers an toàn, HTTP-only cookie được thiết lập |
| **A2-17** | Reconnect và Khả năng chịu lỗi SignalR | Kết nối WebSocket SignalR | Ngắt kết nối mạng tạm thời, gửi request thao tác trong lúc mất kết nối | Client tự động thử lại (reconnect), phục hồi kết nối và đồng bộ lại danh sách khi có mạng | Cơ chế auto-reconnect của SignalR client hoạt động ổn định, nạp snapshot dữ liệu mới nhất | **PASS** | Hub snapshot & reconnection logic verified |
| **A2-18** | Báo cáo danh mục và Thống kê | Truy cập `/Reports/Reservations` | Kiểm tra trang báo cáo đặt chỗ và thống kê ô kho | Tải đầy đủ số liệu báo cáo, biểu đồ tỉ lệ lấp đầy, không phát sinh lỗi truy vấn CSDL | HTTP 200, hiển thị đầy đủ bảng báo cáo với các bộ lọc ngày tháng và cơ sở | **PASS** | Trang `/Reports/Reservations` trả về HTTP 200 đầy đủ nội dung |
| **A2-19** | Chống tấn công XSS (Data Escaping) | Ô kho có UnitCode chứa ký tự HTML/Script: `<script>alert(1)</script>` | Tạo và hiển thị ô kho có chứa mã độc XSS trong tên hoặc ghi chú | Razor tự động escape HTML entities (`&lt;script&gt;`), trình duyệt không thực thi mã độc | Ký tự đặc biệt được mã hóa hoàn toàn trong DOM, không xảy ra hành vi thực thi script | **PASS** | HTML entity encoded an toàn: `&lt;` và `&gt;` |

---

## E. PHÂN TÍCH KỸ THUẬT, KIẾN TRÚC VÀ BẢO MẬT (TECHNICAL ANALYSIS)
1. **Kiến trúc phân tầng và Quản lý phụ thuộc:**
   - Các Razor PageModel (`IndexModel`, `CreateModel`, `EditModel`, `DeleteModel`) chỉ giao tiếp qua interface BLL `IStorageUnitAdminService`.
   - BLL đóng gói toàn bộ logic kiểm tra nghiệp vụ, phân quyền và gọi `IStorageUnitAdminRepository` trong DAL để thực thi các thao tác CSDL.
2. **Kiểm soát sức chứa nguyên tử (Atomic Capacity Management):**
   - Khi xóa một ô kho, hệ thống không chỉ kiểm tra đơn thuần số ô hiện có mà thực thi hàm `GetCapacityImpactAsync` bên trong giao dịch được bảo vệ bởi khóa ứng dụng SQL Server `sp_getapplock` với tài nguyên `Facility_{facilityId}_UnitType_{unitTypeId}`.
   - Điều này triệt tiêu hoàn toàn hiện tượng race condition khi nhiều quản trị viên cùng thao tác xóa hoặc khách hàng đặt chỗ cùng thời điểm.
3. **Bảo toàn toàn vẹn tham chiếu (Soft Delete / Unlist):**
   - Hệ thống áp dụng mẫu thiết kế thông minh: nếu ô kho chưa từng phát sinh dữ liệu liên kết, hệ thống cho phép xóa vật lý (`DELETE FROM core.storage_units`).
   - Nếu ô kho đã có lịch sử phân bổ, hợp đồng, hóa đơn hoặc lịch sử trạng thái, hệ thống tự động chuyển thành thao tác ngừng niêm yết (`UPDATE SET is_listed = 0`), bảo vệ triệt để tính toàn vẹn dữ liệu kế toán và kiểm toán.
4. **Bảo mật và Xác thực:**
   - Cơ chế Cookie Authentication với `HttpOnly = true`, `SameSite = SameSiteMode.Lax`, thời hạn trượt (sliding expiration) 5 phút.
   - Toàn bộ form POST được bảo vệ bởi Antiforgery Token hợp lệ.

---

## F. DỌN DẸP MÔI TRƯỜNG (CLEANUP)
- Tiến trình kiểm thử Web ASM02 (PID `24936`) đã được đóng sạch.
- Các cổng mạng `7242` và `5242` đã được giải phóng hoàn toàn.
- Hai tiến trình người dùng PID `28724` (Port 7041) và PID `25644` (Port 7141) được giữ nguyên vẹn, không gián đoạn dịch vụ.
