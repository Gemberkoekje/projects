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
- Phase 1 is done (2026-10-01): the soak test (1.14) ran the bot for four hours against the live
  API from an empty local database. Phase 2 is next; the bot stays off the cluster until phase 4.

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
| B22 | **The dashboard publishes the internal API key.** The WebUI container writes the key into `config.js`, which anyone who can open the dashboard can read. The old ingress served both the dashboard and the API on the public `gemberkoekje.nl`, so anyone could call `PUT /settings/*` and `POST /control/*`. | `SpaceTraders.WebUI/docker-entrypoint.sh:11-29`, `SpaceTraders.WebUI/index.html:17`; gembernodes `3f9f785^:ingress/spacetraders-ingress.yaml` | 4.2 |
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
| B37 | **The credit-drop alert can't fire** (found in 2.2). `AlertHandler` compares each credit change with the credits it remembers in a field from the previous one, but Wolverine creates the handler anew for every message, so the field is always empty. Until 2.2 nothing published the event anyway. The event carries the old credits, so the fix is small, but it would then warn, and post to `Alerts.WebhookUrl`, on every purchase that costs more than 10% of the credits, a ship included. | `AlertHandler.cs` (`_previousCredits`) | D12 |

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
| D12 | The credit-drop alert (B37) would fire on every purchase above 10% of the credits. Fix it as it is meant, change what it watches, or remove it? | **Open.** It stays as it is (it can't fire) until you decide. |

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
  - The startup probe is still to do, in the manifests (4.2).

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

- **6.1 The first contract, end to end:** B8 and B9 (both fixed in 1.14; what's left is a clean
  reset period). Per D1 and D2 the bot takes one mineral contract per reset; taking the next
  contract is a later addition.
- **6.2 The command ship after scouting:** B10, and the scout part of B16 (both fixed in 1.14;
  what's left is a clean reset period). The ship moves on to its next job instead of holding on to
  the finished scout goal.
- **6.3 Probes** (`ProbeDeploymentPlanService`): B15 and B25.
- **6.4 Mining drones mine and sell** (`MiningAutomationService`, `MineAndSellGoalExecutor`): the
  survey part of B16, B17, and B34 (traits, so drones go where their mineral is).
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
