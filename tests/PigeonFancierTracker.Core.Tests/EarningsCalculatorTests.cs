using FluentAssertions;
using PigeonFancierTracker.Core.Analytics;
using PigeonFancierTracker.Core.Contracts;

namespace PigeonFancierTracker.Core.Tests;

public sealed class EarningsCalculatorTests
{
    [Fact]
    public void Empty_entries_returns_zeroes()
    {
        var result = EarningsCalculator.Calculate([]);

        result.TotalPoints.Should().Be(0);
        result.TotalEntryFees.Should().Be(0m);
        result.RaceCount.Should().Be(0);
    }

    [Fact]
    public void Single_entry_returns_its_values()
    {
        var result = EarningsCalculator.Calculate([new EarningsInput(42, 1.5m)]);

        result.TotalPoints.Should().Be(42);
        result.TotalEntryFees.Should().Be(1.5m);
        result.RaceCount.Should().Be(1);
    }

    [Fact]
    public void Multiple_entries_sums_correctly()
    {
        var entries = new List<EarningsInput>
        {
            new(10, 2.0m),
            new(20, 3.0m),
            new(30, 5.0m),
        };

        var result = EarningsCalculator.Calculate(entries);

        result.TotalPoints.Should().Be(60);
        result.TotalEntryFees.Should().Be(10.0m);
        result.RaceCount.Should().Be(3);
    }

    [Fact]
    public void FormatDisplay_no_races_shows_dash()
    {
        var result = new EarningsResult(0, 0m, 0);
        EarningsCalculator.FormatDisplay(result).Should().Be("—");
    }

    [Fact]
    public void FormatDisplay_with_races_shows_points_and_count()
    {
        var result = new EarningsResult(142, 35.5m, 12);
        EarningsCalculator.FormatDisplay(result).Should().Be("142 ptn / 12 vl.");
    }
}
