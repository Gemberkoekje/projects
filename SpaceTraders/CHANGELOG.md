# Changelog

All notable changes to this project are documented in this file.
Format follows [Keep a Changelog](https://keepachangelog.com/en/1.1.0/).

---

## [Unreleased]

### Docs – Added (2026-10-01)
- `PLAN.md`: the plan for getting the bot safely back on the cluster, with the known issues found by reading the code (B1–B25) and the decisions taken (D1–D11).
- `docs/HOW_IT_WORKS.md`: what the code does today, written from the code.
- `docs/archive/README.md`: an index of the archived documents.
- `.claude/skills/st-investigate/SKILL.md`: draft of the skill Claude uses to investigate and fix misbehaviour.

### Docs – Changed (2026-10-01)
- `PLAN.md`: the soak test's results (slice 1.14, the last of phase 1), the issues it found (B28–B36), a slice for its tidy-ups (0.5), and a short summary of phase 1.
- Moved the old plans, designs, progress logs, strategy notes and the `docs/implementation/` and `docs/operations/` documents to `docs/archive/`.
- `README.md`, `CONTRIBUTING.md`, `docs/GLOSSARY.md` and `spacetraders.md` now match the code: no `SpaceTraders.App`, no `k8s/` folder, no `/control/sync` or `/control/ships/{symbol}/reassign`, and the burst limit as the API guide defines it.
- `CLAUDE.md`: what Claude works on in this project, and that it considers fixing build warnings in the files it touches.

### Config – Changed (2026-10-01)
- `Metrics:Port` (9090) and `Metrics:Hostname` in `appsettings.json`: where `/metrics` is served. Development listens on `localhost` only; `Dockerfile.api` exposes 9090 next to 8080.
- `System.Net.Http` logs at Warning instead of Information (`appsettings.json`, `appsettings.Development.json`), removing about four log lines per outbound API call.

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
- The journal (slice 2.3): one log line per meaningful thing with an `EventKind` property, so `{namespace="spacetraders"} | json | EventKind != ""` reads as a timeline: contracts accepted, delivered and fulfilled; ships and cargo bought and sold; plans started, completed and blocked with a reason; ships idle or blocked with a reason; every setting change (old and new value, secrets hidden); resets, API pauses, and anomalies raised and cleared. `JournalEvents` names them all.
- Metrics for the dashboard (slice 2.1): credits, credits earned and spent by ledger category, ships by role and state, each ship's state with its goal and blocked reason, contract units and deadline, API requests by route template and status, 429s by source, time waited for the request budget, handled messages by type, goal steps by kind, active anomalies (the database size limits for now) and the next server reset. `docs/HOW_IT_WORKS.md` section 11 lists them.
- Database size guard (D8): every 5 minutes the bot reads its database size and exports it as `spacetraders_db_size_bytes`. Above `Database.SoftLimitMegabytes` (1024) it logs a warning; above `Database.HardLimitMegabytes` (3072) it switches automation off.
- Every table has a retention policy, or the reason it can't grow without bound, in `DataRetention`; a test fails for a table without one (B3). New: `startup_snapshots` keeps the agent's first snapshot and the last 10, `runs` and `run_credit_highlights` keep 365 days, `ship_goal_history` and completed `fleet_goals` keep 30 days.
- Earlier agents are cleaned up: at startup, agent bootstrap deletes the rows of every agent but the active one, except their `runs` (B3). Each server reset left the previous agent's rows behind for good.
- One switch per plan: `Automation.Plan.{Scout,Contract,ProbeDeployment,Mining,Trading}.Enabled`. Scout and Contract are on by default, the other three off (D9). A plan that is off isn't bootstrapped, buys nothing, and its ships' goals wait.
- Per-ship circuit breaker: a ship that takes more than `Automation.CircuitBreaker.MaxGoalStepsPerMinute` goal steps in a minute (default 60) gets its goal blocked as `runaway`, with a warning and the `spacetraders_goal_breaker_trips_total` metric. Blocked goals are not stepped again.

### Code – Changed (2026-10-01)
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
