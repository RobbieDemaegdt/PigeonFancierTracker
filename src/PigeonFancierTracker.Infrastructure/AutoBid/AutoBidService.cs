using System.Collections.Concurrent;
using System.Text.Json;
using PigeonFancierTracker.Core.Contracts;

namespace PigeonFancierTracker.Infrastructure.AutoBid;

public sealed class AutoBidService : IAutoBidService
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    };

    private readonly IAuthenticatedWriteTransport writeTransport;
    private readonly IAuthenticatedReadTransport readTransport;
    private readonly ConcurrentDictionary<int, AutoBidEntry> entries = new();
    private CancellationTokenSource? cts;
    private int fancierId;

    public AutoBidService(
        IAuthenticatedWriteTransport writeTransport,
        IAuthenticatedReadTransport readTransport)
    {
        this.writeTransport = writeTransport;
        this.readTransport = readTransport;
    }

    public IReadOnlyCollection<AutoBidEntry> Entries => entries.Values.ToList().AsReadOnly();
    public bool IsRunning => cts is not null && !cts.IsCancellationRequested;
    public event Action<AutoBidEntry>? EntryChanged;
    public event Action<string>? LogMessage;

    public void AddTransfer(int transferId, string pigeonName, decimal currentPrice, int? currentBuyerId, decimal maxPrice)
    {
        var entry = new AutoBidEntry
        {
            TransferId = transferId,
            PigeonName = pigeonName,
            CurrentPrice = currentPrice,
            CurrentBuyerId = currentBuyerId,
            MaxPrice = maxPrice,
            Status = AutoBidStatus.Watching,
            LastMessage = "Toegevoegd aan auto-bied",
        };

        if (entries.TryAdd(transferId, entry))
        {
            Log($"[+] {pigeonName} (#{transferId}) toegevoegd — max {maxPrice}");
            EntryChanged?.Invoke(entry);
        }
    }

    public void RemoveTransfer(int transferId)
    {
        if (entries.TryRemove(transferId, out var entry))
        {
            Log($"[-] {entry.PigeonName} (#{transferId}) verwijderd");
        }
    }

    public void UpdateMaxPrice(int transferId, decimal newMax)
    {
        if (entries.TryGetValue(transferId, out var entry))
        {
            entry.MaxPrice = newMax;
            entry.LastMessage = $"Max prijs bijgewerkt naar {newMax}";
            if (entry.Status == AutoBidStatus.MaxReached && newMax > entry.CurrentPrice)
            {
                entry.Status = AutoBidStatus.Watching;
            }

            EntryChanged?.Invoke(entry);
        }
    }

    public void Start(int fancierId)
    {
        if (IsRunning) return;

        this.fancierId = fancierId;
        cts = new CancellationTokenSource();
        var token = cts.Token;
        _ = Task.Run(() => PollLoopAsync(token), token);
        Log("Auto-bied gestart");
    }

    public void Stop()
    {
        if (cts is null) return;

        cts.Cancel();
        cts.Dispose();
        cts = null;

        foreach (var entry in entries.Values)
        {
            if (entry.Status is AutoBidStatus.Watching or AutoBidStatus.Winning or AutoBidStatus.Bidding)
            {
                entry.Status = AutoBidStatus.Stopped;
                entry.LastMessage = "Gestopt";
                EntryChanged?.Invoke(entry);
            }
        }

        Log("Auto-bied gestopt");
    }

    private async Task PollLoopAsync(CancellationToken token)
    {
        while (!token.IsCancellationRequested)
        {
            var delayMs = Random.Shared.Next(20_000, 45_001);
            try
            {
                await Task.Delay(delayMs, token);
            }
            catch (OperationCanceledException)
            {
                break;
            }

            if (entries.IsEmpty) continue;

            try
            {
                await ProcessTickAsync(token);
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (Exception ex)
            {
                Log($"Fout in poll-lus: {ex.Message}");
            }
        }
    }

    private async Task ProcessTickAsync(CancellationToken token)
    {
        var query = new Dictionary<string, string?> { ["processed"] = "false" };
        var response = await readTransport.GetJsonAsync("/api/transfer", query, token);

        if (!response.IsSuccessStatusCode)
        {
            if (response.StatusCode == 401)
            {
                Log("Sessie verlopen (401) — auto-bied gestopt");
                Stop();
                return;
            }

            Log($"Transfers ophalen mislukt (HTTP {response.StatusCode})");
            return;
        }

        var transfers = DeserializeTransfers(response.Body);
        var transferMap = transfers
            .Where(t => t.Id.HasValue)
            .ToDictionary(t => t.Id!.Value);

        foreach (var entry in entries.Values)
        {
            if (token.IsCancellationRequested) break;
            if (entry.Status is AutoBidStatus.Won or AutoBidStatus.Expired or AutoBidStatus.Stopped) continue;

            if (!transferMap.TryGetValue(entry.TransferId, out var transfer))
            {
                entry.Status = AutoBidStatus.Expired;
                entry.LastMessage = "Transfer niet meer actief";
                EntryChanged?.Invoke(entry);
                continue;
            }

            entry.CurrentPrice = transfer.Price ?? entry.CurrentPrice;
            entry.CurrentBuyerId = transfer.Buyer?.Id;
            entry.CurrentBuyerName = transfer.Buyer?.DisplayName;
            entry.EndTime = transfer.End;

            if (transfer.Buyer?.Id == fancierId)
            {
                entry.Status = AutoBidStatus.Winning;
                entry.LastMessage = "Hoogste bieder";
                EntryChanged?.Invoke(entry);
                continue;
            }

            var bidPrice = (int)Math.Ceiling((transfer.Price ?? 0m) * 1.10m);

            if (bidPrice > entry.MaxPrice)
            {
                entry.Status = AutoBidStatus.MaxReached;
                entry.LastMessage = $"Volgend bod ({bidPrice}) overschrijdt max ({entry.MaxPrice})";
                EntryChanged?.Invoke(entry);
                continue;
            }

            entry.Status = AutoBidStatus.Bidding;
            entry.LastMessage = $"Bieden: {bidPrice}…";
            EntryChanged?.Invoke(entry);

            await PlaceBidAsync(entry, bidPrice, token);

            var postBidDelay = Random.Shared.Next(3_000, 8_001);
            try
            {
                await Task.Delay(postBidDelay, token);
            }
            catch (OperationCanceledException)
            {
                break;
            }
        }
    }

    private async Task PlaceBidAsync(AutoBidEntry entry, int bidPrice, CancellationToken token)
    {
        var body = JsonSerializer.Serialize(new { transferId = entry.TransferId, price = bidPrice }, JsonOptions);
        var response = await writeTransport.PostJsonAsync("/api/transfer/bid", body, token);

        if (response.IsSuccessStatusCode)
        {
            try
            {
                var bidResult = JsonSerializer.Deserialize<TransferItemDto>(response.Body, JsonOptions);
                if (bidResult is not null)
                {
                    entry.CurrentPrice = bidResult.Price ?? bidPrice;
                    entry.CurrentBuyerId = bidResult.Buyer?.Id;
                    entry.CurrentBuyerName = bidResult.Buyer?.DisplayName;
                    entry.EndTime = bidResult.End;
                }
            }
            catch (JsonException)
            {
                entry.CurrentPrice = bidPrice;
            }

            entry.Status = entry.CurrentBuyerId == fancierId
                ? AutoBidStatus.Winning
                : AutoBidStatus.Watching;
            entry.LastMessage = $"Bod van {bidPrice} geplaatst";
            Log($"Bod: {entry.PigeonName} (#{entry.TransferId}) — {bidPrice}");
        }
        else if (response.StatusCode == 401)
        {
            Log("Sessie verlopen (401) — auto-bied gestopt");
            Stop();
        }
        else
        {
            entry.Status = AutoBidStatus.Error;
            entry.LastMessage = $"Bod mislukt (HTTP {response.StatusCode}): {Truncate(response.Body, 100)}";
            Log($"Bod mislukt voor {entry.PigeonName}: HTTP {response.StatusCode}");
        }

        EntryChanged?.Invoke(entry);
    }

    private void Log(string message)
    {
        LogMessage?.Invoke($"[{DateTime.Now:HH:mm:ss}] {message}");
    }

    private static string Truncate(string value, int maxLength) =>
        value.Length <= maxLength ? value : value[..maxLength] + "…";

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
}
