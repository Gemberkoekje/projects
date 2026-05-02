using FluentAssertions;
using NSubstitute;
using SpaceTraders.Application.Commands.Ships;
using SpaceTraders.Application.Events.Handlers.Ships;
using SpaceTraders.Application.Goals;
using SpaceTraders.Application.Goals.Executors;
using SpaceTraders.Application.Interfaces;
using SpaceTraders.Application.Ports;
using SpaceTraders.Domain.Goals;
using Wolverine;

namespace SpaceTraders.Application.Tests.Goals;

/// <summary>
/// Phase 10: unit tests for <see cref="SellCargoGoalExecutor"/>.
/// </summary>
public sealed class SellCargoGoalExecutorTests
{
    private readonly IInOrbitCommandAcceptor _inOrbit = Substitute.For<IInOrbitCommandAcceptor>();
    private readonly IDockedCommandAcceptor _docked = Substitute.For<IDockedCommandAcceptor>();
    private readonly IMessageBus _bus = Substitute.For<IMessageBus>();

    private SellCargoGoalExecutor CreateExecutor() => new(_inOrbit, _docked, _bus);

    private static ShipModel Ship(
        string waypoint = "X1-AB-WP1",
        string status = "IN_ORBIT",
        IReadOnlyList<CargoItemModel>? inventory = null,
        int fuelCap = 100) =>
        new("SHIP-1", "X1-AB", waypoint, status, "CRUISE", 80, fuelCap,
            CargoCurrent: inventory?.Sum(c => c.Units) ?? 0, CargoCapacity: 40,
            CargoInventory: inventory);

    private static SellCargoGoal Goal(string dest = "X1-AB-MKT", params string[] symbols) =>
        new() { DestinationWaypointSymbol = dest, TradeSymbols = symbols.Length > 0 ? [.. symbols] : ["IRON_ORE"] };

    [Fact]
    public void CanExecute_ReturnsTrue_ForSellCargoGoal()
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
        var result = await CreateExecutor().ExecuteStepAsync(Ship("X1-AB-WP1"), Goal("X1-AB-MKT"), new ShipGoalContext(), CancellationToken.None);

        result.Outcome.Should().Be(GoalExecutionOutcome.WaitingForArrival);
        await _inOrbit.Received(1).NavigateAsync("SHIP-1", "X1-AB-MKT", Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ExecuteStepAsync_InOrbit_AtDest_NoCargo_DocksAndCompletes()
    {
        var result = await CreateExecutor().ExecuteStepAsync(Ship("X1-AB-MKT"), Goal("X1-AB-MKT"), new ShipGoalContext(), CancellationToken.None);

        result.Outcome.Should().Be(GoalExecutionOutcome.Completed);
        await _inOrbit.Received(1).DockAsync("SHIP-1", Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ExecuteStepAsync_InOrbit_AtDest_WithCargo_DocksAndSells()
    {
        var inventory = new List<CargoItemModel> { new("IRON_ORE", 10) };
        var market = new MarketSnapshot("X1-AB-MKT", "X1-AB",
            [new("IRON_ORE", "EXCHANGE", 400, 500, 100, "MODERATE")], [], [], []);
        var ctx = new ShipGoalContext { CurrentMarketSnapshot = market };
        var ship = Ship("X1-AB-MKT", "IN_ORBIT", inventory);

        var result = await CreateExecutor().ExecuteStepAsync(ship, Goal("X1-AB-MKT", "IRON_ORE"), ctx, CancellationToken.None);

        result.Outcome.Should().Be(GoalExecutionOutcome.Completed);
        await _inOrbit.Received(1).DockAsync("SHIP-1", Arg.Any<CancellationToken>());
        await _docked.Received(1).SellCargoAsync("SHIP-1", "IRON_ORE", 10, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ExecuteStepAsync_Docked_AtDest_SellsCargo()
    {
        var inventory = new List<CargoItemModel> { new("IRON_ORE", 10) };
        var market = new MarketSnapshot("X1-AB-MKT", "X1-AB",
            [new("IRON_ORE", "EXCHANGE", 400, 500, 100, "MODERATE")], [], [], []);
        var ctx = new ShipGoalContext { CurrentMarketSnapshot = market };
        var ship = Ship("X1-AB-MKT", "DOCKED", inventory);

        var result = await CreateExecutor().ExecuteStepAsync(ship, Goal("X1-AB-MKT", "IRON_ORE"), ctx, CancellationToken.None);

        result.Outcome.Should().Be(GoalExecutionOutcome.Completed);
        await _docked.Received(1).SellCargoAsync("SHIP-1", "IRON_ORE", 10, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ExecuteStepAsync_Docked_AtDest_NoCargo_Completes()
    {
        var result = await CreateExecutor().ExecuteStepAsync(Ship("X1-AB-MKT", "DOCKED"), Goal("X1-AB-MKT"), new ShipGoalContext(), CancellationToken.None);

        result.Outcome.Should().Be(GoalExecutionOutcome.Completed);
    }

    [Fact]
    public async Task ExecuteStepAsync_Docked_NotAtDest_Orbits()
    {
        var result = await CreateExecutor().ExecuteStepAsync(Ship("X1-AB-WP1", "DOCKED"), Goal("X1-AB-MKT"), new ShipGoalContext(), CancellationToken.None);

        result.Outcome.Should().Be(GoalExecutionOutcome.WaitingForArrival);
        await _docked.Received(1).OrbitAsync("SHIP-1", Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ExecuteStepAsync_NoFuelTank_PatchesDriftAndNavigates()
    {
        var result = await CreateExecutor().ExecuteStepAsync(Ship("X1-AB-WP1", fuelCap: 0), Goal("X1-AB-MKT"), new ShipGoalContext(), CancellationToken.None);

        result.Outcome.Should().Be(GoalExecutionOutcome.WaitingForArrival);
        await _bus.Received(1).InvokeAsync(Arg.Is<PatchShipNavCommand>(c => c.FlightMode == "DRIFT"), Arg.Any<CancellationToken>());
        await _inOrbit.Received(1).NavigateAsync("SHIP-1", Arg.Any<string>(), Arg.Any<CancellationToken>());
    }
}
