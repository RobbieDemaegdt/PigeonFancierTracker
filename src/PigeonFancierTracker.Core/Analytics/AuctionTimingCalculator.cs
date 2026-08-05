using PigeonFancierTracker.Core.Contracts;

namespace PigeonFancierTracker.Core.Analytics;

public static class AuctionTimingCalculator
{
    public static AuctionTimingResult Calculate(IReadOnlyList<AuctionTimingInput> auctions)
    {
        if (auctions.Count == 0)
            return new AuctionTimingResult([], null, null);

        var timeSlots = auctions
            .GroupBy(a => FormatTimeSlot(a.TransferEnd.Hour))
            .Select(g => new TimeSlotStats(
                g.Key,
                Math.Round((decimal)g.Average(a => a.BidCount), 1),
                Math.Round(g.Average(a => a.SoldPrice), 0),
                Math.Round(g.Average(a => a.StartPrice > 0
                    ? (a.SoldPrice - a.StartPrice) / a.StartPrice * 100
                    : 0), 1),
                g.Count()))
            .OrderBy(t => t.TimeSlot)
            .ToList();

        var bestSlot = timeSlots
            .Where(t => t.SaleCount >= 2)
            .OrderByDescending(t => t.AvgSoldPrice)
            .FirstOrDefault()?.TimeSlot;

        var worstSlot = timeSlots
            .Where(t => t.SaleCount >= 2)
            .OrderBy(t => t.AvgSoldPrice)
            .FirstOrDefault()?.TimeSlot;

        return new AuctionTimingResult(timeSlots, bestSlot, worstSlot);
    }

    private static string FormatTimeSlot(int hour)
    {
        var slotStart = hour / 4 * 4;
        var slotEnd = slotStart + 4;
        return $"{slotStart:D2}:00-{slotEnd:D2}:00";
    }
}
