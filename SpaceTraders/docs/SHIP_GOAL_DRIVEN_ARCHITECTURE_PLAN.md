# Ship Goal-Driven Architecture Plan

## Motivation

The current architecture (completed through Phase 7 of `ship-automation-architecture-plan.md`) achieves
clean single-step dispatch: each `ShipAutomationTickEvent` causes one planner to emit one atomic command
(`Dock`, `Orbit`, `Navigate`, `Extract`, …). Many ticks are needed to accomplish a single meaningful
action, and the planner — not the ship — contains all the sequencing logic (check fuel, orbit, navigate,
extract, orbit again, navigate to sell, dock, sell).

The goal of this plan is to push sequencing responsibility down into the ship and push spatial decision
responsibility up into the assignment layer, so the three layers become:

| Layer | Responsibility | Questions it answers |
|---|---|---|
| **Orchestrator** | Strategic resource needs, fleet capacity, purchase decisions | What do we need? Can the current fleet deliver it? |
| **Assignment resolver** | Spatial translation of abstract needs into concrete waypoints | Where is the nearest bauxite asteroid? Which market buys iron ore closest to the ship? |
| **Ship goal executor** | Autonomous execution of a goal from any starting state | Am I docked? Do I have fuel? Do I have a mining laser? Navigate, extract, navigate, sell. |

---

## Guiding Principles

1. A ship receives a **goal**, not a sequence of atomic steps.
2. A ship is responsible for achieving its goal regardless of its current state when the goal is assigned.
3. A ship validates its own capabilities before reporting a goal as executable.
4. The assignment resolver owns all spatial decisions: which waypoint, which market, which route.
5. The orchestrator owns resource need definitions and fleet-level decisions; it never references waypoints directly.
6. One orchestrator evaluation produces at most one assignment change per ship.
7. Capability mismatches (no mining laser, no cargo space) are reported as goal errors, not silent loops.

---

## Event Taxonomy

Every domain event is classified into one of three tiers. The tier determines whether an event
handler must exist, may exist, or must not exist.

### Tier 1 — Reactive (event handlers must exist and act)

| Event | Who handles it | Why |
|---|---|---|
| `ShipArrivedEvent` | `ShipGoalExecutorService` | Scheduled at the estimated arrival time; re-activates the ship to continue goal execution |
| `ShipCooldownExpiredEvent` | `ShipGoalExecutorService` | Scheduled at `cooldown.expiration`; re-activates the ship after a mining/siphon/survey cooldown |
| `CreditsChangedEvent` | Orchestrator / `IBudgetPolicy` | Fleet expansion or purchase decisions may need to change |
| `MarketPriceChangedEvent` | Assignment resolver cache invalidation; Orchestrator re-evaluate | Sell route or supply choice may be suboptimal now |
| `GoalCompletedEvent` | Orchestrator | Ship is idle; re-evaluate fleet needs and assign a new goal |
| `GoalBlockedEvent` | Orchestrator | Ship cannot proceed; may require equipment purchase or reassignment |

### Tier 2 — Informative (published for observability; no handlers)

`ResourceExtractedEvent`, `CargoSoldEvent`, `ContractDeliveredEvent`, `ConstructionSuppliedEvent`,
`ShipPurchasedEvent`, and similar outcome events. These are useful for dashboards, audit logs, and
integration tests but must **never** gate the control flow. The event is published but no handler
class exists for it.

### Tier 3 — Removed / replaced by direct return

`ShipDockedEvent`, `ShipOrbitedEvent`, `ShipUndockedEvent`, and any event whose sole consumer was
a handler that immediately issued the next API call. Docking, orbiting, and undocking are instant,
synchronous API calls; the executor continues on the successful return value without scheduling any
event.

### Guiding rule

> An event handler re-activates a ship **only** when the ship cannot know when to resume without
> external information — i.e. a future arrival time or a cooldown expiry. All other sequencing is
> internal to the goal executor.

---

## Ship Goals

A goal represents a complete, self-contained objective for a single ship. The ship executor for each goal
handles all prerequisite actions (refuel, change flight mode, navigate, handle cooldown) without external
coordination.

### Goal types

```
MoveToWaypoint(targetWaypointSymbol)
MineResource(tradeSymbol, sourceWaypointSymbol)
SiphonResource(tradeSymbol, sourceWaypointSymbol)
SellCargo(destinationWaypointSymbol, tradeSymbols[])
DeliverCargo(contractId, tradeSymbol, deliveryWaypointSymbol)
SupplyConstruction(tradeSymbol, constructionSiteWaypointSymbol)
ScoutWaypoint(targetWaypointSymbol)
PatrolMarket(targetWaypointSymbol)
Idle
```

Each goal carries a **GoalId** (correlation token) so orchestrator assignment, goal execution, and
completion events can all be correlated.

### Goal lifecycle

```
Assigned → Validating → Executing → Completed
                     ↘ Blocked (with reason)
```

- **Validating**: the ship checks that the goal is achievable (capabilities, route reachability).
- **Executing**: the ship is making incremental progress toward the goal.
- **Completed**: the goal is done and the ship is available for a new assignment.
- **Blocked**: the ship cannot progress (missing equipment, no fuel market reachable, waypoint
  inaccessible). The orchestrator is notified so it can reassign or purchase missing equipment.

---

## Ship Goal Executor

Replaces `IShipPlanner` and `ShipPlannerDecision`. Instead of deciding one atomic step, the executor
decides the entire next action toward the goal from the ship's current state.

### MineResource executor — internal decision flow

```text
Given: ship.Goal = MineResource(tradeSymbol = "BAUXITE", sourceWaypoint = "X1-TD7-A3")

1. Validate: does the ship have a mining laser (frame MINER or mining mount)?
   → If not: transition to Blocked("no mining equipment")

2. If ship is InTransit → wait (no action)

3. If ship is Docked at any waypoint:
   a. Check fuel. If below threshold: refuel at current market if available, else orbit and navigate to
      nearest fuel market first.
   b. If cargo is full: navigate to sell waypoint, dock, sell non-protected cargo, return.
   c. Otherwise: orbit.

4. If ship is InOrbit and NOT at sourceWaypoint:
   a. Check fuel. If dangerously low: navigate to nearest fuel market.
   b. Patch flight mode if needed (DRIFT for zero-fuel ships, CRUISE otherwise).
   c. Navigate to sourceWaypoint.

5. If ship is InOrbit at sourceWaypoint:
   a. If on cooldown: wait for cooldown expiry.
   b. If cargo is full: navigate to nearest market that buys tradeSymbol and dock there.
   c. If active surveys exist for this waypoint targeting tradeSymbol: extract with best survey.
   d. If survey equipment available and no useful surveys: survey first.
   e. Otherwise: extract.
```

All other goal executors follow the same pattern: validate capabilities, handle state prerequisites
(fuel, location), then execute the core action.

### Capability validation

Each executor declares its required capabilities as a value object:

```
MiningCapabilities: requires MiningMount or MinerFrame
SiphonCapabilities: requires SiphonMount
CargoCapabilities: requires CargoCapacity > 0
NavigationCapabilities: requires FuelCapacity > 0 or DRIFT-capable
```

The ship reads these from cached `ShipModel.Mounts`, `ShipModel.Frame`, and `ShipModel.FuelCapacity`.

---

## Assignment Resolver

The assignment resolver translates an abstract resource need from the orchestrator into a concrete
ship goal with a specific waypoint. It is the only layer that queries waypoint, market, and system
data for spatial decisions.

### Responsibilities

- **Source resolution**: given a trade symbol and a region, find the best extraction or purchase waypoint.
  - For mining: find waypoints with asteroid traits that yield the trade symbol.
  - For trading: find markets that stock the trade symbol at a reasonable price.
  - Prefer closer waypoints (by system-coordinate distance from the assigned ship).
- **Sell resolution**: given a trade symbol and a ship location, find the nearest market that buys it.
- **Route feasibility**: check that the ship can reach the resolved waypoint with available fuel,
  or that a fuel waypoint exists along the route.
- **Fallback handling**: if no suitable waypoint is found in the current system, report the assignment
  as unresolvable rather than blocking the ship silently.

### Example: resolving a mining assignment

```text
Orchestrator input: "mine BAUXITE for construction at X1-TD7-JG, ship = X1-TD7-1"

Assignment resolver:
1. Query WaypointRepository for waypoints in system X1-TD7 with trait COMMON_METAL_DEPOSITS
   or PRECIOUS_METAL_DEPOSITS that are asteroid type.
2. Filter to waypoints where BAUXITE is in the known deposit list (from previous surveys or
   waypoint modifier data).
3. If none known, select any asteroid in the system (BAUXITE may still be present).
4. Sort by coordinate distance from the ship's current position.
5. Select the nearest candidate.
6. Output: MineResource(tradeSymbol = "BAUXITE", sourceWaypoint = "X1-TD7-A3")
```

The resolver does not instruct the ship on how to get there. The ship goal executor handles that.

---

## Orchestrator Evolution

The orchestrator works entirely in terms of **resource production goals**, not waypoints or routes.

### Resource production goal

```
ResourceProductionGoal:
  - tradeSymbol          string   // "BAUXITE"
  - unitsNeeded          int      // 500
  - purposeKind          enum     // Contract | Construction | Market
  - purposeId            string   // contractId or constructionSiteWaypoint
  - deadline             DateTimeOffset?
  - priority             int
```

The orchestrator evaluates whether the current fleet can produce enough of the required resource
before the deadline. If not, it either:

1. Reassigns an idle ship with a suitable role.
2. If no idle suitable ships: evaluate buying a new ship.

### Fleet capacity model (evolution of FleetCapacityEstimator)

Beyond the current ship-count and cargo-capacity model, the orchestrator tracks:

- **Effective mining rate per ship**: estimated units per hour based on cooldown, cargo capacity,
  and travel time to the nearest known asteroid.
- **Haul capacity**: cargo capacity × trips per hour between source and destination.
- **Bottleneck detection**: is the limit mining throughput, haul throughput, or sell market
  trade volume?

### Fleet expansion decision (completing the current advisory-only model)

The orchestrator currently marks fleet expansion as advisory. This plan makes it actionable:

```text
Fleet expansion decision flow:
1. Identify the bottleneck resource goal that cannot be met.
2. Determine the ship type that would relieve the bottleneck
   (mining drone for extraction, light hauler for cargo, satellite for market coverage).
3. Query the shipyard repository for the cheapest eligible ship in a reachable system.
4. Check IBudgetPolicy: can we afford the purchase without breaching the credit reserve?
5. If yes: emit PurchaseShipCommand(shipyardWaypoint, shipType).
6. After purchase: immediately assign the new ship to the bottleneck goal via the assignment resolver.
```

### Orchestrator should not know waypoints

The current `FleetOrchestrator.BuildAssignment` passes waypoints like `OriginWaypoint: ship.WaypointSymbol`
and `DestWaypoint: goal.DestinationWaypoint` directly into `AssignShipCommand`. Under the new model:

- The orchestrator emits a `ResourceProductionGoal` and a ship symbol.
- The assignment resolver converts that into a concrete ship goal (with waypoints).
- The ship goal executor runs the goal.

This is a significant change to `FleetOrchestrator`, `AssignShipCommand`, and their handling path.

---

## Revised Application Flow

```text
GameLoopService tick
        ↓
FleetOrchestrator.EvaluateAndAssignAsync()
  - Aggregates ResourceProductionGoals from goal evaluators
  - For each unmet goal, find or purchase a ship
  - Emit AssignShipToGoalCommand(shipSymbol, resourceProductionGoal)
        ↓
AssignmentResolver.ResolveAsync(ship, resourceProductionGoal)
  - Loads waypoint, market, and system data
  - Returns a ShipGoal with concrete waypoint(s)
        ↓
ShipGoalRepository.SetActiveGoal(shipSymbol, shipGoal)
  - Persists the active goal for the ship
  - Publishes ShipAutomationTickEvent to trigger first execution
        ↓
ShipAutomationTickEvent
        ↓
ShipGoalExecutorService.ExecuteAsync(shipSymbol)
  - Loads ship state and active goal
  - Selects the goal executor for the goal type
  - Executor validates capabilities, evaluates current state, issues one API call
  - Persists updated ship state
  - If goal complete: publishes GoalCompletedEvent, clears goal, sets Idle
  - If blocked: publishes GoalBlockedEvent, clears goal, sets Idle
  - Otherwise: publishes ShipAutomationTickEvent for next step (or schedules for cooldown/arrival)
        ↓
GoalCompletedEvent / GoalBlockedEvent
        ↓
FleetOrchestrator reacts to completion (re-evaluates on next tick)
```

---

## Migration Path

The current codebase is at the end of Phase 7: clean, planner-driven, no chain infrastructure.
Migration to goal-driven architecture proceeds in five phases.

### Phase 8: Goal model and persistence ✅

Goal: introduce the goal type hierarchy and persist active goals per ship.

Tasks:

- ✅ Define `ShipGoalKind` enum and `ShipGoal` sealed record hierarchy (one subtype per goal type) in
  `SpaceTraders.Domain`.
- ✅ Add `GoalId`, `GoalKind`, `GoalPayloadJson`, and `GoalStatus` columns to `cached_ships` (or a
  separate `ship_goals` table).
- ✅ Add `IShipGoalRepository` to `SpaceTraders.Application/Interfaces/Repositories`.
- ✅ Add `ShipGoalRepository` implementation in `SpaceTraders.Infrastructure.Persistence`.
- ✅ Add `GoalCompletedEvent` and `GoalBlockedEvent` to `SpaceTraders.Domain/Events/Ships`.
- ✅ Add unit tests for goal serialization, persistence roundtrip, and event payload preservation.

Deliverables:

- ✅ A ship can have a persisted active goal that survives restarts.
- ✅ Domain event types for goal lifecycle exist.

### Phase 9: Assignment resolver ✅

Goal: translate abstract resource needs into concrete ship goals with specific waypoints.

Tasks:

- ✅ Add `IAssignmentResolver` to `SpaceTraders.Application/Interfaces`:
  ```csharp
  Task<ShipGoal?> ResolveAsync(ShipModel ship, ResourceProductionGoal goal, CancellationToken ct);
  Task<string?> FindNearestSellMarketAsync(ShipModel ship, string tradeSymbol, CancellationToken ct);
  ```
- ✅ Add `AssignmentResolver` implementation in `SpaceTraders.Application/Services` that:
  - Queries `IWaypointRepository` for waypoints matching the resource type and extraction method.
  - Computes coordinate distance from the ship's current system/waypoint.
  - Queries `IMarketRepository` to find sell waypoints.
  - Returns the most appropriate concrete `ShipGoal`.
- ✅ Add `ResourceProductionGoal` record to `SpaceTraders.Application/Orchestration/OrchestrationModels.cs`.
- ✅ Add unit tests for resolver: asteroid selection by distance, market sell resolution, fallback
  when no data is cached, and feasibility reporting.

Deliverables:

- ✅ Given an abstract resource need and a ship, the resolver produces a concrete goal with a specific
  source waypoint.

### Phase 10: Ship goal executors ✅

Goal: replace the `IShipPlanner`/`ShipPlannerDecision` model with goal executors. This is the
largest phase.

Tasks:

- ✅ Add `IShipGoalExecutor` interface in `SpaceTraders.Application/Goals`:
  ```csharp
  bool CanExecute(ShipGoal goal);
  Task<GoalExecutionResult> ExecuteStepAsync(ShipModel ship, ShipGoal goal, ShipGoalContext ctx, CancellationToken ct);
  ```
- ✅ `GoalExecutionResult` replaces `ShipPlannerDecision` for the goal layer. It carries:
  - The outcome of this step (`Progressing`, `WaitingForArrival`, `WaitingForCooldown`, `Completed`, `Blocked`).
  - An optional blocking reason.
  - An optional `CooldownExpiresAt` timestamp for cooldown scheduling.
- ✅ Add `ShipGoalContext` (analogous to `ShipPlannerContext`) with the read-only contextual data a
  goal executor needs (fuel market waypoint, capability flags, active surveys, market snapshot,
  active contracts, nearest sell market, construction complete flag).
- ✅ Add `ShipGoalExecutorService` (replaces `ShipPlannerService`) that:
  - Loads the ship's active goal from `IShipGoalRepository`.
  - Builds `ShipGoalContext`.
  - Selects the matching executor.
  - Calls `ExecuteStepAsync` and handles `GoalExecutionResult`.
  - Publishes `GoalCompletedEvent`, `GoalBlockedEvent`, or a follow-up `ShipAutomationTickEvent`.
  - Falls back to `IShipPlannerService` for ships with no active goal (backward-compatibility bridge).
- ✅ Implement goal executors:
  - `IdleGoalExecutor`
  - `MineResourceGoalExecutor` (full mine → sell cycle, survey-aware, fuel-aware)
  - `MoveToWaypointGoalExecutor`
  - `SellCargoGoalExecutor`
  - `SiphonResourceGoalExecutor` (full siphon → sell cycle, fuel-aware)
  - `DeliverCargoGoalExecutor`
  - `SupplyConstructionGoalExecutor`
  - `ScoutWaypointGoalExecutor`
  - `PatrolMarketGoalExecutor`
- ✅ Update `ShipAutomationTickEventHandler` to call `IShipGoalExecutorService` instead of
  `IShipPlannerService`.
- ✅ Deprecate `IShipPlanner`, `ShipPlannerDecision`, `ShipPlannerCommandKind`, `ShipPlannerContext`,
  and `ShipPlannerService` with `[Obsolete]` (remove in Phase 12 after executors are proven in production).
- ✅ Add unit tests for each executor covering: every starting `ShipLocalStatus`, low-fuel path,
  capability missing path, cooldown waiting, goal completion detection, and goal blocked detection.
  365 tests pass in total.

Deliverables:

- ✅ Ships execute goals autonomously through any starting state.
- ✅ Old planner infrastructure is deprecated and can be removed in Phase 12.

### Phase 11: Orchestrator evolution to resource production goals

Goal: the orchestrator works in terms of `ResourceProductionGoal` and delegates spatial decisions
entirely to the assignment resolver.

Tasks:

- Replace `AssignShipCommand` with `AssignShipToGoalCommand(shipSymbol, ResourceProductionGoal)`.
  - Handler calls `IAssignmentResolver.ResolveAsync(...)` then `IShipGoalRepository.SetActiveGoal(...)`.
  - Publishing the new assignment publishes `ShipAutomationTickEvent` for first-step execution.
- Update `FleetOrchestrator.BuildAssignment` to emit `AssignShipToGoalCommand` instead of the
  current `AssignShipCommand` with hardcoded waypoint fields.
- Update all `IFleetGoalEvaluator` implementations to produce `ResourceProductionGoal` rather than
  `FleetGoal` with origin/destination waypoints.
- Remove waypoint fields from `FleetGoal` (origin/destination); those now belong to the assignment
  resolver output.
- Implement actionable fleet expansion (completing the advisory-only model from Phase 6):
  - Add `PurchaseShipCommand` handler that calls the shipyard API and assigns the new ship.
  - Add `FleetExpansionGoalEvaluator` logic to emit a purchasable `FleetExpansionGoal` when the
    bottleneck is confirmed and budget allows.
  - After purchase, immediately trigger `AssignShipToGoalCommand` for the new ship.
- Update orchestrator tests for the new goal shape and fleet-expansion purchase path.

Deliverables:

- The orchestrator no longer contains waypoint strings; all spatial decisions are in the assignment
  resolver.
- Fleet expansion purchases ships when the bottleneck requires it and budget allows.

### Phase 12: Cleanup and capability registry

Goal: remove deprecated planner infrastructure and add first-class capability tracking.

Tasks:

- Delete `IShipPlanner`, `ShipPlannerDecision`, `ShipPlannerCommandKind`, `ShipPlannerContext`,
  `ShipPlannerService`, and all concrete `*ShipPlanner.cs` files.
- Remove `ShipPlannerDecision`-related DI registrations.
- Add a `ShipCapabilityRegistry` that classifies ships by capabilities derived from their cached
  mounts and frame:
  - `CanMine`, `CanSiphon`, `CanSurvey`, `HasCargo`, `HasFuelTank`, `CanRepair`.
  - Used by the orchestrator to select the right ship type for each goal.
  - Used by goal executors during capability validation.
- Update `FleetCapacityEstimator` to use `ShipCapabilityRegistry` instead of assignment-type inference.
- Add a `ShipCapabilityRegistryTests` suite.

Deliverables:

- Planner layer removed; goal executor layer is the only automation path.
- Ship capability is derived from game data, not from assignment type.

### Phase 13: Event rationalization

Goal: remove all event handlers that exist only to advance a ship from one instant API call to the
next. Only Tier 1 reactive events retain handlers. Tier 2 events are kept as published signals but
their handler classes are deleted. Tier 3 events are deleted entirely.

Tasks:

- Audit every domain event under `SpaceTraders.Domain/Events/Ships` and classify it using the
  Event Taxonomy above (Tier 1 / 2 / 3).
- Delete `ShipDockedEvent`, `ShipOrbitedEvent`, `ShipUndockedEvent` and any other event whose
  sole consumer is a handler that immediately issues the next API call.
- Delete corresponding event handler classes in `SpaceTraders.Application/EventHandlers/Ships`.
- In every goal executor, replace the removed event-driven transitions with direct sequential
  logic: after `OrbitAsync()` succeeds, call the next action in the same `ExecuteStepAsync`
  invocation rather than publishing an event and returning.
- Retain `GoalCompletedEvent`, `GoalBlockedEvent`, `CreditsChangedEvent`, and
  `MarketPriceChangedEvent` with their handlers unchanged.
- Convert Tier 2 events: keep the `IEventPublisher.Publish` call for observability but delete the
  handler class.
- Update integration and unit tests: tests that asserted an intermediate event was published for
  an instant action should instead assert the final outcome (e.g. ship is in orbit, the correct
  API was called).

Deliverables:

- No event handler class exists for any instant (synchronous-return) ship action.
- `ShipGoalExecutorService` communicates with the orchestrator only via `GoalCompletedEvent` and
  `GoalBlockedEvent`.
- The total number of event handler classes is substantially reduced.

### Phase 14: Scheduled future events (arrival and cooldown)

Goal: implement the mechanism that delivers `ShipArrivedEvent` and `ShipCooldownExpiredEvent` at
the correct future moment, replacing the polling tick loop for in-transit and cooling-down ships.

Tasks:

- Add `IShipEventScheduler` to `SpaceTraders.Application/Interfaces`:
  ```csharp
  Task ScheduleArrivalAsync(string shipSymbol, Guid goalId, DateTimeOffset arrivalTime, CancellationToken ct);
  Task ScheduleCooldownExpiryAsync(string shipSymbol, Guid goalId, DateTimeOffset expiresAt, CancellationToken ct);
  Task CancelScheduledAsync(string shipSymbol, Guid goalId, CancellationToken ct);
  ```
- Implement `ShipEventScheduler` as an `IHostedService` backed by an in-memory priority queue
  (ordered by trigger time). On startup, reload all pending rows from the `scheduled_ship_events`
  table so scheduled events survive a process restart. Schema:
  ```sql
  scheduled_ship_events (
      ship_symbol  TEXT        NOT NULL,
      goal_id      UUID        NOT NULL,
      event_kind   TEXT        NOT NULL,  -- 'Arrival' | 'CooldownExpiry'
      trigger_at   TIMESTAMPTZ NOT NULL,
      PRIMARY KEY (ship_symbol, goal_id)
  )
  ```
- In every goal executor that issues a `Navigate` API call: after a successful response, call
  `IShipEventScheduler.ScheduleArrivalAsync(ship.Symbol, goal.GoalId, nav.Route.Arrival)` and
  return `GoalExecutionResult.Waiting`. Do **not** publish `ShipAutomationTickEvent`.
- In goal executors that issue `Extract`, `Siphon`, or `Survey` API calls: after a successful
  response containing a cooldown, call
  `IShipEventScheduler.ScheduleCooldownExpiryAsync(ship.Symbol, goal.GoalId, cooldown.Expiration)`
  and return `GoalExecutionResult.Waiting`.
- Add `ShipArrivedEventHandler` and `ShipCooldownExpiredEventHandler`: each verifies that the
  `GoalId` in the event still matches the ship's currently active goal (stale wake-ups are silently
  ignored), then publishes `ShipAutomationTickEvent` to resume execution.
- In the `GoalCompletedEvent` and `GoalBlockedEvent` handlers, call
  `IShipEventScheduler.CancelScheduledAsync` to prevent ghost wake-ups after goal reassignment.
- Add unit tests for `ShipEventScheduler`:
  - Event fires at the correct time.
  - Persisted schedule is reloaded and fires correctly after a simulated restart.
  - Cancellation before trigger time prevents the event from firing.
  - A `GoalId` mismatch in the handler is silently ignored (no exception, no action).

Deliverables:

- Ships in transit or on cooldown consume no CPU and produce no wasted tick-loop iterations.
- A ship resumes goal execution at its estimated arrival time or cooldown expiry, even after a
  process restart.
- No `ShipAutomationTickEvent` is published for a ship that is in transit or on cooldown.

---

## Comparison: Before and After

### Before (current)

```text
MineGoods assignment
  → MiningShipPlanner.Plan(ship, assignment, context)
  → ShipPlannerDecision { Kind = Navigate, Destination = "X1-TD7-A3" }
  → NavigateShipCommand
  → (next tick) ShipPlannerDecision { Kind = Extract }
  → ExtractResourcesCommand
  → (next tick) ShipPlannerDecision { Kind = Dock }
  → ...
```

- Orchestrator sets `MineGoods` assignment with `OriginWaypoint = "X1-TD7-A3"` hardcoded.
- Planner sequences every micro-step.
- Each step is a separate tick.

### After (proposed)

```text
Orchestrator: "need 500 BAUXITE for construction at X1-TD7-JG"
  → AssignShipToGoalCommand(ship = "X1-TD7-1", ResourceProductionGoal { tradeSymbol = "BAUXITE" })
  → AssignmentResolver → ShipGoal: MineResource("BAUXITE", "X1-TD7-A3")
  → ShipGoalRepository.SetActiveGoal
  → ShipAutomationTickEvent
  → MineResourceGoalExecutor.ExecuteStepAsync
    (ship is docked, checks fuel, orbits)
  → ShipAutomationTickEvent
  → MineResourceGoalExecutor.ExecuteStepAsync
    (ship is in orbit at wrong waypoint, navigates to X1-TD7-A3)
  → ShipAutomationTickEvent (scheduled at arrival)
  → MineResourceGoalExecutor.ExecuteStepAsync
    (ship arrived at X1-TD7-A3, extracts)
  ...
```

- Orchestrator uses resource symbols, not waypoints.
- Assignment resolver picks the waypoint.
- Goal executor handles all internal state transitions.
- Waypoint selection and routing logic are co-located in the assignment resolver, not scattered
  across multiple planners.

---

## Testing Strategy

### Goal executor unit tests

For each goal executor, test the full status × state matrix:

- In transit → wait (no action issued)
- Docked, wrong location, low fuel → refuel
- Docked, wrong location, adequate fuel → orbit
- In orbit, wrong location → navigate
- In orbit, correct location, on cooldown → wait
- In orbit, correct location, cargo full → navigate to sell
- In orbit, correct location, ready → execute goal action (extract/siphon/dock/etc.)
- Capability missing → blocked result, no API call

### Assignment resolver tests

- Known asteroid with trait → resolved to nearest asteroid
- No asteroid with trait in system → resolved to any asteroid (unknown deposit)
- No waypoints cached → resolver reports unresolvable
- Market sell resolution → nearest market that buys the symbol

### Orchestrator tests

- Resource need covered by current fleet → no new assignment
- Resource need with idle miner → assign idle miner
- Resource need with no idle suitable ship → fleet expansion goal emitted
- Fleet expansion approved by budget → purchase command emitted
- Fleet expansion blocked by budget → no purchase

### Integration-style tests

- Full assignment-to-goal-to-completion flow: orchestrator assigns resource goal, resolver selects
  waypoint, executor steps through to completion, `GoalCompletedEvent` published.
- Blocked goal: executor detects missing capability, `GoalBlockedEvent` published, ship becomes
  idle, orchestrator re-evaluates on next tick.

---

## Acceptance Criteria

- A ship given a `MineResource` goal reaches the source, extracts, and sells without any
  external coordination after goal assignment.
- The orchestrator never contains a waypoint symbol; all waypoints are resolved by the
  assignment resolver.
- Capability mismatches are surfaced as `GoalBlockedEvent` within one execution step.
- Fleet expansion transitions from advisory to actionable: a purchase command is emitted and the
  new ship is assigned immediately.
- All prior acceptance criteria from `ship-automation-architecture-plan.md` continue to hold.
