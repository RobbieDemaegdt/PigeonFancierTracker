namespace PigeonFancierTracker.Core.Contracts;

public sealed record ActiveSponsorInfo(
    int ContractId,
    int SponsorId,
    decimal Monthly,
    decimal Direct,
    int Runtime,
    int RuntimeRemaining,
    int Rating,
    decimal Total,
    DateTimeOffset? ContractEnd);

public sealed record SponsorDealScore(
    double Score,
    string Verdict,
    string Recommendation);

public sealed record SponsorHistoryEntry(
    decimal Monthly,
    decimal Direct,
    int Runtime,
    int Rating,
    decimal Total,
    DateTimeOffset CapturedAt);

public sealed record SponsorOverview(
    IReadOnlyList<ActiveSponsorInfo> ActiveSponsors,
    IReadOnlyList<ScoredSponsorInfo> PendingOffers,
    decimal TotalMonthlyIncome,
    bool CanCallSponsors,
    decimal HistoricalAvgMonthly,
    double OfferTrendPercent,
    string OfferTrendDirection,
    IReadOnlyList<ScoredSponsorInfo> ScoredSponsors,
    IReadOnlyList<ScoredSponsorInfo> ExpiredSponsors);

public sealed record ScoredSponsorInfo(
    ActiveSponsorInfo Sponsor,
    SponsorDealScore Score);

public interface ISponsorDataReader
{
    Task<SponsorOverview> GetSponsorOverviewAsync(int selectedFancierId, CancellationToken ct = default);
}
