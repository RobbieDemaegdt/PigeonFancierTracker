using PigeonFancierTracker.Core.Contracts;

namespace PigeonFancierTracker.Core.Analytics;

public static class MultiWindowPriceEstimator
{
    private static readonly int[] FixedWindows = [0, 1, 3, 6];

    public static IReadOnlyList<WindowPriceRow> Estimate(
        PriceEstimationRequest target,
        int? targetAgeMonths,
        IReadOnlyList<CompletedTransferSummary> historicalSales)
    {
        return FixedWindows.Select(window =>
        {
            var filtered = AgeWindowFilter.FilterAtWindow(
                targetAgeMonths, historicalSales, s => s.AgeMonths, window);

            var result = PriceEstimator.Estimate(target, filtered.Items);

            return new WindowPriceRow(
                AgeWindowFilter.FormatWindow(window) ?? "0m",
                window,
                result?.ComparableCount ?? 0,
                result?.EstimatedPrice,
                result?.MinComparablePrice,
                result?.MaxComparablePrice,
                result?.Confidence,
                result?.Comparables);
        }).ToList();
    }
}
