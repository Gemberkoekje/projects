# Glossary

Terms used throughout this project and its documentation. See `HOW_IT_WORKS.md` for how the code
works today and `../PLAN.md` for what happens next (B- and D-numbers refer to its known issues and
decisions).

---

| Term | Definition |
|------|------------|
| **Account Token** | A long-lived token issued by `my.spacetraders.io` that authorises agent registration (`POST /register`). Used by bootstrap when a new agent must be registered. |
| **Agent** | The player's in-game entity. Has credits, a fleet of ships, and a faction. Represented by the `Agent` aggregate in the domain. |
| **Agent Id** | The short id every agent-scoped row is keyed on: the agent's symbol and the server's reset date, such as `GEMBER@2026-09-27` (`AgentIdentity`). The agent that takes the same symbol after a reset gets a new id. The database keeps the active agent's rows only, plus every agent's `runs`. |
| **Agent Token** | A bearer token returned by `POST /register` and used for all `/my/*` authenticated API calls. Stored only in the `stored_credentials` table, and loaded into `IAgentTokenProvider` at startup. |
| **Anomaly** | *Planned (PLAN.md phase 3).* A broken health rule, exposed as a metric and a journal event. |
| **Assignment** | A ship's current task in `ship_assignment_records` (`ShipAssignmentRecord`): a type such as `Scout` or `Contract`, origin, destination, cargo and progress. The contract plan works only through assignments; the scout plan writes both an assignment and a goal. |
| **Burst Limit** | Per the API guide (https://spacetraders.io/api-guide/rate-limits): on top of the limit of 2 requests per second, up to 30 more requests within a 60-second burst duration, counted per IP address and per account. `RequestBudget` follows it. |
| **Dead Reckoning** | Treating a ship as arrived once its cached arrival time has passed, without asking the API. The contract commands do this (`FulfillContractDeliveryCommand`, `MineResourceVolumeCommand`). `GameLoopService` no longer does, despite the name of its `DeadReckoningInterval` constant. |
| **Dead-Letter Queue** | Not used. A failed message is retried three times (after 250 ms, 500 ms and 1 s) and then discarded (`SpaceTraders.Application/DependencyInjection.cs`). |
| **DelegatingHandler** | An ASP.NET Core `HttpMessageHandler` that wraps the inner handler to add cross-cutting behaviour (rate limiting, retries) transparently to callers. |
| **Domain Event** | Two kinds exist. *Bus events* are published through Wolverine and handled by zero or more handlers (for example `ShipInTransitEvent`, `ShipNavigationCompletedEvent`). *Aggregate events* are raised inside domain aggregates (`AggregateRoot.RaiseDomainEvent`) but never dispatched, so nothing handles them (B7). |
| **EF Core** | Entity Framework Core – the ORM used to map C# entities to PostgreSQL tables. The schema itself is created and extended at startup by `SpaceTradersDatabaseInitializer`, not by EF migrations. |
| **Fleet** | All ships owned by the agent. |
| **GameLoopService** | The leader-only loop that runs every 5 seconds: it bootstraps the five plans, steps every ship's active goal, drives contract assignments and publishes API availability changes. |
| **Goal** | What a ship is working towards, such as `ScoutWaypointGoal`, `DeployProbeGoal`, `MineAndSellGoal`, `TradeBetweenMarketsGoal` or `SurveyWaypointGoal`. Each ship has at most one active goal. |
| **Goal Executor** | Code that advances one kind of goal by one step, such as `MineAndSellGoalExecutor`. `ShipGoalExecutorService` picks the executor for a ship's active goal. |
| **Health Rule** | *Planned (PLAN.md phase 3).* An intended behaviour written down as a check the bot runs on itself, such as "a fulfilled contract has no active plan". A broken rule is an anomaly. |
| **Leader Election** | A mechanism ensuring only one instance runs leader-only automation work. Implemented by `LeaderElectionService` and backed by the `leader_leases` table. |
| **Minimal API** | The ASP.NET Core programming model used in `SpaceTraders.API` – endpoint groups defined with `MapGet`/`MapPost` rather than controllers. |
| **Npgsql** | The official .NET PostgreSQL driver and the EF Core provider used in `SpaceTraders.Infrastructure.Persistence`. |
| **Plan** | One of the five automation services `GameLoopService` bootstraps on every tick: scout, contract, probe deployment, mining and trading. Each keeps its own state in the database. |
| **Run** | A period of operation with a strategy label, start and end credits and a settings snapshot, recorded in the `runs` table by `RunLifecycleService`. |
| **SpaceTradersApiClient** | The typed `HttpClient` wrapper in `SpaceTraders.Infrastructure.SpaceTradersAPI` that abstracts all calls to the SpaceTraders v2 REST API. |
| **State-gated command** | A ship command that checks the ship's cached state (docked, in orbit, in transit) before calling the API. When the state is wrong, it publishes `ShipStateMismatchEvent` instead. |
| **Stateless** | A .NET state-machine library. The application project references it, but no code uses it. |
| **Request Budget** | `RequestBudget`, the client's copy of the API guide's limit: 2 requests in any second and, once those are used, up to 30 more in any 60 seconds. Both windows slide, so the client never exceeds a fixed window the server counts in. A singleton, so it survives the HttpClient factory recreating its handlers. |
| **Local Queue** | Wolverine's in-process queue for published messages, kept in memory. Until slice 1.3 it was a *durable* local queue that also stored every message in Postgres, and with Wolverine's durability agent off it never deleted a handled one (B2). |
| **Tick** | One pass of `GameLoopService`. |
| **WebUI** | The React/Vite dashboard in `SpaceTraders.WebUI`, served at `/spacetraders/dashboard`. It reads the internal API with the `X-Api-Key` header and listens to the SignalR hub for refresh hints. |
| **Wolverine** | The in-process command/event bus used in place of MediatR. Provides convention-based handler discovery, retry policies and in-memory local queues. |
