using System.Text.Json.Serialization;

namespace PigeonFancierTracker.Core.Contracts;

public sealed record WeatherForecastDto(
    [property: JsonPropertyName("date")] DateTime? Date,
    [property: JsonPropertyName("temperature")] decimal? Temperature,
    [property: JsonPropertyName("humidity")] decimal? Humidity,
    [property: JsonPropertyName("wind")] decimal? Wind,
    [property: JsonPropertyName("beaufort")] int? Beaufort,
    [property: JsonPropertyName("day")] bool? Day,
    [property: JsonPropertyName("condition")] string? Condition,
    [property: JsonPropertyName("sunrise")] string? Sunrise,
    [property: JsonPropertyName("sunset")] string? Sunset);
