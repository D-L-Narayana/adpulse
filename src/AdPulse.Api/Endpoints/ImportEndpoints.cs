using System.Text;
using System.Text.Json;
using AdPulse.Api.Contracts;
using AdPulse.Api.Data;
using AdPulse.Api.Domain;
using AdPulse.Api.Services;
using AdPulse.Api.Tenancy;
using Microsoft.EntityFrameworkCore;

namespace AdPulse.Api.Endpoints;

public static class ImportEndpoints
{
    public const long MaxUploadBytes = 25 * 1024 * 1024;

    public static IEndpointRouteBuilder MapImports(this IEndpointRouteBuilder app)
    {
        var g = app.MapGroup("/api/v1/imports").WithTags("Imports");

        // multipart/form-data: file=<csv> [channel=GoogleAds|Meta|...] ; ?dryRun=true previews without writing.
        g.MapPost("/csv", async (HttpContext http, AdPulseDbContext db, MetricsIngest ingest, EventBus bus, TenantContext tenant, bool dryRun = false, Channel? channel = null) =>
        {
            if (!http.Request.HasFormContentType) return Problems.Create(http, 415, "unsupported_media_type", "Send multipart/form-data with a 'file' part.");
            var form = await http.Request.ReadFormAsync();
            var file = form.Files.GetFile("file") ?? form.Files.FirstOrDefault();
            if (file is null || file.Length == 0) return Problems.Create(http, 422, "missing_file", "A non-empty CSV file part named 'file' is required.");
            if (file.Length > MaxUploadBytes) return Problems.Create(http, 413, "file_too_large", $"CSV must be <= {MaxUploadBytes / 1024 / 1024} MB.");
            if (channel is null && Enum.TryParse<Channel>(form["channel"].FirstOrDefault(), true, out var formChannel)) channel = formChannel;

            string text;
            using (var reader = new StreamReader(file.OpenReadStream(), Encoding.UTF8, detectEncodingFromByteOrderMarks: true))
                text = await reader.ReadToEndAsync();

            var parsed = CsvImport.Parse(text, channel);
            if (parsed.Detection.Format == "unknown")
                return Problems.Create(http, 422, "unrecognised_csv", parsed.Warnings.FirstOrDefault() ?? "Could not find a header row.",
                    new Dictionary<string, object?> { ["expected"] = "Google Ads export (Campaign, Day, Cost, Impressions, Clicks, Conversions), Meta Ads Manager export (Campaign name, Day, Amount spent, Impressions, Link clicks, Results) or generic (campaign, date, impressions, clicks, conversions, spend, revenue)" });

            if (dryRun)
                return Results.Ok(Preview(file.FileName, parsed));

            if (parsed.Rows.Count == 0)
                return Problems.Create(http, 422, "no_rows", "The file was recognised but contained no importable rows.", new Dictionary<string, object?> { ["warnings"] = parsed.Warnings });

            var result = await ingest.ApplyAsync(parsed.Rows);
            var audit = new Import
            {
                Id = Guid.NewGuid(), Source = parsed.Detection.Format, FileName = Path.GetFileName(file.FileName), RowsTotal = parsed.RowsTotal,
                RowsImported = parsed.RowsTotal - parsed.RowsSkipped, RowsSkipped = parsed.RowsSkipped, CampaignsCreated = result.CampaignsCreated,
                MetricsInserted = result.MetricsInserted, MetricsUpdated = result.MetricsUpdated, WarningsJson = JsonSerializer.Serialize(parsed.Warnings),
            };
            db.Imports.Add(audit);
            await db.SaveChangesAsync();
            var response = new ImportCommitResponse(audit.Id, audit.FileName!, parsed.Detection.Format, parsed.Detection.FormatLabel, parsed.Detection.Channel, parsed.Detection.Currency,
                audit.RowsTotal, audit.RowsImported, audit.RowsSkipped, audit.CampaignsCreated, audit.MetricsInserted, audit.MetricsUpdated, result.CampaignIds, parsed.Warnings, audit.CreatedUtc);
            bus.Publish(tenant.TenantId, EventTypes.ImportCompleted, response);
            return Results.Created($"/api/v1/imports/{audit.Id}", response);
        })
        .DisableAntiforgery()
        .Accepts<IFormFile>("multipart/form-data")
        .WithSummary("Import a Google Ads / Meta Ads Manager / generic CSV. ?dryRun=true returns detection + mapping + first 10 rows.");

        g.MapGet("/", async (AdPulseDbContext db) =>
        {
            var rows = await db.Imports.AsNoTracking().OrderByDescending(i => i.CreatedUtc).Take(50).ToListAsync();
            return Results.Ok(rows.Select(ToHistory));
        }).WithSummary("Last 50 imports for this tenant");

        g.MapGet("/{id:guid}", async (AdPulseDbContext db, Guid id) =>
        {
            var i = await db.Imports.AsNoTracking().SingleOrDefaultAsync(x => x.Id == id);
            return i is null ? Results.NotFound() : Results.Ok(ToHistory(i));
        });

        return app;
    }

    public static ImportPreviewResponse Preview(string fileName, CsvParseResult parsed) => new(
        true, Path.GetFileName(fileName), parsed.Detection.Format, parsed.Detection.FormatLabel, parsed.Detection.Channel, parsed.Detection.Currency,
        parsed.Detection.ColumnMapping, parsed.Detection.Headers, parsed.RowsTotal, parsed.RowsSkipped, parsed.Rows.Count,
        parsed.Rows.Take(10).Select(r => new ImportPreviewRow(r.Campaign, r.Channel, r.Date, r.Impressions, r.Clicks, r.Conversions, r.Spend, r.Revenue)).ToList(),
        parsed.Warnings);

    private static ImportHistoryRow ToHistory(Import i) => new(i.Id, i.Source, i.FileName, i.RowsTotal, i.RowsImported, i.RowsSkipped, i.CampaignsCreated, i.MetricsInserted, i.MetricsUpdated,
        JsonSerializer.Deserialize<List<string>>(i.WarningsJson) ?? [], i.CreatedUtc);
}
