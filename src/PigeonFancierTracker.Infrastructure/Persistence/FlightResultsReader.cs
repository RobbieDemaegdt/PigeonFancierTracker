using Microsoft.EntityFrameworkCore;
using PigeonFancierTracker.Core.Analytics;
using PigeonFancierTracker.Core.Contracts;
using PigeonFancierTracker.Core.Domain;

namespace PigeonFancierTracker.Infrastructure.Persistence;

public sealed class FlightResultsReader(IDbContextFactory<AppDbContext> contextFactory) : IFlightResultsReader
{
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

        var recentResults = results
            .Select(x =>
            {
                var category = Enum.TryParse<DistanceCategory>(x.Flight.DistanceCategory, out var c)
                    ? c
                    : DistanceProfileCalculator.Classify(x.Flight.DistanceKm);

                var percentile = x.Result.TotalParticipants > 0
                    ? Math.Round((double)x.Result.Position / x.Result.TotalParticipants * 100, 1)
                    : 0;

                var pigeonName = nameMap.GetValueOrDefault(x.Result.PigeonId) ?? $"Duif #{x.Result.PigeonId}";

                return new FlightResultListItem(
                    x.Flight.Id,
                    x.Flight.Start,
                    x.Flight.Type,
                    x.Flight.LocationName,
                    x.Flight.DistanceKm,
                    category,
                    x.Result.Position,
                    x.Result.TotalParticipants,
                    percentile,
                    x.Result.Points,
                    x.Result.AverageSpeed,
                    pigeonName,
                    x.Result.PigeonId);
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

        return new FlightResultsPageData(recentResults, profiles);
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
