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
        int selectedFancierId)
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
            new("/api/ranking"),
            new("/api/flight", Optional: true),
            new("/api/flight/live", Optional: true),
        ]);

        return quick;
    }
}