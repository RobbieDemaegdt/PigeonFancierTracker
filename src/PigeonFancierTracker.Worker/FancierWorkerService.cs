using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using PigeonFancierTracker.Core.Contracts;
using PigeonFancierTracker.Core.Domain;
using PigeonFancierTracker.Infrastructure.Http;
using PigeonFancierTracker.Infrastructure.Persistence;
using PigeonFancierTracker.Infrastructure.PigeonFancierApi;

namespace PigeonFancierTracker.Worker;

public sealed class FancierWorkerService(
    IOptions<WorkerOptions> options,
    ILoginService loginService,
    ISessionStateService sessionState,
    ISyncCoordinator syncCoordinator,
    IAutoBidService autoBidService,
    IFinanceGuard financeGuard,
    IFlightManager flightManager,
    IFoodManager foodManager,
    ITrainingManager trainingManager,
    ILoftManager loftManager,
    IBreedingManager breedingManager,
    FlightResultIngester flightResultIngester,
    FoodDistributionIngester foodDistributionIngester,
    AuthenticatedReadTransportProxy transportProxy,
    HttpClientReadTransport readTransport,
    PigeonFancierApiClient apiClient,
    ILogger<FancierWorkerService> logger) : BackgroundService
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        NumberHandling = JsonNumberHandling.AllowReadingFromString,
    };

    private readonly WorkerOptions config = options.Value;
    private volatile bool sessionExpired;

    public override Task StartAsync(CancellationToken cancellationToken)
    {
        sessionState.Changed += OnSessionStateChanged;
        autoBidService.LogMessage += OnAutoBidLog;
        return base.StartAsync(cancellationToken);
    }

    public override Task StopAsync(CancellationToken cancellationToken)
    {
        autoBidService.Stop();
        syncCoordinator.Cancel();
        sessionState.Changed -= OnSessionStateChanged;
        autoBidService.LogMessage -= OnAutoBidLog;
        logger.LogInformation("Worker stopped");
        return base.StopAsync(cancellationToken);
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        logger.LogInformation("Autonomous fancier worker starting for fancier {FancierId}", config.FancierId);

        await EnsureAuthenticatedAsync(stoppingToken);
        await RunSyncCycleAsync(stoppingToken);
        await RunManagementCycleAsync(stoppingToken);
        StartAutoBid();

        var interval = TimeSpan.FromMinutes(Math.Max(config.SyncIntervalMinutes, 1));
        logger.LogInformation("Entering periodic sync loop (interval: {Interval})", interval);

        using var timer = new PeriodicTimer(interval);
        while (await timer.WaitForNextTickAsync(stoppingToken))
        {
            try
            {
                await EnsureAuthenticatedAsync(stoppingToken);
                await RunSyncCycleAsync(stoppingToken);
                await RunManagementCycleAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Sync cycle failed, will retry next interval");
            }
        }
    }

    private async Task EnsureAuthenticatedAsync(CancellationToken ct)
    {
        if (sessionState.Current.State == SessionState.AuthenticatedReady && !sessionExpired)
        {
            var check = await apiClient.GetJsonAsync("/api/user", cancellationToken: ct);
            if (check.IsSuccessStatusCode)
                return;

            logger.LogWarning("Session validation failed (HTTP {Status}), re-authenticating", check.StatusCode);
        }

        sessionExpired = false;
        const int maxAttempts = 5;

        for (var attempt = 1; attempt <= maxAttempts; attempt++)
        {
            ct.ThrowIfCancellationRequested();

            logger.LogInformation("Authentication attempt {Attempt}/{Max}", attempt, maxAttempts);
            transportProxy.SetTransport(readTransport);

            var loginResult = await loginService.LoginAsync(config.Email, config.Password, ct);
            if (!loginResult.Success)
            {
                logger.LogWarning("Login failed: {Error}", loginResult.ErrorMessage);
                if (attempt < maxAttempts)
                {
                    var delay = TimeSpan.FromSeconds(Math.Min(60, 5 * Math.Pow(2, attempt - 1)));
                    await Task.Delay(delay, ct);
                    continue;
                }

                throw new InvalidOperationException($"Login failed after {maxAttempts} attempts: {loginResult.ErrorMessage}");
            }

            logger.LogInformation("Login successful, selecting fancier {FancierId}", config.FancierId);
            var selectResult = await loginService.SelectFancierAsync(config.FancierId, ct);
            if (!selectResult.Success)
            {
                logger.LogWarning("Fancier selection failed: {Error}", selectResult.ErrorMessage);
                if (attempt < maxAttempts)
                {
                    var delay = TimeSpan.FromSeconds(Math.Min(60, 5 * Math.Pow(2, attempt - 1)));
                    await Task.Delay(delay, ct);
                    continue;
                }

                throw new InvalidOperationException($"Fancier selection failed after {maxAttempts} attempts: {selectResult.ErrorMessage}");
            }

            var userResponse = await apiClient.GetJsonAsync("/api/user", cancellationToken: ct);
            if (!userResponse.IsSuccessStatusCode)
            {
                logger.LogWarning("Session validation failed after login (HTTP {Status})", userResponse.StatusCode);
                if (attempt < maxAttempts)
                {
                    var delay = TimeSpan.FromSeconds(Math.Min(60, 5 * Math.Pow(2, attempt - 1)));
                    await Task.Delay(delay, ct);
                    continue;
                }

                throw new InvalidOperationException("Session validation failed after login.");
            }

            var user = Deserialize<UserDto>(userResponse.Body);

            var selectedResponse = await apiClient.GetJsonAsync("/api/fancier/selected", cancellationToken: ct);
            SelectedFancierDto? selectedFancier = null;
            if (selectedResponse.IsSuccessStatusCode)
            {
                selectedFancier = Deserialize<SelectedFancierDto>(selectedResponse.Body);
            }

            sessionState.SetState(SessionState.AuthenticatedReady, user, selectedFancier);
            logger.LogInformation("Authenticated as fancier {Name} (ID {Id})",
                selectedFancier?.DisplayName ?? config.FancierId.ToString(),
                config.FancierId);
            return;
        }
    }

    private async Task RunSyncCycleAsync(CancellationToken ct)
    {
        logger.LogInformation("Starting sync cycle");

        try
        {
            var result = await syncCoordinator.SyncAsync(SyncProfile.Quick, ct);
            var succeeded = result.EndpointResults.Count(x => x.IsSuccess);
            var failed = result.EndpointResults.Count - succeeded;
            logger.LogInformation("Sync completed: {Status} ({Succeeded} succeeded, {Failed} failed, {Duration:N1}s)",
                result.Status, succeeded, failed, (result.CompletedAtUtc - result.StartedAtUtc).TotalSeconds);

            if (result.Status == "SessionExpired")
            {
                logger.LogWarning("Session expired during sync, will re-authenticate");
                sessionExpired = true;
                await EnsureAuthenticatedAsync(ct);

                logger.LogInformation("Retrying sync after re-authentication");
                result = await syncCoordinator.SyncAsync(SyncProfile.Quick, ct);
                succeeded = result.EndpointResults.Count(x => x.IsSuccess);
                failed = result.EndpointResults.Count - succeeded;
                logger.LogInformation("Retry sync completed: {Status} ({Succeeded} succeeded, {Failed} failed)",
                    result.Status, succeeded, failed);
            }

            if (result.IsSuccess)
            {
                await IngestFlightsAsync(ct);
            }
        }
        catch (InvalidOperationException ex) when (ex.Message.Contains("selected fancier", StringComparison.OrdinalIgnoreCase))
        {
            logger.LogWarning("Sync requires a selected fancier, re-authenticating");
            sessionExpired = true;
            await EnsureAuthenticatedAsync(ct);
            await RunSyncCycleAsync(ct);
        }
    }

    private async Task IngestFlightsAsync(CancellationToken ct)
    {
        try
        {
            logger.LogInformation("Ingesting flight results");
            await flightResultIngester.IngestAsync(config.FancierId, ct);
            logger.LogInformation("Flight ingestion completed");

            logger.LogInformation("Ingesting food distribution snapshot");
            await foodDistributionIngester.BackfillAsync(config.FancierId, ct);
            await foodDistributionIngester.IngestAsync(config.FancierId, ct);
            logger.LogInformation("Food distribution ingestion completed");
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Flight ingestion failed");
        }
    }

    private async Task RunManagementCycleAsync(CancellationToken ct)
    {
        if (!config.ManagementEnabled)
            return;

        try
        {
            FinanceStatus? financeStatus = null;
            FoodManagementPlan? foodPlan = null;
            TrainingManagementPlan? trainingPlan = null;
            FlightEnrollmentPlan? flightPlan = null;
            BreedingManagementPlan? breedingPlan = null;
            LoftManagementPlan? loftPlan = null;

            if (config.AutoFinanceGuardEnabled)
            {
                financeStatus = await financeGuard.EvaluateAsync(
                    config.FancierId, config.MinBalanceAlert, ct);

                foreach (var warning in financeStatus.Warnings)
                    logger.LogWarning("Finance: {Warning}", warning);

                logger.LogInformation(
                    "Balance: {Balance:F2} (delta: {Delta:F2}), alert: {Alert}, spending: {Allowed}",
                    financeStatus.Balance, financeStatus.BalanceDelta,
                    financeStatus.AlertLevel, financeStatus.SpendingAllowed);

                if (!financeStatus.SpendingAllowed)
                {
                    logger.LogWarning("Spending blocked — skipping management");

                    if (config.DryRun)
                        await WriteAdvisorReportAsync(new AdvisorReport(
                            DateTime.UtcNow, financeStatus, null, null, null, null, null));

                    return;
                }
            }

            if (config.AutoFeedEnabled)
            {
                logger.LogInformation("Building food plan");
                foodPlan = await foodManager.BuildFoodPlanAsync(config.FancierId, config.MinFoodDaysReserve, ct);

                if (!config.DryRun && (foodPlan.Purchases.Count > 0 || foodPlan.DistributionChange is not null))
                {
                    await foodManager.ExecuteFoodPlanAsync(foodPlan, ct);
                    logger.LogInformation("Food management completed");
                }
            }

            if (config.AutoTrainEnabled)
            {
                logger.LogInformation("Building training plan");
                trainingPlan = await trainingManager.BuildTrainingPlanAsync(config.FancierId, ct);

                if (!config.DryRun)
                {
                    await trainingManager.ExecuteTrainingPlanAsync(trainingPlan, ct);
                    logger.LogInformation("Training management completed");
                }
            }

            if (config.AutoFlightEnabled)
            {
                logger.LogInformation("Building flight enrollment plan");
                flightPlan = await flightManager.BuildEnrollmentPlanAsync(config.FancierId, ct);

                if (!config.DryRun && flightPlan.Actions.Count > 0)
                {
                    var enrolled = await flightManager.ExecuteEnrollmentAsync(flightPlan, ct);
                    logger.LogInformation("Flight enrollment completed: {Enrolled}/{Total}",
                        enrolled, flightPlan.Actions.Count);
                }
            }

            if (config.AutoBreedEnabled)
            {
                logger.LogInformation("Building breeding plan");
                breedingPlan = await breedingManager.BuildBreedingPlanAsync(config.FancierId, ct);

                if (!config.DryRun && (breedingPlan.PairsToCreate.Count > 0 || breedingPlan.PairsToSplit.Count > 0))
                {
                    await breedingManager.ExecuteBreedingPlanAsync(breedingPlan, ct);
                    logger.LogInformation("Breeding management completed");
                }
            }

            if (config.AutoLoftEnabled)
            {
                logger.LogInformation("Building loft plan");
                loftPlan = await loftManager.BuildLoftPlanAsync(
                    config.FancierId, config.AutoBarnUpgradeEnabled, ct);

                if (!config.DryRun && (loftPlan.CleanAction is not null
                    || loftPlan.PenPurchaseAction is not null
                    || loftPlan.BarnUpgrade is not null))
                {
                    await loftManager.ExecuteLoftPlanAsync(loftPlan, ct);
                    logger.LogInformation("Loft management completed");
                }
            }

            if (config.DryRun)
            {
                await WriteAdvisorReportAsync(new AdvisorReport(
                    DateTime.UtcNow, financeStatus, foodPlan, trainingPlan,
                    flightPlan, breedingPlan, loftPlan));
            }
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Management cycle failed");
        }
    }

    private async Task WriteAdvisorReportAsync(AdvisorReport report)
    {
        var summary = FormatAdvisorSummary(report);
        logger.LogInformation("{AdvisorReport}", summary);

        try
        {
            var reportsDir = Path.Combine(config.AppDataDirectory, "advisor-reports");
            Directory.CreateDirectory(reportsDir);

            var fileName = $"report-{report.GeneratedAtUtc:yyyy-MM-dd_HH-mm}.json";
            var filePath = Path.Combine(reportsDir, fileName);

            var json = System.Text.Json.JsonSerializer.Serialize(report, new System.Text.Json.JsonSerializerOptions
            {
                WriteIndented = true,
                PropertyNamingPolicy = System.Text.Json.JsonNamingPolicy.CamelCase,
                Converters = { new System.Text.Json.Serialization.JsonStringEnumConverter() },
            });
            await File.WriteAllTextAsync(filePath, json);

            logger.LogInformation("Advisor report saved to {Path}", filePath);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Failed to write advisor report file");
        }
    }

    private static string FormatAdvisorSummary(AdvisorReport report)
    {
        var sb = new System.Text.StringBuilder();
        var line = new string('=', 55);

        sb.AppendLine();
        sb.AppendLine(line);
        sb.AppendLine($"  ADVISOR REPORT — {report.GeneratedAtUtc:yyyy-MM-dd HH:mm} UTC");
        sb.AppendLine(line);

        if (report.Finance is { } fin)
        {
            sb.AppendLine();
            sb.AppendLine("  FINANCE");
            sb.AppendLine($"     Balance: {fin.Balance:F2} (delta: {fin.BalanceDelta:F2})");
            sb.AppendLine($"     Transfer balance: {fin.TransferBalance:F2}, Savings: {fin.Savings:F2}");
            sb.AppendLine($"     Alert: {fin.AlertLevel} — spending {(fin.SpendingAllowed ? "allowed" : "BLOCKED")}");
            foreach (var w in fin.Warnings)
                sb.AppendLine($"     !! {w}");
        }

        if (report.FoodPlan is { } food)
        {
            sb.AppendLine();
            sb.AppendLine($"  FOOD ({food.DaysRemaining:F1} days remaining)");
            if (food.Purchases.Count > 0)
            {
                foreach (var p in food.Purchases)
                    sb.AppendLine($"     -> Buy {p.Amount}x {p.Name} @ {p.UnitPrice:F2} = {p.TotalCost:F2}");
            }
            if (food.DistributionChange is { } dist)
            {
                sb.AppendLine($"     -> Set distribution: B:{dist.Barley}% G:{dist.Grain}% C:{dist.Corn}% P:{dist.Peanut}%");
            }
            if (food.Purchases.Count == 0 && food.DistributionChange is null)
                sb.AppendLine("     No actions needed");
        }

        if (report.TrainingPlan is { } train)
        {
            sb.AppendLine();
            sb.AppendLine("  TRAINING");
            sb.AppendLine($"     Current: {train.CurrentFocus?.ToString() ?? "unknown"}");
            sb.AppendLine($"     -> Set focus to {train.Recommendation.RecommendedFocus} (score={train.Recommendation.Score:F2})");
            sb.AppendLine($"        Reason: {train.Recommendation.Reason}");
        }

        if (report.FlightPlan is { } flights)
        {
            sb.AppendLine();
            sb.AppendLine("  FLIGHTS");
            if (flights.Actions.Count > 0)
            {
                foreach (var a in flights.Actions)
                    sb.AppendLine($"     -> Enroll \"{a.PigeonName}\" in {a.FlightType} #{a.FlightId} ({a.Location}, {a.DistanceKm}km) score={a.Score:F1}");
            }
            else
            {
                sb.AppendLine("     No enrollments needed");
            }
        }

        if (report.BreedingPlan is { } breed)
        {
            sb.AppendLine();
            sb.AppendLine($"  BREEDING ({breed.CurrentCoupleCount} couples, {breed.AvailableBreedingSlots} slots)");
            if (breed.PairsToSplit.Count > 0)
            {
                foreach (var p in breed.PairsToSplit)
                    sb.AppendLine($"     -> Split couple #{p.CoupleId}: {p.CockName} + {p.HenName} ({p.Days} days)");
            }
            if (breed.PairsToCreate.Count > 0)
            {
                foreach (var c in breed.PairsToCreate)
                    sb.AppendLine($"     -> Pair {c.CockName} + {c.HenName} (score={c.CompatibilityScore:F1})");
            }
            if (breed.PairsToSplit.Count == 0 && breed.PairsToCreate.Count == 0)
                sb.AppendLine("     No actions needed");
        }

        if (report.LoftPlan is { } loft)
        {
            sb.AppendLine();
            sb.AppendLine($"  LOFT (capacity: {loft.CurrentOccupied}/{loft.CurrentCapacity}, {loft.OccupancyPercent:F0}% full, dirt: {loft.CurrentDirt?.ToString() ?? "?"})");
            if (loft.CleanAction is { } clean)
                sb.AppendLine($"     -> Clean loft (dirt={clean.CurrentDirt}): {clean.Reason}");
            if (loft.PenPurchaseAction is { } pen)
                sb.AppendLine($"     -> Buy {pen.Amount}x {pen.Name} @ {pen.UnitPrice:F2} = {pen.TotalCost:F2}");
            if (loft.BarnUpgrade is { } upgrade)
                sb.AppendLine($"     -> Upgrade barn from {upgrade.CurrentTier} (size={upgrade.CurrentSize}): {upgrade.Reason}");
            if (loft.CleanAction is null && loft.PenPurchaseAction is null && loft.BarnUpgrade is null)
                sb.AppendLine("     No actions needed");
        }

        sb.AppendLine();
        sb.AppendLine(line);

        return sb.ToString();
    }

    private void StartAutoBid()
    {
        if (!config.AutoBidEnabled || config.AutoBidRules.Count == 0)
        {
            logger.LogInformation("Auto-bid disabled or no rules configured");
            return;
        }

        foreach (var rule in config.AutoBidRules)
        {
            autoBidService.AddTransfer(rule.TransferId, rule.PigeonName, 0, null, rule.MaxPrice);
            logger.LogInformation("Auto-bid rule: {Pigeon} (#{TransferId}) max {MaxPrice}",
                rule.PigeonName, rule.TransferId, rule.MaxPrice);
        }

        autoBidService.Start(config.FancierId);
        logger.LogInformation("Auto-bid started with {Count} rules", config.AutoBidRules.Count);
    }

    private void OnSessionStateChanged(object? sender, SessionSnapshot snapshot)
    {
        if (snapshot.State == SessionState.SessionExpired)
        {
            logger.LogWarning("Session expired detected");
            sessionExpired = true;
        }
    }

    private void OnAutoBidLog(string message)
    {
        logger.LogInformation("[AutoBid] {Message}", message);
    }

    private static T? Deserialize<T>(string body) =>
        string.IsNullOrWhiteSpace(body) ? default : JsonSerializer.Deserialize<T>(body, JsonOptions);
}
