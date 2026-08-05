namespace PigeonFancierTracker.Core.Contracts;

public enum TransferStatus
{
    Active,
    Sold,
    Expired,
}

public sealed record TransferListItem(
    int TransferId,
    int? PigeonId,
    string PigeonName,
    string? Sex,
    string? Age,
    string? Breed,
    decimal? StartPrice,
    decimal? CurrentPrice,
    string? Seller,
    string? Buyer,
    int BidCount,
    string TimeRemaining,
    DateTimeOffset? Start,
    DateTimeOffset? End,
    decimal? TotalSkill,
    string? TotalSkillDisplay,
    string? FormDisplay,
    string? ExperienceDisplay,
    string? SpeedDisplay,
    string? TechniqueDisplay,
    string? StaminaDisplay,
    string? AerodynamicsDisplay,
    string? IntelligenceDisplay,
    string? LibidoDisplay,
    string? NightvisionDisplay,
    string? NavigationDisplay,
    string? ShortDisplay,
    string? MediumDisplay,
    string? LongDisplay,
    TransferStatus Status,
    decimal? SoldPrice,
    string? SoldTo,
    IReadOnlyList<WindowPriceRow>? PriceEstimates = null,
    IReadOnlyList<WindowPercentileRow>? MarketPercentiles = null,
    IReadOnlyList<WindowPercentileRow>? FlockPercentiles = null)
{
    public string? BestEstimateDisplay
    {
        get
        {
            var best = PriceEstimates?
                .Where(r => r.EstimatedPrice.HasValue)
                .OrderByDescending(r => r.ComparableCount)
                .FirstOrDefault();
            if (best is null) return null;
            return $"€{best.EstimatedPrice:N0} ({best.WindowLabel})";
        }
    }

    public string? PriceDeltaDisplay
    {
        get
        {
            if (Status != TransferStatus.Sold || !SoldPrice.HasValue) return null;
            var best = PriceEstimates?
                .Where(r => r.EstimatedPrice.HasValue)
                .OrderByDescending(r => r.ComparableCount)
                .FirstOrDefault();
            if (best?.EstimatedPrice is not decimal estimated) return null;
            var delta = SoldPrice.Value - estimated;
            return delta switch
            {
                > 0 => $"+€{delta:N0} ↑",
                < 0 => $"−€{Math.Abs(delta):N0} ↓",
                _ => "€0",
            };
        }
    }
}

public sealed record TransferPageData(
    IReadOnlyList<TransferListItem> ActiveTransfers,
    IReadOnlyList<TransferListItem> CompletedTransfers,
    MarketTrendResult? MarketTrend = null,
    AuctionTimingResult? AuctionTiming = null);

public interface ITransferDataReader
{
    Task<TransferPageData> GetTransferDataAsync(
        int selectedFancierId,
        CancellationToken cancellationToken = default);

    Task<bool> RecheckTransferStatusAsync(
        int selectedFancierId,
        int transferId,
        CancellationToken cancellationToken = default);

    Task UpdateTransferStatusAsync(
        int selectedFancierId,
        int transferId,
        TransferStatus newStatus,
        CancellationToken cancellationToken = default);

    Task UpdateTransferBuyerAsync(
        int selectedFancierId,
        int transferId,
        string? buyer,
        CancellationToken cancellationToken = default);

    Task UpdateTransferSoldPriceAsync(
        int selectedFancierId,
        int transferId,
        decimal? soldPrice,
        CancellationToken cancellationToken = default);
}
