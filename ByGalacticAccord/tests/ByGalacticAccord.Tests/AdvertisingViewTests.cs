using System.Linq;
using ByGalacticAccord.Engine.Actors;
using ByGalacticAccord.Engine.Domain;
using ByGalacticAccord.Engine.Economy;
using ByGalacticAccord.Engine.Simulation;
using ByGalacticAccord.Presentation.Session;
using ByGalacticAccord.Presentation.Views;

namespace ByGalacticAccord.Tests;

/// <summary>
/// The words for showing a post further out (plan S6b, design §7.1, D38): the composer's "Shown at" choices and what they
/// cost, the player's own open post with where it is shown and where else it could be, and the board row the player sees only
/// because its poster advertised it where the player is.
/// </summary>
public sealed class AdvertisingViewTests
{
    [Fact]
    public void The_composer_offers_every_further_system_with_its_fee_and_delay_and_counts_what_is_ticked()
    {
        var session = new GameSession(TestWorld.Quiet(withPlayer: true));
        SimulationContext context = session.Context;
        LocationId verdant = context.Place("Verdant").Id, tycho = context.Place("Tycho Foundry").Id;
        ComposerDraft draft = ComposerView.Default(session, PostKind.Wanted); // 60 food wanted at Meridian, ₵600

        ShowingModel none = ComposerView.Build(session, draft).Showing;
        Assert.Equal(new[] { "Verdant", "Helios Prime", "Tycho Foundry", "Halcyon Refinery", "Rust Belt", "Edge Station" }, none.Options.Select(o => o.Label));
        Assert.Equal("1 jump · ₵0.30 · shown 4h after you pay", none.Options[0].Detail);
        Assert.Equal("3 jumps · ₵4.80 · shown 2d after you pay", none.Options[^1].Detail);
        Assert.Equal("Shown at Meridian Station only.", none.Total);
        Assert.DoesNotContain("Showing it further", ComposerView.Build(session, draft).Commitment, System.StringComparison.Ordinal);

        ComposerViewModel ticked = ComposerView.Build(session, draft with { ShowAt = new[] { verdant, tycho, context.Place("Meridian Station").Id } });
        Assert.Equal(new[] { "Verdant", "Tycho Foundry" }, ticked.Showing.Options.Where(o => o.Chosen).Select(o => o.Label));
        Assert.Equal("Showing it in 2 more systems costs ₵0.60.", ticked.Showing.Total);
        Assert.Contains("Showing it further costs ₵0.60, paid when you post.", ticked.Commitment, System.StringComparison.Ordinal);

        Credits before = session.Player.Credits;
        PostOutcome outcome = session.Post(ComposerView.ToPost(session, draft with { ShowAt = new[] { verdant, tycho } }));
        Assert.True(outcome.Posted);
        Assert.Equal(before.Amount - 0.60m, session.Player.Credits.Amount);
        Assert.Equal(context.CurrentTick + 4, outcome.Post!.ShownFrom(tycho));
    }

    [Fact]
    public void The_players_own_post_says_where_it_is_shown_and_offers_the_rest()
    {
        var session = new GameSession(TestWorld.Quiet(withPlayer: true));
        SimulationContext context = session.Context;
        LocationId verdant = context.Place("Verdant").Id;
        ComposerDraft draft = ComposerView.Default(session, PostKind.Wanted) with { ShowAt = new[] { verdant } };
        Contract post = session.Post(ComposerView.ToPost(session, draft)).Post!;

        ContractViewModel view = ContractView.Build(session, post.Id);
        Assert.Equal($"Shown at Meridian Station; Verdant from {Presentation.Text.Format.Date(session.Now + 4)}.", view.Shown);
        Assert.NotNull(view.Advertise);
        Assert.DoesNotContain(view.Advertise!.Options, o => o.Label is "Verdant" or "Meridian Station");
        Assert.Equal("Showing it in one more system costs ₵1.20.", view.Advertise.Choose(new[] { context.Place("Rust Belt").Id }).Total);

        (AdBlock block, Credits fee) = session.Advertise(post.Id, new[] { context.Place("Rust Belt").Id });
        Assert.Equal((AdBlock.None, 1.20m), (block, fee.Amount));
        Assert.Equal("Paid ₵1.20 to show it at Rust Belt. It appears there after the delay; the actors there that can answer it hear of it then.",
            AdView.OutcomeText(session, post, new[] { context.Place("Rust Belt").Id }, block, fee));
        Assert.Equal("Choose where else to show it.", AdView.OutcomeText(session, post, new LocationId[0], AdBlock.NothingChosen, Credits.Zero));

        // Someone else's post offers nothing to buy.
        SaleContract theirs = context.PostWanted(context.Actor("Meridian Habs"), GoodType.Fuel, 20m, 400m);
        Assert.Null(ContractView.Build(session, theirs.Id).Advertise);
        Assert.Equal("Shown at Meridian Station.", ContractView.Build(session, theirs.Id).Shown);
    }

    [Fact]
    public void A_board_row_seen_only_where_it_was_advertised_says_so()
    {
        var session = new GameSession(TestWorld.Quiet(withPlayer: true));
        SimulationContext context = session.Context;
        SaleContract here = context.PostWanted(context.Actor("Meridian Habs"), GoodType.Fuel, 20m, 400m);
        SaleContract away = context.PostWanted(context.Actor("Verdant Township"), GoodType.Parts, 20m, 700m);
        Assert.DoesNotContain(BoardView.Build(session).Rows, r => r.Id == away.Id);

        context.ShowAJumpFurther(away); // shown at Meridian, where the player is
        BoardViewModel board = BoardView.Build(session);
        BoardRow row = board.Rows.Single(r => r.Id == away.Id);
        Assert.Equal(("Advertised at Meridian Station", true), (row.Seen, row.Live));
        Assert.Null(board.Rows.Single(r => r.Id == here.Id).Seen);
        Assert.Null(ContractView.Build(session, away.Id).Known); // live, so no caution
    }
}
