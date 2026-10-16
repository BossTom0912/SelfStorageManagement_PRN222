# Hệ Thống Hợp Đồng Kỹ Thuật Chuyển Đổi Web (Web Migration Contracts)

- **Phiên bản Hợp đồng**: **v1 (Foundation Integration & Feature Handover Freeze)**
- **Ngày cập nhật**: 2026-10-16
- **Task cập nhật**: T08 (Tích hợp nền và kiểm tra điểm bàn giao)
- **Chủ sở hữu hợp đồng**: Gemini A (Foundation & Integration Lead)
- **Đối tượng áp dụng**: Gemini A, Gemini B, Gemini C, Codex Reviewer
- **Trạng thái tích hợp nền**: **IMPLEMENTED (Chờ Reviewer nghiệm thu)**

---

## 1. Kiến Trúc Tổng Thể & Giải Pháp (Solution Architecture)

### 1.1. Cấu trúc Solution và Projects
Hệ thống web độc lập được tổ chức trong solution `SelfStorageManagementSystem.Web.sln` bao gồm 4 project chính:

```text
SelfStorageManagementSystem.Web.sln
├── src/SelfStorageManagementSystem.Web            (MvcWebApp: ASP.NET Core MVC, Razor Pages, SignalR Hub)
├── src/SelfStorageManagementSystem.BusinessLogic  (BLL: Domain Services, DTOs, Security, Composition Entrypoint)
├── src/SelfStorageManagementSystem.DataAccess    (DAL: EF Core DbContext, Repositories, SQL Server Mapping)
└── tests/SelfStorageManagementSystem.WebTests     (Web Host Fixtures, Integration Tests, Security Tests)
```

> **Nguyên tắc bảo tồn giải pháp cũ**: Solution `SelfStorageManagementSystem.sln` (gồm API Presentation, WpfClient, BusinessLogic, DataAccess, Tests) được giữ nguyên vẹn 100% trong workspace bản sao để kiểm thử hồi quy (regression testing).

### 1.2. Phân Tầng Phụ Thuộc (Dependency Rule)
- **`SelfStorageManagementSystem.Web`** CHỈ reference trực tiếp **`SelfStorageManagementSystem.BusinessLogic`**.
- Web tuyệt đối **KHÔNG** reference `SelfStorageManagementSystem.DataAccess`, `SelfStorageManagementSystem.Presentation` (Web API), hoặc `SelfStorageManagementSystem.WpfClient`.
- Web tuyệt đối **KHÔNG** gọi HTTP vòng qua Web API. Toàn bộ nghiệp vụ được thực thi trực tiếp qua các Service Interface của BLL.
- MVC Controller, Razor PageModel, và SignalR Hub tuyệt đối không inject `DbContext` hoặc `IRepository<T>`.
- Fixture của `WebTests` được phép cấu hình DAL trong môi trường kiểm thử (In-Memory hoặc SQL test fixture), nhưng đường dẫn runtime của Web app tuyệt đối tuân thủ 3 tầng:
  $$\text{MvcWebApp} \longrightarrow \text{BusinessLogic} \longrightarrow \text{DataAccess} \longrightarrow \text{SQL Server}$$

---

## 2. Điểm Khởi Tạo Thành Phần (Composition & DI Contract)

### 2.1. Composition Entrypoint trong BLL
Để đảm bảo host Web không cần reference DataAccess, BusinessLogic cung cấp extension method hợp thành tại namespace `SelfStorageManagementSystem.BusinessLogic.DependencyInjection`:

```csharp
public static class ServiceCollectionExtensions
{
    // Hợp thành toàn bộ DAL và BLL Core cho ứng dụng Web
    public static IServiceCollection AddSelfStorageWebComposition(
        this IServiceCollection services,
        IConfiguration configuration);

    // Đăng ký các dịch vụ nghiệp vụ lõi (không phụ thuộc JWT)
    public static IServiceCollection AddBusinessLogicCore(
        this IServiceCollection services);

    // Đăng ký dịch vụ cấp phát token JWT (chỉ dùng cho API cũ)
    public static IServiceCollection AddBusinessLogicJwt(
        this IServiceCollection services);

    // Entrypoint tương thích ngược cho API và bộ test cũ
    public static IServiceCollection AddBusinessLogic(
        this IServiceCollection services);
}
```

### 2.2. Tách Biệt Xác Thực Core và JWT (Zero JWT Secret on Web Host)
- Host Web mới hoạt động dựa trên Cookie Authentication, không cấp phát token JWT.
- Khi gọi `builder.Services.AddSelfStorageWebComposition(builder.Configuration)`, BLL chỉ đăng ký `AddBusinessLogicCore()` và DAL.
- `IJwtTokenGenerator` và `JwtOptions` tuyệt đối không được đăng ký vào DI của Web host.
- **Tiêu chuẩn xác minh**: Host Web khởi động thành công độc lập khi hoàn toàn không có khóa cấu hình `Jwt:Key` hoặc biến môi trường `JWT_SECRET_KEY`.

### 2.3. Bảng Phân Bổ Vòng Đời Dịch Vụ (DI Lifetime Matrix)

| Dịch Vụ / Thành Phần | Interface / Lớp | Vòng Đời (Lifetime) | Lý Do Kiến Trúc |
| :--- | :--- | :--- | :--- |
| **TimeProvider** | `TimeProvider.System` | `Singleton` | Không trạng thái, cung cấp mốc thời gian chuẩn hóa |
| **PasswordHasher** | `IPasswordHasher` | `Singleton` | Không trạng thái, mã hóa mật khẩu PBKDF2/Argon2 |
| **DbContext** | `SelfStorageDbContext` | `Scoped` | Quản lý phiên truy cập CSDL và theo dõi thực thể EF Core |
| **Generic Repository** | `IRepository<T>` | `Scoped` | Phụ thuộc vào DbContext scoped |
| **User Repository** | `IUserRepository` | `Scoped` | Thực thi truy vấn snapshot và thông tin định danh |
| **Catalog Repository** | `IFacilityCatalogRepository` | `Scoped` | Truy vấn cơ sở, loại kho và sơ đồ mặt bằng |
| **Reservation Repository** | `IReservationRepository` | `Scoped` | Quản lý khóa giữ chỗ và đặt phòng |
| **Payment Repository** | `IPaymentRepository` | `Scoped` | Quản lý thanh toán, hóa đơn và hoàn tiền |
| **CheckIn Repository** | `ICheckInRepository` | `Scoped` | Quản lý thủ tục tiếp nhận và cấp phát mã PIN |
| **Storage Unit Admin Repo** | `IStorageUnitAdminRepository` | `Scoped` | Quản trị ô kho CRUD và concurrency lock |
| **Domain Services BLL** | `IAccountIdentityService`, `IStorageUnitAdminService`, `IFacilityCatalogService`, `IReservationService`, `IPaymentService`, `ICheckInService`, `IAdminAccountService`, `IRentalAgreementService`, `IInvoiceService`, `IFacilityScopeService`, `IAuditService` | `Scoped` | Đảm bảo tính nhất quán trong cùng 1 HTTP Request hoặc SignalR Invocation |
| **Payment Gateways** | `DemoGateway`, `VnpayGateway` | `Scoped` | Tích hợp xử lý giao dịch thanh toán |

---

## 3. Quy Chuẩn Công Cụ & Thư Viện Cục Bộ (Exact Versions & Local Assets)

Nhằm đảm bảo tính độc lập và khả năng vận hành offline trong môi trường nội bộ/phòng lab, mọi tài nguyên frontend phải được phục vụ cục bộ từ `wwwroot/lib/`, nghiêm cấm phụ thuộc CDN công cộng bên ngoài.

| Công cụ / Thư viện | Phiên bản chính xác | Vị trí asset cục bộ | Ghi chú vận hành |
| :--- | :--- | :--- | :--- |
| **.NET SDK** | `8.0.425` | `C:\Program Files\dotnet\sdk\8.0.425` | Target runtime `net8.0` |
| **ASP.NET Core** | `8.0.13` | NuGet packages (Microsoft.AspNetCore.*) | MVC, Razor Pages, SignalR |
| **Bootstrap** | `5.3.3` | `wwwroot/lib/bootstrap/dist/css/bootstrap.min.css`<br>`wwwroot/lib/bootstrap/dist/js/bootstrap.bundle.min.js` | Giao diện chuẩn UI |
| **jQuery** | `3.7.1` | `wwwroot/lib/jquery/dist/jquery.min.js` | Hỗ trợ DOM và AJAX |
| **jQuery Validation** | `1.20.0` | `wwwroot/lib/jquery-validation/dist/jquery.validate.min.js` | Client-side validation |
| **jQuery Unobtrusive** | `4.0.0` | `wwwroot/lib/jquery-validation-unobtrusive/jquery.validate.unobtrusive.min.js` | Tích hợp ASP.NET TagHelpers |
| **SignalR JS Client** | `8.0.7` | `wwwroot/lib/microsoft/signalr/dist/browser/signalr.min.js` | `@microsoft/signalr` browser build |

---

## 4. Phân Công Trách Nhiệm & Quyền Sửa File (Ownership Matrix)

| Vai trò / Agent | Phạm vi Task | Danh sách Task phụ trách | Quyền sửa tập tin (File Ownership) |
| :--- | :--- | :--- | :--- |
| **Gemini A** | **Nền móng, Identity, Auth, Reservation, Payment, Tích hợp chung** | **T00, T01, T02, T05, T08**, T10, T12, T15, T16, T19, T21 | **Độc quyền sở hữu các tập tin dùng chung**: `SelfStorageManagementSystem.Web.sln`, các file `*.csproj`, `Program.cs`, `DependencyInjection/`, `appsettings*.json`, `launchSettings.json`, Shared Layout (`_Layout.cshtml`, navigation, shared header/footer sau T04), và `AgentReport/WebMigration_TaskBoard.md`. |
| **Gemini B** | **Quản trị ô kho (Storage Units), Razor CRUD, SignalR Hub, SQL Tests** | **T03, T06**, T09, T13, T17 | Quản trị Storage Unit trong DAL (`IStorageUnitAdminRepository`), BLL (`IStorageUnitAdminService`), Razor Pages (`Pages/StorageUnits/`), Hub (`StorageUnitHub.cs`), JavaScript realtime (`wwwroot/js/storage-units/`), và SQL integration tests. Gửi yêu cầu đăng ký DI/menu cho Gemini A. |
| **Gemini C** | **Giao diện dùng chung ban đầu, Catalog, Quầy Check-in, Admin/Khách, E2E** | **T04, T07**, T11, T14, T18 | Xây dựng Layout ban đầu ở T04 (sau đó bàn giao cho A); Razor Pages Catalog (`Pages/Catalog/`), Quầy Check-in (`Pages/Staff/CheckIn/`), Admin Accounts (`Pages/Admin/Accounts/`), My Units (`Pages/MyUnits/`), Notifications (`Pages/Notifications/`), và browser tests. Gửi yêu cầu đăng ký DI/menu cho Gemini A. |
| **Codex** | **Kiểm toán độc lập & Nghiệm thu** | T20, T22 | Thực hiện review độc lập sau khi hoàn tất T19; phát hiện finding theo mức độ Severity; không tự ý merge code; nghiệm thu tại T22. |

---

## 5. Quy Chuẩn Khởi Tạo Cơ Sở Dữ Liệu (Database Bootstrap Contract)

### 5.1. Danh Sách Cơ Sở Dữ Liệu Hợp Lệ (Strict Allowlist)
Chỉ cho phép tạo và thao tác trên đúng 3 cơ sở dữ liệu sau:
1. `SelfStoragePRN222_WebDemo`: Phục vụ chạy thử nghiệm thủ công trên giao diện web và kiểm thử phát triển cục bộ.
2. `SelfStoragePRN222_WebTest`: Phục vụ chạy các bài kiểm thử tích hợp SQL và kiểm thử đồng thời (concurrency tests).
3. `SelfStoragePRN222_WebE2eTest`: Phục vụ chạy các kịch bản kiểm thử tự động toàn diện qua trình duyệt (end-to-end tests).

### 5.2. Ràng Buộc An Toàn Tuyệt Đối (Safety Constraints)
- **Nghiêm cấm DROP DATABASE**: Trong toàn bộ các script khởi tạo web mới (`SelfStoragePRN222_Web_Bootstrap.sql`, `SelfStoragePRN222_WebDemo_Bootstrap.sql`, v.v.), tuyệt đối không chứa bất kỳ câu lệnh `DROP DATABASE` nào.
- **Dừng ngay lập tức nếu DB đã tồn tại**: Script kiểm tra `DB_ID(@targetDb)` ngay đầu batch. Nếu database đã tồn tại, script thực thi lệnh `THROW 52002` để dừng ngay lập tức, không ghi đè dữ liệu.
- **Không chứa tên DB nguồn**: Tên cơ sở dữ liệu gốc `SelfStoragePRN222` tuyệt đối không xuất hiện độc lập trong script mới để loại trừ hoàn toàn nguy cơ tác động nhầm vào database nguồn.
- **Bảo toàn lược đồ**: Toàn bộ bảng (`core.*`, `api.*`, `auth_private.*`), khóa chính, khóa ngoại, chỉ mục, ràng buộc toàn vẹn, trigger tự động và dữ liệu mẫu (seed data) được chuyển giao đầy đủ từ bản chuẩn.

---

## 6. Hợp Đồng Giao Diện Dịch Vụ Nghiệp Vụ (Service Contracts v1)

### 6.1. Xác Thực & Định Danh (IAccountIdentityService - v1 Freeze)
Interface tại `SelfStorageManagementSystem.BusinessLogic.Services.Interfaces.IAccountIdentityService`:

```csharp
namespace SelfStorageManagementSystem.BusinessLogic.Services.Interfaces;

public interface IAccountIdentityService
{
    // Đăng nhập qua email và password rõ
    Task<AuthenticatedAccountResponse> AuthenticateAsync(
        string email,
        string password,
        string? ipAddress = null,
        string? userAgent = null,
        CancellationToken cancellationToken = default);

    // Đăng nhập qua LoginRequest DTO (UserName map sang email)
    Task<AuthenticatedAccountResponse> AuthenticateAsync(
        LoginRequest request,
        string? ipAddress = null,
        string? userAgent = null,
        CancellationToken cancellationToken = default);

    // Đăng ký khách hàng mới (storage_customer)
    Task<CustomerRegisterResponse> RegisterCustomerAsync(
        CustomerRegisterRequest request,
        CancellationToken cancellationToken = default);

    // Lấy thông tin snapshot người dùng hiện hành từ DB (AsNoTracking)
    Task<CurrentUserResponse?> GetCurrentUserAsync(
        long userId,
        CancellationToken cancellationToken = default);
}
```

**Mô hình CurrentUserResponse v1**:
```csharp
public class CurrentUserResponse
{
    public long UserId { get; set; }
    public string DisplayName { get; set; } = string.Empty;
    public string Email { get; set; } = string.Empty;
    public string? PhoneNumber { get; set; }
    public string Status { get; set; } = string.Empty;
    public List<string> Roles { get; set; } = new();
    public string? UserType { get; set; } // "customer", "employee", "admin"
    public string? EmployeeCode { get; set; }
    public string? EmploymentStatus { get; set; } // "active", "leave", "terminated", "suspended"
    public List<FacilityAssignmentDto> ActiveAssignments { get; set; } = new();
}
```

*Quy tắc v1*:
1. Nhân sự có `employment_status != "active"` bị từ chối đăng nhập và tước toàn bộ quyền hạn ngay lập tức trong `GetCurrentUserAsync` (trả về `null`).
2. Khách hàng (`customer_profile`) không có hồ sơ nhân sự thì `EmploymentStatus == null` và được phép duy trì phiên bình thường.

### 6.2. Quản Trị Ô Kho (IStorageUnitAdminService - v1 Freeze)
Interface tại `SelfStorageManagementSystem.BusinessLogic.Services.Interfaces.IStorageUnitAdminService`:

```csharp
namespace SelfStorageManagementSystem.BusinessLogic.Services.Interfaces;

public interface IStorageUnitAdminService
{
    Task<PagedResult<StorageUnitAdminDto>> ListAsync(
        StorageUnitFilterRequest request,
        long actorUserId,
        CancellationToken cancellationToken = default);

    Task<StorageUnitAdminDetailDto?> GetByIdAsync(
        long unitId,
        long actorUserId,
        CancellationToken cancellationToken = default);

    Task<StorageUnitLookupsDto> GetLookupsAsync(
        long actorUserId,
        long? facilityId = null,
        CancellationToken cancellationToken = default);

    Task<StorageUnitAdminDto> CreateAsync(
        CreateStorageUnitRequest request,
        long actorUserId,
        CancellationToken cancellationToken = default);

    Task<StorageUnitAdminDto> UpdateAsync(
        long unitId,
        UpdateStorageUnitRequest request,
        long actorUserId,
        CancellationToken cancellationToken = default);

    Task<DeleteStorageUnitResult> DeleteAsync(
        long unitId,
        DateTimeOffset expectedUpdatedAt,
        long actorUserId,
        CancellationToken cancellationToken = default);
}
```

*Quy tắc v1*:
1. **Khoảng hiệu lực biểu giá**: Tuân thủ khoảng nửa mở `[valid_from, valid_to)`. Khi `valid_to == localToday` thì giá được tính là hết hạn.
2. **Không fallback unvalidated rate**: Nếu không tìm thấy biểu giá thỏa mãn cả 4 điều kiện (`facility_id`, `unit_type_id`, `valid_from <= today`, `valid_to == null || today < valid_to`), trả về `MonthlyRate = 0m` (Detail) hoặc `null` (Lookups toàn cục).
3. **Múi giờ**: Sử dụng `TimeProvider` inject kết hợp với múi giờ cơ sở (`facility.timezone`).
4. **Phân quyền cơ sở**: Nhân viên và quản lý chỉ được thao tác trên ô kho thuộc cơ sở mình được phân công (`ActiveAssignments`).

### 6.3. Danh Mục Kho & Sơ Đồ (IFacilityCatalogService - v1 Freeze)
Interface tại `SelfStorageManagementSystem.BusinessLogic.Services.Interfaces.IFacilityCatalogService`:

```csharp
public interface IFacilityCatalogService
{
    Task<PagedResult<FacilityCatalogDto>> GetFacilitiesAsync(
        GetFacilitiesCatalogRequest request,
        CancellationToken cancellationToken = default);

    Task<FacilityCatalogDto> GetFacilityByIdAsync(
        long facilityId,
        CancellationToken cancellationToken = default);

    Task<List<FacilityUnitTypeCatalogDto>> GetFacilityUnitTypesAsync(
        long facilityId,
        GetFacilityUnitTypesRequest request,
        CancellationToken cancellationToken = default);

    Task<PagedResult<AvailableStorageUnitDto>> GetAvailableUnitsAsync(
        long facilityId,
        GetAvailableUnitsRequest request,
        CancellationToken cancellationToken = default);

    Task<FacilityFloorMapDto> GetFacilityFloorMapAsync(
        long facilityId,
        GetFacilityFloorMapRequest request,
        CancellationToken cancellationToken = default);
}
```

*Quy tắc v1*:
1. **Quy chuẩn BR-RSV-03**: Phân biệt triệt để Sức chứa khả dụng theo loại kho (`EstimatedAvailableUnits`) và Trạng thái ô vật lý (`physical_status`). Khi có đơn giữ chỗ chờ gán, sức chứa khả dụng giảm nhưng ô kho vật lý vẫn giữ trạng thái `available` cho đến khi check-in gán ô thực tế.
2. **Bảo mật SVG & DOM**: Mã ô kho (`UnitCode`) và tên loại kho (`TypeName`) khi render lên SVG hoặc chèn vào DOM bắt buộc phải được mã hóa HTML, loại trừ nguy cơ Stored XSS.
3. **Mã HTTP**: Truy cập cơ sở không tồn tại trả về `404 Not Found`.

### 6.4. Đặt Chỗ Kho (IReservationService - v1 Freeze)
Interface tại `SelfStorageManagementSystem.BusinessLogic.Services.Interfaces.IReservationService`:

```csharp
public interface IReservationService
{
    Task<ReservationDetailResponse> CreateReservationHoldAsync(
        long currentUserId,
        CreateReservationRequest request,
        CancellationToken cancellationToken = default);

    Task<ReservationDetailResponse> GetReservationByIdAsync(
        long currentUserId,
        IReadOnlyList<string> currentUserRoles,
        long reservationId,
        CancellationToken cancellationToken = default);

    Task<PagedResult<ReservationListItemResponse>> GetMyReservationsAsync(
        long currentUserId,
        GetMyReservationsRequest request,
        CancellationToken cancellationToken = default);

    Task<ReservationDetailResponse> CancelReservationAsync(
        long currentUserId,
        IReadOnlyList<string> currentUserRoles,
        long reservationId,
        CancelReservationRequest? request,
        CancellationToken cancellationToken = default);

    Task<int> ExpireOverdueHoldsAsync(
        CancellationToken cancellationToken = default);
}
```

*Quy tắc v1*:
1. Thời hạn giữ chỗ: Đúng 15 phút kể từ thời điểm tạo đơn.
2. Đếm ngược realtime: Hỗ trợ countdown và background worker tự động giải phóng sức chứa khi quá hạn (`ExpireOverdueHoldsAsync`).

### 6.5. Thanh Toán & Cổng Thanh Toán (IPaymentService - v1 Freeze)
Interface tại `SelfStorageManagementSystem.BusinessLogic.Services.Interfaces.IPaymentService`:

```csharp
public interface IPaymentService
{
    Task<CheckoutQuoteResponse> GetCheckoutQuoteAsync(
        long currentUserId,
        IReadOnlyList<string> roles,
        long reservationId,
        string? promotionCode,
        CancellationToken cancellationToken = default);

    Task<CheckoutResponse> CheckoutAsync(
        long currentUserId,
        IReadOnlyList<string> roles,
        CheckoutRequest request,
        string? idempotencyKeyHeader,
        string? clientIp,
        CancellationToken cancellationToken = default);

    Task<PaymentGatewayCallbackResult> ProcessVnpayIpnAsync(
        IDictionary<string, string> queryParams,
        CancellationToken cancellationToken = default);

    Task<PaymentDetailResponse> ProcessDemoCompleteAsync(
        long currentUserId,
        IReadOnlyList<string> roles,
        long paymentId,
        DemoPaymentCompleteRequest request,
        CancellationToken cancellationToken = default);

    Task<PaymentDetailResponse> GetPaymentByIdAsync(
        long currentUserId,
        IReadOnlyList<string> roles,
        long paymentId,
        CancellationToken cancellationToken = default);

    Task<List<PaymentDetailResponse>> GetPaymentsRequiringReconciliationAsync(
        long currentUserId,
        IReadOnlyList<string> roles,
        CancellationToken cancellationToken = default);

    Task<List<RefundDetailResponse>> GetRefundsAsync(
        long currentUserId,
        IReadOnlyList<string> roles,
        string? status,
        CancellationToken cancellationToken = default);

    Task<RefundDetailResponse> ReviewRefundAsync(
        long currentUserId,
        IReadOnlyList<string> roles,
        long refundId,
        ReviewRefundRequest request,
        CancellationToken cancellationToken = default);
}
```

### 6.6. Hóa Đơn & Hợp Đồng Thuê (IInvoiceService & IRentalAgreementService - v1 Freeze)

```csharp
public interface IInvoiceService
{
    Task<InvoiceDetailResponse> GetInvoiceByIdAsync(
        long currentUserId,
        IReadOnlyList<string> roles,
        long invoiceId,
        CancellationToken cancellationToken = default);
}

public interface IRentalAgreementService
{
    Task<RentalAgreementDetailResponse> GetAgreementByIdAsync(
        long currentUserId,
        IReadOnlyList<string> roles,
        long agreementId,
        CancellationToken cancellationToken = default);
}
```

### 6.7. Tiếp Nhận Tại Quầy & Bàn Giao (ICheckInService - v1 Freeze)
Interface tại `SelfStorageManagementSystem.BusinessLogic.Services.Interfaces.ICheckInService`:

```csharp
public interface ICheckInService
{
    // Nghiệp vụ dành cho nhân viên quầy (Staff Operations)
    Task<CheckInSearchResponse> SearchCheckInAsync(
        string code,
        long currentUserId,
        IReadOnlyList<string> roles,
        CancellationToken cancellationToken = default);

    Task<List<EligibleUnitResponse>> GetEligibleUnitsAsync(
        long reservationId,
        long currentUserId,
        IReadOnlyList<string> roles,
        CancellationToken cancellationToken = default);

    Task<IdentityVerificationResponse> VerifyIdentityAsync(
        long reservationId,
        VerifyIdentityRequest request,
        long currentUserId,
        IReadOnlyList<string> roles,
        CancellationToken cancellationToken = default);

    Task<UnitAllocationResponse> AssignUnitAsync(
        long reservationId,
        AssignUnitRequest request,
        long currentUserId,
        IReadOnlyList<string> roles,
        CancellationToken cancellationToken = default);

    Task<UnitAllocationResponse> ReleaseUnitAsync(
        long reservationId,
        ReleaseUnitRequest request,
        long currentUserId,
        IReadOnlyList<string> roles,
        CancellationToken cancellationToken = default);

    Task<InspectionResponse> PerformInspectionAsync(
        long reservationId,
        UnitInspectionRequest request,
        long currentUserId,
        IReadOnlyList<string> roles,
        CancellationToken cancellationToken = default);

    Task<HandoverCompleteResponse> CompleteCheckInAsync(
        long reservationId,
        CompleteCheckInRequest request,
        long currentUserId,
        IReadOnlyList<string> roles,
        CancellationToken cancellationToken = default);

    Task<PinReissueResponse> ReissuePinAsync(
        long reservationId,
        ReissuePinRequest request,
        long currentUserId,
        IReadOnlyList<string> roles,
        CancellationToken cancellationToken = default);

    // Nghiệp vụ dành cho khách hàng thuê kho (Customer Operations)
    Task<PagedResult<CustomerUnitListItemResponse>> GetCustomerUnitsAsync(
        long currentUserId,
        PagedRequest pagedRequest,
        CancellationToken cancellationToken = default);

    Task<CustomerHandoverResponse> GetCustomerHandoverAsync(
        long currentUserId,
        long agreementId,
        CancellationToken cancellationToken = default);

    Task<PagedResult<CustomerNotificationResponse>> GetCustomerNotificationsAsync(
        long currentUserId,
        PagedRequest pagedRequest,
        CancellationToken cancellationToken = default);
}
```

*Quy tắc v1*:
1. **Bảo mật mã PIN**: Mã PIN chỉ được hiển thị 1 lần duy nhất trên màn hình kết quả sau khi Check-in hoặc Cấp lại PIN thành công, có header `Cache-Control: no-store, no-cache`. Tuyệt đối không lưu PIN ở Cookie, LocalStorage hay SessionStorage.

### 6.8. Quản Trị Tài Khoản & Phân Quyền (IAdminAccountService - v1 Freeze)
Interface tại `SelfStorageManagementSystem.BusinessLogic.Services.Interfaces.IAdminAccountService`:

```csharp
public interface IAdminAccountService
{
    Task<PagedResult<UserAccountDto>> GetAccountsAsync(
        GetAccountsRequest request,
        CancellationToken cancellationToken = default);

    Task<UserAccountDto> GetAccountByIdAsync(
        long userId,
        CancellationToken cancellationToken = default);

    Task<UserAccountDto> CreateStaffAccountAsync(
        CreateStaffAccountRequest request,
        long actorUserId,
        string? ipAddress = null,
        string? requestId = null,
        CancellationToken cancellationToken = default);

    Task<UserAccountDto> UpdateUserStatusAsync(
        long userId,
        UpdateUserStatusRequest request,
        long actorUserId,
        string? ipAddress = null,
        string? requestId = null,
        CancellationToken cancellationToken = default);

    Task<UserAccountDto> ManageUserRolesAsync(
        long userId,
        ManageUserRolesRequest request,
        long actorUserId,
        string? ipAddress = null,
        string? requestId = null,
        CancellationToken cancellationToken = default);

    Task<FacilityAssignmentDto> AssignFacilityAsync(
        long userId,
        CreateFacilityAssignmentRequest request,
        long actorUserId,
        string? ipAddress = null,
        string? requestId = null,
        CancellationToken cancellationToken = default);

    Task TerminateFacilityAssignmentAsync(
        long assignmentId,
        long actorUserId,
        string? ipAddress = null,
        string? requestId = null,
        CancellationToken cancellationToken = default);

    Task<List<FacilityLookupDto>> GetFacilitiesAsync(
        CancellationToken cancellationToken = default);
}
```

---

## 7. Quy Hoạch Tuyến Đường HTTP & Chính Sách Phân Quyền (Routes & Policies Table v1)

### 7.1. Bảng Tuyến Đường Hệ Thống

| Phương thức | Tuyến đường (Route) | Cơ chế xử lý | Vai trò yêu cầu (Role) | Chính sách (Policy) | Trạng thái tích hợp |
| :--- | :--- | :--- | :--- | :--- | :--- |
| **GET** | `/` | MVC (`HomeController.Index`) | Công khai (Mọi đối tượng) | Không yêu cầu | **Đã tích hợp (T04/T08)** |
| **GET/POST**| `/Home/Error` | MVC (`HomeController.Error`) | Công khai (Mọi đối tượng) | Không yêu cầu | **Đã tích hợp (T04/T08)** |
| **GET** | `/Home/StatusCode/{code?}` | MVC (`HomeController.StatusCode`) | Công khai (Mọi đối tượng) | Không yêu cầu | **Đã tích hợp (T04/T08)** |
| **GET/POST**| `/Account/Login` | MVC (`AccountController.Login`) | Khách vãng lai | Không yêu cầu | **Đã tích hợp (T05/T08)** |
| **GET/POST**| `/Account/Register` | MVC (`AccountController.Register`) | Khách vãng lai | Không yêu cầu | **Đã tích hợp (T05/T08)** |
| **GET/POST**| `/Account/Logout` | MVC (`AccountController.Logout`) | Đã xác thực | `[Authorize]` | **Đã tích hợp (T05/T08)** |
| **GET** | `/Account/Forbidden` | MVC (`AccountController.Forbidden`) | Đã xác thực | `[Authorize]` | **Đã tích hợp (T05/T08)** |
| **GET** | `/Account/Profile` | MVC (`AccountController.Profile`) | Đã xác thực | `[Authorize]` | **Đã tích hợp (T05/T08)** |
| **GET** | `/Account/SecuredData` | MVC (`AccountController.SecuredData`) | Đã xác thực | `[Authorize]` | **Đã tích hợp (T05/T08)** |
| **GET** | `/Account/AdminCheck` | MVC (`AccountController.AdminCheck`) | Quản trị viên hệ thống | `[Authorize(Roles="system_administrator")]` | **Đã tích hợp (T05/T08)** |
| **GET** | `/Catalog` | Razor Page (`Pages/Catalog/Index`) | Công khai | Không yêu cầu | **Đã tích hợp (T07/T08)** |
| **GET** | `/Catalog/Details` | Razor Page (`Pages/Catalog/Details`)| Công khai | Không yêu cầu | **Đã tích hợp (T07/T08)** |
| **GET/POST**| `/StorageUnits` | Razor Page (`Pages/StorageUnits/`)| Nhân sự / Quản lý / Admin | `StaffOnly` (Policy) | Sẵn sàng mở **T09** |
| **GET/POST**| `/Reservations` | Razor Page (`Pages/Reservations/`) | Khách hàng thuê kho | `CustomerOnly` (Policy) | Sẵn sàng mở **T10** |
| **GET/POST**| `/Staff/CheckIn` | Razor Page (`Pages/Staff/CheckIn/`)| Nhân viên quầy / Quản lý | `StaffOnly` (Policy) | Sẵn sàng mở **T11** |
| **GET/POST**| `/Payments` | Razor Page (`Pages/Payments/`) | Khách hàng / Quản lý | `CustomerOnly` / `ManagerOnly` | Chờ Wave 5 (**T12**) |
| **GET** | `/Invoices` | Razor Page (`Pages/Invoices/`) | Khách hàng / Nhân viên | `[Authorize]` | Chờ Wave 5 (**T12**) |
| **GET** | `/Agreements` | Razor Page (`Pages/Agreements/`)| Khách hàng / Nhân viên | `[Authorize]` | Chờ Wave 5 (**T12**) |
| **GET/POST**| `/Admin/Accounts` | Razor Page (`Pages/Admin/Accounts/`)| Quản trị viên hệ thống | `AdminOnly` (Policy) | Chờ Wave 5 (**T14**) |
| **GET** | `/MyUnits` | Razor Page (`Pages/MyUnits/`) | Khách hàng thuê kho | `CustomerOnly` (Policy) | Chờ Wave 5 (**T14**) |
| **GET** | `/Notifications` | Razor Page (`Pages/Notifications/`)| Đã xác thực | `[Authorize]` | Chờ Wave 5 (**T14**) |
| **WS/SSE** | `/storageUnitHub` | SignalR Hub (`StorageUnitHub`) | Đã xác thực phiên Cookie | `[Authorize]` | Endpoint nền **T08**, nối dây **T13** |

### 7.2. Định Nghĩa Chính Sách Phân Quyền Chuẩn (Authorization Policies v1)

Bốn chính sách ủy quyền bảo mật được đăng ký chính thức tại `Program.cs`:

```csharp
builder.Services.AddAuthorization(options =>
{
    // Dành riêng cho khách hàng thuê kho
    options.AddPolicy("CustomerOnly", policy => policy.RequireRole(RoleConstants.StorageCustomer));

    // Dành cho nhân sự vận hành (Staff, Manager, Operations, Admin)
    options.AddPolicy("StaffOnly", policy => policy.RequireRole(
        RoleConstants.FacilityStaff,
        RoleConstants.FacilityManager,
        RoleConstants.BusinessOperationsManager,
        RoleConstants.SystemAdministrator));

    // Dành cho cấp quản lý cơ sở và cấp cao (Manager, Operations, Admin)
    options.AddPolicy("ManagerOnly", policy => policy.RequireRole(
        RoleConstants.FacilityManager,
        RoleConstants.BusinessOperationsManager,
        RoleConstants.SystemAdministrator));

    // Dành riêng cho quản trị viên hệ thống
    options.AddPolicy("AdminOnly", policy => policy.RequireRole(RoleConstants.SystemAdministrator));
});
```

### 7.3. Ma Trận Quyền Hạn Thực Tế (Policy Authorization Matrix)

| Vai trò (Role Code) | CustomerOnly | StaffOnly | ManagerOnly | AdminOnly | Phạm vi phân công (Scopes) |
| :--- | :---: | :---: | :---: | :---: | :--- |
| `storage_customer` | **THOẢ MÃN** | Bị từ chối | Bị từ chối | Bị từ chối | Toàn hệ thống (theo tài khoản sở hữu) |
| `facility_staff` | Bị từ chối | **THOẢ MÃN** | Bị từ chối | Bị từ chối | Đúng cơ sở được phân công (`ActiveAssignments`) |
| `facility_manager` | Bị từ chối | **THOẢ MÃN** | **THOẢ MÃN** | Bị từ chối | Đúng cơ sở được phân công (`ActiveAssignments`) |
| `business_operations_manager` | Bị từ chối | **THOẢ MÃN** | **THOẢ MÃN** | Bị từ chối | Toàn hệ thống |
| `system_administrator` | Bị từ chối | **THOẢ MÃN** | **THOẢ MÃN** | **THOẢ MÃN** | Toàn hệ thống |

---

## 8. Hợp Đồng Realtime SignalR (/storageUnitHub v1)

### 8.1. Cơ Chế Xác Thực & Bảo Mật Endpoint (Hub Security Enforcement)
- **Bắt buộc phiên Cookie (`[Authorize]`)**: Lớp `StorageUnitHub` được đánh dấu trực tiếp bằng thuộc tính `[Authorize]`.
- **Từ chối yêu cầu ẩn danh**: Mọi yêu cầu HTTP POST `/storageUnitHub/negotiate` không có Cookie xác thực hợp lệ sẽ bị từ chối ngay lập tức với mã trạng thái `401 Unauthorized` (nhờ cơ chế xử lý AJAX/SignalR của `CookieAuthExtensions`) hoặc chuyển hướng về `/Account/Login`.
- **Cấp phát connectionToken**: Chỉ sau khi client gửi kèm Cookie xác thực hợp lệ (`SelfStoragePRN222.Web.Auth`), server mới xác minh Principal thành công và trả về mã `200 OK` kèm `connectionToken` để nâng cấp lên kết nối WebSocket / SSE / Long Polling.

### 8.2. Client Invocations (Từ Trình Duyệt Gửi Lên Server)
- `HubCreate_StorageUnit(string dataObj)`: Nhận chuỗi JSON `CreateStorageUnitRequest`, deserialize và gọi BLL `CreateAsync(request, actorUserId)`.
- `HubUpdate_StorageUnit(string dataObj)`: Nhận chuỗi JSON `UpdateStorageUnitRequest`, kiểm tra concurrency `expectedUpdatedAt` và gọi BLL `UpdateAsync(unitId, request, actorUserId)`.
- `HubDelete_StorageUnit(string id, string? expectedUpdatedAt)`: Nhận ID ô kho và thời điểm cập nhật dự kiến để thực hiện xóa nguyên tử qua BLL `DeleteAsync`.

### 8.3. Server Broadcasts & Cấu Trúc Dữ Liệu Sự Kiện (Event Fields Specification)

Mọi broadcast từ server phát xuống trình duyệt phải tuân thủ đúng cấu trúc trường JSON sau:

#### 1. `Receiver_CreateStorageUnit(object unitSummary)`
- `id` (`long`): Khóa chính của ô kho vừa tạo.
- `unitCode` (`string`): Mã ô kho duy nhất (ví dụ: `HN1-A101`).
- `facilityId` (`long`): ID cơ sở chứa ô kho.
- `facilityCode` (`string`): Mã chi nhánh cơ sở.
- `unitTypeId` (`long`): ID loại ô kho.
- `typeName` (`string`): Tên loại ô kho.
- `areaId` (`long?`): ID khu vực / tầng (nếu có).
- `areaName` (`string?`): Tên khu vực (ví dụ: `Tầng 1 - Khu A`).
- `status` (`string`): Trạng thái ô kho (`available`, `occupied`, `maintenance`, `reserved`).
- `monthlyRate` (`decimal`): Giá thuê niêm yết theo tháng (VND).
- `isClimateControlled` (`bool`): Có điều hòa hay không.
- `updatedAt` (`string`): Mốc thời gian ISO 8601 UTC.

#### 2. `Receiver_UpdateStorageUnit(object unitSummary)`
- Toàn bộ các trường giống `Receiver_CreateStorageUnit`, phản ánh thông tin mới nhất sau khi sửa đổi.

#### 3. `Receiver_DeleteStorageUnit(string unitId)`
- `unitId` (`string`): Chuỗi biểu diễn ID ô kho vừa bị xóa khỏi hệ thống.

#### 4. `CatalogInvalidated`
- `facilityId` (`long`): ID cơ sở có sự thay đổi về sức chứa hoặc sơ đồ.
- `unitTypeId` (`long?`): ID loại kho bị ảnh hưởng (null nếu toàn bộ cơ sở bị ảnh hưởng).
- `timestampUtc` (`string`): Mốc thời gian ISO 8601 UTC.

#### 5. `ReservationChanged`
- `reservationId` (`long`): ID đơn đặt chỗ có sự thay đổi trạng thái.
- `facilityId` (`long`): ID cơ sở kho.
- `status` (`string`): Trạng thái mới (`hold`, `confirmed`, `cancelled`, `expired`).
- `timestampUtc` (`string`): Mốc thời gian ISO 8601 UTC.

#### 6. `PaymentChanged`
- `paymentId` (`long`): ID bản ghi thanh toán.
- `reservationId` (`long`): ID đơn đặt chỗ tương ứng.
- `status` (`string`): Trạng thái thanh toán (`pending`, `completed`, `failed`, `refunded`, `reconciliation_required`).
- `timestampUtc` (`string`): Mốc thời gian ISO 8601 UTC.

#### 7. `CheckInChanged`
- `checkInId` (`long`): ID phiên tiếp nhận quầy.
- `reservationId` (`long`): ID đơn đặt chỗ.
- `unitId` (`long`): ID ô kho được gán.
- `status` (`string`): Trạng thái tiếp nhận (`verified`, `allocated`, `inspected`, `completed`).
- `timestampUtc` (`string`): Mốc thời gian ISO 8601 UTC.

#### 8. `NotificationsChanged`
- `userId` (`long`): ID người dùng nhận thông báo.
- `notificationId` (`long`): ID bản ghi thông báo mới.
- `timestampUtc` (`string`): Mốc thời gian ISO 8601 UTC.

---

## 9. Ràng Buộc Bảo Mật & Quản Lý Mã PIN (Security & PIN Constraints)

1. **Phiên xác thực Cookie**:
   - `Cookie.Name = "SelfStoragePRN222.Web.Auth"`
   - `HttpOnly = true`, `SecurePolicy = SameAsRequest`, `SameSite = SameSiteMode.Lax`
   - `ExpireTimeSpan = TimeSpan.FromMinutes(5)`, `SlidingExpiration = true`
   - `LoginPath = "/Account/Login"`, `AccessDeniedPath = "/Account/Forbidden"`
   - Kiểm tra hợp lệ động `OnValidatePrincipal`: Tự động từ chối tài khoản inactive, tự động thu hồi phiên nhân viên có `employment_status != "active"`, tự động làm mới Claims và phát cookie mới khi vai trò hoặc phân công thay đổi.

2. **Bảo vệ mã PIN mở khóa kho**:
   - Mã PIN chỉ được hiển thị trực tiếp một lần duy nhất tại màn hình kết quả sau khi hoàn tất Check-in hoặc Cấp lại PIN.
   - Response chứa mã PIN phải có header `Cache-Control: no-store, no-cache`.
   - **Tuyệt đối không lưu PIN** trong URL query string, Cookie, LocalStorage, SessionStorage, TempData, hoặc gửi qua broadcast SignalR.
   - Nhật ký hệ thống (log) tuyệt đối không ghi nhận chuỗi PIN ở dạng rõ (plaintext).

3. **Bảo vệ chống giả mạo yêu cầu (CSRF)**:
   - Toàn bộ các yêu cầu HTTP POST, PUT, DELETE từ trình duyệt bắt buộc phải có Antiforgery Token hợp lệ (`X-CSRF-TOKEN` hoặc `__RequestVerificationToken`).
   - Yêu cầu SignalR kiểm tra chặt chẽ Cookie xác thực và Origin hợp lệ.

---

## 10. Điểm Tích Hợp Đã Đạt & Điều Kiện Mở Wave 4 (Handover Sign-off)

### 10.1. Tóm Tắt Điểm Tích Hợp Đạt Được Tại T08
1. **Kiến trúc & Tách biệt hoàn toàn**:
   - `SelfStorageManagementSystem.Web` tuân thủ tuyệt đối quy tắc 3 tầng, không reference trực tiếp `DataAccess`, `Presentation` (API) hay `WpfClient`.
   - Toàn bộ Controller, PageModel và Hub không inject `DbContext` hay `IRepository<T>`, chỉ sử dụng các Service Interfaces do BLL cung cấp.
2. **Khởi động độc lập Zero JWT Secret**:
   - Host Web khởi động và hoạt động trơn tru không cần bất kỳ khóa bí mật JWT nào (`Jwt:Key`, `JWT_SECRET_KEY`).
   - `IJwtTokenGenerator` không bị rò rỉ vào container DI của Web.
3. **Đăng ký dịch vụ sạch sẽ & Vòng đời an toàn**:
   - 13 dịch vụ nghiệp vụ cốt lõi được đăng ký đúng vòng đời `Scoped` / `Singleton`.
   - Không có đăng ký trùng lặp hoặc memory leak từ Scoped sang Singleton.
4. **Đường truyền trọn vẹn (End-to-End Pipeline trên EF Core InMemory)**:
   - Kiểm thử thành công chu trình: `Cookie` $\longrightarrow$ `OnValidatePrincipal` $\longrightarrow$ `IAccountIdentityService` $\longrightarrow$ `IUserRepository` $\longrightarrow$ `SelfStorageDbContext` (EF Core InMemory).
   - Hỗ trợ làm mới Claims và phạm vi cơ sở (`facility_scope`) tự động.
   - *Lưu ý*: Kiểm thử pipeline trong test host sử dụng EF Core InMemory provider (`CookieAuthWebTestFixture`). Tích hợp trực tiếp trên SQL Server vật lý, transaction và concurrency thuộc phạm vi của task **T17** và chưa được chứng minh bằng test này.
5. **Độc lập bộ kiểm thử (Test Host Decoupling)**:
   - `WebTests` sử dụng `SelfStorageManagementSystem.Web.Program` độc lập hoàn toàn với `Presentation.Program`. Không xảy ra xung đột kiểu dữ liệu.
6. **Bảo toàn 100% kiểm thử hồi quy**:
   - Solution Web (`SelfStorageManagementSystem.Web.sln`): 189/189 kiểm thử PASS (bao gồm 3 kiểm thử bảo mật SignalR Hub mới tại T08-F01).
   - Solution Cũ (`SelfStorageManagementSystem.sln`): 225 kiểm thử PASS, 8 kiểm thử SQL Server Concurrency SKIP (theo đúng thiết kế, không có test fail; 8 test SKIP không được tính là PASS).

### 10.2. Điều Kiện Bàn Giao Mở Các Task Nghiệp Vụ Wave 4

| Task Tiếp Theo | Agent Phụ Trách | Trạng Thái Bàn Giao | Nền Tảng Sẵn Có Cho Triển Khai Độc Lập |
| :--- | :---: | :---: | :--- |
| **T09**: Quản trị ô kho CRUD Razor Pages bằng Form | **Gemini B** | **WAITING** | Chờ Task T08 được reviewer nghiệm thu. Đã có sẵn: `IStorageUnitAdminService` (BLL), `IStorageUnitAdminRepository` (DAL), `StorageUnitHub` (Skeleton có `[Authorize]`), Policy `StaffOnly` & `ManagerOnly`, Layout dùng chung có menu `/StorageUnits`. |
| **T10**: Đặt chỗ kho 15 phút & Background Worker | **Gemini A** | **WAITING** | Chờ Task T08 được reviewer nghiệm thu. Đã có sẵn: `IReservationService` (BLL), `IReservationRepository` (DAL), Policy `CustomerOnly`, Luồng đăng nhập Cookie bảo toàn ReturnUrl nội bộ từ Catalog. |
| **T11**: Quầy tiếp nhận Check-in & Bàn giao kho | **Gemini C** | **WAITING** | Chờ Task T08 được reviewer nghiệm thu. Đã có sẵn: `ICheckInService` (BLL), `ICheckInRepository` (DAL), Policy `StaffOnly`, Ràng buộc bảo mật mã PIN một lần không lưu cache. |
