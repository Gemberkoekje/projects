# SpaceTraders

![.NET](https://img.shields.io/badge/.NET-10.0-512BD4?logo=dotnet)

This repository contains a .NET automation and dashboard system for the [SpaceTraders API](https://spacetraders.io/):

- Typed SpaceTraders API client (`SpaceTraders.Infrastructure.SpaceTradersAPI`)
- PostgreSQL persistence for cached agent/ship/contract/token data (`SpaceTraders.Infrastructure.Persistence`)
- API host with bootstrap + startup sync + game-loop + ship-worker automation (`SpaceTraders.API`)
- Razor Pages dashboard for operational views (`SpaceTraders.App`)
- React WebUI dashboard for live operational monitoring (`SpaceTraders.WebUI`)

SpaceTraders is a headless, open-universe space game exposed through HTTP endpoints. Players write their own clients to control agents, ships, contracts, mining, trading, navigation, and exploration. This project uses that API as the backend for automated fleet operations and a local Razor Pages dashboard.

---

## Implemented Features

- Agent bootstrap flow
- Startup sync flow (agent, ships, contracts from SpaceTraders API)
- Startup recovery service (resumes in-flight ship automation after pod restart)
- Typed API client for: status, factions, agents, systems, waypoints, fleet, contracts
- Dead-reckoning game loop (detects ship arrivals, low fuel, API availability changes)
- Ship automation services and event handlers
- Contract watch service and fleet expansion decisions
- Event handlers: `ContractPriorityHandler`, `ShipFuelLowHandler`, `ApiUnavailabilityHandler`
- Internal REST API (`/status/*`, `/settings/*`, `/control/*`, `/health/*`)
- API key authentication middleware (`X-Api-Key` header)
- Kubernetes manifests + Dockerfiles (`k8s/`, `Dockerfile.api`, `Dockerfile.app`, `Dockerfile.webui`)
- Razor Pages dashboard
- React WebUI dashboard served at `/spacetraders/dashboard`

---

## Solution Structure

```text
SpaceTraders.slnx
├── SpaceTraders.Domain
├── SpaceTraders.Application
├── SpaceTraders.Infrastructure.SpaceTradersAPI
├── SpaceTraders.Infrastructure.Persistence
├── SpaceTraders.API
├── SpaceTraders.App
└── tests/
    ├── SpaceTraders.Domain.Tests
    ├── SpaceTraders.Application.Tests
    ├── SpaceTraders.Infrastructure.Tests
    └── SpaceTraders.API.Tests          ← WebApplicationFactory integration tests
```

---

## Prerequisites

- .NET 10 SDK
- PostgreSQL (local Docker is fine)

---

## Local Run

### 1) Start PostgreSQL

```powershell
docker run -d --name spacetraders-pg -e POSTGRES_DB=spacetraders -e POSTGRES_USER=postgres -e POSTGRES_PASSWORD=changeme -p 5432:5432 postgres:16
```

### 2) Configure secrets

```powershell
cd SpaceTraders.API
dotnet user-secrets set "ConnectionStrings:DefaultConnection" "Host=localhost;Database=spacetraders;Username=postgres;Password=changeme"
dotnet user-secrets set "SpaceTraders:AccountToken" "<your-account-token>"
dotnet user-secrets set "SpaceTraders:AgentName" "<desired-callsign>"
dotnet user-secrets set "SpaceTraders:AgentFaction" "COSMIC"
# Optional: protect the internal REST API
dotnet user-secrets set "SPACETRADERS_INTERNAL_API_KEY" "<random-secret>"

cd ../SpaceTraders.App
dotnet user-secrets set "ConnectionStrings:DefaultConnection" "Host=localhost;Database=spacetraders;Username=postgres;Password=changeme"
```

### 3) Run

```powershell
cd SpaceTraders.API
dotnet run

cd ../SpaceTraders.App
dotnet run
```

The API host creates the DB schema with `EnsureCreated()` on startup. The dashboard (App) uses the same database.

### 4) Internal REST API

The API host applies the path base `/spacetraders/api`. The endpoints below are relative to that path base.

| Endpoint | Auth | Description |
|----------|------|-------------|
| `GET /health/live` | None | Liveness probe |
| `GET /health/ready` | None | Readiness probe (DB check) |
| `GET /health/startup` | None | Startup probe |
| `GET /status/agent` | `X-Api-Key` | Current agent credits + ship count |
| `GET /status/ships` | `X-Api-Key` | All ships with assignment + nav |
| `GET /status/contracts` | `X-Api-Key` | Active cached contracts |
| `GET /status/rate-limit` | `X-Api-Key` | Current rate limit status |
| `GET /status/activity` | `X-Api-Key` | Paged activity log |
| `GET /status/trade-opportunities` | `X-Api-Key` | Best cached trade opportunity |
| `GET /settings/` | `X-Api-Key` | All operator settings |
| `PUT /settings/{key}` | `X-Api-Key` | Update a setting |
| `POST /settings/reset` | `X-Api-Key` | Reset settings to defaults |
| `POST /control/automation/enable` | `X-Api-Key` | Resume automation |
| `POST /control/automation/disable` | `X-Api-Key` | Pause automation |
| `POST /control/ships/{symbol}/reassign` | `X-Api-Key` | Reassign a ship |
| `POST /control/sync` | `X-Api-Key` | Force full sync |

---

## Kubernetes Deployment

All manifests live in `k8s/`. Apply in this order:

```powershell
kubectl apply -f k8s/namespace.yaml
# Copy k8s/secret.yaml.template → k8s/secret.yaml and fill in values, then:
kubectl apply -f k8s/secret.yaml          # NEVER commit this file
kubectl apply -f k8s/configmap.yaml
kubectl apply -f k8s/postgres.yaml        # or use a managed service
kubectl apply -f k8s/deployment-api.yaml
kubectl apply -f k8s/deployment-app.yaml
kubectl apply -f k8s/service-api.yaml
kubectl apply -f k8s/service-app.yaml
kubectl apply -f k8s/ingress.yaml         # optional, requires nginx ingress controller
```

Build and push container images:

```powershell
# From C:\git\projects, the parent directory that contains SpaceTraders:
docker build -f SpaceTraders/Dockerfile.api -t spacetraders-api:latest .
docker build -f SpaceTraders/Dockerfile.app -t spacetraders-app:latest .
docker build -f SpaceTraders/Dockerfile.webui -t spacetraders-webui:latest .
```

---

## Tests

```powershell
cd SpaceTraders
dotnet test SpaceTraders.slnx --filter "Category!=Integration"
```

Integration tests (require Docker / PostgreSQL) are tagged `Category=Integration` and skipped by the filter above.

---

## Documentation

- `docs/implementation/CURRENT_IMPLEMENTATION_OVERVIEW.md` = how the solution works today (implemented behavior)
- `docs/implementation/RACE_CONDITION_PREVENTION_IMPLEMENTATION.md` = implemented consistency and recovery safeguards
- `docs/operations/LOCAL_DEVELOPMENT.md` = local development, internal endpoints, and deployment notes
- `docs/SPACE_TRADERS_IMPLEMENTATION_PLAN.md` = implementation plan based on `spacetraders.md` and current code
- `docs/GLOSSARY.md` = project terminology
- `spacetraders.md` = cleaned SpaceTraders.io reference content and gameplay/API explanation
- `CHANGELOG.md` = notable changes
- `CONTRIBUTING.md` = contribution conventions
