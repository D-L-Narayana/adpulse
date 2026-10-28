using AdPulse.Api.Contracts;
using AdPulse.Api.Data;
using AdPulse.Api.Domain;
using AdPulse.Api.Tenancy;
using Microsoft.EntityFrameworkCore;

namespace AdPulse.Api.Endpoints;

public static class TenantEndpoints
{
    public static IEndpointRouteBuilder MapTenants(this IEndpointRouteBuilder app)
    {
        var g = app.MapGroup("/api/v1/tenants").WithTags("Tenants");

        // Self-serve signup. No password: the API key *is* the credential and is shown exactly once.
        g.MapPost("/", async (AdPulseDbContext db, TenantSignupRequest req, HttpContext http, ILoggerFactory lf) =>
        {
            if (Problems.Validate(http, req) is { } bad) return bad;
            var name = req.Name.Trim();
            var email = req.Email.Trim().ToLowerInvariant();
            var key = ApiKeys.Generate();

            var baseSlug = ApiKeys.Slugify(name);
            var slug = baseSlug;
            for (var attempt = 0; await db.Tenants.AnyAsync(t => t.Slug == slug); attempt++)
            {
                if (attempt > 5) return Problems.Create(http, 409, "slug_unavailable", "Could not allocate a unique workspace slug; try a different name.");
                slug = $"{baseSlug}-{ApiKeys.RandomSuffix()}";
            }

            var tenant = new Tenant { Id = Guid.NewGuid(), Name = name, Slug = slug, Email = email, ApiKeyHash = ApiKeys.Hash(key), ApiKeyPrefix = ApiKeys.DisplayPrefix(key) };
            db.Tenants.Add(tenant);
            await db.SaveChangesAsync();
            lf.CreateLogger("Tenants").LogInformation("tenant.created slug={Slug} id={TenantId}", slug, tenant.Id);
            return Results.Created($"/api/v1/tenants/{tenant.Slug}",
                new TenantCreatedResponse(tenant.Id, tenant.Slug, tenant.Name, email, key, tenant.ApiKeyPrefix, tenant.CreatedUtc, $"/w/{tenant.Slug}"));
        })
        .WithSummary("Create a workspace (tenant). Returns the API key ONCE; only its SHA-256 is stored.")
        .RequireRateLimiting("signup");

        g.MapGet("/me", async (AdPulseDbContext db, TenantContext t) =>
        {
            var tenant = t.Tenant!;
            var campaigns = await db.Campaigns.CountAsync();
            var rows = await db.DailyMetrics.CountAsync();
            var imports = await db.Imports.CountAsync();
            var last = await db.Imports.OrderByDescending(i => i.CreatedUtc).Select(i => (DateTime?)i.CreatedUtc).FirstOrDefaultAsync();
            return Results.Ok(new TenantMeResponse(tenant.Id, tenant.Slug, tenant.Name, tenant.Email, tenant.ApiKeyPrefix, tenant.CreatedUtc, campaigns, rows, imports, last));
        }).WithSummary("Who am I: the tenant behind the X-Api-Key, with row counts");

        return app;
    }
}
