# 15 – Architecture Decision Records

Captures the rationale, alternatives, and consequences of major design decisions made in this project.
Each record corresponds to a Key Design Decision in [`00-overview.md`](00-overview.md).

_See [`../GLOSSARY.md`](../GLOSSARY.md) for definitions of terms used below._

---

## ADR-001: Wolverine as the internal event/command bus

**Status:** Accepted

**Context:**
The application needs an in-process bus for commands, queries, and domain events. MediatR is the
most widely-used option but lacks built-in durable messaging.

**Decision:**
Use [WolverineFx](https://wolverinefx.net/) in place of MediatR.

**Rationale:**
- Built-in durable outbox backed by the project's existing PostgreSQL database — guarantees
  at-least-once delivery even if the process crashes mid-transaction.
- Convention-based handler discovery: no `IRequestHandler<,>` boilerplate per message type.
- Native retry policies and dead-letter queue support without additional libraries.
- PostgreSQL-backed scheduled messages (needed for deadline-approaching contract alerts).

**Alternatives considered:**
- **MediatR** – simpler, but no durable outbox; would require a separate library (e.g. NServiceBus,
  Brighter) to add that capability.
- **Raw `Channel<T>`** – no persistence, retry, or DLQ support.

**Consequences:**
- All commands, queries, and notifications must go through `IMessageBus` — never directly instantiate
  handlers.
- Wolverine's outbox requires the application DB to be available before the bus starts.

---

## ADR-002: PostgreSQL + EF Core for all persistence

**Status:** Accepted

**Context:**
The application needs to cache game state locally and store settings. SQLite is the simplest
embedded option; PostgreSQL is the production-grade alternative.

**Decision:**
Use PostgreSQL (via Npgsql + EF Core) for all persistence — including local development.

**Rationale:**
- Dev/prod parity: behaviour differences between SQLite and PostgreSQL (e.g. `xmin` concurrency
  tokens, `jsonb`, array types) have caused subtle bugs in other projects.
- Wolverine's durable outbox requires PostgreSQL anyway.
- Docker makes spinning up a local PostgreSQL instance trivial.

**Alternatives considered:**
- **SQLite** – zero-dependency local setup, but lacks `xmin`, diverges from prod, and is not
  supported by Wolverine's outbox.
- **In-memory EF Core provider** – test-only; not suitable for production or local dev parity.

**Consequences:**
- Docker is a hard local-dev prerequisite (see [`12-local-dev.md`](12-local-dev.md)).
- Connection string must always be provided via user-secrets or environment variable — never hardcoded.

---

## ADR-003: No-GET-after-POST caching rule

**Status:** Accepted

**Context:**
The SpaceTraders API rate-limits callers to 2 req/s sustained / 30 req/60 s burst. Issuing a
follow-up GET to refresh state after every mutating call would consume half the available budget.

**Decision:**
Every mutating POST/PATCH call returns the updated resource in its response body. Apply that
response directly to the local cache — never issue a follow-up GET for the same entity.

**Rationale:**
- Halves API call volume for every trade/navigation action.
- The SpaceTraders API guarantees the response body is the canonical updated state.
- Simplifies command handling: one network round-trip per action, not two.

**Alternatives considered:**
- **Always GET after POST** – simpler cache-invalidation logic, but doubles API call cost.
- **Cache-aside with TTL** – adds staleness risk and background refresh complexity.

**Consequences:**
- Handlers must extract and persist the response payload, not just check the status code.
- Unit tests must assert that no follow-up GET is issued (see [`11-testing.md §11.3`](11-testing.md)).

---

## ADR-004: Dead-reckoning for ship navigation

**Status:** Accepted

**Context:**
Ships take real time to travel between waypoints. Polling `GET /my/ships/{id}` until status
changes to `IN_ORBIT` would consume rate-limit budget on every in-flight ship every few seconds.

**Decision:**
Store the `arrival` timestamp returned by `POST /my/ships/{id}/navigate`. Mark the ship
`IN_TRANSIT` until that timestamp is reached; then transition automatically via the
`GameLoopService` dead-reckoning tick — no API call needed.

**Rationale:**
- Zero API calls during transit regardless of fleet size.
- `arrival` is authoritative (set by the server); no drift or jitter.
- Survives pod restarts: `arrival` is persisted with cached ship navigation state and the active `ShipPlanRecord`.

**Alternatives considered:**
- **Poll GET /my/ships/{id}** – simple, but O(fleet size) calls per tick.
- **Webhook / push notification** – SpaceTraders v2 does not support webhooks.

**Consequences:**
- `GameLoopService` must run a periodic tick (≤ 1 s resolution) to detect arrivals.
- Clock skew between client and server must be tolerated with a small grace period.

---

## ADR-005: Razor Pages for the monitoring dashboard

**Status:** Accepted

**Context:**
The dashboard needs real-time updates (fleet status, credits, rate-limit gauge) and operator
controls (pause, override assignment, change settings).

**Decision:**
Use ASP.NET Core Razor Pages with htmx for partial page updates and Server-Sent Events (SSE)
for real-time streaming.

**Rationale:**
- Razor Pages is the simplest page-based model in ASP.NET Core — minimal ceremony.
- htmx + SSE avoids a full JavaScript SPA build pipeline while still delivering reactive UI.
- `hx-ext="sse"` integrates directly with ASP.NET Core's `IAsyncEnumerable` response streaming.

**Alternatives considered:**
- **Blazor Server** – real-time built-in, but heavier runtime (SignalR connection per user) and
  a separate component mental model.
- **React / Angular SPA** – full JS build pipeline, CORS to manage, more complexity than warranted
  for an internal ops dashboard.

**Consequences:**
- All UI pages live in `SpaceTraders.App` as `.cshtml` / `.cshtml.cs` pairs.
- Server-Sent Events endpoint must be kept lightweight to avoid blocking the thread pool.

---

## ADR-006: Do not use state machines for ship automation

**Status:** Accepted as target direction; supersedes the earlier Stateless state-machine decision.

**Context:**
Ship assignment state machines made automation harder to reason about because most SpaceTraders commands are already gated by physical ship state. A docked ship can buy, sell, refuel, or undock. An in-orbit ship can dock, navigate, or extract. Persisting procedural workflow steps duplicates that state and creates recovery edge cases.

**Decision:**
Use state-gated event handlers and persisted ship plans instead of ship automation state machines. See [Ship Event Command Plan](ship-event-command-plan.md).

**Rationale:**
- The SpaceTraders API already defines legal actions by ship state.
- Role-specific event handlers can be ordered before generic fallback handlers.
- Persisted ship plans capture intent without procedural `StepIndex` recovery.
- Command handlers can guard invalid states before calling the API.
- Market price changes can update plans directly and trigger the correct current-state event.

**Alternatives considered:**
- **Keep manual switch/state machine logic** – works but duplicates physical ship state.
- **Use Stateless** – explicit, but still preserves workflow state that should be derived from ship state plus plan intent.
- **Workflow engine (e.g. Elsa)** – too heavyweight for in-process ship logic.

**Consequences:**
- New automation work should not add state machines.
- Existing state machines should be migrated behind role-specific event handlers and then removed.
- `Stateless` should be removed when no longer used.

---

## See Also

- [`00-overview.md`](00-overview.md) – High-level architecture and key decisions summary
- [`16-sequence-flows.md`](16-sequence-flows.md) – How these decisions play out in key runtime flows
