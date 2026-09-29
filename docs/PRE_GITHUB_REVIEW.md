# Pre-GitHub Review

Review date: 2026-09-25. Scope: feature-neutral infrastructure, source organization,
Database First integrity, setup, and readiness for an initial GitHub upload.

## 1. Executive Summary

GITHUB STATUS: READY

No unresolved CRITICAL or HIGH findings. Five MEDIUM and two LOW findings were
fixed. The three-layer base builds and starts, Swagger works, and the existing
database is reachable. No authentication or business feature was implemented.
No database data/schema changes, commit, or push were performed.

This directory has no `.git` metadata. Readiness means the reviewed files are
prepared for an initial repository; it does not certify an existing index,
commit history, remote, or push. Git initialization and publication remain with
the owner. All changes were compared with a pre-edit file snapshot instead.

## 2. Architecture

Preserved `Presentation -> BusinessLogic -> DataAccess -> SQL Server`.
Program remains a composition root. Its DataAccess import calls the DI extension
only; it contains no database query or repository construction. The project
reference remains Presentation to BusinessLogic only. Future controllers will
call services, and services will coordinate repositories.

No generic business service, UnitOfWork, CQRS, extra architectural layer,
authentication, JWT, or feature workflow was added.

## 3. Folder / Source Organization

All application code is under the three projects in `src/`, organized by layer.
Controllers, exception handling, configuration, DTO placeholders, services,
mappings, generated entities, context, and repositories are correctly located.

Root setup now includes `SelfStorageManagementSystem.sln`, `CODEX_RULE.md`,
`README.md`, `.gitignore`, and this report. The entire original rule file was read
from `Agent/Codex/CODEX_RULE.md` because the root copy was missing. Its contents
were copied unchanged to the requested root path; the original was preserved.

Existing `Agent/`, `DB/`, `SRS/`, and the root workbook are supporting materials,
not additional application source roots. Existing local `.vs`, `bin`, `obj`,
and `.csproj.user` artifacts are ignored and were not deleted.

The sole `.slnx` was replaced by an equivalent `.sln` with the same three projects
and `src` solution folder. SDK 8.0.425 reproduced an unsupported-header error for
the original `.slnx` and successfully built the replacement. SLNX CLI support
starts in SDK 9.0.200, as described by
[Microsoft](https://devblogs.microsoft.com/dotnet/introducing-slnx-support-dotnet-cli/).
No target framework was upgraded.

## 4. Project References

| Project | Application reference | Framework |
| --- | --- | --- |
| Presentation | BusinessLogic only | net8.0 |
| BusinessLogic | DataAccess only | net8.0 |
| DataAccess | None | net8.0 |

No circular or reverse references. All projects enable nullable reference types
and implicit usings. Project files needed no edits.

## 5. Database-First Integrity

No migrations, `EnsureCreated`, `Database.Migrate`, `MigrationBuilder`, or
Code First startup logic in application source. No application trigger bypass.
The clean-install SQL script was inspected but never executed.

Read-only SQL catalog queries and the EF model verified:

| Object | Live SQL Server | EF model |
| --- | ---: | ---: |
| Base tables | 57 | 57 keyed entities |
| Views | 2 | 2 keyless view entities |
| Foreign keys | 139 | 139, matching constraint names |
| Enabled triggers | 35 | 35, matching table/trigger names |
| Table and view columns combined | 598 | Matching object/column names |

All 57 primary-key column orders were compared with SQL Server. The three
composite keys are `(payment_id, invoice_id)`, `(shift_id, employee_id)`, and
`(user_id, role_id)`. `UnitTypeHaTDT` and `StorageUnitHaTDT` remain `HasNoKey`
and `ToView` mappings in `core`.

The live database has 178 check constraints, 47 bigint identity columns, and one
smallint identity column. Incorrect historical report counts were corrected.

All 59 entity files and the generated context match their original SHA-256
hashes: **60/60 unchanged**. Entities contain scaffolded properties/navigation
collections; no handwritten business methods were found. Without Git history,
changes predating this review cannot be compared with a previous commit.

## 6. DbContext

The constructor receives `DbContextOptions<SelfStorageDbContext>`. No embedded
connection string, credential, `OnConfiguring`, manual JSON loading, or runtime
manual context construction. Generated mappings remain intact.

Trigger metadata is present for all live triggers. No mapping defect requiring
generated code changes was found. This read-only review did not exercise SQL
write/trigger execution.

## 7. Repository

`IRepository<T>` and `GenericRepository<T>` agree on all ten required operations.
`GetByIdAsync(object[] keyValues, CancellationToken)` delegates to EF `FindAsync`.
Safe missing-key reads verified bigint, smallint, nonidentity, and all three
composite-key types. Correct CLR value types and key ordering remain required.

`GetAllAsync`, `FindAsync`, and `FirstOrDefaultAsync` use local `AsNoTracking`.
The default global tracking mode is unchanged. Read methods were exercised
without tracked entries. No `ChangeTracker.Clear` or exception swallowing exists.

`AddAsync`, `Update`, and `Delete` only stage changes; persistence is explicit
through `SaveChangesAsync`. The Update documentation warns against incomplete
detached entity updates. No write operation or SaveChanges was invoked in tests.
Both keyless views were queried successfully; no handwritten caller attempts
key lookup or mutation on them.

## 8. BusinessLogic

One shared `ApiResponse<T>` envelope; no competing wrapper. `PagedRequest`
normalizes PageNumber to at least 1 and PageSize to 1..100. Boundary cases and
`PagedResult<T>.TotalPages` were checked. No current in-memory pagination exists.
The result type carries metadata; future persistence queries must apply
Skip/Take before materialization.

The five application exceptions have no HTTP/framework dependencies.
BusinessLogic contains no direct EF infrastructure usage or business CRUD base
service. Existing DTO/service/mapping placeholders remain feature-neutral.

## 9. Presentation

`BaseController` remains abstract and minimal with `[ApiController]` and
`[Route("api/[controller]")]`. No concrete business controller, direct repository
access, EF queries, or entity response exposure exists.

Middleware order remains exception handler, Development Swagger, HTTPS
redirection, CORS, authorization, and controller mapping. Authentication is absent.
No temporary review controller or debug endpoint was added to the repository.

## 10. Dependency Injection

DataAccess uses `AddDbContext<SelfStorageDbContext>` and reads
`ConnectionStrings:DefaultConnection` from configuration. Context and generic
repository are Scoped; no duplicate or singleton registration was found.

Tests resolved the same instance within a scope and different instances across
scopes for both context and repository. The application DI configuration also
successfully connected to the database. BusinessLogic DI is a future-service hook.

## 11. Exception Handling

Verified mappings: BadRequest 400, Unauthorized 401, Forbidden 403, NotFound 404,
Conflict 409, and unexpected exceptions 500. Unexpected response bodies use the
generic message and do not contain the synthetic private-detail marker used in
tests. Unknown SQL failures are not globally translated to business errors.

Fixed cancellation classification: an `OperationCanceledException` is treated
as client cancellation only when `RequestAborted` is cancelled. That branch
does not write a response. Server-side cancellation/timeouts on an active
request now flow to the generic 500 response. `TaskCanceledException` was also
tested. Custom HTTP 499 assignment was removed; framework disconnect handling
is retained. 499 is a convention, not a standard application success/error API.

The `Response.HasStarted` guard returns false without rewriting the response.
Unexpected errors use `ILogger`; no runtime Console/Debug logging, blocking
async calls, catch-and-return-false/null, or explicit credential logging exists.

## 12. Validation

Found and fixed an ordering defect: custom `ApiBehaviorOptions` configuration
preceded `AddControllers`, whose MVC defaults replaced the response factory.
`AddControllers` now runs first.

Verified HTTP 400 with `ApiResponse<object?>` and `Dictionary<string,string[]>`
for missing required input, invalid numeric JSON input, and malformed JSON.
Exception-backed ModelState errors with no message use a safe nonempty fallback;
raw exception objects/messages are not serialized. The null-forgiving operator
on ModelState entries is justified by the immediately preceding non-null filter.

## 13. Swagger

Final actual API smoke test in Development:

- `/swagger`: HTTP 200 after the normal UI redirect.
- `/swagger/v1/swagger.json`: HTTP 200, OpenAPI 3.0.1.
- Zero operation paths, expected for a base without feature controllers.
- No JWT/Bearer security scheme.

Actual API in Production returned HTTP 404 for Swagger JSON.

## 14. CORS

`FrontendPolicy` reads `Cors:AllowedOrigins`. The current explicit origins are
`http://localhost:5173` and `http://localhost:3000`; there is no wildcard origin
or credentials allowance. Empty origins result in no allowed origin.

HTTP preflight checks accepted the configured localhost origin and omitted
allow-origin headers for an untrusted origin. The actual Production API also
rejected the untrusted origin. Configure real frontend origins when deploying;
the current shared settings are local-development defaults.

## 15. Security / Secret Scan

No credential values found in reviewed repository content. Searched for
password/connection-login assignments, secrets, API keys, tokens, Bearer,
private keys, and common sample-password markers. Matches were rule prose,
cancellation-token identifiers, or schema columns such as secret references
and digests, not embedded credentials.

Inspected source, settings, launch profiles, SQL, rules, reports, the diagram,
and textual XML/relationship parts of DOCX/XLSX files. The SRS ZIP's five members
were hash-matched to the inspected extracted files. Binary/IDE/build artifacts
are excluded from upload by ignore rules; Git history is unavailable.

Local Windows Integrated Authentication is preserved. README documents User
Secrets/environment variables. No password values were added to any report.

## 16. NuGet Packages

| Project | Direct package | Requested | Resolved |
| --- | --- | --- | --- |
| DataAccess | Microsoft.EntityFrameworkCore | 8.0.* | 8.0.31 |
| DataAccess | Microsoft.EntityFrameworkCore.SqlServer | 8.0.* | 8.0.31 |
| DataAccess | Microsoft.EntityFrameworkCore.Design | 8.0.* | 8.0.31 |
| DataAccess | Microsoft.EntityFrameworkCore.Tools | 8.0.* | 8.0.31 |
| Presentation | Microsoft.EntityFrameworkCore.Design | 8.0.* | 8.0.31 |
| Presentation | Swashbuckle.AspNetCore | 6.6.2 | 6.6.2 |
| BusinessLogic | None | — | — |

Design/Tools references are private development assets. Presentation Design
supports EF startup tooling; no package was removed on an assumption of disuse.
No EF9, Configuration9, or prohibited application library was introduced.
Identity-related transitive dependencies of the SQL provider do not implement
application authentication.

`dotnet list ... package --vulnerable --include-transitive` reported no vulnerable
packages for all three projects using the configured sources. This is a result
from the current advisory feeds, not a guarantee about future vulnerabilities.
Floating `8.0.*` versions are retained; a future restore can resolve a newer 8.0
patch, so this review certifies the versions shown above.

## 17. Git Hygiene

Actual initial/final `git status` and `git diff` could not inspect an index or
history because no Git repository exists in this directory or its parents.
No repository was initialized in the project, and no staging/commit/push occurred.

Added `.gitignore` for `.vs`, bin/obj, user/suo files, test output, local secret
configuration, certificate/private-key files, local database files, logs, and
backups. Used separate temporary Git metadata outside the project to check
ignore behavior and enumerate prospective upload files. Required source, docs,
rules, README, and SQL remain included; build/IDE artifacts remain excluded.

Used SHA-256 snapshots and `git diff --no-index` to review changed existing
files. The only application source edits are Program and GlobalExceptionHandler.
No temporary review code, debug endpoint, package, or parallel source root remains.

## 18. README

Created a concise README covering the actual stack, architecture, layout,
database, prerequisites, configuration, build/run commands, Swagger, and status.
It explicitly states that features are unimplemented. It also explains that
the supplied SQL clean-install script deletes the existing database, so it is
not an update step for an existing installation.

## 19. Build

Initial and final sequences ran `dotnet clean`, `dotnet restore`, and `dotnet build`.
All three final commands exited 0. Final build summary:

```text
Build succeeded.
    11 Warning(s)
    0 Error(s)
```

All warnings are CS8981 in generated entities. No handwritten-code warning.
The default installed SDK was 10.0.401, targeting net8.0; a separate build
explicitly selected SDK 8.0.425 and also passed with the same 11 warnings.
Every meaningful source change was followed by a build.

`dotnet format --verify-no-changes --no-restore --include <handwritten files>`
exited 0. Generated files were excluded, and no mass formatting was performed.

Exact command output, package inventory/audit, snapshots, and regression logs
are retained outside the repository at
`%TEMP%\SelfStorage-pre-github-20260925\`, including `final-clean.txt`,
`final-restore.txt`, `final-build.txt`, `sdk8-build.txt`, `package-audit.txt`,
`probe-runtime.txt`, `http-checks.txt`, and `final-runtime.txt`.

## 20. Runtime

The actual API started before changes and again after all source fixes using
`dotnet run --no-build --project src/SelfStorageManagementSystem.Presentation --launch-profile http`.
The Production smoke test used `--no-launch-profile --environment Production`
on a separate loopback port. All processes were stopped with Ctrl+C, and the
review ports were checked for remaining listeners.

An external temporary harness used a copy of the final Program registrations
and pipeline, referenced the actual project assemblies, and supplied temporary
probe controllers only outside the repository. Results: **155 direct checks**
and **9 HTTP regression checks passed**. These supplement the actual API smoke
test; the base itself intentionally has no business endpoints.

The HTTP-only smoke profile emits the expected HTTPS-port warning when no HTTPS
endpoint is supplied. README uses the existing HTTPS launch profile for ordinary
local use. Production TLS/listener configuration is a deployment concern.

## 21. Database Connectivity

Database reachable: **YES**.

Confirmed through application DI/EF `CanConnectAsync`, repository reads, keyless
view reads, SQL system catalog SELECTs, and a final SQLCMD integrated-authentication
connection to `SelfStoragePRN222`. No connection credentials were printed.

No INSERT, UPDATE, DELETE, TRUNCATE, DROP, ALTER, test data, schema migration,
trigger modification, or clean-install script execution occurred. Persistence
and trigger business behavior are intentionally not write-tested in this review.

## 22. Findings

| ID | Severity | File | Issue | Impact | Fix | Status |
| --- | --- | --- | --- | --- | --- | --- |
| F01 | MEDIUM | Presentation/Program.cs | MVC registration overwrote validation factory; empty exception-backed errors lacked fallback | Invalid requests violated the promised response envelope | Register controllers before custom options and provide safe fallback | FIXED; direct and HTTP regressions passed |
| F02 | MEDIUM | Presentation/ExceptionHandling/GlobalExceptionHandler.cs | Every cancellation treated as client disconnect | Server-side cancellation/timeouts mislabeled 499 | Require RequestAborted and avoid writing to disconnected client | FIXED; cancellation and generic-500 tests passed |
| F03 | MEDIUM | SelfStorageManagementSystem.slnx | Sole solution unsupported by .NET 8 SDK | Documented SDK could not open/build the solution | Replace with equivalent .sln | FIXED; SDK 8.0.425 build passed |
| F04 | MEDIUM | .gitignore (missing) | No exclusions for existing build/IDE/local artifacts | Initial broad add could include generated output or local configuration | Add and verify ignore rules | FIXED |
| F05 | MEDIUM | docs/DB_SCHEMA_REPORT.md; docs/BASE_ARCHITECTURE_REPORT.md | Incorrect SQL counts, obsolete repository advice, blanket SQL-error translation guidance, stale behavior claims | Could mislead future persistence/error handling work | Correct verified facts and align with actual base | FIXED |
| F06 | LOW | CODEX_RULE.md (missing at root) | Mandatory entry path unavailable | Future tasks could miss rules | Copy complete original rules to root; preserve original | FIXED |
| F07 | LOW | README.md (missing) | No root setup/status guide | Setup ambiguity and risk of rerunning destructive installer | Add accurate setup guide and installer warning | FIXED |
| I01 | INFO | DataAccess/Entities/*.cs | 11 CS8981 lowercase-name warnings | Harmless generated naming warnings | Preserve scaffolded names | ACCEPTED |
| I02 | INFO | Repository root | No Git metadata | Cannot certify index/history or prior changes | Verify prospective file set, ignore rules, and snapshot diffs | ACCEPTED; initial publication is outside this review |
| I03 | INFO | DataAccess and Presentation project files | EF package patch versions float within 8.0 | Future restore may differ from audited 8.0.31 | Record resolved versions; keep existing policy | ACCEPTED |
| I04 | INFO | HTTP-only runtime smoke configuration | HTTPS redirection cannot infer a port | Warning during deliberate HTTP-only tests | Document HTTPS profile and deployment configuration | ACCEPTED |

Counts: CRITICAL 0; HIGH 0; MEDIUM 5 fixed; LOW 2 fixed; INFO 4 accepted.
No unresolved MEDIUM finding.

## 23. Fixes Applied

Created:

- `.gitignore`
- `CODEX_RULE.md` (unchanged copy of the original rules)
- `README.md`
- `SelfStorageManagementSystem.sln`
- `docs/PRE_GITHUB_REVIEW.md`

Modified:

- `src/SelfStorageManagementSystem.Presentation/Program.cs`
- `src/SelfStorageManagementSystem.Presentation/ExceptionHandling/GlobalExceptionHandler.cs`
- `docs/DB_SCHEMA_REPORT.md`
- `docs/BASE_ARCHITECTURE_REPORT.md`

Replaced: `SelfStorageManagementSystem.slnx` with the `.sln` above. No application
project/package, repository implementation, entity, generated context, SQL file,
or business feature was changed.

## 24. Remaining Warnings

The 11 CS8981 warnings affect `appointment`, `facility`, `inspection`, `invoice`,
`notification`, `payment`, `promotion`, `refund`, `reservation`, `role`, and `user`.
They are intentional scaffold naming, not compilation errors. See INFO findings
for Git-history, package-version, and HTTP-only verification limits.

## 25. GitHub Readiness

- [x] Complete Codex rules and both previous reports read.
- [x] Layer-first source layout under src preserved.
- [x] Correct project references; no circular dependency.
- [x] Database First intact; no migrations.
- [x] Generated entities/context protected and hash-verified.
- [x] Scoped context/repository and DI verified.
- [x] No manual runtime DbContext construction or ChangeTracker.Clear.
- [x] Single/composite keys supported; explicit SaveChanges strategy.
- [x] BusinessLogic independent of direct EF infrastructure and HTTP types.
- [x] No generic business CRUD service.
- [x] Minimal controller; no controller repository/DbContext access.
- [x] Centralized exception handling with correct mappings and safe errors.
- [x] Consistent model validation envelope verified.
- [x] Swagger operational; CORS restricted and tested.
- [x] Authentication/business features remain unimplemented.
- [x] No secret found in prospective upload content; ignore rules verified.
- [x] No bin/obj/.vs in prospective upload set.
- [x] README accurately describes setup and feature status.
- [x] Restore/build succeed, zero compile errors, only known generated warnings.
- [x] Runtime and Swagger smoke tests pass; database connectivity succeeds.
- [x] No database data/schema modification.
- [x] All review changes examined against baseline; no temporary code in project.
- [x] Review processes stopped.

Git index/history checks are **not applicable**, because Git is not initialized;
this exception is explicit rather than an assertion that an index was checked.
The owner still needs to initialize the repository and inspect the staged set
before making the initial commit. No commit or push was made by this review.

GITHUB STATUS: READY
