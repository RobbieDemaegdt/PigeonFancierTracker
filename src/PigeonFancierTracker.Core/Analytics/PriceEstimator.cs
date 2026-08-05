using PigeonFancierTracker.Core.Contracts;

namespace PigeonFancierTracker.Core.Analytics;

public static class PriceEstimator
{
    private const int TopComparableCount = 5;
    private const int MaxReturnedComparables = 10;
    private const int HighConfidenceThreshold = 10;
    private const int MediumConfidenceThreshold = 3;
    private const double MonthlyDepreciationRate = 0.01;
    private const double MinAgeAdjustmentFactor = 0.5;
    private const double MaxAgeAdjustmentFactor = 1.5;
    private const double SkillWeight = 0.60;
    private const double TotalSkillWeight = 0.15;
    private const double AgeWeight = 0.15;
    private const double BreedMatchBonus = 0.10;
    private const double YoungTargetPenaltyRate = 0.08;

    public static PriceEstimationResult? Estimate(
        PriceEstimationRequest target,
        IReadOnlyList<CompletedTransferSummary> historicalSales)
    {
        ArgumentNullException.ThrowIfNull(target);
        ArgumentNullException.ThrowIfNull(historicalSales);

        if (historicalSales.Count == 0)
            return null;

        var scored = historicalSales
            .Select(sale => (
                Sale: sale,
                Similarity: ComputeSimilarity(target, sale),
                AgeFactor: ComputeAgeAdjustmentFactor(target.AgeMonths, sale.AgeMonths)))
            .Where(x => x.Similarity > 0)
            .OrderByDescending(x => x.Similarity)
            .ToList();

        if (scored.Count == 0)
            return null;

        var weightedSum = scored.Sum(x => x.Similarity * (double)x.Sale.SoldPrice * x.AgeFactor);
        var totalWeight = scored.Sum(x => x.Similarity);
        var estimatedPrice = (decimal)(weightedSum / totalWeight);

        var topComparables = scored.Take(TopComparableCount)
            .Select(x => (decimal)(((double)x.Sale.SoldPrice) * x.AgeFactor))
            .ToList();
        var minPrice = topComparables.Min();
        var maxPrice = topComparables.Max();

        var confidence = scored.Count >= HighConfidenceThreshold
            ? PriceConfidence.High
            : scored.Count >= MediumConfidenceThreshold
                ? PriceConfidence.Medium
                : PriceConfidence.Low;

        var comparables = scored.Take(MaxReturnedComparables)
            .Select(x => new ComparablePigeon(
                x.Sale.PigeonName ?? "—",
                x.Sale.TransferId,
                x.Sale.SoldPrice,
                (decimal)((double)x.Sale.SoldPrice * x.AgeFactor),
                x.Similarity,
                x.Sale.Breed,
                x.Sale.AgeMonths,
                x.Sale.TotalSkill))
            .ToList();

        return new PriceEstimationResult(
            Math.Round(estimatedPrice, 0),
            minPrice,
            maxPrice,
            scored.Count,
            confidence,
            comparables);
    }

    internal static double ComputeSimilarity(
        PriceEstimationRequest target,
        CompletedTransferSummary comparable)
    {
        double similarity = 0;
        int components = 0;

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
                var diff = (double)Math.Abs(t.Value - c.Value);
                skillSimilaritySum += 1.0 / (1.0 + diff);
                skillsCompared++;
            }
        }

        if (skillsCompared > 0)
        {
            similarity += (skillSimilaritySum / skillsCompared) * SkillWeight;
            components++;
        }

        if (target.TotalSkill.HasValue && comparable.TotalSkill.HasValue)
        {
            var totalDiff = (double)Math.Abs(target.TotalSkill.Value - comparable.TotalSkill.Value);
            similarity += (1.0 / (1.0 + totalDiff)) * TotalSkillWeight;
            components++;
        }

        if (target.AgeMonths.HasValue && comparable.AgeMonths.HasValue)
        {
            var ageDiff = Math.Abs(target.AgeMonths.Value - comparable.AgeMonths.Value);
            var ageSim = 1.0 / (1.0 + ageDiff);

            if (target.AgeMonths.Value < comparable.AgeMonths.Value)
            {
                var monthsYounger = comparable.AgeMonths.Value - target.AgeMonths.Value;
                ageSim *= 1.0 / (1.0 + monthsYounger * YoungTargetPenaltyRate);
            }

            similarity += ageSim * AgeWeight;
            components++;
        }

        if (!string.IsNullOrEmpty(target.Breed) && !string.IsNullOrEmpty(comparable.Breed))
        {
            if (string.Equals(target.Breed, comparable.Breed, StringComparison.OrdinalIgnoreCase))
                similarity += BreedMatchBonus;
            components++;
        }

        if (components == 0)
            return 0.01;

        return similarity;
    }

    internal static double ComputeAgeAdjustmentFactor(int? targetAgeMonths, int? comparableAgeMonths)
    {
        if (!targetAgeMonths.HasValue || !comparableAgeMonths.HasValue)
            return 1.0;

        var ageDelta = comparableAgeMonths.Value - targetAgeMonths.Value;
        var rawFactor = 1.0 + ageDelta * MonthlyDepreciationRate;
        return Math.Clamp(rawFactor, MinAgeAdjustmentFactor, MaxAgeAdjustmentFactor);
    }
}
