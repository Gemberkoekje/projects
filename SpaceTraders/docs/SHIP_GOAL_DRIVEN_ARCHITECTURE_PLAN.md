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
| **Orchestrator** | Strategic fleet needs (resource production, market coverage, fleet expansion), fleet capacity, purchase decisions | What do we need? Who is missing? Can the current fleet deliver it? |
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

The orchestrator works in terms of **strategic fleet goals** (`FleetGoal`), not waypoints or routes.
Strategic goals are wider than resource allocation alone; the four goal kinds are:

| `FleetGoalKind` | What it represents |
|---|---|
| `Contract` | Deliver N units of a trade symbol to a destination by a deadline |
| `Construction` | Supply N units of a trade symbol to a construction site |
| `MarketCoverage` | Station a probe or patrol ship at an uncovered market waypoint |
| `FleetExpansion` | Purchase a new ship because all ships are busy and the budget allows |

### Two-level goal model

The orchestrator operates at two levels:

**Level 1 — Strategic fleet goals (`FleetGoal`)**: produced by the `IFleetGoalEvaluator` implementations.
These describe *what* the fleet is trying to achieve; they carry no waypoint information.

**Level 2 — Resource production goals (`ResourceProductionGoal`)**: an intermediate translation step used
only for `Contract` and `Construction` goals. The orchestrator converts a `Contract`/`Construction`
`FleetGoal` into a `ResourceProductionGoal` and passes it to the assignment resolver.

`MarketCoverage` and `FleetExpansion` goals do **not** go through `ResourceProductionGoal`; they are
translated directly by the assignment resolver into ship goals (`ScoutWaypoint`/`PatrolMarket`) or a
`PurchaseShipCommand` respectively.

### Resource production goal (Contract / Construction only)

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
1. Identify the bottleneck goal that cannot be met (resource, coverage, or otherwise).
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

- For `Contract`/`Construction` goals: the orchestrator emits a `ResourceProductionGoal` and a ship symbol;
  the assignment resolver converts that into a concrete ship goal (with waypoints).
- For `MarketCoverage` goals: the orchestrator emits the `FleetGoal` with the uncovered market waypoint;
  the assignment resolver creates a `ScoutWaypoint` or `PatrolMarket` ship goal directly.
- For `FleetExpansion` goals: the orchestrator emits a `PurchaseShipCommand`; no assignment resolver step
  is needed until after the ship is purchased.
- The ship goal executor runs the resulting concrete goal.

This is a significant change to `FleetOrchestrator`, `AssignShipCommand`, and their handling path.

---

## Revised Application Flow

```text
GameLoopService tick
        ↓
FleetOrchestrator.EvaluateAndAssignAsync()
  - Aggregates FleetGoals from goal evaluators (Contract, Construction, MarketCoverage, FleetExpansion)
  - For each unmet goal, finds an idle ship or triggers fleet expansion
  - Dispatches by goal kind:

  ┌─ Contract / Construction goal
  │   → AssignShipToGoalCommand(shipSymbol, ResourceProductionGoal)
  │          ↓
  │   AssignmentResolver.ResolveAsync(ship, resourceProductionGoal)
  │     - Loads waypoint, market, and system data
  │     - Returns a ShipGoal with concrete waypoint(s)
  │
  ├─ MarketCoverage goal
  │   → AssignShipToGoalCommand(shipSymbol, FleetGoal { Kind = MarketCoverage })
  │          ↓
  │   AssignmentResolver creates ScoutWaypoint or PatrolMarket ShipGoal directly
  │
  └─ FleetExpansion goal
      → PurchaseShipCommand(shipyardWaypoint, shipType)
             ↓
        After purchase: AssignShipToGoalCommand for the new ship

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

### Phase 11: Orchestrator evolution to fleet goal dispatch

Goal: the orchestrator works in terms of all `FleetGoalKind` values and delegates spatial decisions
entirely to the assignment resolver. `Contract` and `Construction` goals are translated via
`ResourceProductionGoal`; `MarketCoverage` and `FleetExpansion` goals are dispatched directly.

#### Phase 11a: `AssignShipToGoalCommand` and its handler

Tasks:

- Add `AssignShipToGoalCommand(shipSymbol, FleetGoal)` record to `SpaceTraders.Application/Commands`.
- Add `AssignShipToGoalCommandHandler` that inspects `FleetGoalKind` and dispatches:
  - `Contract` / `Construction` → calls `IAssignmentResolver.ResolveAsync(ship, ResourceProductionGoal)`
    to obtain a concrete `ShipGoal` with waypoints.
  - `MarketCoverage` → calls `IAssignmentResolver` to produce a `ScoutWaypoint` or `PatrolMarket`
    `ShipGoal` for the uncovered market waypoint carried in the `FleetGoal`.
  - `FleetExpansion` → emits `PurchaseShipCommand`; after purchase, emits `AssignShipToGoalCommand`
    for the new ship.
- In all cases, the handler ends by calling `IShipGoalRepository.SetActiveGoal(...)` and publishing
  `ShipAutomationTickEvent` for first-step execution.
- Add unit tests for `AssignShipToGoalCommandHandler` covering each `FleetGoalKind` branch.

Deliverables:

- `AssignShipToGoalCommand` exists and its handler routes each goal kind to the correct resolver path.

#### Phase 11b: Update `FleetOrchestrator` to emit `AssignShipToGoalCommand`

Tasks:

- Update `FleetOrchestrator.BuildAssignment` to emit `AssignShipToGoalCommand` instead of the
  current `AssignShipCommand` with hardcoded waypoint fields.
- Keep all `IFleetGoalEvaluator` implementations producing `FleetGoal`; do not change evaluators
  to produce `ResourceProductionGoal` — that conversion happens inside the command handler, not in
  the evaluator.
- Remove waypoint fields (`OriginWaypoint`, `DestinationWaypoint`) from `FleetGoal` for goal kinds
  where they are not needed (`Contract`, `Construction`); keep `OriginWaypoint` for `MarketCoverage`
  because it identifies the market to cover.
- Update orchestrator unit tests for the updated goal shape.

Deliverables:

- `FleetOrchestrator` no longer references waypoint symbols directly.
- All four `FleetGoalKind` values are dispatched via `AssignShipToGoalCommand`.

#### Phase 11c: Actionable fleet expansion

Tasks:

- Add `PurchaseShipCommand(shipyardWaypoint, shipType)` record and its handler that:
  - Calls the shipyard API to purchase the ship.
  - Immediately emits `AssignShipToGoalCommand` for the newly purchased ship.
- Update `FleetExpansionGoalEvaluator` to:
  - Identify the bottleneck goal kind (not just resource goals) and select the appropriate ship type
    to relieve it (mining drone for extraction, light hauler for cargo, satellite for market coverage).
  - Query `IShipyardRepository` for the cheapest eligible ship in a reachable system.
  - Check `IBudgetPolicy` before emitting `FleetExpansion` goal.
- Add unit tests for the purchase command handler and the updated evaluator.

Deliverables:

- Fleet expansion transitions from advisory to actionable: a purchase command is emitted and the
  new ship is immediately assigned to the bottleneck goal.

#### Phase 11d: Orchestrator integration tests

Tasks:

- Add or update orchestrator integration-style tests covering:
  - `Contract` / `Construction` resource need covered by current fleet → no new assignment.
  - `Contract` / `Construction` resource need with idle miner → idle miner assigned.
  - `Contract` / `Construction` resource need with no idle suitable ship → fleet expansion goal emitted.
  - `MarketCoverage` goal: uncovered market → scout or patrol ship assigned.
  - `MarketCoverage` goal: all markets covered → no goal emitted.
  - `FleetExpansion` approved by budget → purchase command emitted; new ship assigned to bottleneck goal.
  - `FleetExpansion` blocked by budget → no purchase.
  - `FleetExpansion` blocked by max-ship cap → no purchase.

Deliverables:

- Full orchestrator coverage for all four `FleetGoalKind` values and the fleet-expansion purchase path.

### Phase 12: Cleanup and capability registry

Goal: remove deprecated planner infrastructure and add first-class capability tracking.

#### Phase 12a: Delete deprecated planner infrastructure

Tasks:

- Delete `IShipPlanner`, `ShipPlannerDecision`, `ShipPlannerCommandKind`, `ShipPlannerContext`,
  `ShipPlannerService`, and all concrete `*ShipPlanner.cs` files.
- Remove all `ShipPlannerDecision`-related DI registrations from the composition root.
- Verify that no compilation errors remain after removal (the goal executor layer must already
  cover all ship types that had a planner).

Deliverables:

- Planner layer removed; the codebase compiles cleanly without any planner types.

#### Phase 12b: Add `ShipCapabilityRegistry`

Tasks:

- Add a `ShipCapabilityRegistry` service to `SpaceTraders.Application/Services` that classifies
  ships by capabilities derived from their cached mounts and frame:
  - `CanMine`, `CanSiphon`, `CanSurvey`, `HasCargo`, `HasFuelTank`, `CanRepair`.
- Expose the registry via `IShipCapabilityRegistry` in `SpaceTraders.Application/Interfaces`.
- Update goal executors to delegate capability validation to `IShipCapabilityRegistry` instead of
  inspecting `ShipModel.Mounts` directly.

Deliverables:

- Ship capability is derived from game data through a single registry rather than duplicated checks.

#### Phase 12c: Update `FleetCapacityEstimator`

Tasks:

- Update `FleetCapacityEstimator` to use `IShipCapabilityRegistry` for role classification instead
  of the current assignment-type inference.
- Ensure the estimator still produces the same effective mining-rate and haul-capacity metrics.

Deliverables:

- `FleetCapacityEstimator` no longer infers ship roles from assignment type; it reads capabilities
  from the registry.

#### Phase 12d: Capability registry tests

Tasks:

- Add a `ShipCapabilityRegistryTests` suite covering:
  - Each capability flag is correctly set for ships with the relevant mount or frame.
  - Ships missing a mount or frame have the corresponding flag as `false`.
  - A ship with multiple relevant mounts has all corresponding flags set.

Deliverables:

- Full unit test coverage for `ShipCapabilityRegistry`.

### Phase 13: Event rationalization

Goal: remove all event handlers that exist only to advance a ship from one instant API call to the
next. Only Tier 1 reactive events retain handlers. Tier 2 events are kept as published signals but
their handler classes are deleted. Tier 3 events are deleted entirely.

#### Phase 13a: Audit and classify all domain events

Tasks:

- Review every domain event under `SpaceTraders.Domain/Events/Ships` and classify each as
  Tier 1, Tier 2, or Tier 3 using the Event Taxonomy defined in this document.
- Produce an audit table (can be a code review comment or a temporary note) listing each event,
  its tier, and the reason for the classification.

Deliverables:

- Clear Tier classification for every existing domain ship event before any deletions begin.

#### Phase 13b: Delete Tier 3 events and handlers

Tasks:

- Delete `ShipDockedEvent`, `ShipOrbitedEvent`, `ShipUndockedEvent`, and any other event whose
  sole consumer is a handler that immediately issues the next API call.
- Delete the corresponding event handler classes in `SpaceTraders.Application/EventHandlers/Ships`.
- Ensure the codebase compiles after removal.

Deliverables:

- No event or handler class exists for any instant (synchronous-return) ship action.

#### Phase 13c: Update goal executors to use direct sequential logic

Tasks:

- In every goal executor that previously relied on a now-deleted Tier 3 event to advance to the
  next step: replace the publish-and-return pattern with direct sequential logic within the same
  `ExecuteStepAsync` invocation.
  - Example: after `OrbitAsync()` succeeds, call the next action in the same method rather than
    publishing an event and returning.

Deliverables:

- Goal executors contain no publish calls for instant ship actions; all instant-action sequencing
  is internal to `ExecuteStepAsync`.

#### Phase 13d: Convert Tier 2 events

Tasks:

- For each Tier 2 event (e.g. `ResourceExtractedEvent`, `CargoSoldEvent`, `ContractDeliveredEvent`,
  `ConstructionSuppliedEvent`, `ShipPurchasedEvent`):
  - Keep the `IEventPublisher.Publish` call in the goal executor or command handler for observability.
  - Delete the handler class that previously reacted to the event.

Deliverables:

- Tier 2 events are published for observability (dashboards, integration tests) but no handler
  class reacts to them.

#### Phase 13e: Update tests

Tasks:

- Update unit and integration tests that previously asserted an intermediate Tier 3 event was
  published for an instant action; replace those assertions with assertions on the final outcome
  (e.g. ship is in orbit, the correct API was called, correct `GoalExecutionResult` was returned).
- Verify that tests for Tier 1 and Tier 2 events are unaffected.

Deliverables:

- Test suite passes with no references to deleted events.
- `ShipGoalExecutorService` communicates with the orchestrator only via `GoalCompletedEvent` and
  `GoalBlockedEvent`.
- The total number of event handler classes is substantially reduced.

### Phase 14: Scheduled future events (arrival and cooldown)

Goal: implement the mechanism that delivers `ShipArrivedEvent` and `ShipCooldownExpiredEvent` at
the correct future moment, replacing the polling tick loop for in-transit and cooling-down ships.

#### Phase 14a: `IShipEventScheduler` interface and database schema

Tasks:

- Add `IShipEventScheduler` to `SpaceTraders.Application/Interfaces`:
  ```csharp
  Task ScheduleArrivalAsync(string shipSymbol, Guid goalId, DateTimeOffset arrivalTime, CancellationToken ct);
  Task ScheduleCooldownExpiryAsync(string shipSymbol, Guid goalId, DateTimeOffset expiresAt, CancellationToken ct);
  Task CancelScheduledAsync(string shipSymbol, Guid goalId, CancellationToken ct);
  ```
- Add the `scheduled_ship_events` table migration:
  ```sql
  scheduled_ship_events (
      ship_symbol  TEXT        NOT NULL,
      goal_id      UUID        NOT NULL,
      event_kind   TEXT        NOT NULL,  -- 'Arrival' | 'CooldownExpiry'
      trigger_at   TIMESTAMPTZ NOT NULL,
      PRIMARY KEY (ship_symbol, goal_id)
  )
  ```

Deliverables:

- Interface and database schema exist; no implementation yet.

#### Phase 14b: `ShipEventScheduler` implementation

Tasks:

- Implement `ShipEventScheduler` as an `IHostedService` in `SpaceTraders.Infrastructure` backed by
  an in-memory priority queue ordered by trigger time.
- On startup, reload all pending rows from the `scheduled_ship_events` table so scheduled events
  survive a process restart.
- When a trigger time is reached, publish the corresponding `ShipArrivedEvent` or
  `ShipCooldownExpiredEvent` and delete the row from the database.

Deliverables:

- Scheduler fires events at the correct time and reloads pending events after a restart.

#### Phase 14c: Update goal executors to use the scheduler

Tasks:

- In every goal executor that issues a `Navigate` API call: after a successful response, call
  `IShipEventScheduler.ScheduleArrivalAsync(ship.Symbol, goal.GoalId, nav.Route.Arrival)` and
  return `GoalExecutionResult.Waiting`. Do **not** publish `ShipAutomationTickEvent`.
- In goal executors that issue `Extract`, `Siphon`, or `Survey` API calls: after a successful
  response containing a cooldown, call
  `IShipEventScheduler.ScheduleCooldownExpiryAsync(ship.Symbol, goal.GoalId, cooldown.Expiration)`
  and return `GoalExecutionResult.Waiting`.

Deliverables:

- No `ShipAutomationTickEvent` is published for a ship that is in transit or on cooldown.
- Ships in transit or on cooldown consume no CPU and produce no wasted tick-loop iterations.

#### Phase 14d: Arrival and cooldown event handlers

Tasks:

- Add `ShipArrivedEventHandler`: verifies that the `GoalId` in the event still matches the ship's
  currently active goal (stale wake-ups are silently ignored), then publishes
  `ShipAutomationTickEvent` to resume execution.
- Add `ShipCooldownExpiredEventHandler`: same stale-wake-up guard, then publishes
  `ShipAutomationTickEvent`.
- In `GoalCompletedEvent` and `GoalBlockedEvent` handlers, call
  `IShipEventScheduler.CancelScheduledAsync` to prevent ghost wake-ups after goal reassignment.

Deliverables:

- Ships resume goal execution at their estimated arrival time or cooldown expiry.
- Goal completion and blockage cancel any pending scheduled event for the ship.

#### Phase 14e: Scheduler unit tests

Tasks:

- Add unit tests for `ShipEventScheduler`:
  - Event fires at the correct time.
  - Persisted schedule is reloaded and fires correctly after a simulated restart.
  - Cancellation before trigger time prevents the event from firing.
  - A `GoalId` mismatch in the handler is silently ignored (no exception, no action).

Deliverables:

- A ship resumes goal execution at its estimated arrival time or cooldown expiry, even after a
  process restart.

### Phase 15: Observability read models

Goal: introduce lightweight, point-in-time read models that capture what the orchestrator is working
towards, what every ship is assigned to, and what every ship is currently doing. These models are
the foundation for the API endpoints and dashboard in Phases 16 and 17.

#### Phase 15a: `OrchestratorGoalChain`

Tasks:

- Add `OrchestratorGoalChain` record to `SpaceTraders.Application/Orchestration/ObservabilityModels.cs`:
  ```
  OrchestratorGoalChain:
    - fleetGoalKind          FleetGoalKind       // Contract | Construction | MarketCoverage | FleetExpansion
    - fleetGoalDescription   string              // human-readable top-level goal, e.g. "Supply Jump Gate at X1-TD7-JG"
    - resourceNeeds          ResourceNeedEntry[] // one per trade symbol required
  
  ResourceNeedEntry:
    - tradeSymbol            string              // "BAUXITE"
    - unitsNeeded            int                 // 500
    - unitsDelivered         int                 // 120
    - purposeDescription     string              // "needed for FRAME_DRONE × 2 to build Jump Gate"
    - assignedShips          string[]            // ship symbols currently assigned to produce this resource
  ```
- Add `IFleetStatusQueryService` to `SpaceTraders.Application/Interfaces`:
  ```csharp
  Task<IReadOnlyList<OrchestratorGoalChain>> GetGoalChainsAsync(CancellationToken ct);
  ```
- Implement `FleetStatusQueryService` in `SpaceTraders.Application/Services` that:
  - Reads active `FleetGoal` records from the orchestrator's in-memory state (or a persisted snapshot).
  - For each `Contract`/`Construction` goal, expands the resource needs with delivery progress.
  - Joins with `IShipGoalRepository` to populate `assignedShips`.
- Add unit tests covering: single construction goal with two resource needs, contract goal with
  partial delivery, and a goal with no assigned ships yet.

Deliverables:

- A single query returns the complete orchestrator intent as a structured, human-readable tree.

#### Phase 15b: `ShipAssignmentSnapshot`

Tasks:

- Add `ShipAssignmentSnapshot` record:
  ```
  ShipAssignmentSnapshot:
    - shipSymbol             string              // "X1-TD7-1"
    - goalKind               ShipGoalKind        // MineResource | SellCargo | Idle | …
    - goalDescription        string              // "Mining BAUXITE at X1-TD7-A3"
    - sourceWaypoint         string?             // waypoint the ship is extracting/siphoning from
    - destinationWaypoint    string?             // waypoint the ship is heading to for sell/delivery
    - fleetGoalDescription   string?             // the top-level orchestrator goal this serves,
                                                 //   e.g. "Supply Jump Gate at X1-TD7-JG"
    - assignedAt             DateTimeOffset
  ```
- Add `Task<IReadOnlyList<ShipAssignmentSnapshot>> GetAssignmentsAsync(CancellationToken ct)` to
  `IFleetStatusQueryService`.
- Implement the method by joining `IShipGoalRepository` active goals with the orchestrator's
  `FleetGoal` context to populate `fleetGoalDescription`.
- Add unit tests: ship with active mining goal, ship with idle goal, ship with no goal set.

Deliverables:

- Every ship's assignment is queryable with its full context (what, where, why).

#### Phase 15c: `ShipActivitySnapshot`

Tasks:

- Add `ShipActivitySnapshot` record:
  ```
  ShipActivitySnapshot:
    - shipSymbol             string              // "X1-TD7-1"
    - localStatus            ShipLocalStatus     // InOrbit | Docked | InTransit
    - currentWaypoint        string?             // waypoint the ship is at (null if in transit)
    - destinationWaypoint    string?             // destination when InTransit
    - estimatedArrival       DateTimeOffset?     // from scheduled arrival event, if applicable
    - onCooldown             bool
    - cooldownExpiresAt      DateTimeOffset?
    - cargoUsed              int
    - cargoCapacity          int
    - fuelCurrent            int
    - fuelCapacity           int
    - activityDescription    string              // plain-English summary, e.g.
                                                 //   "Moving to X1-TD7-A3 (arrives 14:23 UTC)"
                                                 //   "Extracting BAUXITE (cooldown 38 s)"
                                                 //   "Docked at X1-TD7-M12, selling cargo"
  ```
- Add `Task<IReadOnlyList<ShipActivitySnapshot>> GetShipActivitiesAsync(CancellationToken ct)` to
  `IFleetStatusQueryService`.
- Derive `activityDescription` from `localStatus`, `destinationWaypoint`, `estimatedArrival`,
  `onCooldown`, and the active goal kind using a small private factory method.
- Expose a single-ship variant: `Task<ShipActivitySnapshot?> GetShipActivityAsync(string shipSymbol, CancellationToken ct)`.
- Add unit tests for each `activityDescription` scenario.

Deliverables:

- Every ship's current state is queryable as a structured snapshot with a human-readable summary.

---

### Phase 16: Fleet status API endpoints

Goal: expose the three read models from Phase 15 as HTTP endpoints so that any client (a browser
dashboard, monitoring tool, or integration test) can poll the current fleet state.

#### Phase 16a: Controller and route definitions

Tasks:

- Add `FleetStatusController` to `SpaceTraders.Api/Controllers` with three GET endpoints:
  - `GET /api/fleet/goal-chains` → returns `IReadOnlyList<OrchestratorGoalChain>` as JSON.
  - `GET /api/fleet/assignments` → returns `IReadOnlyList<ShipAssignmentSnapshot>` as JSON.
  - `GET /api/fleet/activity` → returns `IReadOnlyList<ShipActivitySnapshot>` as JSON.
  - `GET /api/fleet/activity/{shipSymbol}` → returns `ShipActivitySnapshot?` for a single ship.
- All endpoints are read-only (`[HttpGet]`), require no request body, and return `200 OK` with
  JSON or `404 Not Found` for the single-ship variant when the symbol is unknown.
- Wire `IFleetStatusQueryService` into the controller via constructor injection.

Deliverables:

- Fleet status, assignment, and activity data is accessible over HTTP.

#### Phase 16b: Response DTO mapping

Tasks:

- Add `FleetGoalChainDto`, `ShipAssignmentDto`, and `ShipActivityDto` records in
  `SpaceTraders.Api/Dtos` that are the JSON-serializable shapes (camelCase property names, no
  domain enums directly on the wire — map to strings).
- Add `FleetStatusMapper` (static helper or AutoMapper profile) that converts domain read models to
  DTOs.
- Ensure `estimatedArrival` and `cooldownExpiresAt` are serialized as ISO 8601 strings.

Deliverables:

- API responses are stable, versioning-friendly JSON shapes decoupled from internal domain types.

#### Phase 16c: Caching and freshness

Tasks:

- Wrap `IFleetStatusQueryService` calls in a short-lived in-memory cache (5-second TTL) so that a
  dashboard polling every second does not trigger N database reads per request.
- Add a `Cache-Control: max-age=5` response header so clients can respect the TTL without
  explicitly polling faster than the data changes.

Deliverables:

- Fleet status endpoints are safe to poll at dashboard refresh rates without overloading the
  database.

#### Phase 16d: API endpoint tests

Tasks:

- Add integration-style controller tests:
  - `GetGoalChains` returns the correct chain count and resource need entries.
  - `GetAssignments` maps all active ship goals to DTOs correctly.
  - `GetActivity` returns a snapshot per ship with correct `activityDescription`.
  - `GetActivity/{shipSymbol}` returns `404` for an unknown symbol.

Deliverables:

- All four endpoints have request/response-level test coverage.

---

### Phase 17: Front-end fleet dashboard

Goal: build a single-page dashboard that visualises what the orchestrator is working towards, which
ship is assigned to what, and what every ship is currently doing. The dashboard refreshes
automatically and requires no back-end change to show new data.

#### Phase 17a: Project scaffold

Tasks:

- Add a `SpaceTraders.Dashboard` project (Blazor WebAssembly or a lightweight Vite + TypeScript SPA,
  depending on the existing front-end stack) under the solution root.
- Configure it to proxy `/api/*` requests to the back-end during development.
- Add three top-level pages / route components:
  - `/` → redirect to `/dashboard`
  - `/dashboard` → combined fleet overview (all three panels on one page)
  - `/ships/{symbol}` → detail view for a single ship

Deliverables:

- Project scaffolds and compiles with a placeholder "Fleet Dashboard" heading rendered in the browser.

#### Phase 17b: Orchestrator goal chain panel

Tasks:

- Add a `GoalChainPanel` component that fetches `GET /api/fleet/goal-chains` every 10 seconds.
- Render each `OrchestratorGoalChain` as a collapsible card with:
  - Header: top-level goal kind badge + `fleetGoalDescription`
    (e.g. **[CONSTRUCTION]** Supply Jump Gate at X1-TD7-JG).
  - Body: one row per `ResourceNeedEntry` showing:
    - Trade symbol chip (e.g. **BAUXITE**).
    - Progress bar: `unitsDelivered / unitsNeeded`.
    - `purposeDescription` tooltip (e.g. "needed for FRAME_DRONE × 2").
    - Assigned ships as clickable chips that deep-link to `/ships/{symbol}`.
- Show a "No active goals" placeholder when the list is empty.

Deliverables:

- The orchestrator's current intent is visible at a glance, including how far along each resource
  need is and which ships are covering it.

#### Phase 17c: Ship assignments panel

Tasks:

- Add a `AssignmentsPanel` component that fetches `GET /api/fleet/assignments` every 10 seconds.
- Render a sortable table with columns:
  | Ship | Goal | Source | Destination | Serving |
  |---|---|---|---|---|
  | X1-TD7-1 | Mine BAUXITE | X1-TD7-A3 | — | Supply Jump Gate at X1-TD7-JG |
- Highlight ships whose goal is `Idle` in a muted style.
- `Ship` column links to `/ships/{symbol}`.
- Allow filtering by goal kind (Mine / Siphon / Sell / Scout / Idle / …) via a dropdown.

Deliverables:

- All ship assignments are visible in a single table with context about why each ship has its goal.

#### Phase 17d: Ship activity panel

Tasks:

- Add a `ActivityPanel` component that fetches `GET /api/fleet/activity` every 5 seconds.
- Render a card per ship showing:
  - Ship symbol and a status badge (IN_TRANSIT / IN_ORBIT / DOCKED) with colour coding.
  - `activityDescription` as the primary text line
    (e.g. "Moving to X1-TD7-A3 · arrives 14:23 UTC").
  - Cargo fill bar: `cargoUsed / cargoCapacity`.
  - Fuel fill bar: `fuelCurrent / fuelCapacity`.
  - A countdown timer for `estimatedArrival` or `cooldownExpiresAt` when applicable, ticking
    down in the browser without additional API calls.
- Clicking a card navigates to `/ships/{symbol}`.

Deliverables:

- Real-time (5-second cadence) visibility into what every ship is physically doing right now.

#### Phase 17e: Single-ship detail page

Tasks:

- Add a `/ships/{symbol}` page that combines:
  - Live `ShipActivitySnapshot` (auto-refresh every 5 s).
  - The ship's current `ShipAssignmentSnapshot`.
  - The `OrchestratorGoalChain` entry(ies) that reference this ship.
  - A read-only log of the last N `GoalCompletedEvent` / `GoalBlockedEvent` records for the ship
    (requires a small `GET /api/fleet/activity/{shipSymbol}/history` endpoint and a matching
    query in `FleetStatusQueryService` that reads from a persisted event log table).

Deliverables:

- Operators can drill into any individual ship to see its current state, its assignment context,
  and a short history of recently completed or blocked goals.

#### Phase 17f: Front-end tests

Tasks:

- Add component tests (Vitest + Testing Library, or bUnit for Blazor) for:
  - `GoalChainPanel` renders the correct number of goal cards and progress bars from mock API data.
  - `AssignmentsPanel` renders all rows, sorts correctly, and filters by goal kind.
  - `ActivityPanel` renders status badges, fill bars, and countdown timers from mock data.
  - The countdown timer counts down in the browser and does not trigger extra API calls.
- Add an end-to-end smoke test (Playwright) that loads `/dashboard` against the running back-end
  and asserts that all three panels contain at least one entry.

Deliverables:

- Dashboard components have unit-level test coverage and a smoke end-to-end test.

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

- Contract / Construction resource need covered by current fleet → no new assignment
- Contract / Construction resource need with idle miner → assign idle miner
- Contract / Construction resource need with no idle suitable ship → fleet expansion goal emitted
- MarketCoverage goal: uncovered market → scout or patrol ship assigned
- MarketCoverage goal: all markets covered → no goal emitted
- FleetExpansion approved by budget → purchase command emitted; new ship assigned to bottleneck goal
- FleetExpansion blocked by budget → no purchase
- FleetExpansion blocked by max-ship cap → no purchase

### Integration-style tests

- Full assignment-to-goal-to-completion flow (Contract): orchestrator assigns resource goal, resolver
  selects waypoint, executor steps through to completion, `GoalCompletedEvent` published.
- Full assignment-to-goal-to-completion flow (MarketCoverage): orchestrator assigns market probe
  goal, resolver produces `PatrolMarket` ship goal, executor parks at the market.
- Blocked goal: executor detects missing capability, `GoalBlockedEvent` published, ship becomes
  idle, orchestrator re-evaluates on next tick.

---

## Acceptance Criteria

- A ship given a `MineResource` goal reaches the source, extracts, and sells without any
  external coordination after goal assignment.
- A ship given a `PatrolMarket` or `ScoutWaypoint` goal reaches and monitors the target market
  without any external coordination after goal assignment.
- The orchestrator never contains a waypoint symbol; all waypoints are resolved by the
  assignment resolver.
- All four `FleetGoalKind` values are handled: `Contract` and `Construction` via
  `ResourceProductionGoal`, `MarketCoverage` directly to a scout/patrol ship goal, and
  `FleetExpansion` via a purchase command.
- Capability mismatches are surfaced as `GoalBlockedEvent` within one execution step.
- Fleet expansion transitions from advisory to actionable: a purchase command is emitted and the
  new ship is assigned immediately.
- All prior acceptance criteria from `ship-automation-architecture-plan.md` continue to hold.
