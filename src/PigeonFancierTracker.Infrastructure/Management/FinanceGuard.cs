using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using PigeonFancierTracker.Core.Contracts;
using PigeonFancierTracker.Infrastructure.Persistence;

namespace PigeonFancierTracker.Infrastructure.Management;

public sealed class FinanceGuard(
    IDbContextFactory<AppDbContext> contextFactory,
    ILogger<FinanceGuard> logger) : IFinanceGuard
{
    private const decimal BankruptcyThreshold = -5000m;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        NumberHandling = JsonNumberHandling.AllowReadingFromString,
    };

    public async Task<FinanceStatus> EvaluateAsync(
        int fancierId,
        decimal minBalanceAlert,
        CancellationToken cancellationToken = default)
    {
        var fancier = await ReadFancierSnapshotAsync(fancierId, cancellationToken);
        var warnings = new List<string>();

        if (fancier?.Finances is null)
        {
            logger.LogWarning("No financial data available for fancier {FancierId}", fancierId);
            return new FinanceStatus(
                Balance: 0,
                PreviousBalance: 0,
                BalanceDelta: 0,
                TransferBalance: 0,
                Savings: 0,
                AlertLevel: FinanceAlertLevel.Critical,
                SpendingAllowed: false,
                Warnings: ["No financial data available — blocking spending as precaution"]);
        }

        var balance = fancier.Finances.Capital ?? 0;
        var previousBalance = fancier.Finances.PreviousBalance ?? 0;
        var balanceDelta = balance - previousBalance;
        var transferBalance = fancier.Finances.TransferBalance ?? 0;
        var savings = fancier.Finances.Savings ?? 0;

        var alertLevel = DetermineAlertLevel(balance, minBalanceAlert);

        if (alertLevel == FinanceAlertLevel.Bankrupt)
            warnings.Add($"BANKRUPT: balance €{balance:F2} is below €{BankruptcyThreshold:F2}");
        else if (alertLevel == FinanceAlertLevel.Critical)
            warnings.Add($"Balance €{balance:F2} is negative — spending blocked");
        else if (alertLevel == FinanceAlertLevel.Low)
            warnings.Add($"Balance €{balance:F2} is below alert threshold €{minBalanceAlert:F2}");

        if (balanceDelta < -500m)
            warnings.Add($"Large balance drop: €{balanceDelta:F2} since last cycle");

        var spendingAllowed = alertLevel < FinanceAlertLevel.Critical;

        return new FinanceStatus(
            Balance: balance,
            PreviousBalance: previousBalance,
            BalanceDelta: balanceDelta,
            TransferBalance: transferBalance,
            Savings: savings,
            AlertLevel: alertLevel,
            SpendingAllowed: spendingAllowed,
            Warnings: warnings);
    }

    private static FinanceAlertLevel DetermineAlertLevel(decimal balance, decimal minBalanceAlert)
    {
        if (balance < BankruptcyThreshold)
            return FinanceAlertLevel.Bankrupt;
        if (balance < 0)
            return FinanceAlertLevel.Critical;
        if (balance < minBalanceAlert)
            return FinanceAlertLevel.Low;
        return FinanceAlertLevel.None;
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
}
