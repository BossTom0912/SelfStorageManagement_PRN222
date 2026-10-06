# Báo Cáo Sửa Lỗi: WPF ApiClient Gửi JWT Vào Request Catalog Công Khai

- **Dự án**: Self-Storage Facility Rental and Management System (PRN222)
- **Thời gian hoàn thành**: 06/10/2026
- **Branch**: `Account&RoleManagement`
- **File báo cáo**: `AgentReport/apiClientHeaderFix.md`

---

## 1. Nguyên Nhân Gốc Rễ (Root Cause Analysis)

### Hiện tượng phát hiện:
Sau khi đăng nhập thành công vào ứng dụng WPF, người dùng duyệt danh mục kho công khai (Catalog) thì các request gửi tới các endpoint `[AllowAnonymous]` (như `/api/facilities`, `/api/facilities/{id}/unit-types`, `/api/facilities/{id}/units/available`, `/api/facilities/{id}/floor-map`) vẫn đính kèm header `Authorization: Bearer <JWT>`.

### Nguyên nhân kỹ thuật:
1. **Chia sẻ singleton `HttpClient` với `DefaultRequestHeaders`**:
   `ApiClient` sử dụng một instance `HttpClient` dùng chung duy nhất (`_httpClient`). Trước đây, phương thức `EnsureAuthorizationHeader()` ghi đè trực tiếp Bearer token vào `_httpClient.DefaultRequestHeaders.Authorization`.
2. **Side-effect sau request có xác thực**:
   Sau khi đăng nhập, `LoginWindow` gọi `GetMeAsync()` với `requiresAuth: true`, khiến `_httpClient.DefaultRequestHeaders.Authorization` chứa JWT token của phiên làm việc.
3. **Cờ `requiresAuth: false` không xóa header mặc định**:
   Các phương thức catalog gọi `SendRequestAsync(..., requiresAuth: false)`. Khi `requiresAuth == false`, hàm trước đây chỉ đơn thuần bỏ qua bước gọi `EnsureAuthorizationHeader()`, nhưng **không hề xóa token đã nằm sẵn trong `DefaultRequestHeaders`**.
4. **Không thể xóa header trên `DefaultRequestHeaders` vì race condition**:
   Việc xóa `DefaultRequestHeaders.Authorization` khi `requiresAuth == false` không thể áp dụng vì `HttpClient` được chia sẻ; nếu nhiều async request chạy đồng thời (ví dụ tải catalog ngầm trong khi gọi API profile), việc sửa `DefaultRequestHeaders` sẽ gây race condition và lỗi thread-safety.
5. **Session dọn dẹp chưa triệt để khi nhận 401**:
   Khi API trả về 401 Unauthorized, `SessionStore.Clear()` chỉ xóa thông tin trong bộ nhớ của client nhưng không xóa `DefaultRequestHeaders.Authorization` cũ.

---

## 2. Giải Pháp và Chi Tiết Triển Khai

### 2.1. Thiết kế lại cơ chế gửi request trong `ApiClient.cs`
- **Ngừng lưu Authorization trong `HttpClient.DefaultRequestHeaders`**:
  - Xóa bỏ hoàn toàn phương thức `EnsureAuthorizationHeader()` và xóa lời gọi nó trong `Logout()`. Tuyệt đối không chạm vào `DefaultRequestHeaders` của `_httpClient`.
- **Tạo `HttpRequestMessage` riêng biệt cho từng request**:
  - Triển khai phương thức dispatch tập trung:
    ```csharp
    private async Task<ApiResponse<T>> SendRequestAsync<T>(
        HttpMethod method,
        string relativePathAndQuery,
        object? jsonBody = null,
        bool requiresAuth = true)
    ```
  - Trong mỗi lần gọi:
    1. Kiểm tra URL tuyệt đối HTTPS qua `ValidateBaseUrl`. Nếu không hợp lệ, trả lỗi ngay lập tức mà không phát sinh kết nối mạng.
    2. Khởi tạo `using var request = new HttpRequestMessage(method, requestUri)`.
    3. Gán `request.Content` (nếu có `jsonBody`).
    4. **Chỉ gán Authorization header cho request đó nếu và chỉ nếu**:
       `requiresAuth == true && !string.IsNullOrWhiteSpace(SessionStore.AccessToken)`
       -> `request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", SessionStore.AccessToken);`
    5. Khi `requiresAuth == false`, request hoàn toàn **không có header `Authorization`** (giá trị `null`).
    6. Gửi request qua `using var response = await _httpClient.SendAsync(request);` và đọc dữ liệu qua `HandleResponseAsync<T>(response)`. Cả `request` và `response` đều được giải phóng tài nguyên mạng sạch sẽ.
- **Hỗ trợ Dependency Injection cho Unit Testing**:
  - Bổ sung constructor `public ApiClient(HttpClient httpClient)` và `public ApiClient(HttpMessageHandler handler, bool disposeHandler = true)` cho phép tiêm mock handler để kiểm thử HTTP headers thực tế mà không phá vỡ singleton `ApiClient.Instance`.
- **Tương thích ngược cho các call site**:
  - Bổ sung overload `GetAvailableUnitsAsync` hỗ trợ cả 9 tham số và 11 tham số (kèm `minAreaM2`, `maxAreaM2`), đảm bảo tương thích hoàn toàn với mã nguồn catalog của dự án.

### 2.2. Điều chỉnh dự án kiểm thử `SelfStorageManagementSystem.Tests.csproj`
- Cập nhật `<TargetFramework>net8.0-windows</TargetFramework>` và `<UseWPF>true</UseWPF>`.
- Thêm `<ProjectReference>` tới `SelfStorageManagementSystem.WpfClient.csproj` và các using toàn cục `System.Net.Http`, `System.Net.Http.Json` để cho phép kiểm thử trực tiếp `ApiClient` và `SessionStore`.

### 2.3. Tạo mới bộ Unit Test HTTP Header thực tế `ApiClientAuthorizationHeaderTests.cs`
Sử dụng `RecordingHttpMessageHandler` chặn và kiểm tra trực tiếp snapshot của từng `HttpRequestMessage` được gửi ra:
1. `LoginAsync_Then_GetMeAsync_Then_GetCatalogFacilitiesAsync_VerifiesHeaderIntegrity`: Kiểm tra đúng chuỗi luồng:
   - Request #1 (`LoginAsync`): Header `Authorization` là `null`.
   - Request #2 (`GetMeAsync`): Header `Authorization` có Scheme `"Bearer"` và đúng token.
   - Request #3 (`GetCatalogFacilitiesAsync`): Header `Authorization` là `null` dù phiên đăng nhập vẫn đang hoạt động.
2. `LoginAsync_And_RegisterCustomerAsync_DoNotSendAuthorization_EvenIfPriorSessionExists`: Kiểm tra cả khi client đã có session lưu trước đó, request login và register không bao giờ gửi header `Authorization`.
3. `After401Unauthorized_SubsequentPublicRequestDoesNotSendOldToken`: Sau khi nhận 401 Unauthorized từ request có auth, token trong `SessionStore` được dọn sạch, request catalog công khai tiếp theo và request sau đó không gửi token cũ.
4. `AllCatalogEndpoints_DoNotSendAuthorization_WhenUserIsLoggedIn`: Xác nhận tất cả các endpoint catalog công khai (`GetCatalogFacilitiesAsync`, `GetFacilityUnitTypesAsync`, `GetAvailableUnitsAsync`, `GetFacilityFloorMapAsync`) đều không gửi Authorization header khi người dùng đã đăng nhập.
5. `InsecureHttpUrl_IsBlockedSynchronouslyBeforeSending`: Xác nhận URL HTTP bị chặn ngay tại client, không phát sinh bất kỳ request mạng nào (0 request).

### 2.4. Đính chính bảng tiến độ trong `README.md`
- Cập nhật dòng trạng thái của **Feature 2: Facility & Storage Unit Catalog** từ `"Not Implemented"` sang `"In Progress (Under Verification)"`.
- Ghi nhận chi tiết: repo đã có mã nguồn của Catalog Controller, DTOs, Repository/Service và giao diện WPF, nhưng đang trong giai đoạn hoàn thiện tích hợp và kiểm chứng, không tự nhận hoàn thành khi chưa nghiệm thu xong.

---

## 3. Kết Quả Build và Test Thực Tế

### Build Solution
```powershell
dotnet build SelfStorageManagementSystem.sln
```
- **Kết quả**: `Build succeeded. 0 Warning(s), 0 Error(s)`.

### Chạy Unit Test kiểm tra HTTP Header của ApiClient
```powershell
dotnet test tests/SelfStorageManagementSystem.Tests/SelfStorageManagementSystem.Tests.csproj --filter "FullyQualifiedName~ApiClientAuthorizationHeaderTests"
```
- **Kết quả**:
  ```text
  Passed!  - Failed: 0, Passed: 5, Skipped: 0, Total: 5, Duration: 106 ms
  ```

### Chạy toàn bộ Test Suite Chức Năng 1 & Catalog Integration
```powershell
dotnet test tests/SelfStorageManagementSystem.Tests/SelfStorageManagementSystem.Tests.csproj --filter "FullyQualifiedName!~FacilityCatalogServiceTests"
```
- **Kết quả**:
  ```text
  Passed!  - Failed: 0, Passed: 60, Skipped: 0, Total: 60, Duration: 1 s
  ```

---

## 4. Danh Sách File Đã Thay Đổi

| STT | Đường dẫn file | Trạng thái | Nội dung can thiệp |
| :---: | :--- | :---: | :--- |
| 1 | `src/SelfStorageManagementSystem.WpfClient/Services/ApiClient.cs` | Sửa đổi | Bỏ `EnsureAuthorizationHeader()`, cấp `HttpRequestMessage` độc lập theo từng request, gắn header theo `requiresAuth`, bổ sung constructors và overload. |
| 2 | `tests/SelfStorageManagementSystem.Tests/SelfStorageManagementSystem.Tests.csproj` | Sửa đổi | Cấu hình target `net8.0-windows`, tham chiếu `WpfClient`, thêm usings `System.Net.Http`. |
| 3 | `tests/SelfStorageManagementSystem.Tests/ApiClientAuthorizationHeaderTests.cs` | Thêm mới | 5 unit tests kiểm thử HTTP headers thực tế (Login -> Me -> Catalog, 401 handling, HTTP block). |
| 4 | `README.md` | Sửa đổi | Cập nhật Feature 2 sang `In Progress (Under Verification)`. |
| 5 | `AgentReport/apiClientHeaderFix.md` | Thêm mới | Báo cáo chi tiết quá trình sửa lỗi và bằng chứng kiểm thử. |
