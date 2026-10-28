using AdPulse.Api.Contracts;
using AdPulse.Api.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace AdPulse.Tests;

/// <summary>Runs the real seeder (500 synthetic + 2 demo tenants) and checks isolation holds at scale.</summary>
public class SeederTests : IClassFixture<SeededFactory>
{
    private readonly SeededFactory _f;
    public SeederTests(SeededFactory f) => _f = f;

    [Fact]
    public async Task Seeds_more_than_500_tenants_with_campaigns_and_metrics()
    {
        using var scope = _f.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AdPulseDbContext>();
        Assert.True(await db.Tenants.CountAsync() >= 502);
        Assert.True(await db.Campaigns.IgnoreQueryFilters().CountAsync() >= 1000);
        Assert.True(await db.DailyMetrics.IgnoreQueryFilters().CountAsync() >= 10_000);
        Assert.Equal(await db.Tenants.CountAsync(), await db.Tenants.Select(t => t.ApiKeyHash).Distinct().CountAsync());
    }

    [Fact]
    public async Task Seeding_is_idempotent()
    {
        using var scope = _f.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AdPulseDbContext>();
        var before = await db.Tenants.CountAsync();
        await DbSeeder.SeedAsync(db);
        Assert.Equal(before, await db.Tenants.CountAsync());
    }

    [Fact]
    public async Task Demo_tenant_sees_only_its_own_campaigns()
    {
        var acme = await (await _f.ClientFor(DbSeeder.AcmeKey).GetAsync("/api/v1/campaigns")).Json<List<CampaignResponse>>();
        var globex = await (await _f.ClientFor(DbSeeder.GlobexKey).GetAsync("/api/v1/campaigns")).Json<List<CampaignResponse>>();
        Assert.Equal(4, acme.Count);
        Assert.Equal(4, globex.Count);
        Assert.Empty(acme.Select(c => c.Id).Intersect(globex.Select(c => c.Id)));
        Assert.Contains(acme, c => c.Name == "Search - Brand");
        Assert.Contains(globex, c => c.Name == "Summer Escapes");
    }

    [Fact]
    public async Task Demo_pacing_covers_all_three_bands()
    {
        var rows = await (await _f.ClientFor(DbSeeder.AcmeKey).GetAsync("/api/v1/campaigns/summary")).Json<List<CampaignSummary>>();
        Assert.Contains(rows, r => r.PacingStatus == "over");
        Assert.Contains(rows, r => r.PacingStatus == "under");
        Assert.Contains(rows, r => r.PacingStatus == "on-track");
    }

    [Theory]
    [InlineData(1)]
    [InlineData(250)]
    [InlineData(500)]
    public async Task Synthetic_tenant_report_only_counts_its_own_campaigns(int n)
    {
        var client = _f.ClientFor(DbSeeder.SyntheticKey(n));
        var mine = await (await client.GetAsync("/api/v1/campaigns")).Json<List<CampaignResponse>>();
        Assert.InRange(mine.Count, 2, 4);
        var rep = await (await client.GetAsync("/api/v1/reports/cross-channel?from=2000-01-01&to=2100-01-01")).Json<CrossChannelReport>();
        Assert.True(rep.Total.Campaigns <= mine.Count);
    }
}
