# SpaceTraders Basics Reset Plan

## Goal

Reduce the project to the smallest useful automation slice so the behavior is easy to understand, debug, and extend.

The baseline after this reset is:

1. There is exactly one active plan: **Scout all marketplaces**.
2. The plan picks the **first ship with fuel** as the scout ship.
3. That plan computes a **traveling salesman route** starting from the ship's current waypoint.
4. The plan creates **one assignment at a time** in route order.
5. Ships have one responsibility: **navigate to a waypoint**.
6. Navigation includes only the minimum required steps:
   - refuel if possible at the current waypoint,
   - undock/orbit if docked,
   - navigate,
   - dock on arrival.

Everything else should be removed from the active flow or moved into a `Future` area and left unregistered.

---

## What stays

### 1. Single plan
- `Scout all marketplaces`
- One route
- One responsible ship
- Sequential assignment creation

### 2. Single ship behavior
- Move from current waypoint to target waypoint
- Refuel when at a market that sells fuel
- Leave dock if needed
- Navigate to target
- Dock when arrived
- Mark assignment complete

### 3. Simple orchestration
- If no active scout assignment exists, create the next one
- If an assignment is active, do not create another one for that ship
- When an assignment completes, create the next assignment in sequence

---

## What is out of scope for the reset

These should not be part of the active runtime path:

- Contract planning
- Construction planning
- Fleet expansion
- Mining
- Siphoning
- Trading
- Cargo selling logic
- Patrol/market freshness loops
- Multi-role ship automation
- Probe deployment logic
- Complex goal arbitration
- Any behavior that allows multiple handlers to decide navigation for the same ship

If useful to keep for later reference, move them into a `Future` folder/namespace and remove them from dependency registration and orchestrator selection.

---

## Current areas that look too advanced for the new baseline

Based on the current structure, these are likely candidates to remove from the active flow first:

### Orchestration and planning
- `SpaceTraders.Application/Orchestration/FleetOrchestrator.cs`
- `SpaceTraders.Application/Orchestration/FleetGoalEvaluators.cs`
- `SpaceTraders.Application/Services/ShipAssignmentPlanner.cs`
- `SpaceTraders.Application/Services/AssignmentResolver.cs`
- `SpaceTraders.Application/Goals/ShipGoalExecutorService.cs`

### Advanced goal executors
- Mining executors
- Siphoning executors
- Cargo delivery/selling executors
- Patrol market executor
- Any executor not required for simple waypoint navigation and marketplace scouting

### Automation that should be simplified
- `SpaceTraders.Application/Automation/GameLoopService.cs`
- Any startup/onboarding automation that creates roles or advanced plans

---

## Target simplified model

## A. Plan model
There is one plan instance:

- **Plan name:** Scout all marketplaces
- **Plan owner:** first ship with fuel
- **Plan output:** ordered list of marketplace waypoints
- **Plan execution style:** sequential, one active assignment at a time

Suggested plan state:

- Plan id
- Ship symbol
- Start waypoint
- Ordered marketplace list
- Current index
- Status

This can be stored explicitly or derived from assignments, but the behavior should stay sequential and obvious.

## B. Assignment model
Assignments should become very small and explicit.

Suggested active assignment shape:

- Ship symbol
- Type = `Scout`
- Destination waypoint
- Sequence number
- Status
- Assigned at
- Completed at

No trade symbols, contract ids, construction fields, or multi-purpose payloads are needed for the baseline scout flow.

## C. Ship behavior model
A ship should only know how to satisfy a waypoint assignment.

For one assignment `Go to waypoint X`:

1. If already at `X` and docked, complete assignment.
2. If already at `X` and in orbit, dock and complete assignment.
3. If docked somewhere else:
   - refuel if possible,
   - orbit.
4. If in orbit somewhere else:
   - navigate to `X`.
5. When arrival is detected:
   - dock,
   - complete assignment.

That is the entire baseline ship loop.

---

## Route planning: Scout all marketplaces

## Inputs
- All known marketplace waypoints
- Current waypoint of the first fueled ship

## Ship selection rule
Use the ship that has fuel in the starting setup.

Implementation rule for now:
- pick the only ship with fuel capacity/current fuel available,
- fail clearly if zero or more than one candidate exists.

This keeps startup behavior deterministic.

## Route rule
- Build a route that visits **all marketplace waypoints exactly once**.
- The route **starts at the current waypoint of the selected ship**.
- The produced order becomes the assignment sequence.

## TSP scope for the reset
Keep the first implementation narrow and predictable:

- Use waypoint coordinates already available in the repository/cache.
- Solve for marketplace ordering within the known reachable scope.
- If cross-system routing is not yet simple enough, explicitly limit the first implementation to the starting system and document that cross-system scouting is future work.

The important part is that the scouting plan owns the full visit order and emits assignments one by one.

---

## Proposed implementation phases

## Phase 1 - Freeze advanced behavior

### Objective
Stop advanced systems from participating in runtime decisions.

### Actions
- Remove or unregister advanced fleet goal evaluators.
- Remove or unregister advanced goal executors.
- Remove or bypass ship assignment planning that chooses between mining, trade, siphon, contracts, and scouting.
- Remove any duplicate navigation decision paths.
- Keep only the path needed for "ship has waypoint assignment -> ship navigates there".

### Result
The runtime has one obvious control path.

## LLM-sized slices

### Slice 1.1 - Inventory active runtime entry points
**Goal:** Identify every registered service/handler that can currently create work for ships.

**Do:**
- List dependency registrations for orchestrators, evaluators, planners, executors, automation services, and event handlers.
- Mark each one as `keep`, `disable`, or `move later`.

**Done when:**
- There is one explicit inventory list of runtime decision-makers.
- No guessing remains about where assignments/goals come from.

### Slice 1.1 status (completed)

Inventory source files reviewed:
- `SpaceTraders.Application/DependencyInjection.cs`
- `SpaceTraders.API/Program.cs`
- `SpaceTraders.API/Services/DeferredStartupHostedService.cs`
- `SpaceTraders.Application/Automation/GameLoopService.cs`
- `SpaceTraders.Application/Orchestration/FleetOrchestrator.cs`
- `SpaceTraders.Application/Orchestration/FleetGoalEvaluators.cs`
- `SpaceTraders.Application/Goals/ShipGoalExecutorService.cs`
- `SpaceTraders.Application/Commands/Fleet/AssignShipToGoalCommand.cs`
- `SpaceTraders.Application/Commands/Ships/AssignShipCommand.cs`
- `SpaceTraders.Application/Events/Handlers/Ships/ShipArrivedEventHandler.cs`
- `SpaceTraders.Application/Events/Handlers/Ships/ShipCooldownExpiredEventHandler.cs`
- `SpaceTraders.Application/EventHandlers/ContractPriorityHandler.cs`
- `SpaceTraders.Application/EventHandlers/FleetExpansionDecisionHandler.cs`
- `SpaceTraders.Application/EventHandlers/TradeOpportunityRecomputeHandler.cs`

| Runtime decision-maker | Registration / trigger | Current responsibility | Reset decision |
|---|---|---|---|
| `Automation.GameLoopService` | Singleton started by `DeferredStartupHostedService` | Calls `IFleetOrchestrator.EvaluateAndAssignAsync`, resumes goals on arrival, publishes low-fuel/API events | **disable** (contains advanced orchestration path) |
| `Orchestration.FleetOrchestrator` | `IFleetOrchestrator` scoped registration | Aggregates all fleet goals and assigns ships | **disable** |
| `Orchestration.ContractGoalEvaluator` | `IFleetGoalEvaluator` registration | Produces contract delivery goals | **disable** |
| `Orchestration.ConstructionGoalEvaluator` | `IFleetGoalEvaluator` registration | Produces construction/supply-chain goals | **disable** |
| `Orchestration.MarketCoverageGoalEvaluator` | `IFleetGoalEvaluator` registration | Produces market probe coverage goals | **disable** |
| `Orchestration.FleetExpansionGoalEvaluator` | `IFleetGoalEvaluator` registration | Produces fleet expansion goals | **disable** |
| `Orchestration.MarketScoutingGoalEvaluator` | `IFleetGoalEvaluator` registration | Produces market scouting goals in current orchestration model | **keep (temporary)** until single-plan service replaces evaluator path |
| `Commands.Fleet.AssignShipToGoalCommandHandler` | Wolverine command handler | Converts fleet goals into active ship goals, triggers first execution step | **disable** |
| `Goals.ShipGoalExecutorService` | `IShipGoalExecutorService` scoped registration | Main ship-level goal execution dispatcher | **keep and simplify** |
| `Goals.Executors.IdleGoalExecutor` | `IShipGoalExecutor` registration | Idle behavior | **keep (temporary)** |
| `Goals.Executors.MoveToWaypointGoalExecutor` | `IShipGoalExecutor` registration | Baseline navigation behavior | **keep** |
| `Goals.Executors.MineResourceGoalExecutor` | `IShipGoalExecutor` registration | Mining behavior | **disable** |
| `Goals.Executors.SiphonResourceGoalExecutor` | `IShipGoalExecutor` registration | Siphoning behavior | **disable** |
| `Goals.Executors.SellCargoGoalExecutor` | `IShipGoalExecutor` registration | Cargo selling behavior | **disable** |
| `Goals.Executors.DeliverCargoGoalExecutor` | `IShipGoalExecutor` registration | Cargo delivery/contract behavior | **disable** |
| `Goals.Executors.SupplyConstructionGoalExecutor` | `IShipGoalExecutor` registration | Construction supply behavior | **disable** |
| `Goals.Executors.ScoutWaypointGoalExecutor` | `IShipGoalExecutor` registration | Scout waypoint behavior | **keep and simplify** |
| `Goals.Executors.PatrolMarketGoalExecutor` | `IShipGoalExecutor` registration | Patrol/market freshness behavior | **disable** |
| `Commands.Ships.AssignShipHandler` | Wolverine command handler | Creates/updates ship assignments | **keep and simplify** (minimal assignment payload later) |
| `Events.Handlers.Ships.ShipArrivedEventHandler` | Scheduler event handler | Resumes goal execution on arrival | **keep and simplify** |
| `Events.Handlers.Ships.ShipCooldownExpiredEventHandler` | Scheduler event handler | Resumes goal execution after cooldown | **move later** (not needed for pure navigation baseline) |
| `EventHandlers.ContractPriorityHandler` | Domain event handler (`ContractDeadlineApproachingEvent`) | Emergency reassignment to contract work | **disable** |
| `EventHandlers.FleetExpansionDecisionHandler` | Domain event handlers (`GoalCompletedEvent`, `ContractFulfilledEvent`) | Triggers ship purchasing logic | **disable** |
| `Automation.ContractWatchService` | Singleton started by `DeferredStartupHostedService` | Emits contract deadline events | **disable** |
| `Automation.ShipRefreshWorkerService` | Singleton started by `DeferredStartupHostedService` | Periodic market/shipyard refresh for occupied waypoints | **move later** |
| `Automation.ResetAndReliabilityMonitorService` | Singleton started by `DeferredStartupHostedService` | Reset/reliability checks, may pause automation | **move later** |
| `EventHandlers.TradeOpportunityRecomputeHandler` | Event handler (`MarketDataRefreshedEvent`, `ShipyardDataRefreshedEvent`) | Recomputputes trade opportunities | **move later** |

Slice 1.1 outcome:
- Active runtime decision-makers are explicitly inventoried.
- Assignment/goal creation paths are now mapped and marked for `keep`, `disable`, or `move later`.
- This is a documentation-only slice; behavior changes start in Slice 1.2.

### Slice 1.2 - Disable advanced strategic evaluators
**Goal:** Stop contract, construction, market coverage, and fleet expansion evaluators from producing active work.

**Do:**
- Remove their registration or short-circuit their execution.
- Keep only the minimal path needed for the new scout plan.

**Done when:**
- The runtime no longer creates advanced fleet goals.
- Only baseline scouting work can be emitted.

### Slice 1.2 status (completed)

Changes made:
- Updated `SpaceTraders.Application/DependencyInjection.cs` evaluator registrations.
- Removed active DI registrations for:
  - `Orchestration.ContractGoalEvaluator`
  - `Orchestration.ConstructionGoalEvaluator`
  - `Orchestration.MarketCoverageGoalEvaluator`
  - `Orchestration.FleetExpansionGoalEvaluator`
- Kept active DI registration for:
  - `Orchestration.MarketScoutingGoalEvaluator`
- Kept supporting registrations required by current runtime wiring:
  - `Orchestration.IFleetCapacityEstimator`
  - `Orchestration.IBudgetPolicy`
  - `Orchestration.IFleetOrchestrator`

Notes:
- Initial Slice 1.2 DI change removed `IBudgetPolicy`, which broke service validation because `ShipGoalExecutorService` depends on it.
- `IBudgetPolicy`/`IFleetCapacityEstimator` were restored to keep the runtime valid while still disabling advanced strategic evaluator outputs.

Slice 1.2 outcome:
- Advanced strategic evaluators are no longer in active DI registration.
- Runtime strategic goal production is constrained to scouting evaluator output.

### Slice 1.3 - Disable advanced ship planners
**Goal:** Stop assignment planning from choosing between trade, mining, siphoning, and scouting.

**Do:**
- Bypass `ShipAssignmentPlanner` in active flow, or reduce it to the scout-only case.
- Remove fallback behavior that silently invents new work.

**Done when:**
- A ship cannot receive mining/trade/siphon/contract work through planner fallback.

### Slice 1.3 status (completed)

Verification performed:
- Checked active runtime registrations in `SpaceTraders.Application/DependencyInjection.cs`.
- Confirmed there is no active DI registration for `IShipAssignmentPlanner`/`ShipAssignmentPlanner`.
- Searched workspace for runtime call sites of `IShipAssignmentPlanner` and `ShipAssignmentPlanner.PlanAsync(...)`.
- Confirmed references are limited to `SpaceTraders.Application/Services/ShipAssignmentPlanner.cs` (self-contained implementation and tests), not active runtime orchestration/execution paths.

Runtime impact:
- `ShipAssignmentPlanner` fallback logic (trade/mining/siphon/scout/probe selection) is not reachable in the current runtime flow.
- Ships cannot receive mining/trade/siphon/contract work via planner fallback because planner is not wired into active runtime services.

Slice 1.3 outcome:
- Advanced ship planner path is effectively bypassed in active runtime.
- No code-path changes were required in this slice beyond explicit plan documentation and verification.

### Slice 1.4 - Disable advanced executors
**Goal:** Ensure only the baseline navigation/scouting executor path remains active.

**Do:**
- Unregister mining, siphon, cargo, patrol, and similar executors.
- Keep only executors needed for `navigate to waypoint` and `complete scout visit`.

**Done when:**
- Only the baseline executor set is active at runtime.

### Slice 1.4 status (completed)

Changes made:
- Updated `SpaceTraders.Application/DependencyInjection.cs` ship goal executor registrations.
- Disabled advanced executors by removing active DI registrations for:
  - `MineResourceGoalExecutor`
  - `SiphonResourceGoalExecutor`
  - `SellCargoGoalExecutor`
  - `DeliverCargoGoalExecutor`
  - `SupplyConstructionGoalExecutor`
  - `PatrolMarketGoalExecutor`
- Kept baseline executors active:
  - `IdleGoalExecutor` (temporary baseline support)
  - `MoveToWaypointGoalExecutor`
  - `ScoutWaypointGoalExecutor`

Slice 1.4 outcome:
- Runtime executor dispatch is constrained to baseline navigation/scouting path.
- Advanced mining/siphon/trade/contract/patrol execution paths are no longer active via DI.

### Slice 1.5 - Remove duplicate navigation decision paths
**Goal:** Make one component responsible for deciding the next movement action.

**Do:**
- Identify where movement can currently be triggered.
- Remove overlapping decision logic.

**Done when:**
- There is one obvious path from active assignment to navigation command.

### Slice 1.5 status (completed)

Duplicate movement/resume paths identified:
- `SpaceTraders.Application/Events/Handlers/Ships/ShipArrivedEventHandler.cs`
  - Scheduler-driven continuation path (`ShipArrivedEvent` → `IShipGoalExecutorService.ExecuteAsync`).
- `SpaceTraders.Application/Automation/GameLoopService.cs` (previous)
  - Dead-reckoning continuation path (`ApplyDeadReckoningAsync` → `IShipGoalExecutorService.ExecuteAsync`).

Change made:
- Removed dead-reckoning arrival continuation from `GameLoopService` by deleting `ApplyDeadReckoningAsync(...)` invocation and method.
- Kept scheduler/event-driven continuation path as the single movement decision trigger after in-transit travel.

Resulting control path:
- `NavigateShipCommand` schedules arrival via `IShipEventScheduler`.
- `ShipEventScheduler` publishes `ShipArrivedEvent`.
- `ShipArrivedEventHandler` is the single continuation path that resumes goal execution.

Slice 1.5 outcome:
- Duplicate arrival-triggered movement decision logic removed.
- One obvious assignment-to-navigation continuation path remains in active runtime.

---

## Phase 2 - Introduce a single scout plan

### Objective
Create one service responsible for the full marketplace scouting sequence.

### Responsibilities
- Load ships
- Select the first fueled ship
- Load marketplace waypoints
- Compute ordered route starting from ship location
- Create the first assignment only
- After each completion, create the next assignment

### Suggested new components
- `ScoutAllMarketplacesPlanService`
- `MarketplaceRoutePlanner`
- `ScoutAssignmentSequencer`

Names can vary, but responsibilities should stay separate.

## LLM-sized slices

### Slice 2.1 - Add scout plan state model
**Goal:** Introduce a minimal representation of the single active plan.

**Do:**
- Add a plan model or equivalent persisted state.
- Include ship symbol, start waypoint, route, current index, and status.

**Done when:**
- The system can represent the progress of one scout-all-marketplaces plan.

### Slice 2.1 status (completed)

Changes made:
- Added scout plan domain state model in `SpaceTraders.Application/Automation/ScoutAllMarketplacesPlanState.cs`:
  - `ScoutAllMarketplacesPlanState`
  - `ScoutPlanStatus` (`None`, `Active`, `Completed`)
- Added plan persistence contract:
  - `SpaceTraders.Application/Interfaces/Repositories/IScoutPlanRepository.cs`
- Added persistence entity and repository:
  - `SpaceTraders.Infrastructure.Persistence/Entities/ScoutPlanStateRecord.cs`
  - `SpaceTraders.Infrastructure.Persistence/Repositories/ScoutPlanRepository.cs`
- Wired persistence into runtime registration:
  - Registered `IScoutPlanRepository` in `SpaceTraders.Infrastructure.Persistence/DependencyInjection.cs`
- Added EF model mapping and scoped storage:
  - Added `DbSet<ScoutPlanStateRecord>` and model configuration in `SpaceTraders.Infrastructure.Persistence/SpaceTradersDbContext.cs`
  - Agent-scoped single-row key (`AgentToken`) with route JSON payload
- Added database initialization DDL for PostgreSQL:
  - `scout_plan_states` table creation in `SpaceTraders.Infrastructure.Persistence/SpaceTradersDatabaseInitializer.cs`

Validation run for this slice:
- `dotnet build SpaceTraders.slnx` (via build tool): **successful**
- `SpaceTraders.Infrastructure.Tests.ScoutPlanRepositoryTests`: **3 passed, 0 failed**

Slice 2.1 outcome:
- The system now has a persisted, agent-scoped representation of one scout-all-marketplaces plan with required fields: ship symbol, start waypoint, route, current index, and status.

### Slice 2.2 - Add deterministic scout ship selection
**Goal:** Pick the startup scout ship in a single predictable way.

**Do:**
- Load ships.
- Select the only ship with fuel.
- Fail clearly if zero or multiple candidates exist.

**Done when:**
- The chosen ship is deterministic and validated.

### Slice 2.2 status (completed)

Changes made:
- Added dedicated deterministic selector service:
  - `SpaceTraders.Application/Automation/ScoutShipSelectionService.cs`
  - `IScoutShipSelectionService.SelectAsync(...)`
- Selection rule implemented exactly for this slice:
  - candidate ships are those with `FuelCapacity > 0` and `FuelCurrent > 0`
  - returns the single candidate when exactly one exists
  - throws clear `InvalidOperationException` when zero candidates exist
  - throws clear `InvalidOperationException` when multiple candidates exist (includes candidate symbols)
- Registered service in DI for active runtime usage:
  - `SpaceTraders.Application/DependencyInjection.cs`

Tests added:
- `tests/SpaceTraders.Application.Tests/Automation/ScoutShipSelectionServiceTests.cs`
  - `SelectAsync_ReturnsOnlyShipWithFuel`
  - `SelectAsync_Throws_WhenNoShipHasFuel`
  - `SelectAsync_Throws_WhenMultipleShipsHaveFuel`

Validation run for this slice:
- `dotnet build SpaceTraders.slnx` (via build tool): **successful**
- `SpaceTraders.Application.Tests.Automation.ScoutShipSelectionServiceTests`: **3 passed, 0 failed**

Slice 2.2 outcome:
- Startup scout-ship selection is now deterministic, explicit, and validated with clear failure behavior when startup assumptions are violated.

### Slice 2.3 - Add marketplace discovery
**Goal:** Build the list of candidate waypoints the plan must visit.

**Do:**
- Load known waypoints.
- Filter to marketplaces.
- Limit to the starting system for the first implementation.

**Done when:**
- The plan can produce the exact set of waypoints to scout.

### Slice 2.3 status (completed)

Changes made:
- Added dedicated marketplace discovery service:
  - `SpaceTraders.Application/Automation/ScoutMarketplaceDiscoveryService.cs`
  - `IScoutMarketplaceDiscoveryService.DiscoverAsync(...)`
- Discovery behavior implemented for baseline scope:
  - resolves starting system from `ShipModel.SystemSymbol` (or derives from `ShipModel.WaypointSymbol`)
  - loads waypoints only for the starting system via `IWaypointRepository.GetBySystemAsync(...)`
  - filters strictly to `HasMarket == true`
  - returns deterministically ordered results by waypoint symbol
- Added clear failure behavior when no starting system can be resolved.
- Registered service in DI for active runtime usage:
  - `SpaceTraders.Application/DependencyInjection.cs`

Tests added:
- `tests/SpaceTraders.Application.Tests/Automation/ScoutMarketplaceDiscoveryServiceTests.cs`
  - `DiscoverAsync_ReturnsOnlyMarketWaypointsInStartingSystem`
  - `DiscoverAsync_UsesWaypointSymbol_WhenSystemSymbolMissing`
  - `DiscoverAsync_Throws_WhenStartingSystemCannotBeResolved`

Validation run for this slice:
- `dotnet build SpaceTraders.slnx` (via build tool): **successful**
- `SpaceTraders.Application.Tests.Automation.ScoutMarketplaceDiscoveryServiceTests`: **3 passed, 0 failed**

Slice 2.3 outcome:
- The scout plan can now produce the exact candidate marketplace set for the baseline implementation: known markets in the scout ship's starting system.

### Slice 2.4 - Add route planner contract
**Goal:** Separate route calculation from orchestration.

**Do:**
- Add a route planner interface and implementation.
- Accept start waypoint plus candidate marketplaces.
- Return ordered waypoint symbols.

**Done when:**
- Orchestration depends on a route planner, not inline routing logic.

### Slice 2.4 status (completed)

Changes made:
- Added route planner contract and implementation:
  - `SpaceTraders.Application/Automation/MarketplaceRoutePlanner.cs`
  - `IMarketplaceRoutePlanner.BuildRouteAsync(startWaypointSymbol, candidateMarketplaces, ...)`
- Route planner contract shape for baseline phase:
  - accepts starting waypoint symbol plus candidate marketplace waypoints
  - returns ordered waypoint symbols as the route output
- Baseline deterministic implementation (contract-first, algorithm-light):
  - validates non-empty start symbol
  - deduplicates candidate waypoint symbols
  - returns deterministic symbol ordering
  - ensures start waypoint is first when present in candidate set
- Registered route planner in DI:
  - `SpaceTraders.Application/DependencyInjection.cs`

Tests added:
- `tests/SpaceTraders.Application.Tests/Automation/MarketplaceRoutePlannerTests.cs`
  - `BuildRouteAsync_StartWaypointInCandidates_IsFirstInRoute`
  - `BuildRouteAsync_StartWaypointMissing_ReturnsDeterministicOrderedRoute`
  - `BuildRouteAsync_DeduplicatesCandidateSymbols`

Validation run for this slice:
- `dotnet build SpaceTraders.slnx` (via build tool): **successful**
- `SpaceTraders.Application.Tests.Automation.MarketplaceRoutePlannerTests`: **3 passed, 0 failed**

Slice 2.4 outcome:
- Scout orchestration can now depend on a dedicated route-planner abstraction instead of inline route logic, with deterministic output suitable for the next slice's route algorithm refinement.

### Slice 2.5 - Add first working route algorithm
**Goal:** Produce a correct visit order before optimizing.

**Do:**
- Use coordinates from cached waypoints.
- Implement nearest-neighbor first if needed.
- Treat exact TSP improvement as a later refinement unless it is already small enough.

**Done when:**
- The service returns a stable ordered route beginning at the ship's current waypoint.

### Slice 2.5 status (completed)

Changes made:
- Refined route algorithm in `SpaceTraders.Application/Automation/MarketplaceRoutePlanner.cs`:
  - implemented first working nearest-neighbor route selection using cached waypoint coordinates (`X`, `Y`)
  - distance heuristic uses squared Euclidean distance for stable, efficient comparison
  - route starts at the provided `startWaypointSymbol` when present in candidates
  - if the start waypoint is not in candidates, route falls back deterministically to symbol ordering
  - candidate symbols are deduplicated case-insensitively
  - equal-distance ties are resolved deterministically by waypoint symbol ordering

Tests updated/added:
- `tests/SpaceTraders.Application.Tests/Automation/MarketplaceRoutePlannerTests.cs`
  - retained baseline contract tests from slice 2.4
  - added geometry-driven nearest-neighbor test:
    - `BuildRouteAsync_UsesNearestNeighbor_ByWaypointCoordinates`
  - added deterministic tie-break regression test:
    - `BuildRouteAsync_TieBreaksBySymbol_WhenDistancesAreEqual`

Validation run for this slice:
- `dotnet build SpaceTraders.slnx` (via build tool): **successful**
- `SpaceTraders.Application.Tests.Automation.MarketplaceRoutePlannerTests`: **5 passed, 0 failed**

Slice 2.5 outcome:
- The scout route planner now returns a stable, coordinate-based visit order beginning from the ship's current/start waypoint when available, satisfying the first working route algorithm requirement for the reset baseline.

### Slice 2.6 - Add plan bootstrap
**Goal:** Assemble and link the components to execute the scout plan from beginning to end.

**Do:**
- Connect ship loading, route planning, and assignment creation into a single flow.
- Trigger the flow from the game loop or a dedicated service.

**Done when:**
- The system can execute the scout plan startup sequence.

### Slice 2.6 status (completed)

Changes made:
- Added dedicated scout plan bootstrap service:
  - `SpaceTraders.Application/Automation/ScoutAllMarketplacesPlanService.cs`
  - `IScoutAllMarketplacesPlanService.EnsureBootstrappedAsync(...)`
- Bootstrapping flow implemented end-to-end for startup/tick:
  - checks whether a scout plan already exists via `IScoutPlanRepository`
  - selects deterministic scout ship via `IScoutShipSelectionService`
  - discovers marketplaces in starting system via `IScoutMarketplaceDiscoveryService`
  - computes ordered route via `IMarketplaceRoutePlanner`
  - persists `ScoutAllMarketplacesPlanState` with `CurrentRouteIndex = 0` and `Status = Active`
  - creates the first scout assignment (`AssignmentType = "Scout"`, destination = first route waypoint)
  - guards against duplicate assignment creation when an active assignment already exists for the scout ship
- Registered bootstrap service in DI:
  - `SpaceTraders.Application/DependencyInjection.cs`
- Triggered bootstrap execution from runtime tick:
  - `SpaceTraders.Application/Automation/GameLoopService.cs`
  - invokes `EnsureBootstrappedAsync(...)` at leader tick scope before downstream evaluation

Tests added:
- `tests/SpaceTraders.Application.Tests/Automation/ScoutAllMarketplacesPlanServiceTests.cs`
  - `EnsureBootstrappedAsync_CreatesPlanAndFirstAssignment_WhenNoPlanExists`
  - `EnsureBootstrappedAsync_DoesNothing_WhenPlanAlreadyExists`
  - `EnsureBootstrappedAsync_DoesNotCreateAssignment_WhenActiveAssignmentExists`

Validation run for this slice:
- `dotnet build SpaceTraders.slnx` (via build tool): **successful**
- `SpaceTraders.Application.Tests.Automation.ScoutAllMarketplacesPlanServiceTests`: **3 passed, 0 failed**

Slice 2.6 outcome:
- The system can now bootstrap the single scout plan startup sequence in one connected flow: ship selection → marketplace discovery → route creation → plan persistence → first assignment creation.

---

## Phase 3 - Reduce assignment payloads

### Objective
Make assignments reflect only what the ship must do now.

### Actions
- Keep assignment type focused on `Scout` / `NavigateToWaypoint`
- Store only destination and sequence metadata
- Remove unused fields from the active flow
- Keep legacy fields only if persistence migration cost is too high, but do not use them

### Result
Assignments are readable and easy to inspect in the database and logs.

## LLM-sized slices

### Slice 3.1 - Define minimal active assignment contract
**Goal:** Decide the smallest assignment shape needed by the reset.

**Do:**
- Standardize on ship symbol, type, destination, sequence number, assigned at, completed at, and status.
- Document which legacy fields are ignored.

**Done when:**
- There is one agreed assignment contract for the baseline flow.

### Slice 3.1 status (completed)

Baseline assignment contract established in `SpaceTraders.Application/DTOs/ApplicationDtos.cs`:

| Field | Baseline? | Notes |
|---|---|---|
| `ShipSymbol` | ✅ Keep | Primary key for the assignment |
| `AssignmentType` | ✅ Keep | `"Scout"` in the baseline flow |
| `DestWaypoint` | ✅ Keep | Single destination the ship must reach |
| `StepIndex` | ✅ Keep | Sequence number in the route |
| `AssignedAt` | ✅ Keep | Timestamp set on creation |
| `CompletedAt` | ✅ Keep | Null = active; set = complete |
| `OriginWaypoint` | ❌ Legacy – ignored | Not used by baseline scout navigation |
| `CargoSymbol` | ❌ Legacy – ignored | Cargo/trade fields |
| `ContractId` | ❌ Legacy – ignored | Contract fields |
| `PurchaseUnitPrice` | ❌ Legacy – ignored | Trade fields |
| `RequiredUnits` | ❌ Legacy – ignored | Trade/construction fields |
| `SupplyCompleted` | ❌ Legacy – ignored | Construction supply fields |

Changes made:
- Added XML doc comments to `ShipAssignmentDto` explicitly listing baseline fields and calling out legacy-ignored fields.
- No persistence schema changes; legacy columns kept for migration compatibility.

Validation run for this slice:
- `dotnet build SpaceTraders.slnx` (via build tool): **successful**

Slice 3.1 outcome:
- One agreed assignment contract for the baseline flow is now documented in code.
- Subsequent slices (3.2–3.4) can use this contract as the authoritative reference.

### Slice 3.2 - Update assignment creation path
**Goal:** Make new scout assignments use the minimal contract.

**Do:**
- Change assignment creation to populate only baseline fields.
- Keep persistence compatibility if required.

**Done when:**
- Newly created assignments are simple and consistent.

### Slice 3.2 status (completed)

Changes made:
- Added `ShipAssignmentDto.CreateScout(shipSymbol, destinationWaypoint, stepIndex, assignedAt)` factory method in `SpaceTraders.Application/DTOs/ApplicationDtos.cs`.
  - Only accepts baseline fields; leaves all legacy fields at defaults.
- Updated `ScoutAllMarketplacesPlanService.EnsureBootstrappedAsync` to use `ShipAssignmentDto.CreateScout(...)` instead of the full legacy constructor.
- `StepIndex` is now explicitly set from `plan.CurrentRouteIndex` to link sequence number to plan state.

Validation run for this slice:
- `dotnet build SpaceTraders.slnx` (via build tool): **successful**
- `SpaceTraders.Application.Tests.Automation.ScoutAllMarketplacesPlanServiceTests`: **3 passed, 0 failed**

Slice 3.2 outcome:
- Scout assignment creation enforces the minimal contract through a dedicated factory method.
- Legacy fields are never populated by the baseline scout flow.

### Slice 3.3 - Add sequencing metadata
**Goal:** Preserve the order generated by the scout plan.

**Do:**
- Add sequence number or current-index linkage.
- Ensure the next assignment is unambiguous.

**Done when:**
- The assignment stream clearly reflects route order.

### Slice 3.3 status (completed)

Verification performed:
- `ShipAssignmentDto.StepIndex` (sequence number) exists in the assignment model and is included in persistence.
- `ScoutAllMarketplacesPlanService.EnsureBootstrappedAsync` sets `StepIndex` from `plan.CurrentRouteIndex` (wired in Slice 3.2).
- Existing test for bootstrap asserts `a.StepIndex == 0` matching `CurrentRouteIndex = 0`.
- When Phase 5 advances the index, subsequent assignments will naturally carry the correct sequence number via the same `plan.CurrentRouteIndex` reference.

No additional code changes required; sequencing metadata is already present and linked to plan state.

Slice 3.3 outcome:
- Assignment sequence numbers are explicitly derived from plan route index, making the assignment stream unambiguous.

### Slice 3.4 - Remove active reliance on legacy payload fields
**Goal:** Stop baseline execution from depending on cargo/contract/construction data.

**Do:**
- Remove reads of unused assignment fields from active code paths.
- Keep old columns/properties only if migration cost is not worth paying yet.

**Done when:**
- Baseline scout execution only depends on destination-focused assignment data.

### Slice 3.4 status (completed)

Verification performed:
- Reviewed active executor files: `ScoutWaypointGoalExecutor.cs`, `MoveToWaypointGoalExecutor.cs`, `IdleGoalExecutor.cs`.
- None of the active (registered) executors read `CargoSymbol`, `ContractId`, `PurchaseUnitPrice`, `RequiredUnits`, or `SupplyCompleted` from the assignment DTO.
- Reads of legacy fields exist only in disabled executors (`SellCargoCommand`, `SupplyConstructionCommand`) which are no longer registered in DI (removed in Slice 1.4).
- Legacy database columns retained for migration compatibility; no schema change needed.

No additional code changes required.

Slice 3.4 outcome:
- Baseline scout execution does not depend on any legacy payload fields.
- Legacy columns exist in persistence only; no active code path reads them in the baseline flow.

---

## Phase 4 - Make ship execution purely navigation-based

### Objective
A ship executor should only drive movement to a target waypoint.

### Actions
- Reuse or simplify the existing waypoint movement executor
- Ensure only state-valid commands are used:
  - docked state can refuel and orbit
  - in-orbit state can navigate and dock
  - in-transit waits for arrival
- On arrival, dock and complete the assignment

### Notes
The existing state-scoped command acceptors are a good fit for this simplified model and should be kept.

## LLM-sized slices

### Slice 4.1 - Define the baseline ship state machine
**Goal:** Write down the exact allowed transitions for the reset.

**Do:**
- Document docked, in orbit, and in transit behavior.
- Make completion rules explicit.

**Done when:**
- There is one short state machine for baseline ship movement.

### Slice 4.1 status (completed)

Baseline ship movement state machine (single assignment destination `X`):

| Current state | Condition | Allowed action | Next state / outcome |
|---|---|---|---|
| `DOCKED` | `Waypoint == X` | Complete assignment | `Completed` |
| `DOCKED` | `Waypoint != X` | Refuel if possible, then `Orbit` | `IN_ORBIT` |
| `IN_ORBIT` | `Waypoint == X` | `Dock`, then complete assignment | `Completed` |
| `IN_ORBIT` | `Waypoint != X` | `Navigate(X)` | `IN_TRANSIT` |
| `IN_TRANSIT` | Any | Wait for scheduled arrival event | Resume on arrival continuation |

Completion rules:
- Assignment is complete only when ship is docked at the destination waypoint.
- If the ship arrives in orbit, it must dock before completing.
- In-transit state never issues new navigation decisions.

Validation run for this slice:
- `dotnet build SpaceTraders.slnx` (via build tool): **successful**
- `SpaceTraders.Application.Tests.Goals.MoveToWaypointGoalExecutorTests`: **8 passed, 0 failed**
- `SpaceTraders.Application.Tests.Goals.ScoutWaypointGoalExecutorTests`: **8 passed, 0 failed**

Slice 4.1 outcome:
- One explicit baseline state machine is now defined for navigation-only ship execution.

### Slice 4.2 - Simplify docked behavior
**Goal:** Make docked ships do only the minimum baseline work.

**Do:**
- If at target: complete.
- If not at target: refuel if possible, then orbit.

**Done when:**
- Docked behavior contains no advanced branches.

### Slice 4.2 status (completed)

Changes made:
- Updated `MoveToWaypointGoalExecutor` docked branch:
  - complete immediately when already docked at target.
  - when not at target: refuel if possible (`ctx.CurrentWaypointSellsFuel`), then orbit.
- Updated `ScoutWaypointGoalExecutor` docked branch:
  - kept docked-at-target completion behavior.
  - when not at target: refuel if possible, then orbit.
- Added docked refuel tests in:
  - `tests/SpaceTraders.Application.Tests/Goals/MoveToWaypointGoalExecutorTests.cs`
  - `tests/SpaceTraders.Application.Tests/Goals/ScoutWaypointGoalExecutorTests.cs`

Validation run for this slice:
- `dotnet build SpaceTraders.slnx` (via build tool): **successful**
- `SpaceTraders.Application.Tests.Goals.MoveToWaypointGoalExecutorTests`: **10 passed, 0 failed**
- `SpaceTraders.Application.Tests.Goals.ScoutWaypointGoalExecutorTests`: **10 passed, 0 failed**

Slice 4.2 outcome:
- Docked behavior now follows baseline-only navigation flow with no advanced branches.

### Slice 4.3 - Simplify in-orbit behavior
**Goal:** Make in-orbit ships either dock or navigate.

**Do:**
- If at target: dock and complete.
- Otherwise: navigate to destination.

**Done when:**
- In-orbit behavior contains no extra task logic.

### Slice 4.3 status (completed)

Changes made:
- Updated `MoveToWaypointGoalExecutor` in-orbit behavior:
  - if at target: `DockAsync` then complete.
  - otherwise: navigate to destination.
- `ScoutWaypointGoalExecutor` already matched this baseline shape (dock+refresh+complete at target, otherwise navigate).
- Updated test `ExecuteStepAsync_AlreadyAtTarget_Completes` in `MoveToWaypointGoalExecutorTests` to assert `DockAsync` is called before completing.
- Added `ExecuteStepAsync_DockedAtTarget_CompletesWithoutDockCommand` to confirm no duplicate dock when already docked.

Validation run for this slice:
- `dotnet build SpaceTraders.slnx` (via build tool): **successful**
- `SpaceTraders.Application.Tests.Goals.MoveToWaypointGoalExecutorTests`: **11 passed, 0 failed**
- `SpaceTraders.Application.Tests.Goals.ScoutWaypointGoalExecutorTests`: **10 passed, 0 failed**

Slice 4.3 outcome:
- In-orbit decision path is now strictly `dock+complete` or `navigate`.

### Slice 4.4 - Simplify in-transit behavior
**Goal:** Ensure in-transit ships only wait for arrival.

**Do:**
- Return waiting state.
- Let arrival resume execution.

**Done when:**
- No in-transit code attempts to make new decisions prematurely.

### Slice 4.4 status (completed)

Verification performed:
- `MoveToWaypointGoalExecutor`: in-transit immediately returns `WaitingForArrival`, no further commands issued.
- `ScoutWaypointGoalExecutor`: in-transit immediately returns `WaitingForArrival`, no further commands issued.
- No in-transit branch issues refuel, dock, or navigate commands.

Validation run for this slice:
- `dotnet build SpaceTraders.slnx` (via build tool): **successful**
- `SpaceTraders.Application.Tests.Goals.MoveToWaypointGoalExecutorTests`: **11 passed, 0 failed**
- `SpaceTraders.Application.Tests.Goals.ScoutWaypointGoalExecutorTests`: **10 passed, 0 failed**

Slice 4.4 outcome:
- In-transit behavior is wait-only; continuation is fully driven by the scheduled arrival event.

### Slice 4.5 - Add refuel-if-possible behavior
**Goal:** Keep refueling narrow and local.

**Do:**
- Detect whether current waypoint can supply fuel.
- Attempt refuel only from docked state.
- If refuel is not possible, continue without branching into new roles.

**Done when:**
- Refueling is part of navigation preparation, not a separate strategy.

### Slice 4.5 status (completed)

Changes made:
- Refueling is gated on `ctx.CurrentWaypointSellsFuel` in both `MoveToWaypointGoalExecutor` and `ScoutWaypointGoalExecutor`.
- Refuel is issued only when docked and not at target (navigation preparation step).
- When `CurrentWaypointSellsFuel` is false, executors orbit immediately with no refuel attempt.
- Tests verify both paths: refuel-then-orbit when fuel available, orbit-only when unavailable.

Validation run for this slice:
- `dotnet build SpaceTraders.slnx` (via build tool): **successful**
- `SpaceTraders.Application.Tests.Goals.MoveToWaypointGoalExecutorTests`: **11 passed, 0 failed**
- `SpaceTraders.Application.Tests.Goals.ScoutWaypointGoalExecutorTests`: **10 passed, 0 failed**

Slice 4.5 outcome:
- Refueling is a narrow, local navigation-preparation action; no separate strategy or role is triggered.

### Slice 4.6 - Complete assignment on successful arrival flow
**Goal:** Close the loop after reaching the waypoint.

**Do:**
- Dock at target.
- Mark assignment complete.
- Trigger next-assignment creation.

**Done when:**
- Reaching the waypoint advances the scout plan.

### Slice 4.6 status (completed)

Changes made:
- Added `AdvanceAsync(shipSymbol, cancellationToken)` to `IScoutAllMarketplacesPlanService` and implemented it in `ScoutAllMarketplacesPlanService`:
  - loads active plan; ignores if none, if plan belongs to a different ship, or if plan is not `Active`.
  - increments `CurrentRouteIndex` and persists updated plan.
  - when more waypoints remain: creates next scout assignment via `ShipAssignmentDto.CreateScout(...)` with the new index.
  - when all waypoints are visited: persists `Status = Completed` and creates no new assignment.
- Updated `ShipGoalExecutorService`:
  - injected `IScoutAllMarketplacesPlanService`.
  - on `GoalExecutionOutcome.Completed` for a `ScoutWaypointGoal`: calls `scoutPlanService.AdvanceAsync(ship.Symbol)` instead of assigning `"Idle"`.
  - on `GoalExecutionOutcome.Completed` for any other goal type: existing `AssignShipCommand("Idle")` path is unchanged.

Tests added:
- `tests/SpaceTraders.Application.Tests/Goals/ShipGoalExecutorServiceTests.cs`
  - `ExecuteAsync_WhenScoutGoalCompleted_AdvancesScoutPlan`
  - `ExecuteAsync_WhenScoutGoalCompleted_DoesNotAssignIdle`
  - `ExecuteAsync_WhenNonScoutGoalCompleted_AssignsIdleAndDoesNotAdvanceScoutPlan`
- `tests/SpaceTraders.Application.Tests/Automation/ScoutAllMarketplacesPlanServiceTests.cs`
  - `AdvanceAsync_CreatesNextAssignment_WhenMoreWaypointsRemain`
  - `AdvanceAsync_MarksplanComplete_WhenLastWaypointReached`
  - `AdvanceAsync_DoesNothing_WhenNoPlanExists`
  - `AdvanceAsync_DoesNothing_WhenPlanBelongsToDifferentShip`

Validation run for this slice:
- `dotnet build SpaceTraders.slnx` (via build tool): **successful**
- `SpaceTraders.Application.Tests.Goals.ShipGoalExecutorServiceTests`: **18 passed, 0 failed**
- `SpaceTraders.Application.Tests.Automation.ScoutAllMarketplacesPlanServiceTests`: **7 passed, 0 failed**

Slice 4.6 outcome:
- The loop is now closed: a completed scout goal advances the plan to the next waypoint, creates the next assignment, or marks the plan complete when all waypoints are visited.

---

## Phase 5 - Make completion drive the next assignment

### Objective
The scout plan should progress only when the current waypoint assignment finishes.

### Actions
- On assignment completion, ask the scout plan for the next waypoint in sequence
- If another waypoint exists, create the next assignment
- If no waypoint remains, mark the plan complete

### Result
The system becomes easy to reason about:
- one plan
- one route
- one assignment
- one next step

## LLM-sized slices

### Slice 5.1 - Detect scout assignment completion
**Goal:** Have one reliable completion trigger.

**Do:**
- Pick the completion point in the baseline ship flow.
- Publish or invoke the next-step handler from there.

**Done when:**
- Completing an assignment always hits one continuation path.

### Slice 5.1 status (completed)

The completion point already existed: `ShipGoalExecutorService.HandleResultAsync` calls `scoutPlanService.AdvanceAsync(ship.Symbol)` when a `ScoutWaypointGoal` completes. The gap was that the previous assignment was never stamped with `CompletedAt`, so `EnsureBootstrappedAsync` would see it as still active on the next cycle.

Changes made:
- Updated `ScoutAllMarketplacesPlanService.AdvanceAsync`:
  - loads the current assignment via `IShipAssignmentRepository.FindAsync`.
  - if a non-completed assignment exists, upserts it with `CompletedAt = now` before proceeding.
  - this fires for both the "more waypoints remain" path and the "plan complete" path.
  - idempotent: if the assignment is already completed, skips the upsert.

Tests added:
- `tests/SpaceTraders.Application.Tests/Automation/ScoutAllMarketplacesPlanServiceTests.cs`
  - `AdvanceAsync_MarksCurrentAssignmentComplete_WhenMoreWaypointsRemain`
  - `AdvanceAsync_MarksCurrentAssignmentComplete_WhenLastWaypointReached`
  - `AdvanceAsync_DoesNotMarkComplete_WhenAssignmentAlreadyCompleted`

Validation run for this slice:
- `dotnet build SpaceTraders.slnx` (via build tool): **successful**
- `SpaceTraders.Application.Tests.Automation.ScoutAllMarketplacesPlanServiceTests`: **10 passed, 0 failed**
- Full `SpaceTraders.Application.Tests`: **393 passed, 0 failed**

Slice 5.1 outcome:
- There is now exactly one reliable completion trigger: when a scout goal completes in `ShipGoalExecutorService`, `AdvanceAsync` is called, which marks the current assignment completed before advancing the plan.

### Slice 5.2 - Read next waypoint from plan state
**Goal:** Let the plan, not the ship, decide what comes next.

**Do:**
- Load current plan progress.
- Advance index.
- Resolve next destination.

**Done when:**
- The next assignment is derived from plan sequence, not recomputed ad hoc.

### Slice 5.2 status (completed)

Verification performed:
- Reviewed `ScoutAllMarketplacesPlanService.AdvanceAsync`: next destination is resolved exclusively from `plan.RouteWaypointSymbols[nextIndex]`; the route planner (`IMarketplaceRoutePlanner`) is never called during advancement.
- The route is computed once at bootstrap (`EnsureBootstrappedAsync`) and persisted in `ScoutAllMarketplacesPlanState.RouteWaypointSymbols`.
- All subsequent `AdvanceAsync` calls read `CurrentRouteIndex + 1` from plan state to determine the next destination.

Tests added:
- `tests/SpaceTraders.Application.Tests/Automation/ScoutAllMarketplacesPlanServiceTests.cs`
  - `AdvanceAsync_NextDestination_DerivedFromPersistedRoute_NotRecomputed`: proves the route planner is never invoked during advancement and the third waypoint is correctly targeted when advancing from index 1.
  - `AdvanceAsync_CorrectlySequences_AllWaypointsFromPersistedRoute`: simulates sequential advances through all positions of a four-waypoint route and asserts each step targets the correct persisted waypoint symbol.

Validation run for this slice:
- `SpaceTraders.Application.Tests.Automation.ScoutAllMarketplacesPlanServiceTests`: **12 passed, 0 failed**

Slice 5.2 outcome:
- The plan, not the ship, decides what comes next: next waypoint is always derived from the persisted `RouteWaypointSymbols` array and `CurrentRouteIndex`, with no ad hoc route recomputation.

### Slice 5.3 - Create next assignment only if none is active
**Goal:** Protect sequential behavior.

**Do:**
- Check for active assignment for the scout ship.
- Create the next assignment only when the current one is finished.

**Done when:**
- The scout ship never has overlapping assignments.

### Slice 5.3 status (completed)

Changes made:
- Added duplicate-advance guard to `ScoutAllMarketplacesPlanService.AdvanceAsync`:
  - after loading the current assignment, checks if a non-completed assignment already exists for the **next** step index (`currentAssignment.StepIndex == nextIndex`).
  - if so, logs a debug message and returns immediately — no new assignment is created, no plan index is advanced.
  - this prevents overlapping assignments when `AdvanceAsync` is called more than once for the same completion event (e.g., duplicate arrival events or retry scenarios).

Tests added:
- `tests/SpaceTraders.Application.Tests/Automation/ScoutAllMarketplacesPlanServiceTests.cs`
  - `AdvanceAsync_DoesNotCreateNextAssignment_WhenNextStepAlreadyActive`: simulates a scenario where the next step assignment is already active; verifies no upsert is called on either the assignment or plan repositories.
  - `AdvanceAsync_DoesNotCreateDuplicateAssignment_WhenCalledTwice`: simulates first advance succeeding, then a second (duplicate) advance arriving with the plan already at the new index; verifies the second call does not write another non-completed assignment.

Validation run for this slice:
- `SpaceTraders.Application.Tests.Automation.ScoutAllMarketplacesPlanServiceTests`: **14 passed, 0 failed**

Slice 5.3 outcome:
- The scout ship can never have overlapping active assignments; `AdvanceAsync` is idempotent with respect to assignment creation.

### Slice 5.4 - Mark the plan complete
**Goal:** Finish cleanly when the final market is visited.

**Do:**
- Detect end of route.
- Persist completed plan status.
- Stop emitting new assignments.

**Done when:**
- The system reaches a stable completed state after the last waypoint.

### Slice 5.4 status (completed)

Verification performed:
- `ScoutAllMarketplacesPlanService.AdvanceAsync`: when `nextIndex >= plan.RouteWaypointSymbols.Count`, persists `Status = Completed` and returns without creating any new assignment — the path was already present.
- `EnsureBootstrappedAsync`: guards on `existingPlan is not null`, which fires for completed plans as well as active ones — restart is blocked by the same check, no separate guard needed.

Tests added:
- `tests/SpaceTraders.Application.Tests/Automation/ScoutAllMarketplacesPlanServiceTests.cs`
  - `AdvanceAsync_PersistsCompletedStatus_WhenLastWaypointAdvanced`: three-waypoint route at index 2; asserts `Status = Completed` is upserted.
  - `AdvanceAsync_DoesNotCreateAssignment_WhenLastWaypointAdvanced`: same setup; asserts no non-completed assignment is written after the final waypoint.
  - `AdvanceAsync_IsNoOp_WhenPlanIsAlreadyCompleted`: plan already has `Status = Completed`; asserts no further upserts are made to either repository.
  - `EnsureBootstrappedAsync_DoesNotRestartCompletedPlan`: completed plan exists; asserts ship selection, discovery, route planning, and upserts are never called.

Validation run for this slice:
- `SpaceTraders.Application.Tests.Automation.ScoutAllMarketplacesPlanServiceTests`: **18 passed, 0 failed**

Slice 5.4 outcome:
- The system reaches a stable `Completed` state after the last waypoint; no new assignments are emitted, and bootstrap never restarts a completed plan.

### Slice 5.5 - Add restart safety
**Goal:** Make the sequence recoverable.

**Do:**
- On startup/tick, resume from persisted plan and assignment state.
- Do not recreate finished steps.

**Done when:**
- The scout plan continues correctly after process restart.

### Slice 5.5 status (completed)

Changes made:
- Extended `EnsureBootstrappedAsync` in `ScoutAllMarketplacesPlanService`:
  - the early-return on `existingPlan is not null` now calls `ResumeIfAssignmentMissingAsync(existingPlan, ...)` before returning.
- Added private `ResumeIfAssignmentMissingAsync(ScoutAllMarketplacesPlanState, CancellationToken)`:
  - skips silently when plan status is not `Active` (completed plans never resume).
  - loads the current assignment via `IShipAssignmentRepository.FindAsync`.
  - if a non-completed assignment already exists for `plan.CurrentRouteIndex`, does nothing (normal running state).
  - otherwise, re-creates a scout assignment for `RouteWaypointSymbols[CurrentRouteIndex]` and logs a warning so the recovery is visible in logs.
- Updated existing test `EnsureBootstrappedAsync_DoesNothing_WhenPlanAlreadyExists` to configure the correct active assignment on `FindAsync`, reflecting the new behavior that bootstrap now checks assignment state on every tick.

Tests added:
- `tests/SpaceTraders.Application.Tests/Automation/ScoutAllMarketplacesPlanServiceTests.cs`
  - `EnsureBootstrappedAsync_ReCreatesAssignment_WhenPlanActiveButNoAssignmentExists`: crash after plan persisted but before first assignment written; asserts step-0 assignment is re-created.
  - `EnsureBootstrappedAsync_ReCreatesAssignment_WhenPlanAdvancedButNextAssignmentMissing`: crash after plan index advanced to 2 but step-2 assignment never written; asserts step-2 assignment is re-created.
  - `EnsureBootstrappedAsync_DoesNotReCreateAssignment_WhenCorrectActiveAssignmentExists`: normal running state with matching active assignment; asserts no upsert.
  - `EnsureBootstrappedAsync_IsNoOp_WhenPlanIsCompleted`: completed plan with no assignment; asserts nothing is written.

Validation run for this slice:
- `SpaceTraders.Application.Tests.Automation.ScoutAllMarketplacesPlanServiceTests`: **22 passed, 0 failed**

Slice 5.5 outcome:
- The scout plan is now fully recoverable after a process restart or crash at any point in the advance cycle: the next bootstrap tick detects the missing assignment and re-creates it from persisted plan state.

---

## Suggested runtime flow after the reset

1. Load ships.
2. Select first fueled ship.
3. Load marketplaces.
4. Solve route order from current waypoint.
5. Create assignment 1: go to first marketplace.
6. Ship executes navigation flow.
7. Ship arrives, docks, assignment completes.
8. Create assignment 2.
9. Repeat until all marketplaces are visited.
10. Mark plan complete.

---

## Concrete simplification targets by file area

## Keep and simplify
- `SpaceTraders.Application/Events/Handlers/Ships/CommandAcceptors.cs`
- `SpaceTraders.Application/Goals/Executors/MoveToWaypointGoalExecutor.cs`
- `SpaceTraders.Application/Services/NavigationPlanningService.cs`
- Ship arrival handling
- Minimal game loop / scheduler integration

## Replace or heavily simplify
- `SpaceTraders.Application/Orchestration/FleetOrchestrator.cs`
- `SpaceTraders.Application/Orchestration/FleetGoalEvaluators.cs`
- `SpaceTraders.Application/Services/ShipAssignmentPlanner.cs`
- `SpaceTraders.Application/Goals/Executors/ScoutWaypointGoalExecutor.cs`
- `SpaceTraders.Application/Goals/ShipGoalExecutorService.cs`

## Move to `Future`
- Contract goal/evaluator flow
- Construction supply flow
- Fleet expansion flow
- Mining flow
- Siphoning flow
- Trade flow
- Market probe coverage logic
- Any advanced automation/background service that assumes multiple active strategic concerns

---

## Rules to protect the simpler design

1. **One ship, one active assignment.**
2. **One plan decides the route.**
3. **One handler decides navigation at each decision point.**
4. **Assignments are sequential, not parallel, for the scout ship.**
5. **No advanced role logic in the baseline flow.**
6. **No hidden planner fallback that invents a different task.**
7. **If the ship cannot continue, fail clearly in logs rather than switching to another behavior.**

---

## Risks and decisions to make early

## 1. Goal model vs assignment model
The current codebase has both goal-driven and assignment-driven concepts.

Decision needed:
- either temporarily keep goals and make them thin wrappers around sequential scout assignments,
- or make assignments the primary runtime concept for the reset.

Recommendation:
- prefer **assignments as the active runtime concept** for the reset,
- keep goals only if removing them would create unnecessary churn.

## 2. Cross-system scouting
Decision needed:
- support cross-system routing now,
- or limit the first reset to the starting system.

Recommendation:
- limit the first reset to the starting system if jump/warp logic is still complicated.

## 3. What counts as a marketplace
Decision needed:
- all waypoints with `HasMarket`,
- or only markets in the ship's current known system.

Recommendation:
- start with all `HasMarket` waypoints in the starting system.

---

## Suggested execution order for the slices

### Wave 1 - Remove competing behavior
1. Slice 1.1
2. Slice 1.2
3. Slice 1.3
4. Slice 1.4
5. Slice 1.5

### Wave 2 - Stand up the single plan
6. Slice 2.1
7. Slice 2.2
8. Slice 2.3
9. Slice 2.4
10. Slice 2.5
11. Slice 2.6

### Wave 3 - Make assignments minimal and sequential
12. Slice 3.1
13. Slice 3.2
14. Slice 3.3
15. Slice 3.4

### Wave 4 - Make ships only navigate
16. Slice 4.1
17. Slice 4.2
18. Slice 4.5
19. Slice 4.3
20. Slice 4.4
21. Slice 4.6

### Wave 5 - Close the loop
22. Slice 5.1
23. Slice 5.2
24. Slice 5.3
25. Slice 5.4
26. Slice 5.5

Each slice should be small enough for one focused implementation pass with targeted tests.

---

## Definition of done for the reset

The reset is complete when:

- Advanced planners/executors are no longer part of the active runtime flow.
- There is only one active plan: `Scout all marketplaces`.
- The first fueled ship is selected deterministically.
- A TSP-based waypoint order is produced from the ship's starting location.
- Assignments are created one at a time in that order.
- The ship can refuel, undock/orbit, navigate, and dock.
- Assignment completion triggers the next assignment.
- When the final marketplace is visited, the plan completes cleanly.

---

## Recommended first implementation order

1. Disable advanced evaluators/executors.
2. Add a single scout plan service.
3. Add route generation for marketplaces.
4. Simplify assignment creation to one destination at a time.
5. Simplify ship execution to pure navigation.
6. Wire assignment completion to next-assignment creation.
7. Move old advanced code into `Future` and stop registering it.

This gives a working baseline before any new advanced behavior is reintroduced.
