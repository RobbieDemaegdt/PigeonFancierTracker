using Microsoft.EntityFrameworkCore;
using PigeonFancierTracker.Core.Analytics;
using PigeonFancierTracker.Core.Contracts;

namespace PigeonFancierTracker.Infrastructure.Persistence;

public sealed class SponsorDataReader(IDbContextFactory<AppDbContext> contextFactory) : ISponsorDataReader
{
    public async Task<SponsorOverview> GetSponsorOverviewAsync(int selectedFancierId, CancellationToken ct = default)
    {
        await using var db = await contextFactory.CreateDbContextAsync(ct);

        var latestSnapshot = await db.SponsorSnapshots
            .AsNoTracking()
            .Where(x => x.SelectedFancierId == selectedFancierId)
            .OrderByDescending(x => x.Id)
            .FirstOrDefaultAsync(ct);

        if (latestSnapshot is null)
            return new SponsorOverview([], [], 0, false, 0, 0, "Stabiel", [], []);

        var latestCapturedAt = latestSnapshot.CapturedAtUtc;

        var latestSyncContracts = await db.SponsorSnapshots
            .AsNoTracking()
            .Where(x => x.SelectedFancierId == selectedFancierId
                && x.CapturedAtUtc == latestCapturedAt)
            .ToListAsync(ct);

        var activeSponsors = latestSyncContracts
            .Where(x => x.Signed && x.RuntimeRemaining > 0)
            .Select(ToActiveSponsorInfo)
            .ToList();

        var pendingOffers = latestSyncContracts
            .Where(x => !x.Signed)
            .Select(ToActiveSponsorInfo)
            .ToList();

        var expiredFromLatest = latestSyncContracts
            .Where(x => x.Signed && x.RuntimeRemaining <= 0)
            .Select(ToActiveSponsorInfo)
            .ToList();

        var totalMonthly = activeSponsors.Sum(s => s.Monthly);
        var canCallSponsors = latestSyncContracts.FirstOrDefault()?.CanCallSponsors ?? false;

        var allSnapshots = await db.SponsorSnapshots
            .AsNoTracking()
            .Where(x => x.SelectedFancierId == selectedFancierId)
            .OrderBy(x => x.Id)
            .ToListAsync(ct);

        var latestContractIds = latestSyncContracts
            .Select(x => x.ContractId)
            .ToHashSet();

        var historicalContracts = allSnapshots
            .Where(x => !latestContractIds.Contains(x.ContractId))
            .GroupBy(x => x.ContractId)
            .Select(g => g.OrderByDescending(x => x.Id).First())
            .Select(ToActiveSponsorInfo)
            .ToList();

        var distinctContracts = allSnapshots
            .GroupBy(x => x.ContractId)
            .Select(g => g.First())
            .Select(x => new SponsorHistoryEntry(x.Monthly, x.Direct, x.Runtime, x.Rating, x.Total, x.CapturedAtUtc))
            .ToList();

        var avgMonthly = distinctContracts.Count > 0
            ? Math.Round(distinctContracts.Average(h => h.Monthly), 0)
            : 0;

        var (trendPercent, trendDirection) = SponsorDealScorer.CalculateOfferTrend(distinctContracts);

        var scoredActive = activeSponsors.Select(sponsor =>
        {
            var score = SponsorDealScorer.ScoreCurrentDeal(
                sponsor.Monthly, sponsor.Direct, sponsor.Runtime, distinctContracts);
            return new ScoredSponsorInfo(sponsor, score);
        }).ToList();

        var scoredPending = pendingOffers.Select(offer =>
        {
            var score = SponsorDealScorer.ScoreCurrentDeal(
                offer.Monthly, offer.Direct, offer.Runtime, distinctContracts);
            return new ScoredSponsorInfo(offer, score);
        }).OrderByDescending(x => x.Score.Score).ToList();

        var scoredExpired = expiredFromLatest.Concat(historicalContracts).Select(sponsor =>
        {
            var score = SponsorDealScorer.ScoreCurrentDeal(
                sponsor.Monthly, sponsor.Direct, sponsor.Runtime, distinctContracts);
            return new ScoredSponsorInfo(sponsor, score);
        }).ToList();

        return new SponsorOverview(
            activeSponsors,
            scoredPending,
            totalMonthly,
            canCallSponsors,
            avgMonthly,
            trendPercent,
            trendDirection,
            scoredActive,
            scoredExpired);
    }

    private static ActiveSponsorInfo ToActiveSponsorInfo(SponsorSnapshotEntity e) =>
        new(e.ContractId, e.SponsorId, e.Monthly, e.Direct,
            e.Runtime, e.RuntimeRemaining, e.Rating, e.Total, e.ContractEndUtc);
}
