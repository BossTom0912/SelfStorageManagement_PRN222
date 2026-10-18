# Task T15 — Tích hợp realtime toàn Web

**Giao cho Gemini A, trong một cuộc trò chuyện Antigravity mới gắn với `D:\FPT\PRN222\Project\Final_Project_Web`.** Workspace đồng bộ: `D:\FPT\PRN222\Project\Final_Project`. Chỉ bắt đầu khi TaskBoard của workspace triển khai ghi T12/T13/T14 `VERIFIED` và T15 `READY`; nếu chưa đúng, dừng và báo rõ trạng thái.

## Phạm vi và hợp đồng

- Chỉ triển khai T15. Không khởi động T16, T17, T18. Giữ nguyên các hành vi đã nghiệm thu ở T12 (checkout/IPN/refund), T13 (Storage Unit CRUD), T14 (Admin, MyUnits, Notifications).
- Giữ nguyên `/storageUnitHub`, `HubCreate_StorageUnit`, `HubUpdate_StorageUnit`, `HubDelete_StorageUnit`, `Receiver_CreateStorageUnit`, `Receiver_UpdateStorageUnit`, `Receiver_DeleteStorageUnit`, các DTO và quy tắc phân quyền/scope hiện có. Không đưa PIN, dữ liệu thanh toán nhạy cảm hay EF entity vào event.
- Đọc `docs/WebMigration_Contracts.md` mục 8, `AgentReport/WebMigration_T10.md`, `WebMigration_T12.md`, `WebMigration_T13_R9_FindingFixes.md`, `WebMigration_T14.md` và mã hiện hành trước khi sửa. Tích hợp năm luồng thay đổi `CatalogInvalidated`, `ReservationChanged`, `PaymentChanged`, `CheckInChanged`, `NotificationsChanged` đúng schema contract. Kết nối các hook T14 (`AccountChanged`, `CheckInChanged`, `NotificationsChanged`) với nguồn phát và phạm vi hợp lý; nếu `AccountChanged` chưa thuộc contract v1, ghi rõ schema bổ sung và test.
- Xác định điểm commit thật cho từng luồng: tạo/hủy/hết hạn reservation, thanh toán từ IPN/simulator/refund, check-in/inspection/handover, thay đổi sức chứa/catalog, tạo notification và thay đổi account. Chỉ phát sau khi DB commit; khi phát thất bại, giữ kết quả nghiệp vụ đã commit và thông báo degraded đúng, không gợi ý người dùng gửi lại thao tác. Worker phải có đường phát khi hết hạn hold. Tránh vòng lặp sự kiện và phát trùng do cả BLL/hook/Page cùng phát.
- Chỉ người dùng sở hữu, nhân viên đúng cơ sở còn phân công, hoặc admin còn quyền nhận dữ liệu theo phạm vi. Kiểm tra lại quyền khi kết nối và trước phát theo cơ chế T13; không dùng broadcast toàn cục cho payload riêng tư. Client reconnect/reload phải đọc lại trạng thái chuẩn từ server, không dựa vào sự kiện đã lỡ. Đăng ký listener trước khi start và xử lý mất kết nối hữu hạn.
- Tận dụng hạ tầng T13/T14 và DI chung do Gemini A sở hữu. Không sửa sâu tệp sở hữu của Gemini B/C nếu có thể dùng adapter/hook ở tệp T15; nếu bắt buộc, ghi rõ lý do và kiểm hồi quy.

## Kiểm thử bắt buộc

1. Build và chạy WebTests liên quan tới T10/T12/T13/T14 và T15; chạy toàn Web solution regression, ghi pass/fail/skip thật. Chạy `node --check` cho mọi JS thay đổi. Không chạy browser suites song song.
2. Test HTTP thật và WebSocket thật trên Kestrel với Cookie auth, scope và tối thiểu hai browser/client cho các luồng tích hợp; chứng minh receiver đúng nhận, người không có quyền không nhận, anonymous không kết nối. Kiểm nội dung DOM hoặc refetch thực tế, không chỉ mock `IHubContext`.
3. Test từng event của contract: payload/schema, nguồn commit, phạm vi người nhận, reconnect/refetch và degraded publisher. Với T12, IPN là nguồn sự thật; Return chỉ hiển thị. Test worker expire sau commit và scope của Catalog/Reservation. Kiểm âm bản dữ liệu nhạy cảm/PIN và bảo toàn ba CRUD event T13.
4. Chạy regression BLL/DAL phù hợp. Ghi rõ SQL integration nào skip và lý do; không nhận kết quả SQL chưa chạy làm PASS.
5. Đồng bộ **chỉ các tệp T15 đã sửa** cùng báo cáo sang `Final_Project` sau khi kiểm xung đột; đối chiếu SHA-256 từng tệp giữa hai workspace. Không ghi hash của báo cáo vào chính báo cáo.

## Bàn giao

- Cập nhật TaskBoard thành `T15 IMPLEMENTED — chờ Codex review`; giữ T16–T18 `WAITING`. Không tự đánh dấu T15 `VERIFIED`.
- Tạo `AgentReport/WebMigration_T15_R1_FindingFixes.md` ở cả hai workspace (vòng sửa tiếp theo tăng R2, R3...). Báo cáo liệt kê tệp thay đổi và lý do, đường gọi commit→publish→client, kết quả test thực tế kèm lệnh và số pass/fail/skip, lỗi còn lại, giới hạn môi trường, bảng SHA-256 từng tệp và hash báo cáo ở thông điệp bàn giao riêng.
- Chỉ trả `READY_FOR_CODEX_REVIEW` trong chính chat T15 này sau khi code, test, báo cáo và đồng bộ hoàn tất. Nêu đường dẫn tuyệt đối của hai báo cáo và SHA-256 của chúng. Codex sẽ review độc lập; nếu có finding, chỉ sửa theo prompt T15 của vòng tiếp theo trong chính chat này.
