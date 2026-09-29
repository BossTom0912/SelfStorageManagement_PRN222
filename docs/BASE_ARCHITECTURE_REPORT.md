# Base Architecture Report — Self-Storage Facility Rental and Management System

**Project:** Self-Storage Facility Rental and Management System (PRN222)  
**Architecture:** Strict 3-Layer Architecture  
**Target Framework:** .NET 8 (`net8.0`)  
**Audit Date:** 2026-09-25  
**Audit Status:** PASSED — READY FOR FEATURE DEVELOPMENT  

---

## 1. Solution Overview
- **Solution File:** `SelfStorageManagementSystem.sln`
- **Application Projects:**
  1. `SelfStorageManagementSystem.Presentation` (ASP.NET Core Web API)
  2. `SelfStorageManagementSystem.BusinessLogic` (Class Library)
  3. `SelfStorageManagementSystem.DataAccess` (Class Library)
- **Database Engine:** Microsoft SQL Server 2019+ (`SelfStoragePRN222`, schema `core`)

---

## 2. Technology Stack
- **C# / .NET:** .NET 8 SDK (`net8.0`)
- **Web Framework:** ASP.NET Core Web API
- **ORM:** Entity Framework Core 8 (`8.0.31`) Database First
- **Database Provider:** `Microsoft.EntityFrameworkCore.SqlServer` (`8.0.31`)
- **Documentation & UI:** Swashbuckle ASP.NET Core (`6.6.2`)
- **Dependency Injection:** ASP.NET Core built-in DI (`IServiceCollection`)
- **Logging:** Microsoft Extensions Logging (`ILogger<T>`)
- **Strictly Excluded Libraries:** AutoMapper, FluentValidation, MediatR, Dapper, Serilog, MassTransit, Redis, RabbitMQ.

---

## 3. Project Structure
```text
SelfStorageManagementSystem/
├── SelfStorageManagementSystem.sln
├── CODEX_RULE.md
├── docs/
│   ├── DB_SCHEMA_REPORT.md
│   └── BASE_ARCHITECTURE_REPORT.md
└── src/
    ├── SelfStorageManagementSystem.Presentation/
    │   ├── Controllers/
    │   │   └── BaseController.cs
    │   ├── ExceptionHandling/
    │   │   └── GlobalExceptionHandler.cs
    │   ├── Configuration/
    │   ├── Properties/
    │   │   └── launchSettings.json
    │   ├── appsettings.json
    │   ├── appsettings.Development.json
    │   └── Program.cs
    │
    ├── SelfStorageManagementSystem.BusinessLogic/
    │   ├── Common/
    │   │   ├── ApiResponse.cs
    │   │   ├── PagedRequest.cs
    │   │   └── PagedResult.cs
    │   ├── DTOs/
    │   │   ├── Requests/
    │   │   └── Responses/
    │   ├── Exceptions/
    │   │   ├── BadRequestException.cs
    │   │   ├── UnauthorizedException.cs
    │   │   ├── ForbiddenException.cs
    │   │   ├── NotFoundException.cs
    │   │   └── ConflictException.cs
    │   ├── Services/
    │   │   ├── Interfaces/
    │   │   └── Implementations/
    │   ├── Mappings/
    │   └── DependencyInjection/
    │       └── ServiceCollectionExtensions.cs
    │
    └── SelfStorageManagementSystem.DataAccess/
        ├── Context/
        │   └── SelfStorageDbContext.cs
        ├── Entities/
        │   └── (59 generated entity files)
        ├── Repositories/
        │   ├── Interfaces/
        │   │   └── IRepository.cs
        │   └── Implementations/
        │       └── GenericRepository.cs
        └── DependencyInjection/
            └── ServiceCollectionExtensions.cs
```

---

## 4. Project Dependencies
Strict unidirectional layering verified:
```text
Presentation
    ↓ (ProjectReference)
BusinessLogic
    ↓ (ProjectReference)
DataAccess
    ↓
SQL Server
```
- `Presentation` references only `BusinessLogic`.
- `BusinessLogic` references only `DataAccess`.
- `DataAccess` has no application-level references.
- Circular references: **NONE**. Reverse references: **NONE**.

---

## 5. Database First Model
- **Scaffold Source:** `Server=localhost;Database=SelfStoragePRN222;Trusted_Connection=True;TrustServerCertificate=True;`
- **Output:** 59 entity files in `DataAccess/Entities/` (57 base tables + 2 compatibility views) and `SelfStorageDbContext` in `DataAccess/Context/`.
- **Foreign Keys:** 139 foreign keys fully mapped with navigation properties.
- **Composite Primary Keys:** 3 entities mapped with `HasKey(e => new { ... })`:
  1. `payment_allocations`: `(payment_id, invoice_id)`
  2. `shift_assignments`: `(shift_id, employee_id)`
  3. `user_roles`: `(user_id, role_id)`
- **Keyless Views:** `UnitTypeHaTDT` and `StorageUnitHaTDT` mapped with `HasNoKey().ToView(...)`.
- **Code First Artifacts:** No migrations, no `EnsureCreated()`, no `Database.Migrate()`.

---

## 6. DbContext Configuration
- **Connection String Protection:** Scaffolded with `--no-onconfiguring`. No hardcoded credentials, no `OnConfiguring` override, and no `ConfigurationBuilder` inside `SelfStorageDbContext`.
- **Lifetime:** Registered as `Scoped` via `services.AddDbContext<SelfStorageDbContext>(...)`.
- **Change Tracking:** Default ChangeTracker preserved globally. No global `QueryTrackingBehavior.NoTracking`.

---

## 7. Repository Base
- **Abstraction:** `IRepository<T> where T : class`.
- **Implementation:** `GenericRepository<T> : IRepository<T>`.
- **Constructor Injection:** `SelfStorageDbContext` injected; no manual instantiation, no parameterless constructor.
- **Primary Key Strategy:** Uses `object[] keyValues` with EF Core `DbSet.FindAsync(keyValues, cancellationToken)`. Supports single-column PKs and composite PKs seamlessly.
- **Local Read Tracking:** `GetAllAsync`, `FindAsync`, and `FirstOrDefaultAsync` apply `.AsNoTracking()` locally.
- **Explicit Persistence:** `AddAsync`, `Update`, and `Delete` only stage changes in the ChangeTracker; `SaveChangesAsync(cancellationToken)` must be explicitly called.
- **No ChangeTracker Pollution:** No `ChangeTracker.Clear()` calls.

---

## 8. BusinessLogic Base
- **Response Wrapper:** `ApiResponse<T>` with `Success`, `Message`, `Data`, `Errors`. Completely decoupled from ASP.NET Core HTTP types.
- **Pagination Models:**
  - `PagedRequest`: Default `PageNumber = 1`, `PageSize = 10`, max `PageSize = 100`. Auto-normalizes invalid parameters.
  - `PagedResult<T>`: Holds items, `PageNumber`, `PageSize`, `TotalCount`, and calculated `TotalPages`.
- **Application Exceptions:** `BadRequestException`, `UnauthorizedException`, `ForbiddenException`, `NotFoundException`, `ConflictException` inheriting from `System.Exception` without HTTP dependencies.
- **Architectural Boundary:** No direct EF Core infrastructure types (`DbSet`, `EntityEntry`, `ChangeTracker`, `UseSqlServer`) inside BusinessLogic.

---

## 9. Presentation Base
- **Controller Conventions:** Abstract `BaseController` provides `[ApiController]` and `[Route("api/[controller]")]`.
- **Boundary Enforcement:** Controllers do not inject `DbContext` or `IRepository<T>`. Controllers interact exclusively with BusinessLogic services.
- **Response Consistency:** Controllers and handlers return `ApiResponse<T>` envelopes.

---

## 10. Dependency Injection
- **Registration Flow:**
  - `DataAccess`: Registers `SelfStorageDbContext` (Scoped) and `IRepository<>` -> `GenericRepository<>` (Scoped).
  - `BusinessLogic`: Exposes `AddBusinessLogic()` for future service registrations.
  - `Presentation`: Acts as composition root in `Program.cs`:
    ```csharp
    builder.Services.AddDataAccess(builder.Configuration);
    builder.Services.AddBusinessLogic();
    ```
- **Duplicate Registrations:** NONE.

---

## 11. Exception Handling
- **Mechanism:** Implements .NET 8 `IExceptionHandler` in `GlobalExceptionHandler`.
- **Status Mappings:**
  - `BadRequestException` ➔ 400 Bad Request
  - `UnauthorizedException` ➔ 401 Unauthorized
  - `ForbiddenException` ➔ 403 Forbidden
  - `NotFoundException` ➔ 404 Not Found
  - `ConflictException` ➔ 409 Conflict
  - Client-aborted `OperationCanceledException` ➔ no body write; ASP.NET Core handles disconnect status. Cancellation without an aborted request remains an unexpected HTTP 500.
  - Other Unhandled Exceptions ➔ 500 Internal Server Error (`"An unexpected error occurred."`)
- **Safety:** Guards against `Response.HasStarted`. Never leaks stack traces, SQL error details, or credentials. Logs unhandled 500 errors with `ILogger.LogError`.

---

## 12. Validation
- **Model Validation:** Configured via `ApiBehaviorOptions.InvalidModelStateResponseFactory`.
- **Envelope:** Transforms `ModelStateDictionary` into `Dictionary<string, string[]>` and wraps inside `ApiResponse<object?>` with HTTP 400 Bad Request.
- **Registration:** `AddControllers()` runs before the custom `ApiBehaviorOptions` configuration so MVC defaults do not overwrite the response factory. Empty model-binding messages use a safe fallback.

---

## 13. Swagger
- **Environment:** Enabled in `Development` (`app.Environment.IsDevelopment()`).
- **Endpoints:** `/swagger` and `/swagger/v1/swagger.json`.
- **Security:** No fake JWT Bearer security scheme configured prematurely.

---

## 14. CORS
- **Policy Name:** `FrontendPolicy`.
- **Configuration Source:** `Cors:AllowedOrigins` in `appsettings.json` and `appsettings.Development.json`.
- **Default Origins:** `http://localhost:5173`, `http://localhost:3000`.
- **Production Safety:** Does not use `AllowAnyOrigin()` blindly.

---

## 15. Security Baseline
- **Database Credentials:** Windows Integrated Authentication (`Trusted_Connection=True`). No credential values found in the reviewed source/configuration; Git history is unavailable because this folder is not initialized as a repository.
- **Information Leakage:** Stack traces and internal database errors are completely shielded from client responses.
- **Data Protection:** Database-generated files remain unmodified during the final review, verified against a SHA-256 baseline. This folder has no Git metadata, so no historical tracked-file claim can be made.

---

## 16. Build Status
- `dotnet clean`: Succeeded.
- `dotnet restore`: Succeeded.
- `dotnet build`: Succeeded with **0 Errors**.

---

## 17. Runtime Status
- **Application Startup:** Verified on `http://localhost:5132`.
- **Swagger JSON:** Verified `GET http://localhost:5132/swagger/v1/swagger.json` returns HTTP 200 OK with valid OpenAPI 3.0.1 specification.

---

## 18. Known Warnings
- **Warning Count:** Exactly 11 warnings (CS8981).
- **Code:** `CS8981: The type name '<name>' only contains lower-cased ascii characters.`
- **Affected Files:** 11 scaffolded single-word entity classes in DataAccess (`appointment`, `facility`, `inspection`, `invoice`, `notification`, `payment`, `promotion`, `refund`, `reservation`, `role`, `user`).
- **Assessment:** Benign informational compiler warnings originating from EF Core reverse engineering obeying `--use-database-names`. Must not be renamed to maintain DB-first integrity.

---

## 19. Architecture Rules Summary
1. **Source of Truth:** The database is the authoritative source of truth.
2. **Layer Separation:** Presentation -> BusinessLogic -> DataAccess. No layer skipping.
3. **No Entity Leakage:** Controllers return DTOs, never EF Core entities directly.
4. **Thin Controllers:** Controllers only handle HTTP model binding, service invocation, and status code return.
5. **No Repository in Presentation:** Controllers must never inject `IRepository<T>` or `DbContext`.
6. **No DB Logic in BusinessLogic:** Services coordinate domain workflows; persistence belongs in DataAccess.
7. **Explicit Save Changes:** Repositories do not auto-save on individual CUD operations.

---

## 20. Ready-for-Feature Checklist
- [x] 3-layer dependencies correct
- [x] No circular references
- [x] DB-first model intact
- [x] No Code First migration
- [x] No hard-coded secret
- [x] No DbContext in Controller
- [x] No Repository in Controller
- [x] No DbContext in normal BusinessLogic Service
- [x] GenericRepository supports composite PK
- [x] Explicit SaveChanges strategy
- [x] No ChangeTracker.Clear
- [x] Keyless views remain read-only
- [x] Exceptions centralized via IExceptionHandler
- [x] HTTP validation consistent with ApiResponse
- [x] Swagger operational
- [x] CORS configuration-driven
- [x] Authentication not prematurely implemented
- [x] Build has 0 errors
- [x] Runtime startup succeeds
- [x] Generated files protected

**BASE STATUS: READY FOR FEATURE DEVELOPMENT**
