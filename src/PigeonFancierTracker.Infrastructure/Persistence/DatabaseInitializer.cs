using Microsoft.EntityFrameworkCore;

namespace PigeonFancierTracker.Infrastructure.Persistence;

public sealed class DatabaseInitializer(IDbContextFactory<AppDbContext> contextFactory)
{
    public async Task InitializeAsync(CancellationToken cancellationToken = default)
    {
        await using var db = await contextFactory.CreateDbContextAsync(cancellationToken);
        await BaselineLegacyEnsureCreatedDatabaseAsync(db, cancellationToken);
        await db.Database.MigrateAsync(cancellationToken);
    }

    private static async Task BaselineLegacyEnsureCreatedDatabaseAsync(
        AppDbContext db,
        CancellationToken cancellationToken)
    {
        if (!await db.Database.CanConnectAsync(cancellationToken))
        {
            return;
        }

        var hasAppliedMigration = await HasAppliedMigrationAsync(db, cancellationToken);
        var hasLegacyTables = await HasTableAsync(db, "RawApiSnapshots", cancellationToken)
            && await HasTableAsync(db, "SyncRuns", cancellationToken);
        if (hasAppliedMigration || !hasLegacyTables)
        {
            return;
        }

        // The first development build used EnsureCreated before migrations were added.
        // Baseline that compatible schema rather than dropping the user's local snapshots.
        await db.Database.ExecuteSqlRawAsync(
            "CREATE TABLE IF NOT EXISTS \"__EFMigrationsHistory\" (\"MigrationId\" TEXT NOT NULL CONSTRAINT \"PK___EFMigrationsHistory\" PRIMARY KEY, \"ProductVersion\" TEXT NOT NULL);",
            cancellationToken);

        var firstMigration = db.Database.GetMigrations().FirstOrDefault();
        if (firstMigration is not null)
        {
            await db.Database.ExecuteSqlInterpolatedAsync(
                $"INSERT INTO \"__EFMigrationsHistory\" (\"MigrationId\", \"ProductVersion\") VALUES ({firstMigration}, {"10.0.10"});",
                cancellationToken);
        }
    }

    private static async Task<bool> HasTableAsync(
        AppDbContext db,
        string tableName,
        CancellationToken cancellationToken)
    {
        await using var command = db.Database.GetDbConnection().CreateCommand();
        command.CommandText = "SELECT COUNT(*) FROM sqlite_master WHERE type = 'table' AND name = $name";
        var parameter = command.CreateParameter();
        parameter.ParameterName = "$name";
        parameter.Value = tableName;
        command.Parameters.Add(parameter);
        await db.Database.OpenConnectionAsync(cancellationToken);
        return Convert.ToInt32(await command.ExecuteScalarAsync(cancellationToken)) > 0;
    }

    private static async Task<bool> HasAppliedMigrationAsync(
        AppDbContext db,
        CancellationToken cancellationToken)
    {
        if (!await HasTableAsync(db, "__EFMigrationsHistory", cancellationToken))
        {
            return false;
        }

        await using var command = db.Database.GetDbConnection().CreateCommand();
        command.CommandText = "SELECT COUNT(*) FROM \"__EFMigrationsHistory\"";
        return Convert.ToInt32(await command.ExecuteScalarAsync(cancellationToken)) > 0;
    }
}