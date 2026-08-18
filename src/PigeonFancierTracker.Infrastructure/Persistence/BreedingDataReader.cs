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

        var coupleTasks = couples
            .Where(c => c.CockId is not null && c.HenId is not null)
            .Select(async couple =>
            {
                var cockId = couple.CockId!.Value;
                var henId = couple.HenId!.Value;
                var cockName = pigeonMap.TryGetValue(cockId, out var cock) ? cock.DisplayName : $"Duif #{cockId}";
                var henName = pigeonMap.TryGetValue(henId, out var hen) ? hen.DisplayName : $"Duif #{henId}";

                var offspring = await pedigreeDataFetcher.GetOffspringAsync(cockId, cancellationToken);
                var offspringSkills = offspring
                    .Where(o => o.Skills?.Total is not null)
                    .Select(o => (o.Skills!.Total ?? 0) + 6)
                    .ToList();

                return new OffspringPerformanceCalculator.BreedingPairInput(
                    cockName, cockId, cock?.TotalSkill, henName, henId, hen?.TotalSkill,
                    offspring.Count, offspringSkills);
            })
            .ToList();

        var breedingPairs = (await Task.WhenAll(coupleTasks)).ToList();
        var pairPerformance = OffspringPerformanceCalculator.Calculate(breedingPairs).ToList();

        var pedigreeTasks = dashboard.Pigeons
            .Where(p => p.SourceId.HasValue)
            .Select(async pigeon =>
            {
                var id = pigeon.SourceId!.Value;
                var pedigree = await pedigreeDataFetcher.GetPedigreeAsync(id, cancellationToken);
                return InbreedingCalculator.Calculate(id, pigeon.DisplayName, pedigree);
            })
            .ToList();

        var inbreedingReport = (await Task.WhenAll(pedigreeTasks)).ToList();

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
