# Ship Event Command Plan

## Status

Phase 1 is now implemented in code for event taxonomy, runtime command guards, and initial state-scoped command acceptor wiring.

Target architecture plan. This supersedes assignment state machines and any plan that treats ship automation as a persisted `StepIndex` workflow.

The intended model is:

- ship-state events describe what commands are valid now;
- role-specific handlers get first chance to act;
- generic fallback handlers repair invalid or missing intent;
- persistent ship plans hold role intent, not workflow state;
- commands validate the current ship state before calling SpaceTraders;
- state-scoped command acceptors make invalid commands impossible to issue without explicitly switching interfaces.

## Phase 1 implementation status

Implemented:

- base state events aligned: `ShipDockedEvent`, `ShipInOrbitEvent`, `ShipInTransitEvent`, `ShipStateMismatchEvent`;
- `ShipUndockedEvent` aligned as generic in-orbit chain entry event;
- runtime guard checks added to ship command handlers with fail-fast `ShipStateMismatchEvent` emission on invalid state;
- undocked chain handlers updated to use `IInOrbitCommandAcceptor` instead of direct API calls;
- state-scoped command acceptor interfaces added (`IDockedCommandAcceptor`, `IInOrbitCommandAcceptor`, `IInTransitCommandAcceptor`) with concrete bus-backed implementations;
- fallback undocked handler updated to recovery behavior (dock when in orbit, otherwise emit `ShipStateMismatchEvent`);
- tests added/updated for invalid-state guard behavior and undocked chain ordering/handling.

Remaining follow-up inside this phase:

- expand interface-gated usage across all docked and transit chains (beyond the undocked chain and current command guard surface);
- add explicit compile-time-only safety tests for acceptor boundary enforcement where practical.

## Goals

1. Remove ship assignment state machines.
2. Replace step-by-step workers with state-gated event handlers.
3. Make invalid command paths structurally difficult:
   - only docked handlers may undock, buy, sell, refuel, deliver, or purchase;
   - only in-orbit handlers may dock, navigate, extract, or survey;
   - in-transit handlers may only wait/schedule arrival or trigger legal reroute behavior;
   - handlers can only post commands through the state-scoped command acceptor interface they receive.
4. Store each ship's role and plan data in PostgreSQL so pod restarts resume from intent, not from a state-machine step.
5. Let market price changes dynamically re-plan affected ships.

## Non-goals

- Do not introduce another workflow engine.
- Do not keep `Stateless` state machines for ship automation.
- Do not use `StepIndex` as the source of truth for automation progress.
- Do not add follow-up GET calls after successful POST responses.

## Core Concepts

### State-scoped command acceptors

Handlers should not receive a general-purpose command bus for ship actions. They should receive only a state-scoped acceptor matching the current ship state.

Recommended interfaces:

- `IDockedCommandAcceptor`
  - `OrbitShipCommand`, `BuyCargoCommand`, `SellCargoCommand`, `RefuelShipCommand`, contract delivery/fulfillment commands.
- `IInOrbitCommandAcceptor`
  - `DockShipCommand`, `NavigateShipCommand`, `ExtractResourcesCommand`, survey commands.
- `IInTransitCommandAcceptor`
  - transit-safe operations only (schedule/check arrival, optional legal reroute flow).

Pattern:

1. Event bridge resolves ship state from cache.
2. It routes to the matching chain and provides only the matching acceptor interface.
3. Handler code cannot call invalid commands at compile time, because those methods are not exposed.
4. If state and expected chain disagree, emit `ShipStateMismatchEvent`.

This keeps command legality enforceable by design rather than relying only on runtime checks.

### Base ship-state events

These events represent the ship state that determines which commands are legal.

| Base event | Meaning | Commands allowed from handlers |
|------------|---------|--------------------------------|
| `ShipDockedEvent` | Ship is docked at a waypoint. | Through `IDockedCommandAcceptor`: `OrbitShipCommand`, `BuyCargoCommand`, `SellCargoCommand`, `RefuelShipCommand`, contract delivery/fulfillment commands. |
| `ShipInOrbitEvent` | Ship is in orbit at a waypoint. | Through `IInOrbitCommandAcceptor`: `DockShipCommand`, `NavigateShipCommand`, `ExtractResourcesCommand`, survey commands. |
| `ShipInTransitEvent` | Ship is travelling. | Through `IInTransitCommandAcceptor`: `NavigateShipCommand` to reroute a ship; normally schedule/emit `ShipInOrbitEvent` when due. |
| `ShipStateMismatchEvent` | Emergency: ship is not where/what the current plan expects. | Recovery only: repair plan, dock when possible, or pause ship safely. |

Only `ShipDockedEvent`, `ShipInOrbitEvent`, and `ShipInTransitEvent` are valid normal-operation base state events. `ShipStateMismatchEvent` is an emergency recovery event.

`ShipIdleDockedEvent` is a specific `ShipDockedEvent` used for role selection. A ship that is not docked and has no specific role should emit `ShipStateMismatchEvent` and follow the recovery path.

## Chain of Command

Handlers are ordered from most specific to most generic. The first handler that can make a valid decision handles the event. Fallback handlers should repair the situation, not continue a role-specific plan blindly.

### Undocked / in-orbit chain

For `ShipUndockedEvent`:

1. `ShipMinerUndockedEventHandler`
   - Handles `ShipUndockedEvent` when the ship role is Miner and the ship is in orbit.
   - Receives `IInOrbitCommandAcceptor` (not a generic ship command sender).
   - Navigates to the planned asteroid if not already there.
   - Extracts when already at the mining waypoint.
   - Docks when cargo should be sold and the sell waypoint is reached.
2. `ShipTraderUndockedEventHandler`
   - Handles `ShipUndockedEvent` when the ship role is Trader and the ship is in orbit.
   - Receives `IInOrbitCommandAcceptor`.
   - Uses a `TradeRouteService` to re-evaluate the current plan before navigating.
   - Navigates to the planned buy or sell waypoint.
3. `ShipScoutUndockedEventHandler`
   - Handles `ShipUndockedEvent` when the ship role is Scout and the ship is in orbit.
   - Receives `IInOrbitCommandAcceptor`.
   - Navigates to the planned scouting waypoint.
4. `ShipUndockedEventHandler`
   - Generic fallback.
   - Treats reaching this handler as a broken or missing plan.
   - Sends `DockShipCommand` if the ship is in orbit.
   - Emits `ShipStateMismatchEvent` if the ship state/position is not actionable as expected.
   - Emits `ShipIdleDockedEvent` after docking succeeds.

Only handlers in this in-orbit chain may issue `DockShipCommand` or `NavigateShipCommand`, and only via `IInOrbitCommandAcceptor`.

### Docked chain

For a docked event:

1. `ShipMinerDockedEventHandler`
   - Receives `IDockedCommandAcceptor`.
   - Sells mined cargo if at the planned sell waypoint.
   - Refuels when needed and possible.
   - Emits `ShipUndockedEvent` by undocking when ready for the next mining run.
2. `ShipTraderDockedEventHandler`
   - Receives `IDockedCommandAcceptor`.
   - At buy waypoint: buy planned goods.
   - At sell waypoint: sell planned goods.
   - Re-evaluate the route after buy/sell if prices changed or cargo differs from plan.
   - Undock when the next step requires travel, then emit `ShipUndockedEvent`.
3. `ShipScoutDockedEventHandler`
   - Receives `IDockedCommandAcceptor`.
   - Refresh market/shipyard data for the current waypoint if applicable.
   - Mark the current scout target visited.
   - Pick the next scout target or become idle.
   - Undock when a new target is selected, then emit `ShipUndockedEvent`.
4. `ShipIdleDockedEventHandler`
   - Decision tree for role selection.
   - Emits a ship-becomes-role event.
5. `ShipDockedEventHandler`
   - Generic fallback.
   - If no role/plan is valid, keep the ship idle and emit/log a repair decision.

Only handlers in this docked chain may issue `OrbitShipCommand`, `BuyCargoCommand`, `SellCargoCommand`, `RefuelShipCommand`, and contract hand-in commands, and only via `IDockedCommandAcceptor`.

## Command State Validation

State safety should be enforced in two layers:

1. **Compile-time interface gating** via state-scoped command acceptors (`IDockedCommandAcceptor`, `IInOrbitCommandAcceptor`, `IInTransitCommandAcceptor`).
2. **Runtime guard checks** in command handlers against cached ship state before calling SpaceTraders.

Every command handler must still load cached ship state and fail fast before calling the API when the state is invalid.

| Command | Required state |
|---------|----------------|
| `OrbitShipCommand` | Docked |
| `DockShipCommand` | In orbit |
| `NavigateShipCommand` | In orbit (or transit only in explicit reroute flow) |
| `ExtractResourcesCommand` | In orbit, at extractable waypoint |
| `BuyCargoCommand` | Docked at market waypoint |
| `SellCargoCommand` | Docked at market waypoint |
| `RefuelShipCommand` | Docked at fuel-capable market |
| `DeliverContractCommand` | Docked at contract destination |

Invalid state should produce a recoverable domain event, not a blind API call. Example: attempting to trade while in orbit emits `ShipStateMismatchEvent`.

## Migration Plan

### Phase 1 - Event taxonomy and command guards

- Add or align base state events: docked, in orbit, in transit, and emergency state mismatch.
- Add `ShipUndockedEvent` as the generic in-orbit chain entry event.
- Introduce state-scoped command acceptor interfaces (`IDockedCommandAcceptor`, `IInOrbitCommandAcceptor`, `IInTransitCommandAcceptor`).
- Update handlers to depend on acceptor interfaces instead of a generic command sender.
- Add state validation to ship command handlers.
- Add tests proving invalid state does not call the SpaceTraders API.

### Phase 3 - Docked and in-orbit handler chains

- Implement role-specific docked handlers.
- Implement `ShipUndockedEvent` chain handlers (miner, trader, scout, fallback) with priority ordering.
- Wire each chain to the correct state-scoped command acceptor.
- Implement generic fallback handlers for broken plans.
- Ensure `ShipIdleDockedEvent` is only emitted for docked ships.
- Ensure unexpected ship state/position paths emit `ShipStateMismatchEvent`.

## Testing Strategy

- Unit-test every command guard by state.
- Unit-test handler ordering for `ShipUndockedEvent`: miner/trader/scout handlers skip or handle before fallback.
- Unit-test that docked handlers cannot access in-orbit commands and vice versa (interface-level compile-time safety).
- Unit-test emergency recovery for ships with missing role/plan or mismatched state/position.
- Unit-test role planners with deterministic market/ship/cargo inputs.
- Unit-test market price diffing and affected-ship selection.
- Integration-test a full trade loop using events only: idle docked -> trader plan -> undock -> `ShipUndockedEvent` -> navigate -> in orbit -> dock -> buy/sell -> re-plan.
- Regression-test no follow-up GET after POST.
