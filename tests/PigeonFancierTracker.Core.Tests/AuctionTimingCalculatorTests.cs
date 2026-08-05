using FluentAssertions;
using PigeonFancierTracker.Core.Analytics;
using PigeonFancierTracker.Core.Contracts;

namespace PigeonFancierTracker.Core.Tests;

public sealed class AuctionTimingCalculatorTests
{
    [Fact]
    public void Empty_auctions_returns_empty_result()
    {
        var result = AuctionTimingCalculator.Calculate([]);

        result.TimeSlots.Should().BeEmpty();
        result.BestTimeSlot.Should().BeNull();
        result.WorstTimeSlot.Should().BeNull();
    }

    [Fact]
    public void Groups_by_four_hour_slots()
    {
        var auctions = new List<AuctionTimingInput>
        {
            new(new DateTimeOffset(2026, 6, 1, 2, 0, 0, TimeSpan.Zero), 10m, 20m, 3),
            new(new DateTimeOffset(2026, 6, 1, 3, 0, 0, TimeSpan.Zero), 10m, 25m, 5),
            new(new DateTimeOffset(2026, 6, 1, 14, 0, 0, TimeSpan.Zero), 10m, 30m, 4),
        };

        var result = AuctionTimingCalculator.Calculate(auctions);

        result.TimeSlots.Should().HaveCount(2);
        result.TimeSlots[0].TimeSlot.Should().Be("00:00-04:00");
        result.TimeSlots[1].TimeSlot.Should().Be("12:00-16:00");
    }

    [Fact]
    public void Best_and_worst_slots_need_minimum_two_sales()
    {
        var auctions = new List<AuctionTimingInput>
        {
            new(new DateTimeOffset(2026, 6, 1, 2, 0, 0, TimeSpan.Zero), 10m, 50m, 3),
        };

        var result = AuctionTimingCalculator.Calculate(auctions);

        result.BestTimeSlot.Should().BeNull();
        result.WorstTimeSlot.Should().BeNull();
    }

    [Fact]
    public void Best_slot_has_highest_avg_sold_price()
    {
        var auctions = new List<AuctionTimingInput>
        {
            new(new DateTimeOffset(2026, 6, 1, 1, 0, 0, TimeSpan.Zero), 10m, 20m, 2),
            new(new DateTimeOffset(2026, 6, 2, 2, 0, 0, TimeSpan.Zero), 10m, 30m, 3),
            new(new DateTimeOffset(2026, 6, 1, 14, 0, 0, TimeSpan.Zero), 10m, 100m, 5),
            new(new DateTimeOffset(2026, 6, 2, 15, 0, 0, TimeSpan.Zero), 10m, 200m, 8),
        };

        var result = AuctionTimingCalculator.Calculate(auctions);

        result.BestTimeSlot.Should().Be("12:00-16:00");
        result.WorstTimeSlot.Should().Be("00:00-04:00");
    }

    [Fact]
    public void Avg_premium_computed_correctly()
    {
        var auctions = new List<AuctionTimingInput>
        {
            new(new DateTimeOffset(2026, 6, 1, 10, 0, 0, TimeSpan.Zero), 100m, 150m, 3),
            new(new DateTimeOffset(2026, 6, 2, 11, 0, 0, TimeSpan.Zero), 100m, 200m, 5),
        };

        var result = AuctionTimingCalculator.Calculate(auctions);
        var slot = result.TimeSlots.Single();

        slot.AvgPremium.Should().Be(75.0m);
    }

    [Fact]
    public void Zero_start_price_premium_is_zero()
    {
        var auctions = new List<AuctionTimingInput>
        {
            new(new DateTimeOffset(2026, 6, 1, 10, 0, 0, TimeSpan.Zero), 0m, 150m, 3),
        };

        var result = AuctionTimingCalculator.Calculate(auctions);
        result.TimeSlots.Single().AvgPremium.Should().Be(0m);
    }

    [Fact]
    public void Time_slots_ordered_chronologically()
    {
        var auctions = new List<AuctionTimingInput>
        {
            new(new DateTimeOffset(2026, 6, 1, 22, 0, 0, TimeSpan.Zero), 10m, 20m, 1),
            new(new DateTimeOffset(2026, 6, 1, 6, 0, 0, TimeSpan.Zero), 10m, 20m, 1),
            new(new DateTimeOffset(2026, 6, 1, 14, 0, 0, TimeSpan.Zero), 10m, 20m, 1),
        };

        var result = AuctionTimingCalculator.Calculate(auctions);

        result.TimeSlots.Select(t => t.TimeSlot).Should().BeInAscendingOrder();
    }
}
