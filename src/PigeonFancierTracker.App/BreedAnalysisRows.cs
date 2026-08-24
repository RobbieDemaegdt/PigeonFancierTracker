using PigeonFancierTracker.Core.Contracts;

namespace PigeonFancierTracker.App;

/// <summary>One row of Analysis A: a breed's performance under a single condition bucket.</summary>
public sealed class BreedConditionRow
{
    public required string Breed { get; init; }
    public int PigeonCount { get; init; }
    public required string Dimension { get; init; }
    public required string Bucket { get; init; }
    public int Flights { get; init; }
    public double AvgPercentile { get; init; }
    public double AvgPoints { get; init; }
    public decimal AvgSpeed { get; init; }
    public double Consistency { get; init; }
    public string Reliable { get; init; } = "";

    public static IReadOnlyList<BreedConditionRow> Flatten(BreedAnalysisData data)
    {
        var rows = new List<BreedConditionRow>();
        foreach (var breed in data.Breeds)
        {
            foreach (var condition in breed.Conditions)
            {
                rows.Add(new BreedConditionRow
                {
                    Breed = breed.Breed,
                    PigeonCount = breed.PigeonCount,
                    Dimension = condition.DimensionDisplay,
                    Bucket = condition.Bucket,
                    Flights = condition.Flights,
                    AvgPercentile = condition.AvgPercentile,
                    AvgPoints = condition.AvgPoints,
                    AvgSpeed = condition.AvgSpeed,
                    Consistency = condition.Consistency,
                    Reliable = condition.IsReliable ? "" : "?",
                });
            }
        }

        return rows;
    }
}

/// <summary>One row of Analysis B: how one skill correlates with a breed's results.</summary>
public sealed class BreedSkillRow
{
    public required string Breed { get; init; }
    public int PigeonCount { get; init; }
    public decimal AvgTotalSkill { get; init; }
    public required string Skill { get; init; }
    public double Correlation { get; init; }
    public double AvgValue { get; init; }
    public int SampleSize { get; init; }
    public string Reliable { get; init; } = "";

    public static IReadOnlyList<BreedSkillRow> Flatten(BreedSkillAnalysisData data)
    {
        var rows = new List<BreedSkillRow>();
        foreach (var breed in data.Breeds)
        {
            foreach (var skill in breed.SkillCorrelations)
            {
                rows.Add(new BreedSkillRow
                {
                    Breed = breed.Breed,
                    PigeonCount = breed.PigeonCount,
                    AvgTotalSkill = breed.AvgTotalSkill,
                    Skill = skill.SkillDisplay,
                    Correlation = skill.Correlation,
                    AvgValue = skill.AvgValue,
                    SampleSize = skill.SampleSize,
                    Reliable = skill.IsReliable ? "" : "?",
                });
            }
        }

        return rows;
    }
}
