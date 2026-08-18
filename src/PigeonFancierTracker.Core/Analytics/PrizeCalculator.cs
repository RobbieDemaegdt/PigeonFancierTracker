using PigeonFancierTracker.Core.Domain;

namespace PigeonFancierTracker.Core.Analytics;

public static class PrizeCalculator
{
    private static readonly int[] RegionalPoints = [50, 40, 30, 25, 10, 5];
    private static readonly int[] NationalPoints = [150, 120, 90, 60, 24, 12];

    public static IReadOnlyList<PrizeTier> CalculatePrizeTable(int subscribers, FlightType type)
    {
        var points = type == FlightType.National ? NationalPoints : RegionalPoints;
        var tiers = new List<PrizeTier>();
        var pos = 1;

        for (var i = 0; i < 3 && pos <= subscribers; i++)
        {
            tiers.Add(new PrizeTier(pos, pos, points[i]));
            pos++;
        }

        var band5 = (int)Math.Ceiling(subscribers * 0.05);
        if (band5 > 0 && pos <= subscribers)
        {
            var end = Math.Min(pos + band5 - 1, subscribers);
            tiers.Add(new PrizeTier(pos, end, points[3]));
            pos = end + 1;
        }

        var band10a = (int)Math.Ceiling(subscribers * 0.10);
        if (band10a > 0 && pos <= subscribers)
        {
            var end = Math.Min(pos + band10a - 1, subscribers);
            tiers.Add(new PrizeTier(pos, end, points[4]));
            pos = end + 1;
        }

        var band10b = (int)Math.Ceiling(subscribers * 0.10);
        if (band10b > 0 && pos <= subscribers)
        {
            var end = Math.Min(pos + band10b - 1, subscribers);
            tiers.Add(new PrizeTier(pos, end, points[5]));
        }

        return tiers;
    }

    public static int GetPointsForPosition(int position, int subscribers, FlightType type)
    {
        var tiers = CalculatePrizeTable(subscribers, type);
        foreach (var tier in tiers)
        {
            if (position >= tier.FromPosition && position <= tier.ToPosition)
                return tier.PointsPerPosition;
        }

        return 0;
    }

    public static int GetTotalPrizePositions(int subscribers)
    {
        var top3 = Math.Min(3, subscribers);
        var band5 = (int)Math.Ceiling(subscribers * 0.05);
        var band10a = (int)Math.Ceiling(subscribers * 0.10);
        var band10b = (int)Math.Ceiling(subscribers * 0.10);
        return Math.Min(top3 + band5 + band10a + band10b, subscribers);
    }

    public static decimal GetPrizeMoneyForPosition(int position, int subscribers, FlightType type, decimal entryPrice)
    {
        var points = GetPointsForPosition(position, subscribers, type);
        if (points == 0) return 0;

        var totalPoints = GetTotalDistributedPoints(subscribers, type);
        if (totalPoints == 0) return 0;

        var pool = subscribers * entryPrice;
        return Math.Round(pool * points / totalPoints, 2);
    }

    public static IReadOnlyList<PrizeTier> CalculatePrizeTableWithMoney(int subscribers, FlightType type, decimal entryPrice)
    {
        var tiers = CalculatePrizeTable(subscribers, type);
        var totalPoints = tiers.Sum(t => t.Count * t.PointsPerPosition);
        if (totalPoints == 0) return tiers;

        var pool = subscribers * entryPrice;
        return tiers.Select(t =>
            t with { PrizeMoneyPerPosition = Math.Round(pool * t.PointsPerPosition / totalPoints, 2) })
            .ToList();
    }

    public static int GetTotalDistributedPoints(int subscribers, FlightType type)
    {
        var tiers = CalculatePrizeTable(subscribers, type);
        var total = 0;
        foreach (var tier in tiers)
            total += tier.Count * tier.PointsPerPosition;
        return total;
    }
}

public sealed record PrizeTier(int FromPosition, int ToPosition, int PointsPerPosition, decimal PrizeMoneyPerPosition = 0)
{
    public int Count => ToPosition - FromPosition + 1;

    public string RangeDisplay => FromPosition == ToPosition
        ? $"{FromPosition}"
        : $"{FromPosition}-{ToPosition}";
}
