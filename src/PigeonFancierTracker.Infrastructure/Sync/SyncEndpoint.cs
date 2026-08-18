using PigeonFancierTracker.Core.Domain;

namespace PigeonFancierTracker.Infrastructure.Sync;

public sealed record SyncEndpoint(
    string Path,
    IReadOnlyDictionary<string, string?>? Query = null,
    bool Optional = false);

public static class SyncEndpointCatalog
{
    public static IReadOnlyList<SyncEndpoint> ForProfile(
        SyncProfile profile,
        int selectedFancierId,
        int? season = null,
        int? department = null,
        IReadOnlyList<int>? activeFlightIds = null)
    {
        var quick = new List<SyncEndpoint>
        {
            new("/api/user"),
            new("/api/season"),
            new("/api/translation/nl"),
            new("/api/fancier/selected"),
            new($"/api/fancier/{selectedFancierId}"),
            new($"/api/fancier/{selectedFancierId}/pigeons"),
            new("/api/pigeon"),
            new("/api/couple"),
            new("/api/group"),
            new("/api/fancier/items"),
            new("/api/fancier/reports"),
            new("/api/transfer", new Dictionary<string, string?> { ["processed"] = "false" }),
            new("/api/transfer", new Dictionary<string, string?> { ["processed"] = "true" }),
        };

        if (profile == SyncProfile.Quick)
        {
            return quick;
        }

        quick.AddRange(
        [
            new("/api/weather"),
            new("/api/transfer", new Dictionary<string, string?>
            {
                ["processed"] = "false",
                ["fancierId"] = selectedFancierId.ToString(),
            }),
            new("/api/transfer", new Dictionary<string, string?>
            {
                ["processed"] = "true",
                ["fancierId"] = selectedFancierId.ToString(),
            }),
        ]);

        if (season is not null && department is not null)
        {
            var s = season.Value.ToString();
            var d = department.Value.ToString();

            quick.Add(new("/api/flight", new Dictionary<string, string?>
            {
                ["season"] = s,
                ["department"] = d,
                ["public"] = "false",
                ["status"] = "notStarted",
            }, Optional: true));

            quick.Add(new("/api/flight", new Dictionary<string, string?>
            {
                ["season"] = s,
                ["department"] = d,
                ["public"] = "false",
                ["status"] = "started",
            }, Optional: true));

            quick.Add(new("/api/flight", new Dictionary<string, string?>
            {
                ["season"] = s,
                ["department"] = d,
                ["public"] = "false",
                ["status"] = "ended",
            }, Optional: true));

            quick.Add(new("/api/flight/live", new Dictionary<string, string?>
            {
                ["season"] = s,
                ["department"] = d,
                ["public"] = "false",
            }, Optional: true));

            foreach (var (type, rankingType) in new[]
            {
                ("fanciers", "regional"),
                ("pigeons", "regional"),
                ("fanciers", "national"),
                ("pigeons", "national"),
            })
            {
                quick.Add(new("/api/ranking", new Dictionary<string, string?>
                {
                    ["activeSort"] = "position",
                    ["sortDirection"] = "asc",
                    ["season"] = s,
                    ["type"] = type,
                    ["ageType"] = "elder",
                    ["rankingType"] = rankingType,
                    ["department"] = d,
                    ["page"] = "1",
                    ["pageSize"] = "500",
                }));
            }
        }
        else
        {
            quick.Add(new("/api/flight", new Dictionary<string, string?> { ["status"] = "notStarted" }, Optional: true));
            quick.Add(new("/api/flight", new Dictionary<string, string?> { ["status"] = "started" }, Optional: true));
            quick.Add(new("/api/flight", new Dictionary<string, string?> { ["status"] = "ended" }, Optional: true));
            quick.Add(new("/api/flight/live", Optional: true));
        }

        if (activeFlightIds is { Count: > 0 })
        {
            foreach (var flightId in activeFlightIds)
            {
                quick.Add(new($"/api/flight/{flightId}/results", Optional: true));
            }
        }

        return quick;
    }
}