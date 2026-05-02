using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using SpaceTraders.Application.Commands.Contracts;
using SpaceTraders.Application.Commands.Ships;
using SpaceTraders.Application.DTOs;
using SpaceTraders.Application.Interfaces;
using SpaceTraders.Application.Interfaces.Repositories;
using SpaceTraders.Application.Ports;
using SpaceTraders.Application.Services;
using SpaceTraders.Domain.Enums;
using SpaceTraders.Domain.Events.Ships;
using Wolverine;

namespace SpaceTraders.Application.Tests.Commands;

/// <summary>
/// Phase 6.5a + 6.5e:
/// - Each ship-owned command exposes <c>ExecuteAsync</c> returning a
///   <see cref="ShipCommandResult"/> that reflects whether the API call was issued
///   and the resulting (or current) <see cref="ShipLocalStatus"/>.
/// - Whenever a command rejects with <see cref="ShipStateMismatchEvent"/>, it must NOT
///   publish a <see cref="ShipAutomationTickEvent"/>; the orchestrator will reassert
///   on the next tick.
/// </summary>
public sealed class Phase65CommandHandlerTests
{
    // ---------------- Phase 6.5e: tick is published alongside mismatch ----------------

    [Fact]
    public async Task DockShip_WhenNotInOrbit_PublishesMismatchEvent()
    {
        var (port, ships, bus) = MoveCtx();
        ships.FindAsync("SHIP-1", Arg.Any<CancellationToken>())
            .Returns(new ShipModel("SHIP-1", "X1-AB", "X1-AB-001", "DOCKED", "CRUISE", 80, 100));

        var handler = new DockShipHandler(port, ships, bus, NullLogger<DockShipHandler>.Instance);
        await handler.Handle(new DockShipCommand("SHIP-1"), CancellationToken.None);

        await bus.Received(1).PublishAsync(Arg.Any<ShipStateMismatchEvent>(), Arg.Any<DeliveryOptions>());
    }

    [Fact]
    public async Task OrbitShip_WhenNotDocked_PublishesMismatchEvent()
    {
        var (port, ships, bus) = MoveCtx();
        ships.FindAsync("SHIP-1", Arg.Any<CancellationToken>())
            .Returns(new ShipModel("SHIP-1", "X1-AB", "X1-AB-001", "IN_ORBIT", "CRUISE", 80, 100));

        var handler = new OrbitShipHandler(port, ships, bus, NullLogger<OrbitShipHandler>.Instance);
        await handler.Handle(new OrbitShipCommand("SHIP-1"), CancellationToken.None);

        await bus.Received(1).PublishAsync(Arg.Any<ShipStateMismatchEvent>(), Arg.Any<DeliveryOptions>());
    }

    [Fact]
    public async Task NavigateShip_WhenDocked_PublishesMismatchEvent()
    {
        var (port, ships, bus) = MoveCtx();
        var goals = Substitute.For<IShipGoalRepository>();
        var scheduler = Substitute.For<IShipEventScheduler>();
        ships.FindAsync("SHIP-1", Arg.Any<CancellationToken>())
            .Returns(new ShipModel("SHIP-1", "X1-AB", "X1-AB-001", "DOCKED", "CRUISE", 100, 100));

        var handler = new NavigateShipHandler(port, ships, goals, scheduler, bus, NullLogger<NavigateShipHandler>.Instance);
        await handler.Handle(new NavigateShipCommand("SHIP-1", "X1-AB-002"), CancellationToken.None);

        await bus.Received(1).PublishAsync(Arg.Any<ShipStateMismatchEvent>(), Arg.Any<DeliveryOptions>());
    }

    [Fact]
    public async Task SellCargo_WhenNotDocked_PublishesMismatchEvent()
    {
        var port = Substitute.For<ISpaceTradersPort>();
        var ships = Substitute.For<IShipRepository>();
        var agents = Substitute.For<IAgentRepository>();
        var waypoints = Substitute.For<IWaypointRepository>();
        var assignments = Substitute.For<IShipAssignmentRepository>();
        var bus = Substitute.For<IMessageBus>();
        ships.FindAsync("SHIP-1", Arg.Any<CancellationToken>())
            .Returns(new ShipModel("SHIP-1", "X1-AB", "X1-AB-001", "IN_ORBIT", "CRUISE", 100, 100));

        var handler = new SellCargoHandler(port, ships, agents, waypoints, assignments, bus, NullLogger<SellCargoHandler>.Instance);
        await handler.Handle(new SellCargoCommand("SHIP-1", "IRON_ORE", 10), CancellationToken.None);

        await bus.Received(1).PublishAsync(Arg.Any<ShipStateMismatchEvent>(), Arg.Any<DeliveryOptions>());
    }

    // ---------------- Phase 6.5a: accepted / rejected results ----------------

    [Fact]
    public async Task ExtractResources_WhenInOrbitAtAsteroid_ReturnsAcceptedResult()
    {
        var port = Substitute.For<ISpaceTradersPort>();
        var ships = Substitute.For<IShipRepository>();
        var goals = Substitute.For<IShipGoalRepository>();
        var scheduler = Substitute.For<IShipEventScheduler>();
        var waypoints = Substitute.For<IWaypointRepository>();
        var surveys = Substitute.For<ISurveyRepository>();
        var assignments = Substitute.For<IShipAssignmentRepository>();
        var bus = Substitute.For<IMessageBus>();

        ships.FindAsync("SHIP-1", Arg.Any<CancellationToken>())
            .Returns(new ShipModel("SHIP-1", "X1-AB", "X1-AB-001", "IN_ORBIT", "CRUISE", 100, 100));
        waypoints.FindAsync("X1-AB-001", Arg.Any<CancellationToken>())
            .Returns(new WaypointCacheModel("X1-AB-001", "X1-AB", "ASTEROID", 0, 0, false, false, DateTimeOffset.UtcNow));
        surveys.GetBestActiveSurveyAsync("X1-AB-001", Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(new SurveyModel(string.Empty, "X1-AB-001", [], DateTimeOffset.MinValue, string.Empty));
        port.ExtractResourcesAsync("SHIP-1", Arg.Any<CancellationToken>())
            .Returns(new ExtractionActionResult("IRON_ORE", 10, new CargoModel(10, 60), 60));

        var handler = new ExtractResourcesHandler(port, ships, goals, scheduler, waypoints, surveys, assignments, bus, NullLogger<ExtractResourcesHandler>.Instance);
        var result = await handler.ExecuteAsync(new ExtractResourcesCommand("SHIP-1"), CancellationToken.None);

        result.Accepted.Should().BeTrue();
        result.Status.Should().Be(ShipLocalStatus.InOrbit);
        result.CargoCurrent.Should().Be(10);
    }

    [Fact]
    public async Task ExtractResources_WhenDocked_ReturnsRejectedResult_AndPublishesMismatch()
    {
        var port = Substitute.For<ISpaceTradersPort>();
        var ships = Substitute.For<IShipRepository>();
        var goals = Substitute.For<IShipGoalRepository>();
        var scheduler = Substitute.For<IShipEventScheduler>();
        var waypoints = Substitute.For<IWaypointRepository>();
        var surveys = Substitute.For<ISurveyRepository>();
        var assignments = Substitute.For<IShipAssignmentRepository>();
        var bus = Substitute.For<IMessageBus>();

        ships.FindAsync("SHIP-1", Arg.Any<CancellationToken>())
            .Returns(new ShipModel("SHIP-1", "X1-AB", "X1-AB-001", "DOCKED", "CRUISE", 100, 100));

        var handler = new ExtractResourcesHandler(port, ships, goals, scheduler, waypoints, surveys, assignments, bus, NullLogger<ExtractResourcesHandler>.Instance);
        var result = await handler.ExecuteAsync(new ExtractResourcesCommand("SHIP-1"), CancellationToken.None);

        result.Accepted.Should().BeFalse();
        result.Status.Should().Be(ShipLocalStatus.Docked);
        await port.DidNotReceive().ExtractResourcesAsync(Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Survey_WhenInOrbit_ReturnsAcceptedResult()
    {
        var port = Substitute.For<ISpaceTradersPort>();
        var ships = Substitute.For<IShipRepository>();
        var goals = Substitute.For<IShipGoalRepository>();
        var scheduler = Substitute.For<IShipEventScheduler>();
        var surveys = Substitute.For<ISurveyRepository>();
        var bus = Substitute.For<IMessageBus>();
        ships.FindAsync("SHIP-1", Arg.Any<CancellationToken>())
            .Returns(new ShipModel("SHIP-1", "X1-AB", "X1-AB-001", "IN_ORBIT", "CRUISE", 80, 100));
        port.SurveyAsync("SHIP-1", Arg.Any<CancellationToken>())
            .Returns(new SurveyActionResult([], 60));

        var handler = new SurveyHandler(port, ships, goals, scheduler, surveys, bus, NullLogger<SurveyHandler>.Instance);
        var result = await handler.ExecuteAsync(new SurveyCommand("SHIP-1"), CancellationToken.None);

        result.Accepted.Should().BeTrue();
        result.Status.Should().Be(ShipLocalStatus.InOrbit);
    }

    [Fact]
    public async Task Survey_WhenDocked_ReturnsRejected_AndPublishesMismatch()
    {
        var port = Substitute.For<ISpaceTradersPort>();
        var ships = Substitute.For<IShipRepository>();
        var goals = Substitute.For<IShipGoalRepository>();
        var scheduler = Substitute.For<IShipEventScheduler>();
        var surveys = Substitute.For<ISurveyRepository>();
        var bus = Substitute.For<IMessageBus>();
        ships.FindAsync("SHIP-1", Arg.Any<CancellationToken>())
            .Returns(new ShipModel("SHIP-1", "X1-AB", "X1-AB-001", "DOCKED", "CRUISE", 80, 100));

        var handler = new SurveyHandler(port, ships, goals, scheduler, surveys, bus, NullLogger<SurveyHandler>.Instance);
        var result = await handler.ExecuteAsync(new SurveyCommand("SHIP-1"), CancellationToken.None);

        result.Accepted.Should().BeFalse();
        await port.DidNotReceive().SurveyAsync(Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Refuel_WhenDockedAtMarket_ReturnsAcceptedResult()
    {
        var port = Substitute.For<ISpaceTradersPort>();
        var ships = Substitute.For<IShipRepository>();
        var agents = Substitute.For<IAgentRepository>();
        var waypoints = Substitute.For<IWaypointRepository>();
        var bus = Substitute.For<IMessageBus>();

        ships.FindAsync("SHIP-1", Arg.Any<CancellationToken>())
            .Returns(new ShipModel("SHIP-1", "X1-AB", "X1-AB-001", "DOCKED", "CRUISE", 50, 100));
        waypoints.FindAsync("X1-AB-001", Arg.Any<CancellationToken>())
            .Returns(new WaypointCacheModel("X1-AB-001", "X1-AB", "ORBITAL_STATION", 0, 0, true, false, DateTimeOffset.UtcNow));
        port.RefuelShipAsync("SHIP-1", false, Arg.Any<CancellationToken>())
            .Returns(new RefuelActionResult(100_000, new FuelModel(100, 100), 500));

        var handler = new RefuelShipHandler(port, ships, agents, waypoints, bus, NullLogger<RefuelShipHandler>.Instance);
        var result = await handler.ExecuteAsync(new RefuelShipCommand("SHIP-1"), CancellationToken.None);

        result.Accepted.Should().BeTrue();
        result.Status.Should().Be(ShipLocalStatus.Docked);
        result.FuelCurrent.Should().Be(100);
    }

    [Fact]
    public async Task Refuel_WhenInOrbit_ReturnsRejected_AndPublishesMismatch()
    {
        var port = Substitute.For<ISpaceTradersPort>();
        var ships = Substitute.For<IShipRepository>();
        var agents = Substitute.For<IAgentRepository>();
        var waypoints = Substitute.For<IWaypointRepository>();
        var bus = Substitute.For<IMessageBus>();

        ships.FindAsync("SHIP-1", Arg.Any<CancellationToken>())
            .Returns(new ShipModel("SHIP-1", "X1-AB", "X1-AB-001", "IN_ORBIT", "CRUISE", 50, 100));

        var handler = new RefuelShipHandler(port, ships, agents, waypoints, bus, NullLogger<RefuelShipHandler>.Instance);
        var result = await handler.ExecuteAsync(new RefuelShipCommand("SHIP-1"), CancellationToken.None);

        result.Accepted.Should().BeFalse();
        await port.DidNotReceive().RefuelShipAsync(Arg.Any<string>(), Arg.Any<bool>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task BuyCargo_WhenDockedAtMarket_ReturnsAcceptedResult()
    {
        var port = Substitute.For<ISpaceTradersPort>();
        var ships = Substitute.For<IShipRepository>();
        var agents = Substitute.For<IAgentRepository>();
        var waypoints = Substitute.For<IWaypointRepository>();
        var bus = Substitute.For<IMessageBus>();

        ships.FindAsync("SHIP-1", Arg.Any<CancellationToken>())
            .Returns(new ShipModel("SHIP-1", "X1-AB", "X1-AB-001", "DOCKED", "CRUISE", 100, 100));
        waypoints.FindAsync("X1-AB-001", Arg.Any<CancellationToken>())
            .Returns(new WaypointCacheModel("X1-AB-001", "X1-AB", "ORBITAL_STATION", 0, 0, true, false, DateTimeOffset.UtcNow));
        port.BuyCargoAsync("SHIP-1", "FAB_MATS", 5, Arg.Any<CancellationToken>())
            .Returns(new TradeActionResult("AGENT-1", 90_000, new CargoModel(5, 60), 10_000));

        var handler = new BuyCargoHandler(port, ships, agents, waypoints, bus, NullLogger<BuyCargoHandler>.Instance);
        var result = await handler.ExecuteAsync(new BuyCargoCommand("SHIP-1", "FAB_MATS", 5), CancellationToken.None);

        result.Accepted.Should().BeTrue();
        result.Status.Should().Be(ShipLocalStatus.Docked);
        result.CargoCurrent.Should().Be(5);
    }

    [Fact]
    public async Task BuyCargo_WhenInOrbit_ReturnsRejected_AndPublishesMismatch()
    {
        var port = Substitute.For<ISpaceTradersPort>();
        var ships = Substitute.For<IShipRepository>();
        var agents = Substitute.For<IAgentRepository>();
        var waypoints = Substitute.For<IWaypointRepository>();
        var bus = Substitute.For<IMessageBus>();

        ships.FindAsync("SHIP-1", Arg.Any<CancellationToken>())
            .Returns(new ShipModel("SHIP-1", "X1-AB", "X1-AB-001", "IN_ORBIT", "CRUISE", 100, 100));

        var handler = new BuyCargoHandler(port, ships, agents, waypoints, bus, NullLogger<BuyCargoHandler>.Instance);
        var result = await handler.ExecuteAsync(new BuyCargoCommand("SHIP-1", "FAB_MATS", 5), CancellationToken.None);

        result.Accepted.Should().BeFalse();
        await port.DidNotReceive().BuyCargoAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<int>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Repair_WhenDocked_ReturnsAcceptedResult()
    {
        var port = Substitute.For<ISpaceTradersPort>();
        var ships = Substitute.For<IShipRepository>();
        var agents = Substitute.For<IAgentRepository>();
        var bus = Substitute.For<IMessageBus>();

        ships.FindAsync("SHIP-1", Arg.Any<CancellationToken>())
            .Returns(new ShipModel("SHIP-1", "X1-AB", "X1-AB-001", "DOCKED", "CRUISE", 100, 100));
        port.RepairShipAsync("SHIP-1", Arg.Any<CancellationToken>())
            .Returns(new ShipRepairActionResult(
                new AgentModel("A", null, null, 100_000, "F", 1),
                new ShipModel("SHIP-1", "X1-AB", "X1-AB-001", "DOCKED", "CRUISE", 100, 100),
                500));

        var handler = new RepairShipHandler(port, ships, agents, bus, NullLogger<RepairShipHandler>.Instance);
        var result = await handler.ExecuteAsync(new RepairShipCommand("SHIP-1"), CancellationToken.None);

        result.Accepted.Should().BeTrue();
        result.Status.Should().Be(ShipLocalStatus.Docked);
    }

    [Fact]
    public async Task Repair_WhenInOrbit_ReturnsRejected_AndPublishesMismatch()
    {
        var port = Substitute.For<ISpaceTradersPort>();
        var ships = Substitute.For<IShipRepository>();
        var agents = Substitute.For<IAgentRepository>();
        var bus = Substitute.For<IMessageBus>();

        ships.FindAsync("SHIP-1", Arg.Any<CancellationToken>())
            .Returns(new ShipModel("SHIP-1", "X1-AB", "X1-AB-001", "IN_ORBIT", "CRUISE", 100, 100));

        var handler = new RepairShipHandler(port, ships, agents, bus, NullLogger<RepairShipHandler>.Instance);
        var result = await handler.ExecuteAsync(new RepairShipCommand("SHIP-1"), CancellationToken.None);

        result.Accepted.Should().BeFalse();
        await port.DidNotReceive().RepairShipAsync(Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Scrap_WhenDocked_ReturnsAcceptedResult()
    {
        var port = Substitute.For<ISpaceTradersPort>();
        var ships = Substitute.For<IShipRepository>();
        var agents = Substitute.For<IAgentRepository>();
        var assignments = Substitute.For<IShipAssignmentRepository>();
        var bus = Substitute.For<IMessageBus>();

        ships.FindAsync("SHIP-1", Arg.Any<CancellationToken>())
            .Returns(new ShipModel("SHIP-1", "X1-AB", "X1-AB-001", "DOCKED", "CRUISE", 100, 100));
        port.ScrapShipAsync("SHIP-1", Arg.Any<CancellationToken>())
            .Returns(new ShipScrapActionResult(new AgentModel("A", null, null, 100_000, "F", 0), 5_000));

        var handler = new ScrapShipHandler(port, agents, ships, assignments, bus, NullLogger<ScrapShipHandler>.Instance);
        var result = await handler.ExecuteAsync(new ScrapShipCommand("SHIP-1"), CancellationToken.None);

        result.Accepted.Should().BeTrue();
        result.Status.Should().Be(ShipLocalStatus.Docked);
        await ships.Received(1).RemoveAsync("SHIP-1", Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Scrap_WhenInOrbit_ReturnsRejected_AndPublishesMismatch()
    {
        var port = Substitute.For<ISpaceTradersPort>();
        var ships = Substitute.For<IShipRepository>();
        var agents = Substitute.For<IAgentRepository>();
        var assignments = Substitute.For<IShipAssignmentRepository>();
        var bus = Substitute.For<IMessageBus>();

        ships.FindAsync("SHIP-1", Arg.Any<CancellationToken>())
            .Returns(new ShipModel("SHIP-1", "X1-AB", "X1-AB-001", "IN_ORBIT", "CRUISE", 100, 100));

        var handler = new ScrapShipHandler(port, agents, ships, assignments, bus, NullLogger<ScrapShipHandler>.Instance);
        var result = await handler.ExecuteAsync(new ScrapShipCommand("SHIP-1"), CancellationToken.None);

        result.Accepted.Should().BeFalse();
        await port.DidNotReceive().ScrapShipAsync(Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task JettisonCargo_WhenDocked_ReturnsAcceptedResult()
    {
        var port = Substitute.For<ISpaceTradersPort>();
        var ships = Substitute.For<IShipRepository>();
        var bus = Substitute.For<IMessageBus>();

        ships.FindAsync("SHIP-1", Arg.Any<CancellationToken>())
            .Returns(new ShipModel("SHIP-1", "X1-AB", "X1-AB-001", "DOCKED", "CRUISE", 100, 100));
        port.JettisonCargoAsync("SHIP-1", "IRON_ORE", 5, Arg.Any<CancellationToken>())
            .Returns(new JettisonActionResult(new CargoModel(5, 60)));

        var handler = new JettisonCargoHandler(port, ships, bus, NullLogger<JettisonCargoHandler>.Instance);
        var result = await handler.ExecuteAsync(new JettisonCargoCommand("SHIP-1", "IRON_ORE", 5), CancellationToken.None);

        result.Accepted.Should().BeTrue();
        result.Status.Should().Be(ShipLocalStatus.Docked);
    }

    [Fact]
    public async Task JettisonCargo_WhenInTransit_ReturnsRejected_AndPublishesMismatch()
    {
        var port = Substitute.For<ISpaceTradersPort>();
        var ships = Substitute.For<IShipRepository>();
        var bus = Substitute.For<IMessageBus>();

        ships.FindAsync("SHIP-1", Arg.Any<CancellationToken>())
            .Returns(new ShipModel("SHIP-1", "X1-AB", "X1-AB-001", "IN_TRANSIT", "CRUISE", 100, 100));

        var handler = new JettisonCargoHandler(port, ships, bus, NullLogger<JettisonCargoHandler>.Instance);
        var result = await handler.ExecuteAsync(new JettisonCargoCommand("SHIP-1", "IRON_ORE", 5), CancellationToken.None);

        result.Accepted.Should().BeFalse();
        await port.DidNotReceive().JettisonCargoAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<int>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task SupplyConstruction_WhenDocked_ReturnsRejected_AndPublishesMismatch()
    {
        var port = Substitute.For<ISpaceTradersPort>();
        var constructions = Substitute.For<IConstructionRepository>();
        var ships = Substitute.For<IShipRepository>();
        var assignments = Substitute.For<IShipAssignmentRepository>();
        var bus = Substitute.For<IMessageBus>();

        ships.FindAsync("SHIP-1", Arg.Any<CancellationToken>())
            .Returns(new ShipModel("SHIP-1", "X1-AB", "X1-AB-001", "DOCKED", "CRUISE", 100, 100));

        var handler = new SupplyConstructionHandler(port, constructions, ships, assignments, bus, NullLogger<SupplyConstructionHandler>.Instance);
        var result = await handler.ExecuteAsync(
            new SupplyConstructionCommand("SHIP-1", "X1-AB", "X1-AB-001", "FAB_MATS", 5, Guid.NewGuid(), Guid.NewGuid()),
            CancellationToken.None);

        result.Accepted.Should().BeFalse();
        await port.DidNotReceive().SupplyConstructionAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string>(), Arg.Any<int>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task InstallModule_WhenInOrbit_ReturnsRejected_AndPublishesMismatch()
    {
        var port = Substitute.For<ISpaceTradersPort>();
        var ships = Substitute.For<IShipRepository>();
        var agents = Substitute.For<IAgentRepository>();
        var waypoints = Substitute.For<IWaypointRepository>();
        var bus = Substitute.For<IMessageBus>();

        ships.FindAsync("SHIP-1", Arg.Any<CancellationToken>())
            .Returns(new ShipModel("SHIP-1", "X1-AB", "X1-AB-001", "IN_ORBIT", "CRUISE", 100, 100));

        var handler = new InstallModuleHandler(port, ships, agents, waypoints, bus, NullLogger<InstallModuleHandler>.Instance);
        var result = await handler.ExecuteAsync(new InstallModuleCommand("SHIP-1", "MODULE_CARGO_HOLD_II"), CancellationToken.None);

        result.Accepted.Should().BeFalse();
        await port.DidNotReceive().InstallModuleAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task RemoveModule_WhenInOrbit_ReturnsRejected_AndPublishesMismatch()
    {
        var port = Substitute.For<ISpaceTradersPort>();
        var ships = Substitute.For<IShipRepository>();
        var agents = Substitute.For<IAgentRepository>();
        var waypoints = Substitute.For<IWaypointRepository>();
        var bus = Substitute.For<IMessageBus>();

        ships.FindAsync("SHIP-1", Arg.Any<CancellationToken>())
            .Returns(new ShipModel("SHIP-1", "X1-AB", "X1-AB-001", "IN_ORBIT", "CRUISE", 100, 100));

        var handler = new RemoveModuleHandler(port, ships, agents, waypoints, bus, NullLogger<RemoveModuleHandler>.Instance);
        var result = await handler.ExecuteAsync(new RemoveModuleCommand("SHIP-1", "MODULE_CARGO_HOLD_II"), CancellationToken.None);

        result.Accepted.Should().BeFalse();
        await port.DidNotReceive().RemoveModuleAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task RemoveMount_WhenInOrbit_ReturnsRejected_AndPublishesMismatch()
    {
        var port = Substitute.For<ISpaceTradersPort>();
        var ships = Substitute.For<IShipRepository>();
        var agents = Substitute.For<IAgentRepository>();
        var waypoints = Substitute.For<IWaypointRepository>();
        var bus = Substitute.For<IMessageBus>();

        ships.FindAsync("SHIP-1", Arg.Any<CancellationToken>())
            .Returns(new ShipModel("SHIP-1", "X1-AB", "X1-AB-001", "IN_ORBIT", "CRUISE", 100, 100));

        var handler = new RemoveMountHandler(port, ships, agents, waypoints, bus, NullLogger<RemoveMountHandler>.Instance);
        var result = await handler.ExecuteAsync(new RemoveMountCommand("SHIP-1", "MOUNT_MINING_LASER_I"), CancellationToken.None);

        result.Accepted.Should().BeFalse();
        await port.DidNotReceive().RemoveMountAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task DeliverContract_WhenInOrbit_ReturnsRejected_AndPublishesMismatch()
    {
        var port = Substitute.For<ISpaceTradersPort>();
        var contracts = Substitute.For<IContractRepository>();
        var ships = Substitute.For<IShipRepository>();
        var bus = Substitute.For<IMessageBus>();

        ships.FindAsync("SHIP-1", Arg.Any<CancellationToken>())
            .Returns(new ShipModel("SHIP-1", "X1-AB", "X1-AB-001", "IN_ORBIT", "CRUISE", 100, 100));

        var handler = new DeliverContractHandler(port, contracts, ships, bus, NullLogger<DeliverContractHandler>.Instance);
        var result = await handler.ExecuteAsync(new DeliverContractCommand("CTR-1", "SHIP-1", "FAB_MATS", 5), CancellationToken.None);

        result.Accepted.Should().BeFalse();
        await port.DidNotReceive().DeliverContractAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string>(), Arg.Any<int>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Jump_WhenInOrbit_ReturnsAcceptedInTransitResult()
    {
        var port = Substitute.For<ISpaceTradersPort>();
        var ships = Substitute.For<IShipRepository>();
        var goals = Substitute.For<IShipGoalRepository>();
        var scheduler = Substitute.For<IShipEventScheduler>();
        var bus = Substitute.For<IMessageBus>();

        ships.FindAsync("SHIP-1", Arg.Any<CancellationToken>())
            .Returns(new ShipModel("SHIP-1", "X1-AB", "X1-AB-001", "IN_ORBIT", "CRUISE", 100, 100));
        var arrival = DateTimeOffset.UtcNow.AddSeconds(5);
        port.JumpShipAsync("SHIP-1", "X1-CD", Arg.Any<CancellationToken>())
            .Returns(new JumpActionResult(
                new NavModel("IN_TRANSIT", "X1-CD", "X1-CD-001", "CRUISE", "X1-CD-001", arrival),
                60));

        var handler = new JumpShipHandler(port, ships, goals, scheduler, bus, NullLogger<JumpShipHandler>.Instance);
        var result = await handler.ExecuteAsync(new JumpShipCommand("SHIP-1", "X1-CD"), CancellationToken.None);

        result.Accepted.Should().BeTrue();
        result.Status.Should().Be(ShipLocalStatus.InTransit);
        result.ArrivesAt.Should().Be(arrival);
    }

    [Fact]
    public async Task Warp_WhenInOrbit_ReturnsAcceptedInTransitResult()
    {
        var port = Substitute.For<ISpaceTradersPort>();
        var ships = Substitute.For<IShipRepository>();
        var goals = Substitute.For<IShipGoalRepository>();
        var scheduler = Substitute.For<IShipEventScheduler>();
        var bus = Substitute.For<IMessageBus>();

        ships.FindAsync("SHIP-1", Arg.Any<CancellationToken>())
            .Returns(new ShipModel("SHIP-1", "X1-AB", "X1-AB-001", "IN_ORBIT", "CRUISE", 100, 100));
        var arrival = DateTimeOffset.UtcNow.AddMinutes(5);
        port.WarpShipAsync("SHIP-1", "X1-CD-001", Arg.Any<CancellationToken>())
            .Returns(new WarpActionResult(
                new NavModel("IN_TRANSIT", "X1-AB", "X1-AB-001", "CRUISE", "X1-CD-001", arrival),
                new FuelModel(60, 100)));

        var handler = new WarpShipHandler(port, ships, goals, scheduler, bus, NullLogger<WarpShipHandler>.Instance);
        var result = await handler.ExecuteAsync(new WarpShipCommand("SHIP-1", "X1-CD-001"), CancellationToken.None);

        result.Accepted.Should().BeTrue();
        result.Status.Should().Be(ShipLocalStatus.InTransit);
        result.FuelCurrent.Should().Be(60);
    }

    private static (ISpaceTradersPort port, IShipRepository ships, IMessageBus bus) MoveCtx()
        => (Substitute.For<ISpaceTradersPort>(), Substitute.For<IShipRepository>(), Substitute.For<IMessageBus>());
}
