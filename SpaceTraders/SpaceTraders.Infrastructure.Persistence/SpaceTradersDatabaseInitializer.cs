using Microsoft.EntityFrameworkCore;
using SpaceTraders.Infrastructure.Persistence.Seed;

namespace SpaceTraders.Infrastructure.Persistence;

public static class SpaceTradersDatabaseInitializer
{
    public static async Task InitializeAsync(
        SpaceTradersDbContext dbContext,
        CancellationToken cancellationToken = default)
    {
        await dbContext.Database.EnsureCreatedAsync(cancellationToken);

        if (dbContext.Database.IsNpgsql())
        {
            await EnsureAgentTokenSchemaAsync(dbContext, cancellationToken);

            await dbContext.Database.ExecuteSqlRawAsync(
                "ALTER TABLE cached_ships ADD COLUMN IF NOT EXISTS \"MountsJson\" text NULL;",
                cancellationToken);
            await dbContext.Database.ExecuteSqlRawAsync(
                "ALTER TABLE cached_ships ADD COLUMN IF NOT EXISTS \"ShipType\" text NULL;",
                cancellationToken);
            await dbContext.Database.ExecuteSqlRawAsync(
                "UPDATE cached_ships SET \"ShipType\" = '' WHERE \"ShipType\" IS NULL;",
                cancellationToken);

            // Phase 2: ship component enrichment
            await dbContext.Database.ExecuteSqlRawAsync(
                "ALTER TABLE cached_ships ADD COLUMN IF NOT EXISTS \"ModulesJson\" text NULL;",
                cancellationToken);
            await dbContext.Database.ExecuteSqlRawAsync(
                "ALTER TABLE cached_ships ADD COLUMN IF NOT EXISTS \"FrameJson\" text NULL;",
                cancellationToken);
            await dbContext.Database.ExecuteSqlRawAsync(
                "ALTER TABLE cached_ships ADD COLUMN IF NOT EXISTS \"ReactorJson\" text NULL;",
                cancellationToken);
            await dbContext.Database.ExecuteSqlRawAsync(
                "ALTER TABLE cached_ships ADD COLUMN IF NOT EXISTS \"EngineJson\" text NULL;",
                cancellationToken);
            await dbContext.Database.ExecuteSqlRawAsync(
                "ALTER TABLE cached_ships ADD COLUMN IF NOT EXISTS \"CooldownExpiresAt\" timestamp with time zone NULL;",
                cancellationToken);

            // Phase 2: waypoint enrichment
            await dbContext.Database.ExecuteSqlRawAsync(
                "ALTER TABLE cached_waypoints ADD COLUMN IF NOT EXISTS \"TraitsJson\" text NULL;",
                cancellationToken);
            await dbContext.Database.ExecuteSqlRawAsync(
                "ALTER TABLE cached_waypoints ADD COLUMN IF NOT EXISTS \"ModifiersJson\" text NULL;",
                cancellationToken);
            await dbContext.Database.ExecuteSqlRawAsync(
                "ALTER TABLE cached_waypoints ADD COLUMN IF NOT EXISTS \"OrbitalsJson\" text NULL;",
                cancellationToken);
            await dbContext.Database.ExecuteSqlRawAsync(
                "ALTER TABLE cached_waypoints ADD COLUMN IF NOT EXISTS \"ParentSymbol\" text NULL;",
                cancellationToken);
            await dbContext.Database.ExecuteSqlRawAsync(
                "ALTER TABLE cached_waypoints ADD COLUMN IF NOT EXISTS \"IsUnderConstruction\" boolean NOT NULL DEFAULT FALSE;",
                cancellationToken);
            await dbContext.Database.ExecuteSqlRawAsync(
                "ALTER TABLE cached_waypoints ADD COLUMN IF NOT EXISTS \"ChartJson\" text NULL;",
                cancellationToken);

            // Phase 2: shipyard enrichment
            await dbContext.Database.ExecuteSqlRawAsync(
                "ALTER TABLE cached_shipyards ADD COLUMN IF NOT EXISTS \"ShipsDetailJson\" text NULL;",
                cancellationToken);

            // Phase 3: contract objective planning metadata
            await dbContext.Database.ExecuteSqlRawAsync(
                "ALTER TABLE cached_contracts ADD COLUMN IF NOT EXISTS \"TermsDeadline\" timestamp with time zone NULL;",
                cancellationToken);
            await dbContext.Database.ExecuteSqlRawAsync(
                "ALTER TABLE cached_contracts ADD COLUMN IF NOT EXISTS \"DeliverablesJson\" text NULL;",
                cancellationToken);
        }

        if (!string.IsNullOrWhiteSpace(dbContext.AgentToken))
        {
            await DefaultSettingsSeed.SeedAsync(dbContext, cancellationToken);
        }
    }

    private static async Task EnsureAgentTokenSchemaAsync(
        SpaceTradersDbContext dbContext,
        CancellationToken cancellationToken)
    {
        await dbContext.Database.ExecuteSqlRawAsync("ALTER TABLE stored_credentials ADD COLUMN IF NOT EXISTS \"AgentToken\" text NOT NULL DEFAULT '';", cancellationToken);
        await dbContext.Database.ExecuteSqlRawAsync("ALTER TABLE cached_agents ADD COLUMN IF NOT EXISTS \"AgentToken\" text NOT NULL DEFAULT '';", cancellationToken);
        await dbContext.Database.ExecuteSqlRawAsync("ALTER TABLE cached_ships ADD COLUMN IF NOT EXISTS \"AgentToken\" text NOT NULL DEFAULT '';", cancellationToken);
        await dbContext.Database.ExecuteSqlRawAsync("ALTER TABLE cached_contracts ADD COLUMN IF NOT EXISTS \"AgentToken\" text NOT NULL DEFAULT '';", cancellationToken);
        await dbContext.Database.ExecuteSqlRawAsync("ALTER TABLE cached_markets ADD COLUMN IF NOT EXISTS \"AgentToken\" text NOT NULL DEFAULT '';", cancellationToken);
        await dbContext.Database.ExecuteSqlRawAsync("ALTER TABLE cached_shipyards ADD COLUMN IF NOT EXISTS \"AgentToken\" text NOT NULL DEFAULT '';", cancellationToken);
        await dbContext.Database.ExecuteSqlRawAsync("ALTER TABLE cached_waypoints ADD COLUMN IF NOT EXISTS \"AgentToken\" text NOT NULL DEFAULT '';", cancellationToken);
        await dbContext.Database.ExecuteSqlRawAsync("ALTER TABLE cached_systems ADD COLUMN IF NOT EXISTS \"AgentToken\" text NOT NULL DEFAULT '';", cancellationToken);
        await dbContext.Database.ExecuteSqlRawAsync("ALTER TABLE agent_settings ADD COLUMN IF NOT EXISTS \"AgentToken\" text NOT NULL DEFAULT '';", cancellationToken);
        await dbContext.Database.ExecuteSqlRawAsync("ALTER TABLE ship_assignment_records ADD COLUMN IF NOT EXISTS \"AgentToken\" text NOT NULL DEFAULT '';", cancellationToken);
        await dbContext.Database.ExecuteSqlRawAsync("ALTER TABLE ship_assignment_records ADD COLUMN IF NOT EXISTS \"PurchaseUnitPrice\" integer NOT NULL DEFAULT 0;", cancellationToken);
        await dbContext.Database.ExecuteSqlRawAsync("ALTER TABLE ship_assignment_records ADD COLUMN IF NOT EXISTS \"RequiredUnits\" integer NOT NULL DEFAULT 0;", cancellationToken);
        await dbContext.Database.ExecuteSqlRawAsync("ALTER TABLE ship_assignment_records ADD COLUMN IF NOT EXISTS \"SupplyCompleted\" boolean NOT NULL DEFAULT FALSE;", cancellationToken);
        await dbContext.Database.ExecuteSqlRawAsync("ALTER TABLE trade_opportunities ADD COLUMN IF NOT EXISTS \"AgentToken\" text NOT NULL DEFAULT '';", cancellationToken);
        await dbContext.Database.ExecuteSqlRawAsync("ALTER TABLE trade_opportunities ADD COLUMN IF NOT EXISTS \"ProfitPerJump\" numeric(18,4) NOT NULL DEFAULT 0;", cancellationToken);
        await dbContext.Database.ExecuteSqlRawAsync("ALTER TABLE trade_opportunities ADD COLUMN IF NOT EXISTS \"SupportsSupplyChain\" boolean NOT NULL DEFAULT FALSE;", cancellationToken);
        await dbContext.Database.ExecuteSqlRawAsync("ALTER TABLE trade_opportunities ADD COLUMN IF NOT EXISTS \"SupplyChainDepth\" integer NOT NULL DEFAULT 0;", cancellationToken);
        await dbContext.Database.ExecuteSqlRawAsync("ALTER TABLE trade_opportunities ADD COLUMN IF NOT EXISTS \"ComputedAt\" timestamp with time zone NOT NULL DEFAULT now();", cancellationToken);
        await dbContext.Database.ExecuteSqlRawAsync("ALTER TABLE trade_opportunities ADD COLUMN IF NOT EXISTS \"BuyType\" text NOT NULL DEFAULT 'UNKNOWN';", cancellationToken);
        await dbContext.Database.ExecuteSqlRawAsync("ALTER TABLE trade_opportunities ADD COLUMN IF NOT EXISTS \"SellType\" text NOT NULL DEFAULT 'UNKNOWN';", cancellationToken);
        await dbContext.Database.ExecuteSqlRawAsync("ALTER TABLE trade_opportunities ADD COLUMN IF NOT EXISTS \"EffectiveTradeVolume\" integer NOT NULL DEFAULT 0;", cancellationToken);
        await dbContext.Database.ExecuteSqlRawAsync("ALTER TABLE trade_opportunities ADD COLUMN IF NOT EXISTS \"EstimatedFuelCost\" numeric(18,4) NOT NULL DEFAULT 0;", cancellationToken);
        await dbContext.Database.ExecuteSqlRawAsync("ALTER TABLE trade_opportunities ADD COLUMN IF NOT EXISTS \"EstimatedTravelTimeMinutes\" numeric(18,4) NOT NULL DEFAULT 0;", cancellationToken);
        await dbContext.Database.ExecuteSqlRawAsync("ALTER TABLE trade_opportunities ADD COLUMN IF NOT EXISTS \"OpportunityCostPenalty\" numeric(18,4) NOT NULL DEFAULT 0;", cancellationToken);
        await dbContext.Database.ExecuteSqlRawAsync("ALTER TABLE trade_opportunities ADD COLUMN IF NOT EXISTS \"CooldownPenalty\" numeric(18,4) NOT NULL DEFAULT 0;", cancellationToken);
        await dbContext.Database.ExecuteSqlRawAsync("ALTER TABLE trade_opportunities ADD COLUMN IF NOT EXISTS \"RateLimitPenalty\" numeric(18,4) NOT NULL DEFAULT 0;", cancellationToken);
        await dbContext.Database.ExecuteSqlRawAsync("ALTER TABLE trade_opportunities ADD COLUMN IF NOT EXISTS \"RouteScore\" numeric(18,4) NOT NULL DEFAULT 0;", cancellationToken);
        await dbContext.Database.ExecuteSqlRawAsync("ALTER TABLE activity_logs ADD COLUMN IF NOT EXISTS \"AgentToken\" text NOT NULL DEFAULT '';", cancellationToken);
        await dbContext.Database.ExecuteSqlRawAsync("ALTER TABLE leader_leases ADD COLUMN IF NOT EXISTS \"AgentToken\" text NOT NULL DEFAULT '';", cancellationToken);

        await dbContext.Database.ExecuteSqlRawAsync(
            """
            CREATE TABLE IF NOT EXISTS api_endpoint_usages (
                "Id" bigint GENERATED ALWAYS AS IDENTITY PRIMARY KEY,
                "AgentToken" text NOT NULL,
                "HttpMethod" text NOT NULL,
                "Endpoint" text NOT NULL,
                "Calls" integer NOT NULL,
                "LastCalledAt" timestamp with time zone NOT NULL
            );
            """,
            cancellationToken);

        await dbContext.Database.ExecuteSqlRawAsync(
            "CREATE UNIQUE INDEX IF NOT EXISTS \"IX_api_endpoint_usages_AgentToken_HttpMethod_Endpoint\" ON api_endpoint_usages (\"AgentToken\", \"HttpMethod\", \"Endpoint\");",
            cancellationToken);

        await dbContext.Database.ExecuteSqlRawAsync(
            "CREATE INDEX IF NOT EXISTS \"IX_trade_opportunities_AgentToken_SupportsSupplyChain_SupplyChainDepth_ProfitPerJump_ComputedAt\" ON trade_opportunities (\"AgentToken\", \"SupportsSupplyChain\", \"SupplyChainDepth\", \"ProfitPerJump\", \"ComputedAt\");",
            cancellationToken);

        await dbContext.Database.ExecuteSqlRawAsync(
            "CREATE INDEX IF NOT EXISTS \"IX_trade_opportunities_AgentToken_RouteScore_ComputedAt\" ON trade_opportunities (\"AgentToken\", \"RouteScore\", \"ComputedAt\");",
            cancellationToken);

        await EnsureCompositePrimaryKeyAsync(dbContext, "stored_credentials", "stored_credentials_pkey", "\"AgentToken\", \"Key\"", cancellationToken);
        await EnsureCompositePrimaryKeyAsync(dbContext, "cached_agents", "cached_agents_pkey", "\"AgentToken\", \"Symbol\"", cancellationToken);
        await EnsureCompositePrimaryKeyAsync(dbContext, "cached_ships", "cached_ships_pkey", "\"AgentToken\", \"Symbol\"", cancellationToken);
        await EnsureCompositePrimaryKeyAsync(dbContext, "cached_contracts", "cached_contracts_pkey", "\"AgentToken\", \"Id\"", cancellationToken);
        await EnsureCompositePrimaryKeyAsync(dbContext, "cached_markets", "cached_markets_pkey", "\"AgentToken\", \"WaypointSymbol\"", cancellationToken);
        await EnsureCompositePrimaryKeyAsync(dbContext, "cached_shipyards", "cached_shipyards_pkey", "\"AgentToken\", \"WaypointSymbol\"", cancellationToken);
        await EnsureCompositePrimaryKeyAsync(dbContext, "cached_waypoints", "cached_waypoints_pkey", "\"AgentToken\", \"Symbol\"", cancellationToken);
        await EnsureCompositePrimaryKeyAsync(dbContext, "cached_systems", "cached_systems_pkey", "\"AgentToken\", \"Symbol\"", cancellationToken);
        await EnsureCompositePrimaryKeyAsync(dbContext, "agent_settings", "agent_settings_pkey", "\"AgentToken\", \"Key\"", cancellationToken);
        await EnsureCompositePrimaryKeyAsync(dbContext, "ship_assignment_records", "ship_assignment_records_pkey", "\"AgentToken\", \"ShipSymbol\"", cancellationToken);
        await EnsureCompositePrimaryKeyAsync(dbContext, "leader_leases", "leader_leases_pkey", "\"AgentToken\", \"Key\"", cancellationToken);
        await EnsureCompositePrimaryKeyAsync(dbContext, "cached_surveys", "cached_surveys_pkey", "\"AgentToken\", \"Signature\"", cancellationToken);

        // Phase 10: construction sites
        await dbContext.Database.ExecuteSqlRawAsync(
            """
            CREATE TABLE IF NOT EXISTS cached_construction_sites (
                "AgentToken" text NOT NULL,
                "WaypointSymbol" text NOT NULL,
                "SystemSymbol" text NOT NULL,
                "IsComplete" boolean NOT NULL DEFAULT FALSE,
                "MaterialsJson" text NULL,
                "LastObservedAt" timestamp with time zone NOT NULL DEFAULT now(),
                CONSTRAINT "cached_construction_sites_pkey" PRIMARY KEY ("AgentToken", "WaypointSymbol")
            );
            """,
            cancellationToken);

        await dbContext.Database.ExecuteSqlRawAsync(
            "CREATE INDEX IF NOT EXISTS \"IX_cached_construction_sites_AgentToken_IsComplete\" ON cached_construction_sites (\"AgentToken\", \"IsComplete\");",
            cancellationToken);
    }

    private static Task EnsureCompositePrimaryKeyAsync(
        SpaceTradersDbContext dbContext,
        string tableName,
        string primaryKeyName,
        string columns,
        CancellationToken cancellationToken)
    {
        var sql = $"""
DO $$
DECLARE
    current_constraint text;
BEGIN
    SELECT tc.constraint_name
    INTO current_constraint
    FROM information_schema.table_constraints tc
    WHERE tc.table_schema = 'public'
      AND tc.table_name = '{tableName}'
      AND tc.constraint_type = 'PRIMARY KEY';

    IF current_constraint IS NOT NULL THEN
        EXECUTE format('ALTER TABLE %I DROP CONSTRAINT %I', '{tableName}', current_constraint);
    END IF;

    EXECUTE 'ALTER TABLE "{tableName}" ADD CONSTRAINT "{primaryKeyName}" PRIMARY KEY ({columns})';
END $$;
""";

        return dbContext.Database.ExecuteSqlRawAsync(sql, cancellationToken);
    }
}
