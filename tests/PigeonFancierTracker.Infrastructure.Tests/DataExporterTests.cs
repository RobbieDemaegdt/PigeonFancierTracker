using System.IO.Compression;
using System.Text.Json;
using FluentAssertions;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using PigeonFancierTracker.Core.Contracts;
using PigeonFancierTracker.Infrastructure.Persistence;
using Xunit;

namespace PigeonFancierTracker.Infrastructure.Tests;

public sealed class DataExporterTests : IDisposable
{
    private readonly SqliteConnection connection;
    private readonly IDbContextFactory<AppDbContext> factory;

    public DataExporterTests()
    {
        connection = new SqliteConnection("DataSource=:memory:");
        connection.Open();
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseSqlite(connection)
            .Options;
        factory = new TestDbContextFactory(options);
        using var db = factory.CreateDbContext();
        db.Database.EnsureCreated();
    }

    [Fact]
    public async Task ExportAsync_CreatesValidArchive_WithAllEntities()
    {
        // Arrange
        using var db = factory.CreateDbContext();
        db.RawApiSnapshots.Add(new RawApiSnapshotEntity
        {
            Endpoint = "/test",
            HttpMethod = "GET",
            StatusCode = 200,
            CapturedAtUtc = DateTimeOffset.UtcNow,
            ResponseBodyJson = "{}",
            BodySha256 = "abc123"
        });
        db.SyncRuns.Add(new SyncRunEntity
        {
            StartedAtUtc = DateTimeOffset.UtcNow,
            Profile = "Quick",
            Status = "Succeeded",
            SelectedFancierId = 1
        });
        db.CompletedTransfers.Add(new CompletedTransferEntity
        {
            TransferId = 99,
            SelectedFancierId = 1,
            Status = "Completed",
            BidCount = 3,
            DetectedAtUtc = DateTimeOffset.UtcNow
        });
        await db.SaveChangesAsync();

        var exporter = new DataExporter(factory);
        var path = Path.Combine(Path.GetTempPath(), $"test-export-{Guid.NewGuid()}.pfbackup");

        try
        {
            // Act
            var result = await exporter.ExportAsync(path);

            // Assert
            result.Success.Should().BeTrue();
            result.SnapshotCount.Should().Be(1);
            result.SyncRunCount.Should().Be(1);
            result.TransferCount.Should().Be(1);

            using var archive = ZipFile.OpenRead(path);
            archive.GetEntry("manifest.json").Should().NotBeNull();
            archive.GetEntry("raw_snapshots.json").Should().NotBeNull();
            archive.GetEntry("sync_runs.json").Should().NotBeNull();
            archive.GetEntry("sync_run_items.json").Should().NotBeNull();
            archive.GetEntry("completed_transfers.json").Should().NotBeNull();
            archive.GetEntry("flights.json").Should().NotBeNull();
            archive.GetEntry("flight_results.json").Should().NotBeNull();

            await using var manifestStream = archive.GetEntry("manifest.json")!.Open();
            var manifest = await JsonSerializer.DeserializeAsync<BackupManifest>(manifestStream);
            manifest!.FormatVersion.Should().Be(2);
            manifest.SnapshotCount.Should().Be(1);
        }
        finally
        {
            File.Delete(path);
        }
    }

    public void Dispose() => connection.Dispose();

    private sealed class TestDbContextFactory(DbContextOptions<AppDbContext> options) : IDbContextFactory<AppDbContext>
    {
        public AppDbContext CreateDbContext() => new(options);
        public Task<AppDbContext> CreateDbContextAsync(CancellationToken ct = default) => Task.FromResult(CreateDbContext());
    }
}
