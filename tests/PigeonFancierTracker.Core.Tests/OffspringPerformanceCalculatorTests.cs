using FluentAssertions;
using PigeonFancierTracker.Core.Analytics;
using static PigeonFancierTracker.Core.Analytics.OffspringPerformanceCalculator;

namespace PigeonFancierTracker.Core.Tests;

public sealed class OffspringPerformanceCalculatorTests
{
    [Fact]
    public void Empty_pairs_returns_empty()
    {
        var result = OffspringPerformanceCalculator.Calculate([]);

        result.Should().BeEmpty();
    }

    [Fact]
    public void Pair_with_no_offspring_skills_shows_dash()
    {
        var pairs = new List<BreedingPairInput>
        {
            new("Cock", 1, 60m, "Hen", 2, 50m, 3, []),
        };

        var result = OffspringPerformanceCalculator.Calculate(pairs);

        result.Should().HaveCount(1);
        result[0].OffspringCount.Should().Be(3);
        result[0].AvgOffspringTotalSkill.Should().BeNull();
        result[0].SkillDeltaDisplay.Should().Be("—");
    }

    [Fact]
    public void Offspring_avg_computed_correctly()
    {
        var pairs = new List<BreedingPairInput>
        {
            new("Cock", 1, 60m, "Hen", 2, 50m, 2, [58m, 62m]),
        };

        var result = OffspringPerformanceCalculator.Calculate(pairs);

        result.Should().HaveCount(1);
        result[0].AvgOffspringTotalSkill.Should().Be(60m);
        result[0].OffspringCount.Should().Be(2);
    }

    [Fact]
    public void Parent_avg_and_delta_computed()
    {
        var pairs = new List<BreedingPairInput>
        {
            new("Cock", 1, 60m, "Hen", 2, 50m, 1, [60m]),
        };

        var result = OffspringPerformanceCalculator.Calculate(pairs);

        result[0].ParentAvgTotalSkill.Should().Be(55m);
        result[0].SkillDelta.Should().Be(5m);
        result[0].SkillDeltaDisplay.Should().Contain("↑");
    }

    [Fact]
    public void Negative_delta_shows_down_arrow()
    {
        var pairs = new List<BreedingPairInput>
        {
            new("Cock", 1, 60m, "Hen", 2, 50m, 1, [40m]),
        };

        var result = OffspringPerformanceCalculator.Calculate(pairs);

        result[0].SkillDelta.Should().Be(-15m);
        result[0].SkillDeltaDisplay.Should().Contain("↓");
    }

    [Fact]
    public void Zero_delta_shows_equals()
    {
        var pairs = new List<BreedingPairInput>
        {
            new("Cock", 1, 60m, "Hen", 2, 40m, 1, [50m]),
        };

        var result = OffspringPerformanceCalculator.Calculate(pairs);

        result[0].SkillDelta.Should().Be(0m);
        result[0].SkillDeltaDisplay.Should().Contain("=");
    }

    [Fact]
    public void Null_parent_skill_yields_dash_display()
    {
        var pairs = new List<BreedingPairInput>
        {
            new("Cock", 1, null, "Hen", 2, 50m, 1, [55m]),
        };

        var result = OffspringPerformanceCalculator.Calculate(pairs);

        result[0].ParentAvgTotalSkill.Should().BeNull();
        result[0].SkillDelta.Should().Be(0m);
        result[0].SkillDeltaDisplay.Should().Be("—");
    }

    [Fact]
    public void Results_sorted_by_delta_descending()
    {
        var pairs = new List<BreedingPairInput>
        {
            new("Worst", 1, 80m, "Pair", 2, 80m, 1, [50m]),
            new("Best", 3, 40m, "Pair", 4, 40m, 1, [60m]),
            new("Mid", 5, 50m, "Pair", 6, 50m, 1, [52m]),
        };

        var result = OffspringPerformanceCalculator.Calculate(pairs);

        result[0].PigeonName.Should().Be("Best");
        result[^1].PigeonName.Should().Be("Worst");
    }
}
