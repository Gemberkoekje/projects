# 04 – Application Layer: Wolverine Event Bus

> Automation update: ship automation should move toward [Ship Event Command Plan](ship-event-command-plan.md).
> Commands and events in this document remain useful, but target ship flows are state-gated event handlers with persisted ship plans, not assignment state machines.

## Goals
- Decouple automation decisions from API calls and persistence via Wolverine.
- Every significant game event produces a domain notification → one or more handlers react.
- Commands are dispatched into the Priority Queue so rate-limiting is respected.
- **POST responses are applied directly to local state** – no follow-up GETs.

---

## 4.0 Why Wolverine over MediatR?

**Recommendation: use Wolverine.**

| Concern | MediatR | Wolverine |
|---------|---------|-----------|
| Dispatch model | In-process only | In-process + optional durable outbox (Postgres) |
| Handler convention | Interface-based (`IRequestHandler<,>`) | Convention-based (method name `Handle`) – less boilerplate |
| Middleware / pipeline | Behaviour interfaces, manually registered | Attribute-based policies, auto-discovered |
| Message scheduling | Not built-in | Built-in (`ScheduleAsync`, delayed messages) |
| Retry / error handling | Not built-in | Built-in retry policies per message type |
| Durable messaging | No | Yes – backed by the same Postgres DB (no extra infra) |
| .NET 10 support | Yes | Yes (JasperFx/Wolverine, actively developed) |
| Complexity | Low | Medium |

For this project the durable outbox is valuable: if a ship action (POST navigate) succeeds but the process crashes before the response is persisted, Wolverine can replay the `ShipNavigatedEvent` from the outbox on restart. This directly supports the pod-restart recovery requirement.

**Why not write your own?**
A custom mediator is ~100 lines but you then own pipeline behaviours, error handling, and any future durable-messaging needs. Wolverine solves all of that and is well-maintained. Not recommended to roll your own here.

**MediatR is still acceptable** if Wolverine's learning curve feels too steep at the start – the handler shapes are similar enough to migrate later. But start with Wolverine.

---

## 4.1 Libraries

| Package | Purpose |
|---------|---------|
| `WolverineFx` | In-process message bus (commands, queries, notifications) |
| `WolverineFx.Persistence.Postgresql` | Durable outbox backed by the project's PostgreSQL DB |
| `FluentValidation.DependencyInjectionExtensions` | Command validation middleware |

---

## 4.2 Message Types

### Commands
Commands have a single handler. In Wolverine, a handler is a plain class with a `Handle` method – no interface required.

```
NavigateShipCommand          { ShipSymbol, DestinationWaypoint, Priority }
  → handler calls API, applies ShipNav (ArrivesAt) from response to CachedShip – no follow-up GET

DockShipCommand              { ShipSymbol }
  → applies ShipNav from response to CachedShip

OrbitShipCommand             { ShipSymbol }
  → applies ShipNav from response to CachedShip

ExtractResourcesCommand      { ShipSymbol }
  → applies Extraction + Cargo from response to CachedShip

SellCargoCommand             { ShipSymbol, TradeSymbol, Units }
  → applies Agent.Credits + Cargo from response; publishes ShipCargoSoldEvent

SellAllCargoCommand          { ShipSymbol }
  → loops SellCargoCommand per item; final state applied once

BuyCargoCommand              { ShipSymbol, TradeSymbol, Units }
  → applies Agent.Credits + Cargo from response

RefuelShipCommand            { ShipSymbol }
  → applies Fuel + Agent.Credits from response

PurchaseShipCommand          { ShipType, ShipyardWaypoint }
  → applies Agent + new Ship from response; publishes NewShipPurchasedEvent

AcceptContractCommand        { ContractId }
  → applies Contract from response

DeliverContractCommand       { ContractId, ShipSymbol, TradeSymbol, Units }
  → applies Contract from response

FulfillContractCommand       { ContractId }
  → applies Agent + Contract from response; publishes ContractFulfilledEvent

AssignShipCommand            { ShipSymbol, ShipAssignment }
  → pure local – updates ShipAssignmentRecord, no API call

RefreshMarketDataCommand     { WaypointSymbol }
  → GET only if: ship physically present AND TTL expired
  → upserts CachedMarket, publishes MarketDataRefreshedEvent

RefreshShipyardDataCommand   { WaypointSymbol }
  → GET only if: ship physically present AND TTL expired
  → upserts CachedShipyard, publishes ShipyardDataRefreshedEvent

SyncAllShipsCommand          → startup only: GET /my/ships (paginated)
SyncContractsCommand         → startup only: GET /my/contracts
SyncAgentCommand             → startup only: GET /my/agent
```

### Queries
Queries are Wolverine messages dispatched via `IMessageBus.InvokeAsync<TResult>()`. No interface implementation required.
```
GetAgentQuery                → AgentDto         (always from DB cache)
GetShipQuery                 { ShipSymbol }      → ShipDto (applies dead-reckoning before returning)
GetAllShipsQuery             → IReadOnlyList<ShipDto>
GetActiveContractsQuery      → IReadOnlyList<ContractDto>
GetBestTradeRouteQuery       { CargoCapacity }   → TradeOpportunityDto?
GetSettingsQuery             → IReadOnlyList<SettingDto>
GetActivityLogQuery          { Page, PageSize, ShipFilter? } → PagedResult<ActivityLogDto>
GetRateLimitStatusQuery      → RateLimitStatusDto
```

### Notifications (domain events – Wolverine fan-out to all handlers)
All domain events from `01-domain.md` implement `IDomainEvent`.
Wolverine discovers handlers by convention: `public void Handle(ShipCargoSoldEvent e)`.

---

## 4.3 Event → Handler Mapping

```
ShipCargoSoldEvent           { ShipSymbol, TradeSymbol, Units, Revenue, NewAgentCredits }
  ├── ApplySaleToAgentHandler       → updates CachedAgent.Credits = NewAgentCredits (no GET)
  ├── AssignShipAfterSaleHandler    → dispatches AssignShipCommand (§4.4)
  └── LogActivityHandler

ShipAssignmentCompletedEvent
  └── LegacyReassignShipHandler     → compatibility path while migrating to ShipPlanRecord

AgentCreditsChangedEvent           { OldCredits, NewCredits }
  └── FleetExpansionDecisionHandler → uses NewCredits directly (§4.5)

ContractDeadlineApproachingEvent
  └── ContractPriorityHandler       → re-assigns nearest ship to contract delivery

ContractFulfilledEvent             { Agent, Contract }
  └── LogActivityHandler            (credits already applied by FulfillContractCommand handler)

ShipFuelLowEvent
  └── RefuelHandler                 → schedules RefuelShipCommand at Priority.Critical

ShipArrivedEvent
  ├── ShipArrivedHandler            → emits ShipInOrbitEvent for state-gated handling
  └── MarketPollHandler             → dispatches RefreshMarketDataCommand if TTL expired

MarketDataRefreshedEvent
  ├── TradeOpportunityRecomputeHandler → recalculates TradeOpportunity table (no API call)
  └── MarketPriceChangeDetector        → emits MarketPricesChangedEvent when actionable prices changed

MarketPricesChangedEvent
  └── AffectedShipReplanHandler        → updates affected ShipPlanRecord rows and redirects ships where legal

ShipyardDataRefreshedEvent
  └── TradeOpportunityRecomputeHandler → same handler, separate overload

NewShipPurchasedEvent
  └── InitialAssignmentHandler      → Scout then Trade/Mine
```

---

## 4.4 Ship Role Planning Logic

```
1. Load ship from cache and confirm it is docked before it can become idle.
2. Load active contracts → urgent good we can fulfil? → create contract/trade ship plan.
3. If mining-capable and a viable asteroid plus sell market exists → create miner ship plan.
4. Load top TradeOpportunity for ship's cargo capacity → create trader ship plan.
5. If no route → create scout ship plan for unknown/stale waypoint data.
6. Else → persist idle docked plan.
7. Emit the matching ship-becomes-role event.
8. Log to ActivityLog.
```

---

## 4.5 Fleet Expansion Decision (`FleetExpansionDecisionHandler`)

```
Trigger: AgentCreditsChangedEvent (NewCredits > OldCredits)

1. Load settings: MinCreditReserve, MinCreditRatioForShip, MaxShips, PreferredShipType
2. Count CachedShip rows in DB
3. If count >= MaxShips → skip
4. Find cheapest CachedShipyard offering PreferredShipType
5. affordableCredits = NewCredits - MinCreditReserve   (credits from event, no GET)
6. If affordableCredits >= shipPrice * (1 + MinCreditRatioForShip) → dispatch PurchaseShipCommand
7. Log decision
```

---

## 4.6 Wolverine Pipeline Middleware

```
Message
  └── LoggingMiddleware          ← logs type + duration
        └── ValidationMiddleware ← FluentValidation; discards invalid messages
              └── ActivityLogMiddleware ← writes to ActivityLog for mutating commands
                    └── Handler
```

Applied via `WolverineOptions.Policies` in `DependencyInjection.cs`.

---

## 4.7 Folder Structure

```
SpaceTraders.Application/
├── DependencyInjection.cs               ← configures Wolverine + validators + middleware policies
├── Commands/
│   ├── Ships/
│   │   ├── NavigateShipCommand.cs + Handler   ← applies ShipNav from response
│   │   ├── DockShipCommand.cs + Handler
│   │   ├── OrbitShipCommand.cs + Handler
│   │   ├── ExtractResourcesCommand.cs + Handler
│   │   ├── SellCargoCommand.cs + Handler       ← applies Agent + Cargo from response
│   │   ├── SellAllCargoCommand.cs + Handler
│   │   ├── BuyCargoCommand.cs + Handler
│   │   ├── RefuelShipCommand.cs + Handler      ← applies Fuel + Agent from response
│   │   └── AssignShipCommand.cs + Handler      ← local only
│   ├── Fleet/
│   │   └── PurchaseShipCommand.cs + Handler    ← applies Agent + Ship from response
│   ├── Contracts/
│   │   ├── AcceptContractCommand.cs + Handler
│   │   ├── DeliverContractCommand.cs + Handler
│   │   └── FulfillContractCommand.cs + Handler ← applies Agent + Contract from response
│   └── Sync/
│       ├── RefreshMarketDataCommand.cs + Handler   ← only normal-operation GET
│       ├── RefreshShipyardDataCommand.cs + Handler ← only normal-operation GET
│       ├── SyncAllShipsCommand.cs + Handler        ← startup only
│       ├── SyncContractsCommand.cs + Handler       ← startup only
│       └── SyncAgentCommand.cs + Handler           ← startup only
├── Queries/
│   ├── GetAgentQuery.cs + Handler
│   ├── GetShipQuery.cs + Handler            ← calls ApplyArrivalIfDue() before DTO mapping
│   ├── GetAllShipsQuery.cs + Handler
│   ├── GetActiveContractsQuery.cs + Handler
│   ├── GetBestTradeRouteQuery.cs + Handler
│   ├── GetSettingsQuery.cs + Handler
│   ├── GetActivityLogQuery.cs + Handler
│   └── GetRateLimitStatusQuery.cs + Handler
├── EventHandlers/
│   ├── ApplySaleToAgentHandler.cs
│   ├── AssignShipAfterSaleHandler.cs
│   ├── ReassignShipHandler.cs
│   ├── FleetExpansionDecisionHandler.cs
│   ├── ContractPriorityHandler.cs
│   ├── RefuelHandler.cs
│   ├── ArrivalActionHandler.cs
│   ├── MarketPollHandler.cs
│   ├── TradeOpportunityRecomputeHandler.cs
│   ├── InitialAssignmentHandler.cs
│   └── LogActivityHandler.cs
├── Middleware/
│   ├── LoggingMiddleware.cs
│   ├── ValidationMiddleware.cs
│   └── ActivityLogMiddleware.cs
├── DTOs/
│   ├── AgentDto.cs
│   ├── ShipDto.cs
│   ├── ContractDto.cs
│   ├── TradeOpportunityDto.cs
│   ├── SettingDto.cs
│   ├── ActivityLogDto.cs
│   └── RateLimitStatusDto.cs
├── Interfaces/
│   ├── Repositories/          ← (interfaces only, implementations in Persistence)
│   └── ITradeAnalyser.cs
└── Services/
    └── TradeAnalyser.cs       ← pure logic, no I/O – computes best routes from cached data
```
