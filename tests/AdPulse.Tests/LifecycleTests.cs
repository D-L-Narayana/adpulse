using System.Net;
using System.Net.Http.Json;
using AdPulse.Api.Contracts;
using AdPulse.Api.Domain;
using AdPulse.Api.Services;

namespace AdPulse.Tests;

/// <summary>Draft -> Active <-> Paused -> Completed. Every (state, action) pair is covered.</summary>
public class LifecycleTests : IClassFixture<AdPulseFactory>
{
    private readonly AdPulseFactory _f;
    public LifecycleTests(AdPulseFactory f) => _f = f;

    /// <summary>Drive a fresh campaign into the requested starting state via the public API.</summary>
    private static async Task<Guid> CampaignIn(HttpClient c, CampaignStatus state)
    {
        var id = (await c.CreateCampaign(Flight.Request($"{state} {Guid.NewGuid():N}"[..30]))).Id;
        if (state is CampaignStatus.Active or CampaignStatus.Paused or CampaignStatus.Completed) await c.Transition(id, "activate");
        if (state is CampaignStatus.Paused) await c.Transition(id, "pause");
        if (state is CampaignStatus.Completed) await c.Transition(id, "complete");
        return id;
    }

    public static IEnumerable<object[]> Matrix()
    {
        foreach (var from in Enum.GetValues<CampaignStatus>())
            foreach (var action in new[] { "activate", "pause", "complete" })
            {
                var to = CampaignLifecycle.Actions[action].To;
                yield return [from, action, CampaignLifecycle.CanTransition(from, to) ? to : null!];
            }
    }

    [Theory]
    [MemberData(nameof(Matrix))]
    public async Task Transition_matrix(CampaignStatus from, string action, CampaignStatus? expected)
    {
        var (c, _) = await _f.NewTenantClientAsync();
        var id = await CampaignIn(c, from);
        var r = await c.PostAsync($"/api/campaigns/{id}/{action}", null);
        if (expected is null)
        {
            Assert.Equal(HttpStatusCode.Conflict, r.StatusCode);
            var p = await r.Problem();
            Assert.Equal("invalid_transition", p.Code);
            Assert.Equal("Conflict", p.Title);
            Assert.Contains(from.ToString(), p.Detail);
            // state untouched
            Assert.Equal(from, (await (await c.GetAsync($"/api/campaigns/{id}")).Json<CampaignResponse>()).Status);
        }
        else
        {
            Assert.Equal(HttpStatusCode.OK, r.StatusCode);
            Assert.Equal(expected, (await r.Json<CampaignResponse>()).Status);
        }
    }

    [Fact]
    public async Task Full_happy_path_draft_to_completed()
    {
        var (c, _) = await _f.NewTenantClientAsync();
        var id = (await c.CreateCampaign()).Id;
        Assert.Equal(CampaignStatus.Active, (await c.Transition(id, "activate")).Status);
        Assert.Equal(CampaignStatus.Paused, (await c.Transition(id, "pause")).Status);
        Assert.Equal(CampaignStatus.Active, (await c.Transition(id, "activate")).Status);
        Assert.Equal(CampaignStatus.Completed, (await c.Transition(id, "complete")).Status);
    }

    [Fact]
    public async Task Cannot_activate_when_flight_already_ended()
    {
        var (c, _) = await _f.NewTenantClientAsync();
        var id = (await c.CreateCampaign(new CampaignRequest("Old Promo", Channel.Meta, 50m, Flight.Today.AddDays(-40), Flight.Today.AddDays(-10)))).Id;
        var r = await c.PostAsync($"/api/campaigns/{id}/activate", null);
        Assert.Equal(HttpStatusCode.UnprocessableEntity, r.StatusCode);
        var p = await r.Problem();
        Assert.Equal("flight_ended", p.Code);
        Assert.Contains("end date", p.Detail);
    }

    [Fact]
    public async Task Problem_response_carries_rfc7807_members_and_extensions()
    {
        var (c, _) = await _f.NewTenantClientAsync();
        var id = (await c.CreateCampaign()).Id;
        var r = await c.PostAsync($"/api/campaigns/{id}/pause", null);
        var body = await r.Content.ReadFromJsonAsync<Dictionary<string, System.Text.Json.JsonElement>>();
        Assert.Equal(Problems.TypeBase + "invalid_transition", body!["type"].GetString());
        Assert.Equal(409, body["status"].GetInt32());
        Assert.Equal("Conflict", body["title"].GetString());
        Assert.Equal("Draft", body["currentStatus"].GetString());
        Assert.Equal("pause", body["action"].GetString());
        Assert.False(string.IsNullOrEmpty(body["traceId"].GetString()));
    }

    [Fact]
    public async Task Unknown_action_is_404()
    {
        var (c, _) = await _f.NewTenantClientAsync();
        var id = (await c.CreateCampaign()).Id;
        Assert.Equal(HttpStatusCode.NotFound, (await c.PostAsync($"/api/campaigns/{id}/explode", null)).StatusCode);
    }
}
