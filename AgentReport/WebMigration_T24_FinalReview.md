# WebMigration T24 — final independent review and release gate

Date: 2026-10-19 (Asia/Bangkok). Owner: Codex. Base: `main` / `origin/main` at `1201742ded6d338a00043cd08533392fe4c1a6c7`. Dependency: T01, T08, and T23 VERIFIED; see `AgentReport/WebMigration_T23_Memory.md`.

## Result

**No findings. All required release gates passed.** The final scoped change fixes one date-sensitive T18 browser fixture and adds T23/T24 review evidence. No production BLL, DAL, Web host, schema, or contract code changed in T23/T24. T24 is ready for the scoped Git release.

## Test execution

All commands ran in `D:\FPT\PRN222\Project\Final_Project_Web` without concurrent Gemini DB/browser testing.

| Gate | Exact command | Result |
| --- | --- | --- |
| Initial full Web | `dotnet test SelfStorageManagementSystem.Web.sln --no-restore --logger 'console;verbosity=minimal'` | 449 passed, 1 failed, 0 skipped (450 total). `T18_E2E_04_CheckIn_Inspection_Handover_ZeroPin_TwoBrowserRealtime` used UTC date 2026-10-18 while the facility date was 2026-10-19. |
| Focused browser verification after repair | `dotnet test tests/SelfStorageManagementSystem.WebTests/SelfStorageManagementSystem.WebTests.csproj --filter 'FullyQualifiedName~T18_E2E_04_CheckIn_Inspection_Handover_ZeroPin_TwoBrowserRealtime' --no-restore --logger 'console;verbosity=minimal'` | 1 passed, 0 failed, 0 skipped. |
| Final full Web, including seven T18 browser tests | `dotnet test SelfStorageManagementSystem.Web.sln --no-restore --logger 'console;verbosity=minimal'` | **450 passed, 0 failed, 0 skipped** (4m36s). |
| Legacy default solution | `dotnet test SelfStorageManagementSystem.sln --no-restore --logger 'console;verbosity=minimal'` | **225 passed, 0 failed, 19 expected SQL skips** (244 total). The SQL tests require an explicit isolated connection string. |
| SQL Server concurrency on isolated WebTest DB | Set `SELFSTORAGE_SQL_TEST_CONNECTION_STRING=Server=localhost;Database=SelfStoragePRN222_WebTest;Trusted_Connection=True;TrustServerCertificate=True;MultipleActiveResultSets=true;`, then `dotnet test tests/SelfStorageManagementSystem.Tests/SelfStorageManagementSystem.Tests.csproj --filter 'FullyQualifiedName~SqlServerConcurrencyTests' --no-restore --logger 'console;verbosity=minimal'`; remove the environment variable afterward. | **19 passed, 0 failed, 0 skipped** (12s). |

The targeted repair is in `tests/SelfStorageManagementSystem.WebTests/T18TwoBrowserRealtimeE2ETests.cs`: test 04 reads facility 1's configured timezone and computes `today` in that timezone before creating the hold and dated invoice. This matches `ReservationService.CreateReservationHoldAsync` date validation and avoids crossing the UTC/Vietnam calendar boundary. Source SHA-256 in both workspaces: `B330AACA509212C978E40EDF953BD8E2682B5C014597BC20835A511AC10EF476`.

## Database and evidence integrity

- `SelfStoragePRN222_WebTest` on `localhost` and `SelfStoragePRN222_WebE2eTest` on `(localdb)\mssqllocaldb` were ONLINE before tests. After all tests, an independent `sqlcmd` query found all six named `core` business triggers in each database with `is_disabled=0`: `trg_agreement_status_transition`, `trg_invoice_lines_guard`, `trg_invoice_lines_refresh_totals`, `trg_invoice_status_transition`, `trg_payment_allocation_guard`, and `trg_reservation_status_transition`.
- WebTest counts were zero for the checked SQL concurrency fixture prefixes in units, reservations, agreements, invoices, payments, and refunds.
- E2E counts were zero for T18 units, invoices, payments, and refunds. Five `AGR-T18-%` agreements and their five reservations, four inspections, and five identity verifications from older 2026-10-18 runs were found. A first cleanup attempt failed on SQL `QUOTED_IDENTIFIER` and rolled back; no partial deletion occurred. A second transaction with `SET QUOTED_IDENTIFIER ON` deleted only those five historical T18 fixtures and related records, asserted exact agreement/reservation delete counts, and committed. Final E2E counts for T18 agreements, units, reservation-code fixtures, invoices, payments, and refunds were all zero; all six triggers remained enabled.
- The full Web run regenerated 20 of the 24 T18 PNG evidence files. All 24 comparison copies were checked against the SHA-256 manifest in `AgentReport/WebMigration_T18_R6_FindingFixes.md`; only the 20 regenerated files were restored from those verified copies. No T18 PNG remains modified.

## Code and contract review

Reviewed the complete T23 report/TaskBoard changes and the T24 test diff with the `review-agent` defect criteria; inspected production call sites and relevant tests. **No findings.** The final test diff changes only date derivation; it does not weaken the business date rule or test assertions. `git diff --check` passed on scoped tracked paths.

The current Web host still maps `/storageUnitHub` with `CloseOnAuthenticationExpiration=true`; `StorageUnitHub` has `[Authorize]`; the three `Hub*StorageUnit` methods and three `Receiver_*StorageUnit` broadcasts remain present. Cookie expiration is five minutes with sliding renewal, and `HubOriginSecurityExtensions` retains strict scheme/host/effective-port matching. The T01 HMAC configuration correction and exact eight `IAdminAccountService` methods, plus T08-F01 through F04, were accepted in T23. Only T23/T24 scoped files are eligible for the release commit; the DOCX plan, T15 loop state, scratch generator, unrelated T16–T18 line-ending changes, and regenerated evidence remain excluded.

## Git release

Git branch, commit, merge, and push references will be recorded in `AgentReport/WebMigration_T24_Memory.md` after the release actions complete.
