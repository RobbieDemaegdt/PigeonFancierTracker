using FluentAssertions;
using PigeonFancierTracker.Core.Analytics;

namespace PigeonFancierTracker.Core.Tests;

public sealed class ConsistencyCalculatorTests
{
    [Fact]
    public void Empty_list_returns_zero_unreliable()
    {
        var result = ConsistencyCalculator.Calculate([]);

        result.StdDev.Should().Be(0);
        result.RaceCount.Should().Be(0);
        result.IsReliable.Should().BeFalse();
    }

    [Fact]
    public void Single_race_returns_zero_stddev_unreliable()
    {
        var result = ConsistencyCalculator.Calculate([50.0]);

        result.StdDev.Should().Be(0);
        result.RaceCount.Should().Be(1);
        result.IsReliable.Should().BeFalse();
    }

    [Fact]
    public void Two_races_unreliable_but_computes_stddev()
    {
        var result = ConsistencyCalculator.Calculate([30.0, 70.0]);

        result.StdDev.Should().Be(20.0);
        result.RaceCount.Should().Be(2);
        result.IsReliable.Should().BeFalse();
    }

    [Fact]
    public void Three_identical_races_yields_zero_stddev_reliable()
    {
        var result = ConsistencyCalculator.Calculate([50.0, 50.0, 50.0]);

        result.StdDev.Should().Be(0);
        result.RaceCount.Should().Be(3);
        result.IsReliable.Should().BeTrue();
    }

    [Fact]
    public void Population_stddev_computed_correctly()
    {
        var result = ConsistencyCalculator.Calculate([10.0, 20.0, 30.0]);

        result.StdDev.Should().Be(8.2);
        result.IsReliable.Should().BeTrue();
    }

    [Fact]
    public void FormatDisplay_no_races_shows_dash()
    {
        var result = new Contracts.ConsistencyResult(0, 0, false);
        ConsistencyCalculator.FormatDisplay(result).Should().Be("—");
    }

    [Fact]
    public void FormatDisplay_unreliable_shows_question_mark()
    {
        var result = new Contracts.ConsistencyResult(5.0, 2, false);
        ConsistencyCalculator.FormatDisplay(result).Should().EndWith("(?)");
    }

    [Fact]
    public void FormatDisplay_low_stddev_shows_stabiel()
    {
        var result = new Contracts.ConsistencyResult(4.2, 5, true);
        ConsistencyCalculator.FormatDisplay(result).Should().Contain("stabiel");
    }

    [Fact]
    public void FormatDisplay_medium_stddev_shows_gemiddeld()
    {
        var result = new Contracts.ConsistencyResult(12.0, 5, true);
        ConsistencyCalculator.FormatDisplay(result).Should().Contain("gemiddeld");
    }

    [Fact]
    public void FormatDisplay_high_stddev_shows_wisselvallig()
    {
        var result = new Contracts.ConsistencyResult(18.5, 5, true);
        ConsistencyCalculator.FormatDisplay(result).Should().Contain("wisselvallig");
    }

    [Fact]
    public void FormatDisplay_boundary_8_is_stabiel()
    {
        var result = new Contracts.ConsistencyResult(8.0, 5, true);
        ConsistencyCalculator.FormatDisplay(result).Should().Contain("stabiel");
    }

    [Fact]
    public void FormatDisplay_boundary_15_is_gemiddeld()
    {
        var result = new Contracts.ConsistencyResult(15.0, 5, true);
        ConsistencyCalculator.FormatDisplay(result).Should().Contain("gemiddeld");
    }
}
