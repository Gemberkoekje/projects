using System.Text.Json;
using Curator.Core.Bots;
using Curator.Core.Commands;
using Curator.Core.Game;

namespace Curator.Core.Tests;

/// <summary>BUILD_BRIEF §8.2: rebuilding the state from the log alone gives identical projections.</summary>
public sealed class ReplayTests
{
    private static readonly JsonSerializerOptions Json = new() { WriteIndented = false };

    [Theory]
    [InlineData("lend-all")]
    [InlineData("decline-all")]
    [InlineData("careful")]
    public void ReplayingTheLogRebuildsEveryProjection(string bot)
    {
        var played = BotRunner.Play(TestWorld.Content, BotRunner.Bots[bot](), seed: 11).Session;

        var restored = GameSession.Restore(played.Content, SaveSerializer.Deserialize(SaveSerializer.Serialize(played.ToSave())));

        Assert.Equal(Project(played), Project(restored));
    }

    [Fact]
    public void ReplayingMidDayRebuildsTheVisit()
    {
        var session = TestWorld.NewGame();
        session.ToDay(2);
        session.Ring("elara-voss-1");
        session.Do(new AskQuestion("purpose"));
        session.Do(new ReadThoughts());

        var restored = GameSession.Restore(session.Content, session.ToSave());

        Assert.Equal(Project(session), Project(restored));
    }

    [Fact]
    public void TheSameSeedAndCommandsGiveTheSameEvents()
    {
        var first = BotRunner.Play(TestWorld.Content, new CarefulBot(), seed: 3).Session;
        var second = BotRunner.Play(TestWorld.Content, new CarefulBot(), seed: 3).Session;

        Assert.Equal(SaveSerializer.Serialize(first.ToSave()), SaveSerializer.Serialize(second.ToSave()));
    }

    [Fact]
    public void ReplayedEventsKeepTheirSequence()
    {
        var session = BotRunner.Play(TestWorld.Content, new LendAllBot(), seed: 5).Session;

        Assert.Equal(Enumerable.Range(0, session.Events.Count).Select(i => (long)i), session.Events.Select(e => e.Sequence));
    }

    private static string Project(GameSession session)
    {
        var views = session.Views;
        var all = new List<object>
        {
            views.Hud(),
            views.Visit(),
            views.Morning(),
            views.Catalogue(),
            views.WeekSummary(),
            views.Debug(),
            views.NotebookIndex(),
        };
        all.AddRange(session.Content.Books.Select(b => views.Book(b.Id)));
        all.AddRange(session.Content.Patrons.Select(p => views.Card(p.Id)));
        all.AddRange(session.Content.Patrons.Select(p => views.Notebook(p.Id)));
        all.AddRange(Enumerable.Range(1, session.State.Day).Select(views.DaySummary));
        return string.Join("\n", all.Select(v => JsonSerializer.Serialize(v, v.GetType(), Json)));
    }
}
