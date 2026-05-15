# Current Implementation Overview

This document describes how the solution works today.

## Implemented vs reference matrix

| Area | Implemented today | Reference gap status |
|------|-------------------|----------------------|
| API host and automation runtime | Implemented | Stable baseline |
| React/Vite operational dashboard | Implemented | Needs expanded pages for deeper operations |
| Core SpaceTraders typed client (status/agent/ships/contracts/navigate/dock/orbit/extract/trade/refuel/purchase) | Implemented | Missing additional gameplay endpoints |
| Cache persistence for agent/ships/contracts/systems/waypoints/markets/shipyards/settings/activity/assignments/trade opportunities | Implemented | Needs richer gameplay model fields |
| Chain-of-command ship automation with state-gated command acceptors | Implemented | Needs full lifecycle strategies |
| Divergence health check | Not implemented | Planned in Phase 8 |

## Solution at a glance

- **SpaceTraders.API**: automation host + internal control/status API.
- **SpaceTraders.WebUI**: React/Vite dashboard reading the internal API.
- **SpaceTraders.Application**: commands, queries, automation/background services, event handlers.
- **SpaceTraders.Infrastructure.Persistence**: EF Core + PostgreSQL cache, settings, activity logs, leases.
- **SpaceTraders.Infrastructure.SpaceTradersAPI**: typed SpaceTraders HTTP client, rate limiting/retry, adapter.
- **SpaceTraders.Domain**: domain events, aggregates, enums, value objects.

## Runtime flow (current)

### 1) Startup (API host)

`SpaceTraders.API/Program.cs` configures:

1. Serilog logging.
2. Application layer + Wolverine message bus.
3. Persistence (PostgreSQL `SpaceTradersDbContext`).
4. SpaceTraders API client infrastructure.
5. Health checks and Prometheus metrics.
6. Hosted services.

After DI setup, the API host:

- Applies path base: `/spacetraders/api`.
- Initializes the database via `SpaceTradersDatabaseInitializer.InitializeAsync(...)` (except in `Testing` environment).
- Enables API key middleware for internal endpoints.
- Maps health, metrics, status, settings, and control endpoints.
- Maps SignalR dashboard notifications at `/hubs/dashboard`.

Development builds also expose Swagger UI. Scalar is not currently configured in the API host.

### 2) Agent bootstrap and token selection

`AgentBootstrapService` resolves an agent token in this order:

1. Token for configured `SpaceTraders:AgentName`.
2. Active token saved in DB.
3. Configured `SpaceTraders:AgentToken`.
4. Latest stored token.
5. If none valid, registers a new agent using `SpaceTraders:AccountToken`.

It validates tokens by calling `GetMyAgentAsync`. Invalid reset-date tokens trigger re-registration.

### 3) Startup sync

`StartupSyncService` performs an initial sync from SpaceTraders API into PostgreSQL:

- Agent details.
- All ships (paged retrieval).
- Systems/waypoints needed by owned ships.
- Market and shipyard cache at occupied waypoints.
- Contracts.

### 4) Startup recovery

`StartupRecoveryService` runs after startup sync and resumes ship automation state by publishing events based on each ship's current nav status:

- `ShipInTransitEvent` for in-transit/arrival cases.
- `ShipDockedEvent` for docked ships.
- `ShipInOrbitEvent` for in-orbit ships.

This allows the chain-of-command handlers to continue from current state after restarts.

## Background automation services

Implemented recurring services include:

- **GameLoopService** (every 5s)
  - Leader-gated execution.
  - Dead-reckoning for ship arrivals.
  - Low-fuel detection for docked ships.
  - API availability transition events.
  - Probe deployment, contract, mining, and trade automation ticks.

- **ActivityLogPruningService**
  - Prunes old activity log data.

- **DataRetentionService**
  - Prunes retained operational data according to configured retention policy.

- **PrometheusMetricsService**
  - Records process/service metrics.

- **LeaderElectionService**
  - Ensures only one active instance performs leader-only automation work.

- **ShipEventScheduler**
  - Schedules ship arrival and cooldown events.

## Event and command processing

The application uses Wolverine for command/event dispatch.

- Command handlers cover ship operations, contract actions, sync jobs, fleet purchase, and probes.
- Automation services cover contract mineral fulfillment, mining opportunities, probe deployment, scouting, and trading.
- Domain and integration events are bridged into chain-of-command handlers.
- Ship handling uses state-scoped command acceptors:
  - `IDockedCommandAcceptor`
  - `IInOrbitCommandAcceptor`
  - `IInTransitCommandAcceptor`

This enforces issuing only commands valid for a ship's current physical state.

## Internal API surface

The internal API is exposed under `/spacetraders/api` and includes:

- `/health/live`, `/health/ready`, and `/health/startup`.
- `/health/automation` and `/health/rate-limit/history` for runtime health and API usage.
- `/status/*` for agent, ships, ship diagnostics, contracts, rate limits, activity, trade opportunities, startup snapshots, system alerts, anomalies, top trade routes, and mining opportunities.
- `/fleet/*` for goal chains, assignments, current ship activity, and ship goal history.
- `/markets/*` for market snapshots, price history, best routes, and freshness.
- `/shipyards/*` for shipyard snapshots and freshness.
- `/settings/*` to read/update/reset runtime settings.
- `/control/*` for automation enable/disable.
- `/metrics` for Prometheus metrics.
- `/hubs/dashboard` for SignalR dashboard invalidation events.

Most internal endpoints are protected by API key middleware (`X-Api-Key`) when `SPACETRADERS_INTERNAL_API_KEY` is configured. Health endpoints are always unauthenticated.

## React WebUI (current)

`SpaceTraders.WebUI` provides read-heavy operational views through the internal API:

- Overview: agent credits, fleet counts, rate-limit status, and system health alerts.
- Fleet: ships, detail views, diagnostics, assignments, activity, and goal history.
- Markets and shipyards: cached snapshots, freshness, prices, and trade routes.
- Plans: goal chains, ships by plan, queued mining opportunities, and current activity.
- Settings: runtime setting read/update/reset controls.
- Startup snapshots, logs, and API usage views.

The app uses `X-Api-Key` through its API fetch wrapper and listens to the dashboard SignalR hub for invalidation events.

## Data model role (current)

PostgreSQL is used as a local operational cache and state store for:

- Agent, ships, contracts.
- Systems, waypoints, market, shipyard snapshots.
- Ship assignments and trade opportunities.
- Fleet goals, ship goals, ship goal history, and persisted plan states.
- Settings, activity logs, API endpoint usage.
- Leader lease and token/agent scoping data.

## Notes on scope

This overview intentionally documents implemented behavior now. SpaceTraders.io gameplay and API reference notes are kept in `spacetraders.md`.
