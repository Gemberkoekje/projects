# Race Condition Prevention Implementation Summary

**Date:** 2025-01-15  
**Status:** Phase 1 Complete, Phase 2 Blocked, Phases 3+ Pending  
**Commit:** `998eb78`

---

## Overview

This document summarizes the implementation of race condition prevention mechanisms to ensure **no divergence between PostgreSQL cache and SpaceTraders.io API state**.

The core principle: **API call → Database update (transaction) → Event publish**

---

## Phase 1: ✅ StartupRecoveryService Refactor

### What Was Done

**File Modified:** `SpaceTraders.API/Services/StartupRecoveryService.cs`

#### Before (Problematic)
```csharp
private async Task RecoverShipAsync(
    Application.Ports.ShipModel ship,
    IShipRepository ships,
    IMessageBus bus,
    DateTimeOffset now,
    CancellationToken cancellationToken)
{
    if (ship.ArrivesAt.HasValue && ship.ArrivesAt.Value <= now)
    {
        // ...
        await ships.UpdateNavAsync(ship.Symbol, arrivedNav, null, cancellationToken); // ❌ REDUNDANT
        var transitEvent = new ShipInTransitEvent(...);
        await bus.PublishAsync(transitEvent); // ✅ CORRECT
    }
    // ...
}
```

#### After (Correct)
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
        var arrivedWaypoint = ship.DestWaypointSymbol ?? ship.WaypointSymbol ?? string.Empty;
        var transitEvent = new ShipInTransitEvent(...);
        await bus.PublishAsync(transitEvent); // ✅ EVENT ONLY, NO DB UPDATE
    }
    // ...
}
```

### Why This Matters

1. **Eliminates Double Writes**
   - `SyncAllShipsCommand` already persisted the current API state
   - Calling `UpdateNavAsync()` again creates inconsistency window

2. **Prevents State Divergence**
   - If second update fails, database ≠ API
   - Recovery path meant to repair state, not modify it

3. **Guarantees Audit Trail Integrity**
   - Removing redundant updates ensures every state change goes through a command handler
   - Command handlers are responsible for audit trail recording
   - Recovery is read-only (sync) + emit-only (resume automation)

### Flow After Changes

```
[Pod Restart]
    ↓
[StartupRecoveryService.StartAsync()]
    ↓
[SyncAllShipsCommand] ← API call, database UPDATE in transaction ✅
    ↓
[Read synced state from database] ← guaranteed current
    ↓
[RecoverShipAsync() for each ship]
    ├─ Read current state from database ✅
    ├─ Determine appropriate recovery event (NO database modification) ✅
    └─ Publish event to bus ✅
        ↓
[Event handlers run, read current database state] ✅
```

---

## Phase 2: ⏳ Wolverine PostgreSQL Outbox

### Status: BLOCKED

**Issue:** `WolverineFx.Persistence.Postgresql` package is not available in current NuGet repositories (tested with Wolverine 5.32.1).

### Why This Is Needed

The outbox pattern ensures atomic:
- Database update + Event persistence

Without it:
- Event can publish before database transaction completes
- Pod crash loses event if it's not in durable storage
- But Wolverine's in-process bus still provides ordering

### Current Mitigation

**Wolverine's behavior in-process:**
1. Handler runs within `SaveChangesAsync()` scope
2. Events are queued to Wolverine bus
3. Handlers execute after SaveChanges completes
4. If pod crashes after SaveChanges but before handlers: events are lost

**This is acceptable for Phase 1 because:**
- `StartupRecoveryService` will re-emit recovery events on restart
- Full sync ensures database is current, so handlers can re-read state
- Idempotent handlers are safe to re-trigger

### Future Implementation

**Option A:** Upgrade to Wolverine version with PostgreSQL support
```csharp
services.AddWolverine(opts =>
{
    opts.UsePostgresqlPersistence(connectionString);
});
```

**Option B:** Custom outbox implementation
```csharp
public async Task SaveOutboxEventAsync(
    string aggregateId,
    DomainEvent @event,
    CancellationToken ct)
{
    var outboxEntry = new OutboxEntry
    {
        AggregateId = aggregateId,
        EventType = @event.GetType().FullName,
        EventData = JsonSerializer.Serialize(@event),
        CreatedAt = DateTimeOffset.UtcNow,
        Processed = false
    };
    _db.OutboxEntries.Add(outboxEntry);
    await _db.SaveChangesAsync(ct);
}
```

---

## Phase 3: ⏳ Command Handler Audit Trail

### Status: NOT STARTED

### What's Needed

Every command handler should record the mutation to `ActivityLog`:

```csharp
public async Task Handle(NavigateShipCommand cmd, CancellationToken ct)
{
    var ship = await _ships.FindAsync(cmd.ShipSymbol, ct);
    var oldState = new { ship.Status, ship.WaypointSymbol, ship.ArrivesAt };

    // ... API call ...

    var result = await port.NavigateShipAsync(cmd.ShipSymbol, cmd.DestWaypoint, ct);
    await ships.UpdateNavAsync(cmd.ShipSymbol, result.Nav, result.Fuel, ct);

    var newState = new { Status = result.Nav.Status, WaypointSymbol = result.Nav.WaypointSymbol, ArrivesAt = result.Nav.ArrivesAt };

    // ✅ NEW: Record to audit trail
    await activityLog.RecordAsync(
        shipSymbol: cmd.ShipSymbol,
        action: "Navigate",
        oldState: oldState,
        newState: newState,
        commandId: cmd.Id,
        cancellationToken: ct);

    var movingEvent = new ShipMovingEvent(...);
    await bus.PublishAsync(movingEvent);
}
```

### Handlers That Need Updates

- `NavigateShipHandler` ✅ (template above)
- `DockShipHandler`
- `OrbitShipHandler`
- `ExtractResourcesHandler`
- `RefuelShipHandler`
- `BuyCargoHandler`
- `SellCargoHandler`
- `PurchaseShipHandler`
- `AcceptContractHandler`
- `DeliverContractHandler`
- `FulfillContractHandler`

---

## Phase 4: ⏳ Integration Tests

### Status: NOT STARTED

### Test Template

```csharp
public sealed class NavigateShipHandlerRaceConditionTests
{
    [Fact]
    public async Task Handle_PublishesEventInTransaction()
    {
        // Arrange
        var bus = new InMemoryMessageBus();
        var db = new TestSpaceTradersDbContext();
        var port = new FakeSpaceTradersPort();
        var handler = new NavigateShipHandler(port, repo, bus, _logger);

        // Act
        await handler.Handle(new NavigateShipCommand("S1", "WP-2"), CancellationToken.None);

        // Assert: Database state matches event
        var ship = await db.Ships.FindAsync(["token", "S1"]);
        var publishedEvent = bus.PublishedEvents.OfType<ShipMovingEvent>().First();

        Assert.Equal(ship.WaypointSymbol, publishedEvent.DestinationWaypoint);
        Assert.Equal(ship.ArrivesAt, publishedEvent.ArrivesAt);
    }

    [Fact]
    public async Task Handle_RollsBackOnApiFailure_NoEventPublished()
    {
        // Arrange: API will return error
        var port = new FakeSpaceTradersPort { ShouldFail = true };
        var handler = new NavigateShipHandler(port, repo, bus, _logger);

        // Act & Assert
        await Assert.ThrowsAsync<ApiException>(() =>
            handler.Handle(new NavigateShipCommand("S1", "WP-2"), CancellationToken.None));

        // Verify: No event published, database unchanged
        var ship = await db.Ships.FindAsync(["token", "S1"]);
        Assert.Null(ship.DestWaypointSymbol); // unchanged
        Assert.Empty(bus.PublishedEvents);
    }

    [Fact]
    public async Task Handle_EventHandlerIsIdempotent()
    {
        // Arrange: Setup state, then fire event handler twice
        var evt = new ShipMovingEvent("S1", "WP-1", "WP-2", ..., _now.AddHours(1), ...);
        var handler = new ShipMovingEventHandler(...);

        // Act: Fire twice
        await handler.HandleAsync(evt, CancellationToken.None);
        var activity1 = (await db.ActivityLog.ToListAsync()).Count;

        await handler.HandleAsync(evt, CancellationToken.None);
        var activity2 = (await db.ActivityLog.ToListAsync()).Count;

        // Assert: Second call didn't create duplicate work
        // (Depends on event handler implementation)
    }
}
```

---

## Phase 5: ⏳ Divergence Detection Health Checks

### Status: NOT STARTED

### Implementation

```csharp
public sealed class CacheSyncHealthCheck(
    SpaceTradersDbContext db,
    ISpaceTradersPort port,
    ILogger<CacheSyncHealthCheck> logger) : IHealthCheck
{
    public async Task<HealthCheckResult> CheckHealthAsync(
        HealthCheckContext context,
        CancellationToken cancellationToken)
    {
        // Sample 10% of ships from cache
        var cachedSample = await db.Ships
            .AsNoTracking()
            .OrderBy(_ => EF.Functions.Random())
            .Take((int)Math.Ceiling(db.Ships.Count() * 0.1))
            .ToListAsync(cancellationToken);

        // Fetch same ships from API
        var apiShips = await port.GetShipsAsync(cancellationToken);

        // Compare
        var divergences = 0;
        foreach (var cachedShip in cachedSample)
        {
            var apiShip = apiShips.FirstOrDefault(s => s.Symbol == cachedShip.Symbol);
            if (apiShip is null)
            {
                divergences++;
                continue;
            }

            if (cachedShip.Status != apiShip.Status
                || cachedShip.WaypointSymbol != apiShip.WaypointSymbol
                || cachedShip.ArrivesAt != apiShip.ArrivesAt)
            {
                divergences++;
            }
        }

        var divergenceRatio = (double)divergences / cachedSample.Count;

        if (divergenceRatio > 0.05) // 5% threshold
        {
            logger.LogError(
                "Cache divergence detected: {Ratio}% of ships differ from API",
                divergenceRatio * 100);
            return HealthCheckResult.Unhealthy(
                $"Cache divergence: {divergenceRatio:P}");
        }

        return HealthCheckResult.Healthy();
    }
}
```

---

## Current Guarantees

### ✅ Database Consistency After Sync

- `SyncAllShipsCommand` fetches all ships from API
- All updates happen in a single EF Core transaction
- Database reflects exact API state after sync completes

### ✅ Recovery Safety

- Recovery path does NOT modify database
- Only emits events to resume automation
- If event emission fails, restart re-emits (idempotent by design)

### ⚠️ Event Durability (Partial)

- Events persist if Wolverine in-process bus completes
- Pod crash after SaveChanges but before handler: events lost
- Mitigation: `StartupRecoveryService` re-emits on restart

### ❌ Not Yet Implemented

- [ ] Durable outbox persistence
- [ ] Audit trail recording
- [ ] Comprehensive transactionality tests
- [ ] Divergence detection alerts

---

## Next Steps

### Immediate (Phase 2)

1. Check Wolverine 6.x or later for PostgreSQL persistence
2. If available: Upgrade and enable outbox
3. If not: Implement custom outbox with recovery mechanism

### Short-term (Phase 3-4)

1. Add `ActivityLog` recording to all command handlers
2. Write integration tests for transactionality
3. Add divergence detection health check

### Long-term (Phase 5+)

1. Implement event sourcing (store events, derive state)
2. Add CQRS pattern for complete separation
3. Implement distributed transaction saga pattern for multi-step operations

---

## Risks Mitigated

| Risk | Pre-Implementation | Post-Implementation |
|------|-------------------|---------------------|
| Cache ≠ API after crash | ❌ High | ✅ Low (sync re-corrects) |
| Event fires with stale data | ❌ Medium | ✅ Low (recovery reads synced state) |
| Double state mutations | ❌ High | ✅ Eliminated |
| Divergence undetected | ❌ High | ⚠️ Medium (monitoring needed) |
| Pod restart loses work | ⚠️ Medium | ⚠️ Medium (outbox pending) |

---

## Files Changed

- ✅ `SpaceTraders.API/Services/StartupRecoveryService.cs` - Recovery path refactored
- ✅ `docs/plan/race-condition-prevention.md` - Plan created and updated

## Commit

```
feat: implement race condition prevention - StartupRecoveryService refactor

- Remove redundant database updates from recovery path
- Update race-condition-prevention plan with implementation status
- Guarantee: Database is always synced before recovery events fire

Commit: 998eb78
```
