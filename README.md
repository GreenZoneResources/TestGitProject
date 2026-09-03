# BulkReversal.API

Failed Transaction Reversal Portal backend — implements the BRD for bulk upload, validation,
approval, and status monitoring of failed-transaction reversals (NIP, Bill Payment, Card), and
acts as the **provider** consumed by `SingleReversalEngine.Orchestrator` (the reversal engine)
per `provider-integration-contract.md`.

## Architecture

Clean Architecture, four projects under `src/`:

- **BulkReversal.Domain** — entities (`ReversalBatch`, `ReversalTransaction`, `AuditLogEntry`),
  enums, and business-rule invariants. No dependencies on anything else.
- **BulkReversal.Application** — use-case services (upload/validation, approvals, status
  monitoring, provider integration, audit), DTOs, FluentValidation validators, and the
  ports (`I...Repository`, `IUnitOfWork`, `IUploadFileParser`, `IReportExportService`, ...)
  that Infrastructure implements. Depends only on Domain.
- **BulkReversal.Infrastructure** — EF Core (SQL Server) persistence, repositories, CSV/Excel
  parsing (CsvHelper/ClosedXML), Excel/PDF report generation (ClosedXML/QuestPDF), and both
  authentication schemes (SSO JWT for the portal, API key for the provider endpoints).
- **BulkReversal.API** — ASP.NET Core Web API host: controllers, Swagger/Swashbuckle, Serilog,
  API versioning, rate limiting, health checks, `appsettings.json`.

Dependencies point inward only (`API → Infrastructure → Application → Domain`); Application never
references Infrastructure or ASP.NET Core.

## Two audiences, two auth schemes

1. **Settlement Team portal** (`/api/v1/...`) — SSO-backed JWT ("CustomJwt" scheme), delivered via
   the `access-token` cookie set by the Bank's existing Intranet gateway (or a raw `Authorization:
   Bearer` header). Role-based access control via the `appRoles` claim (see `AppRoles` constants).
   Signing settings are loaded from the SSO database via `[dbo].[GetTokenSettings]` — a faithful
   port of the Bank's shared `ServiceManager.RegisterAuthService` pattern — or from an appsettings
   fallback for local development (`Sso:UseDatabaseTokenSettings: false`).
2. **Reversal engine** (`/api/reversals/pending`, `/api/reversals/callback`) — static API key via
   the `X-API-Key` header, matching `provider-integration-contract.md` §3 `AuthType: ApiKey`. These
   two endpoints are what Wisdom's `SingleReversalEngine.Orchestrator` polls and posts back to;
   BulkReversal.API is the "provider" in that contract.

## Configuration

Everything is environment-configurable via `appsettings.json` / `appsettings.{Environment}.json`
(and, in production, environment variables — e.g. `ProviderApiKey__ApiKeys__0=...`):

| Section | Purpose |
|---|---|
| `ConnectionStrings:BulkReversalConnection` | SQL Server database for this app |
| `ConnectionStrings:SingleSignOnConnection` | SSO database hosting `GetTokenSettings` |
| `BusinessRules` | BRU-01/02: max records per file, allowed extensions, max file size |
| `ProviderIntegration` | Max items per engine poll, reversal currency |
| `Sso` | SSO cookie/claim names, DB-vs-appsettings token settings toggle |
| `ProviderApiKey` | Accepted API keys for the engine-facing endpoints |
| `RateLimiting:Provider` | Fixed-window rate limit applied to the provider endpoints |
| `ForwardedHeaders` | Reverse-proxy header trust (scheme/host correctness post-deployment) |
| `Swagger:Enabled` | Toggle Swagger UI/JSON (on by default, including in deployed environments) |

## Running locally

```bash
dotnet restore
dotnet ef database update --project src/BulkReversal.Infrastructure --startup-project src/BulkReversal.API
dotnet run --project src/BulkReversal.API
```

Swagger UI: `https://localhost:<port>/swagger` — two documents are published, **v1** (Settlement
portal) and **provider** (the two engine-facing endpoints), each with its own security scheme.

`appsettings.Development.json` ships with `Sso:UseDatabaseTokenSettings: false` and a fixed dev
signing key/API key so the app runs without a live SSO database — **never** set that to `false`
in a deployed environment.

## Tests

None yet — the project is scoped to the initial implementation. Recommended next step: unit tests
for `UploadRowFieldValidator`, `BatchUploadService` (BRU-01/04/06), and `ProviderIntegrationService`
(idempotent callback handling per §2.5), plus a `WebApplicationFactory`-based integration test for
the two provider endpoints against the provider-integration-contract.md examples.
