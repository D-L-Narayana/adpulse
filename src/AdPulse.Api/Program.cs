using System.Threading.RateLimiting;
using AdPulse.Api.Contracts;
using AdPulse.Api.Data;
using AdPulse.Api.Endpoints;
using AdPulse.Api.Services;
using AdPulse.Api.Tenancy;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.AspNetCore.RateLimiting;
using Serilog;
using Serilog.Formatting.Compact;
using Serilog.Sinks.Network;

var builder = WebApplication.CreateBuilder(args);

// ---- Logging: structured JSON to console, optional rolling file (Logging:FilePath) and optional TCP to Logstash -> Elasticsearch -> Kibana ----
builder.Host.UseSerilog((ctx, services, cfg) =>
{
    cfg.ReadFrom.Configuration(ctx.Configuration)
       .Enrich.FromLogContext()
       .Enrich.WithProperty("Application", "adpulse-api")
       .Enrich.WithProperty("Environment", ctx.HostingEnvironment.EnvironmentName)
       .WriteTo.Console(new RenderedCompactJsonFormatter());
    var file = ctx.Configuration["Logging:FilePath"]; // e.g. logs/adpulse-.log  -> logs/adpulse-20261028.log
    if (!string.IsNullOrWhiteSpace(file))
        cfg.WriteTo.File(new RenderedCompactJsonFormatter(), file, rollingInterval: RollingInterval.Day, retainedFileCountLimit: 14, shared: true);
    var logstash = ctx.Configuration["Logging:LogstashTcp"]; // e.g. tcp://logstash:5000
    if (!string.IsNullOrWhiteSpace(logstash))
    {
        var uri = new Uri(logstash);
        cfg.WriteTo.TCPSink(uri.Host, uri.Port, textFormatter: new RenderedCompactJsonFormatter());
    }
});

// ---- Data: Database:Provider = SqlServer | Postgres | Sqlite (same EF model, same tests) ----
builder.Services.AddScoped<TenantContext>();
builder.Services.AddAdPulseDatabase(builder.Configuration);
builder.Services.AddScoped<MetricsIngest>();
builder.Services.AddScoped<PacingEvaluator>();
builder.Services.AddSingleton<EventBus>();
builder.Services.AddSingleton<PacingAlertService>();
builder.Services.AddHostedService(sp => sp.GetRequiredService<PacingAlertService>());
builder.Services.Configure<FormOptions>(o => o.MultipartBodyLengthLimit = ImportEndpoints.MaxUploadBytes);

builder.Services.AddProblemDetails();
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen(o =>
{
    o.SwaggerDoc("v1", new()
    {
        Title = "AdPulse API", Version = "v1",
        Description = "Multi-tenant advertising campaign & spend API. POST /api/v1/tenants to get a key, then send X-Api-Key on every /api call. " +
                      "Live updates: GET /api/v1/stream (text/event-stream)."
    });
    o.AddSecurityDefinition("ApiKey", new() { Type = Microsoft.OpenApi.Models.SecuritySchemeType.ApiKey, In = Microsoft.OpenApi.Models.ParameterLocation.Header, Name = TenantMiddleware.HeaderName });
    o.AddSecurityRequirement(new() { { new() { Reference = new() { Type = Microsoft.OpenApi.Models.ReferenceType.SecurityScheme, Id = "ApiKey" } }, Array.Empty<string>() } });
});

// CORS: the Vercel dashboard + local Vite. AllowAnyHeader covers X-Api-Key and Idempotency-Key
builder.Services.AddCors(o => o.AddDefaultPolicy(p => p
    .WithOrigins(builder.Configuration.GetSection("Cors:Origins").Get<string[]>() ?? ["http://localhost:5173", "https://adpulse-dashboard-nu.vercel.app"])
    .SetIsOriginAllowedToAllowWildcardSubdomains()
    .AllowAnyHeader().AllowAnyMethod()
    .WithExposedHeaders("Idempotent-Replayed", "Location")));

// Signup abuse guard: N signups per minute per client IP (tests raise the limit).
var signupPerMinute = builder.Configuration.GetValue("RateLimiting:SignupPerMinute", 20);
builder.Services.AddRateLimiter(o =>
{
    o.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
    o.OnRejected = (ctx, _) => new ValueTask(Problems.Write(ctx.HttpContext, StatusCodes.Status429TooManyRequests, "rate_limited", "Too many signups from this address; try again in a minute."));
    o.AddPolicy("signup", http => RateLimitPartition.GetFixedWindowLimiter(http.Connection.RemoteIpAddress?.ToString() ?? "local",
        _ => new FixedWindowRateLimiterOptions { PermitLimit = signupPerMinute, Window = TimeSpan.FromMinutes(1), QueueLimit = 0 }));
});

builder.Services.AddHealthChecks().AddDbContextCheck<AdPulseDbContext>();

var app = builder.Build();

// Schema: migrations for SQL Server / Postgres, EnsureCreated for SQLite. Demo data ONLY when Seed:Enabled=true (tests, benchmarks).
if (!app.Configuration.GetValue<bool>("Database:SkipInit"))
{
    using var scope = app.Services.CreateScope();
    var db = scope.ServiceProvider.GetRequiredService<AdPulseDbContext>();
    await DatabaseRegistration.InitialiseAsync(db);
    if (app.Configuration.GetValue("Seed:Enabled", false))
        await DbSeeder.SeedAsync(db, app.Configuration.GetValue("Seed:TenantCount", DbSeeder.DefaultTenantCount));
}

app.UseSerilogRequestLogging(o =>
{
    o.EnrichDiagnosticContext = (diag, http) =>
    {
        diag.Set("RequestId", http.TraceIdentifier);
        var tenant = http.RequestServices.GetService<TenantContext>();
        if (tenant?.IsResolved == true) { diag.Set("TenantId", tenant.TenantId); diag.Set("TenantSlug", tenant.Tenant!.Slug); }
    };
});
app.UseExceptionHandler(e => e.Run(http => Problems.Write(http, StatusCodes.Status500InternalServerError, "internal_error", "An unexpected error occurred.")));
app.UseStatusCodePages(); // bare 404/405 from routing also come back as problem+json
app.UseCors();
app.UseRateLimiter();
app.UseSwagger();
app.UseSwaggerUI(o => { o.RoutePrefix = "swagger"; o.DocumentTitle = "AdPulse API"; });
app.UseMiddleware<TenantMiddleware>();

app.MapGet("/", () => Results.Redirect("/swagger")).ExcludeFromDescription();
app.MapHealthChecks("/health");
app.MapGet("/api/v1/health", (EventBus bus, PacingAlertService pacing, IConfiguration cfg) => Results.Ok(new
{
    status = "ok", utc = DateTime.UtcNow, provider = cfg["Database:Provider"], eventsPublished = bus.Published,
    pacing = new { lastRunUtc = pacing.LastRunUtc, lastAlertCount = pacing.LastAlertCount }
})).WithTags("Live").WithSummary("Authenticated health: provider, event counters, last pacing run");

app.MapTenants();
app.MapCampaigns();
app.MapMetrics();
app.MapImports();
app.MapDatasets();
app.MapReports();
app.MapPacing();
app.MapStream();

app.Run();

public partial class Program { }
