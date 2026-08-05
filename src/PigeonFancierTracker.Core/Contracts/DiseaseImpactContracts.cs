namespace PigeonFancierTracker.Core.Contracts;

public sealed record DiseaseSnapshot(
    DateTimeOffset ObservedAt,
    string? Disease,
    decimal TotalSkill);

public sealed record DiseaseEpisode(
    string DiseaseName,
    DateTimeOffset StartedAt,
    DateTimeOffset? EndedAt,
    int DurationSnapshots,
    decimal SkillBefore,
    decimal SkillWorst,
    decimal? SkillAfter,
    decimal SkillLoss,
    decimal? RecoveryAmount,
    decimal? NetImpact);

public sealed record DiseaseImpactResult(
    IReadOnlyList<DiseaseEpisode> Episodes,
    int TotalEpisodes,
    decimal AvgSkillLoss,
    decimal? AvgRecoveryPercentage);
