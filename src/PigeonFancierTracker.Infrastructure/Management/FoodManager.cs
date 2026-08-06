using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using PigeonFancierTracker.Core.Contracts;
using PigeonFancierTracker.Infrastructure.Persistence;
using PigeonFancierTracker.Infrastructure.PigeonFancierApi;

namespace PigeonFancierTracker.Infrastructure.Management;

public sealed class FoodManager(
    IDbContextFactory<AppDbContext> contextFactory,
    PigeonFancierApiClient apiClient,
    IAuthenticatedWriteTransport writeTransport,
    RawSnapshotStore snapshotStore,
    ILogger<FoodManager> logger) : IFoodManager
{
    private const decimal GramsPerPigeonPerDay = 30m;
    private const int TargetBarley = 25;
    private const int TargetGrain = 25;
    private const int TargetCorn = 30;
    private const int TargetPeanut = 20;

    private static readonly Dictionary<string, int> FoodItemIds = new(StringComparer.OrdinalIgnoreCase)
    {
        ["barley"] = 101,
        ["grain"] = 102,
        ["corn"] = 103,
        ["peanut"] = 104,
    };

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        NumberHandling = JsonNumberHandling.AllowReadingFromString,
    };

    public async Task<FoodManagementPlan> BuildFoodPlanAsync(
        int fancierId,
        int minDaysReserve,
        CancellationToken cancellationToken = default)
    {
        var skipped = new List<string>();

        var fancier = await ReadFancierSnapshotAsync(fancierId, cancellationToken);
        if (fancier is null)
        {
            skipped.Add("No fancier snapshot available");
            return new FoodManagementPlan([], null, 0, skipped);
        }

        var pigeonCount = fancier.PigeonCount ?? 0;
        if (pigeonCount == 0)
        {
            skipped.Add("No pigeons — no food needed");
            return new FoodManagementPlan([], null, 0, skipped);
        }

        var items = await ReadInventoryItemsAsync(fancierId, cancellationToken);
        var foodItems = items
            .Where(i => string.Equals(i.Type, "food", StringComparison.OrdinalIgnoreCase))
            .ToList();

        if (foodItems.Count == 0)
        {
            skipped.Add("No food items found in inventory snapshot");
            return new FoodManagementPlan([], null, 0, skipped);
        }

        var totalStock = foodItems.Sum(i => i.Stock);
        var dailyConsumption = pigeonCount * GramsPerPigeonPerDay;
        var daysRemaining = dailyConsumption > 0
            ? (double)(totalStock / dailyConsumption)
            : double.MaxValue;

        logger.LogInformation(
            "Food status: {Stock:F0}g in stock, {PigeonCount} pigeons, {Days:F1} days remaining (min {MinDays})",
            totalStock, pigeonCount, daysRemaining, minDaysReserve);

        var purchases = new List<FoodPurchaseAction>();

        if (daysRemaining < minDaysReserve)
        {
            var targetDays = minDaysReserve * 2;
            var targetStock = dailyConsumption * targetDays;
            var deficit = targetStock - totalStock;

            if (deficit > 0)
            {
                var balance = fancier.Finances?.Capital ?? 0;
                var totalCost = 0m;

                foreach (var item in foodItems)
                {
                    if (item.UnitPrice <= 0) continue;

                    var targetPercent = GetTargetPercentForItem(item.Name);
                    var itemTargetStock = targetStock * targetPercent / 100m;
                    var itemDeficit = itemTargetStock - item.Stock;

                    if (itemDeficit <= 0) continue;

                    var unitsNeeded = (int)Math.Ceiling(itemDeficit);
                    var cost = unitsNeeded * item.UnitPrice;

                    if (totalCost + cost > balance - 500m)
                    {
                        skipped.Add($"Skipping {item.Name} purchase: would push balance below EUR 500 safety margin");
                        continue;
                    }

                    purchases.Add(new FoodPurchaseAction(
                        item.Id,
                        item.Name ?? "unknown",
                        unitsNeeded,
                        item.UnitPrice,
                        cost));

                    totalCost += cost;
                }

                if (purchases.Count == 0 && totalCost == 0)
                    skipped.Add("Insufficient balance for any food purchase");
            }
        }
        else
        {
            skipped.Add($"Food stock sufficient: {daysRemaining:F1} days remaining (min {minDaysReserve})");
        }

        FoodDistributionAction? distributionChange = null;
        var currentFood = fancier.Food;
        if (currentFood is not null)
        {
            var currentBarley = currentFood.Barley ?? 0;
            var currentGrain = currentFood.Grain ?? 0;
            var currentCorn = currentFood.Corn ?? 0;
            var currentPeanut = currentFood.Peanut ?? 0;

            if (currentBarley != TargetBarley || currentGrain != TargetGrain ||
                currentCorn != TargetCorn || currentPeanut != TargetPeanut)
            {
                distributionChange = new FoodDistributionAction(
                    TargetBarley, TargetGrain, TargetCorn, TargetPeanut);

                logger.LogInformation(
                    "Food distribution change planned: B:{CurrentB}→{TargetB} G:{CurrentG}→{TargetG} C:{CurrentC}→{TargetC} P:{CurrentP}→{TargetP}",
                    currentBarley, TargetBarley, currentGrain, TargetGrain,
                    currentCorn, TargetCorn, currentPeanut, TargetPeanut);
            }
            else
            {
                skipped.Add("Food distribution already at target mix");
            }
        }

        return new FoodManagementPlan(purchases, distributionChange, daysRemaining, skipped);
    }

    public async Task ExecuteFoodPlanAsync(
        FoodManagementPlan plan,
        CancellationToken cancellationToken = default)
    {
        if (plan.Purchases.Count > 0)
        {
            await ExecutePurchasesAsync(plan.Purchases, cancellationToken);
        }

        if (plan.DistributionChange is { } dist)
        {
            await ExecuteDistributionChangeAsync(dist, cancellationToken);
        }
    }

    private async Task ExecutePurchasesAsync(
        IReadOnlyList<FoodPurchaseAction> purchases,
        CancellationToken cancellationToken)
    {
        var requestItems = purchases.Select(p =>
        {
            var currentItem = cachedFoodItems?.FirstOrDefault(i => i.Id == p.ItemId);
            return new FoodPurchaseRequestItem(
                Amount: p.Amount,
                Id: p.ItemId,
                Name: p.Name,
                Type: "food",
                UnitPrice: p.UnitPrice,
                Stock: currentItem?.Stock ?? 0);
        }).ToList();

        var request = new FoodPurchaseRequest(requestItems);
        var json = JsonSerializer.Serialize(request, JsonOptions);

        logger.LogInformation("Buying food: {Items}",
            string.Join(", ", purchases.Select(p => $"{p.Amount}x {p.Name} (€{p.TotalCost:F2})")));

        var response = await writeTransport.PostJsonAsync("/api/fancier/items", json, cancellationToken);

        if (response.StatusCode >= 200 && response.StatusCode < 300)
        {
            logger.LogInformation("Food purchase completed: €{Total:F2} total",
                purchases.Sum(p => p.TotalCost));
        }
        else
        {
            logger.LogWarning("Food purchase failed — HTTP {Status}: {Body}",
                response.StatusCode, response.Body);
        }
    }

    private async Task ExecuteDistributionChangeAsync(
        FoodDistributionAction dist,
        CancellationToken cancellationToken)
    {
        var request = new FoodDistributionRequest(dist.Barley, dist.Grain, dist.Corn, dist.Peanut);
        var json = JsonSerializer.Serialize(request, JsonOptions);

        logger.LogInformation("Setting food distribution: B:{Barley}% G:{Grain}% C:{Corn}% P:{Peanut}%",
            dist.Barley, dist.Grain, dist.Corn, dist.Peanut);

        var response = await writeTransport.PutJsonAsync("/api/fancier/distribution", json, cancellationToken);

        if (response.StatusCode >= 200 && response.StatusCode < 300)
        {
            logger.LogInformation("Food distribution updated successfully");
        }
        else
        {
            logger.LogWarning("Food distribution update failed — HTTP {Status}: {Body}",
                response.StatusCode, response.Body);
        }
    }

    private IReadOnlyList<FancierInventoryItemDto>? cachedFoodItems;

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
                var items = JsonSerializer.Deserialize<List<FancierInventoryItemDto>>(response.Body, JsonOptions) ?? [];
                cachedFoodItems = items.Where(i => string.Equals(i.Type, "food", StringComparison.OrdinalIgnoreCase)).ToList();
                return items;
            }
            catch (JsonException)
            {
                return [];
            }
        }

        try
        {
            var items = JsonSerializer.Deserialize<List<FancierInventoryItemDto>>(snapshot.ResponseBodyJson, JsonOptions) ?? [];
            cachedFoodItems = items.Where(i => string.Equals(i.Type, "food", StringComparison.OrdinalIgnoreCase)).ToList();
            return items;
        }
        catch (JsonException)
        {
            return [];
        }
    }

    private static decimal GetTargetPercentForItem(string? name) => name?.ToLowerInvariant() switch
    {
        "barley" => TargetBarley,
        "grain" => TargetGrain,
        "corn" => TargetCorn,
        "peanut" => TargetPeanut,
        _ => 25,
    };
}
