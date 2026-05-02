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

            // Startup snapshots table
            await dbContext.Database.ExecuteSqlRawAsync(
                """
                CREATE TABLE IF NOT EXISTS startup_snapshots (
                    "Id" serial PRIMARY KEY,
                    "AgentToken" character varying(1024) NOT NULL DEFAULT '',
                    "SnapshotJson" text NOT NULL DEFAULT '',
                    "CapturedAt" timestamp with time zone NOT NULL DEFAULT now(),
                    "IsInitialSnapshot" boolean NOT NULL DEFAULT FALSE
                );
                """,
                cancellationToken);
            await dbContext.Database.ExecuteSqlRawAsync(
                "CREATE INDEX IF NOT EXISTS idx_startup_snapshots_agent_captured ON startup_snapshots (\"AgentToken\", \"CapturedAt\");",
                cancellationToken);

            // Phase 0a: analytics & observability tables
            await dbContext.Database.ExecuteSqlRawAsync(
                """
                CREATE TABLE IF NOT EXISTS runs (
                    "Id" uuid PRIMARY KEY,
                    "AgentToken" character varying(1024) NOT NULL DEFAULT '',
                    "Name" character varying(200) NOT NULL DEFAULT '',
                    "StrategyLabel" character varying(200) NOT NULL DEFAULT '',
                    "StartedAt" timestamp with time zone NOT NULL DEFAULT now(),
                    "EndedAt" timestamp with time zone NULL,
                    "SettingsSnapshotJson" text NULL,
                    "StartingCredits" bigint NOT NULL DEFAULT 0,
                    "EndingCredits" bigint NULL
                );
                """,
                cancellationToken);
            await dbContext.Database.ExecuteSqlRawAsync(
                "CREATE INDEX IF NOT EXISTS idx_runs_agent_started ON runs (\"AgentToken\", \"StartedAt\");",
                cancellationToken);

            await dbContext.Database.ExecuteSqlRawAsync(
                """
                CREATE TABLE IF NOT EXISTS scheduled_runs (
                    "Id" uuid PRIMARY KEY,
                    "AgentToken" character varying(1024) NOT NULL DEFAULT '',
                    "Name" character varying(200) NOT NULL DEFAULT '',
                    "StrategyLabel" character varying(200) NOT NULL DEFAULT '',
                    "ScheduledSettingsJson" text NULL,
                    "ActivatesAt" timestamp with time zone NULL,
                    "ActivatesOnNextRestart" boolean NOT NULL DEFAULT FALSE,
                    "CreatedAt" timestamp with time zone NOT NULL DEFAULT now()
                );
                """,
                cancellationToken);
            await dbContext.Database.ExecuteSqlRawAsync(
                "CREATE INDEX IF NOT EXISTS idx_scheduled_runs_agent_activates ON scheduled_runs (\"AgentToken\", \"ActivatesAt\");",
                cancellationToken);

            await dbContext.Database.ExecuteSqlRawAsync(
                """
                CREATE TABLE IF NOT EXISTS ledger_entries (
                    "Id" bigserial PRIMARY KEY,
                    "AgentToken" character varying(1024) NOT NULL DEFAULT '',
                    "OccurredAt" timestamp with time zone NOT NULL DEFAULT now(),
                    "ShipSymbol" character varying(100) NOT NULL DEFAULT '',
                    "RunId" uuid NULL,
                    "Category" character varying(50) NOT NULL DEFAULT '',
                    "Amount" bigint NOT NULL DEFAULT 0,
                    "GoodSymbol" character varying(100) NULL,
                    "UnitPrice" integer NULL,
                    "Units" integer NULL,
                    "WaypointSymbol" character varying(100) NULL,
                    "SourceEventId" character varying(200) NULL
                );
                """,
                cancellationToken);
            await dbContext.Database.ExecuteSqlRawAsync(
                "CREATE INDEX IF NOT EXISTS idx_ledger_entries_agent_occurred ON ledger_entries (\"AgentToken\", \"OccurredAt\");",
                cancellationToken);
            await dbContext.Database.ExecuteSqlRawAsync(
                "CREATE INDEX IF NOT EXISTS idx_ledger_entries_agent_run ON ledger_entries (\"AgentToken\", \"RunId\");",
                cancellationToken);
            await dbContext.Database.ExecuteSqlRawAsync(
                "CREATE INDEX IF NOT EXISTS idx_ledger_entries_agent_ship_occurred ON ledger_entries (\"AgentToken\", \"ShipSymbol\", \"OccurredAt\");",
                cancellationToken);

            await dbContext.Database.ExecuteSqlRawAsync(
                """
                CREATE TABLE IF NOT EXISTS run_credit_highlights (
                    "Id" bigserial PRIMARY KEY,
                    "AgentToken" character varying(1024) NOT NULL DEFAULT '',
                    "RunId" uuid NOT NULL,
                    "OccurredAt" timestamp with time zone NOT NULL DEFAULT now(),
                    "Credits" bigint NOT NULL DEFAULT 0,
                    "DeltaCredits" bigint NOT NULL DEFAULT 0,
                    "EventKind" character varying(100) NOT NULL DEFAULT '',
                    "Label" character varying(500) NULL
                );
                """,
                cancellationToken);
            await dbContext.Database.ExecuteSqlRawAsync(
                "CREATE INDEX IF NOT EXISTS idx_run_credit_highlights_agent_run_occurred ON run_credit_highlights (\"AgentToken\", \"RunId\", \"OccurredAt\");",
                cancellationToken);

            await dbContext.Database.ExecuteSqlRawAsync(
                """
                CREATE TABLE IF NOT EXISTS agent_credits_samples (
                    "Id" bigserial PRIMARY KEY,
                    "AgentToken" character varying(1024) NOT NULL DEFAULT '',
                    "ObservedAt" timestamp with time zone NOT NULL DEFAULT now(),
                    "Credits" bigint NOT NULL DEFAULT 0
                );
                """,
                cancellationToken);
            await dbContext.Database.ExecuteSqlRawAsync(
                "CREATE INDEX IF NOT EXISTS idx_agent_credits_samples_agent_observed ON agent_credits_samples (\"AgentToken\", \"ObservedAt\");",
                cancellationToken);

            await dbContext.Database.ExecuteSqlRawAsync(
                """
                CREATE TABLE IF NOT EXISTS market_price_samples (
                    "Id" bigserial PRIMARY KEY,
                    "AgentToken" character varying(1024) NOT NULL DEFAULT '',
                    "WaypointSymbol" character varying(100) NOT NULL DEFAULT '',
                    "GoodSymbol" character varying(100) NOT NULL DEFAULT '',
                    "ObservedAt" timestamp with time zone NOT NULL DEFAULT now(),
                    "PurchasePrice" integer NOT NULL DEFAULT 0,
                    "SellPrice" integer NOT NULL DEFAULT 0,
                    "Supply" character varying(50) NULL,
                    "Activity" character varying(50) NULL,
                    "TradeVolume" integer NOT NULL DEFAULT 0
                );
                """,
                cancellationToken);
            await dbContext.Database.ExecuteSqlRawAsync(
                "CREATE INDEX IF NOT EXISTS idx_market_price_samples_agent_good_observed ON market_price_samples (\"AgentToken\", \"GoodSymbol\", \"ObservedAt\");",
                cancellationToken);
            await dbContext.Database.ExecuteSqlRawAsync(
                "CREATE INDEX IF NOT EXISTS idx_market_price_samples_agent_waypoint_good_observed ON market_price_samples (\"AgentToken\", \"WaypointSymbol\", \"GoodSymbol\", \"ObservedAt\");",
                cancellationToken);

            await dbContext.Database.ExecuteSqlRawAsync(
                """
                CREATE TABLE IF NOT EXISTS ship_task_records (
                    "Id" bigserial PRIMARY KEY,
                    "AgentToken" character varying(1024) NOT NULL DEFAULT '',
                    "ShipSymbol" character varying(100) NOT NULL DEFAULT '',
                    "StartedAt" timestamp with time zone NOT NULL DEFAULT now(),
                    "EndedAt" timestamp with time zone NULL,
                    "TaskKind" character varying(100) NOT NULL DEFAULT '',
                    "TargetWaypoint" character varying(100) NULL,
                    "PayloadJson" text NULL
                );
                """,
                cancellationToken);
            await dbContext.Database.ExecuteSqlRawAsync(
                "CREATE INDEX IF NOT EXISTS idx_ship_task_records_agent_ship_started ON ship_task_records (\"AgentToken\", \"ShipSymbol\", \"StartedAt\");",
                cancellationToken);

            // Phase 8: ship goal model columns
            await dbContext.Database.ExecuteSqlRawAsync(
                "ALTER TABLE cached_ships ADD COLUMN IF NOT EXISTS \"GoalId\" uuid NULL;",
                cancellationToken);
            await dbContext.Database.ExecuteSqlRawAsync(
                "ALTER TABLE cached_ships ADD COLUMN IF NOT EXISTS \"GoalKind\" character varying(100) NULL;",
                cancellationToken);
            await dbContext.Database.ExecuteSqlRawAsync(
                "ALTER TABLE cached_ships ADD COLUMN IF NOT EXISTS \"GoalPayloadJson\" text NULL;",
                cancellationToken);
            await dbContext.Database.ExecuteSqlRawAsync(
                "ALTER TABLE cached_ships ADD COLUMN IF NOT EXISTS \"GoalStatus\" integer NULL;",
                cancellationToken);

            // Phase 14a: scheduled ship events table
            await dbContext.Database.ExecuteSqlRawAsync(
                """
                CREATE TABLE IF NOT EXISTS scheduled_ship_events (
                    "ShipSymbol"  character varying(100) NOT NULL,
                    "GoalId"      uuid                   NOT NULL,
                    "EventKind"   character varying(30)  NOT NULL,
                    "TriggerAt"   timestamp with time zone NOT NULL,
                    CONSTRAINT "PK_scheduled_ship_events" PRIMARY KEY ("ShipSymbol", "GoalId")
                );
                """,
                cancellationToken);
            await dbContext.Database.ExecuteSqlRawAsync(
                "CREATE INDEX IF NOT EXISTS idx_scheduled_ship_events_trigger_at ON scheduled_ship_events (\"TriggerAt\");",
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
