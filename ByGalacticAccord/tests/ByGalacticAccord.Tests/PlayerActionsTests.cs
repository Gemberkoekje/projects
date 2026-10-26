using System.Linq;
using ByGalacticAccord.Engine.Actors;
using ByGalacticAccord.Engine.Domain;
using ByGalacticAccord.Engine.Simulation;

namespace ByGalacticAccord.Tests;

/// <summary>
/// The player's commands, through the same market commands an AI decision uses, and each way they refuse. The
/// interface words the codes these return. The player also pays running costs like everyone else (plan S3).
/// </summary>
public sealed class PlayerActionsTests
{
    [Fact]
    public void The_player_takes_a_haul_as_posted_through_the_market()
    {
        SimulationContext context = TestWorld.Quiet(withPlayer: true);
        ActorState player = context.Player();
        HaulContract haul = FarmHaul(context);

        PlayerActions.HaulFeasibility check = PlayerActions.CheckHaul(context, player, haul);
        Assert.True(check.Ok);
        Assert.Equal(Signing.Signed, PlayerActions.TakeHaul(context, player, haul, check.Ship!));

        Assert.Equal((ContractStatus.Active, player.Id, haul.OfferedPrice), (haul.Status, haul.Carrier!.Value, haul.Price));
        Assert.Equal(haul.Id, check.Ship!.Job!.Haul);
        ContractSignedEntry signed = Assert.IsType<ContractSignedEntry>(context.Journal.Entries[^1]);
        Assert.Equal((haul.Id, player.Id), (signed.Contract, signed.Taker));
        Assert.Equal(DealBlock.NotOpen, PlayerActions.CheckHaul(context, player, haul).Block);
        Assert.Equal(Signing.NotOpen, PlayerActions.TakeHaul(context, player, haul, check.Ship!));
        context.RunUntilSettled(haul);
        Assert.Equal(ContractStatus.Fulfilled, haul.Status);
        context.AssertSound();
    }

    [Fact]
    public void A_busy_ship_cannot_take_a_second_contract_and_says_when_it_is_free()
    {
        SimulationContext context = TestWorld.Quiet(withPlayer: true);
        ActorState player = context.Player();
        HaulContract first = FarmHaul(context);
        HaulContract second = FarmHaul(context);
        PlayerActions.TakeHaul(context, player, first, context.ShipOf(player));

        PlayerActions.HaulFeasibility busy = PlayerActions.CheckHaul(context, player, second);

        Assert.Equal(DealBlock.NoFreeShip, busy.Block);
        Assert.Same(context.ShipOf(player), busy.Ship);
        (LocationId at, long tick) = Market.NextFree(context, busy.Ship!);
        Assert.Equal(first.Destination, at);
        Assert.True(tick > context.CurrentTick);
    }

    [Fact]
    public void A_haul_larger_than_the_hold_is_blocked()
    {
        SimulationContext context = TestWorld.Quiet(withPlayer: true);
        ActorState player = context.Player();
        HaulContract big = FarmHaul(context, quantity: context.ShipOf(player).CargoCapacity + 1m);

        Assert.Equal(DealBlock.TooLarge, PlayerActions.CheckHaul(context, player, big).Block);
    }

    [Fact]
    public void The_player_fills_a_delivered_sale_by_buying_at_the_source_like_a_trader()
    {
        SimulationContext context = TestWorld.Quiet(withPlayer: true);
        ActorState player = context.Player();
        ActorState camp = context.Actor("Rust Belt Camp");
        SaleContract wanted = context.PostWanted(camp, GoodType.Food, 60m, 1400m, hours: 400);

        PlayerActions.FillFeasibility check = PlayerActions.CheckFill(context, player, wanted);
        Assert.True(check.Ok);
        Assert.Equal((FillSourceKind.Producer, context.Actor("Verdant Agricombine").Id), (check.Source!.Kind, check.Source.Producer!.Value));
        Assert.Equal(Signing.Signed, PlayerActions.Fill(context, player, wanted, check.Ship!, check.Source));
        Credits before = player.Credits;
        context.RunUntilSettled(wanted);

        // Outside the Accord the camp pays at handover: the price, with nothing held back for the 6-day-old food.
        Assert.Equal(ContractStatus.Fulfilled, wanted.Status);
        Assert.Equal(System.Math.Round(before.Amount + 1400m - context.Config.DockingFee.Amount, 2), System.Math.Round(player.Credits.Amount, 2));
        context.AssertSound();
    }

    [Fact]
    public void A_sale_nobody_can_supply_is_blocked_and_one_the_player_cannot_afford_too()
    {
        SimulationContext context = TestWorld.Quiet(withPlayer: true);
        ActorState player = context.Player();
        ActorState habs = context.Actor("Meridian Habs");
        ActorState farm = context.Actor("Verdant Agricombine");
        SaleContract wanted = context.PostWanted(habs, GoodType.Food, 60m, 900m);

        context.ApplySink(player, player.Credits);
        Assert.Equal(DealBlock.CannotAfford, PlayerActions.CheckFill(context, player, wanted).Block);

        context.Consume(farm, GoodType.Food, farm.Stock(GoodType.Food)); // the farm has nothing to sell
        Assert.Equal(DealBlock.NoSource, PlayerActions.CheckFill(context, player, wanted).Block);
    }

    [Fact]
    public void The_player_fills_from_goods_already_aboard_and_hands_them_over_on_the_spot()
    {
        SimulationContext context = TestWorld.Quiet(withPlayer: true);
        ActorState player = context.Player();
        ActorState habs = context.Actor("Meridian Habs"); // at Meridian Station, where the player starts
        Ship ship = context.ShipOf(player);
        SaleTests.GiveAboard(context, ship, player, GoodType.Food, 70m);
        SaleContract wanted = context.PostWanted(habs, GoodType.Food, 50m, 700m);

        PlayerActions.FillFeasibility check = PlayerActions.CheckFill(context, player, wanted);
        Assert.Equal(FillSourceKind.Aboard, check.Source!.Kind);
        Assert.Equal(Signing.Signed, PlayerActions.Fill(context, player, wanted, ship, check.Source));

        Assert.Equal(ContractStatus.Fulfilled, wanted.Status);
        Assert.Equal(20m, ship.LotOf(player.Id, GoodType.Food)!.Quantity);
        Assert.True(ship.IsFree);
        context.AssertSound();
    }

    [Fact]
    public void The_player_buys_a_lot_collects_it_and_stores_and_retrieves_it()
    {
        SimulationContext context = TestWorld.Quiet(withPlayer: true);
        ActorState player = context.Player();
        ActorState fabworks = context.Actor("Meridian Fabworks"); // parts, at the player's own station
        Ship ship = context.ShipOf(player);
        SaleContract lot = Market.PostSale(
            context, fabworks, SaleSide.ForSale, GoodType.Parts, 30m, fabworks.Home, Credits.Of(600m), Credits.Of(550m), 168, Holding.StockAt(fabworks.Home));

        PlayerActions.BuyFeasibility check = PlayerActions.CheckBuy(context, player, lot);
        Assert.True(check.Ok);
        Assert.Equal(Signing.Signed, PlayerActions.Buy(context, player, lot, ship));
        context.RunUntilSettled(lot);
        Assert.Equal(30m, ship.LotOf(player.Id, GoodType.Parts)!.Quantity);

        Assert.True(PlayerActions.Store(context, player, ship, GoodType.Parts));
        Assert.Empty(ship.Hold);
        Assert.Equal(30m, context.StorageOf(player.Id, ship.Location)!.Held(GoodType.Parts));

        Assert.True(PlayerActions.Retrieve(context, player, ship, GoodType.Parts));
        Assert.Equal(30m, ship.LotOf(player.Id, GoodType.Parts)!.Quantity);
        Assert.Null(context.StorageOf(player.Id, ship.Location));
        context.AssertSound();
    }

    [Fact]
    public void The_player_pays_running_costs_every_day_like_everyone_else()
    {
        SimulationContext context = WorldGenerator.Generate(42L, withPlayer: true).Context;
        ActorState player = context.Player();
        Ship ship = context.ShipOf(player);
        Credits daily = Credits.Of(ship.Class.RunningCostPerTick.Amount * context.Config.HeartbeatInterval);
        var charged = new System.Collections.Generic.List<long>();
        Credits last = player.Credits;
        for (long t = 1; t <= 72; t++)
        {
            context.RunTo(t);
            if (player.Credits != last)
            {
                Assert.Equal(last - daily, player.Credits);
                charged.Add(t);
                last = player.Credits;
            }
        }

        Assert.Equal(3, charged.Count);
        Assert.Equal(new long[] { 24, 24 }, new[] { charged[1] - charged[0], charged[2] - charged[1] });
    }

    [Fact]
    public void A_player_alert_is_the_journal_entry_and_stops_the_run()
    {
        SimulationContext context = WorldGenerator.Generate(42L, withPlayer: true).Context;
        var entry = new IncidentEntry(0, new ContractId(1), context.PlayerActorId!.Value, default);
        context.RaisePlayerAlert(entry);

        Assert.Equal(RunStop.PlayerAlert, context.RunTo(100));
        Assert.True(context.CurrentTick < 100);
        Assert.Same(entry, Assert.Single(context.PlayerAlerts));

        context.ClearPlayerAlerts();
        Assert.Equal(RunStop.ReachedTarget, context.RunTo(100));
        Assert.Equal(100, context.CurrentTick);
    }

    // A haul of the farm's own food from Verdant to Meridian Station, posted as the farm would after selling it delivered.
    [Fact]
    public void The_player_fills_a_sale_through_a_carrier_and_is_paid_when_the_haul_hands_it_over()
    {
        SimulationContext context = TestWorld.Quiet(withPlayer: true);
        ActorState player = context.Player();
        ActorState habs = context.Actor("Meridian Habs");
        ActorState juno = context.Actor("Juno Okafor");
        SaleContract wanted = context.PostWanted(habs, GoodType.Food, 40m, 700m, hours: 200);

        PlayerActions.BrokerFeasibility check = PlayerActions.CheckBroker(context, player, wanted);
        Assert.True(check.Ok);
        Assert.Equal((FillSourceKind.Producer, context.Actor("Verdant Agricombine").Id), (check.Source!.Kind, check.Source.Producer!.Value));
        Assert.Equal(DecisionEngine.UsualHaulOffer(context, check.Source.At, habs.Home), check.Fee);
        Assert.Equal(Signing.Signed, PlayerActions.Broker(context, player, wanted, check.Source, check.Fee));

        // The goods are the player's, in storage at Verdant, set aside for the sale; the haul is posted at the usual offer.
        HaulContract haul = context.Contracts.Values.OfType<HaulContract>().Single(h => h.Serves == wanted.Id);
        Assert.Equal((player.Id, check.Fee, Holding.StorageAt(check.Source.At)), (haul.Shipper, haul.OfferedPrice, haul.PickupFrom));
        Assert.Equal(40m, context.StorageOf(player.Id, check.Source.At)!.ReservedOf(GoodType.Food));
        Assert.True(context.ShipOf(player).IsFree); // the player's own ship isn't used

        Assert.Equal(Signing.Signed, Market.TakeHaul(context, juno, haul, context.ShipOf(juno), haul.OfferedPrice));
        Credits before = player.Credits;
        context.RunUntilSettled(wanted);

        // Paid the price at the handover, with its bond on the sale back, and nothing for the haul it already locked.
        Assert.Equal((ContractStatus.Fulfilled, ContractStatus.Fulfilled), (haul.Status, wanted.Status));
        Assert.Equal(System.Math.Round(before.Amount + wanted.Price.Amount + wanted.LockedBond.Amount, 2), System.Math.Round(player.Credits.Amount, 2));
        context.AssertSound();
    }

    [Fact]
    public void Filling_through_a_carrier_is_refused_when_no_carrier_could_make_it_or_the_player_cant_pay()
    {
        SimulationContext context = TestWorld.Quiet(withPlayer: true);
        ActorState player = context.Player();
        ActorState habs = context.Actor("Meridian Habs");

        SaleContract soon = context.PostWanted(habs, GoodType.Food, 40m, 700m, hours: 60); // two days for a carrier to find it, then 30 h
        Assert.Equal(DealBlock.TooLate, PlayerActions.CheckBroker(context, player, soon).Block);

        SaleContract later = context.PostWanted(habs, GoodType.Food, 40m, 700m, hours: 200);
        context.ApplySink(player, player.Credits - Credits.Of(50m));
        Assert.Equal(DealBlock.CannotAfford, PlayerActions.CheckBroker(context, player, later).Block);

        SaleContract lot = Market.PostSale(
            context, habs, SaleSide.ForSale, GoodType.Food, 10m, habs.Home, Credits.Of(50m), Credits.Of(50m), context.CurrentTick + 10, Holding.StockAt(habs.Home));
        Assert.Equal(DealBlock.NotOpen, PlayerActions.CheckBroker(context, player, lot).Block);
    }

    internal static HaulContract FarmHaul(SimulationContext context, decimal quantity = 40m, long hours = 200, TermPackage package = TermPackage.Standard)
    {
        ActorState farm = context.Actor("Verdant Agricombine");
        return Market.PostHaul(
            context, farm, GoodType.Food, quantity, Holding.StockAt(farm.Home), context.Place("Meridian Station").Id,
            Credits.Of(70m), Credits.Of(90m), context.CurrentTick + hours, serves: null, package);
    }
}
