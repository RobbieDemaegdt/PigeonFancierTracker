using FluentAssertions;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using PigeonFancierTracker.Core.Contracts;
using PigeonFancierTracker.Infrastructure.Persistence;

namespace PigeonFancierTracker.Infrastructure.Tests;

public sealed class TransferDataReaderTests
{
    private const int FancierId = 42;

    [Fact]
    public async Task Returns_empty_data_when_no_transfer_snapshots_exist()
    {
        await using var database = await TestDatabase.CreateAsync();
        var reader = new TransferDataReader(database.Factory, new StubTrackerDataReader());

        var result = await reader.GetTransferDataAsync(FancierId);

        result.ActiveTransfers.Should().BeEmpty();
        result.CompletedTransfers.Should().BeEmpty();
    }

    [Fact]
    public async Task Parses_active_transfers_from_latest_snapshot()
    {
        await using var database = await TestDatabase.CreateAsync();
        await SeedAsync(database.Factory, new RawApiSnapshotEntity
        {
            Endpoint = "/api/transfer",
            NormalizedQuery = "processed=false",
            HttpMethod = "GET",
            StatusCode = 200,
            CapturedAtUtc = DateTimeOffset.UtcNow,
            ResponseBodyJson = """
            [
                {"id":1,"startPrice":500,"price":750,"start":"2026-01-01T00:00:00Z","end":"2099-12-31T00:00:00Z",
                 "pigeon":{"id":10,"sex":"true","firstNameId":1,"lastNameId":2,"years":2,"months":3,"skills":{"total":80}},
                 "fancier":{"id":99,"displayName":"SellerName"},
                 "bidders":[1,2,3]},
                {"id":2,"startPrice":300,"price":300,"start":"2026-01-01T00:00:00Z","end":"2099-12-31T00:00:00Z",
                 "pigeon":{"id":11,"sex":"false","firstNameId":3,"lastNameId":null,"skills":null},
                 "fancier":{"id":99,"displayName":"SellerName"},
                 "bidders":[]}
            ]
            """,
            BodySha256 = "active-1",
            SelectedFancierId = FancierId,
        }, new RawApiSnapshotEntity
        {
            Endpoint = "/api/translation/nl",
            NormalizedQuery = "",
            HttpMethod = "GET",
            StatusCode = 200,
            CapturedAtUtc = DateTimeOffset.UtcNow,
            ResponseBodyJson = """{"first-name":{"1":"Tom","3":"Jan"},"last-name":{"2":"Ape-head"}}""",
            BodySha256 = "translation-1",
            SelectedFancierId = FancierId,
        });

        var reader = new TransferDataReader(database.Factory, new StubTrackerDataReader());
        var result = await reader.GetTransferDataAsync(FancierId);

        result.ActiveTransfers.Should().HaveCount(2);

        var first = result.ActiveTransfers.First();
        first.PigeonName.Should().Be("Tom Ape-head");
        first.Sex.Should().Be("♂");
        first.Age.Should().Be("2y 3m");
        first.StartPrice.Should().Be(500);
        first.BidCount.Should().Be(3);
        first.TotalSkill.Should().Be(86m);
        first.Seller.Should().Be("SellerName");

        var second = result.ActiveTransfers.Last();
        second.PigeonName.Should().Be("Jan");
        second.Sex.Should().Be("♀");
    }

    [Fact]
    public async Task Detects_sold_transfer_when_item_disappears_and_found_in_processed()
    {
        await using var database = await TestDatabase.CreateAsync();
        var now = DateTimeOffset.UtcNow;

        await SeedAsync(database.Factory,
            CreateActiveSnapshot(now, """
                [{"id":1,"startPrice":500,"pigeon":{"id":10,"firstNameId":1},"fancier":{"id":99,"displayName":"Seller"},"bidders":[1]}]
            """),
            CreateActiveSnapshot(now.AddMinutes(-5), """
                [{"id":1,"startPrice":500,"pigeon":{"id":10,"firstNameId":1},"fancier":{"id":99,"displayName":"Seller"},"bidders":[1]},
                 {"id":2,"startPrice":300,"pigeon":{"id":11,"firstNameId":2},"fancier":{"id":99,"displayName":"Seller"},"bidders":[1,2]}]
            """),
            CreateProcessedSnapshot(now, """
                [{"id":2,"startPrice":300,"price":800,"pigeon":{"id":11,"firstNameId":2},"fancier":{"id":99,"displayName":"Seller"},
                  "buyer":{"id":50,"displayName":"BuyerName"},"bidders":[1,2]}]
            """),
            CreateTranslationSnapshot());

        var reader = new TransferDataReader(database.Factory, new StubTrackerDataReader());
        var result = await reader.GetTransferDataAsync(FancierId);

        result.CompletedTransfers.Should().ContainSingle();
        var sold = result.CompletedTransfers[0];
        sold.TransferId.Should().Be(2);
        sold.Status.Should().Be(TransferStatus.Sold);
        sold.SoldPrice.Should().Be(800);
        sold.SoldTo.Should().Be("BuyerName");
    }

    [Fact]
    public async Task Detects_expired_transfer_when_item_disappears_without_processed_match()
    {
        await using var database = await TestDatabase.CreateAsync();
        var now = DateTimeOffset.UtcNow;

        await SeedAsync(database.Factory,
            CreateActiveSnapshot(now, """
                [{"id":1,"startPrice":500,"pigeon":{"id":10,"firstNameId":1},"fancier":{"id":99,"displayName":"Seller"},"bidders":[]}]
            """),
            CreateActiveSnapshot(now.AddMinutes(-5), """
                [{"id":1,"startPrice":500,"pigeon":{"id":10,"firstNameId":1},"fancier":{"id":99,"displayName":"Seller"},"bidders":[]},
                 {"id":3,"startPrice":100,"pigeon":{"id":12},"fancier":{"id":99,"displayName":"Seller"},"bidders":[]}]
            """),
            CreateTranslationSnapshot());

        var reader = new TransferDataReader(database.Factory, new StubTrackerDataReader());
        var result = await reader.GetTransferDataAsync(FancierId);

        result.CompletedTransfers.Should().ContainSingle();
        var expired = result.CompletedTransfers[0];
        expired.TransferId.Should().Be(3);
        expired.Status.Should().Be(TransferStatus.Expired);
    }

    [Fact]
    public async Task Detects_sold_transfer_via_bidder_heuristic_when_processed_data_missing()
    {
        await using var database = await TestDatabase.CreateAsync();
        var now = DateTimeOffset.UtcNow;

        await SeedAsync(database.Factory,
            CreateActiveSnapshot(now, """
                [{"id":1,"startPrice":500,"pigeon":{"id":10,"firstNameId":1},"fancier":{"id":99,"displayName":"Seller"},"bidders":[]}]
            """),
            CreateActiveSnapshot(now.AddMinutes(-5), """
                [{"id":1,"startPrice":500,"pigeon":{"id":10,"firstNameId":1},"fancier":{"id":99,"displayName":"Seller"},"bidders":[]},
                 {"id":4,"startPrice":200,"price":350,"pigeon":{"id":13},"fancier":{"id":99,"displayName":"Seller"},"bidders":[50,51]}]
            """),
            CreateTranslationSnapshot());

        var reader = new TransferDataReader(database.Factory, new StubTrackerDataReader());
        var result = await reader.GetTransferDataAsync(FancierId);

        result.CompletedTransfers.Should().ContainSingle();
        var sold = result.CompletedTransfers[0];
        sold.TransferId.Should().Be(4);
        sold.Status.Should().Be(TransferStatus.Sold);
    }

    [Fact]
    public async Task Skips_duplicate_completed_transfers_on_second_call()
    {
        await using var database = await TestDatabase.CreateAsync();
        var now = DateTimeOffset.UtcNow;

        await SeedAsync(database.Factory,
            CreateActiveSnapshot(now, "[]"),
            CreateActiveSnapshot(now.AddMinutes(-5), """
                [{"id":5,"startPrice":200,"pigeon":{"id":15},"fancier":{"id":99,"displayName":"Seller"},"bidders":[]}]
            """),
            CreateTranslationSnapshot());

        var reader = new TransferDataReader(database.Factory, new StubTrackerDataReader());
        await reader.GetTransferDataAsync(FancierId);
        await reader.GetTransferDataAsync(FancierId);

        await using var db = database.Factory.CreateDbContext();
        var count = await db.CompletedTransfers.CountAsync(x => x.TransferId == 5);
        count.Should().Be(1);
    }

    [Fact]
    public async Task Upgrades_expired_transfer_to_sold_when_processed_data_becomes_available()
    {
        await using var database = await TestDatabase.CreateAsync();
        var now = DateTimeOffset.UtcNow;

        await using (var db = database.Factory.CreateDbContext())
        {
            db.CompletedTransfers.Add(new CompletedTransferEntity
            {
                TransferId = 7,
                PigeonId = 20,
                SelectedFancierId = FancierId,
                Status = "Expired",
                StartPrice = 400,
                SoldPrice = null,
                Seller = "OriginalSeller",
                SoldTo = null,
                PigeonName = "TestPigeon",
                BidCount = 0,
                TransferStart = now.AddDays(-3),
                TransferEnd = now.AddDays(-1),
                DetectedAtUtc = now.AddMinutes(-30),
            });
            await db.SaveChangesAsync();
        }

        await SeedAsync(database.Factory,
            CreateActiveSnapshot(now, "[]"),
            CreateActiveSnapshot(now.AddMinutes(-5), "[]"),
            CreateProcessedSnapshot(now, """
                [{"id":7,"startPrice":400,"price":950,"pigeon":{"id":20,"firstNameId":1},
                  "fancier":{"id":99,"displayName":"Seller"},
                  "buyer":{"id":60,"displayName":"HappyBuyer"},"bidders":[1,2,3]}]
            """),
            CreateTranslationSnapshot());

        var reader = new TransferDataReader(database.Factory, new StubTrackerDataReader());
        await reader.GetTransferDataAsync(FancierId);

        await using var verifyDb = database.Factory.CreateDbContext();
        var entity = await verifyDb.CompletedTransfers.SingleAsync(x => x.TransferId == 7);
        entity.Status.Should().Be("Sold");
        entity.SoldPrice.Should().Be(950);
        entity.SoldTo.Should().Be("HappyBuyer");
        entity.BidCount.Should().Be(3);
    }

    [Fact]
    public async Task Deserializes_transfers_from_object_with_items_property()
    {
        await using var database = await TestDatabase.CreateAsync();
        await SeedAsync(database.Factory, new RawApiSnapshotEntity
        {
            Endpoint = "/api/transfer",
            NormalizedQuery = "processed=false",
            HttpMethod = "GET",
            StatusCode = 200,
            CapturedAtUtc = DateTimeOffset.UtcNow,
            ResponseBodyJson = """
            {"items":[{"id":1,"startPrice":500,"pigeon":{"id":10},"fancier":{"id":99,"displayName":"S"},"bidders":[]}]}
            """,
            BodySha256 = "items-format",
            SelectedFancierId = FancierId,
        }, CreateTranslationSnapshot());

        var reader = new TransferDataReader(database.Factory, new StubTrackerDataReader());
        var result = await reader.GetTransferDataAsync(FancierId);

        result.ActiveTransfers.Should().ContainSingle();
    }

    [Fact]
    public async Task Handles_invalid_json_gracefully()
    {
        await using var database = await TestDatabase.CreateAsync();
        await SeedAsync(database.Factory, new RawApiSnapshotEntity
        {
            Endpoint = "/api/transfer",
            NormalizedQuery = "processed=false",
            HttpMethod = "GET",
            StatusCode = 200,
            CapturedAtUtc = DateTimeOffset.UtcNow,
            ResponseBodyJson = "not valid json {{{",
            BodySha256 = "bad-json",
            SelectedFancierId = FancierId,
        });

        var reader = new TransferDataReader(database.Factory, new StubTrackerDataReader());
        var result = await reader.GetTransferDataAsync(FancierId);

        result.ActiveTransfers.Should().BeEmpty();
    }

    [Fact]
    public async Task RecheckTransferStatus_upgrades_expired_to_sold_when_processed_data_exists()
    {
        await using var database = await TestDatabase.CreateAsync();
        var now = DateTimeOffset.UtcNow;

        await using (var db = database.Factory.CreateDbContext())
        {
            db.CompletedTransfers.Add(new CompletedTransferEntity
            {
                TransferId = 10,
                PigeonId = 30,
                SelectedFancierId = FancierId,
                Status = "Expired",
                StartPrice = 200,
                PigeonName = "Recheck Pigeon",
                BidCount = 0,
                TransferStart = now.AddDays(-5),
                TransferEnd = now.AddDays(-2),
                DetectedAtUtc = now.AddMinutes(-60),
            });
            await db.SaveChangesAsync();
        }

        await SeedAsync(database.Factory,
            CreateProcessedSnapshot(now, """
                [{"id":10,"startPrice":200,"price":600,"pigeon":{"id":30,"firstNameId":1},
                  "fancier":{"id":99,"displayName":"Seller"},
                  "buyer":{"id":70,"displayName":"LuckyBuyer"},"bidders":[1]}]
            """));

        var reader = new TransferDataReader(database.Factory, new StubTrackerDataReader());
        var result = await reader.RecheckTransferStatusAsync(FancierId, 10);

        result.Should().BeTrue();

        await using var verifyDb = database.Factory.CreateDbContext();
        var entity = await verifyDb.CompletedTransfers.SingleAsync(x => x.TransferId == 10);
        entity.Status.Should().Be("Sold");
        entity.SoldPrice.Should().Be(600);
        entity.SoldTo.Should().Be("LuckyBuyer");
    }

    [Fact]
    public async Task RecheckTransferStatus_returns_false_when_no_processed_data_found()
    {
        await using var database = await TestDatabase.CreateAsync();
        var now = DateTimeOffset.UtcNow;

        await using (var db = database.Factory.CreateDbContext())
        {
            db.CompletedTransfers.Add(new CompletedTransferEntity
            {
                TransferId = 11,
                PigeonId = 31,
                SelectedFancierId = FancierId,
                Status = "Expired",
                StartPrice = 100,
                PigeonName = "Still Expired",
                BidCount = 0,
                TransferStart = now.AddDays(-4),
                TransferEnd = now.AddDays(-1),
                DetectedAtUtc = now.AddMinutes(-30),
            });
            await db.SaveChangesAsync();
        }

        var reader = new TransferDataReader(database.Factory, new StubTrackerDataReader());
        var result = await reader.RecheckTransferStatusAsync(FancierId, 11);

        result.Should().BeFalse();

        await using var verifyDb = database.Factory.CreateDbContext();
        var entity = await verifyDb.CompletedTransfers.SingleAsync(x => x.TransferId == 11);
        entity.Status.Should().Be("Expired");
    }

    [Fact]
    public async Task RecheckTransferStatus_upgrades_to_sold_via_bidder_heuristic_when_no_processed_data()
    {
        await using var database = await TestDatabase.CreateAsync();
        var now = DateTimeOffset.UtcNow;

        await using (var db = database.Factory.CreateDbContext())
        {
            db.CompletedTransfers.Add(new CompletedTransferEntity
            {
                TransferId = 12,
                PigeonId = 32,
                SelectedFancierId = FancierId,
                Status = "Expired",
                StartPrice = 300,
                PigeonName = "Had Bidders",
                BidCount = 3,
                TransferStart = now.AddDays(-5),
                TransferEnd = now.AddDays(-2),
                DetectedAtUtc = now.AddMinutes(-45),
            });
            await db.SaveChangesAsync();
        }

        var reader = new TransferDataReader(database.Factory, new StubTrackerDataReader());
        var result = await reader.RecheckTransferStatusAsync(FancierId, 12);

        result.Should().BeTrue();

        await using var verifyDb = database.Factory.CreateDbContext();
        var entity = await verifyDb.CompletedTransfers.SingleAsync(x => x.TransferId == 12);
        entity.Status.Should().Be("Sold");
        entity.SoldPrice.Should().Be(300);
    }

    [Fact]
    public async Task UpdateTransferStatus_changes_status_and_clears_sold_fields_when_set_to_expired()
    {
        await using var database = await TestDatabase.CreateAsync();
        var now = DateTimeOffset.UtcNow;

        await using (var db = database.Factory.CreateDbContext())
        {
            db.CompletedTransfers.Add(new CompletedTransferEntity
            {
                TransferId = 13,
                PigeonId = 33,
                SelectedFancierId = FancierId,
                Status = "Sold",
                StartPrice = 200,
                SoldPrice = 500,
                SoldTo = "SomeBuyer",
                PigeonName = "Override Pigeon",
                BidCount = 2,
                TransferStart = now.AddDays(-3),
                TransferEnd = now.AddDays(-1),
                DetectedAtUtc = now.AddMinutes(-20),
            });
            await db.SaveChangesAsync();
        }

        var reader = new TransferDataReader(database.Factory, new StubTrackerDataReader());
        await reader.UpdateTransferStatusAsync(FancierId, 13, TransferStatus.Expired);

        await using var verifyDb = database.Factory.CreateDbContext();
        var entity = await verifyDb.CompletedTransfers.SingleAsync(x => x.TransferId == 13);
        entity.Status.Should().Be("Expired");
        entity.SoldPrice.Should().BeNull();
        entity.SoldTo.Should().BeNull();
    }

    [Fact]
    public async Task UpdateTransferStatus_to_sold_defaults_sold_price_to_start_price()
    {
        await using var database = await TestDatabase.CreateAsync();
        var now = DateTimeOffset.UtcNow;

        await using (var db = database.Factory.CreateDbContext())
        {
            db.CompletedTransfers.Add(new CompletedTransferEntity
            {
                TransferId = 14,
                PigeonId = 34,
                SelectedFancierId = FancierId,
                Status = "Expired",
                StartPrice = 300,
                SoldPrice = null,
                SoldTo = null,
                PigeonName = "Default Price Pigeon",
                BidCount = 0,
                TransferStart = now.AddDays(-3),
                TransferEnd = now.AddDays(-1),
                DetectedAtUtc = now.AddMinutes(-20),
            });
            await db.SaveChangesAsync();
        }

        var reader = new TransferDataReader(database.Factory, new StubTrackerDataReader());
        await reader.UpdateTransferStatusAsync(FancierId, 14, TransferStatus.Sold);

        await using var verifyDb = database.Factory.CreateDbContext();
        var entity = await verifyDb.CompletedTransfers.SingleAsync(x => x.TransferId == 14);
        entity.Status.Should().Be("Sold");
        entity.SoldPrice.Should().Be(300);
    }

    [Fact]
    public async Task UpdateTransferSoldPrice_persists_new_price()
    {
        await using var database = await TestDatabase.CreateAsync();
        var now = DateTimeOffset.UtcNow;

        await using (var db = database.Factory.CreateDbContext())
        {
            db.CompletedTransfers.Add(new CompletedTransferEntity
            {
                TransferId = 15,
                PigeonId = 35,
                SelectedFancierId = FancierId,
                Status = "Sold",
                StartPrice = 200,
                SoldPrice = 200,
                PigeonName = "Price Edit Pigeon",
                BidCount = 1,
                TransferStart = now.AddDays(-3),
                TransferEnd = now.AddDays(-1),
                DetectedAtUtc = now.AddMinutes(-20),
            });
            await db.SaveChangesAsync();
        }

        var reader = new TransferDataReader(database.Factory, new StubTrackerDataReader());
        await reader.UpdateTransferSoldPriceAsync(FancierId, 15, 850);

        await using var verifyDb = database.Factory.CreateDbContext();
        var entity = await verifyDb.CompletedTransfers.SingleAsync(x => x.TransferId == 15);
        entity.SoldPrice.Should().Be(850);
    }

    private static RawApiSnapshotEntity CreateActiveSnapshot(DateTimeOffset capturedAt, string json) =>
        new()
        {
            Endpoint = "/api/transfer",
            NormalizedQuery = "processed=false",
            HttpMethod = "GET",
            StatusCode = 200,
            CapturedAtUtc = capturedAt,
            ResponseBodyJson = json,
            BodySha256 = Guid.NewGuid().ToString(),
            SelectedFancierId = FancierId,
        };

    private static RawApiSnapshotEntity CreateProcessedSnapshot(DateTimeOffset capturedAt, string json) =>
        new()
        {
            Endpoint = "/api/transfer",
            NormalizedQuery = "processed=true",
            HttpMethod = "GET",
            StatusCode = 200,
            CapturedAtUtc = capturedAt,
            ResponseBodyJson = json,
            BodySha256 = Guid.NewGuid().ToString(),
            SelectedFancierId = FancierId,
        };

    private static RawApiSnapshotEntity CreateTranslationSnapshot() =>
        new()
        {
            Endpoint = "/api/translation/nl",
            NormalizedQuery = "",
            HttpMethod = "GET",
            StatusCode = 200,
            CapturedAtUtc = DateTimeOffset.UtcNow,
            ResponseBodyJson = """{"first-name":{"1":"Tom","2":"Jan"},"last-name":{"10":"Ape-head"}}""",
            BodySha256 = "translation",
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

    private sealed class StubTrackerDataReader : ITrackerDataReader
    {
        public Task<TrackerDashboardData> GetDashboardAsync(
            int selectedFancierId,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(new TrackerDashboardData(
                null, null, null, null, null, null, null, null, null, null, null, null, null, null, null, []));
    }
}
