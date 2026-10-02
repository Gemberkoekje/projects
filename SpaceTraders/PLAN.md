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

## Where things stand (2026-10-02)

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
- Phase 1 is done (2026-10-01): the soak test (1.14) ran the bot for four hours against the live
  API from an empty local database.
- Phase 2 is done in the code (2026-10-01): metrics, the ledger and the journal. Its Grafana
  dashboard and alerts (2.4, 2.5) were merged in gembernodes (PR #10) and applied by Flux on
  2026-10-01, and Grafana has read the alert rules since its restart on 2026-10-02.
- Phase 3 is done in the code (2026-10-01): ten health rules check the bot's own state every
  minute, and each broken one is an anomaly (a metric and journal lines).
- Phase 4 is merged (2026-10-02): gembernodes PR #11 put the bot back on the cluster at 08:50Z, at
  main `3373ca7` (with B39–B41), and it registered a new agent. Grafana restarted at 09:09Z, so its
  alerts, "bot is down" included, are live. The first-run watch (4.3) is under way; its first
  minutes found B42–B44.
- Phase 5 is done (2026-10-02): the `st-investigate` skill is finished and was checked against the
  first run (5.1), where it explained B42's anomaly and found B45; the helper and the bot's pod logs
  run without asking (5.2).
- Slice 6.5 (trading) is built on branch `claude/spacetraders-trading` (2026-10-02), with your
  decisions D14–D19; it found B46 (fixed with it) and B47 (open). The trading plan stays off until
  you switch it on (D9).

## Known issues

Found by reading the code on 2026-10-01, unless a row says where it was found. The soak test (1.14)
saw B8, B9, B10 and B27 at runtime and found B28–B36. Each fix starts with a test that reproduces
the misbehaviour.

### Bugs: behaviour that contradicts the code's own intent

| # | Issue | Evidence | Slice |
|---|---|---|---|
| B1 | **"Already at destination" loop.** Navigating to the waypoint a ship is already at publishes `ShipNavigationCompletedEvent` without docking or orbiting. Its handler re-runs the goal executor, which asks to navigate there again. No API call is involved, so the rate limiter doesn't slow it, and the 5 s tick starts another chain every time. The survey executor hits this in its normal state right after any arrival (docked at the target). Most likely what filled the database. | `NavigateToWaypointCommand.cs:82-96`, `ShipNavigationCompletedHandler.cs:26`, `SurveyWaypointGoalExecutor.cs:52`, `ScoutWaypointGoalExecutor.cs:41`, `DeployProbeGoalExecutor.cs:48`, `MineAndSellGoalExecutor.cs:106` | 1.1 (done) |
| B2 | **The Wolverine inbox is never cleaned.** Every published message is stored in Postgres. Turning the durability agent off also turns off the deletion of handled messages: confirmed against Postgres in 1.3, where 50 handled messages were all still there a minute later (with the agent on they were gone). | `Program.cs:180`, `Program.cs:189-191` | 1.3 (done) |
| B3 | **Retention gaps.** Pruning only starts if every earlier startup step succeeded, and one failing table stops the tables after it. `startup_snapshots` (full JSON on every pod start), `runs` and `wolverine.*` are never pruned. All pruning is scoped to the current agent, so each server reset leaves the previous agent's rows behind for good (fixed in 1.4: agent bootstrap deletes them). | `DeferredStartupHostedService.cs:57-76`, `DataRetentionService.cs:55-64` | 1.4 (done: earlier agents), 1.5 (done) |
| B4 | **The agent JWT (about 1 KB) is part of every table's key**, and of its indexes. | `SpaceTradersDbContext.cs` (`AgentToken`, `HasMaxLength(1024)`) | 1.4 (done) |
| B5 | **The automation kill switch doesn't stop the game loop.** Nothing in `Application/Automation/` reads `Automation.Enabled`. | `GameLoopService.cs:69-76` | 1.7 (done) |
| B6 | **A server reset during a run isn't detected.** Every call fails with 401 until the pod restarts, and the liveness check keeps passing. | `AgentBootstrapService` (runs only at startup) | 1.8 (done) |
| B7 | **Domain events are raised but never dispatched.** Nothing outside the domain reads `AggregateRoot.DomainEvents`. As a result:<br>• the ledger only holds fuel purchases;<br>• the credits gauge and the credit samples stay empty;<br>• below 200k credits the probe plan waits forever for an `AgentCreditsChanged` that never comes. | `Domain/Common/AggregateRoot.cs`, `ProbeDeploymentPlanService.cs:71` | 2.2 (done) |
| B8 | **The contract miner leaves with a partial load.** The tick sends it to deliver as soon as any contract cargo is aboard, so the "fill up to required units or a full hold" logic never gets to run. | `GameLoopService.cs:118-139` vs `MineResourceVolumeCommand.cs:145-149` | 1.14 (done) |
| B9 | **The contract plan never completes.** It only advances on `DeliverableObtainedEvent` and `ContractDeliveryRecordedEvent`, and nothing publishes either. After the contract is fulfilled, the plan stays Active and the assignment stays open. | `ContractPlanService.cs:227-266` | 1.14 (done) |
| B10 | **The command ship idles after scouting.** When the scout plan completes, the ship's last `ScoutWaypointGoal` stays active, so mining and trading treat the ship as busy. It also writes two log lines every tick (Debug since 1.9), and every tick it marks its last waypoint visited again: in 1.14, 178 updates of `cached_waypoints` in 15 minutes. | `ScoutAllMarketplacesPlanService.cs:162`, `MiningAutomationService.cs:388-405` | 1.14 (done) |
| B11 | **Prometheus can't scrape `/metrics`.** Only `/health` is exempt from the API key. (`spacetraders_api_throttled_total` also counted every 25 ms local wait as a throttle; since 1.10 it counts 429 responses only.) | `ApiKeyMiddleware.cs:17`, `RateLimitingHandler.cs` | 2.1 (done) |
| B12 | **Log noise.** Information-level logs on every 5 s tick, `System.Net.Http` at Information (about 4 lines per API call), no correlation properties, and the ship symbol logged under three names (`ShipSymbol`, `Symbol`, `Ship`). The production JSON has no rendered message. | `Program.cs:60-74`, tick services | 1.9 (done) |
| B13 | **API limits and errors don't follow the official guide** (https://spacetraders.io/api-guide/rate-limits; per D3 that makes them bugs).<br>• **Limit:** the guide allows 2 requests per second with a burst of 30 requests per 60 seconds, per IP and per account. The code makes every request take a token from both a 2/s bucket and a 30-per-60 s bucket, which caps the bot at 30 requests a minute: a quarter of the sustained rate. This reads "burst" as extra capacity on top of 2/s, the only reading in which a burst is faster than the normal rate; the guide doesn't spell out how the two combine, so the 429 counter must confirm it after the fix.<br>• **502:** the guide says to wait a few minutes. The code retries after 1, 2 and 4 seconds, then the tick keeps calling every 5 s, because nothing reads `IsAvailable`.<br>• **429 without `x-ratelimit-*` headers** (from the cloud infrastructure, not the rate limiter): the guide recommends exponential backoff. The code retries once after 1 second.<br>• **The buckets probably reset.** The limiter is registered as a transient handler, so the HttpClient factory recreates its buckets whenever it rebuilds the handler chain (every 2 minutes by default). | `RateLimitingHandler.cs:13-32`, `RateLimitResponseHandler.cs`, `RetryHandler.cs` | 1.10 (done) |
| B14 | **One failing step stops the whole tick.** The tick has a single try/catch, so an exception in any plan skips every later plan, all ship steps and the contract commands, again every 5 s while it keeps failing. Example: until the scout plan has saved its state, scout ship selection throws whenever there isn't exactly one ship with fuel. | `GameLoopService.cs:33-44`, `ScoutShipSelectionService.cs:21-42` | 1.11 (done) |
| B15 | **The probe plan buys a probe every tick for a target whose probe is still travelling.** Each pass starts with an empty in-flight set, and a travelling probe doesn't count as available, so the target looks unserved. Only the credit reserve stops the purchases. | `ProbeDeploymentPlanService.cs:220-245, 336-345, 427-438` | 6.3 |
| B16 | **Goal status never changes, and scout and survey goals are never cleared.** `UpdateGoalStatusAsync` has no production caller, so every goal stays `Assigned` and the Completed/Blocked checks in mining and trading never match.<br>• A finished scout goal keeps the command ship "busy" (B10).<br>• When the mining executor replaces a miner's goal with a survey goal, that miner keeps surveying and never returns to mining. | `ShipGoalRepository.cs:70-81`, `MineAndSellGoalExecutor.cs:194-213`, `MiningAutomationService.cs:397` | scout part: 1.14 (done); 6.4 |
| B17 | **Some ships stay "in transit" after arriving.**<br>• The arrival handler ignores a wake-up whose goal id doesn't match the ship's active goal, and the mining and contract commands navigate without a goal id.<br>• Executors reload the ship with `FindAsync`, which doesn't apply arrival dead-reckoning. Only `GetAllAsync` does, in memory.<br>• The contract commands dead-reckon for themselves, but a mining drone keeps seeing "in transit" after its first leg. | `ShipArrivedEventHandler.cs:26-34`, `ShipRepository.cs` (`FindAsync` vs `GetAllAsync`), `MineResourceVolumeCommand.cs:67-100` | 6.4 |
| B18 | **Most settings do nothing.** Of the 47 seeded settings, only `Automation.Enabled` (partly, see B5), `FleetExpansion.MinCreditReserve`, `Mining.MaxDrones`, `ActivityLog.RetentionDays` and `Alerts.WebhookUrl` change what the bot does.<br>• `Navigation.*` and `Maintenance.*` are read only by services that never run.<br>• `Trade.*` is read only by the market views.<br>• 21 keys are read by nothing at all.<br>• The `Runtime.*` keys are status flags, not settings to tune.<br>The settings table in `docs/HOW_IT_WORKS.md` lists each one. | `DefaultSettingsSeed.cs` | 2.6 (done) |
| B19 | **Price history is never recorded.** Market trade goods are stored as camelCase JSON, but `MarketPriceSampleRepository` reads them back case-sensitively into PascalCase properties. Every good is skipped, so `market_price_samples` stays empty and the price endpoints return nothing. `MarketRepository` reads the same JSON case-insensitively, so mining and trading are unaffected. | `MarketPriceSampleRepository.cs:15, 119-127`, `SpaceTradersPortAdapter.cs:202` | 2.2 (done) |
| B20 | **A restart clears every ship's active goal.** Startup sync overwrites each existing ship row with `SetValues(new CachedShip { … })`, and that object doesn't carry the goal columns, so they become null.<br>• A scout ship whose assignment already matches the current route step doesn't get its goal back.<br>• Arrival wake-ups scheduled before the restart no longer match any goal (B17). | `StartupSyncService.cs:106-130` | 1.12 (done) |
| B21 | **The app tables may never be created** (confirmed in 1.13). `EnsureCreatedAsync` does nothing when the database already holds any table. Wolverine creates its `wolverine` tables when the host starts, before the deferred initializer runs, so the initializer's `ALTER TABLE` statements would then fail. The cluster's database will be empty on redeploy. | `SpaceTradersDatabaseInitializer.cs:12`, `Program.cs:75-78`, `DeferredStartupHostedService.cs:22-30` | 1.13 (done) |
| B22 | **The dashboard publishes the internal API key.** The WebUI container writes the key into `config.js`, which anyone who can open the dashboard can read. The old ingress served both the dashboard and the API on the public `gemberkoekje.nl`, so anyone could call `PUT /settings/*` and `POST /control/*`. | `SpaceTraders.WebUI/docker-entrypoint.sh:11-29`, `SpaceTraders.WebUI/index.html:17`; gembernodes `3f9f785^:ingress/spacetraders-ingress.yaml` | 4.2 (done: LAN only, not merged) |
| B23 | **A failed startup leaves an idle pod that looks healthy.** One try/catch wraps the startup chain. If database init, agent bootstrap, the run lifecycle, startup sync or recovery throws, the later services (the tick and pruning among them) never start, and nothing retries. `/health/live` runs no checks, and the old deployment used it for the startup and liveness probes, so Kubernetes never restarts the pod. | `DeferredStartupHostedService.cs:57-89`, `Program.cs:146` | 1.11 (done) |
| B24 | **WebUI loose ends** (minor).<br>• SignalR refresh hints probably never match a query: the client reads a string `kind`, but the server sends an object.<br>• The end-to-end test opens `/orchestration`, but the route is `/plans`.<br>• The unrouted pages in `src/Future` call endpoints that don't exist. | `signalr.tsx:27-28`, `DashboardNotifier.cs:19,28`, `orchestration.e2e.ts:5` | with D5 |
| B25 | **The starting probe is probably not recognised as a probe.** Startup sync stores a ship's registration role as its type (`SATELLITE` for the starting probe), but the probe plan only accepts type `SHIP_PROBE` or a symbol containing `PROBE` or `SATELLITE`, and ship symbols look like `AGENT-2`. The plan then buys a probe instead of using the free one. | `StartupSyncService.cs:64`, `ProbeDeploymentPlanService.cs:495-498` | 6.3 |
| B26 | **A newly registered agent had no settings until the pod restarted** (found and confirmed in 1.4). Registration wrote the new agent's rows and default settings through a DbContext that was created before the new agent was set: resolving the API client creates it, for the endpoint-usage counter. So all of it was stored under the previous agent. With every setting missing, `Automation.Enabled` read as off, and after a server reset the bot sat idle until its next restart. | `AgentBootstrapService.cs` (`RegisterNewAgentAsync`), `ApiEndpointUsageRecorder.cs` | 1.4 (done) |
| B27 | **A contract plan waiting for budget calls the API on every tick** (found in 1.9, seen at runtime in 1.14: 12 calls a minute). It is retried every 5 s, and each retry fetches every contract (`GET my/contracts`) and saves a new plan: 12 calls a minute while it waits, and the waiting can last as long as the credits stay short. Its log lines went to Debug in 1.9; the calls are still there. | `ContractPlanService.cs` (`EnsureBootstrappedAsync`, `RefreshContractsCacheOnceAsync`) | 1.14 (done) |
| B28 | **Startup sync caches a shipyard without its prices** (found in 1.14). It stored the priced ships where the ship types belong and left the prices empty, and purchases read the price from the latter. A purchase at a shipyard where a ship sat at startup then failed with "price unknown" until a ship arrived there again, and every restart did the same to each shipyard with a ship parked at it; a parked probe never leaves. In the soak test the contract plan waited 9 minutes for a drone it could afford, until the scout docked at that shipyard. | `StartupSyncService.cs` (`EnsureFacilitiesForShipsAreCachedAsync`) vs `SpaceTradersPortAdapter.GetShipyardAsync`; `ShipyardRepository.MapToDto`, `ShipPurchaseService.ResolveShipPurchasePrice` | 1.14 (done) |
| B29 | **Wolverine logs every handled message at Information** (found in 1.14). It logs "Successfully processed message …" under the message type's name, so the `"Wolverine": "Warning"` override doesn't reach it: one line per message. | `DependencyInjection.cs` (`AddWolverine`); Wolverine's `MessageSuccessLogLevel` defaults to Information | 1.14 (done) |
| B30 | **A contract delivery sends every unit aboard** (found in 1.14). A trip's last extraction can bring more aboard than the contract still needs; the surplus earns nothing, and whether the API refuses such a delivery outright is unconfirmed (its docs don't say). If it does, the final delivery fails on every tick. | `FulfillContractDeliveryCommand.cs` | 1.14 (done) |
| B31 | **A restart blanks the contract's terms** (found in 1.14). Startup sync stored contracts without their deadline and deliverables, so after every restart the contract plan couldn't read its deliverable until the next delivery response wrote it back: it couldn't restore a lost assignment, and a ship that had delivered everything couldn't fulfil. | `StartupSyncService.cs` (contracts) | 1.14 (done) |
| B32 | **The ship table grows without end** (found in 1.14). `cached_ships` holds a few wide rows (about 2 kB of ship JSON each) that change every minute or so. Postgres prunes their old versions in place, which kept the table's dead-tuple count under the autovacuum trigger (50), so VACUUM never ran and every update that didn't fit its page extended the table: about 250 kB an hour with one busy ship, and in phase 6 that scales with the fleet. | soak samples: 32 pages for 3 rows, 0 autovacuums in 2 hours | 1.14 (done) |
| B33 | **Contract payments never reach the cached credits** (found in 1.14). The accept and fulfil responses carry the agent's new credits, but only refuels, sales and purchases wrote them to the cached agent. Purchases are budgeted from the cache, so after a contract the bot thinks it has less than it does until the next restart: in the soak test 6,620 less (130,564 cached, 137,184 in the game). | `FulfillContractDeliveryCommand.cs`, `ContractPlanService.cs` (accept) | 1.14 (done) |
| B34 | **Waypoint traits are never stored** (found in 1.14). Startup sync stores only a waypoint's market and shipyard flags, and nothing calls `WaypointRepository.UpsertRangeAsync`, the one method that writes traits (and modifiers, orbitals and charts). What reads them finds nothing:<br>• the mining plan means to pick the nearest asteroid whose deposits can yield the mineral, but always falls back to the nearest asteroid of any kind, so a drone can be sent where its mineral never comes up;<br>• the contract plan's trait score is always 0. It only breaks ties between equally near asteroids (nearest first is by design); whether a farther asteroid with the right deposits should win is a strategy question;<br>• the dashboard shows no traits. | `StartupSyncService.cs` (`EnsureSystemsForShipsAreCachedAsync`), `MiningAutomationService.cs` (`MatchesTradeSymbolAvailability`), `ContractPlanService.cs` (`ScoreTradeSymbolMatch`), `FleetStatusMapper.cs` | 6.4 |
| B35 | **The startup snapshot repeats startup sync's API calls** (found in 1.14). Right after startup sync it fetches the agent, the ships, the system, every page of its waypoints, and the markets and shipyards where ships are, all of which sync has just fetched or found cached: about 11 calls on every start. | `StartupSnapshotService.cs` vs `StartupSyncService.cs` | 0.5 (done) |
| B36 | **The Docker integration tests skip silently on Windows** (found in 1.14). Four test classes decide whether Docker runs by looking for `/var/run/docker.sock` or `DOCKER_HOST`. Docker Desktop on Windows has neither, so the tests report "skipped" while Docker is running. Until it's fixed, the README says to set `DOCKER_HOST=npipe://./pipe/docker_engine`. | `MessageStorageIntegrationTests.cs`, `AgentCleanupIntegrationTests.cs`, `DatabaseInitializerTests.cs`, `IntegrationTestBase.cs` | 0.5 (done) |
| B37 | **The credit-drop alert can't fire** (found in 2.2). `AlertHandler` compares each credit change with the credits it remembers in a field from the previous one, but Wolverine creates the handler anew for every message, so the field is always empty. Until 2.2 nothing published the event anyway. The event carries the old credits, so the fix is small, but it would then warn, and post to `Alerts.WebhookUrl`, on every purchase that costs more than 10% of the credits, a ship included. | `AlertHandler.cs` (`_previousCredits`) | D12: removed |
| B38 | **Startup recovery reports docked ships as in transit** (found in a local run during phase 2, through `spacetraders_messages_handled_total`). Its first branch takes any ship whose cached arrival time has passed for a ship that has just arrived, but that time stays on a ship after it docks: startup sync stores the last route's arrival. So every docked ship that ever travelled gets a `ShipInTransitEvent` (an "in transit" activity row) on every start: three in a run with an idle fleet. `docs/HOW_IT_WORKS.md` describes the branch as "still marked in transit", which the code doesn't check; a ship that really arrived while the bot was down comes back from `GetAllAsync` already in orbit, so the branch never sees one. | `StartupRecoveryService.cs` (`RecoverShipAsync`), `StartupSyncService.cs` (`ArrivesAt`) | 6.4 |
| B39 | **`/health/startup` answers 200 while the startup chain runs** (found in 4.2). The check reports "still running" as Degraded, and ASP.NET Core answers Degraded with 200 unless told otherwise. The startup probe that 4.2 adds for B23 would pass as soon as the HTTP server is up, like the old probe on `/health/live`. | `Program.cs` (`MapHealthChecks("/health/startup")`), `StartupInitializationHealthCheck.cs` | 4.2 (done) |
| B40 | **The dashboard's probes fill the bot's log budget** (found in 4.2). nginx logs every request, and the WebUI's liveness and readiness probes call `/healthz` 8 times a minute: about 11,500 lines a day in `{namespace="spacetraders"}`, which the dashboard's log-volume panel and the 50,000-a-day alert (2.4, 2.5) count as the bot's. An idle bot logs nothing, so the panel would have shown only the probes. | `SpaceTraders.WebUI/nginx.conf` (`location = /healthz`) | 4.2 (done) |
| B41 | **Hosts in one process share a logger** (found when CI failed on main at `41a7c22`, after #114 had passed). By default the Serilog hosting package sends a host's lines to the process-wide `Log.Logger`, which every host replaces when it starts and closes when it stops. Production runs one host per process; the tests run several side by side, so a host's warnings could reach another host's error log, or none. `HealthRuleScenarioTests`' RepeatingError scenario failed that way, which kept CI from building the images. | `Program.cs` (`UseSerilog`), Serilog.Extensions.Hosting (`preserveStaticLogger`) | 4.2 (done) |
| B42 | **A handler's first message raises `RepeatingError`** (found in 4.3, on the cluster). Wolverine compiles a handler when its first message comes, and under 5.x's `AllowedButWarn` (`RestoreV5Defaults()`) it logged a warning for each dependency it resolves from the container ("Utilizing service location for …"). They all share one template: 11 in the bot's first minute, over the rule's limit of 5, so the anomaly was raised on every start, and could be again whenever a handler first ran later. Their text ("…this is an error") also tripped the error-log alert (B44). | `DependencyInjection.cs` (`RestoreV5Defaults()`), `RepeatingErrorRule.cs` | 4.3 (done) |
| B43 | **A counter's first value never reaches the dashboard** (found in 4.3). Prometheus's `increase()` and `rate()` count what a series gains between two scrapes, never the value it has when first scraped, and prometheus-net creates a labelled series on its first increment. On the cluster the contract's deposit (4,267) and the first drone (46,885) were booked about 25 seconds before Prometheus first scraped the pod, so the ledger panels showed neither; a single 429, failed call or breaker trip could never show at all. | `PrometheusAutomationMetrics.cs`; the dashboard's ledger, 429 and breaker panels | 4.3 (done) |
| B44 | **The error-log alert fires on the bot's ordinary lines** (found in 4.3). Gembernodes' "Error logs detected" rule matches `(?i)error` anywhere in a line, and 2.5 added `spacetraders` to it. The bot's JSON lines contain the word without being errors: the startup settings dump (`Health.Errors.MaxRepeatsIn10Minutes`), Wolverine's "…this is an error" (B42) and a `RepeatingError` anomaly's own lines. It fired five minutes after the first start. | gembernodes `infrastructure/monitoring/grafana-alerting-provisioning.yaml` (`loki-error-logs`) | 4.3 (done: gembernodes PR #13) |
| B45 | **The scout plan can skip a stop** (found in 5.1, on the cluster). When the ship docks at a stop, the tick and the arrival can both run its goal step. On 2026-10-02 at 09:18:58 the arrival's step moved the plan from stop 25 to stop 26, X1-DC53-J58. In the same second the tick's resume check read the plan from before that advance and the assignment from after it, took the assignment for missing and set the ship's goal back to stop 25; and the visit to stop 25 completed a second time, in the tick's goal step, which moved the plan past stop 26. The plan logged "all 26 waypoints visited", but J58's market was never fetched, and markets aren't scouted again. Any stop can be skipped this way, whenever a tick coincides with an arrival. | `ScoutAllMarketplacesPlanService.cs` (`ResumeIfAssignmentMissingAsync`, `AdvanceAsync`), `ShipGoalExecutorService.cs`; Loki, 09:18:58Z (`Tick` 326) | 5.1 (done) |
| B46 | **Two goal steps can run for one ship at once** (found in 6.5, from the code; B45 was the scout plan's case). The tick steps every ship every 5 s, and an arrival steps the ship it docks, on a thread of its own. Both read the ship before either acts, so a trade step would buy twice, or try to sell cargo that is already sold. | `GameLoopService.cs` (goal steps), `ShipNavigationCompletedHandler.cs`, `ShipGoalExecutorService.cs` | 6.5 (done) |
| B47 | **The navigation's fuel fallback leaves a ship in DRIFT** (found in 6.5, from the code). When a flight needs more fuel than the ship has, `NavigateSubCommand` switches it to DRIFT, which burns 1 fuel whatever the distance, and flies there. Nothing switches it back, so every later flight of that ship is DRIFT, about ten times slower than CRUISE. Trade trips plan refuelling stops and never need the fallback (6.5); scouting, contract and probe flights still can. | `INavigateSubCommand.cs` (`TrySwitchToDriftForFuelEfficiencyAsync`) | open |

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
| D12 | The credit-drop alert (B37) would fire on every purchase above 10% of the credits. Fix it as it is meant, change what it watches, or remove it? | **Remove it** (2026-10-01): credits only drop when the bot spends them, so it could only report the bot's own spending. Removed (B37). |
| D13 | Two of 3.2's rules clash with D9 and D1 in the first run: with only scout and contract on, the starting probe, the command ship after scouting and the drone after its contract are idle by design, and once the contract has paid, the credits stop changing. Taken literally, "idle for at most N minutes" and "credits change at least once in 24 hours" would stay active for the rest of every reset, and phase 6 needs a clean reset period. | **Only when work waits** (2026-10-01): a ship counts as idle only while a plan that is on has work it could give that ship, and the credits must change daily only while ships have work. |
| D14 | Slice 6.5: when is a trade trip lucrative, worth starting and worth carrying on when prices change? | **`Trade.MinProfitPerUnit` per unit, after fuel** (2026-10-02): the existing setting (200), now read by the trader. 0 means any profit, but see D15. |
| D15 | Slice 6.5: how do goods in the market tree, which let a market make pricier goods, come first? | **Tree routes first** (2026-10-02): among lucrative routes, one whose sell market makes a pricier good from the cargo beats any that doesn't, then the most profitable. Hence D14's bar matters: at 0, a trip earning 47 credits that feeds JEWELRY would beat one earning 7,000 that feeds nothing (the live prices of 2026-10-02). |
| D16 | Slice 6.5: does the trading plan buy ships? | **Not for now** (2026-10-02): it trades with the ships it has, first the command ship after scouting, then the drone after its contract. Buying haulers needs a budget of its own; keep it in mind for later. |
| D17 | Slice 6.5: may cargo use `FleetExpansion.MinCreditReserve`? | **Yes** (2026-10-02): cargo turns back into credits when it is sold. Credits for the trip's fuel are kept back. |
| D18 | Slice 6.5: may two traders share a route? | **No, for now** (2026-10-02, "to keep everything simple"): a route, the good with its buy and sell market, that one trader holds isn't offered to another. |
| D19 | How do reads (GET: a market refresh) and writes (anything else: moving a ship, trading) share the API's rate limit? | **Writes first** (2026-10-02): "I'd rather have a POST to move a ship or trade goods than a market refresh that can be done 10 seconds later without penalty." A read gives way while a write waits for the budget, leaves the last 10 of the 30-request burst to writes, and stops giving way after 10 seconds, so reads can't starve. The market watch runs last in the tick, one market a tick. |

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

**0.5 Tidy-ups found by the soak test** (done)
- Do:
  - B35: build the startup snapshot from what startup sync has just cached, instead of fetching it
    again;
  - B36: let the four Docker checks find Docker on Windows too (Docker Desktop's named pipe, or
    leave it to Testcontainers), so the integration tests run there instead of skipping;
  - delete `SpaceTraders/packages.lock.json`: no project uses it (it came in with a "wip" commit);
  - remove the UTF-8 BOM from the `.csproj` files of `SpaceTraders.API.Tests`,
    `SpaceTraders.Application.Tests` and `SpaceTraders.Integration.Test`, which the project-file
    analyzer flags (Proj3000) since the NuGet update.
- Done when: the snapshot makes no API calls of its own; `dotnet test --filter Category=Integration`
  runs the Docker tests on Windows without `DOCKER_HOST`; and the build has no Proj3000 warning.
- Done:
  - B35: the snapshot reads what startup sync has just cached: the agent, the ships with their
    goals, the contracts, every waypoint in the ships' systems, and the market and shipyard where
    each ship is. `StartupSnapshotServiceTests` runs it against a substitute API client, which
    receives no call (about 11 on every start before). The cache holds less than the API returns
    (no crew, no mount names or descriptions, and no waypoint traits until B34); it does hold the
    goals and the contracts, which the old snapshot lacked.
  - The snapshot no longer switches `Automation.Enabled` off and back on around itself. That kept
    the fleet still during its API calls; without them it has nothing to wait for, and switching
    automation back on could undo a switch-off made in the meantime.
  - B36: the four checks ask Testcontainers whether it found Docker
    (`TestcontainersSettings.OS.DockerEndpointAuthConfig`), the same discovery it starts the
    container with: `DOCKER_HOST`, the Unix socket, or Docker Desktop's named pipe. On Windows,
    without `DOCKER_HOST`, all 74 integration tests now run; 6 used to skip (`IntegrationTestBase`
    already tried the named pipe itself, the other three classes didn't).
  - The lock file belonged to `.net.csproj`, the project-file analyzer's SDK project, which came in
    with the same "wip" commit. No solution includes it, so nothing restores it.
  - Noticed: where `npm ci` has run in `SpaceTraders.WebUI`, the build still reports 6 Proj3000
    warnings, for BOMs in files under `node_modules`. The analyzer walks the project folder itself
    and skips only `bin`, `obj`, `.vs`, `.git` and `.nuget`, so an MSBuild exclude doesn't reach it.
    CI's .NET job has no `node_modules`, so it sees none.

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

**1.3 Stop storing in-process messages (B2)** (done)
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
- Done:
  - Chosen: no Postgres message storage at all. `UseDurableLocalQueues()`,
    `PersistMessagesWithPostgresql`, the unused EF Core transaction middleware (no handler used
    it, so messages were never stored in the same transaction as the data) and
    `UseResourceSetupOnStartup` are gone, and so are the two Wolverine packages they needed.
  - Why not the durability agent: it would clean up, but every published message would still be
    written to Postgres and deleted again, and nothing here needs it. A crash loses the messages
    in flight; startup sync, startup recovery and `scheduled_ship_events` already cover that.
  - `MessageStorageIntegrationTests` starts the host as on the cluster against an empty Postgres:
    it created 8 `wolverine` tables before, none now. The soak test (1.14) showed it in a real
    run: no `wolverine` schema or table in four hours.
  - `OutboxReplayIntegrationTests` is gone: it tested Wolverine's durable scheduling, which the
    app doesn't use.

**1.4 Short agent identity, old agents cleaned up (B4, part of B3)** (done)
- Do: key rows on a short agent id (agent symbol plus reset date, or a small surrogate key)
  instead of the JWT; the token itself is stored only in the credentials table. When a new agent
  is registered after a reset, delete the previous agents' rows and keep only their run summary
  (`runs`).
- Done when: no table other than credentials stores the token, and the cleanup has a test.
- Done:
  - Rows are keyed on `AgentId`: the agent's symbol and the server's reset date, such as
    `GEMBER@2026-09-27`. Bootstrap reads the reset date from `GET /` before it tries any token, so
    a token the server accepts was registered in that reset. A token keeps the id it was first
    stored under. The token sits only in `stored_credentials`; before, a test found it in four
    more tables after one registration.
  - At every start, once it has picked the agent, bootstrap deletes every other agent's rows,
    table by table, except `runs`. At every start rather than only after a registration, so a
    table that fails (a timeout on a big table, say) is retried at the next start, and an agent
    switched to through configuration is covered too. Against Postgres, the old agent's rows
    stayed in seven tables before.
  - Registration refuses when an agent with the new id is stored already. That only happens when
    the server resets between reading its reset date and registering; the restart reads it again.
  - Found on the way, and fixed: B26, a newly registered agent had no settings until the pod
    restarted.
  - The schema initializer only creates the model's tables now. Its upgrades for old schemas
    would have added a token column back to every table, and the database starts empty anyway. A
    database from before this slice has to be dropped. The unused `scout_plan_states` table is
    gone.

**1.5 Retention for every table that grows (B3)** (done)
- Do:
  - give each table a policy: by age, by row count, or explicitly bounded;
  - add `startup_snapshots` (keep the last N) and `runs`;
  - start pruning independently of the startup chain;
  - run each table's prune separately, so a failure logs and moves on;
  - rewrite the `NOT IN` downsampling delete so it fits the 30 s command timeout.
- Done when: a test fails for any `DbSet` without a retention policy or an explicit "bounded"
  mark, so a new table can't slip through.
- Done:
  - `DataRetention` lists every table: 10 with a policy that prunes them, 18 bounded, each with
    the reason. `DataRetentionTests` fails for a table that isn't listed; before, 23 tables had
    no policy, `startup_snapshots` and `runs` among them.
  - New policies: `startup_snapshots` keeps the agent's first snapshot and the last 10; `runs`
    and `run_credit_highlights` keep 365 days (runs of every agent, since 1.4 keeps those);
    `ship_goal_history` and completed `fleet_goals` keep 30 days. Claude picked these numbers;
    they're yours to change. The existing ones (7 and 90 days for samples, 30 for the ledger and
    ship tasks, `ActivityLog.RetentionDays` for the activity log) are as they were.
  - `DataRetentionService` prunes each table in its own scope and try/catch, and has taken over
    from `ActivityLogPruningService`. It starts right after database initialisation, before any
    step that can fail, and prunes at once, then daily. Pruning covers every agent's rows, so it
    doesn't need agent bootstrap; the activity log takes the longest `ActivityLog.RetentionDays`
    any stored agent has.
  - The downsampling deletes rank the rows once (`row_number()`). On 2.6 million samples (30
    markets, 20 goods, 90 days), the old `NOT IN` was cancelled after 3 minutes, still running:
    Postgres compared every row with a list too big to hash in `work_mem`. The new one took 5 s.

**1.6 Database size guard** (done)
- Goal: the bot can't fill the shared Postgres volume or the NAS share.
- Do: every few minutes, read `pg_database_size(current_database())` and export it as
  `spacetraders_db_size_bytes`. Above the soft limit, raise an anomaly; above the hard limit,
  pause automation (this needs 1.7). The limits are settings, starting at 1 GB and 3 GB (D8).
- Done when: tests with a fake size source cover both limits.
- Done:
  - `DatabaseSizeGuardService` reads the size every 5 minutes and exports it. The limits are
    `Database.SoftLimitMegabytes` (1024) and `Database.HardLimitMegabytes` (3072).
  - Until phase 3, the "anomaly" for the soft limit is a `DbSizeSoftLimit` warning, logged once
    per crossing. Above the hard limit it switches `Automation.Enabled` off and logs
    `DbSizeHardLimit`; while the database stays above it, every check switches automation off
    again.
  - Its first check runs before the rest of startup goes on, so a database already above the
    hard limit has automation off before the tick starts (and before the startup snapshot reads
    the switch, which would otherwise switch it back on).

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

**1.9 Log diet (B12)** (done)
- Do:
  - move per-tick messages to Debug, or log them only when something changes;
  - set `System.Net.Http` to Warning in the Serilog section (done in `appsettings*.json`; the old
    ConfigMap's `Logging__LogLevel__*` variables probably never reached Serilog);
  - use `RenderedCompactJsonFormatter` in Production;
  - use one property name per concept (`ShipSymbol`, `ContractId`, `WaypointSymbol`);
  - push tick and plan context through `LogContext`.
- Done when: an idle bot writes less than one line a minute, and a normal day stays within a
  budget of 50k lines. Loki's 31 days on 10Gi are shared with every app.
- Done:
  - What a tick finds when nothing changed goes to Debug: a plan already complete or still
    waiting, no idle ship, no budget, a purchase denied, the mining drone cap. Every action stays
    at Information (docked, sold, bought, assigned), and so does the contract plan starting to
    wait for budget, once. Ship commands log their result; the "starting" line goes to Debug.
  - Two idle states the first run can meet now write nothing at Information: the command ship
    after scouting (two lines every tick, 24 a minute, from B10) and a contract plan waiting for
    budget (four every tick, 48 a minute). Tests replay a minute of ticks of each.
  - `RenderedCompactJsonFormatter` in Production. The ship a line is about is always
    `ShipSymbol` (no more `Symbol` or `Ship`), a waypoint `WaypointSymbol` unless the message
    names its role (`Destination`, `SellWaypoint`), a contract `ContractId`, a goal kind
    `GoalKind`. Every line logged during a tick carries `Tick`, and its step's `Plan`,
    `ShipSymbol` or `ContractId`, through `ILogger.BeginScope`, which Serilog turns into
    properties (checked against Serilog itself).
  - The soak test (1.14) measured the budget: about 2,000 lines a day while a drone mines, and
    none while the fleet is idle. Probes, mining and trading are still to measure, once phase 6
    switches them on. A ship that can't find a route logs
    a warning on every tick; phase 3's health rules are the place for that.
  - Found on the way: B27 (the waiting contract plan's API calls).

**1.10 Follow the API guide for limits and errors (B13)** (done, apart from the check after the redeploy)
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
- Done:
  - `RequestBudget` (a singleton, so the buckets no longer reset) counts in sliding windows: 2
    requests in any second, then up to 30 more in any 60 seconds. A sliding window is at least
    as strict as any fixed window the server might count in.
  - 429: with the headers it waits until the reset (at most a minute), without them it backs off
    1, 2, 4, 8 and 16 s. It gives up after five retries. Every 429 is logged at Warning and
    counted in `spacetraders_api_throttled_total`, which no longer counts local waits.
  - 502: `OutagePauseHandler` pauses all calls for `Api.BadGatewayPauseMinutes` (default 3).
    Calls during the pause fail at once (`ApiPausedException`); the tick skips its work and
    logs `ApiUnavailable`, then `ApiAvailable` after the first successful call.
  - Still to check after the redeploy (phase 4): the count of 429s stays at zero. It did in the
    soak test (1.14): no 429 in four hours.
  - Noticed: an arrival that falls in a 502 pause can't dock, and its message is dropped after
    Wolverine's three retries. The ship then looks in transit until the next restart; that is
    B17's territory (6.4).

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
  - The startup probe came with the manifests (4.2), and needed B39 fixed.

**1.12 Restarts keep ship goals (B20)** (done)
- Do: startup sync updates a ship's game state without touching its goal columns.
- Done when: a test syncs a ship that has an active goal, and the goal is still there afterwards.
- Done: sync sets the game-state columns one by one instead of replacing the whole row. It now
  also sets `LocalStatus` from the nav status, which the old code reset to `None`.

**1.13 An empty database gets every table (B21)** (done)
- Do: check against an empty Postgres. If B21 is confirmed, create the app tables explicitly, or
  initialise them before Wolverine sets up its storage.
- Done when: starting against an empty database creates every app table, and an integration test
  covers it.
- Done:
  - Confirmed: with any other table in the database, `EnsureCreated` skipped the app's tables and
    the initializer failed on `relation "stored_credentials" does not exist`. Wolverine's tables
    did exactly that until 1.3.
  - The initializer now creates the database and the model's tables itself when none of them
    exist, whatever else the database holds.
  - `DatabaseInitializerTests` (Postgres): an empty database, a database with an unrelated table,
    and a second run on its own schema all end with every model table.

**1.14 Soak test** (done)
- Do: start from an empty local database (Postgres in Docker), and run against the live API for a
  few hours with all of phase 1 in, while the cluster bot is off. Every 15 minutes, record table
  sizes and message counts.
- Done when: tables grow only with real game activity (ledger, activity log), nothing in
  `wolverine` grows, and the breaker never trips.
- Done:
  - Ran on 2026-10-01 from 07:09 to 11:10 UTC against the live API, from an empty Postgres 16 in
    Docker, with no SpaceTraders deployment on the cluster. Configured like the cluster:
    Production, JSON logs, only the scout and contract plans on (D9). The bot registered
    `SPECTER-DEBUG2`, scouted all 26 markets in 28 minutes, bought a mining drone and fulfilled
    its IRON_ORE contract (42 units) at 10:45; after that the fleet was idle. `tools/soak/`
    recorded every table, `/metrics` and the log every 15 minutes: 17 samples.
  - The criteria hold. The database went from 9.25 MB to 9.76 MB and from 202 to 324 rows, all
    game activity: the activity log (one row per navigation), the fuel ledger, the scouted
    markets, and a startup snapshot per start. Every other table kept its size once B32 was
    fixed. No `wolverine` schema or table appeared. The breaker never tripped: a busy ship takes
    12 goal steps a minute.
  - Also measured: no 429s, no errors, no exceptions, no token in the log. 625 API calls, 106 of
    them B27's. Log lines: about 2,000 a day while a drone mines, and none at all while the fleet
    is idle (0 lines and 0 API calls from 10:46 to 11:10), so 1.9's budget holds. Memory stayed at
    220–330 MB. Messages handled, before B29 took their lines away: 168 in the first 70 minutes,
    every one from a ship moving.
  - The bot was restarted four times on fixed builds, killed like a pod each time: the stored
    token was reused, the run resumed, ship goals were kept, and the leader lease was taken over
    after its 30 s.
  - Found and fixed on the way, each with a test that failed first: B28–B33, and B8, B9, B10 and
    B27, pulled forward from phase 6 because the run showed them. B8, B9, B10, B28, B29, B31 and
    B32 were also seen working in the run.
  - Noticed, not fixed, and now in the plan: waypoint traits are never stored (B34, which matters
    most for mining, 6.4); the startup snapshot repeats startup sync's API calls (B35); the Docker
    integration tests skip on Windows unless `DOCKER_HOST` is set (B36); and a stray lock file and
    three `.csproj` files with a BOM. Slice 0.5 collects the small ones.

**Phase 1 in short** (done 2026-10-01)
- The bot can no longer fill the shared Postgres or Loki: messages stay in memory (1.3), every
  table has a retention policy (1.5), a size guard watches the database (1.6), the logs are on a
  diet (1.9, B29), and the ship table no longer bloats (B32). It doesn't loop a ship (1.1, 1.2),
  its kill switches work (1.7), it follows the API's rate rules (1.10), a failing step doesn't
  stop the tick (1.11), and restarts and server resets keep its state (1.8, 1.12, 1.13, B31).
- Traps it took: startup sync rebuilt rows from one API call and dropped what the other paths
  store, three times (B20, B28, B31); Wolverine logs under the message type's name (B29); a small,
  wide, often-updated table outgrows the autovacuum trigger's reach (B32); and Wolverine 6 needs
  runtime compilation and service location for the DbContext (see `CHANGELOG.md`).
- To understand this phase, start with `docs/HOW_IT_WORKS.md` (sections 1, 2 and 6), then
  `GameLoopService.cs`, `ShipGoalExecutorService.cs`, `DeferredStartupHostedService.cs` and
  `DataRetention.cs`.

### Phase 2: Visibility

Prometheus holds the numbers, Loki holds the events, and Grafana shows both. There is no
Grafana-to-Postgres datasource and no new journal table: Loki already keeps 31 days and handles
its own retention, so the bot's database stays small.

**2.1 Metrics Prometheus can scrape (B11)** (done)
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
- Done:
  - `/metrics` is served by a second, minimal Kestrel server on `Metrics:Port` (9090), which
    needs no API key; the main port serves no `/metrics` any more. `MetricsEndpointTests` scrapes
    it without a key and finds every metric below (it fails with the metrics server switched off);
    the deployment's Prometheus annotations point at 9090 (4.2).
  - `PrometheusAutomationMetrics` defines every metric when the host starts, so a scrape lists
    them all, also before they have a value. The names are in `docs/HOW_IT_WORKS.md` section 11:
    - credits (`spacetraders_agent_credits`): read from the cached agent every 10 s, so it shows
      the credits from the first sample on. It was fed by an event nothing publishes (B7);
    - credits earned by source and spent by category: counted with each ledger row, by ledger
      category. Until 2.2 only fuel purchases reach the ledger;
    - ships by role and state, and one series per ship with its role, state, goal and blocked
      reason as labels and, as value, when it entered that combination: Grafana's "time in
      state" is `time()` minus it;
    - contract units required and fulfilled, and the deadline, per accepted contract;
    - API requests by method, route template (`my/ships/{shipSymbol}/navigate`) and status,
      counted by a new innermost handler, so every attempt counts, retries included;
    - 429s by source (`rate_limiter` or `infrastructure`), in `spacetraders_api_throttled_total`,
      which since 1.10 counts real 429s only; and the time requests waited for the local budget;
    - handled messages by type (Wolverine middleware), goal steps by kind, breaker trips,
      active anomalies, the database size, and when the server resets next (from `GET /` at
      bootstrap).
  - "Active anomalies" has two rules until phase 3: the size guard's soft and hard limits
    (`spacetraders_anomaly_active{rule="DbSizeSoftLimit"|"DbSizeHardLimit"}`), which 1.6 already
    called anomalies.
  - Ship roles are the cached ship types: the registration role after a start, the shipyard type
    for a ship bought since (B25), so one kind of ship can show under two roles until the next
    restart.
  - `tools/soak/` reads the metrics from the new port and the new request counter.

**2.2 Record what happens (B7, B19)** (done)
- Do:
  - dispatch aggregate domain events, or publish where the change happens, so that credit
    changes, sales, purchases, contract payments and ship purchases reach the ledger and the
    metrics;
  - read the trade-goods JSON case-insensitively in `MarketPriceSampleRepository`, like
    `MarketRepository` does, so price history gets recorded.
- Done when: tests show each of those four producing a ledger row and moving its counter, and a
  market refresh writing price samples.
- Done:
  - Published where the change happens: production code doesn't use the aggregates, so there
    was nothing to dispatch from. The mining and trade executors publish `ShipCargoSoldEvent`
    and `CargoPurchasedEvent`, `ShipPurchaseService` `NewShipPurchasedEvent`, the contract plan
    `ContractAcceptedEvent` and the delivery command `ContractFulfilledEvent`. The contract
    payments come from the contract's terms in the accept and fulfil responses (the port didn't
    map them), and the acceptance payment is a ledger row too (`ContractDeposit`, which nothing
    used).
  - Every credit change also publishes `AgentCreditsChangedEvent` (`AgentCreditsUpdates`, one
    helper for all the places that stored credits), so the credit samples fill and the probe
    plan stops waiting for credits that already came (B7's third bullet).
  - `LedgerEntryHandler` writes the row and moves `spacetraders_credits_earned_total` or
    `_spent_total` by ledger category. `LedgerWiringTests` sends each of the four through the
    host's own Wolverine and finds the row and the counter; the executor and service tests show
    who publishes, and failed before.
  - B19: `MarketPriceSampleRepository` reads the trade goods case-insensitively, with `long`
    prices like `MarketRepository`. Its test stores a market's goods the way an arrival does and
    finds a sample per good (none before).
  - Found on the way: the probe plan's credits handler would have run the plan, and bought
    probes, while it or automation was off; nothing published its event until now. It checks
    both switches now (1.7, D9). Below 200,000 credits every change would also have woken the
    plan only to send it back to waiting (two plan writes and a log line per sale or refuel); it
    now stays asleep until the credits are there. And B37 (the credit-drop alert can't fire), with
    D12.
  - `NewShipPurchasedEvent` takes the ship type as the `ShipType` enum, which has no
    `SHIP_MINING_DRONE`: the service maps the API's name (`ShipMiningDrone`), so the ledger row
    reads that.
  - Noticed:
    - `ShipCargoSoldEvent` doesn't say whether a sale was mined or traded, so both are
      `TradeSell`; `MiningSell` stays unused. Telling them apart is up to you.
    - Wolverine runs all handlers of an event as one chain and retries the whole chain when
      one fails, so a failing handler can probably write the others' rows twice (ledger, credit
      samples, activity log). Not seen; `MultipleHandlerBehavior.Separated` would isolate them.

**2.3 Journal events** (done)
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
- Done:
  - `JournalEvents` names every kind, and `docs/HOW_IT_WORKS.md` section 11 lists who logs each
    and with which properties. Every journal line starts with its kind (`CargoSold: ship …`), so
    the rendered message reads as a timeline too.
  - Where a line already said what happened, that line became the journal entry, so the log
    doesn't grow: the contract delivery and fulfilment, the purchases and sales, the plans
    starting, completing and waiting, the circuit breaker (`ShipBlocked`), the reset and the API
    pauses. New lines: `ContractAccepted`, `SettingChanged` and `ShipIdle`.
  - `SettingChanged` comes from `SettingsRepository`, so it covers every change, whoever makes
    it (the endpoints, the control switches, the size guard, the reset monitor), and only real
    changes. A key that may hold a secret (`Alerts.WebhookUrl`) shows `(hidden)`.
  - `ShipIdle` comes from the 10 s metrics sample (`ShipStateJournal`): once for each idle ship
    after a start, and once when a ship's goal or assignment ends, so it covers every way a ship
    turns idle without a line in each.
  - The size guard's limits log `AnomalyRaised` and `AnomalyCleared`, with `Rule` and `Subject`,
    in place of `DbSizeSoftLimit`, `DbSizeHardLimit` and `DbSizeNormal`.
  - Mining and trading have no plan to start or complete (they are opportunity queues), so they
    write none of the plan kinds.
  - Tests check the kind and properties of each new line and of the reworded ones (sales,
    purchases, contract payments, the contract plan, the breaker, settings, idle ships, the
    anomalies). The Loki query itself needs the bot deployed (phase 4); a local run shows the
    same lines in the JSON log.
  - `.claude/skills/st-investigate/SKILL.md`: the journal and metrics rows and queries now name
    what exists; the stale B2 and B12 queries are gone.

**2.4 Grafana dashboard** (done: gembernodes PR #10, merged 2026-10-01)
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
- Done, on gembernodes branch `claude/spacetraders-dashboard` (commit `3df654a`), not merged: it
  deploys through Flux once it is on `main`. Merged as PR Gemberkoekje/gembernodes#10 on
  2026-10-01, and applied by Flux (`2cd1e32`).
  - Every panel above, plus a row of numbers (credits, credits in the last hour, next reset,
    database size, active anomalies, whether Prometheus reaches the bot), goal steps and breaker
    trips, and log lines per hour against the 50,000-a-day budget. Series are aggregated with
    `max` or `sum`, so a pod restart doesn't split a line in two.
  - Checked without the cluster: every PromQL expression passed `promtool check rules`, and every
    LogQL query (journal, annotations, log volume) ran against a local Loki fed with the bot's own
    JSON log from a local run, and returned the journal lines and the annotation fields.
  - It can't be checked against real data until the bot runs on the cluster (phase 4).

**2.5 Grafana alerts** (done: gembernodes PR #10, merged 2026-10-01; the Grafana restart is pending)
- Rules:
  - the bot is down (no scrape for 10 minutes);
  - an anomaly has been active for more than 15 minutes;
  - the database is over the soft limit;
  - log volume is over budget;
  - add `spacetraders` to the existing error-log rule.
- Grafana only reads this file at startup, so it needs a rollout restart.
- Done, in the same gembernodes commit, not merged:
  - Group `spacetraders` (every minute): the bot is down (`max_over_time(up[10m])` under 1, or no
    series at all), an anomaly active for 15 minutes (the size limits excepted), and the database
    over its soft limit for 5 minutes (from `spacetraders_anomaly_active`, so it follows the
    setting). Group `spacetraders-logs` (every 10 minutes): more than 50,000 lines in 24 hours.
    `spacetraders` joined the error-log rule's namespaces.
  - "Bot is down" starts paused (`isPaused: true`): with the bot off the cluster it would fire at
    once. Unpause it in 4.3. The other rules can't fire without the bot's data.
  - After the merge: a Grafana rollout restart, so it reads the rules.

**2.6 Only settings that do something (B18, D10)** (done)
- Do: remove from the seed every setting that nothing reads at runtime:
  - the 21 keys that no code reads;
  - the `Navigation.*` and `Maintenance.*` keys, which only code that never runs reads.

  Then update the settings table in `docs/HOW_IT_WORKS.md`. Keep the `Runtime.*` status flags
  for now; moving them out of the settings is a separate cleanup. The database starts empty, so
  no old rows need removing.
- Done when: apart from the `Runtime.*` flags, every seeded setting is read by code that runs.
- Done:
  - 26 settings left the seed, after checking each key against the code again: the 18 non-runtime
    keys that nothing read (`Maintenance.LongRouteJumpThreshold` among them), and the four
    `Navigation.*` and four other `Maintenance.*` keys that only code that never runs reads. 30
    remain: the 14 that work, 4 that are read without changing what the bot does (the run's
    strategy label and the market views), and the 12 `Runtime.*` flags, three of which nothing
    reads.
  - `DefaultSettingsSeedTests` lists the non-runtime settings with who reads each; it failed with
    the 26 before. A new setting has to be added there, with its reader.
  - `NavigationPlanningService` and `FleetMaintenancePlanner` still read their settings; they
    don't run, and per D10 a feature that needs a setting adds it back.
  - `RunLifecycleService` still treats a change under `Navigation.`, `Maintenance.`,
    `Outfitting.` and so on as a strategy change that starts a new run. Those prefixes are now
    only reachable through `PUT /settings/{key}`, which accepts any key.

**2.7 Where each ship is, what it does, what it carries** (done; asked 2026-10-02, during 4.3)
- Asked: the fleet table showed each ship's role, state, goal and time in state. The contract's
  drone was in orbit at its asteroid and mining, but nothing showed that, nor how much it had mined.
- Done:
  - Every 10 seconds, per ship: where it is (the waypoint and its type; in transit, `→` and where
    it goes), what the bot has it do (its goal in a few words; else its contract work, `mining` at
    the contract's source, `delivering` at its destination and `on the way to …` between them; else
    `idle`, or `blocked (…)`), when it arrives while in transit, and its hold per good and its
    capacity: `spacetraders_ship_info`, `_arrival_timestamp_seconds`, `_cargo_units` and
    `_cargo_capacity_units`.
  - What the drones extract and jettison is counted per ship and good:
    `spacetraders_extracted_units_total`, `spacetraders_jettisoned_units_total`.
  - The dashboard's side is gembernodes PR #15 (merged): the fleet table's new columns, and panels
    for the holds and for what was mined. Gembernodes PR #16 deploys the bot with them.
  - Noticed: a goal other than scouting reads as its purpose, not its current step (a `MineAndSell`
    drone reads "mining and selling …" while it sells too); those goals only run from phase 6.

**2.8 A markets dashboard per system** (done; asked 2026-10-02, during 4.3)
- Asked: a second dashboard with a dropdown for the system to watch, that system's market and
  shipyard data, and the market tree: which goods are made from which.
- Done:
  - Every minute the bot exports the markets and shipyards it has cached
    (`PrometheusMarketMetricsService`): per market and good its prices, trade volume, supply (1 to
    5) and activity (0 to 3) as last seen; per shipyard its ship types and, once a ship has been
    there, their prices and supply; and when each was last refreshed. A market that no ship has
    visited yet shows only that it exists.
  - The market tree is the game's own: `GET market/supply-chain`, once per start (retried an hour
    after a failure, so a game API that is down can't raise `RepeatingError`), one series per good
    with what it is made from and what is made from it (`spacetraders_good_supply_chain`).
  - The dashboard itself is gembernodes PR #17 (merged; uid `spacetraders-markets`), and PR #18
    deploys the bot with these metrics (image `fd9e3d3`, since 10:29Z).
  - Noticed: the game lists `MACHINERY` as what raw goods (`ICE_WATER`, `AMMONIA_ICE` and others)
    are made from; the tree shows the game's map as it is.

**Phase 2 in short** (done 2026-10-01; the dashboard and alerts merged in gembernodes PR #10, the Grafana restart pending)
- Prometheus can scrape the bot (port 9090, no key), and every number the dashboard needs is a
  metric: credits, the ledger by category, each ship's state, contracts, the API by endpoint,
  messages, goal steps, anomalies, the database and the next reset (2.1). Sales, purchases and
  contract payments reach the ledger and the credit samples, and price history is recorded (2.2).
  One journal line per meaningful thing makes the log read as a timeline (2.3). Only settings
  that do something are seeded (2.6). Slice 0.5 cleaned up after the soak test.
- Verified with a local run against the live API (2026-10-01, the debug agent, an idle fleet):
  the scrape on 9090 without a key, no `/metrics` on the main port, API requests by route
  template, no API call from the startup snapshot (B35), and the journal's `ShipIdle` and
  `SettingChanged` lines in the JSON log. Then the dashboard's and alerts' queries, against
  `promtool` and a local Loki.
- Traps it took: the domain events had handlers but no publisher, so publishing them woke
  handlers that had never run (the probe plan's, which ignored the plan switches; the credit-drop
  alert, B37); Serilog quotes strings in the rendered message, hence `{EventKind:l}`; and the
  project-file analyzer walks folders itself, so an MSBuild exclude can't keep `node_modules` from
  it.
- Found and recorded: B37 (with D12, open) and B38. Noticed: mined and traded sales share
  `TradeSell`; Wolverine retries every handler of an event when one fails.
- To understand this phase, start with `docs/HOW_IT_WORKS.md` section 11 (metrics and the
  journal), then `PrometheusAutomationMetrics.cs`, `PrometheusMetricsService.cs`,
  `JournalEvents.cs` and `AgentCreditsUpdates.cs`; in gembernodes,
  `infrastructure/monitoring/dashboards/spacetraders-dashboard.json`.

### Phase 3: Health rules (the bot checks itself)

**3.1 Rule mechanism** (done)
- Do: evaluate the rules every minute against the bot's own state.
  - Each violation is an anomaly: rule, subject, since when, details.
  - It's exposed as `spacetraders_anomaly_active{rule,subject}` and as AnomalyRaised and
    AnomalyCleared events.
  - Thresholds are settings.
- Done when: there's a unit test per rule.
- Done:
  - `HealthMonitorService`, the last step of the startup chain, evaluates every `IHealthRule` at
    start and then every minute, each in the scope of one evaluation. A subject that breaks a rule
    is an anomaly: the journal logs `AnomalyRaised` at Warning, with `Rule`, `Subject` and `Details`
    (what the rule saw, its limit and the setting that holds it), and the metric turns 1. Once the
    rule holds again: `AnomalyCleared`, with `ActiveMinutes`, and 0. "Since when" is the raised
    line's time.
  - Anomalies live in memory: after a restart the first evaluation raises the ones still there
    again. A rule that throws is logged at Error and keeps its anomalies; the others carry on.
  - The rules count time no earlier than the monitor saw the bot able to work
    (`HealthCheckContext.WorkingSince`: automation and the plan on, API calls not paused after a
    502), so switching automation off and on, or a restart, doesn't look like a stall. Conditions
    without a timestamp of their own get one from the monitor's memory (`HeldSince`).
  - Thresholds are eight `Health.*` settings. Claude picked the defaults (3.2), from the soak test
    where it had data; they're yours to change. `Health.` isn't a strategy prefix, so a change
    doesn't start a new run.
  - The size guard's two limits stay in the guard, where 1.6 made them the first anomalies: its hard
    limit has to act before the tick starts.
  - `HealthMonitorServiceTests` covers raising, clearing, one anomaly per subject, a failing rule
    and the clocks. Every rule has its own tests (`tests/SpaceTraders.Application.Tests/Health`).

**3.2 First set of rules** (done). Each one states an intended behaviour:
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
- Done:
  - Ten rules, plus the size guard's: `ContractStalled`, `ContractLeftOpen`,
    `ContractDeadlineAtRisk`, `ShipStuck`, `ShipLeftIdle`, `CircuitBreakerTripped`,
    `RepeatingError`, `CreditsUnchanged`, `ApiUnauthorized`, `ApiThrottled`, and `DbSizeSoftLimit`.
    `docs/HOW_IT_WORKS.md` section 12 has what breaks each, and its setting.
    `HealthRuleScenarioTests` breaks one thing per rule in the real host and sees its anomaly
    raised, through the real log pipeline, API client handlers and circuit breaker where a rule
    reads them.
  - D13: the idle-ship and credits rules count only while work waits. A ship is "left idle" only
    while a plan that is on has work it could give it; the credits must change daily only while
    ships have work. Taken literally, both would have stayed active for the rest of every first-run
    reset.
  - "Changes state" (`ShipStuck`) means the bot updates the ship (its `LastSyncedAt`: nav, cargo,
    fuel, cooldown), not its nav state: in the soak test a contract drone stayed in orbit with the
    same assignment for up to 69 minutes while it extracted every 71 seconds, so the metrics' time
    in state (2.1) would flag a working miner. The clock starts at the arrival at the earliest, and
    the ship has to look stuck at two evaluations in a row: the fleet is loaded with arrivals
    dead-reckoned, which drops the arrival time for the seconds until the arrival handler docks it.
  - The contract rules judge the contract the bot works on: the contract plan's, accepted and
    unfulfilled, while the plan is active, waits for a ship or budget, or found no asteroid for its
    mineral. A non-mineral contract is parked by decision (D2) and left out. A contract that missed
    its deadline is an anomaly whatever was delivered.
  - The circuit breaker rule counts a blocked goal, or a trip in the last hour: mining and trading
    replace a blocked goal at once, so the breaker now remembers each ship's last trip.
  - Warnings count as errors: this codebase logs "something is wrong, carrying on" at Warning, like
    B31's missing deliverable on every tick in the soak test, or 1.9's ship that can't find a route.
    Journal lines don't count (their own rules watch them), nor does what startup logged. A Serilog
    sink hands every warning and error to the rule.
  - The API rules count the 401s and 429s that the client's innermost handler sees, over the last
    hour. A 401 during startup is agent bootstrap trying old tokens, and a reset stops the host
    (1.8), so `ApiUnauthorized` means a token the server rejects for another reason.
  - Defaults: a contract may go 4 hours without a delivery (the soak test's took 23 to 69 minutes);
    24 hours before the deadline, half delivered; a working ship 30 minutes without an update; an
    idle ship 10 minutes while work waits; one statement 5 warnings or errors in 10 minutes (once a
    minute is 10); credits unchanged 24 hours (the rule's own number); 10 429s an hour (none are
    expected, and a wrong reading of the burst limit would give dozens).
  - Noticed:
    - Once phase 6 switches plans on, expect `ShipStuck` for B17's drones and `ShipLeftIdle` for the
      starting probe (B25): the rules show known issues, as they should.
    - A burst of 429s raises `RepeatingError` next to `ApiThrottled`: the 429 handler logs a warning
      per retry.
    - Grafana repeats the anomaly email every 12 hours (gembernodes' notification policy) for as
      long as an anomaly lasts, such as a contract that missed its deadline.
    - `/status/anomalies` is an older credit-growth heuristic, unrelated to these anomalies.
      `docs/HOW_IT_WORKS.md` listed it among endpoints that read tables nothing writes, which
      stopped being true with 2.2; corrected.

**Phase 3 in short** (done 2026-10-01)
- The bot checks itself: every minute ten health rules, and the size guard every 5 minutes, compare
  its state with what it is meant to do, and each broken rule is an anomaly with a subject, details,
  a metric and journal lines, which the "anomaly active" alert (2.5) turns into an email after 15
  minutes. D12 removed the credit-drop alert; D13 keeps idle ships and unchanged credits quiet while
  no work waits.
- Traps it took: what "a ship changes state" means (a mining drone works for an hour in one nav
  state); the fleet's dead-reckoned arrivals, which hide the arrival time for a moment; and two
  rules that, read literally, the first run's own decisions would have broken.
- To understand this phase, start with `docs/HOW_IT_WORKS.md` section 12, then
  `HealthMonitorService.cs`, `HealthCheckContext.cs` and one rule such as `ShipStuckRule.cs` (all in
  `SpaceTraders.Application/Health`), and `HealthRuleScenarioTests.cs`.

### Phase 4: Back on the cluster (gembernodes)

**4.1 Database logins** (done by hand; one step left, see below)
- Do: check the connection string in the 1Password item `spacetraders-secrets`.
  - It should use a dedicated, non-superuser login that owns only the `spacetraders` database;
    create both if they're missing.
  - Add a read-only login (for example `spacetraders_ro`) for Claude on your PC.
- Note: a separate database on the shared server doesn't cap disk usage. That's the job of 1.6
  and the alert in 2.5.
- Prepared: `apps/spacetraders/README.md` in gembernodes (4.2's PR) has the steps and the SQL:
  - the login `spacetraders`: not a superuser, can't create databases or roles, and owns the
    `spacetraders` database, which only it and the read-only login may connect to;
  - the read-only login `spacetraders_ro`: read-only transactions, and reading rights on every
    table the bot creates (default privileges), except `stored_credentials` after the first start,
    because it holds the agent token;
  - the four fields of the 1Password item, the connection string pointing at
    `postgresql.flux-system.svc.cluster.local` like every other app's since 2026-09-26.
- Tested against PostgreSQL 18 (the cluster runs 18.4), with the published bot:
  - Started as `spacetraders` against the empty database, the bot created its 28 tables, with
    `cached_ships`'s storage parameters (B32), and its second start used them. It needs no more
    than that: it creates the database only when it's missing, and `pg_database_size` (1.6)
    works for the owner.
  - `spacetraders_ro` could read the tables the bot created after the grants, but not write, even
    with read-only switched off; after the revoke it couldn't read `stored_credentials`.
  - Another app's login couldn't connect, and `spacetraders` couldn't create a database.
- Done by hand before 4.2 was merged: both logins exist. Checked on 2026-10-02 with
  `tools/investigate/st.py check` (5.1): `spacetraders_ro` connects, isn't a superuser and is
  read-only. Its password is in psql's password file on your PC.
- Still to do: step 3 of the gembernodes README, as `postgres` in the `spacetraders` database:
  `REVOKE SELECT ON stored_credentials FROM spacetraders_ro;`. On 2026-10-02 the read-only login
  could still read the agent token. Until then `st.py` refuses any query that names the table.

**4.2 Manifests** (done: gembernodes PR #11, merged 2026-10-02)
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
- The Prometheus annotations are `prometheus.io/scrape: "true"`, `prometheus.io/port: "9090"` and
  `prometheus.io/path: /metrics` (2.1). Don't add 9090 to the Service.
- Noticed (2026-10-01): gembernodes is replacing ingress-nginx with Traefik
  (`docs/ingress-nginx-migration-plan.md`; phases 0 to 3 done). Traefik serves the existing
  Ingress objects, nginx annotations included, so the above still works; but once ingress-nginx
  is gone (its phase 4) the LAN address may be `192.168.1.231` rather than `.230`. Model the
  ingress on `grafana-internal-ingress.yaml` as it is by then.
- Done, in gembernodes PR #11; Flux deploys it once that's merged, which waits for 4.1.
  - `apps/spacetraders/` (the API and the WebUI, their Services, the 1Password item, the API's
    environment, `secret.yaml.template` and a README with the steps by hand),
    `namespaces/spacetraders-namespace.yaml` and `ingress/spacetraders-ingress.yaml`, each in its
    kustomization. Both images are main `12443acb`, the last build, with phases 1–3.
  - The API runs one pod and never two (`strategy: Recreate`): the rate limit is per account, and
    startup sync and recovery call the API before the leader lease decides which pod runs the
    tick.
  - Probes: startup `/health/startup`, every 10 s for up to 10 minutes; liveness `/health/live`;
    readiness `/health/ready`, which checks the database. The startup probe only works with B39
    fixed: with the current image it passes as soon as the HTTP server is up.
  - The Prometheus annotations point at 9090, which the Service doesn't route. The Prometheus
    chart's `kubernetes-pods` job (checked in 25.30.2) adds the `namespace` label that the
    dashboard and alerts select on.
  - The ConfigMap is `api.env`, through a `configMapGenerator`, so a change rolls the pod:
    Production, port 8080, `Metrics__Port` 9090 next to the annotation that points at it, and
    Serilog's default level, with how to override one. Checked with the published bot:
    `Logging__LogLevel__Default=Warning` (the old ConfigMap's style) left Information lines in the
    log, and `Serilog__MinimumLevel__Default=Warning` removed them.
  - Each container reads the secret fields it needs by name, instead of the whole item: the API
    four, the WebUI only the API key. A missing field keeps the pod in
    `CreateContainerConfigError`, which the existing "Container crash-looping or failing to start"
    alert reports.
  - A wait-for-postgres init container, like armabotcs and curatool, so a node reboot doesn't
    crash-loop the bot; and 60 s to stop, the app's own shutdown timeout.
  - The ingress follows `grafana-internal-ingress.yaml`: no host, no certificate, the LAN
    allowlist, `/spacetraders/api` and `/spacetraders/dashboard`. Traefik serves it on
    192.168.1.231, where the router points since the migration's phase 3, and ingress-nginx on
    .230 until it's removed.
  - Checked with `kustomize build` and `kubeconform -strict`.
  - Found on the way, and fixed, each reproduced first: B39 (`StartupProbeTests` got 200 while
    the chain ran) and B40 (nginx with the image's configuration logged 10 lines for 10 probes,
    and none after the fix). Both reach the cluster with the next image: both image tags in
    gembernodes move to the first `main` commit after them whose build pushes images.
  - B41 kept that from being `41a7c22` (#114's merge): its build failed in a test that had passed
    on the PR, because test hosts shared Serilog's static logger. `HostLoggingTests` reproduces it:
    after another host has started and stopped, a host's warning reached no error log at all.
  - Noticed:
    - Each poll of the startup probe while the chain runs logs one Warning ("Health check
      startup with status Degraded"): a few lines per start, before the health monitor starts,
      so `RepeatingError` doesn't count them.
    - Only the tick checks the leader lease: startup sync, recovery and the arrival timers
      (`ShipEventScheduler`) run in every pod. One replica and `Recreate` make that moot; more
      replicas would need more than the lease, although its comment says it covers them.

**4.3 First-run watch**
- Unpause the "SpaceTraders bot is down" alert (2.5). Done in 4.2's PR: it's live once that's
  merged and Grafana has restarted (the restart 2.5 still needs). Restart Grafana once Prometheus
  has scraped the bot, or the alert fires for its first minute without a series.
- Only the scout and contract plans are on (D9).
- First hour: messages per minute, database size, log lines per minute, anomalies. Then check
  again after 24 hours, then after a full reset period.
- Also check that the count of real 429s stays at zero (1.10).
- Phase 6 starts after a clean reset period.
- Started 2026-10-02:
  - Gembernodes PR #11 was merged at 08:48Z; the bot started at 08:50Z on main `3373ca7`, registered
    agent SPECTER, accepted a COPPER_ORE contract and bought a drone for it. Grafana was restarted
    at 09:09Z: the "bot is down" rule is no longer paused.
  - B39 and B40 hold on the cluster: the startup probe got 503 until the chain had completed, and
    the WebUI logs no probes.
  - The first six minutes: database 9.5 MB, 86 API calls and no 429s, about 28 messages and
    29 log lines a minute (about 42,000 lines a day while the scout works, under the budget of
    50,000).
  - Found: B42 (`RepeatingError` raised at 08:51:46 and cleared at 09:01:48), B43 (the ledger
    panels missed the deposit and the drone) and B44 (the error-log email at 08:55:50). B42 and B43
    are fixed in this repository; B44 in gembernodes PR #13.
  - Noticed: "Credits in the last hour" and "Credits per hour" show no data in the bot's first hour
    on the cluster. They subtract the credits of an hour ago (`offset 1h`), which didn't exist yet;
    across restarts `max()` keeps them working.
  - At 10:50Z, two hours in, with 5.1's helper: four starts, one per deploy (08:50, 09:57, 10:02,
    10:29), and no other restart; about 310 log lines an hour, now that the scout has finished (some
    7,500 a day); database 9.5 MB; no 429s and no failed API call in the last hour; no anomaly
    active. 5.1 found B45 in this run.
  - Still to do: the 24-hour check, and a full reset period.

### Phase 5: Claude as mechanic (on your PC)

**5.1 The `/st-investigate` skill** (done: `SpaceTraders/.claude/skills/st-investigate/SKILL.md`)
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
- Done (2026-10-02):
  - `tools/investigate/st.py` reads every source from your PC: `check` (the pods, Prometheus, Loki
    and the database in turn), `prom`, `logs` (with `--group`: warnings and errors per statement)
    and `sql`. This PC has no psql, jq or logcli, so it is Python with the standard library only. It
    starts and stops the port-forwards itself, runs psql from the `postgres:17` Docker image, reads
    the password from psql's password file, and masks tokens and passwords. Its README says how.
  - Grafana's datasource proxy needs a Grafana login, so the helper goes to Prometheus and Loki
    through `kubectl port-forward` instead. The internal API is left out: its key also unlocks
    `PUT /settings/*` and `POST /control/*`, and the database and the metrics hold the same state.
  - The skill: the helper's commands, the queries the run proved useful, and what the run taught
    (`Tick` marks a tick's lines and not a handler's, so two paths acting on one ship show
    interleaved; the startup probe's expected warnings; quoted PascalCase columns; plan state as
    JSON; a metric with no series as evidence). Its procedure now checks first whether a symptom is
    a known B-number and whether the running image has the fix, and lines code up with logs to the
    millisecond when two paths may race.
  - Checked against the first run, with the procedure:
    - **B42's anomaly**, the run's only one, explained: `RepeatingError` was raised at 08:51:46
      and cleared at 09:01:48, on Wolverine's "Utilizing service location" warning (`@i`
      `bd1538c5`). All 15 occurrences came from the first pod (image `3373ca7`), the last four
      at 09:54:38, when the contract's first delivery compiled `FulfillContractDeliveryCommand`'s
      handler: a handler's first message, as B42 says. The fix (`5d72e4e`, projects PR #116) runs
      since 09:57:50 (gembernodes PR #14), and none came in the three starts since.
      Reproduced: with the fix reverted, `HandlerCodegenTests` fails ("18x Utilizing service
      location …"); with it, it passes.
    - **B45, found:** grouping the run's warnings by statement showed one "re-created missing
      assignment" warning from the scout plan, at 09:18:58. The ship's lines, the plan's state in
      the database and the market metrics showed that the plan had skipped its last stop, and the
      lines' `Tick` property and millisecond times showed which two paths raced. Fixed with a test
      that interleaves them, in its own PR.

**5.2 Permissions** (done; your go-ahead of 2026-10-02)
- Allowed without asking: `kubectl get`, `kubectl logs`, `kubectl port-forward`, `psql` with the
  read-only login, and reads from Grafana. Everything else asks.
- Waits for 4.1, because the read-only login doesn't exist yet. These rules widen what Claude may
  do on your cluster without asking, so they go in only with your explicit go-ahead.
- Done when: the skill reproduces and explains one real anomaly from the first run. Done: B42 (5.1).
- Done: `SpaceTraders/.claude/settings.json` allows `Bash(python tools/investigate/st.py *)` and
  `Bash(kubectl -n spacetraders logs *)`, and nothing else. A session started in the `SpaceTraders`
  folder reads them.
  - The helper covers the port-forwards, the pods and their events, Prometheus, Loki and psql with
    the read-only login. Reads from Grafana aren't needed.
  - Not `kubectl get` as such: it reads Secrets too (`kubectl get secret -o yaml` shows the agent
    token, the API key and the database password), and a rule that matches the start of a command
    can't keep them out (`kubectl get pods,secrets`).
  - The helper is only as read-only as its code, which is in this repository; the database login
    is read-only on the server, whatever the code does.

**Phase 5 in short** (done 2026-10-02)
- Claude can investigate the running bot from your PC, read-only: `tools/investigate/st.py` reads
  the pods, Prometheus, Loki and the database (as `spacetraders_ro`), and the `st-investigate` skill
  turns a symptom into evidence, a test that reproduces it and a fix (5.1). Two commands run without
  asking: the helper and the bot's pod logs (5.2).
- Checked against the first run: the procedure explained B42's anomaly and reproduced it with its
  test, and found B45, the scout plan skipping a stop when a tick and an arrival race.
- Traps it took: this PC has no psql, jq or logcli; Grafana's API needs a login; plain `kubectl get`
  reads Secrets; the read-only login can still read the agent token until 4.1's revoke; Wolverine's
  warnings run to dozens of lines; and `sed -i` in Git Bash turns CRLF files into LF.
- To understand this phase, start with `.claude/skills/st-investigate/SKILL.md`, then
  `tools/investigate/README.md` and `st.py`'s `check`; for an example of the procedure, B45 in the
  known issues and `ScoutStopSkippedTests`.

### Phase 6: Make money, one loop at a time

A loop counts as done after a full reset period with no open anomalies for it on the dashboard.
How credits are split stays your call; Claude only fixes deviations from intended behaviour.

- **6.1 The first contract, end to end:** B8 and B9 (both fixed in 1.14; what's left is a clean
  reset period). Per D1 and D2 the bot takes one mineral contract per reset; taking the next
  contract is a later addition.
- **6.2 The command ship after scouting:** B10, and the scout part of B16 (both fixed in 1.14;
  what's left is a clean reset period). The ship moves on to its next job instead of holding on to
  the finished scout goal.
- **6.3 Probes** (`ProbeDeploymentPlanService`): B15 and B25.
- **6.4 Mining drones mine and sell** (`MiningAutomationService`, `MineAndSellGoalExecutor`): the
  survey part of B16, B17, and B34 (traits, so drones go where their mineral is).
- **6.5 Trading** (built 2026-10-02 on branch `claude/spacetraders-trading`). Asked for that day:
  1. a trader can be any ship with fuel and a cargo bay;
  2. a trip's profit is what the sell market pays minus what the buy market charges, minus the fuel,
     the fuel to get to the first market to begin with included (your note, the same day);
  3. two traders on one route are discouraged, to keep it simple (D18);
  4. when new prices come in, from a ship getting there or from the regular check of the ships at
     markets, the ship reconsiders whether its trade is still lucrative, and if not, goes elsewhere;
  5. goods in the market tree that let pricier goods become available come first (D15);

  and, added during the work: every market with one of our ships at its waypoint refreshes once every
  x minutes. That check didn't exist: markets only refreshed when a ship arrived. Then: a move or a
  trade goes before a market refresh, or any GET, for the rate limit. Your choices: D14–D19.
  - Done:
    - **The trade arithmetic** (`TradeRoutePlanner`, no I/O): profit = (sell price − buy price) ×
      units − the fuel for the whole trip: from where the ship is to the buy market, then on to the
      sell market. A trip is one purchase, as many units as the free hold, both markets' trade
      volumes and the credits allow (D17). Fuel is CRUISE, the distance rounded, paid in whole FUEL
      units (100 each) at the market each leg ends at; the logs of the first run confirm that (a
      222-long leg cost 3 × 79). A flight longer than one tank refuels at markets on the way, the
      fewest stops first: from J57, where scouting ended, one tank reaches only I56, and without stops
      the command ship found no route at all. Never DRIFT (B47).
    - **The trading plan** (`TradingAutomationService`) gives every free trader a trip each tick: a
      ship with a cargo hold and a fuel tank, no goal (or a blocked one), no assignment, not in
      transit. Cargo it holds is sold first, where it fetches most after fuel, when that earns
      anything. The best route goes first, to the trader it is best for; a held route isn't offered
      again (D18). It buys no ships (D16). Its state lists the held routes and the open ones, with the
      ships that could take them (`ShipLeftIdle` reads those, D13), and is written only when it changes.
    - **The trip** (`TradeBetweenMarketsGoalExecutor`) reconsiders where it lands, because a ship
      can't change course in flight, and changing its goal in flight would lose its arrival (B17): at
      the buy market, with the prices its arrival just fetched, it buys while the trip is still
      lucrative and otherwise drops it (`TradeDropped`), and the plan chooses again from there. The
      flight there is spent by then, so that check counts only the fuel still ahead. At the
      sell market, when selling there no longer pays and another market pays more after fuel, it takes
      the cargo there, once per trip (`TradeRerouted`). Sales above the market's trade volume go in
      several. New journal kinds: `TradeStarted`, `TradeRerouted`, `TradeDropped`.
    - **The market watch** (`MarketWatchService`), the last step of every tick, one market a tick
      (D19): each market with one of our ships at it is fetched again once `Market.RefreshMinutes` (5,
      new; 0 = off) have passed since it was last seen, the one that has waited longest first. A market
      that fails, or comes back without prices, waits an interval too. A probe parked at a market keeps
      it current; until the probe plan runs (6.3), that is the starting probe at H52 and wherever the
      other ships are.
    - **Writes before reads** (D19, `RateLimitingHandler`): a GET gives way while a POST waits for the
      budget (before, it also gave way while a POST was in flight, and could use the whole burst),
      leaves the last 10 of the 30-request burst to writes, and stops giving way after 10 seconds.
      The wait metric has a `kind` label, `read` or `write`, which shows whether a write ever waits.
      On 2026-10-02 the bot made 67 GETs and 425 POSTs in 6 hours and waited 0 seconds: this is for
      when probes watch every market.
    - **B46**: one goal step at a time per ship (`ShipGoalStepGuard`); a step that finds its ship busy
      is skipped, and the next tick takes it.
    - The production chains are fetched once per process and shared with the markets dashboard
      (`SupplyChainCache`): still one call per start.
    - A dry run of the planner on the live prices of 2026-10-02 (the command ship at J57, 252 fuel,
      129,451 credits): two routes clear 200 a unit, both feeding nothing. The first trip would be
      MEDICINE from D41 to A1, about 6,982 after 738 of fuel: 648 to get from J57 to D41 by way of
      I56, and 90 on to A1. EQUIPMENT from K85 to D41, which feeds SHIP_PARTS, earns 198 a unit from
      there, the 530 to get to K85 included.
  - Noticed (not changed):
    - Travel time is not in the profit (your formula): a trip across the system counts the same as
      one next door.
    - Prices are as last seen. Most markets were last seen during scouting, hours old; each arrival
      and the watch refresh them, and the trip reconsiders on arrival.
    - The watch adds price samples: 3 markets with ships today is about 4,000 rows a day; with probes
      at all 25 markets about 40,000 (7 days raw, then hourly; the size guard watches).
  - To switch it on: `PUT /settings/Automation.Plan.Trading.Enabled` with `{"value": "true"}`.
  - Done when: a full reset period with the trading plan on and no open anomaly for it.
- **6.5 in short** (built 2026-10-02): idle ships with a hold trade between markets for the most
  profit after fuel, routes that grow a pricier good's production first; each trip checks its prices
  again at both markets, and the markets where ships are refresh every 5 minutes, after the moves and
  trades, which go first for the rate limit too (D19). To understand this,
  start with `SpaceTraders.Application/Trading/TradeRoutePlanner.cs`, then
  `Automation/TradingAutomationService.cs` and `Goals/Executors/TradeBetweenMarketsGoalExecutor.cs`;
  `tests/SpaceTraders.Application.Tests/Trading/TradeFixture.cs` holds the live prices the tests use.
  - Files, in `SpaceTraders.Application` unless named:
    - new: `Trading/TradeRoutePlanner.cs` (the arithmetic and refuelling flights), `TradeMarketMap.cs`,
      `TradeContextReader.cs`; `Automation/MarketWatchService.cs`; `Goals/ShipGoalStepGuard.cs` (B46);
      `Services/SupplyChainCache.cs`;
    - rewritten: `Automation/TradingAutomationService.cs`, `Goals/Executors/TradeBetweenMarketsGoalExecutor.cs`;
    - changed: `TradeBetweenMarketsGoal` (`SpaceTraders.Domain/Goals/ShipGoal.cs`: the trip's plan and
      progress), `GameLoopService` (the watch first), `ShipGoalExecutorService` (the guard),
      `ShipLeftIdleRule` (candidates), `MarketAutomationPlanState`, `JournalEvents`,
      `DependencyInjection`; `RequestBudget` and `RateLimitingHandler` (Infrastructure.SpaceTradersAPI,
      D19); `PrometheusMarketMetricsService` (SpaceTraders.API, the shared cache) and
      `PrometheusAutomationMetrics` (the wait metric's `kind`);
      `DefaultSettingsSeed` (Persistence: `Market.RefreshMinutes`); `GetActiveTradeRouteTargetsAsync`
      removed from the goal repository;
    - tests: `Trading/TradeRoutePlannerTests`, `TradingAutomationServiceTests`,
      `TradeBetweenMarketsGoalExecutorTests`, `MarketWatchServiceTests`, `SupplyChainCacheTests`, B46 in
      `ShipGoalExecutorServiceTests`, the trip's round trip in `ShipGoalRepositoryTests`, D19 in
      `RateLimitHandlerTests`.
- **6.6 Jump gate construction.**

## Changes in gembernodes

Changes to files are made on a branch there, and Flux deploys them once merged. The rest needs
your PC, 1Password or kubectl:

| Slice | Change |
|---|---|
| 2.4 | `infrastructure/monitoring/dashboards/spacetraders-dashboard.json` plus a `configMapGenerator` entry (merged: PR #10) |
| 2.5 | Rules in `infrastructure/monitoring/grafana-alerting-provisioning.yaml`, then a Grafana rollout restart (merged: PR #10; Grafana restarted 2026-10-02) |
| 2.7 | The fleet table's new columns, and panels for the holds and for what was mined (merged: PR #15) |
| 2.8 | A markets dashboard per system, uid `spacetraders-markets` (merged: PR #17) |
| 4.1 | Database login and read-only login (Postgres and 1Password): by hand, with the steps in `apps/spacetraders/README.md` (done; the revoke on `stored_credentials`, step 3, is still to do) |
| 4.2 | `apps/spacetraders/`, `namespaces/spacetraders-namespace.yaml`, `ingress/spacetraders-ingress.yaml`, plus the kustomization entries (merged: PR #11) |
| 4.3 | "SpaceTraders bot is down" unpaused (merged: PR #11), then the Grafana rollout restart (done 2026-10-02 09:09Z) |
| 4.3 | B44: the bot's error lines get a rule of their own, by log level, instead of the shared rule's word match (merged: PR #13); then a Grafana rollout restart (done 2026-10-02 09:44Z) |
