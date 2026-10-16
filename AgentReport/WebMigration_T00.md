# Báo cáo Thực hiện Task T00: Tạo Bản Save As và Ghi Nhận Baseline

- **Task ID**: T00
- **Tên Task**: Tạo bản Save As và ghi nhận baseline
- **Owner**: Gemini A
- **Đợt**: 0
- **Chờ (Dependencies)**: Không (Task khởi đầu chuỗi di chuyển)
- **Status**: **VERIFIED**
- **Contract Version**: v0 (Baseline hệ thống hiện hành)
- **Thời điểm thực hiện**: 2026-10-15 15:21:36 +07:00
- **Môi trường**: Windows 11, .NET SDK 8.0.425 / 10.0.401, PowerShell 5.1

---

## 1. Mục Tiêu và Phạm Vi Task

1. Tạo không gian làm việc độc lập Save As tại `D:\FPT\PRN222\Project\Final_Project_Web` từ working tree thực tế của `D:\FPT\PRN222\Project\Final_Project`.
2. Bảo toàn toàn bộ mã nguồn đang phát triển dở dang, bao gồm các file đã sửa đổi (modified) và các file chưa được theo dõi (untracked) của Function 5 và TeacherCode.
3. Giữ thư mục nguồn `D:\FPT\PRN222\Project\Final_Project` ở trạng thái **chỉ đọc (Read-Only)**, tuyệt đối không chỉnh sửa, không commit, không push, không merge, không reset.
4. Loại trừ triệt để các thư mục và tập tin không thuộc phạm vi mã nguồn: `.git`, `.vs`, `bin`, `obj`, `*.user`, file khóa tạm thời của Office (`~$*`) và dữ liệu cấu hình bí mật / cá nhân.
5. Tạo bản lưu vết Manifest (`AgentReport/WebMigration_BaselineManifest.json`) lưu đường dẫn tương đối và mã băm SHA-256 cho từng file, đối chiếu 1-1 giữa nguồn và bản sao.
6. Thực hiện `dotnet restore`, `dotnet build` và chạy kiểm thử baseline trên bản sao để ghi nhận kết quả thực tế, phân biệt rõ kiểm thử thường và kiểm thử SQL Concurrency bị skip.

---

## 2. Kết Quả Kiểm Tra Ban Đầu và Thực Thi

### 2.1. Xác nhận thư mục đích
- Kiểm tra đường dẫn đích: `D:\FPT\PRN222\Project\Final_Project_Web`.
- Kết quả: Thư mục đích **chưa tồn tại** trước khi chạy T00. Quá trình tạo mới được thực hiện sạch sẽ, đảm bảo nguyên tắc không ghi đè dữ liệu có sẵn.

### 2.2. Kiểm toán và phân loại working tree nguồn
- **Workspace nguồn**: `D:\FPT\PRN222\Project\Final_Project`
- **Git Branch**: `Check-in-and-Digital-Handover`
- **Git Commit HEAD**: `ba599208badb2f0e011ae65355d088d0374f1acd`
- **Tổng số file quét được trong working tree nguồn**: 1833 file.

#### Danh sách các nhóm bị loại trừ (Excluded): 1533 file
| Nhóm loại trừ | Số lượng | Diễn giải lý do loại trừ |
| :--- | :--- | :--- |
| `.git/` | 185 | Cơ sở dữ liệu và cấu hình Git version control nội bộ |
| `.vs/` | 19 | Thư mục cấu hình cục bộ và file nhị phân `.suo` của Visual Studio |
| `bin/` | 1120 | Thư mục chứa file nhị phân đã biên dịch (*\bin\*) |
| `obj/` | 207 | Thư mục chứa trung gian biên dịch và project assets (*\obj\*) |
| `*.user` | 1 | File cấu hình môi trường cá nhân Visual Studio (`SelfStorageManagementSystem.Presentation.csproj.user`) |
| `Office Lock (~$*)` | 1 | File khóa tạm thời do Word sinh ra (`docs/~$Hoach_Web_Gemini_Va_Codex_Review.docx`) |
| `Secrets / Credentials` | 0 | Đã rà soát: không có `.env`, `*.key`, `*.pem`, `*.pfx`, mật khẩu hay connection string cá nhân lưu trong repo |

#### Danh sách các nhóm được sao chép (Included): 300 file
| Phân nhóm thư mục | Số lượng file | Nội dung chính |
| :--- | :--- | :--- |
| Root files | 5 | `.gitignore`, `CODEX_RULE.md`, `FER202-SE2041-FA26.xlsx`, `README.md`, `SelfStorageManagementSystem.sln` |
| `Agent/` | 3 | `Agent/Codex/CODEX_RULE.md`, `Agent/Gemini/RULE.md`, `Agent/Gemini/FUNCTION5_BACKEND_PROMPT.md` |
| `AgentReport/` | 19 | Các báo cáo hoàn thành Function 1-5 và tài liệu sửa lỗi |
| `DB/` | 2 | `SelfStoragePRN222_SQLServer_CleanInstall.sql`, `SelfStoragePRN222_SQLServer_Function4_Upgrade_OneRun.sql` |
| `docs/` | 8 | Toàn bộ tài liệu kiến trúc, database schema, roadmap và kế hoạch web migration docx |
| `src/` | 226 | Mã nguồn 3 lớp: `Presentation`, `BusinessLogic`, `DataAccess`, kèm `WpfClient` |
| `SRS/` | 5 | Tài liệu phân tích nghiệp vụ, biểu đồ, state chart và rules nhóm |
| `TeacherCode/` | 10 | Mã mẫu giảng viên (1.txt đến 5.txt, 10092026.txt, thư mục Account) |
| `tests/` | 22 | Bộ kiểm thử tích hợp, unit test và SQL concurrency test hiện hữu |
| **Tổng cộng** | **300** | **Toàn bộ được sao chép và đối chiếu hash SHA-256 khớp 100%** |

---

## 3. Kiểm Tra Tính Đầy Đủ (Integrity Check)

### 3.1. Mã nguồn Function 5 (Check-in & Digital Handover)
Đã kiểm tra và đối chiếu sự tồn tại cùng mã băm SHA-256 của toàn bộ mã nguồn Function 5 chưa commit:
- `src/SelfStorageManagementSystem.BusinessLogic/Services/Implementations/CheckInService.cs` (`f168ed2f848133a0e38fdb2380adb3605a979a59577a00c72503dcf906f563e1`): **ĐẦY ĐỦ**
- `src/SelfStorageManagementSystem.BusinessLogic/Services/Interfaces/ICheckInService.cs` (`bce513b186b785b9f2719c5423dc873f886286f386fdde41244e2d53416fa643`): **ĐẦY ĐỦ**
- `src/SelfStorageManagementSystem.DataAccess/Repositories/Implementations/CheckInRepository.cs` (`1b5456d680a882bf42951d2965416e5d2540cbaf884097f885178b34a5490b85`): **ĐẦY ĐỦ**
- `src/SelfStorageManagementSystem.DataAccess/Repositories/Interfaces/ICheckInRepository.cs` (`ea713830e2ac121989ec8a5f4d44a863a0e1c4d7ed9de29e754e998dcdbdb835`): **ĐẦY ĐỦ**
- `src/SelfStorageManagementSystem.Presentation/Controllers/CheckInController.cs` (`325bf4ba84aa8b4c9235a8dd39cf5ae126e5353d3011544dfa46fabb8e53efa7`): **ĐẦY ĐỦ**
- `src/SelfStorageManagementSystem.Presentation/Controllers/MeController.cs` (`e247cd35e66a89b583662e8692da895d048ce3996a76e984b66d4f0fba557f97`): **ĐẦY ĐỦ**
- `tests/SelfStorageManagementSystem.Tests/CheckInIntegrationTests.cs` (`c427573f6360e1c6db7132954c4b4d6d4165f52c8a717dba88271c445c6399c9`): **ĐẦY ĐỦ**
- `tests/SelfStorageManagementSystem.Tests/CheckInServiceTests.cs` (`8838e7ce6a2e437e04eb4683f9fb72da89066be8037e8af20c363c4971861f1e`): **ĐẦY ĐỦ**
- `tests/SelfStorageManagementSystem.Tests/CheckInSqlServerConcurrencyTests.cs` (`343e2c7de3cda6d5996e26e855a71be95aed79b64d0e4bf30ed55ae84df1b555`): **ĐẦY ĐỦ**
- Thư mục `src/.../DTOs/Requests/CheckIn/` (6 files): **ĐẦY ĐỦ**
- Thư mục `src/.../DTOs/Responses/CheckIn/` (10 files): **ĐẦY ĐỦ**
- Thư mục `src/.../DataAccess/Common/` (1 file): **ĐẦY ĐỦ**
- Các tài liệu báo cáo Function 5 (`DoneFunction5.md`, `DoneFunction5Backend.md`, `DoneFunction5BackendFix.md`, `Function5BackendBugVerification.md`, `Function5BackendFixPrompt.md`, `Function5BackendPreparation.md`, `Agent/Gemini/FUNCTION5_BACKEND_PROMPT.md`): **ĐẦY ĐỦ**

### 3.2. Mã nguồn mẫu giảng viên (TeacherCode)
Đã kiểm tra và đối chiếu sự tồn tại cùng mã băm SHA-256 của toàn bộ file TeacherCode từ manifest đã kiểm chứng:
- `TeacherCode/1.txt` (`8fcc864dd19a0abf606042ded01b8e63c4f59d8724bc9db92dba620f94bc4c2e`): **ĐẦY ĐỦ**
- `TeacherCode/2.txt` (`f064482a0f4338fd80d715dd5cae32f35703dcf59dbc43c6021380188c4d57ac`): **ĐẦY ĐỦ**
- `TeacherCode/3.txt` (`9127376793e24f470848bb1776e49c543d576d3c8f2b49ef93bc0ea664336ea2`): **ĐẦY ĐỦ**
- `TeacherCode/4.txt` (`8ec8ce70c46fbc5d489be2dec5f332559f6020eefb97736a5cf25d10f0c4a121`): **ĐẦY ĐỦ**
- `TeacherCode/5.txt` (`51be8be3b35e2160563421409f73e8b0c123421729240897c581916a8cb10166`): **ĐẦY ĐỦ**
- `TeacherCode/10092026.txt` (`9ada888cad9f417e4f105070f77b3ea8c626b191ab8edfb15972b75a5146f5e8`): **ĐẦY ĐỦ**
- `TeacherCode/AccountController.cs` (`5c5ca987aa9e2f0d79c1270da083407d7409faa336d69bd2182ce430c7c43c3f`): **ĐẦY ĐỦ**
- `TeacherCode/LoginRequest.cs` (`5e26266ad9baf9c7415e682fa8b3b521f125ff21028fcbdf4618a2c5275656da`): **ĐẦY ĐỦ**
- Thư mục `TeacherCode/Account/` (2 files): **ĐẦY ĐỦ**

---

## 4. Kiểm Tra Tính Bất Biến Của Thư Mục Nguồn (Source Immutability)

- Lệnh thực thi: `git -C "D:\FPT\PRN222\Project\Final_Project" status --porcelain`
- Kết quả trước và sau khi hoàn tất T00: **Hoàn toàn trùng khớp 100%**.
- Không có bất kỳ file nào trong thư mục nguồn bị chỉnh sửa, xóa, stage hay tạo mới. Nguyên tắc **Read-Only** đối với thư mục nguồn được tuân thủ nghiêm ngặt.

---

## 5. Kết Quả Restore, Build và Kiểm Thử Trên Bản Sao

Mọi thao tác được thực hiện bên trong không gian làm việc bản sao `D:\FPT\PRN222\Project\Final_Project_Web`:

### 5.1. Khôi phục gói (Restore)
- Lệnh: `dotnet restore SelfStorageManagementSystem.sln`
- Kết quả: **Thành công (Exit code 0)**.
- Đã khôi phục toàn bộ dependencies cho 5 project: `SelfStorageManagementSystem.BusinessLogic`, `SelfStorageManagementSystem.DataAccess`, `SelfStorageManagementSystem.Presentation`, `SelfStorageManagementSystem.WpfClient`, `SelfStorageManagementSystem.Tests`.

### 5.2. Biên dịch Solution (Build)
- Lệnh: `dotnet build SelfStorageManagementSystem.sln --no-restore`
- Kết quả: **Thành công (Exit code 0)**.
- Trạng thái: **0 Error(s)**, **91 Warning(s)** (gồm đúng **11 cảnh báo CS8981** do các entity DB-first đặt tên kiểu toàn chữ thường trong `DataAccess/Entities` và **80 cảnh báo EF1002** do chuỗi nội suy trong `ExecuteSqlRawAsync` tại bộ test SQL concurrency cũ).
- Thời gian biên dịch: 10.63 giây.

### 5.3. Chạy bộ kiểm thử thường (Excluding SqlServerConcurrencyTests)
- Lệnh: `dotnet test SelfStorageManagementSystem.sln --filter "FullyQualifiedName!~SqlServerConcurrencyTests" --no-build`
- Kết quả thực tế:
  ```text
  Passed!  - Failed: 0, Passed: 208, Skipped: 0, Total: 208, Duration: 2 s
  ```
- Toàn bộ **208 / 208 test thường đã PASS 100%**, không có bất kỳ test nào thất bại hoặc bị bỏ qua. Kết quả này phản ánh chính xác trạng thái baseline đã được ghi nhận trong kế hoạch chuyển đổi.

### 5.4. Chạy toàn bộ bộ kiểm thử (Ghi nhận kết quả SQL riêng biệt)
- Lệnh: `dotnet test SelfStorageManagementSystem.sln --no-build`
- Kết quả thực tế:
  ```text
  Passed!  - Failed: 0, Passed: 208, Skipped: 8, Total: 216, Duration: 2 s
  ```
- **Chi tiết 8 bài test bị Skipped**:
  1. `SelfStorageManagementSystem.Tests.CheckInSqlServerConcurrencyTests.ConcurrentCompleteCheckIn_SameReservation_ExactlyOneExecutes_OtherHandlesSafely` [SKIP]
  2. `SelfStorageManagementSystem.Tests.CheckInSqlServerConcurrencyTests.ReissuePin_PersistsRealCredentialIdInAuditLog_OnSqlServer` [SKIP]
  3. `SelfStorageManagementSystem.Tests.CheckInSqlServerConcurrencyTests.CompleteCheckIn_RollbackOnFailure_LeavesNoOrphanRecords` [SKIP]
  4. `SelfStorageManagementSystem.Tests.CheckInSqlServerConcurrencyTests.ConcurrentAssignUnit_TwoReservationsCompetingForSameUnit_ExactlyOneSucceeds` [SKIP]
  5. `SelfStorageManagementSystem.Tests.PaymentSqlServerConcurrencyTests.ConcurrentCheckout_SameReservation_DifferentIdempotencyKeys_ExactlyOneSucceeds` [SKIP]
  6. `SelfStorageManagementSystem.Tests.PaymentSqlServerConcurrencyTests.ConcurrentRefundReview_SameRefund_ExactlyOneSucceedsAndOneFailsWithControlledConflict` [SKIP]
  7. `SelfStorageManagementSystem.Tests.PaymentSqlServerConcurrencyTests.ConcurrentCheckoutRetry_AndLateIpnOnOldAttempt_RoutesOldPaymentToReconciliation` [SKIP]
  8. `SelfStorageManagementSystem.Tests.ReservationSqlServerConcurrencyTests.ConcurrentReservationHold_WhenOnlyOneSlotAvailable_ExactlyOneSucceedsAndOneFailsWithControlledConflict` [SKIP]
- **Lý do Skipped**: Thuộc tính kiểm thử `[SqlIntegrationFact]` chủ động bỏ qua bài test khi biến môi trường `SELFSTORAGE_SQL_TEST_CONNECTION_STRING` chưa được thiết lập trên máy cục bộ, nhằm ngăn ngừa việc chạy concurrency test làm hỏng dữ liệu database chính.

---

## 6. Hồ Sơ Bàn Giao

1. **Thư mục snapshot bản sao độc lập**:
   `D:\FPT\PRN222\Project\Final_Project_Web`
2. **File Manifest Baseline**:
   `D:\FPT\PRN222\Project\Final_Project_Web\AgentReport\WebMigration_BaselineManifest.json` (116,750 bytes, ghi nhận đầy đủ 300 files và mã băm SHA-256).
3. **Báo cáo kết quả T00**:
   `D:\FPT\PRN222\Project\Final_Project_Web\AgentReport\WebMigration_T00.md` (Tập tin này).

---

## 7. Kết Luận và Điều Kiện Nghiệm Thu

| Tiêu chuẩn nghiệm thu | Trạng thái | Minh chứng |
| :--- | :---: | :--- |
| Thư mục đích chưa tồn tại trước khi chạy | **ĐẠT** | Đã kiểm tra không có dữ liệu, tạo mới sạch sẽ |
| Không thiếu code Function 5 hoặc TeacherCode mới | **ĐẠT** | 100% file Function 5 và TeacherCode (cả 4.txt, 5.txt) đã hiện diện và khớp hash |
| Loại trừ sạch .git, .vs, bin, obj, *.user, secrets, temp lock | **ĐẠT** | 1533 file được phân loại và loại trừ chính xác |
| Thư mục nguồn giữ nguyên không thay đổi (Read-Only) | **ĐẠT** | `git status --porcelain` nguồn giống hệt trước khi làm |
| Bản sao build thành công | **ĐẠT** | `dotnet build` đạt 0 Error |
| Baseline có kết quả test thật và phân biệt skipped | **ĐẠT** | 208 Passed (100% test thường), 8 Skipped (SQL test do thiếu env var) |

**Kết luận**: Task T00 đã hoàn tất xuất sắc và đạt trạng thái **VERIFIED**. Sẵn sàng bàn giao đầu vào cho **Task T01** (Tạo solution web và chốt contract).
