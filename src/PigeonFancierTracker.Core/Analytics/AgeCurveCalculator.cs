using PigeonFancierTracker.Core.Contracts;

namespace PigeonFancierTracker.Core.Analytics;

public static class AgeCurveCalculator
{
    public static AgeCurveResult Calculate(IReadOnlyList<AgeCurvePoint> points)
    {
        if (points.Count == 0)
            return new AgeCurveResult([], null, null, null, null);

        var buckets = points
            .GroupBy(p => p.AgeMonths)
            .Select(g =>
            {
                var percentiles = g.Where(p => p.AvgPercentile.HasValue).Select(p => p.AvgPercentile!.Value).ToList();
                return new AgeBucket(
                    g.Key,
                    Math.Round(g.Average(p => p.TotalSkill), 1),
                    percentiles.Count > 0 ? Math.Round(percentiles.Average(), 1) : null,
                    g.Count());
            })
            .OrderBy(b => b.AgeMonths)
            .ToList();

        if (buckets.Count == 0)
            return new AgeCurveResult([], null, null, null, null);

        var peakSkillBucket = buckets.OrderByDescending(b => b.AvgTotalSkill).First();
        var peakPerformanceBucket = buckets
            .Where(b => b.AvgPercentile.HasValue)
            .OrderBy(b => b.AvgPercentile)
            .FirstOrDefault();

        var phaseDisplay = BuildPhaseDisplay(buckets, peakSkillBucket.AgeMonths);

        return new AgeCurveResult(
            buckets,
            peakSkillBucket.AgeMonths,
            peakPerformanceBucket?.AgeMonths,
            peakSkillBucket.AvgTotalSkill,
            phaseDisplay);
    }

    private static string? BuildPhaseDisplay(IReadOnlyList<AgeBucket> buckets, int peakAge)
    {
        if (buckets.Count < 2)
            return null;

        var firstAge = buckets[0].AgeMonths;
        var lastAge = buckets[^1].AgeMonths;

        if (lastAge <= peakAge)
            return $"Groei ({firstAge}-{lastAge}m)";

        if (firstAge >= peakAge)
            return $"Daling ({firstAge}-{lastAge}m)";

        return $"Groei ({firstAge}-{peakAge}m) → Piek ({peakAge}m) → Daling ({peakAge}-{lastAge}m)";
    }
}
