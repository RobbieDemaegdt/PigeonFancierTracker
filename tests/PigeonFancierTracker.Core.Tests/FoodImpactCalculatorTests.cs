using FluentAssertions;
using PigeonFancierTracker.Core.Analytics;
using PigeonFancierTracker.Core.Contracts;
using PigeonFancierTracker.Core.Domain;

namespace PigeonFancierTracker.Core.Tests;

public sealed class FoodImpactCalculatorTests
{
    [Fact]
    public void Empty_results_returns_empty_analysis()
    {
        var result = FoodImpactCalculator.Calculate([]);

        result.MixPerformances.Should().BeEmpty();
    }

    [Fact]
    public void Results_without_food_mix_returns_empty_analysis()
    {
        var results = new[]
        {
            MakeResult(1, 5, 50, 10, 85.5m),
        };

        var result = FoodImpactCalculator.Calculate(results);

        result.MixPerformances.Should().BeEmpty();
    }

    [Fact]
    public void Single_mix_single_flight_counts_one_flight()
    {
        var mix = new FoodMix(25, 25, 30, 20);
        var results = new[]
        {
            MakeResult(1, 5, 50, 10, 85.5m, mix),
        };

        var result = FoodImpactCalculator.Calculate(results);

        result.MixPerformances.Should().HaveCount(1);
        var perf = result.MixPerformances[0];
        perf.Mix.Should().Be(mix);
        perf.FlightCount.Should().Be(1);
        perf.AvgPercentile.Should().Be(10.0);
        perf.AvgPoints.Should().Be(10);
        perf.AvgSpeed.Should().Be(85.50m);
        perf.Consistency.Should().Be(0);
    }

    [Fact]
    public void Multiple_pigeons_same_flight_counts_as_one_flight()
    {
        var mix = new FoodMix(25, 25, 30, 20);
        var results = new[]
        {
            MakeResult(1, 5, 50, 10, 85m, mix, pigeonId: 100),
            MakeResult(1, 10, 50, 8, 83m, mix, pigeonId: 101),
            MakeResult(1, 15, 50, 6, 80m, mix, pigeonId: 102),
        };

        var result = FoodImpactCalculator.Calculate(results);

        result.MixPerformances.Should().HaveCount(1);
        result.MixPerformances[0].FlightCount.Should().Be(1);
    }

    [Fact]
    public void Multiple_flights_counted_correctly()
    {
        var mix = new FoodMix(25, 25, 30, 20);
        var results = new[]
        {
            MakeResult(1, 5, 50, 10, 85m, mix, pigeonId: 100),
            MakeResult(1, 10, 50, 8, 83m, mix, pigeonId: 101),
            MakeResult(2, 3, 50, 12, 87m, mix, pigeonId: 100),
            MakeResult(2, 8, 50, 9, 84m, mix, pigeonId: 101),
        };

        var result = FoodImpactCalculator.Calculate(results);

        result.MixPerformances[0].FlightCount.Should().Be(2);
    }

    [Fact]
    public void Multiple_mixes_sorted_by_best_percentile()
    {
        var goodMix = new FoodMix(30, 20, 30, 20);
        var badMix = new FoodMix(10, 10, 40, 40);

        var results = new[]
        {
            MakeResult(1, 2, 100, 20, 90m, goodMix),
            MakeResult(2, 3, 100, 18, 89m, goodMix),
            MakeResult(3, 50, 100, 5, 70m, badMix),
            MakeResult(4, 60, 100, 3, 65m, badMix),
        };

        var result = FoodImpactCalculator.Calculate(results);

        result.MixPerformances.Should().HaveCount(2);
        result.MixPerformances[0].Mix.Should().Be(goodMix);
        result.MixPerformances[0].AvgPercentile.Should().BeLessThan(result.MixPerformances[1].AvgPercentile);
    }

    [Fact]
    public void Same_mix_values_are_grouped_together()
    {
        var mix1 = new FoodMix(25, 25, 30, 20);
        var mix2 = new FoodMix(25, 25, 30, 20);

        var results = new[]
        {
            MakeResult(1, 5, 50, 10, 85m, mix1),
            MakeResult(2, 10, 50, 8, 82m, mix2),
        };

        var result = FoodImpactCalculator.Calculate(results);

        result.MixPerformances.Should().HaveCount(1);
        result.MixPerformances[0].FlightCount.Should().Be(2);
    }

    [Fact]
    public void Category_stats_computed_per_distance()
    {
        var mix = new FoodMix(25, 25, 30, 20);
        var results = new[]
        {
            MakeResult(1, 10, 100, 15, 80m, mix, category: DistanceCategory.Short),
            MakeResult(2, 5, 100, 20, 90m, mix, category: DistanceCategory.Middle),
            MakeResult(3, 8, 100, 18, 85m, mix, category: DistanceCategory.Middle),
        };

        var result = FoodImpactCalculator.Calculate(results);
        var perf = result.MixPerformances[0];

        perf.Short.Should().NotBeNull();
        perf.Short!.FlightCount.Should().Be(1);

        perf.Middle.Should().NotBeNull();
        perf.Middle!.FlightCount.Should().Be(2);

        perf.Long.Should().BeNull();
    }

    [Fact]
    public void Comments_are_passed_through()
    {
        var mix = new FoodMix(25, 25, 30, 20);
        var results = new[]
        {
            MakeResult(1, 5, 50, 10, 85m, mix),
        };

        var comments = new Dictionary<FoodMix, string?> { [mix] = "Test opmerking" };
        var result = FoodImpactCalculator.Calculate(results, comments);

        result.MixPerformances[0].Comment.Should().Be("Test opmerking");
    }

    private static FlightResultListItem MakeResult(
        int flightId, int position, int total, int points, decimal speed,
        FoodMix? mix = null, int pigeonId = 100,
        DistanceCategory category = DistanceCategory.Middle)
    {
        var percentile = total > 0 ? Math.Round((double)position / total * 100, 1) : 0;
        return new FlightResultListItem(
            flightId,
            new DateTime(2026, 7, 1).AddDays(flightId),
            "regional",
            "Test",
            200,
            category,
            position,
            total,
            percentile,
            points,
            speed,
            "TestPigeon",
            pigeonId,
            mix);
    }
}
