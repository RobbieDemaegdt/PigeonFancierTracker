namespace PigeonFancierTracker.Infrastructure.Persistence;

public sealed class RawApiSnapshotEntity
{
    public long Id { get; set; }
    public required string Endpoint { get; set; }
    public string NormalizedQuery { get; set; } = string.Empty;
    public required string HttpMethod { get; set; }
    public int StatusCode { get; set; }
    public DateTimeOffset CapturedAtUtc { get; set; }
    public string? ContentType { get; set; }
    public required string ResponseBodyJson { get; set; }
    public required string BodySha256 { get; set; }
    public string ContractVersion { get; set; } = "v1";
    public int? SelectedFancierId { get; set; }
    public int? SourceSeasonId { get; set; }
    public string? ErrorDetails { get; set; }
}

public sealed class SyncRunEntity
{
    public long Id { get; set; }
    public DateTimeOffset StartedAtUtc { get; set; }
    public DateTimeOffset? CompletedAtUtc { get; set; }
    public required string Profile { get; set; }
    public required string Status { get; set; }
    public int? SelectedFancierId { get; set; }
    public string? ErrorDetails { get; set; }
}

public sealed class SyncRunItemEntity
{
    public long Id { get; set; }
    public long SyncRunId { get; set; }
    public required string Endpoint { get; set; }
    public string NormalizedQuery { get; set; } = string.Empty;
    public DateTimeOffset StartedAtUtc { get; set; }
    public DateTimeOffset? CompletedAtUtc { get; set; }
    public int Attempts { get; set; }
    public int StatusCode { get; set; }
    public required string Status { get; set; }
    public long? RawSnapshotId { get; set; }
    public string? ErrorDetails { get; set; }
}

public sealed class CompletedTransferEntity
{
    public long Id { get; set; }
    public int TransferId { get; set; }
    public int? PigeonId { get; set; }
    public int SelectedFancierId { get; set; }
    public required string Status { get; set; }
    public decimal? StartPrice { get; set; }
    public decimal? SoldPrice { get; set; }
    public string? Seller { get; set; }
    public string? SoldTo { get; set; }
    public string? PigeonName { get; set; }
    public string? Sex { get; set; }
    public string? Age { get; set; }
    public string? Breed { get; set; }
    public int BidCount { get; set; }
    public DateTimeOffset? TransferStart { get; set; }
    public DateTimeOffset? TransferEnd { get; set; }
    public DateTimeOffset DetectedAtUtc { get; set; }
    public string? SkillsJson { get; set; }
}

public sealed class FlightEntity
{
    public int Id { get; set; }
    public int Season { get; set; }
    public int Department { get; set; }
    public required string Type { get; set; }
    public required string PayoutType { get; set; }
    public required string Status { get; set; }
    public DateTime Start { get; set; }
    public string? LocationName { get; set; }
    public double? LocationLat { get; set; }
    public double? LocationLng { get; set; }
    public int DistanceKm { get; set; }
    public required string DistanceCategory { get; set; }
    public required string AgeType { get; set; }
    public decimal EntryPrice { get; set; }
    public int Subscribers { get; set; }
    public DateTimeOffset DetectedAtUtc { get; set; }
    public DateTimeOffset? ResultsFetchedAtUtc { get; set; }

    // Weather captured near flight time from the /api/weather forecast. Nullable
    // because it can only be recorded while the forecast still covers the flight
    // date; historical flights ingested before this feature stay null.
    public decimal? WeatherTemperature { get; set; }
    public decimal? WeatherHumidity { get; set; }
    public decimal? WeatherWind { get; set; }
    public int? WeatherBeaufort { get; set; }
    public bool? WeatherDay { get; set; }
    public string? WeatherCondition { get; set; }
    public DateTimeOffset? WeatherCapturedAtUtc { get; set; }

    // Per-age-category participant counts for national flights, captured on the
    // flight's own day from the ageType-filtered results endpoint. Nullable
    // because they're only fetched once, on flight day, and only for national
    // flights; every other flight stays null and falls back to the combined
    // prize table. AgeCategoryCountsCapturedAtUtc also gates re-querying.
    public int? AgeCategoryElderCount { get; set; }
    public int? AgeCategoryYearlingCount { get; set; }
    public int? AgeCategoryYouthCount { get; set; }
    public DateTimeOffset? AgeCategoryCountsCapturedAtUtc { get; set; }
}

public sealed class OffspringCacheEntity
{
    public long Id { get; set; }
    public int PigeonId { get; set; }
    public required string OffspringJson { get; set; }
    public DateTimeOffset FetchedAtUtc { get; set; }
}

public sealed class PedigreeCacheEntity
{
    public long Id { get; set; }
    public int PigeonId { get; set; }
    public required string PedigreeJson { get; set; }
    public DateTimeOffset FetchedAtUtc { get; set; }
}

public sealed class FoodDistributionSnapshotEntity
{
    public long Id { get; set; }
    public int SelectedFancierId { get; set; }
    public int Barley { get; set; }
    public int Grain { get; set; }
    public int Corn { get; set; }
    public int Peanut { get; set; }
    public string? Comment { get; set; }
    public DateTimeOffset CapturedAtUtc { get; set; }
}

public sealed class SponsorSnapshotEntity
{
    public long Id { get; set; }
    public int SelectedFancierId { get; set; }
    public int ContractId { get; set; }
    public int SponsorId { get; set; }
    public decimal Monthly { get; set; }
    public decimal Direct { get; set; }
    public int Runtime { get; set; }
    public int RuntimeRemaining { get; set; }
    public bool Signed { get; set; }
    public int Rating { get; set; }
    public decimal Total { get; set; }
    public DateTimeOffset? ContractEndUtc { get; set; }
    public bool CanCallSponsors { get; set; }
    public DateTimeOffset CapturedAtUtc { get; set; }
}

public sealed class FlightResultEntity
{
    public long Id { get; set; }
    public int FlightId { get; set; }
    public int PigeonId { get; set; }
    public int FancierId { get; set; }
    public int Position { get; set; }
    public int TotalParticipants { get; set; }
    public int Points { get; set; }
    public decimal AverageSpeed { get; set; }
    public int PigeonDistance { get; set; }
    public string? PigeonName { get; set; }
    public DateTimeOffset DetectedAtUtc { get; set; }
}