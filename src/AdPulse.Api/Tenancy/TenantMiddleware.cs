using AdPulse.Api.Contracts;
using AdPulse.Api.Data;
using Microsoft.EntityFrameworkCore;
using Serilog.Context;

namespace AdPulse.Api.Tenancy;

/// <summary>
/// Resolves the tenant from the <c>X-Api-Key</c> header. Everything under /api requires a tenant;
/// /health and /swagger are public. The resolved id is pushed into the Serilog LogContext so every
/// log line that reaches Elasticsearch carries <c>TenantId</c>. Failures are RFC 7807 problem+json.
/// </summary>
public sealed class TenantMiddleware(RequestDelegate next)
{
    public const string HeaderName = "X-Api-Key";

    public async Task InvokeAsync(HttpContext http, AdPulseDbContext db, TenantContext ctx)
    {
        if (!http.Request.Path.StartsWithSegments("/api"))
        {
            await next(http);
            return;
        }

        var key = http.Request.Headers[HeaderName].FirstOrDefault();
        if (string.IsNullOrWhiteSpace(key))
        {
            await Problems.Write(http, StatusCodes.Status401Unauthorized, "missing_api_key", $"Header '{HeaderName}' is required.");
            return;
        }

        var tenant = await db.Tenants.AsNoTracking().SingleOrDefaultAsync(t => t.ApiKey == key);
        if (tenant is null)
        {
            await Problems.Write(http, StatusCodes.Status401Unauthorized, "unknown_api_key", "API key is not recognised.");
            return;
        }

        ctx.Set(tenant);
        using (LogContext.PushProperty("TenantId", tenant.Id))
        using (LogContext.PushProperty("TenantName", tenant.Name))
        {
            await next(http);
        }
    }
}
