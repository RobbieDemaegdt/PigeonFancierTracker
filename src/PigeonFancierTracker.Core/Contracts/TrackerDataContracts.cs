namespace PigeonFancierTracker.Core.Contracts;

public sealed record PigeonListItem(
    string DisplayName,
    int? SourceId,
    string? Sex,
    string? Age,
    string? Breed,
    decimal? TotalSkill,
    string? TotalSkillDisplay,
    string? FormDisplay,
    string? ExperienceDisplay,
    string? SpeedDisplay,
    string? TechniqueDisplay,
    string? StaminaDisplay,
    string? AerodynamicsDisplay,
    string? IntelligenceDisplay,
    string? LibidoDisplay,
    string? NightvisionDisplay,
    string? NavigationDisplay,
    decimal? Premium,
    bool? Flying,
    string? Disease,
    string? TrainingDisplay,
    decimal? SkillChange,
    string? BreedingMark,
    string? ShortDisplay,
    string? MediumDisplay,
    string? LongDisplay);

public sealed record TrackerDashboardData(
    string? FancierName,
    int? FancierId,
    int? PigeonCount,
    decimal? Capital,
    decimal? PreviousBalance,
    decimal? TransferBalance,
    decimal? AverageTotalSkill,
    decimal? AverageSkillChange,
    DateTimeOffset? LastDataCapturedAtUtc,
    int? SeasonNumber,
    int? SeasonWeek,
    int? PenOccupied,
    int? PenCapacity,
    string? LocationName,
    decimal? FoodAmount,
    IReadOnlyList<PigeonListItem> Pigeons);

public sealed record PigeonHistoryPigeon(
    int SourceId,
    string DisplayName,
    string? Sex,
    string? Breed,
    string? Age,
    DateTimeOffset? FirstObservedAtUtc,
    DateTimeOffset? LastObservedAtUtc);

public sealed record PigeonHistoryPoint(
    DateTimeOffset ObservedAtUtc,
    decimal? TotalSkill,
    decimal? Form,
    decimal? Experience,
    decimal? Speed,
    decimal? Technique,
    decimal? Stamina,
    decimal? Aerodynamics,
    decimal? Intelligence,
    decimal? Libido,
    decimal? Nightvision,
    decimal? Navigation,
    decimal? Premium,
    bool? Flying,
    string? Disease,
    long SourceSnapshotId,
    string? TotalSkillDisplay = null,
    string? FormDisplay = null,
    string? ExperienceDisplay = null,
    string? SpeedDisplay = null,
    string? TechniqueDisplay = null,
    string? StaminaDisplay = null,
    string? AerodynamicsDisplay = null,
    string? IntelligenceDisplay = null,
    string? LibidoDisplay = null,
    string? NightvisionDisplay = null,
    string? NavigationDisplay = null,
    string? ShortDisplay = null,
    string? MediumDisplay = null,
    string? LongDisplay = null);

public sealed record PigeonHistoryData(
    IReadOnlyList<PigeonHistoryPigeon> Pigeons,
    PigeonHistoryPigeon? SelectedPigeon,
    IReadOnlyList<PigeonHistoryPoint> Points);

public interface ITrackerDataReader
{
    Task<TrackerDashboardData> GetDashboardAsync(
        int selectedFancierId,
        CancellationToken cancellationToken = default);
}

    public interface IPigeonHistoryReader
    {
        Task<PigeonHistoryData> GetHistoryAsync(
        int selectedFancierId,
        int? pigeonId = null,
        CancellationToken cancellationToken = default);
    }