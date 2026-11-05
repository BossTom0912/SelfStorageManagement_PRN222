# Self-Storage Facility Rental and Management System

PRN222 self-storage facility rental and management system. ASM01–04 share one solution: MVC + SignalR, Razor Pages, Blazor Server, and a Console / Windows Service worker. See [SUBMISSION_GUIDE.md](SUBMISSION_GUIDE.md) for current routes, setup, and run commands, and [ASM_REVIEW_STATUS.md](ASM_REVIEW_STATUS.md) for verification results.

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

Run **one** SQL file in a normal SSMS query window for the database state you have:

- **Existing database with data:** [`DB/SelfStoragePRN222_SQLServer_Function4_Upgrade_OneRun.sql`](DB/SelfStoragePRN222_SQLServer_Function4_Upgrade_OneRun.sql) preserves data and updates the Function 4 refund-approval foreign key. A successful result shows `referenced_table = core.users`, `is_disabled = 0`, and `is_not_trusted = 0`.
- **New disposable database:** [`DB/SelfStoragePRN222_SQLServer_CleanInstall.sql`](DB/SelfStoragePRN222_SQLServer_CleanInstall.sql) creates the complete schema and seed data with the Function 4 fix. It drops `SelfStoragePRN222` first, including all existing data.

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
- Configure `AdminAccount:Email` for an existing administrator and supply `AdminAccount:Password` (or `ADMIN_ACCOUNT_PASSWORD`). Bootstrap verifies the administrator role before updating its password. `DemoAccounts:DefaultPassword` (or `DEMO_DEFAULT_PASSWORD`) optionally sets the password for other demo accounts with placeholder hashes; otherwise it uses the admin bootstrap password.
- The Web host uses `Bootstrap:Enabled` in Development. Disable the relevant host's bootstrap flag when password updates are not intended.

```powershell
dotnet user-secrets set "DemoAccounts:Enabled" "true" --project src/SelfStorageManagementSystem.Presentation
dotnet user-secrets set "AdminAccount:Email" "<existing-admin-email>" --project src/SelfStorageManagementSystem.Presentation
dotnet user-secrets set "AdminAccount:Password" "<your-local-admin-password>" --project src/SelfStorageManagementSystem.Presentation
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
| **Feature 1: Authentication & RBAC** | Implemented | Login, registration, cookie auth for Web, JWT for API/WPF, facility scope, and account administration. |
| **Feature 2: Facility & Storage Unit Catalog** | Implemented | Catalog, floor maps, MVC/Razor/Blazor storage-unit management, capacity-safe deletion, and SignalR updates. |
| **Feature 3: Reservation & Hold Unit** | Implemented | Create/view/list/cancel reservations, 15-minute capacity holds, countdowns, and expiration workers. |
| **Feature 4: Payment & Rental Agreement** | Implemented | Quotes, checkout, Demo/VNPAY Sandbox, invoices, scheduled agreements, reconciliation, and refunds. |
| **Feature 5: Check-in & Digital Handover** | Implemented | Identity verification, inspection, allocation, handover, access PIN generation, and agreement activation. |

Current test results and verification limits are recorded in [ASM_REVIEW_STATUS.md](ASM_REVIEW_STATUS.md). Older function reports describe historical checkpoints.

> [!NOTE]
> **Demo Tax Notice (Feature 4)**:
> In this educational capstone version, `tax_amount = 0`. Invoices and receipts generated by the application are demo/internal settlement receipts, not legal VAT e-invoices.
>
> **Pending Scope Notice (Feature 1)**:
> Earlier roadmaps mentioned `Refresh Token` mechanics and an interactive UI for viewing/exporting `Audit Log`. Because the existing database schema does not include tables or columns for refresh tokens and this project strictly adheres to Database First without inventing migrations, these features are explicitly treated as pending scope decisions and are not marked as fully complete.
