using PigeonFancierTracker.Core.Domain;

namespace PigeonFancierTracker.Core.Contracts;

// --- API DTOs ---

public sealed record FlightDto(
    int Id,
    string AgeType,
    DateTime Start,
    FlightLocationDto? Location,
    bool Public,
    int Department,
    int Season,
    DateTime SeasonStart,
    decimal EntryPrice,
    string Type,
    string PayoutType,
    string Status,
    int Progress,
    bool CanSubscribe,
    int? FancierId,
    int Distance,
    int Subscribers,
    int[]? Invitations);

public sealed record FlightLocationDto(
    int Id,
    string Name,
    double Lat,
    double Lng);

public sealed record FlightResultDto(
    int Id,
    int Position,
    int Points,
    decimal AverageSpeed,
    int AgeType,
    int AgePosition,
    decimal CurrentSpeed,
    int Distance,
    int RemainingDistance,
    int Direction,
    int Progress,
    int PigeonId,
    int? FirstNameId,
    int? LastNameId,
    int FancierId,
    string? Fancier);

public sealed record FlightResultsResponse(
    FlightResultDto[] Items,
    int Count,
    int Page);

public sealed record PigeonResultDto(
    int Id,
    int Position,
    int Points,
    int AgeType,
    int? AgePosition,
    int Direction,
    decimal Progress,
    int PigeonId,
    PigeonResultFlightDto Flight);

public sealed record PigeonResultFlightDto(
    int Id,
    string AgeType,
    DateTime Start,
    PigeonResultLocationDto? Location,
    bool Public,
    int Season,
    string Type,
    string Status,
    bool CanSubscribe,
    int Subscribers);

public sealed record PigeonResultLocationDto(
    int Id,
    string Name,
    double Lat,
    double Lng);

// --- View models ---

public sealed record FlightResultListItem(
    int FlightId,
    DateTime FlightDate,
    string FlightType,
    string? Location,
    int FlightDistanceKm,
    DistanceCategory Category,
    int Position,
    int TotalParticipants,
    double Percentile,
    int Points,
    decimal AverageSpeed,
    string PigeonName,
    int PigeonId);

public sealed record PigeonDistanceProfile(
    int PigeonId,
    string PigeonName,
    string? Breed,
    int ShortRaces,
    double ShortAvgPosition,
    double ShortAvgPercentile,
    int ShortBestPosition,
    int ShortTotalPoints,
    int MiddleRaces,
    double MiddleAvgPosition,
    double MiddleAvgPercentile,
    int MiddleBestPosition,
    int MiddleTotalPoints,
    int LongRaces,
    double LongAvgPosition,
    double LongAvgPercentile,
    int LongBestPosition,
    int LongTotalPoints,
    DistanceCategory? BestCategory,
    string? BestCategoryDisplay,
    double OverallConsistency = 0,
    string? ConsistencyDisplay = null,
    double ShortConsistency = 0,
    double MiddleConsistency = 0,
    double LongConsistency = 0);

public sealed record FlightResultsPageData(
    IReadOnlyList<FlightResultListItem> RecentResults,
    IReadOnlyList<PigeonDistanceProfile> PigeonProfiles);

public interface IFlightResultsReader
{
    Task<FlightResultsPageData> GetFlightResultsAsync(int fancierId);
}
