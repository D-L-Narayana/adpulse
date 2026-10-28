using System.ComponentModel.DataAnnotations;
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
        });

        g.MapGet("/{id:guid}", async Task<Results<Ok<CampaignResponse>, NotFound>> (AdPulseDbContext db, Guid id) =>
        {
            var c = await db.Campaigns.AsNoTracking().SingleOrDefaultAsync(x => x.Id == id);
            return c is null ? TypedResults.NotFound() : TypedResults.Ok(CampaignResponse.From(c));
        });

        g.MapPost("/", async (AdPulseDbContext db, CampaignRequest req, HttpContext http) =>
        {
            if (Validate(req) is { } bad) return bad;
            if (req.EndDate < req.StartDate) return Err(http, 422, "invalid_dates", "EndDate must be on or after StartDate.");
            if (await db.Campaigns.AnyAsync(c => c.Name == req.Name))
                return Err(http, 409, "duplicate_name", $"A campaign named '{req.Name}' already exists for this tenant.");

            var c = new Campaign { Id = Guid.NewGuid(), Name = req.Name.Trim(), Channel = req.Channel, DailyBudget = req.DailyBudget, StartDate = req.StartDate, EndDate = req.EndDate };
            db.Campaigns.Add(c);
            await db.SaveChangesAsync();
            return Results.Created($"/api/campaigns/{c.Id}", CampaignResponse.From(c));
        });

        g.MapPut("/{id:guid}", async (AdPulseDbContext db, Guid id, CampaignRequest req, HttpContext http) =>
        {
            if (Validate(req) is { } bad) return bad;
            if (req.EndDate < req.StartDate) return Err(http, 422, "invalid_dates", "EndDate must be on or after StartDate.");
            var c = await db.Campaigns.SingleOrDefaultAsync(x => x.Id == id);
            if (c is null) return Results.NotFound();
            if (c.Status == CampaignStatus.Ended) return Err(http, 409, "campaign_ended", "Ended campaigns are read-only.");
            if (await db.Campaigns.AnyAsync(x => x.Name == req.Name && x.Id != id))
                return Err(http, 409, "duplicate_name", $"A campaign named '{req.Name}' already exists for this tenant.");
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

        g.MapPost("/{id:guid}/activate", (AdPulseDbContext db, Guid id, HttpContext http) => Transition(db, id, http, CampaignStatus.Active, CampaignStatus.Draft, CampaignStatus.Paused));
        g.MapPost("/{id:guid}/pause", (AdPulseDbContext db, Guid id, HttpContext http) => Transition(db, id, http, CampaignStatus.Paused, CampaignStatus.Active));
        g.MapPost("/{id:guid}/end", (AdPulseDbContext db, Guid id, HttpContext http) => Transition(db, id, http, CampaignStatus.Ended, CampaignStatus.Active, CampaignStatus.Paused));

        g.MapPut("/{id:guid}/metrics", async (AdPulseDbContext db, Guid id, MetricsUpsertRequest req, HttpContext http) =>
        {
            if (Validate(req) is { } bad) return bad;
            foreach (var r in req.Rows) if (Validate(r) is { } badRow) return badRow;
            if (req.Rows.GroupBy(r => r.Date).Any(grp => grp.Count() > 1)) return Err(http, 422, "duplicate_dates", "Each date may appear only once per request.");
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
        });

        g.MapGet("/{id:guid}/metrics", async (AdPulseDbContext db, Guid id, DateOnly? from, DateOnly? to) =>
        {
            if (!await db.Campaigns.AnyAsync(x => x.Id == id)) return Results.NotFound();
            var q = db.DailyMetrics.AsNoTracking().Where(m => m.CampaignId == id);
            if (from is not null) q = q.Where(m => m.Date >= from);
            if (to is not null) q = q.Where(m => m.Date <= to);
            var rows = await q.OrderBy(m => m.Date).Select(m => new MetricRow(m.Date, m.Impressions, m.Clicks, m.Conversions, m.Spend, m.Revenue)).ToListAsync();
            return Results.Ok(rows);
        });

        g.MapGet("/{id:guid}/pacing", async (AdPulseDbContext db, Guid id, DateOnly? asOf) =>
        {
            var c = await db.Campaigns.AsNoTracking().SingleOrDefaultAsync(x => x.Id == id);
            if (c is null) return Results.NotFound();
            var day = asOf ?? DateOnly.FromDateTime(DateTime.UtcNow);
            var spend = await db.DailyMetrics.Where(m => m.CampaignId == id && m.Date >= c.StartDate && m.Date <= day).SumAsync(m => (double)m.Spend);
            return Results.Ok(Pacing.Compute(c, (decimal)spend, day));
        });

        return app;
    }

    private static async Task<IResult> Transition(AdPulseDbContext db, Guid id, HttpContext http, CampaignStatus to, params CampaignStatus[] allowedFrom)
    {
        var c = await db.Campaigns.SingleOrDefaultAsync(x => x.Id == id);
        if (c is null) return Results.NotFound();
        if (!allowedFrom.Contains(c.Status))
            return Err(http, 409, "invalid_transition", $"Cannot move a {c.Status} campaign to {to}.");
        c.Status = to; c.UpdatedUtc = DateTime.UtcNow;
        await db.SaveChangesAsync();
        return Results.Ok(CampaignResponse.From(c));
    }

    internal static IResult? Validate(object model)
    {
        var results = new List<ValidationResult>();
        if (Validator.TryValidateObject(model, new ValidationContext(model), results, validateAllProperties: true)) return null;
        var errors = results.GroupBy(r => r.MemberNames.FirstOrDefault() ?? "").ToDictionary(grp => grp.Key, grp => grp.Select(r => r.ErrorMessage ?? "invalid").ToArray());
        return Results.ValidationProblem(errors, statusCode: 422, type: "validation_error");
    }

    internal static IResult Err(HttpContext http, int status, string type, string detail) =>
        Results.Json(new ApiError(type, status == 404 ? "Not Found" : status == 409 ? "Conflict" : "Unprocessable Entity", status, detail, http.TraceIdentifier), statusCode: status);
}
