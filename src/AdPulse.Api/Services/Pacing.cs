using AdPulse.Api.Contracts;
using AdPulse.Api.Domain;

namespace AdPulse.Api.Services;

/// <summary>
/// Budget-pacing engine.
///
/// 1. Linear expectation: by day <c>d</c> of an <c>n</c>-day flight a campaign should have spent <c>d * DailyBudget</c>.
/// 2. Pacing % = actual / expected. A ±10 % band around 100 % is considered healthy:
///      under  &lt; 90 %   |   on-track 90–110 %   |   over &gt; 110 %
/// 3. Correction: the remaining budget (total − actual) is spread evenly over the remaining days, giving a
///    <c>SuggestedDailyBudget</c> that brings the flight back to 100 % by the end date.
///
/// Pure function – no I/O – so it is unit-tested exhaustively (thresholds, clamping, division by zero).
/// </summary>
public static class Pacing
{
    public const decimal BandPercent = 10m;
    public const decimal UnderThreshold = 100m - BandPercent; // 90
    public const decimal OverThreshold = 100m + BandPercent;  // 110

    public static PacingResponse Compute(Campaign c, decimal actualSpendToDate, DateOnly asOf)
    {
        var daysTotal = Math.Max(1, c.EndDate.DayNumber - c.StartDate.DayNumber + 1);
        var daysElapsed = Math.Clamp(asOf.DayNumber - c.StartDate.DayNumber + 1, 0, daysTotal);
        var expected = Math.Round(c.DailyBudget * daysElapsed, 2);

        // Classify on the exact ratio (not the rounded display value) so 89.998 % is correctly "under".
        var exactPacing = expected == 0 ? 0 : actualSpendToDate / expected * 100m;
        var pacing = Math.Round(exactPacing, 1);
        var status = daysElapsed == 0 ? PacingStatus.NotStarted
                   : exactPacing < UnderThreshold ? PacingStatus.Under
                   : exactPacing > OverThreshold ? PacingStatus.Over
                   : PacingStatus.OnTrack;

        var totalBudget = c.DailyBudget * daysTotal;
        var remaining = Math.Max(0, Math.Round(totalBudget - actualSpendToDate, 2));
        var daysLeft = daysTotal - daysElapsed;
        var suggested = daysLeft <= 0 ? 0 : Math.Round(remaining / daysLeft, 2);
        var deviation = Math.Round(actualSpendToDate - expected, 2);

        return new PacingResponse(c.Id, asOf, daysElapsed, daysTotal, c.DailyBudget, expected, Math.Round(actualSpendToDate, 2),
            pacing, status, deviation, remaining, suggested);
    }
}

public static class PacingStatus
{
    public const string NotStarted = "not-started";
    public const string Under = "under";
    public const string OnTrack = "on-track";
    public const string Over = "over";
}
