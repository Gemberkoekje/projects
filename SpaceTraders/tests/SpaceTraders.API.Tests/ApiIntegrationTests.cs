using System.Net;
using System.Net.Http.Json;
using FluentAssertions;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using NSubstitute;
using ApiDtos = SpaceTraders.API.Dtos;
using SpaceTraders.Application.Automation;
using SpaceTraders.Application.DTOs;
using SpaceTraders.Application.Interfaces;
using SpaceTraders.Application.Interfaces.Repositories;
using SpaceTraders.Application.Orchestration;
using SpaceTraders.Application.Ports;
using SpaceTraders.Domain.Enums;
using SpaceTraders.Infrastructure.SpaceTradersAPI.Clients;

namespace SpaceTraders.API.Tests;

/// <summary>
/// Integration tests for the SpaceTraders.API host using WebApplicationFactory.
/// Uses the "Testing" environment so that <see cref="Program"/> skips database init.
/// All external dependencies (DB repositories, SpaceTraders API) are replaced with fakes.
/// </summary>
public sealed class ApiIntegrationTests : IClassFixture<SpaceTradersApiFactory>, IDisposable
{
    private const string ApiPathBase = "/spacetraders/api";

    private readonly HttpClient _client;
    private readonly HttpClient _clientWithKey;
    private readonly SpaceTradersApiFactory _factory;

    public ApiIntegrationTests(SpaceTradersApiFactory factory)
    {
        _factory = factory;
        _client = factory.CreateClient();
        _clientWithKey = factory.CreateClient();
        _clientWithKey.DefaultRequestHeaders.Add("X-Api-Key", SpaceTradersApiFactory.TestApiKey);
    }

    public void Dispose()
    {
        _client.Dispose();
        _clientWithKey.Dispose();
    }

    // ── Health checks – no auth required ────────────────────────────────────

    [Fact]
    public async Task HealthLive_ReturnsOkWithoutApiKey()
    {
        using var response = await _client.GetAsync("/health/live");
        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task HealthReady_DoesNotRequireApiKey()
    {
        // Health probes must never return 401 – whether they return 200 or 503
        // depends on DB connectivity which is not available in tests.
        using var response = await _client.GetAsync("/health/ready");
        response.StatusCode.Should().NotBe(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task HealthStartup_DoesNotRequireApiKey()
    {
        using var response = await _client.GetAsync("/health/startup");
        response.StatusCode.Should().NotBe(HttpStatusCode.Unauthorized);
    }

    // ── API key auth ─────────────────────────────────────────────────────────

    [Fact]
    public async Task StatusAgent_WithoutApiKey_Returns401()
    {
        using var response = await _client.GetAsync("/status/agent");
        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    // ── Status endpoints ─────────────────────────────────────────────────────

    [Fact]
    public async Task StatusAgent_WithValidApiKey_Returns200OrNotFound()
    {
        using var response = await _clientWithKey.GetAsync("/status/agent");
        response.StatusCode.Should().BeOneOf(HttpStatusCode.OK, HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task StatusShips_WithValidApiKey_Returns200()
    {
        using var response = await _clientWithKey.GetAsync("/status/ships");
        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task StatusContracts_WithValidApiKey_Returns200()
    {
        using var response = await _clientWithKey.GetAsync("/status/contracts");
        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task StatusSystemAlerts_WithValidApiKey_Returns200()
    {
        _factory.SettingsRepository.GetAsync<bool>(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(false);
        _factory.SettingsRepository.GetRawAsync("Runtime.Reset.Next", Arg.Any<CancellationToken>())
            .Returns(DateTimeOffset.UtcNow.AddHours(1).ToString("O"));

        using var response = await _clientWithKey.GetAsync("/status/system-alerts");
        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    // ── Settings endpoints ────────────────────────────────────────────────────

    [Fact]
    public async Task Settings_Get_WithValidApiKey_Returns200()
    {
        using var response = await _clientWithKey.GetAsync("/settings/");
        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task Settings_Put_WithValidApiKey_Returns200()
    {
        using var response = await _clientWithKey.PutAsJsonAsync("/settings/Automation.Enabled", new { Value = "false" });
        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    // ── Control endpoints ─────────────────────────────────────────────────────

    [Fact]
    public async Task Control_AutomationEnable_WithValidApiKey_Returns200()
    {
        using var response = await _clientWithKey.PostAsync("/control/automation/enable", null);
        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task Control_AutomationDisable_WithValidApiKey_Returns200()
    {
        using var response = await _clientWithKey.PostAsync("/control/automation/disable", null);
        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    // ── Smoke tests for path-based routing assumptions ────────────────────────

    [Fact]
    public async Task HealthLive_WithPathBase_ReturnsOkWithoutApiKey()
    {
        using var response = await _client.GetAsync($"{ApiPathBase}/health/live");
        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task HealthReady_WithPathBase_DoesNotRequireApiKey()
    {
        using var response = await _client.GetAsync($"{ApiPathBase}/health/ready");
        response.StatusCode.Should().NotBe(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task HealthStartup_WithPathBase_DoesNotRequireApiKey()
    {
        using var response = await _client.GetAsync($"{ApiPathBase}/health/startup");
        response.StatusCode.Should().NotBe(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task StatusAgent_WithPathBaseAndValidApiKey_Returns200OrNotFound()
    {
        using var response = await _clientWithKey.GetAsync($"{ApiPathBase}/status/agent");
        response.StatusCode.Should().BeOneOf(HttpStatusCode.OK, HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task StatusShips_WithPathBaseAndValidApiKey_Returns200()
    {
        using var response = await _clientWithKey.GetAsync($"{ApiPathBase}/status/ships");
        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task StatusContracts_WithPathBaseAndValidApiKey_Returns200()
    {
        using var response = await _clientWithKey.GetAsync($"{ApiPathBase}/status/contracts");
        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task StatusSystemAlerts_WithPathBaseAndValidApiKey_Returns200()
    {
        _factory.SettingsRepository.GetAsync<bool>(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(false);

        using var response = await _clientWithKey.GetAsync($"{ApiPathBase}/status/system-alerts");
        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task Metrics_WithoutApiKeyWithPathBase_ReturnsUnauthorizedWhenApiKeyConfigured()
    {
        using var response = await _client.GetAsync($"{ApiPathBase}/metrics");
        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    // ── Run KPIs endpoint ────────────────────────────────────────────────────

    [Fact]
    public async Task RunKpis_WithValidRunId_Returns200()
    {
        var runId = Guid.NewGuid();
        var runRepoMock = _factory.Services.GetRequiredService<IRunRepository>();
        runRepoMock.GetByIdAsync(runId, Arg.Any<CancellationToken>())
            .Returns(new RunDetailDto(
                runId, "Test Run", "TRADE",
                DateTimeOffset.UtcNow.AddHours(-2), DateTimeOffset.UtcNow,
                100_000L, 300_000L, null));

        using var response = await _clientWithKey.GetAsync($"{ApiPathBase}/runs/{runId}/kpis");
        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task RunKpis_WithUnknownRunId_Returns404()
    {
        var runId = Guid.NewGuid();
        var runRepoMock = _factory.Services.GetRequiredService<IRunRepository>();
        runRepoMock.GetByIdAsync(runId, Arg.Any<CancellationToken>())
            .Returns((RunDetailDto?)null);

        using var response = await _clientWithKey.GetAsync($"{ApiPathBase}/runs/{runId}/kpis");
        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    // ── Universe endpoints ───────────────────────────────────────────────────

    [Fact]
    public async Task UniverseSystems_WithValidApiKey_Returns200WithIsVisitedFlag()
    {
        _factory.SystemRepository.GetAllAsync(Arg.Any<CancellationToken>())
            .Returns(new[]
            {
                new SystemCacheModel("X1-SN", "X1", "RED_STAR", 10, -20, DateTimeOffset.UtcNow),
                new SystemCacheModel("X1-SF", "X1", "BLUE_STAR", -5, 30, DateTimeOffset.UtcNow),
            });
        _factory.WaypointRepository.GetVisitedSystemSymbolsAsync(Arg.Any<CancellationToken>())
            .Returns(new[] { "X1-SN" });

        using var response = await _clientWithKey.GetAsync($"{ApiPathBase}/universe/systems");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await response.Content.ReadAsStringAsync();
        body.Should().Contain("X1-SN");
        body.Should().Contain("isVisited");
    }

    [Fact]
    public async Task UniverseJumpConnections_WithValidApiKey_Returns200()
    {
        _factory.SettingsRepository.GetByKeyPrefixAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(new[]
            {
                ("Navigation.JumpGateConnections.X1-SN-12345",
                 """{"WaypointSymbol":"X1-SN-12345","ConnectedSystems":["X1-SF"],"ObservedAt":"2024-01-01T00:00:00Z"}"""),
            });

        using var response = await _clientWithKey.GetAsync($"{ApiPathBase}/universe/jump-connections");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await response.Content.ReadAsStringAsync();
        body.Should().Contain("X1-SN");
        body.Should().Contain("X1-SF");
    }

    // ── Phase 5: new endpoints ─────────────────────────────────────────────────

    [Fact]
    public async Task TradeRoutes_WithValidApiKey_Returns200()
    {
        using var response = await _clientWithKey.GetAsync($"{ApiPathBase}/finance/trade-routes");
        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task ContractRoi_WithUnknownContractId_Returns404()
    {
        using var response = await _clientWithKey.GetAsync($"{ApiPathBase}/contracts/UNKNOWN/roi");
        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task MarketFreshness_WithValidApiKey_Returns200()
    {
        using var response = await _clientWithKey.GetAsync($"{ApiPathBase}/markets/freshness");
        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task Anomalies_WithValidApiKey_Returns200()
    {
        using var response = await _clientWithKey.GetAsync($"{ApiPathBase}/status/anomalies");
        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task TopTradeRoutes_WithValidApiKey_Returns200()
    {
        using var response = await _clientWithKey.GetAsync($"{ApiPathBase}/status/top-trade-routes");
        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task MiningOpportunities_WithValidApiKey_ReturnsPersistedQueue()
    {
        _factory.PlanRepository.GetAsync<MiningAutomationPlanState>(PlanTypes.MiningAutomation, Arg.Any<CancellationToken>())
            .Returns(new MiningAutomationPlanState
            {
                PlanId = Guid.NewGuid(),
                CreatedAt = DateTimeOffset.UtcNow.AddMinutes(-5),
                UpdatedAt = DateTimeOffset.UtcNow,
                Opportunities =
                [
                    new MiningAutomationOpportunityState
                    {
                        OpportunityKey = "X1-AB-MKT1|IRON_ORE",
                        TradeSymbol = "IRON_ORE",
                        SellWaypointSymbol = "X1-AB-MKT1",
                        Status = MarketAutomationOpportunityStatus.Pending,
                        AssignedShipSymbol = null,
                        FirstObservedAt = DateTimeOffset.UtcNow.AddMinutes(-5),
                        LastObservedAt = DateTimeOffset.UtcNow,
                        StopReason = "No idle mining drone available and purchase unavailable.",
                    }
                ],
            });

        using var response = await _clientWithKey.GetAsync($"{ApiPathBase}/status/mining-opportunities");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await response.Content.ReadAsStringAsync();
        body.Should().Contain("IRON_ORE");
        body.Should().Contain("X1-AB-MKT1");
        body.Should().Contain("Pending");
    }

    [Fact]
    public async Task MiningOpportunities_WithValidApiKey_ReflectsPendingToAssignedTransitionAcrossCalls()
    {
        var planId = Guid.NewGuid();
        var createdAt = DateTimeOffset.UtcNow.AddMinutes(-5);
        var updatedAt = DateTimeOffset.UtcNow;

        _factory.PlanRepository.GetAsync<MiningAutomationPlanState>(PlanTypes.MiningAutomation, Arg.Any<CancellationToken>())
            .Returns(
                new MiningAutomationPlanState
                {
                    PlanId = planId,
                    CreatedAt = createdAt,
                    UpdatedAt = updatedAt,
                    Opportunities =
                    [
                        new MiningAutomationOpportunityState
                        {
                            OpportunityKey = "X1-AB-MKT1|IRON_ORE",
                            TradeSymbol = "IRON_ORE",
                            SellWaypointSymbol = "X1-AB-MKT1",
                            Status = MarketAutomationOpportunityStatus.Pending,
                            AssignedShipSymbol = null,
                            FirstObservedAt = createdAt,
                            LastObservedAt = updatedAt,
                            StopReason = "No idle mining drone available and purchase unavailable.",
                        }
                    ],
                },
                new MiningAutomationPlanState
                {
                    PlanId = planId,
                    CreatedAt = createdAt,
                    UpdatedAt = updatedAt.AddSeconds(10),
                    Opportunities =
                    [
                        new MiningAutomationOpportunityState
                        {
                            OpportunityKey = "X1-AB-MKT1|IRON_ORE",
                            TradeSymbol = "IRON_ORE",
                            SellWaypointSymbol = "X1-AB-MKT1",
                            Status = MarketAutomationOpportunityStatus.Assigned,
                            AssignedShipSymbol = "MINER-1",
                            FirstObservedAt = createdAt,
                            LastObservedAt = updatedAt.AddSeconds(10),
                            StopReason = null,
                        }
                    ],
                });

        using var responsePending = await _clientWithKey.GetAsync($"{ApiPathBase}/status/mining-opportunities");
        responsePending.StatusCode.Should().Be(HttpStatusCode.OK);
        var pendingBody = await responsePending.Content.ReadAsStringAsync();
        pendingBody.Should().Contain("Pending");
        pendingBody.Should().NotContain("MINER-1");

        using var responseAssigned = await _clientWithKey.GetAsync($"{ApiPathBase}/status/mining-opportunities");
        responseAssigned.StatusCode.Should().Be(HttpStatusCode.OK);
        var assignedBody = await responseAssigned.Content.ReadAsStringAsync();
        assignedBody.Should().Contain("Assigned");
        assignedBody.Should().Contain("MINER-1");
    }

    // ── Phase 16a: Fleet status endpoints ────────────────────────────────────

    [Fact]
    public async Task FleetGoalChains_WithValidApiKey_Returns200()
    {
        using var response = await _clientWithKey.GetAsync($"{ApiPathBase}/fleet/goal-chains");
        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task FleetAssignments_WithValidApiKey_Returns200()
    {
        using var response = await _clientWithKey.GetAsync($"{ApiPathBase}/fleet/assignments");
        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task FleetActivity_WithValidApiKey_Returns200()
    {
        using var response = await _clientWithKey.GetAsync($"{ApiPathBase}/fleet/activity");
        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task FleetActivityByShip_WithUnknownSymbol_Returns404()
    {
        using var response = await _clientWithKey.GetAsync($"{ApiPathBase}/fleet/activity/UNKNOWN-SHIP");
        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task FleetActivityByShip_WithKnownSymbol_Returns200()
    {
        _factory.FleetStatusQueryService
            .GetShipActivityAsync("X1-TEST-1", Arg.Any<CancellationToken>())
            .Returns(new ShipActivitySnapshot(
                "X1-TEST-1",
                Domain.Enums.ShipLocalStatus.Docked,
                "Docked at X1-TEST-A1",
                0, 40, 400, 400, false,
                CurrentWaypoint: "X1-TEST-A1"));

        using var response = await _clientWithKey.GetAsync($"{ApiPathBase}/fleet/activity/X1-TEST-1");
        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    // ── Phase 16d: Fleet status endpoint content tests ───────────────────────

    [Fact]
    public async Task GetGoalChains_ReturnsCorrectChainCountAndResourceNeeds()
    {
        var goalId = Guid.NewGuid();
        _factory.FleetStatusQueryService
            .GetGoalChainsAsync(Arg.Any<CancellationToken>())
            .Returns(new[]
            {
                new OrchestratorGoalChain(
                    goalId,
                    FleetGoalKind.Construction,
                    1,
                    "Supply Jump Gate at X1-TD7-JG",
                    new[]
                    {
                        new ResourceNeedEntry("BAUXITE", 100, 30, "needed for Jump Gate", new[] { "X1-TD7-1" }),
                    }),
            });

        using var response = await _clientWithKey.GetAsync($"{ApiPathBase}/fleet/goal-chains");
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var chains = await response.Content.ReadFromJsonAsync<List<ApiDtos.FleetGoalChainDto>>();
        chains.Should().HaveCount(1);

        var chain = chains![0];
        chain.FleetGoalId.Should().Be(goalId);
        chain.FleetGoalKind.Should().Be("Construction");
        chain.Priority.Should().Be(1);
        chain.FleetGoalDescription.Should().Be("Supply Jump Gate at X1-TD7-JG");
        chain.ResourceNeeds.Should().HaveCount(1);

        var need = chain.ResourceNeeds[0];
        need.TradeSymbol.Should().Be("BAUXITE");
        need.UnitsNeeded.Should().Be(100);
        need.UnitsDelivered.Should().Be(30);
        need.PurposeDescription.Should().Be("needed for Jump Gate");
        need.AssignedShips.Should().ContainSingle().Which.Should().Be("X1-TD7-1");
    }

    [Fact]
    public async Task GetAssignments_MapsAllActiveShipGoalsToDto()
    {
        var fleetGoalId = Guid.NewGuid();
        var assignedAt = new DateTimeOffset(2024, 1, 1, 12, 0, 0, TimeSpan.Zero);
        _factory.FleetStatusQueryService
            .GetAssignmentsAsync(Arg.Any<CancellationToken>())
            .Returns(new[]
            {
                new ShipAssignmentSnapshot(
                    "X1-TD7-2",
                    ShipGoalKind.MineResource,
                    "Mining BAUXITE at X1-TD7-A3",
                    assignedAt,
                    SourceWaypoint: "X1-TD7-A3",
                    FleetGoalId: fleetGoalId,
                    FleetGoalDescription: "Supply Jump Gate at X1-TD7-JG"),
                new ShipAssignmentSnapshot(
                    "X1-TD7-3",
                    ShipGoalKind.Idle,
                    "Idle",
                    null),
            });

        using var response = await _clientWithKey.GetAsync($"{ApiPathBase}/fleet/assignments");
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var assignments = await response.Content.ReadFromJsonAsync<List<ApiDtos.ShipAssignmentDto>>();
        assignments.Should().HaveCount(2);

        var miner = assignments!.Single(a => a.ShipSymbol == "X1-TD7-2");
        miner.GoalKind.Should().Be("MineResource");
        miner.GoalDescription.Should().Be("Mining BAUXITE at X1-TD7-A3");
        miner.SourceWaypoint.Should().Be("X1-TD7-A3");
        miner.FleetGoalId.Should().Be(fleetGoalId);
        miner.FleetGoalDescription.Should().Be("Supply Jump Gate at X1-TD7-JG");
        miner.AssignedAt.Should().Be(assignedAt);

        var idleShip = assignments.Single(a => a.ShipSymbol == "X1-TD7-3");
        idleShip.GoalKind.Should().Be("Idle");
        idleShip.AssignedAt.Should().BeNull();
    }

    [Fact]
    public async Task GetActivity_ReturnsSnapshotPerShipWithCorrectActivityDescription()
    {
        var arrival = new DateTimeOffset(2024, 6, 1, 14, 23, 0, TimeSpan.Zero);
        _factory.FleetStatusQueryService
            .GetShipActivitiesAsync(Arg.Any<CancellationToken>())
            .Returns(new[]
            {
                new ShipActivitySnapshot(
                    "X1-TD7-4",
                    ShipLocalStatus.InTransit,
                    "Moving to X1-TD7-A3 (arrives 14:23 UTC)",
                    10, 40, 300, 400, false,
                    DestinationWaypoint: "X1-TD7-A3",
                    EstimatedArrival: arrival),
                new ShipActivitySnapshot(
                    "X1-TD7-5",
                    ShipLocalStatus.Docked,
                    "Docked at X1-TEST-A1",
                    0, 40, 400, 400, false,
                    CurrentWaypoint: "X1-TEST-A1"),
            });

        using var response = await _clientWithKey.GetAsync($"{ApiPathBase}/fleet/activity");
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var activities = await response.Content.ReadFromJsonAsync<List<ApiDtos.ShipActivityDto>>();
        activities.Should().HaveCount(2);

        var inTransit = activities!.Single(a => a.ShipSymbol == "X1-TD7-4");
        inTransit.LocalStatus.Should().Be("InTransit");
        inTransit.ActivityDescription.Should().Be("Moving to X1-TD7-A3 (arrives 14:23 UTC)");
        inTransit.DestinationWaypoint.Should().Be("X1-TD7-A3");
        inTransit.EstimatedArrival.Should().Be(arrival);
        inTransit.CargoUsed.Should().Be(10);
        inTransit.FuelCurrent.Should().Be(300);

        var docked = activities.Single(a => a.ShipSymbol == "X1-TD7-5");
        docked.LocalStatus.Should().Be("Docked");
        docked.ActivityDescription.Should().Be("Docked at X1-TEST-A1");
        docked.CurrentWaypoint.Should().Be("X1-TEST-A1");
    }

    [Fact]
    public async Task GetGoalChains_ResponseHasCacheControlHeader()
    {
        using var response = await _clientWithKey.GetAsync($"{ApiPathBase}/fleet/goal-chains");
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        response.Headers.CacheControl.Should().NotBeNull();
        response.Headers.CacheControl!.MaxAge.Should().Be(TimeSpan.FromSeconds(5));
    }

    [Fact]
    public async Task GetActivityByShip_WithUnknownSymbol_HasNoCacheControlHeader()
    {
        using var response = await _clientWithKey.GetAsync($"{ApiPathBase}/fleet/activity/NO-SUCH-SHIP");
        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
        // 404 responses must not carry a Cache-Control header (per Phase 16c spec)
        response.Headers.CacheControl.Should().BeNull();
    }

    // ── Phase 17f: Ship goal history endpoint ─────────────────────────────────

    [Fact]
    public async Task FleetGoalHistory_WithUnknownSymbol_Returns404()
    {
        using var response = await _clientWithKey.GetAsync($"{ApiPathBase}/fleet/activity/UNKNOWN-SHIP/history");
        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task FleetGoalHistory_WithKnownSymbol_ReturnsCorrectEntries()
    {
        var now = DateTimeOffset.UtcNow;
        var goalId = Guid.NewGuid();
        var entryId = Guid.NewGuid();
        _factory.FleetStatusQueryService
            .GetShipGoalHistoryAsync("X1-HIST-1", 20, Arg.Any<CancellationToken>())
            .Returns(new[]
            {
                new Application.DTOs.ShipGoalHistoryEntry
                {
                    Id = entryId,
                    ShipSymbol = "X1-HIST-1",
                    GoalKind = "MineResource",
                    GoalId = goalId,
                    Outcome = "Completed",
                    Reason = null,
                    StartedAt = now.AddMinutes(-5),
                    EndedAt = now,
                },
            });

        using var response = await _clientWithKey.GetAsync($"{ApiPathBase}/fleet/activity/X1-HIST-1/history");
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var entries = await response.Content.ReadFromJsonAsync<List<ApiDtos.ShipGoalHistoryDto>>();
        entries.Should().HaveCount(1);
        entries![0].Id.Should().Be(entryId);
        entries[0].GoalKind.Should().Be("MineResource");
        entries[0].Outcome.Should().Be("Completed");
        entries[0].Reason.Should().BeNull();
    }

    [Fact]
    public async Task FleetGoalHistory_WithKnownSymbol_LimitQueryParam_PassedThrough()
    {
        _factory.FleetStatusQueryService
            .GetShipGoalHistoryAsync("X1-HIST-2", 5, Arg.Any<CancellationToken>())
            .Returns(Array.Empty<Application.DTOs.ShipGoalHistoryEntry>());

        using var response = await _clientWithKey.GetAsync($"{ApiPathBase}/fleet/activity/X1-HIST-2/history?limit=5");
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        await _factory.FleetStatusQueryService.Received(1).GetShipGoalHistoryAsync("X1-HIST-2", 5, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task FleetGoalChains_WithPathBaseAndValidApiKey_ReturnsMappedChains()
    {
        var fleetGoalId = Guid.NewGuid();
        _factory.FleetStatusQueryService
            .GetGoalChainsAsync(Arg.Any<CancellationToken>())
            .Returns(new[]
            {
                new OrchestratorGoalChain(
                    FleetGoalId: fleetGoalId,
                    FleetGoalKind: FleetGoalKind.Contract,
                    Priority: 1,
                    FleetGoalDescription: "Contract C-1",
                    ResourceNeeds:
                    [
                        new ResourceNeedEntry(
                            TradeSymbol: "COPPER_ORE",
                            UnitsNeeded: 40,
                            UnitsDelivered: 10,
                            PurposeDescription: "needed for contract C-1",
                            AssignedShips: ["SPECTER-DEBUG-1"]),
                    ]),
            });

        using var response = await _clientWithKey.GetAsync($"{ApiPathBase}/fleet/goal-chains");
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var chains = await response.Content.ReadFromJsonAsync<List<ApiDtos.FleetGoalChainDto>>();
        chains.Should().HaveCount(1);
        chains![0].FleetGoalId.Should().Be(fleetGoalId);
        chains[0].FleetGoalKind.Should().Be("Contract");
        chains[0].FleetGoalDescription.Should().Be("Contract C-1");
        chains[0].ResourceNeeds.Should().HaveCount(1);
        chains[0].ResourceNeeds[0].TradeSymbol.Should().Be("COPPER_ORE");
        chains[0].ResourceNeeds[0].AssignedShips.Should().ContainSingle("SPECTER-DEBUG-1");
    }
}

/// <summary>
/// Custom WebApplicationFactory that:
/// <list type="bullet">
///   <item>Sets ASPNETCORE_ENVIRONMENT to "Testing" so <see cref="Program"/> skips
///         database initialisation (EnsureCreated / seed).</item>
///   <item>Replaces all repository interfaces with NSubstitute mocks.</item>
///   <item>Replaces the SpaceTraders API client/port with fakes.</item>
///   <item>Suppresses only the application-level background services,
///         leaving Wolverine's own infrastructure services intact.</item>
/// </list>
/// </summary>
public sealed class SpaceTradersApiFactory : WebApplicationFactory<Program>
{
    public const string TestApiKey = "test-key-12345";

    // Shared mocks – configure return values in tests as needed
    public ISettingsRepository SettingsRepository { get; } = Substitute.For<ISettingsRepository>();

    public IAgentRepository AgentRepository { get; } = Substitute.For<IAgentRepository>();

    public IShipRepository ShipRepository { get; } = Substitute.For<IShipRepository>();

    public IContractRepository ContractRepository { get; } = Substitute.For<IContractRepository>();

    public IActivityLogRepository ActivityLogRepository { get; } = Substitute.For<IActivityLogRepository>();

    public ITradeOpportunityRepository TradeOpportunityRepository { get; } = Substitute.For<ITradeOpportunityRepository>();

    public IShipAssignmentRepository ShipAssignmentRepository { get; } = Substitute.For<IShipAssignmentRepository>();

    public IRateLimitStatus RateLimitStatus { get; } = Substitute.For<IRateLimitStatus>();

    public ILeaderElection LeaderElection { get; } = Substitute.For<ILeaderElection>();

    public ICreditHistoryService CreditHistory { get; } = Substitute.For<ICreditHistoryService>();

    public ILedgerRepository LedgerRepository { get; } = Substitute.For<ILedgerRepository>();

    public IShipTaskRecordRepository ShipTaskRecordRepository { get; } = Substitute.For<IShipTaskRecordRepository>();

    public IApiEndpointUsageRecorder ApiEndpointUsageRecorder { get; } = Substitute.For<IApiEndpointUsageRecorder>();

    public ISystemRepository SystemRepository { get; } = Substitute.For<ISystemRepository>();

    public IWaypointRepository WaypointRepository { get; } = Substitute.For<IWaypointRepository>();

    public IAgentCreditsSampleRepository AgentCreditsSampleRepository { get; } = Substitute.For<IAgentCreditsSampleRepository>();

    public IMarketRepository MarketRepository { get; } = Substitute.For<IMarketRepository>();

    public IPlanRepository PlanRepository { get; } = Substitute.For<IPlanRepository>();

    public IFleetStatusQueryService FleetStatusQueryService { get; } = Substitute.For<IFleetStatusQueryService>();

    public SpaceTradersApiFactory()
    {
        // Default stub responses
        AgentRepository.GetAsync(default).ReturnsForAnyArgs((Application.Ports.AgentModel?)null);
        ShipRepository.GetAllAsync(default).ReturnsForAnyArgs(Array.Empty<Application.Ports.ShipModel>());
        ContractRepository.GetActiveAsync(default).ReturnsForAnyArgs(Array.Empty<ContractDto>());
        ContractRepository.FindAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns((ContractDto?)null);
        ActivityLogRepository.GetPagedAsync(1, 50, null, default).ReturnsForAnyArgs(Array.Empty<ActivityLogDto>());
        SettingsRepository.GetAllAsync(default).ReturnsForAnyArgs(Array.Empty<(string, string, string, string)>());
        SettingsRepository.GetByKeyPrefixAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(Array.Empty<(string, string)>());
        TradeOpportunityRepository.GetBestRouteForCapacityAsync(default, default, default, default)
            .ReturnsForAnyArgs((TradeOpportunityDto?)null);
        TradeOpportunityRepository.GetTopRoutesAsync(Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns(Array.Empty<TradeOpportunityDto>());
        LeaderElection.IsLeader.Returns(true);
        CreditHistory.GetHistory().Returns(Array.Empty<(DateTimeOffset, long)>());
        LedgerRepository.GetSummaryAsync(Arg.Any<Guid?>(), Arg.Any<CancellationToken>())
            .Returns(Array.Empty<Application.Interfaces.Repositories.LedgerSummaryDto>());
        LedgerRepository.GetDistinctShipCountAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>())
            .Returns(0);
        LedgerRepository.GetRangeAsync(
                Arg.Any<DateTimeOffset?>(), Arg.Any<DateTimeOffset?>(),
                Arg.Any<string?>(), Arg.Any<LedgerCategory?>(),
                Arg.Any<Guid?>(), Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns(Array.Empty<Application.Interfaces.Repositories.LedgerEntryDto>());
        ShipTaskRecordRepository.GetAllInTimeWindowAsync(Arg.Any<DateTimeOffset>(), Arg.Any<DateTimeOffset>(), Arg.Any<CancellationToken>())
            .Returns(Array.Empty<Application.Interfaces.Repositories.ShipTaskRecordDto>());
        ApiEndpointUsageRecorder.GetTotalCallsAsync(Arg.Any<CancellationToken>())
            .Returns(0L);
        SystemRepository.GetAllAsync(Arg.Any<CancellationToken>())
            .Returns(Array.Empty<SystemCacheModel>());
        WaypointRepository.GetVisitedSystemSymbolsAsync(Arg.Any<CancellationToken>())
            .Returns(Array.Empty<string>());
        AgentCreditsSampleRepository.GetRangeAsync(Arg.Any<DateTimeOffset>(), Arg.Any<DateTimeOffset>(), Arg.Any<CancellationToken>())
            .Returns(Array.Empty<Application.Interfaces.Repositories.CreditsSampleDto>());
        MarketRepository.GetAllSnapshotsAsync(Arg.Any<CancellationToken>())
            .Returns(Array.Empty<Application.Interfaces.MarketSnapshot>());
        MarketRepository.GetAllFreshnessAsync(Arg.Any<CancellationToken>())
            .Returns(Array.Empty<Application.Interfaces.Repositories.MarketFreshnessRecord>());
        PlanRepository.GetAsync<MiningAutomationPlanState>(PlanTypes.MiningAutomation, Arg.Any<CancellationToken>())
            .Returns((MiningAutomationPlanState?)null);
        FleetStatusQueryService.GetGoalChainsAsync(Arg.Any<CancellationToken>())
            .Returns(Array.Empty<OrchestratorGoalChain>());
        FleetStatusQueryService.GetAssignmentsAsync(Arg.Any<CancellationToken>())
            .Returns(Array.Empty<ShipAssignmentSnapshot>());
        FleetStatusQueryService.GetShipActivitiesAsync(Arg.Any<CancellationToken>())
            .Returns(Array.Empty<ShipActivitySnapshot>());
        FleetStatusQueryService.GetShipActivityAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns((ShipActivitySnapshot?)null);
        FleetStatusQueryService.GetShipGoalHistoryAsync(Arg.Any<string>(), Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns((IReadOnlyList<Application.DTOs.ShipGoalHistoryEntry>?)null);
    }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Testing");

        builder.UseSetting("ConnectionStrings:DefaultConnection", "Host=localhost;Database=test;Username=test;Password=test");
        builder.UseSetting("SPACETRADERS_INTERNAL_API_KEY", TestApiKey);

        builder.ConfigureTestServices(services =>
        {
            // ── Replace repository implementations with mocks ─────────────────
            services.RemoveAll<IAgentRepository>();
            services.AddScoped(_ => AgentRepository);

            services.RemoveAll<IShipRepository>();
            services.AddScoped(_ => ShipRepository);

            services.RemoveAll<IContractRepository>();
            services.AddScoped(_ => ContractRepository);

            services.RemoveAll<IActivityLogRepository>();
            services.AddScoped(_ => ActivityLogRepository);

            services.RemoveAll<ISettingsRepository>();
            services.AddScoped(_ => SettingsRepository);

            services.RemoveAll<ITradeOpportunityRepository>();
            services.AddScoped(_ => TradeOpportunityRepository);

            services.RemoveAll<IShipAssignmentRepository>();
            services.AddScoped(_ => ShipAssignmentRepository);

            services.RemoveAll<IRateLimitStatus>();
            services.AddSingleton(RateLimitStatus);

            services.RemoveAll<ILeaderElection>();
            services.AddSingleton(LeaderElection);

            services.RemoveAll<ICreditHistoryService>();
            services.AddSingleton(CreditHistory);

            services.RemoveAll<ILeaderLeaseRepository>();
            services.AddScoped(_ => Substitute.For<ILeaderLeaseRepository>());

            services.RemoveAll<ILedgerRepository>();
            services.AddScoped(_ => LedgerRepository);

            services.RemoveAll<IShipTaskRecordRepository>();
            services.AddScoped(_ => ShipTaskRecordRepository);

            services.RemoveAll<IApiEndpointUsageRecorder>();
            services.AddScoped(_ => ApiEndpointUsageRecorder);

            // ── Run lifecycle (Phase 0b) ──────────────────────────────────────
            // RunLifecycleService is a hosted service that tries to access the DB
            // at startup. Provide a properly-stubbed IRunRepository so it can
            // initialise without a real database connection.
            var runRepoMock = Substitute.For<Application.Interfaces.Repositories.IRunRepository>();
            runRepoMock
                .GetPendingScheduledRunsAsync(Arg.Any<DateTimeOffset>(), Arg.Any<CancellationToken>())
                .Returns(Array.Empty<Application.Ports.PendingScheduledRunInfo>());
            var testRunId = Guid.NewGuid();
            runRepoMock
                .GetActiveRunAsync(Arg.Any<CancellationToken>())
                .Returns(new Application.Ports.ActiveRunInfo(testRunId, "Test Run", "default", DateTimeOffset.UtcNow, 100_000L));
            services.RemoveAll<Application.Interfaces.Repositories.IRunRepository>();
            services.AddScoped(_ => runRepoMock);

            // ── Universe repositories ─────────────────────────────────────────
            services.RemoveAll<ISystemRepository>();
            services.AddScoped(_ => SystemRepository);

            services.RemoveAll<IWaypointRepository>();
            services.AddScoped(_ => WaypointRepository);

            services.RemoveAll<IAgentCreditsSampleRepository>();
            services.AddScoped(_ => AgentCreditsSampleRepository);

            services.RemoveAll<IMarketRepository>();
            services.AddScoped(_ => MarketRepository);

            services.RemoveAll<IPlanRepository>();
            services.AddScoped(_ => PlanRepository);

            // ── Fleet status (Phase 16a) ─────────────────────────────────────
            services.RemoveAll<IFleetStatusQueryService>();
            services.AddScoped(_ => FleetStatusQueryService);

            // ── SpaceTraders API client / port ────────────────────────────────
            services.RemoveAll<ISpaceTradersApiClient>();
            services.AddSingleton(Substitute.For<ISpaceTradersApiClient>());

            services.RemoveAll<ISpaceTradersPort>();
            services.AddScoped(_ => Substitute.For<ISpaceTradersPort>());

            services.RemoveAll<IAgentTokenProvider>();
            services.AddSingleton(Substitute.For<IAgentTokenProvider>());

            // ── Suppress only application-level background services ───────────
            // (Wolverine's own IHostedService is kept so the bus starts correctly)
            var appServices = services
                .Where(d =>
                    d.ServiceType == typeof(IHostedService) &&
                    d.ImplementationType?.Namespace?.StartsWith("SpaceTraders", StringComparison.Ordinal) == true)
                .ToList();

            foreach (var svc in appServices)
                services.Remove(svc);
        });
    }
}
