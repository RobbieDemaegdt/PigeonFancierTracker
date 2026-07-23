namespace PigeonFancierTracker.Core.Contracts;

public sealed record PriceEstimationRequest(
    decimal? TotalSkill,
    int? AgeMonths,
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
    PriceConfidence Confidence);

public enum PriceConfidence
{
    Low,
    Medium,
    High,
}

public sealed record CompletedTransferSummary(
    decimal SoldPrice,
    decimal? TotalSkill,
    int? AgeMonths,
    int BidCount,
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
