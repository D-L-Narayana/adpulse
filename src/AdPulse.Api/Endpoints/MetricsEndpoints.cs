using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using AdPulse.Api.Contracts;
using AdPulse.Api.Data;
using AdPulse.Api.Domain;
using AdPulse.Api.Services;
using Microsoft.EntityFrameworkCore;

namespace AdPulse.Api.Endpoints;

public static class MetricsEndpoints
{
    public const string IdempotencyHeader = "Idempotency-Key";
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    public static IEndpointRouteBuilder MapMetrics(this IEndpointRouteBuilder app)
    {
        var g = app.MapGroup("/api/v1/metrics").WithTags("Metrics");

        // REST ingest: push campaign-days in one call. Campaigns named in the rows are created on first sight.
        // Idempotency-Key: the first response is stored and replayed for retries with the same key + body.
        g.MapPost("/bulk", async (HttpContext http, AdPulseDbContext db, MetricsIngest ingest) =>
        {
            http.Request.EnableBuffering();
            string raw;
            using (var sr = new StreamReader(http.Request.Body, Encoding.UTF8, leaveOpen: true)) raw = await sr.ReadToEndAsync();
            BulkMetricsRequest? req;
            try { req = JsonSerializer.Deserialize<BulkMetricsRequest>(raw, Json); }
            catch (JsonException ex) { return Problems.Create(http, 400, "invalid_json", ex.Message); }
            if (req is null) return Problems.Create(http, 400, "invalid_json", "Body must be {\"rows\":[...]}.");
            if (Problems.Validate(http, req) is { } bad) return bad;
            foreach (var r in req.Rows) if (Problems.Validate(http, r) is { } badRow) return badRow;
            if (req.Rows.Any(r => string.IsNullOrWhiteSpace(r.Campaign) && r.CampaignId is null))
                return Problems.Create(http, 422, "missing_campaign", "Every row needs 'campaign' (name) or 'campaignId'.");

            var key = http.Request.Headers[IdempotencyHeader].FirstOrDefault()?.Trim();
            if (key is { Length: > 128 }) return Problems.Create(http, 422, "idempotency_key_too_long", "Idempotency-Key must be <= 128 characters.");
            var bodyHash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(raw))).ToLowerInvariant();
            if (key is { Length: > 0 })
            {
                var prior = await db.IdempotencyRecords.AsNoTracking().SingleOrDefaultAsync(r => r.Key == key);
                if (prior is not null)
                {
                    if (prior.RequestHash != bodyHash)
                        return Problems.Create(http, 422, "idempotency_key_reused", "This Idempotency-Key was already used with a different request body.");
                    var replay = JsonSerializer.Deserialize<BulkMetricsResponse>(prior.ResponseJson, Json)! with { Replayed = true };
                    http.Response.Headers["Idempotent-Replayed"] = "true";
                    return Results.Json(replay, Json, statusCode: prior.StatusCode);
                }
            }

            // Resolve campaignId -> name (ids must belong to this tenant; the query filter guarantees that).
            var byId = req.Rows.Where(r => r.CampaignId is not null).Select(r => r.CampaignId!.Value).Distinct().ToList();
            var idNames = byId.Count == 0 ? new Dictionary<Guid, Campaign>() : await db.Campaigns.AsNoTracking().Where(c => byId.Contains(c.Id)).ToDictionaryAsync(c => c.Id);
            var unknown = byId.Where(id => !idNames.ContainsKey(id)).ToList();
            if (unknown.Count > 0) return Problems.Create(http, 404, "campaign_not_found", $"Unknown campaignId(s): {string.Join(", ", unknown)}.");

            var rows = req.Rows.Select(r =>
            {
                var c = r.CampaignId is not null ? idNames[r.CampaignId.Value] : null;
                return new NormalizedRow(c?.Name ?? r.Campaign!.Trim(), r.Channel ?? c?.Channel ?? Channel.Other, r.Date, r.Impressions, r.Clicks, r.Conversions, Math.Round(r.Spend, 2), Math.Round(r.Revenue, 2));
            }).ToList();
            if (rows.GroupBy(r => (r.Campaign.ToLowerInvariant(), r.Date)).Any(grp => grp.Count() > 1))
                return Problems.Create(http, 422, "duplicate_rows", "Each campaign/date pair may appear only once per request.");

            var result = await ingest.ApplyAsync(rows);
            var response = new BulkMetricsResponse(rows.Count, result.CampaignsCreated, result.MetricsInserted, result.MetricsUpdated, result.CampaignIds, false, key);
            if (key is { Length: > 0 })
            {
                db.IdempotencyRecords.Add(new IdempotencyRecord { Key = key, RequestHash = bodyHash, StatusCode = 200, ResponseJson = JsonSerializer.Serialize(response, Json) });
                try { await db.SaveChangesAsync(); }
                catch (DbUpdateException) { /* concurrent retry raced us to the same key; the upsert is idempotent so the data is identical */ }
            }
            return Results.Ok(response);
        })
        .Accepts<BulkMetricsRequest>("application/json")
        .WithSummary("Bulk upsert campaign-day metrics (creates campaigns by name). Idempotent via Idempotency-Key header.");

        return app;
    }
}
