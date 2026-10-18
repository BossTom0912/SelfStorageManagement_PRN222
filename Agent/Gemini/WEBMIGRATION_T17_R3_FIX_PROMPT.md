# T17 — Codex review R3: sửa bằng chứng active trigger

Gemini B, chỉ xử lý T17 trong `D:\FPT\PRN222\Project\Final_Project_Web` và đồng bộ sang `D:\FPT\PRN222\Project\Final_Project`. Báo cáo R3 `AgentReport/WebMigration_T17_R3_FindingFixes.md` có SHA-256 `382EDA771EB6F6BCA0C83571618625368304399C6E1ADEA266ABBE0ED06BB0A7`; Codex đã review độc lập code/call sites và chạy lại suite SQL Server concurrency: 19/19 PASS. Transaction bao quanh DISABLE/cleanup/ENABLE và kiểm tra `is_disabled = 0` đã giải quyết finding R2.

## Finding R3 [P2] — phép thử active enforcement có thể pass khi trigger không chạy

`tests/SelfStorageManagementSystem.Tests/PaymentSqlServerConcurrencyTests.cs:1737-1749` chèn `payment_allocations` với cả `payment_id` và `invoice_id` không tồn tại, rồi chấp nhận `SqlException.Number == 51055 || 547`. Lỗi 547 là vi phạm foreign key trước khi `core.trg_payment_allocation_guard` xử lý; bằng chứng disposable DB trong báo cáo R3 thực tế cũng trả về 547. Do đó phần kiểm chứng “trigger actively blocked invalid operation” pass ngay cả khi guard không chặn gì. Đây là false positive trong test và kết luận báo cáo.

Hãy sửa bài kiểm tra để tạo dữ liệu FK hợp lệ nhưng vi phạm riêng quy tắc của `trg_payment_allocation_guard`, rồi assert đúng mã lỗi nghiệp vụ 51055 (hoặc một phép thử trực tiếp khác chứng minh trigger thực sự chạy). Giữ fixture cô lập và cleanup an toàn trên SQL Server thật; kiểm tra lại trạng thái đủ 6 trigger sau test. Cập nhật bằng chứng disposable DB và báo cáo cho đúng lỗi nhận được; không diễn giải lỗi FK 547 thành bằng chứng trigger. Chạy lại suite SQL concurrency, regression cần thiết, và đối chiếu SHA-256 các tệp thay đổi giữa hai workspace.

Nộp báo cáo mới `AgentReport/WebMigration_T17_R4_FindingFixes.md` với đường dẫn và SHA-256 rõ ràng, để TaskBoard T17 ở IMPLEMENTED chờ Codex review. Không tự đánh dấu VERIFIED. Không sửa T16/T18, không khởi động T19–T22, không thay đổi ba tệp ngoài commit đã bảo toàn hay contract T12–T15.
