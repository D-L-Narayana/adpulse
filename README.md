# AdPulse — Multi-tenant Advertising Campaign & Spend API

C# / ASP.NET Core 8 minimal-API backend for managing advertising campaigns, daily spend and budget pacing across many isolated tenants — the kind of service that sits behind an "advertising cloud" dashboard.

**Stack:** C# 12 · ASP.NET Core 8 · Entity Framework Core · MS SQL Server (SQLite for dev/tests) · Vue 3 + TypeScript dashboard (`web/`) · xUnit + Reqnroll (BDD / Gherkin) · Serilog → Logstash → Elasticsearch → Kibana (ELK) · Docker Compose · GitHub Actions CI

## Features
- **Multi-tenancy** — `X-Api-Key` header resolves a tenant in middleware; every EF Core query is filtered by `TenantId` via global query filters, so one tenant can never read another tenant's campaigns (covered by tests).
- **Campaign lifecycle** — Draft → Active → Paused → Completed with validated transitions (RFC 7807 problem details on invalid moves).
- **Spend ingestion & KPIs** — daily spend/impressions/clicks/conversions per channel; CTR, CPC, CPA, ROAS computed server-side.
- **Budget pacing** — linear expected spend vs actual, status bands (under < 90 % / on-track / over > 110 %) and a suggested corrected daily budget for the remaining flight.
- **Reports** — `/api/reports/channel-mix`, `/api/reports/daily` with date-range filtering and SQL aggregation.
- **Observability** — structured JSON logs enriched with `TenantId` and `RequestId`, shipped to ELK; `/health` with a DB check; Swagger UI.

## Run
```bash
docker compose up --build          # API :8080, Kibana :5601, SQL Server :1433
dotnet test                        # xUnit API tests (SQLite) + Reqnroll BDD specs
```
CI runs the same test suite twice — against SQLite and against a real SQL Server 2022 service container — and builds the Docker image.

## Layout
```
src/AdPulse.Api        Program.cs, Endpoints/, Domain/, Data/ (EF Core), Tenancy/, Services/ (Kpi, Pacing)
tests/AdPulse.Tests    xUnit + WebApplicationFactory integration tests
tests/AdPulse.Specs    Reqnroll (SpecFlow successor) Gherkin features + step definitions
web/                   Vue 3 + Vite + TypeScript dashboard
elk/                   Logstash pipeline
```
