using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using PigeonFancierTracker.Core.Analytics;
using PigeonFancierTracker.Core.Contracts;

namespace PigeonFancierTracker.Infrastructure.Persistence;

public sealed class BreedingDataReader(
    IDbContextFactory<AppDbContext> contextFactory,
    PedigreeDataFetcher pedigreeDataFetcher,
    ITrackerDataReader trackerDataReader) : IBreedingDataReader
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
    };

    public async Task<BreedingAnalysisData> GetBreedingAnalysisAsync(
        int fancierId,
        CancellationToken cancellationToken = default)
    {
        var dashboard = await trackerDataReader.GetDashboardAsync(fancierId, cancellationToken);
        var pigeonMap = dashboard.Pigeons
            .Where(p => p.SourceId.HasValue)
            .ToDictionary(p => p.SourceId!.Value);

        await using var db = await contextFactory.CreateDbContextAsync(cancellationToken);
        var couples = await LoadCouplesAsync(db, fancierId, cancellationToken);

        var pairPerformance = new List<OffspringPerformanceItem>();
        var breedingPairs = new List<OffspringPerformanceCalculator.BreedingPairInput>();

        foreach (var couple in couples)
        {
            if (couple.CockId is not int cockId || couple.HenId is not int henId)
                continue;

            var cockName = pigeonMap.TryGetValue(cockId, out var cock) ? cock.DisplayName : $"Duif #{cockId}";
            var henName = pigeonMap.TryGetValue(henId, out var hen) ? hen.DisplayName : $"Duif #{henId}";

            var offspring = await pedigreeDataFetcher.GetOffspringAsync(cockId, cancellationToken);
            var offspringSkills = offspring
                .Where(o => o.Skills?.Total is not null)
                .Select(o => (o.Skills!.Total ?? 0) + 6)
                .ToList();

            if (offspringSkills.Count > 0)
            {
                breedingPairs.Add(new OffspringPerformanceCalculator.BreedingPairInput(
                    cockName, cockId, cock?.TotalSkill, henName, henId, hen?.TotalSkill, offspringSkills));
            }
        }

        pairPerformance = OffspringPerformanceCalculator.Calculate(breedingPairs).ToList();

        var inbreedingReport = new List<InbreedingInfo>();
        foreach (var pigeon in dashboard.Pigeons)
        {
            if (pigeon.SourceId is not int id) continue;

            var pedigree = await pedigreeDataFetcher.GetPedigreeAsync(id, cancellationToken);
            inbreedingReport.Add(InbreedingCalculator.Calculate(id, pigeon.DisplayName, pedigree));
        }

        var flockAvg = inbreedingReport.Count > 0
            ? Math.Round(inbreedingReport.Average(i => i.InbreedingCoefficient), 4)
            : 0;

        return new BreedingAnalysisData(pairPerformance, inbreedingReport, flockAvg);
    }

    private static async Task<IReadOnlyList<CoupleDto>> LoadCouplesAsync(
        AppDbContext db, int fancierId, CancellationToken ct)
    {
        var snapshot = await db.RawApiSnapshots
            .AsNoTracking()
            .Where(x => x.SelectedFancierId == fancierId
                && x.Endpoint == "/api/couple"
                && x.StatusCode >= 200 && x.StatusCode < 300)
            .OrderByDescending(x => x.Id)
            .FirstOrDefaultAsync(ct);

        if (snapshot is null)
            return [];

        try
        {
            return JsonSerializer.Deserialize<List<CoupleDto>>(snapshot.ResponseBodyJson, JsonOptions) ?? [];
        }
        catch
        {
            return [];
        }
    }
}
