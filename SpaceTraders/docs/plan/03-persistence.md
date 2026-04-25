# 03 – Local Cache & Settings Persistence

## Goals
- Persist all API data locally so the agent operates across pod restarts with minimal re-fetching.
- **API calls are a last resort.** Every mutating call returns fresh state; apply it directly. Never issue a GET just to confirm what a POST already told you.
- Dead-reckon ship navigation: store the arrival timestamp, flip the ship to arrived when time elapses – no polling.
- Poll **only** Market and Shipyard data, and only when a ship is present at that waypoint.
- Store operator-configurable settings that survive pod restarts and can be changed at runtime.
- Store the agent token in the DB; bootstrap it from the account token if absent.

---

## 3.1 Technology Choices

| Concern | Choice | Rationale |
|---------|--------|-----------|
| ORM | EF Core 10 | code-first migrations, LINQ, strong typing |
| Database | **PostgreSQL** | supports horizontal scale-out, no PVC needed, well-supported on K8s |
| Provider | `Npgsql.EntityFrameworkCore.PostgreSQL` | official EF Core provider |
| Connection string | `appsettings.json` → overridden by `ConnectionStrings__DefaultConnection` env var / K8s Secret | no secrets in code |
| Migrations | `dotnet ef migrations` at startup via `app.Services.MigrateAsync()` | automatic, no manual step |
| Connection pooling | Npgsql's built-in pool (default: min 0, max 100) | tune via `Minimum Pool Size` / `Maximum Pool Size` in connection string |

### Connection Pool Tuning

Recommended connection string parameters for production:

```
Host=postgres-svc;Database=spacetraders;Username=st;Password=...;
Minimum Pool Size=2;Maximum Pool Size=20;Connection Idle Lifetime=300;Connection Pruning Interval=10
```

Keep `Maximum Pool Size` below the PostgreSQL `max_connections` limit (default: 100) with headroom for
migrations and administrative connections. For a single-replica deployment, 20 is a safe ceiling.

---

## 3.2 Connection String

```json
// appsettings.json (committed – no real values)
{
  "ConnectionStrings": {
    "DefaultConnection": "Host=localhost;Database=spacetraders;Username=postgres;Password=changeme"
  },
  "SpaceTraders": {
    "AccountToken": "",
    "AgentName":    "",
    "AgentFaction": "COSMIC"
  }
}
```

In production, override via environment variables (or Kubernetes Secret → env):
```
ConnectionStrings__DefaultConnection=Host=postgres-svc;Database=spacetraders;Username=st;Password=...
SpaceTraders__AccountToken=...
```

`dotnet user-secrets` is used in development so no real values are ever committed.

---

## 3.3 DbContext

```csharp
// SpaceTraders.Infrastructure.Persistence/SpaceTradersDbContext.cs

public class SpaceTradersDbContext : DbContext
{
    public DbSet<StoredCredential>     Credentials        { get; set; }  // agent token storage
    public DbSet<CachedAgent>          Agents             { get; set; }
    public DbSet<CachedShip>           Ships              { get; set; }
    public DbSet<CachedContract>       Contracts          { get; set; }
    public DbSet<CachedMarket>         Markets            { get; set; }
    public DbSet<CachedShipyard>       Shipyards          { get; set; }
    public DbSet<CachedWaypoint>       Waypoints          { get; set; }
    public DbSet<CachedSystem>         Systems            { get; set; }
    public DbSet<AgentSetting>         Settings           { get; set; }
    public DbSet<ShipAssignmentRecord> ShipAssignments    { get; set; }
    public DbSet<TradeOpportunity>     TradeOpportunities { get; set; }
    public DbSet<ActivityLog>          ActivityLog        { get; set; }
}
```

---

## 3.4 Agent Bootstrap & Credential Storage

On startup, `AgentBootstrapService` (an `IHostedService` with priority over automation services) runs:

```
1. Query StoredCredential WHERE Key = 'AgentToken'
2. If found → load token into IAgentTokenProvider (singleton), proceed
3. If not found:
   a. Use AccountToken from IConfiguration to call POST /register
      with AgentName + AgentFaction from IConfiguration
   b. Persist the returned agent token to StoredCredential
   c. Persist the returned Agent data to CachedAgent
   d. Load token into IAgentTokenProvider
```

```csharp
public sealed class StoredCredential
{
    public string Key              { get; set; }   // PK  e.g. "AgentToken"
    public string Value            { get; set; }   // the token
    public DateTimeOffset StoredAt { get; set; }
}

// Singleton injected into SpaceTradersApiClient
public interface IAgentTokenProvider
{
    string? Token { get; }
    void Set(string token);
}
```

The `SpaceTradersApiClient` uses `IAgentTokenProvider.Token` for `Authorization: Bearer` on all `AgentToken`-mode requests.

---

## 3.5 Cache Strategy – API Responses as Source of Truth

**Rule:** If a mutating call (POST/PATCH) returns updated state, apply it directly. Never issue a GET to re-read what a POST already returned.

| Action | What the API returns | What we do |
|--------|---------------------|------------|
| Navigate | `ShipNav` (with `route.arrival`) | Update `CachedShip.Nav`, set `ArrivesAt = route.arrival` |
| Dock / Orbit | `ShipNav` | Update `CachedShip.Nav` |
| Sell cargo | `Agent` (credits) + `Cargo` + transaction | Update `CachedAgent.Credits`, update `CachedShip.Cargo` |
| Buy cargo | `Agent` + `Cargo` + transaction | Same |
| Extract | `Extraction` + `Cargo` | Update `CachedShip.Cargo` |
| Refuel | `Agent` + `Fuel` + transaction | Update `CachedShip.Fuel`, `CachedAgent.Credits` |
| Purchase ship | `Agent` + new `Ship` | Update `CachedAgent`, insert `CachedShip` |
| Accept contract | `Contract` | Update `CachedContract` |
| Deliver contract | `Contract` | Update `CachedContract` |
| Fulfill contract | `Agent` + `Contract` | Update both |

**Navigation dead-reckoning:**
```csharp
public sealed class CachedShip
{
    public string          Symbol        { get; set; }
    public string          NavStatus     { get; set; }   // DOCKED | IN_ORBIT | IN_TRANSIT
    public string          CurrentWaypoint { get; set; }
    public string?         DestWaypoint  { get; set; }   // set when navigating
    public DateTimeOffset? ArrivesAt     { get; set; }   // null = already arrived
    public int             FuelCurrent   { get; set; }
    public int             FuelCapacity  { get; set; }
    public int             CargoCurrent  { get; set; }
    public int             CargoCapacity { get; set; }
    public string          CargoJson     { get; set; }   // serialized cargo items
    public DateTimeOffset  LastSyncedAt  { get; set; }

    [NotMapped]
    public bool IsInTransit => ArrivesAt.HasValue && ArrivesAt.Value > DateTimeOffset.UtcNow;

    // Called by GameLoopService on every tick – zero API calls
    public void ApplyArrivalIfDue()
    {
        if (ArrivesAt.HasValue && !IsInTransit)
        {
            CurrentWaypoint = DestWaypoint!;
            DestWaypoint    = null;
            ArrivesAt       = null;
            NavStatus       = "IN_ORBIT";
        }
    }
}
```

---

## 3.6 Cache Entities & Refresh Rules

| Entity | How it gets updated | When to call the API |
|--------|--------------------|--------------------|
| `CachedAgent` | From POST response bodies | Startup sync only |
| `CachedShip` | From POST response bodies + `ApplyArrivalIfDue()` | Startup sync only |
| `CachedContract` | From POST response bodies | Startup sync only |
| `CachedMarket` | **Polled only** | When a ship is docked/orbiting at that waypoint AND TTL expired |
| `CachedShipyard` | **Polled only** | When a ship is docked/orbiting at that waypoint AND TTL expired |
| `CachedWaypoint` | Fetched once, essentially static | First time a system is entered |
| `CachedSystem` | Fetched once, essentially static | First time a system is seen |

Market poll TTL: `Scout.MarketRefreshIntervalMinutes` setting (default: 10 min).
Shipyard poll TTL: `Scout.ShipyardRefreshIntervalMinutes` setting (default: 30 min).
Both only trigger if a ship is physically present (docked or orbiting) at the waypoint.

---

## 3.7 Settings Store

Operator-tunable knobs, editable at runtime via the Internal API (no redeploy required).

```csharp
public sealed class AgentSetting
{
    public string Key         { get; set; }   // PK
    public string Value       { get; set; }
    public string Type        { get; set; }   // "int" | "long" | "decimal" | "bool" | "string"
    public string Description { get; set; }
}
```

### Default Settings Seed

| Key | Default | Description |
|-----|---------|-------------|
| `FleetExpansion.MinCreditReserve` | `100000` | Credits to always keep in bank |
| `FleetExpansion.MinCreditRatioForShip` | `0.5` | Fraction of ship price that must remain after purchase |
| `FleetExpansion.MaxShips` | `20` | Hard cap on fleet size |
| `FleetExpansion.PreferredShipType` | `SHIP_MINING_DRONE` | Default ship type to buy |
| `Trade.MinProfitPerUnit` | `200` | Skip trade routes below this margin |
| `Trade.MaxHaulDistance` | `5` | Max jumps between buy/sell waypoints |
| `Contract.AutoAccept` | `true` | Auto-accept contracts when profitable |
| `Scout.MarketRefreshIntervalMinutes` | `10` | How often to re-poll markets with a ship present |
| `Scout.ShipyardRefreshIntervalMinutes` | `30` | How often to re-poll shipyards with a ship present |
| `Automation.Enabled` | `true` | Master kill-switch for automation |

A typed `ISettingsRepository` wraps the DB and exposes `GetAsync<T>(string key)` / `SetAsync<T>(string key, T value)`.

---

## 3.8 Ship Assignment Record

Persisted so the agent knows what each ship was doing before a restart:

```csharp
public sealed class ShipAssignmentRecord
{
    public string         ShipSymbol     { get; set; }  // PK
    public AssignmentType Type           { get; set; }
    public string?        OriginWaypoint { get; set; }
    public string?        DestWaypoint   { get; set; }
    public string?        CargoSymbol    { get; set; }
    public string?        ContractId     { get; set; }
    public int            StepIndex      { get; set; }  // which step of the assignment we're on
    public DateTimeOffset AssignedAt     { get; set; }
    public DateTimeOffset? CompletedAt   { get; set; }
}
```

---

## 3.9 Trade Opportunity Cache

Pre-computed profitable routes, refreshed whenever market data changes:

```csharp
public sealed class TradeOpportunity
{
    public int            Id              { get; set; }
    public string         TradeSymbol     { get; set; }
    public string         BuyWaypoint     { get; set; }
    public string         SellWaypoint    { get; set; }
    public int            BuyPrice        { get; set; }
    public int            SellPrice       { get; set; }
    public int            ProfitPerUnit   { get; set; }
    public int            DistanceJumps   { get; set; }
    public decimal        ProfitPerJump   { get; set; }
    public DateTimeOffset ComputedAt      { get; set; }
}
```

---

## 3.10 Activity Log

Append-only log of all automation decisions – useful for the dashboard:

```csharp
public sealed class ActivityLog
{
    public long           Id          { get; set; }
    public DateTimeOffset Timestamp   { get; set; }
    public string         ShipSymbol  { get; set; }
    public string         EventType   { get; set; }
    public string         Message     { get; set; }
    public string?        JsonDetails { get; set; }
}
```

**Retention:** Rows older than `ActivityLog.RetentionDays` (default: 30, configurable via `AgentSetting`)
are purged by a nightly background job (`ActivityLogPruningService`). This prevents unbounded table growth.

**Index:** Add a composite index on `(Timestamp DESC, ShipSymbol)` to keep dashboard queries fast as
the table grows.

---

## 3.11 Migration Rollback Strategy

EF Core does not support automatic rollback of applied migrations. To safely roll back:

1. **Preferred:** Write a new migration that reverses the schema change (forward-only approach).
2. **Emergency rollback to a specific migration:**
   ```powershell
   dotnet ef database update <PreviousMigrationName> --startup-project ../SpaceTraders.API
   dotnet ef migrations remove --startup-project ../SpaceTraders.API
   ```
3. Always generate and review the SQL script before applying to production:
   ```powershell
   dotnet ef migrations script <From> <To> --startup-project ../SpaceTraders.API --idempotent
   ```
4. Keep a manual snapshot of the DB before each production migration:
   ```powershell
   pg_dump -Fc spacetraders > backup-$(Get-Date -Format yyyyMMdd-HHmm).dump
   ```

---

## 3.12 Repository Interfaces (in Application layer)

```
ICredentialRepository
IAgentRepository
IShipRepository
IContractRepository
IMarketRepository
IShipyardRepository
IWaypointRepository
ISystemRepository
ISettingsRepository
IShipAssignmentRepository
ITradeOpportunityRepository
IActivityLogRepository
```

---

## 3.13 Folder Structure

```
SpaceTraders.Infrastructure.Persistence/
├── SpaceTradersDbContext.cs
├── DependencyInjection.cs              ← registers DbContext (Npgsql), all repositories
├── Migrations/                         ← EF Core auto-generated
├── Bootstrap/
│   └── AgentBootstrapService.cs        ← IHostedService, runs before automation
├── Entities/
│   ├── StoredCredential.cs
│   ├── CachedAgent.cs
│   ├── CachedShip.cs                   ← includes ArrivesAt, DestWaypoint
│   ├── CachedContract.cs
│   ├── CachedMarket.cs
│   ├── CachedShipyard.cs
│   ├── CachedWaypoint.cs
│   ├── CachedSystem.cs
│   ├── AgentSetting.cs
│   ├── ShipAssignmentRecord.cs
│   ├── TradeOpportunity.cs
│   └── ActivityLog.cs
├── Repositories/
│   ├── Base/
│   │   └── BaseRepository.cs
│   ├── CredentialRepository.cs
│   ├── AgentRepository.cs
│   ├── ShipRepository.cs
│   ├── ContractRepository.cs
│   ├── MarketRepository.cs
│   ├── ShipyardRepository.cs
│   ├── WaypointRepository.cs
│   ├── SystemRepository.cs
│   ├── SettingsRepository.cs
│   ├── ShipAssignmentRepository.cs
│   ├── TradeOpportunityRepository.cs
│   └── ActivityLogRepository.cs
├── TokenProvider/
│   └── AgentTokenProvider.cs           ← IAgentTokenProvider singleton
└── Seed/
    └── DefaultSettingsSeed.cs
```
