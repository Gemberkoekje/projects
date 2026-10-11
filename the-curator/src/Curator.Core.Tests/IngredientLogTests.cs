using Curator.Core.Commands;
using Curator.Core.Events;

namespace Curator.Core.Tests;

public sealed class IngredientLogTests
{
    [Fact]
    public void TheLogStartsEmpty()
    {
        var session = TestWorld.NewGame();

        Assert.Empty(session.Views.IngredientLog());
    }

    [Fact]
    public void EachIngredientListsThePagesItWasSeenOn()
    {
        var session = TestWorld.NewDebugGame();
        session.Do(new DebugRevealBook("common-wards"));

        var log = session.Views.IngredientLog();

        var thread = Assert.Single(log, e => e.Name == "Red thread");
        Assert.True(thread.Known);
        Assert.Equal([1, 5], thread.Seen.Select(s => s.PageNumber));
        Assert.All(thread.Seen, s => Assert.Equal("Common Wards & Small Mendings", s.BookTitle));
        Assert.False(Assert.Single(log, e => e.Name == "Hufeykrey").Known);
    }

    [Fact]
    public void TheLogIsFiledByNameIgnoringALeadingArticle()
    {
        var session = TestWorld.NewDebugGame();
        session.Do(new DebugRevealBook("common-wards"));

        var names = session.Views.IngredientLog().Select(e => e.Name).ToList();

        Assert.Equal(["Ash", "Beeswax", "Brannowort", "A candle stub", "Chalk", "Honey"], names.Take(6));
    }

    [Fact]
    public void OnlyIdentifiedPagesCount()
    {
        var session = TestWorld.NewDebugGame();
        session.Do(new DebugAddMana(2));
        session.Do(new Identify("hearth-and-kettle"));
        session.Do(new Identify("hearth-and-kettle"));

        var read = session.All<PageIdentified>().Select(p => p.PageId).ToHashSet();
        var book = TestWorld.Content.Book("hearth-and-kettle");
        var expected = book.Pages.Where(p => read.Contains(p.Id)).SelectMany(p => p.Ingredients).Distinct().Count();
        var log = session.Views.IngredientLog();
        Assert.Equal(expected, log.Count);
        Assert.All(log.SelectMany(e => e.Seen), s => Assert.Contains(book.Pages[s.PageNumber - 1].Id, read));
    }
}
