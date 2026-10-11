using Curator.Core.Commands;
using Curator.Core.Events;
using Curator.Core.Game;

namespace Curator.Core.Tests;

public sealed class MoneyTests
{
    [Fact]
    public void LendingEarnsTheRarityFeeOrTheBooksOwn()
    {
        var source = TestWorld.Source();
        source.Edit("books/lanterns.json", n => n["fee"] = 9);
        var session = TestWorld.NewGame(source.Load(), 1, debug: false);
        session.ToDay(2);
        session.FinishDayUntil("wren-hale-1");

        session.Do(new LendRequested());

        Assert.Equal(9, session.Last<BookLent>().Fee);
        Assert.Equal(9, session.Views.Catalogue().Cards.Single(c => c.BookId == "lanterns").Fee);
    }

    [Fact]
    public void UpkeepIsPaidEveryEvening()
    {
        var session = TestWorld.NewGame();
        session.ToDay(4, PatienceAndTrustTests.DeclineWherePossible);

        Assert.Equal(3, session.All<UpkeepPaid>().Count());
        Assert.All(session.All<UpkeepPaid>(), u => Assert.Equal(5, u.Amount));
    }

    [Fact]
    public void DebtBringsEscalatingLettersFromTheBoard()
    {
        var session = PoorLibrary();
        session.ToDay(5, PatienceAndTrustTests.DeclineWherePossible);

        var levels = session.All<LetterDelivered>().Where(l => l.Kind == LetterKind.Debt).Select(l => (l.Day, l.DebtLevel)).ToList();
        Assert.Equal([(2, 1), (3, 2), (4, 3), (5, 3)], levels);
        Assert.True(session.State.Money < 0);
    }

    [Fact]
    public void EndingADayOutOfDebtResetsTheLetters()
    {
        var session = PoorLibrary();
        session.ToDay(4, Play.Policy(
            ("elara-voss-1", s => s.Do(new LendRequested())),
            ("ada-marsh-1", s => s.Do(new LendRequested())),
            ("wren-hale-1", s => s.Do(new LendRequested())),
            ("nell-hart-1", s => s.Do(new Decline())),
            ("osric-penn-1", s => s.Do(new Decline()))));

        var levels = session.All<LetterDelivered>().Where(l => l.Kind == LetterKind.Debt).Select(l => (l.Day, l.DebtLevel)).ToList();
        Assert.Equal([(2, 1), (4, 1)], levels);
    }

    [Fact]
    public void TheDaySummaryAddsUpTheDay()
    {
        var session = TestWorld.NewGame();
        session.ToDay(3);

        var summary = session.Views.DaySummary(2);
        Assert.Equal(3, summary.Lent.Count);
        Assert.Equal(2 + 2 + 4, summary.Fees);
        Assert.Equal(5, summary.Upkeep);
        Assert.Equal(9 + 8 - 5, summary.Money);
        Assert.True(summary.HasTomorrow);
        Assert.Equal(session.State.Mana, summary.TomorrowMana);
    }

    private static GameSession PoorLibrary()
    {
        var source = TestWorld.Source();
        source.Edit("balance.json", n => n["money"]!["start"] = 0);
        return TestWorld.NewGame(source.Load(), 1, debug: false);
    }
}
