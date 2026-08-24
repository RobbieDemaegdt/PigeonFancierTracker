using PigeonFancierTracker.Core.Analytics;
using PigeonFancierTracker.Core.Contracts;

namespace PigeonFancierTracker.App;

/// <summary>
/// Display row for one age category of a national flight's prize breakdown. Wraps
/// <see cref="AgeCategoryPrizeInfo"/> with a Dutch header label so the Core layer
/// stays free of UI strings. <see cref="PrizeTable"/> feeds a nested grid.
/// </summary>
public sealed class AgeCategoryPrizeRow
{
    public required string HeaderText { get; init; }
    public required IReadOnlyList<PrizeTier> PrizeTable { get; init; }

    public static AgeCategoryPrizeRow From(AgeCategoryPrizeInfo info) => new()
    {
        HeaderText =
            $"{Label(info.Category)} — {info.Participants} duiven · " +
            $"{info.PrizePositions} prijzen · EUR {info.TotalPrizeMoney:0.00}",
        PrizeTable = info.PrizeTable,
    };

    private static string Label(AgeCategory category) => category switch
    {
        AgeCategory.Elder => "Oude duiven",
        AgeCategory.Yearling => "Jaarlingen",
        AgeCategory.Youth => "Jonge duiven",
        _ => category.ToString(),
    };
}
