using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.EntityFrameworkCore;
using PigeonFancierTracker.Core.Analytics;
using PigeonFancierTracker.Core.Contracts;

namespace PigeonFancierTracker.Infrastructure.Persistence;

public sealed class PigeonOverviewReader(IDbContextFactory<AppDbContext> contextFactory) : IPigeonOverviewReader
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        NumberHandling = JsonNumberHandling.AllowReadingFromString,
    };

    public async Task<PigeonOverviewData> GetOverviewAsync(
        int fancierId,
        CancellationToken cancellationToken = default)
    {
        await using var db = await contextFactory.CreateDbContextAsync(cancellationToken);

        var snapshots = await db.RawApiSnapshots
            .AsNoTracking()
            .Where(x => x.SelectedFancierId == fancierId
                && (x.Endpoint == "/api/pigeon"
                    || x.Endpoint == "/api/couple"
                    || x.Endpoint.StartsWith("/api/translation/"))
                && x.StatusCode >= 200
                && x.StatusCode < 300)
            .ToListAsync(cancellationToken);
        snapshots = snapshots.OrderBy(x => x.CapturedAtUtc).ToList();

        var nameTranslations = PigeonNameResolver.ReadTranslations(snapshots);
        var breedingPigeonIds = ExtractBreedingPigeonIds(snapshots);
        var earningsByPigeon = await BuildEarningsMapAsync(db, fancierId, cancellationToken);

        var pigeonSnapshots = snapshots
            .Where(x => x.Endpoint == "/api/pigeon")
            .ToList();

        if (pigeonSnapshots.Count == 0)
            return new PigeonOverviewData([], 0, null, null);

        var observations = pigeonSnapshots
            .Select(snapshot => new
            {
                Snapshot = snapshot,
                Pigeons = DeserializePigeons(snapshot.ResponseBodyJson),
            })
            .ToArray();

        var pigeonObservations = observations
            .SelectMany(x => x.Pigeons
                .Where(p => p.Id.HasValue)
                .Select(p => new { x.Snapshot, Pigeon = p }))
            .GroupBy(x => x.Pigeon.Id!.Value);

        var totalObservations = 0;
        var pigeonRows = new List<PigeonListItem>();

        foreach (var group in pigeonObservations)
        {
            var orderedObs = group.OrderBy(x => x.Snapshot.CapturedAtUtc).ToArray();
            var latest = orderedObs[^1];

            var points = orderedObs
                .Select(o => CreatePoint(o.Pigeon))
                .ToArray();
            var deduplicated = DeduplicatePoints(points);
            totalObservations += deduplicated.Length;

            SkillSnapshot? current = deduplicated[^1];
            SkillSnapshot? previous = deduplicated.Length >= 2 ? deduplicated[^2] : null;

            var pigeon = latest.Pigeon;
            var skills = pigeon.Skills;
            PigeonSkillsDto? prevSkills = previous?.Skills;

            decimal? skillChange = null;
            if (ComputeTotal(skills) is decimal currentTotal
                && previous is not null
                && ComputeTotal(prevSkills) is decimal previousTotal)
            {
                skillChange = currentTotal - previousTotal;
            }

            var age = pigeon.Years.HasValue && pigeon.Months.HasValue
                ? $"{pigeon.Years.Value}y {pigeon.Months.Value}m"
                : pigeon.TotalMonths is int totalMonths ? $"{totalMonths} months" : null;

            var training = pigeon.Training;
            var trainingParts = new List<string>(3);
            if (training?.Speed == true) trainingParts.Add("Spd");
            if (training?.Technique == true) trainingParts.Add("Tch");
            if (training?.Stamina == true) trainingParts.Add("Sta");
            var trainingDisplay = trainingParts.Count > 0 ? string.Join(" ", trainingParts) : null;

            var sexDisplay = pigeon.Sex?.ToLowerInvariant() switch
            {
                "true" => "♂",
                "false" => "♀",
                _ => pigeon.Sex,
            };

            var breedingMark = breedingPigeonIds.Contains(group.Key) ? "♥" : null;

            EarningsResult? earnings = null;
            if (earningsByPigeon.TryGetValue(group.Key, out var e))
                earnings = e;

            var shortStat = FormatDistanceStat(
                ToOneBased(skills?.Speed), ToOneBased(skills?.Aerodynamics), ToOneBased(skills?.Intelligence),
                ToOneBased(prevSkills?.Speed), ToOneBased(prevSkills?.Aerodynamics), ToOneBased(prevSkills?.Intelligence));
            var mediumStat = FormatDistanceStat(
                ToOneBased(skills?.Stamina), ToOneBased(skills?.Speed), ToOneBased(skills?.Technique),
                ToOneBased(prevSkills?.Stamina), ToOneBased(prevSkills?.Speed), ToOneBased(prevSkills?.Technique));
            var longStat = FormatDistanceStat(
                ToOneBased(skills?.Stamina), ToOneBased(skills?.Navigation), ToOneBased(skills?.Intelligence),
                ToOneBased(prevSkills?.Stamina), ToOneBased(prevSkills?.Navigation), ToOneBased(prevSkills?.Intelligence));

            pigeonRows.Add(new PigeonListItem(
                PigeonNameResolver.CreateDisplayName(pigeon, nameTranslations),
                pigeon.Id,
                sexDisplay,
                age,
                pigeon.Breed,
                ComputeTotal(skills),
                FormatSkillDelta(ComputeTotal(skills), ComputeTotal(prevSkills)),
                FormatSkillDelta(ToOneBased(skills?.Form), ToOneBased(prevSkills?.Form)),
                FormatSkillDelta(ToOneBased(skills?.Experience), ToOneBased(prevSkills?.Experience)),
                FormatSkillDelta(ToOneBased(skills?.Speed), ToOneBased(prevSkills?.Speed)),
                FormatSkillDelta(ToOneBased(skills?.Technique), ToOneBased(prevSkills?.Technique)),
                FormatSkillDelta(ToOneBased(skills?.Stamina), ToOneBased(prevSkills?.Stamina)),
                FormatSkillDelta(ToOneBased(skills?.Aerodynamics), ToOneBased(prevSkills?.Aerodynamics)),
                FormatSkillDelta(ToOneBased(skills?.Intelligence), ToOneBased(prevSkills?.Intelligence)),
                FormatSkillDelta(ToOneBased(skills?.Libido), ToOneBased(prevSkills?.Libido)),
                FormatSkillDelta(ToOneBased(skills?.Nightvision), ToOneBased(prevSkills?.Nightvision)),
                FormatSkillDelta(ToOneBased(skills?.Navigation), ToOneBased(prevSkills?.Navigation)),
                pigeon.Premium,
                pigeon.Flying,
                pigeon.Disease,
                trainingDisplay,
                skillChange,
                breedingMark,
                shortStat,
                mediumStat,
                longStat,
                earnings?.TotalPoints,
                earnings?.TotalEntryFees,
                earnings?.RaceCount,
                earnings is not null ? EarningsCalculator.FormatDisplay(earnings) : null));
        }

        pigeonRows.Sort((a, b) => string.Compare(a.DisplayName, b.DisplayName, StringComparison.OrdinalIgnoreCase));

        return new PigeonOverviewData(
            pigeonRows,
            totalObservations,
            pigeonSnapshots.Min(s => s.CapturedAtUtc),
            pigeonSnapshots.Max(s => s.CapturedAtUtc));
    }

    private sealed record SkillSnapshot(
        decimal? TotalSkill,
        decimal? Form,
        decimal? Experience,
        decimal? Speed,
        decimal? Technique,
        decimal? Stamina,
        decimal? Aerodynamics,
        decimal? Intelligence,
        decimal? Libido,
        decimal? Nightvision,
        decimal? Navigation,
        decimal? Premium,
        PigeonSkillsDto? Skills);

    private static SkillSnapshot CreatePoint(PigeonDto pigeon) =>
        new(
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
            pigeon.Skills);

    private static SkillSnapshot[] DeduplicatePoints(SkillSnapshot[] points)
    {
        if (points.Length == 0)
            return points;

        var result = new List<SkillSnapshot> { points[0] };
        for (var i = 1; i < points.Length; i++)
        {
            if (StatsChanged(points[i], points[i - 1]))
                result.Add(points[i]);
        }

        return result.ToArray();
    }

    private static bool StatsChanged(SkillSnapshot current, SkillSnapshot previous) =>
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

    private static string? FormatSkillDelta(decimal? current, decimal? previous)
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

    private static decimal? ComputeTotal(PigeonSkillsDto? skills) => skills?.Total + 6;

    private static decimal? ToOneBased(decimal? value) => value + 1;

    private static HashSet<int> ExtractBreedingPigeonIds(IReadOnlyList<RawApiSnapshotEntity> snapshots)
    {
        var ids = new HashSet<int>();
        var coupleSnapshot = snapshots
            .Where(x => x.Endpoint == "/api/couple")
            .OrderByDescending(x => x.CapturedAtUtc)
            .FirstOrDefault();

        if (coupleSnapshot is null)
            return ids;

        try
        {
            var couples = JsonSerializer.Deserialize<List<CoupleDto>>(coupleSnapshot.ResponseBodyJson, JsonOptions);
            if (couples is null)
                return ids;

            foreach (var couple in couples)
            {
                if (couple.CockId is int cockId)
                    ids.Add(cockId);
                if (couple.HenId is int henId)
                    ids.Add(henId);
            }
        }
        catch (JsonException)
        {
        }

        return ids;
    }

    private static async Task<Dictionary<int, EarningsResult>> BuildEarningsMapAsync(
        AppDbContext db, int fancierId, CancellationToken ct)
    {
        var raceData = await (
            from r in db.FlightResults.AsNoTracking()
            join f in db.Flights.AsNoTracking() on r.FlightId equals f.Id
            where r.FancierId == fancierId
            select new { r.PigeonId, r.Points, f.EntryPrice }
        ).ToListAsync(ct);

        return raceData
            .GroupBy(x => x.PigeonId)
            .ToDictionary(
                g => g.Key,
                g => EarningsCalculator.Calculate(
                    g.Select(x => new EarningsInput(x.Points, x.EntryPrice)).ToList()));
    }

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
        }

        return [];
    }
}
