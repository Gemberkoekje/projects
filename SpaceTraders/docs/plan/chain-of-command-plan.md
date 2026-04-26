# Chain of Command Event Plan

## Status

### Phase 1: Event Foundation (Implemented)

Implemented in this workspace:

- Base chain event metadata type:
  - `SpaceTraders.Domain/Events/ChainOfCommandEvent.cs`
  - Includes `EventId`, `OccurredAt`, `CorrelationId`, `CausationId`.
- Initial ship chain event records:
  - `ShipUndockedEvent`
  - `ShipMovingEvent`
  - `ShipArrivedEvent`
  - `ShipIdleEvent`
  - `ShipRoleSetEvent`
- Chain-of-command dispatcher abstraction:
  - `IChainOfCommandDispatcher`
  - `ChainOfCommandDispatchResult`
- Handler contract and result abstraction:
  - `IChainOfCommandEventHandler<TEvent>` with `Priority`
  - `ChainOfCommandHandlerResult` (`Skipped`, `Handled`, `Scheduled`, `Failed`)
- Priority-ordered chain dispatching implementation:
  - `ChainOfCommandDispatcher`
  - Executes handlers in ascending priority and stops on first non-skipped result.
  - Publishes immediate next events and schedules delayed events.
- DI registration:
  - `IChainOfCommandDispatcher` mapped to `ChainOfCommandDispatcher` in `SpaceTraders.Application/DependencyInjection.cs`.

Validation added:

- Domain tests for event metadata/correlation behavior.
- Application tests for dispatcher ordering, scheduled events, and missing-handler failure.

### Phase 2: Ship Moving Chain (Implemented)

Implemented in this workspace:

- Ship-moving chain handler:
  - `SpaceTraders.Application/Events/Handlers/Ships/ShipMovingEventHandler.cs`
  - Handles `ShipMovingEvent` with priority 100.
  - Creates `ShipArrivedEvent` with propagated correlation/causation metadata.
  - Schedules arrival for future timestamps.
  - Publishes arrival immediately when the due time is already reached.
- Handler registrations in application DI:
  - `IChainOfCommandEventHandler<ShipMovingEvent>` → `ShipMovingEventHandler`

Validation added:

- Dispatcher-backed tests for ship-moving chain behavior:
  - `tests/SpaceTraders.Application.Tests/Events/Handlers/Ships/ShipMovingEventHandlerTests.cs`
  - Verifies future arrivals are scheduled and due arrivals are published immediately.

### Phase 3: Ship Undocked Chain (Implemented)

Implemented in this workspace:

- Role-specific undocked handlers:
  - `ShipUndockedScoutEventHandler` (priority 100)
    - Resolves scout assignment for the ship.
    - Navigates to scouting target waypoint.
    - Emits `ShipMovingEvent` on successful navigation.
    - Emits `ShipIdleEvent` when assignment target is missing or already reached.
  - `ShipUndockedMineEventHandler` (priority 200)
    - Resolves mine assignment for the ship.
    - Navigates to mining target waypoint.
    - Emits `ShipMovingEvent` on successful navigation.
    - Emits `ShipIdleEvent` when assignment target is missing or already reached.
- Fallback undocked handler:
  - `ShipUndockedEventHandler` (priority 1000)
    - Plans assignment when no role-specific handler matches.
    - Persists assignment intent.
    - Emits `ShipRoleSetEvent` when a role was determined.
    - Emits `ShipIdleEvent` when no role can be assigned.
- Handler registrations in application DI:
  - `IChainOfCommandEventHandler<ShipUndockedEvent>` → `ShipUndockedScoutEventHandler`
  - `IChainOfCommandEventHandler<ShipUndockedEvent>` → `ShipUndockedMineEventHandler`
  - `IChainOfCommandEventHandler<ShipUndockedEvent>` → `ShipUndockedEventHandler`

Validation added:

- Dispatcher-backed tests for undocked chain behavior:
  - `tests/SpaceTraders.Application.Tests/Events/Handlers/Ships/ShipUndockedEventHandlerTests.cs`
  - Verifies scout-first handling, miner fallback after scout skip, and generic fallback role assignment.

## Goal

Introduce an event-driven chain of command where every SpaceTraders `POST` action produces a domain event, every event has one or more handlers, and every event chain continues by producing either:

- a new immediate event, or
- a scheduled future event.

The system should allow role-specific handlers to react first, then fall back to generic handlers when no specialized behavior applies.

## Core Rules

1. Every successful `POST` to SpaceTraders must publish a corresponding event.
   - Example: `POST /v1/undock` -> `ShipUndockedEvent`.
2. Every event must have at least one chain-of-command handler.
3. Every event handler must either:
   - emit another event immediately, or
   - schedule a future event.
4. Role-specific handlers should run before generic fallback handlers.
5. Fallback handlers should assign missing intent, role, or next action.
6. Events should be idempotent where possible so retries are safe.
7. External API state should be refreshed through cache-aware services rather than forced updates unless explicitly required.

## Suggested Implementation Phases

### Phase 1: Event Foundation (Implemented)

- Add base event metadata.
- Add ship event records.
- Add event dispatcher abstraction.
- Add handler result abstraction.
- Add chain-of-command handler ordering.

### Phase 2: Ship Moving Chain (Implemented)

- Implement `ShipMovingEventHandler`.
- Add tests for scheduled `ShipArrivedEvent` creation and immediate publish when due.

### Phase 3: Ship Undocked Chain (Implemented)

- Add scout, miner, and fallback undocked handlers.
- Add role lookup and assignment services.
- Add tests for handler ordering and fallback behavior.

### Phase 4: POST Event Publishing

- Wrap SpaceTraders mutation calls so successful POST responses publish events.
- Start with undock and navigate.
- Add tests proving each POST maps to the expected event.

### Phase 5: Ship Arrived Chain

- Add scout arrival handler.
- Add cache-aware waypoint, marketplace, and shipyard refresh services.
- Add mining arrival behavior.
- Add fallback arrival handler.

### Phase 6: Ship Refresh Worker

- Add 10-minute hosted worker.
- Iterate all owned ships.
- Request cache-aware marketplace and shipyard refreshes.
- Add tests for stale-cache and fresh-cache behavior.

### Phase 7: Complete Event Coverage

- Add event mappings for remaining POST endpoints.
- Add role-specific handlers as new ship roles are introduced.
- Ensure every event chain ends in a new immediate event or scheduled future event.
