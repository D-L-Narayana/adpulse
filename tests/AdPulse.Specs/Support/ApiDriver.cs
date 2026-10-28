using System.Net.Http.Json;
using AdPulse.Api.Data;
using AdPulse.Api.Domain;
using AdPulse.Api.Tenancy;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace AdPulse.Specs.Support;

/// <summary>In-process API on a private in-memory SQLite database (or SQL Server when ADPULSE_TEST_PROVIDER=SqlServer).</summary>
public sealed class SpecsFactory : WebApplicationFactory<Program>
{
    private readonly SqliteConnection? _conn;
    private readonly bool _sqlServer = string.Equals(Environment.GetEnvironmentVariable("ADPULSE_TEST_PROVIDER"), "SqlServer", StringComparison.OrdinalIgnoreCase);

    public SpecsFactory()
    {
        if (_sqlServer) return;
        _conn = new SqliteConnection("DataSource=:memory:");
        _conn.Open();
    }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Testing");
        builder.UseSetting("Database:Seed", "false");
        builder.UseSetting("Serilog:MinimumLevel:Default", "Warning");
        builder.UseSetting("Database:Provider", _sqlServer ? "SqlServer" : "Sqlite");
        if (_sqlServer)
        {
            var cs = Environment.GetEnvironmentVariable("ADPULSE_TEST_CONNECTION") ?? throw new InvalidOperationException("ADPULSE_TEST_CONNECTION not set");
            var b = new Microsoft.Data.SqlClient.SqlConnectionStringBuilder(cs) { InitialCatalog = $"adpulse_spec_{Guid.NewGuid():N}"[..40] };
            builder.UseSetting("ConnectionStrings:Default", b.ConnectionString);
        }
        builder.ConfigureServices(services =>
        {
            if (_conn is null) return;
            foreach (var d in services.Where(d => d.ServiceType == typeof(DbContextOptions<AdPulseDbContext>)).ToList()) services.Remove(d);
            services.AddDbContext<AdPulseDbContext>(o => o.UseSqlite(_conn));
        });
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing && _sqlServer)
        {
            using var scope = Services.CreateScope();
            scope.ServiceProvider.GetRequiredService<AdPulseDbContext>().Database.EnsureDeleted();
        }
        base.Dispose(disposing);
        _conn?.Dispose();
    }
}

/// <summary>
/// Scenario-scoped world shared by all step classes (Reqnroll context injection).
/// One API instance per scenario keeps scenarios fully independent.
/// </summary>
public sealed class ApiDriver : IDisposable
{
    private readonly SpecsFactory _factory = new();
    private readonly Dictionary<string, HttpClient> _clients = new();

    private HttpResponseMessage? _last;
    private string? _body;
    public HttpResponseMessage? LastResponse
    {
        get => _last;
        set { _last = value; _body = null; }
    }

    /// <summary>Body is buffered once so multiple Then-steps can inspect the same response.</summary>
    public async Task<string> Body() => _body ??= await LastResponse!.Content.ReadAsStringAsync();
    public Guid CampaignId { get; set; }
    public string CurrentTenant { get; set; } = "";
    public HttpClient Current => Client(CurrentTenant);
    public HttpClient Anonymous => _factory.CreateClient();

    public HttpClient Client(string tenant) => _clients[tenant];

    public async Task AddTenant(string name, string apiKey)
    {
        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AdPulseDbContext>();
            db.Tenants.Add(new Tenant { Id = Guid.NewGuid(), Name = name, ApiKey = apiKey });
            await db.SaveChangesAsync();
        }
        var c = _factory.CreateClient();
        c.DefaultRequestHeaders.Add(TenantMiddleware.HeaderName, apiKey);
        _clients[name] = c;
        if (string.IsNullOrEmpty(CurrentTenant)) CurrentTenant = name;
    }

    private static readonly System.Text.Json.JsonSerializerOptions JsonOpts = new(System.Text.Json.JsonSerializerDefaults.Web);

    public async Task<T> Read<T>() => System.Text.Json.JsonSerializer.Deserialize<T>(await Body(), JsonOpts)!;

    public Task<Dictionary<string, System.Text.Json.JsonElement>> ReadJson() => Read<Dictionary<string, System.Text.Json.JsonElement>>();

    public void Dispose()
    {
        foreach (var c in _clients.Values) c.Dispose();
        _factory.Dispose();
    }
}
