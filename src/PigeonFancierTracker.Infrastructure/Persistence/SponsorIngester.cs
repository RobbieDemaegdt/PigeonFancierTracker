using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.EntityFrameworkCore;
using PigeonFancierTracker.Core.Contracts;

namespace PigeonFancierTracker.Infrastructure.Persistence;

public sealed class SponsorIngester(IDbContextFactory<AppDbContext> contextFactory)
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        NumberHandling = JsonNumberHandling.AllowReadingFromString,
    };

    public async Task IngestAsync(int selectedFancierId, CancellationToken cancellationToken = default)
    {
        await using var db = await contextFactory.CreateDbContextAsync(cancellationToken);

        var snapshot = await db.RawApiSnapshots
            .AsNoTracking()
            .Where(x => x.SelectedFancierId == selectedFancierId
                && x.Endpoint == "/api/fancier/selected"
                && x.StatusCode >= 200 && x.StatusCode < 300)
            .OrderByDescending(x => x.Id)
            .FirstOrDefaultAsync(cancellationToken);

        if (snapshot is null)
            return;

        var extracted = ExtractSponsors(snapshot.ResponseBodyJson);
        if (extracted is null)
            return;

        await InsertIfChangedAsync(db, selectedFancierId, extracted.Value.Sponsors,
            extracted.Value.CanCallSponsors, snapshot.CapturedAtUtc, cancellationToken);
    }

    public async Task BackfillAsync(int selectedFancierId, CancellationToken cancellationToken = default)
    {
        await using var db = await contextFactory.CreateDbContextAsync(cancellationToken);

        var hasExisting = await db.SponsorSnapshots
            .AnyAsync(x => x.SelectedFancierId == selectedFancierId, cancellationToken);
        if (hasExisting)
            return;

        var snapshots = await db.RawApiSnapshots
            .AsNoTracking()
            .Where(x => x.SelectedFancierId == selectedFancierId
                && x.Endpoint == "/api/fancier/selected"
                && x.StatusCode >= 200 && x.StatusCode < 300)
            .OrderBy(x => x.Id)
            .ToListAsync(cancellationToken);

        foreach (var snapshot in snapshots)
        {
            var extracted = ExtractSponsors(snapshot.ResponseBodyJson);
            if (extracted is null)
                continue;

            await InsertIfChangedAsync(db, selectedFancierId, extracted.Value.Sponsors,
                extracted.Value.CanCallSponsors, snapshot.CapturedAtUtc, cancellationToken);
        }
    }

    private static async Task InsertIfChangedAsync(
        AppDbContext db,
        int fancierId,
        IReadOnlyList<SponsorContractDto> sponsors,
        bool canCallSponsors,
        DateTimeOffset capturedAt,
        CancellationToken cancellationToken)
    {
        var latestPerContract = await db.SponsorSnapshots
            .Where(x => x.SelectedFancierId == fancierId)
            .GroupBy(x => x.ContractId)
            .Select(g => g.OrderByDescending(x => x.Id).First())
            .ToListAsync(cancellationToken);

        foreach (var sponsor in sponsors)
        {
            if (sponsor.Id is null || sponsor.SponsorId is null)
                continue;

            var existing = latestPerContract.FirstOrDefault(x => x.ContractId == sponsor.Id.Value);

            if (existing is not null && existing.CapturedAtUtc == capturedAt)
                continue;

            db.SponsorSnapshots.Add(new SponsorSnapshotEntity
            {
                SelectedFancierId = fancierId,
                ContractId = sponsor.Id.Value,
                SponsorId = sponsor.SponsorId.Value,
                Monthly = sponsor.Monthly ?? 0,
                Direct = sponsor.Direct ?? 0,
                Runtime = sponsor.Runtime ?? 0,
                RuntimeRemaining = sponsor.RuntimeRemaining ?? 0,
                Signed = sponsor.Signed ?? false,
                Rating = sponsor.Rating ?? 0,
                Total = sponsor.Total ?? 0,
                ContractEndUtc = sponsor.End,
                CanCallSponsors = canCallSponsors,
                CapturedAtUtc = capturedAt,
            });
        }

        var currentContractIds = sponsors
            .Where(s => s.Id is not null)
            .Select(s => s.Id!.Value)
            .ToHashSet();

        var disappeared = latestPerContract
            .Where(x => x.Signed
                && !currentContractIds.Contains(x.ContractId)
                && x.CapturedAtUtc != capturedAt);

        foreach (var stale in disappeared)
        {
            db.SponsorSnapshots.Add(new SponsorSnapshotEntity
            {
                SelectedFancierId = fancierId,
                ContractId = stale.ContractId,
                SponsorId = stale.SponsorId,
                Monthly = stale.Monthly,
                Direct = stale.Direct,
                Runtime = stale.Runtime,
                RuntimeRemaining = 0,
                Signed = false,
                Rating = stale.Rating,
                Total = stale.Total,
                ContractEndUtc = stale.ContractEndUtc,
                CanCallSponsors = canCallSponsors,
                CapturedAtUtc = capturedAt,
            });
        }

        await db.SaveChangesAsync(cancellationToken);
    }

    private static (IReadOnlyList<SponsorContractDto> Sponsors, bool CanCallSponsors)? ExtractSponsors(string json)
    {
        try
        {
            var fancier = JsonSerializer.Deserialize<SelectedFancierDto>(json, JsonOptions);
            if (fancier?.Finances?.Sponsors is not { } sponsors)
                return null;

            return (sponsors, fancier.Finances.CanCallSponsors ?? false);
        }
        catch
        {
            return null;
        }
    }
}
