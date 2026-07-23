using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace PigeonFancierTracker.Core.Contracts;

public sealed record SeasonDto(
    [property: JsonPropertyName("id")] int? Id,
    [property: JsonPropertyName("season")] int? Number,
    [property: JsonPropertyName("week")] int? Week);

public sealed record UserDto(
    [property: JsonPropertyName("id")] int? Id,
    [property: JsonPropertyName("email")] string? Email,
    [property: JsonPropertyName("username")] string? Username);

public sealed record SelectedFancierDto(
    [property: JsonPropertyName("id")] int? Id,
    [property: JsonPropertyName("displayName")] string? DisplayName,
    [property: JsonPropertyName("department")] int? Department,
    [property: JsonPropertyName("location")] FancierLocationDto? Location,
    [property: JsonPropertyName("pigeonCount")] int? PigeonCount,
    [property: JsonPropertyName("finances")] FinanceSummaryDto? Finances,
    [property: JsonPropertyName("food")] FoodStockDto? Food,
    [property: JsonPropertyName("pen")] PenDto? Pen);

public sealed record FancierLocationDto(
    [property: JsonPropertyName("id")] int? Id,
    [property: JsonPropertyName("name")] string? Name,
    [property: JsonPropertyName("lat")] decimal? Latitude,
    [property: JsonPropertyName("lng")] decimal? Longitude);

public sealed record FinanceSummaryDto(
    [property: JsonPropertyName("balance")] decimal? Capital,
    [property: JsonPropertyName("balancePrevious")] decimal? PreviousBalance,
    [property: JsonPropertyName("prizes")] decimal? Prizes,
    [property: JsonPropertyName("other")] decimal? Other,
    [property: JsonPropertyName("transferBalance")] decimal? TransferBalance,
    [property: JsonPropertyName("savings")] decimal? Savings);

public sealed record FoodStockDto(
    [property: JsonPropertyName("amount")] decimal? Amount,
    [property: JsonPropertyName("value")] decimal? Value);

public sealed record PenDto(
    [property: JsonPropertyName("capacity")] int? Capacity,
    [property: JsonPropertyName("occupied")] int? Occupied);

public sealed record PigeonDto(
    [property: JsonPropertyName("id")] int? Id,
    [property: JsonPropertyName("sex"), JsonConverter(typeof(FlexibleStringJsonConverter))] string? Sex,
    [property: JsonPropertyName("firstNameId")] int? FirstNameId,
    [property: JsonPropertyName("lastNameId")] int? LastNameId,
    [property: JsonPropertyName("birthDateTime")] DateTimeOffset? BirthDateTime,
    [property: JsonPropertyName("breed"), JsonConverter(typeof(FlexibleStringJsonConverter))] string? Breed,
    [property: JsonPropertyName("totalMonths")] int? TotalMonths,
    [property: JsonPropertyName("ageType"), JsonConverter(typeof(FlexibleStringJsonConverter))] string? AgeType,
    [property: JsonPropertyName("years")] int? Years,
    [property: JsonPropertyName("months")] int? Months,
    [property: JsonPropertyName("certificate")] bool? Certificate,
    [property: JsonPropertyName("fancierId")] int? FancierId,
    [property: JsonPropertyName("disease")] string? Disease,
    [property: JsonPropertyName("flying")] bool? Flying,
    [property: JsonPropertyName("premium")] decimal? Premium,
    [property: JsonPropertyName("skills")] PigeonSkillsDto? Skills,
    [property: JsonPropertyName("training")] PigeonTrainingDto? Training);

public sealed record PigeonSkillsDto(
    [property: JsonPropertyName("form")] decimal? Form,
    [property: JsonPropertyName("experience")] decimal? Experience,
    [property: JsonPropertyName("speed")] decimal? Speed,
    [property: JsonPropertyName("technique")] decimal? Technique,
    [property: JsonPropertyName("stamina")] decimal? Stamina,
    [property: JsonPropertyName("aerodynamics")] decimal? Aerodynamics,
    [property: JsonPropertyName("intelligence")] decimal? Intelligence,
    [property: JsonPropertyName("libido")] decimal? Libido,
    [property: JsonPropertyName("nightvision")] decimal? Nightvision,
    [property: JsonPropertyName("navigation")] decimal? Navigation,
    [property: JsonPropertyName("total")] decimal? Total);

public sealed record PigeonTrainingDto(
    [property: JsonPropertyName("speed")] bool? Speed,
    [property: JsonPropertyName("technique")] bool? Technique,
    [property: JsonPropertyName("stamina")] bool? Stamina);

public sealed record FancierInfoDto(
    [property: JsonPropertyName("id")] int? Id,
    [property: JsonPropertyName("displayName")] string? DisplayName,
    [property: JsonPropertyName("class")] int? Class,
    [property: JsonPropertyName("pigeonCount")] int? PigeonCount,
    [property: JsonPropertyName("avatar")] string? Avatar);

public sealed record TransferPigeonDto(
    [property: JsonPropertyName("id")] int? Id,
    [property: JsonPropertyName("sex"), JsonConverter(typeof(FlexibleStringJsonConverter))] string? Sex,
    [property: JsonPropertyName("firstNameId")] int? FirstNameId,
    [property: JsonPropertyName("lastNameId")] int? LastNameId,
    [property: JsonPropertyName("birthDateTime")] DateTimeOffset? BirthDateTime,
    [property: JsonPropertyName("breed"), JsonConverter(typeof(FlexibleStringJsonConverter))] string? Breed,
    [property: JsonPropertyName("totalMonths")] int? TotalMonths,
    [property: JsonPropertyName("years")] int? Years,
    [property: JsonPropertyName("months")] int? Months,
    [property: JsonPropertyName("certificate")] bool? Certificate,
    [property: JsonPropertyName("disease")] string? Disease,
    [property: JsonPropertyName("fancierId")] int? FancierId,
    [property: JsonPropertyName("fancier")] FancierInfoDto? Fancier,
    [property: JsonPropertyName("parentCock")] TransferPigeonDto? ParentCock,
    [property: JsonPropertyName("parentHen")] TransferPigeonDto? ParentHen,
    [property: JsonPropertyName("skills")] PigeonSkillsDto? Skills);

public sealed record TransferItemDto(
    [property: JsonPropertyName("id")] int? Id,
    [property: JsonPropertyName("start")] DateTimeOffset? Start,
    [property: JsonPropertyName("end")] DateTimeOffset? End,
    [property: JsonPropertyName("startPrice")] decimal? StartPrice,
    [property: JsonPropertyName("price")] decimal? Price,
    [property: JsonPropertyName("pigeon")] TransferPigeonDto? Pigeon,
    [property: JsonPropertyName("fancier")] FancierInfoDto? Fancier,
    [property: JsonPropertyName("buyer")] FancierInfoDto? Buyer,
    [property: JsonPropertyName("bidders")] List<int>? Bidders);

public sealed record CoupleDto(
    [property: JsonPropertyName("id")] int? Id,
    [property: JsonPropertyName("cockId")] int? CockId,
    [property: JsonPropertyName("henId")] int? HenId,
    [property: JsonPropertyName("start")] DateTimeOffset? Start,
    [property: JsonPropertyName("days")] int? Days);

public sealed class FlexibleStringJsonConverter : JsonConverter<string>
{
    public override string? Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options) =>
        reader.TokenType switch
        {
            JsonTokenType.Null => null,
            JsonTokenType.String => reader.GetString(),
            JsonTokenType.Number => reader.TryGetInt64(out var integer)
                ? integer.ToString(CultureInfo.InvariantCulture)
                : reader.GetDecimal().ToString(CultureInfo.InvariantCulture),
            JsonTokenType.True => bool.TrueString.ToLowerInvariant(),
            JsonTokenType.False => bool.FalseString.ToLowerInvariant(),
            _ => throw new JsonException($"Cannot convert {reader.TokenType} to a string."),
        };

    public override void Write(Utf8JsonWriter writer, string value, JsonSerializerOptions options) =>
        writer.WriteStringValue(value);
}