using FluentAssertions;
using PigeonFancierTracker.Core.Analytics;
using PigeonFancierTracker.Core.Contracts;

namespace PigeonFancierTracker.Core.Tests;

public sealed class InbreedingCalculatorTests
{
    [Fact]
    public void Null_tree_returns_zero_with_message()
    {
        var result = InbreedingCalculator.Calculate(1, "Pigeon", null);

        result.PigeonId.Should().Be(1);
        result.PigeonName.Should().Be("Pigeon");
        result.LineageDepth.Should().Be(0);
        result.CommonAncestorCount.Should().Be(0);
        result.InbreedingCoefficient.Should().Be(0);
        result.InbreedingDisplay.Should().Be("Geen stamboom");
    }

    [Fact]
    public void No_common_ancestors_yields_zero_coefficient()
    {
        var tree = new PedigreeNodeDto(1, null, null, "M",
            ParentCock: new PedigreeNodeDto(2, null, null, "M", null, null),
            ParentHen: new PedigreeNodeDto(3, null, null, "F", null, null));

        var result = InbreedingCalculator.Calculate(1, "Pigeon", tree);

        result.InbreedingCoefficient.Should().Be(0);
        result.InbreedingDisplay.Should().Be("Geen (0%)");
        result.LineageDepth.Should().Be(2);
    }

    [Fact]
    public void Depth_computed_correctly()
    {
        var tree = new PedigreeNodeDto(1, null, null, "M",
            ParentCock: new PedigreeNodeDto(2, null, null, "M",
                ParentCock: new PedigreeNodeDto(4, null, null, "M", null, null),
                ParentHen: null),
            ParentHen: new PedigreeNodeDto(3, null, null, "F", null, null));

        var result = InbreedingCalculator.Calculate(1, "Pigeon", tree);

        result.LineageDepth.Should().Be(3);
    }

    [Fact]
    public void Half_sibling_mating_detects_common_ancestor()
    {
        var grandpa = new PedigreeNodeDto(10, null, null, "M", null, null);

        var tree = new PedigreeNodeDto(1, null, null, "M",
            ParentCock: new PedigreeNodeDto(2, null, null, "M",
                ParentCock: grandpa,
                ParentHen: new PedigreeNodeDto(5, null, null, "F", null, null)),
            ParentHen: new PedigreeNodeDto(3, null, null, "F",
                ParentCock: grandpa,
                ParentHen: new PedigreeNodeDto(6, null, null, "F", null, null)));

        var result = InbreedingCalculator.Calculate(1, "Pigeon", tree);

        result.CommonAncestorCount.Should().BeGreaterThan(0);
        result.InbreedingCoefficient.Should().BeGreaterThan(0);
    }

    [Fact]
    public void Wright_coefficient_half_sibling_is_0_0625()
    {
        var grandpa = new PedigreeNodeDto(10, null, null, "M", null, null);

        var tree = new PedigreeNodeDto(1, null, null, "M",
            ParentCock: new PedigreeNodeDto(2, null, null, "M",
                ParentCock: grandpa,
                ParentHen: new PedigreeNodeDto(5, null, null, "F", null, null)),
            ParentHen: new PedigreeNodeDto(3, null, null, "F",
                ParentCock: grandpa,
                ParentHen: new PedigreeNodeDto(6, null, null, "F", null, null)));

        var result = InbreedingCalculator.Calculate(1, "Pigeon", tree);

        result.InbreedingCoefficient.Should().Be(0.0312);
    }

    [Fact]
    public void Coefficient_below_6_25_is_laag()
    {
        var greatGrandpa = new PedigreeNodeDto(20, null, null, "M", null, null);

        var tree = new PedigreeNodeDto(1, null, null, "M",
            ParentCock: new PedigreeNodeDto(2, null, null, "M",
                ParentCock: new PedigreeNodeDto(4, null, null, "M",
                    ParentCock: greatGrandpa,
                    ParentHen: null),
                ParentHen: null),
            ParentHen: new PedigreeNodeDto(3, null, null, "F",
                ParentCock: new PedigreeNodeDto(5, null, null, "M",
                    ParentCock: greatGrandpa,
                    ParentHen: null),
                ParentHen: null));

        var result = InbreedingCalculator.Calculate(1, "Pigeon", tree);

        result.InbreedingCoefficient.Should().BeLessThan(0.0625);
        result.InbreedingDisplay.Should().Contain("Laag");
    }

    [Fact]
    public void Display_categories_correct()
    {
        var result0 = InbreedingCalculator.Calculate(1, "A", null);
        result0.InbreedingDisplay.Should().Be("Geen stamboom");

        var noInbreeding = new PedigreeNodeDto(1, null, null, "M",
            new PedigreeNodeDto(2, null, null, "M", null, null),
            new PedigreeNodeDto(3, null, null, "F", null, null));
        var result1 = InbreedingCalculator.Calculate(1, "B", noInbreeding);
        result1.InbreedingDisplay.Should().Contain("Geen");
    }

    [Fact]
    public void Leaf_node_tree_has_zero_inbreeding()
    {
        var tree = new PedigreeNodeDto(1, null, null, "M", null, null);

        var result = InbreedingCalculator.Calculate(1, "Pigeon", tree);

        result.InbreedingCoefficient.Should().Be(0);
        result.LineageDepth.Should().Be(1);
    }
}
