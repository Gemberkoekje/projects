# SpaceTraders (Current Foundation)

![.NET](https://img.shields.io/badge/.NET-10.0-512BD4?logo=dotnet)

This repository currently contains a **foundation implementation** for a SpaceTraders-based system:

- Typed SpaceTraders API client (`SpaceTraders.Infrastructure.SpaceTradersAPI`)
- PostgreSQL persistence for cached agent/ship/contract/token data (`SpaceTraders.Infrastructure.Persistence`)
- API host with bootstrap + startup sync background services (`SpaceTraders.API`)
- Razor Pages dashboard showing cached agent + ships (`SpaceTraders.App`)

> The detailed design docs in `docs/plan/` describe the **target architecture**. Large parts are planned, not yet implemented.

---

## Implemented Features

- Agent bootstrap flow:
  - Reads stored agent token from DB, or
  - Registers agent via account token and stores returned agent token
- Startup sync flow:
  - Syncs current agent, ships, and contracts from SpaceTraders API into local DB
- Typed API client methods implemented for:
  - Status, factions, agents, systems, waypoints
  - Register, my-agent, my-ships, my-ship, my-contracts
- Razor Pages dashboard (`/`) displaying:
  - Cached agent summary
  - Cached ship table

---

## Solution Structure

```text
SpaceTraders.sln
├── SpaceTraders.Domain
├── SpaceTraders.Application
├── SpaceTraders.Infrastructure.SpaceTradersAPI
├── SpaceTraders.Infrastructure.Persistence
├── SpaceTraders.API
└── SpaceTraders.App
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

The API and App both create the DB schema with `EnsureCreated()` on startup.

---

## Documentation

- `docs/plan/` = target/roadmap architecture documents
- `plan.md` = archived integration plan for initial API client work
- `CHANGELOG.md` = notable changes
- `CONTRIBUTING.md` = contribution conventions
