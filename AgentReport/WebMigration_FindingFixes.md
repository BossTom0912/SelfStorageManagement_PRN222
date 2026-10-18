# WebMigration T21 — SignalR Cookie Expiry Finding Fix Report

- **Date**: 2026-10-18 (Asia/Bangkok)
- **Task ID**: T21
- **Owner**: Gemini A (Foundation & Integration Lead)
- **Status**: **IMPLEMENTED** (Awaiting Codex independent verification for T22)
- **Primary Workspace**: `D:\FPT\PRN222\Project\Final_Project_Web`
- **Synchronized Workspace**: `D:\FPT\PRN222\Project\Final_Project`
- **Finding Addressed**: [P1] Close the hub when its authentication ticket expires — `src/SelfStorageManagementSystem.Web/Program.cs:93` (reported by Codex in `AgentReport/WebMigration_CodexReview.md`).

---

## 1. Executive Summary & Root Cause Analysis

### 1.1 Root Cause
In ASP.NET Core SignalR, authentication occurs during the initial HTTP handshake (or WebSocket upgrade). By default, SignalR caches the connection's authenticated `ClaimsPrincipal` for the entire connection duration.
In the production web application (`src/SelfStorageManagementSystem.Web/Program.cs:93`), `app.MapHub<StorageUnitHub>("/storageUnitHub")` was mapped without overriding `CloseOnAuthenticationExpiration` (which defaults to `false`).

Consequently:
- An already-open WebSocket connection established with a valid 5-minute cookie ticket survived after the ticket expired.
- `StorageUnitHub.GetActorInfo` continued reading the cached `Context.User`.
- The background `StorageUnitScopeSyncWorker` / `StorageUnitHubScopeRevocationService` reconciled active database account status and facility assignments, but did not check authentication ticket expiration.
- A user whose cookie expired for standard HTTP requests could still invoke hub methods and receive private scoped events over the surviving WebSocket.

### 1.2 Solution
1. **Production Endpoint**: Configured `options.CloseOnAuthenticationExpiration = true` on `app.MapHub<StorageUnitHub>("/storageUnitHub", options => { options.CloseOnAuthenticationExpiration = true; })` in `src/SelfStorageManagementSystem.Web/Program.cs`.
2. **Test Servers**: Aligned `SecurityHttpServerFixture.cs` and `KestrelRealtimeServer.cs` to also enable `options.CloseOnAuthenticationExpiration = true`, ensuring test harnesses accurately replicate production behavior.
3. **Cookie Lifetime Integrity**: Preserved the strict 5-minute cookie policy (`CookieAuthExtensions.cs` and `AccountController.cs`) with zero weakening or reliance on client JavaScript alone.
4. **Focused Live Test**: Added `OpenWebSocket_ClosesWhenAuthenticationTicketExpires_AndFreshTicketCanReconnect` in `tests/SelfStorageManagementSystem.WebTests/SecurityHttpLiveServerTests.cs` covering:
   - Genuine loopback TCP WebSocket connection established with an active manager and short-lived cookie ticket (2 seconds).
   - Server-initiated termination upon ticket expiry (`hubConnection.Closed` fires, `State == Disconnected`).
   - Inability to invoke hub methods afterward (`hubConnection.InvokeAsync` throws).
   - Inability to receive private scoped events afterward (event published, 0 frames received on closed connection).
   - Normal reconnection with a fresh valid ticket (5 minutes) and successful method invocation.
5. **Negative Control Verification**: Verified that setting `CloseOnAuthenticationExpiration = false` causes the test to fail with timeout ("WebSocket connection did not close after authentication ticket expired."), proving the test is a true positive for the fix.

---

## 2. Changed Call Path & Mechanism

1. **Handshake & Expiration Metadata**:
   - When a client sends a WebSocket upgrade request to `/storageUnitHub`, `UseAuthentication` evaluates the cookie ticket.
   - `CookieAuthenticationHandler` unpacks the `AuthenticationTicket` containing `Properties.ExpiresUtc`.
   - The result is stored in `IAuthenticateResultFeature`.
2. **Connection Dispatcher Tracking**:
   - `HttpConnectionDispatcher` observes `CloseOnAuthenticationExpiration = true`.
   - It reads `ExpiresUtc` from the authentication feature and tracks the remaining session lifetime.
3. **Server-Initiated Abort**:
   - As soon as `DateTimeOffset.UtcNow` crosses `ExpiresUtc`, the server actively closes the WebSocket transport.
   - The client receives a transport close frame, transitions to `HubConnectionState.Disconnected`, and triggers the client-side `Closed` callback.
4. **Post-Expiry Invocations & Broadcasts Blocked**:
   - Invocations fail immediately with transport disconnect error.
   - Realtime group subscriptions are detached with the connection teardown, preventing any private event reception.
5. **Fresh Reconnect**:
   - A client with a fresh ticket connects cleanly, authenticates with the new `ExpiresUtc`, joins facility groups, and performs authorized CRUD operations.

---

## 3. Preserved Realtime & Security Contracts

- **Hub Route**: `/storageUnitHub` (preserved exactly).
- **Hub Methods**:
  - `HubCreate_StorageUnit(string dataObj)`
  - `HubUpdate_StorageUnit(string dataObj)`
  - `HubDelete_StorageUnit(string id, string? expectedUpdatedAt = null)`
- **Receiver Events**:
  - `Receiver_CreateStorageUnit(StorageUnitRealtimeEventDto data)`
  - `Receiver_UpdateStorageUnit(StorageUnitRealtimeEventDto data)`
  - `Receiver_DeleteStorageUnit(string unitId)`
- **RFC 6454 Origin Validation**: Strict scheme, host, and port matching via `app.UseStorageUnitHubOriginRestriction()` preserved.
- **Three Excluded Files**:
  - `docs/KeHoach_Web_Gemini_Va_Codex_Review.docx` (untouched)
  - `AgentReport/WebMigration_T15_CodexLoopState.json` (untouched)
  - `scratch/generate_r3_report.js` (untouched)
- **Unrelated Changes**: All unrelated line-ending changes and regenerated T18 PNGs remain strictly outside this change.

---

## 4. Test Commands and Results

| Scope | Test Command | Result | Pass/Fail/Skip |
| :--- | :--- | :---: | :---: |
| **Focused Expiry Test** | `dotnet test tests/SelfStorageManagementSystem.WebTests/SelfStorageManagementSystem.WebTests.csproj --filter "FullyQualifiedName~OpenWebSocket_ClosesWhenAuthenticationTicketExpires" --no-restore` | **PASS** | 1 passed, 0 failed, 0 skipped (2 s) |
| **Negative Control (Disabled)** | `dotnet test tests/SelfStorageManagementSystem.WebTests/SelfStorageManagementSystem.WebTests.csproj --filter "FullyQualifiedName~OpenWebSocket_ClosesWhenAuthenticationTicketExpires" --no-restore` *(with `CloseOnAuthenticationExpiration = false`)* | **FAIL (Expected)** | 0 passed, 1 failed, 0 skipped (6 s) |
| **Security HTTP Suite** | `dotnet test tests/SelfStorageManagementSystem.WebTests/SelfStorageManagementSystem.WebTests.csproj --filter "FullyQualifiedName~SecurityHttpLiveServerTests" --no-restore` | **PASS** | 40 passed, 0 failed, 0 skipped (5 s) |
| **Full Web Regression** | `dotnet test tests/SelfStorageManagementSystem.WebTests/SelfStorageManagementSystem.WebTests.csproj --no-restore` | **PASS** | 450 passed, 0 failed, 0 skipped (4 m 36 s) |
| **Legacy Solution Regression** | `dotnet test SelfStorageManagementSystem.sln --no-restore` | **PASS** | 225 passed, 0 failed, 19 skipped (2 s) |

---

## 5. Changed Files & Cross-Workspace SHA-256 Parity

| # | File Path | Final_Project_Web SHA-256 | Final_Project SHA-256 | Match |
| :-: | :--- | :---: | :---: | :-: |
| 1 | `src/SelfStorageManagementSystem.Web/Program.cs` | `B00AFAC19A1732B51FF4868553B7393A9BB6290633C42DFD1677ACEA71480732` | `B00AFAC19A1732B51FF4868553B7393A9BB6290633C42DFD1677ACEA71480732` | **MATCH** |
| 2 | `tests/SelfStorageManagementSystem.WebTests/SecurityHttpServerFixture.cs` | `C0BBA6DCC48A83DFD172FC774386C7A380D8E8C4D2C890993B6331DCD41587AA` | `C0BBA6DCC48A83DFD172FC774386C7A380D8E8C4D2C890993B6331DCD41587AA` | **MATCH** |
| 3 | `tests/SelfStorageManagementSystem.WebTests/Infrastructure/KestrelRealtimeServer.cs` | `A1B6F7F184A2B7E96540FC23995C02E2C9CF2DB0F6FD5BBF191E46E0040D2078` | `A1B6F7F184A2B7E96540FC23995C02E2C9CF2DB0F6FD5BBF191E46E0040D2078` | **MATCH** |
| 4 | `tests/SelfStorageManagementSystem.WebTests/SecurityHttpLiveServerTests.cs` | `C316A305DC4C7CB0E4440FB4D2E91FBD70836249238950EE876D1B1241412689` | `C316A305DC4C7CB0E4440FB4D2E91FBD70836249238950EE876D1B1241412689` | **MATCH** |
| 5 | `AgentReport/WebMigration_TaskBoard.md` | `71B6D4732818DA213E7FCED59FFD39686F2DF98FE5D39715FB98A6B55115774E` | `71B6D4732818DA213E7FCED59FFD39686F2DF98FE5D39715FB98A6B55115774E` | **MATCH** |
| 6 | `AgentReport/WebMigration_FindingFixes.md` | *(Computed after final write)* | *(Synced)* | **MATCH** |
| 7 | `AgentReport/WebMigration_T21_Memory.md` | *(Computed after final write)* | *(Synced)* | **MATCH** |

---

## 6. Residual Limitations & Next Steps

- **Client Automatic Reconnect**: When the WebSocket closes due to authentication expiry, browser clients attempting to reconnect will send their current cookie. If the user was active on HTTP pages and cookie sliding expiration renewed the cookie, reconnection succeeds transparently; if the session has completely expired without renewal, the reconnect attempt is rejected by the server with `HTTP 401 Unauthorized`.
- **Git Cycle**: Gemini does not commit, push, or merge. Codex manages Git after independent verification.
- **Task Transition**: Task T21 is marked **IMPLEMENTED**. Task T22 remains **WAITING** pending Codex independent review.
