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
        // Flight results can carry fractional numeric fields (e.g. remainingDistance)
        // for in-progress flights; round them into the int DTO fields rather than
        // failing the whole payload. See LenientIntConverter.
        Converters = { new LenientIntConverter() },
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

        // Step 5: Backfill weather for any flight that doesn't have it yet, resolved
        // from the /api/weather snapshots captured over time.
        await BackfillFlightWeatherAsync(db, selectedFancierId, cancellationToken);

        // Step 5b: Repair flights whose DistanceKm was previously overwritten with an
        // average of per-pigeon home distances (a historical bug) by restoring it from
        // the flight's originally-cached /api/flight/{id} snapshot.
        await RepairFlightDistancesAsync(db, selectedFancierId, cancellationToken);

        // Step 6: Capture per-age-category participant counts for national flights on
        // their own flight day. These drive the per-category prize breakdown and are
        // only obtainable from the ageType-filtered results endpoint.
        await CaptureNationalAgeCategoryCountsAsync(db, selectedFancierId, cancellationToken);
    }

    /// <summary>
    /// For each national flight whose date is today and whose age-category counts
    /// haven't been captured yet, fetches the Elder/Yearling/Youth participant
    /// counts from the ageType-filtered results endpoint and stores them.
    ///
    /// Gated three ways to keep API load minimal: national flights only, on the
    /// flight's own day only (<c>Start.Date == today</c>), and once per flight
    /// (<see cref="FlightEntity.AgeCategoryCountsCapturedAtUtc"/> null). Worst case
    /// is three extra GETs per national flight, on its flight day.
    /// </summary>
    private async Task CaptureNationalAgeCategoryCountsAsync(
        AppDbContext db,
        int selectedFancierId,
        CancellationToken cancellationToken)
    {
        var today = DateTime.Now.Date;

        var candidates = await db.Flights
            .Where(f => f.Type == "national" && f.AgeCategoryCountsCapturedAtUtc == null)
            .ToListAsync(cancellationToken);

        foreach (var flight in candidates.Where(f => f.Start.Date == today))
        {
            try
            {
                var elder = await FetchAgeCategoryCountAsync(flight.Id, AgeCategory.Elder, selectedFancierId, cancellationToken);
                var yearling = await FetchAgeCategoryCountAsync(flight.Id, AgeCategory.Yearling, selectedFancierId, cancellationToken);
                var youth = await FetchAgeCategoryCountAsync(flight.Id, AgeCategory.Youth, selectedFancierId, cancellationToken);

                // Only lock the counts in once all three fetches succeed; a null means
                // a failed/blank response, so leave the flight uncaptured and retry on
                // a later sync the same day rather than persisting a partial reading.
                if (elder is null || yearling is null || youth is null)
                    continue;

                flight.AgeCategoryElderCount = elder;
                flight.AgeCategoryYearlingCount = yearling;
                flight.AgeCategoryYouthCount = youth;
                flight.AgeCategoryCountsCapturedAtUtc = DateTimeOffset.UtcNow;
                await db.SaveChangesAsync(cancellationToken);
            }
            catch
            {
                // Best-effort per flight; will retry on the next sync while still today.
            }
        }
    }

    /// <summary>
    /// Reads the total participant count for one age category of a flight. The
    /// results endpoint returns the category total in <c>count</c> regardless of
    /// paging, so we request a single-row page to minimise payload. Returns null
    /// when the response is unavailable or unparseable.
    /// </summary>
    private async Task<int?> FetchAgeCategoryCountAsync(
        int flightId,
        AgeCategory category,
        int selectedFancierId,
        CancellationToken cancellationToken)
    {
        var query = new Dictionary<string, string?>
        {
            ["page"] = "1",
            ["pageSize"] = "1",
            ["ageType"] = category.ToString(),
            ["activeSort"] = "position",
            ["sortDirection"] = "asc",
        };

        var response = await apiClient.GetJsonAsync($"/api/flight/{flightId}/results", query, cancellationToken);
        await snapshotStore.SaveAsync($"/api/flight/{flightId}/results", query, response, selectedFancierId, cancellationToken: cancellationToken);

        if (response.StatusCode is < 200 or >= 300 || string.IsNullOrEmpty(response.Body))
            return null;

        try
        {
            var page = JsonSerializer.Deserialize<FlightResultsResponse>(response.Body, JsonOptions);
            return page?.Count;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    /// <summary>
    /// Stamps flights that have no captured weather with the forecast for their date.
    /// Flights are only discovered after they end, so the live forecast no longer
    /// covers the flight date; instead we reconstruct it from the /api/weather
    /// snapshots that the sync stored over time. For each date we keep the entry from
    /// the latest snapshot that still forecast it — a forecast entry for date D only
    /// appears in snapshots captured on or before D, so the latest capture is the
    /// reading closest to the flight and thus the most accurate available.
    /// </summary>
    private async Task BackfillFlightWeatherAsync(
        AppDbContext db,
        int selectedFancierId,
        CancellationToken cancellationToken)
    {
        var flightsNeedingWeather = await db.Flights
            .Where(f => f.WeatherCapturedAtUtc == null)
            .ToListAsync(cancellationToken);

        if (flightsNeedingWeather.Count == 0)
            return;

        var weatherSnapshots = await db.RawApiSnapshots
            .AsNoTracking()
            .Where(x => x.SelectedFancierId == selectedFancierId
                && x.Endpoint == "/api/weather"
                && x.StatusCode >= 200 && x.StatusCode < 300)
            // Order by Id (== capture order), not CapturedAtUtc: SQLite can't ORDER BY
            // a DateTimeOffset. The "latest capture wins" choice is made in memory
            // below via the CapturedAtUtc comparison, so iteration order is immaterial.
            .OrderBy(x => x.Id)
            .Select(x => new { x.ResponseBodyJson, x.CapturedAtUtc })
            .ToListAsync(cancellationToken);

        if (weatherSnapshots.Count == 0)
            return;

        var bestByDate = new Dictionary<DateTime, (WeatherForecastDto Forecast, DateTimeOffset CapturedAtUtc)>();
        foreach (var snapshot in weatherSnapshots)
        {
            List<WeatherForecastDto>? forecasts;
            try
            {
                forecasts = JsonSerializer.Deserialize<List<WeatherForecastDto>>(snapshot.ResponseBodyJson, JsonOptions);
            }
            catch (JsonException)
            {
                continue;
            }

            if (forecasts is null)
                continue;

            foreach (var forecast in forecasts)
            {
                if (forecast.Date is not { } date)
                    continue;

                var day = date.Date;
                if (!bestByDate.TryGetValue(day, out var existing)
                    || snapshot.CapturedAtUtc > existing.CapturedAtUtc)
                {
                    bestByDate[day] = (forecast, snapshot.CapturedAtUtc);
                }
            }
        }

        var changed = false;
        foreach (var flight in flightsNeedingWeather)
        {
            if (!bestByDate.TryGetValue(flight.Start.Date, out var match))
                continue;

            var forecast = match.Forecast;
            flight.WeatherTemperature = forecast.Temperature;
            flight.WeatherHumidity = forecast.Humidity;
            flight.WeatherWind = forecast.Wind;
            flight.WeatherBeaufort = forecast.Beaufort;
            flight.WeatherDay = forecast.Day;
            flight.WeatherCondition = forecast.Condition;
            flight.WeatherCapturedAtUtc = match.CapturedAtUtc;
            changed = true;
        }

        if (changed)
            await db.SaveChangesAsync(cancellationToken);
    }

    /// <summary>
    /// Restores <see cref="FlightEntity.DistanceKm"/> to the flight's true nominal
    /// distance for any flight whose value was corrupted by a historical bug that
    /// overwrote it with the average of the user's own pigeons' individual
    /// home-to-release distances. The nominal distance is recovered from the raw
    /// <c>/api/flight/{id}</c> response cached at discovery time, so no live API
    /// calls are needed. Idempotent: once repaired, a flight's stored value matches
    /// the cached snapshot and is skipped on subsequent runs.
    /// </summary>
    private async Task RepairFlightDistancesAsync(
        AppDbContext db,
        int selectedFancierId,
        CancellationToken cancellationToken)
    {
        var flights = await db.Flights.ToListAsync(cancellationToken);
        if (flights.Count == 0)
            return;

        var flightSnapshots = await db.RawApiSnapshots
            .AsNoTracking()
            .Where(x => x.SelectedFancierId == selectedFancierId
                && x.Endpoint.StartsWith("/api/flight/")
                && !x.Endpoint.Contains("/results")
                && x.StatusCode >= 200 && x.StatusCode < 300)
            .OrderBy(x => x.Id)
            .Select(x => new { x.Endpoint, x.ResponseBodyJson })
            .ToListAsync(cancellationToken);

        if (flightSnapshots.Count == 0)
            return;

        // Keep the earliest snapshot per flight: it was captured right after the
        // flight was discovered, before any distance corruption could occur.
        var nominalDistanceByFlightId = new Dictionary<int, int>();
        foreach (var snapshot in flightSnapshots)
        {
            var idPart = snapshot.Endpoint["/api/flight/".Length..];
            if (!int.TryParse(idPart, out var flightId) || nominalDistanceByFlightId.ContainsKey(flightId))
                continue;

            try
            {
                using var doc = JsonDocument.Parse(snapshot.ResponseBodyJson);
                if (doc.RootElement.TryGetProperty("distance", out var distanceProp)
                    && distanceProp.TryGetInt32(out var distance))
                {
                    nominalDistanceByFlightId[flightId] = distance;
                }
            }
            catch (JsonException)
            {
                // Skip unparsable snapshots; the flight simply won't be repaired this run.
            }
        }

        var changed = false;
        foreach (var flight in flights)
        {
            if (!nominalDistanceByFlightId.TryGetValue(flight.Id, out var nominalDistance))
                continue;
            if (flight.DistanceKm == nominalDistance)
                continue;

            flight.DistanceKm = nominalDistance;
            flight.DistanceCategory = DistanceProfileCalculator.Classify(nominalDistance).ToString();
            changed = true;
        }

        if (changed)
            await db.SaveChangesAsync(cancellationToken);
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
