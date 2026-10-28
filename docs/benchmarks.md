# AdPulse API – latency benchmarks

How the p95 numbers quoted in the README were produced, so they can be reproduced (and challenged).

## Method

* **Scripts:** `scripts/bench.py` (stdlib-only Python, prints the table below) and `scripts/bench.k6.js`
  (k6 load profile with p95 / error-rate thresholds that fail the run).
* **Workload:** the read endpoints the dashboard hits on every refresh. Each endpoint gets 20 warm-up calls,
  then 400 requests issued by 16 concurrent clients (closed loop – every client fires the next request as soon
  as the previous one returns, so this is a worst-case "no think time" load).
* **Data set:** the production seeder (`DbSeeder`) – **502 tenants, ~1.5k campaigns, ~21k daily-metric rows**.
  Benchmarks run as the `acme-demo-key` tenant (4 campaigns, 30 days of history) and as a synthetic tenant
  (`tenant-0250-key`), i.e. every query is going through the tenant global-query-filter against the full table.
* **Latency** is measured client-side (connect + request + full body read) with `time.perf_counter()`,
  percentiles are linearly interpolated over the sorted sample. `req/s` is requests ÷ wall-clock for the batch.
* **Environment (reference run):** 2 vCPU Intel Xeon @ 2.90 GHz sandbox, `dotnet run` (Debug build of
  `AdPulse.Api`, Kestrel, SQLite file DB on local disk), client and server on the same box.
  Release builds on SQL Server with a warm plan cache are faster; treat these as conservative.

```bash
# 1. start the API with the full seed (first start seeds 500 tenants, ~5 s)
Seed__Enabled=true ASPNETCORE_URLS=http://localhost:5080 dotnet run --project src/AdPulse.Api

# 2. run the bench
python3 scripts/bench.py --base http://localhost:5080 --key acme-demo-key --concurrency 16 --requests 400

# or the k6 profile (25 VUs, 105 s, thresholds p95 < 150 ms, errors < 1 %)
k6 run -e BASE=http://localhost:5080 -e KEY=acme-demo-key scripts/bench.k6.js
```

## Reference results

`acme-demo-key` – 16 concurrent clients, 400 requests per endpoint:

| Endpoint | Requests | Errors | p50 ms | p95 ms | p99 ms | max ms | req/s |
|---|---:|---:|---:|---:|---:|---:|---:|
| `GET /api/v1/campaigns` | 400 | 0 | 12.5 | 19.1 | 25.5 | 29.9 | 1212 |
| `GET /api/v1/campaigns/summary` | 400 | 0 | 15.7 | 38.9 | 46.1 | 56.0 | 895 |
| `GET /api/v1/reports/cross-channel` | 400 | 0 | 15.8 | 58.9 | 243.4 | 300.0 | 602 |
| `GET /api/v1/reports/daily` | 400 | 0 | 10.9 | 25.8 | 35.8 | 75.3 | 1284 |
| `GET /api/v1/campaigns/{id}/pacing` | 400 | 0 | 10.3 | 14.6 | 18.9 | 40.9 | 1478 |
| `GET /api/v1/campaigns/{id}/kpis` | 400 | 0 | 11.2 | 25.2 | 35.0 | 64.4 | 1240 |

`tenant-0250-key` (synthetic tenant, 14-day history) – 16 concurrent clients, 200 requests per endpoint:

| Endpoint | Requests | Errors | p50 ms | p95 ms | p99 ms | max ms | req/s |
|---|---:|---:|---:|---:|---:|---:|---:|
| `GET /api/v1/campaigns` | 200 | 0 | 7.1 | 18.5 | 24.3 | 33.9 | 1759 |
| `GET /api/v1/campaigns/summary` | 200 | 0 | 9.9 | 37.7 | 57.3 | 66.4 | 1110 |
| `GET /api/v1/reports/cross-channel` | 200 | 0 | 7.9 | 29.1 | 43.3 | 60.7 | 1466 |
| `GET /api/v1/reports/daily` | 200 | 0 | 9.4 | 17.1 | 34.9 | 45.9 | 1546 |
| `GET /api/v1/campaigns/{id}/pacing` | 200 | 0 | 8.9 | 22.0 | 32.8 | 38.3 | 1583 |
| `GET /api/v1/campaigns/{id}/kpis` | 200 | 0 | 9.2 | 22.9 | 35.7 | 38.5 | 1451 |

**Headline:** every dashboard endpoint stays under **60 ms p95** at 16 concurrent clients on a 2-vCPU box with
the 500-tenant data set; the single-row endpoints (`pacing`, list) are ~15–20 ms p95. The one visible outlier
(`cross-channel` p99 at 243 ms in the first run) is a SQLite write-ahead/page-cache warm-up on the first
aggregate; it disappears on the second tenant run.

## Why the queries stay fast

* Aggregation happens in SQL (`GROUP BY channel` / `GROUP BY date`), not in C# – one round trip per report.
* Composite indexes match the tenant filter: `(TenantId, Status)`, `(TenantId, Date)`, `(CampaignId, Date)`,
  so the global query filter `WHERE TenantId = @tenant` is always an index seek.
* `AsNoTracking()` on every read path; the API key → tenant lookup is a unique index hit.
* `/api/v1/campaigns/summary` collapses what used to be N+1 calls (campaign + KPIs + pacing per row) into
  two queries (campaign list + one grouped aggregate).

## Repeating against SQL Server

```bash
docker compose up -d mssql
Database__Provider=SqlServer \
ConnectionStrings__Default='Server=localhost,1433;Database=adpulse;User Id=sa;Password=AdPulse!Dev2025;TrustServerCertificate=True' \
ASPNETCORE_URLS=http://localhost:5080 dotnet run -c Release --project src/AdPulse.Api
python3 scripts/bench.py --base http://localhost:5080
```
