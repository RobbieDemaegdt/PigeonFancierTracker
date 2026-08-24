using FluentAssertions;
using PigeonFancierTracker.Core.Analytics;
using PigeonFancierTracker.Core.Contracts;

namespace PigeonFancierTracker.Core.Tests;

public sealed class PrizeCalculatorTests
{
    [Fact]
    public void CalculateAgeCategoryPrizes_builds_one_entry_per_populated_category_in_order()
    {
        var prizes = PrizeCalculator.CalculateAgeCategoryPrizes(
            (AgeCategory.Elder, 768),
            (AgeCategory.Yearling, 300),
            (AgeCategory.Youth, 120));

        prizes.Select(p => p.Category).Should()
            .Equal(AgeCategory.Elder, AgeCategory.Yearling, AgeCategory.Youth);
        prizes.Select(p => p.Participants).Should().Equal(768, 300, 120);
    }

    [Fact]
    public void CalculateAgeCategoryPrizes_omits_null_zero_and_negative_categories()
    {
        var prizes = PrizeCalculator.CalculateAgeCategoryPrizes(
            (AgeCategory.Elder, null),
            (AgeCategory.Yearling, 0),
            (AgeCategory.Youth, -5));

        prizes.Should().BeEmpty();
    }

    [Fact]
    public void CalculateAgeCategoryPrizes_pays_a_flat_ten_euro_per_point()
    {
        var elder = PrizeCalculator
            .CalculateAgeCategoryPrizes((AgeCategory.Elder, 768))
            .Single();

        // Every position's money is exactly its points times the flat rate.
        foreach (var tier in elder.PrizeTable)
            tier.PrizeMoneyPerPosition.Should()
                .Be(tier.PointsPerPosition * PrizeCalculator.PrizeMoneyPerPoint);

        // National first place is 150 points -> 1500 euro.
        elder.PrizeTable[0].PointsPerPosition.Should().Be(150);
        elder.PrizeTable[0].PrizeMoneyPerPosition.Should().Be(1500m);
    }

    [Fact]
    public void CalculateAgeCategoryPrizes_positions_and_total_money_match_the_national_table()
    {
        // 768 elder pigeons -> national bands: 3 top places, then 5%, 10%, 10%.
        //   top 3:            150 + 120 + 90                =  360 pts
        //   +5%  = 39 places  x 60                          = 2340 pts
        //   +10% = 77 places  x 24                          = 1848 pts
        //   +10% = 77 places  x 12                          =  924 pts
        // positions = 3 + 39 + 77 + 77 = 196; money = pts x 10 = 54_720 euro.
        var elder = PrizeCalculator
            .CalculateAgeCategoryPrizes((AgeCategory.Elder, 768))
            .Single();

        elder.PrizePositions.Should().Be(196);
        elder.PrizePositions.Should().Be(PrizeCalculator.GetTotalPrizePositions(768));
        elder.TotalPrizeMoney.Should().Be(54_720m);
        elder.TotalPrizeMoney.Should()
            .Be(elder.PrizeTable.Sum(t => t.Count * t.PrizeMoneyPerPosition));
    }

    [Fact]
    public void CalculateAgeCategoryPrizes_uses_the_category_count_not_the_combined_field()
    {
        // A small category caps its own positions on its own head-count.
        var youth = PrizeCalculator
            .CalculateAgeCategoryPrizes((AgeCategory.Youth, 2))
            .Single();

        youth.Participants.Should().Be(2);
        youth.PrizePositions.Should().Be(2);
        youth.PrizeTable.Sum(t => t.Count).Should().Be(2);
    }
}
