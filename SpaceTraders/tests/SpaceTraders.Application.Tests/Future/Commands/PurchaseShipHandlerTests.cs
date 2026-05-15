using System.Text.Json;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using SpaceTraders.Application.Commands.Fleet;
using SpaceTraders.Application.Commands.Sync;
using SpaceTraders.Application.Interfaces.Repositories;
using SpaceTraders.Application.Orchestration;
using SpaceTraders.Application.Ports;
using Wolverine;

namespace SpaceTraders.Application.Tests.Commands;

/// <summary>
/// Phase 11c: unit tests for <see cref="PurchaseShipHandler"/> covering the
/// <see cref="AssignShipToGoalCommand"/> emission after a successful purchase.
/// </summary>
public sealed class PurchaseShipHandlerTests
{
    private readonly ISpaceTradersPort _port = Substitute.For<ISpaceTradersPort>();
    private readonly IAgentRepository _agents = Substitute.For<IAgentRepository>();
    private readonly IShipRepository _ships = Substitute.For<IShipRepository>();
    private readonly ISettingsRepository _settings = Substitute.For<ISettingsRepository>();
    private readonly IMessageBus _bus = Substitute.For<IMessageBus>();

    private const string ShipyardWaypoint = "X1-AB-SHIPYARD";
    private const string ShipType = "SHIP_MINING_DRONE";
    private const string NewShipSymbol = "AGENT-SHIP-2";

    private static readonly AgentModel Agent = new("AGENT-1", null, null, 500_000, "COSMIC", 1);

    private static readonly ShipyardDataModel ShipyardWithPrice = new(
        ShipyardWaypoint,
        "X1-AB",
        ShipTypesJson: null,
        ShipsDetailJson: JsonSerializer.Serialize(new[]
        {
            new { Type = ShipType, PurchasePrice = 100_000 },
        }));

    private static readonly PurchaseShipActionResult PurchaseResult = new(
        Agent: Agent,
        ShipSymbol: NewShipSymbol,
        ShipNav: new NavModel("DOCKED", "X1-AB", ShipyardWaypoint, "CRUISE", null, null),
        ShipFuel: new FuelModel(100, 100),
        Cost: 100_000);

    private PurchaseShipHandler CreateHandler() =>
        new(_port, _agents, _ships, _settings, _bus, NullLogger<PurchaseShipHandler>.Instance);

    private void SetupHappyPath()
    {
        _agents.GetAsync(Arg.Any<CancellationToken>()).Returns(Agent);
        _settings.GetAsync<long>("FleetExpansion.MinCreditReserve", Arg.Any<CancellationToken>()).Returns(100_000L);
        _port.GetShipyardAsync("X1-AB", ShipyardWaypoint, Arg.Any<CancellationToken>())
            .Returns(ShipyardWithPrice);
        _port.PurchaseShipAsync(ShipType, ShipyardWaypoint, Arg.Any<CancellationToken>())
            .Returns(PurchaseResult);
    }

    [Fact]
    public async Task Handle_WhenTargetGoalProvided_EmitsAssignShipToGoalCommand()
    {
        SetupHappyPath();

        var targetGoal = new FleetGoal(
            FleetGoalKind.Contract,
            "Deliver iron",
            Priority: 100,
            ContractId: "c1",
            TradeSymbol: "IRON_ORE",
            RemainingUnits: 50);

        var command = new PurchaseShipCommand(ShipType, ShipyardWaypoint, TargetGoal: targetGoal);

        await CreateHandler().Handle(command, CancellationToken.None);

        await _bus.Received(1).SendAsync(
            Arg.Is<AssignShipToGoalCommand>(c =>
                c.ShipSymbol == NewShipSymbol &&
                c.Goal.Kind == FleetGoalKind.Contract &&
                c.Goal.ContractId == "c1"),
            Arg.Any<DeliveryOptions>());
    }

    [Fact]
    public async Task Handle_WhenNoTargetGoal_DoesNotEmitAssignShipToGoalCommand()
    {
        SetupHappyPath();

        var command = new PurchaseShipCommand(ShipType, ShipyardWaypoint);

        await CreateHandler().Handle(command, CancellationToken.None);

        await _bus.DidNotReceive().SendAsync(
            Arg.Any<AssignShipToGoalCommand>(),
            Arg.Any<DeliveryOptions>());
    }

    [Fact]
    public async Task Handle_WhenNoAgent_DoesNotPurchaseOrAssign()
    {
        _agents.GetAsync(Arg.Any<CancellationToken>()).Returns((AgentModel?)null);

        var targetGoal = new FleetGoal(FleetGoalKind.Contract, "Deliver iron", 100);
        var command = new PurchaseShipCommand(ShipType, ShipyardWaypoint, TargetGoal: targetGoal);

        await CreateHandler().Handle(command, CancellationToken.None);

        await _port.DidNotReceive().PurchaseShipAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>());
        await _bus.DidNotReceive().SendAsync(Arg.Any<AssignShipToGoalCommand>(), Arg.Any<DeliveryOptions>());
    }

    [Fact]
    public async Task Handle_WhenCreditsBelowReserve_DoesNotPurchaseOrAssign()
    {
        var poorAgent = new AgentModel("AGENT-1", null, null, 150_000, "COSMIC", 1);
        _agents.GetAsync(Arg.Any<CancellationToken>()).Returns(poorAgent);
        _settings.GetAsync<long>("FleetExpansion.MinCreditReserve", Arg.Any<CancellationToken>()).Returns(100_000L);
        _port.GetShipyardAsync("X1-AB", ShipyardWaypoint, Arg.Any<CancellationToken>())
            .Returns(ShipyardWithPrice);

        var targetGoal = new FleetGoal(FleetGoalKind.Contract, "Deliver iron", 100);
        var command = new PurchaseShipCommand(ShipType, ShipyardWaypoint, TargetGoal: targetGoal);

        await CreateHandler().Handle(command, CancellationToken.None);

        await _port.DidNotReceive().PurchaseShipAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>());
        await _bus.DidNotReceive().SendAsync(Arg.Any<AssignShipToGoalCommand>(), Arg.Any<DeliveryOptions>());
    }
}
