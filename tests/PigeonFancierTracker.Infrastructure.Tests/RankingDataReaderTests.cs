using FluentAssertions;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using PigeonFancierTracker.Infrastructure.Persistence;

namespace PigeonFancierTracker.Infrastructure.Tests;

public sealed class RankingDataReaderTests
{
    private const int FancierId = 19;

    private const string RegionalFancierQuery =
        "activeSort=position&ageType=elder&department=2&page=1&pageSize=500&rankingType=regional&season=1&sortDirection=asc&type=fanciers";

    private const string RegionalPigeonQuery =
        "activeSort=position&ageType=elder&department=2&page=1&pageSize=500&rankingType=regional&season=1&sortDirection=asc&type=pigeons";

    // The exact live /api/ranking response for regional fanciers.
    private const string RegionalFancierBody = """
        {"items":[
        {"fancierId":19,"fancier":{"id":19,"displayName":"Robshot","bankrupt":false},"points":165,"position":1},
        {"fancierId":20,"fancier":{"id":20,"displayName":"Bierkaert","bankrupt":false},"points":160,"position":2},
        {"fancierId":30,"fancier":{"id":30,"displayName":"John ","bankrupt":false},"points":150,"position":3},
        {"fancierId":27,"fancier":{"id":27,"displayName":"Tanie","bankrupt":false},"points":150,"position":4},
        {"fancierId":33,"fancier":{"id":33,"displayName":"Roo","bankrupt":false},"points":150,"position":5},
        {"fancierId":22,"fancier":{"id":22,"displayName":"Roekoeloos","bankrupt":false},"points":140,"position":6},
        {"fancierId":18,"fancier":{"id":18,"displayName":"Elsie L.","bankrupt":false},"points":130,"position":7},
        {"fancierId":34,"fancier":{"id":34,"displayName":"romain","bankrupt":false},"points":105,"position":8},
        {"fancierId":17,"fancier":{"id":17,"displayName":"Juve","bankrupt":false},"points":105,"position":9},
        {"fancierId":25,"fancier":{"id":25,"displayName":"Simpele Duif","bankrupt":false},"points":100,"position":10},
        {"fancierId":24,"fancier":{"id":24,"displayName":"Hermanos","bankrupt":false},"points":100,"position":11},
        {"fancierId":32,"fancier":{"id":32,"displayName":"rickie","bankrupt":false},"points":85,"position":12},
        {"fancierId":21,"fancier":{"id":21,"displayName":"the old man","bankrupt":false},"points":75,"position":13},
        {"fancierId":23,"fancier":{"id":23,"displayName":"Jantje","bankrupt":false},"points":70,"position":14},
        {"fancierId":29,"fancier":{"id":29,"displayName":"Alaf","bankrupt":false},"points":40,"position":15},
        {"fancierId":26,"fancier":{"id":26,"displayName":"Hokmeester","bankrupt":false},"points":35,"position":16}
        ],"count":16,"page":1}
        """;

    [Fact]
    public async Task Returns_empty_ranking_when_no_snapshots_exist()
    {
        await using var database = await TestDatabase.CreateAsync();
        var reader = new RankingDataReader(database.Factory);

        var result = await reader.GetRankingAsync(FancierId);

        result.RegionalFanciers.Should().BeEmpty();
        result.RegionalPigeons.Should().BeEmpty();
    }

    [Fact]
    public async Task Regional_fancier_ranking_resolves_nested_fancier_names()
    {
        await using var database = await TestDatabase.CreateAsync();
        await SeedAsync(database.Factory, CreateSnapshot(RegionalFancierQuery, RegionalFancierBody));

        var reader = new RankingDataReader(database.Factory);
        var result = await reader.GetRankingAsync(FancierId);

        result.RegionalFanciers.Should().HaveCount(16);

        var leader = result.RegionalFanciers[0];
        leader.Position.Should().Be(1);
        leader.Name.Should().Be("Robshot");
        leader.Id.Should().Be(19);
        leader.Points.Should().Be(165);
        leader.IsOwn.Should().BeTrue();

        result.RegionalFanciers.Should().OnlyContain(e => !e.Name.StartsWith('#'));
        result.RegionalFanciers.Count(e => e.IsOwn).Should().Be(1);
        result.RegionalFanciers.Last().Name.Should().Be("Hokmeester");
    }

    [Fact]
    public async Task Regional_pigeon_ranking_resolves_nested_fancier_name()
    {
        await using var database = await TestDatabase.CreateAsync();
        const string body = """
            {"items":[
            {"pigeonId":150,"pigeon":{"id":150,"displayName":"Speedy"},"fancierId":19,"fancier":{"id":19,"displayName":"Robshot"},"points":90,"position":1},
            {"pigeonId":300,"pigeon":{"id":300,"displayName":"Bolt"},"fancierId":20,"fancier":{"id":20,"displayName":"Bierkaert"},"points":80,"position":2}
            ],"count":2,"page":1}
            """;
        await SeedAsync(database.Factory, CreateSnapshot(RegionalPigeonQuery, body));

        var reader = new RankingDataReader(database.Factory);
        var result = await reader.GetRankingAsync(FancierId);

        result.RegionalPigeons.Should().HaveCount(2);

        var top = result.RegionalPigeons[0];
        top.PigeonName.Should().Be("Speedy");
        top.FancierName.Should().Be("Robshot");
        top.FancierId.Should().Be(19);
        top.IsOwn.Should().BeTrue();
        result.RegionalPigeons.Should().OnlyContain(e => !e.FancierName.StartsWith('#'));
    }

    [Fact]
    public async Task Legacy_flat_fancier_ranking_still_parses()
    {
        await using var database = await TestDatabase.CreateAsync();
        const string body = """
            {"items":[
            {"id":19,"displayName":"Robshot","points":165,"position":1},
            {"id":20,"displayName":"Bierkaert","points":160,"position":2}
            ]}
            """;
        await SeedAsync(database.Factory, CreateSnapshot(RegionalFancierQuery, body));

        var reader = new RankingDataReader(database.Factory);
        var result = await reader.GetRankingAsync(FancierId);

        result.RegionalFanciers.Should().HaveCount(2);
        result.RegionalFanciers[0].Name.Should().Be("Robshot");
        result.RegionalFanciers[0].IsOwn.Should().BeTrue();
    }

    private static RawApiSnapshotEntity CreateSnapshot(string normalizedQuery, string json) =>
        new()
        {
            Endpoint = "/api/ranking",
            NormalizedQuery = normalizedQuery,
            HttpMethod = "GET",
            StatusCode = 200,
            CapturedAtUtc = DateTimeOffset.UtcNow,
            ResponseBodyJson = json,
            BodySha256 = Guid.NewGuid().ToString(),
            SelectedFancierId = FancierId,
        };

    private static async Task SeedAsync(IDbContextFactory<AppDbContext> factory, params RawApiSnapshotEntity[] entities)
    {
        await using var db = await factory.CreateDbContextAsync();
        db.RawApiSnapshots.AddRange(entities);
        await db.SaveChangesAsync();
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
