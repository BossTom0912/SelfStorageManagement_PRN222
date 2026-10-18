# WebMigration T19 R1 fixes — independent Codex review

- Reviewed input: `AgentReport/WebMigration_T19_R1_FindingFixes.md`, SHA-256 `58855F38DCC0ADE818DF4923F2570A23609645F56B6AF1A2F594D67256A1FF78` (identical in both workspaces). This path/hash is reviewed once.
- Corrected handover: `AgentReport/WebMigration_FinalHandover.md`, SHA-256 `28BD72CAD791F62086F4944D7B013B186BF8F6A7E88FA0CAF36110827BF8DD19` (identical in both workspaces).
- Result: **Changes requested.** T19 remains IMPLEMENTED; T20–T22 remain WAITING.

## Finding

[P2] Describe the VNPAY return response accurately — AgentReport/WebMigration_T19_R1_FindingFixes.md:70

The R1 report says `/api/payments/vnpay/return` redirects the browser to `/Payments/Result?id=...&status=...`. `PaymentsApiController.VnpayReturn` actually builds the return page and ends with `return Content(html, "text/html; charset=utf-8")` at line 178; it never calls `Redirect` or routes through `Payments/Result`. A handover reader following the documented flow will look for a redirect and result-page behavior that this callback does not provide. Correct the R1 report's flow description and keep the actual route and display-only behavior.

## Independent verification

- Parsed all 24 PNG hashes from the corrected handover; every hash matches the R6 manifest and both on-disk workspace copies.
- Confirmed the three Hub method signatures and delete receiver string payload against `StorageUnitHub.cs` and `StorageUnitRealtimePublisher.cs`; confirmed the two VNPAY routes against `PaymentsApiController.cs`.
- Confirmed the five role codes, `DateTimeOffset` concurrency field, six enabled SQL trigger names, and the separate 225/19 default and 19/0 SQL test runs. No source changes were made in R1.

The remaining issue is confined to R1 documentation. No additional implementation test run is needed for this correction.
