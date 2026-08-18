namespace PigeonFancierTracker.Core.Contracts;

public sealed record RankingPageData(
    IReadOnlyList<RankingEntry> RegionalFanciers,
    IReadOnlyList<PigeonRankingEntry> RegionalPigeons,
    IReadOnlyList<RankingEntry> NationalFanciers,
    IReadOnlyList<PigeonRankingEntry> NationalPigeons,
    IReadOnlyList<PredictedRankingEntry>? PredictedRegionalFanciers = null);

public sealed record RankingEntry(
    int Position,
    string Name,
    int Id,
    int Points,
    bool IsOwn = false);

public sealed record PigeonRankingEntry(
    int Position,
    string PigeonName,
    int PigeonId,
    string FancierName,
    int FancierId,
    int Points,
    bool IsOwn = false);

public sealed record PredictedRankingEntry(
    int Position,
    string Name,
    int Id,
    int CurrentPoints,
    int PredictedPoints,
    int PointsDelta,
    int PositionDelta,
    bool IsOwn = false);

public interface IRankingDataReader
{
    Task<RankingPageData> GetRankingAsync(int fancierId);
    Task<IReadOnlyList<PredictedRankingEntry>> GetPredictedRankingAsync(
        int fancierId,
        IReadOnlyList<ActiveFlightInfo> activeFlights);
}
