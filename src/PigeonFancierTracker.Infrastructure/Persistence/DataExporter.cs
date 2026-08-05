using System.IO.Compression;
using System.Reflection;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using PigeonFancierTracker.Core.Contracts;

namespace PigeonFancierTracker.Infrastructure.Persistence;

public sealed class DataExporter(IDbContextFactory<AppDbContext> dbContextFactory) : IDataExporter
{
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

    public async Task<DataPortResult> ExportAsync(string destinationPath, IProgress<DataPortProgress>? progress = null, CancellationToken cancellationToken = default)
    {
        await using var db = await dbContextFactory.CreateDbContextAsync(cancellationToken);

        progress?.Report(new DataPortProgress("Momentopnamen laden...", 0));
        var snapshots = await db.RawApiSnapshots.AsNoTracking().ToListAsync(cancellationToken);

        progress?.Report(new DataPortProgress("Syncruns laden...", 0.25));
        var syncRuns = await db.SyncRuns.AsNoTracking().ToListAsync(cancellationToken);

        progress?.Report(new DataPortProgress("Sync-items laden...", 0.40));
        var syncRunItems = await db.SyncRunItems.AsNoTracking().ToListAsync(cancellationToken);

        progress?.Report(new DataPortProgress("Transfers laden...", 0.55));
        var transfers = await db.CompletedTransfers.AsNoTracking().ToListAsync(cancellationToken);

        progress?.Report(new DataPortProgress("Vluchten laden...", 0.60));
        var flights = await db.Flights.AsNoTracking().ToListAsync(cancellationToken);

        progress?.Report(new DataPortProgress("Vluchtresultaten laden...", 0.65));
        var flightResults = await db.FlightResults.AsNoTracking().ToListAsync(cancellationToken);

        progress?.Report(new DataPortProgress("Archief schrijven...", 0.70));

        var manifest = new BackupManifest(
            FormatVersion: 2,
            ExportedAtUtc: DateTimeOffset.UtcNow,
            AppVersion: Assembly.GetEntryAssembly()?.GetName().Version?.ToString() ?? "unknown",
            SnapshotCount: snapshots.Count,
            SyncRunCount: syncRuns.Count,
            SyncRunItemCount: syncRunItems.Count,
            TransferCount: transfers.Count,
            FlightCount: flights.Count,
            FlightResultCount: flightResults.Count);

        using var fileStream = new FileStream(destinationPath, FileMode.Create, FileAccess.Write, FileShare.None);
        using var archive = new ZipArchive(fileStream, ZipArchiveMode.Create);

        await WriteJsonEntryAsync(archive, "manifest.json", manifest, cancellationToken);
        await WriteJsonEntryAsync(archive, "raw_snapshots.json", snapshots, cancellationToken);
        await WriteJsonEntryAsync(archive, "sync_runs.json", syncRuns, cancellationToken);
        await WriteJsonEntryAsync(archive, "sync_run_items.json", syncRunItems, cancellationToken);
        await WriteJsonEntryAsync(archive, "completed_transfers.json", transfers, cancellationToken);
        await WriteJsonEntryAsync(archive, "flights.json", flights, cancellationToken);
        await WriteJsonEntryAsync(archive, "flight_results.json", flightResults, cancellationToken);

        progress?.Report(new DataPortProgress("Export voltooid", 1.0));

        var total = snapshots.Count + syncRuns.Count + syncRunItems.Count + transfers.Count + flights.Count + flightResults.Count;
        return new DataPortResult(true, $"{total:N0} records geëxporteerd.", snapshots.Count, syncRuns.Count, syncRunItems.Count, transfers.Count, flights.Count, flightResults.Count);
    }

    private static async Task WriteJsonEntryAsync<T>(ZipArchive archive, string entryName, T data, CancellationToken cancellationToken)
    {
        var entry = archive.CreateEntry(entryName, CompressionLevel.Optimal);
        await using var stream = entry.Open();
        await JsonSerializer.SerializeAsync(stream, data, JsonOptions, cancellationToken);
    }
}
