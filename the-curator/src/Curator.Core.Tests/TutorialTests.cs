using Curator.Core.Commands;
using Curator.Core.Events;

namespace Curator.Core.Tests;

public sealed class TutorialTests
{
    [Fact]
    public void TheTutorialBookComesBackOnDaySixWithItsLastPageTornOut()
    {
        var session = TestWorld.NewGame();
        session.ToDay(6);

        var morning = session.Views.Morning();
        var returned = Assert.Single(morning.Returns);
        Assert.Equal("common-wards", returned.BookId);
        Assert.Equal(["common-wards-7"], returned.RemovedPageIds);
        Assert.Contains(morning.Letters, l => l.From == "The Board" && l.Text == "Returned, with the Board's thanks.");
        Assert.Equal([7], session.Views.Book("common-wards").RemovedPageNumbers);
        Assert.Equal(6, session.Views.Catalogue().Cards.Single(c => c.BookId == "common-wards").PagesTotal);
    }

    [Fact]
    public void TheClerksLoanSetsItsFlagAtOnce()
    {
        var session = TestWorld.NewGame();
        session.Ring("board-clerk-1");
        session.Do(new LendRequested());

        Assert.Contains("board-clerk:took-common-wards", session.State.Flags);
    }

    [Fact]
    public void TheLastPageWontComeIntoFocus()
    {
        for (var seed = 1; seed < 200; seed++)
        {
            var session = TestWorld.NewGame(seed);
            session.Do(new Identify("common-wards"));
            if (session.Events[^1] is PageResisted resisted)
            {
                Assert.Equal("common-wards-7", resisted.PageId);
                Assert.Equal([7], session.Views.Book("common-wards").ResistedPageNumbers);
                Assert.DoesNotContain(session.Views.Book("common-wards").Pages, p => p.PageId == "common-wards-7");
                return;
            }
        }

        Assert.Fail("No seed landed Identify on the last page.");
    }

    [Fact]
    public void DayOneHasManaForOneIdentifyAndNoReadThoughts()
    {
        var session = TestWorld.NewGame();
        session.Ring("board-clerk-1");

        session.Refused(new ReadThoughts(), RejectionReason.NotEnoughMana);
        session.Do(new Identify("common-wards"));
        Assert.Equal(0, session.State.Mana);
        session.Refused(new Identify("common-wards"), RejectionReason.NotEnoughMana);
    }
}
