using System.Globalization;
using System.Net;
using System.Net.Http.Json;
using AdPulse.Api.Contracts;
using AdPulse.Specs.Support;
using Reqnroll;
using Xunit;

namespace AdPulse.Specs.Steps;

[Binding]
public sealed class PacingAndReportSteps(ApiDriver api)
{
    private static readonly DateOnly Today = DateOnly.FromDateTime(DateTime.UtcNow);

    // ---- spend ingestion ------------------------------------------------------------------------
    [Given(@"^the campaign has spent ([\d.]+) by (\d{4}-\d{2}-\d{2})$")]
    public Task GivenSpentBy(decimal total, string date) => SpendSingleRow(total, DateOnly.Parse(date, CultureInfo.InvariantCulture));

    [Given(@"^the campaign has spent ([\d.]+) by today$")]
    public Task GivenSpentByToday(decimal total) => SpendSingleRow(total, Today);

    private async Task SpendSingleRow(decimal total, DateOnly date)
    {
        // One row dated on the as-of day keeps the scenario arithmetic obvious; pacing sums spend <= asOf.
        var rows = new MetricsUpsertRequest([new MetricRow(date, 1000, 100, 10, total, total * 2)]);
        api.LastResponse = await api.Current.PutAsJsonAsync($"/api/v1/campaigns/{api.CampaignId}/metrics", rows);
        Assert.Equal(HttpStatusCode.OK, api.LastResponse.StatusCode);
    }

    [Given(@"^the campaign recorded (\d+) impressions, (\d+) clicks, (\d+) conversions, ([\d.]+) spend and ([\d.]+) revenue$")]
    public async Task GivenRecorded(long imp, long clicks, long conv, decimal spend, decimal revenue)
    {
        var rows = new MetricsUpsertRequest([new MetricRow(Today, imp, clicks, conv, spend, revenue)]);
        api.LastResponse = await api.Current.PutAsJsonAsync($"/api/v1/campaigns/{api.CampaignId}/metrics", rows);
        Assert.Equal(HttpStatusCode.OK, api.LastResponse.StatusCode);
    }

    // ---- pacing ----------------------------------------------------------------------------------
    [When(@"^I request pacing as of (\d{4}-\d{2}-\d{2})$")]
    public async Task WhenPacing(string asOf) =>
        api.LastResponse = await api.Current.GetAsync($"/api/v1/campaigns/{api.CampaignId}/pacing?asOf={asOf}");

    [When(@"^tenant ""(.*)"" requests pacing for the ""(.*)"" campaign$")]
    public async Task WhenOtherTenantPacing(string tenant, string _) =>
        api.LastResponse = await api.Client(tenant).GetAsync($"/api/v1/campaigns/{api.CampaignId}/pacing");

    [Then(@"^the pacing percentage is ([\d.]+)$")]
    public async Task ThenPacingPercent(decimal pct) => Assert.Equal(pct, (await api.Read<PacingResponse>()).PacingPercent);

    [Then(@"^the pacing status is ""(.*)""$")]
    public async Task ThenPacingStatus(string status) => Assert.Equal(status, (await api.Read<PacingResponse>()).Status);

    [Then(@"^the suggested daily budget is ([\d.]+)$")]
    public async Task ThenSuggested(decimal amount) => Assert.Equal(amount, (await api.Read<PacingResponse>()).SuggestedDailyBudget);

    // ---- tenant-scoped reads ---------------------------------------------------------------------
    [When(@"^tenant ""(.*)"" lists campaigns$")]
    public async Task WhenTenantLists(string tenant) => api.LastResponse = await api.Client(tenant).GetAsync("/api/v1/campaigns");

    [When(@"^an anonymous client lists campaigns$")]
    public async Task WhenAnonymousLists() => api.LastResponse = await api.Anonymous.GetAsync("/api/v1/campaigns");

    [When(@"^tenant ""(.*)"" requests the ""(.*)"" campaign$")]
    public async Task WhenTenantGets(string tenant, string _) =>
        api.LastResponse = await api.Client(tenant).GetAsync($"/api/v1/campaigns/{api.CampaignId}");

    [When(@"^tenant ""(.*)"" requests the cross-channel report$")]
    public async Task WhenTenantReport(string tenant) =>
        api.LastResponse = await api.Client(tenant).GetAsync("/api/v1/reports/cross-channel");

    [Then(@"^the list contains ""(.*)""$")]
    public async Task ThenListContains(string name) => Assert.Contains(await api.Read<List<CampaignResponse>>(), c => c.Name == name);

    [Then(@"^the list does not contain ""(.*)""$")]
    public async Task ThenListExcludes(string name) => Assert.DoesNotContain(await api.Read<List<CampaignResponse>>(), c => c.Name == name);

    [Then(@"^the report total spend is ([\d.]+)$")]
    public async Task ThenReportSpend(decimal spend) => Assert.Equal(spend, (await api.Read<CrossChannelReport>()).Total.Spend);

    // ---- KPIs ------------------------------------------------------------------------------------
    [When(@"^I request the campaign KPIs$")]
    public async Task WhenKpis() => api.LastResponse = await api.Current.GetAsync($"/api/v1/campaigns/{api.CampaignId}/kpis");

    [Then(@"^the CTR is ([\d.]+)$")] public async Task ThenCtr(decimal v) => Assert.Equal(v, (await api.Read<KpiRow>()).Ctr);
    [Then(@"^the CPC is ([\d.]+)$")] public async Task ThenCpc(decimal v) => Assert.Equal(v, (await api.Read<KpiRow>()).Cpc);
    [Then(@"^the CPA is ([\d.]+)$")] public async Task ThenCpa(decimal v) => Assert.Equal(v, (await api.Read<KpiRow>()).Cpa);
    [Then(@"^the ROAS is ([\d.]+)$")] public async Task ThenRoas(decimal v) => Assert.Equal(v, (await api.Read<KpiRow>()).Roas);
}
