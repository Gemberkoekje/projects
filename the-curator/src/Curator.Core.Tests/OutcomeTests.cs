using Curator.Core.Commands;
using Curator.Core.Content;
using Curator.Core.Events;
using Curator.Core.Game;
using Curator.Core.Rules;

namespace Curator.Core.Tests;

public sealed class OutcomeTests
{
    private static readonly VisitGoal Preservation = new() { Tags = ["preservation"], MinPower = 1 };

    [Fact]
    public void ABookThatMeetsTheGoalWithNoTemptationIsGood() =>
        Assert.Equal(new OutcomeEvaluation(OutcomeCategory.Good, Cause.None), Evaluate([Page("preservation", 2)], []));

    [Fact]
    public void ABookThatMissesTheGoalIsUnhelpful() =>
        Assert.Equal(new OutcomeEvaluation(OutcomeCategory.Unhelpful, Cause.None), Evaluate([Page("drying", 1)], []));

    [Fact]
    public void PowerBelowTheGoalDoesntMeetIt() =>
        Assert.Equal(OutcomeCategory.Unhelpful, Evaluate([Page("preservation", 1)], [], minPower: 2).Category);

    [Fact]
    public void ATemptingBookThatMissesTheGoalIsHarm() =>
        Assert.Equal(new OutcomeEvaluation(OutcomeCategory.Harm, Cause.Accident), Evaluate([Page("decay", 2)], [Tempt("decay", TemptationUse.Accident)]));

    [Fact]
    public void ATemptingBookThatMeetsTheGoalIsMixed() =>
        Assert.Equal(
            new OutcomeEvaluation(OutcomeCategory.Mixed, Cause.Misuse),
            Evaluate([Page("preservation", 1), Page("decay", 2)], [Tempt("decay", TemptationUse.Misuse)]));

    [Fact]
    public void MisuseOutweighsAccident() =>
        Assert.Equal(
            Cause.Misuse,
            Evaluate([Page("decay", 2), Page("stasis", 3)], [Tempt("decay", TemptationUse.Accident), Tempt("stasis", TemptationUse.Misuse)]).Cause);

    [Fact]
    public void BenignTemptationsAreSafe() =>
        Assert.Equal(OutcomeCategory.Good, Evaluate([Page("preservation", 2)], [Tempt("preservation", TemptationUse.Benign)]).Category);

    [Fact]
    public void OverridesComeFirst()
    {
        var session = TestWorld.NewGame();
        session.Ring("board-clerk-1");
        session.Do(new LendRequested());

        var resolved = session.Last<OutcomeResolved>();
        Assert.Equal("override:board-clerk-1:common-wards", resolved.OutcomeKey);
        Assert.Equal(OutcomeCategory.Good, resolved.Category);
        Assert.Equal((OutcomeChannel.Letter, 6), (resolved.Channel, resolved.SurfaceDay));
    }

    [Fact]
    public void CauseSpecificOutcomesComeBeforeCategoryOutcomes()
    {
        var session = TestWorld.NewGame();
        session.ToDay(2);
        session.FinishDayUntil("wren-hale-1");

        session.Do(new LendRequested());

        var resolved = session.Last<OutcomeResolved>();
        Assert.Equal("visit:wren-hale-1:mixedAccident", resolved.OutcomeKey);
        Assert.Equal((OutcomeCategory.Mixed, Cause.Accident), (resolved.Category, resolved.Cause));
    }

    [Fact]
    public void CategoryOutcomesComeNext()
    {
        var session = PatienceAndTrustTests.Elara();

        session.Do(new LendRequested());

        Assert.Equal("visit:elara-voss-1:good", session.Last<OutcomeResolved>().OutcomeKey);
    }

    [Fact]
    public void GenericOutcomesAreTheLastResort()
    {
        var visit = TestWorld.Content.Visit("ada-marsh-1").Visit with { Outcomes = new Dictionary<string, Outcome>() };

        var choice = OutcomeRules.ForLend(TestWorld.Content, visit, "hearth-and-kettle", new OutcomeEvaluation(OutcomeCategory.Harm, Cause.Misuse), n => n - 1);

        Assert.Equal("generic:harmMisuse:1", choice.Key);
        Assert.Equal(TestWorld.Content.OutcomesGeneric.HarmMisuse[1], choice.Outcome);
    }

    [Fact]
    public void DeclinesAndWalkOutsAreSilentUnlessTheVisitSaysOtherwise()
    {
        var visit = TestWorld.Content.Visit("ada-marsh-1").Visit;

        var choice = OutcomeRules.ForNonLend(visit, OutcomeCategory.Declined);

        Assert.Equal("default:declined", choice.Key);
        Assert.Equal(OutcomeChannel.None, choice.Outcome.Channel);
    }

    [Fact]
    public void PlaceholdersAreFilled() =>
        Assert.Equal("Ada Marsh liked Hearth & Kettle.", OutcomeRules.Fill("{name} liked {book}.", "Ada Marsh", "Hearth & Kettle"));

    [Fact]
    public void AnOutcomesFlagsAreSetAtLending()
    {
        var session = TestWorld.NewGame();
        session.ToDay(2);
        session.FinishDayUntil("wren-hale-1");

        session.Do(new LendRequested());

        Assert.Contains("wren-hale:hayrick", session.State.Flags);
        Assert.False(session.State.Resolutions[^1].Surfaced);
    }

    [Fact]
    public void ReturnOutcomesOpenThePatronsNextVisit()
    {
        var session = TestWorld.NewGame();
        session.ToDay(4);

        session.Ring("elara-voss-2-returning");

        var visit = session.Views.Visit();
        Assert.Equal(["The stain lifted — and the rest held. Thank you. I mean that."], visit.ReturnTexts);
        Assert.Equal("And now a harder question, I'm afraid.", visit.Greeting);
        Assert.Equal(2, session.State.Patron("elara-voss").Trust);
        Assert.Equal(OutcomeChannel.Return, session.Last<OutcomeSurfaced>().Channel);
    }

    [Fact]
    public void GossipIsToldByTheNextVisitorOnOrAfterItsDay()
    {
        var session = TestWorld.NewGame();
        session.ToDay(4, Play.Policy(("wren-hale-1", s => s.Do(new Decline()))));

        session.Ring("elara-voss-2-returning");

        Assert.All(session.All<GossipShared>(), g => Assert.Equal("elara-voss", g.TellerPatronId));
        Assert.Contains("Did you hear? The Hales dug a firebreak by hand.", session.Views.Visit().Gossip);
    }

    [Fact]
    public void PatronsDontGossipAboutThemselves()
    {
        var source = TestWorld.Source();
        source.Edit("patrons/elara-voss.json", n => n["visits"]![0]!["outcomes"]!["declined"] = System.Text.Json.Nodes.JsonNode.Parse("""
            { "channel": "gossip", "delayDays": 2, "text": "Did you hear? The Voss woman was turned away." }
            """));
        var session = TestWorld.NewGame(source.Load(), 1, debug: false);
        session.ToDay(4, Play.Policy(("elara-voss-1", s => s.Do(new Decline()))));

        session.Ring("elara-voss-2-guarded");
        Assert.DoesNotContain("Did you hear? The Voss woman was turned away.", session.Views.Visit().Gossip);
        session.Settle();
        session.Ring("wren-hale-2-subdued");

        Assert.Contains("Did you hear? The Voss woman was turned away.", session.Views.Visit().Gossip);
    }

    [Fact]
    public void NewspaperOutcomesSurfaceInTheMorningPaperAndMoveReputation()
    {
        var session = TestWorld.NewGame();
        session.ToDay(6, Play.Policy(("ada-marsh-1", s => s.Do(new Decline())), ("wren-hale-1", s => s.Do(new OfferBook("hearth-and-kettle")))));

        var headline = Assert.Single(session.Views.Morning().Newspaper.Headlines);
        Assert.Equal("Lower Hollis firebreak holds", headline.Headline);
        Assert.Contains(session.All<ReputationChanged>(), r => r.Reason == "outcome" && r.Delta == 1);
    }

    [Fact]
    public void AReturnOutcomeBecomesALetterWhenThePatronsSlotPassesWithoutThem()
    {
        var source = TestWorld.Source();
        source.Edit("patrons/wren-hale.json", n => n["visits"]![2]!["when"] = System.Text.Json.Nodes.JsonNode.Parse("""
            { "previousDecisionIn": ["lent"], "flagsAll": ["wren-hale:never"] }
            """));
        var session = TestWorld.NewGame(source.Load(), 1, debug: false);
        session.ToDay(5);

        Assert.Single(session.All<OutcomeRedirected>());
        var letter = Assert.Single(session.Views.Morning().Letters, l => l.Kind == LetterKind.Outcome);
        Assert.Equal("Wren Hale", letter.From);
        Assert.Equal("A hayrick burned. My hands are singed.", letter.Text);
    }

    [Fact]
    public void AReturnOutcomeBecomesALetterWhenThePatronHasNoSlotLeft()
    {
        var source = TestWorld.Source();
        source.Edit("patrons/wren-hale.json", n => n["visits"]![1]!["outcomes"]!["good"] = System.Text.Json.Nodes.JsonNode.Parse("""
            { "channel": "return", "text": "The firebreak held." }
            """));
        var session = TestWorld.NewGame(source.Load(), 1, debug: false);
        session.ToDay(4, Play.Policy(("ada-marsh-1", s => s.Do(new Decline())), ("wren-hale-1", s => s.Do(new OfferBook("hearth-and-kettle")))));
        session.FinishDayUntil("wren-hale-2-candid");
        session.Do(new LendRequested());

        var redirect = session.Last<OutcomeRedirected>();
        Assert.Equal((OutcomeChannel.Letter, 7), (redirect.Channel, redirect.SurfaceDay));
        session.ToDay(7);
        Assert.Contains(session.Views.Morning().Letters, l => l.Text == "The firebreak held.");
    }

    [Fact]
    public void SilentOutcomesAreNeverHeard()
    {
        var session = TestWorld.NewGame();
        session.ToDay(7);
        session.FinishDay(s => s.Settle());

        var silent = session.State.Resolutions.Where(r => r.LoanId.Length > 0 && r.Channel == OutcomeChannel.None).ToList();
        Assert.NotEmpty(silent);
        Assert.All(silent, r => Assert.False(r.Surfaced));
        Assert.True(session.Views.WeekSummary().NeverHeard >= silent.Count);
    }

    [Fact]
    public void AnOutcomeCanKeepTheBookAway()
    {
        var source = TestWorld.Source();
        source.Edit("patrons/ada-marsh.json", n => n["visits"]![0]!["outcomes"]!["good"]!["bookReturns"] = false);
        var session = TestWorld.NewGame(source.Load(), 1, debug: false);
        session.ToDay(7);

        Assert.Equal(BookStatus.Gone, session.State.Book("hearth-and-kettle").Status);
        Assert.DoesNotContain(session.All<BookReturned>(), r => r.BookId == "hearth-and-kettle");
    }

    [Fact]
    public void AnOutcomeCanChangeTheDueDate()
    {
        var source = TestWorld.Source();
        source.Edit("patrons/ada-marsh.json", n => n["visits"]![0]!["outcomes"]!["good"]!["returnInDays"] = 1);
        var session = TestWorld.NewGame(source.Load(), 1, debug: false);
        session.ToDay(3);

        Assert.Contains(session.Views.Morning().Returns, r => r.BookId == "hearth-and-kettle");
    }

    private static OutcomeEvaluation Evaluate(IReadOnlyList<Page> pages, IReadOnlyList<Temptation> temptations, int minPower = 1) =>
        OutcomeRules.Evaluate(pages, Preservation with { MinPower = minPower }, temptations);

    private static Page Page(string tag, int power) => new()
    {
        Id = $"p-{tag}",
        SpellName = tag,
        Tags = [tag],
        Power = power,
        Danger = 0,
        Text = tag,
        Ingredients = [],
    };

    private static Temptation Tempt(string tag, TemptationUse use) => new() { Tag = tag, Use = use };
}
