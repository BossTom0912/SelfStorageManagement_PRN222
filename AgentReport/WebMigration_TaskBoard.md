# Bảng Theo Dõi Tiến Độ Chuyển Đổi Web (Web Migration Task Board)

- **Cập nhật lần cuối**: 2026-10-16 (Codex nghiệm thu T10: 26/26 T10, 30/30 Reservation Domain, 252/252 Web PASS; T09/T10/T11 VERIFIED; T12/T13/T14 READY)
- **Đầu mối duy trì**: Gemini A (Foundation & Integration Lead)
- **Không gian làm việc**: `D:\FPT\PRN222\Project\Final_Project_Web`
- **Solution Web**: `SelfStorageManagementSystem.Web.sln`

---

## 1. Trạng Thái Tổng Thể Các Đợt

```text
[Đợt 0: Nền Tảng]      T00 (VERIFIED) ──> T01 (IMPLEMENTED — Chờ Codex review)
                                           │
                       ┌───────────────────┼───────────────────┐
                       ▼                   ▼                   ▼
[Đợt 1: Song Song 1]  T02 (VERIFIED)      T03 (VERIFIED)      T04 (VERIFIED)
                      (Gemini A)          (Gemini B)          (Gemini C)
                       │                   │                   │
                        ├───────────────────┼───────────────────┤
                        ▼                   ▼                   ▼
[Đợt 2: Song Song 2]  T05 [VERIFIED]      T06 [VERIFIED]      T07 [VERIFIED]
                      (Gemini A)          (Gemini B)          (Gemini C)
                        │                   │                   │
                        └───────────────────┼───────────────────┘
                                            ▼
[Đợt 3: Bàn Giao Nền]                     T08 [VERIFIED]
                                          (Gemini A)
                                           │
                       ┌───────────────────┼───────────────────┐
                       ▼                   ▼                   ▼
[Đợt 4: Song Song 3]  T09 (VERIFIED)      T10 (VERIFIED)      T11 (VERIFIED)
                      (Gemini B)          (Gemini A)          (Gemini C)
                       │                   │                   │
                       │                   ▼                   ▼
[Đợt 5: Song Song 4]   │                  T12 (READY)         T14 (READY)
```

---

## 2. Bảng Theo Dõi Chi Tiết Từng Task (T00 - T22)

| Mã Task | Tên Task | Đợt | Owner | Trạng Thái | Phụ Thuộc (Dependencies) | Báo Cáo (Report) | Bằng Chứng / Ghi Chú (Evidence) |
| :---: | :--- | :--- :---: | :---: | :---: | :---: | :--- | :--- |
| **T00** | Tạo bản Save As và ghi nhận baseline | 0 | Gemini A | **VERIFIED** | Không | [`WebMigration_T00.md`](file:///D:/FPT/PRN222/Project/Final_Project_Web/AgentReport/WebMigration_T00.md) | Snapshot `Final_Project_Web`, 300 files khớp SHA-256; 208/208 test thường PASS; 8 SQL skip. |
| **T01** | Tạo solution web và chốt contract | 0 | Gemini A | **IMPLEMENTED** | T00 | [`WebMigration_T01.md`](file:///D:/FPT/PRN222/Project/Final_Project_Web/AgentReport/WebMigration_T01.md) | Chờ Codex re-review. `SelfStorageManagementSystem.Web.sln`, 14/14 WebTests PASS; 208/208 regression PASS; 2 finding P2 (HMAC config docs, exact 8 Admin methods) đã xử lý xong. |
| **T02** | Tách xác thực tài khoản khỏi JWT | 1 | Gemini A | **VERIFIED** | T01 | [`WebMigration_T02.md`](file:///D:/FPT/PRN222/Project/Final_Project_Web/AgentReport/WebMigration_T02.md) | Đã fix Codex P1. `IAccountIdentityService` tách rời JWT; DAL projection/AsNoTracking snapshot; lọc phân công theo active status & roles; 6/6 ZeroJwt tests PASS; 15/15 AccountIdentity tests PASS; 54/54 WebTests PASS; 223/223 regression PASS. |
| **T03** | Repository quản trị ô kho | 1 | Gemini B | **VERIFIED** | T01 | [`WebMigration_T03.md`](file:///D:/FPT/PRN222/Project/Final_Project_Web/AgentReport/WebMigration_T03.md) | `IStorageUnitAdminRepository` hoàn tất, đã fix 2 finding Codex P1 & P2 (applyUpdates chạy đúng 1 lần với staged mutation plan, try/finally semaphore release chống rò rỉ khi hủy/timeout, target ID validation, distinct DbContexts); 18/18 WebTests PASS; 59/59 Web sln PASS; 223/223 regression PASS. |
| **T04** | Layout Bootstrap và giao diện chung | 1 | Gemini C | **VERIFIED** | T01 | [`WebMigration_T04.md`](file:///D:/FPT/PRN222/Project/Final_Project_Web/AgentReport/WebMigration_T04.md) | Shared layout Bootstrap 5.3.3 cho MVC/Razor, role menu 4 nhóm, partials, safe error, AppFormatters UTC sang múi giờ VN & đa vai trò ưu tiên; 21/21 LayoutTests PASS, 48/48 WebTests PASS, 217/217 regression PASS. Bàn giao layout về Gemini A. |
| **T05** | Cookie Login Register Logout và Forbidden | 2 | Gemini A | **VERIFIED** | T02, T04 | [`WebMigration_T05.md`](file:///D:/FPT/PRN222/Project/Final_Project_Web/AgentReport/WebMigration_T05.md) | MVC Account đầy đủ 5 vai trò; 0 JWT; Cookie 5m Lax HttpOnly; ValidatePrincipal live refresh/revoke; đã fix Codex P1 thu hồi quyền nhân sự inactive/terminated không ngoại lệ Admin; 34/34 AccountCookieAuth tests PASS; 159/159 Web sln PASS; 208 passed, 8 skipped, total 216 regression PASS. |
| **T06** | Service nghiệp vụ quản trị ô kho | 2 | Gemini B | **VERIFIED** | T02, T03 | [`WebMigration_T06.md`](file:///D:/FPT/PRN222/Project/Final_Project_Web/AgentReport/WebMigration_T06.md) | Đã giải quyết Task T06-F02 (MapToDetailDto không fallback sang rate hết hạn/tương lai, [valid_from, valid_to), 0m fallback, TimeProvider + facility timezone); 58/58 StorageUnitAdminService tests PASS; 167/167 Web sln PASS; 208/216 regression PASS. Nghiệm thu hoàn tất tại T08. |
| **T07** | Catalog bộ lọc và sơ đồ ô kho | 2 | Gemini C | **VERIFIED** | T04 | [`WebMigration_T07.md`](file:///D:/FPT/PRN222/Project/Final_Project_Web/AgentReport/WebMigration_T07.md) | Razor Pages Catalog Index & Details (4 tabs: Loại kho & Bảng giá, Sơ đồ SVG tự động tính viewbox từ DB thật, Bảng ô ứng viên phân trang, Thông tin cơ sở). Phân biệt capacity hold với physical status (BR-RSV-03). ReturnUrl nội bộ. Đã fix 4 findings Codex (XSS SVG text, XSS catalog.js, ngày chuẩn hóa UI/query/link, 404 response cho missing facility). 16/16 CatalogRazorPagesTests PASS; 159/159 Web sln PASS; 208/216 regression PASS (8 SQL skip). |
| **T08** | Tích hợp nền và kiểm tra điểm bàn giao | 3 | Gemini A | **IMPLEMENTED** | T05, T06, T07 | [`WebMigration_T08.md`](file:///D:/FPT/PRN222/Project/Final_Project_Web/AgentReport/WebMigration_T08.md) | Contract v1 chốt; Tích hợp trọn vẹn T05/T06/T07; Đã hoàn tất T08-F01 ([Authorize] cho StorageUnitHub), T08-F02 (chuẩn hóa test pipeline EF Core InMemory, SQL Server chờ T17), T08-F03 (đưa StorageUnitHub vào architecture test), T08-F04 (đồng bộ tài liệu & SHA-256); 189/189 WebTests PASS; 225 PASS / 8 SKIP (tổng 233) regression; Tách biệt hoàn toàn API Presentation Program; Zero JWT secret startup; Chờ reviewer nghiệm thu. |
| **T09** | Razor Pages CRUD ô kho bằng form | 4 | Gemini B | **VERIFIED** | T08 | [`WebMigration_T09.md`](file:///D:/FPT/PRN222/Project/Final_Project_Web/AgentReport/WebMigration_T09.md) | Razor Pages CRUD `/StorageUnits` (Index/Create/Edit/Details/Delete); Refactor 100% từ scaffold sang IStorageUnitAdminService; Zero DbContext/Repository in Presentation (Architecture 3/3 PASS); Form POST kèm Antiforgery & validation; GET Delete an toàn; Flow Create giữ facilityId từ Index & dynamic lookup Area/UnitType; POST Delete ngoài scope từ chối có kiểm soát (không 500); Facility/UnitType bất biến khi Edit; Concurrency round-trip ExpectedUpdatedAt; Chặn duplicate code (409) & scope breach (403); Chuẩn bị ID dòng ổn định & message area cho T13; Chống double submit; lookup DOM textContent chống HTML injection, AbortController + kiểm tra request/facility hiện hành, khóa submit trong lúc nạp; browser desktop/mobile PASS; 18/18 StorageUnitsRazorPagesTests PASS; 244/244 Web sln PASS. |
| **T10** | Reservation countdown và worker hết hạn | 4 | Gemini A | **VERIFIED** | T08 | [`WebMigration_T10.md`](file:///D:/FPT/PRN222/Project/Final_Project_Web/AgentReport/WebMigration_T10.md) | Đã giải quyết 4 findings ban đầu, 2 findings re-review, và finding CDP test harness: (1) Khối auth alert riêng (`#reservationAuthAlert`) hiển thị cho 401/403, khối `#reservationExpiredAlert` chỉ hiện khi server khẳng định hết hạn; (2) E2E browser qua CDP WebSocket: countdown live giảm thực tế, reload giữ nguyên deadline và không sinh bản ghi duplicate, kiểm chứng độc lập 2 múi giờ VN (+7) và UTC (+0), cùng Negative Control khi tắt JS; (3) Sửa CDP test harness: xử lý `exceptionDetails` và `subtype error` (ném lỗi thay vì âm thầm trả null), phân biệt `ExecuteScriptAsync` và `EvaluateStringRequiredAsync`, strict parse countdown format `mm:ss`, negative control xác nhận cả 2 mẫu hợp lệ trước khi so sánh, thêm 2 test kiểm chứng lỗi harness. Kết quả: 11/11 Sandbox PASS; 26/26 T10 WebTests PASS; 30/30 Reservation Domain PASS; 252/252 Web Solution PASS. Codex review lại: No findings; chạy lại 26/26 T10, 30/30 Domain và 252/252 Web PASS, 0 Failed, 0 Skipped. T10 VERIFIED; T12 READY. |
| **T11** | Quầy check in inspection và bàn giao | 4 | Gemini C | **VERIFIED** | T08 | [`WebMigration_T11.md`](file:///D:/FPT/PRN222/Project/Final_Project_Web/AgentReport/WebMigration_T11.md) | Sửa phạm vi bằng chứng hàng 3: chỉ Admin/Operations; hàng 7 kiểm tra chưa phân công. Thêm test HTTP terminated với phiên/token có sẵn: GET và POST VerifyId bị chặn, không lộ đơn/giấy tờ, không ghi xác minh; giữ nguyên nghiệp vụ. 19/19 T11 và 244/244 Web PASS. |
| **T12** | Thanh toán hóa đơn hợp đồng và refund | 5 | Gemini A | **READY** | T10 | `WebMigration_T12.md` | T10 đã được Codex nghiệm thu độc lập, đủ điều kiện bắt đầu. |
| **T13** | SignalR Create Update Delete theo mẫu thầy | 5 | Gemini B | **READY** | T09 | `WebMigration_T13.md` | Sẵn sàng triển khai sau khi T09 VERIFIED. |
| **T14** | Quản trị tài khoản và trang của khách | 5 | Gemini C | **READY** | T11 | `WebMigration_T14.md` | Sẵn sàng triển khai sau khi T11 VERIFIED. |
| **T15** | Tích hợp realtime cả năm chức năng | 6 | Gemini A | **WAITING** | T12, T13, T14 | `WebMigration_T15.md` | Chờ T12, T13, T14 VERIFIED. |
| **T16** | Kiểm thử Cookie quyền và HTTP | 7 | Gemini A | **WAITING** | T15 | `WebMigration_T16.md` | Chờ T15 smoke pass. |
| **T17** | Kiểm thử SQL concurrency và rollback | 7 | Gemini B | **WAITING** | T15 | `WebMigration_T17.md` | Chờ T15 smoke pass. |
| **T18** | Kiểm thử hai trình duyệt và luồng giao diện | 7 | Gemini C | **WAITING** | T15 | `WebMigration_T18.md` | Chờ T15 smoke pass. |
| **T19** | Đóng gói bàn giao cho Codex | 8 | Gemini A | **WAITING** | T16, T17, T18 | `WebMigration_FinalHandover.md` | Chờ T16, T17, T18 bàn giao kết quả. |
| **T20** | Review độc lập sau bàn giao Gemini | 8 | Codex | **WAITING** | T19 | `WebMigration_CodexReview.md` | Độc lập thực hiện bởi Codex. |
| **T21** | Sửa các finding Codex đã giao | 9 | Gemini A/B/C | **WAITING** | T20 | `WebMigration_FindingFixes.md` | Có điều kiện (chỉ khi có finding). |
| **T22** | Review lại và kết luận nghiệm thu | 9 | Codex | **WAITING** | T21 | `WebMigration_CodexFinalReview.md` | Kết luận Accepted/Changes Requested. |

---

## 3. Quy Ước Trạng Thái (Lifecycle Convention)
- **WAITING**: Chưa đủ điều kiện mở do phụ thuộc vào các task trước chưa hoàn thành.
- **READY**: Tất cả dependency đã VERIFIED, sẵn sàng để agent phụ trách tiếp nhận và bắt đầu.
- **IN_PROGRESS**: Đang triển khai mã nguồn hoặc kiểm thử.
- **IMPLEMENTED**: Đã viết xong mã nguồn nhưng chưa hoàn tất tích hợp hoặc kiểm chứng.
- **VERIFIED**: Đã tích hợp, biên dịch sạch sẽ, kiểm thử thực tế đạt 100%, có bằng chứng cụ thể.
- **BLOCKED**: Có trở ngại cụ thể chặn đứng tiến trình, đã ghi nhận bằng chứng và điều kiện gỡ.

## 4. Lịch sử kiểm tra T09–T11 trước khi sửa countdown (2026-10-16)

- `AgentReport/TestResults/T09-T11/T09-T11-specialized.trx`: 55 Passed (T09 18, T10 18, T11 19), 0 Failed, 0 Skipped.
- `AgentReport/TestResults/T09-T11/Web-solution.trx`: 244 Passed, 0 Failed, 0 Skipped tại lần kiểm tra trước khi bổ sung các test countdown; tổng mới nhất là 252 ở mục 5.
- `AgentReport/TestResults/T09-T11/Reservation-domain.trx`: 30 Passed, 0 Failed, 0 Skipped (ReservationServiceTests + ReservationIntegrationTests).
- `Reservation-regression.trx` là lần lọc rộng theo tên Reservation: 35 Passed, 4 SQL tests Skipped, total 39; không được ghi thành 39 Passed.
- Browser dùng Edge/Playwright sẵn có, `Browser plugin not available`; ứng dụng thật qua proxy tạm + EF InMemory. T09 PASS; T10 timezone/tick/reload PASS, expiry FAIL. JSON/screenshot và flow sửa còn mở được dẫn trong từng báo cáo. Không đánh dấu T10 VERIFIED chỉ dựa vào .NET tests.

## 5. Nghiệm thu sau cùng của Codex (2026-10-16)

- Review lại mã nguồn T10 và CDP harness: **No findings**; các lỗi countdown ở lần kiểm tra mục 4 đã được xử lý.
- T10 WebTests: **26 Passed, 0 Failed, 0 Skipped**, bao gồm sandbox, browser E2E, negative control và kiểm chứng harness.
- Reservation Domain: **30 Passed, 0 Failed, 0 Skipped**.
- Toàn bộ Web Solution: **252 Passed, 0 Failed, 0 Skipped**. Đây là tổng kiểm thử mới nhất; các mốc 244 trong lịch sử là kết quả của lần chạy trước.
- Phạm vi: Edge/CDP và EF Core InMemory; SQL concurrency được kiểm tra tại T17.
- **T09, T10, T11: VERIFIED**. **T12 (Gemini A), T13 (Gemini B), T14 (Gemini C): READY**, đủ điều kiện triển khai song song theo phạm vi đã phân công.
