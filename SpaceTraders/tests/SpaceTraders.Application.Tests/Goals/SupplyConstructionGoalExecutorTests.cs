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
/// Phase 10: unit tests for <see cref="SupplyConstructionGoalExecutor"/>.
/// </summary>
public sealed class SupplyConstructionGoalExecutorTests
{
    private readonly IInOrbitCommandAcceptor _inOrbit = Substitute.For<IInOrbitCommandAcceptor>();
    private readonly IDockedCommandAcceptor _docked = Substitute.For<IDockedCommandAcceptor>();
    private readonly IMessageBus _bus = Substitute.For<IMessageBus>();

    private SupplyConstructionGoalExecutor CreateExecutor() => new(_inOrbit, _docked, _bus);

    private static ShipModel Ship(
        string waypoint = "X1-AB-WP1",
        string status = "IN_ORBIT",
        IReadOnlyList<CargoItemModel>? inventory = null,
        int fuelCap = 100) =>
        new("SHIP-1", "X1-AB", waypoint, status, "CRUISE", 80, fuelCap,
            CargoInventory: inventory);

    private static SupplyConstructionGoal Goal(string site = "X1-AB-SITE") =>
        new() { ConstructionSiteWaypointSymbol = site, TradeSymbol = "IRON_ORE" };

    [Fact]
    public void CanExecute_ReturnsTrue_ForSupplyConstructionGoal()
    {
        CreateExecutor().CanExecute(Goal()).Should().BeTrue();
    }

    [Fact]
    public void CanExecute_ReturnsFalse_ForOtherGoal()
    {
        CreateExecutor().CanExecute(new IdleGoal()).Should().BeFalse();
    }

    [Fact]
    public async Task ExecuteStepAsync_ConstructionComplete_Completes()
    {
        var ctx = new ShipGoalContext { ConstructionComplete = true };
        var result = await CreateExecutor().ExecuteStepAsync(Ship(), Goal(), ctx, CancellationToken.None);

        result.Outcome.Should().Be(GoalExecutionOutcome.Completed);
        result.Reason.Should().Contain("already complete");
    }

    [Fact]
    public async Task ExecuteStepAsync_InTransit_ReturnsWaitingForArrival()
    {
        var result = await CreateExecutor().ExecuteStepAsync(Ship(status: "IN_TRANSIT"), Goal(), new ShipGoalContext(), CancellationToken.None);

        result.Outcome.Should().Be(GoalExecutionOutcome.WaitingForArrival);
    }

    [Fact]
    public async Task ExecuteStepAsync_InOrbit_NotAtSite_Navigates()
    {
        var result = await CreateExecutor().ExecuteStepAsync(Ship("X1-AB-WP1"), Goal("X1-AB-SITE"), new ShipGoalContext(), CancellationToken.None);

        result.Outcome.Should().Be(GoalExecutionOutcome.Progressing);
        await _inOrbit.Received(1).NavigateAsync("SHIP-1", "X1-AB-SITE", Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ExecuteStepAsync_InOrbit_AtSite_WithCargo_Supplies()
    {
        var inventory = new List<CargoItemModel> { new("IRON_ORE", 20) };
        var ship = Ship("X1-AB-SITE", "IN_ORBIT", inventory);

        var result = await CreateExecutor().ExecuteStepAsync(ship, Goal("X1-AB-SITE"), new ShipGoalContext(), CancellationToken.None);

        result.Outcome.Should().Be(GoalExecutionOutcome.Progressing);
        await _inOrbit.Received(1).SupplyConstructionAsync(
            "SHIP-1", "X1-AB", "X1-AB-SITE", "IRON_ORE", 20,
            Arg.Any<Guid>(), Arg.Any<Guid>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ExecuteStepAsync_InOrbit_AtSite_NoCargo_Completes()
    {
        var ship = Ship("X1-AB-SITE", "IN_ORBIT");
        var result = await CreateExecutor().ExecuteStepAsync(ship, Goal("X1-AB-SITE"), new ShipGoalContext(), CancellationToken.None);

        result.Outcome.Should().Be(GoalExecutionOutcome.Completed);
    }

    [Fact]
    public async Task ExecuteStepAsync_Docked_AtSite_NoCargo_OrbitsAndCompletes()
    {
        var ship = Ship("X1-AB-SITE", "DOCKED");
        var result = await CreateExecutor().ExecuteStepAsync(ship, Goal("X1-AB-SITE"), new ShipGoalContext(), CancellationToken.None);

        result.Outcome.Should().Be(GoalExecutionOutcome.Completed);
        await _docked.Received(1).OrbitAsync("SHIP-1", Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ExecuteStepAsync_Docked_AtSite_WithCargo_OrbitsAndSupplies()
    {
        var inventory = new List<CargoItemModel> { new("IRON_ORE", 20) };
        var ship = Ship("X1-AB-SITE", "DOCKED", inventory);

        var result = await CreateExecutor().ExecuteStepAsync(ship, Goal("X1-AB-SITE"), new ShipGoalContext(), CancellationToken.None);

        result.Outcome.Should().Be(GoalExecutionOutcome.Progressing);
        await _docked.Received(1).OrbitAsync("SHIP-1", Arg.Any<CancellationToken>());
        await _inOrbit.Received(1).SupplyConstructionAsync(
            "SHIP-1", "X1-AB", "X1-AB-SITE", "IRON_ORE", 20,
            Arg.Any<Guid>(), Arg.Any<Guid>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ExecuteStepAsync_Docked_NotAtSite_Orbits()
    {
        var result = await CreateExecutor().ExecuteStepAsync(Ship("X1-AB-WP1", "DOCKED"), Goal("X1-AB-SITE"), new ShipGoalContext(), CancellationToken.None);

        result.Outcome.Should().Be(GoalExecutionOutcome.Progressing);
        await _docked.Received(1).OrbitAsync("SHIP-1", Arg.Any<CancellationToken>());
    }
}
