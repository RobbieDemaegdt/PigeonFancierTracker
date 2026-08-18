using PigeonFancierTracker.Core.Contracts;

namespace PigeonFancierTracker.Core.Analytics;

public static class SponsorDealScorer
{
    public static SponsorDealScore ScoreCurrentDeal(
        decimal monthly,
        decimal direct,
        int runtime,
        IReadOnlyList<SponsorHistoryEntry> historicalDeals)
    {
        if (historicalDeals.Count == 0)
            return new SponsorDealScore(50, ClassifyDeal(50), BuildRecommendation(50));

        var effectiveMonthly = CalculateEffectiveMonthly(monthly, direct, runtime);
        var historicalEffective = historicalDeals
            .Select(h => CalculateEffectiveMonthly(h.Monthly, h.Direct, h.Runtime))
            .ToList();
        var effectivePercentile = CalculatePercentile(effectiveMonthly, historicalEffective);

        var monthlyPercentile = CalculatePercentile(monthly, historicalDeals.Select(h => h.Monthly).ToList());
        var directPercentile = CalculatePercentile(direct, historicalDeals.Select(h => h.Direct).ToList());

        var score = Math.Round(
            effectivePercentile * 0.60 +
            monthlyPercentile * 0.25 +
            directPercentile * 0.15, 1);

        var verdict = ClassifyDeal(score);
        var recommendation = BuildRecommendation(score);

        return new SponsorDealScore(score, verdict, recommendation);
    }

    public static decimal CalculateEffectiveMonthly(decimal monthly, decimal direct, int runtime) =>
        runtime > 0 ? direct / runtime + monthly : monthly;

    public static double CalculatePercentile(decimal value, IReadOnlyList<decimal> population)
    {
        if (population.Count == 0)
            return 50;

        var belowCount = population.Count(v => v < value);
        var equalCount = population.Count(v => v == value);

        return (belowCount + equalCount * 0.5) / population.Count * 100;
    }

    public static (double TrendPercent, string Direction) CalculateOfferTrend(
        IReadOnlyList<SponsorHistoryEntry> historicalDeals)
    {
        if (historicalDeals.Count < 2)
            return (0, "Stabiel");

        var ordered = historicalDeals.OrderBy(h => h.CapturedAt).ToList();
        var midpoint = ordered.Count / 2;
        var olderAvg = ordered.Take(midpoint).Average(h => h.Monthly);
        var newerAvg = ordered.Skip(midpoint).Average(h => h.Monthly);

        if (olderAvg == 0)
            return (0, "Stabiel");

        var changePercent = Math.Round((double)((newerAvg - olderAvg) / olderAvg * 100), 1);

        var direction = changePercent switch
        {
            > 5 => "Stijgend",
            < -5 => "Dalend",
            _ => "Stabiel"
        };

        return (changePercent, direction);
    }

    public static decimal CalculateTotalContractValue(decimal monthly, decimal direct, int runtime) =>
        monthly * runtime + direct;

    public static string ClassifyDeal(double score) => score switch
    {
        >= 80 => "Uitstekend",
        >= 60 => "Goed",
        >= 40 => "Gemiddeld",
        _ => "Ondermaats"
    };

    private static string BuildRecommendation(double score) => score switch
    {
        >= 80 => "Zeker accepteren — uitstekende deal",
        >= 60 => "Accepteren — goede deal",
        >= 40 => "Afwachten — gemiddeld aanbod",
        _ => "Afwijzen — onder het gemiddelde"
    };
}
