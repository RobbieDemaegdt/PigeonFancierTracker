using PigeonFancierTracker.Core.Contracts;

namespace PigeonFancierTracker.Core.Analytics;

public static class MultiWindowComparer
{
    private static readonly int[] FixedWindows = [0, 1, 3, 6];

    public static IReadOnlyList<WindowPercentileRow> Compare<T>(
        DistanceStats target,
        int? targetAgeMonths,
        IReadOnlyList<T> items,
        Func<T, int?> ageSelector,
        Func<T, DistanceStats?> statsSelector,
        Func<T, PercentilePopulationMember?>? memberSelector = null)
    {
        return FixedWindows.Select(window =>
        {
            var filtered = AgeWindowFilter.FilterAtWindow(
                targetAgeMonths, items, ageSelector, window);

            var withStats = filtered.Items
                .Select(item => (Stats: statsSelector(item), Item: item))
                .Where(x => x.Stats is not null)
                .ToList();

            var population = withStats
                .Select(x => x.Stats!)
                .ToList();

            var members = memberSelector is not null
                ? withStats
                    .Select(x => memberSelector(x.Item))
                    .Where(m => m is not null)
                    .Cast<PercentilePopulationMember>()
                    .ToList()
                : null;

            var result = StatsComparer.Compare(target, population);

            return new WindowPercentileRow(
                AgeWindowFilter.FormatWindow(window) ?? "0m",
                window,
                population.Count,
                result?.ShortPercentile,
                result?.MediumPercentile,
                result?.LongPercentile,
                result?.TotalPercentile,
                members);
        }).ToList();
    }
}
