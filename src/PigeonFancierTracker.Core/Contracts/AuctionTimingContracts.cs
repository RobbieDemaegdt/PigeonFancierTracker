namespace PigeonFancierTracker.Core.Contracts;

public sealed record AuctionTimingInput(
    DateTimeOffset TransferEnd,
    decimal StartPrice,
    decimal SoldPrice,
    int BidCount);

public sealed record TimeSlotStats(
    string TimeSlot,
    decimal AvgBidCount,
    decimal AvgSoldPrice,
    decimal AvgPremium,
    int SaleCount);

public sealed record AuctionTimingResult(
    IReadOnlyList<TimeSlotStats> TimeSlots,
    string? BestTimeSlot,
    string? WorstTimeSlot);
