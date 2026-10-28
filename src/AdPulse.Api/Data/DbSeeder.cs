using AdPulse.Api.Domain;
using Microsoft.EntityFrameworkCore;

namespace AdPulse.Api.Data;

/// <summary>
/// Seeds a realistic multi-tenant data set:
///   * 2 named demo tenants (Acme Retail / Globex Travel) with 30 days of metrics – what the dashboard shows.
///   * N synthetic tenants (default 500) each with 2–4 campaigns and 14 days of metrics – used to exercise
///     tenant isolation and query-filter performance at a believable scale (~1.5k campaigns, ~21k metric rows).
/// Deterministic (seeded RNG) so benchmarks and screenshots are reproducible.
/// </summary>
public static class DbSeeder
{
    public const string AcmeKey = "acme-demo-key";
    public const string GlobexKey = "globex-demo-key";
    public const int DefaultTenantCount = 500;

    /// <summary>API key of synthetic tenant #n (1-based): <c>tenant-0001-key</c> …</summary>
    public static string SyntheticKey(int n) => $"tenant-{n:0000}-key";

    private static readonly string[] Industries = ["Retail", "Travel", "Fintech", "Gaming", "Health", "Auto", "Education", "Food", "Fashion", "SaaS"];
    private static readonly string[] Objectives = ["Brand Search", "Prospecting", "Retargeting", "App Installs", "Lead Gen", "Awareness", "Catalog Sales", "Seasonal Push"];

    public static async Task SeedAsync(AdPulseDbContext db, int syntheticTenantCount = DefaultTenantCount)
    {
        if (await db.Tenants.AnyAsync()) return;

        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        var rng = new Random(42);
        var prevDetect = db.ChangeTracker.AutoDetectChangesEnabled;
        db.ChangeTracker.AutoDetectChangesEnabled = false;
        try
        {
            // ---- Demo tenants -------------------------------------------------------------------------
            var acme = new Tenant { Id = Guid.NewGuid(), Name = "Acme Retail", ApiKey = AcmeKey };
            var globex = new Tenant { Id = Guid.NewGuid(), Name = "Globex Travel", ApiKey = GlobexKey };
            db.Tenants.AddRange(acme, globex);

            // pace: multiplier on daily spend so the demo shows all three pacing bands
            db.Campaigns.Add(Build(rng, acme, "Search - Brand", Channel.GoogleAds, 400m, today, 30, 0.045, 0.06, 0.9, 62, pace: 1.0));
            db.Campaigns.Add(Build(rng, acme, "Prospecting - Lookalike", Channel.Meta, 650m, today, 30, 0.012, 0.018, 0.55, 58, pace: 1.22));
            db.Campaigns.Add(Build(rng, acme, "Sponsored Products", Channel.Amazon, 300m, today, 30, 0.004, 0.11, 0.7, 41, pace: 0.78));
            db.Campaigns.Add(Build(rng, acme, "Holiday Teaser", Channel.Meta, 500m, today, 30, 0.010, 0.015, 0.6, 55, pace: 0, status: CampaignStatus.Draft, startOffset: 7));
            db.Campaigns.Add(Build(rng, globex, "Summer Escapes", Channel.Meta, 900m, today, 30, 0.015, 0.009, 0.48, 310, pace: 0.95));
            db.Campaigns.Add(Build(rng, globex, "B2B Corporate Travel", Channel.LinkedIn, 250m, today, 30, 0.006, 0.02, 4.2, 1250, pace: 1.15));
            db.Campaigns.Add(Build(rng, globex, "Programmatic Awareness", Channel.DV360, 500m, today, 30, 0.002, 0.003, 0.3, 280, pace: 0.6, status: CampaignStatus.Paused));
            db.Campaigns.Add(Build(rng, globex, "Spring Flash Sale", Channel.GoogleAds, 350m, today, 30, 0.04, 0.05, 0.8, 190, pace: 1.0, status: CampaignStatus.Completed, startOffset: -60));

            // ---- Synthetic tenants ---------------------------------------------------------------------
            var channels = Enum.GetValues<Channel>();
            for (var n = 1; n <= syntheticTenantCount; n++)
            {
                var t = new Tenant { Id = Guid.NewGuid(), Name = $"{Industries[rng.Next(Industries.Length)]} Co {n:0000}", ApiKey = SyntheticKey(n) };
                db.Tenants.Add(t);
                var campaignCount = 2 + rng.Next(3);
                for (var k = 0; k < campaignCount; k++)
                {
                    var ch = channels[rng.Next(channels.Length)];
                    var name = $"{Objectives[rng.Next(Objectives.Length)]} {k + 1}";
                    var budget = 50m + rng.Next(20) * 25m;
                    var status = rng.Next(10) switch { < 7 => CampaignStatus.Active, 7 => CampaignStatus.Paused, 8 => CampaignStatus.Draft, _ => CampaignStatus.Completed };
                    var pace = status == CampaignStatus.Draft ? 0 : 0.6 + rng.NextDouble() * 0.8;
                    db.Campaigns.Add(Build(rng, t, name, ch, budget, today, 14, 0.005 + rng.NextDouble() * 0.04, 0.005 + rng.NextDouble() * 0.08, 0.3 + rng.NextDouble() * 2, 30 + rng.Next(300), pace, status));
                }
                if (n % 100 == 0) await db.SaveChangesAsync();
            }
            await db.SaveChangesAsync();
        }
        finally
        {
            db.ChangeTracker.AutoDetectChangesEnabled = prevDetect;
            db.ChangeTracker.Clear();
        }
    }

    private static Campaign Build(Random rng, Tenant t, string name, Channel ch, decimal budget, DateOnly today, int historyDays,
        double ctr, double cvr, double cpc, double aov, double pace, CampaignStatus status = CampaignStatus.Active, int startOffset = 0)
    {
        var start = today.AddDays(-(historyDays - 1) + startOffset);
        var c = new Campaign
        {
            Id = Guid.NewGuid(), TenantId = t.Id, Name = name, Channel = ch, Status = status,
            DailyBudget = budget, StartDate = start, EndDate = start.AddDays(historyDays * 2 - 1)
        };
        if (pace <= 0) return c; // drafts have no spend yet
        for (var d = start; d <= today && d <= c.EndDate; d = d.AddDays(1))
        {
            var spend = (double)budget * pace * (0.85 + rng.NextDouble() * 0.3);
            var clicks = (long)(spend / cpc);
            var impressions = (long)(clicks / ctr);
            var conv = (long)(clicks * cvr);
            c.Metrics.Add(new DailyMetric
            {
                TenantId = t.Id, Date = d, Impressions = impressions, Clicks = clicks, Conversions = conv,
                Spend = Math.Round((decimal)spend, 2), Revenue = Math.Round((decimal)(conv * aov), 2)
            });
        }
        return c;
    }
}
