using FluentAssertions;
using PigeonFancierTracker.Core.Analytics;
using PigeonFancierTracker.Core.Contracts;
using PigeonFancierTracker.Core.Domain;
using Xunit;

namespace PigeonFancierTracker.Core.Tests;

public sealed class BreedAnalysisCalculatorTests
{
    [Fact]
    public void Empty_results_returns_empty_analysis()
    {
        var result = BreedAnalysisCalculator.Calculate([]);

        result.Breeds.Should().BeEmpty();
    }

    [Fact]
    public void Results_without_breed_are_ignored()
    {
        var results = new[]
        {
            MakeResult(1, 5, 100, breed: null),
        };

        var result = BreedAnalysisCalculator.Calculate(results);

        result.Breeds.Should().BeEmpty();
    }

    [Fact]
    public void Groups_by_breed_case_insensitively()
    {
        var results = new[]
        {
            MakeResult(1, 5, 100, breed: "Janssen"),
            MakeResult(2, 10, 100, breed: "janssen"),
        };

        var result = BreedAnalysisCalculator.Calculate(results);

        result.Breeds.Should().HaveCount(1);
        result.Breeds[0].TotalFlights.Should().Be(2);
    }

    [Fact]
    public void Distance_buckets_are_built_per_category()
    {
        var results = new[]
        {
            MakeResult(1, 10, 100, breed: "A", category: DistanceCategory.Short),
            MakeResult(2, 20, 100, breed: "A", category: DistanceCategory.Long),
        };

        var result = BreedAnalysisCalculator.Calculate(results);
        var conditions = result.Breeds[0].Conditions;

        conditions.Should().Contain(c => c.Dimension == ConditionDimension.Distance && c.Bucket == "Kort");
        conditions.Should().Contain(c => c.Dimension == ConditionDimension.Distance && c.Bucket == "Lang");
    }

    [Fact]
    public void Night_flights_produce_a_daynight_bucket()
    {
        var results = new[]
        {
            MakeResult(1, 5, 100, breed: "A", weatherDay: false),
        };

        var result = BreedAnalysisCalculator.Calculate(results);

        result.Breeds[0].Conditions
            .Should().Contain(c => c.Dimension == ConditionDimension.DayNight && c.Bucket == "Nacht");
    }

    [Fact]
    public void High_beaufort_is_classified_as_windy()
    {
        var results = new[]
        {
            MakeResult(1, 5, 100, breed: "A", weatherBeaufort: 6),
            MakeResult(2, 8, 100, breed: "A", weatherBeaufort: 2),
        };

        var result = BreedAnalysisCalculator.Calculate(results);
        var wind = result.Breeds[0].Conditions.Where(c => c.Dimension == ConditionDimension.Wind).ToList();

        wind.Should().Contain(c => c.Bucket == "Wind (Bft ≥5)");
        wind.Should().Contain(c => c.Bucket == "Kalm");
    }

    [Fact]
    public void Bucket_with_fewer_than_three_races_is_not_reliable()
    {
        var results = new[]
        {
            MakeResult(1, 5, 100, breed: "A", category: DistanceCategory.Short),
            MakeResult(2, 8, 100, breed: "A", category: DistanceCategory.Short),
        };

        var result = BreedAnalysisCalculator.Calculate(results);
        var shortBucket = result.Breeds[0].Conditions
            .Single(c => c.Dimension == ConditionDimension.Distance && c.Bucket == "Kort");

        shortBucket.IsReliable.Should().BeFalse();
    }

    [Fact]
    public void Bucket_with_three_or_more_races_is_reliable_and_sets_best_condition()
    {
        var results = new[]
        {
            MakeResult(1, 5, 100, breed: "A", category: DistanceCategory.Short),
            MakeResult(2, 8, 100, breed: "A", category: DistanceCategory.Short),
            MakeResult(3, 6, 100, breed: "A", category: DistanceCategory.Short),
        };

        var result = BreedAnalysisCalculator.Calculate(results);
        var profile = result.Breeds[0];

        profile.Conditions
            .Single(c => c.Dimension == ConditionDimension.Distance && c.Bucket == "Kort")
            .IsReliable.Should().BeTrue();
        profile.BestConditionDisplay.Should().Be("Afstand: Kort");
    }

    [Fact]
    public void Breeds_sorted_by_overall_average_percentile()
    {
        var results = new[]
        {
            MakeResult(1, 2, 100, breed: "Good"),
            MakeResult(2, 3, 100, breed: "Good"),
            MakeResult(3, 80, 100, breed: "Bad"),
            MakeResult(4, 90, 100, breed: "Bad"),
        };

        var result = BreedAnalysisCalculator.Calculate(results);

        result.Breeds[0].Breed.Should().Be("Good");
        result.Breeds[0].OverallAvgPercentile
            .Should().BeLessThan(result.Breeds[1].OverallAvgPercentile);
    }

    private static FlightResultListItem MakeResult(
        int flightId, int position, int total,
        string? breed, int pigeonId = 100,
        DistanceCategory category = DistanceCategory.Middle,
        bool? weatherDay = null, int? weatherBeaufort = null,
        decimal? weatherTemperature = null)
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
            10,
            85m,
            "TestPigeon",
            pigeonId,
            null,
            breed,
            weatherDay,
            weatherBeaufort,
            weatherTemperature,
            null);
    }
}
