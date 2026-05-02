using FluentAssertions;
using NSubstitute;
using SpaceTraders.Application.Events.Handlers.Ships;
using SpaceTraders.Application.Goals;
using SpaceTraders.Application.Goals.Executors;
using SpaceTraders.Application.Interfaces;
using SpaceTraders.Application.Ports;
using SpaceTraders.Domain.Goals;
using Wolverine;

namespace SpaceTraders.Application.Tests.Goals;

/// <summary>
/// Phase 10: unit tests for <see cref="SiphonResourceGoalExecutor"/>.
/// </summary>
public sealed class SiphonResourceGoalExecutorTests
{
    private readonly IInOrbitCommandAcceptor _inOrbit = Substitute.For<IInOrbitCommandAcceptor>();
    private readonly IDockedCommandAcceptor _docked = Substitute.For<IDockedCommandAcceptor>();
    private readonly IMessageBus _bus = Substitute.For<IMessageBus>();

    private SiphonResourceGoalExecutor CreateExecutor() => new(_inOrbit, _docked, _bus);

    private static ShipModel Ship(
        string waypoint = "X1-AB-GAS",
        string status = "IN_ORBIT",
        int cargo = 0,
        int capacity = 40,
        DateTimeOffset? cooldown = null,
        IReadOnlyList<CargoItemModel>? inventory = null,
        string shipType = "SHIP_SIPHON_DRONE") =>
        new("SHIP-1", "X1-AB", waypoint, status, "CRUISE", 80, 100,
            CargoCurrent: cargo, CargoCapacity: capacity, CargoInventory: inventory,
            CooldownExpiresAt: cooldown, ShipType: shipType);

    private static SiphonResourceGoal Goal(string source = "X1-AB-GAS") =>
        new() { SourceWaypointSymbol = source, TradeSymbol = "HYDROCARBON" };

    [Fact]
    public void CanExecute_ReturnsTrue_ForSiphonResourceGoal()
    {
        CreateExecutor().CanExecute(Goal()).Should().BeTrue();
    }

    [Fact]
    public void CanExecute_ReturnsFalse_ForOtherGoal()
    {
        CreateExecutor().CanExecute(new IdleGoal()).Should().BeFalse();
    }

    [Fact]
    public async Task ExecuteStepAsync_NoSiphonEquipment_Blocked()
    {
        var ship = Ship(shipType: "SHIP_PROBE");
        var result = await CreateExecutor().ExecuteStepAsync(ship, Goal(), new ShipGoalContext(), CancellationToken.None);

        result.Outcome.Should().Be(GoalExecutionOutcome.Blocked);
        result.Reason.Should().Contain("no gas siphon equipment");
    }

    [Fact]
    public async Task ExecuteStepAsync_InTransit_ReturnsWaitingForArrival()
    {
        var result = await CreateExecutor().ExecuteStepAsync(Ship(status: "IN_TRANSIT"), Goal(), new ShipGoalContext(), CancellationToken.None);

        result.Outcome.Should().Be(GoalExecutionOutcome.WaitingForArrival);
    }

    [Fact]
    public async Task ExecuteStepAsync_AtSource_OnCooldown_ReturnsWaitingForCooldown()
    {
        var cooldown = DateTimeOffset.UtcNow.AddMinutes(1);
        var result = await CreateExecutor().ExecuteStepAsync(Ship(cooldown: cooldown), Goal(), new ShipGoalContext(), CancellationToken.None);

        result.Outcome.Should().Be(GoalExecutionOutcome.WaitingForCooldown);
    }

    [Fact]
    public async Task ExecuteStepAsync_AtSource_ReadyToSiphon_Siphons()
    {
        var result = await CreateExecutor().ExecuteStepAsync(Ship(cargo: 5, capacity: 40), Goal(), new ShipGoalContext(), CancellationToken.None);

        result.Outcome.Should().Be(GoalExecutionOutcome.Progressing);
        await _inOrbit.Received(1).SiphonAsync("SHIP-1", Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ExecuteStepAsync_AtSource_CargoFull_NavigatesToSellMarket()
    {
        var ship = Ship(cargo: 40, capacity: 40);
        var ctx = new ShipGoalContext { NearestSellMarket = "X1-AB-MKT" };
        var result = await CreateExecutor().ExecuteStepAsync(ship, Goal(), ctx, CancellationToken.None);

        result.Outcome.Should().Be(GoalExecutionOutcome.Progressing);
        await _inOrbit.Received(1).NavigateAsync("SHIP-1", "X1-AB-MKT", Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ExecuteStepAsync_AtSource_CargoFull_NoMarket_Blocked()
    {
        var ship = Ship(cargo: 40, capacity: 40);
        var result = await CreateExecutor().ExecuteStepAsync(ship, Goal(), new ShipGoalContext(), CancellationToken.None);

        result.Outcome.Should().Be(GoalExecutionOutcome.Blocked);
    }

    [Fact]
    public async Task ExecuteStepAsync_NotAtSource_Navigates()
    {
        var result = await CreateExecutor().ExecuteStepAsync(Ship("X1-AB-WP9"), Goal("X1-AB-GAS"), new ShipGoalContext(), CancellationToken.None);

        result.Outcome.Should().Be(GoalExecutionOutcome.Progressing);
        await _inOrbit.Received(1).NavigateAsync("SHIP-1", "X1-AB-GAS", Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ExecuteStepAsync_Docked_SellsCargo_WhenMarketAvailable()
    {
        var inventory = new List<CargoItemModel> { new("HYDROCARBON", 10) };
        var market = new MarketSnapshot("X1-AB-MKT", "X1-AB",
            [new("HYDROCARBON", "EXCHANGE", 400, 500, 100, "MODERATE")], [], [], []);
        var ctx = new ShipGoalContext { CurrentMarketSnapshot = market, MiningMinimumSellPrice = 0 };
        var ship = Ship("X1-AB-MKT", "DOCKED", 10, 40, inventory: inventory);

        var result = await CreateExecutor().ExecuteStepAsync(ship, Goal("X1-AB-GAS"), ctx, CancellationToken.None);

        result.Outcome.Should().Be(GoalExecutionOutcome.Progressing);
        await _docked.Received(1).SellCargoAsync("SHIP-1", "HYDROCARBON", 10, Arg.Any<CancellationToken>());
    }
}
