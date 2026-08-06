using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using PigeonFancierTracker.Core.Contracts;
using PigeonFancierTracker.Infrastructure.Persistence;

namespace PigeonFancierTracker.Infrastructure.Management;

public sealed class BreedingManager(
    IDbContextFactory<AppDbContext> contextFactory,
    IAuthenticatedWriteTransport writeTransport,
    ILogger<BreedingManager> logger) : IBreedingManager
{
    private const int IncompatibilityThresholdDays = 21;
    private const int MinBreedingAgeMonths = 3;
    private const int BreedingPenSpaceUnits = 3;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        NumberHandling = JsonNumberHandling.AllowReadingFromString,
    };

    public async Task<BreedingManagementPlan> BuildBreedingPlanAsync(
        int fancierId,
        CancellationToken cancellationToken = default)
    {
        var skipped = new List<string>();

        var pigeons = await ReadPigeonsAsync(fancierId, cancellationToken);
        if (pigeons.Count == 0)
        {
            skipped.Add("No pigeons in roster");
            return new BreedingManagementPlan([], [], 0, 0, skipped);
        }

        var couples = await ReadCouplesAsync(fancierId, cancellationToken);
        var nameTranslations = await ReadNameTranslationsAsync(fancierId, cancellationToken);
        var fancier = await ReadFancierSnapshotAsync(fancierId, cancellationToken);

        var pigeonLookup = pigeons
            .Where(p => p.Id.HasValue)
            .ToDictionary(p => p.Id!.Value);

        var pairedPigeonIds = new HashSet<int>();
        foreach (var couple in couples)
        {
            if (couple.CockId is int cockId) pairedPigeonIds.Add(cockId);
            if (couple.HenId is int henId) pairedPigeonIds.Add(henId);
        }

        var incompatiblePairs = DetectIncompatiblePairs(couples, pigeonLookup, nameTranslations);
        foreach (var pair in incompatiblePairs)
        {
            logger.LogWarning(
                "Incompatible pair detected: couple {CoupleId} ({Cock} + {Hen}), {Days} days without offspring",
                pair.CoupleId, pair.CockName, pair.HenName, pair.Days);
        }

        var capacity = LoftManager.GetCapacityForTier(fancier?.Pen?.Tier);
        var pigeonCount = fancier?.PigeonCount ?? pigeons.Count;
        var currentCoupleCount = couples.Count;

        int availableSlots;
        if (capacity is not null)
        {
            var usedByNonBreeding = pigeonCount - (currentCoupleCount * 2);
            var freeUnits = capacity.Value - usedByNonBreeding - (currentCoupleCount * BreedingPenSpaceUnits);
            availableSlots = Math.Max(0, freeUnits / BreedingPenSpaceUnits);
        }
        else
        {
            skipped.Add($"Unknown loft tier: {fancier?.Pen?.Tier ?? "(null)"}, cannot calculate breeding slots");
            availableSlots = 0;
        }

        logger.LogInformation(
            "Breeding status: {CoupleCount} active couples, {AvailableSlots} breeding slots available, {PigeonCount} pigeons in loft",
            currentCoupleCount, availableSlots, pigeonCount);

        if (availableSlots <= 0)
        {
            skipped.Add("No breeding slots available");
            return new BreedingManagementPlan([], incompatiblePairs, currentCoupleCount, 0, skipped);
        }

        var candidates = FindBreedingCandidates(
            pigeons, pigeonLookup, pairedPigeonIds, nameTranslations, availableSlots, skipped);

        return new BreedingManagementPlan(
            candidates, incompatiblePairs, currentCoupleCount, availableSlots, skipped);
    }

    public async Task ExecuteBreedingPlanAsync(
        BreedingManagementPlan plan,
        CancellationToken cancellationToken = default)
    {
        foreach (var pair in plan.PairsToSplit)
        {
            logger.LogWarning(
                "Incompatible couple {CoupleId} ({Cock} + {Hen}, {Days} days) should be split — delete endpoint not yet configured",
                pair.CoupleId, pair.CockName, pair.HenName, pair.Days);
        }

        foreach (var candidate in plan.PairsToCreate)
        {
            await ExecuteCreateCoupleAsync(candidate, cancellationToken);
        }
    }

    private async Task ExecuteCreateCoupleAsync(BreedingCandidate candidate, CancellationToken ct)
    {
        var request = new CreateCoupleRequest(0, candidate.CockId, candidate.HenId);
        var json = JsonSerializer.Serialize(request, JsonOptions);

        logger.LogInformation(
            "Creating couple: {Cock} (♂) + {Hen} (♀), score={Score:F1}, reason: {Reason}",
            candidate.CockName, candidate.HenName, candidate.CompatibilityScore, candidate.Reason);

        var response = await writeTransport.PostJsonAsync("/api/couple", json, ct);

        if (response.StatusCode >= 200 && response.StatusCode < 300)
        {
            logger.LogInformation(
                "Couple created: {Cock} + {Hen}",
                candidate.CockName, candidate.HenName);
        }
        else
        {
            logger.LogWarning(
                "Couple creation failed ({Cock} + {Hen}) — HTTP {Status}: {Body}",
                candidate.CockName, candidate.HenName, response.StatusCode, response.Body);
        }
    }

    private static IReadOnlyList<IncompatibleCouple> DetectIncompatiblePairs(
        IReadOnlyList<CoupleDto> couples,
        IReadOnlyDictionary<int, PigeonDto> pigeonLookup,
        PigeonNameTranslations nameTranslations)
    {
        var incompatible = new List<IncompatibleCouple>();

        foreach (var couple in couples)
        {
            if (couple.Id is not int coupleId
                || couple.CockId is not int cockId
                || couple.HenId is not int henId
                || couple.Days is not int days)
                continue;

            if (days < IncompatibilityThresholdDays)
                continue;

            var cockName = pigeonLookup.TryGetValue(cockId, out var cock)
                ? PigeonNameResolver.CreateDisplayName(cock, nameTranslations)
                : $"Pigeon #{cockId}";
            var henName = pigeonLookup.TryGetValue(henId, out var hen)
                ? PigeonNameResolver.CreateDisplayName(hen, nameTranslations)
                : $"Pigeon #{henId}";

            incompatible.Add(new IncompatibleCouple(
                coupleId, cockId, cockName, henId, henName, days,
                $"No offspring after {days} days (threshold: {IncompatibilityThresholdDays})"));
        }

        return incompatible;
    }

    private IReadOnlyList<BreedingCandidate> FindBreedingCandidates(
        IReadOnlyList<PigeonDto> pigeons,
        IReadOnlyDictionary<int, PigeonDto> pigeonLookup,
        HashSet<int> pairedPigeonIds,
        PigeonNameTranslations nameTranslations,
        int maxPairs,
        List<string> skipped)
    {
        var cocks = new List<PigeonDto>();
        var hens = new List<PigeonDto>();

        foreach (var pigeon in pigeons)
        {
            if (pigeon.Id is not int id) continue;
            if (pairedPigeonIds.Contains(id))
            {
                skipped.Add($"{PigeonNameResolver.CreateDisplayName(pigeon, nameTranslations)}: already in a couple");
                continue;
            }

            if (pigeon.Disease is not null)
            {
                skipped.Add($"{PigeonNameResolver.CreateDisplayName(pigeon, nameTranslations)}: sick ({pigeon.Disease})");
                continue;
            }

            if (pigeon.Flying == true)
            {
                skipped.Add($"{PigeonNameResolver.CreateDisplayName(pigeon, nameTranslations)}: currently flying");
                continue;
            }

            if ((pigeon.TotalMonths ?? 0) < MinBreedingAgeMonths)
            {
                skipped.Add($"{PigeonNameResolver.CreateDisplayName(pigeon, nameTranslations)}: too young ({pigeon.TotalMonths ?? 0} months)");
                continue;
            }

            var isMale = string.Equals(pigeon.Sex, "true", StringComparison.OrdinalIgnoreCase);
            var isFemale = string.Equals(pigeon.Sex, "false", StringComparison.OrdinalIgnoreCase);

            if (isMale) cocks.Add(pigeon);
            else if (isFemale) hens.Add(pigeon);
            else skipped.Add($"{PigeonNameResolver.CreateDisplayName(pigeon, nameTranslations)}: unknown sex ({pigeon.Sex})");
        }

        if (cocks.Count == 0 || hens.Count == 0)
        {
            skipped.Add($"Not enough unpaired pigeons: {cocks.Count} cocks, {hens.Count} hens");
            return [];
        }

        logger.LogInformation(
            "Breeding candidates: {Cocks} cocks, {Hens} hens available for pairing",
            cocks.Count, hens.Count);

        var scoredPairs = new List<BreedingCandidate>();

        foreach (var cock in cocks)
        {
            foreach (var hen in hens)
            {
                var (score, reason) = ScorePair(cock, hen, nameTranslations);
                scoredPairs.Add(new BreedingCandidate(
                    cock.Id!.Value,
                    PigeonNameResolver.CreateDisplayName(cock, nameTranslations),
                    cock.Skills?.Libido ?? 0,
                    cock.Skills?.Total ?? 0,
                    hen.Id!.Value,
                    PigeonNameResolver.CreateDisplayName(hen, nameTranslations),
                    hen.Skills?.Libido ?? 0,
                    hen.Skills?.Total ?? 0,
                    score,
                    reason));
            }
        }

        scoredPairs.Sort((a, b) => b.CompatibilityScore.CompareTo(a.CompatibilityScore));

        var selected = new List<BreedingCandidate>();
        var usedCocks = new HashSet<int>();
        var usedHens = new HashSet<int>();

        foreach (var pair in scoredPairs)
        {
            if (selected.Count >= maxPairs) break;
            if (usedCocks.Contains(pair.CockId) || usedHens.Contains(pair.HenId)) continue;

            selected.Add(pair);
            usedCocks.Add(pair.CockId);
            usedHens.Add(pair.HenId);
        }

        return selected;
    }

    private static (double Score, string Reason) ScorePair(
        PigeonDto cock,
        PigeonDto hen,
        PigeonNameTranslations nameTranslations)
    {
        var parts = new List<string>();
        double totalScore = 0;

        var cockSkills = cock.Skills;
        var henSkills = hen.Skills;

        var cockLibido = (double)(cockSkills?.Libido ?? 0);
        var henLibido = (double)(henSkills?.Libido ?? 0);
        var libidoScore = (cockLibido + henLibido) * 3.0;
        totalScore += libidoScore;
        if (libidoScore > 0)
            parts.Add($"libido={libidoScore:F0}");

        var cockTotal = (double)(cockSkills?.Total ?? 0);
        var henTotal = (double)(henSkills?.Total ?? 0);
        var skillScore = (cockTotal + henTotal) * 0.5;
        totalScore += skillScore;
        if (skillScore > 0)
            parts.Add($"skills={skillScore:F0}");

        if (cock.Breed is not null && hen.Breed is not null
            && string.Equals(cock.Breed, hen.Breed, StringComparison.OrdinalIgnoreCase))
        {
            totalScore += 15;
            parts.Add("same-breed=+15");
        }

        var complementarity = ComputeComplementarity(cockSkills, henSkills);
        totalScore += complementarity;
        if (complementarity > 0)
            parts.Add($"complementary={complementarity:F0}");

        var cockAge = cock.TotalMonths ?? 0;
        var henAge = hen.TotalMonths ?? 0;
        if (cockAge > 42 || henAge > 42)
        {
            var penalty = -10.0;
            totalScore += penalty;
            parts.Add($"age-penalty={penalty:F0}");
        }

        return (totalScore, string.Join(" + ", parts));
    }

    private static double ComputeComplementarity(PigeonSkillsDto? a, PigeonSkillsDto? b)
    {
        if (a is null || b is null) return 0;

        var skills = new[]
        {
            ((double)(a.Speed ?? 0), (double)(b.Speed ?? 0)),
            ((double)(a.Stamina ?? 0), (double)(b.Stamina ?? 0)),
            ((double)(a.Aerodynamics ?? 0), (double)(b.Aerodynamics ?? 0)),
            ((double)(a.Technique ?? 0), (double)(b.Technique ?? 0)),
            ((double)(a.Navigation ?? 0), (double)(b.Navigation ?? 0)),
            ((double)(a.Intelligence ?? 0), (double)(b.Intelligence ?? 0)),
        };

        double complementScore = 0;
        foreach (var (sa, sb) in skills)
        {
            var combined = Math.Max(sa, sb);
            complementScore += combined;
        }

        return complementScore * 0.3;
    }

    private async Task<IReadOnlyList<PigeonDto>> ReadPigeonsAsync(int fancierId, CancellationToken ct)
    {
        await using var db = await contextFactory.CreateDbContextAsync(ct);
        var snapshot = await db.RawApiSnapshots
            .AsNoTracking()
            .Where(x => x.SelectedFancierId == fancierId
                && x.Endpoint == "/api/pigeon"
                && x.StatusCode >= 200 && x.StatusCode < 300)
            .OrderByDescending(x => x.Id)
            .FirstOrDefaultAsync(ct);

        if (snapshot is null) return [];

        try
        {
            return JsonSerializer.Deserialize<List<PigeonDto>>(snapshot.ResponseBodyJson, JsonOptions) ?? [];
        }
        catch (JsonException)
        {
            return [];
        }
    }

    private async Task<IReadOnlyList<CoupleDto>> ReadCouplesAsync(int fancierId, CancellationToken ct)
    {
        await using var db = await contextFactory.CreateDbContextAsync(ct);
        var snapshot = await db.RawApiSnapshots
            .AsNoTracking()
            .Where(x => x.SelectedFancierId == fancierId
                && x.Endpoint == "/api/couple"
                && x.StatusCode >= 200 && x.StatusCode < 300)
            .OrderByDescending(x => x.Id)
            .FirstOrDefaultAsync(ct);

        if (snapshot is null) return [];

        try
        {
            return JsonSerializer.Deserialize<List<CoupleDto>>(snapshot.ResponseBodyJson, JsonOptions) ?? [];
        }
        catch (JsonException)
        {
            return [];
        }
    }

    private async Task<PigeonNameTranslations> ReadNameTranslationsAsync(int fancierId, CancellationToken ct)
    {
        await using var db = await contextFactory.CreateDbContextAsync(ct);
        var snapshots = await db.RawApiSnapshots
            .AsNoTracking()
            .Where(x => x.SelectedFancierId == fancierId
                && x.Endpoint.StartsWith("/api/translation/")
                && x.StatusCode >= 200 && x.StatusCode < 300)
            .OrderByDescending(x => x.CapturedAtUtc)
            .Take(2)
            .ToListAsync(ct);

        return PigeonNameResolver.ReadTranslations(snapshots);
    }

    private async Task<SelectedFancierDto?> ReadFancierSnapshotAsync(int fancierId, CancellationToken ct)
    {
        await using var db = await contextFactory.CreateDbContextAsync(ct);
        var snapshot = await db.RawApiSnapshots
            .AsNoTracking()
            .Where(x => x.SelectedFancierId == fancierId
                && x.Endpoint == "/api/fancier/selected"
                && x.StatusCode >= 200 && x.StatusCode < 300)
            .OrderByDescending(x => x.Id)
            .FirstOrDefaultAsync(ct);

        if (snapshot is null) return null;

        try
        {
            return JsonSerializer.Deserialize<SelectedFancierDto>(snapshot.ResponseBodyJson, JsonOptions);
        }
        catch (JsonException)
        {
            return null;
        }
    }
}
