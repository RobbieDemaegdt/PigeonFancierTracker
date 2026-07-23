using PigeonFancierTracker.Core.Contracts;

namespace PigeonFancierTracker.Core.Analytics;

public static class PriceEstimator
{
    private const int TopComparableCount = 5;
    private const int HighConfidenceThreshold = 10;
    private const int MediumConfidenceThreshold = 3;

    public static PriceEstimationResult? Estimate(
        PriceEstimationRequest target,
        IReadOnlyList<CompletedTransferSummary> historicalSales)
    {
        ArgumentNullException.ThrowIfNull(target);
        ArgumentNullException.ThrowIfNull(historicalSales);

        if (historicalSales.Count == 0)
            return null;

        var scored = historicalSales
            .Select(sale => (Sale: sale, Similarity: ComputeSimilarity(target, sale)))
            .Where(x => x.Similarity > 0)
            .OrderByDescending(x => x.Similarity)
            .ToList();

        if (scored.Count == 0)
            return null;

        var weightedSum = scored.Sum(x => x.Similarity * (double)x.Sale.SoldPrice);
        var totalWeight = scored.Sum(x => x.Similarity);
        var estimatedPrice = (decimal)(weightedSum / totalWeight);

        var topComparables = scored.Take(TopComparableCount).Select(x => x.Sale.SoldPrice).ToList();
        var minPrice = topComparables.Min();
        var maxPrice = topComparables.Max();

        var confidence = scored.Count >= HighConfidenceThreshold
            ? PriceConfidence.High
            : scored.Count >= MediumConfidenceThreshold
                ? PriceConfidence.Medium
                : PriceConfidence.Low;

        return new PriceEstimationResult(
            Math.Round(estimatedPrice, 0),
            minPrice,
            maxPrice,
            scored.Count,
            confidence);
    }

    internal static double ComputeSimilarity(
        PriceEstimationRequest target,
        CompletedTransferSummary comparable)
    {
        double similarity = 0;
        int components = 0;

        // Per-skill similarity (all 10 skills, weighted equally)
        var skillPairs = new (decimal? Target, decimal? Comp)[]
        {
            (target.Form, comparable.Form),
            (target.Experience, comparable.Experience),
            (target.Speed, comparable.Speed),
            (target.Technique, comparable.Technique),
            (target.Stamina, comparable.Stamina),
            (target.Aerodynamics, comparable.Aerodynamics),
            (target.Intelligence, comparable.Intelligence),
            (target.Libido, comparable.Libido),
            (target.Nightvision, comparable.Nightvision),
            (target.Navigation, comparable.Navigation),
        };

        int skillsCompared = 0;
        double skillSimilaritySum = 0;

        foreach (var (t, c) in skillPairs)
        {
            if (t.HasValue && c.HasValue)
            {
                // Each skill ranges roughly 1–10 (one-based), so max diff ~9
                var diff = (double)Math.Abs(t.Value - c.Value);
                skillSimilaritySum += 1.0 / (1.0 + diff);
                skillsCompared++;
            }
        }

        if (skillsCompared > 0)
        {
            // Average per-skill similarity, weighted at 60% of total
            similarity += (skillSimilaritySum / skillsCompared) * 0.6;
            components++;
        }

        // TotalSkill similarity (weighted at 25%)
        if (target.TotalSkill.HasValue && comparable.TotalSkill.HasValue)
        {
            var totalDiff = (double)Math.Abs(target.TotalSkill.Value - comparable.TotalSkill.Value);
            similarity += (1.0 / (1.0 + totalDiff)) * 0.25;
            components++;
        }

        // Age similarity (weighted at 15%)
        if (target.AgeMonths.HasValue && comparable.AgeMonths.HasValue)
        {
            var ageDiff = Math.Abs(target.AgeMonths.Value - comparable.AgeMonths.Value);
            similarity += (1.0 / (1.0 + ageDiff)) * 0.15;
            components++;
        }

        // If we couldn't compare anything meaningful, fall back to a tiny baseline
        // so the sale still contributes (but weakly)
        if (components == 0)
            return 0.01;

        return similarity;
    }
}
