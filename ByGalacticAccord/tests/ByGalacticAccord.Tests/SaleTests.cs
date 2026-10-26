using System.Linq;
using ByGalacticAccord.Engine.Actors;
using ByGalacticAccord.Engine.Domain;
using ByGalacticAccord.Engine.Simulation;

namespace ByGalacticAccord.Tests;

/// <summary>
/// A Sale's lifecycle (design §3.2, §16.2, §16.3): posted, signed, goods handed over at the named place and the price
/// paid; and each way it fails. Money and goods are checked to the cent and the unit, and the invariants after each.
/// </summary>
public sealed class SaleTests
{
    [Fact]
    public void A_wanted_sale_at_a_producers_door_is_answered_at_once_and_settled()
    {
        SimulationContext context = TestWorld.Quiet();
        ActorState metropole = context.Actor("Helios Prime Metropole");
        ActorState biolabs = context.Actor("Helios Biolabs");
        decimal stock = metropole.Stock(GoodType.Medicine);
        decimal made = biolabs.Stock(GoodType.Medicine);
        Credits buyerBefore = metropole.Credits;
        Credits sellerBefore = biolabs.Credits;
        Credits revenueBefore = context.GovernmentRevenue;
        decimal price = DecisionEngine.Ask(biolabs).Amount * 10m * 1.05m;

        SaleContract sale = context.PostWanted(metropole, GoodType.Medicine, 10m, price);

        Assert.Equal(ContractStatus.Fulfilled, sale.Status);
        Assert.Equal(biolabs.Id, sale.Seller);
        Assert.Equal(stock + 10m, metropole.Stock(GoodType.Medicine));
        Assert.Equal(made - 10m, biolabs.Stock(GoodType.Medicine));
        Assert.Equal(0m, biolabs.Reserved(GoodType.Medicine));
        Assert.Equal(Credits.Zero, sale.EscrowHeld);

        // Deep Accord: the buyer pays the price and the 4% tax; the seller gets the price (and its bond back).
        Credits tax = Credits.Of(price * context.Config.TaxRate);
        Assert.Equal(Round(buyerBefore.Amount - price - tax.Amount), Round(metropole.Credits.Amount));
        Assert.Equal(Round(sellerBefore.Amount + price), Round(biolabs.Credits.Amount));
        Assert.Equal(Round(revenueBefore.Amount + tax.Amount), Round(context.GovernmentRevenue.Amount));

        var entries = context.Journal.Entries.Where(e => Concerns(e, sale.Id)).Select(e => e.GetType().Name).ToArray();
        Assert.Equal(new[] { nameof(ContractPostedEntry), nameof(ContractSignedEntry), nameof(GoodsHandedOverEntry) }, entries);
        context.AssertSound();
    }

    [Fact]
    public void A_trader_fills_a_delivered_sale_by_buying_at_the_source_and_carrying()
    {
        SimulationContext context = TestWorld.Quiet();
        ActorState habs = context.Actor("Meridian Habs");
        ActorState juno = context.Actor("Juno Okafor");
        ActorState farm = context.Actor("Verdant Agricombine");
        Ship ship = context.ShipOf(juno);
        decimal habsFood = habs.Stock(GoodType.Food);
        Credits junoBefore = juno.Credits;

        SaleContract wanted = context.PostWanted(habs, GoodType.Food, 60m, 800m, hours: 200);
        FillSource source = DecisionEngine.FillSources(context, juno, wanted, ship).Single();
        Assert.Equal(FillSourceKind.Producer, source.Kind);
        Assert.Equal(farm.Id, source.Producer);

        Assert.Equal(Signing.Signed, Market.FillWithShip(context, juno, wanted, wanted.OfferedPrice, ship, source));

        // Juno bought the food at Verdant (a wanted Sale there, answered at once) and is the seller of the delivered one.
        SaleContract purchase = context.Contracts.Values.OfType<SaleContract>().Single(s => s.Id != wanted.Id && s.Buyer == juno.Id);
        Assert.Equal((ContractStatus.Active, farm.Id, 60m), (purchase.Status, purchase.Seller!.Value, farm.Reserved(GoodType.Food)));
        Assert.Equal(juno.Id, wanted.Seller);
        Assert.Equal(new ShipJob(juno.Id, GoodType.Food, 60m, farm.Home, PickupSource.Purchase, habs.Home, Purchase: purchase.Id, Delivery: wanted.Id), ship.Job);

        context.RunUntilSettled(wanted);

        Assert.Equal(ContractStatus.Fulfilled, purchase.Status);
        Assert.Equal(ContractStatus.Fulfilled, wanted.Status);
        Assert.Equal(habsFood + 60m, habs.Stock(GoodType.Food));
        Assert.Empty(ship.Hold);
        Assert.True(ship.IsFree);

        // Juno paid for the goods and their tax at Verdant, was paid the price at Meridian, and paid docking there.
        decimal paid = purchase.Price.Amount * (1m + purchase.TaxRate);
        Assert.Equal(Round(junoBefore.Amount - paid + wanted.Price.Amount - context.Config.DockingFee.Amount), Round(juno.Credits.Amount));
        GoodsHandedOverEntry handed = context.Journal.Entries.OfType<GoodsHandedOverEntry>().Single(e => e.Contract == wanted.Id);
        Assert.Equal((juno.Id, habs.Id, 60m, 0L, Credits.Zero, Credits.Zero), (handed.Seller, handed.Buyer, handed.Quantity, handed.TicksLate, handed.LateFee, handed.QualityRefund));
        context.AssertSound();
    }

    [Fact]
    public void A_lot_for_sale_is_bought_whole_and_collected_by_the_buyers_ship()
    {
        SimulationContext context = TestWorld.Quiet();
        ActorState farm = context.Actor("Verdant Agricombine");
        ActorState pell = context.Actor("Pell Mirza");
        Ship ship = context.ShipOf(pell); // a Light Freighter at Helios Prime, 45 h from Verdant
        decimal stock = farm.Stock(GoodType.Food);

        SaleContract lot = Market.PostSale(
            context, farm, SaleSide.ForSale, GoodType.Food, 50m, farm.Home, Credits.Of(300m), Credits.Of(280m),
            context.CurrentTick + 168, Holding.StockAt(farm.Home));
        Assert.Equal(50m, farm.Reserved(GoodType.Food));

        Assert.Equal(Signing.Signed, Market.BuyLot(context, pell, lot, lot.OfferedPrice, Holding.Aboard(ship.Id, farm.Home)));
        Assert.Equal(ShipStatus.InTransit, ship.Status);
        context.RunUntilSettled(lot);

        Assert.Equal(ContractStatus.Fulfilled, lot.Status);
        Assert.Equal(context.Place("Verdant").Id, ship.Location);
        Assert.True(ship.IsFree);
        Lot aboard = Assert.Single(ship.Hold);
        Assert.Equal((pell.Id, GoodType.Food, 50m), (aboard.Owner, aboard.Good, aboard.Quantity));
        Assert.Equal(stock - 50m, farm.Stock(GoodType.Food));
        Assert.Equal(0m, farm.Reserved(GoodType.Food));
        context.AssertSound();
    }

    [Fact]
    public void An_unsold_lot_expires_and_its_goods_are_free_again()
    {
        SimulationContext context = TestWorld.Quiet();
        ActorState farm = context.Actor("Verdant Agricombine");
        SaleContract lot = Market.PostSale(
            context, farm, SaleSide.ForSale, GoodType.Food, 50m, farm.Home, Credits.Of(300m), Credits.Of(280m),
            context.CurrentTick + 10, Holding.StockAt(farm.Home));

        context.RunTo(context.CurrentTick + 10);

        Assert.Equal(ContractStatus.Expired, lot.Status);
        Assert.Equal(0m, farm.Reserved(GoodType.Food));
        Assert.Contains(context.Journal.Entries.OfType<ContractExpiredEntry>(), e => e.Contract == lot.Id);
    }

    [Fact]
    public void Outside_the_Accord_a_buyer_who_cannot_pay_defaults_and_the_seller_keeps_the_goods()
    {
        SimulationContext context = TestWorld.Quiet();
        ActorState camp = context.Actor("Rust Belt Camp");
        ActorState kael = context.Actor("Dust Runner Kael");
        Ship ship = context.ShipOf(kael); // docked at Rust Belt, the camp's home
        context.ApplySink(camp, camp.Credits); // the camp is broke
        GiveAboard(context, ship, kael, GoodType.Food, 40m);
        decimal stock = camp.Stock(GoodType.Food);

        SaleContract wanted = context.PostWanted(camp, GoodType.Food, 40m, 500m);
        Assert.False(wanted.UsesEscrow);
        Assert.Equal(Signing.Signed, Market.FillWithShip(context, kael, wanted, wanted.OfferedPrice, ship, FillSource.FromHold(ship)));

        Assert.Equal(ContractStatus.Defaulted, wanted.Status);
        Assert.Equal(40m, ship.LotOf(kael.Id, GoodType.Food)!.Quantity);
        Assert.Equal(stock, camp.Stock(GoodType.Food));
        PaymentDefaultedEntry entry = context.Journal.Entries.OfType<PaymentDefaultedEntry>().Single();
        Assert.Equal((wanted.Id, camp.Id, kael.Id), (entry.Contract, entry.Payer, entry.Payee));
        Assert.True(context.Reputation.Get(camp.Id, ReputationContext.With(kael.Id)) < 0m);
        Assert.Equal(1, context.ContractsDefaulted);
        context.AssertSound();
    }

    [Fact]
    public void A_late_delivery_pays_the_late_fee_from_the_sellers_bond()
    {
        SimulationContext context = TestWorld.Quiet();
        ActorState habs = context.Actor("Meridian Habs");
        ActorState juno = context.Actor("Juno Okafor");
        Ship ship = context.ShipOf(juno);
        SaleContract wanted = context.PostWanted(habs, GoodType.Food, 60m, 800m, hours: 10); // Verdant → Meridian is 30 h
        FillSource source = DecisionEngine.FillSources(context, juno, wanted, ship).Single();
        Assert.Equal(Signing.Signed, Market.FillWithShip(context, juno, wanted, wanted.OfferedPrice, ship, source));
        Credits bond = wanted.LockedBond;
        Assert.Equal(Credits.Of(120m), bond); // standard terms: the late fee's cap, 15% of the price
        Credits habsBefore = habs.Credits;

        context.RunUntilSettled(wanted);

        GoodsHandedOverEntry handed = context.Journal.Entries.OfType<GoodsHandedOverEntry>().Single(e => e.Contract == wanted.Id);
        Assert.Equal(20, handed.TicksLate);
        Credits lateFee = wanted.LatePenalty!.PenaltyFor(20);
        Assert.Equal(Credits.Of(80m), lateFee); // 0.5% of the price an hour, 20 hours
        Assert.Equal(lateFee, handed.LateFee);

        // The buyer gets the late fee back out of escrow, on top of its goods.
        Assert.Equal(Round(habsBefore.Amount + lateFee.Amount), Round(habs.Credits.Amount));
        context.AssertSound();
    }

    [Fact]
    public void Medicine_spoiled_on_a_long_run_pays_a_warranty_refund_to_the_buyer()
    {
        SimulationContext context = TestWorld.Quiet();
        ActorState workforce = context.Actor("Tycho Workforce");
        ActorState pell = context.Actor("Pell Mirza");
        Ship ship = context.ShipOf(pell); // at Helios Prime, where the medicine is made: 75 h to Tycho in a standard hold
        SaleContract wanted = context.PostWanted(workforce, GoodType.Medicine, 10m, 1200m);
        FillSource source = DecisionEngine.FillSources(context, pell, wanted, ship).Single();
        Assert.Equal(Signing.Signed, Market.FillWithShip(context, pell, wanted, wanted.OfferedPrice, ship, source));

        context.RunUntilSettled(wanted);

        GoodsHandedOverEntry handed = context.Journal.Entries.OfType<GoodsHandedOverEntry>().Single(e => e.Contract == wanted.Id);
        decimal quality = Cargo.Fresh(GoodType.Medicine, 1m).Decayed(75, 0m).Quality;
        Assert.Equal(quality, handed.Quality);
        Assert.True(quality < Goods.StandardWarranty);
        Assert.Equal(Credits.Of(1200m * wanted.Warranty!.RefundFraction(quality)), handed.QualityRefund);
        Assert.True(handed.QualityRefund.Amount > 0m);
        context.AssertSound();
    }

    [Fact]
    public void A_short_delivery_is_paid_pro_rata()
    {
        SimulationContext context = TestWorld.Quiet();
        ActorState habs = context.Actor("Meridian Habs");
        ActorState juno = context.Actor("Juno Okafor");
        Ship ship = context.ShipOf(juno);
        SaleContract wanted = context.PostWanted(habs, GoodType.Food, 60m, 900m, hours: 200);
        Market.FillWithShip(context, juno, wanted, wanted.OfferedPrice, ship, DecisionEngine.FillSources(context, juno, wanted, ship).Single());
        context.RunTo(context.CurrentTick + 5); // loaded and on its way

        // A partial loss on the way, as an incident would cause it.
        ship.Alter(context.CurrentTick, juno.Id, GoodType.Food, cargo => cargo.LosingUnits(15m));
        context.Lose(GoodType.Food, 15m);
        context.RunUntilSettled(wanted);

        GoodsHandedOverEntry handed = context.Journal.Entries.OfType<GoodsHandedOverEntry>().Single(e => e.Contract == wanted.Id);
        Assert.Equal(45m, handed.Quantity);
        Assert.Equal(Credits.Of(900m * 45m / 60m), handed.Price);
        Assert.Equal(45m, wanted.Delivered);
        context.AssertSound();
    }

    [Fact]
    public void A_sale_nothing_is_delivering_goes_into_default_and_its_buyer_cancels_for_the_bond()
    {
        SimulationContext context = TestWorld.Quiet();
        ActorState habs = context.Actor("Meridian Habs");
        ActorState farm = context.Actor("Verdant Agricombine");
        SaleContract wanted = context.PostWanted(habs, GoodType.Food, 60m, 900m, hours: 120);
        Credits habsBefore = habs.Credits;
        Credits farmBefore = farm.Credits;

        // The farm sells it delivered and posts a haul nobody takes.
        Assert.Equal(Signing.Signed, Market.FillViaHaul(context, farm, wanted, wanted.OfferedPrice, FillSource.FromStock(farm), Credits.Of(5m), Credits.Of(6m), out HaulContract? haul));
        Assert.NotNull(haul);
        Assert.Equal(60m, farm.Reserved(GoodType.Food));
        Credits bond = wanted.LockedBond;

        context.RunTo(wanted.GraceEndsTick - 1);
        Assert.Equal(ContractStatus.Active, wanted.Status); // late, but inside its grace
        context.RunTo(wanted.GraceEndsTick);

        // In default, with nothing on its way: the buyer cancels by utility, and takes the seller's bond.
        Assert.Equal(ContractStatus.Cancelled, wanted.Status);
        Assert.Equal(new Cancellation(habs.Id, CancelReason.ForDefault, wanted.GraceEndsTick, bond), wanted.Cancellation);
        Assert.Equal(ContractStatus.Expired, haul!.Status);
        Assert.Equal(0m, farm.Reserved(GoodType.Food));
        ContractInDefaultEntry inDefault = context.Journal.Entries.OfType<ContractInDefaultEntry>().Single();
        Assert.Equal((wanted.Id, farm.Id, habs.Id), (inDefault.Contract, inDefault.Performer, inDefault.Payer));
        ContractCancelledEntry cancelled = context.Journal.Entries.OfType<ContractCancelledEntry>().Single();
        Assert.Equal((wanted.Id, habs.Id, CancelReason.ForDefault, bond, (long?)inDefault.Seq), (cancelled.Contract, cancelled.By, cancelled.Reason, cancelled.Paid, cancelled.Cause));

        // The buyer's escrow came back with the seller's bond on top; the seller lost its bond.
        Assert.Equal(Round(habsBefore.Amount + bond.Amount), Round(habs.Credits.Amount));
        Assert.Equal(Round(farmBefore.Amount - bond.Amount), Round(farm.Credits.Amount));
        context.AssertSound();
    }

    [Fact]
    public void Losing_the_cargo_fails_the_haul_and_the_sale_it_was_delivering()
    {
        SimulationContext context = TestWorld.Quiet();
        ActorState habs = context.Actor("Meridian Habs");
        ActorState farm = context.Actor("Verdant Agricombine");
        ActorState juno = context.Actor("Juno Okafor");
        Ship ship = context.ShipOf(juno);
        SaleContract wanted = context.PostWanted(habs, GoodType.Food, 60m, 900m, hours: 200);
        Market.FillViaHaul(context, farm, wanted, wanted.OfferedPrice, FillSource.FromStock(farm), Credits.Of(80m), Credits.Of(100m), out HaulContract? haul);
        Assert.Equal(Signing.Signed, Market.TakeHaul(context, juno, haul!, ship, haul!.OfferedPrice));
        context.RunTo(context.CurrentTick + 5);
        Assert.Equal(60m, ship.LotOf(farm.Id, GoodType.Food)!.Quantity); // the farm's goods, in Juno's hold
        Credits farmBefore = farm.Credits;

        ship.Alter(context.CurrentTick, farm.Id, GoodType.Food, cargo => cargo.LosingUnits(cargo.Quantity));
        context.Lose(GoodType.Food, 60m);
        context.RunUntilSettled(haul);

        Assert.Equal(ContractStatus.Breached, haul.Status);
        Assert.Equal(ContractStatus.Breached, wanted.Status);
        var reasons = context.Journal.Entries.OfType<DeliveryFailedEntry>().Select(e => (e.Contract, e.Performer, e.Reason)).ToArray();
        Assert.Equal(new[] { (haul.Id, juno.Id, FailureReason.CargoLost), (wanted.Id, farm.Id, FailureReason.CargoLost) }, reasons);

        // The farm got its haul escrow back and the carrier's bond; its own bond on the sale (locked before) went to the Habs.
        decimal expected = farmBefore.Amount + haul.Price.Amount + haul.Tax.Amount + haul.LockedBond.Amount;
        Assert.Equal(Round(expected), Round(farm.Credits.Amount));
        Assert.True(ship.IsFree);
        context.AssertSound();
    }

    [Fact]
    public void Buying_at_a_source_below_the_producers_ask_finds_no_seller_and_is_withdrawn()
    {
        SimulationContext context = TestWorld.Quiet();
        ActorState farm = context.Actor("Verdant Agricombine");
        ActorState juno = context.Actor("Juno Okafor");
        Ship ship = context.ShipOf(juno);
        decimal ask = DecisionEngine.Ask(farm).Amount * 20m;

        SaleContract? cheap = Market.BuyAtSource(context, juno, GoodType.Food, 20m, farm.Home, Credits.Of(ask * 0.5m), Holding.Aboard(ship.Id, farm.Home), context.CurrentTick + 24);
        SaleContract? fair = Market.BuyAtSource(context, juno, GoodType.Food, 20m, farm.Home, Credits.Of(ask), Holding.Aboard(ship.Id, farm.Home), context.CurrentTick + 24);

        Assert.Null(cheap);
        Assert.Contains(context.Journal.Entries.OfType<ContractWithdrawnEntry>(), e => context.Contract(e.Contract).PostedBy == juno.Id);
        Assert.NotNull(fair);
        Assert.Equal((ContractStatus.Active, farm.Id), (fair!.Status, fair.Seller!.Value));
    }

    internal static void GiveAboard(SimulationContext context, Ship ship, ActorState owner, GoodType good, decimal quantity)
    {
        // Goods have to come from somewhere: made at a producer, then handed to the owner and put aboard.
        ActorState maker = context.ProducersOf(good).First();
        context.Produce(maker, good, quantity);
        context.Load(ship, owner.Id, context.TakeFromStock(maker, good, quantity, reserved: false));
    }

    private static bool Concerns(JournalEntry entry, ContractId id) => entry switch
    {
        ContractPostedEntry e => e.Contract == id,
        ContractSignedEntry e => e.Contract == id,
        GoodsHandedOverEntry e => e.Contract == id,
        _ => false,
    };

    private static decimal Round(decimal amount) => System.Math.Round(amount, 2);
}
