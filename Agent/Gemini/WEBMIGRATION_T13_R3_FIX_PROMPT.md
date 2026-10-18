# Prompt sửa finding vòng 3 — T13 SignalR Realtime CRUD ô kho

**Giao riêng cho:** Gemini B, owner T13.  
**Workspace triển khai:** `D:\FPT\PRN222\Project\Final_Project_Web`  
**Workspace đồng bộ:** `D:\FPT\PRN222\Project\Final_Project`  
**Đầu vào:** `AgentReport/WebMigration_T13_R2_FindingFixes.md` và mã hiện hành.  
**Bàn giao bắt buộc:** mã sửa, test thực thi, `AgentReport/WebMigration_T13_R3_FindingFixes.md` ở cả hai workspace.

Chỉ xử lý **T13**. Giữ contract `/storageUnitHub`, ba phương thức Hub và ba event CRUD. Không tự nâng T13 lên `VERIFIED`; T15 giữ `WAITING` cho đến khi Codex review. Đọc code và test trước khi sửa. Các finding dưới đây là đường đi cần khắc phục, không được chỉ đổi lời giải thích trong báo cáo.

## T13-R3-F01 — Assignment tự hết hạn nhưng WebSocket vẫn ở group cơ sở (P1)

**Nguồn lỗi:** `src/SelfStorageManagementSystem.Web/Hubs/StorageUnitHub.cs:86-97` chỉ đọc `GetAccessibleFacilityIdsAsync` lúc kết nối. `FacilityScopeService.cs:130-134` coi assignment hết quyền khi `ends_at <= UtcNow`. Cơ chế mới `StorageUnitHubScopeRevocationService.cs:42-51` chỉ chạy khi `AdminAccountService` gọi notifier sau một mutation; tới đúng thời điểm `ends_at` không có mutation, notifier hoặc worker nào chạy. Vì vậy staff kết nối trước hạn vẫn ở `facility_{id}` và nhận event sau khi quyền đã hết hạn. Assignment có `starts_at` trong tương lai cũng không tự được thêm group khi bắt đầu có hiệu lực.

**Hướng xử lý:** Đồng bộ quyền của connection đang mở tại ranh giới thời gian assignment. Có thể lập lịch theo `starts_at`/`ends_at` hoặc dùng worker quét và đối chiếu với DB; chọn cách có vòng đời, hủy lịch và xử lý restart rõ ràng. Khi hết hạn phải gỡ group trước event CRUD tiếp theo; khi bắt đầu phải thêm group theo vai trò hiện hành. Không dựa vào reconnect hoặc request HTTP mới của client. Tái sử dụng một hàm reconcile quyền, tránh hai quy tắc khác nhau giữa lúc connect và lúc mutation.

**Điều kiện hoàn thành:** Test giữ nguyên một WebSocket của staff, assignment `ends_at` trong tương lai gần, nhận event trước hạn và **không nhận** event sau hạn mà không gọi `TerminateFacilityAssignmentAsync`; kiểm thêm `starts_at` tương lai được cấp quyền đúng lúc. Test xác nhận user ở cơ sở khác vẫn nhận đúng event và không phụ thuộc vào cookie hết hạn.

## T13-R3-F02 — Thu hồi group thất bại vẫn bị ghi nhận là thành công (P1)

**Nguồn lỗi:** `src/SelfStorageManagementSystem.Web/Realtime/StorageUnitHubScopeRevocationService.cs:66-75` và `:108-134` bắt mọi lỗi `RemoveFromGroupAsync`, chỉ log rồi xóa/cập nhật group trong tracker. Khi gỡ group thất bại, connection có thể vẫn ở group SignalR thật nhưng tracker đã quên group, nên lần reconcile sau cũng không thử gỡ lại. `AdminAccountService.cs:283-305`, `:399-423`, `:503-527`, `:563-583` lưu mutation trước rồi truyền tiếp `cancellationToken` của HTTP request cho notifier; admin đóng trang sau commit có thể khiến bước thu hồi bị hủy, không có retry.

**Hướng xử lý:** Sau commit, đưa việc thu hồi quyền qua đường thực thi đáng tin cậy, không phụ thuộc vào `RequestAborted`. Chỉ cập nhật tracker sau khi thao tác group thành công. Nếu gỡ group thất bại, giữ trạng thái cần thu hồi để retry có giới hạn hoặc đóng connection và kiểm tra đã đóng; không được im lặng coi là đã thu hồi. Đảm bảo lỗi thêm group không làm tracker báo đã cấp quyền. Quyết định và ghi rõ hành vi khi lỗi hạ tầng lặp lại.

**Điều kiện hoàn thành:** Test ép `RemoveFromGroupAsync` ném lỗi một lần và test hủy token HTTP ngay sau DB commit: group bị thu hồi ở lần retry/connection bị đóng, user không nhận event CRUD trái quyền; tracker phản ánh membership thực tế. Test đường thành công của khóa account, đổi role và chấm dứt assignment vẫn qua.

## T13-R3-F03 — Search chỉ khớp `notes` bị chặn trước refetch (P2)

**Nguồn lỗi:** Server `StorageUnitAdminRepository.cs:125-132` tìm trong `unit_code`, `notes`, `floor_label`, `zone_label`; event DTO cố ý không chứa `notes`. `wwwroot/js/storage-units-realtime.js:191-200` trả false khi chỉ `notes` khớp. `Pages/StorageUnits/Index.cshtml:319-327` gọi `matchesFilter` rồi `return` trước `refetchTable` ở dòng 330. Bởi vậy tạo ô có `notes` chứa từ khóa đang tìm không xuất hiện, dù GET server trả ô đó. Bình luận trong báo cáo rằng refetch bao phủ notes không đúng với nhánh này.

**Hướng xử lý:** Khi có `SearchTerm`, không được dùng event thiếu `notes` để kết luận âm tính. Dùng refetch theo URL hiện hành làm nguồn sự thật cho Create; Update và Delete cũng phải giữ kết quả cùng server. Vẫn giữ `notes` ngoài payload SignalR. Chỉ dùng lọc nhanh cho các điều kiện có đủ dữ liệu và không thể ảnh hưởng đến kết quả server.

**Điều kiện hoàn thành:** Browser test chạy **listener JS thật** với một ô chỉ khớp từ khóa qua `notes`, một ô chỉ khớp qua `floorLabel`, một ô chỉ khớp `facilityName` nhưng không nằm trong kết quả server; sau Create/Update/Delete, ID và thứ tự DOM bằng GET server. Test không được chép lại `matchesFilter` trong C# rồi coi là kiểm thử code client.

## T13-R3-F04 — Footer phân trang không chuyển được giữa một và hai trang (P2)

**Nguồn lỗi:** `Pages/StorageUnits/Index.cshtml:209-238` chỉ render toàn bộ `.card-footer`, `#storageUnitsTotalCount`, `#storageUnitsDisplayRange`, `#storageUnitsPaginationContainer` khi `TotalPages > 1`. `wwwroot/js/storage-units-realtime.js:225-241` chỉ copy nội dung khi cả phần tử mới lẫn phần tử cũ đều tồn tại. Nếu từ 10 lên 11 ô (page size 10), footer không được tạo và không thể tới trang 2. Nếu từ 11 xuống 10, footer cũ vẫn hiện dù server đã bỏ footer.

**Hướng xử lý:** Đặt vùng chứa footer ổn định rồi thay toàn bộ nội dung footer từ HTML server, bao gồm trường hợp nó xuất hiện hoặc biến mất; hoặc render footer cố định và đồng bộ visibility, count, range, link. Không chỉ thay `innerHTML` của các node có sẵn.

**Điều kiện hoàn thành:** Browser test ở page size 10: Create chuyển 10→11 phải xuất hiện link trang 2 và count 11; Delete chuyển 11→10 phải bỏ phân trang, range/count đúng; có test trang 2 khi item cuối bị xóa. DOM và các URL phân trang khớp GET server.

## T13-R3-F05 — Hai refetch có thể ghi đè kết quả mới bằng HTML cũ (P2)

**Nguồn lỗi:** Mỗi listener Create/Update/Delete ở `Index.cshtml:315-421` gọi `refetchTable` độc lập. `wwwroot/js/storage-units-realtime.js:212-223` áp dụng HTML ngay khi mỗi `fetch` hoàn tất, không kiểm tra thứ tự sự kiện hoặc request. Nếu Create rồi Delete xảy ra gần nhau, response Create có snapshot cũ về chậm hơn response Delete và chèn lại ô đã xóa vào bảng.

**Hướng xử lý:** Serialize/coalesce các lần refresh hoặc dùng thế hệ request để chỉ kết quả mới nhất được phép ghi DOM; nếu một refresh đang chạy mà event mới đến, phải bảo đảm lần cuối đọc trạng thái server mới nhất. Đồng bộ table, count, range và footer trong cùng một lần áp dụng. Tránh abort khiến dữ liệu mới không được áp dụng.

**Điều kiện hoàn thành:** Test có hai event liên tiếp với response giả lập hoàn tất đảo thứ tự: DOM cuối cùng bằng GET server sau event thứ hai, không hồi sinh ô đã xóa và không đảo lại sort/pagination. Chạy thêm event Create/Update liên tiếp trên browser thật.

## T13-R3-F06 — Nhánh fallback tái tạo lỗi sort/pagination khi GET thất bại (P2)

**Nguồn lỗi:** `Pages/StorageUnits/Index.cshtml:352-374` vẫn prepend ô mới và bỏ dòng cuối khi `refetchTable` trả false; `:398-413` cập nhật/gỡ hàng tại chỗ khi Update; `:433-440` chỉ xóa hàng khi Delete. Ví dụ sort `unit_code ASC`, trang đầu `A-01` đến `A-10`, event tạo `Z-99` và GET tạm lỗi: fallback chèn `Z-99` lên đầu, xóa `A-10` dù server sẽ giữ `A-01` đến `A-10`. Update và Delete cũng có thể làm bảng thiếu dòng từ trang sau hoặc sai count.

**Hướng xử lý:** Khi không có kết quả server đáng tin cậy, giữ snapshot hiện tại, hiển thị trạng thái chưa đồng bộ và retry/refetch có giới hạn hoặc nút reload; không tự khẳng định sort, membership hay count từ DTO thiếu dữ liệu. Khi retry thành công, áp dụng snapshot server mới nhất và xóa cảnh báo.

**Điều kiện hoàn thành:** Browser test ép GET trả lỗi khi nhận Create `Z-99` dưới sort tăng và khi Delete ở trang đầy: DOM không bị chèn/xóa sai, có thông báo cần đồng bộ; sau retry thành công DOM bằng GET server. Không báo lỗi realtime đã xử lý thành công khi bảng chưa cập nhật.

## Kiểm thử và bàn giao

1. Chạy `node --check` cho script T13 ở hai workspace; ghi exit code.
2. Chạy HubTests và BrowserTests T13, rồi WebTests regression. Ghi chính xác pass/fail/skip và lệnh. Test browser phải nạp script/listener từ ứng dụng thật; test C# sao chép thuật toán client chỉ là hỗ trợ, không thay thế nghiệm thu UI.
3. Chứng minh các ca quyền trên WebSocket đang mở bằng event delivery; ghi log trước/sau ranh giới `ends_at`, cancel request và một lần lỗi gỡ group.
4. Đồng bộ **chỉ** các tệp T13 cần sửa giữa hai workspace sau khi kiểm tra xung đột; đối chiếu SHA-256 từng tệp T13 và báo cáo. Không ghi đè tệp T12/T14 không liên quan.
5. Tạo `AgentReport/WebMigration_T13_R3_FindingFixes.md` ở cả hai workspace. Báo cáo có bảng F01–F06: nguyên nhân, tệp/dòng sửa, quyết định kỹ thuật, test và log thực chạy, SHA-256, rủi ro còn lại. Ghi `READY_FOR_CODEX_REVIEW`; Task Board giữ T13 `IMPLEMENTED — chờ Codex review`, T15 `WAITING`.
