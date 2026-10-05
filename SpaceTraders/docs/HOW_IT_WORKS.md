# How SpaceTraders works today

> Written from the code on 2026-10-01, not from the plans. Nothing was run, so this describes
> what the code says it does. Where the code contradicts its own intent, this document points to
> the known issue in `../PLAN.md` (B-numbers) instead of repeating it. Every PR that changes
> behaviour updates this file.

## The short version

One process, `SpaceTraders.API`, hosts everything.

- It serves an internal HTTP API under `/spacetraders/api`.
- Once its startup chain has finished, a loop runs every 5 seconds on the leader instance (the
  "tick"). The tick bootstraps the *plans* that are switched on, steps every ship's *goal*, and
  drives contract work.
- Ships act through in-process Wolverine commands and events, which call the SpaceTraders API
  through a rate-limited client.
- State lives in PostgreSQL.
- The React WebUI reads the internal API.

```text
startup chain ─► tick every 5 s (leader only)
                  ├─ bootstrap the plans that are on: Scout, Explore, Roles, Contract, ProbeDeployment, Survey, Mining, Siphon, Construction, Trading, SpareTime
                  ├─ one goal step per ship ─► executor ─► commands ─► SpaceTraders API
                  ├─ contract assignments: deliver, or mine
                  ├─ refresh one market where a ship is, once due (the market watch)
                  └─ publish API availability changes
arrival timer ─► ShipArrivedEvent ─► dock + refresh market ─► ShipNavigationCompletedEvent ─► goal step
health monitor every minute ─► health rules ─► anomalies (metric + journal)
```

---

## 1. Process and startup

### Hosting (`SpaceTraders.API/Program.cs`)

- **Path base:** `/spacetraders/api`. Paths without the prefix also route.
- **Serilog:** writes to the console only. Production uses compact JSON with the rendered message
  (CLEF: `@t`, `@m`, `@i`, and `@l` for levels above Information); other environments use plain
  text. Levels come from the `Serilog` section of `appsettings*.json`: Information by default,
  Warning for ASP.NET Core, EF Core, Wolverine, JasperFx and `System.Net.Http`. Every line carries
  `Application=SpaceTraders.API`, and, once agent bootstrap has picked the agent, `ResetDate` (slice
  2.13, see [Logging and metrics](#11-logging-and-metrics)); a line about a ship also carries its `ShipName` (slice 2.14). The host logs through its own logger and leaves Serilog's static
  `Log.Logger` alone, so test hosts running side by side don't share one (B41).
- **Wolverine** (6.x) discovers handlers in the Application assembly and keeps messages in memory:
  nothing goes to Postgres. It compiles a handler's code at runtime (`WolverineFx.RuntimeCompilation`)
  when the handler's first message comes, not at startup.
  Its generated code resolves the DbContext from the scope (`AlwaysUseServiceLocationFor`), because
  EF Core registers the DbContext's options through a factory. Anything else registered through a
  lambda (an interface resolving its concrete type, a typed HttpClient) is resolved from the scope
  too, without a warning (`ServiceLocationPolicy.AlwaysAllowed`): 5.x's warning, one per handler and
  dependency on each handler's first message, raised a `RepeatingError` anomaly on every start (B42). A crash loses the messages still in flight; after the restart,
  startup sync and startup recovery pick the ships up again, and pending arrivals wait in
  `scheduled_ship_events`. Any handler exception is retried after 250 ms, 500 ms and 1 s, then
  the message is discarded.
- **Health checks:**
  - `/health/live` runs no checks.
  - `/health/ready` checks the database.
  - `/health/startup` is Healthy when the startup chain completed, Unhealthy if it failed, and
    Degraded before or while it runs. Only Healthy answers 200; Degraded and Unhealthy answer 503,
    so Kubernetes' startup probe holds the pod back until the chain has completed (B39).
- **CORS** (`Dashboard` policy): GET only. With `WebUI:Origin` set, only that origin is allowed;
  otherwise any origin is.
- **Middleware order:**
  1. path base
  2. CORS
  3. Swagger (Development only)
  4. API key
  5. HTTP metrics
  6. health endpoints
  7. endpoint groups
  8. SignalR hub `/hubs/dashboard`
- **Metrics:** `/metrics` has a port of its own, `Metrics:Port` (9090; 0 means no metrics
  server), served by a second, minimal Kestrel server (prometheus-net's `AddMetricServer`). It
  needs no API key: the Service and the ingress don't route that port, so only something that can
  reach the pod, such as Prometheus, can scrape it (B11, fixed). `Metrics:Hostname` is `+` (all
  interfaces) unless set; Development sets `localhost`. The main port serves no `/metrics`.
- **Hosted services:** the app registers `DeferredStartupHostedService` and the metrics server.
  Every other service is a singleton that the deferred startup starts.

### The startup chain (`DeferredStartupHostedService`)

The chain starts after the HTTP server is up (`ApplicationStarted`) and awaits each step in turn:

| # | Step | Runs | Notes |
|---|---|---|---|
| 1 | Database initialisation | once | Skipped in `Testing`; see [Data](#6-data) |
| 2 | `ShipEventScheduler` | keeps running | Arrival timers. Skipped in `Testing` |
| 3 | `DataRetentionService` | at start, then daily | Prunes every table by its policy; see [Retention](#retention) |
| 4 | `AgentBootstrapService` | once | Picks or registers the agent, deletes other agents' rows |
| 5 | `DatabaseSizeGuardService` | at start, then every 5 min | See [Database size guard](#database-size-guard) |
| 6 | `RunLifecycleService` | every 60 s | Opens or resumes a run |
| 7 | `LeaderElectionService` | every 10 s | Lease `game-loop`, 30 s |
| 8 | `StartupSyncService` | once | Agent, ships, systems, markets, contracts |
| 9 | `StartupSnapshotService` | once | One JSON snapshot row, from the cache |
| 10 | `DiscoverySnapshotService` | every minute | Another snapshot when the cache lists a ship type or good no snapshot of the run held (slice 2.15) |
| 11 | `StartupRecoveryService` | once | Resumes ships; releases the contract's ships (D26) |
| 12 | `SettingsStartupLoggingService` | once | Logs every setting |
| 13 | `GameLoopService` | every 5 s | The tick |
| 14 | `PrometheusMetricsService` | every 10 s | Exports the cached state: credits, ships, contracts |
| 15 | `PrometheusMarketMetricsService` | every minute | Exports the cached markets and shipyards, and once the game's production chains (`GET market/supply-chain`, through `SupplyChainCache`, which the trading plan shares: one call per start, retried hourly after a failure) |
| 16 | `HealthMonitorService` | at start, then every minute | Evaluates the health rules; see [Health rules](#12-health-rules) |

One try/catch wraps the chain. Steps 5, 9, 10, 12 and 16 catch their own errors. A throw in steps 1, 4, 6,
8 or 11 ends the chain: startup is marked failed (`/health/startup` turns Unhealthy) and the host
stops. The process exits with code 1, so Kubernetes restarts it with back-off. Pruning starts
before any of those, so a pod that keeps failing during startup still prunes at every start.

**Agent bootstrap** (`AgentBootstrapService`):
- First it reads the server's reset date (`GET /`).
- Candidate tokens, in order:
  1. the active stored token;
  2. the configured `SpaceTraders:AgentToken`;
  3. the latest stored token;
  4. failing all of those, it registers a new agent.
- Each candidate is checked with `GET my/agent`. It is skipped if its agent symbol differs from
  `SpaceTraders:AgentName`.
- A 401 whose message contains "reset_date does not match" sets the token-mismatch flags,
  publishes `TokenResetMismatchDetectedEvent` and tries the next candidate. Any other API error
  throws.
- Registration needs `SpaceTraders:AccountToken` and `AgentName`; the faction defaults to
  `COSMIC`. It refuses (and so stops the host) when an agent with the new agent's id is stored
  already: the server must have reset after its reset date was read, and the new agent would
  share the old one's rows. The next start reads the date again.
- On success it:
  - sets the agent data scope to the agent's id, which every later database query filters on
    (see [Agent scoping](#agent-scoping));
  - seeds the settings (D69): a setting the agent doesn't have yet starts with the value chosen for
    the next runs, else with its default; a setting that still follows its default gets the default
    as the running version has it (a `SettingChanged` journal line each); see
    [How settings work](#how-settings-work);
  - records the active token.
- Then it deletes the rows of every other agent, table by table, except their `runs`. A table
  that fails is logged and left for the next start.
- This only happens at startup. A reset during a run is noticed by the API client (see
  [Outbound API client](#8-outbound-api-client-spacetradersinfrastructurespacetradersapi)): the
  host stops, and after the restart this registers the new agent and deletes the old one's rows.
  Settings are stored per agent, so the new agent's settings are seeded afresh: with the values
  chosen for the next runs (`next_run_settings`, which belongs to no agent and outlives the old
  one), else with the defaults (D69). The old values also survive in the settings snapshot of the
  old agent's runs.

**Run lifecycle** (`RunLifecycleService`):
- Opens a run, or resumes the open one, with a name, a strategy label, a settings snapshot and
  starting credits. Starting credits come from `cached_agents`, which is still empty on a fresh
  database, because sync runs later.
- Changing a setting with a strategy prefix through `PUT /settings/{key}` closes the run and opens
  a new one.
- Scheduled runs are never created.

**Leader election:** lease `game-loop` in `leader_leases`, 30 s, renewed every 10 s. Only the tick
checks it. The lease is not released on shutdown.

**Startup sync** (`StartupSyncService`):
- **Fetches:** the agent, all ships, and for each system that has a ship, the system and its
  waypoints, with their traits and modifiers, when none are cached or any cached one lacks its
  traits (B34, fixed: until then no trait was ever stored, so nothing knew what an asteroid yields).
  Waypoints cached already keep when they were last observed, which scouting reads. It also fetches market and shipyard data at waypoints where
  a ship is not in transit, and the first page of contracts (20). It stores them the way the
  other paths do: shipyards with their prices, so a purchase can read them (B28, fixed), and an
  answer without the ships for sale leaves the cached ones (B64, fixed), and
  contracts with their terms, so a restart doesn't blank the deliverables the contract plan works
  from (B31, fixed).
- **Updates** the game state of existing ship rows (nav, fuel, cargo, mounts and so on). It
  leaves their goal columns alone, so ships keep their goals across a restart.
- It has no error handling.

**Snapshots** (`GameStateSnapshots`, slice 2.15): a `startup_snapshots` row holds the game state as
the bot has cached it, as one JSON document: why it was taken (`Reason`), for a discovery what was new
and where (`Discoveries`), the agent, the ships with their goals, the contracts, and every system the
bot has a ship, market or shipyard in (the ships' systems first), with the system's cached waypoints
and every market and shipyard cached there, as last seen, each with when (`LastObservedAt`). A
shipyard lists its ship types, and the ships for sale (price, supply, frame, reactor, engine, modules,
mounts, crew) once one of our ships has been there; a market its imports, exports and exchange, and
its prices once a ship has been there. Until slice 2.15 a snapshot held only the market and shipyard
where a ship was, so the others were missing even though the cache had them. It calls no API (B35,
fixed). The cache holds less than the API returns for our own ships: no crew or mount details.
- **At startup** (`StartupSnapshotService`): one, `Reason` `Startup`, right after startup sync.
- **On a discovery** (`DiscoverySnapshotService`, every minute; asked on 2026-10-04, D73): when the
  cached shipyards list a ship type, or the cached markets a good (in their imports, exports, exchange
  or prices), that no snapshot of the run held yet, it takes one, `Reason` `Discovery`, which lists each
  new ship type and good with the shipyards or markets that list it, also in the row's `Discovered`
  column (`Ship types: SHIP_LIGHT_HAULER (X1-FJ91-A2). Goods: FAB_MATS (X1-FJ91-H59).`) and in a
  `Discovered` journal line. A type is new to the run, not to a place: a second shipyard selling a known
  type discovers nothing. It reads the cache rather than following its writers, so whatever stores a
  shipyard or market counts, and what was stored within the same minute shares one snapshot. One
  process serves one agent, so what the snapshots held is kept in memory, from the startup snapshot on;
  when that failed, from what the cache lists at the first look.
- Retention is unchanged: the agent's first snapshot and the 10 newest, whatever the reason (D73).

**Startup recovery** (`StartupRecoveryService`): skipped when `Automation.Enabled` is false. With
the contract plan on, it first releases every ship on the contract that isn't in flight, so the
first tick reconsiders it (D26, see
[Contract](#contract-contractplanservice-plus-step-3-of-the-tick)). Then, for each cached ship:

| Ship state | Recovery action |
|---|---|
| Arrival time passed, still marked in transit | Publish `ShipInTransitEvent`, then run one goal step. The arrival dead-reckoning that `GetAllAsync` applies puts such a ship in orbit at its destination, so in practice it takes the last row |
| Still in transit | Publish `ShipInTransitEvent` only |
| Docked or in orbit | Run one goal step. The ship keeps the arrival time of its last route, which startup sync stores, but it isn't in transit, so it gets no `ShipInTransitEvent` (B38, fixed: until then every ship that had ever flown, and a new agent's ships, got one on every start, which wrote an "in transit" activity row) |

It doesn't reschedule arrivals. Pending arrivals survive a restart only through
`scheduled_ship_events`. Before the ships' steps it tells the name book the fleet (slice 2.14), so their
lines carry the ships' names from the start.

---

## 2. The tick (`GameLoopService`)

- Runs 5 s after the previous tick ends, on the leader only.
- Each tick:
  1. `EnsureBootstrappedAsync` for each plan that is switched on, in this order: Scout, Explore, Roles (the
     role board), Contract, ProbeDeployment, Survey, Mining, Siphon, Construction, Trading and SpareTime.
  2. One goal step for every cached ship (`ShipGoalExecutorService.ExecuteAsync`).
  3. If the contract plan is switched on, for every active `Contract` assignment:
     `FulfillContractDeliveryCommand` once the ship holds a whole trip (what the contract still
     needs, at most a full hold; with nothing left to deliver, that command fulfils the contract),
     otherwise `MineResourceVolumeCommand` (B8, fixed), with the best survey for the contract's ore
     when there is one (slice 6.4). Every miner on the contract has such an assignment (D23).
  4. Refresh one market where a ship is, when one is due (the market watch, below): last, because
     a refresh is a read, which can go later without loss, while moves and trades can't (D19).
  5. Publish `ApiUnavailableEvent` or `ApiAvailableEvent` when availability changed, and log
     it (`ApiUnavailable`, `ApiAvailable`). Nothing handles the events.

  With `Automation.Enabled` off, or while API calls are paused after a 502, it skips steps 1
  to 4.
- Each plan bootstrap, each ship's goal step, each contract assignment and the market watch runs
  in its own DI scope and try/catch. A failure is logged at Error with the plan or the ship, and the rest of the
  tick carries on. Its own scope means a step can't leave a broken DbContext to the steps after
  it.
- A failure outside those steps (reading the switches, listing the ships or assignments) ends
  the tick; the next one starts 5 s later.

### Market watch (`MarketWatchService`)

Asked for in slice 6.5: any market that has one of our ships at its waypoint refreshes once every
`Market.RefreshMinutes` (5; 0 switches it off).

- The API shows a market's prices only while a ship of ours is there, so the watch fetches only those
  markets: each waypoint with a market where a ship is docked or in orbit, once however many ships
  are there.
- A market is due once that many minutes have passed since its prices were last seen, by the watch,
  by an arrival or after a trade there (D25), so a ship that has just arrived or traded adds no call.
- Each refresh goes through `MarketRefresher`, which the trade and mining executors use too: after
  each purchase and sale they fetch the market again while the ship is still docked there (D25).
- One market a tick, the one that has waited longest (D19): twelve ticks a minute refresh 25 markets
  in about two minutes, and the watch never holds the next tick up with a run of reads. A market the
  watch asked for waits an interval even when the answer failed or had no prices, so it can't stay
  the most overdue (`MarketWatchAttempts`, in memory).
- Each refresh stores the market and publishes `MarketDataRefreshedEvent`, as an arrival does, so the
  price history records it. An answer without prices (no ship there after all) is not stored: it
  would wipe the cached prices. The arrival and startup sync keep to that too (B62).
- A market cached without prices counts as never seen (B62): `GetLastObservedAtAsync` answers none for
  it, so the watch fetches it as soon as one of our ships is there, and the probe plan sends a probe
  there first.
- An arrival and the watch can fetch one market at the same moment: the watch counts a ship as there
  once its arrival time has passed, while the arrival may still be fetching, and a market never
  fetched is due at once. Both store their answer in the market's one row, and the later one wins
  (B61).
- A market that fails is logged at Warning and tried again an interval later. Otherwise the watch
  logs at Debug.
- A probe parked at a market keeps it current. The probe plan (slice 6.3) parks one at every market
  once there are enough; until then its probes roam, and with the plan off only the starting probe
  and wherever the other ships are keep markets current.

---

## 3. Plans

All plan state is JSON in `plan_states`, one row per plan type.

Each plan has a switch, `Automation.Plan.{Plan}.Enabled` (`AutomationSwitches`). Per D9 only
Scout and Contract are on by default; the survey plan (slice 6.4), the siphon plan (slice 6.7), the
spare-time plan (slice 6.8), the role board (slice 6.9), the explore plan (slice 6.11) and the construction plan
(slice 6.6) are off too until switched on. A plan that is switched off:
- isn't bootstrapped, so it doesn't buy anything;
- doesn't move its ships: `ShipGoalExecutorService` skips the goals it gives (scout, jump and explore-system, probe,
  survey and the survey ship's move, D54, mine-and-sell, siphon-and-sell, construction, trade, gather-and-sell). They
  resume when it is switched back on; the explore plan's command ship keeps its explore assignment meanwhile, so no other
  plan takes it (switched off away from home, it stays where it is);
- for the contract plan: the tick's contract work (step 3) is skipped too;
- for the role board, which gives no goals: the plans give work by the fixed rules below, whatever roles its
  state still holds.

The plans are bootstrapped in the order of the table, so a plan higher up claims the ships it wants
first. Ships are bought in an order of their own, whichever plan buys them (slice 6.10b, D43): see
[The order ships are bought in](#the-order-ships-are-bought-in-purchaseorder-slice-610b). Which plan a ship works for follows from what it can do (`FleetRoles`, slice 6.4): with the
survey plan on, a ship that can survey surveys, and only that (D20), unless the spare-time plan is on
too: then the command ship, with nothing to survey, trades (D34), and with no trade either mines or
siphons in its spare time (slice 6.8); a ship that can mine mines (the contract first, D23, then the
mining plan's trips); a ship that can siphon, and can neither mine nor survey, siphons (slice 6.7);
trading takes what the others leave free, and the spare-time plan what trading leaves. With the construction
plan on (slice 6.6), the ship with the largest hold builds the home system's jump gate while it needs materials,
and trades when it has no load it may buy. With the role
board on (slice 6.9), which plan a ship works for follows from the role the board gives it instead, by what
it and the other ships can do and what each role would earn: see
[Role board](#role-board-roleplanservice-slice-69).

| Plan | Purpose | Ships it uses | Statuses | Buys |
|---|---|---|---|---|
| Scout | Visit every marketplace in the starting system once | The one ship with fuel | Active → Completed | Nothing |
| Explore | Jump through active gates to every system not explored yet, scout each one's markets and shipyards once, and come home (slice 6.11, D59–D63) | The command ship, once its trip ends | What it knows of each system and gate; the command ship Waiting, Exploring, Returning or Done | Nothing; each jump buys one ANTIMATTER |
| Roles | Give every ship the role that earns the fleet most per hour: surveys first, the contract next, a drone per scarce mineral and area, the rest by an assignment (slice 6.9, D38–D42, D48, D53) | Every ship but the probes; it gives no goals: the plans below read the roles | Each ship's role, why, and what each role it could take would earn it | Nothing; the drones the mining and siphon plans buy beyond one per scarce mineral and area must be worth their role |
| Contract | Fulfil one mineral contract | Every free miner (D23) | PendingBudget, Active, DeferredUnsupported, Completed | One `SHIP_MINING_DRONE`, first in the order (D43) |
| ProbeDeployment | A probe at every market of the HQ system; until then the probes roam between markets, the stalest nearby first (slice 6.3, D29); a purchase where none of our ships is fetches a probe (D30) | Probes | Markets with their probe, the next probe's price, open calls | `SHIP_PROBE`, while there are fewer probes than markets, after the cargo ships of the list (D43) |
| Survey | Survey the contract's ore, else ores the markets buy (slice 6.4) | Ships that can survey (D20) | Targets, best first | A `SHIP_SURVEYOR` for each system with mining drones, with the role board on (D47); then one more for each further area with mining drones, after the drones per scarce mineral (D55) |
| Mining | Mine surveyed ores, else ores in low supply, and sell them (slice 6.4), never for a market that has the ore ABUNDANT; a drone shares a pair rather than trade, until every ore is ABUNDANT (D77) | Free miners | Low-supply openings (Pending/Assigned) | `SHIP_MINING_DRONE`: one per scarce ore and area (D48, D53), then in turn with the cargo ships (D43); up to `Mining.MaxDrones` |
| Siphon | Siphon gases in low supply at gas giants, keep every gas, and sell them (slice 6.7), never for a market that has the gas ABUNDANT; a drone shares a pair rather than trade, until every gas is ABUNDANT (D77) | Free siphoners: a gas siphon, a hold and a tank, nothing to mine or survey with | Low-supply openings (Pending/Assigned) | `SHIP_SIPHON_DRONE`: one per scarce gas and area (D48, D53), then in turn with the cargo ships (D43); up to `Siphon.MaxDrones` (D32) |
| Construction | Build the home system's jump gate: buy its materials a full hold at a time and supply them (slice 6.6, D64–D68) | The ship with the construction role: the largest hold that isn't a drone or the surveyor (D65); any free ship that holds what the gate needs | The gate's materials (required, fulfilled, on their way), the builders, why no load was bought | No ship: the gate's next load of materials, after the cargo ships in the order (D64), above the credit reserve |
| Trading | Carry goods between markets for the most profit after fuel | Ships with a cargo hold and a fuel tank that the plans above leave free | Held and open routes (Assigned/Pending) | Cargo ships, `Trade.ShipPurchases` (D21), then one more of the list's last type in turn with the drones (D43) |
| SpareTime | Keep the command ship busy when it has nothing to survey or trade: mine or siphon whatever sells at the nearest place it can, and sell it (slice 6.8, D34–D37) | Surveyors with a mining laser or a gas siphon, a hold and a tank (the command ship), while the survey plan is on | Each such ship and what it does (Gathering, Selling, Busy, Waiting) | Nothing |

### Scout (`ScoutAllMarketplacesPlanService`)

1. **Picks** the single ship with fuel capacity and fuel above zero. It throws if there are zero
   or several, which stops the tick (B14).
2. **Targets** the cached waypoints with a market in that ship's system.
3. **Routes** by nearest neighbour, starting at the ship's waypoint if it is a market, otherwise
   at the alphabetically first market.
4. **For each stop** it writes a `Scout` assignment and a `ScoutWaypointGoal`. The plan advances
   when the ship is docked at the stop, once: the tick and an arrival can both run the ship's goal
   step as it docks, and only a visit of the plan's current stop moves the plan on (B45, fixed).
   Advancing writes the plan before the assignment.
5. **On every tick** while the plan is active, it re-creates the current stop's assignment and goal
   if the ship has no open assignment for it, as after a restart. It reads the assignment before
   the plan, so an advance under way never looks like a missing assignment (B45).
6. **After the last stop** the plan is Completed and the ship's goal is cleared, so the ship is
   free for other work (B10, fixed). A scout goal that outlived its plan (a database from before
   the fix) is cleared on its next step.

Markets are not scouted again. Other systems are the explore plan's (below).

### Explore (`ExplorePlanService`, slice 6.11)

Asked on 2026-10-04: "if an active jump gate goes to a system that isn't explored yet, the COMMAND ship should go through
that jump gate. If there are markets or shipyard there, the COMMAND ship should scout them, as it initially does for the
home system, recursively." Off by default (`Automation.Plan.Explore.Enabled`); your decisions are D59–D63.

- **What it knows** (`ExplorePlanState`, `plan_states` row `Explore`): every system it has seen, with its gate's waypoint,
  whether the gate is built (`Active`), still `UnderConstruction`, `None` or `Unknown`, the gates it connects to (asked
  for each explored system, `GET …/jump-gate`), when the API last refused a jump there, and when it was explored. Home
  counts as explored (the scout plan's). It learns one thing a pass (D19): home's gate (fresh from the API: the cache
  keeps what startup sync saw when the agent started), then each explored system's connections, then each connected
  gate (`GET …/waypoints/{gate}`); a gate under construction, or a jump the API refused, is looked at again hourly, a look
  that failed after 5 minutes. A system the command ship has just jumped into has its system and all its waypoints
  fetched at once (`cached_systems`, `cached_waypoints`), as startup sync stores them.
- **The command ship** (cached type `COMMAND`): the plan takes it when it is free (its trip has ended, D61: no goal, no
  assignment, not in transit) and a system is left to explore, with an `Explore` assignment, so no other plan takes it.
  It bootstraps before the role board and every plan after it. While the scout plan or the contract has the ship, it
  waits.
- **Where it goes** (`ExploreAtlas`): the nearest system not explored yet by jumps through built gates (a jump needs the
  gates at both ends built), then by symbol; no limit (D59). While a look that could change the choice is still to come,
  it waits a pass. With nothing left it jumps home (D60), and at home its assignment ends: the other plans give it work
  again. Away with no built way home, it waits (`no_way_home`, journaled once, Warning).
- **A jump** (`JumpGoal`): fly to the system's gate, fill the tank when docked at a gate that sells fuel, orbit, and jump
  (`POST my/ships/{ship}/jump` with the destination gate's `waypointSymbol`), once the cooldown is over. Each jump buys one
  ANTIMATTER at the gate's market, booked as `AntimatterPurchase`. The plan gives a jump only while the credits after the
  antimatter (its last price at that gate) stay at or above `FleetExpansion.MinCreditReserve` (60,000, D63); until then it
  holds the jump (`waiting_for_credits`, journaled once): a free command ship keeps its other work, an exploring one waits
  where it is. A jump the API refuses (a client error, `JumpRefusedException`) blocks the goal with `jump_refused`; the
  plan then leaves that gate alone for an hour and chooses again.
- **Scouting a system** (`ExploreSystemGoal`): its markets and shipyards with nothing cached yet, nearest first from the
  gate, each visited once, "as it initially does for the home system"; the arrival stores the market and the
  shipyard, and a stop it didn't store (the gate it jumped to) is fetched there. It charts nothing (D62): uncharted
  waypoints keep their traits hidden. A system with neither is explored as soon as the ship is there. When the goal ends
  the system is explored (`SystemExplored`), whatever a fetch that failed missed, so the ship doesn't go back for it.
- **Business stays home** (D60, `BusinessSystems`): the systems where the plans do business are those where a ship that
  doesn't explore is. The mining, siphon and trading plans buy ships only there, the contract plan looks for its drone's
  shipyard only there, and the mining, siphon and survey plans plan no work in a system because the explorer is in it.
- **What it costs:** API reads while it learns (one a pass, a few at each new system: about 85 waypoints are five pages),
  the jumps' antimatter, and the command ship's time. The cache grows by a system's waypoints, markets and shipyards for
  each system explored; an agent reset clears it.

### Role board (`RolePlanService`, slice 6.9)

Asked for on 2026-10-02: "Each ship should have a set of potential roles. … A ship should occasionally
consider whether it's role is still the best thing it can do. This is not only based on it's own potential
roles but also of other ships." Off by default (`Automation.Plan.Roles.Enabled`); your decisions are
D38–D42. Bootstrapped after the scout plan and before the plans whose ships it gives roles, it gives no
goals: it decides which plan each ship works for, and the plans read that (`FleetRoleBoard`).

- **Potential roles** (`FleetRoles.PotentialRoles`), by what a ship carries: survey with a surveyor mount
  (or a bought `SHIP_SURVEYOR`, whose mounts aren't recorded yet); mine with a mining laser, a hold and a
  tank; siphon with a gas siphon, a hold and a tank; trade with a hold and a tank; construct (build the jump
  gate, slice 6.6) with a hold and a tank, unless it is a drone (`FleetRoles.CanConstruct`). A probe has none:
  the probe plan flies it. A role counts only while its plan is on; mining also while the contract wants ore,
  and constructing only in a system whose jump gate needs materials, as the construction cache has it: the
  home system (D68).
- **Who takes which role** (`RolePlanner`, no I/O), in order:
  1. a ship with one role takes it (`only_role`): a hauler trades, a survey ship surveys;
  2. surveys come first (D38): in a system with a ship that can only survey, that ship surveys and no
     other does. Otherwise, while another ship there can mine (surveys are for miners), the ship that can
     survey with the least to lose surveys (`survey_first`): the one whose best other trip earns least per
     hour. The ship that surveys now keeps it unless another would lose less by more than the head start.
     In X1-DC53 that is the command ship, the only ship that can survey, once a drone can mine;
  3. while the contract plan is on and its contract still wants ore (D40, D23 kept), every other ship that
     can mine mines for it (`contract`);
  4. one drone per SCARCE or LIMITED mineral and area keeps gathering it (`coverage`, slice 6.10b, D48, D53): for
     every ore a mining drone, and every gas a siphon drone, could serve a market short of
     (`MiningPlanner.ScarceOres`, `SiphonPlanner.ScarceGases`), in each area, the markets short of it that the
     drones fly between in CRUISE (`MiningPlanner.Areas`, with the smallest tank among the drones of the kind;
     in X1-DC53 the middle and B7), the drone whose trip covers it, else one that has the role, else the one
     whose best trip in another role earns least; the minerals the fewest drones could serve first. A ship
     that can survey is no drone. Without that, a drone that earns more trading would leave its mineral,
     and the plan would buy the next, as one drone per scarce mineral and area is bought whatever trading
     pays;
  5. every other drone gathers too (`gathers_first`, D58): "Mining drones should be mining drones first, and
     traders second, and they should not leave gaps when trading in a way that results in endless drones being
     bought." A drone (it can mine or siphon, and trade, and nothing else: no surveyor) takes its gathering role
     whatever trading would pay, and trades only when its plan has no trip for it: since D77, once nothing it can
     gather is below ABUNDANT, as it shares a pair before that (see [Mining](#mining-miningautomationservice-slice-64)).
     Moved to trading for profit, drones left the ores they had mined short, and the plans bought drones for them (on
     2026-10-03 the board moved SPECTER-3 between mining and trading three times in 30 minutes);
  6. while the home system's jump gate needs materials, the ship with the largest hold there of those left
     that can construct builds it (`construction`, slice 6.6, D65), as many as `Construction.Ships` (1):
     supplying pays nothing, so no estimate could choose it, and finishing the gate comes first. Drones and the
     ship that surveys are decided by then. A light hauler (80) builds before a light shuttle (40); of two equal
     holds the one that builds now keeps it, else the one that can do least else (the shuttle before the command
     ship). The construction plan gives the builder work (see
     [Construction](#construction-constructionplanservice-slice-66)), and the trading plan when that has nothing
     it may buy. A ship whose one role is constructing and isn't chosen gets `None`;
  7. the rest share the work for the most credits per hour across the fleet (`most_profitable`): each ship
     takes one trip or none, no two the same good bought at the same market (D18, D80, B67) or the same mining or
     siphon opening, by the
     assignment that earns most in total (the Hungarian method, `Assignment`). A ship's current role counts
     `Roles.HeadStartPercent` (20) more (D41), so close calls don't flip back and forth. A ship left
     without a trip keeps its role (`no_work`), or takes its first role but surveying and constructing.

  A ship with no role whose plan is on gets `None` (`no_role`), and no plan gives it work.
- **What a role earns** (`RoleEstimator`): the trips its plan would offer the ship, the best 20 per role (one per job,
  its best), each valued per hour:
  - trade: every lucrative route from where the ship is, or is going, that the trading plan could give it, its profit
    after fuel, as the trading plan reckons it (D14): none another ship's trip holds, nor of a good another trip is on
    its way to buy at that market (D80), and the routes of one good from one market are one job (B67: on 2026-10-05 the
    board credited SPECTER-1 and SPECTER-2B each with MEDICINE bought at D48 while SPECTER-2A took it). No trade trip
    counts for more an hour than the trade trips that ended in the last two hours made per hour of their time
    (`TradeEarnings`, D87, asked on 2026-10-05, "Cap at realized", when the board put SPECTER-1's trading at 0.14 to
    1.77 million an hour, one trip's profit over its flights, while its trades made about 83,000): such a trip says
    "at most what trading earned lately (D87)". In memory: after a start nothing caps it until a trade trip ends;
  - mine: every mining target (D28's; none for a market that has the ore ABUNDANT, D77), a full hold of the
    target ore, filled at the ship's rate times the ore's share of the extractions (its survey's deposits, or one
    of the asteroid's ores without one), less the fuel there and on to the market;
  - siphon: every siphon target (none for a market that has the gas ABUNDANT, D77), a full hold of the gases a
    market buys (a trip keeps them all, D33): the trip's own gas at the market it sells it to, each other gas where
    it counts most among the markets the ship can carry it to from the gas giant, less the fuel;
  - each with the production chains' share (D39, `ChainValues`): a good sold to a market that makes a
    pricier good from it (it imports the good and exports something made from it, by the game's production
    chains, as D15 reads them) counts `Roles.ChainValueSharePercent` (50) of the price difference, and that
    share again of the next step, at the market in the system that makes the most of the pricier good in
    turn (iron ore → iron → machinery). A step counts fully while its market is SCARCE of the input, three
    quarters at LIMITED, half at MODERATE, a quarter at HIGH, and not at all at ABUNDANT. A market that only
    exchanges the good adds nothing. The share counts at most what the trip earns on a unit (D49,
    `ChainValues.PerUnitAtMost`): a mined or siphoned unit's price at its market, a traded unit's margin; so
    feeding a factory at most doubles a trip. Each step's price difference counts for every unit of input,
    while nobody knows how many units of input make one of output, or how fast: uncapped, the second step
    (PLASTICS → EQUIPMENT, ~3,200) valued a siphon drone at ~274,000 credits an hour that earned 7–10k. Only
    the comparison of roles reads it: within a role the plans still choose by D15 and D28;
  - a trip's time is its flights in CRUISE as the API reckons them (15 seconds plus the distance times 25
    over the engine's speed, 9 for an engine not cached yet), 10 seconds a landing, and for mining and
    siphoning the cooldowns to fill the hold, with half a tick after each. A mining or siphon trip to a market
    out of the ship's CRUISE reach (slice 6.10c, D45) adds its drift there, ten times as long (the distance
    times 250 over the speed), and the unit of fuel bought back where it lands: about 2.5 hours from the middle
    of X1-DC53 to B7 for a drone, so such a trip rarely wins on its own per hour; the drone kept for a far
    scarce mineral (`coverage`, above, one per area) takes it whatever it earns. The flight itself takes the
    fastest way (slice 6.19, D84), which often cruises part of it, so the estimate errs long.

  Surveying and constructing have no estimate: they come first.
- **Rates** (`GatheringRates`, in memory): each extraction (for the contract, the mining plan or in spare
  time) and each siphon records its yield and cooldown. A ship's rate is the average of its last 10, else
  of the other ships' of the kind, else 3 units every 70 seconds. The state keeps each ship's rate, which
  the first evaluation after a restart takes back.
- **When** it weighs the roles again (`RoleBoardMemory`, in memory): at the first pass after a start; every
  `Roles.ReconsiderMinutes` (10, D41); at once when a ship joins the fleet, a plan is switched on or off, the
  contract starts or stops wanting ore, or the home gate starts or stops needing materials (slice 6.6, so a
  complete gate frees its builder at once); and at once, at most once a minute, when a ship with a choice of
  roles has had no work for a minute, but for a ship that surveys, which waits for surveys by design.
  Otherwise a pass reads the ships and their goals, and stops.
- **A new role takes effect when the ship's trip ends:** the plans give work only to free ships. A ship
  bought since the last evaluation has no role yet: the next pass, a tick later, gives it one.
- **With the board on, the plans give work by role** (see
  [Which ships a plan considers free](#which-ships-a-plan-considers-free)):
  - the survey plan to the ships with the survey role; with the spare-time plan on too, such a ship trades
    or gathers when it has nothing to survey (D34);
  - the contract plan to every ship that can mine but those that survey, whatever its role, so the contract
    never waits for the next evaluation (D40);
  - the mining plan to the ships with the mining role, the siphon plan to those with the siphon role; each
    trades when its plan has no trip for it, as before;
  - the construction plan to the ships with the construction role (slice 6.6);
  - the mining plan's collecting rounds to the ships with the collecting role (slice 6.18, D83): a shuttle the mining
    plan designated for a collection point has that role (reason `collection`) once one of the point's drones is parked
    at the asteroid, whatever else it could do, and does nothing else; until then it is a cargo ship like any other, and
    trades (D86). Like a surveyor, a collector without work waits by design: it doesn't make the board weigh the roles
    again, and a change in the collecting shuttles does;
  - the trading plan to the ships with the trade role, and those with the mining, siphon or construction role
    that their plan left free;
  - the mining and siphon plans buy a drone beyond one per scarce mineral and area only when the board would give it
    their role (`RoleAdvisor`): a drone that would earn more trading would trade, and the plan would buy the
    next for the same opening. Since D58 a drone always gathers first, so the board always would.
- **Off** (the default), the fixed rules hold, as before slice 6.9 (D20, D34).
- **The state** (`plan_states`, `Roles`), written at each evaluation: when it ran, the plans that were on and
  whether the contract wanted ore, and per ship its role, why, since when, the trip that decided it and what
  that earns per hour, what each role it could take would earn it (its best trip), and the rates it used.
  Probes are left out.
- **Visibility:** each change is a `RoleChanged` journal line. The metrics `spacetraders_ship_role_info` and
  `spacetraders_ship_role_credits_per_hour`, exported while the board is on, feed the SpaceTraders
  dashboard's **Roles** table (gembernodes).

### Contract (`ContractPlanService`, plus step 3 of the tick)

- **While the plan is Active,** bootstrap advances it from the cached contract, which every delivery
  updates: it records the units delivered and keeps the remaining units of every ship's contract
  assignment current. Then every free miner joins (D23, slice 6.4), the plan's first ship among
  them: it gets a `Contract` assignment for one round trip (D26) and logs `MiningStarted` (reason
  `contract`). A free miner has no goal (or a finished or blocked one), no assignment, isn't in
  transit and, with the survey plan on, can't survey (D20); with the role board on, it can mine and doesn't
  have the survey role, whatever its role (D40). Several ships may bring more than the
  contract still needs; the mining plan sells what is left over. It writes only what changed. A plan
  in any other status except PendingBudget makes bootstrap return immediately: one contract per
  reset (D1).
- **Otherwise it:**
  1. refreshes contracts from the API, ignoring errors, but only while there is no plan yet;
  2. negotiates a contract if none is open, using the first ship that has a waypoint;
  3. takes the unfulfilled contract with the earliest deadline;
  4. accepts it, records the acceptance payment in the cached credits (B33, fixed) and publishes
     `ContractAcceptedEvent` with it, for the ledger (B7, fixed);
  5. parks a non-mineral deliverable as DeferredUnsupported (D2);
  6. picks an idle miner (no goal, no assignment, not a surveyor while the survey plan is on; with the
     role board on, not a ship with the survey role), or buys a drone. If neither works, the plan becomes
     PendingBudget and is retried every tick. A retry works from the cached contract and only
     tries the ship again: it calls the API only to buy, and stores nothing while the plan keeps
     waiting (B27, fixed);
  7. picks the nearest asteroid;
  8. saves an Active plan and a `Contract` assignment.
- **The work itself is step 3 of the tick:**
  - **Mining:** `MineResourceVolumeCommand` travels to the asteroid as a goal's flight does (`CommandFlight`, D84: in
    BURN where that strands nothing, otherwise CRUISE), through refuelling stops when it is beyond one tank (B47,
    under Commands below), extracts once per cooldown,
    with the best survey there for the contract's ore when there is one (slice 6.4), and
    jettisons other goods: unlike a mining plan's trip (D71), it keeps none, so its hold fills with
    the contract's ore only.
  - **Delivery:** `FulfillContractDeliveryCommand` travels to the destination the same way, docks, delivers
    what it holds but at most what the contract still needs (B30, fixed), and calls fulfil once
    nothing is pending and the cached contract isn't fulfilled yet (another ship may have done it,
    D23), recording the payment in the cached credits and publishing `ContractFulfilledEvent` with
    it.
  - **The trip ends there** (D26): the delivery closes the ship's assignment, and on the next tick
    the plans assign the ship again, in their order, so work that matters more comes first: a miner
    rejoins the contract, while the command ship surveys once the survey plan is on. A ship whose
    fulfil call failed keeps its assignment and makes the call again. A ship keeps its cargo when it
    is released.
  - **At a restart** (`StartupRecoveryService`, with automation and the contract plan on), every
    ship on the contract that isn't in flight is released at once (D26). A ship in flight keeps its
    assignment until its delivery: without one, nothing would record its arrival.
- **Completion:** once the contract is fulfilled, bootstrap completes the plan and closes every
  contract assignment, which releases the ships (B9, fixed). A ship that still holds the ore sells
  it through the mining plan. Every unit delivered isn't enough: until the fulfil call has gone out,
  the assignment stays open with 0 units left, so the ship goes back to make it.

### Probe deployment (`ProbeDeploymentPlanService`, slice 6.3)

The goal is a probe at every market of the HQ system, where the market watch keeps the prices fresh
(D29). Each pass:
- **Probes** are the ships with a probe frame, or cached as `SHIP_PROBE` (bought) or `SATELLITE`
  (startup sync stores a ship's *registration role* as its type), so the starting probe is one (B25).
  No other plan uses them.
- **Buying** (D29): while the system has fewer probes than markets, it buys a `SHIP_PROBE` at the
  shipyard that sells it for the least, at most one a pass, through `ShipPurchaseService`: the
  purchase must leave the credit reserve (D51), and needs one of our ships at the shipyard (D30).
  Probes in flight count (B15). The contract's drone, a surveyor, a drone per scarce mineral and area, a surveyor per area, and the
  cargo ships of `Trade.ShipPurchases` come first (D43): while one of them waits, the probe waits
  too (`Purchase` `WaitingForAnotherPurchase`), and the probes fly on.
- **Flights** (`ProbePlanner`, no I/O):
  1. a shipyard where a purchase waits for one of our ships (`ShipyardCalls`, D30) gets the nearest
     free probe (`ProbeCalled`), which stays there while the call is open;
  2. every other free probe gets a market that is due, its prices older than `Market.RefreshMinutes`
     (5), with no probe at it or on its way. Each pair of free probe and due market is scored by the
     market's age minus twice the flight there in CRUISE (15 s plus the distance times 25 over the
     engine's speed, 9 for a probe), and the best pair goes first. A market never seen is the oldest,
     and so is a market the cache holds without prices (B62).
  With a probe at every market, none is due without one, and the probes stay where they are.
- **The flight** is a `DeployProbeGoal`: one per flight, ended at the arrival, which fetches the
  market and shipyard there. The plan then chooses again.
- **State** (`plan_states`, written only when it changes): every market with the probe at it or on
  its way, whether another ship of ours is at it, and for a market nobody watches when it is due; the
  next probe's shipyard and price and why it isn't bought (`Purchase`); the open calls.
- **Journal:** `PlanStarted` once, `PlanBlocked` (`waiting_for_credits`) when it starts waiting for
  credits, `ProbeCalled`; the purchase logs `ShipPurchased`.

### Survey (`SurveyPlanService`, slice 6.4)

- **A surveyor** is any ship with a surveyor mount; with this plan on it surveys, and nothing else
  (D20): the command ship's mining laser stays unused, unless the spare-time plan is on (slice 6.8):
  then, with nothing to survey, the command ship trades, or mines and siphons (D34). With the role board
  on (slice 6.9), only the ships with the survey role survey: every ship that can only survey, else one per
  system, the one with the least to lose, while another ship there can mine (D38).
- **Each tick** it first ends the surveys that expired (`SurveyKeeper`: removed from `cached_surveys`,
  `SurveyEnded` journaled with how often each was used, `spacetraders_surveys_ended_total`). Then
  each free surveyor gets one survey to take (`SurveyWaypointGoal`), the best target it can reach that
  no other surveyor works on (or the best one when all are taken), from `MiningPlanner.SurveyTargets`. It
  reaches a target when it gets there in CRUISE and, with the fuel left, on to a market that sells fuel
  (`MiningPlanner.CanSurveyAt`, B58), as a mining trip must get on to its market; a ship that can only survey also
  reaches a collection point's asteroid it gets to (slice 6.18, D83), as it stays parked there, as the drones do. Drones
  parked at a collection point count in the areas by their point's market, so the area gets a survey ship of its own
  (D55), which moves to that market (D54) and on to the asteroid; each collection point's ores at its asteroid are targets
  for its market. A survey ship at a collection point's asteroid isn't moved again (B68): from there, as far as its fuel
  cruises, its own area may hold none of the point's drones, which count by the market.
  The targets are:
  1. the contract's ore at the contract's asteroid, while the contract plan mines it;
  2. each ore a market in the system buys, at the asteroid nearest each market that buys it (D27,
     refined: surveys close to wherever the ore is sold), among those whose traits yield the ore
     (`AsteroidDeposits`) and where one of the miners could mine it for that market: a trip in CRUISE
     from where the miner is, to the asteroid and on to the market with the fuel left, as the mining plan
     reckons it (any asteroid while there are no miners). A drone still drifting to a far market (slice
     6.10c) counts once it is there (B54). One target per ore and asteroid, for the market that pays most
     of those it is nearest; each keeps its own stock.

  A target needs a survey while it has fewer usable surveys holding its ore than
  `Survey.StockPerOre` (2, D27). Among those, the contract's ore comes first, then the ore with the
  fewest usable surveys, then the best paid. With the stock for every ore, the surveyor waits until
  a survey expires or is used up, or, with the spare-time plan on, mines or siphons in the meantime
  (slice 6.8). A ship that can only survey (`FleetRoles.CanOnlySurvey`: a bought `SHIP_SURVEYOR`) surveys on
  instead (D52): of the targets it reaches, the one with the fewest usable surveys, then the contract's, then
  the best paid.
- **A ship that can only survey works where most drones mine** (D54, `MiningPlanner.TryFindBusierArea`):
  before it gets a survey, the plan counts the mining drones by where they work (the market their trip sells
  at, a drone still drifting there included; between trips, where they are). Its own area is what it reaches
  in CRUISE; the drones beyond that group into areas as it would fly between them. When an area has more drones
  than its own (a tie keeps it where it is), it gets a `MoveToWaypointGoal` with `Drifting` instead of a survey, unless it
  is at a collection point's asteroid, where it stays parked (B68):
  to the market of that area where the most drones work, among those that sell fuel, and logs "moves to …
  (D54)". It goes there the fastest way (D45, D84: it cruises as far as it can and drifts the rest, 1 fuel whatever
  the distance; drifting all the way took about 2.5 hours from the middle of X1-DC53 to B7), and surveys from
  there. A survey lasts 10 to 55 minutes, so one survey ship serves one area at a time; the command ship, which
  can do more, never moves for this. Each area with drones gets a survey ship of its own (D55): an area where
  another ship that can only survey works, or is moving to, is taken; of two in one area, the one free first
  moves to the busiest area with drones that has none, whatever its own area has.
- **A spare-time trip that fills its hold** counts as free: a survey that needs taking takes the ship off it
  at once, with its hold aboard (D37), when that is safe (`SpareTimeInterruption`, see
  [Spare time](#spare-time-sparetimeplanservice-slice-68)), and logs `GatheringInterrupted` (`Reason`
  `survey`). A survey isn't ore-specific: the API surveys the whole asteroid and
  returns random deposits, so "for" an ore is the plan's label, and one survey often counts for
  several ores. Within the drones' reach every target is XB5C.
- **A designated surveyor** (slice 6.10b, D47): with the role board on, the plan buys a `SHIP_SURVEYOR`
  (33,905 at H52 on 2026-10-03) for each system with a mining drone and no ship that can only survey, at
  the system's shipyard that sells it for the least, second in the order ships are bought in (D43). The
  board gives it the survey role, as a ship that can only survey (D38), which frees the command ship for
  what pays it most. A bought surveyor counts by its type until startup sync records its mount. With the
  board off the command ship surveys (D20) and no surveyor is bought. **One per area** (D55, asked on
  2026-10-03: "extra surveyor drones are bought to try and cover all areas with surveys"): while a system has
  fewer ships that can only survey than areas with mining drones (`MiningPlanner.CountAreas`, the drones
  where they work, grouped as the survey ships fly), it buys one more, `SurveyorPerArea` in the order, after
  the drones per scarce mineral and before the cargo ships. In X1-DC53 that is one for the middle and one for
  B7; the new one, bought at H52, drifts to B7 when the first is in the middle.
- **The state** (`plan_states`, `Survey`) lists the targets, best first, with how many usable surveys
  each has, whether it needs one, the surveyors on each, and the surveyors that can reach each in CRUISE
  and fly on from it to fuel (`CandidateShipSymbols`, B58); it is written only when it changes. The `ShipLeftIdle` rule reads it: only
  targets that need a survey are work waiting, and only for the surveyors that can reach them (B55). A
  designated surveyor's 80-unit tank keeps it in the middle of X1-DC53, while a miner's targets can be far
  out: the command ship's, when it mines, or a drone's at a market it drifted to.

**What an asteroid yields** (`AsteroidDeposits`): the game doesn't publish it, so this is the table
community bots use, from the trait descriptions and what extractions have shown: common metal
deposits yield aluminum, copper and iron ore, ice water, quartz sand and silicon crystals (as XB5C did
on 2026-10-02); mineral deposits silicon, quartz, ammonia ice, ice water, iron ore and precious stones;
precious metal deposits platinum, gold and silver ore and five common ores; rare metal deposits
uranite and meritium ore; frozen and ice crystals ice water and ammonia ice. Only ASTEROID,
ASTEROID_FIELD and ENGINEERED_ASTEROID waypoints can be mined. A survey shows what is really there.

### Mining (`MiningAutomationService`, slice 6.4)

- **A miner** is a ship with a mining laser, a hold and a tank that doesn't survey (D20); with the role
  board on (slice 6.9), a ship with the mining role. The contract plan, bootstrapped earlier, has taken the
  free miners it wants (D23).
- **Each tick** every free miner gets one trip (`MineAndSellGoal`); the trip ends when it is sold,
  and the plan chooses the next one:
  1. a miner that holds ore a market buys sells it first, one good a trip, where each fetches most after fuel
     (reason `held_cargo`, `TradeRoutePlanner.TryFindBestCargoSale`): ore left over from the contract, and the other
     ores a trip keeps (D71, below). A full hold only sells, even where the sale doesn't pay for its fuel: a mining trip
     would turn to selling at once and end without its ore aboard, on every tick. A full hold no market it can reach
     buys gets no trip, so the trading plan jettisons it (D42);
  2. otherwise the best of `MiningPlanner.MiningTargets`: every market that buys an ore (imported or
     exchanged), mined at an asteroid with a usable survey holding the ore, or else at the asteroid
     nearest the market whose traits yield it, and sold there; only trips the miner can make in CRUISE,
     through refuelling stops: to the asteroid, and on to the market with the fuel it has left there (a
     full tank where the asteroid sells fuel, as XB5C does; slice 6.10c). A SCARCE or LIMITED ore no
     miner's trip covers comes first, the nearest asteroid first (slice 6.10b, D48, "near before far"). A
     trip covers its ore at the markets its ship reaches in CRUISE from the market it sells at, through
     refuelling stops, leaving with a full tank (D53, `CoveringTrip`): a drone mining for B7 doesn't cover
     the middle, nor one in the middle B7, while the command ship's 400-unit tank reaches both. Then the
     market shortest of its ore (D28): SCARCE, then LIMITED (low supply, D22), and once
     no market is short, the lowest supply there is, even when it pays less. A market that has the ore ABUNDANT
     has all it wants, and is no target (D77, `MiningPlanner.IsAbundant`); HIGH still is. Within a supply level, a
     surveyed ore first, then the most a single extraction is expected to fetch: the ore's share of the
     survey's deposits (without a survey, one of the asteroid's ores) times its price. One miner per
     sell market and ore. It logs `MiningStarted`, reason `uncovered` (D48 chose it over D28's first),
     `surveyed`, `low_supply` or `lowest_supply`;
  3. **far targets** (slice 6.10c, D45): a market out of the miner's CRUISE reach that sells fuel counts
     too, mined at the asteroid nearest it within a CRUISE round trip of it (out with a full tank, back
     with what is left): the trip (`Drifting`) gets to the market first, the fastest way (D84: it cruises as far
     as it can and drifts the rest, 1 fuel whatever the distance and about ten times slower), and mines from
     there. A far target ranks after every reachable one of
     its supply level, and among D48's uncovered ores after those in reach. In X1-DC53 on 2026-10-03 that was
     B7, SCARCE or LIMITED in five ores that B14, 25 from it, yields; from the middle a drone drifted there in
     about 2.5 hours, before slice 6.19 let it cruise the first part. Once it is there, B7's ores are in reach, and the middle is the drift away;
  4. **sharing** (slice 6.14, D77, asked on 2026-10-05: "I'd like the miners to only mine, even if there is more profit in
     trading. They can mine until every mineral is ABUNDANT."): a mining drone (`FleetRoles.IsMiningDrone`) whose every
     pair below ABUNDANT, in reach or a drift away, already has a miner shares one (`MiningPlanner.SharedTargets`): the
     lowest supply first, a pair in CRUISE reach before one a drift away (D45), then the pair with the fewest ships on it
     (the trips under way, and those given out earlier in the same pass), then step 2's order. It logs `MiningStarted`,
     reason `shared`. A drone is passed over to the trading plan (B63) only when nothing below ABUNDANT is left that it
     can reach and sell, or with a full hold nobody it can reach buys (step 1). The command ship shares nothing: it takes
     what pays it most (D38), and is passed over when every pair has a miner.
- **Far asteroids with a shuttle** (slice 6.18, D83, asked on 2026-10-05: "We park a light shuttle ... at the asteroid, and
  have the drones drop their ore into the light shuttle. When the light shuttle is full, it sells the ore at the market,
  then comes back."): a collection point (`MiningPlanner.CollectionPoints`) is, for a market that sells fuel and buys an
  ore below ABUNDANT that no drone mines on a CRUISE round trip of it (D45), the asteroid nearest it that yields the ore,
  when a drone gets there from the market on a full tank and a light shuttle (its tank from the cheapest shipyard's
  listing) flies there and back; one point per asteroid and market, with every such ore. In X1-FJ91 on 2026-10-05: B44,
  53 from B7, whose GOLD_ORE, SILVER_ORE and PLATINUM_ORE were SCARCE (a drone's 80-unit tank doesn't fly the 106 there
  and back; B7 buys all eight of B44's ores, the rest as exchange goods). A point wants a drone per SCARCE or LIMITED ore
  (D48):
  - a mining drone at a point's asteroid stays there (`MineForShuttleGoal`), before anything else in the steps above;
  - a free mining drone whose best target isn't an ore no miner covers (step 2's D48) takes a place at a point that
    wants more drones, before the other targets and sharing: it flies to the asteroid the fastest way (D84: out of its
    CRUISE reach it cruises as far as it can and drifts the rest, rather than by the point's market as D45's trips go),
    for the scarce ore no drone there mines for yet. It logs
    `MiningStarted`, reason `collection`. The command ship takes none (D38);
  - a shuttle designated for the point trades like any cargo ship until a drone is parked at the asteroid (D86, asked on
    2026-10-05: "Trade until parked"; a drone's way there takes hours). Then, with the role board on, its role is
    `Collect`, and once its trip ends it gets a round (`CollectOreGoal`, journaled `CollectionStarted`);
  - purchases, with the drones for scarce minerals (`PurchaseTier.Coverage`, as chosen on 2026-10-05): first a light
    shuttle for a point where a drone has a place and no shuttle is designated yet, then the drones (each point's
    scarce ores count a drone each in the coverage count, below), and a second shuttle, at most, for a point where a
    drone parked at the asteroid waits with its hold full while the first is away selling ("A second shuttle is bought
    when drones wait for one"). A shuttle bought for a point is designated for it in the plan's state, which the role
    board reads. A point that is gone (every ore ABUNDANT) releases its shuttles to the other roles.
- **What a trip keeps** (D71, asked on 2026-10-04: "only throw out minerals that they cannot sell within a single tank
  of fuel, instead of everything they're not specifically mining for"): each extraction keeps the trip's ore, and every
  other ore a market buys within one tank of the asteroid: a full tank's CRUISE flight there without a refuelling stop
  (`MiningPlanner.IsSellableWithinOneTank`; for a drone's 80-unit tank, the markets within about 80). The rest goes
  overboard. The trip still extracts with the survey best for its own ore, and turns to selling when the hold is full,
  of whatever ores; it sells its own ore at its market, and the plan sells the others on the trips after it (step 1
  above). The contract's round trips keep only the contract's ore, so their holds fill with it alone. Siphon
  trips keep more: every gas a market they can carry it to buys, refuelling stops included (D33).
- **Drones,** one a tick, so the next tick counts the new drone; up to `Mining.MaxDrones` (default 20),
  within the credit reserve and when the order ships are bought in lets it (D43). Not while the
  contract plan mines: the contract would take the drone, and the contract plan buys at most one
  (D23). The plan says what it would buy on every pass, whether it may buy it or not:
  1. a drone for a scarce ore (D48), while the system has fewer mining drones (ships that can mine and
     can't survey) than SCARCE or LIMITED ores a drone from the shipyard could serve, each counted once per
     area (`MiningPlanner.ScarceOres`, D53): "at least 1 drone per mineral that is scarce or limited", and
     "the coverage tier may buy a drone per scarce mineral per area (more drones)". An area is the markets
     short of the ore that such a drone flies between in CRUISE (`MiningPlanner.Areas`): in X1-DC53, the
     middle and B7. It doesn't ask the role board, which keeps one drone mining per scarce ore and area.
     Since slice 6.10c an ore a drift away counts (D45: "new and free drones"), so each ore a far market is
     short of adds a drone (on 2026-10-03, B7's five); since slice 6.18 so does each scarce ore of a collection point
     (D83), after the shuttle the point needs (above);
  2. otherwise, when no miner was free, a drone whose first trip by the same ranking (its tank from the
     shipyard's listing, the trips under way held) would serve a market short of its ore (SCARCE or
     LIMITED, D28), in turn with the cargo ships. With the role board on, only when the board would give
     the drone the mining role (`RoleAdvisor`), which, since a drone gathers first (D58), it does.

  A pair a drone would only share buys no drone (D77): the first trip is judged with the pairs under way held, as
  before.
- **The state** (`plan_states`, `MiningAutomation`) lists the low-supply openings: Assigned while a
  miner's trip sells there, Pending otherwise, with the free miners that could take it (they reach its
  asteroid, or would drift to its market, D45), for the `ShipLeftIdle` rule. An opening several drones share names one
  of them (D77); a parked drone holds its ore's opening at its point's market. Since slice 6.18 it lists the collection
  points too, each with its ores, its scarce ores, the shuttles designated for it and the drones with a place there. It
  is written only when it changes.

### Siphon (`SiphonAutomationService`, slice 6.7)

The mining plan for gases, by the same rules, without surveys: the API's siphon call takes none
(`POST my/ships/{ship}/siphon` has no body, and a surveyor's deposits are ores only).

- **A siphoner** is a ship with a gas siphon, a hold and a tank, and nothing to mine or survey with
  (`FleetRoles.IsSiphoner`), whichever plans are on: a siphon drone. The command ship has a siphon too,
  but it mines, or surveys (D20). With the role board on (slice 6.9), a siphoner is a ship with the siphon
  role, which the command ship can have too.
- **Each tick** every free siphoner gets one trip (`SiphonAndSellGoal`); the trip ends when its gas is
  sold, and the plan chooses the next one:
  1. a siphoner that holds goods a market buys sells them first, one good a trip, where each fetches
     most after fuel (reason `held_cargo`): a trip keeps every gas it siphons (D33), and sells only its
     own. A full hold only sells, even where the sale doesn't pay for its fuel: a siphon trip would turn
     to selling at once and end without its gas aboard, on every tick;
  2. otherwise the best of `SiphonPlanner.SiphonTargets`: every market that buys a gas (imported or
     exchanged), siphoned at the gas giant nearest the market from which the siphoner can make the trip
     in CRUISE (there, and on to the market with the fuel left, slice 6.10c), and sold there. A SCARCE or
     LIMITED gas no siphoner's trip covers (D53, as for the miners) comes first, the nearest gas giant first
     (D48); then the
     market shortest of its gas (D28): SCARCE, then LIMITED (low supply, D22), and once no market is
     short, the lowest supply there is; a market that has the gas ABUNDANT is no target (D77). Within a supply level,
     the most a single siphon is expected to fetch (one of the gas giant's three gases times the price), then the
     nearest gas giant. One siphoner per sell market and gas. It logs `SiphonStarted`, reason `uncovered`, `low_supply`
     or `lowest_supply`;
  3. **far targets** (slice 6.10c, D45), as for the miners: a market out of the siphoner's CRUISE reach that
     sells fuel, with a gas giant within a CRUISE round trip of it; the trip drifts there first. X1-DC53 has
     none: its one gas giant, C38, has every buyer in reach of a siphon drone;
  4. **sharing** (slice 6.14, D77, siphon drones as the mining drones): a siphon drone (`FleetRoles.IsSiphoner`) whose
     every pair below ABUNDANT already has a siphoner shares one (`SiphonPlanner.SharedTargets`), by the miners' order:
     the lowest supply, in CRUISE reach before a drift away, then the fewest ships on it. It logs `SiphonStarted`, reason
     `shared`, and is passed over to the trading plan only when nothing below ABUNDANT is left that it can reach and
     sell. The command ship, in the siphon role, shares nothing (D38).
- **Drones** (D32, the miners' rule): one a tick, at the shipyard that sells `SHIP_SIPHON_DRONE` for the
  least in a system where our ships are, up to `Siphon.MaxDrones` (default 10), within the credit reserve
  and when the order ships are bought in lets it (D43): first one per SCARCE or LIMITED gas and area a drone
  from the shipyard could serve (D48, D53, `SiphonPlanner.ScarceGases`; X1-DC53's gas markets are one area, all
  in reach from C38), without asking the role board; then, when no
  siphoner was free, a drone whose first trip by the same ranking (its tank and hold from the shipyard's
  listing, the trips under way held) would serve a market short of its gas, in turn with the cargo ships,
  and with the role board on only when the board would give it the siphon role (`RoleAdvisor`, which it does since
  D58: a drone gathers first). In X1-DC53
  the shipyard is C39, the station at the gas giant C38, where none of our ships stays: the purchase calls
  for a probe (D30), which only the probe plan answers, so with the probe plan off no siphon drone is bought
  until one of our ships happens to be at C39.
- **Gas contracts** stay unsupported (D2, D31): no contract takes the siphoners.
- **The state** (`plan_states`, `SiphonAutomation`, as the mining plan's) lists the low-supply openings
  of every system where our ships are, before the first drone too: Assigned while a siphoner's trip
  sells there, Pending otherwise, with the free siphoners that could take it (they reach the gas giant, or
  would drift to the market, D45), for the `ShipLeftIdle` rule. An opening several drones share names one of them, and a
  pair a drone would only share buys no drone (D77). It is written only when it changes.

**What a gas giant yields** (`GasGiants`): the game doesn't publish it, and a gas giant's traits name no
gas (X1-DC53's C38 has only STRONG_MAGNETOSPHERE), so every gas giant counts as yielding the game's
three gases, HYDROCARBON, LIQUID_HYDROGEN and LIQUID_NITROGEN, about equally: the gases C39 exchanges.
The `Siphoned` journal lines show what the siphons really yield. Only GAS_GIANT waypoints can be
siphoned.

### Construction (`ConstructionPlanService`, slice 6.6)

Asked for on 2026-10-04: "Spacetraders has unfinished buildings at waypoints, specifically an unbuilt jump node.
Finishing this jump node should be top priority, as it opens up the rest of the game. Can you implement a special
role that works on this jump gate?" Off by default (`Automation.Plan.Construction.Enabled`); your decisions are
D64–D68. Bootstrapped after the siphon plan and before trading, which takes its builder when it has nothing it may
buy.

- **Which site** (`ConstructionSites`, D68: "Only the home base jump gate construction should be high priority, any
  other jump gate construction should be low priority or maybe not even considered at all"): the jump gate of the
  home system, where the headquarters are, while it needs materials and one of our ships, a probe aside, is in that
  system. A gate elsewhere is never fetched, built or given a role.
  - The site (`GET systems/{system}/waypoints/{waypoint}/construction`) is fetched when the waypoint cache lists the
    gate as under construction and it isn't cached yet, and again every 10 minutes while it needs materials
    (`ConstructionSiteWatch`, in memory), as other agents supply it too. A gate the waypoint cache lists as built, or
    a site cached as complete, costs no call; a fetch that fails is logged at Warning and waits its 10 minutes.
    Every supply's answer is stored as well (`cached_construction_sites`).
  - The first time a site is seen needing materials the plan journals `PlanStarted` (`Plan` `Construction`, and the
    `Materials`, such as `FAB_MATS 120/1600, ADVANCED_CIRCUITRY 0/400, QUANTUM_STABILIZERS 1/1`); when a site seen
    under construction is complete, `PlanCompleted`.
  - Supplying pays nothing: the API's supply call (`POST systems/{system}/waypoints/{waypoint}/construction/supply`)
    answers with the site and the ship's cargo, without credits.
- **Who builds** (D65): the ships with the construction role, `Construction.Ships` (1) of them: the largest holds
  that aren't drones, probes or the surveyor (`FleetRoles.CanConstruct`). The role board gives the role (reason
  `construction`, see [Role board](#role-board-roleplanservice-slice-69)); with the board off the plan picks the
  builders by the same rule, among the ships that don't survey (`ConstructionPlanner.PickBuilders`).
- **Each tick:**
  1. a free ship in the home system that holds a material the gate still needs supplies it first, whatever its role
     (`ConstructionStarted`, reason `held_cargo`), as much as the gate still needs: the trading plan would sell it, or
     jettison it where no market buys it. While the plan is on, the trading plan jettisons no such cargo;
  2. the plan tells the order ships are bought in what construction would buy next (`PurchaseTier.Construction`,
     D64): the first builder's next load, as if its hold were empty where it is going, worth its cost with fuel. A
     builder on its way lands with the fuel its flight leaves it and docks, so where fuel is sold it leaves with a full
     tank (B65: judged without, a builder flying a load to the far-out gate had no market in reach, and the order heard
     of no load until it landed);
  3. when the order lets construction buy, each free builder with an empty hold takes the first load the credits pay
     for (`ConstructionStarted`, reason `purchase`), leaving the credit reserve;
  4. a builder that takes no load stays free, and the trading plan gives it a trade. The state says why
     (`Waiting`): `purchase_order`, `waiting_for_credits`, `low_supply` (D66), `market_busy` (another trip, a trade or
     construction trip, is on its way to buy the material at every market that would sell a load: one buyer at a time,
     D80) or `no_market`. It does so only when the
     builder is free with an empty hold; while it trades, `ReadyShipSymbols` says whether a load waits for it.
- **A load** (`ConstructionPlanner.Loads`, no I/O) is one material: the builder's free hold, or what the gate still
  needs when that is less, less what our construction trips carry or go to buy, bought at one market in batches of its
  trade volume (D81, asked on 2026-10-05: "Full hold in batches"). A market's trade volume is the most one purchase
  takes, not its stock; D67 had the builder wait for a trade volume of its whole hold, which no market in X1-FJ91 had
  (FAB_MATS and ADVANCED_CIRCUITRY 20 at a time), so the gate got no load for 13 hours. Each purchase raises the price,
  so what a load costs is estimated batch by batch (`PriceSteps.CostInBatches`: each batch 2%, 4% or 6% dearer than the
  one before, for a good traded up to 6, up to 20 or more at a time; measured on 2026-10-05 as medians of 1.8%, 3.6%
  and 5.7%). Only at a market whose supply of it isn't SCARCE or LIMITED (D66); a market short of it gets time to
  recover. Of the materials, the one with the smallest share supplied or on its way
  comes first, then by name; of the markets, the one where the load costs least with its fuel, to the market and on
  to the gate, in CRUISE through refuelling stops (`TradeRoutePlanner.TryPlanFlight`). In X1-DC53, F49 exports
  FAB_MATS and D42 ADVANCED_CIRCUITRY.
- **Money** (D64): supplying pays nothing, so a load is judged like a ship purchase. It leaves the credit reserve
  (`BudgetPolicy`: the floor and the trading holds, D51; the dearest full hold a trader saves up for, D56; and what the
  trips on their way to buy hold back, D57), and waits for every purchase before it in the order ships are bought in:
  the contract's drone, the surveyors, a drone per scarce mineral and area, and the cargo ships of
  `Trade.ShipPurchases`. While the plan has a load to buy, the probes and the further drones and cargo ships wait.
  A construction trip holds back what its cargo costs from the moment it starts until it buys
  (`SupplyConstructionGoal.ReservedCredits`, as D57): the traders, the other trips and ship purchases leave it. As for
  a ship purchase, nothing holds the traders back while the credits for a load build up.
- **The trip** (`SupplyConstructionGoal`): the builder flies to the market, buys the load, flies to the gate and
  supplies it; see `SupplyConstruction` under [Executors](#executors). A ship whose supply the site refused isn't
  offered that material again for 10 minutes (`ConstructionRetries`, in memory).
- **The state** (`plan_states`, `Construction`): the sites with each material's required, fulfilled and on-the-way
  units, the builders, the builders a load waits for (`ReadyShipSymbols`, for the `ShipLeftIdle` rule) and
  `Waiting`. It is written only when it changes.
- **Visibility:** the journal says `ConstructionStarted`, `CargoBought`, `ConstructionSupplied` and
  `ConstructionDropped`, and each trip ends with `TripEnded` (`Activity` `construction`, always a loss). The ledger
  books the purchases as `ConstructionBuy`. The fleet view says `buying FAB_MATS at X1-DC53-F49 for X1-DC53-I55` or
  `supplying FAB_MATS to X1-DC53-I55`. `spacetraders_construction_units_required` and `_fulfilled` feed the
  dashboard's jump gate panels (gembernodes).
- **In X1-DC53** (2026-10-04) the home gate, X1-DC53-I55, was complete before our ships came (FAB_MATS 1600/1600,
  ADVANCED_CIRCUITRY 400/400, QUANTUM_STABILIZERS 1/1), so the plan finds nothing to build there. Its neighbours
  X1-HZ59-I59 and X1-BG54-I54 were under construction, needing the same.
- **And exploring** (slice 6.11): a jump needs the gates at both ends built, so the explore plan can't take the command
  ship away while the home gate needs materials, and the two take turns: the command ship may build first, when it has
  the largest hold, and explore after. A ship on an explore assignment isn't free, so it gets no load. The explore plan
  looks at a gate under construction again hourly (`ExploreAtlas.RecheckAfter`), so it may set off up to an hour after
  this plan has seen the gate complete.
- **Off** (the default): nothing is fetched or bought, no ship gets the construction role, and a trip under way
  waits where it is, as every plan's goals do.

### Trading (`TradingAutomationService`, slice 6.5)

- **A trader** is any ship with a cargo hold and a fuel tank that has no goal (or a blocked one), no
  open assignment, and isn't in transit, and that the survey, mining and siphon plans, which go first,
  left free. With the survey plan on, a ship that can survey never trades (D20), unless the spare-time
  plan is on (below); a miner trades only when neither the contract nor the mining plan has work for
  it, and a siphoner only when the siphon plan has none: for a drone, since D77, once nothing it can gather is below
  ABUNDANT, as it shares a pair before that. With the role board on (slice 6.9), a trader is a
  ship with the trade role, or with the mining or siphon role when that plan had no trip for it.
  "Had no trip for it" is what that plan recorded at its pass in the same tick (`PassedOverShips`, B63): the
  mining, siphon and construction plans each note the ships they work with and the free ones they gave no
  work. An arrival's goal step runs outside the tick, so a trip can end after its plan's pass and before
  the trading plan's; that ship wasn't passed over and waits for its own plan's next pass, a tick later,
  instead of trading. A plan that is switched off has no say.
- **A ship that gathers in its spare time** (the command ship, with the survey and spare-time plans on,
  slice 6.8) trades when it has nothing to survey (D34), but only for a route that waits for it once
  its hold is sold, and after the other traders have chosen. The route is judged from where selling its
  hold leaves it (`GatherPlanner.AfterSellingHold`), so it is still there once the hold is sold. Then
  the ship sells its hold first, one good a trip, by the rule every trader sells held cargo by, and
  takes the route after; a spare-time trip that fills its hold is interrupted for that
  (`GatheringInterrupted`, `Reason` `trade`), when it is safe. Without such a route the plan leaves the
  ship, and its hold, to the spare-time plan (D37).
- **A trip** (`TradeRoutePlanner`) is one good, bought at one market and sold at another:
  - its profit is what the sell market pays minus what the buy market charges, times the units,
    minus the fuel for the whole trip: from where the ship is to the buy market, then on to the sell
    market;
  - the units are as many as each earn `Trade.MinProfitPerUnit` (D79, asked on 2026-10-05: "A ship should buy as
    much as is profitable per trip, and sell as much as is profitable per trip"), up to the ship's free hold and what
    the credits no other trip holds back (D57) pay for (cargo may use the credit reserve, D17; the trip's fuel and
    `Trade.FuelReserveCredits`, 5,000, are kept back, D24: below them only fuel is bought). A market's trade volume is the
    most one purchase or sale takes, not its stock (the API's own definition), and the API gives no stock at all, only
    supply and activity levels; more goes in batches, each at the price quoted then. Each purchase raises the next quote
    and each sale lowers it, so a unit's price is estimated by its batch (`PriceSteps`): each batch bought 2%, 4% or 6%
    dearer than the one before, for a good traded up to 6, up to 20 or more at a time, and each sold 2% cheaper (measured
    on 2026-10-05 from the bot's 222 purchases since the reset: medians of 1.8%, 3.6% and 5.7% a full batch bought, 1.8%
    and 2.1% sold; a raised price was back within 1% after a median of 56 minutes). As each further unit earns less, the
    units stop at the first that wouldn't earn the minimum (`TradeRoutePlanner.TryEvaluate`). The trip holds back what
    they are expected to cost (`TradeRoute.CargoCost`). In X1-FJ91 every trade volume was 6, 18, 20, 60 or 180, by kind
    of good, and none moved in the run's first 18 hours; in X1-DC53, a week into its reset, 20 of 150 moved over two days,
    in steps of about 10%, up to three times where they started. D79 replaced D56's full hold in one purchase and one sale
    and D74's exception for ABUNDANT sellers;
  - the fuel is CRUISE, the distance rounded, at least 1 per flight, paid in whole FUEL units of 100
    at the market each flight ends at, at that market's price (where it sells none, the system's
    average). A flight longer than the tank holds refuels at markets that sell fuel on the way, the
    fewest stops first, then the cheapest fuel, each hop within a full tank. Never DRIFT (B47). The flights
    burn where the fuel allows it (slice 6.19, D84), which costs up to twice the fuel counted here, an extra
    cost accepted on 2026-10-05 ("I accept the extra fuel costs this brings");
  - it is lucrative when it earns at least `Trade.MinProfitPerUnit` per unit after fuel (D14).
- **Feeding the jump gate first** (slice 6.22, D89, asked on 2026-10-05: "Please make sure the trade routes prioritize
  the feeding to the portal construction materials"): while the system's jump gate needs materials and the construction
  plan is on, a lucrative trip that delivers a good to a market making one of those materials from it (the market exports
  the material, imports the good below ABUNDANT, and the production chains make the material from the good;
  `TradeMarketMap.ConstructionMaterialMadeFrom`) comes before every other, whatever it earns. The trade context reads the
  materials still needed from the construction cache (`TradeContextReader`). On 2026-10-05 the FAB_MATS markets D52 and
  F58 made 1 to 4 units a tick while their IRON was SCARCE: IRON for them now goes first. Such a trip's `TradeStarted` says
  "which makes the jump gate's … from it (D89)".
- **Ranking** (D15, D82, D85): among the lucrative trips (after those that feed the jump gate, D89), the most profitable
  first, an end product's counted at half its profit (`TradeRoutePlanner.RankingProfit`): one goes first only when it
  earns more than twice as much. An
  end product is a good nothing is made from by the game's production chains (`TradeMarketMap.IsEndProduct`; ships
  count as made from SHIP_PARTS and SHIP_PLATING), wherever it is sold. Asked on 2026-10-05: "Why is iron
  prioritized over ship parts, although the profit would be a lot higher?" D15 had put a trip first only when its
  sell market makes a pricier good from the cargo, by its exports, so SHIP_PARTS, which sell only at shipyards'
  markets, never came first. D82 then put every end product after every other trip, until no trader took FOOD at
  about 75,000 a load while trips of 302 to 3,864 went first; D85, "Half weight", replaced that. The end products
  traded in X1-FJ91 are ANTIMATTER, ASSAULT_RIFLES, CLOTHING, DRUGS, FAB_MATS, FIREARMS, FOOD, FUEL, ICE_WATER,
  JEWELRY, MEDICINE, RELIC_TECH and SUPERGRAINS. Without the production chains every good is one, and the trips go by
  profit alone. A trip's `FeedsTradeSymbol` still names the pricier good its sell market makes
  from the cargo, if any, for the journal and the routes view.
- **Saving up for a trip** (D56, `FullHoldSavings`): "Full hold or nothing, when this occurs the credit
  floor should be temporarily expanded so any ship purchases wait for the full hold to be bought before new ships
  are bought." When a free trader's best route, credits aside, carries more units than the credits for cargo pay for
  (with the trip's fuel), the plan notes it ("saves up for … (D56)"), and the trader takes the best trip it can pay for
  meanwhile: fewer units (D79), or another route. The credit reserve every ship purchase keeps grows by the dearest such
  hold (`BudgetPolicy`), so ships are bought after it. The saving ends when the trader sets off for that hold (what
  the trip holds back takes its place, D57, so ships still wait until it is bought), when the trader's best route is
  another it can pay for, or when the ship no longer trades. In memory: a restart forgets it, and the plan notes it
  again at its first pass. Setting off with fewer units on the route it saved up for ends the saving too.
- **Credits held back for a trip** (D57, `TripReservations`): "Let's have these credits reserved as soon as a ship
  starts towards it, so that this cannot happen (waste of time and fuel)." A trip holds back what its cargo was expected
  to cost when it was chosen, batch by batch (`TradeBetweenMarketsGoal.ReservedCredits`, D79), from the moment it starts
  towards the buy market until the cargo is aboard, less what each batch bought so far cost; a trip that is blocked or
  done holds back nothing. The plan gives the other
  traders only the credits no trip holds back, a trip at its buy market spends its own and those no other trip holds
  back, and every ship purchase leaves them (`BudgetPolicy`). Kept with the trip's goal, so a restart keeps it. A
  price that rose since the trip was chosen is paid from the credits no trip holds back. Seen on 2026-10-03 at 19:29Z:
  SPECTER-8 set off to buy 15 EQUIPMENT (49,485) at K85, another trader spent about 121,000 before it got there, and
  the trip was dropped with nothing bought. A construction trip on its way to buy holds back its cargo the same way
  (slice 6.6, D64), and the traders leave it too.
- **Each tick** every free trader gets a trip, from the cached prices:
  - one that holds cargo first sells it where it fetches the most after fuel, one good a trip, when that
    earns anything. Once no good aboard pays for its sale, the goods go overboard before the trader takes a
    route (D42, `HeldCargo`): they would only take room from it. The contract's ore on a ship that mines for
    the contract stays aboard while the contract wants it, and so do the materials the home gate still needs while
    the construction plan is on (slice 6.6), which that plan, bootstrapped first, has the ship supply;
  - otherwise the best route goes first, to the trader it is best for, and a route one trader holds
    is not offered to another (D18). Nor is a good at a market where another trip is on its way to buy it, a trade or a
    construction trip, those given out earlier in the same pass included: one buyer at a time (D80, `HeldBuys`, asked on
    2026-10-05). Every purchase raises the price, so a second trip would find the market the first left behind: on
    2026-10-05 SPECTER-D and SPECTER-E were both sent for EQUIPMENT at K94, D bought first and E dropped its trip on
    arrival, seven times since the reset. A trip holds the good from when it starts until its cargo is aboard. It logs
    `TradeStarted`.
- **A surveyor with cargo** that the survey plan left free (nothing to survey), and that doesn't gather in
  its spare time, sells its hold the same way, one good a trip, and jettisons what doesn't pay (D42): it
  would otherwise carry it for good. A ship that gathers in its spare time keeps its hold for its next trip
  (D37).
- **A jettison** (`CargoJettison`, D42) throws all of one good overboard, stores the hold the API answers
  with, counts the units in `spacetraders_jettisoned_units_total` and journals `CargoJettisoned` (`Reason`
  `no_buyer`, or `not_worth_the_fuel`). A ship in flight keeps its cargo. A jettison the API refuses is
  logged at Warning and the cargo stays aboard; the same good of the same ship isn't tried again for 10
  minutes (`JettisonRetries`, in memory), as the plan asks on every tick.
- **Cargo ships** (D21, which replaced D16): when every trader has a trip, the plan buys the next
  ship in `Trade.ShipPurchases` (`SHIP_LIGHT_SHUTTLE,SHIP_LIGHT_HAULER,SHIP_LIGHT_HAULER`): the Nth
  while the fleet has fewer than N cargo ships (a hold and a tank, nothing to mine, siphon or survey
  with), at the shipyard that sells it for the least, and only when the new ship would have a
  lucrative route from the shipyard with the credits left after the purchase. Once the list is bought,
  one more of its last type at a time, in turn with the drones (D43), and only when the traders can't keep up
  (D88, asked on 2026-10-05: "can you add a limitation on buying more trade ships unless a trade ship actually adds
  value?"): a route worth `Trade.ShipPurchaseMinRouteProfit` (10,000) that the new ship would have from the shipyard,
  and no trader holds, has waited `Trade.ShipPurchaseWaitMinutes` (30) for a ship with every trader busy
  (`TradeShipDemand`, in memory: a start waits anew). A stable market never gets there. `ShipPurchaseService` keeps
  the credit reserve. One purchase a pass, when the order ships are bought in lets it: a ship of the list is
  saved up for, whatever the routes; a ship beyond it needs nothing until a route has waited so, so the drones' turn
  comes.
- **The state** (`plan_states`, `TradingAutomation`) lists the held routes (Assigned) and up to 20
  lucrative routes no trader holds (Pending), each with the free traders that could have taken it,
  for the `ShipLeftIdle` rule. It is written only when it changes. `GET /status/trading-routes` serves it (slice 2.17,
  D75): the held routes, then the others numbered in the order the plan gives them out, for the markets dashboard.
- **Why a good isn't traded** (slice 2.18, D76): where the plan lists a free trader's lucrative routes, it puts every
  route with a price gap (a market in the system sells the good for less than another pays for it) through the same
  checks `Rank` runs, in their order (`TradeRoutePlanner.Judge`): the buy market in reach, the sell market in reach from
  it, room in the hold and a price and trade volume at both markets, a unit the credits for cargo pay for after the
  trip's fuel, and the first unit and the trip after fuel earning the minimum (D14, D79). A good another trip is on its
  way to buy at that market (D80) is left out, as a held route is. The state's `NotTraded` keeps, for each such good that no
  trader's route carries, the check its route failed for the free trader that got furthest with it (then the largest price
  gap), in a sentence with the figures (`TradeRouteJudgement.Why`): `buy_market_out_of_reach`, `sell_market_out_of_reach`,
  `no_room`, `too_few_credits`, `not_lucrative`, `waiting` for a lucrative one among the 20 waiting routes kept ("It waits
  for a free trader"), or `below_the_listed_routes` for a lucrative one beyond them. B66: a good whose route waited had no
  row, being listed, and once no trader was free the waiting routes went and the good was in neither list (asked on
  2026-10-05: "Assault Rifles should make a tidy profit at 2191, yet it doesn't even show up in the Goods not traded and why
  tab"). A system without a free trader at a pass, every trader there on a trip, keeps what was found there before, with
  its time, less the goods a trader's route now carries. Ships that gather in their spare time aren't judged.
  `/status/trading-routes` serves it as `notTraded`.
- **After each purchase and sale** the trip fetches the market again while the ship is still docked
  there (D25, `MarketRefresher`), so the next decisions see what the trade did to the prices. A
  failed fetch is logged at Warning and leaves the trade as it is.
- **Prices change** while a trip is under way. A ship can't change course in flight, so the trip
  reconsiders where it lands, with the prices its arrival has just fetched: see
  `TradeBetweenMarkets` under [Executors](#executors).

### Spare time (`SpareTimePlanService`, slice 6.8)

Asked for on 2026-10-02: "I'd like my command ship not to be idle." With the survey plan on, the
command ship surveys (D20), and with every ore stocked (D27) it waited. The spare-time plan, bootstrapped
last, gives it something to do then; your decisions are D34–D37.

- **Who:** a surveyor (with the survey plan on) that also has a mining laser or a gas siphon, a hold and a
  tank (`FleetRoles.GathersInSpareTime`): the command ship. With the survey plan off it is a miner, and the
  mining plan gives it work. With the role board on (slice 6.9), a ship with the survey role that has a mining
  laser or a gas siphon, a hold and a tank.
- **The order (D34):** survey first, then trade, then mine or siphon. The survey plan goes first, the
  trading plan takes the ship for a route that waits for it (see [Trading](#trading-tradingautomationservice-slice-65)),
  and this plan gets it only when neither did.
- **The trip** (`GatherAndSellGoal`, one per goal; `GatheringStarted`, `Method` `mines` or `siphons`):
  1. at the nearest asteroid (with its mining laser) or gas giant (with its gas siphon) that it can reach
     through refuelling stops and that yields a good a market it can carry it to buys (D35,
     `GatherPlanner.TryFindSource`): in X1-DC53 usually XB5C, where it surveys. No survey guides it: the
     surveys stay for the drones;
  2. it mines (`ExtractResourcesCommand`) or siphons (`SiphonResourcesCommand`) once per cooldown, keeping
     every good a market it can carry it to buys, whatever it is, and jettisoning the rest (D33's rule),
     until its hold is full. A source that no longer yields anything a market buys ends the trip;
  3. full, it sells the hold one good at a time, each where it fetches most after the fuel to get there
     (D36, `TradeRoutePlanner.TryFindBestCargoSale`, the rule every trader sells held cargo by): it records
     the sale in the goal, flies there, docks, sells in batches of the trade volume and fetches the market
     again (D25). A full hold sells even where the sale doesn't pay for its fuel, or the next trip would have
     no room; after that, what doesn't pay for its fuel stays aboard for the next trip;
  4. the goal ends, and the plans choose again: a survey, a trade or the next trip.
- **Interrupted** while the trip still fills its hold (a trip that sells is nearly done):
  - a survey that needs taking takes the ship at once, with its hold aboard (D37): surveying needs no room,
    and the next trip fills the hold on;
  - a route that waits for it takes it, its hold sold first (D34): see Trading;
  - only when replacing the trip's goal is safe (`SpareTimeInterruption`): no goal step of the ship runs
    (B46), and the ship as stored isn't in flight, so its arrival still matches the goal that flew it (B17).
    Otherwise a later tick tries again. The journal says `GatheringInterrupted` (`Reason` `survey` or
    `trade`, with the `Units` aboard).
- **Its state** (`plan_states`, `SpareTime`) lists each such ship, what it does (`Gathering`, `Selling`,
  `Busy` with another plan's work, or `Waiting` with nowhere to gather and sell) and its trip's source, for
  the `ShipLeftIdle` rule; it is written only when it changes. A ship with a full hold that no market it can
  reach buys jettisons it (D42), and gets its trip on the next pass: a trip would end at once, on every tick.
- **Visibility:** each extraction logs `Extracted` and each siphon `Siphoned`, with `Target` `whatever sells`.
  The yield counts in `spacetraders_extracted_units_total`, so the dashboard's mined panels show it, but a
  spare-time extraction isn't counted in `spacetraders_extractions_total`: the survey statistics would read an
  extraction without a survey, which it is by design, as a sign of too few surveys. The fleet view says
  `mining in its spare time`, `siphoning in its spare time` or `selling …`.
- **Off** (the default), the command ship surveys and waits as before, and doesn't trade (D20); a trip under
  way waits where it is, as every plan's goals do, and a survey still takes the ship off a trip that fills
  its hold.

### Purchasing

- **`ShipPurchaseService`:**
  1. It needs the ship type's price cached for that shipyard. Early in a reset, no ship has
     visited a shipyard yet, so purchases fail with "price unknown".
  2. It asks `BudgetPolicy`.
  3. It needs one of our ships at the shipyard, not in flight: the API sells a ship only there (D30).
     Without one it makes no API call; it records a call at the shipyard (`ShipyardCalls`, in memory,
     open for 2 minutes after the last attempt), which the probe plan answers with its nearest free
     probe, and the plan's next attempt buys. A purchase closes the call.
  4. With a ship there, it fetches the shipyard again and asks `BudgetPolicy` again with the price
     the shipyard asks now (a cached price can be hours old, and every purchase moves it); when the
     fetch fails it uses the cached price.
  5. It buys the ship and saves the new credits and the ship. Mounts are not recorded until the
     next startup sync. Mining capability also follows from the ship type, so a purchased
     `SHIP_MINING_DRONE` still counts as a miner. With the role board on, the new ship waits a tick for
     its role (slice 6.9).
  6. It publishes `NewShipPurchasedEvent` (for the ledger) and `AgentCreditsChangedEvent`, and records the
     purchase for the order ships are bought in at once (`PurchaseNeeds.Bought`): the ledger's row comes a
     moment later.
  7. It tells the name book the fleet, the new ship in it, and its `ShipPurchased` line says the name the
     bot gives the ship: "the bot calls it PICKAXE-3" (slice 2.14, see [Names](#names-shipnames-slice-214)).
  - A purchase that doesn't happen says why (`ShipPurchaseFailure`): `PriceUnknown`, `OverBudget`
    or `NoShipAtShipyard`.
- **`BudgetPolicy`:** spendable credits are the cached credits minus the credit reserve (slice 6.10b,
  D51, `CreditReserve`): `FleetExpansion.MinCreditReserve` (60,000) and
  `FleetExpansion.ReservePerTradingCargoUnit` (1,000) for every unit the ships that trade can carry, so
  the traders can still buy their loads after a purchase. The ships that trade are the cargo ships, the
  command ship (cached as `COMMAND`; it trades whenever it isn't surveying, D34, D38) and any other ship
  the role board has in the trade role. Drones that gather, probes and surveyors buy no cargo: with every
  hold counted, the 205 units of 2026-10-03 would have asked 265,000, above what the credits peaked at.
  With the command ship alone the reserve is 100,000; a light shuttle (40) makes it 140,000, a light hauler
  (80) 220,000, a second 300,000. While a trader saves up for a trip the credits don't pay for yet
  (D56, see [Trading](#trading-tradingautomationservice-slice-65)), the reserve grows by the dearest such hold, and
  by what the trade and construction trips on their way to buy hold back for their cargo (D57, D64). It is judged from
  the cached fleet, the role board and the trips' goals at every evaluation, and exported as
  `spacetraders_credit_reserve`. A load of the jump gate's materials, which pays nothing back, is judged like a ship
  purchase (slice 6.6, D64): see [Construction](#construction-constructionplanservice-slice-66).

### The order ships are bought in (`PurchaseOrder`, slice 6.10b)

Asked on 2026-10-03: "I feel there are too many siphoning drones and not enough other ship types." Each
plan bought on its own, and a drone (~50k) was affordable long before a light shuttle (114k) or a probe
(77k): five siphon drones and two mining drones were bought overnight, and no probe or cargo ship. Your
decisions are D43, D47 and D48: "I'd like at least 1 drone per mineral that is scarce or limited, then
save up for cargo ships, then a mix based on if the minerals aren't going above LIMITED", the mix being
"Alternate drones and cargo ships, but probes first".

- **The order** (`PurchaseTier`), first first:
  1. `Contract`: the contract's drone, while no miner is free for the contract (D23, D40);
  2. `Surveyor`: a designated surveyor for each system with mining drones (D47);
  3. `Coverage`: a drone for each SCARCE or LIMITED mineral, ore or gas, until there is one drone per
     such mineral and area (D48, D53);
  4. `SurveyorPerArea`: one more surveyor for each area with mining drones that has none (D55: "The second
     surveyor is lower priority than the first on the buy order");
  5. `CargoShips`: the cargo ships of `Trade.ShipPurchases` (D21), saved up for;
  6. `Construction`: no ship, but the home gate's next load of materials (slice 6.6, D64: "after the cargo ships"),
     spent for good, so it keeps the credit reserve as a purchase does; reported with the material as the ship
     type and its market as the shipyard, worth the load with its fuel;
  7. `Probes`: a probe for every market (D29);
  8. `Alternating`: drones by the miners' rule (D28, D32) and one more cargo ship of the list's last type,
     in turn: the kind not bought last, so after the list's last cargo ship a drone, then a cargo ship, and
     so on (the ledger's `ShipPurchase` rows, which carry the type, and this process's purchases, which the
     ledger gets a moment later). Any drone counts, the contract's and a scarce mineral's too; a cargo ship is
     a type of the list or one of the game's freighters, so the ones bought before the list was changed
     count. A turn passes when the other kind has nothing to buy: no drone's first trip would serve a market
     short of its mineral, or a miner is free; no new cargo ship would have a lucrative route, or a trader has
     no trip. A turn that passed isn't made up later.
- **Needs** (`PurchaseNeed`, `PurchaseNeeds`, a singleton in memory): every plan that buys says on each
  pass what it would buy now, or nothing, and asks before it buys (`IPurchaseOrder.ReportAsync`). A plan
  may buy when no other plan that is on has a need that comes first, or between drones and cargo ships,
  the turn. A need counts while its plan is on, and only while it can be met: its plan's cap not reached, a
  known shipyard selling the ship. Until each plan that is on, and could need something earlier, has said
  what it needs within the last 2 minutes, nothing after it is bought: after a start, or a pause in which
  no plan ran (a 502 pauses them for 3 minutes), the probe plan, which runs before the survey, mining,
  siphon and trading plans, waits a tick; a plan that fails before it says holds the purchases after it.
- **What it means:** the credits pile up for the purchase first in the order, while everything after it
  waits: the probes fly on, the drones and traders work, only their purchases wait. The order says who
  may buy; the credit reserve stays the purchase's own check (`BudgetPolicy`). The tick still runs the
  plans in their order (contract, probe, survey, mining, siphon, construction, trading), so after a purchase the
  next one in line may come in the same tick.
- **Visibility:** `spacetraders_purchase_need_credits{plan,tier,position,ship_type,shipyard}`, one series
  per plan that needs something, worth the ship's price: the lowest `position` is what the credits are
  saved up for. A purchase is journaled as before (`ShipPurchased`); a purchase held back by the order logs
  only at Debug.

### Which ships a plan considers free

| Plan | Claims a ship with | Treats a ship as free when |
|---|---|---|
| Scout | an assignment and a goal | — (picks one ship, once) |
| Explore | an `Explore` assignment, and a `JumpGoal` or `ExploreSystemGoal` | it is the command ship (cached type `COMMAND`), not in transit, and has no assignment and no goal (or a finished or blocked one) |
| Contract | an assignment only | it is a miner (not a surveyor while the survey plan is on), not in transit, and has no assignment and no goal (or a finished or blocked one) |
| ProbeDeployment | a `DeployProbeGoal` | it is a probe (`FleetRoles.IsProbe`: a probe frame, or cached as `SHIP_PROBE` or `SATELLITE`), not in transit, and has no goal (or a finished or blocked one) |
| Survey | a `SurveyWaypointGoal` | it has a surveyor mount, is not in transit, and has no assignment and no goal (or a finished or blocked one), or is on a spare-time trip that fills its hold (D37) |
| Mining | a `MineAndSellGoal` | it is a miner, not in transit, and has no assignment and no goal (or a finished or blocked one) |
| Siphon | a `SiphonAndSellGoal` | it is a siphoner (`FleetRoles.IsSiphoner`: a gas siphon, a hold and a tank, nothing to mine or survey with), not in transit, and has no assignment and no goal (or a finished or blocked one) |
| Construction | a `SupplyConstructionGoal` | it is in the home system and not a probe, not in transit, and has no assignment and no goal (or a finished or blocked one); a load goes only to a builder with an empty hold (the construction role, or with the role board off one of the `Construction.Ships` largest holds that don't survey), a delivery of held materials to any such ship |
| Trading | a `TradeBetweenMarketsGoal` | it has a cargo hold and a fuel tank, isn't a surveyor while the survey plan is on, is not in transit, and has no assignment and no goal (or a finished or blocked one). With the spare-time plan on, a ship that gathers in its spare time too, free or on a spare-time trip that fills its hold, but only for a route that waits for it (D34) |
| SpareTime | a `GatherAndSellGoal` | it gathers in its spare time (`FleetRoles.GathersInSpareTime`: a surveyor, with the survey plan on, with a mining laser or a gas siphon, a hold and a tank), is not in transit, and has no assignment and no goal (or a finished or blocked one) |

**With the role board on** (slice 6.9), the roles replace the fixed rules in the table: the contract
takes a ship that can mine and doesn't have the survey role, whatever its role (D40); the survey plan a ship
with the survey role; the mining plan one with the mining role, the siphon plan one with the siphon role;
the construction plan one with the construction role for a load (slice 6.6); the trading plan one with the trade
role, or the mining, siphon or construction role; the spare-time plan a ship with the survey role and a mining laser
or a gas siphon, a hold and a tank. Each still needs to be out of transit,
with no assignment and no goal (or a finished or blocked one), as in the table. A ship with no role (`None`:
just bought, or none of its roles' plans is on) gets work only from the contract plan, when it can mine. The
scout and probe plans don't read the roles.

**Consequences:**
- After scouting, the command ship has no goal and no assignment. With the survey plan on it
  surveys (D20), and with the spare-time plan on too it trades or gathers when it has nothing to
  survey (D34); with the survey plan off it is a miner, so the contract or the mining plan takes it,
  and trading only when neither has work for it.
- A drone that finishes a trip is free for a moment: the contract plan, bootstrapped first, takes it
  while the contract needs units (D23), and the mining plan otherwise.
- With the role board on, the command ship surveys while it is the only ship that can survey and a drone
  can mine (D38); every drone mines for the contract while the contract wants ore (D40), and otherwise
  gathers whatever trading would pay (D58): it shares a pair once every pair below ABUNDANT has a drone, and trades
  only once nothing it can gather is below ABUNDANT (D77).

---

## 4. Ships: goals, executors and commands

### Goals

- **Storage:** each ship has at most one active goal, stored in `cached_ships` (`GoalId`,
  `GoalKind`, `GoalPayloadJson`, `GoalStatus`).
- **Kinds:** 19 kinds are defined, but only thirteen are ever created: `ScoutWaypoint`,
  `DeployProbe`, `MineAndSell`, `SiphonAndSell`, `GatherAndSell`, `TradeBetweenMarkets`,
  `SurveyWaypoint`, `MoveToWaypoint` (the survey ship's move, D54), the explore plan's `Jump` and `ExploreSystem`
  (slice 6.11), `SupplyConstruction` (a construction trip, slice 6.6), and the mining plan's `MineForShuttle` (a drone
  parked at a far asteroid) and `CollectOre` (a shuttle's round of collecting there; slice 6.18, D83). The older
  `SiphonResource`, like `MineResource`, is never created.
- **Status:** `Assigned`, or `Blocked` once the circuit breaker stops the goal (see below).
  Nothing else changes it (B16). A blocked goal also records why, in `StatusReason`
  (`runaway`).
- **Set by:**
  - the scout, explore, probe, survey (a survey, or a move to where most drones mine, D54), mining, siphon,
    construction, trading and spare-time plans; the survey and trading plans
    also replace a spare-time trip that fills its hold (`SpareTimeInterruption`, slice 6.8);
  - `MineAndSellGoalExecutor` and `SiphonAndSellGoalExecutor`, which record that their trip's drift has
    ended (slice 6.10c) and that it turns to selling, `GatherAndSellGoalExecutor`, which records that its
    trip turns to selling and each sale it chooses, and
    `TradeBetweenMarketsGoalExecutor`, which records its purchase, and a sale it moves, and
    `SupplyConstructionGoalExecutor`, which records its purchase, in their own goal (the goal id stays, so the
    arrival still matches), and `ExploreSystemGoalExecutor`, which records each stop visited.
- **Cleared by:**
  - `DeployProbeGoalExecutor` when it finishes, `TradeBetweenMarketsGoalExecutor` when the trip is
    sold or dropped, `MineAndSellGoalExecutor`, `SiphonAndSellGoalExecutor` and
    `GatherAndSellGoalExecutor` when the trip is sold or can't go on, and
    `SurveyWaypointGoalExecutor` after each survey (slice 6.4; B16's survey part, fixed: survey goals
    were never cleared), `MoveToWaypointGoalExecutor` at the move's target (D54), `JumpGoalExecutor` after the jump (or
    when the credits no longer pay for it), `ExploreSystemGoalExecutor` after the last stop, and
    `SupplyConstructionGoalExecutor` when the trip has supplied its site or is dropped (slice 6.6);
  - the scout plan, when its last stop is done.
- **A trip books what it made** (D46, slice 6.10a): the six trip goals (`TradeBetweenMarkets`, `MineAndSell`,
  `SiphonAndSell`, `GatherAndSell`, since slice 6.6 `SupplyConstruction`, which earns nothing, and since slice 6.18
  `CollectOre`, activity `collecting`, which sells what parked drones mined; the parked drone's `MineForShuttle` is no
  trip) derive from `TripGoal`, which keeps `Earned` (sales) and `Spent` (cargo bought)
  as the executor records them (a goal stored before has both at 0). Every end of a trip clears the goal and then books
  it (`TripBook`): fuel is the ship's `FuelPurchase` ledger rows since the goal's `StartedAt`, profit is earned − spent −
  fuel, logged as a `TripEnded` line and counted by activity. Sales and purchases come from the goal because a sale's
  ledger row is written after the goal has ended; fuel, bought at departures minutes earlier, is in the ledger by then.
  The ends: sold, and every way a trip stops early (`rejected`, `nothing_aboard`, `not_bought_here`, `no_buyer`, and a
  trade's `not_lucrative`, `not_possible` and `not_bought_here`), `interrupted` when the survey or trading plan takes a spare-time trip
  over, and `runaway` when the circuit breaker blocks a trip; a construction trip's `supplied`, or why it was dropped
  (`not_needed`, `not_sold_here`, `low_supply`, `over_budget`, `wrong_location`). Not booked: a contract round trip closed without a
  delivery (released at a restart, or when the contract is fulfilled), goals an agent reset wipes, and the earlier
  batches of a sale that fails partway; a trade bought before slice 6.10a's deploy books its whole sale once.

### `ShipGoalExecutorService`

- **Loads** the ship with `FindAsync`. That skips the arrival dead-reckoning that `GetAllAsync`
  applies (B17).
- **Runs** one step of the executor for the active goal. Only the eleven kinds above are
  dispatched (the move since B56), so `IdleGoalExecutor` is unreachable.
- **Skips** every goal step while `Automation.Enabled` is off, whatever triggered it, and the
  goals of a plan that is switched off.
- **One step at a time per ship** (B46): a step that finds another step of the same ship running is
  skipped (`ShipGoalStepGuard`, in memory), and the next tick takes the ship's next step. The tick and
  an arrival could both step a ship as it docks, and a trade step would have bought twice.
- **Skips** a goal that is `Blocked`. It stays blocked until a plan replaces it; mining, siphon,
  trading and spare time treat its ship as free, but the scout plan never replaces its goal.
- **Circuit breaker:** before each step it counts the ship's goal steps over the last minute
  (`GoalStepCircuitBreaker`, in memory). The tick alone takes 12. Above
  `Automation.CircuitBreaker.MaxGoalStepsPerMinute` (default 60) it doesn't run the step: it
  blocks the goal with reason `runaway`, logs `ShipBlocked` at Warning and counts
  `spacetraders_goal_breaker_trips_total{ship}`. A loop like B1 is stopped after 60 steps. It
  remembers each ship's last trip (`LastTrips`, in memory) for the `CircuitBreakerTripped` rule,
  because mining, siphon and trading replace a blocked goal at once.
- **Counts** every step it runs in `spacetraders_goal_steps_total{kind}`.
- **On Completed,** only a scout goal triggers anything: advancing the scout plan. No status,
  event or history row is written for any outcome.
- **Called by:**
  - the tick (every ship);
  - `ShipNavigationCompletedHandler`;
  - `StartupRecoveryService`.

### Executors

In this table, *[cmd]* means an inline command (`InvokeAsync`) and *[API]* a direct call to the
game API. Every executor first returns "waiting for arrival" while the ship is in transit.

No executor navigates to the waypoint it is already at. At its target, an executor that needs the
ship docked docks it, and one that needs it in orbit orbits it; that is the whole step, and the next
step does the work.

**How they fly** (`GoalFlight`, slice 6.19, D84, asked on 2026-10-05: "I'd like a ship to burn if they can reach the
destination with double fuel consumption, but cruise if they cannot", and "can we optimize the routing for a location
where a combination of cruising and drifting is faster than just drifting?"): one leg a step, as
`TradeRoutePlanner.TryPlanNextLeg` plans it:

- within reach of a chain of markets that sell fuel, the next stop of that flight, the fewest stops first (B47);
- out of that reach, the first leg of the fastest way (`TryPlanMixedFlight`): CRUISE legs on the fuel aboard, the tank
  filled at each market that sells fuel, and DRIFT legs (1 fuel whatever the distance, ten times slower) where nothing
  faster gets on, by the API's flight times (A* over the waypoints and the fuel aboard; about a millisecond in X1-FJ91's
  94 waypoints). From H60 to B44, a drone cruises to F57 by way of A1 and drifts the 244 from there: 2.0 hours, against
  2.9 drifting to B7 first;
- a leg burns (twice as fast, on twice the fuel) when the tank holds twice its CRUISE fuel and burning strands nothing:
  into a market that sells fuel, where the tank fills; elsewhere only when what is left still takes the ship on as
  cruising would (to the trip's market with no more refuelling stops, or, without one, to a market that sells fuel, in
  CRUISE, B58); in a way out of reach, only a cruise leg into a market that sells fuel. Otherwise it cruises;
- no leg lands the ship with an empty tank where no fuel is sold (asked on 2026-10-05: "A ship can technically land
  anywhere with 1 fuel and then drift to a fuel station"): with 1 left it can always drift on to fuel.

In orbit at a market that sells fuel, short of a full tank, a ship docks first when a full tank would fly the leg
differently (further, or in BURN): only a docked ship fills its tank. Without a way known (a ship without fuel), it
flies straight there in CRUISE and the navigation does what it can. The contract's commands fly the same way
(`CommandFlight`). The planners still count CRUISE for reach, time and fuel; a burnt leg costs up to twice the fuel
they count.

| Executor | Behaviour |
|---|---|
| `ScoutWaypoint` | Docked at the target: mark it visited and complete. In orbit at the target: dock. Elsewhere: [cmd] navigate towards it (`GoalFlight`: through refuelling stops when it is beyond one tank, B47, in BURN where that strands nothing, D84). |
| `DeployProbe` | One flight of a probe (slice 6.3). At the target (the arrival fetched its market and shipyard, and docked): clear the goal and complete; the probe plan chooses again. Elsewhere: in DRIFT, [cmd] switch to CRUISE first (a probe has no tank, so no flight costs it fuel); then [cmd] navigate. |
| `MineAndSell` | One trip (slice 6.4). **Drifting** (slice 6.10c, D45), first, for a trip to a market out of the ship's CRUISE reach: [cmd] navigate towards that market the fastest way (`GoalFlight`, D84: cruising as far as it can and drifting the rest, 1 fuel whatever the distance and about ten times slower), logging `DriftStarted` on the leg that drifts; at the market (the arrival docked it), record that the drift has ended; the trip's next flight refuels there. **Mining:** [cmd] navigate towards the asteroid (`GoalFlight`: in the leg's mode, which switches a ship left in DRIFT out of it; in BURN when what is left still takes the ore to the market as cruising would, D84; refuelling stops when it is beyond one tank; in orbit at a fuel market where a full tank would fly the leg differently, dock first so the navigation refuels); there, wait for the cooldown, then [cmd] `MineResourceVolumeCommand` once per step, which extracts with the best survey for the ore and keeps the other ores a market buys within one tank (`KeepOtherOres`, D71). A full hold, of any ores, turns the trip to selling. **Selling:** with none of the trip's ore aboard, clear the goal and complete (the plan sells the other ores); else navigate towards the sell market, dock, [API] sell the trip's ore in batches of the market's trade volume, publishing `ShipCargoSoldEvent` for each, fetch the market again (D25), clear the goal and complete. A market that no longer buys the ore, or an extraction the command rejects, clears the goal; the plan chooses again. |
| `SiphonAndSell` | One trip (slice 6.7), as `MineAndSell`, the way to a far market first included (slice 6.10c, D84). **Siphoning:** [cmd] navigate towards the gas giant (`GoalFlight`); there, wait for the cooldown, then [cmd] `SiphonResourcesCommand` once per step, which keeps every gas a market it can reach buys (D33). A full hold, of any gases, turns the trip to selling. **Selling:** with none of the trip's gas aboard, clear the goal and complete (the plan sells the other gases); else navigate towards the sell market, dock, [API] sell the trip's gas in batches of the market's trade volume, publishing `ShipCargoSoldEvent` for each, fetch the market again (D25), clear the goal and complete. A market that no longer buys the gas, or a siphon the command rejects, clears the goal; the plan chooses again. |
| `GatherAndSell` | One spare-time trip (slice 6.8). **Gathering:** [cmd] navigate towards its asteroid or gas giant (`GoalFlight`); there, wait for the cooldown, then [cmd] `ExtractResourcesCommand` at an asteroid or `SiphonResourcesCommand` (for `whatever sells`) at a gas giant, once per step, keeping every good a market it can reach buys. A source that no longer yields anything a market buys ends the trip. A full hold turns the trip to selling. **Selling:** choose the good that fetches most after fuel (with a full hold, even at a loss on the fuel) and record the sale in the goal; navigate there, dock, [API] sell it in batches of the market's trade volume, publishing `ShipCargoSoldEvent` for each, fetch the market again (D25), and clear the sale from the goal; the next step chooses the next. A market that no longer buys the good: the next step chooses again. Nothing left that pays for its fuel: clear the goal and complete. An extraction or siphon the command rejects clears the goal; the plan chooses again. |
| `TradeBetweenMarkets` | [cmd] navigate towards the buy market (`GoalFlight`: in BURN where the fuel allows it, otherwise CRUISE, D84; a ship left in DRIFT is switched out of it, slice 6.10c), by way of refuelling stops when it is beyond one tank (each stop's arrival refreshes that market), and dock. **Docked at the buy market:** with nothing bought yet, work the trip out again with the prices the arrival has just fetched (the flight there is spent, so only the fuel still ahead counts); when not even a unit earns the minimum, the trip no longer earns it a unit after fuel (`not_lucrative`), or the credits, its own and those no other trip holds back (D57), don't pay for a unit (`not_possible`), clear the goal (`TradeDropped`) and the plan chooses again from there. Otherwise buy in batches (D79): each [API] purchase takes the units of the next batch of the trade volume whose sale, as last seen at the sell market and each batch sold a step cheaper (`PriceSteps`), still earns `Trade.MinProfitPerUnit` over the price quoted now (`TradeRoutePlanner.UnitsWorthBuying`), up to the free hold and what the credits pay for with the fuel ahead kept back; it publishes `CargoPurchasedEvent`, logs `CargoBought`, fetches the market again (D25) and stores the goal with what the batch cost, holding back only what is left to buy; a restart goes on from what is aboard. The first batch whose units wouldn't earn the minimum ends the buying; then record the purchase in the goal and end the saving for that route, if any. Then navigate towards the sell market and dock. **Docked at the sell market:** when selling there no longer earns `Trade.MinProfitPerUnit` over what the cargo cost and another market pays more after fuel, move the sale there, once per trip (`TradeRerouted`); otherwise sell in batches of the market's trade volume (D79): [API] sell one, publishing `ShipCargoSoldEvent` and logging `CargoSold`, fetch the market again and store what it fetched, and go on while the quote still earns the minimum. When it no longer does, the rest goes where it fetches more after fuel, on the same once-per-trip terms (`TradeRerouted`), or with nowhere better is sold there all the same; a sale that never earned it sells anyway. Then clear the goal and complete. A market that doesn't buy the good, once the sale has moved: clear the goal (`TradeDropped`); the plan then sells the cargo where it can. |
| `MoveToWaypoint` | One flight to a waypoint (D54). At the target (its arrival docked it): clear the goal and complete. Elsewhere: [cmd] navigate towards it (`GoalFlight`); out of the ship's CRUISE reach (**Drifting**), that is the fastest way, which cruises as far as it can and drifts the rest (D84), logging `DriftStarted` on the leg that drifts. The survey plan moves a ship that can only survey this way. |
| `Jump` | One jump (slice 6.11). Not at the system's gate: [cmd] navigate towards it (`GoalFlight`). At the gate: wait for the cooldown; when the credits after the antimatter (its price at the gate's market as last seen; fetched once if never seen) fall below `FleetExpansion.MinCreditReserve`, clear the goal (the plan holds the jump); docked, refuel when the gate sells fuel and the tank isn't full, then orbit; [API] jump to the destination gate, store the nav and the cooldown, store the credits, publish `ShipJumpedEvent`, log `Jumped`, clear the goal and complete. In the destination's system already: clear the goal and complete. A refused jump (`JumpRefusedException`): block the goal with `jump_refused` and log `ShipBlocked` at Warning; the plan chooses again. |
| `ExploreSystem` | Scouting one system (slice 6.11). At the next stop: fetch its market (`MarketRefresher`) and its shipyard unless they were stored since the goal began (the arrival stores them; a jump doesn't), mark it visited, and record the visit in the goal; after the last stop clear the goal and complete. A fetch that fails is logged and the ship moves on. Before a flight, wait for the cooldown (a jump's); then [cmd] navigate towards the stop (`GoalFlight`). |
| `SurveyWaypoint` | One survey (slice 6.4). [cmd] navigate towards the asteroid (`GoalFlight`). Docked there: orbit. On cooldown: wait. In orbit: [API] survey, store the cooldown and the surveys (`SurveyKeeper`: `cached_surveys`, `Surveyed` per survey, `spacetraders_surveys_taken_total`), clear the goal and complete. A failed survey clears the goal too (the plan gives it again; a failure that repeats shows as `RepeatingError`). |
| `SupplyConstruction` | One construction trip (slice 6.6). [cmd] navigate towards the buy market (`GoalFlight`: through refuelling stops, in BURN where that strands nothing) and dock. **Docked at the market**, with the prices the arrival has just fetched: the units are the trip's, at most the free hold and what the site still needs less what the other construction trips carry; the trip is dropped (`ConstructionDropped`) when that is nothing (`not_needed`), the market no longer sells the material (`not_sold_here`), its supply is SCARCE or LIMITED (`low_supply`, D66), or the first batch would dip into the credit reserve (`over_budget`, D64; what the trip holds back is its own to spend); otherwise buy them in batches of the market's trade volume (D81): for each, [API] buy it at the price quoted then, publish `CargoPurchasedEvent` (`ForConstruction`: the ledger's `ConstructionBuy`), log `CargoBought`, fetch the market again (D25), and store the goal with what the batch cost, holding back only what is left to buy; a restart goes on from what is aboard. Before each batch it checks the market as the last refresh fetched it: when the supply has fallen to SCARCE or LIMITED, or the batch would dip into the reserve, it buys no more and takes what it has to the site (logged at Information). Then it records the purchase in the goal: the units it bought, what they cost. Then navigate towards the site and dock. **Docked at the site:** with none of the material aboard, clear the goal and complete; else supply as much as the site still needs, as cached (when that says none, it fetches the site once more), [API] supply, store the site and the hold the API answers with, publish `ConstructionSuppliedEvent`, log `ConstructionSupplied`, clear the goal and complete. A supply the API refuses (4800, 4801: `not_needed`; 4802: `wrong_location`) logs `ConstructionDropped` at Warning, fetches the site again and clears the goal, with the cargo aboard; the plan doesn't offer that ship the material again for 10 minutes. Every end books the trip (`TripBook`, `construction`): a loss, as supplying pays nothing. |
| `MineForShuttle` | A drone parked at a far asteroid (slice 6.18, D83). Elsewhere: [cmd] navigate towards the asteroid the fastest way (`GoalFlight`, D84): out of its CRUISE reach (**Drifting**) it cruises as far as it can and drifts the rest, logging `DriftStarted` on the leg that drifts; at the asteroid, record that the drift has ended. **At the asteroid** (orbiting first: the arrival docked it, and a transfer needs both ships in the same state): with cargo aboard and a shuttle with a `CollectOre` round there, in orbit, with room and not selling, [API] transfer each good (`POST my/ships/{ship}/transfer`), as much as the shuttle has room for, storing the drone's hold the API answers with and the shuttle's as it held and was handed, and logging `CargoTransferred`; a refused transfer is logged at Warning and fetches the shuttle's hold again. Otherwise, with room, wait for the cooldown, then [cmd] `MineResourceVolumeCommand` once per step with the best survey for its ore, keeping the ores a market buys within one tank (D71); with its hold full and no shuttle there, wait, without an API call. It never ends on its own: the mining plan gives it again while the point is open. |
| `CollectOre` | A shuttle's round of collecting (slice 6.18, D83). **Collecting:** [cmd] navigate towards the asteroid (`GoalFlight`); there, orbit, and wait without an API call while the drones hand over, until its hold is full, or no drone with a place there is at the asteroid: then record that it sells. **Selling:** [cmd] navigate towards the market and dock; [API] fetch its hold (the drones wrote the cache from what they handed over), [API] sell each good the market buys in batches of its trade volume, publishing `ShipCargoSoldEvent` and logging `CargoSold`, jettison what the market doesn't buy (D42), fetch the market again (D25), clear the goal and book the trip (`collecting`, `sold`, or `nothing_aboard`). |
| `Idle` | Unreachable. |

### Commands

- **`NavigateToWaypointCommand`:**
  - Already at the destination: it does nothing and logs a warning. It publishes no
    `ShipNavigationCompletedEvent`, because that would run the caller's goal step again without
    progress (B1, fixed).
  - Docked: it refuels if the waypoint sells fuel, then orbits.
  - In orbit, it sets the flight mode the command asks for, if any (`FlightMode`, slice 6.10c, slice 6.19;
    `FlightModeSubCommand`, which calls the API only when the ship's mode differs): the leg's, as every flight
    a goal plans asks for it (`GoalFlight`, D84: BURN, CRUISE or DRIFT). A probe asks for none: its executor
    switches a probe in DRIFT to CRUISE itself. The contract's commands don't send this command; they set the
    leg's mode themselves (below).
  - Then it navigates. A flight the fuel aboard can't pay for in the ship's flight mode (`FlightFuel`,
    from the cached positions: the distance rounded, at least 1, in CRUISE; twice that in BURN; 1 in DRIFT)
    isn't asked of the API, which would refuse it with a 400 (B62); it goes to the fallback at once, with a
    warning, as does a flight the API refuses for its fuel. The fallback flies a leg planned in BURN in CRUISE
    when the fuel aboard pays for that (D84), then tries DRIFT mode and intermediate markets, then schedules
    the arrival and publishes `ShipInTransitEvent`. Every flight a goal or the contract plans goes through
    refuelling stops when it is beyond one tank, and the fastest way where no chain of fuel markets reaches
    (D84), so the fallback is left for a ship whose fuel has run short of its plan. It leaves the ship in
    DRIFT, and the ship's next flight asks for its leg's mode, which switches it out of DRIFT (B47).
- **On arrival** (`NavigateToWaypointArrivedCommand`) it refreshes the market through `MarketRefresher`
  (publishing `MarketDataRefreshedEvent`; an answer without prices isn't stored, B62) and the shipyard
  (an answer without the ships for sale leaves the cached ones, B64), docks, and publishes
  `ShipNavigationCompletedEvent`.
- **Orbit, Navigate, Dock, Refuel and FlightMode** are DI sub-commands, not bus messages. Refuel publishes
  `ShipRefueledEvent`.
- **`MineResourceVolumeCommand`** extracts once per call at an ASTEROID, ASTEROID_FIELD or
  ENGINEERED_ASTEROID waypoint (B48, fixed: only the last two were accepted, so 56 of X1-DC53's 57
  asteroids were refused), with the best usable survey of the waypoint for its ore
  (`SurveySelection`: the largest share of the deposits, then the larger deposit, then the later
  expiry), or without one. It counts the extraction (`spacetraders_extractions_total`, with or
  without a survey), the survey's use, and logs `Extracted`. A survey the API refuses (4224
  exhausted, 4221 expired, 4220 not verified, or a 422 without a game error code when it can't read
  the survey, reason `rejected`, B51; mapped by the API adapter to `SurveyRefusedException`) is
  dropped and journaled as `SurveyEnded`, and nothing is extracted that step (B49, fixed: the survey
  would have been tried again on every step). A rejected survey is also logged as a warning with the
  API's response body. The survey goes back to the API as it was given out, its expiry in the API's
  own form (`…51.937Z`, B51). Other goods are jettisoned, but for a mining trip's (`KeepOtherOres`, D71): it keeps
  every other ore a market buys within one tank of the asteroid, a full tank's CRUISE flight without a refuelling stop.
  The contract's round trips keep only the contract's ore.
- **`SiphonResourcesCommand`** (slice 6.7) siphons once per call at the GAS_GIANT waypoint the ship is
  at, in orbit (a docked ship orbits first; the arrival docks it), off cooldown and with room in the
  hold, without a survey: the API's siphon call takes none. It stores the cargo and the cooldown,
  counts the yield in `spacetraders_extracted_units_total` (not in `spacetraders_extractions_total`,
  which the survey statistics read) and logs `Siphoned`. It keeps every good that a market the ship can
  carry it to from the gas giant buys (D33), whichever gas its trip is for, and jettisons the rest, which
  would fill the hold for good. Another waypoint type is refused, with a `ShipStateMismatchEvent`.
- **`ExtractResourcesCommand`** (slice 6.8) extracts once per call at the ASTEROID, ASTEROID_FIELD or
  ENGINEERED_ASTEROID waypoint the ship is at, for a spare-time trip, without a survey (the surveys stay
  for the drones, D35): in orbit (a docked ship orbits first), off cooldown and with room in the hold.
  It stores the cargo and the cooldown, counts the yield in `spacetraders_extracted_units_total` (not
  in `spacetraders_extractions_total`) and logs `Extracted` (`Target` `whatever sells`, no `Signature`).
  It keeps every good that a market the ship can carry it to buys, as a siphon does (D33), and jettisons
  the rest. Another waypoint type is refused, with a `ShipStateMismatchEvent`.
- **`MineResourceVolumeCommand` and `FulfillContractDeliveryCommand`**, used by the tick's contract
  work, dead-reckon arrival themselves and navigate without a goal id (B17). The mining and survey
  goals navigate with `NavigateToWaypointCommand`, which carries the goal id, so their arrivals wake
  them. The contract's flights go in CRUISE (B47, `CommandFlight`): straight to the asteroid or the
  delivery when the fuel aboard will do, otherwise to the first market on the way that sells fuel,
  one leg a tick. A ship the next tick finds at such a stop is in orbit, so it docks and refuels before
  it flies on; a docked ship fills its tank before it orbits.
- **`PatchShipNavCommand`** changes the flight mode; the probe executor sends it (a navigation that asks
  for a mode sets it itself).
- **Selling and buying** are direct API calls from the executors, which publish what they did.
- **Credits:** whatever changes the credits stores them in the cached agent and publishes
  `AgentCreditsChangedEvent` (`AgentCreditsUpdates`): refuels, sales, cargo and ship purchases, jumps,
  and contract payments (B7, fixed). Startup sync and agent registration write the credits
  without the event.
- **A ship in the wrong state** for a command (for example not in orbit) leads to a
  `ShipStateMismatchEvent`, which only writes the activity log.

### Arrivals and timers

1. Navigate stores an arrival in `scheduled_ship_events`, keyed by ship and goal id.
2. `ShipEventScheduler` loads all timers into memory, sleeps until the next one (or 30 s), deletes
   the row and publishes `ShipArrivedEvent`. It is not leader-gated.
3. `ShipArrivedEventHandler` continues only if the event's goal id matches the ship's active goal
   (B17). It then sends `NavigateToWaypointArrivedCommand`.

Cooldown timers are never scheduled, and `ShipCooldownExpiredEvent` has no handler. Goals that
wait for a cooldown simply run again on a later tick.

### Names (`ShipNames`, slice 2.14)

Asked on 2026-10-04: "SHIPS are now named by the game in ascending order. Can we make custom names within the API which
should be type-number … Bonus points if there's a list of relevant names for each of the types, one of which is picked
per reset to call that type, e.g. all sattelites being called SPUTNIK-1, SPUTNIK-2 etc. … while SIPHON DRONE and MINING
DRONE are both EXCAVATORs, they should get different names." Your decision is D72: the name shows beside the symbol.

- **The game's symbol stays** what every API call, table, metric label (`ship`) and journal line keys a ship by
  (`SPECTER-4`): the API can't rename a ship. The name is the bot's own, worked out from the fleet, never stored.
- **A ship's type** (`ShipNames.TypeOf`) is the one it was bought as, while it is cached by that; after the next start's
  sync, which caches its registration role instead (B25), what its frame makes it: `FRAME_PROBE` a probe,
  `FRAME_FRIGATE` the command frigate, `FRAME_SHUTTLE` a light shuttle, `FRAME_LIGHT_FREIGHTER` a light hauler, a
  `FRAME_DRONE` a mining drone, a siphon drone or a surveyor by its mount, and so on. Both give a bought ship the same
  type, so it keeps its name across the restart.
- **Each type has a list of names** (`ShipNames.Lists`, one for every type a shipyard sells): probes after probes and
  telescopes (SPUTNIK, VOYAGER, …), mining drones after what digs (PICKAXE, MOLE, …), siphon drones after what sips
  (MOSQUITO, HUMMINGBIRD, …), surveyors after their instruments, the command frigate after flagships, shuttles after small
  birds, haulers after pack animals, and so on. No name is in two lists. Each server reset picks one name of each list,
  by its reset date (FNV-1a, so every start of the reset picks the same); on 2026-10-04 the command frigate is INTREPID,
  the probes MARINER, the mining drones PICKAXE and the siphon drones HUMMINGBIRD.
- **The number** counts the ships of the type in the order they joined the fleet, which is the order of the game's own
  numbers, in hexadecimal (SPECTER-F before SPECTER-10): MARINER-1 is the starting probe, MARINER-2 the next bought.
  Ships only join the fleet, so a name never changes during a reset.
- **A type with no list** (a frame no shipyard type has yet) is named after its registration role: `PATROL-1`.
- **Where it shows** (D72: beside the symbol): `ShipName` on every log line about a ship (`ShipNameEnricher`), and the
  `ShipPurchased` line says it; `spacetraders_ship_name_info{ship,name,type}`, which the SpaceTraders dashboard's Fleet and
  Roles tables show in a "name" column, and its journals in front of a line (gembernodes); the ship list of the internal
  API (`/status/ships`, `name`), which the WebUI's fleet page shows, and searches, and the ship's page.
- **The name book** (`IShipNameBook`, in memory) keeps the names for the log lines, which can't read the fleet: startup
  recovery, a purchase, the ship list and the metrics every 10 seconds tell it the fleet. A ship it hasn't been told of
  yet has no `ShipName`; before agent bootstrap has picked the agent, nothing has one.

---

## 5. Events

### Published at runtime

| Event | Published by | Handled by |
|---|---|---|
| `ShipInTransitEvent` | Navigate, startup recovery | Dashboard notification; `activity_logs` row |
| `ShipArrivedEvent` | `ShipEventScheduler` | `ShipArrivedEventHandler` → `NavigateToWaypointArrivedCommand` |
| `MarketDataRefreshedEvent` | Arrival at a market; the market watch; a refresh after a trade (D25) | `MarketPriceSampleHandler` → `market_price_samples`, one row per good (B19, fixed) |
| `ShipNavigationCompletedEvent` | Arrival, after docking (`NavigateToWaypointArrivedCommand`) | `ShipNavigationCompletedHandler` → one goal step |
| `ShipRefueledEvent` | Refuel | `LedgerEntryHandler` → `ledger_entries` (FuelPurchase) |
| `ShipJumpedEvent` | `JumpGoalExecutor` (slice 6.11) | `LedgerEntryHandler` → `ledger_entries` (AntimatterPurchase, one unit of ANTIMATTER at the gate it left) |
| `ShipCargoSoldEvent` | Mining, siphon, spare-time and trade executors; carries the market it was sold to (`WaypointSymbol`) | `LedgerEntryHandler` (TradeSell, with that market and the unit price since B57, and `spacetraders_goods_sold_units_total`); `activity_logs` row |
| `CargoPurchasedEvent` | Trade and construction executors | `LedgerEntryHandler` (TradeBuy, or ConstructionBuy for a construction trip's materials, slice 6.6; and `spacetraders_goods_bought_units_total`) |
| `ConstructionSuppliedEvent` | Construction executor, per supply (slice 6.6) | `activity_logs` row |
| `NewShipPurchasedEvent` | `ShipPurchaseService` | `LedgerEntryHandler` (ShipPurchase); `activity_logs` row |
| `ContractAcceptedEvent` | Contract plan | `LedgerEntryHandler` (ContractDeposit, unless it paid nothing); `activity_logs` row |
| `ContractFulfilledEvent` | `FulfillContractDeliveryCommand` | `LedgerEntryHandler` (ContractPayout); `activity_logs` row |
| `AgentCreditsChangedEvent` | Every credit change but sync and registration | `AgentCreditsSampleHandler` → `agent_credits_samples`; `CreditHistoryHandler` (in memory). There is no credit-drop alert: credits only drop when the bot spends them (D12) |
| `ShipStateMismatchEvent` | State-gated commands | `activity_logs` row |
| `TokenResetMismatchDetectedEvent` | Agent bootstrap | `AlertHandler` (webhook); `activity_logs` row |
| `ShipCooldownExpiredEvent`, `ApiUnavailableEvent`, `ApiAvailableEvent` | Scheduler, tick | Nothing |

### Handled but never published

- **Ledger and activity events:** repairs, mounts and modules (nothing calls those API
  operations).
- **Alerts:** contract deadlines, reset warnings, cache divergence.
- **Activity-log-only events:** arrivals, idle ships, assignments, low fuel, contract
  negotiation, acceptance and delivery, automation paused and resumed. (Construction supplies are
  published since slice 6.6.)
- **Contract plan events:** `DeliverableObtainedEvent` and `ContractDeliveryRecordedEvent`.

### Never dispatched

Domain aggregates (`Agent`, `Ship`, `Contract`) raise events into a list that nothing reads, and
production code doesn't use the aggregates at all. The events are published where the change
happens instead (B7, fixed).

### Wolverine details

- **Discovery:** Wolverine finds handlers by convention (class names ending in `Handler` or
  `Consumer`). The `Handle` methods on `ContractPlanService` are therefore not wired; the trading,
  survey, mining, siphon and construction plans have none (since slices 6.5, 6.4, 6.7 and 6.6). `DiValidationTests`
  checks this, because a plan that is switched off could otherwise still run from an event.
- **`InvokeAsync`** runs a command inline, and its exceptions reach the caller. Executors and
  the tick use it.
- **`PublishAsync`** goes through in-memory local queues. All events use it, as does
  `NavigateToWaypointArrivedCommand`.

---

## 6. Data

### Schema

- There are no EF migrations. `SpaceTradersDatabaseInitializer` creates the database and all
  of the model's tables when none of them exist yet; unlike `EnsureCreated`, other tables in the
  database don't stop it (B21). It doesn't upgrade an existing schema: a database from before
  slice 1.4 has to be dropped.
- Agent bootstrap seeds the settings of the agent it picks.
- A column or table added after the cluster's tables exist is added at every start by a statement
  that is safe to repeat (`AddedSchema`): `cached_surveys."Extractions"` (slice 6.4),
  `agent_settings."FollowsDefault"` and `next_run_settings` (D69), in one transaction with the
  one-off judging of the settings stored before D69 (see
  [How settings work](#how-settings-work)). `DatabaseInitializerTests` checks each against what the
  model creates.
- Wolverine stores nothing in the database.

### Agent scoping

- Every table but `scheduled_ship_events` and `next_run_settings` has an `AgentId` column: the agent's symbol and the
  server's reset date, such as `GEMBER@2026-09-27` (`AgentIdentity`). A token keeps the id it
  was first stored under. Where a table has a natural key, the id is part of it.
- EF filters every query on the active agent's id. `startup_snapshots`, `agent_credits_samples`,
  `scheduled_ship_events` and `next_run_settings` have no filter.
- The token itself is stored only in `stored_credentials`.
- The database keeps one agent: at startup, agent bootstrap deletes every other agent's rows,
  except their `runs` (`AgentDataCleanup`). Pruning then only has the active agent's rows to deal
  with. `next_run_settings` has no agent, so the cleanup leaves it (D69).

### Retention

- `DataRetention` lists every table, with a policy that prunes it or the reason it can't grow
  without bound ("bounded"). `DataRetentionTests` fails for a table that isn't listed.
- `DataRetentionService` prunes each table in its own scope at start and then every 24 hours. A
  table that fails is logged and the others carry on.
- Pruning covers every agent's rows, so it doesn't wait for agent bootstrap. Next to it, agent
  bootstrap deletes every other agent's rows at startup, except their `runs`.
- Downsampling ranks the rows in one pass (`row_number()`). On 2.6 million samples (30 markets,
  20 goods, 90 days), the `NOT IN` it replaces didn't finish within 3 minutes; this takes about 5
  seconds, within the 30 s command timeout.

### Database size guard

`DatabaseSizeGuardService` keeps the bot from filling the Postgres volume it shares with every
other app (D8):
- It reads `pg_database_size` at start, before the rest of startup goes on, and then every 5
  minutes, and exports it as `spacetraders_db_size_bytes`.
- Each limit, `Database.SoftLimitMegabytes` (1024) and `Database.HardLimitMegabytes` (3072), is
  an anomaly while the database is above it, with rule `DbSizeSoftLimit` or `DbSizeHardLimit` and
  subject `database`. The journal logs `AnomalyRaised` when the database crosses a limit (Warning
  for the soft one, Error for the hard one) and `AnomalyCleared` when it is back under, and
  `spacetraders_anomaly_active` is 1 meanwhile. The other anomalies come from the health rules
  (section 12); these two stay in the guard, because the hard limit has to act before the tick
  starts.
- Above the hard limit it also switches `Automation.Enabled` off, and again at every check while
  the database stays above it (with an Error line each time it has to), so switching it back on
  only lasts once the database is smaller.

### Tables

| Table | Holds | Written by | Retention |
|---|---|---|---|
| `stored_credentials` | Agent tokens, active token marker | Agent bootstrap | bounded: the active agent's token |
| `cached_agents` | Agent (credits, HQ) | Sync, bootstrap, purchases, sales, refuels, contract payments | bounded: one row |
| `cached_ships` | Ship state and the active goal | Sync (game state only), ship commands, goal repository | bounded: one row per ship. Created with `fillfactor=50` and `autovacuum_vacuum_threshold=10`, so its frequent updates stay in place and VACUUM runs (B32) |
| `cached_contracts` | Contracts | Sync, bootstrap, contract plan, delivery | bounded: the agent's contracts |
| `cached_markets`, `cached_shipyards` | Market and shipyard JSON | Sync, arrivals, the explore plan; markets also the market watch and the refresh after a trade (D25), shipyards also purchases | bounded: one row per market or shipyard. The repositories store a row in one statement (`INSERT … ON CONFLICT … DO UPDATE`), so two writers at once update one row (B61). The API lists a shipyard's ships for sale only while one of our ships is there; an answer without them leaves a row that has them as it is (B64), as an answer without prices does a market's (B62) |
| `cached_waypoints`, `cached_systems` | Systems where ships are, with each waypoint's traits and modifiers (B34, fixed) | Sync (inserts, and fills in missing traits); scouting sets `LastObservedAt` | bounded: the systems the fleet has been in |
| `agent_settings` | Settings, each with whether it follows its default (`FollowsDefault`, D69) | Seed, `PUT /settings`, the control endpoints, the size guard, the reset monitor | bounded: one row per setting |
| `next_run_settings` | The values chosen for the next runs; no agent (D69) | `PUT /settings`, `PUT` and `DELETE /settings/next-run/{key}`, the control endpoints; `POST /settings/reset` empties it | bounded: at most one row per seeded setting |
| `ship_assignment_records` | Scout and contract assignment per ship | Scout and contract plans | bounded: one row per ship |
| `plan_states` | Plan JSON per plan type | All eleven plans, the role board included | bounded: one row per plan |
| `scheduled_ship_events` | Arrival timers | Navigate | bounded: deleted when fired |
| `activity_logs` | Activity log | `LogActivityHandler`: transit, state mismatch, token reset | `ActivityLog.RetentionDays` (30) |
| `ledger_entries` | Credit ledger, each row booked to the ship it is about (the contract's payments to `AGENT`); the metrics sum it by ship and category every 10 s (slice 2.16) | `LedgerEntryHandler`: refuels, sales, cargo and ship purchases, the jump gate's materials (`ConstructionBuy`, slice 6.6), contract payments; a sale and a cargo purchase with their market, good, units and unit price (sales since B57) | 30 days |
| `cached_surveys` | Surveys, with who took them, when, and how many extractions used them (`Extractions`, slice 6.4) | `SurveyKeeper`: the survey executor stores, extractions count, refusals remove | bounded: the survey plan removes expired surveys on every pass (journaling each), and an extraction refusal removes its survey |
| `leader_leases` | Leader lease | Leader election | bounded: one row |
| `api_endpoint_usages` | Call count per endpoint string | Every outbound call once the agent is known | bounded: one counter per endpoint |
| `runs` | Run summaries | Run lifecycle | 365 days, every agent's |
| `run_credit_highlights` | Start/end credits per run | Run lifecycle | 365 days |
| `startup_snapshots` | One full JSON snapshot per start and per discovery, with why (`Reason`) and what was new (`Discovered`) | Startup snapshot, discovery snapshots (slice 2.15) | the agent's first and the last 10 |
| `market_price_samples` | Price history | `MarketPriceSampleHandler`, one row per good on every market refresh | 7 days raw, first per hour to 90 days |
| `agent_credits_samples` | Credits over time | `AgentCreditsSampleHandler`, on every credit change | 7 days raw, first per hour to 90 days |
| `ship_task_records` | Task timeline | Nothing | 30 days |
| `ship_goal_history` | Ended goals | Nothing | 30 days |
| `fleet_goals` | Fleet goals | Nothing | completed ones 30 days |
| `trade_opportunities` | Trade routes | Nothing | bounded: replaced as a whole |
| `cached_construction_sites` | Construction sites: the home system's jump gate, with each material's required and fulfilled units | `ConstructionSites` (slice 6.6): the construction plan's fetches, and every supply's answer | bounded: one row per site |
| `scheduled_runs` | Runs to start later | Nothing | bounded: deleted when promoted |

---

## 7. Settings and configuration

### How settings work

- Settings live in `agent_settings` and are read from the database on every use (no cache), so a
  change applies on the next read.
- **Defaults:** `DefaultSettingsSeed` holds every setting's default. Every plan is on by default
  (D69, asked on 2026-10-04; until then only the scout and contract plans were, D9).
- **A setting follows its default** while nobody has set it: seeded from its default, it gets the
  default as the running version has it at every start, so a default that changes reaches the agent
  that runs (`FollowsDefault`). Set through `SettingsRepository`, by these endpoints, the control
  endpoints, the size guard or the reset monitor, it keeps its value until it is set again. Of the
  settings stored before D69, the start that adds the column takes one whose value isn't its default
  as set, and lets the rest follow, with the plan switches that are off: D69 switched their defaults
  on. So that start switches on the plans the reset of 2026-10-04 13:00Z left off, and keeps what was
  changed before it. The `Runtime.*` status flags never follow.
- **The next run** is the agent the next server reset registers. It starts with the value chosen for
  it for each setting, else with the default (D69). A chosen value lasts until it is changed, for
  every run after; it belongs to no agent (`next_run_settings`). Only settings the seed holds can be
  chosen, and no status flag.
- `PUT /settings/{key}` with `{"value": "…"}` writes any key, even an unknown one, and for a setting
  the seed holds also chooses that value for the next runs (D69). The kill switch
  (`POST /control/automation/enable`, `/disable`) does the same for `Automation.Enabled`. When the
  bot sets a setting itself (the size guard and the reset monitor switch automation off), the next
  runs keep their value.
- `GET /settings/next-run` lists every setting a run starts with, the value the next run starts
  with and whether that is the default (`isDefault`). `PUT /settings/next-run/{key}` with
  `{"value": "…"}` chooses a value for the next runs only, and leaves the run that runs now alone;
  `DELETE /settings/next-run/{key}` gives the next runs the default again. Both answer 404 for a key
  the seed doesn't hold and for a status flag. For example, to trade at 200 credits a unit now and
  try 300 in the next run, `PUT /settings/Trade.MinProfitPerUnit` with `200`, then
  `PUT /settings/next-run/Trade.MinProfitPerUnit` with `300`; in that order, since the first also
  sets the next runs.
- `POST /settings/reset` gives every setting its default back, to follow it from then on, and
  forgets the values chosen for the next runs.
- Every change, whoever makes it (these endpoints, the control endpoints, the size guard, the
  reset monitor, a start that gives a setting its new default), is a `SettingChanged` journal line
  with `Setting`, `OldValue` and `NewValue` (`SettingsRepository`), and every change of a value
  chosen for the next runs a `NextRunSettingChanged` line, `(default)` where none is chosen. A key
  that may hold a secret (ending in `Url`, or naming a secret, password or API key) shows `(hidden)`
  instead of its value.
- The SpaceTraders dashboard's **Settings** table shows every setting with its value now, the value
  the next run starts with (D69) and what it does (`spacetraders_setting_info`, section 11): the
  switches first, on or off, then the rest by name; the `Runtime.*` status flags are left out. A
  value that may hold a secret is hidden there too.
  What a setting does comes from the running version's seed: a stored description is the one the
  setting was seeded with, which an older version may have written (slice 2.9).

### What each setting does

The seed holds 54 settings: the 39 that change what the bot does (8 of them the health rules'
thresholds), 3 that are read without changing it, and 12 status flags. Settings that nothing read, or only code that never runs, were
removed from it in slice 2.6 (B18, D10); `DefaultSettingsSeedTests` pins the list.

| Setting (default) | Effect |
|---|---|
| `Automation.Enabled` (true) | Off: no plans, goal steps or contract work, whatever would trigger them. Startup recovery skips. |
| `Automation.Plan.Scout.Enabled`, `.Contract.Enabled`, `.Explore.Enabled`, `.Roles.Enabled`, `.ProbeDeployment.Enabled`, `.Survey.Enabled`, `.Mining.Enabled`, `.Siphon.Enabled`, `.Construction.Enabled`, `.Trading.Enabled`, `.SpareTime.Enabled` (true, D69) | Off: the plan isn't bootstrapped, buys nothing and its ships' goals wait (D9). With the survey plan on, a ship that can survey only surveys (D20); with the spare-time plan on too, the command ship trades or mines and siphons when it has nothing to survey (D34–D37). With the role board on, every ship works for the plan of the role the board gives it instead (D38–D41). With the construction plan on, the largest hold builds the home system's jump gate while it needs materials (slice 6.6, D64–D68) |
| `Construction.Ships` (1) | Ships that build the home system's jump gate while it needs materials: the largest holds that aren't drones, probes or the surveyor. The role board gives them the construction role; with the board off, the construction plan picks them (D65) |
| `Roles.ReconsiderMinutes` (10) | Minutes between the role board's evaluations of the whole fleet; a new ship, a plan switched, the contract starting or stopping to want ore, the home gate starting or stopping to need materials, or a ship left without work weighs the roles at once (D41) |
| `Roles.HeadStartPercent` (20) | Percent more a ship's current role counts on the role board, so close calls don't flip back and forth (D41); 0 means none |
| `Roles.ChainValueSharePercent` (50) | Percent of the price difference to the pricier good a market makes from what a ship sells it that the role board counts, and that share again of the step after; fully while the market is SCARCE of it, not at ABUNDANT (D39); at most what the trip earns on a unit (D49); 0 means none |
| `Automation.CircuitBreaker.MaxGoalStepsPerMinute` (60) | Goal steps per ship per minute above which the circuit breaker blocks the goal |
| `Api.BadGatewayPauseMinutes` (3) | Minutes without any API call after a 502 |
| `Database.SoftLimitMegabytes` (1024), `Database.HardLimitMegabytes` (3072) | Database size above which the size guard warns, or switches automation off (D8) |
| `FleetExpansion.MinCreditReserve` (60000) | Credits every ship purchase must leave with no ship that trades: the floor of the credit reserve (D51). Seeded at 100,000 before slice 6.10b; a stored value stays as it is |
| `FleetExpansion.ReservePerTradingCargoUnit` (1000) | Credits the credit reserve grows by for every unit of hold on the ships that trade: the cargo ships, the command ship and any ship the role board has trading (D51); 0 means the floor only |
| `Mining.MaxDrones` (20) | Cap on drones bought by mining automation |
| `Siphon.MaxDrones` (10) | Cap on the siphon drones the siphon plan keeps; it buys one only for a market short of a gas (D32) |
| `Survey.StockPerOre` (2) | Usable surveys the survey plan keeps of each ore at its asteroid; with that many for every ore, the surveyors wait until one runs out (D27) |
| `Trade.MinProfitPerUnit` (200) | Credits per unit, after the fuel for the whole trip, a trade trip must earn to be started, and to be carried on when prices change (D14); 0 means any profit, but see D15 |
| `Trade.FuelReserveCredits` (5000) | Credits a cargo purchase must leave, so ships can always buy fuel: below them only fuel is bought (D24) |
| `Trade.ShipPurchases` (`SHIP_LIGHT_SHUTTLE,SHIP_LIGHT_HAULER,SHIP_LIGHT_HAULER`) | The cargo ships the trading plan buys, in order: the Nth while the fleet has fewer than N cargo ships (D21); empty buys none |
| `Market.RefreshMinutes` (5) | Minutes between refreshes of a market where one of our ships is (the market watch); 0 switches it off. Also how old a market's prices may get before a roaming probe flies there (slice 6.3); at 0 that is 5 |
| `ActivityLog.RetentionDays` (30) | Activity log retention |
| `Alerts.WebhookUrl` (empty) | Where alerts are posted, as `{"text": …}`. Only the token-reset alert can fire. |
| `Health.*` (8 settings) | The health rules' thresholds; see [Health rules](#12-health-rules). Changing one doesn't start a new run |

**The other 15:**

- **Read without changing what the bot does:**
  - the run's strategy label: `FleetExpansion.PreferredShipType`, `Automation.MiningShipPercentage`;
  - the market views: `Trade.MaxHaulDistance`, read by queries over the never-written
    `trade_opportunities` (as is `Trade.MinProfitPerUnit`, which the trader reads too).
- **Status flags, not settings to tune** (moving them out of the settings is a separate cleanup):
  - Read by the endpoints but never written: `Runtime.Reset.Next`, `Runtime.Alert.ApiUnavailable`,
    `CacheDivergence`, `ContractDeadlinesApproaching`, `ResetUpcoming`.
  - Written by bootstrap: `Runtime.Alert.TokenResetMismatch`.
  - Written but never read: `Runtime.TokenResetMismatchDetected`, `Runtime.AutomationPausedByReset`,
    `Runtime.Alert.AutomationDisabled`.
  - Read by nothing: `Runtime.Reset.Warning`, `Runtime.ApiUnavailable`,
    `Runtime.CacheDivergenceDetected`.
- **Read but not seeded:** `BudgetPolicy` reads `Construction.FabMatsBuyThreshold`,
  `FabMatsTransactionSize` and `HourlyBudgetCapEnabled`, and nothing uses the result. Code that
  never runs reads `Navigation.*` (`NavigationPlanningService`, never called) and
  `Maintenance.*` (`FleetMaintenancePlanner`, not registered); without a seeded value they read
  as 0 or false.

### Configuration (appsettings, user secrets, environment)

| Key | Used for |
|---|---|
| `ConnectionStrings:DefaultConnection` | PostgreSQL, for EF Core and Wolverine. Required. |
| `SpaceTraders:AccountToken`, `AgentName`, `AgentFaction`, `AgentToken` | Agent bootstrap and registration |
| `SpaceTradersApi:BaseUrl` | Game API base URL (`https://api.spacetraders.io/v2/`) |
| `SPACETRADERS_AGENT_TOKEN` | Initial agent data scope |
| `SPACETRADERS_INTERNAL_API_KEY` | Required `X-Api-Key` for the internal API when set |
| `WebUI:Origin` | CORS origin for the dashboard |
| `Metrics:Port`, `Metrics:Hostname` | Where `/metrics` is served: 9090 on all interfaces; 0 means no metrics server. Development uses `localhost` |
| `Serilog:*` | Log levels |
| `ASPNETCORE_ENVIRONMENT` | `Development`: user secrets and Swagger. `Production`: JSON logs. `Testing`: no database work. |

---

## 8. Outbound API client (`SpaceTraders.Infrastructure.SpaceTradersAPI`)

**Handler pipeline,** outermost first:

Steps 2 to 4 follow the API guide (https://spacetraders.io/api-guide/rate-limits, D3).

1. **`ApiRequestInitiatedHandler`** (slice 2.10): counts every request the bot initiates, once, as
   it starts, in `spacetraders_api_requests_initiated_total{method,endpoint}`, by route template
   like step 5. It comes before the pause and the budget, so a request still waiting for the
   budget, or one the pause refuses, counts here before step 5 counts it going out, or without it
   ever going out; a retry of a 429 counts again in step 5, not here. The dashboard's "API request
   rates" sets the two side by side.
2. **`OutagePauseHandler`:** a 502 comes from the API's DDoS protection, and the guide asks to
   wait a few minutes. So after a 502 no call goes out for `Api.BadGatewayPauseMinutes`
   (default 3): calls in that time fail at once with `ApiPausedException`. The first call after
   the pause goes out as usual; a success marks the API available, another 502 pauses again.
3. **`RateLimitResponseHandler`:** a 429 with `x-ratelimit-*` headers comes from the API's rate
   limiter: it waits until `x-ratelimit-reset` (else `retry-after`, else 1 s; at most a minute)
   and retries. A 429 without them comes from the cloud infrastructure: it backs off 1, 2, 4, 8
   and 16 s. Either way it gives up after five retries. Every 429 is counted and logged at
   Warning with the limiter's headers (`x-ratelimit-*` and `retry-after`, "none" without them,
   B59), and the headers are recorded for `/status/rate-limit`. A 429 from the rate limiter also
   holds every other request back until its reset (`RequestBudget.PauseUntil`, B59): the server's
   budget is empty for them too, and they used to go out and draw 429s of their own.
4. **`RateLimitingHandler`:** each request takes from `RequestBudget`, a singleton: 2 requests
   in any second and, once those are used, up to 30 more in any 60 seconds. It waits only when
   both are used. Each window is 100 ms longer (`JourneyMargin`, B59): a request that leaves a
   second after the one two before it can still reach the server less than a second after it. A
   new process starts with its burst spent (`RequestBudget.ForANewProcess`, B59), as the server
   still counts what the process before it sent in the last minute, so it goes at 2 a second for
   its first minute. Writes (anything but GET: moving a ship, trading) go before reads (D19): a read
   gives way while a write waits for the budget, leaves the last 10 of the burst to writes
   (`WriteReserve`), and stops giving way after 10 seconds (`MaxReadDelay`), so reads can't
   starve. Time spent waiting is counted in `spacetraders_api_rate_limit_wait_seconds_total`, by
   `kind`: `read` or `write`.
5. **`ApiRequestMetricsHandler`:** counts every request that goes out, retries included, in
   `spacetraders_api_requests_total{method,endpoint,status}`, with the route template as
   `endpoint` (`my/ships/{shipSymbol}/navigate`, `ApiEndpointTemplate`) and `error` as status when
   no response came. A 429 is also counted in `spacetraders_api_throttled_total{source}`:
   `rate_limiter` with the `x-ratelimit-*` headers, `infrastructure` without. Every 401 and 429
   also goes to `ApiResponseLog` (in memory, the last hour) for the API health rules.

**Other parts:**

- **Availability:** `ApiAvailabilityState` tracks availability and the pause after a 502. The
  tick does no work during the pause.
- **Alerts:** `WebhookAlertNotifier` posts alerts to `Alerts.WebhookUrl` and ignores errors.
- **Server reset:** every failed call goes through one place in `SpaceTradersApiClient`. A 401
  with "Token reset_date does not match the server" goes to `ServerResetMonitor`, which switches
  `Automation.Enabled` off, logs `ResetDetected` at Critical and stops the host. It ignores
  reports until startup has completed: agent bootstrap tries old tokens on purpose.
- **Jumps** (slice 6.11): `POST my/ships/{ship}/jump` sends the destination gate's `waypointSymbol`, as API v2.3.0
  asks (B60: it sent the destination's `systemSymbol`; nothing jumped before slice 6.11). A client error from the jump (a 4xx other than
  401 and 429) comes back as `JumpRefusedException`, so the jump executor can tell a refused jump from an outage.
- **Tokens:** status, systems and waypoints calls go without a token, `register` uses the account
  token, and everything else uses the agent token.
- **Usage counts:** every call increments `api_endpoint_usages`, except the calls agent bootstrap
  makes before it knows the agent.
- **`ISpaceTradersPort`** has 43 operations:
  - status, agent and registration;
  - ships and cargo;
  - navigate, dock, orbit, flight mode, warp, jump and refuel;
  - buy, sell and jettison;
  - extract, survey and siphon;
  - contracts: list, accept, deliver, fulfil and negotiate;
  - systems, waypoints, markets, shipyards, jump gates, charts and construction;
  - ship purchase, repair, scrap, mounts and modules.

---

## 9. Internal HTTP API (`/spacetraders/api`)

**Authentication:** everything except `/health/*` needs `X-Api-Key` when
`SPACETRADERS_INTERNAL_API_KEY` is set, the SignalR hub included. When the key is unset,
everything is open. `/metrics` isn't on this port: see [Hosting](#hosting-spacetradersapiprogramcs).

| Group | Endpoints | Notes |
|---|---|---|
| Health | `GET /health/live`, `/ready`, `/startup`, `/automation`, `/rate-limit/history` | No key needed |
| Status | `GET /status/agent`, `/ships`, `/ships/{s}/diagnostics`, `/waypoints/{s}`, `/contracts`, `/rate-limit`, `/activity?page&size&ship`, `/mining-opportunities`, `/trading-routes`, `/startup-snapshots` (+ `/{id}/download`), `/system-alerts` | Cached data; `/ships` with each ship's `name` (slice 2.14); `/startup-snapshots` with each snapshot's `reason` and `discovered`, and the download named `startup-snapshot-…` or `discovery-snapshot-…` (slice 2.15); `/trading-routes` with the trading plan's routes in its order (slice 2.17) and why the other goods with a price gap aren't traded, `notTraded` (slice 2.18) |
| Status (empty) | `GET /status/trade-opportunities`, `/top-trade-routes` | Read tables that are never written; always 204, `[]` or zeros |
| Status (credit growth) | `GET /status/anomalies` | A heuristic over the credit samples: credits per hour over the last 24 hours against the last hour. Not the health rules' anomalies (section 12) |
| Universe | `GET /universe/systems`, `/jump-connections` | Jump connections are always `[]` |
| Runs and finance | `GET /runs/{id}/kpis`, `/finance/trade-routes` | KPIs is a stub; trade routes are always `[]` |
| Fleet | `GET /fleet/assignments`, `/activity`, `/activity/{ship}`, `/activity/{ship}/history`, `/goal-chains` | 5 s cache. History is always `[]` |
| Markets | `GET /markets/waypoints`, `/waypoints/{s}`, `/freshness`, `/goods/{s}/prices`, `/waypoints/{s}/prices`, `/best-routes` | Best routes always 204 |
| Shipyards | `GET /shipyards/waypoints`, `/waypoints/{s}`, `/freshness` | |
| Settings | `GET /settings`, `PUT /settings/{key}`, `POST /settings/reset`; `GET /settings/next-run`, `PUT /settings/next-run/{key}`, `DELETE /settings/next-run/{key}` | `PUT /settings/{key}` also sets the next runs; the `next-run` endpoints only the next runs, and answer 404 for a key a run doesn't start with (D69) |
| Control | `POST /control/automation/enable`, `/disable` | Sets `Automation.Enabled`, now and for the next runs (D69) |

**SignalR** (`/hubs/dashboard`): the server broadcasts `ReceiveInvalidation({Kind, Id, OccurredAt})`
for ships, activity, assignments, goal chains, contracts and markets. Clients can't call
anything.

---

## 10. WebUI (`SpaceTraders.WebUI`)

- **Stack:** React 19, Vite, TanStack Query, react-router and Tailwind. It is served by nginx at
  `/spacetraders/dashboard/`. nginx doesn't proxy the API; the ingress routes
  `/spacetraders/api` to the API service.
- **Configuration:** the container entrypoint writes `config.js` with the API base URL, the hub
  URL and the API key, so the key is readable by anyone who can load the dashboard (B22). In
  development, Vite proxies `/spacetraders/api` to `https://localhost:49305`.
- **Data fetching:** only GET requests, with the `X-Api-Key` header. Queries refetch after 30 s.
  SignalR refresh hints probably never match (B24).

**Routes and the endpoints they read:**

| Route | Reads |
|---|---|
| `/` Overview | `/status/agent`, `/status/ships`, `/status/rate-limit`, `/status/system-alerts` |
| `/plans` | `/fleet/goal-chains`, `/fleet/assignments`, `/fleet/activity`, `/status/mining-opportunities` |
| `/fleet`, `/fleet/:symbol` | `/status/ships`, diagnostics, waypoints; each ship's name beside its symbol, which the search finds too (slice 2.14) |
| `/markets` | Market and shipyard waypoints and freshness |
| `/snapshots` | Snapshots: when, why, what a discovery found, and a download of each |
| `/health` | `/status/rate-limit`, `/health/automation`, `/health/rate-limit/history` |
| `/settings` | `/settings` (read-only page) |

The seven pages in `src/Future` are not routed.

---

## 11. Logging and metrics

- **Logs:** console only. Kubernetes and Promtail pick them up, and Loki keeps them for 31 days.
  - Production writes JSON with the rendered message (`RenderedCompactJsonFormatter`: `@m`).
  - Information is for what happens: a ship docks, sells, is bought; a plan starts, advances,
    completes or starts waiting. What a tick finds when nothing changed (no idle ship, no budget,
    a plan already complete) goes to Debug, and so does the "starting" line of a ship command
    whose result line follows. An idle bot logs nothing at Information.
  - A flight logs two lines at Information (B53): `NavigateSubCommand` when it leaves (from, to,
    when it arrives) and `ShipNavigationCompletedHandler` when it lands (with what its goal did
    next), plus `RefuelSubCommand` when it refuels, which is a purchase. The steps between, each
    handler and sub-command saying the ship left, orbited, was woken, arrived, docked or had its
    market refreshed, log at Debug: they were about 80% of the bot's lines with twelve ships. A
    flight that fails still logs its warning or error.
  - One property name per concept: `ShipSymbol`, `ContractId`, `WaypointSymbol` (unless the
    message names a role, such as `Destination` or `SellWaypoint`), `GoalKind`.
  - Wolverine logs each handled message ("Successfully processed message …") under the message
    type's name, which the `Wolverine` level override doesn't reach. The Wolverine setup puts
    that line at Debug (`MessageSuccessLogLevel`, B29); the handlers log what happened themselves.
  - Every line logged during a tick carries `Tick`; a step's lines also carry its `Plan`, or its
    `ShipSymbol` (and `ContractId`). The game loop sets these with `ILogger.BeginScope`, which
    Serilog turns into properties.
  - Every line carries `ResetDate` (slice 2.13, D70): the server reset the agent was registered
    under, such as `2026-10-04`, read from the agent id (`SPECTER@2026-10-04`) by `ResetDateEnricher`.
    The bot registers the same symbol after every reset, so the agent's and its ships' symbols repeat
    from run to run; the reset date doesn't, and the dashboards' Loki queries filter on it. Lines
    logged before agent bootstrap has picked the agent (the start's first lines) have none.
  - A line about a ship, one with a `ShipSymbol` from its message or from the tick's scope, carries `ShipName`
    beside it (slice 2.14, D72): the name the bot gives the ship, such as `PICKAXE-2`, from the name book
    (`ShipNameEnricher`; see [Names](#names-shipnames-slice-214)). A ship the book hasn't been told of has none.
- **The journal:** one line per meaningful thing, with an `EventKind` property and a message that
  starts with it (`CargoSold: ship …`), so `{namespace="spacetraders"} | json | EventKind != ""`
  in Loki reads as a timeline of the run. `JournalEvents` names every kind:

  | Kind | Logged by | Properties |
  |---|---|---|
  | `ContractAccepted` | Contract plan | `ContractId`, `TradeSymbol`, `WaypointSymbol`, `Payment` |
  | `ContractDelivered`, `ContractFulfilled` | `FulfillContractDeliveryCommand` | `ContractId`, `ShipSymbol`, `TradeSymbol`, `Units`, `WaypointSymbol`; `Payment` |
  | `ShipPurchased` | `ShipPurchaseService` | `ShipSymbol`, `ShipType`, `WaypointSymbol`, `Cost`, `ShipName` (slice 2.14) |
  | `CargoBought`, `CargoSold` | Trade, mining, siphon and spare-time executors; `CargoBought` also the construction executor (slice 6.6) | `ShipSymbol`, `TradeSymbol`, `Units`, `WaypointSymbol`, `Cost` or `Revenue` |
  | `TradeStarted` | Trading plan | `ShipSymbol`, `TradeSymbol`, `Units`, `BuyWaypoint`, `SellWaypoint`, `SellPrice`, `FuelCost` (the whole trip, the flight to the buy market included), `ExpectedProfit`; `BuyPrice` for a purchase; `FeedsTradeSymbol` when the sell market makes a pricier good from it |
  | `TradeRerouted` | Trade executor, at the sell market | `ShipSymbol`, `TradeSymbol`, `WaypointSymbol`, `SellPrice`, `SellWaypoint`, `NewSellPrice`, `FuelCost`, `Reason` |
  | `TradeDropped` | Trade executor | `ShipSymbol`, `TradeSymbol`, `WaypointSymbol`, `SellWaypoint`, `Reason`: `not_lucrative` (with `Units`, `BuyPrice`, `SellPrice`, `ExpectedProfit`, `MinProfitPerUnit`; when not even a unit earns the minimum, `BuyPrice`, `SellPrice`, `Margin` and `MinProfitPerUnit`), `not_possible` or `not_bought_here` (also when a market stops buying part way through a sale) |
  | `Surveyed` | `SurveyKeeper`, one per survey a ship takes (slice 6.4) | `ShipSymbol`, `WaypointSymbol`, `TradeSymbol` surveyed for, `Signature`, `Size`, `Deposits` (`COPPER_ORE x2, IRON_ORE`), `Expiration` |
  | `SurveyEnded` | `SurveyKeeper`: the survey plan for expired surveys, the extraction command for refused ones | `Signature`, `WaypointSymbol`, `Size`, `Reason` (`expired`, `exhausted`, `not_verified`), `Extractions` made with it, `ShipSymbol` that took it, `SurveyedAt` |
  | `Extracted` | `MineResourceVolumeCommand`, per extraction; `ExtractResourcesCommand`, per spare-time extraction (slice 6.8) | `ShipSymbol`, `Units`, `TradeSymbol` it got, `WaypointSymbol`, `Target` it mines for (`whatever sells` in spare time), `Signature` of the survey (empty without one) |
  | `MiningStarted` | Mining plan (a trip), contract plan (a miner joining, D23) | `ShipSymbol`, `TradeSymbol`, `WaypointSymbol` it mines at, `SellWaypoint`, `Reason` (`held_cargo`, `surveyed`, `low_supply`, `lowest_supply`, `uncovered`, `shared`, `collection` for a drone's place at a collection point (slice 6.18), `contract`); `ContractId` for the contract |
  | `CargoTransferred` | Drone executor at a collection point, per good handed over (slice 6.18, D83) | `ShipSymbol`, `TargetShipSymbol` (the shuttle), `TradeSymbol`, `Units`, `WaypointSymbol` (the asteroid) |
  | `CollectionStarted` | Mining plan, for each round of a collecting shuttle (slice 6.18, D83) | `ShipSymbol`, `WaypointSymbol` (the asteroid), `SellWaypoint`, `Reason` (`collection`) |
  | `Siphoned` | `SiphonResourcesCommand`, per siphon (slice 6.7) | `ShipSymbol`, `Units`, `TradeSymbol` it got, `WaypointSymbol`, `Target`: the gas its trip is for (`whatever sells` in spare time) |
  | `SiphonStarted` | Siphon plan (a trip) | `ShipSymbol`, `TradeSymbol`, `WaypointSymbol` it siphons at, `SellWaypoint`, `Reason` (`held_cargo`, `low_supply`, `lowest_supply`, `uncovered`, `shared`) |
  | `DriftStarted` | Mining and siphon executors, when a trip's way to a market out of its ship's CRUISE reach sets off on a leg in DRIFT (slice 6.10c, D45, D84); the move executor, when a ship that can only survey does so on its way to where most drones mine (D54); a drone parked at a far asteroid, on its way there (D83) | `ShipSymbol`, `WaypointSymbol` it leaves, `Leg` where the drift ends, `SellWaypoint` it is on its way to, `TradeSymbol`, `SourceWaypoint` it gathers at from there; for a move, `Destination`; for a parked drone, `SourceWaypoint` is the asteroid it is on its way to |
  | `GatheringStarted` | Spare-time plan (a trip, slice 6.8) | `ShipSymbol`, `WaypointSymbol` it gathers at, `Method` (`mines`, `siphons`) |
  | `GatheringInterrupted` | Survey and trading plans, taking a ship off a spare-time trip that fills its hold (D34, D37) | `ShipSymbol`, `WaypointSymbol` it gathered at, `Reason` (`survey`, which keeps the hold aboard; `trade`, which sells it first), `Units` aboard |
  | `RoleChanged` | Role board, for each ship whose role changes (slice 6.9) | `ShipSymbol`, `OldRole`, `NewRole` (`Survey`, `Mine`, `Siphon`, `Trade`, `Construct`, `Collect`, `None`), `Reason` (`only_role`, `survey_first`, `contract`, `coverage`, `gathers_first`, `construction`, `collection`, `most_profitable`, `no_work`, `no_role`); for a role chosen by profit, `CreditsPerHour` and `Job`: the trip that decided it |
  | `CargoJettisoned` | `CargoJettison`: the trading and spare-time plans, for cargo nothing will sell or use (D42) | `ShipSymbol`, `Units`, `TradeSymbol`, `WaypointSymbol`, `Reason` (`no_buyer`, `not_worth_the_fuel`) |
  | `TripEnded` | `TripBook`, when a trade, mining, siphon, spare-time, construction or collecting trip ends, and at each contract delivery (D46) | `ShipSymbol`, `Activity` (`trade`, `mining`, `siphoning`, `spare_time`, `contract`, `construction`, `collecting`), `Earned`, `Spent`, `FuelCost`, `Profit`, `Minutes`, `Reason` (`sold`, `delivered`, `interrupted`, `runaway`, `rejected`, `nothing_aboard`, `no_buyer`, `not_bought_here`, `not_lucrative`, `not_possible`; for construction `supplied`, or why it was dropped) |
  | `ConstructionStarted` | Construction plan, for each trip (slice 6.6) | `ShipSymbol`, `TradeSymbol`, `Units`, `BuyWaypoint`, `WaypointSymbol` (the site), `Reason` (`purchase`, with `BuyPrice`, `Cost` with its fuel and `FuelCost`; `held_cargo` for materials the ship already holds) |
  | `ConstructionSupplied` | Construction executor, per supply (slice 6.6) | `ShipSymbol`, `TradeSymbol`, `Units`, `WaypointSymbol`, and the site's `Fulfilled` and `Required` units of it afterwards |
  | `ConstructionDropped` | Construction executor, when a trip is given up (slice 6.6) | `ShipSymbol`, `TradeSymbol`, `WaypointSymbol` where the ship is, `SiteWaypoint`, `Reason` (`not_needed`, `not_sold_here`, `low_supply`, `over_budget`, at the market before it bought anything: D81); at Warning, when the API refused the supply, `WaypointSymbol` (the site), `Units` kept aboard and `Reason` (`not_needed`, `wrong_location`) |
  | `ProbeCalled` | Probe plan, when it sends a probe to a shipyard where a purchase waits for one of our ships (D30) | `ShipSymbol`, `WaypointSymbol`, `ShipType` the purchase is for |
  | `Jumped` | `JumpGoalExecutor`, per jump (slice 6.11) | `ShipSymbol`, `WaypointSymbol`: the gate it left, `Destination`: the gate it jumped to, `SystemSymbol` it is in now, `Cost` of the antimatter |
  | `SystemExplored` | Explore plan, when the command ship has scouted a system, or is in one with no market or shipyard (slice 6.11) | `ShipSymbol`, `SystemSymbol`, `Markets`, `Shipyards` |
  | `Discovered` | `GameStateSnapshots`, when the cache lists a ship type or good no snapshot of the run held, and a snapshot was saved (slice 2.15) | `Discovered`: each new ship type and good with where it is listed; `SnapshotId` |
  | `PlanStarted`, `PlanCompleted` | Scout, contract, probe and explore plans; the construction plan when it first sees the home gate need materials, and when it is complete (slice 6.6) | `Plan`, and the plan's ship, contract or system; the explore plan's start also `Purpose` (`explores`, `flies home to`), the `SystemSymbol` it goes to and its `Jumps`, its completion the systems `Explored`; for construction the site (`WaypointSymbol`) and, at the start, its `Materials` |
  | `PlanBlocked` | Contract plan (`unsupported_deliverable`, `no_ship_or_budget`, `no_asteroid`), probe plan (`waiting_for_credits`), explore plan (`waiting_for_credits`, with the jump's `WaypointSymbol`, `Destination`, `SystemSymbol`, `Price`, `Floor` and `Credits`; `no_way_home` at Warning) | `Plan`, `Reason` |
  | `ShipIdle` | `ShipStateJournal`, from the 10 s sample: `idle_at_start`, `new_ship`, `goal_ended` (with `PreviousGoal`) | `ShipSymbol`, `Reason` |
  | `ShipBlocked` | The circuit breaker (Warning); `JumpGoalExecutor`, for a jump the API refused (`jump_refused`, Warning, slice 6.11) | `ShipSymbol`, `GoalKind`, `Reason`; a refused jump has `WaypointSymbol` and `Destination` instead of `GoalKind` |
  | `SettingChanged` | `SettingsRepository`; agent bootstrap, for a setting it gives its new default (D69) | `Setting`, `OldValue`, `NewValue` |
  | `NextRunSettingChanged` | `SettingsRepository`, for a value chosen for the next runs (D69) | `Setting`, `OldValue`, `NewValue`; `(default)` where none is chosen |
  | `ResetDetected` | `ServerResetMonitor` (Critical) | `Detail` |
  | `ApiUnavailable`, `ApiAvailable` | The tick | `PausedUntil` |
  | `AnomalyRaised` | The health monitor (Warning) and the size guard | `Rule`, `Subject`; the monitor's also `Details` |
  | `AnomalyCleared` | The health monitor and the size guard | `Rule`, `Subject`; the monitor's also `ActiveMinutes` |

  Mining, siphoning, trading and spare time have no plan to start or complete. Mining journals each trip
  (`MiningStarted`, `DriftStarted` for a trip to a market out of CRUISE reach, `Extracted` per extraction,
  `CargoSold`), and each survey's life (`Surveyed`, then `SurveyEnded`); siphoning each trip
  (`SiphonStarted`, `DriftStarted` likewise, `Siphoned` per siphon, `CargoSold`); spare time
  each trip (`GatheringStarted`, `Extracted` or `Siphoned`, `CargoSold`, or `GatheringInterrupted`);
  trading journals each trip: `TradeStarted`, then `CargoBought` and `CargoSold`, with
  `TradeRerouted` or `TradeDropped` when prices change. Each of those trips ends with a `TripEnded` line: what it
  made after fuel (D46). Construction journals the gate (`PlanStarted`, `PlanCompleted`) and each trip:
  `ConstructionStarted`, `CargoBought`, then `ConstructionSupplied` or `ConstructionDropped`, and `TripEnded`
  (slice 6.6). The role board journals each role that changes
  (`RoleChanged`), and cargo that goes overboard because nothing will sell or use it is a
  `CargoJettisoned` line (D42). Exploring journals each jump (`Jumped`) and each system scouted
  (`SystemExplored`), between the explore plan's `PlanStarted` and `PlanCompleted`. A ship type or good
  new to the run is a `Discovered` line, with the snapshot taken for it (slice 2.15).
- **Metrics** on the metrics port (`Metrics:Port`, 9090), without the API key.
  `PrometheusAutomationMetrics` defines them all at startup, so a scrape lists every one, also
  before it has a value; prometheus-net adds its defaults (process, .NET and HTTP metrics, and the
  .NET meters, Wolverine's among them). Labels stay low-cardinality: a few per ship or contract
  at most.

  Every `spacetraders_*` series carries `reset_date` as its first label (slice 2.13, D70): the server
  reset the agent was registered under, such as `2026-10-04`, the same value as the log lines'
  `ResetDate`. The agent's symbol and its ships' symbols are the same in every run, so without it a
  counter of the new run looked like the old one reset, and a graph or an `increase()` across the
  reset mixed both runs. `ResetDateLabel` reads it from the agent id at every write; a reset ends the
  process, so a process has one value once agent bootstrap has set it. What is written before that
  (bootstrap's own API calls) carries an empty value, which Prometheus stores as no label. The three
  SpaceTraders dashboards filter every query on it with a "Reset" picker, the newest run first.

  Every `spacetraders_*` counter series reaches Prometheus at 0 before it counts anything
  (`ZeroFirstCounter`, B43). Prometheus's `increase()` and `rate()` never count the value a series
  has when it is first scraped, so a series that held its first increment then (the contract's
  deposit and the first drone, booked before the pod's first scrape; any single 429 or breaker
  trip) never showed on the dashboard. A new series is published at 0, and what it counts waits
  until a scrape has exported that 0: up to two scrape intervals (2 minutes on the cluster). Locally,
  a new series shows its count from the second `curl` of `/metrics` after it appeared.

  A gauge without labels (`spacetraders_agent_credits`, `spacetraders_credit_reserve`, `spacetraders_db_size_bytes`,
  `spacetraders_server_next_reset_timestamp_seconds`) is the opposite: its 0 would be read as a
  value, so it has no series until the bot first sets it (B52); its `# TYPE` line is there from the
  start. Prometheus's first scrape of a new pod can come before the first sample, and the dashboard
  read 0 credits for a minute after a deploy, which "Value gained per hour" showed as a loss of the
  whole fleet's value, and an hour later as the same gain. Now that minute is a gap.

  | Metric | Labels | What it counts or shows | Updated |
  |---|---|---|---|
  | `spacetraders_agent_credits` | | The agent's credits, as cached | Every 10 s (`PrometheusMetricsService`) |
  | `spacetraders_credit_reserve` | | The credits a ship purchase must leave (D51): `FleetExpansion.MinCreditReserve`, and `FleetExpansion.ReservePerTradingCargoUnit` for every unit the ships that trade can carry; the dearest full hold a trader saves up for (D56); and what the trade and construction trips on their way to buy hold back (D57, D64) | Every 10 s |
  | `spacetraders_purchase_need_credits` | `plan`, `tier`, `position`, `ship_type`, `shipyard` | What each plan that buys ships would buy now (slice 6.10b, D43), one series per plan, worth the ship's price as cached; `tier` is its place in the order ships are bought in (`Contract`, `Surveyor`, `Coverage`, `SurveyorPerArea`, `CargoShips`, `Construction`, `Probes`, `Alternating`) and `position` the same as a number, 1 first (D55 put `SurveyorPerArea` at 4, so the cargo ships moved from 4 to 5; slice 6.6 put `Construction` at 6, so probes moved to 7, the turns to 8). Construction's series is the gate's next load: the material as `ship_type`, its market as `shipyard`, worth the load with its fuel. A plan that needs nothing has no series | Every 10 s, from `PurchaseNeeds` |
  | `spacetraders_ships` | `role`, `state` | Ships by type as cached (B25) and by `DOCKED`, `IN_ORBIT` or `IN_TRANSIT` | Every 10 s |
  | `spacetraders_ship_status_since_timestamp_seconds` | `ship`, `role`, `state`, `goal`, `reason` | One series per ship. `goal` is the goal's kind, else the assignment's type (`Contract`), else `None`; `reason` says why a goal is blocked (`runaway`). The value is when the ship entered this combination (Unix time, since the start at the latest), so `time() - …` is the time in state | Every 10 s |
  | `spacetraders_ship_info` | `ship`, `location`, `activity` | One series per ship, always 1. `location` is the waypoint and its type, such as `X1-AB-A1 (ASTEROID)`, or in transit `→` and where it goes; `activity` is what the bot has it do: its goal in a few words (`scouting`; `drifting to X1-AB-B7 to mine GOLD_ORE` for a trip's drift, slice 6.10c; `drifting to X1-AB-B7` for the survey ship's move, D54; `buying FAB_MATS at X1-AB-F49 for X1-AB-I55` and `supplying FAB_MATS to X1-AB-I55` for a construction trip, slice 6.6), else its contract work (`mining COPPER_ORE` at the contract's source, `delivering COPPER_ORE` at its destination, `on the way to …` between them), else `idle`, or `blocked (…)` | Every 10 s |
  | `spacetraders_ship_name_info` | `ship`, `name`, `type` | One series per ship, always 1 (slice 2.14, D72): the name the bot gives it beside the game's symbol, such as `MARINER-2`, and the type that name is for: the shipyard type (`SHIP_PROBE`), else its registration role. No series before the agent is known. The Fleet and Roles tables' "name" column | Every 10 s |
  | `spacetraders_ship_arrival_timestamp_seconds` | `ship` | When a ship in transit arrives (Unix time); no series otherwise | Every 10 s |
  | `spacetraders_ship_cargo_units`, `spacetraders_ship_cargo_capacity_units` | `ship`, `good`; `ship` | A ship's hold per good aboard, and what it takes | Every 10 s |
  | `spacetraders_ship_value_credits` | `ship` | What was paid for a ship and the mounts and modules installed on it, from the ledger; 0 for a starting ship. The dashboard's "Total value" | Every 10 s |
  | `spacetraders_ship_ledger_credits` | `ship`, `category` | A ship's ledger since it joined the fleet, summed by ledger category (slice 2.16): what it earned positive (`TradeSell`, every sale, traded, mined or siphoned), what it spent negative (`ShipPurchase`, `TradeBuy`, `FuelPurchase`, `AntimatterPurchase`, `ConstructionBuy`, …). One series per category the ship has rows of, so summed by `ship` it is what the ship has made. The contract's payments are booked to `AGENT`, no ship, so no series has them. The ledger keeps 30 days, longer than a reset lasts. The dashboard's "Profit by ship" | Every 10 s, one query grouping the ledger by ship and category |
  | `spacetraders_contract_units_required`, `_units_fulfilled` | `contract`, `trade_symbol` | The accepted contracts' deliverables | Every 10 s |
  | `spacetraders_contract_deadline_timestamp_seconds` | `contract` | An accepted contract's deadline (Unix time) | Every 10 s |
| `spacetraders_construction_units_required`, `_units_fulfilled` | `site`, `trade_symbol` | The home system's jump gate (slice 6.6), per material: the units it needs and the units supplied, by anyone, as cached (complete or not). A material no longer cached loses its series. The dashboard's jump gate panels | Every 10 s |
  | `spacetraders_credits_earned_total` | `source` | Credits earned, by ledger category | With each ledger row (`LedgerEntryHandler`) |
  | `spacetraders_credits_spent_total` | `category` | Credits spent, by ledger category | With each ledger row |
  | `spacetraders_api_requests_initiated_total` | `method`, `endpoint` | Requests the bot initiated to the game API, by route template: once each, as it starts, before the pause after a 502 and the local budget, so retries don't count again (slice 2.10). Above `spacetraders_api_requests_total`, requests wait for the budget or the pause refuses them; below it, 429s are retried | Per request, as it starts |
  | `spacetraders_api_requests_total` | `method`, `endpoint`, `status` | Requests to the game API by route template, retries included | Per request |
  | `spacetraders_api_throttled_total` | `source` | 429s: `rate_limiter` or `infrastructure` | Per 429 |
  | `spacetraders_api_rate_limit_wait_seconds_total` | `kind` | Time requests waited for the local budget: `read` (GET, which gives way to writes, D19) or `write` | Per request that waited |
  | `spacetraders_messages_handled_total` | `type` | Messages Wolverine handled without an error (`MessageMetricsMiddleware`) | Per message |
  | `spacetraders_goal_steps_total` | `kind` | Goal steps run | Per step |
  | `spacetraders_goal_breaker_trips_total` | `ship` | Goals the circuit breaker blocked | Per trip |
  | `spacetraders_extracted_units_total` | `ship`, `good` | Units extracted: each extraction's yield (`MineResourceVolumeHandler`, and `ExtractResourcesHandler` in spare time, slice 6.8), and each siphon's (`SiphonResourcesHandler`, slice 6.7), so the dashboard's mined panels show gases too | Per extraction or siphon |
  | `spacetraders_jettisoned_units_total` | `ship`, `good` | Units jettisoned: extracted goods the trip doesn't mine for; siphoned goods, and goods a spare-time extraction got, that no market the ship can reach buys; cargo nothing will sell or use (D42) | Per jettison |
  | `spacetraders_extractions_total` | `ship`, `surveyed` | Extractions, with a survey (`true`) or without one (`false`): many without one while a surveyor works means too few surveys (slice 6.4). Siphons aren't extractions here: they can't take a survey; nor are spare-time extractions (slice 6.8), which take none by design | Per extraction |
  | `spacetraders_surveys_taken_total` | `waypoint`, `size` | Surveys our ships took | Per survey |
  | `spacetraders_surveys_ended_total` | `waypoint`, `reason`, `used` | Surveys that ended: `expired`, `exhausted` or `not_verified`; `used` when any extraction used it. Many that expire unused mean too many surveys | Per survey |
  | `spacetraders_surveys_active` | `waypoint`, `used` | Usable surveys in the cache, used yet or not | Every 10 s |
  | `spacetraders_anomaly_active` | `rule`, `subject` | 1 while an anomaly is active, then 0: the health rules (section 12) and the size guard's limits | Every minute (health rules), every 5 minutes (size guard) |
  | `spacetraders_db_size_bytes` | | `pg_database_size` | Every 5 minutes (size guard) |
  | `spacetraders_server_next_reset_timestamp_seconds` | | When the server resets next (Unix time), from `GET /` | At agent bootstrap |
  | `spacetraders_market_observed_timestamp_seconds` | `system`, `waypoint`, `waypoint_type` | When the bot last refreshed a cached market | Every minute (`PrometheusMarketMetricsService`) |
  | `spacetraders_market_purchase_price`, `_sell_price`, `_trade_volume` | `system`, `waypoint`, `good`, `kind` | A good at a market as last seen: what the market charges, what it pays, its trade volume; `kind` is `EXPORT`, `IMPORT` or `EXCHANGE`. Only once a ship has been there, and only in home and the systems where a ship that doesn't explore is: a system the command ship has only explored keeps its markets' refresh times and its `spacetraders_system_*` summary below, not each good's series (slice 6.11; exploring has no limit, and at about a thousand series a system Prometheus would grow without end) | Every minute |
  | `spacetraders_market_supply`, `_activity` | `system`, `waypoint`, `good`, `kind` | Supply 1 `SCARCE` to 5 `ABUNDANT`; activity 0 `RESTRICTED`, 1 `WEAK`, 2 `GROWING`, 3 `STRONG`. The same systems as the prices | Every minute |
  | `spacetraders_shipyard_observed_timestamp_seconds` | `system`, `waypoint`, `waypoint_type` | When the bot last refreshed a cached shipyard | Every minute |
  | `spacetraders_shipyard_ship_type` | `system`, `waypoint`, `ship_type` | 1 for each ship type a shipyard sells | Every minute |
  | `spacetraders_shipyard_ship_price`, `_ship_supply` | `system`, `waypoint`, `ship_type` | A ship type's price and supply (1 to 5) as last seen, once a ship has been there | Every minute |
  | `spacetraders_shipyard_ship_fuel_capacity_units`, `_ship_cargo_capacity_units` | `system`, `waypoint`, `ship_type` | What a ship type's tank holds (its frame's `fuelCapacity`; 0 for a probe) and what its cargo holds take together, as the shipyard last listed it, once a ship has been there (slice 2.11) | Every minute |
  | `spacetraders_shipyard_ship_info` | `system`, `waypoint`, `ship_type`, `can`, `equipment` | One series per ship type a shipyard listed in full, always 1 (slice 2.11). `can` is what it could do in the fleet, judged as `spacetraders_ship_capabilities_info` judges a ship (`FleetRoles.PotentialRoles`, by its type, mounts, hold and tank), such as `Mine, Trade`, but `Probe` for a probe, which the probe plan buys and flies; `equipment` is its mounts, then its modules, each in symbol order and without its `MOUNT_` or `MODULE_` prefix, such as `MINING_LASER_I, MINERAL_PROCESSOR_I`, without the cargo holds (the hold shows them) and crew quarters; `none` without any. The markets dashboard's shipyards table | Every minute |
  | `spacetraders_system_info` | `system`, `state`, `gate`, `gate_state` | One series per system the bot knows (slice 6.11, `SystemOpportunities`), always 1. `state` is what the explore plan knows of it: `home`, `explored`, `to_explore`, `gate_under_construction`, `jump_refused`, `no_gate`, `gate_unknown`, or `cached` for one it has waypoints of but the plan doesn't know; `gate` its jump gate and `gate_state` (`active`, `under_construction`, `none`, `unknown`). The systems dashboard's table | Every minute (`PrometheusMarketMetricsService`) |
  | `spacetraders_system_jumps_from_home` | `system` | Jumps from home through built gates, one more to a gate still under construction; no series for a system no known gate leads to | Every minute |
  | `spacetraders_system_explored_timestamp_seconds` | `system` | When the command ship explored a system (Unix time); home's is when the plan first ran | Every minute |
  | `spacetraders_system_connection_info` | `system`, `to` | One series per connection the plan asked a gate for, always 1: `to` is the system the gate leads to | Every minute |
  | `spacetraders_system_facilities` | `system`, `kind` | A system's cached waypoints with a `market`, a `shipyard`, or still `uncharted` (their traits hidden, D62) | Every minute |
  | `spacetraders_system_waypoints` | `system`, `type` | A system's cached waypoints by type (`ASTEROID`, `GAS_GIANT`, `JUMP_GATE`, ...) | Every minute |
  | `spacetraders_system_gathering_sites` | `system`, `good` | In how many of a system's waypoints a good can be mined, by the asteroids' deposit traits (as the mining plan reads them), or siphoned (each gas giant, for `HYDROCARBON`, `LIQUID_HYDROGEN` and `LIQUID_NITROGEN`). An uncharted asteroid doesn't count: its deposits are hidden | Every minute |
  | `spacetraders_system_raw_good_price` | `system`, `good`, `market` | The best price a market in a system pays for an ore or a gas it imports or exchanges, as last seen, and which market | Every minute |
  | `spacetraders_system_raw_good_supply` | `system`, `good` | The lowest supply of that ore or gas among the system's markets that buy it, 1 `SCARCE` to 5 `ABUNDANT`: the scarcer, the more a delivery pays | Every minute |
  | `spacetraders_system_trade_margin`, `_trade_volume` | `system`, `good`, `buy_at`, `sell_at` | The five best trades within a system, one per good: what a unit earns before fuel, buying where the good is cheapest and selling where it pays most, and the units one trade moves (the smaller of the two markets' trade volumes) | Every minute |
  | `spacetraders_good_supply_chain` | `good`, `made_from`, `used_for` | One series per good, always 1: the goods it is made from and the goods made from it, comma-separated (`GET market/supply-chain`) | Once per start |
  | `spacetraders_ship_role_info` | `ship`, `role`, `reason` | One series per ship on the role board (slice 6.9), always 1: its role (`Survey`, `Mine`, `Siphon`, `Trade`, `Construct`, `None`) and why (`survey_first`, `coverage`, `gathers_first`, `construction`, `most_profitable`, ...). Only while the board is on: switched off, the plans don't read its roles. The dashboard's roles table | Every 10 s, from the board's state |
  | `spacetraders_ship_role_credits_per_hour` | `ship`, `role` | What each role a ship could take but surveying and constructing would earn it per hour, by the board's estimate of its best trip; 0 for a role without a trip. Only while the board is on | Every 10 s |
  | `spacetraders_setting_info` | `setting`, `current`, `next_run`, `description` | One series per setting the agent has, always 1: its value now (`true` or `false` for a switch; `(hidden)` for a key that may hold a secret, as in `SettingChanged`), the value the next run starts with, hidden alike (D69; empty for a status flag or a key the seed doesn't hold), and what it does, from the running version's seed, else as stored. The dashboard's settings table (slice 2.9) | Every 10 s |
  | `spacetraders_ship_capabilities_info` | `ship`, `can` | One series per ship, always 1: the roles its equipment allows whichever plans are on (`FleetRoles.PotentialRoles`), in the order `Survey` (a surveyor mount, or a bought SHIP_SURVEYOR), `Mine` (a mining laser, or a bought mining drone or ore hound, with a hold and a tank), `Siphon` (a gas siphon, or a bought siphon drone, with a hold and a tank), `Trade` (a hold and a tank), `Construct` (a hold and a tank, and no drone, slice 6.6); `none` for a probe or a ship with none. Mining and siphon drones are both cached as EXCAVATOR, the game's registration role, which the `role` label of `spacetraders_ships` shows; this tells them apart (slice 6.10a). The fleet and roles tables' "can do" column | Every 10 s |
  | `spacetraders_goods_sold_units_total`, `spacetraders_goods_bought_units_total` | `system`, `waypoint`, `good` | Units our ships sold to, or bought from, a market, whoever traded them (trade, mining, siphon and spare-time trips; only traders buy). The same labels as the market gauges without `kind`, so what we sell into a market can be set against the supply, trade volume and price of what it makes (D50) | Per sale or purchase (`LedgerEntryHandler`) |
  | `spacetraders_trips_total`, `spacetraders_trip_profit_credits_total`, `spacetraders_trip_loss_credits_total` | `activity` | Trips that ended, and what they made or lost after fuel, by `trade`, `mining`, `siphoning`, `spare_time`, `contract` or `construction` (D46; construction only ever loses, slice 6.6): a trip adds its profit to one counter and 0 to the other, so both series exist and profit − loss is what the activity made. A contract's deposit and payout count as its profit; each delivery's round trip, its fuel as a loss | Per trip end (`TripBook`); contract payments in `LedgerEntryHandler` |

---

## 12. Health rules

The bot checks itself (`../PLAN.md` phase 3). `HealthMonitorService`, the last step of the startup
chain, evaluates every health rule (`IHealthRule`, in `SpaceTraders.Application/Health`) against the
bot's own state: at start, then every minute.

- **An anomaly** is a subject that breaks a rule: a ship, a contract, the agent, `api` or a log
  statement. When it starts, the journal logs `AnomalyRaised` at Warning, with `Rule`, `Subject`
  and `Details` (what the rule saw, its limit and the setting that holds it), and
  `spacetraders_anomaly_active{rule,subject}` turns 1. When the rule holds again, the journal logs
  `AnomalyCleared` (`ActiveMinutes`) and the metric turns 0. Grafana emails once an anomaly has
  been active for 15 minutes (gembernodes, slice 2.5).
- **Anomalies live in memory.** After a restart the first evaluation raises the ones still there
  again. A rule that throws is logged at Error; its anomalies stay as they are until it runs
  again, and the other rules carry on.
- **Clocks:** a rule that expects something within a time starts counting no earlier than when the
  monitor saw the bot able to work on the plan concerned: automation and the plan on, and API calls
  not paused after a 502, at every evaluation since (at most since the monitor started). Switching
  automation off and on, or a restart, never looks like a stall; after a restart detection starts
  over.
- **Every instance** runs the monitor, like the size guard and the metrics: the rules read the
  shared database.
- **Thresholds are settings** (`Health.*`); a missing, zero or negative value means the default.
  The defaults are Claude's, from the soak test (1.14) where there was data; they are yours to
  change.

| Rule | Subject | Intended behaviour (`PLAN.md` 3.2) | Broken when | Setting (default) |
|---|---|---|---|---|
| `ContractStalled` | contract | An accepted contract makes progress | The contract the bot works on gets no delivery for N hours, counted from the contract plan's last change (a delivery, or the plan starting or starting to wait). In the soak test a delivery took 23 to 69 minutes | `Health.Contract.MaxHoursWithoutProgress` (4) |
| `ContractLeftOpen` | contract | A fulfilled contract has no active plan or assignment (B9) | 5 minutes after a contract is fulfilled, the contract plan still holds it, or a ship still has an open assignment for it. Not while the contract plan is off: then nothing closes them, on purpose | — |
| `ContractDeadlineAtRisk` | contract | A deadline within N hours comes with at least X% delivered | From N hours before its deadline, the contract the bot works on has less than X% of its units delivered; or it missed its deadline, whatever was delivered | `Health.Contract.DeadlineHours` (24), `Health.Contract.MinDeliveredPercent` (50) |
| `ShipStuck` | ship | A ship with a goal changes state, unless it's in transit | A ship with work, not in transit, isn't updated by the bot (nav, cargo, fuel or cooldown: its `LastSyncedAt`) for N minutes, at two evaluations in a row. Counted from its arrival at the earliest. A blocked goal is `CircuitBreakerTripped`'s | `Health.Ship.MaxMinutesWithoutChange` (30) |
| `ShipLeftIdle` | ship | A ship without a goal is idle for at most N minutes, while work waits for it (D13) | A ship with no goal and no assignment, not in transit, stays so for N minutes while a plan that is on has work it could give that ship (below) | `Health.Ship.MaxIdleMinutes` (10) |
| `CircuitBreakerTripped` | ship | The circuit breaker hasn't tripped (slice 1.2) | A ship's goal is blocked (`runaway`), or the ship tripped the breaker in the last hour | — |
| `RepeatingError` | log statement | The same error repeats at most N times in 10 minutes | One statement (its class and message template) logs more than N warnings or errors in 10 minutes, since startup. Journal lines don't count: other rules watch what they report | `Health.Errors.MaxRepeatsIn10Minutes` (5) |
| `CreditsUnchanged` | agent | Credits change at least once a day while the fleet works (D13) | Ships have had work at every evaluation for N hours, and the credits didn't change in that time (no credit sample, B7) | `Health.Credits.MaxHoursUnchanged` (24) |
| `ApiUnauthorized` | `api` | No 401 or reset errors | The API answered a call with 401 in the last hour, since startup. A reset stops the host (slice 1.8), so this is a token the server rejects for another reason | — |
| `ApiThrottled` | `api` | At most N 429s an hour | More than N 429s in the last hour, retries included, by source. None are expected (slice 1.10) | `Health.Api.Max429sPerHour` (10) |
| `DbSizeSoftLimit`, `DbSizeHardLimit` | `database` | The database stays under its limits (D8) | The size guard's own checks, every 5 minutes; see [Database size guard](#database-size-guard) | `Database.*` |

**Work** is a goal, or a contract assignment, of a plan that is on (`ShipStuck`,
`CreditsUnchanged`). For `ShipLeftIdle`, the work a plan has waiting, mirroring which ships it
treats as free (section 3):

- scout: the active plan's next stop, for the plan's ship;
- contract: the active plan's contract, for the plan's ship and, while units remain, for any other
  miner (D23); while the plan waits for a ship or budget, any miner. With the role board on, a miner is
  any ship that can mine and doesn't have the survey role (D40);
- survey: a target to survey, for a ship that can survey (D20), or, with the role board on, a ship with
  the survey role (slice 6.9), that the plan lists as able to reach it (B55); for a ship that can only
  survey, any target it can reach, as it surveys on (D52);
- probe deployment: a market whose prices are due, with no probe at it or on its way and no other
  ship of ours at it, for any probe (slice 6.3, D29); the starting probe is one (B25). A probe parked
  at its market while every market is watched is idle by design;
- mining: a low-supply opening without a ship (Pending), for a miner the plan lists as able to reach
  its asteroid (slice 6.4). A drone whose tank can't reach the open ones is idle by design;
- siphon: likewise, a gas opening without a ship, for a siphoner the plan lists as able to reach its
  gas giant (slice 6.7);
- trading: a lucrative route without a trader (Pending), for a ship the plan lists as able to take it:
  one it can fly, and that is lucrative from where the ship is (slice 6.5). A drone whose tank can't
  reach the open routes is idle by design;
- spare time: a place to mine or siphon, for a ship the plan lists with one (slice 6.8). The plan gives
  such a ship a trip at once, so only a plan that has stopped leaves it idle; one it lists as waiting has
  nowhere to gather and sell;
- construction: a load of materials for the home system's jump gate, for a builder the plan lists as one a
  load waits for (`ReadyShipSymbols`, slice 6.6): the order ships are bought in lets construction buy, and the
  credits pay for a load from where the builder is going. The plan gives a free one its load at once, so only a
  plan that has stopped leaves it idle; a builder waiting for credits, supply or trade volume trades meanwhile.

Without such work an idle ship is idle by design: in the first run (D9, D1) the starting probe,
the command ship after scouting and the drone after its contract. Likewise an idle fleet isn't
expected to change the credits.

**The contract the bot works on** is the contract plan's, while the plan is Active, waits for a ship
or budget (PendingBudget), or found no asteroid for its mineral; and while that contract is accepted
and unfulfilled. A contract whose deliverable isn't a mineral is parked by decision (D2; a gas too,
D31) and left out.

**In-memory signals,** all since the process started:

- `ErrorLog`: a Serilog sink (`ErrorLogSink`) hands it every warning and error without an
  `EventKind`, by class and template, for the last 10 minutes. The 429 handler logs a warning per
  retry, so a burst of 429s can raise `RepeatingError` next to `ApiThrottled`.
- `ApiResponseLog`: `ApiRequestMetricsHandler` records every 401 and 429, for the last hour.
- `GoalStepCircuitBreaker.LastTrips`: each ship's last trip.

---

## 13. Code that exists but never runs

This makes the codebase look bigger than what actually runs:

- `IdleGoalExecutor`, and 6 of the 17 goal kinds: `Idle`, `MineResource`, `SiphonResource`, `SellCargo`,
  `DeliverCargo` and `PatrolMarket` (`SupplyConstruction` runs since slice 6.6).
- `NavigationPlanningService`: registered, but never called.
- `FleetMaintenancePlanner`: not registered.
- `JumpGateCacheService`: no caller.
- `PurchaseShipCommand`: never sent.
- `ContractPlanService.LegacyContractShipPurchaseService`: never instantiated.
- `ShipEventScheduler.ScheduleCooldownExpiryAsync` and `CancelScheduledAsync`.
- `RunRepository.ScheduleRunAsync`.
- `ShipGoalRepository.UpdateGoalStatusAsync`.
- `ShipGoalHistoryRepository.AppendAsync`.
- `TradeOpportunityRepository.ReplaceAllAsync`.
- The `Stateless` package: referenced, but never used.
- The domain aggregates and their events.

---

## 14. Build, tests and deployment

**Images:**
- `Dockerfile.api`: SDK 10 publish, then the ASP.NET 10 runtime on port 8080, and the metrics on 9090.
- `Dockerfile.webui`: Node 22 build, then nginx 1.27 on port 80 with the runtime-config
  entrypoint.

**CI** (`.github/workflows/ci-spacetraders.yml` in the parent repository):
1. Builds the API.
2. Runs `dotnet test SpaceTraders.slnx --filter "Category!=Integration"`.
3. Runs the WebUI tests and build.
4. On `main`, pushes `ghcr.io/gemberkoekje/spacetraders-api` and `-webui`, tagged `latest` and
   with the commit SHA.

**Deployment:** there is no deploy step here. Flux deploys `apps/spacetraders/` from gembernodes
(slice 4.2), and an image reaches the cluster when its commit SHA goes into both deployments there.
- `spacetraders-api`: one pod, replaced with `Recreate`, so there are never two. Its startup probe
  is `/health/startup` (503 until the startup chain has completed), its liveness probe
  `/health/live` and its readiness probe `/health/ready`. Prometheus scrapes port 9090 through
  pod annotations; the Service only routes 8080.
- `spacetraders-webui`: nginx, probed on `/healthz`, which it doesn't log (B40).
- An ingress on the LAN only (D11): `/spacetraders/api` and `/spacetraders/dashboard` on
  http://192.168.1.231.

**Tests:**

| Project | Tests | Covers |
|---|---|---|
| `SpaceTraders.Domain.Tests` | ~63 | Aggregates, events, goal serialization, value objects |
| `SpaceTraders.Application.Tests` | ~700 | Plans (the trade arithmetic included), commands, executors, the health rules and their monitor, budget policy, retry and 429 handlers (NSubstitute, EF in-memory) |
| `SpaceTraders.Infrastructure.Tests` | ~72 | Repositories, the initializer and retention against Testcontainers PostgreSQL (`Category=Integration`) |
| `SpaceTraders.API.Tests` | ~145 | WebApplicationFactory tests in `Testing`: DI validation, bootstrap and run lifecycle, and a broken scenario per health rule in the real host. Also message storage and the agent cleanup against Testcontainers PostgreSQL (`Category=Integration`), and sandbox tests against the live API (`Category=Sandbox`, need `SPACETRADERS_AGENT_TOKEN`). |
| `SpaceTraders.Integration.Test` | 1 | Replays the contract plan from a captured snapshot. No category, so CI runs it. |

The `Category=Integration` tests ask Testcontainers whether it can reach Docker, the way it starts
its containers (`DOCKER_HOST`, the Unix socket, or Docker Desktop's named pipe on Windows), and skip
only when it can't (B36, fixed).

**WebUI tests:**
- `npm test` runs Vitest.
- `npm run test:e2e` runs Playwright against a running stack. Its test opens `/orchestration`,
  which no longer exists (B24).
