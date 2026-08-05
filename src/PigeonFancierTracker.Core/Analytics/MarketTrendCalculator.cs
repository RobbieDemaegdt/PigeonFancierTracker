using PigeonFancierTracker.Core.Contracts;

namespace PigeonFancierTracker.Core.Analytics;

public static class MarketTrendCalculator
{
    public const int MinSalesForTrend = 3;

    public static MarketTrendResult Calculate(IReadOnlyList<MarketSalePoint> sales)
    {
        if (sales.Count == 0)
            return new MarketTrendResult([], "Onvoldoende data", 0, 0, 0);

        var validSales = sales
            .Where(s => s.TotalSkill > 0)
            .OrderBy(s => s.SoldAt)
            .ToList();

        if (validSales.Count == 0)
            return new MarketTrendResult([], "Onvoldoende data", 0, 0, 0);

        var buckets = validSales
            .GroupBy(s => FormatPeriod(s.SoldAt))
            .Select(g => new MarketTrendBucket(
                g.Key,
                Math.Round(g.Average(s => s.SoldPrice / s.TotalSkill), 2),
                g.Count()))
            .ToList();

        var currentAvg = 0m;
        var previousAvg = 0m;
        var trendPercentage = 0m;
        var direction = "Onvoldoende data";

        if (buckets.Count >= 2)
        {
            currentAvg = buckets[^1].AvgPricePerSkill;
            previousAvg = buckets[^2].AvgPricePerSkill;

            if (previousAvg > 0)
            {
                trendPercentage = Math.Round((currentAvg - previousAvg) / previousAvg * 100, 1);
                direction = trendPercentage switch
                {
                    > 5 => "Stijgend",
                    < -5 => "Dalend",
                    _ => "Stabiel",
                };
            }
        }
        else if (buckets.Count == 1)
        {
            currentAvg = buckets[0].AvgPricePerSkill;
            direction = "Stabiel";
        }

        return new MarketTrendResult(buckets, direction, currentAvg, previousAvg, trendPercentage);
    }

    private static string FormatPeriod(DateTimeOffset date)
    {
        var weekOfYear = System.Globalization.ISOWeek.GetWeekOfYear(date.DateTime);
        return $"{date.Year}-W{weekOfYear:D2}";
    }
}
