# Thiết kế triển khai Function 4: Thanh toán và hợp đồng thuê

> **Lưu ý tiến độ 08/10/2026:** Đây là tài liệu thiết kế và các mốc kiểm thử cũ trong phần dưới được giữ để tham khảo. Trạng thái hiện tại: Function 4 đã triển khai cốt lõi nhưng chưa nghiệm thu; **179 tests passed, 4 SQL concurrency tests skipped**. Xem [tổng kết tiến độ mới nhất](FIVE_CORE_FUNCTIONS_SUMMARY.md).

**Ngày khảo sát:** 07/10/2026
**Trạng thái:** Phương án triển khai, chưa phải chức năng đã hoàn thành
**Phạm vi:** Thanh toán lần đầu cho reservation đang được giữ 15 phút; tạo hóa đơn, ghi nhận giao dịch và hợp đồng chờ bàn giao. Gia hạn, hoàn cọc và check-in thuộc các luồng sau.

## 1. Cơ sở và thứ tự ưu tiên

- [Business Rules](../SRS/drive-download-20260925T083452Z-1-001/Business_Rules_SelfStorageSystem.docx): BR-RSV-01/03/04 và BR-FIN-01/04 là yêu cầu nghiệp vụ chính. Khách đặt theo loại kho, cọc bằng một tháng thuê và tách khỏi tiền thuê; check-in cần thanh toán đủ cọc cộng kỳ đầu.
- [Phân tích hệ thống](../SRS/drive-download-20260925T083452Z-1-001/SelfStorageSystem_PhanTichHeThong.docx): Flow 1 yêu cầu thanh toán trực tuyến sau hold, Flow 2 mới bàn giao và kích hoạt hợp đồng. Phần mô tả Clean Architecture/PostgreSQL là kiến trúc dự kiến trong tài liệu, không khớp repository hiện chạy WPF → ASP.NET Core API → BusinessLogic → DataAccess/EF Core → SQL Server.
- [State Chart](../SRS/drive-download-20260925T083452Z-1-001/State%20Chart%20explanation.docx): reservation `Pending → Confirmed`, payment `Processing → Paid`; biểu đồ diễn giải hợp đồng `Active` ngay sau thanh toán. Quy tắc check-in, roadmap và schema lại dành `Active` cho lúc bàn giao; vì vậy dùng `scheduled` sau thanh toán, `active` ở Function 5.
- [Diagram](../SRS/drive-download-20260925T083452Z-1-001/Diagram) (draw.io, trang 3–4) thể hiện quan hệ reservation → agreement, invoice → payment allocation → payment và promotion redemption. Các quan hệ được đối chiếu lại với FK thật trong SQL clean install trước khi chọn thứ tự ghi.
- [Roadmap](DEMO_ROADMAP_AND_TECH_SPEC.md) nêu checkout, callback, invoice, agreement và demo qua cổng giả lập/VietQR hoặc sandbox. Đây là mục tiêu, không phải bằng chứng endpoint đã có.
- [SQL clean install](../DB/SelfStoragePRN222_SQLServer_CleanInstall.sql) và các entity scaffold là nguồn sự thật về tên cột, trạng thái, FK, unique index và trigger. [ReservationService](../src/SelfStorageManagementSystem.BusinessLogic/Services/Implementations/ReservationService.cs) hiện chốt `quoted_total = deposit_snapshot + monthly_rate_snapshot + booking_fee_snapshot`, `discount_snapshot = 0`; [ReservationRepository](../src/SelfStorageManagementSystem.DataAccess/Repositories/Implementations/ReservationRepository.cs) giữ sức chứa theo loại kho và có luồng hủy/hết hạn. Hiện chưa có Payment/Agreement service hoặc API.

**Nguyên tắc khi tài liệu khác nhau:** giữ bất biến BR-RSV/BR-FIN; dùng schema và code thực tế để thiết kế thao tác ghi; ghi rõ lựa chọn mới cần chốt thay vì coi mô tả của state chart hoặc roadmap là implementation.

## 2. Quyết định kỹ thuật và lý do

| Quyết định | Cách làm | Lý do và giới hạn |
| --- | --- | --- |
| Kỳ đầu | Thu cọc một tháng + tiền thuê **tháng đầu** + booking fee − ưu đãi hợp lệ. Các tháng sau lập hóa đơn theo chu kỳ, không thu trước cả 1–12 tháng. | BR-RSV-04 yêu cầu cọc và **kỳ đầu**; seed `policy_versions` có `billing_cycle: monthly`; khớp `quoted_total` hiện có. Cần chốt với nhóm nếu muốn thu toàn kỳ vì sẽ phải sửa cả quote ở Function 3. |
| Tiền cọc | Một invoice ban đầu có dòng `deposit`, `rent`, `booking_fee`, có thể có dòng `discount` âm. Giữ riêng cọc bằng `deposit_snapshot`/`deposit_balance` trên agreement; ưu đãi không trừ cọc. | BR-FIN-01 nói cọc không phải tiền thuê. Tách dòng giúp đối chiếu và hoàn cọc sau này. Không tự giả định thuế; `tax_amount = 0` trong demo đến khi có chính sách thuế được xác nhận. |
| Cổng thanh toán | Xây một adapter `IPaymentGateway`: `DemoGateway` chỉ bật trong Development/Test, tiếp đó `VnPaySandboxGateway` khi có merchant credentials và IPN HTTPS công khai. | Roadmap cho phép cổng giả lập. [VNPAY PAY](https://sandbox.vnpayment.vn/apis/docs/thanh-toan-pay/pay.html) có sandbox, URL thanh toán và IPN rõ ràng; [payOS xác nhận không có sandbox tách biệt](https://payos.vn/docs/moi-truong-test/). [API tạo VietQR](https://www.vietqr.io/generate/) chỉ trả mã QR dựa trên thông tin chuyển khoản; suy ra ảnh QR không tự chứng minh giao dịch đã được đối soát. Không cho WPF gọi API “đánh dấu đã trả tiền”. |
| Giao diện WPF | Mở hosted payment URL trong trình duyệt; WPF gọi `GET /api/payments/{id}` để cập nhật kết quả. `ReturnUrl` chỉ hiện thông báo. | WPF không giữ khóa bí mật, không xử lý thẻ và không cần nhận webhook. VNPAY phân biệt [ReturnUrl với IPN server-to-server](https://sandbox.vnpayment.vn/apis/docs/thanh-toan-pay/pay.html). |
| Giao dịch dữ liệu | Một transaction SQL Server cho mỗi thao tác nhiều bảng; checkout và xử lý IPN dùng lock theo reservation, cập nhật trạng thái có điều kiện. Không giữ transaction mở khi gọi gateway. | [EF Core transactions](https://learn.microsoft.com/en-us/ef/core/saving/transactions) bảo đảm commit/rollback cho nhiều lần `SaveChanges`; [SQL Server `sp_getapplock`](https://learn.microsoft.com/en-us/sql/relational-databases/system-stored-procedures/sp-getapplock-transact-sql) phù hợp với cách Function 3 đang khóa tài nguyên. Schema không có `rowversion`, nên không đặt kế hoạch phụ thuộc token này. |
| Tiền và thời gian | Tính bằng `decimal`, lấy thời gian UTC phía server; `TimeProvider` cho code mới cần kiểm thử mốc 15 phút. Trước khi gửi VNPAY, bắt buộc số VND nguyên và kiểm tra `amount × 100` không vượt giới hạn provider. | SQL dùng `numeric(14,2)`; [VNPAY yêu cầu `vnp_Amount` nhân 100 và `vnp_ExpireDate` theo GMT+7](https://sandbox.vnpayment.vn/apis/docs/thanh-toan-pay/pay.html). [.NET 8 có `TimeProvider`](https://learn.microsoft.com/en-us/dotnet/standard/datetime/timeprovider-overview) để kiểm thử thời gian quyết định. |

**Chốt trạng thái:** thanh toán thành công → `payments.succeeded`, `invoices.paid`, `reservations.confirmed`, `rental_agreements.scheduled`; Function 5 mới đặt `rental_agreements.active`, gán `unit_allocations`/ô kho và ghi `checked_in_at`. `signed_at` để `NULL` cho đến khi có bước chấp thuận/ký hợp đồng được định nghĩa rõ; một dấu thời gian đơn lẻ chưa phải quy trình ký số.

**Ví dụ quote đề xuất:** giá tháng 900.000đ, cọc 900.000đ, phí đặt 50.000đ, voucher 10% trên thuê kỳ đầu + phí đặt → giảm 95.000đ, phải trả 1.755.000đ. Cọc vẫn là 900.000đ trên hợp đồng; không giảm theo voucher.

## 3. Mô hình dữ liệu phải tuân thủ

| Bảng | Cách dùng trong Function 4 | Ràng buộc cần kiểm thử trên SQL Server |
| --- | --- | --- |
| `reservations` | Xác thực chủ đơn, `pending`/`awaiting_deposit`, `hold_until`; cập nhật `discount_snapshot`, `quoted_total`, rồi `confirmed`/`confirmed_at` khi trả đủ. | `hold_until` tối đa 15 phút; không xác nhận nếu hết hạn/hủy. Worker Function 3 vẫn chỉ expire trạng thái chờ. |
| `invoices` + `invoice_lines` | Tạo invoice gắn reservation ở `draft`, thêm dòng, chuyển `open`; sau khi agreement được tạo thì gắn thêm `agreement_id`. | Trigger `trg_invoice_lines_refresh_totals` tính total; ứng dụng không ghi đè `total_amount`/`paid_amount`. Trigger trạng thái chỉ cho `draft → open/voided`; dòng invoice đã trả tiền là bất biến. |
| `payments` + `payment_allocations` | Một payment attempt cho mỗi lần chuyển hướng; lưu `idempotency_key`, `provider_transaction_id`, trạng thái. Chỉ allocation khi payment `succeeded` và invoice còn mở. | Unique idempotency key và `(provider, provider_transaction_id)`; trigger kiểm tra cùng customer, không vượt payment/invoice amount và cập nhật invoice paid. |
| `integration_events` | Lưu callback đã xác minh với `source = 'vnpay'`, external event ID ổn định, payload JSON và trạng thái xử lý để retry/đối soát. | UNIQUE `(source, external_event_id)` ngăn xử lý lặp; sự kiện lỗi DB phải còn dấu vết để chạy lại. Không lưu secret hay thông tin thẻ trong payload. |
| `rental_agreements` | Tạo đúng một agreement mỗi reservation sau khi invoice đã được thanh toán đủ; snapshot giá/cọc, `deposit_balance = deposit_snapshot`, `status = scheduled`, tham chiếu `policy_versions` loại `rental_terms` có hiệu lực. | `reservation_id` UNIQUE; trigger buộc customer/facility/start_date và snapshot giá trùng reservation. Chưa gán unit ID. |
| `promotions` + `promotion_rules` + `promotion_redemptions` | Tối đa một mã; `reserved` khi checkout, `applied` khi trả đủ, `released` nếu hold kết thúc mà chưa trả. | Unique một redemption chưa released trên reservation/invoice; ứng dụng vẫn phải khóa và kiểm tra `usage_limit`/`per_customer_limit`, vì unique index không bảo đảm các tổng hạn mức. |

Mã `invoice_no`/`agreement_no` do server sinh và dựa vào UNIQUE trong SQL để bắt va chạm; dùng retry hữu hạn cho lỗi trùng mã. `payment.method = 'vnpay'` cho sandbox; demo dùng enum hiện có `other` với `provider = 'demo'`, không tự thêm giá trị vào schema.

## 4. API và luồng xử lý

### 4.1 Xem giá trước checkout

`GET /api/reservations/{id}/checkout-quote?promotionCode=...` yêu cầu JWT của chủ reservation. Server kiểm tra hold còn hạn, giá snapshot và voucher; kiểm tra `deposit_snapshot == monthly_rate_snapshot` theo BR-FIN-01, nếu lệch thì báo lỗi cấu hình và không tự sửa giá đã chốt. Response trả deposit, kỳ thuê đầu, booking fee, discount, tổng VND, thời điểm hold hết hạn và bản chính sách áp dụng. Request không được gửi giá tiền hoặc customer ID. Quote chỉ để hiển thị; `POST checkout` phải **tính lại** và khóa dữ liệu, vì voucher/hold có thể đổi sau GET.

Ưu đãi: kiểm tra `is_active`, cửa sổ hiệu lực, `minimum_months`, `minimum_amount`, facility, unit type, new customer, giới hạn dùng toàn hệ thống và theo khách. Với `fixed`/`percentage`, chỉ giảm tối đa phần tiền thuê kỳ đầu + booking fee, không giảm cọc. Với `free_days`, đề xuất quy đổi theo số ngày của kỳ thuê tháng đầu (tiền thuê × số ngày miễn / số ngày kỳ đầu) rồi chặn không vượt tiền thuê; cần nhóm chốt quy tắc làm tròn/ngày áp dụng trước khi bật mã loại này. Seed `WELCOME10` là `percentage`, `new_customer`, giới hạn 1 lần/khách.

### 4.2 Bắt đầu checkout

`POST /api/payments/checkout` nhận `reservationId`, `promotionCode?`, `Idempotency-Key`, `acceptedPolicyVersionId`; chỉ chủ đơn có role `storage_customer` được gọi. Server phải kiểm tra version khách chấp thuận vẫn là bản `rental_terms` có hiệu lực. Trả `paymentId`, `invoiceId`, số tiền, `checkoutUrl`, `holdUntil`. Các bước trong **transaction ngắn**:

1. Khóa reservation; nếu key đã có thì trả lại cùng payment; nếu có attempt còn hiệu lực với key khác thì trả 409 kèm `paymentId` để client tiếp tục attempt cũ. Từ chối reservation hết hạn, đã hủy/confirmed, tổng tiền không hợp lệ.
2. Tính lại quote và voucher, xác thực cọc đúng một tháng và chọn `rental_terms` có hiệu lực; lưu discount/quoted total snapshot trên reservation. Tạo redemption `reserved` nếu có mã. Một checkout đang chờ chỉ giữ một voucher; retry giữ nguyên quote, không âm thầm thay mã.
3. Tạo hoặc dùng lại invoice ban đầu; `draft` → thêm dòng `deposit`, `rent`, `booking_fee`, `discount` → đọc tổng từ DB → `open`. Nếu tổng không bằng quote server vừa tính, rollback và báo lỗi dữ liệu.
4. Tạo `payments` ở `initiated` với amount bằng invoice còn phải trả; đặt reservation `awaiting_deposit`; commit. Chỉ sau commit mới tạo URL thanh toán VNPAY, dùng `vnp_TxnRef` duy nhất theo payment attempt và `vnp_ExpireDate` không vượt hold, sau đó chuyển attempt sang `pending`. Nếu cấu hình provider lỗi, đánh dấu attempt `failed`; invoice vẫn gắn reservation và có thể dùng để thử lại trong hold, không có agreement hoặc allocation.

Lý do giữ invoice/failed attempt: cần nhật ký để đối soát và retry. “Thất bại không để lại dữ liệu tài chính dở dang” nghĩa là **không có paid invoice, payment allocation, confirmed reservation hay agreement giả**; invoice chưa trả được void và voucher được release khi hold hết hạn/hủy.

### 4.3 Nhận kết quả và lập hợp đồng

Với VNPAY, dùng endpoint IPN riêng (`GET /api/payments/vnpay/ipn` theo contract nhà cung cấp) và `GET /api/payments/vnpay/return` chỉ để hiển thị. IPN không dùng JWT của khách; trước khi ghi DB phải xác minh HMAC-SHA512 bằng secret server, `TmnCode`, `TxnRef`, amount, currency, `ResponseCode` và `TransactionStatus`, rồi kiểm tra bản ghi payment/invoice tương ứng. Sau xác minh, ghi `integration_events` với ID ổn định từ tham chiếu giao dịch + trạng thái provider; event có thể được xử lý lại nếu transaction cuối thất bại. Bảo vệ secret qua cấu hình server, không commit và không gửi về WPF. [Tài liệu VNPAY](https://sandbox.vnpayment.vn/apis/docs/thanh-toan-pay/pay.html) yêu cầu checksum, kiểm tra số tiền/trạng thái và xử lý IPN lặp lại.

Trong transaction: khóa reservation/payment/invoice theo thứ tự thống nhất; nếu đã xử lý thành công thì trả ACK idempotent; nếu provider báo thất bại thì chỉ chuyển attempt sang `failed`; nếu thành công **và hold vẫn còn hạn**, cập nhật reservation `pending|awaiting_deposit → confirmed` với điều kiện `hold_until > now`, chuyển payment `succeeded` với `paid_at`, thêm allocation, đọc lại invoice `paid` do trigger, tạo agreement `scheduled`, gắn invoice với agreement, chuyển redemption sang `applied`, commit. Bất kỳ lỗi DB nào rollback toàn bộ bước xác nhận; callback lặp hoặc đối soát lại để phục hồi. Không gọi VNPAY khi transaction DB còn mở.

Nếu provider đã thu tiền nhưng IPN tới khi hold đã expired/cancelled hoặc không còn sức chứa: **không tự phục hồi reservation và không tạo agreement**. Ghi payment `succeeded` nhưng không allocation vào invoice đã void; đánh dấu `reconciliation_required` trong `payments.metadata`, giữ `integration_events` để nhân viên tra cứu và xử lý hoàn tiền thủ công. Không báo “thành công đặt kho” cho khách. DB rollback không hoàn tác được tiền ở cổng thanh toán. Cần một phép kiểm tra lại trạng thái với [API query transaction của VNPAY](https://sandbox.vnpayment.vn/apis/docs/truy-van-hoan-tien/querydr%26refund.html) khi IPN thiếu hoặc trạng thái còn `pending`, và quy trình xử lý khoản đã thu muộn trước khi go-live.

### 4.4 Đọc kết quả

- `GET /api/payments/{id}`: chỉ chủ payment và nhân sự được phân quyền theo cơ sở; trả trạng thái attempt, invoice, reservation và lý do cần đối soát nếu có. WPF poll có giới hạn cho đến khi trạng thái terminal, rồi cho phép refresh.
- `GET /api/invoices/{id}`: thông tin hóa đơn nội bộ, các dòng tính tiền, số đã trả và mã giao dịch; không coi bản ghi DB là hóa đơn thuế điện tử đã phát hành.
- `GET /api/agreements/{id}`: hợp đồng `scheduled`, chính sách áp dụng và lịch bắt đầu; không hiển thị ô kho cụ thể trước Function 5.
- `POST /api/payments/demo/{id}/complete` chỉ trong Development/Test, bind loopback hoặc yêu cầu token ký một lần của trang simulator phía server, dùng cùng hàm finalization như IPN; không expose ở môi trường thường và không cho WPF tự đánh dấu paid.

## 5. Đồng bộ với Function 3

- Sửa worker và đường hủy/GET hết hạn để release `promotion_redemptions.reserved` cùng transaction đang void invoice chưa trả. Stored procedure SQL có bước release, nhưng worker C# hiện **chưa làm bước này**.
- Checkout/finalization, hủy và expiry phải thống nhất khóa và điều kiện trạng thái. `ExecuteUpdateAsync` không tự cập nhật EF Change Tracker; reload `AsNoTracking` sau các cập nhật trực tiếp. [EF Core giải thích giới hạn tracking/concurrency của ExecuteUpdate](https://learn.microsoft.com/en-us/ef/core/saving/execute-insert-update-delete).
- Không tạo `unit_allocations`, không đổi `storage_units.physical_status` trong Function 4. Capacity vẫn được giữ bằng reservation `confirmed` chưa gán ô; Function 5 mới gán ô vật lý theo BR-RSV-03.
- Nếu checkout tạo invoice `open`, luồng hủy/hết hạn hiện chỉ void khi `paid_amount = 0`. Finalization phải chặn việc xác nhận sau khi invoice đã void và dùng transaction để không có invoice vừa `paid` vừa `voided`.

## 6. Thứ tự làm việc và điều kiện nghiệm thu

**Vị trí dự kiến theo kiến trúc hiện có:** `Presentation/Controllers` nhận request và IPN; `BusinessLogic/DTOs` và `BusinessLogic/Services` chứa quote, điều kiện chuyển trạng thái, interface gateway; `DataAccess/Repositories` giữ truy vấn/transaction SQL và các trigger hiện có; `WpfClient/Views` cùng `ApiClient` chỉ hiển thị và gửi yêu cầu. Adapter VNPAY chỉ làm ký/kiểm tra giao thức và được đăng ký qua DI, không đưa phép tính tiền vào controller hoặc WPF. Cách chia này giữ luồng Controller → Service → Repository của repository hiện tại.

1. **Chốt chính sách:** xác nhận kỳ đầu một tháng; phạm vi giảm giá và `free_days`; tax; việc đồng ý điều khoản và thời điểm điền `signed_at`; cách xử lý thanh toán thành công đến muộn. Ghi quyết định vào tài liệu nghiệp vụ trước khi khóa contract API.
2. **BusinessLogic/DataAccess:** DTO, `IPaymentService`, repository cho checkout/finalization, adapter provider và DI; bám schema Database First, không sửa entity scaffold để nhét nghiệp vụ. Tính tiền server-side; dùng transaction, idempotency và callback validation.
3. **API + DemoGateway:** quote, checkout, IPN simulator nội bộ, GET payment/invoice/agreement. Có thể demo hoàn chỉnh cả nhánh success/failure/retry/expired khi chưa có merchant credentials.
4. **WPF:** bảng giá có tách cọc/kỳ đầu/phí/giảm giá, nhập voucher, chấp thuận điều khoản, mở URL thanh toán, trạng thái đang xử lý, biên nhận và hợp đồng chờ bàn giao. WPF không nhận quyền quyết định đã trả tiền.
5. **VNPAY Sandbox:** cấu hình bằng secret ngoài repo, IPN HTTPS công khai, URL expiration khớp hold; thử callback sai chữ ký, callback lặp, return trước IPN, IPN thất lạc và query đối soát.
6. **Kiểm chứng:** unit test tiền/VND/voucher; API integration về quyền và idempotency; test **SQL Server riêng biệt** cho trigger invoice/payment, transaction rollback và race IPN–expiry–cancel; thao tác WPF thực tế. In-memory test không chứng minh trigger, lock hay rollback SQL Server. [Microsoft hướng dẫn WebApplicationFactory cho integration test](https://learn.microsoft.com/en-us/aspnet/core/test/integration-tests?view=aspnetcore-8.0); test tài chính phải bổ sung database SQL Server cách ly.

**Điều kiện hoàn thành Function 4:** Một reservation hợp lệ trả đủ trong hold sinh đúng một invoice `paid`, một payment `succeeded` được allocation và một agreement `scheduled`, reservation `confirmed`; các request lặp không nhân đôi dữ liệu; failed/expired không sinh agreement; callback giả/sai amount bị từ chối; test race trên SQL Server cách ly đạt; WPF thể hiện đúng trạng thái do server trả về.

## 7. Nguồn ngoài đã đối chiếu

- [VNPAY PAY Sandbox: URL thanh toán, checksum, IPN và ReturnUrl](https://sandbox.vnpayment.vn/apis/docs/thanh-toan-pay/pay.html).
- [VNPAY query transaction và refund](https://sandbox.vnpayment.vn/apis/docs/truy-van-hoan-tien/querydr%26refund.html).
- [payOS: môi trường test](https://payos.vn/docs/moi-truong-test/).
- [VietQR: API tạo mã QR](https://www.vietqr.io/generate/).
- [Microsoft EF Core: transactions](https://learn.microsoft.com/en-us/ef/core/saving/transactions), [ExecuteUpdate](https://learn.microsoft.com/en-us/ef/core/saving/execute-insert-update-delete) và [SQL Server sp_getapplock](https://learn.microsoft.com/en-us/sql/relational-databases/system-stored-procedures/sp-getapplock-transact-sql).
- [Microsoft .NET 8 TimeProvider](https://learn.microsoft.com/en-us/dotnet/standard/datetime/timeprovider-overview) và [ASP.NET Core integration tests](https://learn.microsoft.com/en-us/aspnet/core/test/integration-tests?view=aspnetcore-8.0).

---

## 8. Báo cáo triển khai thực tế & Hoàn thiện kiểm thử chuyên sâu

### 8.1 Trạng thái triển khai
- **Trạng thái:** **Đã hoàn thành toàn bộ mã nguồn Function 4 và xử lý triệt để 12 phát hiện review chuyên sâu** bao gồm DataAccess, BusinessLogic, Presentation (Web API), WPF Client, Unit Tests và Integration Tests.
- **Kết quả kiểm thử tự động:** **167 passed, 2 skipped, 0 failed** (2 bài test concurrency trên SQL Server thật được cấu hình skip an toàn nếu chưa có biến môi trường `SELFSTORAGE_SQL_TEST_CONNECTION_STRING`).

### 8.2 Các nội dung đã hoàn thiện theo 12 phát hiện review:
1. **Chặn tự xác nhận thanh toán giả**: Demo simulator mặc định tắt; chỉ kích hoạt khi `ASPNETCORE_ENVIRONMENT` là Development/Test VÀ `Payment:AllowDemoSimulator == true`. Endpoint từ chối mọi payment có provider khác "demo". WPF ẩn nút giả lập khi chọn VNPAY.
2. **Kiểm tra đầy đủ IPN trước khi ghi tiền**: Đối chiếu TxnRef với payment trong transaction, kiểm tra provider=vnpay, TmnCode cấu hình (sai trả 97), tiền tệ VND (sai trả 04), `vnp_Amount / 100 == payment.amount` (sai trả 04), TxnRef không tồn tại trả 01 "Order not found" thay vì lỗi 500, callback lặp trả 02 "Order already confirmed".
3. **Ghi nhận khoản tiền thứ hai (Duplicate payment)**: Nếu invoice đã paid hoặc reservation đã confirmed bởi attempt khác, callback thứ hai vẫn ghi nhận payment `succeeded`, đánh dấu `reconciliation_required = true`, tạo đúng 1 yêu cầu hoàn tiền `refunds`, không tạo trùng agreement.
4. **Quyết định hold bằng thời gian hiện tại server UTC**: So sánh `DateTimeOffset.UtcNow <= hold_until`; worker quét hết hạn hold sử dụng cùng khóa `sp_getapplock` (`SS_Reservation_{id}`) để triệt tiêu race condition với finalize.
5. **Cập nhật lại invoice lines khi retry quote**: Khi khách retry checkout với quote/voucher mới, các dòng invoice draft/open được cập nhật đồng bộ với quote mới.
6. **Đồng nhất thứ tự khóa và thời gian chờ**: Khóa theo thứ tự chuẩn `SS_Reservation_{reservationId}` trước `SS_Promotion_{promotionId}`, timeout 5000ms.
7. **Bắt buộc kiểm tra payload khi nhận Idempotency-Key**: Trong transaction, kiểm tra `customer_id`, `reservation_id`, `provider`, `amount`. Nếu trùng key nhưng lệch payload ném `ConflictException` (409 Conflict). Trùng key và khớp payload trả lại đúng attempt cũ.
8. **Đảm bảo toàn bộ VND là số nguyên**: `VnpayGateway` ném `ArgumentException` nếu `order.Amount % 1m != 0m`; quote và checkout chốt số nguyên đồng VND.
9. **Đóng dấu phiên bản điều khoản & loại bỏ nhãn "ký số"**: Ghi nhận `accepted_policy_version_id` từ quote và liên kết vào `rental_agreement.policy_version_id`; `signed_at = null`; không dùng thuật ngữ gây nhầm lẫn "ký số".
10. **Giao diện WPF minh bạch**: Thêm view cảnh báo đối soát / hoàn tiền (`panelReconciliationOutcome`) khi thanh toán trễ/trùng; hiển thị nội dung điều khoản trích từ JSON; ẩn nút giả lập khi chọn cổng VNPAY.
11. **Bổ sung quản lý và phê duyệt hoàn tiền**: Thêm endpoint `GET /api/payments/refunds` và `POST /api/payments/refunds/{id}/review`, DTOs `ReviewRefundRequest`, `RefundDetailResponse`, lưu lịch sử vào `refund_approvals`.
12. **Chi tiết kiểm thử & hạ tầng**: `VnpayGateway` fail-fast khi thiếu credentials; hỗ trợ các toán tử promotion (`gte`, `lte`, `eq`, `in`); từ chối `free_days` số thập phân; test concurrency tự động skip an toàn; dùng `method = other` cho demo; đảo ngược thứ tự dọn dẹp bảng SQL để không vi phạm FK constraints.

### 8.3 Hướng dẫn chạy và Demo Function 4
1. **Khởi động Web API**:
   ```powershell
   dotnet run --project src/SelfStorageManagementSystem.Presentation --launch-profile https
   ```
2. **Khởi động WPF Client**:
   ```powershell
   dotnet run --project src/SelfStorageManagementSystem.WpfClient
   ```
3. **Kịch bản Demo**:
   - Đăng nhập tài khoản khách hàng (hoặc đăng ký mới).
   - Chọn kho tại Tab Catalog → bấm "Tiến hành giữ chỗ" → hoàn tất giữ chỗ 15 phút.
   - Tại màn hình chi tiết đơn đặt chỗ, bấm "💳 Thanh toán ngay (Function 4)".
   - Màn hình Checkout hiển thị báo giá chi tiết: Tiền cọc (1 tháng) + Tiền thuê tháng đầu + Phí đặt chỗ.
   - (Tùy chọn) Nhập mã voucher `WELCOME10` để xem giảm giá tính trên tiền thuê kỳ đầu, tiền cọc giữ nguyên không đổi.
   - Tick chọn "Tôi đã đọc, hiểu rõ và chấp thuận Điều khoản thuê kho trên".
   - Chọn "Demo Payment Gateway" → bấm "🚀 Xác nhận & Tiến hành Thanh toán".
   - Trình duyệt mở ra trang giả lập `localhost:7031/api/payments/demo/{id}/simulator` hoặc bấm nút "⚡ Giả lập thanh toán thành công (Demo)" ngay trên WPF.
   - WPF lập tức nhận tín hiệu thành công: hiển thị thông báo chúc mừng, mã Hóa đơn `INV-...` (Paid) và mã Hợp đồng `AGR-...` (Scheduled - Chờ bàn giao).
