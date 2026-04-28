# Race Condition Prevention Implementation Summary

**Date:** 2025-01-15  
**Status:** Implemented safeguards documented against current code  

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

## Phase 2: ✅ Wolverine PostgreSQL durable local queues

### Status: COMPLETED

### Why This Is Needed

The outbox pattern ensures atomic:
- Database update + Event persistence

Without it:
- Event can publish before database transaction completes
- Pod crash loses event if it's not in durable storage
- But Wolverine's in-process bus still provides ordering

### Current behavior

**Wolverine's behavior in-process:**
1. Handler runs within `SaveChangesAsync()` scope
2. Events are queued to Wolverine bus
3. Handlers execute after SaveChanges completes
4. Durable local queues persist local messages in production hosts

Startup recovery remains useful because:
- `StartupRecoveryService` will re-emit recovery events on restart
- Full sync ensures database is current, so handlers can re-read state
- Idempotent handlers are safe to re-trigger

### Current implementation

`SpaceTraders.API/Program.cs` configures Wolverine with EF Core transactions and PostgreSQL-backed message persistence:

```csharp
options.UseEntityFrameworkCoreTransactions(TransactionMiddlewareMode.Eager);
options.PersistMessagesWithPostgresql(connectionString, "wolverine")
    .Enroll<SpaceTradersDbContext>()
    .EnableCommandQueues(false);
options.Policies.UseDurableLocalQueues();
```

This gives local message durability for production hosts. The configuration is disabled in the `Testing` environment.

### Remaining risk

The code still relies on command handlers being idempotent and on startup recovery re-emitting state-chain events after restarts. Durable local queues reduce lost-message risk, but do not remove the need for replay guards.

### Example custom outbox shape if needed later

```csharp
public async Task SaveOutboxEventAsync(string aggregateId, DomainEvent @event, CancellationToken cancellationToken)
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
    await _db.SaveChangesAsync(cancellationToken);
}
```

---

## Phase 3: ✅ Command Handler Audit Trail + Idempotency Hardening (Docked role-specific)

### Status: COMPLETED (current pass)

### What Was Done In This Pass

**Files Modified:**
- `SpaceTraders.Application/Events/Handlers/Ships/ShipDockedMineEventHandler.cs`
- `SpaceTraders.Application/Events/Handlers/Ships/ShipDockedTraderEventHandler.cs`
- `SpaceTraders.Application/Events/Handlers/Ships/ShipDockedScoutEventHandler.cs`
- `tests/SpaceTraders.Application.Tests/Events/Handlers/Ships/ShipDockedRoleHandlersTests.cs`

### Idempotency Change

Added replay guard to each docked role-specific handler:
- If persisted ship state is not `DOCKED` → existing mismatch/skip behavior remains
- If persisted `ship.WaypointSymbol` differs from `ShipDockedEvent.WaypointSymbol` → handler now returns `ChainOfCommandHandlerResult.Skipped()`

This prevents stale/replayed docked events from issuing commands against a newer ship location.

### New Tests Added

In `ShipDockedRoleHandlersTests`:
- `MinerDockedHandler_Skips_WhenShipWaypointDiffersFromDockedEvent`
- `TraderDockedHandler_Skips_WhenShipWaypointDiffersFromDockedEvent`
- `ScoutDockedHandler_Skips_WhenShipWaypointDiffersFromDockedEvent`

Assertions verify no role commands are issued when the docked event waypoint is stale.

### Validation

Targeted test run:
- `ShipDockedRoleHandlersTests`: **6 passed, 0 failed**

---

## Phase 4: ✅ Integration Tests

### Status: COMPLETED

### What Was Implemented In This Pass

**File Added:**
- `tests/SpaceTraders.API.Tests/Services/StartupRecoveryServiceTests.cs`

**Tests added:**
- `StartAsync_SyncsBeforeRecoveryAndEmitsBasedOnSyncedState`
- `StartAsync_WhenAutomationDisabled_SkipsRecoveryEmissionButStillSyncs`
- `StartAsync_RecoveryTransitEvent_CanBeDispatchedAgainstCurrentStateWithoutDuplicateFollowUp`
- `StartAsync_RecoveryDockedEvent_CanBeDispatchedAgainstCurrentState`
- `StartAsync_RecoveryInOrbitEvent_CanBeDispatchedAgainstCurrentState`

**Coverage provided:**
- Verifies `StartupRecoveryService` performs sync before recovery emission
- Verifies emitted recovery event uses synced ship state (not stale pre-sync state)
- Verifies automation-off mode still performs sync but emits no recovery events
- Verifies an elapsed transit recovery event can flow into the chain dispatcher and read synced state without producing duplicate in-orbit follow-up
- Verifies docked and in-orbit recovery events flow into the chain dispatcher and emit downstream events from current synced state

### Validation

Targeted test run:
- `StartupRecoveryServiceTests`: **5 passed, 0 failed**

### Outbox Replay Coverage Added

**File Added:**
- `tests/SpaceTraders.API.Tests/OutboxReplayIntegrationTests.cs`

**Test added:**
- `DurableScheduledLocalMessage_ReplaysAfterHostRestart`

**Coverage provided:**
- Verifies Wolverine PostgreSQL durable message storage persists a scheduled local envelope across host shutdown
- Verifies a restarted host replays the persisted envelope after it becomes due
- Verifies replay handles the message once, preventing duplicate downstream command behavior in restart scenarios

### Validation

Targeted test run:
- `OutboxReplayIntegrationTests.DurableScheduledLocalMessage_ReplaysAfterHostRestart`: **1 passed, 0 failed**

---

## Phase 5: ⏳ Divergence Detection Health Checks

### Status: NOT IMPLEMENTED

The current API host maps `/health/live`, `/health/ready`, and `/health/startup` with the EF Core database health check. There is no `CacheSyncHealthCheck` class and no `/health/divergence` endpoint in the current code.

### Future implementation idea

A divergence health check could sample cached ships and compare persisted status, system, waypoint, destination, and arrival time against SpaceTraders API state. This should be exposed separately from readiness so cache/API drift can alert operators without taking healthy pods out of service.

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

### ✅ Docked Role Handler Replay Safety

- Docked role handlers now verify event waypoint against persisted waypoint
- Stale/replayed docked events are skipped without issuing commands
- Prevents duplicate sell/buy/refuel/orbit actions from stale chain events

### ✅ Startup Recovery Ordering Test Coverage

- Tests verify recovery emission is based on post-sync persisted state
- Tests verify automation toggle gates emission without skipping sync
- Tests verify recovery-to-handler dispatch reads current synced state for elapsed transit, docked, and in-orbit replay

### ✅ Event Durability Configuration

- Wolverine PostgreSQL durable message storage is configured for production hosts
- Startup recovery still re-emits state-chain events on restart as a defense-in-depth recovery path

### ❌ Not Yet Implemented

- [ ] Cache/API divergence health check and alerting
- [ ] Alert manager integration or dashboard wiring for divergence incidents

---

## Next Steps

### Short-term

1. Add alerting/operational runbook for divergence incidents
2. Consolidate and standardize activity logging policy across handlers

---

## Risks Mitigated

| Risk | Pre-Implementation | Post-Implementation |
|------|-------------------|---------------------|
| Cache ≠ API after crash | ❌ High | ✅ Low (sync re-corrects) |
| Event fires with stale data | ❌ Medium | ✅ Low (recovery reads synced state) |
| Double state mutations | ❌ High | ✅ Eliminated |
| Replay of stale docked event causes duplicate role commands | ❌ Medium | ✅ Low (waypoint replay guard) |
| Recovery emits from stale pre-sync state | ❌ Medium | ✅ Low (covered by tests) |
| Recovery chain handlers use stale state | ❌ Medium | ✅ Low (covered for transit/docked/in-orbit recovery paths) |
| Divergence undetected | ❌ High | ⚠️ Medium (sync/recovery mitigates drift, but no dedicated divergence check exists) |
| Pod restart loses work | ⚠️ Medium | ✅ Low (durable outbox replay coverage added) |

---

## Files Changed

- ✅ `SpaceTraders.API/Services/StartupRecoveryService.cs` - Recovery path refactored
- ✅ `SpaceTraders.Application/Events/Handlers/Ships/ShipDockedMineEventHandler.cs` - Added docked waypoint replay guard
- ✅ `SpaceTraders.Application/Events/Handlers/Ships/ShipDockedTraderEventHandler.cs` - Added docked waypoint replay guard
- ✅ `SpaceTraders.Application/Events/Handlers/Ships/ShipDockedScoutEventHandler.cs` - Added docked waypoint replay guard
- ✅ `tests/SpaceTraders.Application.Tests/Events/Handlers/Ships/ShipDockedRoleHandlersTests.cs` - Added replay/idempotency coverage for docked role handlers
- ✅ `tests/SpaceTraders.API.Tests/Services/StartupRecoveryServiceTests.cs` - Added recovery ordering, automation-gating, and recovery-to-handler integration tests
- ✅ `tests/SpaceTraders.API.Tests/OutboxReplayIntegrationTests.cs` - Added durable scheduled local message replay coverage across host restart
- ✅ `SpaceTraders.API/Program.cs` - Configures Wolverine EF Core transactions and PostgreSQL durable local queues

