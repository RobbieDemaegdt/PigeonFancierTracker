using System.IO.Compression;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using PigeonFancierTracker.Core.Contracts;

namespace PigeonFancierTracker.Infrastructure.Persistence;

public sealed class DataImporter(IDbContextFactory<AppDbContext> dbContextFactory) : IDataImporter
{
    private const int BatchSize = 500;

    public async Task<DataPortResult> ImportAsync(string sourcePath, IProgress<DataPortProgress>? progress = null, CancellationToken cancellationToken = default)
    {
        using var fileStream = new FileStream(sourcePath, FileMode.Open, FileAccess.Read, FileShare.Read);
        using var archive = new ZipArchive(fileStream, ZipArchiveMode.Read);

        progress?.Report(new DataPortProgress("Manifest controleren...", 0));
        var manifest = await ReadJsonEntryAsync<BackupManifest>(archive, "manifest.json", cancellationToken);
        if (manifest is null || manifest.FormatVersion > 1)
        {
            return new DataPortResult(false, "Ongeldig of niet-ondersteund back-upbestand.", 0, 0, 0, 0);
        }

        progress?.Report(new DataPortProgress("Momentopnamen laden...", 0.10));
        var snapshots = await ReadJsonEntryAsync<List<RawApiSnapshotEntity>>(archive, "raw_snapshots.json", cancellationToken) ?? [];

        progress?.Report(new DataPortProgress("Syncruns laden...", 0.25));
        var syncRuns = await ReadJsonEntryAsync<List<SyncRunEntity>>(archive, "sync_runs.json", cancellationToken) ?? [];

        progress?.Report(new DataPortProgress("Sync-items laden...", 0.40));
        var syncRunItems = await ReadJsonEntryAsync<List<SyncRunItemEntity>>(archive, "sync_run_items.json", cancellationToken) ?? [];

        progress?.Report(new DataPortProgress("Transfers laden...", 0.55));
        var transfers = await ReadJsonEntryAsync<List<CompletedTransferEntity>>(archive, "completed_transfers.json", cancellationToken) ?? [];

        await using var db = await dbContextFactory.CreateDbContextAsync(cancellationToken);

        progress?.Report(new DataPortProgress("Momentopnamen importeren...", 0.60));
        int snapshotCount = await MergeSnapshotsAsync(db, snapshots, cancellationToken);

        progress?.Report(new DataPortProgress("Syncruns importeren...", 0.75));
        int syncRunCount = await MergeSyncRunsAsync(db, syncRuns, syncRunItems, cancellationToken);
        int syncRunItemCount = syncRunCount > 0 ? syncRunItems.Count : 0;

        progress?.Report(new DataPortProgress("Transfers importeren...", 0.90));
        int transferCount = await MergeTransfersAsync(db, transfers, cancellationToken);

        progress?.Report(new DataPortProgress("Import voltooid", 1.0));

        var total = snapshotCount + syncRunCount + syncRunItemCount + transferCount;
        return new DataPortResult(true, $"{total:N0} nieuwe records geïmporteerd.", snapshotCount, syncRunCount, syncRunItemCount, transferCount);
    }

    private static async Task<int> MergeSnapshotsAsync(AppDbContext db, List<RawApiSnapshotEntity> incoming, CancellationToken ct)
    {
        int added = 0;
        foreach (var batch in Chunk(incoming, BatchSize))
        {
            foreach (var item in batch)
            {
                bool exists = await db.RawApiSnapshots.AnyAsync(x =>
                    x.Endpoint == item.Endpoint &&
                    x.NormalizedQuery == item.NormalizedQuery &&
                    x.SelectedFancierId == item.SelectedFancierId &&
                    x.SourceSeasonId == item.SourceSeasonId &&
                    x.BodySha256 == item.BodySha256, ct);

                if (!exists)
                {
                    item.Id = 0;
                    db.RawApiSnapshots.Add(item);
                    added++;
                }
            }

            await db.SaveChangesAsync(ct);
            db.ChangeTracker.Clear();
        }

        return added;
    }

    private static async Task<int> MergeSyncRunsAsync(AppDbContext db, List<SyncRunEntity> runs, List<SyncRunItemEntity> items, CancellationToken ct)
    {
        int added = 0;
        var itemsByOriginalRunId = items.GroupBy(x => x.SyncRunId).ToDictionary(g => g.Key, g => g.ToList());

        foreach (var batch in Chunk(runs, BatchSize))
        {
            foreach (var run in batch)
            {
                bool exists = await db.SyncRuns.AnyAsync(x =>
                    x.StartedAtUtc == run.StartedAtUtc &&
                    x.SelectedFancierId == run.SelectedFancierId &&
                    x.Profile == run.Profile, ct);

                if (!exists)
                {
                    long originalId = run.Id;
                    run.Id = 0;
                    db.SyncRuns.Add(run);
                    await db.SaveChangesAsync(ct);

                    if (itemsByOriginalRunId.TryGetValue(originalId, out var relatedItems))
                    {
                        foreach (var item in relatedItems)
                        {
                            item.Id = 0;
                            item.SyncRunId = run.Id;
                            item.RawSnapshotId = null;
                            db.SyncRunItems.Add(item);
                        }

                        await db.SaveChangesAsync(ct);
                    }

                    added++;
                }
            }

            db.ChangeTracker.Clear();
        }

        return added;
    }

    private static async Task<int> MergeTransfersAsync(AppDbContext db, List<CompletedTransferEntity> incoming, CancellationToken ct)
    {
        int added = 0;
        foreach (var batch in Chunk(incoming, BatchSize))
        {
            foreach (var item in batch)
            {
                bool exists = await db.CompletedTransfers.AnyAsync(x =>
                    x.SelectedFancierId == item.SelectedFancierId &&
                    x.TransferId == item.TransferId, ct);

                if (!exists)
                {
                    item.Id = 0;
                    db.CompletedTransfers.Add(item);
                    added++;
                }
            }

            await db.SaveChangesAsync(ct);
            db.ChangeTracker.Clear();
        }

        return added;
    }

    private static IEnumerable<List<T>> Chunk<T>(List<T> source, int size)
    {
        for (int i = 0; i < source.Count; i += size)
        {
            yield return source.GetRange(i, Math.Min(size, source.Count - i));
        }
    }

    private static async Task<T?> ReadJsonEntryAsync<T>(ZipArchive archive, string entryName, CancellationToken ct)
    {
        var entry = archive.GetEntry(entryName);
        if (entry is null) return default;
        await using var stream = entry.Open();
        return await JsonSerializer.DeserializeAsync<T>(stream, cancellationToken: ct);
    }
}
