# WebMigration T21 — memory

- **Task ID**: T21
- **Owner**: Gemini A (Foundation & Integration Lead).
- **Scope**: Fix the single P1 finding from Codex's T20 independent review: enable `CloseOnAuthenticationExpiration = true` on the production `/storageUnitHub` endpoint (`src/SelfStorageManagementSystem.Web/Program.cs:93`), configure corresponding test server fixtures, and add a focused live SignalR test proving server-initiated WebSocket closure across ticket expiry, post-expiry invocation/event rejection, and successful reconnection with a fresh valid ticket.
- **Dependencies**: T20 VERIFIED (Changes requested).
- **Final TaskBoard Status**: T21 **VERIFIED** by Codex; T22 **VERIFIED**.
- **Report**: `AgentReport/WebMigration_FindingFixes.md`, SHA-256 `D8639969B1A4F8FED35F4F334E121B7E675C1721516D39B430BFC88C6DD93A17`.
- **Independent review**: `AgentReport/WebMigration_CodexFinalReview.md`, SHA-256 `89278E8FC5DDB7F9EC4A2FF89623882128D6B2B01B405EA93BAC8FB410153EE3` (**No findings**).
- **Finding Addressed**: [P1] Close the hub when its authentication ticket expires — `src/SelfStorageManagementSystem.Web/Program.cs:93` (Review: `AgentReport/WebMigration_CodexReview.md`, SHA-256 `AFEAA967448366992897C87A620FBED222EB873AF84F2942D50B89AAAB592127`; Instruction prompt: `Agent/Gemini/WEBMIGRATION_T21_FIX_PROMPT.md`, SHA-256 `C45D864638C488318F1903406D1B1D97C567B529D7BCB2F40A1C1C24414DF8B1`).

---

## 1. Changed Paths & Established Behavior

1. `src/SelfStorageManagementSystem.Web/Program.cs`:
   - Updated `app.MapHub<StorageUnitHub>("/storageUnitHub")` to configure `options.CloseOnAuthenticationExpiration = true`.
   - Behavior: Enforces that ASP.NET Core SignalR's `HttpConnectionDispatcher` tracks the `ExpiresUtc` embedded in the authentication ticket during handshake. When the ticket expires, the server actively terminates the persistent WebSocket connection.
2. `tests/SelfStorageManagementSystem.WebTests/SecurityHttpServerFixture.cs`:
   - Updated `_app.MapHub<StorageUnitHub>("/storageUnitHub")` with `options.CloseOnAuthenticationExpiration = true` to match production endpoint configuration.
3. `tests/SelfStorageManagementSystem.WebTests/Infrastructure/KestrelRealtimeServer.cs`:
   - Updated `app.MapHub<StorageUnitHub>("/storageUnitHub")` with `options.CloseOnAuthenticationExpiration = true` to match production endpoint configuration.
4. `tests/SelfStorageManagementSystem.WebTests/SecurityHttpLiveServerTests.cs`:
   - Added `OpenWebSocket_ClosesWhenAuthenticationTicketExpires_AndFreshTicketCanReconnect`:
     - Creates active manager with facility assignment in DB.
     - Protects an `AuthenticationTicket` with 2-second lifespan (`ExpiresUtc = DateTimeOffset.UtcNow.AddSeconds(2)`).
     - Connects via `HubConnectionBuilder` using genuine `HttpTransportType.WebSockets` over TCP loopback.
     - Confirms initial connection is `Connected`.
     - Waits across expiry (2 seconds) and asserts `hubConnection.Closed` event fires and `hubConnection.State == HubConnectionState.Disconnected`.
     - Asserts `hubConnection.InvokeAsync` throws exception on closed connection.
     - Publishes a private scoped storage unit event and asserts 0 events received on disconnected connection.
     - Creates a fresh 5-minute ticket, reconnects, and verifies method invocation succeeds.
   - Negative Control: Verified that setting `CloseOnAuthenticationExpiration = false` causes this test to fail with timeout ("WebSocket connection did not close after authentication ticket expired.") after 6 seconds.
5. `AgentReport/WebMigration_FindingFixes.md`:
   - Comprehensive implementation, test evidence, call path, and limitation report.
6. `AgentReport/WebMigration_TaskBoard.md`:
   - Updated header, diagram, table, and added Section 15 for T21 IMPLEMENTED.
7. `AgentReport/WebMigration_T21_Memory.md`:
   - This task memory document.

---

## 2. Test Commands and Observed Totals (Gemini T21 Run)

- **Focused Live Expiry Test**:
  - Command: `dotnet test tests/SelfStorageManagementSystem.WebTests/SelfStorageManagementSystem.WebTests.csproj --filter "FullyQualifiedName~OpenWebSocket_ClosesWhenAuthenticationTicketExpires" --no-restore`
  - Result: **1 passed, 0 failed, 0 skipped** (Duration: 2 s).
- **Negative Control Test (Disabled Option)**:
  - Command: `dotnet test tests/SelfStorageManagementSystem.WebTests/SelfStorageManagementSystem.WebTests.csproj --filter "FullyQualifiedName~OpenWebSocket_ClosesWhenAuthenticationTicketExpires" --no-restore`
  - Result: **0 passed, 1 failed, 0 skipped** (Duration: 6 s; Message: "WebSocket connection did not close after authentication ticket expired.").
- **Security HTTP Live Server Test Suite**:
  - Command: `dotnet test tests/SelfStorageManagementSystem.WebTests/SelfStorageManagementSystem.WebTests.csproj --filter "FullyQualifiedName~SecurityHttpLiveServerTests" --no-restore`
  - Result: **40 passed, 0 failed, 0 skipped** (Duration: 5 s; previously 39 passed).
- **Full Web Solution Regression**:
  - Command: `dotnet test tests/SelfStorageManagementSystem.WebTests/SelfStorageManagementSystem.WebTests.csproj --no-restore`
  - Result: **450 passed, 0 failed, 0 skipped** (Duration: 4 m 36 s; previously 449 passed).
- **Legacy Solution Regression**:
  - Command: `dotnet test SelfStorageManagementSystem.sln --no-restore`
  - Result: **225 passed, 0 failed, 19 skipped** (Total: 244; Duration: 2 s).

---

## 3. Preserved Realtime & Security Contracts

- **Realtime Route**: `/storageUnitHub` preserved.
- **Hub Methods**: `HubCreate_StorageUnit(string dataObj)`, `HubUpdate_StorageUnit(string dataObj)`, `HubDelete_StorageUnit(string id, string? expectedUpdatedAt = null)` preserved.
- **Receiver Events**: `Receiver_CreateStorageUnit(StorageUnitRealtimeEventDto data)`, `Receiver_UpdateStorageUnit(StorageUnitRealtimeEventDto data)`, `Receiver_DeleteStorageUnit(string unitId)` preserved.
- **Strict RFC 6454 Origin Validation**: Preserved via `app.UseStorageUnitHubOriginRestriction()`.
- **Three Excluded Files**:
  - `docs/KeHoach_Web_Gemini_Va_Codex_Review.docx`
  - `AgentReport/WebMigration_T15_CodexLoopState.json`
  - `scratch/generate_r3_report.js`
- **Line Endings & Artifacts**: Kept unrelated line-ending changes and regenerated T18 PNGs outside this change.

---

## 4. Git Reference & Synchronization

- Base Commit: `b67563aea6257e7d41e7da5843bb3f8593ec7a1a` on `main`.
- Feature Commit for T19-T20 Handoff: `87ee0b3d926ebe4f868076b6af7a292ba17031f5`.
- Git Policy: Gemini does not commit, push, or merge; Codex manages the Git lifecycle after independent review.
- Workspace Synchronization: All scoped T21 files are synchronized to `D:\FPT\PRN222\Project\Final_Project` and verified with exact SHA-256 match.

---

## 5. Next Task Entry Conditions

- **Next Task**: T22 (Final independent Codex review).
- **Owner**: Codex.
- **Completion**: Codex confirmed scoped file parity across `Final_Project_Web` and `Final_Project`, inspected the full T21 diff, and independently passed the live expiry test (1/1), security suite (40/40), Web solution (450/450), and legacy default suite (225 passed, 19 skipped). The T22 review found no remaining findings. The T21/T22 Git cycle follows this verified snapshot.
