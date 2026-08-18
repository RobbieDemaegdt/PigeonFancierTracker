using FluentAssertions;
using PigeonFancierTracker.Core.Analytics;
using PigeonFancierTracker.Core.Contracts;

namespace PigeonFancierTracker.Core.Tests;

public sealed class SponsorDealScorerTests
{
    [Fact]
    public void ScoreCurrentDeal_empty_history_returns_50()
    {
        var result = SponsorDealScorer.ScoreCurrentDeal(200, 300, 4, []);

        result.Score.Should().Be(50);
        result.Verdict.Should().Be("Gemiddeld");
    }

    [Fact]
    public void ScoreCurrentDeal_high_offer_scores_above_average()
    {
        var history = MakeHistory((100, 100, 2), (120, 150, 3), (110, 120, 2));

        var result = SponsorDealScorer.ScoreCurrentDeal(250, 400, 5, history);

        result.Score.Should().BeGreaterThan(70);
    }

    [Fact]
    public void ScoreCurrentDeal_low_offer_scores_below_average()
    {
        var history = MakeHistory((200, 300, 4), (250, 400, 5), (220, 350, 3));

        var result = SponsorDealScorer.ScoreCurrentDeal(50, 50, 1, history);

        result.Score.Should().BeLessThan(30);
    }

    [Fact]
    public void ScoreCurrentDeal_median_offer_scores_around_50()
    {
        var history = MakeHistory((100, 100, 2), (200, 200, 4), (300, 300, 6));

        var result = SponsorDealScorer.ScoreCurrentDeal(200, 200, 4, history);

        result.Score.Should().BeInRange(30, 70);
    }

    [Fact]
    public void ClassifyDeal_returns_correct_verdicts()
    {
        SponsorDealScorer.ClassifyDeal(90).Should().Be("Uitstekend");
        SponsorDealScorer.ClassifyDeal(80).Should().Be("Uitstekend");
        SponsorDealScorer.ClassifyDeal(70).Should().Be("Goed");
        SponsorDealScorer.ClassifyDeal(60).Should().Be("Goed");
        SponsorDealScorer.ClassifyDeal(50).Should().Be("Gemiddeld");
        SponsorDealScorer.ClassifyDeal(40).Should().Be("Gemiddeld");
        SponsorDealScorer.ClassifyDeal(30).Should().Be("Ondermaats");
        SponsorDealScorer.ClassifyDeal(0).Should().Be("Ondermaats");
    }

    [Fact]
    public void CalculatePercentile_empty_population_returns_50()
    {
        SponsorDealScorer.CalculatePercentile(100, []).Should().Be(50);
    }

    [Fact]
    public void CalculatePercentile_highest_value_returns_high_percentile()
    {
        var population = new List<decimal> { 50, 100, 150, 200 };

        SponsorDealScorer.CalculatePercentile(300, population).Should().Be(100);
    }

    [Fact]
    public void CalculatePercentile_lowest_value_returns_low_percentile()
    {
        var population = new List<decimal> { 50, 100, 150, 200 };

        SponsorDealScorer.CalculatePercentile(10, population).Should().Be(0);
    }

    [Fact]
    public void CalculatePercentile_median_value_returns_around_50()
    {
        var population = new List<decimal> { 50, 100, 150, 200 };

        var result = SponsorDealScorer.CalculatePercentile(100, population);
        result.Should().BeInRange(20, 50);
    }

    [Fact]
    public void CalculateOfferTrend_single_entry_returns_stable()
    {
        var history = MakeHistory((200, 300, 4));

        var (percent, direction) = SponsorDealScorer.CalculateOfferTrend(history);

        direction.Should().Be("Stabiel");
        percent.Should().Be(0);
    }

    [Fact]
    public void CalculateOfferTrend_rising_values_returns_stijgend()
    {
        var history = new List<SponsorHistoryEntry>
        {
            new(100, 100, 2, 10, 300, DateTimeOffset.UtcNow.AddDays(-30)),
            new(110, 100, 2, 10, 320, DateTimeOffset.UtcNow.AddDays(-20)),
            new(200, 200, 3, 15, 800, DateTimeOffset.UtcNow.AddDays(-10)),
            new(250, 300, 4, 17, 1300, DateTimeOffset.UtcNow),
        };

        var (percent, direction) = SponsorDealScorer.CalculateOfferTrend(history);

        direction.Should().Be("Stijgend");
        percent.Should().BeGreaterThan(5);
    }

    [Fact]
    public void CalculateOfferTrend_falling_values_returns_dalend()
    {
        var history = new List<SponsorHistoryEntry>
        {
            new(250, 300, 4, 17, 1300, DateTimeOffset.UtcNow.AddDays(-30)),
            new(200, 200, 3, 15, 800, DateTimeOffset.UtcNow.AddDays(-20)),
            new(110, 100, 2, 10, 320, DateTimeOffset.UtcNow.AddDays(-10)),
            new(100, 100, 2, 10, 300, DateTimeOffset.UtcNow),
        };

        var (percent, direction) = SponsorDealScorer.CalculateOfferTrend(history);

        direction.Should().Be("Dalend");
        percent.Should().BeLessThan(-5);
    }

    [Fact]
    public void CalculateTotalContractValue_computes_correctly()
    {
        SponsorDealScorer.CalculateTotalContractValue(249, 350, 4).Should().Be(249 * 4 + 350);
        SponsorDealScorer.CalculateTotalContractValue(0, 0, 0).Should().Be(0);
        SponsorDealScorer.CalculateTotalContractValue(100, 0, 12).Should().Be(1200);
    }

    [Fact]
    public void CalculateEffectiveMonthly_spreads_direct_over_runtime()
    {
        SponsorDealScorer.CalculateEffectiveMonthly(249, 350, 4).Should().Be(249 + 350m / 4);
        SponsorDealScorer.CalculateEffectiveMonthly(100, 0, 3).Should().Be(100);
        SponsorDealScorer.CalculateEffectiveMonthly(100, 200, 0).Should().Be(100);
    }

    [Fact]
    public void ScoreCurrentDeal_higher_effective_monthly_scores_higher()
    {
        var history = MakeHistory((100, 100, 2), (150, 200, 3), (200, 300, 4));

        var better = SponsorDealScorer.ScoreCurrentDeal(300, 600, 3, history);
        var worse = SponsorDealScorer.ScoreCurrentDeal(120, 60, 6, history);

        better.Score.Should().BeGreaterThan(worse.Score);
    }

    private static List<SponsorHistoryEntry> MakeHistory(params (decimal monthly, decimal direct, int runtime)[] entries) =>
        entries.Select((e, i) => new SponsorHistoryEntry(e.monthly, e.direct, e.runtime, 15, e.monthly * e.runtime + e.direct, DateTimeOffset.UtcNow.AddDays(-entries.Length + i))).ToList();
}
