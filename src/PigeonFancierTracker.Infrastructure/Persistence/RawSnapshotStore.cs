using System.Security.Cryptography;
using System.Text;
using Microsoft.EntityFrameworkCore;
using PigeonFancierTracker.Core.Contracts;

namespace PigeonFancierTracker.Infrastructure.Persistence;

public sealed class RawSnapshotStore(IDbContextFactory<AppDbContext> contextFactory)
{
    public async Task<long> SaveAsync(
        string endpoint,
        IReadOnlyDictionary<string, string?>? query,
        TransportResponse response,
        int? selectedFancierId = null,
        int? sourceSeasonId = null,
        string? errorDetails = null,
        CancellationToken cancellationToken = default)
    {
        var normalizedQuery = NormalizeQuery(query);
        var bodyHash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(response.Body))).ToLowerInvariant();
        await using var db = await contextFactory.CreateDbContextAsync(cancellationToken);

        var snapshot = new RawApiSnapshotEntity
        {
            Endpoint = endpoint,
            NormalizedQuery = normalizedQuery,
            HttpMethod = "GET",
            StatusCode = response.StatusCode,
            CapturedAtUtc = DateTimeOffset.UtcNow,
            ContentType = response.ContentType,
            ResponseBodyJson = response.Body,
            BodySha256 = bodyHash,
            SelectedFancierId = selectedFancierId,
            SourceSeasonId = sourceSeasonId,
            ErrorDetails = errorDetails,
        };

        db.RawApiSnapshots.Add(snapshot);
        await db.SaveChangesAsync(cancellationToken);
        return snapshot.Id;
    }

    public static string NormalizeQuery(IReadOnlyDictionary<string, string?>? query)
    {
        if (query is null || query.Count == 0)
        {
            return string.Empty;
        }

        return string.Join('&', query
            .Where(x => !string.IsNullOrWhiteSpace(x.Key) && x.Value is not null)
            .OrderBy(x => x.Key, StringComparer.Ordinal)
            .ThenBy(x => x.Value, StringComparer.Ordinal)
            .Select(x => $"{Uri.EscapeDataString(x.Key)}={Uri.EscapeDataString(x.Value!)}"));
    }
}