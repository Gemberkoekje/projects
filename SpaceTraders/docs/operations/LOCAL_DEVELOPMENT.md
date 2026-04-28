# Local Development and Operations

This document describes how to run and operate the current implementation locally.

## Prerequisites

- .NET 10 SDK
- Docker or another PostgreSQL 16-compatible instance
- A SpaceTraders account token from `https://my.spacetraders.io`

## Services

The solution is split into two runnable hosts:

- `SpaceTraders.API` runs the automation workers and internal Minimal API endpoints under `/spacetraders/api`.
- `SpaceTraders.App` runs the Razor Pages dashboard under `/spacetraders`.

Both hosts use the same PostgreSQL database. The API host writes cache and automation state; the Razor Pages app reads and updates operational data such as settings.

## Start PostgreSQL

```powershell
docker run -d --name spacetraders-pg -e POSTGRES_DB=spacetraders -e POSTGRES_USER=postgres -e POSTGRES_PASSWORD=changeme -p 5432:5432 postgres:16
```

## Configure local secrets

```powershell
cd SpaceTraders.API
dotnet user-secrets set "ConnectionStrings:DefaultConnection" "Host=localhost;Database=spacetraders;Username=postgres;Password=changeme"
dotnet user-secrets set "SpaceTraders:AccountToken" "<account-token>"
dotnet user-secrets set "SpaceTraders:AgentName" "<agent-callsign>"
dotnet user-secrets set "SpaceTraders:AgentFaction" "COSMIC"
dotnet user-secrets set "SPACETRADERS_INTERNAL_API_KEY" "<optional-internal-api-key>"

cd ..\SpaceTraders.App
dotnet user-secrets set "ConnectionStrings:DefaultConnection" "Host=localhost;Database=spacetraders;Username=postgres;Password=changeme"
```

`SPACETRADERS_INTERNAL_API_KEY` is optional for local development. If it is set, internal API requests must include an `X-Api-Key` header. Health endpoints are always unauthenticated.

## Run the hosts

Run the API host:

```powershell
cd SpaceTraders.API
dotnet run
```

Run the Razor Pages dashboard in a second terminal:

```powershell
cd SpaceTraders.App
dotnet run
```

The API host initializes the database schema on startup except in the `Testing` environment. The Razor Pages host also initializes the schema before serving pages.

## Internal API endpoints

The API host applies the path base `/spacetraders/api`. The routes below are relative to that path base.

| Endpoint | Auth | Description |
|----------|------|-------------|
| `GET /health/live` | None | Liveness probe. |
| `GET /health/ready` | None | Readiness probe with database health check. |
| `GET /health/startup` | None | Startup health probe. |
| `GET /metrics` | `X-Api-Key` when configured | Prometheus metrics. |
| `GET /status/agent` | `X-Api-Key` when configured | Cached agent state. |
| `GET /status/ships` | `X-Api-Key` when configured | Cached ships and current assignments. |
| `GET /status/contracts` | `X-Api-Key` when configured | Cached active contracts. |
| `GET /status/rate-limit` | `X-Api-Key` when configured | Current rate limiter status. |
| `GET /status/activity` | `X-Api-Key` when configured | Paged activity log. Supports `page`, `size`, and `ship`. |
| `GET /status/trade-opportunities` | `X-Api-Key` when configured | Best cached trade opportunity. |
| `GET /settings/` | `X-Api-Key` when configured | Runtime settings. |
| `PUT /settings/{key}` | `X-Api-Key` when configured | Update a runtime setting. |
| `POST /settings/reset` | `X-Api-Key` when configured | Reset settings to defaults. |
| `POST /control/automation/enable` | `X-Api-Key` when configured | Resume automation. |
| `POST /control/automation/disable` | `X-Api-Key` when configured | Pause automation. |
| `POST /control/ships/{symbol}/reassign` | `X-Api-Key` when configured | Assign a ship role. |
| `POST /control/sync` | `X-Api-Key` when configured | Queue full agent, ship, and contract sync. |

## Razor Pages dashboard

The app currently exposes these operational pages:

| Page | Description |
|------|-------------|
| `/` | Agent and fleet summary. |
| `/Ships/Detail` | Ship detail and assignment snapshot. |
| `/Contracts` | Cached contracts. |
| `/Market` | Cached market/trade opportunities. |
| `/ApiUsage` | SpaceTraders endpoint usage counters. |
| `/Log` | Activity log. |
| `/Settings` | Runtime settings and active/selected agent token controls. |

## Build and test

```powershell
dotnet build SpaceTraders.slnx
dotnet test SpaceTraders.slnx --filter "Category!=Integration"
```

Integration tests require Docker/PostgreSQL and are tagged with `Category=Integration`.

## Container images

Build from the parent repository root because the Dockerfiles publish `SpaceTraders/SpaceTraders.API` and `SpaceTraders/SpaceTraders.App` paths:

```powershell
cd C:\git\projects
docker build -f SpaceTraders/Dockerfile.api -t spacetraders-api:latest .
docker build -f SpaceTraders/Dockerfile.app -t spacetraders-app:latest .
```

## Kubernetes notes

Manifests live in `k8s/`. Copy `k8s/secret.yaml.template` to `k8s/secret.yaml`, fill in real values, and never commit the generated secret file.

Apply the base manifests in this order:

```powershell
kubectl apply -f k8s/namespace.yaml
kubectl apply -f k8s/secret.yaml
kubectl apply -f k8s/configmap.yaml
kubectl apply -f k8s/postgres.yaml
kubectl apply -f k8s/deployment-api.yaml
kubectl apply -f k8s/deployment-app.yaml
kubectl apply -f k8s/service-api.yaml
kubectl apply -f k8s/service-app.yaml
kubectl apply -f k8s/ingress.yaml
kubectl apply -f k8s/hpa.yaml
```

The API and app hosts both configure path bases in code. Keep Kubernetes ingress paths aligned with those path bases when changing ingress routing.
