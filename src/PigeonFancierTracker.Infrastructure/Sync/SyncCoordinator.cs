using System.Collections.Concurrent;
using System.Net;
using System.Net.Http;
using System.Runtime.InteropServices;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using PigeonFancierTracker.Core.Contracts;
using PigeonFancierTracker.Core.Domain;
using PigeonFancierTracker.Infrastructure.Persistence;
using PigeonFancierTracker.Infrastructure.PigeonFancierApi;

namespace PigeonFancierTracker.Infrastructure.Sync;

public sealed class SyncCoordinator(
    PigeonFancierApiClient apiClient,
    RawSnapshotStore snapshotStore,
    IDbContextFactory<AppDbContext> contextFactory,
    ISessionStateService sessionState) : ISyncCoordinator
{
    private const int MaximumConcurrency = 4;
    private const int MaximumAttempts = 3;
    private static readonly TimeSpan RequestTimeout = TimeSpan.FromSeconds(15);
    private readonly object runGate = new();
    private CancellationTokenSource? activeRunCancellation;

    public bool IsRunning
    {
        get
        {
            lock (runGate)
            {
                return activeRunCancellation is not null;
            }
        }
    }

    public event EventHandler<SyncProgress>? ProgressChanged;

    public async Task<SyncRunResult> SyncAsync(
        SyncProfile profile,
        CancellationToken cancellationToken = default)
    {
        var selectedFancierId = sessionState.Current.SelectedFancier?.Id
            ?? throw new InvalidOperationException("A selected fancier is required before synchronization.");
        if (sessionState.Current.State != SessionState.AuthenticatedReady)
        {
            throw new InvalidOperationException("The authenticated session is not ready for synchronization.");
        }

        using var runCancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        lock (runGate)
        {
            if (activeRunCancellation is not null)
            {
                throw new InvalidOperationException("A synchronization is already running.");
            }

            activeRunCancellation = runCancellation;
        }

        var startedAt = DateTimeOffset.UtcNow;
        var syncRunId = await CreateSyncRunAsync(profile, selectedFancierId, startedAt, runCancellation.Token);
        var (season, department) = await ReadSeasonAndDepartmentAsync(selectedFancierId, runCancellation.Token);
        var activeFlightIds = await ReadActiveFlightIdsAsync(selectedFancierId, runCancellation.Token);
        var endpoints = SyncEndpointCatalog.ForProfile(profile, selectedFancierId, season, department, activeFlightIds);
        var results = new ConcurrentBag<SyncEndpointResult>();
        var completedEndpoints = 0;
        var sessionExpired = 0;
        using var concurrency = new SemaphoreSlim(MaximumConcurrency, MaximumConcurrency);

        try
        {
            var work = endpoints.Select(async endpoint =>
            {
                await concurrency.WaitAsync(runCancellation.Token);
                try
                {
                    var result = await ExecuteEndpointAsync(
                        syncRunId,
                        endpoint,
                        selectedFancierId,
                        runCancellation,
                        () => Interlocked.Increment(ref sessionExpired));
                    results.Add(result);
                    var completed = Interlocked.Increment(ref completedEndpoints);
                    RaiseProgress(new SyncProgress(
                        syncRunId,
                        profile,
                        endpoint.Path,
                        completed,
                        endpoints.Count,
                        result.StatusCode,
                        result.IsSuccess,
                        result.ErrorMessage));
                }
                finally
                {
                    concurrency.Release();
                }
            });

            try
            {
                await Task.WhenAll(work);
            }
            catch (OperationCanceledException) when (runCancellation.IsCancellationRequested)
            {
                // The run is recorded as cancelled below. Completed endpoint results remain persisted.
            }

            if (!runCancellation.IsCancellationRequested && profile != SyncProfile.Quick)
            {
                await FetchNewlyDiscoveredFlightResultsAsync(
                    syncRunId, selectedFancierId, activeFlightIds,
                    results, concurrency, runCancellation,
                    () => Interlocked.Increment(ref sessionExpired));
            }

            var completedAt = DateTimeOffset.UtcNow;
            var resultList = results.OrderBy(x => x.Endpoint, StringComparer.Ordinal).ToArray();
            var status = sessionExpired > 0
                ? "SessionExpired"
                : runCancellation.IsCancellationRequested
                    ? "Cancelled"
                    : resultList.Length == 0 || resultList.All(x => !x.IsSuccess)
                        ? "Failed"
                        : resultList.All(x => x.IsSuccess)
                            ? "Succeeded"
                            : "PartiallySucceeded";

            await CompleteSyncRunAsync(syncRunId, completedAt, status, resultList, CancellationToken.None);
            return new SyncRunResult(syncRunId, profile, status, resultList, startedAt, completedAt);
        }
        finally
        {
            lock (runGate)
            {
                activeRunCancellation = null;
            }
        }
    }

    public void Cancel()
    {
        lock (runGate)
        {
            activeRunCancellation?.Cancel();
        }
    }

    private async Task<SyncEndpointResult> ExecuteEndpointAsync(
        long syncRunId,
        SyncEndpoint endpoint,
        int selectedFancierId,
        CancellationTokenSource runCancellation,
        Action onSessionExpired)
    {
        var startedAt = DateTimeOffset.UtcNow;
        var attempts = 0;
        long? finalSnapshotId = null;
        var lastStatusCode = 0;
        string? lastError = null;

        for (attempts = 1; attempts <= MaximumAttempts; attempts++)
        {
            runCancellation.Token.ThrowIfCancellationRequested();
            try
            {
                using var requestTimeout = CancellationTokenSource.CreateLinkedTokenSource(runCancellation.Token);
                requestTimeout.CancelAfter(RequestTimeout);
                var response = await apiClient.GetJsonAsync(endpoint.Path, endpoint.Query, requestTimeout.Token);
                lastStatusCode = response.StatusCode;
                var isUnauthorized = response.StatusCode == (int)HttpStatusCode.Unauthorized;
                var isTransient = IsTransientStatus(response.StatusCode);
                lastError = response.IsSuccessStatusCode ? null : $"HTTP {response.StatusCode}";
                finalSnapshotId = await snapshotStore.SaveAsync(
                    endpoint.Path,
                    endpoint.Query,
                    response,
                    selectedFancierId,
                    errorDetails: lastError,
                    cancellationToken: CancellationToken.None);

                if (isUnauthorized)
                {
                    sessionState.SetState(
                        SessionState.SessionExpired,
                        errorMessage: "The Pigeon Fancier session has expired.");
                    onSessionExpired();
                    runCancellation.Cancel();
                }

                if (!isTransient || isUnauthorized || attempts == MaximumAttempts)
                {
                    var result = new SyncEndpointResult(
                        FormatEndpoint(endpoint),
                        lastStatusCode,
                        response.IsSuccessStatusCode,
                        attempts,
                        finalSnapshotId,
                        lastError);
                    await SaveSyncRunItemAsync(syncRunId, endpoint, startedAt, result, CancellationToken.None);
                    return result;
                }

                await DelayBeforeRetryAsync(response, attempts, runCancellation.Token);
            }
            catch (OperationCanceledException) when (!runCancellation.IsCancellationRequested && attempts < MaximumAttempts)
            {
                lastError = "Request timed out.";
                await DelayBeforeRetryAsync(null, attempts, runCancellation.Token);
            }
            catch (OperationCanceledException) when (!runCancellation.IsCancellationRequested)
            {
                lastError = "Request timed out.";
                var response = new TransportResponse(0, null, string.Empty, new Dictionary<string, string>());
                finalSnapshotId = await snapshotStore.SaveAsync(
                    endpoint.Path,
                    endpoint.Query,
                    response,
                    selectedFancierId,
                    errorDetails: lastError,
                    cancellationToken: CancellationToken.None);
                var result = new SyncEndpointResult(
                    FormatEndpoint(endpoint),
                    0,
                    false,
                    attempts,
                    finalSnapshotId,
                    lastError);
                await SaveSyncRunItemAsync(syncRunId, endpoint, startedAt, result, CancellationToken.None);
                return result;
            }
            catch (Exception exception) when (IsTransientException(exception) && attempts < MaximumAttempts)
            {
                lastError = exception.Message;
                await DelayBeforeRetryAsync(null, attempts, runCancellation.Token);
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception exception)
            {
                lastError = exception.Message;
                var response = new TransportResponse(0, null, string.Empty, new Dictionary<string, string>());
                finalSnapshotId = await snapshotStore.SaveAsync(
                    endpoint.Path,
                    endpoint.Query,
                    response,
                    selectedFancierId,
                    errorDetails: lastError,
                    cancellationToken: CancellationToken.None);
                var result = new SyncEndpointResult(
                    FormatEndpoint(endpoint),
                    0,
                    false,
                    attempts,
                    finalSnapshotId,
                    lastError);
                await SaveSyncRunItemAsync(syncRunId, endpoint, startedAt, result, CancellationToken.None);
                return result;
            }
        }

        throw new InvalidOperationException("Synchronization endpoint exited without a result.");
    }

    private async Task<long> CreateSyncRunAsync(
        SyncProfile profile,
        int selectedFancierId,
        DateTimeOffset startedAt,
        CancellationToken cancellationToken)
    {
        await using var db = await contextFactory.CreateDbContextAsync(cancellationToken);
        var syncRun = new SyncRunEntity
        {
            StartedAtUtc = startedAt,
            Profile = profile.ToString(),
            Status = "Running",
            SelectedFancierId = selectedFancierId,
        };
        db.SyncRuns.Add(syncRun);
        await db.SaveChangesAsync(cancellationToken);
        return syncRun.Id;
    }

    private async Task SaveSyncRunItemAsync(
        long syncRunId,
        SyncEndpoint endpoint,
        DateTimeOffset startedAt,
        SyncEndpointResult result,
        CancellationToken cancellationToken)
    {
        await using var db = await contextFactory.CreateDbContextAsync(cancellationToken);
        db.SyncRunItems.Add(new SyncRunItemEntity
        {
            SyncRunId = syncRunId,
            Endpoint = endpoint.Path,
            NormalizedQuery = RawSnapshotStore.NormalizeQuery(endpoint.Query),
            StartedAtUtc = startedAt,
            CompletedAtUtc = DateTimeOffset.UtcNow,
            Attempts = result.Attempts,
            StatusCode = result.StatusCode,
            Status = result.IsSuccess ? "Succeeded" : "Failed",
            RawSnapshotId = result.SnapshotId,
            ErrorDetails = result.ErrorMessage,
        });
        await db.SaveChangesAsync(cancellationToken);
    }

    private async Task CompleteSyncRunAsync(
        long syncRunId,
        DateTimeOffset completedAt,
        string status,
        IReadOnlyList<SyncEndpointResult> results,
        CancellationToken cancellationToken)
    {
        await using var db = await contextFactory.CreateDbContextAsync(cancellationToken);
        var syncRun = await db.SyncRuns.SingleAsync(x => x.Id == syncRunId, cancellationToken);
        syncRun.CompletedAtUtc = completedAt;
        syncRun.Status = status;
        syncRun.ErrorDetails = results.Count(x => !x.IsSuccess) == 0
            ? null
            : string.Join("; ", results.Where(x => !x.IsSuccess).Select(x => $"{x.Endpoint}: {x.ErrorMessage}"));
        await db.SaveChangesAsync(cancellationToken);
    }

    private static string FormatEndpoint(SyncEndpoint endpoint)
    {
        var query = RawSnapshotStore.NormalizeQuery(endpoint.Query);
        return string.IsNullOrEmpty(query) ? endpoint.Path : $"{endpoint.Path}?{query}";
    }

    private static bool IsTransientStatus(int statusCode) => statusCode is 408 or 429 or 502 or 503 or 504;

    private static bool IsTransientException(Exception exception) => exception switch
    {
        IOException => true,
        HttpRequestException => true,
        TimeoutException => true,
        WebException => true,
        COMException => true,
        _ => false,
    };

    private static async Task DelayBeforeRetryAsync(
        TransportResponse? response,
        int attempt,
        CancellationToken cancellationToken)
    {
        if (response?.Headers.TryGetValue("retry-after", out var retryAfter) == true
            && int.TryParse(retryAfter, out var retryAfterSeconds)
            && retryAfterSeconds >= 0)
        {
            await Task.Delay(TimeSpan.FromSeconds(Math.Min(retryAfterSeconds, 30)), cancellationToken);
            return;
        }

        var backoffMilliseconds = Math.Min(4_000, 500 * Math.Pow(2, attempt - 1));
        var jitterMilliseconds = Random.Shared.Next(0, 250);
        await Task.Delay(TimeSpan.FromMilliseconds(backoffMilliseconds + jitterMilliseconds), cancellationToken);
    }

    private async Task<(int? Season, int? Department)> ReadSeasonAndDepartmentAsync(
        int fancierId, CancellationToken ct)
    {
        await using var db = await contextFactory.CreateDbContextAsync(ct);

        int? season = null;
        var seasonSnapshot = await db.RawApiSnapshots
            .AsNoTracking()
            .Where(x => x.Endpoint == "/api/season"
                && x.StatusCode >= 200 && x.StatusCode < 300)
            .OrderByDescending(x => x.Id)
            .FirstOrDefaultAsync(ct);

        if (seasonSnapshot is not null)
        {
            try
            {
                using var doc = JsonDocument.Parse(seasonSnapshot.ResponseBodyJson);
                if (doc.RootElement.ValueKind == JsonValueKind.Array)
                {
                    foreach (var item in doc.RootElement.EnumerateArray())
                    {
                        if (item.TryGetProperty("active", out var active) && active.GetBoolean()
                            && item.TryGetProperty("id", out var id))
                        {
                            season = id.GetInt32();
                            break;
                        }
                    }
                }
            }
            catch (JsonException) { }
        }

        int? department = null;
        var fancierSnapshot = await db.RawApiSnapshots
            .AsNoTracking()
            .Where(x => x.SelectedFancierId == fancierId
                && x.Endpoint == "/api/fancier/selected"
                && x.StatusCode >= 200 && x.StatusCode < 300)
            .OrderByDescending(x => x.Id)
            .FirstOrDefaultAsync(ct);

        if (fancierSnapshot is not null)
        {
            try
            {
                using var doc = JsonDocument.Parse(fancierSnapshot.ResponseBodyJson);
                if (doc.RootElement.TryGetProperty("department", out var dept)
                    && dept.TryGetInt32(out var deptVal))
                {
                    department = deptVal;
                }
            }
            catch (JsonException) { }
        }

        return (season, department);
    }

    private async Task<IReadOnlyList<int>> ReadActiveFlightIdsAsync(int fancierId, CancellationToken ct)
    {
        await using var db = await contextFactory.CreateDbContextAsync(ct);

        var liveSnapshot = await db.RawApiSnapshots
            .AsNoTracking()
            .Where(x => x.SelectedFancierId == fancierId
                && x.Endpoint == "/api/flight/live"
                && x.StatusCode >= 200 && x.StatusCode < 300)
            .OrderByDescending(x => x.Id)
            .FirstOrDefaultAsync(ct);

        if (liveSnapshot is null)
        {
            var flightSnapshot = await db.RawApiSnapshots
                .AsNoTracking()
                .Where(x => x.SelectedFancierId == fancierId
                    && x.Endpoint == "/api/flight"
                    && x.StatusCode >= 200 && x.StatusCode < 300)
                .OrderByDescending(x => x.Id)
                .FirstOrDefaultAsync(ct);

            liveSnapshot = flightSnapshot;
        }

        if (liveSnapshot is null) return [];

        try
        {
            using var doc = JsonDocument.Parse(liveSnapshot.ResponseBodyJson);
            if (doc.RootElement.ValueKind != JsonValueKind.Array) return [];

            var ids = new List<int>();
            foreach (var item in doc.RootElement.EnumerateArray())
            {
                if (item.TryGetProperty("status", out var status)
                    && status.GetString() is "started"
                    && item.TryGetProperty("id", out var id)
                    && id.TryGetInt32(out var flightId))
                {
                    ids.Add(flightId);
                }
            }

            return ids;
        }
        catch (JsonException)
        {
            return [];
        }
    }

    private async Task FetchNewlyDiscoveredFlightResultsAsync(
        long syncRunId,
        int selectedFancierId,
        IReadOnlyList<int> previousActiveFlightIds,
        ConcurrentBag<SyncEndpointResult> results,
        SemaphoreSlim concurrency,
        CancellationTokenSource runCancellation,
        Action onSessionExpired)
    {
        var currentActiveFlightIds = await ReadActiveFlightIdsAsync(selectedFancierId, runCancellation.Token);
        var newFlightIds = currentActiveFlightIds
            .Where(id => !previousActiveFlightIds.Contains(id))
            .ToList();

        if (newFlightIds.Count == 0) return;

        var extraEndpoints = newFlightIds
            .Select(id => new SyncEndpoint($"/api/flight/{id}/results", Optional: true))
            .ToList();

        foreach (var endpoint in extraEndpoints)
        {
            await concurrency.WaitAsync(runCancellation.Token);
            try
            {
                var result = await ExecuteEndpointAsync(
                    syncRunId, endpoint, selectedFancierId,
                    runCancellation, onSessionExpired);
                results.Add(result);
            }
            finally
            {
                concurrency.Release();
            }
        }
    }

    private void RaiseProgress(SyncProgress progress)
    {
        ProgressChanged?.Invoke(this, progress);
    }
}