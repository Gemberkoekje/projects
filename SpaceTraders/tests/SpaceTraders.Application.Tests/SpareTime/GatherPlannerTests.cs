using FluentAssertions;
using SpaceTraders.Application.Ports;
using SpaceTraders.Application.SpareTime;
using SpaceTraders.Domain.Enums;
using static SpaceTraders.Application.Tests.SpareTime.SpareTimeFixture;

namespace SpaceTraders.Application.Tests.SpareTime;

/// <summary>
/// Slice 6.8: in its spare time the command ship mines or siphons at the nearest place it can (D35), keeping whatever
/// a market buys, and sells each good where it fetches most after fuel (D36). The trading plan foresees where selling
/// the hold leaves it.
/// </summary>
public sealed class GatherPlannerTests
{
    [Fact]
    public void AtTheAsteroid_ItGathersRightThere()
    {
        GatherPlanner.TryFindSource(Map(), CommandShip(XB5C), out var source).Should().BeTrue();

        source.Should().Be(new GatherSource(XB5C, Siphoning: false, 0));
    }

    [Fact]
    public void AfterASale_ItGathersAtTheNearestSource_NotAtTheGasGiantFarAway()
    {
        // D35: from H51, XB5C is 19 away, C38 187.
        GatherPlanner.TryFindSource(Map(), CommandShip(H51, "DOCKED"), out var source).Should().BeTrue();

        (source.WaypointSymbol, source.Siphoning).Should().Be((XB5C, false));
    }

    [Fact]
    public void AtTheGasStation_ItSiphonsAtTheGasGiantThere()
    {
        // C39 is the station at C38; XB5C is 169 away.
        GatherPlanner.TryFindSource(Map(), CommandShip(C39, "DOCKED"), out var source).Should().BeTrue();

        (source.WaypointSymbol, source.Siphoning).Should().Be((C38, true));
    }

    [Fact]
    public void WithoutAGasSiphon_ItNeverSiphons()
    {
        var ship = CommandShip(C39, "DOCKED") with { MountSymbols = ["MOUNT_MINING_LASER_II", "MOUNT_SURVEYOR_II"] };

        GatherPlanner.TryFindSource(Map(), ship, out var source).Should().BeTrue();

        (source.WaypointSymbol, source.Siphoning).Should().Be((XB5C, false));
    }

    [Fact]
    public void ASurveyorMount_IsNoMiningLaser()
    {
        // ShipModel.HasMiningEquipment counts a surveyor mount; it can't extract.
        var ship = CommandShip(H51, "DOCKED") with { MountSymbols = ["MOUNT_GAS_SIPHON_II", "MOUNT_SURVEYOR_II"] };

        GatherPlanner.TryFindSource(Map(), ship, out var source).Should().BeTrue();

        (source.WaypointSymbol, source.Siphoning).Should().Be((C38, true));
    }

    [Fact]
    public void AnAsteroidWhoseGoodsNoMarketBuys_IsPassedOver()
    {
        // I60 is nearer H51 than XB5C, but no market here buys ICE_WATER or AMMONIA_ICE.
        GatherPlanner.TryFindSource(Map(), CommandShip(H51, "DOCKED"), out var source).Should().BeTrue();

        source.WaypointSymbol.Should().Be(XB5C);
        GatherPlanner.YieldsSellable(Map(), CommandShip(H51), I60).Should().BeFalse();
        GatherPlanner.YieldsSellable(Map(), CommandShip(H51), XB5C).Should().BeTrue();
    }

    [Fact]
    public void ASourceItCantReach_IsPassedOver()
    {
        // A siphon and a 30-unit tank: no chain of fuel markets 30 apart leads from H51 to C38.
        var ship = new ShipModel("SHIP-9", SystemSymbol, H51, "DOCKED", "CRUISE", 30, 30, CargoCapacity: 15, MountSymbols: ["MOUNT_GAS_SIPHON_I", "MOUNT_SURVEYOR_I"]);

        GatherPlanner.TryFindSource(Map(), ship, out _).Should().BeFalse();
    }

    [Fact]
    public void AShipWithNeitherALaserNorASiphon_HasNoSource()
    {
        var ship = CommandShip() with { MountSymbols = ["MOUNT_SURVEYOR_II"] };

        GatherPlanner.TryFindSource(Map(), ship, out _).Should().BeFalse();
    }

    [Fact]
    public void AWaypointOffTheMap_YieldsNothingSellable()
        => GatherPlanner.YieldsSellable(Map(), CommandShip(), "X1-DC53-ZZ9").Should().BeFalse();

    [Fact]
    public void SellingTheHold_SellsEachGoodWhereItFetchesMost_AndEndsAtTheLastSale()
    {
        // D36. From XB5C, 10 copper fetch 670 at H51, less 95 for the fuel there; 10 quartz 260 at F49, less 82.
        // Copper first; then the quartz, from H51.
        var ship = CommandShip(cargo: [new CargoItemModel("QUARTZ_SAND", 10), new CargoItemModel("COPPER_ORE", 10)]);

        var after = GatherPlanner.AfterSellingHold(Map(), ship);

        after.WaypointSymbol.Should().Be(F49);
        after.LocalStatus.Should().Be(ShipLocalStatus.Docked);
        after.CargoInventory.Should().BeEmpty();
        after.CargoCurrent.Should().Be(0);
    }

    [Fact]
    public void SellingTheHold_LeavesWhatDoesntPayForItsFuelAboard()
    {
        // One quartz fetches 26 at F49, against 82 for the fuel there.
        var ship = CommandShip(cargo: [new CargoItemModel("QUARTZ_SAND", 1), new CargoItemModel("COPPER_ORE", 10)]);

        var after = GatherPlanner.AfterSellingHold(Map(), ship);

        after.WaypointSymbol.Should().Be(H51);
        after.CargoInventory.Should().Equal(new CargoItemModel("QUARTZ_SAND", 1));
        after.CargoCurrent.Should().Be(1);
    }

    [Fact]
    public void WithAnEmptyHold_SellingTheHoldLeavesTheShipAsItIs()
    {
        var ship = CommandShip();

        GatherPlanner.AfterSellingHold(Map(), ship).Should().BeSameAs(ship);
    }
}
