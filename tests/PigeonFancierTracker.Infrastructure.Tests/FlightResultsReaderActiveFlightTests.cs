using FluentAssertions;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using PigeonFancierTracker.Core.Contracts;
using PigeonFancierTracker.Infrastructure.Persistence;
using PigeonFancierTracker.Infrastructure.PigeonFancierApi;

namespace PigeonFancierTracker.Infrastructure.Tests;

public sealed class FlightResultsReaderActiveFlightTests
{
    private const int FancierId = 42;
    private const int FlightId = 41;

    // A live flight's results carry fractional tracking values (the birds are mid-air):
    // remainingDistance, distance, progress and direction all come back as decimals,
    // even though the corresponding FlightResultDto fields are typed int. Before the
    // LenientIntConverter fix a single fractional value threw a JsonException on the
    // first item, so the whole payload failed and both klassement grids came back empty.
    [Fact]
    public async Task GetActiveFlights_fills_klassement_when_live_results_have_fractional_fields()
    {
        await using var database = await TestDatabase.CreateAsync();

        var transport = new StubTransport(path =>
        {
            if (path == "/api/flight/live")
                return Ok(LiveFlightJson);
            if (path == $"/api/flight/{FlightId}/results")
                return Ok(LiveResultsJson);
            return NotFound();
        });

        var reader = new FlightResultsReader(database.Factory, new PigeonFancierApiClient(transport));

        var active = await reader.GetActiveFlightsAsync(FancierId);

        active.Should().ContainSingle();
        var flight = active[0];

        // The regression: both grids populate rather than coming back empty.
        flight.PigeonStandings.Should().HaveCount(2);
        flight.FancierStandings.Should().HaveCount(2);

        // Fractional remainingDistance (156.7) is rounded into the int field.
        var leader = flight.PigeonStandings.Single(p => p.Position == 1);
        leader.RemainingDistance.Should().Be(157);
        leader.FancierId.Should().Be(99);

        // Points are computed from the 60-subscriber regional prize table:
        // position 1 -> 50, position 2 -> 40.
        leader.Points.Should().Be(50);
        flight.PigeonStandings.Single(p => p.Position == 2).Points.Should().Be(40);

        // The owning fancier is flagged and aggregated.
        var ownStanding = flight.FancierStandings.Single(f => f.FancierId == FancierId);
        ownStanding.IsOwn.Should().BeTrue();
        ownStanding.TotalPoints.Should().Be(40);

        // Fancier prize money is a flat 10 euro per point: 40 points -> 400 euro.
        ownStanding.TotalPrizeMoney.Should().Be(400m);
    }

    // id 41 matches FlightId; kept literal because interpolated strings can't be const.
    private const string LiveFlightJson = """
        [
            {
                "id": 41,
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
                "status": "started",
                "progress": 31,
                "canSubscribe": false,
                "fancierId": null,
                "distance": 100,
                "subscribers": 60,
                "invitations": []
            }
        ]
        """;

    private const string LiveResultsJson = """
        {
            "items": [
                {
                    "id": 1, "position": 1, "points": 0, "averageSpeed": 1450.5,
                    "ageType": 4, "agePosition": 1, "currentSpeed": 72.5,
                    "distance": 99.4, "remainingDistance": 156.7, "direction": 182.9,
                    "progress": 30.6, "pigeonId": 500, "firstNameId": null, "lastNameId": null,
                    "fancierId": 99, "fancier": "Alice"
                },
                {
                    "id": 2, "position": 2, "points": 0, "averageSpeed": 1440.2,
                    "ageType": 4, "agePosition": 2, "currentSpeed": 70.1,
                    "distance": 98.2, "remainingDistance": 160.3, "direction": 181.4,
                    "progress": 29.9, "pigeonId": 501, "firstNameId": null, "lastNameId": null,
                    "fancierId": 42, "fancier": "Me"
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
