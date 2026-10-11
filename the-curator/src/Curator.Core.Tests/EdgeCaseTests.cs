using System.Text.Json.Nodes;
using Curator.Core.Commands;
using Curator.Core.Content;
using Curator.Core.Events;
using Curator.Core.Game;
using Curator.Core.Rules;

namespace Curator.Core.Tests;

/// <summary>Edges found by review pass 1, each pinned down by a test.</summary>
public sealed class EdgeCaseTests
{
    [Fact]
    public void AForcedVisitNeverEndsInAWalkOut()
    {
        var session = TestWorld.NewGame();
        session.Ring("board-clerk-1");
        foreach (var question in new[] { "purpose", "knowledge", "source", "experience" })
        {
            session.Do(new AskQuestion(question));
        }

        Assert.True(session.State.InVisit);
        Assert.Empty(session.All<PatronWalkedOut>());
        Assert.Equal(0, session.State.Patron("board-clerk").Trust);
        session.Do(new LendRequested());
    }

    [Fact]
    public void AForcedVisitTakesNoOffers()
    {
        var session = TestWorld.NewGame();
        session.Ring("board-clerk-1");

        session.Refused(new OfferBook("hearth-and-kettle"), RejectionReason.VisitIsForced);
        Assert.False(session.Views.Visit().CanOffer);
        Assert.False(session.Views.Book("hearth-and-kettle").CanOffer);
    }

    [Fact]
    public void AForcedVisitCantBeSkippedByClosingEarly()
    {
        var session = TestWorld.NewGame();

        session.Refused(new CloseForTheDay(), RejectionReason.ForcedVisitWaiting);
        Assert.False(session.Views.Hud().CanClose);
    }

    [Fact]
    public void ClosingEarlyStillTurnsAMissedReturnIntoALetter()
    {
        var session = TestWorld.NewGame();
        session.ToDay(4);
        session.Ring("elara-voss-2-returning");
        session.Settle();

        session.Do(new CloseForTheDay());

        var redirect = Assert.Single(session.All<OutcomeRedirected>());
        Assert.Equal(5, redirect.SurfaceDay);
        Assert.Contains(session.Views.Morning().Letters, l => l.Text == "A hayrick burned. My hands are singed.");
    }

    [Fact]
    public void ACauseSpecificMissFallsBackToTheCategory()
    {
        var visit = TestWorld.Content.Visit("wren-hale-1").Visit;

        var choice = OutcomeRules.ForLend(TestWorld.Content, visit, "lanterns", new OutcomeEvaluation(OutcomeCategory.Harm, Cause.Accident), n => 0);

        Assert.Equal("visit:wren-hale-1:harm", choice.Key);
        Assert.Equal((OutcomeCategory.Harm, Cause.Accident), (choice.Category, choice.Cause));
    }

    [Fact]
    public void AnOverrideKeepsTheEvaluatedCauseOnlyForHarmAndMixed()
    {
        var harmful = TestWorld.Content.Visit("board-clerk-1").Visit with
        {
            Overrides = new Dictionary<string, Outcome>
            {
                ["lanterns"] = new() { Channel = OutcomeChannel.None, Category = OutcomeCategory.Harm },
                ["hearth-and-kettle"] = new() { Channel = OutcomeChannel.None, Category = OutcomeCategory.Good },
            },
        };
        var risky = new OutcomeEvaluation(OutcomeCategory.Mixed, Cause.Misuse);

        Assert.Equal(Cause.Misuse, OutcomeRules.ForLend(TestWorld.Content, harmful, "lanterns", risky, n => 0).Cause);
        Assert.Equal(Cause.None, OutcomeRules.ForLend(TestWorld.Content, harmful, "hearth-and-kettle", risky, n => 0).Cause);
    }

    [Fact]
    public void PublicHarmCostsReputationAndAnOutcomeCanSetItsOwn()
    {
        var source = TestWorld.Source();
        source.Edit("patrons/wren-hale.json", n => n["visits"]![0]!["outcomes"]!["mixedAccident"] = JsonNode.Parse("""
            { "channel": "gossip", "delayDays": 0, "text": "Did you hear? A hayrick burned at Lower Hollis." }
            """));
        source.Edit("patrons/nell-hart.json", n => n["visits"]![0]!["outcomes"]!["unhelpful"]!["reputation"] = 2);
        var session = TestWorld.NewGame(source.Load(), 1, debug: false);
        session.ToDay(5, PolicyLendingWrenAndOfferingNell);

        Assert.Contains(session.All<ReputationChanged>(), r => r.Reason == "outcome" && r.Delta == -2);
        Assert.Contains(session.All<ReputationChanged>(), r => r.Reason == "outcome" && r.Delta == 2);
    }

    [Fact]
    public void ASpecificQuestionCanChangeTrustAndCostItsOwnPatience()
    {
        var source = TestWorld.Source();
        source.Edit("patrons/wren-hale.json", n =>
        {
            var question = n["visits"]![0]!["questions"]![0]!;
            question["trust"] = 1;
            question["patienceCost"] = 0;
        });
        var session = TestWorld.NewGame(source.Load(), 1, debug: false);
        session.ToDay(2);
        session.FinishDayUntil("wren-hale-1");
        session.Do(new ReadThoughts());

        session.Do(new AskQuestion("wren-hale-1-paces"));

        Assert.Equal(4, session.State.Visit.Patience);
        Assert.Equal(("question", 1), (session.Last<TrustChanged>().Reason, session.Last<TrustChanged>().Trust));
    }

    [Fact]
    public void TheStateRemembersTheOutcomeOfEachPatronsLastLend()
    {
        var session = TestWorld.NewGame();
        session.ToDay(3);

        Assert.Equal(OutcomeCategory.Good, session.State.Patron("elara-voss").LastLendCategory);
        Assert.Equal(OutcomeCategory.Mixed, session.State.Patron("wren-hale").LastLendCategory);
        Assert.Equal(DecisionKind.Lent, session.State.Patron("wren-hale").LastDecision);
    }

    [Fact]
    public void GenericLettersFillInTheSender()
    {
        var source = TestWorld.Source();
        source.Edit("outcomes_generic.json", n => n["good"] = JsonNode.Parse("""[ { "channel": "letter", "delayDays": 1, "from": "{name}", "text": "Thanks for {book}." } ]"""));
        source.Edit("patrons/ada-marsh.json", n => n["visits"]![0]!["outcomes"]!.AsObject().Remove("good"));
        var session = TestWorld.NewGame(source.Load(), 1, debug: false);
        session.ToDay(3);

        var letter = Assert.Single(session.Views.Morning().Letters, l => l.Kind == LetterKind.Outcome);
        Assert.Equal(("Ada Marsh", "Thanks for Hearth & Kettle."), (letter.From, letter.Text));
    }

    [Fact]
    public void TwoReturnOutcomesAtOneVisitAreBothHeard()
    {
        GameEvent[] log =
        [
            new GameStarted(1, "v", 10, 0) { Sequence = 0 },
            new DayStarted { Sequence = 1, Day = 4 },
            new VisitStarted("elara-voss-3", "elara-voss", 3, 0, "elara-voss", 2, 4, false, "", "preservation", false) { Sequence = 2, Day = 4 },
            Resolved("outcome-1", "Thank you for the first book.", 3),
            Resolved("outcome-2", "And for the second.", 4),
            new OutcomeSurfaced("outcome-1", OutcomeChannel.Return) { Sequence = 5, Day = 4 },
            new OutcomeSurfaced("outcome-2", OutcomeChannel.Return) { Sequence = 6, Day = 4 },
        ];

        var state = GameState.Replay(log);

        Assert.Equal(["Thank you for the first book.", "And for the second."], state.Visit.ReturnTexts);
    }

    private static OutcomeResolved Resolved(string id, string text, long sequence) =>
        new(id, "elara-voss", "elara-voss-1", "", "", OutcomeCategory.Good, Cause.None, "test", OutcomeChannel.Return, 4, "", "", text, 0, false, 0, "")
        {
            Sequence = sequence,
            Day = 2,
        };

    [Fact]
    public void ARestoredSaveFromAnotherSchemaIsRefused()
    {
        var session = TestWorld.NewGame();
        var save = session.ToSave() with { SchemaVersion = 99 };

        Assert.Throws<SaveIncompatibleException>(() => GameSession.Restore(session.Content, save));
    }

    [Fact]
    public void ARestoredSaveKnowsWhenTheContentChanged()
    {
        var session = TestWorld.NewGame();
        var source = TestWorld.Source();
        source.Edit("books/lanterns.json", n => n["summary"] = "Changed.");

        Assert.False(GameSession.Restore(session.Content, session.ToSave()).ContentChanged);
        Assert.True(GameSession.Restore(source.Load(), session.ToSave()).ContentChanged);
    }

    [Fact]
    public void ASaveThatNoLongerReplaysIsRefused()
    {
        var session = TestWorld.NewGame();
        session.Ring("board-clerk-1");
        var source = TestWorld.Source();
        source.Remove("patrons/board-clerk.json");
        source.Edit("schedule.json", n => n["days"]![0]!["slots"] = new JsonArray("filler"));

        Assert.Throws<SaveIncompatibleException>(() => GameSession.Restore(source.Load(), session.ToSave()));
    }

    [Fact]
    public void TheEmptyVisitIsNeverShared()
    {
        var first = new GameState();
        var second = new GameState();

        Assert.NotSame(first.Visit, second.Visit);
    }

    [Fact]
    public void ChecksMatchWhatHandleDoes()
    {
        var session = TestWorld.NewGame();
        Command[] commands = [new RingBell(), new CloseForTheDay(), new Decline(), new Identify("common-wards"), new NoteThought()];

        foreach (var command in commands)
        {
            var predicted = session.Check(command);
            Assert.Equal(predicted, session.Handle(command).Rejection);
        }
    }

    private static void PolicyLendingWrenAndOfferingNell(GameSession session)
    {
        switch (session.State.Visit.VisitId)
        {
            case "nell-hart-1":
                session.Do(new OfferBook("hearth-and-kettle"));
                break;
            case "ada-marsh-1":
                session.Do(new Decline());
                break;
            default:
                session.Settle();
                break;
        }
    }
}
