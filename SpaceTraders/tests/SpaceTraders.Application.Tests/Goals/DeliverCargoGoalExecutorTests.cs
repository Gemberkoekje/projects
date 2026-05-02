using FluentAssertions;
using NSubstitute;
using SpaceTraders.Application.Commands.Ships;
using SpaceTraders.Application.Events.Handlers.Ships;
using SpaceTraders.Application.Goals;
using SpaceTraders.Application.Goals.Executors;
using SpaceTraders.Application.Ports;
using SpaceTraders.Domain.Goals;
using Wolverine;

namespace SpaceTraders.Application.Tests.Goals;

/// <summary>
/// Phase 10: unit tests for <see cref="DeliverCargoGoalExecutor"/>.
/// </summary>
public sealed class DeliverCargoGoalExecutorTests
{
    private readonly IInOrbitCommandAcceptor _inOrbit = Substitute.For<IInOrbitCommandAcceptor>();
    private readonly IDockedCommandAcceptor _docked = Substitute.For<IDockedCommandAcceptor>();
    private readonly IMessageBus _bus = Substitute.For<IMessageBus>();

    private DeliverCargoGoalExecutor CreateExecutor() => new(_inOrbit, _docked, _bus);

    private static ShipModel Ship(
        string waypoint = "X1-AB-WP1",
        string status = "IN_ORBIT",
        IReadOnlyList<CargoItemModel>? inventory = null,
        int fuelCap = 100) =>
        new("SHIP-1", "X1-AB", waypoint, status, "CRUISE", 80, fuelCap,
            CargoInventory: inventory);

    private static DeliverCargoGoal Goal(string dest = "X1-AB-DEST", string tradeSymbol = "IRON_ORE") =>
        new() { ContractId = "CONTRACT-1", TradeSymbol = tradeSymbol, DeliveryWaypointSymbol = dest };

    [Fact]
    public void CanExecute_ReturnsTrue_ForDeliverCargoGoal()
    {
        CreateExecutor().CanExecute(Goal()).Should().BeTrue();
    }

    [Fact]
    public void CanExecute_ReturnsFalse_ForOtherGoal()
    {
        CreateExecutor().CanExecute(new IdleGoal()).Should().BeFalse();
    }

    [Fact]
    public async Task ExecuteStepAsync_InTransit_ReturnsWaitingForArrival()
    {
        var result = await CreateExecutor().ExecuteStepAsync(Ship(status: "IN_TRANSIT"), Goal(), new ShipGoalContext(), CancellationToken.None);

        result.Outcome.Should().Be(GoalExecutionOutcome.WaitingForArrival);
    }

    [Fact]
    public async Task ExecuteStepAsync_InOrbit_NotAtDest_Navigates()
    {
        var result = await CreateExecutor().ExecuteStepAsync(Ship("X1-AB-WP1"), Goal("X1-AB-DEST"), new ShipGoalContext(), CancellationToken.None);

        result.Outcome.Should().Be(GoalExecutionOutcome.Progressing);
        await _inOrbit.Received(1).NavigateAsync("SHIP-1", "X1-AB-DEST", Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ExecuteStepAsync_InOrbit_AtDest_NoCargo_DocksAndCompletes()
    {
        var result = await CreateExecutor().ExecuteStepAsync(Ship("X1-AB-DEST"), Goal("X1-AB-DEST"), new ShipGoalContext(), CancellationToken.None);

        result.Outcome.Should().Be(GoalExecutionOutcome.Completed);
        await _inOrbit.Received(1).DockAsync("SHIP-1", Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ExecuteStepAsync_InOrbit_AtDest_WithCargo_DocksAndDelivers()
    {
        var inventory = new List<CargoItemModel> { new("IRON_ORE", 15) };
        var ship = Ship("X1-AB-DEST", "IN_ORBIT", inventory);

        var result = await CreateExecutor().ExecuteStepAsync(ship, Goal("X1-AB-DEST"), new ShipGoalContext(), CancellationToken.None);

        result.Outcome.Should().Be(GoalExecutionOutcome.Progressing);
        await _inOrbit.Received(1).DockAsync("SHIP-1", Arg.Any<CancellationToken>());
        await _docked.Received(1).DeliverContractAsync(
            "CONTRACT-1", "SHIP-1", "IRON_ORE", 15, "X1-AB-DEST", Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ExecuteStepAsync_Docked_AtDest_WithCargo_Delivers()
    {
        var inventory = new List<CargoItemModel> { new("IRON_ORE", 15) };
        var ship = Ship("X1-AB-DEST", "DOCKED", inventory);

        var result = await CreateExecutor().ExecuteStepAsync(ship, Goal("X1-AB-DEST"), new ShipGoalContext(), CancellationToken.None);

        result.Outcome.Should().Be(GoalExecutionOutcome.Progressing);
        await _docked.Received(1).DeliverContractAsync(
            "CONTRACT-1", "SHIP-1", "IRON_ORE", 15, "X1-AB-DEST", Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ExecuteStepAsync_Docked_AtDest_NoCargo_Completes()
    {
        var ship = Ship("X1-AB-DEST", "DOCKED");
        var result = await CreateExecutor().ExecuteStepAsync(ship, Goal("X1-AB-DEST"), new ShipGoalContext(), CancellationToken.None);

        result.Outcome.Should().Be(GoalExecutionOutcome.Completed);
    }

    [Fact]
    public async Task ExecuteStepAsync_Docked_NotAtDest_Orbits()
    {
        var result = await CreateExecutor().ExecuteStepAsync(Ship("X1-AB-WP9", "DOCKED"), Goal("X1-AB-DEST"), new ShipGoalContext(), CancellationToken.None);

        result.Outcome.Should().Be(GoalExecutionOutcome.Progressing);
        await _docked.Received(1).OrbitAsync("SHIP-1", Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ExecuteStepAsync_NoFuelTank_PatchesDriftAndNavigates()
    {
        var result = await CreateExecutor().ExecuteStepAsync(Ship("X1-AB-WP1", fuelCap: 0), Goal("X1-AB-DEST"), new ShipGoalContext(), CancellationToken.None);

        result.Outcome.Should().Be(GoalExecutionOutcome.Progressing);
        await _bus.Received(1).InvokeAsync(Arg.Is<PatchShipNavCommand>(c => c.FlightMode == "DRIFT"), Arg.Any<CancellationToken>());
        await _inOrbit.Received(1).NavigateAsync("SHIP-1", Arg.Any<string>(), Arg.Any<CancellationToken>());
    }
}
