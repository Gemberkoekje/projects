# SpaceTraders (Current Foundation)

![.NET](https://img.shields.io/badge/.NET-10.0-512BD4?logo=dotnet)

This repository currently contains a **foundation implementation** for a SpaceTraders-based system:

- Typed SpaceTraders API client (`SpaceTraders.Infrastructure.SpaceTradersAPI`)
- PostgreSQL persistence for cached agent/ship/contract/token data (`SpaceTraders.Infrastructure.Persistence`)
- API host with bootstrap + startup sync + game-loop + ship-worker automation (`SpaceTraders.API`)
- Razor Pages dashboard showing cached agent + ships (`SpaceTraders.App`)

> The detailed design docs in `docs/plan/` describe the **target architecture**. Large parts are planned, not yet implemented.

---

## Implemented Features

- Agent bootstrap flow
- Startup sync flow (agent, ships, contracts from SpaceTraders API)
- Startup recovery service (resumes in-flight ship automation after pod restart)
- Typed API client for: status, factions, agents, systems, waypoints, fleet, contracts
- Dead-reckoning game loop (detects ship arrivals, low fuel, API availability changes)
- Ship automation services and event handlers
- Contract watch service, Scout service, Fleet expansion
- Event handlers: `ContractPriorityHandler`, `ShipFuelLowHandler`, `ApiUnavailabilityHandler`
- Internal REST API (`/status/*`, `/settings/*`, `/control/*`, `/health/*`)
- API key authentication middleware (`X-Api-Key` header)
- Kubernetes manifests + Dockerfiles (`k8s/`, `Dockerfile.api`, `Dockerfile.app`)
- Razor Pages dashboard

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

| Endpoint | Auth | Description |
|----------|------|-------------|
| `GET /health/live` | None | Liveness probe |
| `GET /health/ready` | None | Readiness probe (DB check) |
| `GET /status/agent` | `X-Api-Key` | Current agent credits + ship count |
| `GET /status/ships` | `X-Api-Key` | All ships with assignment + nav |
| `GET /settings/` | `X-Api-Key` | All operator settings |
| `PUT /settings/{key}` | `X-Api-Key` | Update a setting |
| `POST /control/automation/enable` | `X-Api-Key` | Resume automation |
| `POST /control/automation/disable` | `X-Api-Key` | Pause automation |
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
# From the repository root:
docker build -f SpaceTraders/Dockerfile.api -t spacetraders-api:latest .
docker build -f SpaceTraders/Dockerfile.app -t spacetraders-app:latest .
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

- `docs/plan/00-overview.md` = planning index
- `docs/plan/ship-event-command-plan.md` = current target ship automation plan
- `docs/plan/` = broader target/roadmap architecture documents
- `plan.md` = archived integration plan for initial API client work
- `CHANGELOG.md` = notable changes
- `CONTRIBUTING.md` = contribution conventions
