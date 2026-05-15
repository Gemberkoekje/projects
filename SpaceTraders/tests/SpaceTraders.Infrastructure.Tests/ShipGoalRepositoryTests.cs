using FluentAssertions;
using SpaceTraders.Application.Ports;
using SpaceTraders.Domain.Enums;
using SpaceTraders.Domain.Goals;
using SpaceTraders.Infrastructure.Persistence.Repositories;

namespace SpaceTraders.Infrastructure.Tests;

[Trait("Category", "Integration")]
public sealed class ShipGoalRepositoryTests : IntegrationTestBase
{
    private async Task SeedShipAsync(string symbol)
    {
        var repo = new ShipRepository(Db);
        await repo.UpsertAsync(new ShipModel(
            Symbol: symbol,
            SystemSymbol: "X1-TEST",
            WaypointSymbol: "X1-TEST-WP1",
            Status: "IN_ORBIT",
            FlightMode: "CRUISE",
            FuelCurrent: 400,
            FuelCapacity: 400,
            ArrivesAt: null));
    }

    [SkippableFact]
    public async Task GetActiveGoalAsync_WhenNoGoalSet_ReturnsNull()
    {
        await SeedShipAsync("SHIP-G1");

        await using var fresh = CreateFreshContext();
        var repo = new ShipGoalRepository(fresh);

        var result = await repo.GetActiveGoalAsync("SHIP-G1");

        result.Should().BeNull();
    }

    [SkippableFact]
    public async Task SetAndGetActiveGoalAsync_MineResourceGoal_RoundTripsCorrectly()
    {
        await SeedShipAsync("SHIP-G2");
        var goalId = Guid.NewGuid();
        var goal = new MineResourceGoal
        {
            GoalId = goalId,
            Status = GoalStatus.Executing,
            TradeSymbol = "IRON_ORE",
            SourceWaypointSymbol = "X1-TEST-A1",
        };

        var setRepo = new ShipGoalRepository(Db);
        await setRepo.SetActiveGoalAsync("SHIP-G2", goal);

        await using var fresh = CreateFreshContext();
        var getRepo = new ShipGoalRepository(fresh);
        var result = await getRepo.GetActiveGoalAsync("SHIP-G2");

        result.Should().BeOfType<MineResourceGoal>();
        var mineResult = (MineResourceGoal)result!;
        mineResult.GoalId.Should().Be(goalId);
        mineResult.Status.Should().Be(GoalStatus.Executing);
        mineResult.TradeSymbol.Should().Be("IRON_ORE");
        mineResult.SourceWaypointSymbol.Should().Be("X1-TEST-A1");
    }

    [SkippableFact]
    public async Task SetAndGetActiveGoalAsync_IdleGoal_RoundTripsCorrectly()
    {
        await SeedShipAsync("SHIP-G3");
        var goalId = Guid.NewGuid();
        var goal = new IdleGoal { GoalId = goalId };

        var setRepo = new ShipGoalRepository(Db);
        await setRepo.SetActiveGoalAsync("SHIP-G3", goal);

        await using var fresh = CreateFreshContext();
        var getRepo = new ShipGoalRepository(fresh);
        var result = await getRepo.GetActiveGoalAsync("SHIP-G3");

        result.Should().BeOfType<IdleGoal>();
        result!.GoalId.Should().Be(goalId);
        result.Kind.Should().Be(ShipGoalKind.Idle);
    }

    [SkippableFact]
    public async Task SetAndGetActiveGoalAsync_SellCargoGoal_PreservesTradeSymbolList()
    {
        await SeedShipAsync("SHIP-G4");
        var goal = new SellCargoGoal
        {
            GoalId = Guid.NewGuid(),
            DestinationWaypointSymbol = "X1-TEST-MKT",
            TradeSymbols = ["IRON_ORE", "ALUMINUM_ORE"],
        };

        var setRepo = new ShipGoalRepository(Db);
        await setRepo.SetActiveGoalAsync("SHIP-G4", goal);

        await using var fresh = CreateFreshContext();
        var getRepo = new ShipGoalRepository(fresh);
        var result = await getRepo.GetActiveGoalAsync("SHIP-G4");

        result.Should().BeOfType<SellCargoGoal>();
        var sellResult = (SellCargoGoal)result!;
        sellResult.DestinationWaypointSymbol.Should().Be("X1-TEST-MKT");
        sellResult.TradeSymbols.Should().BeEquivalentTo(["IRON_ORE", "ALUMINUM_ORE"]);
    }

    [SkippableFact]
    public async Task ClearActiveGoalAsync_RemovesGoal()
    {
        await SeedShipAsync("SHIP-G5");
        var setRepo = new ShipGoalRepository(Db);
        await setRepo.SetActiveGoalAsync("SHIP-G5", new IdleGoal { GoalId = Guid.NewGuid() });

        await setRepo.ClearActiveGoalAsync("SHIP-G5");

        await using var fresh = CreateFreshContext();
        var getRepo = new ShipGoalRepository(fresh);
        var result = await getRepo.GetActiveGoalAsync("SHIP-G5");

        result.Should().BeNull();
    }

    [SkippableFact]
    public async Task UpdateGoalStatusAsync_ChangesStatusOfMatchingGoal()
    {
        await SeedShipAsync("SHIP-G6");
        var goalId = Guid.NewGuid();
        var setRepo = new ShipGoalRepository(Db);
        await setRepo.SetActiveGoalAsync("SHIP-G6", new MineResourceGoal
        {
            GoalId = goalId,
            Status = GoalStatus.Assigned,
            TradeSymbol = "BAUXITE",
            SourceWaypointSymbol = "X1-TEST-A2",
        });

        await setRepo.UpdateGoalStatusAsync("SHIP-G6", goalId, GoalStatus.Executing);

        await using var fresh = CreateFreshContext();
        var getRepo = new ShipGoalRepository(fresh);
        var result = await getRepo.GetActiveGoalAsync("SHIP-G6");

        result!.Status.Should().Be(GoalStatus.Executing);
    }

    [SkippableFact]
    public async Task UpdateGoalStatusAsync_DoesNothing_WhenGoalIdDoesNotMatch()
    {
        await SeedShipAsync("SHIP-G7");
        var goalId = Guid.NewGuid();
        var setRepo = new ShipGoalRepository(Db);
        await setRepo.SetActiveGoalAsync("SHIP-G7", new IdleGoal { GoalId = goalId, Status = GoalStatus.Assigned });

        var differentId = Guid.NewGuid();
        await setRepo.UpdateGoalStatusAsync("SHIP-G7", differentId, GoalStatus.Executing);

        await using var fresh = CreateFreshContext();
        var getRepo = new ShipGoalRepository(fresh);
        var result = await getRepo.GetActiveGoalAsync("SHIP-G7");

        result!.Status.Should().Be(GoalStatus.Assigned);
    }

    [SkippableFact]
    public async Task SetActiveGoalAsync_OverwritesPreviousGoal()
    {
        await SeedShipAsync("SHIP-G8");
        var setRepo = new ShipGoalRepository(Db);
        await setRepo.SetActiveGoalAsync("SHIP-G8", new IdleGoal { GoalId = Guid.NewGuid() });

        var newGoal = new MoveToWaypointGoal { GoalId = Guid.NewGuid(), TargetWaypointSymbol = "X1-TEST-WP2" };
        await setRepo.SetActiveGoalAsync("SHIP-G8", newGoal);

        await using var fresh = CreateFreshContext();
        var getRepo = new ShipGoalRepository(fresh);
        var result = await getRepo.GetActiveGoalAsync("SHIP-G8");

        result.Should().BeOfType<MoveToWaypointGoal>();
        ((MoveToWaypointGoal)result!).TargetWaypointSymbol.Should().Be("X1-TEST-WP2");
    }

    [SkippableFact]
    public async Task GetActiveTradeRouteTargetsAsync_ReturnsNormalizedTradeRouteTargets()
    {
        await SeedShipAsync("SHIP-G9");
        var setRepo = new ShipGoalRepository(Db);
        await setRepo.SetActiveGoalAsync("SHIP-G9", new TradeBetweenMarketsGoal
        {
            GoalId = Guid.NewGuid(),
            TradeSymbol = "food",
            BuyWaypointSymbol = "x1-test-buy",
            SellWaypointSymbol = "x1-test-sell",
        });

        await using var fresh = CreateFreshContext();
        var getRepo = new ShipGoalRepository(fresh);
        var result = await getRepo.GetActiveTradeRouteTargetsAsync();

        result.Should().Contain(("X1-TEST-BUY", "X1-TEST-SELL", "FOOD"));
    }
}
