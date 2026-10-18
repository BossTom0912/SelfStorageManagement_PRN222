# Báo Cáo Nghiệm Thu Task T16: Kiểm Thử Cookie, Quyền và HTTP Thực (Vòng R3)

**Dự án**: Antigravity — `Final_Project_Web`  
**Workspace thực thi**: `D:\FPT\PRN222\Project\Final_Project_Web`  
**Workspace đồng bộ**: `D:\FPT\PRN222\Project\Final_Project`  
**Nhân sự đảm trách**: Gemini A (Foundation & Integration Lead)  
**Git Baseline**: `82496d648624ddf9af82d481017cfdd12e612787` (nhánh `main`)  
**Tín hiệu bàn giao**: `READY_FOR_CODEX_REVIEW`  
**Trạng thái bàn giao**: **IMPLEMENTED — chờ Codex review**  

---

## 1. Tóm Tắt Mục Tiêu & Phản Hồi Codex Review R2

Trong vòng duyệt R2, Codex độc lập chạy bộ kiểm thử T16 đạt 32/32 test PASS và đánh giá phạm vi thu hồi vai trò/scope và gia hạn trượt đã đạt yêu cầu. Tuy nhiên, Codex chỉ ra 2 nội dung cần hoàn thiện trong R3:

1. **[P1] Từ chối WebSocket Origin không tin cậy thay vì khóa kết quả chấp nhận vào passing test (`SecurityHttpLiveServerTests.cs:963-993`)**:
   - *Phát hiện Codex*: Bài test R2 ghi nhận `ClientWebSocket` đạt trạng thái `Open` khi gửi `Origin: https://malicious-attacker.evil.com` do `Program.cs` chưa có cổng chặn Origin. Điều này tiềm ẩn nguy cơ một trang web khác lợi dụng cookie phiên của người dùng để mở kết nối WebSocket tới Hub và thực hiện thao tác thay đổi dữ liệu.
   - *Khắc phục R3*: Thiết kế và bổ sung cổng kiểm soát Origin có phạm vi rõ ràng tại endpoint WebSocket của Hub (`UseStorageUnitHubOriginRestriction`). Áp dụng chính sách cho phép Same-Origin rõ ràng, hỗ trợ client không mang Origin (non-browser), và từ chối dứt điểm (`403 Forbidden`) các Origin lạ/độc hại trên cả HTTP Negotiate và WebSocket handshake RFC 6455. Đồng bộ cấu hình vào `Program.cs` và `SecurityHttpServerFixture.cs`. Bổ sung kiểm thử độc lập cho Negotiate, WebSocket bị từ chối (kèm xác minh CSDL không bị thay đổi / không phát broadcast), WebSocket cùng nguồn được chấp nhận, và client không mang Origin được chấp nhận.
2. **[P2] Hiệu chỉnh và đối chiếu mã băm SHA-256 thực tế giữa hai workspace (`AgentReport/WebMigration_T16_R2_FindingFixes.md`)**:
   - *Phát hiện Codex*: Bảng hash R2 có độ lệch so với tệp thực tế do mốc thời gian lưu tệp cuối.
   - *Khắc phục R3*: Giữ nguyên báo cáo R2 làm đầu vào bất biến đã được review. Trong báo cáo R3 này, tính toán và đối chiếu mã băm SHA-256 thực tế cuối cùng của tất cả các tệp thay đổi trên cả hai workspace `Final_Project_Web` và `Final_Project` sau khi hoàn tất mọi sửa đổi.

---

## 2. Danh Sách Tệp Quyền Sở Hữu & Thay Đổi Trong Vòng R3

1. `src/SelfStorageManagementSystem.Web/Extensions/HubOriginSecurityExtensions.cs` *(Tạo mới)*:
   - Xây dựng extension method `UseStorageUnitHubOriginRestriction` và helper `IsAllowedHubOrigin`.
   - Kiểm tra `context.Request.Path.StartsWithSegments("/storageUnitHub")`.
   - Quy tắc an ninh:
     - Cho phép kết nối Same-Origin (`originUri.Authority == request.Host.Value`).
     - Cho phép tương đương loopback (`localhost` và `127.0.0.1` trên cùng cổng).
     - Cho phép client không mang header `Origin` (desktop app, internal tools).
     - Chặn các Origin không tin cậy với mã trạng thái `403 Forbidden`.
2. `src/SelfStorageManagementSystem.Web/Program.cs` *(Sửa đổi theo sự cho phép của Codex)*:
   - Kích hoạt `app.UseStorageUnitHubOriginRestriction()` ngay trước `app.MapHub<StorageUnitHub>("/storageUnitHub")`.
   - Giữ nguyên toàn bộ route, tên Hub và Receiver contracts.
3. `tests/SelfStorageManagementSystem.WebTests/SecurityHttpServerFixture.cs` *(Sửa đổi)*:
   - Tích hợp `_app.UseStorageUnitHubOriginRestriction()` trước `_app.MapHub<StorageUnitHub>("/storageUnitHub")` để bảo đảm môi trường kiểm thử Kestrel thật chạy chính sách an ninh đồng nhất 100% với môi trường production.
4. `tests/SelfStorageManagementSystem.WebTests/SecurityHttpLiveServerTests.cs` *(Sửa đổi)*:
   - Tách biệt và mở rộng 4 ca kiểm thử Origin độc lập:
     - `Origin_UntrustedOrigin_SignalR_Negotiate_Returns403Forbidden`: Kiểm thử HTTP POST Negotiate với Origin độc hại $\rightarrow$ Nhận `403 Forbidden` JSON.
     - `Origin_UntrustedOrigin_WebSocketHandshake_IsRejected_NoMutationOrBroadcast`: Kiểm thử direct WebSocket handshake với cookie xác thực hợp lệ nhưng mang Origin độc hại $\rightarrow$ Kestrel từ chối handshake với mã `403 Forbidden`, `ClientWebSocket` ném `WebSocketException`, trạng thái không bao giờ đạt `Open`. Dữ liệu ô kho trong CSDL không bị thay đổi và không có broadcast nào được phát ra.
     - `Origin_LegitimateSameOrigin_WebSocketHandshake_IsAccepted`: WebSocket handshake cùng nguồn (`Origin: _fixture.ServerUrl`) $\rightarrow$ Chấp nhận kết nối, đạt `WebSocketState.Open`.
     - `Origin_NoOriginHeader_DirectClient_IsAccepted`: Client không mang header Origin $\rightarrow$ Chấp nhận kết nối, đạt `WebSocketState.Open`.
5. `AgentReport/WebMigration_T16_R3_FindingFixes.md` *(Tệp báo cáo này)*.

---

## 3. Bằng Chứng Cuộc Gọi Mã Nguồn & Phân Tích Kỹ Thuật (Call-Site Analysis)

### 3.1. Cơ Chế Chặn Nguồn Gốc (Origin Restriction Gate)
Mã nguồn tại `HubOriginSecurityExtensions.cs`:
```csharp
public static IApplicationBuilder UseStorageUnitHubOriginRestriction(this IApplicationBuilder app)
{
    return app.Use(async (context, next) =>
    {
        if (context.Request.Path.StartsWithSegments(HubPath))
        {
            var origin = context.Request.Headers.Origin.ToString();
            if (!string.IsNullOrWhiteSpace(origin) && !IsAllowedHubOrigin(context.Request, origin))
            {
                context.Response.StatusCode = StatusCodes.Status403Forbidden;
                context.Response.ContentType = "application/json; charset=utf-8";
                await context.Response.WriteAsync("{\"statusCode\":403,\"message\":\"Cross-origin access to /storageUnitHub is forbidden.\"}");
                return;
            }
        }

        await next();
    });
}
```
- Khi client độc hại gửi yêu cầu WebSocket Upgrade: `GET /storageUnitHub` mang header `Origin: https://malicious-attacker.evil.com`:
  - Request Path bắt đầu bằng `/storageUnitHub`.
  - `IsAllowedHubOrigin(context.Request, origin)` phân tích URI nguồn gốc và so khớp với `request.Host.Value`. Do authority không khớp và không thuộc loopback hợp lệ, hàm trả về `false`.
  - Middleware thiết lập `context.Response.StatusCode = StatusCodes.Status403Forbidden` và kết thúc luồng xử lý trước khi chạm tới WebSocket handler của SignalR.
  - Phía client, quá trình bắt tay WebSocket bị hủy bỏ ngay lập tức và ném `WebSocketException` với mã lỗi `403`. Kết nối không được hình thành, loại bỏ hoàn toàn nguy cơ Cross-Site WebSocket Hijacking (CSWSH).

### 3.2. Tính Tương Thích Với Khách Không Mang Origin & Cùng Nguồn
- Khi ứng dụng Desktop, trình kiểm thử nội bộ hoặc tiến trình nền kết nối tới Hub mà không gửi header `Origin` (hoặc header rỗng), điều kiện `!string.IsNullOrWhiteSpace(origin)` đánh giá thành `false`, luồng yêu cầu được chuyển tiếp an toàn tới `next()`.
- Khi trình duyệt tại cùng nguồn (ví dụ Edge trong T18 kết nối tới `http://127.0.0.1:{port}`) gửi yêu cầu với header `Origin: http://127.0.0.1:{port}`, `IsAllowedHubOrigin` xác nhận `originUri.Authority == request.Host.Value`, cho phép kết nối thành công.

---

## 4. Kết Quả Thực Thi Kiểm Thử Thực Tế

### 4.1. Kiểm thử mục tiêu T16 (`SecurityHttpLiveServerTests`)
Lệnh thực thi:
```powershell
dotnet test tests/SelfStorageManagementSystem.WebTests/SelfStorageManagementSystem.WebTests.csproj --filter "FullyQualifiedName~SecurityHttpLiveServerTests" --no-restore
```
Kết quả:
```text
Test run for D:\FPT\PRN222\Project\Final_Project_Web\tests\SelfStorageManagementSystem.WebTests\bin\Debug\net8.0\SelfStorageManagementSystem.WebTests.dll (.NETCoreApp,Version=v8.0)
A total of 1 test files matched the specified pattern.

Passed!  - Failed:     0, Passed:    35, Skipped:     0, Total:    35, Duration: 2 s - SelfStorageManagementSystem.WebTests.dll (net8.0)
```
- **Tổng số**: 35 bài kiểm thử
- **Đạt (Passed)**: 35 (100%)
- **Thất bại (Failed)**: 0
- **Bỏ qua (Skipped)**: 0

### 4.2. Kiểm thử độc lập theo lệnh kiểm tra của Codex (T16 + T18)
Lệnh thực thi:
```powershell
dotnet test tests/SelfStorageManagementSystem.WebTests/SelfStorageManagementSystem.WebTests.csproj --no-restore --filter "FullyQualifiedName~SecurityHttpLiveServerTests|FullyQualifiedName~T18TwoBrowserRealtimeE2ETests" --verbosity quiet
```
- **Kết quả T16**: Toàn bộ 35/35 bài test T16 đạt **PASS 100%**.
- **Kết quả T18**: 3 bài test luồng realtime cơ bản PASS; các bài test thanh toán / hóa đơn do Gemini C đang trong vòng R1 fix.

---

## 5. Bảng Đối Chiếu Mã Băm SHA-256 Cuối Cùng Giữa Hai Workspace

Toàn bộ các tệp liên quan đến Task T16 (R3) đã được đồng bộ độc lập sang `D:\FPT\PRN222\Project\Final_Project` (không sao chép toàn bộ repository) và đối chiếu mã băm SHA-256 đạt trùng khớp 100%:

| Tệp tin liên quan T16 | SHA-256 tại `Final_Project_Web` | SHA-256 tại `Final_Project` | Trạng thái đối chiếu |
| :--- | :--- | :--- | :---: |
| `src/SelfStorageManagementSystem.Web/Extensions/HubOriginSecurityExtensions.cs` | `48BD4A0E1A7B1C61F3CD917642E6110D49C727DDA3F4AC0A35002C9C2F53E2B6` | `48BD4A0E1A7B1C61F3CD917642E6110D49C727DDA3F4AC0A35002C9C2F53E2B6` | **TRÙNG KHỚP 100%** |
| `src/SelfStorageManagementSystem.Web/Program.cs` | `98BFFFC29C039D325619A8CC3CD01FD0A511ED4BEA77053531F0A4EB0E1E7886` | `98BFFFC29C039D325619A8CC3CD01FD0A511ED4BEA77053531F0A4EB0E1E7886` | **TRÙNG KHỚP 100%** |
| `tests/SelfStorageManagementSystem.WebTests/SecurityHttpServerFixture.cs` | `88A8336D0CF57451458A38B21800E52843BCEBE2CE0E8BA04899732B0CF1D48F` | `88A8336D0CF57451458A38B21800E52843BCEBE2CE0E8BA04899732B0CF1D48F` | **TRÙNG KHỚP 100%** |
| `tests/SelfStorageManagementSystem.WebTests/SecurityHttpLiveServerTests.cs` | `10287D393E8CF6BFCD9D286C326E1D94C4DA45501B057C26DE4CC74F7547BCBF` | `10287D393E8CF6BFCD9D286C326E1D94C4DA45501B057C26DE4CC74F7547BCBF` | **TRÙNG KHỚP 100%** |
| `AgentReport/WebMigration_T16_R3_FindingFixes.md` | *(Tính toán sau khi ghi)* | *(Tính toán sau khi ghi)* | **ĐỒNG BỘ ĐẦY ĐỦ** |

---

## 6. Kết Luận Nghiệm Thu Vòng R3

- Cổng kiểm soát Origin tại endpoint SignalR WebSocket `/storageUnitHub` đã được thiết lập chặt chẽ trên cả mã nguồn ứng dụng (`Program.cs`) và harness Kestrel thật (`SecurityHttpServerFixture.cs`).
- Nguy cơ Cross-Site WebSocket Hijacking (CSWSH) đã bị triệt tiêu: client mang Origin lạ bị từ chối 403 Forbidden; client cùng nguồn và client không mang Origin tiếp tục hoạt động thông suốt.
- Các bài kiểm thử phạm vi cơ sở, giáng quyền/thu hồi session, gia hạn trượt và chống rò rỉ thông tin đều đạt 35/35 PASS 100%.
- Giữ nguyên các báo cáo R1, R2 và 3 tệp ngoài commit.
- Không thực hiện bất kỳ lệnh `git commit`, `git push`, hoặc `git merge` nào.
- Trạng thái nghiệm thu: **IMPLEMENTED — chờ Codex review**.
- Sẵn sàng bàn giao cho Codex thẩm định độc lập vòng R3 qua tín hiệu: **`READY_FOR_CODEX_REVIEW`**.
