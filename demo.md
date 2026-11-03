# HƯỚNG DẪN DEMO BẢO VỆ ĐỒ ÁN PRN222 (GỘP HỢP NHẤT ASM 01 – ASM 04)
**Sinh viên thực hiện:** Võ Thái Hoàng Khôi — Lớp: SE1905  
**Định danh chống đạo văn (Anti-Plagiarism):** `KhoiVTH`  
- Entities: `StorageUnitKhoiVTH.cs`, `UnitTypeKhoiVTH.cs`  
- Service: `IStorageUnitKhoiVTHService.cs`, `StorageUnitKhoiVTHService.cs`  
- Repository: `IStorageUnitKhoiVTHRepository.cs`, `StorageUnitKhoiVTHRepository.cs`  
- SQL Views: `[core].[StorageUnitKhoiVTH]`, `[core].[UnitTypeKhoiVTH]`

---

## 🔑 I. DANH SÁCH TÀI KHOẢN ĐĂNG NHẬP (Tất cả chung 1 mật khẩu)

> **MẬT KHẨU CHUNG CHO MỌI TÀI KHOẢN:** `SecretAdminPassword123!`  
> *(Hệ thống mã hóa chuẩn PBKDF2-SHA256, phiên Cookie HttpOnly an toàn 5 phút)*

| Vai trò (Role) | Email đăng nhập | Tên hiển thị | Cơ sở phân công | Quyền hạn & Chức năng chính |
| :--- | :--- | :--- | :--- | :--- |
| **Quản trị viên**<br>`system_administrator` | `admin@selfstorage.local` | System Administrator | Toàn hệ thống | Quản trị tài khoản người dùng (`/Admin/Accounts`), đổi trạng thái, phân quyền, xem/quản lý toàn bộ ô kho các cơ sở. |
| **Quản lý cơ sở**<br>`facility_manager` | `manager.d1@selfstorage.local`<br>*(hoặc `manager1@gmail.com`)* | Manager District 1 | Cơ sở 1 (HCM) | Quản lý ô kho tại cơ sở (Thêm/Sửa/Xóa ô kho), tiếp nhận tại quầy, duyệt bàn giao, xem báo cáo đặt chỗ. |
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

## 🚀 III. HƯỚNG DẪN KHỞI CHẠY DỰ ÁN HỢP NHẤT (`SelfStorageManagement_PRN222`)

Toàn bộ **ASM 01, ASM 02, ASM 03, ASM 04** đã được gộp chung vào duy nhất thư mục `D:\PRN222\SelfStorageManagement_PRN222`:

| Phần thi | Thư mục thực thi | Lệnh khởi chạy | Cổng / Hiển thị |
| :--- | :--- | :--- | :--- |
| **ASM 01 + 02 + 03**<br>*(MVC + SignalR + Razor + Blazor)* | `D:\PRN222\SelfStorageManagement_PRN222` | `.\run.ps1` *(hoặc `dotnet run --project src/SelfStorageManagementSystem.Web`)* | `https://localhost:7041` *(hoặc `http://localhost:5155`)* |
| **ASM 04**<br>*(Console Worker 5s & Windows Service)* | `D:\PRN222\SelfStorageManagement_PRN222` | `.\run-worker.ps1` & `.\check-service.ps1 -OpenServices` | Log trực tiếp trên Console & hộp thoại `services.msc` |

---

## 🖥️ IV. KỊCH BẢN THAO TÁC DEMO TRÊN UI (TỪNG BƯỚC CHI TIẾT)

*(Tất cả các nút chức năng `/StorageUnits`, `/Staff/CheckIn`, `/Reports/Reservations`, `/Blazor/StorageUnits` đều đã được gắn sẵn thành nút bấm trực tiếp trên thanh Navbar!)*

---

### 📌 1. ASM 01: ASP.NET Core MVC + Real-time SignalR
* **Trọng tâm công nghệ:** Cấu trúc chuẩn ASP.NET Core MVC (`Controllers/StorageUnitsController.cs` gọi `IStorageUnitKhoiVTHService` + `Views/StorageUnits/*.cshtml`), kết hợp SignalR realtime đồng bộ dữ liệu hai chiều.

#### Các bước thao tác demo:
1. **Đăng nhập:**
   - Vào `/Account/Login`, nhập `manager.d1@selfstorage.local` (hoặc `manager1@gmail.com`) / `SecretAdminPassword123!`.
2. **Thao tác MVC CRUD (`/StorageUnits`):**
   - Click nút **"Quản trị Ô kho (MVC/Razor)"** trên Navbar.
   - **Tìm kiếm & Phân trang:** Gõ mã kho vào ô tìm kiếm, lọc theo trạng thái (`Available`, `Occupied`, `Maintenance`) hoặc bấm chuyển trang (Pagination).
   - **Thêm mới (Create):** Nhấn **"Thêm mới ô kho"** -> Điền thông tin ô kho -> Bấm Lưu -> Danh sách cập nhật ngay.
   - **Chỉnh sửa (Edit):** Nhấn **"Sửa"** tại một ô kho -> Cập nhật thông số -> Bấm Cập nhật.
   - **Xóa (Delete):** Nhấn **"Xóa"** ô kho trống -> Xác nhận -> Ô kho biến mất khỏi danh sách.
   - **Báo cáo đặt chỗ (`/Reports/Reservations`):** Bấm nút **"Báo cáo Đặt chỗ"** ngay trên thanh Navbar để xem thống kê.
3. **Show tính năng Real-time SignalR (Điểm nhấn ăn điểm):**
   - Mở **2 cửa sổ trình duyệt song song** (1 bên trình duyệt thường đang login Manager, 1 bên Cửa sổ ẩn danh đang login Admin/Manager).
   - Ở cửa sổ 1, thao tác Thêm/Sửa/Xóa ô kho.
   - **Chỉ cho thầy thấy:** Cửa sổ 2 **tự động cập nhật ngay lập tức mà KHÔNG cần bấm F5**.
4. **Chứng minh Phân quyền bảo mật (Security):**
   - Đăng xuất, đăng nhập bằng `customer1@example.com`.
   - Bấm thử hoặc gõ URL `/StorageUnits` trên thanh địa chỉ.
   - **Kết quả:** Hệ thống chặn ngay lập tức và hiển thị **403 Forbidden / Từ chối truy cập**.

---

### 📌 2. ASM 02: ASP.NET Core Razor Pages + Nghiệp vụ Quầy
* **Trọng tâm công nghệ:** Triển khai theo mô hình Razor Pages (`Pages/StorageUnits/*.cshtml.cs`, `Pages/Staff/CheckIn/*.cshtml.cs`), nghiệp vụ check-in tiếp nhận khách tại quầy.

#### Các bước thao tác demo:
1. **Đăng nhập:** Bằng `manager.d1@selfstorage.local` (hoặc `staff.d1@selfstorage.local`) / `SecretAdminPassword123!`.
2. **Giao diện Razor Pages (`/StorageUnits`):**
   - Click nút **"Quản trị Ô kho (MVC/Razor)"** -> Giới thiệu cấu trúc `PageModel` với bộ lọc tìm kiếm và phân trang.
3. **Nghiệp vụ tiếp nhận tại quầy (`/Staff/CheckIn`):**
   - Click nút **"Tiếp nhận tại quầy (Check-in)"** ngay trên Navbar.
   - Tra cứu mã đặt chỗ `RES-DEMO-001` hoặc `RES-DEMO-002` để thực hiện quy trình xác nhận khách đến nhận kho và bàn giao.

---

### 📌 3. ASM 03: Blazor Server Interactive Component
* **Trọng tâm công nghệ:** Giao diện tương tác cao với Blazor Server (`Components/StorageUnitManager.razor` gọi `IStorageUnitKhoiVTHService` tại `/Blazor/StorageUnits`), render vi sai qua WebSocket mà không cần reload trang.

#### Các bước thao tác demo:
1. **Đăng nhập:** Bằng `admin@selfstorage.local` hoặc `manager1@gmail.com`.
2. **Giao diện Blazor Interactive:**
   - Bấm trực tiếp nút màu vàng **"⚡ Blazor StorageUnits (ASM03)"** trên thanh Navbar.
   - **Tìm kiếm trực tiếp (Live Search):** Gõ mã kho (ví dụ `A-10`) rồi bấm Tìm kiếm -> Bảng dữ liệu tự động lọc theo thời gian thực mà không reload trang.
   - **Thêm / Sửa / Xóa trực tiếp trên cùng 1 màn hình Blazor:** Bấm nút Tạo ô kho, Sửa, Xóa ngay trên component mà không chuyển trang.
   - **Mở F12 kiểm chứng:** Bật tab **Network -> WS (WebSocket)** -> Chỉ thấy kết nối `/_blazor` trao đổi các gói tin nhị phân siêu nhẹ.

---

### 📌 4. ASM 04: Console Worker / Windows Service (5s Heartbeat)
* **Trọng tâm công nghệ:** Ứng dụng chạy ngầm Background Service (`SelfStorageManagementSystem.Worker`), in log chu kỳ 5 giây và tích hợp kiểm tra dịch vụ hệ thống Windows Service.

#### Các bước thao tác demo:
1. **Chạy Console Worker in log 5s (Thao tác chính):**
   - Mở cửa sổ PowerShell tại thư mục `D:\PRN222\SelfStorageManagement_PRN222`.
   - Gõ lệnh:
     ```powershell
     .\run-worker.ps1
     ```
   - **Chỉ cho thầy xem console:** Mỗi đúng **5 giây**, màn hình sẽ tự động in ra dòng log:
     ```text
     [16:30:00 INF] ASM04 VOTHAIHOANGKHOI heartbeat #1 - Active worker alive
     [16:30:05 INF] ASM04 VOTHAIHOANGKHOI heartbeat #2 - Active worker alive
     [16:30:10 INF] ASM04 VOTHAIHOANGKHOI heartbeat #3 - Active worker alive
     ```
   - Nhấn `Ctrl + C` khi muốn dừng tiến trình.
2. **Kiểm tra Windows Service:**
   - Chạy lệnh:
     ```powershell
     .\check-service.ps1 -OpenServices
     ```
   - **Hiện tượng:** Cửa sổ **Services (`services.msc`)** của Windows sẽ tự động mở lên để kiểm tra dịch vụ `PRN222_VOTHAIHOANGKHOI_ASM04`.
