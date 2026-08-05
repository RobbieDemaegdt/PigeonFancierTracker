namespace PigeonFancierTracker.Core.Analytics;

public static class AgeWindowFilter
{
    private const int DefaultMinComparables = 3;
    private const int DefaultInitialWindow = 3;
    private const int MaxWindow = 120;

    public static AgeWindowResult<T> Filter<T>(
        int? targetAgeMonths,
        IReadOnlyList<T> items,
        Func<T, int?> ageSelector,
        int minComparables = DefaultMinComparables)
    {
        if (!targetAgeMonths.HasValue)
            return new AgeWindowResult<T>(items, null);

        int window = targetAgeMonths.Value == 0 ? 0 : DefaultInitialWindow;

        while (window <= MaxWindow)
        {
            var filtered = items
                .Where(x =>
                {
                    var age = ageSelector(x);
                    if (!age.HasValue) return false;
                    return Math.Abs(age.Value - targetAgeMonths.Value) <= window;
                })
                .ToList();

            if (filtered.Count >= minComparables)
                return new AgeWindowResult<T>(filtered, window);

            window = window == 0 ? DefaultInitialWindow : window * 2;
        }

        var final = items
            .Where(x => ageSelector(x).HasValue)
            .ToList();

        return new AgeWindowResult<T>(
            final.Count > 0 ? final : items,
            null);
    }

    public static AgeWindowResult<T> FilterAtWindow<T>(
        int? targetAgeMonths,
        IReadOnlyList<T> items,
        Func<T, int?> ageSelector,
        int windowMonths)
    {
        if (!targetAgeMonths.HasValue)
            return new AgeWindowResult<T>(items, null);

        var filtered = items
            .Where(x =>
            {
                var age = ageSelector(x);
                if (!age.HasValue) return false;
                return Math.Abs(age.Value - targetAgeMonths.Value) <= windowMonths;
            })
            .ToList();

        return new AgeWindowResult<T>(filtered, windowMonths);
    }

    public static string? FormatWindow(int? windowMonths) => windowMonths switch
    {
        null => null,
        0 => "0m",
        _ => $"±{windowMonths}m",
    };
}

public sealed record AgeWindowResult<T>(
    IReadOnlyList<T> Items,
    int? WindowMonths);
