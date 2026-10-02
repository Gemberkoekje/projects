# Changelog

All notable changes to this project are documented in this file.
Format follows [Keep a Changelog](https://keepachangelog.com/en/1.1.0/).

---

## [Unreleased]

### Code – Changed (2026-10-02, D27 refined)
- Survey targets are per ore and asteroid: every market that buys an ore gets the reachable asteroid nearest it, not only the market that pays most, so surveys lie close to wherever the ore is sold; each keeps its own stock of usable surveys.

### Code – Changed (2026-10-02, D27)
- The survey plan keeps a stock of usable surveys per ore instead of surveying the contract's ore without end: `Survey.StockPerOre` (new, 2). The contract's ore comes first only while it has fewer; then the ore with the fewest usable surveys, then the best paid. With the stock for every ore, the surveyor waits until one runs out, and `ShipLeftIdle` doesn't count that as idle.

### Docs – Changed (2026-10-02, D27)
- `PLAN.md`: D27, and the third 6.4 follow-up; `docs/HOW_IT_WORKS.md`: the survey stock and its setting.

### Code – Fixed (2026-10-02, B51)
- Extracting with a survey works: the survey's expiry goes back to the API as the API wrote it (`…Z`), not with an offset (`+00:00`), which the API answered with 422 "invalid payload" on every extraction.
- A survey the API can't read (422 without a game error code) is dropped with reason `rejected`, and the warning carries the API's response body; it was tried again on every step, five calls a tick with Wolverine's retries.

### Docs – Changed (2026-10-02, B51)
- `PLAN.md`: B51, and the second 6.4 follow-up with a note on Wolverine's retries; `docs/HOW_IT_WORKS.md`: the rejected survey.

### Code – Changed (2026-10-02, after slice 6.4's switch-on)
- A contract assignment lasts one round trip (D26, asked on 2026-10-02): the delivery closes it, after the fulfil call when one was due, and the plans assign the ship again on the next tick, in their order, so work that matters more comes first. A ship whose fulfil call fails keeps its assignment and makes the call again. The plan's first ship joins like every other free miner; it no longer gets its assignment back on every tick, or takes it back from other work.
- A restart reconsiders the contract's ships once (D26): with automation and the contract plan on, startup recovery releases every ship on the contract that isn't in flight, and the first tick assigns it again. A ship in flight keeps its assignment until its delivery.

### Code – Fixed (2026-10-02, after slice 6.4's switch-on)
- The command ship surveys once the survey plan is on, after its current contract trip or at the next restart (B50): it had joined the contract before the survey switch, and a contract assignment lasted until the contract was fulfilled.

### Docs – Changed (2026-10-02, after slice 6.4's switch-on)
- `PLAN.md`: 6.4 merged (projects#122, gembernodes#21 and #22) and switched on; B50 and D26; the follow-up's summary. `docs/HOW_IT_WORKS.md`: contract trips, the restart release.

### Code – Added (2026-10-02, slice 6.4)
- Surveying, as asked on 2026-10-02 (`PLAN.md` 6.4, decisions D20–D25). A new survey plan (`Automation.Plan.Survey.Enabled`, off by default) has every ship that can survey survey, and only that (D20): the contract's ore at the contract's asteroid first, otherwise ores the system's markets buy, at the asteroid nearest the market that pays most for them among those the miners can reach; ores without a usable survey first. One survey per goal; the goal ends after each.
- What an asteroid yields, from its traits (`AsteroidDeposits`), and which survey to extract with: the one where the ore makes up the largest share of the deposits, then the larger deposit, then the later expiry. Extractions use it, for the contract's miners too.
- The mining plan, rewritten: one trip per goal (mine until the hold is full, sell, choose again). A miner sells ore it holds first; otherwise it mines a surveyed ore first, then an ore in low supply (SCARCE or LIMITED, D22) at the asteroid nearest the market that is short of it, and sells it there. Trips fly with their goal through refuelling stops, never DRIFT. Drones are bought only for openings a drone could reach, and not while the contract takes the miners.
- Every free miner joins the active contract (D23); completion releases them all, and ore left over is sold by the mining plan.
- The trading plan buys its own cargo ships (D21, replacing D16): `Trade.ShipPurchases` (new; a light shuttle, then up to two light haulers), when every trader has a trip, a new ship would have a lucrative route, and the purchase keeps the credit reserve. With the survey plan on, a ship that can survey doesn't trade (D20); miners trade only when no mining work waits.
- `Trade.FuelReserveCredits` (new, 5,000): cargo purchases leave it untouched, so only fuel is bought below it (D24).
- After each purchase or sale, by a trader or a miner, the market is fetched again while the ship is still docked (D25, `MarketRefresher`, shared with the market watch).
- For a survey dashboard: journal kinds `Surveyed`, `SurveyEnded` (with how many extractions used the survey), `Extracted` and `MiningStarted`; metrics `spacetraders_surveys_taken_total`, `spacetraders_surveys_ended_total{reason,used}`, `spacetraders_surveys_active{used}` and `spacetraders_extractions_total{surveyed}`. `cached_surveys` gained `Extractions`; the initializer adds the column to an existing table.

### Code – Fixed (2026-10-02, slice 6.4)
- Waypoint traits are stored (B34): startup sync stores each waypoint's traits and modifiers, and fetches a system's waypoints again once when a cached one has none, so the cluster's waypoints get theirs at the next start.
- Every asteroid type can be mined (B48): `ASTEROID`, the type of 56 of X1-DC53's 57 asteroids, was refused.
- A survey the API refuses (exhausted, expired, not verified) is dropped instead of tried again on every step (B49).
- Survey goals end after their survey, and a miner no longer hands out survey goals or waits for one (B16's survey part); mining and survey trips navigate with their goal, so their arrivals wake them (B17 for those trips).
- A ship that delivers after another has fulfilled the contract no longer calls fulfil again.

### Code – Removed (2026-10-02, slice 6.4)
- The mining plan's event `Handle` methods (never wired, see `DiValidationTests`), its stale-goal cleanup, and `IShipGoalRepository.GetActiveMineAndSellTargetsAsync` and `GetActiveSurveyTargetsAsync`, which only the old mining code used.

### Docs – Changed (2026-10-02, slice 6.4)
- `PLAN.md`: slice 6.4 built, with its summary; D20–D25; B48 and B49 found and fixed; B16, B17 and B34 updated; 6.5 merged; 4.1's revoke done. `docs/HOW_IT_WORKS.md` describes the survey plan, the mining plan, the contract's miners, the cargo ship purchases, the fuel reserve, the refetch after trades, the new journal kinds, metrics and settings.

### Code – Added (2026-10-02, slice 6.5)
- Trading, as asked on 2026-10-02 (`PLAN.md` 6.5, decisions D14–D18). Any ship with a cargo hold and a fuel tank that has nothing else to do trades; after scouting that is the command ship. A trip's profit is what the sell market pays minus what the buy market charges, times the units, minus the fuel for the whole trip, the flight to the buy market included; it must earn `Trade.MinProfitPerUnit` per unit (D14), and routes whose sell market makes a pricier good from the cargo come first (D15). Two traders never share a route (D18), and the plan buys no ships (D16). Flights beyond one tank refuel at markets on the way instead of drifting. A trip checks its prices again where it lands: at the buy market it buys only while the trip is still lucrative (`TradeDropped` otherwise), and at the sell market it takes the cargo to a market that pays more, once (`TradeRerouted`). Sales above a market's trade volume go in several. Journal: `TradeStarted`, `TradeRerouted`, `TradeDropped`.
- The market watch: every market with one of our ships at its waypoint is fetched again once `Market.RefreshMinutes` (new, 5; 0 = off) have passed since its prices were last seen; one market a tick, the most overdue, as the tick's last step (D19).
- Writes before reads for the rate limit (D19): a GET gives way while a POST waits for the budget, leaves the last 10 of the 30-request burst to writes, and stops giving way after 10 seconds. `spacetraders_api_rate_limit_wait_seconds_total` has a `kind` label, `read` or `write`.
- The game's production chains are fetched once per process and shared by the trading plan and the markets dashboard (`SupplyChainCache`).

### Code – Fixed (2026-10-02, slice 6.5)
- One goal step at a time per ship (B46): the tick and an arrival could both step a ship as it docks, and a trade step would have bought twice. A step that finds its ship busy is skipped; the next tick takes it.

### Code – Removed (2026-10-02, slice 6.5)
- The old trading opportunities (a good scarce at one market and abundant at another, prices unread), the hauler purchases that went with them, and `IShipGoalRepository.GetActiveTradeRouteTargetsAsync`, which only they used.

### Docs – Changed (2026-10-02, slice 6.5)
- `PLAN.md`: slice 6.5 built, with its summary; D14–D19; B46 fixed and B47 (the navigation's fuel fallback leaves a ship in DRIFT) found. `docs/HOW_IT_WORKS.md` describes the market watch, the trading plan and the trip, and the one-step-per-ship guard; `docs/GLOSSARY.md` adds lucrative, market watch, trader and trade trip.

### Tools – Added (2026-10-02)
- `tools/investigate/st.py` (slice 5.1): reads the bot's data on the cluster from your PC, read-only, for the `st-investigate` skill: `check` (the pods, Prometheus, Loki and the database in turn), `prom`, `logs` (with `--group`, warnings and errors per statement) and `sql` (as `spacetraders_ro`, through psql or the `postgres:17` image, with the password from psql's password file). It starts and stops its own port-forwards and masks tokens and passwords. See its README.

### Tools – Added (2026-10-02, slice 5.2)
- `.claude/settings.json`: Claude may run `tools/investigate/st.py` and `kubectl -n spacetraders logs` without asking, and nothing else (your go-ahead of 2026-10-02).

### Docs – Changed (2026-10-02, phase 5)
- `.claude/skills/st-investigate/SKILL.md` is finished (slice 5.1) and was checked against the bot's first run on the cluster: it explained B42's anomaly and found B45. It reads every source through `tools/investigate/st.py`, leaves the internal API out (its key also unlocks settings and control), and records what the run taught. `PLAN.md`: phase 5 done, with a short summary; 4.1 done but for the revoke on `stored_credentials`; the gembernodes PRs of 2.7, 2.8 and B44 named. `README.md` reports phase 5.

### Code – Added (2026-10-02)
- Metrics for a markets dashboard per system (slice 2.8): every minute the cached markets (per good: prices, trade volume, supply, activity) and shipyards (ship types, prices, supply), with when each was last refreshed (`spacetraders_market_*`, `spacetraders_shipyard_*`, `PrometheusMarketMetricsService`); and once per start the game's production chains from `GET market/supply-chain`, one series per good with what it is made from and what is made from it (`spacetraders_good_supply_chain`).
- Metrics for the dashboard's fleet table (slice 2.7): where each ship is (the waypoint and its type, or `→` and where it goes), what the bot has it do (`mining COPPER_ORE`, `scouting`, `idle`…), when it arrives, and its hold per good (`spacetraders_ship_info`, `spacetraders_ship_arrival_timestamp_seconds`, `spacetraders_ship_cargo_units`, `spacetraders_ship_cargo_capacity_units`); and what drones extract and jettison, per ship and good (`spacetraders_extracted_units_total`, `spacetraders_jettisoned_units_total`).

### Code – Fixed (2026-10-02)
- The scout plan no longer skips a stop when a tick coincides with an arrival (B45). Both can run the ship's goal step as it docks; now only a visit of the plan's current stop moves the plan on, and the tick's resume check reads the assignment before the plan (advancing writes the plan first), so an advance under way no longer looks like a missing assignment. On the cluster the plan reported all 26 stops visited while the last, X1-DC53-J58, never was.
- A handler's first message no longer logs a warning for each dependency it resolves from the container (B42): Wolverine allows service location without one (`ServiceLocationPolicy.AlwaysAllowed`). On the cluster these warnings, all with one template, raised the `RepeatingError` anomaly in the bot's second minute, and their text ("…this is an error") tripped gembernodes' error-log alert.
- Every counter series reaches Prometheus at 0 before it counts (B43, `ZeroFirstCounter`): what a new series counts waits until a scrape has exported that 0, at most two scrape intervals. Prometheus's `increase()` and `rate()` never count the value a series has when it is first scraped, so the contract's deposit and the first drone, booked before the pod's first scrape, were missing from the ledger panels, and a single 429, failed call or breaker trip could never show.

### Docs – Changed (2026-10-02)
- `PLAN.md`: phase 4 merged and the first-run watch (4.3) started; B42–B44 found in its first minutes. `README.md` reports the bot back on the cluster, and `docs/HOW_IT_WORKS.md` says when Wolverine compiles a handler and how counters reach Prometheus.

### Docs – Added (2026-10-01)
- `PLAN.md`: the plan for getting the bot safely back on the cluster, with the known issues found by reading the code (B1–B25) and the decisions taken (D1–D11).
- `docs/HOW_IT_WORKS.md`: what the code does today, written from the code.
- `docs/archive/README.md`: an index of the archived documents.
- `.claude/skills/st-investigate/SKILL.md`: draft of the skill Claude uses to investigate and fix misbehaviour.

### Docs – Changed (2026-10-01)
- `PLAN.md`: B41, and the manifests are in gembernodes PR #11.
- `PLAN.md`: phase 4 ready to merge. The manifests (4.2) and the unpaused "bot is down" alert (4.3) are on a gembernodes branch, with a README for the steps by hand; the database logins (4.1) are prepared and tested against PostgreSQL 18; and B39 and B40 were found and fixed on the way. `README.md`, `spacetraders.md` and `docs/HOW_IT_WORKS.md` describe the deployment, and the `st-investigate` skill names the read-only login and the LAN address of the internal API.
- `PLAN.md`: phase 3 done (slices 3.1 and 3.2), with a short summary; D12 decided (the credit-drop alert is gone) and D13 added (the idle-ship and credits rules count only while work waits); the dashboard and alerts (2.4, 2.5) merged as gembernodes PR #10, with the Grafana restart still to do. `docs/HOW_IT_WORKS.md` has a section on the health rules (12), `docs/GLOSSARY.md` describes anomalies and health rules as they are, and the `st-investigate` skill says where each rule's anomaly points (and that production logs carry `@m` and `@i`, not `@mt`). `README.md` reports phase 3; `spacetraders.md` says where `/metrics` is served since slice 2.1.
- `PLAN.md`: phase 2 done in the code (slices 2.1, 2.2, 2.3, 2.6, and 0.5), with a short summary; the dashboard and alerts (2.4, 2.5) ready on a gembernodes branch; new known issues B37 (the credit-drop alert can't fire) and B38 (startup recovery reports docked ships as in transit), open decision D12, and notes for 4.2 (metrics port annotations, the Traefik migration) and 4.3 (unpause the "bot is down" alert).
- `PLAN.md`: the soak test's results (slice 1.14, the last of phase 1), the issues it found (B28–B36), a slice for its tidy-ups (0.5), and a short summary of phase 1.
- Moved the old plans, designs, progress logs, strategy notes and the `docs/implementation/` and `docs/operations/` documents to `docs/archive/`.
- `README.md`, `CONTRIBUTING.md`, `docs/GLOSSARY.md` and `spacetraders.md` now match the code: no `SpaceTraders.App`, no `k8s/` folder, no `/control/sync` or `/control/ships/{symbol}/reassign`, and the burst limit as the API guide defines it.
- `CLAUDE.md`: what Claude works on in this project, and that it considers fixing build warnings in the files it touches.

### Config – Changed (2026-10-01)
- The WebUI's nginx no longer logs requests to `/healthz`, which only Kubernetes' probes call (B40): 8 lines a minute, about 11,500 a day, which Grafana's log-volume panel and alert counted against the bot's budget of 50,000.
- `Metrics:Port` (9090) and `Metrics:Hostname` in `appsettings.json`: where `/metrics` is served. Development listens on `localhost` only; `Dockerfile.api` exposes 9090 next to 8080.
- `System.Net.Http` logs at Warning instead of Information (`appsettings.json`, `appsettings.Development.json`), removing about four log lines per outbound API call.

### Tools – Changed (2026-10-01)
- `tools/soak/` reads `/metrics` from the metrics port (`--metrics-url`, default `http://127.0.0.1:9090/metrics`) and counts API calls with `spacetraders_api_requests_total`; `launch.py` keeps the metrics on loopback.

### Tools – Added (2026-10-01)
- `tools/soak/`: runs the bot like the cluster against a local database, and samples every table, `/metrics` and the log every 15 minutes (slice 1.14). See its README.

### Build – Changed (2026-10-01)
- The Docker integration tests run on Windows without `DOCKER_HOST` (B36): they ask Testcontainers whether it can reach Docker, which finds Docker Desktop's named pipe, instead of looking for `/var/run/docker.sock` or `DOCKER_HOST` themselves. Three of the four test classes skipped silently there.
- Deleted the unused `packages.lock.json` in the project root (it belonged to `.net.csproj`, which no solution includes), and the UTF-8 BOM in three test `.csproj` files (Proj3000).

### Packages – Changed (2026-10-01)
- Every NuGet package is on its latest version, except `Microsoft.CodeAnalysis.CSharp` and `.Common` (kept at 5.0.0): they set the oldest compiler that can load the ST0001 analyzer, and 5.9 would switch it off on any SDK before 10.0.4xx. This clears both NuGet audit warnings (`Microsoft.OpenApi` through Swashbuckle, `SSH.NET` through Testcontainers).
- Majors: WolverineFx 6, NSubstitute 6, `xunit.runner.visualstudio` 4, `xunit.analyzers` 2. Wolverine 6 no longer compiles handler code without `WolverineFx.RuntimeCompilation` (now referenced, and registered with `UseRuntimeCompilation()` because discovery is `ManualOnly`), and refuses service location by default. Every handler that reaches the database needs it, because EF Core registers the DbContext's options through a factory: the host now resolves the DbContext from the scope (`AlwaysUseServiceLocationFor<SpaceTradersDbContext>()`), and `RestoreV5Defaults()` turns any other case into a warning instead of a failing handler. Found by running the published build; the unit tests replace those registrations.
- The newer analyzers bring new rules: about 220 more warnings, almost all style (Qowaiv's strongly typed identifiers and date literals, Sonar's redundant `!`, project-file rules), plus two licences to accept (`Proj0503`: FluentAssertions, SonarAnalyzer).

### Code – Added (2026-10-01)
- Health rules (phase 3): every minute `HealthMonitorService` checks the bot's own state against ten rules: the contract it works on (`ContractStalled`, `ContractLeftOpen`, `ContractDeadlineAtRisk`), its ships (`ShipStuck`, `ShipLeftIdle`, `CircuitBreakerTripped`), and `RepeatingError`, `CreditsUnchanged`, `ApiUnauthorized` and `ApiThrottled`. A subject that breaks one is an anomaly: `spacetraders_anomaly_active{rule,subject}`, and the journal's `AnomalyRaised` (with `Details`) and `AnomalyCleared`. Their thresholds are eight new `Health.*` settings. `docs/HOW_IT_WORKS.md` section 12 lists them.
- The journal (slice 2.3): one log line per meaningful thing with an `EventKind` property, so `{namespace="spacetraders"} | json | EventKind != ""` reads as a timeline: contracts accepted, delivered and fulfilled; ships and cargo bought and sold; plans started, completed and blocked with a reason; ships idle or blocked with a reason; every setting change (old and new value, secrets hidden); resets, API pauses, and anomalies raised and cleared. `JournalEvents` names them all.
- Metrics for the dashboard (slice 2.1): credits, credits earned and spent by ledger category, ships by role and state, each ship's state with its goal and blocked reason, contract units and deadline, API requests by route template and status, 429s by source, time waited for the request budget, handled messages by type, goal steps by kind, active anomalies (the database size limits for now) and the next server reset. `docs/HOW_IT_WORKS.md` section 11 lists them.
- Database size guard (D8): every 5 minutes the bot reads its database size and exports it as `spacetraders_db_size_bytes`. Above `Database.SoftLimitMegabytes` (1024) it logs a warning; above `Database.HardLimitMegabytes` (3072) it switches automation off.
- Every table has a retention policy, or the reason it can't grow without bound, in `DataRetention`; a test fails for a table without one (B3). New: `startup_snapshots` keeps the agent's first snapshot and the last 10, `runs` and `run_credit_highlights` keep 365 days, `ship_goal_history` and completed `fleet_goals` keep 30 days.
- Earlier agents are cleaned up: at startup, agent bootstrap deletes the rows of every agent but the active one, except their `runs` (B3). Each server reset left the previous agent's rows behind for good.
- One switch per plan: `Automation.Plan.{Scout,Contract,ProbeDeployment,Mining,Trading}.Enabled`. Scout and Contract are on by default, the other three off (D9). A plan that is off isn't bootstrapped, buys nothing, and its ships' goals wait.
- Per-ship circuit breaker: a ship that takes more than `Automation.CircuitBreaker.MaxGoalStepsPerMinute` goal steps in a minute (default 60) gets its goal blocked as `runaway`, with a warning and the `spacetraders_goal_breaker_trips_total` metric. Blocked goals are not stepped again.

### Code – Removed (2026-10-01)
- The credit-drop alert (D12, B37): credits only drop when the bot spends them, so it could only have reported the bot's own spending. It couldn't fire either: it compared each change with a field that Wolverine never kept.

### Code – Changed (2026-10-01)
- The host logs through its own Serilog logger instead of the process-wide `Log.Logger` (B41). Every host replaced the static logger when it started and closed it when it stopped, so tests running hosts side by side could log into another host's sinks, or none: CI failed on `main` after #114, in a health-rule scenario that had passed on the PR. One host per process in production, so nothing changes there.
- `/health/startup` answers 503 until the startup chain has completed (B39). It answered 200 while the chain ran, because the check reports that as Degraded, so the Kubernetes startup probe on it would have passed at once.
- Journal lines render their kind without quotes (`{EventKind:l}`): Serilog quotes string values in the rendered message, so a line read `"ShipIdle": ship …` in Loki.
- The settings seed holds only settings that code which runs reads (B18, D10): 26 are gone (`Navigation.*`, `Maintenance.*`, `Outfitting.*`, and the others nothing read), 30 remain, `Runtime.*` flags included. A database seeded earlier keeps its rows; the bot ignores them, as it always did.
- The size guard logs its limits as anomalies, `AnomalyRaised` and `AnomalyCleared` with rule `DbSizeSoftLimit` or `DbSizeHardLimit`, instead of `DbSizeSoftLimit`, `DbSizeHardLimit` and `DbSizeNormal` events.
- Rows are keyed on a short agent id instead of the agent token (B4): the agent's symbol and the server's reset date, such as `GEMBER@2026-09-27`. The token, a JWT of about 1 KB, is stored only in `stored_credentials`. **A database from before this change has to be dropped**: the schema initializer no longer upgrades old schemas, and the unused `scout_plan_states` table is gone.

### Code – Fixed (2026-10-01)
- Sales, cargo and ship purchases, and contract payments reach the ledger, the credit samples and the metrics (B7): each is published where it happens (`ShipCargoSoldEvent`, `CargoPurchasedEvent`, `NewShipPurchasedEvent`, `ContractAcceptedEvent`, `ContractFulfilledEvent`), and every credit change publishes `AgentCreditsChangedEvent`. Before, the ledger only held fuel purchases. The probe plan's credits handler now checks the automation and plan switches, since waking it can buy probes.
- Market refreshes record price history (B19): the trade goods are read case-insensitively, as they are stored.
- Prometheus can scrape `/metrics` (B11): it has a port of its own, `Metrics:Port` (9090), that needs no API key and that the Service and ingress don't route. The main port no longer serves it. The credits gauge shows the cached credits; it waited for an event nothing published (B7).
- The startup snapshot reads what startup sync has just cached instead of fetching it all again (B35, about 11 API calls on every start). It now also holds the ships' goals and the contracts, and it no longer switches `Automation.Enabled` off and back on around itself.
- Contract payments reach the cached credits (B33, found in the soak test): the accept and fulfil responses carry the agent's new credits, but nothing stored them, so purchases were budgeted from too little until the next restart (6,620 credits short in the soak test).
- `cached_ships` no longer grows without end (B32, found in the soak test): it is created with `fillfactor=50` and `autovacuum_vacuum_threshold=10`. Its few wide rows change every minute or so; pruning kept the dead-tuple count under the autovacuum trigger, so VACUUM never ran and updates that didn't fit their page kept extending the table (about 250 kB an hour with one busy ship). A database created before this keeps the old table: `ALTER TABLE cached_ships SET (fillfactor = 50, autovacuum_vacuum_threshold = 10)`.
- A restart keeps the contract's terms (B31, found in the soak test): startup sync stored contracts without their deadline and deliverables, so the contract plan couldn't read its deliverable until the next delivery.
- The contract plan completes once its contract is fulfilled, and releases the ship (B9): it waited for two events that nothing publishes, so after fulfilment the tick went on sending the ship to mine and deliver for a finished contract. The tick now advances the plan from the cached contract.
- A contract ship delivers whole trips (B8): what the contract still needs, at most a full hold. The tick sent it off as soon as one unit was aboard, so it shuttled one to three units a trip. A delivery also sends no more than the contract still needs (B30).
- The command ship is free after scouting (B10): the scout plan clears the ship's last goal when it completes. Before, every tick ran that finished goal again, rewriting a waypoint row each time.
- Startup sync caches a shipyard with its prices (B28, found in the soak test). It stored the priced ships where the ship types belong and left the prices empty, so a purchase at a shipyard where a ship sat at startup failed with "price unknown" until a ship arrived there again. Every restart did the same to each shipyard with a ship parked at it.
- Wolverine logs a handled message at Debug instead of Information (B29, found in the soak test). It logs that line under the message type's name, so the `Wolverine` level override didn't reach it: one line per message.
- A contract plan waiting for budget no longer calls the API on every tick (B27). Each 5 s retry fetched every contract (`GET my/contracts`, 12 calls a minute) and saved the plan again; a retry now tries only the ship again, and stores nothing while the plan keeps waiting.
- Log diet (B12): an idle bot no longer logs at Information on every tick. What a tick finds when nothing changed goes to Debug (the command ship after scouting wrote two lines every 5 s, a contract plan waiting for budget four). Production logs JSON with the rendered message (`RenderedCompactJsonFormatter`); ships, contracts and waypoints are always `ShipSymbol`, `ContractId` and `WaypointSymbol`; and every line logged during a tick carries `Tick` and its step's `Plan`, `ShipSymbol` or `ContractId`.
- Pruning starts right after database initialisation and prunes each table on its own (B3): it started only after every other startup step had succeeded, and one failing table stopped the tables after it. `DataRetentionService` has taken over from `ActivityLogPruningService`.
- Downsampling price and credit samples fits the 30 s command timeout: the `NOT IN` delete didn't finish within 3 minutes on 2.6 million samples; ranking the rows once takes 5 seconds.
- A newly registered agent gets its default settings right away (B26). They were stored under the previous agent, so after every server reset all settings read as missing, and automation stayed off until the pod restarted.
- The schema is created on a database that already holds other tables (B21): `EnsureCreated` skipped the app's tables as soon as any table existed, such as Wolverine's.
- Wolverine no longer stores messages in Postgres (B2): with its durability agent off it never deleted a handled message, so every published message stayed in the database. Messages now stay in memory; `WolverineFx.EntityFrameworkCore` and `WolverineFx.Postgresql` are no longer referenced.
- API limits and errors follow the API guide (B13): 2 requests per second plus a burst of 30 per 60 seconds, in a budget that no longer resets; a 429 waits for the rate limiter's reset or backs off exponentially; a 502 pauses all API calls for `Api.BadGatewayPauseMinutes` (default 3) instead of retrying after 1, 2 and 4 seconds. `spacetraders_api_throttled_total` now counts 429 responses only.
- A server reset during a run is noticed (B6): the first call that fails with the reset error switches automation off, logs `ResetDetected` and stops the host, so the restart registers a new agent.
- A restart no longer clears every ship's active goal (B20): startup sync updates the ship's game state and leaves its goal alone.
- One failing step no longer stops the whole tick (B14): each plan bootstrap, ship goal step and contract assignment runs in its own scope and try/catch.
- A failed startup chain stops the host and exits with code 1, so Kubernetes restarts the pod instead of leaving an idle one that looks healthy (B23).
- `Automation.Enabled` now stops the bot (B5): the tick runs no plans, goal steps or contract work, and no goal step runs from an arrival, the probe handler or startup recovery either.
- A ship that was already at its goal's target no longer loops (B1, the likely cause of the full database in May). In orbit at the target, the scout, probe and mining executors dock instead of navigating to where they are; docked at the target, the survey executor orbits. Navigating to the waypoint a ship is already at does nothing and no longer publishes `ShipNavigationCompletedEvent`.

### Docs – Changed
- Removed obsolete plan-related Markdown files.
- Renamed cleaned SpaceTraders.io reference content to `spacetraders.md`.
- Updated README, contribution guidance, glossary, and implementation docs to match the current code.
- Added `docs/SPACE_TRADERS_IMPLEMENTATION_PLAN.md` to map SpaceTraders.io reference capabilities to current implementation gaps and milestones.

---

## [0.2.0] – 2026-01-01 – Automation foundation

### Code – Added
- Typed `SpaceTradersApiClient` with implemented public/authenticated endpoint subset
- `SpaceTradersApiOptions`, DI registration, `SpaceTradersApiException`
- Startup services in API host:
  - `AgentBootstrapService`
  - `StartupSyncService`
- Persistence entities and `SpaceTradersDbContext` for cached agent/ships/contracts and stored credentials
- Razor Pages dashboard overview page showing cached agent + ships

### Docs – Added
- `docs/GLOSSARY.md`
- `CONTRIBUTING.md`
- `CHANGELOG.md`

### Docs – Changed
- `README.md` aligned with currently implemented foundation scope
- `CONTRIBUTING.md` aligned with current repository state

---

## [0.1.0] – 2025-01-01 – Infrastructure foundation

### Added
- Solution with six projects: Domain, Application, Infrastructure.SpaceTradersAPI, Infrastructure.Persistence, API, App
- Initial project structure

---

[Unreleased]: https://github.com/Gemberkoekje/projects/compare/v0.1.0...HEAD
[0.2.0]: https://github.com/Gemberkoekje/projects/releases/tag/v0.2.0
[0.1.0]: https://github.com/Gemberkoekje/projects/releases/tag/v0.1.0
