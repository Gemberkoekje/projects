# 06 – Internal REST API

## Goals
- Expose configuration endpoints so humans can tune the automation without redeploying.
- Expose read-only status endpoints for external dashboards, scripts, and alerting systems.
- Secured with an API key (passed via `X-Api-Key` header, stored in a Kubernetes Secret).

---

## 6.1 Project: `SpaceTraders.API`

ASP.NET Core Minimal API (already exists). Uses Wolverine's `IMessageBus` to dispatch queries/commands and keep endpoint handlers thin.

---

## 6.2 Endpoint Groups

### `/status` – Read-only game state

| Method | Path | Description |
|--------|------|-------------|
| GET | `/status/agent` | Current agent credits, ship count |
| GET | `/status/ships` | All ships with current assignment & nav |
| GET | `/status/ships/{symbol}` | Single ship detail |
| GET | `/status/contracts` | Active contracts |
| GET | `/status/rate-limit` | Current rate-limit counters |
| GET | `/status/activity?page=1&size=50&ship=` | Paginated activity log |
| GET | `/status/trade-opportunities` | Top 20 computed trade routes |

### `/settings` – Operator configuration

| Method | Path | Body | Description |
|--------|------|------|-------------|
| GET | `/settings` | – | All settings key/value/description |
| GET | `/settings/{key}` | – | Single setting |
| PUT | `/settings/{key}` | `{ "value": "..." }` | Update a setting |
| POST | `/settings/reset` | – | Reset all settings to defaults |

### `/control` – Operator commands

| Method | Path | Body | Description |
|--------|------|------|-------------|
| POST | `/control/automation/enable` | – | Resume automation |
| POST | `/control/automation/disable` | – | Pause automation (ships finish current step) |
| POST | `/control/ships/{symbol}/reassign` | `{ "assignmentType": "..." }` | Override a ship's assignment |
| POST | `/control/fleet/expand` | – | Trigger fleet expansion check immediately |
| POST | `/control/sync` | – | Force full sync now |

### `/health` – Kubernetes probes (no auth required)

| Method | Path | Description |
|--------|------|-------------|
| GET | `/health/live` | Liveness: always 200 if process is up |
| GET | `/health/ready` | Readiness: 200 if DB is reachable + agent token valid |
| GET | `/health/startup` | Startup: 200 after migrations complete |

---

## 6.3 Authentication

```csharp
// Middleware: ApiKeyMiddleware
// Reads X-Api-Key header, compares to value from environment variable SPACETRADERS_INTERNAL_API_KEY
// Returns 401 on mismatch (except /health/* routes)
```

The API key is mounted from a Kubernetes Secret as an environment variable.

---

## 6.4 OpenAPI / Swagger

- Enabled in Development environment only.
- Scalar UI at `/scalar` (use `Scalar.AspNetCore` package – .NET 10 compatible).

---

## 6.5 Program.cs Outline

```csharp
var builder = WebApplication.CreateBuilder(args);

builder.Services
    .AddApplication()
    .AddSpaceTradersApiInfrastructure(builder.Configuration)
    .AddPersistenceInfrastructure(builder.Configuration)
    .AddHealthChecks()
        .AddDbContextCheck<SpaceTradersDbContext>("postgresql");

builder.Services.AddOpenApi();

var app = builder.Build();

// Run migrations on startup
await app.Services.MigrateAsync();

app.UseMiddleware<ApiKeyMiddleware>();

app.MapStatusEndpoints();
app.MapSettingsEndpoints();
app.MapControlEndpoints();
app.MapHealthChecks("/health/live", new() { Predicate = _ => false });
app.MapHealthChecks("/health/ready");
app.MapHealthChecks("/health/startup", new() { Predicate = r => r.Tags.Contains("startup") });

if (app.Environment.IsDevelopment())
    app.MapOpenApi().MapScalarApiReference();

app.Run();
```

---

## 6.6 Folder Structure

```
SpaceTraders.API/
├── Program.cs
├── Middleware/
│   └── ApiKeyMiddleware.cs
├── Endpoints/
│   ├── StatusEndpoints.cs
│   ├── SettingsEndpoints.cs
│   ├── ControlEndpoints.cs
│   └── HealthEndpoints.cs
└── appsettings.json
```
