using AdPulse.Api.Data;
using AdPulse.Api.Domain;
using Microsoft.EntityFrameworkCore;

namespace AdPulse.Api.Services;

public sealed record IngestResult(int CampaignsCreated, int CampaignsUpdated, int MetricsInserted, int MetricsUpdated, IReadOnlyList<Guid> CampaignIds);

/// <summary>
/// The single write path for metrics coming from outside (CSV import, bulk REST push, sample dataset).
/// Campaigns are matched by name within the tenant and created on the fly (Active, flight = min..max date,
/// daily budget = average daily spend) so a first-time user can push data without modelling campaigns first.
/// Metrics are upserted per (campaign, date). Emits <c>campaign.changed</c> and <c>metrics.upserted</c>.
/// </summary>
public sealed class MetricsIngest(AdPulseDbContext db, EventBus bus, Tenancy.TenantContext tenant)
{
    public const int HistoricalAfterDays = 30;

    public async Task<IngestResult> ApplyAsync(IReadOnlyList<NormalizedRow> rows, CancellationToken ct = default)
    {
        if (rows.Count == 0) return new IngestResult(0, 0, 0, 0, []);
        var today = DateOnly.FromDateTime(DateTime.UtcNow);

        var names = rows.Select(r => r.Campaign).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        var campaigns = (await db.Campaigns.Where(c => names.Contains(c.Name)).ToListAsync(ct))
            .ToDictionary(c => c.Name, StringComparer.OrdinalIgnoreCase);

        int created = 0, updatedCampaigns = 0;
        foreach (var grp in rows.GroupBy(r => r.Campaign, StringComparer.OrdinalIgnoreCase))
        {
            var min = grp.Min(r => r.Date); var max = grp.Max(r => r.Date);
            var days = Math.Max(1, max.DayNumber - min.DayNumber + 1);
            var avgDaily = Math.Max(1m, Math.Round(grp.Sum(r => r.Spend) / days, 2));
            if (!campaigns.TryGetValue(grp.Key, out var c))
            {
                // Data older than HistoricalAfterDays is treated as a finished flight; anything recent is a live campaign
                // whose flight runs at least two more weeks (the user can edit dates/budget afterwards).
                var historical = max < today.AddDays(-HistoricalAfterDays);
                c = new Campaign
                {
                    Id = Guid.NewGuid(), Name = grp.Key.Length > 160 ? grp.Key[..160] : grp.Key, Channel = grp.First().Channel,
                    Status = historical ? CampaignStatus.Completed : CampaignStatus.Active,
                    DailyBudget = avgDaily, StartDate = min, EndDate = historical ? max : (max > today.AddDays(13) ? max : today.AddDays(13)),
                };
                db.Campaigns.Add(c);
                campaigns[c.Name] = c;
                created++;
            }
            else
            {
                var changed = false;
                if (min < c.StartDate) { c.StartDate = min; changed = true; }
                if (max > c.EndDate && c.Status != CampaignStatus.Completed) { c.EndDate = max; changed = true; }
                if (changed) { c.UpdatedUtc = DateTime.UtcNow; updatedCampaigns++; }
            }
        }
        await db.SaveChangesAsync(ct); // campaigns first so metrics can reference ids

        var ids = campaigns.Values.Select(c => c.Id).ToList();
        var dates = rows.Select(r => r.Date).Distinct().ToList();
        var existing = await db.DailyMetrics.Where(m => ids.Contains(m.CampaignId) && dates.Contains(m.Date)).ToListAsync(ct);
        var byKey = existing.ToDictionary(m => (m.CampaignId, m.Date));
        int inserted = 0, updated = 0;
        var touched = new Dictionary<Guid, (int rows, DateOnly from, DateOnly to)>();
        foreach (var r in rows)
        {
            var c = campaigns[r.Campaign];
            if (byKey.TryGetValue((c.Id, r.Date), out var m))
            {
                m.Impressions = r.Impressions; m.Clicks = r.Clicks; m.Conversions = r.Conversions; m.Spend = r.Spend; m.Revenue = r.Revenue; updated++;
            }
            else
            {
                db.DailyMetrics.Add(new DailyMetric { CampaignId = c.Id, Date = r.Date, Impressions = r.Impressions, Clicks = r.Clicks, Conversions = r.Conversions, Spend = r.Spend, Revenue = r.Revenue });
                inserted++;
            }
            touched.TryGetValue(c.Id, out var t);
            touched[c.Id] = (t.rows + 1, t.rows == 0 || r.Date < t.from ? r.Date : t.from, t.rows == 0 || r.Date > t.to ? r.Date : t.to);
        }
        await db.SaveChangesAsync(ct);

        foreach (var c in campaigns.Values)
            bus.Publish(tenant.TenantId, EventTypes.CampaignChanged, new { campaignId = c.Id, name = c.Name, status = c.Status.ToString(), channel = c.Channel.ToString(), reason = created > 0 ? "ingest" : "metrics" });
        foreach (var (id, t) in touched)
            bus.Publish(tenant.TenantId, EventTypes.MetricsUpserted, new { campaignId = id, rows = t.rows, from = t.from, to = t.to });

        return new IngestResult(created, updatedCampaigns, inserted, updated, ids);
    }
}
