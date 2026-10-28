using AdPulse.Api.Data;
using Microsoft.EntityFrameworkCore;
using Serilog.Context;

namespace AdPulse.Api.Tenancy;

/// <summary>
/// Resolves the tenant from the <c>X-Api-Key</c> header. Everything under /api requires a tenant;
/// /health and /swagger are public. The resolved id is pushed into the Serilog LogContext so every
/// log line that reaches Elasticsearch carries <c>TenantId</c>.
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
            await Problem(http, StatusCodes.Status401Unauthorized, "missing_api_key", $"Header '{HeaderName}' is required.");
            return;
        }

        var tenant = await db.Tenants.AsNoTracking().IgnoreQueryFilters().SingleOrDefaultAsync(t => t.ApiKey == key);
        if (tenant is null)
        {
            await Problem(http, StatusCodes.Status401Unauthorized, "unknown_api_key", "API key is not recognised.");
            return;
        }

        ctx.Set(tenant);
        using (LogContext.PushProperty("TenantId", tenant.Id))
        using (LogContext.PushProperty("TenantName", tenant.Name))
        {
            await next(http);
        }
    }

    private static Task Problem(HttpContext http, int status, string code, string detail)
    {
        http.Response.StatusCode = status;
        return http.Response.WriteAsJsonAsync(new { type = code, title = "Unauthorized", status, detail, traceId = http.TraceIdentifier });
    }
}
