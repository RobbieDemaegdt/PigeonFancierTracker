using System.Text.Json.Serialization;

namespace PigeonFancierTracker.Core.Contracts;

// --- API DTOs ---

public sealed record OffspringDto(
    [property: JsonPropertyName("id")] int Id,
    [property: JsonPropertyName("firstNameId")] int? FirstNameId,
    [property: JsonPropertyName("lastNameId")] int? LastNameId,
    [property: JsonPropertyName("sex"), JsonConverter(typeof(FlexibleStringJsonConverter))] string? Sex,
    [property: JsonPropertyName("totalMonths")] int? TotalMonths,
    [property: JsonPropertyName("skills")] PigeonSkillsDto? Skills);

public sealed record PedigreeNodeDto(
    [property: JsonPropertyName("id")] int Id,
    [property: JsonPropertyName("firstNameId")] int? FirstNameId,
    [property: JsonPropertyName("lastNameId")] int? LastNameId,
    [property: JsonPropertyName("sex"), JsonConverter(typeof(FlexibleStringJsonConverter))] string? Sex,
    [property: JsonPropertyName("parentCock")] PedigreeNodeDto? ParentCock,
    [property: JsonPropertyName("parentHen")] PedigreeNodeDto? ParentHen);

// --- View models ---

public sealed record OffspringPerformanceItem(
    string PigeonName,
    int PigeonId,
    string PartnerName,
    int PartnerId,
    int OffspringCount,
    decimal? AvgOffspringTotalSkill,
    decimal? ParentAvgTotalSkill,
    decimal SkillDelta,
    string SkillDeltaDisplay);

public sealed record InbreedingInfo(
    int PigeonId,
    string PigeonName,
    int LineageDepth,
    int CommonAncestorCount,
    double InbreedingCoefficient,
    string? InbreedingDisplay);

public sealed record BreedingAnalysisData(
    IReadOnlyList<OffspringPerformanceItem> PairPerformance,
    IReadOnlyList<InbreedingInfo> InbreedingReport,
    double FlockAvgInbreeding);

public interface IBreedingDataReader
{
    Task<BreedingAnalysisData> GetBreedingAnalysisAsync(
        int fancierId,
        CancellationToken cancellationToken = default);
}
