using FluentAssertions;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using PigeonFancierTracker.Core.Contracts;
using PigeonFancierTracker.Infrastructure.Persistence;
using PigeonFancierTracker.Infrastructure.PigeonFancierApi;

namespace PigeonFancierTracker.Infrastructure.Tests;

/// <summary>
/// Covers the Step 6 age-category capture in <see cref="FlightResultIngester"/>:
/// the three gates (national only, flight-day only, once only) and the mapping of
/// each ageType filter's <c>count</c> onto the flight's stored columns.
/// </summary>
public sealed class FlightResultIngesterAgeCategoryTests
{
    private const int FancierId = 42;
    private const int FlightId = 3;

    [Fact]
    public async Task Captures_elder_yearling_youth_counts_for_a_national_flight_dated_today()
    {
        await using var db = await TestDatabase.CreateAsync();
        await SeedPigeonRosterAsync(db);
        await SeedFlightAsync(db, type: "national", start: Today(), captured: false);

        var transport = new StubTransport();
        await RunIngestAsync(db, transport);

        var flight = await LoadFlightAsync(db);
        flight.AgeCategoryElderCount.Should().Be(700);
        flight.AgeCategoryYearlingCount.Should().Be(200);
        flight.AgeCategoryYouthCount.Should().Be(100);
        flight.AgeCategoryCountsCapturedAtUtc.Should().NotBeNull();
        transport.AgeCategoryCalls.Should().Be(3);
    }

    [Fact]
    public async Task Skips_a_national_flight_that_is_not_dated_today()
    {
        await using var db = await TestDatabase.CreateAsync();
        await SeedPigeonRosterAsync(db);
        await SeedFlightAsync(db, type: "national", start: Today().AddDays(-1), captured: false);

        var transport = new StubTransport();
        await RunIngestAsync(db, transport);

        var flight = await LoadFlightAsync(db);
        flight.AgeCategoryCountsCapturedAtUtc.Should().BeNull();
        flight.AgeCategoryElderCount.Should().BeNull();
        transport.AgeCategoryCalls.Should().Be(0);
    }

    [Fact]
    public async Task Skips_a_national_flight_whose_counts_were_already_captured()
    {
        await using var db = await TestDatabase.CreateAsync();
        await SeedPigeonRosterAsync(db);
        await SeedFlightAsync(db, type: "national", start: Today(), captured: true);

        var transport = new StubTransport();
        await RunIngestAsync(db, transport);

        var flight = await LoadFlightAsync(db);
        // Sentinel values written at seed time must be left untouched.
        flight.AgeCategoryElderCount.Should().Be(111);
        flight.AgeCategoryYearlingCount.Should().Be(222);
        flight.AgeCategoryYouthCount.Should().Be(333);
        transport.AgeCategoryCalls.Should().Be(0);
    }

    [Fact]
    public async Task Skips_a_regional_flight_dated_today()
    {
        await using var db = await TestDatabase.CreateAsync();
        await SeedPigeonRosterAsync(db);
        await SeedFlightAsync(db, type: "regional", start: Today(), captured: false);

        var transport = new StubTransport();
        await RunIngestAsync(db, transport);

        var flight = await LoadFlightAsync(db);
        flight.AgeCategoryCountsCapturedAtUtc.Should().BeNull();
        flight.AgeCategoryElderCount.Should().BeNull();
        transport.AgeCategoryCalls.Should().Be(0);
    }

    private static DateTime Today() => DateTime.Now.Date.AddHours(9);

    private static async Task RunIngestAsync(TestDatabase db, StubTransport transport)
    {
        var apiClient = new PigeonFancierApiClient(transport);
        var snapshotStore = new RawSnapshotStore(db.Factory);
        var ingester = new FlightResultIngester(db.Factory, apiClient, snapshotStore);
        await ingester.IngestAsync(FancierId);
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
            ResponseBodyJson = """[{"id":500}]""",
            BodySha256 = "seed",
            SelectedFancierId = FancierId,
        });
        await context.SaveChangesAsync();
    }

    private static async Task SeedFlightAsync(TestDatabase db, string type, DateTime start, bool captured)
    {
        await using var context = db.Factory.CreateDbContext();
        context.Flights.Add(new FlightEntity
        {
            Id = FlightId,
            Season = 1,
            Department = 2,
            Type = type,
            PayoutType = "prize",
            Status = "started",
            Start = start,
            DistanceKm = 500,
            DistanceCategory = "Long",
            AgeType = "all",
            EntryPrice = 0,
            Subscribers = 1000,
            DetectedAtUtc = DateTimeOffset.UtcNow,
            AgeCategoryElderCount = captured ? 111 : null,
            AgeCategoryYearlingCount = captured ? 222 : null,
            AgeCategoryYouthCount = captured ? 333 : null,
            AgeCategoryCountsCapturedAtUtc = captured ? DateTimeOffset.UtcNow : null,
        });
        await context.SaveChangesAsync();
    }

    private static TransportResponse Ok(string body) =>
        new(200, "application/json", body, new Dictionary<string, string>());

    private static TransportResponse NotFound() =>
        new(404, "application/json", "", new Dictionary<string, string>());

    /// <summary>
    /// Answers pigeon-results discovery and the three ageType-filtered flight
    /// results queries, returning a distinct <c>count</c> per category and tallying
    /// how many age-category calls were made.
    /// </summary>
    private sealed class StubTransport : IAuthenticatedReadTransport
    {
        public int AgeCategoryCalls { get; private set; }

        public Task<TransportResponse> GetJsonAsync(
            string path,
            IReadOnlyDictionary<string, string?>? query = null,
            CancellationToken cancellationToken = default)
        {
            if (path == "/api/pigeon/500/results")
                return Task.FromResult(Ok("[{\"flight\":{\"id\":" + FlightId + "}}]"));

            if (path == $"/api/flight/{FlightId}/results"
                && query is not null
                && query.TryGetValue("ageType", out var ageType)
                && ageType is not null)
            {
                AgeCategoryCalls++;
                var count = ageType switch
                {
                    "Elder" => 700,
                    "Yearling" => 200,
                    "Youth" => 100,
                    _ => 0,
                };
                return Task.FromResult(Ok("{\"items\":[],\"count\":" + count + ",\"page\":1}"));
            }

            return Task.FromResult(NotFound());
        }
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
