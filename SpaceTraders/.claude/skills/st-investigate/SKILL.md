---
name: st-investigate
description: Investigate SpaceTraders bot misbehaviour on the home cluster and fix it. Use when an anomaly fires, when the bot does something it shouldn't (stuck ship, contract that never progresses, repeating errors, a loop, growing tables), or with "week" for a weekly check. Fixes only things that don't work as intended; never tunes strategy or settings.
---

# st-investigate

> Draft of 2026-10-01 (PLAN.md slice 5.1). Parts depend on later phases: metrics and journal
> events arrive in phase 2, anomalies in phase 3, the read-only database login in slice 4.1.
> Use whatever exists; check what does before relying on it.

Arguments: `$ARGUMENTS` is one of:
- an anomaly (`<rule> <subject>`, for example `contract-no-progress CONTRACT-123`);
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
- run `kubectl apply/delete/edit/patch/exec/scale/rollout`;
- write to the database;
- call `PUT /settings/*` or `POST /control/*`.

Never print or commit secrets: agent token, account token, API key, database password.

## Where the data is

Commands are bash; Git Bash works on Windows. kubectl uses the `default` context. mcp-k8s is
read-only and also fine for pod status and events.

| Source | How to reach it | What it answers | Available |
|---|---|---|---|
| Pod state | `kubectl -n spacetraders get pods,events`; `kubectl -n spacetraders logs deploy/spacetraders-api --since=1h` | Restarts, crash loops, recent logs of the current pod | When deployed |
| Loki logs (31 days) | Grafana Explore at http://192.168.1.230/grafana, or `kubectl -n monitoring port-forward svc/grafana-loki 3100:3100` and query `http://localhost:3100/loki/api/v1/query_range` | Everything the bot logged, by time | When deployed |
| Postgres | `psql -h 192.168.1.232 -U <read-only login> spacetraders` | Current state: ships, goals, assignments, plans, contracts, ledger | After slice 4.1 (read-only login) |
| Internal API | LAN only (D11): `curl -H "X-Api-Key: $KEY" http://192.168.1.230/spacetraders/api/...`. Fallback: `kubectl -n spacetraders port-forward svc/spacetraders-api-service 8080:80` and `http://localhost:8080/spacetraders/api/...` | The bot's own views: `/status/agent`, `/status/ships`, `/status/ships/{symbol}/diagnostics`, `/status/contracts`, `/status/activity`, `/fleet/assignments`, `/health/automation` | When deployed (needs the internal API key) |
| Prometheus | Grafana, or `kubectl -n monitoring port-forward svc/prometheus-server 9090:80` | `spacetraders_*` metrics: credits, ships by state, API calls and 429s, messages by type, DB size | After phase 2 |
| Journal events | Loki: `{namespace="spacetraders"} \|= "EventKind"` | Timeline of contracts, purchases, plans, idle and blocked ships, setting changes | After phase 2 |
| Anomalies | Prometheus `spacetraders_anomaly_active == 1`; Loki events `AnomalyRaised` and `AnomalyCleared` | Which intended behaviour is broken, and since when | After phase 3 |

Check what exists before relying on it. In Grafana Explore, a Prometheus query of
`{__name__=~"spacetraders_.*"}` lists the bot's metrics.

### Useful queries

Production logs are compact JSON (CLEF): `@mt` is the message template, and `@l` is present only
for levels other than Information.

```logql
# Errors and warnings, newest first
{namespace="spacetraders"} |= "\"@l\":\"Error\""
{namespace="spacetraders"} |= "\"@l\":\"Warning\""

# Error volume per hour (spot bursts and loops)
sum(count_over_time({namespace="spacetraders"} |= "\"@l\":\"Error\"" [1h]))

# Everything about one ship (B12: the property may be ShipSymbol, Symbol or Ship)
{namespace="spacetraders"} |= "SHIP-SYMBOL-HERE"
```

```sql
-- Biggest tables (unbounded growth)
SELECT n.nspname||'.'||c.relname AS tbl, pg_size_pretty(pg_total_relation_size(c.oid)) AS size, c.reltuples::bigint AS approx_rows
FROM pg_class c JOIN pg_namespace n ON n.oid = c.relnamespace
WHERE c.relkind IN ('r','p') ORDER BY pg_total_relation_size(c.oid) DESC LIMIT 15;

-- Message volume by type (loops show up here, as long as Wolverine stores messages: B2)
SELECT message_type, status, count(*) FROM wolverine.wolverine_incoming_envelopes
GROUP BY 1, 2 ORDER BY 3 DESC LIMIT 10;
```

`docs/HOW_IT_WORKS.md` lists every table and what writes it.

## Procedure for one symptom

1. **Pin down the symptom.** What happens, to which ship or contract, since when, and how often.
   Save the queries and their results; they go into the PR.
2. **Find the intended behaviour** and where it's written down: a health rule, a test, a code
   comment, `docs/HOW_IT_WORKS.md`, or a decision in `PLAN.md`.
   - If it's a decision, there's nothing to fix. Report it under Noticed if it looks costly.
   - If it isn't written down anywhere, ask the user and stop.
3. **Find the code path.** Orient with `docs/HOW_IT_WORKS.md` (or the graphify query from
   `CLAUDE.md` if graphify works), then read the source. Check `PLAN.md` "Known issues": it may
   already have a B-number.
4. **Reproduce it in a test** under `tests/` that fails for the reason you found.
5. **Fix it minimally**, then run
   `dotnet test SpaceTraders.slnx --filter "Category!=Integration"`.
6. **Update the docs:**
   - `docs/HOW_IT_WORKS.md`, if behaviour changed;
   - `PLAN.md`: add or close the B-number;
   - `CHANGELOG.md`.
7. **Open a PR** with:
   - the symptom;
   - the evidence (queries and log lines, with secrets removed);
   - the intended behaviour and where it's written;
   - the root cause, the fix and the test.

## `week`

1. Collect the last 7 days:
   - restarts and crash loops;
   - errors grouped by `@mt` (count, first and last seen);
   - anomalies opened and cleared (after phase 3);
   - ships idle or stuck;
   - contract progress;
   - table sizes;
   - API 429s and 502s.
2. Report two lists:
   - **Bug candidates**, each with evidence and the intended behaviour it breaks;
   - **Noticed**, for strategy observations, without recommendations to change settings.
3. Then work through the clear bug candidates with the procedure above, one PR each. Ask about
   anything unclear.
