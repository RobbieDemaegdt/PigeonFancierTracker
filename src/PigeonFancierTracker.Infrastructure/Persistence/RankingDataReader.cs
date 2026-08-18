using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.EntityFrameworkCore;
using PigeonFancierTracker.Core.Contracts;

namespace PigeonFancierTracker.Infrastructure.Persistence;

public sealed class RankingDataReader(IDbContextFactory<AppDbContext> contextFactory) : IRankingDataReader
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        NumberHandling = JsonNumberHandling.AllowReadingFromString,
    };

    public async Task<RankingPageData> GetRankingAsync(int fancierId)
    {
        await using var db = await contextFactory.CreateDbContextAsync();

        var regionalFanciers = await ReadRankingListAsync(db, "fanciers", "regional", fancierId);
        var regionalPigeons = await ReadPigeonRankingListAsync(db, "pigeons", "regional", fancierId);
        var nationalFanciers = await ReadRankingListAsync(db, "fanciers", "national", fancierId);
        var nationalPigeons = await ReadPigeonRankingListAsync(db, "pigeons", "national", fancierId);

        if (regionalFanciers.Count == 0 && nationalFanciers.Count == 0)
        {
            var legacySnapshot = await db.RawApiSnapshots
                .AsNoTracking()
                .Where(x => x.Endpoint == "/api/ranking"
                    && (x.NormalizedQuery == null || x.NormalizedQuery == "")
                    && x.StatusCode >= 200 && x.StatusCode < 300)
                .OrderByDescending(x => x.Id)
                .FirstOrDefaultAsync();

            if (legacySnapshot is not null)
            {
                try
                {
                    return ParseRankingSnapshot(legacySnapshot.ResponseBodyJson, fancierId);
                }
                catch (JsonException) { }
            }
        }

        return new RankingPageData(regionalFanciers, regionalPigeons, nationalFanciers, nationalPigeons);
    }

    private static async Task<List<RankingEntry>> ReadRankingListAsync(
        AppDbContext db, string type, string rankingType, int fancierId)
    {
        var snapshot = await db.RawApiSnapshots
            .AsNoTracking()
            .Where(x => x.Endpoint == "/api/ranking"
                && x.NormalizedQuery != null
                && x.NormalizedQuery.Contains($"type={type}")
                && x.NormalizedQuery.Contains($"rankingType={rankingType}")
                && x.StatusCode >= 200 && x.StatusCode < 300)
            .OrderByDescending(x => x.Id)
            .FirstOrDefaultAsync();

        if (snapshot is null) return [];

        try
        {
            return ParseFancierRankingResponse(snapshot.ResponseBodyJson, fancierId);
        }
        catch (JsonException)
        {
            return [];
        }
    }

    private static async Task<List<PigeonRankingEntry>> ReadPigeonRankingListAsync(
        AppDbContext db, string type, string rankingType, int fancierId)
    {
        var snapshot = await db.RawApiSnapshots
            .AsNoTracking()
            .Where(x => x.Endpoint == "/api/ranking"
                && x.NormalizedQuery != null
                && x.NormalizedQuery.Contains($"type={type}")
                && x.NormalizedQuery.Contains($"rankingType={rankingType}")
                && x.StatusCode >= 200 && x.StatusCode < 300)
            .OrderByDescending(x => x.Id)
            .FirstOrDefaultAsync();

        if (snapshot is null) return [];

        try
        {
            return ParsePigeonRankingResponse(snapshot.ResponseBodyJson, fancierId);
        }
        catch (JsonException)
        {
            return [];
        }
    }

    private static List<RankingEntry> ParseFancierRankingResponse(string json, int fancierId)
    {
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;

        var array = root;
        if (root.ValueKind == JsonValueKind.Object)
        {
            if (root.TryGetProperty("items", out var items) && items.ValueKind == JsonValueKind.Array)
                array = items;
            else if (root.TryGetProperty("data", out var data) && data.ValueKind == JsonValueKind.Array)
                array = data;
            else if (root.TryGetProperty("rankings", out var rankings) && rankings.ValueKind == JsonValueKind.Array)
                array = rankings;
            else
                return [];
        }

        if (array.ValueKind != JsonValueKind.Array) return [];

        return TryParseFancierRankingFromArray(array, fancierId);
    }

    private static List<PigeonRankingEntry> ParsePigeonRankingResponse(string json, int fancierId)
    {
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;

        var array = root;
        if (root.ValueKind == JsonValueKind.Object)
        {
            if (root.TryGetProperty("items", out var items) && items.ValueKind == JsonValueKind.Array)
                array = items;
            else if (root.TryGetProperty("data", out var data) && data.ValueKind == JsonValueKind.Array)
                array = data;
            else if (root.TryGetProperty("rankings", out var rankings) && rankings.ValueKind == JsonValueKind.Array)
                array = rankings;
            else
                return [];
        }

        if (array.ValueKind != JsonValueKind.Array) return [];

        var entries = new List<PigeonRankingEntry>();
        var position = 1;

        foreach (var item in array.EnumerateArray())
        {
            var pigeonId = TryGetInt(item, "pigeonId") ?? TryGetInt(item, "id") ?? 0;
            var pigeonName = TryGetString(item, "pigeonName") ?? TryGetString(item, "displayName")
                ?? TryGetString(item, "name") ?? $"#{pigeonId}";
            var fId = TryGetInt(item, "fancierId") ?? 0;
            var fancierName = TryGetString(item, "fancier") ?? TryGetString(item, "fancierName")
                ?? TryGetString(item, "fancierDisplayName") ?? $"#{fId}";
            var points = TryGetInt(item, "points") ?? TryGetInt(item, "totalPoints") ?? 0;
            var pos = TryGetInt(item, "position") ?? position;

            entries.Add(new PigeonRankingEntry(pos, pigeonName, pigeonId, fancierName, fId, points, fId == fancierId));
            position++;
        }

        return entries;
    }

    public Task<IReadOnlyList<PredictedRankingEntry>> GetPredictedRankingAsync(
        int fancierId,
        IReadOnlyList<ActiveFlightInfo> activeFlights)
    {
        return GetPredictedRankingInternalAsync(fancierId, activeFlights);
    }

    private async Task<IReadOnlyList<PredictedRankingEntry>> GetPredictedRankingInternalAsync(
        int fancierId,
        IReadOnlyList<ActiveFlightInfo> activeFlights)
    {
        var ranking = await GetRankingAsync(fancierId);
        if (ranking.RegionalFanciers.Count == 0) return [];

        var regionalFlights = activeFlights
            .Where(f => string.Equals(f.FlightType, "regional", StringComparison.OrdinalIgnoreCase))
            .ToList();

        if (regionalFlights.Count == 0) return [];

        var pointsToAdd = new Dictionary<int, int>();
        foreach (var flight in regionalFlights)
        {
            foreach (var standing in flight.FancierStandings)
            {
                pointsToAdd.TryGetValue(standing.FancierId, out var existing);
                pointsToAdd[standing.FancierId] = existing + standing.TotalPoints;
            }
        }

        var predicted = ranking.RegionalFanciers
            .Select(entry =>
            {
                var delta = pointsToAdd.GetValueOrDefault(entry.Id, 0);
                return new PredictedRankingEntry(
                    0,
                    entry.Name,
                    entry.Id,
                    entry.Points,
                    entry.Points + delta,
                    delta,
                    0,
                    entry.IsOwn);
            })
            .OrderByDescending(e => e.PredictedPoints)
            .ThenBy(e => e.Name)
            .ToList();

        for (var i = 0; i < predicted.Count; i++)
        {
            var original = ranking.RegionalFanciers
                .FirstOrDefault(r => r.Id == predicted[i].Id);
            var originalPosition = original?.Position ?? i + 1;
            var positionDelta = originalPosition - (i + 1);

            predicted[i] = predicted[i] with
            {
                Position = i + 1,
                PositionDelta = positionDelta,
            };
        }

        return predicted;
    }

    private static RankingPageData ParseRankingSnapshot(string json, int fancierId)
    {
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;

        var regionalFanciers = new List<RankingEntry>();
        var regionalPigeons = new List<PigeonRankingEntry>();
        var nationalFanciers = new List<RankingEntry>();
        var nationalPigeons = new List<PigeonRankingEntry>();

        if (root.ValueKind == JsonValueKind.Object)
        {
            regionalFanciers = TryParseFancierRanking(root, "regionalFanciers", fancierId)
                ?? TryParseFancierRanking(root, "regional", fancierId)
                ?? [];
            regionalPigeons = TryParsePigeonRanking(root, "regionalPigeons", fancierId)
                ?? [];
            nationalFanciers = TryParseFancierRanking(root, "nationalFanciers", fancierId)
                ?? TryParseFancierRanking(root, "national", fancierId)
                ?? [];
            nationalPigeons = TryParsePigeonRanking(root, "nationalPigeons", fancierId)
                ?? [];

            if (regionalFanciers.Count == 0 && nationalFanciers.Count == 0)
            {
                foreach (var prop in root.EnumerateObject())
                {
                    if (prop.Value.ValueKind == JsonValueKind.Array)
                    {
                        var entries = TryParseFancierRankingFromArray(prop.Value, fancierId);
                        if (entries.Count > 0)
                        {
                            if (prop.Name.Contains("regional", StringComparison.OrdinalIgnoreCase))
                                regionalFanciers = entries;
                            else if (prop.Name.Contains("national", StringComparison.OrdinalIgnoreCase))
                                nationalFanciers = entries;
                            else if (regionalFanciers.Count == 0)
                                regionalFanciers = entries;
                        }
                    }
                }
            }
        }
        else if (root.ValueKind == JsonValueKind.Array)
        {
            regionalFanciers = TryParseFancierRankingFromArray(root, fancierId);
        }

        return new RankingPageData(
            regionalFanciers, regionalPigeons,
            nationalFanciers, nationalPigeons);
    }

    private static List<RankingEntry>? TryParseFancierRanking(
        JsonElement root, string propertyName, int fancierId)
    {
        if (!root.TryGetProperty(propertyName, out var prop))
            return null;

        if (prop.ValueKind != JsonValueKind.Array)
            return null;

        return TryParseFancierRankingFromArray(prop, fancierId);
    }

    private static List<RankingEntry> TryParseFancierRankingFromArray(
        JsonElement array, int fancierId)
    {
        var entries = new List<RankingEntry>();
        var position = 1;

        foreach (var item in array.EnumerateArray())
        {
            var id = TryGetInt(item, "id") ?? TryGetInt(item, "fancierId") ?? 0;
            var name = TryGetString(item, "displayName")
                ?? TryGetString(item, "name")
                ?? TryGetString(item, "fancier")
                ?? $"#{id}";
            var points = TryGetInt(item, "points") ?? TryGetInt(item, "totalPoints") ?? 0;
            var pos = TryGetInt(item, "position") ?? position;

            entries.Add(new RankingEntry(pos, name, id, points, id == fancierId));
            position++;
        }

        return entries;
    }

    private static List<PigeonRankingEntry>? TryParsePigeonRanking(
        JsonElement root, string propertyName, int fancierId)
    {
        if (!root.TryGetProperty(propertyName, out var prop))
            return null;

        if (prop.ValueKind != JsonValueKind.Array)
            return null;

        var entries = new List<PigeonRankingEntry>();
        var position = 1;

        foreach (var item in prop.EnumerateArray())
        {
            var pigeonId = TryGetInt(item, "pigeonId") ?? TryGetInt(item, "id") ?? 0;
            var pigeonName = TryGetString(item, "pigeonName") ?? TryGetString(item, "name") ?? $"#{pigeonId}";
            var fId = TryGetInt(item, "fancierId") ?? 0;
            var fancierName = TryGetString(item, "fancier") ?? TryGetString(item, "fancierName") ?? $"#{fId}";
            var points = TryGetInt(item, "points") ?? TryGetInt(item, "totalPoints") ?? 0;
            var pos = TryGetInt(item, "position") ?? position;

            entries.Add(new PigeonRankingEntry(pos, pigeonName, pigeonId, fancierName, fId, points, fId == fancierId));
            position++;
        }

        return entries;
    }

    private static int? TryGetInt(JsonElement element, string propertyName)
    {
        if (!element.TryGetProperty(propertyName, out var prop)) return null;
        if (prop.ValueKind == JsonValueKind.Number && prop.TryGetInt32(out var val)) return val;
        if (prop.ValueKind == JsonValueKind.String && int.TryParse(prop.GetString(), out var parsed)) return parsed;
        return null;
    }

    private static string? TryGetString(JsonElement element, string propertyName)
    {
        if (!element.TryGetProperty(propertyName, out var prop)) return null;
        return prop.ValueKind == JsonValueKind.String ? prop.GetString() : null;
    }
}
