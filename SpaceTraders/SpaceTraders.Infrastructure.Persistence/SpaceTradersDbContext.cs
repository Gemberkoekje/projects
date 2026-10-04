using Microsoft.EntityFrameworkCore;
using SpaceTraders.Domain.Enums;
using SpaceTraders.Infrastructure.Persistence.Entities;
using SpaceTraders.Infrastructure.Persistence.Scoping;

namespace SpaceTraders.Infrastructure.Persistence;

[Mutable]
public sealed class SpaceTradersDbContext(
    DbContextOptions<SpaceTradersDbContext> options,
    IAgentDataScope agentDataScope) : DbContext(options)
{
    /// <summary>The <see cref="AgentIdentity"/> whose rows this context reads and writes.</summary>
    public string AgentId { get; } = agentDataScope.AgentId;

    public DbSet<StoredCredential> Credentials => Set<StoredCredential>();

    public DbSet<CachedAgent> Agents => Set<CachedAgent>();

    public DbSet<CachedShip> Ships => Set<CachedShip>();

    public DbSet<CachedContract> Contracts => Set<CachedContract>();

    public DbSet<CachedMarket> Markets => Set<CachedMarket>();

    public DbSet<CachedShipyard> Shipyards => Set<CachedShipyard>();

    public DbSet<CachedWaypoint> Waypoints => Set<CachedWaypoint>();

    public DbSet<CachedSystem> Systems => Set<CachedSystem>();

    public DbSet<AgentSetting> Settings => Set<AgentSetting>();

    public DbSet<NextRunSetting> NextRunSettings => Set<NextRunSetting>();

    public DbSet<ShipAssignmentRecord> ShipAssignments => Set<ShipAssignmentRecord>();

    public DbSet<TradeOpportunity> TradeOpportunities => Set<TradeOpportunity>();

    public DbSet<ActivityLog> ActivityLogs => Set<ActivityLog>();

    public DbSet<LeaderLease> LeaderLeases => Set<LeaderLease>();

    public DbSet<ApiEndpointUsage> ApiEndpointUsages => Set<ApiEndpointUsage>();

    public DbSet<CachedSurvey> Surveys => Set<CachedSurvey>();

    public DbSet<CachedConstruction> ConstructionSites => Set<CachedConstruction>();

    public DbSet<StartupSnapshot> StartupSnapshots => Set<StartupSnapshot>();

    public DbSet<Run> Runs => Set<Run>();

    public DbSet<ScheduledRun> ScheduledRuns => Set<ScheduledRun>();

    public DbSet<LedgerEntry> LedgerEntries => Set<LedgerEntry>();

    public DbSet<RunCreditHighlight> RunCreditHighlights => Set<RunCreditHighlight>();

    public DbSet<AgentCreditsSample> AgentCreditsSamples => Set<AgentCreditsSample>();

    public DbSet<MarketPriceSample> MarketPriceSamples => Set<MarketPriceSample>();

    public DbSet<ShipTaskRecord> ShipTaskRecords => Set<ShipTaskRecord>();

    public DbSet<FleetGoalRecord> FleetGoals => Set<FleetGoalRecord>();

    public DbSet<PlanStateRecord> PlanStates => Set<PlanStateRecord>();

    public DbSet<ScheduledShipEvent> ScheduledShipEvents => Set<ScheduledShipEvent>();

    public DbSet<ShipGoalHistoryRecord> ShipGoalHistory => Set<ShipGoalHistoryRecord>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<StoredCredential>(entity =>
        {
            entity.ToTable("stored_credentials");
            entity.HasKey(x => new { x.AgentId, x.Key });
            entity.Property(x => x.AgentId).HasMaxLength(AgentIdentity.MaxLength);
            entity.Property(x => x.Key).HasMaxLength(100);
            entity.Property(x => x.Value).HasMaxLength(1024).IsRequired();
            entity.HasQueryFilter(x => x.AgentId == AgentId);
        });

        modelBuilder.Entity<CachedAgent>(entity =>
        {
            entity.ToTable("cached_agents");
            entity.HasKey(x => new { x.AgentId, x.Symbol });
            entity.Property(x => x.AgentId).HasMaxLength(AgentIdentity.MaxLength);
            entity.Property(x => x.Symbol).HasMaxLength(100);
            entity.Property(x => x.StartingFaction).HasMaxLength(100);
            entity.HasQueryFilter(x => x.AgentId == AgentId);
        });

        modelBuilder.Entity<CachedShip>(entity =>
        {
            entity.ToTable("cached_ships");

            // A few wide rows (about 2 kB of ship JSON each), each updated every minute or so. Room on
            // every page keeps those updates in place, and the low threshold lets autovacuum run on a
            // table this small. Without them it never did, and the table grew by every update that
            // didn't fit its page: about 250 kB an hour with one busy ship in the soak test (B32).
            entity.HasStorageParameter("fillfactor", 50);
            entity.HasStorageParameter("autovacuum_vacuum_threshold", 10);

            entity.HasKey(x => new { x.AgentId, x.Symbol });
            entity.Property(x => x.AgentId).HasMaxLength(AgentIdentity.MaxLength);
            entity.Property(x => x.Symbol).HasMaxLength(100);
            entity.Property(x => x.SystemSymbol).HasMaxLength(100);
            entity.Property(x => x.WaypointSymbol).HasMaxLength(100);
            entity.Property(x => x.DestWaypointSymbol).HasMaxLength(100);
            entity.Property(x => x.Status).HasMaxLength(50);
            entity.Property(x => x.FlightMode).HasMaxLength(50);
            entity.Property(x => x.LocalStatus).HasDefaultValue(ShipLocalStatus.None);
            entity.Property(x => x.ShipType).HasMaxLength(100).IsRequired();
            entity.Property(x => x.GoalId);
            entity.Property(x => x.GoalKind).HasMaxLength(100);
            entity.Property(x => x.GoalPayloadJson);
            entity.Property(x => x.GoalStatus);
            entity.HasQueryFilter(x => x.AgentId == AgentId);
        });

        modelBuilder.Entity<CachedContract>(entity =>
        {
            entity.ToTable("cached_contracts");
            entity.HasKey(x => new { x.AgentId, x.Id });
            entity.Property(x => x.AgentId).HasMaxLength(AgentIdentity.MaxLength);
            entity.Property(x => x.Id).HasMaxLength(100);
            entity.Property(x => x.FactionSymbol).HasMaxLength(100);
            entity.Property(x => x.Type).HasMaxLength(100);
            entity.Property(x => x.DeliverablesJson);
            entity.HasQueryFilter(x => x.AgentId == AgentId);
        });

        modelBuilder.Entity<CachedMarket>(entity =>
        {
            entity.ToTable("cached_markets");
            entity.HasKey(x => new { x.AgentId, x.WaypointSymbol });
            entity.Property(x => x.AgentId).HasMaxLength(AgentIdentity.MaxLength);
            entity.Property(x => x.WaypointSymbol).HasMaxLength(100);
            entity.Property(x => x.SystemSymbol).HasMaxLength(100).IsRequired();
            entity.HasQueryFilter(x => x.AgentId == AgentId);
        });

        modelBuilder.Entity<CachedShipyard>(entity =>
        {
            entity.ToTable("cached_shipyards");
            entity.HasKey(x => new { x.AgentId, x.WaypointSymbol });
            entity.Property(x => x.AgentId).HasMaxLength(AgentIdentity.MaxLength);
            entity.Property(x => x.WaypointSymbol).HasMaxLength(100);
            entity.Property(x => x.SystemSymbol).HasMaxLength(100).IsRequired();
            entity.HasQueryFilter(x => x.AgentId == AgentId);
        });

        modelBuilder.Entity<CachedWaypoint>(entity =>
        {
            entity.ToTable("cached_waypoints");
            entity.HasKey(x => new { x.AgentId, x.Symbol });
            entity.Property(x => x.AgentId).HasMaxLength(AgentIdentity.MaxLength);
            entity.Property(x => x.Symbol).HasMaxLength(100);
            entity.Property(x => x.SystemSymbol).HasMaxLength(100).IsRequired();
            entity.Property(x => x.Type).HasMaxLength(100).IsRequired();
            entity.HasIndex(x => new { x.AgentId, x.SystemSymbol });
            entity.HasQueryFilter(x => x.AgentId == AgentId);
        });

        modelBuilder.Entity<CachedSystem>(entity =>
        {
            entity.ToTable("cached_systems");
            entity.HasKey(x => new { x.AgentId, x.Symbol });
            entity.Property(x => x.AgentId).HasMaxLength(AgentIdentity.MaxLength);
            entity.Property(x => x.Symbol).HasMaxLength(100);
            entity.Property(x => x.SectorSymbol).HasMaxLength(100).IsRequired();
            entity.Property(x => x.Type).HasMaxLength(100).IsRequired();
            entity.HasQueryFilter(x => x.AgentId == AgentId);
        });

        modelBuilder.Entity<AgentSetting>(entity =>
        {
            entity.ToTable("agent_settings");
            entity.HasKey(x => new { x.AgentId, x.Key });
            entity.Property(x => x.AgentId).HasMaxLength(AgentIdentity.MaxLength);
            entity.Property(x => x.Key).HasMaxLength(200);
            entity.Property(x => x.Value).IsRequired();
            entity.Property(x => x.Type).HasMaxLength(50).IsRequired();
            entity.Property(x => x.Description).IsRequired();
            entity.HasQueryFilter(x => x.AgentId == AgentId);
        });

        // No agent: the values chosen for the next runs outlive the agent that runs now (D69).
        modelBuilder.Entity<NextRunSetting>(entity =>
        {
            entity.ToTable("next_run_settings");
            entity.HasKey(x => x.Key);
            entity.Property(x => x.Key).HasMaxLength(200);
            entity.Property(x => x.Value).IsRequired();
        });

        modelBuilder.Entity<ShipAssignmentRecord>(entity =>
        {
            entity.ToTable("ship_assignment_records");
            entity.HasKey(x => new { x.AgentId, x.ShipSymbol });
            entity.Property(x => x.AgentId).HasMaxLength(AgentIdentity.MaxLength);
            entity.Property(x => x.ShipSymbol).HasMaxLength(100);
            entity.Property(x => x.Type).HasMaxLength(100).IsRequired();
            entity.Property(x => x.OriginWaypoint).HasMaxLength(100);
            entity.Property(x => x.DestWaypoint).HasMaxLength(100);
            entity.Property(x => x.CargoSymbol).HasMaxLength(100);
            entity.Property(x => x.ContractId).HasMaxLength(100);
            entity.Property(x => x.RequiredUnits);
            entity.HasQueryFilter(x => x.AgentId == AgentId);
        });

        modelBuilder.Entity<TradeOpportunity>(entity =>
        {
            entity.ToTable("trade_opportunities");
            entity.HasKey(x => x.Id);
            entity.Property(x => x.AgentId).HasMaxLength(AgentIdentity.MaxLength).IsRequired();
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
            entity.HasIndex(x => new { x.AgentId, x.SupportsSupplyChain, x.SupplyChainDepth, x.ProfitPerJump, x.ComputedAt });
            entity.HasIndex(x => new { x.AgentId, x.RouteScore, x.ComputedAt });
            entity.HasQueryFilter(x => x.AgentId == AgentId);
        });

        modelBuilder.Entity<ActivityLog>(entity =>
        {
            entity.ToTable("activity_logs");
            entity.HasKey(x => x.Id);
            entity.Property(x => x.AgentId).HasMaxLength(AgentIdentity.MaxLength).IsRequired();
            entity.Property(x => x.ShipSymbol).HasMaxLength(100).IsRequired();
            entity.Property(x => x.EventType).HasMaxLength(200).IsRequired();
            entity.Property(x => x.Message).IsRequired();
            entity.HasIndex(x => new { x.AgentId, x.Timestamp, x.ShipSymbol });
            entity.HasQueryFilter(x => x.AgentId == AgentId);
        });

        modelBuilder.Entity<LeaderLease>(entity =>
        {
            entity.ToTable("leader_leases");
            entity.HasKey(x => new { x.AgentId, x.Key });
            entity.Property(x => x.AgentId).HasMaxLength(AgentIdentity.MaxLength);
            entity.Property(x => x.Key).HasMaxLength(100);
            entity.Property(x => x.HolderId).HasMaxLength(200).IsRequired();
            entity.HasQueryFilter(x => x.AgentId == AgentId);
        });

        modelBuilder.Entity<ApiEndpointUsage>(entity =>
        {
            entity.ToTable("api_endpoint_usages");
            entity.HasKey(x => x.Id);
            entity.Property(x => x.AgentId).HasMaxLength(AgentIdentity.MaxLength).IsRequired();
            entity.Property(x => x.HttpMethod).HasMaxLength(10).IsRequired();
            entity.Property(x => x.Endpoint).HasMaxLength(1000).IsRequired();
            entity.HasIndex(x => new { x.AgentId, x.HttpMethod, x.Endpoint }).IsUnique();
            entity.HasQueryFilter(x => x.AgentId == AgentId);
        });

        modelBuilder.Entity<CachedSurvey>(entity =>
        {
            entity.ToTable("cached_surveys");
            entity.HasKey(x => new { x.AgentId, x.Signature });
            entity.Property(x => x.AgentId).HasMaxLength(AgentIdentity.MaxLength);
            entity.Property(x => x.Signature).HasMaxLength(200);
            entity.Property(x => x.ShipSymbol).HasMaxLength(100).IsRequired();
            entity.Property(x => x.WaypointSymbol).HasMaxLength(100).IsRequired();
            entity.Property(x => x.Size).HasMaxLength(30).IsRequired();
            entity.HasIndex(x => new { x.AgentId, x.WaypointSymbol, x.Expiration });
            entity.HasQueryFilter(x => x.AgentId == AgentId);
        });

        modelBuilder.Entity<CachedConstruction>(entity =>
        {
            entity.ToTable("cached_construction_sites");
            entity.HasKey(x => new { x.AgentId, x.WaypointSymbol });
            entity.Property(x => x.AgentId).HasMaxLength(AgentIdentity.MaxLength);
            entity.Property(x => x.WaypointSymbol).HasMaxLength(100);
            entity.Property(x => x.SystemSymbol).HasMaxLength(100).IsRequired();
            entity.HasIndex(x => new { x.AgentId, x.IsComplete });
            entity.HasQueryFilter(x => x.AgentId == AgentId);
        });

        modelBuilder.Entity<StartupSnapshot>(entity =>
        {
            entity.ToTable("startup_snapshots");
            entity.HasKey(x => x.Id);
            entity.Property(x => x.Id).UseIdentityColumn();
            entity.Property(x => x.AgentId).HasMaxLength(AgentIdentity.MaxLength).IsRequired();
            entity.Property(x => x.SnapshotJson).IsRequired();
            entity.HasIndex(x => new { x.AgentId, x.CapturedAt });
        });

        modelBuilder.Entity<Run>(entity =>
        {
            entity.ToTable("runs");
            entity.HasKey(x => x.Id);
            entity.Property(x => x.Id).ValueGeneratedNever();
            entity.Property(x => x.AgentId).HasMaxLength(AgentIdentity.MaxLength).IsRequired();
            entity.Property(x => x.Name).HasMaxLength(200).IsRequired();
            entity.Property(x => x.StrategyLabel).HasMaxLength(200).IsRequired();
            entity.Property(x => x.SettingsSnapshotJson);
            entity.HasIndex(x => new { x.AgentId, x.StartedAt });
            entity.HasQueryFilter(x => x.AgentId == AgentId);
        });

        modelBuilder.Entity<ScheduledRun>(entity =>
        {
            entity.ToTable("scheduled_runs");
            entity.HasKey(x => x.Id);
            entity.Property(x => x.Id).ValueGeneratedNever();
            entity.Property(x => x.AgentId).HasMaxLength(AgentIdentity.MaxLength).IsRequired();
            entity.Property(x => x.Name).HasMaxLength(200).IsRequired();
            entity.Property(x => x.StrategyLabel).HasMaxLength(200).IsRequired();
            entity.Property(x => x.ScheduledSettingsJson);
            entity.HasIndex(x => new { x.AgentId, x.ActivatesAt });
            entity.HasQueryFilter(x => x.AgentId == AgentId);
        });

        modelBuilder.Entity<LedgerEntry>(entity =>
        {
            entity.ToTable("ledger_entries");
            entity.HasKey(x => x.Id);
            entity.Property(x => x.Id).UseIdentityColumn();
            entity.Property(x => x.AgentId).HasMaxLength(AgentIdentity.MaxLength).IsRequired();
            entity.Property(x => x.ShipSymbol).HasMaxLength(100).IsRequired();
            entity.Property(x => x.Category).HasConversion<string>().HasMaxLength(50).IsRequired();
            entity.Property(x => x.GoodSymbol).HasMaxLength(100);
            entity.Property(x => x.WaypointSymbol).HasMaxLength(100);
            entity.Property(x => x.SourceEventId).HasMaxLength(200);
            entity.HasIndex(x => new { x.AgentId, x.OccurredAt });
            entity.HasIndex(x => new { x.AgentId, x.RunId });
            entity.HasIndex(x => new { x.AgentId, x.ShipSymbol, x.OccurredAt });
            entity.HasQueryFilter(x => x.AgentId == AgentId);
        });

        modelBuilder.Entity<RunCreditHighlight>(entity =>
        {
            entity.ToTable("run_credit_highlights");
            entity.HasKey(x => x.Id);
            entity.Property(x => x.Id).UseIdentityColumn();
            entity.Property(x => x.AgentId).HasMaxLength(AgentIdentity.MaxLength).IsRequired();
            entity.Property(x => x.EventKind).HasMaxLength(100).IsRequired();
            entity.Property(x => x.Label).HasMaxLength(500);
            entity.HasIndex(x => new { x.AgentId, x.RunId, x.OccurredAt });
            entity.HasQueryFilter(x => x.AgentId == AgentId);
        });

        modelBuilder.Entity<AgentCreditsSample>(entity =>
        {
            entity.ToTable("agent_credits_samples");
            entity.HasKey(x => x.Id);
            entity.Property(x => x.Id).UseIdentityColumn();
            entity.Property(x => x.AgentId).HasMaxLength(AgentIdentity.MaxLength).IsRequired();
            entity.HasIndex(x => new { x.AgentId, x.ObservedAt });
        });

        modelBuilder.Entity<MarketPriceSample>(entity =>
        {
            entity.ToTable("market_price_samples");
            entity.HasKey(x => x.Id);
            entity.Property(x => x.Id).UseIdentityColumn();
            entity.Property(x => x.AgentId).HasMaxLength(AgentIdentity.MaxLength).IsRequired();
            entity.Property(x => x.WaypointSymbol).HasMaxLength(100).IsRequired();
            entity.Property(x => x.GoodSymbol).HasMaxLength(100).IsRequired();
            entity.Property(x => x.Supply).HasMaxLength(50);
            entity.Property(x => x.Activity).HasMaxLength(50);
            entity.HasIndex(x => new { x.AgentId, x.GoodSymbol, x.ObservedAt });
            entity.HasIndex(x => new { x.AgentId, x.WaypointSymbol, x.GoodSymbol, x.ObservedAt });
            entity.HasQueryFilter(x => x.AgentId == AgentId);
        });

        modelBuilder.Entity<ShipTaskRecord>(entity =>
        {
            entity.ToTable("ship_task_records");
            entity.HasKey(x => x.Id);
            entity.Property(x => x.Id).UseIdentityColumn();
            entity.Property(x => x.AgentId).HasMaxLength(AgentIdentity.MaxLength).IsRequired();
            entity.Property(x => x.ShipSymbol).HasMaxLength(100).IsRequired();
            entity.Property(x => x.TaskKind).HasMaxLength(100).IsRequired();
            entity.Property(x => x.TargetWaypoint).HasMaxLength(100);
            entity.HasIndex(x => new { x.AgentId, x.ShipSymbol, x.StartedAt });
            entity.HasQueryFilter(x => x.AgentId == AgentId);
        });

        modelBuilder.Entity<FleetGoalRecord>(entity =>
        {
            entity.ToTable("fleet_goals");
            entity.HasKey(x => x.Id);
            entity.Property(x => x.AgentId).HasMaxLength(AgentIdentity.MaxLength).IsRequired();
            entity.Property(x => x.Kind).HasMaxLength(100).IsRequired();
            entity.Property(x => x.Description).IsRequired();
            entity.Property(x => x.PayloadJson).IsRequired();
            entity.HasIndex(x => new { x.AgentId, x.CompletedAt });
            entity.HasQueryFilter(x => x.AgentId == AgentId);
        });

        modelBuilder.Entity<PlanStateRecord>(entity =>
        {
            entity.ToTable("plan_states");
            entity.HasKey(x => new { x.AgentId, x.PlanType });
            entity.Property(x => x.AgentId).HasMaxLength(AgentIdentity.MaxLength).IsRequired();
            entity.Property(x => x.PlanType).HasMaxLength(200).IsRequired();
            entity.Property(x => x.StateJson).IsRequired();
            entity.Property(x => x.UpdatedAt).IsRequired();
            entity.HasQueryFilter(x => x.AgentId == AgentId);
        });

        modelBuilder.Entity<ScheduledShipEvent>(entity =>
        {
            entity.ToTable("scheduled_ship_events");
            entity.HasKey(x => new { x.ShipSymbol, x.GoalId });
            entity.Property(x => x.ShipSymbol).HasMaxLength(100).IsRequired();
            entity.Property(x => x.EventKind).HasMaxLength(30).IsRequired();
            entity.HasIndex(x => x.TriggerAt);
        });

        modelBuilder.Entity<ShipGoalHistoryRecord>(entity =>
        {
            entity.ToTable("ship_goal_history");
            entity.HasKey(x => x.Id);
            entity.Property(x => x.Id).ValueGeneratedNever();
            entity.Property(x => x.AgentId).HasMaxLength(AgentIdentity.MaxLength).IsRequired();
            entity.Property(x => x.ShipSymbol).HasMaxLength(100).IsRequired();
            entity.Property(x => x.GoalKind).HasMaxLength(100).IsRequired();
            entity.Property(x => x.Outcome).HasMaxLength(50).IsRequired();
            entity.HasIndex(x => new { x.AgentId, x.ShipSymbol, x.EndedAt });
            entity.HasQueryFilter(x => x.AgentId == AgentId);
        });
    }
}
