using PigeonFancierTracker.Core.Domain;

namespace PigeonFancierTracker.Core.Contracts;

public sealed record FoodCategoryStats(
    int FlightCount,
    double AvgPercentile,
    double AvgPoints,
    decimal AvgSpeed);

public sealed record FoodMixPerformance(
    FoodMix Mix,
    int FlightCount,
    double AvgPercentile,
    double AvgPoints,
    decimal AvgSpeed,
    double Consistency,
    string? Comment,
    FoodCategoryStats? Short,
    FoodCategoryStats? Middle,
    FoodCategoryStats? Long);

public sealed record FoodImpactAnalysis(
    IReadOnlyList<FoodMixPerformance> MixPerformances);
