using System.Globalization;
using System.Net;
using System.Net.Http.Json;
using AdPulse.Api.Contracts;
using AdPulse.Api.Domain;
using AdPulse.Specs.Support;
using Reqnroll;
using Xunit;

namespace AdPulse.Specs.Steps;

[Binding]
public sealed class CommonSteps(ApiDriver api)
{
    private static readonly DateOnly Today = DateOnly.FromDateTime(DateTime.UtcNow);

    [Given(@"^a tenant ""(.*)"" with API key ""(.*)""$")]
    [Given(@"^another tenant ""(.*)"" with API key ""(.*)""$")]
    public Task GivenTenant(string name, string key) => api.AddTenant(name, key);

    [Given(@"^a draft campaign ""(.*)""$")]
    public Task GivenDraft(string name) => Create(name, Today.AddDays(-5), Today.AddDays(24), 100m);

    [Given(@"^an active campaign ""(.*)""$")]
    public async Task GivenActive(string name)
    {
        await GivenDraft(name);
        await Transition("activate");
        Assert.Equal(HttpStatusCode.OK, api.LastResponse!.StatusCode);
    }

    [Given(@"^a paused campaign ""(.*)""$")]
    public async Task GivenPaused(string name)
    {
        await GivenActive(name);
        await Transition("pause");
        Assert.Equal(HttpStatusCode.OK, api.LastResponse!.StatusCode);
    }

    [Given(@"^a draft campaign ""(.*)"" that ended on (\d{4}-\d{2}-\d{2})$")]
    public Task GivenEndedDraft(string name, string end)
    {
        var e = DateOnly.Parse(end, CultureInfo.InvariantCulture);
        return Create(name, e.AddDays(-30), e, 100m);
    }

    [Given(@"^a campaign ""(.*)"" running from (\d{4}-\d{2}-\d{2}) to (\d{4}-\d{2}-\d{2}) with a daily budget of ([\d.]+)$")]
    public Task GivenCampaignWithFlight(string name, string start, string end, decimal budget) =>
        Create(name, DateOnly.Parse(start, CultureInfo.InvariantCulture), DateOnly.Parse(end, CultureInfo.InvariantCulture), budget);

    private async Task Create(string name, DateOnly start, DateOnly end, decimal budget)
    {
        api.LastResponse = await api.Current.PostAsJsonAsync("/api/campaigns", new CampaignRequest(name, Channel.GoogleAds, budget, start, end));
        Assert.Equal(HttpStatusCode.Created, api.LastResponse.StatusCode);
        api.CampaignId = (await api.Read<CampaignResponse>()).Id;
    }

    [When(@"^I activate the campaign$")] public Task WhenActivate() => Transition("activate");
    [When(@"^I pause the campaign$")] public Task WhenPause() => Transition("pause");
    [When(@"^I complete the campaign$")] public Task WhenComplete() => Transition("complete");

    private async Task Transition(string action) =>
        api.LastResponse = await api.Current.PostAsync($"/api/campaigns/{api.CampaignId}/{action}", null);

    [Then(@"^the response status is (\d+)$")]
    public void ThenStatus(int status) => Assert.Equal(status, (int)api.LastResponse!.StatusCode);

    [Then(@"^the campaign status is ""(.*)""$")]
    public async Task ThenCampaignStatus(string status)
    {
        var c = await (await api.Current.GetAsync($"/api/campaigns/{api.CampaignId}")).Content.ReadFromJsonAsync<CampaignResponse>();
        Assert.Equal(Enum.Parse<CampaignStatus>(status), c!.Status);
    }

    [Then(@"^the response is an RFC 7807 problem with type ""(.*)""$")]
    public async Task ThenProblem(string code)
    {
        Assert.Equal("application/problem+json", api.LastResponse!.Content.Headers.ContentType?.MediaType);
        var body = await api.ReadJson();
        Assert.Equal(Problems.TypeBase + code, body["type"].GetString());
        Assert.Equal((int)api.LastResponse.StatusCode, body["status"].GetInt32());
        Assert.True(body.ContainsKey("title"));
        Assert.True(body.ContainsKey("traceId"));
    }

    [Then(@"^the problem detail mentions ""(.*)""$")]
    public async Task ThenDetailMentions(string text)
    {
        var body = await api.ReadJson();
        Assert.Contains(text, body["detail"].GetString(), StringComparison.OrdinalIgnoreCase);
    }

    [Then(@"^editing the campaign is rejected with (\d+) and problem type ""(.*)""$")]
    public async Task ThenEditRejected(int status, string code)
    {
        api.LastResponse = await api.Current.PutAsJsonAsync($"/api/campaigns/{api.CampaignId}", new CampaignRequest("Renamed", Channel.Meta, 50m, Today, Today.AddDays(5)));
        ThenStatus(status);
        await ThenProblem(code);
    }
}
