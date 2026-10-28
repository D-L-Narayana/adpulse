using AdPulse.Api.Data;
using AdPulse.Api.Domain;
using AdPulse.Api.Tenancy;
using Microsoft.EntityFrameworkCore;

namespace AdPulse.Api.Services;

public sealed record PacingAlert(Guid CampaignId, string Campaign, Channel Channel, DateOnly AsOf, decimal PacingPercent, string Status, decimal Deviation, decimal SuggestedDailyBudget, string Message);

/// <summary>
/// Evaluates every Active campaign of one tenant and returns the ones that are more than
/// <see cref="AlertBandPercent"/> over / under their linear pace. Pure read + event publish; no state is stored,
/// so re-running it is safe (the dashboard also triggers it on demand).
/// </summary>
public sealed class PacingEvaluator(AdPulseDbContext db, EventBus bus, TenantContext tenant)
{
    public const decimal AlertBandPercent = 15m;

    public async Task<IReadOnlyList<PacingAlert>> EvaluateAsync(DateOnly? asOf = null, bool publish = true, CancellationToken ct = default)
    {
        var day = asOf ?? DateOnly.FromDateTime(DateTime.UtcNow);
        var campaigns = await db.Campaigns.AsNoTracking().Where(c => c.Status == CampaignStatus.Active && c.StartDate <= day).ToListAsync(ct);
        if (campaigns.Count == 0) return [];
        var ids = campaigns.Select(c => c.Id).ToList();
        var spend = await db.DailyMetrics.AsNoTracking()
            .Where(m => ids.Contains(m.CampaignId) && m.Date <= day)
            .GroupBy(m => m.CampaignId)
            .Select(g => new { g.Key, Spend = g.Sum(m => (double)m.Spend) })
            .ToDictionaryAsync(x => x.Key, x => (decimal)x.Spend, ct);

        var alerts = new List<PacingAlert>();
        foreach (var c in campaigns)
        {
            var p = Pacing.Compute(c, spend.GetValueOrDefault(c.Id), day);
            if (p.Status == PacingStatus.NotStarted) continue;
            var deviationPct = p.PacingPercent - 100m;
            if (Math.Abs(deviationPct) <= AlertBandPercent) continue;
            var direction = deviationPct > 0 ? "over" : "under";
            var msg = $"{c.Name} is {Math.Abs(deviationPct):0.#}% {direction} pace ({p.ActualSpendToDate:0.00} spent vs {p.ExpectedSpendToDate:0.00} expected). Suggested daily budget: {p.SuggestedDailyBudget:0.00}.";
            var alert = new PacingAlert(c.Id, c.Name, c.Channel, day, p.PacingPercent, direction, p.Deviation, p.SuggestedDailyBudget, msg);
            alerts.Add(alert);
            if (publish) bus.Publish(tenant.TenantId, EventTypes.PacingAlert, alert);
        }
        return alerts;
    }
}

/// <summary>
/// Daily background job: walks every tenant, runs <see cref="PacingEvaluator"/> under that tenant's context
/// (so the global query filters do the scoping) and emits <c>pacing.alert</c> events. Interval is
/// <c>Pacing:EvaluationInterval</c> (default 24h) with an initial delay of <c>Pacing:InitialDelay</c> (default 1 min).
/// </summary>
public sealed class PacingAlertService(IServiceScopeFactory scopes, IConfiguration config, ILogger<PacingAlertService> log) : BackgroundService
{
    public DateTime? LastRunUtc { get; private set; }
    public int LastAlertCount { get; private set; }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!config.GetValue("Pacing:Enabled", true)) return;
        var interval = config.GetValue("Pacing:EvaluationInterval", TimeSpan.FromHours(24));
        var initial = config.GetValue("Pacing:InitialDelay", TimeSpan.FromMinutes(1));
        try { await Task.Delay(initial, stoppingToken); } catch (OperationCanceledException) { return; }
        while (!stoppingToken.IsCancellationRequested)
        {
            try { LastAlertCount = await RunOnceAsync(stoppingToken); LastRunUtc = DateTime.UtcNow; }
            catch (OperationCanceledException) { return; }
            catch (Exception ex) { log.LogError(ex, "Pacing evaluation failed"); }
            try { await Task.Delay(interval, stoppingToken); } catch (OperationCanceledException) { return; }
        }
    }

    public async Task<int> RunOnceAsync(CancellationToken ct)
    {
        List<Tenant> tenants;
        using (var s = scopes.CreateScope())
            tenants = await s.ServiceProvider.GetRequiredService<AdPulseDbContext>().Tenants.AsNoTracking().ToListAsync(ct);

        var total = 0;
        foreach (var t in tenants)
        {
            using var scope = scopes.CreateScope();
            scope.ServiceProvider.GetRequiredService<TenantContext>().Set(t);
            var alerts = await scope.ServiceProvider.GetRequiredService<PacingEvaluator>().EvaluateAsync(ct: ct);
            foreach (var a in alerts) log.LogWarning("pacing.alert tenant={TenantSlug} campaign={Campaign} pacing={PacingPercent}% {Status}", t.Slug, a.Campaign, a.PacingPercent, a.Status);
            total += alerts.Count;
        }
        log.LogInformation("Pacing evaluation: {Tenants} tenants, {Alerts} alerts", tenants.Count, total);
        return total;
    }
}
