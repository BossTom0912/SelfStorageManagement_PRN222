# Báo cáo Sửa Finding T12 — Thanh toán, Callback và Đồng bộ Bàn giao

> **Trạng thái:** `READY_FOR_CODEX_REVIEW`  
> **Người thực hiện (Owner):** Gemini A  
> **Đợt (Wave):** 5  
> **Task:** T12 — Thanh toán hóa đơn hợp đồng và refund  
> **Workspace phát triển:** `D:\FPT\PRN222\Project\Final_Project_Web`  
> **Workspace bàn giao:** `D:\FPT\PRN222\Project\Final_Project`  
> **Ngày báo cáo:** 16/10/2026  

---

## 1. Tóm tắt findings cần xử lý

| Mã Finding | Mức độ | Phạm vi | Mô tả tóm tắt | Trạng thái |
| :--- | :--- | :--- | :--- | :--- |
| **T12-F01** | **P1 (High)** | `PaymentsApiController.cs` (`VnpayReturn`) | Lỗ hổng Cross-Site Scripting (XSS) do chèn query parameter `vnp_TxnRef` thô vào HTML body và thuộc tính `href`. | `IMPLEMENTED — chờ Codex review` |
| **T12-F02** | **P1 (High)** | Workspace `Final_Project` | Thiếu toàn bộ 20 tệp T12, cấu hình và báo cáo; Task Board hai workspace bị lệch trạng thái. | `IMPLEMENTED — chờ Codex review` |

---

## 2. Chi tiết Finding T12-F01: XSS ở trang VNPAY Return

### 2.1. Nguyên nhân gốc rễ (Root Cause)
- **Vị trí lỗi:** [`src/SelfStorageManagementSystem.Web/Controllers/PaymentsApiController.cs`](file:///D:/FPT/PRN222/Project/Final_Project_Web/src/SelfStorageManagementSystem.Web/Controllers/PaymentsApiController.cs#L66-L112), action `VnpayReturn`.
- **Cơ chế phát sinh:** Endpoint `[HttpGet("vnpay/return")]` được đánh dấu `[AllowAnonymous]`. Biến `rawTxnRef = Request.Query["vnp_TxnRef"].ToString()` được lấy trực tiếp từ query string do client gửi lên và được nối chuỗi nội suy thẳng vào chuỗi HTML mà không qua bộ mã hóa (`HtmlEncoder`):
  1. Chèn vào nội dung thẻ text: `<strong class="font-monospace text-primary">{{rawTxnRef}}</strong>`
  2. Chèn vào thuộc tính thẻ liên kết: `<a href="/Payments/Details?id={{rawTxnRef}}"...>`
- **Hậu quả:** Kẻ tấn công có thể tạo liên kết lừa đảo dạng `https://localhost:7041/api/payments/vnpay/return?vnp_TxnRef="><script>alert('xss')</script>&vnp_ResponseCode=00` hoặc dùng payload thoát thuộc tính `"><img src=x onerror=...>`, kích hoạt script độc hại trong ngữ cảnh phiên duyệt web của người dùng.

### 2.2. Danh sách tệp và các dòng đã sửa
- [`src/SelfStorageManagementSystem.Web/Controllers/PaymentsApiController.cs`](file:///D:/FPT/PRN222/Project/Final_Project_Web/src/SelfStorageManagementSystem.Web/Controllers/PaymentsApiController.cs):
  - **Dòng 1–13:** Thêm chỉ thị `using System.Text.Encodings.Web;`.
  - **Dòng 66–95:**
    - Kiểm tra định dạng nghiêm ngặt bằng `long.TryParse(rawTxnRef, out var paymentId) && paymentId > 0`.
    - Mã hóa an toàn giá trị text hiển thị qua `HtmlEncoder.Default.Encode(...)` và cắt độ dài an toàn tối đa 64 ký tự nếu là chuỗi bất thường.
    - Với `isValidPaymentId == true`: Chỉ chèn ID kiểu số nguyên đã qua validate vào URL liên kết: `/Payments/Details?id={paymentId}`.
    - Với `isValidPaymentId == false`: Không sinh thẻ liên kết mở chi tiết; thay bằng cảnh báo an toàn Bootstrap Alert (`"⚠️ Cảnh báo: Mã tham chiếu giao dịch không đúng định dạng. Không thể mở trang chi tiết trực tiếp."`) và cung cấp nút quay về danh sách an toàn `/Payments`.
  - **Dòng 115–126:** Render `{{safeTxnRefDisplay}}` và `{{detailActionHtml}}` an toàn tuyệt đối.
  - **Giữ nguyên nguyên tắc kiến trúc cốt lõi:** Endpoint `VnpayReturn` tuyệt đối chỉ đọc (display-only), không xác nhận thanh toán, không thay đổi trạng thái database hay sinh invoice/agreement (nguồn sự thật tài chính duy nhất là VNPAY IPN).

### 2.3. So sánh hành vi trước và sau khi sửa
| Tình huống đầu vào | Trước khi sửa | Sau khi sửa |
| :--- | :--- | :--- |
| **`vnp_TxnRef="><script>alert(1)</script>`** | Render thẻ `<script>` thực thi mã JavaScript tùy ý; chèn thuộc tính độc hại vào thẻ `<a>`. | Mã hóa text an toàn; hiển thị cảnh báo định dạng không hợp lệ; không sinh thẻ script hoặc liên kết độc hại; điều hướng fallback về `/Payments`. |
| **`vnp_TxnRef=" onmouseover="alert('xss')"`** | Kích hoạt sự kiện JavaScript khi người dùng di chuột qua liên kết. | Chuỗi được encode an toàn; không lọt vào thuộc tính DOM; chuyển về fallback an toàn. |
| **`vnp_TxnRef=15` (Hợp lệ)** | Hiển thị mã `15`, link đến `/Payments/Details?id=15`. | Hiển thị mã `15`, link an toàn đến `/Payments/Details?id=15`. |
| **Mutation DB khi gọi Return** | Không cập nhật (giữ nguyên display-only). | Giữ nguyên display-only; đã bổ sung unit/integration test xác nhận DB trạng thái không đổi. |

---

## 3. Chi tiết Finding T12-F02: Đồng bộ bàn giao sang `Final_Project`

### 3.1. Nguyên nhân gốc rễ (Root Cause)
- Sau khi hoàn thành Wave 5 tại workspace thử nghiệm `Final_Project_Web`, agent chưa thực hiện bước bàn giao tệp sang repo chính `Final_Project`.
- Workspace `Final_Project` thiếu toàn bộ mã nguồn Razor Pages liên quan đến Payments/Invoices/Agreements, controller API callback, service cập nhật `PaymentService.cs`, cấu hình `appsettings.json`, fixture/test WebTests và báo cáo `AgentReport/WebMigration_T12.md`.
- Trạng thái Task Board giữa hai nơi bị lệch (`Final_Project_Web` ghi VERIFIED/READY trong khi `Final_Project` ghi READY/WAITING).

### 3.2. Biện pháp xử lý và đồng bộ
1. Đồng bộ toàn bộ 20 tệp bắt buộc thuộc phạm vi T12 từ `Final_Project_Web` sang `Final_Project`.
2. Đồng bộ các tệp hạ tầng Web chung (`SelfStorageManagementSystem.Web.sln`, `SelfStorageManagementSystem.Web.csproj`, `wwwroot`, v.v.) bảo đảm dự án `Final_Project` build và test độc lập thành công.
3. Cập nhật và đồng bộ `AgentReport/WebMigration_TaskBoard.md` trên cả hai workspace:
   - Chuyển T12 về trạng thái: `IMPLEMENTED — chờ Codex review`.
   - Chuyển T15 về trạng thái: `WAITING` (chờ nghiệm thu T12 từ Codex).
   - Không tự ý chuyển trạng thái sang `VERIFIED`.

---

## 4. Bảng đối chiếu tệp T12 và mã băm SHA-256 hai workspace

Toàn bộ 20 tệp bàn giao T12 được kiểm tra mã băm SHA-256 giữa `Final_Project_Web` và `Final_Project`:

| STT | Tệp bàn giao T12 | SHA-256 (`Final_Project_Web`) | SHA-256 (`Final_Project`) | Kết quả |
| :---: | :--- | :--- | :--- | :---: |
| 1 | `src\SelfStorageManagementSystem.Web\Controllers\PaymentsApiController.cs` | `26AFFFA6B6EC3008B76850B40192B247F4E3667EE7753487E6119C5F2DF5D51F` | `26AFFFA6B6EC3008B76850B40192B247F4E3667EE7753487E6119C5F2DF5D51F` | **MATCH (Khớp)** |
| 2 | `src\SelfStorageManagementSystem.Web\Pages\Payments\Checkout.cshtml` | `384E0EEEECFE77E3EE16D2242A3B27A9E57BD02DA8BCE6B76BB41B7136C86E8E` | `384E0EEEECFE77E3EE16D2242A3B27A9E57BD02DA8BCE6B76BB41B7136C86E8E` | **MATCH (Khớp)** |
| 3 | `src\SelfStorageManagementSystem.Web\Pages\Payments\Checkout.cshtml.cs` | `FBA3DE00578DDE5D471D2E08494A7187D908B0A91AD529820038D553CAF0583E` | `FBA3DE00578DDE5D471D2E08494A7187D908B0A91AD529820038D553CAF0583E` | **MATCH (Khớp)** |
| 4 | `src\SelfStorageManagementSystem.Web\Pages\Payments\DemoSimulator.cshtml` | `71376FFB5DA9EC243F7143D924D7C2D7061AC35B0BEBAC647334C08FC03FB140` | `71376FFB5DA9EC243F7143D924D7C2D7061AC35B0BEBAC647334C08FC03FB140` | **MATCH (Khớp)** |
| 5 | `src\SelfStorageManagementSystem.Web\Pages\Payments\DemoSimulator.cshtml.cs` | `D829E066A64FB33AB1CDA90E7F418C64245378D087F49577AC12848AD2DE3AD6` | `D829E066A64FB33AB1CDA90E7F418C64245378D087F49577AC12848AD2DE3AD6` | **MATCH (Khớp)** |
| 6 | `src\SelfStorageManagementSystem.Web\Pages\Payments\Result.cshtml` | `3EF305822D3DC3B7EAA71451A7BF6F25E80EB8D7976A14266F6A652C966A583A` | `3EF305822D3DC3B7EAA71451A7BF6F25E80EB8D7976A14266F6A652C966A583A` | **MATCH (Khớp)** |
| 7 | `src\SelfStorageManagementSystem.Web\Pages\Payments\Result.cshtml.cs` | `89FAF32D23CE4072896AD418323F50AF7A873532F8164EFA1FE68190C475DB83` | `89FAF32D23CE4072896AD418323F50AF7A873532F8164EFA1FE68190C475DB83` | **MATCH (Khớp)** |
| 8 | `src\SelfStorageManagementSystem.Web\Pages\Payments\Details.cshtml` | `029F885DF4E04C1D59B8371C79B11A5804917F0A5209AEC32FB6132EE1547BED` | `029F885DF4E04C1D59B8371C79B11A5804917F0A5209AEC32FB6132EE1547BED` | **MATCH (Khớp)** |
| 9 | `src\SelfStorageManagementSystem.Web\Pages\Payments\Details.cshtml.cs` | `3436E7E4C135C96AE79FDC6AC792311EBDBFB91A880DF22C001F0CA27CC987F0` | `3436E7E4C135C96AE79FDC6AC792311EBDBFB91A880DF22C001F0CA27CC987F0` | **MATCH (Khớp)** |
| 10 | `src\SelfStorageManagementSystem.Web\Pages\Payments\Index.cshtml` | `9F5B72EF203393A3EE8E1C3B216599FBC485BC53DEB222DDCF47C6B89E39784D` | `9F5B72EF203393A3EE8E1C3B216599FBC485BC53DEB222DDCF47C6B89E39784D` | **MATCH (Khớp)** |
| 11 | `src\SelfStorageManagementSystem.Web\Pages\Payments\Index.cshtml.cs` | `D9E60FC70F1D26ECCAF3C9E482202B7F53E6858B2BA3FFFCCFD762BCC7DDB0E6` | `D9E60FC70F1D26ECCAF3C9E482202B7F53E6858B2BA3FFFCCFD762BCC7DDB0E6` | **MATCH (Khớp)** |
| 12 | `src\SelfStorageManagementSystem.Web\Pages\Invoices\Details.cshtml` | `98760846102460FCD6D6FACF42CD64C581F150197CC80E3CB901637683AE99F3` | `98760846102460FCD6D6FACF42CD64C581F150197CC80E3CB901637683AE99F3` | **MATCH (Khớp)** |
| 13 | `src\SelfStorageManagementSystem.Web\Pages\Invoices\Details.cshtml.cs` | `2AA860003251E5C22D9115BECD9017AF871ACF4FD9B56344B0A224170B6D9067` | `2AA860003251E5C22D9115BECD9017AF871ACF4FD9B56344B0A224170B6D9067` | **MATCH (Khớp)** |
| 14 | `src\SelfStorageManagementSystem.Web\Pages\Agreements\Details.cshtml` | `A49F875AC20810322517DC0698D67DFBDE433013C663C4D984788675B695E716` | `A49F875AC20810322517DC0698D67DFBDE433013C663C4D984788675B695E716` | **MATCH (Khớp)** |
| 15 | `src\SelfStorageManagementSystem.Web\Pages\Agreements\Details.cshtml.cs` | `DD7E6FC400477398C3C982DECB119A43B9BF9C2A8290A6F8FB6890A58C22047F` | `DD7E6FC400477398C3C982DECB119A43B9BF9C2A8290A6F8FB6890A58C22047F` | **MATCH (Khớp)** |
| 16 | `src\SelfStorageManagementSystem.BusinessLogic\Services\Implementations\PaymentService.cs` | `896CEBA01B2D880A1A8339873247F96B1119939C12C210B9B8BF03B1DAAF3CDB` | `896CEBA01B2D880A1A8339873247F96B1119939C12C210B9B8BF03B1DAAF3CDB` | **MATCH (Khớp)** |
| 17 | `src\SelfStorageManagementSystem.Web\appsettings.json` | `AF8E700D5692D078D35328A320AFBEADE0ABB824885DC6080D9286E2EF9B8D7F` | `AF8E700D5692D078D35328A320AFBEADE0ABB824885DC6080D9286E2EF9B8D7F` | **MATCH (Khớp)** |
| 18 | `tests\SelfStorageManagementSystem.WebTests\Fixtures\PaymentWebTestFixture.cs` | `657D864CC8B99D45129DA91DAB554AAFDAB1072F1C021E41BA4BAD01FB384A2F` | `657D864CC8B99D45129DA91DAB554AAFDAB1072F1C021E41BA4BAD01FB384A2F` | **MATCH (Khớp)** |
| 19 | `tests\SelfStorageManagementSystem.WebTests\PaymentRazorPagesAndCallbackTests.cs` | `8FC13A4EDF4C5B0C22E2CFFE2C757CFE0050567AB8B6360D75D3DF79C3FD7DD1` | `8FC13A4EDF4C5B0C22E2CFFE2C757CFE0050567AB8B6360D75D3DF79C3FD7DD1` | **MATCH (Khớp)** |
| 20 | `AgentReport\WebMigration_T12.md` | `28FF8FF014846E06477CBE18B490ED4AD8778E7EAB84351985092BFD66AF22DA` | `28FF8FF014846E06477CBE18B490ED4AD8778E7EAB84351985092BFD66AF22DA` | **MATCH (Khớp)** |
| -- | `AgentReport\WebMigration_TaskBoard.md` | `E6E4147D46BECBF12CD164B2AB5C668ACE72EF72162E249A06A7DDFAE52084C2` | `E6E4147D46BECBF12CD164B2AB5C668ACE72EF72162E249A06A7DDFAE52084C2` | **MATCH (Khớp)** |

---

## 5. Bằng chứng kiểm thử (Test Execution Evidence)

### 5.1. Kiểm thử T12 Web Tests tại `Final_Project_Web`
- **Lệnh thực thi:**
  ```powershell
  cd D:\FPT\PRN222\Project\Final_Project_Web
  dotnet test tests/SelfStorageManagementSystem.WebTests/SelfStorageManagementSystem.WebTests.csproj --filter "FullyQualifiedName~Payment" --logger "console;verbosity=normal"
  ```
- **Kết quả:**
  ```text
  Passed: 26, Failed: 0, Skipped: 0. Total: 26.
  Total time: 6.3196 Seconds. Test Run Successful.
  ```
- **Các test quan trọng về finding T12-F01:**
  - `PaymentRazorPagesAndCallbackTests.VnpayReturn_DisplayOnly_DoesNotMutateFinancialState` — **PASSED**
  - `PaymentRazorPagesAndCallbackTests.VnpayReturn_XssPayloadInTxnRef_SanitizedAndDoesNotInjectScriptOrAttribute(xssPayload: "<script>alert('xss')</script>")` — **PASSED**
  - `PaymentRazorPagesAndCallbackTests.VnpayReturn_XssPayloadInTxnRef_SanitizedAndDoesNotInjectScriptOrAttribute(xssPayload: "\"><img src=x onerror=alert(1)>")` — **PASSED**
  - `PaymentRazorPagesAndCallbackTests.VnpayReturn_XssPayloadInTxnRef_SanitizedAndDoesNotInjectScriptOrAttribute(xssPayload: "\" onmouseover=\"alert('xss')\"")` — **PASSED**
  - `PaymentRazorPagesAndCallbackTests.VnpayReturn_InvalidFormatTxnRef_RendersWarningAndSafeFallbackLink` — **PASSED**

### 5.2. Kiểm thử T12 Web Tests tại `Final_Project`
- **Lệnh thực thi:**
  ```powershell
  cd D:\FPT\PRN222\Project\Final_Project
  dotnet test tests/SelfStorageManagementSystem.WebTests/SelfStorageManagementSystem.WebTests.csproj --filter "FullyQualifiedName~Payment" --logger "console;verbosity=normal"
  ```
- **Kết quả:**
  ```text
  Passed: 26, Failed: 0, Skipped: 0. Total: 26.
  Total time: 6.2347 Seconds. Test Run Successful.
  ```

### 5.3. Kiểm thử hồi quy Core Services (`SelfStorageManagementSystem.Tests`)
- **Workspace `Final_Project_Web`:**
  - Lệnh: `dotnet test tests/SelfStorageManagementSystem.Tests/SelfStorageManagementSystem.Tests.csproj --logger "console;verbosity=normal"`
  - Kết quả: **225 Passed, 8 Skipped, 0 Failed** (Toàn bộ test liên quan `PaymentService`, `CheckInService`, `PaymentIntegrationTests` đều Passed).
- **Workspace `Final_Project`:**
  - Lệnh: `dotnet test tests/SelfStorageManagementSystem.Tests/SelfStorageManagementSystem.Tests.csproj --logger "console;verbosity=normal"`
  - Kết quả: **208 Passed, 8 Skipped, 0 Failed** (Toàn bộ test thanh toán đều Passed).

---

## 6. Phân tích rủi ro còn lại và biện pháp phòng ngừa

1. **Rủi ro môi trường thực tế VNPAY gửi tham số bổ sung:**
   - *Phân tích:* Cổng thanh toán VNPAY có thể bổ sung các query parameter khác trong tương lai.
   - *Biện pháp đã áp dụng:* Action `VnpayReturn` chỉ đọc các tham số cụ thể cần thiết (`vnp_TxnRef`, `vnp_ResponseCode`), không thực hiện bind động toàn bộ query string hay render raw data vào view, loại bỏ hoàn toàn bề mặt tấn công.
2. **Khách hàng tắt trình duyệt trước khi về Return URL:**
   - *Phân tích:* Khách hàng có thể đóng tab trình duyệt ngay khi trừ tiền thành công tại ngân hàng.
   - *Biện pháp đã áp dụng:* Hệ thống tuân thủ chặt chẽ kiến trúc thanh toán phân tán: VNPAY IPN là kênh server-to-server duy nhất xác nhận tài chính và chuyển trạng thái đơn hàng. Kênh Return URL chỉ mang tính hiển thị hướng dẫn khách hàng.
3. **Demo Simulator tại môi trường Production:**
   - *Phân tích:* Nguy cơ nhân viên hoặc khách hàng tự kích hoạt thanh toán ảo trên Production.
   - *Biện pháp đã áp dụng:* Kiểm tra kép qua cấu hình `Payment:AllowDemoSimulator` và `IWebHostEnvironment.IsDevelopment()`; tự động từ chối bằng mã lỗi HTTP 403 Forbidden nếu gọi ở môi trường khác hoặc cờ bị tắt.

---

## 7. Kết luận và Bàn giao

- Các finding **T12-F01** và **T12-F02** đã được khắc phục hoàn toàn, được kiểm chứng bằng test tự động và đối chiếu SHA-256 khớp 100% giữa hai workspace.
- **Trạng thái bàn giao hiện tại:** `READY_FOR_CODEX_REVIEW`.
