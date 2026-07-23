using Microsoft.EntityFrameworkCore;

namespace PigeonFancierTracker.Infrastructure.Persistence;

public sealed class AppDbContext(DbContextOptions<AppDbContext> options) : DbContext(options)
{
    public DbSet<RawApiSnapshotEntity> RawApiSnapshots => Set<RawApiSnapshotEntity>();
    public DbSet<SyncRunEntity> SyncRuns => Set<SyncRunEntity>();
    public DbSet<SyncRunItemEntity> SyncRunItems => Set<SyncRunItemEntity>();
    public DbSet<CompletedTransferEntity> CompletedTransfers => Set<CompletedTransferEntity>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<RawApiSnapshotEntity>(entity =>
        {
            entity.HasKey(x => x.Id);
            entity.Property(x => x.Endpoint).HasMaxLength(256).IsRequired();
            entity.Property(x => x.HttpMethod).HasMaxLength(10).IsRequired();
            entity.Property(x => x.BodySha256).HasMaxLength(64).IsRequired();
            entity.HasIndex(x => new { x.Endpoint, x.NormalizedQuery, x.SelectedFancierId, x.SourceSeasonId, x.BodySha256 });
        });

        modelBuilder.Entity<SyncRunEntity>(entity =>
        {
            entity.HasKey(x => x.Id);
            entity.Property(x => x.Profile).HasMaxLength(64).IsRequired();
            entity.Property(x => x.Status).HasMaxLength(32).IsRequired();
        });

        modelBuilder.Entity<SyncRunItemEntity>(entity =>
        {
            entity.HasKey(x => x.Id);
            entity.Property(x => x.Endpoint).HasMaxLength(256).IsRequired();
            entity.Property(x => x.Status).HasMaxLength(32).IsRequired();
            entity.HasIndex(x => new { x.SyncRunId, x.Endpoint, x.NormalizedQuery });
        });

        modelBuilder.Entity<CompletedTransferEntity>(entity =>
        {
            entity.HasKey(x => x.Id);
            entity.Property(x => x.Status).HasMaxLength(32).IsRequired();
            entity.HasIndex(x => new { x.SelectedFancierId, x.TransferId }).IsUnique();
        });
    }
}