namespace PigeonFancierTracker.Core.Contracts;

public sealed record PriceEstimationRequest(
    decimal? TotalSkill,
    int? AgeMonths,
    string? Breed,
    decimal? Form,
    decimal? Experience,
    decimal? Speed,
    decimal? Technique,
    decimal? Stamina,
    decimal? Aerodynamics,
    decimal? Intelligence,
    decimal? Libido,
    decimal? Nightvision,
    decimal? Navigation);

public sealed record PriceEstimationResult(
    decimal EstimatedPrice,
    decimal? MinComparablePrice,
    decimal? MaxComparablePrice,
    int ComparableCount,
    PriceConfidence Confidence,
    IReadOnlyList<ComparablePigeon> Comparables);

public enum PriceConfidence
{
    Low,
    Medium,
    High,
}

public sealed record ComparablePigeon(
    string PigeonName,
    int? TransferId,
    decimal SoldPrice,
    decimal AdjustedPrice,
    double Similarity,
    string? Breed,
    int? AgeMonths,
    decimal? TotalSkill)
{
    public string? AgeDisplay => AgeMonths is int m
        ? $"{m / 12}j {m % 12}m"
        : null;
}

public sealed record CompletedTransferSummary(
    decimal SoldPrice,
    decimal? TotalSkill,
    int? AgeMonths,
    string? Breed,
    int BidCount,
    string? PigeonName,
    int? TransferId,
    decimal? Form,
    decimal? Experience,
    decimal? Speed,
    decimal? Technique,
    decimal? Stamina,
    decimal? Aerodynamics,
    decimal? Intelligence,
    decimal? Libido,
    decimal? Nightvision,
    decimal? Navigation);
