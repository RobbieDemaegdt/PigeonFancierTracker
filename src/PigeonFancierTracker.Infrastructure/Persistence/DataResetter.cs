using Microsoft.EntityFrameworkCore;
using PigeonFancierTracker.Core.Contracts;

namespace PigeonFancierTracker.Infrastructure.Persistence;

public sealed class DataResetter(
    IDbContextFactory<AppDbContext> contextFactory,
    ICredentialStore credentialStore) : IDataResetter
{
    public async Task ResetAllAsync(CancellationToken cancellationToken = default)
    {
        await using var db = await contextFactory.CreateDbContextAsync(cancellationToken);
        await db.Database.ExecuteSqlRawAsync("DELETE FROM \"CompletedTransfers\"", cancellationToken);
        await db.Database.ExecuteSqlRawAsync("DELETE FROM \"SyncRunItems\"", cancellationToken);
        await db.Database.ExecuteSqlRawAsync("DELETE FROM \"SyncRuns\"", cancellationToken);
        await db.Database.ExecuteSqlRawAsync("DELETE FROM \"RawApiSnapshots\"", cancellationToken);
        await credentialStore.ClearAsync();
    }
}
