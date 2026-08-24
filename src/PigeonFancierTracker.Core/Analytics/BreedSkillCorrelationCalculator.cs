using PigeonFancierTracker.Core.Contracts;

namespace PigeonFancierTracker.Core.Analytics;

/// <summary>
/// Analysis B: for each breed, how strongly each skill tracks with better placings.
/// For every result row it pairs the pigeon's skill value with a "quality" score
/// (100 − percentile, so higher is better) and computes a Pearson correlation across
/// the breed's rows. A positive coefficient means higher skill goes with better
/// results — a hint at which skill is worth training for that breed.
/// </summary>
public static class BreedSkillCorrelationCalculator
{
    /// <summary>Distinct pigeons a breed needs before its correlations are treated as reliable.</summary>
    public const int MinReliablePigeons = 3;

    private static readonly (string Name, string Display, Func<PigeonSkillsDto, decimal?> Selector)[] Skills =
    [
        ("form", "Vorm", s => s.Form),
        ("experience", "Ervaring", s => s.Experience),
        ("speed", "Snelheid", s => s.Speed),
        ("technique", "Techniek", s => s.Technique),
        ("stamina", "Uithouding", s => s.Stamina),
        ("aerodynamics", "Aerodynamica", s => s.Aerodynamics),
        ("intelligence", "Intelligentie", s => s.Intelligence),
        ("libido", "Libido", s => s.Libido),
        ("nightvision", "Nachtzicht", s => s.Nightvision),
        ("navigation", "Navigatie", s => s.Navigation),
    ];

    public static BreedSkillAnalysisData Calculate(
        IReadOnlyList<FlightResultListItem> results,
        IReadOnlyDictionary<int, PigeonSkillsDto> skillsByPigeon)
    {
        var samples = results
            .Where(r => !string.IsNullOrWhiteSpace(r.Breed) && skillsByPigeon.ContainsKey(r.PigeonId))
            .Select(r => (Breed: r.Breed!, r.PigeonId, Quality: 100 - r.Percentile, Skills: skillsByPigeon[r.PigeonId]))
            .ToList();

        if (samples.Count == 0)
            return new BreedSkillAnalysisData([]);

        var breeds = samples
            .GroupBy(s => s.Breed, StringComparer.OrdinalIgnoreCase)
            .Select(BuildProfile)
            .OrderByDescending(p => p.SkillCorrelations.FirstOrDefault(c => c.IsReliable)?.Correlation ?? double.MinValue)
            .ToList();

        return new BreedSkillAnalysisData(breeds);
    }

    private static BreedSkillProfile BuildProfile(
        IGrouping<string, (string Breed, int PigeonId, double Quality, PigeonSkillsDto Skills)> group)
    {
        var rows = group.ToList();
        var distinctPigeons = rows.Select(r => r.PigeonId).Distinct().Count();
        var reliable = distinctPigeons >= MinReliablePigeons;

        var avgTotalSkill = rows
            .GroupBy(r => r.PigeonId)
            .Select(g => g.First().Skills.Total ?? 0m)
            .DefaultIfEmpty(0m)
            .Average();

        var correlations = Skills
            .Select(skill =>
            {
                var pairs = rows
                    .Select(r => (X: skill.Selector(r.Skills), r.Quality))
                    .Where(p => p.X is not null)
                    .Select(p => ((double)p.X!.Value, p.Quality))
                    .ToList();

                var correlation = Pearson(pairs);
                var avgValue = pairs.Count > 0 ? Math.Round(pairs.Average(p => p.Item1), 1) : 0;

                return new BreedSkillCorrelation(
                    skill.Name,
                    skill.Display,
                    Math.Round(correlation, 2),
                    avgValue,
                    pairs.Count,
                    reliable && pairs.Count >= MinReliablePigeons);
            })
            .OrderByDescending(c => c.Correlation)
            .ToList();

        var top = correlations.FirstOrDefault(c => c.IsReliable);

        return new BreedSkillProfile(
            group.Key,
            distinctPigeons,
            rows.Count,
            Math.Round(avgTotalSkill, 1),
            top?.SkillDisplay,
            correlations);
    }

    /// <summary>Pearson correlation coefficient; 0 when undefined (fewer than two points or no variance).</summary>
    private static double Pearson(IReadOnlyList<(double X, double Y)> pairs)
    {
        if (pairs.Count < 2)
            return 0;

        var meanX = pairs.Average(p => p.X);
        var meanY = pairs.Average(p => p.Y);

        double covariance = 0, varX = 0, varY = 0;
        foreach (var (x, y) in pairs)
        {
            var dx = x - meanX;
            var dy = y - meanY;
            covariance += dx * dy;
            varX += dx * dx;
            varY += dy * dy;
        }

        if (varX <= 0 || varY <= 0)
            return 0;

        return covariance / Math.Sqrt(varX * varY);
    }
}
