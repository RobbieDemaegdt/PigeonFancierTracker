using PigeonFancierTracker.Core.Contracts;
using PigeonFancierTracker.Core.Domain;

namespace PigeonFancierTracker.Core.Analytics;

public static class DistanceProfileCalculator
{
    public const int ShortMaxKm = 300;
    public const int MiddleMaxKm = 500;

    public static DistanceCategory Classify(int distanceKm)
    {
        if (distanceKm <= ShortMaxKm) return DistanceCategory.Short;
        if (distanceKm <= MiddleMaxKm) return DistanceCategory.Middle;
        return DistanceCategory.Long;
    }

    public static PigeonDistanceProfile Calculate(
        int pigeonId,
        string pigeonName,
        string? breed,
        IReadOnlyList<FlightResultListItem> results)
    {
        var shortResults = results.Where(r => r.Category == DistanceCategory.Short).ToList();
        var middleResults = results.Where(r => r.Category == DistanceCategory.Middle).ToList();
        var longResults = results.Where(r => r.Category == DistanceCategory.Long).ToList();

        var shortAgg = Aggregate(shortResults);
        var middleAgg = Aggregate(middleResults);
        var longAgg = Aggregate(longResults);

        var bestCategory = DetermineBestCategory(shortAgg, middleAgg, longAgg);
        var bestDisplay = bestCategory switch
        {
            DistanceCategory.Short => "Kort",
            DistanceCategory.Middle => "Midden",
            DistanceCategory.Long => "Lang",
            _ => null,
        };

        var overallConsistency = ConsistencyCalculator.Calculate(
            results.Select(r => r.Percentile).ToList());
        var shortConsistency = ConsistencyCalculator.Calculate(
            shortResults.Select(r => r.Percentile).ToList());
        var middleConsistency = ConsistencyCalculator.Calculate(
            middleResults.Select(r => r.Percentile).ToList());
        var longConsistency = ConsistencyCalculator.Calculate(
            longResults.Select(r => r.Percentile).ToList());

        return new PigeonDistanceProfile(
            pigeonId,
            pigeonName,
            breed,
            shortAgg.Races, shortAgg.AvgPosition, shortAgg.AvgPercentile, shortAgg.BestPosition, shortAgg.TotalPoints,
            middleAgg.Races, middleAgg.AvgPosition, middleAgg.AvgPercentile, middleAgg.BestPosition, middleAgg.TotalPoints,
            longAgg.Races, longAgg.AvgPosition, longAgg.AvgPercentile, longAgg.BestPosition, longAgg.TotalPoints,
            bestCategory,
            bestDisplay,
            overallConsistency.StdDev,
            ConsistencyCalculator.FormatDisplay(overallConsistency),
            shortConsistency.StdDev,
            middleConsistency.StdDev,
            longConsistency.StdDev);
    }

    private static CategoryAggregate Aggregate(IReadOnlyList<FlightResultListItem> results)
    {
        if (results.Count == 0)
            return new CategoryAggregate(0, 0, 0, 0, 0);

        return new CategoryAggregate(
            Races: results.Count,
            AvgPosition: Math.Round(results.Average(r => r.Position), 1),
            AvgPercentile: Math.Round(results.Average(r => r.Percentile), 1),
            BestPosition: results.Min(r => r.Position),
            TotalPoints: results.Sum(r => r.Points));
    }

    private static DistanceCategory? DetermineBestCategory(
        CategoryAggregate shortAgg,
        CategoryAggregate middleAgg,
        CategoryAggregate longAgg)
    {
        var candidates = new List<(DistanceCategory Category, double AvgPercentile, int Races)>();

        if (shortAgg.Races > 0) candidates.Add((DistanceCategory.Short, shortAgg.AvgPercentile, shortAgg.Races));
        if (middleAgg.Races > 0) candidates.Add((DistanceCategory.Middle, middleAgg.AvgPercentile, middleAgg.Races));
        if (longAgg.Races > 0) candidates.Add((DistanceCategory.Long, longAgg.AvgPercentile, longAgg.Races));

        if (candidates.Count == 0)
            return null;

        // Lower percentile = better (top 10% < top 50%)
        return candidates
            .OrderBy(c => c.AvgPercentile)
            .ThenByDescending(c => c.Races)
            .First()
            .Category;
    }

    private sealed record CategoryAggregate(
        int Races,
        double AvgPosition,
        double AvgPercentile,
        int BestPosition,
        int TotalPoints);
}
