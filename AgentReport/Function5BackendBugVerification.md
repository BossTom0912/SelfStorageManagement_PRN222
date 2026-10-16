# Xác minh bug backend Function 5 và hướng sửa

Ngày xác minh: 09/10/2026, Asia/Bangkok.

## Phạm vi và trạng thái

- Workspace: `D:\FPT\PRN222\Project\Final_Project`.
- Nhánh: `Check-in-and-Digital-Handover`.
- HEAD/baseline: `ba599208badb2f0e011ae65355d088d0374f1acd`.
- Code Function 5 và báo cáo Gemini đang là thay đổi chưa commit. Không reset, clean Git, commit, push hoặc merge.
- Lượt này xác minh lỗi và tạo tài liệu giao agent sửa; không sửa source, test, cấu hình hay schema ứng dụng.
- `Function5BackendPreparation.md` là báo cáo lịch sử trước triển khai. Không dùng câu “chưa triển khai” trong báo cáo đó để kết luận về code hiện tại.
- Prompt sửa: [Function5BackendFixPrompt.md](D:/FPT/PRN222/Project/Final_Project/AgentReport/Function5BackendFixPrompt.md).

## Bằng chứng thực hiện

Chạy lại lệnh:

```powershell
dotnet test SelfStorageManagementSystem.sln --no-restore --filter 'FullyQualifiedName!~SqlServerConcurrencyTests' --verbosity quiet
```

Kết quả: **200 passed, 0 failed, 0 skipped trong tập được chọn**. Lệnh có build; hoàn tất với exit code 0. Các test SQL concurrency bị loại bởi filter, không được tính là đã chạy hoặc đã nghiệm thu.

Các probe tạm trong PowerShell tải assembly vừa build và sử dụng fixture `CheckInServiceTests`; không thêm test/file source vào repo. Kết quả probe không được cộng vào con số 200 test:

```text
Condition ' Damaged ': inspection=completed, overall=good, complete succeeded=True
Condition 'not_applicable': inspection=completed, overall=good, complete succeeded=True
Operator status at completion=disabled; Condition 'good': inspection=completed, overall=good, complete succeeded=True
While reservation lock is held: verify-id returned result=rejected; mutation was not serialized.
```

Đã đọc metadata SQL Server trên `SelfStoragePRN222_Test`, không thay dữ liệu:

```text
inspection_items.condition: good, acceptable, damaged, missing, not_applicable
access_credentials.id: is_identity = 1
```

Probe với EF SQL Server provider, không gọi SaveChanges và không chạy INSERT/UPDATE/DELETE: tạo entity credential chỉ trong ChangeTracker; mở/commit transaction rỗng rồi thử rollback. Kết quả:

```text
SQL provider before SaveChanges: CLR credential.id=0, key temporary=True
Rollback after commit: InvalidOperationException: This SqlTransaction has completed; it is no longer usable.
```

Không chạy fixture SQL concurrency tạo dữ liệu trong lượt này. Probe InMemory chứng minh nhánh logic và thiếu khóa trong implementation hiện tại, không thay thế kiểm thử concurrency/rollback trên SQL Server.

## F5-01 — P1: Inspection kết luận sai do dùng condition chưa chuẩn hóa

Vị trí: [CheckInRepository.cs:616](D:/FPT/PRN222/Project/Final_Project/src/SelfStorageManagementSystem.DataAccess/Repositories/Implementations/CheckInRepository.cs:616), validator ở dòng 1302 và ghi item ở dòng 644.

Validator kiểm tra `Trim().ToLowerInvariant()`, và giá trị ghi DB cũng được chuẩn hóa. Tuy nhiên `hasFailure` và `anyAcceptable` so sánh trực tiếp chuỗi đầu vào:

```csharp
var hasFailure = items.Any(it => it.Condition is "damaged" or "missing" or "unsafe");
// Khi ghi item:
condition = item.Condition.Trim().ToLowerInvariant();
```

Với `" Damaged "`, validator chấp nhận; `hasFailure=false`; item lưu `damaged` nhưng inspection lưu `completed/good`. Complete chỉ kiểm tra trạng thái/kết luận tổng thể nên vẫn kích hoạt hợp đồng, chiếm ô và cấp PIN. Đã tái hiện trọn luồng trên fixture InMemory.

`not_applicable` cũng được validator chấp nhận và coi là đạt. Theo phạm vi đã chốt, bốn mục bắt buộc phải thực sự được kiểm tra, chỉ `good/acceptable` mới đạt; `not_applicable` không đủ điều kiện bàn giao.

Hướng sửa: chuẩn hóa item đúng một lần, dùng cùng dữ liệu cho validation, kết luận và persistence. Với bốn mục bắt buộc, từ chối `not_applicable` bằng 400, không ghi gì. Các mục damaged/missing phải tạo failed, release allocation và đưa ô sang maintenance trong cùng transaction. Bổ sung test có khoảng trắng/chữ hoa, acceptable, null và các mục thiếu/trùng.

## F5-02 — P1: Verify không tham gia khóa reservation

Vị trí: [CheckInRepository.cs:111](D:/FPT/PRN222/Project/Final_Project/src/SelfStorageManagementSystem.DataAccess/Repositories/Implementations/CheckInRepository.cs:111), phương thức `VerifyIdentityAsync` dòng 97–217.

Verify mở transaction ReadCommitted nhưng không lấy `SS_Reservation_{reservationId}` hoặc semaphore tương ứng. Probe giữ semaphore do assign đã tạo; verify vẫn lưu và trả rejected khi khóa chưa được thả.

Trên SQL Server, khóa ứng dụng chỉ tuần tự hóa các request cùng sử dụng khóa đó. Một transaction verify không lấy khóa có thể ghi rejected sau khi complete đã đọc collection verification là verified nhưng trước khi complete commit. Khi đó complete dùng snapshot cũ và vẫn bàn giao. Đọc lại dưới khóa ở complete không đủ nếu tác vụ ghi verification không tuân thủ khóa.

Hướng sửa: tất cả mutation của cùng reservation, kể cả verify, phải dùng cùng khóa và đọc lại sau khóa. Bổ sung test SQL có điểm đồng bộ xác định; không dựa vào sleep để hy vọng tái hiện race. Đối với hai reservation cùng customer đang thiếu identity_number, bảo vệ cập nhật hồ sơ bằng conditional update/row lock hoặc khóa customer thống nhất để không ghi đè số vừa được request khác bổ sung.

## F5-03 — P1: Quyền được kiểm tra trước khóa, không được kiểm tra lại

Vị trí: [CheckInService.cs:549](D:/FPT/PRN222/Project/Final_Project/src/SelfStorageManagementSystem.BusinessLogic/Services/Implementations/CheckInService.cs:549) và [CheckInRepository.cs:760](D:/FPT/PRN222/Project/Final_Project/src/SelfStorageManagementSystem.DataAccess/Repositories/Implementations/CheckInRepository.cs:760).

Service gọi `HasCheckInCounterAccessAsync` rồi repository chờ khóa. Sau khi lấy khóa, repository đọc reservation nhưng không đọc lại user/role/employee/assignment. Trigger handover chỉ kiểm tra agreement/allocation/inspection cùng scope; không thay thế authorization tại quầy.

Đã tái hiện: giữ khóa reservation; bắt đầu complete với staff hợp lệ; khi request đang chờ khóa, đổi user thành disabled; thả khóa. Complete vẫn thành công. Đây là bằng chứng logic trên InMemory, cần bổ sung phiên bản SQL qua hai DbContext độc lập.

Hướng sửa: sau khóa, đọc snapshot quyền mới từ DB và áp dụng policy tại BusinessLogic trước mọi mutation, gồm replay complete và reissue. Yêu cầu user active, role staff/manager, employee active, assignment đúng cơ sở và còn hiệu lực tại thời điểm mới. Không chỉ gọi lại helper dùng entity đã track: `UserRepository.GetByIdWithRolesAndProfilesAsync` hiện là tracking query, có thể trả dữ liệu cũ trong cùng scoped context. Dùng projection/AsNoTracking hoặc reload có kiểm soát; không Clear ChangeTracker toàn cục, không mở DbContext ở controller.

## F5-04 — P2: Item unsafe được chấp nhận nhưng không hợp schema

Vị trí: [CheckInRepository.cs:1303](D:/FPT/PRN222/Project/Final_Project/src/SelfStorageManagementSystem.DataAccess/Repositories/Implementations/CheckInRepository.cs:1303). Schema: `DB/SelfStoragePRN222_SQLServer_CleanInstall.sql:590`; metadata DB test xác nhận cùng ràng buộc.

Code chấp nhận rồi ghi `inspection_items.condition=unsafe`. SQL CHECK không cho giá trị này. Lỗi 547 không nằm trong mapping hiện tại, dẫn đến 500 và rollback, thay vì ghi failed/release/maintenance. InMemory không thi hành CHECK nên che lỗi.

Hướng sửa: không nới CHECK hoặc sửa generated model. Thêm trường assessment boolean rõ nghĩa, ví dụ `isUnsafe`, vào request inspection; giữ item trong tập giá trị hợp schema và lưu `inspections.overall_condition=unsafe` khi staff đánh giá không an toàn. Request dùng item condition unsafe trả 400 rõ ràng. Unsafe assessment phải dẫn đến failed + release + maintenance nguyên tử. Không đoán unsafe từ chuỗi summary.

## F5-05 — P2: Rollback sau commit che mất lỗi giấy tờ

Vị trí: [CheckInRepository.cs:156](D:/FPT/PRN222/Project/Final_Project/src/SelfStorageManagementSystem.DataAccess/Repositories/Implementations/CheckInRepository.cs:156) và catch dòng 199–205.

Nếu số không khớp và staff xác nhận match, code lưu rejected, commit, rồi throw `DOCUMENT_NUMBER_MISMATCH`. Catch cố rollback transaction đã commit; probe transaction rỗng trên SQL Server xác nhận thao tác này throw `This SqlTransaction has completed`. Exception mới thoát ra trước mapper, vì vậy lỗi 400 dự kiến bị biến thành 500 dù rejected đã được lưu.

Hướng sửa: lưu rejected và trả outcome typed từ repository; Service chuyển outcome thành BadRequest sau khi repository đã commit/dispose. Hoặc bố trí throw lỗi nghiệp vụ bên ngoài vùng catch rollback. Rollback chỉ áp dụng cho transaction chưa commit; giữ exception gốc nếu cleanup thất bại. Không sửa bằng cách nuốt mọi exception hoặc map mọi lỗi SQL thành 400.

Test cần kiểm tra trên SQL Server: HTTP 400 có envelope đúng, có đúng một rejected, identity_number cũ giữ nguyên, không có handover/credential, không xuất hiện lỗi transaction completed.

## F5-06 — P2: Audit cấp lại PIN lấy ID trước khi DB sinh khóa

Vị trí: [CheckInRepository.cs:1157](D:/FPT/PRN222/Project/Final_Project/src/SelfStorageManagementSystem.DataAccess/Repositories/Implementations/CheckInRepository.cs:1157).

Audit serialize `newCred.id` ngay sau Add, trước SaveChanges. Metadata xác nhận khóa identity; probe SQL provider xác nhận CLR id=0, dù EF có temporary key nội bộ. JSON và entity_id đã tạo không tự được sửa khi EF nhận ID thật. InMemory cấp ID ngay khi Add nên không phát hiện lỗi này.

Hướng sửa: trong cùng transaction, lưu revoke + credential mới để nhận ID thật, sau đó tạo/lưu audit và commit. Lỗi audit phải rollback cả revoke và credential mới. Audit dùng ID > 0 khớp credential response/DB và role server đã xác thực; không chứa PIN, digest hay số giấy tờ.

## F5-07 — P2: Báo cáo ghi sai request API

Vị trí: [DoneFunction5Backend.md:28](D:/FPT/PRN222/Project/Final_Project/AgentReport/DoneFunction5Backend.md:28) và dòng 33. DTO thực tế nằm trong `BusinessLogic/DTOs/Requests/CheckIn/`.

- Verify nhận `verificationMethod`, `documentNumber`, `isConfirmedMatch`, `notes`; báo cáo lại ghi identityNumber/staffConfirmedFaceMatch. Model binding không gán các property thực tế, nên request có thể lưu rejected.
- Reissue cần `currentCredentialId`, `documentNumber`, `isConfirmedMatch`, `reason`; báo cáo chỉ ghi reason. Gửi theo báo cáo sẽ thất bại validation.
- Báo cáo nói reissue tạo notification nhưng code chỉ tạo audit. Phạm vi đã chốt yêu cầu notification ở complete; không thêm notification ở reissue chỉ để khớp báo cáo sai.

Hướng sửa: đối chiếu DTO/controller và Swagger để sửa cả hai báo cáo Done; bổ sung ví dụ JSON với giá trị giả, không chứa bí mật. Sửa các mô tả Authorize, masking, header và tên test cho đúng implementation cuối cùng.

## F5-08 — P2: Báo cáo ghi sai tên biến môi trường

Vị trí: [DoneFunction5Backend.md:78](D:/FPT/PRN222/Project/Final_Project/AgentReport/DoneFunction5Backend.md:78), `DoneFunction5.md:95`, code `CheckInService.cs:556,635`.

Code đọc `CheckIn:PinHmacSecret` hoặc `CHECKIN_PIN_HMAC_SECRET`; báo cáo ghi `SELFSTORAGE_PIN_HMAC_SECRET`. Nếu chỉ thiết lập biến được báo cáo hướng dẫn, cả complete/reissue vẫn thiếu key.

Hướng sửa: thống nhất tên đã được implementation hỗ trợ, gồm env dạng configuration `CheckIn__PinHmacSecret` nếu tài liệu sử dụng provider mặc định. Không thêm default key vào source. Kiểm tra thiếu/sai key không gây bất kỳ bàn giao một phần nào; phân biệt validation khi gọi API với kiểm tra tại startup nếu thực sự có.

## F5-09 — P2: Test SQL chưa chứng minh kết luận trong báo cáo

Vị trí: [CheckInSqlServerConcurrencyTests.cs:775](D:/FPT/PRN222/Project/Final_Project/tests/SelfStorageManagementSystem.Tests/CheckInSqlServerConcurrencyTests.cs:775), claims tại `DoneFunction5Backend.md:149–150`.

Hai task complete bắt mọi Exception rồi trả Success=false. Assertion chỉ yêu cầu đúng một completion mới và một handover. Test vẫn xanh nếu request thứ hai lỗi 500 thay vì replay; chưa đếm credential/notification. Test tranh ô cũng không kiểm tra cụ thể exception request thua. Chưa có fault-injection rollback complete.

Hướng sửa: complete cùng IDs phải có một initial success và một replay success, replay không PIN; chỉ một handover/credential/notification. Tranh ô phải có một thành công và một lỗi Conflict đã nhận diện. Thêm fault injection sau khi một số lệnh ghi đã chạy nhưng trước commit, rồi kiểm tra bằng DbContext mới không có trạng thái bàn giao một phần. Bổ sung capacity sau assign/release/complete ở catalog, reservation query và tạo hold dưới khóa; test hiện tại chủ yếu kiểm tra catalog trước/sau assign.

Tên ba HTTP tests thực tế là Search anonymous, Search customer forbidden và FullCheckInFlow. Một test lifecycle có nhiều assertion không đồng nghĩa tồn tại ba test độc lập như tên trong báo cáo hiện tại.

## Kết luận và điều kiện review tiếp

Có 3 lỗi P1, 3 lỗi code P2, 2 lỗi tài liệu P2 và 1 lỗi assertion/bằng chứng test P2. Không kết luận backend an toàn chỉ từ 200 test xanh. Chưa nên merge vào main.

Agent sửa phải tạo `AgentReport/DoneFunction5BackendFix.md`, map từng F5-01…F5-09 với file, test và kết quả thật. Các phần chưa chạy phải đánh dấu chưa kiểm chứng. Codex review lại diff, test và transaction/trigger trước khi nghiệm thu; không tự commit/push/merge.
