using FluentAssertions;
using PigeonFancierTracker.Core.Analytics;

namespace PigeonFancierTracker.Core.Tests;

public sealed class WeeklyGrowthCalculatorTests
{
    [Fact]
    public void Calculates_absolute_and_percentage_growth()
    {
        var result = WeeklyGrowthCalculator.Calculate("pigeon-1",
        [
            new("pigeon-1", DateTimeOffset.Parse("2026-07-20T10:00:00Z"), 120),
            new("pigeon-1", DateTimeOffset.Parse("2026-07-13T10:00:00Z"), 100),
        ]);

        result.AbsoluteGrowth.Should().Be(20);
        result.PercentageGrowth.Should().Be(20);
        result.IsNewlyObserved.Should().BeFalse();
        result.HasMissingData.Should().BeFalse();
    }

    [Fact]
    public void Does_not_calculate_percentage_when_previous_value_is_zero()
    {
        var result = WeeklyGrowthCalculator.Calculate("pigeon-1",
        [
            new("pigeon-1", DateTimeOffset.UtcNow, 12),
            new("pigeon-1", DateTimeOffset.UtcNow.AddDays(-7), 0),
        ]);

        result.AbsoluteGrowth.Should().Be(12);
        result.PercentageGrowth.Should().BeNull();
    }
}