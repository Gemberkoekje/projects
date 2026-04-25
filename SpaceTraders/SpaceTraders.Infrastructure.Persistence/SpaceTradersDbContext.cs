using Microsoft.EntityFrameworkCore;
using SpaceTraders.Infrastructure.Persistence.Entities;

namespace SpaceTraders.Infrastructure.Persistence;

public sealed class SpaceTradersDbContext(DbContextOptions<SpaceTradersDbContext> options) : DbContext(options)
{
    public DbSet<StoredCredential> Credentials => Set<StoredCredential>();

    public DbSet<CachedAgent> Agents => Set<CachedAgent>();

    public DbSet<CachedShip> Ships => Set<CachedShip>();

    public DbSet<CachedContract> Contracts => Set<CachedContract>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<StoredCredential>(entity =>
        {
            entity.ToTable("stored_credentials");
            entity.HasKey(x => x.Key);
            entity.Property(x => x.Key).HasMaxLength(100);
            entity.Property(x => x.Value).IsRequired();
        });

        modelBuilder.Entity<CachedAgent>(entity =>
        {
            entity.ToTable("cached_agents");
            entity.HasKey(x => x.Symbol);
            entity.Property(x => x.Symbol).HasMaxLength(100);
            entity.Property(x => x.StartingFaction).HasMaxLength(100);
        });

        modelBuilder.Entity<CachedShip>(entity =>
        {
            entity.ToTable("cached_ships");
            entity.HasKey(x => x.Symbol);
            entity.Property(x => x.Symbol).HasMaxLength(100);
            entity.Property(x => x.SystemSymbol).HasMaxLength(100);
            entity.Property(x => x.WaypointSymbol).HasMaxLength(100);
            entity.Property(x => x.Status).HasMaxLength(50);
            entity.Property(x => x.FlightMode).HasMaxLength(50);
        });

        modelBuilder.Entity<CachedContract>(entity =>
        {
            entity.ToTable("cached_contracts");
            entity.HasKey(x => x.Id);
            entity.Property(x => x.Id).HasMaxLength(100);
            entity.Property(x => x.FactionSymbol).HasMaxLength(100);
            entity.Property(x => x.Type).HasMaxLength(100);
        });
    }
}
