using PigeonFancierTracker.Core.Contracts;
using PigeonFancierTracker.Core.Domain;

namespace PigeonFancierTracker.Core.Analytics;

/// <summary>
/// Analysis A: descriptive performance of each breed sliced by race condition
/// (distance, day/night, wind, temperature). Purely observational — it reports the
/// average placing per bucket and flags buckets with too few races to trust.
/// </summary>
public static class BreedAnalysisCalculator
{
    /// <summary>Beaufort at or above which a flight counts as "windy" (matches the flight scorer).</summary>
    public const int WindyBeaufort = 5;

    public static BreedAnalysisData Calculate(IReadOnlyList<FlightResultListItem> results)
    {
        var withBreed = results
            .Where(r => !string.IsNullOrWhiteSpace(r.Breed))
            .ToList();

        if (withBreed.Count == 0)
            return new BreedAnalysisData([]);

        var breeds = withBreed
            .GroupBy(r => r.Breed!, StringComparer.OrdinalIgnoreCase)
            .Select(BuildProfile)
            .OrderBy(p => p.OverallAvgPercentile)
            .ToList();

        return new BreedAnalysisData(breeds);
    }

    private static BreedProfile BuildProfile(IGrouping<string, FlightResultListItem> group)
    {
        var results = group.ToList();
        var conditions = new List<BreedConditionPerformance>();

        conditions.AddRange(BuildDimension(
            results, ConditionDimension.Distance,
            r => r.Category switch
            {
                DistanceCategory.Short => "Kort",
                DistanceCategory.Middle => "Midden",
                DistanceCategory.Long => "Lang",
                _ => null,
            }));

        conditions.AddRange(BuildDimension(
            results, ConditionDimension.DayNight,
            r => r.WeatherDay switch
            {
                true => "Dag",
                false => "Nacht",
                null => null,
            }));

        conditions.AddRange(BuildDimension(
            results, ConditionDimension.Wind,
            r => r.WeatherBeaufort switch
            {
                null => null,
                >= WindyBeaufort => "Wind (Bft ≥5)",
                _ => "Kalm",
            }));

        conditions.AddRange(BuildDimension(
            results, ConditionDimension.Temperature,
            r => r.WeatherTemperature switch
            {
                null => null,
                < 10m => "Koud (<10°)",
                <= 20m => "Mild (10–20°)",
                _ => "Warm (>20°)",
            }));

        var pigeonCount = results.Select(r => r.PigeonId).Distinct().Count();
        var totalFlights = results.Select(r => r.FlightId).Distinct().Count();
        var overallAvgPercentile = Math.Round(results.Average(r => r.Percentile), 1);

        var best = conditions
            .Where(c => c.IsReliable)
            .OrderBy(c => c.AvgPercentile)
            .FirstOrDefault();

        return new BreedProfile(
            group.Key,
            pigeonCount,
            totalFlights,
            overallAvgPercentile,
            best?.ConditionDisplay,
            conditions);
    }

    private static IEnumerable<BreedConditionPerformance> BuildDimension(
        IReadOnlyList<FlightResultListItem> results,
        ConditionDimension dimension,
        Func<FlightResultListItem, string?> bucketSelector)
    {
        return results
            .Select(r => (Bucket: bucketSelector(r), Result: r))
            .Where(x => x.Bucket is not null)
            .GroupBy(x => x.Bucket!, x => x.Result)
            .Select(g => BuildBucket(dimension, g.Key, g.ToList()))
            .OrderBy(b => b.AvgPercentile);
    }

    private static BreedConditionPerformance BuildBucket(
        ConditionDimension dimension,
        string bucket,
        IReadOnlyList<FlightResultListItem> results)
    {
        var percentiles = results.Select(r => r.Percentile).ToList();
        var consistency = ConsistencyCalculator.Calculate(percentiles);

        return new BreedConditionPerformance(
            dimension,
            bucket,
            Flights: results.Select(r => r.FlightId).Distinct().Count(),
            Pigeons: results.Select(r => r.PigeonId).Distinct().Count(),
            AvgPercentile: Math.Round(percentiles.Average(), 1),
            AvgPoints: Math.Round(results.Average(r => r.Points), 1),
            AvgSpeed: Math.Round(results.Average(r => r.AverageSpeed), 2),
            Consistency: consistency.StdDev,
            IsReliable: consistency.IsReliable);
    }
}
