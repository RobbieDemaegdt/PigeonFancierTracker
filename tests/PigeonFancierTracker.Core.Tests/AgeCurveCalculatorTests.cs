using FluentAssertions;
using PigeonFancierTracker.Core.Analytics;
using PigeonFancierTracker.Core.Contracts;

namespace PigeonFancierTracker.Core.Tests;

public sealed class AgeCurveCalculatorTests
{
    [Fact]
    public void Empty_points_returns_empty_result()
    {
        var result = AgeCurveCalculator.Calculate([]);

        result.Buckets.Should().BeEmpty();
        result.PeakSkillAge.Should().BeNull();
        result.PeakPerformanceAge.Should().BeNull();
        result.PeakTotalSkill.Should().BeNull();
        result.PhaseDisplay.Should().BeNull();
    }

    [Fact]
    public void Single_point_yields_one_bucket_no_phase()
    {
        var points = new List<AgeCurvePoint>
        {
            new(6, 30m, null),
        };

        var result = AgeCurveCalculator.Calculate(points);

        result.Buckets.Should().HaveCount(1);
        result.PeakSkillAge.Should().Be(6);
        result.PeakTotalSkill.Should().Be(30m);
        result.PhaseDisplay.Should().BeNull();
    }

    [Fact]
    public void Buckets_grouped_by_age_months()
    {
        var points = new List<AgeCurvePoint>
        {
            new(6, 30m, null),
            new(6, 32m, null),
            new(12, 50m, null),
        };

        var result = AgeCurveCalculator.Calculate(points);

        result.Buckets.Should().HaveCount(2);
        result.Buckets[0].AgeMonths.Should().Be(6);
        result.Buckets[0].AvgTotalSkill.Should().Be(31m);
        result.Buckets[0].ObservationCount.Should().Be(2);
        result.Buckets[1].AgeMonths.Should().Be(12);
    }

    [Fact]
    public void Peak_skill_age_is_month_with_highest_avg()
    {
        var points = new List<AgeCurvePoint>
        {
            new(6, 30m, null),
            new(12, 60m, null),
            new(18, 55m, null),
        };

        var result = AgeCurveCalculator.Calculate(points);

        result.PeakSkillAge.Should().Be(12);
        result.PeakTotalSkill.Should().Be(60m);
    }

    [Fact]
    public void Peak_performance_age_uses_lowest_percentile()
    {
        var points = new List<AgeCurvePoint>
        {
            new(6, 30m, 50.0),
            new(12, 40m, 10.0),
            new(18, 35m, 30.0),
        };

        var result = AgeCurveCalculator.Calculate(points);

        result.PeakPerformanceAge.Should().Be(12);
    }

    [Fact]
    public void Phase_display_growth_only_when_all_before_peak()
    {
        var points = new List<AgeCurvePoint>
        {
            new(6, 30m, null),
            new(12, 60m, null),
        };

        var result = AgeCurveCalculator.Calculate(points);

        result.PhaseDisplay.Should().Be("Groei (6-12m)");
    }

    [Fact]
    public void Phase_display_decline_only_when_all_after_peak()
    {
        var points = new List<AgeCurvePoint>
        {
            new(18, 60m, null),
            new(24, 50m, null),
        };

        var result = AgeCurveCalculator.Calculate(points);

        result.PhaseDisplay.Should().Be("Daling (18-24m)");
    }

    [Fact]
    public void Phase_display_full_lifecycle()
    {
        var points = new List<AgeCurvePoint>
        {
            new(6, 30m, null),
            new(12, 60m, null),
            new(18, 50m, null),
        };

        var result = AgeCurveCalculator.Calculate(points);

        result.PhaseDisplay.Should().Be("Groei (6-12m) → Piek (12m) → Daling (12-18m)");
    }

    [Fact]
    public void Percentile_only_averaged_when_present()
    {
        var points = new List<AgeCurvePoint>
        {
            new(6, 30m, null),
            new(6, 32m, 20.0),
        };

        var result = AgeCurveCalculator.Calculate(points);

        result.Buckets[0].AvgPercentile.Should().Be(20.0);
    }

    [Fact]
    public void Buckets_ordered_by_age()
    {
        var points = new List<AgeCurvePoint>
        {
            new(18, 50m, null),
            new(6, 30m, null),
            new(12, 40m, null),
        };

        var result = AgeCurveCalculator.Calculate(points);

        result.Buckets.Select(b => b.AgeMonths).Should().BeInAscendingOrder();
    }
}
