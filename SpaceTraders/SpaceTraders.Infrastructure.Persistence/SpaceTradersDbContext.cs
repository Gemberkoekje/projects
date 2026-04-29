using Microsoft.EntityFrameworkCore;
using SpaceTraders.Infrastructure.Persistence.Entities;
using SpaceTraders.Infrastructure.Persistence.Scoping;

namespace SpaceTraders.Infrastructure.Persistence;

[Mutable]
public sealed class SpaceTradersDbContext(
    DbContextOptions<SpaceTradersDbContext> options,
    IAgentDataScope agentDataScope) : DbContext(options)
{
    public string AgentToken { get; } = agentDataScope.Token;

    public DbSet<StoredCredential> Credentials => Set<StoredCredential>();

    public DbSet<CachedAgent> Agents => Set<CachedAgent>();

    public DbSet<CachedShip> Ships => Set<CachedShip>();

    public DbSet<CachedContract> Contracts => Set<CachedContract>();

    public DbSet<CachedMarket> Markets => Set<CachedMarket>();

    public DbSet<CachedShipyard> Shipyards => Set<CachedShipyard>();

    public DbSet<CachedWaypoint> Waypoints => Set<CachedWaypoint>();

    public DbSet<CachedSystem> Systems => Set<CachedSystem>();

    public DbSet<AgentSetting> Settings => Set<AgentSetting>();

    public DbSet<ShipAssignmentRecord> ShipAssignments => Set<ShipAssignmentRecord>();

    public DbSet<TradeOpportunity> TradeOpportunities => Set<TradeOpportunity>();

    public DbSet<ActivityLog> ActivityLogs => Set<ActivityLog>();

    public DbSet<LeaderLease> LeaderLeases => Set<LeaderLease>();

    public DbSet<ApiEndpointUsage> ApiEndpointUsages => Set<ApiEndpointUsage>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<StoredCredential>(entity =>
        {
            entity.ToTable("stored_credentials");
            entity.HasKey(x => new { x.AgentToken, x.Key });
            entity.Property(x => x.AgentToken).HasMaxLength(512);
            entity.Property(x => x.Key).HasMaxLength(100);
            entity.Property(x => x.Value).IsRequired();
            entity.HasQueryFilter(x => x.AgentToken == AgentToken);
        });

        modelBuilder.Entity<CachedAgent>(entity =>
        {
            entity.ToTable("cached_agents");
            entity.HasKey(x => new { x.AgentToken, x.Symbol });
            entity.Property(x => x.AgentToken).HasMaxLength(512);
            entity.Property(x => x.Symbol).HasMaxLength(100);
            entity.Property(x => x.StartingFaction).HasMaxLength(100);
            entity.HasQueryFilter(x => x.AgentToken == AgentToken);
        });

        modelBuilder.Entity<CachedShip>(entity =>
        {
            entity.ToTable("cached_ships");
            entity.HasKey(x => new { x.AgentToken, x.Symbol });
            entity.Property(x => x.AgentToken).HasMaxLength(512);
            entity.Property(x => x.Symbol).HasMaxLength(100);
            entity.Property(x => x.SystemSymbol).HasMaxLength(100);
            entity.Property(x => x.WaypointSymbol).HasMaxLength(100);
            entity.Property(x => x.DestWaypointSymbol).HasMaxLength(100);
            entity.Property(x => x.Status).HasMaxLength(50);
            entity.Property(x => x.FlightMode).HasMaxLength(50);
            entity.Property(x => x.ShipType).HasMaxLength(100).IsRequired();
            entity.HasQueryFilter(x => x.AgentToken == AgentToken);
        });

        modelBuilder.Entity<CachedContract>(entity =>
        {
            entity.ToTable("cached_contracts");
            entity.HasKey(x => new { x.AgentToken, x.Id });
            entity.Property(x => x.AgentToken).HasMaxLength(512);
            entity.Property(x => x.Id).HasMaxLength(100);
            entity.Property(x => x.FactionSymbol).HasMaxLength(100);
            entity.Property(x => x.Type).HasMaxLength(100);
            entity.Property(x => x.DeliverablesJson);
            entity.HasQueryFilter(x => x.AgentToken == AgentToken);
        });

        modelBuilder.Entity<CachedMarket>(entity =>
        {
            entity.ToTable("cached_markets");
            entity.HasKey(x => new { x.AgentToken, x.WaypointSymbol });
            entity.Property(x => x.AgentToken).HasMaxLength(512);
            entity.Property(x => x.WaypointSymbol).HasMaxLength(100);
            entity.Property(x => x.SystemSymbol).HasMaxLength(100).IsRequired();
            entity.HasQueryFilter(x => x.AgentToken == AgentToken);
        });

        modelBuilder.Entity<CachedShipyard>(entity =>
        {
            entity.ToTable("cached_shipyards");
            entity.HasKey(x => new { x.AgentToken, x.WaypointSymbol });
            entity.Property(x => x.AgentToken).HasMaxLength(512);
            entity.Property(x => x.WaypointSymbol).HasMaxLength(100);
            entity.Property(x => x.SystemSymbol).HasMaxLength(100).IsRequired();
            entity.HasQueryFilter(x => x.AgentToken == AgentToken);
        });

        modelBuilder.Entity<CachedWaypoint>(entity =>
        {
            entity.ToTable("cached_waypoints");
            entity.HasKey(x => new { x.AgentToken, x.Symbol });
            entity.Property(x => x.AgentToken).HasMaxLength(512);
            entity.Property(x => x.Symbol).HasMaxLength(100);
            entity.Property(x => x.SystemSymbol).HasMaxLength(100).IsRequired();
            entity.Property(x => x.Type).HasMaxLength(100).IsRequired();
            entity.HasIndex(x => new { x.AgentToken, x.SystemSymbol });
            entity.HasQueryFilter(x => x.AgentToken == AgentToken);
        });

        modelBuilder.Entity<CachedSystem>(entity =>
        {
            entity.ToTable("cached_systems");
            entity.HasKey(x => new { x.AgentToken, x.Symbol });
            entity.Property(x => x.AgentToken).HasMaxLength(512);
            entity.Property(x => x.Symbol).HasMaxLength(100);
            entity.Property(x => x.SectorSymbol).HasMaxLength(100).IsRequired();
            entity.Property(x => x.Type).HasMaxLength(100).IsRequired();
            entity.HasQueryFilter(x => x.AgentToken == AgentToken);
        });

        modelBuilder.Entity<AgentSetting>(entity =>
        {
            entity.ToTable("agent_settings");
            entity.HasKey(x => new { x.AgentToken, x.Key });
            entity.Property(x => x.AgentToken).HasMaxLength(512);
            entity.Property(x => x.Key).HasMaxLength(200);
            entity.Property(x => x.Value).IsRequired();
            entity.Property(x => x.Type).HasMaxLength(50).IsRequired();
            entity.Property(x => x.Description).IsRequired();
            entity.HasQueryFilter(x => x.AgentToken == AgentToken);
        });

        modelBuilder.Entity<ShipAssignmentRecord>(entity =>
        {
            entity.ToTable("ship_assignment_records");
            entity.HasKey(x => new { x.AgentToken, x.ShipSymbol });
            entity.Property(x => x.AgentToken).HasMaxLength(512);
            entity.Property(x => x.ShipSymbol).HasMaxLength(100);
            entity.Property(x => x.Type).HasMaxLength(100).IsRequired();
            entity.Property(x => x.OriginWaypoint).HasMaxLength(100);
            entity.Property(x => x.DestWaypoint).HasMaxLength(100);
            entity.Property(x => x.CargoSymbol).HasMaxLength(100);
            entity.Property(x => x.ContractId).HasMaxLength(100);
            entity.Property(x => x.RequiredUnits);
            entity.HasQueryFilter(x => x.AgentToken == AgentToken);
        });

        modelBuilder.Entity<TradeOpportunity>(entity =>
        {
            entity.ToTable("trade_opportunities");
            entity.HasKey(x => x.Id);
            entity.Property(x => x.AgentToken).HasMaxLength(512).IsRequired();
            entity.Property(x => x.TradeSymbol).HasMaxLength(100).IsRequired();
            entity.Property(x => x.BuyWaypoint).HasMaxLength(100).IsRequired();
            entity.Property(x => x.SellWaypoint).HasMaxLength(100).IsRequired();
            entity.Property(x => x.BuyType).HasMaxLength(30).IsRequired();
            entity.Property(x => x.SellType).HasMaxLength(30).IsRequired();
            entity.Property(x => x.ProfitPerJump).HasColumnType("numeric(18,4)");
            entity.Property(x => x.EstimatedFuelCost).HasColumnType("numeric(18,4)");
            entity.Property(x => x.EstimatedTravelTimeMinutes).HasColumnType("numeric(18,4)");
            entity.Property(x => x.OpportunityCostPenalty).HasColumnType("numeric(18,4)");
            entity.Property(x => x.CooldownPenalty).HasColumnType("numeric(18,4)");
            entity.Property(x => x.RateLimitPenalty).HasColumnType("numeric(18,4)");
            entity.Property(x => x.RouteScore).HasColumnType("numeric(18,4)");
            entity.HasIndex(x => new { x.AgentToken, x.SupportsSupplyChain, x.SupplyChainDepth, x.ProfitPerJump, x.ComputedAt });
            entity.HasIndex(x => new { x.AgentToken, x.RouteScore, x.ComputedAt });
            entity.HasQueryFilter(x => x.AgentToken == AgentToken);
        });

        modelBuilder.Entity<ActivityLog>(entity =>
        {
            entity.ToTable("activity_logs");
            entity.HasKey(x => x.Id);
            entity.Property(x => x.AgentToken).HasMaxLength(512).IsRequired();
            entity.Property(x => x.ShipSymbol).HasMaxLength(100).IsRequired();
            entity.Property(x => x.EventType).HasMaxLength(200).IsRequired();
            entity.Property(x => x.Message).IsRequired();
            entity.HasIndex(x => new { x.AgentToken, x.Timestamp, x.ShipSymbol });
            entity.HasQueryFilter(x => x.AgentToken == AgentToken);
        });

        modelBuilder.Entity<LeaderLease>(entity =>
        {
            entity.ToTable("leader_leases");
            entity.HasKey(x => new { x.AgentToken, x.Key });
            entity.Property(x => x.AgentToken).HasMaxLength(512);
            entity.Property(x => x.Key).HasMaxLength(100);
            entity.Property(x => x.HolderId).HasMaxLength(200).IsRequired();
            entity.HasQueryFilter(x => x.AgentToken == AgentToken);
        });

        modelBuilder.Entity<ApiEndpointUsage>(entity =>
        {
            entity.ToTable("api_endpoint_usages");
            entity.HasKey(x => x.Id);
            entity.Property(x => x.AgentToken).HasMaxLength(512).IsRequired();
            entity.Property(x => x.HttpMethod).HasMaxLength(10).IsRequired();
            entity.Property(x => x.Endpoint).HasMaxLength(1000).IsRequired();
            entity.HasIndex(x => new { x.AgentToken, x.HttpMethod, x.Endpoint }).IsUnique();
            entity.HasQueryFilter(x => x.AgentToken == AgentToken);
        });
    }
}
