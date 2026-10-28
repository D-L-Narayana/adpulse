using System.Net;
using System.Net.Http.Headers;
using AdPulse.Api.Contracts;
using AdPulse.Api.Domain;
using AdPulse.Api.Services;

namespace AdPulse.Tests;

/// <summary>Format detection on the real export shapes + the dry-run / commit behaviour of POST /api/v1/imports/csv.</summary>
public class CsvImportTests : IClassFixture<AdPulseFactory>
{
    private readonly AdPulseFactory _f;
    public CsvImportTests(AdPulseFactory f) => _f = f;

    // Google Ads "Download CSV" has two title lines, a header, then data and a Total row. Cost uses thousands separators.
    public const string GoogleCsv =
        "Campaign report (Oct 1, 2026 - Oct 7, 2026)\n" +
        "All time\n" +
        "Campaign,Campaign state,Day,Currency code,Cost,Impressions,Clicks,Conversions,Conv. value\n" +
        "Brand Search,Enabled,2026-10-01,INR,\"1,250.50\",10000,500,25,\"8,000.00\"\n" +
        "Brand Search,Enabled,2026-10-02,INR,1300.00,11000,540,27,8500\n" +
        "Shopping - All,Enabled,2026-10-01,INR,900.25,40000,800,30,12000\n" +
        "Total: Account,,,,\"3,450.75\",61000,1840,82,28500\n";

    // Meta Ads Manager export, segmented by age (two rows per campaign-day that must be summed).
    public const string MetaCsv =
        "Reporting starts,Reporting ends,Campaign name,Day,Age,Amount spent (INR),Impressions,Link clicks,Results,Result indicator,Purchases conversion value\n" +
        "2026-10-01,2026-10-01,Prospecting - Lookalike,2026-10-01,18-24,500.5,20000,300,10,actions:purchase,4000\n" +
        "2026-10-01,2026-10-01,Prospecting - Lookalike,2026-10-01,25-34,700.25,30000,450,15,actions:purchase,6000\n" +
        "2026-10-02,2026-10-02,Prospecting - Lookalike,2026-10-02,18-24,520,21000,310,11,actions:purchase,4200\n" +
        "2026-10-01,2026-10-01,Retargeting,2026-10-01,25-34,200,5000,250,20,actions:purchase,9000\n";

    public const string GenericCsv =
        "campaign;date;channel;impressions;clicks;conversions;spend;revenue\n" +
        "Sponsored Products;01/10/2026;Amazon;5000;120;8;300.00;1200.00\n" +
        "Sponsored Products;02/10/2026;Amazon;5200;130;9;310.00;1300.00\n" +
        "B2B Leads;01/10/2026;LinkedIn;800;20;2;150.00;0\n";

    [Fact]
    public void Detects_google_ads_and_skips_title_and_total_rows()
    {
        var p = CsvImport.Parse(GoogleCsv);
        Assert.Equal("google-ads", p.Detection.Format);
        Assert.Equal(Channel.GoogleAds, p.Detection.Channel);
        Assert.Equal("Cost", p.Detection.ColumnMapping["spend"]);
        Assert.Equal("Day", p.Detection.ColumnMapping["date"]);
        Assert.Equal("Conv. value", p.Detection.ColumnMapping["revenue"]);
        Assert.Equal(3, p.Rows.Count);
        Assert.Equal(1, p.RowsSkipped); // Total row
        var brand = p.Rows.Single(r => r.Campaign == "Brand Search" && r.Date == new DateOnly(2026, 10, 1));
        Assert.Equal(1250.50m, brand.Spend);
        Assert.Equal(8000m, brand.Revenue);
        Assert.Equal(10000, brand.Impressions);
    }

    [Fact]
    public void Detects_meta_ads_reads_currency_and_sums_segments()
    {
        var p = CsvImport.Parse(MetaCsv);
        Assert.Equal("meta-ads", p.Detection.Format);
        Assert.Equal(Channel.Meta, p.Detection.Channel);
        Assert.Equal("INR", p.Detection.Currency);
        Assert.Equal("Amount spent (INR)", p.Detection.ColumnMapping["spend"]);
        Assert.Equal("Link clicks", p.Detection.ColumnMapping["clicks"]);
        Assert.Equal("Results", p.Detection.ColumnMapping["conversions"]);
        Assert.Equal(3, p.Rows.Count);
        var day1 = p.Rows.Single(r => r.Campaign == "Prospecting - Lookalike" && r.Date == new DateOnly(2026, 10, 1));
        Assert.Equal(1200.75m, day1.Spend);
        Assert.Equal(50000, day1.Impressions);
        Assert.Equal(750, day1.Clicks);
        Assert.Equal(25, day1.Conversions);
        Assert.Equal(10000m, day1.Revenue);
        Assert.Contains(p.Warnings, w => w.Contains("segmented"));
    }

    [Fact]
    public void Detects_generic_with_semicolons_per_row_channel_and_dd_mm_yyyy()
    {
        var p = CsvImport.Parse(GenericCsv);
        Assert.Equal("generic", p.Detection.Format);
        Assert.Equal(3, p.Rows.Count);
        Assert.Equal(Channel.Amazon, p.Rows.First(r => r.Campaign == "Sponsored Products").Channel);
        Assert.Equal(Channel.LinkedIn, p.Rows.First(r => r.Campaign == "B2B Leads").Channel);
        Assert.Equal(new DateOnly(2026, 10, 2), p.Rows.Max(r => r.Date));
    }

    [Fact]
    public void Unknown_layout_is_reported_not_guessed()
    {
        var p = CsvImport.Parse("foo,bar\n1,2\n");
        Assert.Equal("unknown", p.Detection.Format);
        Assert.Empty(p.Rows);
    }

    [Fact]
    public async Task Dry_run_previews_without_writing_then_commit_upserts_and_audits()
    {
        var (client, _) = await _f.NewTenantClientAsync();

        var preview = await Upload<ImportPreviewResponse>(client, "/api/v1/imports/csv?dryRun=true", "google.csv", GoogleCsv, HttpStatusCode.OK);
        Assert.True(preview.DryRun);
        Assert.Equal("google-ads", preview.DetectedFormat);
        Assert.Equal(3, preview.CampaignDays);
        Assert.Equal(3, preview.Preview.Count);
        Assert.Empty(await (await client.GetAsync("/api/v1/campaigns")).Json<List<CampaignResponse>>());
        Assert.Empty(await (await client.GetAsync("/api/v1/imports")).Json<List<ImportHistoryRow>>());

        var commit = await Upload<ImportCommitResponse>(client, "/api/v1/imports/csv", "google.csv", GoogleCsv, HttpStatusCode.Created);
        Assert.Equal(2, commit.CampaignsCreated);
        Assert.Equal(3, commit.MetricsInserted);
        Assert.Equal(0, commit.MetricsUpdated);
        var campaigns = await (await client.GetAsync("/api/v1/campaigns")).Json<List<CampaignResponse>>();
        Assert.Equal(2, campaigns.Count);
        Assert.All(campaigns, c => Assert.Equal(Channel.GoogleAds, c.Channel));

        // re-importing the same file is an update, not a duplicate
        var again = await Upload<ImportCommitResponse>(client, "/api/v1/imports/csv", "google.csv", GoogleCsv, HttpStatusCode.Created);
        Assert.Equal(0, again.CampaignsCreated);
        Assert.Equal(3, again.MetricsUpdated);

        var history = await (await client.GetAsync("/api/v1/imports")).Json<List<ImportHistoryRow>>();
        Assert.Equal(2, history.Count);
        Assert.All(history, h => Assert.Equal("google-ads", h.Source));

        var report = await (await client.GetAsync("/api/v1/reports/cross-channel?from=2026-10-01&to=2026-10-07")).Json<CrossChannelReport>();
        Assert.Equal(3450.75m, report.Total.Spend);
    }

    [Fact]
    public async Task Meta_import_lands_on_meta_channel_and_isolated_per_tenant()
    {
        var (a, _) = await _f.NewTenantClientAsync();
        var (b, _) = await _f.NewTenantClientAsync();
        var commit = await Upload<ImportCommitResponse>(a, "/api/v1/imports/csv", "meta.csv", MetaCsv, HttpStatusCode.Created);
        Assert.Equal("meta-ads", commit.DetectedFormat);
        Assert.Equal("INR", commit.Currency);
        Assert.Equal(2, commit.CampaignsCreated);
        Assert.Empty(await (await b.GetAsync("/api/v1/campaigns")).Json<List<CampaignResponse>>());
        Assert.Equal(HttpStatusCode.NotFound, (await b.GetAsync($"/api/v1/imports/{commit.ImportId}")).StatusCode);
    }

    [Fact]
    public async Task Garbage_file_is_422_problem()
    {
        var (client, _) = await _f.NewTenantClientAsync();
        using var form = new MultipartFormDataContent { { new StringContent("nothing,to,see\n1,2,3"), "file", "x.csv" } };
        var r = await client.PostAsync("/api/v1/imports/csv", form);
        Assert.Equal(HttpStatusCode.UnprocessableEntity, r.StatusCode);
        Assert.Equal("unrecognised_csv", (await r.Problem()).Code);
    }

    internal static async Task<T> Upload<T>(HttpClient client, string url, string fileName, string csv, HttpStatusCode expected)
    {
        using var form = new MultipartFormDataContent();
        var part = new StringContent(csv);
        part.Headers.ContentType = new MediaTypeHeaderValue("text/csv");
        form.Add(part, "file", fileName);
        var r = await client.PostAsync(url, form);
        Assert.True(expected == r.StatusCode, $"expected {expected} got {r.StatusCode}: {await r.Content.ReadAsStringAsync()}");
        return await r.Json<T>();
    }
}
