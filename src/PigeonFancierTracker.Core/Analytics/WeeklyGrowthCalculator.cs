namespace PigeonFancierTracker.Core.Analytics;

public sealed record MetricObservation(
    string SourceId,
    DateTimeOffset ObservedAtUtc,
    decimal? Value);

public sealed record WeeklyGrowthResult(
    string SourceId,
    decimal? CurrentValue,
    decimal? PreviousValue,
    decimal? AbsoluteGrowth,
    decimal? PercentageGrowth,
    bool IsNewlyObserved,
    bool HasMissingData);

public static class WeeklyGrowthCalculator
{
    public static WeeklyGrowthResult Calculate(
        string sourceId,
        IEnumerable<MetricObservation> observations)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sourceId);
        ArgumentNullException.ThrowIfNull(observations);

        var ordered = observations
            .Where(x => string.Equals(x.SourceId, sourceId, StringComparison.Ordinal))
            .OrderByDescending(x => x.ObservedAtUtc)
            .Take(2)
            .ToArray();

        var current = ordered.ElementAtOrDefault(0)?.Value;
        var previous = ordered.ElementAtOrDefault(1)?.Value;
        var hasMissingData = current is null || (ordered.Length > 1 && previous is null);
        var absoluteGrowth = current.HasValue && previous.HasValue ? current - previous : null;
        var percentageGrowth = previous is not null and not 0 && absoluteGrowth is not null
            ? absoluteGrowth / previous * 100
            : null;

        return new WeeklyGrowthResult(
            sourceId,
            current,
            previous,
            absoluteGrowth,
            percentageGrowth,
            ordered.Length == 1,
            hasMissingData);
    }
}