using AdPulse.Api.Domain;

namespace AdPulse.Api.Tenancy;

/// <summary>Per-request tenant, resolved once by <see cref="TenantMiddleware"/>.</summary>
public sealed class TenantContext
{
    public Tenant? Tenant { get; private set; }
    public Guid TenantId => Tenant?.Id ?? throw new InvalidOperationException("No tenant resolved for this request.");
    public bool IsResolved => Tenant is not null;
    internal void Set(Tenant tenant) => Tenant = tenant;
}
