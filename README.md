# Self-Storage Facility Rental and Management System

PRN222 project base for a self-storage facility rental and management system.

## Technology Stack

- **C# / .NET 8**
- **ASP.NET Core Web API**
- **WPF (Desktop Client)**
- **Entity Framework Core 8 (Database First)**
- **SQL Server**
- **Swagger / OpenAPI**

## Architecture

```text
WPF Client / Presentation (Web API)
       ↓
BusinessLogic
       ↓
DataAccess (EF Core Database First)
       ↓
SQL Server
```

- **Presentation Layer**:
  - `SelfStorageManagementSystem.Presentation`: ASP.NET Core Web API exposed for client applications and Swagger.
  - `SelfStorageManagementSystem.WpfClient`: Windows desktop client communicating with Web API over HTTPS.
- **BusinessLogic Layer** (`SelfStorageManagementSystem.BusinessLogic`): Contains domain logic, authentication, JWT token generation, role & facility-scoped access checks, admin account management, and validation.
- **DataAccess Layer** (`SelfStorageManagementSystem.DataAccess`): Scaffolded EF Core DbContext (`SelfStorageDbContext`), entities mapped directly to SQL Server `core` schema, and repositories.

## Database

The existing `SelfStoragePRN222` database uses schema `core`: 57 tables, 2 views, 139 foreign keys, and 35 triggers, mapped to 59 EF entity classes.
The database schema is the strict source of truth. Do NOT use Code First migrations, `EnsureCreated()`, or automatic schema updates. Generated entities and `SelfStorageDbContext.cs` remain scaffolded database-first code.

## Prerequisites

- .NET 8 SDK and ASP.NET Core 8 runtime.
- SQL Server 2019 or later reachable by the application.
- An existing `SelfStoragePRN222` database with credentials configured.

## Configuration & Security

### 1. Connection String

Configure your database connection via User Secrets or environment variables. Never hardcode credentials into tracked files:

```powershell
dotnet user-secrets init --project src/SelfStorageManagementSystem.Presentation
dotnet user-secrets set "ConnectionStrings:DefaultConnection" "Server=localhost;Database=SelfStoragePRN222;Integrated Security=True;TrustServerCertificate=True;" --project src/SelfStorageManagementSystem.Presentation
```

### 2. JWT Configuration (Mandatory)

The Web API validates the signing key on startup. The key **must be configured** and must be at least **256 bits (32 bytes)** long; otherwise, the API aborts startup with a configuration error.

Configure the key locally via User Secrets or the `JWT_SECRET_KEY` environment variable:

```powershell
# Via User Secrets:
dotnet user-secrets set "Jwt:Key" "<your-secure-32-byte-secret-key-goes-here>" --project src/SelfStorageManagementSystem.Presentation

# Or via environment variable:
$env:JWT_SECRET_KEY="<your-secure-32-byte-secret-key-goes-here>"
```

Optional JWT settings:
- `Jwt:Issuer`: Default is `SelfStoragePRN222`.
- `Jwt:Audience`: Default is `SelfStoragePRN222Clients`.
- `Jwt:ExpiryMinutes`: Token validity in minutes (default: 60).

### 3. HTTPS & Development Certificates

The WPF desktop client uses standard certificate validation and strictly enforces HTTPS for all credential transmissions.
If running locally and your development certificate is untrusted, trust the certificate using:

```powershell
dotnet dev-certs https --trust
```

### 4. Demo Accounts Bootstrap

The demo account bootstrap service (`DemoAccountBootstrapService`) replaces initial placeholder hashes for seed accounts. To protect production environments:
- It **only** runs when `ASPNETCORE_ENVIRONMENT=Development` **AND** `DemoAccounts:Enabled=true`.
- The bootstrap password must be explicitly supplied via `DemoAccounts:DefaultPassword` (or `DEMO_DEFAULT_PASSWORD` environment variable); if missing, it halts with a descriptive configuration error.

```powershell
dotnet user-secrets set "DemoAccounts:Enabled" "true" --project src/SelfStorageManagementSystem.Presentation
dotnet user-secrets set "DemoAccounts:DefaultPassword" "<your-local-demo-password>" --project src/SelfStorageManagementSystem.Presentation
```

## Running the Application

### Build & Test

```powershell
dotnet clean
dotnet restore
dotnet build SelfStorageManagementSystem.sln
dotnet test SelfStorageManagementSystem.sln
```

### Start Web API

```powershell
dotnet run --project src/SelfStorageManagementSystem.Presentation --launch-profile https
```

- Swagger UI available in Development at: `https://localhost:7031/swagger`

### Start WPF Client

```powershell
dotnet run --project src/SelfStorageManagementSystem.WpfClient
```

## Implementation & Roadmap Status

| Module | Status | Notes |
| :--- | :--- | :--- |
| **Feature 1: Authentication & RBAC** | **Implemented (Core)** | Core implementation complete: login, customer registration, JWT auth, facility scope authorization (`FacilityScopeService`), admin account management with database pagination, and WPF UI. |
| **Feature 2: Facility & Storage Unit Catalog** | In Progress (Under Verification) | Catalog controller (`/api/facilities`), DTOs, service/repository, floor map endpoint, WPF client catalog views, and catalog tests are drafted in the codebase. Currently under ongoing integration verification; not yet marked fully finalized. |
| **Feature 3: Reservation & Hold Unit** | **Implemented (Core); SQL Server/WPF Verification Pending** | Create/view/list/cancel reservations, 15-minute capacity hold by unit type, 30-second expiration worker, WPF countdown. 124 tests pass; the dedicated SQL Server concurrency test is skipped until a separate test database is configured. See [function status](docs/FIVE_CORE_FUNCTIONS_SUMMARY.md). |
| **Feature 4: Payment & Rental Agreement** | **Implemented (Core); SQL Server/WPF Verification Pending** | Initial quote (1-month deposit + 1st month rent + booking fee - voucher), checkout with idempotency lock, Demo & VNPAY Sandbox gateways, paid invoices, scheduled agreements, reconciliation handling for late callback, and full WPF checkout flow. 149 tests pass. |
| **Feature 5: Check-in & Digital Handover** | Not Implemented | Identity verification, unit handover, access PIN/QR generation, and agreement activation. Pending future development. |

> [!NOTE]
> **Demo Tax Notice (Feature 4)**:
> In this educational capstone version, `tax_amount = 0`. Invoices and receipts generated by the application are demo/internal settlement receipts, not legal VAT e-invoices.
>
> **Pending Scope Notice (Feature 1)**:
> Earlier roadmaps mentioned `Refresh Token` mechanics and an interactive UI for viewing/exporting `Audit Log`. Because the existing database schema does not include tables or columns for refresh tokens and this project strictly adheres to Database First without inventing migrations, these features are explicitly treated as pending scope decisions and are not marked as fully complete.
