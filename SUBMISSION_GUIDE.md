# BÁO CÁO NỘP BÀI ASSIGNMENT 01 - 04 MÔN PRN222 (.NET 8)
**Sinh viên:** Võ Thái Hoàng Khôi  
**Lớp:** SE1905  
**Môn học:** PRN222 (Thầy VũLNS)  
**Nhánh:** `PRN222_SE1905_VOTHAIHOANGKHOI_ASM01-04`

---

## 1. CẤU TRÚC 4 THƯ MỤC NỘP BÀI (ASM 01 -> ASM 04)

Mỗi thư mục đại diện cho một giai đoạn hoàn chỉnh, độc lập, có đầy đủ Solution `.sln`, mã nguồn 3 tầng, bộ unit tests và script chạy tự động (`run.bat`, `run.ps1`):

### 1.1. `PRN222_SE1905_VOTHAIHOANGKHOI_ASM01`
* **Trọng tâm công nghệ:** Razor Pages + EF Core + 3-Layer Architecture.
* **Cấu trúc kiến trúc:**
  * `SelfStorageManagementSystem.BusinessObjects`: Chứa Entities, DbContext.
  * `SelfStorageManagementSystem.DataAccess`: Chứa các DAO và Generic/Specific Repositories.
  * `SelfStorageManagementSystem.BusinessLogic`: Chứa Services và DTOs.
  * `SelfStorageManagementSystem.Web`: Razor Pages UI (Tuân thủ nghiêm ngặt: UI không gọi DbContext trực tiếp).
* **Bảo mật & Cấu hình:** Chuỗi kết nối và tài khoản Admin đọc từ `appsettings.json`, phân quyền đăng nhập Session.
* **Kiểm thử:** 185/185 Unit & Web Tests passed.

### 1.2. `PRN222_SE1905_VOTHAIHOANGKHOI_ASM02`
* **Trọng tâm công nghệ:** Tích hợp SignalR Real-time + Nghiệp vụ báo cáo StartDate/EndDate.
* **Tính năng:**
  * Real-time notification hub đồng bộ tức thì cho các client đang mở danh sách khi có Thêm / Sửa / Xóa dữ liệu.
  * Báo cáo thống kê đơn đặt chỗ / hợp đồng thuê theo khoảng thời gian (`StartDate` - `EndDate`).
* **Kiểm thử:** 192/192 Unit & Web Tests passed.

### 1.3. `PRN222_SE1905_VOTHAIHOANGKHOI_ASM03`
* **Trọng tâm công nghệ:** Xử lý bất đồng bộ (`async`/`await`), Phân trang (`Pagination`), Dependency Injection (DI) toàn diện.
* **Tính năng:** Tái sử dụng tầng BusinessObjects & DataAccess, tối ưu hiệu năng cơ sở dữ liệu với phân trang phía server (Server-side Pagination).
* **Kiểm thử:** 190/190 Unit & Web Tests passed.

### 1.4. `PRN222_SE1905_VOTHAIHOANGKHOI_ASM04`
* **Trọng tâm công nghệ:** Final Project hoàn thiện / Tích hợp Web API + Client đa nền tảng (Web + WPF).
* **Tính năng nâng cao:**
  * Kiểm soát tương tranh (Optimistic Concurrency Control, rowversion).
  * Transaction an toàn, chống Race Condition khi đặt chỗ/thanh toán.
  * Web API (Swagger UI) chuẩn RESTful với JWT Token Authentication (khóa 256-bit).
  * Background Worker Service kiểm tra tự động giải phóng vị trí quá hạn giữ chỗ (`ReservationHoldExpirationWorker`).
* **Kiểm thử:** 681/681 Tests passed (450 WebTests + 231 API/Logic/WPF Tests).

---

## 2. HƯỚNG DẪN KHỞI CHẠY TỪNG ASSIGNMENT

### Cách 1: Mở bằng Visual Studio (Khuyên dùng)
Double-click mở file solution tương ứng trong từng thư mục:
1. `PRN222_SE1905_VOTHAIHOANGKHOI_ASM01/PRN222_SE1905_VOTHAIHOANGKHOI_ASM01.sln`
2. `PRN222_SE1905_VOTHAIHOANGKHOI_ASM02/PRN222_SE1905_VOTHAIHOANGKHOI_ASM02.sln`
3. `PRN222_SE1905_VOTHAIHOANGKHOI_ASM03/PRN222_SE1905_VOTHAIHOANGKHOI_ASM03.sln`
4. `PRN222_SE1905_VOTHAIHOANGKHOI_ASM04/PRN222_SE1905_VOTHAIHOANGKHOI_ASM04.sln` (Web App) hoặc `SelfStorageManagementSystem.sln` (Web API Swagger)
Nhấn **F5** để khởi chạy ngay (startup project đã được cấu hình mặc định sẵn).

### Cách 2: Chạy bằng Command Line (`dotnet run`)
```powershell
# Chạy ASM 01
cd PRN222_SE1905_VOTHAIHOANGKHOI_ASM01/src/SelfStorageManagementSystem.Web
dotnet run

# Chạy ASM 02
cd PRN222_SE1905_VOTHAIHOANGKHOI_ASM02/src/SelfStorageManagementSystem.Web
dotnet run

# Chạy ASM 03
cd PRN222_SE1905_VOTHAIHOANGKHOI_ASM03/src/SelfStorageManagementSystem.Web
dotnet run

# Chạy ASM 04 Web
cd PRN222_SE1905_VOTHAIHOANGKHOI_ASM04/src/SelfStorageManagementSystem.Web
dotnet run

# Chạy ASM 04 Web API (Swagger)
cd PRN222_SE1905_VOTHAIHOANGKHOI_ASM04/src/SelfStorageManagementSystem.Presentation
dotnet run
```

---

## 3. DANH SÁCH TÀI KHOẢN ĐÃ SEED SẴN

| Vai trò | Email đăng nhập | Mật khẩu mặc định |
| :--- | :--- | :--- |
| **System Administrator (Admin)** | `admin@selfstorage.local` | `SecretAdminPassword123!` |
| **Facility Manager (Quản lý chi nhánh)** | `manager.d1@selfstorage.local` | `SecretAdminPassword123!` |
| **Facility Staff (Nhân viên vận hành)** | `staff.d1@selfstorage.local` | `SecretAdminPassword123!` |
| **Storage Customer (Khách hàng)** | `customer1@example.com` | `SecretAdminPassword123!` |
