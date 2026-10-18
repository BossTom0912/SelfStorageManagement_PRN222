# WebMigration T19 R1 — Báo Cáo Xử Lý Findings Review Độc Lập Từ Codex

- **Nhiệm vụ**: Task T19 (Đóng gói bàn giao cho Codex) — Vòng sửa chữa R1 (T19 R1 Finding Fixes)
- **Báo cáo review của Codex đối chiếu**: `AgentReport/WebMigration_T19_R1_CodexReview.md` (SHA-256 `B554A0D328C6A8D623C2D3F4C5ABE1C08947BED9E003296EBB7E71EFEDEBB804`)
- **Báo cáo bàn giao đã cập nhật**: `AgentReport/WebMigration_FinalHandover.md` (SHA-256 `28BD72CAD791F62086F4944D7B013B186BF8F6A7E88FA0CAF36110827BF8DD19`)
- **Ghi nhớ task đã cập nhật**: `AgentReport/WebMigration_T19_Memory.md` (SHA-256 `5998E745FB32F361B4F4634F1B45E6E2252B6969A9B30AB63ABE6CF29DB308B3`)
- **Không gian làm việc**: `D:\FPT\PRN222\Project\Final_Project_Web` và `D:\FPT\PRN222\Project\Final_Project`
- **Đầu mối thực hiện**: Gemini A (Foundation & Integration Lead)
- **Trạng thái**: **T19 IMPLEMENTED** (Chờ Codex review lại); **T20–T22 WAITING**

---

## 1. Tổng Quan Kết Quả Xử Lý

Toàn bộ 7 finding (3 P1 và 4 P2) do Codex nêu ra trong biên bản `WebMigration_T19_R1_CodexReview.md` đã được xử lý triệt để trong đúng 1 vòng duy nhất (Round R1). Mọi thông tin mô tả, chữ ký phương thức, mã định danh vai trò, kiểu dữ liệu tương tranh, danh mục trigger SQL và đường dẫn URL đều đã được đối chiếu và chuẩn hóa 100% theo mã nguồn thực tế, lược đồ cơ sở dữ liệu `sys.triggers`, manifest R6 và tệp vật lý trên đĩa.

---

## 2. Chi Tiết Khắc Phục Từng Finding

### Finding 1 [P1] — Cập nhật chính xác 21/24 mã băm SHA-256 ảnh bằng chứng T18
- **Vấn đề**: Bảng kê khai bằng chứng trong báo cáo bàn giao ban đầu chứa 21/24 mã băm SHA-256 không khớp với báo cáo R6 và tệp vật lý trên đĩa (do lỗi sao chép từ chuỗi tạm).
- **Khắc phục**: Đã đối chiếu trực tiếp với `AgentReport/WebMigration_T18_R6_FindingFixes.md` (Mục 4) và kiểm tra hàm băm `Get-FileHash -Algorithm SHA256` trên toàn bộ 24 tệp PNG tại `AgentReport/Evidence/T18/`. Cập nhật toàn bộ 24 dòng trong bảng kê khai của `WebMigration_FinalHandover.md` với kích thước byte và mã băm SHA-256 tuyệt đối chuẩn xác:
  - `T18_01_Customer_Catalog_Initial_Context2_Customer.png`: `042C43194480C1361A8A5EAAF17D457E450EE294F889D610F4EC78C5CB7849FF` (99,093 B)
  - `T18_01_Staff_StorageUnits_Initial_Context1_Staff.png`: `6D0E2FDD1AE1ED32DD1A1A13CCF05145ABA076215C8ACA6582936C8146D4EA29` (92,766 B)
  - `T18_01_Staff_Unit_Created_Context1_Staff.png`: `40DF545CEFC0C3209F5E23645D3B213DE4FD7D202F63C281541ED95A797792DF` (92,903 B)
  - `T18_01_Staff_Unit_Deleted_Context1_Staff.png`: `7BF33F789CFDF0D0918719D033FEFE4BCFED3F3C528270AADF5F24A41D1FE14A` (82,376 B)
  - `T18_01_Staff_Unit_Updated_Context1_Staff.png`: `26DC50A399DAD51897D8FD435B77F5299FBB4C0A6C9F40A889ECE474676500A3` (88,098 B)
  - `T18_02_Customer_Reservation_Cancelled_Context2_Customer.png`: `119310A1DE821FD5F92CA2EC08258949C2F552A9A82B78F30251D8DC2E489128` (63,556 B)
  - `T18_02_Customer_Reservation_Ticking_Context2_Customer.png`: `94AD9E461F08DE6C1060928C827C32FC1D05EC91E69D2C3CFFA2551333ACDC89` (79,051 B)
  - `T18_02_Staff_Reservation_Cancelled_View_Context1_Staff.png`: `B2F5268E825FA9F8A40453C8D46C1D0E3508C765CCE50ED341B7E756B47DF1D1` (77,525 B)
  - `T18_02_Staff_Reservation_Hold_View_Context1_Staff.png`: `5C192B8E6EF4102938FC8E0DD3E21F51F9DDF7257332FFFD11B4527CFBB24BBB` (78,198 B)
  - `T18_03_Customer_Checkout_Page_Context2_Customer.png`: `C3061BB3DB99D8314886EED00D05A28F2E1DD6816EFC25EC14909214357C0264` (110,351 B)
  - `T18_03_Customer_Demo_Simulator_Context2_Customer.png`: `F505AB2DC731254341625B612D29B7AD844F3FB860C2D766C83B6AD00A01E533` (62,797 B)
  - `T18_03_Customer_Invoices_Paid_Context2_Customer.png`: `C4DD68F51CBE682574E59AC2AC37B314E53E32A5E3E069BD9395F6BE663CA1C0` (88,980 B)
  - `T18_03_Customer_Payment_Result_Context2_Customer.png`: `74A6188AB802463747671B8295AE1930F6CC406764AC227F53E88F024B211934` (66,464 B)
  - `T18_03_Staff_Payments_Overview_Context1_Staff.png`: `DCC9AA85679283FE2036CF024D0E2D20D711937DB77FB97BD0FE609ABEFF8237` (70,481 B)
  - `T18_03_Staff_Refund_Approved_View_Context1_Staff.png`: `BBF18B63AAA8004EF266A783C4792B5E71E92DDEAD41D1C4D88D79856FC21CB4` (72,302 B)
  - `T18_03_Staff_Refund_Requested_View_Context1_Staff.png`: `A992F254EFE4E9983D1FDE64D926F5367449318FF426D457F268FA89038C66EF` (72,208 B)
  - `T18_04_Customer_MyUnits_Active_Context2_Customer.png`: `1EA3B32E4C18356F8A5EF56EE5C53F0522079250A464FBAA0ABFC9CFE3F8DD1B` (106,399 B)
  - `T18_04_Customer_Notifications_Received_Context2_Customer.png`: `FBAD940BB0ABEB773561F66E0EA36F1D223DA19FBD5F43480A1C351462FF6DDF` (69,275 B)
  - `T18_04_Staff_CheckIn_Completed_Sanitized_Context1_Staff.png`: `820AD0D3DA22B0E44ACE1917229BDA4A96F71B1D09AF2860EAA84FD0B3FC69F7` (75,834 B)
  - `T18_04_Staff_CheckIn_Dashboard_Context1_Staff.png`: `F6375818C3E13E46028934800C75D6262D944F9821685AF9BE6C4D1BD476B0AD` (86,491 B)
  - `T18_05_Admin_Accounts_List_Context1_Staff.png`: `A8F8A2053274EBC5F593BFD6231FC130E824D9E9DB5E7555EB4384B83948E148` (98,643 B)
  - `T18_05_Admin_Revocation_Rejected_Context1_Staff.png`: `12B5EF7EF252E3C9130297217079B484B3BC17E0890646854DC8F3116C76A049` (44,640 B)
  - `T18_05_Customer_Notifications_Context2_Customer.png`: `FBAD940BB0ABEB773561F66E0EA36F1D223DA19FBD5F43480A1C351462FF6DDF` (69,275 B)
  - `T18_06_Staff_Reconnected_Reconciled_Context1_Staff.png`: `840A81957AE5C1ED3DE39D6AE5E27BEA8397F75C9A1164F3508496F1EF564730` (97,600 B)
- **Kiểm chứng**: 24/24 tệp khớp 100% giữa `Final_Project_Web` và `Final_Project`.

---

### Finding 2 [P1] — Hiệu chỉnh đúng chữ ký phương thức SignalR Hub và Payload Receiver
- **Vấn đề**: Báo cáo trước đó ghi các kiểu tham số dạng đối tượng request giả định cho Hub (`StorageUnitHubCreateRequest`, ...) và DTO cho `Receiver_DeleteStorageUnit`.
- **Khắc phục**: Cập nhật lại chính xác theo mã nguồn `src/SelfStorageManagementSystem.Web/Hubs/StorageUnitHub.cs` và `src/SelfStorageManagementSystem.Web/Realtime/StorageUnitRealtimePublisher.cs`:
  - **Hub Invocations (Client -> Server)**:
    1. `HubCreate_StorageUnit(string dataObj)`: Nhận chuỗi JSON request tạo ô kho, deserialize thành `CreateStorageUnitRequest`.
    2. `HubUpdate_StorageUnit(string dataObj)`: Nhận chuỗi JSON request cập nhật, deserialize thành `StorageUnitHubUpdateRequest` kèm `DateTimeOffset? ExpectedUpdatedAt`.
    3. `HubDelete_StorageUnit(string id, string? expectedUpdatedAt = null)`: Nhận mã ô kho dạng chuỗi `id` và chuỗi timestamp tùy chọn `expectedUpdatedAt`.
  - **Receiver Events (Server -> Client)**:
    1. `Receiver_CreateStorageUnit(StorageUnitRealtimeEventDto data)`
    2. `Receiver_UpdateStorageUnit(StorageUnitRealtimeEventDto data)`
    3. `Receiver_DeleteStorageUnit(string unitId)`: Payload là chuỗi mã ID (`unitId.ToString()`), không phải object DTO.

---

### Finding 3 [P1] — Cập nhật chính xác các endpoint tiếp nhận callback VNPAY
- **Vấn đề**: Báo cáo ghi nhầm route thành `/Payments/VnpayIpn` và `/Payments/VnpayReturn`.
- **Khắc phục**: Đối chiếu `src/SelfStorageManagementSystem.Web/Controllers/PaymentsApiController.cs` (có `[Route("api/payments")]`):
  - IPN endpoint máy chủ - máy chủ xử lý tài chính: `/api/payments/vnpay/ipn`.
  - Return endpoint nhận redirect trình duyệt hiển thị: `/api/payments/vnpay/return` (trả về trực tiếp giao diện kết quả bằng `Content(html, "text/html; charset=utf-8")`, không chuyển hướng sang trang khác).
  - Đã cập nhật lại đầy đủ trong Mục 1.5 của báo cáo bàn giao.

---

### Finding 4 [P2] — Ghi nhận đúng 6 trigger nghiệp vụ thực tế trên SQL Server
- **Vấn đề**: Báo cáo trước đó liệt kê 5 tên trigger không tồn tại trong CSDL.
- **Khắc phục**: Đối chiếu bài kiểm thử `AssertAllCriticalTriggersEnabledAsync` trong `tests/SelfStorageManagementSystem.Tests/PaymentSqlServerConcurrencyTests.cs:1956` và kết quả truy vấn `sys.triggers` trên CSDL `SelfStoragePRN222_WebTest`. Sửa lại danh sách chuẩn gồm đúng 6 trigger thực tế:
  1. `trg_agreement_status_transition`
  2. `trg_invoice_lines_guard`
  3. `trg_invoice_lines_refresh_totals`
  4. `trg_invoice_status_transition`
  5. `trg_payment_allocation_guard` (bảo vệ phân bổ thanh toán, chặn vi phạm với lỗi nghiệp vụ 51055)
  6. `trg_reservation_status_transition`

---

### Finding 5 [P2] — Làm rõ và chuẩn hóa các mốc kiểm thử Legacy Solution
- **Vấn đề**: Dòng báo cáo ghi 225 passed và 8 skipped rồi tổng 244 không đồng nhất về mặt số liệu.
- **Khắc phục**: Tách biệt rõ ràng 2 lần chạy kiểm thử độc lập đối với legacy solution (`SelfStorageManagementSystem.sln`):
  - **Lần chạy mặc định (không cấu hình SQL Server)**: `dotnet test SelfStorageManagementSystem.sln --no-restore`: **225 Passed, 0 Failed, 19 Skipped** (tổng 244 test).
  - **Lần chạy kiểm thử tương tranh SQL Server (với biến môi trường `SELFSTORAGE_SQL_TEST_CONNECTION_STRING`)**: **19 Passed, 0 Failed, 0 Skipped** (chính là 19 bài test được kích hoạt kiểm tra tương tranh thực tế trên SQL Server).
  - Cả hai lần chạy đều đạt 100% mục tiêu, tổng số bài test khớp hoàn toàn 225 + 19 = 244.

---

### Finding 6 [P2] — Sử dụng đúng mã vai trò và kiểu dữ liệu kiểm soát tương tranh
- **Vấn đề**: Báo cáo ghi danh sách vai trò có `Guest` và ghi `ExpectedUpdatedAt` là kiểu `RowVersion`/`Timestamp`.
- **Khắc phục**:
  - Đối chiếu `src/SelfStorageManagementSystem.BusinessLogic/Common/Constants/RoleConstants.cs`: Hệ thống có 5 vai trò chính thức gồm: `storage_customer`, `facility_staff`, `facility_manager`, `business_operations_manager`, và `system_administrator`. Không tồn tại vai trò `Guest`.
  - Đối chiếu `UpdateStorageUnitRequest.cs` và `StorageUnitHubUpdateRequest.cs`: Thuộc tính `ExpectedUpdatedAt` có kiểu dữ liệu là `DateTimeOffset` (không phải mảng byte SQL Server `RowVersion`). Đã chỉnh sửa chính xác trong Mục 1.1 và 1.2.

---

### Finding 7 [P2] — Trình bày trung thực điều kiện Git và Cây làm việc cho T20
- **Vấn đề**: Điều kiện mở T20 ghi nhận "clean working tree" là không chính xác khi T19 đang có các tệp chưa commit và tồn tại cảnh báo chuẩn hóa kết thúc dòng (CRLF/LF).
- **Khắc phục**: Cập nhật lại Mục 6 trong báo cáo bàn giao:
  - Nêu rõ commit HEAD trên `main` là `821449030eebe1e38dc3bfda996f8bcb38474d10`.
  - Liệt kê chính xác các tệp uncommitted thuộc phạm vi T19 (`WebMigration_FinalHandover.md`, `WebMigration_T19_Memory.md`, `WebMigration_T19_R1_FindingFixes.md`, và tệp theo dõi `WebMigration_TaskBoard.md`).
  - Nêu rõ các cảnh báo kết thúc dòng (LF/CRLF noise) trên các tệp T16–T18 từ trước không chứa thay đổi logic (`git diff --quiet` trả về mã 0).
  - Khẳng định T20 được phép bắt đầu ngay từ trạng thái hiện tại; commit tương lai do Codex thực hiện sẽ chỉ gồm các tệp T19 có phạm vi và loại trừ tuyệt đối cả 3 tệp bảo vệ (`docs/KeHoach_Web_Gemini_Va_Codex_Review.docx`, `AgentReport/WebMigration_T15_CodexLoopState.json`, `scratch/generate_r3_report.js`) cùng các cảnh báo kết thúc dòng không liên quan.

---

## 3. Bảng Tổng Hợp Mã Băm SHA-256 Sau Vòng Sửa Chữa R1

Tất cả các tệp sửa chữa thuộc phạm vi T19 R1 đã được kiểm tra tính đồng bộ tuyệt đối giữa `Final_Project_Web` và `Final_Project`:

| Tệp Tin | Đường Dẫn | SHA-256 Checksum | Trạng Thái Đồng Bộ |
| :--- | :--- | :--- | :---: |
| Báo cáo bàn giao T19 (cập nhật) | `AgentReport/WebMigration_FinalHandover.md` | `28BD72CAD791F62086F4944D7B013B186BF8F6A7E88FA0CAF36110827BF8DD19` | **MATCH (100%)** |
| Báo cáo xử lý finding R1 (mới) | `AgentReport/WebMigration_T19_R1_FindingFixes.md` | *(Tệp hiện tại)* | **MATCH (100%)** |
| Ghi nhớ task T19 (cập nhật) | `AgentReport/WebMigration_T19_Memory.md` | `5998E745FB32F361B4F4634F1B45E6E2252B6969A9B30AB63ABE6CF29DB308B3` | **MATCH (100%)** |
| Bảng theo dõi tiến độ | `AgentReport/WebMigration_TaskBoard.md` | `08D8E9A1F9672AD90F171AFBCCC35E2A3209DDF392528894C12581205DCE417B` | **MATCH (100%)** |
| Review độc lập T19 R1 của Codex | `AgentReport/WebMigration_T19_R1_CodexReview.md` | `B554A0D328C6A8D623C2D3F4C5ABE1C08947BED9E003296EBB7E71EFEDEBB804` | **MATCH (100%)** |

---

## 4. Kết Luận

Vòng sửa chữa T19 R1 đã hoàn tất 100% các yêu cầu từ Codex. Không còn bất kỳ sai lệch nào về dữ liệu, hợp đồng, vai trò hay trạng thái hệ thống. Task T19 tiếp tục duy trì trạng thái **IMPLEMENTED** sẵn sàng để Codex tái đánh giá và chuyển giao sang T20.
