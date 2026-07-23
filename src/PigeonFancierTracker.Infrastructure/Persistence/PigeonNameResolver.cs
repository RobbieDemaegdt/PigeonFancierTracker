using System.Globalization;
using System.Text.Json;
using PigeonFancierTracker.Core.Contracts;

namespace PigeonFancierTracker.Infrastructure.Persistence;

public sealed record PigeonNameTranslations(
    IReadOnlyDictionary<int, string> FirstNames,
    IReadOnlyDictionary<int, string> LastNames);

public static class PigeonNameResolver
{
    public static PigeonNameTranslations ReadTranslations(IEnumerable<RawApiSnapshotEntity> snapshots)
    {
        var firstNames = new Dictionary<int, string>();
        var lastNames = new Dictionary<int, string>();

        foreach (var snapshot in snapshots
            .Where(x => x.StatusCode is >= 200 and < 300
                && x.Endpoint.StartsWith("/api/translation/", StringComparison.Ordinal))
            .OrderByDescending(x => x.CapturedAtUtc))
        {
            try
            {
                using var document = JsonDocument.Parse(snapshot.ResponseBodyJson);
                AddTranslations(document.RootElement, "first-name", firstNames);
                AddTranslations(document.RootElement, "last-name", lastNames);
            }
            catch (JsonException)
            {
                // Keep the pigeon list usable when a translation response changes shape.
            }
        }

        return new PigeonNameTranslations(firstNames, lastNames);
    }

    public static string CreateDisplayName(
        PigeonDto pigeon,
        PigeonNameTranslations translations)
    {
        var nameParts = new[]
        {
            GetName(translations.FirstNames, pigeon.FirstNameId),
            GetName(translations.LastNames, pigeon.LastNameId),
        };
        var name = string.Join(' ', nameParts.Where(x => !string.IsNullOrWhiteSpace(x)));

        return string.IsNullOrWhiteSpace(name)
            ? pigeon.Id is int id ? $"Pigeon #{id}" : "Pigeon"
            : name;
    }

    public static string CreateDisplayName(
        TransferPigeonDto pigeon,
        PigeonNameTranslations translations)
    {
        var nameParts = new[]
        {
            GetName(translations.FirstNames, pigeon.FirstNameId),
            GetName(translations.LastNames, pigeon.LastNameId),
        };
        var name = string.Join(' ', nameParts.Where(x => !string.IsNullOrWhiteSpace(x)));

        return string.IsNullOrWhiteSpace(name)
            ? pigeon.Id is int id ? $"Pigeon #{id}" : "Pigeon"
            : name;
    }

    private static void AddTranslations(
        JsonElement root,
        string category,
        IDictionary<int, string> translations)
    {
        if (!root.TryGetProperty(category, out var categoryElement)
            || categoryElement.ValueKind != JsonValueKind.Object)
        {
            return;
        }

        foreach (var property in categoryElement.EnumerateObject())
        {
            if (int.TryParse(property.Name, NumberStyles.Integer, CultureInfo.InvariantCulture, out var id)
                && property.Value.ValueKind == JsonValueKind.String
                && !translations.ContainsKey(id))
            {
                translations[id] = property.Value.GetString()!;
            }
        }
    }

    private static string? GetName(
        IReadOnlyDictionary<int, string> translations,
        int? id) =>
        id is int value && translations.TryGetValue(value, out var name) ? name : null;
}