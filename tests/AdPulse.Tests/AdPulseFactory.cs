using System.Net.Http.Json;
using AdPulse.Api.Contracts;
using AdPulse.Api.Data;
using AdPulse.Api.Domain;
using AdPulse.Api.Tenancy;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace AdPulse.Tests;

/// <summary>
/// Boots the real API in-process. Provider is chosen by ADPULSE_TEST_PROVIDER:
///   (unset) / Sqlite  -> private in-memory SQLite per factory (fast, no infra)
///   SqlServer         -> ADPULSE_TEST_CONNECTION (CI runs the same tests against a SQL Server 2022 container)
/// </summary>
public class AdPulseFactory : WebApplicationFactory<Program>
{
    private readonly SqliteConnection? _conn;
    public bool IsSqlServer { get; }
    protected virtual bool Seed => false;
    protected virtual int SeedTenantCount => 0;

    public AdPulseFactory()
    {
        IsSqlServer = string.Equals(Environment.GetEnvironmentVariable("ADPULSE_TEST_PROVIDER"), "SqlServer", StringComparison.OrdinalIgnoreCase);
        if (!IsSqlServer)
        {
            _conn = new SqliteConnection("DataSource=:memory:");
            _conn.Open();
        }
    }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Testing");
        builder.UseSetting("Database:Seed", Seed ? "true" : "false");
        builder.UseSetting("Database:SeedTenantCount", SeedTenantCount.ToString());
        builder.UseSetting("Database:Provider", IsSqlServer ? "SqlServer" : "Sqlite");
        builder.UseSetting("Serilog:MinimumLevel:Default", "Warning");
        if (IsSqlServer)
        {
            var cs = Environment.GetEnvironmentVariable("ADPULSE_TEST_CONNECTION") ?? throw new InvalidOperationException("ADPULSE_TEST_CONNECTION not set");
            // Every factory gets its own database so parallel test classes never share rows.
            var b = new Microsoft.Data.SqlClient.SqlConnectionStringBuilder(cs) { InitialCatalog = $"adpulse_test_{Guid.NewGuid():N}"[..40] };
            builder.UseSetting("ConnectionStrings:Default", b.ConnectionString);
        }

        builder.ConfigureServices(services =>
        {
            if (_conn is null) return;
            services.RemoveAll(typeof(DbContextOptions<AdPulseDbContext>));
            services.AddDbContext<AdPulseDbContext>(o => o.UseSqlite(_conn));
        });
    }

    /// <summary>Creates a fresh tenant and returns a client that always sends its API key.</summary>
    public async Task<(HttpClient Client, Tenant Tenant)> NewTenantClientAsync(string? name = null)
    {
        var tenant = new Tenant { Id = Guid.NewGuid(), Name = name ?? $"Tenant {Guid.NewGuid():N}"[..20], ApiKey = Guid.NewGuid().ToString("N") };
        using (var scope = Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AdPulseDbContext>();
            db.Tenants.Add(tenant);
            await db.SaveChangesAsync();
        }
        return (ClientFor(tenant.ApiKey), tenant);
    }

    public HttpClient ClientFor(string apiKey)
    {
        var client = CreateClient();
        client.DefaultRequestHeaders.Add(TenantMiddleware.HeaderName, apiKey);
        return client;
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing && IsSqlServer)
        {
            using var scope = Services.CreateScope();
            scope.ServiceProvider.GetRequiredService<AdPulseDbContext>().Database.EnsureDeleted();
        }
        base.Dispose(disposing);
        _conn?.Dispose();
    }
}

/// <summary>Factory variant that runs the production seeder (500 synthetic tenants + 2 demo tenants).</summary>
public sealed class SeededFactory : AdPulseFactory
{
    protected override bool Seed => true;
    protected override int SeedTenantCount => DbSeeder.DefaultTenantCount;
}

/// <summary>Flight dates relative to "today" so the activate end-date guard and pacing behave deterministically.</summary>
internal static class Flight
{
    public static readonly DateOnly Today = DateOnly.FromDateTime(DateTime.UtcNow);
    public static readonly DateOnly Start = Today.AddDays(-10);
    public static readonly DateOnly End = Today.AddDays(19); // 30-day flight, day 11 today
    public static DateOnly Day(int n) => Start.AddDays(n - 1); // 1-based flight day
    public static CampaignRequest Request(string name = "Brand Search", Channel ch = Channel.GoogleAds, decimal budget = 250m) =>
        new(name, ch, budget, Start, End);
}

internal static class ServiceCollectionExt
{
    public static void RemoveAll(this IServiceCollection services, Type serviceType)
    {
        foreach (var d in services.Where(d => d.ServiceType == serviceType).ToList()) services.Remove(d);
    }
}

internal static class Http
{
    public static Task<HttpResponseMessage> PostJson<T>(this HttpClient c, string url, T body) => c.PostAsJsonAsync(url, body);
    public static Task<HttpResponseMessage> PutJson<T>(this HttpClient c, string url, T body) => c.PutAsJsonAsync(url, body);
    public static async Task<T> Json<T>(this HttpResponseMessage r) => (await r.Content.ReadFromJsonAsync<T>())!;
    public static async Task<ApiError> Problem(this HttpResponseMessage r)
    {
        Assert.Equal("application/problem+json", r.Content.Headers.ContentType?.MediaType);
        return await r.Json<ApiError>();
    }

    public static async Task<CampaignResponse> CreateCampaign(this HttpClient c, CampaignRequest? req = null)
    {
        var r = await c.PostJson("/api/campaigns", req ?? Flight.Request());
        Assert.Equal(System.Net.HttpStatusCode.Created, r.StatusCode);
        return await r.Json<CampaignResponse>();
    }

    public static async Task<CampaignResponse> Transition(this HttpClient c, Guid id, string action)
    {
        var r = await c.PostAsync($"/api/campaigns/{id}/{action}", null);
        Assert.Equal(System.Net.HttpStatusCode.OK, r.StatusCode);
        return await r.Json<CampaignResponse>();
    }
}
