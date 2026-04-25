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
├── Assignment          : ShipAssignment? (see §1.3)
└── LastSyncedAt        : DateTimeOffset
```
**Raises:**
- `ShipAssignmentCompletedEvent` – when a ship finishes its current assignment.
- `ShipCargoSoldEvent` – when cargo is sold (carries TradeSymbol, units, totalRevenue).
- `ShipArrivedAtWaypointEvent` – when nav status transitions to DOCKED/IN_ORBIT.
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

## 1.3 Ship Assignment (value object / owned entity)

Represents the current autonomous task of a ship.

```csharp
public enum AssignmentType
{
    Idle,
    Mine,           // extract resources at an asteroid
    Haul,           // move cargo from A to B
    Trade,          // buy at origin, sell at destination
    FulfillContract,
    Refuel,
    Scout,          // visit waypoints to populate Market/Shipyard cache
    SellAll,        // sell everything in cargo at best available market
}

public sealed class ShipAssignment
{
    public AssignmentType Type        { get; init; }
    public WaypointSymbol? Origin     { get; init; }
    public WaypointSymbol? Destination{ get; init; }
    public TradeSymbol?    Cargo      { get; init; }
    public string?         ContractId { get; init; }
    public DateTimeOffset  AssignedAt { get; init; }
}
```

---

## 1.4 Domain Events (full list)

```
AgentCreditsChangedEvent       { long OldCredits, long NewCredits }
ShipAssignmentCompletedEvent   { string ShipSymbol, AssignmentType CompletedType }
ShipCargoSoldEvent             { string ShipSymbol, TradeSymbol Good, int Units, long Revenue, long NewAgentCredits }
ShipArrivedAtWaypointEvent     { string ShipSymbol, WaypointSymbol Waypoint }
ShipFuelLowEvent               { string ShipSymbol, int CurrentFuel, int Capacity }
ContractAcceptedEvent          { string ContractId }
ContractDeadlineApproachingEvent { string ContractId, TimeSpan Remaining }
ContractFulfilledEvent         { string ContractId, long Payment }
MarketDataRefreshedEvent       { WaypointSymbol Waypoint }
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
│   │   ├── ShipAssignment.cs
│   │   └── Events/
│   │       ├── ShipAssignmentCompletedEvent.cs
│   │       ├── ShipCargoSoldEvent.cs
│   │       ├── ShipArrivedAtWaypointEvent.cs
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
