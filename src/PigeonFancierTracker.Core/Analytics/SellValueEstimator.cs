using PigeonFancierTracker.Core.Contracts;

namespace PigeonFancierTracker.Core.Analytics;

public static class SellValueEstimator
{
    public static IReadOnlyList<SellValueEstimate> Estimate(
        IReadOnlyList<PigeonListItem> ownedPigeons,
        IReadOnlyList<CompletedTransferSummary> historicalSales,
        MarketTrendResult? marketTrend,
        AuctionTimingResult? auctionTiming)
    {
        if (ownedPigeons.Count == 0 || historicalSales.Count == 0)
            return [];

        var estimates = ownedPigeons
            .Select(p => EstimatePigeon(p, historicalSales, marketTrend, auctionTiming))
            .Where(e => e is not null)
            .Select(e => e!)
            .OrderByDescending(e => e.EstimatedSellPrice)
            .ToList();

        return ApplyClassifications(estimates);
    }

    private static SellValueEstimate? EstimatePigeon(
        PigeonListItem pigeon,
        IReadOnlyList<CompletedTransferSummary> historicalSales,
        MarketTrendResult? marketTrend,
        AuctionTimingResult? auctionTiming)
    {
        var ageMonths = ParseAgeToMonths(pigeon.Age);
        var request = BuildPriceRequest(pigeon);

        var priceWindows = MultiWindowPriceEstimator.Estimate(request, ageMonths, historicalSales);

        var bestWindow = priceWindows
            .Where(w => w.EstimatedPrice.HasValue)
            .OrderByDescending(w => w.ComparableCount)
            .FirstOrDefault();

        if (bestWindow?.EstimatedPrice is not decimal basePrice)
            return null;

        var trendAdjustedPrice = basePrice;
        string? trendImpact = null;

        if (marketTrend is { TrendPercentage: not 0 })
        {
            trendAdjustedPrice = Math.Round(basePrice * (1 + marketTrend.TrendPercentage / 100), 0);
            trendImpact = marketTrend.TrendPercentage switch
            {
                > 0 => $"Stijgende markt: +{marketTrend.TrendPercentage:0.0}%",
                < 0 => $"Dalende markt: {marketTrend.TrendPercentage:0.0}%",
                _ => "Stabiel",
            };
        }

        var optimalTiming = auctionTiming?.BestTimeSlot;

        return new SellValueEstimate(
            pigeon.SourceId,
            pigeon.DisplayName,
            pigeon.Sex,
            pigeon.Age,
            pigeon.Breed,
            pigeon.TotalSkill,
            trendAdjustedPrice,
            bestWindow.MinPrice,
            bestWindow.MaxPrice,
            optimalTiming,
            trendImpact,
            pigeon.TotalPoints,
            pigeon.RaceCount,
            pigeon.EarningsDisplay,
            SellValueClassification.Gemiddeld,
            "Gemiddeld",
            bestWindow.Confidence ?? PriceConfidence.Low);
    }

    private static IReadOnlyList<SellValueEstimate> ApplyClassifications(
        List<SellValueEstimate> sortedEstimates)
    {
        if (sortedEstimates.Count == 0)
            return sortedEstimates;

        var highThreshold = Math.Max(1, (int)Math.Ceiling(sortedEstimates.Count * 0.25));
        var lowStart = sortedEstimates.Count - Math.Max(1, (int)Math.Ceiling(sortedEstimates.Count * 0.25));

        for (var i = 0; i < sortedEstimates.Count; i++)
        {
            var (classification, display) = i < highThreshold
                ? (SellValueClassification.HogeWaarde, "Hoge waarde")
                : i >= lowStart
                    ? (SellValueClassification.LageWaarde, "Lage waarde")
                    : (SellValueClassification.Gemiddeld, "Gemiddeld");

            sortedEstimates[i] = sortedEstimates[i] with
            {
                Classification = classification,
                ClassificationDisplay = display,
            };
        }

        return sortedEstimates;
    }

    private static PriceEstimationRequest BuildPriceRequest(PigeonListItem pigeon)
    {
        return new PriceEstimationRequest(
            pigeon.TotalSkill,
            ParseAgeToMonths(pigeon.Age),
            pigeon.Breed,
            ParseSkill(pigeon.FormDisplay),
            ParseSkill(pigeon.ExperienceDisplay),
            ParseSkill(pigeon.SpeedDisplay),
            ParseSkill(pigeon.TechniqueDisplay),
            ParseSkill(pigeon.StaminaDisplay),
            ParseSkill(pigeon.AerodynamicsDisplay),
            ParseSkill(pigeon.IntelligenceDisplay),
            ParseSkill(pigeon.LibidoDisplay),
            ParseSkill(pigeon.NightvisionDisplay),
            ParseSkill(pigeon.NavigationDisplay));
    }

    private static decimal? ParseSkill(string? display) =>
        decimal.TryParse(display, out var v) ? v : null;

    private static int? ParseAgeToMonths(string? age)
    {
        if (string.IsNullOrWhiteSpace(age))
            return null;

        var yearMatch = System.Text.RegularExpressions.Regex.Match(age, @"(\d+)\s*y");
        var monthMatch = System.Text.RegularExpressions.Regex.Match(age, @"(\d+)\s*m");

        if (!yearMatch.Success && !monthMatch.Success)
            return null;

        int months = 0;
        if (yearMatch.Success)
            months += int.Parse(yearMatch.Groups[1].Value) * 12;
        if (monthMatch.Success)
            months += int.Parse(monthMatch.Groups[1].Value);
        return months;
    }
}
