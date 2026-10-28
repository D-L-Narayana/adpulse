using AdPulse.Api.Domain;
using AdPulse.Api.Services;

namespace AdPulse.Tests;

public class PacingTests
{
    private static Campaign Flight(decimal daily = 100m, int days = 10) => new()
    {
        Id = Guid.NewGuid(), Name = "f", DailyBudget = daily, StartDate = new DateOnly(2025, 1, 1), EndDate = new DateOnly(2025, 1, days)
    };

    [Fact]
    public void On_track_when_actual_matches_linear_expectation()
    {
        var p = Pacing.Compute(Flight(), actualSpendToDate: 500m, asOf: new DateOnly(2025, 1, 5));
        Assert.Equal(5, p.DaysElapsed); Assert.Equal(10, p.DaysTotal);
        Assert.Equal(500m, p.ExpectedSpendToDate); Assert.Equal(100m, p.PacingPercent); Assert.Equal("on-track", p.Status);
        Assert.Equal(500m, p.RemainingBudget); Assert.Equal(100m, p.SuggestedDailyBudget);
    }

    [Fact]
    public void Over_pacing_lowers_suggested_daily_budget()
    {
        var p = Pacing.Compute(Flight(), 700m, new DateOnly(2025, 1, 5));
        Assert.Equal(140m, p.PacingPercent); Assert.Equal("over", p.Status);
        Assert.Equal(300m, p.RemainingBudget); Assert.Equal(60m, p.SuggestedDailyBudget);
    }

    [Fact]
    public void Under_pacing_raises_suggested_daily_budget()
    {
        var p = Pacing.Compute(Flight(), 200m, new DateOnly(2025, 1, 5));
        Assert.Equal(40m, p.PacingPercent); Assert.Equal("under", p.Status);
        Assert.Equal(160m, p.SuggestedDailyBudget);
    }

    [Theory]
    [InlineData(450, "on-track")]   // exactly 90%
    [InlineData(449.99, "under")]
    [InlineData(550, "on-track")]   // exactly 110%
    [InlineData(550.5, "over")]
    public void Thresholds_are_inclusive(decimal actual, string expected) =>
        Assert.Equal(expected, Pacing.Compute(Flight(), actual, new DateOnly(2025, 1, 5)).Status);

    [Fact]
    public void Band_is_ten_percent_either_side_of_100()
    {
        Assert.Equal(10m, Pacing.BandPercent);
        Assert.Equal(90m, Pacing.UnderThreshold);
        Assert.Equal(110m, Pacing.OverThreshold);
    }

    [Fact]
    public void Deviation_is_actual_minus_expected()
    {
        Assert.Equal(200m, Pacing.Compute(Flight(), 700m, new DateOnly(2025, 1, 5)).Deviation);
        Assert.Equal(-300m, Pacing.Compute(Flight(), 200m, new DateOnly(2025, 1, 5)).Deviation);
    }

    [Fact]
    public void Suggested_budget_never_negative_when_overspent_beyond_total()
    {
        var p = Pacing.Compute(Flight(), 5000m, new DateOnly(2025, 1, 5));
        Assert.Equal(0m, p.RemainingBudget); Assert.Equal(0m, p.SuggestedDailyBudget); Assert.Equal("over", p.Status);
    }

    [Fact]
    public void Before_start_is_not_started_and_never_divides_by_zero()
    {
        var p = Pacing.Compute(Flight(), 0m, new DateOnly(2024, 12, 20));
        Assert.Equal(0, p.DaysElapsed); Assert.Equal("not-started", p.Status); Assert.Equal(0m, p.PacingPercent);
    }

    [Fact]
    public void After_end_clamps_to_total_days_and_suggests_zero()
    {
        var p = Pacing.Compute(Flight(), 1200m, new DateOnly(2025, 2, 1));
        Assert.Equal(10, p.DaysElapsed); Assert.Equal(0m, p.SuggestedDailyBudget); Assert.Equal(0m, p.RemainingBudget);
    }
}
