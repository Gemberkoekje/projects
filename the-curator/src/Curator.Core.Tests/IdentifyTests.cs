using Curator.Core.Commands;
using Curator.Core.Events;

namespace Curator.Core.Tests;

public sealed class IdentifyTests
{
    [Fact]
    public void IdentifyOnALentBookIsRefused()
    {
        var session = TestWorld.NewGame();
        session.ToDay(2);

        session.Refused(new Identify("common-wards"), RejectionReason.BookNotOnShelf);
        Assert.False(session.Views.Book("common-wards").CanIdentify);
    }

    [Fact]
    public void IdentifyOnAnUnknownBookIsRefused()
    {
        var session = TestWorld.NewGame();

        session.Refused(new Identify("no-such-book"), RejectionReason.UnknownBook);
    }

    [Fact]
    public void IdentifyRevealsPagesAndRepeatsTeachNothing()
    {
        var session = TestWorld.NewDebugGame();
        session.Do(new DebugAddMana(12));
        for (var i = 0; i < 12; i++)
        {
            session.Do(new Identify("hearth-and-kettle"));
        }

        var identified = session.All<PageIdentified>().ToList();
        Assert.Equal(12, identified.Count);
        Assert.Contains(identified, p => p.Repeat);
        Assert.Equal(identified.Select(p => p.PageId).Distinct().Count(), identified.Count(p => !p.Repeat));
        var book = session.Views.Book("hearth-and-kettle");
        Assert.Equal(book.Pages.Count, identified.Count(p => !p.Repeat));
        Assert.Equal(4 - book.Pages.Count, book.UnreadCount);
    }

    [Fact]
    public void IdentifyNeverLandsOnARemovedPage()
    {
        var session = TestWorld.NewDebugGame();
        session.ToDay(6);
        session.Do(new DebugAddMana(30));
        for (var i = 0; i < 30; i++)
        {
            session.Do(new Identify("common-wards"));
        }

        Assert.DoesNotContain(session.All<PageIdentified>(), p => p.PageId == "common-wards-7");
        Assert.Empty(session.All<PageResisted>());
    }

    [Fact]
    public void ResistantBooksSometimesFailOutright()
    {
        var source = TestWorld.Source();
        source.Edit("books/lanterns.json", n => n["identifyResistance"] = 0.5);
        var session = TestWorld.NewGame(source.Load(), 1, debug: true);
        session.Do(new DebugAddMana(40));
        for (var i = 0; i < 40; i++)
        {
            session.Do(new Identify("lanterns"));
        }

        Assert.InRange(session.All<IdentifyFailed>().Count(), 8, 32);
        Assert.NotEmpty(session.All<PageIdentified>());
        Assert.Equal(40, session.All<ManaSpent>().Count());
    }

    [Fact]
    public void TheSameSeedIdentifiesTheSamePages()
    {
        static IEnumerable<string> Pages(long seed)
        {
            var session = TestWorld.NewDebugGame(seed);
            session.Do(new DebugAddMana(5));
            for (var i = 0; i < 5; i++)
            {
                session.Do(new Identify("household-arts-3"));
            }

            return session.All<PageIdentified>().Select(p => p.PageId).ToList();
        }

        Assert.Equal(Pages(7), Pages(7));
    }

    [Fact]
    public void IdentifiedPagesShowInPartialTranslation()
    {
        var session = TestWorld.NewDebugGame();
        session.Do(new DebugRevealBook("household-arts-3"));

        var holdFast = session.Views.Book("household-arts-3").Pages.Single(p => p.PageId == "household-arts-3-5");
        Assert.Equal(5, holdFast.Number);
        Assert.Contains(holdFast.Text, s => s.Kind == Text.SpanKind.Illegible && s.Text == "Never");
        Assert.Contains(holdFast.Ingredients, i => i.Name == "Eye of Newt" && i.Known);
        Assert.Contains(holdFast.Ingredients, i => i.Name == "Hufeykrey" && !i.Known);
    }
}
