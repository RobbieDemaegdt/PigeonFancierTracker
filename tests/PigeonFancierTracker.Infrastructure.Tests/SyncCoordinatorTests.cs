using System.Collections.Concurrent;
using FluentAssertions;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using PigeonFancierTracker.Core.Contracts;
using PigeonFancierTracker.Core.Domain;
using PigeonFancierTracker.Infrastructure.Persistence;
using PigeonFancierTracker.Infrastructure.PigeonFancierApi;
using PigeonFancierTracker.Infrastructure.Sync;
using PigeonFancierTracker.Infrastructure.Http;

namespace PigeonFancierTracker.Infrastructure.Tests;

public sealed class SyncCoordinatorTests
{
    [Fact]
    public async Task Quick_sync_captures_raw_responses_and_audits_each_endpoint()
    {
        await using var database = await TestDatabase.CreateAsync();
        var transport = new FakeTransport((_, _, _) => Success());
        var session = ReadySession();
        var coordinator = CreateCoordinator(database.Factory, transport, session);

        var result = await coordinator.SyncAsync(SyncProfile.Quick);

        result.Status.Should().Be("Succeeded");
        result.EndpointResults.Should().HaveCount(13);
        await using var db = database.Factory.CreateDbContext();
        db.RawApiSnapshots.Should().HaveCount(13);
        db.SyncRunItems.Should().HaveCount(13);
        db.SyncRuns.Single().Status.Should().Be("Succeeded");
        db.RawApiSnapshots.All(x => x.HttpMethod == "GET").Should().BeTrue();
    }

    [Fact]
    public async Task Retries_transient_status_once_and_keeps_the_final_success()
    {
        await using var database = await TestDatabase.CreateAsync();
        var transport = new FakeTransport((path, _, callNumber) =>
            path == "/api/weather" && callNumber == 1
                ? new TransportResponse(503, "application/json", "{\"temporary\":true}", new Dictionary<string, string>())
                : Success());
        var session = ReadySession();
        var coordinator = CreateCoordinator(database.Factory, transport, session);

        var result = await coordinator.SyncAsync(SyncProfile.Standard);

        result.Status.Should().Be("Succeeded");
        var weather = result.EndpointResults.Single(x => x.Endpoint == "/api/weather");
        weather.Attempts.Should().Be(2);
        weather.IsSuccess.Should().BeTrue();
        transport.GetCallCount("/api/weather").Should().Be(2);
    }

    [Fact]
    public async Task Does_not_retry_unauthorized_and_transitions_to_session_expired()
    {
        await using var database = await TestDatabase.CreateAsync();
        var transport = new FakeTransport((path, _, _) =>
            path == "/api/user"
                ? new TransportResponse(401, "application/json", "{}", new Dictionary<string, string>())
                : Success());
        var session = ReadySession();
        var coordinator = CreateCoordinator(database.Factory, transport, session);

        var result = await coordinator.SyncAsync(SyncProfile.Quick);

        result.Status.Should().Be("SessionExpired");
        transport.GetCallCount("/api/user").Should().Be(1);
        session.Current.State.Should().Be(SessionState.SessionExpired);
    }

    private static SyncCoordinator CreateCoordinator(
        IDbContextFactory<AppDbContext> factory,
        FakeTransport transport,
        SessionStateService session)
    {
        return new SyncCoordinator(
            new PigeonFancierApiClient(transport),
            new RawSnapshotStore(factory),
            factory,
            session);
    }

    private static SessionStateService ReadySession()
    {
        var session = new SessionStateService();
        session.SetState(
            SessionState.AuthenticatedReady,
            selectedFancier: new SelectedFancierDto(42, "Test fancier", null, null, null, null, null, null));
        return session;
    }

    private static TransportResponse Success() =>
        new(200, "application/json", "{}", new Dictionary<string, string>());

    private sealed class FakeTransport(
        Func<string, IReadOnlyDictionary<string, string?>?, int, TransportResponse> responseFactory)
        : IAuthenticatedReadTransport
    {
        private readonly ConcurrentDictionary<string, int> callCounts = new(StringComparer.Ordinal);

        public Task<TransportResponse> GetJsonAsync(
            string path,
            IReadOnlyDictionary<string, string?>? query = null,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var callNumber = callCounts.AddOrUpdate(path, 1, (_, count) => count + 1);
            return Task.FromResult(responseFactory(path, query, callNumber));
        }

        public int GetCallCount(string path) => callCounts.TryGetValue(path, out var count) ? count : 0;
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