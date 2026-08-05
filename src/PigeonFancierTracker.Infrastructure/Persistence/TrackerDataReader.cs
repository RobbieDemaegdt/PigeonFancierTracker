using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using PigeonFancierTracker.Core.Analytics;
using PigeonFancierTracker.Core.Contracts;

namespace PigeonFancierTracker.Infrastructure.Persistence;

public sealed class TrackerDataReader(IDbContextFactory<AppDbContext> contextFactory) : ITrackerDataReader
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
    };

    public async Task<TrackerDashboardData> GetDashboardAsync(
        int selectedFancierId,
        CancellationToken cancellationToken = default)
    {
        await using var db = await contextFactory.CreateDbContextAsync(cancellationToken);

        var relevantSnapshots = await db.RawApiSnapshots
            .AsNoTracking()
            .Where(x => x.SelectedFancierId == selectedFancierId
                && x.StatusCode >= 200
                && x.StatusCode < 300
                && (x.Endpoint == "/api/fancier/selected"
                    || x.Endpoint == "/api/pigeon"
                    || x.Endpoint == "/api/season"
                    || x.Endpoint == "/api/couple"
                    || x.Endpoint.StartsWith("/api/translation/")))
            .ToListAsync(cancellationToken);
        relevantSnapshots = relevantSnapshots
            .OrderByDescending(x => x.CapturedAtUtc)
            .ToList();

        var selectedFancierSnapshot = relevantSnapshots
            .Where(x => x.Endpoint == "/api/fancier/selected")
            .FirstOrDefault();
        var seasonSnapshot = relevantSnapshots
            .Where(x => x.Endpoint == "/api/season")
            .FirstOrDefault();
        var pigeonSnapshots = relevantSnapshots
            .Where(x => x.Endpoint == "/api/pigeon")
            .Take(2)
            .ToArray();

        var selectedFancier = selectedFancierSnapshot is null
            ? null
            : Deserialize<SelectedFancierDto>(selectedFancierSnapshot.ResponseBodyJson);
        var season = seasonSnapshot is null
            ? null
            : Deserialize<SeasonDto>(seasonSnapshot.ResponseBodyJson);
        var currentPigeons = pigeonSnapshots.ElementAtOrDefault(0) is { } currentSnapshot
            ? DeserializePigeons(currentSnapshot.ResponseBodyJson)
            : [];
        var previousPigeons = pigeonSnapshots.ElementAtOrDefault(1) is { } previousSnapshot
            ? DeserializePigeons(previousSnapshot.ResponseBodyJson)
            : [];
        var previousById = previousPigeons
            .Where(x => x.Id.HasValue)
            .ToDictionary(x => x.Id!.Value);
        var nameTranslations = PigeonNameResolver.ReadTranslations(relevantSnapshots);
        var breedingPigeonIds = ExtractBreedingPigeonIds(relevantSnapshots);
        var earningsByPigeon = await BuildEarningsMapAsync(db, selectedFancierId, cancellationToken);

        var pigeonRows = currentPigeons
            .OrderByDescending(p => p.TotalMonths ?? 0)
            .Select(pigeon => CreatePigeonRow(pigeon, previousById, nameTranslations, breedingPigeonIds, earningsByPigeon))
            .ToArray();
        var skillValues = pigeonRows
            .Where(x => x.TotalSkill.HasValue)
            .Select(x => x.TotalSkill!.Value)
            .ToArray();
        var skillChanges = pigeonRows
            .Where(x => x.SkillChange.HasValue)
            .Select(x => x.SkillChange!.Value)
            .ToArray();

        return new TrackerDashboardData(
            selectedFancier?.DisplayName,
            selectedFancier?.Id ?? selectedFancierId,
            selectedFancier?.PigeonCount ?? (pigeonRows.Length == 0 ? null : pigeonRows.Length),
            selectedFancier?.Finances?.Capital,
            selectedFancier?.Finances?.PreviousBalance,
            selectedFancier?.Finances?.TransferBalance,
            skillValues.Length == 0 ? null : skillValues.Average(),
            skillChanges.Length == 0 ? null : skillChanges.Average(),
            relevantSnapshots.FirstOrDefault()?.CapturedAtUtc,
            season?.Number,
            season?.Week,
            selectedFancier?.Pen?.Occupied,
            selectedFancier?.Pen?.Capacity,
            selectedFancier?.Location?.Name,
            selectedFancier?.Food?.Amount,
            pigeonRows);
    }

    private static PigeonListItem CreatePigeonRow(
        PigeonDto pigeon,
        IReadOnlyDictionary<int, PigeonDto> previousById,
        PigeonNameTranslations nameTranslations,
        HashSet<int> breedingPigeonIds,
        IReadOnlyDictionary<int, EarningsResult> earningsByPigeon)
    {
        PigeonSkillsDto? prevSkills = null;
        decimal? skillChange = null;
        if (pigeon.Id is int id
            && ComputeTotal(pigeon.Skills) is decimal currentTotal
            && previousById.TryGetValue(id, out var previous)
            && ComputeTotal(previous.Skills) is decimal previousTotal)
        {
            skillChange = currentTotal - previousTotal;
            prevSkills = previous.Skills;
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

        var skills = pigeon.Skills;

        var sexDisplay = pigeon.Sex?.ToLowerInvariant() switch
        {
            "true" => "♂",
            "false" => "♀",
            _ => pigeon.Sex,
        };

        var breedingMark = pigeon.Id is int pigeonId && breedingPigeonIds.Contains(pigeonId) ? "♥" : null;

        EarningsResult? earnings = null;
        if (pigeon.Id is int earningsId && earningsByPigeon.TryGetValue(earningsId, out var e))
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

        return new PigeonListItem(
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
            earnings is not null ? EarningsCalculator.FormatDisplay(earnings) : null);
    }

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

    private static T? Deserialize<T>(string json)
    {
        try
        {
            return JsonSerializer.Deserialize<T>(json, JsonOptions);
        }
        catch (JsonException)
        {
            return default;
        }
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

    private static decimal? ComputeTotal(PigeonSkillsDto? skills) => skills?.Total + 6;

    private static decimal? ToOneBased(decimal? value) => value + 1;

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
            // Keep the dashboard usable when an endpoint changes shape. The raw response remains available.
        }

        return [];
    }
}