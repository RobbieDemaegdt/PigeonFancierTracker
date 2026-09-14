using FluentAssertions;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using PigeonFancierTracker.Core.Contracts;
using PigeonFancierTracker.Core.Domain;
using PigeonFancierTracker.Infrastructure.Persistence;
using PigeonFancierTracker.Infrastructure.PigeonFancierApi;

namespace PigeonFancierTracker.Infrastructure.Tests;

/// <summary>
/// Regression coverage for a bug where a single flight could be counted under two
/// different distance categories: <see cref="FlightResultsReader.GetFlightResultsAsync"/>
/// used to classify each result by the pigeon's individual home-to-release distance
/// (which varies per pigeon) instead of the flight's own nominal distance, so two
/// pigeons on the same flight with distances straddling a Short/Middle/Long boundary
/// produced two different categories for what is really one flight.
/// </summary>
public sealed class FlightResultsReaderDistanceClassificationTests
{
    private const int FancierId = 42;
    private const int FlightId = 41;

    [Fact]
    public async Task Two_pigeons_on_the_same_flight_share_the_flights_category_even_when_home_distances_straddle_a_boundary()
    {
        await using var database = await TestDatabase.CreateAsync();

        // Flight's nominal distance (490) sits just under the Middle/Long boundary (500).
        await SeedFlightAsync(database, distanceKm: 490);

        // One pigeon's individual home-to-release distance is under 500, the other is
        // over it. Before the fix, classifying per-result by PigeonDistance would put
        // pigeon 100 in Middle and pigeon 101 in Long, fragmenting one flight into two
        // categories.
        await SeedResultAsync(database, pigeonId: 100, pigeonDistance: 480);
        await SeedResultAsync(database, pigeonId: 101, pigeonDistance: 510);

        var reader = new FlightResultsReader(database.Factory, new PigeonFancierApiClient(new StubTransport()));

        var page = await reader.GetFlightResultsAsync(FancierId);

        page.RecentResults.Should().HaveCount(2);
        page.RecentResults.Should().OnlyContain(r => r.Category == DistanceCategory.Middle);
        page.RecentResults.Should().OnlyContain(r => r.FlightDistanceKm == 490);

        // FoodImpactCalculator (and everything else that groups by Category) counts
        // distinct FlightId per category, so this must resolve to exactly one Middle
        // flight and zero Long flights - not one flight double-counted across both.
        var pigeon101Profile = page.PigeonProfiles.Single(p => p.PigeonId == 101);
        pigeon101Profile.MiddleRaces.Should().Be(1);
        pigeon101Profile.LongRaces.Should().Be(0);
    }

    private static async Task SeedFlightAsync(TestDatabase database, int distanceKm)
    {
        await using var context = database.Factory.CreateDbContext();
        context.Flights.Add(new FlightEntity
        {
            Id = FlightId,
            Season = 1,
            Department = 2,
            Type = "regional",
            PayoutType = "prize",
            Status = "ended",
            Start = new DateTime(2026, 8, 20, 8, 0, 0),
            DistanceKm = distanceKm,
            DistanceCategory = DistanceCategory.Middle.ToString(),
            AgeType = "elder",
            EntryPrice = 5,
            Subscribers = 60,
            DetectedAtUtc = DateTimeOffset.UtcNow,
            ResultsFetchedAtUtc = DateTimeOffset.UtcNow,
        });
        await context.SaveChangesAsync();
    }

    private static async Task SeedResultAsync(TestDatabase database, int pigeonId, int pigeonDistance)
    {
        await using var context = database.Factory.CreateDbContext();
        context.FlightResults.Add(new FlightResultEntity
        {
            FlightId = FlightId,
            PigeonId = pigeonId,
            FancierId = FancierId,
            Position = 1,
            TotalParticipants = 60,
            Points = 10,
            AverageSpeed = 1400m,
            PigeonDistance = pigeonDistance,
            PigeonName = $"Duif {pigeonId}",
            DetectedAtUtc = DateTimeOffset.UtcNow,
        });
        await context.SaveChangesAsync();
    }

    private sealed class StubTransport : IAuthenticatedReadTransport
    {
        public Task<TransportResponse> GetJsonAsync(
            string path,
            IReadOnlyDictionary<string, string?>? query = null,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(new TransportResponse(404, "application/json", "", new Dictionary<string, string>()));
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
