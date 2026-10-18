# Bảng Theo Dõi Tiến Độ Chuyển Đổi Web (Web Migration Task Board)

- **Cập nhật lần cuối**: 2026-10-18 (T21 VERIFIED; T22 VERIFIED — Accepted, No findings)
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
[Đợt 5: Song Song 4]   T13 (VERIFIED)      T12 (VERIFIED)      T14 (VERIFIED)
                       (Gemini B)          (Gemini A)          (Gemini C)
                        │                   │                   │
                        └───────────────────┼───────────────────┘
                                            ▼
[Đợt 6: Tích Hợp Toàn Bộ]                  T15 (VERIFIED)
                                          (Gemini A)
                                           │
                       ┌───────────────────┼───────────────────┐
                       ▼                   ▼                   ▼
[Đợt 7: Kiểm Thử Độc Lập] T16 (VERIFIED)      T17 (VERIFIED)      T18 (VERIFIED)
                      (Gemini A)          (Gemini B)          (Gemini C)
                       │                   │                   │
                       └───────────────────┼───────────────────┘
                                           ▼
[Đợt 8: Bàn Giao & Review]                T19 (VERIFIED)
                                          (Gemini A)
                                           │
                                           ▼
                                          T20 [VERIFIED — Changes requested]
                                           │
                                           ▼
                                          T21 [VERIFIED]
                                           │
                                           ▼
                                          T22 [VERIFIED — Accepted]
                                          (Codex)
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
| **T09** | Razor Pages CRUD ô kho bằng form | 4 | Gemini B | **VERIFIED** | T08 | [`WebMigration_T09.md`](file:///D:/FPT/PRN222/Project/Final_Project_Web/AgentReport/WebMigration_T09.md) | Razor Pages CRUD `/StorageUnits` (Index/Create/Edit/Details/Delete); Refactor 100% từ scaffold sang IStorageUnitAdminService; Zero DbContext/Repository in Presentation (Architecture 3/3 PASS); Form POST kèm Antiforgery & validation; GET Delete an toàn; Flow Create giữ facilityId từ Index & dynamic lookup Area/UnitType; POST Delete ngoài scope từ chối có kiểm soát (không 500); Facility/UnitType bất biến khi Edit; Concurrency round-trip ExpectedUpdatedAt; Chặn duplicate code (409) & scope breach (403); Chuẩn bị ID dòng ổn định & message area cho T13; Chống double submit; 18/18 StorageUnitsRazorPagesTests PASS; 242/242 Web sln PASS. |
| **T10** | Reservation countdown và worker hết hạn | 4 | Gemini A | **VERIFIED** | T08 | [`WebMigration_T10.md`](file:///D:/FPT/PRN222/Project/Final_Project_Web/AgentReport/WebMigration_T10.md) | Razor Pages Create/Details/Cancel/Index/MyReservations; Cookie actor; Hold 15m; Khắc phục 5 lỗi flow (múi giờ cơ sở Create, kẹp tháng JS 31/01->28/02, AJAX status confirmed/cancelled không bị coi là expired, cập nhật đúng dòng data-reservation-row trên Index/MyReservations, bỏ ToLocalTime thay bằng AppFormatters); Worker 30s scoped DI; Restart resilience; Hook T15; 17/17 T10 WebTests PASS; 23/23 Reservation tests PASS; 242/242 Web sln tests PASS. |
| **T11** | Quầy check in inspection và bàn giao | 4 | Gemini C | **VERIFIED** | T08 | [`WebMigration_T11.md`](file:///D:/FPT/PRN222/Project/Final_Project_Web/AgentReport/WebMigration_T11.md) | Razor Pages `/Staff/CheckIn` hoàn tất 8 operations; Phân quyền quầy nghiêm ngặt không bypass; Zero-persistence PIN; 4 mục checklist chuẩn hóa; 18/18 StaffCheckInPageTests PASS; 236/236 Web sln PASS; 225 PASS / 8 SKIP regression PASS. |
| **T12** | Thanh toán hóa đơn hợp đồng và refund | 5 | Gemini A | **VERIFIED** | T10 | [`WebMigration_T12.md`](file:///D:/FPT/PRN222/Project/Final_Project_Web/AgentReport/WebMigration_T12.md) / [`WebMigration_T12_FindingFixes.md`](file:///D:/FPT/PRN222/Project/Final_Project_Web/AgentReport/WebMigration_T12_FindingFixes.md) | **Kết luận nghiệm thu độc lập từ Codex: No findings.** Đã kiểm 22 tệp T12 khớp SHA-256 giữa Final_Project_Web và Final_Project; 26/26 WebTests T12 ở mỗi workspace và 50 Payment business tests PASS, 3 SKIP. Checkout quote/promo/policy; Demo Simulator web thuần, 0 WPF, 403 Production; VNPAY IPN 7041 HMAC-SHA512 nguồn sự thật tài chính; Return display-only; Details/Invoices/Agreements; Refund review scoped; Đã khắc phục triệt để T12-F01 & T12-F02. |
| **T13** | SignalR Create Update Delete theo mẫu thầy | 5 | Gemini B | **VERIFIED** | T09 | [`WebMigration_T13.md`](file:///D:/FPT/PRN222/Project/Final_Project_Web/AgentReport/WebMigration_T13.md) / [`WebMigration_T13_FindingFixes.md`](file:///D:/FPT/PRN222/Project/Final_Project_Web/AgentReport/WebMigration_T13_FindingFixes.md) / [`WebMigration_T13_R2_FindingFixes.md`](file:///D:/FPT/PRN222/Project/Final_Project_Web/AgentReport/WebMigration_T13_R2_FindingFixes.md) / [`WebMigration_T13_R3_FindingFixes.md`](file:///D:/FPT/PRN222/Project/Final_Project_Web/AgentReport/WebMigration_T13_R3_FindingFixes.md) / [`WebMigration_T13_R4_FindingFixes.md`](file:///D:/FPT/PRN222/Project/Final_Project_Web/AgentReport/WebMigration_T13_R4_FindingFixes.md) / [`WebMigration_T13_R5_FindingFixes.md`](file:///D:/FPT/PRN222/Project/Final_Project_Web/AgentReport/WebMigration_T13_R5_FindingFixes.md) / [`WebMigration_T13_R6_FindingFixes.md`](file:///D:/FPT/PRN222/Project/Final_Project_Web/AgentReport/WebMigration_T13_R6_FindingFixes.md) / [`WebMigration_T13_R7_FindingFixes.md`](file:///D:/FPT/PRN222/Project/Final_Project_Web/AgentReport/WebMigration_T13_R7_FindingFixes.md) / [`WebMigration_T13_R8_FindingFixes.md`](file:///D:/FPT/PRN222/Project/Final_Project_Web/AgentReport/WebMigration_T13_R8_FindingFixes.md) / [`WebMigration_T13_R9_FindingFixes.md`](file:///D:/FPT/PRN222/Project/Final_Project_Web/AgentReport/WebMigration_T13_R9_FindingFixes.md) | **Kết luận nghiệm thu độc lập từ Codex (Round 9): No findings.** Đã kiểm hai finding R9, chạy độc lập 6/6 test mục tiêu, báo cáo/TaskBoard và tệp test R9 khớp SHA-256 hai workspace; route `/storageUnitHub`, 3 Hub methods (`HubCreateStorageUnit`, `HubUpdateStorageUnit`, `HubDeleteStorageUnit`) và 3 Receiver CRUD events (`Receive_CreateStorageUnit`, `Receive_UpdateStorageUnit`, `Receive_DeleteStorageUnit`) còn nguyên vẹn; 10/10 BrowserTests PASS; 50/50 HubTests PASS; 374/374 Web sln PASS; 225/233 regression PASS (8 SQL skip); Đồng bộ 100% SHA-256 sang Final_Project. |
| **T14** | Quản trị tài khoản và trang của khách | 5 | Gemini C | **VERIFIED** | T11 | [`WebMigration_T14.md`](file:///D:/FPT/PRN222/Project/Final_Project_Web/AgentReport/WebMigration_T14.md) | Quản trị tài khoản Admin (Index, Create, Details) + Khách hàng (MyUnits, HandoverDetails, Notifications); Phân quyền nghiêm ngặt Cookie claims; Chặn khóa/thu hồi Admin cuối cùng; Zero-persistence PIN bảo mật; Bộ lắng nghe SignalR T15; 32/32 tests PASS; 298/298 toàn Web sln PASS. |
| **T15** | Tích hợp realtime cả năm chức năng | 6 | Gemini A | **VERIFIED** | T12, T13, T14 | [`WebMigration_T15_R5_FindingFixes.md`](file:///D:/FPT/PRN222/Project/Final_Project_Web/AgentReport/WebMigration_T15_R5_FindingFixes.md) | **Kết luận nghiệm thu độc lập từ Codex (Round R5): No findings.** Đã kiểm trực tiếp diff, code, call sites và test cho R4-F01/R4-F02; 8/8 test R4/R5 concurrency và architecture PASS; 2/2 test bảo toàn realtime PASS; BLL/DAL 225 PASS, 8 SKIP; node --check 10/10 PASS; 46 tệp T15 và TaskBoard khớp SHA-256 giữa hai workspace; route `/storageUnitHub`, ba Hub methods và ba Receiver CRUD events còn nguyên; Giới hạn: kiểm thử tranh chấp SQL Server thật được dành cho T17. Đồng bộ 100% sang Final_Project. |
| **T16** | Kiểm thử Cookie quyền và HTTP | 7 | Gemini A | **VERIFIED** | T15 | [`WebMigration_T16_R4_FindingFixes.md`](file:///D:/FPT/PRN222/Project/Final_Project_Web/AgentReport/WebMigration_T16_R4_FindingFixes.md) / [`WebMigration_T16_R4_CodexReview.md`](file:///D:/FPT/PRN222/Project/Final_Project_Web/AgentReport/WebMigration_T16_R4_CodexReview.md) | **Kết luận nghiệm thu độc lập từ Codex (Round R4): No findings.** Đã kiểm tra diff, code, call sites và test trên Kestrel thật; `HubOriginSecurityExtensions.IsAllowedHubOrigin` so khớp chính xác bộ ba (Scheme, Host, Effective Port) theo chuẩn RFC 6454; loại bỏ ngoại lệ loopback; hỗ trợ client không mang Origin; 8 test Origin âm/dương hoàn tất; 4 mã băm source khớp cả hai workspace; 39/39 SecurityHttpLiveServerTests PASS; hồi quy WebTests không browser 426/426 PASS; bảo toàn route `/storageUnitHub`, ba Hub methods và ba Receiver CRUD events; đồng bộ 100% SHA-256 sang Final_Project. |
| **T17** | Kiểm thử SQL concurrency và rollback | 7 | Gemini B | **VERIFIED** | T15 | [`WebMigration_T17_R4_FindingFixes.md`](file:///D:/FPT/PRN222/Project/Final_Project_Web/AgentReport/WebMigration_T17_R4_FindingFixes.md) / [`WebMigration_T17_R4_CodexReview.md`](file:///D:/FPT/PRN222/Project/Final_Project_Web/AgentReport/WebMigration_T17_R4_CodexReview.md) | **Kết luận nghiệm thu độc lập từ Codex (Round R4): No findings.** Đã kiểm tra diff, code, call sites và test; loại bỏ hoàn toàn false-positive FK 547; dùng fixture khóa ngoại thực (paymentId, invoiceId) kiểm chứng chính xác lỗi nghiệp vụ 51055 từ `core.trg_payment_allocation_guard`; chứng minh tương phản trigger disabled (insert thành công) vs. sau rollback kích hoạt lại (chặn đứng với lỗi 51055); cơ chế transactional DDL bảo vệ dọn dẹp fixture không để lại disabled trigger; 19/19 SQL Server concurrency tests PASS; 225/225 non-concurrency tests PASS (tổng 244/244 PASS); toàn bộ 6 trigger nghiệp vụ đều có `is_disabled = 0` trên DB WebTest thật; đồng bộ 100% SHA-256 sang Final_Project. |
| **T18** | Kiểm thử hai trình duyệt và luồng giao diện | 7 | Gemini C | **VERIFIED** | T15 | [`WebMigration_T18_R6_FindingFixes.md`](file:///D:/FPT/PRN222/Project/Final_Project_Web/AgentReport/WebMigration_T18_R6_FindingFixes.md) / [`WebMigration_T18_R6_CodexReview.md`](file:///D:/FPT/PRN222/Project/Final_Project_Web/AgentReport/WebMigration_T18_R6_CodexReview.md) | **Kết luận nghiệm thu độc lập từ Codex (Round R6): No findings.** Báo cáo R6 SHA-256 `18A2AA0D0462483FB3CE923D2A194E76279645EA701959BB2FE4077AE7BE7C29`, Codex review SHA-256 `631E6D7B0EE9EB60A49A20DB8A0E191F0EC46D4F8EC453D3AACA81A6D5297B75` (khớp hai workspace); kiểm thử T18 độc lập 7/7 PASS; hồi quy Web solution độc lập 449/449 PASS; 6 trigger nghiệp vụ đều có `is_disabled = 0`; 0 fixture T18 rác; bảo toàn contract Hub/Receiver T12–T15; đồng bộ 100% SHA-256 sang Final_Project. |
| **T19** | Đóng gói bàn giao cho Codex | 8 | Gemini A | **VERIFIED** | T16, T17, T18 | [`WebMigration_FinalHandover.md`](file:///D:/FPT/PRN222/Project/Final_Project_Web/AgentReport/WebMigration_FinalHandover.md) / [`WebMigration_T19_R2_CodexReview.md`](file:///D:/FPT/PRN222/Project/Final_Project_Web/AgentReport/WebMigration_T19_R2_CodexReview.md) / [`WebMigration_T19_Memory.md`](file:///D:/FPT/PRN222/Project/Final_Project_Web/AgentReport/WebMigration_T19_Memory.md) | **Đã hoàn tất nghiệm thu bàn giao cuối cùng (Codex Review R2: No findings).** Báo cáo bàn giao đã nghiệm thu `WebMigration_FinalHandover.md` (SHA-256 `28BD72CAD791F62086F4944D7B013B186BF8F6A7E88FA0CAF36110827BF8DD19`); Kết luận review độc lập của Codex `WebMigration_T19_R2_CodexReview.md` (SHA-256 `68A748D6832D0E9CBAC751CD70B77F6552CE2D47424A70A4C4F649469777E301`); Báo cáo sửa finding R2 `WebMigration_T19_R2_FindingFixes.md` (SHA-256 `DCD558D284F4D8BB6BAE0D33353B72A8872AE673FC7C2D1FF73DC965E56071E5`); Ghi nhớ `WebMigration_T19_Memory.md`; Git SHA `821449030eebe1e38dc3bfda996f8bcb38474d10`; Web solution 449/449 PASS; Bảo toàn hợp đồng Hub/Receiver T12–T15; Đồng bộ 100% sang `Final_Project`; Giữ nguyên 3 tệp loại trừ; Giữ T20–T22 WAITING chờ Codex kiểm tra lại board trước khi khởi động T20. |
| **T20** | Review độc lập sau bàn giao Gemini | 8 | Codex | **VERIFIED — Changes requested** | T19 | [`WebMigration_CodexReview.md`](file:///D:/FPT/PRN222/Project/Final_Project_Web/AgentReport/WebMigration_CodexReview.md) / [`WebMigration_T20_Memory.md`](file:///D:/FPT/PRN222/Project/Final_Project_Web/AgentReport/WebMigration_T20_Memory.md) | Một finding P1 về WebSocket còn hoạt động sau khi cookie 5 phút hết hạn. Review SHA-256 `AFEAA967448366992897C87A620FBED222EB873AF84F2942D50B89AAAB592127`; memory SHA-256 `D31F29829BE69DEA85F7174B3BE5C836B8CE504208A189DEB17DDEC250A47895`. Mở T21 để sửa đúng finding. |
| **T21** | Sửa các finding Codex đã giao | 9 | Gemini A | **VERIFIED** | T20 | [`WebMigration_FindingFixes.md`](file:///D:/FPT/PRN222/Project/Final_Project_Web/AgentReport/WebMigration_FindingFixes.md) / [`WebMigration_T21_Memory.md`](file:///D:/FPT/PRN222/Project/Final_Project_Web/AgentReport/WebMigration_T21_Memory.md) | Codex nghiệm thu độc lập: No findings. `CloseOnAuthenticationExpiration = true` đóng WebSocket khi ticket hết hạn; test Kestrel thật 1/1 PASS; Web solution 450/450 PASS; giữ route, Hub/Receiver contract và Origin RFC 6454. |
| **T22** | Review lại và kết luận nghiệm thu | 9 | Codex | **VERIFIED — Accepted** | T21 | [`WebMigration_CodexFinalReview.md`](file:///D:/FPT/PRN222/Project/Final_Project_Web/AgentReport/WebMigration_CodexFinalReview.md) / [`WebMigration_T22_Memory.md`](file:///D:/FPT/PRN222/Project/Final_Project_Web/AgentReport/WebMigration_T22_Memory.md) | No findings; test độc lập 1/1 live expiry, 40/40 security HTTP, 450/450 Web, 225 PASS/19 SKIP legacy; SHA-256 tệp phạm vi khớp hai workspace. |

---

## 3. Quy Ước Trạng Thái (Lifecycle Convention)
- **WAITING**: Chưa đủ điều kiện mở do phụ thuộc vào các task trước chưa hoàn thành.
- **READY**: Tất cả dependency đã VERIFIED, sẵn sàng để agent phụ trách tiếp nhận và bắt đầu.
- **IN_PROGRESS**: Đang triển khai mã nguồn hoặc kiểm thử.
- **IMPLEMENTED**: Đã viết xong mã nguồn nhưng chưa hoàn tất tích hợp hoặc kiểm chứng.
- **VERIFIED**: Đã tích hợp, biên dịch sạch sẽ, kiểm thử thực tế đạt 100%, có bằng chứng cụ thể.
- **BLOCKED**: Có trở ngại cụ thể chặn đứng tiến trình, đã ghi nhận bằng chứng và điều kiện gỡ.

---

## 4. Lịch sử kiểm tra T09–T11 trước khi sửa countdown (2026-10-16)

- `AgentReport/TestResults/T09-T11/T09-T11-specialized.trx`: 55 Passed (T09 18, T10 18, T11 19), 0 Failed, 0 Skipped.
- `AgentReport/TestResults/T09-T11/Web-solution.trx`: 244 Passed, 0 Failed, 0 Skipped tại lần kiểm tra trước khi bổ sung các test countdown; tổng mới nhất là 252 ở mục 5.
- `AgentReport/TestResults/T09-T11/Reservation-domain.trx`: 30 Passed, 0 Failed, 0 Skipped (ReservationServiceTests + ReservationIntegrationTests).
- `Reservation-regression.trx` là lần lọc rộng theo tên Reservation: 35 Passed, 4 SQL tests Skipped, total 39; không được ghi thành 39 Passed.
- Browser dùng Edge/Playwright sẵn có, `Browser plugin not available`; ứng dụng thật qua proxy tạm + EF InMemory. T09 PASS; T10 timezone/tick/reload PASS, expiry FAIL. JSON/screenshot và flow sửa còn mở được dẫn trong từng báo cáo. Không đánh dấu T10 VERIFIED chỉ dựa vào .NET tests.

---

## 5. Nghiệm thu sau cùng của Codex cho Đợt 4 (2026-10-16)

- Review lại mã nguồn T10 và CDP harness: **No findings**; các lỗi countdown ở lần kiểm tra mục 4 đã được xử lý.
- T10 WebTests: **26 Passed, 0 Failed, 0 Skipped**, bao gồm sandbox, browser E2E, negative control và kiểm chứng harness.
- Reservation Domain: **30 Passed, 0 Failed, 0 Skipped**.
- Toàn bộ Web Solution: **252 Passed, 0 Failed, 0 Skipped**. Đây là tổng kiểm thử mới nhất; các mốc 244 trong lịch sử là kết quả của lần chạy trước.
- Phạm vi: Edge/CDP và EF Core InMemory; SQL concurrency được kiểm tra tại T17.
- **T09, T10, T11: VERIFIED**. **T12 (Gemini A), T13 (Gemini B), T14 (Gemini C): READY**, đủ điều kiện triển khai song song theo phạm vi đã phân công.

---

## 6. Lịch sử sửa findings Đợt 5 (2026-10-16)

- **T12 (Thanh toán, hóa đơn, hợp đồng, refund - Gemini A)**: Đã khắc phục triệt để T12-F01 (XSS VnpayReturn với HTML encoding + kiểm tra định dạng ID tham chiếu giao dịch + link an toàn) và T12-F02 (Đồng bộ toàn bộ tệp, cấu hình, dependency và test sang workspace `Final_Project`, khớp SHA-256 100%). 26/26 WebTests PASS.
- **T13 (SignalR Realtime CRUD - Gemini B)**: Đã trải qua các vòng review và sửa finding (R1–R9), chuyển test SignalR sang Kestrel loopback TCP thật ép `HttpTransportType.WebSockets`, kiểm chứng `CookieTempData` redirect HTML message, pre-commit Create/Edit/Delete & rogue mutation EqualException. 50/50 HubTests PASS, 10/10 BrowserTests PASS.
- **T14 (Quản trị tài khoản & Khách hàng - Gemini C)**: 32/32 WebTests PASS. Admin accounts, MyUnits, HandoverDetails, Notifications, Zero-persistence PIN.

---

## 7. Kết Luận Nghiệm Thu Độc Lập của Codex cho Đợt 5 (2026-10-18)

*Ghi chú xác thực: Đây là kết luận review độc lập của Codex, không phải tự đánh giá của Gemini.*

1. **Task T12 — Thanh toán hóa đơn hợp đồng và refund**: **VERIFIED**
   - **Codex review**: **No findings**.
   - Đã kiểm tra 22 tệp T12 khớp mã băm SHA-256 giữa `Final_Project_Web` và `Final_Project`.
   - 26/26 Payment WebTests đạt PASS ở cả hai workspace (`Final_Project_Web` và `Final_Project`).
   - 50 Payment business tests PASS, 3 SKIP.
   - VNPAY IPN được xác nhận là nguồn sự thật tài chính duy nhất; Return URL display-only không thay đổi trạng thái database; triệt tiêu lỗ hổng XSS; Demo Simulator hoạt động thuần trên web và bị chặn ở Production.

2. **Task T13 — SignalR Create Update Delete theo mẫu thầy**: **VERIFIED**
   - **Codex review (Round 9)**: **No findings**.
   - Đã kiểm tra hai finding R9, chạy độc lập 6/6 test mục tiêu.
   - Báo cáo, TaskBoard và tệp test R9 khớp SHA-256 giữa `Final_Project_Web` và `Final_Project`.
   - Route `/storageUnitHub`, 3 Hub methods (`HubCreateStorageUnit`, `HubUpdateStorageUnit`, `HubDeleteStorageUnit`) và 3 Receiver CRUD events (`Receive_CreateStorageUnit`, `Receive_UpdateStorageUnit`, `Receive_DeleteStorageUnit`) còn nguyên vẹn.

3. **Task T14 — Quản trị tài khoản và trang của khách**: **VERIFIED**
   - Toàn bộ 32/32 WebTests PASS, đã hoàn tất nghiệm thu.

4. **Task T15 — Tích hợp realtime cả năm chức năng**: **VERIFIED**
   - **Codex review (Round R5)**: **No findings**.
   - Báo cáo đã review: `AgentReport/WebMigration_T15_R5_FindingFixes.md`, SHA-256 `40A4B9559AF66FC45B069DF2BD7AD9B695D6CB85B26B5F7683EC85AF2658E7C8` (khớp hai workspace).
   - Đã kiểm tra trực tiếp diff, code, call sites và test cho R4-F01/R4-F02: 8/8 test R4/R5 concurrency và architecture PASS; 2/2 test bảo toàn realtime liên quan PASS.
   - BLL/DAL 225 PASS, 8 SKIP; node --check 10/10 PASS.
   - Hash 46 tệp T15 từ các báo cáo R3–R5 khớp giữa `Final_Project_Web` và `Final_Project`; TaskBoard cũng khớp.
   - Route `/storageUnitHub`, ba Hub methods (`HubCreate_StorageUnit`, `HubUpdate_StorageUnit`, `HubDelete_StorageUnit`) và ba Receiver CRUD events còn nguyên vẹn.
   - Giới hạn: Chưa tự chạy tranh chấp trên SQL Server thật; kiểm thử SQL được dành cho T17, không tính InMemory là bằng chứng SQL.

5. **Các Task tiếp theo (T16, T17, T18)**: **WAITING**
   - Giữ nguyên trạng thái WAITING theo kế hoạch, không tự ý khởi động T16–T18.

---

## 8. Tiến Độ Sửa Findings Vòng R3 Cho Task T15 (2026-10-18)

- **R2-F01 [P1] Khôi phục ranh giới kiến trúc Web → BLL → DAL**: Đã đưa `ExpiredReservationHoldInfo` sang namespace BLL (`SelfStorageManagementSystem.BusinessLogic.DTOs.Responses.Reservations`). Web assembly hoàn toàn sạch tham chiếu DataAccess. Cả 2 bài test kiến trúc `Architecture_MvcWebApp_DoesNotReferenceDataAccessOrApiOrWpf` và `MvcWebApp_DoesNotReferenceDataAccessOrApiOrWpf` đạt PASS 100%.
- **R2-F02 [P1] Chỉ phát IPN event khi repository thực sự chuyển trạng thái DB**: Xử lý triệt để trong `PaymentRepository` và `PaymentService`. Duplicate concurrent success IPN trả `IsAlreadyProcessed = true` -> ACK `02`, `IsStatusTransitionCommitted = false` -> 0 realtime event. Duplicate decline hoặc race with success trả `false` -> 0 realtime event. Late payment IPN cho hold đã expired bảo toàn `reconciliation_required`, tuyệt đối không phát `ReservationChanged(confirmed)`.
- **R2-F03 [P2] Triển khai realtime receiver chức năng trên trang Payments**: Tạo `payments.js` và nhúng vào `Payments/Index.cshtml`, `Payments/Details.cshtml`. Xử lý `app:payment-changed` cập nhật badge động và hiển thị thông báo alert; xử lý `app:realtime-reconnected` cảnh báo đồng bộ dữ liệu chuẩn.
- **R2-F04 [P2] Đọc lại trạng thái chuẩn ngay lập tức khi SignalR reconnect**: Cập nhật `reservation-countdown.js` với listener `app:realtime-reconnected`, gọi API endpoint `/Reservations/Details?id=...&handler=Status` cập nhật UI tức thì khi SignalR phục hồi kết nối.

---

## 9. Tiến Độ Sửa Findings Vòng R4 Cho Task T15 (2026-10-18)

- **R3-P1 [P1] Serialize failed IPN transitions with success and duplicate failures**:
  - Khóa đồng bộ hai cấp đối xứng (`SS_Reservation_{reservationId}` rồi `SS_Payment_{paymentId}`) qua `sp_getapplock` (SQL Server) và `SemaphoreSlim` (In-Memory) cho cả `ExecuteFinalizePaymentTransactionAsync` và `ExecuteFinalizeFailedPaymentTransactionAsync`.
  - Nạp lại bản ghi tươi từ database sau khi vào khóa độc quyền (`EntityState.Detached` + fresh query).
  - Terminal status check: Từ chối chuyển đổi trạng thái khi payment đã ở `succeeded`, `failed`, hoặc `cancelled`, trả `IsTransitionCommitted = false` và mã ACK thích hợp (`02` cho confirmed, `00` cho duplicate decline).
  - Reverse race handling: Khi success callback tới sau khi decline đã commit `failed`, hệ thống chuyển payment sang `succeeded` kèm `reconciliation_required = true` (`PAYMENT_ALREADY_FAILED`), tạo bản ghi hoàn tiền `refund` cho nhân sự đối soát, tuyệt đối không tạo hợp đồng và không xác nhận reservation.
  - 4/4 kiểm thử concurrency thực tế (gọi trực tiếp PaymentService/PaymentRepository qua nhiều DbContext, không mock kết quả cuối) đều đạt PASS 100%. Toàn bộ 401 WebTests, 225 BLL/DAL regression tests, và 10 tệp JavaScript syntax check đạt PASS 100%. Đồng bộ hoàn toàn sang `Final_Project`.

---

## 10. Tiến Độ Sửa Findings Vòng R5 Cho Task T15 (2026-10-18)

- **R4-F01 [P1] — Không bỏ qua success sau decline khi cùng transaction ID**:
  - `PaymentRepository.ExecuteFinalizePaymentTransactionAsync`: Cập nhật điều kiện idempotency `existingEvent != null && existingEvent.status == "processed" && existingEvent.event_type != "payment_failed"`. Nếu integration event trước đó là của decline (`event_type == "payment_failed"`), callback success cùng X/Y không bị bỏ qua mà đi thẳng vào nhánh reconciliation (`PAYMENT_ALREADY_FAILED`).
  - Hệ thống ghi nhận tiền, cập nhật payment `succeeded` với `reconciliation_required = true`, tạo đúng 1 bản ghi `refund`, cập nhật event thành `payment_succeeded_reconciliation`, phát đúng sự kiện `PaymentChanged(reconciliation_required)`, và tuyệt đối không xác nhận reservation hay tạo hợp đồng thuê.
  - Callback retry thành công tiếp theo với cùng ID tuân thủ tính lũy thừa (idempotent), trả ACK `02`, phát 0 event mới và tạo 0 refund trùng lặp.
- **R4-F02 [P2] — Giải phóng reservation semaphore nếu việc chờ khóa payment bị hủy**:
  - `PaymentRepository`: Đặt toàn bộ quá trình acquire semaphore (`inMemoryResSemaphore` và `inMemoryPaySemaphore` / `inMemoryPromoSemaphore`) vào trong khối `try`, theo dõi trạng thái bằng cờ boolean `resSemaphoreAcquired` và `paySemaphoreAcquired`.
  - Trong khối `finally`, giải phóng mỗi semaphore nếu và chỉ nếu cờ tương ứng là `true`. Nếu việc chờ khóa payment bị hủy qua `CancellationToken`, reservation semaphore được giải phóng an toàn ngay lập tức, tránh rò rỉ semaphore vĩnh viễn và không gây `SemaphoreFullException`.
  - Export `InMemorySemaphores` nội bộ cho test harness qua `InternalsVisibleTo` trong `SelfStorageManagementSystem.DataAccess.csproj`.
  - 6/6 bài kiểm thử concurrency WebTests (bao gồm kịch bản reverse race cùng transaction ID và kịch bản hủy khóa có kiểm soát) đạt PASS 100%. Toàn bộ 403 WebTests, 225 BLL/DAL regression tests, 2 Architecture Clean Architecture tests, và 10/10 JavaScript syntax check đạt PASS 100%. Đồng bộ hoàn toàn sang `Final_Project`.

---

## 11. Mở T16–T18 sau smoke độc lập (2026-10-18)

- Codex kiểm `Final_Project_Web` tại HEAD `82496d648624ddf9af82d481017cfdd12e612787`, T15 đã VERIFIED. Lệnh `dotnet test tests/SelfStorageManagementSystem.WebTests/SelfStorageManagementSystem.WebTests.csproj --no-restore --verbosity quiet`: **403 passed, 0 failed, 0 skipped**, thời gian 4 phút 36 giây.
- T16, T17, T18 chuyển từ WAITING sang **READY** theo dependency T15. Các đoạn lịch sử phía trên ghi WAITING phản ánh thời điểm trước smoke, không phải trạng thái hiện tại. T19–T22 tiếp tục WAITING.
- Contract source hiện hành: `/storageUnitHub`; `HubCreate_StorageUnit`, `HubUpdate_StorageUnit`, `HubDelete_StorageUnit`; `Receiver_CreateStorageUnit`, `Receiver_UpdateStorageUnit`, `Receiver_DeleteStorageUnit`. Một số dòng lịch sử ghi tên cũ; source hiện hành là chuẩn đối chiếu.

---

## 12. Ghi nhớ nghiệm thu T16–T18 (2026-10-18)

- T16 VERIFIED: `AgentReport/WebMigration_T16_Memory.md`.
- T17 VERIFIED: `AgentReport/WebMigration_T17_Memory.md`.
- T18 VERIFIED: `AgentReport/WebMigration_T18_Memory.md`.
- Bàn giao nhóm: `AgentReport/WebMigration_T16_T18_HandoffMemory.md`; quy tắc ghi nhớ cho mỗi task tiếp theo: `AgentReport/WebMigration_TaskMemoryGuide.md`.
- T19–T22 vẫn WAITING cho đến khi T16–T18 được commit trên nhánh riêng, merge vào `main`, và bàn giao qua chat mới.

---

## 13. Nghiệm thu bàn giao cuối cùng T19 (2026-10-18)

- **Trạng thái**: T19 chuyển sang **VERIFIED** (nghiệm thu bởi Codex, No findings). T20–T22 tiếp tục **WAITING** cho đến khi Codex kiểm tra lại TaskBoard.
- **Báo cáo bàn giao đã nghiệm thu**: [`WebMigration_FinalHandover.md`](file:///D:/FPT/PRN222/Project/Final_Project_Web/AgentReport/WebMigration_FinalHandover.md), SHA-256 `28BD72CAD791F62086F4944D7B013B186BF8F6A7E88FA0CAF36110827BF8DD19`.
- **Kết luận review độc lập của Codex (R2)**: [`WebMigration_T19_R2_CodexReview.md`](file:///D:/FPT/PRN222/Project/Final_Project_Web/AgentReport/WebMigration_T19_R2_CodexReview.md), SHA-256 `68A748D6832D0E9CBAC751CD70B77F6552CE2D47424A70A4C4F649469777E301` (**No findings**).
- **Báo cáo sửa finding R2**: [`WebMigration_T19_R2_FindingFixes.md`](file:///D:/FPT/PRN222/Project/Final_Project_Web/AgentReport/WebMigration_T19_R2_FindingFixes.md), SHA-256 `DCD558D284F4D8BB6BAE0D33353B72A8872AE673FC7C2D1FF73DC965E56071E5`.
- **Ghi nhớ task**: [`WebMigration_T19_Memory.md`](file:///D:/FPT/PRN222/Project/Final_Project_Web/AgentReport/WebMigration_T19_Memory.md).
- **Git HEAD commit**: `821449030eebe1e38dc3bfda996f8bcb38474d10` trên nhánh `main` (Merge WebMigration T16-T18 verified batch).
- **Kiểm thử Web Solution**: 449 Passed, 0 Failed, 0 Skipped (đã xác thực độc lập từ T18).
- **Hợp đồng SignalR Realtime**: Giữ nguyên vẹn route `/storageUnitHub`, 3 Hub methods (`HubCreate_StorageUnit`, `HubUpdate_StorageUnit`, `HubDelete_StorageUnit`) và 3 Receiver events (`Receiver_CreateStorageUnit`, `Receiver_UpdateStorageUnit`, `Receiver_DeleteStorageUnit`).
- **3 tệp loại trừ**: Tiếp tục giữ nguyên ngoài commit (`docs/KeHoach_Web_Gemini_Va_Codex_Review.docx`, `AgentReport/WebMigration_T15_CodexLoopState.json`, `scratch/generate_r3_report.js`).
- **Đồng bộ Workspace**: Báo cáo bàn giao, ghi nhớ, bảng theo dõi và evidence đồng bộ 100% SHA-256 sang `Final_Project`.
- **Điều kiện mở T20**: Codex sẵn sàng tiếp nhận review độc lập T20 theo các điều kiện tiên quyết tại Mục 6 của `WebMigration_FinalHandover.md` sau khi hoàn tất kiểm tra lại TaskBoard.

---

## 14. Review độc lập T20 (2026-10-18)

- **Trạng thái**: T20 hoàn thành review độc lập, **Changes requested** với một finding P1; T21 **READY**; T22 **WAITING**.
- **Finding**: `src/SelfStorageManagementSystem.Web/Program.cs:93` ánh xạ SignalR hub mà chưa bật đóng kết nối khi authentication ticket hết hạn. WebSocket đã kết nối tiếp tục dùng principal cũ sau khi cookie 5 phút hết hạn; kiểm tra nhóm định kỳ không kiểm tra thời hạn ticket.
- **Review Codex**: [`WebMigration_CodexReview.md`](file:///D:/FPT/PRN222/Project/Final_Project_Web/AgentReport/WebMigration_CodexReview.md), SHA-256 `AFEAA967448366992897C87A620FBED222EB873AF84F2942D50B89AAAB592127`.
- **Memory T20**: [`WebMigration_T20_Memory.md`](file:///D:/FPT/PRN222/Project/Final_Project_Web/AgentReport/WebMigration_T20_Memory.md), SHA-256 `D31F29829BE69DEA85F7174B3BE5C836B8CE504208A189DEB17DDEC250A47895`.
- **Prompt T21**: [`WEBMIGRATION_T21_FIX_PROMPT.md`](file:///D:/FPT/PRN222/Project/Final_Project_Web/Agent/Gemini/WEBMIGRATION_T21_FIX_PROMPT.md), SHA-256 `C45D864638C488318F1903406D1B1D97C567B529D7BCB2F40A1C1C24414DF8B1`.
- **Tests**: Không chạy thêm trong T20; baseline Codex đã kiểm trước đó 449/449 Web, 225 PASS/19 SKIP legacy default, 19/19 SQL concurrency. Thiếu test WebSocket đang mở vượt qua hạn cookie.
- **Hợp đồng T12–T15 và ba tệp ngoài commit**: Giữ nguyên. Git cycle đợi T21/T22 hoàn tất nghiệm thu độc lập.

---

## 15. Tiến Độ Sửa Finding Cho Task T21 (2026-10-18)

- **Trạng thái**: T21 chuyển sang **IMPLEMENTED**, chờ Codex review độc lập (T22 **WAITING**).
- **Finding đã fix**: [P1] `src/SelfStorageManagementSystem.Web/Program.cs:93` đã cấu hình `options.CloseOnAuthenticationExpiration = true` trên endpoint `MapHub<StorageUnitHub>("/storageUnitHub")`. Đồng bộ cấu hình tương ứng trên test server fixtures (`SecurityHttpServerFixture.cs` và `KestrelRealtimeServer.cs`).
- **Báo cáo fix**: [`WebMigration_FindingFixes.md`](file:///D:/FPT/PRN222/Project/Final_Project_Web/AgentReport/WebMigration_FindingFixes.md).
- **Ghi nhớ task**: [`WebMigration_T21_Memory.md`](file:///D:/FPT/PRN222/Project/Final_Project_Web/AgentReport/WebMigration_T21_Memory.md).
- **Kiểm thử thực tế**:
  - Test tập trung `OpenWebSocket_ClosesWhenAuthenticationTicketExpires_AndFreshTicketCanReconnect`: **1 passed, 0 failed, 0 skipped** (2 s). Kiểm chứng WebSocket TCP loopback thật đóng kết nối khi cookie hết hạn, chặn gọi method và chặn nhận event sau khi đóng, và cho phép cookie mới kết nối bình thường.
  - Negative control: Kiểm chứng với `CloseOnAuthenticationExpiration = false` thất bại chính xác với lỗi không đóng kết nối sau timeout 6s.
  - Toàn bộ `SecurityHttpLiveServerTests`: **40 passed, 0 failed, 0 skipped** (5 s).
  - Toàn bộ Web Solution (`SelfStorageManagementSystem.WebTests.csproj`): **450 passed, 0 failed, 0 skipped** (4 m 36 s).
  - Toàn bộ Legacy Solution (`SelfStorageManagementSystem.sln`): **225 passed, 0 failed, 19 skipped** (2 s).
- **Bảo toàn**: Route `/storageUnitHub`, 3 Hub methods, 3 Receiver events, Origin RFC 6454, 3 tệp loại trừ và thời hạn cookie 5 phút giữ nguyên vẹn.
- **Đồng bộ Workspace**: Đồng bộ 100% các tệp phạm vi T21 sang `Final_Project`. Chờ Codex tiến hành T22.

---

## 16. Nghiệm thu độc lập T21/T22 (2026-10-18)

- **Trạng thái**: T21 **VERIFIED**; T22 **VERIFIED — Accepted, No findings** sau review độc lập của Codex.
- **Báo cáo T21**: `AgentReport/WebMigration_FindingFixes.md`, SHA-256 `D8639969B1A4F8FED35F4F334E121B7E675C1721516D39B430BFC88C6DD93A17`.
- **Review T22**: `AgentReport/WebMigration_CodexFinalReview.md`, SHA-256 `89278E8FC5DDB7F9EC4A2FF89623882128D6B2B01B405EA93BAC8FB410153EE3`.
- **Ghi nhớ**: `AgentReport/WebMigration_T21_Memory.md` và `AgentReport/WebMigration_T22_Memory.md`.
- **Kiểm thử Codex**: live WebSocket expiry 1/1 PASS; SecurityHttpLiveServerTests 40/40 PASS; Web solution 450/450 PASS; legacy default 225 PASS, 19 SKIP. Không chạy lại SQL concurrency vì T21 không đổi SQL; T17 đã xác minh 19/19.
- **Bảo toàn**: `/storageUnitHub`, ba Hub methods, ba Receiver events, thời hạn cookie 5 phút và kiểm tra Origin RFC 6454. Test Web tạo lại 19 PNG T18; đã khôi phục từ evidence R6 đồng bộ, không còn diff.
- **Đồng bộ**: Toàn bộ tệp T21/T22 và báo cáo/memory/TaskBoard khớp SHA-256 giữa `Final_Project_Web` và `Final_Project`. Git chỉ lấy các tệp T21/T22; ba tệp loại trừ và thay đổi line ending không liên quan ở ngoài commit.
