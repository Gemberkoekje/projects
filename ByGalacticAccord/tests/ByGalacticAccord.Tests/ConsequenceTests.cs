using System;
using System.Linq;
using ByGalacticAccord.Engine.Actors;
using ByGalacticAccord.Engine.Domain;
using ByGalacticAccord.Engine.Economy;
using ByGalacticAccord.Engine.Events;
using ByGalacticAccord.Engine.Simulation;
using ByGalacticAccord.Presentation.Session;
using ByGalacticAccord.Presentation.Text;
using ByGalacticAccord.Presentation.Views;

namespace ByGalacticAccord.Tests;

/// <summary>
/// Plan S8, consequences (design §5.4, §8.4, §16.7, §16.8): debt below ₵0 with interest; a performer signs only on its whole bond;
/// the deep Accord enforces remedies into debt; a warning above half of what an actor owns and insolvency after seven days there or
/// above all of it; insolvency for an AI actor (contracts cancelled, fire sales, dissolution) and for the player (the game ends,
/// the legacy tells its story); the throttle; and the first hour's trap, visibly a trap.
/// </summary>
public sealed class ConsequenceTests
{
    [Fact]
    public void Running_costs_and_interest_keep_charging_below_zero()
    {
        SimulationContext context = TestWorld.Quiet(withPlayer: true);
        ActorState player = context.Player();
        context.ApplySink(player, player.Credits - Credits.Of(1m)); // ₵1 left

        // A day of a Light Freighter costs ₵4.32 whatever its owner has, and the debt that leaves costs 0.2% a day.
        Heartbeat(context, player);
        Assert.Equal(-3.33m, player.Credits.Amount); // 1 − 4.32, then 0.2% of 3.32
        Assert.Equal(Credits.Of(3.33m), player.Debt);
        Heartbeat(context, player);
        Assert.Equal(-7.67m, player.Credits.Amount); // − 4.32, then 0.2% of 7.65
        Assert.Equal(0.03m, context.Sunk(SinkKind.Interest).Amount);
        Assert.Equal(SolvencyStatus.None, player.Solvency); // a few credits against an ₵8,000 ship
        context.AssertSound();
    }

    [Fact]
    public void Docking_fees_and_rent_go_to_the_station_operator_and_keep_charging_into_debt()
    {
        SimulationContext context = TestWorld.Quiet(withPlayer: true);
        ActorState player = context.Player();
        ActorState edgeDocks = context.Actor("Edge Station Docks");
        ActorState meridianDocks = context.Actor("Meridian Station Docks");
        context.PresentAt(player, "Meridian Station"); // a unit of ore in storage there
        context.ApplySink(player, player.Credits);
        Credits edgeBefore = edgeDocks.Credits;

        ShipArrived.Dock(context, player, context.Place("Edge Station").Id);
        Assert.Equal(-3m, player.Credits.Amount);
        Assert.Equal(edgeBefore + Credits.Of(3m), edgeDocks.Credits);

        Credits meridianBefore = meridianDocks.Credits;
        context.RunTo(context.CurrentTick + 240); // ten days of a unit at ₵0.10 a day
        context.SettleRent(context.StorageOf(player.Id, context.Place("Meridian Station").Id)!);
        Assert.Equal(-4m, player.Credits.Amount);
        Assert.Equal(meridianBefore + Credits.Of(1m), meridianDocks.Credits);
        context.AssertSound();
    }

    /// <summary>
    /// Plan S8's defect: a broke player still took escrow jobs on a ₵0 bond. Now every bond is locked in full: what the performer lacks
    /// the Accord clearing house lends, as debt, while its debt stays within half of what it owns; past that, it can't sign.
    /// </summary>
    [Fact]
    public void Every_bond_is_locked_in_full_on_the_clearing_houses_credit_up_to_half_of_what_the_performer_owns()
    {
        SimulationContext context = TestWorld.Quiet(withPlayer: true);
        ActorState player = context.Player();
        HaulContract haul = PlayerActionsTests.FarmHaul(context); // Verdant → Meridian, at the Accord's edge: escrow and a bond
        decimal bond = PlayerActions.BondFor(haul);
        Assert.True(bond > 0m);

        // Owing half of its ₵8,000 freighter already, the bond would take it past half: refused, the player and an AI alike.
        context.Charge(player, player.Credits + Credits.Of(4_000m), SinkKind.None);
        Assert.Equal(DealBlock.CannotBond, PlayerActions.CheckHaul(context, player, haul).Block);
        Assert.Equal(Signing.CannotBond, Market.TakeHaul(context, player, haul, context.ShipOf(player), haul.OfferedPrice));
        ActorState juno = context.Actor("Juno Okafor");
        context.Charge(juno, juno.Credits + Credits.Of(4_000m), SinkKind.None);
        Assert.Equal(Signing.CannotBond, Market.TakeHaul(context, juno, haul, context.ShipOf(juno), haul.OfferedPrice));
        Assert.True(haul.IsOpen);

        // Broke but owing nothing, the player signs: the whole bond goes into escrow, lent by the clearing house.
        context.ApplyFaucet(player, Credits.Of(4_000m));
        Assert.Equal(0m, player.Credits.Amount);
        Assert.Equal(Signing.Signed, PlayerActions.TakeHaul(context, player, haul, context.ShipOf(player)));
        Assert.Equal(Credits.Of(bond), haul.LockedBond);
        Assert.Equal(-bond, player.Credits.Amount);

        // Delivered, the bond comes back and the fee with it: the debt is paid off.
        context.RunUntilSettled(haul);
        Assert.Equal(ContractStatus.Fulfilled, haul.Status);
        Assert.True(player.Credits.Amount > 0m);
        context.AssertSound();
    }

    [Theory]
    [InlineData("Helios Prime", true)]       // the deep Accord: the courts collect the rest, into debt
    [InlineData("Meridian Station", false)]  // the Accord's edge: only what escrow holds
    public void The_deep_accord_collects_a_carriers_cargo_liability_into_debt(string destination, bool deep)
    {
        SimulationContext context = TestWorld.Quiet();
        ActorState farm = context.Actor("Verdant Agricombine");
        ActorState juno = context.Actor("Juno Okafor");
        HaulContract haul = Market.PostHaul(
            context, farm, GoodType.Food, 40m, Holding.StockAt(farm.Home), context.Place(destination).Id, Credits.Of(70m), Credits.Of(90m),
            context.CurrentTick + 200, serves: null, TermPackage.Strict);
        context.ApplySink(juno, juno.Credits - Credits.Of(PlayerActions.BondFor(haul))); // just its bond
        Assert.Equal(Signing.Signed, Market.TakeHaul(context, juno, haul, context.ShipOf(juno), haul.OfferedPrice));
        Assert.Equal(0m, juno.Credits.Amount);

        Settlement.Fail(context, haul, FailureReason.CargoLost);

        decimal owed = haul.Terms.Liability!.For(40m).Amount - haul.LockedBond.Amount;
        Assert.True(owed > 0m);
        Assert.Equal(deep ? -owed : 0m, juno.Credits.Amount);
        context.AssertSound();
    }

    [Fact]
    public void Debt_above_half_of_what_an_actor_owns_warns_it_and_seven_days_there_make_it_insolvent()
    {
        SimulationContext context = TestWorld.Quiet(withPlayer: true);
        ActorState player = context.Player();
        Credits assets = Solvency.Assets(context, player);
        Assert.Equal(context.ShipOf(player).Stats.Value, assets); // its Light Freighter

        context.Charge(player, player.Credits + Credits.Of(assets.Amount * 0.6m), SinkKind.None);
        Assert.False(Solvency.Review(context, player));
        Assert.Equal(SolvencyStatus.Warned, player.Solvency);
        DebtWarningEntry warning = context.Journal.Entries.OfType<DebtWarningEntry>().Single();
        Assert.Equal(context.CurrentTick + Solvency.WarnedHoursLimit, warning.InsolventBy);
        Assert.Contains(warning, context.PlayerAlerts); // the player is told, and the clock stops

        // Back to half or less: the warning is lifted.
        context.ApplyFaucet(player, Credits.Of(assets.Amount * 0.2m));
        Solvency.Review(context, player);
        Assert.Equal(SolvencyStatus.None, player.Solvency);
        Assert.Single(context.Journal.Entries.OfType<DebtWarningLiftedEntry>());

        // Above half again, for seven days (the alerts acknowledged, so the clock runs).
        context.Charge(player, Credits.Of(assets.Amount * 0.2m), SinkKind.None);
        Solvency.Review(context, player);
        context.ClearPlayerAlerts();
        context.RunTo(context.CurrentTick + Solvency.WarnedHoursLimit - 1);
        Assert.False(Solvency.Review(context, player));
        context.RunTo(context.CurrentTick + 1);
        Assert.True(Solvency.Review(context, player));
        Assert.Equal(SolvencyStatus.Liquidating, player.Solvency);
        Assert.Equal(InsolvencyReason.StayedAboveHalf, context.Journal.Entries.OfType<InsolventEntry>().Single().Reason);
        context.AssertSound();
    }

    [Fact]
    public void Debt_above_everything_an_actor_owns_is_insolvency_at_once()
    {
        SimulationContext context = TestWorld.Quiet();
        ActorState sable = context.Actor("Sable Vance");
        context.Charge(sable, sable.Credits + Solvency.Assets(context, sable) + Credits.Of(1m), SinkKind.None);

        Assert.True(Solvency.Review(context, sable));
        Assert.Equal(SolvencyStatus.Liquidating, sable.Solvency);
        Assert.Equal(InsolvencyReason.DebtAboveAssets, context.Journal.Entries.OfType<InsolventEntry>().Single().Reason);
        Assert.Equal(1, context.Failures(FailureKind.Insolvency));
        context.AssertSound();
    }

    [Fact]
    public void An_insolvent_trader_has_its_contracts_cancelled_its_ship_sold_in_a_fire_sale_and_dissolves()
    {
        SimulationContext context = TestWorld.Quiet();
        ActorState farm = context.Actor("Verdant Agricombine");
        ActorState juno = context.Actor("Juno Okafor"); // a Light Freighter at Verdant
        ActorState coreward = context.Actor("Coreward Logistics");
        Ship ship = context.ShipOf(juno);
        HaulContract haul = PlayerActionsTests.FarmHaul(context); // Verdant → Meridian, standard terms: escrow and a bond
        Assert.Equal(Signing.Signed, Market.TakeHaul(context, juno, haul, ship, haul.OfferedPrice));
        Credits farmBefore = farm.Credits;
        Credits bond = haul.LockedBond;

        // A debt bigger than everything it owns: insolvent on its next heartbeat.
        context.Charge(juno, juno.Credits + Solvency.Assets(context, juno) + Credits.Of(500m), SinkKind.None);
        Heartbeat(context, juno);

        Assert.Equal(SolvencyStatus.Liquidating, juno.Solvency);
        Assert.False(juno.InBusiness);
        Assert.Equal(ContractStatus.Cancelled, haul.Status);
        Assert.Equal(CancelReason.Insolvency, haul.Cancellation!.Reason);
        Assert.Equal(farmBefore + haul.Price + haul.Tax + bond, farm.Credits); // its escrow back, and Juno's bond
        Assert.Equal(1, context.Failures(FailureKind.CancelledOnInsolvency));
        Assert.Equal(Signing.Insolvent, Market.TakeHaul(context, juno, PlayerActionsTests.FarmHaul(context), ship, Credits.Of(70m)));

        // The ship finds nothing to load, and once free the clearing house lists it, shown in every system at once and free.
        context.RunTo(context.CurrentTick + 1);
        Assert.True(ship.IsFree);
        Heartbeat(context, juno);
        var sale = (ShipSaleContract)context.Contract(ship.ListedFor!.Value);
        Assert.True(sale.FireSale);
        Assert.Equal(Credits.Of(Math.Round(ship.Stats.Value.Amount * Liquidation.ShipAskShare, 2)), sale.OfferedPrice);
        Assert.All(context.Locations.Keys, l => Assert.True(sale.ShownFrom(l) <= context.CurrentTick));
        Assert.Equal(Credits.Zero, sale.AdFeesPaid);

        // A trader buys it (as one wanting a ship would), paying down Juno's debt; with nothing left, Juno dissolves.
        Credits debt = juno.Debt;
        Assert.Equal(Signing.Signed, Market.BuyShip(context, coreward, sale, sale.OfferedPrice));
        Assert.Equal(coreward.Id, ship.Owner);
        Assert.Equal(1, context.FireSaleShipsSold);
        Assert.Equal(debt - sale.OfferedPrice, juno.Debt);
        Heartbeat(context, juno);

        Assert.Equal(SolvencyStatus.Dissolved, juno.Solvency);
        ActorDissolvedEntry dissolved = context.Journal.Entries.OfType<ActorDissolvedEntry>().Single();
        Assert.Equal(dissolved.WrittenOff, context.DebtWrittenOff);
        Assert.True(dissolved.WrittenOff.Amount > 0m);
        Assert.Equal(0m, juno.Credits.Amount);
        Assert.Contains(context.Dissolutions, d => d.Actor == juno.Id);
        context.AssertSound();
    }

    [Fact]
    public void An_insolvent_actors_goods_go_up_in_fire_sales_and_are_written_off_if_nobody_buys()
    {
        SimulationContext context = TestWorld.Quiet();
        ActorState pell = context.Actor("Pell Mirza");
        LocationId meridian = context.Place("Meridian Station").Id;
        context.PutInStorage(pell, meridian, context.TakeFromStock(context.Actor("Verdant Agricombine"), GoodType.Food, 20m, reserved: false));
        context.Charge(pell, pell.Credits + Solvency.Assets(context, pell) + Credits.Of(100m), SinkKind.None);
        Heartbeat(context, pell);

        SaleContract lot = context.OpenContracts.OfType<SaleContract>().Single(s => s.PostedBy == pell.Id);
        Assert.True(lot.FireSale);
        Assert.Equal(SaleSide.ForSale, lot.Side);
        Assert.Equal(meridian, lot.Place);
        Assert.All(context.Locations.Keys, l => Assert.True(lot.ShownFrom(l) <= context.CurrentTick));

        // Nobody buys: marked down week by week, and after sixty days the ship is broken up, the food written off, and Pell dissolves.
        decimal lostBefore = context.GoodsLost(GoodType.Food);
        for (int day = 0; day <= Liquidation.LongestHours / 24 + 1 && pell.InBusiness == false && pell.Solvency != SolvencyStatus.Dissolved; day++)
        {
            context.RunTo(context.CurrentTick + 24);
            Heartbeat(context, pell);
        }

        Assert.Equal(SolvencyStatus.Dissolved, pell.Solvency);
        Assert.Equal(20m, context.GoodsLost(GoodType.Food) - lostBefore);
        Assert.Contains(context.Ships.Values, s => s.Scrapped && s.History.Any(r => r.To == pell.Id || r.From == pell.Id || s.Owner == pell.Id));
        context.AssertSound();
    }

    [Fact]
    public void A_carrier_whose_shipper_goes_insolvent_gets_its_bond_back_and_the_break_fee()
    {
        SimulationContext context = TestWorld.Quiet(withPlayer: true);
        ActorState player = context.Player();
        ActorState farm = context.Actor("Verdant Agricombine");
        HaulContract haul = PlayerActionsTests.FarmHaul(context);
        Assert.Equal(Signing.Signed, PlayerActions.TakeHaul(context, player, haul, context.ShipOf(player)));
        Credits before = player.Credits;

        context.Charge(farm, farm.Credits + Solvency.Assets(context, farm) + Credits.Of(1m), SinkKind.None);
        Heartbeat(context, farm);

        Assert.Equal(ContractStatus.Cancelled, haul.Status);
        Assert.Equal(farm.Id, haul.Cancellation!.By); // the insolvent party walks away
        Assert.Equal(before + haul.LockedBond + haul.Terms.BreakFee, player.Credits);
        ContractCancelledEntry cancelled = context.Journal.Entries.OfType<ContractCancelledEntry>().Single();
        string sentence = JournalText.Describe(context, cancelled);
        Assert.Contains("Verdant Agricombine is insolvent", sentence);
        Assert.Contains("you got its break fee", sentence);
        context.AssertSound();
    }

    [Fact]
    public void The_players_insolvency_ends_the_game_and_its_legacy_tells_its_story()
    {
        SimulationContext context = TestWorld.Quiet(withPlayer: true);
        var session = new GameSession(context);
        ActorState player = session.Player;

        // A haul delivered first, so the record has something in it; then an open post for insolvency to take down.
        HaulContract haul = PlayerActionsTests.FarmHaul(context);
        Assert.Equal(Signing.Signed, PlayerActions.TakeHaul(context, player, haul, context.ShipOf(player)));
        context.RunUntilSettled(haul);
        Assert.Equal(ContractStatus.Fulfilled, haul.Status);
        LocationId meridian = context.Place("Meridian Station").Id;
        SaleContract wanted = Market.PostSale(
            context, player, SaleSide.Wanted, GoodType.Food, 10m, meridian, Credits.Of(100m), Credits.Of(100m), context.CurrentTick + 100,
            Holding.StorageAt(meridian));

        context.Charge(player, player.Credits + Credits.Of(20_000m), SinkKind.None);
        Heartbeat(context, player);

        Assert.True(session.IsOver);
        Assert.Equal(ContractStatus.Withdrawn, wanted.Status);
        Assert.Contains(context.PlayerAlerts, a => a is InsolventEntry);
        session.SetSpeed(ClockSpeed.X1);
        Assert.Equal(ClockSpeed.Paused, session.Speed);
        Assert.False(session.Advance(1.0));

        LegacyViewModel legacy = LegacyView.Build(session);
        Assert.StartsWith("You are insolvent", legacy.Ending);
        Assert.Contains("1 on time", legacy.Record[0]);
        Assert.Contains(legacy.Markets, m => m.Contains("Food") && m.Contains("Meridian Station"));
        LegacyShip flown = Assert.Single(legacy.Ships);
        Assert.Contains("Light Freighter", flown.Name);
        Assert.NotEmpty(flown.History);

        // The world goes on without the player and sells up its ship like anyone's: in a fire sale.
        Heartbeat(context, player);
        Assert.True(context.Contract(context.ShipOf(player).ListedFor!.Value).FireSale);
        context.AssertSound();
    }

    [Theory]
    [InlineData(26L, Throttle.Push)]   // 30 hours at cruise is late; 24 pushed is on time, on a safe route
    [InlineData(200L, Throttle.Cruise)] // plenty of time: no reason to risk the cargo
    public void An_ai_carrier_pushes_when_pushing_beats_the_late_fee(long hours, Throttle expected)
    {
        SimulationContext context = TestWorld.Quiet();
        ActorState juno = context.Actor("Juno Okafor"); // a free Light Freighter at Verdant
        HaulContract haul = PlayerActionsTests.FarmHaul(context, hours: hours, package: TermPackage.Strict);

        DecisionEngine.RunTrader(context, juno);

        Assert.Equal(juno.Id, haul.CounterpartyId);
        Assert.Equal(expected, context.ShipOf(juno).Job!.Throttle);
    }

    /// <summary>
    /// Plan S8's check: the first hour's trap (design §13: a fat fee on a red route, a harsh late fee, and a deadline the freighter can
    /// only meet by pushing) is visibly a trap: on the board and in the contract, before the player signs.
    /// </summary>
    [Fact]
    public void The_first_hours_trap_reads_as_a_trap()
    {
        SimulationContext context = TestWorld.Quiet(withPlayer: true, incidents: true);
        var session = new GameSession(context);
        ActorState fab = context.Actor("Meridian Fabworks");

        // Parts from Meridian to Edge Station: ₵900 across the red Rust Belt run on strict terms (a late fee of 2% an hour, to half the
        // fee), due in 170 hours; a Light Freighter takes 190 at cruise and 152 pushed, and its standard drive runs hot.
        HaulContract trap = Market.PostHaul(
            context, fab, GoodType.Parts, 40m, Holding.StockAt(fab.Home), context.Place("Edge Station").Id, Credits.Of(900m), Credits.Of(900m),
            context.CurrentTick + 170, serves: null, TermPackage.Strict);

        BoardRow row = BoardView.Build(session).Rows.Single(r => r.Id == trap.Id);
        Assert.Equal(DangerBand.Red, row.Band);
        Assert.Contains("on time only if you push", row.Note);
        Assert.Equal(Tone.Loss, row.NoteTone);

        ContractViewModel view = ContractView.Build(session, trap.Id);
        PushChoice push = Assert.IsType<PushChoice>(view.Push);
        Assert.Contains("Only pushing makes the deadline", push.Trap);
        Assert.Contains("50%, against 25% at cruise (the drive runs hot)", push.Note);
        InfoLine incident = view.Uncertain.Single(l => l.Label == Templates.Get("Uncertain.Incident"));
        Assert.Equal(Tone.Warn, incident.Tone);
        Assert.Contains("the whole cargo lost 7.2%", incident.Note); // the red run shifts the table toward the severe end

        // Taking it pushed flies the laden leg at push: 152 hours.
        Assert.True(session.Accept(trap.Id, Throttle.Push).Signed);
        Ship ship = context.ShipOf(session.Player);
        Assert.Equal(Throttle.Push, ship.Job!.Throttle);
        context.RunTo(context.CurrentTick + 1);
        Assert.Equal(152, ship.Course!.ArrivalTick - ship.Course.DepartedTick);
    }

    [Fact]
    public void Every_debt_and_insolvency_entry_reads_as_a_sentence_for_an_ai_and_for_the_player()
    {
        SimulationContext context = TestWorld.Quiet(withPlayer: true);
        foreach (ActorState actor in new[] { context.Actor("Juno Okafor"), context.Player() })
        {
            JournalEntry[] entries =
            {
                new DebtWarningEntry(0, actor.Id, Credits.Of(5_000m), Credits.Of(8_000m), 168),
                new DebtWarningLiftedEntry(0, actor.Id, Credits.Of(3_000m), Credits.Of(8_000m)),
                new InsolventEntry(0, actor.Id, Credits.Of(9_000m), Credits.Of(8_000m), InsolvencyReason.DebtAboveAssets),
                new InsolventEntry(0, actor.Id, Credits.Of(5_000m), Credits.Of(8_000m), InsolvencyReason.StayedAboveHalf),
                new ActorDissolvedEntry(0, actor.Id, Credits.Of(4_800m), Credits.Of(4_200m), Credits.Zero),
                new ActorDissolvedEntry(0, actor.Id, Credits.Of(9_800m), Credits.Zero, Credits.Of(800m)),
            };
            foreach (JournalEntry entry in entries)
            {
                string sentence = JournalText.Describe(context, entry);
                Assert.DoesNotContain("{", sentence);
                Assert.StartsWith(actor.IsPlayer ? "You" : actor.Name, sentence);
                Assert.Equal(actor.Id, JournalText.Subject(entry));
            }
        }
    }

    // A day of an actor's heartbeat, now, as ActorHeartbeat does it, without the AI's own turn (the quiet world keeps that off): a
    // day's running costs and interest, the solvency review, and the clearing house's turn if it is insolvent.
    private static void Heartbeat(SimulationContext context, ActorState actor)
    {
        DecisionEngine.ChargeRunningCosts(context, actor, 24);
        Solvency.ChargeInterest(context, actor, 24);
        Solvency.Review(context, actor);
        if (actor.Solvency == SolvencyStatus.Liquidating)
        {
            Liquidation.Run(context, actor);
        }
    }
}
