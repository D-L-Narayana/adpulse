using AdPulse.Api.Contracts;
using AdPulse.Api.Data;
using Microsoft.EntityFrameworkCore;
using Serilog.Context;

namespace AdPulse.Api.Tenancy;

/// <summary>
/// Resolves the tenant from the <c>X-Api-Key</c> header (or, for <c>EventSource</c> clients that cannot set
/// headers, the <c>api_key</c> query parameter on <c>/api/v1/stream</c>). Everything under /api requires a tenant
/// except the public signup + sample-dataset routes; /health and /swagger are public. The key is hashed and
/// compared with the stored SHA-256 so the database never holds a usable secret. The resolved id is pushed into
/// the Serilog LogContext so every log line carries <c>TenantId</c>. Failures are RFC 7807 problem+json.
/// </summary>
public sealed class TenantMiddleware(RequestDelegate next)
{
    public const string HeaderName = "X-Api-Key";
    public const string QueryName = "api_key";

    private static readonly (string Method, string Path)[] PublicRoutes =
    [
        ("POST", "/api/v1/tenants"),
        ("GET", "/api/v1/datasets/sample"),
        ("GET", "/api/v1/datasets/sample/download"),
    ];

    public async Task InvokeAsync(HttpContext http, AdPulseDbContext db, TenantContext ctx)
    {
        var path = http.Request.Path;
        if (!path.StartsWithSegments("/api") || IsPublic(http.Request.Method, path) || HttpMethods.IsOptions(http.Request.Method))
        {
            await next(http);
            return;
        }

        var key = http.Request.Headers[HeaderName].FirstOrDefault();
        if (string.IsNullOrWhiteSpace(key) && path.StartsWithSegments("/api/v1/stream"))
            key = http.Request.Query[QueryName].FirstOrDefault();
        if (string.IsNullOrWhiteSpace(key))
        {
            await Problems.Write(http, StatusCodes.Status401Unauthorized, "missing_api_key", $"Header '{HeaderName}' is required.");
            return;
        }

        var hash = ApiKeys.Hash(key);
        var tenant = await db.Tenants.AsNoTracking().SingleOrDefaultAsync(t => t.ApiKeyHash == hash);
        if (tenant is null)
        {
            await Problems.Write(http, StatusCodes.Status401Unauthorized, "unknown_api_key", "API key is not recognised.");
            return;
        }

        ctx.Set(tenant);
        using (LogContext.PushProperty("TenantId", tenant.Id))
        using (LogContext.PushProperty("TenantSlug", tenant.Slug))
        {
            await next(http);
        }
    }

    private static bool IsPublic(string method, PathString path) =>
        PublicRoutes.Any(r => r.Method == method && path.Equals(r.Path, StringComparison.OrdinalIgnoreCase));
}
