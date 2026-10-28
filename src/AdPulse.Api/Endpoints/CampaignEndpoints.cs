using AdPulse.Api.Contracts;
using AdPulse.Api.Data;
using AdPulse.Api.Domain;
using AdPulse.Api.Services;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;

namespace AdPulse.Api.Endpoints;

public static class CampaignEndpoints
{
    public static IEndpointRouteBuilder MapCampaigns(this IEndpointRouteBuilder app)
    {
        var g = app.MapGroup("/api/campaigns").WithTags("Campaigns");

        g.MapGet("/", async (AdPulseDbContext db, CampaignStatus? status, Channel? channel) =>
        {
            var q = db.Campaigns.AsNoTracking();
            if (status is not null) q = q.Where(c => c.Status == status);
            if (channel is not null) q = q.Where(c => c.Channel == channel);
            var list = await q.OrderBy(c => c.Name).ToListAsync();
            return Results.Ok(list.Select(CampaignResponse.From));
        }).WithSummary("List campaigns (tenant-scoped), optional ?status= & ?channel= filters");

        // Dashboard feed: every campaign with lifetime KPIs and pacing in one round-trip.
        g.MapGet("/summary", async (AdPulseDbContext db, DateOnly? asOf) =>
        {
            var day = asOf ?? DateOnly.FromDateTime(DateTime.UtcNow);
            var campaigns = await db.Campaigns.AsNoTracking().OrderBy(c => c.Name).ToListAsync();
            var agg = await db.DailyMetrics.AsNoTracking()
                .GroupBy(m => m.CampaignId)
                .Select(grp => new
                {
                    CampaignId = grp.Key,
                    Impressions = grp.Sum(m => m.Impressions), Clicks = grp.Sum(m => m.Clicks), Conversions = grp.Sum(m => m.Conversions),
                    Spend = grp.Sum(m => (double)m.Spend), Revenue = grp.Sum(m => (double)m.Revenue),
                    SpendToDate = grp.Where(m => m.Date <= day).Sum(m => (double)m.Spend)
                })
                .ToDictionaryAsync(x => x.CampaignId);

            var rows = campaigns.Select(c =>
            {
                agg.TryGetValue(c.Id, out var a);
                var spend = (decimal)(a?.Spend ?? 0); var revenue = (decimal)(a?.Revenue ?? 0);
                var p = Pacing.Compute(c, (decimal)(a?.SpendToDate ?? 0), day);
                return new CampaignSummary(c.Id, c.Name, c.Channel, c.Status, c.DailyBudget, c.StartDate, c.EndDate,
                    a?.Impressions ?? 0, a?.Clicks ?? 0, a?.Conversions ?? 0, Math.Round(spend, 2), Math.Round(revenue, 2),
                    Kpi.Ctr(a?.Clicks ?? 0, a?.Impressions ?? 0), Kpi.Cpc(spend, a?.Clicks ?? 0), Kpi.Cpa(spend, a?.Conversions ?? 0), Kpi.Roas(revenue, spend),
                    p.PacingPercent, c.Status == CampaignStatus.Draft ? PacingStatus.NotStarted : p.Status, p.SuggestedDailyBudget);
            });
            return Results.Ok(rows);
        }).WithSummary("Campaigns + lifetime KPIs + pacing status (dashboard feed)");

        g.MapGet("/{id:guid}", async Task<Results<Ok<CampaignResponse>, NotFound>> (AdPulseDbContext db, Guid id) =>
        {
            var c = await db.Campaigns.AsNoTracking().SingleOrDefaultAsync(x => x.Id == id);
            return c is null ? TypedResults.NotFound() : TypedResults.Ok(CampaignResponse.From(c));
        });

        g.MapPost("/", async (AdPulseDbContext db, CampaignRequest req, HttpContext http) =>
        {
            if (Problems.Validate(http, req) is { } bad) return bad;
            if (req.EndDate < req.StartDate) return Problems.Create(http, 422, "invalid_dates", "EndDate must be on or after StartDate.");
            if (await db.Campaigns.AnyAsync(c => c.Name == req.Name))
                return Problems.Create(http, 409, "duplicate_name", $"A campaign named '{req.Name}' already exists for this tenant.");

            var c = new Campaign { Id = Guid.NewGuid(), Name = req.Name.Trim(), Channel = req.Channel, DailyBudget = req.DailyBudget, StartDate = req.StartDate, EndDate = req.EndDate };
            db.Campaigns.Add(c);
            await db.SaveChangesAsync();
            return Results.Created($"/api/campaigns/{c.Id}", CampaignResponse.From(c));
        });

        g.MapPut("/{id:guid}", async (AdPulseDbContext db, Guid id, CampaignRequest req, HttpContext http) =>
        {
            if (Problems.Validate(http, req) is { } bad) return bad;
            if (req.EndDate < req.StartDate) return Problems.Create(http, 422, "invalid_dates", "EndDate must be on or after StartDate.");
            var c = await db.Campaigns.SingleOrDefaultAsync(x => x.Id == id);
            if (c is null) return Results.NotFound();
            if (c.Status == CampaignStatus.Completed) return Problems.Create(http, 409, "campaign_completed", "Completed campaigns are read-only.");
            if (await db.Campaigns.AnyAsync(x => x.Name == req.Name && x.Id != id))
                return Problems.Create(http, 409, "duplicate_name", $"A campaign named '{req.Name}' already exists for this tenant.");
            c.Name = req.Name.Trim(); c.Channel = req.Channel; c.DailyBudget = req.DailyBudget; c.StartDate = req.StartDate; c.EndDate = req.EndDate; c.UpdatedUtc = DateTime.UtcNow;
            await db.SaveChangesAsync();
            return Results.Ok(CampaignResponse.From(c));
        });

        g.MapDelete("/{id:guid}", async (AdPulseDbContext db, Guid id) =>
        {
            var c = await db.Campaigns.SingleOrDefaultAsync(x => x.Id == id);
            if (c is null) return Results.NotFound();
            db.Campaigns.Remove(c);
            await db.SaveChangesAsync();
            return Results.NoContent();
        });

        // Lifecycle: Draft -> Active <-> Paused -> Completed. Invalid moves return RFC 7807 problem+json.
        g.MapPost("/{id:guid}/{action:regex(^(activate|pause|complete)$)}", async (AdPulseDbContext db, Guid id, string action, HttpContext http) =>
        {
            var c = await db.Campaigns.SingleOrDefaultAsync(x => x.Id == id);
            if (c is null) return Results.NotFound();
            var today = DateOnly.FromDateTime(DateTime.UtcNow);
            if (CampaignLifecycle.Validate(c, action, today) is { } err)
                return Problems.Create(http, err.Status, err.Code, err.Detail, new Dictionary<string, object?> { ["currentStatus"] = c.Status.ToString(), ["action"] = action });
            c.Status = CampaignLifecycle.Actions[action].To; c.UpdatedUtc = DateTime.UtcNow;
            await db.SaveChangesAsync();
            return Results.Ok(CampaignResponse.From(c));
        }).WithSummary("Lifecycle transition: activate | pause | complete");

        g.MapPut("/{id:guid}/metrics", async (AdPulseDbContext db, Guid id, MetricsUpsertRequest req, HttpContext http) =>
        {
            if (Problems.Validate(http, req) is { } bad) return bad;
            foreach (var r in req.Rows) if (Problems.Validate(http, r) is { } badRow) return badRow;
            if (req.Rows.GroupBy(r => r.Date).Any(grp => grp.Count() > 1)) return Problems.Create(http, 422, "duplicate_dates", "Each date may appear only once per request.");
            var c = await db.Campaigns.SingleOrDefaultAsync(x => x.Id == id);
            if (c is null) return Results.NotFound();

            var dates = req.Rows.Select(r => r.Date).ToList();
            var existing = await db.DailyMetrics.Where(m => m.CampaignId == id && dates.Contains(m.Date)).ToDictionaryAsync(m => m.Date);
            int inserted = 0, updated = 0;
            foreach (var r in req.Rows)
            {
                if (existing.TryGetValue(r.Date, out var m))
                {
                    m.Impressions = r.Impressions; m.Clicks = r.Clicks; m.Conversions = r.Conversions; m.Spend = r.Spend; m.Revenue = r.Revenue; updated++;
                }
                else
                {
                    db.DailyMetrics.Add(new DailyMetric { CampaignId = id, Date = r.Date, Impressions = r.Impressions, Clicks = r.Clicks, Conversions = r.Conversions, Spend = r.Spend, Revenue = r.Revenue });
                    inserted++;
                }
            }
            await db.SaveChangesAsync();
            return Results.Ok(new MetricsUpsertResponse(inserted, updated));
        }).WithSummary("Upsert daily spend/metrics rows (idempotent per date)");

        g.MapGet("/{id:guid}/metrics", async (AdPulseDbContext db, Guid id, DateOnly? from, DateOnly? to) =>
        {
            if (!await db.Campaigns.AnyAsync(x => x.Id == id)) return Results.NotFound();
            var q = db.DailyMetrics.AsNoTracking().Where(m => m.CampaignId == id);
            if (from is not null) q = q.Where(m => m.Date >= from);
            if (to is not null) q = q.Where(m => m.Date <= to);
            var rows = await q.OrderBy(m => m.Date).Select(m => new MetricRow(m.Date, m.Impressions, m.Clicks, m.Conversions, m.Spend, m.Revenue)).ToListAsync();
            return Results.Ok(rows);
        });

        g.MapGet("/{id:guid}/kpis", async (AdPulseDbContext db, Guid id, DateOnly? from, DateOnly? to) =>
        {
            var c = await db.Campaigns.AsNoTracking().SingleOrDefaultAsync(x => x.Id == id);
            if (c is null) return Results.NotFound();
            var q = db.DailyMetrics.AsNoTracking().Where(m => m.CampaignId == id);
            if (from is not null) q = q.Where(m => m.Date >= from);
            if (to is not null) q = q.Where(m => m.Date <= to);
            var a = await q.GroupBy(_ => 1).Select(grp => new
            {
                Impressions = grp.Sum(m => m.Impressions), Clicks = grp.Sum(m => m.Clicks), Conversions = grp.Sum(m => m.Conversions),
                Spend = grp.Sum(m => (double)m.Spend), Revenue = grp.Sum(m => (double)m.Revenue)
            }).SingleOrDefaultAsync();
            return Results.Ok(Kpi.Row(c.Channel.ToString(), 1, a?.Impressions ?? 0, a?.Clicks ?? 0, a?.Conversions ?? 0, (decimal)(a?.Spend ?? 0), (decimal)(a?.Revenue ?? 0)));
        }).WithSummary("Server-side KPIs for one campaign (CTR, CPC, CPA, ROAS)");

        g.MapGet("/{id:guid}/pacing", async (AdPulseDbContext db, Guid id, DateOnly? asOf) =>
        {
            var c = await db.Campaigns.AsNoTracking().SingleOrDefaultAsync(x => x.Id == id);
            if (c is null) return Results.NotFound();
            var day = asOf ?? DateOnly.FromDateTime(DateTime.UtcNow);
            var spend = await db.DailyMetrics.Where(m => m.CampaignId == id && m.Date >= c.StartDate && m.Date <= day).SumAsync(m => (double)m.Spend);
            return Results.Ok(Pacing.Compute(c, (decimal)spend, day));
        }).WithSummary("Budget pacing: expected vs actual, ±10% band, corrected daily budget");

        return app;
    }
}
