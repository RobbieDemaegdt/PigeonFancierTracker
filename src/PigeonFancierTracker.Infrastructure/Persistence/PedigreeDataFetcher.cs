using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using PigeonFancierTracker.Core.Contracts;

namespace PigeonFancierTracker.Infrastructure.Persistence;

public sealed class PedigreeDataFetcher(
    IDbContextFactory<AppDbContext> contextFactory,
    IAuthenticatedReadTransport transport)
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
    };

    private static readonly TimeSpan CacheTtl = TimeSpan.FromDays(7);

    public async Task<IReadOnlyList<OffspringDto>> GetOffspringAsync(
        int pigeonId, CancellationToken ct = default)
    {
        await using var db = await contextFactory.CreateDbContextAsync(ct);

        var cached = await db.OffspringCache
            .AsNoTracking()
            .FirstOrDefaultAsync(x => x.PigeonId == pigeonId, ct);

        if (cached is not null && DateTimeOffset.UtcNow - cached.FetchedAtUtc < CacheTtl)
        {
            return DeserializeOffspring(cached.OffspringJson);
        }

        try
        {
            var response = await transport.GetJsonAsync($"/api/pigeon/{pigeonId}/offspring", null, ct);
            if (response.IsSuccessStatusCode && response.Body is not null)
            {
                await UpsertOffspringCache(db, pigeonId, response.Body, ct);
                return DeserializeOffspring(response.Body);
            }
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch
        {
            // Fall back to stale cache on network/auth failure
        }

        return cached is not null ? DeserializeOffspring(cached.OffspringJson) : [];
    }

    public async Task<PedigreeNodeDto?> GetPedigreeAsync(
        int pigeonId, CancellationToken ct = default)
    {
        await using var db = await contextFactory.CreateDbContextAsync(ct);

        var cached = await db.PedigreeCache
            .AsNoTracking()
            .FirstOrDefaultAsync(x => x.PigeonId == pigeonId, ct);

        if (cached is not null && DateTimeOffset.UtcNow - cached.FetchedAtUtc < CacheTtl)
        {
            return DeserializePedigree(cached.PedigreeJson);
        }

        try
        {
            var response = await transport.GetJsonAsync($"/api/pigeon/{pigeonId}/pedigree", null, ct);
            if (response.IsSuccessStatusCode && response.Body is not null)
            {
                await UpsertPedigreeCache(db, pigeonId, response.Body, ct);
                return DeserializePedigree(response.Body);
            }
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch
        {
            // Fall back to stale cache on network/auth failure
        }

        return cached is not null ? DeserializePedigree(cached.PedigreeJson) : null;
    }

    private static async Task UpsertOffspringCache(
        AppDbContext db, int pigeonId, string json, CancellationToken ct)
    {
        var existing = await db.OffspringCache
            .FirstOrDefaultAsync(x => x.PigeonId == pigeonId, ct);

        if (existing is not null)
        {
            existing.OffspringJson = json;
            existing.FetchedAtUtc = DateTimeOffset.UtcNow;
        }
        else
        {
            db.OffspringCache.Add(new OffspringCacheEntity
            {
                PigeonId = pigeonId,
                OffspringJson = json,
                FetchedAtUtc = DateTimeOffset.UtcNow,
            });
        }

        await db.SaveChangesAsync(ct);
    }

    private static async Task UpsertPedigreeCache(
        AppDbContext db, int pigeonId, string json, CancellationToken ct)
    {
        var existing = await db.PedigreeCache
            .FirstOrDefaultAsync(x => x.PigeonId == pigeonId, ct);

        if (existing is not null)
        {
            existing.PedigreeJson = json;
            existing.FetchedAtUtc = DateTimeOffset.UtcNow;
        }
        else
        {
            db.PedigreeCache.Add(new PedigreeCacheEntity
            {
                PigeonId = pigeonId,
                PedigreeJson = json,
                FetchedAtUtc = DateTimeOffset.UtcNow,
            });
        }

        await db.SaveChangesAsync(ct);
    }

    private static IReadOnlyList<OffspringDto> DeserializeOffspring(string json)
    {
        try
        {
            return JsonSerializer.Deserialize<List<OffspringDto>>(json, JsonOptions) ?? [];
        }
        catch
        {
            return [];
        }
    }

    private static PedigreeNodeDto? DeserializePedigree(string json)
    {
        try
        {
            return JsonSerializer.Deserialize<PedigreeNodeDto>(json, JsonOptions);
        }
        catch
        {
            return null;
        }
    }
}
