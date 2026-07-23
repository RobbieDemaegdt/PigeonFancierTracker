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
    public int BidCount { get; set; }
    public DateTimeOffset? TransferStart { get; set; }
    public DateTimeOffset? TransferEnd { get; set; }
    public DateTimeOffset DetectedAtUtc { get; set; }
    public string? SkillsJson { get; set; }
}