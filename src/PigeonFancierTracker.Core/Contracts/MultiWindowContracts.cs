namespace PigeonFancierTracker.Core.Contracts;

public sealed record WindowPercentileRow(
    string WindowLabel,
    int WindowMonths,
    int PopulationCount,
    decimal? ShortPercentile,
    decimal? MediumPercentile,
    decimal? LongPercentile,
    decimal? TotalPercentile,
    IReadOnlyList<PercentilePopulationMember>? Population = null);

public sealed record WindowPriceRow(
    string WindowLabel,
    int WindowMonths,
    int ComparableCount,
    decimal? EstimatedPrice,
    decimal? MinPrice,
    decimal? MaxPrice,
    PriceConfidence? Confidence,
    IReadOnlyList<ComparablePigeon>? Comparables = null);
