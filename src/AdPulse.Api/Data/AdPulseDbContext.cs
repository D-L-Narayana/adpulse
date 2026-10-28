using AdPulse.Api.Domain;
using AdPulse.Api.Tenancy;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace AdPulse.Api.Data;

/// <summary>
/// Shared-schema multi-tenant context. Isolation is enforced in two places so a forgotten WHERE
/// can never leak data: (1) global query filters on every ITenantOwned entity, (2) SaveChanges stamps
/// TenantId on inserts and refuses writes that target another tenant.
///
/// The same model runs on SQL Server, PostgreSQL (schema <c>adpulse</c>) and SQLite. Provider-specific
/// subclasses (<see cref="SqlServerDbContext"/>, <see cref="PostgresDbContext"/>) exist only so that
/// each provider owns its own migrations folder; DI always resolves <see cref="AdPulseDbContext"/>.
/// </summary>
public class AdPulseDbContext(DbContextOptions options, TenantContext tenant) : DbContext(options)
{
    public const string Schema = "adpulse";

    public DbSet<Tenant> Tenants => Set<Tenant>();
    public DbSet<Campaign> Campaigns => Set<Campaign>();
    public DbSet<DailyMetric> DailyMetrics => Set<DailyMetric>();
    public DbSet<Import> Imports => Set<Import>();
    public DbSet<IdempotencyRecord> IdempotencyRecords => Set<IdempotencyRecord>();

    protected override void OnModelCreating(ModelBuilder b)
    {
        // Supabase is shared with another product: everything AdPulse owns lives in its own schema.
        if (Database.IsNpgsql()) b.HasDefaultSchema(Schema);

        b.Entity<Tenant>(e =>
        {
            e.Property(x => x.Name).HasMaxLength(120).IsRequired();
            e.Property(x => x.Slug).HasMaxLength(80).IsRequired();
            e.Property(x => x.Email).HasMaxLength(254);
            e.Property(x => x.ApiKeyHash).HasMaxLength(64).IsRequired();
            e.Property(x => x.ApiKeyPrefix).HasMaxLength(12).IsRequired();
            e.HasIndex(x => x.ApiKeyHash).IsUnique();
            e.HasIndex(x => x.Slug).IsUnique();
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

        b.Entity<Import>(e =>
        {
            e.Property(x => x.Source).HasMaxLength(32).IsRequired();
            e.Property(x => x.FileName).HasMaxLength(260);
            e.HasIndex(x => new { x.TenantId, x.CreatedUtc });
            e.HasQueryFilter(x => x.TenantId == tenant.TenantId);
        });

        b.Entity<IdempotencyRecord>(e =>
        {
            e.Property(x => x.Key).HasMaxLength(128).IsRequired();
            e.Property(x => x.RequestHash).HasMaxLength(64).IsRequired();
            e.HasIndex(x => new { x.TenantId, x.Key }).IsUnique();
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

/// <summary>SQL Server flavour – owns <c>Migrations/SqlServer</c>.</summary>
public sealed class SqlServerDbContext(DbContextOptions<SqlServerDbContext> options, TenantContext tenant) : AdPulseDbContext(options, tenant);

/// <summary>PostgreSQL flavour (schema <c>adpulse</c>) – owns <c>Migrations/Postgres</c>.</summary>
public sealed class PostgresDbContext(DbContextOptions<PostgresDbContext> options, TenantContext tenant) : AdPulseDbContext(options, tenant);

/// <summary>
/// Chooses the provider from <c>Database:Provider</c> (SqlServer | Postgres | Sqlite) and registers the matching
/// context so that <c>AdPulseDbContext</c> resolves to the provider-specific subclass (and therefore its migrations).
/// </summary>
public static class DatabaseRegistration
{
    public static IServiceCollection AddAdPulseDatabase(this IServiceCollection services, IConfiguration config)
    {
        var provider = (config["Database:Provider"] ?? "Sqlite").Trim();
        var cs = config.GetConnectionString("Default") ?? "Data Source=adpulse.db";
        switch (provider.ToLowerInvariant())
        {
            case "sqlserver":
                services.AddDbContext<AdPulseDbContext, SqlServerDbContext>(o => o.UseSqlServer(cs, sql => sql
                    .EnableRetryOnFailure(5)
                    .MigrationsHistoryTable("__EFMigrationsHistory")));
                break;
            case "postgres":
            case "postgresql":
            case "npgsql":
                services.AddDbContext<AdPulseDbContext, PostgresDbContext>(o => o.UseNpgsql(cs, pg => pg
                    .EnableRetryOnFailure(5)
                    .MigrationsHistoryTable("__EFMigrationsHistory", AdPulseDbContext.Schema)));
                break;
            case "sqlite":
                services.AddDbContext<AdPulseDbContext>(o => o.UseSqlite(cs));
                break;
            default:
                throw new InvalidOperationException($"Unknown Database:Provider '{provider}'. Use SqlServer, Postgres or Sqlite.");
        }
        return services;
    }

    /// <summary>Migrations for SQL Server / Postgres; SQLite (dev + fast tests) has no migrations and uses EnsureCreated.</summary>
    public static async Task InitialiseAsync(AdPulseDbContext db, CancellationToken ct = default)
    {
        if (db.Database.IsSqlite()) await db.Database.EnsureCreatedAsync(ct);
        else await db.Database.MigrateAsync(ct);
    }
}

// ---- design-time factories so `dotnet ef migrations add X --context PostgresDbContext -o Migrations/Postgres` works offline ----
public sealed class SqlServerDesignTimeFactory : IDesignTimeDbContextFactory<SqlServerDbContext>
{
    public SqlServerDbContext CreateDbContext(string[] args) => new(
        new DbContextOptionsBuilder<SqlServerDbContext>().UseSqlServer(Environment.GetEnvironmentVariable("ADPULSE_SQLSERVER") ?? "Server=localhost;Database=adpulse;Trusted_Connection=True;TrustServerCertificate=True").Options,
        new TenantContext());
}

public sealed class PostgresDesignTimeFactory : IDesignTimeDbContextFactory<PostgresDbContext>
{
    public PostgresDbContext CreateDbContext(string[] args) => new(
        new DbContextOptionsBuilder<PostgresDbContext>().UseNpgsql(Environment.GetEnvironmentVariable("ADPULSE_POSTGRES") ?? "Host=localhost;Database=postgres;Username=postgres;Password=postgres",
            pg => pg.MigrationsHistoryTable("__EFMigrationsHistory", AdPulseDbContext.Schema)).Options,
        new TenantContext());
}
