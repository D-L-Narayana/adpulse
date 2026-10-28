using AdPulse.Api.Contracts;
using AdPulse.Api.Domain;

namespace AdPulse.Api.Services;

/// <summary>
/// Budget pacing: compares what a campaign *should* have spent by <paramref name="asOf"/> (linear pacing)
/// against what it actually spent, and suggests a corrected daily budget for the remaining flight.
/// Status bands: under &lt; 90%, on-track 90–110%, over &gt; 110%.
/// </summary>
public static class Pacing
{
    public const decimal UnderThreshold = 90m;
    public const decimal OverThreshold = 110m;

    public static PacingResponse Compute(Campaign c, decimal actualSpendToDate, DateOnly asOf)
    {
        var daysTotal = Math.Max(1, c.EndDate.DayNumber - c.StartDate.DayNumber + 1);
        var daysElapsed = Math.Clamp(asOf.DayNumber - c.StartDate.DayNumber + 1, 0, daysTotal);
        var expected = Math.Round(c.DailyBudget * daysElapsed, 2);
        var pacing = expected == 0 ? 0 : Math.Round(actualSpendToDate / expected * 100m, 1);
        var status = daysElapsed == 0 ? "not-started" : pacing < UnderThreshold ? "under" : pacing > OverThreshold ? "over" : "on-track";
        var totalBudget = c.DailyBudget * daysTotal;
        var remaining = Math.Max(0, Math.Round(totalBudget - actualSpendToDate, 2));
        var daysLeft = daysTotal - daysElapsed;
        var suggested = daysLeft <= 0 ? 0 : Math.Round(remaining / daysLeft, 2);
        return new PacingResponse(c.Id, asOf, daysElapsed, daysTotal, c.DailyBudget, expected, Math.Round(actualSpendToDate, 2), pacing, status, remaining, suggested);
    }
}
