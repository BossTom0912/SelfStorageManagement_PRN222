# Prompt Gemini 3.8 Flash — Backend Function 5

Copy nội dung trong khối dưới đây vào agent Gemini. Nhánh `Check-in-and-Digital-Handover` đã được Codex tạo từ `Payment&RentalAgreement` tại commit `ba599208badb2f0e011ae65355d088d0374f1acd`. Backend Function 5 chưa được triển khai ở bước chuẩn bị này.

```text
Bạn triển khai backend Function 5: Check-in & Digital Handover cho dự án
D:\FPT\PRN222\Project\Final_Project.

Hãy triển khai code và kiểm thử, không chỉ trả lại kế hoạch.

1. Chuẩn bị và nguyên tắc

Đọc đầy đủ CODEX_RULE.md và Agent/Gemini/RULE.md. Đối chiếu mã nguồn,
docs/FIVE_CORE_FUNCTIONS_SUMMARY.md, docs/DB_SCHEMA_REPORT.md và
DB/SelfStoragePRN222_SQLServer_CleanInstall.sql. Schema SQL Server thực tế
là nguồn sự thật; không chỉ dựa vào bản tổng kết.

Kiểm tra git status và HEAD trước khi làm. Baseline đã được review là
ba599208badb2f0e011ae65355d088d0374f1acd trên Payment&RentalAgreement.

Codex đã tạo nhánh Check-in-and-Digital-Handover từ baseline trên.
Nếu đang ở nhánh đích, tiếp tục làm tại đó. Nếu chưa có nhánh đích, tạo:
git switch -c "Check-in-and-Digital-Handover" "Payment&RentalAgreement"
Nếu nhánh đã có nhưng chưa được checkout, kiểm tra lịch sử rồi dùng
git switch "Check-in-and-Digital-Handover"; không tạo lại hoặc ghi đè nhánh.

Nhánh mới phải giữ toàn bộ code/lịch sử của nhánh nguồn. Không di chuyển
file vật lý, không reset/clean, không ghi đè nhánh có sẵn, không tự commit,
push hoặc merge. Nếu trạng thái khác baseline, kiểm tra khác biệt và bảo
toàn thay đổi trước khi tiếp tục. Hai file chuẩn bị của Codex có thể đang
untracked: Agent/Gemini/FUNCTION5_BACKEND_PROMPT.md và
AgentReport/Function5BackendPreparation.md; giữ nguyên chúng.

Chỉ làm backend Function 5 và các chỉnh sửa backend cần thiết để nối đúng
với chức năng hiện có. Giữ .NET 8, EF Core Database First, SQL Server,
kiến trúc Controller → Service → Repository, ApiResponse<T>, DI và
exception handling hiện tại. Không thêm framework hoặc package khi API
có sẵn của .NET đáp ứng được.

Không sửa WPF, không triển khai email, QR, khóa cửa thật, checkout,
renewal hoặc check-out. Không thay schema ứng dụng, trigger, generated
entities hoặc generated DbContext để làm code mới chạy.

2. Điều kiện nghiệp vụ và quyền

Function 4 thành công tạo reservation confirmed và agreement scheduled,
chưa gán ô vật lý. Function 5 chỉ được kích hoạt từ trạng thái này.

Thao tác tại quầy yêu cầu tài khoản active, role facility_staff hoặc
facility_manager, employee_profile active và phân công đúng cơ sở còn
hiệu lực. Lấy người thao tác từ danh tính đã xác thực trên server.
Không lấy staff ID, role hoặc facility scope từ request.

Không dùng nhánh bypass của Admin/Operations Manager để thay thế điều
kiện trên. Nếu tái sử dụng FacilityScopeService, bổ sung kiểm tra riêng
cho thao tác check-in, giữ nguyên quyền của các chức năng khác.

Gán ô và complete chỉ được thực hiện khi:
- Ngày tại múi giờ cơ sở thỏa start_date <= today < end_date.
- Reservation confirmed, agreement scheduled.
- Hóa đơn checkout kỳ đầu liên kết đúng reservation/agreement, đã paid
  và paid_amount == total_amount.
- Kết quả xác minh mới nhất là verified.

Chưa hỗ trợ gán hoặc bàn giao sớm 24 giờ. Tra cứu vẫn trả trạng thái và
lý do chưa thể tiếp tục để client có thể hiển thị.

3. API và DTO

Thêm CheckInController, ICheckInService/CheckInService và
ICheckInRepository/CheckInRepository tại đúng layer.

API staff:
GET  /api/staff/check-in/search?code=...
GET  /api/staff/check-in/{reservationId}/eligible-units
POST /api/staff/check-in/{reservationId}/verify-id
POST /api/staff/check-in/{reservationId}/assign-unit
POST /api/staff/check-in/{reservationId}/release-unit
POST /api/staff/check-in/{reservationId}/inspection
POST /api/staff/check-in/{reservationId}/complete
POST /api/staff/check-in/{reservationId}/reissue-pin

API customer:
GET /api/me/units
GET /api/me/units/{agreementId}/handover
GET /api/me/notifications

Các API customer chỉ trả dữ liệu của người gọi; endpoint notifications
trong đợt này chỉ trả thông báo in_app của họ. Áp dụng phân trang hiện có
cho các danh sách.

Search trả thông tin đặt chỗ, khách, cơ sở/loại kho, kỳ thuê, trạng thái
thanh toán và tiến độ xác minh/allocation/inspection/handover để tiếp tục
quy trình sau khi mất kết nối. Che số giấy tờ; không trả secret_digest.

Request chỉ chứa dữ liệu nhân viên được nhập. Server quyết định trạng
thái, timestamps, người thực hiện và liên kết nghiệp vụ. Request inspection,
release và complete phải mang ID allocation hiện tại để phát hiện dữ liệu
client đã cũ; complete mang cả inspection ID.

Giữ quy ước HTTP 400/401/403/404/409/500 và ApiResponse<T>. Controller
không truy vấn DbContext, gọi Repository hoặc xử lý transaction.

4. Xác minh giấy tờ

V1 hỗ trợ government_id và passport, ghi identity_verifications với
verified/rejected, verified_by và thời điểm server.

Nếu hồ sơ đã có identity_number, so khớp giấy tờ và yêu cầu staff xác nhận
đã đối chiếu người nhận với hồ sơ. Không cho verified khi số không khớp;
không tự ghi đè số đã có.

Nếu identity_number đang trống, staff được bổ sung số giấy tờ sau khi
đối chiếu bản gốc và người nhận. Lưu số vào customer_profiles cùng kết quả
verified trong một transaction. Request bị rejected không cập nhật hồ sơ.

Không lưu số giấy tờ hoặc ảnh trong notes, audit payload hay log.
Không tự sửa họ tên khách để làm dữ liệu khớp.

5. Gán ô, kiểm tra và giải phóng

Ô ứng viên phải cùng facility và unit_type, is_listed, physical_status
available, không có maintenance work order còn hiệu lực blocks_booking,
không có active allocation trùng khoảng thuê [start_date, end_date).

Assign tạo allocation_kind rental, status active, agreement_id đúng hợp
đồng, reservation_id null, thời hạn bằng kỳ thuê và assigned_by lấy từ
server. Sau đó đổi ô available → reserved trong cùng transaction.

Một agreement chỉ có một allocation active cho quy trình này. Assign lặp
cùng ô trả allocation hiện có; chọn ô khác khi còn allocation active trả
409 và yêu cầu release trước.

Sửa cả truy vấn catalog và truy vấn reservation capacity, bao gồm kiểm
tra khi tạo hold dưới khóa: reservation đã được gán qua agreement không
còn được tính là reservation chưa gán.

Inspection gắn agreement và ô của allocation hiện tại. Checklist bắt
buộc gồm cửa, khóa, đèn và vệ sinh, không trùng hoặc thiếu mục.
Server suy ra kết quả:
- Các mục good/acceptable: completed, overall good hoặc acceptable.
- Có damaged/missing hoặc staff đánh giá unsafe: failed.

Check-in không phát sinh phí inspection. Inspection phải sau thời điểm
tạo allocation và không được tái sử dụng sau khi release/reassign.

Nếu inspection thất bại, trong cùng transaction lưu kết quả, release
allocation với ended_at và chuyển ô reserved → maintenance. Giữ reservation
confirmed và agreement scheduled để chọn ô khác.

Release chủ động chỉ áp dụng trước bàn giao, có lý do, release allocation
và trả ô reserved → available khi không còn allocation active khác.
Cho phép giải phóng allocation chờ bàn giao dù kỳ thuê đã hết.
Không xóa lịch sử verification, allocation hoặc inspection.

6. Complete, khóa và transaction

Business validation thuộc Service; SQL, khóa và ghi dữ liệu thuộc
Repository. Validation cuối cùng phải chạy với trạng thái đọc lại sau
khi lấy khóa trong transaction, không chỉ dựa vào pre-check ngoài khóa.

Các mutation của một reservation dùng cùng khóa
SS_Reservation_{reservationId}.
Thao tác đổi allocation/sức chứa lấy tiếp khóa
SS_Facility_{facilityId}_UnitType_{unitTypeId}_Capacity.
Dùng sp_getapplock với LockOwner Transaction và thứ tự khóa thống nhất;
đọc lại trạng thái và quyền thao tác sau khóa.

Complete kiểm tra lại quyền, thanh toán, ngày thuê, verification mới nhất,
allocation active, ô reserved và inspection mới nhất đạt của allocation
hiện tại. Yêu cầu customerAcknowledged=true để ghi nhận khách đồng ý nhận
bàn giao tại quầy; không giả lập ảnh/chữ ký điện tử.

Trong một transaction:
- Ghi đúng một handover check_in, liên kết cùng agreement/allocation/
  inspection/ô theo trigger.
- Chuyển agreement scheduled → active, ghi checked_in_at và signed_at.
- Chuyển reservation confirmed → checked_in.
- Chuyển ô reserved → occupied.
- Cấp credential và lưu thông báo in_app.

Giữ allocation rental active sau complete để bảo vệ thời hạn thuê.
Không ghi thêm unit_status_history vì trigger đã ghi.
Không insert/update allocation active sau khi ô đã occupied:
trigger kiểm tra bookable sẽ từ chối thao tác đó.

Complete lặp cùng allocation/inspection trả biên bản đã hoàn tất,
IsAlreadyCompleted=true và Pin=null; không thêm handover, credential hoặc
notification. ID khác với bàn giao đã lưu trả 409.

Lỗi trước commit phải rollback toàn bộ complete. Các bước chuẩn bị đã
lưu trước đó vẫn còn để tiếp tục. Không có network call trong transaction.

Chỉ map lỗi SQL/constraint/concurrency đã nhận diện; không chuyển mọi
DbUpdateException thành 409 hoặc mọi SqlException thành 400.

7. PIN và thông báo

Sinh PIN 6 chữ số bằng RandomNumberGenerator.GetInt32, giữ số 0 đầu.
Lưu secret_digest bằng HMAC-SHA256 với khóa bí mật từ User Secrets/env;
không có khóa mặc định trong source. Khóa thiếu/sai phải được báo rõ,
không tạo bàn giao một phần.

Tránh trùng PIN còn hiệu lực trong cùng facility; tuần tự hóa cấp/cấp lại
PIN bằng khóa issuance dùng chung, lấy sau các khóa nghiệp vụ. Giới hạn
20 lần sinh lại nếu gặp collision, sau đó báo conflict.

Credential pin active, issued_by là employee đang thao tác; expires_at
là đầu ngày end_date theo múi giờ cơ sở. Không lưu PIN thô vào DB,
display_hint, log, notification, email hoặc URL.

PIN chỉ xuất hiện trong response cấp mới thành công sau commit; response
chứa PIN phải dùng Cache-Control: no-store. API đọc và complete replay
không trả PIN.

Reissue-pin yêu cầu quyền tại quầy, khách được đối chiếu lại giấy tờ,
agreement đang active/trong kỳ thuê và ID credential hiện tại. Thu hồi
PIN cũ và tạo PIN mới nguyên tử. Request lặp với ID cũ không được tạo
thêm credential. Lưu audit không chứa PIN hoặc số giấy tờ.

Ghi một notification in_app cùng complete, payload chỉ gồm thông tin/ID
bàn giao, có deduplication_key theo agreement. Đánh dấu sent và sent_at
khi thông báo được lưu vào inbox. Không thêm email worker ở đợt này.

Cập nhật API chi tiết agreement: IsStorageUnitAllocated và Notice phải
phản ánh allocation/status thực tế; bổ sung thông tin ô và checked_in_at
theo cách tương thích với client hiện có.

8. Kiểm thử và bàn giao kết quả

Baseline đã được Codex chạy: build 0 lỗi/11 CS8981; 179 test không dùng
SQL Server đạt. Bốn SQL concurrency test cũ chưa được chạy.

Thêm unit/API tests cho happy path, quyền sai cơ sở, tài khoản/employee/
assignment không hợp lệ, chưa trả đủ tiền, thiếu/sai giấy tờ, ngày thuê,
ô sai loại/bảo trì/unlisted, checklist thiếu hoặc không đạt, stale IDs,
release/reassign, complete replay, cấp lại PIN và customer ownership.

Thêm regression test capacity sau assign/release/complete để chứng minh
reservation được trừ đúng một lần. Kiểm tra PIN/giấy tờ không xuất hiện
trong các response đọc, notification và audit.

Kiểm thử SQL Server thật cho:
- Hai staff tranh cùng ô: chỉ một allocation thành công.
- Hai request cùng reservation: không tạo allocation/biên bản trùng.
- Complete chạy đồng thời hoặc retry: đúng một handover/credential/
  notification.
- Lỗi sau các bước ghi trong complete: rollback, không có trạng thái
  “đã nhận kho” một phần.
- Inspection cũ không được dùng sau đổi ô; trigger và trạng thái chạy đúng.

Chuẩn bị DB test riêng có tên chứa test bằng phần schema/triggers/seed
được tách và đổi target an toàn từ script hiện có. Mọi batch phải giữ
target là DB test đã kiểm tra; không chạy nguyên CleanInstall vì script
đó DROP database ứng dụng SelfStoragePRN222. Không dùng EnsureCreated hoặc
Code First migrations. Fixture chỉ dọn dữ liệu do chính test tạo.

Dùng SELFSTORAGE_SQL_TEST_CONNECTION_STRING; không in connection string.
Nếu không chuẩn bị/chạy được SQL tests, ghi rõ blocker và đánh dấu chưa
nghiệm thu SQL concurrency, không coi InMemory là bằng chứng thay thế.

Build sau từng bước có ý nghĩa; cuối cùng clean/restore/build và chạy
tests phù hợp, kiểm tra Swagger/API startup, rồi dừng process kiểm thử.

Không sửa backlog Function 4 ngoài phạm vi. Nếu phát hiện lỗi cũ thực sự
chặn Function 5, báo bằng chứng và tách rõ khỏi lỗi do thay đổi mới.

Tạo AgentReport/DoneFunction5Backend.md với:
- Hành vi/API đã triển khai và ví dụ luồng gọi, không chứa bí mật.
- Files thay đổi, database objects sử dụng và tác động kiến trúc.
- Lệnh đã chạy; số test passed/failed/skipped; kết quả build/runtime.
- Kết quả SQL concurrency và các phần chưa kiểm chứng.
- Baseline commit, nhánh đang làm và các điểm cần Codex review.

Cập nhật tài liệu trạng thái chính xác: backend Function 5 đã làm đến đâu;
WPF/email/QR và nghiệm thu Function 4 vẫn còn riêng. Giữ thay đổi tại nhánh
để Codex review; không tự commit, push hoặc merge.
```
