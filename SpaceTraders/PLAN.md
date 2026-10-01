# SpaceTraders — Plan

> Draft of 2026-10-01. This is the single plan for what happens next; the older plan documents
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
- `SpaceTradersV3/` is an unbuilt copy of this project's API client and interfaces. It has no
  host, persistence or automation, and it probably doesn't compile.

## Known issues

Found by reading the code on 2026-10-01. None has been reproduced at runtime yet; each fix starts
with a test that does.

### Bugs: behaviour that contradicts the code's own intent

| # | Issue | Evidence | Slice |
|---|---|---|---|
| B1 | **"Already at destination" loop.** Navigating to the waypoint a ship is already at publishes `ShipNavigationCompletedEvent` without docking or orbiting. Its handler re-runs the goal executor, which asks to navigate there again. No API call is involved, so the rate limiter doesn't slow it, and the 5 s tick starts another chain every time. The survey executor hits this in its normal state right after any arrival (docked at the target). Most likely what filled the database. | `NavigateToWaypointCommand.cs:82-96`, `ShipNavigationCompletedHandler.cs:26`, `SurveyWaypointGoalExecutor.cs:52`, `ScoutWaypointGoalExecutor.cs:41`, `DeployProbeGoalExecutor.cs:48`, `MineAndSellGoalExecutor.cs:106` | 1.1 |
| B2 | **The Wolverine inbox is probably never cleaned.** Every published message is stored in Postgres. Turning the durability agent off very likely also turns off the deletion of handled messages (about 80% sure; Wolverine's source was not checked for this version). | `Program.cs:180`, `Program.cs:189-191` | 1.3 |
| B3 | **Retention gaps.** Pruning only starts if every earlier startup step succeeded, and one failing table stops the tables after it. `startup_snapshots` (full JSON on every pod start), `runs` and `wolverine.*` are never pruned. All pruning is scoped to the current agent, so each server reset leaves the previous agent's rows behind for good. | `DeferredStartupHostedService.cs:57-76`, `DataRetentionService.cs:55-64` | 1.5 |
| B4 | **The agent JWT (about 1 KB) is part of every table's key**, and of its indexes. | `SpaceTradersDbContext.cs` (`AgentToken`, `HasMaxLength(1024)`) | 1.4 |
| B5 | **The automation kill switch doesn't stop the game loop.** Nothing in `Application/Automation/` reads `Automation.Enabled`. | `GameLoopService.cs:69-76` | 1.7 |
| B6 | **A server reset during a run isn't detected.** Every call fails with 401 until the pod restarts, and the liveness check keeps passing. | `AgentBootstrapService` (runs only at startup) | 1.8 |
| B7 | **Domain events are raised but never dispatched.** Nothing outside the domain reads `AggregateRoot.DomainEvents`. As a result:<br>• the ledger only holds fuel purchases;<br>• the credits gauge and the credit samples stay empty;<br>• below 200k credits the probe plan waits forever for an `AgentCreditsChanged` that never comes. | `Domain/Common/AggregateRoot.cs`, `ProbeDeploymentPlanService.cs:71` | 2.2 |
| B8 | **The contract miner leaves with a partial load.** The tick sends it to deliver as soon as any contract cargo is aboard, so the "fill up to required units or a full hold" logic never gets to run. | `GameLoopService.cs:118-139` vs `MineResourceVolumeCommand.cs:145-149` | 6.1 |
| B9 | **The contract plan never completes.** It only advances on `DeliverableObtainedEvent` and `ContractDeliveryRecordedEvent`, and nothing publishes either. After the contract is fulfilled, the plan stays Active and the assignment stays open. | `ContractPlanService.cs:227-266` | 6.1 |
| B10 | **The command ship idles after scouting.** When the scout plan completes, the ship's last `ScoutWaypointGoal` stays active, so mining and trading treat the ship as busy. It also writes two log lines every tick. | `ScoutAllMarketplacesPlanService.cs:162`, `MiningAutomationService.cs:388-405` | 6.2 |
| B11 | **Prometheus can't scrape `/metrics`.** Only `/health` is exempt from the API key. Separately, `spacetraders_api_throttled_total` counts every 25 ms local wait as a throttle. | `ApiKeyMiddleware.cs:17`, `RateLimitingHandler.cs` | 2.1 |
| B12 | **Log noise.** Information-level logs on every 5 s tick, `System.Net.Http` at Information (about 4 lines per API call), no correlation properties, and the ship symbol logged under three names (`ShipSymbol`, `Symbol`, `Ship`). The production JSON has no rendered message. | `Program.cs:60-74`, tick services | 1.9 |

### Needs your decision (scope or strategy, not bugs)

| # | Question |
|---|---|
| D1 | **Only one contract per reset.** Bootstrap returns early once a plan is Completed or DeferredUnsupported (`ContractPlanService.cs:51-63`), so no second contract is ever taken. Was this a deliberate baseline, or should the bot move on to the next contract? |
| D2 | **Non-mineral contracts are parked as unsupported** (`ContractPlanService.cs:104-127`), and because of D1 that blocks every later contract. Should the bot buy the goods, skip the contract, or do something else? |
| D3 | **The rate limiter takes a token from both the 2/s and the 30-per-60 s bucket** (`RateLimitingHandler.cs:13-32`), which caps the bot at 30 requests a minute. If the API's burst pool is meant as extra capacity on top of 2/s, the bot uses a quarter of its allowance. Check the API docs, then decide. |
| D4 | **The probe plan waits for 200k credits** (`ProbeDeploymentPlanService.cs:71`). Is that threshold what you want? |
| D5 | **Keep the React WebUI**, or let Grafana take over the read-only views and keep the WebUI only for settings? |
| D6 | **Exclude the `spacetraders` database from the nightly `pg_dumpall`** (gembernodes `postgresql-backup`)? Most of it is a cache that every reset wipes anyway. |
| D7 | **Delete `SpaceTradersV3/`?** It's a copy with nothing of its own, and git history keeps it. |
| D8 | **Size guard limits for slice 1.6.** Suggestion: a soft limit of 1 GB (anomaly) and a hard limit of 3 GB (pause automation). |

## Phases

Each slice is one PR, including tests. All of phase 1 must be done before the bot goes back on the
cluster (phase 4).

### Phase 0: Housekeeping

**0.1 Claude's role in `CLAUDE.md`** (done)

**0.2 Archive the old plans**
- Do: move `basics-reset-plan.md`, `REFACTOR_PLAN_ShipAutomationTickEvent_Removal.md`, the
  `docs/*PLAN*.md` files, `docs/contract-plan-implementation.md`,
  `docs/ship-automation-architecture-plan.md` and `docs/implementation/*` into `docs/archive/`.
  Add a README there saying they describe past intentions, not the current code.
- Done when: the only current docs are `README.md`, `PLAN.md`, `docs/HOW_IT_WORKS.md`,
  `docs/GLOSSARY.md`, `spacetraders.md` (the game reference), `CONTRIBUTING.md` and
  `CHANGELOG.md`.

**0.3 `docs/HOW_IT_WORKS.md`**
- Do: describe the current runtime from the code:
  - the startup chain;
  - the 5 s tick and the plans it bootstraps;
  - goal executors, events and handlers;
  - every table and its retention.
- Also fix the stale parts of `README.md`: the `SpaceTraders.App` project, the `k8s/` folder,
  and `/control/sync` and `/reassign`, none of which exist anymore.
- Done when: every later PR that changes behaviour updates this file.

**0.4 Remove `SpaceTradersV3/`.** Waits on D7.

### Phase 1: Safe to run

**1.1 Fix the "already at destination" loop (B1)**
- Goal: navigating to the waypoint a ship is already at ends in the state the caller needs, and
  never re-triggers the executor without progress.
- Do: either have `NavigateToWaypointHandler`'s step 1 dock before emitting the completed event,
  or have the executors handle "at target" themselves without calling navigate. Pick whichever
  keeps a single obvious path.
- Done when: for every executor, tests cover both "docked at target" and "in orbit at target",
  and each case finishes in one step without publishing `ShipNavigationCompletedEvent` again.

**1.2 Per-ship circuit breaker**
- Goal: any future loop is contained to one ship, and it shows up.
- Do: count goal steps per ship in a sliding window. Above N per minute (a setting), the ship's
  goal becomes Blocked with the reason `runaway`. Until phase 3 exists, this means a Warning log
  plus a metric; after that, an anomaly.
- Done when: a test with a looping fake executor trips the breaker.

**1.3 Stop storing in-process messages (B2)**
- Do: remove `UseDurableLocalQueues()`. In-process events don't need to survive a restart:
  `StartupRecoveryService` and `scheduled_ship_events` already resume ships. If nothing else needs
  Wolverine's Postgres storage, drop `PersistMessagesWithPostgresql` too. The alternative is to
  turn the durability agent back on; record the choice in `HOW_IT_WORKS.md`.
- Done when: the soak test (1.10) shows no `wolverine` tables, or flat ones.

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
  pause automation (this needs 1.7). The limits are settings (D8).
- Done when: tests with a fake size source cover both limits.

**1.7 A kill switch that works (B5)**
- Do: the tick, the plan bootstraps and scheduler-triggered goal steps all respect
  `Automation.Enabled`.
- Done when: a test shows a tick with automation disabled issues no ship commands.

**1.8 Server reset during a run (B6)**
- Do: a 401 with the reset-date error pauses automation, logs `ResetDetected`, and stops the
  host. Kubernetes restarts the pod, and startup already registers the new agent through the
  account token. Simpler than re-bootstrapping in-process.
- Done when: a test with a fake port returning the reset error stops the host.

**1.9 Log diet (B12)**
- Do:
  - move per-tick messages to Debug, or log them only when something changes;
  - set `System.Net.Http` to Warning in the Serilog section (the old ConfigMap's
    `Logging__LogLevel__*` variables probably never reached Serilog);
  - use `RenderedCompactJsonFormatter` in Production;
  - use one property name per concept (`ShipSymbol`, `ContractId`, `WaypointSymbol`);
  - push tick and plan context through `LogContext`.
- Done when: an idle bot writes less than one line a minute, and a normal day stays within a
  budget of 50k lines. Loki's 31 days on 10Gi are shared with every app.

**1.10 Soak test**
- Do: run locally (Postgres in Docker) against the live API for a few hours with all of phase 1
  in. Every 15 minutes, record table sizes and message counts.
- Done when: tables grow only with real game activity (market samples, ledger), nothing in
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

**2.2 Publish the missing events (B7)**
- Do: dispatch aggregate domain events, or publish where the change happens, so that credit
  changes, sales, purchases, contract payments and ship purchases reach the ledger and the
  metrics.
- Done when: tests show each of those four producing a ledger row and moving its counter.

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
  - TLS secret `spacetraders-tls` (per-app names since 2026-09-26);
  - no route to the old `spacetraders-app-service`, because that project no longer exists.
- Decide D5 first.

**4.3 First-run watch**
- First hour: messages per minute, database size, log lines per minute, anomalies. Then check
  again after 24 hours, then after a full reset period.
- Phase 6 starts after a clean reset period.

### Phase 5: Claude as mechanic (on your PC)

**5.1 The `/st-investigate` skill** (`.claude/skills/st-investigate/SKILL.md` in this repo)
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
- Done when: the skill reproduces and explains one real anomaly from the first run.

### Phase 6: Make money, one loop at a time

A loop counts as done after a full reset period with no open anomalies for it on the dashboard.
How credits are split stays your call; Claude only fixes deviations from intended behaviour.

- **6.1 Contracts, end to end and repeatable:** B8, B9, plus decisions D1 and D2.
- **6.2 The command ship after scouting:** B10. The ship moves on to its next job instead of
  holding on to the finished scout goal.
- **6.3 Mining drones mine and sell** (`MiningAutomationService`, `MineAndSellGoalExecutor`).
- **6.4 Trading** (`TradingAutomationService`).
- **6.5 Jump gate construction.**

## Changes in gembernodes

This cloud session can only read gembernodes. These changes are made from your PC or by hand:

| Slice | Change |
|---|---|
| 2.4 | `infrastructure/monitoring/dashboards/spacetraders-dashboard.json` plus a `configMapGenerator` entry |
| 2.5 | Rules in `infrastructure/monitoring/grafana-alerting-provisioning.yaml`, then a Grafana rollout restart |
| 4.1 | Database login and read-only login (Postgres and 1Password) |
| 4.2 | `apps/spacetraders/`, `namespaces/spacetraders-namespace.yaml`, `ingress/spacetraders-ingress.yaml`, plus the kustomization entries |
| D6 | `infrastructure/postgresql-backup/backup-cronjob.yaml`: `pg_dumpall --exclude-database=spacetraders` |
