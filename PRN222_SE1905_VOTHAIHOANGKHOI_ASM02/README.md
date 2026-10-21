# PRN222_SE1905_VOTHAIHOANGKHOI_ASM02 — Hệ Thống Quản Lý Kho Tự Quản (Real-Time SignalR & Quản Lý Đặt Chỗ)

Dự án bài tập Assignment 02 môn PRN222 (C# & .NET Core / ASP.NET Core) thực hiện theo kiến trúc 3 tầng chuẩn mực của Đại học FPT, kế thừa và mở rộng toàn diện từ Assignment 01, bổ sung cơ chế giao tiếp thời gian thực hai chiều với **SignalR Hub**, quản lý đặt chỗ (**Reservations**), tự động hóa giải phóng giữ chỗ hết hạn (15 phút), kiểm soát tranh chấp đồng thời và thống kê báo cáo doanh số/đặt chỗ theo phạm vi cơ sở.

---

## 1. Công nghệ sử dụng (.NET Tech Stack)

- **Framework**: .NET 8 (LTS) — C# 12
- **Tầng giao diện (Presentation / UI)**:
  - ASP.NET Core Razor Pages cho luồng quản trị danh mục ô kho (`/StorageUnits`), quy trình đặt chỗ (`/Reservations`), báo cáo thống kê (`/Reports/Reservations`), và trang người dùng.
  - MVC Controllers cho luồng xác thực Cookie (`/Account/Login`, `/Account/Logout`), API điều hướng thanh toán và mô phỏng giao dịch.
  - Real-time Client: SignalR JavaScript Client (`@microsoft/signalr`), xử lý sự kiện bất đồng bộ hai chiều, DOM diffing / patching, generation counter chống race condition, hiển thị trạng thái đồng bộ real-time.
- **Thời gian thực (Real-time Hub & Communication)**:
  - ASP.NET Core SignalR Hub (`/storageUnitHub`).
  - Hỗ trợ đầy đủ các invocation & broadcast theo chuẩn thực hành:
    - Invocations: `HubCreate_StorageUnit`, `HubUpdate_StorageUnit`, `HubDelete_StorageUnit`.
    - Broadcasts: `Receiver_CreateStorageUnit`, `Receiver_UpdateStorageUnit`, `Receiver_DeleteStorageUnit`.
  - Giám sát kết nối đa client (`IStorageUnitHubConnectionTracker`), tự động quản lý group SignalR theo phạm vi cơ sở (`FacilityId`) và thu hồi phân quyền động tức thì (`StorageUnitHubScopeRevocationService`).
- **Tầng nghiệp vụ (Business Logic Layer - BLL)**:
  - C# Class Library, Service-oriented, Dependency Injection (DI) Composition Entrypoint (`AddSelfStorageWebComposition`).
  - Typed Domain Exceptions (`NotFoundException`, `ConflictException`, `ForbiddenException`, `BadRequestException`, `UnauthorizedException`).
  - Dịch vụ đặt chỗ (`IReservationService`), tự động hủy giữ chỗ quá hạn 15 phút (`ExpireOverdueHoldsDetailedAsync`), kiểm tra sức chứa khả dụng theo ngày (`GetAvailableCapacityAsync`).
  - Dịch vụ nền định kỳ (`IHostedService`): `ReservationHoldExpirationWorker` (chu kỳ quét 30s) và `StorageUnitScopeSyncWorker`.
- **Tầng truy cập dữ liệu (Data Access Layer - DAL)**:
  - C# Class Library, Entity Framework Core 8 (`EF Core`).
  - Generic Repository & Specialized Unit Repositories (`IReservationRepository`, `IStorageUnitAdminRepository`, `ICheckInRepository`, `IPaymentRepository`, `IUserRepository`, `IFacilityCatalogRepository`).
  - Khóa bi quan và kiểm soát giao dịch dữ liệu trên SQL Server (`UPDLOCK`, `ROWLOCK`).
- **Cơ sở dữ liệu (Database)**: SQL Server Database-First schema (`SelfStorageManagement`).
- **Xác thực & Phân quyền (Authentication & Authorization)**:
  - Cookie Authentication (`CookieAuthenticationDefaults.AuthenticationScheme`), thời hạn 5 phút, sliding expiration, an toàn cho cả HTTP Form và SignalR WebSocket/LongPolling transport.
  - Phân quyền theo Policy: `AdminOnly`, `ManagerOnly`, `StaffOnly`, `CustomerOnly`.
  - Kiểm soát phạm vi cơ sở dữ liệu phân công (Facility Scoping).

---

## 2. Kiến trúc giải pháp (3-Tier & Real-Time Architecture)

Hệ thống tuân thủ nghiêm ngặt chuỗi phụ thuộc đơn hướng:
$$\text{Presentation (Web + SignalR Hub)} \longrightarrow \text{BusinessLogic (BLL)} \longrightarrow \text{DataAccess (DAL)} \longrightarrow \text{SQL Server (DbContext)}$$

1. **`SelfStorageManagementSystem.Web` (Presentation & Real-Time)**:
   - Razor Pages:
     - Quản trị danh mục ô kho (`/StorageUnits`): Tích hợp script `storage-units-realtime.js`, cập nhật danh sách tức thì giữa các trình duyệt khi có thao tác Create/Update/Delete mà không cần tải lại toàn trang.
     - Quy trình Đặt chỗ (`/Reservations`): Khách hàng đặt giữ chỗ 15 phút (`/Reservations/Create`), xem chi tiết (`/Reservations/Details`), hủy đặt chỗ (`/Reservations/Cancel`), danh sách đặt chỗ cá nhân (`/Reservations/MyReservations`).
     - Báo cáo Đặt chỗ (`/Reports/Reservations`): Thống kê dành cho Nhân viên/Quản lý theo khoảng ngày và lọc cơ sở phân quyền.
   - SignalR Hub (`StorageUnitHub` tại endpoint `/storageUnitHub`): Tiếp nhận lệnh từ client, kiểm tra xác thực Cookie `[Authorize]`, phân quyền theo vai trò và gọi BLL service tương ứng, sau đó phát broadcast tới client trong cùng cơ sở.
2. **`SelfStorageManagementSystem.BusinessLogic` (BLL)**:
   - Quản lý logic nghiệp vụ, tính toán biểu giá, đối soát trạng thái, đồng bộ realtime sau khi giao dịch cơ sở dữ liệu đã commit thành công (`IStorageUnitRealtimePublisher`).
   - Xử lý nghiệp vụ đặt chỗ: giữ chỗ tối đa 15 phút, giải phóng sức chứa khi hết hạn hoặc khi bị hủy.
3. **`SelfStorageManagementSystem.DataAccess` (DAL)**:
   - Triển khai Repositories với các truy vấn chuyên biệt, sử dụng transaction và locking để ngăn ngừa việc hai khách hàng đặt trùng ô kho hoặc vượt quá công suất cùng một thời điểm.

---

## 3. Các chức năng chính trong ASM02 (Core Features)

1. **Giao tiếp Real-Time hai chiều với SignalR**:
   - Nhân viên/Quản lý thực hiện tạo mới, cập nhật hoặc xóa ô kho trên một trình duyệt; các trình duyệt khác đang mở danh sách ô kho sẽ lập tức phản ánh thay đổi và hiển thị hiệu ứng highlight dòng tương ứng.
   - Cơ chế generation counter chống race condition khi phản hồi mạng về chậm hoặc không đúng thứ tự.
   - Tự động ngắt và thu hồi phân quyền tức thì khi tài khoản bị thay đổi phạm vi cơ sở hoặc bị vô hiệu hóa.
2. **Quy trình Quản lý Đặt chỗ (Reservations)**:
   - Đặt giữ chỗ tạm thời (Hold) với thời hạn tự động tính 15 phút theo quy định nghiệp vụ.
   - Khách hàng kiểm tra thông tin ô kho, đơn giá niêm yết theo loại ô kho và cơ sở trước khi xác nhận.
   - Hủy giữ chỗ chủ động hoặc tự động hủy thông qua Background Worker (`ReservationHoldExpirationWorker`).
   - Phát sự kiện SignalR giải phóng năng lực ô kho tức thì khi lượt giữ chỗ hết hạn.
3. **Báo cáo Thống kê Đặt chỗ (`/Reports/Reservations`)**:
   - Lọc báo cáo theo khoảng thời gian (`StartDate` đến `EndDate`), trạng thái đặt chỗ và cơ sở thuộc quyền quản lý.
   - Tổng hợp số lượng đặt chỗ, tỷ lệ hoàn tất, tỷ lệ hủy, tổng doanh thu dự kiến/thực thu.
4. **Kế thừa toàn diện các tính năng ASM01**:
   - Bảo mật Cookie Authentication, chống CSRF, ngăn chặn Open Redirect.
   - Chặn xóa cứng ô kho khi đã phát sinh dữ liệu liên kết tham chiếu (hợp đồng, đặt chỗ, bàn giao, bảo trì).

---

## 4. Cấu hình Chuỗi kết nối Database & Khởi động Hệ thống

Mã nguồn tuân thủ nguyên tắc không lưu trữ thông tin nhạy cảm (secrets) vào git.

### 4.1. Chuỗi kết nối Database

Cấu hình trong `src/SelfStorageManagementSystem.Web/appsettings.json` hoặc nạp qua **.NET User Secrets** / Biến môi trường:

```powershell
cd src/SelfStorageManagementSystem.Web
dotnet user-secrets set "ConnectionStrings:DefaultConnection" "Server=YOUR_SERVER;Database=SelfStorageManagement;User Id=YOUR_USER;Password=YOUR_SECRET;TrustServerCertificate=True"
```

### 4.2. Lệnh Build và Chạy Ứng dụng

```powershell
# Di chuyển tới thư mục ASM02
cd D:\PRN222\PRN222_SE1905_VOTHAIHOANGKHOI_ASM02

# Build toàn bộ giải pháp
dotnet build PRN222_SE1905_VOTHAIHOANGKHOI_ASM02.sln

# Khởi chạy ứng dụng Web
dotnet run --project src/SelfStorageManagementSystem.Web/SelfStorageManagementSystem.Web.csproj
```

Truy cập ứng dụng tại địa chỉ: `https://localhost:5001` hoặc `http://localhost:5000`.
