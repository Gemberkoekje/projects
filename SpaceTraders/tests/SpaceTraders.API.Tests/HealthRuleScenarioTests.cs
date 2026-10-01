using System.Globalization;
using System.Net;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;
using NSubstitute;
using SpaceTraders.Application.Automation;
using SpaceTraders.Application.DTOs;
using SpaceTraders.Application.Goals;
using SpaceTraders.Application.Health;
using SpaceTraders.Application.Interfaces;
using SpaceTraders.Application.Interfaces.Repositories;
using SpaceTraders.Application.Ports;
using SpaceTraders.Domain.Goals;
using SpaceTraders.Infrastructure.SpaceTradersAPI.Clients;

namespace SpaceTraders.API.Tests;

/// <summary>
/// Phase 3.2, done when: a deliberately broken scenario in a test host raises each rule. The host is
/// the real composition (rules, monitor, log pipeline, API client handlers) with the database and the
/// game API replaced; each test breaks one thing and runs the monitor.
/// </summary>
public sealed class HealthRuleScenarioTests
{
    private static readonly DateTimeOffset Start = new(2026, 10, 01, 12, 00, 00, TimeSpan.Zero);

    [Fact]
    public async Task AContractWithoutDeliveries_RaisesContractStalled()
    {
        await using var host = new HealthScenarioHost();
        host.ContractPlanIs(ContractMineralPlanStatus.Active, updatedAt: Start.AddHours(-1));
        host.ContractIs(fulfilled: 11, deadline: Start.AddDays(6));

        await host.EvaluateAsync(Start);
        await host.EvaluateAsync(Start.AddHours(4).AddMinutes(1));

        host.Metrics.Received(1).Anomaly("ContractStalled", "C-1", true);
    }

    [Fact]
    public async Task AFulfilledContractThatStaysOpen_RaisesContractLeftOpen()
    {
        await using var host = new HealthScenarioHost();
        host.ContractPlanIs(ContractMineralPlanStatus.Active, updatedAt: Start);
        host.ContractIs(fulfilled: 42, deadline: Start.AddDays(6), isFulfilled: true);

        await host.EvaluateAsync(Start);
        await host.EvaluateAsync(Start.AddMinutes(6));

        host.Metrics.Received(1).Anomaly("ContractLeftOpen", "C-1", true);
    }

    [Fact]
    public async Task ANearDeadlineWithLittleDelivered_RaisesContractDeadlineAtRisk()
    {
        await using var host = new HealthScenarioHost();
        host.ContractPlanIs(ContractMineralPlanStatus.Active, updatedAt: Start);
        host.ContractIs(fulfilled: 10, deadline: Start.AddHours(20));

        await host.EvaluateAsync(Start);

        host.Metrics.Received(1).Anomaly("ContractDeadlineAtRisk", "C-1", true);
    }

    [Fact]
    public async Task AShipWhoseGoalGoesNowhere_RaisesShipStuck()
    {
        await using var host = new HealthScenarioHost();
        host.ShipsAre(Drone("SHIP-1", lastSyncedAt: Start.AddHours(-1)));
        host.GoalIs("SHIP-1", new ScoutWaypointGoal { TargetWaypointSymbol = "X1-AB-B2" });

        await host.EvaluateAsync(Start);
        await host.EvaluateAsync(Start.AddMinutes(31));
        await host.EvaluateAsync(Start.AddMinutes(32));

        host.Metrics.Received(1).Anomaly("ShipStuck", "SHIP-1", true);
    }

    [Fact]
    public async Task AMinerLeftIdleWhileTheContractWaitsForOne_RaisesShipLeftIdle()
    {
        await using var host = new HealthScenarioHost();
        host.ContractPlanIs(ContractMineralPlanStatus.PendingBudget, updatedAt: Start);
        host.ContractIs(fulfilled: 0, deadline: Start.AddDays(6));
        host.ShipsAre(Drone("SHIP-1", lastSyncedAt: Start));

        await host.EvaluateAsync(Start);
        await host.EvaluateAsync(Start.AddMinutes(11));

        host.Metrics.Received(1).Anomaly("ShipLeftIdle", "SHIP-1", true);
    }

    [Fact]
    public async Task AShipThatLoops_RaisesCircuitBreakerTripped()
    {
        await using var host = new HealthScenarioHost();
        host.ShipsAre(Drone("SHIP-1", lastSyncedAt: Start));

        // The host's own breaker, the one every goal step goes through: 61 steps in a minute.
        var breaker = host.Services.GetRequiredService<IGoalStepCircuitBreaker>();
        for (var step = 0; step <= 60; step++)
        {
            breaker.RecordStep("SHIP-1", 60, Start.AddMilliseconds(100 * step));
        }

        await host.EvaluateAsync(Start.AddMinutes(1));

        host.Metrics.Received(1).Anomaly("CircuitBreakerTripped", "SHIP-1", true);
    }

    [Fact]
    public async Task AWarningOnEveryTick_RaisesRepeatingError()
    {
        await using var host = new HealthScenarioHost();
        await host.EvaluateAsync(TimeProvider.System.GetUtcNow());

        // Through the host's own log pipeline (Serilog), as a failing tick step would log.
        var logger = host.Services.GetRequiredService<ILogger<GameLoopService>>();
        for (var tick = 0; tick < 6; tick++)
        {
            logger.LogWarning("GameLoopService: the goal step for ship {ShipSymbol} failed; the rest of the tick carries on.", "SHIP-1");
        }

        await host.EvaluateAsync(TimeProvider.System.GetUtcNow().AddSeconds(1));

        host.Metrics.Received(1).Anomaly("RepeatingError", "GameLoopService: the goal step for ship {ShipSymbol} failed; the rest of the tick carries on.", true);
    }

    [Fact]
    public async Task AWorkingFleetWhoseCreditsDontMove_RaisesCreditsUnchanged()
    {
        await using var host = new HealthScenarioHost();
        host.ShipsAre(Drone("SHIP-3", lastSyncedAt: Start));
        host.AssignmentsAre(new ShipAssignmentDto("SHIP-3", "Contract", "X1-AB-A1", "X1-AB-H58", "IRON_ORE", "C-1", 0, Start, null, RequiredUnits: 15));
        host.AgentIs(new AgentModel("SPECTER", null, "X1-AB-A1", 137_184, "COSMIC", 3));

        await host.EvaluateAsync(Start);
        await host.EvaluateAsync(Start.AddHours(24).AddMinutes(1));

        host.Metrics.Received(1).Anomaly("CreditsUnchanged", "SPECTER", true);
    }

    [Fact]
    public async Task A401FromTheApi_RaisesApiUnauthorized()
    {
        await using var host = new HealthScenarioHost();
        await host.EvaluateAsync(TimeProvider.System.GetUtcNow());
        host.GameApi.Respond = _ => new HttpResponseMessage(HttpStatusCode.Unauthorized);

        // Through the API client's own handlers, as every call to the game goes.
        using var response = await host.GameApiClient().GetAsync(new Uri("my/agent", UriKind.Relative));

        await host.EvaluateAsync(TimeProvider.System.GetUtcNow().AddSeconds(1));
        host.Metrics.Received(1).Anomaly("ApiUnauthorized", "api", true);
    }

    [Fact]
    public async Task RepeatedThrottling_RaisesApiThrottled()
    {
        await using var host = new HealthScenarioHost();
        host.GameApi.Respond = _ =>
        {
            // The rate limiter's 429, which asks to wait until a reset that has come already.
            var response = new HttpResponseMessage(HttpStatusCode.TooManyRequests);
            response.Headers.Add("x-ratelimit-type", "IP_ADDRESS");
            response.Headers.Add("x-ratelimit-reset", TimeProvider.System.GetUtcNow().ToString("O", CultureInfo.InvariantCulture));
            return response;
        };

        // Each call is tried six times (five retries): twelve 429s.
        using var first = await host.GameApiClient().GetAsync(new Uri("my/agent", UriKind.Relative));
        using var second = await host.GameApiClient().GetAsync(new Uri("my/agent", UriKind.Relative));

        await host.EvaluateAsync(TimeProvider.System.GetUtcNow());
        host.Metrics.Received(1).Anomaly("ApiThrottled", "api", true);
    }

    private static ShipModel Drone(string symbol, DateTimeOffset lastSyncedAt)
        => new(symbol, "X1-AB", "X1-AB-A1", "IN_ORBIT", "CRUISE", 100, 100, CargoCapacity: 15, LastSyncedAt: lastSyncedAt, ShipType: "SHIP_MINING_DRONE");

    /// <summary>The API host, with automation and every plan on, the repositories the rules read substituted, and the game API stubbed.</summary>
    private sealed class HealthScenarioHost : IAsyncDisposable
    {
        private readonly WebApplicationFactory<Program> _factory;

        public HealthScenarioHost()
        {
            Api.SettingsRepository.GetAsync<bool>(Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns(true);
            Api.ShipAssignmentRepository.GetAllActiveAsync(Arg.Any<CancellationToken>()).Returns(Array.Empty<ShipAssignmentDto>());
            _factory = Api.WithWebHostBuilder(builder => builder.ConfigureTestServices(services =>
            {
                services.RemoveAll<IAutomationMetrics>();
                services.AddSingleton(Metrics);

                services.RemoveAll<IShipGoalRepository>();
                services.AddScoped(_ => Goals);

                services.RemoveAll<IContractMineralPlanRepository>();
                services.AddScoped(_ => ContractPlans);

                services.RemoveAll<IScoutPlanRepository>();
                services.AddScoped(_ => Substitute.For<IScoutPlanRepository>());

                services.RemoveAll<IProbeDeploymentPlanRepository>();
                services.AddScoped(_ => Substitute.For<IProbeDeploymentPlanRepository>());

                services.AddHttpClient(nameof(ISpaceTradersApiClient)).ConfigurePrimaryHttpMessageHandler(() => new StubHandler(GameApi));
            }));
        }

        public SpaceTradersApiFactory Api { get; } = new();

        public IAutomationMetrics Metrics { get; } = Substitute.For<IAutomationMetrics>();

        public IShipGoalRepository Goals { get; } = Substitute.For<IShipGoalRepository>();

        public IContractMineralPlanRepository ContractPlans { get; } = Substitute.For<IContractMineralPlanRepository>();

        public GameApiStub GameApi { get; } = new();

        public IServiceProvider Services => _factory.Services;

        public Task EvaluateAsync(DateTimeOffset now)
            => Services.GetRequiredService<HealthMonitorService>().EvaluateAsync(now, CancellationToken.None);

        /// <summary>The HttpClient the game API client uses, with all of its handlers.</summary>
        public HttpClient GameApiClient() => Services.GetRequiredService<IHttpClientFactory>().CreateClient(nameof(ISpaceTradersApiClient));

        public void ShipsAre(params ShipModel[] ships) => Api.ShipRepository.GetAllAsync(Arg.Any<CancellationToken>()).Returns(ships);

        public void GoalIs(string ship, ShipGoal goal) => Goals.GetActiveGoalAsync(ship, Arg.Any<CancellationToken>()).Returns(goal);

        public void AssignmentsAre(params ShipAssignmentDto[] assignments) => Api.ShipAssignmentRepository.GetAllActiveAsync(Arg.Any<CancellationToken>()).Returns(assignments);

        public void AgentIs(AgentModel agent) => Api.AgentRepository.GetAsync(Arg.Any<CancellationToken>()).Returns(agent);

        public void ContractPlanIs(ContractMineralPlanStatus status, DateTimeOffset updatedAt)
            => ContractPlans.GetAsync(Arg.Any<CancellationToken>()).Returns(new ContractMineralPlanState
            {
                PlanId = Guid.NewGuid(),
                ContractId = "C-1",
                ShipSymbol = status == ContractMineralPlanStatus.Active ? "SHIP-3" : string.Empty,
                TradeSymbol = "IRON_ORE",
                SourceWaypoint = "X1-AB-A1",
                DestinationWaypoint = "X1-AB-H58",
                UnitsRequired = 42,
                UnitsFulfilled = 0,
                Status = status,
                CreatedAt = updatedAt,
                UpdatedAt = updatedAt,
            });

        public void ContractIs(int fulfilled, DateTimeOffset deadline, bool isFulfilled = false)
            => Api.ContractRepository.FindAsync("C-1", Arg.Any<CancellationToken>()).Returns(new ContractDto(
                "C-1",
                "COSMIC",
                "PROCUREMENT",
                IsAccepted: true,
                IsFulfilled: isFulfilled,
                Expiration: null,
                DeadlineToAccept: null,
                TermsDeadline: deadline,
                DeliverablesJson: JsonSerializer.Serialize(new[] { new ContractDeliverableDto("IRON_ORE", "X1-AB-H58", 42, fulfilled) })));

        public async ValueTask DisposeAsync()
        {
            await _factory.DisposeAsync();
            await Api.DisposeAsync();
        }
    }

    /// <summary>What the stubbed game API answers.</summary>
    private sealed class GameApiStub
    {
        public Func<HttpRequestMessage, HttpResponseMessage> Respond { get; set; } = _ => new HttpResponseMessage(HttpStatusCode.OK);
    }

    private sealed class StubHandler(GameApiStub stub) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
            => Task.FromResult(stub.Respond(request));
    }
}
