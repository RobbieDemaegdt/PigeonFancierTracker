using FluentAssertions;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using PigeonFancierTracker.Core.Contracts;
using PigeonFancierTracker.Infrastructure.Persistence;
using PigeonFancierTracker.Infrastructure.PigeonFancierApi;

namespace PigeonFancierTracker.Infrastructure.Tests;

/// <summary>
/// Covers a historical bug where <see cref="FlightResultIngester"/> overwrote a
/// flight's nominal <see cref="FlightEntity.DistanceKm"/> (from <c>/api/flight/{id}</c>)
/// with the average of the user's own pigeons' individual home-to-release distances.
/// That corrupted the flight's canonical distance/category and, combined with
/// per-pigeon classification elsewhere, could make one flight appear under two
/// different distance categories. These tests cover both the fix (no more overwrite
/// on ingestion) and the one-time repair of data already corrupted by the bug.
/// </summary>
public sealed class FlightResultIngesterDistanceRepairTests
{
    private const int FancierId = 42;
    private const int FlightId = 41;
    private const int PigeonAId = 500;
    private const int PigeonBId = 501;

    [Fact]
    public async Task Ingesting_results_does_not_overwrite_the_flights_nominal_distance_with_a_pigeon_average()
    {
        await using var db = await TestDatabase.CreateAsync();
        await SeedPigeonRosterAsync(db);

        // Nominal flight distance is 490 (Middle). The two pigeons' individual
        // home-to-release distances (495 and 515) average to 505, which would flip
        // the flight into Long under the old (buggy) averaging behavior.
        var transport = new StubTransport(path => path switch
        {
            _ when path == $"/api/pigeon/{PigeonAId}/results" => Ok(PigeonResultsJson),
            _ when path == $"/api/pigeon/{PigeonBId}/results" => Ok(PigeonResultsJson),
            _ when path == $"/api/flight/{FlightId}" => Ok(FlightJson),
            _ when path == $"/api/flight/{FlightId}/results" => Ok(ResultsJson),
            _ => NotFound(),
        });

        var ingester = CreateIngester(db, transport);
        await ingester.IngestAsync(FancierId);

        var flight = await LoadFlightAsync(db);
        flight.DistanceKm.Should().Be(490);
        flight.DistanceCategory.Should().Be("Middle");
    }

    [Fact]
    public async Task Repair_restores_a_flights_distance_from_its_cached_discovery_snapshot()
    {
        await using var db = await TestDatabase.CreateAsync();
        await SeedPigeonRosterAsync(db);

        // Simulate a flight already corrupted by the historical averaging bug: stored
        // DistanceKm (505/Long) no longer matches the nominal distance (490) that was
        // cached in the raw /api/flight/{id} snapshot at discovery time.
        await SeedCorruptedFlightAsync(db);
        await SeedFlightDiscoverySnapshotAsync(db);

        var transport = new StubTransport(path => path switch
        {
            _ when path == $"/api/pigeon/{PigeonAId}/results" => Ok(PigeonResultsJson),
            _ => NotFound(),
        });

        var ingester = CreateIngester(db, transport);
        await ingester.IngestAsync(FancierId);

        var flight = await LoadFlightAsync(db);
        flight.DistanceKm.Should().Be(490);
        flight.DistanceCategory.Should().Be("Middle");
    }

    private static FlightResultIngester CreateIngester(TestDatabase db, StubTransport transport)
    {
        var apiClient = new PigeonFancierApiClient(transport);
        var snapshotStore = new RawSnapshotStore(db.Factory);
        return new FlightResultIngester(db.Factory, apiClient, snapshotStore);
    }

    private static async Task<FlightEntity> LoadFlightAsync(TestDatabase db)
    {
        await using var context = db.Factory.CreateDbContext();
        return await context.Flights.SingleAsync(f => f.Id == FlightId);
    }

    private static async Task SeedPigeonRosterAsync(TestDatabase db)
    {
        await using var context = db.Factory.CreateDbContext();
        context.RawApiSnapshots.Add(new RawApiSnapshotEntity
        {
            Endpoint = "/api/pigeon",
            HttpMethod = "GET",
            StatusCode = 200,
            CapturedAtUtc = DateTimeOffset.UtcNow,
            ResponseBodyJson = $$"""[{"id":{{PigeonAId}}},{"id":{{PigeonBId}}}]""",
            BodySha256 = "seed",
            SelectedFancierId = FancierId,
        });
        await context.SaveChangesAsync();
    }

    private static async Task SeedCorruptedFlightAsync(TestDatabase db)
    {
        await using var context = db.Factory.CreateDbContext();
        context.Flights.Add(new FlightEntity
        {
            Id = FlightId,
            Season = 1,
            Department = 2,
            Type = "regional",
            PayoutType = "prize",
            Status = "ended",
            Start = new DateTime(2026, 8, 20, 8, 0, 0),
            DistanceKm = 505,
            DistanceCategory = "Long",
            AgeType = "elder",
            EntryPrice = 5,
            Subscribers = 60,
            DetectedAtUtc = DateTimeOffset.UtcNow,
            ResultsFetchedAtUtc = DateTimeOffset.UtcNow,
        });
        await context.SaveChangesAsync();
    }

    private static async Task SeedFlightDiscoverySnapshotAsync(TestDatabase db)
    {
        await using var context = db.Factory.CreateDbContext();
        context.RawApiSnapshots.Add(new RawApiSnapshotEntity
        {
            Endpoint = $"/api/flight/{FlightId}",
            HttpMethod = "GET",
            StatusCode = 200,
            CapturedAtUtc = DateTimeOffset.UtcNow.AddDays(-1),
            ResponseBodyJson = FlightJson,
            BodySha256 = "seed-flight",
            SelectedFancierId = FancierId,
        });
        await context.SaveChangesAsync();
    }

    private static string PigeonResultsJson => "[{\"flight\":{\"id\":" + FlightId + "}}]";

    private static string FlightJson => $$"""
        {
            "id": {{FlightId}},
            "ageType": "elder",
            "start": "2026-08-20T08:00:00",
            "location": { "id": 1, "name": "Test", "lat": 50.0, "lng": 4.0 },
            "public": false,
            "department": 2,
            "season": 1,
            "seasonStart": "2026-08-01T00:00:00",
            "entryPrice": 5,
            "type": "regional",
            "payoutType": "prize",
            "status": "ended",
            "progress": 100,
            "canSubscribe": false,
            "fancierId": null,
            "distance": 490,
            "subscribers": 60,
            "invitations": []
        }
        """;

    private static string ResultsJson => $$"""
        {
            "items": [
                {
                    "id": 1, "position": 1, "points": 50, "averageSpeed": 1450.5,
                    "ageType": 4, "agePosition": 1, "currentSpeed": 0,
                    "distance": 495, "remainingDistance": 0, "direction": 100,
                    "progress": 100, "pigeonId": {{PigeonAId}}, "firstNameId": null, "lastNameId": null,
                    "fancierId": {{FancierId}}, "fancier": "Me"
                },
                {
                    "id": 2, "position": 2, "points": 40, "averageSpeed": 1440.2,
                    "ageType": 4, "agePosition": 2, "currentSpeed": 0,
                    "distance": 515, "remainingDistance": 0, "direction": 100,
                    "progress": 100, "pigeonId": {{PigeonBId}}, "firstNameId": null, "lastNameId": null,
                    "fancierId": {{FancierId}}, "fancier": "Me"
                }
            ],
            "count": 2,
            "page": 1
        }
        """;

    private static TransportResponse Ok(string body) =>
        new(200, "application/json", body, new Dictionary<string, string>());

    private static TransportResponse NotFound() =>
        new(404, "application/json", "", new Dictionary<string, string>());

    private sealed class StubTransport(Func<string, TransportResponse> responder) : IAuthenticatedReadTransport
    {
        public Task<TransportResponse> GetJsonAsync(
            string path,
            IReadOnlyDictionary<string, string?>? query = null,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(responder(path));
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
