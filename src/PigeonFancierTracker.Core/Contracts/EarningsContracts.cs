namespace PigeonFancierTracker.Core.Contracts;

public sealed record EarningsInput(int Points, decimal EntryPrice);

public sealed record EarningsResult(
    int TotalPoints,
    decimal TotalEntryFees,
    int RaceCount);
