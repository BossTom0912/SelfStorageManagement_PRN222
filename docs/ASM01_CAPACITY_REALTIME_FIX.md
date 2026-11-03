# ASM01 capacity and SignalR integration

This change ports the Thanh Ha ASM01 fixes into the consolidated solution on
`main`. It retains the existing `KhoiVTH` service/repository names, DI bindings,
MVC and Razor Pages routes, and disabled browser console logging. The four
removed assignment directories are not restored.

## Resulting behavior

- An available unit can be deleted when the remaining candidate capacity still
  covers active, unassigned reservation holds and confirmed reservations.
- Removing a unit outside the candidate pool does not consume candidate capacity.
- Authorization, facility scope, optimistic timestamps, capacity locks, and
  database transactions remain in place.
- Referenced units are retained and unlisted. The DAL/BLL result carries
  `IsUnlistedOnly`; MVC, Razor Pages, and the Hub publish updates for unlisting
  and deletion events only for actual deletion.
- Delete confirmation uses the repository's complete reference summary, including
  status history and transfers, to describe the operation accurately.
- Realtime tables retrieve authorized DTO snapshots through
  `HubGet_StorageUnitsTableSnapshot` instead of fetching a complete HTML page.
  Invalid or superseded snapshots preserve the existing DOM. Filtering,
  pagination, reconnect, retry, and bigint string IDs are preserved.
- SignalR traffic remains visible in browser developer tools. The change removes
  the additional full-page HTML fetch; it does not hide network traffic.

## Verification on 2026-11-03

- `dotnet build SelfStorageManagementSystem.sln`: succeeded with no errors.
  Existing generated-entity, nullable, and SQL-test analyzer warnings remain.
- BusinessLogicTests: 38 passed.
- Focused delete/capacity/MVC/Hub tests: 36 passed.
- Edge browser tests: both the client suite (including a no-HTTP-fetch, bigint,
  and escaping case) and the real Kestrel/WebSocket sequencing/retry test passed.

The test assemblies were rebuilt from this worktree before validation. Test
fixtures used isolated InMemory databases for these application/browser checks;
these results do not establish SQL Server locking/concurrency correctness.

The broad web suite is not claimed green. These reservation tests also fail on
the unmodified `origin/main` baseline (`35945cc`):

- `MultipleCountdowns_OnIndexAndMyReservations_RenderCorrectRowAttributes_WithFacilityTimeZone`
- `CatalogCompatibility_RedirectsToCreatePage_WhenFacilityAndUnitTypeProvided`
- `HoldDeadline_UsesEachFacilityTimezone_OnDetailsAndBothLists_WithoutChangingUtcInstant`

The separate T18 two-browser SQL suite requires its dedicated
`SelfStoragePRN222_WebE2eTest` database and was unavailable during the broad run.
No application database, schema, or existing reservation was changed for this
integration.
