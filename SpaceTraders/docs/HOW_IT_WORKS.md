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
                  ├─ bootstrap the plans that are on: Scout, Contract, ProbeDeployment, Mining, Trading
                  ├─ one goal step per ship ─► executor ─► commands ─► SpaceTraders API
                  ├─ contract assignments: deliver, or mine
                  └─ publish API availability changes
arrival timer ─► ShipArrivedEvent ─► dock + refresh market ─► ShipNavigationCompletedEvent ─► goal step
```

---

## 1. Process and startup

### Hosting (`SpaceTraders.API/Program.cs`)

- **Path base:** `/spacetraders/api`. Paths without the prefix also route.
- **Serilog:** writes to the console only. Production uses compact JSON with the rendered message
  (CLEF: `@t`, `@m`, `@i`, and `@l` for levels above Information); other environments use plain
  text. Levels come from the `Serilog` section of `appsettings*.json`: Information by default,
  Warning for ASP.NET Core, EF Core, Wolverine, JasperFx and `System.Net.Http`. Every line carries
  `Application=SpaceTraders.API`.
- **Wolverine** discovers handlers in the Application assembly and keeps messages in memory:
  nothing goes to Postgres. A crash loses the messages still in flight; after the restart,
  startup sync and startup recovery pick the ships up again, and pending arrivals wait in
  `scheduled_ship_events`. Any handler exception is retried after 250 ms, 500 ms and 1 s, then
  the message is discarded.
- **Health checks:**
  - `/health/live` runs no checks.
  - `/health/ready` checks the database.
  - `/health/startup` is Healthy when the startup chain completed, Unhealthy if it failed, and
    Degraded before or while it runs.
- **CORS** (`Dashboard` policy): GET only. With `WebUI:Origin` set, only that origin is allowed;
  otherwise any origin is.
- **Middleware order:**
  1. path base
  2. CORS
  3. Swagger (Development only)
  4. API key
  5. HTTP metrics
  6. health endpoints
  7. `/metrics`
  8. endpoint groups
  9. SignalR hub `/hubs/dashboard`
- **Hosted services:** the app registers exactly one, `DeferredStartupHostedService`. Every other
  service is a singleton that it starts.

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
| 9 | `StartupSnapshotService` | once | One JSON snapshot row |
| 10 | `StartupRecoveryService` | once | Resumes ships |
| 11 | `SettingsStartupLoggingService` | once | Logs every setting |
| 12 | `GameLoopService` | every 5 s | The tick |
| 13 | `PrometheusMetricsService` | every 10 s | |

One try/catch wraps the chain. Steps 5, 9 and 11 catch their own errors. A throw in steps 1, 4, 6,
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
  waypoints if they aren't cached yet. It also fetches market and shipyard data at waypoints where
  a ship is not in transit, and the first page of contracts (20). It stores them the way the
  other paths do: shipyards with their prices, so a purchase can read them (B28, fixed), and
  contracts with their terms, so a restart doesn't blank the deliverables the contract plan works
  from (B31, fixed).
- **Updates** the game state of existing ship rows (nav, fuel, cargo, mounts and so on). It
  leaves their goal columns alone, so ships keep their goals across a restart.
- It has no error handling.

**Startup snapshot:** switches `Automation.Enabled` off while it runs and back on afterwards. It
writes one `startup_snapshots` row with the agent, ships, waypoints and market and shipyard data.

**Startup recovery** (`StartupRecoveryService`): skipped when `Automation.Enabled` is false.
For each cached ship:

| Ship state | Recovery action |
|---|---|
| Arrival time passed, still marked in transit | Publish `ShipInTransitEvent`, then run one goal step |
| Still in transit | Publish `ShipInTransitEvent` only |
| Docked or in orbit | Run one goal step |

It doesn't reschedule arrivals. Pending arrivals survive a restart only through
`scheduled_ship_events`.

---

## 2. The tick (`GameLoopService`)

- Runs 5 s after the previous tick ends, on the leader only.
- Each tick:
  1. `EnsureBootstrappedAsync` for each plan that is switched on, in this order: Scout, Contract,
     ProbeDeployment, Mining and Trading.
  2. One goal step for every cached ship (`ShipGoalExecutorService.ExecuteAsync`).
  3. If the contract plan is switched on, for every active `Contract` assignment:
     `FulfillContractDeliveryCommand` once the ship holds a whole trip (what the contract still
     needs, at most a full hold; with nothing left to deliver, that command fulfils the contract),
     otherwise `MineResourceVolumeCommand` (B8, fixed).
  4. Publish `ApiUnavailableEvent` or `ApiAvailableEvent` when availability changed, and log
     it (`ApiUnavailable`, `ApiAvailable`). Nothing handles the events.

  With `Automation.Enabled` off, or while API calls are paused after a 502, it skips steps 1
  to 3.
- Each plan bootstrap, each ship's goal step and each contract assignment runs in its own DI
  scope and try/catch. A failure is logged at Error with the plan or the ship, and the rest of the
  tick carries on. Its own scope means a step can't leave a broken DbContext to the steps after
  it.
- A failure outside those steps (reading the switches, listing the ships or assignments) ends
  the tick; the next one starts 5 s later.

---

## 3. Plans

All plan state is JSON in `plan_states`, one row per plan type.

Each plan has a switch, `Automation.Plan.{Plan}.Enabled` (`AutomationSwitches`). Per D9 only
Scout and Contract are on by default. A plan that is switched off:
- isn't bootstrapped, so it doesn't buy anything;
- doesn't move its ships: `ShipGoalExecutorService` skips the goals it gives (scout, probe,
  mine-and-sell and survey, trade). They resume when it is switched back on;
- for the contract plan: the tick's contract work (step 3) is skipped too.

| Plan | Purpose | Ships it uses | Statuses | Buys |
|---|---|---|---|---|
| Scout | Visit every marketplace in the starting system once | The one ship with fuel | Active → Completed | Nothing |
| Contract | Fulfil one mineral contract | A mining-capable ship without an assignment | PendingBudget, Active, DeferredUnsupported, Completed | `SHIP_MINING_DRONE` |
| ProbeDeployment | Park a probe at every market and shipyard in the HQ system | Probes | Active ⇄ Completed, plus a waiting flag | `SHIP_PROBE` |
| Mining | Sell minerals where they are scarce | Mining-capable ships without a goal | Opportunity queue (Pending/Assigned) | `SHIP_MINING_DRONE`, up to `Mining.MaxDrones` |
| Trading | Haul goods from abundant to scarce markets | Ships with cargo and fuel without a goal | Opportunity queue (Pending/Assigned) | `SHIP_LIGHT_HAULER`, no cap |

### Scout (`ScoutAllMarketplacesPlanService`)

1. **Picks** the single ship with fuel capacity and fuel above zero. It throws if there are zero
   or several, which stops the tick (B14).
2. **Targets** the cached waypoints with a market in that ship's system.
3. **Routes** by nearest neighbour, starting at the ship's waypoint if it is a market, otherwise
   at the alphabetically first market.
4. **For each stop** it writes a `Scout` assignment and a `ScoutWaypointGoal`. The plan advances
   when the ship is docked at the stop.
5. **After the last stop** the plan is Completed and the ship's goal is cleared, so the ship is
   free for other work (B10, fixed). A scout goal that outlived its plan (a database from before
   the fix) is cleared on its next step.

Markets are not scouted again.

### Contract (`ContractPlanService`, plus step 3 of the tick)

- **While the plan is Active,** bootstrap advances it from the cached contract, which every
  delivery updates: it records the units delivered, keeps the assignment's remaining units current,
  and restores a missing assignment. It writes only what changed. A plan in any other status
  except PendingBudget makes bootstrap return immediately: one contract per reset (D1).
- **Otherwise it:**
  1. refreshes contracts from the API, ignoring errors, but only while there is no plan yet;
  2. negotiates a contract if none is open, using the first ship that has a waypoint;
  3. takes the unfulfilled contract with the earliest deadline;
  4. accepts it, and records the acceptance payment in the cached credits (B33, fixed);
  5. parks a non-mineral deliverable as DeferredUnsupported (D2);
  6. picks an idle mining-capable ship, or buys a drone. If neither works, the plan becomes
     PendingBudget and is retried every tick. A retry works from the cached contract and only
     tries the ship again: it calls the API only to buy, and stores nothing while the plan keeps
     waiting (B27, fixed);
  7. picks the nearest asteroid;
  8. saves an Active plan and a `Contract` assignment.
- **The work itself is step 3 of the tick:**
  - **Mining:** `MineResourceVolumeCommand` travels to the asteroid, extracts once per cooldown
    and jettisons other goods.
  - **Delivery:** `FulfillContractDeliveryCommand` travels to the destination, docks, delivers
    what it holds but at most what the contract still needs (B30, fixed), and calls fulfil once
    nothing is pending, recording the payment in the cached credits.
- **Completion:** once the contract is fulfilled, bootstrap completes the plan and closes the
  assignment, which releases the ship (B9, fixed). Every unit delivered isn't enough: until the
  fulfil call has gone out, the assignment stays open with 0 units left, so the ship goes back
  to make it.

### Probe deployment (`ProbeDeploymentPlanService`)

- **Targets:** the waypoints with a market or shipyard in the HQ system.
  - Shipyards come first.
  - Market-only waypoints wait until credits reach 200,000, a hard-coded threshold (D4). Below
    that the plan sets `WaitingForPhase1Credits`, which only an event that is never published
    clears (B7).
- **Probes** are recognised by ship type `SHIP_PROBE`, or by a symbol containing `PROBE` or
  `SATELLITE`. Startup sync stores a ship's *registration role* as its type (for example
  `COMMAND` or `SATELLITE`). Only purchased ships get the shipyard type (for example
  `SHIP_PROBE`). So the starting probe is probably not recognised, and the plan buys a probe
  instead (B25).
- **A free probe** gets a `DeployProbeCommand`. Its `DeployProbeGoal` sets DRIFT mode, travels,
  docks, marks the waypoint deployed and clears the goal.
- **Without a free probe,** it buys one after a budget check (B15).

### Mining (`MiningAutomationService`)

- **Opportunities** are cached market goods of type IMPORT or EXCHANGE, with supply SCARCE and a
  mineral symbol.
- **Each tick it:**
  1. clears `MineAndSellGoal`s whose opportunity is gone;
  2. gives the first idle miner a `MineAndSellGoal` for each untargeted opportunity;
  3. if there was no idle miner when the pass started, buys one drone per leftover opportunity,
     up to `Mining.MaxDrones` (default 20). It prefers a shipyard in the sell market's system.

### Trading (`TradingAutomationService`)

- **An opportunity** is a non-mineral good that is IMPORT and SCARCE at the sell market. The buy
  side is the first other market where that good is ABUNDANT and of type EXPORT or EXCHANGE.
- **Assignment:** idle traders (non-miners first) get a `TradeBetweenMarketsGoal`. Without an
  idle trader, it buys a `SHIP_LIGHT_HAULER` per unassigned opportunity. Only the budget check
  limits it.

### Purchasing

- **`ShipPurchaseService`:**
  1. It needs the ship type's price cached for that shipyard. Early in a reset, no ship has
     visited a shipyard yet, so purchases fail with "price unknown".
  2. It asks `BudgetPolicy`.
  3. It buys the ship and saves the new credits and the ship. Mounts are not recorded until the
     next startup sync. Mining capability also follows from the ship type, so a purchased
     `SHIP_MINING_DRONE` still counts as a miner.

  It publishes nothing.
- **`BudgetPolicy`:** spendable credits are the cached credits minus
  `FleetExpansion.MinCreditReserve`.
- **Buying order within a tick:** contract, probe, mining, trading. There is no other priority.

### Which ships a plan considers free

| Plan | Claims a ship with | Treats a ship as free when |
|---|---|---|
| Scout | an assignment and a goal | — (picks one ship, once) |
| Contract | an assignment only | it has no active assignment and is mining-capable; goals are ignored |
| ProbeDeployment | a `DeployProbeGoal` | it is a probe, not in transit, and not parked at a deployed waypoint |
| Mining | a `MineAndSellGoal` | it is mining-capable, not in transit, and has no goal (goals never count as finished, B16) |
| Trading | a `TradeBetweenMarketsGoal` | as for mining, plus fuel and free cargo |

**Consequences:**
- A drone that the contract plan just bought has no goal yet, so mining or trading can also give
  it one. The ship then gets both a goal step and contract commands in the same tick.
- After scouting, the command ship has no goal and no assignment. It is mining-capable, so
  mining, trading or a new contract plan can use it.

---

## 4. Ships: goals, executors and commands

### Goals

- **Storage:** each ship has at most one active goal, stored in `cached_ships` (`GoalId`,
  `GoalKind`, `GoalPayloadJson`, `GoalStatus`).
- **Kinds:** 13 kinds are defined, but only five are ever created: `ScoutWaypoint`,
  `DeployProbe`, `MineAndSell`, `TradeBetweenMarkets` and `SurveyWaypoint`.
- **Status:** `Assigned`, or `Blocked` once the circuit breaker stops the goal (see below).
  Nothing else changes it (B16). A blocked goal also records why, in `StatusReason`
  (`runaway`).
- **Set by:**
  - the scout, mining and trading plans;
  - `DeployProbeHandler`;
  - `MineAndSellGoalExecutor`, which assigns survey goals and can overwrite another ship's goal.
- **Cleared by:**
  - `DeployProbeGoalExecutor` and `TradeBetweenMarketsGoalExecutor` when they finish;
  - the scout plan, when its last stop is done;
  - mining and trading, for opportunities that have gone.

  Survey goals are never cleared.

### `ShipGoalExecutorService`

- **Loads** the ship with `FindAsync`. That skips the arrival dead-reckoning that `GetAllAsync`
  applies (B17).
- **Runs** one step of the executor for the active goal. Only the five kinds above are
  dispatched, so `IdleGoalExecutor` is unreachable.
- **Skips** every goal step while `Automation.Enabled` is off, whatever triggered it, and the
  goals of a plan that is switched off.
- **Skips** a goal that is `Blocked`. It stays blocked until a plan replaces it; mining and
  trading treat its ship as free, but the scout plan never replaces its goal.
- **Circuit breaker:** before each step it counts the ship's goal steps over the last minute
  (`GoalStepCircuitBreaker`, in memory). The tick alone takes 12. Above
  `Automation.CircuitBreaker.MaxGoalStepsPerMinute` (default 60) it doesn't run the step: it
  blocks the goal with reason `runaway`, logs a warning and counts
  `spacetraders_goal_breaker_trips_total{ship}`. A loop like B1 is stopped after 60 steps.
- **On Completed,** only a scout goal triggers anything: advancing the scout plan. No status,
  event or history row is written for any outcome.
- **Called by:**
  - the tick (every ship);
  - `ShipNavigationCompletedHandler`;
  - `DeployProbeHandler`;
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
| `DeployProbe` | Docked at the target: set DRIFT, advance the probe plan, clear the goal, complete. In orbit at the target: dock. Elsewhere: DRIFT if it has no fuel tank, then [cmd] navigate. |
| `MineAndSell` | **No target good aboard, no usable survey:** assign a survey goal to a survey-capable ship, possibly itself.<br>**With a survey:** [cmd] `MineResourceVolumeCommand`.<br>**Holding the good:** [cmd] navigate to the sell market, dock, then [API] sell. It never completes. |
| `TradeBetweenMarkets` | [cmd] navigate to the buy market and dock, [API] buy (free cargo, trade volume and credits limit the amount), then navigate to the sell market and dock, [API] sell, clear the goal, complete. |
| `SurveyWaypoint` | [cmd] navigate to the target. Docked there: orbit. In orbit: [API] survey, store the surveys in `cached_surveys`, complete. The goal is never cleared. |
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
  `ShipRefueledEvent`, the only source of ledger rows.
- **`MineResourceVolumeCommand` and `FulfillContractDeliveryCommand`** dead-reckon arrival
  themselves and navigate without a goal id (B17).
- **`PatchShipNavCommand`** changes the flight mode.
- **Selling and buying** are direct API calls from the executors. No events are published.
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
| `MarketDataRefreshedEvent` | Arrival at a market | `MarketPriceSampleHandler` (writes nothing, B19); the mining and trading `Handle` methods aren't discovered (see below) |
| `ShipNavigationCompletedEvent` | Arrival, after docking (`NavigateToWaypointArrivedCommand`) | `ShipNavigationCompletedHandler` → one goal step |
| `ShipRefueledEvent` | Refuel | `LedgerEntryHandler` → `ledger_entries` (FuelPurchase) |
| `ShipStateMismatchEvent` | State-gated commands | `activity_logs` row |
| `DeployProbeCommand` | Probe plan | `DeployProbeHandler` |
| `TokenResetMismatchDetectedEvent` | Agent bootstrap | `AlertHandler` (webhook); `activity_logs` row |
| `ShipCooldownExpiredEvent`, `ApiUnavailableEvent`, `ApiAvailableEvent` | Scheduler, tick | Nothing |

### Handled but never published

- **Ledger and activity events:** sales, purchases, repairs, mounts and modules, ship purchases,
  contract fulfilment.
- **`AgentCreditsChangedEvent`:** credit samples, the credits metric, alerts, the probe plan's
  waiting flag.
- **Alerts:** contract deadlines, reset warnings, cache divergence.
- **Activity-log-only events:** arrivals, idle ships, assignments, low fuel, construction,
  contract negotiation, acceptance and delivery, automation paused and resumed.
- **Contract plan events:** `DeliverableObtainedEvent` and `ContractDeliveryRecordedEvent`.

### Never dispatched

Domain aggregates (`Agent`, `Ship`, `Contract`) raise events into a list that nothing reads, and
production code doesn't use the aggregates at all (B7).

### Wolverine details

- **Discovery:** Wolverine finds handlers by convention (class names ending in `Handler` or
  `Consumer`). The `Handle` methods on `ContractPlanService`, `MiningAutomationService` and
  `TradingAutomationService` are therefore not wired. `DiValidationTests` checks this, because a
  plan that is switched off could otherwise still run from an event.
- **`InvokeAsync`** runs a command inline, and its exceptions reach the caller. Executors and
  the tick use it.
- **`PublishAsync`** goes through in-memory local queues. All events use it, as do
  `DeployProbeCommand` and `NavigateToWaypointArrivedCommand`.

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
- Above `Database.SoftLimitMegabytes` (1024) it logs `DbSizeSoftLimit` at Warning, once per
  crossing.
- Above `Database.HardLimitMegabytes` (3072) it switches `Automation.Enabled` off and logs
  `DbSizeHardLimit` at Error. It switches automation off again at every check while the
  database stays above the limit, so switching it back on only lasts once the database is smaller.
- Back under the soft limit it logs `DbSizeNormal`.
- Until there are anomalies (phase 3), these logs and the metric are how it shows.

### Tables

| Table | Holds | Written by | Retention |
|---|---|---|---|
| `stored_credentials` | Agent tokens, active token marker | Agent bootstrap | bounded: the active agent's token |
| `cached_agents` | Agent (credits, HQ) | Sync, bootstrap, purchases, sales, refuels, contract payments | bounded: one row |
| `cached_ships` | Ship state and the active goal | Sync (game state only), ship commands, goal repository | bounded: one row per ship. Created with `fillfactor=50` and `autovacuum_vacuum_threshold=10`, so its frequent updates stay in place and VACUUM runs (B32) |
| `cached_contracts` | Contracts | Sync, bootstrap, contract plan, delivery | bounded: the agent's contracts |
| `cached_markets`, `cached_shipyards` | Market and shipyard JSON | Sync, arrivals | bounded: one row per market or shipyard |
| `cached_waypoints`, `cached_systems` | Systems where ships are | Sync (insert only); scouting sets `LastObservedAt` | bounded: the systems the fleet has been in |
| `agent_settings` | Settings | Seed, `PUT /settings` | bounded: one row per setting |
| `ship_assignment_records` | Scout and contract assignment per ship | Scout and contract plans | bounded: one row per ship |
| `plan_states` | Plan JSON per plan type | All five plans | bounded: one row per plan |
| `scheduled_ship_events` | Arrival timers | Navigate | bounded: deleted when fired |
| `activity_logs` | Activity log | `LogActivityHandler`: transit, state mismatch, token reset | `ActivityLog.RetentionDays` (30) |
| `ledger_entries` | Credit ledger | `LedgerEntryHandler`: fuel purchases only (B7) | 30 days |
| `cached_surveys` | Surveys | Survey executor | bounded: expired rows removed when new surveys are saved |
| `leader_leases` | Leader lease | Leader election | bounded: one row |
| `api_endpoint_usages` | Call count per endpoint string | Every outbound call once the agent is known | bounded: one counter per endpoint |
| `runs` | Run summaries | Run lifecycle | 365 days, every agent's |
| `run_credit_highlights` | Start/end credits per run | Run lifecycle | 365 days |
| `startup_snapshots` | One full JSON snapshot per start | Startup snapshot | the agent's first and the last 10 |
| `market_price_samples` | Price history | Nothing, in practice (B19) | 7 days raw, first per hour to 90 days |
| `agent_credits_samples` | Credits over time | Nothing (B7) | 7 days raw, first per hour to 90 days |
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

### What each setting does

Only 14 of the 56 seeded settings change what the bot does (B18):

| Setting (default) | Effect |
|---|---|
| `Automation.Enabled` (true) | Off: no plans, goal steps or contract work, whatever would trigger them. Startup recovery skips. Startup snapshot switches it off and on. |
| `Automation.Plan.Scout.Enabled`, `.Contract.Enabled` (true); `.ProbeDeployment.Enabled`, `.Mining.Enabled`, `.Trading.Enabled` (false) | Off: the plan isn't bootstrapped, buys nothing and its ships' goals wait (D9) |
| `Automation.CircuitBreaker.MaxGoalStepsPerMinute` (60) | Goal steps per ship per minute above which the circuit breaker blocks the goal |
| `Api.BadGatewayPauseMinutes` (3) | Minutes without any API call after a 502 |
| `Database.SoftLimitMegabytes` (1024), `Database.HardLimitMegabytes` (3072) | Database size above which the size guard warns, or switches automation off (D8) |
| `FleetExpansion.MinCreditReserve` (100000) | Credits every purchase must leave untouched |
| `Mining.MaxDrones` (20) | Cap on drones bought by mining automation |
| `ActivityLog.RetentionDays` (30) | Activity log retention |
| `Alerts.WebhookUrl` (empty) | Where alerts are posted, as `{"text": …}`. Only the token-reset alert can fire. |

**The other 42:**

- **Run label only:** `FleetExpansion.PreferredShipType`, `Automation.MiningShipPercentage`.
- **Market views only:** `Trade.MinProfitPerUnit`, `Trade.MaxHaulDistance`. They are read by
  queries over the never-written `trade_opportunities`.
- **Read only by code that never runs:**
  - `Navigation.CriticalFuelRatio`, `LowFuelRatioForDrift`, `BurnFuelRatioMinimum` and
    `BurnDistanceThreshold` (`NavigationPlanningService` is never called);
  - `Maintenance.RepairConditionThreshold`, `MinIntegrityForLongRoutes`, `ScrapIntegrityThreshold`
    and `MinScrapValue` (`FleetMaintenancePlanner` isn't registered).
- **Status flags, not settings to tune:**
  - Read by the endpoints but never written: `Runtime.Reset.Next`, `Runtime.Alert.ApiUnavailable`,
    `CacheDivergence`, `ContractDeadlinesApproaching`, `ResetUpcoming`.
  - Written by bootstrap: `Runtime.Alert.TokenResetMismatch`.
  - Written but never read: `Runtime.TokenResetMismatchDetected`, `Runtime.AutomationPausedByReset`,
    `Runtime.Alert.AutomationDisabled`.
- **Read by nothing:**
  - `FleetExpansion.MinCreditRatioForShip`, `FleetExpansion.MaxShips`
  - `Contract.AutoAccept`
  - `Scout.MarketRefreshIntervalMinutes`, `Scout.ShipyardRefreshIntervalMinutes`
  - `Automation.Trade.MaxLossPerUnitBeforeReroute`
  - `Mining.SurveyMinimumCooldownSeconds`, `JettisonLowValueWhenFull`,
    `MinimumSellPriceToKeepCargo`, `ReserveHydrocarbonUnits`
  - `Maintenance.LongRouteJumpThreshold`
  - all six `Outfitting.*` keys
  - `Reliability.PauseAutomationBeforeReset`
  - `Runtime.Reset.Warning`, `Runtime.ApiUnavailable`, `Runtime.CacheDivergenceDetected`
- **Read but not seeded:** `BudgetPolicy` reads `Construction.FabMatsBuyThreshold`,
  `FabMatsTransactionSize` and `HourlyBudgetCapEnabled`, and nothing uses the result.

### Configuration (appsettings, user secrets, environment)

| Key | Used for |
|---|---|
| `ConnectionStrings:DefaultConnection` | PostgreSQL, for EF Core and Wolverine. Required. |
| `SpaceTraders:AccountToken`, `AgentName`, `AgentFaction`, `AgentToken` | Agent bootstrap and registration |
| `SpaceTradersApi:BaseUrl` | Game API base URL (`https://api.spacetraders.io/v2/`) |
| `SPACETRADERS_AGENT_TOKEN` | Initial agent data scope |
| `SPACETRADERS_INTERNAL_API_KEY` | Required `X-Api-Key` for the internal API when set |
| `WebUI:Origin` | CORS origin for the dashboard |
| `Serilog:*` | Log levels |
| `ASPNETCORE_ENVIRONMENT` | `Development`: user secrets and Swagger. `Production`: JSON logs. `Testing`: no database work. |

---

## 8. Outbound API client (`SpaceTraders.Infrastructure.SpaceTradersAPI`)

**Handler pipeline,** outermost first:

All three follow the API guide (https://spacetraders.io/api-guide/rate-limits, D3).

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
   both are used. Non-GET requests go first.

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
`SPACETRADERS_INTERNAL_API_KEY` is set. That includes `/metrics` (B11) and the SignalR hub. When
the key is unset, everything is open.

| Group | Endpoints | Notes |
|---|---|---|
| Health | `GET /health/live`, `/ready`, `/startup`, `/automation`, `/rate-limit/history` | No key needed |
| Status | `GET /status/agent`, `/ships`, `/ships/{s}/diagnostics`, `/waypoints/{s}`, `/contracts`, `/rate-limit`, `/activity?page&size&ship`, `/mining-opportunities`, `/startup-snapshots` (+ `/{id}/download`), `/system-alerts` | Cached data |
| Status (empty) | `GET /status/trade-opportunities`, `/top-trade-routes`, `/anomalies` | Read tables that are never written; always 204, `[]` or zeros |
| Universe | `GET /universe/systems`, `/jump-connections` | Jump connections are always `[]` |
| Runs and finance | `GET /runs/{id}/kpis`, `/finance/trade-routes` | KPIs is a stub; trade routes are always `[]` |
| Fleet | `GET /fleet/assignments`, `/activity`, `/activity/{ship}`, `/activity/{ship}/history`, `/goal-chains` | 5 s cache. History is always `[]` |
| Markets | `GET /markets/waypoints`, `/waypoints/{s}`, `/freshness`, `/goods/{s}/prices`, `/waypoints/{s}/prices`, `/best-routes` | Prices are empty (B19); best routes always 204 |
| Shipyards | `GET /shipyards/waypoints`, `/waypoints/{s}`, `/freshness` | |
| Settings | `GET /settings`, `PUT /settings/{key}`, `POST /settings/reset` | |
| Control | `POST /control/automation/enable`, `/disable` | Sets `Automation.Enabled` |
| Metrics | `GET /metrics` | See below |

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
  - One property name per concept: `ShipSymbol`, `ContractId`, `WaypointSymbol` (unless the
    message names a role, such as `Destination` or `SellWaypoint`), `GoalKind`.
  - Wolverine logs each handled message ("Successfully processed message …") under the message
    type's name, which the `Wolverine` level override doesn't reach. The Wolverine setup puts
    that line at Debug (`MessageSuccessLogLevel`, B29); the handlers log what happened themselves.
  - Every line logged during a tick carries `Tick`; a step's lines also carry its `Plan`, or its
    `ShipSymbol` (and `ContractId`). The game loop sets these with `ILogger.BeginScope`, which
    Serilog turns into properties.
- **Metrics** (`PrometheusMetricsService`, every 10 s), plus the prometheus-net defaults:

  | Metric | Updated? |
  |---|---|
  | `spacetraders_api_calls_total` | Yes, one per outbound request |
  | `spacetraders_api_throttled_total` | Yes, one per 429 response |
  | `spacetraders_agent_credits` | Never (B7) |
  | `spacetraders_goal_breaker_trips_total{ship}` | Yes, when the circuit breaker blocks a goal |
  | `spacetraders_db_size_bytes` | Yes, every 5 minutes (size guard) |

  `/metrics` requires the API key (B11), so Prometheus can't scrape it as deployed.

---

## 12. Code that exists but never runs

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

## 13. Build, tests and deployment

**Images:**
- `Dockerfile.api`: SDK 10 publish, then the ASP.NET 10 runtime on port 8080.
- `Dockerfile.webui`: Node 22 build, then nginx 1.27 on port 80 with the runtime-config
  entrypoint.

**CI** (`.github/workflows/ci-spacetraders.yml` in the parent repository):
1. Builds the API.
2. Runs `dotnet test SpaceTraders.slnx --filter "Category!=Integration"`.
3. Runs the WebUI tests and build.
4. On `main`, pushes `ghcr.io/gemberkoekje/spacetraders-api` and `-webui`, tagged `latest` and
   with the commit SHA.

There is no deploy step. The manifests live in gembernodes (`../PLAN.md`, phase 4).

**Tests:**

| Project | Tests | Covers |
|---|---|---|
| `SpaceTraders.Domain.Tests` | ~61 | Aggregates, events, goal serialization, value objects |
| `SpaceTraders.Application.Tests` | ~258 | Plans, commands, executors, budget policy, retry and 429 handlers (NSubstitute, EF in-memory) |
| `SpaceTraders.Infrastructure.Tests` | ~71 | Repositories, the initializer and retention against Testcontainers PostgreSQL (`Category=Integration`) |
| `SpaceTraders.API.Tests` | ~76 | WebApplicationFactory tests in `Testing`, DI validation, bootstrap and run lifecycle. Also message storage and the agent cleanup against Testcontainers PostgreSQL (`Category=Integration`), and sandbox tests against the live API (`Category=Sandbox`, need `SPACETRADERS_AGENT_TOKEN`). |
| `SpaceTraders.Integration.Test` | 1 | Replays the contract plan from a captured snapshot. No category, so CI runs it. |

**WebUI tests:**
- `npm test` runs Vitest.
- `npm run test:e2e` runs Playwright against a running stack. Its test opens `/orchestration`,
  which no longer exists (B24).
