namespace PigeonFancierTracker.Core.Contracts;

// --- Analysis A: descriptive breed × condition performance ---

/// <summary>
/// The kind of condition a <see cref="BreedConditionPerformance"/> bucket slices on.
/// </summary>
public enum ConditionDimension
{
    Distance,
    DayNight,
    Wind,
    Temperature,
}

/// <summary>
/// How one breed performs under a single condition bucket (e.g. long-distance,
/// night flights, or windy weather). Percentile is "lower is better" (1 = winner).
/// </summary>
public sealed record BreedConditionPerformance(
    ConditionDimension Dimension,
    string Bucket,
    int Flights,
    int Pigeons,
    double AvgPercentile,
    double AvgPoints,
    decimal AvgSpeed,
    double Consistency,
    bool IsReliable)
{
    public string DimensionDisplay => Dimension switch
    {
        ConditionDimension.Distance => "Afstand",
        ConditionDimension.DayNight => "Dag/Nacht",
        ConditionDimension.Wind => "Wind",
        ConditionDimension.Temperature => "Temperatuur",
        _ => Dimension.ToString(),
    };

    public string ConditionDisplay => $"{DimensionDisplay}: {Bucket}";
}

public sealed record BreedProfile(
    string Breed,
    int PigeonCount,
    int TotalFlights,
    double OverallAvgPercentile,
    string? BestConditionDisplay,
    IReadOnlyList<BreedConditionPerformance> Conditions);

public sealed record BreedAnalysisData(
    IReadOnlyList<BreedProfile> Breeds);

// --- Analysis B: skill correlation per breed ---

/// <summary>
/// Correlation between one skill and result quality for a breed. Positive means
/// higher skill goes with better placings; the magnitude is a Pearson coefficient
/// in [-1, 1] over the breed's result rows.
/// </summary>
public sealed record BreedSkillCorrelation(
    string SkillName,
    string SkillDisplay,
    double Correlation,
    double AvgValue,
    int SampleSize,
    bool IsReliable);

public sealed record BreedSkillProfile(
    string Breed,
    int PigeonCount,
    int ResultCount,
    decimal AvgTotalSkill,
    string? TopSkillDisplay,
    IReadOnlyList<BreedSkillCorrelation> SkillCorrelations);

public sealed record BreedSkillAnalysisData(
    IReadOnlyList<BreedSkillProfile> Breeds);
