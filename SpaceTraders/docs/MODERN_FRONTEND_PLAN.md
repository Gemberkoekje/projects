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
| Build/host | New project `SpaceTraders.WebUI` built as static assets, served from its **own pod** (nginx or `serve`). Uses the existing `SpaceTraders.API` — no second API. | Clean separation; API host stays unchanged; CORS configured for the WebUI origin. |

The existing Razor Pages `SpaceTraders.App` stays around during migration and is retired once the new UI has feature parity for the read views we keep.

## 4. Architecture

```
┌────────────────────────┐       SignalR / WSS         ┌─────────────────────────┐
│ SpaceTraders.WebUI     │ ◄──────────────────────────  │ SpaceTraders.API        │
│ (React + TS, static)   │                              │ (existing host, 1 API)  │
│ own pod / nginx        │  REST GET /spacetraders/api  │  - existing endpoints   │
│                        │ ───────────────────────────► │  - new /hubs/dashboard  │
│  - TanStack Query      │                              │  - new /finance/*       │
│  - ECharts             │                              │  - new /runs/*          │
│  - SignalR client      │                              │  - new /universe/*      │
└────────────────────────┘                              └─────────────────────────┘
                                                                   │
                                                                   ▼
                                                        ┌─────────────────────────┐
                                                        │ PostgreSQL              │
                                                        │ + new time-series tables│
                                                        └─────────────────────────┘
```

Key principles:

- **One API, separate UI pod.** `SpaceTraders.WebUI` is its own deployment (nginx serving static files); `SpaceTraders.API` is the only backend. No second API is introduced. CORS is configured on the API to allow the WebUI origin.
- **All reads go through the API**, not directly to the DB. The new frontend never gets a connection string. (Today `SpaceTraders.App` reads the DB directly; we don't repeat that.)
- **One SignalR hub** (`/hubs/dashboard`) pushes typed events. The frontend uses those events to invalidate the relevant TanStack Query cache keys, then re-fetches the small REST payload. This keeps payloads cacheable, ETag-able, and easy to test, while still feeling realtime.
- **Read-only by construction.** The frontend bundle is built with no client for `/control/*` endpoints. The API host enforces that the dashboard's API key (a separate one) only has access to read scopes — see §10.

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
- A "run" is a contiguous period during which a particular strategy/configuration was active.
- **Run lifecycle**: auto-created when configuration that affects strategy changes; the previous run closes atomically. A run can also be **scheduled in advance** — operators create a `ScheduledRun` (via the existing operator-scoped settings/control API) with an optional `activatesAt` timestamp or `activatesOnNextRestart` flag. The current run stays open until the scheduled time (or next restart) arrives; only then does the transition happen. This means strategy changes can be queued for future runs without interrupting the current one.
- List of runs: name, strategy label, start, end (or "active"), starting credits, ending credits, ΔCredits, Δ/hour, ships at start/end, contracts completed. Scheduled/pending future runs shown with a distinct badge.
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
- Map (2D scatter) of known systems, coloured by whether automation has been there. See §6.12 for the full universe map.
- System detail: list of waypoints, traits, ships currently present, markets, shipyards. See §6.13 for the in-system map.
- Read-only — no "send ship here" button.

### 6.10 Health & ops (`/health`)
- API rate limit budget over time (per endpoint group). Helps tune the automation.
- Background-worker heartbeat ages, leader lease holder, recent errors, last-success timestamps for each sync.
- API endpoint usage table (already tracked in `ApiEndpointUsage`).
- A clearly labelled "I am read-only" banner so it's obvious to anyone looking that this UI cannot break things.

### 6.11 Settings (read-only mirror) (`/settings`)
- Shows all `AgentSetting` rows and current effective values, last changed at, last changed by. No edit form.
- Useful when comparing runs to see what was different.
- **Scheduled runs panel**: shows any pending `ScheduledRun` (name, strategy, queued settings snapshot, `activatesAt` or "on next restart"). Operator creates/cancels these via the control API; this view is read-only.

### 6.12 Universe map (`/universe`)

A full 2D scatter-plot of every known system in the current server instance.

- **X/Y axes** are the SpaceTraders coordinate plane (`x`, `y` from `CachedSystem`). Each system is a dot; hover shows symbol, faction, type, and whether it has been visited.
- **Colour encoding**:
  - Green — visited (automation has been here).
  - Orange — known but unvisited.
  - Grey — charted by others (scanned from the system list) but never visited by our agent.
- **Jump-gate overlay**: draw lines between systems with known jump-gate connections (`cached_jump_gate_connections`). Lines are solid for confirmed bi-directional connections and dashed for one-way/unconfirmed.
- **Faction regions**: optional soft background shading by faction territory (if faction headquarters coordinates are known).
- **Ships layer**: live dots (updated via SignalR) showing each ship's current system. In-transit ships are rendered along the line between source and destination, positioned by interpolated ETA progress.
- **Interaction**:
  - Click a system → opens `/systems/:symbol` detail panel (side-sheet, no full-page nav).
  - Click a jump-gate line → shows both connected systems and the ships currently queued for the connection.
  - Zoom and pan (mouse wheel / touch pinch). "Reset view" button.
  - Search box to jump to a named system.
- **Exploration frontier**: a toggle that highlights systems reachable in ≤N jumps from our current position (N configurable, default 3), useful for planning scouting runs.

### 6.13 System map (`/systems/:symbol`)

A zoomed-in 2D map of a single system's waypoints.

- **Layout**: waypoints placed by their `x`/`y` coordinates within the system. Orbital bodies (moons, orbitals) are shown as small satellites clustered around their parent.
- **Waypoint icons** by type: asteroid belt (ring), planet (circle), moon (small circle), jump gate (diamond), station (square), gas giant (large circle with gradient), asteroid (irregular), debris field, etc.
- **Trait badges**: MARKETPLACE, SHIPYARD, UNCHARTED, MINERAL_DEPOSITS, VOLCANIC, STRIPPED, etc., shown as small icon overlays on the waypoint dot.
- **Ship positions**: live dots per ship currently in this system (position updates via SignalR), with a tooltip showing symbol, role, current task, and ETA if in transit within the system.
- **Jump-gate connections**: a button to toggle an edge from this system's jump gate to every connected system symbol (abbreviated), linking out to the universe map at that position.
- **Automation annotations**: a coloured ring on waypoints the automation is actively using (mining, trading, probing) so the operator can see the activity hotspots at a glance.
- **Side panel on click**: opens waypoint detail (traits, market snapshot, shipyard snapshot, last-scan age, chart status) without leaving the map.
- **Mini-breadcrumb**: "Universe → :system" for easy navigation back.

## 7. Backend additions required

The data is mostly there. The gaps that the frontend needs the backend to fill:

1. **Time-series price history** — today the latest `CachedMarket` is overwritten on each scan. Add `MarketPriceSample(waypointSymbol, goodSymbol, observedAt, purchasePrice, sellPrice, supply, activity, tradeVolume)` written on every market refresh. Index on `(goodSymbol, observedAt)` and `(waypointSymbol, goodSymbol, observedAt)`. Retention: keep raw for **7 days**, hourly aggregates for **90 days**; raw beyond 7 days is pruned by a nightly job.
2. **Credits history** — `AgentCreditsSample(token, observedAt, credits)` written on every change (already known from API responses). Cheap; one row per change. Retained for 90 days; older samples are pruned keeping one row per hour.
3. **Ledger / financial events** — `LedgerEntry(occurredAt, shipSymbol, runId, category, amount, goodSymbol?, unitPrice?, units?, waypointSymbol?, sourceEventId)`. `category ∈ {TradeBuy, TradeSell, MiningSell, ContractPayout, ContractDeposit, FuelPurchase, ShipPurchase, ModulePurchase, MountPurchase, Repair, Other}`. Raw ledger entries retained for **30 days**; a nightly job rolls them into `RunCreditHighlight` (see §8) so the Finance view always has the per-run summary regardless of raw age.
4. **Runs** — `Run(id, name, strategyLabel, startedAt, endedAt?, settingsSnapshotJson, startingCredits, endingCredits?)`. Auto-created when a strategy-relevant setting changes; previous run closes atomically. Additionally, a `ScheduledRun(id, name, strategyLabel, scheduledSettingsJson, activatesAt?, activatesOnNextRestart, createdAt)` table lets operators queue a future run via the operator-scoped control API (`POST /control/runs/schedule`, `DELETE /control/runs/schedule/:id`) without closing the current run. When `activatesAt` is reached (or on next process startup if `activatesOnNextRestart`), the pending `ScheduledRun` is promoted to an active `Run` and the current run closes. Activity, ledger, and price samples are tagged with `runId` where applicable.
5. **Run credit highlights** — `RunCreditHighlight(runId, occurredAt, credits, deltaCredits, eventKind, label?)`. Written for every significant credit event during a run (start, contract payout, large purchase, hourly snapshot, end). These rows are kept **indefinitely** — they are small and directly answer "how did this run go credit-wise?" without touching the pruned raw ledger. The Finance and Runs views use these for historical runs; they fall back to live `LedgerEntry` for the active run.
6. **Ship task & assignment timeline** — promote the in-memory automation decision into a persisted `ShipTaskRecord(shipSymbol, startedAt, endedAt?, taskKind, targetWaypoint?, payloadJson)` so the ship-detail "what is it doing and why" view is reconstructable after restart.
7. **Jump-gate connections cache** — `CachedJumpGateConnection(fromSystem, toSystem, waypointSymbol, recordedAt)` for the universe map jump-link overlay. Already partially exists from Phase 5; verify it is queryable by system pair.
8. **SignalR hub** `Microsoft.AspNetCore.SignalR` with methods:
   - `SubscribeAgent(token)`, `SubscribeShip(symbol)`, `SubscribeMarket(goodSymbol)`, `SubscribeRun(runId)`, etc.
   - Server pushes lightweight `{ kind, id, version, occurredAt }` envelopes; clients use them as cache invalidations.
9. **New REST read endpoints** on the existing `/spacetraders/api` group (all `GET`, all `X-Api-Key`):
   - `/finance/credits-history?from=&to=&runId=`
   - `/finance/ledger?from=&to=&shipSymbol=&category=&runId=`
   - `/finance/summary?runId=` (income/expense by category)
   - `/finance/run-highlights?runId=` (credit highlight events for a specific run, works for any age)
   - `/markets/goods/{symbol}/prices?waypoint=&from=&to=&granularity=raw|hour|day`
   - `/markets/waypoints/{symbol}/prices?from=&to=`
   - `/markets/best-routes?cargoCapacity=&fuelCapacity=&maxJumps=`
   - `/runs`, `/runs/{id}`, `/runs/{id}/summary`, `/runs/compare?a=&b=`
   - `/runs/scheduled` (list pending scheduled runs)
   - `/ships/{symbol}/timeline?from=&to=`
   - `/ships/{symbol}/stats?runId=`
   - `/contracts/{id}/timeline`
   - `/health/automation` (worker heartbeats), `/health/rate-limit/history`
   - `/universe/systems` (all known systems with coordinates + jump-gate edge list for map rendering)
   - `/universe/systems/{symbol}/map` (waypoints with coordinates, traits, orbital relationships)
10. **OpenAPI/Swagger** schema for these read endpoints, used to generate a typed TypeScript client (`openapi-typescript` or NSwag) so the frontend can never silently drift from the backend.

## 8. Data model summary (new tables)

| Table | Purpose | Retention | Hot indexes |
|---|---|---|---|
| `MarketPriceSample` | Time series of buy/sell/supply per (waypoint, good) | 7 days raw; 90 days hourly agg | `(goodSymbol, observedAt)`, `(waypointSymbol, goodSymbol, observedAt)` |
| `AgentCreditsSample` | Time series of credits per agent token | 90 days (1 row/hour after 7 days) | `(agentToken, observedAt)` |
| `LedgerEntry` | Every credit-affecting event, categorised | 30 days raw; rolled into `RunCreditHighlight` | `(occurredAt)`, `(runId)`, `(shipSymbol, occurredAt)` |
| `RunCreditHighlight` | Compact per-run credit milestones for long-term history | Indefinite (small) | `(runId, occurredAt)` |
| `Run` | Strategy run boundaries + settings snapshot | Indefinite | `(startedAt)` |
| `ScheduledRun` | Future run queued by operator without closing current run | Until promoted or cancelled | `(activatesAt)` |
| `ShipTaskRecord` | Persisted automation task per ship | 30 days | `(shipSymbol, startedAt)` |
| `CachedJumpGateConnection` | Jump-gate edges for universe map overlay | Until overwritten | `(fromSystem)`, `(toSystem)` |

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
- The dashboard API key is stored in the WebUI container's environment (injected at deploy time as an env var, read by the nginx config or a thin start-up script that writes it into the built JS config). It is **not** baked into the static bundle. No key in the JS source or `localStorage`.
- **CORS**: `SpaceTraders.API` is configured with a CORS policy allowing the WebUI origin (configurable via `appsettings`, e.g. `WebUI:Origin`). Only `GET` and the SignalR upgrade are allowed for the dashboard scope.
- All numbers shown carry an `as of <timestamp>` tooltip sourced from the row's `LastUpdatedAt` (or `observedAt` for time series). Stale data (>configurable threshold) is rendered with a subtle warning badge, not silently.
- Charts always use the same backend-computed aggregates as the tables on the same page (no client-side recomputation that could disagree with the API).

## 11. Phased delivery

## 11. Phased delivery

### Phase 0 — backend foundations

**0a — new tables & writes** ✅ *Implemented*
- ✅ Added `Run`, `ScheduledRun`, `LedgerEntry`, `RunCreditHighlight`, `AgentCreditsSample`, `MarketPriceSample`, `ShipTaskRecord` entity classes and table creation in `SpaceTradersDatabaseInitializer`.
- ✅ Added `LedgerCategory` enum to Domain.
- ✅ Wired writes into event handlers: `AgentCreditsSampleHandler` (credits change → `AgentCreditsSample`), `MarketPriceSampleHandler` (market sync → `MarketPriceSample`), `LedgerEntryHandler` (every credit-affecting action → `LedgerEntry`), `ShipTaskRecordHandler` (ship assignment change → `ShipTaskRecord`).
- ✅ Added new domain events: `CargoPurchasedEvent`, `ShipRefueledEvent`, `ShipRepairedEvent`, `MountInstalledEvent`, `ModuleInstalledEvent` and published them from `BuyCargoHandler`, `RefuelShipHandler`, `RepairShipHandler`, `InstallMountHandler`, `InstallModuleHandler`.
- ✅ Added repository interfaces and implementations: `IAgentCreditsSampleRepository`, `IMarketPriceSampleRepository`, `ILedgerRepository`, `IShipTaskRecordRepository`.
- ✅ `MarketDataRefreshedEvent` enriched with `TradeGoodsJson` payload to avoid a second DB read in the sample handler.

**0b — run lifecycle** ✅ *Implemented*
- ✅ Added `IActiveRunIdProvider` interface and `ActiveRunIdProvider` singleton implementation (Application) — tracks current active run ID in memory, removing the need for a DB round-trip on every ledger write.
- ✅ Added `IRunLifecycleManager` interface (Application) — `RotateForSettingsChangeAsync` called by `SettingsEndpoints.PUT /{key}` for strategy-relevant key changes.
- ✅ Added `ActiveRunInfo` and `PendingScheduledRunInfo` port models (Application).
- ✅ Added `IRunRepository` interface (Application) — covers `GetActiveRunAsync`, `OpenRunAsync`, `CloseRunAsync`, `GetPendingScheduledRunsAsync`, `ScheduleRunAsync`, `DeleteScheduledRunAsync`, `AppendCreditHighlightAsync`, `GetRunCountAsync`.
- ✅ Added `RunRepository` implementation (Infrastructure.Persistence).
- ✅ Updated `LedgerRepository` to automatically tag entries with `activeRunIdProvider.ActiveRunId` (explicit `runId` parameter still overrides).
- ✅ Registered `ActiveRunIdProvider`, `IActiveRunIdProvider`, and `IRunRepository` in `DependencyInjection.cs` (Persistence).
- ✅ Created `RunLifecycleService` (API/Services) — implements `IHostedService` + `IRunLifecycleManager`; on startup promotes pending `ScheduledRun`s or resumes/opens a fresh `Run`; 60 s `PeriodicTimer` loop promotes time-triggered `ScheduledRun`s; `RotateForSettingsChangeAsync` closes + reopens on strategy-relevant key changes.
- ✅ Updated `SettingsEndpoints` (`PUT /{key}`) to call `IRunLifecycleManager.RotateForSettingsChangeAsync`.
- ✅ Added `POST /control/runs/schedule` and `DELETE /control/runs/schedule/{id}` endpoints.
- ✅ Registered `RunLifecycleService` as singleton + `IHostedService` in `Program.cs`.
- ✅ Updated `DiValidationTests` to mock `IActiveRunIdProvider`, `IRunLifecycleManager`, and `IRunRepository`.
- ✅ Added `RunLifecycleServiceTests` (5 unit tests covering startup paths and settings-change rotation).
- Note: `RunCreditHighlight` rows are now written on run open and close via `AppendCreditHighlightAsync`.

**0c — nightly data retention jobs** ✅ *Implemented*
- ✅ Added `PruneAsync(rawRetentionCutoff, aggregateRetentionCutoff)` to `IMarketPriceSampleRepository` and `IAgentCreditsSampleRepository`; implemented with a two-phase SQL strategy: delete hourly-duplicate rows in the 7–90-day window (`NOT IN (SELECT MIN(id) ... GROUP BY ... date_trunc('hour', …))`), then delete everything older than 90 days.
- ✅ Added `PruneAsync(olderThan)` to `ILedgerRepository` and `IShipTaskRecordRepository`; implemented with `ExecuteDeleteAsync`.
- ✅ Created `DataRetentionService` (`SpaceTraders.Application/Automation/`) — `BackgroundService` with a 24 h loop; runs all four prune operations and logs counts when rows are deleted.
- ✅ Registered `DataRetentionService` as a hosted service in `Program.cs`.
- ✅ Fixed `SpaceTradersApiFactory` (`ApiIntegrationTests`) — added a properly-stubbed `IRunRepository` mock (returns an active run so `RunLifecycleService` resumes without a DB call), restoring the 20 previously-failing `ApiIntegrationTests`.
- ✅ Added `DataRetentionServiceTests` (5 unit tests: calls all four repos, passes correct cutoffs to each, propagates repository exceptions).

**0d — SignalR hub & read endpoints** ✅ *Implemented*
- ✅ Added `IDashboardNotifier` interface to `SpaceTraders.Application.Interfaces`.
- ✅ Created `DashboardHub` (typed SignalR hub with `IDashboardHubClient.ReceiveInvalidation`) at `/hubs/dashboard`.
- ✅ Created `DashboardNotifier` singleton implementation of `IDashboardNotifier` that uses `IHubContext<DashboardHub, IDashboardHubClient>`; swallows exceptions so SignalR failures never propagate to event handlers.
- ✅ Updated `AgentCreditsSampleHandler`, `LedgerEntryHandler`, `MarketPriceSampleHandler`, and `ShipTaskRecordHandler` to inject `IDashboardNotifier` and push lightweight invalidation events (`"agent-credits"`, `"ship"`, `"market"`, `"contract"`) after each write.
- ✅ Extended `IAgentCreditsSampleRepository` with `GetRangeAsync(from, to)`; extended `ILedgerRepository` with `GetRangeAsync(...)` and `GetSummaryAsync(runId)`; extended `IShipTaskRecordRepository` with `GetTimelineAsync(symbol, from, to)`; extended `ISystemRepository` with `GetAllAsync()`; extended `IMarketPriceSampleRepository` with `GetGoodPricesAsync(...)` and `GetWaypointPricesAsync(...)`.
- ✅ Extended `IRunRepository` with `GetAllAsync()`, `GetByIdAsync(id)`, `GetScheduledRunsAsync()`, and `GetRunHighlightsAsync(runId)`.
- ✅ Implemented all new repository query methods in the corresponding `SpaceTraders.Infrastructure.Persistence.Repositories` classes.
- ✅ Added new endpoint files: `FinanceEndpoints` (`/finance/credits-history`, `/finance/ledger`, `/finance/summary`, `/finance/run-highlights`), `RunsEndpoints` (`/runs`, `/runs/scheduled`, `/runs/compare`, `/runs/{id}`, `/runs/{id}/summary`), `MarketsEndpoints` (`/markets/goods/{symbol}/prices`, `/markets/waypoints/{symbol}/prices`, `/markets/best-routes`), `UniverseEndpoints` (`/universe/systems`, `/universe/systems/{symbol}/map`), `ShipsReadEndpoints` (`/ships/{symbol}/timeline`, `/ships/{symbol}/stats`), `ContractsReadEndpoints` (`/contracts/{id}/timeline`), `HealthExtendedEndpoints` (`/health/automation`, `/health/rate-limit/history`).
- ✅ Added CORS policy `"Dashboard"` in `Program.cs` allowing the configured `WebUI:Origin` (falls back to any origin for dev when blank).
- ✅ Registered `AddSignalR()`, `DashboardNotifier` singleton, and `MapHub<DashboardHub>("/hubs/dashboard")` in `Program.cs`.
- ✅ Added `WebUI:Origin` key to `appsettings.json`.
- ✅ Updated `DiValidationTests` to mock `IDashboardNotifier`.
- Note: TypeScript client generation (`openapi-typescript`) is deferred to Phase 1a when the `SpaceTraders.WebUI` project is created.

### Phase 1 — frontend skeleton

**1a — project setup** ✅ *Implemented*
- ✅ Created `SpaceTraders.WebUI` project: Vite + React + TypeScript, shadcn/ui dependencies (class-variance-authority, clsx, tailwind-merge, lucide-react), Tailwind CSS v4.
- ✅ Configured Tailwind CSS v4 via `@tailwindcss/vite` plugin; set up `@` path alias.
- ✅ Configured Vitest (jsdom environment, `@testing-library/react`, `@testing-library/jest-dom`); 3 passing smoke tests.
- ✅ Runtime config mechanism: `public/config.js` stub for dev; `docker-entrypoint.sh` writes `window.__RUNTIME_CONFIG__` from `DASHBOARD_API_KEY` and `API_BASE_URL` env vars at container startup (key never baked into the bundle); `src/config.ts` reads it.
- ✅ `Dockerfile.webui` (multi-stage: `node:22-alpine` build → `nginx:1.27-alpine`; entrypoint script runs before nginx starts).
- ✅ `k8s/deployment-webui.yaml` — nginx container, `DASHBOARD_API_KEY` from secret, `API_BASE_URL` from configmap, probes, resource limits.
- ✅ `k8s/service-webui.yaml` — ClusterIP service on port 80.
- ✅ `k8s/ingress.yaml` updated — `/` now routes to `spacetraders-webui` instead of `spacetraders-app`.
- ✅ CI pipeline updated: new `build-webui` job (`npm ci` → `npm test` → `npm run build`); `docker` job now depends on both `build` and `build-webui` and pushes `spacetraders-webui` image to GHCR.

**1b — auth, transport & layout** ✅ *Implemented*
- ✅ Installed `@tanstack/react-query` (v5), `@microsoft/signalr` (v10), `react-router` (v7).
- ✅ `src/lib/api-fetch.ts` — `apiFetch<T>` wrapper injects `X-Api-Key` header (from `config.dashboardApiKey`) into every fetch; `Content-Type: application/json` is set only when a body is present; caller headers are merged before the key so it cannot be accidentally overridden.
- ✅ `src/lib/query-client.ts` — `QueryClient` with `staleTime: 30 s`, `retry: 2`.
- ✅ `src/lib/theme.tsx` — `ThemeProvider` + `useTheme` hook; persists `'light' | 'dark'` in `localStorage`; respects `prefers-color-scheme` system preference as fallback; applies class to `document.documentElement`.
- ✅ `src/lib/signalr.tsx` — `SignalRProvider` (inner component uses `useQueryClient`); auto-reconnect with delays `[0, 2 s, 5 s, 10 s, 30 s]`; on reconnect invalidates all TanStack Query cache; `liveUpdatesPaused` is true when disconnected/reconnecting or when server heartbeat has not been received for >30 s. Hub URL uses `config.hubBaseUrl` (independent of the REST API base path) so the SignalR path `/hubs/dashboard` is routed correctly through the ingress.
- ✅ `src/index.css` — Tailwind CSS v4 `@custom-variant dark` for class-based dark mode; CSS custom properties for light/dark design tokens mapped to Tailwind utilities via `@theme inline`.
- ✅ `src/components/ui/ThemeToggle.tsx` — Sun/Moon button, aria-label describes current action.
- ✅ `src/components/ui/LiveUpdatesBanner.tsx` — yellow banner shown when `liveUpdatesPaused`; distinguishes reconnecting / disconnected / heartbeat-stale states.
- ✅ `src/components/layout/TopNav.tsx` — logo, connection-state dot (green/yellow/red with accessible title), theme toggle.
- ✅ `src/components/layout/Sidebar.tsx` — `NavLink` items for all 10 pages with `lucide-react` icons; active item highlighted.
- ✅ `src/components/layout/AppShell.tsx` — full layout: TopNav → LiveUpdatesBanner → Sidebar + `<main>`; React Router `<Routes>` defined here; all 10 routes wired to placeholder pages.
- ✅ Placeholder pages created for all routes: Overview, Fleet, Finance, Markets, Runs, Universe, Contracts, Activity, Health, Settings.
- ✅ `src/App.tsx` — wraps `QueryClientProvider` → `ThemeProvider` → `SignalRProvider` → `BrowserRouter(basename="/spacetraders/dashboard")` → `AppShell`.
- ✅ **Base-path alignment**: `vite.config.ts` sets `base: '/spacetraders/dashboard/'` so all built asset URLs are rooted there; `index.html` adds `<base href="/spacetraders/dashboard/">` as the first `<head>` element so `config.js` (and any other relative references) resolve correctly at any route depth; `BrowserRouter` uses the matching `basename`.
- ✅ `nginx.conf` — nginx handles the sub-path internally via `rewrite ^/spacetraders/dashboard/(.*)$ /$1 break` rules; SPA fallback serves `index.html`; `/healthz` endpoint added for probes; no ingress prefix-stripping required.
- ✅ `src/config.ts` — added `hubBaseUrl` (default `/hubs`) alongside `apiBaseUrl` and `dashboardApiKey`; `docker-entrypoint.sh` and `public/config.js` dev stub updated accordingly.
- ✅ Tests: `App.test.tsx` updated (4 tests: top nav, sidebar presence, all 10 nav links, theme toggle); `apiFetch.test.ts` added (6 tests: header injection, URL construction, JSON parse, error throw, no Content-Type on GET, Content-Type on POST with body); `theme.test.tsx` added (7 tests: default light, DOM class, localStorage persist, read stored theme, toggle, system preference, outside-provider safety); `signalr.test.tsx` added (3 tests: renders children, exposes state, initial paused=true); `setup.ts` updated with class-based `HubConnectionBuilder` mock and `history.pushState` to base path. Total: 22 tests passing.

**1c — core read views** ✅ *Implemented*
- ✅ `src/types.ts` — TypeScript type definitions for all API response shapes (AgentDto, ShipDto, ContractDto, TradeOpportunityDto, RateLimitStatusDto, SystemAlertsDto, ActivityLogDto, SettingDto, RunSummaryDto, ScheduledRunDto, ShipTaskRecordDto, LedgerSummaryDto, AutomationHealthDto, ApiEndpointUsageDto, CreditSampleDto).
- ✅ Overview (`/`): credits (formatted with locale grouping), 24h delta derived from `/finance/credits-history`, active run name + strategy + uptime, ship counts (total / in-transit / docked / in-orbit), active contracts with per-deliverable progress bars and time-to-deadline, top trade opportunity card, SVG credits sparkline, health strip with alert dots (API, automation, token, cache, contract deadline, server reset).
- ✅ Fleet (`/fleet`): filterable ship table; filters by symbol search, nav state (all/transit/docked/orbit), and system; fuel and cargo progress bars; ETA display for in-transit ships; "Details" link per row navigating to ship detail.
- ✅ Ship detail (`/fleet/:symbol`): added `/fleet/:symbol` route in `AppShell`; `ShipDetailPage` shows header with nav state badge + waypoint, fuel/cargo bars, lifetime stats table from `/ships/{symbol}/stats`, recent activity timeline from `/status/activity?ship=`, and task timeline from `/ships/{symbol}/timeline`.
- ✅ Activity log (`/activity`): paginated event feed from `/status/activity`; filters by ship (populated from `/status/ships`) and event type (auto-populated from loaded events); previous/next pagination.
- ✅ Health & ops (`/health`): automation status (enabled/leader) from `/health/automation`; rate-limit headroom bar + stats from `/status/rate-limit`; API endpoint usage table from `/health/rate-limit/history`; read-only badge.
- ✅ Settings mirror (`/settings`): `AgentSetting` rows table from `/settings/`; scheduled runs panel from `/runs/scheduled` showing activation condition; read-only badge.
- ✅ Tests: `pages.test.tsx` added (21 tests covering all 6 pages: headings, loading states, data rendering, filter controls, pagination). Total: 43 tests passing.

### Phase 2 — finance & markets

**2a — finance views** ✅ *Implemented*
- ✅ Finance (`/finance`): run selector (active vs historical runs), credits-over-time SVG line chart, income/expense by category table with proportional bars, summary stat cards (total income, total expenses, net P&L), budget panel (expense breakdown by category), per-ship P&L table (aggregated from `/finance/ledger`), per-good profit table (units sold, avg buy/sell price, total profit — computed client-side from `/finance/ledger`).
- ✅ Falls back to `RunCreditHighlight` (via `/finance/run-highlights`) for historical runs; uses live `CreditSample` (via `/finance/credits-history`) for the active run.
- ✅ Added `LedgerEntryDto` and `RunCreditHighlightDto` TypeScript types to `src/types.ts`.
- ✅ Tests: `pages.test.tsx` extended with 6 FinancePage tests (heading, loading state, run selector, category summary, per-ship P&L, per-good profit, credits chart). Total: 50 tests passing.

**2b — market views**
- Markets list (`/markets`): searchable waypoint list, heatmap by good.
- Good detail (`/markets/goods/:symbol`): price-over-time, candlestick, supply step chart, best buy→sell pairs.
- Waypoint detail (`/markets/waypoints/:symbol`): imports/exports/exchanges, current prices, recent transactions.

### Phase 3 — runs & comparison

**3a — runs list & detail**
- Runs list (`/runs`): all runs + scheduled/pending runs with distinct badge.
- Run detail: summary card, per-category income, ships at start/end, contracts completed.

**3b — compare view**
- Compare (`/runs/compare?a=&b=`): side-by-side headers, overlaid normalised charts (credits, income, cargo throughput, fuel efficiency, API efficiency), per-good table, decisions diff.

**3c — efficiency KPIs**
- Add backend computation and frontend display of: credits/hour, credits/ship/hour, credits/API-call, idle %, fuel cost per credit earned.

### Phase 4 — universe & system maps

**4a — universe map (`/universe`)**
- 2D scatter of all known systems (ECharts scatter + custom overlay).
- Colour by visited/unvisited/known-only; jump-gate connection lines; ships layer updated via SignalR; search; exploration frontier highlight.

**4b — system map (`/systems/:symbol`)**
- In-system 2D layout: waypoints by coordinate, orbital clustering, waypoint-type icons, trait badges.
- Ship position dots updated via SignalR; jump-gate connection edges; automation annotation rings; side-panel on click.

### Phase 5 — strategy aids

- Trade-route heatmap (origin × destination matrix, coloured by realised margin/hour).
- Decision attribution: top-N rejected alternatives shown per automation decision.
- Contract ROI panel.
- Market freshness map and rate-limit pressure overlay on time-series charts.
- Anomaly badges (server-computed, pushed via SignalR): ship credits/hour drop, good price spike.
- Annotation markers on charts (from operator-authored `AgentSetting` notes).

### Phase 6 — retire `SpaceTraders.App`

- Once the new UI covers all views still actively used, remove `SpaceTraders.App` from k8s.
- Remaining deployment: `SpaceTraders.API` + `SpaceTraders.WebUI` + PostgreSQL.

## 12. Decisions

| # | Question | Decision |
|---|---|---|
| 1 | Frontend served by the existing API host or its own pod? | **Own pod** (`SpaceTraders.WebUI` nginx container). Uses the existing `SpaceTraders.API` — no second API. CORS configured for the WebUI origin. |
| 2 | Retention policy for `MarketPriceSample` and `LedgerEntry`? | Raw samples pruned after 7 days (market) / 30 days (ledger). Hourly aggregates kept 90 days. **`RunCreditHighlight`** kept indefinitely — compact per-run credit milestones answer "how did this run go?" for any historical run without touching pruned raw data. |
| 3 | Run boundaries automatic or operator-driven? | **Hybrid**: runs auto-close on strategy-relevant settings changes. Operators can additionally queue a `ScheduledRun` (with optional `activatesAt` or `activatesOnNextRestart`) that promotes to an active run at the right time — letting strategy changes be prepared for a future run without touching the current one. |
| 4 | SignalR vs SSE? | **SignalR.** Already idiomatic in .NET 10; SSE remains a valid simpler fallback if SignalR proves overkill. |
| 5 | Multi-agent frontend? | **Single active agent shown by default** (the one with the currently configured API key). Any other agent token, regardless of age, is treated as a "historic agent" accessible via a separate historical view. No agent selector in the top bar for the live view. |

