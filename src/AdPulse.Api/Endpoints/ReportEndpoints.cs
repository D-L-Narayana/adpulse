using AdPulse.Api.Contracts;
using AdPulse.Api.Data;
using AdPulse.Api.Domain;
using AdPulse.Api.Services;
using Microsoft.EntityFrameworkCore;

namespace AdPulse.Api.Endpoints;

public static class ReportEndpoints
{
    public static IEndpointRouteBuilder MapReports(this IEndpointRouteBuilder app)
    {
        var g = app.MapGroup("/api/v1/reports").WithTags("Reports");

        // Unified cross-channel view: one row per channel + a total row, aggregated in SQL.
        g.MapGet("/cross-channel", async (AdPulseDbContext db, DateOnly? from, DateOnly? to, HttpContext http) =>
        {
            var end = to ?? DateOnly.FromDateTime(DateTime.UtcNow);
            var start = from ?? end.AddDays(-29);
            if (end < start) return Problems.Create(http, 422, "invalid_range", "'to' must be on or after 'from'.");

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
        }).WithSummary("Per-channel KPI report (CTR, CPC, CPA, ROAS) + total row");

        // Daily trend for the dashboard chart.
        g.MapGet("/daily", async (AdPulseDbContext db, DateOnly? from, DateOnly? to, HttpContext http) =>
        {
            var end = to ?? DateOnly.FromDateTime(DateTime.UtcNow);
            var start = from ?? end.AddDays(-29);
            if (end < start) return Problems.Create(http, 422, "invalid_range", "'to' must be on or after 'from'.");
            var rows = await db.DailyMetrics.AsNoTracking()
                .Where(m => m.Date >= start && m.Date <= end)
                .GroupBy(m => m.Date)
                .Select(grp => new { Date = grp.Key, Spend = grp.Sum(m => (double)m.Spend), Revenue = grp.Sum(m => (double)m.Revenue), Clicks = grp.Sum(m => m.Clicks), Conversions = grp.Sum(m => m.Conversions) })
                .OrderBy(x => x.Date)
                .ToListAsync();
            return Results.Ok(rows.Select(x => new DailyTrendRow(x.Date, Math.Round((decimal)x.Spend, 2), Math.Round((decimal)x.Revenue, 2), x.Clicks, x.Conversions)));
        }).WithSummary("Daily spend/revenue trend across all channels");

        // Headline block for the dashboard: window vs previous window, lifetime, pacing distribution.
        g.MapGet("/summary", async (AdPulseDbContext db, DateOnly? from, DateOnly? to, HttpContext http) =>
        {
            var end = to ?? DateOnly.FromDateTime(DateTime.UtcNow);
            var start = from ?? end.AddDays(-29);
            if (end < start) return Problems.Create(http, 422, "invalid_range", "'to' must be on or after 'from'.");
            var len = end.DayNumber - start.DayNumber + 1;
            var prevStart = start.AddDays(-len); var prevEnd = start.AddDays(-1);

            async Task<KpiRow> Block(string label, DateOnly? s, DateOnly? e)
            {
                var q = db.DailyMetrics.AsNoTracking();
                if (s is not null) q = q.Where(m => m.Date >= s);
                if (e is not null) q = q.Where(m => m.Date <= e);
                var a = await q.GroupBy(_ => 1).Select(grp => new
                {
                    Campaigns = grp.Select(m => m.CampaignId).Distinct().Count(),
                    Impressions = grp.Sum(m => m.Impressions), Clicks = grp.Sum(m => m.Clicks), Conversions = grp.Sum(m => m.Conversions),
                    Spend = grp.Sum(m => (double)m.Spend), Revenue = grp.Sum(m => (double)m.Revenue)
                }).SingleOrDefaultAsync();
                return Kpi.Row(label, a?.Campaigns ?? 0, a?.Impressions ?? 0, a?.Clicks ?? 0, a?.Conversions ?? 0, (decimal)(a?.Spend ?? 0), (decimal)(a?.Revenue ?? 0));
            }

            var window = await Block("window", start, end);
            var previous = await Block("previous", prevStart, prevEnd);
            var lifetime = await Block("lifetime", null, null);

            var campaigns = await db.Campaigns.AsNoTracking().ToListAsync();
            var active = campaigns.Where(c => c.Status == CampaignStatus.Active).ToList();
            var ids = active.Select(c => c.Id).ToList();
            var spendToDate = await db.DailyMetrics.AsNoTracking().Where(m => ids.Contains(m.CampaignId) && m.Date <= end)
                .GroupBy(m => m.CampaignId).Select(g => new { g.Key, Spend = g.Sum(m => (double)m.Spend) }).ToDictionaryAsync(x => x.Key, x => (decimal)x.Spend);
            int over = 0, under = 0, onTrack = 0;
            foreach (var c in active)
            {
                var st = Pacing.Compute(c, spendToDate.GetValueOrDefault(c.Id), end).Status;
                if (st == PacingStatus.Over) over++; else if (st == PacingStatus.Under) under++; else if (st == PacingStatus.OnTrack) onTrack++;
            }
            var lastDate = await db.DailyMetrics.AsNoTracking().OrderByDescending(m => m.Date).Select(m => (DateOnly?)m.Date).FirstOrDefaultAsync();
            return Results.Ok(new SummaryReport(start, end, window, previous, lifetime, active.Count, campaigns.Count, over, under, onTrack, lastDate));
        }).WithSummary("Dashboard headline: window KPIs vs previous window, lifetime totals, pacing distribution");

        return app;
    }
}
