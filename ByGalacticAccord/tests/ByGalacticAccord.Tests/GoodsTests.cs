using ByGalacticAccord.Engine.Domain;
using ByGalacticAccord.Engine.Simulation;

namespace ByGalacticAccord.Tests;

/// <summary>Perishables keep as long as design §15.3 says, and every unit is accounted for (design §20.2).</summary>
public sealed class GoodsTests
{
    [Theory]
    [InlineData(GoodType.Food, 7 * 24, 35 * 24)]
    [InlineData(GoodType.Medicine, 2 * 24, 10 * 24)]
    public void Perishables_keep_to_the_design_targets_in_a_standard_hold_and_refrigerated(GoodType good, long standard, long refrigerated)
    {
        decimal chilled = ShipClasses.Info(ShipType.RefrigeratedTransport).Preservation;

        Assert.Equal(Goods.StandardWarranty, Cargo.Fresh(good, 1m).Decayed(standard, 0m).Quality, 12);
        Assert.Equal(Goods.StandardWarranty, Cargo.Fresh(good, 1m).Decayed(refrigerated, chilled).Quality, 12);
        Assert.True(Cargo.Fresh(good, 1m).Decayed(standard - 1, 0m).Quality > Goods.StandardWarranty);
    }

    [Fact]
    public void Food_survives_Verdant_to_Rust_Belt_in_a_standard_hold_but_medicine_reaches_the_frontier_only_refrigerated()
    {
        // Design §15.3's own check on the keep-times.
        SimulationContext context = TestWorld.Quiet();
        long food = context.FindPath(context.Place("Verdant").Id, context.Place("Rust Belt").Id)!.DistanceTicks;
        long medicine = context.FindPath(context.Place("Helios Prime").Id, context.Place("Edge Station").Id)!.DistanceTicks;
        ShipClassInfo reefer = ShipClasses.Info(ShipType.RefrigeratedTransport);
        long reeferHours = Engine.Economy.PriceModel.TravelTicks(medicine, reefer.SpeedMultiplier);

        Assert.True(Cargo.Fresh(GoodType.Food, 1m).Decayed(food, 0m).Quality >= Goods.StandardWarranty);
        Assert.True(Cargo.Fresh(GoodType.Medicine, 1m).Decayed(medicine, 0m).Quality < Goods.StandardWarranty);
        Assert.True(Cargo.Fresh(GoodType.Medicine, 1m).Decayed(reeferHours, reefer.Preservation).Quality >= Goods.StandardWarranty);
    }

    [Fact]
    public void Merging_parcels_keeps_the_weighted_quality()
    {
        var fresh = new Cargo(GoodType.Food, 30m, 1m);
        var tired = new Cargo(GoodType.Food, 10m, 0.6m);

        Cargo merged = fresh.Merged(tired);

        Assert.Equal(40m, merged.Quantity);
        Assert.Equal(0.9m, merged.Quality);
        Assert.Throws<System.ArgumentException>(() => fresh.Merged(new Cargo(GoodType.Ore, 1m, 1m)));
    }

    [Fact]
    public void A_lot_decays_from_when_it_was_last_read()
    {
        var lot = new Lot(new ActorId(1), Cargo.Fresh(GoodType.Medicine, 10m), Since: 100);

        Lot later = lot.At(124, preservation: 0m);

        Assert.Equal(124, later.Since);
        Assert.Equal(1m - (24m * Goods.Info(GoodType.Medicine).QualityDecayPerTick), later.Cargo.Quality);
        Assert.Same(lot, lot.At(90, 0m));
    }

    [Fact]
    public void A_unit_that_vanishes_without_being_booked_breaks_goods_conservation()
    {
        SimulationContext context = TestWorld.Quiet();
        context.AssertSound();
        var farm = context.Actor("Verdant Agricombine");

        context.TakeFromStock(farm, GoodType.Food, 5m, reserved: false); // taken and dropped on the floor

        Assert.Contains(Invariants.Check(context), v => v.Name == "GoodsConservation");
    }
}
