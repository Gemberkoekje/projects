# 01 – Domain Model

## Goals
- Represent all SpaceTraders concepts as rich domain objects (not just API DTOs).
- Carry domain events that the Application layer reacts to.
- Zero dependencies on infrastructure or framework libraries.

---

## 1.1 Aggregate Roots

### `Agent`
```
Agent
├── Symbol              : string
├── Credits             : long
├── StartingFaction     : string
├── ShipCount           : int
└── HeadquartersSymbol  : string
```
**Raises:**
- `AgentCreditsChangedEvent` – when credits increase/decrease beyond a configurable threshold.

---

### `Ship`
```
Ship
├── Symbol              : string
├── Role                : ShipRole (enum)
├── Status              : ShipStatus (enum) – DOCKED | IN_TRANSIT | IN_ORBIT
├── FlightMode          : FlightMode (enum) – DRIFT | STEALTH | CRUISE | BURN
├── CurrentWaypoint     : WaypointSymbol (value object)
├── CurrentSystem       : SystemSymbol (value object)
├── Fuel                : Fuel { Current, Capacity }
├── Cargo               : Cargo { Units, Capacity, Items: CargoItem[] }
├── Plan                : ShipPlan? (see §1.3)
└── LastSyncedAt        : DateTimeOffset
```
**Raises:**
- `ShipPlanCompletedEvent` – when a ship finishes its current plan.
- `ShipCargoSoldEvent` – when cargo is sold (carries TradeSymbol, units, totalRevenue).
- `ShipArrivedEvent` / `ShipInOrbitEvent` – when navigation completes and the ship is in orbit.
- `ShipFuelLowEvent` – when fuel falls below 20 % of capacity.

---

### `Contract`
```
Contract
├── Id                  : string
├── FactionSymbol       : string
├── Type                : ContractType (enum)
├── Terms               : ContractTerms { Deadline, Payment, DeliverGoods[] }
├── IsAccepted          : bool
├── IsFulfilled         : bool
└── Expiration          : DateTimeOffset
```
**Raises:**
- `ContractDeadlineApproachingEvent` – 24 h / 6 h warnings.
- `ContractFulfilledEvent`.

---

### `Market`
```
Market
├── WaypointSymbol      : WaypointSymbol
├── TradeGoods          : TradeGood[]  { Symbol, Type, Supply, Activity, PurchasePrice, SellPrice, TradeVolume }
├── Imports             : TradeSymbol[]
├── Exports             : TradeSymbol[]
├── Exchange            : TradeSymbol[]
└── LastObservedAt      : DateTimeOffset
```
No domain events – pure read model used for decision making.

---

### `Shipyard`
```
Shipyard
├── WaypointSymbol      : WaypointSymbol
├── ShipTypes           : ShipyardShip[]  { Type, PurchasePrice, ... }
└── LastObservedAt      : DateTimeOffset
```

---

## 1.2 Value Objects

| Value Object | Fields | Notes |
|---|---|---|
| `WaypointSymbol` | `string Value` | format `SYS-WP-XXX` |
| `SystemSymbol` | `string Value` | format `SYS-XXX` |
| `TradeSymbol` | `string Value` | e.g. `IRON_ORE` |
| `Fuel` | `int Current, int Capacity` | |
| `CargoItem` | `TradeSymbol Symbol, int Units` | |
| `Cargo` | `int Units, int Capacity, IReadOnlyList<CargoItem> Items` | |
| `ContractPayment` | `long OnAccepted, long OnFulfilled` | |
| `DeliverGood` | `TradeSymbol, WaypointSymbol Destination, int UnitsRequired, int UnitsFulfilled` | |

---

## 1.3 Ship Plan (value object / owned entity)

Represents the current autonomous intent of a ship. Target automation persists plan intent and derives the next action from the ship's physical state. It does not persist procedural state-machine steps.

```csharp
public enum ShipPlanRole
{
    None,
    Idle,
    Miner,
    Trader,
    Scout,
    Contract,
    Recovery,
}

public sealed class ShipPlan
{
    public ShipPlanRole Role          { get; init; }
    public WaypointSymbol Objective   { get; init; }
    public TradeSymbol Cargo          { get; init; }
    public int CargoUnits             { get; init; }
    public string DetailsJson         { get; init; }
    public DateTimeOffset PlannedAt   { get; init; }
}
```

See [ship-event-command-plan.md](ship-event-command-plan.md) for persisted `ShipPlanRecord` columns and role-specific JSON details.

---

## 1.4 Domain Events (full list)

```
AgentCreditsChangedEvent       { long OldCredits, long NewCredits }
ShipPlanCompletedEvent         { string ShipSymbol, ShipPlanRole CompletedRole }
ShipCargoSoldEvent             { string ShipSymbol, TradeSymbol Good, int Units, long Revenue, long NewAgentCredits }
ShipDockedEvent                { string ShipSymbol, WaypointSymbol Waypoint }
ShipInOrbitEvent               { string ShipSymbol, WaypointSymbol Waypoint }
ShipInTransitEvent             { string ShipSymbol, WaypointSymbol Origin, WaypointSymbol Destination, DateTimeOffset ArrivesAt }
ShipArrivedEvent               { string ShipSymbol, WaypointSymbol Waypoint }
ShipIdleDockedEvent            { string ShipSymbol, WaypointSymbol Waypoint }
ShipNeedsDockingEvent          { string ShipSymbol, WaypointSymbol Waypoint }
ShipFuelLowEvent               { string ShipSymbol, int CurrentFuel, int Capacity }
ContractAcceptedEvent          { string ContractId }
ContractDeadlineApproachingEvent { string ContractId, TimeSpan Remaining }
ContractFulfilledEvent         { string ContractId, long Payment }
MarketDataRefreshedEvent       { WaypointSymbol Waypoint }
MarketPricesChangedEvent       { WaypointSymbol Waypoint, TradeSymbol[] ChangedGoods }
ShipyardDataRefreshedEvent     { WaypointSymbol Waypoint }
NewShipPurchasedEvent          { string ShipSymbol, ShipType Type, long CostPaid }
```

---

## 1.5 Folder Structure

```
SpaceTraders.Domain/
├── Aggregates/
│   ├── AgentAggregate/
│   │   ├── Agent.cs
│   │   └── Events/
│   │       └── AgentCreditsChangedEvent.cs
│   ├── ShipAggregate/
│   │   ├── Ship.cs
│   │   ├── ShipPlan.cs
│   │   └── Events/
│   │       ├── ShipPlanCompletedEvent.cs
│   │       ├── ShipCargoSoldEvent.cs
│   │       ├── ShipDockedEvent.cs
│   │       ├── ShipInOrbitEvent.cs
│   │       ├── ShipArrivedEvent.cs
│   │       └── ShipFuelLowEvent.cs
│   ├── ContractAggregate/
│   │   ├── Contract.cs
│   │   └── Events/
│   │       ├── ContractAcceptedEvent.cs
│   │       ├── ContractDeadlineApproachingEvent.cs
│   │       └── ContractFulfilledEvent.cs
│   └── MarketAggregate/
│       ├── Market.cs
│       ├── Shipyard.cs
│       └── Events/
│           ├── MarketDataRefreshedEvent.cs
│           └── ShipyardDataRefreshedEvent.cs
├── ValueObjects/
│   ├── WaypointSymbol.cs
│   ├── SystemSymbol.cs
│   ├── TradeSymbol.cs
│   ├── Fuel.cs
│   ├── Cargo.cs
│   └── CargoItem.cs
├── Enums/
│   ├── ShipRole.cs
│   ├── ShipStatus.cs
│   ├── FlightMode.cs
│   ├── AssignmentType.cs
│   └── ContractType.cs
└── Common/
    └── IDomainEvent.cs
```
