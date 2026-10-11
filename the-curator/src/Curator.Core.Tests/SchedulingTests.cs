using Curator.Core.Commands;
using Curator.Core.Events;

namespace Curator.Core.Tests;

public sealed class SchedulingTests
{
    [Fact]
    public void APatronsSlotUsesTheirLowestOpenStep()
    {
        var session = TestWorld.NewGame();
        session.ToDay(2);

        session.Ring("elara-voss-1");
    }

    [Theory]
    [InlineData("lend", "elara-voss-2-returning")]
    [InlineData("offer", "elara-voss-2-candid")]
    [InlineData("decline", "elara-voss-2-guarded")]
    public void StepVariantsFollowThePreviousDecision(string decision, string expected)
    {
        var session = TestWorld.NewGame();
        session.ToDay(4, Play.Policy(("elara-voss-1", s =>
        {
            Command command = decision switch
            {
                "lend" => new LendRequested(),
                "offer" => new OfferBook("hearth-and-kettle"),
                _ => new Decline(),
            };
            s.Do(command);
        })));

        session.Ring(expected);
    }

    [Fact]
    public void WhenNoVariantQualifiesTheSlotGoesToAFiller()
    {
        var session = TestWorld.NewGame();
        session.ToDay(4, Play.Policy(("elara-voss-1", WalkOut)));

        session.Do(new RingBell());

        Assert.Equal("elara-voss", session.State.Visit.SlotId);
        Assert.NotEqual("elara-voss", session.State.Visit.PatronId);
    }

    [Fact]
    public void FillersComeInFillerOrder()
    {
        var session = TestWorld.NewGame();
        session.ToDay(5);

        var fillers = session.All<VisitStarted>().Where(v => v.SlotId == "filler").Select(v => v.PatronId).ToList();
        Assert.Equal(["ada-marsh", "nell-hart", "osric-penn", "lysander-quill"], fillers);
    }

    [Fact]
    public void WhenNobodyIsLeftTheSlotIsSkipped()
    {
        var session = TestWorld.NewGame();
        session.ToDay(5);

        session.Do(new RingBell());

        Assert.False(session.State.HasVisit);
        Assert.Single(session.All<SlotSkipped>());
        Assert.False(session.Views.Hud().SlotsLeft);
        session.Refused(new RingBell(), RejectionReason.NoSlotsLeft);
    }

    [Fact]
    public void ASteppedVisitWaitsForItsEarliestDay()
    {
        var source = TestWorld.Source();
        source.Edit("patrons/elara-voss.json", n => n["visits"]![2]!["earliestDay"] = 6);
        var session = TestWorld.NewGame(source.Load(), 1, debug: false);
        session.ToDay(4);

        session.Do(new RingBell());
        Assert.NotEqual("elara-voss", session.State.Visit.PatronId);
        session.ToDay(6);
        session.Ring("elara-voss-2-returning");
    }

    [Fact]
    public void ASteppedVisitWaitsForItsSpacing()
    {
        var source = TestWorld.Source();
        source.Edit("patrons/elara-voss.json", n => n["visits"]![2]!["minDaysAfterPrevious"] = 3);
        var session = TestWorld.NewGame(source.Load(), 1, debug: false);
        session.ToDay(4);

        session.Do(new RingBell());

        Assert.NotEqual("elara-voss", session.State.Visit.PatronId);
    }

    [Fact]
    public void AStepThatCameStaysUsed()
    {
        var session = TestWorld.NewGame();
        session.ToDay(6);

        session.Do(new RingBell());

        Assert.NotEqual("elara-voss-2-returning", session.State.Visit.VisitId);
        Assert.DoesNotContain(session.All<VisitStarted>(), v => v.VisitId.StartsWith("elara-voss-1", StringComparison.Ordinal) && v.Day > 2);
    }

    private static void WalkOut(Curator.Core.Game.GameSession session)
    {
        foreach (var question in new[] { "purpose", "knowledge", "source", "experience" })
        {
            session.Do(new AskQuestion(question));
        }

        while (session.State.InVisit)
        {
            session.Do(new OfferBook("lanterns"));
        }
    }
}
