# SpaceTraders — Modern Read-Only Frontend Plan

A plan for a new, modern frontend for SpaceTraders that focuses on **observability of automation**, not manual control. The current `SpaceTraders.App` (Razor Pages, server-rendered, polling the database) is operationally useful but does not lend itself to live dashboards, comparative analytics, or rich charting. This plan proposes a replacement that does.

---

## 1. Goals

1. **Read-only.** No buttons that change game state. No undock, no navigate, no buy/sell, no contract accept. The frontend never mutates anything in the SpaceTraders universe; the automation backend owns all actions.
2. **Automation-first.** Surface *what the automation is doing and why*, not raw game primitives. Every screen should help answer "is my strategy working?".
3. **Real-time.** When the backend records an event (ship arrives, market updates, credits change, strategy switches), the UI updates within ~1s without a manual refresh.
4. **Strategy analytics.** First-class support for comparing two (or more) automation runs side-by-side with charts.
5. **Market intelligence.** Time-series visualisations of good prices, supply, and volume across the universe.
6. **Accuracy.** Numbers shown match the database of record. No stale aggregate caches that silently drift. Where data is delayed (e.g. last market scan time) that staleness is shown explicitly.
7. **Operable.** Health, automation status, API rate-limit headroom, and recent errors are always visible.

## 2. Non-goals

- No write/control actions (those stay on the existing internal `/control/*` REST API, used only by operators via API key).
- No replacement of the existing `SpaceTraders.API` host or automation workers; this is purely a new UI tier.
- No mobile-app deliverable (responsive web is enough).
- No multi-user/auth system beyond what the internal API already requires (`X-Api-Key`); the frontend is a personal/operator tool.

## 3. Recommended tech stack

| Concern | Choice | Why |
|---|---|---|
| Framework | **React + TypeScript** with **Vite** | Mature ecosystem for dashboards; TS catches schema drift early. |
| UI kit | **shadcn/ui** + **Tailwind CSS** | Modern look, copy-in components (no heavy runtime), accessible by default. |
| Data fetching | **TanStack Query** | Cache, retries, background refetch, and a clean integration with realtime invalidation. |
| Charts | **Apache ECharts** (via `echarts-for-react`) | Handles dense time series, brushing/zoom, comparison overlays, candlesticks for prices, and is fast on 10k+ points. |
| Realtime transport | **SignalR** (ASP.NET Core) | Already idiomatic for .NET 10; falls back to SSE/long-polling automatically. SSE-only is also acceptable if we want to stay transport-light. |
| Routing | **TanStack Router** or React Router | Either is fine; pick one and stick with it. |
| Date/number | **date-fns**, `Intl.NumberFormat` | Avoid moment; render credits with locale grouping. |
| Tests | **Vitest** + **Playwright** | Unit + a couple of smoke E2E flows. |
| Build/host | New project `SpaceTraders.WebUI` served as static assets behind the existing API host (or its own nginx pod) | Keeps deployment story compatible with current k8s manifests. |

The existing Razor Pages `SpaceTraders.App` stays around during migration and is retired once the new UI has feature parity for the read views we keep.

## 4. Architecture

```
┌────────────────────────┐       SignalR / SSE        ┌─────────────────────────┐
│ SpaceTraders.WebUI     │ ◄─────────────────────────  │ SpaceTraders.API        │
│ (React + TS, static)   │                             │ (existing host)         │
│                        │  REST GET /spacetraders/api │  - read endpoints       │
│  - TanStack Query      │ ──────────────────────────► │  - new /hubs/dashboard  │
│  - ECharts             │                             │  - new /metrics/*       │
└────────────────────────┘                             │  - new /runs/*          │
                                                       └─────────────────────────┘
                                                                  │
                                                                  ▼
                                                       ┌─────────────────────────┐
                                                       │ PostgreSQL              │
                                                       │ + new time-series tables│
                                                       └─────────────────────────┘
```

Key principles:

- **All reads go through the API**, not directly to the DB. The new frontend never gets a connection string. (Today `SpaceTraders.App` reads the DB directly; we don't repeat that.)
- **One SignalR hub** (`/hubs/dashboard`) pushes typed events. The frontend uses those events to invalidate the relevant TanStack Query cache keys, then re-fetches the small REST payload. This keeps payloads cacheable, ETag-able, and easy to test, while still feeling realtime.
- **Read-only by construction.** The frontend bundle is built with no client for `/control/*` endpoints. The API host should additionally enforce that the dashboard's API key (a separate one) only has access to read scopes — see §10.

## 5. Realtime update strategy

Backend emits domain events to the hub when:

- Agent credits change.
- A ship's nav state transitions (in transit → arrived, docked ↔ in orbit).
- A ship completes an action (extract, sell, purchase, refuel, jump, navigate, deliver).
- A contract is accepted / progressed / fulfilled / failed (by automation).
- A market or shipyard scan is recorded.
- The automation strategy/assignment for a ship changes.
- API rate-limit headroom crosses a threshold.
- A run starts/stops or a phase transition happens.
- A `system-alert` toggles.

Each event carries minimal payload (entity id + version/timestamp). The frontend treats them as cache invalidations and re-queries the small typed REST endpoint. This avoids divergence between "what the websocket pushed" and "what the REST endpoint says".

Connection lifecycle:

- Auto-reconnect with exponential backoff.
- On reconnect, the UI shows a "syncing…" indicator and re-queries everything currently mounted.
- A heartbeat from server every 15s; if missed for >30s the UI shows a "live updates paused" banner so the user knows numbers may be stale.

## 6. Page / view inventory

The IA is small and task-oriented. Each view has a clear question it answers.

### 6.1 Overview (`/`)
- Total credits (large), 24h delta, all-time delta.
- Active run name + strategy + uptime.
- Ship count by role (mining, trading, hauling, probe, idle).
- Active contracts with progress bars and time-to-deadline.
- Top 3 best trade opportunities the automation currently sees.
- Sparkline: credits over the last 7 days.
- Health strip: API reachable, automation enabled, rate-limit headroom, leader lease, last sync age.

### 6.2 Fleet (`/fleet`)
- Table of all ships: symbol, role, current waypoint/system, nav state, fuel %, cargo %, current task ("Mining COPPER_ORE at X1-AB-12", "En route to X1-CD-34, ETA 02:14"), credits earned today.
- Filter by role/system/state.
- Click row → ship detail.

### 6.3 Ship detail (`/fleet/:symbol`)
- Header: symbol, frame, role, current task, nav animation (orbit/dock/transit) showing ETA.
- Live timeline of recent activity (last 100 events: nav, dock, extract, sell, refuel, …) with timestamps, durations and credits delta per event.
- Lifetime stats: credits earned, credits spent (fuel, modules, repairs), net P&L, distance travelled, jumps, units extracted by good, units sold by good.
- Cargo bar broken down by good.
- Fuel chart over time.
- Assignment history (what automation role it has been in, with timestamps).
- "What is this ship doing right now and why?" panel that shows the current automation decision (selected trade route, mining target, etc.) and the inputs that produced it.

### 6.4 Finance (`/finance`)
- Credits over time chart with annotations (run start, strategy change, big purchases, contract fulfilled).
- Income vs expenses stacked area, by category: trade margin, contract payouts, mining sales, refuel, ship purchase, module/mount, repairs.
- Per-ship contribution to P&L (bar chart, sortable).
- Per-good profit table (units sold, avg buy, avg sell, margin, total profit).
- **Budget panel**: shows operator-configured budgets (e.g. "max 200k credits/day on ship purchases", "reserve 50k for contract penalties") versus actual spend, with traffic-light status. Budgets are read from `AgentSetting` rows; this view never edits them.

### 6.5 Markets (`/markets`)
- Searchable list of waypoints with markets, last scan time, distance from a chosen reference.
- Heatmap of price vs system for a selected good.
- **Good detail (`/markets/goods/:symbol`)**:
  - Price-over-time chart with separate series for buy price and sell price per waypoint (toggleable legend).
  - Optional candlestick (open/high/low/close per hour or day) for the waypoint with the most data.
  - Supply level over time (ABUNDANT/HIGH/MODERATE/LIMITED/SCARCE rendered as a step chart).
  - Volume traded over time (purchase/sale events recorded by automation).
  - Best buy → sell pairs right now with margin per unit and per round-trip including fuel cost estimate.
- **Waypoint detail (`/markets/waypoints/:symbol`)**: imports/exports/exchanges, current prices, recent transactions involving this waypoint.

### 6.6 Runs & strategy comparison (`/runs`)
- A "run" is a contiguous period during which a particular strategy/configuration was active. Runs are auto-created when configuration changes that affect strategy (or explicitly marked by the operator via the existing settings API).
- List of runs: name, strategy label, start, end (or "active"), starting credits, ending credits, ΔCredits, Δ/hour, ships at start/end, contracts completed.
- **Compare view (`/runs/compare?a=:idA&b=:idB`)**:
  - Side-by-side header cards (strategy, duration, ships, ΔCredits, Δ/hour, credits/ship/hour).
  - Overlaid charts (normalised to "hours since run start" so different absolute timestamps line up):
    - Credits over time.
    - Income by category over time.
    - Cargo throughput (units/hour).
    - Average fuel spent per credit earned.
    - API calls per credit earned (efficiency vs rate-limit budget).
  - Per-good profit comparison table.
  - "Decisions diff": which automation parameters differ between the two runs (read from the snapshot of `AgentSetting` taken at run start).
- Support comparing more than two runs (small multiples) — nice-to-have for v2.

### 6.7 Contracts (`/contracts`)
- Active and historical contracts. Progress, deadlines, payout, accepted-by (which run), profitability after fulfilling (revenue − cost of goods − fuel).
- Timeline view per contract showing each delivery event.

### 6.8 Activity log (`/activity`)
- Unified, filterable feed of all automation events (already partially exposed at `/status/activity`). Filters: ship, system, event type, time range. Free-text search.

### 6.9 Systems & waypoints (`/systems`, `/systems/:symbol`)
- Map (2D scatter) of known systems, coloured by whether automation has been there.
- System detail: list of waypoints, traits, ships currently present, markets, shipyards.
- Read-only — no "send ship here" button.

### 6.10 Health & ops (`/health`)
- API rate limit budget over time (per endpoint group). Helps tune the automation.
- Background-worker heartbeat ages, leader lease holder, recent errors, last-success timestamps for each sync.
- API endpoint usage table (already tracked in `ApiEndpointUsage`).
- A clearly labelled "I am read-only" banner so it's obvious to anyone looking that this UI cannot break things.

### 6.11 Settings (read-only mirror) (`/settings`)
- Shows all `AgentSetting` rows and current effective values, last changed at, last changed by. No edit form.
- Useful when comparing runs to see what was different.

## 7. Backend additions required

The data is mostly there. The gaps that the frontend needs the backend to fill:

1. **Time-series price history** — today the latest `CachedMarket` is overwritten on each scan. Add `MarketPriceSample(waypointSymbol, goodSymbol, observedAt, purchasePrice, sellPrice, supply, activity, tradeVolume)` written on every market refresh. Index on `(goodSymbol, observedAt)` and `(waypointSymbol, goodSymbol, observedAt)`. Retention: keep raw for 30 days, then 1h-aggregated forever.
2. **Credits history** — `AgentCreditsSample(token, observedAt, credits)` written on every change (already known from API responses). Cheap; one row per change.
3. **Ledger / financial events** — `LedgerEntry(occurredAt, shipSymbol, runId, category, amount, goodSymbol?, unitPrice?, units?, waypointSymbol?, sourceEventId)`. `category ∈ {TradeBuy, TradeSell, MiningSell, ContractPayout, ContractDeposit, FuelPurchase, ShipPurchase, ModulePurchase, MountPurchase, Repair, Other}`. This is the source of truth for the Finance view.
4. **Runs** — `Run(id, name, strategyLabel, startedAt, endedAt?, settingsSnapshotJson, startingCredits, endingCredits?)`. A run starts at boot and on settings changes that touch strategy keys; previous run is closed atomically. Activity, ledger, and price samples are tagged with `runId` where applicable.
5. **Ship task & assignment timeline** — promote the in-memory automation decision into a persisted `ShipTaskRecord(shipSymbol, startedAt, endedAt?, taskKind, targetWaypoint?, payloadJson)` so the ship-detail "what is it doing and why" view is reconstructable after restart.
6. **SignalR hub** `Microsoft.AspNetCore.SignalR` with methods:
   - `SubscribeAgent(token)`, `SubscribeShip(symbol)`, `SubscribeMarket(goodSymbol)`, `SubscribeRun(runId)`, etc.
   - Server pushes lightweight `{ kind, id, version, occurredAt }` envelopes; clients use them as cache invalidations.
7. **New REST read endpoints** on the existing `/spacetraders/api` group (all `GET`, all `X-Api-Key`):
   - `/finance/credits-history?from=&to=&runId=`
   - `/finance/ledger?from=&to=&shipSymbol=&category=&runId=`
   - `/finance/summary?runId=` (income/expense by category)
   - `/markets/goods/{symbol}/prices?waypoint=&from=&to=&granularity=raw|hour|day`
   - `/markets/waypoints/{symbol}/prices?from=&to=`
   - `/markets/best-routes?cargoCapacity=&fuelCapacity=&maxJumps=`
   - `/runs`, `/runs/{id}`, `/runs/{id}/summary`, `/runs/compare?a=&b=`
   - `/ships/{symbol}/timeline?from=&to=`
   - `/ships/{symbol}/stats?runId=`
   - `/contracts/{id}/timeline`
   - `/health/automation` (worker heartbeats), `/health/rate-limit/history`
8. **CORS** configured to allow the new web UI origin (configurable, defaults to same-origin when served from the API host).
9. **OpenAPI/Swagger** schema for these read endpoints, used to generate a typed TypeScript client (`openapi-typescript` or NSwag) so the frontend can never silently drift from the backend.

## 8. Data model summary (new tables)

| Table | Purpose | Hot indexes |
|---|---|---|
| `MarketPriceSample` | Time series of buy/sell/supply per (waypoint, good) | `(goodSymbol, observedAt)`, `(waypointSymbol, goodSymbol, observedAt)` |
| `AgentCreditsSample` | Time series of credits per agent token | `(agentToken, observedAt)` |
| `LedgerEntry` | Every credit-affecting event, categorised | `(occurredAt)`, `(runId)`, `(shipSymbol, occurredAt)` |
| `Run` | Strategy run boundaries + settings snapshot | `(startedAt)` |
| `ShipTaskRecord` | Persisted automation task per ship | `(shipSymbol, startedAt)` |

`AgentSetting`, `CachedShip`, `CachedMarket`, `CachedContract`, `ActivityLog`, `ApiEndpointUsage` already exist and are reused.

## 9. Strategy-tuning aids (the "etcetera" the user asked for)

Beyond the explicit asks, these are the views/metrics that meaningfully help iterate on automation strategy:

- **Credits/hour, credits/ship/hour, credits/API-call** as headline efficiency KPIs on each run.
- **Time spent in state**: per ship, % time docked / in transit / mining / idle / refuelling. Idle % is the strongest single indicator of a bad assignment policy.
- **Fuel efficiency**: credits earned per unit fuel burned, per ship and per route.
- **Trade route heatmap**: matrix of (origin waypoint × destination waypoint) coloured by realised margin/hour, sized by units moved. Surfaces the routes the automation is actually exploiting.
- **Decision attribution**: for each "navigate to X to sell Y" decision, log the candidate alternatives and their scored values. The UI exposes the top-N rejected alternatives so you can see whether the scoring function is choosing wisely.
- **Contract ROI**: realised payout − cost of goods − fuel − opportunity cost (credits/hour the ship could have earned trading instead). Helps decide which contracts to accept.
- **Market freshness map**: how stale is the price data the automation is acting on, per waypoint. Highlights blind spots.
- **Rate-limit pressure overlay**: rate-limit headroom plotted alongside credits/hour to show whether scaling automation is API-bound or strategy-bound.
- **What-if replay** (v2 nice-to-have): given the recorded `MarketPriceSample` history, replay an alternative scoring function and report the counterfactual credits/hour. Frontend just needs to render the result.
- **Annotations / notes** (read-only display): operator-authored markers (added via the existing settings/control API or a future API) shown on every time-series chart, e.g. "switched to mining-only at 14:02".
- **Anomaly badges**: flag ships whose credits/hour drops >2σ below their 24h average; flag goods whose price moves >X% in an hour. These are computed server-side and pushed via SignalR.

## 10. Security & accuracy

- The frontend uses a **dashboard-scoped API key** distinct from the operator key. The API host adds a "scope" claim to keys; the dashboard key is allowed only on `GET` endpoints in `/status/*`, `/finance/*`, `/markets/*`, `/runs/*`, `/ships/*`, `/contracts/*`, `/health/*`, `/settings/` (read), and on the SignalR hub. `/control/*` and `PUT /settings/*` are forbidden for this scope. Defence in depth on top of "frontend has no UI for those calls".
- API key never leaves the server; the static UI is served from the same origin as the API and the key is injected as an `HttpOnly` cookie on first load by the host, which the API host then translates to `X-Api-Key` for downstream auth (or the API just trusts the cookie for the dashboard scope). No key in `localStorage`, no key in the JS bundle.
- All numbers shown carry an `as of <timestamp>` tooltip sourced from the row's `LastUpdatedAt` (or `observedAt` for time series). Stale data (>configurable threshold) is rendered with a subtle warning badge, not silently.
- Charts always use the same backend-computed aggregates as the tables on the same page (no client-side recomputation that could disagree with the API).

## 11. Phased delivery

**Phase 0 — foundations (backend)**
- Add `Run`, `LedgerEntry`, `AgentCreditsSample`, `MarketPriceSample`, `ShipTaskRecord` tables and the writes that populate them.
- Add SignalR hub and event publishing from existing event handlers.
- Add new GET endpoints + OpenAPI.

**Phase 1 — frontend skeleton**
- New `SpaceTraders.WebUI` project (Vite + React + TS), Dockerfile, k8s manifest, served behind the API host.
- Auth wiring, SignalR client, generated typed API client, layout, dark/light theme, health strip.
- Implement Overview, Fleet, Ship detail, Activity, Health, Settings (read).

**Phase 2 — finance & markets**
- Implement Finance view (credits-over-time, income/expense, per-good, budgets).
- Implement Markets view + good detail with price-over-time and supply.

**Phase 3 — runs & comparison**
- Run lifecycle on the backend (auto-open/close on settings change).
- Implement Runs list and compare-two-runs view with overlaid charts and decisions-diff.
- Add credits/hour, credits/API-call, idle %, fuel efficiency KPIs.

**Phase 4 — strategy aids**
- Trade-route heatmap, decision attribution, contract ROI, market-freshness map.
- Anomaly badges pushed via SignalR.
- Annotations on charts.

**Phase 5 — retire `SpaceTraders.App`**
- Remove the Razor Pages app from k8s once the new UI covers all the views still in use, leaving only the API host + WebUI + Postgres.

## 12. Open questions

1. Do we want the frontend served by the existing API host or as its own pod (separate scaling, separate cache headers)?
2. Retention policy for `MarketPriceSample` and `LedgerEntry` — is 30 days raw + indefinite hourly aggregates acceptable, or do we want longer raw retention for replay?
3. Should "run" boundaries be fully automatic (any change to a strategy-tagged setting closes and opens a run) or operator-driven via a `POST /runs` call (which is a write — fine because it's still on the operator-scoped key, not the dashboard key)?
4. SignalR vs plain Server-Sent Events — SSE is simpler and read-only by nature, which fits the goal; SignalR is more idiomatic in .NET. Recommend SignalR but flag SSE as a valid alternative.
5. Is multi-agent (multiple tokens) something the frontend needs to switch between (the current Razor app already does via `AgentViewSelection`)? Plan assumes yes; the agent selector lives in the top bar.
