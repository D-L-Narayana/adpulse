using AdPulse.Api.Contracts;
using AdPulse.Api.Data;
using AdPulse.Api.Services;
using Microsoft.EntityFrameworkCore;

namespace AdPulse.Api.Endpoints;

public static class ReportEndpoints
{
    public static IEndpointRouteBuilder MapReports(this IEndpointRouteBuilder app)
    {
        var g = app.MapGroup("/api/reports").WithTags("Reports");

        // Unified cross-channel view: one row per channel + a total row, aggregated in SQL.
        g.MapGet("/cross-channel", async (AdPulseDbContext db, DateOnly? from, DateOnly? to, HttpContext http) =>
        {
            var end = to ?? DateOnly.FromDateTime(DateTime.UtcNow);
            var start = from ?? end.AddDays(-29);
            if (end < start) return CampaignEndpoints.Err(http, 422, "invalid_range", "'to' must be on or after 'from'.");

            var grouped = await db.DailyMetrics.AsNoTracking()
                .Where(m => m.Date >= start && m.Date <= end)
                .Join(db.Campaigns.AsNoTracking(), m => m.CampaignId, c => c.Id, (m, c) => new { c.Channel, m.CampaignId, m.Impressions, m.Clicks, m.Conversions, m.Spend, m.Revenue })
                .GroupBy(x => x.Channel)
                .Select(grp => new
                {
                    Channel = grp.Key,
                    Campaigns = grp.Select(x => x.CampaignId).Distinct().Count(),
                    Impressions = grp.Sum(x => x.Impressions),
                    Clicks = grp.Sum(x => x.Clicks),
                    Conversions = grp.Sum(x => x.Conversions),
                    Spend = grp.Sum(x => (double)x.Spend),
                    Revenue = grp.Sum(x => (double)x.Revenue)
                })
                .ToListAsync();

            var rows = grouped.OrderByDescending(x => x.Spend)
                .Select(x => Kpi.Row(x.Channel.ToString(), x.Campaigns, x.Impressions, x.Clicks, x.Conversions, (decimal)x.Spend, (decimal)x.Revenue)).ToList();
            var total = Kpi.Row("Total", rows.Sum(r => r.Campaigns), rows.Sum(r => r.Impressions), rows.Sum(r => r.Clicks), rows.Sum(r => r.Conversions), rows.Sum(r => r.Spend), rows.Sum(r => r.Revenue));
            return Results.Ok(new CrossChannelReport(start, end, rows, total));
        });

        // Daily trend for the dashboard chart.
        g.MapGet("/daily", async (AdPulseDbContext db, DateOnly? from, DateOnly? to) =>
        {
            var end = to ?? DateOnly.FromDateTime(DateTime.UtcNow);
            var start = from ?? end.AddDays(-29);
            var rows = await db.DailyMetrics.AsNoTracking()
                .Where(m => m.Date >= start && m.Date <= end)
                .GroupBy(m => m.Date)
                .Select(grp => new { Date = grp.Key, Spend = grp.Sum(m => (double)m.Spend), Revenue = grp.Sum(m => (double)m.Revenue), Clicks = grp.Sum(m => m.Clicks), Conversions = grp.Sum(m => m.Conversions) })
                .OrderBy(x => x.Date)
                .ToListAsync();
            return Results.Ok(rows.Select(x => new { x.Date, Spend = Math.Round((decimal)x.Spend, 2), Revenue = Math.Round((decimal)x.Revenue, 2), x.Clicks, x.Conversions }));
        });

        return app;
    }
}
