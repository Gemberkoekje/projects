# 11 – Testing Strategy

## Goals
- Ensure correctness of domain logic, handler behaviour and API contracts without requiring a live
  SpaceTraders account or a running database.
- Keep the test suite fast: unit tests run in milliseconds; integration tests run against a real
  PostgreSQL instance spun up by Testcontainers.
- Establish clear boundaries so developers know where to add new tests.

---

## 11.1 Test Project Structure

```
SpaceTraders.sln
├── tests/
│   ├── SpaceTraders.Domain.Tests          # Unit – pure domain logic, no I/O
│   ├── SpaceTraders.Application.Tests     # Unit – Wolverine handler logic with fakes
│   ├── SpaceTraders.Infrastructure.Tests  # Integration – EF Core + real PostgreSQL (Testcontainers)
│   └── SpaceTraders.API.Tests             # Integration – HTTP endpoints via WebApplicationFactory
```

Each project targets `net10.0` and uses:

| Package | Purpose |
|---------|---------|
| `xunit` | Test runner |
| `FluentAssertions` | Readable assertions |
| `NSubstitute` | Mocking/faking dependencies |
| `Testcontainers.PostgreSql` | Spin up real PostgreSQL for integration tests |
| `Microsoft.AspNetCore.Mvc.Testing` | `WebApplicationFactory` for API tests |
| `WolverineFx.TestingSupport` | In-process Wolverine test host |

---

## 11.2 Unit Tests – Domain (`SpaceTraders.Domain.Tests`)

Test pure domain logic with no dependencies on EF Core, HTTP, or Wolverine.

### What to test
- Value object equality and validation (`WaypointSymbol`, `SystemSymbol`, `TradeSymbol`, etc.)
- Domain event generation (e.g. `Ship.UpdateFuel()` raises `ShipFuelLowEvent` when below 20%)
- `TradeAnalyser.ScoreRoutes()` returns routes in correct priority order
- `TradeAnalyser.ShouldAcceptContract()` returns expected decisions given credit levels
- State machine transition guards (e.g. cannot navigate from `DOCKED` without orbiting first)

### Example
```csharp
[Fact]
public void Ship_UpdateFuel_BelowThreshold_RaisesShipFuelLowEvent()
{
    var ship = Ship.Reconstitute(/* test data */);
    ship.UpdateFuel(new Fuel(Current: 5, Capacity: 100));
    ship.DomainEvents.Should().ContainSingle(e => e is ShipFuelLowEvent);
}
```

---

## 11.3 Unit Tests – Application Handlers (`SpaceTraders.Application.Tests`)

Test Wolverine handler classes in isolation using `NSubstitute` fakes for all I/O.

### Faking `ISpaceTradersApiClient`
```csharp
var apiClient = Substitute.For<ISpaceTradersApiClient>();
apiClient.NavigateShipAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
         .Returns(Task.FromResult(FakeNavigateResponse()));
```

### Faking `SpaceTradersDbContext`
Use an EF Core in-memory provider **only** for handler unit tests where DB behaviour is trivial.
For anything touching migrations, constraints, or JSON columns → use `SpaceTraders.Infrastructure.Tests`.

### What to test
- Each command handler applies the correct fields from the API response to the DB entity.
- No follow-up GET is ever issued after a successful POST (assert `apiClient` received no GET calls).
- `GameLoopService` publishes `ShipArrivedAtWaypointEvent` when `ArrivesAt` has elapsed.
- `ContractWatchService` publishes `ContractDeadlineApproachingEvent` at the right thresholds.
- Dead-reckoning: ship is marked arrived without any API call.

### Example
```csharp
[Fact]
public async Task NavigateShipHandler_AppliesArrivalTimestamp_WithoutFollowUpGet()
{
    // Arrange
    var apiClient = Substitute.For<ISpaceTradersApiClient>();
    var fakeNav   = FakeShipNav(arrivesAt: DateTimeOffset.UtcNow.AddMinutes(5));
    apiClient.NavigateShipAsync("SHIP-1", "WP-DEST", default).Returns(fakeNav);

    var db      = BuildInMemoryDb();
    var handler = new NavigateShipHandler(apiClient, db);

    // Act
    await handler.Handle(new NavigateShipCommand("SHIP-1", "WP-DEST", Priority.Normal), default);

    // Assert
    var ship = await db.Ships.FindAsync("SHIP-1");
    ship!.ArrivesAt.Should().BeCloseTo(fakeNav.Route.Arrival, TimeSpan.FromSeconds(1));
    await apiClient.DidNotReceive().GetShipAsync(Arg.Any<string>(), Arg.Any<CancellationToken>());
}
```

---

## 11.4 Integration Tests – Persistence (`SpaceTraders.Infrastructure.Tests`)

Use **Testcontainers** to spin up a real PostgreSQL instance per test class (shared within a class,
torn down after).

### Setup pattern
```csharp
public class ShipRepositoryTests : IAsyncLifetime
{
    private readonly PostgreSqlContainer _pg = new PostgreSqlBuilder().Build();

    public async Task InitializeAsync()
    {
        await _pg.StartAsync();
        // build DbContext with _pg.GetConnectionString(), run migrations
    }

    public async Task DisposeAsync() => await _pg.DisposeAsync();
}
```

### What to test
- Migrations apply cleanly against a fresh PostgreSQL instance.
- EF Core entity configurations (owned types, JSON columns, value converters) round-trip correctly.
- `AgentBootstrapService` inserts `StoredCredential` on first run and reads it back on second run.
- Optimistic concurrency conflicts are handled gracefully.
- `ShipAssignmentRecord` persists and resumes `StepIndex` correctly.

---

## 11.5 Integration Tests – API Endpoints (`SpaceTraders.API.Tests`)

Use `WebApplicationFactory<Program>` with test doubles substituted for external dependencies.

### Setup pattern
```csharp
public class StatusEndpointsTests : IClassFixture<WebApplicationFactory<Program>>
{
    private readonly HttpClient _client;

    public StatusEndpointsTests(WebApplicationFactory<Program> factory)
    {
        _client = factory.WithWebHostBuilder(builder =>
        {
            builder.ConfigureServices(services =>
            {
                // Replace real DB with Testcontainers or in-memory
                // Replace ISpaceTradersApiClient with NSubstitute fake
            });
        }).CreateClient();

        _client.DefaultRequestHeaders.Add("X-Api-Key", "test-key");
    }
}
```

### What to test
- `GET /status/agent` returns `200` with correct DTO shape.
- `PUT /settings/{key}` persists the new value and returns `200`.
- Requests without `X-Api-Key` return `401`.
- `/health/live` returns `200` without auth.
- `/health/ready` returns `503` when the DB is unavailable.

---

## 11.6 What Not to Test

- The SpaceTraders API itself – that is a third-party service.
- Wolverine's internal message routing – trust the library; test your handler logic.
- EF Core's SQL generation – trust the ORM; test your entity shapes and migrations.

---

## 11.7 Running the Tests

```powershell
# All tests
dotnet test

# Unit tests only (fast, no Docker required)
dotnet test --filter "Category!=Integration"

# Integration tests only
dotnet test --filter "Category=Integration"
```

Tag integration tests with `[Trait("Category", "Integration")]`.

Testcontainers requires Docker to be running. Tests will be skipped automatically if Docker is not
available (add `[SkippableFact]` from the `Xunit.SkippableFact` package where needed).

---

## 11.8 Related Documents

- `04-application-events.md` – Wolverine handler shapes that need to be tested
- `05-automation-engine.md` – State machine transitions that need unit coverage
- `09-milestones.md` – Testing tasks are spread across all phases
