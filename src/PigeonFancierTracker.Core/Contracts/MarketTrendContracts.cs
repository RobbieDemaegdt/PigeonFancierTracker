namespace PigeonFancierTracker.Core.Contracts;

public sealed record MarketSalePoint(
    decimal SoldPrice,
    decimal TotalSkill,
    DateTimeOffset SoldAt);

public sealed record MarketTrendBucket(
    string Period,
    decimal AvgPricePerSkill,
    int SaleCount);

public sealed record MarketTrendResult(
    IReadOnlyList<MarketTrendBucket> Buckets,
    string TrendDirection,
    decimal CurrentAvgPricePerSkill,
    decimal PreviousAvgPricePerSkill,
    decimal TrendPercentage);
