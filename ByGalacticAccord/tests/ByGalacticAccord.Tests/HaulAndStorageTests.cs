using System.Linq;
using ByGalacticAccord.Engine.Actors;
using ByGalacticAccord.Engine.Domain;
using ByGalacticAccord.Engine.Simulation;

namespace ByGalacticAccord.Tests;

/// <summary>
/// The Haul and Storage lifecycles (design §3.2, §16.1): a carrier moves goods it doesn't own, from where they wait to
/// the shipper's place or the Sale they were sold under; storage holds goods at a station, for rent, until they leave.
/// </summary>
public sealed class HaulAndStorageTests
{
    [Fact]
    public void A_producer_sells_delivered_and_its_haul_hands_the_goods_over_on_arrival()
    {
        SimulationContext context = TestWorld.Quiet();
        ActorState habs = context.Actor("Meridian Habs");
        ActorState farm = context.Actor("Verdant Agricombine");
        ActorState juno = context.Actor("Juno Okafor");
        Ship ship = context.ShipOf(juno);
        decimal farmStock = farm.Stock(GoodType.Food);
        decimal habsStock = habs.Stock(GoodType.Food);
        SaleContract wanted = context.PostWanted(habs, GoodType.Food, 60m, 900m, hours: 200);

        Assert.Equal(Signing.Signed, Market.FillViaHaul(context, farm, wanted, wanted.OfferedPrice, FillSource.FromStock(farm), Credits.Of(80m), Credits.Of(100m), out HaulContract? haul));
        Assert.Equal((farm.Id, farm.Home, habs.Home, wanted.Id), (haul!.Shipper, haul.Origin, haul.Destination, haul.Serves!.Value));
        Assert.Equal(Holding.StockAt(farm.Home), haul.PickupFrom);
        Assert.Equal(60m, farm.Reserved(GoodType.Food));
        Credits farmBefore = farm.Credits;
        Credits junoBefore = juno.Credits;

        Assert.Equal(Signing.Signed, Market.TakeHaul(context, juno, haul, ship, haul.OfferedPrice));
        context.RunUntilSettled(haul);

        Assert.Equal(ContractStatus.Fulfilled, haul.Status);
        Assert.Equal(ContractStatus.Fulfilled, wanted.Status);
        Assert.Equal(farmStock - 60m, farm.Stock(GoodType.Food));
        Assert.Equal(habsStock + 60m, habs.Stock(GoodType.Food));
        Assert.Equal(0m, farm.Reserved(GoodType.Food));
        Assert.Empty(ship.Hold);

        // The farm paid the fee and its tax and was paid the price (its bond on the sale back with it); Juno got the fee.
        decimal expectedFarm = farmBefore.Amount - (80m * 1.04m) + 900m + wanted.LockedBond.Amount;
        Assert.Equal(System.Math.Round(expectedFarm, 2), System.Math.Round(farm.Credits.Amount, 2));
        Assert.Equal(System.Math.Round(junoBefore.Amount + 80m - context.Config.DockingFee.Amount, 2), System.Math.Round(juno.Credits.Amount, 2));
        HaulDeliveredEntry delivered = context.Journal.Entries.OfType<HaulDeliveredEntry>().Single();
        Assert.Equal((haul.Id, juno.Id, 60m, Credits.Of(80m)), (delivered.Contract, delivered.Hauler, delivered.Quantity, delivered.Fee));
        context.AssertSound();
    }

    [Fact]
    public void A_consumer_buys_a_lot_at_its_source_into_storage_and_hauls_it_home()
    {
        SimulationContext context = TestWorld.Quiet();
        ActorState habs = context.Actor("Meridian Habs");
        ActorState farm = context.Actor("Verdant Agricombine");
        ActorState juno = context.Actor("Juno Okafor");
        ActorState docks = context.Actor("Verdant Docks");
        LocationId verdant = farm.Home;
        decimal habsStock = habs.Stock(GoodType.Food);
        SaleContract lot = Market.PostSale(
            context, farm, SaleSide.ForSale, GoodType.Food, 50m, verdant, Credits.Of(300m), Credits.Of(280m), 168, Holding.StockAt(verdant));

        Assert.Equal(Signing.Signed, Market.BuyLot(context, habs, lot, lot.OfferedPrice, Holding.StorageAt(verdant)));

        // Bought and handed over at once, into storage the Habs rent at Verdant's station.
        Assert.Equal(ContractStatus.Fulfilled, lot.Status);
        StorageContract storage = context.StorageOf(habs.Id, verdant)!;
        Assert.Equal((docks.Id, habs.Id, ContractStatus.Active, 50m), (storage.Operator, storage.Renter, storage.Status, storage.Held(GoodType.Food)));
        Assert.Contains(context.Journal.Entries.OfType<StorageOpenedEntry>(), e => e.Contract == storage.Id);

        HaulContract haul = Market.PostHaul(context, habs, GoodType.Food, 50m, Holding.StorageAt(verdant), habs.Home, Credits.Of(60m), Credits.Of(80m), 300, serves: null);
        Assert.Equal(50m, storage.ReservedOf(GoodType.Food));
        context.RunTo(48); // the goods wait two days in storage
        Credits docksBefore = docks.Credits;
        Market.TakeHaul(context, juno, haul, context.ShipOf(juno), haul.OfferedPrice);
        context.RunUntilSettled(haul);

        Assert.Equal(ContractStatus.Fulfilled, haul.Status);
        Assert.Equal(habsStock + 50m, habs.Stock(GoodType.Food));

        // Taking the goods out emptied the store, which closed it: two days' rent on 50 units went to the station.
        Assert.Equal(ContractStatus.Fulfilled, storage.Status);
        Assert.Null(context.StorageOf(habs.Id, verdant));
        Credits rent = Credits.Of(WorldGenerator.StorageRate.Amount * 50m * 2m);
        Assert.Equal(rent, storage.RentPaid);
        Assert.Equal(docksBefore + rent, docks.Credits);
        StorageClosedEntry closed = context.Journal.Entries.OfType<StorageClosedEntry>().Single();
        Assert.Equal((storage.Id, habs.Id, rent), (closed.Contract, closed.Renter, closed.RentPaid));
        context.AssertSound();
    }

    [Fact]
    public void An_untaken_haul_expires_and_frees_the_goods_it_set_aside()
    {
        SimulationContext context = TestWorld.Quiet();
        ActorState habs = context.Actor("Meridian Habs");
        LocationId verdant = context.Place("Verdant").Id;
        context.PutInStorage(habs, verdant, MadeAt(context, GoodType.Food, 30m));
        HaulContract haul = Market.PostHaul(context, habs, GoodType.Food, 30m, Holding.StorageAt(verdant), habs.Home, Credits.Of(60m), Credits.Of(80m), 24, serves: null);
        StorageContract storage = context.StorageOf(habs.Id, verdant)!;
        Assert.Equal(30m, storage.ReservedOf(GoodType.Food));

        context.RunTo(24);

        Assert.Equal(ContractStatus.Expired, haul.Status);
        Assert.Equal(0m, storage.ReservedOf(GoodType.Food));
        Assert.Equal(30m, storage.Available(GoodType.Food));
        context.AssertSound();
    }

    [Fact]
    public void Storage_charges_rent_on_what_it_holds_and_food_spoils_in_it()
    {
        SimulationContext context = TestWorld.Quiet();
        ActorState juno = context.Actor("Juno Okafor");
        ActorState docks = context.Actor("Verdant Docks");
        LocationId verdant = juno.Home;
        Credits before = juno.Credits;

        StorageContract storage = context.PutInStorage(juno, verdant, MadeAt(context, GoodType.Food, 100m));
        context.RunTo(48);
        Cargo taken = context.TakeFromStorage(juno, verdant, GoodType.Food, 40m, reserved: false);
        context.Load(context.ShipOf(juno), juno.Id, taken);
        context.RunTo(96);
        context.SettleRent(storage);

        // 100 units for two days, then 60 for two more.
        Credits rent = Credits.Of(WorldGenerator.StorageRate.Amount * ((100m * 2m) + (60m * 2m)));
        Assert.Equal(rent, storage.RentPaid);
        Assert.Equal(before - rent, juno.Credits);
        Assert.Equal(WorldGenerator.StationFloat + rent, docks.Credits);
        Assert.Equal(40m, taken.Quantity);
        Assert.Equal(Cargo.Fresh(GoodType.Food, 1m).Decayed(48, 0m).Quality, taken.Quality, 12);
        Assert.Equal(Cargo.Fresh(GoodType.Food, 1m).Decayed(96, 0m).Quality, storage.Peek(GoodType.Food, 96)!.Value.Quality, 12);

        // A station keeps a float and spends the rest on upkeep, a sink.
        DecisionEngine.Run(context, docks);
        Assert.Equal(WorldGenerator.StationFloat, docks.Credits);
        context.AssertSound();
    }

    [Fact]
    public void Goods_in_storage_set_aside_for_a_sale_are_not_used_twice()
    {
        SimulationContext context = TestWorld.Quiet();
        ActorState juno = context.Actor("Juno Okafor");
        ActorState habs = context.Actor("Meridian Habs");
        LocationId verdant = juno.Home;
        context.PutInStorage(juno, verdant, MadeAt(context, GoodType.Food, 50m));
        SaleContract first = context.PostWanted(habs, GoodType.Food, 40m, 600m, hours: 200);
        SaleContract second = context.PostWanted(habs, GoodType.Food, 40m, 600m, hours: 200);

        Assert.Single(DecisionEngine.FillSources(context, juno, first, ship: null), s => s.Kind == FillSourceKind.Storage);
        Assert.Equal(Signing.Signed, Market.FillViaHaul(context, juno, first, first.OfferedPrice, FillSource.FromStorage(verdant), Credits.Of(60m), Credits.Of(80m)));

        Assert.Equal(40m, context.StorageOf(juno.Id, verdant)!.ReservedOf(GoodType.Food));
        Assert.DoesNotContain(DecisionEngine.FillSources(context, juno, second, ship: null), s => s.Kind == FillSourceKind.Storage);
    }

    private static Cargo MadeAt(SimulationContext context, GoodType good, decimal quantity)
    {
        ActorState maker = context.ProducersOf(good).First();
        context.Produce(maker, good, quantity);
        return context.TakeFromStock(maker, good, quantity, reserved: false);
    }
}
