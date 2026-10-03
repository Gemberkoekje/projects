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
                  ├─ bootstrap the plans that are on: Scout, Roles, Contract, ProbeDeployment, Survey, Mining, Siphon, Trading, SpareTime
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
  `Application=SpaceTraders.API`. The host logs through its own logger and leaves Serilog's static
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
| 10 | `StartupRecoveryService` | once | Resumes ships; releases the contract's ships (D26) |
| 11 | `SettingsStartupLoggingService` | once | Logs every setting |
| 12 | `GameLoopService` | every 5 s | The tick |
| 13 | `PrometheusMetricsService` | every 10 s | Exports the cached state: credits, ships, contracts |
| 14 | `PrometheusMarketMetricsService` | every minute | Exports the cached markets and shipyards, and once the game's production chains (`GET market/supply-chain`, through `SupplyChainCache`, which the trading plan shares: one call per start, retried hourly after a failure) |
| 15 | `HealthMonitorService` | at start, then every minute | Evaluates the health rules; see [Health rules](#12-health-rules) |

One try/catch wraps the chain. Steps 5, 9, 11 and 15 catch their own errors. A throw in steps 1, 4, 6,
8 or 10 ends the chain: startup is marked failed (`/health/startup` turns Unhealthy) and the host
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
  - seeds any missing settings;
  - records the active token.
- Then it deletes the rows of every other agent, table by table, except their `runs`. A table
  that fails is logged and left for the next start.
- This only happens at startup. A reset during a run is noticed by the API client (see
  [Outbound API client](#8-outbound-api-client-spacetradersinfrastructurespacetradersapi)): the
  host stops, and after the restart this registers the new agent and deletes the old one's rows.
  The new agent's settings start from the defaults, because settings are stored per agent; the
  old values survive only in the settings snapshot of the old agent's runs.

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
  other paths do: shipyards with their prices, so a purchase can read them (B28, fixed), and
  contracts with their terms, so a restart doesn't blank the deliverables the contract plan works
  from (B31, fixed).
- **Updates** the game state of existing ship rows (nav, fuel, cargo, mounts and so on). It
  leaves their goal columns alone, so ships keep their goals across a restart.
- It has no error handling.

**Startup snapshot** (`StartupSnapshotService`): writes one `startup_snapshots` row from what
startup sync has just cached: the agent, the ships with their goals, the contracts, every waypoint
in the ships' systems, and the market and shipyard where each ship is (not in transit). It calls no
API (B35, fixed); before, it fetched all of that again, about 11 calls on every start. The cache
holds less than the API returns: no crew or mount details.

**Startup recovery** (`StartupRecoveryService`): skipped when `Automation.Enabled` is false. With
the contract plan on, it first releases every ship on the contract that isn't in flight, so the
first tick reconsiders it (D26, see
[Contract](#contract-contractplanservice-plus-step-3-of-the-tick)). Then, for each cached ship:

| Ship state | Recovery action |
|---|---|
| Arrival time passed, still marked in transit | Publish `ShipInTransitEvent`, then run one goal step. The code doesn't check "still in transit", and a docked ship keeps its last arrival time, so every docked ship that ever travelled lands here (B38) |
| Still in transit | Publish `ShipInTransitEvent` only |
| Docked or in orbit | Run one goal step |

It doesn't reschedule arrivals. Pending arrivals survive a restart only through
`scheduled_ship_events`.

---

## 2. The tick (`GameLoopService`)

- Runs 5 s after the previous tick ends, on the leader only.
- Each tick:
  1. `EnsureBootstrappedAsync` for each plan that is switched on, in this order: Scout, Roles (the role
     board), Contract, ProbeDeployment, Survey, Mining, Siphon, Trading and SpareTime.
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
  would wipe the cached prices.
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
spare-time plan (slice 6.8) and the role board (slice 6.9) are off too until switched on. A plan that is
switched off:
- isn't bootstrapped, so it doesn't buy anything;
- doesn't move its ships: `ShipGoalExecutorService` skips the goals it gives (scout, probe, survey,
  mine-and-sell, siphon-and-sell, trade, gather-and-sell). They resume when it is switched back on;
- for the contract plan: the tick's contract work (step 3) is skipped too;
- for the role board, which gives no goals: the plans give work by the fixed rules below, whatever roles its
  state still holds.

The plans are bootstrapped in the order of the table, so a plan higher up claims the ships it wants
first. Which plan a ship works for follows from what it can do (`FleetRoles`, slice 6.4): with the
survey plan on, a ship that can survey surveys, and only that (D20), unless the spare-time plan is on
too: then the command ship, with nothing to survey, trades (D34), and with no trade either mines or
siphons in its spare time (slice 6.8); a ship that can mine mines (the contract first, D23, then the
mining plan's trips); a ship that can siphon, and can neither mine nor survey, siphons (slice 6.7);
trading takes what the others leave free, and the spare-time plan what trading leaves. With the role
board on (slice 6.9), which plan a ship works for follows from the role the board gives it instead, by what
it and the other ships can do and what each role would earn: see
[Role board](#role-board-roleplanservice-slice-69).

| Plan | Purpose | Ships it uses | Statuses | Buys |
|---|---|---|---|---|
| Scout | Visit every marketplace in the starting system once | The one ship with fuel | Active → Completed | Nothing |
| Roles | Give every ship the role that earns the fleet most per hour: surveys first, the contract next, the rest by an assignment (slice 6.9, D38–D42) | Every ship but the probes; it gives no goals: the plans below read the roles | Each ship's role, why, and what each role it could take would earn it | Nothing; the drones the mining and siphon plans buy must be worth their role |
| Contract | Fulfil one mineral contract | Every free miner (D23) | PendingBudget, Active, DeferredUnsupported, Completed | One `SHIP_MINING_DRONE` |
| ProbeDeployment | A probe at every market of the HQ system; until then the probes roam between markets, the stalest nearby first (slice 6.3, D29); a purchase where none of our ships is fetches a probe (D30) | Probes | Markets with their probe, the next probe's price, open calls | `SHIP_PROBE`, while there are fewer probes than markets |
| Survey | Survey the contract's ore, else ores the markets buy (slice 6.4) | Ships that can survey (D20) | Targets, best first | Nothing |
| Mining | Mine surveyed ores, else ores in low supply, and sell them (slice 6.4) | Free miners | Low-supply openings (Pending/Assigned) | `SHIP_MINING_DRONE`, up to `Mining.MaxDrones` |
| Siphon | Siphon gases in low supply at gas giants, keep every gas, and sell them (slice 6.7) | Free siphoners: a gas siphon, a hold and a tank, nothing to mine or survey with | Low-supply openings (Pending/Assigned) | `SHIP_SIPHON_DRONE`, up to `Siphon.MaxDrones` (D32) |
| Trading | Carry goods between markets for the most profit after fuel | Ships with a cargo hold and a fuel tank that the plans above leave free | Held and open routes (Assigned/Pending) | Cargo ships, `Trade.ShipPurchases` (D21) |
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

Markets are not scouted again.

### Role board (`RolePlanService`, slice 6.9)

Asked for on 2026-10-02: "Each ship should have a set of potential roles. … A ship should occasionally
consider whether it's role is still the best thing it can do. This is not only based on it's own potential
roles but also of other ships." Off by default (`Automation.Plan.Roles.Enabled`); your decisions are
D38–D42. Bootstrapped after the scout plan and before the plans whose ships it gives roles, it gives no
goals: it decides which plan each ship works for, and the plans read that (`FleetRoleBoard`).

- **Potential roles** (`FleetRoles.PotentialRoles`), by what a ship carries: survey with a surveyor mount
  (or a bought `SHIP_SURVEYOR`, whose mounts aren't recorded yet); mine with a mining laser, a hold and a
  tank; siphon with a gas siphon, a hold and a tank; trade with a hold and a tank. A probe has none: the
  probe plan flies it. A role counts only while its plan is on; mining also while the contract wants ore.
- **Who takes which role** (`RolePlanner`, no I/O), in order:
  1. a ship with one role takes it (`only_role`): a hauler trades, a survey ship surveys;
  2. surveys come first (D38): in a system with a ship that can only survey, that ship surveys and no
     other does. Otherwise, while another ship there can mine (surveys are for miners), the ship that can
     survey with the least to lose surveys (`survey_first`): the one whose best other trip earns least per
     hour. The ship that surveys now keeps it unless another would lose less by more than the head start.
     In X1-DC53 that is the command ship, the only ship that can survey, once a drone can mine;
  3. while the contract plan is on and its contract still wants ore (D40, D23 kept), every other ship that
     can mine mines for it (`contract`);
  4. the rest share the work for the most credits per hour across the fleet (`most_profitable`): each ship
     takes one trip or none, no two the same trade route (D18) or the same mining or siphon opening, by the
     assignment that earns most in total (the Hungarian method, `Assignment`). A ship's current role counts
     `Roles.HeadStartPercent` (20) more (D41), so close calls don't flip back and forth. A ship left
     without a trip keeps its role (`no_work`), or takes its first role but surveying.

  A ship with no role whose plan is on gets `None` (`no_role`), and no plan gives it work.
- **What a role earns** (`RoleEstimator`): the trips its plan would offer the ship, the best 20 per role,
  each valued per hour:
  - trade: every lucrative route from where the ship is, or is going, its profit after fuel, as the trading
    plan reckons it (D14);
  - mine: every mining target (D28's), a full hold of the target ore, filled at the ship's rate times the
    ore's share of the extractions (its survey's deposits, or one of the asteroid's ores without one), less
    the fuel there and on to the market;
  - siphon: every siphon target, a full hold of the gases a market buys (a trip keeps them all, D33), each
    at the best price it fetches, less the fuel;
  - each with the production chains' share (D39, `ChainValues`): a good sold to a market that makes a
    pricier good from it (it imports the good and exports something made from it, by the game's production
    chains, as D15 reads them) counts `Roles.ChainValueSharePercent` (50) of the price difference, and that
    share again of the next step, at the market in the system that makes the most of the pricier good in
    turn (iron ore → iron → machinery). A step counts fully while its market is SCARCE of the input, three
    quarters at LIMITED, half at MODERATE, a quarter at HIGH, and not at all at ABUNDANT. Only the
    comparison of roles reads it: within a role the plans still choose by D15 and D28;
  - a trip's time is its flights in CRUISE as the API reckons them (15 seconds plus the distance times 25
    over the engine's speed, 9 for an engine not cached yet), 10 seconds a landing, and for mining and
    siphoning the cooldowns to fill the hold, with half a tick after each.

  Surveying has no estimate: it comes first.
- **Rates** (`GatheringRates`, in memory): each extraction (for the contract, the mining plan or in spare
  time) and each siphon records its yield and cooldown. A ship's rate is the average of its last 10, else
  of the other ships' of the kind, else 3 units every 70 seconds. The state keeps each ship's rate, which
  the first evaluation after a restart takes back.
- **When** it weighs the roles again (`RoleBoardMemory`, in memory): at the first pass after a start; every
  `Roles.ReconsiderMinutes` (10, D41); at once when a ship joins the fleet, a plan is switched on or off, or
  the contract starts or stops wanting ore; and at once, at most once a minute, when a ship with a choice of
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
  - the trading plan to the ships with the trade role, and those with the mining or siphon role that their
    plan left free;
  - the mining and siphon plans buy a drone only when the board would give it their role (`RoleAdvisor`): a
    drone that would earn more trading would trade, and the plan would buy the next for the same opening.
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
  - **Mining:** `MineResourceVolumeCommand` travels to the asteroid, extracts once per cooldown,
    with the best survey there for the contract's ore when there is one (slice 6.4), and
    jettisons other goods.
  - **Delivery:** `FulfillContractDeliveryCommand` travels to the destination, docks, delivers
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
  purchase must leave `FleetExpansion.MinCreditReserve` (100,000), and needs one of our ships at the
  shipyard (D30). Probes in flight count (B15).
- **Flights** (`ProbePlanner`, no I/O):
  1. a shipyard where a purchase waits for one of our ships (`ShipyardCalls`, D30) gets the nearest
     free probe (`ProbeCalled`), which stays there while the call is open;
  2. every other free probe gets a market that is due, its prices older than `Market.RefreshMinutes`
     (5), with no probe at it or on its way. Each pair of free probe and due market is scored by the
     market's age minus twice the flight there in CRUISE (15 s plus the distance times 25 over the
     engine's speed, 9 for a probe), and the best pair goes first. A market never seen is the oldest.
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
  no other surveyor works on (or the best one when all are taken), from `MiningPlanner.SurveyTargets`.
  The targets are:
  1. the contract's ore at the contract's asteroid, while the contract plan mines it;
  2. each ore a market in the system buys, at the asteroid nearest each market that buys it (D27,
     refined: surveys close to wherever the ore is sold), among those whose traits yield the ore
     (`AsteroidDeposits`) and that one of the miners can reach (any asteroid while there are no
     miners). One target per ore and asteroid, for the market that pays most of those it is nearest;
     each keeps its own stock.

  A target needs a survey while it has fewer usable surveys holding its ore than
  `Survey.StockPerOre` (2, D27). Among those, the contract's ore comes first, then the ore with the
  fewest usable surveys, then the best paid. With the stock for every ore, the surveyor waits until
  a survey expires or is used up, or, with the spare-time plan on, mines or siphons in the meantime
  (slice 6.8).
- **A spare-time trip that fills its hold** counts as free: a survey that needs taking takes the ship off it
  at once, with its hold aboard (D37), when that is safe (`SpareTimeInterruption`, see
  [Spare time](#spare-time-sparetimeplanservice-slice-68)), and logs `GatheringInterrupted` (`Reason`
  `survey`). A survey isn't ore-specific: the API surveys the whole asteroid and
  returns random deposits, so "for" an ore is the plan's label, and one survey often counts for
  several ores. Within the drones' reach every target is XB5C.
- **The state** (`plan_states`, `Survey`) lists the targets, best first, with how many usable surveys
  each has, whether it needs one, and the surveyors on each; it is written only when it changes. The
  `ShipLeftIdle` rule reads it: only targets that need a survey are work waiting for a surveyor.

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
  1. a miner that holds ore a market buys sells it first, where it fetches most after fuel (reason
     `held_cargo`): ore left over from the contract, for one;
  2. otherwise the best of `MiningPlanner.MiningTargets`: every market that buys an ore (imported or
     exchanged), mined at an asteroid it can reach with a usable survey holding the ore, or else at
     the asteroid nearest the market whose traits yield it, and sold there. The market shortest of its
     ore comes first (D28): SCARCE, then LIMITED (low supply, D22), and once no market is short, the
     lowest supply there is, even when it pays less. Within a supply level, a surveyed ore first, then
     the most a single extraction is expected to fetch: the ore's share of the survey's deposits
     (without a survey, one of the asteroid's ores) times its price. One miner per sell market and
     ore. It logs `MiningStarted`, reason `surveyed`, `low_supply` or `lowest_supply`.
- **Drones:** when no miner was free, it asks the same ranking what a drone from the shipyard would
  mine (its tank from the shipyard's listing, the trips under way held), and buys one only if that
  trip would serve a market short of its ore (SCARCE or LIMITED, D28). One a tick, so the next tick
  counts its trip; up to `Mining.MaxDrones` (default 20), within the credit reserve. Not while the
  contract plan mines: the contract would take the drone, and the contract plan buys at most one
  (D23). With the role board on, only when the board would give the drone the mining role
  (`RoleAdvisor`): a drone that would earn more trading would trade, and the plan would buy the next for
  the same opening.
- **The state** (`plan_states`, `MiningAutomation`) lists the low-supply openings: Assigned while a
  miner's trip sells there, Pending otherwise, with the free miners that could reach it, for the
  `ShipLeftIdle` rule. It is written only when it changes.

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
     exchanged) and that the siphoner can carry its hold to, siphoned at the gas giant nearest the
     market that it can reach, and sold there. The market shortest of its
     gas comes first (D28): SCARCE, then LIMITED (low supply, D22), and once no market is short, the
     lowest supply there is. Within a supply level, the most a single siphon is expected to fetch (one
     of the gas giant's three gases times the price), then the nearest gas giant. One siphoner per
     sell market and gas. It logs `SiphonStarted`, reason `low_supply` or `lowest_supply`.
- **Drones** (D32, the miners' rule): when no siphoner was free, it asks the same ranking what a
  `SHIP_SIPHON_DRONE` from the shipyard would siphon (its tank and hold from the shipyard's listing, the
  trips under way held), and buys one only if that trip would serve a market short of its gas (SCARCE
  or LIMITED). One a tick, at the shipyard that sells it for the least in a system where our ships
  are, up to `Siphon.MaxDrones` (default 10), within the credit reserve. In X1-DC53 that is C39, the
  station at the gas giant C38, where none of our ships stays: the purchase calls for a probe (D30),
  which only the probe plan answers, so with the probe plan off no siphon drone is bought until one of
  our ships happens to be at C39. With the role board on, a drone is bought only when the board would give
  it the siphon role (`RoleAdvisor`), as for the mining plan's drones.
- **Gas contracts** stay unsupported (D2, D31): no contract takes the siphoners.
- **The state** (`plan_states`, `SiphonAutomation`, as the mining plan's) lists the low-supply openings
  of every system where our ships are, before the first drone too: Assigned while a siphoner's trip
  sells there, Pending otherwise, with the free siphoners that could reach the gas giant, for the
  `ShipLeftIdle` rule. It is written only when it changes.

**What a gas giant yields** (`GasGiants`): the game doesn't publish it, and a gas giant's traits name no
gas (X1-DC53's C38 has only STRONG_MAGNETOSPHERE), so every gas giant counts as yielding the game's
three gases, HYDROCARBON, LIQUID_HYDROGEN and LIQUID_NITROGEN, about equally: the gases C39 exchanges.
The `Siphoned` journal lines show what the siphons really yield. Only GAS_GIANT waypoints can be
siphoned.

### Trading (`TradingAutomationService`, slice 6.5)

- **A trader** is any ship with a cargo hold and a fuel tank that has no goal (or a blocked one), no
  open assignment, and isn't in transit, and that the survey, mining and siphon plans, which go first,
  left free. With the survey plan on, a ship that can survey never trades (D20), unless the spare-time
  plan is on (below); a miner trades only when neither the contract nor the mining plan has work for
  it, and a siphoner only when the siphon plan has none. With the role board on (slice 6.9), a trader is a
  ship with the trade role, or with the mining or siphon role when that plan had no trip for it.
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
  - the units are one purchase: as many as the free hold, both markets' trade volumes and the
    credits allow (cargo may use the credit reserve, D17; the trip's fuel and
    `Trade.FuelReserveCredits`, 5,000, are kept back, D24: below them only fuel is bought);
  - the fuel is CRUISE, the distance rounded, at least 1 per flight, paid in whole FUEL units of 100
    at the market each flight ends at, at that market's price (where it sells none, the system's
    average). A flight longer than the tank holds refuels at markets that sell fuel on the way, the
    fewest stops first, then the cheapest fuel, each hop within a full tank. Never DRIFT (B47);
  - it is lucrative when it earns at least `Trade.MinProfitPerUnit` per unit after fuel (D14).
- **Ranking** (D15): among the lucrative trips, one whose sell market makes a pricier good from the
  cargo (it imports the good, and exports something made from it, by the game's production chains,
  at a higher price) comes before any that doesn't; then the most profitable.
- **Each tick** every free trader gets a trip, from the cached prices:
  - one that holds cargo first sells it where it fetches the most after fuel, one good a trip, when that
    earns anything. Once no good aboard pays for its sale, the goods go overboard before the trader takes a
    route (D42, `HeldCargo`): they would only take room from it. The contract's ore on a ship that mines for
    the contract stays aboard while the contract wants it;
  - otherwise the best route goes first, to the trader it is best for, and a route one trader holds
    is not offered to another (D18). It logs `TradeStarted`.
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
  lucrative route from the shipyard with the credits left after the purchase. `ShipPurchaseService`
  keeps the credit reserve. One purchase a pass.
- **The state** (`plan_states`, `TradingAutomation`) lists the held routes (Assigned) and up to 20
  lucrative routes no trader holds (Pending), each with the free traders that could have taken it,
  for the `ShipLeftIdle` rule. It is written only when it changes.
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
  6. It publishes `NewShipPurchasedEvent` (for the ledger) and `AgentCreditsChangedEvent`.
  - A purchase that doesn't happen says why (`ShipPurchaseFailure`): `PriceUnknown`, `OverBudget`
    or `NoShipAtShipyard`.
- **`BudgetPolicy`:** spendable credits are the cached credits minus
  `FleetExpansion.MinCreditReserve`.
- **Buying order within a tick:** contract, probe, mining, siphon, trading. There is no other priority.

### Which ships a plan considers free

| Plan | Claims a ship with | Treats a ship as free when |
|---|---|---|
| Scout | an assignment and a goal | — (picks one ship, once) |
| Contract | an assignment only | it is a miner (not a surveyor while the survey plan is on), not in transit, and has no assignment and no goal (or a finished or blocked one) |
| ProbeDeployment | a `DeployProbeGoal` | it is a probe (`FleetRoles.IsProbe`: a probe frame, or cached as `SHIP_PROBE` or `SATELLITE`), not in transit, and has no goal (or a finished or blocked one) |
| Survey | a `SurveyWaypointGoal` | it has a surveyor mount, is not in transit, and has no assignment and no goal (or a finished or blocked one), or is on a spare-time trip that fills its hold (D37) |
| Mining | a `MineAndSellGoal` | it is a miner, not in transit, and has no assignment and no goal (or a finished or blocked one) |
| Siphon | a `SiphonAndSellGoal` | it is a siphoner (`FleetRoles.IsSiphoner`: a gas siphon, a hold and a tank, nothing to mine or survey with), not in transit, and has no assignment and no goal (or a finished or blocked one) |
| Trading | a `TradeBetweenMarketsGoal` | it has a cargo hold and a fuel tank, isn't a surveyor while the survey plan is on, is not in transit, and has no assignment and no goal (or a finished or blocked one). With the spare-time plan on, a ship that gathers in its spare time too, free or on a spare-time trip that fills its hold, but only for a route that waits for it (D34) |
| SpareTime | a `GatherAndSellGoal` | it gathers in its spare time (`FleetRoles.GathersInSpareTime`: a surveyor, with the survey plan on, with a mining laser or a gas siphon, a hold and a tank), is not in transit, and has no assignment and no goal (or a finished or blocked one) |

**With the role board on** (slice 6.9), the roles replace the fixed rules in the table: the contract
takes a ship that can mine and doesn't have the survey role, whatever its role (D40); the survey plan a ship
with the survey role; the mining plan one with the mining role, the siphon plan one with the siphon role;
the trading plan one with the trade role, or the mining or siphon role; the spare-time plan a ship with the
survey role and a mining laser or a gas siphon, a hold and a tank. Each still needs to be out of transit,
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
  mines, siphons or trades, whichever earns the fleet most per hour.

---

## 4. Ships: goals, executors and commands

### Goals

- **Storage:** each ship has at most one active goal, stored in `cached_ships` (`GoalId`,
  `GoalKind`, `GoalPayloadJson`, `GoalStatus`).
- **Kinds:** 15 kinds are defined, but only seven are ever created: `ScoutWaypoint`,
  `DeployProbe`, `MineAndSell`, `SiphonAndSell`, `GatherAndSell`, `TradeBetweenMarkets` and
  `SurveyWaypoint`. The older `SiphonResource`, like `MineResource`, is never created.
- **Status:** `Assigned`, or `Blocked` once the circuit breaker stops the goal (see below).
  Nothing else changes it (B16). A blocked goal also records why, in `StatusReason`
  (`runaway`).
- **Set by:**
  - the scout, probe, survey, mining, siphon, trading and spare-time plans; the survey and trading plans
    also replace a spare-time trip that fills its hold (`SpareTimeInterruption`, slice 6.8);
  - `MineAndSellGoalExecutor` and `SiphonAndSellGoalExecutor`, which record that their trip turns to
    selling, `GatherAndSellGoalExecutor`, which records that too and each sale it chooses, and
    `TradeBetweenMarketsGoalExecutor`, which records its purchase, and a sale it moves, in their own goal
    (the goal id stays, so the arrival still matches).
- **Cleared by:**
  - `DeployProbeGoalExecutor` when it finishes, `TradeBetweenMarketsGoalExecutor` when the trip is
    sold or dropped, `MineAndSellGoalExecutor`, `SiphonAndSellGoalExecutor` and
    `GatherAndSellGoalExecutor` when the trip is sold or can't go on, and
    `SurveyWaypointGoalExecutor` after each survey (slice 6.4; B16's survey part, fixed: survey goals
    were never cleared);
  - the scout plan, when its last stop is done.

### `ShipGoalExecutorService`

- **Loads** the ship with `FindAsync`. That skips the arrival dead-reckoning that `GetAllAsync`
  applies (B17).
- **Runs** one step of the executor for the active goal. Only the seven kinds above are
  dispatched, so `IdleGoalExecutor` is unreachable.
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

| Executor | Behaviour |
|---|---|
| `ScoutWaypoint` | Docked at the target: mark it visited and complete. In orbit at the target: dock. Elsewhere: [cmd] navigate. |
| `DeployProbe` | One flight of a probe (slice 6.3). At the target (the arrival fetched its market and shipyard, and docked): clear the goal and complete; the probe plan chooses again. Elsewhere: in DRIFT, [cmd] switch to CRUISE first (a probe has no tank, so no flight costs it fuel); then [cmd] navigate. |
| `MineAndSell` | One trip (slice 6.4). **Mining:** [cmd] navigate towards the asteroid (`GoalFlight`: refuelling stops when it is beyond one tank, never DRIFT; in orbit at a fuel market without the fuel for the flight, dock first so the navigation refuels); there, wait for the cooldown, then [cmd] `MineResourceVolumeCommand` once per step, which extracts with the best survey for the ore. A full hold turns the trip to selling. **Selling:** navigate towards the sell market, dock, [API] sell in batches of the market's trade volume, publishing `ShipCargoSoldEvent` for each, fetch the market again (D25), clear the goal and complete. A market that no longer buys the ore, or an extraction the command rejects, clears the goal; the plan chooses again. |
| `SiphonAndSell` | One trip (slice 6.7), as `MineAndSell`. **Siphoning:** [cmd] navigate towards the gas giant (`GoalFlight`); there, wait for the cooldown, then [cmd] `SiphonResourcesCommand` once per step, which keeps every gas a market it can reach buys (D33). A full hold, of any gases, turns the trip to selling. **Selling:** with none of the trip's gas aboard, clear the goal and complete (the plan sells the other gases); else navigate towards the sell market, dock, [API] sell the trip's gas in batches of the market's trade volume, publishing `ShipCargoSoldEvent` for each, fetch the market again (D25), clear the goal and complete. A market that no longer buys the gas, or a siphon the command rejects, clears the goal; the plan chooses again. |
| `GatherAndSell` | One spare-time trip (slice 6.8). **Gathering:** [cmd] navigate towards its asteroid or gas giant (`GoalFlight`); there, wait for the cooldown, then [cmd] `ExtractResourcesCommand` at an asteroid or `SiphonResourcesCommand` (for `whatever sells`) at a gas giant, once per step, keeping every good a market it can reach buys. A source that no longer yields anything a market buys ends the trip. A full hold turns the trip to selling. **Selling:** choose the good that fetches most after fuel (with a full hold, even at a loss on the fuel) and record the sale in the goal; navigate there, dock, [API] sell it in batches of the market's trade volume, publishing `ShipCargoSoldEvent` for each, fetch the market again (D25), and clear the sale from the goal; the next step chooses the next. A market that no longer buys the good: the next step chooses again. Nothing left that pays for its fuel: clear the goal and complete. An extraction or siphon the command rejects clears the goal; the plan chooses again. |
| `TradeBetweenMarkets` | [cmd] navigate towards the buy market, by way of refuelling stops when it is beyond one tank (each stop's arrival refreshes that market), and dock. **Docked at the buy market:** work the trip out again with the prices the arrival has just fetched (the flight there is spent, so only the fuel still ahead counts); still lucrative: [API] buy and publish `CargoPurchasedEvent`, and record the purchase in the goal; otherwise clear the goal (`TradeDropped`), and the plan chooses again from there. Then navigate towards the sell market and dock. **Docked at the sell market:** when selling there no longer earns `Trade.MinProfitPerUnit` over what the cargo cost and another market pays more after fuel, move the sale there, once per trip (`TradeRerouted`); otherwise [API] sell, in batches of the market's trade volume, publishing `ShipCargoSoldEvent` for each, then clear the goal and complete. A market that doesn't buy the good, once the sale has moved: clear the goal (`TradeDropped`); the plan then sells the cargo where it can. |
| `SurveyWaypoint` | One survey (slice 6.4). [cmd] navigate towards the asteroid (`GoalFlight`). Docked there: orbit. On cooldown: wait. In orbit: [API] survey, store the cooldown and the surveys (`SurveyKeeper`: `cached_surveys`, `Surveyed` per survey, `spacetraders_surveys_taken_total`), clear the goal and complete. A failed survey clears the goal too (the plan gives it again; a failure that repeats shows as `RepeatingError`). |
| `Idle` | Unreachable. |

### Commands

- **`NavigateToWaypointCommand`:**
  - Already at the destination: it does nothing and logs a warning. It publishes no
    `ShipNavigationCompletedEvent`, because that would run the caller's goal step again without
    progress (B1, fixed).
  - Docked: it refuels if the waypoint sells fuel, then orbits.
  - Then it navigates. Navigate tries DRIFT mode and intermediate markets when fuel is short,
    then schedules the arrival and publishes `ShipInTransitEvent`.
- **On arrival** (`NavigateToWaypointArrivedCommand`) it refreshes the market (publishing
  `MarketDataRefreshedEvent`) and the shipyard, docks, and publishes
  `ShipNavigationCompletedEvent`.
- **Orbit, Navigate, Dock and Refuel** are DI sub-commands, not bus messages. Refuel publishes
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
  own form (`…51.937Z`, B51). Other goods are jettisoned.
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
  them.
- **`PatchShipNavCommand`** changes the flight mode.
- **Selling and buying** are direct API calls from the executors, which publish what they did.
- **Credits:** whatever changes the credits stores them in the cached agent and publishes
  `AgentCreditsChangedEvent` (`AgentCreditsUpdates`): refuels, sales, cargo and ship purchases,
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
| `ShipCargoSoldEvent` | Mining and trade executors | `LedgerEntryHandler` (TradeSell); `activity_logs` row |
| `CargoPurchasedEvent` | Trade executor | `LedgerEntryHandler` (TradeBuy) |
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
- **Activity-log-only events:** arrivals, idle ships, assignments, low fuel, construction,
  contract negotiation, acceptance and delivery, automation paused and resumed.
- **Contract plan events:** `DeliverableObtainedEvent` and `ContractDeliveryRecordedEvent`.

### Never dispatched

Domain aggregates (`Agent`, `Ship`, `Contract`) raise events into a list that nothing reads, and
production code doesn't use the aggregates at all. The events are published where the change
happens instead (B7, fixed).

### Wolverine details

- **Discovery:** Wolverine finds handlers by convention (class names ending in `Handler` or
  `Consumer`). The `Handle` methods on `ContractPlanService` are therefore not wired; the trading,
  survey, mining and siphon plans have none (since slices 6.5, 6.4 and 6.7). `DiValidationTests`
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
- Wolverine stores nothing in the database.

### Agent scoping

- Every table but `scheduled_ship_events` has an `AgentId` column: the agent's symbol and the
  server's reset date, such as `GEMBER@2026-09-27` (`AgentIdentity`). A token keeps the id it
  was first stored under. Where a table has a natural key, the id is part of it.
- EF filters every query on the active agent's id. `startup_snapshots`, `agent_credits_samples`
  and `scheduled_ship_events` have no filter.
- The token itself is stored only in `stored_credentials`.
- The database keeps one agent: at startup, agent bootstrap deletes every other agent's rows,
  except their `runs` (`AgentDataCleanup`). Pruning then only has the active agent's rows to deal
  with.

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
| `cached_markets`, `cached_shipyards` | Market and shipyard JSON | Sync, arrivals | bounded: one row per market or shipyard |
| `cached_waypoints`, `cached_systems` | Systems where ships are, with each waypoint's traits and modifiers (B34, fixed) | Sync (inserts, and fills in missing traits); scouting sets `LastObservedAt` | bounded: the systems the fleet has been in |
| `agent_settings` | Settings | Seed, `PUT /settings` | bounded: one row per setting |
| `ship_assignment_records` | Scout and contract assignment per ship | Scout and contract plans | bounded: one row per ship |
| `plan_states` | Plan JSON per plan type | All nine plans, the role board included | bounded: one row per plan |
| `scheduled_ship_events` | Arrival timers | Navigate | bounded: deleted when fired |
| `activity_logs` | Activity log | `LogActivityHandler`: transit, state mismatch, token reset | `ActivityLog.RetentionDays` (30) |
| `ledger_entries` | Credit ledger | `LedgerEntryHandler`: refuels, sales, cargo and ship purchases, contract payments | 30 days |
| `cached_surveys` | Surveys, with who took them, when, and how many extractions used them (`Extractions`, slice 6.4) | `SurveyKeeper`: the survey executor stores, extractions count, refusals remove | bounded: the survey plan removes expired surveys on every pass (journaling each), and an extraction refusal removes its survey |
| `leader_leases` | Leader lease | Leader election | bounded: one row |
| `api_endpoint_usages` | Call count per endpoint string | Every outbound call once the agent is known | bounded: one counter per endpoint |
| `runs` | Run summaries | Run lifecycle | 365 days, every agent's |
| `run_credit_highlights` | Start/end credits per run | Run lifecycle | 365 days |
| `startup_snapshots` | One full JSON snapshot per start | Startup snapshot | the agent's first and the last 10 |
| `market_price_samples` | Price history | `MarketPriceSampleHandler`, one row per good on every market refresh | 7 days raw, first per hour to 90 days |
| `agent_credits_samples` | Credits over time | `AgentCreditsSampleHandler`, on every credit change | 7 days raw, first per hour to 90 days |
| `ship_task_records` | Task timeline | Nothing | 30 days |
| `ship_goal_history` | Ended goals | Nothing | 30 days |
| `fleet_goals` | Fleet goals | Nothing | completed ones 30 days |
| `trade_opportunities` | Trade routes | Nothing | bounded: replaced as a whole |
| `cached_construction_sites` | Construction sites | Nothing | bounded: one row per site |
| `scheduled_runs` | Runs to start later | Nothing | bounded: deleted when promoted |

---

## 7. Settings and configuration

### How settings work

- Settings live in `agent_settings` and are read from the database on every use (no cache), so a
  change applies on the next read.
- `PUT /settings/{key}` with `{"value": "…"}` writes any key, even an unknown one.
- `POST /settings/reset` restores the defaults.
- Every change, whoever makes it (these endpoints, the control endpoints, the size guard, the
  reset monitor), is a `SettingChanged` journal line with `Setting`, `OldValue` and `NewValue`
  (`SettingsRepository`). A key that may hold a secret (ending in `Url`, or naming a secret,
  password or API key) shows `(hidden)` instead of its value.
- The SpaceTraders dashboard's **Settings** table shows every setting with its value now and what
  it does (`spacetraders_setting_info`, section 11): the switches first, on or off, then the rest by
  name; the `Runtime.*` status flags are left out. A value that may hold a secret is hidden there too.
  What a setting does comes from the running version's seed: a stored description is the one the
  setting was seeded with, which an older version may have written (slice 2.9).

### What each setting does

The seed holds 50 settings: the 35 that change what the bot does (8 of them the health rules'
thresholds), 3 that are read without changing it, and 12 status flags. Settings that nothing read, or only code that never runs, were
removed from it in slice 2.6 (B18, D10); `DefaultSettingsSeedTests` pins the list.

| Setting (default) | Effect |
|---|---|
| `Automation.Enabled` (true) | Off: no plans, goal steps or contract work, whatever would trigger them. Startup recovery skips. |
| `Automation.Plan.Scout.Enabled`, `.Contract.Enabled` (true); `.Roles.Enabled`, `.ProbeDeployment.Enabled`, `.Survey.Enabled`, `.Mining.Enabled`, `.Siphon.Enabled`, `.Trading.Enabled`, `.SpareTime.Enabled` (false) | Off: the plan isn't bootstrapped, buys nothing and its ships' goals wait (D9). With the survey plan on, a ship that can survey only surveys (D20); with the spare-time plan on too, the command ship trades or mines and siphons when it has nothing to survey (D34–D37). With the role board on, every ship works for the plan of the role the board gives it instead (D38–D41) |
| `Roles.ReconsiderMinutes` (10) | Minutes between the role board's evaluations of the whole fleet; a new ship, a plan switched, the contract starting or stopping to want ore, or a ship left without work weighs the roles at once (D41) |
| `Roles.HeadStartPercent` (20) | Percent more a ship's current role counts on the role board, so close calls don't flip back and forth (D41); 0 means none |
| `Roles.ChainValueSharePercent` (50) | Percent of the price difference to the pricier good a market makes from what a ship sells it that the role board counts, and that share again of the step after; fully while the market is SCARCE of it, not at ABUNDANT (D39); 0 means none |
| `Automation.CircuitBreaker.MaxGoalStepsPerMinute` (60) | Goal steps per ship per minute above which the circuit breaker blocks the goal |
| `Api.BadGatewayPauseMinutes` (3) | Minutes without any API call after a 502 |
| `Database.SoftLimitMegabytes` (1024), `Database.HardLimitMegabytes` (3072) | Database size above which the size guard warns, or switches automation off (D8) |
| `FleetExpansion.MinCreditReserve` (100000) | Credits every purchase must leave untouched |
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

The first three follow the API guide (https://spacetraders.io/api-guide/rate-limits, D3).

1. **`OutagePauseHandler`:** a 502 comes from the API's DDoS protection, and the guide asks to
   wait a few minutes. So after a 502 no call goes out for `Api.BadGatewayPauseMinutes`
   (default 3): calls in that time fail at once with `ApiPausedException`. The first call after
   the pause goes out as usual; a success marks the API available, another 502 pauses again.
2. **`RateLimitResponseHandler`:** a 429 with `x-ratelimit-*` headers comes from the API's rate
   limiter: it waits until `x-ratelimit-reset` (else `retry-after`, else 1 s; at most a minute)
   and retries. A 429 without them comes from the cloud infrastructure: it backs off 1, 2, 4, 8
   and 16 s. Either way it gives up after five retries. Every 429 is counted and logged at
   Warning, and the headers are recorded for `/status/rate-limit`.
3. **`RateLimitingHandler`:** each request takes from `RequestBudget`, a singleton: 2 requests
   in any second and, once those are used, up to 30 more in any 60 seconds. It waits only when
   both are used. Writes (anything but GET: moving a ship, trading) go before reads (D19): a read
   gives way while a write waits for the budget, leaves the last 10 of the burst to writes
   (`WriteReserve`), and stops giving way after 10 seconds (`MaxReadDelay`), so reads can't
   starve. Time spent waiting is counted in `spacetraders_api_rate_limit_wait_seconds_total`, by
   `kind`: `read` or `write`.
4. **`ApiRequestMetricsHandler`:** counts every request that goes out, retries included, in
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
- **Tokens:** status, systems and waypoints calls go without a token, `register` uses the account
  token, and everything else uses the agent token.
- **Usage counts:** every call increments `api_endpoint_usages`, except the calls agent bootstrap
  makes before it knows the agent.
- **`ISpaceTradersPort`** has 41 operations:
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
| Status | `GET /status/agent`, `/ships`, `/ships/{s}/diagnostics`, `/waypoints/{s}`, `/contracts`, `/rate-limit`, `/activity?page&size&ship`, `/mining-opportunities`, `/startup-snapshots` (+ `/{id}/download`), `/system-alerts` | Cached data |
| Status (empty) | `GET /status/trade-opportunities`, `/top-trade-routes` | Read tables that are never written; always 204, `[]` or zeros |
| Status (credit growth) | `GET /status/anomalies` | A heuristic over the credit samples: credits per hour over the last 24 hours against the last hour. Not the health rules' anomalies (section 12) |
| Universe | `GET /universe/systems`, `/jump-connections` | Jump connections are always `[]` |
| Runs and finance | `GET /runs/{id}/kpis`, `/finance/trade-routes` | KPIs is a stub; trade routes are always `[]` |
| Fleet | `GET /fleet/assignments`, `/activity`, `/activity/{ship}`, `/activity/{ship}/history`, `/goal-chains` | 5 s cache. History is always `[]` |
| Markets | `GET /markets/waypoints`, `/waypoints/{s}`, `/freshness`, `/goods/{s}/prices`, `/waypoints/{s}/prices`, `/best-routes` | Best routes always 204 |
| Shipyards | `GET /shipyards/waypoints`, `/waypoints/{s}`, `/freshness` | |
| Settings | `GET /settings`, `PUT /settings/{key}`, `POST /settings/reset` | |
| Control | `POST /control/automation/enable`, `/disable` | Sets `Automation.Enabled` |

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
| `/fleet`, `/fleet/:symbol` | `/status/ships`, diagnostics, waypoints |
| `/markets` | Market and shipyard waypoints and freshness |
| `/snapshots` | Startup snapshots |
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
- **The journal:** one line per meaningful thing, with an `EventKind` property and a message that
  starts with it (`CargoSold: ship …`), so `{namespace="spacetraders"} | json | EventKind != ""`
  in Loki reads as a timeline of the run. `JournalEvents` names every kind:

  | Kind | Logged by | Properties |
  |---|---|---|
  | `ContractAccepted` | Contract plan | `ContractId`, `TradeSymbol`, `WaypointSymbol`, `Payment` |
  | `ContractDelivered`, `ContractFulfilled` | `FulfillContractDeliveryCommand` | `ContractId`, `ShipSymbol`, `TradeSymbol`, `Units`, `WaypointSymbol`; `Payment` |
  | `ShipPurchased` | `ShipPurchaseService` | `ShipSymbol`, `ShipType`, `WaypointSymbol`, `Cost` |
  | `CargoBought`, `CargoSold` | Trade, mining, siphon and spare-time executors | `ShipSymbol`, `TradeSymbol`, `Units`, `WaypointSymbol`, `Cost` or `Revenue` |
  | `TradeStarted` | Trading plan | `ShipSymbol`, `TradeSymbol`, `Units`, `BuyWaypoint`, `SellWaypoint`, `SellPrice`, `FuelCost` (the whole trip, the flight to the buy market included), `ExpectedProfit`; `BuyPrice` for a purchase; `FeedsTradeSymbol` when the sell market makes a pricier good from it |
  | `TradeRerouted` | Trade executor, at the sell market | `ShipSymbol`, `TradeSymbol`, `WaypointSymbol`, `SellPrice`, `SellWaypoint`, `NewSellPrice`, `FuelCost`, `Reason` |
  | `TradeDropped` | Trade executor | `ShipSymbol`, `TradeSymbol`, `WaypointSymbol`, `SellWaypoint`, `Reason`: `not_lucrative` (with `Units`, `BuyPrice`, `SellPrice`, `ExpectedProfit`, `MinProfitPerUnit`), `not_possible` or `not_bought_here` |
  | `Surveyed` | `SurveyKeeper`, one per survey a ship takes (slice 6.4) | `ShipSymbol`, `WaypointSymbol`, `TradeSymbol` surveyed for, `Signature`, `Size`, `Deposits` (`COPPER_ORE x2, IRON_ORE`), `Expiration` |
  | `SurveyEnded` | `SurveyKeeper`: the survey plan for expired surveys, the extraction command for refused ones | `Signature`, `WaypointSymbol`, `Size`, `Reason` (`expired`, `exhausted`, `not_verified`), `Extractions` made with it, `ShipSymbol` that took it, `SurveyedAt` |
  | `Extracted` | `MineResourceVolumeCommand`, per extraction; `ExtractResourcesCommand`, per spare-time extraction (slice 6.8) | `ShipSymbol`, `Units`, `TradeSymbol` it got, `WaypointSymbol`, `Target` it mines for (`whatever sells` in spare time), `Signature` of the survey (empty without one) |
  | `MiningStarted` | Mining plan (a trip), contract plan (a miner joining, D23) | `ShipSymbol`, `TradeSymbol`, `WaypointSymbol` it mines at, `SellWaypoint`, `Reason` (`held_cargo`, `surveyed`, `low_supply`, `contract`); `ContractId` for the contract |
  | `Siphoned` | `SiphonResourcesCommand`, per siphon (slice 6.7) | `ShipSymbol`, `Units`, `TradeSymbol` it got, `WaypointSymbol`, `Target`: the gas its trip is for (`whatever sells` in spare time) |
  | `SiphonStarted` | Siphon plan (a trip) | `ShipSymbol`, `TradeSymbol`, `WaypointSymbol` it siphons at, `SellWaypoint`, `Reason` (`held_cargo`, `low_supply`, `lowest_supply`) |
  | `GatheringStarted` | Spare-time plan (a trip, slice 6.8) | `ShipSymbol`, `WaypointSymbol` it gathers at, `Method` (`mines`, `siphons`) |
  | `GatheringInterrupted` | Survey and trading plans, taking a ship off a spare-time trip that fills its hold (D34, D37) | `ShipSymbol`, `WaypointSymbol` it gathered at, `Reason` (`survey`, which keeps the hold aboard; `trade`, which sells it first), `Units` aboard |
  | `RoleChanged` | Role board, for each ship whose role changes (slice 6.9) | `ShipSymbol`, `OldRole`, `NewRole` (`Survey`, `Mine`, `Siphon`, `Trade`, `None`), `Reason` (`only_role`, `survey_first`, `contract`, `most_profitable`, `no_work`, `no_role`); for a role chosen by profit, `CreditsPerHour` and `Job`: the trip that decided it |
  | `CargoJettisoned` | `CargoJettison`: the trading and spare-time plans, for cargo nothing will sell or use (D42) | `ShipSymbol`, `Units`, `TradeSymbol`, `WaypointSymbol`, `Reason` (`no_buyer`, `not_worth_the_fuel`) |
  | `ProbeCalled` | Probe plan, when it sends a probe to a shipyard where a purchase waits for one of our ships (D30) | `ShipSymbol`, `WaypointSymbol`, `ShipType` the purchase is for |
  | `PlanStarted`, `PlanCompleted` | Scout, contract and probe plans | `Plan`, and the plan's ship, contract or system |
  | `PlanBlocked` | Contract plan (`unsupported_deliverable`, `no_ship_or_budget`, `no_asteroid`), probe plan (`waiting_for_credits`) | `Plan`, `Reason` |
  | `ShipIdle` | `ShipStateJournal`, from the 10 s sample: `idle_at_start`, `new_ship`, `goal_ended` (with `PreviousGoal`) | `ShipSymbol`, `Reason` |
  | `ShipBlocked` | The circuit breaker (Warning) | `ShipSymbol`, `GoalKind`, `Reason` |
  | `SettingChanged` | `SettingsRepository` | `Setting`, `OldValue`, `NewValue` |
  | `ResetDetected` | `ServerResetMonitor` (Critical) | `Detail` |
  | `ApiUnavailable`, `ApiAvailable` | The tick | `PausedUntil` |
  | `AnomalyRaised` | The health monitor (Warning) and the size guard | `Rule`, `Subject`; the monitor's also `Details` |
  | `AnomalyCleared` | The health monitor and the size guard | `Rule`, `Subject`; the monitor's also `ActiveMinutes` |

  Mining, siphoning, trading and spare time have no plan to start or complete. Mining journals each trip
  (`MiningStarted`, `Extracted` per extraction, `CargoSold`), and each survey's life (`Surveyed`, then
  `SurveyEnded`); siphoning each trip (`SiphonStarted`, `Siphoned` per siphon, `CargoSold`); spare time
  each trip (`GatheringStarted`, `Extracted` or `Siphoned`, `CargoSold`, or `GatheringInterrupted`);
  trading journals each trip: `TradeStarted`, then `CargoBought` and `CargoSold`, with
  `TradeRerouted` or `TradeDropped` when prices change. The role board journals each role that changes
  (`RoleChanged`), and cargo that goes overboard because nothing will sell or use it is a
  `CargoJettisoned` line (D42).
- **Metrics** on the metrics port (`Metrics:Port`, 9090), without the API key.
  `PrometheusAutomationMetrics` defines them all at startup, so a scrape lists every one, also
  before it has a value; prometheus-net adds its defaults (process, .NET and HTTP metrics, and the
  .NET meters, Wolverine's among them). Labels stay low-cardinality: a few per ship or contract
  at most.

  Every `spacetraders_*` counter series reaches Prometheus at 0 before it counts anything
  (`ZeroFirstCounter`, B43). Prometheus's `increase()` and `rate()` never count the value a series
  has when it is first scraped, so a series that held its first increment then (the contract's
  deposit and the first drone, booked before the pod's first scrape; any single 429 or breaker
  trip) never showed on the dashboard. A new series is published at 0, and what it counts waits
  until a scrape has exported that 0: up to two scrape intervals (2 minutes on the cluster). Locally,
  a new series shows its count from the second `curl` of `/metrics` after it appeared.

  A gauge without labels (`spacetraders_agent_credits`, `spacetraders_db_size_bytes`,
  `spacetraders_server_next_reset_timestamp_seconds`) is the opposite: its 0 would be read as a
  value, so it has no series until the bot first sets it (B52); its `# TYPE` line is there from the
  start. Prometheus's first scrape of a new pod can come before the first sample, and the dashboard
  read 0 credits for a minute after a deploy, which "Value gained per hour" showed as a loss of the
  whole fleet's value, and an hour later as the same gain. Now that minute is a gap.

  | Metric | Labels | What it counts or shows | Updated |
  |---|---|---|---|
  | `spacetraders_agent_credits` | | The agent's credits, as cached | Every 10 s (`PrometheusMetricsService`) |
  | `spacetraders_ships` | `role`, `state` | Ships by type as cached (B25) and by `DOCKED`, `IN_ORBIT` or `IN_TRANSIT` | Every 10 s |
  | `spacetraders_ship_status_since_timestamp_seconds` | `ship`, `role`, `state`, `goal`, `reason` | One series per ship. `goal` is the goal's kind, else the assignment's type (`Contract`), else `None`; `reason` says why a goal is blocked (`runaway`). The value is when the ship entered this combination (Unix time, since the start at the latest), so `time() - …` is the time in state | Every 10 s |
  | `spacetraders_ship_info` | `ship`, `location`, `activity` | One series per ship, always 1. `location` is the waypoint and its type, such as `X1-AB-A1 (ASTEROID)`, or in transit `→` and where it goes; `activity` is what the bot has it do: its goal in a few words (`scouting`), else its contract work (`mining COPPER_ORE` at the contract's source, `delivering COPPER_ORE` at its destination, `on the way to …` between them), else `idle`, or `blocked (…)` | Every 10 s |
  | `spacetraders_ship_arrival_timestamp_seconds` | `ship` | When a ship in transit arrives (Unix time); no series otherwise | Every 10 s |
  | `spacetraders_ship_cargo_units`, `spacetraders_ship_cargo_capacity_units` | `ship`, `good`; `ship` | A ship's hold per good aboard, and what it takes | Every 10 s |
  | `spacetraders_contract_units_required`, `_units_fulfilled` | `contract`, `trade_symbol` | The accepted contracts' deliverables | Every 10 s |
  | `spacetraders_contract_deadline_timestamp_seconds` | `contract` | An accepted contract's deadline (Unix time) | Every 10 s |
  | `spacetraders_credits_earned_total` | `source` | Credits earned, by ledger category | With each ledger row (`LedgerEntryHandler`) |
  | `spacetraders_credits_spent_total` | `category` | Credits spent, by ledger category | With each ledger row |
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
  | `spacetraders_market_purchase_price`, `_sell_price`, `_trade_volume` | `system`, `waypoint`, `good`, `kind` | A good at a market as last seen: what the market charges, what it pays, its trade volume; `kind` is `EXPORT`, `IMPORT` or `EXCHANGE`. Only once a ship has been there | Every minute |
  | `spacetraders_market_supply`, `_activity` | `system`, `waypoint`, `good`, `kind` | Supply 1 `SCARCE` to 5 `ABUNDANT`; activity 0 `RESTRICTED`, 1 `WEAK`, 2 `GROWING`, 3 `STRONG` | Every minute |
  | `spacetraders_shipyard_observed_timestamp_seconds` | `system`, `waypoint`, `waypoint_type` | When the bot last refreshed a cached shipyard | Every minute |
  | `spacetraders_shipyard_ship_type` | `system`, `waypoint`, `ship_type` | 1 for each ship type a shipyard sells | Every minute |
  | `spacetraders_shipyard_ship_price`, `_ship_supply` | `system`, `waypoint`, `ship_type` | A ship type's price and supply (1 to 5) as last seen, once a ship has been there | Every minute |
  | `spacetraders_good_supply_chain` | `good`, `made_from`, `used_for` | One series per good, always 1: the goods it is made from and the goods made from it, comma-separated (`GET market/supply-chain`) | Once per start |
  | `spacetraders_ship_role_info` | `ship`, `role`, `reason` | One series per ship on the role board (slice 6.9), always 1: its role (`Survey`, `Mine`, `Siphon`, `Trade`, `None`) and why (`survey_first`, `most_profitable`, ...). Only while the board is on: switched off, the plans don't read its roles. The dashboard's roles table | Every 10 s, from the board's state |
  | `spacetraders_ship_role_credits_per_hour` | `ship`, `role` | What each role a ship could take but surveying would earn it per hour, by the board's estimate of its best trip; 0 for a role without a trip. Only while the board is on | Every 10 s |
  | `spacetraders_setting_info` | `setting`, `current`, `description` | One series per setting the agent has, always 1: its value now (`true` or `false` for a switch; `(hidden)` for a key that may hold a secret, as in `SettingChanged`) and what it does, from the running version's seed, else as stored. The dashboard's settings table (slice 2.9) | Every 10 s |

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
  the survey role (slice 6.9);
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
  nowhere to gather and sell.

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

- `IdleGoalExecutor`, and 8 of the 13 goal kinds.
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
