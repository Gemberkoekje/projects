using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using SpaceTraders.Application.DTOs;
using SpaceTraders.Application.Events.Handlers.Ships;
using SpaceTraders.Application.Interfaces.Repositories;
using SpaceTraders.Application.Planning;
using SpaceTraders.Application.Ports;
using SpaceTraders.Application.Services;
using Wolverine;

namespace SpaceTraders.Application.Tests.Planning;

/// <summary>
/// Phase 3: tests for the planner service that translates planner decisions into commands
/// via the existing command acceptors. Verifies the "one decision -> one command" rule.
/// </summary>
public sealed class ShipPlannerServiceTests
{
    private static ShipPlannerService Build(
        out IShipRepository shipsRepo,
        out IShipAssignmentRepository assignmentsRepo,
        out ISurveyRepository surveysRepo,
        out INavigationPlanningService navigation,
        out IInOrbitCommandAcceptor inOrbit,
        out IDockedCommandAcceptor docked,
        out IMessageBus bus,
        params IShipPlanner[] planners)
    {
        shipsRepo = Substitute.For<IShipRepository>();
        assignmentsRepo = Substitute.For<IShipAssignmentRepository>();
        surveysRepo = Substitute.For<ISurveyRepository>();
        navigation = Substitute.For<INavigationPlanningService>();
        inOrbit = Substitute.For<IInOrbitCommandAcceptor>();
        docked = Substitute.For<IDockedCommandAcceptor>();
        bus = Substitute.For<IMessageBus>();

        var constructions = Substitute.For<IConstructionRepository>();
        var waypoints = Substitute.For<IWaypointRepository>();
        var markets = Substitute.For<IMarketRepository>();
        waypoints.GetBySystemAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns([]);
        markets.GetAllSnapshotsAsync(Arg.Any<CancellationToken>())
            .Returns([]);
        var maintenance = Substitute.For<IFleetMaintenancePlanner>();
        maintenance.DecideAsync(Arg.Any<ShipModel>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(new FleetMaintenanceDecision(ShouldRepair: false, ShouldScrap: false, AvoidLongRoutes: false));

        return new ShipPlannerService(
            planners,
            shipsRepo,
            assignmentsRepo,
            surveysRepo,
            navigation,
            constructions,
            waypoints,
            markets,
            maintenance,
            inOrbit,
            docked,
            bus,
            NullLogger<ShipPlannerService>.Instance);
    }

    [Fact]
    public async Task PlanAndExecuteAsync_ReturnsNone_WhenShipNotFound()
    {
        var service = Build(out var ships, out _, out _, out _, out var inOrbit, out var docked, out _, new MiningShipPlanner());
        ships.FindAsync("SHIP-X", Arg.Any<CancellationToken>()).Returns((ShipModel?)null);

        var decision = await service.PlanAndExecuteAsync("SHIP-X", CancellationToken.None);

        decision.Kind.Should().Be(ShipPlannerCommandKind.None);
        await inOrbit.DidNotReceiveWithAnyArgs().NavigateAsync(default!, default!, default);
        await docked.DidNotReceiveWithAnyArgs().OrbitAsync(default!, default);
    }

    [Fact]
    public async Task PlanAndExecuteAsync_ReturnsNone_WhenShipHasNoAssignment()
    {
        var service = Build(out var ships, out var assignments, out _, out _, out _, out _, out _, new MiningShipPlanner());
        ships.FindAsync("SHIP-1", Arg.Any<CancellationToken>())
            .Returns(new ShipModel("SHIP-1", "X1-AB", "X1-AB-A", "IN_ORBIT", "CRUISE", 80, 100));
        assignments.FindAsync("SHIP-1", Arg.Any<CancellationToken>()).Returns((ShipAssignmentDto?)null);

        var decision = await service.PlanAndExecuteAsync("SHIP-1", CancellationToken.None);

        decision.Kind.Should().Be(ShipPlannerCommandKind.None);
    }

    [Fact]
    public async Task PlanAndExecuteAsync_RoutesExtract_ViaInOrbitAcceptor()
    {
        var service = Build(out var ships, out var assignments, out var surveys, out _, out var inOrbit, out _, out _, new MiningShipPlanner());

        ships.FindAsync("SHIP-1", Arg.Any<CancellationToken>())
            .Returns(new ShipModel("SHIP-1", "X1-AB", "X1-AB-AST", "IN_ORBIT", "CRUISE", 80, 100,
                CargoCurrent: 0, CargoCapacity: 40, MountSymbols: ["MOUNT_MINING_LASER_I"]));

        assignments.FindAsync("SHIP-1", Arg.Any<CancellationToken>())
            .Returns(new ShipAssignmentDto("SHIP-1", "Mine", "X1-AB-AST", "X1-AB-MKT", null, null, 0, DateTimeOffset.UtcNow, null));

        surveys.GetActiveByWaypointAsync("X1-AB-AST", Arg.Any<CancellationToken>()).Returns([]);

        var decision = await service.PlanAndExecuteAsync("SHIP-1", CancellationToken.None);

        decision.Kind.Should().Be(ShipPlannerCommandKind.Extract);
        await inOrbit.Received(1).ExtractAsync("SHIP-1", Arg.Any<CancellationToken>());
        await inOrbit.DidNotReceiveWithAnyArgs().NavigateAsync(default!, default!, default);
        await inOrbit.DidNotReceiveWithAnyArgs().DockAsync(default!, default);
    }

    [Fact]
    public async Task PlanAndExecuteAsync_RoutesNavigate_ViaInOrbitAcceptor()
    {
        var service = Build(out var ships, out var assignments, out _, out var navigation, out var inOrbit, out _, out _, new MiningShipPlanner());

        ships.FindAsync("SHIP-1", Arg.Any<CancellationToken>())
            .Returns(new ShipModel("SHIP-1", "X1-AB", "X1-AB-AST", "IN_ORBIT", "CRUISE", 80, 100,
                CargoCurrent: 40, CargoCapacity: 40));

        assignments.FindAsync("SHIP-1", Arg.Any<CancellationToken>())
            .Returns(new ShipAssignmentDto("SHIP-1", "Mine", "X1-AB-AST", "X1-AB-MKT", null, null, 0, DateTimeOffset.UtcNow, null));

        navigation.BuildPlanAsync(Arg.Any<ShipModel>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns((NavigationPlan?)null);

        var decision = await service.PlanAndExecuteAsync("SHIP-1", CancellationToken.None);

        decision.Kind.Should().Be(ShipPlannerCommandKind.Navigate);
        await inOrbit.Received(1).NavigateAsync("SHIP-1", "X1-AB-MKT", Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task PlanAndExecuteAsync_ReturnsNone_WhenNoPlannerMatchesAssignment()
    {
        var service = Build(out var ships, out var assignments, out _, out _, out var inOrbit, out _, out _, new MiningShipPlanner());

        ships.FindAsync("SHIP-1", Arg.Any<CancellationToken>())
            .Returns(new ShipModel("SHIP-1", "X1-AB", "X1-AB-A", "IN_ORBIT", "CRUISE", 80, 100));

        assignments.FindAsync("SHIP-1", Arg.Any<CancellationToken>())
            .Returns(new ShipAssignmentDto("SHIP-1", "Trade", "X1-AB-A", "X1-AB-B", null, null, 0, DateTimeOffset.UtcNow, null));

        var decision = await service.PlanAndExecuteAsync("SHIP-1", CancellationToken.None);

        decision.Kind.Should().Be(ShipPlannerCommandKind.None);
        await inOrbit.DidNotReceiveWithAnyArgs().NavigateAsync(default!, default!, default);
    }
}
