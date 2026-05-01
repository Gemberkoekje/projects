# SpaceTraders SPECTERDEBUG3 — Expansion Plan

> Based on snapshot: `startup-snapshot-6-20260501-223730-initial.json`

## Initial State Summary

| Item | Value |
|---|---|
| Agent | SPECTERDEBUG3 |
| Credits | 176,394 cr |
| Faction | COBALT |
| System | X1-BQ60 (RED_STAR) |
| HQ | X1-BQ60-A1 (PLANET) |
| Ships | 2 |

### Ships

| Symbol | Role | Frame | Location | Notes |
|---|---|---|---|---|
| SPECTERDEBUG3-1 | COMMAND | FRAME_FRIGATE | X1-BQ60-A1 (HQ) | Mining Laser II, Gas Siphon II, Surveyor II, Sensor Array II; Fuel 400/400; Cargo 40 |
| SPECTERDEBUG3-2 | SATELLITE | FRAME_PROBE | X1-BQ60-H62 | No fuel/cargo; stationary market drone |

### Known Shipyards

| Waypoint | Type | Ships Available |
|---|---|---|
| X1-BQ60-A2 | MOON (orbits HQ) | Unvisited — needs scouting |
| X1-BQ60-C44 | ORBITAL_STATION (dist ~179) | Unvisited — needs scouting |
| X1-BQ60-H62 | MOON (dist ~58) | SHIP_MINING_DRONE 38,957 cr · SHIP_SURVEYOR 29,860 cr |

### Jump Gate

- **X1-BQ60-I65** — **UNDER CONSTRUCTION** (~451 units from HQ)
- Construction material requirements unknown until visited

### Marketplaces

30 total across the system. Only X1-BQ60-A1 has live trade data (FOOD, MEDICINE, CLOTHING, EQUIPMENT, JEWELRY, EXOTIC_MATTER imports; FUEL exchange).

---

## Phase 1 — Scout All Marketplaces with the Command Ship

**Goal:** Collect live trade-good data from all 30 marketplaces.

Plot an efficient outward route from HQ, using fuel stations to top up:

- **Cluster A (dist 0):** A2 *(SHIPYARD)*, A3, A4 — leave HQ and hit these first; refuel at A1 before departing
- **Cluster DB/E/F (dist 50–55):** DB5E, E48, E49, E50, F52, F53, F54, F56
- **Cluster H (dist 58):** H61, H62 *(SHIPYARD — satellite already here)*, H63, H64
- **Cluster G (dist 83):** G58, G59, G60
- **Cluster D (dist 93):** D46, D47
- **Cluster K/C (dist 113–179):** K95, C45 *(FUEL STATION)*, C44 *(SHIPYARD)*, B6 *(FUEL STATION)*
- **Cluster B/I (dist 228–321):** B7, I66 *(FUEL STATION)*
- **Far cluster (dist 451–719):** I65 *(JUMP GATE)*, J67 *(FUEL STATION)*, J68 *(PIRATE ASTEROID BASE)* — chain fuel stations to manage range

At each SHIPYARD (A2, C44, H62), record all available ship types and prices to inform Phase 2.

---

## Phase 2 — Identify the Cheapest Market Drone

**Goal:** Pick the lowest-cost ship suitable as a stationary market drone.

- From the snapshot H62 sells **SHIP_SURVEYOR at 29,860 cr** and SHIP_MINING_DRONE at 38,957 cr.
- A2 and C44 are unvisited — check during Phase 1 for SHIP_PROBE (~20,000–25,000 cr) or cheaper alternatives.
- A market drone only needs to dock at a marketplace; no fuel or cargo capacity required, so a FRAME_PROBE is ideal.
- Choose the **cheapest available probe-frame ship** from any shipyard as the standard drone.

---

## Phase 3 — Deploy Market Drones to All Marketplaces

**Goal:** One drone per marketplace for continuous price data (30 total).

### Purchase priority (highest ROI first)

1. **Industrial planets** (D46, E48, G58, H61, K95) — likely exporters of manufactured goods
2. **High-tech / research stations** (A4 RESEARCH_FACILITY, F53 HIGH_TECH)
3. **Shipyard marketplaces** (A2, C44, H62)
4. **Asteroid base / pirate base** (B7, J68) — trade volume and arbitrage opportunities
5. **Fuel stations** (B6, C45, I66, J67) — fuel price tracking
6. Remaining moons and orbital stations

### Budget guidance

At ~29,860 cr each, 30 drones ≈ **895,000 cr** total. Buy in batches; maintain a minimum **50,000 cr reserve** at all times. The command ship ferries newly purchased drones from a shipyard to their target marketplace.

---

## Phase 4 — Command Ship: Mining or Contract

**Goal:** Generate income while drones are being deployed.

The command ship has a premium mining suite (Mining Laser II + Surveyor II + Gas Siphon II, cargo 40):

### Option A — Mining (recommended early)

- Move to the asteroid belt (B8–B15, dist ~330–360 from HQ)
- Use Surveyor II to identify high-value deposit sites (GOLD_ORE, PLATINUM_ORE, DIAMONDS, URANITE_ORE)
- Mine until cargo is full (40 units), then sell at the nearest marketplace with demand
- Refuel via the belt's nearby fuel stations when needed

### Option B — Trading

- Once market drones are providing live data, identify the best buy-low/sell-high pair
- With 40 cargo units, margins need to be >500 cr/unit to be competitive with mining

### Option C — Contract (highest priority if available)

- Check for an active or available contract; contracts pay an on-accept bonus plus on-fulfill reward
- If a mining or delivery contract exists, complete it first to compound starting capital
- Accept any pending contract before beginning routine mining

> **Recommended flow:** Accept contract → mine to fulfill it → transition to free mining/trading

---

## Phase 5 — Expand the Fleet (Miners and Haulers)

**Goal:** Scale up income with dedicated mining and hauling ships.

| Ship Type | Quantity | Purpose | Approx. Cost |
|---|---|---|---|
| SHIP_SURVEYOR | 1 | Continuous asteroid surveying in belt | 29,860 cr |
| SHIP_MINING_DRONE | 2–4 | Dedicated belt mining | 38,957 cr each |
| Hauler (SHIP_LIGHT_HAULER or similar) | 1 | Shuttle ore from belt to market | ~50,000–100,000 cr |

- Assign the Surveyor to the asteroid belt (B8–B15) to generate surveys for all miners
- Mining drones work autonomously; hauler collects their output, freeing the command ship from logistics
- **Income target before jump gate work:** ~500,000–1,000,000 cr buffer to fund materials and ongoing ops

---

## Phase 6 — Finish the Jump Gate

**Goal:** Complete construction of X1-BQ60-I65.

1. Visit X1-BQ60-I65 to read the `Construction` resource requirements (not in snapshot)
2. Identify which in-system markets supply the required materials (typically FAB_MATS, ADVANCED_CIRCUITRY, or similar)
3. Assign a dedicated hauler to repeatedly deliver materials to the gate site
4. Use fuel stations at I66 and J67 to keep haulers refueled on outer-system runs
5. Once construction completes the gate becomes active and connections open

---

## Phase 7 — Expand Outside the System

**Goal:** Use the completed jump gate to enter neighboring systems.

1. Check the jump gate's connection list for reachable systems
2. Send the command ship (or a scout) through first to chart the new system's waypoints, marketplaces, and shipyards
3. Establish a market drone presence in the new system's key marketplaces
4. Identify cross-system trade routes with favorable margins
5. Replicate the local expansion playbook (drones → miners → haulers) in the new system
6. If the command ship cannot jump, procure a jump-capable vessel from a shipyard discovered in Phase 1

---

## Credits Milestones

| Milestone | Estimated Credits Needed |
|---|---|
| First 5 market drones (priority hubs) | ~150,000 cr |
| 1× Surveyor + 2× Mining Drones | ~108,000 cr |
| 1× Hauler | ~50,000–100,000 cr |
| All 30 market drones | ~900,000 cr |
| Jump gate construction materials | TBD (visit gate first) |

> **Starting treasury: 176,394 cr.**  
> Buy 3–4 priority drones, then fund the rest through mining + contract income before scaling further.
