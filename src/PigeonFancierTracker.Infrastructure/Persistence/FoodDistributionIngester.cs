using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.EntityFrameworkCore;
using PigeonFancierTracker.Core.Contracts;

namespace PigeonFancierTracker.Infrastructure.Persistence;

public sealed class FoodDistributionIngester(IDbContextFactory<AppDbContext> contextFactory)
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        NumberHandling = JsonNumberHandling.AllowReadingFromString,
    };

    public async Task IngestAsync(int selectedFancierId, CancellationToken cancellationToken = default)
    {
        await using var db = await contextFactory.CreateDbContextAsync(cancellationToken);

        var snapshot = await db.RawApiSnapshots
            .AsNoTracking()
            .Where(x => x.SelectedFancierId == selectedFancierId
                && x.Endpoint == "/api/fancier/selected"
                && x.StatusCode >= 200 && x.StatusCode < 300)
            .OrderByDescending(x => x.Id)
            .FirstOrDefaultAsync(cancellationToken);

        if (snapshot is null)
            return;

        var food = ExtractFoodDistribution(snapshot.ResponseBodyJson);
        if (food is null)
            return;

        await InsertIfChangedAsync(db, selectedFancierId, food.Value.Barley, food.Value.Grain,
            food.Value.Corn, food.Value.Peanut, snapshot.CapturedAtUtc, cancellationToken);
    }

    public async Task BackfillAsync(int selectedFancierId, CancellationToken cancellationToken = default)
    {
        await using var db = await contextFactory.CreateDbContextAsync(cancellationToken);

        var hasExisting = await db.FoodDistributionSnapshots
            .AnyAsync(x => x.SelectedFancierId == selectedFancierId, cancellationToken);
        if (hasExisting)
            return;

        var snapshots = await db.RawApiSnapshots
            .AsNoTracking()
            .Where(x => x.SelectedFancierId == selectedFancierId
                && x.Endpoint == "/api/fancier/selected"
                && x.StatusCode >= 200 && x.StatusCode < 300)
            .OrderBy(x => x.Id)
            .ToListAsync(cancellationToken);

        foreach (var snapshot in snapshots)
        {
            var food = ExtractFoodDistribution(snapshot.ResponseBodyJson);
            if (food is null)
                continue;

            await InsertIfChangedAsync(db, selectedFancierId, food.Value.Barley, food.Value.Grain,
                food.Value.Corn, food.Value.Peanut, snapshot.CapturedAtUtc, cancellationToken);
        }
    }

    private static async Task InsertIfChangedAsync(
        AppDbContext db,
        int fancierId,
        int barley, int grain, int corn, int peanut,
        DateTimeOffset capturedAt,
        CancellationToken cancellationToken)
    {
        var latest = await db.FoodDistributionSnapshots
            .Where(x => x.SelectedFancierId == fancierId)
            .OrderByDescending(x => x.Id)
            .FirstOrDefaultAsync(cancellationToken);

        if (latest is not null
            && latest.Barley == barley && latest.Grain == grain
            && latest.Corn == corn && latest.Peanut == peanut)
            return;

        db.FoodDistributionSnapshots.Add(new FoodDistributionSnapshotEntity
        {
            SelectedFancierId = fancierId,
            Barley = barley,
            Grain = grain,
            Corn = corn,
            Peanut = peanut,
            CapturedAtUtc = capturedAt,
        });

        await db.SaveChangesAsync(cancellationToken);
    }

    private static (int Barley, int Grain, int Corn, int Peanut)? ExtractFoodDistribution(string json)
    {
        try
        {
            var fancier = JsonSerializer.Deserialize<SelectedFancierDto>(json, JsonOptions);
            if (fancier?.Food is not { } food)
                return null;

            return (food.Barley ?? 0, food.Grain ?? 0, food.Corn ?? 0, food.Peanut ?? 0);
        }
        catch
        {
            return null;
        }
    }
}
