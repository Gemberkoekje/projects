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

## Where things stand (2026-10-04)

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
- Slice 6.5 (trading) was merged as projects#121 on 2026-10-02 (deployed by gembernodes#20), with
  your decisions D14–D19; it found B46 (fixed with it) and B47 (open). The trading plan stays off
  until you switch it on (D9).
- Slice 6.4 (surveying and mining, and cargo ships for trading) was merged as projects#122 on
  2026-10-02 (its dashboard section as gembernodes#21), with your decisions D20–D25, and deployed by
  gembernodes#22 at 14:13Z. It fixed B34 first, at your request, then the survey part of B16, B17
  for the mining and survey trips, B48 and B49. Trading was switched on at 13:08Z, surveying and
  mining at 14:14Z. Its first watch found B50, fixed with your decision D26 (projects#123, deployed
  by gembernodes#23 at 14:47Z). That deploy's first surveys found B51, fixed by projects#125
  (deployed by gembernodes#25 at 15:08Z). Your decision D27, a stock of surveys per ore instead of
  surveying the contract's ore without end, is live (projects#126 and #127, deployed by gembernodes#26
  and #27). The contract was fulfilled at 15:49Z (23,076 credits). Your decision D28, one rule for
  what miners mine and when a drone is bought, was merged as projects#128.
- Slice 6.3 (probes) was merged as projects#129 on 2026-10-02 (deployed by gembernodes#28), with your
  decisions D29 (a probe for every market, bought while the credits stay at 100,000; until then they
  roam) and D30 (a purchase where none of our ships is fetches a probe first). It fixes B15 and B25. The
  probe plan stays off until you switch it on (D9).
- Slice 6.7 (siphoning) was merged as projects#130 on 2026-10-02 (deployed by gembernodes#29), with your
  decisions D31–D33: siphon drones siphon gases at gas giants by the miners' rules, without surveys (the
  API's siphon call takes none). The siphon plan stays off until you switch it on (D9).
- Slice 6.8 (spare time) was merged as projects#131 on 2026-10-02 (deployed by gembernodes#30), with your
  decisions D34–D37, asked that day: "I'd like my command ship not to be idle." With nothing to survey the
  command ship trades, and with no trade either it mines or siphons whatever sells at the nearest place it
  can, and sells it; a survey or a trade interrupts that. You switched on the siphon, probe and spare-time
  plans at 21:40–21:42Z; the first minutes' journal matched the plan.
- Slice 2.9 (a settings table on the dashboard) was merged as projects#132 and deployed by gembernodes#31 on
  2026-10-02: which settings exist, and which are on.
- Slice 6.9 (roles by what pays most, and cargo nothing will sell) was merged as projects#133 with your decisions
  D38–D42, and deployed by gembernodes#32 on 2026-10-03 at 07:27Z; the role board is on.
- The health check of 2026-10-03, after the bot's first day on the cluster, found B52 (projects#134, deployed by
  gembernodes#33) and B53 (projects#135). gembernodes#34 makes the log budget a number per ship and stays open until
  slice 6.10a is merged, so it deploys both.
- Slice 6.10 (the fleet's shape, asked on 2026-10-03, with your decisions D43–D51) is split in three: 6.10a (visibility and
  the role board's rates) is merged and deployed (projects#136, gembernodes#34); 6.10b (the order ships are bought in, a designated
  surveyor, one drone per scarce mineral, and a credit reserve that grows with the trading holds, D51) is merged and deployed
  (projects#137, its turn fix projects#138, gembernodes#35, #36 and #37); 6.10c (drones drifting to minerals out of fuel range)
  is merged and deployed (projects#139, gembernodes#37, 12:13Z on 2026-10-03). Its first drift (SPECTER-4 to B7, 12:35Z)
  found B54 (projects#140, gembernodes#38), and the designated surveyor's first wait found B55 (projects#141, gembernodes#39)
  and your decision D52, a ship that can only survey surveys on (projects#142, gembernodes#40: the cluster runs `6c9f8cc`
  since 14:00Z). Your decision D53, coverage per area, is merged and deployed (projects#143, gembernodes#41: the cluster runs
  `489720e` since 14:40Z; its first coverage drone, SPECTER-11, mines the middle's silicon). Your decision D54, the survey
  ship works where most drones mine, and D55, a survey ship per area with drones, are merged and deployed (projects#144,
  gembernodes#42). Their first move found B56: the move never ran (projects#145, gembernodes#43: the cluster runs
  `827785b` since 16:07Z). B57, sales booked without their market and unit price, is merged and deployed (projects#146,
  gembernodes#44: the cluster runs `2cfc071` since 17:53Z). Your decision D56, trade only full holds, in one purchase and
  one sale, and keep ship purchases back while a trader saves up for one, is merged and deployed (projects#147,
  gembernodes#45: the cluster runs `ef1cdb7` since 19:00Z), and so is B58, a survey ship flying to an asteroid it can't
  get away from (projects#148, gembernodes#46: the cluster runs `e17a765` since 19:03Z; SPECTER-F surveys B14 since
  19:17Z). B59, 429s from the API's rate limiter while the bot keeps to its budget: its first step, logging the
  limiter's headers with each 429, is merged and deployed (projects#149, gembernodes#47: the cluster runs `5ef42bd`
  since 19:51Z). Your decision D57, credits held back for a trade trip from the moment it starts until it buys, is merged
  and deployed (projects#150, gembernodes#48), and so is your decision D58, drones gather first (projects#151,
  gembernodes#49).
- Slice 2.10 (the API's request rates on the dashboard, and the graphs' legends as tables, asked on 2026-10-03) is merged
  and deployed (projects#152, gembernodes#50: the cluster runs `df677b9`).
- Slice 6.11 (exploring through the jump gates, and a systems dashboard, asked on 2026-10-04, with your decisions
  D59–D63) is merged and deployed (projects#153, its dashboard gembernodes#51, deployed by gembernodes#52). It found and
  fixed B60. You switched the explore plan on at 11:54Z: the command ship explored X1-KR90 and had jumped on to X1-CV66
  when the reset ended the run.
- Slice 2.11 (each ship for sale's tank, hold, what it could do in the fleet and its equipment, on the markets dashboard's
  shipyards table, asked on 2026-10-04) is merged and deployed (projects#154, gembernodes#52).
- Slice 6.6 (the jump gate, asked on 2026-10-04, with your decisions D64–D68) is merged and deployed (projects#155, its
  dashboard gembernodes#53, deployed by gembernodes#52). Supplying a construction site pays nothing. X1-DC53's own gate was
  complete before our ships came; the new home system's gate is not (below).
- The server reset on 2026-10-04 at 13:00Z. The bot found it at 13:00:33Z, restarted until the API was back (it answered
  503, "The universe is being reset", for about six minutes), and registered a new agent at 13:06Z, SPECTER again, in
  X1-FJ91. The scout plan visited all 28 of its markets (so B45 is fixed), and the contract was fulfilled at 16:12Z. Its
  jump gate, X1-FJ91-I64, needs 1,600 FAB_MATS and 400 ADVANCED_CIRCUITRY, which the construction plan (6.6) now works on;
  until it is built the explore plan has no gate to jump through.
- Slice 2.12 (asked on 2026-10-04, after that reset left the new agent with only the scout and contract plans on, with your
  decision D69) is merged and deployed (projects#156, gembernodes#55: the cluster runs `52a73fd` since 18:09Z): every plan
  is on by default, the settings you set carry over to the next run (the agent the next reset registers), and
  `/settings/next-run` sets the next run alone. Its first start switched on, at 18:09Z, the plans the agent of 13:00Z had
  had off. With it, "stays off until you switch it on (D9)" in the bullets above no longer holds.
- Phase 6's checks, on the run that ended at the reset (on the cluster since 2026-10-02 08:50Z, so the last 2.2 days of
  its period): 6.10b's and 6.10c's are met. The other loops ran without anomalies of their own, but none has had a full
  period yet; the first is the one that began at 13:00Z, with every plan on since 18:09Z. The only anomalies left open
  were B59's 429s.

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
| B15 | **The probe plan buys a probe every tick for a target whose probe is still travelling.** Each pass starts with an empty in-flight set, and a travelling probe doesn't count as available, so the target looks unserved. Only the credit reserve stops the purchases. | `ProbeDeploymentPlanService.cs:220-245, 336-345, 427-438` | 6.3 (done: the plan buys by count, probes in flight included, and a probe in flight keeps its market) |
| B16 | **Goal status never changes, and scout and survey goals are never cleared.** `UpdateGoalStatusAsync` has no production caller, so every goal stays `Assigned` and the Completed/Blocked checks in mining and trading never match.<br>• A finished scout goal keeps the command ship "busy" (B10).<br>• When the mining executor replaces a miner's goal with a survey goal, that miner keeps surveying and never returns to mining. | `ShipGoalRepository.cs:70-81`, `MineAndSellGoalExecutor.cs:194-213`, `MiningAutomationService.cs:397` | scout part: 1.14 (done); survey part: 6.4 (done) |
| B17 | **Some ships stay "in transit" after arriving.**<br>• The arrival handler ignores a wake-up whose goal id doesn't match the ship's active goal, and the mining and contract commands navigate without a goal id.<br>• Executors reload the ship with `FindAsync`, which doesn't apply arrival dead-reckoning. Only `GetAllAsync` does, in memory.<br>• The contract commands dead-reckon for themselves, but a mining drone keeps seeing "in transit" after its first leg. | `ShipArrivedEventHandler.cs:26-34`, `ShipRepository.cs` (`FindAsync` vs `GetAllAsync`), `MineResourceVolumeCommand.cs:67-100` | 6.4 (done for the mining and survey trips, which navigate with their goal; the contract commands still dead-reckon for themselves) |
| B18 | **Most settings do nothing.** Of the 47 seeded settings, only `Automation.Enabled` (partly, see B5), `FleetExpansion.MinCreditReserve`, `Mining.MaxDrones`, `ActivityLog.RetentionDays` and `Alerts.WebhookUrl` change what the bot does.<br>• `Navigation.*` and `Maintenance.*` are read only by services that never run.<br>• `Trade.*` is read only by the market views.<br>• 21 keys are read by nothing at all.<br>• The `Runtime.*` keys are status flags, not settings to tune.<br>The settings table in `docs/HOW_IT_WORKS.md` lists each one. | `DefaultSettingsSeed.cs` | 2.6 (done) |
| B19 | **Price history is never recorded.** Market trade goods are stored as camelCase JSON, but `MarketPriceSampleRepository` reads them back case-sensitively into PascalCase properties. Every good is skipped, so `market_price_samples` stays empty and the price endpoints return nothing. `MarketRepository` reads the same JSON case-insensitively, so mining and trading are unaffected. | `MarketPriceSampleRepository.cs:15, 119-127`, `SpaceTradersPortAdapter.cs:202` | 2.2 (done) |
| B20 | **A restart clears every ship's active goal.** Startup sync overwrites each existing ship row with `SetValues(new CachedShip { … })`, and that object doesn't carry the goal columns, so they become null.<br>• A scout ship whose assignment already matches the current route step doesn't get its goal back.<br>• Arrival wake-ups scheduled before the restart no longer match any goal (B17). | `StartupSyncService.cs:106-130` | 1.12 (done) |
| B21 | **The app tables may never be created** (confirmed in 1.13). `EnsureCreatedAsync` does nothing when the database already holds any table. Wolverine creates its `wolverine` tables when the host starts, before the deferred initializer runs, so the initializer's `ALTER TABLE` statements would then fail. The cluster's database will be empty on redeploy. | `SpaceTradersDatabaseInitializer.cs:12`, `Program.cs:75-78`, `DeferredStartupHostedService.cs:22-30` | 1.13 (done) |
| B22 | **The dashboard publishes the internal API key.** The WebUI container writes the key into `config.js`, which anyone who can open the dashboard can read. The old ingress served both the dashboard and the API on the public `gemberkoekje.nl`, so anyone could call `PUT /settings/*` and `POST /control/*`. | `SpaceTraders.WebUI/docker-entrypoint.sh:11-29`, `SpaceTraders.WebUI/index.html:17`; gembernodes `3f9f785^:ingress/spacetraders-ingress.yaml` | 4.2 (done: LAN only, not merged) |
| B23 | **A failed startup leaves an idle pod that looks healthy.** One try/catch wraps the startup chain. If database init, agent bootstrap, the run lifecycle, startup sync or recovery throws, the later services (the tick and pruning among them) never start, and nothing retries. `/health/live` runs no checks, and the old deployment used it for the startup and liveness probes, so Kubernetes never restarts the pod. | `DeferredStartupHostedService.cs:57-89`, `Program.cs:146` | 1.11 (done) |
| B24 | **WebUI loose ends** (minor).<br>• SignalR refresh hints probably never match a query: the client reads a string `kind`, but the server sends an object.<br>• The end-to-end test opens `/orchestration`, but the route is `/plans`.<br>• The unrouted pages in `src/Future` call endpoints that don't exist. | `signalr.tsx:27-28`, `DashboardNotifier.cs:19,28`, `orchestration.e2e.ts:5` | with D5 |
| B25 | **The starting probe is probably not recognised as a probe.** Startup sync stores a ship's registration role as its type (`SATELLITE` for the starting probe), but the probe plan only accepts type `SHIP_PROBE` or a symbol containing `PROBE` or `SATELLITE`, and ship symbols look like `AGENT-2`. The plan then buys a probe instead of using the free one. | `StartupSyncService.cs:64`, `ProbeDeploymentPlanService.cs:495-498` | 6.3 (done: `FleetRoles.IsProbe` reads the frame and both cached types) |
| B26 | **A newly registered agent had no settings until the pod restarted** (found and confirmed in 1.4). Registration wrote the new agent's rows and default settings through a DbContext that was created before the new agent was set: resolving the API client creates it, for the endpoint-usage counter. So all of it was stored under the previous agent. With every setting missing, `Automation.Enabled` read as off, and after a server reset the bot sat idle until its next restart. | `AgentBootstrapService.cs` (`RegisterNewAgentAsync`), `ApiEndpointUsageRecorder.cs` | 1.4 (done) |
| B27 | **A contract plan waiting for budget calls the API on every tick** (found in 1.9, seen at runtime in 1.14: 12 calls a minute). It is retried every 5 s, and each retry fetches every contract (`GET my/contracts`) and saves a new plan: 12 calls a minute while it waits, and the waiting can last as long as the credits stay short. Its log lines went to Debug in 1.9; the calls are still there. | `ContractPlanService.cs` (`EnsureBootstrappedAsync`, `RefreshContractsCacheOnceAsync`) | 1.14 (done) |
| B28 | **Startup sync caches a shipyard without its prices** (found in 1.14). It stored the priced ships where the ship types belong and left the prices empty, and purchases read the price from the latter. A purchase at a shipyard where a ship sat at startup then failed with "price unknown" until a ship arrived there again, and every restart did the same to each shipyard with a ship parked at it; a parked probe never leaves. In the soak test the contract plan waited 9 minutes for a drone it could afford, until the scout docked at that shipyard. | `StartupSyncService.cs` (`EnsureFacilitiesForShipsAreCachedAsync`) vs `SpaceTradersPortAdapter.GetShipyardAsync`; `ShipyardRepository.MapToDto`, `ShipPurchaseService.ResolveShipPurchasePrice` | 1.14 (done) |
| B29 | **Wolverine logs every handled message at Information** (found in 1.14). It logs "Successfully processed message …" under the message type's name, so the `"Wolverine": "Warning"` override doesn't reach it: one line per message. | `DependencyInjection.cs` (`AddWolverine`); Wolverine's `MessageSuccessLogLevel` defaults to Information | 1.14 (done) |
| B30 | **A contract delivery sends every unit aboard** (found in 1.14). A trip's last extraction can bring more aboard than the contract still needs; the surplus earns nothing, and whether the API refuses such a delivery outright is unconfirmed (its docs don't say). If it does, the final delivery fails on every tick. | `FulfillContractDeliveryCommand.cs` | 1.14 (done) |
| B31 | **A restart blanks the contract's terms** (found in 1.14). Startup sync stored contracts without their deadline and deliverables, so after every restart the contract plan couldn't read its deliverable until the next delivery response wrote it back: it couldn't restore a lost assignment, and a ship that had delivered everything couldn't fulfil. | `StartupSyncService.cs` (contracts) | 1.14 (done) |
| B32 | **The ship table grows without end** (found in 1.14). `cached_ships` holds a few wide rows (about 2 kB of ship JSON each) that change every minute or so. Postgres prunes their old versions in place, which kept the table's dead-tuple count under the autovacuum trigger (50), so VACUUM never ran and every update that didn't fit its page extended the table: about 250 kB an hour with one busy ship, and in phase 6 that scales with the fleet. | soak samples: 32 pages for 3 rows, 0 autovacuums in 2 hours | 1.14 (done) |
| B33 | **Contract payments never reach the cached credits** (found in 1.14). The accept and fulfil responses carry the agent's new credits, but only refuels, sales and purchases wrote them to the cached agent. Purchases are budgeted from the cache, so after a contract the bot thinks it has less than it does until the next restart: in the soak test 6,620 less (130,564 cached, 137,184 in the game). | `FulfillContractDeliveryCommand.cs`, `ContractPlanService.cs` (accept) | 1.14 (done) |
| B34 | **Waypoint traits are never stored** (found in 1.14). Startup sync stores only a waypoint's market and shipyard flags, and nothing calls `WaypointRepository.UpsertRangeAsync`, the one method that writes traits (and modifiers, orbitals and charts). What reads them finds nothing:<br>• the mining plan means to pick the nearest asteroid whose deposits can yield the mineral, but always falls back to the nearest asteroid of any kind, so a drone can be sent where its mineral never comes up;<br>• the contract plan's trait score is always 0. It only breaks ties between equally near asteroids (nearest first is by design); whether a farther asteroid with the right deposits should win is a strategy question;<br>• the dashboard shows no traits. | `StartupSyncService.cs` (`EnsureSystemsForShipsAreCachedAsync`), `MiningAutomationService.cs` (`MatchesTradeSymbolAvailability`), `ContractPlanService.cs` (`ScoreTradeSymbolMatch`), `FleetStatusMapper.cs` | 6.4 (done) |
| B35 | **The startup snapshot repeats startup sync's API calls** (found in 1.14). Right after startup sync it fetches the agent, the ships, the system, every page of its waypoints, and the markets and shipyards where ships are, all of which sync has just fetched or found cached: about 11 calls on every start. | `StartupSnapshotService.cs` vs `StartupSyncService.cs` | 0.5 (done) |
| B36 | **The Docker integration tests skip silently on Windows** (found in 1.14). Four test classes decide whether Docker runs by looking for `/var/run/docker.sock` or `DOCKER_HOST`. Docker Desktop on Windows has neither, so the tests report "skipped" while Docker is running. Until it's fixed, the README says to set `DOCKER_HOST=npipe://./pipe/docker_engine`. | `MessageStorageIntegrationTests.cs`, `AgentCleanupIntegrationTests.cs`, `DatabaseInitializerTests.cs`, `IntegrationTestBase.cs` | 0.5 (done) |
| B37 | **The credit-drop alert can't fire** (found in 2.2). `AlertHandler` compares each credit change with the credits it remembers in a field from the previous one, but Wolverine creates the handler anew for every message, so the field is always empty. Until 2.2 nothing published the event anyway. The event carries the old credits, so the fix is small, but it would then warn, and post to `Alerts.WebhookUrl`, on every purchase that costs more than 10% of the credits, a ship included. | `AlertHandler.cs` (`_previousCredits`) | D12: removed |
| B38 | **Startup recovery reports docked ships as in transit** (found in a local run during phase 2, through `spacetraders_messages_handled_total`). Its first branch takes any ship whose cached arrival time has passed for a ship that has just arrived, but that time stays on a ship after it docks: startup sync stores the last route's arrival. So every docked ship that ever travelled gets a `ShipInTransitEvent` (an "in transit" activity row) on every start: three in a run with an idle fleet. `docs/HOW_IT_WORKS.md` describes the branch as "still marked in transit", which the code doesn't check; a ship that really arrived while the bot was down comes back from `GetAllAsync` already in orbit, so the branch never sees one. | `StartupRecoveryService.cs` (`RecoverShipAsync`), `StartupSyncService.cs` (`ArrivesAt`) | 6.4 (fixed: only a ship marked in transit is recovered as one, so a docked or orbiting ship gets its goal step and no `ShipInTransitEvent`; the "in transit" rows already written are pruned after 30 days) |
| B39 | **`/health/startup` answers 200 while the startup chain runs** (found in 4.2). The check reports "still running" as Degraded, and ASP.NET Core answers Degraded with 200 unless told otherwise. The startup probe that 4.2 adds for B23 would pass as soon as the HTTP server is up, like the old probe on `/health/live`. | `Program.cs` (`MapHealthChecks("/health/startup")`), `StartupInitializationHealthCheck.cs` | 4.2 (done) |
| B40 | **The dashboard's probes fill the bot's log budget** (found in 4.2). nginx logs every request, and the WebUI's liveness and readiness probes call `/healthz` 8 times a minute: about 11,500 lines a day in `{namespace="spacetraders"}`, which the dashboard's log-volume panel and the 50,000-a-day alert (2.4, 2.5) count as the bot's. An idle bot logs nothing, so the panel would have shown only the probes. | `SpaceTraders.WebUI/nginx.conf` (`location = /healthz`) | 4.2 (done) |
| B41 | **Hosts in one process share a logger** (found when CI failed on main at `41a7c22`, after #114 had passed). By default the Serilog hosting package sends a host's lines to the process-wide `Log.Logger`, which every host replaces when it starts and closes when it stops. Production runs one host per process; the tests run several side by side, so a host's warnings could reach another host's error log, or none. `HealthRuleScenarioTests`' RepeatingError scenario failed that way, which kept CI from building the images. | `Program.cs` (`UseSerilog`), Serilog.Extensions.Hosting (`preserveStaticLogger`) | 4.2 (done) |
| B42 | **A handler's first message raises `RepeatingError`** (found in 4.3, on the cluster). Wolverine compiles a handler when its first message comes, and under 5.x's `AllowedButWarn` (`RestoreV5Defaults()`) it logged a warning for each dependency it resolves from the container ("Utilizing service location for …"). They all share one template: 11 in the bot's first minute, over the rule's limit of 5, so the anomaly was raised on every start, and could be again whenever a handler first ran later. Their text ("…this is an error") also tripped the error-log alert (B44). | `DependencyInjection.cs` (`RestoreV5Defaults()`), `RepeatingErrorRule.cs` | 4.3 (done) |
| B43 | **A counter's first value never reaches the dashboard** (found in 4.3). Prometheus's `increase()` and `rate()` count what a series gains between two scrapes, never the value it has when first scraped, and prometheus-net creates a labelled series on its first increment. On the cluster the contract's deposit (4,267) and the first drone (46,885) were booked about 25 seconds before Prometheus first scraped the pod, so the ledger panels showed neither; a single 429, failed call or breaker trip could never show at all. | `PrometheusAutomationMetrics.cs`; the dashboard's ledger, 429 and breaker panels | 4.3 (done) |
| B44 | **The error-log alert fires on the bot's ordinary lines** (found in 4.3). Gembernodes' "Error logs detected" rule matches `(?i)error` anywhere in a line, and 2.5 added `spacetraders` to it. The bot's JSON lines contain the word without being errors: the startup settings dump (`Health.Errors.MaxRepeatsIn10Minutes`), Wolverine's "…this is an error" (B42) and a `RepeatingError` anomaly's own lines. It fired five minutes after the first start. | gembernodes `infrastructure/monitoring/grafana-alerting-provisioning.yaml` (`loki-error-logs`) | 4.3 (done: gembernodes PR #13) |
| B45 | **The scout plan can skip a stop** (found in 5.1, on the cluster). When the ship docks at a stop, the tick and the arrival can both run its goal step. On 2026-10-02 at 09:18:58 the arrival's step moved the plan from stop 25 to stop 26, X1-DC53-J58. In the same second the tick's resume check read the plan from before that advance and the assignment from after it, took the assignment for missing and set the ship's goal back to stop 25; and the visit to stop 25 completed a second time, in the tick's goal step, which moved the plan past stop 26. The plan logged "all 26 waypoints visited", but J58's market was never fetched, and markets aren't scouted again. Any stop can be skipped this way, whenever a tick coincides with an arrival. | `ScoutAllMarketplacesPlanService.cs` (`ResumeIfAssignmentMissingAsync`, `AdvanceAsync`), `ShipGoalExecutorService.cs`; Loki, 09:18:58Z (`Tick` 326) | 5.1 (done) |
| B46 | **Two goal steps can run for one ship at once** (found in 6.5, from the code; B45 was the scout plan's case). The tick steps every ship every 5 s, and an arrival steps the ship it docks, on a thread of its own. Both read the ship before either acts, so a trade step would buy twice, or try to sell cargo that is already sold. | `GameLoopService.cs` (goal steps), `ShipNavigationCompletedHandler.cs`, `ShipGoalExecutorService.cs` | 6.5 (done) |
| B47 | **The navigation's fuel fallback leaves a ship in DRIFT** (found in 6.5, from the code). When a flight needs more fuel than the ship has, `NavigateSubCommand` switches it to DRIFT, which burns 1 fuel whatever the distance, and flies there. Nothing switches it back, so every later flight of that ship is DRIFT, about ten times slower than CRUISE. Trade trips plan refuelling stops and never need the fallback (6.5); scouting and contract flights still can. A probe has no tank, so no flight of its runs short of fuel, and the probe executor switches a probe it finds in DRIFT back to CRUISE (6.3). | `INavigateSubCommand.cs` (`TrySwitchToDriftForFuelEfficiencyAsync`) | 6.10c (in part: the flights of mining, siphon, survey, spare-time and trade trips ask for CRUISE, so a ship left in DRIFT flies them in CRUISE again; scouting and the contract's flights don't, and the fallback still switches to DRIFT) |
| B48 | **Only two of three asteroid types can be mined** (found in 6.4, from the code and the live waypoints). `MineResourceVolumeCommand` accepted only `ASTEROID_FIELD` and `ENGINEERED_ASTEROID`; 56 of X1-DC53's 57 asteroids are of type `ASTEROID`, so a drone sent to any of them got a state mismatch instead of ore, on every step. | `MineResourceVolumeCommand.cs` (`IsValidExtractionWaypoint`) | 6.4 (done) |
| B49 | **A used-up survey is tried again and again** (found in 6.4, from the code). An extraction with a survey that is exhausted, expired or doesn't verify fails with 4224, 4221 or 4220, and nothing removed the survey from the cache, so the miner picked it again on every step; only its expiry ended that. | `MineAndSellGoalExecutor.cs` (`GetBestActiveSurveyAsync`), `SurveyRepository.cs` | 6.4 (done) |
| B50 | **The command ship stays on the contract after the survey plan is switched on** (found in 6.4's first watch, on the cluster). The contract plan gave SPECTER-1 a contract assignment at 14:14:29Z, 23 s before the survey switch, and a contract assignment lasted until the contract was fulfilled. The survey plan takes only free ships, so SPECTER-1 went on mining copper (7 units in its first 22 minutes) instead of surveying (D20). The plan also gave its first ship its assignment back on every tick, whatever that ship did or had become. | `ContractPlanService.cs` (`EnsureActivePlanAssignmentAsync`), `FulfillContractDeliveryCommand.cs` | 6.4 (fixed with D26) |
| B51 | **The API can't read the surveys the bot sends back** (found on the cluster on 2026-10-02, with the first surveys, right after the D26 deploy). Every extraction with a survey was answered 422 "invalid payload": the bot sent the survey's expiry as .NET writes a `DateTimeOffset` (`2026-10-02T15:44:51.937+00:00`), not as the API gave it out (`…51.937Z`). The refusal isn't one of B49's codes, so the step failed, Wolverine tried it 4 more times, and the next tick did it all again: 364 failed calls in the first 12 minutes, and SPECTER-3 extracted nothing. | `SpaceTradersApiClient.cs` (`ExtractWithSurveyAsync`), `SpaceTradersPortAdapter.cs` | 6.4 (fixed) |
| B52 | **After a restart the dashboard could read 0 credits** (found in a health check on 2026-10-03, on the cluster). prometheus-net publishes a gauge without labels at 0 as soon as it is defined, and the bot sets the credits only once the startup chain knows the agent. When Prometheus scraped the new pod in between, it stored the 0: after the deploy of 2026-10-03 the credits read 0 from 07:27:51Z to 07:28:36Z, and with the old pod gone at 07:28:30 the dashboard's "Value gained per hour" fell from 56,896 to −517,672, to show the same amount as a gain an hour later. After the deploy at 15:45Z the day before, the credits and the database size both read 0: −153,292, then +228,819 at 16:45:30Z. Three of the run's 16 starts stored a 0 (15:34Z and 15:45Z on 2026-10-02, 07:27Z on 2026-10-03); whether one does depends on when the first scrape comes. | `PrometheusAutomationMetrics.cs` (`spacetraders_agent_credits`, `spacetraders_db_size_bytes`, `spacetraders_server_next_reset_timestamp_seconds`), `PrometheusMetricsService.cs` (the first sample) | health check (done) |
| B53 | **Every flight logs about a dozen lines** (found in the same health check). Handler after handler said at Information that the ship had left or arrived: the navigation command, the orbit, "in transit", the scheduler's wake-up, "arrived; dispatching", "arrived at", the market and shipyard refresh, the dock, "docked; navigation complete", "navigation complete; resuming goal" and the goal's outcome, about 12 lines a hop. With twelve ships, flights were about 80% of the bot's 2,850 lines an hour (about 6,000 a ship a day), and the log budget of 50,000 a day (2.5) would have been passed that night on normal running. Now a flight logs one line when it leaves and one when it lands, besides its refuel. Replaying the day's logs without the others gives about 2,300 lines a ship a day, as the soak test measured for a mining drone (1.9); the budget itself becomes a number per ship in gembernodes. | `NavigateToWaypointCommand.cs`, `SubCommands/` (orbit, dock, navigate), `ShipArrivedEventHandler.cs`, `ShipNavigationCompletedHandler.cs`, `ShipEventScheduler.cs`; Loki, 2026-10-03 00:27–07:27Z (`--group`) | health check (done) |
| B54 | **The survey plan surveys where no miner will mine** (found in 6.10c's first watch, on the cluster). An asteroid counted for the surveys when one of the miners could reach it, one way. A ship in transit counts as at its destination, so when SPECTER-4 set off at 12:35:20Z on 2026-10-03 on its drift of 2 hours 25 to B7 (D45), the survey plan at once wanted surveys at B14 for B7's five ores, and at B37 for B7's gold, platinum and silver: B37 is 68 from B7, 136 there and back, more than a drone's tank, so no drone can mine it for B7. The command ship left its trading for B37 (12:35:51Z) and took two surveys there at 12:40:11Z that no drone could use, expiring 13:13Z and 13:26Z; B14's would have expired long before the drone got there. Before 6.10c no miner was ever at a far market, so reaching was enough. | `MiningPlanner.cs` (`SurveyTargets`), `SurveyPlanService.cs`; Loki, 12:35–12:41Z (`ShipSymbol="SPECTER-1"`); the survey plan's state at 12:35:49Z | 6.10c follow-up (fixed: an asteroid counts where a miner could mine it for the market, the mining plan's trip in CRUISE; a drone still drifting counts once it is there) |
| B55 | **`ShipLeftIdle` counts survey targets a surveyor can't reach** (found in 6.10c's first watch, on the cluster). The rule took every target that needed a survey for work waiting for any surveyor, but the plan gives a surveyor only targets it can reach. With 6.10b's designated surveyor (SPECTER-F, bought 12:50:29Z on 2026-10-03, an 80-unit tank) in the middle and the command ship mining far out with its 400-unit tank, the targets that needed a survey were at B14, B37, B8 and J72: SPECTER-F waited at XB5C from 13:05:29Z, by design, and at 13:15:33Z the rule raised `ShipLeftIdle` on it ("9 targets to survey"). The mining, siphon and trading branches of the rule read the ships each plan lists as able; the survey branch didn't. | `ShipLeftIdleRule.cs`, `SurveyPlanService.cs`, `SurveyPlanState.cs`; Loki, `EventKind="AnomalyRaised"` at 13:15:33Z; the survey plan's state at 13:16:44Z | 6.10c follow-up (fixed: the plan lists the surveyors that can reach each target, and the rule counts a target only for those) |
| B56 | **The survey ship's move never runs** (found in D54's first minutes, on the cluster). At 15:55:03Z on 2026-10-03, the survey plan gave SPECTER-F its move to B7 (D54: "moves to X1-DC53-B7, where 4 mining drones work and no other survey ship, against 3 in its own area"), and no step of it ran: `ShipGoalExecutorService` steps only the goal types it lists, and `MoveToWaypointGoal`, which had no executor before D54, wasn't one of them. The unit tests ran the new executor directly. SPECTER-F stayed at XB5C with a goal that never ended, so it wasn't free and the survey plan gave it nothing else either. | `ShipGoalExecutorService.cs`, `AutomationSwitches.cs`; Loki, `ShipSymbol="SPECTER-F"` from 15:55Z: the "moves to" line and nothing after it | 6.10c follow-up (fixed: the move is stepped, and belongs to the survey plan's switch; the plan's log line no longer renders its empty phrase as `""`) |
| B57 | **Sales are booked without their market and unit price** (found on 2026-10-03, while measuring how our trades move prices). All 1,336 `TradeSell` rows of the ledger, from the first on 2026-10-02 at 13:20Z, have no `WaypointSymbol` and no `UnitPrice`, while all 281 `TradeBuy` rows have both. `ShipCargoSoldEvent` carries the market (slice 6.10a, D50), and `LedgerEntryHandler` passed it to `spacetraders_goods_sold_units_total` but not to the row; its test asserted the row without them. | `LedgerEntryHandler.cs`; `ledger_entries` grouped by category and empty columns, 2026-10-03 17:33Z | 6.10c follow-up (fixed: a sale is booked with its market and unit price, as a purchase is; the rows already written stay without them) |
| B58 | **A survey ship flies to an asteroid it can't get away from, and back** (found on 2026-10-03, watching D54 on the cluster). A surveyor took any target it could reach one way (`MiningPlanner.CanReach`). The command ship's 400-unit tank makes B37's gold, platinum and silver targets for B7 (68 away; no drone's 80-unit tank gets there and back). SPECTER-F reached B7 at 18:40:10Z after its drift from the middle (D54), refuelled, and at once flew to B37 for gold, the best paid ore without a survey (as all were): it got there at 18:43:38Z with 12 of 80 fuel, and the nearest market that sells fuel is B7, 68 away. Its survey (no gold in it) done, the area rule found no drones within its CRUISE reach and drifted it back to B7 (18:43:49Z, arriving 19:15:33Z), where gold at B37 would again be the best paid ore without a survey: a loop of about 36 minutes, 32 of them drifting, while B14, where the drones mine for B7, gets no survey. D54 has the survey ship drift once, and survey from where the drones are. | `SurveyPlanService.cs`, `MiningPlanner.cs` (`CanReach`); Loki, `ShipSymbol="SPECTER-F"` from 18:40Z; the survey plan's state at 18:43:56Z (every target at B37 and B14 with 0 usable surveys, SPECTER-F the one candidate) | 6.10c follow-up (fixed: a surveyor takes a target only where it can get on, with the fuel left, to a market that sells fuel, `MiningPlanner.CanSurveyAt`; the state lists only those surveyors) |
| B59 | **The API's rate limiter answers 429 while the bot keeps to its budget** (found on 2026-10-03, on the cluster). `ApiThrottled` expects none (slice 1.10), and B13 left its reading of the guide's burst (30 more requests per 60 seconds on top of 2 a second) for the 429 counter to confirm. From about 10:30Z on 2026-10-03, as the fleet grew to 21 ships and its requests to about 0.6 a second, the limiter answered 429 7 to 13 times every three hours (about 1 in 500 requests), in bursts: four in 4 seconds at 19:07Z, six in 16 seconds at 19:14Z. Each came while the bot's own budget was in full use (writes waited 3.1 and 9.1 seconds in the two minutes around them), and each went through on its first retry, most after about 35 ms. `RepeatingError` was raised at 19:15Z, `ApiThrottled` at 19:27Z (11 in an hour). What the server counted isn't known: the warning gave the endpoint and the wait, not the limiter's headers. Step 1's headers (2026-10-03 19:51Z to 2026-10-04 13:00Z, 119 429s, more as the fleet grew: 4, 16, 29, 37 and 53 in successive 6 hours) all said `X-Ratelimit-Type=IP Address`, `Remaining=0`, a burst of 30 and 2 a second. Half named a reset a few to some tens of milliseconds away, and their retry after 35 to 80 ms went through: requests that reached the server a little early. 68 of 118 came within 20 seconds of the one before: a 429 made only its own request wait, and the requests after it went out into the empty budget. The four at 10:07:50Z on 2026-10-04 came during a start, whose budget began full while the server still counted the process before. | `RateLimitResponseHandler.cs`, `RequestBudget.cs`; Loki, `|= "429 from"` from 18:55Z; `spacetraders_api_throttled_total`, `spacetraders_api_rate_limit_wait_seconds_total`, `spacetraders_api_requests_total` | 6.10c follow-up (step 1, asked on 2026-10-03, "log headers first": each 429's warning carries the limiter's headers; step 2, fixed on 2026-10-04 from what they showed: each window is 100 ms longer for a request's journey, a 429 holds every request back until its reset, and a new process starts with its burst spent) |
| B60 | **The jump call sends the destination's system, not its gate** (found on 2026-10-04, building slice 6.11, from the API's spec). `POST my/ships/{ship}/jump` takes the jump gate to jump to, `{"waypointSymbol": …}` (API v2.3.0), and buys one ANTIMATTER at the gate's market; `SpaceTradersApiClient.JumpShipAsync` sent `{"systemSymbol": …}`. The port's jump-gate read turned each connection into its system, so nothing could have named the gate either. No plan jumped before 6.11, so neither ever ran. | `SpaceTradersApiClient.cs`, `SpaceTradersPortAdapter.cs` (`JumpShipAsync`, `GetJumpGateConnectionsAsync`); the API's spec, `jump-ship` and `get-jump-gate` | 6.11 (fixed: the jump sends the gate's `waypointSymbol`, which `JumpRequestTests` checks in the request's body; the connections stay gates; the result carries the antimatter's price and the credits after it, and a refused jump is a `JumpRefusedException`) |
| B61 | **A market seen for the first time can be stored twice at once** (found on 2026-10-04, on the cluster). An arrival at a market fetches it and stores it, and so does the market watch, each in a scope of its own. The watch counts a ship as there as soon as its arrival time has passed, while the arrival is still fetching, and a market never fetched is due at once. Each looked for the market's row first: for a market the bot had never fetched, both found none and both inserted one, and the second insert failed on `PK_cached_markets` (23505). That logged EF Core's Errors, which Grafana's error-log alert counts, and the watch's Warning; the watch's call was wasted and its prices dropped. It happened twice to SPECTER-1: at 12:46:05Z at X1-KR90-E18A, the last market of the system it had just jumped into (6.11), and at 13:07:23Z at X1-FJ91-A2, one of the first markets of the new agent's scout plan after the server reset. The first agent's scout ran before the market watch existed (6.5). Shipyards were stored the same way; two ships storing a shipyard never fetched at the same moment would have collided too. | `MarketRepository.cs`, `ShipyardRepository.cs` (`UpsertAsync`); `MarketWatchService.cs`, `NavigateToWaypointCommand.cs` (the arrival); Loki, the lines naming `PK_cached_markets` or `PK_cached_shipyards` over 30 days: 12:46:05Z and 13:07:23Z only | 6.11 follow-up (fixed: the market and shipyard caches store a row in one statement, `INSERT … ON CONFLICT … DO UPDATE`, so the later of two writers updates the row the first one inserted) |

### Decisions (2026-10-01)

Scope and strategy calls are yours; they are recorded here so nobody "fixes" them. New questions
get the next D-number.

| # | Question | Decision |
|---|---|---|
| D1 | Bootstrap stops once a contract plan is Completed or DeferredUnsupported (`ContractPlanService.cs:51-63`), so the bot never takes a second contract. | **Intended for now:** one contract per reset. Taking the next contract comes later. B9 still applies: after fulfilment the plan must complete and release the ship. |
| D2 | Non-mineral contracts are parked as unsupported (`ContractPlanService.cs:104-127`). | **Keep it simple:** they stay unsupported. Together with D1, a reset whose first contract isn't a mineral gets no contract. |
| D3 | Does the rate limiter follow the API's rules? | **It must follow the official guide; any difference is a bug** (B13, slice 1.10). |
| D4 | The probe plan waits for 200k credits (`ProbeDeploymentPlanService.cs:71`). | **Keep it for now;** tune once everything runs. **Replaced by D29.** |
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
| D16 | Slice 6.5: does the trading plan buy ships? | **Not for now** (2026-10-02): it trades with the ships it has, first the command ship after scouting, then the drone after its contract. Buying haulers needs a budget of its own; keep it in mind for later. **Replaced by D21.** |
| D17 | Slice 6.5: may cargo use `FleetExpansion.MinCreditReserve`? | **Yes** (2026-10-02): cargo turns back into credits when it is sold. Credits for the trip's fuel are kept back. |
| D18 | Slice 6.5: may two traders share a route? | **No, for now** (2026-10-02, "to keep everything simple"): a route, the good with its buy and sell market, that one trader holds isn't offered to another. |
| D19 | How do reads (GET: a market refresh) and writes (anything else: moving a ship, trading) share the API's rate limit? | **Writes first** (2026-10-02): "I'd rather have a POST to move a ship or trade goods than a market refresh that can be done 10 seconds later without penalty." A read gives way while a write waits for the budget, leaves the last 10 of the 30-request burst to writes, and stops giving way after 10 seconds, so reads can't starve. The market watch runs last in the tick, one market a tick. |
| D20 | Slice 6.4: the command ship can both survey (MOUNT_SURVEYOR_II) and mine (MOUNT_MINING_LASER_II). With surveying on, which does a ship that can do both do? | **Survey only** (2026-10-02): "Let's start with survey only. I think we'll end up with too many surveys, but we'll start simple and iterate." Its laser stays unused; the drones mine with its surveys. **Amended by D34** while the spare-time plan is on, and **by D38** while the role board is on. |
| D21 | Slice 6.4: mining comes before trading, so the command ship no longer trades. Which cargo ships does the trading plan buy, and how many? | **A light shuttle first, then up to 2 light haulers** (2026-10-02): "start with a light shuttle to get things going, then pick up, for now, up to 2 light haulers once funds become available." Only when a lucrative route waits and every trader has a trip, within the credit reserve. The list is the setting `Trade.ShipPurchases`. Replaces D16. |
| D22 | Slice 6.4: which supply counts as "low supply" for mining an ore to sell at that market? | **SCARCE and LIMITED** (2026-10-02). |
| D23 | Slice 6.4: how many ships may mine for the contract? | **Every free miner** (2026-10-02), the contract before market mining; ore left over is sold. The contract plan still buys at most one drone, and the mining plan buys none while the contract takes the miners. |
| D24 | Slice 6.4 (asked during the work): how do traders keep money for fuel? | **A trading bar** (2026-10-02): "a trading bar of, say 5.000 credits, under which only fuel can be bought", so the bot never holds expensive cargo without the fuel to move it. Cargo leaves `Trade.FuelReserveCredits` (5,000) untouched, on top of the trip's own fuel. |
| D25 | Slice 6.4 (asked during the work): when are prices fetched again after a trade? | **Right after each purchase or sale** (2026-10-02), "while the ship is still there". Cargo purchases and sales, by traders and miners; refuels aren't counted as purchases here (they happen at almost every departure and move only FUEL's price). |
| D26 | Slice 6.4's first watch (asked on 2026-10-02): when does a ship on the contract reconsider its work? | **After each round trip, and once at every restart:** "Any ship should probably have a release and re-assign after each mining round trip. Just to determine if there's something more important to do at that point", and it "explicitly reconsiders once whenever the pod restarts". A delivery closes the ship's contract assignment, and the plans assign it again on the next tick, in their order; at startup, every ship on the contract that isn't in flight is released. Mining, trading and survey trips already ended with each trip. |
| D27 | Slice 6.4's first watch (asked on 2026-10-02): how much does a surveyor survey? SPECTER-1 surveyed XB5C "for copper" 36 times in half an hour, and 27 surveys lay unused, because the contract's ore always came first. | **A small stock per ore:** "I'd expect him to make 1 copper ore survey and then move to the next ore type"; of the options, keep a stock of 2 usable surveys of each ore (`Survey.StockPerOre`), the contract's ore first, then the ore with the fewest. With the stock for every ore, the surveyor waits until one runs out. Refined the same day: "Stock per ore per asteroid. I'd like the surveys to be close to wherever the mineral can be sold": every market that buys an ore gets the reachable asteroid nearest it, each keeping its own stock. |
| D28 | Slice 6.4's first watch (asked on 2026-10-02): what do miners mine, and when is a drone bought? After the contract, the mining plan bought SPECTER-4 for A3's scarce silicon, and the drone mined surveyed iron for H51, where iron was MODERATE: the opening that paid for it stayed open, ready to pay for the next drone. | **One rule for both:** "The buying logic and the mining logic should follow the same rules. I do not mind if the buying logic buys a drone for silicon and the drone mines iron if they are both SCARCE. I do mind if the iron is not SCARCE, because at that point, endless drones are going to be bought." And: "Mine for scarce first, but once all ores are no longer SCARCE, keep mining for whatever the lowest supply ore is, even if it's not that profitable (as it will improve the amount and prices of higher-valued goods such as the metals that's made from the ores)." Miners serve the markets that buy an ore by supply, shortest first (SCARCE, LIMITED, then the lowest there is); within a level surveyed first, then value. A drone is bought, one a tick, only when its first trip by that ranking would be in low supply (SCARCE or LIMITED, D22). |
| D29 | Slice 6.3 (asked on 2026-10-02, "I'd like more scouting to be done"): how many probes, bought when, doing what? The probe plan (D4) never ran (D9): the starting probe sat at H52, and most markets' prices were hours old. | **A probe for every market, roaming until then:** "Long term goal: Each marketplace should have a sattelite"; probes "can be bought as long as the total credits doesn't dip below 100.000 credits (down from 200.000)"; "If not enough sattelites are available to cover the entire market, sattelites should drift between nearby markets (prioritizing markets that haven't been updated for a while)". Asked, as SHIP_PROBE isn't the cheapest ship in X1-DC53 (81,645 at A2, against 33,905 for a SHIP_SURVEYOR): **SHIP_PROBE**, which needs no fuel and which no other plan uses. The 100,000 is the credit reserve every purchase keeps (`FleetExpansion.MinCreditReserve`). Replaces D4. |
| D30 | Slice 6.3 (asked on 2026-10-02): the API sells a ship only where one of our ships is. SPECTER-2, parked at H52 since the start, is what let the plans buy drones there (SPECTER-4 at 15:49Z); roaming probes leave the shipyards. | **Fetch a probe:** when a plan can afford a ship at a shipyard where none of our ships is, the nearest free probe flies there and waits until the purchase is made, then roams on. The purchase makes no API call that could only fail. |
| D31 | Slice 6.7 (asked on 2026-10-02): miners work an ore contract before anything else (D23), but gas contracts (HYDROCARBON, LIQUID_HYDROGEN, LIQUID_NITROGEN) are parked as unsupported (D2). Should siphons take them? | **Keep D2 for now:** siphons only siphon and sell to the markets. With one contract per reset (D1) a gas contract rarely comes up; it can be a slice of its own. |
| D32 | Slice 6.7 (asked on 2026-10-02): when does the siphon plan buy siphon drones? | **The miners' rule (D28), with a cap of their own:** one a tick, only when its first trip would serve a market where the gas is SCARCE or LIMITED, within the credit reserve, up to `Siphon.MaxDrones`, "max 10 default". |
| D33 | Slice 6.7 (asked on 2026-10-02): miners jettison every ore but their trip's (6.4, Noticed). A gas giant can't be surveyed, so a siphon drone would jettison about two siphons in three. Do siphons do the same? | **Keep every gas:** a trip keeps every gas it siphons, which fills the hold about three times faster; it sells its own gas at its market, and the plan sells the others on the following trips. |
| D34 | Slice 6.8 (asked on 2026-10-02): "I'd like my command ship not to be idle. So can we add a interuptable mining/siphoning task that just fills up the cargo with whatever and sells it where it's relevant. If a more important job comes up such as trading or surveying it should stop mining, sell it's inventory and start on the new job." With the survey plan on, the command ship never trades (D20): should it trade when it has nothing to survey? | **Survey, then trade, then mine** (2026-10-02): with nothing to survey it takes a lucrative route that waits for it (`Trade.MinProfitPerUnit` after fuel), after the other traders; it mines or siphons only when there is neither, and a route that turns up interrupts that, its hold sold first. Amends D20 while the spare-time plan is on; with it off, D20 holds, so the switch brings all of slice 6.8 (D9). **Amended by D38** while the role board is on. |
| D35 | Slice 6.8 (asked on 2026-10-02): where does the command ship mine or siphon? It has a mining laser, a gas siphon and a 40-unit hold; XB5C, where it surveys, is 19 from H51, and the only gas giant, C38, about 170 away. | **The nearest source:** the nearest asteroid or gas giant it can work, that it can reach and that yields a good a market it can carry it to buys: it stays near its surveys, so an interruption costs little. Without surveys, which stay for the drones. |
| D36 | Slice 6.8 (asked on 2026-10-02): "sells it where it's relevant": which market does each good go to? | **Best price after fuel:** each good where it fetches most after the fuel to get there, the rule miners, siphoners and traders sell cargo they hold by. **Amended by D42**: a good no market buys, or whose sale doesn't pay for the fuel, goes overboard. |
| D37 | Slice 6.8 (asked on 2026-10-02): a survey usually comes up at the asteroid the command ship mines at. Sell first, as asked for D34, or survey straight away? Surveying needs no room in the hold. | **Survey first:** it surveys on the spot with its hold aboard, then carries on filling, and sells once full. Only a trade makes it sell first, as a trade needs the room. |
| D38 | Slice 6.9 (asked on 2026-10-02): "Each ship should have a set of potential roles. A ship with a mining laser can have the mining role. A ship with a surveyor can have the surveyor role. A ship with a cargo hold and fuel can have the trader role. Etcetera. A ship should occasionally consider whether it's role is still the best thing it can do. This is not only based on it's own potential roles but also of other ships." | **Roles by what earns the fleet most, surveys first:** "If there is only 1 ship that can survey, then that ship should prioritize surveying. But if there are 2 ships that can survey, but one of them can only survey and the other can survey, mine, trade and siphon, the ship that can only survey should take the job. Goal is to have every ship be the most profitable it can be by comparing each role it has with the potential profits it can make." Each role is valued per hour by the trips its plan would offer; the work is shared for the most per hour across the fleet. Amends D20 and D34 while the role board is on. |
| D39 | Slice 6.9 (asked on 2026-10-02): "Some profits are not direct: mining iron ore isn't that profitable by itself, but iron ore gettting refined to iron getting made to machinery is very profitable." How does that count when roles are compared? | **A share of the next steps:** of the options, "selling a good to a market that makes something pricier from it adds a share (setting, default 50%) of the price difference, and that share again for the step after (iron ore → iron → machinery). Full while the market is SCARCE of the good, less as its supply grows, nothing at ABUNDANT." (`Roles.ChainValueSharePercent`) **Amended by D49:** at most what the trip earns on a unit. |
| D40 | Slice 6.9 (asked on 2026-10-02): should contract work stay first for every ship that can mine (D23), or compete on profit? | **Contract first:** "Every ship that can mine, except the survey-role holder, joins the contract while units remain, as now. Roles are compared for the rest of the reset, so a contract can't stall because drones found trading more profitable." D23 kept. |
| D41 | Slice 6.9 (asked on 2026-10-02): how often does a ship reconsider its role? | **Every 10 minutes, with a head start:** "The whole fleet is re-evaluated every 10 minutes, and at once for a ship whose role has no work for it. A ship's current role gets a 20% head start in the comparison, so close calls don't flip back and forth. Both numbers become settings." (`Roles.ReconsiderMinutes`, `Roles.HeadStartPercent`) A new role takes effect when the ship's trip ends. |
| D42 | Slice 6.9 (asked on 2026-10-02, during the work): what happens to cargo nothing will sell? Surveyors carried theirs for good (6.4's Noticed), and traders kept what didn't pay for its fuel. | **Sell it, or jettison it:** "if a ship's cargo hold isn't empty and the goods aren't going to be sold or earmarked for another reason, the ship should either go to a waypoint to sell it or, if that's not profitable, jettison it." The contract's ore on a ship that mines for the contract is earmarked; so is a spare-time hold, which the next trip fills on (D37), unless no market it can reach buys it. |
| D43 | Slice 6.10 (asked on 2026-10-03): "I feel there are too many siphoning drones and not enough other ship types." Each plan buys on its own: drones (~50k) are affordable at ~150k credits, so a light shuttle (114k, needs 214k with the reserve) or a probe (77k, 177k) never was. In what order are ships bought? | **A fixed order** (slice 6.10b): first a designated surveyor (D47); then "at least 1 drone per mineral that is scarce or limited" (D48); "then save up for cargo ships": while a ship in `Trade.ShipPurchases` is still to buy, no probes and no other drones are bought; then probes until every market has one (D29); then "alternate drones and cargo ships": a drone by today's rules (D28, D32), then one more cargo ship of the list's last type, and so on, while minerals stay at or below LIMITED. The contract's drone stays first (D23, D40). |
| D44 | Slice 6.10 (asked on 2026-10-03): "I feel like there should be more profitable trades." In a day, 102 of 176 trades were taken because they feed production (D15), at a median 538 credits against 3,386 for the others; with `Trade.MinProfitPerUnit` at 5 almost any feeding route wins. Change D15? | **Keep D15:** "I feel like there aren't many trades to begin with. I'm not too worried about trades that don't have a lot of profit, as they should feed into trades that are more profitable, and make ships more affordable (by making more of them so the availability becomes better)." Only the command ship trades; more trades come from cargo ships (D43) and from the command ship once a surveyor takes over (D47). |
| D45 | Slice 6.10 (asked on 2026-10-03): "I'd like a way to add mining/siphoning drones for the minerals outside of fuel range, e.g. by having a drone drift to the marketplace that buys the mineral first, then refueling and resuming normal behavior." Which drones drift where? | **New and free drones, near first** (slice 6.10c): a target out of a drone's CRUISE reach counts when its asteroid is within a CRUISE round trip of the market that buys the ore; it ranks after every reachable target of the same supply level (D28). The drone drifts there once (1 fuel, about ten times slower), refuels, switches back to CRUISE and mines from that market. |
| D46 | Slice 6.10 (asked on 2026-10-03): "I'd like to see the actual trade profits, so the actual sell − buy − fuel … offset in the same graph by other profit sources such as mining profits (− fuel) and contract profits (preferably − fuel)." Book profit per transaction or per trip? | **Per trip, at its end** (slice 6.10a): a trade, mining, siphon or spare-time trip books its sales − purchases − fuel when it ends, with a journal line; contracts book their payments, and the fuel their ships bought on contract work. No dips from a purchase and its sale landing in different hours. |
| D47 | Slice 6.10 (asked on 2026-10-03): "I'd like to add the purchase of a designated surveyor ship, so that the COMMAND ship is freed up to use it's considerable cargo for trading and mining." Where in the order, and what does the command ship do then? | **First in the order** (slice 6.10b): one SHIP_SURVEYOR (33,905) for each system with miners, while the survey plan and the role board are on; the board gives it the survey role (D38, a ship that can only survey). The command ship then gets the role the board finds most profitable (D38), once its rates are fixed (D49). |
| D48 | Slice 6.10 (asked on 2026-10-03): buying "one drone per scarce mineral" ends only if those drones work on those minerals; otherwise each new drone takes the best-paying opening. | **Uncovered minerals first** (slice 6.10b): a free drone first takes a SCARCE or LIMITED mineral that no drone works on, near before far, then D28's order; the role board keeps one drone in its gathering role for each such mineral, as the contract keeps its miners (D40). A mineral no drone can reach, or that no asteroid yields, doesn't count. |
| D49 | Slice 6.10 (asked on 2026-10-03): the board valued a siphon drone at ~274,000 credits an hour (it earns 7–10k), mining drones at 80–95k (1–3k), the command ship's trades at up to 4.7M (13k): D39's second step counts, for every unit of gas, a quarter of a price difference such as PLASTICS → EQUIPMENT (~3,200), and a siphon trip's gases were valued at the best market anywhere. How are the rates fixed? | **Cap the chain value, at the trip's own market** (slice 6.10a), "for now"; amends D39: goods are valued where the trip sells them, and the chain's share (both D39 steps kept) is capped so that feeding a factory at most doubles what a trip earns per unit: its price for mined and siphoned goods, its margin for trades. |
| D50 | Slice 6.10 (asked on 2026-10-03): "we should visualize the actual correlation between the amount of goods sold and the amount of processed goods added, and what that does to the price. Because a unit of hydrocarbons might make a unit of plastics, but we don't know how many … (we also don't know at what rate a hydrocarbon gets converted to plastics per unit of time)." | **Measure it** (slice 6.10a): the bot counts the units it sells and buys per market and good; the dashboard plots what we sell into a market per hour against the supply, trade volume and price of what that market makes from it, so the rate and the delay can be read off. D49's cap stands until those numbers say better. |
| D51 | Slice 6.10b (asked on 2026-10-03, in another session): "What if we made the amount of credits for trade wider based on the amount of cargo total in the fleet? Something like: 60.000 hard minimum, 1.000 per cargo hold. … This might ramp it up a bit too much. Which defaults would you recommend?" In 24 hours the command ship made 150 trade purchases, 51,610 credits on average (median 54,331, 27 units): an expensive good's trade volume (often 20 units) caps a load, so a trip's cost barely grows with the hold; the drones' few trades cost 45–67k. Counting every hold (11 drones and the command ship, 205 units) would ask 265,000, above the ~150,000 the credits peak at, and freeze every purchase. | **Grow the reserve with the trading holds:** the credits every ship purchase keeps are `FleetExpansion.MinCreditReserve` (the floor, seeded at 60,000) plus `FleetExpansion.ReservePerTradingCargoUnit` (1,000) for every unit of hold on the ships that trade: the cargo ships, the command ship (it trades whenever it isn't surveying, D34, D38) and any other ship the role board has in the trade role. Drones that gather, probes and surveyors buy no cargo. The command ship alone keeps 100,000 (as today); with a light shuttle (40) 140,000, so the first light hauler needs 354,210 + 140,000; with one light hauler (80) 220,000, with two 300,000; a drone in the trade role adds 15,000 while it has it; no ship that trades, 60,000. The recommended defaults, yours unchanged. |
| D52 | Slice 6.10c's first watch (2026-10-03): with every ore it reached at its stock of surveys (D27), the designated surveyor SPECTER-F (D47), which can do nothing else, waited at XB5C (and B55's false anomaly fired on it). | **Survey on:** "A (single role) surveyor which is idle is allowed to keep surveying, starting with whichever ore is lowest." A ship that can only survey, once every ore it reaches has its stock, surveys the target it reaches with the fewest usable surveys, then the contract's, then the best paid. The command ship, which can do more, still waits, or trades and mines in its spare time (D27, D34). Amends D27 for ships that can only survey. ("Lowest" read as the fewest usable surveys, D27's own order.) |
| D53 | Slice 6.10c's watch (2026-10-03): coverage (D48) counted a mineral for the whole system. With SPECTER-10 bound for B7's silicon, silicon counted as covered: the middle's SCARCE silicon (H53) had no drone, and by 14:01Z four of the five mining drones were drifting to B7 (SPECTER-4, -10, -A and -9), with SPECTER-3 alone in the middle. How does coverage treat far markets? | **Cover per area:** "A drone covers a mineral only for the markets it can reach in CRUISE from where it works (the middle, or B7). The middle's scarce silicon gets a drone of its own; the coverage tier may buy a drone per scarce mineral per area (more drones)." A trip covers its mineral at the markets its ship reaches in CRUISE, through refuelling stops, from the market it sells at; the coverage tier counts each SCARCE or LIMITED mineral once per area (the markets a drone flies between in CRUISE: in X1-DC53 the middle and B7); the role board keeps one drone per mineral and area. Amends D48. |
| D54 | Slice 6.10c's watch (2026-10-03): from 15:02Z SPECTER-4 mined B14 for B7 without surveys (copper on about one extraction in six), with three more drones drifting there, while the survey ship SPECTER-F, whose 80-unit tank keeps it in the middle, surveyed XB5C for SPECTER-3 alone; the command ship doesn't survey while a ship that can only survey exists (D38). Asked: "Please add the option for the survey ship to get to the mining location without surveys." A drift between the middle and B7 takes about 2.5 hours and a survey lasts 10 to 55 minutes, so one survey ship serves one area at a time: where should it work? | **Where most drones mine:** a ship that can only survey works in the area where the most mining drones work (their trip's market, a drone drifting there included; between trips, where they are), and drifts once to the market of another area that has more drones than its own; a tie keeps it where it is. The command ship never moves for this. |
| D55 | After D54 (2026-10-03): one survey ship serves one area at a time, so the area with fewer drones mines without surveys (at 15:45Z three drones in the middle, four for B7). Asked: "Can we add that extra surveyor drones are bought to try and cover all areas with surveys? The second surveyor is lower priority than the first on the buy order." Where in the order? | **A survey ship per area, after the coverage drones:** while a system has fewer ships that can only survey than areas with mining drones (as the survey ships fly between them), one more is bought, after the drones per scarce mineral and area (D48, D53) and before the cargo ships; the first stays second in the order (D47). Each area with drones gets a survey ship of its own: one already there or on its way takes it, and of two in one area, one drifts to an area with drones that has none. Amends D54. |
| D56 | Slice 6.10 (asked on 2026-10-03): "Can we add the rule that only full cargo holds can be traded? As the price changes after the buy, it's much more effective if 40 units are bought compared to 6 or 7." A market trades at most its trade volume at once, and each trade moves its price: on 2026-10-03 a purchase raised it 4% (under half the trade volume), 7% (half or more) or 9% (all of it), a sale lowered it 1 to 3%; a unit costs the price quoted for its purchase. The drones bought SHIP_PARTS 6 or 7 at a time at D41 (15 at once) and sold them at C39 and H52 (7 and 6 at once); a trip took as many units as the free hold, both trade volumes and the credits allowed, in one purchase. How should a trade fill a hold, and with what credits? | **Full hold or nothing, in one purchase and one sale:** "So I'd suggest waiting for the market trade volume to be at max cargo capacity, and only then buy all of it at once. And especially mining drones can mine while this is not the case. The entire goal is to buy full holds in one go, because it makes no sense to buy more times than one." A route counts only when both markets' trade volumes are at least the ship's free hold and the credits pay for all of it (the trip's fuel and `Trade.FuelReserveCredits` kept back, D24); otherwise the ship takes other work, and drones keep trading when a full hold is there (D37's spare time). The credits: "Full hold or nothing, when this occurs the credit floor should be temporarily expanded so any ship purchases wait for the full hold to be bought before new ships are bought." While a trader's best route is a full hold the credits don't pay for yet, the credit reserve every ship purchase keeps (D51) grows by the dearest such hold, until it is bought. Amends D51; replaces "as many units as the credits allow". |
| D57 | After D56 (2026-10-03): traders share one pot of credits, and nothing held back the credits of a trip already on its way to buy. At 19:29Z SPECTER-8 set off to buy 15 EQUIPMENT (49,485) at K85; by the time it got there another trader had spent about 121,000, leaving 54,596, too little once the fuel reserve was kept back, and it dropped the trip as `not_possible` with nothing bought (before D56 it would have bought what the credits paid for). | **Hold the credits back from the start:** "Let's have these credits reserved as soon as a ship starts towards it, so that this cannot happen (waste of time and fuel)." A trip holds back what its cargo costs at the price it was chosen with, from the moment it starts until the cargo is aboard: the other traders get only the credits no trip holds back, the trip at its buy market spends its own, and ship purchases leave them, as they leave a saving (D56); a trader that sets off for the hold it saved up for saves up no more, as the trip's hold takes its place. A price that rose meanwhile is paid from the credits no trip holds back. |
| D58 | After D57 (2026-10-03): asked why a drone (SPECTER-15, 18:52Z) was bought while the light shuttle waited, we found the role board moving drones between gathering and trading every 10 minutes for profit (SPECTER-3: Mine to Trade at 19:33Z, back at 19:51Z, to Trade at 20:01Z; siphon drones the same). The ores and gases they no longer gathered went short, and the coverage tier (D48, D53) bought drones for them: SPECTER-15's first trip was the middle's copper, "uncovered". Asked: "I feel it's wrong if the drone buying system feels like there are not enough mining drones, but the mining drones themselves are trading. Mining drones should be mining drones first, and traders second, and they should not leave gaps when trading in a way that results in endless drones being bought." | **Drones gather first, mining and siphon drones alike:** the role board gives every drone (it can mine or siphon, and trade, and nothing else) its gathering role, whatever trading would pay; a drone trades only when its plan has no trip for it, as it already could, so its trading leaves no mineral without a drone. The command ship, which can survey, still takes what pays it most (D38). Amends D38's "most profitable" for drones. |
| D59 | Slice 6.11 (asked on 2026-10-04): "if an active jump gate goes to a system that isn't explored yet, the COMMAND ship should go through that jump gate. If there are markets or shipyard there, the COMMAND ship should scout them, as it initially does for the home system, recursively." How far does it go? X1-DC53's gate connects to four systems that day: X1-KR90 and X1-MT49, built, and X1-HZ59 and X1-BG54, still under construction; a walk through the gates' connections passed 60 systems within 7 jumps. | **No limit:** "No limit. Most likely a shipyard will be found with dedicated explorer type ships that can take over from the command ship eventually." The command ship explores every system the built gates reach, the nearest by jumps first. |
| D60 | Slice 6.11: what does the command ship do once nothing is left to explore, and do the other plans work in the systems it finds? | **Come home, for now:** "For now: come home. Long term: plans should just work across systems, keeping in mind antimatter fuel costs etc. But let's start simple and expand once we understand what's out there a bit better." It jumps home, where the other plans give it work again. Meanwhile they do business only in the systems where a ship that doesn't explore is (home); what the command ship finds elsewhere is for the systems dashboard. |
| D61 | Slice 6.11: when does exploring take the command ship from its work (surveying, trading, spare time)? | **When its current trip ends** (the recommended option): the plan takes it once it is free (no goal, no assignment, not in transit), before the role board and every plan after it. |
| D62 | Slice 6.11: an uncharted waypoint keeps its traits hidden (an asteroid's deposits among them), and the command ship could chart the waypoints it visits (`POST my/ships/{ship}/chart`). Chart them? | **Not for now:** "Skip them for now, charting might be interesting later but simple first, expansion later." |
| D63 | Slice 6.11: each jump buys one ANTIMATTER at the gate's market. What credits may a jump spend? | **Keep the 60,000 credit floor** (the recommended option): a jump goes only while the credits after it stay at or above `FleetExpansion.MinCreditReserve` (seeded at 60,000), the floor every ship purchase keeps (D51); until then the plan holds the jump. |
| D64 | Slice 6.6 (asked on 2026-10-04): "Also please check whether the construction command gives any money, if so the new role can be set up like a trader, if not it probably needs a different money cap." It gives none: the API's supply call answers with the construction site and the ship's cargo, without credits. Where does a load of materials stand against the other purchases? | **Judged like a ship purchase, after the cargo ships:** a load keeps the credit reserve (D51, with the saving of D56) and what the trips on their way to buy hold back (D57), and comes after the cargo ships in the order ships are bought in (D43): the contract's drone, the surveyors, a drone per scarce mineral and area, and the cargo ships of `Trade.ShipPurchases` go first; the probes and the further drones and cargo ships wait while the gate has a load to buy. A construction trip holds back its cargo from the moment it starts until it buys, as a trade trip does (D57). |
| D65 | Slice 6.6: "Can you implement a special role that works on this jump gate?" Which ships take the role, and what do they do when there is nothing to buy? | **One: the largest hold:** the ship with the largest hold that isn't a drone or the surveyor builds (`Construction.Ships`, 1, for more); of equal holds the one that builds now, then the one that can do least else. It trades while construction has nothing it may buy. Supplying pays nothing, so no estimate chooses it: it goes first to the largest hold, as surveys go first (D38). |
| D66 | Slice 6.6: each purchase raises a market's price, and a market short of a material asks more for it. Buy at any supply? | **Not at low supply:** no purchase where the material's supply is SCARCE or LIMITED; the builder waits (and trades) until a market is back at MODERATE or better. |
| D67 | Slice 6.6: one purchase per trip, or several? Then asked: "If the markets trade volume is smaller than a haulers hold, it should wait until the trade volume is a haulers hold." | **A full hold in one purchase:** a load is the builder's free hold, or what the gate still needs when that is less, bought at once, and only where the market's trade volume takes all of it; otherwise the builder waits (and trades), while the purchases after construction in the order keep waiting for it. |
| D68 | Slice 6.6 (asked on 2026-10-04): "Only the home base jump gate construction should be high priority, any other jump gate construction should be low priority or maybe not even considered at all." | **Only the home gate:** the plan builds only the jump gate of the headquarters' system; a gate elsewhere is never fetched, built or given a role. |
| D69 | Slice 2.12 (asked on 2026-10-04, after the server reset of 13:00Z left the new agent with only the scout and contract plans on): "After the restart most config items were turned off. Can you ensure everything is on by default? And changes in config changes those defaults?" Asked what the second part should do, you chose to have your changes remembered apart from the agent, the bot's own switch-offs left out, and a setting nobody changed follow the default. Then: "In addition, id like to have a separate set of endpoints to only affect future runs. So I can have a setting for this run (e.g. 50% split between miners and traders) and change those settings for the next run to see if it gives an improvement." | **Every plan on by default, and settings for the next runs:** every plan switch defaults to on, which replaces D9. The next run is the agent the next server reset registers; it starts with the value chosen for each setting, else the default. `PUT /settings/{key}` and the kill switch set a setting now and for the next runs; `PUT /settings/next-run/{key}` only for the next runs, `DELETE` gives them the default back and `GET /settings/next-run` lists them. When the bot switches automation off itself (the size guard, the reset monitor), the next runs keep their value. A setting nobody has set follows its default, also on the agent that runs. `POST /settings/reset` forgets the values chosen for the next runs. |

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

**2.9 Which settings exist, and which are on** (built 2026-10-02 on branch `ccr-212dac2b-p2ent0`, in projects
and gembernodes; asked that day)
- Asked: "Can you work on an extra panel in grafana with which configuration items exist, and which of
  them are turned on?"
- Done:
  - Every 10 seconds the bot exports its settings, one series per setting, always 1:
    `spacetraders_setting_info{setting,current,description}`, the value as stored and what the setting
    does. Grafana reads only Prometheus and Loki (phase 2), so the table needs a metric.
  - A value that may hold a secret shows `(hidden)`, by the rule `SettingChanged` follows
    (`SettingsRepository.Shown`, now shared). Today that is only `Alerts.WebhookUrl`, which is empty.
  - What a setting does comes from the running version's seed (`DefaultSettingsSeed.DescriptionOf`): a
    stored setting keeps the description it was seeded with, and the cluster's agent was registered
    before 6.3 to 6.5 rewrote the probe, mining and trading plans' descriptions. A key the seed doesn't
    hold shows its stored description (empty for one only `PUT /settings/{key}` wrote).
  - The dashboard's side is gembernodes (branch `ccr-212dac2b-p2ent0`): a **Settings** table after the
    database size and the anomalies. The switches come first (every setting whose value is `true` or
    `false`), on in green, off in plain text; then the other settings by name, with their values and
    what they do. The `Runtime.*` status flags are left out (status flags, not settings to tune, B18);
    the metric has them. The blue "Setting changes" annotations already say when each one changed.
  - Tests: `PrometheusMetricsServiceTests` (every setting, the secret hidden, the running version's
    description, only the agent's own), `PrometheusAutomationMetricsTests` (one series per setting as
    its value changes; gone with the setting) and `MetricsEndpointTests` (the scrape lists it). The
    table was checked in a browser, in Grafana 11.6.1 (what the chart's newest 8.x ships) against a
    local Prometheus fed the metric as the bot writes it: 34 rows, the 9 switches first, the empty
    webhook URL as `(empty)`, no `Runtime.*` row.
- Noticed (not changed):
  - The startup settings dump (`SettingsSnapshotLogger`: "Setting {Key} = {Value} …") logs every value as
    stored, `Alerts.WebhookUrl` included, where `SettingChanged` hides it. The URL is empty, so nothing
    has leaked; hiding it there too would take one line.
  - A change reaches the table within about a minute: the 10-second sample, then Prometheus's scrape.

**2.10 The API's request rates, and legends as tables** (built 2026-10-03 on branch `ccr-ff72b415-uqqj4q`, in projects
and gembernodes; asked that day)
- Asked: "For spacetraders, can we add a graph similar to this?", with a screenshot of a graph of requests initiated,
  executed and completed, and rate limited, per second, with each one's minimum, maximum, mean and last in a table under
  it. Then: "In addition, can we change the legends to table view where appropriate".
- Done:
  - A new counter, `spacetraders_api_requests_initiated_total{method,endpoint}`, from a new outermost handler
    (`ApiRequestInitiatedHandler`): every request the bot initiates, once, as it starts, before the pause after a 502
    and the local budget. Nothing counted a request before it went out, so "initiated" needed it; the other three lines
    come from the counters the bot already had.
  - The dashboard's side is gembernodes (same branch): an **API request rates** graph, full width, above the other API
    panels: initiated; executed, each request that went out, retries included; completed, each that got an answer; and
    rate limited, the 429s. Per second over 5 minutes, in the screenshot's colours, with a table of min, max, mean and
    last. Initiated above executed: requests wait for the budget, or the pause refuses them; executed above initiated:
    retries.
  - Every graph with a list legend on both SpaceTraders dashboards has a table under it: mean, max and last for rates
    and counts per minute or hour; min, max and last for levels (credits, ships, usable surveys, the database size,
    prices, supply); total, mean and max for the hourly bars of what we sold into a market. Graphs with many series sort
    by mean (levels by last), so the biggest come first. The graphs with more series grew by two rows, so their tables
    show three or four lines before they scroll; the panels below moved down.
  - Tests: `ApiRequestInitiatedHandlerTests` (counted by method and template before the request goes on),
    `ApiRequestPipelineMetricsTests` (through the client's own handlers: a request retried after a 429 is initiated
    once and executed twice; one the pause refuses is initiated and never executed), `PrometheusAutomationMetricsTests`
    and `MetricsEndpointTests` (the scrape lists it, and its first increment reaches Prometheus). The graph's four
    queries with `promtool test rules` against synthetic series, and both dashboards in Grafana 11.6.1 against a local
    Prometheus holding a day of synthetic data, at desktop and phone width.
- Noticed (not changed):
  - On a phone, a table wider than its graph scrolls sideways: long names, such as the endpoints, push the numbers off
    to the right, as in the screenshot the graph was asked from.
  - "429s, failed calls and rate-limit waits" draws a straight line across hours where a series is missing: a new pod
    has no `status 429` series until its first 429, and the panel joins the gap (`spanNulls`). In the screenshot of
    2026-10-03 23:11 (local time) it climbs from 0 to about 4 between about 18:10 and 21:20 local time, the hours
    between the deploy at 16:07Z and the 429s at 19:07Z. The new graph joins gaps of up to 10 minutes only (a restart),
    and reads 0 for rate limited while the bot runs without a 429 series.

**2.11 What each ship for sale holds, could do and carries** (done: merged 2026-10-04 as projects#154 and gembernodes#52,
which deployed it; built on branch `ccr-1e461fef-n6hydk`; asked that day)
- Asked: "For spacetraders, can we add some more information to the shipyard ships? I'd like to know fuel tank size,
  cargo size, and which special bits they have (e.g. mining laser)", in Grafana, then "Also which role they can fulfill
  within my fleet".
- Done:
  - With the markets and shipyards, every minute (`PrometheusMarketMetricsService`), the bot exports for each ship type
    a shipyard lists in full, once a ship has been there: its tank, the frame's `fuelCapacity`
    (`spacetraders_shipyard_ship_fuel_capacity_units`); its hold, what its cargo holds take together
    (`spacetraders_shipyard_ship_cargo_capacity_units`); and one series with what it could do in the fleet and its
    equipment (`spacetraders_shipyard_ship_info{system,waypoint,ship_type,can,equipment}`). The cached listings hold the
    frame, modules and mounts; slice 6.4 read the tank and hold from them for purchases, and the mounts and modules are
    read now too (`ShipyardShipDto.Mounts`, `.Modules`).
  - `can` is judged as the fleet table's "can do" judges a ship (`FleetRoles.PotentialRoles`), on the ship as the
    plans see one they would buy: by its type, mounts, hold and tank, whichever plans are on. `Survey`, `Mine`, `Siphon`
    and `Trade` in that order, such as `Mine, Trade` for a mining drone; `none` for a ship that can do none of them. A
    probe says `Probe`, where the fleet table says `none`: the probe plan buys and flies it.
  - `equipment` is its mounts, then its modules, each in symbol order and without its `MOUNT_` or `MODULE_` prefix,
    such as `MINING_LASER_I, MINERAL_PROCESSOR_I`. The cargo holds are left out, as the cargo column has them, and so
    are the crew quarters, which house the crew (the command frigate has two); `none` without any.
  - The dashboard's side is gembernodes (same branch): the markets dashboard's **Shipyards** table gets the columns
    "can do", "fuel", "cargo" and "equipment", and is full width under "Places", which is too; the panels below moved
    down.
  - `ShipyardWaypointDto` lists its ship types and ships as read-only lists, which removes the two QW0012 warnings in
    `ApplicationDtos.cs`. The file's QW0028 and QW0029 warnings (strongly typed identifiers) stay, as across the code.
  - Tests: `ShipyardShipCapacityTests` (a listing's mounts and modules), `PrometheusMarketMetricsServiceTests` (tank,
    hold, can do and equipment of a mining drone, siphon drone, surveyor, light hauler, probe and command frigate as
    listed), `PrometheusAutomationMetricsTests` (the series; one info series per ship type that follows the listing,
    gone with the details or the type) and `MetricsEndpointTests` (the scrape lists them). The panel's queries with
    `promtool test rules` against synthetic series in two systems, and the dashboard in Grafana 11.6.1 against a local
    Prometheus scraping the series as the bot writes them, at desktop and phone width.
- Noticed (not changed):
  - Slice 6.7 noted that whether a siphon drone has a gas processor "isn't known here (the shipyard listing the bot
    caches has no modules)". The cached listings do hold the modules (A2's light hauler, with its two holds and crew
    quarters, on 2026-10-02), so the shipyards table's equipment column answers it whenever the cache holds C39's
    listing in full.

**2.12 The next run's settings, and every plan on by default** (done: merged 2026-10-04 as projects#156 and gembernodes#55,
which deployed it at 18:09Z; built on branch `ccr-856636cc-qj1te0`; asked that day, D69)
- Asked, after the server reset of 2026-10-04 13:00Z registered a new agent with the seeded settings (only the scout and
  contract plans on, D9): "After the restart most config items were turned off. Can you ensure everything is on by
  default? And changes in config changes those defaults?" Then: "In addition, id like to have a separate set of endpoints
  to only affect future runs. So I can have a setting for this run (e.g. 50% split between miners and traders) and change
  those settings for the next run to see if it gives an improvement."
- A run here is one agent, from one server reset to the next. `RunLifecycleService`'s runs also start when a strategy
  setting changes; a value chosen for the next run only reaches the agent the next reset registers.
- Done:
  - Every plan switch defaults to on (`DefaultSettingsSeed`).
  - A setting follows its default while nobody has set it (`agent_settings."FollowsDefault"`): every start gives it the
    default as the running version has it, a `SettingChanged` journal line each. Set through `SettingsRepository`, by you
    or by the bot (the size guard, the reset monitor), a setting keeps its value. The `Runtime.*` status flags never
    follow.
  - The settings stored before are judged once, by the start that adds the column, in one transaction with it: one whose
    value isn't its default was set and keeps it (a setting you change, or automation you switch off, before the deploy);
    the rest follow, with the plan switches that are off, whose defaults D69 switched on. So the first start of this build
    switches on the plans the agent of 13:00Z has off, and keeps what was changed before it.
  - The values the next runs start with live in `next_run_settings`, which has no agent, so the agent cleanup leaves it.
    `PUT /settings/{key}` and the kill switch set them too; `PUT /settings/next-run/{key}` sets one alone, `DELETE` forgets
    it, `GET /settings/next-run` lists what the next run starts with, and a key the seed doesn't hold or a status flag is
    not found (404). A new agent is seeded from them, else from the defaults. `POST /settings/reset` forgets them. Each
    change is a `NextRunSettingChanged` journal line.
  - The cluster's database gets the column and the table at the first start: `SpaceTradersDatabaseInitializer.AddedSchema`,
    renamed from `AddedColumns` as it now adds a table too.
  - `spacetraders_setting_info` gets the label `next_run`. The dashboard's side is gembernodes (same branch): the Settings
    table gets a "next run" column with the same on and off as "value", both 180 px wide so they fit side by side on a
    phone, and its description the endpoints; it no longer says a setting can be changed on the bot's own dashboard, whose
    Settings page only shows them.
  - `ControlEndpoints.cs` lost its SA1507 warning (two blank lines). `ApplicationDtos.cs` keeps its QW0028 and QW0029
    warnings (strongly typed identifiers), as across the code, and `SpaceTradersDbContext.cs` its QW0029.
  - Tests: `AutomationSwitchesTests` (every plan on), `DefaultSettingsSeedTests` (a new agent starts with the chosen
    values; a setting that follows its default gets the new one, journaled; a setting someone set and a status flag keep
    theirs; a restart doesn't take the next run's values), `NextRunSettingsTests` (the run that runs now is left alone;
    status flags and unknown keys refused; forget, journal with secrets hidden, reset), `ApiIntegrationTests` (the
    endpoints, and that `PUT /settings/{key}` and the kill switch set the next runs too), `AgentBootstrapServiceTests` (a
    new agent after a reset), `AgentDataCleanupTests` and `DataRetentionTests` (the new table), `PrometheusMetricsTests`
    (the label), and against PostgreSQL 16: `DatabaseInitializerTests` (a database from before gets the column and table
    as the model creates them; of its settings, a plan that is off comes on while a changed setting and automation
    switched off keep their values; the settings are judged only when the column is added) and
    `AgentCleanupIntegrationTests` (the chosen values outlive the old agent and reach the new one). The Settings table in Grafana 11.6.1 against a local Prometheus
    scraping the series as the bot writes them, at desktop and phone width.
- Noticed (not changed):
  - `SettingsRepository.SetAsync` stores the type of what it was given, so `PUT /settings/{key}` turns a `bool` setting's
    type into `string`. Only the bot's own Settings page and the settings snapshot log show it; nothing acts on it.

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
- Step 3 of the gembernodes README, `REVOKE SELECT ON stored_credentials FROM spacetraders_ro;`, was
  done by you on 2026-10-02: `st.py check` reports `can_read_agent_token = f`. `st.py` still refuses
  any query that names the table.

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
  reads Secrets; the read-only login could read the agent token until 4.1's revoke (done the same day); Wolverine's
  warnings run to dozens of lines; and `sed -i` in Git Bash turns CRLF files into LF.
- To understand this phase, start with `.claude/skills/st-investigate/SKILL.md`, then
  `tools/investigate/README.md` and `st.py`'s `check`; for an example of the procedure, B45 in the
  known issues and `ScoutStopSkippedTests`.

### Phase 6: Make money, one loop at a time

A loop counts as done after a full reset period with no open anomalies for it on the dashboard.
How credits are split stays your call; Claude only fixes deviations from intended behaviour.

Checked after the reset of 2026-10-04 13:00Z, on the run that ended then (on the cluster since 2026-10-02 08:50Z, the last
2.2 days of its period): 6.10b's and 6.10c's checks are met, and 6.11's plan explored a system. The contract ran end to end
twice, the second time under the new agent (accepted at 13:06Z, fulfilled at 16:12Z), and the command ship went straight on
from scouting to the contract (6.2). No loop has had its full period yet; the first began at 13:00Z, with every plan on
since 18:09Z. The run's anomalies came from bugs since fixed (B42, B51, B55), a short outage of the shared Postgres, and
B59's 429s. The checks found three
more bugs: B38 is still open, B47's DRIFT fallback still flew the contract's trip, and a new one, B61, stores a market twice
when it is seen for the first time.

- **6.1 The first contract, end to end:** B8 and B9 (both fixed in 1.14; what's left is a clean
  reset period). Per D1 and D2 the bot takes one mineral contract per reset; taking the next
  contract is a later addition.
- **6.2 The command ship after scouting:** B10, and the scout part of B16 (both fixed in 1.14;
  what's left is a clean reset period). The ship moves on to its next job instead of holding on to
  the finished scout goal.
- **6.3 Probes** (merged 2026-10-02 as projects#129, deployed by gembernodes#28; built on branch `claude/spacetraders-more-scouting`, with your decisions
  D29 and D30; fixes B15 and B25). Asked that day: "I'd like more scouting to be done": a probe at
  every market in the long run, bought while the credits stay at 100,000; while there are fewer
  probes than markets, they drift between nearby markets, those not updated for a while first.
  - Done:
    - **Which ships are probes** (`FleetRoles.IsProbe`): a probe frame, or the type a probe is cached
      with, `SHIP_PROBE` when bought and its role `SATELLITE` after startup sync. The starting probe
      is one (B25). No other plan uses a probe: it has no hold, no tank and no mounts.
    - **Buying** (D29): while the headquarters' system has fewer probes than markets (26 in X1-DC53),
      the plan buys a SHIP_PROBE at the shipyard that sells it for the least (A2, 81,645 on
      2026-10-02), at most one a tick, as long as the purchase leaves the credit reserve
      (`FleetExpansion.MinCreditReserve`, 100,000): the first from 181,645 credits. Probes in flight
      count (B15). The 200,000 gate (D4), and the credits handler that woke the old plan, are gone.
    - **Roaming** (`Probes/ProbePlanner.cs`, no I/O): each tick every free probe (no flight, not in
      transit) gets a market that is due, its prices older than `Market.RefreshMinutes` (5), with no
      probe at it or on its way. Each pair of free probe and due market is scored by the market's age
      minus twice the flight there (CRUISE, as the API reckons it), and the best pair goes first, so
      a market goes to the probe nearest it: a market 10 minutes away must be 20 minutes staler than
      one next door. A market never seen is the oldest (J58, which B45 skipped). With a probe at every
      market nothing due is left without one, and the probes stay; the market watch keeps their
      markets fresh. Simulated on X1-DC53's 26 markets over a day, the prices' average age is about an
      hour with one probe, 36 minutes with two, 22 with three, 13 with five and 7 with ten; of the
      weights 1 to 6, 2 kept them youngest without leaving the far markets much older.
    - **The flight** (`DeployProbeGoalExecutor`): one goal per flight, in CRUISE. A probe has no tank,
      so no flight costs it fuel, and DRIFT (where the old plan parked probes) would make it ten times
      slower: a probe found in DRIFT is switched to CRUISE first. The arrival fetches the market and
      the shipyard, as every arrival does; then the goal ends and the plan chooses again.
    - **A purchase fetches a probe** (D30, `Services/ShipyardCalls.cs`): the API sells a ship only
      where one of ours is. `ShipPurchaseService` checks that first: without a ship there it makes no
      API call and records a call at the shipyard. The probe plan sends the nearest free probe, which
      stays while the call is open (2 minutes after the last attempt), and the next attempt buys.
      Every plan buys this way: the mining plan's drones at H52, the trading plan's shuttle at A2
      (which, with none of our ships at A2, would have failed at the API) and the probes. New journal
      kind `ProbeCalled`.
    - **The price at the moment of purchase:** with a ship at the shipyard, the purchase fetches the
      shipyard again and keeps the reserve with the price it asks now. A cached price can be hours
      old, and every purchase moves it (SPECTER-3 cost 46,885, SPECTER-4 48,328).
    - **The plan's state** lists every market with the probe at it or on its way, or another ship of
      ours at it, or when it is due; the next probe's shipyard and price and why it isn't bought
      (`Purchase`: `WaitingForCredits`, `WaitingForAShipAtTheShipyard`, ...); and the open calls.
      Written only when it changes. Journal: `PlanStarted` once, `PlanBlocked`
      (`waiting_for_credits`) when it starts waiting, `ProbeCalled`, and `ShipPurchased`.
    - `ShipLeftIdle` (D13): a due market that no probe or ship watches is work for any probe; a probe
      parked at its market while every market is watched is not idle.
    - The fleet view: a probe flying to a market is "scouting", one fetched to a shipyard "called to a
      shipyard", one without a flight "watching its market" (they said "deploying" and "idle").
  - Noticed (not changed):
    - Purchases share the credits above the reserve, plan by plan in the tick's order (probes, then
      mining, then trading), and as the credits grow a cheaper ship's bar comes first: a drone (48,328)
      from 148,328 credits, a probe from 181,645. While the mining plan wants drones (D28), probes may
      wait. How the credits are split is yours.
    - A roaming probe has no goal for up to a tick after each arrival, and the 10-second sampler can
      catch that: a `ShipIdle` (`goal_ended`) journal line for about half its flights, as for a miner
      after each trip.
    - The flight weight (2) and a call's 2 minutes are constants, not settings.
    - A probe at A2 is SCARCE, so its price rises as probes are bought: 26 probes cost well over 2
      million credits.
  - To switch it on: `PUT /settings/Automation.Plan.ProbeDeployment.Enabled` with `{"value": "true"}`.
    The setting's description in the cluster's database still describes the old plan: a description
    is seeded once per agent, so the new one comes with the next reset.
  - Done when: a full reset period with the probe plan on and no open anomaly for it.
- **6.3 in short** (merged 2026-10-02 as projects#129): every market of the headquarters' system gets a probe in the
  long run, bought while the credits stay at the reserve; until then the probes roam, each to the
  market whose prices are oldest once the flight there counts against it; and a purchase where none
  of our ships is fetches a probe first. To understand this, start with
  `SpaceTraders.Application/Probes/ProbePlanner.cs`, then `Automation/ProbeDeploymentPlanService.cs`,
  and `Services/ShipPurchaseService.cs` with `Services/ShipyardCalls.cs`;
  `tests/SpaceTraders.Application.Tests/Probes/ProbePlannerTests.cs` holds X1-DC53's positions.
  - Files, in `SpaceTraders.Application` unless named:
    - new: `Probes/ProbePlanner.cs`, `Services/ShipyardCalls.cs`;
    - rewritten: `Automation/ProbeDeploymentPlanService.cs`, `ProbeDeploymentPlanState.cs`,
      `Goals/Executors/DeployProbeGoalExecutor.cs`;
    - changed: `Services/ShipPurchaseService.cs` and `IShipPurchaseService.cs` (a ship there, the
      calls, the price again, `ShipPurchaseFailure`), `Commands/Fleet/PurchaseShipCommand.cs`,
      `Automation/FleetRoles.cs` (`IsProbe`), `Health/ShipLeftIdleRule.cs`, `JournalEvents.cs`
      (`ProbeCalled`), `DependencyInjection.cs`; `DeployProbeGoal` (Domain: `ForPurchase`);
      `PrometheusMetricsService` (API: the fleet view's words); `DefaultSettingsSeed` (Persistence:
      the probe switch's description, for the next agent);
    - removed: `EventHandlers/ProbeDeploymentCreditsChangedHandler.cs` and
      `Commands/Ships/DeployProbeCommand.cs`, which only the old plan used;
    - tests: `Probes/ProbePlannerTests` (new), `Automation/ProbeDeploymentPlanServiceTests` and
      `Goals/DeployProbeGoalExecutorTests` (rewritten), `Services/ShipPurchaseServiceTests` (a ship
      there, the calls, the price; `ShipyardCallsTests`), `PurchaseShipHandlerTests`,
      `Health/ShipRuleTests` (the probe cases), `AlreadyAtDestinationLoopTests`,
      `PrometheusMetricsTests` (API), and the credits handler's tests removed from
      `LedgerEntryHandlerTests`.
- **6.4 Surveying and mining** (merged 2026-10-02 as projects#122, its dashboard as gembernodes#21,
  deployed by gembernodes#22; it took in the old 6.4, mining drones mine and sell, with the survey
  part of B16, B17 and B34). Asked that day:
  1. a ship that can survey surveys before it trades: the contract's ore first, otherwise ores the
     system's markets buy, at asteroids near the market that buys them;
  2. a miner mines surveyed ores before unsurveyed asteroids;
  3. several ships may mine for a contract; over-mining is fine, the rest is sold;
  4. ships mine ores that are in low supply at a market, and sell them there;
  5. a ship that can mine mines before it trades, so the command ship stops trading, and the trading
     plan buys its own cargo ships;

  and during the work: fix B34 first; a survey dashboard, to see whether we survey too much or too
  little; a credit bar under which only fuel is bought (D24); and the market fetched again after each
  trade (D25). Your choices: D20–D25.
  - Done:
    - **B34, first:** startup sync stores each waypoint's traits and modifiers (and orbitals, parent,
      chart), and fetches a system's waypoints again, once, when a cached one has no traits: the
      cluster's 85 waypoints get theirs at the first start after the deploy, keeping when each was
      last observed. **What an asteroid yields** (`AsteroidDeposits`) follows from its traits, by the
      table community bots use (the game publishes none): XB5C's common metal deposits yielded the six
      ores it lists, about equally, on 2026-10-02.
    - **The survey plan** (`SurveyPlanService`, new switch `Automation.Plan.Survey.Enabled`, off): a
      ship that can survey surveys, and only that (D20). One survey per goal: the contract's ore at
      the contract's asteroid while the contract plan mines it; otherwise an ore a market buys, at the
      asteroid nearest the market that pays most, among those the miners can reach (a drone's 80-unit
      tank keeps it in the middle of X1-DC53, so that is XB5C); ores without a usable survey there
      first, then the best paid. The goal ends after each survey (B16's survey part: survey goals
      never ended), and the plan gives the next.
    - **Extraction** (`MineResourceVolumeCommand`) picks the best usable survey of the asteroid for its
      ore: the largest share of the deposits, then the larger deposit, then the later expiry. A survey
      the API refuses (4224 exhausted, 4221 expired, 4220 not verified) is dropped (B49). Any asteroid
      type can be mined (B48).
    - **The mining plan** (rewritten): one trip per goal, chosen again after each sale. A miner that
      holds ore sells it first, where it fetches most (the contract's leftovers); otherwise a surveyed
      ore first, sold where it fetches most; then an ore in low supply (SCARCE or LIMITED, D22), mined
      at the asteroid nearest that market and sold there. One miner per sell market and ore. The trip
      flies with its goal (B17), through refuelling stops, never DRIFT. It buys drones only for
      low-supply openings a drone could reach, and not while the contract takes the miners (D23).
    - **The contract** (D23): every free miner joins the active contract with an assignment like the
      first ship's; completion releases them all, and a ship that arrives after another fulfilled the
      contract doesn't call fulfil again. With the survey plan on, the command ship surveys the
      contract's ore, and the contract's miners extract with those surveys.
    - **Who does what** (`FleetRoles`): the survey plan is bootstrapped before mining and trading;
      with it on, a ship that can survey neither mines nor trades (D20); a miner trades only when
      neither the contract nor the mining plan has work for it.
    - **Cargo ships** (D21): when every trader has a trip, the trading plan buys the next ship in
      `Trade.ShipPurchases` (`SHIP_LIGHT_SHUTTLE,SHIP_LIGHT_HAULER,SHIP_LIGHT_HAULER`), when it would
      have a lucrative route from the shipyard, within the credit reserve: the shuttle (117,273) from
      about 217,000 credits, a hauler (354,210) from about 454,000.
    - **D24:** cargo leaves `Trade.FuelReserveCredits` (5,000) untouched, on top of the trip's fuel.
      **D25:** after each purchase and sale, by a trader or a miner, the market is fetched again while
      the ship is still docked (`MarketRefresher`, which the market watch uses too).
    - **The survey dashboard's data:** each survey's life is counted and journaled: `Surveyed` (one per
      survey: deposits, size, expiry), `Extracted` (per extraction, with the survey's signature or
      none), `SurveyEnded` (expired, exhausted or not verified, with how many extractions used it);
      metrics `spacetraders_surveys_taken_total`, `_surveys_ended_total{reason,used}`,
      `_surveys_active{used}` and `spacetraders_extractions_total{surveyed}`. Surveys expiring unused
      mean too many; extractions without a survey while a surveyor works mean too few.
      `cached_surveys` gained `Extractions`; there are no migrations, so the initializer adds the
      column to the cluster's table (`ADD COLUMN IF NOT EXISTS`). The dashboard's survey section is a
      gembernodes change (see the table at the end); its PromQL passed `promtool check rules`.
    - `ShipLeftIdle` (D13) counts survey work for surveyors, contract work for every free miner, and a
      low-supply opening only for the miners that can reach it.
  - Noticed (not changed):
    - `HasMiningEquipment` counts a surveyor mount as mining equipment, and a test asserts it, against
      the comment that says "mining mount or miner-type frame". No ship we have or can buy has a
      surveyor and a hold without a laser, so it changes nothing today. Yours to call.
    - Extraction still jettisons every ore but the one the trip mines, as before. At XB5C without a
      survey that is five extractions in six; keeping what a nearby market buys would fill holds
      faster, at the price of more sell stops.
    - The contract plan picks the asteroid nearest the ship it starts with (by design, B34); with the
      traits stored, its tie-break by deposits now works. Whether an asteroid that yields the ore
      should beat a nearer one that doesn't is still your call.
    - Asteroid modifiers (STRIPPED, UNSTABLE, ...) are stored but not used.
    - The survey request sends the cached survey back to the API; whether the API accepts the expiry
      as it comes back from the database is only known once it runs: a rejection shows as
      `SurveyEnded` with reason `not_verified`, and the miner carries on without surveys.
  - Follow-up after the switch-on (2026-10-02, your decision D26, fixes B50; built on branch
    `claude/spacetraders-trip-release`). To understand it, start with `EndTripAsync` in
    `FulfillContractDeliveryCommand.cs`, then `AddFreeMinersAsync` and `ReleaseShipsAsync` in
    `Automation/ContractPlanService.cs`.
    - **A contract trip ends at its delivery:** the delivery closes the ship's contract assignment,
      after the fulfil call when one was due, and the plans assign the ship again on the next tick,
      in their order. A miner rejoins the contract (`MiningStarted`, reason `contract`, now once per
      trip); with the survey plan on, the command ship surveys. A ship whose fulfil call fails keeps
      its assignment and makes the call again: no ship would get a trip with no units left.
    - **The plan's first ship is no longer special:** it got its assignment back on every tick,
      whatever it was doing, and even had another assignment replaced; now it joins like every free
      miner. The two tests of that restore are replaced by tests of the new rule.
    - **A restart reconsiders once** (`StartupRecoveryService`, with automation and the contract plan
      on): every ship on the contract that isn't in flight is released, and the first tick assigns
      it again. A ship in flight keeps its assignment until its delivery: without one, nothing would
      record its arrival, and it would never be free again.
    - Noticed (not changed): a released ship keeps its cargo. A miner delivers it with its next trip,
      but the command ship, once it surveys, carries what it mined for the contract (7 copper on
      SPECTER-1) for good: nothing sells a surveyor's cargo. Surveying needs no hold, so it costs only
      the ore's value. Yours to call.
    - Files: `FulfillContractDeliveryCommand.cs`, `Automation/ContractPlanService.cs`
      (`EnsureActivePlanAssignmentAsync` removed, `ReleaseShipsAsync` added),
      `StartupRecoveryService.cs` (API); tests: `ContractMinersTests` (four new),
      `FulfillContractDeliveryHandlerTests` (two new, one extended), `StartupRecoveryServiceTests`
      (new, API), and the two restore tests removed from `ContractPlanServiceTests`.
  - Second follow-up (2026-10-02, fixes B51; built on branch `claude/spacetraders-b51`). To understand
    it, start with `ExtractWithSurveyAsync` in `SpaceTradersPortAdapter.cs`.
    - **A survey goes back as the API gave it out:** its expiry is written as the API writes it (UTC,
      milliseconds, `Z`, `ExtractWithSurveyRequest.FormatExpiration`).
    - **A survey the API can't read is dropped** (a 422 without a game error code, reason
      `rejected`): one call per survey instead of five a tick for as long as the survey lasts. The
      warning carries the API's response body, whose `data` names what it couldn't read. A 422 with a
      game error code (a full hold, say) stays the API's.
    - Noticed (not changed): Wolverine retries every failed command 4 times (`RetryWithCooldown` in
      `DependencyInjection.cs`), whatever the error, so any API call that fails for good costs five
      calls a tick. Retrying makes sense for 429s and 5xx, not for a 4xx that will fail the same way.
    - Files: `SpaceTradersPortAdapter.cs`, `SpaceTradersApiClient.cs`, `Phase1ActionModels.cs`
      (Infrastructure.SpaceTradersAPI), `Ports/SurveyRefusedException.cs`,
      `Commands/Ships/MineResourceVolumeCommand.cs`; tests: `SurveyRequestTests` (new),
      `SurveyRefusalTests` (two new), `MineResourceVolumeHandlerTests` (one new).
  - Third follow-up (2026-10-02, your decision D27; built on branch
    `claude/spacetraders-survey-stock`). To understand it, start with `SurveyTargets` in
    `Mining/MiningPlanner.cs`, then `EnsureBootstrappedAsync` in `Automation/SurveyPlanService.cs`.
    - **A stock of surveys per ore:** a target needs a survey while fewer usable surveys hold its ore
      than `Survey.StockPerOre` (new setting, 2). The contract's ore comes first only while it needs
      one; then the ore with the fewest usable surveys, then the best paid. With the stock for every
      ore, the surveyor waits until a survey expires or is used up.
    - The plan's state shows each target's usable surveys and whether it needs one; `ShipLeftIdle`
      counts only those that need one, so a surveyor that waits is not an anomaly.
    - **Deployed** by gembernodes#26 at 15:34Z: every ore at XB5C had 17 to 24 usable surveys left over,
      so SPECTER-1 waits until they expire.
    - **Refined the same day** ("Stock per ore per asteroid. I'd like the surveys to be close to
      wherever the mineral can be sold"; branch `claude/spacetraders-survey-per-market`): every market
      that buys an ore gets the reachable asteroid nearest it, not only the market that pays most; one
      target per ore and asteroid, each with its own stock. With only drones as miners nothing changes
      yet: the one asteroid they reach is XB5C, nearest every buyer they can reach. It shows once
      miners reach further.
    - Files: `Mining/MiningPlanner.cs` (`SurveyTargets`, `SurveyTarget`), `Mining/SurveySelection.cs`
      (`CountUsable`), `Automation/SurveyPlanService.cs`, `SurveyPlanState.cs`,
      `Health/ShipLeftIdleRule.cs`, `DefaultSettingsSeed.cs` (Persistence); tests:
      `MiningPlannerTests` (the contract-first test replaced, two new; two more for the refinement),
      `SurveyPlanServiceTests` (three new), `ShipRuleTests` (one new), `DefaultSettingsSeedTests`.
  - Fourth follow-up (2026-10-02, your decision D28; built on branch `claude/spacetraders-mining-rules`).
    To understand it, start with `MiningTargets` and `CompareBestFirst` in `Mining/MiningPlanner.cs`,
    then `BuyDroneAsync` in `Automation/MiningAutomationService.cs`.
    - **What a miner mines:** every market that buys an ore is a target, mined at an asteroid with a
      usable survey holding it or else the asteroid nearest the market. They rank by the market's
      supply first: SCARCE, LIMITED, MODERATE, HIGH, ABUNDANT; within a level, surveyed first, then
      the most an extraction is expected to fetch. So miners serve the scarce markets first, and once
      none is short, keep mining the lowest supply there is, even when it pays less. A trip for a
      market that isn't short logs reason `lowest_supply`.
    - **When a drone is bought:** with every miner working, the plan asks the same ranking what a new
      drone would mine (the trips under way held), and buys it only if that is in low supply (SCARCE or
      LIMITED, D22). One a tick: it used to buy one for every opening at once, and a drone could then
      go to a market that wasn't short, leaving its opening to pay for the next.
    - Files: `Mining/MiningPlanner.cs` (`MiningTargets`, `CompareBestFirst`, `SupplyRank`,
      `MiningTarget.Supply`), `Automation/MiningAutomationService.cs` (`BuyDroneAsync`),
      `JournalEvents.cs`; tests: `MiningPlannerTests` and `MiningAutomationServiceTests` (the
      expectations that assumed surveyed-first rewritten, four new).
  - To switch it on: `PUT /settings/Automation.Plan.Survey.Enabled` and
    `.../Automation.Plan.Mining.Enabled` with `{"value": "true"}` (trading as in 6.5).
  - Done when: a full reset period with these plans on and no open anomaly for them.
- **6.4 in short** (merged 2026-10-02 as projects#122): the command ship surveys (the contract's ore
  first), every free miner works the contract and then mines surveyed or scarce ores for the market,
  one trip at a time; the trading plan buys a shuttle and then haulers, keeps 5,000 credits for
  fuel, and fetches a market again after each trade; asteroids' traits are stored, so the bot knows
  what each yields. To understand this, start with
  `SpaceTraders.Application/Mining/MiningPlanner.cs` and `AsteroidDeposits.cs`, then
  `Automation/SurveyPlanService.cs`, `MiningAutomationService.cs` and `FleetRoles.cs`;
  `tests/SpaceTraders.Application.Tests/Mining/MiningFixture.cs` holds X1-DC53's middle as the tests
  use it.
  - Files, in `SpaceTraders.Application` unless named:
    - new: `Mining/AsteroidDeposits.cs`, `SurveySelection.cs`, `MiningPlanner.cs`, `MiningContext.cs`,
      `SurveyKeeper.cs`; `Automation/SurveyPlanService.cs`, `SurveyPlanState.cs`, `FleetRoles.cs`;
      `Goals/Executors/GoalFlight.cs`; `Services/MarketRefresher.cs`; `Ports/SurveyRefusedException.cs`;
    - rewritten: `Automation/MiningAutomationService.cs`, `Goals/Executors/MineAndSellGoalExecutor.cs`,
      `SurveyWaypointGoalExecutor.cs`; `ISurveyRepository` and `SurveyRepository` (Persistence);
    - changed: `ContractPlanService` (D23), `TradingAutomationService` (D20, D21), `TradeContextReader`
      (D24), `TradeBetweenMarketsGoalExecutor` (D25), `MarketWatchService`, `MineResourceVolumeCommand`
      (B48, B49), `FulfillContractDeliveryCommand`, `ShipLeftIdleRule`, `AutomationSwitches` (the
      Survey plan), `GameLoopService`, `JournalEvents`, `IAutomationMetrics`, `TradeMarketMap`,
      `TradeRoutePlanner`, `MineAndSellGoal` (Domain: `Selling`), `ShipyardShipDto`;
      `StartupSyncService` (API, B34), `PrometheusAutomationMetrics`, `PrometheusMetricsService`;
      `SpaceTradersPortAdapter` (the refusals); `ShipyardRepository`, `CachedSurvey`,
      `SpaceTradersDatabaseInitializer`, `DefaultSettingsSeed` (Persistence); two unused goal queries
      removed from `IShipGoalRepository`;
    - tests: `Mining/*`, `Automation/SurveyPlanServiceTests`, `MiningAutomationServiceTests`,
      `ContractMinersTests`, the executors', `SurveyRepositoryTests`, `SurveyRefusalTests`,
      `MarketRefresherTests`, `ShipyardShipCapacityTests`; B34 in `StartupSyncServiceTests`, the
      column in `DatabaseInitializerTests`, and additions to the trading, contract delivery, idle rule,
      game loop and metrics tests.
- **6.5 Trading** (merged 2026-10-02 as projects#121; deployed by gembernodes#20). Asked for that day:
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
- **6.6 Jump gate construction** (merged 2026-10-04 as projects#155, its dashboard as gembernodes#53, deployed by
  gembernodes#52; built on branch `ccr-914173a3-6coo89`, with your decisions D64–D68). Asked that day: "Spacetraders has unfinished buildings at waypoints, specifically an unbuilt jump
  node. Finishing this jump node should be top priority, as it opens up the rest of the game. Can you implement a special
  role that works on this jump gate? Also please check whether the construction command gives any money, if so the new
  role can be set up like a trader, if not it probably needs a different money cap."
  - **Supplying pays nothing:** the API's supply call (`POST systems/{system}/waypoints/{waypoint}/construction/supply`,
    OpenAPI spec v2.3.0) answers with the construction site and the ship's cargo only, without credits or the agent. So
    the role isn't set up like a trader: a load is judged like a ship purchase (D64). A refused supply answers 4800 (the
    site doesn't need the material), 4801 (it has all of it) or 4802 (the ship isn't at the site).
  - **X1-DC53** (public API, 2026-10-04): its jump gate, X1-DC53-I55, was complete before our ships came (FAB_MATS
    1600/1600, ADVANCED_CIRCUITRY 400/400, QUANTUM_STABILIZERS 1/1). Its neighbours X1-HZ59-I59 and X1-BG54-I54 were
    under construction, needing the same, but D68 leaves them out. F49 exports FAB_MATS and D42 ADVANCED_CIRCUITRY. So
    the plan finds nothing to build until the server reset (2026-10-04 13:00Z) gives a new home system.
  - **X1-FJ91**, the home system after that reset: its gate, X1-FJ91-I64, needs 1,600 FAB_MATS and 400 ADVANCED_CIRCUITRY
    (QUANTUM_STABILIZERS 1/1). The plan started at 18:09Z, when slice 2.12 switched it on, with SPECTER-1 as its builder;
    the builder trades while the ships ahead of the gate's loads in the purchase order are bought (D64).
  - Done:
    - **The role** (`FleetRole.Construct`, `FleetRoles.CanConstruct`): a hold and a tank, and no drone or probe. While the
      home gate needs materials, the role board gives it (reason `construction`) to the `Construction.Ships` (new, 1)
      largest holds that are left once the drones and the surveyor are decided, before the trips by profit (D65); of
      equal holds the one that builds now, then the one that can do least else. The gate starting or stopping to need
      materials weighs the roles at once. A builder trades when the construction plan has nothing it may buy. With the
      board off, the plan picks its builders by the same rule.
    - **The site** (`Construction/ConstructionSites.cs`): only the home system's jump gate (D68), fetched when the
      waypoint cache lists it under construction and it isn't cached, then every 10 minutes while it needs materials
      (other agents supply it too), and stored from every supply's answer: `cached_construction_sites`, which nothing
      wrote before. Journal: `PlanStarted` with its materials, `PlanCompleted`.
    - **The plan** (`Automation/ConstructionPlanService.cs`, new switch `Automation.Plan.Construction.Enabled`, off;
      bootstrapped after siphon and before trading): a free ship that holds what the gate needs supplies it first,
      whatever its role (`held_cargo`); then it tells the order ships are bought in what it would buy next, and when
      that lets it, each free builder with an empty hold takes the first load the credits pay for above the credit
      reserve (`purchase`). The state (`plan_states`, `Construction`): each material with what is on its way, the
      builders, those a load waits for, and why none was bought (`purchase_order`, `waiting_for_credits`, `low_supply`,
      `trade_volume`, `no_market`).
    - **A load** (`Construction/ConstructionPlanner.cs`, no I/O): one material in one purchase, a full hold or what the
      gate still needs, at a market whose trade volume takes it at once (D67) and whose supply isn't SCARCE or LIMITED
      (D66); the material with the smallest share supplied or on its way first; the market where the load costs least
      with its fuel, there and on to the gate, in CRUISE through refuelling stops.
    - **The trip** (`SupplyConstructionGoal`, now a `TripGoal` with the load, what it holds back and what it paid;
      `Goals/Executors/SupplyConstructionGoalExecutor.cs`): to the market, where it checks the load again with the
      prices its arrival fetched (dropped as `not_needed`, `not_sold_here`, `low_supply`, `not_full_hold` or
      `over_budget`), buys it in one purchase, flies to the gate and supplies it. A supply the API refuses
      (`ConstructionRefusedException`, from the port adapter) ends the trip at Warning with the cargo aboard, fetches
      the site again, and the ship isn't offered the material again for 10 minutes (`ConstructionRetries`). Each trip
      books its loss (`TripEnded`, activity `construction`, D46).
    - **Money** (D64): a load leaves the credit reserve and waits for tiers 1 to 5 of the order ships are bought in
      (`PurchaseTier.Construction`, 6); the probes (now 7) and the turns of drones and cargo ships (now 8) wait while
      the gate has a load to buy. A trip holds back its cargo from start to purchase (`ReservedCredits`, as D57): the
      trading plan, the trade executor and `BudgetPolicy` (so `spacetraders_credit_reserve`) leave it. The purchases
      have a ledger category of their own, `ConstructionBuy` (`CargoPurchasedEvent.ForConstruction`).
    - **Cargo the gate needs** isn't jettisoned by the trading plan while the construction plan is on.
    - **Visibility:** the journal kinds `ConstructionStarted`, `ConstructionSupplied` and `ConstructionDropped`; the
      metrics `spacetraders_construction_units_required` and `_fulfilled` (`site`, `trade_symbol`); the fleet view's
      "buying … at … for …" and "supplying … to …"; `ShipLeftIdle` counts a load as work for the builders a load waits
      for; `ConstructionSuppliedEvent` is published (an `activity_logs` row).
    - **The dashboard** (gembernodes, same branch): "Jump gate progress" (percent), "Jump gate: materials still needed"
      (a table of what is left, supplied and required per material) and "Jump gate materials" (each material's share),
      under Contracts; the Roles, Purchase order, Spent per hour and Profit per hour descriptions name the new reason,
      tier, ledger category and activity.
  - Noticed (not changed):
    - **The scale:** X1-DC53's neighbours needed 1,600 FAB_MATS and 400 ADVANCED_CIRCUITRY each. If the next home gate is
      like them, an 80-unit hold takes 25 loads, each waiting for a market at MODERATE or better whose trade volume
      takes the hold (D66, D67), and probes and further ships wait meanwhile (D64). The state's `Waiting` and the
      dashboard's Purchase order table show what holds it up.
    - **A trade volume under the hold** holds that material up for as long as it lasts (D67): if no market trades a
      whole hold of it at once (say 40 at a time for an 80-unit hauler), the builder never buys it, the state says
      `trade_volume`, and the probes and further ships keep waiting behind the load (D64). Whether to change that is
      your call; nothing here does.
    - **A gate others finish:** the plan sees it at the next fetch, within 10 minutes. A trip on its way to buy is then
      dropped `not_needed` at its market; one that has bought is refused at the gate (`not_needed`) and keeps its cargo,
      which the trading plan sells where it fetches most, likely below what it cost.
    - **Settings after a reset:** a new agent's settings start at their defaults, so the plan (and the role board) must
      be switched on again for the new home gate.
    - **And exploring** (slice 6.11, merged meanwhile): a jump needs both gates built, so the explore plan can't take
      the command ship while the home gate needs materials; they take turns. The explore plan looks at a gate under
      construction again hourly, so it may set off up to an hour after this plan has seen the gate complete; it could
      read the construction cache instead, which is your call.
    - **The shipyards table** (slice 2.11) judges what a ship for sale could do as the fleet table does, so a cargo ship
      now reads `Trade, Construct` there too; its gembernodes description (on its own branch) doesn't name `Construct`.
    - The dashboard's Purchase order description had no SurveyorPerArea (D55) and the Roles description no
      `gathers_first` (D58); both are fixed with this slice. HOW_IT_WORKS said "8 of the 13 goal kinds" never run; with
      `SupplyConstruction` running it is 6 of 15.
    - Left as they were: the Qowaiv warnings (QW0028, QW0029) in `ShipGoal.cs` and `DomainEvents.cs`, which ask for
      strongly typed identifiers across the domain. The five S8969 warnings in `ShipGoalRepositoryTests.cs` are fixed.
  - To switch it on: `PUT /settings/Automation.Plan.Construction.Enabled` with `{"value": "true"}`, with the role board on
    (`Automation.Plan.Roles.Enabled`) so the role shows on the dashboard; after the reset, on the new agent.
  - Done when: the home gate is complete with the plan on, and no open anomaly for it.
  - **To understand this,** start with D64–D68, then `Construction/ConstructionPlanner.cs` and `ConstructionSites.cs`,
    `Automation/ConstructionPlanService.cs` and `Goals/Executors/SupplyConstructionGoalExecutor.cs`;
    `tests/SpaceTraders.Application.Tests/Construction/ConstructionFixture.cs` holds X1-DC53's gate side (positions from
    the API, prices made up).
  - Files, in `SpaceTraders.Application` unless named:
    - new: `Construction/ConstructionPlanner.cs`, `Construction/ConstructionSites.cs` (with `ConstructionSiteWatch`),
      `Construction/ConstructionRetries.cs`, `Automation/ConstructionPlanService.cs`, `Automation/ConstructionPlanState.cs`,
      `Goals/Executors/SupplyConstructionGoalExecutor.cs`, `Ports/ConstructionRefusedException.cs`;
    - changed: `Automation/FleetRoles.cs`, `AutomationSwitches.cs`, `GameLoopService.cs`, `RolePlanService.cs`,
      `TradingAutomationService.cs`; `Roles/FleetRole.cs`, `FleetRoleBoard.cs`, `RolePlanner.cs`, `RoleSettings.cs`,
      `RoleAdvisor.cs`, `RoleEstimator.cs`; `Services/PurchaseOrder.cs`, `TripBook.cs`; `Trading/TripReservations.cs`;
      `Orchestration/BudgetPolicy.cs`; `Goals/ShipGoalExecutorService.cs`,
      `Goals/Executors/TradeBetweenMarketsGoalExecutor.cs`; `EventHandlers/LedgerEntryHandler.cs`;
      `Health/ShipLeftIdleRule.cs`; `Interfaces/IAutomationMetrics.cs`, `Interfaces/Repositories/IShipGoalRepository.cs`;
      `JournalEvents.cs`; `DependencyInjection.cs`; `SupplyConstructionGoal`, `LedgerCategory` and
      `CargoPurchasedEvent` (Domain); `ShipGoalRepository` and `DefaultSettingsSeed` (Persistence);
      `SpaceTradersPortAdapter` (Infrastructure.SpaceTradersAPI); `PrometheusMetricsService` and
      `PrometheusAutomationMetrics` (API);
    - tests: new `Construction/ConstructionPlannerTests`, `ConstructionSitesTests`, `Roles/RolePlannerConstructionTests`,
      `Roles/ConstructionRoleTests`, `Automation/ConstructionPlanServiceTests`, `Goals/SupplyConstructionGoalExecutorTests`;
      more in `RolePlanServiceTests`, `TradingAutomationServiceTests`, `TradeBetweenMarketsGoalExecutorTests`,
      `BudgetPolicyTests`, `LedgerEntryHandlerTests`, `PurchaseOrderTests`, `ShipGoalExecutorServiceTests`,
      `ShipRuleTests`, `DefaultSettingsSeedTests`, `AutomationSwitchesTests`, `ShipGoalSerializationTests` (Domain),
      `PrometheusMetricsTests` and `DiValidationTests` (API), and `ShipGoalRepositoryTests` (Postgres).
  - Tests: App 940 (76 new), Domain 72, API 167 (2 new, and 4 skipped); against Postgres, Infrastructure 75 (1 new, in
    `ShipGoalRepositoryTests`) and API 2. Merged with slices 6.11 and 2.11 (whose decisions took D59–D63, so this
    slice's are D64–D68): App 981, Domain 72, API 171 (and 4 skipped), Infrastructure 76 and API 2 against Postgres;
    2.11's shipyards test now expects `Construct` in what a cargo ship for sale can do.
- **6.7 Siphoning** (merged 2026-10-02 as projects#130, deployed by gembernodes#29; built on branch `ccr-0969c532-x5ricm`, with your decisions D31–D33).
  Asked that day: "Can you work on implementing syphons. Functions practically the same as minors,
  including surveys, but for gassy materials."
  - **No surveys, by the game's rules:** the API's siphon call (`POST my/ships/{ship}/siphon`) takes no
    survey (the OpenAPI spec v2.3.0 gives it no body), the docs use surveys with the extract call only,
    and a surveyor's deposits are ores only (the command ship's Surveyor II lists 13 ores). Everything
    else follows the miners.
  - **X1-DC53** (public API, 2026-10-02): one gas giant, C38 (-57,-143), with STRONG_MAGNETOSPHERE as its
    only trait. The orbital station C39 at the same spot sells `SHIP_SIPHON_DRONE` (and probes), and
    exchanges the three gases; G50 imports all three, E47 and F48 the two liquids. C40, a fuel station
    39 from C38, is the refuelling stop towards G50 and E47 for an 80-unit tank.
  - Done:
    - **Who siphons** (`FleetRoles.IsSiphoner`): a ship with a gas siphon, a hold and a tank, and nothing
      to mine or survey with, whichever plans are on: a siphon drone. The command ship has a
      MOUNT_GAS_SIPHON_II too, but it mines or surveys, as before. A siphoner trades only when the siphon
      plan has no trip for it, as a miner.
    - **What a gas giant yields** (`Siphoning/GasGiants.cs`): the game publishes no table, and C38's
      traits name no gas, so every gas giant counts as yielding HYDROCARBON, LIQUID_HYDROGEN and
      LIQUID_NITROGEN, about equally (the gases C39 exchanges). Only GAS_GIANT waypoints are siphoned.
    - **The siphon plan** (`SiphonAutomationService`, new switch `Automation.Plan.Siphon.Enabled`, off;
      bootstrapped after mining and before trading): one trip per goal (`SiphonAndSellGoal`), chosen again
      after each sale. A siphoner that holds goods a market buys sells them first, one good a trip, where
      each fetches most after fuel (a full hold only sells, even at a loss on the fuel: a siphon trip would
      end at once without its gas aboard, on every tick); otherwise the best of
      `SiphonPlanner.SiphonTargets`: every market
      that buys a gas, siphoned at the gas giant nearest it and sold there, the market shortest of its
      gas first (D28), then the most a siphon is expected to fetch, then the nearest gas giant. One
      siphoner per sell market and gas. Journal: `SiphonStarted`, reason `low_supply`, `lowest_supply`
      or `held_cargo`.
    - **The trip** (`SiphonAndSellGoalExecutor`, `SiphonResourcesCommand`): flies with its goal through
      refuelling stops, never DRIFT (`GoalFlight`), orbits, siphons once per cooldown and keeps every
      good a market it can reach from the gas giant buys (D33), jettisoning the rest, which would fill the
      hold for good. A full hold, of any gases, turns it to selling: its own gas at its market, in
      batches of the trade volume, then the market fetched again (D25). Journal: `Siphoned` per siphon.
    - **Drones** (D32): when no siphoner is free, a `SHIP_SIPHON_DRONE` is bought, one a tick, only when
      its first trip by the same ranking would serve a market short of its gas, at the shipyard that
      sells it for the least in a system where our ships are, up to `Siphon.MaxDrones` (new, 10), within
      the credit reserve. In X1-DC53 that is C39.
    - **Contracts** stay mineral-only (D2, D31).
    - **Visibility:** a siphon's yield counts in `spacetraders_extracted_units_total`, so the dashboard's
      fleet table and "Mined and jettisoned per hour" show gases with no gembernodes change; it isn't an
      extraction in `spacetraders_extractions_total`, which the survey statistics read. The Journal
      panel shows the new kinds. The fleet view says "siphoning for …" and "selling …". The plan's state
      (`plan_states`, `SiphonAutomation`) lists the gas openings of every system where our ships are,
      before the first drone too; `ShipLeftIdle` (D13) counts a gas opening as work for the siphoners
      the plan lists as able to reach its gas giant.
  - Noticed (not changed):
    - **The first drone needs a ship at C39:** the API sells a ship only where one of ours is, and no
      ship of ours stays at C39. The purchase calls for a probe (D30), which only the probe plan answers:
      with it off, no siphon drone is bought until a ship happens to be there.
    - **The gas processor:** the docs say a siphon needs a gas siphon and a gas processor module. The
      command ship has a MODULE_GAS_PROCESSOR_I; whether a siphon drone does isn't known here (the
      shipyard listing the bot caches has no modules), and the plan doesn't ask. If the API refused a
      drone's siphons, each attempt would drop its trip with a warning, and `RepeatingError` would show it.
    - **Unconfirmed yields:** the three gases at about a third each are an assumption until the first
      `Siphoned` lines.
    - **Log volume:** a `Siphoned` line per siphon, as `Extracted` per extraction: at a cooldown of about
      70 seconds (an extraction's, in the soak test) that is some 1,200 lines a day per drone, 12,000 for
      ten, against the 50,000-a-day log alert that mining's lines count towards too.
    - A trip sells only its own gas at its market, as you chose; the other gases follow one good a trip,
      each where it fetches most after fuel, which is often the same market without a flight.
    - The mining planner's tie-break by the nearest asteroid never applies: its comparer ends with the
      opening's key, so ties go by key. The siphon planner's does apply.
  - To switch it on: `PUT /settings/Automation.Plan.Siphon.Enabled` with `{"value": "true"}` (and the
    probe plan, for the first purchase at C39).
  - Done when: a full reset period with the siphon plan on and no open anomaly for it.
  - **To understand this,** start with `SpaceTraders.Application/Siphoning/SiphonPlanner.cs` and
    `GasGiants.cs`, then `Automation/SiphonAutomationService.cs` and
    `Goals/Executors/SiphonAndSellGoalExecutor.cs`;
    `tests/SpaceTraders.Application.Tests/Siphoning/SiphonFixture.cs` holds X1-DC53's gas side (positions
    from the API, prices made up).
  - Files, in `SpaceTraders.Application` unless named:
    - new: `Siphoning/GasGiants.cs`, `Siphoning/SiphonPlanner.cs`, `Automation/SiphonAutomationService.cs`,
      `Goals/Executors/SiphonAndSellGoalExecutor.cs`, `Commands/Ships/SiphonResourcesCommand.cs`;
    - changed: `Automation/FleetRoles.cs` (`IsSiphoner`), `AutomationSwitches.cs` (the Siphon plan),
      `GameLoopService.cs`, `MarketAutomationPlanState.cs` (`PlanTypes.SiphonAutomation`),
      `Goals/ShipGoalExecutorService.cs`, `Health/ShipLeftIdleRule.cs`, `Mining/MiningPlanner.cs`
      (`IsDemanded` and `CanSellFrom` shared), `JournalEvents.cs`, `IAutomationMetrics.cs`,
      `Services/FleetStatusQueryService.cs`, `DependencyInjection.cs`; `SiphonAndSellGoal` and
      `ShipGoalKind` (Domain); `PrometheusMetricsService`, `PrometheusAutomationMetrics` (API);
      `DefaultSettingsSeed` (Persistence: the switch and `Siphon.MaxDrones`);
    - tests: `Siphoning/SiphonPlannerTests` and `SiphonFixture`, `Automation/SiphonAutomationServiceTests`,
      `Goals/SiphonAndSellGoalExecutorTests`, `Commands/SiphonResourcesHandlerTests` (new); additions to
      `AutomationSwitchesTests`, `GameLoopServiceTests`, `DefaultSettingsSeedTests`, `ShipRuleTests`,
      `FleetStatusQueryServiceTests`, `ShipGoalSerializationTests` (Domain) and `PrometheusMetricsTests`
      (API).
- **6.8 Spare time** (merged 2026-10-02 as projects#131, deployed by gembernodes#30; built on branch `ccr-3f080253-4o4wfu`, with your decisions D34–D37).
  Asked that day: "I'd like my command ship not to be idle. So can we add a interuptable mining/siphoning
  task that just fills up the cargo with whatever and sells it where it's relevant. If a more important job
  comes up such as trading or surveying it should stop mining, sell it's inventory and start on the new
  job." With the survey plan on, the command ship surveys and nothing else (D20); with a stock of surveys
  for every ore (D27), it waited.
  - Done:
    - **The order** (D34): survey, then trade, then mine or siphon. A new plan, spare time
      (`SpareTimePlanService`, switch `Automation.Plan.SpareTime.Enabled`, off), is bootstrapped last, so it
      gets the command ship only when the survey and trading plans left it free. It gives a trip to every
      ship that gathers in its spare time (`FleetRoles.GathersInSpareTime`): a surveyor, with the survey plan
      on, with a mining laser or a gas siphon, a hold and a tank. That is the command ship; with the survey
      plan off it is a miner, as before.
    - **The trip** (`GatherAndSellGoal`, `GatherAndSellGoalExecutor`): at the nearest asteroid or gas giant it
      can work and reach that yields a good a market it can carry it to buys (D35, `GatherPlanner`): XB5C,
      where it surveys, in practice. It mines (`ExtractResourcesCommand`, new) or siphons
      (`SiphonResourcesCommand`) once per cooldown, without surveys, which stay for the drones, keeping every
      good a reachable market buys (D33's rule) until the hold is full. Then it sells one good at a time,
      each where it fetches most after fuel (D36): it records the sale in its goal, flies there through
      refuelling stops, docks, sells in batches and fetches the market again (D25). A full hold sells even
      where the sale doesn't pay for its fuel; after that, what doesn't pay stays aboard for the next trip.
      The goal ends, and the plans choose again.
    - **A survey interrupts it** (D37): the survey plan treats a spare-time trip that fills its hold as free
      and replaces it with the survey; the hold stays aboard, and the next trip fills it on.
    - **A trade interrupts it** (D34): with the spare-time plan on, the trading plan takes the command ship,
      free or on a trip that fills its hold, for a route that waits for it once its hold is sold, after the
      other traders have chosen. It judges the route from where selling the hold leaves the ship
      (`GatherPlanner.AfterSellingHold`), so the route is still lucrative once the hold is sold and the ship
      doesn't turn back to gathering on the way. The ship sells its hold first, one good a trip, by the rule
      held cargo is sold by (`TradeRoutePlanner.TryFindBestCargoSale`, now shared), then takes its route.
      Without such a route the trading plan leaves the ship and its hold to the spare-time plan (D37).
    - **Safe interruptions** (`SpareTimeInterruption`): only while the trip fills its hold, only when no
      goal step of the ship runs (B46: a step that turns the trip to selling would write it back over the
      new goal), and only when the ship as stored isn't in flight, so its arrival still matches the goal
      that flew it (B17). Otherwise a later tick tries again.
    - **Visibility:** journal kinds `GatheringStarted` (`Method` `mines` or `siphons`) and
      `GatheringInterrupted` (`Reason` `survey` or `trade`, `Units` aboard); extractions and siphons log
      `Extracted` and `Siphoned` with `Target` `whatever sells`. The yield counts in
      `spacetraders_extracted_units_total`, so the dashboard's mined panels show it with no gembernodes
      change, but a spare-time extraction isn't counted in `spacetraders_extractions_total`: the survey
      statistics would read an extraction without a survey, which it is by design, as a sign of too few
      surveys. The fleet view says "mining in its spare time", "siphoning in its spare time" or "selling …".
      The plan's state (`plan_states`, `SpareTime`) lists each such ship, what it does and its trip's source;
      `ShipLeftIdle` (D13) counts a ship it lists with a source as having work.
    - **The switch brings it all** (D9): with the spare-time plan off, the command ship surveys and waits as
      before, and doesn't trade (D20).
    - Warnings fixed on the way, in files this slice touches: `FleetStatusQueryService` reads the time from
      `TimeProvider` (S6354), and `FleetStatusQueryServiceTests` lost 13 QW0021 and 2 S8969 warnings. Left:
      QW0028 on `SpareTimePlanState.PlanId`, a `Guid` like every plan state's.
  - Noticed (not changed):
    - **A survey may wait one cooldown:** mining and surveying share the ship's cooldown, so a survey that
      comes up right after an extraction waits for it (about 70 seconds in the soak test); before, the
      ship was idle and surveyed at once.
    - **Spare-time ore is sold, never delivered:** with the survey plan on the command ship isn't a miner,
      so the contract never takes it (D20, D23), even when the contract wants the ore it holds.
    - **Shared markets:** it sells at the markets the drones sell at (H51, F49 near XB5C), which moves their
      prices for the drones' trips too.
    - **Travel time isn't in the sale** (as in 6.5): with a 400-unit tank fuel is cheap, so a good may go to
      a far market for a few credits more per unit.
    - **Cargo ships:** the command ship takes routes only after the other traders, and doesn't count
      towards "every trader has a trip" (D21), so it neither delays nor triggers a cargo ship purchase.
    - **Selling the hold for a trade goes good by good** through the trading plan, so a survey that comes
      up between two sales comes first (survey > trade).
    - **Log volume:** an `Extracted` or `Siphoned` line per extraction, about 1,200 a day, as for a drone.
  - To switch it on: `PUT /settings/Automation.Plan.SpareTime.Enabled` with `{"value": "true"}`. The survey
    plan must be on for the command ship to count as gathering in its spare time, and the trading plan for
    trades to come first; both are.
  - Done when: a full reset period with the spare-time plan on and no open anomaly for it.
  - **To understand this,** start with `SpaceTraders.Application/SpareTime/GatherPlanner.cs`, then
    `Automation/SpareTimePlanService.cs`, `Goals/Executors/GatherAndSellGoalExecutor.cs` and
    `Automation/SpareTimeInterruption.cs`, with `TradeInsteadOfGatheringAsync` in
    `Automation/TradingAutomationService.cs`; `tests/SpaceTraders.Application.Tests/SpareTime/SpareTimeFixture.cs`
    holds X1-DC53's middle and its gas side.
  - Files, in `SpaceTraders.Application` unless named:
    - new: `SpareTime/GatherPlanner.cs`, `Automation/SpareTimePlanService.cs`, `SpareTimePlanState.cs`,
      `SpareTimeInterruption.cs`, `Goals/Executors/GatherAndSellGoalExecutor.cs`,
      `Commands/Ships/ExtractResourcesCommand.cs`;
    - changed: `Automation/FleetRoles.cs` (`GathersInSpareTime`, `HasMiningLaser`), `AutomationSwitches.cs`
      (the SpareTime plan), `GameLoopService.cs`, `SurveyPlanService.cs` (D37), `TradingAutomationService.cs`
      (D34), `Trading/TradeRoutePlanner.cs` (`TryFindBestCargoSale`), `Mining/MiningPlanner.cs`
      (`IsSellableFrom`, shared with `Commands/Ships/SiphonResourcesCommand.cs`),
      `Goals/ShipGoalExecutorService.cs`, `Health/ShipLeftIdleRule.cs`, `JournalEvents.cs`,
      `Interfaces/IAutomationMetrics.cs`, `Services/FleetStatusQueryService.cs`, `DependencyInjection.cs`;
      `GatherAndSellGoal` and `ShipGoalKind` (Domain); `PrometheusMetricsService` (API); `DefaultSettingsSeed`
      (Persistence: the switch);
    - tests: `SpareTime/GatherPlannerTests` and `SpareTimeFixture`, `Automation/SpareTimePlanServiceTests`,
      `Goals/GatherAndSellGoalExecutorTests`, `Commands/ExtractResourcesHandlerTests` (new); additions to
      `SurveyPlanServiceTests`, `TradingAutomationServiceTests`, `TradeRoutePlannerTests`,
      `AutomationSwitchesTests`, `GameLoopServiceTests`, `DefaultSettingsSeedTests`, `ShipRuleTests`,
      `FleetStatusQueryServiceTests`, `ShipGoalSerializationTests` (Domain), `DiValidationTests` and
      `PrometheusMetricsTests` (API).

- **6.9 Roles** (built 2026-10-02 on branch `claude/ship-role-profitability-ghjzlb`, in projects and gembernodes, with your
  decisions D38–D42). Asked that day: "Please help me do some refinement on the ship's behavior. Each ship should have a
  set of potential roles. … A ship should occasionally consider whether it's role is still the best thing it can do. …
  Goal is to have every ship be the most profitable it can be by comparing each role it has with the potential profits it
  can make." And, during the work, D42: cargo nothing will sell is sold where that pays, or jettisoned.
  - Done:
    - **Potential roles** (`FleetRoles.PotentialRoles`): survey with a surveyor (or a bought `SHIP_SURVEYOR`, whose mounts
      startup sync records later); mine with a mining laser, a hold and a tank; siphon with a gas siphon, a hold and a tank;
      trade with a hold and a tank. A probe has none. A role counts only while its plan is on; mining also while the
      contract wants ore.
    - **The role board** (`RolePlanService`, new switch `Automation.Plan.Roles.Enabled`, off; bootstrapped after the scout
      plan and before the others) gives every ship one role (`RolePlanner`, no I/O):
      1. a ship with one role takes it (`only_role`);
      2. surveys first (D38): in a system with a ship that can only survey, it surveys and the others don't; otherwise the
         ship that can survey with the least to lose (its best other trip earns least per hour) surveys, while another
         ship there can mine, since surveys are for miners (`survey_first`). It keeps the role unless another would lose
         less by more than the head start. With the spare-time plan on it trades or gathers when it has nothing to survey
         (D34), as the command ship does now;
      3. while the contract wants ore, every other ship that can mine mines (`contract`, D40);
      4. the rest share the work for the most credits per hour across the fleet: each takes one trip (or none), no two the
         same trade route (D18) or the same mining or siphon opening (`most_profitable`). It is the assignment problem,
         solved exactly (`Assignment`, the Hungarian method), so the work goes where it earns the fleet most, not to the
         first ship that wants it. A ship's current role counts `Roles.HeadStartPercent` (20) more (D41). A ship without a
         trip keeps its role (`no_work`).
    - **The estimates** (`RoleEstimator`, no I/O): for each role a ship could take but surveying, the trips its plan would
      offer, best per hour first: every lucrative route (D14), its profit after fuel; every mining target (D28's), a full
      hold of the target ore, filled at the ship's rate times the ore's share of the extractions (its survey's, or one of
      the asteroid's ores), less fuel; every siphon target, a full hold of the gases a market buys (D33), the trip's own gas
      at its market and each other gas where it counts most (D49). Each adds the production chains' share (D39, `ChainValues`): a good sold to a market that imports it
      and exports a pricier good made from it counts `Roles.ChainValueSharePercent` (50%) of the price difference (what the
      market charges for each, as D15 compares them), and that share again of the next step at the market in the system that
      makes the most of it; each step times how short its market is of the input (SCARCE 1, LIMITED ¾, MODERATE ½, HIGH ¼,
      ABUNDANT 0); at most what the trip earns on a unit, its price for a mined or siphoned good, its margin for a traded one
      (D49). Time: the flights in CRUISE as the API reckons them (15 seconds plus the distance times 25 over the
      engine's speed, 9 when it isn't cached), 10 seconds a landing, and the cooldowns to fill the hold, half a tick after
      each.
    - **How fast a ship fills its hold** (`GatheringRates`): every extraction and siphon records its yield and cooldown; a
      ship's last 10 give its rate, else the average of the other ships of the kind, else 3 units every 70 seconds (the soak
      test's drone extracted every 71 seconds; the command ship's Mining Laser II has strength 5). In memory; the plan state
      keeps them, and the first evaluation after a restart takes them back.
    - **When** (D41): at the first pass after a start, every `Roles.ReconsiderMinutes` (10), and at once when a ship joins,
      a plan is switched, the contract starts or stops wanting ore, or a ship with a choice of roles (not the one that
      surveys, which waits for surveys by design) has had no work for a minute, at most once a minute. A new role takes
      effect when the ship's trip ends: the plans only give work to free ships.
    - **The plans** read one board (`FleetRoleBoard`): with the board on, the survey plan gives surveys to the survey role,
      the mining plan trips to the mining role, the siphon plan to the siphon role, and the trading plan routes to the trade
      role and to a miner or siphoner its plan had no trip for, as before; the spare-time plan takes the survey role's ship.
      The contract takes every ship that can mine but the one that surveys (D40), whatever its role, so it never waits for
      the next evaluation. A ship bought this tick waits one tick for its role. With the board off, the fixed rules of D20
      and D34 hold, unchanged.
    - **Purchases:** with the board on, the mining and siphon plans buy a drone only when the board would give it their
      role (`RoleAdvisor`: its best trip in the role earns at least as much per hour as in any other, from the shipyard). A
      drone that would earn more trading would trade, and the plan, finding no free miner, would buy the next, and the next.
    - **Dead cargo** (D42, `HeldCargo`, `CargoJettison`): a free trader whose hold has nothing that pays for its sale after
      fuel jettisons it before it takes a route (it used to stay aboard for good), but for the contract's ore on a ship that
      mines for the contract. A surveyor with nothing to survey and no spare-time trip to fill its hold on sells its hold
      where that pays (a held-cargo trip), and jettisons the rest: the 7 copper SPECTER-1 carried since its contract work
      (6.4's Noticed). A spare-time ship with a full hold that no market it can reach buys jettisons it, and gathers again.
      Journal `CargoJettisoned` (`Reason` `no_buyer` or `not_worth_the_fuel`); counted in
      `spacetraders_jettisoned_units_total`. A jettison the API refuses is a warning, and waits 10 minutes before the next
      try (`JettisonRetries`).
    - **Visibility:** journal kind `RoleChanged` (`OldRole`, `NewRole`, `Reason`, and for a role chosen by profit
      `CreditsPerHour` and the `Job` that decided it). The state (`plan_states`, `Roles`) lists every ship's role, why,
      since when, what each role it could take would earn it per hour with the trip, and the rates it used. Metrics
      `spacetraders_ship_role_info{ship,role,reason}` and `spacetraders_ship_role_credits_per_hour{ship,role}`, exported
      while the board is on (switched off, the plans no longer read its roles); the dashboard's new "Roles" table shows
      them (gembernodes).
  - Noticed (not changed):
    - **The estimates are estimates:** prices as last seen; a full hold sold at one price, while each sale lowers it; a
      ship's first evaluations use the default rate until it extracts. The state and the dashboard show every estimate, so
      a wrong one shows: on the first day they were 30 to 300 times what ships earned, fixed for now by D49.
    - **Within a role the plans still choose by their own rules** (D15: a route to a market that makes a pricier good
      first; D28: the market shortest of its ore first), so the trip a ship gets may not be the one its role was valued by. The chain value only weighs roles; ranking the plans' own trips
      by it would change D15 and D28. Yours to call.
    - **One ship surveys per system** (besides every ship that can only survey), and only while another ship there can
      mine. With many drones far apart, one surveyor may not keep up; the board doesn't measure that.
    - **Spare time:** what doesn't pay for its fuel after a spare-time sale stays aboard for the next trip, as D36 has it;
      D42 jettisons a spare-time hold only when it is full and no market it can reach buys any of it.
    - **With the trading plan off,** nothing sells or jettisons a free ship's cargo: D42 runs in the trading plan.
    - **Jettisoning while docked:** the API's documentation names no state for it, and the archived ship planner allowed it
      anywhere but in flight; the bot never did it docked before. A refusal would show as a warning, once every 10 minutes.
    - No plan buys a `SHIP_SURVEYOR`; one bought by hand reaches the bot at its next startup sync.
    - The board writes its state at each evaluation: every 10 minutes, and at most once a minute while a ship with a choice
      of roles has no work.
  - To switch it on: `PUT /settings/Automation.Plan.Roles.Enabled` with `{"value": "true"}`. The first evaluation follows
    at the next tick, and the "Roles" table and the journal's `RoleChanged` lines show what it decided.
  - Done when: a full reset period with the role board on and no open anomaly for it.
  - **To understand this,** start with `SpaceTraders.Application/Roles/RolePlanner.cs`, then `RoleEstimator.cs` and
    `ChainValues.cs`, `Automation/RolePlanService.cs` and `Roles/FleetRoleBoard.cs`; for D42, `Trading/HeldCargo.cs` and
    `TradeInsteadOfGatheringAsync`'s neighbours in `Automation/TradingAutomationService.cs`.
    `tests/SpaceTraders.Application.Tests/Roles/RolePlannerTests.cs` has your two examples.
  - Files, in `SpaceTraders.Application` unless named:
    - new: `Roles/FleetRole.cs`, `FleetRoleBoard.cs`, `RolePlanner.cs`, `RoleEstimator.cs`, `ChainValues.cs`,
      `Assignment.cs`, `GatheringRates.cs`, `RoleSettings.cs`, `RoleAdvisor.cs`; `Automation/RolePlanService.cs` (with
      `RoleBoardMemory`), `RolePlanState.cs`; `Trading/HeldCargo.cs`; `Services/CargoJettison.cs` (with
      `JettisonRetries`);
    - changed: `Automation/FleetRoles.cs` (`PotentialRoles`, `CanSurvey`, `CanMine`, `CanSiphon`, `EngineSpeed`),
      `AutomationSwitches.cs` (the Roles plan), `GameLoopService.cs`, `ContractPlanService.cs`, `SurveyPlanService.cs`,
      `MiningAutomationService.cs`, `SiphonAutomationService.cs`, `TradingAutomationService.cs`, `SpareTimePlanService.cs`,
      `ProbeDeploymentPlanService.cs`, `Health/ShipLeftIdleRule.cs`, `Commands/Ships/MineResourceVolumeCommand.cs`,
      `ExtractResourcesCommand.cs`, `SiphonResourcesCommand.cs`, `JournalEvents.cs`, `Interfaces/IAutomationMetrics.cs`,
      `DependencyInjection.cs`; `PrometheusAutomationMetrics`, `PrometheusMetricsService` (API); `DefaultSettingsSeed`
      (Persistence);
    - tests: `Roles/RolePlannerTests`, `RoleEstimatorTests`, `ChainValuesTests`, `AssignmentTests`, `GatheringRatesTests`,
      `FleetRoleBoardTests`, `RoleAdvisorTests`, `RoleBoardTestSupport`, `Automation/RolePlanServiceTests`,
      `Trading/HeldCargoTests`, `Services/CargoJettisonTests` (new); additions to `TradingAutomationServiceTests` (one
      rewritten: cargo no market buys is jettisoned now), `SpareTimePlanServiceTests` (one rewritten),
      `MiningAutomationServiceTests`, `SiphonAutomationServiceTests`, `SurveyPlanServiceTests`, `ContractMinersTests`,
      `ShipRuleTests`, `ExtractResourcesHandlerTests`, `SiphonResourcesHandlerTests`, `AutomationSwitchesTests`,
      `DefaultSettingsSeedTests`; `PrometheusMetricsTests` and `MetricsEndpointTests` (API).

- **6.10 The fleet's shape** (asked 2026-10-03, after the bot's first day on the cluster, with your decisions D43–D50).
  Asked: too many siphon drones and too few other ships; more trades; drones for minerals out of fuel range; the actual
  profit of each source; the price gap per good in the market tree; what each ship can do next to its role; a designated
  surveyor; and what selling an input does to what a market makes from it. Split in three, so each part stays reviewable.
  - **What the data said** (2026-10-03, the last 12 to 24 hours):
    - Net credits an hour after fuel (ledger): the command ship trading about 13,000; siphon drones 2,000–4,000;
      mining drones 1,000–3,000. A few drones made one-off trades of goods worth 50,000 with a 15-unit hold; some lost.
    - Each plan buys on its own, and every purchase keeps the 100,000 reserve: a drone (49,011–51,411) is affordable at
      about 150,000 credits, a probe (77,117) at 177,000, a light shuttle (114,225) at 214,000, a light hauler (354,210) at
      454,000. The credits peaked at 150,000–166,000 before each drone purchase, so 5 siphon drones and 2 mining drones
      were bought overnight, and no probe or cargo ship.
    - The role board's estimates were 30 to 300 times what the ships earned (D49).
    - 176 trades in 24 hours, 159 of them by the command ship; 102 taken because they feed production (D15), at a median
      538 credits against 3,386 (D44 keeps D15). Price gaps are thin: most goods under 50 a unit, SHIP_PLATING, SHIP_PARTS
      and MEDICINE excepted.
    - SCARCE or LIMITED minerals, and what a drone could do about them: near, COPPER_ORE (H51), SILICON_CRYSTALS (A3,
      H53) and QUARTZ_SAND (F49), all from XB5C; far, ALUMINUM, COPPER, IRON, QUARTZ and SILICON at B7, from asteroid B14
      (25 from B7); out of any drone's round trip, AMMONIA_ICE, GOLD_ORE, SILVER_ORE, PLATINUM_ORE and PRECIOUS_STONES
      (their nearest asteroids are 68 to 476 from their markets); DIAMONDS, which no asteroid yields; and the three gases,
      all from C38. The drones worked on four of the ores.
  - **6.10a Visibility and the role board's rates** (built 2026-10-03; dashboards in gembernodes#34):
    - What each ship can do, whatever the switches: `spacetraders_ship_capabilities_info{ship,can}` (`Survey, Mine,
      Siphon, Trade`, or `none`); the fleet and roles tables show it next to the role (gembernodes#34), as drones of both
      kinds report the registration role EXCAVATOR.
    - Profit per trip (D46): journal kind `TripEnded`; counters `spacetraders_trip_profit_credits_total`,
      `spacetraders_trip_loss_credits_total` and `spacetraders_trips_total` by `activity`; sales and purchases from the
      trip's own figures (`TripGoal.Earned`, `Spent`: a ledger row lands after the trip has ended), fuel from the ledger
      (`TripBook`). Every end books: sold, each early stop, `interrupted` (spare time taken over) and `runaway` (the
      circuit breaker). Contracts: deposit and payout as profit, each delivery's round-trip fuel as a loss. Not booked:
      contract round trips released without a delivery, goals an agent reset wipes, the earlier batches of a sale that
      fails partway; a trade bought before the deploy books its whole sale once; a trade that sells a held spare-time
      hold books it under `trade`. Dashboard: profit an hour by activity, stacked.
    - Units sold and bought per market and good (D50); dashboard: what we sell into a market an hour against the supply,
      trade volume and price of what it makes from it.
    - The role board's rates (D49).
    - The market tree's price gap per good, where to buy, where to sell (dashboard only, in gembernodes#34).
  - **6.10b The order ships are bought in** (built 2026-10-03 on branch `claude/spacetraders-purchase-order`; D43, D47, D48,
    D51). Asked: "I feel there are too many siphoning drones and not enough other ship types"; "I'd like at least 1 drone per
    mineral that is scarce or limited, then save up for cargo ships, then a mix based on if the minerals aren't going above
    LIMITED", the mix being "Alternate drones and cargo ships, but probes first"; a designated surveyor first (D47); and,
    during the work, a reserve that grows with the trading holds (D51).
    - Done:
      - **The order** (`PurchaseOrder`, `PurchaseNeeds`, D43): every plan that buys says on each pass what it would buy
        (`PurchaseNeed`: a tier, the ship, the shipyard, the price) and buys only when nothing comes first: the contract's
        drone, a surveyor, a drone per scarce mineral, the cargo ships of `Trade.ShipPurchases`, probes, then drones and
        cargo ships of the list's last type in turn. A need counts while its plan is on, and only while it can be met (its
        cap not reached, a known shipyard with a price for it). Until each plan that is on and could need something earlier
        has said what it needs within the last 2 minutes, nothing after it is bought: after a start, or a pause in which no
        plan ran (a 502 pauses them for 3 minutes), the probe plan, which runs before the survey, mining, siphon and trading
        plans, waits a tick; a plan that fails before it says holds the purchases after it. The order says who may buy; the
        reserve stays the purchase's own check.
      - **The turn** between drones and cargo ships goes to the kind not bought last: after the list's last cargo ship a
        drone, then a cargo ship, and so on. It reads the ledger's `ShipPurchase` rows (once a start) and the purchases of
        this process, which `ShipPurchaseService` records at once, as the ledger's row comes a moment later and the siphon
        plan, later in the same tick, would otherwise buy a second drone. Any drone counts, the contract's and a scarce
        mineral's too; a cargo ship is a type of the list or one of the game's freighters. A turn passes when the other
        kind has nothing to buy: a drone's first trip wouldn't serve a market short of its mineral, or a miner is free; a
        new cargo ship would have no lucrative route (judged with any credits, so a hauler that isn't affordable yet keeps
        its turn), or a trader has no trip. A turn that passed isn't made up later. Probes and surveyors don't take
        turns. (The design notes counted the purchases since the list's last cargo ship; a review found that an edited
        list, or a lost ledger row, then left it the drones' turn for good, and that passed turns came back as a run of
        one kind.)
      - **A cargo ship of the list is saved up for** whatever the routes (D43, "then save up for cargo ships"); it is
        bought, as before (D21), when every trader has a trip and the new ship would have a lucrative route with the
        credits left after it. Past the list, one more of its last type at a time, in turn with the drones: D21's "up to 2
        light haulers" became that.
      - **The surveyor** (D47): with the role board on, the survey plan buys a `SHIP_SURVEYOR` (33,905 at H52) for each
        system with a mining drone and no ship that can only survey. The board gives it the survey role (D38), which
        frees the command ship. The survey executor makes no check of its own, so a surveyor bought since the last restart,
        whose mount startup sync hasn't recorded, surveys.
      - **Coverage** (D48): the mining and siphon plans give a free drone a SCARCE or LIMITED mineral no drone's trip works
        on first, near before far, then D28's order; the journal's `MiningStarted` and `SiphonStarted` say `uncovered` when
        that came before D28's choice. They buy a drone (tier `Coverage`, without asking `RoleAdvisor`) while the system
        has fewer drones of the kind than SCARCE or LIMITED minerals a new drone could serve (`MiningPlanner.ScarceOres`,
        `SiphonPlanner.ScarceGases`): a count, so a drone between trips doesn't buy another. The role board keeps one
        drone gathering per such mineral after the contract (`coverage`): the drone whose trip works on it, else one that
        has the role, else the one with the least to lose. A drone beyond one per mineral is bought by D28's and D32's
        rules, in turn with the cargo ships, and asks `RoleAdvisor` as before.
      - **The reserve** (D51, `CreditReserve`): `BudgetPolicy` keeps the floor plus 1,000 a unit of hold on the ships that
        trade, read from the cached fleet and the role board at every evaluation. New setting
        `FleetExpansion.ReservePerTradingCargoUnit` (1,000); `FleetExpansion.MinCreditReserve` is seeded at 60,000.
      - **Visibility:** `spacetraders_purchase_need_credits{plan,tier,position,ship_type,shipyard}`, one series per plan
        that needs something, worth the ship's price (the lowest position is what the credits are saved for);
        `spacetraders_credit_reserve`; the probe plan's state says `WaitingForAnotherPurchase`; the role board's
        `coverage` reason.
    - After the deploy (yours): **set `FleetExpansion.MinCreditReserve` to 60,000.** The seed adds missing keys at every
      start, so `FleetExpansion.ReservePerTradingCargoUnit` arrives by itself, but a stored value is never overwritten: until
      then the reserve is 100,000 plus the command ship's 40,000, 140,000.
    - Noticed (not changed):
      - **A long saving phase:** after the surveyor (33,905), the list is 114,225 + 2 × 354,210 = 822,645, with the reserve
        rising to 140,000, 220,000 and then 300,000; then 25 probes for X1-DC53's 26 markets (77,117 at A2, more as each
        purchase moves the price) above a 300,000 reserve, before any drone beyond one per scarce mineral. Today one per
        scarce mineral is already there (four mining drones for COPPER, SILICON and QUARTZ near the middle; seven siphon
        drones for three gases), so the next purchases are the surveyor, then the shuttle.
      - **A trader without a trip holds the list back:** a cargo ship is bought only when every trader has a trip (D21),
        and nothing after it while it is to buy ("save up", D43). Freed by the surveyor, the command ship trades: while it
        finds no lucrative route, the shuttle waits, and so do probes and drones. With the role board on, a drone that its
        mining or siphon plan had no trip for, and that finds no route, counts as such a trader too.
      - **During a contract** the mining plan buys nothing (D23), so a drone for a scarce ore waits for the contract's end,
        while cargo ships and probes, after it in the order, may be bought.
      - **Coverage counts drones, not trips:** a drone kept for a mineral may be on another trip; the trip-level rule moves
        drones to uncovered minerals as their trips end, so with as many drones as minerals they rotate rather than stay.
      - **One system:** needs are per plan, not per system; with ships in several systems a plan reports its first need.
      - `ContractPlanService` holds a `LegacyContractShipPurchaseService` that nothing constructs (dead code).
    - Done when: the surveyor and the shuttle are bought in that order, the probes after the list, and no drone beyond one
      per scarce mineral before the probes; `spacetraders_purchase_need_credits` shows what waits.
    - **Met** (checked on 2026-10-04): after the deploy (11:37Z on 2026-10-03) the purchases were the surveyor SPECTER-F
      (12:50Z), five drones the role board kept for `coverage` (SPECTER-10, -11, -12, -14, -15), the second survey ship
      SPECTER-13 (16:32Z, D55), the shuttle SPECTER-16 (20:33Z) and the hauler SPECTER-17 (04:51Z on 2026-10-04); no probe and
      no further drone, and none at all after D58 (20:26Z), as the order says.
    - **To understand this,** start with `SpaceTraders.Application/Services/PurchaseOrder.cs`, then each plan's need:
      `DroneNeedAsync` in `Automation/MiningAutomationService.cs` and `SiphonAutomationService.cs`, `BuyCargoShipAsync` in
      `TradingAutomationService.cs`, `SurveyorNeedAsync` in `SurveyPlanService.cs`; for coverage, `UncoveredFirst` in
      `Mining/MiningPlanner.cs` and `Keepers` in `Roles/RolePlanner.cs`; for D51, `Orchestration/CreditReserve.cs`.
    - Files, in `SpaceTraders.Application` unless named:
      - new: `Services/PurchaseOrder.cs` (`IPurchaseOrder`, `PurchaseOrder`, `PurchaseNeeds`, `PurchaseTier`,
        `PurchaseKind`, `PurchaseNeed`, `ReportedNeed`, `PurchaseRecord`), `Orchestration/CreditReserve.cs`;
      - changed: `Automation/ContractPlanService.cs`, `ProbeDeploymentPlanService.cs`, `ProbeDeploymentPlanState.cs`
        (`WaitingForAnotherPurchase`), `SurveyPlanService.cs`, `MiningAutomationService.cs`, `SiphonAutomationService.cs`,
        `TradingAutomationService.cs`, `RolePlanService.cs`, `FleetRoles.cs` (`IsMiningDrone`); `Mining/MiningPlanner.cs`,
        `Siphoning/SiphonPlanner.cs` (`UncoveredFirst`, `ScarceOres`, `ScarceGases`); `Roles/RolePlanner.cs`
        (`MineralCoverage`, `coverage`); `Orchestration/BudgetPolicy.cs`; `Services/ShipPurchaseService.cs`;
        `Commands/Fleet/PurchaseShipCommand.cs`; `Interfaces/IAutomationMetrics.cs`; `DependencyInjection.cs`;
        `PrometheusAutomationMetrics`, `PrometheusMetricsService` (API); `DefaultSettingsSeed` (Persistence);
      - tests: `Services/PurchaseOrderTests` and `OpenPurchaseOrder` (new); additions to `ContractPlanServiceTests`,
        `ProbeDeploymentPlanServiceTests`, `SurveyPlanServiceTests`, `MiningAutomationServiceTests` (two rewritten: the D28
        purchase now needs a drone per scarce ore first), `SiphonAutomationServiceTests` (two rewritten, the same),
        `TradingAutomationServiceTests` (one rewritten: past the list, one more hauler in turn), `RolePlanServiceTests`
        (one rewritten: the only drone is kept for a scarce ore), `RolePlannerTests`, `MiningPlannerTests`,
        `SiphonPlannerTests`, `ShipPurchaseServiceTests`, `BudgetPolicyTests`, `DefaultSettingsSeedTests`;
        `PrometheusMetricsTests`, `MetricsEndpointTests` (API).
  - **6.10c Drift to minerals out of fuel range** (built 2026-10-03 on branch `claude/spacetraders-drift`; D45). Asked: "I'd
    like a way to add mining/siphoning drones for the minerals outside of fuel range, e.g. by having a drone drift to the
    marketplace that buys the mineral first, then refueling and resuming normal behavior."
    - Done:
      - **Far targets** (`MiningPlanner.MiningTargets`, `SiphonPlanner.SiphonTargets`): a market out of the ship's CRUISE
        reach (no chain of fuel markets gets it there) that sells fuel counts, gathered at the asteroid or gas giant
        nearest it within a CRUISE round trip of it (`IsWithinRoundTrip`: out with a full tank, back with what is left, or
        a full tank where the source sells fuel), marked `Far`. It ranks after every reachable target of its supply level
        (D28), and among the minerals no drone works on (D48) after those in reach. In `MiningFixture` B7's gold and copper,
        from B14, now come after F49's SCARCE silicon and quartz and before H51's LIMITED copper; iron at B7, whose nearest
        asteroid (B13) is 48 away, 96 there and back, doesn't count.
      - **The trip** (`MineAndSellGoal.Drifting`, `SiphonAndSellGoal.Drifting`, `DriftStepAsync`, `GoalFlight.DriftAsync`):
        it navigates to the market asking for DRIFT (1 fuel, about ten times slower) and journals `DriftStarted`; the
        arrival docks it there and refreshes the market; the next step records that the drift has ended, and the trip goes
        on from there. Its next flight refuels (the orbit fills the tank at a fuel market) and asks for CRUISE. Never to
        the asteroid in DRIFT: the fallback would have left the drone there, in DRIFT (B47).
      - **The flight mode a navigation asks for** (`NavigateToWaypointCommand.FlightMode`, `FlightModeSubCommand`): set in
        orbit, after the orbit's refuel, just before the flight, so it never depends on whether the API lets a docked ship
        change mode; the API is called only when the cached mode differs. `GoalFlight` (mining, siphon, survey, spare time)
        and the trade executor ask for CRUISE, so a ship left in DRIFT, after a drift or by the fallback (B47, in part),
        flies in CRUISE again. The API's spec lists no restriction on the call; its response is `{nav, fuel, events}`, as
        the client reads it.
      - **A trip counts the fuel left at its source** (`MiningPlanner.Arrival`): the haul was checked with a full tank at
        the asteroid (`CanSellFrom`), true from XB5C, which sells fuel, but not from a far market's asteroids: from B7 a
        drone could have been sent to an asteroid 48 away, with 32 left for the 48 back, and drifted back. A trip now counts
        only when the ship can carry its hold to the market in CRUISE with the fuel it has there, as D45's "mines from
        that market in CRUISE" needs. In X1-DC53's middle nothing changes.
      - **New and free drones** (D45): `ScarceOres` and `ScarceGases` count the far minerals, so the coverage tier (D48)
        buys a drone for an ore only a far market is short of, and the role board keeps one gathering it (`coverage`). The
        plans' openings list a free drone that would drift to them (`MiningPlanner.CanTake`, read by `ShipLeftIdle`).
      - **The role board's estimate** (`RoleEstimator`): a far trip adds its drift (`DriftSeconds`: 15 seconds plus the
        distance times 250 over the engine's speed) and the unit of fuel bought back where it lands, so the board neither
        skips it nor counts it as near. Its job reads `…, drifting there first`. From the middle to B7 that is about 2.5
        hours, so a far trip rarely wins on its rate; the drone kept for a far mineral takes it anyway.
      - **Visibility:** the fleet view says `drifting to X1-DC53-B7 to mine GOLD_ORE`, as does the WebUI's fleet status;
        the journal has `DriftStarted`, and `FlightModeSubCommand` logs each change of mode.
    - What to expect, from the data of 2026-10-03 (not checked against the live markets): B7 was SCARCE or LIMITED in
      aluminum, copper, iron, quartz and silicon, all from B14. Aluminum and iron weren't short near the middle, so the
      ores a drone could serve go from three to five: with four mining drones, the coverage tier buys one more, ahead of
      the cargo ships (D43), and one drone for each of the two ends up at B7 after a drift of about 2.5 hours. Siphon drones
      don't drift in X1-DC53: C38 has every buyer in reach.
    - Deployed by gembernodes#37 at 12:13Z on 2026-10-03, with projects#138. On the cluster the whole cycle ran by 13:30Z:
      the command ship, left with 14 fuel at B14 by B54's surveys, drifted to B7 (12:51Z), switched back to CRUISE there
      (12:54:29Z), mined iron at B14 and sold it at B7 (1,616 after fuel). SPECTER-4 (12:35Z), SPECTER-10, the coverage
      drone bought at 13:09Z, and SPECTER-A (13:36Z) drifted to B7.
    - Noticed (not changed):
      - **Drift back and forth:** supply comes first (D28), so a drone at B7 whose ores there have risen to MODERATE drifts
        back to a SCARCE market in the middle, and a free drone in the middle drifts to a SCARCE one at B7, 2.5 hours each
        way. That is the rule as decided; the coverage keepers keep one drone at each far mineral.
      - **Surveys before the drone gets there:** a ship in transit counts as at its destination, so while a drone drifts to
        B7 the survey plan may survey B14 for it, if the surveyor reaches B14 in CRUISE; those surveys can expire first.
        It happened at the first drift, worse than noted: the command ship also surveyed B37, where no drone can mine.
        That is B54, fixed in the follow-up below.
      - **Coverage counts a mineral, not a market** (D48): with SPECTER-10 bound for B7's silicon, silicon counted as
        covered, and SPECTER-A left the middle's silicon markets for B7's iron. Your call was D53, coverage per area: see the
        follow-up below.
      - **The contract plan takes any free miner** without checking that it can reach the contract's asteroid in CRUISE,
        and the contract's commands fly with the fallback (B47). Under D1 (one contract per reset) and D23 (no drone is
        bought while the contract mines) no drone has drifted while a contract wants ore; a slice that takes the next
        contract should check.
      - **B47 remains** for scouting and the contract's flights; the fallback itself still switches to DRIFT.
      - **Cost:** the far-target search adds about 20 ms to each ranking of mining targets on an 85-waypoint system (timed
        on a synthetic one, Release build; the ranking before it took about 30 ms), a few rankings a tick. The bot waits
        on the API's rate limit, not its CPU.
      - The analyzers ask for strongly typed ids on `ShipGoal.GoalId`, `DeliverCargoGoal.ContractId` and
        `NavigateToWaypointArrivedCommand.GoalId` (QW0028, QW0029): a change across the goal model, left.
    - Done when: a drone drifts to B7, mines there and sells there in CRUISE, and its later trips leave B7 in CRUISE.
    - **Met** (checked on 2026-10-04): SPECTER-4 drifted from H53 at 12:35Z on 2026-10-03, reached B7 at 15:00:45Z and
      switched to CRUISE at 15:00:52Z; from then on it mined at B14 and sold at B7, about 90 fuel a round trip, as did
      SPECTER-10 and SPECTER-A.
    - Tests: App 817, Domain 71, API 163.
    - **To understand this,** start with `MiningTargets` in `Mining/MiningPlanner.cs` (the far targets, `CanDriftTo` and
      `IsWithinRoundTrip`), then `DriftStepAsync` in `Goals/Executors/MineAndSellGoalExecutor.cs` with `GoalFlight.cs`,
      then the flight mode in `Commands/Ships/NavigateToWaypointCommand.cs` and `SubCommands/IFlightModeSubCommand.cs`.
    - Files, in `SpaceTraders.Application` unless named:
      - new: `Commands/Ships/SubCommands/IFlightModeSubCommand.cs`;
      - changed: `Mining/MiningPlanner.cs`, `Siphoning/SiphonPlanner.cs`, `Goals/Executors/GoalFlight.cs`,
        `MineAndSellGoalExecutor.cs`, `SiphonAndSellGoalExecutor.cs`, `TradeBetweenMarketsGoalExecutor.cs`,
        `Commands/Ships/NavigateToWaypointCommand.cs`, `Automation/MiningAutomationService.cs`,
        `SiphonAutomationService.cs`, `Roles/RoleEstimator.cs`, `Services/FleetStatusQueryService.cs`, `JournalEvents.cs`
        (`DriftStarted`), `DependencyInjection.cs`; `ShipGoal.cs` (Domain: `Drifting`); `PrometheusMetricsService` (API);
      - tests: `Commands/FlightModeSubCommandTests` (new); additions to `MiningPlannerTests` (three expectations rewritten:
        B7's ores count now, a drift away), `SiphonPlannerTests` and `SiphonFixture` (`MapWithAGasGiantNearF48`),
        `MiningAutomationServiceTests` (four rewritten: B7's gold and copper get drones too, so each keeps its point),
        `SiphonAutomationServiceTests`, `RolePlanServiceTests` (two rewritten: a sixth drone), `RoleEstimatorTests`,
        `MineAndSellGoalExecutorTests`, `SiphonAndSellGoalExecutorTests`, `TradeBetweenMarketsGoalExecutorTests`,
        `NavigateToWaypointHandlerTests`, `FlightLogLinesTests`, `AlreadyAtDestinationLoopTests`,
        `FleetStatusQueryServiceTests`, `ShipRuleTests` (a comment); `ShipGoalSerializationTests` (Domain);
        `PrometheusMetricsTests` (API); docs: `docs/HOW_IT_WORKS.md`, the `st-investigate` skill (a drift takes hours).
    - **Follow-up, B54** (projects#140, deployed by gembernodes#38): the survey plan counts an asteroid where a miner could
      mine it for the market, as the mining plan reckons the trip (in CRUISE, there and on to the market with the fuel
      left), not where a miner merely reaches it; and a drone still drifting to a far market counts once it is there. To
      understand it, start with `SurveyTargets` in `Mining/MiningPlanner.cs`, then `MinersAsync` in
      `Automation/SurveyPlanService.cs`. Tests: `MiningPlannerTests` and `SurveyPlanServiceTests` (one new each).
    - **Follow-up, B55** (projects#141, deployed by gembernodes#39): the survey plan's state lists the surveyors that can
      reach each target (`CandidateShipSymbols`), and `ShipLeftIdle` counts a target as work only for those. Tests:
      `ShipRuleTests`, `SurveyPlanServiceTests` (one new each).
    - **Follow-up, D52** (projects#142, deployed by gembernodes#40 at 14:00Z): a ship that can only survey
      (`FleetRoles.CanOnlySurvey`), with every ore it reaches at its stock, surveys on: the target it reaches with the
      fewest usable surveys first. `ShipLeftIdle` counts any target it reaches as work for it. Start with the surveyor
      loop in `Automation/SurveyPlanService.cs`. Tests: `SurveyPlanServiceTests`, `ShipRuleTests` (one new each; B55's
      test keeps its point without a target the surveyor reaches).
    - **Follow-up, D53** (branch `claude/spacetraders-coverage-per-area`): coverage per area.
      - **A trip covers its mineral near the market it sells at** (`CoveringTrip`, `MiningPlanner.Covers`): at the markets
        its ship reaches in CRUISE from there, through refuelling stops, leaving with a full tank. The mining and siphon
        plans list the trips under way as `CoveringTrip`s (the mineral, the market, the ship's tank), and `UncoveredFirst`
        puts first the SCARCE or LIMITED targets no trip covers. A drone at B7 doesn't cover the middle, nor one in the
        middle B7; the command ship's 400-unit tank reaches both, so its trip covers both.
      - **The coverage tier counts areas** (`ScarceOres`, `ScarceGases`, `MiningPlanner.Areas`): each SCARCE or LIMITED
        mineral once per area, an area being the markets short of it that a new drone flies between in CRUISE (two markets
        share one when either reaches the other). So it buys a drone per scarce mineral and area, ahead of the cargo ships
        (D43), as before without asking the role board.
      - **The role board keeps one drone per mineral and area** (`MineralCoverage.MarketSymbols`): the areas are those of
        the system's drones of the kind (the smallest tank among them); the drone kept for one is the one whose trip covers
        it, else as before.
      - **What to expect,** from the markets as the bot last saw them at 14:30Z (B7's at 13:30Z): the middle is short of
        aluminum (H51) and silicon (A3, F49, H53), and B7 of aluminum, copper, iron, quartz and silicon, all from B14 (B7's
        gold, silver and platinum come from B37 or B10, beyond a drone's round trip; nothing in a drone's reach yields the
        ammonia ice, diamonds or precious stones that H53, J58 and H54 want). That is seven ores and areas for five mining
        drones, against five ores before, so the coverage tier wants two more drones; a free drone in the middle, or a new
        one, takes whichever of the middle's two ores has no drone, and the drones at B7 keep to B7's ores, as uncovered ores
        in reach come before those a drift away. Siphon drones reach every gas market in X1-DC53 from C38, one area, so nothing
        changes for them.
      - To understand it, start with `UncoveredFirst` and `Areas` in `Mining/MiningPlanner.cs` (`CoveringTrip` and
        `MineralArea` at the end of the file), then `Minerals` in `Automation/RolePlanService.cs`.
      - Tests: App 832 (9 new: `MiningPlannerTests` 3, `SiphonPlannerTests` 2, `MiningAutomationServiceTests` 2,
        `SiphonAutomationServiceTests` 1, `RolePlannerTests` 1; rewritten to keep their point with the extra area:
        `MiningAutomationServiceTests` 3, `RolePlanServiceTests` 2; the scarce ores and gases are listed per area), Domain
        71, API 161 (and 4 skipped, as on main).
        On the old code the new tests of the plans and the board fail as the cluster did: the free drone drifts to B7's
        gold instead of mining the middle's copper, and the purchase comes as `Alternating`, not `Coverage`.
      - Deployed by gembernodes#41 (merged before the CI run had pushed the images: the API, which is replaced rather than
        rolled, was down 14:38:55–14:40:33Z). The coverage tier bought SPECTER-11 at 15:16:59Z, which the board kept for
        coverage and which took the middle's silicon (XB5C for H53).
    - **Follow-up, D54** (branch `claude/spacetraders-surveyor-follows-drones`): the survey ship works where most drones mine.
      - **Where it works** (`MiningPlanner.TryFindBusierArea`, `SurveyorMove`): the survey plan counts the mining drones by
        where they work, the market their trip sells at (a drone still drifting there included), else where they are;
        its own area is what it reaches in CRUISE, and the drones beyond that group into areas as it would fly between
        them (`Areas`' grouping). An area with more drones than its own gets it: the market there, among those that sell
        fuel, where the most drones work. A tie keeps it where it is. Only a ship that can only survey moves.
      - **The move** (`MoveToWaypointGoal.Drifting`, new `MoveToWaypointGoalExecutor`): the goal existed with no executor.
        One flight, in DRIFT out of CRUISE reach (`DriftStarted`), else in CRUISE; at the target it ends, and the survey
        plan gives the ship its surveys there (D27, D52). The fleet view says `drifting to X1-DC53-B7`; the plan logs
        "moves to … (D54)" with both counts.
      - **What to expect:** at 15:30Z B7 had four mining drones (SPECTER-4 mining, -10, -A and -9 drifting there) against
        two in the middle (SPECTER-3, SPECTER-11), so SPECTER-F drifts to B7 at its first free tick after the deploy, about
        2.5 hours, and surveys B14 from there. The middle's drones then mine
        without surveys, by design; the command ship doesn't survey (D38).
      - To understand it, start with `TryFindBusierArea` in `Mining/MiningPlanner.cs`, then the surveyor loop in
        `Automation/SurveyPlanService.cs` and `Goals/Executors/MoveToWaypointGoalExecutor.cs`.
      - Tests: App 842 (10 new: `MiningPlannerTests` 4, `SurveyPlanServiceTests` 2, `MoveToWaypointGoalExecutorTests` 4),
        Domain 72 (1 new: `ShipGoalSerializationTests`), API 161 (and 4 skipped; `PrometheusMetricsTests` and
        `FleetStatusQueryServiceTests` cover the drifting survey ship's words).
    - **Follow-up, D55** (branch `claude/spacetraders-surveyor-per-area`, on top of D54's): a survey ship per area.
      - **The order** (`PurchaseTier.SurveyorPerArea`, 4): one more surveyor, after the drones per scarce mineral and area
        and before the cargo ships; the cargo ships, probes and turns moved one place down (5, 6, 7), as does `position` in
        `spacetraders_purchase_need_credits`. The dashboard reads the labels as they come.
      - **The need** (`SurveyorNeedAsync`, `MiningPlanner.CountAreas`): a system with no ship that can only survey needs
        its first (`Surveyor`, D47); one with fewer of them than areas with mining drones (the drones where they work,
        grouped with the survey ships' smallest tank) needs one more (`SurveyorPerArea`), at the same shipyard.
      - **Where each works** (`TryFindBusierArea` with the other survey ships): the plan notes where each ship that can only
        survey works, where it is or where it is moving to; an area with one of them is taken. A survey ship alone in its
        area moves only to an area that isn't taken and has more drones than its own (D54); one that shares its area moves
        to the busiest area with drones that isn't taken. The log says "moves to …, where N mining drones work and no
        other survey ship, against M in its own area[, which another survey ship works in] (D54, D55)".
      - **What to expect** after D54 and D55 are deployed, from 15:45Z: SPECTER-F drifts to B7 (four drones against three in
        the middle), and the survey plan wants a second survey ship (33,905 at H52) for the middle, bought once the
        coverage drones are (the third, for B7's aluminum, may still be wanted) and the credits allow; it surveys XB5C
        where it is bought. If SPECTER-F is still in the middle when the second arrives, one of the two drifts to B7.
      - To understand it, start with `TryFindBusierArea` and `CountAreas` in `Mining/MiningPlanner.cs`, then
        `SurveyorNeedAsync` and the surveyor loop in `Automation/SurveyPlanService.cs`, then `PurchaseTier` in
        `Services/PurchaseOrder.cs`.
      - Tests: App 849 (7 new: `MiningPlannerTests` 3, `SurveyPlanServiceTests` 3, `PurchaseOrderTests` 1; D54's planner tests
        pass no other survey ship), Domain 72, API 161 (and 4 skipped; the positions in `PrometheusMetricsTests` follow the
        new order).
    - **Follow-up, D56** (branch `claude/spacetraders-full-holds`): full holds only, in one purchase and one sale.
      - **A route** (`TradeRoutePlanner.TakesFullHold`, `TryEvaluateFrom`): its units are the ship's free hold; it counts only
        when the buy market's and the sell market's trade volumes are both at least that, and the credits for cargo, the
        trip's fuel kept back, pay for all of it. The held-cargo sale (D42) is unchanged: it sells what is aboard.
      - **At the buy market** (`TradeBetweenMarketsGoalExecutor`): the trip is worked out again with the prices just
        fetched; a market that no longer trades the full hold at once drops it (`TradeDropped`, `Reason` `not_full_hold`),
        as do too few credits (`not_possible`). The purchase is the whole hold.
      - **Saving up** (`FullHoldSavings`, `TradingAutomationService.NoteSaving`): a free trader whose best route, credits
        aside, is a hold the credits don't pay for logs "saves up for a full hold of … (D56)" and takes the best hold it
        can pay for meanwhile, or none. `BudgetPolicy` adds the dearest saving to the credit reserve, and so does
        `spacetraders_credit_reserve`. The saving ends when that hold is bought, when the trader's best route is one it
        can pay for, or when the ship no longer trades; a restart forgets it until the plan's next pass.
      - **What to expect** after the deploy: at 18:25Z on 2026-10-03, 84 of the 124 goods the markets of X1-DC53 listed,
        fuel aside, traded at least 40 at once, and 26 at least 80 (158 pairs of markets for a 40-unit hold, 18 for an
        80-unit one). The command ship (40) trades only those routes; EQUIPMENT from K85 (43 at once) has no buyer that
        takes 40 (20 each), and SHIP_PARTS (15 at D41, 6 to 13 where it is sold) fills no hold, not even a drone's 15. Its
        purchases are all 40 units; with about 120,000 credits a 40-unit hold of a good dearer than about 3,000 a unit is
        saved up for, and ship purchases wait meanwhile (the reserve shows it). At 18:25Z's prices the best 40-unit routes
        were FABRICS from D43 (2,622 a unit, 104,880 the hold) to D41 and K85, 70 and 48 a unit more, then IRON from H51
        (22) and PRECIOUS_STONES from J58 (18), against `Trade.MinProfitPerUnit` 5. Drones trade only 15-unit holds.
      - To understand it, start with `TakesFullHold` and `TryEvaluateFrom` in `Trading/TradeRoutePlanner.cs`, then
        `NoteSaving` in `Automation/TradingAutomationService.cs`, `Trading/FullHoldSavings.cs` and
        `Orchestration/BudgetPolicy.cs`.
      - Tests: App 857 (6 new, 3 rewritten: `TradeRoutePlannerTests`, `TradeBetweenMarketsGoalExecutorTests`,
        `TradingAutomationServiceTests`, `BudgetPolicyTests`; the trading fixture's EQUIPMENT and MEDICINE trade 40 at once,
        and its credits are 250,000), Domain 72, API 162 (and 4 skipped; the credit-reserve theory has a case with a
        saving).
    - **Follow-up, D57** (branch `claude/spacetraders-reserve-trip-credits`): credits held back for a trip.
      - **What a trip holds back** (`TradeBetweenMarketsGoal.ReservedCredits`, `Trading/TripReservations.cs`): its units at
        the buy price it was chosen with, from the moment the trading plan starts it until its cargo is aboard; nothing
        once bought, blocked or done. It is stored with the goal, so a restart keeps it, and a trip stored before D57
        holds back nothing.
      - **Who leaves it:** the trading plan gives free traders only the credits no trip on its way to buy holds back;
        the trade executor at the buy market spends its own and those no other trip holds back (the goal store reads the
        fleet's trips at once, `GetActiveTradeGoalsAsync`); `BudgetPolicy` adds the holds to the credits every ship
        purchase must leave, and `spacetraders_credit_reserve` shows them. A trader that sets off for the hold it saved
        up for (D56) saves up no more: the trip's hold takes its place.
      - **What to expect:** no trade trip dropped `not_possible` at its buy market because another trader spent the
        credits; while the command ship flies to buy a dear hold, the reserve shows it and ship purchases wait. A price
        that rose since the trip was chosen (COPPER at H51: chosen at 270, bought at 286 on 2026-10-03) is paid from
        the credits no trip holds back.
      - To understand it, start with `Trading/TripReservations.cs`, then `AssignRoutesAsync` and `RouteGoal` in
        `Automation/TradingAutomationService.cs`, the buy step of `Goals/Executors/TradeBetweenMarketsGoalExecutor.cs` and
        `Orchestration/BudgetPolicy.cs`.
      - Tests: App 863 (4 new: `TradingAutomationServiceTests` 1, `TradeBetweenMarketsGoalExecutorTests` 2,
        `BudgetPolicyTests` 1; D56's saving test now ends in the trip's hold), Domain 72, API 164 (and 4 skipped; two
        credit-reserve cases with a trip's hold), and `ShipGoalRepositoryTests` 13 against Postgres (1 new).
    - **Follow-up, D58** (branch `claude/spacetraders-drones-gather-first`): drones gather first.
      - **The board** (`RolePlanner`, reason `gathers_first`): after the coverage keepers (D48, D53), every other drone
        takes its gathering role, Mine for a mining drone and Siphon for a siphon drone, whatever trading would pay. A
        drone is a ship whose roles include mining or siphoning and that has no surveyor (`FleetRoles.CanSurvey`), so
        the command ship still shares the work by profit. Nothing changes in the plans: a drone with the mining or
        siphon role already trades when its plan has no trip for it.
      - **Purchases:** the coverage tier counts as before; the last tier's check that the board would have a new drone
        gather (`RoleAdvisor`) now always passes, so such a drone is bought when its first trip would serve a market
        short of its ore or gas, and no miner (or siphoner) is free.
      - **What to expect:** no more `RoleChanged` lines moving a drone to Trade "most_profitable"; drones that traded
        (SPECTER-3, and at times all seven siphon drones) gather at the next evaluation, within 10 minutes
        of the deploy, as their trips end (D41). With the minerals they mine supplied, fewer go short, so the coverage
        tier should stop buying drones for them.
      - To understand it, start with `Decide` and `GathersFirst` in `Roles/RolePlanner.cs`.
      - Tests: App 864 (1 new, `RolePlannerTests.ADrone_GathersFirst_ThoughTradingWouldPayItMore`; the tests of the
        fleet-wide assignment, the head start and "no work" now use ships that can survey, and the coverage tests expect
        the other drones to gather), Domain 72, API 164 (and 4 skipped).
  - **To understand this,** start with the decisions D43–D58, then this slice's notes; 6.10b and 6.10c each have their own
    entry.

- **6.11 Exploring through the jump gates, and a systems dashboard** (asked 2026-10-04, with your decisions D59–D63; merged
  as projects#153, its dashboard as gembernodes#51, deployed by gembernodes#52; built on branch `ccr-c7061120-est7c1`; fixes
  B60). Asked: "if an active jump gate goes to a system
  that isn't explored yet, the COMMAND ship should go through that jump gate. If there are markets or shipyard there, the
  COMMAND ship should scout them, as it initially does for the home system, recursively." And during the work: "I'd
  probably want a systems grafana dashboard with a more wide view of which systems have been explored and what kind of
  mining, trading and shipyard opportunities it gives."
  - **What the API said** (2026-10-04, public endpoints and the spec, v2.3.0):
    - X1-DC53's gate, I55, is built; its market exchanges ANTIMATTER and FUEL. It connects to X1-KR90 (18 waypoints, 7
      markets, no shipyard) and X1-MT49 (31 waypoints, 11 markets, two gas giants, no shipyard), both built, and to X1-HZ59
      and X1-BG54 (each about 90 waypoints, 26 to 28 markets and 3 shipyards), whose gates are under construction. A walk
      through the connections passed 60 systems within 7 jumps; farther out, systems such as X1-VR15 and X1-CV66 have a
      shipyard each.
    - A jump (`POST my/ships/{ship}/jump`) takes the destination gate's `waypointSymbol`, needs the ship in orbit at a gate
      and both gates built, buys one ANTIMATTER at the gate's market, and starts a cooldown (B60: the bot's call sent the
      destination's system).
    - An uncharted waypoint shows the trait `UNCHARTED` instead of its own. X1-DC53 and its four neighbours have none;
      some systems farther out do (X1-UF58: 13 of 34).
    - The server resets weekly; the next is on 2026-10-04 at 13:00Z, so this runs under a new agent, maybe in another
      system: nothing in it is tied to X1-DC53.
  - Done:
    - **The explore plan** (`Exploring/ExplorePlanService.cs`, switch `Automation.Plan.Explore.Enabled`, off by
      default): bootstrapped after the scout plan and before the role board. It knows every system it has seen
      (`ExplorePlanState`, `plan_states` row `Explore`): the gate, whether it is built, the gates it connects to, when a
      jump there was refused and when it was explored. Home counts as explored (the scout plan's). It learns one thing
      from the API a pass (D19): home's gate, then each explored system's connections, then each connected gate; a gate
      under construction, or one a jump was refused at, is looked at again hourly. A system the ship has just jumped into
      has its system and waypoints fetched and cached at once, as startup sync does.
    - **The command ship** is taken when its trip ends (D61): free, with a system left to explore; an `Explore`
      assignment keeps the other plans off it. It goes to the nearest system not explored yet by jumps through built
      gates, no limit (D59, `ExploreAtlas`), jumps home when none is left (D60) and is released there.
    - **A jump** (`JumpGoal`, `JumpGoalExecutor`): fly to the gate, fill the tank when docked at a gate that sells fuel,
      orbit, wait out the cooldown, jump. Only while the credits after the antimatter stay at or above
      `FleetExpansion.MinCreditReserve` (D63); until then the plan holds it (`PlanBlocked`, `waiting_for_credits`, once),
      and a free command ship keeps its other work. The antimatter is booked (`AntimatterPurchase`). A jump the API
      refuses blocks the goal (`jump_refused`); the plan leaves that gate an hour and chooses again.
    - **Scouting a system** (`ExploreSystemGoal`, `ExploreSystemGoalExecutor`): each market and shipyard not cached yet,
      nearest first from the gate, once; the arrival stores them, and the gate it jumped to is fetched there. Nothing is
      charted (D62). Journal `SystemExplored` when it is done; a system without either is explored at once.
    - **Business stays home** (D60, `Automation/BusinessSystems.cs`): the mining, siphon and trading plans buy ships, and
      the contract plan looks for its drone's shipyard, only in systems where a ship that doesn't explore is
      (`IShipyardRepository.FindShipyardForTypeAsync` takes those systems); the mining, siphon and survey plans plan no
      work for the system the explorer is in.
    - **Visibility:** journal kinds `Jumped` and `SystemExplored`, and the plan's `PlanStarted`, `PlanCompleted` and
      `PlanBlocked`; the ship's activity reads "jumping to …" or "exploring …". Eleven `spacetraders_system_*` gauges
      (`SystemOpportunities`, every minute): each known system's state, jumps from home, when it was explored, its
      connections, markets, shipyards and uncharted waypoints, waypoints by type, gathering sites per good (asteroids by
      their deposits, gas giants), the best price its markets pay for each ore and gas with the lowest supply, and its
      five best trades with their volume. A system only explored keeps its markets' refresh times but not each good's
      price series, so Prometheus doesn't grow with every system explored.
    - **The systems dashboard** (gembernodes, uid `spacetraders-systems`): counts of systems known, explored, to explore
      and behind gates under construction, and the antimatter bought; the command ship; a table per system; mining and
      siphoning; trades; shipyards; gates; systems over time; the exploring journal. The two other SpaceTraders
      dashboards link to it.
  - **What to expect** once switched on: within a minute (one call a 5-second pass) the plan has looked at home's gate,
    what it connects to and each gate there; at the end of the command ship's trip, `PlanStarted` ("explores X1-…, 1 jumps
    away"), the flight to the gate, `Jumped`, then the new system's markets and shipyards one by one, `SystemExplored`,
    the next jump. Each jump costs one ANTIMATTER at the gate's price; while the credits after it would be under the
    floor, `PlanBlocked` says so once and the ship keeps working. With nothing left it jumps home, `PlanCompleted`, and
    the other plans give it work again. The systems dashboard fills as it goes. While it is away it neither surveys nor
    trades at home: a ship that can only survey (D47) surveys on, and without one the drones mine without surveys.
  - Noticed (not changed):
    - **A gate the API won't describe:** the connections of a gate that is uncharted may be refused, or come back
      empty; the plan treats either as no connections and asks again hourly. Charting it (D62) would settle that.
    - **The role board still evaluates the command ship while it explores,** in the system it is in; the plans give it
      no work there (it is assigned), so only the board's state and `RoleChanged` lines show it.
    - **The credit reserve still counts the command ship's 40-unit hold** while it explores (D51), 40,000 credits that
      no trade of it will spend meanwhile. Yours to call.
    - **`JumpGateCacheService`** is registered but nothing calls it (it was already unused); the explore plan keeps the
      gates in its own state.
    - The explore plan is off by default, as every new plan (D9), and the reset of 13:00Z registers a new agent with the
      seeded settings: switch it on after the reset.
  - To switch it on: `PUT /settings/Automation.Plan.Explore.Enabled` with `{"value": "true"}`.
  - Done when: the command ship has explored every system the built gates reach and is back home, with no open anomaly.
  - **The first run** (switched on at 11:54Z on 2026-10-04): the plan started at 12:24Z, SPECTER-1 jumped to X1-KR90 at
    12:31Z (antimatter 7,723), explored its 7 markets by 12:46Z and jumped on to X1-CV66 at 12:57Z (4,862), when the reset
    ended the run. In X1-FJ91 the home gate is still to be built (6.6), so the plan has nothing to jump through until then;
    its state says done, and it looks at the gate again every hour.
  - **To understand this,** start with `SpaceTraders.Application/Exploring/ExplorePlanService.cs` and `ExploreAtlas.cs`,
    then the two executors (`Goals/Executors/JumpGoalExecutor.cs`, `ExploreSystemGoalExecutor.cs`) and
    `Automation/BusinessSystems.cs`; for the dashboard, `Exploring/SystemOpportunities.cs`.
  - Files, in `SpaceTraders.Application` unless named:
    - new: `Exploring/ExplorePlanService.cs`, `ExplorePlanState.cs`, `ExploreAtlas.cs`, `SystemOpportunities.cs`;
      `Goals/Executors/JumpGoalExecutor.cs`, `ExploreSystemGoalExecutor.cs`; `Automation/BusinessSystems.cs`;
      `Ports/WaypointSymbols.cs`, `JumpRefusedException.cs`;
    - changed: `Ports/ISpaceTradersPort.cs` (`GetWaypointAsync`, the jump's gate), `ApiPortModels.cs`;
      `Automation/AutomationSwitches.cs`, `GameLoopService.cs`, `ScoutAllMarketplacesPlanState.cs` (`PlanTypes.Explore`),
      `MiningAutomationService.cs`, `SiphonAutomationService.cs`, `TradingAutomationService.cs`, `SurveyPlanService.cs`,
      `ContractPlanService.cs`; `Goals/ShipGoalExecutorService.cs`; `EventHandlers/LedgerEntryHandler.cs`;
      `JournalEvents.cs`; `Services/FleetStatusQueryService.cs`; `Interfaces/IAutomationMetrics.cs`,
      `Repositories/IShipyardRepository.cs`; `DependencyInjection.cs`; `ShipGoal.cs`, `ShipGoalKind.cs`,
      `LedgerCategory.cs`, `DomainEvents.cs` (Domain); `SpaceTradersApiClient.cs`, `ISpaceTradersApiClient.cs`,
      `Phase1ActionModels.cs`, `SpaceTradersPortAdapter.cs` (SpaceTradersAPI); `ShipyardRepository.cs`,
      `DefaultSettingsSeed.cs` (Persistence); `PrometheusAutomationMetrics.cs`, `PrometheusMarketMetricsService.cs`,
      `PrometheusMetricsService.cs` (API);
    - tests: `Exploring/ExplorePlanServiceTests`, `ExploreAtlasTests`, `JumpGoalExecutorTests`,
      `ExploreSystemGoalExecutorTests`, `SystemOpportunitiesTests`, `Ports/JumpRequestTests` (new); additions to
      `MiningAutomationServiceTests`, `SiphonAutomationServiceTests`, `SurveyPlanServiceTests`,
      `TradingAutomationServiceTests`, `ContractPlanServiceTests`, `AutomationSwitchesTests`,
      `ShipGoalExecutorServiceTests`, `LedgerEntryHandlerTests`, `DefaultSettingsSeedTests`; `ShipyardRepositoryTests`
      (Infrastructure); `PrometheusMetricsTests`, `PrometheusMarketMetricsServiceTests`, `MetricsEndpointTests` (API).
  - Tests: App 907 (new: `ExplorePlanServiceTests` 8, `JumpGoalExecutorTests` 6, `ExploreSystemGoalExecutorTests` 6,
    `ExploreAtlasTests` 4, `SystemOpportunitiesTests` 4, `JumpRequestTests` 3; one each for the mining, siphon, survey,
    trading and contract plans, which failed before the change; the antimatter's ledger row; the two new goals in
    `ShipGoalExecutorServiceTests`), Domain 72, API 167 (and 6 skipped: the sandbox, and Postgres without Docker),
    Integration 1. `ShipyardRepositoryTests` (Infrastructure) need Docker and didn't run here.

## Changes in gembernodes

Changes to files are made on a branch there, and Flux deploys them once merged. The rest needs
your PC, 1Password or kubectl:

| Slice | Change |
|---|---|
| 2.4 | `infrastructure/monitoring/dashboards/spacetraders-dashboard.json` plus a `configMapGenerator` entry (merged: PR #10) |
| 2.5 | Rules in `infrastructure/monitoring/grafana-alerting-provisioning.yaml`, then a Grafana rollout restart (merged: PR #10; Grafana restarted 2026-10-02) |
| 2.7 | The fleet table's new columns, and panels for the holds and for what was mined (merged: PR #15) |
| 2.8 | A markets dashboard per system, uid `spacetraders-markets` (merged: PR #17) |
| 6.4 | A survey section on the SpaceTraders dashboard: surveys taken, the share that ended unused, the share of extractions with a survey, usable surveys per asteroid, and the survey journal (merged: PR #21) |
| 4.1 | Database login and read-only login (Postgres and 1Password): by hand, with the steps in `apps/spacetraders/README.md` (done; the revoke on `stored_credentials`, step 3, done on 2026-10-02) |
| 4.2 | `apps/spacetraders/`, `namespaces/spacetraders-namespace.yaml`, `ingress/spacetraders-ingress.yaml`, plus the kustomization entries (merged: PR #11) |
| 4.3 | "SpaceTraders bot is down" unpaused (merged: PR #11), then the Grafana rollout restart (done 2026-10-02 09:09Z) |
| 4.3 | B44: the bot's error lines get a rule of their own, by log level, instead of the shared rule's word match (merged: PR #13); then a Grafana rollout restart (done 2026-10-02 09:44Z) |
| 2.9 | The settings table on the SpaceTraders dashboard (merged: PR #31) |
| 6.9 | A "Roles" table under the SpaceTraders dashboard's fleet table: each ship's role, why, and what mining, siphoning and trading would earn it per hour (merged: PR #32). It shows data while the role board is switched on |
| 2.10 | An "API request rates" graph on the SpaceTraders dashboard (initiated, executed, completed, rate limited), and table legends on the graphs of both SpaceTraders dashboards (merged: PR #50) |
| 6.11 | A systems dashboard, uid `spacetraders-systems` (`dashboards/spacetraders-systems-dashboard.json` plus its `configMapGenerator` entry), and links to it from the two other SpaceTraders dashboards (merged: PR #51). It shows the systems beyond home once the explore plan has jumped |
| 2.11 | The markets dashboard's shipyards table: "can do", fuel, cargo and equipment for each ship for sale, and the table at full width (merged: PR #52, which deployed the build) |
| 2.12 | The Settings table's "next run" column, "value" and "next run" 180 px wide, and its description (merged: PR #55, which deployed the build) |
| 6.6 | "Jump gate progress", "Jump gate: materials still needed" and "Jump gate materials" on the SpaceTraders dashboard, and the Roles, Purchase order, Spent per hour and Profit per hour descriptions brought up to date (merged: PR #53). They show data while the home gate is under construction, as X1-FJ91's is |
