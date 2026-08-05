using FluentAssertions;
using PigeonFancierTracker.Core.Analytics;
using PigeonFancierTracker.Core.Contracts;

namespace PigeonFancierTracker.Core.Tests;

public sealed class MarketTrendCalculatorTests
{
    [Fact]
    public void Empty_sales_returns_insufficient_data()
    {
        var result = MarketTrendCalculator.Calculate([]);

        result.TrendDirection.Should().Be("Onvoldoende data");
        result.Buckets.Should().BeEmpty();
    }

    [Fact]
    public void Zero_skill_sales_filtered_out()
    {
        var sales = new List<MarketSalePoint>
        {
            new(100m, 0m, DateTimeOffset.UtcNow),
        };

        var result = MarketTrendCalculator.Calculate(sales);

        result.TrendDirection.Should().Be("Onvoldoende data");
    }

    [Fact]
    public void Single_bucket_shows_stabiel()
    {
        var baseDate = new DateTimeOffset(2026, 6, 15, 12, 0, 0, TimeSpan.Zero);
        var sales = new List<MarketSalePoint>
        {
            new(100m, 50m, baseDate),
            new(200m, 100m, baseDate.AddHours(1)),
        };

        var result = MarketTrendCalculator.Calculate(sales);

        result.TrendDirection.Should().Be("Stabiel");
        result.Buckets.Should().HaveCount(1);
        result.CurrentAvgPricePerSkill.Should().Be(2m);
    }

    [Fact]
    public void Rising_trend_detected()
    {
        var week1 = new DateTimeOffset(2026, 6, 1, 12, 0, 0, TimeSpan.Zero);
        var week2 = new DateTimeOffset(2026, 6, 8, 12, 0, 0, TimeSpan.Zero);

        var sales = new List<MarketSalePoint>
        {
            new(100m, 50m, week1),
            new(200m, 50m, week2),
        };

        var result = MarketTrendCalculator.Calculate(sales);

        result.TrendDirection.Should().Be("Stijgend");
        result.TrendPercentage.Should().BeGreaterThan(5);
    }

    [Fact]
    public void Falling_trend_detected()
    {
        var week1 = new DateTimeOffset(2026, 6, 1, 12, 0, 0, TimeSpan.Zero);
        var week2 = new DateTimeOffset(2026, 6, 8, 12, 0, 0, TimeSpan.Zero);

        var sales = new List<MarketSalePoint>
        {
            new(200m, 50m, week1),
            new(100m, 50m, week2),
        };

        var result = MarketTrendCalculator.Calculate(sales);

        result.TrendDirection.Should().Be("Dalend");
        result.TrendPercentage.Should().BeLessThan(-5);
    }

    [Fact]
    public void Stable_trend_within_threshold()
    {
        var week1 = new DateTimeOffset(2026, 6, 1, 12, 0, 0, TimeSpan.Zero);
        var week2 = new DateTimeOffset(2026, 6, 8, 12, 0, 0, TimeSpan.Zero);

        var sales = new List<MarketSalePoint>
        {
            new(100m, 50m, week1),
            new(102m, 50m, week2),
        };

        var result = MarketTrendCalculator.Calculate(sales);

        result.TrendDirection.Should().Be("Stabiel");
    }

    [Fact]
    public void Buckets_grouped_by_iso_week()
    {
        var mon = new DateTimeOffset(2026, 6, 1, 12, 0, 0, TimeSpan.Zero);
        var sameMon = new DateTimeOffset(2026, 6, 3, 12, 0, 0, TimeSpan.Zero);
        var nextWeek = new DateTimeOffset(2026, 6, 8, 12, 0, 0, TimeSpan.Zero);

        var sales = new List<MarketSalePoint>
        {
            new(100m, 50m, mon),
            new(100m, 50m, sameMon),
            new(100m, 50m, nextWeek),
        };

        var result = MarketTrendCalculator.Calculate(sales);

        result.Buckets.Should().HaveCount(2);
    }
}
