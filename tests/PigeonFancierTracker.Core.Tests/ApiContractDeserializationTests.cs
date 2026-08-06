using System.Text.Json;
using FluentAssertions;
using PigeonFancierTracker.Core.Contracts;

namespace PigeonFancierTracker.Core.Tests;

public sealed class ApiContractDeserializationTests
{
    [Fact]
    public void Deserializes_the_observed_selected_fancier_shape()
    {
        const string json = """
            {
              "id": 7,
              "displayName": "Robshot",
              "department": 1,
              "location": { "id": 58, "name": "merchtem", "lat": 50.9569, "lng": 4.23417 },
              "pigeonCount": 8,
              "finances": { "balance": 4590, "balancePrevious": 0, "prizes": 0, "other": 3090, "savings": 0 },
              "food": { "barley": 55, "grain": 15, "corn": 15, "peanut": 15 },
              "pen": { "tier": "dilapidated", "dirt": 0 }
            }
            """;

        var selected = JsonSerializer.Deserialize<SelectedFancierDto>(
            json,
            new JsonSerializerOptions { PropertyNameCaseInsensitive = true });

        selected.Should().NotBeNull();
        selected!.Id.Should().Be(7);
        selected.Department.Should().Be(1);
        selected.Location!.Name.Should().Be("merchtem");
        selected.Finances!.Capital.Should().Be(4590);
        selected.Pen.Should().NotBeNull();
        selected.Pen!.Tier.Should().Be("dilapidated");
        selected.Pen.Dirt.Should().Be(0);
    }

    [Fact]
    public void Deserializes_the_observed_pigeon_shape_with_boolean_and_numeric_fields()
    {
        const string json = """
            [{
              "id": 59,
              "sex": false,
              "breed": 3,
              "totalMonths": 72,
              "ageType": 4,
              "years": 6,
              "months": 0,
              "flying": false,
              "premium": 21,
              "skills": { "total": 9 },
              "training": { "technique": false }
            }]
            """;

        var pigeons = JsonSerializer.Deserialize<List<PigeonDto>>(
            json,
            new JsonSerializerOptions { PropertyNameCaseInsensitive = true });

        pigeons.Should().ContainSingle();
        pigeons![0].Sex.Should().Be("false");
        pigeons[0].Breed.Should().Be("3");
        pigeons[0].AgeType.Should().Be("4");
        pigeons[0].Training!.Technique.Should().BeFalse();
        pigeons[0].Skills!.Total.Should().Be(9);
    }
}