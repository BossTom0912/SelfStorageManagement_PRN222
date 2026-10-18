# Báo Cáo Xử Lý Finding T14: HTTP 404 Cho Chi Tiết Bàn Giao

- **Mã Task**: T14 (Finding Fixes — T14-F01)
- **Tên Finding**: T14-F01 — Trả HTTP 200 cho hợp đồng không thuộc khách hàng (P2)
- **Đợt (Wave)**: 5
- **Owner**: Gemini C (Owner T14)
- **Trạng thái**: `READY_FOR_CODEX_REVIEW`
- **Không gian làm việc chính**: `D:\FPT\PRN222\Project\Final_Project_Web`
- **Không gian làm việc đồng bộ**: `D:\FPT\PRN222\Project\Final_Project`
- **Solution Web**: `SelfStorageManagementSystem.Web.sln`

---

## 1. Nguyên Nhân Gốc (Root Cause)

1. **Cơ chế trả về của Razor Pages**:
   - Trong `src/SelfStorageManagementSystem.Web/Pages/MyUnits/HandoverDetails.cshtml.cs` (dòng 43–47 và 57–63): Khi `agreementId <= 0` hoặc khi `_checkInService.GetCustomerHandoverAsync` ném `NotFoundException` (do hợp đồng không tồn tại hoặc không thuộc quyền sở hữu của khách hàng), handler gán `IsNotFoundOrForbidden = true` và `return Page()`.
   - Trong ASP.NET Core Razor Pages, `return Page()` theo mặc định thiết lập HTTP Status Code là `200 OK` nếu không gán rõ ràng thuộc tính `Response.StatusCode`.
2. **Sai sót trong bộ test ban đầu**:
   - Test case `Customer_CrossCustomer_HandoverDetails_CannotAccessOtherCustomerData` trong `tests/SelfStorageManagementSystem.WebTests/AdminAccountsAndCustomerPagesTests.cs` (dòng 823) assert nhầm `Assert.Equal(HttpStatusCode.OK, response.StatusCode);`, khiến hành vi sai lệch này bị bỏ qua trong lần nghiệm thu trước.

---

## 2. Chi Tiết Các Tệp và Dòng Sửa Đổi

### 2.1. `src/SelfStorageManagementSystem.Web/Pages/MyUnits/HandoverDetails.cshtml.cs`
- **Dòng 35**: Khai báo hằng số thông điệp an toàn thống nhất:
  ```csharp
  public const string SafeNotFoundErrorMessage = "Không tìm thấy thông tin bàn giao cho hợp đồng được yêu cầu hoặc hợp đồng không thuộc quyền sở hữu của bạn.";
  ```
- **Dòng 42–49**: Xử lý `agreementId <= 0`:
  ```csharp
  if (agreementId <= 0)
  {
      IsNotFoundOrForbidden = true;
      ErrorMessage = SafeNotFoundErrorMessage;
      Response.StatusCode = StatusCodes.Status404NotFound;
      return Page();
  }
  ```
- **Dòng 56–72**: Bắt `NotFoundException` và `ForbiddenException`:
  ```csharp
  catch (NotFoundException)
  {
      IsNotFoundOrForbidden = true;
      ErrorMessage = SafeNotFoundErrorMessage;
      Response.StatusCode = StatusCodes.Status404NotFound;
      return Page();
  }
  catch (ForbiddenException)
  {
      IsNotFoundOrForbidden = true;
      ErrorMessage = SafeNotFoundErrorMessage;
      Response.StatusCode = StatusCodes.Status404NotFound;
      return Page();
  }
  ```
- **Dòng 73–80**: Bắt `Exception` chung: Thiết lập `Response.StatusCode = StatusCodes.Status500InternalServerError; return Page();`.
- **Đặc điểm giải pháp**:
  - Render giao diện thân thiện với card thông báo lỗi rõ ràng và nút "Quay Lại Danh Sách Ô Kho".
  - Mã trạng thái HTTP trả về chính xác là `404 Not Found`.
  - Giữ nguyên thông điệp an toàn thống nhất cho cả 3 trường hợp (ID không hợp lệ, hợp đồng không tồn tại, hợp đồng của khách hàng khác) để phía người gọi hoàn toàn không thể dò đoán sự tồn tại của bản ghi.
  - Không thay đổi nghiệp vụ kiểm tra quyền sở hữu trong BLL/DAL.

### 2.2. `tests/SelfStorageManagementSystem.WebTests/AdminAccountsAndCustomerPagesTests.cs`
- **Dòng 820–832**: Cập nhật test `Customer_CrossCustomer_HandoverDetails_CannotAccessOtherCustomerData`:
  - Thay đổi assertion từ `HttpStatusCode.OK` thành `Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);`.
- **Dòng 834–850**: Bổ sung test `Customer_HandoverDetails_NonExistentAgreementId_ReturnsNotFound`:
  - Kiểm tra khách hàng truy cập ID không tồn tại (`999999999L`) nhận HTTP 404 và thông điệp an toàn chuẩn hóa.
- **Dòng 851–870**: Bổ sung `[Theory]` test `Customer_HandoverDetails_InvalidAgreementId_ReturnsNotFound`:
  - Kiểm tra các ID không hợp lệ (`0`, `-1`, `-999`) nhận HTTP 404 và thông điệp an toàn chuẩn hóa.

---

## 3. Bằng Chứng HTTP Thực Tế & Đảm Bảo An Toàn Dữ Liệu

| Trường Hợp Truy Cập | HTTP Status Code Trước Sửa | HTTP Status Code Sau Sửa | Nội Dung Trả Về / Dữ Liệu Hiển Thị | Đảm Bảo Bảo Mật |
| :--- | :---: | :---: | :--- | :--- |
| **Khách hàng A xem hợp đồng của chính mình** | `200 OK` | `200 OK` | Đầy đủ metadata hợp đồng, ô kho, checklist kiểm định, thời hạn truy cập; zero plaintext PIN. | Dữ liệu chính chủ, an toàn. |
| **Khách hàng B truy cập hợp đồng của A** | `200 OK` ❌ | `404 Not Found`  | Giao diện báo lỗi thân thiện; **không có bất kỳ thông tin nào của Khách hàng A hay ô kho của A bị rò rỉ**. | Ngăn chặn rò rỉ quyền sở hữu; kẻ tấn công không biết ID đó có tồn tại hay không. |
| **Khách hàng truy cập ID không tồn tại** (`999999999`) | `200 OK` ❌ | `404 Not Found`  | Giao diện báo lỗi thân thiện với thông điệp an toàn giống hệt trường hợp truy cập chéo. | Đồng nhất hành vi với trường hợp truy cập chéo. |
| **Khách hàng truyền ID không hợp lệ** (`0`, `-1`) | `200 OK` ❌ | `404 Not Found`  | Giao diện báo lỗi thân thiện với thông điệp an toàn giống hệt. | Chặn sớm từ đầu handler, không gây 500 hay leak stack trace. |

---

## 4. Kết Quả Kiểm Thử Thực Tế

### 4.1. Bộ kiểm thử T14 (`AdminAccountsAndCustomerPagesTests`)
- **Lệnh thực thi**:
  ```bash
  dotnet test "tests/SelfStorageManagementSystem.WebTests/SelfStorageManagementSystem.WebTests.csproj" --filter "FullyQualifiedName~AdminAccountsAndCustomerPagesTests"
  ```
- **Kết quả**:
  ```text
  Passed!  - Failed: 0, Passed: 36, Skipped: 0, Total: 36, Duration: 6 s
  ```
  *(Tăng từ 32 lên 36 test cases do bổ sung thêm 4 kịch bản kiểm tra HTTP 404 cho ID không tồn tại và ID không hợp lệ).*

### 4.2. Kiểm thử hồi quy toàn bộ WebTests
- **Lệnh thực thi**:
  ```bash
  dotnet test "tests/SelfStorageManagementSystem.WebTests/SelfStorageManagementSystem.WebTests.csproj" --filter "FullyQualifiedName!~StorageUnitRealtimeHubTests"
  ```
- **Kết quả**:
  ```text
  Passed!  - Failed: 0, Passed: 310, Skipped: 0, Total: 310, Duration: 21 s
  ```

### 4.3. Kiểm thử Realtime Hubs
- **Lệnh thực thi**:
  ```bash
  dotnet test "tests/SelfStorageManagementSystem.WebTests/SelfStorageManagementSystem.WebTests.csproj" --filter "FullyQualifiedName~StorageUnitRealtimeHubTests"
  ```
- **Kết quả**:
  ```text
  Passed!  - Failed: 0, Passed: 14, Skipped: 0, Total: 14, Duration: 1 m 3 s
  ```
- **Tổng cộng**: **324/324 tests PASS (100%)**, zero regression trên toàn hệ thống Web.

---

## 5. Bảng Đối Chiếu Đồng Bộ Mã Nguồn (SHA-256)

Toàn bộ các tệp thay đổi cho T14-F01 đã được đồng bộ chính xác từ `Final_Project_Web` sang `Final_Project`, không ảnh hưởng hay ghi đè lên các tệp của T12/T13:

| Đường dẫn tệp | SHA-256 (`Final_Project_Web`) | SHA-256 (`Final_Project`) | Trạng thái khớp |
| :--- | :--- | :--- | :---: |
| `src/SelfStorageManagementSystem.Web/Pages/MyUnits/HandoverDetails.cshtml.cs` | `A044C81BF885A136F3835710CC76D2395496FA6579D8B7DC29D4E155A9A39D83` | `A044C81BF885A136F3835710CC76D2395496FA6579D8B7DC29D4E155A9A39D83` | **KHỚP 100%** |
| `tests/SelfStorageManagementSystem.WebTests/AdminAccountsAndCustomerPagesTests.cs` | `1F7B008F09F7ED1DC8C93AECD8DF058C6A86F4BECBE01854B70C23C42B5700F5` | `1F7B008F09F7ED1DC8C93AECD8DF058C6A86F4BECBE01854B70C23C42B5700F5` | **KHỚP 100%** |

---

## 6. Đánh Giá Rủi Ro Còn Lại (Remaining Risks)

- **Rủi ro hồi quy**: Không có. Chỉ sửa đổi logic gán mã HTTP 404 cho các luồng lỗi tại trang Razor Page `HandoverDetails` và mở rộng các test cases kiểm chứng HTTP status.
- **Rủi ro rò rỉ dữ liệu**: Đã được triệt tiêu hoàn toàn. Phía giao diện hiển thị cùng một thông điệp lỗi và trả về HTTP 404 cho mọi trường hợp lỗi truy cập, không có dữ liệu hợp đồng hay cơ sở nào của khách hàng khác bị hiển thị.

---

## 7. Kết Luận

Finding T14-F01 đã được khắc phục hoàn chỉnh, kiểm thử nghiêm ngặt đạt 100% PASS và đồng bộ chính xác sang cả hai không gian làm việc.

`READY_FOR_CODEX_REVIEW`
