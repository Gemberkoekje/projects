---
name: st-investigate
description: Investigate SpaceTraders bot misbehaviour on the home cluster and fix it. Use when an anomaly fires, when the bot does something it shouldn't (stuck ship, contract that never progresses, repeating errors, a loop, growing tables), or with "week" for a weekly check. Fixes only things that don't work as intended; never tunes strategy or settings.
---

# st-investigate

> PLAN.md slice 5.1. Checked against the bot's first run on the cluster (2026-10-02): every source
> below answered, and the procedure explained the run's one anomaly (B42) and found B45.

Arguments: `$ARGUMENTS` is one of:
- an anomaly (`<rule> <subject>`, for example `ContractStalled cmup71is3f88dt06vr7h29pbw` or
  `ShipStuck SPECTER-3`);
- a ship symbol or contract id;
- a short description of the symptom;
- `week`, for a review of the last 7 days.

## Scope (read first)

Follow "Claude's role in this project" in `SpaceTraders/CLAUDE.md`:

- **In scope:** behaviour that contradicts what is intended. That covers errors, loops, stuck
  ships or plans, state that never advances, data that is never recorded, unbounded growth and
  wasted API calls.
- **Out of scope:** strategy, budgets, thresholds, priorities and settings values. Put such
  observations under **Noticed** and change nothing.
- **Decisions in `PLAN.md` are intended behaviour.** For example, D1 says one contract per reset;
  that is not a bug.
- **If the intended behaviour isn't written down** (health rules, code comments, tests,
  `docs/HOW_IT_WORKS.md`, `PLAN.md`), stop and ask, with the evidence. Don't guess.

**Read-only on the cluster.** Never:
- run `kubectl apply/delete/edit/patch/exec/scale/rollout`, or restart anything;
- write to the database;
- call the bot's internal API: its key also unlocks `PUT /settings/*` and `POST /control/*`, and
  the database and the metrics show the same state.

Never print or commit secrets: the agent token, the account token, the API key, a password.

## Where the data is

`tools/investigate/st.py` (Python, standard library only) reads every source, from the
`SpaceTraders` folder. It starts and stops the port-forwards it needs, prints times in UTC, and
masks anything shaped like a token or a password.

```bash
python tools/investigate/st.py check            # start here: what each source answers
python tools/investigate/st.py prom '<PromQL>' [--range 6h] [--at <time>]
python tools/investigate/st.py logs '<LogQL>' [--since 2h | --from <time> --to <time>] [--group] [--props] [--full]
python tools/investigate/st.py sql "<SQL>"      # or: python tools/investigate/st.py sql - <<'EOF'
```

`--help` on each command lists the rest. Times are ISO (`2026-10-02T09:18Z`, UTC unless they say
otherwise) or a duration ago (`2h`).

| Source | Command | What it answers | Kept |
|---|---|---|---|
| Pods | `st.py check`; `kubectl -n spacetraders logs deploy/spacetraders-api --since=1h` | Restarts, the running image, recent warning events | Events for about an hour |
| Loki | `st.py logs` | Everything the bot logged: errors, the journal, a ship's or a tick's lines | 31 days |
| Prometheus | `st.py prom` | `spacetraders_*` metrics (`docs/HOW_IT_WORKS.md` section 11), scraped every minute | 15 days |
| Postgres | `st.py sql`, as `spacetraders_ro` | Current state: ships, goals, assignments, plans, contracts, ledger, markets | Now |
| Code history | `git log -- SpaceTraders`; gembernodes' `git log -- apps/spacetraders` | Which commit fixed what, and which image ran when ("Deploy SpaceTraders at main <sha>") | Always |

How the helper reaches them, should it fail: `kubectl -n monitoring port-forward
svc/prometheus-server 19090:80` and `svc/grafana-loki 13100:3100` (it reuses one already running on
those ports). The database is 192.168.1.232, database `spacetraders`, login `spacetraders_ro`, with
psql, or psql from the `postgres:17` Docker image where psql isn't installed. The password comes
from psql's password file, `%APPDATA%\postgresql\pgpass.conf` (one line,
`192.168.1.232:5432:spacetraders:spacetraders_ro:<password>`); if it's missing, ask the user to
create it, never to paste the password. mcp-k8s (read-only) works for pod status too.

### Facts that save time

- **Logs** are compact JSON (CLEF): `@m` is the rendered message, `@i` an id of its template, and
  `@l` is present only for levels other than Information. `logs` prints one line per entry; `--props`
  adds the properties, `--full` whole messages (Wolverine's run to dozens of lines).
- **Who logged it:** a line written during a tick carries `Tick` (and its `Plan`, or `ShipSymbol`);
  lines from handlers, such as an arrival's, carry no `Tick`. Two code paths acting on one ship at
  the same moment show as lines with and without `Tick`, interleaved (B45).
- **A flight is two lines** (B53): `NavigateSubCommand: … in transit from … to …, arrives at …`
  when it leaves and `ShipNavigationCompletedHandler: … arrived at …; goal resumed, outcome=…` when
  it lands, plus a `RefuelSubCommand` line for each refuel. Orbiting, docking, the scheduler's
  wake-up and the market refresh on arrival log at Debug and never reach Loki. A departure with no
  arrival line after its arrival time means the arrival chain didn't finish: a wake-up it ignored as
  stale (Debug), or a failure, whose error is logged.
- **A drift takes hours** (slice 6.10c, D45): a mining or siphon trip to a market out of the drone's
  CRUISE reach drifts there first, with a `DriftStarted` journal line, an `arrives at` hours away (about
  2.5 from the middle of X1-DC53 to B7), and `drifting to … to mine …` in `spacetraders_ship_info`. A ship
  in transit is never `ShipStuck`. Its next flight logs `FlightModeSubCommand: … switches from DRIFT to
  CRUISE`; a drone that keeps flying in DRIFT after its drift is a bug.
- **Construction loses money by design** (slice 6.6, D59–D63): supplying the home jump gate pays nothing, so every
  `TripEnded` with `Activity` `construction` is a loss and its purchases are `ConstructionBuy` ledger rows. A builder
  that trades while the construction plan's state (`plan_states`, `Construction`) says `Waiting` (`purchase_order`,
  `waiting_for_credits`, `low_supply`, `trade_volume`, `no_market`) is waiting by design, and so are the probes and
  further ships held behind the gate's load in the purchase order.
- **Starts and deploys:** `|= "Deferred startup initialization completed"` lists every start. When
  lines come from several pods, `logs` prefixes each with the pod's suffix, so a restart or a deploy
  shows as a new suffix. `check` shows the running image; earlier ones are in gembernodes' history.
- **Expected noise:** each start logs a few `Health check "startup" with status Degraded` warnings
  and Kubernetes "Startup probe failed: …503" events while the startup chain runs (4.2); that is the
  probe working, not a bug.
- **The database:** columns are PascalCase, so quote them (`"ShipSymbol"`); plan state is JSON in
  `plan_states."StateJson"`, with enums as numbers (a scout plan's status 2 is Completed);
  `docs/HOW_IT_WORKS.md` section 6 lists every table and what writes it. Never read
  `stored_credentials` (the agent token): the helper refuses, and `check` says whether the login
  can still read it.
- **A metric with no series** means nothing ever set it, which is evidence too: a market that is
  missing from `spacetraders_market_observed_timestamp_seconds` was never fetched.

### Useful queries

```logql
# Warnings and errors per statement: count, first and last seen, template id (@i)
{namespace="spacetraders"} |~ "\"@l\":\"(Warning|Error|Fatal|Critical)\""        (with --group)

# Every occurrence of one statement
{namespace="spacetraders"} |= "\"@i\":\"bd1538c5\""

# Everything about one ship (always ShipSymbol since slice 1.9)
{namespace="spacetraders"} | json | ShipSymbol="SPECTER-1"

# Anomalies raised and cleared, with what was wrong (Details)
{namespace="spacetraders"} | json | EventKind=~"Anomaly.*"                          (with --props)

# The journal: a timeline of the run
{namespace="spacetraders"} | json | EventKind != ""

# Log volume per hour (a loop shows up here)
sum(count_over_time({namespace="spacetraders"}[1h]))                               (with --step 1h)
```

```promql
# Anomalies active now
spacetraders_anomaly_active == 1

# Message volume by type (a loop shows up here; messages aren't stored since slice 1.3)
sum by (type) (rate(spacetraders_messages_handled_total[5m]))

# Each ship: where it is and what the bot has it do; how long it has been in its state
spacetraders_ship_info
time() - spacetraders_ship_status_since_timestamp_seconds

# Every setting as it is now, the Runtime.* flags included (a secret shows "(hidden)")
spacetraders_setting_info

# API calls that failed, and real 429s
sum by (endpoint, status) (increase(spacetraders_api_requests_total{status!~"2.."}[1h]))
sum by (source) (increase(spacetraders_api_throttled_total[1h]))

# Requests initiated per second, and those that went out (slice 2.10): initiated above it means
# requests wait for the budget (or the pause after a 502 refuses them); below it, 429s are retried
sum(rate(spacetraders_api_requests_initiated_total[5m]))
sum(rate(spacetraders_api_requests_total[5m]))
```

```sql
-- Biggest tables (unbounded growth)
SELECT n.nspname||'.'||c.relname AS tbl, pg_size_pretty(pg_total_relation_size(c.oid)) AS size, c.reltuples::bigint AS approx_rows
FROM pg_class c JOIN pg_namespace n ON n.oid = c.relnamespace
WHERE c.relkind IN ('r','p') AND n.nspname NOT IN ('pg_catalog', 'information_schema')
ORDER BY pg_total_relation_size(c.oid) DESC LIMIT 15;

-- The plans, their state and the ships' assignments
SELECT "PlanType", "UpdatedAt", "StateJson" FROM plan_states;
SELECT "ShipSymbol", "Type", "DestWaypoint", "StepIndex", "AssignedAt", "CompletedAt" FROM ship_assignment_records;
```

### Where an anomaly points

Each rule writes down an intended behaviour (`docs/HOW_IT_WORKS.md` section 12); its `Details` say
what it saw. Where to start:

| Rule | Start with |
|---|---|
| `ContractStalled`, `ContractDeadlineAtRisk` | The contract plan's state and its ship: the contract's journal (`ContractDelivered`, `PlanBlocked`), then `ShipStuck` or `ShipLeftIdle` for the plan's ship |
| `ContractLeftOpen` | `ContractPlanService.AdvanceActivePlanAsync` (B9's fix) and errors from the contract plan's bootstrap |
| `ShipStuck` | Everything about the ship (`ShipSymbol`), its goal step's errors, and B17 (ships that stay "in transit") |
| `ShipLeftIdle` | The plan named in `Details`: why it doesn't take the ship (B25 for the starting probe) |
| `CircuitBreakerTripped` | The ship's goal steps just before the trip: a loop like B1 |
| `RepeatingError` | The subject is `<class>: <message template>`, cut at 120 characters. Find the statement in the code; `--group` over the warnings gives its `@i`, and the `@i` query every occurrence, by pod |
| `CreditsUnchanged` | The ships with work (in `Details`) and the ledger: is anything spending or earning? |
| `ApiUnauthorized`, `ApiThrottled` | API requests by endpoint and status; a 401 that isn't a reset (`ResetDetected`) means a rejected token |
| `DbSizeSoftLimit`, `DbSizeHardLimit` | The biggest tables (query above) and their retention (`docs/HOW_IT_WORKS.md` section 6) |

## Procedure for one symptom

1. **Pin down the symptom.** What happens, to which ship or contract, since when, and how often.
   Save the queries and their results; they go into the PR.
2. **Check whether it's known.** `PLAN.md` "Known issues" may have a B-number already, fixed or
   not, and `git log` the commit that fixed it. A fix only counts once the image that runs has it:
   compare `check`'s image with the fix's commit, and look for the symptom after that deploy.
3. **Find the intended behaviour** and where it's written down: a health rule, a test, a code
   comment, `docs/HOW_IT_WORKS.md`, or a decision in `PLAN.md`.
   - If it's a decision, there's nothing to fix. Report it under Noticed if it looks costly.
   - If it isn't written down anywhere, ask the user and stop.
4. **Find the code path.** Orient with `docs/HOW_IT_WORKS.md` (or the graphify query from
   `CLAUDE.md` if graphify works), then read the source. Line the code up with the log lines, to
   the millisecond if two paths may race (`logs` prints milliseconds).
5. **Reproduce it in a test** under `tests/` that fails for the reason you found. For a race,
   interleave the two paths in the test the way the logs show (`ScoutStopSkippedTests` does).
6. **Fix it minimally**, then run
   `dotnet test SpaceTraders.slnx --filter "Category!=Integration"`.
7. **Update the docs:**
   - `docs/HOW_IT_WORKS.md`, if behaviour changed;
   - `PLAN.md`: add or close the B-number;
   - `CHANGELOG.md`.
8. **Open a PR**, one per bug, with:
   - the symptom;
   - the evidence (queries and log lines, with secrets removed);
   - the intended behaviour and where it's written;
   - the root cause, the fix and the test.

   What the fix can't undo (state the bug already left behind, such as a market never fetched)
   goes into the PR too; repairing it is the user's call.

## `week`

1. Collect the last 7 days:
   - restarts, deploys and crash loops (`check`; the starts query; gembernodes' history);
   - warnings and errors per statement (`--group`), each with count, first and last seen;
   - anomalies raised and cleared (`--props` shows `Details` and `ActiveMinutes`);
   - ships idle or stuck (`spacetraders_ship_info`, time in state, the `ShipIdle` journal);
   - contract progress (the contract metrics and the journal);
   - table sizes and the database size;
   - API errors, 429s and 502s (`ApiUnavailable` in the journal).
2. Report two lists:
   - **Bug candidates**, each with evidence and the intended behaviour it breaks;
   - **Noticed**, for strategy observations, without recommendations to change settings.
3. Then work through the clear bug candidates with the procedure above, one PR each. Ask about
   anything unclear.
