using FluentAssertions;
using SpaceTraders.Application.Ports;
using SpaceTraders.Application.Trading;
using static SpaceTraders.Application.Tests.Trading.TradeFixture;

namespace SpaceTraders.Application.Tests.Trading;

/// <summary>
/// D42, asked on 2026-10-02: "if a ship's cargo hold isn't empty and the goods aren't going to be sold or earmarked for
/// another reason, the ship should either go to a waypoint to sell it or, if that's not profitable, jettison it."
/// </summary>
public sealed class HeldCargoTests
{
    [Fact]
    public void WhileAGoodAboardPaysForItsSale_NothingIsJettisoned_ItIsSoldFirst()
    {
        var ship = Hauler(A1, new CargoItemModel("MEDICINE", 10), new CargoItemModel("GRAVEL", 3));

        HeldCargo.ToJettison(Map(), ship, _ => false).Should().BeEmpty();
    }

    [Fact]
    public void AGoodNoMarketBuys_GoesOverboard()
    {
        var ship = Hauler(A1, new CargoItemModel("GRAVEL", 3));

        HeldCargo.ToJettison(Map(), ship, _ => false).Should().Equal((new CargoItemModel("GRAVEL", 3), HeldCargo.NoBuyer));
    }

    [Fact]
    public void AGoodWhoseBestSaleDoesntPayForTheFuel_GoesOverboard()
    {
        // Only K85 buys PLASTICS, at 5: three units fetch 15, and the fuel there from A1, 104 away, costs 2 x 93.
        var k85 = K85Market() with { TradeGoods = [.. K85Market().TradeGoods, Good("PLASTICS", "IMPORT", 9, 5, 60)] };
        var ship = Hauler(A1, new CargoItemModel("PLASTICS", 3));

        HeldCargo.ToJettison(Map(k85, D41Market(), A1Market()), ship, _ => false)
            .Should().Equal((new CargoItemModel("PLASTICS", 3), HeldCargo.NotWorthTheFuel));
    }

    [Fact]
    public void AnEarmarkedGood_StaysAboard()
    {
        var ship = Hauler(A1, new CargoItemModel("GRAVEL", 3), new CargoItemModel("COPPER_ORE", 7));

        HeldCargo.ToJettison(Map(), ship, good => good == "COPPER_ORE").Should().Equal((new CargoItemModel("GRAVEL", 3), HeldCargo.NoBuyer));
    }

    private static ShipModel Hauler(string waypoint, params CargoItemModel[] cargo)
        => new(
            "SHIP-7",
            SystemSymbol,
            waypoint,
            "DOCKED",
            "CRUISE",
            400,
            400,
            CargoCurrent: cargo.Sum(item => item.Units),
            CargoCapacity: 40,
            ShipType: "SHIP_LIGHT_HAULER",
            CargoInventory: cargo);
}
