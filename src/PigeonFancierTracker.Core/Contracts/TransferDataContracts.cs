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
    decimal? EstimatedPrice,
    string? EstimatedPriceDisplay,
    string? PriceDeltaDisplay);

public sealed record TransferPageData(
    IReadOnlyList<TransferListItem> ActiveTransfers,
    IReadOnlyList<TransferListItem> CompletedTransfers);

public interface ITransferDataReader
{
    Task<TransferPageData> GetTransferDataAsync(
        int selectedFancierId,
        CancellationToken cancellationToken = default);
}
