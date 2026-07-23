using FluentAssertions;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using PigeonFancierTracker.Infrastructure.Persistence;
using Xunit;

namespace PigeonFancierTracker.Infrastructure.Tests;

public sealed class DataImporterTests : IDisposable
{
    private readonly SqliteConnection connection;
    private readonly IDbContextFactory<AppDbContext> factory;

    public DataImporterTests()
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
    public async Task ImportAsync_MergesNewRecords_SkipsDuplicates()
    {
        // Arrange — seed existing data
        using (var db = factory.CreateDbContext())
        {
            db.CompletedTransfers.Add(new CompletedTransferEntity
            {
                TransferId = 1,
                SelectedFancierId = 10,
                Status = "Completed",
                BidCount = 2,
                DetectedAtUtc = DateTimeOffset.UtcNow
            });
            await db.SaveChangesAsync();
        }

        // Create an export file with overlapping + new data
        var exportPath = Path.Combine(Path.GetTempPath(), $"test-import-{Guid.NewGuid()}.pfbackup");
        var sourceConnection = new SqliteConnection("DataSource=:memory:");
        sourceConnection.Open();
        var sourceOptions = new DbContextOptionsBuilder<AppDbContext>()
            .UseSqlite(sourceConnection)
            .Options;
        var sourceFactory = new TestDbContextFactory(sourceOptions);
        using (var sourceDb = sourceFactory.CreateDbContext())
        {
            sourceDb.Database.EnsureCreated();
            // Duplicate transfer (same FancierId + TransferId)
            sourceDb.CompletedTransfers.Add(new CompletedTransferEntity
            {
                TransferId = 1,
                SelectedFancierId = 10,
                Status = "Completed",
                BidCount = 2,
                DetectedAtUtc = DateTimeOffset.UtcNow
            });
            // New transfer
            sourceDb.CompletedTransfers.Add(new CompletedTransferEntity
            {
                TransferId = 2,
                SelectedFancierId = 10,
                Status = "Active",
                BidCount = 5,
                DetectedAtUtc = DateTimeOffset.UtcNow
            });
            // New snapshot
            sourceDb.RawApiSnapshots.Add(new RawApiSnapshotEntity
            {
                Endpoint = "/pigeons",
                HttpMethod = "GET",
                StatusCode = 200,
                CapturedAtUtc = DateTimeOffset.UtcNow,
                ResponseBodyJson = "[{\"id\":1}]",
                BodySha256 = "sha256hash1"
            });
            await sourceDb.SaveChangesAsync();
        }

        var exporter = new DataExporter(sourceFactory);
        await exporter.ExportAsync(exportPath);
        sourceConnection.Dispose();

        try
        {
            // Act
            var importer = new DataImporter(factory);
            var result = await importer.ImportAsync(exportPath);

            // Assert
            result.Success.Should().BeTrue();
            result.TransferCount.Should().Be(1, "only the new transfer should be added");
            result.SnapshotCount.Should().Be(1);

            using var db = factory.CreateDbContext();
            (await db.CompletedTransfers.CountAsync()).Should().Be(2);
            (await db.RawApiSnapshots.CountAsync()).Should().Be(1);
        }
        finally
        {
            File.Delete(exportPath);
        }
    }

    [Fact]
    public async Task ImportAsync_RemapsSyncRunItemsToNewParentId()
    {
        // Arrange — create export with a sync run + items
        var exportPath = Path.Combine(Path.GetTempPath(), $"test-remap-{Guid.NewGuid()}.pfbackup");
        var sourceConnection = new SqliteConnection("DataSource=:memory:");
        sourceConnection.Open();
        var sourceOptions = new DbContextOptionsBuilder<AppDbContext>()
            .UseSqlite(sourceConnection)
            .Options;
        var sourceFactory = new TestDbContextFactory(sourceOptions);
        using (var sourceDb = sourceFactory.CreateDbContext())
        {
            sourceDb.Database.EnsureCreated();
            var run = new SyncRunEntity
            {
                StartedAtUtc = new DateTimeOffset(2026, 1, 15, 10, 0, 0, TimeSpan.Zero),
                Profile = "Quick",
                Status = "Succeeded",
                SelectedFancierId = 5
            };
            sourceDb.SyncRuns.Add(run);
            await sourceDb.SaveChangesAsync();

            sourceDb.SyncRunItems.Add(new SyncRunItemEntity
            {
                SyncRunId = run.Id,
                Endpoint = "/pigeons",
                StartedAtUtc = run.StartedAtUtc,
                Attempts = 1,
                StatusCode = 200,
                Status = "Success"
            });
            await sourceDb.SaveChangesAsync();
        }

        var exporter = new DataExporter(sourceFactory);
        await exporter.ExportAsync(exportPath);
        sourceConnection.Dispose();

        try
        {
            // Act
            var importer = new DataImporter(factory);
            var result = await importer.ImportAsync(exportPath);

            // Assert
            result.Success.Should().BeTrue();
            result.SyncRunCount.Should().Be(1);

            using var db = factory.CreateDbContext();
            var importedRun = await db.SyncRuns.FirstAsync();
            var importedItem = await db.SyncRunItems.FirstAsync();
            importedItem.SyncRunId.Should().Be(importedRun.Id, "item should be remapped to new parent ID");
        }
        finally
        {
            File.Delete(exportPath);
        }
    }

    public void Dispose() => connection.Dispose();

    private sealed class TestDbContextFactory(DbContextOptions<AppDbContext> options) : IDbContextFactory<AppDbContext>
    {
        public AppDbContext CreateDbContext() => new(options);
        public Task<AppDbContext> CreateDbContextAsync(CancellationToken ct = default) => Task.FromResult(CreateDbContext());
    }
}
