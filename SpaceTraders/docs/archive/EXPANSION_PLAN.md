# SpaceTraders — Expansion Plan

> This plan is **generic** and applies to any new run. Specific waypoint names, ship prices, and
> credit amounts will differ each reset. Consult the current startup snapshot to fill in the blanks
> where `[placeholder]` notation is used.

## Starting Conditions (check your snapshot)

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
| Jump gate | `Waypoint.type == "JUMP_GATE"` — check `isUnderConstruction` |

### Typical starting shape (consistent across resets)

- **2 ships:** one COMMAND frigate at HQ, one SATELLITE probe already docked elsewhere in the system
- **~20–30 marketplaces** spread across the system; only the HQ marketplace has live trade data at start
- **2–3 shipyards** — one is usually the satellite's current location and will sell Mining Drones / Surveyors
- **1 jump gate under construction** in the outer system
- The command ship is equipped with a mining laser, surveyor mount, and gas siphon — making it mining-capable from day one

---

## Phase 1 — Scout All Marketplaces with the Command Ship

**Goal:** Collect live trade-good data from every marketplace in the starting system.

1. Sort all marketplace waypoints by distance from HQ (use their `x`/`y` coordinates)
2. Group them into distance clusters and plan an outward spiral route so the ship never backtracks far
3. Refuel at every FUEL_STATION or marketplace that trades FUEL along the way to avoid stranding
4. When visiting a waypoint with the `SHIPYARD` trait, record every ship type and purchase price — this feeds Phase 2
5. When visiting the JUMP_GATE, read its `Construction` resource requirements — this feeds Phase 6
6. The satellite probe already covers its docked marketplace for free; skip re-visiting it with the command ship unless it is a shipyard you need to price-check

**Routing tips:**
- Inner marketplaces (orbitals, moons near HQ) — visit first, cheapest travel cost
- Mid-range planets and asteroid bases — visit on the outward leg; use any fuel station en route
- Outer fuel stations and asteroid bases near the jump gate — chain them together; always refuel before the final jump gate leg and before returning home

---

## Phase 2 — Identify the Cheapest Market Drone

**Goal:** Find the lowest-cost ship that can serve as a stationary market drone.

- A market drone only needs to **dock at a marketplace** — it requires no fuel, no cargo, and no crew
- The ideal frame is `FRAME_PROBE` (used by `SHIP_PROBE`, `SHIP_SURVEYOR`, etc.)
- Compare prices across all shipyards discovered in Phase 1; pick the **cheapest probe-frame ship**
- If no probe is available, any cheap ship that can dock will do — even a mining drone works as a fallback

> **Decision point:** Record the cheapest drone price and multiply by the number of marketplaces to estimate total drone budget.

---

## Phase 3 — Deploy Market Drones to All Marketplaces

**Goal:** Station one drone at every marketplace for continuous, live price data.

### Purchase priority (do the most valuable hubs first)

1. **Industrial planets and moons** — most likely to export manufactured goods with strong margins
2. **High-tech / research stations** (traits: `HIGH_TECH`, `RESEARCH_FACILITY`) — expensive exports, rare imports
3. **Shipyard marketplaces** — need live data to track ship prices over time
4. **Asteroid bases** (especially pirate bases) — can have surprising trade volumes
5. **Fuel stations** — useful for fuel price arbitrage monitoring
6. **Remaining moons and orbital stations**

### Budget guidance

- Calculate: `drone_price × marketplace_count` = total drone budget
- Buy in batches; always keep a **minimum reserve** (suggested: the cost of ~2 drones) in the bank
- The command ship ferries newly purchased drones from the nearest shipyard to their target waypoint, then leaves them docked

---

## Phase 4 — Command Ship: Mining or Contract

**Goal:** Generate income while drones are being rolled out.

Check the command ship's mounts first:

- **Has `MOUNT_MINING_LASER_*`** → asteroid belt mining is viable (recommended)
- **Has `MOUNT_SURVEYOR_*`** → use survey-then-mine cycle for better ore quality
- **Has `MOUNT_GAS_SIPHON_*`** → gas giant siphoning is an option if a gas giant is nearby

### Option A — Mining (recommended if the command ship has a mining laser)

1. Fly to the nearest asteroid cluster (look for waypoints with `MINERAL_DEPOSITS` or `COMMON_METAL_DEPOSITS` traits)
2. If the ship has a surveyor, survey first — this increases the chance of extracting high-value ores
3. Mine until cargo is full, then sell at the nearest marketplace that imports ores or has exchange goods
4. Refuel at belt fuel stations; repeat

### Option B — Trading

- Wait until market drones provide enough price data to identify a profitable buy/sell pair
- Best suited once most drones are deployed; less setup than mining but depends on route margins

### Option C — Contract (highest priority if one exists)

- Check `Contracts` in the snapshot or via the API for any active/available contracts
- Contracts pay a bonus on acceptance plus a reward on fulfillment — highest early-game ROI
- If a contract requires delivering mined goods, align it with Option A mining runs

> **Recommended flow:** Accept any available contract → fulfill it via mining → transition to free mining or trading

---

## Phase 5 — Expand the Fleet (Miners and Haulers)

**Goal:** Scale income with dedicated ships, freeing the command ship from routine logistics.

| Role | Ship type to buy | Quantity | Purpose |
|---|---|---|---|
| Surveyor | Cheapest ship with `MOUNT_SURVEYOR_*` | 1 | Station in asteroid belt; generates surveys for all miners |
| Mining drone | `SHIP_MINING_DRONE` or similar | 2–4 | Dedicated autonomous mining in the belt |
| Hauler | `SHIP_LIGHT_HAULER` or cheapest hauler | 1 | Collect ore from drones and deliver to market |

- Deploy the surveyor to the best asteroid cluster (identified in Phase 4)
- Mining drones work autonomously once a survey is active; the hauler handles pickup and delivery
- Once a hauler is running, the command ship is free for jump gate material runs or deeper scouting
- **Target credit buffer before starting jump gate deliveries:** enough to sustain fleet operations for several round trips without dipping into the minimum reserve

---

## Phase 6 — Finish the Jump Gate

**Goal:** Complete construction of the system's jump gate.

1. Visit the jump gate waypoint to read its `Construction` material requirements (obtained in Phase 1)
2. Cross-reference the required materials against the live market data from drones to find the cheapest suppliers
3. Assign a dedicated hauler to a loop: buy materials → deliver to gate → repeat
4. Use any fuel stations along the outer-system route to keep the hauler's tank full
5. If multiple material types are required, prioritize the most expensive or scarcest first, as they take longest to accumulate
6. Once all materials are delivered and construction completes, the gate activates and inter-system connections open

---

## Phase 7 — Expand Outside the System

**Goal:** Use the completed jump gate to reach and exploit neighboring systems.

1. Check the jump gate's connection list for reachable systems
2. Send the command ship (or a dedicated scout) through first to chart the new system — waypoints, marketplaces, shipyards, and resources
3. Deploy market drones to the new system's marketplaces (buy from its local shipyards if available and cheaper)
4. Identify cross-system trade routes: goods cheap in the home system that are expensive in the new one, and vice versa
5. Replicate the local expansion playbook in the new system: drones → miners/surveyors → haulers
6. If the command ship is not jump-capable, purchase a jump-capable vessel (check for `SHIP_EXPLORER` or similar at discovered shipyards)

---

## Credits Planning Template

Use this template after completing Phase 1 scouting:

| Milestone | Formula | Estimated Cost |
|---|---|---|
| First batch of priority drones (5) | `drone_price × 5` | — |
| Full drone coverage | `drone_price × marketplace_count` | — |
| Surveyor | `surveyor_price` (from shipyard) | — |
| 2–4 Mining Drones | `mining_drone_price × quantity` | — |
| 1 Hauler | `hauler_price` (from shipyard) | — |
| Jump gate materials | Sum of construction requirements | TBD after Phase 1 |

> **Rule of thumb:** Never spend below a 2-drone-equivalent reserve. Start with the highest-ROI drones
> and miners, fund the rest through contract and mining income before scaling further.
