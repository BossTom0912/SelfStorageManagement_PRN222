<<<<<<< HEAD
# Self-Storage Facility Rental and Management System

PRN222 project base for a self-storage rental and management API.

## Technology Stack

.NET 8, C#, ASP.NET Core Web API, Entity Framework Core 8, SQL Server,
Database First, and Swagger/OpenAPI.

## Architecture

`Presentation -> BusinessLogic -> DataAccess -> SQL Server`

Controllers will call business services; services coordinate repositories.
Repositories stage changes, and services explicitly call `SaveChangesAsync`.
All application code stays under `src/`. Read [CODEX_RULE.md](CODEX_RULE.md)
before changing the project.

## Project Structure

```text
SelfStorageManagementSystem.sln
CODEX_RULE.md
docs/
DB/
src/
  SelfStorageManagementSystem.Presentation/
  SelfStorageManagementSystem.BusinessLogic/
  SelfStorageManagementSystem.DataAccess/
```

`Agent/`, `SRS/`, and the root workbook contain existing project references.

## Database

The existing `SelfStoragePRN222` database uses schema `core`: 57 tables,
2 views, 139 foreign keys, and 35 triggers, mapped to 59 EF entity classes.
The views are keyless and read-only. The repository accepts `object[]` keys
in EF primary-key order, including composite keys; supply the correct CLR types.

Do not use Code First migrations, `EnsureCreated`, or automatic schema updates.
Generated entities and `SelfStorageDbContext.cs` must remain scaffolded code.

## Prerequisites

- .NET 8 SDK and ASP.NET Core 8 runtime.
- SQL Server 2019 or later, reachable by the application.
- An existing `SelfStoragePRN222` database and database access for your account.

## Configuration

Local development defaults to SQL Server on `localhost`, using Windows
Integrated Authentication and `TrustServerCertificate=True`.
No database password is included. The certificate setting is for local development;
configure a validated SQL Server certificate for deployment.

Override `ConnectionStrings:DefaultConnection` through User Secrets or the
`ConnectionStrings__DefaultConnection` environment variable. Do not put credentials
in tracked JSON, scripts, or documentation. To enable User Secrets locally:

```powershell
dotnet user-secrets init --project src/SelfStorageManagementSystem.Presentation
```

Supply your connection string through your local secret tooling. Configure
`Cors:AllowedOrigins` for the actual frontend origins in each environment.
Development allows `http://localhost:5173` and `http://localhost:3000`.

## Local Setup

Use the existing database when available. For a new disposable local database,
inspect `DB/SelfStoragePRN222_SQLServer_CleanInstall.sql` before running it in SSMS.
**That script drops and recreates `SelfStoragePRN222`, deleting existing data.**
It is not an incremental upgrade script and is never executed by the API.

From the repository root:

```powershell
dotnet clean
dotnet restore
dotnet build
```

The scaffold produces 11 accepted `CS8981` warnings for lowercase entity names.

## Run

```powershell
dotnet run --project src/SelfStorageManagementSystem.Presentation --launch-profile https
```

If needed, trust the local development certificate with `dotnet dev-certs https --trust`.
The HTTPS profile listens on `https://localhost:7031` and `http://localhost:5132`.
Stop the API with Ctrl+C. The `http` profile is also available for local smoke tests.

## Swagger

In Development, open `https://localhost:7031/swagger`.
The specification is at `/swagger/v1/swagger.json`. Swagger is disabled outside
Development. An empty operations list is expected because there are no feature controllers.

## Current Status

Base architecture complete. Feature development not yet implemented.
Authentication, JWT, business CRUD, reservations, payments, and other workflows
are intentionally absent. See [the final review](docs/PRE_GITHUB_REVIEW.md)
for verified build, runtime, database, and Git preparation results.
=======
# SelfStorageManagement_PRN222
FinalProject
>>>>>>> 6f2acb474cebcef22d14819b7d29f3865ee8a008
