using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using SpaceTraders.Application.Automation;
using SpaceTraders.Application.Commands.Ships;
using SpaceTraders.Application.DTOs;
using SpaceTraders.Application.Interfaces.Repositories;
using SpaceTraders.Application.Orchestration;
using SpaceTraders.Application.Ports;
using Wolverine;

namespace SpaceTraders.Application.Tests.Automation;

public sealed class ProbeDeploymentPlanServiceTests
{
    private static readonly DateTimeOffset Now = new(2025, 1, 1, 0, 0, 0, TimeSpan.Zero);

    private readonly IProbeDeploymentPlanRepository _plans = Substitute.For<IProbeDeploymentPlanRepository>();
    private readonly IShipRepository _ships = Substitute.For<IShipRepository>();
    private readonly IWaypointRepository _waypoints = Substitute.For<IWaypointRepository>();
    private readonly IAgentRepository _agents = Substitute.For<IAgentRepository>();
    private readonly IShipyardRepository _shipyards = Substitute.For<IShipyardRepository>();
    private readonly IBudgetPolicy _budget = Substitute.For<IBudgetPolicy>();
    private readonly ISpaceTradersPort _port = Substitute.For<ISpaceTradersPort>();
    private readonly IMessageBus _bus = Substitute.For<IMessageBus>();

    private ProbeDeploymentPlanService CreateService() =>
        new(
            _plans,
            _ships,
            _waypoints,
            _agents,
            _shipyards,
            _budget,
            _port,
            _bus,
            NullLogger<ProbeDeploymentPlanService>.Instance);

    private static AgentModel Agent(long credits = 300_000, string hq = "X1-AB-HQ") =>
        new("AGENT", null, hq, credits, "FACTION", 1);

    private static WaypointCacheModel Waypoint(string symbol, bool hasMarket = false, bool hasShipyard = false) =>
        new(symbol, "X1-AB", "MOON", 0, 0, hasMarket, hasShipyard, Now);

    private static ShipModel Probe(string symbol, string waypointSymbol = "X1-AB-HQ", bool inTransit = false) =>
        new(symbol, "X1-AB", waypointSymbol, inTransit ? "IN_TRANSIT" : "DOCKED", "DRIFT",
            0, 0, inTransit ? Now.AddMinutes(10) : null, ShipType: "SHIP_PROBE");

    private static ShipyardWaypointDto Shipyard(string waypointSymbol, long probePrice = 23_000) =>
        new()
        {
            WaypointSymbol = waypointSymbol,
            SystemSymbol = "X1-AB",
            ShipTypes = ["SHIP_PROBE"],
            Ships =
            [
                new ShipyardShipDto
                {
                    Type = "SHIP_PROBE",
                    PurchasePrice = probePrice,
                }
            ]
        };

    private static ProbeDeploymentPlanState ActivePlan(
        List<string> targets,
        List<string> deployed,
        List<string> shipyardTargets) =>
        new()
        {
            PlanId = Guid.NewGuid(),
            SystemSymbol = "X1-AB",
            TargetWaypointSymbols = targets,
            ShipyardWaypointSymbols = shipyardTargets,
            DeployedWaypointSymbols = deployed,
            Status = ProbeDeploymentPlanStatus.Active,
            CreatedAt = Now,
            UpdatedAt = Now,
        };

    private static PurchaseShipActionResult PurchaseResult(string shipSymbol = "PROBE-NEW") =>
        new(
            Agent: new AgentModel("AGENT", null, "X1-AB-HQ", 177_000, "FACTION", 2),
            ShipSymbol: shipSymbol,
            ShipNav: new NavModel("DOCKED", "X1-AB", "X1-AB-SY1", "DRIFT", null, null),
            ShipFuel: new FuelModel(0, 0),
            ShipCargo: new CargoModel(0, 0, []),
            Cost: 23_000);

    // Helper: checks published message is a DeployProbeCommand with the given destination.
    private static bool IsDeployCommand(object msg, string destination) =>
        msg is DeployProbeCommand cmd && cmd.DestinationWaypoint == destination;

    private static bool IsDeployCommand(object msg) =>
        msg is DeployProbeCommand;

    private static bool IsDeployCommand(object msg, string probeSymbol, string destination) =>
        msg is DeployProbeCommand cmd && cmd.ProbeSymbol == probeSymbol && cmd.DestinationWaypoint == destination;

    // -------------------------------------------------------------------------
    // EnsureBootstrappedAsync — bootstrap behaviour
    // -------------------------------------------------------------------------

    [Fact]
    public async Task EnsureBootstrappedAsync_SkipsBootstrap_WhenAgentNotFound()
    {
        _plans.GetAsync(Arg.Any<CancellationToken>()).Returns((ProbeDeploymentPlanState?)null);
        _agents.GetAsync(Arg.Any<CancellationToken>()).Returns((AgentModel?)null);

        await CreateService().EnsureBootstrappedAsync();

        await _plans.DidNotReceive().UpsertAsync(Arg.Any<ProbeDeploymentPlanState>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task EnsureBootstrappedAsync_SkipsBootstrap_WhenNoWaypointsFound()
    {
        _plans.GetAsync(Arg.Any<CancellationToken>()).Returns((ProbeDeploymentPlanState?)null);
        _agents.GetAsync(Arg.Any<CancellationToken>()).Returns(Agent());
        _waypoints.GetBySystemAsync("X1-AB", Arg.Any<CancellationToken>())
            .Returns(Array.Empty<WaypointCacheModel>());

        await CreateService().EnsureBootstrappedAsync();

        await _plans.DidNotReceive().UpsertAsync(Arg.Any<ProbeDeploymentPlanState>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task EnsureBootstrappedAsync_CreatesPlan_WithCorrectTargets()
    {
        _plans.GetAsync(Arg.Any<CancellationToken>()).Returns((ProbeDeploymentPlanState?)null);
        _agents.GetAsync(Arg.Any<CancellationToken>()).Returns(Agent());
        _waypoints.GetBySystemAsync("X1-AB", Arg.Any<CancellationToken>())
            .Returns([
                Waypoint("X1-AB-SY1", hasMarket: true, hasShipyard: true),
                Waypoint("X1-AB-MKT1", hasMarket: true),
                Waypoint("X1-AB-NONE"),
            ]);
        _ships.GetAllAsync(Arg.Any<CancellationToken>()).Returns([]);
        _shipyards.GetAllAsync(Arg.Any<CancellationToken>()).Returns([]);

        await CreateService().EnsureBootstrappedAsync();

        await _plans.Received(1).UpsertAsync(
            Arg.Is<ProbeDeploymentPlanState>(p =>
                p.TargetWaypointSymbols.Count == 2
                && p.TargetWaypointSymbols.Contains("X1-AB-SY1")
                && p.TargetWaypointSymbols.Contains("X1-AB-MKT1")
                && p.ShipyardWaypointSymbols != null
                && p.ShipyardWaypointSymbols.Count == 1
                && p.ShipyardWaypointSymbols.Contains("X1-AB-SY1")),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task EnsureBootstrappedAsync_ResumesPlan_WhenPlanAlreadyExists()
    {
        var plan = ActivePlan(["X1-AB-SY1"], [], ["X1-AB-SY1"]);
        _plans.GetAsync(Arg.Any<CancellationToken>()).Returns(plan);
        _waypoints.GetBySystemAsync("X1-AB", Arg.Any<CancellationToken>())
            .Returns([Waypoint("X1-AB-SY1", hasShipyard: true)]);
        _ships.GetAllAsync(Arg.Any<CancellationToken>()).Returns([Probe("PROBE-1")]);

        await CreateService().EnsureBootstrappedAsync();

        await _bus.Received(1).PublishAsync(
            Arg.Is<object>(o => IsDeployCommand(o, "X1-AB-SY1")));
    }

    [Fact]
    public async Task EnsureBootstrappedAsync_DoesNotResume_WhenPlanIsCompleted()
    {
        var plan = ActivePlan(["X1-AB-SY1"], ["X1-AB-SY1"], ["X1-AB-SY1"]) with
        {
            Status = ProbeDeploymentPlanStatus.Completed
        };
        _plans.GetAsync(Arg.Any<CancellationToken>()).Returns(plan);
        _waypoints.GetBySystemAsync("X1-AB", Arg.Any<CancellationToken>())
            .Returns([Waypoint("X1-AB-SY1", hasShipyard: true)]);

        await CreateService().EnsureBootstrappedAsync();

        await _bus.DidNotReceive().PublishAsync(Arg.Is<object>(o => IsDeployCommand(o)));
    }

    [Fact]
    public async Task EnsureBootstrappedAsync_ReconcilesAndDispatches_WhenNewTargetDiscoveredForActivePlan()
    {
        var plan = ActivePlan(["X1-AB-SY1"], ["X1-AB-SY1"], ["X1-AB-SY1"]);
        _plans.GetAsync(Arg.Any<CancellationToken>()).Returns(plan);
        _waypoints.GetBySystemAsync("X1-AB", Arg.Any<CancellationToken>())
            .Returns([
                Waypoint("X1-AB-SY1", hasShipyard: true),
                Waypoint("X1-AB-MKT1", hasMarket: true),
            ]);
        _agents.GetAsync(Arg.Any<CancellationToken>()).Returns(Agent(credits: 250_000));
        _ships.GetAllAsync(Arg.Any<CancellationToken>()).Returns([Probe("PROBE-1", "X1-AB-HOME")]);

        await CreateService().EnsureBootstrappedAsync();

        await _plans.Received(1).UpsertAsync(
            Arg.Is<ProbeDeploymentPlanState>(p =>
                p.TargetWaypointSymbols.Count == 2
                && p.TargetWaypointSymbols.Contains("X1-AB-MKT1")
                && p.Status == ProbeDeploymentPlanStatus.Active),
            Arg.Any<CancellationToken>());
        await _bus.Received(1).PublishAsync(
            Arg.Is<object>(o => IsDeployCommand(o, "X1-AB-MKT1")));
    }

    [Fact]
    public async Task EnsureBootstrappedAsync_ReopensCompletedPlan_WhenNewTargetDiscovered()
    {
        var plan = ActivePlan(["X1-AB-SY1"], ["X1-AB-SY1"], ["X1-AB-SY1"]) with
        {
            Status = ProbeDeploymentPlanStatus.Completed
        };
        _plans.GetAsync(Arg.Any<CancellationToken>()).Returns(plan);
        _waypoints.GetBySystemAsync("X1-AB", Arg.Any<CancellationToken>())
            .Returns([
                Waypoint("X1-AB-SY1", hasShipyard: true),
                Waypoint("X1-AB-MKT1", hasMarket: true),
            ]);
        _agents.GetAsync(Arg.Any<CancellationToken>()).Returns(Agent(credits: 250_000));
        _ships.GetAllAsync(Arg.Any<CancellationToken>()).Returns([Probe("PROBE-1", "X1-AB-HOME")]);

        await CreateService().EnsureBootstrappedAsync();

        await _plans.Received(1).UpsertAsync(
            Arg.Is<ProbeDeploymentPlanState>(p =>
                p.TargetWaypointSymbols.Count == 2
                && p.TargetWaypointSymbols.Contains("X1-AB-MKT1")
                && p.Status == ProbeDeploymentPlanStatus.Active),
            Arg.Any<CancellationToken>());
        await _bus.Received(1).PublishAsync(
            Arg.Is<object>(o => IsDeployCommand(o, "X1-AB-MKT1")));
    }

    // -------------------------------------------------------------------------
    // AdvanceAsync
    // -------------------------------------------------------------------------

    [Fact]
    public async Task AdvanceAsync_MarksWaypointDeployed_AndDispatchesNextTarget()
    {
        var plan = ActivePlan(["X1-AB-SY1", "X1-AB-SY2"], [], ["X1-AB-SY1", "X1-AB-SY2"]);
        _plans.GetAsync(Arg.Any<CancellationToken>()).Returns(plan);
        _ships.GetAllAsync(Arg.Any<CancellationToken>()).Returns([Probe("PROBE-1")]);

        await CreateService().AdvanceAsync("X1-AB-SY1");

        await _plans.Received(1).UpsertAsync(
            Arg.Is<ProbeDeploymentPlanState>(p =>
                p.DeployedWaypointSymbols.Contains("X1-AB-SY1")
                && p.Status == ProbeDeploymentPlanStatus.Active),
            Arg.Any<CancellationToken>());
        await _bus.Received(1).PublishAsync(
            Arg.Is<object>(o => IsDeployCommand(o, "X1-AB-SY2")));
    }

    [Fact]
    public async Task AdvanceAsync_CompletesPlan_WhenAllTargetsDeployed()
    {
        var plan = ActivePlan(["X1-AB-SY1"], [], ["X1-AB-SY1"]);
        _plans.GetAsync(Arg.Any<CancellationToken>()).Returns(plan);

        await CreateService().AdvanceAsync("X1-AB-SY1");

        await _plans.Received(1).UpsertAsync(
            Arg.Is<ProbeDeploymentPlanState>(p => p.Status == ProbeDeploymentPlanStatus.Completed),
            Arg.Any<CancellationToken>());
        await _bus.DidNotReceive().PublishAsync(Arg.Is<object>(o => IsDeployCommand(o)));
    }

    [Fact]
    public async Task AdvanceAsync_SkipsAlreadyDeployedWaypoint()
    {
        var plan = ActivePlan(["X1-AB-SY1"], ["X1-AB-SY1"], ["X1-AB-SY1"]);
        _plans.GetAsync(Arg.Any<CancellationToken>()).Returns(plan);

        await CreateService().AdvanceAsync("X1-AB-SY1");

        await _plans.DidNotReceive().UpsertAsync(Arg.Any<ProbeDeploymentPlanState>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task AdvanceAsync_DoesNothing_WhenNoPlanExists()
    {
        _plans.GetAsync(Arg.Any<CancellationToken>()).Returns((ProbeDeploymentPlanState?)null);

        await CreateService().AdvanceAsync("X1-AB-SY1");

        await _bus.DidNotReceive().PublishAsync(Arg.Is<object>(o => IsDeployCommand(o)));
    }

    // -------------------------------------------------------------------------
    // Phase ordering
    // -------------------------------------------------------------------------

    [Fact]
    public async Task Dispatch_PrioritisesShipyardTargets_BeforeMarketOnly()
    {
        var plan = ActivePlan(
            targets: ["X1-AB-SY1", "X1-AB-MKT1"],
            deployed: [],
            shipyardTargets: ["X1-AB-SY1"]);
        _plans.GetAsync(Arg.Any<CancellationToken>()).Returns(plan);
        _ships.GetAllAsync(Arg.Any<CancellationToken>()).Returns([Probe("PROBE-1")]);

        await CreateService().EnsureBootstrappedAsync();

        await _bus.Received(1).PublishAsync(Arg.Is<object>(o => IsDeployCommand(o, "X1-AB-SY1")));
        await _bus.DidNotReceive().PublishAsync(Arg.Is<object>(o => IsDeployCommand(o, "X1-AB-MKT1")));
    }

    [Fact]
    public async Task Dispatch_DefersMarketOnlyTarget_WhenBelowPhase1Threshold()
    {
        var plan = ActivePlan(
            targets: ["X1-AB-SY1", "X1-AB-MKT1"],
            deployed: ["X1-AB-SY1"],
            shipyardTargets: ["X1-AB-SY1"]);
        _plans.GetAsync(Arg.Any<CancellationToken>()).Returns(plan);
        _agents.GetAsync(Arg.Any<CancellationToken>()).Returns(Agent(credits: 150_000));
        _ships.GetAllAsync(Arg.Any<CancellationToken>()).Returns([Probe("PROBE-1")]);

        await CreateService().EnsureBootstrappedAsync();

        await _bus.DidNotReceive().PublishAsync(Arg.Is<object>(o => IsDeployCommand(o)));
    }

    [Fact]
    public async Task Dispatch_AllowsMarketOnlyTarget_WhenAbovePhase1Threshold()
    {
        var plan = ActivePlan(
            targets: ["X1-AB-SY1", "X1-AB-MKT1"],
            deployed: ["X1-AB-SY1"],
            shipyardTargets: ["X1-AB-SY1"]);
        _plans.GetAsync(Arg.Any<CancellationToken>()).Returns(plan);
        _agents.GetAsync(Arg.Any<CancellationToken>()).Returns(Agent(credits: 250_000));
        _ships.GetAllAsync(Arg.Any<CancellationToken>()).Returns([Probe("PROBE-1")]);

        await CreateService().EnsureBootstrappedAsync();

        await _bus.Received(1).PublishAsync(Arg.Is<object>(o => IsDeployCommand(o, "X1-AB-MKT1")));
    }

    [Fact]
    public async Task Dispatch_PurchasesProbeForMarketOnlyTarget_WhenPhase1ThresholdMetAndNoneAvailable()
    {
        var plan = ActivePlan(
            targets: ["X1-AB-SY1", "X1-AB-MKT1"],
            deployed: ["X1-AB-SY1"],
            shipyardTargets: ["X1-AB-SY1"]);
        _plans.GetAsync(Arg.Any<CancellationToken>()).Returns(plan);
        _agents.GetAsync(Arg.Any<CancellationToken>()).Returns(Agent(credits: 250_000));
        _ships.GetAllAsync(Arg.Any<CancellationToken>()).Returns([]);
        _shipyards.GetAllAsync(Arg.Any<CancellationToken>())
            .Returns([Shipyard("X1-AB-SY1", probePrice: 23_000)]);
        _budget.EvaluateAsync(23_000, Arg.Any<CancellationToken>())
            .Returns(new BudgetDecision(
                CanAfford: true,
                AvailableCredits: 250_000,
                ReservedCredits: 100_000,
                SpendableCredits: 150_000,
                FabMatsBuyThreshold: 2_500,
                FabMatsTransactionSize: 60,
                HourlyConstructionBudgetCapEnabled: false));
        _port.PurchaseShipAsync("SHIP_PROBE", "X1-AB-SY1", Arg.Any<CancellationToken>())
            .Returns(PurchaseResult("PROBE-PHASE1"));

        await CreateService().EnsureBootstrappedAsync();

        await _port.Received(1).PurchaseShipAsync("SHIP_PROBE", "X1-AB-SY1", Arg.Any<CancellationToken>());
        await _bus.Received(1).PublishAsync(
            Arg.Is<object>(o => IsDeployCommand(o, "PROBE-PHASE1", "X1-AB-MKT1")));
    }

    // -------------------------------------------------------------------------
    // Multi-probe parallel dispatch
    // -------------------------------------------------------------------------

    [Fact]
    public async Task Dispatch_SendsMultipleProbes_WhenMultipleIdleProbesAvailable()
    {
        var plan = ActivePlan(
            targets: ["X1-AB-SY1", "X1-AB-SY2"],
            deployed: [],
            shipyardTargets: ["X1-AB-SY1", "X1-AB-SY2"]);
        _plans.GetAsync(Arg.Any<CancellationToken>()).Returns(plan);
        _ships.GetAllAsync(Arg.Any<CancellationToken>()).Returns([
            Probe("PROBE-1", "X1-AB-HOME"),
            Probe("PROBE-2", "X1-AB-HOME"),
        ]);

        await CreateService().EnsureBootstrappedAsync();

        await _bus.Received(1).PublishAsync(Arg.Is<object>(o => IsDeployCommand(o, "X1-AB-SY1")));
        await _bus.Received(1).PublishAsync(Arg.Is<object>(o => IsDeployCommand(o, "X1-AB-SY2")));
    }

    [Fact]
    public async Task Dispatch_DoesNotDoubleDispatch_SameProbe_ToTwoTargets()
    {
        var plan = ActivePlan(
            targets: ["X1-AB-SY1", "X1-AB-SY2"],
            deployed: [],
            shipyardTargets: ["X1-AB-SY1", "X1-AB-SY2"]);
        _plans.GetAsync(Arg.Any<CancellationToken>()).Returns(plan);
        // Only one probe available — second target falls through to purchase (no shipyard)
        _ships.GetAllAsync(Arg.Any<CancellationToken>()).Returns([Probe("PROBE-1", "X1-AB-HOME")]);
        _shipyards.GetAllAsync(Arg.Any<CancellationToken>()).Returns([]);

        await CreateService().EnsureBootstrappedAsync();

        await _bus.Received(1).PublishAsync(Arg.Is<object>(o => IsDeployCommand(o)));
    }

    // -------------------------------------------------------------------------
    // Probe purchase fallback
    // -------------------------------------------------------------------------

    [Fact]
    public async Task Dispatch_PurchasesProbe_WhenNoneAvailable_AndCanAfford()
    {
        var plan = ActivePlan(["X1-AB-SY1"], [], ["X1-AB-SY1"]);
        _plans.GetAsync(Arg.Any<CancellationToken>()).Returns(plan);
        _ships.GetAllAsync(Arg.Any<CancellationToken>()).Returns([]);
        _shipyards.GetAllAsync(Arg.Any<CancellationToken>())
            .Returns([Shipyard("X1-AB-SY1", probePrice: 23_000)]);

        _budget.EvaluateAsync(23_000, Arg.Any<CancellationToken>())
            .Returns(new BudgetDecision(
                CanAfford: true,
                AvailableCredits: 200_000,
                ReservedCredits: 100_000,
                SpendableCredits: 100_000,
                FabMatsBuyThreshold: 2_500,
                FabMatsTransactionSize: 60,
                HourlyConstructionBudgetCapEnabled: false));

        _port.PurchaseShipAsync("SHIP_PROBE", "X1-AB-SY1", Arg.Any<CancellationToken>())
            .Returns(PurchaseResult("PROBE-NEW"));

        await CreateService().EnsureBootstrappedAsync();

        await _port.Received(1).PurchaseShipAsync("SHIP_PROBE", "X1-AB-SY1", Arg.Any<CancellationToken>());
        await _agents.Received(1).UpsertAsync(Arg.Any<AgentModel>(), Arg.Any<CancellationToken>());
        await _ships.Received(1).UpsertAsync(Arg.Any<ShipModel>(), Arg.Any<CancellationToken>());
        await _bus.Received(1).PublishAsync(
            Arg.Is<object>(o => IsDeployCommand(o, "PROBE-NEW", "X1-AB-SY1")));
    }

    [Fact]
    public async Task Dispatch_SkipsPurchase_WhenBudgetDenies()
    {
        var plan = ActivePlan(["X1-AB-SY1"], [], ["X1-AB-SY1"]);
        _plans.GetAsync(Arg.Any<CancellationToken>()).Returns(plan);
        _ships.GetAllAsync(Arg.Any<CancellationToken>()).Returns([]);
        _shipyards.GetAllAsync(Arg.Any<CancellationToken>())
            .Returns([Shipyard("X1-AB-SY1", probePrice: 23_000)]);

        _budget.EvaluateAsync(23_000, Arg.Any<CancellationToken>())
            .Returns(new BudgetDecision(
                CanAfford: false,
                AvailableCredits: 80_000,
                ReservedCredits: 100_000,
                SpendableCredits: 0,
                Reason: "Reserve exceeded.",
                FabMatsBuyThreshold: 2_500,
                FabMatsTransactionSize: 60,
                HourlyConstructionBudgetCapEnabled: false));

        await CreateService().EnsureBootstrappedAsync();

        await _port.DidNotReceive().PurchaseShipAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>());
        await _bus.DidNotReceive().PublishAsync(Arg.Is<object>(o => IsDeployCommand(o)));
    }

    [Fact]
    public async Task Dispatch_SkipsPurchase_WhenNoShipyardKnown()
    {
        var plan = ActivePlan(["X1-AB-SY1"], [], ["X1-AB-SY1"]);
        _plans.GetAsync(Arg.Any<CancellationToken>()).Returns(plan);
        _ships.GetAllAsync(Arg.Any<CancellationToken>()).Returns([]);
        _shipyards.GetAllAsync(Arg.Any<CancellationToken>()).Returns([]);

        await CreateService().EnsureBootstrappedAsync();

        await _port.DidNotReceive().PurchaseShipAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>());
        await _bus.DidNotReceive().PublishAsync(Arg.Is<object>(o => IsDeployCommand(o)));
    }

    // -------------------------------------------------------------------------
    // In-transit probe is not selected
    // -------------------------------------------------------------------------

    [Fact]
    public async Task Dispatch_IgnoresInTransitProbe_AndFallsThroughToPurchase()
    {
        var plan = ActivePlan(["X1-AB-SY1"], [], ["X1-AB-SY1"]);
        _plans.GetAsync(Arg.Any<CancellationToken>()).Returns(plan);
        _ships.GetAllAsync(Arg.Any<CancellationToken>()).Returns([Probe("PROBE-1", inTransit: true)]);
        _shipyards.GetAllAsync(Arg.Any<CancellationToken>()).Returns([]);

        await CreateService().EnsureBootstrappedAsync();

        await _port.DidNotReceive().PurchaseShipAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>());
        await _bus.DidNotReceive().PublishAsync(Arg.Is<object>(o => IsDeployCommand(o)));
    }

    // -------------------------------------------------------------------------
    // Already-deployed probe is excluded from selection
    // -------------------------------------------------------------------------

    [Fact]
    public async Task Dispatch_ExcludesProbeAlreadyAtDeployedWaypoint()
    {
        var plan = ActivePlan(
            targets: ["X1-AB-SY1", "X1-AB-SY2"],
            deployed: ["X1-AB-SY1"],
            shipyardTargets: ["X1-AB-SY1", "X1-AB-SY2"]);
        _plans.GetAsync(Arg.Any<CancellationToken>()).Returns(plan);
        _ships.GetAllAsync(Arg.Any<CancellationToken>()).Returns([
            Probe("PROBE-1", waypointSymbol: "X1-AB-SY1"),   // already deployed
            Probe("PROBE-2", waypointSymbol: "X1-AB-HOME"),  // idle
        ]);

        await CreateService().EnsureBootstrappedAsync();

        await _bus.Received(1).PublishAsync(
            Arg.Is<object>(o => IsDeployCommand(o, "PROBE-2", "X1-AB-SY2")));
    }
}
