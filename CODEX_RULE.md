# RULE.md — Codex Engineering Rules
## Self-Storage Facility Rental and Management System

> Target agent: Codex / Astra / code-review agents  
> Course: PRN222  
> Stack: .NET 8, ASP.NET Core Web API, EF Core 8, SQL Server  
> Architecture: Database First + 3-Layer Architecture

---

# 0. Mandatory Agent Behavior

Before doing any task:

1. Read this entire `RULE.md`.
2. Inspect the current repository state.
3. Inspect relevant existing source files before modifying anything.
4. Do not rely only on prior AI summaries.
5. Treat actual source code and actual SQL Server schema as authoritative.
6. Make the smallest coherent change that satisfies the task.
7. Build after every meaningful coding step.
8. Never silently change architecture, naming conventions, database schema, or folder structure.

If a new prompt conflicts with this file, priority is:

1. User's newest explicit instruction.
2. Actual SQL Server database/schema.
3. This `RULE.md`.
4. Existing working project conventions.
5. Agent assumptions.

If uncertain, inspect first. Do not invent.

---

# 1. Project Identity

Project name:

```text
Self-Storage Facility Rental and Management System
```

Solution:

```text
SelfStorageManagementSystem.sln
```

Main roles:

```text
Storage Customer
Facility Staff
Facility Manager
Business Operations Manager
System Administrator
```

Core business scope includes:

```text
Facilities
Storage Unit Types
Storage Units
Availability
Reservation
Payment
Rental Agreement
Check-in / Handover
Renewal
Move-out / Check-out
Inspection
Support Tickets
Staff Operations
Reports
Administration
Audit
```

Do not treat this scope as permission to invent missing tables or fields.

The database remains the source of truth.

---

# 2. Mandatory Technology Stack

Use:

```text
C#
.NET 8
ASP.NET Core Web API
Entity Framework Core 8
SQL Server
Database First
Swagger / OpenAPI
Built-in Dependency Injection
Built-in ILogger<T>
```

Do not change stack without explicit user instruction.

Do NOT introduce by default:

```text
Clean Architecture
Onion Architecture
Hexagonal Architecture
CQRS
MediatR
DDD abstractions
Event Sourcing
Microservices
Kafka
RabbitMQ
MassTransit
Redis
Dapper
AutoMapper
FluentValidation
Serilog
UnitOfWork abstraction
Repository Manager abstraction
```

This is a PRN222 academic project.

Prefer code that is:

```text
easy to read
easy to debug
easy to explain
easy to defend
easy to maintain
```

---

# 3. Mandatory 3-Layer Architecture

The project uses exactly:

```text
Presentation
    ↓
BusinessLogic
    ↓
DataAccess
    ↓
SQL Server
```

Allowed project references:

```text
Presentation -> BusinessLogic
BusinessLogic -> DataAccess
DataAccess -> no application project
```

Forbidden:

```text
Presentation -> DataAccess
BusinessLogic -> Presentation
DataAccess -> BusinessLogic
DataAccess -> Presentation
```

No circular dependencies.

---

# 4. Root Folder Organization

Expected repository root:

```text
SelfStorageManagementSystem/
│
├── SelfStorageManagementSystem.sln
├── RULE.md
├── README.md
├── .gitignore
│
├── docs/
│   ├── DB_SCHEMA_REPORT.md
│   ├── BASE_ARCHITECTURE_REPORT.md
│   └── PRE_GITHUB_REVIEW.md
│
└── src/
    ├── SelfStorageManagementSystem.Presentation/
    ├── SelfStorageManagementSystem.BusinessLogic/
    └── SelfStorageManagementSystem.DataAccess/
```

Do not place source code directly in repository root.

Do not create random folders such as:

```text
Helpers/
Common/
Utils/
Services/
Repositories/
```

at root level.

All source code must belong to the correct project under `src/`.

---

# 5. Mandatory `src/` Organization

All application source code lives under:

```text
src/
```

Never create another parallel source root such as:

```text
Source/
Backend/
API/
App/
Core/
Infrastructure/
```

unless explicitly instructed.

Correct:

```text
src/
├── SelfStorageManagementSystem.Presentation/
├── SelfStorageManagementSystem.BusinessLogic/
└── SelfStorageManagementSystem.DataAccess/
```

Do not move files between layers simply for style.

---

# 6. Presentation Folder Structure

Expected:

```text
src/SelfStorageManagementSystem.Presentation/
│
├── Controllers/
│   ├── BaseController.cs
│   └── <Feature>Controller.cs
│
├── ExceptionHandling/
│   └── GlobalExceptionHandler.cs
│
├── Configuration/
│
├── Properties/
│   └── launchSettings.json
│
├── Program.cs
├── appsettings.json
├── appsettings.Development.json
└── SelfStorageManagementSystem.Presentation.csproj
```

Presentation responsibilities:

```text
HTTP request
model binding
basic input validation
authentication / authorization integration
call BusinessLogic service
return HTTP response
Swagger
CORS
exception handling
middleware
composition root
```

Presentation must NOT contain:

```text
business rules
EF Core queries
DbSet<T> access
direct DbContext access
repository calls from Controllers
price calculations
reservation allocation logic
payment logic
status-machine logic
SQL queries
```

Future runtime path:

```text
Controller
    ↓
Service
    ↓
Repository
    ↓
DbContext
```

---

# 7. Controller Organization

Controllers belong only in:

```text
Presentation/Controllers/
```

Naming:

```text
AuthController
FacilityController
StorageUnitController
ReservationController
PaymentController
RentalAgreementController
SupportTicketController
```

Rules:

```text
Controller = thin
Controller does not contain business logic
Controller does not query database
Controller does not call IRepository<T> directly
Controller does not inject SelfStorageDbContext
Controller does not expose EF entities directly
```

Allowed responsibilities:

```text
receive DTO
pass CancellationToken
call Service
select correct HTTP result
return ApiResponse<T>
```

`BaseController` must remain minimal.

Do not turn it into a generic CRUD controller.

---

# 8. BusinessLogic Folder Structure

Expected:

```text
src/SelfStorageManagementSystem.BusinessLogic/
│
├── Common/
│   ├── ApiResponse.cs
│   ├── PagedRequest.cs
│   └── PagedResult.cs
│
├── DTOs/
│   ├── Requests/
│   └── Responses/
│
├── Exceptions/
│   ├── BadRequestException.cs
│   ├── UnauthorizedException.cs
│   ├── ForbiddenException.cs
│   ├── NotFoundException.cs
│   └── ConflictException.cs
│
├── Services/
│   ├── Interfaces/
│   └── Implementations/
│
├── Mappings/
│
├── DependencyInjection/
│   └── ServiceCollectionExtensions.cs
│
└── SelfStorageManagementSystem.BusinessLogic.csproj
```

Do not create domain-independent generic business services.

Forbidden by default:

```text
GenericService<T>
IGenericService<T>
CrudService<T>
BaseService<T>
```

Business services represent use cases, not tables.

---

# 9. DTO Organization

DTOs must be use-case driven.

Requests:

```text
BusinessLogic/DTOs/Requests/
```

Responses:

```text
BusinessLogic/DTOs/Responses/
```

Recommended feature subfolders when the project grows:

```text
DTOs/
├── Requests/
│   ├── Auth/
│   ├── Facilities/
│   ├── StorageUnits/
│   ├── Reservations/
│   ├── Payments/
│   ├── Rentals/
│   └── SupportTickets/
│
└── Responses/
    ├── Auth/
    ├── Facilities/
    ├── StorageUnits/
    ├── Reservations/
    ├── Payments/
    ├── Rentals/
    └── SupportTickets/
```

Use feature subfolders only when enough DTOs exist to justify them.

Do not create dozens of empty folders in advance.

Naming examples:

```text
LoginRequest
LoginResponse

CreateReservationRequest
ReservationResponse
ReservationDetailResponse

StorageUnitListItemResponse
StorageUnitDetailResponse

CreatePaymentRequest
PaymentResponse
```

Do not expose EF entities directly to API clients.

---

# 10. Service Organization

Interfaces:

```text
BusinessLogic/Services/Interfaces/
```

Implementations:

```text
BusinessLogic/Services/Implementations/
```

Recommended naming:

```text
IAuthService
AuthService

IFacilityService
FacilityService

IStorageUnitService
StorageUnitService

IReservationService
ReservationService

IPaymentService
PaymentService

IRentalAgreementService
RentalAgreementService

ISupportTicketService
SupportTicketService
```

Do not create service interfaces until an actual use case exists.

Service responsibilities:

```text
business validation
workflow decisions
status transition decisions
price/deposit calculation
authorization scope decisions
coordinate repositories
map Entity <-> DTO
throw application exceptions
```

Service must not contain raw SQL.

Service should not normally inject `SelfStorageDbContext`.

Prefer repository abstractions.

---

# 11. Mapping Organization

Default strategy:

```text
explicit manual mapping
```

Do not install AutoMapper unless explicitly requested.

For small features, mapping may remain in Service.

If mapping code becomes large, place mapping helpers under:

```text
BusinessLogic/Mappings/
```

Recommended naming:

```text
ReservationMapper
FacilityMapper
PaymentMapper
```

Do not create a generic reflection-based mapper.

---

# 12. DataAccess Folder Structure

Expected:

```text
src/SelfStorageManagementSystem.DataAccess/
│
├── Context/
│   └── SelfStorageDbContext.cs
│
├── Entities/
│   └── *.cs
│
├── Repositories/
│   ├── Interfaces/
│   │   └── IRepository.cs
│   └── Implementations/
│       └── GenericRepository.cs
│
├── DependencyInjection/
│   └── ServiceCollectionExtensions.cs
│
└── SelfStorageManagementSystem.DataAccess.csproj
```

When specific repositories are genuinely required:

```text
Repositories/
├── Interfaces/
│   ├── IRepository.cs
│   ├── IReservationRepository.cs
│   ├── IStorageUnitRepository.cs
│   └── IPaymentRepository.cs
│
└── Implementations/
    ├── GenericRepository.cs
    ├── ReservationRepository.cs
    ├── StorageUnitRepository.cs
    └── PaymentRepository.cs
```

Do not create empty repository interfaces in advance.

---

# 13. Database First Is Mandatory

Database:

```text
SelfStoragePRN222
```

Schema:

```text
core
```

Known model:

```text
57 base tables
2 SQL views
59 EF entities
139 foreign keys
35 SQL Server triggers
```

Known composite PKs:

```text
payment_allocations
    (payment_id, invoice_id)

shift_assignments
    (shift_id, employee_id)

user_roles
    (user_id, role_id)
```

Known keyless views:

```text
UnitTypeHaTDT
StorageUnitHaTDT
```

The SQL Server database is the source of truth.

Never invent:

```text
table
column
primary key
foreign key
relationship
index
status
default value
```

unless explicitly asked to change the database.

---

# 14. Generated EF Code Protection

Treat these files as generated:

```text
DataAccess/Context/SelfStorageDbContext.cs
DataAccess/Entities/*.cs
```

Do not manually add:

```text
business logic
validation
DTO mapping
service methods
authorization logic
helper methods
repository methods
custom convenience methods
```

to generated entity files.

Re-scaffolding may overwrite them.

Do not rename generated classes just for C# style.

Current lowercase entity warnings:

```text
CS8981
```

are known and acceptable.

Do not rename generated classes solely to remove these warnings.

---

# 15. EF Core Reverse Engineering Rules

If re-scaffolding is required, use Database First.

Preferred pattern:

```bash
dotnet ef dbcontext scaffold "<CONNECTION_STRING>" Microsoft.EntityFrameworkCore.SqlServer   --project src/SelfStorageManagementSystem.DataAccess   --startup-project src/SelfStorageManagementSystem.Presentation   --context SelfStorageDbContext   --context-dir Context   --output-dir Entities   --schema core   --no-onconfiguring   --use-database-names   --force
```

Before `--force`:

1. inspect generated files;
2. confirm no manual code was added;
3. confirm re-scaffolding is intended.

Do not create Code First migrations.

---

# 16. DbContext Configuration

`SelfStorageDbContext` must receive:

```csharp
DbContextOptions<SelfStorageDbContext>
```

through constructor injection.

Do not implement:

```text
GetConnectionString()
ConfigurationBuilder
AddJsonFile
manual appsettings loading
```

inside DbContext.

Do not hard-code credentials.

Do not manually instantiate DbContext in runtime code.

Configuration belongs in:

```text
DataAccess/DependencyInjection/ServiceCollectionExtensions.cs
```

using `AddDbContext`.

---

# 17. DbContext Lifetime

`SelfStorageDbContext` must be Scoped.

Repository lifetime must be Scoped.

Do not register either as Singleton.

All repositories inside one HTTP request should share the same scoped DbContext.

This is required for:

```text
consistent tracking
multi-entity operations
future transactions
SaveChanges coordination
```

---

# 18. Generic Repository Rules

Current base:

```text
IRepository<T>
GenericRepository<T>
```

Required capabilities:

```text
GetAllAsync
GetByIdAsync
FirstOrDefaultAsync
FindAsync
AnyAsync
CountAsync
AddAsync
Update
Delete
SaveChangesAsync
```

Use async-first APIs.

Use `CancellationToken`.

Do not add synchronous duplicates without a real requirement.

---

# 19. Primary Key Strategy

Never assume:

```text
int Id
```

Most database keys use:

```text
bigint -> long
```

Composite keys exist.

Generic repository key access must support:

```csharp
object[] keyValues
```

with:

```csharp
DbSet<T>.FindAsync(...)
```

Example:

```csharp
await repository.GetByIdAsync(
    new object[] { id },
    cancellationToken);
```

Composite:

```csharp
await repository.GetByIdAsync(
    new object[] { paymentId, invoiceId },
    cancellationToken);
```

Do not create int/long/string/Guid overload explosion.

---

# 20. Tracking Strategy

Do not globally configure:

```csharp
QueryTrackingBehavior.NoTracking
```

Use:

```csharp
AsNoTracking()
```

for read-only queries.

Use tracked entities for update workflows.

Do not call:

```csharp
ChangeTracker.Clear()
```

inside repositories.

---

# 21. Update Safety

`GenericRepository.Update(T entity)` may exist.

However, do not encourage:

```text
partial request DTO
→ create incomplete Entity
→ mark whole Entity Modified
```

Preferred feature update:

```text
load entity
→ validate
→ modify allowed fields
→ SaveChangesAsync
```

Avoid accidental overwrite of untouched columns.

---

# 22. SaveChanges Strategy

Repository mutation methods should not automatically persist.

Expected:

```text
Add / Update / Delete
    ↓
Service completes logical workflow
    ↓
SaveChangesAsync
```

Do not add custom `IUnitOfWork`.

EF Core `DbContext` already provides unit-of-work behavior.

---

# 23. Specific Repository Rule

Create a specific repository only when GenericRepository cannot express the persistence query cleanly.

Valid reasons:

```text
complex Include / ThenInclude
availability query
aggregate/report query
date-overlap query
multi-filter query
special read projection
```

Specific repository may answer:

```text
Which units match facility/type/date constraints?
Which active allocations overlap this date range?
Which invoice has these lines/payments?
```

Specific repository must NOT decide:

```text
whether customer may reserve
which unit customer deserves
deposit amount
refund eligibility
status transition legality
overdue policy
```

Those belong in BusinessLogic.

---

# 24. Keyless View Rule

These entities are read-only:

```text
UnitTypeHaTDT
StorageUnitHaTDT
```

Do not call:

```text
GetById
Update
Delete
```

on them.

Do not invent keys.

Use them only as read projections when useful.

---

# 25. Trigger Awareness

SQL Server contains approximately 35 active triggers.

Do not:

```text
disable trigger
drop trigger
alter trigger
bypass trigger
```

from application code.

Known trigger categories include:

```text
date overlap validation
status transition validation
invoice total refresh
payment-to-invoice refresh
multi-facility integrity
```

BusinessLogic should validate predictable rules before persistence.

SQL Server remains the final integrity boundary.

---

# 26. SQL Exception Rule

Do NOT translate every `SqlException` into HTTP 400.

Only translate known, predictable database constraint/trigger violations.

Examples:

```text
known state conflict -> ConflictException
known invalid business input -> BadRequestException
```

Unexpected:

```text
SQL Server unavailable
timeout
permission failure
unknown trigger failure
connection problem
```

must remain unexpected server failures and flow to HTTP 500.

Never expose raw SQL exception messages to clients.

---

# 27. Transaction Rule

Do not create generic transaction abstractions before a concrete use case requires them.

Known future transaction candidates:

```text
Reservation
+ Unit Allocation
+ Invoice

Check-in
+ Rental Agreement
+ Unit Allocation
+ Storage Unit status
+ Handover Record

Check-out
+ Inspection
+ Charges
+ Refund
+ Storage Unit status
```

When implementing such use cases:

1. inspect existing repository boundaries;
2. choose the smallest transaction scope;
3. do not leak transaction orchestration into Controllers;
4. do not create enterprise transaction frameworks.

---

# 28. Business Exceptions

Current exceptions:

```text
BadRequestException
UnauthorizedException
ForbiddenException
NotFoundException
ConflictException
```

They belong in:

```text
BusinessLogic/Exceptions/
```

They must not reference:

```text
HttpContext
HttpStatusCode
IActionResult
ASP.NET MVC
```

Presentation maps them to HTTP.

---

# 29. Global Exception Handling

Presentation uses:

```text
IExceptionHandler
GlobalExceptionHandler
```

Expected mapping:

```text
BadRequestException -> 400
UnauthorizedException -> 401
ForbiddenException -> 403
NotFoundException -> 404
ConflictException -> 409
Unexpected -> 500
```

Do not leak:

```text
stack trace
inner exception
SqlException.Message
connection string
server path
trigger implementation
```

to clients.

Unexpected exceptions should be logged using `ILogger<T>`.

---

# 30. HTTP 499 Rule

Current code may use:

```text
499 Client Closed Request
```

for cancelled requests.

Remember:

```text
499 is an Nginx convention, not an IANA standard HTTP status.
```

Do not expand custom 499 handling unnecessarily.

If writing a response after client disconnect causes issues, simplify cancellation handling.

Do not treat client cancellation as an unexpected 500 by default.

---

# 31. API Response Model

Use one shared envelope:

```csharp
ApiResponse<T>
```

Fields:

```text
Success
Message
Data
Errors
```

Do not create competing wrappers such as:

```text
BaseResponse
ApiResult
ResponseModel
ResponseDTO
```

unless explicitly required.

Do not double-wrap responses.

---

# 32. HTTP Status Semantics

Do not return HTTP 200 for every outcome.

Use:

```text
200 success
201 created
204 no content when appropriate
400 invalid request
401 unauthenticated
403 unauthorized scope
404 missing resource
409 state/uniqueness conflict
500 unexpected server error
```

JSON envelope supplements HTTP status; it does not replace it.

---

# 33. Validation

Use built-in ASP.NET Core validation initially.

No FluentValidation by default.

Invalid ModelState should return:

```text
HTTP 400
ApiResponse<object?>
Dictionary<string, string[]>
```

Do not serialize raw framework exceptions.

---

# 34. Pagination

Current base:

```text
PagedRequest
PagedResult<T>
```

Rules:

```text
PageNumber >= 1
PageSize >= 1
PageSize <= 100
```

Database pagination must happen before materialization.

Correct:

```csharp
query
    .Skip(...)
    .Take(...)
    .ToListAsync(...)
```

Wrong:

```csharp
var all = await query.ToListAsync();
var page = all.Skip(...).Take(...);
```

---

# 35. Authentication / Authorization

Authentication is a feature, not base infrastructure to invent prematurely.

When implemented later, use actual DB schema:

```text
users
roles
user_roles
customer_profiles
employee_profiles
staff_facility_assignments
```

Do not hard-code role IDs.

Known logical roles:

```text
storage_customer
facility_staff
facility_manager
business_operations_manager
system_administrator
```

Facility-scoped roles must not automatically access all facilities.

Never trust facility IDs or roles supplied by client without server-side verification.

---

# 36. Password / Token Rules

When Auth is implemented:

```text
never store plain-text passwords
never log passwords
never return password hashes
never log JWT
never expose signing secret
```

JWT signing keys belong in:

```text
User Secrets
Environment Variables
secure deployment configuration
```

not tracked source.

Do not add auth code before the corresponding feature task.

---

# 37. Program.cs Rule

`Program.cs` is the Composition Root.

Allowed:

```text
service registration
configuration
middleware pipeline
Swagger
CORS
exception handling
authentication registration when implemented
authorization registration when implemented
```

Forbidden:

```text
business logic
database query
feature workflow
manual entity creation
manual repository construction
```

Keep it short and readable.

---

# 38. Dependency Injection Organization

DataAccess registrations:

```text
DataAccess/DependencyInjection/ServiceCollectionExtensions.cs
```

BusinessLogic registrations:

```text
BusinessLogic/DependencyInjection/ServiceCollectionExtensions.cs
```

Presentation:

```text
Program.cs
```

should call:

```csharp
builder.Services.AddDataAccess(builder.Configuration);
builder.Services.AddBusinessLogic();
```

Future feature services are registered inside BusinessLogic DI.

Future specific repositories are registered inside DataAccess DI.

Do not scatter DI registration across random files.

---

# 39. CORS

Use named policy:

```text
FrontendPolicy
```

Origins should come from:

```text
Cors:AllowedOrigins
```

Development examples:

```text
http://localhost:5173
http://localhost:3000
```

Do not use unrestricted Production CORS without explicit reason.

Do not enable credentials unless actually required.

---

# 40. Swagger

Swagger is allowed in Development.

Do not add JWT Bearer Swagger configuration until real JWT authentication exists.

Once JWT exists, configure Swagger Bearer properly.

Do not add fake lock icons.

---

# 41. Logging

Use:

```csharp
ILogger<T>
```

Do not use application runtime:

```text
Console.WriteLine
Debug.WriteLine
```

for operational logging.

Never log:

```text
password
JWT
refresh token
payment secret
access PIN
database password
full connection string
```

---

# 42. Async Rules

Database and I/O operations should be async.

Use:

```text
ToListAsync
FirstOrDefaultAsync
SingleOrDefaultAsync
AnyAsync
CountAsync
SaveChangesAsync
```

Avoid:

```text
.Result
.Wait()
GetAwaiter().GetResult()
```

Support `CancellationToken` where practical.

---

# 43. Naming Conventions

Handwritten C# code:

```text
PascalCase -> classes, interfaces, methods, public properties
camelCase -> parameters, locals
_privateField -> private fields if used
IName -> interface
Async suffix -> asynchronous methods
```

Examples:

```text
IReservationService
ReservationService

IStorageUnitRepository
StorageUnitRepository

CreateReservationRequest
ReservationResponse
```

Generated DB-first files are exempt from handwritten naming cleanup.

Do not rename generated lowercase classes for style.

---

# 44. Namespace Organization

Namespaces should reflect project and folder purpose.

Examples:

```text
SelfStorageManagementSystem.Presentation.Controllers

SelfStorageManagementSystem.BusinessLogic.Services.Interfaces
SelfStorageManagementSystem.BusinessLogic.Services.Implementations
SelfStorageManagementSystem.BusinessLogic.DTOs.Requests
SelfStorageManagementSystem.BusinessLogic.DTOs.Responses

SelfStorageManagementSystem.DataAccess.Repositories.Interfaces
SelfStorageManagementSystem.DataAccess.Repositories.Implementations
```

Avoid arbitrary root namespaces such as:

```text
Common
Utils
Helpers
Models
```

without project prefix.

---

# 45. File Organization Rules

One primary public class/interface per file unless there is a strong reason otherwise.

File name should match primary type:

```text
ReservationService.cs
IReservationService.cs
ReservationController.cs
CreateReservationRequest.cs
```

Do not create files such as:

```text
Services.cs
Models.cs
Helpers.cs
Common.cs
Utils.cs
```

containing unrelated types.

---

# 46. Feature Folder Growth Rule

At small scale, keep current flat structure.

As a feature grows, organize inside existing layer folders.

Example:

```text
BusinessLogic/
├── DTOs/
│   ├── Requests/
│   │   └── Reservations/
│   └── Responses/
│       └── Reservations/
│
├── Services/
│   ├── Interfaces/
│   │   └── IReservationService.cs
│   └── Implementations/
│       └── ReservationService.cs
```

Do not create a new architecture style such as:

```text
Features/Reservations/
```

unless the user explicitly decides to switch organization.

Current project is layer-first, not feature-first.

---

# 47. Entity Exposure Rule

Never return generated EF entities directly from API.

Do not return navigation graphs.

Use response DTOs.

Benefits:

```text
avoid circular serialization
avoid over-posting
avoid leaking internal fields
stable API contract
decouple API from DB schema
```

---

# 48. Request Safety

Do not trust server-controlled fields from request DTOs.

Examples:

```text
database generated ID
CreatedAt
UpdatedAt
CreatedBy
Role
FinalAmount
PaidAmount
status transitions
facility scope
```

Server must determine these values.

---

# 49. Status / Enum Rule

Do not convert status fields into enums merely because they look enum-like.

First verify:

```text
CHECK constraint
lookup table
seed data
documentation
```

Known proven status sets may eventually become enums if the user chooses.

Do not invent values.

---

# 50. Money Rules

Database financial values use decimal/numeric.

Use:

```csharp
decimal
```

Never use:

```text
float
double
```

for money.

Preserve DB precision such as:

```text
numeric(14,2)
```

---

# 51. Time Rules

Database commonly uses:

```text
datetimeoffset
date
time
```

Respect generated mappings:

```text
DateTimeOffset
DateOnly
TimeOnly
```

Do not replace them casually with `DateTime`.

---

# 52. Delete Rules

Do not assume hard delete.

Current DB uses lifecycle/status fields rather than universal soft-delete.

Before implementing delete:

1. inspect FK constraints;
2. inspect status lifecycle;
3. inspect audit requirement;
4. inspect feature requirement.

GenericRepository can expose `Delete`, but BusinessLogic decides whether deletion is valid.

---

# 53. Facility Scope Rules

This project is multi-facility.

Facility scope is a critical security boundary.

When implementing facility-scoped features:

```text
Facility Staff
Facility Manager
```

must only access authorized facility data.

Do not rely solely on client-provided `facilityId`.

Server-side access verification is mandatory.

Business Operations Manager and System Administrator may have broader scope according to actual authorization rules.

---

# 54. Role Rules

Do not hard-code numeric role IDs.

Use role names/codes from database or a centralized proven mapping.

Do not duplicate role string literals throughout controllers/services.

When Auth exists, centralize role constants if appropriate.

Do not add role constants before verifying actual DB values.

---

# 55. Repository Query Efficiency

Prefer server-side query execution.

Avoid:

```text
load all rows
filter in memory
sort in memory
paginate in memory
```

Use LINQ translated to SQL.

Use projection when large navigation graphs are unnecessary.

Avoid unnecessary `Include`.

Specific repositories may expose tailored queries.

---

# 56. N+1 Awareness

When returning related data:

1. inspect required fields;
2. choose projection or Include intentionally;
3. avoid repeated per-row database queries.

Do not eagerly Include every navigation property by default.

---

# 57. Transaction Scope

Transactions should wrap a business operation, not entire HTTP request.

Keep transaction scope short.

Do not perform unrelated network calls while holding DB transaction unless unavoidable.

Do not introduce distributed transaction complexity.

---

# 58. Error Handling in Services

Do not write:

```csharp
catch (Exception)
{
    throw new BadRequestException(...);
}
```

around all service logic.

Only catch exceptions when:

```text
you can add meaningful context
you can translate a known business/persistence condition
you can recover safely
```

Otherwise let global exception handling process unexpected failure.

---

# 59. No Exception Swallowing

Forbidden:

```csharp
catch
{
}
```

Forbidden:

```csharp
catch (Exception)
{
    return false;
}
```

Forbidden:

```csharp
catch (Exception)
{
    return null;
}
```

unless the behavior is explicitly justified and documented.

---

# 60. Security Baseline

Never commit:

```text
database passwords
JWT signing keys
API secrets
private keys
payment secrets
access PIN values
real credentials
```

Prefer:

```text
User Secrets
Environment Variables
deployment secret stores
```

Do not print secrets into logs or reports.

---

# 61. `.gitignore`

Repository should ignore at minimum:

```text
.vs/
bin/
obj/
*.user
*.suo
```

Also ignore local secret/development artifacts when appropriate.

Do not ignore:

```text
source code
RULE.md
docs/
required SQL scripts
README.md
```

---

# 62. Git Discipline

Do not:

```text
git reset --hard
git clean -fd
force push
rewrite history
discard unrelated user changes
```

unless explicitly requested.

Do not commit automatically.

Do not push automatically.

Before suggesting commit:

```text
git status
git diff
```

should be reviewed.

---

# 63. Build Discipline

After coding:

```bash
dotnet restore
dotnet build
```

For final reviews:

```bash
dotnet clean
dotnet restore
dotnet build
```

Expected base status:

```text
0 Errors
11 known CS8981 warnings
```

The 11 warnings originate from generated lowercase EF entity names.

Any new warning from handwritten code must be investigated.

---

# 64. Runtime Verification

When infrastructure changes:

```bash
dotnet run --project src/SelfStorageManagementSystem.Presentation
```

Verify startup.

In Development verify:

```text
/swagger
/swagger/v1/swagger.json
```

Stop test process after verification.

Do not leave background API processes running.

---

# 65. Database Safety During Review

Review/audit tasks must be read-only unless task explicitly requires data mutation.

Do not run destructive SQL.

Forbidden during review:

```text
DROP
TRUNCATE
ALTER schema
DELETE business data
UPDATE business data
INSERT test data into production-like DB
```

Use safe connectivity/schema inspection only.

---

# 66. Pre-Code Checklist

Before implementing any feature:

```text
[ ] RULE.md read
[ ] relevant DB entities inspected
[ ] DB_SCHEMA_REPORT.md checked
[ ] current layer structure inspected
[ ] required use case understood
[ ] no DB field is being invented
[ ] DTO boundary identified
[ ] correct Service identified
[ ] GenericRepository sufficiency evaluated
[ ] specific repository justified if needed
[ ] authorization/facility scope considered
[ ] transaction need considered
[ ] trigger constraints considered
```

---

# 67. Post-Code Checklist

Before finishing:

```text
[ ] generated entities unchanged
[ ] DbContext generated mapping unchanged
[ ] Controller thin
[ ] no Repository injected into Controller
[ ] no DbContext injected into Controller
[ ] Service owns business logic
[ ] Repository owns persistence query only
[ ] DTO used at API boundary
[ ] no raw Entity exposed
[ ] async APIs used
[ ] CancellationToken propagated where practical
[ ] no ChangeTracker.Clear
[ ] no hidden SaveChanges
[ ] no secret added
[ ] no unnecessary package added
[ ] build succeeds
[ ] no new warning from handwritten code
[ ] final diff reviewed
```

---

# 68. Feature Implementation Order

Unless user explicitly chooses otherwise, recommended order:

```text
1. Authentication / Authorization
2. Facility
3. Unit Type
4. Storage Unit
5. Availability
6. Reservation
7. Payment
8. Rental Agreement
9. Check-in / Handover
10. Renewal
11. Move-out / Check-out
12. Inspection
13. Support Ticket
14. Staff Operations
15. Reports
16. Administration / Audit
```

Do not skip security boundaries when implementing dependent features.

---

# 69. Authentication Feature Folder Example

When Auth is implemented:

```text
BusinessLogic/
├── DTOs/
│   ├── Requests/
│   │   └── Auth/
│   │       ├── LoginRequest.cs
│   │       └── RegisterRequest.cs
│   └── Responses/
│       └── Auth/
│           └── LoginResponse.cs
│
├── Services/
│   ├── Interfaces/
│   │   └── IAuthService.cs
│   └── Implementations/
│       └── AuthService.cs
```

Presentation:

```text
Controllers/
└── AuthController.cs
```

DataAccess specific repository only if required:

```text
Repositories/
├── Interfaces/
│   └── IUserRepository.cs
└── Implementations/
    └── UserRepository.cs
```

Do not create an Auth folder outside layer boundaries.

---

# 70. Reservation Feature Folder Example

BusinessLogic:

```text
DTOs/
├── Requests/
│   └── Reservations/
│       ├── CreateReservationRequest.cs
│       └── CancelReservationRequest.cs
│
└── Responses/
    └── Reservations/
        ├── ReservationResponse.cs
        └── ReservationDetailResponse.cs

Services/
├── Interfaces/
│   └── IReservationService.cs
└── Implementations/
    └── ReservationService.cs
```

DataAccess:

```text
Repositories/
├── Interfaces/
│   └── IReservationRepository.cs
└── Implementations/
    └── ReservationRepository.cs
```

Presentation:

```text
Controllers/
└── ReservationController.cs
```

Keep the project layer-first.

---

# 71. Payment Feature Folder Example

BusinessLogic:

```text
DTOs/
├── Requests/
│   └── Payments/
└── Responses/
    └── Payments/

Services/
├── Interfaces/
│   └── IPaymentService.cs
└── Implementations/
    └── PaymentService.cs
```

DataAccess only when needed:

```text
Repositories/
├── Interfaces/
│   └── IPaymentRepository.cs
└── Implementations/
    └── PaymentRepository.cs
```

Presentation:

```text
Controllers/
└── PaymentController.cs
```

Do not place payment provider SDK logic inside Entity classes.

---

# 72. Reports Folder Guidance

Reports are still part of the same 3 layers.

Do not create a separate Reports project.

Possible:

```text
BusinessLogic/
├── DTOs/Responses/Reports/
├── Services/Interfaces/IReportService.cs
└── Services/Implementations/ReportService.cs
```

DataAccess may have:

```text
IReportRepository
ReportRepository
```

for complex aggregates.

Do not use GenericRepository for every report query if projections/aggregations are clearer.

---

# 73. Admin Folder Guidance

Do not create:

```text
Admin/
Manager/
Staff/
Customer/
```

as separate top-level application projects.

Roles are authorization concerns, not architecture layers.

Controllers/services may expose role-specific use cases while remaining in the same 3-layer structure.

---

# 74. Avoid "Helpers" Dumping Ground

Do not create a generic:

```text
Helpers/
Utils/
Common/
```

folder merely to place unrelated methods.

If a utility has a clear domain:

```text
TokenService
PasswordService
DateRangeValidator
```

place it in the appropriate BusinessLogic area.

If code is cross-cutting infrastructure, place it in a clearly named existing folder.

---

# 75. Keep Interfaces Meaningful

Do not create an interface for every tiny class automatically.

Interfaces are most useful for:

```text
Services
Repositories
external integration abstractions
test seams
```

Plain DTO/model/helper classes do not need interfaces.

---

# 76. Code Review Severity

For Codex/Astra review tasks, classify findings:

```text
CRITICAL
HIGH
MEDIUM
LOW
INFO
```

CRITICAL:

```text
secret leak
database corruption risk
broken architecture
application cannot run
```

HIGH:

```text
wrong DI lifetime
serious EF misuse
unsafe authorization
persistence bug
```

MEDIUM:

```text
maintainability issue likely to cause defects
```

LOW:

```text
minor cleanup
```

INFO:

```text
intentional/harmless known condition
```

Do not exaggerate harmless style issues.

---

# 77. Review vs Refactor Rule

When asked to review:

1. inspect first;
2. report actual defects;
3. fix only genuine issues if user allowed fixes;
4. do not redesign working code based on personal preference.

Do not turn a review into an architecture migration.

---

# 78. Package Rule

Before adding a NuGet package, ask internally:

```text
Can .NET 8 built-in APIs already solve this cleanly?
```

If yes, do not add the package.

If package is required:

1. ensure .NET 8 compatibility;
2. ensure package has a clear purpose;
3. install only in correct project;
4. document why.

---

# 79. README Accuracy

README must not claim unfinished features are complete.

Base status may say:

```text
Base architecture complete.
Feature development in progress.
```

Do not include credentials.

Document setup using:

```text
Windows Integrated Authentication
User Secrets
environment variables
```

as appropriate.

---

# 80. Final Golden Rules

Always preserve these rules:

```text
Database first.
Layer boundaries first.
Business rules in Service.
Persistence queries in Repository.
HTTP concerns in Presentation.
Generated EF files stay generated.
DTOs protect the API boundary.
No secret in source.
No over-engineering.
Build before finishing.
```

And specifically:

```text
Do not create code first and force the database to match it.
Do not create folders first and invent responsibilities later.
Do not create abstractions without a real use case.
Do not rewrite working architecture merely because another pattern is popular.
```

---

# 81. Final Agent Output Format

For coding tasks, final response should include:

```text
1. What was implemented
2. Files created
3. Files modified
4. Architecture impact
5. Database objects used
6. Business rules implemented
7. Commands executed
8. Build result
9. Warnings
10. Remaining risks / next step
```

For review tasks:

```text
1. Status
2. Findings by severity
3. Fixes applied
4. Files changed
5. Build result
6. Runtime result
7. Security result
8. Git readiness
```

Do not claim success if build/runtime verification was not actually performed.

---

# 82. Final Reminder

Every new Codex/Astra task must begin by re-reading this file.

Do not rely on memory from previous tasks.

Actual repository state and actual SQL Server schema always win over assumptions.
