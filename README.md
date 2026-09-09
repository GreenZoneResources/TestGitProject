# BulkReversal.API

Failed Transaction Reversal Portal backend — implements the BRD for bulk upload, validation,
approval, and status monitoring of failed-transaction reversals (NIP, Bill Payment, Card), and
acts as the **provider** consumed by `SingleReversalEngine.Orchestrator` (the reversal engine)
per `provider-integration-contract.md`.

## Architecture

Clean Architecture, four projects under `src/`:

- **BulkReversal.Domain** — entities (`ReversalBatch`, `ReversalTransaction`, `AuditLogEntry`),
  enums, and business-rule invariants. No dependencies on anything else.
- **BulkReversal.Application** — use-case services (batch validation, approvals, status
  monitoring, provider integration, audit), DTOs, FluentValidation validators, and the
  ports (`I...Repository`, `IUnitOfWork`, `ITransferService`, `IReportExportService`, ...)
  that Infrastructure implements. Depends only on Domain.
- **BulkReversal.Infrastructure** — EF Core (SQL Server) persistence, repositories, the Transfer
  Service HTTP client (BRU-05), Excel/PDF report generation (ClosedXML/QuestPDF), and both
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
   the standard `Authorization: ApiKey <key>` header, matching `provider-integration-contract.md`
   §3 `AuthType: ApiKey`. SSO is never a criterion for these two endpoints — they are what Wisdom's
   `SingleReversalEngine.Orchestrator` polls and posts back to; BulkReversal.API is the "provider"
   in that contract.

## Configuration

Everything is environment-configurable via `appsettings.json` / `appsettings.{Environment}.json`
(and, in production, environment variables — e.g. `ProviderApiKey__ApiKeys__0=...`):

| Section | Purpose |
|---|---|
| `ConnectionStrings:BulkReversalConnection` | SQL Server database for this app |
| `ConnectionStrings:SingleSignOnConnection` | SSO database hosting `GetTokenSettings` |
| `BusinessRules` | BRU-01: max records per batch; max accepted transaction-date age |
| `ProviderIntegration` | Max items per engine poll, reversal currency |
| `TransferService` | BRU-05 source-transaction check: enabled toggle, BaseUrl/path/ApiKey, timeout, max concurrent lookups |
| `Sso` | SSO cookie/claim names, DB-vs-appsettings token settings toggle |
| `ProviderApiKey` | Accepted API keys for the engine-facing endpoints |
| `RateLimiting:Provider` | Fixed-window rate limit applied to the provider endpoints |
| `ForwardedHeaders` | Reverse-proxy header trust (scheme/host correctness post-deployment) |
| `Swagger:Enabled` | Toggle Swagger UI/JSON (on by default, including in deployed environments) |
| `Database:Provider` | `SqlServer` (default, the only supported provider in a deployed environment) or `InMemory` — a local/dev-only escape hatch to run the app and exercise its endpoints without a live SQL Server. Never set to `InMemory` outside local exploration: no migrations, no durability, no concurrency safety. |

## Running locally

```bash
dotnet restore
dotnet ef database update --project src/BulkReversal.Infrastructure --startup-project src/BulkReversal.API
dotnet run --project src/BulkReversal.API
```

Swagger UI: `https://localhost:<port>/swagger` — two documents are published, **v1** (Settlement
portal) and **provider** (the two engine-facing endpoints), each with its own security scheme.
Per-endpoint description text is intentionally omitted (XML doc comments are not fed into Swagger)
for a leaner UI — only routes, parameters, and schemas are shown.

`appsettings.Development.json` ships with `Sso:UseDatabaseTokenSettings: false` and a fixed dev
signing key/API key so the app runs without a live SSO database — **never** set that to `false`
in a deployed environment.

### Creating and staging a batch

The frontend parses/collects rows itself and posts them as structured JSON — this app does not
parse an uploaded file server-side:

```
POST /api/v1/upload
{
  "batchName": "Array Test Batch",
  "transactions": [
    {
      "transactionType": "Nip",
      "sessionIdOrFtReference": "FT0000000001",
      "accountNumber": "0123456789",
      "transactionDate": "2026-08-01",
      "transactionAmount": 50000.00,
      "channel": "NIP",
      "beneficiaryBank": "GTBank",
      "reasonForFailure": "No value received by beneficiary"
    }
  ]
}
```

Every row is validated on the way in (field rules; BRU-04 already-reversed; BRU-06 in-batch
duplicate; BRU-05 source-transaction match via the Transfer Service, when `TransferService:Enabled`
is true) and marked Valid/Invalid — the request itself always succeeds as long as it's
structurally well-formed (batch name present, at least one row, row count within
`BusinessRules:MaxRecordsPerFile`); an individual row failing business validation never rejects
the whole batch.

### Upload results screen (edit/delete/retry before submission)

A batch stays in the **Validated** status — editable — after `POST /api/v1/upload` until Settlement
explicitly calls `POST /api/v1/upload/{batchReference}/submit`. While Validated:

- `GET /api/v1/upload/{batchReference}/records` — list staged rows for the review table.
- `PUT /api/v1/upload/{batchReference}/records/{transactionId}` — correct a row in place (partial
  update — only supplied fields change) and revalidate it (field rules + BRU-04/05/06 checks).
- `DELETE /api/v1/upload/{batchReference}/records/{transactionId}` — remove a row.
- `POST /api/v1/upload/{batchReference}/records/{transactionId}/retry` — revalidate a row's current
  values without changing them (e.g. after a Transfer Service outage clears).

All four return the refreshed batch summary (counts + remaining invalid rows). Once the batch is
submitted (or later approved/rejected), every one of these returns `400` — rows can't shift under
a reviewer once submitted for approval.

## Tests

`tests/BulkReversal.UnitTests` (xUnit + Moq) covers the upload validation flow:

```bash
dotnet test tests/BulkReversal.UnitTests
```

- `UploadRowFieldValidatorTests` — mandatory fields, account number format, type-conditional
  fields (RRN/Beneficiary Bank/Biller), date window, amount.
- `BatchUploadServiceTests` — BRU-01 (max records), BRU-04 (already reversed), BRU-05 (Transfer
  Service not-found/unavailable/mismatch/match, via a mocked `ITransferService`), BRU-06 (in-batch
  duplicate), and edit/delete/retry/submit against the real `ReversalBatch`/`ReversalTransaction`
  domain aggregate (repositories mocked).
- `TransferServiceClientTests` — URL construction (BaseUrl + path + percent-encoded reference,
  confirming reserved characters in a reference can't redirect the request to a different path),
  the `ApiKey` header, response-code mapping, and retry-then-`Unavailable` on connection failure —
  against a fake `HttpMessageHandler`, no real network.
- `ReversalBatchTests` / `ReversalTransactionTests` — the edit/delete mutability gate
  (`EnsureMutable`) and callback idempotency (provider contract §2.5).
