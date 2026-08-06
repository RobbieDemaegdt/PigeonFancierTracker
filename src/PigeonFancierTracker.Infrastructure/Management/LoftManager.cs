using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using PigeonFancierTracker.Core.Contracts;
using PigeonFancierTracker.Infrastructure.Persistence;
using PigeonFancierTracker.Infrastructure.PigeonFancierApi;

namespace PigeonFancierTracker.Infrastructure.Management;

public sealed class LoftManager(
    IDbContextFactory<AppDbContext> contextFactory,
    PigeonFancierApiClient apiClient,
    IAuthenticatedWriteTransport writeTransport,
    RawSnapshotStore snapshotStore,
    ILogger<LoftManager> logger) : ILoftManager
{
    private const int SinglePenItemId = 401;
    private const string SinglePenName = "single-pen";
    private const decimal SinglePenUnitPrice = 40m;

    private static readonly Dictionary<string, (LoftTier Tier, int Capacity)> TierMap =
        new(StringComparer.OrdinalIgnoreCase)
        {
            ["dilapidated"] = (LoftTier.RunDown, 10),
            ["run-down"] = (LoftTier.RunDown, 10),
            ["loft"] = (LoftTier.Loft, 25),
            ["villa"] = (LoftTier.PigeonVilla, 50),
            ["pigeon villa"] = (LoftTier.PigeonVilla, 50),
            ["complex"] = (LoftTier.PigeonComplex, 100),
            ["pigeon complex"] = (LoftTier.PigeonComplex, 100),
            ["paradise"] = (LoftTier.PigeonParadise, 250),
            ["pigeon paradise"] = (LoftTier.PigeonParadise, 250),
        };

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        NumberHandling = JsonNumberHandling.AllowReadingFromString,
    };

    public static int? GetCapacityForTier(string? tier)
    {
        if (tier is null) return null;
        return TierMap.TryGetValue(tier, out var info) ? info.Capacity : null;
    }

    public async Task<LoftManagementPlan> BuildLoftPlanAsync(
        int fancierId,
        CancellationToken cancellationToken = default)
    {
        var skipped = new List<string>();

        var fancier = await ReadFancierSnapshotAsync(fancierId, cancellationToken);
        if (fancier is null)
        {
            skipped.Add("No fancier snapshot available");
            return new LoftManagementPlan(null, null, 0, 0, 0, null, skipped);
        }

        var pen = fancier.Pen;
        var dirt = pen?.Dirt;
        var pigeonCount = fancier.PigeonCount ?? 0;

        int capacity;
        if (pen?.Tier is { } tierName && TierMap.TryGetValue(tierName, out var tierInfo))
        {
            capacity = tierInfo.Capacity;
        }
        else
        {
            if (pen?.Tier is not null)
                logger.LogWarning("Unknown loft tier '{Tier}', cannot determine capacity", pen.Tier);
            skipped.Add($"Unknown loft tier: {pen?.Tier ?? "(null)"}");
            return new LoftManagementPlan(null, null, 0, pigeonCount, 0, dirt, skipped);
        }

        var occupancyPercent = capacity > 0
            ? (double)pigeonCount / capacity * 100
            : 0;

        logger.LogInformation(
            "Loft status: tier={Tier}, capacity={Capacity}, pigeons={Pigeons} ({Occupancy:F0}%), dirt={Dirt}",
            pen.Tier, capacity, pigeonCount, occupancyPercent, dirt);

        LoftCleanAction? cleanAction = null;
        if (dirt is > 0)
        {
            cleanAction = new LoftCleanAction(dirt.Value, $"Loft dirty (dirt={dirt})");
        }
        else
        {
            skipped.Add("Loft is clean (dirt=0)");
        }

        LoftPenPurchaseAction? penPurchaseAction = null;
        var items = await ReadInventoryItemsAsync(fancierId, cancellationToken);
        var singlePen = items.FirstOrDefault(i => i.Id == SinglePenItemId);
        var penStock = (int)(singlePen?.Stock ?? 0);

        if (pigeonCount > penStock)
        {
            var deficit = pigeonCount - penStock + 2;
            var cost = deficit * SinglePenUnitPrice;

            var balance = fancier.Finances?.Capital ?? 0;
            if (balance - cost >= 500m)
            {
                penPurchaseAction = new LoftPenPurchaseAction(
                    SinglePenItemId,
                    SinglePenName,
                    deficit,
                    SinglePenUnitPrice,
                    cost);
            }
            else
            {
                skipped.Add($"Need {deficit} single pens (€{cost:F0}) but balance too low (€{balance:F0})");
            }
        }
        else
        {
            skipped.Add($"Pen stock sufficient: {penStock} pens for {pigeonCount} pigeons");
        }

        return new LoftManagementPlan(
            cleanAction,
            penPurchaseAction,
            capacity,
            pigeonCount,
            occupancyPercent,
            dirt,
            skipped);
    }

    public async Task ExecuteLoftPlanAsync(
        LoftManagementPlan plan,
        CancellationToken cancellationToken = default)
    {
        if (plan.CleanAction is not null)
            await ExecuteCleanAsync(cancellationToken);

        if (plan.PenPurchaseAction is { } penAction)
            await ExecutePenPurchaseAsync(penAction, cancellationToken);
    }

    private async Task ExecuteCleanAsync(CancellationToken ct)
    {
        logger.LogInformation("Cleaning loft");
        var response = await writeTransport.PatchAsync("/api/barn", ct);

        if (response.StatusCode >= 200 && response.StatusCode < 300)
            logger.LogInformation("Loft cleaned successfully");
        else
            logger.LogWarning("Loft cleaning failed — HTTP {Status}: {Body}",
                response.StatusCode, response.Body);
    }

    private async Task ExecutePenPurchaseAsync(LoftPenPurchaseAction penAction, CancellationToken ct)
    {
        var requestItems = new List<FoodPurchaseRequestItem>
        {
            new(
                Amount: penAction.Amount,
                Id: penAction.ItemId,
                Name: penAction.Name,
                Type: "other",
                UnitPrice: penAction.UnitPrice,
                Stock: 0),
        };

        var request = new FoodPurchaseRequest(requestItems);
        var json = JsonSerializer.Serialize(request, JsonOptions);

        logger.LogInformation("Buying pens: {Amount}x {Name} @ €{Price:F2} = €{Total:F2}",
            penAction.Amount, penAction.Name, penAction.UnitPrice, penAction.TotalCost);

        var response = await writeTransport.PostJsonAsync("/api/fancier/items", json, ct);

        if (response.StatusCode >= 200 && response.StatusCode < 300)
            logger.LogInformation("Pen purchase completed: €{Total:F2}", penAction.TotalCost);
        else
            logger.LogWarning("Pen purchase failed — HTTP {Status}: {Body}",
                response.StatusCode, response.Body);
    }

    private async Task<SelectedFancierDto?> ReadFancierSnapshotAsync(int fancierId, CancellationToken ct)
    {
        await using var db = await contextFactory.CreateDbContextAsync(ct);
        var snapshot = await db.RawApiSnapshots
            .AsNoTracking()
            .Where(x => x.SelectedFancierId == fancierId
                && x.Endpoint == "/api/fancier/selected"
                && x.StatusCode >= 200 && x.StatusCode < 300)
            .OrderByDescending(x => x.Id)
            .FirstOrDefaultAsync(ct);

        if (snapshot is null) return null;

        try
        {
            return JsonSerializer.Deserialize<SelectedFancierDto>(snapshot.ResponseBodyJson, JsonOptions);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private async Task<IReadOnlyList<FancierInventoryItemDto>> ReadInventoryItemsAsync(int fancierId, CancellationToken ct)
    {
        await using var db = await contextFactory.CreateDbContextAsync(ct);
        var snapshot = await db.RawApiSnapshots
            .AsNoTracking()
            .Where(x => x.SelectedFancierId == fancierId
                && x.Endpoint == "/api/fancier/items"
                && x.StatusCode >= 200 && x.StatusCode < 300)
            .OrderByDescending(x => x.Id)
            .FirstOrDefaultAsync(ct);

        if (snapshot is null)
        {
            var response = await apiClient.GetJsonAsync("/api/fancier/items", cancellationToken: ct);
            await snapshotStore.SaveAsync("/api/fancier/items", null, response, fancierId, cancellationToken: ct);

            if (!response.IsSuccessStatusCode || string.IsNullOrEmpty(response.Body))
                return [];

            try
            {
                return JsonSerializer.Deserialize<List<FancierInventoryItemDto>>(response.Body, JsonOptions) ?? [];
            }
            catch (JsonException)
            {
                return [];
            }
        }

        try
        {
            return JsonSerializer.Deserialize<List<FancierInventoryItemDto>>(snapshot.ResponseBodyJson, JsonOptions) ?? [];
        }
        catch (JsonException)
        {
            return [];
        }
    }
}
