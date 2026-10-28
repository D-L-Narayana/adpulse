using AdPulse.Api.Domain;
using Microsoft.EntityFrameworkCore;

namespace AdPulse.Api.Data;

/// <summary>Creates two demo tenants with 30 days of metrics so the dashboard has something to show.</summary>
public static class DbSeeder
{
    public const string AcmeKey = "acme-demo-key";
    public const string GlobexKey = "globex-demo-key";

    public static async Task SeedAsync(AdPulseDbContext db)
    {
        if (await db.Tenants.AnyAsync()) return;

        var acme = new Tenant { Id = Guid.NewGuid(), Name = "Acme Retail", ApiKey = AcmeKey };
        var globex = new Tenant { Id = Guid.NewGuid(), Name = "Globex Travel", ApiKey = GlobexKey };
        db.Tenants.AddRange(acme, globex);

        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        var rng = new Random(42);
        void Add(Tenant t, string name, Channel ch, decimal budget, double ctr, double cvr, double cpc, double aov)
        {
            var c = new Campaign
            {
                Id = Guid.NewGuid(), TenantId = t.Id, Name = name, Channel = ch, Status = CampaignStatus.Active,
                DailyBudget = budget, StartDate = today.AddDays(-29), EndDate = today.AddDays(30)
            };
            for (var i = 29; i >= 0; i--)
            {
                var spend = (double)budget * (0.75 + rng.NextDouble() * 0.35);
                var clicks = (long)(spend / cpc);
                var impressions = (long)(clicks / ctr);
                var conv = (long)(clicks * cvr);
                c.Metrics.Add(new DailyMetric
                {
                    TenantId = t.Id, Date = today.AddDays(-i), Impressions = impressions, Clicks = clicks,
                    Conversions = conv, Spend = Math.Round((decimal)spend, 2), Revenue = Math.Round((decimal)(conv * aov), 2)
                });
            }
            db.Campaigns.Add(c);
        }

        Add(acme, "Search - Brand", Channel.GoogleAds, 400m, 0.045, 0.06, 0.9, 62);
        Add(acme, "Prospecting - Lookalike", Channel.Meta, 650m, 0.012, 0.018, 0.55, 58);
        Add(acme, "Sponsored Products", Channel.Amazon, 300m, 0.004, 0.11, 0.7, 41);
        Add(globex, "Summer Escapes", Channel.Meta, 900m, 0.015, 0.009, 0.48, 310);
        Add(globex, "B2B Corporate Travel", Channel.LinkedIn, 250m, 0.006, 0.02, 4.2, 1250);
        Add(globex, "Programmatic Awareness", Channel.DV360, 500m, 0.002, 0.003, 0.3, 280);

        await db.SaveChangesAsync();
    }
}
