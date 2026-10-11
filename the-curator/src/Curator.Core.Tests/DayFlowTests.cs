using Curator.Core.Commands;
using Curator.Core.Events;
using Curator.Core.Game;

namespace Curator.Core.Tests;

public sealed class DayFlowTests
{
    [Fact]
    public void ANewGameOpensOnDayOneWithTheSchedulesMana()
    {
        var session = TestWorld.NewGame();

        Assert.Equal(1, session.State.Day);
        Assert.Equal(DayPhase.Morning, session.State.Phase);
        Assert.Equal(1, session.State.Mana);
        Assert.Equal(10, session.State.Money);
        Assert.True(session.Last<ManaGranted>().Fixed);
        var morning = session.Views.Morning();
        Assert.True(morning.Newspaper.Delivered);
        Assert.Equal(2, morning.Newspaper.Flavour.Count);
        Assert.Equal("The library keeps its usual hours.", morning.Newspaper.ToneLine);
        Assert.Single(morning.Letters, l => l.Kind == LetterKind.Tutorial && l.Title == "Rules of the desk");
    }

    [Fact]
    public void ANewGameCanOnlyStartOnce()
    {
        var session = TestWorld.NewGame();

        session.Refused(new NewGame(2), RejectionReason.GameAlreadyStarted);
    }

    [Fact]
    public void NothingHappensBeforeTheGameStarts()
    {
        var session = new GameSession(TestWorld.Content);

        session.Refused(new RingBell(), RejectionReason.GameNotStarted);
        session.Refused(new CloseForTheDay(), RejectionReason.GameNotStarted);
    }

    [Fact]
    public void TheClerksVisitIsForced()
    {
        var session = TestWorld.NewGame();
        session.Ring("board-clerk-1");

        session.Refused(new Decline(), RejectionReason.VisitIsForced);
        Assert.False(session.Views.Visit().CanDecline);
        session.Do(new LendRequested());

        var lent = session.Last<BookLent>();
        Assert.Equal("common-wards", lent.BookId);
        Assert.Equal(6, lent.DueDay);
        Assert.Equal(14, session.State.Money);
        Assert.Equal("The Board thanks you. It will be returned.", session.Views.Visit().ExitLine);
    }

    [Fact]
    public void CommandsThatNeedAPatronAreRefusedWithoutOne()
    {
        var session = TestWorld.NewGame();

        session.Refused(new AskQuestion("purpose"), RejectionReason.NoVisit);
        session.Refused(new ReadThoughts(), RejectionReason.NoVisit);
        session.Refused(new LendRequested(), RejectionReason.NoVisit);
        session.Refused(new OfferBook("hearth-and-kettle"), RejectionReason.NoVisit);
        session.Refused(new Decline(), RejectionReason.NoVisit);
    }

    [Fact]
    public void EachFinishedVisitMovesTheTimeOfDayOn()
    {
        var session = TestWorld.NewGame();
        session.ToDay(2);

        var phases = new List<DayPhase>();
        while (session.Views.Hud().SlotsLeft)
        {
            session.Do(new RingBell());
            session.Settle();
            phases.Add(session.State.Phase);
        }

        Assert.Equal([DayPhase.Midday, DayPhase.Afternoon, DayPhase.Dusk], phases);
    }

    [Fact]
    public void TheBellRingsOnlyWhileSlotsAreLeftAndNobodyIsWaiting()
    {
        var session = TestWorld.NewGame();
        session.Ring("board-clerk-1");

        session.Refused(new RingBell(), RejectionReason.PatronAtCounter);
        session.Refused(new CloseForTheDay(), RejectionReason.PatronAtCounter);
        session.Do(new LendRequested());
        session.Refused(new RingBell(), RejectionReason.NoSlotsLeft);
    }

    [Fact]
    public void ClosingRunsTheEveningAndThenTheNextMorning()
    {
        var session = TestWorld.NewGame();
        session.Ring("board-clerk-1");
        session.Do(new LendRequested());

        var events = session.Do(new CloseForTheDay()).Events;

        var names = events.Select(e => e.GetType().Name).ToList();
        Assert.Equal(["UpkeepPaid", "MoneyChanged", "AttentivenessRecorded", "DayEnded", "PhaseAdvanced", "DayStarted"], names.Take(6));
        Assert.All(events.Take(5), e => Assert.Equal(1, e.Day));
        Assert.Equal(DayPhase.Night, events.OfType<PhaseAdvanced>().First().Phase);
        Assert.Equal(2, session.State.Day);
        Assert.Equal(9, session.State.Money);
        Assert.Equal(7, session.State.Mana);
        Assert.Single(session.Views.Morning().Letters, l => l.Title == "Asking");
    }

    [Fact]
    public void ClosingEarlyLosesTheUnusedSlots()
    {
        var session = TestWorld.NewGame();
        session.ToDay(2);
        session.Ring("elara-voss-1");
        session.Settle();

        session.Do(new CloseForTheDay());

        Assert.Equal(3, session.State.Day);
        Assert.False(session.State.HasMet("wren-hale"));
    }

    [Fact]
    public void TheWeekEndsAfterDaySeven()
    {
        var session = TestWorld.NewGame();
        session.ToDay(7);
        session.FinishDay(s => s.Settle());

        Assert.Equal(DayStatus.WeekOver, session.State.Status);
        Assert.Single(session.All<WeekEnded>());
        Assert.True(session.Views.Hud().WeekOver);
        session.Refused(new RingBell(), RejectionReason.WeekOver);
        session.Refused(new CloseForTheDay(), RejectionReason.WeekOver);
        session.Refused(new Identify("hearth-and-kettle"), RejectionReason.WeekOver);
    }

    [Fact]
    public void DatesFollowTheCalendar()
    {
        var session = TestWorld.NewGame();

        Assert.Equal("3 Seedmonth", session.Views.Hud().Date);
    }
}
