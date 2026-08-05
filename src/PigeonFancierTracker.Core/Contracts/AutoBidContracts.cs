namespace PigeonFancierTracker.Core.Contracts;

public enum AutoBidStatus
{
    Watching,
    Winning,
    Bidding,
    MaxReached,
    Won,
    Expired,
    Error,
    Stopped,
}

public sealed class AutoBidEntry
{
    public int TransferId { get; set; }
    public string PigeonName { get; set; } = "";
    public decimal MaxPrice { get; set; }
    public decimal CurrentPrice { get; set; }
    public string? CurrentBuyerName { get; set; }
    public int? CurrentBuyerId { get; set; }
    public DateTimeOffset? EndTime { get; set; }
    public AutoBidStatus Status { get; set; }
    public string? LastMessage { get; set; }
}

public interface IAutoBidService
{
    void AddTransfer(int transferId, string pigeonName, decimal currentPrice, int? currentBuyerId, decimal maxPrice);
    void RemoveTransfer(int transferId);
    void UpdateMaxPrice(int transferId, decimal newMax);
    void Start(int fancierId);
    void Stop();
    IReadOnlyCollection<AutoBidEntry> Entries { get; }
    bool IsRunning { get; }
    event Action<AutoBidEntry>? EntryChanged;
    event Action<string>? LogMessage;
}
