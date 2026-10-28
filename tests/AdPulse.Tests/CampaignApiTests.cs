using System.Net;
using System.Net.Http.Json;
using AdPulse.Api.Contracts;
using AdPulse.Api.Domain;

namespace AdPulse.Tests;

public class CampaignApiTests : IClassFixture<AdPulseFactory>
{
    private readonly AdPulseFactory _f;
    public CampaignApiTests(AdPulseFactory f) => _f = f;

    private static CampaignRequest Req(string name = "Brand Search", Channel ch = Channel.GoogleAds, decimal budget = 250m) =>
        new(name, ch, budget, new DateOnly(2025, 3, 1), new DateOnly(2025, 3, 31));

    [Fact]
    public async Task Health_is_public()
    {
        var r = await _f.CreateClient().GetAsync("/health");
        Assert.Equal(HttpStatusCode.OK, r.StatusCode);
    }

    [Fact]
    public async Task Api_without_key_is_401_with_problem_body()
    {
        var r = await _f.CreateClient().GetAsync("/api/campaigns");
        Assert.Equal(HttpStatusCode.Unauthorized, r.StatusCode);
        var body = await r.Json<ApiError>();
        Assert.Equal("missing_api_key", body.Type);
    }

    [Fact]
    public async Task Api_with_unknown_key_is_401()
    {
        var c = _f.CreateClient();
        c.DefaultRequestHeaders.Add("X-Api-Key", "nope");
        var r = await c.GetAsync("/api/campaigns");
        Assert.Equal(HttpStatusCode.Unauthorized, r.StatusCode);
        Assert.Equal("unknown_api_key", (await r.Json<ApiError>()).Type);
    }

    [Fact]
    public async Task Create_then_get_round_trips()
    {
        var (c, _) = await _f.NewTenantClientAsync();
        var created = await c.PostJson("/api/campaigns", Req());
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        var dto = await created.Json<CampaignResponse>();
        Assert.Equal(CampaignStatus.Draft, dto.Status);
        Assert.Equal($"/api/campaigns/{dto.Id}", created.Headers.Location!.ToString());

        var got = await (await c.GetAsync($"/api/campaigns/{dto.Id}")).Json<CampaignResponse>();
        Assert.Equal(dto with { }, got);
    }

    [Fact]
    public async Task Validation_errors_are_422_with_field_map()
    {
        var (c, _) = await _f.NewTenantClientAsync();
        var r = await c.PostJson("/api/campaigns", Req(name: "x", budget: 0));
        Assert.Equal(HttpStatusCode.UnprocessableEntity, r.StatusCode);
        var body = await r.Content.ReadFromJsonAsync<Dictionary<string, System.Text.Json.JsonElement>>();
        var errors = body!["errors"];
        Assert.True(errors.TryGetProperty("Name", out _));
        Assert.True(errors.TryGetProperty("DailyBudget", out _));
    }

    [Fact]
    public async Task End_before_start_is_rejected()
    {
        var (c, _) = await _f.NewTenantClientAsync();
        var r = await c.PostJson("/api/campaigns", new CampaignRequest("Bad dates", Channel.Meta, 10, new DateOnly(2025, 5, 2), new DateOnly(2025, 5, 1)));
        Assert.Equal(HttpStatusCode.UnprocessableEntity, r.StatusCode);
        Assert.Equal("invalid_dates", (await r.Json<ApiError>()).Type);
    }

    [Fact]
    public async Task Duplicate_name_within_tenant_is_409_but_allowed_across_tenants()
    {
        var (a, _) = await _f.NewTenantClientAsync();
        var (b, _) = await _f.NewTenantClientAsync();
        Assert.Equal(HttpStatusCode.Created, (await a.PostJson("/api/campaigns", Req("Same"))).StatusCode);
        var dup = await a.PostJson("/api/campaigns", Req("Same"));
        Assert.Equal(HttpStatusCode.Conflict, dup.StatusCode);
        Assert.Equal("duplicate_name", (await dup.Json<ApiError>()).Type);
        Assert.Equal(HttpStatusCode.Created, (await b.PostJson("/api/campaigns", Req("Same"))).StatusCode);
    }

    [Fact]
    public async Task Tenants_cannot_see_or_mutate_each_others_campaigns()
    {
        var (a, _) = await _f.NewTenantClientAsync();
        var (b, _) = await _f.NewTenantClientAsync();
        var mine = await (await a.PostJson("/api/campaigns", Req("Private"))).Json<CampaignResponse>();

        var list = await (await b.GetAsync("/api/campaigns")).Json<List<CampaignResponse>>();
        Assert.DoesNotContain(list, x => x.Id == mine.Id);
        Assert.Equal(HttpStatusCode.NotFound, (await b.GetAsync($"/api/campaigns/{mine.Id}")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await b.PutJson($"/api/campaigns/{mine.Id}", Req("Hijack"))).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await b.DeleteAsync($"/api/campaigns/{mine.Id}")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await b.PutJson($"/api/campaigns/{mine.Id}/metrics", new MetricsUpsertRequest([new MetricRow(new DateOnly(2025, 3, 1), 1, 1, 1, 1, 1)]))).StatusCode);

        // still intact for the owner
        Assert.Equal("Private", (await (await a.GetAsync($"/api/campaigns/{mine.Id}")).Json<CampaignResponse>()).Name);
    }

    [Fact]
    public async Task Lifecycle_transitions_are_enforced()
    {
        var (c, _) = await _f.NewTenantClientAsync();
        var id = (await (await c.PostJson("/api/campaigns", Req("Life"))).Json<CampaignResponse>()).Id;

        var pauseDraft = await c.PostAsync($"/api/campaigns/{id}/pause", null);
        Assert.Equal(HttpStatusCode.Conflict, pauseDraft.StatusCode);
        Assert.Equal("invalid_transition", (await pauseDraft.Json<ApiError>()).Type);

        Assert.Equal(CampaignStatus.Active, (await (await c.PostAsync($"/api/campaigns/{id}/activate", null)).Json<CampaignResponse>()).Status);
        Assert.Equal(CampaignStatus.Paused, (await (await c.PostAsync($"/api/campaigns/{id}/pause", null)).Json<CampaignResponse>()).Status);
        Assert.Equal(CampaignStatus.Active, (await (await c.PostAsync($"/api/campaigns/{id}/activate", null)).Json<CampaignResponse>()).Status);
        Assert.Equal(CampaignStatus.Ended, (await (await c.PostAsync($"/api/campaigns/{id}/end", null)).Json<CampaignResponse>()).Status);

        Assert.Equal(HttpStatusCode.Conflict, (await c.PostAsync($"/api/campaigns/{id}/activate", null)).StatusCode);
        var edit = await c.PutJson($"/api/campaigns/{id}", Req("Life renamed"));
        Assert.Equal(HttpStatusCode.Conflict, edit.StatusCode);
        Assert.Equal("campaign_ended", (await edit.Json<ApiError>()).Type);
    }

    [Fact]
    public async Task Metrics_upsert_is_idempotent_and_reports_inserted_vs_updated()
    {
        var (c, _) = await _f.NewTenantClientAsync();
        var id = (await (await c.PostJson("/api/campaigns", Req("Metrics"))).Json<CampaignResponse>()).Id;
        var rows = new List<MetricRow>
        {
            new(new DateOnly(2025, 3, 1), 1000, 50, 5, 40m, 200m),
            new(new DateOnly(2025, 3, 2), 2000, 80, 8, 60m, 320m),
        };
        var first = await (await c.PutJson($"/api/campaigns/{id}/metrics", new MetricsUpsertRequest(rows))).Json<MetricsUpsertResponse>();
        Assert.Equal((2, 0), (first.Inserted, first.Updated));

        rows[1] = rows[1] with { Spend = 65m };
        rows.Add(new(new DateOnly(2025, 3, 3), 500, 10, 1, 9m, 30m));
        var second = await (await c.PutJson($"/api/campaigns/{id}/metrics", new MetricsUpsertRequest(rows))).Json<MetricsUpsertResponse>();
        Assert.Equal((1, 2), (second.Inserted, second.Updated));

        var stored = await (await c.GetAsync($"/api/campaigns/{id}/metrics")).Json<List<MetricRow>>();
        Assert.Equal(3, stored.Count);
        Assert.Equal(65m, stored.Single(r => r.Date == new DateOnly(2025, 3, 2)).Spend);
    }

    [Fact]
    public async Task Metrics_with_duplicate_dates_or_negatives_are_422()
    {
        var (c, _) = await _f.NewTenantClientAsync();
        var id = (await (await c.PostJson("/api/campaigns", Req("Bad metrics"))).Json<CampaignResponse>()).Id;
        var d = new DateOnly(2025, 3, 1);
        var dup = await c.PutJson($"/api/campaigns/{id}/metrics", new MetricsUpsertRequest([new(d, 1, 1, 1, 1, 1), new(d, 2, 2, 2, 2, 2)]));
        Assert.Equal(HttpStatusCode.UnprocessableEntity, dup.StatusCode);
        var neg = await c.PutJson($"/api/campaigns/{id}/metrics", new MetricsUpsertRequest([new(d, -1, 1, 1, 1, 1)]));
        Assert.Equal(HttpStatusCode.UnprocessableEntity, neg.StatusCode);
    }

    [Fact]
    public async Task Pacing_uses_only_this_campaigns_spend()
    {
        var (c, _) = await _f.NewTenantClientAsync();
        var a = (await (await c.PostJson("/api/campaigns", Req("Pace A", budget: 100m))).Json<CampaignResponse>()).Id;
        var b = (await (await c.PostJson("/api/campaigns", Req("Pace B", budget: 100m))).Json<CampaignResponse>()).Id;
        await c.PutJson($"/api/campaigns/{a}/metrics", new MetricsUpsertRequest([new(new DateOnly(2025, 3, 1), 1, 1, 1, 120m, 0), new(new DateOnly(2025, 3, 2), 1, 1, 1, 130m, 0)]));
        await c.PutJson($"/api/campaigns/{b}/metrics", new MetricsUpsertRequest([new(new DateOnly(2025, 3, 1), 1, 1, 1, 10m, 0)]));

        var pa = await (await c.GetAsync($"/api/campaigns/{a}/pacing?asOf=2025-03-02")).Json<PacingResponse>();
        Assert.Equal(250m, pa.ActualSpendToDate); Assert.Equal(200m, pa.ExpectedSpendToDate); Assert.Equal("over", pa.Status);
        var pb = await (await c.GetAsync($"/api/campaigns/{b}/pacing?asOf=2025-03-02")).Json<PacingResponse>();
        Assert.Equal(10m, pb.ActualSpendToDate); Assert.Equal("under", pb.Status);
    }

    [Fact]
    public async Task Delete_cascades_metrics()
    {
        var (c, _) = await _f.NewTenantClientAsync();
        var id = (await (await c.PostJson("/api/campaigns", Req("Del"))).Json<CampaignResponse>()).Id;
        await c.PutJson($"/api/campaigns/{id}/metrics", new MetricsUpsertRequest([new(new DateOnly(2025, 3, 1), 1, 1, 1, 1, 1)]));
        Assert.Equal(HttpStatusCode.NoContent, (await c.DeleteAsync($"/api/campaigns/{id}")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await c.GetAsync($"/api/campaigns/{id}/metrics")).StatusCode);
    }
}
