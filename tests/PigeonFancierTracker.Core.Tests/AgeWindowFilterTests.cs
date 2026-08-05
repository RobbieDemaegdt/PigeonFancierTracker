using FluentAssertions;
using PigeonFancierTracker.Core.Analytics;

namespace PigeonFancierTracker.Core.Tests;

public sealed class AgeWindowFilterTests
{
    private sealed record TestItem(int? AgeMonths);

    private static IReadOnlyList<TestItem> Items(params int?[] ages) =>
        ages.Select(a => new TestItem(a)).ToList();

    [Fact]
    public void Newborn_returns_exact_match_only()
    {
        var items = Items(0, 0, 0, 3, 6, 12);

        var result = AgeWindowFilter.Filter(0, items, x => x.AgeMonths);

        result.Items.Should().HaveCount(3);
        result.Items.Should().OnlyContain(x => x.AgeMonths == 0);
        result.WindowMonths.Should().Be(0);
    }

    [Fact]
    public void Newborn_widens_when_not_enough_exact_matches()
    {
        var items = Items(0, 3, 5, 12, 24);

        var result = AgeWindowFilter.Filter(0, items, x => x.AgeMonths);

        result.Items.Should().HaveCount(3);
        result.Items.Select(x => x.AgeMonths).Should().BeEquivalentTo([0, 3, 5]);
        result.WindowMonths.Should().Be(6);
    }

    [Fact]
    public void Non_newborn_uses_three_month_window()
    {
        var items = Items(9, 10, 11, 12, 13, 14, 15, 20, 30);

        var result = AgeWindowFilter.Filter(12, items, x => x.AgeMonths);

        result.Items.Select(x => x.AgeMonths).Should().BeEquivalentTo([9, 10, 11, 12, 13, 14, 15]);
        result.WindowMonths.Should().Be(3);
    }

    [Fact]
    public void Non_newborn_widens_when_not_enough_comparables()
    {
        var items = Items(5, 12, 30);

        var result = AgeWindowFilter.Filter(12, items, x => x.AgeMonths);

        result.Items.Should().HaveCount(3);
        result.WindowMonths.Should().Be(24);
    }

    [Fact]
    public void Null_target_age_returns_full_list()
    {
        var items = Items(0, 5, 12, 24);

        var result = AgeWindowFilter.Filter(null, items, x => x.AgeMonths);

        result.Items.Should().HaveCount(4);
        result.WindowMonths.Should().BeNull();
    }

    [Fact]
    public void Null_age_items_excluded_from_filtered_results()
    {
        var items = Items(10, 11, null, 12, null, 13);

        var result = AgeWindowFilter.Filter(12, items, x => x.AgeMonths);

        result.Items.Should().OnlyContain(x => x.AgeMonths.HasValue);
        result.WindowMonths.Should().Be(3);
    }

    [Fact]
    public void Falls_back_to_all_with_age_when_max_window_exceeded()
    {
        var items = Items(1, 200);

        var result = AgeWindowFilter.Filter(100, items, x => x.AgeMonths);

        result.Items.Should().HaveCount(2);
        result.WindowMonths.Should().BeNull();
    }

    [Fact]
    public void Custom_min_comparables_respected()
    {
        var items = Items(10, 11, 12, 13, 14);

        var result = AgeWindowFilter.Filter(12, items, x => x.AgeMonths, minComparables: 5);

        result.Items.Should().HaveCount(5);
        result.WindowMonths.Should().Be(3);
    }

    [Fact]
    public void FormatWindow_null_returns_null()
    {
        AgeWindowFilter.FormatWindow(null).Should().BeNull();
    }

    [Fact]
    public void FormatWindow_zero_returns_exact_match_label()
    {
        AgeWindowFilter.FormatWindow(0).Should().Be("0m");
    }

    [Fact]
    public void FormatWindow_positive_returns_plus_minus_label()
    {
        AgeWindowFilter.FormatWindow(3).Should().Be("±3m");
        AgeWindowFilter.FormatWindow(12).Should().Be("±12m");
    }

    [Fact]
    public void Empty_list_returns_empty()
    {
        var result = AgeWindowFilter.Filter(12, Items(), x => x.AgeMonths);

        result.Items.Should().BeEmpty();
        result.WindowMonths.Should().BeNull();
    }

    [Fact]
    public void FilterAtWindow_returns_exact_window_without_widening()
    {
        var items = Items(10, 11, 12, 13, 14, 20, 30);

        var result = AgeWindowFilter.FilterAtWindow(12, items, x => x.AgeMonths, 1);

        result.Items.Select(x => x.AgeMonths).Should().BeEquivalentTo([11, 12, 13]);
        result.WindowMonths.Should().Be(1);
    }

    [Fact]
    public void FilterAtWindow_window_zero_returns_exact_age_only()
    {
        var items = Items(10, 12, 12, 14);

        var result = AgeWindowFilter.FilterAtWindow(12, items, x => x.AgeMonths, 0);

        result.Items.Should().HaveCount(2);
        result.Items.Should().OnlyContain(x => x.AgeMonths == 12);
        result.WindowMonths.Should().Be(0);
    }

    [Fact]
    public void FilterAtWindow_does_not_widen_when_too_few_results()
    {
        var items = Items(5, 30);

        var result = AgeWindowFilter.FilterAtWindow(12, items, x => x.AgeMonths, 1);

        result.Items.Should().BeEmpty();
        result.WindowMonths.Should().Be(1);
    }

    [Fact]
    public void FilterAtWindow_null_target_returns_all_items()
    {
        var items = Items(5, 10, 20);

        var result = AgeWindowFilter.FilterAtWindow(null, items, x => x.AgeMonths, 3);

        result.Items.Should().HaveCount(3);
        result.WindowMonths.Should().BeNull();
    }

    [Fact]
    public void FilterAtWindow_excludes_null_age_items()
    {
        var items = Items(11, null, 12, null, 13);

        var result = AgeWindowFilter.FilterAtWindow(12, items, x => x.AgeMonths, 1);

        result.Items.Should().OnlyContain(x => x.AgeMonths.HasValue);
        result.Items.Should().HaveCount(3);
    }
}
