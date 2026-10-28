using System.Linq;
using ByGalacticAccord.Engine.Actors;
using ByGalacticAccord.Engine.Domain;
using ByGalacticAccord.Engine.Simulation;

namespace ByGalacticAccord.Tests;

/// <summary>
/// Every failure path of design §16.3 that Slice 1 has by S4, for an AI actor and through the player's commands, each
/// settled by the contract's terms (plan S4). Some paths are covered where their kind's lifecycle is tested:
/// <list type="bullet">
/// <item>late delivery: AI, <see cref="SaleTests.A_late_delivery_pays_the_late_fee_from_the_sellers_bond"/>; player, here;</item>
/// <item>default by the performer: AI cancels, <see cref="SaleTests.A_sale_nothing_is_delivering_goes_into_default_and_its_buyer_cancels_for_the_bond"/>;
/// AI waits, the player cancels or waits, and the player in default: here;</item>
/// <item>short delivery: AI pro rata, <see cref="SaleTests.A_short_delivery_is_paid_pro_rata"/>; AI as a failure, and the player: here;</item>
/// <item>quality shortfall: AI refund, <see cref="SaleTests.Medicine_spoiled_on_a_long_run_pays_a_warranty_refund_to_the_buyer"/>; rejection by an AI
/// and by the player, and the player accepting: here;</item>
/// <item>cancellation at will, AI and player: here;</item>
/// <item>payer default: AI, <see cref="SaleTests.Outside_the_Accord_a_buyer_who_cannot_pay_defaults_and_the_seller_keeps_the_goods"/>; player, here;</item>
/// <item>expiry: AI, <see cref="SaleTests.An_unsold_lot_expires_and_its_goods_are_free_again"/>; player, here.</item>
/// </list>
/// Substitution and supply failure arrive with ship parts (S7), insolvency with debt (S8).
/// </summary>
public sealed class FailurePathTests
{
    [Fact]
    public void The_player_delivering_a_haul_late_pays_the_late_fee_from_its_bond()
    {
        SimulationContext context = TestWorld.Quiet(withPlayer: true);
        ActorState player = context.Player();
        ActorState farm = context.Actor("Verdant Agricombine");
        HaulContract haul = PlayerActionsTests.FarmHaul(context, hours: 50); // 60 h from Meridian and back: 10 h late, inside the grace
        Assert.Equal(Signing.Signed, PlayerActions.TakeHaul(context, player, haul, context.ShipOf(player)));
        Credits bond = haul.LockedBond;
        Credits playerBefore = player.Credits;
        Credits farmBefore = farm.Credits;

        context.RunUntilSettled(haul);

        HaulDeliveredEntry delivered = context.Journal.Entries.OfType<HaulDeliveredEntry>().Single();
        Assert.Equal(ContractStatus.Fulfilled, haul.Status);
        Assert.Equal(10, delivered.TicksLate);
        Assert.Equal(haul.LatePenalty!.PenaltyFor(10), delivered.LateFee);
        Assert.True(delivered.LateFee <= bond);
        Assert.Empty(context.Journal.Entries.OfType<ContractInDefaultEntry>());

        // The player is paid the fee and its bond less the late fee, and pays docking; the farm gets the late fee back.
        Assert.Equal(Round(playerBefore.Amount + haul.Price.Amount + bond.Amount - delivered.LateFee.Amount - context.Config.DockingFee.Amount), Round(player.Credits.Amount));
        Assert.Equal(Round(farmBefore.Amount + delivered.LateFee.Amount), Round(farm.Credits.Amount));
        context.AssertSound();
    }

    [Fact]
    public void An_ai_buyer_waits_on_a_sale_in_default_while_the_goods_are_on_their_way()
    {
        SimulationContext context = TestWorld.Quiet();
        ActorState habs = context.Actor("Meridian Habs");
        ActorState juno = context.Actor("Juno Okafor");
        Ship ship = context.ShipOf(juno); // at Verdant, 30 h from Meridian
        SaleContract wanted = context.PostWanted(habs, GoodType.Food, 60m, 800m, hours: 10, TermPackage.Strict); // 6 h of grace
        Assert.Equal(Signing.Signed, Market.FillWithShip(context, juno, wanted, wanted.OfferedPrice, ship, DecisionEngine.FillSources(context, juno, wanted, ship).Single()));

        context.RunTo(wanted.GraceEndsTick);

        // In default with the goods aboard and due: the goods are worth more to the Habs than its money back and the bond.
        Assert.True(wanted.InDefault(context.CurrentTick));
        Assert.Single(context.Journal.Entries.OfType<ContractInDefaultEntry>());
        Assert.False(DecisionEngine.WouldCancel(context, habs, wanted));
        Assert.Equal(ContractStatus.Active, wanted.Status);

        context.RunUntilSettled(wanted);

        GoodsHandedOverEntry handed = context.Journal.Entries.OfType<GoodsHandedOverEntry>().Single(e => e.Contract == wanted.Id);
        Assert.Equal(ContractStatus.Fulfilled, wanted.Status);
        Assert.Equal(20, handed.TicksLate);
        Assert.Equal(wanted.LatePenalty!.PenaltyFor(20), handed.LateFee);
        context.AssertSound();
    }

    [Fact]
    public void The_player_whose_carrier_defaults_is_asked_and_the_world_waits_for_its_answer()
    {
        (SimulationContext context, SaleContract sale, HaulContract haul) = PlayerBrokerWithLateCarrier();
        ActorState player = context.Player();

        RunStop stop = context.RunTo(context.CurrentTick + 500);

        Assert.Equal(RunStop.PlayerDecision, stop);
        Assert.Equal(haul.GraceEndsTick, context.CurrentTick);
        PlayerDecision decision = Assert.Single(context.PlayerDecisions);
        Assert.Equal((DecisionKind.CancelForDefault, haul.Id), (decision.Kind, decision.Contract));
        Assert.Equal(new[] { DecisionChoice.Cancel, DecisionChoice.Wait }, decision.Choices);
        ContractInDefaultEntry inDefault = context.Journal.Entries.OfType<ContractInDefaultEntry>().Single(e => e.Contract == haul.Id);
        Assert.Equal(inDefault.Seq, decision.Entry);

        // The hard pause: nothing moves until the player answers.
        long tick = context.CurrentTick;
        long recorded = context.Journal.TotalRecorded;
        Assert.Equal(RunStop.PlayerDecision, context.RunTo(tick + 100));
        Assert.Equal((tick, recorded), (context.CurrentTick, context.Journal.TotalRecorded));
        Assert.False(PlayerActions.Decide(context, player, decision, DecisionChoice.Accept)); // not an answer to this question
        Assert.Equal(ContractStatus.Active, sale.Status);
    }

    [Fact]
    public void The_player_cancels_its_carrier_for_default_takes_the_bond_and_the_goods_still_arrive()
    {
        (SimulationContext context, SaleContract sale, HaulContract haul) = PlayerBrokerWithLateCarrier();
        ActorState player = context.Player();
        ActorState pell = context.Actor("Pell Mirza");
        context.RunTo(context.CurrentTick + 500);
        Credits held = haul.EscrowHeld;
        Credits bond = haul.LockedBond;
        Credits before = player.Credits;
        Credits pellBefore = pell.Credits;

        Assert.True(PlayerActions.Decide(context, player, context.PlayerDecisions.Single(), DecisionChoice.Cancel));

        // Its escrow back with the carrier's bond: the fee and tax it locked, and the bond.
        Assert.Equal(ContractStatus.Cancelled, haul.Status);
        Assert.Equal(new Cancellation(player.Id, CancelReason.ForDefault, context.CurrentTick, bond), haul.Cancellation);
        Assert.Equal(Round(before.Amount + held.Amount), Round(player.Credits.Amount));
        Assert.False(context.HasPlayerDecisions);

        // The goods are the player's and already aboard: Pell still sets them down at Meridian, unpaid, where they are handed
        // over (late) under the player's own sale, which the Habs was waiting on.
        context.RunUntilSettled(sale);
        Assert.Equal(ContractStatus.Fulfilled, sale.Status);
        Assert.True(context.Journal.Entries.OfType<GoodsHandedOverEntry>().Single(e => e.Contract == sale.Id).TicksLate > 0);
        Assert.True(context.Ship(haul.AssignedShip!.Value).IsFree);
        Assert.Equal(Round(pellBefore.Amount - context.Config.DockingFee.Amount), Round(pell.Credits.Amount));
        Assert.True(context.Reputation.Get(pell.Id, ReputationContext.With(player.Id)) < 0m);
        context.AssertSound();
    }

    [Fact]
    public void The_player_waits_on_its_carrier_and_gets_the_late_fee_when_the_goods_come()
    {
        (SimulationContext context, SaleContract sale, HaulContract haul) = PlayerBrokerWithLateCarrier();
        ActorState player = context.Player();
        context.RunTo(context.CurrentTick + 500);

        Assert.True(PlayerActions.Decide(context, player, context.PlayerDecisions.Single(), DecisionChoice.Wait));
        Assert.False(context.HasPlayerDecisions);
        context.RunUntilSettled(haul);

        // The carrier delivers late: its late fee, capped, goes to the player; the player's own sale is handed over late too.
        HaulDeliveredEntry delivered = context.Journal.Entries.OfType<HaulDeliveredEntry>().Single(e => e.Contract == haul.Id);
        Assert.Equal(ContractStatus.Fulfilled, haul.Status);
        Assert.Equal(haul.LatePenalty!.MaxPenalty, delivered.LateFee); // well past the cap's hours
        Assert.Equal(ContractStatus.Fulfilled, sale.Status);
        GoodsHandedOverEntry handed = context.Journal.Entries.OfType<GoodsHandedOverEntry>().Single(e => e.Contract == sale.Id);
        Assert.True(handed.TicksLate > 0);
        Assert.Equal((long?)delivered.Seq, handed.Cause); // late because its haul was late
        context.AssertSound();
    }

    [Fact]
    public void The_player_in_default_on_a_haul_is_cancelled_and_loses_its_bond_but_sets_the_goods_down()
    {
        SimulationContext context = TestWorld.Quiet(withPlayer: true);
        ActorState player = context.Player();
        ActorState farm = context.Actor("Verdant Agricombine");
        HaulContract haul = PlayerActionsTests.FarmHaul(context, hours: 20); // 60 h from Meridian and back: 40 h late, past the grace
        Ship ship = context.ShipOf(player);
        Assert.Equal(Signing.Signed, PlayerActions.TakeHaul(context, player, haul, ship));
        Credits bond = haul.LockedBond;
        Credits playerBefore = player.Credits;

        context.RunUntilSettled(haul);
        context.RunTo(context.CurrentTick + 100);

        // The farm cancelled at the end of the grace and took the bond; the player flew on and set the goods down at Meridian.
        Assert.Equal(ContractStatus.Cancelled, haul.Status);
        Assert.Equal((farm.Id, CancelReason.ForDefault, bond), (haul.Cancellation!.By, haul.Cancellation.Reason, haul.Cancellation.Paid));
        Assert.Equal(haul.GraceEndsTick, haul.Cancellation.Tick);
        Assert.True(ship.IsFree);
        Assert.Equal(40m, context.StorageOf(farm.Id, haul.Destination)!.Held(GoodType.Food));
        Assert.Equal(Round(playerBefore.Amount - context.Config.DockingFee.Amount), Round(player.Credits.Amount)); // no fee, the bond already gone
        context.AssertSound();
    }

    [Fact]
    public void A_short_delivery_under_strict_terms_fails_and_the_seller_keeps_what_arrived()
    {
        SimulationContext context = TestWorld.Quiet();
        ActorState habs = context.Actor("Meridian Habs");
        ActorState juno = context.Actor("Juno Okafor");
        Ship ship = context.ShipOf(juno);
        SaleContract wanted = context.PostWanted(habs, GoodType.Food, 60m, 900m, hours: 200, TermPackage.Strict);
        Market.FillWithShip(context, juno, wanted, wanted.OfferedPrice, ship, DecisionEngine.FillSources(context, juno, wanted, ship).Single());
        Credits habsBefore = habs.Credits;
        Credits bond = wanted.LockedBond;
        context.RunTo(context.CurrentTick + 5);
        LoseAboard(context, ship, juno, GoodType.Food, 15m);

        context.RunUntilSettled(wanted);

        DeliveryFailedEntry failed = context.Journal.Entries.OfType<DeliveryFailedEntry>().Single(e => e.Contract == wanted.Id);
        Assert.Equal(ContractStatus.Breached, wanted.Status);
        Assert.Equal((FailureReason.ShortDelivery, 45m), (failed.Reason, failed.Delivered));
        Assert.Equal(45m, ship.LotOf(juno.Id, GoodType.Food)!.Quantity); // what arrived stays Juno's
        Assert.Equal(Round(habsBefore.Amount + wanted.Price.Amount * 1.04m + bond.Amount), Round(habs.Credits.Amount));
        context.AssertSound();
    }

    [Fact]
    public void The_player_short_on_a_haul_is_paid_pro_rata_and_owes_for_the_goods_it_lost()
    {
        SimulationContext context = TestWorld.Quiet(withPlayer: true);
        ActorState player = context.Player();
        ActorState farm = context.Actor("Verdant Agricombine");
        HaulContract haul = PlayerActionsTests.FarmHaul(context); // standard terms: pro rata, liability half the goods' value
        Ship ship = context.ShipOf(player);
        PlayerActions.TakeHaul(context, player, haul, ship);
        context.RunTo(context.CurrentTick + 35); // loaded at Verdant and on its way
        LoseAboard(context, ship, farm, GoodType.Food, 10m);
        Credits bond = haul.LockedBond;
        Credits playerBefore = player.Credits;

        context.RunUntilSettled(haul);

        // Paid for 30 of 40; owing 10 lost units at their declared value, which at the Accord's edge is collected only as far
        // as escrow holds: the payment and the bond.
        HaulDeliveredEntry delivered = context.Journal.Entries.OfType<HaulDeliveredEntry>().Single();
        Credits payment = Credits.Of(haul.Price.Amount * 30m / 40m);
        Credits owed = haul.Terms.Liability!.For(10m);
        Credits collected = Credits.Of(System.Math.Min(owed.Amount, payment.Amount + bond.Amount));
        Assert.Equal(ContractStatus.PartlyFulfilled, haul.Status);
        Assert.Equal((30m, payment, collected), (delivered.Quantity, delivered.Fee, delivered.Liability));
        Assert.Equal(Round(playerBefore.Amount + payment.Amount + bond.Amount - collected.Amount - context.Config.DockingFee.Amount), Round(player.Credits.Amount));
        Assert.Equal(30m, context.StorageOf(farm.Id, haul.Destination)!.Held(GoodType.Food)); // what arrived is the farm's, at Meridian
        Assert.Equal(1, context.Failures(FailureKind.ShortProRata));
        Assert.Equal(1, context.Failures(FailureKind.CargoLiability));
        context.AssertSound();
    }

    [Fact]
    public void Deep_accord_courts_collect_liability_beyond_escrow_where_the_edge_stops_at_what_it_holds()
    {
        decimal Charged(string destination)
        {
            SimulationContext context = TestWorld.Quiet();
            ActorState farm = context.Actor("Verdant Agricombine");
            ActorState juno = context.Actor("Juno Okafor");
            Ship ship = context.ShipOf(juno);
            HaulContract haul = Market.PostHaul(
                context, farm, GoodType.Food, 60m, Holding.StockAt(farm.Home), context.Place(destination).Id, Credits.Of(40m), Credits.Of(40m),
                context.CurrentTick + 300, serves: null, TermPackage.Strict);
            Market.TakeHaul(context, juno, haul, ship, haul.OfferedPrice);
            context.RunTo(context.CurrentTick + 1);
            LoseAboard(context, ship, farm, GoodType.Food, 60m); // the whole cargo
            Credits before = juno.Credits;
            context.RunUntilSettled(haul);
            Assert.Equal(ContractStatus.Breached, haul.Status);
            context.AssertSound();
            return before.Amount - juno.Credits.Amount - context.Config.DockingFee.Amount;
        }

        // Strict liability is the goods' whole value, ₵600; the bond is 50% of the ₵40 fee.
        Assert.Equal(0m, Charged("Meridian Station")); // Accord edge: the bond, already in escrow, is all it loses
        Assert.Equal(600m - 20m, Charged("Helios Prime")); // deep Accord: the rest from its account
    }

    [Fact]
    public void An_ai_buyer_rejects_goods_below_the_floor_and_the_seller_keeps_them()
    {
        SimulationContext context = TestWorld.Quiet();
        ActorState workforce = context.Actor("Tycho Workforce");
        ActorState pell = context.Actor("Pell Mirza");
        Ship ship = context.ShipOf(pell); // at Helios Prime: 75 h to Tycho, and strict terms reject medicine below 60% fresh
        SaleContract wanted = context.PostWanted(workforce, GoodType.Medicine, 10m, 1200m, package: TermPackage.Strict);
        Market.FillWithShip(context, pell, wanted, wanted.OfferedPrice, ship, DecisionEngine.FillSources(context, pell, wanted, ship).Single());
        Credits before = workforce.Credits;
        Credits bond = wanted.LockedBond;
        decimal stock = workforce.Stock(GoodType.Medicine);

        context.RunUntilSettled(wanted);

        DeliveryFailedEntry rejected = context.Journal.Entries.OfType<DeliveryFailedEntry>().Single();
        decimal quality = Cargo.Fresh(GoodType.Medicine, 1m).Decayed(75, 0m).Quality;
        Assert.True(wanted.Warranty!.Rejectable(quality));
        Assert.Equal((ContractStatus.Breached, FailureReason.Rejected, quality), (wanted.Status, rejected.Reason, rejected.Quality));
        Assert.Equal(10m, ship.LotOf(pell.Id, GoodType.Medicine)!.Quantity);
        Assert.Equal(stock, workforce.Stock(GoodType.Medicine));
        Assert.Equal(Round(before.Amount + wanted.Price.Amount * 1.04m + bond.Amount), Round(workforce.Credits.Amount));
        context.AssertSound();
    }

    [Fact]
    public void The_player_inspects_goods_below_the_floor_and_rejects_them()
    {
        (SimulationContext context, SaleContract lot, Ship ship) = PlayerCollectsAStaleLot();
        ActorState player = context.Player();
        ActorState sable = context.Actor("Sable Vance");
        Credits held = lot.EscrowHeld - lot.LockedBond;
        Credits bond = lot.LockedBond;
        Credits before = player.Credits;

        PlayerDecision decision = Assert.Single(context.PlayerDecisions);
        Assert.Equal((DecisionKind.Inspection, lot.Id, 40m), (decision.Kind, decision.Contract, decision.Quantity));
        Assert.Equal(context.Place("Halcyon Refinery").Id, ship.Location); // the ship waits at the pickup
        Assert.True(PlayerActions.Decide(context, player, decision, DecisionChoice.Reject));
        context.RunTo(context.CurrentTick + 1);

        Assert.Equal(ContractStatus.Breached, lot.Status);
        Assert.Equal(FailureReason.Rejected, context.Journal.Entries.OfType<DeliveryFailedEntry>().Single().Reason);
        Assert.Equal(Round(before.Amount + held.Amount + bond.Amount), Round(player.Credits.Amount));
        Assert.Empty(ship.Hold);
        Assert.True(ship.IsFree);
        Assert.Equal(40m, context.StorageOf(sable.Id, ship.Location)!.Available(GoodType.Food)); // still Sable's, free again
        context.AssertSound();
    }

    [Fact]
    public void The_player_inspects_goods_below_the_floor_and_accepts_them_with_the_refund()
    {
        (SimulationContext context, SaleContract lot, Ship ship) = PlayerCollectsAStaleLot();
        ActorState player = context.Player();
        PlayerDecision decision = context.PlayerDecisions.Single();

        Assert.True(PlayerActions.Decide(context, player, decision, DecisionChoice.Accept));
        context.RunTo(context.CurrentTick + 1);

        GoodsHandedOverEntry handed = context.Journal.Entries.OfType<GoodsHandedOverEntry>().Single(e => e.Contract == lot.Id);
        Assert.Equal(ContractStatus.Fulfilled, lot.Status);
        Assert.Equal(decision.Quality, handed.Quality);
        Assert.Equal(Credits.Of(lot.Price.Amount * lot.Warranty!.RefundFraction(decision.Quality)), handed.QualityRefund);
        Assert.Equal(40m, ship.LotOf(player.Id, GoodType.Food)!.Quantity);
        Assert.True(ship.IsFree);
        context.AssertSound();
    }

    [Fact]
    public void A_producer_that_can_no_longer_deliver_walks_away_and_pays_the_break_fee()
    {
        SimulationContext context = TestWorld.Quiet();
        ActorState habs = context.Actor("Meridian Habs");
        ActorState farm = context.Actor("Verdant Agricombine");
        SaleContract wanted = context.PostWanted(habs, GoodType.Food, 60m, 900m, hours: 120);
        Market.FillViaHaul(context, farm, wanted, wanted.OfferedPrice, FillSource.FromStock(farm), Credits.Of(5m), Credits.Of(5m), out HaulContract? haul);
        Credits habsBefore = habs.Credits;

        // Its haul expires untaken at the deadline; a day later no carrier could make the grace, and walking away costs it
        // less than its bond.
        context.RunTo(wanted.DeadlineTick + 1);
        Assert.Equal(ContractStatus.Expired, haul!.Status);
        Assert.True(DecisionEngine.WouldWalkAway(farm, wanted));
        DecisionEngine.RunProducer(context, farm);

        Assert.Equal(ContractStatus.Cancelled, wanted.Status);
        Assert.Equal((farm.Id, CancelReason.AtWill, wanted.Terms.BreakFee), (wanted.Cancellation!.By, wanted.Cancellation.Reason, wanted.Cancellation.Paid));
        Assert.Null(wanted.Reserved);
        Assert.Equal(0m, farm.Reserved(GoodType.Food)); // its goods are free again
        Assert.Equal(Round(habsBefore.Amount + wanted.Price.Amount * 1.04m + wanted.Terms.BreakFee.Amount), Round(habs.Credits.Amount));
        context.AssertSound();
    }

    [Fact]
    public void The_player_walks_away_from_a_haul_and_pays_the_break_fee_from_its_bond()
    {
        SimulationContext context = TestWorld.Quiet(withPlayer: true);
        ActorState player = context.Player();
        ActorState farm = context.Actor("Verdant Agricombine");
        HaulContract haul = PlayerActionsTests.FarmHaul(context);
        Ship ship = context.ShipOf(player);
        PlayerActions.TakeHaul(context, player, haul, ship);
        Credits playerBefore = player.Credits;
        Credits farmBefore = farm.Credits;

        Assert.True(PlayerActions.CancelAtWill(context, player, haul));
        context.RunTo(context.CurrentTick + 40);

        // The break fee (20% of the fee) comes out of its bond; the rest of the bond comes back. The ship arrives at Verdant
        // with nothing to load, and the farm's goods are free again.
        Credits fee = haul.Terms.BreakFee;
        Assert.Equal(ContractStatus.Cancelled, haul.Status);
        Assert.Equal((player.Id, CancelReason.AtWill, fee), (haul.Cancellation!.By, haul.Cancellation.Reason, haul.Cancellation.Paid));
        Assert.Equal(Round(playerBefore.Amount + haul.LockedBond.Amount - fee.Amount), Round(player.Credits.Amount));
        Assert.Equal(Round(farmBefore.Amount + haul.Price.Amount * 1.04m + fee.Amount), Round(farm.Credits.Amount));
        Assert.True(ship.IsFree);
        Assert.Empty(ship.Hold);
        Assert.Equal(0m, farm.Reserved(GoodType.Food));
        Assert.False(PlayerActions.CancelAtWill(context, player, haul));
        context.AssertSound();
    }

    [Fact]
    public void The_player_who_cannot_pay_its_carrier_on_the_frontier_defaults_and_nobody_enforces_it()
    {
        SimulationContext context = TestWorld.Quiet(withPlayer: true);
        ActorState player = context.Player();
        ActorState camp = context.Actor("Rust Belt Camp");
        ActorState juno = context.Actor("Juno Okafor");
        context.PresentAt(player, "Rust Belt");
        SaleContract wanted = context.PostWanted(camp, GoodType.Food, 40m, 900m, hours: 400);
        PlayerActions.BrokerFeasibility check = PlayerActions.CheckBroker(context, player, wanted);
        Assert.True(check.Ok);
        Assert.Equal(Signing.Signed, PlayerActions.Broker(context, player, wanted, check.Source!, check.Fee));
        HaulContract haul = context.Contracts.Values.OfType<HaulContract>().Single(h => h.Serves == wanted.Id);
        Assert.False(haul.UsesEscrow); // performed at Rust Belt: outside the Accord, paid at delivery
        Assert.Equal(Signing.Signed, Market.TakeHaul(context, juno, haul, context.ShipOf(juno), haul.OfferedPrice));
        context.ApplySink(player, player.Credits); // broke by the time the goods arrive
        Credits junoBefore = juno.Credits;

        context.RunUntilSettled(haul);

        // The player couldn't pay Juno: a default, and Juno's only remedy is its memory of the player. The goods still went
        // on to the camp, which paid the player for them.
        PaymentDefaultedEntry defaulted = context.Journal.Entries.OfType<PaymentDefaultedEntry>().Single();
        Assert.Equal((haul.Id, player.Id, juno.Id), (defaulted.Contract, defaulted.Payer, defaulted.Payee));
        Assert.Equal(ContractStatus.Defaulted, haul.Status);
        Assert.Equal(Round(junoBefore.Amount - context.Config.DockingFee.Amount), Round(juno.Credits.Amount));
        Assert.True(context.Reputation.Get(player.Id, ReputationContext.With(juno.Id)) < 0m);
        Assert.Equal(ContractStatus.Fulfilled, wanted.Status);
        Assert.Equal(Round(wanted.Price.Amount), Round(player.Credits.Amount));
        context.AssertSound();
    }

    [Fact]
    public void The_players_untaken_haul_can_be_raised_and_then_expires_and_its_sale_is_cancelled()
    {
        SimulationContext context = TestWorld.Quiet(withPlayer: true);
        ActorState player = context.Player();
        ActorState habs = context.Actor("Meridian Habs");
        SaleContract wanted = context.PostWanted(habs, GoodType.Food, 40m, 700m, hours: 200);
        PlayerActions.BrokerFeasibility check = PlayerActions.CheckBroker(context, player, wanted);
        PlayerActions.Broker(context, player, wanted, check.Source!, check.Fee);
        HaulContract first = context.Contracts.Values.OfType<HaulContract>().Single(h => h.Serves == wanted.Id);
        context.RunTo(context.CurrentTick + 30);

        // Nobody took it: post it again, a quarter dearer.
        PlayerActions.HaulAgainFeasibility again = PlayerActions.CheckHaulAgain(context, player, wanted);
        Assert.True(again.Ok);
        Assert.Equal(DecisionEngine.RaisedHaulOffer(first), again.Fee);
        HaulContract second = PlayerActions.HaulAgain(context, player, wanted, again.From!.Value, again.Fee);
        Assert.Equal(ContractStatus.Withdrawn, first.Status);
        Assert.Equal((ContractStatus.Open, again.Fee), (second.Status, second.OfferedPrice));

        // Nobody takes that either: it expires, and when the sale's grace ends the Habs cancels it for the player's bond.
        context.RunTo(wanted.GraceEndsTick);
        Assert.Equal(ContractStatus.Expired, second.Status);
        Assert.Equal(ContractStatus.Cancelled, wanted.Status);
        Assert.Equal((habs.Id, CancelReason.ForDefault), (wanted.Cancellation!.By, wanted.Cancellation.Reason));
        Assert.Equal(40m, context.StorageOf(player.Id, check.Source!.At)!.Available(GoodType.Food)); // the goods are the player's, in storage
        context.AssertSound();
    }

    // The player fills the Habs' wanted food through a carrier, and Pell Mirza takes the haul so late that its ship (from
    // Helios Prime, by way of Verdant) arrives long after the haul's grace.
    internal static (SimulationContext Context, SaleContract Sale, HaulContract Haul) PlayerBrokerWithLateCarrier()
    {
        SimulationContext context = TestWorld.Quiet(withPlayer: true);
        ActorState player = context.Player();
        ActorState habs = context.Actor("Meridian Habs");
        ActorState pell = context.Actor("Pell Mirza");
        SaleContract wanted = context.PostWanted(habs, GoodType.Food, 40m, 700m, hours: 90);
        PlayerActions.BrokerFeasibility check = PlayerActions.CheckBroker(context, player, wanted);
        Assert.True(check.Ok);
        Assert.Equal(Signing.Signed, PlayerActions.Broker(context, player, wanted, check.Source!, check.Fee));
        HaulContract haul = context.Contracts.Values.OfType<HaulContract>().Single(h => h.Serves == wanted.Id);
        Assert.Equal((player.Id, TermPackage.Standard), (haul.Shipper, haul.Terms.Package));

        context.RunTo(context.CurrentTick + 60);
        Assert.Equal(Signing.Signed, Market.TakeHaul(context, pell, haul, context.ShipOf(pell), haul.OfferedPrice));
        Assert.True(Market.NextFree(context, context.ShipOf(pell)).Tick > haul.GraceEndsTick);
        return (context, wanted, haul);
    }

    // Sable Vance has 40 food in storage at Halcyon at 30% fresh and puts it up as a lot for an hour; the player buys it
    // and its ship, 75 h away, collects it: by then it has decayed below the floor of its warranty "as seen".
    internal static (SimulationContext Context, SaleContract Lot, Ship Ship) PlayerCollectsAStaleLot()
    {
        SimulationContext context = TestWorld.Quiet(withPlayer: true);
        ActorState player = context.Player();
        ActorState sable = context.Actor("Sable Vance");
        ActorState farm = context.Actor("Verdant Agricombine");
        LocationId halcyon = context.Place("Halcyon Refinery").Id;
        context.PresentAt(player, "Halcyon Refinery"); // so it sees the lot (design §7.1)
        context.PutInStorage(sable, halcyon, context.TakeFromStock(farm, GoodType.Food, 40m, reserved: false) with { Quality = 0.3m });
        SaleContract lot = Market.PostSale(
            context, sable, SaleSide.ForSale, GoodType.Food, 40m, halcyon, Credits.Of(120m), Credits.Of(100m), context.CurrentTick + 1,
            Holding.StorageAt(halcyon), TermPackage.Standard);
        Ship ship = context.ShipOf(player);
        Assert.True(PlayerActions.CheckBuy(context, player, lot).Ok);
        Assert.Equal(Signing.Signed, PlayerActions.Buy(context, player, lot, ship));

        Assert.Equal(RunStop.PlayerDecision, context.RunTo(context.CurrentTick + 200));
        decimal quality = context.StorageOf(sable.Id, halcyon)!.Peek(GoodType.Food, context.CurrentTick)!.Value.Quality;
        Assert.True(lot.Warranty!.Rejectable(quality));
        return (context, lot, ship);
    }

    // A partial loss on the way, as an incident would cause it.
    internal static void LoseAboard(SimulationContext context, Ship ship, ActorState owner, GoodType good, decimal units)
    {
        ship.Alter(context.CurrentTick, owner.Id, good, cargo => cargo.LosingUnits(units));
        context.Lose(good, units);
    }

    private static decimal Round(decimal amount) => System.Math.Round(amount, 2);
}
