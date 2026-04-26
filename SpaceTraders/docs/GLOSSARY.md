# Glossary

Terms used throughout this project and its documentation.

_See [`docs/plan/00-overview.md`](plan/00-overview.md) for the architecture overview and [`docs/plan/`](plan/) for the full implementation plan._

---

| Term | Definition |
|------|------------|
| **Account Token** | A long-lived token issued by `my.spacetraders.io` that authorises agent registration (`POST /register`). Used once at bootstrap; never stored beyond the registration transaction. |
| **Agent** | The player's in-game entity. Has credits, a fleet of ships, and a faction. Represented by the `Agent` aggregate in the domain. |
| **Agent Token** | A bearer token returned by `POST /register` and used for all `/my/*` authenticated API calls. Stored in the `stored_credentials` table and loaded into `IAgentTokenProvider` at startup. |
| **Assignment** | Legacy term for an autonomous ship task. Target automation uses a persisted ship plan instead of assignment state-machine steps. |
| **Burst Limit** | The SpaceTraders API allows up to 30 requests within any 60-second window. Enforced by the burst bucket in the rate limiter. |
| **Circuit Breaker** | A resilience pattern that stops sending requests when a threshold of failures is reached, giving the downstream service time to recover. See `02-rate-limiter.md §2.6`. |
| **Dead Reckoning** | Computing a ship's position and arrival time from the `arrival` timestamp in the navigate response, without polling the API for status. See `00-overview.md Key Decision 4`. |
| **Dead-Letter Queue** | A Wolverine-managed PostgreSQL table (`wolverine_dead_letters`) where messages land after exhausting all retry attempts. Prevents message loss. |
| **DelegatingHandler** | An ASP.NET Core `HttpMessageHandler` that wraps the inner handler to add cross-cutting behaviour (rate limiting, retries, metrics) transparently to callers. |
| **Domain Event** | A record of something significant that happened in the domain (e.g. `ShipDockedEvent`, `ShipInOrbitEvent`, `ShipArrivedEvent`). Published via Wolverine; zero or more handlers react. |
| **Durable Outbox** | A Wolverine feature backed by the project's PostgreSQL database. Guarantees that a message is published even if the process crashes immediately after the DB write. |
| **EF Core** | Entity Framework Core – the ORM used to map C# entities to PostgreSQL tables and generate migrations. |
| **Fleet** | All ships owned by the agent. |
| **GameLoopService** | The master `BackgroundService` that runs the dead-reckoning tick and triggers startup sync. See `05-automation-engine.md §5.2`. |
| **Leader Election** | A mechanism ensuring only one pod runs singleton services (e.g. `GameLoopService`) when `replicas > 1`. Planned via Wolverine's built-in support. See `08-kubernetes.md §8.4`. |
| **Minimal API** | The ASP.NET Core programming model used in `SpaceTraders.API` – endpoint groups defined with `MapGet`/`MapPost` rather than controllers. |
| **No-GET-after-POST** | The core caching rule: every mutating API call returns fresh state; apply it directly without issuing a follow-up GET. See `00-overview.md Key Decision 3`. |
| **Npgsql** | The official .NET PostgreSQL driver and the EF Core provider used in `SpaceTraders.Infrastructure.Persistence`. |
| **Priority Queue** | The `PriorityApiQueue` that buffers outgoing SpaceTraders API requests at three priority levels (Critical, Normal, Low). Drained by `ApiDispatcher` at ≤ 2 req/s. |
| **Razor Pages** | The ASP.NET Core page-based UI model used in `SpaceTraders.App`. Each page is a `.cshtml` + `.cshtml.cs` pair. |
| **Scalar** | An OpenAPI documentation UI (`Scalar.AspNetCore`) served at `/scalar` in Development. Replaces Swagger UI. |
| **Server-Sent Events (SSE)** | A one-way HTTP streaming mechanism used to push real-time updates from the server to the dashboard without WebSockets. Used with htmx's `hx-ext="sse"`. |
| **ShipAssignmentRecord** | Legacy persisted assignment representation. Target automation replaces active use with `ShipPlanRecord`. |
| **ShipPlanRecord** | Target persisted representation of a ship's role intent: role, objective, waypoints, goods, units, and role-specific JSON details. It does not store procedural state-machine steps. |
| **SpaceTradersApiClient** | The typed `HttpClient` wrapper in `SpaceTraders.Infrastructure.SpaceTradersAPI` that abstracts all calls to the SpaceTraders v2 REST API. |
| **State-gated event handler** | A handler that may only issue commands valid for the ship's current physical state, such as docked or in orbit. |
| **Stateless** | A .NET library for building state machines. It is legacy for ship automation and should be removed when no longer used. |
| **Token Bucket** | A rate-limiting algorithm that grants a fixed number of request tokens per time window. Two buckets are used: per-second (2 tokens) and burst (30 tokens/60 s). |
| **Wolverine** | The in-process command/event/query bus used in place of MediatR. Provides convention-based handlers, retry policies, and a durable PostgreSQL-backed outbox. See `04-application-events.md §4.0`. |
| **xmin** | A PostgreSQL system column (row version) used as an optimistic concurrency token in EF Core to detect concurrent modifications. |
