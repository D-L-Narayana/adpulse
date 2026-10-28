using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using AdPulse.Api.Contracts;
using AdPulse.Api.Data;
using AdPulse.Api.Tenancy;
using AdPulse.Specs.Support;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Reqnroll;
using Xunit;

namespace AdPulse.Specs.Steps;

[Binding]
public sealed class WorkspaceSteps(ApiDriver api)
{
    private TenantCreatedResponse? _signup;

    private const string GoogleCsv =
        "Campaign report (Oct 1, 2026 - Oct 7, 2026)\n\nCampaign,Campaign state,Day,Currency code,Cost,Impressions,Clicks,Conversions,Conv. value\n" +
        "Brand Search,Enabled,2026-10-01,INR,\"1,250.50\",10000,500,25,8000\nTotal: Account,,,,\"1,250.50\",10000,500,25,8000\n";
    private const string MetaCsv =
        "Reporting starts,Reporting ends,Campaign name,Day,Amount spent (INR),Impressions,Link clicks,Results\n" +
        "2026-10-01,2026-10-01,Prospecting,2026-10-01,500.5,20000,300,10\n2026-10-02,2026-10-02,Prospecting,2026-10-02,520,21000,310,11\n";

    [When(@"^a visitor signs up as ""(.*)"" with email ""(.*)""$")]
    public async Task WhenSignup(string name, string email)
    {
        api.LastResponse = await api.Anonymous.PostAsJsonAsync("/api/v1/tenants", new TenantSignupRequest(name, email));
        if (api.LastResponse.StatusCode != HttpStatusCode.Created) return;
        _signup = await api.Read<TenantCreatedResponse>();
        api.AddSignedUpTenant(name, _signup.ApiKey);
    }

    [Then(@"^the signup response contains an API key starting with ""(.*)""$")]
    public void ThenKeyPrefix(string prefix) => Assert.StartsWith(prefix, _signup!.ApiKey);

    [Then(@"^the workspace slug starts with ""(.*)""$")]
    public void ThenSlug(string prefix) => Assert.StartsWith(prefix, _signup!.Slug);

    [Then(@"^the stored tenant holds the SHA-256 of that key, not the key$")]
    public async Task ThenHashed()
    {
        using var scope = api.Services.CreateScope();
        var row = await scope.ServiceProvider.GetRequiredService<AdPulseDbContext>().Tenants.SingleAsync(t => t.Id == _signup!.Id);
        Assert.Equal(ApiKeys.Hash(_signup!.ApiKey), row.ApiKeyHash);
        Assert.NotEqual(_signup.ApiKey, row.ApiKeyHash);
    }

    [When(@"^the new workspace lists its campaigns$")]
    public async Task WhenNewLists() => api.LastResponse = await api.Client(api.NewestTenant).GetAsync("/api/v1/campaigns");

    [Then(@"^the list is empty$")]
    public async Task ThenListEmpty() => Assert.Empty(await api.Read<List<CampaignResponse>>());

    [When(@"^the new workspace previews a Google Ads CSV export$")]
    public Task WhenPreviewGoogle() => Upload("/api/v1/imports/csv?dryRun=true", "google.csv", GoogleCsv);

    [When(@"^the new workspace imports a Meta Ads Manager CSV export$")]
    public Task WhenImportMeta() => Upload("/api/v1/imports/csv", "meta.csv", MetaCsv);

    private async Task Upload(string url, string fileName, string csv)
    {
        using var form = new MultipartFormDataContent();
        var part = new StringContent(csv);
        part.Headers.ContentType = new MediaTypeHeaderValue("text/csv");
        form.Add(part, "file", fileName);
        api.LastResponse = await api.Client(api.NewestTenant).PostAsync(url, form);
    }

    [Then(@"^the detected format is ""(.*)""$")]
    public async Task ThenFormat(string format) => Assert.Equal(format, (await api.ReadJson())["detectedFormat"].GetString());

    [Then(@"^the column ""(.*)"" is mapped to ""(.*)""$")]
    public async Task ThenMapped(string field, string header) =>
        Assert.Equal(header, (await api.Read<ImportPreviewResponse>()).ColumnMapping[field]);

    [Then(@"^the import history has (\d+) entry$")]
    public async Task ThenHistory(int count)
    {
        api.LastResponse = await api.Client(api.NewestTenant).GetAsync("/api/v1/imports");
        Assert.Equal(count, (await api.Read<List<ImportHistoryRow>>()).Count);
    }
}
