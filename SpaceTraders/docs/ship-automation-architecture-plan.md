# Ship Automation Architecture Plan

## Goal

Replace the current chain-of-command ship automation with a clearer per-ship automation model where each ship owns its local state transitions, while a higher-level orchestrator assigns goals and balances fleet capacity, budget, and strategic objectives.

## Principles

1. Each ship has exactly one current local status.
2. Each ship may execute commands only against itself.
3. Ship commands return the ship's new local status and any relevant state changes.
4. Higher-level assignments are set by an orchestrator, not by low-level command handlers.
5. Global business effects are emitted as global events when needed.
6. The orchestrator owns strategic goals, fleet sizing, budget decisions, and market coverage.
7. One decision point should produce at most one ship command.

## Local Ship State

Each ship should maintain a local state that reflects what commands are currently valid.

Suggested local statuses:

- `Docked`
- `InOrbit`
- `InTransit`
- `Extracting`
- `Surveying`
- `Siphoning`
- `CoolingDown`
- `Unavailable`

The initial core model can start with only:

- `Docked`
- `InOrbit`
- `InTransit`

Additional statuses can be introduced when they provide meaningful command restrictions or planning value.

## Ship-Owned Commands

Commands should be scoped to a single ship and should return the new ship state.

Examples:

| Command | Required Status | Resulting Status | Other State Changes |
| --- | --- | --- | --- |
| `Orbit` | `Docked` | `InOrbit` | Updates nav state |
| `Dock` | `InOrbit` | `Docked` | Updates nav state |
| `Navigate` | `InOrbit` | `InTransit` | Updates fuel, route, arrival time |
| `Arrive` | `InTransit` | `InOrbit` | Updates waypoint/system |
| `Extract` | `InOrbit` | `InOrbit` or `CoolingDown` | Updates cargo, cooldown |
| `Survey` | `InOrbit` | `InOrbit` or `CoolingDown` | Adds survey data |
| `Siphon` | `InOrbit` | `InOrbit` or `CoolingDown` | Updates cargo, cooldown |
| `Refuel` | `Docked` | `Docked` | Updates fuel, credits or cargo |
| `BuyCargo` | `Docked` | `Docked` | Updates cargo and credits |
| `SellCargo` | `Docked` | `Docked` | Updates cargo and credits |
| `DeliverContractCargo` | `Docked` | `Docked` | Updates cargo and contract progress |
| `Repair` | `Docked` | `Docked` | Updates condition and credits |
| `Scrap` | `Docked` | `Unavailable` | Removes or retires ship |

Command handlers should be responsible for:

1. Loading the current ship state.
2. Validating that the command is valid for the current local status.
3. Calling the SpaceTraders API when required.
4. Persisting the returned ship state.
5. Returning or publishing the resulting ship state transition.
6. Emitting global events only for global side effects.

## Command Results

Each ship command should return a result that contains the updated local state.

Suggested shape:

```csharp
public sealed record ShipCommandResult
{
    public required string ShipSymbol { get; init; }

    public required ShipLocalStatus Status { get; init; }

    public required string SystemSymbol { get; init; }

    public required string WaypointSymbol { get; init; }

    public DateTimeOffset ArrivesAt { get; init; }

    public int FuelCurrent { get; init; }

    public int FuelCapacity { get; init; }

    public int CargoCurrent { get; init; }

    public int CargoCapacity { get; init; }
}
```

The exact type can evolve, but the important rule is that command execution updates and returns the ship's local state rather than relying on a separate chain of event handlers to infer it.

## Global Events

Most ship commands should primarily update the ship state. Some commands also produce global events because they affect account-wide or world-level state.

Examples:

| Action | Global Event |
| --- | --- |
| Buy cargo | `AgentCreditsChangedEvent`, possibly `MarketInteractionRecordedEvent` |
| Sell cargo | `AgentCreditsChangedEvent`, `ShipCargoSoldEvent` |
| Refuel with credits | `AgentCreditsChangedEvent` |
| Repair | `AgentCreditsChangedEvent` |
| Purchase ship | `NewShipPurchasedEvent`, `AgentCreditsChangedEvent` |
| Deliver contract cargo | `ContractDeliveryRecordedEvent` |
| Fulfill contract | `ContractFulfilledEvent`, `AgentCreditsChangedEvent` |
| Supply construction | `ConstructionSuppliedEvent` |

Local ship events should describe ship facts. Global events should describe changes outside a single ship.

## Ship Assignments

Ships should have higher-level assignments set by the orchestrator.

Examples:

- `Idle`
- `MineGoods`
- `SiphonGoods`
- `TradeRoute`
- `ScoutSystem`
- `FulfillContract`
- `SupplyConstruction`
- `MarketAnchor`
- `FuelRecovery`
- `Maintenance`

Assignments should include enough context for a ship planner to act without needing to understand the entire global strategy.

Example assignment fields:

- Assignment type
- Requested trade good
- Origin waypoint
- Destination waypoint
- Contract id
- Construction site waypoint
- Priority
- Deadline
- Minimum required units
- Maximum budget
- Correlation id

## Ship Planner

A ship planner should convert the current ship state plus assignment into one next command.

Suggested responsibilities:

1. Load the ship's local state.
2. Load the ship's current assignment.
3. Load only the contextual data required for that assignment.
4. Decide the next command.
5. Emit exactly one command, or no command if the ship should wait.

Example decision flow for a mining assignment:

1. If ship is in transit, wait for arrival.
2. If docked and needs refuel, refuel.
3. If docked and has cargo for the destination, orbit.
4. If in orbit at mining waypoint and cargo has space, extract or survey.
5. If in orbit with cargo and not at delivery/sell waypoint, navigate.
6. If in orbit at delivery/sell waypoint, dock.
7. If docked at delivery/sell waypoint, deliver or sell cargo.

The planner should not perform global goal selection. It should only execute the assignment already given to the ship.

## Orchestrator

The orchestrator owns strategic goals and assigns work to ships.

Primary responsibilities:

1. Track active contracts.
2. Track construction goals, such as jump gate completion.
3. Track market coverage.
4. Track fleet capacity and ship roles.
5. Decide whether existing ships can complete goals in time.
6. Decide whether available budget supports purchasing more ships.
7. Assign or reassign ships to work.
8. Keep at least one cheap ship near or at each important market where useful.

The orchestrator should not directly issue low-level commands such as `Dock`, `Orbit`, or `Navigate`. It should assign work. Ship planners should decide the next local command.

## Strategic Goals

### Contract Fulfillment

The orchestrator should evaluate each active contract and estimate:

- Required goods
- Remaining units
- Destination waypoint
- Deadline
- Current inventory already available
- Ships capable of mining, buying, hauling, or delivering
- Expected travel time
- Expected production or purchase rate
- Expected credit impact

If the contract cannot be completed in time with the current fleet, the orchestrator should consider buying or reassigning ships.

### Jump Gate or Construction Completion

The orchestrator should evaluate each construction site and estimate:

- Required materials
- Remaining units
- Known markets for each material
- Known extraction sources for each material
- Hauling capacity
- Travel time
- Budget needed to buy materials
- Whether mining or trading is faster

### Market Coverage

The orchestrator should maintain market coverage by placing a cheap ship at each important market when beneficial.

Possible policy:

1. Identify markets that are strategically useful.
2. Prefer cheap probes, satellites, or low-cost ships when available.
3. Keep one market anchor per key market or cluster.
4. Use anchors to refresh market data and reduce travel uncertainty.
5. Reassign anchors only when the strategic value changes.

### Fleet Expansion

The orchestrator should decide whether to buy more ships based on:

- Current credits
- Reserved budget
- Contract deadlines
- Construction deadlines or strategic priority
- Current ship utilization
- Expected payback period
- Available shipyards
- Available cheap ship types
- Minimum fleet composition targets

Example decision:

1. Estimate whether active goals can be completed with current ships.
2. If not, estimate the bottleneck: mining, hauling, scouting, trading, or market coverage.
3. Find shipyards selling a ship that solves the bottleneck.
4. Check whether credits exceed reserved budget plus purchase cost.
5. Buy the ship if it improves goal completion enough to justify the cost.
6. Assign the new ship immediately.

## Recommended Application Flow

```text
Global state changes or scheduled tick
        ↓
Orchestrator evaluates goals
        ↓
Orchestrator sets ship assignments
        ↓
Ship automation tick or ship state event occurs
        ↓
Ship planner loads one ship + assignment
        ↓
Planner emits one ship command
        ↓
Command handler calls API and persists new ship state
        ↓
Command emits local result and any global events
        ↓
Orchestrator reacts to global events or next tick
```

## Migration Plan

### Phase 1: Introduce Local Ship Status ✅ COMPLETE

- Add a first-class local ship status enum or value object.
  - Renamed `ShipStatus` to `ShipLocalStatus` in `SpaceTraders.Domain/Enums/ShipLocalStatus.cs`.
- Map SpaceTraders API nav statuses into local statuses.
  - Added `ShipLocalStatusMapper` in `SpaceTraders.Domain/Enums/ShipLocalStatusMapper.cs` mapping `"DOCKED"`, `"IN_ORBIT"`, `"IN_TRANSIT"` to enum values.
- Persist local status with ship state.
  - Added `LocalStatus` (int) column to `cached_ships` table.
  - Added `LocalStatus` property to `CachedShip` entity and persisted in `ShipRepository`.
  - Added DB migration SQL in `SpaceTradersDatabaseInitializer`.
- Keep compatibility with existing string statuses during migration.
  - `ShipModel.LocalStatus` is a computed property derived from the existing `Status` string; all existing string-based code remains compatible.
  - `StartupRecoveryService` updated to use `LocalStatus` instead of raw string comparisons.

### Phase 2: Standardize Ship Command Results ✅ COMPLETE

- Add a shared ship command result model.
  - Added `ShipCommandResult` record in `SpaceTraders.Application/Commands/Ships/ShipCommandResult.cs` carrying ship symbol, `ShipLocalStatus`, system/waypoint, arrival time, fuel, cargo, and an `Accepted` flag with a `Rejected` factory.
- Update command handlers such as `DockShipCommand`, `OrbitShipCommand`, and `NavigateShipCommand` to return the updated ship state.
  - `DockShipHandler`, `OrbitShipHandler`, and `NavigateShipHandler` now expose `ExecuteAsync(...)` returning `Task<ShipCommandResult>`. The Wolverine `Handle(...)` entry point delegates to `ExecuteAsync` so the existing message bus contract (returning `Task`) is unchanged.
- Ensure commands validate against local status before calling the API.
  - All three handlers validate against `ShipLocalStatus` (Docked/InOrbit) instead of the raw `Status` string. On mismatch they publish `ShipStateMismatchEvent` and return a rejected `ShipCommandResult` reflecting the current state without calling the port.
- Keep publishing existing events temporarily for compatibility.
  - `ShipDockedEvent`, `ShipUndockedEvent`, `ShipInTransitEvent`, `ShipStateMismatchEvent`, and `RefreshSystemDataCommand` continue to be published exactly as before so existing chain handlers remain functional during the migration.
- Tests: added `ShipCommandResultTests` covering accepted/rejected cases for `DockShipHandler`, `OrbitShipHandler`, and `NavigateShipHandler`. All existing `NavigateShipHandlerTests` still pass.

### Phase 3: Add Ship Planner Boundary ✅ COMPLETE

- Introduce one ship planner entry point.
  - Added `IShipPlanner` and `ShipPlannerContext` in `SpaceTraders.Application/Planning/IShipPlanner.cs` describing the pure planner contract: `CanPlan(ship, assignment)` and `Plan(ship, assignment, context)`.
  - Added `ShipPlannerDecision` and `ShipPlannerCommandKind` in `SpaceTraders.Application/Planning/ShipPlannerDecision.cs`. The kind enum models the single command produced by a planner (`None`, `Dock`, `Orbit`, `Navigate`, `Extract`, `Survey`, `Siphon`, `AssignIdle`, `PatchFlightMode`).
  - Added `IShipPlannerService` / `ShipPlannerService` in `SpaceTraders.Application/Planning/ShipPlannerService.cs` as the boundary entry point. It loads the ship and assignment, selects the first matching planner, builds a `ShipPlannerContext` from `ISurveyRepository` and `INavigationPlanningService`, then dispatches exactly one command via the existing `IInOrbitCommandAcceptor`, `IDockedCommandAcceptor`, or the message bus (for `AssignShipCommand` / `PatchShipNavCommand`).
- Move one role first, preferably mining, from chain handlers into a planner.
  - Added `MiningShipPlanner` in `SpaceTraders.Application/Planning/MiningShipPlanner.cs` mirroring the legacy mining/siphon decision logic from `ShipInOrbitMineEventHandler` (extract/survey/siphon at origin, dock at sell destination, navigate to sell or origin, assign idle when destinations are missing, patch flight mode when DRIFT or recommended-mode changes are needed).
  - The legacy `ShipInOrbitMineEventHandler` is intentionally kept registered so chain-of-command and planner can coexist during the migration. Phase 5 will remove the chain handler once all roles have planners.
- Ensure the planner emits at most one command per decision.
  - `MiningShipPlanner.Plan(...)` returns a single `ShipPlannerDecision`; the planner itself has no side effects.
  - `ShipPlannerService.ExecuteAsync(...)` switches on `ShipPlannerCommandKind` and issues exactly one command per call, satisfying the "one decision -> one command" principle.
- Add tests for each status and assignment combination.
  - Added `tests/SpaceTraders.Application.Tests/Planning/MiningShipPlannerTests.cs` covering: assignment matching, in-transit/docked no-ops, survey vs extract at origin (with and without survey equipment / active surveys), siphon at siphon origin, dock at sell destination, navigate when cargo must travel, assign-idle when no destination is configured, dock at origin with empty cargo, DRIFT patching for fuel-less ships, and recommended-flight-mode patching when planning navigation.
  - Added `tests/SpaceTraders.Application.Tests/Planning/ShipPlannerServiceTests.cs` covering: ship-not-found and missing-assignment short-circuit paths, command routing through `IInOrbitCommandAcceptor`, and that no planner match returns `None` without issuing a command.

DI: `Planning.IShipPlanner` -> `Planning.MiningShipPlanner` and `Planning.IShipPlannerService` -> `Planning.ShipPlannerService` are registered in `SpaceTraders.Application/DependencyInjection.cs`.

### Phase 4: Move Role Logic Out of Chain Handlers ✅ COMPLETE

Migrate role-specific chain handlers into planners. Phase 4a covered the orbit-time roles whose decisions map cleanly onto the existing `ShipPlannerCommandKind` set (`Dock` / `Orbit` / `Navigate` / `AssignIdle` / `PatchFlightMode`). Phase 4b introduced the new command kinds (`SupplyConstruction`, `Refuel`, `BuyCargo`, `Repair`, `Scrap`) needed by the remaining roles and migrated the cross-cutting fuel-recovery and maintenance branches plus the builder/construction role into planners.

Phase 4a — Orbit role planners ✅ COMPLETE

- Mining planner ✅ (Phase 3)
  - `SpaceTraders.Application/Planning/MiningShipPlanner.cs`.
- Trading planner ✅
  - Added `SpaceTraders.Application/Planning/TradingShipPlanner.cs` mirroring the destination resolution and dock/navigate/idle decisions of `ShipInOrbitTraderEventHandler`. Cargo-aware destination selection (sell when carrying cargo, otherwise return to buy origin), DRIFT patching for fuel-less ships, and recommended-flight-mode patching reuse the same `ShipPlannerContext` shape as the mining planner.
  - Tests: `tests/SpaceTraders.Application.Tests/Planning/TradingShipPlannerTests.cs` covering assignment matching, transit/docked no-ops, dock-at-buy, dock-at-sell, navigate-to-sell, navigate-to-buy, assign-idle when no waypoints are configured, DRIFT patch for zero fuel-capacity ships, and recommended-mode patch.
- Contract planner ✅
  - Added `SpaceTraders.Application/Planning/ContractShipPlanner.cs` mirroring the cargo-aware delivery/origin decision of `ShipInOrbitContractEventHandler`. The planner inspects `ShipModel.CargoInventory` for the contract good and routes to the delivery waypoint when carrying it, otherwise back to origin to load.
  - Tests: `tests/SpaceTraders.Application.Tests/Planning/ContractShipPlannerTests.cs` covering assignment matching, transit no-op, dock at load, navigate to delivery when carrying cargo, dock at delivery, assign-idle without waypoints, and DRIFT patch.
- Scouting planner ✅
  - Added `SpaceTraders.Application/Planning/ScoutingShipPlanner.cs` for the `Scout` and `MarketProbe` assignment types. Side effects in the legacy handler (waypoint visit marking, market refresh, shipyard refresh, chart creation) intentionally remain in `ShipInOrbitScoutEventHandler` so the planner stays pure; the planner only chooses the next dock / navigate / idle / DRIFT-patch command based on `assignment.OriginWaypoint` as the scout target.
  - Tests: `tests/SpaceTraders.Application.Tests/Planning/ScoutingShipPlannerTests.cs` covering both assignment types, transit no-op, dock-at-target, navigate-when-away, assign-idle without target, and DRIFT patch.

DI: `TradingShipPlanner`, `ContractShipPlanner`, and `ScoutingShipPlanner` are registered alongside `MiningShipPlanner` as `IShipPlanner` in `SpaceTraders.Application/DependencyInjection.cs`. The legacy chain handlers (`ShipInOrbitTraderEventHandler`, `ShipInOrbitContractEventHandler`, `ShipInOrbitScoutEventHandler`) remain registered so the chain-of-command flow still drives runtime behavior; they will be retired in Phase 5 once `ShipPlannerService` is the active automation entry point.

Phase 4b — Cross-cutting + builder planners ✅ COMPLETE

- Decision/context model expanded
  - `SpaceTraders.Application/Planning/ShipPlannerDecision.cs` adds `ShipPlannerCommandKind` members `SupplyConstruction`, `Refuel`, `BuyCargo`, `Repair`, and `Scrap`, plus payload properties (`TradeSymbol`, `Units`, `SystemSymbol`, `WaypointSymbol`) and matching factory methods.
  - `SpaceTraders.Application/Planning/IShipPlanner.cs` extends `ShipPlannerContext` with `FuelMarketWaypoint`, `CurrentWaypointSellsFuel`, `Maintenance` (`FleetMaintenanceDecision`), and `ConstructionComplete`.
- Planner-service refactor
  - `SpaceTraders.Application/Planning/ShipPlannerService.cs` now resolves fuel-market data from `IWaypointRepository`/`IMarketRepository`, queries `IFleetMaintenancePlanner` for docked ships, checks construction completion via `IConstructionRepository`, and evaluates all matching planners in registration order — using the first non-`None` decision so cross-cutting planners can preempt role planners. `ExecuteAsync` translates the new decision kinds into `IInOrbitCommandAcceptor.SupplyConstructionAsync`, `IDockedCommandAcceptor.RefuelAsync` / `BuyCargoAsync` / `RepairAsync` / `ScrapAsync` calls.
- Fuel recovery planner ✅
  - Added `SpaceTraders.Application/Planning/FuelRecoveryShipPlanner.cs` mirroring `ShipInOrbitFuelRecoveryEventHandler`. Patches DRIFT for fuel-less or critically low ships, navigates to the nearest known fuel market, docks at the fuel market, and refuels when docked at a fuel-selling waypoint. Returns `None` when fuel is acceptable so the role planner runs.
  - Tests: `tests/SpaceTraders.Application.Tests/Planning/FuelRecoveryShipPlannerTests.cs`.
- Maintenance planner ✅
  - Added `SpaceTraders.Application/Planning/MaintenanceShipPlanner.cs` wrapping `IFleetMaintenancePlanner`. Issues `Scrap` (with precedence) or `Repair` for docked ships when the maintenance decision flags it; returns `None` otherwise.
  - Tests: `tests/SpaceTraders.Application.Tests/Planning/MaintenanceShipPlannerTests.cs`.
- Builder/construction planner ✅
  - Added `SpaceTraders.Application/Planning/BuilderShipPlanner.cs` mirroring `ShipInOrbitBuilderEventHandler`/`ShipDockedBuilderEventHandler`. Handles the full construction loop: idle when site is complete or assignment is missing metadata, docked buy/orbit cycle, in-orbit `SupplyConstruction` at the site, and navigation between origin and construction site based on cargo state.
  - Tests: `tests/SpaceTraders.Application.Tests/Planning/BuilderShipPlannerTests.cs`.

DI: `FuelRecoveryShipPlanner` and `MaintenanceShipPlanner` are registered before the role planners (`MiningShipPlanner`, `TradingShipPlanner`, `ContractShipPlanner`, `ScoutingShipPlanner`, `BuilderShipPlanner`) in `SpaceTraders.Application/DependencyInjection.cs` so they can preempt role decisions while remaining composable. The legacy chain handlers stay registered until Phase 5 retires the chain-of-command flow.

Test-host stabilization (Phase 4 prerequisite) ✅
- `SpaceTraders.API/Program.cs` now skips Postgres-backed startup work when `ASPNETCORE_ENVIRONMENT=Testing`: the database initializer call, `UseResourceSetupOnStartup()`, and the Wolverine Postgres persistence configuration are all gated. This unblocks `WebApplicationFactory<Program>` integration tests and the strict DI validation host so the full solution test suite is green.

### Phase 5: Replace Chain Dispatch With Explicit Automation Events

- Remove inheritance-based routing such as arrived events being dispatched as in-orbit events.
- Keep events factual and explicit.
- Route state changes to a single ship automation handler.
- Remove chain priorities from business behavior.

Phase 5a — Single ship automation handler + explicit automation event ✅ COMPLETE

- Added `ShipAutomationTickEvent` in `SpaceTraders.Domain/Events/Ships/ShipAutomationTickEvent.cs`. The event is intentionally **not** derived from `ChainOfCommandEvent`: it is the explicit, factual automation entry point that replaces inheritance-based routing (e.g. `ShipArrivedEvent` being upcast to `ShipInOrbitEvent`). It carries only ship symbol, an optional reason, and standard correlation/causation/occurred-at metadata; the authoritative ship state is loaded by the handler.
- Added `ShipAutomationTickEventHandler` in `SpaceTraders.Application/EventHandlers/ShipAutomationTickEventHandler.cs`. This is the **single ship automation handler** for the new flow: it delegates to `IShipPlannerService.PlanAndExecuteAsync(...)` so exactly one command (or none) is issued per decision point. No chain priorities are involved; planner ordering inside `ShipPlannerService` is the only precedence (cross-cutting planners preempt role planners as established in Phase 4b).
- Tests: `tests/SpaceTraders.Application.Tests/EventHandlers/ShipAutomationTickEventHandlerTests.cs` covers (1) delegation to `IShipPlannerService` and (2) a structural assertion that `ShipAutomationTickEvent` is **not** a subclass of `ChainOfCommandEvent`, locking in the Phase 5 "no inheritance-based routing" rule for this event.

The legacy `ChainOfCommandBridgeHandler`, `ChainOfCommandDispatcher`, and chain handler registrations remain in place so existing flows (e.g. `ShipArrivedEvent` upcast to `ShipInOrbitEvent`, `ShipUndockedEvent`/`ShipIdleDockedEvent`/`ShipRefueledEvent`/`ShipAssignmentTypeSetEvent` upcast to their bases) keep working untouched. Phase 5b will start migrating publishers (`GameLoopService`, `DockShipCommand`, `OrbitShipCommand`, `ShipInTransitEventHandler`, `StartupRecoveryService`) to publish `ShipAutomationTickEvent` instead of relying on chain-routed base events. Phase 7 will then remove the chain infrastructure entirely.

Phase 5b — Migrate publishers to the explicit automation event ✅ COMPLETE

- `SpaceTraders.Application/Automation/GameLoopService.cs` now publishes a `ShipAutomationTickEvent` (reason `"Arrived"`) immediately after the dead-reckoning `ShipArrivedEvent`, so arrivals trigger the planner without depending on the chain-of-command upcast `ShipArrivedEvent → ShipInOrbitEvent`.
- `SpaceTraders.Application/Commands/Ships/DockShipCommand.cs` and `OrbitShipCommand.cs` publish a `ShipAutomationTickEvent` (reasons `"Docked"` / `"Undocked"`) on success, alongside the legacy `ShipDockedEvent` / `ShipUndockedEvent` so the planner runs after every successful state transition while existing chain handlers stay functional.
- `SpaceTraders.Application/Events/Handlers/Ships/ShipInTransitEventHandler.cs` now also drives the explicit automation flow: it publishes the tick immediately when arrival is due, and calls `bus.ScheduleAsync(tick, arrivalTime)` when arrival is in the future. The legacy `ChainOfCommandHandlerResult.Handled(ShipArrivedEvent)` / `Scheduled(...)` return values are preserved so the chain dispatcher path continues to work during migration.
- `SpaceTraders.API/Services/StartupRecoveryService.cs` emits an automation tick for every recovered ship: docked, in-orbit, arrival-elapsed, and a scheduled tick at `ship.ArrivesAt` for ships still in transit. The legacy recovery events (`ShipInTransitEvent`, `ShipDockedEvent`, `ShipInOrbitEvent`) keep being published so chain handlers remain unaffected.
- Tests:
  - `tests/SpaceTraders.Application.Tests/Commands/ShipCommandResultTests.cs` adds `DockShip_WhenAccepted_PublishesShipAutomationTickEvent` and `OrbitShip_WhenAccepted_PublishesShipAutomationTickEvent` covering the new tick publication on accepted dock/orbit commands.
  - `tests/SpaceTraders.Application.Tests/Events/Handlers/Ships/ShipInTransitEventHandlerTests.cs` constructs the handler with an `IMessageBus` and asserts: (1) immediate `ShipAutomationTickEvent` publish when arrival is due, and (2) scheduled publish (via `DeliveryOptions.ScheduledTime`) when arrival is in the future.

The legacy chain handlers (`ShipArrivedEventHandler`, `ShipInOrbitEventHandler`, `ShipDockedEventHandler`, `ShipUndockedEventHandler`, etc.) and the `ChainOfCommandBridgeHandler` remain registered, so the chain-of-command flow continues to drive runtime behavior while planner-driven behavior is validated. Phase 5c will retire chain priorities from business behavior, and Phase 7 will remove the chain infrastructure entirely.

Phase 5c — Retire chain priorities from business behavior ⏳ PENDING

### Phase 6: Build Orchestrator Goal Evaluation

- Add goal models for contracts, construction, market coverage, and fleet expansion.
- Add capacity estimation for mining, hauling, trading, and scouting.
- Add budget policy with reserved credits.
- Add assignment generation from goals.

### Phase 7: Remove Chain-of-Command Infrastructure

Once all roles have migrated:

- Remove `IChainOfCommandEventHandler`.
- Remove `ChainOfCommandDispatcher`.
- Remove `ChainOfCommandBridgeHandler`.
- Remove chain handler result types.
- Remove derived events that exist only for routing convenience.

## Testing Strategy

### Unit Tests

Add planner tests for each assignment and local status:

- Docked mining ship with empty cargo should orbit.
- In-orbit mining ship at source with cargo space should extract or survey.
- In-orbit mining ship with cargo should navigate to delivery/sell waypoint.
- In-orbit ship at destination with cargo should dock.
- Docked ship at destination should sell or deliver.
- In-transit ship should not receive another navigation command.

### Command Handler Tests

Add command tests for state transitions:

- `Orbit` requires `Docked` and returns `InOrbit`.
- `Dock` requires `InOrbit` and returns `Docked`.
- `Navigate` requires `InOrbit`, updates fuel, and returns `InTransit`.
- Invalid status does not call the API.

### Orchestrator Tests

Add orchestrator tests for strategic behavior:

- Assigns miners for contract goods.
- Assigns haulers to deliver completed cargo.
- Buys ships only when budget allows.
- Does not spend reserved credits.
- Maintains cheap market anchor ships at important markets.
- Reassigns idle ships before buying new ships.

## Acceptance Criteria

- Each ship has one persisted local status.
- Each ship command validates against the ship's current local status.
- Each ship command returns the updated local state.
- One ship automation decision emits at most one command.
- Orchestrator assigns goals but does not issue low-level ship commands.
- Global events are emitted only for global effects.
- Existing high-level goals are represented: contracts, construction, market coverage, and fleet expansion.
- Chain-of-command priorities are no longer required for ship behavior.
