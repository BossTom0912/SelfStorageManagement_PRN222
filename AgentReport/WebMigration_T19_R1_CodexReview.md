# WebMigration T19 R1 — independent Codex review

- Reviewed report: `AgentReport/WebMigration_FinalHandover.md`
- Report SHA-256: `929566881A32191E2E7495E41690A3CB0AC4D46C64A75C5E11049B390C5B882C` (identical in `Final_Project_Web` and `Final_Project`). Review this path/hash only once.
- Result: **Changes requested.** T19 remains IMPLEMENTED; T20–T22 remain WAITING.

## Findings

[P1] Replace the incorrect T18 evidence hashes — AgentReport/WebMigration_FinalHandover.md:82

The handover manifest lists 21 of 24 PNG SHA-256 values differently from both the R6 report and the actual files in both workspaces. For example, `T18_01_Customer_Catalog_Initial_Context2_Customer.png` is `042C43194480C1361A8A5EAAF17D457E450EE294F889D610F4EC78C5CB7849FF`, while T19 lists `042C43194480A145...`. A recipient checking the handover will reject valid evidence or accept a wrong manifest. Copy the exact hashes from the verified R6 report and verify all 24 against both workspaces.

[P1] Correct the Hub and receiver payload contract — AgentReport/WebMigration_FinalHandover.md:179

The handover claims typed request objects for all three Hub methods and a DTO for `Receiver_DeleteStorageUnit`. `StorageUnitHub.cs` actually exposes `HubCreate_StorageUnit(string dataObj)`, `HubUpdate_StorageUnit(string dataObj)`, and `HubDelete_StorageUnit(string id, string? expectedUpdatedAt = null)`; `StorageUnitRealtimePublisher.cs` sends a string unit ID for delete. A client built from the handover's signatures will invoke the wrong protocol.

[P1] Correct the VNPAY callback URLs — AgentReport/WebMigration_FinalHandover.md:48

The report directs integrations to `/Payments/VnpayIpn` and `/Payments/VnpayReturn`. `PaymentsApiController` has `[Route("api/payments")]` with `[HttpGet("vnpay/ipn")]` and `[HttpGet("vnpay/return")]`, so the actual endpoints are `/api/payments/vnpay/ipn` and `/api/payments/vnpay/return`. Using the documented IPN URL would prevent financial callbacks.

[P2] List the real six SQL triggers — AgentReport/WebMigration_FinalHandover.md:153

Five of the six trigger names in the handover do not exist in the verified WebTest database. An independent `sys.triggers` query after 19/19 SQL concurrency tests found `trg_agreement_status_transition`, `trg_invoice_lines_guard`, `trg_invoice_lines_refresh_totals`, `trg_invoice_status_transition`, `trg_payment_allocation_guard`, and `trg_reservation_status_transition`, all enabled. The invented names make the stated post-test check impossible to reproduce.

[P2] Repair the legacy test totals — AgentReport/WebMigration_FinalHandover.md:148

The line says 225 passed and 8 skipped, then says total 244; those numbers cannot describe one run. An independent default `dotnet test SelfStorageManagementSystem.sln --no-restore` run observed 225 passed, 19 skipped, total 244. A separate run with `SELFSTORAGE_SQL_TEST_CONNECTION_STRING` against WebTest observed 19 passed, 0 skipped. State these as separate runs.

[P2] Use the actual role codes and concurrency token type — AgentReport/WebMigration_FinalHandover.md:23

`RoleConstants.cs` defines `storage_customer`, `facility_staff`, `facility_manager`, `business_operations_manager`, and `system_administrator`; there is no `Guest` role. The handover also calls `ExpectedUpdatedAt` a `RowVersion`/`Timestamp`, but the request and page models use a `DateTimeOffset` value. These descriptions would mislead callers implementing authorization or concurrency handling.

[P2] State the real Git entry condition for T20 — AgentReport/WebMigration_FinalHandover.md:203

The handover says T20 can start only from a clean working tree except for three excluded files. T19 itself has three uncommitted task files, and the worktree also reports preexisting line-ending normalization on prior T16–T18 files. The clean condition is currently false and cannot hold until the authorized later commit. Describe the scoped uncommitted T19 files and the line-ending noise accurately, and require that the eventual commit excludes both unrelated changes and the three protected files.

## Independent evidence and limit

- T19 report, memory, and TaskBoard hashes match between workspaces; the new TaskBoard diff keeps T19 IMPLEMENTED and T20–T22 WAITING.
- `dotnet test SelfStorageManagementSystem.Web.sln --no-restore --verbosity quiet`: 449 passed, 0 failed, 0 skipped.
- `dotnet test SelfStorageManagementSystem.sln --no-restore --verbosity quiet`: 225 passed, 0 failed, 19 skipped. Separate SQL concurrency filter with the WebTest connection string: 19 passed, 0 failed, 0 skipped; all six queried business triggers remained enabled.
- All 24 actual T18 PNGs match the R6 manifest in both workspaces. This review concerns inaccuracies introduced in the T19 handover, not regressions in the previously verified implementation.
