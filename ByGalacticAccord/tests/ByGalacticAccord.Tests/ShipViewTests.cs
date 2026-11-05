using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using ByGalacticAccord.Engine.Actors;
using ByGalacticAccord.Engine.Domain;
using ByGalacticAccord.Engine.Simulation;
using ByGalacticAccord.Presentation.Session;
using ByGalacticAccord.Presentation.Text;
using ByGalacticAccord.Presentation.Views;

namespace ByGalacticAccord.Tests;

/// <summary>
/// The client's ships (plan S7, design §6, §11.3): the ship sheet, a Build's orders tree, the build composer, the yards' bids as
/// threads, the decisions a Build hands the player, ships for sale and Builds on the board, and every new sentence.
/// </summary>
public sealed class ShipViewTests
{
    [Fact]
    public void The_ship_sheet_shows_its_parts_its_stats_from_the_parts_where_it_was_built_and_what_was_substituted()
    {
        GameSession session = Session();
        SimulationContext context = session.Context;
        ActorState yard = context.Actor("Tycho Yards");
        context.Consume(context.Actor("Tycho Frameworks"), GoodType.LightFrameA, 1m);
        context.Produce(yard, GoodType.LightFrameB, 1m);
        context.Produce(yard, GoodType.StandardDriveA, 1m);
        context.Produce(yard, GoodType.RefrigerationUnitA, 1m);
        BuildContract build = Market.PostBuild(
            context, session.Player, Designs.Parts(Design.RefrigeratedTransport), yard.Home, Credits.Of(13_000m), Credits.Of(13_000m), context.CurrentTick + (30 * 24), TermPackage.Standard);
        Assert.Equal(Signing.Signed, Market.TakeBuild(context, yard, build, build.OfferedPrice));
        DecisionEngine.RunYard(context, yard);
        context.RunTo(yard.Works.Current!.AssemblyDone!.Value);
        DecisionEngine.RunYard(context, yard);

        ShipSheetModel sheet = ShipView.Build(session, build.Ship!.Value);
        Assert.StartsWith("Refrigerated Transport S", sheet.Title, StringComparison.Ordinal);
        Assert.Contains("Light frame, grade B", sheet.Parts.Select(p => p.Value));
        StatLine cargo = sheet.Stats.Single(s => s.Label == "Cargo");
        Assert.Equal("105", cargo.Value); // a grade-B Light frame's 115.2, less the refrigeration unit's 10
        Assert.Contains("Light frame, grade B 115", cargo.Explanation, StringComparison.Ordinal);
        Assert.Contains("Refrigeration unit, grade A -10", cargo.Explanation.Replace("−", "-", StringComparison.Ordinal), StringComparison.Ordinal);
        Assert.Contains("power", sheet.Stats.Single(s => s.Label == "Speed").Explanation, StringComparison.Ordinal);
        Assert.Contains("Tycho Yards", sheet.Built, StringComparison.Ordinal);
        Assert.Contains("Frame: Light frame, grade B for grade A", sheet.Substituted, StringComparison.Ordinal);
        Assert.Equal(2, sheet.History.Count); // built, then handed over to the player: newest first
        Assert.Contains("you", sheet.History[0].Text, StringComparison.Ordinal);
        AssertFilled(sheet.Sub, sheet.Where, sheet.Flown, sheet.PartsTitle, sheet.StatsTitle, sheet.HistoryTitle);

        // It is in the player's ledger, as a line that opens the sheet, and the contract panel links to it.
        Assert.Contains(LedgerView.Build(session).Possessions, l => l.Ship == build.Ship);
        Assert.Equal(build.Ship, ContractView.Build(session, build.Id).Ship);
    }

    [Fact]
    public void A_builds_orders_tree_shows_each_part_its_order_who_took_it_and_what_carries_it()
    {
        GameSession session = Session();
        SimulationContext context = session.Context;
        ActorState yard = context.Actor("Tycho Yards");
        ActorState drives = context.Actor("Meridian Drive Works");
        BuildContract build = Market.PostBuild(
            context, session.Player, Designs.Parts(Design.LightFreighter), yard.Home, Credits.Of(9_000m), Credits.Of(9_000m), context.CurrentTick + (30 * 24), TermPackage.Standard);
        Assert.Equal(Signing.Signed, Market.TakeBuild(context, yard, build, build.OfferedPrice));
        DecisionEngine.RunYard(context, yard);
        SaleContract driveOrder = context.Contracts.Values.OfType<SaleContract>().Single(s => s.ForBuild == build.Id && s.Good == GoodType.StandardDriveA);
        context.ShowAJumpFurther(driveOrder);
        DecisionEngine.RunFactory(context, drives);

        IReadOnlyList<OrderLine> orders = ContractView.Build(session, build.Id).Orders!;
        Assert.Equal(0, orders[0].Depth);
        Assert.Contains("Tycho Yards", orders[0].Text, StringComparison.Ordinal);
        Assert.Contains(orders, l => l.Depth == 1 && l.Text.StartsWith("Frame: Light frame, grade A, in at the yard", StringComparison.Ordinal));
        Assert.Contains(orders, l => l.Depth == 1 && l.Text.StartsWith("Drive: Standard drive, grade A, on order", StringComparison.Ordinal));
        OrderLine order = orders.Single(l => l.Contract == driveOrder.Id);
        Assert.Contains("Meridian Drive Works", order.Text, StringComparison.Ordinal);
        Assert.Contains(orders, l => l.Depth == 3 && l.Text.StartsWith("Carried from Meridian Station by nobody yet", StringComparison.Ordinal));

        // The parts order shows the Build it is for above it.
        Assert.Equal(build.Id, ContractView.Build(session, driveOrder.Id).Orders![0].Contract);
        AssertFilled(orders.Select(o => o.Text).ToArray());
    }

    [Fact]
    public void The_build_composer_drafts_a_design_shows_its_stats_and_terms_and_is_shown_where_the_yards_are()
    {
        GameSession session = Session();
        SimulationContext context = session.Context;
        ComposerDraft draft = ComposerView.Default(session, PostKind.Build);
        ComposerViewModel composer = ComposerView.Build(session, draft);

        Assert.True(composer.IsBuild);
        Assert.Equal(Design.RefrigeratedTransport, composer.Design);
        Assert.Equal(6, composer.Designs!.Count); // five designs and custom
        Assert.Equal(13_000m, draft.Price);
        Assert.Contains(composer.SpecStats!, s => s.Label == "Refrigeration" && s.Value == "80%");
        Assert.Contains(composer.Terms, t => t.Contains("deposit", StringComparison.Ordinal));
        Assert.Contains(composer.Terms, t => t.StartsWith("Standard: a part one grade lower", StringComparison.Ordinal));
        Assert.Contains("deposit of ₵2,600", composer.Commitment, StringComparison.Ordinal);
        Assert.Equal(new[] { context.Actor("Tycho Yards").Home, context.Actor("Helios Orbital Yards").Home }.OrderBy(l => l.Value), draft.ShowAt!.OrderBy(l => l.Value));
        Assert.True(composer.CanPost, composer.Blocked);

        // A custom build (design §13: a Light frame, a Fast drive and refrigeration) re-prices a draft still at the design's value.
        ShipParts custom = ShipParts.Of(PartKind.LightFrame, PartKind.FastDrive, PartKind.RefrigerationUnit);
        ComposerDraft changed = ComposerView.WithSpec(context, draft, custom);
        Assert.Equal(ShipStats.From(custom).Value.Amount, changed.Price);
        Assert.Null(ComposerView.Build(session, changed).Design);

        PostOutcome outcome = session.Post(ComposerView.ToPost(session, changed));
        var build = Assert.IsType<BuildContract>(outcome.Post);
        Assert.Equal(custom, build.Spec);
        Assert.Contains("custom ship (Light frame, Fast drive, Refrigeration unit)", ContractView.Build(session, build.Id).Head, StringComparison.Ordinal);
    }

    [Fact]
    public void The_yards_bids_read_as_threads_on_the_players_build()
    {
        GameSession session = new(World(answers: true));
        SimulationContext context = session.Context;
        PostOutcome outcome = session.Post(ComposerView.ToPost(session, ComposerView.Default(session, PostKind.Build)));
        context.RunTo(context.CurrentTick + 4 + Haggling.SlowestReply);

        ContractViewModel view = ContractView.Build(session, outcome.Post!.Id);
        Assert.Equal(2, view.Threads.Count);
        Assert.Contains(view.Threads, t => t.Title.Contains("Tycho Yards", StringComparison.Ordinal));
        Assert.Contains(view.Threads, t => t.Title.Contains("Helios Orbital Yards", StringComparison.Ordinal));
        Assert.All(view.Threads, t => Assert.True(t.YourTurn)); // each yard bid; the player may accept, counter or decline
    }

    [Fact]
    public void A_supply_failure_and_a_ship_built_outside_the_policy_are_put_to_the_player_with_what_each_answer_does()
    {
        GameSession session = Session();
        SimulationContext context = session.Context;
        ActorState yard = context.Actor("Tycho Yards");
        BuildContract build = Market.PostBuild(
            context, session.Player, Designs.Parts(Design.LightFreighter), yard.Home, Credits.Of(9_000m), Credits.Of(9_000m), context.CurrentTick + (14 * 24), TermPackage.Standard);
        Assert.Equal(Signing.Signed, Market.TakeBuild(context, yard, build, build.OfferedPrice));
        DecisionEngine.RunYard(context, yard);
        SaleContract order = context.Contracts.Values.OfType<SaleContract>().Single(s => s.ForBuild == build.Id && s.Good == GoodType.StandardDriveA);
        context.RunTo(order.DeadlineTick);
        DecisionEngine.RunYard(context, yard);

        DecisionPrompt prompt = Assert.Single(DecisionView.Build(session));
        Assert.Contains("Standard drive", prompt.Text, StringComparison.Ordinal);
        Assert.Equal(new[] { DecisionChoice.Extend, DecisionChoice.Substitute, DecisionChoice.TakeRefund }, prompt.Options.Select(o => o.Choice));
        Assert.Contains("₵600 off", prompt.Options[1].Note, StringComparison.Ordinal);
        Assert.Contains(Format.Money(build.Proposal!.Compensation), prompt.Options[0].Note, StringComparison.Ordinal);
        AssertFilled(prompt.Options.Select(o => o.Note).ToArray());
        Assert.Contains(LogView.Build(session).Lines, l => l.Text.Contains("can't get the Standard drive", StringComparison.Ordinal));
    }

    [Fact]
    public void Ships_for_sale_and_builds_are_on_the_board_and_a_ship_can_be_bought_from_the_contract_panel()
    {
        GameSession session = Session();
        SimulationContext context = session.Context;
        ActorState yard = context.Actor("Tycho Yards");
        ActorState client = context.Actor("Foundry Freightways");
        context.PresentAt(session.Player, "Tycho Foundry");
        ActorState pell = context.Actor("Pell Mirza");
        Ship ship = context.ShipOf(pell);
        ShipSaleContract sale = Market.PostShipSale(context, pell, ship, Credits.Of(6_000m), Credits.Of(5_000m), context.CurrentTick + 240);
        sale.ShowAt(yard.Home, context.CurrentTick);
        BuildContract build = Market.PostBuild(context, client, Designs.Parts(Design.BulkHauler), yard.Home, Credits.Of(17_000m), Credits.Of(18_000m), context.CurrentTick + 720);

        BoardViewModel board = BoardView.Build(session);
        BoardRow forSale = board.Rows.Single(r => r.Id == sale.Id);
        Assert.Contains("Light Freighter", forSale.Title, StringComparison.Ordinal);
        Assert.Equal("₵6,240", forSale.TakeHome); // the price and the 4% tax at Tycho
        BoardRow wanted = board.Rows.Single(r => r.Id == build.Id);
        Assert.True(wanted.Blocked);
        Assert.Contains("Bulk Hauler", wanted.Title, StringComparison.Ordinal);

        ContractViewModel view = ContractView.Build(session, sale.Id);
        Assert.True(view.CanAccept);
        Assert.Equal("Buy the ship", view.AcceptLabel);
        Assert.Equal(ship.Id, view.Ship);
        Assert.Contains(view.Terms, t => t.Contains("changes hands", StringComparison.Ordinal));
        Assert.Equal(Templates.Get("Block.Build"), ContractView.Build(session, build.Id).Blocked);

        DealOutcome bought = session.Accept(sale.Id);
        Assert.Equal(Signing.Signed, bought.Signing);
        Assert.StartsWith("Bought for ₵6,000", ContractView.OutcomeText(session, sale, bought), StringComparison.Ordinal);
        Assert.Contains(ship.Id, session.Player.Ships);
    }

    [Fact]
    public void Every_build_and_ship_sale_entry_reads_as_a_sentence_for_an_ai_and_for_the_player()
    {
        SimulationContext context = World();
        ActorId player = context.PlayerActorId!.Value;
        ActorState yard = context.Actor("Tycho Yards");
        ActorState client = context.Actor("Foundry Freightways");
        ActorState pell = context.Actor("Pell Mirza");
        context.ApplyFaucet(context.Player(), Credits.Of(20_000m));
        var builds = new List<BuildContract>();
        foreach (ActorState poster in new[] { context.Player(), client })
        {
            BuildContract b = Market.PostBuild(context, poster, Designs.Parts(Design.LightFreighter), yard.Home, Credits.Of(9_000m), Credits.Of(9_000m), context.CurrentTick + 720);
            Assert.Equal(Signing.Signed, Market.TakeBuild(context, yard, b, b.OfferedPrice));
            builds.Add(b);
        }

        context.Produce(yard, GoodType.LightFrameA, 1m);
        context.Produce(yard, GoodType.StandardDriveA, 1m);
        Ship built = context.BuildShip(yard, Designs.Parts(Design.LightFreighter), yard, builds[0].Id, Designs.Parts(Design.LightFreighter));
        var sales = new[]
        {
            Market.PostShipSale(context, pell, context.ShipOf(pell), Credits.Of(6_000m), Credits.Of(5_000m), context.CurrentTick + 240),
            Market.PostShipSale(context, yard, built, Credits.Of(8_000m), Credits.Of(7_000m), context.CurrentTick + 240),
        };

        var entries = new List<JournalEntry> { new StockShipBuiltEntry(5, yard.Id, built.Id), new ShipScrappedEntry(9, yard.Id, built.Id) };
        foreach (BuildContract b in builds)
        {
            entries.Add(new ContractPostedEntry(1, b.Id, b.Client));
            entries.Add(new ContractSignedEntry(2, b.Id, yard.Id, b.Price));
            entries.Add(new ContractExpiredEntry(3, b.Id));
            entries.Add(new ContractWithdrawnEntry(3, b.Id));
            entries.Add(new ContractInDefaultEntry(7, b.Id, yard.Id, b.Client));
            entries.Add(new PaymentDefaultedEntry(7, b.Id, b.Client, yard.Id));
            foreach (CancelReason reason in Enum.GetValues<CancelReason>())
            {
                entries.Add(new ContractCancelledEntry(8, b.Id, b.Client, reason, Credits.Of(1_800m)));
            }

            entries.Add(new ContractCancelledEntry(8, b.Id, yard.Id, CancelReason.AtWill, Credits.Of(1_800m)));
            foreach (FailureReason reason in Enum.GetValues<FailureReason>())
            {
                entries.Add(new DeliveryFailedEntry(8, b.Id, yard.Id, reason));
            }

            entries.Add(new ShipBuiltEntry(5, b.Id, yard.Id, built.Id));
            entries.Add(new ShipHandedOverEntry(6, b.Id, yard.Id, b.Client, built.Id, 0, b.Price, Credits.Zero, Credits.Zero));
            entries.Add(new ShipHandedOverEntry(6, b.Id, yard.Id, b.Client, built.Id, 30, b.Price, Credits.Of(90m), Credits.Of(600m)));
            entries.Add(new SupplyFailureEntry(5, b.Id, yard.Id, b.Id, PartSlot.Drive));
            foreach (Remedy remedy in Enum.GetValues<Remedy>())
            {
                entries.Add(new RemedyChosenEntry(5, b.Id, b.Client, remedy));
            }

            entries.Add(new OfferMadeEntry(4, b.Id, new NegotiationId(b.Id.Value), yard.Id, b.Client, new Offer(Credits.Of(9_400m), 300, TermPackage.Lenient), 1, 5, Tell.Readily));
        }

        foreach (ShipSaleContract sale in sales)
        {
            entries.Add(new ContractPostedEntry(1, sale.Id, sale.Seller));
            foreach (ActorId buyer in new[] { player, client.Id })
            {
                entries.Add(new ContractSignedEntry(2, sale.Id, buyer, sale.OfferedPrice));
                entries.Add(new ShipHandedOverEntry(2, sale.Id, sale.Seller, buyer, sale.Ship, 0, sale.OfferedPrice, Credits.Zero, Credits.Zero));
                entries.Add(new PaymentDefaultedEntry(2, sale.Id, buyer, sale.Seller));
            }

            entries.Add(new ContractExpiredEntry(3, sale.Id));
            entries.Add(new ContractWithdrawnEntry(3, sale.Id));
        }

        foreach (JournalEntry entry in entries)
        {
            string text = JournalText.Describe(context, entry);
            Assert.DoesNotContain("{", text, StringComparison.Ordinal);
            Assert.DoesNotContain("  ", text, StringComparison.Ordinal);
            Assert.EndsWith(".", text, StringComparison.Ordinal);
            Assert.False(Regex.IsMatch(text, @"(?<!^|\. )You\b"), $"The player's name inside a sentence: {text}");
        }
    }

    // A quiet world whose yards build nothing on spec, and a player who can afford a ship.
    private static SimulationContext World(bool answers = false)
    {
        SimulationContext context = TestWorld.Quiet(withPlayer: true, answers: answers);
        foreach (ActorState yard in context.ActorsOrdered.Where(a => a.Yard is not null))
        {
            yard.Works.SpecSells = false;
        }

        context.ApplyFaucet(context.Player(), Credits.Of(20_000m));
        return context;
    }

    private static GameSession Session() => new(World());

    private static void AssertFilled(params string[] texts) =>
        Assert.All(texts, t => Assert.False(string.IsNullOrWhiteSpace(t) || t.Contains('{', StringComparison.Ordinal), $"Not filled: '{t}'"));
}
