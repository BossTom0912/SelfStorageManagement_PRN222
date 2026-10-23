# PRN222_SE1905_VOTHAIHOANGKHOI_ASM04 — Hệ Thống Quản Lý Kho Tự Quản (Triển Khai Nâng Cao & Tích Hợp Hệ Thống)

Dự án bài tập Assignment 04 (Đồ án tổng kết / Final Project) môn PRN222 (C# & .NET Core / ASP.NET Core / WPF & REST API) thực hiện theo kiến trúc phân tầng chuẩn Enterprise của Đại học FPT. Bài tập tích hợp toàn diện hệ thống quản lý kho tự quản (Self-Storage Management), áp dụng các kỹ thuật kiến trúc nâng cao: **Kiểm soát đồng thời (Concurrency Control)**, **Giao dịch phân tán an toàn (Transactions)**, **Tách biệt Backend Presentation API và Frontend Desktop WPF Client giao tiếp qua mạng**, kết hợp thời gian thực **SignalR Hub** và bảo mật đa tầng.

---

## 1. Công nghệ sử dụng (.NET Tech Stack)

- **Framework**: .NET 8 (LTS) — C# 12
- **Tầng giao diện (Presentation & Multi-Client)**:
  - **REST Web API (`SelfStorageManagementSystem.Presentation`)**: Tách biệt độc lập Backend API, cung cấp endpoints RESTful chuẩn JSON cho các client mạng.
  - **Desktop WPF Client (`SelfStorageManagementSystem.WpfClient`)**: Giao diện máy trạm WPF trên nền MVVM, giao tiếp với Backend qua mạng HTTP (`HttpClient`), xử lý thao tác quản lý kho mượt mà không khóa luồng UI.
  - **Web Client (`SelfStorageManagementSystem.Web`)**: Web Application đa năng kết hợp Razor Pages, MVC Controllers và giao tiếp thời gian thực SignalR Hub (`/storageUnitHub`).
- **Tầng nghiệp vụ (Business Logic Layer - BLL)**:
  - `SelfStorageManagementSystem.BusinessLogic`: Service-oriented architecture, Dependency Injection Composition.
  - Cơ chế **Optimistic Concurrency** (`ExpectedUpdatedAt`) và **Pessimistic Locking** (`UPDLOCK`, `ROWLOCK`) ngăn chặn tình trạng tranh chấp đặt chỗ (Double-Booking / Overbooking Race Condition).
  - Quản lý giao dịch dữ liệu chặt chẽ (`IDbContextTransaction`) đảm bảo tính toàn vẹn ACID trong các luồng đặt kho, thanh toán và bàn giao.
  - Worker nền tự động giải phóng giữ chỗ quá hạn 15 phút (`ReservationHoldExpirationWorker`).
- **Tầng truy cập dữ liệu (Data Access Layer - DAL)**:
  - `SelfStorageManagementSystem.DataAccess`: Entity Framework Core 8 (`EF Core`), Generic Repository & Specialized Unit Repositories.
  - Schema SQL Server Database-First (`SelfStorageManagement`).
- **Bảo mật & Kiểm soát truy cập**:
  - Đọc ConnectionStrings và tài khoản Admin từ `appsettings.json` qua `IConfiguration`.
  - Cookie Authentication (Web), Bearer JWT Tokens (API), Role Policies (`AdminOnly`, `ManagerOnly`, `StaffOnly`, `CustomerOnly`).
  - Chống CSRF (`[ValidateAntiForgeryToken]`), chống XSS (Razor HTML Encoding), ngăn chặn SQL Injection qua LINQ Parameterization, xử lý an toàn khóa ngoại khi xóa (chuyển `is_listed = false` / Inactive thay vì xóa cứng).

---

## 2. Kiến trúc giải pháp (Enterprise Multi-Tier Architecture)

Hệ thống được tổ chức với cấu trúc dự án độc lập, không tham chiếu chéo ra ngoài thư mục:

$$\begin{aligned}
\text{WPF Desktop Client} &\longrightarrow \text{HTTP REST API (Presentation)} \longrightarrow \text{BLL (BusinessLogic)} \longrightarrow \text{DAL (DataAccess)} \longrightarrow \text{SQL Server} \\
\text{Web Application (Razor/MVC)} &\longrightarrow \text{BLL (BusinessLogic)} \longrightarrow \text{DAL (DataAccess)} \longrightarrow \text{SQL Server}
\end{aligned}$$

1. **`SelfStorageManagementSystem.Presentation`**: Tầng API Server độc lập, ánh xạ các Controller API phục vụ client ngoài (WPF, Mobile, bên thứ ba).
2. **`SelfStorageManagementSystem.WpfClient`**: Ứng dụng Desktop sử dụng WPF (Windows Presentation Foundation) kết nối tới Backend API.
3. **`SelfStorageManagementSystem.Web`**: Cổng thông tin Web cho khách hàng thuê kho và ban quản lý cơ sở, tích hợp SignalR Hub đồng bộ tức thời.
4. **`SelfStorageManagementSystem.BusinessLogic`**: Trọng tâm nghiệp vụ, quy tắc sức chứa kho, tính toán chi phí, xử lý concurrency.
5. **`SelfStorageManagementSystem.DataAccess`**: Quản lý DbContext, Repositories, ánh xạ thực thể cơ sở dữ liệu.
6. **Tuyệt đối tuân thủ**: Tầng UI / Controller / Window **không bao giờ** inject hoặc gọi trực tiếp `DbContext`.

---

## 3. Các kỹ thuật nâng cao nổi bật trong ASM04

### 3.1. Kiểm soát tranh chấp đồng thời (Concurrency Control)
- **Optimistic Concurrency**: Sử dụng trường thời gian `updated_at` đối soát qua `ExpectedUpdatedAt`. Khi hai quản trị viên cùng chỉnh sửa một ô kho, người gửi sau sẽ nhận lỗi `ConflictException` kèm dữ liệu mới nhất trên server để đối soát, không bị ghi đè ngầm (Lost Update).
- **Pessimistic Concurrency & Capacity Locking**: Áp dụng gợi ý khóa SQL Server (`UPDLOCK`, `ROWLOCK`) khi khách hàng đồng thời đặt chỗ kho trong cùng một khoảng thời gian, loại bỏ hoàn toàn hiện tượng Race Condition gây vượt sức chứa tối đa của cơ sở.

### 3.2. Quản lý Giao dịch an toàn (ACID Transactions)
- Các luồng nghiệp vụ phức tạp như Tạo hợp đồng thuê + Trừ kho + Tạo hóa đơn ban đầu + Cấp mã PIN được thực thi trong transaction nguyên tử. Nếu xảy ra bất kỳ ngoại lệ nào, hệ thống tự động rollback 100%, không để lại dữ liệu rác hay trạng thái mồ côi.

### 3.3. Tách biệt hoàn toàn Kiến trúc Mạng (Client/Server Separation)
- Ứng dụng WPF giao tiếp với Backend Presentation API hoàn toàn qua HTTP RESTful JSON, chứng minh khả năng mở rộng hệ thống đa nền tảng và tích hợp với các hệ sinh thái doanh nghiệp (KMS / OData / API).

### 3.4. Chính sách Xóa an toàn & Bảo toàn tính toàn vẹn (Soft Delete / Inactive Policy)
- Khi thực hiện yêu cầu xóa ô kho hoặc bản ghi có dữ liệu liên quan (lịch sử kiểm tra, hợp đồng, đặt chỗ), hệ thống tự động chuyển trạng thái niêm yết sang ngừng hoạt động (`is_listed = false` / Inactive) thay vì xóa vật lý khỏi database, tuân thủ nghiêm ngặt tiêu chuẩn đánh giá của FPTU.

---

## 4. Cấu hình & Tài khoản Demo

File cấu hình tại `src/SelfStorageManagementSystem.Web/appsettings.json`:
- **Chuỗi kết nối (Connection String)**: Đọc từ `ConnectionStrings:DefaultConnection`.
- **Tài khoản Admin**: Đọc từ section `AdminAccount:Email`.

Danh sách tài khoản kiểm thử:
- **Quản trị viên (Admin)**: `admin@selfstorage.local` (Toàn quyền hệ thống)
- **Quản lý cơ sở (Manager)**: `manager.d1@selfstorage.local` (Quản lý kho cơ sở Quận 1)
- **Nhân viên cơ sở (Staff)**: `staff.d1@selfstorage.local` (Vận hành, bàn giao)
- **Khách hàng (Customer)**: `customer1@example.com` (Đặt giữ chỗ, thanh toán)

---

## 5. Hướng dẫn Build, Kiểm thử & Chạy hệ thống

### 5.1. Build Toàn bộ Solution
Tại thư mục `D:\PRN222\PRN222_SE1905_VOTHAIHOANGKHOI_ASM04`:
```powershell
# Build Solution tích hợp đầy đủ (BLL, DAL, Presentation API, WPF Client, Tests)
dotnet build SelfStorageManagementSystem.sln

# Build Solution Web Application (Web, SignalR, WebTests)
dotnet build SelfStorageManagementSystem.Web.sln
```

### 5.2. Chạy Kiểm thử Tự động (Automated Testing)
```powershell
# Chạy bộ test nghiệp vụ cốt lõi (250 tests bao gồm 19 tests Concurrency SQL LocalDB thực tế)
dotnet test tests/SelfStorageManagementSystem.Tests/SelfStorageManagementSystem.Tests.csproj

# Chạy bộ test Web integration (Razor Pages, MVC, Cookie Auth, API, SignalR)
dotnet test tests/SelfStorageManagementSystem.WebTests/SelfStorageManagementSystem.WebTests.csproj
```

### 5.3. Khởi chạy Ứng dụng
```powershell
# Chạy Web Application & SignalR Hub
dotnet run --project src/SelfStorageManagementSystem.Web

# Chạy Backend Presentation API (nếu demo tách mạng)
dotnet run --project src/SelfStorageManagementSystem.Presentation

# Chạy ứng dụng Desktop WPF Client
dotnet run --project src/SelfStorageManagementSystem.WpfClient
```

---

## 6. Hướng dẫn Vấn đáp & Bảo vệ Đồ án (Defense / Q&A Checklist)

1. **Câu hỏi về Kiến trúc 3 tầng & DI**:
   - *Hỏi:* Tầng UI có được gọi `DbContext` không?
   - *Trả lời:* Tuyệt đối không. Luồng thực thi luôn là `Presentation (Web / WPF / API) -> BLL (Service) -> DAL (Repository / DAO) -> EF Core DbContext`. Không có Controller hay ViewModel nào tham chiếu trực tiếp đến `DbContext`.
2. **Câu hỏi về Concurrency & Race Condition**:
   - *Hỏi:* Nếu hai khách hàng bấm đặt kho cùng 1 giây thì xử lý thế nào?
   - *Trả lời:* Sử dụng Pessimistic Locking với câu lệnh SQL Server có `UPDLOCK, ROWLOCK` trong Repository và kiểm tra tổng sức chứa khả dụng trước khi commit transaction. Nếu hết sức chứa, người thứ hai sẽ nhận ngoại lệ `ConflictException`.
3. **Câu hỏi về Xóa dữ liệu có quan hệ**:
   - *Hỏi:* Nếu ô kho đã có khách đặt hoặc có lịch sử thanh toán, bấm Xóa có mất dữ liệu không?
   - *Trả lời:* Không mất. Hệ thống kiểm tra quan hệ ràng buộc ngoại và chuyển cờ `is_listed = false` (Inactive) để giữ nguyên lịch sử kế toán và hợp đồng, không thực hiện hard-delete làm hỏng toàn vẹn dữ liệu.
