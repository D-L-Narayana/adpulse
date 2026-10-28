# AdPulse — Multi-tenant Advertising Campaign & Budget-Pacing Platform

[![CI](https://github.com/D-L-Narayana/adpulse/actions/workflows/ci.yml/badge.svg)](https://github.com/D-L-Narayana/adpulse/actions/workflows/ci.yml)
**Live dashboard:** https://adpulse-dashboard-nu.vercel.app (runs standalone on a bundled snapshot of the seeded API)

AdPulse is the kind of service that sits behind an "advertising cloud" dashboard: hundreds of advertiser tenants,
each with campaigns across Google Ads / Meta / Amazon / LinkedIn / DV360, daily spend ingestion, server-side KPIs
(CTR, CPC, CPA, ROAS) and a budget-pacing engine that tells you *today* whether a campaign will blow or under-spend
its budget and what the corrected daily budget should be.

| Layer | Tech |
|---|---|
| API | C# 12 · ASP.NET Core 8 minimal API · EF Core 8 · MS SQL Server 2022 (SQLite for dev/tests) |
| Tenancy | `X-Api-Key` middleware → `TenantContext` → EF Core global query filters on `TenantId` + write guard |
| Tests | xUnit + `WebApplicationFactory` (111 tests, 40 tenant-isolation cases) · Reqnroll BDD (18 Gherkin scenarios) · Coverlet |
| Observability | Serilog compact JSON enriched with `TenantId` / `RequestId` → Logstash → Elasticsearch → Kibana |
| Web | Vue 3 + Vite + TypeScript dashboard (KPI cards, pacing badges, SVG spend chart) · Vercel |
| Ops | Docker Compose (api + sqlserver + ELK) · GitHub Actions (SQLite + SQL Server 2022 service container) |

---

## Architecture

```
                 X-Api-Key: acme-demo-key
  Vue 3 dashboard  ────────────────────────►  ASP.NET Core 8 minimal API (Kestrel :8080)
  (Vercel / Vite)   ◄── JSON / problem+json ──   │
        │ falls back to                          │  TenantMiddleware        resolve key → Tenant, push TenantId into LogContext
        │ bundled mock JSON                      │  Endpoints/              Campaigns · Metrics · Lifecycle · Reports
        │ when API unreachable                   │  Services/               Kpi (pure math) · Pacing (pure math) · CampaignLifecycle (state table)
                                                 │  Data/AdPulseDbContext   HasQueryFilter(x => x.TenantId == tenant.TenantId) on every tenant-owned entity
                                                 │                          SaveChanges stamps TenantId / rejects cross-tenant writes
                                                 │  Data/DbSeeder           2 demo tenants + 500 synthetic tenants (~1.5k campaigns, ~21k metric rows)
                                                 │
                        ┌────────────────────────┼─────────────────────────┐
                        ▼                        ▼                                 ▼
              SQL Server 2022 (prod/CI)   SQLite (dev / tests)        Serilog JSON ─TCP:5000► Logstash ─► Elasticsearch ─► Kibana
              same EF model, same tests   in-memory per test class    {"@t","@m","TenantId","RequestId","StatusCode","Elapsed"...}
```

```
src/AdPulse.Api/        Program.cs, Endpoints/, Domain/, Data/, Tenancy/, Services/, Contracts/ (DTOs + RFC 7807 helper)
tests/AdPulse.Tests/    xUnit integration tests (WebApplicationFactory, SQLite or SQL Server via env vars)
tests/AdPulse.Specs/    Reqnroll: Features/*.feature + Steps/ + Support/ApiDriver (WebApplicationFactory per scenario)
web/                    Vue 3 + Vite + TS dashboard, src/mock/*.json = snapshots from the seeded API
elk/logstash.conf       Serilog compact JSON → ECS-ish fields → adpulse-* indices
scripts/                bench.py (stdlib p95 bench), bench.k6.js (k6 profile)    docs/benchmarks.md = method + numbers
docker-compose.yml      api + mssql + elasticsearch + logstash + kibana
.github/workflows/      ci.yml — build, test on SQLite (+ Coverlet), test on SQL Server 2022 container, docker build, web build
```

---

## Features

* **Multi-tenancy by construction** — one shared schema; isolation is enforced twice so a forgotten `WHERE` can never leak:
  1. `TenantMiddleware` resolves `X-Api-Key` → `Tenant` (unique index) and stores it in a scoped `TenantContext`; `/api/*` without a valid key is a `401 application/problem+json`.
  2. `AdPulseDbContext` applies `HasQueryFilter(x => x.TenantId == tenant.TenantId)` to every `ITenantOwned` entity (`Campaign`, `DailyMetric`), and `SaveChangesAsync` stamps `TenantId` on inserts and throws on cross-tenant modify/delete.
* **Campaign lifecycle** — `Draft → Active ⇄ Paused → Completed` encoded as a transition table (`CampaignLifecycle`). Invalid moves, re-activating an expired flight, or editing a Completed campaign return RFC 7807 `ProblemDetails` with a stable `type` URI, `currentStatus`/`action` extensions and a `traceId` that matches the ELK log line.
* **Spend ingestion** — `PUT /api/campaigns/{id}/metrics` upserts one row per day (idempotent, reports inserted vs updated, rejects duplicate dates / negatives).
* **Server-side KPIs** — CTR, CPC, CPA, ROAS computed in one place (`Kpi`) from SQL `GROUP BY` aggregates; zero-safe (no division by zero), rounded to 2 dp; identical for per-campaign, per-channel and total rows.
* **Budget-pacing engine** — see below. Flags under/over-spend outside a ±10 % band and returns the corrected daily budget.
* **Reports** — `/api/reports/cross-channel` (one KPI row per channel + total, sorted by spend) and `/api/reports/daily` (trend for the chart), both date-range filtered and aggregated in SQL.
* **Seeder** — 2 named demo tenants with 30 days of history showing all pacing bands + **500 synthetic tenants** (deterministic RNG) for isolation and performance testing. Boots in ~5 s on SQLite.
* **Observability** — Serilog `RenderedCompactJsonFormatter` to console and (optionally) TCP to Logstash; every request log carries `RequestId`, `TenantId`, `TenantName`, `RequestPath`, `StatusCode`, `Elapsed`. `/health` includes an EF Core DB check. Swagger UI at `/swagger` with the API-key security scheme wired in.

---

## API

All `/api/*` routes require `X-Api-Key`. Errors are `application/problem+json` (RFC 7807) with `type = https://adpulse.dev/errors/<code>`.

| Method | Route | Purpose | Notable responses |
|---|---|---|---|
| GET | `/health` | Liveness + DB check (public) | 200 |
| GET | `/api/me` | Resolved tenant | 200 `{id,name}` · 401 `missing_api_key` / `unknown_api_key` |
| GET | `/api/campaigns?status=&channel=` | List (tenant-scoped, filterable) | 200 |
| GET | `/api/campaigns/summary?asOf=` | Campaigns + lifetime KPIs + pacing in one call (dashboard feed) | 200 |
| POST | `/api/campaigns` | Create (Draft) | 201 + Location · 422 `validation_error` / `invalid_dates` · 409 `duplicate_name` |
| GET / PUT / DELETE | `/api/campaigns/{id}` | Read / update / delete (cascades metrics) | 404 for foreign tenant · 409 `campaign_completed` |
| POST | `/api/campaigns/{id}/activate` | Draft/Paused → Active | 409 `invalid_transition` · 422 `flight_ended` |
| POST | `/api/campaigns/{id}/pause` | Active → Paused | 409 `invalid_transition` |
| POST | `/api/campaigns/{id}/complete` | Active/Paused → Completed (terminal) | 409 `invalid_transition` |
| PUT | `/api/campaigns/{id}/metrics` | Upsert daily rows | 200 `{inserted,updated}` · 422 `duplicate_dates` |
| GET | `/api/campaigns/{id}/metrics?from=&to=` | Raw daily rows | 200 |
| GET | `/api/campaigns/{id}/kpis?from=&to=` | CTR / CPC / CPA / ROAS for one campaign | 200 |
| GET | `/api/campaigns/{id}/pacing?asOf=` | Pacing status + suggested daily budget | 200 |
| GET | `/api/reports/cross-channel?from=&to=` | Per-channel KPI rows + total | 200 · 422 `invalid_range` |
| GET | `/api/reports/daily?from=&to=` | Daily spend/revenue/clicks/conversions | 200 |

Example problem response:

```json
POST /api/campaigns/{id}/pause on a Draft campaign → 409 application/problem+json
{
  "type": "https://adpulse.dev/errors/invalid_transition",
  "title": "Conflict",
  "status": 409,
  "detail": "Cannot move a Draft campaign to Paused. Allowed from: Active.",
  "currentStatus": "Draft",
  "action": "pause",
  "traceId": "0HNPKP13ANTT5:00000001"
}
```

---

## Tenancy design

```
request ─► TenantMiddleware ( /api/* only )
            │  key = X-Api-Key header
            │  tenant = Tenants.Single(t => t.ApiKey == key)      -- unique index, AsNoTracking
            │  TenantContext.Set(tenant)                          -- scoped per request
            │  LogContext.PushProperty("TenantId", tenant.Id)     -- every log line below is tagged
            ▼
         endpoint ─► AdPulseDbContext(options, TenantContext)
                       Campaigns.Where(...)      => SQL always gets  AND TenantId = @tenant   (global query filter)
                       Add(new Campaign{...})    => TenantId stamped in SaveChangesAsync
                       Update/Delete foreign row => InvalidOperationException("Cross-tenant write rejected.")
```

* Foreign resources are indistinguishable from non-existent ones (`404`, never `403`) so ids cannot be enumerated.
* Uniqueness is per tenant: `(TenantId, Name)` on campaigns, `(CampaignId, Date)` on metrics; hot paths are covered by `(TenantId, Status)` and `(TenantId, Date)` indexes so the filter is always an index seek.
* The seeder / tests that need to cross tenants use `IgnoreQueryFilters()` explicitly — it never happens implicitly.
* **40 isolation tests** (`TenantIsolationTests`) = 20 hostile probes × 2 attacker profiles (an empty tenant, and a tenant owning a *decoy* campaign with the same name/channel). Every endpoint is probed for reads, writes, lifecycle actions and reports, the victim's data is asserted intact afterwards, and one probe bypasses HTTP to prove the filter + write guard at the `DbContext` level.

---

## Pacing algorithm

`Services/Pacing.cs` is a pure function so it is unit-tested exhaustively (thresholds, clamping, division by zero).

```
daysTotal      = EndDate - StartDate + 1
daysElapsed    = clamp(asOf - StartDate + 1, 0, daysTotal)
expected       = DailyBudget × daysElapsed                      -- linear pacing
pacing%        = actual / expected × 100

status         = not-started  if daysElapsed = 0
               | under        if pacing% <  90      (beyond the −10 % band)
               | over         if pacing% > 110      (beyond the +10 % band)
               | on-track     otherwise

remaining      = max(0, DailyBudget × daysTotal − actual)
suggestedDaily = remaining / (daysTotal − daysElapsed)           -- corrected budget that lands the flight at 100 %
deviation      = actual − expected
```

Example (10-day flight, ₹1000/day, day 5): spent 6500 → 130 % **over** → suggest 700/day; spent 3000 → 60 % **under** → suggest 1400/day.
Classification uses the exact ratio, not the 1-dp display value, so 89.998 % is correctly "under". The dashboard renders the band, the fill and the suggested correction per campaign.

---

## Testing

```bash
dotnet test                                   # 129 tests: 111 xUnit + 18 Reqnroll scenarios, SQLite in-memory, ~6 s
dotnet test --collect:"XPlat Code Coverage"   # Coverlet cobertura report under TestResults/
ADPULSE_TEST_PROVIDER=SqlServer ADPULSE_TEST_CONNECTION="Server=localhost,1433;..." dotnet test   # same suites on SQL Server
```

| Project | What | Count |
|---|---|---|
| `tests/AdPulse.Tests` | `WebApplicationFactory<Program>` integration tests: auth, CRUD, validation, lifecycle matrix (every state × action), metrics upsert, KPIs, pacing, reports, seeder (502 tenants), **40 tenant-isolation cases** via `[Theory]` + `MemberData` | **111** |
| `tests/AdPulse.Specs` | Reqnroll (SpecFlow successor) BDD: `CampaignLifecycle` (6), `Pacing` (5), `TenantIsolation` (4), `Kpis` (3) `.feature` files; step definitions in `Steps/`; `Support/ApiDriver` boots the API per scenario | **18 scenarios** |

Coverage from the integration suite: ~96 % line / ~84 % branch of `AdPulse.Api` (Coverlet). CI runs both projects twice — against SQLite and against a **SQL Server 2022 service container** — then builds the Docker image and the Vue app.

A Gherkin sample (`tests/AdPulse.Specs/Features/Pacing.feature`):

```gherkin
Scenario: Campaign is under-pacing beyond the 10% band
  Given the campaign has spent 3000.00 by 2025-10-05
  When I request pacing as of 2025-10-05
  Then the pacing percentage is 60.0
  And the pacing status is "under"
  And the suggested daily budget is 1400.00
```

---

## Logging & ELK

* Serilog writes compact JSON (`@t`, `@m`, `@l`, properties) to stdout; set `Logging__LogstashTcp=tcp://logstash:5000` to also ship over TCP.
* `UseSerilogRequestLogging` enriches every request line with `RequestId` (= `HttpContext.TraceIdentifier`, also returned as `traceId` in problem responses) and `TenantId`.
* `elk/logstash.conf` parses the JSON lines, maps `@t→@timestamp`, `RequestPath→http.path`, `StatusCode→http.status`, `Elapsed→http.elapsed_ms` and indexes into `adpulse-adpulse-api-YYYY.MM.dd`.
* In Kibana: filter `TenantId: "<guid>"` to see one advertiser's traffic, or visualise `http.elapsed_ms` percentiles per `http.path`.

```json
{"@t":"2026-10-28T07:51:29.93Z","@m":"HTTP \"GET\" \"/api/campaigns/summary\" responded 200 in 15.7 ms","RequestId":"0HNPKP13ANTT5:00000001","TenantId":"4209e416-...","RequestPath":"/api/campaigns/summary","StatusCode":200,"Elapsed":15.7,"Application":"adpulse-api"}
```

---

## Performance

Measured with `scripts/bench.py` against the 502-tenant seed (method + full tables in [`docs/benchmarks.md`](docs/benchmarks.md)):
16 concurrent clients on a 2-vCPU box → every dashboard endpoint **< 60 ms p95**, single-row endpoints ~15–20 ms p95, 0 errors.
`scripts/bench.k6.js` encodes the same budget as k6 thresholds (`p(95)<150`, `http_req_failed<1%`).

---

## Run it

```bash
# Everything (API :8080, SQL Server :1433, Kibana :5601)
docker compose up --build

# API only, SQLite, seeds 502 tenants on first start
dotnet run --project src/AdPulse.Api            # http://localhost:5000/swagger
curl -H 'X-Api-Key: acme-demo-key' http://localhost:5000/api/campaigns/summary

# Dashboard
cd web && npm install
VITE_API_URL=http://localhost:5000 npm run dev  # live mode (add the origin to Cors:Origins if not :5173)
npm run build                                   # without VITE_API_URL the build uses the bundled snapshot → Vercel demo
```

Demo API keys: `acme-demo-key`, `globex-demo-key`, and `tenant-0001-key` … `tenant-0500-key` for the synthetic tenants.

Configuration (env vars override `appsettings.json`): `Database__Provider` (`Sqlite`|`SqlServer`), `ConnectionStrings__Default`, `Database__Seed`, `Database__SeedTenantCount`, `Logging__LogstashTcp`, `Cors__Origins__0`.

---

## Live demo

**https://adpulse-dashboard-nu.vercel.app** — the Vue dashboard deployed on Vercel. Without a reachable `VITE_API_URL` it loads `web/src/mock/*.json`, which are verbatim responses captured from the seeded API for both demo tenants, so KPI cards, channel mix, the spend chart and pacing badges are real output of the pacing engine. Point `VITE_API_URL` at a running API to switch to live mode (lifecycle buttons appear).
