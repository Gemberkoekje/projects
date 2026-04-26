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
- Seed handlers for undocked chain ordering:
  - `ShipUndockedScoutEventHandler` (priority 100, currently skips)
  - `ShipUndockedEventHandler` (priority 1000 fallback to `ShipIdleEvent`)

Validation added:

- Domain tests for event metadata/correlation behavior.
- Application tests for dispatcher ordering, scheduled events, and missing-handler failure.

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

## Proposed Project Areas

### Domain

Add domain events and supporting value objects in `SpaceTraders.Domain`.

Suggested areas:

- `Events/Ships/`
- `Events/Markets/`
- `Events/Shipyards/`
- `Ships/`
- `Waypoints/`

### Application

Add chain-of-command handlers and orchestration services in `SpaceTraders.Application`.

Suggested areas:

- `Events/Handlers/Ships/`
- `Events/Dispatching/`
- `Scheduling/`
- `Services/Ships/`
- `Services/Markets/`
- `Services/Shipyards/`

### Infrastructure

Add event persistence, delayed scheduling, background workers, and API integrations in infrastructure projects.

Suggested areas:

- `SpaceTraders.Infrastructure.Persistence`
- `SpaceTraders.Infrastructure.SpaceTradersAPI`
- hosted workers in the app/API host project

## Event Naming

Use past-tense event names for completed facts:

- `ShipUndockedEvent`
- `ShipMovedEvent`
- `ShipArrivedEvent`
- `ShipIdleEvent`
- `ShipRoleSetEvent`
- `MarketplaceUpdatedEvent`
- `ShipyardUpdatedEvent`

Use command names only for requested actions, not completed facts:

- `UndockShipCommand`
- `NavigateShipCommand`
- `SetShipRoleCommand`
- `RefreshMarketplaceCommand`
- `RefreshShipyardCommand`

## POST to Event Mapping

Every successful SpaceTraders API mutation should publish a matching event.

| POST action | Event | Notes |
| --- | --- | --- |
| Undock ship | `ShipUndockedEvent` | Starts role-specific next-action chain. |
| Dock ship | `ShipDockedEvent` | Can trigger market, shipyard, or cargo decisions. |
| Navigate ship | `ShipMovingEvent` | Schedules `ShipArrivedEvent` for arrival time. |
| Orbit ship | `ShipOrbitedEvent` | Can trigger navigation, survey, or extraction decisions. |
| Extract resources | `ResourcesExtractedEvent` | Can trigger cargo checks or another extraction. |
| Survey waypoint | `WaypointSurveyedEvent` | Can trigger mining decisions. |
| Sell cargo | `CargoSoldEvent` | Can trigger more sales, refuel, or idle. |
| Buy cargo | `CargoPurchasedEvent` | Can trigger transport or trade route movement. |
| Refuel ship | `ShipRefueledEvent` | Can continue pending movement or assignment. |
| Purchase ship | `ShipPurchasedEvent` | Should assign an initial role or emit `ShipIdleEvent`. |
| Accept contract | `ContractAcceptedEvent` | Can trigger role assignment or fulfillment planning. |
| Deliver contract cargo | `ContractCargoDeliveredEvent` | Can trigger more deliveries or fulfillment. |
| Fulfill contract | `ContractFulfilledEvent` | Can trigger idle/reassignment events. |
| Set ship role internally | `ShipRoleSetEvent` | Not from SpaceTraders POST, but part of chain fallback. |

## Chain of Command Dispatching

Handlers should be evaluated in priority order. A handler can either handle the event or pass it to the next handler.

Recommended handler result shape:

- `Handled`: the handler produced the next event or scheduled future event.
- `Skipped`: the handler does not apply and the dispatcher should continue.
- `Failed`: the handler could not complete and should publish/log a failure path.

Recommended handler behavior:

1. Check whether the event applies.
2. Load only the state required for the decision.
3. Emit the next event, issue the next command, or schedule a future event.
4. Return a clear result to stop or continue the chain.

## Ship Undocked Chain

### Event

`ShipUndockedEvent`

Suggested data:

- `ShipSymbol`
- `SystemSymbol`
- `WaypointSymbol`
- `OccurredAt`
- `CorrelationId`
- `CausationId`

### Handler Order

1. `ShipUndockedScoutEventHandler`
2. `ShipUndockedMineEventHandler`
3. `ShipUndockedTradeEventHandler`
4. `ShipUndockedContractEventHandler`
5. `ShipUndockedEventHandler`

### `ShipUndockedScoutEventHandler`

Purpose:

- Check whether the undocked ship is a scout.
- If it is a scout, send it to the waypoint it should scout.

Behavior:

1. Load ship role.
2. If role is not `Scout`, return `Skipped`.
3. Resolve the next scouting waypoint.
4. If the ship is already at the target waypoint, emit `ShipArrivedEvent` or `ShipIdleEvent` depending on current state.
5. Otherwise issue navigation.
6. On successful navigation POST, publish `ShipMovingEvent`.

Next event:

- `ShipMovingEvent`, or
- `ShipIdleEvent` if no scouting target exists.

### `ShipUndockedMineEventHandler`

Purpose:

- Check whether the undocked ship is a miner.
- If it is a miner, send it to the waypoint it should mine.

Behavior:

1. Load ship role.
2. If role is not `Miner`, return `Skipped`.
3. Resolve the best mining waypoint.
4. If already at the mining waypoint, emit `ShipReadyToMineEvent`.
5. Otherwise issue navigation.
6. On successful navigation POST, publish `ShipMovingEvent`.

Next event:

- `ShipMovingEvent`, or
- `ShipReadyToMineEvent`, or
- `ShipIdleEvent` if no mining target exists.

### Other Role-Based Undocked Handlers

Add specialized handlers as ship roles become available:

- `ShipUndockedTradeEventHandler`
- `ShipUndockedContractEventHandler`
- `ShipUndockedSurveyEventHandler`
- `ShipUndockedTransportEventHandler`
- `ShipUndockedRefuelEventHandler`

Each handler should follow the same pattern:

1. Check role or assignment.
2. Skip if it does not apply.
3. Determine next command.
4. Publish the event caused by the successful command.

### `ShipUndockedEventHandler`

Purpose:

- Fallback handler for idle ships or ships without a role.
- Assign a role or emit a safe idle event.

Behavior:

1. Check whether earlier role handlers handled the event.
2. If the ship has no role, determine the best role based on current fleet needs.
3. Persist the role assignment.
4. Emit `ShipRoleSetEvent`.
5. If no role can be assigned, emit `ShipIdleEvent`.

Next event:

- `ShipRoleSetEvent`, or
- `ShipIdleEvent`.

## Ship Moving Chain

### Event

`ShipMovingEvent`

Suggested data:

- `ShipSymbol`
- `OriginWaypointSymbol`
- `DestinationWaypointSymbol`
- `DepartureTime`
- `ArrivalTime`
- `FuelConsumed`
- `CorrelationId`
- `CausationId`

### `ShipMovingEventHandler`

Purpose:

- Schedule a future arrival event when a ship starts moving.

Behavior:

1. Read the arrival timestamp from the SpaceTraders navigation response.
2. Persist the current movement state.
3. Schedule `ShipArrivedEvent` for the arrival timestamp.
4. Do not emit `ShipArrivedEvent` immediately unless the arrival time is already due.

Next event:

- scheduled `ShipArrivedEvent`.

## Ship Arrived Chain

### Event

`ShipArrivedEvent`

Suggested data:

- `ShipSymbol`
- `SystemSymbol`
- `WaypointSymbol`
- `ArrivedAt`
- `CorrelationId`
- `CausationId`

### Handler Order

1. `ShipArrivedScoutEventHandler`
2. `ShipArrivedMineEventHandler`
3. `ShipArrivedTradeEventHandler`
4. `ShipArrivedContractEventHandler`
5. `ShipArrivedEventHandler`

### `ShipArrivedScoutEventHandler`

Purpose:

- Ensure marketplace and shipyard data is updated when a scout arrives.
- Return the ship to idle when scouting work for the waypoint is complete.

Behavior:

1. Load ship role.
2. If role is not `Scout`, return `Skipped`.
3. Refresh the waypoint details.
4. If a marketplace exists, request a cache-aware marketplace refresh.
5. If a shipyard exists, request a cache-aware shipyard refresh.
6. Mark the waypoint as scouted if applicable.
7. Emit `ShipIdleEvent`.

Next event:

- `MarketplaceUpdatedEvent` when refreshed,
- `ShipyardUpdatedEvent` when refreshed,
- then `ShipIdleEvent`.

If marketplace or shipyard cache is still fresh, the handler may skip the refresh event and emit `ShipIdleEvent`.

### `ShipArrivedMineEventHandler`

Purpose:

- Start mining when a miner arrives at its mining waypoint.

Behavior:

1. Load ship role.
2. If role is not `Miner`, return `Skipped`.
3. Check cargo capacity.
4. If cargo is full, emit `ShipCargoFullEvent`.
5. If cargo has capacity, issue extraction.
6. On successful extraction POST, emit `ResourcesExtractedEvent`.

Next event:

- `ResourcesExtractedEvent`, or
- `ShipCargoFullEvent`.

### `ShipArrivedEventHandler`

Purpose:

- Generic fallback when no role-specific arrival handler applies.

Behavior:

1. Persist that the ship is no longer in transit.
2. If the ship has a role but no matching handler applies, emit `ShipIdleEvent`.
3. If the ship has no role, emit `ShipRoleAssignmentRequestedEvent`.

Next event:

- `ShipIdleEvent`, or
- `ShipRoleAssignmentRequestedEvent`.

## Ship Idle Chain

### Event

`ShipIdleEvent`

Purpose:

- Represent that a ship has no active immediate task.

Handler order:

1. `ShipIdleScoutEventHandler`
2. `ShipIdleMineEventHandler`
3. `ShipIdleTradeEventHandler`
4. `ShipIdleContractEventHandler`
5. `ShipIdleEventHandler`

Fallback behavior:

1. Evaluate fleet priorities.
2. Assign or reassign a role if needed.
3. Emit `ShipRoleSetEvent` if a role changed.
4. Otherwise schedule a future `ShipIdleEvent` to re-check later.

Next event:

- `ShipRoleSetEvent`, or
- scheduled `ShipIdleEvent`.

## Future Event Scheduling

Use a durable scheduler so arrival events survive process restarts.

Preferred options:

1. Wolverine delayed messages if already used or planned.
2. A persisted scheduled-events table plus a background worker.

Required scheduled-event fields:

- `Id`
- `EventType`
- `Payload`
- `DueAt`
- `Status`
- `Attempts`
- `CreatedAt`
- `ProcessedAt`
- `CorrelationId`
- `CausationId`

Processing rules:

1. A worker picks due scheduled events.
2. The worker marks events as in-progress before dispatch.
3. Successful dispatch marks the scheduled event as processed.
4. Failed dispatch increments attempts and reschedules with backoff.
5. Poison events should be marked failed after the retry limit.

## Background Ship Refresh Worker

Add a background worker that runs every 10 minutes and inspects every owned ship.

Purpose:

- Keep waypoint economy data reasonably warm without forcing API calls.
- Allow cache-aware marketplace and shipyard refreshes to decide whether work is needed.

Behavior:

1. Every 10 minutes, load all owned ships.
2. For each ship, identify its current waypoint.
3. Check whether the waypoint has a marketplace.
4. If it has a marketplace, request a cache-aware marketplace refresh.
5. Check whether the waypoint has a shipyard.
6. If it has a shipyard, request a cache-aware shipyard refresh.
7. If cache is fresh, do nothing.
8. If cache is stale and refresh succeeds, publish the matching update event.

Possible events:

- `MarketplaceRefreshRequestedEvent`
- `MarketplaceUpdatedEvent`
- `ShipyardRefreshRequestedEvent`
- `ShipyardUpdatedEvent`

The worker does not need to force updates. Stale-cache checks should decide whether API calls are made.

## Cache-Aware Refresh Services

Add services that centralize stale-cache behavior:

- `IMarketplaceRefreshService`
- `IShipyardRefreshService`
- `IWaypointRefreshService`

Recommended behavior:

1. Check local cache timestamp.
2. If fresh, return without API call.
3. If stale, call the SpaceTraders API.
4. Persist new data.
5. Emit updated event.

This keeps event handlers simple and prevents duplicated stale-cache logic.

## Idempotency and Deduplication

Every handler should protect against duplicate event delivery.

Recommended safeguards:

- store processed event IDs,
- use correlation and causation IDs,
- check current ship state before issuing POST commands,
- avoid issuing navigation if the ship is already moving to the same destination,
- avoid refreshing marketplace or shipyard data when cache is fresh.

## Observability

Log each chain decision with:

- event type,
- ship symbol,
- handler name,
- result: handled, skipped, failed,
- next event or scheduled event,
- correlation ID.

Do not log full agent tokens or other secrets.

## Suggested Implementation Phases

### Phase 1: Event Foundation

- Add base event metadata.
- Add ship event records.
- Add event dispatcher abstraction.
- Add handler result abstraction.
- Add chain-of-command handler ordering.

### Phase 2: POST Event Publishing

- Wrap SpaceTraders mutation calls so successful POST responses publish events.
- Start with undock and navigate.
- Add tests proving each POST maps to the expected event.

### Phase 3: Ship Undocked Chain

- Add scout, miner, and fallback undocked handlers.
- Add role lookup and assignment services.
- Add tests for handler ordering and fallback behavior.

### Phase 4: Future Events

- Add durable scheduled event storage or Wolverine delayed messages.
- Implement `ShipMovingEventHandler`.
- Add tests for scheduled `ShipArrivedEvent` creation.

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

## Acceptance Criteria

- Every successful SpaceTraders `POST` publishes a corresponding event.
- Every event has at least one handler.
- Every event handler either emits a new event or schedules a future event.
- `ShipUndockedScoutEventHandler` sends scouts to scouting waypoints.
- `ShipUndockedMineEventHandler` sends miners to mining waypoints.
- `ShipUndockedEventHandler` assigns a role or emits an idle event as fallback.
- `ShipMovingEventHandler` schedules `ShipArrivedEvent` for the API-provided arrival time.
- `ShipArrivedScoutEventHandler` refreshes marketplace and shipyard data when present and then emits `ShipIdleEvent`.
- A background worker checks all ships every 10 minutes and performs cache-aware marketplace and shipyard refreshes.
- Duplicate event delivery does not cause duplicate unsafe POST commands.
