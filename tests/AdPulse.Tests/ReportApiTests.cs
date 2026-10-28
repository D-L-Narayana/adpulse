using System.Net;
using AdPulse.Api.Contracts;
using AdPulse.Api.Domain;

namespace AdPulse.Tests;

public class ReportApiTests : IClassFixture<AdPulseFactory>
{
    private readonly AdPulseFactory _f;
    public ReportApiTests(AdPulseFactory f) => _f = f;

    private static string D(int day) => Flight.Day(day).ToString("yyyy-MM-dd");

    private static async Task<Guid> Seed(HttpClient c, string name, Channel ch, params (int day, long imp, long clk, long conv, decimal spend, decimal rev)[] rows)
    {
        var id = (await c.CreateCampaign(Flight.Request(name, ch, 100m))).Id;
        var r = await c.PutJson($"/api/campaigns/{id}/metrics", new MetricsUpsertRequest(rows.Select(r => new MetricRow(Flight.Day(r.day), r.imp, r.clk, r.conv, r.spend, r.rev)).ToList()));
        Assert.Equal(HttpStatusCode.OK, r.StatusCode);
        return id;
    }

    [Fact]
    public async Task Cross_channel_report_aggregates_per_channel_and_total_within_tenant_only()
    {
        var (c, _) = await _f.NewTenantClientAsync();
        var (other, _) = await _f.NewTenantClientAsync();
        await Seed(c, "G1", Channel.GoogleAds, (1, 1000, 100, 10, 50m, 500m), (2, 1000, 100, 10, 50m, 500m));
        await Seed(c, "G2", Channel.GoogleAds, (1, 2000, 100, 0, 100m, 0m));
        await Seed(c, "M1", Channel.Meta, (1, 10000, 200, 4, 80m, 160m));
        await Seed(other, "Noise", Channel.Meta, (1, 999999, 99999, 9999, 99999m, 99999m));

        var rep = await (await c.GetAsync($"/api/reports/cross-channel?from={D(1)}&to={D(30)}")).Json<CrossChannelReport>();
        Assert.Equal(2, rep.Channels.Count);
        var g = rep.Channels.Single(r => r.Channel == "GoogleAds");
        Assert.Equal(2, g.Campaigns); Assert.Equal(4000, g.Impressions); Assert.Equal(300, g.Clicks); Assert.Equal(20, g.Conversions);
        Assert.Equal(200m, g.Spend); Assert.Equal(1000m, g.Revenue); Assert.Equal(7.5m, g.Ctr); Assert.Equal(0.67m, g.Cpc); Assert.Equal(10m, g.Cpa); Assert.Equal(5m, g.Roas);
        Assert.Equal("GoogleAds", rep.Channels[0].Channel); // ordered by spend desc
        Assert.Equal(280m, rep.Total.Spend); Assert.Equal(3, rep.Total.Campaigns); Assert.Equal(14000, rep.Total.Impressions);
    }

    [Fact]
    public async Task Date_range_filters_rows()
    {
        var (c, _) = await _f.NewTenantClientAsync();
        await Seed(c, "Range", Channel.Amazon, (1, 10, 1, 1, 10m, 10m), (5, 10, 1, 1, 20m, 10m), (9, 10, 1, 1, 30m, 10m));
        var rep = await (await c.GetAsync($"/api/reports/cross-channel?from={D(3)}&to={D(7)}")).Json<CrossChannelReport>();
        Assert.Equal(20m, rep.Total.Spend);
    }

    [Fact]
    public async Task Default_range_is_last_30_days()
    {
        var (c, _) = await _f.NewTenantClientAsync();
        await Seed(c, "Recent", Channel.Amazon, (1, 10, 1, 1, 10m, 10m));
        var rep = await (await c.GetAsync("/api/reports/cross-channel")).Json<CrossChannelReport>();
        Assert.Equal(Flight.Today, rep.To);
        Assert.Equal(Flight.Today.AddDays(-29), rep.From);
        Assert.Equal(10m, rep.Total.Spend);
    }

    [Fact]
    public async Task Inverted_range_is_422_problem()
    {
        var (c, _) = await _f.NewTenantClientAsync();
        var r = await c.GetAsync($"/api/reports/cross-channel?from={D(9)}&to={D(1)}");
        Assert.Equal(HttpStatusCode.UnprocessableEntity, r.StatusCode);
        Assert.Equal("invalid_range", (await r.Problem()).Code);
    }

    [Fact]
    public async Task Empty_tenant_gets_empty_report_not_error()
    {
        var (c, _) = await _f.NewTenantClientAsync();
        var rep = await (await c.GetAsync("/api/reports/cross-channel")).Json<CrossChannelReport>();
        Assert.Empty(rep.Channels); Assert.Equal(0m, rep.Total.Spend); Assert.Equal(0m, rep.Total.Roas);
    }

    [Fact]
    public async Task Daily_trend_groups_by_date()
    {
        var (c, _) = await _f.NewTenantClientAsync();
        await Seed(c, "D1", Channel.Meta, (1, 10, 1, 1, 10m, 1m), (2, 10, 1, 1, 20m, 1m));
        await Seed(c, "D2", Channel.Meta, (1, 10, 1, 1, 5m, 1m));
        var rows = await (await c.GetAsync($"/api/reports/daily?from={D(1)}&to={D(2)}")).Json<List<DailyTrendRow>>();
        Assert.Equal(2, rows.Count);
        Assert.Equal(15m, rows[0].Spend); Assert.Equal(Flight.Day(1), rows[0].Date);
        Assert.Equal(20m, rows[1].Spend);
    }
}
