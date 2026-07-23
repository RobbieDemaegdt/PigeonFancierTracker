using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.EntityFrameworkCore;
using PigeonFancierTracker.Core.Contracts;

namespace PigeonFancierTracker.Infrastructure.Persistence;

public sealed class PigeonHistoryReader(IDbContextFactory<AppDbContext> contextFactory) : IPigeonHistoryReader
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        NumberHandling = JsonNumberHandling.AllowReadingFromString,
    };

    public async Task<PigeonHistoryData> GetHistoryAsync(
        int selectedFancierId,
        int? pigeonId = null,
        CancellationToken cancellationToken = default)
    {
        await using var db = await contextFactory.CreateDbContextAsync(cancellationToken);

        var snapshots = await db.RawApiSnapshots
            .AsNoTracking()
            .Where(x => x.SelectedFancierId == selectedFancierId
                && (x.Endpoint == "/api/pigeon"
                    || x.Endpoint.StartsWith("/api/translation/"))
                && x.StatusCode >= 200
                && x.StatusCode < 300)
            .ToListAsync(cancellationToken);
        snapshots = snapshots
            .OrderBy(x => x.CapturedAtUtc)
            .ToList();
        var nameTranslations = PigeonNameResolver.ReadTranslations(snapshots);

        var observations = snapshots
            .Select(snapshot => new
            {
                Snapshot = snapshot,
                Pigeons = DeserializePigeons(snapshot.ResponseBodyJson),
            })
            .ToArray();

        var pigeonRows = observations
            .SelectMany(x => x.Pigeons.Select(pigeon => new { x.Snapshot.CapturedAtUtc, Pigeon = pigeon }))
            .Where(x => x.Pigeon.Id.HasValue)
            .GroupBy(x => x.Pigeon.Id!.Value)
            .Select(group =>
            {
                var latest = group.OrderByDescending(x => x.CapturedAtUtc).First();
                var first = group.Min(x => x.CapturedAtUtc);
                return new PigeonHistoryPigeon(
                    group.Key,
                    PigeonNameResolver.CreateDisplayName(latest.Pigeon, nameTranslations),
                    latest.Pigeon.Sex,
                    latest.Pigeon.Breed,
                    FormatAge(latest.Pigeon),
                    first,
                    latest.CapturedAtUtc);
            })
            .OrderBy(x => x.DisplayName, StringComparer.OrdinalIgnoreCase)
            .ToArray();

        var selected = pigeonId is int requestedId
            ? pigeonRows.FirstOrDefault(x => x.SourceId == requestedId)
            : pigeonRows
                .OrderByDescending(x => x.LastObservedAtUtc)
                .ThenBy(x => x.SourceId)
                .FirstOrDefault();

        var rawPoints = selected is null
            ? []
            : observations
                .SelectMany(x => x.Pigeons
                    .Where(pigeon => pigeon.Id == selected.SourceId)
                    .Select(pigeon => CreatePoint(x.Snapshot, pigeon)))
                .OrderBy(x => x.ObservedAtUtc)
                .ToArray();

        var deduplicated = DeduplicatePoints(rawPoints);
        var points = AddDeltaIndicators(deduplicated);

        return new PigeonHistoryData(pigeonRows, selected, points);
    }

    private static PigeonHistoryPoint[] DeduplicatePoints(PigeonHistoryPoint[] points)
    {
        if (points.Length == 0)
            return points;

        var result = new List<PigeonHistoryPoint> { points[0] };
        for (var i = 1; i < points.Length; i++)
        {
            if (StatsChanged(points[i], points[i - 1]))
                result.Add(points[i]);
        }

        return result.ToArray();
    }

    private static bool StatsChanged(PigeonHistoryPoint current, PigeonHistoryPoint previous) =>
        current.TotalSkill != previous.TotalSkill
        || current.Form != previous.Form
        || current.Experience != previous.Experience
        || current.Speed != previous.Speed
        || current.Technique != previous.Technique
        || current.Stamina != previous.Stamina
        || current.Aerodynamics != previous.Aerodynamics
        || current.Intelligence != previous.Intelligence
        || current.Libido != previous.Libido
        || current.Nightvision != previous.Nightvision
        || current.Navigation != previous.Navigation
        || current.Premium != previous.Premium;

    private static IReadOnlyList<PigeonHistoryPoint> AddDeltaIndicators(PigeonHistoryPoint[] points)
    {
        if (points.Length == 0)
            return points;

        var result = new PigeonHistoryPoint[points.Length];
        result[0] = points[0] with
        {
            TotalSkillDisplay = FormatValue(points[0].TotalSkill),
            FormDisplay = FormatValue(points[0].Form),
            ExperienceDisplay = FormatValue(points[0].Experience),
            SpeedDisplay = FormatValue(points[0].Speed),
            TechniqueDisplay = FormatValue(points[0].Technique),
            StaminaDisplay = FormatValue(points[0].Stamina),
            AerodynamicsDisplay = FormatValue(points[0].Aerodynamics),
            IntelligenceDisplay = FormatValue(points[0].Intelligence),
            LibidoDisplay = FormatValue(points[0].Libido),
            NightvisionDisplay = FormatValue(points[0].Nightvision),
            NavigationDisplay = FormatValue(points[0].Navigation),
            ShortDisplay = FormatDistanceStat(points[0].Speed, points[0].Aerodynamics, points[0].Intelligence),
            MediumDisplay = FormatDistanceStat(points[0].Stamina, points[0].Speed, points[0].Technique),
            LongDisplay = FormatDistanceStat(points[0].Stamina, points[0].Navigation, points[0].Intelligence),
        };

        for (var i = 1; i < points.Length; i++)
        {
            var current = points[i];
            var previous = points[i - 1];

            result[i] = current with
            {
                TotalSkillDisplay = FormatDelta(current.TotalSkill, previous.TotalSkill),
                FormDisplay = FormatDelta(current.Form, previous.Form),
                ExperienceDisplay = FormatDelta(current.Experience, previous.Experience),
                SpeedDisplay = FormatDelta(current.Speed, previous.Speed),
                TechniqueDisplay = FormatDelta(current.Technique, previous.Technique),
                StaminaDisplay = FormatDelta(current.Stamina, previous.Stamina),
                AerodynamicsDisplay = FormatDelta(current.Aerodynamics, previous.Aerodynamics),
                IntelligenceDisplay = FormatDelta(current.Intelligence, previous.Intelligence),
                LibidoDisplay = FormatDelta(current.Libido, previous.Libido),
                NightvisionDisplay = FormatDelta(current.Nightvision, previous.Nightvision),
                NavigationDisplay = FormatDelta(current.Navigation, previous.Navigation),
                ShortDisplay = FormatDistanceStat(
                    current.Speed, current.Aerodynamics, current.Intelligence,
                    previous.Speed, previous.Aerodynamics, previous.Intelligence),
                MediumDisplay = FormatDistanceStat(
                    current.Stamina, current.Speed, current.Technique,
                    previous.Stamina, previous.Speed, previous.Technique),
                LongDisplay = FormatDistanceStat(
                    current.Stamina, current.Navigation, current.Intelligence,
                    previous.Stamina, previous.Navigation, previous.Intelligence),
            };
        }

        return result;
    }

    private static string? FormatValue(decimal? value) =>
        value?.ToString("N0");

    private static string? FormatDelta(decimal? current, decimal? previous)
    {
        if (current is not decimal currentValue)
            return null;

        var display = currentValue.ToString("N0");

        if (previous is not decimal previousValue)
            return display;

        if (currentValue > previousValue)
            return $"{display} ↑";
        if (currentValue < previousValue)
            return $"{display} ↓";

        return display;
    }

    private static string? FormatDistanceStat(
        decimal? skill1, decimal? skill2, decimal? skill3,
        decimal? prev1 = null, decimal? prev2 = null, decimal? prev3 = null)
    {
        if (skill1 is not decimal a || skill2 is not decimal b || skill3 is not decimal c)
            return null;

        var current = a + b + c;
        var display = current.ToString("N0");

        if (prev1 is not decimal pa || prev2 is not decimal pb || prev3 is not decimal pc)
            return display;

        var previous = pa + pb + pc;
        if (current > previous)
            return $"{display} ↑";
        if (current < previous)
            return $"{display} ↓";

        return display;
    }

    private static decimal? ToOneBased(decimal? value) => value + 1;

    private static decimal? ComputeTotal(PigeonSkillsDto? skills) => skills?.Total + 6;

    private static PigeonHistoryPoint CreatePoint(RawApiSnapshotEntity snapshot, PigeonDto pigeon) =>
        new(
            snapshot.CapturedAtUtc,
            ComputeTotal(pigeon.Skills),
            ToOneBased(pigeon.Skills?.Form),
            ToOneBased(pigeon.Skills?.Experience),
            ToOneBased(pigeon.Skills?.Speed),
            ToOneBased(pigeon.Skills?.Technique),
            ToOneBased(pigeon.Skills?.Stamina),
            ToOneBased(pigeon.Skills?.Aerodynamics),
            ToOneBased(pigeon.Skills?.Intelligence),
            ToOneBased(pigeon.Skills?.Libido),
            ToOneBased(pigeon.Skills?.Nightvision),
            ToOneBased(pigeon.Skills?.Navigation),
            pigeon.Premium,
            pigeon.Flying,
            pigeon.Disease,
            snapshot.Id);

    private static string? FormatAge(PigeonDto pigeon) =>
        pigeon.Years.HasValue && pigeon.Months.HasValue
            ? $"{pigeon.Years.Value}y {pigeon.Months.Value}m"
            : pigeon.TotalMonths is int totalMonths ? $"{totalMonths} months" : null;

    private static IReadOnlyList<PigeonDto> DeserializePigeons(string json)
    {
        try
        {
            using var document = JsonDocument.Parse(json);
            var root = document.RootElement;
            if (root.ValueKind == JsonValueKind.Array)
            {
                return JsonSerializer.Deserialize<List<PigeonDto>>(root.GetRawText(), JsonOptions) ?? [];
            }

            if (root.ValueKind == JsonValueKind.Object)
            {
                foreach (var propertyName in new[] { "items", "pigeons", "data" })
                {
                    if (root.TryGetProperty(propertyName, out var property)
                        && property.ValueKind == JsonValueKind.Array)
                    {
                        return JsonSerializer.Deserialize<List<PigeonDto>>(property.GetRawText(), JsonOptions) ?? [];
                    }
                }
            }
        }
        catch (JsonException)
        {
            // Keep the history page usable when one response changes shape.
        }

        return [];
    }
}
