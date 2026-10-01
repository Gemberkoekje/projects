# SpaceTraders

![.NET](https://img.shields.io/badge/.NET-10.0-512BD4?logo=dotnet)

An automation bot and dashboard for [SpaceTraders](https://spacetraders.io/), a headless space
trading game played entirely through an HTTP API. The bot registers an agent, keeps a PostgreSQL
cache of the game state, and runs its ships automatically. A React dashboard shows what it is doing.

> **Status (2026-10-01):** not running on the cluster. It was taken off in May 2026 after it filled
> the shared PostgreSQL database. `PLAN.md` describes the way back: phase 1 (safe to run) is done,
> including a four-hour soak test. Known issues are listed there under B-numbers and decisions
> under D-numbers.

---

## Documentation

| File | What it's for |
|---|---|
| `PLAN.md` | What happens next: phases and slices, known issues, decisions |
| `docs/HOW_IT_WORKS.md` | What the code does today: startup, the tick, plans, ships, events, tables, endpoints |
| `docs/GLOSSARY.md` | Project terms |
| `spacetraders.md` | Solution overview: stack, configuration, conventions |
| `CONTRIBUTING.md` | Conventions and the PR checklist |
| `CHANGELOG.md` | Notable changes |
| `CLAUDE.md` | What Claude works on in this project |
| `docs/archive/` | Earlier plans and designs. They don't describe the current code. |

---

## Solution Structure

```text
SpaceTraders.slnx
├── SpaceTraders.Domain                        aggregates, goals, events, enums
├── SpaceTraders.Application                   plans, goal executors, commands, event handlers
├── SpaceTraders.Infrastructure.SpaceTradersAPI typed API client, rate limiting, retries
├── SpaceTraders.Infrastructure.Persistence    EF Core + PostgreSQL, schema initializer, scheduler
├── SpaceTraders.API                           host: automation services + internal HTTP API
├── SpaceTraders.WebUI                         React/Vite dashboard
├── SpaceTraders.Analyzers                     Roslyn analyzer ST0001: state-transition events only from command handlers
├── docs/                                      documentation
└── tests/
    ├── SpaceTraders.Domain.Tests
    ├── SpaceTraders.Application.Tests
    ├── SpaceTraders.Infrastructure.Tests
    ├── SpaceTraders.API.Tests                 WebApplicationFactory integration tests
    └── SpaceTraders.Integration.Test
```

---

## Prerequisites

- .NET 10 SDK
- PostgreSQL (local Docker is fine)
- Node.js 22, only for the WebUI

---

## Local Run

> The SpaceTraders rate limit is per IP address **and per account**. Don't run a local instance
> against the same account while another instance (for example the cluster one) is running.

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
# Optional: protect the internal API (the local WebUI then needs the key in public/config.js)
dotnet user-secrets set "SPACETRADERS_INTERNAL_API_KEY" "<random-secret>"
```

### 3) Run the API host

```powershell
dotnet run --project SpaceTraders.API
```

- It listens on `https://localhost:49305` and `http://localhost:49306` (launch profile), under the
  path base `/spacetraders/api`. Swagger UI is available in Development.
- On startup it creates and extends the database schema (`SpaceTradersDatabaseInitializer`).
  If no valid agent token is stored, it registers a new agent with the account token, then
  starts the automation services.
- `docs/HOW_IT_WORKS.md` lists what runs and every internal endpoint.

### 4) Run the WebUI (optional)

```powershell
cd SpaceTraders.WebUI
npm ci
npm run dev
```

Vite serves the dashboard at `/spacetraders/dashboard/` and proxies `/spacetraders/api` to
`https://localhost:49305`.

---

## Tests

```powershell
dotnet test SpaceTraders.slnx --filter "Category!=Integration"
```

Integration tests are tagged `Category=Integration` and start PostgreSQL in Docker through
Testcontainers. They find Docker the way Testcontainers does (`DOCKER_HOST`, the Unix socket, or
Docker Desktop on Windows) and skip when no Docker is running. WebUI tests run
with `npm test` in `SpaceTraders.WebUI`.

---

## Deployment

- CI (`.github/workflows/ci-spacetraders.yml` in the parent repository) builds and tests every
  change. On `main` it pushes `ghcr.io/gemberkoekje/spacetraders-api` and
  `ghcr.io/gemberkoekje/spacetraders-webui`, tagged `latest` and with the commit SHA.
- To build the images by hand, run this from the parent directory that contains `SpaceTraders/`:

  ```powershell
  docker build -f SpaceTraders/Dockerfile.api -t spacetraders-api:latest .
  docker build -f SpaceTraders/Dockerfile.webui -t spacetraders-webui:latest .
  ```

- The Kubernetes manifests live in the cluster's GitOps repository (gembernodes), deployed by
  Flux. They were removed there while the bot is off; bringing them back is phase 4 of `PLAN.md`.
