using AdPulse.Api.Contracts;

namespace AdPulse.Api.Services;

/// <summary>Pure KPI math – no I/O, so it is unit-tested exhaustively (division by zero, rounding).</summary>
public static class Kpi
{
    public static decimal Ctr(long clicks, long impressions) => impressions == 0 ? 0 : Math.Round((decimal)clicks / impressions * 100m, 2);
    public static decimal Cpc(decimal spend, long clicks) => clicks == 0 ? 0 : Math.Round(spend / clicks, 2);
    public static decimal Cpa(decimal spend, long conversions) => conversions == 0 ? 0 : Math.Round(spend / conversions, 2);
    public static decimal Roas(decimal revenue, decimal spend) => spend == 0 ? 0 : Math.Round(revenue / spend, 2);

    public static KpiRow Row(string channel, int campaigns, long impressions, long clicks, long conversions, decimal spend, decimal revenue) =>
        new(channel, campaigns, impressions, clicks, conversions, Math.Round(spend, 2), Math.Round(revenue, 2),
            Ctr(clicks, impressions), Cpc(spend, clicks), Cpa(spend, conversions), Roas(revenue, spend));
}
