using Curator.Core.Commands;
using Curator.Core.Events;

namespace Curator.Core.Tests;

public sealed class ManaTests
{
    [Fact]
    public void ManaNeverGoesBelowZero()
    {
        var session = TestWorld.NewGame();
        session.Do(new Identify("hearth-and-kettle"));

        session.Refused(new Identify("hearth-and-kettle"), RejectionReason.NotEnoughMana);
        Assert.Equal(0, session.State.Mana);
    }

    [Fact]
    public void UnspentManaRollsOverUpToTheCap()
    {
        var session = TestWorld.NewGame();
        session.ToDay(3);

        var grant = session.Last<ManaGranted>();
        Assert.Equal(2, grant.Rollover);
        Assert.Equal(8, grant.Total);
    }

    [Fact]
    public void InvestigatedVisitsEarnBonusManaUpToTheCap()
    {
        var session = TestWorld.NewGame();
        session.ToDay(2);
        session.FinishDay(s =>
        {
            s.Do(new AskQuestion("purpose"));
            s.Settle();
        });

        var grant = session.Last<ManaGranted>();
        Assert.Equal(3, session.Last<AttentivenessRecorded>().InvestigatedVisits);
        Assert.Equal(2, grant.AttentiveBonus);
        Assert.Equal(6 + 2 + 2, grant.Total);
    }

    [Fact]
    public void IdentifyingInTheMorningIsNotAttentiveness()
    {
        var session = TestWorld.NewGame();
        session.ToDay(2);
        session.Do(new Identify("hearth-and-kettle"));
        session.FinishDay(s => s.Settle());

        Assert.Equal(0, session.Last<AttentivenessRecorded>().InvestigatedVisits);
    }

    [Fact]
    public void GoodOutcomesThatSurfaceAddBonusMana()
    {
        var session = TestWorld.NewGame();
        session.ToDay(5);

        var day5 = session.Last<ManaGranted>();
        Assert.Equal(1, day5.GoodBonus);
    }

    [Fact]
    public void TheTotalBonusIsCapped()
    {
        var source = TestWorld.Source();
        source.Edit("balance.json", n => n["mana"]!["totalBonusCap"] = 1);
        var session = TestWorld.NewGame(source.Load(), 1, debug: false);
        session.ToDay(2);
        session.FinishDay(s =>
        {
            s.Do(new AskQuestion("purpose"));
            s.Settle();
        });

        Assert.Equal(1, session.Last<ManaGranted>().AttentiveBonus);
    }

    [Fact]
    public void ReadThoughtsCostsTwoAndIdentifyOne()
    {
        var session = TestWorld.NewGame();
        session.ToDay(2);
        session.Ring("elara-voss-1");

        session.Do(new ReadThoughts());
        session.Do(new Identify("household-arts-3"));

        Assert.Equal([2, 1], session.All<ManaSpent>().Select(m => m.Amount));
        Assert.Equal(4, session.State.Mana);
    }

    [Fact]
    public void DebugManaNeedsADebugBuild()
    {
        var plain = TestWorld.NewGame();
        plain.Refused(new DebugAddMana(5), RejectionReason.DebugDisabled);

        var debug = TestWorld.NewDebugGame();
        debug.Do(new DebugAddMana(5));
        Assert.Equal(6, debug.State.Mana);
        Assert.Single(debug.All<DebugCheatUsed>());
    }

    [Fact]
    public void AFixedGrantIgnoresBonusAndRollover()
    {
        var source = TestWorld.Source();
        source.Edit("schedule.json", n => n["days"]![2]!["startMana"] = 3);
        var session = TestWorld.NewGame(source.Load(), 1, debug: false);
        session.ToDay(3);

        Assert.Equal(3, session.State.Mana);
        Assert.True(session.Last<ManaGranted>().Fixed);
    }
}
