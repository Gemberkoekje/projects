# 10 – Error Handling & Resilience

## Goals
- Define what happens when each failure mode occurs so every developer has a consistent reference.
- Avoid silent failures that leave the automation engine in an undefined state.
- Ensure pod restarts are safe and require no manual intervention.

---

## 10.1 Failure Taxonomy

| Layer | Failure Type | Expected Response |
|-------|-------------|-------------------|
| HTTP / SpaceTraders API | `429 Too Many Requests` | Wait until `x-ratelimit-reset`, retry once (handled by `RateLimitResponseHandler`) |
| HTTP / SpaceTraders API | `502 Bad Gateway` | Exponential back-off: 1 s, 2 s, 4 s – max 3 retries (handled by `RetryHandler`) |
| HTTP / SpaceTraders API | `401 Unauthorized` | Log, publish `AgentTokenInvalidEvent`, pause automation until token is re-validated |
| HTTP / SpaceTraders API | `4xx` (other) | Log with full request context, throw `SpaceTradersApiException`, let Wolverine retry policy decide |
| HTTP / SpaceTraders API | Persistent `502` (> 3 retries) | Publish `ApiUnavailableEvent`; automation pauses; dashboard shows alert |
| Agent bootstrap | No account token in config | Throw `InvalidOperationException` at startup with a clear message pointing to user-secrets setup |
| Agent bootstrap | Registration API call fails | Log, retry once after 5 s, then throw – pod will restart via K8s liveness probe |
| Agent bootstrap | Token already in DB | Use existing token; skip registration entirely |
| Wolverine handler | Unhandled exception | Wolverine moves message to dead-letter queue (PostgreSQL-backed); logged with full context |
| Wolverine handler | Repeated failure (> retry limit) | `DeadLetterArrivedHandler` logs structured error, publishes `AutomationErrorEvent` for dashboard alert |
| Database | Connection failure at startup | Startup probe fails → K8s will not route traffic; pod restarts |
| Database | Connection failure at runtime | EF Core throws; handler fails; Wolverine retries with back-off |
| Database | Migration failure | Startup throws, pod does not start – investigate migration scripts |
| Ship event handler | Unexpected ship status | Log warning, emit `ShipNeedsDockingEvent` for non-docked roleless ships or `ShipIdleDockedEvent` for docked ships |
| Contract | Deadline passed before fulfillment | `ContractWatchService` marks contract failed, frees assigned ship (see `05-automation-engine.md §5.5`) |

---

## 10.2 Wolverine Retry & Dead-Letter Policy

Configure in `Program.cs` (or `AddApplication()` extension):

```csharp
builder.Host.UseWolverine(opts =>
{
    // Default retry for any handler that throws
    opts.Policies.OnException<SpaceTradersApiException>()
        .RetryWithCooldown(250.Milliseconds(), 500.Milliseconds(), 1.Seconds());

    opts.Policies.OnException<DbException>()
        .RetryWithCooldown(1.Seconds(), 2.Seconds(), 5.Seconds());

    // Everything else that still fails → dead-letter queue
    opts.Policies.OnException<Exception>()
        .MoveToErrorQueue();
});
```

The dead-letter table lives in the same PostgreSQL DB (`wolverine_dead_letters`).

---

## 10.3 Pod Restart Recovery

On restart, `StartupRecoveryService` runs before any automation:

```
1. Run EF Core migrations (idempotent).
2. AgentBootstrapService: load token from DB (or register if absent).
3. Replay any Wolverine outbox messages that were not yet acknowledged.
4. Load all active `ShipPlanRecord` rows.
5. For each planned ship:
   - Ship.ArrivesAt > UtcNow  → mark IN_TRANSIT; GameLoopService will handle arrival.
   - Ship.ArrivesAt <= UtcNow → treat as arrived; publish `ShipArrivedEvent` / `ShipInOrbitEvent`.
   - Ship is docked           → publish the matching docked role event or `ShipIdleDockedEvent`.
   - Ship is in orbit         → publish the matching in-orbit role event or `ShipNeedsDockingEvent`.
6. Start all BackgroundServices.
```

No API GETs are issued during recovery for ships that were already synced. Only if the last sync
was more than `RecoverySyncThresholdMinutes` (default: 60) ago will a `SyncAllShipsCommand` be
dispatched.

---

## 10.4 `AgentTokenInvalidEvent` Handling

When a `401` is received:

1. `SpaceTradersApiClient` throws `SpaceTradersApiException(401)`.
2. The calling handler fails; Wolverine retries per policy.
3. After retries exhausted, message goes to dead-letter queue.
4. `AgentTokenInvalidHandler` (subscribes to `AgentTokenInvalidEvent`) sets
   `AutomationSettings.Enabled = false` in the DB.
5. Dashboard shows a red banner: *"Agent token invalid – automation paused"*.
6. Operator corrects the token via user-secrets / K8s Secret and redeploys or uses the
   `/control/automation/enable` endpoint after fixing.

---

## 10.5 API Unavailability Handling

When `RetryHandler` exhausts all 502 retries:

1. Publishes `ApiUnavailableEvent` via Wolverine.
2. `ApiUnavailabilityHandler` sets `AutomationSettings.Enabled = false`.
3. A scheduled Wolverine message (`ScheduleAsync`) probes `GET /` every 30 s.
4. On successful probe → sets `Enabled = true`, publishes `ApiAvailableEvent`, resumes.

---

## 10.6 Logging Standards

All exceptions must be logged with:
- `ShipSymbol` (when applicable)
- `CommandType` / `EventType`
- `AttemptNumber` (retry count)
- Full exception including inner exceptions

Use structured logging (Serilog – see Phase 6 in `09-milestones.md`):

```csharp
_logger.LogError(ex,
    "Command {CommandType} failed for ship {ShipSymbol} (attempt {Attempt})",
    command.GetType().Name, command.ShipSymbol, attemptNumber);
```

---

## 10.7 Additional Edge Cases

### Stranded Ship (No Fuel, No Credits)
```
1. Ship arrives at a waypoint with no fuel and agent credits < refuel cost.
2. A docked-state refuel handler dispatches `RefuelShipCommand` at Priority.Critical – fails with 4xx (insufficient credits).
3. After retry exhaustion → move ship to Idle, publish `ShipStrandedEvent`.
4. `ShipStrandedHandler`:
   - Logs a structured warning with ship symbol, waypoint, credits, fuel level.
    - Suspends the ship plan or sets it to idle docked so other ships are not blocked.
   - If other ships have credits income → re-attempts refuel after configurable cooldown (default: 5 min).
5. Dashboard shows a yellow alert: "Ship {symbol} stranded at {waypoint} – insufficient credits".
```

### Concurrent Modification Conflict
When two Wolverine handlers update the same `CachedShip` row simultaneously:
- Configure EF Core optimistic concurrency via a `xmin` (PostgreSQL system column) concurrency token.
- On `DbUpdateConcurrencyException`: reload the entity, re-apply the update, retry once.
- If second retry also fails → let the exception propagate to Wolverine's retry policy.

```csharp
// In entity configuration:
builder.Property<uint>("xmin").IsRowVersion();
```

### Circular Route Detection
If `TradeAnalyser.ScoreRoutes` returns the same origin/destination pair on consecutive assignments:
- Track last 3 completed assignments per ship in memory.
- If the same (buyWaypoint, sellWaypoint) appears twice in a row → score that route 0 for this ship on the next cycle, forcing exploration of the next best route.
- Reset the counter when market data changes (`MarketDataRefreshedEvent`).

### All Markets Unavailable / No Trade Routes
```
1. TradeAnalyser returns an empty list.
2. Role planner falls through to a scout plan.
3. If no unvisited waypoints remain → ship remains idle docked.
4. GameLoopService detects all ships idle docked → publishes AllShipsIdleEvent.
5. AllShipsIdleHandler:
   - Waits 5 min then dispatches SyncAllShipsCommand + RefreshMarketDataCommand for all known markets.
   - Logs a warning: "All ships idle – no viable routes found; forcing market refresh."
```

### Agent Suspended / Banned by SpaceTraders
If the API returns `403 Forbidden` on any authenticated endpoint:
- `SpaceTradersApiClient` throws `SpaceTradersApiException(403)`.
- Wolverine retries (same as 401 path) then dead-letters the message.
- A dedicated `AgentForbiddenHandler` sets `Automation.Enabled = false`.
- Dashboard shows a red banner: *"Agent account suspended – contact SpaceTraders support."*
- No automatic recovery; operator action required.

---

## 10.8 Graceful Shutdown

When the Kubernetes pod receives `SIGTERM`:
1. ASP.NET Core `IHostApplicationLifetime.ApplicationStopping` fires.
2. `GameLoopService` stops dispatching new assignments.
3. In-flight Wolverine handlers finish their **current atomic step** (e.g. finish a sell call) then stop.
4. Wolverine drains its in-process queue within `HostOptions.ShutdownTimeout` (default: 30 s; increase to 60 s in `Program.cs`).
5. EF Core saves any pending ship cache and `ShipPlanRecord` updates.
6. Process exits cleanly – Kubernetes replaces the pod.

```csharp
// Program.cs
builder.Services.Configure<HostOptions>(opts =>
    opts.ShutdownTimeout = TimeSpan.FromSeconds(60));
```

---

## 10.9 Related Documents

- `02-rate-limiter.md` – HTTP handler chain details (429 / 502 handlers)
- `03-persistence.md` – credential storage and bootstrap flow
- `04-application-events.md` – Wolverine retry/dead-letter configuration
- `05-automation-engine.md §5.7` – full startup & recovery procedure
