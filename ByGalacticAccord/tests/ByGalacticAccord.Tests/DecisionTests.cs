using System.Linq;
using ByGalacticAccord.Engine.Actors;
using ByGalacticAccord.Engine.Domain;
using ByGalacticAccord.Engine.Economy;
using ByGalacticAccord.Engine.Simulation;

namespace ByGalacticAccord.Tests;

/// <summary>
/// The AI's rules per role (design §4.1, §4.3; plan S3), one turn at a time in a quiet world: consumers post delivered
/// wanted Sales and learn their prices; producers post lots and answer; traders fill by buying and carrying, and post
/// Hauls when overcommitted; nobody hoards past its working capital.
/// </summary>
public sealed class DecisionTests
{
    [Fact]
    public void A_consumer_short_of_a_good_posts_a_delivered_wanted_sale_and_offers_more_after_one_expires()
    {
        SimulationContext context = TestWorld.Quiet();
        ActorState habs = context.Actor("Meridian Habs");
        context.Consume(habs, GoodType.Food, habs.Stock(GoodType.Food)); // the larder is empty

        DecisionEngine.RunConsumer(context, habs);
        SaleContract first = OpenWanted(context, habs, GoodType.Food);
        Assert.Equal((habs.Home, SaleSide.Wanted, Holding.StockAt(habs.Home)), (first.Place, first.Side, first.Receipt!.Value));
        Assert.Equal(100m, first.Quantity); // its need, in a post a hold can carry

        context.RunTo(first.DeadlineTick);
        Assert.Equal(ContractStatus.Expired, first.Status);
        DecisionEngine.RunConsumer(context, habs);
        SaleContract second = OpenWanted(context, habs, GoodType.Food);

        // The same goods and need, but more of the spread offered: nobody took the first one.
        Assert.True(second.OfferedPrice > first.OfferedPrice, $"{second.OfferedPrice} after {first.OfferedPrice}");
    }

    [Fact]
    public void A_full_producer_posts_a_lot_under_its_ask_but_never_under_cost()
    {
        SimulationContext context = TestWorld.Quiet();
        ActorState farm = context.Actor("Verdant Agricombine");
        context.Produce(farm, GoodType.Food, farm.Producer!.StockCap - farm.Stock(GoodType.Food)); // full

        DecisionEngine.RunProducer(context, farm);

        SaleContract lot = context.OpenContracts.OfType<SaleContract>().Single(s => s.PostedBy == farm.Id && s.Side == SaleSide.ForSale);
        Assert.Equal(lot.Quantity, farm.Reserved(GoodType.Food));
        Assert.True(lot.UnitPrice.Amount <= DecisionEngine.Ask(farm).Amount);
        Assert.True(lot.UnitPrice >= farm.Producer.UnitProductionCost);
    }

    [Fact]
    public void A_trader_with_a_free_ship_fills_a_delivered_sale_it_can_see()
    {
        SimulationContext context = TestWorld.Quiet();
        ActorState habs = context.Actor("Meridian Habs");
        ActorState juno = context.Actor("Juno Okafor");
        SaleContract wanted = context.PostWanted(habs, GoodType.Food, 60m, 900m, hours: 200);
        context.RunTo(context.CurrentTick + Visibility.ReachNear); // Meridian is a jump from Juno's Verdant: the post reaches it a few hours on

        DecisionEngine.RunTrader(context, juno);

        Assert.Equal((ContractStatus.Active, juno.Id), (wanted.Status, wanted.Seller!.Value));
        Assert.Equal(wanted.Id, context.ShipOf(juno).Job!.Delivery);
    }

    [Fact]
    public void A_trader_whose_ships_are_all_busy_buys_into_storage_and_posts_a_haul_for_another_carrier()
    {
        SimulationContext context = TestWorld.Quiet();
        ActorState habs = context.Actor("Meridian Habs");
        ActorState juno = context.Actor("Juno Okafor");
        Market.Dispatch(context, context.ShipOf(juno), new ShipJob(juno.Id, GoodType.Food, 0m, context.Place("Edge Station").Id, PickupSource.Aboard, null, Delivery: new ContractId(999)));
        SaleContract wanted = context.PostWanted(habs, GoodType.Food, 60m, 1200m, hours: 300);
        context.RunTo(context.CurrentTick + Visibility.ReachNear);

        DecisionEngine.RunTrader(context, juno);

        Assert.Equal((ContractStatus.Active, juno.Id), (wanted.Status, wanted.Seller!.Value));
        HaulContract haul = context.OpenContracts.OfType<HaulContract>().Single();
        Assert.Equal((juno.Id, wanted.Id, habs.Home), (haul.Shipper, haul.Serves!.Value, haul.Destination));
        Assert.Equal(Holding.StorageAt(context.Place("Verdant").Id), haul.PickupFrom);
        Assert.Equal(60m, context.StorageOf(juno.Id, haul.Origin)!.ReservedOf(GoodType.Food));
    }

    [Fact]
    public void A_producer_with_stock_to_spare_sells_delivered_and_posts_a_haul()
    {
        SimulationContext context = TestWorld.Quiet();
        ActorState habs = context.Actor("Meridian Habs");
        ActorState farm = context.Actor("Verdant Agricombine");
        context.Produce(farm, GoodType.Food, farm.Producer!.StockCap - farm.Stock(GoodType.Food));
        SaleContract wanted = context.PostWanted(habs, GoodType.Food, 60m, 1200m, hours: 300);
        context.RunTo(context.CurrentTick + Visibility.ReachNear);

        DecisionEngine.RunProducer(context, farm);

        Assert.Equal(farm.Id, wanted.Seller);
        HaulContract haul = context.OpenContracts.OfType<HaulContract>().Single();
        Assert.Equal((farm.Id, Holding.StockAt(farm.Home), wanted.Id), (haul.Shipper, haul.PickupFrom, haul.Serves!.Value));
    }

    [Fact]
    public void An_ai_actor_pays_out_what_it_holds_above_its_working_capital()
    {
        SimulationContext context = TestWorld.Quiet();
        ActorState farm = context.Actor("Verdant Agricombine");
        Credits capital = DecisionEngine.WorkingCapital(context, farm);
        context.ApplyFaucet(farm, capital); // well above it now
        Credits sunk = context.TotalSink;

        DecisionEngine.Run(context, farm);

        Assert.Equal(capital, farm.Credits);
        Assert.True(context.TotalSink > sunk);
        context.AssertSound();
    }

    [Fact]
    public void A_rich_producer_gives_up_its_margin()
    {
        var profile = new ProducerProfile(GoodType.Food, 26m, Credits.Of(5m), 400m, 0.25m);
        decimal reserve = 5m * 400m;

        Credits modest = PriceModel.ProducerAsk(profile, 200m, Credits.Of(reserve));
        Credits rich = PriceModel.ProducerAsk(profile, 200m, Credits.Of(reserve * 4m));

        Assert.Equal(PriceModel.ProducerAsk(profile, 200m), modest);
        Assert.Equal(PriceModel.ProducerAsk(profile with { Margin = 0m }, 200m), rich);
        Assert.True(rich < modest);
    }

    private static SaleContract OpenWanted(SimulationContext context, ActorState poster, GoodType good) =>
        context.OpenContracts.OfType<SaleContract>().Single(s => s.PostedBy == poster.Id && s.Good == good);
}
