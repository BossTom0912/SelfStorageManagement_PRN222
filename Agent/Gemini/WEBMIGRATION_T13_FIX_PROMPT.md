# Prompt sửa finding T13 — SignalR CRUD ô kho

**Giao cho:** Gemini B (owner T13). **Task:** T13, Đợt 5. **Workspace triển khai:** `D:\FPT\PRN222\Project\Final_Project_Web`.

Hãy sửa riêng ba finding T13 dưới đây. Kiểm tra contract, service và test hiện tại trước khi sửa. Giữ tên endpoint `/storageUnitHub` và tên các sự kiện CRUD đang được client dùng, trừ khi việc sửa bảo mật buộc phải điều chỉnh và đã cập nhật toàn bộ call site. Không sửa T12 hoặc T14 trong task này.

## T13-F01 — Broadcast lộ dữ liệu tới mọi tài khoản đăng nhập (P2)

- **Nguồn lỗi:** `src/SelfStorageManagementSystem.Web/Hubs/StorageUnitHub.cs` dòng 28 chỉ có `[Authorize]`; `src/SelfStorageManagementSystem.Web/Realtime/StorageUnitRealtimePublisher.cs` dòng 39, 57, 73 dùng `Clients.All`. Một customer có thể kết nối Hub, không gọi CRUD nhưng vẫn nhận `Receiver_CreateStorageUnit`/`Receiver_UpdateStorageUnit`/`Receiver_DeleteStorageUnit`, gồm ID, trạng thái, giá, `isListed` và vị trí ô kho. Nhân viên thuộc cơ sở A cũng nhận sự kiện của cơ sở B.
- **Hướng giải quyết:** Tách quyền **kết nối/nhận** khỏi quyền **gọi CRUD**. Chỉ cho vai trò phù hợp tham gia kênh quản trị, gán connection vào group cơ sở theo phạm vi hiện hành và gửi event CRUD đến group được phép; admin/toàn hệ thống nhận theo quyền tương ứng. Đảm bảo event Delete cũng có đủ facility ID để định tuyến mà vẫn giữ payload client theo contract. Nếu `CatalogInvalidated` cần đến khách hàng, phát qua kênh/sự kiện công khai chỉ chứa dữ liệu an toàn, không mở lại CRUD broadcast cho họ.
- **Điều kiện hoàn thành:** Customer không nhận bất kỳ CRUD event quản trị nào; staff/manager cơ sở A không nhận sự kiện cơ sở B; người có quyền trong đúng cơ sở vẫn nhận cả Create/Update/Delete từ Hub và Form POST; reconnect không mở rộng phạm vi. Test phải kiểm tra **subscribe và nhận event**, không chỉ kiểm tra invoke bị từ chối.

## T13-F02 — Mất cập nhật đồng thời trên trang Edit (P2)

- **Nguồn lỗi:** `src/SelfStorageManagementSystem.Web/Pages/StorageUnits/Edit.cshtml` khoảng dòng 249–268. Listener `Receiver_UpdateStorageUnit` tự thay mã ô và `Input.ExpectedUpdatedAt`, nhưng các trường `AreaId`, `FloorLabel`, `ZoneLabel`, `Notes` của form vẫn là bản cũ. Người dùng bấm Lưu sau đó có thể vượt qua kiểm tra concurrency và ghi đè thay đổi của người khác.
- **Hướng giải quyết:** Khi form đang mở và nhận update từ phiên khác, không tự nhận ETag mới cho dữ liệu form cũ. Hiển thị cảnh báo bản đang xem đã lỗi thời, chặn lưu cho đến khi reload hoặc nạp lại đầy đủ dữ liệu với quyết định rõ ràng của người dùng. Nếu dùng tự cập nhật toàn bộ form thì phải xử lý ô đang được người dùng chỉnh và không âm thầm bỏ nội dung họ nhập.
- **Điều kiện hoàn thành:** Hai phiên cùng mở Edit; phiên A sửa một trường, phiên B đang giữ dữ liệu cũ; sau event, B không thể lưu đè thay đổi của A bằng ETag mới trong khi form còn dữ liệu cũ. Test đi qua client listener và thao tác lưu thực tế; test stale ETag ở service vẫn pass.

## T13-F03 — Dòng realtime phá bộ lọc và phân trang (P2)

- **Nguồn lỗi:** `src/SelfStorageManagementSystem.Web/Pages/StorageUnits/Index.cshtml` khoảng dòng 259–310. Listener Create chỉ so `facilityId`, trong khi server `Index.cshtml.cs` dòng 96–107 còn lọc theo `UnitTypeId`, `AreaId`, `PhysicalStatus`, `IsListed`, `SearchTerm`, sort và `PageNumber`/`PageSize`.
- **Hướng giải quyết:** Khi nhận Create/Update/Delete, chỉ sửa DOM nếu bản ghi và vị trí trang chắc chắn khớp bộ lọc/sort/pagination hiện hành. Nếu không thể xác định chắc chắn từ event DTO, hiển thị thông báo có thay đổi và reload/refetch dữ liệu theo query hiện tại. Đồng bộ số lượng/trạng thái trang sau thay đổi; không chèn một dòng ngoài điều kiện hoặc vượt quá page size.
- **Điều kiện hoàn thành:** Với từng bộ lọc loại, khu vực, trạng thái, niêm yết, từ khóa và trang 2, event không làm xuất hiện bản ghi không khớp hoặc sai phân trang. Kiểm chứng ít nhất một luồng Create, Update làm bản ghi rời bộ lọc, và Delete trên hai browser/context.

## Kiểm thử và báo cáo bắt buộc

1. Chạy test T13 và regression Web có liên quan; bổ sung test cho nhận event trái quyền, mất cập nhật trên Edit và bộ lọc/phân trang bằng browser hoặc DOM thực. Ghi lệnh, pass/fail/skip; không coi test server-side broadcast hiện có là bằng chứng đủ cho hành vi client.
2. Đồng bộ các tệp T13 đã sửa sang `D:\FPT\PRN222\Project\Final_Project` sau khi kiểm tra xung đột theo từng tệp.
3. Tạo **`AgentReport/WebMigration_T13_FindingFixes.md`** trong `Final_Project_Web` và bản đồng bộ tại `Final_Project`. Ghi từng finding T13-F01/F02/F03, nguyên nhân gốc, tệp và dòng sửa, kết quả test, bằng chứng hai browser, bảng diff/SHA-256 đồng bộ, rủi ro còn lại. Kết thúc bằng `READY_FOR_CODEX_REVIEW`; chưa tự xác nhận VERIFIED cho phần sửa finding.
4. Khi bàn giao, gửi đường dẫn báo cáo `.md` để Codex review lại riêng T13.
