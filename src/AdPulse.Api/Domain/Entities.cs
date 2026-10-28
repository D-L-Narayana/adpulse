namespace AdPulse.Api.Domain;

public enum Channel { GoogleAds = 1, Meta = 2, Amazon = 3, LinkedIn = 4, DV360 = 5 }
public enum CampaignStatus { Draft = 0, Active = 1, Paused = 2, Ended = 3 }

/// <summary>An advertiser account. Every other row belongs to exactly one tenant.</summary>
public sealed class Tenant
{
    public Guid Id { get; set; }
    public required string Name { get; set; }
    public required string ApiKey { get; set; }
    public DateTime CreatedUtc { get; set; } = DateTime.UtcNow;
}

public interface ITenantOwned { Guid TenantId { get; set; } }

public sealed class Campaign : ITenantOwned
{
    public Guid Id { get; set; }
    public Guid TenantId { get; set; }
    public required string Name { get; set; }
    public Channel Channel { get; set; }
    public CampaignStatus Status { get; set; } = CampaignStatus.Draft;
    public decimal DailyBudget { get; set; }
    public DateOnly StartDate { get; set; }
    public DateOnly EndDate { get; set; }
    public DateTime CreatedUtc { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedUtc { get; set; } = DateTime.UtcNow;
    public List<DailyMetric> Metrics { get; set; } = new();
}

/// <summary>One row per campaign per day, upserted from channel reports.</summary>
public sealed class DailyMetric : ITenantOwned
{
    public long Id { get; set; }
    public Guid TenantId { get; set; }
    public Guid CampaignId { get; set; }
    public DateOnly Date { get; set; }
    public long Impressions { get; set; }
    public long Clicks { get; set; }
    public long Conversions { get; set; }
    public decimal Spend { get; set; }
    public decimal Revenue { get; set; }
}
