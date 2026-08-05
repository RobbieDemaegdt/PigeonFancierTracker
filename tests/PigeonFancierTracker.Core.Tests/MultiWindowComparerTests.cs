using FluentAssertions;
using PigeonFancierTracker.Core.Analytics;
using PigeonFancierTracker.Core.Contracts;

namespace PigeonFancierTracker.Core.Tests;

public sealed class MultiWindowComparerTests
{
    private sealed record TestItem(int? AgeMonths, DistanceStats? Stats);

    private static DistanceStats S(decimal s, decimal m, decimal l, decimal t) =>
        new(s, m, l, t);

    [Fact]
    public void Returns_four_rows_with_correct_window_labels()
    {
        var target = S(10, 10, 10, 40);
        var items = new List<TestItem> { new(12, S(8, 8, 8, 32)) };

        var result = MultiWindowComparer.Compare(
            target, 12, items, x => x.AgeMonths, x => x.Stats);

        result.Should().HaveCount(4);
        result.Select(r => r.WindowLabel).Should().BeEquivalentTo(["0m", "±1m", "±3m", "±6m"]);
        result.Select(r => r.WindowMonths).Should().BeEquivalentTo([0, 1, 3, 6]);
    }

    [Fact]
    public void Population_count_reflects_filtering()
    {
        var target = S(10, 10, 10, 40);
        var items = new List<TestItem>
        {
            new(12, S(8, 8, 8, 32)),
            new(13, S(9, 9, 9, 36)),
            new(16, S(7, 7, 7, 28)),
            new(20, S(6, 6, 6, 24)),
        };

        var result = MultiWindowComparer.Compare(
            target, 12, items, x => x.AgeMonths, x => x.Stats);

        result.First(r => r.WindowMonths == 0).PopulationCount.Should().Be(1);
        result.First(r => r.WindowMonths == 1).PopulationCount.Should().Be(2);
        result.First(r => r.WindowMonths == 3).PopulationCount.Should().Be(2);
        result.First(r => r.WindowMonths == 6).PopulationCount.Should().Be(3);
    }

    [Fact]
    public void Percentiles_null_when_population_empty()
    {
        var target = S(10, 10, 10, 40);
        var items = new List<TestItem> { new(50, S(5, 5, 5, 20)) };

        var result = MultiWindowComparer.Compare(
            target, 12, items, x => x.AgeMonths, x => x.Stats);

        var row0m = result.First(r => r.WindowMonths == 0);
        row0m.PopulationCount.Should().Be(0);
        row0m.ShortPercentile.Should().BeNull();
        row0m.MediumPercentile.Should().BeNull();
        row0m.LongPercentile.Should().BeNull();
        row0m.TotalPercentile.Should().BeNull();
    }

    [Fact]
    public void Wider_windows_have_non_decreasing_population_count()
    {
        var target = S(10, 10, 10, 40);
        var items = Enumerable.Range(0, 30)
            .Select(i => new TestItem(i, S(i, i, i, i * 3)))
            .ToList();

        var result = MultiWindowComparer.Compare(
            target, 12, items, x => x.AgeMonths, x => x.Stats);

        for (var i = 1; i < result.Count; i++)
        {
            result[i].PopulationCount.Should()
                .BeGreaterThanOrEqualTo(result[i - 1].PopulationCount);
        }
    }

    [Fact]
    public void Null_target_age_returns_full_population_for_all_windows()
    {
        var target = S(10, 10, 10, 40);
        var items = new List<TestItem>
        {
            new(5, S(3, 3, 3, 12)),
            new(20, S(8, 8, 8, 32)),
            new(50, S(12, 12, 12, 48)),
        };

        var result = MultiWindowComparer.Compare(
            target, null, items, x => x.AgeMonths, x => x.Stats);

        result.Should().OnlyContain(r => r.PopulationCount == 3);
    }

    [Fact]
    public void Percentile_values_match_stats_comparer()
    {
        var target = S(15, 15, 15, 55);
        var items = new List<TestItem>
        {
            new(12, S(10, 10, 10, 40)),
            new(12, S(20, 20, 20, 70)),
        };

        var result = MultiWindowComparer.Compare(
            target, 12, items, x => x.AgeMonths, x => x.Stats);

        var row0m = result.First(r => r.WindowMonths == 0);
        row0m.ShortPercentile.Should().Be(50);
        row0m.TotalPercentile.Should().Be(50);
    }

    [Fact]
    public void Items_with_null_stats_excluded_from_population_count()
    {
        var target = S(10, 10, 10, 40);
        var items = new List<TestItem>
        {
            new(12, S(8, 8, 8, 32)),
            new(12, null),
        };

        var result = MultiWindowComparer.Compare(
            target, 12, items, x => x.AgeMonths, x => x.Stats);

        result.First(r => r.WindowMonths == 0).PopulationCount.Should().Be(1);
    }

    [Fact]
    public void Population_is_null_when_no_member_selector()
    {
        var target = S(10, 10, 10, 40);
        var items = new List<TestItem> { new(12, S(8, 8, 8, 32)) };

        var result = MultiWindowComparer.Compare(
            target, 12, items, x => x.AgeMonths, x => x.Stats);

        result.First(r => r.WindowMonths == 0).Population.Should().BeNull();
    }

    [Fact]
    public void Population_members_returned_when_selector_provided()
    {
        var target = S(10, 10, 10, 40);
        var items = new List<TestItem>
        {
            new(12, S(8, 8, 8, 32)),
            new(13, S(9, 9, 9, 36)),
        };

        PercentilePopulationMember? ToMember(TestItem x) =>
            x.Stats is { } s ? new PercentilePopulationMember(
                $"Pigeon {x.AgeMonths}", null, s.Short, s.Medium, s.Long, s.Total, null, null) : null;

        var result = MultiWindowComparer.Compare(
            target, 12, items, x => x.AgeMonths, x => x.Stats, ToMember);

        var row1m = result.First(r => r.WindowMonths == 1);
        row1m.Population.Should().NotBeNull();
        row1m.Population.Should().HaveCount(2);
        row1m.Population![0].PigeonName.Should().Be("Pigeon 12");
        row1m.Population![1].PigeonName.Should().Be("Pigeon 13");
    }

    [Fact]
    public void Population_members_filtered_by_age_window()
    {
        var target = S(10, 10, 10, 40);
        var items = new List<TestItem>
        {
            new(12, S(8, 8, 8, 32)),
            new(20, S(6, 6, 6, 24)),
        };

        PercentilePopulationMember? ToMember(TestItem x) =>
            x.Stats is { } s ? new PercentilePopulationMember(
                $"Pigeon {x.AgeMonths}", null, s.Short, s.Medium, s.Long, s.Total, null, null) : null;

        var result = MultiWindowComparer.Compare(
            target, 12, items, x => x.AgeMonths, x => x.Stats, ToMember);

        result.First(r => r.WindowMonths == 0).Population.Should().HaveCount(1);
        result.First(r => r.WindowMonths == 6).Population.Should().HaveCount(1);
    }
}
