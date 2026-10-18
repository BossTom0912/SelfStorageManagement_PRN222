# WebMigration T22 — memory

- Task ID: T22. Owner: Codex. Dependency: T21 independently VERIFIED.
- Scope: Final independent review of the single T20 SignalR session-expiry fix, source and test mappings, live WebSocket behavior, regressions, preserved realtime and Origin contracts, and synchronized evidence.
- Final status: **VERIFIED — Accepted, No findings.** T21 is **VERIFIED**.
- Review: `AgentReport/WebMigration_CodexFinalReview.md`, SHA-256 `89278E8FC5DDB7F9EC4A2FF89623882128D6B2B01B405EA93BAC8FB410153EE3`.
- T21 report: `AgentReport/WebMigration_FindingFixes.md`, SHA-256 `D8639969B1A4F8FED35F4F334E121B7E675C1721516D39B430BFC88C6DD93A17`.

## Changed paths and behavior

- `src/SelfStorageManagementSystem.Web/Program.cs`: production `/storageUnitHub` mapping sets `CloseOnAuthenticationExpiration = true` so an existing WebSocket closes at cookie ticket expiry.
- `tests/SelfStorageManagementSystem.WebTests/SecurityHttpServerFixture.cs` and `tests/SelfStorageManagementSystem.WebTests/Infrastructure/KestrelRealtimeServer.cs`: test mappings use the same option.
- `tests/SelfStorageManagementSystem.WebTests/SecurityHttpLiveServerTests.cs`: real Kestrel WebSocket test proves closure across a short-lived ticket, blocked post-expiry invocation and private event, and fresh-ticket reconnection.
- Documentation: T21 report, T21 memory, TaskBoard, T22 final review, and this T22 memory. Source and report hashes were verified against `D:\FPT\PRN222\Project\Final_Project`.
- Route `/storageUnitHub`, all three Hub methods and three Receiver CRUD events, the five-minute cookie policy, and strict RFC 6454 Origin gate are preserved.

## Independent tests

- `dotnet test tests/SelfStorageManagementSystem.WebTests/SelfStorageManagementSystem.WebTests.csproj --filter "FullyQualifiedName~OpenWebSocket_ClosesWhenAuthenticationTicketExpires" --no-restore --logger "console;verbosity=normal"`: 1 passed, 0 failed, 0 skipped.
- `dotnet test tests/SelfStorageManagementSystem.WebTests/SelfStorageManagementSystem.WebTests.csproj --filter "FullyQualifiedName~SecurityHttpLiveServerTests" --no-restore --logger "console;verbosity=minimal"`: 40 passed, 0 failed, 0 skipped.
- `dotnet test SelfStorageManagementSystem.Web.sln --no-restore --logger "console;verbosity=minimal"`: 450 passed, 0 failed, 0 skipped.
- `dotnet test SelfStorageManagementSystem.sln --no-restore --logger "console;verbosity=minimal"`: 225 passed, 0 failed, 19 skipped.
- `git diff --check` on scoped paths: passed. SQL concurrency was not rerun because T21 has no SQL change; last independent T17 SQL result was 19/19 passed.

## Evidence and handoff

- The focused test uses a two-second ticket instead of waiting five minutes; it exercises the production cookie scheme and endpoint option. Gemini's disabled-option negative control failed as expected, and Codex independently verified the enabled case.
- The full Web run regenerated 19 T18 PNGs. They were restored from the synchronized R6 evidence, leaving no T18 evidence diff.
- Base Git commit: `b67563aea6257e7d41e7da5843bb3f8593ec7a1a` on `main`. Final branch: `codex/webmigration-t21-t22-final`; scoped feature commit: `002d84e1dfdb4bea51b4bd58244ef290a623a294`; merge commit on pushed `main`: `57a6bf0fedba46d17a6a9d35afc2dd119546d65f`.
- Keep outside the commit: `docs/KeHoach_Web_Gemini_Va_Codex_Review.docx`, `AgentReport/WebMigration_T15_CodexLoopState.json`, `scratch/generate_r3_report.js`, and unrelated line-ending-only changes.
- Next task: begin in a new chat using this memory and the TaskBoard. T21 Gemini chat: `https://127.0.0.1:50608/c/06a8e4fd-e223-43ee-a8ad-3df980bdc40b`; T22 Codex review chat: `01a14fcb-6606-7d81-92f6-769fd9b04817`. No further T21/T22 implementation work is required.
