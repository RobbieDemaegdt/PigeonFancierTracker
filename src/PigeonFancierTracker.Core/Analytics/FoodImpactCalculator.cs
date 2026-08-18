using PigeonFancierTracker.Core.Contracts;
using PigeonFancierTracker.Core.Domain;

namespace PigeonFancierTracker.Core.Analytics;

public static class FoodImpactCalculator
{
    public static FoodImpactAnalysis Calculate(
        IReadOnlyList<FlightResultListItem> results,
        IReadOnlyDictionary<FoodMix, string?>? comments = null)
    {
        var withFood = results.Where(r => r.FoodMix is not null).ToList();

        if (withFood.Count == 0)
            return new FoodImpactAnalysis([]);

        var grouped = withFood
            .GroupBy(r => r.FoodMix!, FoodMixComparer.Instance)
            .Select(g =>
            {
                var flightCount = g.Select(r => r.FlightId).Distinct().Count();
                var percentiles = g.Select(r => r.Percentile).ToList();
                var avgPercentile = Math.Round(percentiles.Average(), 1);
                var avgPoints = Math.Round(g.Average(r => r.Points), 1);
                var avgSpeed = Math.Round(g.Average(r => r.AverageSpeed), 2);
                var consistency = percentiles.Count >= 2
                    ? Math.Round(PopulationStdDev(percentiles), 1)
                    : 0;

                var comment = comments?.GetValueOrDefault(g.Key, FoodMixComparer.Instance);

                return new FoodMixPerformance(
                    g.Key,
                    flightCount,
                    avgPercentile,
                    avgPoints,
                    avgSpeed,
                    consistency,
                    comment,
                    BuildCategoryStats(g, DistanceCategory.Short),
                    BuildCategoryStats(g, DistanceCategory.Middle),
                    BuildCategoryStats(g, DistanceCategory.Long));
            })
            .OrderBy(m => m.AvgPercentile)
            .ToList();

        return new FoodImpactAnalysis(grouped);
    }

    private static FoodCategoryStats? BuildCategoryStats(
        IEnumerable<FlightResultListItem> results, DistanceCategory category)
    {
        var items = results.Where(r => r.Category == category).ToList();
        if (items.Count == 0)
            return null;

        return new FoodCategoryStats(
            items.Select(r => r.FlightId).Distinct().Count(),
            Math.Round(items.Average(r => r.Percentile), 1),
            Math.Round(items.Average(r => r.Points), 1),
            Math.Round(items.Average(r => r.AverageSpeed), 2));
    }

    private static double PopulationStdDev(IReadOnlyList<double> values)
    {
        var mean = values.Average();
        var sumSquares = values.Sum(v => (v - mean) * (v - mean));
        return Math.Sqrt(sumSquares / values.Count);
    }

    public sealed class FoodMixComparer : IEqualityComparer<FoodMix>
    {
        public static readonly FoodMixComparer Instance = new();

        public bool Equals(FoodMix? x, FoodMix? y) =>
            x is not null && y is not null
            && x.Barley == y.Barley && x.Grain == y.Grain
            && x.Corn == y.Corn && x.Peanut == y.Peanut;

        public int GetHashCode(FoodMix obj) =>
            HashCode.Combine(obj.Barley, obj.Grain, obj.Corn, obj.Peanut);
    }
}

internal static class DictionaryExtensions
{
    public static TValue? GetValueOrDefault<TKey, TValue>(
        this IReadOnlyDictionary<TKey, TValue> dict,
        TKey key,
        IEqualityComparer<TKey> comparer)
    {
        foreach (var kvp in dict)
        {
            if (comparer.Equals(kvp.Key, key))
                return kvp.Value;
        }

        return default;
    }
}
