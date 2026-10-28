using AdPulse.Api.Services;

namespace AdPulse.Tests;

public class KpiTests
{
    [Theory]
    [InlineData(50, 1000, 5.00)]
    [InlineData(0, 1000, 0)]
    [InlineData(3, 7, 42.86)]
    public void Ctr_is_percentage_rounded_to_2dp(long clicks, long impressions, decimal expected) => Assert.Equal(expected, Kpi.Ctr(clicks, impressions));

    [Fact] public void Ctr_with_zero_impressions_is_zero_not_exception() => Assert.Equal(0, Kpi.Ctr(10, 0));
    [Fact] public void Cpc_divides_spend_by_clicks() => Assert.Equal(0.5m, Kpi.Cpc(100m, 200));
    [Fact] public void Cpc_with_zero_clicks_is_zero() => Assert.Equal(0, Kpi.Cpc(100m, 0));
    [Fact] public void Cpa_divides_spend_by_conversions() => Assert.Equal(12.5m, Kpi.Cpa(100m, 8));
    [Fact] public void Roas_is_revenue_over_spend() => Assert.Equal(3.2m, Kpi.Roas(320m, 100m));
    [Fact] public void Roas_with_zero_spend_is_zero() => Assert.Equal(0, Kpi.Roas(320m, 0));

    [Fact]
    public void Row_rounds_money_and_derives_all_kpis()
    {
        var row = Kpi.Row("Meta", 2, 10_000, 250, 10, 123.456m, 500.004m);
        Assert.Equal(123.46m, row.Spend);
        Assert.Equal(500.00m, row.Revenue);
        Assert.Equal(2.5m, row.Ctr);
        Assert.Equal(0.49m, row.Cpc);
        Assert.Equal(12.35m, row.Cpa);
        Assert.Equal(4.05m, row.Roas);
    }
}
