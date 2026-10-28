using System.Globalization;
using System.Text;
using AdPulse.Api.Domain;

namespace AdPulse.Api.Services;

/// <summary>A campaign-day row after column mapping, before it touches the database.</summary>
public sealed record NormalizedRow(string Campaign, Channel Channel, DateOnly Date, long Impressions, long Clicks, long Conversions, decimal Spend, decimal Revenue);

public sealed record CsvDetection(
    string Format,                                  // google-ads | meta-ads | generic | unknown
    string FormatLabel,
    Channel Channel,
    string? Currency,
    IReadOnlyDictionary<string, string?> ColumnMapping,  // logical field -> source header (null = not present)
    IReadOnlyList<string> Headers,
    int HeaderLineIndex);

public sealed record CsvParseResult(CsvDetection Detection, IReadOnlyList<NormalizedRow> Rows, IReadOnlyList<string> Warnings, int RowsTotal, int RowsSkipped);

/// <summary>
/// Reads the CSV files advertisers actually export and maps them onto the AdPulse metric row.
///
///  * Google Ads (Reports -> Download CSV): optional title lines ("Campaign report", date range), then a header row
///    with <c>Campaign, Day, Cost, Impressions, Clicks, Conversions, Conv. value</c>… and trailing <c>Total: …</c> rows.
///  * Meta Ads Manager (Export -> CSV): <c>Campaign name, Day, Amount spent (INR), Impressions, Link clicks, Results,
///    Purchases conversion value</c>… (currency is read from the header).
///  * Generic template: <c>campaign, date, impressions, clicks, conversions, spend, revenue[, channel]</c>, header names
///    matched case-insensitively with common synonyms.
///
/// Detection is header-based and deterministic; the mapping is returned so the UI can show it before committing.
/// </summary>
public static class CsvImport
{
    private static readonly string[] DateFormats = ["yyyy-MM-dd", "dd/MM/yyyy", "MM/dd/yyyy", "d/M/yyyy", "M/d/yyyy", "yyyy/MM/dd", "dd-MM-yyyy", "MMM d, yyyy", "d MMM yyyy", "yyyy-MM-ddTHH:mm:ss", "yyyy-MM-dd HH:mm:ss"];

    // logical field -> accepted header names (lower-case, trimmed). Order matters: first hit wins.
    private static readonly Dictionary<string, string[]> Synonyms = new()
    {
        ["campaign"] = ["campaign", "campaign name", "campaign_name", "campaignname", "name"],
        ["date"] = ["day", "date", "reporting starts", "reporting_start", "reporting start", "report date", "period"],
        ["impressions"] = ["impressions", "impr.", "impr", "views"],
        ["clicks"] = ["clicks", "link clicks", "link_clicks", "all clicks", "clicks (all)"],
        ["conversions"] = ["conversions", "results", "conv.", "purchases", "approved_conversion", "total_conversion", "leads"],
        ["spend"] = ["spend", "cost", "amount spent", "amount_spent", "spent", "total cost", "ad spend", "media cost"],
        ["revenue"] = ["revenue", "conv. value", "conversion value", "conversions value", "purchases conversion value", "purchase conversion value", "total conv. value", "all conv. value", "sales", "purchase value"],
        ["channel"] = ["channel", "platform", "network", "source"],
    };

    public static CsvParseResult Parse(string text, Channel? defaultChannel = null, string? campaignPrefix = null)
    {
        var warnings = new List<string>();
        var lines = SplitRecords(text);
        var (headerIdx, headers) = FindHeader(lines);
        if (headerIdx < 0)
            return new CsvParseResult(new CsvDetection("unknown", "Unrecognised", defaultChannel ?? Channel.Other, null, new Dictionary<string, string?>(), [], -1), [], ["No header row with a campaign and a date column was found."], 0, 0);

        var detection = Detect(headers, defaultChannel);
        var map = detection.ColumnMapping;
        int Col(string field) => map.TryGetValue(field, out var h) && h is not null ? IndexOf(headers, h) : -1;
        int cCamp = Col("campaign"), cDate = Col("date"), cImp = Col("impressions"), cClk = Col("clicks"), cConv = Col("conversions"), cSpend = Col("spend"), cRev = Col("revenue"), cChan = Col("channel");
        foreach (var missing in new[] { ("impressions", cImp), ("clicks", cClk), ("conversions", cConv), ("spend", cSpend) }.Where(x => x.Item2 < 0))
            warnings.Add($"Column for '{missing.Item1}' not found; it will be imported as 0.");
        if (cRev < 0) warnings.Add("No revenue / conversion value column found; ROAS will be 0 for these rows.");

        // Aggregate duplicates (e.g. Google Ads segmented by device, Meta by age/gender) into one row per campaign-day.
        var agg = new Dictionary<(string, DateOnly), (Channel ch, long imp, long clk, long conv, decimal spend, decimal rev)>();
        int total = 0, skipped = 0;
        for (var i = headerIdx + 1; i < lines.Count; i++)
        {
            var f = lines[i];
            if (f.Count == 0 || f.All(string.IsNullOrWhiteSpace)) continue;
            total++;
            var camp = Get(f, cCamp).Trim();
            if (camp.Length == 0 || camp.StartsWith("Total", StringComparison.OrdinalIgnoreCase)) { skipped++; continue; } // Google Ads footer rows
            if (!TryDate(Get(f, cDate), out var date)) { skipped++; if (warnings.Count < 25) warnings.Add($"Row {i + 1}: unreadable date '{Get(f, cDate)}' - skipped."); continue; }
            var ch = detection.Channel;
            if (cChan >= 0 && TryChannel(Get(f, cChan), out var rowCh)) ch = rowCh;
            var key = ((campaignPrefix ?? "") + camp, date);
            agg.TryGetValue(key, out var a);
            agg[key] = (ch, a.imp + Long(Get(f, cImp)), a.clk + Long(Get(f, cClk)), a.conv + Long(Get(f, cConv)), a.spend + Money(Get(f, cSpend)), a.rev + Money(Get(f, cRev)));
        }
        if (agg.Count < total - skipped) warnings.Add($"{total - skipped - agg.Count} segmented rows were summed into {agg.Count} campaign-day rows.");

        var rows = agg.Select(kv => new NormalizedRow(kv.Key.Item1, kv.Value.ch, kv.Key.Item2, kv.Value.imp, kv.Value.clk, kv.Value.conv, Math.Round(kv.Value.spend, 2), Math.Round(kv.Value.rev, 2)))
            .OrderBy(r => r.Campaign).ThenBy(r => r.Date).ToList();
        return new CsvParseResult(detection, rows, warnings, total, skipped);
    }

    public static CsvDetection Detect(IReadOnlyList<string> headers, Channel? defaultChannel = null)
    {
        var lower = headers.Select(h => h.Trim().ToLowerInvariant()).ToList();
        bool Has(string h) => lower.Contains(h);
        bool HasPrefix(string p) => lower.Any(h => h.StartsWith(p));

        string format; string label; Channel channel; string? currency = null;
        if (Has("campaign name") && (HasPrefix("amount spent") || Has("link clicks") || Has("results") || Has("reporting starts")))
        {
            format = "meta-ads"; label = "Meta Ads Manager export"; channel = Channel.Meta;
            var spentHeader = headers.FirstOrDefault(h => h.Trim().StartsWith("Amount spent", StringComparison.OrdinalIgnoreCase));
            if (spentHeader is not null) { var o = spentHeader.IndexOf('('); var c = spentHeader.IndexOf(')'); if (o >= 0 && c > o) currency = spentHeader[(o + 1)..c].Trim(); }
        }
        else if (Has("campaign") && (Has("cost") || HasPrefix("conv.") || Has("impr.")) && (Has("day") || Has("date") || Has("week") || Has("month")))
        {
            format = "google-ads"; label = "Google Ads report export"; channel = Channel.GoogleAds;
            var costHeader = headers.FirstOrDefault(h => h.Trim().StartsWith("Cost (", StringComparison.OrdinalIgnoreCase));
            if (costHeader is not null) currency = costHeader[(costHeader.IndexOf('(') + 1)..costHeader.IndexOf(')')].Trim();
        }
        else
        {
            format = "generic"; label = "Generic AdPulse template"; channel = defaultChannel ?? Channel.Other;
        }

        var mapping = new Dictionary<string, string?>();
        foreach (var (field, names) in Synonyms)
        {
            string? hit = null;
            foreach (var n in names)
            {
                var idx = lower.IndexOf(n);
                if (idx < 0 && field == "spend") idx = lower.FindIndex(h => h.StartsWith("amount spent") || h.StartsWith("cost ("));
                if (idx < 0 && field == "revenue") idx = lower.FindIndex(h => h.Contains("conv. value") || h.Contains("conversion value"));
                if (idx >= 0) { hit = headers[idx].Trim(); break; }
            }
            mapping[field] = hit;
        }
        return new CsvDetection(format, label, channel, currency, mapping, headers, 0);
    }

    // ---- helpers -------------------------------------------------------------------------------------------

    private static (int, List<string>) FindHeader(List<List<string>> lines)
    {
        for (var i = 0; i < Math.Min(lines.Count, 12); i++)
        {
            var l = lines[i].Select(h => h.Trim().ToLowerInvariant()).ToList();
            var hasCampaign = Synonyms["campaign"].Any(l.Contains);
            var hasDate = Synonyms["date"].Any(l.Contains);
            if (hasCampaign && hasDate && l.Count >= 3) return (i, lines[i].Select(h => h.Trim()).ToList());
        }
        return (-1, []);
    }

    private static int IndexOf(IReadOnlyList<string> headers, string h)
    {
        for (var i = 0; i < headers.Count; i++) if (string.Equals(headers[i].Trim(), h, StringComparison.OrdinalIgnoreCase)) return i;
        return -1;
    }

    private static string Get(List<string> f, int idx) => idx >= 0 && idx < f.Count ? f[idx] : "";

    public static bool TryDate(string s, out DateOnly d)
    {
        s = s.Trim().Trim('"');
        if (DateOnly.TryParseExact(s, DateFormats, CultureInfo.InvariantCulture, DateTimeStyles.None, out d)) return true;
        if (DateTime.TryParse(s, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out var dt)) { d = DateOnly.FromDateTime(dt); return true; }
        d = default; return false;
    }

    public static long Long(string s)
    {
        s = Clean(s);
        if (long.TryParse(s, NumberStyles.Integer, CultureInfo.InvariantCulture, out var l)) return l;
        return decimal.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out var d) ? (long)Math.Round(d) : 0;
    }

    public static decimal Money(string s) => decimal.TryParse(Clean(s), NumberStyles.Float, CultureInfo.InvariantCulture, out var d) ? d : 0m;

    private static string Clean(string s)
    {
        s = s.Trim().Trim('"');
        if (s.Length == 0 || s is "--" or "-" or "—") return "0";
        var sb = new StringBuilder(s.Length);
        foreach (var c in s) if (char.IsDigit(c) || c is '.' or '-' || (c == 'e' && sb.Length > 0)) sb.Append(c);
        return sb.Length == 0 ? "0" : sb.ToString();
    }

    public static bool TryChannel(string s, out Channel ch)
    {
        var k = s.Trim().ToLowerInvariant().Replace(" ", "").Replace("_", "").Replace("-", "");
        ch = k switch
        {
            "google" or "googleads" or "adwords" or "search" or "youtube" => Channel.GoogleAds,
            "meta" or "facebook" or "instagram" or "fb" or "metaads" => Channel.Meta,
            "amazon" or "amazonads" => Channel.Amazon,
            "linkedin" => Channel.LinkedIn,
            "dv360" or "displayvideo360" or "programmatic" => Channel.DV360,
            "other" => Channel.Other,
            _ => default,
        };
        return ch != default;
    }

    /// <summary>Minimal RFC 4180 reader: quoted fields, doubled quotes, CR/LF inside quotes. Delimiter auto-detected (, ; tab).</summary>
    public static List<List<string>> SplitRecords(string text)
    {
        if (text.Length > 0 && text[0] == '\uFEFF') text = text[1..];
        var delim = DetectDelimiter(text);
        var rows = new List<List<string>>();
        var row = new List<string>();
        var field = new StringBuilder();
        var inQuotes = false;
        for (var i = 0; i < text.Length; i++)
        {
            var c = text[i];
            if (inQuotes)
            {
                if (c == '"') { if (i + 1 < text.Length && text[i + 1] == '"') { field.Append('"'); i++; } else inQuotes = false; }
                else field.Append(c);
            }
            else if (c == '"') inQuotes = true;
            else if (c == delim) { row.Add(field.ToString()); field.Clear(); }
            else if (c == '\r') { }
            else if (c == '\n') { row.Add(field.ToString()); field.Clear(); rows.Add(row); row = new(); }
            else field.Append(c);
        }
        if (field.Length > 0 || row.Count > 0) { row.Add(field.ToString()); rows.Add(row); }
        return rows;
    }

    private static char DetectDelimiter(string text)
    {
        var head = text.Length > 4000 ? text[..4000] : text;
        int commas = head.Count(c => c == ','), semis = head.Count(c => c == ';'), tabs = head.Count(c => c == '\t');
        return tabs > commas && tabs > semis ? '\t' : semis > commas ? ';' : ',';
    }
}
