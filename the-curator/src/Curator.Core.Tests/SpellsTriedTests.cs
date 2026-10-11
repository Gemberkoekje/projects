using Curator.Core.Commands;
using Curator.Core.Content;
using Curator.Core.Events;
using Curator.Core.Game;
using Curator.Core.Rules;

namespace Curator.Core.Tests;

/// <summary>Which spells a patron tries from a book, and keeping a book that covers their next visit.</summary>
public sealed class SpellsTriedTests
{
    private static readonly VisitGoal Sleep = new() { Tags = ["sleep"], MinPower = 1 };

    private static readonly Lazy<ContentSet> RealContent = new(() => ContentLoader.Load(new DirectoryContentSource(RepoPaths.GameContent)));

    [Fact]
    public void OneSpellIsDrawnAmongThoseThatMeetTheNeed()
    {
        Page[] pages = [Page("a", "sleep"), Page("b", "memory-aid"), Page("c", "sleep")];

        Assert.Equal(["a"], Tried(pages, [], Cause.None, need: 0));
        Assert.Equal(["c"], Tried(pages, [], Cause.None, need: 1));
    }

    [Fact]
    public void ATemptingSpellIsTriedToo()
    {
        Page[] pages = [Page("a", "sleep"), Page("deep", "sleep-deep")];
        Temptation[] tempt = [new() { Tag = "sleep-deep", Use = TemptationUse.Accident }];

        Assert.Equal(["a", "deep"], Tried(pages, tempt, Cause.Accident, need: 0));
    }

    [Fact]
    public void TheTemptingSpellFollowsTheCause()
    {
        Page[] pages = [Page("botch", "sleep-deep"), Page("misuse", "forgery")];
        Temptation[] tempt = [new() { Tag = "sleep-deep", Use = TemptationUse.Accident }, new() { Tag = "forgery", Use = TemptationUse.Misuse }];

        Assert.Equal(["misuse"], Tried(pages, tempt, Cause.Misuse, need: 0));
    }

    [Fact]
    public void ASpellThatMeetsTheNeedAndTemptsIsTriedOnce()
    {
        Page[] pages = [Page("fire", "fire-destructive")];
        Temptation[] tempt = [new() { Tag = "fire-destructive", Use = TemptationUse.Accident }];

        Assert.Equal(["fire"], OutcomeRules.SpellsTried(pages, new VisitGoal { Tags = ["fire-destructive"], MinPower = 1 }, tempt, Cause.Accident, _ => 0, _ => 0));
    }

    [Fact]
    public void ABookWithNothingForThemStillGetsOneSpellTried()
    {
        Page[] pages = [Page("a", "warmth"), Page("b", "cooking")];

        Assert.Equal(["b"], Tried(pages, [], Cause.None, need: 1));
    }

    [Fact]
    public void LendingRecordsTheSpellsTried()
    {
        var session = TestWorld.NewGame(RealContent.Value, 1, debug: false);
        session.ToDay(2);
        session.FinishDayUntil("jory-fenn-1");

        session.Do(new LendRequested());

        var tried = session.Last<SpellsTried>();
        Assert.Equal("patient-mind", tried.BookId);
        Assert.Equal(2, tried.PageIds.Count);
        Assert.Contains(tried.PageIds[0], new[] { "patient-mind-1", "patient-mind-2", "patient-mind-3", "patient-mind-5" });
        Assert.Equal("patient-mind-4", tried.PageIds[1]);
        Assert.Equal(OutcomeCategory.Mixed, session.Last<OutcomeResolved>().Category);
    }

    [Fact]
    public void ABookThatCoversTheNextVisitIsKeptAndThatVisitSkipped()
    {
        // The reported bug: Jory was lent the sleep book for his exam, brought it back, then asked for sleep.
        var session = TestWorld.NewGame(RealContent.Value, 1, debug: false);
        session.ToDay(2);
        session.FinishDayUntil("jory-fenn-1");
        session.Do(new OfferBook("sleep-neighbours"));
        Assert.Equal(["sleep-neighbours-2"], session.Last<SpellsTried>().PageIds);
        var loanId = session.Last<BookLent>().LoanId;

        session.ToDay(4);

        var kept = Assert.Single(session.All<LoanKept>());
        Assert.Equal((loanId, "jory-fenn-2-brittle"), (kept.LoanId, kept.VisitId));
        Assert.DoesNotContain(session.All<BookReturned>(), r => r.LoanId == loanId && r.Day <= 4);
        Assert.Contains(2, session.State.Patron("jory-fenn").UsedSteps);
        Assert.Contains("jory-fenn:slept-through", session.State.Flags);
        Assert.Contains("sleep-neighbours-4", session.Last<SpellsTried>().PageIds);
        Assert.Equal(BookStatus.Lent, session.State.Book("sleep-neighbours").Status);

        session.FinishDay(s => s.Settle());
        Assert.DoesNotContain(session.All<VisitStarted>(), v => v.VisitId.StartsWith("jory-fenn-2", StringComparison.Ordinal));
    }

    [Fact]
    public void ABookThatDoesntCoverTheNextVisitComesBack()
    {
        var session = TestWorld.NewGame(RealContent.Value, 1, debug: false);
        session.ToDay(2);
        session.FinishDayUntil("jory-fenn-1");
        session.Do(new LendRequested());
        var loanId = session.Last<BookLent>().LoanId;

        session.ToDay(session.State.Loan(loanId).DueDay);

        Assert.Empty(session.All<LoanKept>());
        Assert.Contains(session.All<BookReturned>(), r => r.LoanId == loanId);
    }

    private static List<string> Tried(Page[] pages, Temptation[] temptations, Cause cause, int need) =>
        [.. OutcomeRules.SpellsTried(pages, Sleep, temptations, cause, _ => need, _ => 0)];

    private static Page Page(string id, string tag) => new()
    {
        Id = id,
        SpellName = id,
        Tags = [tag],
        Power = 1,
        Danger = 0,
        Text = id,
        Ingredients = [],
    };
}
