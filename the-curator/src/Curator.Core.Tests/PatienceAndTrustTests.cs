using Curator.Core.Commands;
using Curator.Core.Content;
using Curator.Core.Events;

namespace Curator.Core.Tests;

public sealed class PatienceAndTrustTests
{
    private static readonly string[] StandardQuestions = ["purpose", "knowledge", "source", "experience"];

    [Fact]
    public void AQuestionCostsPatience()
    {
        var session = Elara();

        session.Do(new AskQuestion("purpose"));

        Assert.Equal(3, session.State.Visit.Patience);
        Assert.Equal(1, session.Last<PatienceSpent>().Amount);
    }

    [Fact]
    public void ReadThoughtsCostsNoPatience()
    {
        var session = Elara();

        session.Do(new ReadThoughts());

        Assert.Equal(4, session.State.Visit.Patience);
        Assert.Empty(session.All<PatienceSpent>());
    }

    [Fact]
    public void IdentifyCostsPatienceOnlyWhileAPatronWaits()
    {
        var session = TestWorld.NewGame();
        session.ToDay(2);
        session.Do(new Identify("hearth-and-kettle"));
        Assert.Empty(session.All<PatienceSpent>());

        session.Ring("elara-voss-1");
        session.Do(new Identify("hearth-and-kettle"));

        Assert.Equal(3, session.State.Visit.Patience);
    }

    [Fact]
    public void PastZeroPatienceAnActionCostsTrustInstead()
    {
        var session = Elara();
        foreach (var question in StandardQuestions)
        {
            session.Do(new AskQuestion(question));
        }

        Assert.Equal(0, session.State.Visit.Patience);
        session.Do(new Identify("household-arts-3"));

        var trust = session.Last<TrustChanged>();
        Assert.Equal(("pastPatience", 0), (trust.Reason, trust.Trust));
        Assert.True(session.State.InVisit);
    }

    [Fact]
    public void AtTrustZeroThePatronWalksOutAndTheActionDoesntHappen()
    {
        var session = Elara();
        foreach (var question in StandardQuestions)
        {
            session.Do(new AskQuestion(question));
        }

        session.Do(new Identify("household-arts-3"));
        var manaBefore = session.State.Mana;
        session.Do(new Identify("household-arts-3"));

        Assert.Single(session.All<PatronWalkedOut>());
        Assert.Equal(manaBefore, session.State.Mana);
        var visit = session.Views.Visit();
        Assert.Equal(DecisionKind.WalkedOut, visit.Decision);
        Assert.Equal("I should go. I'm sorry to have kept you.", visit.ExitLine);
        Assert.Contains("elara-voss:gone", session.State.Flags);
        Assert.Equal(-1, session.State.Reputation);
        var outcome = session.Last<OutcomeResolved>();
        Assert.Equal((OutcomeCategory.WalkedOut, OutcomeChannel.None), (outcome.Category, outcome.Channel));
    }

    [Fact]
    public void ARefusedOfferCanMakeAPatronWalkOut()
    {
        var session = TestWorld.NewGame();
        session.ToDay(3, DeclineWherePossible);
        session.FinishDayUntil("osric-penn-1");

        session.Do(new OfferBook("lanterns"));
        session.Do(new OfferBook("hearth-and-kettle"));
        Assert.True(session.State.InVisit);
        session.Do(new OfferBook("household-arts-3"));

        Assert.Single(session.All<PatronWalkedOut>());
        Assert.Equal("I'm sorry. I shouldn't have come.", session.Views.Visit().ExitLine);
        Assert.Contains("osric-penn:gone", session.State.Flags);
    }

    [Fact]
    public void ImpatiencShowsATell()
    {
        var session = Elara();
        session.Do(new AskQuestion("purpose"));
        session.Do(new AskQuestion("knowledge"));
        Assert.False(session.Views.Visit().Impatient);

        session.Do(new AskQuestion("source"));

        Assert.True(session.Views.Visit().Impatient);
        Assert.True(session.Views.Hud().Impatient);
        Assert.Equal("They glance at the door.", session.Views.Visit().Tell);
    }

    [Fact]
    public void TrustIsClampedToZeroThroughThree()
    {
        var session = Elara();

        session.Do(new Decline());

        Assert.Equal(0, session.State.Patron("elara-voss").Trust);
        var source = TestWorld.Source();
        source.Edit("patrons/elara-voss.json", n => n["startTrust"] = 3);
        var candid = TestWorld.NewGame(source.Load(), 1, debug: false);
        candid.ToDay(2);
        candid.Ring("elara-voss-1");
        candid.Do(new OfferBook("hearth-and-kettle"));
        Assert.Equal(3, candid.State.Patron("elara-voss").Trust);
        Assert.Equal(1, candid.Last<TrustChanged>().Delta);
    }

    [Fact]
    public void StrangersStartWarmerWhenTheLibraryIsWellThoughtOf()
    {
        var source = TestWorld.Source();
        source.Edit("balance.json", n => n["reputation"]!["start"] = 3);
        var session = TestWorld.NewGame(source.Load(), 1, debug: false);
        session.ToDay(3, Play.Policy(("ada-marsh-1", s => s.Settle())));

        session.Ring("nell-hart-1");

        Assert.Equal(1, session.State.Patron("nell-hart").Trust);
    }

    internal static void DeclineWherePossible(Curator.Core.Game.GameSession session)
    {
        if (session.Views.Visit().CanDecline)
        {
            session.Do(new Decline());
        }
        else
        {
            session.Settle();
        }
    }

    internal static Curator.Core.Game.GameSession Elara()
    {
        var session = TestWorld.NewGame();
        session.ToDay(2);
        session.Ring("elara-voss-1");
        return session;
    }
}
