using PigeonFancierTracker.Core.Contracts;

namespace PigeonFancierTracker.Core.Analytics;

public static class DiseaseImpactCalculator
{
    public static DiseaseImpactResult Calculate(IReadOnlyList<DiseaseSnapshot> snapshots)
    {
        if (snapshots.Count == 0)
            return new DiseaseImpactResult([], 0, 0, null);

        var ordered = snapshots.OrderBy(s => s.ObservedAt).ToList();
        var episodes = new List<DiseaseEpisode>();

        int i = 0;
        while (i < ordered.Count)
        {
            if (string.IsNullOrEmpty(ordered[i].Disease))
            {
                i++;
                continue;
            }

            var episodeStart = i;
            var diseaseName = ordered[i].Disease!;
            var worstSkill = ordered[i].TotalSkill;

            while (i < ordered.Count && !string.IsNullOrEmpty(ordered[i].Disease))
            {
                if (ordered[i].TotalSkill < worstSkill)
                    worstSkill = ordered[i].TotalSkill;
                i++;
            }

            var episodeEnd = i - 1;

            var skillBefore = episodeStart > 0
                ? ordered[episodeStart - 1].TotalSkill
                : ordered[episodeStart].TotalSkill;

            decimal? skillAfter = i < ordered.Count
                ? ordered[i].TotalSkill
                : null;

            var skillLoss = skillBefore - worstSkill;
            var recoveryAmount = skillAfter.HasValue ? skillAfter.Value - worstSkill : (decimal?)null;
            var netImpact = skillAfter.HasValue ? skillAfter.Value - skillBefore : (decimal?)null;

            episodes.Add(new DiseaseEpisode(
                diseaseName,
                ordered[episodeStart].ObservedAt,
                ordered[episodeEnd].ObservedAt,
                episodeEnd - episodeStart + 1,
                skillBefore,
                worstSkill,
                skillAfter,
                skillLoss,
                recoveryAmount,
                netImpact));
        }

        if (episodes.Count == 0)
            return new DiseaseImpactResult([], 0, 0, null);

        var avgLoss = Math.Round(episodes.Average(e => e.SkillLoss), 1);
        var recoveries = episodes
            .Where(e => e.RecoveryAmount.HasValue && e.SkillLoss > 0)
            .ToList();
        decimal? avgRecoveryPct = recoveries.Count > 0
            ? Math.Round(recoveries.Average(e => e.RecoveryAmount!.Value / e.SkillLoss * 100), 1)
            : null;

        return new DiseaseImpactResult(episodes, episodes.Count, avgLoss, avgRecoveryPct);
    }
}
