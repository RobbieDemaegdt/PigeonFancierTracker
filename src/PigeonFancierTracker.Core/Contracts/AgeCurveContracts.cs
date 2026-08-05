namespace PigeonFancierTracker.Core.Contracts;

public sealed record AgeCurvePoint(
    int AgeMonths,
    decimal TotalSkill,
    double? AvgPercentile);

public sealed record AgeBucket(
    int AgeMonths,
    decimal AvgTotalSkill,
    double? AvgPercentile,
    int ObservationCount);

public sealed record AgeCurveResult(
    IReadOnlyList<AgeBucket> Buckets,
    int? PeakSkillAge,
    int? PeakPerformanceAge,
    decimal? PeakTotalSkill,
    string? PhaseDisplay);
