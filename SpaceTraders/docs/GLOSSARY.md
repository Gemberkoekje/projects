# Glossary

Terms used throughout this project and its documentation. See `docs/implementation/CURRENT_IMPLEMENTATION_OVERVIEW.md` for current architecture and `spacetraders.md` for SpaceTraders.io gameplay/API reference notes.

---

| Term | Definition |
|------|------------|
| **Account Token** | A long-lived token issued by `my.spacetraders.io` that authorises agent registration (`POST /register`). Used by bootstrap when a new agent must be registered. |
| **Agent** | The player's in-game entity. Has credits, a fleet of ships, and a faction. Represented by the `Agent` aggregate in the domain. |
| **Agent Token** | A bearer token returned by `POST /register` and used for all `/my/*` authenticated API calls. Stored in the `stored_credentials` table and loaded into `IAgentTokenProvider` at startup. |
| **Assignment** | The current persisted autonomous ship role/task, stored by `ShipAssignmentRecord` and changed through ship assignment commands. |
| **Burst Limit** | The SpaceTraders API allows up to 30 requests within any 60-second window. Enforced by the burst bucket in the rate limiter. |
| **Dead Reckoning** | Computing a ship's position and arrival from cached navigation timestamps without polling every ship continuously. Implemented by `GameLoopService`. |
| **Dead-Letter Queue** | A Wolverine-managed PostgreSQL table (`wolverine_dead_letters`) where messages land after exhausting all retry attempts. Prevents message loss. |
| **DelegatingHandler** | An ASP.NET Core `HttpMessageHandler` that wraps the inner handler to add cross-cutting behaviour (rate limiting, retries, metrics) transparently to callers. |
| **Domain Event** | A record of something significant that happened in the domain (e.g. `ShipDockedEvent`, `ShipInOrbitEvent`, `ShipArrivedEvent`). Published via Wolverine; zero or more handlers react. |
| **Durable Local Queue** | Wolverine local queue durability backed by PostgreSQL in production hosts. Configured in `SpaceTraders.API/Program.cs` with `PersistMessagesWithPostgresql(...)` and `UseDurableLocalQueues()`. |
| **EF Core** | Entity Framework Core – the ORM used to map C# entities to PostgreSQL tables and generate migrations. |
| **Fleet** | All ships owned by the agent. |
| **GameLoopService** | A recurring `BackgroundService` that runs the dead-reckoning tick and publishes ship arrival, low-fuel, and API availability events. |
| **Leader Election** | A mechanism ensuring only one instance runs leader-only automation work. Implemented by `LeaderElectionService` and backed by the `LeaderLease` persistence entity. |
| **Minimal API** | The ASP.NET Core programming model used in `SpaceTraders.API` – endpoint groups defined with `MapGet`/`MapPost` rather than controllers. |
| **Npgsql** | The official .NET PostgreSQL driver and the EF Core provider used in `SpaceTraders.Infrastructure.Persistence`. |
| **Razor Pages** | The ASP.NET Core page-based UI model used in `SpaceTraders.App`. Each page is a `.cshtml` + `.cshtml.cs` pair. |
| **ShipAssignmentRecord** | Persisted assignment representation for a ship's active automation role. |
| **SpaceTradersApiClient** | The typed `HttpClient` wrapper in `SpaceTraders.Infrastructure.SpaceTradersAPI` that abstracts all calls to the SpaceTraders v2 REST API. |
| **State-gated event handler** | A handler that may only issue commands valid for the ship's current physical state, such as docked or in orbit. |
| **Stateless** | A .NET library referenced by the application project for state-machine support. |
| **Token Bucket** | A rate-limiting algorithm that grants a fixed number of request tokens per time window. `RateLimitingHandler` uses per-second and burst token buckets. |
| **Wolverine** | The in-process command/event/query bus used in place of MediatR. Provides convention-based handlers, retry policies, and PostgreSQL-backed durable local queues. |
