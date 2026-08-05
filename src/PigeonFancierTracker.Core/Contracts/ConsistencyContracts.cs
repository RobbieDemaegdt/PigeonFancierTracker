namespace PigeonFancierTracker.Core.Contracts;

public sealed record ConsistencyResult(
    double StdDev,
    int RaceCount,
    bool IsReliable);
