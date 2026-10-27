using System.Collections.Generic;
using System.Linq;
using ByGalacticAccord.Engine.Actors;
using ByGalacticAccord.Engine.Domain;
using ByGalacticAccord.Engine.Simulation;
using ByGalacticAccord.Presentation.Session;
using ByGalacticAccord.Presentation.Views;

namespace ByGalacticAccord.Tests;

/// <summary>
/// The views S5 brings (plan S5: "the post composer, 'my posts' with responses, and negotiation threads"), and the check
/// that no view gives away a poster's hidden reservation.
/// </summary>
public sealed class PostingViewTests
{
    [Fact]
    public void The_composer_reads_each_kind_with_its_reference_terms_and_what_it_commits()
    {
        var session = new GameSession(TestWorld.Quiet(withPlayer: true));
        SimulationContext context = session.Context;
        LocationId verdant = context.Place("Verdant").Id;

        ComposerDraft wanted = ComposerView.Default(session, PostKind.Wanted) with { Place = verdant };
        ComposerViewModel view = ComposerView.Build(session, wanted);
        Assert.True(view.CanPost);
        Assert.Null(view.Stored);
        Assert.False(view.HasDestination);
        Assert.Equal(600m, wanted.Price); // 60 food at its base value
        Assert.Contains("base value: ₵10.00 a unit", view.Reference, System.StringComparison.Ordinal);
        Assert.StartsWith("Whoever takes it hands you 60 Food at Verdant", view.Terms[0], System.StringComparison.Ordinal);
        Assert.Contains(view.Terms, t => t.StartsWith("On signing, you lock the price", System.StringComparison.Ordinal));
        Assert.Contains("you lock ₵600 and the 4% tax in escrow", view.Commitment, System.StringComparison.Ordinal);

        // A lot or a haul needs goods in storage there; with none, it says so and can't be posted.
        ComposerViewModel noGoods = ComposerView.Build(session, ComposerView.Default(session, PostKind.Lot));
        Assert.False(noGoods.CanPost);
        Assert.NotNull(noGoods.Blocked);
        context.PutInStorage(session.Player, verdant, context.TakeFromStock(context.Actor("Verdant Agricombine"), GoodType.Food, 50m, reserved: false));

        ComposerDraft lot = ComposerView.Default(session, PostKind.Lot);
        Assert.Equal((verdant, GoodType.Food, 50m), (lot.Place, lot.Good, lot.Quantity));
        ComposerViewModel lotView = ComposerView.Build(session, lot);
        Assert.True(lotView.CanPost);
        Assert.Contains("50 Food free in storage at Verdant", lotView.Stored!, System.StringComparison.Ordinal);
        Assert.StartsWith("Whoever takes it collects your 50 Food at Verdant", lotView.Terms[0], System.StringComparison.Ordinal);

        ComposerDraft haul = ComposerView.Default(session, PostKind.Haul);
        ComposerViewModel haulView = ComposerView.Build(session, haul);
        Assert.True(haulView.CanPost && haulView.HasDestination);
        Assert.NotEqual(haul.Place, haul.Destination);
        Assert.Equal(DecisionEngine.UsualHaulOffer(context, haul.Place, haul.Destination).Amount, haul.Price);
        Assert.Contains("The usual fee for Verdant", haulView.Reference, System.StringComparison.Ordinal);
        Assert.StartsWith("Carried by whoever takes it", haulView.Terms[0], System.StringComparison.Ordinal);
        Assert.Equal("A haul needs a destination other than Verdant.", ComposerView.Build(session, haul with { Destination = haul.Place }).Blocked);

        PostOutcome outcome = session.Post(ComposerView.ToPost(session, lot));
        Assert.True(outcome.Posted);
        Assert.StartsWith("Posted: 50 Food for sale at Verdant", ComposerView.OutcomeText(session, outcome, ComposerView.ToPost(session, lot)), System.StringComparison.Ordinal);
    }

    [Fact]
    public void A_post_and_its_answers_read_as_threads_with_their_tells_and_the_players_turn()
    {
        var session = new GameSession(TestWorld.Quiet(withPlayer: true, answers: true));
        SimulationContext context = session.Context;
        PostOutcome outcome = session.Post(ComposerView.ToPost(session, ComposerView.Default(session, PostKind.Wanted) with { Place = context.Place("Verdant").Id, Price = 300m }));
        Contract post = outcome.Post!;

        ContractViewModel before = ContractView.Build(session, post.Id);
        Assert.Empty(before.Threads);
        Assert.Contains(before.Actions, a => a.Command == ContractCommand.Withdraw && a.Enabled);
        Assert.StartsWith("Your post, open until", before.Status, System.StringComparison.Ordinal);
        Assert.Equal("no answers yet", PostsView.Build(session).Posts.Single().Detail.Split(" · ").Last());

        context.RunTo(context.CurrentTick + Haggling.SlowestReply);

        ContractViewModel after = ContractView.Build(session, post.Id);
        ThreadViewModel farm = after.Threads.Single(t => t.Title.StartsWith("With Verdant Agricombine", System.StringComparison.Ordinal));
        Assert.True(farm.YourTurn);
        Assert.Equal("Answers", after.ThreadsTitle);
        Assert.Equal("Your post", farm.Bids[0].Who);
        Assert.Matches(@"^Replied in \d+h; (readily|reluctant|appears to be at its limit)\.$", farm.Bids[1].Note!);
        Assert.Contains("more than posted", farm.AcceptNote, System.StringComparison.Ordinal);
        Assert.NotNull(farm.Counter);
        Assert.Contains(farm.Counter!.Deadlines, d => d.Label.EndsWith("as offered", System.StringComparison.Ordinal));
        PostLine line = PostsView.Build(session).Posts.Single();
        Assert.Matches(@"^\d+ waiting for you$", line.Flag!);

        // The player counters; the thread says the reply is coming, and when at the latest; the panel offers no second counter.
        Negotiation thread = context.Negotiation(farm.Id);
        Assert.Equal(DealBlock.None, session.Counter(post.Id, thread.Latest.Offer with { Price = Credits.Of(360m) }, farm.Id));
        ThreadViewModel waiting = ContractView.Build(session, post.Id).Threads.Single(t => t.Id == farm.Id);
        Assert.False(waiting.YourTurn);
        Assert.Null(waiting.Counter);
        Assert.StartsWith("Your offer is with Verdant Agricombine. Replies take 1h to 12h of game time", waiting.Status, System.StringComparison.Ordinal);
        Assert.Equal("Counter-offer sent to Verdant Agricombine. Its reply comes while the clock runs.", NegotiationView.CounterText(session, post, DealBlock.None, thread));
    }

    [Fact]
    public void Someone_elses_post_offers_a_counter_and_the_haggle_reads_back_after_it_is_signed()
    {
        var session = new GameSession(TestWorld.Quiet(withPlayer: true));
        SimulationContext context = session.Context;
        HaulContract haul = PlayerActionsTests.FarmHaul(context);
        context.RunTo(context.CurrentTick + 48);

        ContractViewModel view = ContractView.Build(session, haul.Id);
        Assert.NotNull(view.Counter);
        Assert.Null(view.CounterBlocked);
        Assert.Equal(70m, view.Counter!.Price);
        Assert.Equal("Haggle", view.ThreadsTitle);

        Assert.Equal(DealBlock.None, session.Counter(haul.Id, Offer.Of(haul) with { Price = Credits.Of(80m) }));
        context.RunTo(context.Negotiation(context.NegotiationWith(haul.Id, session.Player.Id)!.Id).ReplyDue!.Value);

        ContractViewModel answered = ContractView.Build(session, haul.Id);
        ThreadViewModel thread = answered.Threads.Single();
        Assert.Equal("Verdant Agricombine's post", thread.Bids[0].Who);
        Assert.Equal("You", thread.Bids[1].Who);
        if (haul.Status == ContractStatus.Active)
        {
            Assert.StartsWith("Agreed, and signed at", thread.Status, System.StringComparison.Ordinal);
            Assert.Contains(answered.Why, l => l.Text.StartsWith("Verdant Agricombine accepts your offer", System.StringComparison.Ordinal));
        }
        else
        {
            Assert.True(thread.YourTurn);
        }
    }

    [Fact]
    public void No_view_gives_away_a_posters_hidden_reservation()
    {
        // Two worlds alike in everything but the hidden reservation on one wanted Sale: everything the player can read is the same.
        string[] low = Everything(reservation: 900m);
        string[] high = Everything(reservation: 2400m);

        Assert.NotEmpty(low);
        Assert.Equal(low, high);
    }

    // Every word and figure of the board, the contract, the posts, the ledger and the log, with one wanted Sale up whose hidden
    // reservation is the only thing that differs.
    private static string[] Everything(decimal reservation)
    {
        var session = new GameSession(TestWorld.Quiet(withPlayer: true));
        SimulationContext context = session.Context;
        ActorState habs = context.Actor("Meridian Habs");
        SaleContract wanted = Market.PostSale(
            context, habs, SaleSide.Wanted, GoodType.Food, 60m, habs.Home, Credits.Of(500m), Credits.Of(reservation), context.CurrentTick + 300,
            Holding.StockAt(habs.Home), TermPackage.Standard);
        context.RunTo(context.CurrentTick + 6);

        var text = new List<string>();
        foreach (BoardRow row in BoardView.Build(session).Rows)
        {
            text.Add(row.ToString());
        }

        ContractViewModel c = ContractView.Build(session, wanted.Id);
        text.AddRange(new[] { c.Head, c.Sub, c.TakeHome, c.TakeHomeNote, c.Blocked ?? string.Empty });
        text.AddRange(c.Lines.Select(l => l.ToString()));
        text.AddRange(c.Schedule.Concat(c.Uncertain).Select(l => l.ToString()));
        text.AddRange(c.Terms);
        text.AddRange(c.Why.Select(l => l.ToString()));
        text.AddRange(c.Actions.Select(a => a.ToString()));
        text.Add(c.Counter?.Price.ToString(System.Globalization.CultureInfo.InvariantCulture) ?? string.Empty);
        text.AddRange(LedgerView.Build(session).Contracts.Select(l => l.ToString()));
        text.AddRange(LogView.Build(session).Lines.Select(l => l.ToString()));
        text.AddRange(PostsView.Build(session).Posts.Select(l => l.ToString()));
        return text.ToArray();
    }
}
