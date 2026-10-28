using System.ComponentModel.DataAnnotations;
using AdPulse.Api.Domain;

namespace AdPulse.Api.Contracts;

public sealed record CampaignRequest(
    [property: Required, StringLength(160, MinimumLength = 2)] string Name,
    [property: Required] Channel Channel,
    [property: Range(1, 10_000_000)] decimal DailyBudget,
    DateOnly StartDate,
    DateOnly EndDate);

public sealed record CampaignResponse(
    Guid Id, string Name, Channel Channel, CampaignStatus Status, decimal DailyBudget,
    DateOnly StartDate, DateOnly EndDate, DateTime CreatedUtc, DateTime UpdatedUtc)
{
    public static CampaignResponse From(Campaign c) =>
        new(c.Id, c.Name, c.Channel, c.Status, c.DailyBudget, c.StartDate, c.EndDate, c.CreatedUtc, c.UpdatedUtc);
}

public sealed record MetricRow(
    DateOnly Date,
    [property: Range(0, long.MaxValue)] long Impressions,
    [property: Range(0, long.MaxValue)] long Clicks,
    [property: Range(0, long.MaxValue)] long Conversions,
    [property: Range(0, 1_000_000_000)] decimal Spend,
    [property: Range(0, 1_000_000_000)] decimal Revenue);

public sealed record MetricsUpsertRequest([property: Required, MinLength(1)] List<MetricRow> Rows);
public sealed record MetricsUpsertResponse(int Inserted, int Updated);

/// <summary>Cross-channel KPI block; the same shape is reused for per-channel and total rows.</summary>
public sealed record KpiRow(
    string Channel, int Campaigns, long Impressions, long Clicks, long Conversions, decimal Spend, decimal Revenue,
    decimal Ctr, decimal Cpc, decimal Cpa, decimal Roas);

public sealed record CrossChannelReport(DateOnly From, DateOnly To, IReadOnlyList<KpiRow> Channels, KpiRow Total);

public sealed record DailyTrendRow(DateOnly Date, decimal Spend, decimal Revenue, long Clicks, long Conversions);

public sealed record PacingResponse(
    Guid CampaignId, DateOnly AsOf, int DaysElapsed, int DaysTotal, decimal DailyBudget,
    decimal ExpectedSpendToDate, decimal ActualSpendToDate, decimal PacingPercent, string Status,
    decimal Deviation, decimal RemainingBudget, decimal SuggestedDailyBudget);

/// <summary>Per-campaign KPI + pacing summary consumed by the dashboard campaign table.</summary>
public sealed record CampaignSummary(
    Guid Id, string Name, Channel Channel, CampaignStatus Status, decimal DailyBudget, DateOnly StartDate, DateOnly EndDate,
    long Impressions, long Clicks, long Conversions, decimal Spend, decimal Revenue,
    decimal Ctr, decimal Cpc, decimal Cpa, decimal Roas,
    decimal PacingPercent, string PacingStatus, decimal SuggestedDailyBudget);

/// <summary>
/// Client-side view of an RFC 7807 problem response (application/problem+json). Used by tests and
/// documented here so SDK consumers know which fields to expect. <c>Type</c> carries a stable machine-readable
/// error code URI such as <c>https://adpulse.dev/errors/invalid_transition</c>.
/// </summary>
public sealed record ApiError(string Type, string Title, int Status, string Detail, string? TraceId)
{
    public string Code => Type[(Type.LastIndexOf('/') + 1)..];
}
