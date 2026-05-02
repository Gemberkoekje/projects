# Refactor Plan: Remove `ShipAutomationTickEvent` and Adopt Clean Orchestrator → Assignment → Ship Model

**Objective:** Eliminate the synthetic "wake up ship" pub/sub event layer and establish a clean, direct call flow from delayed state-change events (`ShipArrivedEvent`, `ShipCooldownExpiredEvent`) to ship goal execution.

**Status:** ✅ Complete (mandatory steps 1–5 implemented and all tests passing)
**Priority:** High — enables correct orchestrator-driven architecture
**Scope:** Application layer + Domain events

---

## Current State (What We're Undoing)

### The Problem

`ShipAutomationTickEvent` is a middleman pub/sub event that sits between real triggers and the goal executor. It's published from **5 sources**, all of which collapse to a single handler that just calls `IShipGoalExecutorService.ExecuteAsync(shipSymbol)`.

| Publisher | What it Represents | Issue |
|---|---|---|
| `ShipArrivedEventHandler` | Real delayed event (ship arrived at destination) | ✓ Legitimate, but uses pub/sub for a simple call |
| `ShipCooldownExpiredEventHandler` | Real delayed event (action cooldown expired) | ✓ Legitimate, but uses pub/sub for a simple call |
| `GameLoopService.ApplyDeadReckoningAsync` | Recovery: scheduler missed an arrival | ✓ Safety net, acceptable overhead |
| `OrbitShipCommand` "Undocked" tick | Spurious continuation after instant orbit | ✗ Unnecessary — orbit is synchronous, executor continues inline |
| `ShipStateMismatchPublisher` | Recovery: ship state diverged from API | ✗ Unnecessary — should return `Blocked` instead |

### Why This Is Wrong

1. **Extra indirection:** Event published → handler subscribes → calls executor. Should be direct call.
2. **Timing fragility:** Pub/sub introduces queue delays; direct calls are deterministic.
3. **Confuses architecture:** Makes it look like ships have multiple independent wake-up paths when they really don't.
4. **Tests obscured:** Executors tested as if they loop via ticks, but they shouldn't.

---

## Target Model (Matches Your Mental Model)

```
[FleetOrchestrator]  (BackgroundService runs every ~5 seconds)
       │
       │  evaluates idle ships against priority goals
       │  calls AssignShipToGoalCommand for promising idle ships
       ▼
[Assignment Layer]   (AssignShipToGoalCommandHandler + AssignmentResolvers)
       │
       │  translates "deliver 100 Bauxite" → specific ship X, asteroid Y
       │  creates and sets active ShipGoal via IShipGoalRepository
       ▼
[Ship Goal Step]     (IShipGoalExecutorService.ExecuteAsync calls IShipGoalExecutor)
       │
       │  SINGLE synchronous execution step:
       │    - if docked: orbit (instant) → navigate (delayed) → return WaitingForArrival
       │    - if in transit: check arrival → continue if arrived
       │    - if ready to act: extract/siphon/sell (delayed) → return WaitingForCooldown
       │    - if goal satisfied: return Completed
       │  
       │  schedules exactly ONE delayed wake-up (or none)
       ▼
[Ship Goes Dormant]  (IShipEventScheduler holds the wake-up)
       │
       │  at scheduled time, ShipEventScheduler fires:
       │    - ShipArrivedEvent (when ArrivesAt elapses)
       │    - ShipCooldownExpiredEvent (when cooldown elapses)
       ▼
[Direct Executor Call] (event handler → IShipGoalExecutorService.ExecuteAsync)
       │
       │  NO pub/sub middleman
       │  NO synthetic ticks
       ▼
[Goal Step Repeats]
```

### Event Model: What Remains

Only **two** ship-level delayed events:
- `ShipArrivedEvent` (fired by `IShipEventScheduler` when `ship.ArrivesAt` elapses)
- `ShipCooldownExpiredEvent` (fired by `IShipEventScheduler` when action cooldown elapses)

Plus **global events** (which feed the orchestrator, not ships):
- `MarketPriceChangedEvent` (invalidates trade scores, triggers orchestrator re-eval)
- `CreditsChangedEvent` (invalidates budget, triggers orchestrator re-eval)
- `ShipFuelLowEvent` (diagnostic; allows quick action on low fuel)

---

## Detailed Steps

### ✅ Step 1: Audit and Fix Executors

**Goal:** Ensure each `IShipGoalExecutor.ExecuteStepAsync` can complete all synchronous work in one call, up to the next *delayed* boundary.

**Context:** Some executors currently do work in multiple steps because they relied on re-ticks:
- `Progressing` result after orbit → relied on a tick to continue
- `Progressing` result after docking → relied on a tick to continue

**Action:** For each executor in `SpaceTraders.Application/Goals/Executors/`:

1. Trace the logic: what is the minimal work to do before hitting a delay (navigate/extract/cooldown)?
2. If the executor returns `Progressing`, check: is this only because a tick will re-enter the same function?
3. If yes, **inline the continuation**. Example:

   **Before:**
   ```csharp
   if (ship.LocalStatus == ShipLocalStatus.Docked)
   {
       await docked.OrbitAsync(ship.Symbol, cancellationToken);
       return GoalExecutionResult.Progressing("Orbited, waiting for tick to navigate");
   }

   if (ship.LocalStatus == ShipLocalStatus.InOrbit && ship.WaypointSymbol != target)
   {
       await inOrbit.NavigateAsync(ship.Symbol, target, cancellationToken);
       return GoalExecutionResult.WaitingForArrival("Navigating to target", ship.ArrivesAt);
   }
   ```

   **After:**
   ```csharp
   if (ship.LocalStatus == ShipLocalStatus.Docked)
   {
       await docked.OrbitAsync(ship.Symbol, cancellationToken);
       // Continue immediately; orbit is instant
       ship = await ships.FindAsync(ship.Symbol, cancellationToken); // Reload nav state
   }

   if (ship.LocalStatus == ShipLocalStatus.InOrbit && ship.WaypointSymbol != target)
   {
       await inOrbit.NavigateAsync(ship.Symbol, target, cancellationToken);
       ship = await ships.FindAsync(ship.Symbol, cancellationToken); // Reload nav state
       return GoalExecutionResult.WaitingForArrival("Navigating to target", ship.ArrivesAt);
   }
   ```

4. **Audit each executor:**
   - `ScoutWaypointGoalExecutor` — likely has orbit→navigate split
   - `MoveToWaypointGoalExecutor` — likely has docked→orbited split
   - `MineResourceGoalExecutor` — likely has docked→orbited split
   - `SellCargoGoalExecutor` — likely has docked→orbited split
   - `DeliverCargoGoalExecutor` — likely has docked→orbited split
   - `SiphonResourceGoalExecutor` — likely has docked→orbited split
   - `SupplyConstructionGoalExecutor` — likely has docked→orbited split
   - `PatrolMarketGoalExecutor` — likely has docked→orbited split

5. **Tests:** Add assertions that each executor does NOT return `Progressing` after calling an instant command. Example test for `ScoutWaypoint`:
   ```csharp
   [Fact]
   public async Task ExecuteStepAsync_DockedAtTarget_Orbits_Then_Continues_In_One_Call()
   {
       // Setup: ship docked, at waypoint, no cargo
       // Execute
       var result = await executor.ExecuteStepAsync(ship, goal, ctx, ct);
       // Assert: result is either WaitingForArrival (if navigated)
       // or Completed (if already had data), NOT Progressing
       Assert.NotEqual(GoalExecutionOutcome.Progressing, result.Outcome);
   }
   ```

**Files to modify:**
- `SpaceTraders.Application/Goals/Executors/*.cs` (all executor implementations)
- `tests/SpaceTraders.Application.Tests/Goals/*ExecutorTests.cs` (update assertions)

**Build & Test:** After this step, build and run executor tests. They should still green (semantics unchanged, only inlined).

---

### ✅ Step 2: Remove Tick Publishes from Command Handlers

**Goal:** Stop publishing `ShipAutomationTickEvent` after instant commands complete.

**Action:**

1. **`OrbitShipCommand`** (`SpaceTraders.Application/Commands/Ships/OrbitShipCommand.cs`):
   - Remove the "Undocked" `PublishAsync(new ShipAutomationTickEvent(...))` block.
   - Remove the `SuppressContinuationTick` field entirely (no longer needed).
   - Keep the orbit logic; just don't publish a tick after.
   - **Why:** Orbit is instant; the calling executor (if one) continues inline. External code that issues orbit should handle what happens next.

   **Before:**
   ```csharp
   if (!command.SuppressContinuationTick)
   {
       await bus.PublishAsync(new ShipAutomationTickEvent(
           command.ShipSymbol,
           "Undocked",
           publishedAt,
           Guid.NewGuid(),
           Guid.Empty));
   }
   ```

   **After:** (nothing)

2. **`DockShipCommand`** (`SpaceTraders.Application/Commands/Ships/DockShipCommand.cs`):
   - Check if it publishes `ShipAutomationTickEvent` after docking succeeds.
   - If yes, remove it (dock is instant).
   - Keep or remove `SuppressContinuationTick` based on usage.

3. **`ShipStateMismatchPublisher`** (`SpaceTraders.Application/Commands/Ships/ShipStateMismatchPublisher.cs`):
   - Remove the `PublishAsync(new ShipAutomationTickEvent(...))` call.
   - Keep the `PublishAsync(new ShipStateMismatchEvent(...))` for diagnostics.
   - **Why:** Mismatch is a bug; it should bubble up as a blocked goal on the next orchestrator tick, not re-poke the ship immediately.

4. **Update associated tests:**
   - In `SpaceTraders.Application.Tests/Commands/ShipCommandResultTests.cs`:
     - Remove tests like `OrbitShip_WhenAccepted_PublishesShipAutomationTickEvent`.
     - Keep tests for orbit succeeding; just drop the tick assertion.
   - In `SpaceTraders.Application.Tests/Commands/Phase65CommandHandlerTests.cs`:
     - Remove tests like `OrbitShip_WhenNotDocked_AlsoPublishesAutomationTick`.
     - Drop any `SuppressContinuationTick` assertions.

**Files to modify:**
- `SpaceTraders.Application/Commands/Ships/OrbitShipCommand.cs`
- `SpaceTraders.Application/Commands/Ships/DockShipCommand.cs`
- `SpaceTraders.Application/Commands/Ships/ShipStateMismatchPublisher.cs`
- `tests/SpaceTraders.Application.Tests/Commands/ShipCommandResultTests.cs`
- `tests/SpaceTraders.Application.Tests/Commands/Phase65CommandHandlerTests.cs`

**Build & Test:** Build and run command tests. Tick-related assertions should be removed; command success/failure assertions remain green.

---

### ✅ Step 3: Convert Event Handlers to Direct Service Calls

**Goal:** Replace pub/sub of `ShipAutomationTickEvent` with direct calls to `IShipGoalExecutorService`.

#### 3a. `ShipArrivedEventHandler`

**File:** `SpaceTraders.Application/Events/Handlers/Ships/ShipArrivedEventHandler.cs`

**Current:**
```csharp
await bus.PublishAsync(new ShipAutomationTickEvent(
    @event.ShipSymbol,
    "Arrived",
    @event.OccurredAt,
    @event.EventId,
    Guid.Empty));
```

**Change to:**
```csharp
var logger = scope.ServiceProvider.GetRequiredService<ILogger<ShipArrivedEventHandler>>();
var goalExecutor = scope.ServiceProvider.GetRequiredService<IShipGoalExecutorService>();

var result = await goalExecutor.ExecuteAsync(@event.ShipSymbol, cancellationToken);

if (result is not null)
{
    logger.LogInformation(
        "ShipArrivedEventHandler: ship {Ship} resumed after arrival; outcome={Outcome} reason={Reason}",
        @event.ShipSymbol,
        result.Outcome,
        result.Reason);
}
```

#### 3b. `ShipCooldownExpiredEventHandler`

**File:** `SpaceTraders.Application/Events/Handlers/Ships/ShipCooldownExpiredEventHandler.cs`

**Current:**
```csharp
await bus.PublishAsync(new ShipAutomationTickEvent(
    @event.ShipSymbol,
    "CooldownExpired",
    @event.OccurredAt,
    @event.EventId,
    Guid.Empty));
```

**Change to:**
```csharp
var logger = scope.ServiceProvider.GetRequiredService<ILogger<ShipCooldownExpiredEventHandler>>();
var goalExecutor = scope.ServiceProvider.GetRequiredService<IShipGoalExecutorService>();

var result = await goalExecutor.ExecuteAsync(@event.ShipSymbol, cancellationToken);

if (result is not null)
{
    logger.LogInformation(
        "ShipCooldownExpiredEventHandler: ship {Ship} resumed after cooldown; outcome={Outcome} reason={Reason}",
        @event.ShipSymbol,
        result.Outcome,
        result.Reason);
}
```

**Update tests:**

- `ShipArrivedEventHandlerTests.cs`:
  ```csharp
  [Fact]
  public async Task Handle_WhenGoalIdMatches_CallsGoalExecutor()
  {
      var goalExecutor = Substitute.For<IShipGoalExecutorService>();
      var handler = new ShipArrivedEventHandler(goals, logger);

      await handler.Handle(new ShipArrivedEvent(...), goalExecutor, cancellationToken);

      await goalExecutor.Received(1).ExecuteAsync("SHIP-1", cancellationToken);
  }
  ```

- `ShipCooldownExpiredEventHandlerTests.cs`: same pattern.

**Files to modify:**
- `SpaceTraders.Application/Events/Handlers/Ships/ShipArrivedEventHandler.cs`
- `SpaceTraders.Application/Events/Handlers/Ships/ShipCooldownExpiredEventHandler.cs`
- `tests/SpaceTraders.Application.Tests/Events/Handlers/Ships/ShipArrivedEventHandlerTests.cs`
- `tests/SpaceTraders.Application.Tests/Events/Handlers/Ships/ShipCooldownExpiredEventHandlerTests.cs`

**Build & Test:** Build and run event handler tests. They should pass with the new assertions.

---

### ✅ Step 4: Convert Dead-Reckoning to Direct Service Call

**Goal:** Replace the synthetic tick publish in `GameLoopService` with a direct executor call.

**File:** `SpaceTraders.Application/Automation/GameLoopService.cs`

**Current (in `ApplyDeadReckoningAsync`):**
```csharp
await bus.PublishAsync(new ShipAutomationTickEvent(
    ship.Symbol,
    "Arrived",
    now,
    Guid.NewGuid(),
    Guid.Empty));

logger.LogInformation(
    "Ship {Symbol} arrived at {Waypoint} (dead-reckoning); emitting ShipAutomationTickEvent.",
    ship.Symbol,
    arrivedWaypoint);
```

**Change to:**
```csharp
var goalExecutor = scope.ServiceProvider.GetRequiredService<IShipGoalExecutorService>();

var result = await goalExecutor.ExecuteAsync(ship.Symbol, cancellationToken);

logger.LogInformation(
    "Ship {Symbol} arrived at {Waypoint} (dead-reckoning); executed goal step (outcome={Outcome}, reason={Reason}).",
    ship.Symbol,
    arrivedWaypoint,
    result?.Outcome,
    result?.Reason);
```

**Rationale:**
- Dead-reckoning is a safety net: if the in-memory scheduler misses an arrival (e.g., process restart before reload), the 5-second tick catches it.
- With direct calls, there's no pub/sub queue delay — the ship executes immediately.
- Consider: once `ShipEventScheduler` is proven reliable (persisted, reloads correctly), this dead-reckoning block might become obsolete. But keep it for now as insurance.

**Files to modify:**
- `SpaceTraders.Application/Automation/GameLoopService.cs`

**Build & Test:** Build and run game loop service tests.

---

### ✅ Step 5: Delete the Now-Unreferenced `ShipAutomationTickEvent` Type and Handler

**Goal:** Remove the dead event type and handler since nothing publishes or consumes it anymore.

**Action:**

1. Delete `SpaceTraders.Domain/Events/Ships/ShipAutomationTickEvent.cs`.
2. Delete `SpaceTraders.Application/EventHandlers/ShipAutomationTickEventHandler.cs`.
3. Delete `tests/SpaceTraders.Application.Tests/EventHandlers/ShipAutomationTickEventHandlerTests.cs`.

**Validation:**
- Build should fail if any code still references `ShipAutomationTickEvent`.
- This is intentional — the compile failure tells you if you missed a publisher/subscriber.

**Files to remove:**
- `SpaceTraders.Domain/Events/Ships/ShipAutomationTickEvent.cs`
- `SpaceTraders.Application/EventHandlers/ShipAutomationTickEventHandler.cs`
- `tests/SpaceTraders.Application.Tests/EventHandlers/ShipAutomationTickEventHandlerTests.cs`

**Build & Test:** Build should now pass with zero references to the deleted type.

---

### ⬜ Step 6: (Optional) Wire Global Events to Orchestrator

**Goal:** Ensure that global state-change events (`MarketPriceChangedEvent`, `CreditsChangedEvent`) trigger orchestrator re-evaluation rather than poking ships directly.

**Current flow (likely):**
- Market price changes → ship ticks fired → ships re-plan trades

**Target flow:**
- Market price changes → invalidate trade cache → trigger orchestrator tick
- Orchestrator re-evaluates idle ships with new trade data
- Ships get fresh assignments if trade opportunity changes

**Action:**

1. Add handlers for global events (if they don't exist):
   - `MarketPriceChangedEventHandler`: invalidate `ITradeAnalyzer` cache, call `orchestrator.EvaluateAndAssignAsync()`.
   - `CreditsChangedEventHandler`: invalidate budget cache, call `orchestrator.EvaluateAndAssignAsync()`.

2. Remove any handlers that were waking individual ships on these events.

3. Ensure these handlers only invalidate state and trigger orchestrator, never ship-level code.

**Files to create/modify:**
- `SpaceTraders.Application/Events/Handlers/Global/MarketPriceChangedEventHandler.cs` (new)
- `SpaceTraders.Application/Events/Handlers/Global/CreditsChangedEventHandler.cs` (new)
- Remove or rewrite any handlers that used to wake ships

**Build & Test:** Verify orchestrator is re-evaluated on global events; no individual ship wakes.

---

### ⬜ Step 7: (Optional) Eliminate `GoalExecutionOutcome.Progressing`

**Goal:** After inlining executor continuations, `Progressing` becomes unnecessary.

**Rationale:**
- Every executor must reach a boundary: `Completed`, `Blocked`, `WaitingForArrival`, or `WaitingForCooldown`.
- `Progressing` only existed to signal "come back and run me again soon," but we no longer do that.
- Removing it makes the contract explicit: **each executor call takes exactly one step, then schedules a wake-up or completes.**

**Action:**

1. Audit all executors; ensure none return `Progressing`.
2. Update `GoalExecutionOutcome` enum to remove `Progressing`.
3. Update `ShipGoalExecutorService.HandleResultAsync` — remove the `Progressing` case.
4. Update tests to assert **only** the four outcomes.

**Files to modify:**
- `SpaceTraders.Domain/Goals/GoalExecutionOutcome.cs`
- `SpaceTraders.Application/Goals/IShipGoalExecutor.cs` (update documentation)
- `SpaceTraders.Application/Goals/ShipGoalExecutorService.cs`
- All executor implementations (`SpaceTraders.Application/Goals/Executors/*.cs`)
- All executor tests (`tests/SpaceTraders.Application.Tests/Goals/*ExecutorTests.cs`)

**Build & Test:** Build and run full test suite. All tests should pass with the contracted outcome set.

---

## Testing Strategy

### Unit Tests: Per Step

| Step | What to Test | Assertions |
|---|---|---|
| 1 | Executors inline continuations | No `Progressing` after orbit/dock; direct navigation/action in one call |
| 2 | Command handlers don't publish ticks | `bus.PublishAsync` never called with `ShipAutomationTickEvent` |
| 3 | Event handlers call executor directly | `goalExecutor.ExecuteAsync` called exactly once per event |
| 4 | Dead-reckoning uses executor | `goalExecutor.ExecuteAsync` called on arrival detection |
| 5 | Type deletion | Compilation succeeds; no dangling references |
| 6 | Global event handlers | Orchestrator called on `MarketPriceChangedEvent` / `CreditsChangedEvent` |
| 7 | Outcome enum change | No code references `Progressing` |

### Integration Tests: End-to-End Flow

**Scenario 1: Scout waypoint, no tick loop**
```gherkin
Given ship is docked at starting waypoint
And scout goal is assigned
When goal executor runs
Then:
  - Ship orbits (instant, no tick)
  - Ship navigates to target
  - Arrival event is scheduled
  - No ShipAutomationTickEvent is published
```

**Scenario 2: Arrival triggers execution**
```gherkin
Given ship is in transit with arrival event scheduled
When ShipArrivedEvent fires
Then:
  - Goal executor is called directly (no pub/sub)
  - If goal satisfied, executor returns Completed
  - If more work needed, executor schedules next event
  - No synthetic ticks published
```

**Scenario 3: Orchestrator reassigns on market change**
```gherkin
Given trade route becomes unprofitable
When MarketPriceChangedEvent fires
Then:
  - Trade cache is invalidated
  - Orchestrator.EvaluateAndAssignAsync is called
  - New goals assigned to idle ships
  - Busy ships are not interrupted
```

---

## Risk Mitigation

| Risk | Mitigation |
|---|---|
| Executor logic becomes complex during inlining | Pair-program step 1; review each executor carefully |
| Missing a publisher of `ShipAutomationTickEvent` | Compile failure in step 5 catches it; review build error carefully |
| Dead-reckoning becomes a bottleneck | Monitor; if `ShipEventScheduler` is reliable, delete dead-reckoning in a follow-up PR |
| Tests break during refactor | Each step is small and builds cleanly; run tests after each step |
| Orchestrator tick interval is too long | If 5s is insufficient, adjust `DeadReckoningInterval` in `GameLoopService` |

---

## Success Criteria

- [x] All 412 application tests pass (4 skipped are DB-dependent integration tests).
- [x] Zero references to `ShipAutomationTickEvent` remain.
- [x] No `Progressing` outcomes returned by any executor (all tests updated to reflect `WaitingForArrival`, `WaitingForCooldown`, or `Completed`).
- [x] Each executor step completes synchronous work in one `ExecuteStepAsync` call.
- [x] Arrival/cooldown events trigger executor directly (no pub/sub layer).
- [x] Dead-reckoning (`GameLoopService`) calls executor directly.
- [x] Startup recovery (`StartupRecoveryService`) calls executor directly for docked/in-orbit ships.
- [x] Build compiles with zero compiler warnings.
- [ ] Global events trigger orchestrator, not ships. *(optional Step 6 — not yet implemented)*
- [ ] `GoalExecutionOutcome.Progressing` removed from enum. *(optional Step 7 — not yet implemented)*

---

## Files Affected (Summary)

### Delete
- `SpaceTraders.Domain/Events/Ships/ShipAutomationTickEvent.cs`
- `SpaceTraders.Application/EventHandlers/ShipAutomationTickEventHandler.cs`
- `tests/SpaceTraders.Application.Tests/EventHandlers/ShipAutomationTickEventHandlerTests.cs`

### Modify (Step 1)
- `SpaceTraders.Application/Goals/Executors/ScoutWaypointGoalExecutor.cs`
- `SpaceTraders.Application/Goals/Executors/MoveToWaypointGoalExecutor.cs`
- `SpaceTraders.Application/Goals/Executors/MineResourceGoalExecutor.cs`
- `SpaceTraders.Application/Goals/Executors/SellCargoGoalExecutor.cs`
- `SpaceTraders.Application/Goals/Executors/DeliverCargoGoalExecutor.cs`
- `SpaceTraders.Application/Goals/Executors/SiphonResourceGoalExecutor.cs`
- `SpaceTraders.Application/Goals/Executors/SupplyConstructionGoalExecutor.cs`
- `SpaceTraders.Application/Goals/Executors/PatrolMarketGoalExecutor.cs`
- `tests/SpaceTraders.Application.Tests/Goals/*ExecutorTests.cs` (all)

### Modify (Step 2)
- `SpaceTraders.Application/Commands/Ships/OrbitShipCommand.cs`
- `SpaceTraders.Application/Commands/Ships/DockShipCommand.cs`
- `SpaceTraders.Application/Commands/Ships/ShipStateMismatchPublisher.cs`
- `tests/SpaceTraders.Application.Tests/Commands/ShipCommandResultTests.cs`
- `tests/SpaceTraders.Application.Tests/Commands/Phase65CommandHandlerTests.cs`

### Modify (Step 3)
- `SpaceTraders.Application/Events/Handlers/Ships/ShipArrivedEventHandler.cs`
- `SpaceTraders.Application/Events/Handlers/Ships/ShipCooldownExpiredEventHandler.cs`
- `tests/SpaceTraders.Application.Tests/Events/Handlers/Ships/ShipArrivedEventHandlerTests.cs`
- `tests/SpaceTraders.Application.Tests/Events/Handlers/Ships/ShipCooldownExpiredEventHandlerTests.cs`

### Modify (Step 4)
- `SpaceTraders.Application/Automation/GameLoopService.cs`
- `tests/SpaceTraders.Application.Tests/Automation/GameLoopServiceTests.cs` (if exists)

### Modify (Step 6, Optional)
- `SpaceTraders.Application/Events/Handlers/Global/` (new handlers)
- Tests for global event handlers

### Modify (Step 7, Optional)
- `SpaceTraders.Domain/Goals/GoalExecutionOutcome.cs`
- `SpaceTraders.Application/Goals/IShipGoalExecutor.cs`
- `SpaceTraders.Application/Goals/ShipGoalExecutorService.cs`
- All executor implementations and tests

---

## Timeline Estimate

| Step | Effort | Duration |
|---|---|---|
| 1. Audit & inline executors | High | 2–3 hours (careful review of each executor) |
| 2. Remove tick publishes | Medium | 30–45 min |
| 3. Direct service calls | Medium | 45 min |
| 4. Dead-reckoning | Low | 15–20 min |
| 5. Delete event type | Low | 5 min (plus verification) |
| 6. Global events (optional) | Medium | 1–2 hours |
| 7. Remove `Progressing` (optional) | Low | 30 min |
| **Total (mandatory steps 1–5)** | **High** | **4–5 hours** |
| **Total (including optional)** | **High** | **6–7 hours** |

---

## Notes for Implementation

1. **Commit frequently:** After each step, commit with a clear message: `"Step 1: inline executor continuations"`, etc.
2. **Branch strategy:** Use a feature branch `refactor/remove-ship-automation-tick-event`.
3. **Communication:** Notify team before starting; this is a significant refactor.
4. **Code review:** Have a senior reviewer check step 1 especially, since executor logic is core.
5. **Deployment:** This is a **non-breaking internal refactor** — no API changes, no behavioral changes (only timing improvements).

---

## Follow-Up Work (Post-Refactor)

- [ ] Monitor orchestrator tick interval; adjust if needed.
- [ ] Profile `ShipEventScheduler` persistence layer for latency.
- [ ] Consider removing dead-reckoning if scheduler proves 100% reliable.
- [ ] Add metrics for executor execution time, outcome distribution.
- [ ] Write architecture documentation explaining the clean orchestrator → assignment → ship model.

