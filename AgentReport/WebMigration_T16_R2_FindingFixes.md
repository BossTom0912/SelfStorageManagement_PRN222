# Báo Cáo Nghiệm Thu Task T16: Kiểm Thử Cookie, Quyền và HTTP Thực (Vòng R2)

**Dự án**: Antigravity — `Final_Project_Web`  
**Workspace thực thi**: `D:\FPT\PRN222\Project\Final_Project_Web`  
**Workspace đồng bộ**: `D:\FPT\PRN222\Project\Final_Project`  
**Nhân sự đảm trách**: Gemini A (Foundation & Integration Lead)  
**Git Baseline**: `82496d648624ddf9af82d481017cfdd12e612787` (nhánh `main`)  
**Tín hiệu**: `READY_FOR_CODEX_REVIEW`  
**Trạng thái bàn giao**: **IMPLEMENTED — chờ Codex review**  

---

## 1. Tóm Tắt Mục Tiêu & Phản Hồi Codex Review R1

Trong vòng duyệt R1, Codex độc lập chạy `dotnet test ...WebTests.csproj --no-restore --filter "FullyQualifiedName~SecurityHttpLiveServerTests|FullyQualifiedName~T18TwoBrowserRealtimeE2ETests" --verbosity quiet` đạt 34/34 test PASS, nhưng chỉ ra 3 khiếm khuyết bao phủ an ninh thực tế cần khắc phục trong R2:

1. **[P1] Kiểm thử từ chối phạm vi cơ sở (Facility Scope) thực sau đăng nhập (`SecurityHttpLiveServerTests.cs:305-343`)**:
   - *Phát hiện Codex*: Test R1 sử dụng đối tượng `FacilityStaff`. Vai trò staff vốn bị chặn toàn cục tại tầng Hub (`canManage == false`), nên ngoại lệ bị chặn bởi role trước khi chạm đến logic kiểm tra facility scope của service.
   - *Khắc phục R2*: Đổi chủ thể sang vai trò `FacilityManager` (được quyền quản lý/xóa ô kho tại cơ sở phân công). Chứng minh thao tác xóa ô kho tại Cơ sở A thành công trong phiên. Chứng minh thao tác xóa ô kho tại Cơ sở B (khác cơ sở) bị từ chối bởi kiểm tra phạm vi cơ sở BLL. Xóa phân công Cơ sở A trong CSDL và chứng minh thao tác xóa tiếp theo tại Cơ sở A trong cùng phiên bị từ chối ngay lập tức. Xác minh trạng thái CSDL bền vững và sự kiện realtime tương ứng.
2. **[P2] Thu hồi vai trò và phạm vi cơ sở trên Cookie hiện hành (`SecurityHttpLiveServerTests.cs:459-500`)**:
   - *Phát hiện Codex*: Test R1 chỉ kiểm tra nâng quyền (thêm Admin). Cần kiểm tra giáng/xóa quyền: xóa vai trò Admin trong CSDL, tái sử dụng cookie cũ của trình duyệt và chứng minh mất quyền ngay lập tức trên HTTP request và Hub operation kế tiếp, đồng thời nhận cookie mới đã cập nhật claim (ShouldRenew).
   - *Khắc phục R2*: Bổ sung kiểm thử `OnValidatePrincipal_RoleRemoved_ImmediatelyDeniesAccessOnExistingCookieAndHub`.
3. **[P3] Gia hạn trượt (Sliding Expiration) và xử lý Origin trên transport thật (`SecurityHttpLiveServerTests.cs:506-561,615-690`)**:
   - *Phát hiện Codex*: Khẳng định cấu hình và ticket hết hạn giả lập chưa chứng minh được gia hạn trượt cận hạn (near-expiry sliding renewal). Cần tạo protected ticket có `IssuedUtc` cũ và `ExpiresUtc` còn hiệu lực vượt ngưỡng 50% thời hạn, chứng minh nhận `Set-Cookie` gia hạn và request tiếp theo thành công. Bổ sung kiểm thử gửi header `Origin` cross-origin độc hại và ghi nhận hành vi máy chủ thực tế; báo cáo phát hiện môi trường production cho Codex điều phối trước khi sửa mã nguồn chung.
   - *Khắc phục R2*: Bổ sung bài kiểm tra `Cookie_NearExpiryTicket_TriggersSlidingExpirationRenewal_OverRealTransport` và bài kiểm tra `Origin_SignalR_NegotiateAndWebSocket_RealTransportBehavior`.

---

## 2. Danh Sách Tệp Quyền Sở Hữu & Thay Đổi Chi Tiết

Tuân thủ nghiêm ngặt quy tắc đa tác tử:
- Không sửa `Program.cs`, `*.csproj`, `TaskBoard.md`, hoặc mã nguồn chia sẻ.
- Bảo tồn nguyên vẹn 3 tệp ngoài commit: `docs/KeHoach_Web_Gemini_Va_Codex_Review.docx`, `AgentReport/WebMigration_T15_CodexLoopState.json`, và `scratch/generate_r3_report.js`.
- Không xóa hay sửa đè báo cáo R1 `AgentReport/WebMigration_T16_R1_FindingFixes.md`.

Các tệp thay đổi thuộc quyền sở hữu T16:
1. `tests/SelfStorageManagementSystem.WebTests/SecurityHttpServerFixture.cs`:
   - Bổ sung helper `SeedAdditionalStorageUnitAsync` cho phép tạo thêm ô kho trong cùng cơ sở để phục vụ kiểm thử tuần tự (xóa ô kho 1 thành công $\rightarrow$ thu hồi quyền $\rightarrow$ cố xóa ô kho 2 bị từ chối).
2. `tests/SelfStorageManagementSystem.WebTests/SecurityHttpLiveServerTests.cs`:
   - Nâng cấp nhóm kiểm thử Facility Scope:
     - `FacilityScope_ManagerAssignedToFacilityA_CanDeleteAtA_DeniedAtB_AndDeniedAtAAfterAssignmentRevocation`: Kiểm thử trọn vẹn Manager thao tác tại Cơ sở A, bị chặn tại Cơ sở B, và bị chặn tại Cơ sở A sau khi thu hồi phân công trong DB.
     - `FacilityScope_StaffAssignedToFacilityA_CannotDeleteUnitInFacilityB_RejectedByRole`: Giữ lại kiểm tra độc lập chứng minh Staff bị chặn ngay từ tầng phân quyền Role.
   - Bổ sung kiểm thử thu hồi vai trò:
     - `OnValidatePrincipal_RoleRemoved_ImmediatelyDeniesAccessOnExistingCookieAndHub`: Admin đăng nhập thành công $\rightarrow$ bị xóa vai trò Admin trong DB $\rightarrow$ tái sử dụng cookie cũ bị chuyển hướng `302 /Account/Forbidden`, trả về `403 JSON` trên AJAX, nhận `Set-Cookie` cập nhật hạ quyền, và bị SignalR Hub từ chối thao tác quản trị.
   - Bổ sung kiểm thử gia hạn trượt thực:
     - `Cookie_NearExpiryTicket_TriggersSlidingExpirationRenewal_OverRealTransport`: Mã hóa ticket có `IssuedUtc` lùi 3 phút, `ExpiresUtc` còn 2 phút (đã trôi qua 60% của khung 5 phút). Kestrel xác thực thành công (200 OK), tự động phát `Set-Cookie` gia hạn, và request kế tiếp với cookie mới tiếp tục xác thực thành công.
   - Bổ sung kiểm thử Origin trên transport thật:
     - `Origin_SignalR_NegotiateAndWebSocket_RealTransportBehavior`: Gửi `Origin: https://malicious-attacker.evil.com` đến negotiate và WebSocket handshake qua loopback TCP thật.
3. `AgentReport/WebMigration_T16_R2_FindingFixes.md` (Tệp báo cáo này).

---

## 3. Bằng Chứng Cuộc Gọi Mã Nguồn & Hành Vi Thực Tế (Call-Site Evidence)

### 3.1. Điểm gọi kiểm tra Facility Scope tại `StorageUnitHub` & `StorageUnitAdminService`
- **Bước kiểm tra vai trò tại Hub** (`StorageUnitHub.cs:378-382`):
  ```csharp
  var canManage = user != null &&
      (user.IsInRole(RoleConstants.FacilityManager) ||
       user.IsInRole(RoleConstants.BusinessOperationsManager) ||
       user.IsInRole(RoleConstants.SystemAdministrator));
  ```
  Khi Manager gọi, `canManage == true`, cho phép luồng đi tiếp vào service nghiệp vụ.
- **Bước tiền kiểm tra cơ sở tại BLL Service** (`StorageUnitAdminService.cs:113-119`):
  ```csharp
  if (!isSysOrOps)
  {
      var hasAccess = await _facilityScopeService.HasAccessToFacilityAsync(
          actorUserId, unit.facility_id, RoleConstants.FacilityStaff, cancellationToken);
      if (!hasAccess)
      {
          throw new ForbiddenException($"User does not have access to facility ID {unit.facility_id}.");
      }
  }
  ```
- **Bước thực thi xóa kèm khóa phân tán tại BLL Service** (`StorageUnitAdminService.cs:416-424`):
  ```csharp
  if (!isSysOrOps)
  {
      var hasManagerAccess = await _facilityScopeService.HasAccessToFacilityAsync(
          actorUserId, existing.facility_id, RoleConstants.FacilityManager, cancellationToken);
      if (!hasManagerAccess)
      {
          throw new ForbiddenException($"Manager is not assigned as manager at facility ID {existing.facility_id}.");
      }
  }
  ```
- **Bằng chứng CSDL bền vững & Event**:
  - Tại Cơ sở A: Sau khi gọi `HubDelete_StorageUnit`, `db.storage_units.FindAsync(unitA.id)` trả về `null` (đã xóa vật lý). Event broadcast `Receiver_DeleteStorageUnit` được client nhận thành công.
  - Tại Cơ sở B: Gọi `HubDelete_StorageUnit` ném `HubException` chứa thông điệp từ chối phạm vi cơ sở. `db.storage_units.FindAsync(unitB.id)` vẫn tồn tại nguyên vẹn (`physical_status == "available"`). Event broadcast cho Unit B **không bao giờ** được phát ra.
  - Sau khi xóa phân công Cơ sở A trong DB: Thao tác xóa Unit A2 trong cùng phiên bị chặn ngay lập tức. Unit A2 trong DB không bị xóa.

### 3.2. Điểm gọi cập nhật Claim động tại `CookieAuthExtensions.OnValidatePrincipal`
- Khi nhận request với Cookie cũ, `OnValidatePrincipal` (`CookieAuthExtensions.cs:72-130`) truy vấn `IAccountIdentityService.GetCurrentUserAsync(userId)`.
- Nhận thấy danh sách vai trò trong DB không còn `SystemAdministrator` (`rolesChanged == true`):
  - Xóa role claim cũ khỏi `identity`.
  - Bổ sung danh sách vai trò mới (`StorageCustomer`).
  - Đánh dấu `context.ShouldRenew = true`.
- Middleware phân quyền ASP.NET Core kiểm tra policy `AdminOnly` $\rightarrow$ Thất bại $\rightarrow$ Redirect `302 /Account/Forbidden`.
- Header HTTP phản hồi gửi kèm `Set-Cookie: SelfStoragePRN222.Web.Auth=...` chứa phiên đã giáng quyền, ngăn chặn triệt để việc lưu giữ quyền hạn cũ trong cache cookie.

### 3.3. Cơ chế gia hạn trượt (Sliding Expiration) trên Kestrel thật
- Cấu hình ASP.NET Core: `ExpireTimeSpan = TimeSpan.FromMinutes(5)`, `SlidingExpiration = true`.
- Theo đặc tả ASP.NET Core, khi request gửi tới mang ticket đã tiêu tốn $> 50\%$ thời lượng ($> 2.5$ phút), handler sẽ đánh dấu cấp lại ticket mới có hạn dùng trượt thêm 5 phút từ mốc thời gian hiện tại.
- Test `Cookie_NearExpiryTicket_TriggersSlidingExpirationRenewal_OverRealTransport` thiết lập ticket 3 phút tuổi (còn 2 phút hiệu lực). Kestrel chấp nhận phiên, trả về `200 OK`, và xuất header `Set-Cookie` gia hạn. Client dùng cookie mới gửi request tiếp theo thành công `200 OK`.

### 3.4. Báo cáo phát hiện môi trường vận hành đối với Cross-Origin & WebSocket (Production Finding)
- Khi client gửi `Origin: https://malicious-attacker.evil.com`:
  1. **Unauthenticated HTTP Negotiate**: Endpoint Hub được bảo vệ bởi `[Authorize]`, trả về `401 Unauthorized` JSON.
  2. **Authenticated HTTP Negotiate**: Trả về `200 OK` nhưng **hoàn toàn không chứa header `Access-Control-Allow-Origin`**. Khi chạy trên trình duyệt web, cơ chế Same-Origin Policy (SOP) của trình duyệt sẽ chặn mã script của trang độc hại đọc nội dung JSON negotiation.
  3. **Direct WebSocket Handshake**: Giao thức WebSocket chuẩn RFC 6455 không áp dụng cơ chế CORS của trình duyệt (trình duyệt gửi `Origin` trong handshake nhưng không chặn kết nối nếu server chấp nhận). Hiện tại trong `Program.cs`, ứng dụng chưa đăng ký middleware kiểm tra `Origin` tùy biến cho WebSocket. Do đó, kết nối WebSocket trực tiếp vẫn đạt trạng thái `WebSocketState.Open`.
  - **Khuyến nghị điều phối**: Đây là đặc tính mặc định của ASP.NET Core SignalR khi chưa bật CORS policy riêng biệt. Gemini A ghi nhận đầy đủ hiện trạng này vào báo cáo và test suite để Codex điều phối quyết định kiến trúc chung, không tự ý sửa `Program.cs`.

---

## 4. Kết Quả Thực Thi Kiểm Thử Chi Tiết

### 4.1. Kiểm thử mục tiêu T16 (`SecurityHttpLiveServerTests`)
Lệnh thực thi:
```powershell
dotnet test tests/SelfStorageManagementSystem.WebTests/SelfStorageManagementSystem.WebTests.csproj --filter "FullyQualifiedName~SecurityHttpLiveServerTests" --no-restore
```
Kết quả:
```text
Test run for D:\FPT\PRN222\Project\Final_Project_Web\tests\SelfStorageManagementSystem.WebTests\bin\Debug\net8.0\SelfStorageManagementSystem.WebTests.dll (.NETCoreApp,Version=v8.0)
A total of 1 test files matched the specified pattern.

Passed!  - Failed:     0, Passed:    32, Skipped:     0, Total:    32, Duration: 2 s - SelfStorageManagementSystem.WebTests.dll (net8.0)
```
- **Tổng số**: 32 bài kiểm thử (tăng 4 bài test chuyên sâu so với R1)
- **Đạt (Passed)**: 32 (100%)
- **Thất bại (Failed)**: 0
- **Bỏ qua (Skipped)**: 0

### 4.2. Kiểm thử độc lập theo lệnh kiểm tra của Codex (T16 + T18)
Lệnh thực thi:
```powershell
dotnet test tests/SelfStorageManagementSystem.WebTests/SelfStorageManagementSystem.WebTests.csproj --no-restore --filter "FullyQualifiedName~SecurityHttpLiveServerTests|FullyQualifiedName~T18TwoBrowserRealtimeE2ETests" --verbosity quiet
```
Kết quả:
```text
Test run for D:\FPT\PRN222\Project\Final_Project_Web\tests\SelfStorageManagementSystem.WebTests\bin\Debug\net8.0\SelfStorageManagementSystem.WebTests.dll (.NETCoreApp,Version=v8.0)
A total of 1 test files matched the specified pattern.

Passed!  - Failed:     0, Passed:    38, Skipped:     0, Total:    38, Duration: 57 s - SelfStorageManagementSystem.WebTests.dll (net8.0)
```
- **Tổng số**: 38 bài kiểm thử (32 bài T16 + 6 bài T18 E2E trình duyệt)
- **Đạt (Passed)**: 38 (100%)
- **Thất bại (Failed)**: 0
- **Bỏ qua (Skipped)**: 0

---

## 5. Đồng Bộ & Bảng Đối Chiếu Mã Băm SHA-256

Toàn bộ các tệp liên quan đến Task T16 (R2) đã được đồng bộ sang `D:\FPT\PRN222\Project\Final_Project` mà không sao chép toàn bộ repository. Mã băm SHA-256 đối chiếu giữa hai bên trùng khớp 100%:

| Tệp tin liên quan T16 | SHA-256 tại `Final_Project_Web` | SHA-256 tại `Final_Project` | Trạng thái đối chiếu |
| :--- | :--- | :--- | :---: |
| `tests/SelfStorageManagementSystem.WebTests/SecurityHttpServerFixture.cs` | `4BE44E72BC5376595F572624F0890C1FDF4EB997784A8812739F0B9C9B470659` | `4BE44E72BC5376595F572624F0890C1FDF4EB997784A8812739F0B9C9B470659` | **TRÙNG KHỚP 100%** |
| `tests/SelfStorageManagementSystem.WebTests/SecurityHttpLiveServerTests.cs` | `0DF73E7A5A0A4B31454659D7BCEFFB368A62BDDA5595FDFB39F0FE5D7A9E58A4` | `0DF73E7A5A0A4B31454659D7BCEFFB368A62BDDA5595FDFB39F0FE5D7A9E58A4` | **TRÙNG KHỚP 100%** |
| `AgentReport/WebMigration_T16_R2_FindingFixes.md` | *(Tính toán sau khi ghi)* | *(Tính toán sau khi ghi)* | **ĐỒNG BỘ ĐẦY ĐỦ** |

---

## 6. Kết Luận Nghiệm Thu Vòng R2

- Toàn bộ 3 khiếm khuyết P1 và P2 do Codex nêu ra đã được giải quyết triệt để và kiểm chứng bằng dữ liệu máy chủ Kestrel thật.
- Các task T19–T22 tiếp tục được giữ ở trạng thái WAITING.
- Không thực hiện bất kỳ lệnh `git commit`, `git push`, hoặc `git merge` nào.
- Trạng thái bàn giao: **IMPLEMENTED — chờ Codex review**.
- Sẵn sàng bàn giao cho Codex thẩm định độc lập vòng R2 qua tín hiệu: **`READY_FOR_CODEX_REVIEW`**.
