# SpaceTraders Solution Documentation

> Solution overview: stack, structure, configuration and conventions. Start with `README.md`. For
> what the code does at runtime see `docs/HOW_IT_WORKS.md`; for what happens next see `PLAN.md`.

## Overview

This repository contains a .NET 10 solution for automating and operating a SpaceTraders agent.

The solution is organized in a layered architecture:

- **SpaceTraders.API**: ASP.NET Core API host, health/metrics/endpoints, and startup orchestration.
- **SpaceTraders.Application**: use-cases, command handlers, orchestration, automation, and domain event handlers.
- **SpaceTraders.Domain**: aggregates, domain events, enums, and value objects.
- **SpaceTraders.Infrastructure.Persistence**: Entity Framework Core persistence, repositories, scheduler, and data bootstrapping.
- **SpaceTraders.Infrastructure.SpaceTradersAPI**: outbound SpaceTraders API client, adapters, rate limiting, and availability logic.
- **SpaceTraders.WebUI**: React/Vite dashboard that reads the internal API.
- **tests/**: unit/integration test projects.

## Tech Stack

- **.NET 10**
- **C# 14**
- **ASP.NET Core Minimal APIs + SignalR**
- **Wolverine** (messaging/workflows)
- **Entity Framework Core + PostgreSQL**
- **Serilog**
- **Prometheus metrics**

## Repository Structure

```text
SpaceTraders.API/
SpaceTraders.Application/
SpaceTraders.Domain/
SpaceTraders.Infrastructure.Persistence/
SpaceTraders.Infrastructure.SpaceTradersAPI/
SpaceTraders.Analyzers/
SpaceTraders.WebUI/
tests/
docs/            HOW_IT_WORKS.md, GLOSSARY.md, archive/
PLAN.md
spacetraders.md
```

## Prerequisites

- .NET 10 SDK
- PostgreSQL (local or remote)
- Optional: Node.js (for `SpaceTraders.WebUI` if used)

## Configuration

Primary runtime configuration is in:

- `SpaceTraders.API/appsettings.json`
- `SpaceTraders.API/appsettings.Development.json` (if present)
- User secrets (Development)
- Environment variables

Important settings:

- `ConnectionStrings:DefaultConnection`
- `SpaceTraders:AccountToken`
- `SpaceTraders:AgentName`
- `SpaceTraders:AgentFaction`
- `SpaceTradersApi:BaseUrl`
- `WebUI:Origin`

## Running the API

From repository root:

```powershell
dotnet run --project SpaceTraders.API/SpaceTraders.API.csproj
```

Notes:

- API path base is `/spacetraders/api`.
- Swagger UI is enabled in Development.
- Health endpoints:
  - `/spacetraders/api/health/live`
  - `/spacetraders/api/health/ready`
  - `/spacetraders/api/health/startup`
- Prometheus metrics are mapped at `/metrics`; like every non-health endpoint, they require the `X-Api-Key` header when `SPACETRADERS_INTERNAL_API_KEY` is set (B11 in `PLAN.md`).

## Running Tests

Run all tests except the integration tests (which need Docker/PostgreSQL):

```powershell
dotnet test SpaceTraders.slnx --filter "Category!=Integration"
```

Run a specific test project:

```powershell
dotnet test tests/SpaceTraders.Application.Tests/SpaceTraders.Application.Tests.csproj
```

## Coding and Build Conventions

- Language version is centralized in `Directory.Build.props` (`LangVersion=14`).
- Analyzer packages are enabled solution-wide.
- NuGet audit is enabled.
- Implicit usings are intentionally disabled; required usings must be explicit (typically in `GlobalUsings.cs` per project).

## Deployment

- `Dockerfile.api` and `Dockerfile.webui` build the two container images. Build them from the
  parent directory that contains `SpaceTraders/`.
- On every push to `main` that touches `SpaceTraders/**`, CI (`.github/workflows/ci-spacetraders.yml`
  in the parent repository) builds, tests, and pushes `ghcr.io/gemberkoekje/spacetraders-api` and
  `ghcr.io/gemberkoekje/spacetraders-webui`, tagged `latest` and with the commit SHA.
- The Kubernetes manifests live in the cluster's GitOps repository (gembernodes), not here. They
  were removed there on 2026-09-26 while the bot is off; bringing them back is phase 4 of `PLAN.md`.

## Troubleshooting

- If startup fails early, verify `ConnectionStrings:DefaultConnection` and PostgreSQL accessibility.
- If game API calls fail, verify SpaceTraders tokens and `SpaceTradersApi:BaseUrl`.
- If dashboard/websocket access fails in browser, verify `WebUI:Origin` CORS configuration.

## Notes for Contributors

- Keep changes aligned with the existing layered architecture.
- Prefer small, test-covered changes in `Application` and `Domain` before infrastructure updates.
- Add or update tests under `tests/` when behavior changes.

