using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using PigeonFancierTracker.Core.Analytics;
using PigeonFancierTracker.Core.Contracts;
using PigeonFancierTracker.Core.Domain;
using PigeonFancierTracker.Infrastructure.Persistence;

namespace PigeonFancierTracker.Infrastructure.Management;

public sealed class TrainingManager(
    IDbContextFactory<AppDbContext> contextFactory,
    IAuthenticatedWriteTransport writeTransport,
    ISessionStateService sessionState,
    ILogger<TrainingManager> logger) : ITrainingManager
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        NumberHandling = JsonNumberHandling.AllowReadingFromString,
    };

    private enum Skill { Speed, Stamina, Aerodynamics, Technique, Navigation, Intelligence }

    private static readonly Dictionary<(DistanceCategory, TrainingFocus), Skill[]> TrainingMatrix = new()
    {
        [(DistanceCategory.Short, TrainingFocus.General)] = [Skill.Speed, Skill.Aerodynamics, Skill.Intelligence],
        [(DistanceCategory.Short, TrainingFocus.Conditional)] = [Skill.Speed],
        [(DistanceCategory.Short, TrainingFocus.Strategic)] = [Skill.Aerodynamics, Skill.Intelligence],

        [(DistanceCategory.Middle, TrainingFocus.General)] = [Skill.Speed, Skill.Stamina, Skill.Technique],
        [(DistanceCategory.Middle, TrainingFocus.Conditional)] = [Skill.Speed, Skill.Stamina],
        [(DistanceCategory.Middle, TrainingFocus.Strategic)] = [Skill.Technique],

        [(DistanceCategory.Long, TrainingFocus.General)] = [Skill.Stamina, Skill.Navigation, Skill.Intelligence],
        [(DistanceCategory.Long, TrainingFocus.Conditional)] = [Skill.Stamina],
        [(DistanceCategory.Long, TrainingFocus.Strategic)] = [Skill.Navigation, Skill.Intelligence],
    };

    private static readonly TrainingFocus[] AllFocuses =
        [TrainingFocus.General, TrainingFocus.Conditional, TrainingFocus.Strategic];

    public async Task<TrainingManagementPlan> BuildTrainingPlanAsync(
        int fancierId,
        CancellationToken cancellationToken = default)
    {
        var skipped = new List<string>();

        var pigeons = await ReadPigeonsAsync(fancierId, cancellationToken);
        if (pigeons.Count == 0)
        {
            skipped.Add("No pigeons in roster");
            return new TrainingManagementPlan(null,
                new TrainingRecommendation(TrainingFocus.General, 0, "no pigeons"),
                [], skipped);
        }

        var distanceWeights = await ComputeDistanceWeightsAsync(fancierId, cancellationToken);
        var skillDeficits = ComputeFlockSkillDeficits(pigeons);
        var skillAnalysis = new List<string>();

        foreach (var (skill, deficit) in skillDeficits.OrderByDescending(kv => kv.Value))
            skillAnalysis.Add($"{skill}: deficit={deficit:F2}");

        var scores = new Dictionary<TrainingFocus, (double Score, string Reason)>();

        foreach (var focus in AllFocuses)
        {
            var (score, reason) = ScoreFocus(focus, distanceWeights, skillDeficits);
            scores[focus] = (score, reason);
        }

        var best = scores.MaxBy(kv => kv.Value.Score);

        logger.LogInformation(
            "Training scores: General={General:F2}, Conditional={Conditional:F2}, Strategic={Strategic:F2} → recommend {Best}",
            scores[TrainingFocus.General].Score,
            scores[TrainingFocus.Conditional].Score,
            scores[TrainingFocus.Strategic].Score,
            best.Key);

        var recommendation = new TrainingRecommendation(
            best.Key,
            best.Value.Score,
            best.Value.Reason);

        var currentFocus = ParseTrainingType(sessionState.Current.SelectedFancier?.TrainingType);

        return new TrainingManagementPlan(currentFocus, recommendation, skillAnalysis, skipped);
    }

    public async Task ExecuteTrainingPlanAsync(
        TrainingManagementPlan plan,
        CancellationToken cancellationToken = default)
    {
        var focus = plan.Recommendation.RecommendedFocus;
        var apiName = focus switch
        {
            TrainingFocus.General => "general",
            TrainingFocus.Conditional => "conditional",
            TrainingFocus.Strategic => "strategic",
            _ => throw new ArgumentOutOfRangeException(),
        };

        var path = $"/api/fancier/trainingtype/{apiName}";

        logger.LogInformation("Setting training focus to {Focus} via PATCH {Path}", focus, path);

        var response = await writeTransport.PatchAsync(path, cancellationToken);

        if (response.StatusCode >= 200 && response.StatusCode < 300)
        {
            logger.LogInformation("Training focus set to {Focus}", focus);
        }
        else
        {
            logger.LogWarning("Training focus change failed — HTTP {Status}: {Body}",
                response.StatusCode, response.Body);
        }
    }

    private static (double Score, string Reason) ScoreFocus(
        TrainingFocus focus,
        Dictionary<DistanceCategory, double> distanceWeights,
        Dictionary<Skill, double> skillDeficits)
    {
        double totalScore = 0;
        var parts = new List<string>();

        foreach (var (distance, weight) in distanceWeights)
        {
            if (weight <= 0) continue;

            if (!TrainingMatrix.TryGetValue((distance, focus), out var trainedSkills))
                continue;

            double distanceScore = 0;
            foreach (var skill in trainedSkills)
            {
                var deficit = skillDeficits.GetValueOrDefault(skill, 0);
                distanceScore += deficit;
            }

            var weighted = distanceScore * weight;
            totalScore += weighted;

            if (weighted > 0)
                parts.Add($"{distance}({weight:F1}x)→[{string.Join(",", trainedSkills)}]={weighted:F1}");
        }

        return (totalScore, string.Join(" + ", parts));
    }

    private static Dictionary<Skill, double> ComputeFlockSkillDeficits(IReadOnlyList<PigeonDto> pigeons)
    {
        var totals = new Dictionary<Skill, double>();
        var counts = 0;

        foreach (var pigeon in pigeons)
        {
            var skills = pigeon.Skills;
            if (skills is null) continue;

            counts++;
            Accumulate(totals, Skill.Speed, skills.Speed);
            Accumulate(totals, Skill.Stamina, skills.Stamina);
            Accumulate(totals, Skill.Aerodynamics, skills.Aerodynamics);
            Accumulate(totals, Skill.Technique, skills.Technique);
            Accumulate(totals, Skill.Navigation, skills.Navigation);
            Accumulate(totals, Skill.Intelligence, skills.Intelligence);
        }

        if (counts == 0)
            return new Dictionary<Skill, double>();

        var averages = totals.ToDictionary(kv => kv.Key, kv => kv.Value / counts);
        var overallAvg = averages.Values.DefaultIfEmpty(0).Average();

        var deficits = new Dictionary<Skill, double>();
        foreach (var (skill, avg) in averages)
        {
            deficits[skill] = Math.Max(0, overallAvg - avg);
        }

        return deficits;
    }

    private static void Accumulate(Dictionary<Skill, double> totals, Skill skill, decimal? value)
    {
        totals.TryGetValue(skill, out var current);
        totals[skill] = current + (double)(value ?? 0);
    }

    private async Task<Dictionary<DistanceCategory, double>> ComputeDistanceWeightsAsync(
        int fancierId, CancellationToken ct)
    {
        var flights = await ReadUpcomingFlightsAsync(fancierId, ct);

        var counts = new Dictionary<DistanceCategory, int>
        {
            [DistanceCategory.Short] = 0,
            [DistanceCategory.Middle] = 0,
            [DistanceCategory.Long] = 0,
        };

        foreach (var flight in flights)
        {
            var category = DistanceProfileCalculator.Classify(flight.Distance);
            counts[category]++;
        }

        var total = counts.Values.Sum();

        if (total == 0)
        {
            logger.LogDebug("No upcoming flights — using equal distance weights");
            return new Dictionary<DistanceCategory, double>
            {
                [DistanceCategory.Short] = 1.0,
                [DistanceCategory.Middle] = 1.0,
                [DistanceCategory.Long] = 1.0,
            };
        }

        return counts.ToDictionary(kv => kv.Key, kv => (double)kv.Value / total);
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

    private static TrainingFocus? ParseTrainingType(string? trainingType) =>
        trainingType?.ToLowerInvariant() switch
        {
            "general" => TrainingFocus.General,
            "conditional" => TrainingFocus.Conditional,
            "strategic" => TrainingFocus.Strategic,
            _ => null,
        };

    private async Task<IReadOnlyList<FlightDto>> ReadUpcomingFlightsAsync(int fancierId, CancellationToken ct)
    {
        await using var db = await contextFactory.CreateDbContextAsync(ct);
        var snapshot = await db.RawApiSnapshots
            .AsNoTracking()
            .Where(x => x.SelectedFancierId == fancierId
                && x.Endpoint == "/api/flight"
                && x.StatusCode >= 200 && x.StatusCode < 300)
            .OrderByDescending(x => x.Id)
            .FirstOrDefaultAsync(ct);

        if (snapshot is null) return [];

        try
        {
            var flights = JsonSerializer.Deserialize<List<FlightDto>>(snapshot.ResponseBodyJson, JsonOptions) ?? [];
            return flights
                .Where(f => f.Start > DateTime.UtcNow)
                .ToList();
        }
        catch (JsonException)
        {
            return [];
        }
    }
}
