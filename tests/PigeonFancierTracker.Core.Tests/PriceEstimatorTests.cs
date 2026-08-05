using FluentAssertions;
using PigeonFancierTracker.Core.Analytics;
using PigeonFancierTracker.Core.Contracts;

namespace PigeonFancierTracker.Core.Tests;

public sealed class PriceEstimatorTests
{
    private static PriceEstimationRequest CreateRequest(
        decimal? totalSkill = 50,
        int? ageMonths = 12,
        string? breed = null,
        decimal? form = 5, decimal? experience = 5,
        decimal? speed = 5, decimal? technique = 5,
        decimal? stamina = 5, decimal? aerodynamics = 5,
        decimal? intelligence = 5, decimal? libido = 5,
        decimal? nightvision = 5, decimal? navigation = 5) =>
        new(totalSkill, ageMonths, breed, form, experience, speed, technique,
            stamina, aerodynamics, intelligence, libido, nightvision, navigation);

    private static CompletedTransferSummary CreateSale(
        decimal soldPrice,
        decimal? totalSkill = 50,
        int? ageMonths = 12,
        string? breed = null,
        int bidCount = 3,
        string? pigeonName = null,
        int? transferId = null,
        decimal? form = 5, decimal? experience = 5,
        decimal? speed = 5, decimal? technique = 5,
        decimal? stamina = 5, decimal? aerodynamics = 5,
        decimal? intelligence = 5, decimal? libido = 5,
        decimal? nightvision = 5, decimal? navigation = 5) =>
        new(soldPrice, totalSkill, ageMonths, breed, bidCount, pigeonName, transferId,
            form, experience, speed, technique, stamina, aerodynamics, intelligence,
            libido, nightvision, navigation);

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
            TotalSkill: 50, AgeMonths: 12, Breed: null,
            Form: null, Experience: null, Speed: null, Technique: null,
            Stamina: null, Aerodynamics: null, Intelligence: null,
            Libido: null, Nightvision: null, Navigation: null);

        var sales = new[] { CreateSale(1000, totalSkill: 50, ageMonths: 12) };

        var result = PriceEstimator.Estimate(request, sales);

        result.Should().NotBeNull();
        result!.EstimatedPrice.Should().Be(1000);
    }

    [Fact]
    public void Younger_pigeon_with_same_stats_is_valued_higher()
    {
        var youngRequest = CreateRequest(totalSkill: 50, ageMonths: 6);
        var oldRequest = CreateRequest(totalSkill: 50, ageMonths: 48);

        var sales = new[]
        {
            CreateSale(1000, totalSkill: 50, ageMonths: 24),
        };

        var youngResult = PriceEstimator.Estimate(youngRequest, sales);
        var oldResult = PriceEstimator.Estimate(oldRequest, sales);

        youngResult.Should().NotBeNull();
        oldResult.Should().NotBeNull();
        youngResult!.EstimatedPrice.Should().BeGreaterThan(oldResult!.EstimatedPrice);
    }

    [Fact]
    public void Age_adjustment_increases_price_for_younger_target()
    {
        var request = CreateRequest(totalSkill: 50, ageMonths: 6);

        var sales = new[] { CreateSale(1000, totalSkill: 50, ageMonths: 30) };

        var result = PriceEstimator.Estimate(request, sales);

        result.Should().NotBeNull();
        // 24 months younger → factor 1.24 → estimated 1240
        result!.EstimatedPrice.Should().Be(1240);
    }

    [Fact]
    public void Age_adjustment_decreases_price_for_older_target()
    {
        var request = CreateRequest(totalSkill: 50, ageMonths: 48);

        var sales = new[] { CreateSale(1000, totalSkill: 50, ageMonths: 12) };

        var result = PriceEstimator.Estimate(request, sales);

        result.Should().NotBeNull();
        // 36 months older → factor 0.64 → estimated 640
        result!.EstimatedPrice.Should().Be(640);
    }

    [Fact]
    public void Age_adjustment_clamped_at_upper_bound()
    {
        var factor = PriceEstimator.ComputeAgeAdjustmentFactor(1, 100);

        factor.Should().Be(1.5);
    }

    [Fact]
    public void Age_adjustment_clamped_at_lower_bound()
    {
        var factor = PriceEstimator.ComputeAgeAdjustmentFactor(100, 1);

        factor.Should().Be(0.5);
    }

    [Fact]
    public void Age_adjustment_returns_one_when_same_age()
    {
        var factor = PriceEstimator.ComputeAgeAdjustmentFactor(24, 24);

        factor.Should().Be(1.0);
    }

    [Fact]
    public void Age_adjustment_returns_one_when_target_age_null()
    {
        var factor = PriceEstimator.ComputeAgeAdjustmentFactor(null, 24);

        factor.Should().Be(1.0);
    }

    [Fact]
    public void Age_adjustment_returns_one_when_comparable_age_null()
    {
        var factor = PriceEstimator.ComputeAgeAdjustmentFactor(24, null);

        factor.Should().Be(1.0);
    }

    [Fact]
    public void Age_adjustment_returns_one_when_both_ages_null()
    {
        var factor = PriceEstimator.ComputeAgeAdjustmentFactor(null, null);

        factor.Should().Be(1.0);
    }

    [Fact]
    public void Same_breed_increases_similarity()
    {
        var target = CreateRequest(totalSkill: 50, breed: "Sprinter");
        var sameBreed = CreateSale(1000, totalSkill: 50, breed: "Sprinter");
        var diffBreed = CreateSale(1000, totalSkill: 50, breed: "Voyager");

        var sameSimilarity = PriceEstimator.ComputeSimilarity(target, sameBreed);
        var diffSimilarity = PriceEstimator.ComputeSimilarity(target, diffBreed);

        sameSimilarity.Should().BeGreaterThan(diffSimilarity);
    }

    [Fact]
    public void Breed_match_is_case_insensitive()
    {
        var target = CreateRequest(totalSkill: 50, breed: "sprinter");
        var sale = CreateSale(1000, totalSkill: 50, breed: "Sprinter");

        var similarity = PriceEstimator.ComputeSimilarity(target, sale);

        var targetNullBreed = CreateRequest(totalSkill: 50, breed: null);
        var saleNullBreed = CreateSale(1000, totalSkill: 50, breed: null);
        var baselineSimilarity = PriceEstimator.ComputeSimilarity(targetNullBreed, saleNullBreed);

        similarity.Should().BeGreaterThan(baselineSimilarity);
    }

    [Fact]
    public void Null_breed_does_not_affect_similarity()
    {
        var target = CreateRequest(totalSkill: 50, breed: null);
        var sale = CreateSale(1000, totalSkill: 50, breed: "Sprinter");

        var withBreed = PriceEstimator.ComputeSimilarity(target, sale);

        var targetNullBreed = CreateRequest(totalSkill: 50, breed: null);
        var saleNullBreed = CreateSale(1000, totalSkill: 50, breed: null);
        var withoutBreed = PriceEstimator.ComputeSimilarity(targetNullBreed, saleNullBreed);

        withBreed.Should().Be(withoutBreed);
    }

    [Fact]
    public void Same_age_gives_higher_similarity_than_different_age()
    {
        var target = CreateRequest(totalSkill: 50, ageMonths: 12);
        var sameAge = CreateSale(1000, totalSkill: 50, ageMonths: 12);
        var differentAge = CreateSale(1000, totalSkill: 50, ageMonths: 36);

        var sameSimilarity = PriceEstimator.ComputeSimilarity(target, sameAge);
        var diffSimilarity = PriceEstimator.ComputeSimilarity(target, differentAge);

        sameSimilarity.Should().BeGreaterThan(diffSimilarity);
    }

    [Fact]
    public void Larger_age_gap_reduces_similarity_more()
    {
        var target = CreateRequest(totalSkill: 50, ageMonths: 12);
        var smallGap = CreateSale(1000, totalSkill: 50, ageMonths: 18);
        var largeGap = CreateSale(1000, totalSkill: 50, ageMonths: 48);

        var smallGapSim = PriceEstimator.ComputeSimilarity(target, smallGap);
        var largeGapSim = PriceEstimator.ComputeSimilarity(target, largeGap);

        smallGapSim.Should().BeGreaterThan(largeGapSim);
    }

    [Fact]
    public void Young_target_vs_old_comparable_has_lower_similarity_than_reverse()
    {
        var youngTarget = CreateRequest(totalSkill: 50, ageMonths: 6);
        var oldTarget = CreateRequest(totalSkill: 50, ageMonths: 30);
        var oldComparable = CreateSale(1000, totalSkill: 50, ageMonths: 30);
        var youngComparable = CreateSale(1000, totalSkill: 50, ageMonths: 6);

        var youngVsOld = PriceEstimator.ComputeSimilarity(youngTarget, oldComparable);
        var oldVsYoung = PriceEstimator.ComputeSimilarity(oldTarget, youngComparable);

        youngVsOld.Should().BeLessThan(oldVsYoung,
            "a young pigeon compared to an older one is less trustworthy");
    }

    [Fact]
    public void Null_age_skips_age_similarity_component()
    {
        var target = CreateRequest(totalSkill: 50, ageMonths: null);
        var withAge = CreateSale(1000, totalSkill: 50, ageMonths: 24);
        var withoutAge = CreateSale(1000, totalSkill: 50, ageMonths: null);

        var sim1 = PriceEstimator.ComputeSimilarity(target, withAge);
        var sim2 = PriceEstimator.ComputeSimilarity(target, withoutAge);

        sim1.Should().Be(sim2);
    }

    [Fact]
    public void Comparables_returned_ordered_by_similarity()
    {
        var request = CreateRequest(totalSkill: 60, speed: 8);

        var sales = new[]
        {
            CreateSale(500, totalSkill: 30, speed: 2, pigeonName: "Distant"),
            CreateSale(1500, totalSkill: 60, speed: 8, pigeonName: "Close"),
            CreateSale(1000, totalSkill: 50, speed: 6, pigeonName: "Middle"),
        };

        var result = PriceEstimator.Estimate(request, sales);

        result.Should().NotBeNull();
        result!.Comparables.Should().HaveCount(3);
        result.Comparables[0].PigeonName.Should().Be("Close");
        result.Comparables[^1].PigeonName.Should().Be("Distant");
    }

    [Fact]
    public void Comparables_include_identity_and_adjusted_price()
    {
        var request = CreateRequest(totalSkill: 50, ageMonths: 6);
        var sales = new[] { CreateSale(1000, totalSkill: 50, ageMonths: 30, pigeonName: "Test Pigeon", transferId: 42) };

        var result = PriceEstimator.Estimate(request, sales);

        result.Should().NotBeNull();
        var comp = result!.Comparables.Should().ContainSingle().Subject;
        comp.PigeonName.Should().Be("Test Pigeon");
        comp.TransferId.Should().Be(42);
        comp.SoldPrice.Should().Be(1000);
        comp.AdjustedPrice.Should().Be((decimal)(1000 * 1.24));
    }

    [Fact]
    public void Comparables_capped_at_max_count()
    {
        var request = CreateRequest();
        var sales = Enumerable.Range(0, 15)
            .Select(i => CreateSale(800 + i * 10, pigeonName: $"Pigeon {i}"))
            .ToList();

        var result = PriceEstimator.Estimate(request, sales);

        result.Should().NotBeNull();
        result!.Comparables.Should().HaveCount(10);
    }

    [Fact]
    public void Price_range_reflects_age_adjusted_prices()
    {
        var request = CreateRequest(totalSkill: 50, ageMonths: 6);
        var sales = new[]
        {
            CreateSale(800, totalSkill: 50, ageMonths: 30),
            CreateSale(1200, totalSkill: 50, ageMonths: 30),
            CreateSale(1000, totalSkill: 50, ageMonths: 30),
        };

        var result = PriceEstimator.Estimate(request, sales);

        result.Should().NotBeNull();
        // 24 months younger → factor 1.24
        result!.MinComparablePrice.Should().Be((decimal)(800 * 1.24));
        result.MaxComparablePrice.Should().Be((decimal)(1200 * 1.24));
    }
}
