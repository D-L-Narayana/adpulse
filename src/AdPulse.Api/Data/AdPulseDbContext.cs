using AdPulse.Api.Domain;
using AdPulse.Api.Tenancy;
using Microsoft.EntityFrameworkCore;

namespace AdPulse.Api.Data;

/// <summary>
/// Shared-schema multi-tenant context. Isolation is enforced in two places so a forgotten WHERE
/// can never leak data: (1) global query filters on every ITenantOwned entity, (2) SaveChanges stamps
/// TenantId on inserts and refuses writes that target another tenant.
/// </summary>
public sealed class AdPulseDbContext(DbContextOptions<AdPulseDbContext> options, TenantContext tenant) : DbContext(options)
{
    public DbSet<Tenant> Tenants => Set<Tenant>();
    public DbSet<Campaign> Campaigns => Set<Campaign>();
    public DbSet<DailyMetric> DailyMetrics => Set<DailyMetric>();

    protected override void OnModelCreating(ModelBuilder b)
    {
        b.Entity<Tenant>(e =>
        {
            e.Property(x => x.Name).HasMaxLength(120).IsRequired();
            e.Property(x => x.ApiKey).HasMaxLength(64).IsRequired();
            e.HasIndex(x => x.ApiKey).IsUnique();
        });

        b.Entity<Campaign>(e =>
        {
            e.Property(x => x.Name).HasMaxLength(160).IsRequired();
            e.Property(x => x.DailyBudget).HasPrecision(18, 2);
            e.Property(x => x.Channel).HasConversion<string>().HasMaxLength(20);
            e.Property(x => x.Status).HasConversion<string>().HasMaxLength(20);
            e.HasIndex(x => new { x.TenantId, x.Name }).IsUnique();
            e.HasIndex(x => new { x.TenantId, x.Status });
            e.HasMany(x => x.Metrics).WithOne().HasForeignKey(m => m.CampaignId).OnDelete(DeleteBehavior.Cascade);
            e.HasQueryFilter(x => x.TenantId == tenant.TenantId);
        });

        b.Entity<DailyMetric>(e =>
        {
            e.Property(x => x.Spend).HasPrecision(18, 2);
            e.Property(x => x.Revenue).HasPrecision(18, 2);
            e.HasIndex(x => new { x.CampaignId, x.Date }).IsUnique();
            e.HasIndex(x => new { x.TenantId, x.Date });
            e.HasQueryFilter(x => x.TenantId == tenant.TenantId);
        });
    }

    public override Task<int> SaveChangesAsync(CancellationToken ct = default)
    {
        if (tenant.IsResolved)
        {
            foreach (var entry in ChangeTracker.Entries<ITenantOwned>())
            {
                if (entry.State == EntityState.Added)
                    entry.Entity.TenantId = tenant.TenantId;
                else if (entry.State is EntityState.Modified or EntityState.Deleted && entry.Entity.TenantId != tenant.TenantId)
                    throw new InvalidOperationException("Cross-tenant write rejected.");
            }
        }
        return base.SaveChangesAsync(ct);
    }
}
