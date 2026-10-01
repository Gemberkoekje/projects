# SpaceTraders — Plan

> Plan of 2026-10-01, with your decisions of the same day. This is the single plan for what happens next; the older plan documents
> (`basics-reset-plan.md`, `REFACTOR_PLAN_*.md`, `docs/*PLAN*.md`) are history (see slice 0.2).
> Mark slices `(done)` as they land, and keep the "Known issues" table current.

## Goal

1. The bot runs unattended on the cluster without endangering anything else on it.
2. You can see what it does and why, in Grafana, without needing an LLM to interpret it.
3. It makes money: contracts first, then mining and trading, and eventually the jump gate.

## Roles

- **You: strategy.** What the bot should do, settings, budgets, priorities. You read the dashboard
  and change settings yourself.
- **The bot: checks itself.** Health rules (phase 3) write down the intended behaviour. A broken
  rule raises an anomaly: a metric, a journal event and a Grafana alert.
- **Claude: mechanic.** Fixes things that don't work the way they are intended to, and nothing
  else. See "Claude's role" in `CLAUDE.md`.

## Where things stand (2026-10-01)

- The bot was taken off the cluster on 2026-05-26 (gembernodes `906fd99`), and its manifests were
  removed on 2026-09-26 (`3f9f785`). The 1Password item `spacetraders-secrets` still exists.
- The old database is gone, so the next run starts empty. That makes this the cheapest moment
  for schema changes: there is nothing to migrate.
- Nothing can confirm what filled Postgres in May: the data is lost, and Loki only started
  working on 2026-09-26, so no logs from any earlier run exist. The suspects in the table below
  come from reading the code.
- **Cluster facts that shape this plan (from gembernodes):**
  - **Postgres** is one Bitnami instance shared by every app: `postgresql.flux-system`, LAN
    address 192.168.1.232.
    - Its 32Gi volume sits on the QNAP NFS share that every PVC uses (Loki, Prometheus, Grafana,
      the backups, the file server).
    - The NFS CSI driver most likely doesn't enforce the 32Gi, so a runaway table can fill the
      whole share unless the QNAP share has a quota. Slice 1.6 exists for this reason.
  - **Logs:** Promtail ships every container log to Loki with namespace, pod, container and app
    labels. Loki keeps 31 days on a 10Gi volume.
  - **Metrics:** Prometheus (chart 25.x) scrapes pods annotated `prometheus.io/scrape`. The old
    deployment had no such annotation, and `/metrics` needs the API key anyway (B11), so the
    bot's metrics were never collected.
  - **Grafana** runs at http://192.168.1.230/grafana (LAN only). Its datasources are Prometheus
    and Loki. Dashboards and alert rules are provisioned from
    `gembernodes/infrastructure/monitoring/`, and alerts go out by email.
- `SpaceTradersV3/` was deleted on 2026-10-01 (D7). It was an unbuilt copy of this project's API
  client and interfaces; git history still has it.

## Known issues

Found by reading the code on 2026-10-01. None has been reproduced at runtime yet; each fix starts
with a test that does.

### Bugs: behaviour that contradicts the code's own intent

| # | Issue | Evidence | Slice |
|---|---|---|---|
| B1 | **"Already at destination" loop.** Navigating to the waypoint a ship is already at publishes `ShipNavigationCompletedEvent` without docking or orbiting. Its handler re-runs the goal executor, which asks to navigate there again. No API call is involved, so the rate limiter doesn't slow it, and the 5 s tick starts another chain every time. The survey executor hits this in its normal state right after any arrival (docked at the target). Most likely what filled the database. | `NavigateToWaypointCommand.cs:82-96`, `ShipNavigationCompletedHandler.cs:26`, `SurveyWaypointGoalExecutor.cs:52`, `ScoutWaypointGoalExecutor.cs:41`, `DeployProbeGoalExecutor.cs:48`, `MineAndSellGoalExecutor.cs:106` | 1.1 (done) |
| B2 | **The Wolverine inbox is probably never cleaned.** Every published message is stored in Postgres. Turning the durability agent off very likely also turns off the deletion of handled messages (about 80% sure; Wolverine's source was not checked for this version). | `Program.cs:180`, `Program.cs:189-191` | 1.3 |
| B3 | **Retention gaps.** Pruning only starts if every earlier startup step succeeded, and one failing table stops the tables after it. `startup_snapshots` (full JSON on every pod start), `runs` and `wolverine.*` are never pruned. All pruning is scoped to the current agent, so each server reset leaves the previous agent's rows behind for good. | `DeferredStartupHostedService.cs:57-76`, `DataRetentionService.cs:55-64` | 1.5 |
| B4 | **The agent JWT (about 1 KB) is part of every table's key**, and of its indexes. | `SpaceTradersDbContext.cs` (`AgentToken`, `HasMaxLength(1024)`) | 1.4 |
| B5 | **The automation kill switch doesn't stop the game loop.** Nothing in `Application/Automation/` reads `Automation.Enabled`. | `GameLoopService.cs:69-76` | 1.7 (done) |
| B6 | **A server reset during a run isn't detected.** Every call fails with 401 until the pod restarts, and the liveness check keeps passing. | `AgentBootstrapService` (runs only at startup) | 1.8 (done) |
| B7 | **Domain events are raised but never dispatched.** Nothing outside the domain reads `AggregateRoot.DomainEvents`. As a result:<br>• the ledger only holds fuel purchases;<br>• the credits gauge and the credit samples stay empty;<br>• below 200k credits the probe plan waits forever for an `AgentCreditsChanged` that never comes. | `Domain/Common/AggregateRoot.cs`, `ProbeDeploymentPlanService.cs:71` | 2.2 |
| B8 | **The contract miner leaves with a partial load.** The tick sends it to deliver as soon as any contract cargo is aboard, so the "fill up to required units or a full hold" logic never gets to run. | `GameLoopService.cs:118-139` vs `MineResourceVolumeCommand.cs:145-149` | 6.1 |
| B9 | **The contract plan never completes.** It only advances on `DeliverableObtainedEvent` and `ContractDeliveryRecordedEvent`, and nothing publishes either. After the contract is fulfilled, the plan stays Active and the assignment stays open. | `ContractPlanService.cs:227-266` | 6.1 |
| B10 | **The command ship idles after scouting.** When the scout plan completes, the ship's last `ScoutWaypointGoal` stays active, so mining and trading treat the ship as busy. It also writes two log lines every tick. | `ScoutAllMarketplacesPlanService.cs:162`, `MiningAutomationService.cs:388-405` | 6.2 |
| B11 | **Prometheus can't scrape `/metrics`.** Only `/health` is exempt from the API key. Separately, `spacetraders_api_throttled_total` counts every 25 ms local wait as a throttle. | `ApiKeyMiddleware.cs:17`, `RateLimitingHandler.cs` | 2.1 |
| B12 | **Log noise.** Information-level logs on every 5 s tick, `System.Net.Http` at Information (about 4 lines per API call), no correlation properties, and the ship symbol logged under three names (`ShipSymbol`, `Symbol`, `Ship`). The production JSON has no rendered message. | `Program.cs:60-74`, tick services | 1.9 |
| B13 | **API limits and errors don't follow the official guide** (https://spacetraders.io/api-guide/rate-limits; per D3 that makes them bugs).<br>• **Limit:** the guide allows 2 requests per second with a burst of 30 requests per 60 seconds, per IP and per account. The code makes every request take a token from both a 2/s bucket and a 30-per-60 s bucket, which caps the bot at 30 requests a minute: a quarter of the sustained rate. This reads "burst" as extra capacity on top of 2/s, the only reading in which a burst is faster than the normal rate; the guide doesn't spell out how the two combine, so the 429 counter must confirm it after the fix.<br>• **502:** the guide says to wait a few minutes. The code retries after 1, 2 and 4 seconds, then the tick keeps calling every 5 s, because nothing reads `IsAvailable`.<br>• **429 without `x-ratelimit-*` headers** (from the cloud infrastructure, not the rate limiter): the guide recommends exponential backoff. The code retries once after 1 second.<br>• **The buckets probably reset.** The limiter is registered as a transient handler, so the HttpClient factory recreates its buckets whenever it rebuilds the handler chain (every 2 minutes by default). | `RateLimitingHandler.cs:13-32`, `RateLimitResponseHandler.cs`, `RetryHandler.cs` | 1.10 |
| B14 | **One failing step stops the whole tick.** The tick has a single try/catch, so an exception in any plan skips every later plan, all ship steps and the contract commands, again every 5 s while it keeps failing. Example: until the scout plan has saved its state, scout ship selection throws whenever there isn't exactly one ship with fuel. | `GameLoopService.cs:33-44`, `ScoutShipSelectionService.cs:21-42` | 1.11 (done) |
| B15 | **The probe plan buys a probe every tick for a target whose probe is still travelling.** Each pass starts with an empty in-flight set, and a travelling probe doesn't count as available, so the target looks unserved. Only the credit reserve stops the purchases. | `ProbeDeploymentPlanService.cs:220-245, 336-345, 427-438` | 6.3 |
| B16 | **Goal status never changes, and scout and survey goals are never cleared.** `UpdateGoalStatusAsync` has no production caller, so every goal stays `Assigned` and the Completed/Blocked checks in mining and trading never match.<br>• A finished scout goal keeps the command ship "busy" (B10).<br>• When the mining executor replaces a miner's goal with a survey goal, that miner keeps surveying and never returns to mining. | `ShipGoalRepository.cs:70-81`, `MineAndSellGoalExecutor.cs:194-213`, `MiningAutomationService.cs:397` | 6.2, 6.4 |
| B17 | **Some ships stay "in transit" after arriving.**<br>• The arrival handler ignores a wake-up whose goal id doesn't match the ship's active goal, and the mining and contract commands navigate without a goal id.<br>• Executors reload the ship with `FindAsync`, which doesn't apply arrival dead-reckoning. Only `GetAllAsync` does, in memory.<br>• The contract commands dead-reckon for themselves, but a mining drone keeps seeing "in transit" after its first leg. | `ShipArrivedEventHandler.cs:26-34`, `ShipRepository.cs` (`FindAsync` vs `GetAllAsync`), `MineResourceVolumeCommand.cs:67-100` | 6.4 |
| B18 | **Most settings do nothing.** Of the 47 seeded settings, only `Automation.Enabled` (partly, see B5), `FleetExpansion.MinCreditReserve`, `Mining.MaxDrones`, `ActivityLog.RetentionDays` and `Alerts.WebhookUrl` change what the bot does.<br>• `Navigation.*` and `Maintenance.*` are read only by services that never run.<br>• `Trade.*` is read only by the market views.<br>• 21 keys are read by nothing at all.<br>• The `Runtime.*` keys are status flags, not settings to tune.<br>The settings table in `docs/HOW_IT_WORKS.md` lists each one. | `DefaultSettingsSeed.cs` | 2.6 |
| B19 | **Price history is never recorded.** Market trade goods are stored as camelCase JSON, but `MarketPriceSampleRepository` reads them back case-sensitively into PascalCase properties. Every good is skipped, so `market_price_samples` stays empty and the price endpoints return nothing. `MarketRepository` reads the same JSON case-insensitively, so mining and trading are unaffected. | `MarketPriceSampleRepository.cs:15, 119-127`, `SpaceTradersPortAdapter.cs:202` | 2.2 |
| B20 | **A restart clears every ship's active goal.** Startup sync overwrites each existing ship row with `SetValues(new CachedShip { … })`, and that object doesn't carry the goal columns, so they become null.<br>• A scout ship whose assignment already matches the current route step doesn't get its goal back.<br>• Arrival wake-ups scheduled before the restart no longer match any goal (B17). | `StartupSyncService.cs:106-130` | 1.12 (done) |
| B21 | **On an empty database the app tables may never be created** (to verify). `EnsureCreatedAsync` does nothing when the database already holds any table. Wolverine creates its `wolverine` tables when the host starts, before the deferred initializer runs, so the initializer's `ALTER TABLE` statements would then fail. The cluster's database will be empty on redeploy. | `SpaceTradersDatabaseInitializer.cs:12`, `Program.cs:75-78`, `DeferredStartupHostedService.cs:22-30` | 1.13 |
| B22 | **The dashboard publishes the internal API key.** The WebUI container writes the key into `config.js`, which anyone who can open the dashboard can read. The old ingress served both the dashboard and the API on the public `gemberkoekje.nl`, so anyone could call `PUT /settings/*` and `POST /control/*`. | `SpaceTraders.WebUI/docker-entrypoint.sh:11-29`, `SpaceTraders.WebUI/index.html:17`; gembernodes `3f9f785^:ingress/spacetraders-ingress.yaml` | 4.2 |
| B23 | **A failed startup leaves an idle pod that looks healthy.** One try/catch wraps the startup chain. If database init, agent bootstrap, the run lifecycle, startup sync or recovery throws, the later services (the tick and pruning among them) never start, and nothing retries. `/health/live` runs no checks, and the old deployment used it for the startup and liveness probes, so Kubernetes never restarts the pod. | `DeferredStartupHostedService.cs:57-89`, `Program.cs:146` | 1.11 (done) |
| B24 | **WebUI loose ends** (minor).<br>• SignalR refresh hints probably never match a query: the client reads a string `kind`, but the server sends an object.<br>• The end-to-end test opens `/orchestration`, but the route is `/plans`.<br>• The unrouted pages in `src/Future` call endpoints that don't exist. | `signalr.tsx:27-28`, `DashboardNotifier.cs:19,28`, `orchestration.e2e.ts:5` | with D5 |
| B25 | **The starting probe is probably not recognised as a probe.** Startup sync stores a ship's registration role as its type (`SATELLITE` for the starting probe), but the probe plan only accepts type `SHIP_PROBE` or a symbol containing `PROBE` or `SATELLITE`, and ship symbols look like `AGENT-2`. The plan then buys a probe instead of using the free one. | `StartupSyncService.cs:64`, `ProbeDeploymentPlanService.cs:495-498` | 6.3 |

### Decisions (2026-10-01)

Scope and strategy calls are yours; they are recorded here so nobody "fixes" them. New questions
get the next D-number.

| # | Question | Decision |
|---|---|---|
| D1 | Bootstrap stops once a contract plan is Completed or DeferredUnsupported (`ContractPlanService.cs:51-63`), so the bot never takes a second contract. | **Intended for now:** one contract per reset. Taking the next contract comes later. B9 still applies: after fulfilment the plan must complete and release the ship. |
| D2 | Non-mineral contracts are parked as unsupported (`ContractPlanService.cs:104-127`). | **Keep it simple:** they stay unsupported. Together with D1, a reset whose first contract isn't a mineral gets no contract. |
| D3 | Does the rate limiter follow the API's rules? | **It must follow the official guide; any difference is a bug** (B13, slice 1.10). |
| D4 | The probe plan waits for 200k credits (`ProbeDeploymentPlanService.cs:71`). | **Keep it for now;** tune once everything runs. |
| D5 | Keep the React WebUI, or let Grafana take over? | **Keep it for now;** decide later. |
| D6 | Exclude the `spacetraders` database from the nightly `pg_dumpall`? | **No change.** The size guard (1.6) keeps the database small, and the dump stays the consistent copy. A file-level copy of a running Postgres can only be restored reliably if the NAS snapshot is atomic. |
| D7 | Delete `SpaceTradersV3/`? | **Done 2026-10-01.** |
| D8 | Size guard limits (slice 1.6). | **Soft limit 1 GB** (anomaly), **hard limit 3 GB** (pause automation). |
| D9 | Which plans run in the first run after the redeploy? All five run on every tick today, and nothing can switch one off; slice 1.7 adds the switches. | **Scout and contract only.** Probes, mining and trading come on one at a time in phase 6, after their known issues (B15–B17, B25) are fixed. |
| D10 | Remove the settings that nothing reads (B18), or keep them as placeholders? | **Remove them**, so the settings page shows only settings that work. A feature that needs one adds it back (slice 2.6). |
| D11 | Should the dashboard and the internal API stay reachable from the internet (B22)? | **LAN only**, like Grafana (slice 4.2). |

## Phases

Each slice is one PR, including tests. All of phase 1 must be done before the bot goes back on the
cluster (phase 4).

### Phase 0: Housekeeping

**0.1 Claude's role in `CLAUDE.md`** (done)

**0.2 Archive the old plans** (done)
- Moved the old plans, designs, progress logs, the strategy notes, `docs/implementation/*` and
  `docs/operations/LOCAL_DEVELOPMENT.md` into `docs/archive/`. Its README says what each file is,
  and that none of them describes the current code.
- The current docs are `README.md`, `PLAN.md`, `docs/HOW_IT_WORKS.md`, `docs/GLOSSARY.md`,
  `spacetraders.md` (solution overview: stack, configuration, conventions), `CONTRIBUTING.md` and
  `CHANGELOG.md`.

**0.3 `docs/HOW_IT_WORKS.md`** (done; kept current by every PR that changes behaviour)
- Do: describe the current runtime from the code:
  - the startup chain;
  - the 5 s tick and the plans it bootstraps;
  - goal executors, events and handlers;
  - every table and its retention.
- Also fix the stale parts of `README.md`: the `SpaceTraders.App` project, the `k8s/` folder,
  and `/control/sync` and `/reassign`, none of which exist anymore.
- Done when: every later PR that changes behaviour updates this file.

**0.4 Remove `SpaceTradersV3/`** (done)

### Phase 1: Safe to run

**1.1 Fix the "already at destination" loop (B1)** (done)
- Goal: navigating to the waypoint a ship is already at ends in the state the caller needs, and
  never re-triggers the executor without progress.
- Do: either have `NavigateToWaypointHandler`'s step 1 dock before emitting the completed event,
  or have the executors handle "at target" themselves without calling navigate. Pick whichever
  keeps a single obvious path.
- Done when: for every executor, tests cover both "docked at target" and "in orbit at target",
  and each case finishes in one step without publishing `ShipNavigationCompletedEvent` again.
- Done:
  - The executors handle "at target" themselves. Docking first would not have fixed the survey
    executor, which needs orbit. In orbit at the target, the scout, probe and mining executors
    now dock, like the trade executor already did; docked at the target, the survey executor
    orbits. That is the whole step; the next one does the work.
  - Navigating to the waypoint a ship is already at does nothing and logs a warning. It no
    longer publishes `ShipNavigationCompletedEvent`, so that event only follows a real arrival.
  - `AlreadyAtDestinationLoopTests` reproduces the loop through the real navigate and
    navigation-completed handlers: before the fix, each of the four cases ran until the test's
    limit of 10 steps.

**1.2 Per-ship circuit breaker** (done)
- Goal: any future loop is contained to one ship, and it shows up.
- Do: count goal steps per ship in a sliding window. Above N per minute (a setting), the ship's
  goal becomes Blocked with the reason `runaway`. Until phase 3 exists, this means a Warning log
  plus a metric; after that, an anomaly.
- Done when: a test with a looping fake executor trips the breaker.
- Done:
  - `ShipGoalExecutorService` counts every goal step per ship over the last minute, whatever
    triggered it. Above `Automation.CircuitBreaker.MaxGoalStepsPerMinute` it blocks the goal
    (`StatusReason` = `runaway`), logs a warning and counts
    `spacetraders_goal_breaker_trips_total{ship}`. Blocked goals are not stepped again.
  - The default of 60 is five times what the tick takes (12 a minute); a loop like B1 takes
    thousands. It's a setting, so it's yours to tune.
  - A blocked goal stays blocked until a plan replaces it. The scout plan never does, so a
    tripped scout ship waits for someone to look at it.

**1.3 Stop storing in-process messages (B2)**
- Background: the durable queues were added on purpose, as an outbox. A message is stored in the
  same transaction as the database change, so a crash can't lose it (phase 2 of
  `docs/archive/RACE_CONDITION_PREVENTION_IMPLEMENTATION.md`). The same document names startup
  recovery as the second line of defence.
- Do: choose one of these and record the choice in `HOW_IT_WORKS.md`:
  - **Turn the durability agent back on**, which keeps the outbox and should clean up handled
    messages.
  - **Remove `UseDurableLocalQueues()`** and rely on `StartupRecoveryService` and
    `scheduled_ship_events` to resume ships after a restart. If nothing else needs Wolverine's
    Postgres storage, drop `PersistMessagesWithPostgresql` too.
- Done when: the soak test (1.14) shows no `wolverine` tables, or flat ones.

**1.4 Short agent identity, old agents cleaned up (B4, part of B3)**
- Do: key rows on a short agent id (agent symbol plus reset date, or a small surrogate key)
  instead of the JWT; the token itself is stored only in the credentials table. When a new agent
  is registered after a reset, delete the previous agents' rows and keep only their run summary
  (`runs`).
- Done when: no table other than credentials stores the token, and the cleanup has a test.

**1.5 Retention for every table that grows (B3)**
- Do:
  - give each table a policy: by age, by row count, or explicitly bounded;
  - add `startup_snapshots` (keep the last N) and `runs`;
  - start pruning independently of the startup chain;
  - run each table's prune separately, so a failure logs and moves on;
  - rewrite the `NOT IN` downsampling delete so it fits the 30 s command timeout.
- Done when: a test fails for any `DbSet` without a retention policy or an explicit "bounded"
  mark, so a new table can't slip through.

**1.6 Database size guard**
- Goal: the bot can't fill the shared Postgres volume or the NAS share.
- Do: every few minutes, read `pg_database_size(current_database())` and export it as
  `spacetraders_db_size_bytes`. Above the soft limit, raise an anomaly; above the hard limit,
  pause automation (this needs 1.7). The limits are settings, starting at 1 GB and 3 GB (D8).
- Done when: tests with a fake size source cover both limits.

**1.7 A kill switch that works (B5), and one switch per plan (D9)** (done)
- Do:
  - make the tick, the plan bootstraps and scheduler-triggered goal steps all respect
    `Automation.Enabled`;
  - add one setting per plan (`Automation.Plan.Scout.Enabled`, `.Contract`,
    `.ProbeDeployment`, `.Mining`, `.Trading`). The tick skips a disabled plan's bootstrap.
    Per D9, Scout and Contract default to on, the other three to off.
- Done when: tests show that a tick with automation disabled issues no ship commands, and that a
  disabled plan neither bootstraps nor buys anything.
- Done:
  - The tick checks `Automation.Enabled` first. So does `ShipGoalExecutorService`, which every
    goal step goes through (the tick, arrivals, the probe handler, startup recovery).
  - A plan that is off isn't bootstrapped, which is where all buying happens. A test checks that
    Wolverine doesn't wire the plan services' `Handle` methods, so no event can run a plan either.
  - One step further than the "Do": a plan that is off also doesn't move its ships. Their goals
    are skipped (they wait, they aren't cleared), and the tick's contract work is skipped when the
    contract plan is off. Otherwise switching mining off mid-run would leave its drones mining.
  - An arriving ship still docks and refreshes its market while automation is off: that finishes
    a command issued before. Only the goal step after it is skipped.

**1.8 Server reset during a run (B6)** (done)
- Do: a 401 with the reset-date error pauses automation, logs `ResetDetected`, and stops the
  host. Kubernetes restarts the pod, and startup already registers the new agent through the
  account token. Simpler than re-bootstrapping in-process.
- Done when: a test with a fake port returning the reset error stops the host.
- Done:
  - The API client notices it, not the callers: every failed call goes through one method, so
    no caller can swallow the error first (the contract plan, for one, ignores API errors). The
    test therefore fakes the server's HTTP response rather than the port.
  - `ServerResetMonitor` switches `Automation.Enabled` off (for the old agent), logs
    `ResetDetected` at Critical and stops the host, once.
  - It ignores reports until startup has completed, because agent bootstrap tries old tokens on
    purpose. A reset in the middle of startup makes that step fail, which stops the host (1.11).
  - The new agent starts with the default settings, because settings are stored per agent.

**1.9 Log diet (B12)**
- Do:
  - move per-tick messages to Debug, or log them only when something changes;
  - set `System.Net.Http` to Warning in the Serilog section (done in `appsettings*.json`; the old
    ConfigMap's `Logging__LogLevel__*` variables probably never reached Serilog);
  - use `RenderedCompactJsonFormatter` in Production;
  - use one property name per concept (`ShipSymbol`, `ContractId`, `WaypointSymbol`);
  - push tick and plan context through `LogContext`.
- Done when: an idle bot writes less than one line a minute, and a normal day stays within a
  budget of 50k lines. Loki's 31 days on 10Gi are shared with every app.

**1.10 Follow the API guide for limits and errors (B13)**
- Do: make the client do what https://spacetraders.io/api-guide/rate-limits says:
  - **Limit:** 2 requests per second sustained. When that's used up, a request may draw from a
    burst pool of 30 that refills over 60 seconds. Only when both are empty does it wait.
  - **429 with `x-ratelimit-type`** (the rate limiter): wait until `x-ratelimit-reset`, or
    `retry-after` if that's missing, then retry.
  - **429 without those headers** (the cloud infrastructure): retry with exponential backoff,
    up to a cap.
  - **502** (DDoS protection): stop all outbound calls for a few minutes (a setting, default 3),
    then try one call. The tick and the plan bootstraps skip while paused. Log
    `ApiUnavailable` and `ApiAvailable` as journal events.
  - Correct the "Burst Limit" entry in `docs/GLOSSARY.md` (done).
- The limit is per IP and per account. Never run two instances against the same account at the
  same time (for example, the soak test while the bot runs on the cluster).
- Done when:
  - tests cover each of the four behaviours above;
  - after redeploy, the count of real 429s stays at zero, which confirms the burst reading (the
    429 rule in 3.2 watches this).

**1.11 Failures don't silently stop work (B14, B23)** (done)
- Do:
  - run each tick step (each plan bootstrap, each ship's goal step, each contract assignment)
    in its own try/catch that logs the step and the ship;
  - make a failing startup chain stop the host, so Kubernetes restarts the pod with back-off;
  - use `/health/startup` for the startup probe (4.2).
- Done when: tests show that a throwing plan doesn't stop the other plans or the ship steps, and
  that a throwing startup step stops the host.
- Done:
  - Each tick step also gets its own DI scope, so a step that fails halfway can't leave a broken
    DbContext (with unsaved changes) to every step after it.
  - A failed startup chain logs at Critical, stops the host and exits with code 1.
  - The startup probe is still to do, in the manifests (4.2).

**1.12 Restarts keep ship goals (B20)** (done)
- Do: startup sync updates a ship's game state without touching its goal columns.
- Done when: a test syncs a ship that has an active goal, and the goal is still there afterwards.
- Done: sync sets the game-state columns one by one instead of replacing the whole row. It now
  also sets `LocalStatus` from the nav status, which the old code reset to `None`.

**1.13 An empty database gets every table (B21)**
- Do: check against an empty Postgres. If B21 is confirmed, create the app tables explicitly, or
  initialise them before Wolverine sets up its storage.
- Done when: starting against an empty database creates every app table, and an integration test
  covers it.

**1.14 Soak test**
- Do: start from an empty local database (Postgres in Docker), and run against the live API for a
  few hours with all of phase 1 in, while the cluster bot is off. Every 15 minutes, record table
  sizes and message counts.
- Done when: tables grow only with real game activity (ledger, activity log), nothing in
  `wolverine` grows, and the breaker never trips.

### Phase 2: Visibility

Prometheus holds the numbers, Loki holds the events, and Grafana shows both. There is no
Grafana-to-Postgres datasource and no new journal table: Loki already keeps 31 days and handles
its own retention, so the bot's database stays small.

**2.1 Metrics Prometheus can scrape (B11)**
- Do: serve `/metrics` on a separate port that the Service and ingress don't route. It then needs
  no API key and isn't public (the API itself is on the public ingress).
  - Fix the credits gauge.
  - Replace the throttle counter with a wait-time counter and a counter for real 429s.
  - Label API calls by route template, not by raw path.
  - Add these metrics, all low-cardinality:
    - credits;
    - credits earned by source and spent by category;
    - ships by role and state;
    - per-ship status (ship, role, state, goal, reason);
    - contract units required and fulfilled;
    - API requests by endpoint and status;
    - rate-limit wait time;
    - handled messages by type;
    - goal steps by kind;
    - breaker trips;
    - active anomalies;
    - database size;
    - next reset time.
- Done when: a local scrape returns all of them without an API key.

**2.2 Record what happens (B7, B19)**
- Do:
  - dispatch aggregate domain events, or publish where the change happens, so that credit
    changes, sales, purchases, contract payments and ship purchases reach the ledger and the
    metrics;
  - read the trade-goods JSON case-insensitively in `MarketPriceSampleRepository`, like
    `MarketRepository` does, so price history gets recorded.
- Done when: tests show each of those four producing a ledger row and moving its counter, and a
  market refresh writing price samples.

**2.3 Journal events**
- Do: write one Information event per meaningful thing, each with an `EventKind` and the standard
  properties:
  - contracts: ContractAccepted, ContractDelivered, ContractFulfilled;
  - money: ShipPurchased, CargoBought, CargoSold;
  - plans: PlanStarted, PlanCompleted, and PlanBlocked with a reason;
  - ships: ShipIdle and ShipBlocked, each with a reason;
  - settings: SettingChanged, with key, old value and new value;
  - ResetDetected, AnomalyRaised and AnomalyCleared.
- Done when: `{namespace="spacetraders"} | json | EventKind != ""` in Grafana reads as a timeline
  of the run.

**2.4 Grafana dashboard** (gembernodes)
- Location: `infrastructure/monitoring/dashboards/spacetraders-dashboard.json`, added to the
  monitoring `configMapGenerator`.
- Panels:
  - credits, and credits per hour;
  - earned and spent by source;
  - fleet table: ship, role, state, goal, time in state, reason;
  - contracts: required vs. fulfilled, and the deadline;
  - API calls and 429s;
  - messages per minute by type;
  - database size;
  - active anomalies;
  - the journal (from Loki).
- `SettingChanged` and `ResetDetected` appear as annotations, so you can see what changed when.

**2.5 Grafana alerts** (gembernodes `grafana-alerting-provisioning.yaml`)
- Rules:
  - the bot is down (no scrape for 10 minutes);
  - an anomaly has been active for more than 15 minutes;
  - the database is over the soft limit;
  - log volume is over budget;
  - add `spacetraders` to the existing error-log rule.
- Grafana only reads this file at startup, so it needs a rollout restart.

**2.6 Only settings that do something (B18, D10)**
- Do: remove from the seed every setting that nothing reads at runtime:
  - the 21 keys that no code reads;
  - the `Navigation.*` and `Maintenance.*` keys, which only code that never runs reads.

  Then update the settings table in `docs/HOW_IT_WORKS.md`. Keep the `Runtime.*` status flags
  for now; moving them out of the settings is a separate cleanup. The database starts empty, so
  no old rows need removing.
- Done when: apart from the `Runtime.*` flags, every seeded setting is read by code that runs.

### Phase 3: Health rules (the bot checks itself)

**3.1 Rule mechanism**
- Do: evaluate the rules every minute against the bot's own state.
  - Each violation is an anomaly: rule, subject, since when, details.
  - It's exposed as `spacetraders_anomaly_active{rule,subject}` and as AnomalyRaised and
    AnomalyCleared events.
  - Thresholds are settings.
- Done when: there's a unit test per rule.

**3.2 First set of rules.** Each one states an intended behaviour:
- **Contracts:**
  - an accepted contract makes progress within N hours;
  - a fulfilled contract has no active plan or assignment;
  - a deadline within N hours comes with at least X% delivered.
- **Ships:**
  - a ship with a goal changes state within N minutes, unless it's in transit;
  - a ship without a goal is idle for at most N minutes;
  - the circuit breaker hasn't tripped.
- **Errors:** the same error repeats at most N times in 10 minutes.
- **Credits:** they change at least once in 24 hours while automation is enabled.
- **API:** no 401 or reset errors, and at most N 429s an hour.
- **Database:** under the soft size limit.
- Done when: a deliberately broken scenario in a test host raises each rule.

### Phase 4: Back on the cluster (gembernodes)

**4.1 Database logins**
- Do: check the connection string in the 1Password item `spacetraders-secrets`.
  - It should use a dedicated, non-superuser login that owns only the `spacetraders` database;
    create both if they're missing.
  - Add a read-only login (for example `spacetraders_ro`) for Claude on your PC.
- Note: a separate database on the shared server doesn't cap disk usage. That's the job of 1.6
  and the alert in 2.5.

**4.2 Manifests**
- Do: restore `apps/spacetraders/`, the namespace and the ingress (from `3f9f785^`), with these
  changes:
  - Prometheus annotations for the metrics port;
  - a ConfigMap with Serilog overrides;
  - no route to the old `spacetraders-app-service`, because that project no longer exists;
  - the WebUI deployment comes back too (D5);
  - a LAN-only ingress (D11, B22), modelled on `grafana-internal-ingress.yaml`:
    - no public hosts;
    - `nginx.ingress.kubernetes.io/whitelist-source-range: "192.168.0.0/16,10.0.0.0/8"`;
    - `/spacetraders/api` and `/spacetraders/dashboard` on http://192.168.1.230;
    - no certificate needed;
  - `/health/startup` as the startup probe (B23).

**4.3 First-run watch**
- Only the scout and contract plans are on (D9).
- First hour: messages per minute, database size, log lines per minute, anomalies. Then check
  again after 24 hours, then after a full reset period.
- Phase 6 starts after a clean reset period.

### Phase 5: Claude as mechanic (on your PC)

**5.1 The `/st-investigate` skill** (draft written: `SpaceTraders/.claude/skills/st-investigate/SKILL.md`)
- The draft marks which data sources arrive with phases 2–4. Finish it once they exist, and
  check it against a real run.
- Input: an anomaly (rule plus subject), or `week`.
- Reads:
  - **Prometheus and Loki:** anomalies, metrics, journal events and errors. Access through
    Grafana's datasource proxy at http://192.168.1.230/grafana, or through
    `kubectl port-forward` to `grafana-loki:3100` and `prometheus-server`.
  - **Postgres:** current state at 192.168.1.232, with the read-only login.
  - **Pod status:** mcp-k8s, which is read-only.
- Does:
  - finds the code path;
  - writes a failing test that reproduces the misbehaviour, then fixes it;
  - opens a PR with the evidence (queries and log lines).
- Never: changes settings, or tunes thresholds, budgets or priorities. Observations about
  strategy go under "Noticed".

**5.2 Permissions**
- Allowed without asking: `kubectl get`, `kubectl logs`, `kubectl port-forward`, `psql` with the
  read-only login, and reads from Grafana. Everything else asks.
- Waits for 4.1, because the read-only login doesn't exist yet. These rules widen what Claude may
  do on your cluster without asking, so they go in only with your explicit go-ahead.
- Done when: the skill reproduces and explains one real anomaly from the first run.

### Phase 6: Make money, one loop at a time

A loop counts as done after a full reset period with no open anomalies for it on the dashboard.
How credits are split stays your call; Claude only fixes deviations from intended behaviour.

- **6.1 The first contract, end to end:** B8 and B9. Per D1 and D2 the bot takes one mineral
  contract per reset; taking the next contract is a later addition.
- **6.2 The command ship after scouting:** B10, and the scout part of B16. The ship moves on to
  its next job instead of holding on to the finished scout goal.
- **6.3 Probes** (`ProbeDeploymentPlanService`): B15 and B25.
- **6.4 Mining drones mine and sell** (`MiningAutomationService`, `MineAndSellGoalExecutor`): the
  survey part of B16, and B17.
- **6.5 Trading** (`TradingAutomationService`).
- **6.6 Jump gate construction.**

## Changes in gembernodes

This cloud session can only read gembernodes. These changes are made from your PC or by hand:

| Slice | Change |
|---|---|
| 2.4 | `infrastructure/monitoring/dashboards/spacetraders-dashboard.json` plus a `configMapGenerator` entry |
| 2.5 | Rules in `infrastructure/monitoring/grafana-alerting-provisioning.yaml`, then a Grafana rollout restart |
| 4.1 | Database login and read-only login (Postgres and 1Password) |
| 4.2 | `apps/spacetraders/`, `namespaces/spacetraders-namespace.yaml`, `ingress/spacetraders-ingress.yaml`, plus the kustomization entries |
