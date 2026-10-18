# Prompt sửa finding vòng 2 — T13 SignalR CRUD ô kho

**Giao cho:** Gemini B (owner T13).  
**Workspace triển khai:** `D:\FPT\PRN222\Project\Final_Project_Web`  
**Workspace cần đồng bộ:** `D:\FPT\PRN222\Project\Final_Project`  
**Đầu vào review:** `AgentReport/WebMigration_T13_FindingFixes.md` và năm finding T13-R2 dưới đây.  
**Đầu ra bắt buộc:** mã sửa, test chạy được, báo cáo `AgentReport/WebMigration_T13_R2_FindingFixes.md` ở **cả hai** workspace.

Chỉ nhận task **T13**. Đọc mã, contract và test hiện tại trước khi sửa. Không gộp công việc T12 hoặc T14 vào báo cáo T13; nếu bắt buộc chạm một điểm tích hợp quản trị tài khoản để thu hồi kết nối, ghi rõ lý do, phạm vi và test hồi quy của điểm đó. Giữ nguyên route `/storageUnitHub`, tên ba phương thức Hub và ba sự kiện CRUD theo contract.

## T13-R2-F01 — Toàn bộ helper realtime không chạy do SyntaxError (P1)

**Nguồn lỗi:** `src/SelfStorageManagementSystem.Web/wwwroot/js/storage-units-realtime.js` khoảng dòng 129–203. Sau `return` trong `RealtimeUtils.generateRowContent`, thiếu dấu đóng hàm `};` trước khai báo `RealtimeUtils.matchesFilter` ở dòng 162. Lệnh `node --check src/SelfStorageManagementSystem.Web/wwwroot/js/storage-units-realtime.js` hiện thất bại với `SyntaxError: Unexpected token ')'` tại dòng 203. Vì vậy browser không tạo được `window.StorageUnitsRealtime`; các listener của trang `/StorageUnits` lỗi khi gọi `matchesFilter`, `generateRowContent` hoặc `showMessage`.

**Hướng xử lý:** Đóng đúng hàm `generateRowContent` và kiểm tra cú pháp toàn bộ JavaScript liên quan. Chạy trang Index bằng browser thật, xác nhận script tải thành công, không có `SyntaxError`/`ReferenceError` ở console, rồi phát Create/Update/Delete thật để xác nhận listener hoạt động. Không chỉ sửa dấu ngoặc và dừng ở `dotnet test`: trình biên dịch .NET không parse tệp JS tĩnh.

**Điều kiện hoàn thành:** `node --check` exit code 0 trên tệp này ở hai workspace; `window.StorageUnitsRealtime.generateRowContent` và `.matchesFilter` đều là function; một browser đang mở Index nhận và xử lý sự kiện từ browser thứ hai; test tự động phải fail nếu tái tạo thiếu `};`.

## T13-R2-F02 — Kết nối WebSocket còn nhận dữ liệu sau khi bị thu hồi quyền cơ sở (P1)

**Nguồn lỗi:** `src/SelfStorageManagementSystem.Web/Hubs/StorageUnitHub.cs` dòng 60–89 chỉ gán connection vào `facility_{id}` trong `OnConnectedAsync`. `StorageUnitRealtimePublisher.cs` dòng 39–41, 59–61, 76–78 gửi sự kiện theo group. Khi admin chấm dứt phân công cơ sở hoặc thu hồi vai trò trong lúc nhân viên giữ WebSocket mở, connection vẫn ở group cũ; sự kiện sau đó tiếp tục tới người không còn quyền. `OnValidatePrincipal` của Cookie không tự chạy lại trên từng frame của WebSocket.

**Hướng xử lý:** Thiết kế cơ chế thu hồi **connection đang sống** khi quyền/assignment đổi: quản lý connection theo user, cập nhật hoặc gỡ các group sau mutation, hoặc đóng connection để client xác thực lại. Đặt hook ở nơi bao phủ mọi đường cập nhật vai trò, trạng thái tài khoản và phân công cơ sở liên quan; chỉ kiểm tra quyền khi `Hub` được invoke là không đủ cho client chỉ lắng nghe. Xác định rõ hành vi khi user bị khóa, role bị thu hồi, assignment bị chấm dứt và khi reconnect. Không dựa vào cookie hết hạn sau 5 phút làm cơ chế thu hồi tức thời.

**Điều kiện hoàn thành:** Trong test có kết nối SignalR mở liên tục: staff A ban đầu nhận event cơ sở A; admin chấm dứt assignment A; không reconnect thủ công; event CRUD tiếp theo của A **không** đến staff đó. Tương tự với role quản trị bị thu hồi hoặc tài khoản bị khóa. Người còn quyền vẫn nhận event; kết nối mới/reconnect nạp đúng phạm vi hiện tại. Test phải xác minh **event delivery**, không chỉ HTTP/Hub invoke trả 403.

## T13-R2-F03 — Bộ lọc `SearchTerm` ở client không giống server (P2)

**Nguồn lỗi:** `StorageUnitAdminRepository.cs` dòng 125–132 tìm trong `unit_code`, `notes`, `floor_label`, `zone_label`. Helper mới tại `wwwroot/js/storage-units-realtime.js` dòng 188–194 lại tìm `unitCode`, `typeName`, `facilityName`. Một ô có `notes`/tầng khớp search có thể bị client bỏ; một ô chỉ có tên loại/cơ sở khớp lại được chèn dù server sẽ không trả. Event DTO cố ý không chứa `notes` nội bộ, nên client không đủ dữ liệu để tự quyết định mọi search.

**Hướng xử lý:** Dùng cùng quy tắc với truy vấn server. Với `SearchTerm` đang bật và event không chứa đủ trường để so khớp an toàn, invalidation/refetch theo URL/filter hiện hành hoặc gọi endpoint trả danh sách có kiểm tra quyền; **không đưa `notes` nội bộ vào broadcast** chỉ để sửa bộ lọc. Đảm bảo Create, Update và Delete đều giữ kết quả đúng.

**Điều kiện hoàn thành:** Test có ô khớp search chỉ ở `notes` hoặc `floor_label`, và ô chỉ khớp `typeName`/`facilityName`; sau event, bảng tương ứng với kết quả GET/refetch từ server. Không lộ `notes` trong payload SignalR. Test thực thi code client thật thay vì viết lại thuật toán trong C# hoặc HTML test.

## T13-R2-F04 — Prepend ô mới làm sai sort và dữ liệu trang 1 (P2)

**Nguồn lỗi:** `Pages/StorageUnits/Index.cshtml` dòng 338–353 luôn xóa hàng cuối khi đầy rồi prepend Create ở trang 1. Repository (`StorageUnitAdminRepository.cs` dòng 135–152) mặc định sắp tăng theo `facility_id`, `unit_code` và còn nhiều kiểu `SortBy`. Ví dụ trang đầu chứa mã `A-01` đến `A-10`, ô mới `Z-99` không thuộc trang đầu nhưng bị chèn lên đầu, đẩy `A-10` khỏi bảng. Update đổi `unitCode` cũng có thể làm thứ tự sai mà listener chỉ thay `innerHTML`.

**Hướng xử lý:** Trước khi sửa DOM, quyết định đúng vị trí theo sort và page hiện hành; nếu không xác định chắc chắn từ DTO, refetch/reload danh sách với nguyên query, phân trang và quyền server. Chỉ prepend/highlight khi thứ tự thực sự cho phép. Xử lý cả Create, Update đổi khóa sort, và Delete cần kéo bản ghi kế tiếp vào trang đầy. Không coi việc giữ `rowCount <= pageSize` là đủ.

**Điều kiện hoàn thành:** So sánh ID/thứ tự hàng sau event với kết quả GET server ở ít nhất sort mặc định `unit_code`, `created_at` tăng/giảm, trang 1 đầy và trang 2. Không có hàng ngoài trang, mất hàng đúng hoặc sai thứ tự; số tổng và khoảng “Hiển thị … đến …” phản ánh dữ liệu mới hoặc được refetch.

## T13-R2-F05 — Razor HTML-encode làm sai chuỗi bộ lọc JavaScript (P2)

**Nguồn lỗi:** `Pages/StorageUnits/Index.cshtml` dòng 255–257 nội suy `PhysicalStatus` và `SearchTerm` trực tiếp vào JavaScript trong dấu nháy đơn. Razor HTML-encode ký tự đặc biệt, nhưng nội dung `<script>` không được trình duyệt HTML-decode như text thường. Search `A&B` thành `A&amp;B` ở `currentFilter`, nên `matchesFilter` không nhận event cho mã `A&B` mà server đã tìm đúng.

**Hướng xử lý:** Truyền dữ liệu từ Razor sang JavaScript bằng JSON serialization đúng ngữ cảnh, với encoder an toàn cho `<script>`, hoặc qua thuộc tính `data-*` đã được Razor encode và đọc bằng DOM `dataset`. Không dùng chuỗi HTML-encoded làm giá trị JavaScript, không dùng `Html.Raw` trên input chưa được JSON-encode an toàn.

**Điều kiện hoàn thành:** Test với search chứa `&`, dấu nháy, dấu `<`/`>` và Unicode: giá trị JS bằng chính xác giá trị server nhận, không có XSS hay syntax error; URL/query và bộ lọc sau refetch vẫn được giữ nguyên.

## Bằng chứng test bắt buộc

1. Chạy `node --check` cho helper và các script T13 được sửa trên **cả hai** workspace. Ghi lệnh và exit code.
2. Biến `tests/SelfStorageManagementSystem.WebTests/BrowserTests/storage-units-realtime-tests.html` thành test **được test runner gọi tự động**, hoặc thay bằng browser test tự động hiện có (có thể tái dùng Edge/CDP harness trong WebTests). Test phải tải script và listener từ ứng dụng thật, không copy thủ công nhánh `if`/logic production vào tệp test. Nếu browser không chạy được, báo `BLOCKED` và lý do cụ thể; không ghi “7/7 PASS”.
3. Chạy test T13 có sẵn, test mới cho năm finding trên, rồi regression WebTests. Ghi số pass/fail/skip từng lệnh. Kiểm tra console error và có bằng chứng hai browser/context nhận SignalR thật.
4. Đồng bộ từng tệp T13 đã sửa sang `Final_Project` sau khi kiểm tra xung đột. Đối chiếu SHA-256 của **mọi** tệp T13 đụng tới, gồm test và báo cáo. Không ghi đè thay đổi T12/T14 ngoài điểm tích hợp thu hồi quyền đã giải thích.

## Báo cáo bàn giao cho Codex review

Tạo `AgentReport/WebMigration_T13_R2_FindingFixes.md` tại `Final_Project_Web`, rồi đồng bộ bản khớp sang `Final_Project`. Báo cáo phải có bảng riêng cho **T13-R2-F01 đến F05**: nguyên nhân gốc, dòng/tệp trước và sau, quyết định thiết kế, test gắn với finding, log/lệnh chạy mới, bằng chứng browser thật, bảng SHA-256 hai workspace, rủi ro còn lại. Nêu rõ test nào từng chỉ kiểm tra logic copy hoặc chưa chạy. Đặt trạng thái phần sửa là **`READY_FOR_CODEX_REVIEW`**; Task Board giữ **T13 `IMPLEMENTED — chờ Codex review`** và **T15 `WAITING`** cho đến khi Codex nghiệm thu. Khi bàn giao, gửi đường dẫn báo cáo `.md` và tóm tắt tối đa vài dòng; không tự tuyên bố đã `VERIFIED`.
