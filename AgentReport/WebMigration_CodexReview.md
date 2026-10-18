# WebMigration T20 — independent Codex review

- Date: 2026-10-18 (Asia/Bangkok)
- Base: `2a2834c` (pre-Web migration); delivered `main`: `821449030eebe1e38dc3bfda996f8bcb38474d10`, plus the separately verified T19 handover files in the worktree.
- Result: **Changes requested.** T19 remains VERIFIED. T21 is required; T22 waits for the T21 fix and an independent review.

## Finding

[P1] Close the hub when its authentication ticket expires — `src/SelfStorageManagementSystem.Web/Program.cs:93`

`MapHub<StorageUnitHub>` uses the default `CloseOnAuthenticationExpiration = false`. A WebSocket established with the five-minute cookie from `CookieAuthExtensions.cs:33` therefore retains its authenticated principal after that ticket expires. `StorageUnitHub.GetActorInfo` continues to read `Context.User`, while the periodic scope worker reconciles database account/assignment state rather than the authentication ticket's expiry. An active account can thus continue receiving scoped events and invoking hub methods through that existing connection after ordinary HTTP requests reject the expired cookie. Set expiry handling on the production hub endpoint and cover an already-open WebSocket crossing a short-lived ticket's expiry; preserve the T12–T15 hub route/method/receiver contract.

## Evidence and assessment

- Reviewed the Web migration diff inventory from `2a2834c` to `8214490` (365 changed paths), T19 handover and task dependencies, production composition and route mapping, cookie validation and login ticket creation, `StorageUnitHub` connection/invocation paths, scope reconciliation worker and publisher, payment callback routes, and relevant security, realtime, and two-browser tests.
- `AccountController.cs:127` sets `ExpiresUtc` to five minutes; `CookieAuthExtensions.cs:33` configures the same cookie lifespan. `StorageUnitHub.cs:369` uses the connection principal. `StorageUnitHubScopeRevocationService.cs:126` reconciles current database identity and group membership without checking ticket expiry. `StorageUnitScopeSyncWorker.cs` runs that reconciliation every second. The existing expired-cookie tests verify a new HTTP request or connection, not an already-open WebSocket after expiry.
- Microsoft documents that SignalR caches the connection principal and that connections continue after authentication expiry by default: [SignalR authentication and authorization (.NET 8)](https://learn.microsoft.com/en-us/aspnet/core/signalr/authn-and-authz?view=aspnetcore-8.0), [SignalR configuration (.NET 8)](https://learn.microsoft.com/en-us/aspnet/core/signalr/configuration?view=aspnetcore-8.0).
- No new test run was needed to establish the missing endpoint option and uncovered call path. The prior independent baseline remains 449/449 Web tests, 225 passed/19 skipped default legacy tests, and 19/19 SQL concurrency tests; these passing results do not cover an open connection crossing cookie expiry.
- The earlier independently reviewed T19 report/hash pairs were not re-reviewed. No other qualifying finding was established from the inspected paths. Full 89k-line migration diff was not line-by-line re-reviewed; T00–T19 retain their prior independent review evidence.

## Next

Send the single finding as T21 to a fresh Gemini Antigravity chat. Gemini repairs and tests only the scoped session-expiry behavior, synchronizes changed files and evidence to `Final_Project`, reports hashes, and leaves T21 IMPLEMENTED for Codex's T22 independent review. Preserve the three excluded files and the T12–T15 realtime contract.
