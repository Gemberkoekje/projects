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
        result.GoalId.Should().Be(goalId);
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
        var mineGoal = (MineResourceGoal)result;
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
        var siphonGoal = (SiphonResourceGoal)result;
        siphonGoal.TradeSymbol.Should().Be("HYDROCARBON");
        siphonGoal.SourceWaypointSymbol.Should().Be("X1-AB-GG1");
    }

    [Fact]
    public void SiphonAndSellGoal_RoundTrip_PreservesAllFields()
    {
        // Slice 6.7: a siphon trip is stored with the ship, as a mining trip is.
        var goal = new SiphonAndSellGoal
        {
            GoalId = Guid.NewGuid(),
            TradeSymbol = "LIQUID_HYDROGEN",
            SourceWaypointSymbol = "X1-AB-GG1",
            SellWaypointSymbol = "X1-AB-G50",
            Selling = true,
        };

        var json = JsonSerializer.Serialize<ShipGoal>(goal);
        var result = JsonSerializer.Deserialize<ShipGoal>(json);

        result.Should().BeOfType<SiphonAndSellGoal>().Which.Should().BeEquivalentTo(goal);
        result.Kind.Should().Be(ShipGoalKind.SiphonAndSell);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void ATripThatDrifts_RoundTrip_KeepsItsDrift(bool siphon)
    {
        // Slice 6.10c (D45): a trip to a market out of the ship's CRUISE reach drifts there first, which takes hours; a
        // restart in between must not lose that.
        ShipGoal goal = siphon
            ? new SiphonAndSellGoal { TradeSymbol = "LIQUID_NITROGEN", SourceWaypointSymbol = "X1-AB-D90", SellWaypointSymbol = "X1-AB-F48", Drifting = true }
            : new MineAndSellGoal { TradeSymbol = "GOLD_ORE", SourceWaypointSymbol = "X1-AB-B14", SellWaypointSymbol = "X1-AB-B7", Drifting = true };

        var result = JsonSerializer.Deserialize<ShipGoal>(JsonSerializer.Serialize(goal));

        result.Should().BeOfType(goal.GetType()).And.BeEquivalentTo(goal, options => options.PreferringRuntimeMemberTypes());
    }

    [Fact]
    public void AMoveThatDrifts_RoundTrip_KeepsItsDrift()
    {
        // D54: the survey ship drifts to the area where most drones mine, for hours; a restart in between must not lose that.
        var goal = new MoveToWaypointGoal { TargetWaypointSymbol = "X1-AB-B7", Drifting = true };

        var result = JsonSerializer.Deserialize<ShipGoal>(JsonSerializer.Serialize<ShipGoal>(goal));

        result.Should().BeOfType<MoveToWaypointGoal>().Which.Should().BeEquivalentTo(goal);
        result.Kind.Should().Be(ShipGoalKind.MoveToWaypoint);
    }

    [Fact]
    public void ATripStoredBeforeItCouldDrift_LoadsWithoutADrift()
    {
        const string Stored = """{"$type":"MineAndSell","TradeSymbol":"COPPER_ORE","SourceWaypointSymbol":"X1-AB-XB5C","SellWaypointSymbol":"X1-AB-H51","Selling":false,"Earned":0,"Spent":0,"GoalId":"0f8fad5b-d9cb-469f-a165-70867728950e","Status":0,"StatusReason":null,"StartedAt":"2026-10-03T09:00:00+00:00"}""";

        JsonSerializer.Deserialize<ShipGoal>(Stored).Should().BeOfType<MineAndSellGoal>().Which.Drifting.Should().BeFalse();
    }

    [Fact]
    public void GatherAndSellGoal_RoundTrip_PreservesAllFields()
    {
        // Slice 6.8: a spare-time trip is stored with the ship, with the sale it has chosen.
        var goal = new GatherAndSellGoal
        {
            GoalId = Guid.NewGuid(),
            SourceWaypointSymbol = "X1-AB-GG1",
            Siphoning = true,
            Selling = true,
            SellTradeSymbol = "HYDROCARBON",
            SellWaypointSymbol = "X1-AB-G50",
        };

        var json = JsonSerializer.Serialize<ShipGoal>(goal);
        var result = JsonSerializer.Deserialize<ShipGoal>(json);

        result.Should().BeOfType<GatherAndSellGoal>().Which.Should().BeEquivalentTo(goal);
        result.Kind.Should().Be(ShipGoalKind.GatherAndSell);
    }

    /// <summary>The four trips, each with what it sold for and paid for cargo so far.</summary>
    public static TheoryData<TripGoal> Trips => new()
    {
        new TradeBetweenMarketsGoal { TradeSymbol = "EQUIPMENT", BuyWaypointSymbol = "X1-AB-K85", SellWaypointSymbol = "X1-AB-D41", CargoBought = true, Earned = 69_740, Spent = 65_080 },
        new MineAndSellGoal { TradeSymbol = "COPPER_ORE", SourceWaypointSymbol = "X1-AB-XB5C", SellWaypointSymbol = "X1-AB-H51", Selling = true, Earned = 1_005 },
        new SiphonAndSellGoal { TradeSymbol = "LIQUID_HYDROGEN", SourceWaypointSymbol = "X1-AB-C38", SellWaypointSymbol = "X1-AB-G50", Selling = true, Earned = 330 },
        new GatherAndSellGoal { SourceWaypointSymbol = "X1-AB-XB5C", Selling = true, Earned = 1_340 },
    };

    [Theory]
    [MemberData(nameof(Trips))]
    public void ATrip_RoundTrip_KeepsWhatItEarnedAndSpent(TripGoal trip)
    {
        // D46: a trip is booked when it ends, with what its sales brought in and its cargo cost, which it keeps with its goal.
        var json = JsonSerializer.Serialize<ShipGoal>(trip);
        var result = JsonSerializer.Deserialize<ShipGoal>(json);

        result.Should().BeOfType(trip.GetType()).And.BeEquivalentTo(trip, options => options.PreferringRuntimeMemberTypes());
    }

    [Fact]
    public void ATripStoredBeforeItKeptItsMoney_LoadsWithNothingEarnedOrSpent()
    {
        // D46: a trip stored before the goals kept what they earned and spent still loads.
        const string Stored = """{"$type":"TradeBetweenMarkets","TradeSymbol":"EQUIPMENT","BuyWaypointSymbol":"X1-AB-K85","SellWaypointSymbol":"X1-AB-D41","Units":20,"ExpectedProfit":4508,"FeedsTradeSymbol":"","CargoBought":true,"PricePaidPerUnit":3254,"SellWaypointChanged":false,"GoalId":"0f8fad5b-d9cb-469f-a165-70867728950e","Status":0,"StatusReason":null,"StartedAt":"2026-10-02T14:14:00+00:00"}""";

        var trip = JsonSerializer.Deserialize<ShipGoal>(Stored).Should().BeOfType<TradeBetweenMarketsGoal>().Subject;

        (trip.Earned, trip.Spent).Should().Be((0L, 0L));
        trip.PricePaidPerUnit.Should().Be(3_254);
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
        var sellGoal = (SellCargoGoal)result;
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
        var deliverGoal = (DeliverCargoGoal)result;
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
        var supplyGoal = (SupplyConstructionGoal)result;
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
        ((MoveToWaypointGoal)result).TargetWaypointSymbol.Should().Be("X1-AB-WP5");
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
        ((ScoutWaypointGoal)result).TargetWaypointSymbol.Should().Be("X1-AB-WP7");
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
        ((PatrolMarketGoal)result).TargetWaypointSymbol.Should().Be("X1-AB-WP3");
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
