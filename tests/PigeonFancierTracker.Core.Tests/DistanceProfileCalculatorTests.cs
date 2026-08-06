using FluentAssertions;
using PigeonFancierTracker.Core.Analytics;
using PigeonFancierTracker.Core.Contracts;
using PigeonFancierTracker.Core.Domain;
using Xunit;

namespace PigeonFancierTracker.Core.Tests;

public sealed class DistanceProfileCalculatorTests
{
    [Theory]
    [InlineData(100, DistanceCategory.Short)]
    [InlineData(200, DistanceCategory.Short)]
    [InlineData(201, DistanceCategory.Middle)]
    [InlineData(300, DistanceCategory.Middle)]
    [InlineData(500, DistanceCategory.Middle)]
    [InlineData(501, DistanceCategory.Long)]
    [InlineData(1000, DistanceCategory.Long)]
    public void Classify_ReturnsCorrectCategory(int distanceKm, DistanceCategory expected)
    {
        DistanceProfileCalculator.Classify(distanceKm).Should().Be(expected);
    }

    [Fact]
    public void Calculate_EmptyResults_ReturnsZeroCounts()
    {
        var profile = DistanceProfileCalculator.Calculate(1, "Test Pigeon", null, []);

        profile.ShortRaces.Should().Be(0);
        profile.MiddleRaces.Should().Be(0);
        profile.LongRaces.Should().Be(0);
        profile.BestCategory.Should().BeNull();
        profile.BestCategoryDisplay.Should().BeNull();
    }

    [Fact]
    public void Calculate_SingleCategory_SetsBestToThatCategory()
    {
        var results = new[]
        {
            MakeResult(1, DistanceCategory.Short, position: 3, totalParticipants: 100, points: 10),
            MakeResult(2, DistanceCategory.Short, position: 5, totalParticipants: 100, points: 8),
        };

        var profile = DistanceProfileCalculator.Calculate(1, "Test Pigeon", null, results);

        profile.ShortRaces.Should().Be(2);
        profile.ShortBestPosition.Should().Be(3);
        profile.ShortTotalPoints.Should().Be(18);
        profile.ShortAvgPosition.Should().BeApproximately(4.0, 0.1);
        profile.BestCategory.Should().Be(DistanceCategory.Short);
        profile.BestCategoryDisplay.Should().Be("Kort");
    }

    [Fact]
    public void Calculate_MultipleCategories_BestIsLowestPercentile()
    {
        var results = new[]
        {
            // Short: position 50/100 = 50% percentile
            MakeResult(1, DistanceCategory.Short, position: 50, totalParticipants: 100, points: 0),
            // Middle: position 5/100 = 5% percentile (better)
            MakeResult(2, DistanceCategory.Middle, position: 5, totalParticipants: 100, points: 10),
            // Long: position 80/100 = 80% percentile
            MakeResult(3, DistanceCategory.Long, position: 80, totalParticipants: 100, points: 0),
        };

        var profile = DistanceProfileCalculator.Calculate(1, "Test Pigeon", null, results);

        profile.BestCategory.Should().Be(DistanceCategory.Middle);
        profile.BestCategoryDisplay.Should().Be("Midden");
        profile.MiddleAvgPercentile.Should().BeLessThan(profile.ShortAvgPercentile);
    }

    [Fact]
    public void Calculate_AggregatesCorrectly()
    {
        var results = new[]
        {
            MakeResult(1, DistanceCategory.Short, position: 1, totalParticipants: 50, points: 20),
            MakeResult(2, DistanceCategory.Short, position: 10, totalParticipants: 50, points: 5),
            MakeResult(3, DistanceCategory.Long, position: 3, totalParticipants: 80, points: 15),
        };

        var profile = DistanceProfileCalculator.Calculate(42, "Racer", null, results);

        profile.PigeonId.Should().Be(42);
        profile.PigeonName.Should().Be("Racer");
        profile.ShortRaces.Should().Be(2);
        profile.ShortBestPosition.Should().Be(1);
        profile.ShortTotalPoints.Should().Be(25);
        profile.LongRaces.Should().Be(1);
        profile.LongBestPosition.Should().Be(3);
        profile.LongTotalPoints.Should().Be(15);
        profile.MiddleRaces.Should().Be(0);
    }

    private static FlightResultListItem MakeResult(
        int flightId,
        DistanceCategory category,
        int position,
        int totalParticipants,
        int points)
    {
        double percentile = totalParticipants > 0 ? (double)position / totalParticipants * 100 : 0;
        return new FlightResultListItem(
            flightId,
            DateTime.UtcNow,
            "regional",
            "test-location",
            category == DistanceCategory.Short ? 200 : category == DistanceCategory.Middle ? 400 : 600,
            category,
            position,
            totalParticipants,
            percentile,
            points,
            1400m,
            "Test Pigeon",
            1);
    }
}
