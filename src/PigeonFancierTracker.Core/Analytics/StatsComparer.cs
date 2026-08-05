using PigeonFancierTracker.Core.Contracts;

namespace PigeonFancierTracker.Core.Analytics;

public static class StatsComparer
{
    public static StatsComparisonResult? Compare(
        DistanceStats target,
        IReadOnlyList<DistanceStats> population)
    {
        ArgumentNullException.ThrowIfNull(target);
        ArgumentNullException.ThrowIfNull(population);

        if (population.Count == 0)
            return null;

        return new StatsComparisonResult(
            ComputePercentile(target.Short, population.Select(p => p.Short).ToList()),
            ComputePercentile(target.Medium, population.Select(p => p.Medium).ToList()),
            ComputePercentile(target.Long, population.Select(p => p.Long).ToList()),
            ComputePercentile(target.Total, population.Select(p => p.Total).ToList()));
    }

    internal static decimal? ComputePercentile(decimal targetValue, IReadOnlyList<decimal> population)
    {
        if (population.Count == 0)
            return null;

        int countAtOrBelow = population.Count(v => v <= targetValue);
        return Math.Round((decimal)countAtOrBelow / population.Count * 100, 1);
    }
}
