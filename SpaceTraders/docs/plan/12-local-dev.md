# 12 – Local Development Guide

## Prerequisites

| Tool | Minimum version | Install |
|------|----------------|---------|
| .NET SDK | 10.0 | [dot.net](https://dot.net) |
| Docker Desktop | any recent | [docker.com](https://www.docker.com/products/docker-desktop/) |
| `dotnet-ef` global tool | 10.0 | `dotnet tool install -g dotnet-ef` |
| Visual Studio 2022 / Rider / VS Code | – | optional |

---

## 1 – Clone the Repository

```powershell
git clone https://github.com/Gemberkoekje/projects
cd SpaceTraders
dotnet restore
```

---

## 2 – Start a Local PostgreSQL Instance

The project uses PostgreSQL in development.

```powershell
docker run -d `
  --name spacetraders-pg `
  -e POSTGRES_DB=spacetraders `
  -e POSTGRES_USER=postgres `
  -e POSTGRES_PASSWORD=changeme `
  -p 5432:5432 `
  postgres:16
```

To stop and remove later:
```powershell
docker rm -f spacetraders-pg
```

---

## 3 – Obtain a SpaceTraders Account Token

1. Go to [my.spacetraders.io](https://my.spacetraders.io/) and create an account.
2. Copy your **account token**.

---

## 4 – Configure User Secrets

```powershell
cd SpaceTraders.API

dotnet user-secrets set "ConnectionStrings:DefaultConnection" `
  "Host=localhost;Database=spacetraders;Username=postgres;Password=changeme"

dotnet user-secrets set "SpaceTraders:AccountToken"  "<your-account-token>"
dotnet user-secrets set "SpaceTraders:AgentName"     "<desired-callsign>"
dotnet user-secrets set "SpaceTraders:AgentFaction"  "COSMIC"

cd ../SpaceTraders.App
dotnet user-secrets set "ConnectionStrings:DefaultConnection" `
  "Host=localhost;Database=spacetraders;Username=postgres;Password=changeme"
```

To confirm:
```powershell
dotnet user-secrets list
```

---

## 5 – Database Initialization

Current implementation creates schema at startup using `EnsureCreated()` in both API and App hosts.

If you add EF migrations later, you can switch to `dotnet ef database update` workflows.

---

## 6 – Run the Solution

### Option A – Visual Studio / Rider

1. Open `SpaceTraders.sln`.
2. Set multiple startup projects: `SpaceTraders.API` and `SpaceTraders.App`.
3. Run.

### Option B – Terminal

```powershell
cd SpaceTraders.API
dotnet run

cd ../SpaceTraders.App
dotnet run
```

### Option C – Hot Reload

```powershell
cd SpaceTraders.API
dotnet watch run

cd ../SpaceTraders.App
dotnet watch run
```

---

## 7 – First-Run Bootstrap

On first run without a stored agent token:

1. `AgentBootstrapService` reads `SpaceTraders:AccountToken`.
2. Calls `POST /register` using `AgentName` + `AgentFaction`.
3. Persists returned agent token to `stored_credentials`.
4. Persists returned agent/ship/contract cache data.
5. `StartupSyncService` refreshes cached agent/ships/contracts.

Subsequent runs load token from DB and skip registration.

---

## 8 – Verifying Connectivity

Current implementation does not yet expose the planned `/status/*` and `/health/*` endpoint set.
Verification options:

- Check API logs for successful bootstrap/sync messages.
- Open the Razor Pages dashboard and confirm cached agent/ship data appears.
- Query tables in PostgreSQL (`cached_agents`, `cached_ships`, `cached_contracts`, `stored_credentials`).

---

## 9 – Running Tests

Test projects are planned but not yet present in the current solution.

---

## 10 – Offline / Mock Development

A fake `ISpaceTradersApiClient` can be added later for offline development. This is not yet implemented.

---

## 11 – Debugging Tips

| Scenario | Tip |
|----------|-----|
| App starts but no dashboard data | Ensure API ran first and completed bootstrap/sync |
| `InvalidOperationException` about account token | Set `SpaceTraders:AccountToken` in API user-secrets |
| DB connection failures | Verify PostgreSQL container is running and connection string matches |
| Agent registration fails | Verify account token and desired agent name/faction |

---

## 12 – Useful dotnet-ef Commands

These are optional until migrations are introduced:

```powershell
dotnet ef migrations list --startup-project ../SpaceTraders.API
dotnet ef migrations script --startup-project ../SpaceTraders.API
dotnet ef migrations remove --startup-project ../SpaceTraders.API
```

---

## 13 – Troubleshooting

| Symptom | Likely cause | Fix |
|---------|-------------|-----|
| `Connection refused` on startup | PostgreSQL container not running | `docker start spacetraders-pg` |
| Agent bootstrap fails | Missing/invalid account token | Set valid `SpaceTraders:AccountToken` in API secrets |
| Dashboard shows no data | API sync did not complete yet | Start API first and check logs |
| `dotnet ef` not found | Tool not installed | `dotnet tool install -g dotnet-ef` |

---

## 14 – Related Documents

- [`README.md`](../../README.md)
- [`03-persistence.md`](03-persistence.md)
- [`08-kubernetes.md`](08-kubernetes.md)
- [`11-testing.md`](11-testing.md)
