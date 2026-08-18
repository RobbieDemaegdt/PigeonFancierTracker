using PigeonFancierTracker.Core.Contracts;

namespace PigeonFancierTracker.Core.Analytics;

public static class TransferMarketAnalyzer
{
    private const decimal BargainRatioThreshold = 0.65m;
    private const decimal OverpricedRatioThreshold = 1.35m;
    private const decimal SkillSpecialistThreshold = 5.0m;
    private const int GrowthPhaseCeiling = 18;
    private const int PeakPhaseCeiling = 36;
    private const decimal GrowthPhaseMultiplier = 1.15m;
    private const decimal PeakPhaseMultiplier = 1.0m;
    private const decimal DeclinePhaseMultiplier = 0.80m;

    public static IReadOnlyList<BuyRecommendation> Analyze(
        IReadOnlyList<TransferListItem> activeTransfers,
        IReadOnlyList<CompletedTransferSummary> historicalSales,
        MarketTrendResult? marketTrend)
    {
        if (activeTransfers.Count == 0 || historicalSales.Count == 0)
            return [];

        return activeTransfers
            .Select(t => ScoreTransfer(t, historicalSales, marketTrend))
            .Where(r => r is not null)
            .Select(r => r!)
            .OrderBy(r => r.ValueRatio)
            .ToList();
    }

    private static BuyRecommendation? ScoreTransfer(
        TransferListItem transfer,
        IReadOnlyList<CompletedTransferSummary> historicalSales,
        MarketTrendResult? marketTrend)
    {
        var askingPrice = transfer.CurrentPrice ?? transfer.StartPrice;
        if (askingPrice is not > 0)
            return null;

        var ageMonths = ParseAgeToMonths(transfer.Age);
        var request = BuildPriceRequest(transfer);
        var estimation = PriceEstimator.Estimate(request, historicalSales);

        if (estimation is null)
            return null;

        var estimatedValue = estimation.EstimatedPrice;
        if (estimatedValue <= 0)
            return null;

        var valueRatio = Math.Round(askingPrice.Value / estimatedValue, 2);
        var skillPerEuro = transfer.TotalSkill.HasValue && askingPrice > 0
            ? Math.Round(transfer.TotalSkill.Value / askingPrice.Value, 2)
            : 0m;

        var (agePhase, phaseMultiplier) = DetermineAgePhase(ageMonths);
        var ageAdjustedValue = Math.Round(estimatedValue * phaseMultiplier, 0);

        var skillProfile = AnalyzeSkillProfile(transfer);

        var (classification, classificationDisplay) = Classify(valueRatio, skillProfile);

        var (isOutlier, outlierReason) = DetectOutlier(
            valueRatio, skillProfile, estimation.Confidence);

        return new BuyRecommendation(
            transfer.TransferId,
            transfer.PigeonName,
            transfer.Sex,
            transfer.Age,
            transfer.Breed,
            askingPrice.Value,
            estimatedValue,
            valueRatio,
            skillPerEuro,
            agePhase,
            ageAdjustedValue,
            classification,
            classificationDisplay,
            isOutlier,
            outlierReason,
            transfer.TotalSkill,
            skillProfile,
            estimation.Confidence,
            transfer.TimeRemaining);
    }

    private static (string Phase, decimal Multiplier) DetermineAgePhase(int? ageMonths)
    {
        if (!ageMonths.HasValue)
            return ("Onbekend", PeakPhaseMultiplier);

        return ageMonths.Value switch
        {
            < GrowthPhaseCeiling => ("Groei", GrowthPhaseMultiplier),
            <= PeakPhaseCeiling => ("Piek", PeakPhaseMultiplier),
            _ => ("Daling", DeclinePhaseMultiplier),
        };
    }

    private static SkillProfileSummary? AnalyzeSkillProfile(TransferListItem transfer)
    {
        var skills = new (string Name, decimal? Value)[]
        {
            ("Vorm", ParseSkill(transfer.FormDisplay)),
            ("Ervaring", ParseSkill(transfer.ExperienceDisplay)),
            ("Snelheid", ParseSkill(transfer.SpeedDisplay)),
            ("Techniek", ParseSkill(transfer.TechniqueDisplay)),
            ("Uithoudingsvermogen", ParseSkill(transfer.StaminaDisplay)),
            ("Aerodynamica", ParseSkill(transfer.AerodynamicsDisplay)),
            ("Intelligentie", ParseSkill(transfer.IntelligenceDisplay)),
            ("Libido", ParseSkill(transfer.LibidoDisplay)),
            ("Nachtzicht", ParseSkill(transfer.NightvisionDisplay)),
            ("Navigatie", ParseSkill(transfer.NavigationDisplay)),
        };

        var validSkills = skills.Where(s => s.Value.HasValue).ToList();
        if (validSkills.Count < 2)
            return null;

        var highest = validSkills.MaxBy(s => s.Value!.Value);
        var lowest = validSkills.MinBy(s => s.Value!.Value);
        var range = highest.Value!.Value - lowest.Value!.Value;
        var isSpecialist = range >= SkillSpecialistThreshold;

        return new SkillProfileSummary(
            highest.Name, highest.Value!.Value,
            lowest.Name, lowest.Value!.Value,
            range, isSpecialist);
    }

    private static (MarketValueClassification Classification, string Display) Classify(
        decimal valueRatio,
        SkillProfileSummary? skillProfile)
    {
        if (valueRatio <= BargainRatioThreshold && skillProfile?.IsSpecialist == true)
            return (MarketValueClassification.Uitzonderlijk, "Uitzonderlijk");
        if (valueRatio <= BargainRatioThreshold)
            return (MarketValueClassification.Koopje, "Koopje");
        if (valueRatio > OverpricedRatioThreshold)
            return (MarketValueClassification.TeDuur, "Te duur");
        return (MarketValueClassification.EerlijkePrijs, "Eerlijke prijs");
    }

    private static (bool IsOutlier, string? Reason) DetectOutlier(
        decimal valueRatio,
        SkillProfileSummary? skillProfile,
        PriceConfidence confidence)
    {
        if (confidence == PriceConfidence.Low)
            return (false, null);

        if (valueRatio < 0.4m)
            return (true, "Sterk ondergeprijsd");
        if (valueRatio > 2.0m)
            return (true, "Sterk overgeprijsd");
        if (skillProfile is { IsSpecialist: true } sp && valueRatio <= BargainRatioThreshold)
            return (true, $"Specialist: zeer hoge {sp.HighestSkill.ToLowerInvariant()}");

        return (false, null);
    }

    private static PriceEstimationRequest BuildPriceRequest(TransferListItem transfer)
    {
        return new PriceEstimationRequest(
            transfer.TotalSkill,
            ParseAgeToMonths(transfer.Age),
            transfer.Breed,
            ParseSkill(transfer.FormDisplay),
            ParseSkill(transfer.ExperienceDisplay),
            ParseSkill(transfer.SpeedDisplay),
            ParseSkill(transfer.TechniqueDisplay),
            ParseSkill(transfer.StaminaDisplay),
            ParseSkill(transfer.AerodynamicsDisplay),
            ParseSkill(transfer.IntelligenceDisplay),
            ParseSkill(transfer.LibidoDisplay),
            ParseSkill(transfer.NightvisionDisplay),
            ParseSkill(transfer.NavigationDisplay));
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
