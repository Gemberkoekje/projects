using System;
using System.Linq;
using ByGalacticAccord.Engine.Actors;
using ByGalacticAccord.Engine.Domain;
using ByGalacticAccord.Engine.Economy;
using ByGalacticAccord.Engine.Simulation;

namespace ByGalacticAccord.Tests;

/// <summary>
/// Ships from parts (plan S7, design §6, §15.4–§15.6): a Build's life from posting to handover through the parts web, and each of
/// its failure paths (design §16.3), for an AI client and through the player's commands:
/// <list type="bullet">
/// <item>late delivery: AI and player, here (the player through the decision at the grace's end);</item>
/// <item>default by the yard: an AI client waits on a yard still building; the player cancels for default;</item>
/// <item>substitution within the policy (a standard Build takes grade B at the discount) and none under a strict one;</item>
/// <item>spec shortfall outside the policy: an AI client rejects, the player accepts with the refund or rejects;</item>
/// <item>supply failure: an AI client chooses by utility; the player extends, substitutes or takes the refund;</item>
/// <item>cancellation at will by the client (the deposit is the break fee) and by the yard (the deposit back and as much again).</item>
/// </list>
/// Payer default can't reach a Build: both yards are in Accord space, where the balance is in escrow. A ship's Sale outside the
/// Accord can default, here.
/// </summary>
public sealed class BuildTests
{
    private static readonly ShipParts Freighter = Designs.Parts(Design.LightFreighter);

    [Fact]
    public void A_yard_takes_a_build_buys_its_parts_through_the_market_builds_the_ship_and_hands_it_over()
    {
        SimulationContext context = World();
        ActorState client = context.Actor("Foundry Freightways");
        ActorState yard = context.Actor("Tycho Yards");
        ActorState drives = context.Actor("Meridian Drive Works");
        Location tycho = context.Place("Tycho Foundry");
        BuildContract build = Market.PostBuild(context, client, Freighter, tycho.Id, Credits.Of(9_000m), Credits.Of(10_000m), context.CurrentTick + (30 * 24), TermPackage.Standard);
        Credits clientBefore = client.Credits;
        Credits yardBefore = yard.Credits;

        DecisionEngine.RunYard(context, yard); // it sees the Build at its door and takes it as posted

        Assert.Equal((ContractStatus.Active, yard.Id, tycho.Id), (build.Status, build.Builder!.Value, build.Yard!.Value));
        Assert.Equal(Credits.Of(1_800m), build.DepositPaid);
        Assert.Equal(yardBefore + Credits.Of(1_800m) - build.LockedBond, yard.Credits);
        Assert.Equal(clientBefore - Credits.Of(9_000m + 360m), client.Credits); // deposit to the yard, balance and tax in escrow
        Assert.Equal(Credits.Of(7_200m + 360m + 900m), build.EscrowHeld); // the balance, the 4% tax and the yard's bond (10%)

        DecisionEngine.RunYard(context, yard); // it orders the parts: the frame next door is handed over at once
        Assert.Equal(1m, yard.Stock(GoodType.LightFrameA));
        SaleContract driveOrder = context.Contracts.Values.OfType<SaleContract>().Single(s => s.ForBuild == build.Id && s.Good == GoodType.StandardDriveA);
        Assert.Equal((SaleSide.Wanted, yard.Id, tycho.Id, ContractStatus.Open), (driveOrder.Side, driveOrder.PostedBy, driveOrder.Place, driveOrder.Status));

        // Shown a jump further (as the yard pays after a day), the drive maker at Meridian sells it delivered and posts a haul.
        context.ShowAJumpFurther(driveOrder);
        DecisionEngine.RunFactory(context, drives);
        Assert.Equal((ContractStatus.Active, drives.Id), (driveOrder.Status, driveOrder.Seller!.Value));
        HaulContract haul = context.Contracts.Values.OfType<HaulContract>().Single(h => h.Serves == driveOrder.Id);
        ActorState pell = context.Actor("Pell Mirza");
        Assert.Equal(Signing.Signed, Market.TakeHaul(context, pell, haul, context.ShipOf(pell), haul.OfferedPrice));
        context.RunUntilSettled(driveOrder);
        Assert.Equal(ContractStatus.Fulfilled, driveOrder.Status);
        Assert.Equal(1m, yard.Stock(GoodType.StandardDriveA));

        DecisionEngine.RunYard(context, yard); // every part in: assembly starts, five days at Tycho
        Assert.NotNull(yard.Works.Current!.AssemblyDone);
        context.RunTo(yard.Works.Current.AssemblyDone!.Value);
        DecisionEngine.RunYard(context, yard);

        // Built from the parts at their grades, handed over at the yard: the ship is the client's, the yard paid the balance.
        Ship ship = context.Ship(build.Ship!.Value);
        Assert.Equal((ContractStatus.Fulfilled, client.Id, tycho.Id), (build.Status, ship.Owner, ship.Location));
        Assert.Equal(Freighter, ship.Parts);
        Assert.Contains(ship.Id, client.Ships);
        Assert.Equal(new[] { ShipEventKind.Commissioned, ShipEventKind.ChangedHands }, ship.History.Select(h => h.Kind));
        Assert.Equal((yard.Id, build.Id), (ship.History[0].From!.Value, ship.History[0].Contract!.Value));
        ShipHandedOverEntry handed = context.Journal.Entries.OfType<ShipHandedOverEntry>().Single(e => e.Contract == build.Id);
        Assert.Equal((Credits.Zero, Credits.Zero, 0L), (handed.Discount, handed.LateFee, handed.TicksLate));
        Assert.Equal(Credits.Zero, build.EscrowHeld);
        Assert.Equal(1m, context.GoodsBuiltIn(GoodType.LightFrameA));
        Assert.Equal(0m, yard.Stock(GoodType.LightFrameA));
        context.AssertSound();
    }

    [Fact]
    public void Goods_conservation_holds_through_parts_into_ships_and_yards_build_in_a_world_left_to_run()
    {
        SimulationContext context = TestWorld.RunHeadless(42L, 40_000);

        Assert.True(context.ShipsBuiltToOrder + context.ShipsBuiltOnSpec > 0, "No ship was built.");
        Assert.True(context.BuildsSigned > 0);
        Assert.True(context.PartsMade.Sum() > 0);
        decimal builtIn = ShipPart.Goods.Sum(context.GoodsBuiltIn);
        decimal inShips = context.Ships.Values.Where(s => s.BuiltBy is not null && !s.Scrapped).Sum(s => s.Parts.All.Count);
        Assert.Equal(inShips, builtIn);
        context.AssertSound();
    }

    [Fact]
    public void A_standard_build_takes_a_part_a_grade_lower_without_asking_at_the_policy_discount()
    {
        SimulationContext context = World();
        ActorState client = context.Actor("Foundry Freightways");
        ActorState yard = context.Actor("Tycho Yards");
        EmptyFrames(context);
        context.Produce(yard, GoodType.LightFrameB, 1m);
        context.Produce(yard, GoodType.StandardDriveA, 1m);
        BuildContract build = PostAndSign(context, client, yard, TermPackage.Standard);

        DecisionEngine.RunYard(context, yard); // a grade-B frame is in: a standard Build allows one grade lower
        context.RunTo(yard.Works.Current!.AssemblyDone!.Value);
        Credits yardBefore = yard.Credits;
        DecisionEngine.RunYard(context, yard);

        Ship ship = context.Ship(build.Ship!.Value);
        Assert.Equal(new FittedPart(PartKind.LightFrame, Grade.B), ship.Parts.Frame);
        Assert.Equal((ContractStatus.Fulfilled, client.Id), (build.Status, ship.Owner));
        ShipHandedOverEntry handed = context.Journal.Entries.OfType<ShipHandedOverEntry>().Single(e => e.Contract == build.Id);
        Assert.Equal(Credits.Of(600m), handed.Discount); // 20% of a Light frame's ₵3,000
        Assert.Equal(yardBefore + Credits.Of(7_200m - 600m) + build.LockedBond, yard.Credits);
        Assert.Equal(1, context.Failures(FailureKind.Substituted));
        Assert.Equal(115.2m, ship.Stats.CargoCapacity); // the ship's stats tell the story of its parts
        context.AssertSound();
    }

    [Fact]
    public void A_strict_build_allows_no_substitute_and_a_yard_late_building_with_one_has_the_ship_rejected_by_an_ai_client()
    {
        SimulationContext context = World();
        ActorState client = context.Actor("Foundry Freightways");
        ActorState yard = context.Actor("Tycho Yards");
        EmptyFrames(context);
        context.Produce(yard, GoodType.LightFrameB, 1m);
        context.Produce(yard, GoodType.StandardDriveA, 1m);
        BuildContract build = PostAndSign(context, client, yard, TermPackage.Strict, reservation: 9_000m, days: 10);

        DecisionEngine.RunYard(context, yard); // strict: the grade-B frame won't do, so it orders a grade-A one
        Assert.Null(yard.Works.Current!.AssemblyDone);
        Assert.Contains(context.Contracts.Values.OfType<SaleContract>(), s => s.ForBuild == build.Id && s.Good == GoodType.LightFrameA);

        context.RunTo(build.DeadlineTick + 1);
        DecisionEngine.RunYard(context, yard); // past its deadline it builds with what it has
        context.RunTo(yard.Works.Current!.AssemblyDone!.Value);
        Credits clientBefore = client.Credits;
        Credits bond = build.LockedBond;
        DecisionEngine.RunYard(context, yard);

        // Outside the strict policy, and not worth the price to a client that wanted no more than it posted: rejected.
        Ship ship = context.Ship(build.Ship!.Value);
        Assert.Equal(ContractStatus.Breached, build.Status);
        Assert.Equal(yard.Id, ship.Owner);
        Assert.Equal(clientBefore + Credits.Of(7_200m + 360m) + bond + Credits.Of(1_800m), client.Credits); // escrow, bond and deposit back
        Assert.Single(context.Journal.Entries.OfType<DeliveryFailedEntry>(), e => e.Contract == build.Id && e.Reason == FailureReason.Rejected);
        context.AssertSound();
    }

    [Theory]
    [InlineData(DecisionChoice.Accept)]
    [InlineData(DecisionChoice.Reject)]
    public void The_player_inspects_a_ship_built_outside_its_builds_policy_and_accepts_it_with_the_refund_or_rejects_it(DecisionChoice choice)
    {
        SimulationContext context = World(withPlayer: true);
        ActorState player = context.Player();
        ActorState yard = context.Actor("Tycho Yards");
        EmptyFrames(context);
        context.Produce(yard, GoodType.LightFrameB, 1m);
        context.Produce(yard, GoodType.StandardDriveA, 1m);
        BuildContract build = PostAndSign(context, player, yard, TermPackage.Strict, days: 10);
        context.RunTo(build.DeadlineTick + 1);
        DecisionEngine.RunYard(context, yard);
        long done = yard.Works.Current!.AssemblyDone!.Value;

        // Its strict grace runs out during the assembly: the player is asked whether to cancel, and waits for the ship.
        Assert.Equal(RunStop.PlayerDecision, context.RunTo(done));
        Assert.True(PlayerActions.Decide(context, player, context.PlayerDecisionFor(build.Id)!, DecisionChoice.Wait));
        context.RunTo(done);
        DecisionEngine.RunYard(context, yard);

        PlayerDecision decision = context.PlayerDecisionFor(build.Id) ?? throw new Xunit.Sdk.XunitException("The player wasn't asked.");
        Assert.Equal(DecisionKind.Inspection, decision.Kind);
        Assert.Equal(RunStop.PlayerDecision, context.RunTo(context.CurrentTick + 10)); // the hard pause
        Ship ship = context.Ship(build.Ship!.Value);
        Assert.True(PlayerActions.Decide(context, player, decision, choice));

        if (choice == DecisionChoice.Accept)
        {
            ShipHandedOverEntry handed = context.Journal.Entries.OfType<ShipHandedOverEntry>().Single(e => e.Contract == build.Id);
            Assert.Equal((ContractStatus.Fulfilled, player.Id), (build.Status, ship.Owner));
            Assert.Equal(Credits.Of(450m), handed.Discount); // a grade short, at what the grade is worth: 15% of ₵3,000
            Assert.True(handed.TicksLate > 0);
        }
        else
        {
            Assert.Equal((ContractStatus.Breached, yard.Id), (build.Status, ship.Owner));
        }

        context.AssertSound();
    }

    [Fact]
    public void A_supply_failure_has_the_yard_propose_remedies_and_an_ai_client_chooses_one()
    {
        SimulationContext context = World();
        ActorState client = context.Actor("Foundry Freightways");
        ActorState yard = context.Actor("Tycho Yards");
        BuildContract build = PostAndSign(context, client, yard, TermPackage.Standard, days: 14);
        SaleContract order = FailTheDriveOrder(context, yard, build);

        // The order expired with nobody to supply it, too late to get another in time: the yard invokes the supply-failure term.
        Assert.Equal(ContractStatus.Expired, order.Status);
        SupplyFailureEntry failure = context.Journal.Entries.OfType<SupplyFailureEntry>().Single(e => e.Contract == build.Id);
        Assert.Equal((order.Id, PartSlot.Drive), (failure.FailedOrder, failure.Slot));
        Assert.Equal(failure.Seq, context.Journal.Entries.OfType<RemedyChosenEntry>().Single().Cause);
        Assert.Equal(order.LastEntry, failure.Cause);
        Assert.Null(build.Proposal); // the AI chose at once
        Assert.Single(build.Remedies);
        Assert.Equal(1, context.Failures(FailureKind.SupplyFailure));
        context.AssertSound();
    }

    [Theory]
    [InlineData(DecisionChoice.Extend)]
    [InlineData(DecisionChoice.Substitute)]
    [InlineData(DecisionChoice.TakeRefund)]
    public void The_player_chooses_a_remedy_for_a_supply_failure_on_its_build(DecisionChoice choice)
    {
        SimulationContext context = World(withPlayer: true);
        ActorState player = context.Player();
        ActorState yard = context.Actor("Tycho Yards");
        BuildContract build = PostAndSign(context, player, yard, TermPackage.Standard, days: 14);
        FailTheDriveOrder(context, yard, build);

        PlayerDecision decision = context.PlayerDecisionFor(build.Id) ?? throw new Xunit.Sdk.XunitException("The player wasn't asked.");
        Assert.Equal(DecisionKind.SupplyFailure, decision.Kind);
        Assert.Equal(new[] { DecisionChoice.Extend, DecisionChoice.Substitute, DecisionChoice.TakeRefund }, decision.Choices);
        SupplyFailure proposal = build.Proposal!;
        long deadline = build.DeadlineTick;
        Credits playerBefore = player.Credits;
        Credits yardBefore = yard.Credits;
        Assert.True(PlayerActions.Decide(context, player, decision, choice));

        switch (choice)
        {
            case DecisionChoice.Extend:
                Assert.Equal((ContractStatus.Active, proposal.ExtendedDeadline), (build.Status, build.DeadlineTick));
                Assert.True(build.DeadlineTick > deadline);
                Assert.Equal(playerBefore + proposal.Compensation, player.Credits);
                Assert.Equal(yardBefore - proposal.Compensation, yard.Credits);
                break;
            case DecisionChoice.Substitute:
                Assert.Equal(new FittedPart(PartKind.StandardDrive, Grade.B), build.Spec.Drive);
                Assert.Equal(proposal.SubstituteDiscount, build.AgreedDiscount);
                Assert.Equal(Credits.Of(600m), proposal.SubstituteDiscount); // a standard substitution: 20% of a Standard drive's ₵3,000
                Assert.Equal(proposal.SubstituteDeadline, build.DeadlineTick);
                DecisionEngine.RunYard(context, yard); // it orders the grade-B drive now
                Assert.Contains(context.Contracts.Values.OfType<SaleContract>(), s => s.ForBuild == build.Id && s.Good == GoodType.StandardDriveB && s.IsOpen);
                break;
            default:
                Assert.Equal((ContractStatus.Cancelled, CancelReason.SupplyFailure), (build.Status, build.Cancellation!.Reason));
                Assert.Equal(proposal.CancelCompensation, build.Cancellation.Paid);
                Assert.Equal(Credits.Zero, build.EscrowHeld);
                Assert.Equal(1, context.Failures(FailureKind.CancelledOnSupplyFailure));
                break;
        }

        Assert.Single(context.Journal.Entries.OfType<RemedyChosenEntry>());
        context.AssertSound();
    }

    [Fact]
    public void A_build_handed_over_late_pays_the_late_fee_from_the_yards_bond()
    {
        SimulationContext context = World();
        ActorState client = context.Actor("Foundry Freightways");
        ActorState yard = context.Actor("Tycho Yards");
        context.Produce(yard, GoodType.StandardDriveA, 1m);
        BuildContract build = PostAndSign(context, client, yard, TermPackage.Standard, days: 3); // five days' assembly can't make three

        DecisionEngine.RunYard(context, yard); // the frame from next door
        DecisionEngine.RunYard(context, yard);
        context.RunTo(yard.Works.Current!.AssemblyDone!.Value);
        Credits clientBefore = client.Credits;
        DecisionEngine.RunYard(context, yard);

        ShipHandedOverEntry handed = context.Journal.Entries.OfType<ShipHandedOverEntry>().Single(e => e.Contract == build.Id);
        Assert.Equal(ContractStatus.Fulfilled, build.Status);
        Assert.True(handed.TicksLate > 0);
        Assert.Equal(build.LatePenalty!.PenaltyFor(handed.TicksLate), handed.LateFee);
        Assert.Equal(clientBefore + handed.LateFee, client.Credits);
        Assert.Equal(1, context.Failures(FailureKind.LateDelivery));
        context.AssertSound();
    }

    [Fact]
    public void An_ai_client_waits_on_a_build_in_default_while_its_yard_still_builds_it_and_the_player_may_cancel_for_default()
    {
        SimulationContext context = World(withPlayer: true);
        ActorState client = context.Actor("Foundry Freightways");
        ActorState player = context.Player();
        ActorState tycho = context.Actor("Tycho Yards");
        ActorState helios = context.Actor("Helios Orbital Yards");
        BuildContract ai = PostAndSign(context, client, tycho, TermPackage.Strict, days: 1);
        BuildContract mine = PostAndSign(context, player, helios, TermPackage.Strict, days: 1);

        context.RunTo(ai.GraceEndsTick);
        Assert.True(ai.InDefault(context.CurrentTick));
        Assert.Equal(ContractStatus.Active, ai.Status); // the ship is still coming, worth more than its money back and the bond
        Assert.False(DecisionEngine.WouldCancel(context, client, ai));

        context.RunTo(mine.GraceEndsTick);
        PlayerDecision decision = context.PlayerDecisionFor(mine.Id) ?? throw new Xunit.Sdk.XunitException("The player wasn't asked.");
        Credits before = player.Credits;
        Credits bond = mine.LockedBond;
        Assert.True(PlayerActions.Decide(context, player, decision, DecisionChoice.Cancel));
        Assert.Equal((ContractStatus.Cancelled, CancelReason.ForDefault), (mine.Status, mine.Cancellation!.Reason));
        Assert.Equal(before + Credits.Of(7_200m + 360m) + bond + Credits.Of(1_800m), player.Credits);
        Assert.False(helios.Works.Has(mine.Id) && helios.Works.Current?.Build == mine.Id);
        context.AssertSound();
    }

    [Fact]
    public void Cancelling_a_build_at_will_costs_the_client_its_deposit_and_the_yard_the_deposit_twice()
    {
        SimulationContext context = World(withPlayer: true);
        ActorState player = context.Player();
        ActorState client = context.Actor("Foundry Freightways");
        ActorState tycho = context.Actor("Tycho Yards");
        ActorState helios = context.Actor("Helios Orbital Yards");
        BuildContract mine = PostAndSign(context, player, tycho, TermPackage.Standard);
        BuildContract theirs = PostAndSign(context, client, helios, TermPackage.Standard);

        Credits playerBefore = player.Credits;
        Assert.True(PlayerActions.CancelAtWill(context, player, mine));
        Assert.Equal((CancelReason.AtWill, Credits.Of(1_800m)), (mine.Cancellation!.Reason, mine.Cancellation.Paid)); // the deposit it paid
        Assert.Equal(playerBefore + Credits.Of(7_200m + 360m), player.Credits); // the balance and tax back from escrow

        Credits clientBefore = client.Credits;
        Assert.True(Market.CancelAtWill(context, theirs, helios));
        Assert.Equal(clientBefore + Credits.Of(7_200m + 360m) + Credits.Of(1_800m) + Credits.Of(1_800m), client.Credits);
        Assert.Equal(Credits.Of(1_800m), theirs.Cancellation!.Paid);
        context.RunTo(context.CurrentTick + 24);
        DecisionEngine.RunYard(context, helios);
        Assert.False(helios.Works.Has(theirs.Id));
        context.AssertSound();
    }

    [Fact]
    public void The_players_build_reaches_the_yards_it_is_shown_to_and_each_bids_on_its_habit()
    {
        SimulationContext context = World(withPlayer: true, answers: true);
        ActorState player = context.Player();
        ActorState tycho = context.Actor("Tycho Yards");
        ActorState helios = context.Actor("Helios Orbital Yards");
        var draft = new PostDraft(
            PostKind.Build, GoodType.Food, 1m, player.Home, null,
            new Offer(Credits.Of(7_000m), context.CurrentTick + (25 * 24), TermPackage.Standard, 1m),
            new[] { tycho.Home, helios.Home }, Freighter);
        Assert.Equal(PostBlock.None, PlayerActions.CheckPost(context, player, draft));
        var build = (BuildContract)PlayerActions.Post(context, player, draft)!;
        Assert.Equal(player.Home, build.Place);

        context.RunTo(context.CurrentTick + 4 + Haggling.SlowestReply);

        // Both yards counter: their bids, each on its habit's terms (design §15.5: Tycho lenient, Helios strict).
        Negotiation fromTycho = context.NegotiationWith(build.Id, tycho.Id) ?? throw new Xunit.Sdk.XunitException("Tycho Yards didn't bid.");
        Negotiation fromHelios = context.NegotiationWith(build.Id, helios.Id) ?? throw new Xunit.Sdk.XunitException("Helios Orbital Yards didn't bid.");
        Assert.Equal(TermPackage.Lenient, fromTycho.Latest.Offer.Package);
        Assert.Equal(TermPackage.Strict, fromHelios.Latest.Offer.Package);
        Assert.True(fromHelios.Latest.Offer.Price > fromTycho.Latest.Offer.Price, "Helios is the dearer yard.");

        Assert.Equal(Signing.Signed, PlayerActions.AcceptOffer(context, player, fromTycho));
        Assert.Equal((ContractStatus.Active, tycho.Id, TermPackage.Lenient), (build.Status, build.Builder!.Value, build.Terms.Package));
        Assert.Equal(fromTycho.Latest.Offer.Price, build.Price);
        Assert.Equal(NegotiationStatus.Ended, fromHelios.Status);
        Assert.True(tycho.Works.Has(build.Id));
        context.AssertSound();
    }

    [Fact]
    public void A_yard_with_room_builds_a_ship_on_spec_and_sells_it_from_its_lot_to_the_player()
    {
        SimulationContext context = World(withPlayer: true);
        ActorState player = context.Player();
        ActorState yard = context.Actor("Tycho Yards");
        yard.Works.SpecSells = null;
        context.Produce(yard, GoodType.StandardDriveA, 1m);

        DecisionEngine.RunYard(context, yard); // nothing ordered: a Light Freighter on spec, its frame from next door
        DecisionEngine.RunYard(context, yard);
        context.RunTo(yard.Works.Current!.AssemblyDone!.Value);
        DecisionEngine.RunYard(context, yard);
        Ship ship = context.Ship(yard.Ships.Single());
        Assert.Null(ship.History[0].Contract);
        Assert.Equal(1, context.ShipsBuiltOnSpec);
        DecisionEngine.RunYard(context, yard); // onto its lot
        ShipSaleContract sale = context.Contracts.Values.OfType<ShipSaleContract>().Single(s => s.Ship == ship.Id);
        Assert.False(ship.IsFree);
        context.PresentAt(player, "Tycho Foundry");

        Credits before = player.Credits;
        Assert.Equal(DealBlock.None, PlayerActions.CheckBuyShip(context, player, sale, sale.OfferedPrice));
        Assert.Equal(Signing.Signed, PlayerActions.BuyShip(context, player, sale));

        Assert.Equal((ContractStatus.Fulfilled, player.Id), (sale.Status, ship.Owner));
        Assert.Contains(ship.Id, player.Ships);
        Assert.DoesNotContain(ship.Id, yard.Ships);
        Assert.Equal(before - sale.Price - sale.Tax, player.Credits);
        Assert.True(ship.IsFree);
        Assert.Equal(true, yard.Works.SpecSells);
        context.AssertSound();
    }

    [Fact]
    public void A_ship_for_sale_on_the_frontier_is_paid_at_handover_with_no_tax_and_a_buyer_that_cannot_pay_defaults()
    {
        SimulationContext context = World(withPlayer: true);
        ActorState player = context.Player();
        ActorState salvage = context.Actor("Edge Salvage");
        Ship worn = context.Ship(salvage.Ships.Single());
        Assert.Equal(new FittedPart(PartKind.LightFrame, Grade.B), worn.Parts.Frame);
        ShipSaleContract sale = Market.PostShipSale(context, salvage, worn, Credits.Of(9_000m), Credits.Of(7_000m), context.CurrentTick + 240);
        Assert.False(sale.UsesEscrow);
        Assert.Equal(0m, sale.TaxRate);

        ActorState broke = context.Actor("Dust Runner Kael");
        context.ApplySink(broke, broke.Credits);
        Assert.Equal(Signing.Signed, Market.BuyShip(context, broke, sale, sale.OfferedPrice));
        Assert.Equal((ContractStatus.Defaulted, salvage.Id), (sale.Status, worn.Owner)); // the seller keeps its ship

        ShipSaleContract again = Market.PostShipSale(context, salvage, worn, Credits.Of(9_000m), Credits.Of(7_000m), context.CurrentTick + 240);
        Credits before = player.Credits;
        Assert.Equal(Signing.Signed, Market.BuyShip(context, player, again, again.OfferedPrice));
        Assert.Equal((ContractStatus.Fulfilled, player.Id), (again.Status, worn.Owner));
        Assert.Equal(before - Credits.Of(9_000m), player.Credits);
        context.AssertSound();
    }

    [Fact]
    public void Edge_salvage_buys_a_ship_cheap_brings_it_home_and_breaks_up_one_it_cannot_sell()
    {
        SimulationContext context = World();
        ActorState salvage = context.Actor("Edge Salvage");
        ActorState pell = context.Actor("Pell Mirza");
        Ship ship = context.ShipOf(pell);
        decimal value = ship.Stats.Value.Amount;
        ShipSaleContract sale = Market.PostShipSale(context, pell, ship, Credits.Of(value * 0.44m), Credits.Of(value * 0.4m), context.CurrentTick + 2_000);
        sale.ShowAt(salvage.Home, context.CurrentTick);

        DecisionEngine.RunSalvage(context, salvage);

        Assert.Equal((ContractStatus.Fulfilled, salvage.Id), (sale.Status, ship.Owner));
        Assert.True(ship.Job is { IsFerry: true }, "It flies home.");
        context.RunTo(context.CurrentTick + 400);
        Assert.Equal(salvage.Home, ship.Location);
        Assert.True(ship.IsFree);

        context.RunTo(ship.OwnedSince + DecisionEngine.ScrapAfterHours);
        DecisionEngine.RunSalvage(context, salvage);
        DecisionEngine.RunSalvage(context, salvage);
        Assert.True(ship.Scrapped);
        Assert.DoesNotContain(ship.Id, salvage.Ships);
        Assert.Equal(ShipEventKind.Scrapped, ship.History[^1].Kind);
        context.AssertSound();
    }

    [Fact]
    public void A_trader_with_busy_ships_orders_another_and_one_with_idle_ships_sells_the_idlest()
    {
        SimulationContext context = World();
        ActorState coreward = context.Actor("Coreward Logistics");
        ActorState foundry = context.Actor("Foundry Freightways");
        long start = context.CurrentTick;

        // Coreward's ships busy the whole month; Foundry's sit idle.
        foreach (Ship ship in coreward.Ships.Select(context.Ship))
        {
            ship.Reserve(start, new ShipJob(coreward.Id, GoodType.Food, 0m, ship.Location, PickupSource.Aboard, null), null);
        }

        context.RunTo(start + (40 * 24));
        Assert.True(DecisionEngine.WantsShip(context, coreward));
        Assert.False(DecisionEngine.WantsShip(context, foundry));
        foreach (Ship ship in coreward.Ships.Select(context.Ship))
        {
            ship.FinishJob(context.CurrentTick);
        }

        Market.Dispatch(context, context.Ship(coreward.Ships[0]), new ShipJob(coreward.Id, GoodType.Food, 0m, coreward.Home, PickupSource.Aboard, null));
        DecisionEngine.RunTrader(context, coreward);
        BuildContract order = context.Contracts.Values.OfType<BuildContract>().Single(b => b.Client == coreward.Id);
        Assert.Equal(coreward.Home, order.Place);

        DecisionEngine.RunTrader(context, foundry);
        ShipSaleContract sale = context.Contracts.Values.OfType<ShipSaleContract>().Single(s => s.Seller == foundry.Id);
        Assert.Equal(1, foundry.Ships.Count(id => context.Ship(id).ListedFor is not null));
        DecisionEngine.RunTrader(context, foundry);
        Assert.Single(context.Contracts.Values.OfType<ShipSaleContract>(), s => s.Seller == foundry.Id); // one up for sale at a time
        Assert.True(context.Ship(sale.Ship).Stats.RunningCostPerTick > Credits.Zero);
        context.AssertSound();
    }

    [Fact]
    public void A_factory_makes_parts_from_its_inputs_and_makes_do_a_grade_lower_without_one()
    {
        SimulationContext context = World();
        ActorState frames = context.Actor("Tycho Frameworks");
        ActorState ore = context.Actor("Rust Belt Extraction");
        context.Consume(frames, GoodType.LightFrameA, 1m); // none in stock: it makes one
        decimal parts = frames.Stock(GoodType.Parts);
        decimal oreHeld = frames.Stock(GoodType.Ore);
        Credits before = frames.Credits;

        DecisionEngine.RunFactory(context, frames);
        Assert.Equal((PartKind.LightFrame, Grade.A), (frames.Work!.Kind, frames.Work.Grade));
        Assert.Equal((parts - 20m, oreHeld - 40m), (frames.Stock(GoodType.Parts), frames.Stock(GoodType.Ore)));
        Assert.Equal(before - frames.Factory!.Labour(PartKind.LightFrame, Grade.A), frames.Credits);
        context.RunTo(frames.Work.Finishes);
        DecisionEngine.RunFactory(context, frames);
        Assert.Equal(1m, frames.Stock(GoodType.LightFrameA));

        // Out of ore, with none of a part it is short of: it waits, posts for ore, and after three days makes it without, a grade lower.
        context.Consume(frames, GoodType.Ore, frames.Stock(GoodType.Ore));
        context.Consume(frames, GoodType.LightFrameA, 1m);
        frames.Work = null;
        DecisionEngine.RunFactory(context, frames);
        Assert.Null(frames.Work);
        Assert.Contains(context.Contracts.Values.OfType<SaleContract>(), s => s.PostedBy == frames.Id && s.Good == GoodType.Ore && s.Side == SaleSide.Wanted);
        context.RunTo(context.CurrentTick + DecisionEngine.MakeDoAfterHours);
        DecisionEngine.RunFactory(context, frames);
        Assert.Equal((PartKind.LightFrame, Grade.B), (frames.Work!.Kind, frames.Work.Grade));
        Assert.True(context.PartsMadeDo >= 1);
        _ = ore;
        context.AssertSound();
    }

    // ---- helpers ----

    // A quiet world whose yards build nothing on spec unless a test asks, and whose player can afford a ship.
    private static SimulationContext World(bool withPlayer = false, bool answers = false)
    {
        SimulationContext context = TestWorld.Quiet(withPlayer: withPlayer, answers: answers);
        foreach (ActorState yard in context.ActorsOrdered.Where(a => a.Yard is not null))
        {
            yard.Works.SpecSells = false;
        }

        if (withPlayer)
        {
            context.ApplyFaucet(context.Player(), Credits.Of(20_000m));
        }

        return context;
    }

    // A Light Freighter's Build at the yard's own place, signed by the yard at the posted price.
    private static BuildContract PostAndSign(
        SimulationContext context, ActorState client, ActorState yard, TermPackage package, decimal reservation = 10_000m, int days = 30)
    {
        BuildContract build = Market.PostBuild(
            context, client, Freighter, yard.Home, Credits.Of(9_000m), Credits.Of(client.IsPlayer ? 9_000m : reservation), context.CurrentTick + (days * 24), package);
        Assert.Equal(Signing.Signed, Market.TakeBuild(context, yard, build, build.OfferedPrice));
        return build;
    }

    // Tycho Frameworks holds no Light frames, so the yard can't get a grade-A one at once (booked as used, so the ledger balances).
    private static void EmptyFrames(SimulationContext context)
    {
        ActorState frames = context.Actor("Tycho Frameworks");
        context.Consume(frames, GoodType.LightFrameA, frames.Stock(GoodType.LightFrameA));
    }

    // The yard orders the Build's parts; the drive's order (from Meridian, which never sees it in a quiet world) expires unsupplied.
    private static SaleContract FailTheDriveOrder(SimulationContext context, ActorState yard, BuildContract build)
    {
        DecisionEngine.RunYard(context, yard);
        SaleContract order = context.Contracts.Values.OfType<SaleContract>().Single(s => s.ForBuild == build.Id && s.Good == GoodType.StandardDriveA);
        context.RunTo(order.DeadlineTick);
        DecisionEngine.RunYard(context, yard);
        return order;
    }
}
