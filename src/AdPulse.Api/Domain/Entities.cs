using System.Text.Json.Serialization;

namespace AdPulse.Api.Domain;

[JsonConverter(typeof(JsonStringEnumConverter))]
public enum Channel { GoogleAds = 1, Meta = 2, Amazon = 3, LinkedIn = 4, DV360 = 5, Other = 6 }

/// <summary>Lifecycle: Draft -> Active <-> Paused -> Completed. Completed is terminal.</summary>
[JsonConverter(typeof(JsonStringEnumConverter))]
public enum CampaignStatus { Draft = 0, Active = 1, Paused = 2, Completed = 3 }

/// <summary>
/// An advertiser workspace. Every other row belongs to exactly one tenant. The API key is never stored:
/// only its SHA-256 hash (<see cref="ApiKeyHash"/>) and a short display prefix survive signup.
/// </summary>
public sealed class Tenant
{
    public Guid Id { get; set; }
    public required string Name { get; set; }
    /// <summary>URL-safe, unique handle used by the dashboard (<c>/w/{slug}</c>).</summary>
    public required string Slug { get; set; }
    public string? Email { get; set; }
    /// <summary>Lower-case hex SHA-256 of the API key.</summary>
    public required string ApiKeyHash { get; set; }
    /// <summary>First 8 characters of the key, shown in the UI so a user can recognise which key they hold.</summary>
    public string ApiKeyPrefix { get; set; } = "";
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

/// <summary>Audit row written for every committed CSV / sample-dataset import.</summary>
public sealed class Import : ITenantOwned
{
    public Guid Id { get; set; }
    public Guid TenantId { get; set; }
    /// <summary>google-ads | meta-ads | generic | sample-dataset</summary>
    public required string Source { get; set; }
    public string? FileName { get; set; }
    public int RowsTotal { get; set; }
    public int RowsImported { get; set; }
    public int RowsSkipped { get; set; }
    public int CampaignsCreated { get; set; }
    public int MetricsInserted { get; set; }
    public int MetricsUpdated { get; set; }
    /// <summary>JSON array of human-readable warnings produced while parsing.</summary>
    public string WarningsJson { get; set; } = "[]";
    public DateTime CreatedUtc { get; set; } = DateTime.UtcNow;
}

/// <summary>Stored response for an <c>Idempotency-Key</c> so a retried bulk push replays instead of re-applying.</summary>
public sealed class IdempotencyRecord : ITenantOwned
{
    public long Id { get; set; }
    public Guid TenantId { get; set; }
    public required string Key { get; set; }
    /// <summary>SHA-256 of the request body; a different body under the same key is rejected.</summary>
    public required string RequestHash { get; set; }
    public int StatusCode { get; set; }
    public required string ResponseJson { get; set; }
    public DateTime CreatedUtc { get; set; } = DateTime.UtcNow;
}
