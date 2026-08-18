using PigeonFancierTracker.Core.Contracts;

namespace PigeonFancierTracker.App;

public sealed class FoodMixPerformanceRow
{
    public FoodMix Mix { get; set; }
    public int FlightCount { get; set; }
    public double AvgPercentile { get; set; }
    public double AvgPoints { get; set; }
    public decimal AvgSpeed { get; set; }
    public double Consistency { get; set; }
    public string? Comment { get; set; }
    public FoodCategoryStats? Short { get; set; }
    public FoodCategoryStats? Middle { get; set; }
    public FoodCategoryStats? Long { get; set; }

    public FoodMixPerformanceRow(FoodMixPerformance p)
    {
        Mix = p.Mix;
        FlightCount = p.FlightCount;
        AvgPercentile = p.AvgPercentile;
        AvgPoints = p.AvgPoints;
        AvgSpeed = p.AvgSpeed;
        Consistency = p.Consistency;
        Comment = p.Comment;
        Short = p.Short;
        Middle = p.Middle;
        Long = p.Long;
    }
}
