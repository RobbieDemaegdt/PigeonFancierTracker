using FluentAssertions;
using PigeonFancierTracker.Core.Analytics;
using PigeonFancierTracker.Core.Contracts;

namespace PigeonFancierTracker.Core.Tests;

public sealed class DiseaseImpactCalculatorTests
{
    private static readonly DateTimeOffset T0 = new(2026, 6, 1, 0, 0, 0, TimeSpan.Zero);

    [Fact]
    public void Empty_snapshots_returns_empty_result()
    {
        var result = DiseaseImpactCalculator.Calculate([]);

        result.Episodes.Should().BeEmpty();
        result.TotalEpisodes.Should().Be(0);
        result.AvgSkillLoss.Should().Be(0);
        result.AvgRecoveryPercentage.Should().BeNull();
    }

    [Fact]
    public void No_disease_snapshots_returns_zero_episodes()
    {
        var snapshots = new List<DiseaseSnapshot>
        {
            new(T0, null, 50m),
            new(T0.AddDays(1), null, 52m),
        };

        var result = DiseaseImpactCalculator.Calculate(snapshots);

        result.TotalEpisodes.Should().Be(0);
    }

    [Fact]
    public void Single_disease_episode_detected()
    {
        var snapshots = new List<DiseaseSnapshot>
        {
            new(T0, null, 50m),
            new(T0.AddDays(1), "Pokken", 45m),
            new(T0.AddDays(2), "Pokken", 42m),
            new(T0.AddDays(3), null, 48m),
        };

        var result = DiseaseImpactCalculator.Calculate(snapshots);

        result.TotalEpisodes.Should().Be(1);
        var ep = result.Episodes[0];
        ep.DiseaseName.Should().Be("Pokken");
        ep.DurationSnapshots.Should().Be(2);
        ep.SkillBefore.Should().Be(50m);
        ep.SkillWorst.Should().Be(42m);
        ep.SkillAfter.Should().Be(48m);
        ep.SkillLoss.Should().Be(8m);
        ep.RecoveryAmount.Should().Be(6m);
        ep.NetImpact.Should().Be(-2m);
    }

    [Fact]
    public void Disease_at_start_uses_first_snapshot_as_before()
    {
        var snapshots = new List<DiseaseSnapshot>
        {
            new(T0, "Pokken", 50m),
            new(T0.AddDays(1), null, 48m),
        };

        var result = DiseaseImpactCalculator.Calculate(snapshots);

        result.TotalEpisodes.Should().Be(1);
        result.Episodes[0].SkillBefore.Should().Be(50m);
        result.Episodes[0].SkillAfter.Should().Be(48m);
    }

    [Fact]
    public void Disease_at_end_has_null_skill_after()
    {
        var snapshots = new List<DiseaseSnapshot>
        {
            new(T0, null, 50m),
            new(T0.AddDays(1), "Pokken", 45m),
        };

        var result = DiseaseImpactCalculator.Calculate(snapshots);

        result.Episodes[0].SkillAfter.Should().BeNull();
        result.Episodes[0].RecoveryAmount.Should().BeNull();
        result.Episodes[0].NetImpact.Should().BeNull();
    }

    [Fact]
    public void Multiple_episodes_tracked_separately()
    {
        var snapshots = new List<DiseaseSnapshot>
        {
            new(T0, null, 60m),
            new(T0.AddDays(1), "Pokken", 55m),
            new(T0.AddDays(2), null, 58m),
            new(T0.AddDays(3), "Griep", 50m),
            new(T0.AddDays(4), null, 56m),
        };

        var result = DiseaseImpactCalculator.Calculate(snapshots);

        result.TotalEpisodes.Should().Be(2);
        result.Episodes[0].DiseaseName.Should().Be("Pokken");
        result.Episodes[1].DiseaseName.Should().Be("Griep");
    }

    [Fact]
    public void Average_skill_loss_computed_across_episodes()
    {
        var snapshots = new List<DiseaseSnapshot>
        {
            new(T0, null, 60m),
            new(T0.AddDays(1), "Pokken", 50m),
            new(T0.AddDays(2), null, 58m),
            new(T0.AddDays(3), "Griep", 52m),
            new(T0.AddDays(4), null, 56m),
        };

        var result = DiseaseImpactCalculator.Calculate(snapshots);

        result.AvgSkillLoss.Should().Be(8m);
    }

    [Fact]
    public void Recovery_percentage_computed_correctly()
    {
        var snapshots = new List<DiseaseSnapshot>
        {
            new(T0, null, 60m),
            new(T0.AddDays(1), "Pokken", 50m),
            new(T0.AddDays(2), null, 55m),
        };

        var result = DiseaseImpactCalculator.Calculate(snapshots);

        result.AvgRecoveryPercentage.Should().Be(50.0m);
    }

    [Fact]
    public void Snapshots_sorted_by_time_regardless_of_input_order()
    {
        var snapshots = new List<DiseaseSnapshot>
        {
            new(T0.AddDays(2), null, 48m),
            new(T0, null, 50m),
            new(T0.AddDays(1), "Pokken", 45m),
        };

        var result = DiseaseImpactCalculator.Calculate(snapshots);

        result.TotalEpisodes.Should().Be(1);
        result.Episodes[0].SkillBefore.Should().Be(50m);
    }
}
