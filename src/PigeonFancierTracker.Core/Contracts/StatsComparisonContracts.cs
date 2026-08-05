namespace PigeonFancierTracker.Core.Contracts;

public sealed record DistanceStats(decimal Short, decimal Medium, decimal Long, decimal Total);

public sealed record StatsComparisonResult(
    decimal? ShortPercentile,
    decimal? MediumPercentile,
    decimal? LongPercentile,
    decimal? TotalPercentile);

public sealed record PercentilePopulationMember(
    string PigeonName,
    int? SourceId,
    decimal Short,
    decimal Medium,
    decimal Long,
    decimal Total,
    string? Breed,
    string? Age);
