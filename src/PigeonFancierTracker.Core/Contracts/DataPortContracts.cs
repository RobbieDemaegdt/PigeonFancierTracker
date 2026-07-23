namespace PigeonFancierTracker.Core.Contracts;

public interface IDataExporter
{
    Task<DataPortResult> ExportAsync(string destinationPath, IProgress<DataPortProgress>? progress = null, CancellationToken cancellationToken = default);
}

public interface IDataImporter
{
    Task<DataPortResult> ImportAsync(string sourcePath, IProgress<DataPortProgress>? progress = null, CancellationToken cancellationToken = default);
}

public sealed record DataPortResult(
    bool Success,
    string Message,
    int SnapshotCount,
    int SyncRunCount,
    int SyncRunItemCount,
    int TransferCount);

public sealed record DataPortProgress(string StepLabel, double Percentage);

public sealed record BackupManifest(
    int FormatVersion,
    DateTimeOffset ExportedAtUtc,
    string AppVersion,
    int SnapshotCount,
    int SyncRunCount,
    int SyncRunItemCount,
    int TransferCount);

public interface IDataResetter
{
    Task ResetAllAsync(CancellationToken cancellationToken = default);
}
