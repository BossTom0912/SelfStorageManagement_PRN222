# Prompt sửa finding T14 — HTTP 404 cho chi tiết bàn giao

**Giao cho:** Gemini C (owner T14). **Task:** T14, Đợt 5. **Workspace triển khai:** `D:\FPT\PRN222\Project\Final_Project_Web`.

Hãy sửa riêng finding T14 dưới đây. Không trộn thay đổi T12 hoặc T13 vào task này.

## T14-F01 — Trả HTTP 200 cho hợp đồng không thuộc khách hàng (P2)

- **Nguồn lỗi:** `src/SelfStorageManagementSystem.Web/Pages/MyUnits/HandoverDetails.cshtml.cs` dòng 43–47 và 57–63: ID không hợp lệ hoặc `GetCustomerHandoverAsync` ném `NotFoundException` đều `return Page()`, khiến HTTP trả 200. `AgentReport/WebMigration_T14.md` mục 3.1 cam kết trả 404 để không tiết lộ quyền sở hữu. Test `Customer_CrossCustomer_HandoverDetails_CannotAccessOtherCustomerData` trong `tests/SelfStorageManagementSystem.WebTests/AdminAccountsAndCustomerPagesTests.cs` khoảng dòng 811–824 hiện lại assert `HttpStatusCode.OK`, nên củng cố hành vi sai.
- **Hướng giải quyết:** Trả HTTP 404 cho ID không hợp lệ, hợp đồng không tồn tại và hợp đồng của khách khác. Có thể render trang thông báo thân thiện với `Response.StatusCode = 404` hoặc dùng `NotFound()`; giữ cùng thông điệp an toàn để người gọi không phân biệt bản ghi có tồn tại hay không. Không thay đổi kiểm tra ownership trong service.
- **Điều kiện hoàn thành:** Khách A xem bàn giao của mình nhận 200 và dữ liệu đúng; khách B truy cập ID của A nhận 404 và không có dữ liệu của A; ID không tồn tại/không hợp lệ cũng nhận 404 với nội dung không tiết lộ sự tồn tại. Cập nhật test đang assert 200 thành 404 và bổ sung các trường hợp thiếu; toàn bộ T14 tests vẫn pass.

## Kiểm thử và báo cáo bắt buộc

1. Chạy test T14 và regression Web liên quan; ghi lệnh, số pass/fail/skip, kết quả HTTP thực tế.
2. Đồng bộ đúng các tệp T14 đã sửa sang `D:\FPT\PRN222\Project\Final_Project`, đối chiếu SHA-256 hoặc diff; không ghi đè công việc T12/T13.
3. Tạo **`AgentReport/WebMigration_T14_FindingFixes.md`** trong `Final_Project_Web` và bản đồng bộ trong `Final_Project`. Ghi nguyên nhân gốc, tệp và dòng sửa, bằng chứng 200/404 và không rò dữ liệu, test, bảng đồng bộ, rủi ro còn lại. Kết thúc bằng `READY_FOR_CODEX_REVIEW`; không tự tuyên bố Codex đã nghiệm thu.
4. Khi bàn giao, gửi đường dẫn báo cáo `.md` để Codex review lại riêng T14.
