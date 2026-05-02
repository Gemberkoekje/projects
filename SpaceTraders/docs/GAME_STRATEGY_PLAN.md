# SpaceTraders — Game Strategy Plan

> **Primary objective:** Complete the jump gate construction as fast as possible, then expand to
> connected systems. Experienced players complete this in **18–24 hours** after reset — that is
> your benchmark.

This document replaces `EXPANSION_PLAN.md` and `STARTER_SYSTEM_STRATEGY.md`. Every strategic phase
is expressed in terms of the goal-driven orchestrator architecture described in
`SHIP_GOAL_DRIVEN_ARCHITECTURE_PLAN.md`. The orchestrator maintains a prioritised list of active
`FleetGoal` records; ships are always assigned to the highest-priority unmet goal first. You do not
need to issue ship commands manually — configure the evaluators and budget policy correctly and the
orchestrator drives everything.

---

## Starting Conditions Checklist

Check these items in your snapshot at the start of each reset:

| Item | What to look up |
|---|---|
| Agent symbol | `Agent.symbol` |
| Starting credits | `Agent.credits` |
| Starting faction | `Agent.startingFaction` |
| Home system | `Agent.headquarters` (system prefix) |
| HQ waypoint | `Agent.headquarters` |
| Command ship | Ship with `registration.role == "COMMAND"` — note its mounts, cargo, and fuel capacity |
| Satellite probe | Ship with `registration.role == "SATELLITE"` — already deployed as a passive market drone |
| Marketplaces | All `Waypoint.traits` containing `MARKETPLACE` — count them and note their types |
| Shipyards | All `Waypoint.traits` containing `SHIPYARD` — visit each during Phase 1 |
| Jump gate | `Waypoint.type == "JUMP_GATE"` — read `isUnderConstruction` and construction material requirements |

### Typical starting shape (consistent across resets)

- **2 ships:** one COMMAND frigate at HQ, one SATELLITE probe already docked elsewhere in the system
- **~20–30 marketplaces** spread across the system; only the HQ marketplace has live trade data at start
- **2–3 shipyards** — one is usually the satellite's current location and will sell Mining Drones / Surveyors
- **1 jump gate under construction** in the outer system
- The command ship is equipped with a mining laser, surveyor mount, and gas siphon — making it mining-capable from day one

---

## Orchestrator Fleet Goal Priority Reference

The orchestrator evaluates this goal list on every tick and assigns idle ships to the
highest-priority unmet goal first. All priorities follow the constants defined in
`FleetGoalPriority` (see the architecture plan).

| Priority | `FleetGoalKind` | Strategic phase | Description |
|---|---|---|---|
| 10 | `MarketScouting` | Phase 1 | Scout every marketplace with no cached price data |
| 20 | `Contract` | Phase 2 | Fulfill any active contract (deadline-bound, highest credit ROI) |
| 30 | `Construction` | Phase 3–4 | Supply jump gate construction materials |
| 31 | `Construction` *(precursor)* | Phase 3 | Keep supply-chain nodes above `LIMITED` — one goal per bottleneck node, deepest node gets lowest sub-priority number |
| 40 | `MarketCoverage` | Phase 6 | Station a probe permanently at each marketplace |
| 50 | `FleetExpansion` | Phases 5–7 | Purchase a new ship when the fleet is the bottleneck |

> **Sub-priority note:** `SupplyChainPriming` is not a separate `FleetGoalKind`. Represent it as
> a `Construction` goal at priority 31, with a `description` field such as
> `"Prime supply chain: supply IRON to X1-TD7-M3"`. The evaluator assigns the lowest sub-priority
> number (e.g. 31, 32, 33 …) to the deepest nodes in the dependency tree so the most foundational
> bottlenecks are resolved first.

---

## Phase-by-Phase Strategy

### Phase 1 — Market Scouting (Orchestrator priority 10)

**Goal:** Collect live trade-good data from every marketplace in the starting system so every
subsequent decision is based on real prices.

The `MarketScoutingGoalEvaluator` automatically produces one `MarketScouting` fleet goal for every
market waypoint that has no cached price data (or data older than one game day). The orchestrator
dispatches idle ships to `ScoutWaypoint` ship goals resolved by the `AssignmentResolver`.

#### What the orchestrator does automatically

- Produces `MarketScouting` goals (priority 10) for every unvisited market at game start.
- Dispatches the command ship (and the satellite probe, if idle) to `ScoutWaypoint` goals.
- Does not re-produce a goal for a waypoint that already has a scout assigned.
- Transitions completed scouts to `PatrolMarket` goals once a waypoint has fresh price data.

#### Routing guidance (for manual override or resolver tuning)

- Sort marketplace waypoints by distance from HQ; visit inner orbitals and moons first.
- Refuel at every `FUEL_STATION` or market that trades `FUEL` along the route.
- When visiting a shipyard waypoint, record every ship type and purchase price — this data feeds Phase 5 fleet expansion.
- When visiting the jump gate, read its `Construction` material requirements — this feeds Phase 3.
- The satellite probe already covers its docked marketplace; skip re-visiting it unless it is a shipyard.

---

### Phase 2 — Contract Fulfillment (Orchestrator priority 20)

**Goal:** Generate early-game credits by fulfilling contracts. Contracts pay an acceptance bonus
plus a fulfillment reward and are the highest-ROI early activity.

The `ContractGoalEvaluator` produces a `Contract` fleet goal for every accepted contract. The
orchestrator translates it via `ResourceProductionGoal` → `AssignmentResolver` → concrete ship goals
(`MineResource`, `DeliverCargo`).

#### Strategy

- Accept any available contract immediately on game start.
- Where the contract requires mined goods, align it with the mining loop from Phase 4 — the same
  ship that mines fills the contract delivery.
- The orchestrator assigns the best-capable idle ship; if no idle ship is available it will
  evaluate a `FleetExpansion` goal.
- Contract goals preempt construction goals (priority 20 < 30), so the fleet will always finish
  contract deliveries before switching fully to gate construction.

---

### Phase 3 — Jump Gate Construction (Orchestrator priority 30)

**Goal:** Supply all required construction materials to the jump gate. This is the primary
long-term objective of the starter phase.

The `ConstructionGoalEvaluator` produces one `Construction` fleet goal per required material type
(e.g. `FAB_MAT`, `ADVANCED_CIRCUIT`). Each goal resolves to `MineResource` or `SupplyConstruction`
ship goals via the `AssignmentResolver`.

#### Advanced circuits — ring-fencing

Mark advanced circuits as ring-fenced in the construction goal payload. The orchestrator must never
assign a ship carrying advanced circuits to a `SellCargo` goal. Every unit of advanced circuits
must go directly to the construction site.

#### FAB_MAT bulk buying rule

Configure `IBudgetPolicy` with the following threshold:

```
if unit_price <= 2_500 AND available_credits > operating_reserve:
    buy max(affordable_units, 60)   // 60 is the volume cap in starter systems
else:
    wait for price recovery
```

- Do **not** apply a percentage-based hourly cap on construction purchases. Buying in small trickles
  wastes money because each purchase increments the price. Dump large quantities when the price is right.
- Keep a minimum credit reserve (see Budget Policy Reference below) but deploy everything above it
  into construction materials when the price condition is met.

#### Supply chain priming

The `ConstructionGoalEvaluator` traces the full transitive import dependency of each construction
material recursively:

- `FAB_MAT` depends on upstream goods (e.g. `IRON`, `ALUMINUM`), which depend on their own raw
  inputs.
- `ADVANCED_CIRCUIT` depends on `MICROPROCESSORS` or `ELECTRONICS`, which depend on
  `SILICON_CRYSTALS`, `COPPER`, etc.

For every node in that dependency tree that is currently at `LIMITED` or `SCARCE` supply, the
evaluator emits a `Construction` goal at **priority 31** with `description` indicating it is a
precursor supply goal. The deepest nodes (fewest upstream dependencies) get the lowest sub-priority
numbers so foundational bottlenecks are resolved first.

Supply level target:

```
SCARCE → LIMITED → MODERATE → HIGH → ABUNDANT
```

Keep every node **above `LIMITED`**. Once a market recovers to `MODERATE` or above, the
evaluator removes the corresponding priority-31 goal and redirects effort to the next bottleneck.

---

### Phase 4 — Mining for Income and Market Stimulation (Orchestrator priority 20–30, subordinate)

**Goal:** Generate credits while construction is in progress, and prime the supply chain by selling
mined goods to upstream markets.

When no contract is active and the fleet has idle mining ships, the orchestrator assigns them
`MineResource` goals for goods that are supply-chain precursors of the construction materials.
The `MineResourceGoalExecutor` handles the full mine → sell cycle autonomously, including refuelling,
survey-first extraction, and sell route selection.

#### Sell destination selection

Configure the `AssignmentResolver` sell resolution to prefer marketplaces that:

1. Import the mined ore **and**
2. Are part of the construction supply chain (the ore is a precursor for a construction material).

This simultaneously generates credits and stimulates market growth, lowering future FAB_MAT prices.

#### Mining approach

1. `AssignmentResolver` selects the nearest asteroid cluster with `MINERAL_DEPOSITS` or `COMMON_METAL_DEPOSITS`.
2. If the ship has a surveyor mount, it surveys first to improve ore quality.
3. Mine until cargo is full; `MineResourceGoalExecutor` navigates to the best sell market automatically.
4. Repeat until the goal is completed or a higher-priority goal preempts the assignment.

---

### Phase 5 — Fleet Expansion (Orchestrator priority 50)

**Goal:** Scale income and construction throughput by adding dedicated ships.

`FleetExpansionGoalEvaluator` identifies the bottleneck (mining extraction rate, haul capacity, or
market coverage gap) and emits a `FleetExpansion` fleet goal. The orchestrator emits a
`PurchaseShipCommand` when `IBudgetPolicy` permits.

#### Purchase order

| Step | Role | Ship type | Purpose |
|---|---|---|---|
| 1 | Surveyor | Cheapest ship with `MOUNT_SURVEYOR_*` | Station in asteroid belt; generates surveys for all miners |
| 2–5 | Mining drone | `SHIP_MINING_DRONE` or similar | Dedicated autonomous mining in the belt |
| 6 | Hauler | `SHIP_LIGHT_HAULER` or cheapest hauler | Collect ore from drones and deliver to market |

- After each purchase, the new ship is immediately assigned to the bottleneck goal via
  `AssignShipToGoalCommand`.
- `FleetExpansion` is blocked while `credits < minimum_reserve + ship_purchase_price` (see Budget
  Policy Reference).
- Once a hauler is operating, the command ship is freed for jump gate material runs or inter-system
  scouting.

---

### Phase 6 — Market Coverage (Orchestrator priority 40)

**Goal:** Station a probe permanently at every marketplace for continuous live price data.

`MarketCoverageGoalEvaluator` emits `MarketCoverage` fleet goals for every marketplace that has no
permanently stationed probe. The `AssignmentResolver` returns a `PatrolMarket` ship goal for known
markets and a `ScoutWaypoint` goal for any market with no snapshot yet.

#### Probe deployment priority

Deploy probes to the most valuable market hubs first:

1. Industrial planets and moons (most likely to export manufactured goods with strong margins)
2. High-tech / research stations (traits: `HIGH_TECH`, `RESEARCH_FACILITY`) — expensive exports, rare imports
3. Shipyard marketplaces — track ship prices over time
4. Asteroid bases (especially pirate bases) — can have surprising trade volumes
5. Fuel stations — useful for fuel-price arbitrage monitoring
6. Remaining moons and orbital stations

#### Probe fleet scaling

- Probes are stationary after deployment; operating cost is near zero.
- The top leaderboard agents (2,000+ ships) are mostly probe fleets providing market coverage at scale.
- API rate limits are per-call, not per-fleet — a large probe fleet does not saturate the rate limit
  while stationary.
- Deploy probes to new-system marketplaces as early as possible to build the price-data picture
  needed for profitable cross-system trading.

---

### Phase 7 — Post-Gate Expansion (Orchestrator priority 10 extended to new systems)

**Goal:** Use the completed jump gate to reach and exploit neighbouring systems.

When the jump gate construction completes, the `MarketScoutingGoalEvaluator` automatically emits
`MarketScouting` goals (priority 10) for every market waypoint in connected systems that has no
cached price data. The `AssignmentResolver` checks jump capability before assigning an inter-system
`ScoutWaypoint` goal.

#### Immediate steps after gate activation

1. The orchestrator produces `MarketScouting` goals for connected systems automatically — verify
   evaluator is running.
2. If the command ship is not jump-capable, check for `SHIP_EXPLORER` at discovered shipyards and
   emit a `FleetExpansion` goal for a jump-capable scout.
3. In each new system: replicate the full Phase 1–6 loop — scouting → contracts → construction →
   mining → expansion → market coverage.
4. Identify cross-system trade routes: goods cheap in the home system and expensive in the new one
   will emerge naturally from the `MarketCoverage` price data.
5. Buy probes from local shipyards in the new system where they are cheaper.

---

## Budget Policy Reference

Configure `IBudgetPolicy` with these values each reset:

| Parameter | Value | Notes |
|---|---|---|
| Minimum credit reserve | `probe_price × 2` | Never spend below this; ensures emergency probe purchases remain possible |
| FAB_MAT buy threshold | ≤ 2,500 cr/unit | Buy at this price or lower; wait for recovery otherwise |
| FAB_MAT transaction size | min(affordable_units, 60) | 60 is the volume cap in starter systems; always buy in one transaction |
| Hourly construction budget cap | **None** | Remove any percentage-based cap; buy aggressively when price is right |
| Fleet expansion minimum balance | `minimum_reserve + ship_price` | Ship purchase is blocked if this check fails |

> **Rule of thumb:** Never spend below the 2-probe-equivalent reserve. Beyond that floor, deploy
> every available credit into construction materials whenever the price condition is met.

---

## Quick-Reference Checklist (runtime)

Use this each reset to verify the orchestrator is configured correctly before leaving it to run:

- [ ] Check jump gate construction requirements; confirm `ConstructionGoalEvaluator` has produced `Construction` fleet goals for each required material
- [ ] Confirm `MarketScouting` goals (priority 10) exist for all unvisited marketplaces
- [ ] Accept any available contract; confirm `ContractGoalEvaluator` has produced a `Contract` fleet goal
- [ ] Ring-fence advanced circuits: verify construction goal payload marks them as non-sellable
- [ ] FAB_MAT buy threshold set at ≤ 2,500 cr/unit; confirm hard hourly cap is removed from `IBudgetPolicy`
- [ ] Mining ships assigned to `MineResource` goals targeting construction supply-chain precursors
- [ ] Supply chain audit: identify all nodes at `LIMITED` or `SCARCE` and confirm priority-31 `Construction` goals exist for each bottleneck
- [ ] Re-check supply levels each evaluation cycle; deprioritise recovered markets and redirect effort to remaining bottlenecks
- [ ] Fleet expansion check: surveyor → 2–4 mining drones → hauler, each gated by budget policy
- [ ] Market coverage probes deployed in priority order (industrial → high-tech → shipyard → asteroid → fuel → orbital)
- [ ] After gate completes: confirm `MarketScouting` goals emitted for all connected system markets
- [ ] Begin probe fleet deployment in connected systems using local shipyards where cheaper
