using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.EntityFrameworkCore;
using PigeonFancierTracker.Core.Analytics;
using PigeonFancierTracker.Core.Contracts;
using PigeonFancierTracker.Core.Domain;
using PigeonFancierTracker.Infrastructure.PigeonFancierApi;

namespace PigeonFancierTracker.Infrastructure.Persistence;

public sealed class FlightResultsReader(
    IDbContextFactory<AppDbContext> contextFactory,
    PigeonFancierApiClient apiClient) : IFlightResultsReader
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        NumberHandling = JsonNumberHandling.AllowReadingFromString,
    };
    public async Task<FlightResultsPageData> GetFlightResultsAsync(int fancierId)
    {
        await using var db = await contextFactory.CreateDbContextAsync();

        var results = await (
            from r in db.FlightResults.AsNoTracking()
            join f in db.Flights.AsNoTracking() on r.FlightId equals f.Id
            where r.FancierId == fancierId
            orderby f.Start descending
            select new { Result = r, Flight = f }
        ).ToListAsync();

        var (nameMap, breedMap) = await BuildPigeonMapsAsync(db, fancierId);

        var foodSnapshots = await db.FoodDistributionSnapshots
            .AsNoTracking()
            .Where(x => x.SelectedFancierId == fancierId)
            .OrderBy(x => x.Id)
            .ToListAsync();

        var recentResults = results
            .Select(x =>
            {
                var pigeonDistance = x.Result.PigeonDistance > 0
                    ? x.Result.PigeonDistance
                    : x.Flight.DistanceKm;
                var category = DistanceProfileCalculator.Classify(pigeonDistance);

                var percentile = x.Result.TotalParticipants > 0
                    ? Math.Round((double)x.Result.Position / x.Result.TotalParticipants * 100, 1)
                    : 0;

                var pigeonName = nameMap.GetValueOrDefault(x.Result.PigeonId) ?? $"Duif #{x.Result.PigeonId}";

                var foodMix = FindFoodMixAtTime(foodSnapshots, x.Flight.Start);

                return new FlightResultListItem(
                    x.Flight.Id,
                    x.Flight.Start,
                    x.Flight.Type,
                    x.Flight.LocationName,
                    pigeonDistance,
                    category,
                    x.Result.Position,
                    x.Result.TotalParticipants,
                    percentile,
                    x.Result.Points,
                    x.Result.AverageSpeed,
                    pigeonName,
                    x.Result.PigeonId,
                    foodMix);
            })
            .ToList();

        var profiles = recentResults
            .GroupBy(r => r.PigeonId)
            .Select(g =>
            {
                var pigeonName = g.First().PigeonName;
                var breed = breedMap.GetValueOrDefault(g.Key);
                return DistanceProfileCalculator.Calculate(g.Key, pigeonName, breed, g.ToList());
            })
            .OrderBy(p => p.PigeonName, StringComparer.OrdinalIgnoreCase)
            .ToList();

        var foodComments = BuildFoodComments(foodSnapshots);
        var foodAnalysis = FoodImpactCalculator.Calculate(recentResults, foodComments);

        return new FlightResultsPageData(recentResults, profiles, foodAnalysis);
    }

    public async Task<IReadOnlyList<UpcomingFlightInfo>> GetUpcomingFlightsAsync(int fancierId)
    {
        await using var db = await contextFactory.CreateDbContextAsync();
        var (season, department) = await ReadSeasonAndDepartmentAsync(db, fancierId);

        var flights = await FetchFlightsFromApiAsync(season, department, "notStarted");

        if (flights.Count == 0) return [];

        var fancierLocation = await ReadFancierLocationAsync(db, fancierId);

        return flights
            .Where(f => string.Equals(f.Type, "regional", StringComparison.OrdinalIgnoreCase)
                || string.Equals(f.Type, "national", StringComparison.OrdinalIgnoreCase))
            .OrderBy(f => f.Start)
            .Select(f =>
            {
                var realDistance = ComputeFlightDistance(f, fancierLocation);
                var category = DistanceProfileCalculator.Classify(realDistance);
                var flightType = ParseFlightType(f.Type);
                var prizeTable = PrizeCalculator.CalculatePrizeTableWithMoney(f.Subscribers, flightType, f.EntryPrice);
                var totalPrizes = PrizeCalculator.GetTotalPrizePositions(f.Subscribers);

                return new UpcomingFlightInfo(
                    f.Id, f.Start, f.Location?.Name, f.Type, realDistance,
                    category, f.Subscribers, f.EntryPrice, prizeTable, totalPrizes);
            })
            .ToList();
    }

    public async Task<IReadOnlyList<ActiveFlightInfo>> GetActiveFlightsAsync(int fancierId)
    {
        await using var db = await contextFactory.CreateDbContextAsync();
        var (season, department) = await ReadSeasonAndDepartmentAsync(db, fancierId);

        var activeFlights = await FetchLiveFlightsFromApiAsync(season, department);

        if (activeFlights.Count == 0) return [];

        var fancierLocation = await ReadFancierLocationAsync(db, fancierId);
        var translations = await ReadTranslationsAsync(db);
        var result = new List<ActiveFlightInfo>();

        foreach (var flight in activeFlights)
        {
            FlightResultsResponse? resultsResponse = null;
            try
            {
                var resultsResp = await apiClient.GetJsonAsync($"/api/flight/{flight.Id}/results");
                if (resultsResp.IsSuccessStatusCode && !string.IsNullOrEmpty(resultsResp.Body))
                {
                    resultsResponse = JsonSerializer.Deserialize<FlightResultsResponse>(resultsResp.Body, JsonOptions);
                }
            }
            catch { }

            var realDistance = ComputeFlightDistance(flight, fancierLocation);
            var category = DistanceProfileCalculator.Classify(realDistance);
            var flightType = ParseFlightType(flight.Type);
            var prizeTable = PrizeCalculator.CalculatePrizeTableWithMoney(flight.Subscribers, flightType, flight.EntryPrice);

            var pigeonStandings = new List<ActivePigeonStanding>();
            var fancierPoints = new Dictionary<int, (string Name, int TotalPoints, int PigeonCount, int BestPosition, decimal TotalPrizeMoney, int PrizePigeonCount)>();
            var totalPrizePool = flight.Subscribers * flight.EntryPrice;

            if (resultsResponse?.Items is { } items)
            {
                foreach (var item in items)
                {
                    var points = PrizeCalculator.GetPointsForPosition(
                        item.Position, flight.Subscribers, flightType);
                    var prizeMoney = PrizeCalculator.GetPrizeMoneyForPosition(
                        item.Position, flight.Subscribers, flightType, flight.EntryPrice);

                    var pigeonName = ResolvePigeonName(item.FirstNameId, item.LastNameId, item.PigeonId, translations);

                    pigeonStandings.Add(new ActivePigeonStanding(
                        item.PigeonId,
                        pigeonName,
                        item.Position,
                        points,
                        item.CurrentSpeed,
                        item.RemainingDistance,
                        item.Progress,
                        item.FancierId,
                        item.Fancier,
                        prizeMoney));

                    if (!fancierPoints.TryGetValue(item.FancierId, out var current))
                    {
                        current = (item.Fancier ?? $"#{item.FancierId}", 0, 0, item.Position, 0m, 0);
                    }

                    fancierPoints[item.FancierId] = (
                        current.Name,
                        current.TotalPoints + points,
                        current.PigeonCount + 1,
                        Math.Min(current.BestPosition, item.Position),
                        current.TotalPrizeMoney + prizeMoney,
                        current.PrizePigeonCount + (points > 0 ? 1 : 0));
                }
            }

            var fancierStandings = fancierPoints
                .Select(kvp => new ActiveFancierStanding(
                    kvp.Value.Name,
                    kvp.Key,
                    kvp.Value.TotalPoints,
                    kvp.Value.PigeonCount,
                    kvp.Value.BestPosition,
                    kvp.Key == fancierId,
                    kvp.Value.TotalPrizeMoney,
                    kvp.Value.PrizePigeonCount))
                .OrderByDescending(f => f.TotalPoints)
                .ThenBy(f => f.BestPosition)
                .ToList();

            result.Add(new ActiveFlightInfo(
                flight.Id, flight.Start, flight.Location?.Name, flight.Type,
                realDistance, category, flight.Subscribers, flight.Progress,
                pigeonStandings, fancierStandings, prizeTable,
                flight.EntryPrice, totalPrizePool));
        }

        return result;
    }

    private static async Task<PigeonNameTranslations> ReadTranslationsAsync(AppDbContext db)
    {
        var translationSnapshots = await db.RawApiSnapshots
            .AsNoTracking()
            .Where(x => x.Endpoint.StartsWith("/api/translation/")
                && x.StatusCode >= 200 && x.StatusCode < 300)
            .ToListAsync();

        return PigeonNameResolver.ReadTranslations(translationSnapshots);
    }

    private static string ResolvePigeonName(
        int? firstNameId, int? lastNameId, int pigeonId,
        PigeonNameTranslations translations)
    {
        var parts = new[]
        {
            firstNameId is int fId && translations.FirstNames.TryGetValue(fId, out var firstName) ? firstName : null,
            lastNameId is int lId && translations.LastNames.TryGetValue(lId, out var lastName) ? lastName : null,
        };
        var name = string.Join(' ', parts.Where(x => !string.IsNullOrWhiteSpace(x)));
        return string.IsNullOrWhiteSpace(name) ? $"Duif #{pigeonId}" : name;
    }

    private static FlightType ParseFlightType(string? type) =>
        string.Equals(type, "national", StringComparison.OrdinalIgnoreCase)
            ? FlightType.National
            : FlightType.Regional;

    private static int ComputeFlightDistance(FlightDto flight, FancierLocationDto? fancierLocation)
    {
        if (fancierLocation?.Latitude is { } fLat
            && fancierLocation?.Longitude is { } fLng
            && flight.Location is { } releaseLoc)
        {
            var computed = DistanceCalculator.HaversineKm(
                (double)fLat, (double)fLng,
                releaseLoc.Lat, releaseLoc.Lng);
            if (computed > 0)
                return computed;
        }

        return flight.Distance;
    }

    private static async Task<FancierLocationDto?> ReadFancierLocationAsync(AppDbContext db, int fancierId)
    {
        var snapshot = await db.RawApiSnapshots
            .AsNoTracking()
            .Where(x => x.SelectedFancierId == fancierId
                && x.Endpoint == "/api/fancier/selected"
                && x.StatusCode >= 200 && x.StatusCode < 300)
            .OrderByDescending(x => x.Id)
            .FirstOrDefaultAsync();

        if (snapshot is null) return null;

        try
        {
            var fancier = JsonSerializer.Deserialize<SelectedFancierDto>(snapshot.ResponseBodyJson,
                new JsonSerializerOptions
                {
                    PropertyNameCaseInsensitive = true,
                    NumberHandling = JsonNumberHandling.AllowReadingFromString,
                });
            return fancier?.Location;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    public async Task<FlightDiagnosticReport> GetFlightDiagnosticsAsync(int fancierId)
    {
        await using var db = await contextFactory.CreateDbContextAsync();

        var liveSnapshots = await db.RawApiSnapshots
            .AsNoTracking()
            .Where(x => x.Endpoint == "/api/flight/live")
            .OrderByDescending(x => x.Id)
            .Take(10)
            .Select(x => new FlightSnapshotDiagnostic(
                x.Endpoint, x.NormalizedQuery, x.StatusCode, x.CapturedAtUtc,
                x.ResponseBodyJson.Length))
            .ToListAsync();

        var flightSnapshots = await db.RawApiSnapshots
            .AsNoTracking()
            .Where(x => x.Endpoint == "/api/flight")
            .OrderByDescending(x => x.Id)
            .Take(10)
            .Select(x => new FlightSnapshotDiagnostic(
                x.Endpoint, x.NormalizedQuery, x.StatusCode, x.CapturedAtUtc,
                x.ResponseBodyJson.Length))
            .ToListAsync();

        string? livePreview = null;
        string? flightPreview = null;
        int parsedCount = 0;
        int filteredCount = 0;
        string? parseError = null;

        var bestLive = await db.RawApiSnapshots
            .AsNoTracking()
            .Where(x => x.SelectedFancierId == fancierId
                && x.Endpoint == "/api/flight/live"
                && x.StatusCode >= 200 && x.StatusCode < 300)
            .OrderByDescending(x => x.Id)
            .FirstOrDefaultAsync();

        if (bestLive is null)
        {
            bestLive = await db.RawApiSnapshots
                .AsNoTracking()
                .Where(x => x.Endpoint == "/api/flight/live"
                    && x.StatusCode >= 200 && x.StatusCode < 300)
                .OrderByDescending(x => x.Id)
                .FirstOrDefaultAsync();
        }

        if (bestLive is not null)
        {
            livePreview = bestLive.ResponseBodyJson.Length > 500
                ? bestLive.ResponseBodyJson[..500] + "..."
                : bestLive.ResponseBodyJson;

            try
            {
                var flights = JsonSerializer.Deserialize<List<FlightDto>>(bestLive.ResponseBodyJson, JsonOptions);
                parsedCount = flights?.Count ?? 0;
                filteredCount = flights?
                    .Where(f => string.Equals(f.Status, "started", StringComparison.OrdinalIgnoreCase))
                    .Where(f => string.Equals(f.Type, "regional", StringComparison.OrdinalIgnoreCase)
                        || string.Equals(f.Type, "national", StringComparison.OrdinalIgnoreCase))
                    .Count() ?? 0;
            }
            catch (JsonException ex)
            {
                parseError = ex.Message;
            }
        }

        var bestFlight = await db.RawApiSnapshots
            .AsNoTracking()
            .Where(x => x.Endpoint == "/api/flight"
                && x.StatusCode >= 200 && x.StatusCode < 300)
            .OrderByDescending(x => x.Id)
            .FirstOrDefaultAsync();

        if (bestFlight is not null)
        {
            flightPreview = bestFlight.ResponseBodyJson.Length > 500
                ? bestFlight.ResponseBodyJson[..500] + "..."
                : bestFlight.ResponseBodyJson;
        }

        return new FlightDiagnosticReport(
            fancierId, liveSnapshots, flightSnapshots,
            livePreview, flightPreview,
            parsedCount, filteredCount, parseError);
    }

    public async Task<IReadOnlyList<CompletedFlightSummary>> GetCompletedFlightSummariesAsync(int fancierId)
    {
        await using var db = await contextFactory.CreateDbContextAsync();

        var flights = await db.Flights
            .AsNoTracking()
            .Where(f => f.Status == "ended")
            .OrderByDescending(f => f.Start)
            .ToListAsync();

        if (flights.Count == 0) return [];

        var ownResults = await db.FlightResults
            .AsNoTracking()
            .Where(r => r.FancierId == fancierId)
            .ToListAsync();

        var resultsByFlight = ownResults
            .GroupBy(r => r.FlightId)
            .ToDictionary(g => g.Key, g => g.ToList());

        return flights
            .Select(f =>
            {
                var flightType = ParseFlightType(f.Type);
                var category = DistanceProfileCalculator.Classify(f.DistanceKm);
                var prizeTable = PrizeCalculator.CalculatePrizeTable(f.Subscribers, flightType);
                var totalPrizes = PrizeCalculator.GetTotalPrizePositions(f.Subscribers);

                var ownPigeonCount = 0;
                var bestPosition = 0;
                var totalPoints = 0;
                var totalParticipants = f.Subscribers;

                if (resultsByFlight.TryGetValue(f.Id, out var results))
                {
                    ownPigeonCount = results.Count;
                    bestPosition = results.Min(r => r.Position);
                    totalPoints = results.Sum(r => r.Points);
                    if (results.Count > 0 && results[0].TotalParticipants > 0)
                        totalParticipants = results[0].TotalParticipants;
                }

                return new CompletedFlightSummary(
                    f.Id, f.Start, f.LocationName, f.Type, f.DistanceKm,
                    category, ownPigeonCount, bestPosition, totalParticipants,
                    totalPoints, prizeTable, totalPrizes);
            })
            .ToList();
    }

    public async Task SaveFoodCommentAsync(int fancierId, FoodMix mix, string? comment)
    {
        await using var db = await contextFactory.CreateDbContextAsync();

        var snapshots = await db.FoodDistributionSnapshots
            .Where(x => x.SelectedFancierId == fancierId
                && x.Barley == mix.Barley && x.Grain == mix.Grain
                && x.Corn == mix.Corn && x.Peanut == mix.Peanut)
            .ToListAsync();

        foreach (var s in snapshots)
            s.Comment = comment;

        await db.SaveChangesAsync();
    }

    private static FoodMix? FindFoodMixAtTime(
        List<FoodDistributionSnapshotEntity> snapshots,
        DateTime flightStart)
    {
        var flightOffset = new DateTimeOffset(flightStart, TimeSpan.Zero);
        FoodDistributionSnapshotEntity? best = null;
        foreach (var s in snapshots)
        {
            if (s.CapturedAtUtc <= flightOffset)
                best = s;
            else
                break;
        }

        return best is null ? null : new FoodMix(best.Barley, best.Grain, best.Corn, best.Peanut);
    }

    private static Dictionary<FoodMix, string?> BuildFoodComments(
        List<FoodDistributionSnapshotEntity> snapshots)
    {
        var dict = new Dictionary<FoodMix, string?>(FoodImpactCalculator.FoodMixComparer.Instance);
        foreach (var s in snapshots)
        {
            var mix = new FoodMix(s.Barley, s.Grain, s.Corn, s.Peanut);
            if (!string.IsNullOrWhiteSpace(s.Comment))
                dict[mix] = s.Comment;
            else if (!dict.ContainsKey(mix))
                dict[mix] = null;
        }

        return dict;
    }

    private async Task<(int Season, int Department)> ReadSeasonAndDepartmentAsync(AppDbContext db, int fancierId)
    {
        var snapshot = await db.RawApiSnapshots
            .AsNoTracking()
            .Where(x => x.SelectedFancierId == fancierId
                && x.Endpoint == "/api/season"
                && x.StatusCode >= 200 && x.StatusCode < 300)
            .OrderByDescending(x => x.Id)
            .FirstOrDefaultAsync();

        if (snapshot is not null)
        {
            try
            {
                using var doc = JsonDocument.Parse(snapshot.ResponseBodyJson);
                var root = doc.RootElement;
                var season = root.TryGetProperty("id", out var sid) ? sid.GetInt32() : 1;
                var dept = root.TryGetProperty("department", out var did) ? did.GetInt32() : 2;
                return (season, dept);
            }
            catch { }
        }

        return (1, 2);
    }

    private async Task<List<FlightDto>> FetchFlightsFromApiAsync(int season, int department, string status)
    {
        try
        {
            var path = $"/api/flight?flightType=national&flightType=regional&status={Uri.EscapeDataString(status)}";
            var query = new Dictionary<string, string?>
            {
                ["season"] = season.ToString(),
                ["department"] = department.ToString(),
                ["public"] = "false",
            };
            var response = await apiClient.GetJsonAsync(path, query);
            if (response.IsSuccessStatusCode && !string.IsNullOrEmpty(response.Body))
            {
                return JsonSerializer.Deserialize<List<FlightDto>>(response.Body, JsonOptions) ?? [];
            }
        }
        catch { }

        return [];
    }

    private async Task<List<FlightDto>> FetchLiveFlightsFromApiAsync(int season, int department)
    {
        try
        {
            var query = new Dictionary<string, string?>
            {
                ["season"] = season.ToString(),
                ["department"] = department.ToString(),
                ["public"] = "false",
            };
            var response = await apiClient.GetJsonAsync("/api/flight/live", query);
            if (response.IsSuccessStatusCode && !string.IsNullOrEmpty(response.Body))
            {
                var flights = JsonSerializer.Deserialize<List<FlightDto>>(response.Body, JsonOptions);
                return flights?
                    .Where(f => string.Equals(f.Status, "started", StringComparison.OrdinalIgnoreCase))
                    .Where(f => string.Equals(f.Type, "regional", StringComparison.OrdinalIgnoreCase)
                        || string.Equals(f.Type, "national", StringComparison.OrdinalIgnoreCase))
                    .ToList() ?? [];
            }
        }
        catch { }

        return [];
    }

    private static async Task<(Dictionary<int, string> Names, Dictionary<int, string?> Breeds)> BuildPigeonMapsAsync(
        AppDbContext db,
        int fancierId)
    {
        var snapshot = await db.RawApiSnapshots
            .AsNoTracking()
            .Where(x => x.SelectedFancierId == fancierId
                && x.Endpoint == "/api/pigeon"
                && x.StatusCode >= 200 && x.StatusCode < 300)
            .OrderByDescending(x => x.Id)
            .FirstOrDefaultAsync();

        if (snapshot is null)
            return (new Dictionary<int, string>(), new Dictionary<int, string?>());

        var translationSnapshots = await db.RawApiSnapshots
            .AsNoTracking()
            .Where(x => x.Endpoint.StartsWith("/api/translation/")
                && x.StatusCode >= 200 && x.StatusCode < 300)
            .ToListAsync();

        var translations = PigeonNameResolver.ReadTranslations(translationSnapshots);

        var pigeons = System.Text.Json.JsonSerializer.Deserialize<PigeonDto[]>(
            snapshot.ResponseBodyJson,
            new System.Text.Json.JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true,
                NumberHandling = System.Text.Json.Serialization.JsonNumberHandling.AllowReadingFromString,
            }) ?? [];

        var nameMap = new Dictionary<int, string>();
        var breedMap = new Dictionary<int, string?>();
        foreach (var p in pigeons)
        {
            if (p.Id is { } id)
            {
                nameMap[id] = PigeonNameResolver.CreateDisplayName(p, translations);
                breedMap[id] = p.Breed;
            }
        }

        return (nameMap, breedMap);
    }
}
