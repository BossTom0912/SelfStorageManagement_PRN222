# HƯỚNG DẪN DEMO BẢO VỆ ĐỒ ÁN PRN222 (ASM 01 – ASM 04)
**Sinh viên thực hiện:** Võ Thái Hoàng Khôi — Lớp: SE1905

---

## 🔑 I. DANH SÁCH TÀI KHOẢN ĐĂNG NHẬP (Tất cả chung 1 mật khẩu)

> **MẬT KHẨU CHUNG CHO MỌI TÀI KHOẢN:** `SecretAdminPassword123!`  
> *(Hệ thống mã hóa chuẩn PBKDF2-SHA256, phiên Cookie HttpOnly an toàn 5 phút)*

| Vai trò (Role) | Email đăng nhập | Tên hiển thị | Cơ sở phân công | Quyền hạn & Chức năng chính |
| :--- | :--- | :--- | :--- | :--- |
| **Quản trị viên**<br>`system_administrator` | `admin@selfstorage.local` | System Administrator | Toàn hệ thống | Quản trị tài khoản người dùng (`/Admin/Accounts`), đổi trạng thái, phân quyền, xem/quản lý toàn bộ ô kho các cơ sở. |
| **Quản lý cơ sở**<br>`facility_manager` | `manager.d1@selfstorage.local`<br>*(hoặc `manager1@gmail.com`)* | Manager District 1 | Cơ sở 1 (HCM) | Quản lý ô kho tại cơ sở (Thêm/Sửa/Xóa ô kho), tiếp nhận tại quầy, duyệt bàn giao. |
| **Nhân viên quầy**<br>`facility_staff` | `staff.d1@selfstorage.local` | Staff District 1 | Cơ sở 1 (HCM) | Tiếp nhận khách tại quầy (`/Staff/CheckIn`), tra cứu ô kho cơ sở được gán. Bị chặn vào trang quản trị tài khoản. |
| **Khách hàng**<br>`storage_customer` | `customer1@example.com` | Customer One | Cá nhân | Xem danh sách kho đang thuê (`/MyUnits`), biên bản bàn giao. **Bị chặn 403 Forbidden** nếu cố tình vào menu quản trị. |

---

## 📊 II. BỘ DỮ LIỆU MẪU (DATA SEED) ĐÃ SẴN SÀNG HIỂN THỊ

Hệ thống đã nạp đầy đủ dữ liệu phong phú vào Database (áp dụng cho cả `SelfStoragePRN222` và `SelfStoragePRN222_WebDemo`):

1. **Tổng cộng 19 ô kho (Storage Units)** trải đều trên 2 cơ sở (HCM & Hà Nội), phân bổ theo Tầng 1, Tầng 2, Khu A, Khu B:
   - **11 ô Trống (`Available`):** `A-101`, `A-102`, `A-103`, `A-105`, `A-201`, `A-202`, `A-204`, `HN-101`, `HN-104`, `HN-105`, `HN-107`.
   - **4 ô Đang thuê (`Occupied`):** `A-106`, `A-107`, `A-203`, `HN-102`.
   - **1 ô Đã đặt trước (`Reserved`):** `A-104`.
   - **2 ô Bảo trì (`Maintenance`):** `A-108`, `A-109`, `HN-103`.
   - **1 ô Chờ kiểm tra (`Pending Inspection`):** `A-110`.
   *(Đủ số lượng để kích hoạt thanh Phân trang Pagination 2 trang, và lọc dropdown ra đầy đủ mọi trạng thái).*

2. **2 Đơn đặt chỗ mẫu (Reservations) sẵn sàng cho nghiệp vụ tiếp nhận quầy (`/Staff/CheckIn`):**
   - **`RES-DEMO-001`**: Đơn đã xác nhận (`confirmed`), khách hàng `customer1@example.com`, giá thuê 1.000.000 đ/tháng.
   - **`RES-DEMO-002`**: Đơn chờ đặt cọc (`awaiting_deposit`), giá thuê 1.800.000 đ/tháng.

---

## 🚀 III. HƯỚNG DẪN KHỞI CHẠY NHANH CÁC DỰ ÁN

| Bài | Thư mục thực thi | Lệnh khởi chạy Web / Console | Cổng truy cập mặc định |
| :--- | :--- | :--- | :--- |
| **ASM 01** | `PRN222_SE1905_VOTHAIHOANGKHOI_ASM01` | `.\run.ps1` hoặc `dotnet run --project src/SelfStorageManagementSystem.Web` | `https://localhost:7041` |
| **ASM 02** | `PRN222_SE1905_VOTHAIHOANGKHOI_ASM02` | `.\run.ps1` hoặc `dotnet run --project src/SelfStorageManagementSystem.Web` | `https://localhost:7041` |
| **ASM 03** | `PRN222_SE1905_VOTHAIHOANGKHOI_ASM03` | `.\run.ps1` hoặc `dotnet run --project src/SelfStorageManagementSystem.Web` | `https://localhost:7041` |
| **ASM 04** | `PRN222_SE1905_VOTHAIHOANGKHOI_ASM04` | `.\run-worker.ps1` (Console Worker) & `.\check-service.ps1 -OpenServices` | Không dùng cổng (tiến trình nền) |

---

## 🖥️ III. KỊCH BẢN THAO TÁC DEMO TRÊN UI (TỪNG BƯỚC CHI TIẾT)

---

### 📌 1. ASM 01: ASP.NET Core MVC + Real-time SignalR
* **Trọng tâm công nghệ:** Cấu trúc chuẩn ASP.NET Core MVC (`Controllers/StorageUnitsController.cs` + `Views/StorageUnits/*.cshtml`), kết hợp SignalR realtime đồng bộ dữ liệu hai chiều.

#### Các bước thao tác demo:
1. **Đăng nhập:**
   - Vào `/Account/Login`, nhập `manager.d1@selfstorage.local` (hoặc `manager1@gmail.com`) / `SecretAdminPassword123!`.
2. **Thao tác MVC CRUD (`/StorageUnits`):**
   - Click menu **"Quản lý ô kho"** trên Navbar.
   - **Tìm kiếm & Phân trang:** Gõ mã kho vào ô tìm kiếm, lọc theo trạng thái (`Available`, `Occupied`, `Maintenance`) hoặc bấm chuyển trang (Pagination).
   - **Thêm mới (Create):** Nhấn **"Thêm mới ô kho"** -> Điền thông tin ô kho -> Bấm Lưu -> Danh sách cập nhật ngay.
   - **Chỉnh sửa (Edit):** Nhấn **"Sửa"** tại một ô kho -> Cập nhật thông số/giá thuê -> Bấm Cập nhật.
   - **Xóa (Delete):** Nhấn **"Xóa"** ô kho trống -> Xác nhận -> Ô kho biến mất khỏi danh sách.
3. **Show tính năng Real-time SignalR (Điểm nhấn ăn điểm):**
   - Mở **2 cửa sổ trình duyệt song song** (1 bên trình duyệt thường đang login Manager, 1 bên Cửa sổ ẩn danh đang login Admin/Manager khác).
   - Ở cửa sổ 1, bấm Sửa trạng thái một ô kho từ `Available` sang `Occupied`.
   - **Chỉ cho thầy thấy:** Cửa sổ 2 **tự động nhảy trạng thái ngay lập tức mà KHÔNG cần bấm F5**.
4. **Chứng minh Phân quyền bảo mật (Security):**
   - Đăng xuất, đăng nhập bằng `customer1@example.com`.
   - Cố tình gõ trực tiếp URL `/StorageUnits` trên thanh địa chỉ.
   - **Kết quả:** Hệ thống chặn ngay lập tức và hiển thị **403 Forbidden / Từ chối truy cập**.

---

### 📌 2. ASM 02: ASP.NET Core Razor Pages + Nghiệp vụ Quầy
* **Trọng tâm công nghệ:** Triển khai theo mô hình Razor Pages (`Pages/StorageUnits/Index.cshtml.cs`), nghiệp vụ check-in tiếp nhận khách.

#### Các bước thao tác demo:
1. **Đăng nhập:** Bằng `manager.d1@selfstorage.local` / `SecretAdminPassword123!`.
2. **Giao diện Razor Pages:**
   - Vào menu **"Quản lý ô kho"** -> Giới thiệu cho thầy đây là kiến trúc Razor Pages với `PageModel` riêng biệt.
   - Thử nghiệm các bộ lọc tìm kiếm và phân trang dữ liệu.
3. **Nghiệp vụ tiếp nhận tại quầy (Check-in):**
   - Vào menu **"Tiếp nhận tại quầy"** (`/Staff/CheckIn`).
   - Nhập mã đặt chỗ/mã PIN của khách để mô phỏng quy trình bàn giao chìa khóa/mã mở kho.

---

### 📌 3. ASM 03: Blazor Server Interactive Component
* **Trọng tâm công nghệ:** Giao diện tương tác cao với Blazor Server (`/Blazor/StorageUnits`), render vi sai qua WebSocket mà không cần reload trang.

#### Các bước thao tác demo:
1. **Đăng nhập:** Bằng `admin@selfstorage.local` hoặc `manager.d1@selfstorage.local`.
2. **Giao diện Blazor Interactive:**
   - Bấm vào menu **"Quản trị ô kho Blazor"** (`/Blazor/StorageUnits`).
   - **Tìm kiếm trực tiếp (Live Search):** Gõ từng chữ cái vào ô tìm kiếm -> Bảng dữ liệu tự động lọc theo thời gian thực mà không chớp trang.
   - **Phân trang mượt mà:** Bấm nút chuyển trang 1, 2, 3 -> Dữ liệu đổi tức thì qua kết nối WebSocket SignalR của Blazor.
   - **Mở F12 kiểm chứng:** Bật tab **Network** -> Chỉ thấy 1 luồng WebSocket duy nhất trao đổi các gói tin nhị phân siêu nhẹ, chứng minh 100% Blazor Server thuần.

---

### 📌 4. ASM 04: Console Worker / Windows Service (5s Heartbeat)
* **Trọng tâm công nghệ:** Ứng dụng chạy ngầm Background Service, in log chu kỳ 5 giây và tích hợp kiểm tra dịch vụ hệ thống Windows Service.

#### Các bước thao tác demo:
1. **Chạy Console Worker in log 5s (Thao tác chính):**
   - Mở cửa sổ PowerShell tại thư mục `PRN222_SE1905_VOTHAIHOANGKHOI_ASM04`.
   - Gõ lệnh:
     ```powershell
     .\run-worker.ps1
     ```
   - **Chỉ cho thầy xem console:** Mỗi đúng **5 giây**, màn hình sẽ tự động in ra 1 dòng log màu xanh:
     ```text
     [15:35:00 INF] ASM04 VOTHAIHOANGKHOI heartbeat #1 - Active worker alive
     [15:35:05 INF] ASM04 VOTHAIHOANGKHOI heartbeat #2 - Active worker alive
     [15:35:10 INF] ASM04 VOTHAIHOANGKHOI heartbeat #3 - Active worker alive
     ```
   - Nhấn `Ctrl + C` khi muốn dừng tiến trình.
2. **Kiểm tra Windows Service:**
   - Chạy lệnh:
     ```powershell
     .\check-service.ps1 -OpenServices
     ```
   - **Hiện tượng:** Cửa sổ **Services (`services.msc`)** của Windows sẽ tự động mở lên, sẵn sàng để kiểm tra dịch vụ `PRN222_VOTHAIHOANGKHOI_ASM04`.
