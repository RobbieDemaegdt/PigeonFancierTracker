using System.Security.Cryptography;
using System.Text;
using FluentAssertions;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using PigeonFancierTracker.Core.Contracts;
using PigeonFancierTracker.Infrastructure.Persistence;

namespace PigeonFancierTracker.Infrastructure.Tests;

public sealed class RawSnapshotStoreTests
{
    [Fact]
    public void NormalizeQuery_returns_empty_for_null()
    {
        RawSnapshotStore.NormalizeQuery(null).Should().BeEmpty();
    }

    [Fact]
    public void NormalizeQuery_returns_empty_for_empty_dict()
    {
        RawSnapshotStore.NormalizeQuery(new Dictionary<string, string?>()).Should().BeEmpty();
    }

    [Fact]
    public void NormalizeQuery_sorts_by_key_ordinal()
    {
        var query = new Dictionary<string, string?> { ["b"] = "2", ["a"] = "1" };

        RawSnapshotStore.NormalizeQuery(query).Should().Be("a=1&b=2");
    }

    [Fact]
    public void NormalizeQuery_filters_null_values()
    {
        var query = new Dictionary<string, string?> { ["a"] = null, ["b"] = "1" };

        RawSnapshotStore.NormalizeQuery(query).Should().Be("b=1");
    }

    [Fact]
    public void NormalizeQuery_filters_whitespace_keys()
    {
        var query = new Dictionary<string, string?> { [" "] = "x", ["a"] = "1" };

        RawSnapshotStore.NormalizeQuery(query).Should().Be("a=1");
    }

    [Fact]
    public void NormalizeQuery_escapes_special_characters()
    {
        var query = new Dictionary<string, string?> { ["k&y"] = "v=l" };

        RawSnapshotStore.NormalizeQuery(query).Should().Be("k%26y=v%3Dl");
    }

    [Fact]
    public async Task SaveAsync_persists_snapshot_and_returns_id()
    {
        await using var database = await TestDatabase.CreateAsync();
        var store = new RawSnapshotStore(database.Factory);
        var response = new TransportResponse(200, "application/json", "{\"ok\":true}", new Dictionary<string, string>());

        var id = await store.SaveAsync("/api/test", null, response);

        id.Should().BeGreaterThan(0);
        await using var db = database.Factory.CreateDbContext();
        var entity = await db.RawApiSnapshots.SingleAsync(x => x.Id == id);
        entity.Endpoint.Should().Be("/api/test");
        entity.StatusCode.Should().Be(200);
        entity.ContentType.Should().Be("application/json");
        entity.ResponseBodyJson.Should().Be("{\"ok\":true}");
    }

    [Fact]
    public async Task SaveAsync_computes_lowercase_hex_sha256_hash()
    {
        await using var database = await TestDatabase.CreateAsync();
        var store = new RawSnapshotStore(database.Factory);
        var body = "{\"test\":true}";
        var response = new TransportResponse(200, "application/json", body, new Dictionary<string, string>());

        var id = await store.SaveAsync("/api/hash", null, response);

        var expectedHash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(body))).ToLowerInvariant();
        await using var db = database.Factory.CreateDbContext();
        var entity = await db.RawApiSnapshots.SingleAsync(x => x.Id == id);
        entity.BodySha256.Should().Be(expectedHash);
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
