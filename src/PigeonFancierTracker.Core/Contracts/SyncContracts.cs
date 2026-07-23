using PigeonFancierTracker.Core.Domain;

namespace PigeonFancierTracker.Core.Contracts;

public sealed record SyncProgress(
    long SyncRunId,
    SyncProfile Profile,
    string Endpoint,
    int CompletedEndpoints,
    int TotalEndpoints,
    int? StatusCode,
    bool IsSuccess,
    string? ErrorMessage);

public sealed record SyncEndpointResult(
    string Endpoint,
    int StatusCode,
    bool IsSuccess,
    int Attempts,
    long? SnapshotId,
    string? ErrorMessage);

public sealed record SyncRunResult(
    long SyncRunId,
    SyncProfile Profile,
    string Status,
    IReadOnlyList<SyncEndpointResult> EndpointResults,
    DateTimeOffset StartedAtUtc,
    DateTimeOffset CompletedAtUtc)
{
    public bool IsSuccess => Status is "Succeeded" or "PartiallySucceeded";
}

public interface ISyncCoordinator
{
    bool IsRunning { get; }

    event EventHandler<SyncProgress>? ProgressChanged;

    Task<SyncRunResult> SyncAsync(
        SyncProfile profile,
        CancellationToken cancellationToken = default);

    void Cancel();
}