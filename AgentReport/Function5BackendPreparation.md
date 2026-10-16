# Chuẩn bị backend Function 5 và bàn giao cho Gemini

Ngày: 09/10/2026 (Asia/Bangkok).

## Trạng thái Git và phạm vi

- Nhánh nguồn: `Payment&RentalAgreement`.
- Baseline: `ba599208badb2f0e011ae65355d088d0374f1acd`.
- Đã tạo và checkout `Check-in-and-Digital-Handover` từ nhánh nguồn. Hai nhánh cùng HEAD ngay sau khi tạo; toàn bộ code và lịch sử được kế thừa.
- Working tree sạch trước khi tạo nhánh. Bước chuẩn bị chỉ thêm prompt và báo cáo này; chưa sửa source, test, SQL hoặc WPF.
- Không commit, push hoặc merge. Nhánh đích hiện là nhánh local; hai file mới chưa được commit.
- Gemini triển khai backend theo [prompt đã chốt](../Agent/Gemini/FUNCTION5_BACKEND_PROMPT.md); Codex review sau khi có kết quả.

## Kết quả rà soát hướng đi

Quy trình tại quầy gồm xác minh → gán ô → kiểm tra ô → complete. Complete là giao dịch kích hoạt duy nhất: agreement `scheduled → active`, reservation `confirmed → checked_in`, ô `reserved → occupied`, handover, credential và thông báo in-app cùng commit.

| Mức độ | Điểm cần xử lý khi triển khai | Bằng chứng hiện có |
| --- | --- | --- |
| HIGH | Allocation `rental` phải gắn `agreement_id`, để `reservation_id = null`; inspection và handover phải cùng agreement/ô. | CHECK của `core.unit_allocations` chỉ cho một trong hai liên kết; trigger `trg_handover_records_validate_scope` kiểm tra allocation/inspection. |
| HIGH | Sửa tính sức chứa để nhận diện allocation qua agreement, bao gồm truy vấn tạo hold dưới khóa. | `ReservationRepository` và `FacilityCatalogRepository` hiện chỉ loại reservation có allocation active qua `ua.reservation_id == r.id`. Khi assign tạo allocation qua agreement mà reservation vẫn confirmed, truy vấn cũ sẽ trừ thêm reservation đã gán. |
| HIGH | Kiểm tra nhân viên và phân công thực tế cho thao tác tại quầy. | `FacilityScopeService` cho Admin/Operations Manager quyền rộng trước kiểm tra employee/assignment; không dùng shortcut này để cấp quyền bàn giao. |
| HIGH | Serialize, đọc lại sau khóa và rollback complete; replay không sinh lại PIN hoặc biên bản. | Chưa có implementation Function 5. Schema không có unique handover check-in/credential theo agreement để thay thế việc serialize ở application. |
| MEDIUM | Đổi ô/inspection thất bại phải release allocation đúng trạng thái, không tái dùng inspection cũ. | Trigger chỉ cho `available → reserved → occupied`; allocation không active cần `ended_at`. Trigger allocation active từ chối ô occupied/maintenance. |
| MEDIUM | Cập nhật API chi tiết hợp đồng theo dữ liệu thật. | `RentalAgreementService` hiện gán `IsStorageUnitAllocated = false`; DTO có Notice chờ bàn giao mặc định. |

Các mục trên là yêu cầu tích hợp và nguy cơ cần ngăn khi thêm Function 5, không phải kết luận rằng backend Function 5 hiện có các lỗi đó. Feature này chưa được triển khai.

## Quyết định đã được người dùng chốt

- Chỉ backend; WPF/email/QR/thiết bị mở cửa để đợt sau.
- Gán ô và complete trong `[start_date, end_date)` theo múi giờ cơ sở; chưa bàn giao sớm.
- Staff được bổ sung số giấy tờ còn trống sau khi đối chiếu bản gốc/người nhận; không tự ghi đè hồ sơ đã có số.
- PIN 6 số, HMAC digest có khóa bí mật, hiển thị một lần và có API cấp lại tại quầy; replay không cấp thêm.
- Notification in-app không chứa PIN; không cần email worker trong đợt này.
- Function 4 vẫn chưa nghiệm thu đầy đủ. Không mở rộng phạm vi sang backlog Function 4.

## Bằng chứng kiểm tra baseline

Các kiểm tra dưới đây đã thực hiện ở lượt lập kế hoạch trong cùng chat, trước khi tạo nhánh, trên đúng baseline nêu trên:

```powershell
dotnet build SelfStorageManagementSystem.sln --no-restore --verbosity quiet
dotnet test SelfStorageManagementSystem.sln --no-build --no-restore --filter 'FullyQualifiedName!~SqlServerConcurrencyTests' --verbosity quiet
```

- Build: 0 errors, 11 warnings CS8981 từ entity lowercase sinh tự động.
- Test không dùng SQL Server: 179 passed, 0 failed, 0 skipped; bốn SQL concurrency test được loại khỏi lệnh trên, không được coi là đã chạy.
- Đã kết nối read-only SQL Server `localhost`, database `SelfStoragePRN222`; xác nhận các CHECK allocation/inspection/credential và các trigger liên quan đang bật. Không chạy installer hoặc thay đổi dữ liệu.
- Chưa có database test riêng hoặc biến `SELFSTORAGE_SQL_TEST_CONNECTION_STRING` ở thời điểm kiểm tra. Chưa xác minh SQL concurrency, WPF end-to-end, VNPAY IPN hoặc runtime backend Function 5.
- Trong lượt tạo nhánh, đã kiểm tra lại Git: nhánh nguồn vẫn ở baseline, working tree sạch và nhánh đích chưa tồn tại.
- Không chạy lại build/tests chỉ vì thêm hai tài liệu; source và cấu hình runtime không thay đổi.

## Checklist Codex review sau Gemini

- [ ] Đọc `AgentReport/DoneFunction5Backend.md` và đối chiếu diff từ baseline, gồm cả tracked/untracked files.
- [ ] Kiểm tra role, employee, facility assignment và customer ownership; mutation kiểm tra lại quyền sau khóa.
- [ ] Kiểm tra XOR allocation, thứ tự ghi theo trigger, capacity regression, release/reassign và inspection cũ.
- [ ] Kiểm tra transaction complete/reissue, replay, đồng thời cùng ô/cùng reservation và rollback khi lỗi giữa transaction.
- [ ] Kiểm tra PIN một lần, key configuration, expiry/collision, no-store và không lộ PIN/giấy tờ trong DTO đọc, notification/audit/log.
- [ ] Đối chiếu kết quả build/API tests/SQL tests với lệnh và môi trường thực sự được chạy; không coi skip là nghiệm thu.
- [ ] Báo findings theo CRITICAL/HIGH/MEDIUM/LOW/INFO; chỉ rõ lỗi mới và vấn đề Function 4 đã tồn tại.

Review triển khai sẽ thực hiện khi Gemini hoàn tất và người dùng thông báo trong chat này; chưa có agent Gemini được khởi chạy hoặc tác vụ theo dõi tự động.
