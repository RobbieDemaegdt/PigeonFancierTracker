using PigeonFancierTracker.Core.Contracts;

namespace PigeonFancierTracker.Core.Analytics;

public static class OffspringPerformanceCalculator
{
    public sealed record BreedingPairInput(
        string Parent1Name,
        int Parent1Id,
        decimal? Parent1TotalSkill,
        string Parent2Name,
        int Parent2Id,
        decimal? Parent2TotalSkill,
        int TotalOffspringCount,
        IReadOnlyList<decimal> OffspringTotalSkills);

    public static IReadOnlyList<OffspringPerformanceItem> Calculate(
        IReadOnlyList<BreedingPairInput> pairs)
    {
        return pairs
            .Select(p =>
            {
                var hasSkills = p.OffspringTotalSkills.Count > 0;
                var avgOffspring = hasSkills ? Math.Round(p.OffspringTotalSkills.Average(), 1) : (decimal?)null;
                decimal? parentAvg = p.Parent1TotalSkill.HasValue && p.Parent2TotalSkill.HasValue
                    ? Math.Round((p.Parent1TotalSkill.Value + p.Parent2TotalSkill.Value) / 2, 1)
                    : null;
                var delta = avgOffspring.HasValue && parentAvg.HasValue
                    ? Math.Round(avgOffspring.Value - parentAvg.Value, 1)
                    : 0m;
                var deltaDisplay = avgOffspring.HasValue && parentAvg.HasValue
                    ? delta switch
                    {
                        > 0 => $"+{delta:0.0} ↑",
                        < 0 => $"{delta:0.0} ↓",
                        _ => "0.0 =",
                    }
                    : "—";

                return new OffspringPerformanceItem(
                    p.Parent1Name,
                    p.Parent1Id,
                    p.Parent2Name,
                    p.Parent2Id,
                    p.TotalOffspringCount,
                    avgOffspring,
                    parentAvg,
                    delta,
                    deltaDisplay);
            })
            .OrderByDescending(x => x.SkillDelta)
            .ToList();
    }
}
