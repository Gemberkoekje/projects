using System.Linq;
using ByGalacticAccord.Engine.Actors;
using ByGalacticAccord.Engine.Domain;
using ByGalacticAccord.Engine.Simulation;
using ByGalacticAccord.Presentation.Views;

namespace ByGalacticAccord.Tests;

/// <summary>
/// The estimate uses the engine's own rules (design §11.5), so what it calls certain must be what the engine then
/// does when nothing goes wrong on the way: for a haul, a delivered Sale filled by buying at a source, and a lot
/// bought and collected. In a quiet world no heartbeat charges running costs, so the change in the player's
/// credits is the estimate without them.
/// </summary>
public sealed class DealEstimateTests
{
    [Fact]
    public void The_haul_estimate_predicts_the_settlement_of_a_clean_run()
    {
        SimulationContext context = TestWorld.Quiet(withPlayer: true);
        ActorState player = context.Player();
        Ship ship = context.ShipOf(player);
        HaulContract haul = PlayerActionsTests.FarmHaul(context);
        DealEstimate estimate = DealEstimate.ForHaul(context, player, ship, haul)!;
        Credits before = player.Credits;

        Assert.Equal(Signing.Signed, PlayerActions.TakeHaul(context, player, haul, ship));
        context.RunUntilSettled(haul);

        HaulDeliveredEntry delivered = context.Journal.Entries.OfType<HaulDeliveredEntry>().Single(e => e.Contract == haul.Id);
        Assert.Equal(
            (estimate.ArrivalTick, estimate.Income, estimate.QualityRefund, estimate.LateFee, estimate.TicksLate),
            (delivered.Tick, delivered.Fee, delivered.QualityRefund, delivered.LateFee, delivered.TicksLate));
        Assert.Equal(Cents(estimate.TakeHome + estimate.Running), Cents(player.Credits - before));
    }

    [Fact]
    public void A_late_arrival_makes_its_fee_certain_and_the_bond_pays_it()
    {
        SimulationContext context = TestWorld.Quiet(withPlayer: true);
        ActorState player = context.Player();
        Ship ship = context.ShipOf(player);
        HaulContract haul = PlayerActionsTests.FarmHaul(context, hours: 50); // 60 h from Meridian and back: 10 h late, inside the grace

        DealEstimate estimate = DealEstimate.ForHaul(context, player, ship, haul)!;
        Assert.True(estimate.TicksLate > 0);
        Assert.True(estimate.LateFee.Amount > 0m);
        Assert.True(estimate.LateFee <= estimate.Bond);

        Credits before = player.Credits;
        PlayerActions.TakeHaul(context, player, haul, ship);
        context.RunUntilSettled(haul);

        HaulDeliveredEntry delivered = context.Journal.Entries.OfType<HaulDeliveredEntry>().Single(e => e.Contract == haul.Id);
        Assert.Equal((estimate.TicksLate, estimate.LateFee), (delivered.TicksLate, delivered.LateFee));
        Assert.Equal(Cents(estimate.TakeHome + estimate.Running), Cents(player.Credits - before));
    }

    [Fact]
    public void Running_costs_cover_the_empty_leg_and_the_laden_one()
    {
        SimulationContext context = TestWorld.Quiet(withPlayer: true);
        ActorState player = context.Player();
        Ship ship = context.ShipOf(player);
        HaulContract haul = PlayerActionsTests.FarmHaul(context); // from Verdant: an empty leg from Meridian first

        DealEstimate estimate = DealEstimate.ForHaul(context, player, ship, haul)!;

        Assert.NotNull(estimate.Repositioning);
        Assert.True(estimate.RepoTicks > 0);
        Assert.Equal(ship.Class.RunningCostPerTick.Amount * (estimate.RepoTicks + estimate.LadenTicks), estimate.Running.Amount);
        Assert.Equal(context.CurrentTick + estimate.RepoTicks + estimate.LadenTicks, estimate.ArrivalTick);
    }

    [Fact]
    public void A_busy_ship_is_estimated_from_where_and_when_it_is_next_free()
    {
        SimulationContext context = TestWorld.Quiet(withPlayer: true);
        ActorState player = context.Player();
        Ship ship = context.ShipOf(player);
        HaulContract first = PlayerActionsTests.FarmHaul(context);
        long arrival = DealEstimate.ForHaul(context, player, ship, first)!.ArrivalTick;
        PlayerActions.TakeHaul(context, player, first, ship);

        HaulContract next = PlayerActionsTests.FarmHaul(context);
        DealEstimate later = DealEstimate.ForHaul(context, player, ship, next)!;

        Assert.Equal((first.Destination, arrival), (later.StartAt, later.StartTick));
        Assert.True(later.RepoTicks > 0, "From Meridian back to Verdant for the next one.");
    }

    [Fact]
    public void The_fill_estimate_buys_at_the_source_and_predicts_the_handover()
    {
        SimulationContext context = TestWorld.Quiet(withPlayer: true);
        ActorState player = context.Player();
        Ship ship = context.ShipOf(player);
        context.PresentAt(player, "Rust Belt");
        SaleContract wanted = context.PostWanted(context.Actor("Rust Belt Camp"), GoodType.Food, 60m, 1400m, hours: 400);
        DealEstimate estimate = DealEstimate.For(context, player, ship, wanted)!;
        PlayerActions.FillFeasibility check = PlayerActions.CheckFill(context, player, wanted);
        Credits before = player.Credits;

        // Bought at the farm, taxed there; outside the Accord the camp holds no bond and pays at handover.
        Assert.Equal((DealKind.Fill, check.Source!.PurchasePrice), (estimate.Kind, estimate.Goods));
        Assert.True(estimate.Goods.Amount > 0m);
        Assert.True(estimate.GoodsTax.Amount > 0m);
        Assert.Equal(Credits.Zero, estimate.Bond);

        Assert.Equal(Signing.Signed, PlayerActions.Fill(context, player, wanted, ship, check.Source));
        context.RunUntilSettled(wanted);

        GoodsHandedOverEntry handed = context.Journal.Entries.OfType<GoodsHandedOverEntry>().Single(e => e.Contract == wanted.Id);
        Assert.Equal(
            (estimate.ArrivalTick, estimate.Income, estimate.QualityRefund, estimate.LateFee, estimate.TicksLate),
            (handed.Tick, handed.Price, handed.QualityRefund, handed.LateFee, handed.TicksLate));
        Assert.Equal(Cents(estimate.TakeHome + estimate.Running), Cents(player.Credits - before));
    }

    [Fact]
    public void The_lot_estimate_is_its_cost_and_the_goods_are_collected_when_it_says()
    {
        SimulationContext context = TestWorld.Quiet(withPlayer: true);
        ActorState player = context.Player();
        Ship ship = context.ShipOf(player);
        ActorState farm = context.Actor("Verdant Agricombine");
        SaleContract lot = Market.PostSale(
            context, farm, SaleSide.ForSale, GoodType.Food, 30m, farm.Home, Credits.Of(240m), Credits.Of(220m), 168, Holding.StockAt(farm.Home));
        DealEstimate estimate = DealEstimate.For(context, player, ship, lot)!;
        Credits before = player.Credits;

        Assert.Equal((DealKind.Buy, Credits.Zero, lot.OfferedPrice), (estimate.Kind, estimate.Income, estimate.Goods));
        Assert.Equal(estimate.Cost, estimate.Goods + estimate.GoodsTax + estimate.Running);
        Assert.Equal(-estimate.Cost.Amount, estimate.TakeHome.Amount);

        Assert.Equal(Signing.Signed, PlayerActions.Buy(context, player, lot, ship));
        context.RunUntilSettled(lot);

        GoodsHandedOverEntry handed = context.Journal.Entries.OfType<GoodsHandedOverEntry>().Single(e => e.Contract == lot.Id);
        Assert.Equal(estimate.PickupTick, handed.Tick);
        Assert.Equal(30m, ship.LotOf(player.Id, GoodType.Food)!.Quantity);
        Assert.Equal(Cents(-(estimate.Goods + estimate.GoodsTax).Amount), Cents(player.Credits - before));
    }

    // To the cent: the engine's fee divisions leave noise in the 26th place.
    private static decimal Cents(Credits amount) => System.Math.Round(amount.Amount, 2);

    private static decimal Cents(decimal amount) => System.Math.Round(amount, 2);
}
