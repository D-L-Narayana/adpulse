using System.Text.Json;
using AdPulse.Api.Contracts;
using AdPulse.Api.Data;
using AdPulse.Api.Domain;
using AdPulse.Api.Services;
using AdPulse.Api.Tenancy;

namespace AdPulse.Api.Endpoints;

/// <summary>
/// The one and only sample dataset: a real, PDDL-licensed Facebook campaign export (see Datasets/SOURCE.md).
/// Metadata + download are public; importing it into a workspace needs the workspace key and is never automatic.
/// </summary>
public static class DatasetEndpoints
{
    public const string FileName = "facebook_ad_campaign_daily.csv";
    private static readonly Lazy<string> Csv = new(() => File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Datasets", FileName)));

    public static IEndpointRouteBuilder MapDatasets(this IEndpointRouteBuilder app)
    {
        var g = app.MapGroup("/api/v1/datasets").WithTags("Datasets");

        g.MapGet("/sample", () =>
        {
            var parsed = CsvImport.Parse(Csv.Value);
            return Results.Ok(new SampleDatasetInfo(
                "Facebook Ad Campaign (Bunq, Aug 2017)",
                "https://www.kaggle.com/datasets/madislemsalu/facebook-ad-campaign",
                "ODC Public Domain Dedication and Licence (PDDL) v1.0",
                "https://opendatacommons.org/licenses/pddl/1-0/",
                "Madis Lemsalu / Bunq (Kaggle)",
                "Real paid-social data: 1,143 ad-day rows across 3 Facebook campaigns, aggregated to one row per campaign per day.",
                "generic", parsed.Rows.Count, parsed.Rows.Select(r => r.Campaign).Distinct().Count(),
                parsed.Rows.Min(r => r.Date), parsed.Rows.Max(r => r.Date),
                "/api/v1/datasets/sample/download", "/api/v1/datasets/sample/import",
                parsed.Detection.Headers,
                [
                    "Revenue is not part of the source data, so ROAS is 0 for these campaigns.",
                    "382 raw rows of campaign 1178 have shifted columns in the Kaggle upload; they are re-aligned (see src/AdPulse.Api/Datasets/SOURCE.md).",
                    "Pass ?rebaseDates=true to the import to move the 14-day window so it ends today (clearly recorded in the import audit).",
                ]));
        }).WithSummary("Public: metadata, license and attribution of the sample dataset");

        g.MapGet("/sample/download", () => Results.Text(Csv.Value, "text/csv"))
         .WithSummary("Public: the derived CSV (generic template) – also a handy example of the import format");

        g.MapPost("/sample/import", async (AdPulseDbContext db, MetricsIngest ingest, EventBus bus, TenantContext tenant, bool rebaseDates = false) =>
        {
            var parsed = CsvImport.Parse(Csv.Value);
            var rows = parsed.Rows;
            var warnings = new List<string>(parsed.Warnings);
            if (rebaseDates)
            {
                var shift = DateOnly.FromDateTime(DateTime.UtcNow).DayNumber - rows.Max(r => r.Date).DayNumber;
                rows = rows.Select(r => r with { Date = r.Date.AddDays(shift) }).ToList();
                warnings.Add($"Dates shifted by +{shift} days so the window ends today (original: 2017-08-17..2017-08-30).");
            }
            var result = await ingest.ApplyAsync(rows);
            var audit = new Import
            {
                Id = Guid.NewGuid(), Source = "sample-dataset", FileName = FileName, RowsTotal = parsed.RowsTotal, RowsImported = parsed.RowsTotal - parsed.RowsSkipped, RowsSkipped = parsed.RowsSkipped,
                CampaignsCreated = result.CampaignsCreated, MetricsInserted = result.MetricsInserted, MetricsUpdated = result.MetricsUpdated, WarningsJson = JsonSerializer.Serialize(warnings),
            };
            db.Imports.Add(audit);
            await db.SaveChangesAsync();
            var response = new ImportCommitResponse(audit.Id, FileName, "sample-dataset", "Kaggle: Facebook Ad Campaign (PDDL)", Channel.Meta, null,
                audit.RowsTotal, audit.RowsImported, audit.RowsSkipped, audit.CampaignsCreated, audit.MetricsInserted, audit.MetricsUpdated, result.CampaignIds, warnings, audit.CreatedUtc);
            bus.Publish(tenant.TenantId, EventTypes.ImportCompleted, response);
            return Results.Created($"/api/v1/imports/{audit.Id}", response);
        }).WithSummary("Import the sample dataset into YOUR workspace (explicit action; ?rebaseDates=true ends the window today)");

        return app;
    }
}
