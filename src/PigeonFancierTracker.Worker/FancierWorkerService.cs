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
            if (config.AutoFinanceGuardEnabled)
            {
                var financeStatus = await financeGuard.EvaluateAsync(
                    config.FancierId, config.MinBalanceAlert, ct);

                foreach (var warning in financeStatus.Warnings)
                    logger.LogWarning("Finance: {Warning}", warning);

                logger.LogInformation(
                    "Balance: €{Balance:F2} (delta: €{Delta:F2}), alert: {Alert}, spending: {Allowed}",
                    financeStatus.Balance, financeStatus.BalanceDelta,
                    financeStatus.AlertLevel, financeStatus.SpendingAllowed);

                if (!financeStatus.SpendingAllowed)
                {
                    logger.LogWarning("Spending blocked — skipping food and flight management");
                    return;
                }
            }

            if (config.AutoFeedEnabled)
            {
                logger.LogInformation("Running food management");
                var foodPlan = await foodManager.BuildFoodPlanAsync(config.FancierId, config.MinFoodDaysReserve, ct);

                foreach (var skip in foodPlan.Skipped)
                    logger.LogDebug("Food skip: {Reason}", skip);

                var hasPurchases = foodPlan.Purchases.Count > 0;
                var hasDistChange = foodPlan.DistributionChange is not null;

                if (hasPurchases || hasDistChange)
                {
                    if (config.DryRun)
                    {
                        if (hasPurchases)
                        {
                            logger.LogInformation("[DRY RUN] Would buy food ({Days:F1} days remaining):", foodPlan.DaysRemaining);
                            foreach (var purchase in foodPlan.Purchases)
                                logger.LogInformation("[DRY RUN]   {Amount}x {Name} @ €{Price:F2} = €{Total:F2}",
                                    purchase.Amount, purchase.Name, purchase.UnitPrice, purchase.TotalCost);
                        }

                        if (foodPlan.DistributionChange is { } dist)
                        {
                            logger.LogInformation("[DRY RUN] Would set food distribution: B:{Barley}% G:{Grain}% C:{Corn}% P:{Peanut}%",
                                dist.Barley, dist.Grain, dist.Corn, dist.Peanut);
                        }
                    }
                    else
                    {
                        await foodManager.ExecuteFoodPlanAsync(foodPlan, ct);
                        logger.LogInformation("Food management completed");
                    }
                }
                else
                {
                    logger.LogInformation("No food actions needed this cycle ({Days:F1} days remaining)", foodPlan.DaysRemaining);
                }
            }

            if (config.AutoTrainEnabled)
            {
                logger.LogInformation("Running training management");
                var trainingPlan = await trainingManager.BuildTrainingPlanAsync(config.FancierId, ct);

                foreach (var skip in trainingPlan.Skipped)
                    logger.LogDebug("Training skip: {Reason}", skip);

                foreach (var analysis in trainingPlan.SkillAnalysis)
                    logger.LogDebug("Training analysis: {Analysis}", analysis);

                if (config.DryRun)
                {
                    logger.LogInformation(
                        "[DRY RUN] Would set training focus to {Focus} (score={Score:F2}): {Reason}",
                        trainingPlan.Recommendation.RecommendedFocus,
                        trainingPlan.Recommendation.Score,
                        trainingPlan.Recommendation.Reason);
                }
                else
                {
                    await trainingManager.ExecuteTrainingPlanAsync(trainingPlan, ct);
                    logger.LogInformation("Training management completed");
                }
            }

            if (config.AutoFlightEnabled)
            {
                logger.LogInformation("Running flight enrollment");
                var plan = await flightManager.BuildEnrollmentPlanAsync(config.FancierId, ct);

                foreach (var skip in plan.Skipped)
                    logger.LogDebug("Flight skip: {Reason}", skip);

                if (plan.Actions.Count > 0)
                {
                    if (config.DryRun)
                    {
                        logger.LogInformation("[DRY RUN] Would enroll {Count} pigeons in flights:", plan.Actions.Count);
                        foreach (var action in plan.Actions)
                            logger.LogInformation("[DRY RUN]   {Pigeon} → {Type} {FlightId} ({Location}, {Distance}km) score={Score:F1}",
                                action.PigeonName, action.FlightType, action.FlightId,
                                action.Location, action.DistanceKm, action.Score);
                    }
                    else
                    {
                        var enrolled = await flightManager.ExecuteEnrollmentAsync(plan, ct);
                        logger.LogInformation("Flight enrollment completed: {Enrolled}/{Total} pigeons enrolled",
                            enrolled, plan.Actions.Count);
                    }
                }
                else
                {
                    logger.LogInformation("No flight enrollments needed this cycle");
                }
            }

            if (config.AutoBreedEnabled)
            {
                logger.LogInformation("Running breeding management");
                var breedingPlan = await breedingManager.BuildBreedingPlanAsync(config.FancierId, ct);

                foreach (var skip in breedingPlan.Skipped)
                    logger.LogDebug("Breeding skip: {Reason}", skip);

                var hasActions = breedingPlan.PairsToCreate.Count > 0
                    || breedingPlan.PairsToSplit.Count > 0;

                if (hasActions)
                {
                    if (config.DryRun)
                    {
                        foreach (var pair in breedingPlan.PairsToSplit)
                            logger.LogInformation(
                                "[DRY RUN] Would split incompatible couple {CoupleId}: {Cock} + {Hen} ({Days} days)",
                                pair.CoupleId, pair.CockName, pair.HenName, pair.Days);

                        foreach (var candidate in breedingPlan.PairsToCreate)
                            logger.LogInformation(
                                "[DRY RUN] Would pair {Cock} (♂) + {Hen} (♀), score={Score:F1}",
                                candidate.CockName, candidate.HenName, candidate.CompatibilityScore);
                    }
                    else
                    {
                        await breedingManager.ExecuteBreedingPlanAsync(breedingPlan, ct);
                        logger.LogInformation("Breeding management completed");
                    }
                }
                else
                {
                    logger.LogInformation(
                        "No breeding actions needed this cycle ({Couples} active couples, {Slots} slots available)",
                        breedingPlan.CurrentCoupleCount, breedingPlan.AvailableBreedingSlots);
                }
            }

            if (config.AutoLoftEnabled)
            {
                logger.LogInformation("Running loft management");
                var loftPlan = await loftManager.BuildLoftPlanAsync(config.FancierId, ct);

                foreach (var skip in loftPlan.Skipped)
                    logger.LogDebug("Loft skip: {Reason}", skip);

                var hasActions = loftPlan.CleanAction is not null
                    || loftPlan.PenPurchaseAction is not null;

                if (hasActions)
                {
                    if (config.DryRun)
                    {
                        if (loftPlan.CleanAction is { } cleanAction)
                            logger.LogInformation("[DRY RUN] Would clean loft (dirt={Dirt})",
                                cleanAction.CurrentDirt);

                        if (loftPlan.PenPurchaseAction is { } penAction)
                            logger.LogInformation("[DRY RUN] Would buy pens: {Amount}x {Name} @ €{Price:F2} = €{Total:F2}",
                                penAction.Amount, penAction.Name, penAction.UnitPrice, penAction.TotalCost);
                    }
                    else
                    {
                        await loftManager.ExecuteLoftPlanAsync(loftPlan, ct);
                        logger.LogInformation("Loft management completed");
                    }
                }
                else
                {
                    logger.LogInformation("No loft actions needed this cycle");
                }
            }
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Management cycle failed");
        }
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
