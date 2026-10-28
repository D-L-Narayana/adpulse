using System.Net;
using System.Net.Http.Json;
using AdPulse.Api.Contracts;
using AdPulse.Api.Domain;

namespace AdPulse.Tests;

public class CampaignApiTests : IClassFixture<AdPulseFactory>
{
    private readonly AdPulseFactory _f;
    public CampaignApiTests(AdPulseFactory f) => _f = f;

    [Fact]
    public async Task Health_is_public()
    {
        var r = await _f.CreateClient().GetAsync("/health");
        Assert.Equal(HttpStatusCode.OK, r.StatusCode);
    }

    [Fact]
    public async Task Swagger_is_public()
    {
        var r = await _f.CreateClient().GetAsync("/swagger/v1/swagger.json");
        Assert.Equal(HttpStatusCode.OK, r.StatusCode);
    }

    [Fact]
    public async Task Api_without_key_is_401_problem_json()
    {
        var r = await _f.CreateClient().GetAsync("/api/campaigns");
        Assert.Equal(HttpStatusCode.Unauthorized, r.StatusCode);
        var body = await r.Problem();
        Assert.Equal("missing_api_key", body.Code);
        Assert.Equal(401, body.Status);
        Assert.False(string.IsNullOrEmpty(body.TraceId));
    }

    [Fact]
    public async Task Api_with_unknown_key_is_401()
    {
        var r = await _f.ClientFor("nope").GetAsync("/api/campaigns");
        Assert.Equal(HttpStatusCode.Unauthorized, r.StatusCode);
        Assert.Equal("unknown_api_key", (await r.Problem()).Code);
    }

    [Fact]
    public async Task Me_returns_resolved_tenant()
    {
        var (c, t) = await _f.NewTenantClientAsync("Me Tenant");
        var me = await (await c.GetAsync("/api/me")).Json<Dictionary<string, string>>();
        Assert.Equal(t.Id.ToString(), me["id"]);
        Assert.Equal("Me Tenant", me["name"]);
    }

    [Fact]
    public async Task Create_then_get_round_trips()
    {
        var (c, _) = await _f.NewTenantClientAsync();
        var created = await c.PostJson("/api/campaigns", Flight.Request());
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        var dto = await created.Json<CampaignResponse>();
        Assert.Equal(CampaignStatus.Draft, dto.Status);
        Assert.Equal($"/api/campaigns/{dto.Id}", created.Headers.Location!.ToString());

        var got = await (await c.GetAsync($"/api/campaigns/{dto.Id}")).Json<CampaignResponse>();
        Assert.Equal(dto, got);
    }

    [Fact]
    public async Task Enums_are_serialized_as_strings()
    {
        var (c, _) = await _f.NewTenantClientAsync();
        var created = await c.CreateCampaign(Flight.Request(ch: Channel.LinkedIn));
        var raw = await (await c.GetAsync($"/api/campaigns/{created.Id}")).Content.ReadAsStringAsync();
        Assert.Contains("\"channel\":\"LinkedIn\"", raw);
        Assert.Contains("\"status\":\"Draft\"", raw);
    }

    [Fact]
    public async Task List_filters_by_status_and_channel()
    {
        var (c, _) = await _f.NewTenantClientAsync();
        var a = await c.CreateCampaign(Flight.Request("Alpha", Channel.Meta));
        await c.CreateCampaign(Flight.Request("Beta", Channel.GoogleAds));
        await c.Transition(a.Id, "activate");
        var active = await (await c.GetAsync("/api/campaigns?status=Active")).Json<List<CampaignResponse>>();
        Assert.Single(active); Assert.Equal("Alpha", active[0].Name);
        var google = await (await c.GetAsync("/api/campaigns?channel=GoogleAds")).Json<List<CampaignResponse>>();
        Assert.Single(google); Assert.Equal("Beta", google[0].Name);
    }

    [Fact]
    public async Task Validation_errors_are_422_problem_with_field_map()
    {
        var (c, _) = await _f.NewTenantClientAsync();
        var r = await c.PostJson("/api/campaigns", Flight.Request(name: "x", budget: 0));
        Assert.Equal(HttpStatusCode.UnprocessableEntity, r.StatusCode);
        Assert.Equal("application/problem+json", r.Content.Headers.ContentType?.MediaType);
        var body = await r.Content.ReadFromJsonAsync<Dictionary<string, System.Text.Json.JsonElement>>();
        var errors = body!["errors"];
        Assert.True(errors.TryGetProperty("Name", out _));
        Assert.True(errors.TryGetProperty("DailyBudget", out _));
        Assert.EndsWith("validation_error", body["type"].GetString());
    }

    [Fact]
    public async Task End_before_start_is_rejected()
    {
        var (c, _) = await _f.NewTenantClientAsync();
        var r = await c.PostJson("/api/campaigns", new CampaignRequest("Bad dates", Channel.Meta, 10, Flight.End, Flight.Start));
        Assert.Equal(HttpStatusCode.UnprocessableEntity, r.StatusCode);
        Assert.Equal("invalid_dates", (await r.Problem()).Code);
    }

    [Fact]
    public async Task Duplicate_name_within_tenant_is_409_but_allowed_across_tenants()
    {
        var (a, _) = await _f.NewTenantClientAsync();
        var (b, _) = await _f.NewTenantClientAsync();
        Assert.Equal(HttpStatusCode.Created, (await a.PostJson("/api/campaigns", Flight.Request("Same"))).StatusCode);
        var dup = await a.PostJson("/api/campaigns", Flight.Request("Same"));
        Assert.Equal(HttpStatusCode.Conflict, dup.StatusCode);
        Assert.Equal("duplicate_name", (await dup.Problem()).Code);
        Assert.Equal(HttpStatusCode.Created, (await b.PostJson("/api/campaigns", Flight.Request("Same"))).StatusCode);
    }

    [Fact]
    public async Task Update_changes_fields_and_bumps_updated_timestamp()
    {
        var (c, _) = await _f.NewTenantClientAsync();
        var created = await c.CreateCampaign(Flight.Request("Before"));
        var r = await c.PutJson($"/api/campaigns/{created.Id}", Flight.Request("After", Channel.Amazon, 999m));
        Assert.Equal(HttpStatusCode.OK, r.StatusCode);
        var updated = await r.Json<CampaignResponse>();
        Assert.Equal("After", updated.Name); Assert.Equal(Channel.Amazon, updated.Channel); Assert.Equal(999m, updated.DailyBudget);
        Assert.True(updated.UpdatedUtc >= created.UpdatedUtc);
    }

    [Fact]
    public async Task Completed_campaign_is_read_only()
    {
        var (c, _) = await _f.NewTenantClientAsync();
        var id = (await c.CreateCampaign(Flight.Request("Life"))).Id;
        await c.Transition(id, "activate");
        Assert.Equal(CampaignStatus.Completed, (await c.Transition(id, "complete")).Status);
        var edit = await c.PutJson($"/api/campaigns/{id}", Flight.Request("Life renamed"));
        Assert.Equal(HttpStatusCode.Conflict, edit.StatusCode);
        Assert.Equal("campaign_completed", (await edit.Problem()).Code);
    }

    [Fact]
    public async Task Metrics_upsert_is_idempotent_and_reports_inserted_vs_updated()
    {
        var (c, _) = await _f.NewTenantClientAsync();
        var id = (await c.CreateCampaign(Flight.Request("Metrics"))).Id;
        var rows = new List<MetricRow>
        {
            new(Flight.Day(1), 1000, 50, 5, 40m, 200m),
            new(Flight.Day(2), 2000, 80, 8, 60m, 320m),
        };
        var first = await (await c.PutJson($"/api/campaigns/{id}/metrics", new MetricsUpsertRequest(rows))).Json<MetricsUpsertResponse>();
        Assert.Equal((2, 0), (first.Inserted, first.Updated));

        rows[1] = rows[1] with { Spend = 65m };
        rows.Add(new(Flight.Day(3), 500, 10, 1, 9m, 30m));
        var second = await (await c.PutJson($"/api/campaigns/{id}/metrics", new MetricsUpsertRequest(rows))).Json<MetricsUpsertResponse>();
        Assert.Equal((1, 2), (second.Inserted, second.Updated));

        var stored = await (await c.GetAsync($"/api/campaigns/{id}/metrics")).Json<List<MetricRow>>();
        Assert.Equal(3, stored.Count);
        Assert.Equal(65m, stored.Single(r => r.Date == Flight.Day(2)).Spend);
    }

    [Fact]
    public async Task Metrics_with_duplicate_dates_or_negatives_are_422()
    {
        var (c, _) = await _f.NewTenantClientAsync();
        var id = (await c.CreateCampaign(Flight.Request("Bad metrics"))).Id;
        var d = Flight.Day(1);
        var dup = await c.PutJson($"/api/campaigns/{id}/metrics", new MetricsUpsertRequest([new(d, 1, 1, 1, 1, 1), new(d, 2, 2, 2, 2, 2)]));
        Assert.Equal(HttpStatusCode.UnprocessableEntity, dup.StatusCode);
        Assert.Equal("duplicate_dates", (await dup.Problem()).Code);
        var neg = await c.PutJson($"/api/campaigns/{id}/metrics", new MetricsUpsertRequest([new(d, -1, 1, 1, 1, 1)]));
        Assert.Equal(HttpStatusCode.UnprocessableEntity, neg.StatusCode);
        var empty = await c.PutJson($"/api/campaigns/{id}/metrics", new MetricsUpsertRequest([]));
        Assert.Equal(HttpStatusCode.UnprocessableEntity, empty.StatusCode);
    }

    [Fact]
    public async Task Campaign_kpis_are_computed_server_side()
    {
        var (c, _) = await _f.NewTenantClientAsync();
        var id = (await c.CreateCampaign(Flight.Request("KPI"))).Id;
        await c.PutJson($"/api/campaigns/{id}/metrics", new MetricsUpsertRequest([new(Flight.Day(1), 10_000, 250, 10, 125m, 500m), new(Flight.Day(2), 10_000, 250, 10, 125m, 500m)]));
        var k = await (await c.GetAsync($"/api/campaigns/{id}/kpis")).Json<KpiRow>();
        Assert.Equal(2.5m, k.Ctr); Assert.Equal(0.5m, k.Cpc); Assert.Equal(12.5m, k.Cpa); Assert.Equal(4m, k.Roas); Assert.Equal(250m, k.Spend);
    }

    [Fact]
    public async Task Pacing_uses_only_this_campaigns_spend()
    {
        var (c, _) = await _f.NewTenantClientAsync();
        var a = (await c.CreateCampaign(Flight.Request("Pace A", budget: 100m))).Id;
        var b = (await c.CreateCampaign(Flight.Request("Pace B", budget: 100m))).Id;
        await c.PutJson($"/api/campaigns/{a}/metrics", new MetricsUpsertRequest([new(Flight.Day(1), 1, 1, 1, 120m, 0), new(Flight.Day(2), 1, 1, 1, 130m, 0)]));
        await c.PutJson($"/api/campaigns/{b}/metrics", new MetricsUpsertRequest([new(Flight.Day(1), 1, 1, 1, 10m, 0)]));

        var pa = await (await c.GetAsync($"/api/campaigns/{a}/pacing?asOf={Flight.Day(2):yyyy-MM-dd}")).Json<PacingResponse>();
        Assert.Equal(250m, pa.ActualSpendToDate); Assert.Equal(200m, pa.ExpectedSpendToDate); Assert.Equal("over", pa.Status); Assert.Equal(50m, pa.Deviation);
        var pb = await (await c.GetAsync($"/api/campaigns/{b}/pacing?asOf={Flight.Day(2):yyyy-MM-dd}")).Json<PacingResponse>();
        Assert.Equal(10m, pb.ActualSpendToDate); Assert.Equal("under", pb.Status);
    }

    [Fact]
    public async Task Summary_combines_kpis_and_pacing_per_campaign()
    {
        var (c, _) = await _f.NewTenantClientAsync();
        var a = (await c.CreateCampaign(Flight.Request("Sum A", budget: 100m))).Id;
        await c.Transition(a, "activate");
        var b = (await c.CreateCampaign(Flight.Request("Sum B", budget: 100m))).Id;
        await c.Transition(b, "activate");
        await c.CreateCampaign(Flight.Request("Sum C draft", budget: 100m));
        await c.PutJson($"/api/campaigns/{a}/metrics", new MetricsUpsertRequest([new(Flight.Day(1), 1000, 100, 10, 100m, 400m), new(Flight.Day(2), 1000, 100, 10, 100m, 400m)]));
        var rows = await (await c.GetAsync($"/api/campaigns/summary?asOf={Flight.Day(2):yyyy-MM-dd}")).Json<List<CampaignSummary>>();
        Assert.Equal(3, rows.Count);
        Assert.Equal("not-started", rows.Single(r => r.Name == "Sum C draft").PacingStatus); // drafts are never "under"
        var sa = rows.Single(r => r.Name == "Sum A");
        Assert.Equal(10m, sa.Ctr); Assert.Equal(1m, sa.Cpc); Assert.Equal(10m, sa.Cpa); Assert.Equal(4m, sa.Roas);
        Assert.Equal(100m, sa.PacingPercent); Assert.Equal("on-track", sa.PacingStatus);
        var sb = rows.Single(r => r.Name == "Sum B");
        Assert.Equal(0m, sb.Spend); Assert.Equal("under", sb.PacingStatus);
    }

    [Fact]
    public async Task Delete_cascades_metrics()
    {
        var (c, _) = await _f.NewTenantClientAsync();
        var id = (await c.CreateCampaign(Flight.Request("Del"))).Id;
        await c.PutJson($"/api/campaigns/{id}/metrics", new MetricsUpsertRequest([new(Flight.Day(1), 1, 1, 1, 1, 1)]));
        Assert.Equal(HttpStatusCode.NoContent, (await c.DeleteAsync($"/api/campaigns/{id}")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await c.GetAsync($"/api/campaigns/{id}/metrics")).StatusCode);
    }

    [Fact]
    public async Task Unknown_campaign_is_404()
    {
        var (c, _) = await _f.NewTenantClientAsync();
        Assert.Equal(HttpStatusCode.NotFound, (await c.GetAsync($"/api/campaigns/{Guid.NewGuid()}")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await c.PostAsync($"/api/campaigns/{Guid.NewGuid()}/activate", null)).StatusCode);
    }
}
