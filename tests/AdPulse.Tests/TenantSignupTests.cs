using System.Net;
using AdPulse.Api.Contracts;
using AdPulse.Api.Data;
using AdPulse.Api.Tenancy;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace AdPulse.Tests;

/// <summary>Self-serve signup: key shown once, only its hash stored, slug unique, cross-tenant 404s.</summary>
public class TenantSignupTests : IClassFixture<AdPulseFactory>
{
    private readonly AdPulseFactory _f;
    public TenantSignupTests(AdPulseFactory f) => _f = f;

    [Fact]
    public async Task Signup_returns_key_once_and_stores_only_its_hash()
    {
        var created = await _f.SignupAsync("Acme Retail", "ops@acme.test");
        Assert.StartsWith(ApiKeys.Prefix, created.ApiKey);
        Assert.StartsWith("acme-retail", created.Slug);
        Assert.Equal("/w/" + created.Slug, created.WorkspacePath);
        Assert.Equal(created.ApiKey[..12], created.ApiKeyPrefix);

        using var scope = _f.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AdPulseDbContext>();
        var row = await db.Tenants.SingleAsync(t => t.Id == created.Id);
        Assert.Equal(ApiKeys.Hash(created.ApiKey), row.ApiKeyHash);
        Assert.DoesNotContain(created.ApiKey, row.ApiKeyHash);
        Assert.Equal("ops@acme.test", row.Email);
        // the raw key never appears anywhere in the tenants table
        Assert.False(await db.Tenants.AnyAsync(t => t.ApiKeyHash == created.ApiKey || t.ApiKeyPrefix == created.ApiKey));
    }

    [Fact]
    public async Task Same_name_twice_gets_distinct_slugs()
    {
        var a = await _f.SignupAsync("Globex Travel");
        var b = await _f.SignupAsync("Globex Travel");
        Assert.NotEqual(a.Slug, b.Slug);
        Assert.StartsWith("globex-travel", b.Slug);
    }

    [Fact]
    public async Task Me_reflects_the_key_owner_and_row_counts()
    {
        var created = await _f.SignupAsync("Initech");
        var client = _f.ClientFor(created.ApiKey);
        var me = await (await client.GetAsync("/api/v1/tenants/me")).Json<TenantMeResponse>();
        Assert.Equal(created.Id, me.Id);
        Assert.Equal(0, me.Campaigns);
        await client.CreateCampaign();
        me = await (await client.GetAsync("/api/v1/tenants/me")).Json<TenantMeResponse>();
        Assert.Equal(1, me.Campaigns);
        Assert.Equal(created.ApiKeyPrefix, me.ApiKeyPrefix);
    }

    [Theory]
    [InlineData("A", "ok@example.test", "Name")]
    [InlineData("Valid Name", "not-an-email", "Email")]
    [InlineData("", "", "Name")]
    public async Task Invalid_signup_is_422_problem(string name, string email, string field)
    {
        using var anon = _f.CreateClient();
        var r = await anon.PostJson("/api/v1/tenants", new TenantSignupRequest(name, email));
        Assert.Equal(HttpStatusCode.UnprocessableEntity, r.StatusCode);
        var body = await r.Content.ReadAsStringAsync();
        Assert.Contains("validation_error", body);
        Assert.Contains(field, body);
    }

    [Fact]
    public async Task Wrong_or_missing_key_is_401_and_signup_needs_no_key()
    {
        using var anon = _f.CreateClient();
        Assert.Equal(HttpStatusCode.Unauthorized, (await anon.GetAsync("/api/v1/tenants/me")).StatusCode);
        var bad = _f.ClientFor(ApiKeys.Generate());
        var r = await bad.GetAsync("/api/v1/campaigns");
        Assert.Equal(HttpStatusCode.Unauthorized, r.StatusCode);
        Assert.Equal("unknown_api_key", (await r.Problem()).Code);
        Assert.Equal(HttpStatusCode.OK, (await anon.GetAsync("/api/v1/datasets/sample")).StatusCode); // public
    }

    [Fact]
    public async Task Two_signed_up_tenants_cannot_see_each_other()
    {
        var (a, _) = await _f.NewTenantClientAsync();
        var (b, _) = await _f.NewTenantClientAsync();
        var ca = await a.CreateCampaign(Flight.Request("Only A"));
        var cb = await b.CreateCampaign(Flight.Request("Only A")); // same name is fine across tenants
        Assert.NotEqual(ca.Id, cb.Id);
        Assert.Equal(HttpStatusCode.NotFound, (await b.GetAsync($"/api/v1/campaigns/{ca.Id}")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await a.GetAsync($"/api/v1/campaigns/{cb.Id}")).StatusCode);
        var listA = await (await a.GetAsync("/api/v1/campaigns")).Json<List<CampaignResponse>>();
        Assert.Single(listA);
        Assert.Equal(ca.Id, listA[0].Id);
    }

    [Fact]
    public void Slugify_is_url_safe()
    {
        Assert.Equal("acme-retail", ApiKeys.Slugify("  Acme   Retail! "));
        Assert.Equal("workspace", ApiKeys.Slugify("★★★"));
        Assert.Equal(40, ApiKeys.Slugify(new string('a', 100)).Length);
        Assert.Equal(64, ApiKeys.Hash("x").Length);
    }
}
