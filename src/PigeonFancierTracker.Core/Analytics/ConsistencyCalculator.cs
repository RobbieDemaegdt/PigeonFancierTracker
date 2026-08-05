using PigeonFancierTracker.Core.Contracts;

namespace PigeonFancierTracker.Core.Analytics;

public static class ConsistencyCalculator
{
    public const int MinReliableRaces = 3;

    public static ConsistencyResult Calculate(IReadOnlyList<double> percentiles)
    {
        if (percentiles.Count == 0)
            return new ConsistencyResult(0, 0, false);

        if (percentiles.Count == 1)
            return new ConsistencyResult(0, 1, false);

        var mean = percentiles.Average();
        var sumSquaredDiffs = percentiles.Sum(p => (p - mean) * (p - mean));
        var stdDev = Math.Round(Math.Sqrt(sumSquaredDiffs / percentiles.Count), 1);

        return new ConsistencyResult(stdDev, percentiles.Count, percentiles.Count >= MinReliableRaces);
    }

    public static string FormatDisplay(ConsistencyResult result)
    {
        if (!result.IsReliable)
            return result.RaceCount == 0 ? "—" : $"{result.StdDev:0.0} (?)";

        return result.StdDev switch
        {
            <= 8 => $"{result.StdDev:0.0} (stabiel)",
            <= 15 => $"{result.StdDev:0.0} (gemiddeld)",
            _ => $"{result.StdDev:0.0} (wisselvallig)",
        };
    }
}
