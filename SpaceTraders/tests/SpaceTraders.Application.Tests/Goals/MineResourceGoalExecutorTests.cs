using FluentAssertions;
using NSubstitute;
using SpaceTraders.Application.Events.Handlers.Ships;
using SpaceTraders.Application.Goals;
using SpaceTraders.Application.Goals.Executors;
using SpaceTraders.Application.Interfaces;
using SpaceTraders.Application.Ports;
using SpaceTraders.Application.Services;
using SpaceTraders.Domain.Goals;
using Wolverine;

namespace SpaceTraders.Application.Tests.Goals;

/// <summary>
/// Phase 10: unit tests for <see cref="MineResourceGoalExecutor"/>.
/// </summary>
public sealed class MineResourceGoalExecutorTests
{
    private readonly IInOrbitCommandAcceptor _inOrbit = Substitute.For<IInOrbitCommandAcceptor>();
    private readonly IDockedCommandAcceptor _docked = Substitute.For<IDockedCommandAcceptor>();
    private readonly IMessageBus _bus = Substitute.For<IMessageBus>();
    private readonly IShipCapabilityRegistry _capabilityRegistry = new ShipCapabilityRegistry();

    private MineResourceGoalExecutor CreateExecutor() => new(_inOrbit, _docked, _bus, _capabilityRegistry);

    private static ShipModel Ship(
        string waypoint = "X1-AB-AST",
        string status = "IN_ORBIT",
        int cargo = 0,
        int capacity = 40,
        IReadOnlyList<CargoItemModel>? inventory = null,
        DateTimeOffset? cooldown = null,
        IReadOnlyList<string>? mounts = null,
        string shipType = "SHIP_MINING_DRONE") =>
        new("SHIP-1", "X1-AB", waypoint, status, "CRUISE", 80, 100,
            CargoCurrent: cargo, CargoCapacity: capacity, CargoInventory: inventory,
            MountSymbols: mounts, CooldownExpiresAt: cooldown, ShipType: shipType);

    private static MineResourceGoal Goal(string source = "X1-AB-AST") =>
        new() { SourceWaypointSymbol = source, TradeSymbol = "IRON_ORE" };

    private static ShipGoalContext Ctx(string? nearestSell = null) => new() { NearestSellMarket = nearestSell };

    [Fact]
    public void CanExecute_ReturnsTrue_ForMineResourceGoal()
    {
        CreateExecutor().CanExecute(Goal()).Should().BeTrue();
    }

    [Fact]
    public void CanExecute_ReturnsFalse_ForOtherGoal()
    {
        CreateExecutor().CanExecute(new IdleGoal()).Should().BeFalse();
    }

    [Fact]
    public async Task ExecuteStepAsync_NoMiningEquipment_Blocked()
    {
        var ship = Ship(mounts: ["MOUNT_SENSOR_ARRAY_I"], shipType: "SHIP_PROBE");
        var result = await CreateExecutor().ExecuteStepAsync(ship, Goal(), Ctx(), CancellationToken.None);

        result.Outcome.Should().Be(GoalExecutionOutcome.Blocked);
        result.Reason.Should().Contain("no mining equipment");
    }

    [Fact]
    public async Task ExecuteStepAsync_InTransit_ReturnsWaitingForArrival()
    {
        var result = await CreateExecutor().ExecuteStepAsync(Ship(status: "IN_TRANSIT"), Goal(), Ctx(), CancellationToken.None);

        result.Outcome.Should().Be(GoalExecutionOutcome.WaitingForArrival);
    }

    [Fact]
    public async Task ExecuteStepAsync_AtSource_OnCooldown_ReturnsWaitingForCooldown()
    {
        var cooldown = DateTimeOffset.UtcNow.AddMinutes(1);
        var result = await CreateExecutor().ExecuteStepAsync(Ship(cooldown: cooldown), Goal(), Ctx(), CancellationToken.None);

        result.Outcome.Should().Be(GoalExecutionOutcome.WaitingForCooldown);
        result.CooldownExpiresAt.Should().Be(cooldown);
    }

    [Fact]
    public async Task ExecuteStepAsync_AtSource_CargoFull_NavigatesToSellMarket()
    {
        var ship = Ship(cargo: 40, capacity: 40);
        var result = await CreateExecutor().ExecuteStepAsync(ship, Goal(), Ctx(nearestSell: "X1-AB-MKT"), CancellationToken.None);

        result.Outcome.Should().Be(GoalExecutionOutcome.Progressing);
        await _inOrbit.Received(1).NavigateAsync("SHIP-1", "X1-AB-MKT", Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ExecuteStepAsync_AtSource_CargoFull_NoMarket_Blocked()
    {
        var ship = Ship(cargo: 40, capacity: 40);
        var result = await CreateExecutor().ExecuteStepAsync(ship, Goal(), Ctx(), CancellationToken.None);

        result.Outcome.Should().Be(GoalExecutionOutcome.Blocked);
    }

    [Fact]
    public async Task ExecuteStepAsync_AtSource_ReadyToExtract_Extracts()
    {
        var result = await CreateExecutor().ExecuteStepAsync(Ship(cargo: 5, capacity: 40), Goal(), Ctx(), CancellationToken.None);

        result.Outcome.Should().Be(GoalExecutionOutcome.Progressing);
        await _inOrbit.Received(1).ExtractAsync("SHIP-1", Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ExecuteStepAsync_NotAtSource_InOrbit_Navigates()
    {
        var result = await CreateExecutor().ExecuteStepAsync(Ship("X1-AB-WP9"), Goal("X1-AB-AST"), Ctx(), CancellationToken.None);

        result.Outcome.Should().Be(GoalExecutionOutcome.Progressing);
        await _inOrbit.Received(1).NavigateAsync("SHIP-1", "X1-AB-AST", Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ExecuteStepAsync_Docked_SellsCargo_WhenMarketAvailable()
    {
        var inventory = new List<CargoItemModel> { new("IRON_ORE", 10) };
        var market = new MarketSnapshot("X1-AB-MKT", "X1-AB",
            [new("IRON_ORE", "EXCHANGE", 400, 500, 100, "MODERATE")], [], [], []);
        var ctx = new ShipGoalContext { CurrentMarketSnapshot = market, MiningMinimumSellPrice = 0 };
        var ship = Ship("X1-AB-MKT", "DOCKED", 10, 40, inventory);

        var result = await CreateExecutor().ExecuteStepAsync(ship, Goal("X1-AB-AST"), ctx, CancellationToken.None);

        result.Outcome.Should().Be(GoalExecutionOutcome.Progressing);
        await _docked.Received(1).SellCargoAsync("SHIP-1", "IRON_ORE", 10, Arg.Any<CancellationToken>());
    }
}
