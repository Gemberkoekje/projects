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

            // Phase 1: local ship status
            await dbContext.Database.ExecuteSqlRawAsync(
                "ALTER TABLE cached_ships ADD COLUMN IF NOT EXISTS \"LocalStatus\" integer NOT NULL DEFAULT 0;",
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

        // Update column type constraints for AgentToken from varchar(512) to varchar(1024)
        await dbContext.Database.ExecuteSqlRawAsync(
            """
            DO $$
            BEGIN
                IF EXISTS (SELECT 1 FROM information_schema.columns WHERE table_name = 'stored_credentials' AND column_name = 'AgentToken' AND character_maximum_length = 512) THEN
                    ALTER TABLE stored_credentials ALTER COLUMN "AgentToken" TYPE character varying(1024);
                END IF;
            END $$;
            """,
            cancellationToken);
        await dbContext.Database.ExecuteSqlRawAsync(
            """
            DO $$
            BEGIN
                IF EXISTS (SELECT 1 FROM information_schema.columns WHERE table_name = 'cached_agents' AND column_name = 'AgentToken' AND character_maximum_length = 512) THEN
                    ALTER TABLE cached_agents ALTER COLUMN "AgentToken" TYPE character varying(1024);
                END IF;
            END $$;
            """,
            cancellationToken);
        await dbContext.Database.ExecuteSqlRawAsync(
            """
            DO $$
            BEGIN
                IF EXISTS (SELECT 1 FROM information_schema.columns WHERE table_name = 'cached_ships' AND column_name = 'AgentToken' AND character_maximum_length = 512) THEN
                    ALTER TABLE cached_ships ALTER COLUMN "AgentToken" TYPE character varying(1024);
                END IF;
            END $$;
            """,
            cancellationToken);
        await dbContext.Database.ExecuteSqlRawAsync(
            """
            DO $$
            BEGIN
                IF EXISTS (SELECT 1 FROM information_schema.columns WHERE table_name = 'cached_contracts' AND column_name = 'AgentToken' AND character_maximum_length = 512) THEN
                    ALTER TABLE cached_contracts ALTER COLUMN "AgentToken" TYPE character varying(1024);
                END IF;
            END $$;
            """,
            cancellationToken);
        await dbContext.Database.ExecuteSqlRawAsync(
            """
            DO $$
            BEGIN
                IF EXISTS (SELECT 1 FROM information_schema.columns WHERE table_name = 'cached_markets' AND column_name = 'AgentToken' AND character_maximum_length = 512) THEN
                    ALTER TABLE cached_markets ALTER COLUMN "AgentToken" TYPE character varying(1024);
                END IF;
            END $$;
            """,
            cancellationToken);
        await dbContext.Database.ExecuteSqlRawAsync(
            """
            DO $$
            BEGIN
                IF EXISTS (SELECT 1 FROM information_schema.columns WHERE table_name = 'cached_shipyards' AND column_name = 'AgentToken' AND character_maximum_length = 512) THEN
                    ALTER TABLE cached_shipyards ALTER COLUMN "AgentToken" TYPE character varying(1024);
                END IF;
            END $$;
            """,
            cancellationToken);
        await dbContext.Database.ExecuteSqlRawAsync(
            """
            DO $$
            BEGIN
                IF EXISTS (SELECT 1 FROM information_schema.columns WHERE table_name = 'cached_waypoints' AND column_name = 'AgentToken' AND character_maximum_length = 512) THEN
                    ALTER TABLE cached_waypoints ALTER COLUMN "AgentToken" TYPE character varying(1024);
                END IF;
            END $$;
            """,
            cancellationToken);
        await dbContext.Database.ExecuteSqlRawAsync(
            """
            DO $$
            BEGIN
                IF EXISTS (SELECT 1 FROM information_schema.columns WHERE table_name = 'cached_systems' AND column_name = 'AgentToken' AND character_maximum_length = 512) THEN
                    ALTER TABLE cached_systems ALTER COLUMN "AgentToken" TYPE character varying(1024);
                END IF;
            END $$;
            """,
            cancellationToken);
        await dbContext.Database.ExecuteSqlRawAsync(
            """
            DO $$
            BEGIN
                IF EXISTS (SELECT 1 FROM information_schema.columns WHERE table_name = 'agent_settings' AND column_name = 'AgentToken' AND character_maximum_length = 512) THEN
                    ALTER TABLE agent_settings ALTER COLUMN "AgentToken" TYPE character varying(1024);
                END IF;
            END $$;
            """,
            cancellationToken);
        await dbContext.Database.ExecuteSqlRawAsync(
            """
            DO $$
            BEGIN
                IF EXISTS (SELECT 1 FROM information_schema.columns WHERE table_name = 'ship_assignment_records' AND column_name = 'AgentToken' AND character_maximum_length = 512) THEN
                    ALTER TABLE ship_assignment_records ALTER COLUMN "AgentToken" TYPE character varying(1024);
                END IF;
            END $$;
            """,
            cancellationToken);
        await dbContext.Database.ExecuteSqlRawAsync(
            """
            DO $$
            BEGIN
                IF EXISTS (SELECT 1 FROM information_schema.columns WHERE table_name = 'trade_opportunities' AND column_name = 'AgentToken' AND character_maximum_length = 512) THEN
                    ALTER TABLE trade_opportunities ALTER COLUMN "AgentToken" TYPE character varying(1024);
                END IF;
            END $$;
            """,
            cancellationToken);
        await dbContext.Database.ExecuteSqlRawAsync(
            """
            DO $$
            BEGIN
                IF EXISTS (SELECT 1 FROM information_schema.columns WHERE table_name = 'activity_logs' AND column_name = 'AgentToken' AND character_maximum_length = 512) THEN
                    ALTER TABLE activity_logs ALTER COLUMN "AgentToken" TYPE character varying(1024);
                END IF;
            END $$;
            """,
            cancellationToken);
        await dbContext.Database.ExecuteSqlRawAsync(
            """
            DO $$
            BEGIN
                IF EXISTS (SELECT 1 FROM information_schema.columns WHERE table_name = 'leader_leases' AND column_name = 'AgentToken' AND character_maximum_length = 512) THEN
                    ALTER TABLE leader_leases ALTER COLUMN "AgentToken" TYPE character varying(1024);
                END IF;
            END $$;
            """,
            cancellationToken);
        await dbContext.Database.ExecuteSqlRawAsync(
            """
            DO $$
            BEGIN
                IF EXISTS (SELECT 1 FROM information_schema.columns WHERE table_name = 'api_endpoint_usages' AND column_name = 'AgentToken' AND character_maximum_length = 512) THEN
                    ALTER TABLE api_endpoint_usages ALTER COLUMN "AgentToken" TYPE character varying(1024);
                END IF;
            END $$;
            """,
            cancellationToken);
        await dbContext.Database.ExecuteSqlRawAsync(
            """
            DO $$
            BEGIN
                IF EXISTS (SELECT 1 FROM information_schema.columns WHERE table_name = 'cached_surveys' AND column_name = 'AgentToken' AND character_maximum_length = 512) THEN
                    ALTER TABLE cached_surveys ALTER COLUMN "AgentToken" TYPE character varying(1024);
                END IF;
            END $$;
            """,
            cancellationToken);
        await dbContext.Database.ExecuteSqlRawAsync(
            """
            DO $$
            BEGIN
                IF EXISTS (SELECT 1 FROM information_schema.columns WHERE table_name = 'cached_construction_sites' AND column_name = 'AgentToken' AND character_maximum_length = 512) THEN
                    ALTER TABLE cached_construction_sites ALTER COLUMN "AgentToken" TYPE character varying(1024);
                END IF;
            END $$;
            """,
            cancellationToken);

        // Update Value column in stored_credentials
        await dbContext.Database.ExecuteSqlRawAsync(
            """
            DO $$
            BEGIN
                IF EXISTS (SELECT 1 FROM information_schema.columns WHERE table_name = 'stored_credentials' AND column_name = 'Value' AND character_maximum_length = 512) THEN
                    ALTER TABLE stored_credentials ALTER COLUMN "Value" TYPE character varying(1024);
                END IF;
            END $$;
            """,
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
