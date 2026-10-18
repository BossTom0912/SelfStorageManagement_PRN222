# Báo Cáo Thực Hiện Task T12: Thanh Toán Hóa Đơn Hợp Đồng và Refund

- **Task ID**: T12
- **Tên Task**: Thanh toán hóa đơn hợp đồng và refund
- **Owner**: Gemini A (Architecture, Foundation & Core Integration Lead)
- **Đợt (Wave)**: 5 (Song song cùng T13 - Gemini B và T14 - Gemini C)
- **Chờ (Dependencies)**: T10 (**VERIFIED**)
- **Trạng thái**: **VERIFIED**
- **Contract Version**: v1
- **Không gian làm việc**: `D:\FPT\PRN222\Project\Final_Project_Web`
- **Solution Web**: `SelfStorageManagementSystem.Web.sln`
- **Môi trường**: Windows 11, .NET SDK 8.0, ASP.NET Core Kestrel 8.0 (Port 7041)

---

## 1. Mục Tiêu và Phạm Vi Hoàn Thành

Task T12 đảm bảo toàn bộ quy trình thanh toán hóa đơn hợp đồng, giả lập cổng thanh toán thử nghiệm (Demo Simulator), đối soát giao dịch và quy trình xét duyệt hoàn tiền (Refund Review) được chuyển đổi trọn vẹn từ kiến trúc máy trạm cũ (WPF/API riêng biệt) sang ứng dụng Web Razor Pages và Controller Callback Adapters độc lập, vận hành trên Host Web (Port 7041):

1. **Bộ trang Razor Pages thanh toán và đối soát (`Pages/Payments`)**:
   - `Checkout.cshtml` / `Checkout.cshtml.cs`:
     - Tính toán biểu phí (Quote) trực tiếp từ máy chủ thông qua `IPaymentService.GetCheckoutQuoteAsync`.
     - Áp dụng mã ưu đãi voucher linh hoạt và tính lại chiết khấu qua handler AJAX `OnPostApplyPromoAsync`.
     - Bắt buộc kiểm tra phiên bản điều khoản dịch vụ hợp lệ (`rental_terms`) và xác nhận checkbox `AcceptPolicy`.
     - Cơ chế `Idempotency-Key` ổn định, duy trì xuyên suốt cùng một phiên thử nghiệm để ngăn chặn việc tạo giao dịch trùng lặp khi người dùng click đúp hoặc gửi lại form.
   - `DemoSimulator.cshtml` / `DemoSimulator.cshtml.cs`:
     - Giao diện giả lập thanh toán hoạt động 100% trên trình duyệt Web với Cookie Authentication + Antiforgery Token.
     - Loại bỏ hoàn toàn mọi hướng dẫn hoặc phụ thuộc quay về ứng dụng WPF cũ.
     - Chặn tuyệt đối ở môi trường `Production` (trả về mã HTTP `403 Forbidden` nếu `ASPNETCORE_ENVIRONMENT == "Production"` hoặc cấu hình `Payment:AllowDemoSimulator != true`).
   - `Result.cshtml` / `Result.cshtml.cs`: Trang hiển thị kết quả giao dịch thanh toán trực quan, liên kết trực tiếp tới hợp đồng (`/Agreements/Details`), hóa đơn (`/Invoices/Details`) và biên nhận thanh toán.
   - `Details.cshtml` / `Details.cshtml.cs`: Xem chi tiết biên lai giao dịch thanh toán, số tiền, mã tham chiếu, mã `idempotency_key`, lý do thất bại (nếu có); hỗ trợ cả route param và query param `id`.
   - `Index.cshtml` / `Index.cshtml.cs`: Bảng điều khiển đối soát giao dịch (Reconciliation) và quản lý xét duyệt hoàn tiền (Refund Review) phân quyền chặt chẽ cho Quản lý cơ sở (`FacilityManager`) và Quản trị viên (`Admin`).

2. **Trang chi tiết hóa đơn & Hợp đồng thuê (`Pages/Invoices` & `Pages/Agreements`)**:
   - `Pages/Invoices/Details.cshtml` / `Details.cshtml.cs`: Hiển thị chi tiết hóa đơn theo các dòng chi phí (Line items), cọc, tiền thuê, chiết khấu, VAT và tổng thanh toán; bảo vệ quyền sở hữu dữ liệu khách hàng.
   - `Pages/Agreements/Details.cshtml` / `Details.cshtml.cs`: Hiển thị các cam kết tài chính (giá thuê tháng, tiền đặt cọc ban đầu, số dư tiền đặt cọc hiện tại), trạng thái hợp đồng (`SCHEDULED`, `ACTIVE`), thông tin ô kho và điều khoản pháp lý.

3. **Cổng tiếp nhận Callbacks & Webhook (`Controllers/PaymentsApiController.cs`) trên Host Web Port 7041**:
   - `/api/payments/vnpay/ipn`: Webhook Server-to-Server tiếp nhận thông báo từ cổng VNPAY. Kiểm tra nghiêm ngặt chữ ký bảo mật HMAC-SHA512. Đây là **nguồn sự thật duy nhất** (Sole Source of Truth) để ghi nhận thanh toán thành công và khởi tạo hợp đồng thuê. Phản hồi các mã chuẩn VNPAY: `"00"` (Success), `"97"` (Invalid Signature), `"02"` (Order already confirmed), `"04"` (Invalid Amount).
   - `/api/payments/vnpay/return`: Trang trả về hiển thị sau khi người dùng tương tác tại cổng VNPAY. Chỉ mang tính chất thông báo hiển thị, tuyệt đối **không thay đổi trạng thái tài chính**.
   - `/api/payments/demo/{id}/simulator` & `/api/payments/demo/{id}/complete`: Adapter chuyển tiếp và xử lý hoàn tất thanh toán demo nội bộ. Bị khóa chặt ở Production (`403 Forbidden`).

---

## 2. Thiết Kế An Toàn và Tuân Thủ Codex Rule

### 2.1. Bảo Vệ Giao Dịch Bằng Idempotency-Key Ổn Định
- Tại form Checkout, `IdempotencyKey` được sinh ra tự động hoặc nhận từ client và được bảo lưu trong `hidden input`.
- Khi người dùng gửi yêu cầu thanh toán nhiều lần hoặc click đúp nút thanh toán, `IdempotencyKey` giữ nguyên giá trị ban đầu.
- Backend `PaymentService` tra cứu theo `idempotency_key`: nếu đã tồn tại bản ghi thanh toán đang hoạt động, hệ thống trả về thông tin giao dịch hiện có thay vì tạo bản ghi mới trong cơ sở dữ liệu.

### 2.2. Ngăn Chặn Demo Simulator Trong Môi Trường Production
- Cả controller adapter (`PaymentsApiController`) và trang Razor Page (`DemoSimulator.cshtml.cs`) đều triển khai cơ chế kiểm tra môi trường nghiêm ngặt hai tầng:
  1. Kiểm tra môi trường hosting: Nếu `EnvironmentName` là `"Production"`, lập tức từ chối với mã HTTP `403 Forbidden`.
  2. Kiểm tra cờ cấu hình: `Payment:AllowDemoSimulator` phải được bật rõ ràng là `true` trong môi trường `Development`, `Test` hoặc `Testing`.
- Nếu không thỏa mãn cả hai điều kiện, yêu cầu bị chặn ngay trước khi thực thi bất kỳ logic nghiệp vụ nào.

### 2.3. Tách Biệt Rõ Ràng Giữa IPN Webhook và Return URL
- **IPN (`/api/payments/vnpay/ipn`)**:
  - Nhận yêu cầu ngầm từ máy chủ VNPAY kèm chữ ký HMAC-SHA512 (`vnp_SecureHash`).
  - Kiểm tra tính toàn vẹn dữ liệu, kiểm tra khớp số tiền giao dịch (`vnp_Amount / 100 == payment.amount`).
  - Cập nhật trạng thái thanh toán từ `pending` sang `succeeded` hoặc `failed`.
  - Khởi tạo hóa đơn và hợp đồng thuê tương ứng theo giao dịch tài chính.
- **Return URL (`/api/payments/vnpay/return`)**:
  - Nhận chuyển hướng từ trình duyệt của khách hàng.
  - Chỉ render giao diện thông báo trạng thái giao dịch cho người dùng.
  - Tuyệt đối không thay đổi trạng thái trong DB để chống lại các cuộc tấn công giả mạo chuyển hướng hoặc thao túng query string từ phía client.

### 2.4. Phân Quyền Xét Duyệt Hoàn Tiền (Refund Review)
- Chức năng xét duyệt hoàn tiền tại `Pages/Payments/Index.cshtml` yêu cầu vai trò `FacilityManager` hoặc `Admin`.
- Kiểm tra chặt chẽ phạm vi cơ sở (Facility Scoping): Quản lý chỉ được quyền duyệt refund cho các giao dịch thuộc cơ sở mình quản lý.
- Kiểm tra tính hợp lệ của trạng thái thanh toán và hợp đồng trước khi thực hiện hoàn tiền.

---

## 3. Danh Sách Tệp Mã Nguồn Triển Khai

| STT | Tệp tin (File Path) | Vai trò / Chức năng |
| :---: | :--- | :--- |
| 1 | `src/SelfStorageManagementSystem.Web/Controllers/PaymentsApiController.cs` | Callback adapters: VNPAY IPN, VNPAY Return, Demo Simulator endpoints trên Host Web port 7041 |
| 2 | `src/SelfStorageManagementSystem.Web/Pages/Payments/Checkout.cshtml` / `.cs` | Giao diện thanh toán Checkout: Server quote, Voucher promo, Accepted policy version, Idempotency-Key |
| 3 | `src/SelfStorageManagementSystem.Web/Pages/Payments/DemoSimulator.cshtml` / `.cs` | Cổng thanh toán giả lập trên Web: Cookie auth + Antiforgery token, loại bỏ hướng dẫn WPF, chặn Production |
| 4 | `src/SelfStorageManagementSystem.Web/Pages/Payments/Result.cshtml` / `.cs` | Trang hiển thị kết quả thanh toán, điều hướng đến hóa đơn, hợp đồng và biên nhận |
| 5 | `src/SelfStorageManagementSystem.Web/Pages/Payments/Details.cshtml` / `.cs` | Chi tiết giao dịch thanh toán, hỗ trợ cả route param và query param `id` |
| 6 | `src/SelfStorageManagementSystem.Web/Pages/Payments/Index.cshtml` / `.cs` | Dashboard đối soát (Reconciliation) và xét duyệt hoàn tiền (Refund Review) cho Manager/Admin |
| 7 | `src/SelfStorageManagementSystem.Web/Pages/Invoices/Details.cshtml` / `.cs` | Chi tiết hóa đơn, các dòng mục chi phí, tổng tiền, thuế, giảm trừ và bảo vệ quyền sở hữu |
| 8 | `src/SelfStorageManagementSystem.Web/Pages/Agreements/Details.cshtml` / `.cs` | Chi tiết hợp đồng thuê, cam kết tài chính, tiền đặt cọc, thông tin ô kho và quy chuẩn bàn giao |
| 9 | `src/SelfStorageManagementSystem.BusinessLogic/Services/Implementations/PaymentService.cs` | Bổ sung môi trường `"Testing"` vào bộ lọc môi trường hợp lệ cho demo payment |
| 10 | `src/SelfStorageManagementSystem.Web/appsettings.json` | Cấu hình cổng 7041, `Payment:AllowDemoSimulator`, VNPAY callback URLs |
| 11 | `tests/SelfStorageManagementSystem.WebTests/Fixtures/PaymentWebTestFixture.cs` | Test fixture tích hợp: Seed đầy đủ cơ sở, đơn giá, ô kho, voucher, chính sách, tài khoản khách và quản lý |
| 12 | `tests/SelfStorageManagementSystem.WebTests/PaymentRazorPagesAndCallbackTests.cs` | Bộ kiểm thử tích hợp tự động gồm 22 bài test chuyên sâu cho T12 |

---

## 4. Kết Quả Kiểm Thử và Nghiệm Thu Thực Tế

### 4.1. Bộ Kiểm Thử Tích Hợp T12 (`PaymentRazorPagesAndCallbackTests`)
Đã thực thi toàn bộ 22 kịch bản kiểm thử tích hợp trên host Web và đạt tỉ lệ thành công **22/22 (100%)**:

```text
Test run for D:\FPT\PRN222\Project\Final_Project_Web\tests\SelfStorageManagementSystem.WebTests\bin\Debug\net8.0\SelfStorageManagementSystem.WebTests.dll (.NETCoreApp,Version=v8.0)
A total of 1 test files matched the specified pattern.

Passed!  - Failed:     0, Passed:    22, Skipped:     0, Total:    22, Duration: 5 s - SelfStorageManagementSystem.WebTests.dll (net8.0)
```

**Chi tiết 22 kịch bản đã kiểm chứng**:
1. `Checkout_AnonymousUser_RedirectsToLogin`: Khách vãng lai bị chuyển hướng đăng nhập.
2. `Checkout_Get_RendersServerQuoteAndTermsPolicy`: Nạp quote máy chủ, giá gốc và điều khoản pháp lý.
3. `Checkout_PostApplyPromo_CalculatesVoucherDiscount`: Áp dụng voucher hợp lệ, chiết khấu được tính toán lại chính xác.
4. `Checkout_PostApplyPromo_InvalidCode_ReturnsValidationError`: Mã giảm giá không tồn tại bị từ chối với thông báo lỗi.
5. `Checkout_Post_WithoutAcceptingPolicy_FailsValidation`: Không tích checkbox đồng ý điều khoản sẽ không thể tạo giao dịch.
6. `Checkout_Post_CreatesPaymentRecord_WithPendingStatus_AndRedirectsToDemoSimulator`: Tạo giao dịch thanh toán ở trạng thái `pending` và chuyển hướng sang Demo Simulator.
7. `Checkout_Post_SameIdempotencyKeyTwice_ReturnsSamePaymentWithoutDuplicates`: Gửi 2 lần cùng `IdempotencyKey` không tạo bản ghi thanh toán trùng lặp trong DB.
8. `DemoSimulator_InProductionEnvironment_ReturnsForbidden403`: Môi trường `Production` bị chặn với HTTP `403 Forbidden`.
9. `DemoSimulator_PostComplete_Success_ConfirmsPaymentAndCreatesAgreement`: Mô phỏng thanh toán thành công, xác nhận tài chính và khởi tạo hợp đồng thuê.
10. `DemoSimulator_PostComplete_Failure_RecordsFailureAndAllowsRetry`: Mô phỏng thanh toán thất bại, ghi nhận lý do và cho phép thử lại phiên mới.
11. `VnpayIpn_ValidSignature_ConfirmsPayment_AndCreatesAgreement`: IPN với chữ ký HMAC-SHA512 hợp lệ xác nhận giao dịch thành công (mã `"00"`).
12. `VnpayIpn_InvalidSignature_RejectsFinancialUpdate`: IPN với chữ ký sai bị từ chối (mã `"97"`), trạng thái thanh toán trong DB giữ nguyên `pending`.
13. `VnpayIpn_Replay_ReturnsCode02AlreadyConfirmed`: IPN gửi lại cho giao dịch đã xử lý phản hồi mã `"02"` (Order already confirmed).
14. `VnpayIpn_AmountMismatch_ReturnsCode04InvalidAmount`: IPN gửi sai số tiền bị từ chối với mã `"04"` (Invalid Amount).
15. `VnpayReturn_DisplayOnly_DoesNotMutateFinancialState`: Endpoint Return URL hiển thị kết quả mà không làm thay đổi trạng thái tài chính.
16. `PaymentResult_DisplaysPaymentDetailsAndAgreementLink`: Trang Result hiển thị mã tham chiếu và link liên kết hợp đồng/hóa đơn.
17. `PaymentDetails_AllowsAccessToOwner`: Chủ sở hữu giao dịch truy cập xem chi tiết biên nhận thành công.
18. `PaymentDetails_StrangersAccess_ReturnsForbidden`: Khách hàng khác truy cập trái phép bị từ chối (`403 Forbidden`).
19. `InvoiceDetails_RendersLineItemsAndFinancialTotals`: Trang chi tiết hóa đơn hiển thị đầy đủ các dòng mục và tổng tiền.
20. `AgreementDetails_RendersScheduledNoticeAndTerms`: Trang hợp đồng hiển thị tiền đặt cọc ban đầu và thông tin kho.
21. `Reconciliation_ManagerCanViewPaymentsInScope`: Quản lý xem được danh sách thanh toán trong phạm vi cơ sở mình quản lý.
22. `RefundReview_AuthorizedManager_ApprovesRefundSuccessfully`: Quản lý cơ sở xét duyệt hoàn tiền thành công cho giao dịch hợp lệ.

### 4.2. Kiểm Thử Hồi Quy Toàn Bộ Web Solution (`SelfStorageManagementSystem.WebTests`)
Toàn bộ **318/318** tests tích hợp web của tất cả các đợt (T00 đến T14) đều vượt qua thành công:
```text
Passed!  - Failed:     0, Passed:   318, Skipped:     0, Total:   318, Duration: 1 m 2 s - SelfStorageManagementSystem.WebTests.dll (net8.0)
```

### 4.3. Kiểm Thử Nghiệp Vụ Core (`SelfStorageManagementSystem.Tests`)
Toàn bộ **225/225** tests tầng Business Logic đều vượt qua thành công:
```text
Passed!  - Failed:     0, Passed:   225, Skipped:     0, Total:   225, Duration: 2 s - SelfStorageManagementSystem.Tests.dll (net8.0)
```

---

## 5. Kết Luận Nghiệm Thu (Handover Conclusion)

- **Trạng thái Task T12**: **VERIFIED**
- Mọi cam kết kiến trúc đã được hoàn thành:
  - 100% quy trình Checkout, Demo Simulator, Result, Details, Invoices, Agreements, Reconciliation và Refund Review vận hành trực tiếp trên nền tảng Web Razor Pages với Cookie Authentication và Antiforgery Token.
  - Hoàn toàn loại bỏ mọi chỉ dẫn quay lại WPF.
  - Demo Simulator được kiểm soát chặt chẽ và bị chặn hoàn toàn tại môi trường Production.
  - Cổng tiếp nhận VNPAY IPN webhook và Return URL hoạt động chuẩn mực trên Host Web (Port 7041), bảo vệ chữ ký số HMAC-SHA512 và xác nhận tài chính an toàn.
  - Đảm bảo tính Idempotency, chống gửi trùng giao dịch và kiểm soát quyền hạn phân cấp nghiêm ngặt.
- Sẵn sàng bàn giao để mở khóa Task **T15** (Tích hợp Realtime cả 5 chức năng).
