using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using PigeonFancierTracker.Core.Analytics;
using PigeonFancierTracker.Core.Contracts;
using PigeonFancierTracker.Infrastructure.PigeonFancierApi;

namespace PigeonFancierTracker.Infrastructure.Persistence;

public sealed class TransferDataReader(
    IDbContextFactory<AppDbContext> contextFactory,
    ITrackerDataReader trackerDataReader,
    PigeonFancierApiClient? apiClient = null) : ITransferDataReader
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
    };

    public async Task<TransferPageData> GetTransferDataAsync(
        int selectedFancierId,
        CancellationToken cancellationToken = default)
    {
        await using var db = await contextFactory.CreateDbContextAsync(cancellationToken);

        var transferSnapshots = (await db.RawApiSnapshots
            .AsNoTracking()
            .Where(x => x.SelectedFancierId == selectedFancierId
                && x.Endpoint == "/api/transfer"
                && x.StatusCode >= 200
                && x.StatusCode < 300)
            .ToListAsync(cancellationToken))
            .OrderByDescending(x => x.CapturedAtUtc)
            .ToList();

        var translationSnapshots = await db.RawApiSnapshots
            .AsNoTracking()
            .Where(x => x.SelectedFancierId == selectedFancierId
                && x.Endpoint.StartsWith("/api/translation/")
                && x.StatusCode >= 200
                && x.StatusCode < 300)
            .ToListAsync(cancellationToken);

        var nameTranslations = PigeonNameResolver.ReadTranslations(translationSnapshots);

        var latestActive = transferSnapshots
            .FirstOrDefault(x => x.NormalizedQuery.Contains("processed=false", StringComparison.OrdinalIgnoreCase)
                && !x.NormalizedQuery.Contains("fancierId=", StringComparison.OrdinalIgnoreCase));

        var activeItems = latestActive is not null
            ? DeserializeTransfers(latestActive.ResponseBodyJson)
            : [];

        var activeTransfers = activeItems
            .Where(x => x.Id.HasValue && x.Pigeon is not null)
            .Select(item => CreateTransferListItem(item, nameTranslations, TransferStatus.Active))
            .OrderBy(x => x.End)
            .ToList();

        // Detect newly completed transfers and persist them
        await PersistNewlyCompletedTransfers(db, transferSnapshots, nameTranslations, selectedFancierId, cancellationToken);

        // Load all historical completed transfers from DB
        var completedTransfers = await LoadPersistedCompletedTransfers(db, selectedFancierId, cancellationToken);

        // Build historical sales for price estimation
        var historicalSales = BuildHistoricalSales(completedTransfers);

        // Attach price estimates to active transfers
        var activeWithEstimates = activeTransfers
            .Select(t => AttachPriceEstimate(t, historicalSales))
            .ToList();

        // Attach price estimates to completed transfers
        var completedWithEstimates = completedTransfers
            .Select(t => AttachPriceEstimate(t, historicalSales))
            .ToList();

        // Build comparison populations filtered by age and attach to active transfers
        var dashboard = await trackerDataReader.GetDashboardAsync(selectedFancierId, cancellationToken);

        var activeWithComparison = activeWithEstimates
            .Select(t => AttachStatsComparison(t, completedWithEstimates, dashboard.Pigeons))
            .ToList();

        var completedWithComparison = completedWithEstimates
            .Select(t => AttachStatsComparison(t, completedWithEstimates, dashboard.Pigeons))
            .ToList();

        var soldTransfers = completedWithComparison
            .Where(t => t.Status == TransferStatus.Sold && t.SoldPrice.HasValue && t.TotalSkill.HasValue)
            .ToList();

        var marketSalePoints = soldTransfers
            .Where(t => t.End.HasValue)
            .Select(t => new MarketSalePoint(t.SoldPrice!.Value, t.TotalSkill!.Value, t.End!.Value))
            .ToList();
        var marketTrend = MarketTrendCalculator.Calculate(marketSalePoints);

        var auctionInputs = soldTransfers
            .Where(t => t.End.HasValue && t.StartPrice.HasValue)
            .Select(t => new AuctionTimingInput(t.End!.Value, t.StartPrice!.Value, t.SoldPrice!.Value, t.BidCount))
            .ToList();
        var auctionTiming = AuctionTimingCalculator.Calculate(auctionInputs);

        return new TransferPageData(activeWithComparison, completedWithComparison, marketTrend, auctionTiming);
    }

    private async Task PersistNewlyCompletedTransfers(
        AppDbContext db,
        List<RawApiSnapshotEntity> transferSnapshots,
        PigeonNameTranslations nameTranslations,
        int selectedFancierId,
        CancellationToken cancellationToken)
    {
        var newlyCompleted = await DetectCompletedTransfers(transferSnapshots, nameTranslations);

        if (newlyCompleted.Count > 0)
        {
            var existingIds = await db.CompletedTransfers
                .Where(x => x.SelectedFancierId == selectedFancierId)
                .Select(x => x.TransferId)
                .ToListAsync(cancellationToken);

            var existingIdSet = new HashSet<int>(existingIds);

            foreach (var item in newlyCompleted)
            {
                if (existingIdSet.Contains(item.TransferId))
                    continue;

                db.CompletedTransfers.Add(new CompletedTransferEntity
                {
                    TransferId = item.TransferId,
                    PigeonId = item.PigeonId,
                    SelectedFancierId = selectedFancierId,
                    Status = item.Status.ToString(),
                    StartPrice = item.StartPrice,
                    SoldPrice = item.SoldPrice,
                    Seller = item.Seller,
                    SoldTo = item.SoldTo,
                    PigeonName = item.PigeonName,
                    Sex = item.Sex,
                    Age = item.Age,
                    Breed = item.Breed,
                    BidCount = item.BidCount,
                    TransferStart = item.Start,
                    TransferEnd = item.End,
                    DetectedAtUtc = DateTimeOffset.UtcNow,
                    SkillsJson = SerializeSkills(item),
                });
            }

            await db.SaveChangesAsync(cancellationToken);
        }

        await UpgradeExpiredToSoldAsync(db, transferSnapshots, selectedFancierId, cancellationToken);
    }

    private async Task UpgradeExpiredToSoldAsync(
        AppDbContext db,
        List<RawApiSnapshotEntity> transferSnapshots,
        int selectedFancierId,
        CancellationToken cancellationToken)
    {
        var entitiesToCheck = await db.CompletedTransfers
            .Where(x => x.SelectedFancierId == selectedFancierId)
            .ToListAsync(cancellationToken);

        if (entitiesToCheck.Count == 0)
            return;

        var liveItems = await FetchProcessedTransfersFromApiAsync(cancellationToken);

        if (liveItems is null && HasApiAccess)
            return;

        var processedSource = liveItems
            ?? transferSnapshots
                .Where(x => x.NormalizedQuery.Contains("processed=true", StringComparison.OrdinalIgnoreCase))
                .OrderByDescending(x => x.CapturedAtUtc)
                .SelectMany(s => DeserializeTransfers(s.ResponseBodyJson));

        var processedItems = processedSource
            .Where(x => x.Id.HasValue)
            .GroupBy(x => x.Id!.Value)
            .ToDictionary(g => g.Key, g => g.First());

        foreach (var entity in entitiesToCheck)
        {
            if (processedItems.TryGetValue(entity.TransferId, out var processedItem)
                && processedItem.Buyer is not null
                && processedItem.Price is not null)
            {
                var newSoldTo = processedItem.Buyer.DisplayName;
                var needsUpdate = entity.Status != TransferStatus.Sold.ToString()
                    || entity.SoldTo != newSoldTo
                    || entity.SoldPrice != processedItem.Price;

                if (needsUpdate)
                {
                    entity.Status = TransferStatus.Sold.ToString();
                    entity.SoldPrice = processedItem.Price;
                    entity.SoldTo = newSoldTo;
                    entity.Seller = processedItem.Fancier?.DisplayName ?? entity.Seller;
                    entity.BidCount = processedItem.Bidders?.Count ?? entity.BidCount;
                }
            }
        }

        // For entities still not resolved, try the pigeonId-specific endpoint
        var unresolved = entitiesToCheck
            .Where(e => e.Status != TransferStatus.Sold.ToString() && e.PigeonId.HasValue)
            .ToList();

        foreach (var entity in unresolved)
        {
            var pigeonTransfers = await FetchProcessedTransfersForPigeonAsync(entity.PigeonId!.Value, cancellationToken);
            if (pigeonTransfers is null)
                continue;

            var match = pigeonTransfers.FirstOrDefault(x => x.Id == entity.TransferId);
            if (match is { Buyer: not null, Price: not null })
            {
                entity.Status = TransferStatus.Sold.ToString();
                entity.SoldPrice = match.Price;
                entity.SoldTo = match.Buyer.DisplayName;
                entity.Seller = match.Fancier?.DisplayName ?? entity.Seller;
                entity.BidCount = match.Bidders?.Count ?? entity.BidCount;
            }
        }

        await db.SaveChangesAsync(cancellationToken);
    }

    public async Task<bool> RecheckTransferStatusAsync(
        int selectedFancierId,
        int transferId,
        CancellationToken cancellationToken = default)
    {
        await using var db = await contextFactory.CreateDbContextAsync(cancellationToken);

        var entity = await db.CompletedTransfers
            .FirstOrDefaultAsync(
                x => x.SelectedFancierId == selectedFancierId && x.TransferId == transferId,
                cancellationToken);

        if (entity is null || entity.Status == TransferStatus.Sold.ToString())
            return false;

        var processedSnapshots = (await db.RawApiSnapshots
            .AsNoTracking()
            .Where(x => x.SelectedFancierId == selectedFancierId
                && x.Endpoint == "/api/transfer"
                && x.StatusCode >= 200
                && x.StatusCode < 300
                && x.NormalizedQuery.Contains("processed=true"))
            .ToListAsync(cancellationToken))
            .OrderByDescending(x => x.CapturedAtUtc)
            .ToList();

        foreach (var snapshot in processedSnapshots)
        {
            var items = DeserializeTransfers(snapshot.ResponseBodyJson);
            var match = items.FirstOrDefault(x => x.Id == transferId);
            if (match is { Buyer: not null, Price: not null })
            {
                entity.Status = TransferStatus.Sold.ToString();
                entity.SoldPrice = match.Price;
                entity.SoldTo = match.Buyer.DisplayName;
                entity.Seller = match.Fancier?.DisplayName ?? entity.Seller;
                entity.BidCount = match.Bidders?.Count ?? entity.BidCount;
                await db.SaveChangesAsync(cancellationToken);
                return true;
            }
        }

        var liveItems = await FetchProcessedTransfersFromApiAsync(cancellationToken);
        var liveMatch = liveItems?.FirstOrDefault(x => x.Id == transferId);
        if (liveMatch is { Buyer: not null, Price: not null })
        {
            entity.Status = TransferStatus.Sold.ToString();
            entity.SoldPrice = liveMatch.Price;
            entity.SoldTo = liveMatch.Buyer.DisplayName;
            entity.Seller = liveMatch.Fancier?.DisplayName ?? entity.Seller;
            entity.BidCount = liveMatch.Bidders?.Count ?? entity.BidCount;
            await db.SaveChangesAsync(cancellationToken);
            return true;
        }

        if (entity.PigeonId.HasValue)
        {
            var pigeonTransfers = await FetchProcessedTransfersForPigeonAsync(entity.PigeonId.Value, cancellationToken);
            var pigeonMatch = pigeonTransfers?.FirstOrDefault(x => x.Id == transferId);
            if (pigeonMatch is { Buyer: not null, Price: not null })
            {
                entity.Status = TransferStatus.Sold.ToString();
                entity.SoldPrice = pigeonMatch.Price;
                entity.SoldTo = pigeonMatch.Buyer.DisplayName;
                entity.Seller = pigeonMatch.Fancier?.DisplayName ?? entity.Seller;
                entity.BidCount = pigeonMatch.Bidders?.Count ?? entity.BidCount;
                await db.SaveChangesAsync(cancellationToken);
                return true;
            }
        }

        if (entity.BidCount > 0)
        {
            entity.Status = TransferStatus.Sold.ToString();
            entity.SoldPrice ??= entity.StartPrice;
            await db.SaveChangesAsync(cancellationToken);
            return true;
        }

        return false;
    }

    public async Task UpdateTransferStatusAsync(
        int selectedFancierId,
        int transferId,
        TransferStatus newStatus,
        CancellationToken cancellationToken = default)
    {
        await using var db = await contextFactory.CreateDbContextAsync(cancellationToken);

        var entity = await db.CompletedTransfers
            .FirstOrDefaultAsync(
                x => x.SelectedFancierId == selectedFancierId && x.TransferId == transferId,
                cancellationToken);

        if (entity is null)
            return;

        entity.Status = newStatus.ToString();
        if (newStatus == TransferStatus.Expired)
        {
            entity.SoldPrice = null;
            entity.SoldTo = null;
        }
        else if (newStatus == TransferStatus.Sold)
        {
            entity.SoldPrice ??= entity.StartPrice;
        }

        await db.SaveChangesAsync(cancellationToken);
    }

    public async Task UpdateTransferBuyerAsync(
        int selectedFancierId,
        int transferId,
        string? buyer,
        CancellationToken cancellationToken = default)
    {
        await using var db = await contextFactory.CreateDbContextAsync(cancellationToken);

        var entity = await db.CompletedTransfers
            .FirstOrDefaultAsync(
                x => x.SelectedFancierId == selectedFancierId && x.TransferId == transferId,
                cancellationToken);

        if (entity is null)
            return;

        entity.SoldTo = string.IsNullOrWhiteSpace(buyer) ? null : buyer;
        await db.SaveChangesAsync(cancellationToken);
    }

    public async Task UpdateTransferSoldPriceAsync(
        int selectedFancierId,
        int transferId,
        decimal? soldPrice,
        CancellationToken cancellationToken = default)
    {
        await using var db = await contextFactory.CreateDbContextAsync(cancellationToken);

        var entity = await db.CompletedTransfers
            .FirstOrDefaultAsync(
                x => x.SelectedFancierId == selectedFancierId && x.TransferId == transferId,
                cancellationToken);

        if (entity is null)
            return;

        entity.SoldPrice = soldPrice;
        await db.SaveChangesAsync(cancellationToken);
    }

    private static async Task<IReadOnlyList<TransferListItem>> LoadPersistedCompletedTransfers(
        AppDbContext db,
        int selectedFancierId,
        CancellationToken cancellationToken)
    {
        var entities = (await db.CompletedTransfers
            .AsNoTracking()
            .Where(x => x.SelectedFancierId == selectedFancierId)
            .ToListAsync(cancellationToken))
            .OrderByDescending(x => x.TransferEnd)
            .ToList();

        return entities.Select(e =>
        {
            var status = Enum.TryParse<TransferStatus>(e.Status, out var s) ? s : TransferStatus.Expired;
            var skills = DeserializeSkills(e.SkillsJson);

            return new TransferListItem(
                e.TransferId,
                e.PigeonId,
                e.PigeonName ?? $"Pigeon #{e.TransferId}",
                e.Sex,
                e.Age,
                e.Breed,
                e.StartPrice,
                e.SoldPrice ?? e.StartPrice,
                e.Seller,
                status == TransferStatus.Sold ? e.SoldTo : null,
                e.BidCount,
                status == TransferStatus.Sold ? "Sold" : "Expired",
                e.TransferStart,
                e.TransferEnd,
                skills?.Total,
                FormatSkill(skills?.Total),
                FormatSkill(skills?.Form),
                FormatSkill(skills?.Experience),
                FormatSkill(skills?.Speed),
                FormatSkill(skills?.Technique),
                FormatSkill(skills?.Stamina),
                FormatSkill(skills?.Aerodynamics),
                FormatSkill(skills?.Intelligence),
                FormatSkill(skills?.Libido),
                FormatSkill(skills?.Nightvision),
                FormatSkill(skills?.Navigation),
                FormatSkill(skills?.Short),
                FormatSkill(skills?.Medium),
                FormatSkill(skills?.Long),
                status,
                e.SoldPrice,
                e.SoldTo);
        }).ToList();
    }

    private static string? SerializeSkills(TransferListItem item)
    {
        if (item.TotalSkill is null) return null;
        var obj = new PersistedSkills
        {
            Total = item.TotalSkill,
            Form = ParseSkill(item.FormDisplay),
            Experience = ParseSkill(item.ExperienceDisplay),
            Speed = ParseSkill(item.SpeedDisplay),
            Technique = ParseSkill(item.TechniqueDisplay),
            Stamina = ParseSkill(item.StaminaDisplay),
            Aerodynamics = ParseSkill(item.AerodynamicsDisplay),
            Intelligence = ParseSkill(item.IntelligenceDisplay),
            Libido = ParseSkill(item.LibidoDisplay),
            Nightvision = ParseSkill(item.NightvisionDisplay),
            Navigation = ParseSkill(item.NavigationDisplay),
            Short = ParseSkill(item.ShortDisplay),
            Medium = ParseSkill(item.MediumDisplay),
            Long = ParseSkill(item.LongDisplay),
        };
        return JsonSerializer.Serialize(obj, JsonOptions);
    }

    private static PersistedSkills? DeserializeSkills(string? json)
    {
        if (string.IsNullOrEmpty(json)) return null;
        try { return JsonSerializer.Deserialize<PersistedSkills>(json, JsonOptions); }
        catch { return null; }
    }

    private static decimal? ParseSkill(string? display) =>
        decimal.TryParse(display, out var v) ? v : null;

    private static IReadOnlyList<CompletedTransferSummary> BuildHistoricalSales(
        IReadOnlyList<TransferListItem> completedTransfers)
    {
        return completedTransfers
            .Where(t => t.Status == TransferStatus.Sold && t.SoldPrice.HasValue)
            .Select(t => new CompletedTransferSummary(
                t.SoldPrice!.Value,
                t.TotalSkill,
                ParseAgeToMonths(t.Age),
                t.Breed,
                t.BidCount,
                t.PigeonName,
                t.TransferId,
                ParseSkill(t.FormDisplay),
                ParseSkill(t.ExperienceDisplay),
                ParseSkill(t.SpeedDisplay),
                ParseSkill(t.TechniqueDisplay),
                ParseSkill(t.StaminaDisplay),
                ParseSkill(t.AerodynamicsDisplay),
                ParseSkill(t.IntelligenceDisplay),
                ParseSkill(t.LibidoDisplay),
                ParseSkill(t.NightvisionDisplay),
                ParseSkill(t.NavigationDisplay)))
            .ToList();
    }

    private static TransferListItem AttachPriceEstimate(
        TransferListItem item,
        IReadOnlyList<CompletedTransferSummary> historicalSales)
    {
        var targetAgeMonths = ParseAgeToMonths(item.Age);

        var request = new PriceEstimationRequest(
            item.TotalSkill,
            targetAgeMonths,
            item.Breed,
            ParseSkill(item.FormDisplay),
            ParseSkill(item.ExperienceDisplay),
            ParseSkill(item.SpeedDisplay),
            ParseSkill(item.TechniqueDisplay),
            ParseSkill(item.StaminaDisplay),
            ParseSkill(item.AerodynamicsDisplay),
            ParseSkill(item.IntelligenceDisplay),
            ParseSkill(item.LibidoDisplay),
            ParseSkill(item.NightvisionDisplay),
            ParseSkill(item.NavigationDisplay));

        var priceEstimates = MultiWindowPriceEstimator.Estimate(
            request, targetAgeMonths, historicalSales);

        return item with
        {
            PriceEstimates = priceEstimates,
        };
    }

    private static int? ParseAgeToMonths(string? age)
    {
        if (string.IsNullOrWhiteSpace(age))
            return null;

        // Format: "2y 3m" or "14 months"
        var yearMatch = System.Text.RegularExpressions.Regex.Match(age, @"(\d+)\s*y");
        var monthMatch = System.Text.RegularExpressions.Regex.Match(age, @"(\d+)\s*m");

        if (yearMatch.Success || monthMatch.Success)
        {
            int months = 0;
            if (yearMatch.Success)
                months += int.Parse(yearMatch.Groups[1].Value) * 12;
            if (monthMatch.Success)
                months += int.Parse(monthMatch.Groups[1].Value);
            return months;
        }

        return null;
    }

    private async Task<IReadOnlyList<TransferListItem>> DetectCompletedTransfers(
        List<RawApiSnapshotEntity> transferSnapshots,
        PigeonNameTranslations nameTranslations)
    {
        var activeSnapshots = transferSnapshots
            .Where(x => x.NormalizedQuery.Contains("processed=false", StringComparison.OrdinalIgnoreCase)
                && !x.NormalizedQuery.Contains("fancierId=", StringComparison.OrdinalIgnoreCase))
            .OrderByDescending(x => x.CapturedAtUtc)
            .Take(2)
            .ToArray();

        if (activeSnapshots.Length < 2)
            return [];

        var currentItems = DeserializeTransfers(activeSnapshots[0].ResponseBodyJson);
        var previousItems = DeserializeTransfers(activeSnapshots[1].ResponseBodyJson);

        var currentIds = new HashSet<int>(
            currentItems.Where(x => x.Id.HasValue).Select(x => x.Id!.Value));

        var disappeared = previousItems
            .Where(x => x.Id.HasValue && !currentIds.Contains(x.Id!.Value))
            .ToList();

        if (disappeared.Count == 0)
            return [];

        var processedSnapshots = transferSnapshots
            .Where(x => x.NormalizedQuery.Contains("processed=true", StringComparison.OrdinalIgnoreCase))
            .OrderByDescending(x => x.CapturedAtUtc)
            .ToList();

        var liveItems = await FetchProcessedTransfersFromApiAsync();

        var processedItems = (liveItems ?? processedSnapshots
                .SelectMany(s => DeserializeTransfers(s.ResponseBodyJson)))
            .Where(x => x.Id.HasValue)
            .GroupBy(x => x.Id!.Value)
            .ToDictionary(g => g.Key, g => g.First());

        var completed = new List<TransferListItem>();
        foreach (var item in disappeared)
        {
            if (processedItems.TryGetValue(item.Id!.Value, out var processedItem)
                && processedItem.Buyer is not null
                && processedItem.Price is not null)
            {
                completed.Add(CreateTransferListItem(
                    processedItem, nameTranslations, TransferStatus.Sold));
                continue;
            }

            // Try pigeonId-specific endpoint before falling back to heuristics
            if (item.Pigeon?.Id is int pigeonId)
            {
                var pigeonTransfers = await FetchProcessedTransfersForPigeonAsync(pigeonId);
                var pigeonMatch = pigeonTransfers?.FirstOrDefault(x => x.Id == item.Id);
                if (pigeonMatch is { Buyer: not null, Price: not null })
                {
                    completed.Add(CreateTransferListItem(
                        pigeonMatch, nameTranslations, TransferStatus.Sold));
                    continue;
                }
            }

            if (item.Bidders is { Count: > 0 })
            {
                completed.Add(CreateTransferListItem(
                    item with { Buyer = null }, nameTranslations, TransferStatus.Sold));
            }
            else
            {
                completed.Add(CreateTransferListItem(
                    item, nameTranslations, TransferStatus.Expired));
            }
        }

        return completed.OrderByDescending(x => x.End).ToList();
    }

    private async Task<IReadOnlyList<TransferItemDto>?> FetchProcessedTransfersFromApiAsync(
        CancellationToken cancellationToken = default)
    {
        if (apiClient is null)
            return null;

        try
        {
            var query = new Dictionary<string, string?> { ["processed"] = "true" };
            var response = await apiClient.GetJsonAsync("/api/transfer", query, cancellationToken);
            if (!response.IsSuccessStatusCode)
                return null;
            return DeserializeTransfers(response.Body);
        }
        catch
        {
            return null;
        }
    }

    private async Task<IReadOnlyList<TransferItemDto>?> FetchProcessedTransfersForPigeonAsync(
        int pigeonId,
        CancellationToken cancellationToken = default)
    {
        if (apiClient is null)
            return null;

        try
        {
            var query = new Dictionary<string, string?>
            {
                ["processed"] = "true",
                ["pigeonId"] = pigeonId.ToString(),
            };
            var response = await apiClient.GetJsonAsync("/api/transfer", query, cancellationToken);
            if (!response.IsSuccessStatusCode)
                return null;
            return DeserializeTransfers(response.Body);
        }
        catch
        {
            return null;
        }
    }

    private bool HasApiAccess => apiClient is not null;

    private static TransferListItem CreateTransferListItem(
        TransferItemDto item,
        PigeonNameTranslations nameTranslations,
        TransferStatus status)
    {
        var pigeon = item.Pigeon;
        var skills = pigeon?.Skills;

        var age = pigeon?.Years.HasValue == true && pigeon.Months.HasValue
            ? $"{pigeon.Years.Value}y {pigeon.Months.Value}m"
            : pigeon?.TotalMonths is int totalMonths ? $"{totalMonths} months" : null;

        var displayName = pigeon is not null
            ? PigeonNameResolver.CreateDisplayName(pigeon, nameTranslations)
            : $"Pigeon #{item.Id}";

        var timeRemaining = FormatTimeRemaining(item.End, status);

        decimal? displayPrice = status == TransferStatus.Sold
            ? item.Price ?? item.StartPrice
            : item.Price ?? item.StartPrice;

        var displayBuyer = status == TransferStatus.Sold
            ? item.Buyer?.DisplayName ?? "-"
            : item.Buyer?.DisplayName;

        var sexDisplay = pigeon?.Sex?.ToLowerInvariant() switch
        {
            "true" => "♂",
            "false" => "♀",
            _ => pigeon?.Sex,
        };

        return new TransferListItem(
            item.Id ?? 0,
            pigeon?.Id,
            displayName,
            sexDisplay,
            age,
            pigeon?.Breed,
            item.StartPrice,
            displayPrice,
            item.Fancier?.DisplayName ?? "-",
            displayBuyer,
            item.Bidders?.Count ?? 0,
            timeRemaining,
            item.Start,
            item.End,
            ComputeTotal(skills),
            FormatSkill(ComputeTotal(skills)),
            FormatSkill(ToOneBased(skills?.Form)),
            FormatSkill(ToOneBased(skills?.Experience)),
            FormatSkill(ToOneBased(skills?.Speed)),
            FormatSkill(ToOneBased(skills?.Technique)),
            FormatSkill(ToOneBased(skills?.Stamina)),
            FormatSkill(ToOneBased(skills?.Aerodynamics)),
            FormatSkill(ToOneBased(skills?.Intelligence)),
            FormatSkill(ToOneBased(skills?.Libido)),
            FormatSkill(ToOneBased(skills?.Nightvision)),
            FormatSkill(ToOneBased(skills?.Navigation)),
            FormatDistanceStat(ToOneBased(skills?.Speed), ToOneBased(skills?.Aerodynamics), ToOneBased(skills?.Intelligence)),
            FormatDistanceStat(ToOneBased(skills?.Stamina), ToOneBased(skills?.Speed), ToOneBased(skills?.Technique)),
            FormatDistanceStat(ToOneBased(skills?.Stamina), ToOneBased(skills?.Navigation), ToOneBased(skills?.Intelligence)),
            status,
            status == TransferStatus.Sold ? item.Price : null,
            status == TransferStatus.Sold ? item.Buyer?.DisplayName : null);
    }

    private static string FormatTimeRemaining(DateTimeOffset? end, TransferStatus status)
    {
        if (status == TransferStatus.Sold)
            return "Sold";
        if (status == TransferStatus.Expired)
            return "Expired";
        if (end is not DateTimeOffset endDate)
            return "—";

        var remaining = endDate - DateTimeOffset.UtcNow;
        if (remaining.TotalSeconds <= 0)
            return "Ended";
        if (remaining.TotalDays >= 1)
            return $"{(int)remaining.TotalDays}d {remaining.Hours}h";
        if (remaining.TotalHours >= 1)
            return $"{(int)remaining.TotalHours}h {remaining.Minutes}m";
        return $"{(int)remaining.TotalMinutes}m";
    }

    private static decimal? ComputeTotal(PigeonSkillsDto? skills) => skills?.Total + 6;

    private static decimal? ToOneBased(decimal? value) => value + 1;

    private static string? FormatSkill(decimal? skillValue) =>
        skillValue?.ToString("N0");

    private static string? FormatDistanceStat(
        decimal? skill1, decimal? skill2, decimal? skill3)
    {
        if (skill1 is not decimal a || skill2 is not decimal b || skill3 is not decimal c)
            return null;

        return (a + b + c).ToString("N0");
    }

    private static IReadOnlyList<TransferItemDto> DeserializeTransfers(string json)
    {
        try
        {
            using var document = JsonDocument.Parse(json);
            var root = document.RootElement;
            if (root.ValueKind == JsonValueKind.Array)
            {
                return JsonSerializer.Deserialize<List<TransferItemDto>>(root.GetRawText(), JsonOptions) ?? [];
            }

            if (root.ValueKind == JsonValueKind.Object)
            {
                foreach (var propertyName in new[] { "items", "transfers", "data" })
                {
                    if (root.TryGetProperty(propertyName, out var property)
                        && property.ValueKind == JsonValueKind.Array)
                    {
                        return JsonSerializer.Deserialize<List<TransferItemDto>>(property.GetRawText(), JsonOptions) ?? [];
                    }
                }
            }
        }
        catch (JsonException)
        {
        }

        return [];
    }

    private static DistanceStats? ExtractDistanceStats(TransferListItem item)
    {
        var s = ParseSkill(item.ShortDisplay);
        var m = ParseSkill(item.MediumDisplay);
        var l = ParseSkill(item.LongDisplay);
        var t = item.TotalSkill;
        if (s is null || m is null || l is null || t is null)
            return null;
        return new DistanceStats(s.Value, m.Value, l.Value, t.Value);
    }

    private static DistanceStats? ExtractDistanceStats(PigeonListItem item)
    {
        var s = ParseSkill(item.ShortDisplay);
        var m = ParseSkill(item.MediumDisplay);
        var l = ParseSkill(item.LongDisplay);
        var t = item.TotalSkill;
        if (s is null || m is null || l is null || t is null)
            return null;
        return new DistanceStats(s.Value, m.Value, l.Value, t.Value);
    }

    private static PercentilePopulationMember? ExtractPopulationMember(TransferListItem item)
    {
        var stats = ExtractDistanceStats(item);
        if (stats is null) return null;
        return new PercentilePopulationMember(
            item.PigeonName, item.TransferId,
            stats.Short, stats.Medium, stats.Long, stats.Total,
            item.Breed, item.Age);
    }

    private static PercentilePopulationMember? ExtractPopulationMember(PigeonListItem item)
    {
        var stats = ExtractDistanceStats(item);
        if (stats is null) return null;
        return new PercentilePopulationMember(
            item.DisplayName, item.SourceId,
            stats.Short, stats.Medium, stats.Long, stats.Total,
            item.Breed, item.Age);
    }

    private static TransferListItem AttachStatsComparison(
        TransferListItem item,
        IReadOnlyList<TransferListItem> marketTransfers,
        IReadOnlyList<PigeonListItem> flockPigeons)
    {
        var target = ExtractDistanceStats(item);
        if (target is null)
            return item;

        var targetAgeMonths = ParseAgeToMonths(item.Age);

        var marketPercentiles = MultiWindowComparer.Compare(
            target, targetAgeMonths, marketTransfers,
            t => ParseAgeToMonths(t.Age),
            ExtractDistanceStats,
            ExtractPopulationMember);

        var flockPercentiles = MultiWindowComparer.Compare(
            target, targetAgeMonths, flockPigeons,
            p => ParseAgeToMonths(p.Age),
            ExtractDistanceStats,
            ExtractPopulationMember);

        return item with
        {
            MarketPercentiles = marketPercentiles,
            FlockPercentiles = flockPercentiles,
        };
    }
}

internal sealed class PersistedSkills
{
    public decimal? Total { get; set; }
    public decimal? Form { get; set; }
    public decimal? Experience { get; set; }
    public decimal? Speed { get; set; }
    public decimal? Technique { get; set; }
    public decimal? Stamina { get; set; }
    public decimal? Aerodynamics { get; set; }
    public decimal? Intelligence { get; set; }
    public decimal? Libido { get; set; }
    public decimal? Nightvision { get; set; }
    public decimal? Navigation { get; set; }
    public decimal? Short { get; set; }
    public decimal? Medium { get; set; }
    public decimal? Long { get; set; }
}
