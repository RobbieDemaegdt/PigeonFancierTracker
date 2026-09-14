using FluentAssertions;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using PigeonFancierTracker.Core.Contracts;
using PigeonFancierTracker.Core.Domain;
using PigeonFancierTracker.Infrastructure.PigeonFancierApi;
using PigeonFancierTracker.Infrastructure.Persistence;

namespace PigeonFancierTracker.Infrastructure.Tests;

/// <summary>
/// Covers how <see cref="FlightResultsReader"/> resolves a flight's effective
/// location and distance: a manual override wins, otherwise the great-circle
/// distance from the loft to the release point (correcting placeholder API
/// distances), otherwise the raw stored distance.
/// </summary>
public sealed class FlightResultsReaderOverrideTests
{
    private const int FancierId = 42;
    private const int FlightId = 41;

    // Loft in Lasne (BE); release point in Göteborg (SE) is ~919 km away — far past
    // the Middle/Long boundary of 500 km — yet the stored distance is a wrong 220 km.
    private const double HomeLat = 50.6864, HomeLng = 4.48444;
    private const double ReleaseLat = 57.7089, ReleaseLng = 11.9746;

    [Fact]
    public async Task Distance_is_recomputed_from_coordinates_when_no_override()
    {
        await using var database = await TestDatabase.CreateAsync();
        await SeedHomeLocationAsync(database);
        await SeedFlightAsync(database, storedDistanceKm: 220);
        await SeedResultAsync(database, pigeonId: 100);

        var reader = new FlightResultsReader(database.Factory, new PigeonFancierApiClient(new StubTransport()));

        var page = await reader.GetFlightResultsAsync(FancierId);

        var result = page.RecentResults.Single();
        result.FlightDistanceKm.Should().BeInRange(890, 950);
        result.Category.Should().Be(DistanceCategory.Long);
    }

    [Fact]
    public async Task Falls_back_to_stored_distance_when_home_location_is_unknown()
    {
        await using var database = await TestDatabase.CreateAsync();
        // No /api/fancier/selected snapshot -> no coordinates to compute from.
        await SeedFlightAsync(database, storedDistanceKm: 220);
        await SeedResultAsync(database, pigeonId: 100);

        var reader = new FlightResultsReader(database.Factory, new PigeonFancierApiClient(new StubTransport()));

        var page = await reader.GetFlightResultsAsync(FancierId);

        var result = page.RecentResults.Single();
        result.FlightDistanceKm.Should().Be(220);
        result.Category.Should().Be(DistanceCategory.Middle);
    }

    [Fact]
    public async Task Distance_override_wins_over_computed_and_stored()
    {
        await using var database = await TestDatabase.CreateAsync();
        await SeedHomeLocationAsync(database);
        await SeedFlightAsync(database, storedDistanceKm: 220);
        await SeedResultAsync(database, pigeonId: 100);

        var reader = new FlightResultsReader(database.Factory, new PigeonFancierApiClient(new StubTransport()));
        await reader.SaveFlightOverrideAsync(FlightId, locationOverride: null, distanceOverride: 150);

        var page = await reader.GetFlightResultsAsync(FancierId);

        var result = page.RecentResults.Single();
        result.FlightDistanceKm.Should().Be(150);
        result.Category.Should().Be(DistanceCategory.Short);
    }

    [Fact]
    public async Task Location_override_replaces_synced_location()
    {
        await using var database = await TestDatabase.CreateAsync();
        await SeedHomeLocationAsync(database);
        await SeedFlightAsync(database, storedDistanceKm: 220, locationName: "göteborg");
        await SeedResultAsync(database, pigeonId: 100);

        var reader = new FlightResultsReader(database.Factory, new PigeonFancierApiClient(new StubTransport()));
        await reader.SaveFlightOverrideAsync(FlightId, locationOverride: "  Gent  ", distanceOverride: null);

        var page = await reader.GetFlightResultsAsync(FancierId);

        page.RecentResults.Single().Location.Should().Be("Gent");
    }

    [Fact]
    public async Task Clearing_the_override_reverts_to_the_computed_distance()
    {
        await using var database = await TestDatabase.CreateAsync();
        await SeedHomeLocationAsync(database);
        await SeedFlightAsync(database, storedDistanceKm: 220);
        await SeedResultAsync(database, pigeonId: 100);

        var reader = new FlightResultsReader(database.Factory, new PigeonFancierApiClient(new StubTransport()));
        await reader.SaveFlightOverrideAsync(FlightId, locationOverride: null, distanceOverride: 150);
        await reader.SaveFlightOverrideAsync(FlightId, locationOverride: null, distanceOverride: null);

        var page = await reader.GetFlightResultsAsync(FancierId);

        var result = page.RecentResults.Single();
        result.FlightDistanceKm.Should().BeInRange(890, 950);
        result.Category.Should().Be(DistanceCategory.Long);
    }

    private static async Task SeedHomeLocationAsync(TestDatabase database)
    {
        await using var context = database.Factory.CreateDbContext();
        var lat = HomeLat.ToString(System.Globalization.CultureInfo.InvariantCulture);
        var lng = HomeLng.ToString(System.Globalization.CultureInfo.InvariantCulture);
        var json =
            "{\"id\":" + FancierId + ",\"displayName\":\"Test\",\"location\":" +
            "{\"id\":1,\"name\":\"lasne\",\"lat\":" + lat + ",\"lng\":" + lng + "}}";
        context.RawApiSnapshots.Add(new RawApiSnapshotEntity
        {
            Endpoint = "/api/fancier/selected",
            HttpMethod = "GET",
            StatusCode = 200,
            CapturedAtUtc = DateTimeOffset.UtcNow,
            ResponseBodyJson = json,
            BodySha256 = "test",
            SelectedFancierId = FancierId,
        });
        await context.SaveChangesAsync();
    }

    private static async Task SeedFlightAsync(TestDatabase database, int storedDistanceKm, string? locationName = "göteborg")
    {
        await using var context = database.Factory.CreateDbContext();
        context.Flights.Add(new FlightEntity
        {
            Id = FlightId,
            Season = 1,
            Department = 2,
            Type = "national",
            PayoutType = "prize",
            Status = "ended",
            Start = new DateTime(2026, 8, 23, 8, 0, 0),
            LocationName = locationName,
            LocationLat = ReleaseLat,
            LocationLng = ReleaseLng,
            DistanceKm = storedDistanceKm,
            DistanceCategory = DistanceCategory.Middle.ToString(),
            AgeType = "elder",
            EntryPrice = 5,
            Subscribers = 60,
            DetectedAtUtc = DateTimeOffset.UtcNow,
            ResultsFetchedAtUtc = DateTimeOffset.UtcNow,
        });
        await context.SaveChangesAsync();
    }

    private static async Task SeedResultAsync(TestDatabase database, int pigeonId)
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
            PigeonDistance = 900,
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
