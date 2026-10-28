using AdPulse.Api.Data;
using AdPulse.Api.Endpoints;
using AdPulse.Api.Tenancy;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Serilog;
using Serilog.Formatting.Compact;
using Serilog.Sinks.Network;

var builder = WebApplication.CreateBuilder(args);

// ---- Logging: structured JSON to console (+ optional TCP to Logstash -> Elasticsearch -> Kibana) ----
builder.Host.UseSerilog((ctx, services, cfg) =>
{
    cfg.ReadFrom.Configuration(ctx.Configuration)
       .Enrich.FromLogContext()
       .Enrich.WithProperty("Application", "adpulse-api")
       .Enrich.WithProperty("Environment", ctx.HostingEnvironment.EnvironmentName)
       .WriteTo.Console(new RenderedCompactJsonFormatter());
    var logstash = ctx.Configuration["Logging:LogstashTcp"]; // e.g. tcp://logstash:5000
    if (!string.IsNullOrWhiteSpace(logstash))
    {
        var uri = new Uri(logstash);
        cfg.WriteTo.TCPSink(uri.Host, uri.Port, textFormatter: new RenderedCompactJsonFormatter());
    }
});

// ---- Data: SQL Server in prod/CI, SQLite for local dev & fast tests (same EF model, same tests) ----
var provider = builder.Configuration["Database:Provider"] ?? "Sqlite";
var connString = builder.Configuration.GetConnectionString("Default") ?? "Data Source=adpulse.db";
builder.Services.AddScoped<TenantContext>();
builder.Services.AddDbContext<AdPulseDbContext>(o =>
{
    if (provider.Equals("SqlServer", StringComparison.OrdinalIgnoreCase))
        o.UseSqlServer(connString, sql => sql.EnableRetryOnFailure(5));
    else
        o.UseSqlite(connString);
});

builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen(o =>
{
    o.SwaggerDoc("v1", new() { Title = "AdPulse API", Version = "v1", Description = "Multi-tenant advertising campaign & spend API. Send X-Api-Key on every /api call." });
    o.AddSecurityDefinition("ApiKey", new() { Type = Microsoft.OpenApi.Models.SecuritySchemeType.ApiKey, In = Microsoft.OpenApi.Models.ParameterLocation.Header, Name = TenantMiddleware.HeaderName });
    o.AddSecurityRequirement(new() { { new() { Reference = new() { Type = Microsoft.OpenApi.Models.ReferenceType.SecurityScheme, Id = "ApiKey" } }, Array.Empty<string>() } });
});
builder.Services.AddCors(o => o.AddDefaultPolicy(p => p.WithOrigins(builder.Configuration.GetSection("Cors:Origins").Get<string[]>() ?? ["http://localhost:5173"]).AllowAnyHeader().AllowAnyMethod()));
builder.Services.AddHealthChecks().AddDbContextCheck<AdPulseDbContext>();
builder.Services.ConfigureHttpJsonOptions(o => o.SerializerOptions.Converters.Add(new System.Text.Json.Serialization.JsonStringEnumConverter()));

var app = builder.Build();

// Schema + demo data (migrations are applied by the pipeline in production; EnsureCreated keeps dev/test simple).
if (!app.Configuration.GetValue<bool>("Database:SkipInit"))
{
    using var scope = app.Services.CreateScope();
    var db = scope.ServiceProvider.GetRequiredService<AdPulseDbContext>();
    await db.Database.EnsureCreatedAsync();
    if (app.Configuration.GetValue("Database:Seed", true)) await DbSeeder.SeedAsync(db);
}

app.UseSerilogRequestLogging(o =>
{
    o.EnrichDiagnosticContext = (diag, http) =>
    {
        diag.Set("RequestId", http.TraceIdentifier);
        var tenant = http.RequestServices.GetService<TenantContext>();
        if (tenant?.IsResolved == true) diag.Set("TenantId", tenant.TenantId);
    };
});
app.UseExceptionHandler(e => e.Run(async http =>
{
    http.Response.StatusCode = 500;
    await http.Response.WriteAsJsonAsync(new { type = "internal_error", title = "Internal Server Error", status = 500, detail = "Unexpected error.", traceId = http.TraceIdentifier });
}));
app.UseCors();
app.UseSwagger();
app.UseSwaggerUI();
app.UseMiddleware<TenantMiddleware>();

app.MapHealthChecks("/health");
app.MapGet("/api/me", (TenantContext t) => Results.Ok(new { t.Tenant!.Id, t.Tenant.Name })).WithTags("Tenant");
app.MapCampaigns();
app.MapReports();

app.Run();

public partial class Program { }
