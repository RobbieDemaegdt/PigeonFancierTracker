using PigeonFancierTracker.Core.Analytics;
using PigeonFancierTracker.Core.Contracts;

namespace PigeonFancierTracker.Infrastructure.Persistence;

public sealed class MarketAnalysisReader(
    ITransferDataReader transferDataReader,
    ITrackerDataReader trackerDataReader) : IMarketAnalysisReader
{
    public async Task<MarketAnalysisPageData> GetMarketAnalysisAsync(
        int selectedFancierId,
        TransferPageData? preComputed = null,
        CancellationToken cancellationToken = default)
    {
        var transferData = preComputed
            ?? await transferDataReader.GetTransferDataAsync(selectedFancierId, cancellationToken);

        var historicalSales = BuildHistoricalSales(transferData.CompletedTransfers);

        var buyRecommendations = TransferMarketAnalyzer.Analyze(
            transferData.ActiveTransfers,
            historicalSales,
            transferData.MarketTrend);

        var dashboard = await trackerDataReader.GetDashboardAsync(selectedFancierId, cancellationToken);

        var sellEstimates = SellValueEstimator.Estimate(
            dashboard.Pigeons,
            historicalSales,
            transferData.MarketTrend,
            transferData.AuctionTiming);

        var koopjeCount = buyRecommendations.Count(r =>
            r.Classification is MarketValueClassification.Koopje or MarketValueClassification.Uitzonderlijk);
        var teDuurCount = buyRecommendations.Count(r =>
            r.Classification == MarketValueClassification.TeDuur);
        var avgSkillPerEuro = buyRecommendations.Count > 0
            ? Math.Round(buyRecommendations.Average(r => r.SkillPerEuro), 2)
            : 0m;
        var topBargain = buyRecommendations
            .Where(r => r.Classification is MarketValueClassification.Koopje or MarketValueClassification.Uitzonderlijk)
            .MinBy(r => r.ValueRatio);

        return new MarketAnalysisPageData(
            buyRecommendations,
            sellEstimates,
            transferData.MarketTrend,
            transferData.AuctionTiming,
            koopjeCount,
            teDuurCount,
            avgSkillPerEuro,
            topBargain?.PigeonName);
    }

    private static IReadOnlyList<CompletedTransferSummary> BuildHistoricalSales(
        IReadOnlyList<TransferListItem> completedTransfers)
    {
        return completedTransfers
            .Where(t => t.Status == TransferStatus.Sold && t.SoldPrice.HasValue)
            .Select(t => new CompletedTransferSummary(
                t.SoldPrice!.Value,
                t.TotalSkill,
                ParseAgeToMonths(t.Age),
                t.Breed,
                t.BidCount,
                t.PigeonName,
                t.TransferId,
                ParseSkill(t.FormDisplay),
                ParseSkill(t.ExperienceDisplay),
                ParseSkill(t.SpeedDisplay),
                ParseSkill(t.TechniqueDisplay),
                ParseSkill(t.StaminaDisplay),
                ParseSkill(t.AerodynamicsDisplay),
                ParseSkill(t.IntelligenceDisplay),
                ParseSkill(t.LibidoDisplay),
                ParseSkill(t.NightvisionDisplay),
                ParseSkill(t.NavigationDisplay)))
            .ToList();
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
