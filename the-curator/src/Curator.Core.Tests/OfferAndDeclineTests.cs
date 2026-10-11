using Curator.Core.Commands;
using Curator.Core.Content;
using Curator.Core.Events;

namespace Curator.Core.Tests;

public sealed class OfferAndDeclineTests
{
    [Fact]
    public void ASameTopicOfferIsTakenAndEarnsTrust()
    {
        var session = PatienceAndTrustTests.Elara();

        session.Do(new OfferBook("hearth-and-kettle"));

        var lent = session.Last<BookLent>();
        Assert.True(lent.AsAlternative);
        Assert.Equal(2, lent.Fee);
        var trust = session.Last<TrustChanged>();
        Assert.Equal(("alternativeAccepted", 2), (trust.Reason, trust.Trust));
        var visit = session.Views.Visit();
        Assert.Equal(DecisionKind.Alternative, visit.Decision);
        Assert.Equal("Oh. Well — thank you. I'll try it.", visit.ExitLine);
    }

    [Fact]
    public void AnOffTopicOfferIsRefusedAndCostsPatience()
    {
        var session = PatienceAndTrustTests.Elara();

        session.Do(new OfferBook("lanterns"));

        Assert.Equal("That's not quite what I meant.", session.Last<OfferRefused>().Line);
        Assert.Equal(3, session.State.Visit.Patience);
        Assert.True(session.State.InVisit);
        Assert.Equal(["lanterns"], session.Views.Visit().OffersRefused);
    }

    [Fact]
    public void CandidPatronsTakeAnyOffer()
    {
        var source = TestWorld.Source();
        source.Edit("patrons/elara-voss.json", n => n["startTrust"] = 2);
        var session = TestWorld.NewGame(source.Load(), 1, debug: false);
        session.ToDay(2);
        session.Ring("elara-voss-1");

        session.Do(new OfferBook("lanterns"));

        Assert.Equal("lanterns", session.Last<BookLent>().BookId);
    }

    [Fact]
    public void AVisitsOwnRuleOverridesTheBalance()
    {
        var source = TestWorld.Source();
        source.Edit("patrons/elara-voss.json", n => n["visits"]![0]!["alternatives"] = System.Text.Json.Nodes.JsonNode.Parse("""{ "acceptSameTopic": false }"""));
        var session = TestWorld.NewGame(source.Load(), 1, debug: false);
        session.ToDay(2);
        session.Ring("elara-voss-1");

        session.Do(new OfferBook("hearth-and-kettle"));

        Assert.Single(session.All<OfferRefused>());
    }

    [Fact]
    public void AnAcceptedOfferForATopicRequestCountsAsLent()
    {
        var source = TestWorld.Source();
        source.Edit("patrons/osric-penn.json", n => n["startTrust"] = 2);
        var session = TestWorld.NewGame(source.Load(), 1, debug: false);
        session.ToDay(3, PatienceAndTrustTests.DeclineWherePossible);
        session.FinishDayUntil("osric-penn-1");

        session.Do(new OfferBook("hearth-and-kettle"));

        Assert.False(session.Last<BookLent>().AsAlternative);
        Assert.Equal(DecisionKind.Lent, session.Views.Visit().Decision);
        Assert.DoesNotContain(session.All<TrustChanged>(), t => t.Reason == "alternativeAccepted");
        session.Refused(new LendRequested(), RejectionReason.NoVisit);
    }

    [Fact]
    public void TheRequestedBookIsLentNotOffered()
    {
        var session = PatienceAndTrustTests.Elara();

        session.Refused(new OfferBook("household-arts-3"), RejectionReason.OfferIsRequestedBook);
        session.Refused(new OfferBook("common-wards"), RejectionReason.BookNotOnShelf);
        session.Refused(new OfferBook("no-such-book"), RejectionReason.UnknownBook);
    }

    [Fact]
    public void WhenTheRequestedBookIsOutOnlyOffersRemain()
    {
        var session = TestWorld.NewGame();
        session.ToDay(3, PatienceAndTrustTests.DeclineWherePossible);
        session.Ring("nell-hart-1");

        var visit = session.Views.Visit();
        Assert.True(visit.RequestedBookOut);
        Assert.False(visit.CanLendRequested);
        Assert.Equal("That one's out on loan, I'm afraid.", visit.BookOutLine);
        session.Refused(new LendRequested(), RejectionReason.BookNotOnShelf);
        session.Do(new OfferBook("hearth-and-kettle"));
        Assert.True(session.Last<BookLent>().AsAlternative);
    }

    [Fact]
    public void DecliningCostsTrust()
    {
        var session = PatienceAndTrustTests.Elara();

        session.Do(new Decline());

        Assert.Equal(("declined", 0), (session.Last<TrustChanged>().Reason, session.Last<TrustChanged>().Trust));
        Assert.Equal(DecisionKind.Declined, session.Views.Visit().Decision);
        Assert.Equal("I see. Thank you anyway.", session.Views.Visit().ExitLine);
    }

    [Fact]
    public void DecliningWithoutLookingIntoAnythingCostsReputation()
    {
        var session = PatienceAndTrustTests.Elara();

        session.Do(new Decline());

        Assert.Equal("unexplainedDecline", session.Last<ReputationChanged>().Reason);
        Assert.Equal(-1, session.State.Reputation);
    }

    [Fact]
    public void DecliningAfterAQuestionCostsNoReputation()
    {
        var session = PatienceAndTrustTests.Elara();
        session.Do(new AskQuestion("purpose"));

        session.Do(new Decline());

        Assert.Empty(session.All<ReputationChanged>());
    }

    [Fact]
    public void AVisitCanSetItsOwnDeclineTrust()
    {
        var source = TestWorld.Source();
        source.Edit("patrons/elara-voss.json", n => n["visits"]![0]!["decisions"]!["declined"]!["trust"] = 0);
        var session = TestWorld.NewGame(source.Load(), 1, debug: false);
        session.ToDay(2);
        session.Ring("elara-voss-1");

        session.Do(new Decline());

        Assert.Empty(session.All<TrustChanged>());
    }

    [Fact]
    public void DecisionLinesFallBackToGenericLines()
    {
        var session = TestWorld.NewGame();
        session.ToDay(2);
        session.FinishDayUntil("ada-marsh-1");

        session.Do(new LendRequested());

        Assert.Equal("Thank you.", session.Views.Visit().ExitLine);
    }
}
