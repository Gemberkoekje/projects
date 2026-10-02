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
| **Anomaly** | A subject (a ship, a contract, the agent, the API, a log statement) that breaks a health rule. While it lasts, `spacetraders_anomaly_active{rule,subject}` is 1; the journal logs `AnomalyRaised` when it starts and `AnomalyCleared` when it ends. `docs/HOW_IT_WORKS.md` section 12 lists the rules. |
| **Assignment** | A ship's current task in `ship_assignment_records` (`ShipAssignmentRecord`): a type such as `Scout` or `Contract`, origin, destination, cargo and progress. The contract plan works only through assignments; the scout plan writes both an assignment and a goal. |
| **Burst Limit** | Per the API guide (https://spacetraders.io/api-guide/rate-limits): on top of the limit of 2 requests per second, up to 30 more requests within a 60-second burst duration, counted per IP address and per account. `RequestBudget` follows it. |
| **Dead Reckoning** | Treating a ship as arrived once its cached arrival time has passed, without asking the API. The contract commands do this (`FulfillContractDeliveryCommand`, `MineResourceVolumeCommand`). `GameLoopService` no longer does, despite the name of its `DeadReckoningInterval` constant. |
| **Dead-Letter Queue** | Not used. A failed message is retried three times (after 250 ms, 500 ms and 1 s) and then discarded (`SpaceTraders.Application/DependencyInjection.cs`). |
| **DelegatingHandler** | An ASP.NET Core `HttpMessageHandler` that wraps the inner handler to add cross-cutting behaviour (rate limiting, retries) transparently to callers. |
| **Domain Event** | Two kinds exist. *Bus events* are published through Wolverine and handled by zero or more handlers (for example `ShipInTransitEvent`, `ShipNavigationCompletedEvent`). *Aggregate events* are raised inside domain aggregates (`AggregateRoot.RaiseDomainEvent`) but never dispatched, so nothing handles them (B7). |
| **EF Core** | Entity Framework Core – the ORM used to map C# entities to PostgreSQL tables. The schema itself is created and extended at startup by `SpaceTradersDatabaseInitializer`, not by EF migrations. |
| **Fleet** | All ships owned by the agent. |
| **GameLoopService** | The leader-only loop that runs every 5 seconds: it bootstraps the plans that are switched on, steps every ship's active goal, drives contract assignments and publishes API availability changes. |
| **Gas Giant** | A waypoint of type `GAS_GIANT`, the only kind a gas siphon works at. Every gas giant counts as yielding HYDROCARBON, LIQUID_HYDROGEN and LIQUID_NITROGEN (`GasGiants`, slice 6.7): the game publishes no table, and its traits name no gas. |
| **Gather** | What the command ship does in its spare time (slice 6.8): mine or siphon whatever a market buys at the nearest asteroid or gas giant it can work (D35), without surveys, until its hold is full, then sell each good where it fetches most after fuel (D36). One such trip is a `GatherAndSellGoal`. |
| **Goal** | What a ship is working towards, such as `ScoutWaypointGoal`, `DeployProbeGoal`, `MineAndSellGoal`, `SiphonAndSellGoal`, `GatherAndSellGoal`, `TradeBetweenMarketsGoal` or `SurveyWaypointGoal`. Each ship has at most one active goal. |
| **Goal Executor** | Code that advances one kind of goal by one step, such as `MineAndSellGoalExecutor`. `ShipGoalExecutorService` picks the executor for a ship's active goal. |
| **Health Rule** | An intended behaviour written down as a check the bot runs on itself every minute (`IHealthRule`, evaluated by `HealthMonitorService`), such as "a fulfilled contract has no active plan or assignment". A broken rule is an anomaly. |
| **Leader Election** | A mechanism ensuring only one instance runs leader-only automation work. Implemented by `LeaderElectionService` and backed by the `leader_leases` table. |
| **Market Watch** | `MarketWatchService`, the last step of every tick: one market a tick, among those with one of our ships at the waypoint, is fetched again once `Market.RefreshMinutes` (5) have passed since its prices were last seen. The API shows a market's prices only while a ship is there. |
| **Read, Write** | For the rate limit (D19): a read is a GET, a write anything else. Writes go first: a read gives way while a write waits, and leaves part of the burst to writes. |
| **Minimal API** | The ASP.NET Core programming model used in `SpaceTraders.API` – endpoint groups defined with `MapGet`/`MapPost` rather than controllers. |
| **Npgsql** | The official .NET PostgreSQL driver and the EF Core provider used in `SpaceTraders.Infrastructure.Persistence`. |
| **Plan** | One of the eight automation services `GameLoopService` bootstraps on every tick, each with its own switch: scout, contract, probe deployment, survey, mining, siphon, trading and spare time. Each keeps its own state in the database. |
| **Run** | A period of operation with a strategy label, start and end credits and a settings snapshot, recorded in the `runs` table by `RunLifecycleService`. |
| **SpaceTradersApiClient** | The typed `HttpClient` wrapper in `SpaceTraders.Infrastructure.SpaceTradersAPI` that abstracts all calls to the SpaceTraders v2 REST API. |
| **State-gated command** | A ship command that checks the ship's cached state (docked, in orbit, in transit) before calling the API. When the state is wrong, it publishes `ShipStateMismatchEvent` instead. |
| **Siphon** | Collecting gas at a gas giant with a gas siphon (`POST my/ships/{ship}/siphon`), the mining laser's counterpart for gases. It takes no survey: the API's siphon call has none, and a surveyor finds ores only. |
| **Siphoner** | A ship the siphon plan gives trips (slice 6.7): a gas siphon, a hold and a tank, and nothing to mine or survey with (`FleetRoles.IsSiphoner`), in practice a siphon drone. The command ship has a siphon too, but it mines or surveys, and siphons only in its spare time. |
| **Spare Time** | The time a surveyor that can also mine or siphon (the command ship) has nothing to survey or trade, while the survey and spare-time plans are on (slice 6.8, D34). It gathers then; a survey that needs taking takes it at once, with its hold aboard (D37), and a route that waits for it takes it after it sells its hold (D34). |
| **Stateless** | A .NET state-machine library. The application project references it, but no code uses it. |
| **Request Budget** | `RequestBudget`, the client's copy of the API guide's limit: 2 requests in any second and, once those are used, up to 30 more in any 60 seconds. Both windows slide, so the client never exceeds a fixed window the server counts in. A singleton, so it survives the HttpClient factory recreating its handlers. |
| **Lucrative** | A trade trip that earns at least `Trade.MinProfitPerUnit` per unit after the fuel for the whole trip, the flight to the buy market included (D14). The trading plan offers only lucrative trips, and a trip checks it again at the buy market before it buys. |
| **Local Queue** | Wolverine's in-process queue for published messages, kept in memory. Until slice 1.3 it was a *durable* local queue that also stored every message in Postgres, and with Wolverine's durability agent off it never deleted a handled one (B2). |
| **Tick** | One pass of `GameLoopService`. |
| **Trader** | Any ship with a cargo hold and a fuel tank that the trading plan may give a trip: no goal (or a blocked one), no assignment, not in transit (slice 6.5). |
| **Trade Trip** | One good bought at one market and sold at another (`TradeBetweenMarketsGoal`): one purchase, refuelling at markets on the way where a flight is beyond one tank. Its route, the good with its buy and sell market, belongs to one trader at a time (D18). |
| **WebUI** | The React/Vite dashboard in `SpaceTraders.WebUI`, served at `/spacetraders/dashboard`. It reads the internal API with the `X-Api-Key` header and listens to the SignalR hub for refresh hints. |
| **Wolverine** | The in-process command/event bus used in place of MediatR. Provides convention-based handler discovery, retry policies and in-memory local queues. |
