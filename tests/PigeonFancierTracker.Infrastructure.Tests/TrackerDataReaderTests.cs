using FluentAssertions;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using PigeonFancierTracker.Infrastructure.Persistence;

namespace PigeonFancierTracker.Infrastructure.Tests;

public sealed class TrackerDataReaderTests
{
    [Fact]
    public async Task Loads_dashboard_summary_and_skill_change_from_raw_snapshots()
    {
        await using var database = await TestDatabase.CreateAsync();
        await using (var db = await database.Factory.CreateDbContextAsync())
        {
            db.RawApiSnapshots.AddRange(
                new RawApiSnapshotEntity
                {
                    Endpoint = "/api/fancier/selected",
                    HttpMethod = "GET",
                    StatusCode = 200,
                    CapturedAtUtc = DateTimeOffset.UtcNow,
                    ResponseBodyJson = "{\"id\":42,\"displayName\":\"Test fancier\",\"pigeonCount\":1,\"finances\":{\"balance\":1355.25,\"balancePrevious\":1200.00}}",
                    BodySha256 = "fancier",
                    SelectedFancierId = 42,
                },
                new RawApiSnapshotEntity
                {
                    Endpoint = "/api/pigeon",
                    HttpMethod = "GET",
                    StatusCode = 200,
                    CapturedAtUtc = DateTimeOffset.UtcNow.AddMinutes(-1),
                    ResponseBodyJson = "[{\"id\":7,\"sex\":\"Hen\",\"firstNameId\":1,\"lastNameId\":182,\"breed\":\"Janssen\",\"skills\":{\"total\":80}}]",
                    BodySha256 = "pigeon-current",
                    SelectedFancierId = 42,
                },
                new RawApiSnapshotEntity
                {
                    Endpoint = "/api/translation/nl",
                    HttpMethod = "GET",
                    StatusCode = 200,
                    CapturedAtUtc = DateTimeOffset.UtcNow,
                    ResponseBodyJson = "{\"first-name\":{\"1\":\"Tom\"},\"last-name\":{\"182\":\"Ape-head\"}}",
                    BodySha256 = "translation",
                    SelectedFancierId = 42,
                },
                new RawApiSnapshotEntity
                {
                    Endpoint = "/api/pigeon",
                    HttpMethod = "GET",
                    StatusCode = 200,
                    CapturedAtUtc = DateTimeOffset.UtcNow.AddMinutes(-2),
                    ResponseBodyJson = "[{\"id\":7,\"skills\":{\"total\":75}}]",
                    BodySha256 = "pigeon-previous",
                    SelectedFancierId = 42,
                });
            await db.SaveChangesAsync();
        }

        var reader = new TrackerDataReader(database.Factory);
        var result = await reader.GetDashboardAsync(42);

        result.FancierName.Should().Be("Test fancier");
        result.PigeonCount.Should().Be(1);
        result.Capital.Should().Be(1355.25m);
        result.AverageTotalSkill.Should().Be(86m);
        result.Pigeons.Single().DisplayName.Should().Be("Tom Ape-head");
        result.Pigeons.Should().ContainSingle()
            .Which.SkillChange.Should().Be(5m);
    }

    [Fact]
    public async Task Loads_ordered_pigeon_history_from_all_successful_snapshots()
    {
        await using var database = await TestDatabase.CreateAsync();
        var firstObserved = DateTimeOffset.UtcNow.AddDays(-2);
        var secondObserved = DateTimeOffset.UtcNow.AddDays(-1);
        await using (var db = await database.Factory.CreateDbContextAsync())
        {
            db.RawApiSnapshots.AddRange(
                new RawApiSnapshotEntity
                {
                    Endpoint = "/api/pigeon",
                    HttpMethod = "GET",
                    StatusCode = 200,
                    CapturedAtUtc = secondObserved,
                    ResponseBodyJson = "[{\"id\":7,\"firstNameId\":1,\"lastNameId\":182,\"breed\":\"Janssen\",\"skills\":{\"total\":82,\"speed\":41},\"premium\":125}]",
                    BodySha256 = "history-second",
                    SelectedFancierId = 42,
                },
                new RawApiSnapshotEntity
                {
                    Endpoint = "/api/translation/nl",
                    HttpMethod = "GET",
                    StatusCode = 200,
                    CapturedAtUtc = secondObserved,
                    ResponseBodyJson = "{\"first-name\":{\"1\":\"Tom\"},\"last-name\":{\"182\":\"Ape-head\"}}",
                    BodySha256 = "history-translation",
                    SelectedFancierId = 42,
                },
                new RawApiSnapshotEntity
                {
                    Endpoint = "/api/pigeon",
                    HttpMethod = "GET",
                    StatusCode = 200,
                    CapturedAtUtc = firstObserved,
                    ResponseBodyJson = "[{\"id\":7,\"firstNameId\":1,\"lastNameId\":182,\"breed\":\"Janssen\",\"skills\":{\"total\":80,\"speed\":40},\"premium\":100}]",
                    BodySha256 = "history-first",
                    SelectedFancierId = 42,
                });
            await db.SaveChangesAsync();
        }

        var reader = new PigeonHistoryReader(database.Factory);
        var result = await reader.GetHistoryAsync(42, 7);

        result.Pigeons.Should().ContainSingle();
        result.SelectedPigeon!.DisplayName.Should().Be("Tom Ape-head");
        result.SelectedPigeon.Breed.Should().Be("Janssen");
        // Points are returned newest-first (the reader reverses after computing
        // deltas), so Points[0] is the most recent observation. The UI relies on
        // this (PigeonHistoryView treats Points[0] as the latest).
        result.Points.Should().HaveCount(2);
        result.Points[0].ObservedAtUtc.Should().Be(secondObserved);
        result.Points[0].TotalSkill.Should().Be(88m);
        result.Points[1].ObservedAtUtc.Should().Be(firstObserved);
        result.Points[1].TotalSkill.Should().Be(86m);
        result.Points[1].SourceSnapshotId.Should().NotBe(result.Points[0].SourceSnapshotId);
    }

    [Fact]
    public async Task Loads_observed_api_pigeon_shape_and_uses_all_snapshots_without_snapshot_selection()
    {
        await using var database = await TestDatabase.CreateAsync();
        var firstObserved = DateTimeOffset.UtcNow.AddDays(-2);
        var secondObserved = DateTimeOffset.UtcNow.AddDays(-1);
        await using (var db = await database.Factory.CreateDbContextAsync())
        {
            db.RawApiSnapshots.AddRange(
                new RawApiSnapshotEntity
                {
                    Endpoint = "/api/pigeon",
                    HttpMethod = "GET",
                    StatusCode = 200,
                    CapturedAtUtc = secondObserved,
                    ResponseBodyJson = "[{\"id\":59,\"sex\":false,\"breed\":3,\"years\":6,\"months\":0,\"premium\":21,\"skills\":{\"total\":9},\"training\":{\"technique\":false}}]",
                    BodySha256 = "observed-history-second",
                    SelectedFancierId = 42,
                },
                new RawApiSnapshotEntity
                {
                    Endpoint = "/api/pigeon",
                    HttpMethod = "GET",
                    StatusCode = 200,
                    CapturedAtUtc = firstObserved,
                    ResponseBodyJson = "[{\"id\":59,\"sex\":false,\"breed\":3,\"years\":6,\"months\":0,\"premium\":20,\"skills\":{\"total\":8},\"training\":{}}]",
                    BodySha256 = "observed-history-first",
                    SelectedFancierId = 42,
                });
            await db.SaveChangesAsync();
        }

        var reader = new PigeonHistoryReader(database.Factory);
        var result = await reader.GetHistoryAsync(42);

        result.Pigeons.Should().ContainSingle();
        result.SelectedPigeon!.SourceId.Should().Be(59);
        // Newest-first: Points[0] is the more recent snapshot (total 9 -> 15).
        result.Points.Should().HaveCount(2);
        result.Points[0].TotalSkill.Should().Be(15);
        result.Points[1].TotalSkill.Should().Be(14);
    }

    private sealed class TestDatabase : IAsyncDisposable
    {
        private readonly SqliteConnection connection;

        private TestDatabase(SqliteConnection connection, TestDbContextFactory factory)
        {
            this.connection = connection;
            Factory = factory;
        }

        public TestDbContextFactory Factory { get; }

        public static async Task<TestDatabase> CreateAsync()
        {
            var connection = new SqliteConnection("Data Source=:memory:");
            await connection.OpenAsync();
            var options = new DbContextOptionsBuilder<AppDbContext>()
                .UseSqlite(connection)
                .Options;
            var factory = new TestDbContextFactory(options);
            await using var db = factory.CreateDbContext();
            await db.Database.EnsureCreatedAsync();
            return new TestDatabase(connection, factory);
        }

        public ValueTask DisposeAsync() => connection.DisposeAsync();
    }

    private sealed class TestDbContextFactory(DbContextOptions<AppDbContext> options)
        : IDbContextFactory<AppDbContext>
    {
        public AppDbContext CreateDbContext() => new(options);

        public Task<AppDbContext> CreateDbContextAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(CreateDbContext());
    }
}