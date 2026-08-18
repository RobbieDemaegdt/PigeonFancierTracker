namespace PigeonFancierTracker.Core.Contracts;

public enum MarketValueClassification
{
    Koopje,
    EerlijkePrijs,
    TeDuur,
    Uitzonderlijk,
}

public enum SellValueClassification
{
    HogeWaarde,
    Gemiddeld,
    LageWaarde,
}

public sealed record SkillProfileSummary(
    string HighestSkill,
    decimal HighestValue,
    string LowestSkill,
    decimal LowestValue,
    decimal SkillRange,
    bool IsSpecialist);

public sealed record BuyRecommendation(
    int TransferId,
    string PigeonName,
    string? Sex,
    string? Age,
    string? Breed,
    decimal AskingPrice,
    decimal EstimatedMarketValue,
    decimal ValueRatio,
    decimal SkillPerEuro,
    string AgePhase,
    decimal AgeAdjustedValue,
    MarketValueClassification Classification,
    string ClassificationDisplay,
    bool IsOutlier,
    string? OutlierReason,
    decimal? TotalSkill,
    SkillProfileSummary? SkillProfile,
    PriceConfidence Confidence,
    string TimeRemaining);

public sealed record SellValueEstimate(
    int? PigeonId,
    string PigeonName,
    string? Sex,
    string? Age,
    string? Breed,
    decimal? TotalSkill,
    decimal EstimatedSellPrice,
    decimal? MinEstimate,
    decimal? MaxEstimate,
    string? OptimalTiming,
    string? MarketTrendImpact,
    int? TotalPoints,
    int? RaceCount,
    string? EarningsDisplay,
    SellValueClassification Classification,
    string ClassificationDisplay,
    PriceConfidence Confidence);

public sealed record MarketAnalysisPageData(
    IReadOnlyList<BuyRecommendation> BuyRecommendations,
    IReadOnlyList<SellValueEstimate> SellEstimates,
    MarketTrendResult? MarketTrend,
    AuctionTimingResult? AuctionTiming,
    int KoopjeCount,
    int TeDuurCount,
    decimal AvgSkillPerEuro,
    string? TopBargainName);

public interface IMarketAnalysisReader
{
    Task<MarketAnalysisPageData> GetMarketAnalysisAsync(
        int selectedFancierId,
        TransferPageData? preComputed = null,
        CancellationToken cancellationToken = default);
}
