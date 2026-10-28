using System.Net;
using AdPulse.Api.Contracts;
using AdPulse.Api.Data;
using AdPulse.Api.Domain;
using AdPulse.Api.Tenancy;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace AdPulse.Tests;

/// <summary>
/// 40 tenant-isolation checks = 20 attack probes x 2 attacker profiles.
///
/// Setup per case: a *victim* tenant owns an Active campaign with metrics. An *attacker* tenant (with its own valid
/// API key) tries to observe or mutate it through every endpoint. Attacker profiles:
///   Empty  - attacker has no data of its own (a leak would be the only row it sees)
///   Decoy  - attacker owns a campaign with the *same name and channel* (catches filters keyed on name instead of TenantId)
/// Every probe must come back 404 / empty / unchanged, and the victim's data must remain intact afterwards.
/// </summary>
public class TenantIsolationTests : IClassFixture<AdPulseFactory>
{
    private readonly AdPulseFactory _f;
    public TenantIsolationTests(AdPulseFactory f) => _f = f;

    public enum Attacker { Empty, Decoy }

    private sealed record Scene(HttpClient Victim, HttpClient Attacker, Tenant VictimTenant, Tenant AttackerTenant, CampaignResponse Campaign);

    private async Task<Scene> Arrange(Attacker profile)
    {
        var (v, vt) = await _f.NewTenantClientAsync();
        var (a, at) = await _f.NewTenantClientAsync();
        var campaign = await v.CreateCampaign(Flight.Request("Victim Campaign", Channel.Meta, 100m));
        await v.Transition(campaign.Id, "activate");
        await v.PutJson($"/api/v1/campaigns/{campaign.Id}/metrics", new MetricsUpsertRequest([
            new(Flight.Day(1), 1000, 100, 10, 100m, 400m), new(Flight.Day(2), 1000, 100, 10, 100m, 400m)]));
        if (profile == Attacker.Decoy)
        {
            var decoy = await a.CreateCampaign(Flight.Request("Victim Campaign", Channel.Meta, 100m));
            await a.Transition(decoy.Id, "activate");
        }
        return new Scene(v, a, vt, at, await (await v.GetAsync($"/api/v1/campaigns/{campaign.Id}")).Json<CampaignResponse>());
    }

    // ---- probe catalogue ---------------------------------------------------------------------------------------
    private static readonly Dictionary<string, Func<Scene, Task>> Probes = new()
    {
        ["GET /campaigns excludes foreign id"] = async s =>
        {
            var list = await (await s.Attacker.GetAsync("/api/v1/campaigns")).Json<List<CampaignResponse>>();
            Assert.DoesNotContain(list, x => x.Id == s.Campaign.Id);
        },
        ["GET /campaigns?status=Active excludes foreign id"] = async s =>
        {
            var list = await (await s.Attacker.GetAsync("/api/v1/campaigns?status=Active")).Json<List<CampaignResponse>>();
            Assert.DoesNotContain(list, x => x.Id == s.Campaign.Id);
        },
        ["GET /campaigns?channel=Meta excludes foreign id"] = async s =>
        {
            var list = await (await s.Attacker.GetAsync("/api/v1/campaigns?channel=Meta")).Json<List<CampaignResponse>>();
            Assert.DoesNotContain(list, x => x.Id == s.Campaign.Id);
        },
        ["GET /campaigns/summary excludes foreign id"] = async s =>
        {
            var list = await (await s.Attacker.GetAsync("/api/v1/campaigns/summary")).Json<List<CampaignSummary>>();
            Assert.DoesNotContain(list, x => x.Id == s.Campaign.Id);
        },
        ["GET /campaigns/{id} is 404"] = async s =>
            Assert.Equal(HttpStatusCode.NotFound, (await s.Attacker.GetAsync($"/api/v1/campaigns/{s.Campaign.Id}")).StatusCode),
        ["PUT /campaigns/{id} is 404"] = async s =>
            Assert.Equal(HttpStatusCode.NotFound, (await s.Attacker.PutJson($"/api/v1/campaigns/{s.Campaign.Id}", Flight.Request("Hijacked"))).StatusCode),
        ["DELETE /campaigns/{id} is 404"] = async s =>
            Assert.Equal(HttpStatusCode.NotFound, (await s.Attacker.DeleteAsync($"/api/v1/campaigns/{s.Campaign.Id}")).StatusCode),
        ["POST /activate is 404"] = async s =>
            Assert.Equal(HttpStatusCode.NotFound, (await s.Attacker.PostAsync($"/api/v1/campaigns/{s.Campaign.Id}/activate", null)).StatusCode),
        ["POST /pause is 404"] = async s =>
            Assert.Equal(HttpStatusCode.NotFound, (await s.Attacker.PostAsync($"/api/v1/campaigns/{s.Campaign.Id}/pause", null)).StatusCode),
        ["POST /complete is 404"] = async s =>
            Assert.Equal(HttpStatusCode.NotFound, (await s.Attacker.PostAsync($"/api/v1/campaigns/{s.Campaign.Id}/complete", null)).StatusCode),
        ["PUT /metrics is 404 and writes nothing"] = async s =>
        {
            var r = await s.Attacker.PutJson($"/api/v1/campaigns/{s.Campaign.Id}/metrics", new MetricsUpsertRequest([new(Flight.Day(9), 1, 1, 1, 9999m, 0)]));
            Assert.Equal(HttpStatusCode.NotFound, r.StatusCode);
            var rows = await (await s.Victim.GetAsync($"/api/v1/campaigns/{s.Campaign.Id}/metrics")).Json<List<MetricRow>>();
            Assert.Equal(2, rows.Count);
        },
        ["GET /metrics is 404"] = async s =>
            Assert.Equal(HttpStatusCode.NotFound, (await s.Attacker.GetAsync($"/api/v1/campaigns/{s.Campaign.Id}/metrics")).StatusCode),
        ["GET /kpis is 404"] = async s =>
            Assert.Equal(HttpStatusCode.NotFound, (await s.Attacker.GetAsync($"/api/v1/campaigns/{s.Campaign.Id}/kpis")).StatusCode),
        ["GET /pacing is 404"] = async s =>
            Assert.Equal(HttpStatusCode.NotFound, (await s.Attacker.GetAsync($"/api/v1/campaigns/{s.Campaign.Id}/pacing")).StatusCode),
        ["cross-channel report excludes foreign spend"] = async s =>
        {
            var rep = await (await s.Attacker.GetAsync($"/api/v1/reports/cross-channel?from={Flight.Start:yyyy-MM-dd}&to={Flight.End:yyyy-MM-dd}")).Json<CrossChannelReport>();
            Assert.Equal(0m, rep.Total.Spend);
            Assert.Equal(0, rep.Total.Impressions);
        },
        ["daily report excludes foreign spend"] = async s =>
        {
            var rows = await (await s.Attacker.GetAsync($"/api/v1/reports/daily?from={Flight.Start:yyyy-MM-dd}&to={Flight.End:yyyy-MM-dd}")).Json<List<DailyTrendRow>>();
            Assert.Empty(rows);
        },
        ["/me resolves to the attacker, never the victim"] = async s =>
        {
            var me = await (await s.Attacker.GetAsync("/api/v1/tenants/me")).Json<TenantMeResponse>();
            Assert.Equal(s.AttackerTenant.Id, me.Id);
            Assert.NotEqual(s.VictimTenant.Id, me.Id);
        },
        ["same-name create does not conflict across tenants"] = async s =>
        {
            var r = await s.Attacker.PostJson("/api/v1/campaigns", Flight.Request("Victim Campaign Twin", Channel.Meta, 100m));
            Assert.Equal(HttpStatusCode.Created, r.StatusCode);
            var created = await r.Json<CampaignResponse>();
            Assert.NotEqual(s.Campaign.Id, created.Id);
            // and the victim still can't see the attacker's new campaign
            Assert.Equal(HttpStatusCode.NotFound, (await s.Victim.GetAsync($"/api/v1/campaigns/{created.Id}")).StatusCode);
        },
        ["victim data intact after hostile probes"] = async s =>
        {
            await s.Attacker.PutJson($"/api/v1/campaigns/{s.Campaign.Id}", Flight.Request("Hijacked"));
            await s.Attacker.PostAsync($"/api/v1/campaigns/{s.Campaign.Id}/complete", null);
            await s.Attacker.DeleteAsync($"/api/v1/campaigns/{s.Campaign.Id}");
            var after = await (await s.Victim.GetAsync($"/api/v1/campaigns/{s.Campaign.Id}")).Json<CampaignResponse>();
            Assert.Equal(s.Campaign, after);
        },
        ["EF global query filter hides foreign rows at the DbContext level"] = s => Task.CompletedTask, // handled specially below
    };

    public static IEnumerable<object[]> Cases() =>
        from profile in new[] { Attacker.Empty, Attacker.Decoy }
        from probe in Probes.Keys
        select new object[] { profile, probe };

    [Theory]
    [MemberData(nameof(Cases))]
    public async Task Attacker_cannot_cross_tenant_boundary(Attacker profile, string probe)
    {
        var s = await Arrange(profile);
        if (probe.StartsWith("EF global"))
        {
            await DbContextLevelCheck(s);
            return;
        }
        await Probes[probe](s);
    }

    /// <summary>Bypasses HTTP: a DbContext whose TenantContext is the attacker sees 0 victim rows, while IgnoreQueryFilters proves they exist.</summary>
    private async Task DbContextLevelCheck(Scene s)
    {
        using var scope = _f.Services.CreateScope();
        scope.ServiceProvider.GetRequiredService<TenantContext>().Set(s.AttackerTenant);
        var db = scope.ServiceProvider.GetRequiredService<AdPulseDbContext>();

        Assert.Equal(0, await db.Campaigns.CountAsync(c => c.Id == s.Campaign.Id));
        Assert.Equal(0, await db.DailyMetrics.CountAsync(m => m.CampaignId == s.Campaign.Id));
        Assert.Equal(1, await db.Campaigns.IgnoreQueryFilters().CountAsync(c => c.Id == s.Campaign.Id));
        Assert.Equal(2, await db.DailyMetrics.IgnoreQueryFilters().CountAsync(m => m.CampaignId == s.Campaign.Id));

        // SaveChanges guard: a tracked foreign entity cannot be modified under another tenant's context
        var foreign = await db.Campaigns.IgnoreQueryFilters().SingleAsync(c => c.Id == s.Campaign.Id);
        foreign.Name = "Hijacked";
        await Assert.ThrowsAsync<InvalidOperationException>(() => db.SaveChangesAsync());
    }
}
