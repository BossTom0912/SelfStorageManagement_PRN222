# Báo Cáo Nghiệm Thu Task T16: Kiểm Thử Cookie, Quyền và HTTP Thực (Vòng R4)

**Dự án**: Antigravity — `Final_Project_Web`  
**Workspace thực thi**: `D:\FPT\PRN222\Project\Final_Project_Web`  
**Workspace đồng bộ**: `D:\FPT\PRN222\Project\Final_Project`  
**Nhân sự đảm trách**: Gemini A (Foundation & Integration Lead)  
**Git Baseline**: `82496d648624ddf9af82d481017cfdd12e612787` (nhánh `main`)  
**Tín hiệu bàn giao**: `READY_FOR_CODEX_REVIEW`  
**Trạng thái bàn giao**: **IMPLEMENTED — chờ Codex review**  

---

## 1. Tóm Tắt Mục Tiêu & Phản Hồi Codex Review R3

Trong đợt thẩm định độc lập vòng R3, Codex đã chạy bộ kiểm thử `SecurityHttpLiveServerTests` với kết quả 35/35 test PASS. Codex xác nhận cơ chế chặn nguồn lạ (hostile external origin) đã hoạt động. Tuy nhiên, Codex chỉ ra 2 nội dung kỹ thuật trọng tâm cần khắc phục triệt để trong vòng R4:

1. **[P1] So sánh toàn bộ bộ ba (Scheme, Host, Effective Port) và loại bỏ ngoại lệ cùng loopback (`HubOriginSecurityExtensions.cs:53-70`)**:
   - *Phát hiện Codex*: Trong R3, logic `IsAllowedHubOrigin` chỉ so khớp `originUri.Authority == request.Host.Value` mà không so sánh `originUri.Scheme` với `request.Scheme`. Do đó một trang web `http://host:port` (khác nguồn) có thể bị nhầm lẫn là cùng nguồn với ứng dụng đang phục vụ tại `https://host:port`. Đồng thời nhánh loopback trước đây chấp nhận chéo giữa `localhost` và `127.0.0.1`, vi phạm nguyên tắc cùng nguồn RFC 6454.
   - *Khắc phục R4*:
     - So sánh chuẩn xác 100% bộ ba `(Scheme, Host, Effective Port)` theo đúng đặc tả RFC 6454.
     - Chuẩn hóa cổng mặc định (default port normalization): cổng 80 cho scheme `http`, cổng 443 cho scheme `https`.
     - Loại bỏ hoàn toàn ngoại lệ loopback: `localhost` và `127.0.0.1` là hai origin hoàn toàn khác biệt và phải bị từ chối nếu gọi chéo.
     - Cho phép các client trực tiếp không mang header `Origin` (direct/non-browser clients, desktop app, console tool) hoạt động bình thường.
     - Mở rộng bộ kiểm thử trên Kestrel thật với 4 ca kiểm thử âm tính và dương tính mới:
       1. Từ chối khi sai scheme (`https` gọi vào server `http`) $\rightarrow$ 403 Forbidden trên cả Negotiate và WebSocket handshake.
       2. Từ chối khi sai loopback alias (`localhost` gọi vào server `127.0.0.1`) $\rightarrow$ 403 Forbidden trên cả Negotiate và WebSocket handshake.
       3. Từ chối khi sai port (`port + 1` gọi vào server) $\rightarrow$ 403 Forbidden trên cả Negotiate và WebSocket handshake.
       4. Chấp nhận kết nối Same-Origin và chuẩn hóa cổng mặc định (80 / 443) hợp lệ.
2. **[P2] Hiệu chỉnh và xác thực bằng chứng mã băm SHA-256 thực tế cuối cùng (`AgentReport/WebMigration_T16_R4_FindingFixes.md`)**:
   - *Phát hiện Codex*: Bảng hash R3 chứa các giá trị hash chưa khớp với các byte cuối cùng trên đĩa sau khi hoàn thiện code.
   - *Khắc phục R4*: Tính toán mã băm SHA-256 từ các tệp thực tế trên đĩa sau khi hoàn tất toàn bộ chỉnh sửa mã nguồn và đồng bộ sang `Final_Project`. Kiểm tra đối chiếu chéo 1:1 giữa hai workspace.

---

## 2. Danh Sách Tệp Quyền Sở Hữu & Thay Đổi Trong Vòng R4

Toàn bộ các tệp thuộc quyền sở hữu của Gemini A trong Task T16:

1. `src/SelfStorageManagementSystem.Web/Extensions/HubOriginSecurityExtensions.cs` *(Cập nhật R4)*:
   - Xây dựng lại logic `IsAllowedHubOrigin` so khớp bộ ba `(Scheme, Host, Effective Port)` chuẩn RFC 6454.
   - Xử lý chuẩn hóa cổng mặc định (`request.Host.Port ?? (request.Scheme == "https" ? 443 : 80)` và `originUri.IsDefaultPort`).
   - Loại bỏ nhánh ngoại lệ loopback.
   - Trả về `403 Forbidden` JSON khi phát hiện vi phạm nguồn gốc.
2. `src/SelfStorageManagementSystem.Web/Program.cs` *(Giữ nguyên cấu hình R3)*:
   - Kích hoạt `app.UseStorageUnitHubOriginRestriction()` trước `app.MapHub<StorageUnitHub>("/storageUnitHub")`.
   - Giữ nguyên toàn bộ route MVC, Razor Pages, API, tên Hub và Receiver contracts.
3. `tests/SelfStorageManagementSystem.WebTests/SecurityHttpServerFixture.cs` *(Cập nhật R4)*:
   - Tích hợp `_app.UseStorageUnitHubOriginRestriction()` đồng nhất với production pipeline.
4. `tests/SelfStorageManagementSystem.WebTests/SecurityHttpLiveServerTests.cs` *(Cập nhật R4)*:
   - Bổ sung `using Microsoft.AspNetCore.Http;`.
   - Nâng cấp bộ kiểm thử Origin từ 4 test lên 8 test độc lập toàn diện:
     - `Origin_UntrustedOrigin_SignalR_Negotiate_Returns403Forbidden` (Domain ngoài bị từ chối trên Negotiate).
     - `Origin_UntrustedOrigin_WebSocketHandshake_IsRejected_NoMutationOrBroadcast` (Domain ngoài bị từ chối trên WebSocket handshake, xác minh CSDL không bị thay đổi / không broadcast).
     - `Origin_LegitimateSameOrigin_WebSocketHandshake_IsAccepted` (Same-origin handshake đạt `WebSocketState.Open`).
     - `Origin_NoOriginHeader_DirectClient_IsAccepted` (Client không mang Origin header kết nối thành công `WebSocketState.Open`).
     - `Origin_WrongScheme_SignalR_NegotiateAndWebSocket_RejectedWith403` (Sai scheme bị từ chối 403).
     - `Origin_LoopbackAlias_SignalR_NegotiateAndWebSocket_RejectedWith403` (Khác loopback alias bị từ chối 403).
     - `Origin_WrongPort_SignalR_NegotiateAndWebSocket_RejectedWith403` (Sai port bị từ chối 403).
     - `Origin_SameOrigin_Negotiate_Returns200_AndDefaultPortNormalizationMatches` (Same-origin Negotiate 200 OK và kiểm tra unit test chuẩn hóa cổng 80/443).
5. `AgentReport/WebMigration_T16_R4_FindingFixes.md` *(Tệp báo cáo này)*.

---

## 3. Phân Tích Kỹ Thuật & Mã Nguồn Kiểm Soát Nguồn Gốc (Call-Site Analysis)

### 3.1. Thuật Toán So Sánh Bộ Ba Nguồn Gốc Chuẩn RFC 6454
Tại `src/SelfStorageManagementSystem.Web/Extensions/HubOriginSecurityExtensions.cs`:
```csharp
public static bool IsAllowedHubOrigin(HttpRequest request, string origin)
{
    // Cho phép các client trực tiếp không mang header Origin (non-browser clients, desktop app, worker nội bộ)
    if (string.IsNullOrWhiteSpace(origin))
    {
        return true;
    }

    if (!Uri.TryCreate(origin, UriKind.Absolute, out var originUri))
    {
        return false;
    }

    // Tính toán effective port chuẩn xác theo RFC 6454
    int requestEffectivePort = request.Host.Port ?? (string.Equals(request.Scheme, "https", StringComparison.OrdinalIgnoreCase) ? 443 : 80);
    int originEffectivePort = originUri.IsDefaultPort
        ? (string.Equals(originUri.Scheme, "https", StringComparison.OrdinalIgnoreCase) ? 443 : 80)
        : originUri.Port;

    // So khớp chính xác toàn bộ bộ ba (Scheme, Host, Effective Port)
    bool schemeMatches = string.Equals(originUri.Scheme, request.Scheme, StringComparison.OrdinalIgnoreCase);
    bool hostMatches = string.Equals(originUri.Host, request.Host.Host, StringComparison.OrdinalIgnoreCase);
    bool portMatches = originEffectivePort == requestEffectivePort;

    return schemeMatches && hostMatches && portMatches;
}
```

### 3.2. Đánh Giá Khả Năng Phòng Thủ
- **Phòng chống Cross-Site WebSocket Hijacking (CSWSH)**:
  - Khi kẻ tấn công nhúng mã độc trên `https://evil.com` hoặc `http://evil.com`, trình duyệt của nạn nhân sẽ gửi `Origin: https://evil.com`. Do `hostMatches == false`, middleware chặn ngay lập tức với mã `403 Forbidden`.
- **Phòng chống tấn công tráo Scheme (Scheme Confusion)**:
  - Nếu ứng dụng chạy HTTPS tại `https://selfstorage.vn` nhưng kẻ tấn công đứng ở một trang không an toàn `http://selfstorage.vn`, `schemeMatches == false` ngăn chặn triệt để kết nối WebSocket.
- **Phòng chống tấn công qua dịch vụ nội bộ khác (Loopback Alias Separation)**:
  - Khi ứng dụng lắng nghe tại `http://127.0.0.1:{port}`, một dịch vụ web độc hại khác của bên thứ ba đang chạy trên máy nhà phát triển tại `http://localhost:{other_port}` không thể gửi request chéo sang `127.0.0.1`. Do `originUri.Host ("localhost") != request.Host.Host ("127.0.0.1")`, yêu cầu bị từ chối `403 Forbidden`.
- **Hỗ trợ Non-Browser Clients**:
  - Các client chuyên dụng như console tool, backend service, hoặc worker nội bộ không tự động gắn header `Origin` (theo RFC 6455 chỉ trình duyệt web bắt buộc phải gửi header `Origin`). Hệ thống cho phép các client này kết nối thông suốt mà không gặp trở ngại.

---

## 4. Kết Quả Thực Thi Kiểm Thử Thực Tế

### 4.1. Kiểm Thử Mục Tiêu T16 (`SecurityHttpLiveServerTests`)
Lệnh thực thi:
```powershell
dotnet test tests/SelfStorageManagementSystem.WebTests/SelfStorageManagementSystem.WebTests.csproj --no-restore --filter "FullyQualifiedName~SecurityHttpLiveServerTests"
```
Kết quả chi tiết từ Kestrel thật:
```text
Test run for D:\FPT\PRN222\Project\Final_Project_Web\tests\SelfStorageManagementSystem.WebTests\bin\Debug\net8.0\SelfStorageManagementSystem.WebTests.dll (.NETCoreApp,Version=v8.0)
A total of 1 test files matched the specified pattern.

Passed!  - Failed:     0, Passed:    39, Skipped:     0, Total:    39, Duration: 3 s - SelfStorageManagementSystem.WebTests.dll (net8.0)
```
- **Tổng số**: 39 bài kiểm thử
- **Đạt (Passed)**: 39 (100%)
- **Thất bại (Failed)**: 0
- **Bỏ qua (Skipped)**: 0

### 4.2. Kiểm Thử Hồi Quy WebTests Toàn Diện (Không Bao Gồm T18 Browser E2E)
Lệnh thực thi:
```powershell
dotnet test tests/SelfStorageManagementSystem.WebTests/SelfStorageManagementSystem.WebTests.csproj --no-restore --filter "FullyQualifiedName!~T18TwoBrowserRealtimeE2ETests"
```
Kết quả:
```text
Test run for D:\FPT\PRN222\Project\Final_Project_Web\tests\SelfStorageManagementSystem.WebTests\bin\Debug\net8.0\SelfStorageManagementSystem.WebTests.dll (.NETCoreApp,Version=v8.0)
A total of 1 test files matched the specified pattern.

Passed!  - Failed:     0, Passed:   426, Skipped:     0, Total:   426, Duration: 3 m 7 s - SelfStorageManagementSystem.WebTests.dll (net8.0)
```
- **Tổng số kiểm thử hồi quy**: 426 bài kiểm thử
- **Đạt (Passed)**: 426 (100%)
- **Thất bại (Failed)**: 0
- Không có bất kỳ hồi quy nào đối với các chức năng MVC, Razor Pages, API, hay dịch vụ nền.

---

## 5. Bảng Đối Chiếu Mã Băm SHA-256 Cuối Cùng Giữa Hai Workspace

Toàn bộ các tệp liên quan đến Task T16 (R4) đã được đồng bộ độc lập sang `D:\FPT\PRN222\Project\Final_Project` (chỉ sao chép tệp liên quan, không sao chép toàn bộ repository) và đối chiếu mã băm SHA-256 thực tế tại thời điểm hoàn tất:

| Tệp tin liên quan T16 | SHA-256 tại `Final_Project_Web` | SHA-256 tại `Final_Project` | Trạng thái đối chiếu |
| :--- | :--- | :--- | :---: |
| `src/SelfStorageManagementSystem.Web/Extensions/HubOriginSecurityExtensions.cs` | `A9EF7A92852431783ABE6A3396AA1C0635E3450C82EBD3116FB9DA5F8FB1EF08` | `A9EF7A92852431783ABE6A3396AA1C0635E3450C82EBD3116FB9DA5F8FB1EF08` | **TRÙNG KHỚP 100%** |
| `src/SelfStorageManagementSystem.Web/Program.cs` | `77471871E79E8E2B3883E12BADE702E7BEB896D533543167F151C6348FD974E5` | `77471871E79E8E2B3883E12BADE702E7BEB896D533543167F151C6348FD974E5` | **TRÙNG KHỚP 100%** |
| `tests/SelfStorageManagementSystem.WebTests/SecurityHttpServerFixture.cs` | `8243C50F3EF6461083982A9E9639E034142A699566957DB6CBF5B4BD86623037` | `8243C50F3EF6461083982A9E9639E034142A699566957DB6CBF5B4BD86623037` | **TRÙNG KHỚP 100%** |
| `tests/SelfStorageManagementSystem.WebTests/SecurityHttpLiveServerTests.cs` | `A347BF93C58672A1D3958EA5134343CEED7CDF5533B5C7F2FE31744315F5147F` | `A347BF93C58672A1D3958EA5134343CEED7CDF5533B5C7F2FE31744315F5147F` | **TRÙNG KHỚP 100%** |
| `AgentReport/WebMigration_T16_R4_FindingFixes.md` | *(Được đồng bộ đồng nhất giữa 2 workspace ngay sau khi tạo)* | *(Được đồng bộ đồng nhất giữa 2 workspace ngay sau khi tạo)* | **ĐỒNG BỘ 100%** |

---

## 6. Kết Luận Nghiệm Thu Vòng R4

- Toàn bộ yêu cầu kỹ thuật nghiêm ngặt của Codex trong vòng R3 đã được giải quyết triệt để:
  1. Cổng kiểm soát Origin tại `/storageUnitHub` tuân thủ đầy đủ đặc tả RFC 6454 so khớp bộ ba `(Scheme, Host, Effective Port)`.
  2. Loại bỏ hoàn toàn ngoại lệ loopback (`localhost` vs `127.0.0.1` được đối xử như hai origin riêng biệt).
  3. Xử lý chuẩn hóa cổng mặc định (80 cho HTTP, 443 cho HTTPS).
  4. Hỗ trợ đầy đủ direct clients không mang Origin.
  5. 39/39 bài test bảo mật/HTTP/Kestrel đạt PASS 100%, 426/426 bài test hồi quy WebTests đạt PASS 100%.
- Giữ nguyên các báo cáo lịch sử R1, R2, R3 và 3 tệp ngoài commit.
- Giữ các Task T19–T22 ở trạng thái WAITING.
- Không thực hiện bất kỳ lệnh `git commit`, `git push`, hoặc `git merge` nào.
- Trạng thái nghiệm thu: **IMPLEMENTED — chờ Codex review**.
- Sẵn sàng bàn giao cho Codex thẩm định độc lập vòng R4 qua tín hiệu: **`READY_FOR_CODEX_REVIEW`**.
