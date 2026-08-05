using FluentAssertions;
using PigeonFancierTracker.Core.Analytics;
using PigeonFancierTracker.Core.Contracts;

namespace PigeonFancierTracker.Core.Tests;

public sealed class MultiWindowPriceEstimatorTests
{
    private static PriceEstimationRequest Req(decimal? totalSkill = 50, int? ageMonths = 12) =>
        new(totalSkill, ageMonths, null, 5, 5, 5, 5, 5, 5, 5, 5, 5, 5);

    private static CompletedTransferSummary Sale(
        decimal soldPrice, int? ageMonths = 12, decimal? totalSkill = 50) =>
        new(soldPrice, totalSkill, ageMonths, null, 3, null, null, 5, 5, 5, 5, 5, 5, 5, 5, 5, 5);

    [Fact]
    public void Returns_four_rows_with_correct_window_labels()
    {
        var sales = new List<CompletedTransferSummary> { Sale(1000) };

        var result = MultiWindowPriceEstimator.Estimate(Req(), 12, sales);

        result.Should().HaveCount(4);
        result.Select(r => r.WindowLabel).Should().BeEquivalentTo(["0m", "±1m", "±3m", "±6m"]);
        result.Select(r => r.WindowMonths).Should().BeEquivalentTo([0, 1, 3, 6]);
    }

    [Fact]
    public void Comparable_count_reflects_filtering()
    {
        var sales = new List<CompletedTransferSummary>
        {
            Sale(1000, ageMonths: 12),
            Sale(1200, ageMonths: 13),
            Sale(800, ageMonths: 16),
            Sale(900, ageMonths: 20),
        };

        var result = MultiWindowPriceEstimator.Estimate(Req(), 12, sales);

        result.First(r => r.WindowMonths == 0).ComparableCount.Should().Be(1);
        result.First(r => r.WindowMonths == 1).ComparableCount.Should().Be(2);
        result.First(r => r.WindowMonths == 3).ComparableCount.Should().Be(2);
        result.First(r => r.WindowMonths == 6).ComparableCount.Should().Be(3);
    }

    [Fact]
    public void Null_estimate_when_no_sales_in_window()
    {
        var sales = new List<CompletedTransferSummary> { Sale(1000, ageMonths: 50) };

        var result = MultiWindowPriceEstimator.Estimate(Req(), 12, sales);

        var row0m = result.First(r => r.WindowMonths == 0);
        row0m.ComparableCount.Should().Be(0);
        row0m.EstimatedPrice.Should().BeNull();
        row0m.MinPrice.Should().BeNull();
        row0m.MaxPrice.Should().BeNull();
        row0m.Confidence.Should().BeNull();
    }

    [Fact]
    public void Wider_windows_have_non_decreasing_comparable_count()
    {
        var sales = Enumerable.Range(0, 30)
            .Select(i => Sale(1000 + i * 10, ageMonths: i))
            .ToList();

        var result = MultiWindowPriceEstimator.Estimate(Req(), 12, sales);

        for (var i = 1; i < result.Count; i++)
        {
            result[i].ComparableCount.Should()
                .BeGreaterThanOrEqualTo(result[i - 1].ComparableCount);
        }
    }

    [Fact]
    public void Null_target_age_returns_all_sales_for_all_windows()
    {
        var sales = new List<CompletedTransferSummary>
        {
            Sale(500, ageMonths: 5),
            Sale(1000, ageMonths: 20),
            Sale(1500, ageMonths: 50),
        };

        var result = MultiWindowPriceEstimator.Estimate(Req(ageMonths: null), null, sales);

        result.Should().OnlyContain(r => r.ComparableCount == 3);
    }

    [Fact]
    public void Estimate_matches_price_estimator_for_same_input()
    {
        var sales = new List<CompletedTransferSummary> { Sale(1000, ageMonths: 12) };
        var request = Req();

        var multiResult = MultiWindowPriceEstimator.Estimate(request, 12, sales);
        var directResult = PriceEstimator.Estimate(request, sales);

        var row0m = multiResult.First(r => r.WindowMonths == 0);
        row0m.EstimatedPrice.Should().Be(directResult!.EstimatedPrice);
        row0m.ComparableCount.Should().Be(directResult.ComparableCount);
    }

    [Fact]
    public void Confidence_set_correctly_based_on_comparable_count()
    {
        var sales = Enumerable.Range(0, 15)
            .Select(i => Sale(1000, ageMonths: 12))
            .ToList();

        var result = MultiWindowPriceEstimator.Estimate(Req(), 12, sales);

        var row0m = result.First(r => r.WindowMonths == 0);
        row0m.Confidence.Should().Be(PriceConfidence.High);
    }
}
