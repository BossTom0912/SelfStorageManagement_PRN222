# Prompt giao agent sửa backend Function 5

Copy toàn bộ nội dung trong khối dưới đây vào agent triển khai. Báo cáo xác minh đi kèm: [Function5BackendBugVerification.md](D:/FPT/PRN222/Project/Final_Project/AgentReport/Function5BackendBugVerification.md).

```text
Bạn sửa các bug đã được Codex xác minh trong backend Function 5: Check-in & Digital Handover.
Workspace: D:\FPT\PRN222\Project\Final_Project.

Hãy sửa code và kiểm thử thực tế, không chỉ đưa kế hoạch. Sau khi hoàn tất, tạo
AgentReport/DoneFunction5BackendFix.md để Codex review lại.

1. Đọc nguồn và bảo toàn trạng thái

- Đọc đầy đủ CODEX_RULE.md, Agent/Gemini/RULE.md, và AGENTS.md nếu có.
- Đọc AgentReport/Function5BackendBugVerification.md; đối chiếu từng finding
  trực tiếp với code hiện tại. Đọc prompt gốc Agent/Gemini/FUNCTION5_BACKEND_PROMPT.md
  để giữ các quyết định nghiệp vụ đã chốt.
- Đọc DTO, controller, Service/Repository/FacilityScopeService, tests, schema
  thực tế và triggers liên quan. Báo cáo Done cũ không phải nguồn sự thật.
- Kiểm tra git status, branch và HEAD. Trạng thái Codex vừa xác minh:
  branch Check-in-and-Digital-Handover, HEAD
  ba599208badb2f0e011ae65355d088d0374f1acd; Function 5 đang chưa commit.
- Bảo toàn cả tracked và untracked changes. Không tạo lại nhánh, switch nhánh
  khác, reset, git clean, ghi đè code người khác hoặc xóa các báo cáo lịch sử.
  Nếu trạng thái đã thay đổi, xem diff mới trước khi tiếp tục.
- Chỉ sửa backend Function 5, test và tài liệu cần thiết. Không sửa WPF,
  email/QR/khóa thật, checkout/renewal hoặc backlog Function 4.
- Giữ .NET 8, EF Core Database First, SQL Server, Controller -> Service ->
  Repository, ApiResponse<T>, DI và exception handling hiện tại.
- Không sửa schema/trigger database ứng dụng, generated entities hoặc
  generated DbContext; không thêm migration, framework/package hoặc một
  kiến trúc transaction tổng quát mới.
- Không tự commit, push, merge hoặc tuyên bố sẵn sàng nghiệm thu khi còn blocker.

2. Tạo regression tests làm rõ lỗi trước khi sửa

Baseline vừa chạy lại: 200 test trong tập không dùng SQL Server passed.
Lệnh đã dùng:
dotnet test SelfStorageManagementSystem.sln --no-restore --filter
"FullyQualifiedName!~SqlServerConcurrencyTests" --verbosity quiet
(Viết lệnh trên cùng một dòng khi chạy.)

Codex đã tái hiện:
- Item condition " Damaged " -> completed/good -> complete thành công.
- Item condition not_applicable -> completed/good -> complete thành công.
- Complete chờ khóa, user chuyển disabled, thả khóa -> vẫn complete thành công.
- Giữ khóa reservation nhưng verify-id vẫn ghi rejected.
- SQL provider: credential CLR id=0 trước SaveChanges, key temporary=true.
- SQL transaction: rollback sau commit throw transaction completed.

Viết regression tests có assertion về hành vi đúng; ghi lại lỗi trước sửa.
Không sửa expectation để hợp thức hóa bug. Probe InMemory của Codex không
thay thế test SQL Server về constraint, generated ID, rollback và concurrency.

3. Sửa F5-01/F5-04: inspection và unsafe

Files trọng tâm: CheckInService.cs, UnitInspectionRequest.cs,
ICheckInRepository.cs, CheckInRepository.cs.

- Chuẩn hóa item name/category và condition đúng một lần. Dùng cùng kết quả
  chuẩn hóa cho validation, suy ra trạng thái và persistence; không kết luận
  từ chuỗi gốc rồi lưu chuỗi đã normalize.
- Bốn mục bắt buộc: door, lock, lighting, cleanliness; giữ alias tiếng Việt
  đã hỗ trợ. Không thiếu hoặc trùng category sau chuẩn hóa.
- Tất cả good -> completed/good; có acceptable và không failure ->
  completed/acceptable; có damaged/missing -> failed/damaged.
- Với checklist bắt buộc V1, not_applicable không đủ điều kiện: trả 400
  trước khi ghi dữ liệu. Null item/name/condition hoặc condition không hợp lệ
  trả 400 rõ ràng, không NullReferenceException/500.
- inspection_items.condition SQL chỉ nhận good, acceptable, damaged,
  missing, not_applicable. Không ghi unsafe vào cột này.
- Thêm assessment boolean có tên rõ nghĩa, ví dụ IsUnsafe, vào request
  inspection (mặc định false, giữ các field cũ). Staff đánh giá unsafe ->
  inspection failed, overall_condition=unsafe theo schema hiện có.
  Không đoán unsafe từ OverallSummary; không đổi CHECK để hợp thức hóa item unsafe.
- Nếu client gửi item condition unsafe, trả 400 và hướng dẫn dùng assessment.
- Failed inspection phải lưu biên bản/items, release allocation với ended_at
  và chuyển unit reserved -> maintenance trong cùng transaction. Giữ
  reservation confirmed/agreement scheduled, không phát sinh inspection fee.
- Inspection thuộc allocation hiện tại, sau allocation.created_at; không
  tái sử dụng inspection khi release/reassign. Complete chỉ nhận inspection
  đạt hợp lệ của allocation hiện tại; áp dụng yêu cầu mới nhất đã chốt.
- Mutation inspection có thể giải phóng capacity: lấy reservation lock rồi
  capacity lock theo cùng thứ tự các thao tác còn lại, đọc lại sau khóa.

Tests bắt buộc: good, acceptable, " Damaged ", DAMAGED, missing,
not_applicable, unsafe assessment, item unsafe, null/invalid, thiếu/trùng,
failed/reassign/stale inspection. Test HTTP + SQL chứng minh unsafe không
gây lỗi CHECK/500; failed không thể complete.

4. Sửa F5-02/F5-03: tuần tự hóa và quyền mới sau khóa

- Mọi mutation cùng reservation, gồm verify, assign, release, inspection,
  complete/replay và reissue, dùng SS_Reservation_{reservationId}.
- Trên SQL dùng sp_getapplock với LockOwner=Transaction, xử lý timeout/cancel
  và trả khóa khi transaction kết thúc. InMemory chỉ là fallback phục vụ test.
- Thứ tự chung: reservation -> capacity nếu đổi allocation/sức chứa ->
  facility PIN issuance nếu cấp/cấp lại PIN. Không có nhánh lấy khóa ngược.
- Verify đọc lại profile/reservation sau khóa. Ghi profile bổ sung và verified
  nguyên tử; rejected không đổi identity_number. Số đã có không bị ghi đè.
- Hai reservation khác nhau có thể cùng customer: bảo vệ lần bổ sung số bằng
  conditional update/row lock phù hợp hoặc khóa customer có thứ tự thống nhất;
  đọc lại và so khớp nếu request khác đã bổ sung trước. Không dùng mỗi khóa
  reservation rồi cho phép hai request cùng ghi đè profile trống.
- Sau khi lấy khóa và trước quyết định ghi/replay, kiểm tra quyền từ snapshot
  DB mới: user active; actual role facility_staff/facility_manager; employee
  active; assignment đúng facility, starts_at <= now < ends_at (hoặc null).
  Không tin staff ID/role/facility từ body hoặc chỉ tin role trong JWT.
- Giữ policy nghiệp vụ/quyền ở BusinessLogic; DAL phụ trách SQL, khóa và
  persistence. Có thể dùng một API transaction riêng cho check-in nhận
  callback validation từ Service với snapshot DAL vừa đọc sau khóa, hoặc
  thiết kế nhỏ tương đương. DAL không tham chiếu BusinessLogic. Controller
  không inject DbContext, repository hay điều khiển transaction.
- Không gọi lại helper dựa trên entity cũ rồi coi là fresh: UserRepository
  GetByIdWithRolesAndProfilesAsync hiện tracking. Dùng projection/AsNoTracking
  cho permission snapshot hoặc reload có kiểm soát; không Clear tracker
  toàn cục. Các entity workflow đã preload cũng phải được đọc mới sau khóa.
- Không mở rộng bypass Admin/Operations Manager; giữ quyền chức năng khác.
- Business validation cuối gồm ngày tại facility, checkout invoice kỳ đầu
  đúng liên kết và paid đủ, verification mới nhất, IDs/status allocation,
  inspection và unit. Không chỉ kiểm tra trước transaction.
- Không có network call trong transaction. Giữ thời gian giữ khóa ngắn.

Tests SQL dùng hai DbContext độc lập và barrier/gate có timeout:
- Giữ reservation app lock bằng transaction A; request B verify phải chờ;
  không có verification mới trước khi A thả khóa.
- Verify rejected đã commit trước lượt complete lấy khóa -> complete từ chối,
  không handover/credential/notification. Các request cạnh tranh phải tuần tự
  theo khóa, không dùng snapshot verification đã đọc ngoài khóa.
- Request đã qua precheck nhưng đang chờ khóa; context khác disable user /
  revoke role / inactive employee / kết thúc assignment; thả khóa -> 403,
  không mutation. Có test complete, reissue và các mutation còn lại phù hợp.
- Sai facility, future/expired assignment và admin/operations-only bị từ chối.
- Hai reservation bổ sung giấy tờ khác nhau cho cùng profile trống không
  được ghi đè nhau; kết quả/verification phù hợp số được giữ lại.
Không dùng sleep làm cơ chế chứng minh thứ tự hoặc semaphore process-local
để thay bằng chứng sp_getapplock trong SQL Server.

5. Sửa F5-05: outcome mismatch và vòng đời transaction

- Code cũ commit rejected rồi throw trong try, catch gọi RollbackAsync và
  che DOCUMENT_NUMBER_MISMATCH. Loại bỏ cấu trúc này.
- Ưu tiên repository trả outcome typed (verified/rejected/mismatch) sau khi
  transaction hoàn tất; Service map mismatch thành BadRequestException 400.
  Hoặc throw nghiệp vụ bên ngoài vùng catch rollback. Không throw rồi rollback
  transaction đã commit. Cleanup không được làm mất exception gốc.
- Vẫn lưu rejected khi số không khớp; không đổi profile hoặc tự sửa họ tên.
- Chỉ map lỗi SQL/constraint/concurrency đã nhận diện. Không catch mọi lỗi
  thành conflict/bad request, không nuốt lỗi để trả thành công giả.
- Kiểm tra các mutation còn lại có cùng lỗi vòng đời transaction không.

Tests SQL/API: mismatch -> 400 ApiResponse, đúng một rejected, profile cũ giữ
nguyên; không lỗi transaction completed, không handover/credential. Rejected
thông thường và verified/bổ sung giấy tờ vẫn hoạt động nguyên tử.

6. Sửa F5-06: audit PIN gắn ID thật

- ID SQL identity chưa có trong CLR entity trước SaveChanges. Trong một
  transaction: revoke credential cũ + lưu credential mới -> lấy ID thật ->
  lưu audit -> commit. Audit lỗi phải rollback toàn bộ hai bước trước.
- entity_id và new_values.credential_id phải > 0, khớp credential thực tế
  và response; old_values trỏ đúng credential bị revoke.
- actor role dựa trên danh tính/quyền server thực tế; không hardcode mọi
  manager thành facility_staff, không lấy role từ request.
- Audit, notification, log, display_hint, URL và API đọc không có PIN thô,
  HMAC key/digest hoặc số giấy tờ. Không serialize toàn request vào audit.
- Reissue vẫn yêu cầu đối chiếu giấy tờ lại, active agreement/trong kỳ thuê,
  currentCredentialId đúng. Retry ID cũ không tạo thêm credential.
- Giữ issuance lock chung theo facility, RNG 6 chữ số giữ số 0 đầu,
  HMAC-SHA256 có key bên ngoài source, giới hạn 20 collision attempts,
  expires_at đầu ngày end_date theo timezone cơ sở và Cache-Control no-store.

Tests SQL: audit ID thật; lỗi khi lưu audit -> PIN cũ còn active, không credential
mới/audit dở dang; hai reissue cùng ID -> đúng một credential mới, request còn
lại conflict rõ ràng, không lộ PIN trong API đọc/notification/audit/log.

7. Sửa F5-07/F5-08: tài liệu khớp contract và cấu hình

- Đối chiếu Swagger/DTO thực tế cuối cùng, cập nhật DoneFunction5Backend.md
  và DoneFunction5.md; ghi rõ đã được sửa sau review nếu giữ phần kết quả cũ.
- Verify fields đúng: verificationMethod, documentNumber, isConfirmedMatch, notes.
- Reissue fields đúng: currentCredentialId, documentNumber, isConfirmedMatch, reason.
- Inspection docs phải mô tả assessment unsafe mới và condition được hỗ trợ.
- Code hiện đọc CheckIn:PinHmacSecret hoặc CHECKIN_PIN_HMAC_SECRET. Dùng tên
  thống nhất trong code/tests/docs. Env CheckIn__PinHmacSecret là lựa chọn
  của configuration provider nếu giữ mặc định ASP.NET Core.
- Không thêm key mặc định/secret thật vào repo hoặc báo cáo. Có test fake key
  riêng; thiếu/sai key không được tạo trạng thái bàn giao một phần.
- Sửa mô tả Authorize ở MeController, masking, headers, notifications và tên
  test theo code cuối. Không thêm chức năng ngoài phạm vi để làm đúng báo cáo sai.
- Notification bắt buộc ở complete; reissue chỉ cần audit theo scope đã chốt.
- Không dùng “100% hoàn tất”, “toàn bộ suite passed” nếu SQL bị excluded/skipped
  hoặc rollback/trigger/concurrency chưa được kiểm chứng.
- Function5BackendPreparation.md là báo cáo lịch sử; không sửa thành báo cáo
  triển khai mới. Tài liệu xác minh của Codex là snapshot trước sửa, giữ nguyên.

8. Sửa F5-09: kiểm thử SQL có assertion đủ mạnh

- Tranh cùng ô: đúng một assign thành công, request kia Conflict đã nhận diện;
  đúng một active allocation. Không chấp nhận exception bất kỳ như 500/deadlock
  chưa được xử lý mà vẫn coi test passed.
- Hai assign cùng reservation/cùng ô: không nhân đôi allocation.
- Complete cùng allocation/inspection: cả hai request thành công; đúng một
  initial completion, một IsAlreadyCompleted=true/Pin=null replay. Một
  handover check_in, một credential pin, một notification dedup theo agreement.
- Complete replay sai IDs -> 409, không ghi thêm dữ liệu.
- Complete initial thành công trả PIN sau commit; API đọc/replay không PIN.
- Fault injection bằng interceptor/fixture chỉ trong test, ở thời điểm một
  phần SQL writes đã chạy nhưng chưa commit. Kiểm tra bằng DbContext mới:
  reservation confirmed, agreement scheduled, unit reserved, allocation
  active, không handover/credential/notification mới; verification/allocation/
  inspection chuẩn bị trước đó vẫn còn. Request retry sau bỏ fault thành công.
- Không thêm endpoint debug/fault vào production, không thêm cờ bỏ trigger.
- Inspection failed/đổi ô không tái dùng inspection cũ; luồng chạy qua trigger thật.
- Capacity regression: trước assign, sau assign, release, complete; so sánh
  FacilityCatalogRepository, ReservationRepository.GetAvailableCapacityAsync
  và kiểm tra tạo hold dưới capacity lock. Reservation có allocation qua
  agreement chỉ được tính một lần; rental allocation active giữ sau complete.
- Giữ reservation_id=null cho rental allocation gắn agreement. Không ghi
  allocation active sau unit occupied, không ghi unit_status_history thủ công.

9. Môi trường SQL, chạy kiểm tra và báo cáo bàn giao

- Dùng SELFSTORAGE_SQL_TEST_CONNECTION_STRING, không in connection string.
- SelfStoragePRN222_Test đã tồn tại ở lần xác minh; kiểm tra lại target/schema
  trước khi dùng. Không mặc định tái tạo hoặc xóa DB test đang có dữ liệu khác.
- Bắt buộc guard DB name có test và khác SelfStoragePRN222; mọi batch/fixture
  phải nhắm cùng DB test đã kiểm tra. Chỉ seed/dọn rows do chính run tạo.
- Không chạy nguyên DB/SelfStoragePRN222_SQLServer_CleanInstall.sql: script
  DROP DB ứng dụng, có nhiều USE SelfStoragePRN222. Nếu cần dựng DB test,
  tách schema/triggers/seed và kiểm tra mọi target trước khi thực thi.
- Không dùng EnsureCreated, migrations hoặc InMemory để thay SQL acceptance.
- Build sau các thay đổi có ý nghĩa. Cuối cùng clean/restore/build solution,
  chạy non-SQL suite và SQL suite với env đã cấu hình. Ghi đúng passed/failed/
  skipped/excluded; SQL thiếu môi trường là blocker, không phải pass.
- Kiểm tra API startup/Swagger và ít nhất các HTTP regression quan trọng;
  dừng process thử nghiệm do bạn tạo khi xong, không dừng process người khác.
- Nếu phát hiện vấn đề Function 4 có trước, tách bằng chứng và phạm vi; chỉ
  sửa khi thực sự chặn Function 5 và giải thích rõ, không dọn backlog ngoài scope.

Tạo AgentReport/DoneFunction5BackendFix.md bằng tiếng Việt, gồm:
1) branch, baseline, trạng thái Git trước/sau và phạm vi sửa;
2) bảng F5-01...F5-09: nguyên nhân, file/line sửa, regression test và kết quả;
3) thay đổi API/DTO (đặc biệt unsafe assessment), JSON ví dụ không chứa bí mật;
4) thứ tự khóa, cách validation và quyền chạy trên dữ liệu mới sau khóa,
   lifetime transaction, rollback/replay, cách audit nhận ID thật;
5) lệnh thực tế đã chạy, build errors/warnings, test counts riêng unit/API/SQL,
   environment target an toàn và kết quả runtime;
6) SQL assertions cho conflict/replay/credential/notification/rollback,
   capacity, quyền và verification concurrency; ghi rõ phần chưa kiểm chứng;
7) files thay đổi, tài liệu Done cũ đã đính chính, rủi ro/blocker còn lại và
   checklist để Codex review lại. Không đưa key, PIN, giấy tờ thật hoặc connection string.

Dừng ở nhánh hiện tại để Codex review. Không commit/push/merge main.
```
