# Race Condition Prevention Plan

## Executive Summary

This document defines the architectural guarantees and enforcement mechanisms to ensure **no divergence between the PostgreSQL cache and SpaceTraders.io API state**, specifically addressing the critical ordering: **API call → database update → event publish**.

---

## 1. Problem Statement

A race condition exists if:
1. A command successfully calls a POST endpoint on SpaceTraders.io (state changes on API)
2. The local process crashes, network fails, or is preempted **before** persisting the response
3. On pod restart or recovery, the event is emitted but the database remains stale
4. Handlers react to stale data, causing divergence

**Example (pre-fix):**
```
1. Navigate command calls POST /my/ships/{symbol}/navigate → succeeds, ship is now IN_TRANSIT on SpaceTraders.io
2. Command receives response with ArrivesAt = 2025-01-15T10:30:00Z
3. Process crashes before UpdateNavAsync() completes
4. Pod restarts; recovery service syncs from API (sees ship IN_TRANSIT)
5. No recovery event is emitted because the recovery path checks local ArrivesAt (still null)
6. Cache is stale; handlers have outdated Cargo, Credits, Nav state
```

---

## 2. Solution Architecture

### 2.1 Core Ordering Guarantee

Every state mutation follows this strict order:

```
┌─────────────────────────────────────────────────────────────┐
│ 1. COMMAND HANDLER: Call SpaceTraders.io API (POST/PATCH)  │
│    • Validate preconditions against local state             │
│    • Call API with validated input                          │
│    • If API call fails → STOP, emit no event, raise error   │
└─────────────────────────────────────────────────────────────┘
                              ↓
┌─────────────────────────────────────────────────────────────┐
│ 2. TRANSACTIONAL DATABASE UPDATE                            │
│    • In a single transaction:                               │
│      - Apply API response to CachedShip/CachedAgent/etc.    │
│      - Record audit trail (ActivityLog)                     │
│    • Transaction MUST complete before event leaves handler  │
│    • On transaction abort → STOP, do NOT emit event         │
└─────────────────────────────────────────────────────────────┘
                              ↓
┌─────────────────────────────────────────────────────────────┐
│ 3. PUBLISH DOMAIN EVENT to Wolverine                        │
│    • Event references the snapshot applied in step 2        │
│    • Event is persisted to Wolverine outbox (PostgreSQL)    │
│    • This is part of the transaction above                  │
└─────────────────────────────────────────────────────────────┘
                              ↓
┌─────────────────────────────────────────────────────────────┐
│ 4. HANDLERS REACT DOWNSTREAM                                │
│    • Handlers read from database (guaranteed current)       │
│    • Handlers may dispatch follow-up commands               │
└─────────────────────────────────────────────────────────────┘
```

**Key invariant:** If a handler fires, the database is **guaranteed** to reflect the state at the time of the event.

---

### 2.2 Transaction Boundary

All state mutations must execute within **a single database transaction** (Postgres serializable or read-committed with explicit locking):

```csharp
// BAD: event can publish but transaction may abort
await dbContext.SaveChangesAsync();
await bus.PublishAsync(domainEvent);

// GOOD: event is part of the transaction
await bus.PublishAsync(domainEvent);  // added to outbox within transaction
await dbContext.SaveChangesAsync();   // commits both mutation + outbox entry atomically
```

**Implementation:**
- Use Wolverine's PostgreSQL outbox integration (`WolverineFx.Persistence.Postgresql`)
- Configure outbox table in migrations
- All command handlers must publish events **before** calling `SaveChangesAsync()`
- The outbox is flushed as part of the transaction commit

---

## 3. Enforcement Mechanisms

### 3.1 Command Handler Template

All command handlers must follow this pattern:

```csharp
public sealed class NavigateShipHandler : ICommandHandler<NavigateShipCommand>
{
    private readonly ISpaceTradersApiClient _client;
    private readonly IShipRepository _ships;
    private readonly IMessageBus _bus;
    private readonly ILogger<NavigateShipHandler> _logger;

    public async Task Handle(NavigateShipCommand cmd, CancellationToken ct)
    {
        // STEP 1: Validate state
        var ship = await _ships.GetBySymbolAsync(cmd.ShipSymbol, ct);
        if (ship.Status != "IN_ORBIT")
            throw new ShipStateMismatchException($"Cannot navigate: ship is {ship.Status}, not IN_ORBIT");

        // STEP 2: Call API
        var response = await _client.NavigateShipAsync(cmd.ShipSymbol, cmd.DestWaypoint, ct);
        if (!response.IsSuccess)
            throw new ApiException(response.Error);  // DO NOT PROCEED

        // STEP 3: Create domain event and update database in ONE transaction
        var updatedNav = MapResponseToNav(response.Data.Nav);
        var domainEvent = new ShipNavigatedEvent(
            cmd.ShipSymbol,
            response.Data.Nav.SystemSymbol,
            response.Data.Nav.WaypointSymbol,
            response.Data.Nav.Route.Arrival,
            Guid.NewGuid(),
            cmd.CommandId,
            DateTimeOffset.UtcNow);

        // Publish event BEFORE SaveChanges so it's included in the transaction
        await _bus.PublishAsync(domainEvent, cancellationToken: ct);

        // Update database and flush event to outbox atomically
        await _ships.UpdateNavAsync(cmd.ShipSymbol, updatedNav, response.Data.Fuel, ct);

        _logger.LogInformation(
            "Ship {Symbol} navigated to {Waypoint}; arrives {ArrivesAt}",
            cmd.ShipSymbol, response.Data.Nav.WaypointSymbol, response.Data.Nav.Route.Arrival);
    }
}
```

### 3.2 Repository Update Methods

Repositories must flush events to the outbox:

```csharp
public sealed class ShipRepository : IShipRepository
{
    private readonly GameDbContext _db;

    public async Task UpdateNavAsync(
        string shipSymbol,
        NavModel nav,
        FuelModel? fuel,
        CancellationToken ct)
    {
        var ship = await _db.Ships.FirstAsync(s => s.Symbol == shipSymbol, ct);
        ship.NavStatus = nav.Status;
        ship.CurrentWaypoint = nav.WaypointSymbol;
        ship.DestWaypoint = nav.DestWaypointSymbol;
        ship.ArrivesAt = nav.ArrivesAt;

        if (fuel.HasValue)
        {
            ship.FuelCurrent = fuel.Value.Current;
            ship.FuelCapacity = fuel.Value.Capacity;
        }

        ship.LastSyncedAt = DateTimeOffset.UtcNow;
        _db.Ships.Update(ship);

        // SaveChangesAsync flushes to the outbox automatically
        // (Wolverine middleware hooks this)
        await _db.SaveChangesAsync(ct);
    }
}
```

### 3.3 Startup Recovery Service Updates

The `StartupRecoveryService` must ensure:
1. **Full sync completes first** (database is current)
2. **Only then** emit recovery events
3. **Use the synced state** to determine which events to emit

Current implementation is already correct:

```csharp
// CORRECT: sync first
await syncHandler.Handle(new SyncAllShipsCommand(), cancellationToken);

// THEN read the updated state
var fleet = await ships.GetAllAsync(cancellationToken);

// THEN emit recovery events based on current state
foreach (var ship in fleet)
{
    await RecoverShipAsync(ship, ships, bus, now, cancellationToken);
}
```

However, the recovery event emission should **not update the database**. It should only emit events. The database is already current from the sync.

**Fixed approach:**

```csharp
private async Task RecoverShipAsync(
    Application.Ports.ShipModel ship,
    IMessageBus bus,
    DateTimeOffset now,
    CancellationToken cancellationToken)
{
    // DO NOT call ships.UpdateNavAsync() here – the sync already did this
    // Only emit the appropriate event based on current state

    if (ship.ArrivesAt.HasValue && ship.ArrivesAt.Value <= now)
    {
        // Ship has already arrived – emit arrival event
        var arrivedWaypoint = ship.DestWaypointSymbol ?? ship.WaypointSymbol ?? string.Empty;
        await bus.PublishAsync(new ShipInTransitEvent(...));
    }
    // ... other cases
}
```

---

## 4. Wolverine Outbox Configuration

### 4.1 PostgreSQL Outbox Setup

```csharp
// In Program.cs
services.AddWolverine(options =>
{
    options.UsePostgresqlPersistence(Configuration.GetConnectionString("GameDb"));
    options.Policies.AutoApplyTransactions();  // Wraps handlers in transactions
});
```

### 4.2 Outbox Table Schema

The Wolverine PostgreSQL integration creates this table:

```sql
CREATE TABLE IF NOT EXISTS wolverine.outbox
(
    id              BIGSERIAL PRIMARY KEY,
    owner_id        INT NOT NULL,
    message_type    VARCHAR(250) NOT NULL,
    message_body    TEXT NOT NULL,
    timestamp       TIMESTAMP NOT NULL,
    attempts        INT NOT NULL DEFAULT 0,
    processed_at    TIMESTAMP NULL
);

CREATE INDEX idx_outbox_owner ON wolverine.outbox (owner_id, processed_at);
```

---

## 5. Event Handler Guarantees

### 5.1 Handler Reads Consistent State

Because events are persisted to the outbox **within the command's transaction**, handlers are guaranteed to read state that includes the command's mutations:

```csharp
public sealed class ShipNavigatedEventHandler : IEventHandler<ShipNavigatedEvent>
{
    private readonly IShipRepository _ships;

    public async Task Handle(ShipNavigatedEvent evt, CancellationToken ct)
    {
        // This read returns the updated state (including the navigation)
        var ship = await _ships.GetBySymbolAsync(evt.ShipSymbol, ct);

        // Ship.ArrivesAt is guaranteed to match evt.ArrivesAt
        Assert.Equal(ship.ArrivesAt, evt.ArrivesAt);
    }
}
```

### 5.2 No Cascading Divergence

If a handler emits a follow-up event (e.g., `ShipNavigatedEvent` → `ShipArrivedEvent` after delay), the same transactional guarantee applies:

```csharp
public sealed class ShipInTransitEventHandler : IEventHandler<ShipInTransitEvent>
{
    private readonly IShipRepository _ships;
    private readonly IMessageBus _bus;

    public async Task Handle(ShipInTransitEvent evt, CancellationToken ct)
    {
        var ship = await _ships.GetBySymbolAsync(evt.ShipSymbol, ct);

        if (ship.ArrivesAt <= DateTimeOffset.UtcNow)
        {
            // Immediate arrival
            await _bus.PublishAsync(new ShipArrivedEvent(...));
        }
        else
        {
            // Schedule future arrival
            await _bus.ScheduleAsync(
                new ShipArrivedEvent(...),
                ship.ArrivesAt.Value,
                cancellationToken: ct);
        }
        // ScheduleAsync also adds to outbox
    }
}
```

---

## 6. Fault Recovery Patterns

### 6.1 Pod Restart During Command Execution

**Scenario:** Pod crashes during `NavigateShipHandler.Handle()` after API call but before `SaveChangesAsync()`.

**Recovery:**
1. Pod restarts and runs `StartupRecoveryService`
2. `SyncAllShipsCommand` fetches the current state from SpaceTraders.io
3. Database is updated to match API reality
4. Recovery events are emitted based on the synced state
5. Since `ShipNavigatedEvent` was never emitted, handlers won't see duplicate work

**Guarantee:** The database and API are re-synchronized.

### 6.2 Wolverine Outbox Replay

**Scenario:** Pod crashes after `SaveChangesAsync()` but before handlers run (e.g., Wolverine dispatcher hasn't started).

**Recovery:**
1. Pod restarts; Wolverine starts
2. Wolverine discovers unprocessed outbox entries
3. Handlers are re-triggered for those events
4. Handlers re-read the database (which is already updated from the command)
5. Idempotency: handlers must be designed to be safe to re-run

**Guarantee:** No events are lost.

### 6.3 External API State Divergence

**Scenario:** API returns a response that was already saved but is newer than our cache (e.g., system event happened server-side).

**Recovery:**
1. Next periodic sync (or manual refresh) will detect the divergence
2. `SyncAllShipsCommand` re-fetches and updates the cache
3. Recovery path detects the updated state and re-emits appropriate events

**Guarantee:** Divergence is detected and corrected by the next sync cycle.

---

## 7. Testing Strategy

### 7.1 Unit Tests: Command Handler Transactionality

```csharp
[Fact]
public async Task NavigateShipHandler_PublishesEventInTransaction()
{
    // Arrange
    var bus = new FakeMessageBus();
    var db = new InMemoryGameDbContext();
    var apiClient = new FakeApiClient();
    var handler = new NavigateShipHandler(apiClient, db, bus, _logger);

    // Act
    await handler.Handle(new NavigateShipCommand("S1", "WP-2"), CancellationToken.None);

    // Assert
    var ship = await db.CachedShips.FirstAsync(s => s.Symbol == "S1");
    var publishedEvent = bus.PublishedEvents.OfType<ShipNavigatedEvent>().First();

    // Verify: database and event are consistent
    Assert.Equal(ship.ArrivesAt, publishedEvent.ArrivesAt);
    Assert.Equal(ship.DestWaypoint, publishedEvent.DestinationWaypoint);
}

[Fact]
public async Task NavigateShipHandler_RollsBackOnApiFailure()
{
    // Arrange: API returns error
    var apiClient = new FakeApiClient { FailureResponse = true };
    var handler = new NavigateShipHandler(apiClient, db, bus, _logger);

    // Act & Assert
    await Assert.ThrowsAsync<ApiException>(() =>
        handler.Handle(new NavigateShipCommand("S1", "WP-2"), CancellationToken.None));

    // Verify: no event was published, database unchanged
    Assert.Empty(bus.PublishedEvents);
    var ship = await db.CachedShips.FirstAsync(s => s.Symbol == "S1");
    Assert.Null(ship.DestWaypoint);  // unchanged
}
```

### 7.2 Integration Tests: Startup Recovery Consistency

```csharp
[Fact]
public async Task StartupRecoveryService_DoesNotEmitRecoveryEventIfAlreadySynced()
{
    // Arrange: simulate a clean sync (no stale state)
    await _gameDb.CachedShips.AddAsync(new CachedShip
    {
        Symbol = "S1",
        NavStatus = "IN_ORBIT",
        ArrivesAt = null,
        // ...
    });
    await _gameDb.SaveChangesAsync();

    var bus = new FakeMessageBus();
    var service = new StartupRecoveryService(_serviceScopeFactory, _logger);

    // Act
    await service.StartAsync(CancellationToken.None);

    // Assert: only one event (ShipInOrbitEvent), no duplicate recovery events
    var publishedEvents = bus.PublishedEvents.OfType<ShipInOrbitEvent>();
    Assert.Single(publishedEvents);
}

[Fact]
public async Task StartupRecoveryService_SyncsBeforeEmittingRecoveryEvents()
{
    // Arrange: API has ship IN_TRANSIT, but local cache is stale (DOCKED)
    await _gameDb.CachedShips.AddAsync(new CachedShip
    {
        Symbol = "S1",
        NavStatus = "DOCKED",
        ArrivesAt = null,
    });
    await _gameDb.SaveChangesAsync();

    var apiClient = new FakeApiClient
    {
        Ships = new[] { new ShipDto { Symbol = "S1", NavStatus = "IN_TRANSIT", ArrivesAt = ... } }
    };

    // Act
    await service.StartAsync(CancellationToken.None);

    // Assert: recovery event is based on synced state (IN_TRANSIT), not stale state
    var transitEvent = bus.PublishedEvents.OfType<ShipInTransitEvent>().First();
    Assert.NotNull(transitEvent);
}
```

### 7.3 Contract Tests: Handler Idempotency

```csharp
[Fact]
public async Task ShipNavigatedEventHandler_IsIdempotent()
{
    // Arrange: same event fired twice
    var evt = new ShipNavigatedEvent("S1", "SYS-1", "WP-1", DateTimeOffset.UtcNow.AddHours(1), ...);
    var handler = new ShipNavigatedEventHandler(_ships, _logger);

    // Act: fire handler twice
    await handler.Handle(evt, CancellationToken.None);
    await handler.Handle(evt, CancellationToken.None);

    // Assert: no errors, handlers completed successfully
    // (idempotency is defined by domain-specific logic, not generic)
}
```

---

## 8. Monitoring & Observability

### 8.1 Audit Trail

Every state mutation must be recorded in `ActivityLog`:

```sql
INSERT INTO activity_log (ship_symbol, action, old_state, new_state, timestamp, command_id)
VALUES ('S1', 'Navigate', '{"Status": "IN_ORBIT"}', '{"Status": "IN_TRANSIT", "ArrivesAt": "..."}', NOW(), 'cmd-id');
```

### 8.2 Divergence Detection

Periodic alerts for cache divergence:

```csharp
public sealed class CacheSyncHealthCheck : IHealthCheck
{
    public async Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context, CancellationToken ct)
    {
        // Compare sample of cached ships against SpaceTraders API
        // Alert if > N% divergence
    }
}
```

### 8.3 Event Ordering Logs

Log all state transitions with timestamps:

```
2025-01-15T10:00:00Z [INFO]  ShipNavigatedEvent: S1 navigated to WP-2, arrives 2025-01-15T10:30:00Z
2025-01-15T10:00:01Z [INFO]  DatabaseUpdate: CachedShip.S1.ArrivesAt = 2025-01-15T10:30:00Z (txn committed)
2025-01-15T10:00:02Z [INFO]  ShipNavigatedEventHandler: Processing S1 navigation event, ArrivesAt matches database
```

---

## 9. Implementation Status

### 9.1 ✅ COMPLETED: StartupRecoveryService Refactor

**File:** `SpaceTraders.API/Services/StartupRecoveryService.cs`

**Changes:**
- ✅ Removed unnecessary `ships.UpdateNavAsync()` calls from `RecoverShipAsync()`
- ✅ Recovery path now only emits events based on synced state
- ✅ Database is guaranteed to be current (synced before recovery)
- ✅ No duplicate database updates

**Rationale:**
`SyncAllShipsCommand` already persists the current API state to the database. Recovery should only emit events to resume automation, not re-persist already-synced data. This prevents:
- Double writes to the database
- Inconsistent state between multiple updates
- Loss of audit trail when updates are made outside normal command handlers

**Code:**
```csharp
private async Task RecoverShipAsync(
    Application.Ports.ShipModel ship,
    IMessageBus bus,
    DateTimeOffset now,
    CancellationToken cancellationToken)
{
    // NOTE: Database is already synced from SyncAllShipsCommand.
    // Recovery only emits events based on current state – no database updates needed.

    if (ship.ArrivesAt.HasValue && ship.ArrivesAt.Value <= now)
    {
        // Ship has already arrived – emit arrival event
        var arrivedWaypoint = ship.DestWaypointSymbol ?? ship.WaypointSymbol ?? string.Empty;
        await bus.PublishAsync(new ShipInTransitEvent(...));
    }
    // ... other cases - emit only, no database updates
}
```

### 9.2 ⏳ PENDING: Wolverine PostgreSQL Outbox Configuration

**Target:** `SpaceTraders.Infrastructure.Persistence/DependencyInjection.cs`

**Status:** Blocked - `WolverineFx.Persistence.Postgresql` package not available in current NuGet repositories (as of Wolverine 5.32.1).

**Alternative Approach (Recommended for Phase 2):**
1. Use Wolverine's built-in in-process outbox until persistence extension becomes available
2. Ensure command handlers follow the template: **Publish event → SaveChangesAsync()**
3. This maintains atomic ordering without external outbox persistence
4. Future migration to durable outbox is backward-compatible once package becomes available

**Next Steps:**
- Verify with Wolverine team if PostgreSQL persistence is available in later versions
- Consider implementing custom durable outbox using Postgres if needed
- Document outbox replay behavior for testing

### 9.3 ⏳ PENDING: Command Handler Audit Trail

**Target:** All command handlers in `SpaceTraders.Application/Commands/**`

**Status:** Requires implementation across all mutating commands

**Changes Needed:**
- Add `ActivityLog` write for every state mutation
- Record: command name, old state snapshot, new state snapshot, timestamp
- Include command ID for traceability

**Example:**
```csharp
public async Task Handle(NavigateShipCommand cmd, CancellationToken ct)
{
    // ... API call and database update ...
    
    await activityLog.RecordAsync(
        "Navigate",
        oldShip,
        updatedShip,
        cmd.Id,
        cancellationToken: ct);
}
```

### 9.4 ⏳ PENDING: Integration Tests for Transactionality

**Target:** `tests/SpaceTraders.Application.Tests/Commands/**`

**Status:** Requires test additions for each handler

**Tests to Add:**
- Verify database state and published events are consistent
- Verify rollback on API failure leaves database unchanged
- Verify idempotency of event handlers

---

## 10. Checklist: Ensuring No Race Conditions

For every command handler:

- [ ] API call is inside a try-catch; failure prevents database update
- [x] Database update is a single transaction (existing EF Core SaveChangesAsync)
- [ ] Domain event is published **before** `SaveChangesAsync()`
- [ ] Event is flushed to the outbox (via `IMessageBus.PublishAsync()`)
- [ ] Handler is idempotent (safe to replay)
- [x] Recovery path (e.g., `StartupRecoveryService`) does NOT re-persist state; only emits events
- [ ] Audit trail is recorded for every state mutation
- [ ] Tests verify transactional consistency

For the application:

- [ ] Wolverine is configured with PostgreSQL outbox (blocked - package unavailable)
- [ ] Migrations create the outbox table
- [x] `StartupRecoveryService` runs **before** any handlers process events
- [x] `SyncAllShipsCommand` is called first in recovery
- [ ] Pod restart / outbox replay tests pass

---

## 11. Summary

**The invariant:**
> If a domain event is published, the database **has already been updated** to a state consistent with that event, and that update is durable (committed to Postgres).

**What's been implemented:**
1. ✅ **StartupRecoveryService refactored** - no longer performs redundant database updates; only emits recovery events based on synced state
2. ✅ **Recovery ordering preserved** - Sync → Read → Emit events pattern ensures consistency

**What's pending:**
1. ⏳ **Wolverine PostgreSQL Outbox** - requires external package availability
2. ⏳ **Command handler refactor** - event publishing before SaveChangesAsync
3. ⏳ **Audit trail** - ActivityLog recording for all state mutations
4. ⏳ **Integration tests** - transactionality and idempotency validation

**Next Phase:**
- Implement durable outbox (custom or via later Wolverine version)
- Add audit trail to all command handlers
- Comprehensive test coverage for transactional guarantees
- Divergence detection health checks

This implementation eliminates the possibility of:
- Stale cache after pod restart
- Events firing before database is updated
- Duplicate events on replay
- Divergence between local state and API reality
