using System.Collections.Generic;
using System.Linq;
using ByGalacticAccord.Engine.Actors;
using ByGalacticAccord.Engine.Domain;
using ByGalacticAccord.Engine.Simulation;
using ByGalacticAccord.Presentation.Session;
using ByGalacticAccord.Presentation.Views;

namespace ByGalacticAccord.Tests;

/// <summary>
/// The view models over a played game: every panel builds in every state the game passes through, every
/// template fills, the map keeps everything inside its frame, and the ledger shows what the player owns and where.
/// </summary>
public sealed class ViewTests
{
    [Theory]
    [InlineData(42L)]
    [InlineData(20260619L)]
    public void Every_view_builds_while_the_player_trades(long seed)
    {
        var session = new GameSession(seed, startTick: 30);
        session.SetSpeed(ClockSpeed.X16);
        var seen = new HashSet<ContractId>();
        var kinds = new HashSet<ContractType>();
        int signed = 0;
        int stored = 0;
        int brokered = 0;
        int decided = 0;
        int posted = 0;
        int agreed = 0;
        int countered = 0;
        bool boughtLot = false;
        int frame = 0;
        while (session.Now < 2_400)
        {
            session.Advance(0.25);
            frame++;
            if (session.Speed == ClockSpeed.Paused)
            {
                AssertFilled(HeaderView.Build(session).Alerts);
                foreach (DecisionPrompt prompt in DecisionView.Build(session))
                {
                    AssertFilled(prompt.Title, prompt.Text);
                    AssertFilled(prompt.Options.Select(o => o.Label).Concat(prompt.Options.Select(o => o.Note)));
                    CheckContract(session, prompt.Contract);
                    session.Decide(prompt.Decision, prompt.Options[decided++ % prompt.Options.Count].Choice);
                }

                session.SetSpeed(ClockSpeed.X16);
            }

            if (frame % 6 != 0)
            {
                continue; // look every game day: six frames of four hours
            }

            HeaderViewModel header = HeaderView.Build(session);
            AssertFilled(header.Who, header.Date, header.Credits, header.Fleet, header.Running);
            BoardViewModel board = BoardView.Build(session);
            AssertFilled(board.Title);
            foreach (BoardRow row in board.Rows)
            {
                AssertFilled(row.Kind, row.Title, row.Route, row.TakeHome, row.Rate, row.Danger, row.Jurisdiction, row.Due, row.Note ?? string.Empty);
                Assert.Equal(session.Context.Contract(row.Id).Type, row.Type);
                seen.Add(row.Id);
                kinds.Add(row.Type);
                CheckContract(session, row.Id);
            }

            // Contracts seen earlier, now taken, settled or expired.
            foreach (ContractId id in seen.Take(10))
            {
                CheckContract(session, id);
            }

            foreach (LogLine line in LogView.Build(session).Lines)
            {
                AssertFilled(line.When, line.Text);
            }

            CheckShips(session);
            LedgerViewModel ledger = CheckLedger(session);
            PostsViewModel posts = CheckPosts(session);
            if (frame % 24 == 0)
            {
                CheckComposer(session);
            }

            // Post a wanted Sale of food at Verdant now and then, a little under what it is worth; answer each answer that
            // waits: accept one within the base value, else counter it once, else decline.
            if (posted < 3 && frame % 60 == 0 && session.Post(ComposerView.ToPost(session, ComposerView.Default(session, PostKind.Wanted) with
                { Place = session.Context.Place("Verdant").Id, Price = 330m })).Posted)
            {
                posted++;
            }

            foreach (Negotiation thread in session.PlayerNegotiations.Where(n => n.IsOpen && n.AwaitingFrom == session.Player.Id).ToList())
            {
                CheckContract(session, thread.Contract);
                Offer theirs = thread.Latest.Offer;
                if (theirs.Price.Amount <= 600m && session.AcceptOffer(thread.Id) == Signing.Signed)
                {
                    agreed++;
                }
                else if (thread.Rounds < 3 && session.Counter(thread.Contract, theirs with { Price = Credits.Of(System.Math.Round(theirs.Price.Amount * 0.9m, 2)) }, thread.Id) == DealBlock.None)
                {
                    countered++;
                }
                else
                {
                    session.Decline(thread.Id);
                }

                CheckContract(session, thread.Contract);
            }

            CheckPosts(session);

            // Put anything aboard into storage when the ship is free, so the hold is clear for the next deal.
            if (ledger.Possessions.FirstOrDefault(l => l.Action is { Store: true }) is { Action: { } store } && session.Store(store.Good))
            {
                stored++;
                CheckLedger(session);
            }

            // Buy one lot, once, to have goods of the player's own; otherwise take the best-paying deal whenever the ship is free.
            BoardRow? lot = boughtLot ? null : board.Rows.FirstOrDefault(r => !r.Blocked && session.Context.Contract(r.Id) is SaleContract { Side: SaleSide.ForSale });
            BoardRow? best = lot ?? board.Rows
                .Where(r => !r.Blocked && r.TakeHomeTone == Tone.Gain)
                .OrderByDescending(r => decimal.Parse(r.TakeHome.Replace("₵", string.Empty).Replace(",", string.Empty), System.Globalization.CultureInfo.InvariantCulture))
                .FirstOrDefault();
            if (best is not null && session.PlayerShip.IsFree && session.Accept(best.Id).Signed)
            {
                signed++;
                boughtLot |= lot is not null;
                CheckContract(session, best.Id);
                CheckLedger(session);
            }

            // With the ship busy, fill a wanted Sale through a carrier now and then, as a trader brokers.
            if (!session.PlayerShip.IsFree && brokered < 3 && frame % 48 == 0
                && board.Rows.FirstOrDefault(r => ContractView.Build(session, r.Id).Actions.Any(a => a.Command == ContractCommand.Broker && a.Enabled)) is { } wanted
                && session.Broker(wanted.Id).Signed)
            {
                brokered++;
                CheckContract(session, wanted.Id);
                CheckLedger(session);
            }
        }

        Assert.True(signed >= 3, $"The player signed {signed} deals.");
        Assert.True(boughtLot && stored > 0, $"Bought a lot: {boughtLot}; stored goods {stored} times.");
        Assert.True(brokered > 0, "The player filled a sale through a carrier.");
        Assert.True(posted > 0 && session.PlayerNegotiations.Any(), $"The player posted {posted} times and haggled {session.PlayerNegotiations.Count()} times.");
        Assert.True(agreed + countered > 0, "The player answered a haggle.");
        Assert.Contains(ContractType.Sale, kinds);
        Assert.Contains(ContractType.Haul, kinds);
        Assert.Contains(LogView.Build(session, 400).Lines, l => l.Yours);
    }

    [Fact]
    public void The_board_lists_only_open_contracts_the_player_can_see_newest_first_each_with_its_kind()
    {
        var session = new GameSession(42L, startTick: 48);
        HaulContract haul = PlayerActionsTests.FarmHaul(session.Context);
        BoardViewModel board = BoardView.Build(session);

        Assert.NotEmpty(board.Rows);
        Assert.All(board.Rows, r => Assert.Equal(ContractStatus.Open, session.Context.Contract(r.Id).Status));
        Assert.All(board.Rows, r => Assert.Contains(r.Type, new[] { ContractType.Sale, ContractType.Haul }));
        Assert.Equal(board.Rows.Select(r => r.Id.Value).OrderByDescending(v => v), board.Rows.Select(r => r.Id.Value));
        BoardRow row = board.Rows.Single(r => r.Id == haul.Id);
        Assert.Equal((ContractType.Haul, "Haul"), (row.Type, row.Kind));
    }

    [Fact]
    public void The_ledger_shows_goods_aboard_and_in_storage_and_the_contracts_in_progress()
    {
        var session = new GameSession(42L, startTick: 48);
        SaleTests.GiveAboard(session.Context, session.PlayerShip, session.Player, GoodType.Parts, 25m);

        LedgerViewModel aboard = LedgerView.Build(session);
        LedgerLine goods = Assert.Single(aboard.Possessions);
        Assert.Equal(new LedgerAction("Store at Meridian Station", GoodType.Parts, Store: true), goods.Action);
        Assert.Contains("hold", goods.Detail, System.StringComparison.Ordinal);
        Assert.Empty(aboard.Contracts);

        session.Store(GoodType.Parts);
        LedgerLine stored = Assert.Single(LedgerView.Build(session).Possessions);
        Assert.Equal(new LedgerAction("Load aboard", GoodType.Parts, Store: false), stored.Action);
        Assert.Contains("storage at Meridian Station", stored.Detail, System.StringComparison.Ordinal);

        HaulContract haul = PlayerActionsTests.FarmHaul(session.Context);
        session.Accept(haul.Id);
        LedgerViewModel busy = LedgerView.Build(session);
        Assert.Null(Assert.Single(busy.Possessions).Action); // the ship is away, so the goods stay put
        LedgerLine contract = Assert.Single(busy.Contracts);
        Assert.Equal((haul.Id, Tone.Gain), (contract.Contract, contract.Tone));
        Assert.StartsWith("carrying for Verdant Agricombine", contract.Detail, System.StringComparison.Ordinal);
    }

    [Fact]
    public void Selecting_a_contract_draws_its_empty_leg_and_laden_legs()
    {
        var session = new GameSession(42L, startTick: 48);
        HaulContract away = PlayerActionsTests.FarmHaul(session.Context); // from Verdant; the player is at Meridian
        DealEstimate estimate = DealEstimate.ForHaul(session.Context, session.Player, session.PlayerShip, away)!;

        MapViewModel map = MapView.Build(session, away.Id);

        Assert.Equal(estimate.Repositioning!.Path.Count - 1, map.Overlay.Count(l => l.Empty));
        Assert.Equal(estimate.Laden!.Path.Count - 1, map.Overlay.Count(l => !l.Empty));
        Assert.Equal(map.Overlay.Count, map.Routes.Count(r => r.Dimmed) + DuplicateLegs(map.Overlay));
        Assert.NotNull(map.OverlayEta);
        Assert.Empty(MapView.Build(session, null).Overlay);
    }

    [Fact]
    public void A_ship_moves_along_its_path_by_share_of_the_hours()
    {
        SimulationContext context = WorldGenerator.Generate(42L, withPlayer: false).Context;
        Location meridian = context.Locations.Values.Single(l => l.Name == "Meridian Station");
        Location tycho = context.Locations.Values.Single(l => l.Name == "Tycho Foundry");
        Location rust = context.Locations.Values.Single(l => l.Name == "Rust Belt");
        var course = new ShipCourse(new[] { meridian.Id, tycho.Id, rust.Id }, DepartedTick: 100, ArrivalTick: 210); // 40 h + 70 h

        Assert.Equal((meridian.Map.X, meridian.Map.Y), Rounded(MapView.Along(context, course, 90)));
        Assert.Equal((tycho.Map.X, tycho.Map.Y), Rounded(MapView.Along(context, course, 140)));
        Assert.Equal((rust.Map.X, rust.Map.Y), Rounded(MapView.Along(context, course, 300)));
        (double x, double y) = MapView.Along(context, course, 175); // 35 h into the 70 h leg
        Assert.Equal((tycho.Map.X + rust.Map.X) / 2.0, x, precision: 6);
        Assert.Equal((tycho.Map.Y + rust.Map.Y) / 2.0, y, precision: 6);
    }

    [Theory]
    [InlineData(TermPackage.Strict)]
    [InlineData(TermPackage.Standard)]
    [InlineData(TermPackage.Lenient)]
    public void The_terms_read_as_sentences_from_the_players_side_for_every_package(TermPackage package)
    {
        SimulationContext context = TestWorld.Quiet(withPlayer: true);
        var session = new GameSession(context);
        ActorState habs = context.Actor("Meridian Habs");
        ActorState camp = context.Actor("Rust Belt Camp");
        ActorState farm = context.Actor("Verdant Agricombine");

        // What the player would perform, in the Accord and outside it; a lot it would buy; a haul it posted as shipper.
        SaleContract wanted = context.PostWanted(habs, GoodType.Food, 40m, 700m, hours: 200, package);
        SaleContract frontier = context.PostWanted(camp, GoodType.Food, 40m, 900m, hours: 400, package);
        HaulContract haul = PlayerActionsTests.FarmHaul(context, package: package);
        SaleContract lot = Market.PostSale(
            context, farm, SaleSide.ForSale, GoodType.Food, 20m, farm.Home, Credits.Of(150m), Credits.Of(140m), context.CurrentTick + 48, Holding.StockAt(farm.Home), package);
        session.Broker(frontier.Id);
        HaulContract posted = context.Contracts.Values.OfType<HaulContract>().Single(h => h.Serves == frontier.Id);

        foreach (Contract contract in new Contract[] { wanted, haul, lot, frontier, posted })
        {
            ContractViewModel view = ContractView.Build(session, contract.Id);
            AssertFilled(view.Terms);
            AssertFilled(view.TermsTitle);
            Assert.EndsWith(Presentation.Text.Templates.Package(contract == posted ? TermPackage.Standard : package), view.TermsTitle, System.StringComparison.Ordinal);
            Assert.All(view.Terms, t => Assert.EndsWith(".", t, System.StringComparison.Ordinal));
            Assert.Contains(view.Terms, t => t.Contains("break fee", System.StringComparison.Ordinal));
            if (contract != lot)
            {
                Assert.Contains(view.Terms, t => t.Contains("in default", System.StringComparison.Ordinal));
                Assert.Contains(view.Terms, t => t.Contains("short delivery", System.StringComparison.Ordinal));
            }
        }

        // Its own haul reads from the shipper's side; the haul it would carry, from the carrier's; outside the Accord, no bond.
        Assert.Contains(ContractView.Build(session, posted.Id).Terms, t => t.StartsWith("If ", System.StringComparison.Ordinal) && t.Contains("you may cancel", System.StringComparison.Ordinal));
        Assert.Contains(ContractView.Build(session, haul.Id).Terms, t => t.StartsWith("Goods lost on the way cost you", System.StringComparison.Ordinal) || package == TermPackage.Lenient);
        Assert.Contains(ContractView.Build(session, frontier.Id).Terms, t => t.Contains("no bond to lose", System.StringComparison.Ordinal));
        Assert.Contains(ContractView.Build(session, wanted.Id).Terms, t => t.StartsWith("At the Accord's edge", System.StringComparison.Ordinal));
    }

    [Fact]
    public void A_decision_prompt_says_what_happened_and_what_each_answer_does()
    {
        (SimulationContext context, SaleContract _, HaulContract haul) = FailurePathTests.PlayerBrokerWithLateCarrier();
        var session = new GameSession(context);
        session.Context.RunTo(session.Now + 500);

        DecisionPrompt prompt = Assert.Single(DecisionView.Build(session));
        Assert.Equal(haul.Id, prompt.Contract);
        Assert.StartsWith("Pell Mirza is in default on your haul of 40 Food to Meridian Station", prompt.Text, System.StringComparison.Ordinal);
        Assert.Contains("Its ship is due in", prompt.Text, System.StringComparison.Ordinal);
        Assert.Equal(new[] { DecisionChoice.Cancel, DecisionChoice.Wait }, prompt.Options.Select(o => o.Choice));
        Assert.Contains("bond of " + Presentation.Text.Format.Money(haul.LockedBond), prompt.Options[0].Note, System.StringComparison.Ordinal);

        (SimulationContext stale, SaleContract lot, Ship _) = FailurePathTests.PlayerCollectsAStaleLot();
        DecisionPrompt inspect = Assert.Single(DecisionView.Build(new GameSession(stale)));
        Assert.Equal(lot.Id, inspect.Contract);
        Assert.StartsWith("Sable Vance hands over 40 Food at Halcyon Refinery", inspect.Text, System.StringComparison.Ordinal);
        Assert.Equal(new[] { DecisionChoice.Accept, DecisionChoice.Reject }, inspect.Options.Select(o => o.Choice));
    }

    [Fact]
    public void The_why_chain_reads_a_late_sale_back_through_its_late_haul()
    {
        (SimulationContext context, SaleContract sale, HaulContract haul) = FailurePathTests.PlayerBrokerWithLateCarrier();
        var session = new GameSession(context);
        context.RunTo(context.CurrentTick + 500);
        session.Decide(session.Decisions.Single(), DecisionChoice.Wait);
        context.RunUntilSettled(sale);

        ContractViewModel view = ContractView.Build(session, sale.Id);

        // Newest first: handed over late, because the haul delivered late, after it went into default; each line is the player's.
        Assert.StartsWith("You handed 40 Food to Meridian Habs", view.Why[0].Text, System.StringComparison.Ordinal);
        Assert.Equal(haul.Id, view.Why[1].Contract);
        Assert.StartsWith("Pell Mirza delivered your 40 Food", view.Why[1].Text, System.StringComparison.Ordinal);
        Assert.Contains(view.Why, l => l.Text.StartsWith("Pell Mirza is in default on your haul", System.StringComparison.Ordinal));
        Assert.All(view.Why, l => Assert.True(l.Yours));
        Assert.Equal(ContractView.WhyLines, view.Why.Count);
    }

    [Fact]
    public void The_contract_offers_what_the_player_can_still_do_with_it()
    {
        SimulationContext context = TestWorld.Quiet(withPlayer: true);
        var session = new GameSession(context);
        SaleContract wanted = context.PostWanted(context.Actor("Meridian Habs"), GoodType.Food, 40m, 700m, hours: 200);

        ContractAction broker = Assert.Single(ContractView.Build(session, wanted.Id).Actions);
        Assert.Equal((ContractCommand.Broker, true), (broker.Command, broker.Enabled));
        Assert.StartsWith("Buy at Verdant for", broker.Note, System.StringComparison.Ordinal);

        session.Broker(wanted.Id);
        ContractViewModel yours = ContractView.Build(session, wanted.Id);
        Assert.Equal(new[] { ContractCommand.HaulAgain, ContractCommand.CancelAtWill }, yours.Actions.Select(a => a.Command));
        Assert.Equal("Walk away · " + Presentation.Text.Format.Money(wanted.Terms.BreakFee), yours.Actions[1].Label);
    }

    private static int DuplicateLegs(IReadOnlyList<MapLeg> overlay) =>
        overlay.Count - overlay.Select(l => (System.Math.Min(l.X1, l.X2), System.Math.Max(l.X1, l.X2), System.Math.Min(l.Y1, l.Y2), System.Math.Max(l.Y1, l.Y2))).Distinct().Count();

    private static (int, int) Rounded((double X, double Y) p) => ((int)System.Math.Round(p.X), (int)System.Math.Round(p.Y));

    private static void CheckContract(GameSession session, ContractId id)
    {
        ContractViewModel c = ContractView.Build(session, id);
        AssertFilled(c.KindLine, c.Head, c.Sub, c.Status ?? string.Empty, c.TakeHome, c.TakeHomeNote, c.Party, c.PartyRole, c.Blocked ?? string.Empty);
        AssertFilled(c.Terms);
        foreach (MoneyLine line in c.Lines)
        {
            AssertFilled(line.Label, line.Note, line.Amount);
        }

        foreach (InfoLine line in c.Schedule.Concat(c.Uncertain))
        {
            AssertFilled(line.Label, line.Note, line.Value);
        }

        AssertFilled(c.TermsTitle, c.WhyTitle, c.WhyEnd ?? string.Empty);
        AssertFilled(c.Actions.Select(a => a.Label).Concat(c.Actions.Select(a => a.Note)));
        foreach (LogLine line in c.Why)
        {
            AssertFilled(line.When, line.Text);
        }

        Assert.True(c.Why.Count <= ContractView.WhyLines);
        Assert.Equal(c.IsOpen, c.Status is null);
        AssertFilled(c.ThreadsTitle, c.CounterTitle, c.CounterBlocked ?? string.Empty);
        foreach (ThreadViewModel thread in c.Threads)
        {
            CheckThread(thread);
        }

        if (c.Counter is { } counter)
        {
            CheckForm(counter);
        }

        MapViewModel map = MapView.Build(session, id);
        AssertFilled(map.OverlayTitle ?? string.Empty, map.OverlayEta ?? string.Empty);
        Assert.All(map.Nodes, n => Assert.InRange(n.X, 0, map.Width));
        Assert.All(map.Regions, r => Assert.InRange(r.X + r.Width, 0, map.Width));
    }

    private static void CheckThread(ThreadViewModel thread)
    {
        AssertFilled(thread.Title, thread.Status, thread.AcceptLabel, thread.AcceptNote, thread.DeclineLabel);
        Assert.NotEmpty(thread.Bids);
        foreach (BidLine bid in thread.Bids)
        {
            AssertFilled(bid.Who, bid.When, bid.Offer, bid.Note ?? string.Empty);
        }

        Assert.True(thread.Counter is null || thread.YourTurn);
        if (thread.Counter is { } counter)
        {
            CheckForm(counter);
        }
    }

    private static void CheckForm(OfferFormModel form)
    {
        AssertFilled(form.PriceLabel, form.DeadlineLabel, form.PackageLabel, form.LateFeeLabel, form.SubmitLabel, form.Note);
        AssertFilled(form.Deadlines.Select(d => d.Label).Concat(form.Packages.Select(p => p.Label)).Concat(form.LateFees.Select(l => l.Label)));
        Assert.Contains(form.Deadlines, d => d.Value == form.Deadline);
        Assert.True(form.Price > 0m);
    }

    private static PostsViewModel CheckPosts(GameSession session)
    {
        PostsViewModel posts = PostsView.Build(session);
        AssertFilled(posts.Title, posts.NewPostLabel, posts.PostsTitle, posts.PostsEmpty, posts.HagglesTitle, posts.HagglesEmpty);
        foreach (PostLine line in posts.Posts.Concat(posts.Haggles))
        {
            AssertFilled(line.Head, line.Detail, line.Flag ?? string.Empty);
        }

        Assert.Equal(session.PlayerOpenPosts.Count(), posts.Posts.Count);
        return posts;
    }

    private static void CheckComposer(GameSession session)
    {
        foreach (PostKind kind in System.Enum.GetValues<PostKind>())
        {
            ComposerViewModel composer = ComposerView.Build(session, ComposerView.Default(session, kind));
            AssertFilled(composer.Title, composer.Hint, composer.KindNote, composer.Reference, composer.Stored ?? string.Empty, composer.TermsTitle,
                composer.Commitment, composer.Blocked ?? string.Empty, composer.PostLabel, composer.CloseLabel);
            AssertFilled(composer.Terms);
            Assert.Equal(composer.CanPost, composer.Blocked is null);
        }
    }

    private static LedgerViewModel CheckLedger(GameSession session)
    {
        LedgerViewModel ledger = LedgerView.Build(session);
        AssertFilled(ledger.Title, ledger.PossessionsTitle, ledger.PossessionsEmpty, ledger.ContractsTitle, ledger.ContractsEmpty);
        foreach (LedgerLine line in ledger.Money.Concat(ledger.Possessions).Concat(ledger.Contracts))
        {
            AssertFilled(line.Label, line.Detail, line.Value, line.Action?.Label ?? string.Empty);
        }

        Assert.Equal(session.PlayerActiveContracts.Count() + session.PlayerOpenPosts.Count(), ledger.Contracts.Count);
        return ledger;
    }

    private static void CheckShips(GameSession session)
    {
        foreach (MapShip ship in MapView.Ships(session, session.Clock))
        {
            AssertFilled(ship.Tip);
            Assert.InRange(ship.X, 0, MapView.Width);
            Assert.InRange(ship.Y, 0, MapView.Height);
        }

        Assert.Single(MapView.Ships(session, session.Clock), s => s.Yours);
    }

    private static void AssertFilled(params string[] texts) => AssertFilled((IEnumerable<string>)texts);

    private static void AssertFilled(IEnumerable<string> texts)
    {
        foreach (string text in texts)
        {
            Assert.DoesNotContain("{", text, System.StringComparison.Ordinal);
            Assert.DoesNotContain("}", text, System.StringComparison.Ordinal);
        }
    }
}
