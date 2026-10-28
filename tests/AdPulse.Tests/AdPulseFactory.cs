using System.Net.Http.Json;
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
///   SqlServer         -> ADPULSE_TEST_CONNECTION (CI runs the same tests against a SQL Server container)
/// </summary>
public sealed class AdPulseFactory : WebApplicationFactory<Program>
{
    private readonly SqliteConnection? _conn;
    public bool IsSqlServer { get; }

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
        builder.UseSetting("Database:Seed", "false");
        builder.UseSetting("Database:Provider", IsSqlServer ? "SqlServer" : "Sqlite");
        if (IsSqlServer)
            builder.UseSetting("ConnectionStrings:Default", Environment.GetEnvironmentVariable("ADPULSE_TEST_CONNECTION") ?? throw new InvalidOperationException("ADPULSE_TEST_CONNECTION not set"));

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
        var client = CreateClient();
        client.DefaultRequestHeaders.Add(TenantMiddleware.HeaderName, tenant.ApiKey);
        return (client, tenant);
    }

    protected override void Dispose(bool disposing)
    {
        base.Dispose(disposing);
        _conn?.Dispose();
    }
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
}
