using FluentAssertions;
using PigeonFancierTracker.Core.Analytics;
using PigeonFancierTracker.Core.Contracts;
using PigeonFancierTracker.Core.Domain;
using Xunit;

namespace PigeonFancierTracker.Core.Tests;

public sealed class BreedSkillCorrelationCalculatorTests
{
    [Fact]
    public void Empty_results_returns_empty_analysis()
    {
        var result = BreedSkillCorrelationCalculator.Calculate([], new Dictionary<int, PigeonSkillsDto>());

        result.Breeds.Should().BeEmpty();
    }

    [Fact]
    public void Pigeons_without_skill_data_are_ignored()
    {
        var results = new[] { MakeResult(1, 5, 100, "A", pigeonId: 100) };

        var result = BreedSkillCorrelationCalculator.Calculate(results, new Dictionary<int, PigeonSkillsDto>());

        result.Breeds.Should().BeEmpty();
    }

    [Fact]
    public void Higher_skill_with_better_placing_yields_positive_correlation()
    {
        // Speed rises as placing improves (quality = 100 - percentile), so the
        // correlation should be strongly positive and top the ranking.
        var results = new[]
        {
            MakeResult(1, 1, 100, "A", pigeonId: 100),
            MakeResult(2, 50, 100, "A", pigeonId: 101),
            MakeResult(3, 99, 100, "A", pigeonId: 102),
        };
        var skills = new Dictionary<int, PigeonSkillsDto>
        {
            [100] = MakeSkills(speed: 90, stamina: 50, total: 200),
            [101] = MakeSkills(speed: 50, stamina: 50, total: 150),
            [102] = MakeSkills(speed: 10, stamina: 50, total: 100),
        };

        var result = BreedSkillCorrelationCalculator.Calculate(results, skills);

        var profile = result.Breeds.Single();
        profile.PigeonCount.Should().Be(3);
        profile.TopSkillDisplay.Should().Be("Snelheid");

        var speed = profile.SkillCorrelations.Single(c => c.SkillName == "speed");
        speed.Correlation.Should().BeApproximately(1.0, 0.01);
        speed.IsReliable.Should().BeTrue();
    }

    [Fact]
    public void Constant_skill_has_zero_correlation()
    {
        var results = new[]
        {
            MakeResult(1, 1, 100, "A", pigeonId: 100),
            MakeResult(2, 50, 100, "A", pigeonId: 101),
            MakeResult(3, 99, 100, "A", pigeonId: 102),
        };
        var skills = new Dictionary<int, PigeonSkillsDto>
        {
            [100] = MakeSkills(speed: 90, stamina: 50, total: 200),
            [101] = MakeSkills(speed: 50, stamina: 50, total: 150),
            [102] = MakeSkills(speed: 10, stamina: 50, total: 100),
        };

        var result = BreedSkillCorrelationCalculator.Calculate(results, skills);

        var stamina = result.Breeds.Single().SkillCorrelations.Single(c => c.SkillName == "stamina");
        stamina.Correlation.Should().Be(0);
    }

    [Fact]
    public void Fewer_than_three_pigeons_is_not_reliable()
    {
        var results = new[]
        {
            MakeResult(1, 1, 100, "A", pigeonId: 100),
            MakeResult(2, 50, 100, "A", pigeonId: 101),
        };
        var skills = new Dictionary<int, PigeonSkillsDto>
        {
            [100] = MakeSkills(speed: 90, stamina: 50, total: 200),
            [101] = MakeSkills(speed: 50, stamina: 50, total: 150),
        };

        var result = BreedSkillCorrelationCalculator.Calculate(results, skills);
        var profile = result.Breeds.Single();

        profile.SkillCorrelations.Should().OnlyContain(c => !c.IsReliable);
        profile.TopSkillDisplay.Should().BeNull();
    }

    [Fact]
    public void Average_total_skill_is_computed_per_distinct_pigeon()
    {
        // Pigeon 100 flies twice; its Total must not be double-counted.
        var results = new[]
        {
            MakeResult(1, 1, 100, "A", pigeonId: 100),
            MakeResult(2, 10, 100, "A", pigeonId: 100),
            MakeResult(3, 50, 100, "A", pigeonId: 101),
            MakeResult(4, 90, 100, "A", pigeonId: 102),
        };
        var skills = new Dictionary<int, PigeonSkillsDto>
        {
            [100] = MakeSkills(speed: 90, stamina: 50, total: 300),
            [101] = MakeSkills(speed: 50, stamina: 50, total: 150),
            [102] = MakeSkills(speed: 10, stamina: 50, total: 150),
        };

        var result = BreedSkillCorrelationCalculator.Calculate(results, skills);

        result.Breeds.Single().AvgTotalSkill.Should().Be(200m);
    }

    private static FlightResultListItem MakeResult(
        int flightId, int position, int total, string breed, int pigeonId)
    {
        var percentile = total > 0 ? Math.Round((double)position / total * 100, 1) : 0;
        return new FlightResultListItem(
            flightId,
            new DateTime(2026, 7, 1).AddDays(flightId),
            "regional",
            "Test",
            200,
            DistanceCategory.Middle,
            position,
            total,
            percentile,
            10,
            85m,
            "TestPigeon",
            pigeonId,
            null,
            breed);
    }

    private static PigeonSkillsDto MakeSkills(decimal speed, decimal stamina, decimal total) =>
        new(Form: null, Experience: null, Speed: speed, Technique: null, Stamina: stamina,
            Aerodynamics: null, Intelligence: null, Libido: null, Nightvision: null,
            Navigation: null, Total: total);
}
