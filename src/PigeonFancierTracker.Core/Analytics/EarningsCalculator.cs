using PigeonFancierTracker.Core.Contracts;

namespace PigeonFancierTracker.Core.Analytics;

public static class EarningsCalculator
{
    public static EarningsResult Calculate(IReadOnlyList<EarningsInput> raceEntries)
    {
        if (raceEntries.Count == 0)
            return new EarningsResult(0, 0m, 0);

        return new EarningsResult(
            TotalPoints: raceEntries.Sum(e => e.Points),
            TotalEntryFees: raceEntries.Sum(e => e.EntryPrice),
            RaceCount: raceEntries.Count);
    }

    public static string FormatDisplay(EarningsResult result)
    {
        if (result.RaceCount == 0)
            return "—";

        return $"{result.TotalPoints} ptn / {result.RaceCount} vl.";
    }
}
