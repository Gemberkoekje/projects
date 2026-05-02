using System.Text.Json;
using FluentAssertions;
using SpaceTraders.Domain.Enums;
using SpaceTraders.Domain.Goals;

namespace SpaceTraders.Domain.Tests.Goals;

public sealed class ShipGoalSerializationTests
{
    [Fact]
    public void IdleGoal_RoundTrip_PreservesGoalIdAndStatus()
    {
        var goalId = Guid.NewGuid();
        var goal = new IdleGoal { GoalId = goalId, Status = GoalStatus.Executing };

        var json = JsonSerializer.Serialize<ShipGoal>(goal);
        var result = JsonSerializer.Deserialize<ShipGoal>(json);

        result.Should().BeOfType<IdleGoal>();
        result!.GoalId.Should().Be(goalId);
        result.Status.Should().Be(GoalStatus.Executing);
        result.Kind.Should().Be(ShipGoalKind.Idle);
    }

    [Fact]
    public void MineResourceGoal_RoundTrip_PreservesAllFields()
    {
        var goalId = Guid.NewGuid();
        var goal = new MineResourceGoal
        {
            GoalId = goalId,
            Status = GoalStatus.Executing,
            TradeSymbol = "IRON_ORE",
            SourceWaypointSymbol = "X1-AB-A1",
        };

        var json = JsonSerializer.Serialize<ShipGoal>(goal);
        var result = JsonSerializer.Deserialize<ShipGoal>(json);

        result.Should().BeOfType<MineResourceGoal>();
        var mineGoal = (MineResourceGoal)result!;
        mineGoal.GoalId.Should().Be(goalId);
        mineGoal.Status.Should().Be(GoalStatus.Executing);
        mineGoal.TradeSymbol.Should().Be("IRON_ORE");
        mineGoal.SourceWaypointSymbol.Should().Be("X1-AB-A1");
        mineGoal.Kind.Should().Be(ShipGoalKind.MineResource);
    }

    [Fact]
    public void SiphonResourceGoal_RoundTrip_PreservesAllFields()
    {
        var goal = new SiphonResourceGoal
        {
            GoalId = Guid.NewGuid(),
            TradeSymbol = "HYDROCARBON",
            SourceWaypointSymbol = "X1-AB-GG1",
        };

        var json = JsonSerializer.Serialize<ShipGoal>(goal);
        var result = JsonSerializer.Deserialize<ShipGoal>(json);

        result.Should().BeOfType<SiphonResourceGoal>();
        var siphonGoal = (SiphonResourceGoal)result!;
        siphonGoal.TradeSymbol.Should().Be("HYDROCARBON");
        siphonGoal.SourceWaypointSymbol.Should().Be("X1-AB-GG1");
    }

    [Fact]
    public void SellCargoGoal_RoundTrip_PreservesTradeSymbolList()
    {
        var symbols = new[] { "IRON_ORE", "ALUMINUM_ORE" };
        var goal = new SellCargoGoal
        {
            GoalId = Guid.NewGuid(),
            DestinationWaypointSymbol = "X1-AB-01",
            TradeSymbols = symbols,
        };

        var json = JsonSerializer.Serialize<ShipGoal>(goal);
        var result = JsonSerializer.Deserialize<ShipGoal>(json);

        result.Should().BeOfType<SellCargoGoal>();
        var sellGoal = (SellCargoGoal)result!;
        sellGoal.DestinationWaypointSymbol.Should().Be("X1-AB-01");
        sellGoal.TradeSymbols.Should().BeEquivalentTo(symbols);
    }

    [Fact]
    public void DeliverCargoGoal_RoundTrip_PreservesAllFields()
    {
        var goal = new DeliverCargoGoal
        {
            GoalId = Guid.NewGuid(),
            ContractId = "contract-xyz",
            TradeSymbol = "EQUIPMENT",
            DeliveryWaypointSymbol = "X1-AB-02",
        };

        var json = JsonSerializer.Serialize<ShipGoal>(goal);
        var result = JsonSerializer.Deserialize<ShipGoal>(json);

        result.Should().BeOfType<DeliverCargoGoal>();
        var deliverGoal = (DeliverCargoGoal)result!;
        deliverGoal.ContractId.Should().Be("contract-xyz");
        deliverGoal.TradeSymbol.Should().Be("EQUIPMENT");
        deliverGoal.DeliveryWaypointSymbol.Should().Be("X1-AB-02");
    }

    [Fact]
    public void SupplyConstructionGoal_RoundTrip_PreservesAllFields()
    {
        var goal = new SupplyConstructionGoal
        {
            GoalId = Guid.NewGuid(),
            TradeSymbol = "ALUMINUM",
            ConstructionSiteWaypointSymbol = "X1-AB-JG",
        };

        var json = JsonSerializer.Serialize<ShipGoal>(goal);
        var result = JsonSerializer.Deserialize<ShipGoal>(json);

        result.Should().BeOfType<SupplyConstructionGoal>();
        var supplyGoal = (SupplyConstructionGoal)result!;
        supplyGoal.TradeSymbol.Should().Be("ALUMINUM");
        supplyGoal.ConstructionSiteWaypointSymbol.Should().Be("X1-AB-JG");
    }

    [Fact]
    public void MoveToWaypointGoal_RoundTrip_PreservesTargetWaypoint()
    {
        var goal = new MoveToWaypointGoal
        {
            GoalId = Guid.NewGuid(),
            TargetWaypointSymbol = "X1-AB-WP5",
        };

        var json = JsonSerializer.Serialize<ShipGoal>(goal);
        var result = JsonSerializer.Deserialize<ShipGoal>(json);

        result.Should().BeOfType<MoveToWaypointGoal>();
        ((MoveToWaypointGoal)result!).TargetWaypointSymbol.Should().Be("X1-AB-WP5");
    }

    [Fact]
    public void ScoutWaypointGoal_RoundTrip_PreservesTargetWaypoint()
    {
        var goal = new ScoutWaypointGoal
        {
            GoalId = Guid.NewGuid(),
            TargetWaypointSymbol = "X1-AB-WP7",
        };

        var json = JsonSerializer.Serialize<ShipGoal>(goal);
        var result = JsonSerializer.Deserialize<ShipGoal>(json);

        result.Should().BeOfType<ScoutWaypointGoal>();
        ((ScoutWaypointGoal)result!).TargetWaypointSymbol.Should().Be("X1-AB-WP7");
    }

    [Fact]
    public void PatrolMarketGoal_RoundTrip_PreservesTargetWaypoint()
    {
        var goal = new PatrolMarketGoal
        {
            GoalId = Guid.NewGuid(),
            TargetWaypointSymbol = "X1-AB-WP3",
        };

        var json = JsonSerializer.Serialize<ShipGoal>(goal);
        var result = JsonSerializer.Deserialize<ShipGoal>(json);

        result.Should().BeOfType<PatrolMarketGoal>();
        ((PatrolMarketGoal)result!).TargetWaypointSymbol.Should().Be("X1-AB-WP3");
    }

    [Fact]
    public void ShipGoal_DefaultGoalId_IsNotEmpty()
    {
        var goal = new IdleGoal();

        goal.GoalId.Should().NotBe(Guid.Empty);
    }

    [Fact]
    public void ShipGoal_DefaultStatus_IsAssigned()
    {
        var goal = new MineResourceGoal { TradeSymbol = "IRON_ORE", SourceWaypointSymbol = "X1-AB-A1" };

        goal.Status.Should().Be(GoalStatus.Assigned);
    }

    [Fact]
    public void ShipGoal_KindProperty_IsNotIncludedInJson()
    {
        var goal = new MineResourceGoal { TradeSymbol = "IRON_ORE", SourceWaypointSymbol = "X1-AB-A1" };

        var json = JsonSerializer.Serialize<ShipGoal>(goal);

        json.Should().NotContain("\"Kind\"");
    }
}
