using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace PigeonFancierTracker.Infrastructure.Persistence;

/// <summary>
/// Deserializes a JSON integer property even when the API sends a fractional
/// number. Live flight tracking returns fields such as remainingDistance,
/// distance, progress and direction as decimals while the flight is in
/// progress (e.g. 156.7), but the corresponding DTO fields are typed int.
/// Without this, a single fractional value fails the whole results payload
/// and the active-flight klassement comes back empty. Values are rounded to
/// the nearest int; numbers sent as strings are also tolerated.
/// Applies to int and (via STJ nullable wrapping) int? properties.
/// </summary>
public sealed class LenientIntConverter : JsonConverter<int>
{
    public override int Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        switch (reader.TokenType)
        {
            case JsonTokenType.Number:
                return reader.TryGetInt32(out var i)
                    ? i
                    : (int)Math.Round(reader.GetDouble(), MidpointRounding.AwayFromZero);

            case JsonTokenType.String:
                var s = reader.GetString();
                if (int.TryParse(s, NumberStyles.Any, CultureInfo.InvariantCulture, out var parsed))
                    return parsed;
                if (double.TryParse(s, NumberStyles.Any, CultureInfo.InvariantCulture, out var d))
                    return (int)Math.Round(d, MidpointRounding.AwayFromZero);
                return 0;

            default:
                return 0;
        }
    }

    public override void Write(Utf8JsonWriter writer, int value, JsonSerializerOptions options)
        => writer.WriteNumberValue(value);
}
