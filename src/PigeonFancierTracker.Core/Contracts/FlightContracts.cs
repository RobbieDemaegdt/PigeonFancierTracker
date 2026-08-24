using PigeonFancierTracker.Core.Analytics;
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

public sealed record FoodMix(int Barley, int Grain, int Corn, int Peanut)
{
    public string Display => $"B:{Barley}% G:{Grain}% C:{Corn}% P:{Peanut}%";
}

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
    int PigeonId,
    FoodMix? FoodMix = null,
    string? Breed = null,
    bool? WeatherDay = null,
    int? WeatherBeaufort = null,
    decimal? WeatherTemperature = null,
    string? WeatherCondition = null)
{
    public string? FoodMixDisplay => FoodMix?.Display;
}

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
    IReadOnlyList<PigeonDistanceProfile> PigeonProfiles,
    FoodImpactAnalysis? FoodAnalysis = null,
    BreedAnalysisData? BreedAnalysis = null,
    BreedSkillAnalysisData? BreedSkillAnalysis = null);

// --- Age-category prizes (national flights) ---

/// <summary>
/// The three age categories a national flight is prized in. The member names are
/// the exact <c>ageType</c> query values used by the flight results endpoint.
/// </summary>
public enum AgeCategory { Elder, Yearling, Youth }

/// <summary>
/// Prize breakdown for one age category of a national flight. Prize money is a
/// flat 10 EUR per point (see <see cref="PrizeCalculator.PrizeMoneyPerPoint"/>),
/// so it depends only on the category's own participant count — never on a pool.
/// </summary>
public sealed record AgeCategoryPrizeInfo(
    AgeCategory Category,
    int Participants,
    int PrizePositions,
    decimal TotalPrizeMoney,
    IReadOnlyList<PrizeTier> PrizeTable);

// --- Upcoming & active flight view models ---

public sealed record UpcomingFlightInfo(
    int FlightId,
    DateTime Start,
    string? Location,
    string FlightType,
    int DistanceKm,
    DistanceCategory Category,
    int Subscribers,
    decimal EntryPrice,
    IReadOnlyList<PrizeTier> PrizeTable,
    int TotalPrizePositions,
    IReadOnlyList<AgeCategoryPrizeInfo>? AgeCategoryPrizes = null);

public sealed record ActiveFlightInfo(
    int FlightId,
    DateTime Start,
    string? Location,
    string FlightType,
    int DistanceKm,
    DistanceCategory Category,
    int Subscribers,
    int Progress,
    IReadOnlyList<ActivePigeonStanding> PigeonStandings,
    IReadOnlyList<ActiveFancierStanding> FancierStandings,
    IReadOnlyList<PrizeTier> PrizeTable,
    decimal EntryPrice = 0,
    decimal TotalPrizePool = 0,
    IReadOnlyList<AgeCategoryPrizeInfo>? AgeCategoryPrizes = null);

public sealed record ActivePigeonStanding(
    int PigeonId,
    string PigeonName,
    int Position,
    int Points,
    decimal CurrentSpeed,
    int RemainingDistance,
    int Progress,
    int FancierId,
    string? FancierName,
    decimal PrizeMoney = 0);

public sealed record ActiveFancierStanding(
    string FancierName,
    int FancierId,
    int TotalPoints,
    int PigeonCount,
    int BestPosition,
    bool IsOwn,
    decimal TotalPrizeMoney = 0,
    int PrizePigeonCount = 0);

public sealed record CompletedFlightSummary(
    int FlightId,
    DateTime FlightDate,
    string? Location,
    string FlightType,
    int DistanceKm,
    DistanceCategory Category,
    int OwnPigeonCount,
    int BestPosition,
    int TotalParticipants,
    int TotalPoints,
    IReadOnlyList<PrizeTier> PrizeTable,
    int TotalPrizePositions,
    IReadOnlyList<AgeCategoryPrizeInfo>? AgeCategoryPrizes = null);

public interface IFlightResultsReader
{
    Task<FlightResultsPageData> GetFlightResultsAsync(int fancierId);
    Task<IReadOnlyList<UpcomingFlightInfo>> GetUpcomingFlightsAsync(int fancierId);
    Task<IReadOnlyList<ActiveFlightInfo>> GetActiveFlightsAsync(int fancierId);
    Task<IReadOnlyList<CompletedFlightSummary>> GetCompletedFlightSummariesAsync(int fancierId);
    Task SaveFoodCommentAsync(int fancierId, FoodMix mix, string? comment);
}
