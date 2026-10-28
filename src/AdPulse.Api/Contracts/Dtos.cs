using System.ComponentModel.DataAnnotations;
using AdPulse.Api.Domain;

namespace AdPulse.Api.Contracts;

// ---- tenants ------------------------------------------------------------------------------------------------

public sealed record TenantSignupRequest(
    [property: Required, StringLength(120, MinimumLength = 2)] string Name,
    [property: Required, EmailAddress, StringLength(254)] string Email);

/// <summary>Returned exactly once: the API key cannot be recovered afterwards (only its hash is stored).</summary>
public sealed record TenantCreatedResponse(Guid Id, string Slug, string Name, string Email, string ApiKey, string ApiKeyPrefix, DateTime CreatedUtc, string WorkspacePath);

public sealed record TenantMeResponse(Guid Id, string Slug, string Name, string? Email, string ApiKeyPrefix, DateTime CreatedUtc, int Campaigns, int MetricRows, int Imports, DateTime? LastImportUtc);

// ---- campaigns ----------------------------------------------------------------------------------------------

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

// ---- bulk ingest --------------------------------------------------------------------------------------------

/// <summary>One campaign-day. Identify the campaign by <c>campaign</c> (name; created if unknown) or <c>campaignId</c>.</summary>
public sealed record BulkMetricRow(
    [property: StringLength(160)] string? Campaign,
    Guid? CampaignId,
    Channel? Channel,
    DateOnly Date,
    [property: Range(0, long.MaxValue)] long Impressions,
    [property: Range(0, long.MaxValue)] long Clicks,
    [property: Range(0, long.MaxValue)] long Conversions,
    [property: Range(0, 1_000_000_000)] decimal Spend,
    [property: Range(0, 1_000_000_000)] decimal Revenue);

public sealed record BulkMetricsRequest([property: Required, MinLength(1), MaxLength(10_000)] List<BulkMetricRow> Rows);

public sealed record BulkMetricsResponse(int RowsReceived, int CampaignsCreated, int MetricsInserted, int MetricsUpdated, IReadOnlyList<Guid> CampaignIds, bool Replayed, string? IdempotencyKey);

// ---- csv import ---------------------------------------------------------------------------------------------

public sealed record ImportPreviewRow(string Campaign, Channel Channel, DateOnly Date, long Impressions, long Clicks, long Conversions, decimal Spend, decimal Revenue);

public sealed record ImportPreviewResponse(
    bool DryRun, string FileName, string DetectedFormat, string DetectedFormatLabel, Channel Channel, string? Currency,
    IReadOnlyDictionary<string, string?> ColumnMapping, IReadOnlyList<string> Headers,
    int RowsTotal, int RowsSkipped, int CampaignDays, IReadOnlyList<ImportPreviewRow> Preview, IReadOnlyList<string> Warnings);

public sealed record ImportCommitResponse(
    Guid ImportId, string FileName, string DetectedFormat, string DetectedFormatLabel, Channel Channel, string? Currency,
    int RowsTotal, int RowsImported, int RowsSkipped, int CampaignsCreated, int MetricsInserted, int MetricsUpdated,
    IReadOnlyList<Guid> CampaignIds, IReadOnlyList<string> Warnings, DateTime CreatedUtc);

public sealed record ImportHistoryRow(Guid Id, string Source, string? FileName, int RowsTotal, int RowsImported, int RowsSkipped, int CampaignsCreated, int MetricsInserted, int MetricsUpdated, IReadOnlyList<string> Warnings, DateTime CreatedUtc);

// ---- sample dataset -----------------------------------------------------------------------------------------

public sealed record SampleDatasetInfo(string Name, string Source, string License, string LicenseUrl, string Publisher, string Description, string Format, int CampaignDays, int Campaigns, DateOnly From, DateOnly To, string DownloadPath, string ImportPath, IReadOnlyList<string> Columns, IReadOnlyList<string> Notes);

// ---- reports ------------------------------------------------------------------------------------------------

/// <summary>Cross-channel KPI block; the same shape is reused for per-channel and total rows.</summary>
public sealed record KpiRow(
    string Channel, int Campaigns, long Impressions, long Clicks, long Conversions, decimal Spend, decimal Revenue,
    decimal Ctr, decimal Cpc, decimal Cpa, decimal Roas);

public sealed record CrossChannelReport(DateOnly From, DateOnly To, IReadOnlyList<KpiRow> Channels, KpiRow Total);

public sealed record DailyTrendRow(DateOnly Date, decimal Spend, decimal Revenue, long Clicks, long Conversions);

/// <summary>Headline numbers for the dashboard: lifetime totals, the selected window and the previous window of equal length.</summary>
public sealed record SummaryReport(DateOnly From, DateOnly To, KpiRow Window, KpiRow Previous, KpiRow Lifetime, int ActiveCampaigns, int TotalCampaigns, int PacingOver, int PacingUnder, int PacingOnTrack, DateOnly? LastMetricDate);

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
