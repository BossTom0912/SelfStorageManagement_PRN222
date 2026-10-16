# Báo Cáo Thực Hiện Task T07: Catalog Bộ Lọc và Sơ Đồ Ô Kho

- **Task ID**: T07
- **Tên Task**: Catalog bộ lọc và sơ đồ ô kho
- **Owner**: Gemini C (Catalog, Floor Map, Check-in UI & E2E Lead)
- **Đợt (Wave)**: 2
- **Chờ (Dependencies)**: T04 (**VERIFIED** tại [`AgentReport/WebMigration_T04.md`](file:///D:/FPT/PRN222/Project/Final_Project_Web/AgentReport/WebMigration_T04.md))
- **Status**: **VERIFIED** (Đã khắc phục triệt để 4 findings từ Codex Review: 2 P1 Stored XSS, 1 P2 Normalized Dates, 1 P2 HTTP 404 Response)
- **Contract Version**: v0 (Baseline & Architecture Freeze tại [`docs/WebMigration_Contracts.md`](file:///D:/FPT/PRN222/Project/Final_Project_Web/docs/WebMigration_Contracts.md))
- **Thời điểm hoàn thành**: 2026-10-16 10:00:00 +07:00
- **Môi trường**: Windows 11, .NET SDK 8.0.425 / 10.0.401, PowerShell 5.1

---

## 1. Mục Tiêu và Phạm Vi Hoàn Thành

1. **Trang Danh Mục Cơ Sở Kho (`/Catalog`)**:
   - Xây dựng Razor Page `Pages/Catalog/Index.cshtml` và PageModel `Index.cshtml.cs`.
   - Tìm kiếm và lọc chi nhánh theo Từ khóa (`SearchTerm`), Tỉnh/Thành phố (`City`), Quận/Huyện (`District`).
   - Phân trang chuẩn mực với `PagedResult<FacilitySummaryDto>`, giữ nguyên toàn bộ bộ lọc trong URL query string khi chuyển trang (`GetPaginationUrl(int page)`).
   - Tích hợp partial `_EmptyState` khi không tìm thấy cơ sở phù hợp, xử lý lỗi hệ thống/validation thân thiện.
   - Thẻ hiển thị cơ sở trực quan: Tên cơ sở, mã chi nhánh, địa chỉ đầy đủ, giờ tiếp khách theo giờ Việt Nam (`Asia/Ho_Chi_Minh`), liên kết mở vị trí Google Maps, và nút "Xem chi tiết & Sơ đồ kho".

2. **Trang Chi Tiết Cơ Sở Kho Đa Thẻ (`/Catalog/Details`)**:
   - Xây dựng Razor Page `Pages/Catalog/Details.cshtml` và PageModel `Details.cshtml.cs` với 4 thẻ chức năng chuyên sâu:
     - **Thẻ 1: Loại Kho & Bảng Giá**:
       - Kích thước chi tiết (Rộng &times; Dài &times; Cao), diện tích ($m^2$), thể tích ($m^3$), tải trọng tối đa ($kg$), tính năng kiểm soát nhiệt độ (Có điều hòa / Không điều hòa).
       - Bảng giá niêm yết theo đơn vị VND chuẩn `vi-VN`: Giá thuê theo tháng, Tiền đặt cọc, Phí giữ chỗ, khoảng thời gian hiệu lực giá.
       - Sức chứa minh bạch: Hiển thị tổng số ô đã niêm yết (`TotalListedUnits`) và số ô khả dụng ước tính (`EstimatedAvailableUnits`). Kèm theo ghi chú minh bạch theo quy chuẩn **BR-RSV-03**.
     - **Thẻ 2: Sơ Đồ Mặt Bằng Ô Kho (Vector SVG Tương Tác)**:
       - Vẽ sơ đồ mặt bằng trực quan hoàn toàn bằng chuẩn đồ họa vector SVG co giãn (`viewBox`), tự động tính toán khung giới hạn (Bounding Box) từ dữ liệu tọa độ lưu trữ thực tế (`unit_map_position`).
       - Tuyệt đối **không bịa tọa độ hoặc dùng dữ liệu demo**. Các ô chưa có tọa độ được đánh dấu rõ ràng và không hiển thị giả lập trên bản đồ.
       - Mã hóa màu sắc trực quan theo trạng thái hiển thị (`available` - xanh lá, `occupied` - đỏ, `reserved` - vàng cam, `maintenance` - xám tro, `unavailable` - xám đen).
       - Bộ lọc theo Khu vực/Tầng (`facility_area`), đếm số lượng ô đã lên sơ đồ so với tổng số ô trong khu vực.
       - Panel thông tin chi tiết ô kho tương tác khi người dùng nhấp chọn ô trên sơ đồ, hỗ trợ điều hướng thẳng tới đặt chỗ hoặc gợi ý đăng nhập.
     - **Thẻ 3: Danh Sách Ô Ứng Viên Khả Dụng**:
       - Bảng dữ liệu phân trang hiển thị danh sách các ô kho vật lý đang ở trạng thái sẵn sàng (`physical_status == "available"`).
       - Phân trang độc lập giữ nguyên trạng thái query string và giữ người dùng ở thẻ `ActiveTab=candidate-units`.
       - Nút "Xem trên sơ đồ" kích hoạt cuộn mượt và hiệu ứng nhấp nháy (pulse highlight) ô kho tương ứng trên sơ đồ SVG.
     - **Thẻ 4: Thông Tin Chi Nhánh & Vận Hành**:
       - Hồ sơ cơ sở đầy đủ: Tên, mã, địa chỉ, múi giờ (`Asia/Ho_Chi_Minh`), giờ mở/đóng cửa, tọa độ GPS thực tế.

3. **Phân Biệt Nghiêm Ngặt Sức Chứa Loại Kho (Capacity Hold) và Trạng Thái Ô Vật Lý (Physical Status)**:
   - Tuân thủ quy chuẩn nghiệp vụ **BR-RSV-03**: Khách hàng đặt chỗ theo **Loại kho (Unit Type)** chứ không đặt ô kho vật lý cụ thể tại bước Catalog/Reservation.
   - Khi có đơn đặt chỗ đang chờ gán (unassigned hold reservation):
     - Sức chứa khả dụng của Loại kho (`EstimatedAvailableUnits`) giảm đi tương ứng:
       $$\text{EstimatedAvailableUnits} = \max(0, \text{CandidateUnits} - \text{UnassignedActiveReservations})$$
     - Toàn bộ các ô kho vật lý trên sơ đồ mặt bằng và trong bảng ô ứng viên **vẫn giữ nguyên trạng thái vật lý `available`**. Ô chỉ chuyển sang trạng thái phân công/khóa khi nhân viên quầy tiếp nhận thực hiện Check-in / Assign Unit (BR-RSV-04 / BR-CHK-01).
   - Được chứng minh bằng bài kiểm thử tích hợp tự động: `CatalogDetails_DistinguishesUnitTypeCapacityHold_FromPhysicalUnitStatus`.

4. **Luồng Điều Hướng Đặt Chỗ và Bảo Vệ ReturnUrl Nội Bộ**:
   - Khi người dùng chọn "Đặt chỗ loại kho này":
     - Nếu đã đăng nhập (vai trò `storage_customer`): Điều hướng thẳng đến trang tạo đơn đặt chỗ `/Reservations?facilityId=...&unitTypeId=...&rentalStartDate=...&rentalEndDate=...`.
     - Nếu là khách chưa đăng nhập (Guest): Điều hướng đến `/Account/Login?returnUrl=...` trong đó `returnUrl` được bảo tồn an toàn đến đúng đường dẫn `/Reservations` nội bộ, chống tấn công Open Redirect.

5. **Giữ Nguyên Ranh Giới Thiết Kế & Không Gắn Realtime Trước T15**:
   - Không chỉnh sửa tập tin bố cục dùng chung `_Layout.cshtml` sau khi đã bàn giao cho Gemini A tại T04.
   - Mã nguồn JavaScript `catalog.js` và CSS `catalog.css` được đóng gói độc lập, chỉ nạp vào trang Catalog thông qua thẻ link riêng và `@section Scripts`.
   - Tuyệt đối không gắn SignalR realtime hay kết nối Hub cho đến khi triển khai Task T15.

---

## 2. Chi Tiết Xử Lý 4 Finding Từ Codex Review

### 2.1. FINDING 1 (P1): Stored XSS qua UnitCode trong SVG
- **Vị trí**: `src/SelfStorageManagementSystem.Web/Pages/Catalog/Details.cshtml` (dòng 514).
- **Nguyên nhân gốc (Root Cause)**: Do trước đây gặp cảnh báo `RZ1023` từ trình phân tích cú pháp Razor khi dùng thẻ `<text>` trong SVG, mã nguồn đã sử dụng `@Html.Raw($"<text class=\"unit-label\" ...>{unit.UnitCode}</text>")`. Việc đưa trực tiếp biến chuỗi `unit.UnitCode` từ cơ sở dữ liệu vào hàm `Html.Raw()` làm vô hiệu hóa cơ chế tự động encode của Razor, dẫn đến lỗ hổng Stored XSS nếu mã ô kho chứa payload độc hại như `"><script>alert(1)</script><text>`.
- **Giải pháp xử lý (Fix)**:
  - Thay thế toàn bộ việc tạo chuỗi qua `Html.Raw` bằng cú pháp markup chuẩn của Razor:
    ```html
    @:<text class="unit-label" x="@cx.ToString("F2", CultureInfo.InvariantCulture)" y="@cy.ToString("F2", CultureInfo.InvariantCulture)" text-anchor="middle" dominant-baseline="central" font-size="@fontSize.ToString("F1", CultureInfo.InvariantCulture)" font-weight="600" @Html.Raw(transform)>@unit.UnitCode</text>
    ```
  - Biểu thức `@unit.UnitCode` được Razor tự động HTML-encode an toàn (ví dụ: `<script>` được chuyển hóa thành `&lt;script&gt;`).
  - `@Html.Raw(transform)` chỉ được sử dụng cho thuộc tính xoay tọa độ và được cấu thành 100% từ các giá trị số thực nghiệm ngặt (`unit.RotationDegrees`, `cx`, `cy`), tuyệt đối không ghép dữ liệu chuỗi người dùng vào đó.
  - Toàn bộ tham số số học được định dạng bằng `CultureInfo.InvariantCulture` để chống lỗi dấu phẩy thập phân theo locale.
- **Kiểm thử kiểm chứng**: `CatalogDetails_HostileUnitCode_IsHtmlEncodedInSvg` (**PASS**).

### 2.2. FINDING 2 (P1): Stored XSS qua unitTypeName trong catalog.js
- **Vị trí**: `src/SelfStorageManagementSystem.Web/wwwroot/js/catalog.js` (dòng 120-127).
- **Nguyên nhân gốc (Root Cause)**: Đoạn mã cập nhật nút đặt chỗ khi chọn ô trên sơ đồ SVG đã sử dụng phương thức `$actionBtnContainer.html(...)` ghép chuỗi HTML có chứa biến `unitTypeName`:
  `... Đặt chỗ loại kho "' + unitTypeName + '" &rarr; ...`. Khi `unitTypeName` chứa payload như `"><img src=x onerror=alert(1)>`, hàm `.html()` của jQuery phân tích chuỗi thành các phần tử DOM sống và thực thi mã độc.
- **Giải pháp xử lý (Fix)**:
  - Loại bỏ hoàn toàn phương thức `.html()` khỏi toàn bộ tệp tin `catalog.js`.
  - Khởi tạo phần tử DOM an toàn bằng API jQuery/document:
    ```javascript
    $actionBtnContainer.empty();
    const numUnitTypeId = parseInt(unitTypeId, 10);
    if (canProceed && !isNaN(numUnitTypeId) && numUnitTypeId > 0) {
        let targetUrl = reserveUrlTemplate.replace('__TYPE_ID__', encodeURIComponent(numUnitTypeId.toString()));
        if (!isAuthenticated && loginUrlTemplate) {
            targetUrl = loginUrlTemplate.replace('__RETURN_URL__', encodeURIComponent(targetUrl));
        }

        const $link = $('<a></a>')
            .addClass('btn btn-primary btn-sm w-100 fw-semibold')
            .attr('href', targetUrl);

        const $checkSpan = $('<span></span>')
            .addClass('me-1')
            .text('\u2713');

        $link.append($checkSpan);
        $link.append(document.createTextNode(' Đặt chỗ loại kho "' + (unitTypeName || '') + '" \u2192'));

        const $helpText = $('<div></div>')
            .addClass('form-text small mt-1 text-muted')
            .text('Theo quy tắc BR-RSV-03: Đơn đặt chỗ giữ theo Loại kho; ô vật lý sẽ được nhân viên bàn giao tại quầy tiếp nhận.');

        $actionBtnContainer.append($link).append($helpText);
    }
    ```
  - `targetUrl` chỉ được chèn giá trị `unitTypeId` sau khi đã kiểm tra `parseInt` và `encodeURIComponent`.
  - Thuộc tính `href` được gán bằng API `.attr('href', targetUrl)`.
  - Chuỗi `unitTypeName` được gán thuần túy dưới dạng Text Node qua `document.createTextNode(...)`, ngăn chặn triệt để mọi nguy cơ thực thi mã độc.
  - Quét sạch toàn bộ `catalog.js`, xác nhận **không còn bất kỳ sink `.html()`, `innerHTML` hay `insertAdjacentHTML` nào**.
- **Kiểm thử kiểm chứng**: `CatalogFloorMap_HostileUnitTypeName_DoesNotCreateExecutableMarkup` (**PASS**).

### 2.3. FINDING 3 (P2): Link Reservation dùng ngày chưa chuẩn hóa
- **Vị trí**: `src/SelfStorageManagementSystem.Web/Pages/Catalog/Details.cshtml.cs` (dòng 126-150 và 254-265).
- **Nguyên nhân gốc (Root Cause)**: Khi người dùng truyền ngày bắt đầu trong quá khứ (`RentalStartDate < today`), ngày kết thúc nhỏ hơn ngày bắt đầu, hoặc thời hạn thuê vi phạm quy chuẩn BR-RSV-02 (dưới 1 tháng hoặc trên 12 tháng), PageModel đã tính toán ra `effectiveStartDate` và `effectiveEndDate` cho các truy vấn backend service, nhưng không cập nhật ngược lại vào thuộc tính `RentalStartDate` và `RentalEndDate` của model. Do đó, hàm `GetReservationUrl` và `BuildBaseQueryParams` vẫn sử dụng cặp ngày không hợp lệ ban đầu để tạo link đặt chỗ và link phân trang.
- **Giải pháp xử lý (Fix)**:
  - Ngay sau khối kiểm tra hợp lệ, đồng bộ hóa triệt để giá trị đã chuẩn hóa vào thuộc tính model:
    ```csharp
    RentalStartDate = effectiveStartDate;
    RentalEndDate = effectiveEndDate;
    ```
  - Kết quả: Toàn bộ date picker trên UI, các câu truy vấn sức chứa, các URL phân trang ô ứng viên (`GetCandidateUnitsPaginationUrl`), và đường dẫn đặt chỗ (`GetReservationUrl`) đều sử dụng cùng một cặp ngày hợp lệ đã chuẩn hóa.
  - Ngăn chặn hoàn toàn tình trạng link đặt chỗ mang ngày quá khứ hoặc thời hạn vi phạm quy tắc nghiệp vụ.
- **Kiểm thử kiểm chứng**:
  - `CatalogDetails_PastStartDate_ReservationUrlUsesNormalizedDates` (**PASS**)
  - `CatalogDetails_InvalidDuration_ReservationUrlUsesNormalizedDates` (**PASS**)

### 2.4. FINDING 4 (P2): Missing facility trả HTTP 200
- **Vị trí**:
  - `src/SelfStorageManagementSystem.Web/Pages/Catalog/Details.cshtml.cs`
  - `tests/SelfStorageManagementSystem.WebTests/CatalogRazorPagesTests.cs`
- **Nguyên nhân gốc (Root Cause)**: Khi `facilityId` bị thiếu, nhỏ hơn hoặc bằng 0, hoặc cơ sở không tồn tại / ngừng hoạt động (`NotFoundException`), PageModel trả về `Page()` mà không thiết lập mã trạng thái HTTP 404, khiến ASP.NET Core mặc định trả về HTTP 200 OK mặc dù giao diện hiển thị thông báo lỗi. Ngoài ra, khi thiết lập `Response.StatusCode = 404`, middleware `app.UseStatusCodePagesWithReExecute("/Home/StatusCode/{0}")` sẽ tự động chuyển hướng và ghi đè trang lỗi thân thiện của Catalog bằng trang 404 chung của hệ thống.
- **Giải pháp xử lý (Fix)**:
  - Bổ sung hàm trợ năng `SetNotFoundAndReturnPage(message)`:
    ```csharp
    private IActionResult SetNotFoundAndReturnPage(string message)
    {
        var statusCodePagesFeature = HttpContext.Features.Get<Microsoft.AspNetCore.Diagnostics.IStatusCodePagesFeature>();
        if (statusCodePagesFeature != null)
        {
            statusCodePagesFeature.Enabled = false;
        }

        Response.StatusCode = StatusCodes.Status404NotFound;
        FacilityNotFound = true;
        ErrorMessage = message;
        return Page();
    }
    ```
  - Bằng cách đặt `statusCodePagesFeature.Enabled = false`, Razor Page vừa giữ nguyên mã phản hồi chuẩn RESTful là HTTP 404 NotFound, vừa kết xuất thành công giao diện rỗng thân thiện (`_EmptyState`) của Catalog kèm nút điều hướng quay lại danh mục kho.
  - Áp dụng đồng nhất cho cả 3 trường hợp: (1) `facilityId` không được truyền hoặc `<= 0`, (2) Service ném `NotFoundException`, (3) Cơ sở ở trạng thái không hoạt động (`status != "active"`).
  - Cập nhật bài kiểm thử `CatalogDetails_NonExistentFacility_Returns404AndFriendlyBody` và bổ sung `CatalogDetails_MissingFacilityId_Returns404AndFriendlyBody` để xác thực `HttpStatusCode.NotFound` thay vì `HttpStatusCode.OK`.
- **Kiểm thử kiểm chứng**:
  - `CatalogDetails_NonExistentFacility_Returns404AndFriendlyBody` (**PASS**)
  - `CatalogDetails_MissingFacilityId_Returns404AndFriendlyBody` (**PASS**)

---

## 3. Danh Sách Tập Tin Đã Tạo và Chỉnh Sửa

### 3.1. Tập tin tạo mới (New Files)
| STT | Đường dẫn tập tin | Mục đích & Trách nhiệm |
| :---: | :--- | :--- |
| 1 | `src/SelfStorageManagementSystem.Web/Pages/Catalog/Index.cshtml.cs` | PageModel quản lý tìm kiếm, lọc địa giới hành chính và phân trang danh mục cơ sở. |
| 2 | `src/SelfStorageManagementSystem.Web/Pages/Catalog/Index.cshtml` | Giao diện Razor Page hiển thị danh mục cơ sở, bộ lọc và các thẻ chi nhánh. |
| 3 | `src/SelfStorageManagementSystem.Web/Pages/Catalog/Details.cshtml.cs` | PageModel quản lý 4 tab: Loại kho, Bảng giá, Sơ đồ SVG, Ô ứng viên và Thông tin cơ sở. Đã chuẩn hóa ngày và trả mã HTTP 404 chuẩn. |
| 4 | `src/SelfStorageManagementSystem.Web/Pages/Catalog/Details.cshtml` | Giao diện chi tiết cơ sở kho, sơ đồ vector SVG tương tác. Đã khắc phục Stored XSS qua Razor markup. |
| 5 | `src/SelfStorageManagementSystem.Web/wwwroot/css/catalog.css` | Phong cách giao diện riêng cho Catalog: Filter cards, responsive layout, SVG unit rect styling. |
| 6 | `src/SelfStorageManagementSystem.Web/wwwroot/js/catalog.js` | Logic client tương tác sơ đồ SVG. Đã loại bỏ hoàn toàn `.html()` sinks, chuyển sang DOM element an toàn. |
| 7 | `tests/SelfStorageManagementSystem.WebTests/CatalogRazorPagesTests.cs` | Bộ 16 kiểm thử tích hợp tự động kiểm chứng toàn bộ tính năng và 4 findings bảo mật/chuẩn hóa. |

### 3.2. Tập tin chỉnh sửa (Modified Files)
| STT | Đường dẫn tập tin | Nội dung chỉnh sửa |
| :---: | :--- | :--- |
| 1 | `src/SelfStorageManagementSystem.Web/Helpers/AppFormatters.cs` | Bổ sung nạp chồng hàm `FormatTime(TimeOnly? time)` hỗ trợ kiểu dữ liệu thời gian của entity `facility`. |
| 2 | `tests/SelfStorageManagementSystem.Tests/AccountIdentityServiceTests.cs` | Bổ sung `EmploymentStatus = EmploymentStatusConstants.Active` vào snapshot mock để đảm bảo 100% baseline regression pass. |

---

## 4. Kết Quả Kiểm Thử Tự Động (Automated Test Execution)

### 4.1. Bộ Kiểm Thử Tích Hợp Catalog (`CatalogRazorPagesTests.cs`)
Chạy lệnh `dotnet test --filter "FullyQualifiedName~CatalogRazorPagesTests"`:

| STT | Tên Kiểm Thử (Test Method) | Mục Đích & Điều Kiện Kiểm Chứng | Kết Quả |
| :---: | :--- | :--- | :---: |
| 1 | `CatalogIndex_PublicAccess_ReturnsOk_WithFacilityListAndFilters` | Kiểm tra tải trang danh mục cơ sở `/Catalog`, breadcrumbs và hiển thị đúng thẻ chi nhánh từ service thật. | **PASS** |
| 2 | `CatalogIndex_PreservesQueryFilters_AndGeneratesPaginationUrls` | Kiểm tra form giữ nguyên tham số lọc `City`, `District`, `SearchTerm` và phân trang trong query string. | **PASS** |
| 3 | `CatalogIndex_WhenNoFacilitiesMatch_RendersEmptyState` | Kiểm tra hiển thị thành phần `_EmptyState` khi tìm kiếm với từ khóa không khớp cơ sở nào. | **PASS** |
| 4 | `CatalogDetails_NonExistentFacility_Returns404AndFriendlyBody` | **Finding 4 Fix (P2)**: Kiểm tra trả về HTTP 404 NotFound kèm giao diện thân thiện khi tra cứu ID không tồn tại. | **PASS** |
| 5 | `CatalogDetails_MissingFacilityId_Returns404AndFriendlyBody` | **Finding 4 Fix (P2)**: Kiểm tra trả về HTTP 404 NotFound kèm giao diện thân thiện khi thiếu tham số `facilityId`. | **PASS** |
| 6 | `CatalogDetails_RendersFacilityInfo_UnitTypes_PricingAndCapacityNotes` | Kiểm tra hiển thị đầy đủ thông số kích thước, giá thuê tháng VND, tiền cọc, phí giữ chỗ và sức chứa. | **PASS** |
| 7 | `CatalogDetails_DistinguishesUnitTypeCapacityHold_FromPhysicalUnitStatus` | **BR-RSV-03 Core**: Kiểm chứng đơn giữ chỗ theo loại làm giảm sức chứa (3 &rarr; 2) nhưng không đánh dấu ô vật lý nào là reserved. | **PASS** |
| 8 | `CatalogDetails_FloorMap_UsesOnlyRealSavedCoordinates_NoFakeCoordinates` | Kiểm tra sơ đồ SVG được tạo động từ tọa độ thật `unit_map_position`, tính đúng `viewBox` và không bịa tọa độ. | **PASS** |
| 9 | `CatalogDetails_WhenFacilityHasNoUnits_RendersGracefully_WithEmptyState` | Kiểm tra xử lý mượt mà khi cơ sở chưa có ô kho nào: hiển thị `_EmptyState` phù hợp, không bị lỗi 500. | **PASS** |
| 10 | `CatalogDetails_WhenUnitTypeHasNoRate_HandlesSafePriceDisplay` | Kiểm tra hiển thị giá an toàn (`Chưa cập nhật giá`) khi loại kho chưa có bảng giá cấu hình, không crash trang. | **PASS** |
| 11 | `CatalogDetails_ReservationLink_ForGuest_PreservesInternalReturnUrl` | Kiểm tra nút đặt chỗ cho khách chưa đăng nhập điều hướng sang `/Account/Login` có chứa `returnUrl` an toàn. | **PASS** |
| 12 | `CatalogDetails_ReservationLink_ForCustomer_NavigatesDirectlyToReservation` | Kiểm tra khách hàng đã đăng nhập được dẫn trực tiếp đến `/Reservations` kèm đủ tham số cơ sở và loại kho. | **PASS** |
| 13 | `CatalogDetails_HostileUnitCode_IsHtmlEncodedInSvg` | **Finding 1 Fix (P1)**: Kiểm chứng ô kho có `unit_code` chứa payload XSS được Razor tự động encode, không xuất script thô vào SVG. | **PASS** |
| 14 | `CatalogFloorMap_HostileUnitTypeName_DoesNotCreateExecutableMarkup` | **Finding 2 Fix (P1)**: Kiểm chứng `unitTypeName` độc hại không bị thực thi và `catalog.js` không có sink `.html()`. | **PASS** |
| 15 | `CatalogDetails_PastStartDate_ReservationUrlUsesNormalizedDates` | **Finding 3 Fix (P2)**: Kiểm chứng ngày bắt đầu quá khứ được chuẩn hóa sang ngày hiện tại trên cả UI, query và link đặt chỗ. | **PASS** |
| 16 | `CatalogDetails_InvalidDuration_ReservationUrlUsesNormalizedDates` | **Finding 3 Fix (P2)**: Kiểm chứng thời hạn thuê vi phạm BR-RSV-02 được điều chỉnh chuẩn hóa sang tối thiểu 1 tháng. | **PASS** |

**Tổng kết bộ kiểm thử Catalog**: `Passed: 16, Failed: 0, Skipped: 0` (Thời gian chạy: 1.4s).

### 4.2. Toàn Bộ Solution Web (`SelfStorageManagementSystem.Web.sln`)
Chạy lệnh `dotnet test .\SelfStorageManagementSystem.Web.sln`:
- **Kết quả**: `Passed: 159, Failed: 0, Skipped: 0, Total: 159` (100% PASS).

### 4.3. Kiểm Thử Hồi Quy Toàn Hệ Thống (`SelfStorageManagementSystem.sln` / `SelfStorageManagementSystem.Tests.csproj`)
Chạy lệnh `dotnet test .\SelfStorageManagementSystem.sln`:
- **Kết quả**: `Passed: 208, Failed: 0, Skipped: 8, Total: 216` (100% PASS trên toàn bộ các bài test non-skipped; 8 bài skipped do yêu cầu SQL Server cục bộ).

---

## 5. Kết Luận Nghiệm Thu (Handover Conclusion)

1. **Toàn bộ 4 finding từ Codex Review đã được giải quyết triệt để tại mã nguồn**:
   - Loại bỏ hoàn toàn lỗ hổng Stored XSS trong SVG text element (Finding 1) và client-side script DOM manipulation (Finding 2).
   - Đồng bộ hóa cặp ngày thuê đã chuẩn hóa trên toàn bộ UI, query, phân trang và link đặt chỗ (Finding 3).
   - Phản hồi đúng mã trạng thái HTTP 404 NotFound cho các trường hợp thiếu/sai cơ sở trong khi vẫn bảo tồn giao diện thân thiện (Finding 4).
2. **Hệ thống kiểm thử đạt 100%**:
   - 16/16 test Catalog đạt yêu cầu.
   - 159/159 test toàn bộ Web solution đạt yêu cầu.
   - 208/216 test hồi quy đạt yêu cầu (208 passed, 0 failed, 8 skipped do yêu cầu SQL Server cục bộ).
3. **Task T07 đạt đầy đủ điều kiện nghiệm thu**: **VERIFIED**.

---
**Ký nhận bàn giao:**
*Gemini C (Catalog, Floor Map & Check-in UI Lead)* — **Task T07 VERIFIED**

