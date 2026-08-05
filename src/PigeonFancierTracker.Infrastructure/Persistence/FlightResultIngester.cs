using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.EntityFrameworkCore;
using PigeonFancierTracker.Core.Analytics;
using PigeonFancierTracker.Core.Contracts;
using PigeonFancierTracker.Infrastructure.PigeonFancierApi;

namespace PigeonFancierTracker.Infrastructure.Persistence;

public sealed class FlightResultIngester(
    IDbContextFactory<AppDbContext> contextFactory,
    PigeonFancierApiClient apiClient,
    RawSnapshotStore snapshotStore)
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        NumberHandling = JsonNumberHandling.AllowReadingFromString,
    };

    public async Task IngestAsync(
        int selectedFancierId,
        CancellationToken cancellationToken = default)
    {
        // Step 1: Fetch pigeon results to discover flight IDs
        var flightIds = await DiscoverFlightIdsAsync(selectedFancierId, cancellationToken);
        if (flightIds.Count == 0)
            return;

        await using var db = await contextFactory.CreateDbContextAsync(cancellationToken);

        // Step 2: Determine which flights are new
        var existingFlightIds = await db.Flights
            .Where(f => flightIds.Contains(f.Id))
            .Select(f => f.Id)
            .ToListAsync(cancellationToken);
        var newFlightIds = flightIds.Except(existingFlightIds).ToList();

        // Step 3: Fetch and persist new flight metadata
        foreach (var flightId in newFlightIds)
        {
            await FetchAndPersistFlightAsync(db, flightId, selectedFancierId, cancellationToken);
        }

        // Step 4: Fetch results for ended flights that haven't been processed yet
        var endedFlights = await db.Flights
            .Where(f => f.Status == "ended" && f.ResultsFetchedAtUtc == null)
            .ToListAsync(cancellationToken);

        foreach (var flight in endedFlights)
        {
            await FetchAndPersistResultsAsync(db, flight, selectedFancierId, cancellationToken);
        }
    }

    private async Task<IReadOnlySet<int>> DiscoverFlightIdsAsync(
        int selectedFancierId,
        CancellationToken cancellationToken)
    {
        await using var db = await contextFactory.CreateDbContextAsync(cancellationToken);

        // Get current pigeon roster from latest snapshot
        var pigeonSnapshot = await db.RawApiSnapshots
            .AsNoTracking()
            .Where(x => x.SelectedFancierId == selectedFancierId
                && x.Endpoint == "/api/pigeon"
                && x.StatusCode >= 200 && x.StatusCode < 300)
            .OrderByDescending(x => x.Id)
            .FirstOrDefaultAsync(cancellationToken);

        if (pigeonSnapshot is null)
            return new HashSet<int>();

        var pigeons = JsonSerializer.Deserialize<PigeonDto[]>(pigeonSnapshot.ResponseBodyJson, JsonOptions) ?? [];

        var flightIds = new HashSet<int>();
        foreach (var pigeon in pigeons)
        {
            if (pigeon.Id is not { } pigeonId)
                continue;

            try
            {
                var query = new Dictionary<string, string?> { ["page"] = "1", ["pageSize"] = "1000" };
                var response = await apiClient.GetJsonAsync($"/api/pigeon/{pigeonId}/results", query, cancellationToken);
                await snapshotStore.SaveAsync($"/api/pigeon/{pigeonId}/results", query, response, selectedFancierId, cancellationToken: cancellationToken);

                if (response.StatusCode is < 200 or >= 300 || string.IsNullOrEmpty(response.Body))
                    continue;

                var results = JsonSerializer.Deserialize<PigeonResultDto[]>(response.Body, JsonOptions) ?? [];
                foreach (var result in results)
                {
                    flightIds.Add(result.Flight.Id);
                }
            }
            catch
            {
                // Best-effort per pigeon; continue with others.
            }
        }

        return flightIds;
    }

    private async Task FetchAndPersistFlightAsync(
        AppDbContext db,
        int flightId,
        int selectedFancierId,
        CancellationToken cancellationToken)
    {
        try
        {
            var response = await apiClient.GetJsonAsync($"/api/flight/{flightId}", cancellationToken: cancellationToken);
            await snapshotStore.SaveAsync($"/api/flight/{flightId}", null, response, selectedFancierId, cancellationToken: cancellationToken);

            if (response.StatusCode is < 200 or >= 300 || string.IsNullOrEmpty(response.Body))
                return;

            var flight = JsonSerializer.Deserialize<FlightDto>(response.Body, JsonOptions);
            if (flight is null)
                return;

            // Only track regional and national flights
            if (!string.Equals(flight.Type, "regional", StringComparison.OrdinalIgnoreCase)
                && !string.Equals(flight.Type, "national", StringComparison.OrdinalIgnoreCase))
                return;

            var entity = new FlightEntity
            {
                Id = flight.Id,
                Season = flight.Season,
                Department = flight.Department,
                Type = flight.Type,
                PayoutType = flight.PayoutType,
                Status = flight.Status,
                Start = flight.Start,
                LocationName = flight.Location?.Name,
                LocationLat = flight.Location?.Lat,
                LocationLng = flight.Location?.Lng,
                DistanceKm = flight.Distance,
                DistanceCategory = DistanceProfileCalculator.Classify(flight.Distance).ToString(),
                AgeType = flight.AgeType,
                EntryPrice = flight.EntryPrice,
                Subscribers = flight.Subscribers,
                DetectedAtUtc = DateTimeOffset.UtcNow,
            };

            db.Flights.Add(entity);
            await db.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException)
        {
            // Flight may have been inserted concurrently; ignore duplicate.
        }
        catch
        {
            // Best-effort; will retry on next ingestion.
        }
    }

    private async Task FetchAndPersistResultsAsync(
        AppDbContext db,
        FlightEntity flight,
        int selectedFancierId,
        CancellationToken cancellationToken)
    {
        try
        {
            var response = await apiClient.GetJsonAsync($"/api/flight/{flight.Id}/results", cancellationToken: cancellationToken);
            await snapshotStore.SaveAsync($"/api/flight/{flight.Id}/results", null, response, selectedFancierId, cancellationToken: cancellationToken);

            if (response.StatusCode is < 200 or >= 300 || string.IsNullOrEmpty(response.Body))
                return;

            var resultsPage = JsonSerializer.Deserialize<FlightResultsResponse>(response.Body, JsonOptions);
            if (resultsPage?.Items is null)
                return;

            // Get fancier IDs associated with this user
            var fancierIds = await GetUserFancierIdsAsync(db, selectedFancierId, cancellationToken);

            foreach (var result in resultsPage.Items)
            {
                if (!fancierIds.Contains(result.FancierId))
                    continue;

                var existing = await db.FlightResults
                    .FirstOrDefaultAsync(r => r.FlightId == flight.Id && r.PigeonId == result.PigeonId, cancellationToken);

                if (existing is not null)
                    continue;

                db.FlightResults.Add(new FlightResultEntity
                {
                    FlightId = flight.Id,
                    PigeonId = result.PigeonId,
                    FancierId = result.FancierId,
                    Position = result.Position,
                    TotalParticipants = flight.Subscribers,
                    Points = result.Points,
                    AverageSpeed = result.AverageSpeed,
                    PigeonDistance = result.Distance,
                    PigeonName = result.Fancier,
                    DetectedAtUtc = DateTimeOffset.UtcNow,
                });
            }

            flight.ResultsFetchedAtUtc = DateTimeOffset.UtcNow;
            await db.SaveChangesAsync(cancellationToken);
        }
        catch
        {
            // Best-effort; will retry on next ingestion.
        }
    }

    private static async Task<HashSet<int>> GetUserFancierIdsAsync(
        AppDbContext db,
        int selectedFancierId,
        CancellationToken cancellationToken)
    {
        // The user may have multiple fanciers; check snapshots for fancier list
        var fancierSnapshot = await db.RawApiSnapshots
            .AsNoTracking()
            .Where(x => x.Endpoint == "/api/fancier/selected"
                && x.StatusCode >= 200 && x.StatusCode < 300)
            .OrderByDescending(x => x.Id)
            .FirstOrDefaultAsync(cancellationToken);

        var ids = new HashSet<int> { selectedFancierId };

        if (fancierSnapshot is not null)
        {
            try
            {
                var fancier = JsonSerializer.Deserialize<SelectedFancierDto>(fancierSnapshot.ResponseBodyJson, JsonOptions);
                if (fancier?.Id is { } fId && fId != selectedFancierId)
                    ids.Add(fId);
            }
            catch
            {
                // Best-effort.
            }
        }

        return ids;
    }
}
