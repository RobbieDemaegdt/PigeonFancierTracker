using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace PigeonFancierTracker.Infrastructure.Persistence;

/// <summary>
/// Lets the EF Core tooling (e.g. <c>dotnet ef migrations add</c>) construct an
/// <see cref="AppDbContext"/> at design time without the full application DI graph.
/// The connection string is a placeholder; it is only used for schema generation.
/// </summary>
public sealed class DesignTimeDbContextFactory : IDesignTimeDbContextFactory<AppDbContext>
{
    public AppDbContext CreateDbContext(string[] args)
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseSqlite("Data Source=design-time.db")
            .Options;

        return new AppDbContext(options);
    }
}
