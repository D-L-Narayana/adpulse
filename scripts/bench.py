#!/usr/bin/env python3
"""
AdPulse latency benchmark (stdlib only - no pip installs needed).

Measures p50 / p95 / p99 for the hot read endpoints under concurrent load and
prints a Markdown table. Used to produce docs/benchmarks.md.

  python3 scripts/bench.py --base http://localhost:5080 --key acme-demo-key --concurrency 16 --requests 400

For a heavier, scripted load test use scripts/bench.k6.js (k6 run scripts/bench.k6.js).
"""
import argparse
import json
import statistics
import time
import urllib.request
from concurrent.futures import ThreadPoolExecutor

ENDPOINTS = [
    ("GET", "/api/v1/campaigns"),
    ("GET", "/api/v1/campaigns/summary"),
    ("GET", "/api/v1/reports/cross-channel"),
    ("GET", "/api/v1/reports/daily"),
    ("GET", "/api/v1/campaigns/{id}/pacing"),
    ("GET", "/api/v1/campaigns/{id}/kpis"),
]


def call(base, key, method, path):
    req = urllib.request.Request(base + path, method=method, headers={"X-Api-Key": key})
    t0 = time.perf_counter()
    with urllib.request.urlopen(req, timeout=30) as r:
        r.read()
        status = r.status
    return (time.perf_counter() - t0) * 1000.0, status


def pct(sorted_values, p):
    if not sorted_values:
        return float("nan")
    k = (len(sorted_values) - 1) * p
    f, c = int(k), min(int(k) + 1, len(sorted_values) - 1)
    return sorted_values[f] + (sorted_values[c] - sorted_values[f]) * (k - f)


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("--base", default="http://localhost:8080")
    ap.add_argument("--key", default="acme-demo-key")
    ap.add_argument("--concurrency", type=int, default=16)
    ap.add_argument("--requests", type=int, default=400, help="requests per endpoint")
    ap.add_argument("--warmup", type=int, default=20)
    ap.add_argument("--json", help="also write raw results to this file")
    a = ap.parse_args()

    # resolve a campaign id for the per-campaign endpoints
    req = urllib.request.Request(a.base + "/api/v1/campaigns", headers={"X-Api-Key": a.key})
    campaigns = json.load(urllib.request.urlopen(req, timeout=30))
    if not campaigns:
        raise SystemExit("tenant has no campaigns - run with the seeded demo key")
    cid = campaigns[0]["id"]

    print(f"base={a.base} concurrency={a.concurrency} requests/endpoint={a.requests}\n")
    print("| Endpoint | Requests | Errors | p50 ms | p95 ms | p99 ms | max ms | req/s |")
    print("|---|---:|---:|---:|---:|---:|---:|---:|")
    results = {}
    with ThreadPoolExecutor(max_workers=a.concurrency) as pool:
        for method, tmpl in ENDPOINTS:
            path = tmpl.replace("{id}", cid)
            for _ in range(a.warmup):
                call(a.base, a.key, method, path)
            t0 = time.perf_counter()
            samples = list(pool.map(lambda _: call(a.base, a.key, method, path), range(a.requests)))
            wall = time.perf_counter() - t0
            lat = sorted(s[0] for s in samples)
            errors = sum(1 for s in samples if s[1] >= 400)
            row = {
                "p50": pct(lat, 0.50), "p95": pct(lat, 0.95), "p99": pct(lat, 0.99), "max": lat[-1],
                "mean": statistics.fmean(lat), "rps": a.requests / wall, "errors": errors,
            }
            results[f"{method} {tmpl}"] = row
            print(f"| `{method} {tmpl}` | {a.requests} | {errors} | {row['p50']:.1f} | {row['p95']:.1f} | {row['p99']:.1f} | {row['max']:.1f} | {row['rps']:.0f} |")
    if a.json:
        with open(a.json, "w") as f:
            json.dump(results, f, indent=2)


if __name__ == "__main__":
    main()
