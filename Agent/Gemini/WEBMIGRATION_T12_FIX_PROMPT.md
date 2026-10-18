# Prompt sửa finding T12 — Thanh toán, callback và đồng bộ bàn giao

**Giao cho:** Gemini A (owner T12). **Task:** T12, Đợt 5. **Workspace triển khai:** `D:\FPT\PRN222\Project\Final_Project_Web`. **Workspace cần đồng bộ:** `D:\FPT\PRN222\Project\Final_Project`.

Hãy sửa riêng các finding dưới đây của T12. Đọc mã và test hiện tại trước khi sửa; giữ nguyên nguyên tắc VNPAY IPN là nơi duy nhất xác nhận tài chính, còn Return URL chỉ hiển thị. Không gộp việc sửa T13 hoặc T14 vào task này.

## T12-F01 — XSS ở trang VNPAY Return (P1)

- **Nguồn lỗi:** `src/SelfStorageManagementSystem.Web/Controllers/PaymentsApiController.cs`, action `VnpayReturn`, khoảng dòng 66–112. `vnp_TxnRef` lấy trực tiếp từ query string rồi chèn vào HTML tại dòng 89 và vào thuộc tính `href` tại dòng 100. Đây là endpoint `[AllowAnonymous]`; kẻ khác có thể tạo link với payload HTML/JavaScript.
- **Hướng giải quyết:** Không đưa query string thô vào HTML. Mã hóa giá trị khi hiển thị dưới dạng text và tạo URL chi tiết bằng ID đã được kiểm tra định dạng cùng giá trị query đã URL encode. Với mã không hợp lệ, hiển thị thông báo an toàn và không tạo link chi tiết sai. Giữ Return URL chỉ đọc, không gọi logic xác nhận hoặc cập nhật thanh toán.
- **Điều kiện hoàn thành:** Request chứa `vnp_TxnRef` có dấu nháy, thẻ HTML hoặc payload trong thuộc tính không tạo DOM/script ngoài ý muốn; mã hợp lệ vẫn hiện và link đến đúng payment; test HTTP cho cả trường hợp độc hại và bình thường; trạng thái DB không thay đổi khi gọi Return.

## T12-F02 — Bàn giao T12 chưa có trong Final_Project (P1)

- **Nguồn lỗi:** `AgentReport/WebMigration_TaskBoard.md` trong `Final_Project_Web` ghi T12 VERIFIED và T15 READY tại dòng 63, 66, 115, nhưng `Final_Project` vẫn ghi T12 READY/T15 WAITING. Tại thời điểm review, `Final_Project` còn thiếu `AgentReport/WebMigration_T12.md`, `src/SelfStorageManagementSystem.Web/Controllers/PaymentsApiController.cs` và `src/SelfStorageManagementSystem.Web/Pages/Payments/Checkout.cshtml.cs`; đây là ví dụ, cần kiểm toàn bộ danh sách tệp T12.
- **Hướng giải quyết:** Đối chiếu danh sách tệp ở mục 3 của `AgentReport/WebMigration_T12.md`, các dependency và thay đổi cấu hình/test liên quan. Đồng bộ đầy đủ bản cuối từ `Final_Project_Web` sang `Final_Project` theo từng tệp T12; không ghi đè thay đổi không thuộc T12 của agent khác. Đồng bộ báo cáo và Task Board sau khi xác minh. Nếu hai workspace cần khác biệt có chủ đích, ghi rõ khác biệt và lý do; không tuyên bố “đồng bộ đầy đủ” khi còn thiếu tệp.
- **Điều kiện hoàn thành:** Tất cả tệp T12 bắt buộc có mặt tại hai workspace và nội dung tương ứng khớp (ghi bảng đường dẫn và SHA-256 hoặc diff); hai Task Board thống nhất trạng thái T12/T15; build và test T12 chạy trên **cả hai** workspace. Không tự đổi trạng thái finding sang VERIFIED trước review độc lập; ghi `IMPLEMENTED — chờ Codex review` cho phần sửa finding nếu cần cập nhật board.

## Kiểm thử và báo cáo bắt buộc

1. Chạy test T12 có sẵn và test mới cho XSS, URL hợp lệ, Return display-only; chạy regression Web và core có liên quan. Ghi lệnh, số pass/fail/skip và đường dẫn workspace của từng lần chạy. Không dùng kết quả cũ trong báo cáo T12 để thay cho lần kiểm mới.
2. Tạo **`AgentReport/WebMigration_T12_FindingFixes.md`** trong `Final_Project_Web`, sau đó đồng bộ báo cáo này sang `Final_Project`. Báo cáo phải có: từng finding T12-F01/F02; nguyên nhân gốc; danh sách tệp và dòng đã sửa; trước/sau hành vi; bằng chứng test; bảng đối chiếu SHA-256 hai workspace; rủi ro còn lại. Kết thúc bằng trạng thái `READY_FOR_CODEX_REVIEW`, không tự tuyên bố Codex đã nghiệm thu.
3. Khi bàn giao, chỉ gửi đường dẫn báo cáo `.md` và tóm tắt ngắn những gì đã sửa để Codex review độc lập.
