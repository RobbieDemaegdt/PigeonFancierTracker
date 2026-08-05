using FluentAssertions;
using PigeonFancierTracker.Core.Analytics;
using PigeonFancierTracker.Core.Contracts;

namespace PigeonFancierTracker.Core.Tests;

public sealed class StatsComparerTests
{
    private static DistanceStats Stats(decimal s, decimal m, decimal l, decimal t) =>
        new(s, m, l, t);

    [Fact]
    public void Returns_null_when_population_is_empty()
    {
        var result = StatsComparer.Compare(Stats(20, 20, 20, 70), []);

        result.Should().BeNull();
    }

    [Fact]
    public void Returns_100_when_target_is_highest_in_population()
    {
        var population = new[] { Stats(10, 10, 10, 40), Stats(15, 15, 15, 55) };

        var result = StatsComparer.Compare(Stats(20, 20, 20, 70), population);

        result.Should().NotBeNull();
        result!.ShortPercentile.Should().Be(100);
        result.MediumPercentile.Should().Be(100);
        result.LongPercentile.Should().Be(100);
        result.TotalPercentile.Should().Be(100);
    }

    [Fact]
    public void Returns_low_percentile_when_target_is_lowest()
    {
        var population = new[] { Stats(20, 20, 20, 70), Stats(25, 25, 25, 85) };

        var result = StatsComparer.Compare(Stats(5, 5, 5, 20), population);

        result.Should().NotBeNull();
        result!.ShortPercentile.Should().Be(0);
        result.TotalPercentile.Should().Be(0);
    }

    [Fact]
    public void Handles_ties_correctly()
    {
        var population = new[]
        {
            Stats(15, 15, 15, 55),
            Stats(15, 15, 15, 55),
            Stats(15, 15, 15, 55),
            Stats(20, 20, 20, 70),
        };

        var result = StatsComparer.Compare(Stats(15, 15, 15, 55), population);

        result.Should().NotBeNull();
        result!.ShortPercentile.Should().Be(75);
    }

    [Fact]
    public void Single_element_population_returns_100_when_equal_or_better()
    {
        var population = new[] { Stats(18, 18, 18, 62) };

        var result = StatsComparer.Compare(Stats(18, 20, 15, 62), population);

        result.Should().NotBeNull();
        result!.ShortPercentile.Should().Be(100);
        result.MediumPercentile.Should().Be(100);
        result.LongPercentile.Should().Be(0);
        result.TotalPercentile.Should().Be(100);
    }

    [Fact]
    public void Computes_median_percentile_correctly()
    {
        var population = Enumerable.Range(1, 10)
            .Select(i => Stats(i * 3, i * 3, i * 3, i * 10))
            .ToList();

        var result = StatsComparer.Compare(Stats(15, 15, 15, 50), population);

        result.Should().NotBeNull();
        result!.ShortPercentile.Should().Be(50);
        result.TotalPercentile.Should().Be(50);
    }

    [Fact]
    public void Dimensions_are_independent()
    {
        var population = new[]
        {
            Stats(10, 25, 10, 50),
            Stats(20, 15, 20, 60),
        };

        var result = StatsComparer.Compare(Stats(15, 20, 25, 55), population);

        result.Should().NotBeNull();
        result!.ShortPercentile.Should().Be(50);
        result.MediumPercentile.Should().Be(50);
        result.LongPercentile.Should().Be(100);
        result.TotalPercentile.Should().Be(50);
    }
}
