using FluentAssertions;
using PigeonFancierTracker.Core.Analytics;
using PigeonFancierTracker.Core.Contracts;

namespace PigeonFancierTracker.Core.Tests;

public sealed class PriceEstimatorTests
{
    private static PriceEstimationRequest CreateRequest(
        decimal? totalSkill = 50,
        int? ageMonths = 12,
        decimal? form = 5, decimal? experience = 5,
        decimal? speed = 5, decimal? technique = 5,
        decimal? stamina = 5, decimal? aerodynamics = 5,
        decimal? intelligence = 5, decimal? libido = 5,
        decimal? nightvision = 5, decimal? navigation = 5) =>
        new(totalSkill, ageMonths, form, experience, speed, technique,
            stamina, aerodynamics, intelligence, libido, nightvision, navigation);

    private static CompletedTransferSummary CreateSale(
        decimal soldPrice,
        decimal? totalSkill = 50,
        int? ageMonths = 12,
        int bidCount = 3,
        decimal? form = 5, decimal? experience = 5,
        decimal? speed = 5, decimal? technique = 5,
        decimal? stamina = 5, decimal? aerodynamics = 5,
        decimal? intelligence = 5, decimal? libido = 5,
        decimal? nightvision = 5, decimal? navigation = 5) =>
        new(soldPrice, totalSkill, ageMonths, bidCount, form, experience,
            speed, technique, stamina, aerodynamics, intelligence, libido, nightvision, navigation);

    [Fact]
    public void Returns_null_when_no_historical_sales()
    {
        var result = PriceEstimator.Estimate(CreateRequest(), []);

        result.Should().BeNull();
    }

    [Fact]
    public void Returns_exact_price_when_single_identical_comparable()
    {
        var request = CreateRequest(totalSkill: 60, ageMonths: 10);
        var sales = new[] { CreateSale(1000, totalSkill: 60, ageMonths: 10) };

        var result = PriceEstimator.Estimate(request, sales);

        result.Should().NotBeNull();
        result!.EstimatedPrice.Should().Be(1000);
        result.ComparableCount.Should().Be(1);
        result.Confidence.Should().Be(PriceConfidence.Low);
    }

    [Fact]
    public void Weights_more_similar_pigeons_higher()
    {
        var request = CreateRequest(totalSkill: 60, speed: 8, stamina: 7);

        var sales = new[]
        {
            CreateSale(500, totalSkill: 30, speed: 3, stamina: 3),  // dissimilar
            CreateSale(1500, totalSkill: 60, speed: 8, stamina: 7), // very similar
        };

        var result = PriceEstimator.Estimate(request, sales);

        result.Should().NotBeNull();
        // Estimated price should be closer to 1500 than 500
        result!.EstimatedPrice.Should().BeGreaterThan(1000);
    }

    [Fact]
    public void Returns_high_confidence_with_ten_or_more_comparables()
    {
        var request = CreateRequest();
        var sales = Enumerable.Range(0, 12)
            .Select(i => CreateSale(800 + i * 10))
            .ToList();

        var result = PriceEstimator.Estimate(request, sales);

        result.Should().NotBeNull();
        result!.Confidence.Should().Be(PriceConfidence.High);
        result.ComparableCount.Should().Be(12);
    }

    [Fact]
    public void Returns_medium_confidence_with_three_to_nine_comparables()
    {
        var request = CreateRequest();
        var sales = Enumerable.Range(0, 5)
            .Select(i => CreateSale(1000 + i * 50))
            .ToList();

        var result = PriceEstimator.Estimate(request, sales);

        result.Should().NotBeNull();
        result!.Confidence.Should().Be(PriceConfidence.Medium);
    }

    [Fact]
    public void Price_range_reflects_top_comparables()
    {
        var request = CreateRequest(totalSkill: 50);
        var sales = new[]
        {
            CreateSale(800, totalSkill: 50),
            CreateSale(1200, totalSkill: 50),
            CreateSale(1000, totalSkill: 50),
        };

        var result = PriceEstimator.Estimate(request, sales);

        result.Should().NotBeNull();
        result!.MinComparablePrice.Should().Be(800);
        result.MaxComparablePrice.Should().Be(1200);
    }

    [Fact]
    public void High_libido_pigeon_matches_high_libido_sales()
    {
        // A breeding pigeon with high libido should match historical breeding pigeon sales
        var request = CreateRequest(totalSkill: 50, libido: 9, speed: 3);

        var racerSale = CreateSale(500, totalSkill: 50, libido: 3, speed: 9);   // racing pigeon
        var breederSale = CreateSale(1200, totalSkill: 50, libido: 9, speed: 3); // breeding pigeon

        var result = PriceEstimator.Estimate(request, [racerSale, breederSale]);

        result.Should().NotBeNull();
        // Should be closer to the breeder sale price
        result!.EstimatedPrice.Should().BeGreaterThan(800);
    }

    [Fact]
    public void Handles_missing_skills_gracefully()
    {
        var request = new PriceEstimationRequest(
            TotalSkill: 50, AgeMonths: 12,
            Form: null, Experience: null, Speed: null, Technique: null,
            Stamina: null, Aerodynamics: null, Intelligence: null,
            Libido: null, Nightvision: null, Navigation: null);

        var sales = new[] { CreateSale(1000, totalSkill: 50, ageMonths: 12) };

        var result = PriceEstimator.Estimate(request, sales);

        result.Should().NotBeNull();
        result!.EstimatedPrice.Should().Be(1000);
    }

    [Fact]
    public void Age_proximity_affects_estimate()
    {
        var request = CreateRequest(totalSkill: 50, ageMonths: 6);

        var sales = new[]
        {
            CreateSale(500, totalSkill: 50, ageMonths: 60),  // old pigeon
            CreateSale(1500, totalSkill: 50, ageMonths: 6),  // same age
        };

        var result = PriceEstimator.Estimate(request, sales);

        result.Should().NotBeNull();
        // Should be closer to 1500 (same age) than 500
        result!.EstimatedPrice.Should().BeGreaterThan(1000);
    }
}
