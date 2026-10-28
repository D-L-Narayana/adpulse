using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using AdPulse.Api.Contracts;
using AdPulse.Api.Domain;
using AdPulse.Api.Endpoints;
using AdPulse.Api.Services;
using AdPulse.Api.Tenancy;
using Microsoft.Extensions.DependencyInjection;

namespace AdPulse.Tests;

/// <summary>Bulk REST ingest (idempotent), SSE stream, pacing alerts, sample dataset and the summary report.</summary>
public class IngestAndLiveTests : IClassFixture<AdPulseFactory>
{
    private readonly AdPulseFactory _f;
    public IngestAndLiveTests(AdPulseFactory f) => _f = f;

    private static BulkMetricsRequest Body(string campaign = "Push Campaign", decimal spend = 100m) => new([
        new(campaign, null, Channel.GoogleAds, Flight.Day(1), 1000, 50, 5, spend, 400m),
        new(campaign, null, Channel.GoogleAds, Flight.Day(2), 1100, 55, 6, spend, 450m)]);

    // ---- bulk + idempotency ---------------------------------------------------------------------------------

    [Fact]
    public async Task Bulk_creates_campaign_by_name_and_upserts()
    {
        var (client, _) = await _f.NewTenantClientAsync();
        var first = await (await client.PostJson("/api/v1/metrics/bulk", Body())).Json<BulkMetricsResponse>();
        Assert.Equal(1, first.CampaignsCreated);
        Assert.Equal(2, first.MetricsInserted);
        Assert.False(first.Replayed);

        var second = await (await client.PostJson("/api/v1/metrics/bulk", Body(spend: 120m))).Json<BulkMetricsResponse>();
        Assert.Equal(0, second.CampaignsCreated);
        Assert.Equal(2, second.MetricsUpdated);

        var c = Assert.Single(await (await client.GetAsync("/api/v1/campaigns")).Json<List<CampaignResponse>>());
        Assert.Equal("Push Campaign", c.Name);
        Assert.Equal(CampaignStatus.Active, c.Status);
        var rows = await (await client.GetAsync($"/api/v1/campaigns/{c.Id}/metrics")).Json<List<MetricRow>>();
        Assert.Equal(2, rows.Count);
        Assert.All(rows, r => Assert.Equal(120m, r.Spend));
    }

    [Fact]
    public async Task Same_idempotency_key_replays_instead_of_reapplying()
    {
        var (client, _) = await _f.NewTenantClientAsync();
        var key = Guid.NewGuid().ToString();
        using var r1 = new HttpRequestMessage(HttpMethod.Post, "/api/v1/metrics/bulk") { Content = JsonContent.Create(Body()) };
        r1.Headers.Add(MetricsEndpoints.IdempotencyHeader, key);
        var first = await (await client.SendAsync(r1)).Json<BulkMetricsResponse>();
        Assert.Equal(2, first.MetricsInserted);

        using var r2 = new HttpRequestMessage(HttpMethod.Post, "/api/v1/metrics/bulk") { Content = JsonContent.Create(Body()) };
        r2.Headers.Add(MetricsEndpoints.IdempotencyHeader, key);
        var resp2 = await client.SendAsync(r2);
        Assert.Equal(HttpStatusCode.OK, resp2.StatusCode);
        Assert.Equal("true", resp2.Headers.GetValues("Idempotent-Replayed").Single());
        var replay = await resp2.Json<BulkMetricsResponse>();
        Assert.True(replay.Replayed);
        Assert.Equal(2, replay.MetricsInserted); // the original response, not "2 updated"
        Assert.Equal(first.CampaignIds, replay.CampaignIds);

        // same key, different body -> rejected
        using var r3 = new HttpRequestMessage(HttpMethod.Post, "/api/v1/metrics/bulk") { Content = JsonContent.Create(Body(spend: 999m)) };
        r3.Headers.Add(MetricsEndpoints.IdempotencyHeader, key);
        var resp3 = await client.SendAsync(r3);
        Assert.Equal(HttpStatusCode.UnprocessableEntity, resp3.StatusCode);
        Assert.Equal("idempotency_key_reused", (await resp3.Problem()).Code);

        // keys are per tenant: another tenant may use the same key
        var (other, _) = await _f.NewTenantClientAsync();
        using var r4 = new HttpRequestMessage(HttpMethod.Post, "/api/v1/metrics/bulk") { Content = JsonContent.Create(Body()) };
        r4.Headers.Add(MetricsEndpoints.IdempotencyHeader, key);
        var fresh = await (await other.SendAsync(r4)).Json<BulkMetricsResponse>();
        Assert.False(fresh.Replayed);
        Assert.Equal(1, fresh.CampaignsCreated);
    }

    [Fact]
    public async Task Bulk_validation_problems()
    {
        var (client, _) = await _f.NewTenantClientAsync();
        var r = await client.PostJson("/api/v1/metrics/bulk", new { rows = new[] { new { date = "2026-01-01", impressions = 1, clicks = 1, conversions = 1, spend = 1, revenue = 1 } } });
        Assert.Equal(HttpStatusCode.UnprocessableEntity, r.StatusCode);
        Assert.Equal("missing_campaign", (await r.Problem()).Code);

        r = await client.PostJson("/api/v1/metrics/bulk", new BulkMetricsRequest([new(null, Guid.NewGuid(), null, Flight.Day(1), 1, 1, 1, 1, 1)]));
        Assert.Equal(HttpStatusCode.NotFound, r.StatusCode);
        Assert.Equal("campaign_not_found", (await r.Problem()).Code);

        r = await client.PostJson("/api/v1/metrics/bulk", new BulkMetricsRequest([new("X", null, null, Flight.Day(1), 1, 1, 1, 1, 1), new("x", null, null, Flight.Day(1), 1, 1, 1, 1, 1)]));
        Assert.Equal("duplicate_rows", (await r.Problem()).Code);

        r = await client.PostJson("/api/v1/metrics/bulk", new BulkMetricsRequest([new("X", null, null, Flight.Day(1), -1, 1, 1, 1, 1)]));
        Assert.Equal(HttpStatusCode.UnprocessableEntity, r.StatusCode);
    }

    // ---- SSE ----------------------------------------------------------------------------------------------

    [Fact]
    public async Task Stream_emits_campaign_metrics_and_import_events_for_the_right_tenant_only()
    {
        var (client, tenant) = await _f.NewTenantClientAsync();
        var (other, _) = await _f.NewTenantClientAsync();
        var bus = _f.Services.GetRequiredService<EventBus>();

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(20));
        using var req = new HttpRequestMessage(HttpMethod.Get, "/api/v1/stream");
        var resp = await client.SendAsync(req, HttpCompletionOption.ResponseHeadersRead, cts.Token);
        Assert.Equal(HttpStatusCode.OK, resp.StatusCode);
        Assert.Equal("text/event-stream", resp.Content.Headers.ContentType?.MediaType);
        using var reader = new StreamReader(await resp.Content.ReadAsStreamAsync(cts.Token));

        // wait until the subscription is registered before producing events
        var deadline = DateTime.UtcNow.AddSeconds(5);
        while (bus.SubscriberCount(tenant.Id) == 0 && DateTime.UtcNow < deadline) await Task.Delay(20);
        Assert.Equal(1, bus.SubscriberCount(tenant.Id));

        await other.PostJson("/api/v1/metrics/bulk", Body("Noise"));                 // must NOT reach our stream
        var c = await client.CreateCampaign(Flight.Request("Streamed"));            // campaign.changed
        await client.PutJson($"/api/v1/campaigns/{c.Id}/metrics", new MetricsUpsertRequest([new(Flight.Day(1), 1, 1, 1, 1, 1)])); // metrics.upserted
        await CsvImportTests.Upload<ImportCommitResponse>(client, "/api/v1/imports/csv", "g.csv", CsvImportTests.GoogleCsv, HttpStatusCode.Created); // import.completed

        var events = new List<(string Type, JsonElement Data)>();
        var wanted = new HashSet<string> { EventTypes.CampaignChanged, EventTypes.MetricsUpserted, EventTypes.ImportCompleted };
        string? type = null;
        while (wanted.Count > 0 && !cts.IsCancellationRequested)
        {
            var line = await reader.ReadLineAsync(cts.Token);
            if (line is null) break;
            if (line.StartsWith("event: ")) type = line[7..];
            else if (line.StartsWith("data: ") && type is not null)
            {
                var json = JsonDocument.Parse(line[6..]).RootElement;
                events.Add((type, json));
                wanted.Remove(type);
                type = null;
            }
        }
        Assert.Empty(wanted);
        Assert.Contains(events, e => e.Type == EventTypes.Heartbeat);
        var changed = events.First(e => e.Type == EventTypes.CampaignChanged).Data.GetProperty("data");
        Assert.Equal(c.Id, changed.GetProperty("campaignId").GetGuid());
        Assert.DoesNotContain(events, e => e.Type == EventTypes.CampaignChanged && e.Data.GetProperty("data").GetProperty("name").GetString() == "Noise");
        var import = events.First(e => e.Type == EventTypes.ImportCompleted).Data.GetProperty("data");
        Assert.Equal("google-ads", import.GetProperty("detectedFormat").GetString());

        cts.Cancel();
        resp.Dispose();
        deadline = DateTime.UtcNow.AddSeconds(5);
        while (bus.SubscriberCount(tenant.Id) > 0 && DateTime.UtcNow < deadline) await Task.Delay(20);
        Assert.Equal(0, bus.SubscriberCount(tenant.Id)); // subscription cleaned up on disconnect
    }

    [Fact]
    public async Task Stream_accepts_api_key_query_param_for_EventSource_and_rejects_without()
    {
        var created = await _f.SignupAsync();
        using var anon = _f.CreateClient();
        Assert.Equal(HttpStatusCode.Unauthorized, (await anon.GetAsync("/api/v1/stream")).StatusCode);
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        var resp = await anon.GetAsync($"/api/v1/stream?{TenantMiddleware.QueryName}={created.ApiKey}", HttpCompletionOption.ResponseHeadersRead, cts.Token);
        Assert.Equal(HttpStatusCode.OK, resp.StatusCode);
        Assert.Equal("text/event-stream", resp.Content.Headers.ContentType?.MediaType);
        // query param is only honoured on the stream route
        Assert.Equal(HttpStatusCode.Unauthorized, (await anon.GetAsync($"/api/v1/campaigns?{TenantMiddleware.QueryName}={created.ApiKey}")).StatusCode);
        cts.Cancel();
    }

    [Fact]
    public void EventBus_drops_oldest_when_a_subscriber_is_slow_and_isolates_tenants()
    {
        var bus = new EventBus();
        var a = Guid.NewGuid(); var b = Guid.NewGuid();
        using var subA = bus.Subscribe(a, capacity: 2);
        using var subB = bus.Subscribe(b);
        bus.Publish(a, "x", 1); bus.Publish(a, "x", 2); bus.Publish(a, "x", 3);
        bus.Publish(b, "y", 9);
        var gotA = new List<object>(); while (subA.Reader.TryRead(out var e)) gotA.Add(e.Data);
        var gotB = new List<object>(); while (subB.Reader.TryRead(out var e)) gotB.Add(e.Data);
        Assert.Equal([2, 3], gotA.Cast<int>());
        Assert.Equal([9], gotB.Cast<int>());
        Assert.Equal(4, bus.Published);
    }

    // ---- pacing alerts -----------------------------------------------------------------------------------

    [Fact]
    public async Task Pacing_alerts_fire_only_beyond_15_percent_and_stream_as_pacing_alert()
    {
        var (client, tenant) = await _f.NewTenantClientAsync();
        // day 11 of a 30-day flight, 100/day => expected 1100
        async Task<CampaignResponse> Make(string name, decimal spentPerDay)
        {
            var c = await client.CreateCampaign(Flight.Request(name, Channel.Meta, 100m));
            await client.Transition(c.Id, "activate");
            var rows = Enumerable.Range(1, 11).Select(d => new MetricRow(Flight.Day(d), 100, 10, 1, spentPerDay, 0)).ToList();
            await client.PutJson($"/api/v1/campaigns/{c.Id}/metrics", new MetricsUpsertRequest(rows));
            return c;
        }
        var over = await Make("Way over", 130m);     // 130 %
        var under = await Make("Way under", 60m);    // 60 %
        var fine = await Make("Slightly over", 112m); // 112 % -> pacing status 'over' but inside the 15 % alert band
        var draft = await client.CreateCampaign(Flight.Request("Draft", Channel.Meta, 100m)); // never evaluated

        var alerts = await (await client.PostAsync("/api/v1/pacing/evaluate", null)).Json<List<PacingAlert>>();
        Assert.Equal(2, alerts.Count);
        Assert.Contains(alerts, a => a.CampaignId == over.Id && a.Status == "over" && a.PacingPercent == 130m);
        Assert.Contains(alerts, a => a.CampaignId == under.Id && a.Status == "under" && a.PacingPercent == 60m);
        Assert.DoesNotContain(alerts, a => a.CampaignId == fine.Id || a.CampaignId == draft.Id);
        Assert.All(alerts, a => Assert.Contains("Suggested daily budget", a.Message));

        // the daily job walks all tenants and publishes to each tenant's stream
        var bus = _f.Services.GetRequiredService<EventBus>();
        using var sub = bus.Subscribe(tenant.Id);
        var total = await _f.Services.GetRequiredService<PacingAlertService>().RunOnceAsync(CancellationToken.None);
        Assert.True(total >= 2);
        var mine = new List<ServerEvent>(); while (sub.Reader.TryRead(out var e)) mine.Add(e);
        Assert.Equal(2, mine.Count(e => e.Type == EventTypes.PacingAlert));
    }

    // ---- sample dataset + summary ----------------------------------------------------------------------

    [Fact]
    public async Task Sample_dataset_is_public_attributed_and_imports_only_on_request()
    {
        using var anon = _f.CreateClient();
        var info = await (await anon.GetAsync("/api/v1/datasets/sample")).Json<SampleDatasetInfo>();
        Assert.Contains("PDDL", info.License);
        Assert.Contains("kaggle.com", info.Source);
        Assert.Equal(35, info.CampaignDays);
        Assert.Equal(3, info.Campaigns);
        var csv = await anon.GetStringAsync("/api/v1/datasets/sample/download");
        Assert.StartsWith("campaign,date,channel", csv);
        Assert.Equal(HttpStatusCode.Unauthorized, (await anon.PostAsync("/api/v1/datasets/sample/import", null)).StatusCode);

        var (client, _) = await _f.NewTenantClientAsync();
        Assert.Empty(await (await client.GetAsync("/api/v1/campaigns")).Json<List<CampaignResponse>>()); // never auto-loaded
        var r = await client.PostAsync("/api/v1/datasets/sample/import?rebaseDates=true", null);
        Assert.Equal(HttpStatusCode.Created, r.StatusCode);
        var commit = await r.Json<ImportCommitResponse>();
        Assert.Equal("sample-dataset", commit.DetectedFormat);
        Assert.Equal(3, commit.CampaignsCreated);
        Assert.Equal(35, commit.MetricsInserted);
        Assert.Contains(commit.Warnings, w => w.Contains("shifted"));
        var campaigns = await (await client.GetAsync("/api/v1/campaigns")).Json<List<CampaignResponse>>();
        Assert.All(campaigns, c => { Assert.Equal(Channel.Meta, c.Channel); Assert.Equal(CampaignStatus.Active, c.Status); });

        var summary = await (await client.GetAsync("/api/v1/reports/summary")).Json<SummaryReport>();
        Assert.Equal(58705.23m, summary.Lifetime.Spend); // sum of per-day values rounded to cents (raw file: 58,705.21)
        Assert.Equal(38165, summary.Lifetime.Clicks);
        Assert.Equal(1079, summary.Lifetime.Conversions);
        Assert.Equal(0m, summary.Lifetime.Roas); // no invented revenue
        Assert.Equal(3, summary.ActiveCampaigns);
        Assert.Equal(Flight.Today, summary.LastMetricDate);
        Assert.True(summary.Window.Spend > 0);
    }

    [Fact]
    public async Task Summary_compares_window_with_previous_window()
    {
        var (client, _) = await _f.NewTenantClientAsync();
        var c = await client.CreateCampaign(Flight.Request("Trend", Channel.GoogleAds, 100m));
        await client.Transition(c.Id, "activate");
        var rows = Enumerable.Range(1, 10).Select(d => new MetricRow(Flight.Day(d), 100, 10, 1, d <= 5 ? 50m : 150m, 0)).ToList();
        await client.PutJson($"/api/v1/campaigns/{c.Id}/metrics", new MetricsUpsertRequest(rows));
        var from = Flight.Day(6); var to = Flight.Day(10);
        var s = await (await client.GetAsync($"/api/v1/reports/summary?from={from:yyyy-MM-dd}&to={to:yyyy-MM-dd}")).Json<SummaryReport>();
        Assert.Equal(750m, s.Window.Spend);
        Assert.Equal(250m, s.Previous.Spend);
        Assert.Equal(1000m, s.Lifetime.Spend);
        Assert.Equal(1, s.TotalCampaigns);
        Assert.Equal(1, s.PacingOver + s.PacingUnder + s.PacingOnTrack);
    }
}
